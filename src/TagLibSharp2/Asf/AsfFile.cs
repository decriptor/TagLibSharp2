// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents an ASF/WMA audio file.
/// </summary>
public sealed class AsfFile : IDisposable
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
	public AudioProperties AudioProperties { get; }

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	AsfFile (AsfTag tag, AudioProperties audioProperties)
	{
		Tag = tag;
		AudioProperties = audioProperties;
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
			return AsfFileReadResult.Failure ("Data too short for ASF header");

		var offset = 0;

		// Verify header GUID
		var headerGuidResult = AsfGuid.Parse (data[offset..]);
		if (!headerGuidResult.IsSuccess)
			return AsfFileReadResult.Failure ($"Failed to parse header GUID: {headerGuidResult.Error}");

		if (headerGuidResult.Value != AsfGuids.HeaderObject)
			return AsfFileReadResult.Failure ("Invalid ASF header: not an ASF file");

		offset += 16;

		// Read header size
		var headerSize = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		if (headerSize > (ulong)data.Length)
			return AsfFileReadResult.Failure ("Header size exceeds available data");

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
			}

			offset += contentSize;
		}

		// Create tag
		var tag = new AsfTag (contentDesc, extendedDesc);

		// Create audio properties
		var audioProps = CreateAudioProperties (fileProps, streamProps);

		var file = new AsfFile (tag, audioProps);
		file._originalData = data.ToArray ();
		return AsfFileReadResult.Success (file);
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

		var result = Read (readResult.Data!);
		if (result.IsSuccess) {
			result.Value.SourcePath = path;
			result.Value._sourceFileSystem = fs;
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

		var result = Read (readResult.Data!);
		if (result.IsSuccess) {
			result.Value.SourcePath = path;
			result.Value._sourceFileSystem = fs;
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

		// Build new header child objects
		var childObjects = new List<byte[]> ();

		// Copy preserved objects (File Properties, Stream Properties, etc.)
		foreach (var obj in headerInfo.PreservedObjects) {
			childObjects.Add (obj);
		}

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
		if (extendedDesc.Descriptors.Count > 0) {
			childObjects.Add (RenderExtendedContentDescriptionObject (extendedDesc));
		}

		// Build new header
		var newHeader = RenderHeaderObject ([.. childObjects]);

		// Combine header with data object and remainder
		using var ms = new MemoryStream ();
		ms.Write (newHeader, 0, newHeader.Length);
		ms.Write (originalData[headerInfo.HeaderEndOffset..].ToArray (), 0,
			originalData.Length - headerInfo.HeaderEndOffset);
		return ms.ToArray ();
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
	/// Saves the file to the specified path.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The result of the write operation.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (_originalData);
		return AtomicFileWriter.Write (path, rendered, fs);
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
	/// Saves the file to the specified path asynchronously.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The result of the write operation.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (_originalData);
		return await AtomicFileWriter.WriteAsync (path, rendered, fs, cancellationToken).ConfigureAwait (false);
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
	/// Gets the parsed ASF file.
	/// </summary>
	public AsfFile Value { get; }

	/// <summary>
	/// Gets the error message if reading failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets whether reading was successful.
	/// </summary>
	public bool IsSuccess => Error is null;

	AsfFileReadResult (AsfFile value, string? error)
	{
		Value = value;
		Error = error;
	}

	/// <summary>
	/// Creates a successful read result.
	/// </summary>
	public static AsfFileReadResult Success (AsfFile value) => new (value, null);

	/// <summary>
	/// Creates a failed read result.
	/// </summary>
	public static AsfFileReadResult Failure (string error) => new (null!, error);

	/// <inheritdoc/>
	public bool Equals (AsfFileReadResult other) => Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Error);

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
