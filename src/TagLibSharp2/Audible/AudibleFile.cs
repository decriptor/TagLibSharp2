// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;

#pragma warning disable CA1513 // Use ObjectDisposedException.ThrowIf (not available in netstandard)

namespace TagLibSharp2.Audible;

/// <summary>
/// Represents the result of parsing an Audible <c>.aa</c> file.
/// </summary>
public readonly struct AudibleFileReadResult : IEquatable<AudibleFileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public AudibleFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	AudibleFileReadResult (AudibleFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="file">The parsed file.</param>
	/// <returns>A successful result containing the file.</returns>
	public static AudibleFileReadResult Success (AudibleFile file) => new (file, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static AudibleFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (AudibleFileReadResult other) =>
		Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is AudibleFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (AudibleFileReadResult left, AudibleFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (AudibleFileReadResult left, AudibleFileReadResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents an Audible <c>.aa</c> audiobook file: its text dictionary, cover image, and duration.
/// </summary>
/// <remarks>
/// <para>
/// There is no published specification. The layout below follows the prose description at
/// https://wiki.multimedia.cx/index.php?title=Audible_Audio (revision of 14 May 2025), checked byte by
/// byte against three real files (two ACELP.net, one MP3) and against what ffprobe reports for them.
/// All integers are big-endian.
/// </para>
/// <code>
/// offset 0   u32 file size, including this field
/// offset 4   u32 magic 0x57907536
/// offset 8   u32 table-of-contents entry count (12 in every file seen)
/// offset 12  u32 unknown (0 in every file seen), kept as is
/// offset 16  count x { u32 type, u32 offset, u32 size }
/// </code>
/// <para>
/// Chunk types: 0 is the whole file (offset 0, size = file size). 2 is the text dictionary
/// (<see cref="AudibleTag"/>). 6 is the chapter and seek table, whose offsets are relative to the
/// audio. 10 is the encrypted audio. 11 is the cover: u32 image length, u32 file position of the image
/// (the next byte), then the image. The other chunks are opaque and are copied unchanged.
/// </para>
/// <para>
/// Duration is the size of chunk 10 in bits divided by the bit rate the <c>codec</c> key names
/// (<c>acelp85</c> 8500 bit/s, <c>acelp16</c> 16000 bit/s, <c>mp332</c> 32000 bit/s). This matches
/// ffprobe's duration on all three real files to the microsecond.
/// </para>
/// <para>
/// A save rewrites only the dictionary. Every table-of-contents offset at or after the dictionary moves
/// by the change in its size, the whole-file entry and the header file size grow or shrink by the same
/// amount, and the cover's image position moves with it. Every other byte, including the encrypted
/// audio and its DRM keys, is copied unchanged. Decryption is out of scope.
/// </para>
/// </remarks>
public sealed class AudibleFile : IMediaFile
{
	/// <summary>The magic value at offset 4.</summary>
	public const uint Magic = 0x57907536;

	/// <summary>Table-of-contents chunk type of the whole file.</summary>
	internal const uint WholeFileChunk = 0;

	/// <summary>Table-of-contents chunk type of the text dictionary.</summary>
	internal const uint DictionaryChunk = 2;

	/// <summary>Table-of-contents chunk type of the encrypted audio.</summary>
	internal const uint AudioChunk = 10;

	/// <summary>Table-of-contents chunk type of the cover image.</summary>
	internal const uint CoverChunk = 11;

	const int HeaderSize = 16;
	const int TocEntrySize = 12;

	// No real file has more than a dozen entries. The cap bounds the TOC read.
	internal const int MaxTocEntries = 1024;

	// Real dictionaries are a few kilobytes and covers tens of kilobytes. The caps bound a stream read.
	internal const int MaxDictionarySize = 16 * 1024 * 1024;
	internal const int MaxCoverSize = 16 * 1024 * 1024;

	byte[]? _originalData;
	bool _disposed;
	IFileSystem? _sourceFileSystem;
	Layout _layout;

	AudibleFile (Layout layout, AudibleTag tag, AudioProperties properties)
	{
		_layout = layout;
		Tag = tag;
		Properties = properties;
	}

	/// <summary>
	/// Gets the path to the source file, if available.
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <summary>
	/// Gets the text dictionary.
	/// </summary>
	public AudibleTag Tag { get; }

	/// <summary>
	/// Gets the audio properties derived from the codec key and the audio chunk size.
	/// </summary>
	public AudioProperties Properties { get; }

	/// <summary>
	/// Gets the file size the header declares. It is larger than the file when the file is truncated.
	/// </summary>
	public uint DeclaredFileSize => _layout.DeclaredSize;

	/// <inheritdoc />
	Tag? IMediaFile.Tag => Tag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties.Duration > TimeSpan.Zero ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Audible;

	/// <summary>
	/// Checks whether the data starts with an Audible <c>.aa</c> header.
	/// </summary>
	/// <param name="data">The first bytes of the file (at least 8).</param>
	/// <returns>True if the magic value is at offset 4.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data) =>
		data.Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian (data.Slice (4)) == Magic;

	/// <summary>
	/// Parses an Audible <c>.aa</c> file from its bytes.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>The parsed file, or a failure for malformed input.</returns>
	/// <remarks>
	/// A file truncated after the dictionary still reads; its duration comes from the table of contents.
	/// </remarks>
	public static AudibleFileReadResult Read (ReadOnlySpan<byte> data)
	{
		var layout = ParseLayout (data, data.Length, out var error);
		if (layout is null)
			return AudibleFileReadResult.Failure (error!);

		var dictionary = layout.Toc[layout.DictionaryIndex];
		var cover = CoverSpan (layout, data.Length);
		var result = Build (layout,
			data.Slice ((int)dictionary.Offset, (int)dictionary.Size),
			cover.Length > 0 ? data.Slice ((int)cover.Offset, cover.Length) : default,
			cover.Offset);
		if (result.IsSuccess)
			result.File!._originalData = data.ToArray ();
		return result;
	}

	/// <summary>
	/// Reads the header, table of contents, dictionary, and cover. The audio stays on disk.
	/// </summary>
	internal static AudibleFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek) {
			var copied = Read (StreamBytes.ReadToEnd (stream));
			if (sourcePath is not null && copied.IsSuccess)
				copied.File!._originalData = null;
			return WithSource (copied, sourcePath, fileSystem);
		}

		stream.Position = 0;
		var header = StreamBytes.ReadAtMost (stream, HeaderSize);
		var tocBytes = TocBytes (header);
		if (tocBytes > header.Length) {
			stream.Position = 0;
			header = StreamBytes.ReadAtMost (stream, tocBytes);
		}

		var layout = ParseLayout (header, stream.Length, out var error);
		if (layout is null)
			return AudibleFileReadResult.Failure (error!);

		var dictionaryEntry = layout.Toc[layout.DictionaryIndex];
		stream.Position = dictionaryEntry.Offset;
		var dictionary = StreamBytes.ReadAtMost (stream, (int)dictionaryEntry.Size);
		var cover = CoverSpan (layout, stream.Length);
		var coverBytes = Array.Empty<byte> ();
		if (cover.Length > 0) {
			stream.Position = cover.Offset;
			coverBytes = StreamBytes.ReadAtMost (stream, cover.Length);
		}

		return WithSource (Build (layout, dictionary, coverBytes, cover.Offset), sourcePath, fileSystem);
	}

	/// <summary>
	/// Reads the header, table of contents, dictionary, and cover asynchronously. The audio stays on disk.
	/// </summary>
	internal static async Task<AudibleFileReadResult> ReadAsync (
		Stream stream,
		string? sourcePath,
		IFileSystem? fileSystem,
		CancellationToken cancellationToken)
	{
		if (!stream.CanSeek) {
			var copied = Read (await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false));
			if (sourcePath is not null && copied.IsSuccess)
				copied.File!._originalData = null;
			return WithSource (copied, sourcePath, fileSystem);
		}

		stream.Position = 0;
		var header = await StreamBytes.ReadAtMostAsync (stream, HeaderSize, cancellationToken).ConfigureAwait (false);
		var tocBytes = TocBytes (header);
		if (tocBytes > header.Length) {
			stream.Position = 0;
			header = await StreamBytes.ReadAtMostAsync (stream, tocBytes, cancellationToken).ConfigureAwait (false);
		}

		var layout = ParseLayout (header, stream.Length, out var error);
		if (layout is null)
			return AudibleFileReadResult.Failure (error!);

		var dictionaryEntry = layout.Toc[layout.DictionaryIndex];
		stream.Position = dictionaryEntry.Offset;
		var dictionary = await StreamBytes.ReadAtMostAsync (stream, (int)dictionaryEntry.Size, cancellationToken).ConfigureAwait (false);
		var cover = CoverSpan (layout, stream.Length);
		var coverBytes = Array.Empty<byte> ();
		if (cover.Length > 0) {
			stream.Position = cover.Offset;
			coverBytes = await StreamBytes.ReadAtMostAsync (stream, cover.Length, cancellationToken).ConfigureAwait (false);
		}

		return WithSource (Build (layout, dictionary, coverBytes, cover.Offset), sourcePath, fileSystem);
	}

	// Bytes of header plus TOC that a header claims, capped so a hostile count cannot size a read.
	static int TocBytes (ReadOnlySpan<byte> header)
	{
		if (header.Length < HeaderSize)
			return header.Length;
		var count = BinaryPrimitives.ReadUInt32BigEndian (header.Slice (8));
		return count is 0 or > MaxTocEntries ? header.Length : HeaderSize + ((int)count * TocEntrySize);
	}

	static AudibleFileReadResult WithSource (AudibleFileReadResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (result.IsSuccess) {
			result.File!.SourcePath = sourcePath;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Attempts to read an Audible <c>.aa</c> file from its bytes.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out AudibleFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Reads an Audible <c>.aa</c> file from disk. Only the header, dictionary, and cover are read.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	public static AudibleFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (!fs.FileExists (path))
			return AudibleFileReadResult.Failure ($"File not found: {path}");
		try {
			using var stream = fs.OpenRead (path);
			return Read (stream, path, fs);
		} catch (IOException ex) {
			return AudibleFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return AudibleFileReadResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	/// <summary>
	/// Reads an Audible <c>.aa</c> file from disk asynchronously. Only the header, dictionary, and cover are read.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result with the parsed file or an error.</returns>
	public static async Task<AudibleFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (!fs.FileExists (path))
			return AudibleFileReadResult.Failure ($"File not found: {path}");
		try {
			using var stream = fs.OpenRead (path);
			return await ReadAsync (stream, path, fs, cancellationToken).ConfigureAwait (false);
		} catch (IOException ex) {
			return AudibleFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return AudibleFileReadResult.Failure ($"Access denied: {ex.Message}");
		} catch (OperationCanceledException) {
			return AudibleFileReadResult.Failure ("Operation was cancelled");
		}
	}

	readonly record struct TocEntry (uint Type, uint Offset, uint Size)
	{
		public ulong End => (ulong)Offset + Size;
	}

	sealed class Layout (uint declaredSize, TocEntry[] toc, int dictionaryIndex, int tocEnd)
	{
		public uint DeclaredSize { get; } = declaredSize;
		public TocEntry[] Toc { get; } = toc;
		public int DictionaryIndex { get; } = dictionaryIndex;
		public int TocEnd { get; } = tocEnd;

		public bool SameAs (Layout other)
		{
			if (DeclaredSize != other.DeclaredSize || DictionaryIndex != other.DictionaryIndex || Toc.Length != other.Toc.Length)
				return false;
			for (var i = 0; i < Toc.Length; i++) {
				if (Toc[i] != other.Toc[i])
					return false;
			}
			return true;
		}
	}

	// Parses the header and table of contents. data holds at least the header and TOC; fileLength is
	// the real length, which bounds the dictionary.
	static Layout? ParseLayout (ReadOnlySpan<byte> data, long fileLength, out string? error)
	{
		error = null;
		if (data.Length < HeaderSize) {
			error = $"Data too short for an Audible header: {data.Length} bytes, need {HeaderSize}";
			return null;
		}
		if (!IsValidFormat (data)) {
			error = "Not an Audible .aa file: magic 0x57907536 not found at offset 4";
			return null;
		}

		var declaredSize = BinaryPrimitives.ReadUInt32BigEndian (data);
		var count = BinaryPrimitives.ReadUInt32BigEndian (data.Slice (8));
		if (count is 0 or > MaxTocEntries) {
			error = $"Audible table of contents has {count} entries";
			return null;
		}

		var tocEnd = HeaderSize + ((int)count * TocEntrySize);
		if (data.Length < tocEnd) {
			error = $"Audible table of contents is truncated: {count} entries need {tocEnd} bytes, have {data.Length}";
			return null;
		}

		var toc = new TocEntry[count];
		var dictionaryIndex = -1;
		for (var i = 0; i < toc.Length; i++) {
			var at = HeaderSize + (i * TocEntrySize);
			toc[i] = new TocEntry (
				BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at)),
				BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at + 4)),
				BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at + 8)));
			if (dictionaryIndex < 0 && toc[i].Type == DictionaryChunk)
				dictionaryIndex = i;
		}

		if (dictionaryIndex < 0) {
			error = "Audible table of contents has no dictionary chunk (type 2)";
			return null;
		}

		var dictionary = toc[dictionaryIndex];
		if (dictionary.Offset < tocEnd) {
			error = $"Audible dictionary at {dictionary.Offset} overlaps the table of contents";
			return null;
		}
		if (dictionary.End > (ulong)fileLength) {
			error = $"Audible dictionary ends at {dictionary.End}, past the end of the file ({fileLength} bytes)";
			return null;
		}
		if (dictionary.Size > MaxDictionarySize) {
			error = $"Audible dictionary claims {dictionary.Size} bytes";
			return null;
		}

		return new Layout (declaredSize, toc, dictionaryIndex, tocEnd);
	}

	// The cover chunk's bytes that are in the file and small enough to read, or an empty span.
	static (uint Offset, int Length) CoverSpan (Layout layout, long fileLength)
	{
		foreach (var entry in layout.Toc) {
			if (entry.Type != CoverChunk)
				continue;
			if (entry.Size < 8 || entry.Size > MaxCoverSize || entry.End > (ulong)fileLength || entry.Offset < layout.TocEnd)
				return (0, 0);
			return (entry.Offset, (int)entry.Size);
		}
		return (0, 0);
	}

	static AudibleFileReadResult Build (Layout layout, ReadOnlySpan<byte> dictionary, ReadOnlySpan<byte> cover, uint coverOffset)
	{
		var parsed = AudibleTag.Parse (dictionary);
		if (!parsed.IsSuccess)
			return AudibleFileReadResult.Failure (parsed.Error!);

		var tag = parsed.Tag!;
		var picture = ParseCover (cover, coverOffset);
		if (picture is not null)
			tag.SetPictures ([picture]);

		return AudibleFileReadResult.Success (new AudibleFile (layout, tag, ComputeProperties (layout, tag.Codec)));
	}

	// Cover chunk: u32 image length, u32 file position of the image, then the image.
	static AudibleCoverPicture? ParseCover (ReadOnlySpan<byte> chunk, uint chunkOffset)
	{
		if (chunk.Length < 8)
			return null;

		var length = BinaryPrimitives.ReadUInt32BigEndian (chunk);
		var position = BinaryPrimitives.ReadUInt32BigEndian (chunk.Slice (4));
		if (position < (ulong)chunkOffset + 8)
			return null;
		var start = position - chunkOffset;
		if (start > (uint)chunk.Length || length > (uint)chunk.Length - start || length == 0)
			return null;

		return new AudibleCoverPicture (chunk.Slice ((int)start, (int)length).ToArray ());
	}

	static AudioProperties ComputeProperties (Layout layout, string? codec)
	{
		var (bitsPerSecond, sampleRate, channels, name) = codec?.ToUpperInvariant () switch {
			"ACELP85" => (8500, 8000, 1, "ACELP.net"),
			"ACELP16" => (16000, 16000, 1, "ACELP.net"),
			"MP332" => (32000, 22050, 0, "MP3"),
			_ => (0, 0, 0, (string?)codec)
		};

		var audioBytes = 0UL;
		foreach (var entry in layout.Toc) {
			if (entry.Type == AudioChunk) {
				audioBytes = entry.Size;
				break;
			}
		}

		var duration = bitsPerSecond > 0
			? TimeSpan.FromTicks ((long)(audioBytes * 8UL * TimeSpan.TicksPerSecond / (ulong)bitsPerSecond))
			: TimeSpan.Zero;
		return new AudioProperties (duration, bitsPerSecond / 1000, sampleRate, 0, channels, name);
	}

	/// <summary>
	/// Renders the file with the current dictionary.
	/// </summary>
	/// <param name="originalData">The original file bytes, including the audio.</param>
	/// <returns>The new file bytes, or <see cref="BinaryData.Empty"/> when <paramref name="originalData"/> is not the file this object was read from or the new sizes do not fit in 32 bits.</returns>
	/// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
	/// <remarks>
	/// Only the dictionary chunk changes. A dictionary that renders to the same bytes gives back the original bytes.
	/// </remarks>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));

		var layout = ParseLayout (originalData, originalData.Length, out _);
		if (layout is null || !layout.SameAs (_layout))
			return BinaryData.Empty;

		var dictionary = layout.Toc[layout.DictionaryIndex];
		var newDictionary = Tag.Render ();
		var delta = (long)newDictionary.Length - dictionary.Size;
		var dictionaryEnd = dictionary.End;

		var newDeclared = (long)layout.DeclaredSize + delta;
		if (newDeclared is < 0 or > uint.MaxValue)
			return BinaryData.Empty;

		var tailLength = originalData.Length - (int)dictionaryEnd;
		var output = new byte[(long)dictionary.Offset + newDictionary.Length + tailLength];
		originalData.Slice (0, (int)dictionary.Offset).CopyTo (output);
		newDictionary.Span.CopyTo (output.AsSpan ((int)dictionary.Offset));
		originalData.Slice ((int)dictionaryEnd).CopyTo (output.AsSpan ((int)dictionary.Offset + newDictionary.Length));

		BinaryPrimitives.WriteUInt32BigEndian (output, (uint)newDeclared);
		for (var i = 0; i < layout.Toc.Length; i++) {
			var entry = layout.Toc[i];
			long offset = entry.Offset;
			long size = entry.Size;
			if (i == layout.DictionaryIndex) {
				size = newDictionary.Length;
			} else if ((ulong)entry.Offset >= dictionaryEnd) {
				offset += delta;
				if (entry.Type == CoverChunk && !MoveCoverPosition (output, entry, delta))
					return BinaryData.Empty;
			} else if (entry.Offset <= dictionary.Offset && entry.End >= dictionaryEnd) {
				size += delta;
			} else if (entry.End > dictionary.Offset) {
				// Starts before the dictionary and ends inside it: the shift would split it.
				return BinaryData.Empty;
			}

			if (offset is < 0 or > uint.MaxValue || size is < 0 or > uint.MaxValue || offset + size > uint.MaxValue)
				return BinaryData.Empty;

			var at = HeaderSize + (i * TocEntrySize);
			BinaryPrimitives.WriteUInt32BigEndian (output.AsSpan (at + 4), (uint)offset);
			BinaryPrimitives.WriteUInt32BigEndian (output.AsSpan (at + 8), (uint)size);
		}

		return new BinaryData (output);
	}

	// The cover's image position is an absolute file offset into its own chunk, so it moves with the chunk.
	// A chunk past the end of a truncated file is not in the output and has nothing to patch.
	static bool MoveCoverPosition (byte[] output, TocEntry entry, long delta)
	{
		var newOffset = entry.Offset + delta;
		if (entry.Size < 8 || newOffset + 8 > output.Length)
			return true;

		var at = (int)newOffset + 4;
		var position = BinaryPrimitives.ReadUInt32BigEndian (output.AsSpan (at));
		if (position < entry.Offset || position > entry.End)
			return true;

		var moved = position + delta;
		if (moved is < 0 or > uint.MaxValue)
			return false;
		BinaryPrimitives.WriteUInt32BigEndian (output.AsSpan (at), (uint)moved);
		return true;
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file bytes, including the audio.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));

		var rendered = Render (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render Audible file: the original data does not match the file that was read");

		var fs = fileSystem ?? _sourceFileSystem;
		var result = AtomicFileWriter.Write (path, rendered.Span, fs);
		if (result.IsSuccess && Rendered (rendered) is { } layout && SourcePath is not null)
			RefreshLayout (layout, ReadSourceHeader (fs));
		return result;
	}

	// The next save re-reads the source and checks it against _layout. When this save rewrote the
	// source, under any spelling of its path (".", "..", case, a link), the source's header and TOC are
	// now the rendered ones, so the check is made against what is on disk, not against the path string.
	static Layout? Rendered (BinaryData rendered) => ParseLayout (rendered.Span, rendered.Length, out _);

	void RefreshLayout (Layout rendered, (byte[] Header, long Length)? source)
	{
		if (source is not { } read)
			return;
		var onDisk = ParseLayout (read.Header, read.Length, out _);
		if (onDisk is not null && onDisk.SameAs (rendered))
			_layout = onDisk;
	}

	(byte[] Header, long Length)? ReadSourceHeader (IFileSystem? fileSystem)
	{
		try {
			using var stream = (fileSystem ?? DefaultFileSystem.Instance).OpenRead (SourcePath!);
			var header = StreamBytes.ReadAtMost (stream, HeaderSize);
			var tocBytes = TocBytes (header);
			if (tocBytes > header.Length) {
				stream.Position = 0;
				header = StreamBytes.ReadAtMost (stream, tocBytes);
			}
			return (header, stream.Length);
		} catch (IOException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		}
	}

	async Task<(byte[] Header, long Length)?> ReadSourceHeaderAsync (IFileSystem? fileSystem, CancellationToken cancellationToken)
	{
		try {
			using var stream = (fileSystem ?? DefaultFileSystem.Instance).OpenRead (SourcePath!);
			var header = await StreamBytes.ReadAtMostAsync (stream, HeaderSize, cancellationToken).ConfigureAwait (false);
			var tocBytes = TocBytes (header);
			if (tocBytes > header.Length) {
				stream.Position = 0;
				header = await StreamBytes.ReadAtMostAsync (stream, tocBytes, cancellationToken).ConfigureAwait (false);
			}
			return (header, stream.Length);
		} catch (IOException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary>
	/// Saves the file to the specified path asynchronously using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file bytes, including the audio.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));

		cancellationToken.ThrowIfCancellationRequested ();
		var rendered = Render (originalData.Span);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render Audible file: the original data does not match the file that was read");

		var fs = fileSystem ?? _sourceFileSystem;
		var result = await AtomicFileWriter.WriteAsync (path, rendered.Memory, fs, cancellationToken).ConfigureAwait (false);
		if (result.IsSuccess && Rendered (rendered) is { } layout && SourcePath is not null)
			RefreshLayout (layout, await ReadSourceHeaderAsync (fs, cancellationToken).ConfigureAwait (false));
		return result;
	}

	/// <summary>
	/// Saves the file to the specified path, copying the audio from the data or source file it was read from.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));

		var fs = fileSystem ?? _sourceFileSystem;
		if (_originalData is not null)
			return SaveToFile (path, _originalData, fs);
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var read = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!read.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {read.Error}");
		return SaveToFile (path, read.Data!.Value.Span, fs);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");
		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Saves the file to the specified path asynchronously, copying the audio from the data or source file it was read from.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));

		var fs = fileSystem ?? _sourceFileSystem;
		if (_originalData is not null)
			return await SaveToFileAsync (path, _originalData, fs, cancellationToken).ConfigureAwait (false);
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var read = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken).ConfigureAwait (false);
		if (!read.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {read.Error}");
		return await SaveToFileAsync (path, read.Data!.Value, fs, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Saves the file back to its source path asynchronously.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (AudibleFile));
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));
		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Releases the retained original data.
	/// </summary>
	/// <remarks>
	/// After disposal, <see cref="Render"/> and the save methods throw <see cref="ObjectDisposedException"/>.
	/// </remarks>
	public void Dispose ()
	{
		if (_disposed)
			return;

		_originalData = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}

/// <summary>
/// The cover image stored in an Audible <c>.aa</c> cover chunk.
/// </summary>
sealed class AudibleCoverPicture (byte[] data) : Picture
{
	readonly BinaryData _data = new (data);

	public override string MimeType { get; } = DetectMimeType (data);

	public override PictureType PictureType => PictureType.FrontCover;

	public override string Description => string.Empty;

	public override BinaryData PictureData => _data;
}
