// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents an ASF/WMA audio file.
/// </summary>
public sealed class AsfFile : IMediaFile
{
	bool _disposed;
	byte[] _originalData = [];

	/// <summary>
	/// Gets the ASF tag.
	/// </summary>
	public AsfTag Tag { get; }

	/// <summary>
	/// Gets the audio properties.
	/// </summary>
	public AudioProperties Properties { get; }

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	/// <inheritdoc />
	Tag? IMediaFile.Tag => Tag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties.IsValid ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Asf;

	AsfFile (AsfTag tag, AudioProperties audioProperties)
	{
		Tag = tag;
		Properties = audioProperties;
	}

	// ═══════════════════════════════════════════════════════════════
	// Convenience Properties (delegating to Tag)
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Gets or sets the title (delegates to Tag.Title).
	/// </summary>
	public string? Title {
		get => Tag.Title;
		set => Tag.Title = value;
	}

	/// <summary>
	/// Gets or sets the artist (delegates to Tag.Artist).
	/// </summary>
	public string? Artist {
		get => Tag.Artist;
		set => Tag.Artist = value;
	}

	/// <summary>
	/// Gets or sets the album (delegates to Tag.Album).
	/// </summary>
	public string? Album {
		get => Tag.Album;
		set => Tag.Album = value;
	}

	// ═══════════════════════════════════════════════════════════════
	// Parsing
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Reads an ASF file from binary data.
	/// </summary>
	public static AsfFileReadResult Read (ReadOnlySpan<byte> data)
	{
		// Minimum size: Header GUID (16) + Size (8) + Count (4) + Reserved (2) = 30
		if (data.Length < 30)
			return AsfFileReadResult.Failure ("Invalid ASF file: data too short for header");

		var offset = 0;

		// Verify header GUID
		var headerGuidResult = AsfGuid.Parse (data[offset..]);
		if (!headerGuidResult.IsSuccess)
			return AsfFileReadResult.Failure ($"Failed to parse header GUID: {headerGuidResult.Error}");

		if (headerGuidResult.Value != AsfGuids.HeaderObject)
			return AsfFileReadResult.Failure ("Invalid ASF file: not an ASF file");

		offset += 16;

		// Read header size
		var headerSize = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		if (headerSize > (ulong)data.Length)
			return AsfFileReadResult.Failure ("Invalid ASF file: header size exceeds available data");

		// Read child object count
		var childCount = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Skip reserved bytes (2)
		offset += 2;

		// Parse child objects
		AsfContentDescription? contentDesc = null;
		AsfExtendedContentDescription? extendedDesc = null;
		AsfFileProperties? fileProps = null;
		AsfStreamProperties? streamProps = null;
		List<AsfMetadataRecord>? metadata = null;
		List<AsfMetadataRecord>? library = null;
		var extensionUnreadable = false;

		for (uint i = 0; i < childCount && offset < data.Length - 24; i++) {
			// Read object GUID
			var guidResult = AsfGuid.Parse (data[offset..]);
			if (!guidResult.IsSuccess)
				break; // Stop parsing but don't fail completely

			var objectGuid = guidResult.Value;
			offset += 16;

			// Read object size
			if (offset + 8 > data.Length)
				break;

			var objectSize = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
			offset += 8;

			// Content size is object size minus GUID (16) and size field (8)
			// Guard against overflow: objectSize could be huge
			if (objectSize < 24 || objectSize > int.MaxValue)
				break;
			var contentSize = (int)(objectSize - 24);
			// Use subtraction to avoid integer overflow in comparison
			if (contentSize > data.Length - offset)
				break;

			var objectContent = data.Slice (offset, contentSize);

			// Parse known objects
			if (objectGuid == AsfGuids.ContentDescriptionObject) {
				var result = AsfContentDescription.Parse (objectContent);
				if (result.IsSuccess)
					contentDesc = result.Value;
			} else if (objectGuid == AsfGuids.ExtendedContentDescriptionObject) {
				var result = AsfExtendedContentDescription.Parse (objectContent);
				if (result.IsSuccess)
					extendedDesc = result.Value;
			} else if (objectGuid == AsfGuids.FilePropertiesObject) {
				var result = AsfFileProperties.Parse (objectContent);
				if (result.IsSuccess)
					fileProps = result.Value;
			} else if (objectGuid == AsfGuids.StreamPropertiesObject) {
				var result = AsfStreamProperties.Parse (objectContent);
				if (result.IsSuccess && result.Value.IsAudio)
					streamProps = result.Value;
			} else if (objectGuid == AsfGuids.HeaderExtensionObject) {
				ReadHeaderExtension (objectContent, ref metadata, ref library, ref extensionUnreadable);
			}

			offset += contentSize;
		}

		// Create tag
		var tag = new AsfTag (contentDesc, extendedDesc);
		tag.AttachHeaderExtension (metadata, library, extensionUnreadable);

		// Create audio properties
		var audioProps = CreateAudioProperties (fileProps, streamProps);

		var file = new AsfFile (tag, audioProps);
		file._originalData = data.ToArray ();
		return AsfFileReadResult.Success (file);
	}

	// Header Extension Object (ASF spec rev. 01.20.03 §3.4): Reserved Field 1 (GUID), Reserved Field 2 (WORD),
	// Header Extension Data Size (DWORD), then child objects.
	const int HeaderExtensionFixedSize = 16 + 2 + 4;

	// ASF_Reserved_1, the value of Reserved Field 1 (§3.4).
	static readonly AsfGuid HeaderExtensionReserved1 = AsfGuid.Parse (
		new byte[] { 0x11, 0xD2, 0xD3, 0xAB, 0xBA, 0xA9, 0xCF, 0x11, 0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65 }).Value;

	/// <summary>
	/// Reads the Metadata Object (§4.7) and Metadata Library Object (§4.8) out of a Header Extension Object.
	/// A second copy of either, or one whose records run past it, makes the extension unreadable: the tag can
	/// then not promise which values a name holds.
	/// </summary>
	static void ReadHeaderExtension (ReadOnlySpan<byte> content, ref List<AsfMetadataRecord>? metadata,
		ref List<AsfMetadataRecord>? library, ref bool unreadable)
	{
		var children = HeaderExtensionChildren (content, out var complete);
		if (!complete)
			unreadable = true;
		foreach (var (guid, body, _) in children) {
			if (guid != AsfGuids.MetadataObject && guid != AsfGuids.MetadataLibraryObject)
				continue;

			var isLibrary = guid == AsfGuids.MetadataLibraryObject;
			if ((isLibrary ? library : metadata) is not null ||
				!AsfMetadataRecord.TryParseObject (body, out var records)) {
				unreadable = true;
				continue;
			}

			if (isLibrary)
				library = records;
			else
				metadata = records;
		}
	}

	/// <summary>
	/// The child objects of a Header Extension Object's content: GUID, body after GUID and size, and the whole
	/// object. A child that does not fit ends the walk, and <paramref name="complete"/> is then false: a Metadata
	/// Library Object could hide past it.
	/// </summary>
	static List<(AsfGuid Guid, byte[] Body, byte[] Whole)> HeaderExtensionChildren (ReadOnlySpan<byte> content, out bool complete)
	{
		var children = new List<(AsfGuid, byte[], byte[])> ();
		complete = false;
		if (content.Length < HeaderExtensionFixedSize)
			return children;

		var dataSize = BinaryPrimitives.ReadUInt32LittleEndian (content.Slice (18));
		var data = content.Slice (HeaderExtensionFixedSize);
		if (dataSize < (uint)data.Length)
			data = data.Slice (0, (int)dataSize);

		var offset = 0;
		while (data.Length - offset >= 24) {
			var guid = AsfGuid.Parse (data.Slice (offset));
			var size = BinaryPrimitives.ReadUInt64LittleEndian (data.Slice (offset + 16));
			if (!guid.IsSuccess || size < 24 || size > (ulong)(data.Length - offset))
				break;
			var whole = data.Slice (offset, (int)size);
			children.Add ((guid.Value, whole.Slice (24).ToArray (), whole.ToArray ()));
			offset += (int)size;
		}

		complete = offset == data.Length;
		return children;
	}

	/// <summary>
	/// Rebuilds a Header Extension Object with the tag's Metadata and Metadata Library records in place of the
	/// old objects. Other children keep their bytes and order. Null <paramref name="original"/> builds a new
	/// object holding only the records.
	/// </summary>
	static byte[] RenderHeaderExtension (byte[]? original, AsfTag tag)
	{
		var reserved1 = HeaderExtensionReserved1.Render ().ToArray ();
		ushort reserved2 = 6;
		var children = new List<byte[]> ();
		var wroteMetadata = false;
		var wroteLibrary = false;
		if (original is not null && original.Length >= 24 + HeaderExtensionFixedSize) {
			var content = original.AsSpan (24);
			reserved1 = content.Slice (0, 16).ToArray ();
			reserved2 = BinaryPrimitives.ReadUInt16LittleEndian (content.Slice (16));
			foreach (var (guid, _, whole) in HeaderExtensionChildren (content, out _)) {
				if (guid == AsfGuids.MetadataObject) {
					if (!wroteMetadata && tag.MetadataRecords is { } metadata)
						children.Add (AsfMetadataRecord.RenderObject (AsfGuids.MetadataObject, metadata));
					wroteMetadata = true;
				} else if (guid == AsfGuids.MetadataLibraryObject) {
					if (!wroteLibrary && tag.LibraryRecords is { } library)
						children.Add (AsfMetadataRecord.RenderObject (AsfGuids.MetadataLibraryObject, library));
					wroteLibrary = true;
				} else {
					children.Add (whole);
				}
			}
		}
		if (!wroteLibrary && tag.LibraryRecords is { Count: > 0 } added)
			children.Add (AsfMetadataRecord.RenderObject (AsfGuids.MetadataLibraryObject, added));

		var dataSize = 0;
		foreach (var child in children)
			dataSize += child.Length;
		var result = new byte[24 + HeaderExtensionFixedSize + dataSize];
		AsfGuids.HeaderExtensionObject.Render ().Span.CopyTo (result);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (16), (ulong)result.Length);
		reserved1.CopyTo (result, 24);
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (40), reserved2);
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (42), (uint)dataSize);
		var offset = 24 + HeaderExtensionFixedSize;
		foreach (var child in children) {
			child.CopyTo (result, offset);
			offset += child.Length;
		}
		return result;
	}

	/// <summary>
	/// Reads the header object. The data object that follows is not copied.
	/// </summary>
	internal static AsfFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return Finish (Read (StreamBytes.ReadToEnd (stream)), sourcePath, fileSystem, dropPayload: sourcePath is not null);

		stream.Position = 0;
		var head = StreamBytes.ReadAtMost (stream, 30);
		if (head.Length < 30)
			return Finish (Read (head), sourcePath, fileSystem, dropPayload: false);

		var headerSize = BinaryPrimitives.ReadUInt64LittleEndian (head.AsSpan (16));
		if (headerSize < 30 || headerSize > int.MaxValue)
			return AsfFileReadResult.Failure ("Invalid ASF file: header size is out of range");

		var rest = StreamBytes.ReadAtMost (stream, (int)headerSize - head.Length);
		var header = new byte[head.Length + rest.Length];
		head.CopyTo (header, 0);
		rest.CopyTo (header, head.Length);
		var coversFile = headerSize >= (ulong)stream.Length;
		return Finish (Read (header), sourcePath, fileSystem, dropPayload: !coversFile);
	}

	static AsfFileReadResult Finish (AsfFileReadResult result, string? sourcePath, IFileSystem? fileSystem, bool dropPayload)
	{
		if (!result.IsSuccess || result.File is null)
			return result;

		result.File.SourcePath = sourcePath;
		result.File._sourceFileSystem = fileSystem;
		if (dropPayload)
			result.File._originalData = [];
		return result;
	}

	/// <summary>
	/// Attempts to read an ASF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out AsfFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read an ASF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out AsfFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid ASF file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with the ASF Header Object GUID.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 16 bytes for GUID
		if (data.Length < 16)
			return false;

		// Check for ASF Header Object GUID: 30 26 B2 75 8E 66 CF 11 A6 D9 00 AA 00 62 CE 6C
		return data[0] == 0x30 && data[1] == 0x26 && data[2] == 0xB2 && data[3] == 0x75 &&
			   data[4] == 0x8E && data[5] == 0x66 && data[6] == 0xCF && data[7] == 0x11 &&
			   data[8] == 0xA6 && data[9] == 0xD9 && data[10] == 0x00 && data[11] == 0xAA &&
			   data[12] == 0x00 && data[13] == 0x62 && data[14] == 0xCE && data[15] == 0x6C;
	}

	/// <summary>
	/// Reads an ASF file from a file path.
	/// </summary>
	public static AsfFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return AsfFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Reads an ASF file from a file path asynchronously.
	/// </summary>
	public static async Task<AsfFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return AsfFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	static AudioProperties CreateAudioProperties (AsfFileProperties? fileProps, AsfStreamProperties? streamProps)
	{
		var duration = fileProps?.Duration ?? TimeSpan.Zero;
		var bitrate = (int)((fileProps?.MaxBitrate ?? 0) / 1000);
		var sampleRate = (int)(streamProps?.SampleRate ?? 0);
		var channels = streamProps?.Channels ?? 0;
		var bitsPerSample = streamProps?.BitsPerSample ?? 0;
		var codec = streamProps?.CodecName;

		return new AudioProperties (duration, bitrate, sampleRate, bitsPerSample, channels, codec);
	}

	// ═══════════════════════════════════════════════════════════════
	// Rendering
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Renders the ASF file with updated metadata to a byte array.
	/// </summary>
	/// <param name="originalData">The original file data to preserve audio content from.</param>
	/// <returns>The rendered file bytes.</returns>
	public byte[] Render (ReadOnlySpan<byte> originalData)
	{
		// Parse original header to find structure
		var headerInfo = ParseHeaderStructure (originalData);
		if (!headerInfo.IsValid)
			return originalData.ToArray (); // Can't parse, return unchanged

		var newHeader = RenderRebuiltHeader (headerInfo);

		// Combine header with data object and remainder
		using var ms = new MemoryStream ();
		ms.Write (newHeader, 0, newHeader.Length);
		ms.Write (originalData[headerInfo.HeaderEndOffset..].ToArray (), 0,
			originalData.Length - headerInfo.HeaderEndOffset);
		return ms.ToArray ();
	}

	// Same child objects Render writes. The bytes after HeaderEndOffset stay in the source file.
	byte[] RenderRebuiltHeader (HeaderParseInfo headerInfo)
	{
		var childObjects = new List<byte[]> ();

		// Copy preserved objects (File Properties, Stream Properties, etc.). The Header Extension Object is
		// rebuilt only when a write changed its Metadata or Metadata Library records.
		var wroteExtension = false;
		foreach (var obj in headerInfo.PreservedObjects) {
			var isExtension = AsfGuid.Parse (obj).Value == AsfGuids.HeaderExtensionObject;
			if (isExtension && Tag.HeaderExtensionChanged && !wroteExtension) {
				childObjects.Add (RenderHeaderExtension (obj, Tag));
				wroteExtension = true;
				continue;
			}
			childObjects.Add (obj);
		}
		if (!wroteExtension && Tag.HeaderExtensionChanged && Tag.LibraryRecords is { Count: > 0 })
			childObjects.Add (RenderHeaderExtension (null, Tag));

		// Add Content Description if tag has content
		var contentDesc = Tag.ContentDescription;
		if (!string.IsNullOrEmpty (contentDesc.Title) ||
			!string.IsNullOrEmpty (contentDesc.Author) ||
			!string.IsNullOrEmpty (contentDesc.Copyright) ||
			!string.IsNullOrEmpty (contentDesc.Description) ||
			!string.IsNullOrEmpty (contentDesc.Rating)) {
			childObjects.Add (RenderContentDescriptionObject (contentDesc));
		}

		// Add Extended Content Description if tag has extended content
		var extendedDesc = Tag.ExtendedContentDescription;
		if (extendedDesc.Descriptors.Count > 0)
			childObjects.Add (RenderExtendedContentDescriptionObject (extendedDesc));

		return RenderHeaderObject ([.. childObjects]);
	}

	static byte[] RenderHeaderObject (byte[][] childObjects)
	{
		// Calculate content size
		var contentSize = 6; // Object count (4) + Reserved (2)
		foreach (var child in childObjects)
			contentSize += child.Length;

		var totalSize = 24 + contentSize; // GUID (16) + Size (8) + content
		var result = new byte[totalSize];
		var offset = 0;

		// Write Header Object GUID
		AsfGuids.HeaderObject.Render ().ToArray ().CopyTo (result, offset);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)totalSize);
		offset += 8;

		// Write number of header objects
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), (uint)childObjects.Length);
		offset += 4;

		// Write reserved bytes (0x01, 0x02 per spec)
		result[offset++] = 0x01;
		result[offset++] = 0x02;

		// Write child objects
		foreach (var child in childObjects) {
			child.CopyTo (result, offset);
			offset += child.Length;
		}

		return result;
	}

	static byte[] RenderContentDescriptionObject (AsfContentDescription contentDesc)
	{
		var rendered = contentDesc.Render ();
		var contentBytes = rendered.ToArray ();

		// Object = GUID (16) + Size (8) + Content
		var result = new byte[24 + contentBytes.Length];
		AsfGuids.ContentDescriptionObject.Render ().ToArray ().CopyTo (result, 0);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (16), (ulong)result.Length);
		contentBytes.CopyTo (result, 24);
		return result;
	}

	static byte[] RenderExtendedContentDescriptionObject (AsfExtendedContentDescription extendedDesc)
	{
		var rendered = extendedDesc.Render ();
		var contentBytes = rendered.ToArray ();

		// Object = GUID (16) + Size (8) + Content
		var result = new byte[24 + contentBytes.Length];
		AsfGuids.ExtendedContentDescriptionObject.Render ().ToArray ().CopyTo (result, 0);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (16), (ulong)result.Length);
		contentBytes.CopyTo (result, 24);
		return result;
	}

	readonly struct HeaderParseInfo
	{
		public bool IsValid { get; }
		public int HeaderEndOffset { get; }
		public List<byte[]> PreservedObjects { get; }

		public HeaderParseInfo (bool isValid, int headerEndOffset, List<byte[]> preservedObjects)
		{
			IsValid = isValid;
			HeaderEndOffset = headerEndOffset;
			PreservedObjects = preservedObjects;
		}

		public static HeaderParseInfo Invalid () => new (false, 0, []);
	}

	static HeaderParseInfo ParseHeaderStructure (ReadOnlySpan<byte> data)
	{
		var preservedObjects = new List<byte[]> ();

		if (data.Length < 30)
			return HeaderParseInfo.Invalid ();

		// Verify header GUID
		var headerGuidResult = AsfGuid.Parse (data);
		if (!headerGuidResult.IsSuccess || headerGuidResult.Value != AsfGuids.HeaderObject)
			return HeaderParseInfo.Invalid ();

		// Read header size
		var headerSize = BinaryPrimitives.ReadUInt64LittleEndian (data[16..]);
		if (headerSize > (ulong)data.Length)
			return HeaderParseInfo.Invalid ();

		// Read child object count
		var childCount = BinaryPrimitives.ReadUInt32LittleEndian (data[24..]);

		var offset = 30; // After header: GUID (16) + Size (8) + Count (4) + Reserved (2)

		// Parse child objects
		for (uint i = 0; i < childCount && offset < data.Length - 24; i++) {
			var guidResult = AsfGuid.Parse (data[offset..]);
			if (!guidResult.IsSuccess)
				break;

			var objectGuid = guidResult.Value;

			if (offset + 24 > data.Length)
				break;

			var objectSize = BinaryPrimitives.ReadUInt64LittleEndian (data[(offset + 16)..]);
			if (objectSize < 24 || objectSize > int.MaxValue)
				break;

			var objSize = (int)objectSize;
			if (offset + objSize > data.Length)
				break;

			// Preserve all objects except Content Description and Extended Content Description
			// (those will be re-rendered from the Tag)
			if (objectGuid != AsfGuids.ContentDescriptionObject &&
				objectGuid != AsfGuids.ExtendedContentDescriptionObject) {
				preservedObjects.Add (data.Slice (offset, objSize).ToArray ());
			}

			offset += objSize;
		}

		return new HeaderParseInfo (true, (int)headerSize, preservedObjects);
	}

	// ═══════════════════════════════════════════════════════════════
	// File I/O
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Saves the file to the specified path using the provided original data.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="originalData">The original file data containing audio content.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The result of the write operation.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (originalData);
		return AtomicFileWriter.Write (path, rendered, fs);
	}

	/// <summary>
	/// Saves the file to the specified path using internally stored data.
	/// </summary>
	/// <remarks>
	/// When the data object was left on disk, copies that range from <see cref="SourcePath"/>
	/// and writes a rebuilt header. A file read from a byte array still renders from memory.
	/// </remarks>
	/// <param name="path">The file path to save to.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The result of the write operation.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem;
		if (_originalData.Length > 0)
			return SaveToFile (path, _originalData, fs);
		if (SourcePath is not { Length: > 0 })
			return FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead.");

		return SaveFromSource (path, fs);
	}

	// Same bytes as Render: rebuilt header, then every byte after the declared header size.
	// A header that does not parse is copied through unchanged.
	FileWriteResult SaveFromSource (string destinationPath, IFileSystem? fileSystem, CancellationToken cancellationToken = default)
	{
		OpenedFile? source = null;
		byte[]? chunk = null;
		try {
			if (!PathSave.TryOpen (SourcePath!, fileSystem, out source, out var error))
				return error;

			if (cancellationToken.IsCancellationRequested)
				return FileWriteResult.Failure ("Failed to re-read source file: Operation was cancelled");

			chunk = StreamCopy.Rent ();
			var copyBuffer = chunk;
			var opened = source!;
			var fileLength = opened.Length;
			byte[]? headerBytes = null;
			long copyStart = 0;
			var copyWhole = true;
			if (fileLength >= 30) {
				var probe = new byte[30];
				StreamCopy.Read (opened.Stream, probe, 0, 30, copyBuffer);
				if (TryDeclaredHeaderSize (probe, fileLength, out var headerSize)) {
					var header = new byte[headerSize];
					probe.AsSpan (0, 30).CopyTo (header);
					if (headerSize > 30)
						StreamCopy.Read (opened.Stream, header, 30, headerSize - 30, copyBuffer);

					var info = ParseHeaderStructure (header);
					if (info.IsValid) {
						headerBytes = RenderRebuiltHeader (info);
						copyStart = info.HeaderEndOffset;
						copyWhole = false;
					}
				}
			}

			if (copyWhole)
				copyStart = 0;

			if (opened.Stream.Position != copyStart)
				opened.Stream.Position = copyStart;

			var tail = fileLength - copyStart;
			var written = PathSave.Replace (destinationPath, opened, output => {
				if (headerBytes is not null)
					StreamCopy.Write (output, headerBytes, copyBuffer);
				if (tail > 0)
					StreamCopy.Copy (opened.Stream, output, tail, copyBuffer);
			});
			source = null;
			return written;
		} finally {
			source?.Dispose ();
			if (chunk is not null)
				StreamCopy.Return (chunk);
		}
	}

	static bool TryDeclaredHeaderSize (ReadOnlySpan<byte> head, long fileLength, out int headerSize)
	{
		headerSize = 0;
		if (head.Length < 30 || fileLength < 30)
			return false;

		var headerGuid = AsfGuid.Parse (head);
		if (!headerGuid.IsSuccess || headerGuid.Value != AsfGuids.HeaderObject)
			return false;

		var size = BinaryPrimitives.ReadUInt64LittleEndian (head[16..]);
		if (size < 30 || size > (ulong)fileLength || size > int.MaxValue)
			return false;

		headerSize = (int)size;
		return true;
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The result of the write operation.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Saves the file to the specified path asynchronously using the provided original data.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="originalData">The original file data containing audio content.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The result of the write operation.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (originalData.Span);
		return await AtomicFileWriter.WriteAsync (path, rendered, fs, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Saves the file to the specified path asynchronously using internally stored data.
	/// </summary>
	/// <remarks>
	/// When the data object was left on disk, copies that range from <see cref="SourcePath"/>
	/// and writes a rebuilt header. A source that cannot be opened is reported before cancellation.
	/// A token that is already cancelled returns before the destination is replaced.
	/// The copy itself runs to completion.
	/// </remarks>
	/// <param name="path">The file path to save to.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The result of the write operation.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem;
		if (_originalData.Length > 0)
			return SaveToFileAsync (path, _originalData, fs, cancellationToken);
		if (SourcePath is not { Length: > 0 })
			return Task.FromResult (FileWriteResult.Failure ("No source path available. Use SaveToFileAsync(path) instead."));

		return Task.FromResult (SaveFromSource (path, fs, cancellationToken));
	}

	/// <summary>
	/// Saves the file back to its source path asynchronously.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The result of the write operation.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFileAsync(path) instead.");

		return await SaveToFileAsync (SourcePath!, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Releases resources held by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		SourcePath = null;
		_sourceFileSystem = null;
		_originalData = [];
		_disposed = true;
	}
}

/// <summary>
/// Result of reading an ASF file.
/// </summary>
public readonly struct AsfFileReadResult : IEquatable<AsfFileReadResult>
{
	/// <summary>
	/// Gets the parsed ASF file, or null if parsing failed.
	/// </summary>
	public AsfFile? File { get; }

	/// <summary>
	/// Gets the error message if reading failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets whether reading was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	AsfFileReadResult (AsfFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful read result.
	/// </summary>
	public static AsfFileReadResult Success (AsfFile file) => new (file, null);

	/// <summary>
	/// Creates a failed read result.
	/// </summary>
	public static AsfFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (AsfFileReadResult other) => Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfFileReadResult left, AsfFileReadResult right)
		=> left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfFileReadResult left, AsfFileReadResult right)
		=> !left.Equals (right);
}
