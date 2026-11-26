// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;

#pragma warning disable CA2000 // Dispose objects before losing scope - factory method pattern

namespace TagLibSharp2.Aac;

/// <summary>
/// Represents a raw AAC file: an ADTS or ADIF stream with optional ID3v2, APEv2, and ID3v1 tags.
/// </summary>
/// <remarks>
/// <para>
/// File structure:
/// </para>
/// <code>
/// [ID3v2 tag]          optional (https://id3.org/id3v2.4.0-structure)
/// [ADTS frames | ADIF header + raw data blocks]   (ISO/IEC 13818-7 §6.1, §6.2)
/// [APEv2 tag]          optional (https://wiki.hydrogenaud.io/index.php?title=APEv2_specification)
/// [ID3v1 tag]          optional, last 128 bytes (https://id3.org/ID3v1)
/// </code>
/// <para>
/// <see cref="Tag"/> reads ID3v2 first, then APEv2, then ID3v1, and writes every tag the file has.
/// A file with no tag gets an ID3v2 tag on the first write through <see cref="EnsureId3v2Tag"/>.
/// </para>
/// <para>
/// Saving rewrites only the tags. The bytes between the end of the original ID3v2 tag and the start
/// of the trailing tags are copied unchanged, so every ADTS frame stays byte-identical.
/// </para>
/// <para>
/// AAC inside an MP4 container is read by <see cref="Mp4.Mp4File"/>.
/// </para>
/// </remarks>
public sealed class AacFile : IMediaFile
{
	bool _disposed;
	IFileSystem? _sourceFileSystem;

	// The first byte after the leading ID3v2 tag's header, size, and footer: where the read placed the audio start,
	// even when the tag's body did not parse. Render places the tail against it, as the read did.
	long _audioStart;

	// The APEv2 tag that was read, when it had only a footer. Spec-valid, so a save keeps that layout
	// for that tag. Every other tag, including one assigned in its place, is written with a header and
	// a footer, as ffmpeg writes it; some readers (mediainfo) find the tag only through the header.
	ApeTag? _footerOnlyApeTag;

	AacFile ()
	{
	}

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag in front of the stream. Set to null to remove it on save.
	/// </summary>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>
	/// Gets or sets the APEv2 tag after the stream. Set to null to remove it on save.
	/// </summary>
	public ApeTag? ApeTag { get; set; }

	/// <summary>
	/// Gets or sets the ID3v1 tag in the last 128 bytes. Set to null to remove it on save.
	/// </summary>
	public Id3v1Tag? Id3v1Tag { get; set; }

	/// <summary>
	/// Gets the size in bytes of the ID3v2 tag that was read (0 if none).
	/// </summary>
	public int Id3v2Size { get; private set; }

	/// <summary>
	/// Gets the stream properties.
	/// </summary>
	public AacAudioProperties? Properties { get; private set; }

	/// <summary>
	/// Gets the duration. Convenience property that delegates to <see cref="Properties"/>.
	/// </summary>
	public TimeSpan? Duration => Properties?.Duration;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v2 tag.
	/// </summary>
	public bool HasId3v2Tag => Id3v2Tag is not null;

	/// <summary>
	/// Gets a value indicating whether this file has an APEv2 tag.
	/// </summary>
	public bool HasApeTag => ApeTag is not null;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v1 tag.
	/// </summary>
	public bool HasId3v1Tag => Id3v1Tag is not null;

	/// <inheritdoc />
	/// <remarks>
	/// A <see cref="CombinedTag"/> over ID3v2, APEv2, and ID3v1 in that order, or null when the file
	/// has none of them. Reads take the first tag with a value. Writes go to every tag present.
	/// </remarks>
	public Tag? Tag =>
		Id3v2Tag is null && ApeTag is null && Id3v1Tag is null
			? null
			: new CombinedTag (Id3v2Tag, ApeTag, Id3v1Tag);

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is not null ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Aac;

	/// <summary>
	/// Returns the ID3v2 tag, creating an empty one when the file has none.
	/// </summary>
	public Id3v2Tag EnsureId3v2Tag ()
	{
		Id3v2Tag ??= new Id3v2Tag ();
		return Id3v2Tag;
	}

	/// <summary>
	/// Reads an AAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static AacFileReadResult Read (ReadOnlySpan<byte> data)
	{
		var copy = data.ToArray ();
		using var stream = new MemoryStream (copy, writable: false);
		return ReadSeekable (stream, copy);
	}

	/// <summary>
	/// Reads from memory without copying it when it is backed by an array.
	/// </summary>
	internal static AacFileReadResult Read (ReadOnlyMemory<byte> data)
	{
		if (!MemoryMarshal.TryGetArray (data, out var segment) || segment.Array is null)
			return Read (data.Span);

		using var stream = new MemoryStream (segment.Array, segment.Offset, segment.Count, writable: false);
		return ReadSeekable (stream, data);
	}

	/// <summary>
	/// Reads the tags and walks the frame headers of a stream. The audio payload is skipped, not kept.
	/// A stream that cannot seek is copied once.
	/// </summary>
	internal static AacFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		AacFileReadResult result;
		if (stream.CanSeek) {
			result = ReadSeekable (stream);
		} else {
			using var copy = new MemoryStream (StreamBytes.ReadToEnd (stream), writable: false);
			result = ReadSeekable (copy);
		}

		if (result.IsSuccess) {
			result.File!.SourcePath = sourcePath;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	// bytes, when given, are the stream's own bytes in memory: the trailing tags are then located and parsed on them
	// directly instead of being read out of the stream again.
	static AacFileReadResult ReadSeekable (Stream stream, ReadOnlyMemory<byte> bytes = default)
	{
		var length = stream.Length;
		if (length < 4)
			return AacFileReadResult.Failure ("Invalid AAC file: data too short");

		var file = new AacFile ();
		long audioStart = 0;
		stream.Position = 0;
		var prefix = StreamBytes.ReadAtMost (stream, Id3v2Header.HeaderSize);
		var header = Id3v2Header.Read (prefix);
		if (header.IsSuccess) {
			audioStart = Math.Min ((long)header.Header.TotalSize, length);
			stream.Position = 0;
			var tag = Id3v2Tag.Read (StreamBytes.ReadAtMost (stream, (int)audioStart));
			if (tag.IsSuccess && tag.Tag is not null) {
				file.Id3v2Tag = tag.Tag;
				file.Id3v2Size = tag.BytesConsumed;
			}
		}

		file._audioStart = audioStart;
		var tail = bytes.Length == length
			? AacTail.Locate (new AacTailSource (bytes.Span), audioStart, readTags: true)
			: AacTail.Locate (new AacTailSource (stream), audioStart, readTags: true);
		file.TakeTags (tail);
		var audioEnd = tail.AudioEnd;
		if (!AacStream.TryRead (stream, audioStart, audioEnd, out var properties)) {
			file.Dispose ();
			return AacFileReadResult.Failure ("Invalid AAC file: no ADTS frame or ADIF header found");
		}

		file.Properties = properties;
		return AacFileReadResult.Success (file);
	}

	void TakeTags (AacTail tail)
	{
		ApeTag = tail.ApeTag;
		if (tail.ApeTag is not null && !tail.ApeHasHeader)
			_footerOnlyApeTag = tail.ApeTag;
		Id3v1Tag = tail.Id3v1Tag;
	}

	/// <summary>
	/// The end of the audio in <paramref name="data"/>, which starts at the first byte after a leading ID3v2 tag and
	/// runs to the end of the file, by <see cref="AacTail.Locate"/>: the one tail rule detection, both reads and the
	/// save share. Nothing is copied.
	/// </summary>
	internal static long AudioEnd (ReadOnlySpan<byte> data) => AacTail.Locate (new AacTailSource (data), 0, readTags: false).AudioEnd;

	/// <summary>The stream twin of <see cref="AudioEnd(ReadOnlySpan{byte})"/>. The stream position moves.</summary>
	internal static long AudioEnd (Stream stream, long audioStart) =>
		AacTail.Locate (new AacTailSource (stream), audioStart, readTags: false).AudioEnd;

	/// <summary>
	/// Reads an AAC file from a file path.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static AacFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return AacFileReadResult.Failure (readResult.Error!);

		return Remember (Read (readResult.Data!.Value), path, fileSystem);
	}

	/// <summary>
	/// Asynchronously reads an AAC file from a file path.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public static async Task<AacFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return AacFileReadResult.Failure (readResult.Error!);

		return Remember (Read (readResult.Data!.Value), path, fileSystem);
	}

	static AacFileReadResult Remember (AacFileReadResult result, string path, IFileSystem? fileSystem)
	{
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Attempts to read an AAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out AacFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Checks whether the data is a raw AAC file, by the rule <see cref="Read(ReadOnlySpan{byte})"/> uses
	/// to find the stream.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>
	/// <para>
	/// True when the stream starts at offset 0, or after a leading ID3v2 tag within the next 8 KB. The audio ends at
	/// the first trailing tag (APEv2, a Lyrics3 block in front of ID3v1, or ID3v1) or at the end of the data, and
	/// every check below is made against that end, so a trailing tag never changes the answer. "ADIF" counts only
	/// at the first byte and only when its header parses inside the audio. Each 0xFF byte in the window is judged in turn:
	/// </para>
	/// <list type="bullet">
	/// <item>An ADTS header (sync 0xFFF, layer '00') whose next frame header, inside the audio, names the same stream
	/// makes the data AAC.</item>
	/// <item>An ADTS header whose frame runs to or past the end of the audio cannot be checked. It makes the data AAC
	/// only at the first byte.</item>
	/// <item>An MPEG audio frame header (layer other than '00') ends the search as not AAC when its successor
	/// confirms it or its frame runs past the audio, so an ID3-tagged MP3, even a truncated one, stays MP3.</item>
	/// <item>A header its successor contradicts, or one cut by a trailing tag, is skipped.</item>
	/// </list>
	/// <para>
	/// <see cref="Read(ReadOnlySpan{byte})"/> uses the same rule against the same bytes, so data this accepts opens.
	/// </para>
	/// </returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		var start = StreamStart (data);
		if (start >= data.Length)
			return false;

		// The audio end is found only when a candidate needs it, on the span, without a copy.
		return AacStream.FindStart (data[start..], SearchBytes (start), -1, out _) >= 0;
	}

	/// <summary>
	/// The stream twin of <see cref="IsValidFormat(ReadOnlySpan{byte})"/>. The stream position moves.
	/// </summary>
	internal static bool IsValidFormat (Stream stream)
	{
		stream.Position = 0;
		var prefix = StreamBytes.ReadAtMost (stream, Id3v2Header.HeaderSize);
		var start = StreamStart (prefix);
		return AacStream.FindStart (stream, start, SearchBytes (start), -1, out _) >= 0;
	}

	// A leading ID3v2 tag says this is an audio file, so the first frame may sit after padding, as
	// AacFile.Read allows. Without a tag only offset 0 counts; the .aac extension covers the rest.
	static int SearchBytes (int start) => start > 0 ? AacStream.FirstFrameSearchBytes : 1;

	// The byte after a leading ID3v2 tag (header, body, and v2.4 footer), or 0 without one.
	static int StreamStart (ReadOnlySpan<byte> data)
	{
		var header = Id3v2Header.Read (data);
		if (!header.IsSuccess)
			return 0;
		var total = header.Header.TotalSize;
		return total > int.MaxValue ? int.MaxValue : (int)total;
	}

	/// <summary>
	/// Renders the file with the current tags.
	/// </summary>
	/// <param name="originalData">The original file data.</param>
	/// <returns>
	/// The new ID3v2 tag, the original bytes from the end of the read ID3v2 tag to the start of the
	/// trailing tags, the new APEv2 tag, any Lyrics3 block that sat in front of ID3v1, and the new ID3v1 tag.
	/// The APEv2 tag has a header and a footer unless it is the tag that was read and that tag had only a footer.
	/// </returns>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		// The read's tail rule against the read's audio start, so a save keeps every frame the read counted.
		var audioStart = Math.Min (Id3v2Size, originalData.Length);
		var tail = AacTail.Locate (new AacTailSource (originalData), Math.Min (_audioStart, originalData.Length), readTags: false);
		var audioEnd = Math.Max (tail.AudioEnd, audioStart);

		var id3v2 = Id3v2Tag?.Render () ?? BinaryData.Empty;
		var ape = ApeTag?.RenderWithOptions (includeHeader: !ReferenceEquals (ApeTag, _footerOnlyApeTag)) ?? BinaryData.Empty;
		var id3v1 = Id3v1Tag?.Render () ?? BinaryData.Empty;

		// Lyrics3 belongs in front of ID3v1 and goes when that tag goes.
		var lyrics = Id3v1Tag is not null && tail.LyricsStart >= audioEnd && tail.Id3v1Start > tail.LyricsStart
			? originalData[(int)tail.LyricsStart..(int)tail.Id3v1Start]
			: [];

		var audio = originalData[audioStart..(int)audioEnd];
		using var builder = new BinaryDataBuilder (id3v2.Length + audio.Length + ape.Length + lyrics.Length + id3v1.Length);
		builder.Add (id3v2);
		builder.Add (audio);
		builder.Add (ape);
		builder.Add (lyrics);
		builder.Add (id3v1);
		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var rendered = Render (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render AAC file");

		return AtomicFileWriter.Write (path, rendered.Span, fileSystem ?? _sourceFileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var rendered = Render (originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("Failed to render AAC file"));

		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem ?? _sourceFileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path, copying the audio from <see cref="SourcePath"/>.
	/// </summary>
	/// <remarks>
	/// Writes the same bytes as <see cref="Render"/>: the current ID3v2 tag, the original audio, the current
	/// APEv2 tag, any Lyrics3 block in front of ID3v1, and the current ID3v1 tag. The audio is copied in chunks.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveFromSource (path, fileSystem ?? _sourceFileSystem);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path, copying the audio from <see cref="SourcePath"/>.
	/// </summary>
	/// <remarks>
	/// A source that cannot be opened or rendered is reported before cancellation. A token that is already cancelled returns before the destination is replaced. The copy itself runs to completion.
	/// The bytes match <see cref="Render"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return Task.FromResult (SaveFromSource (path, fileSystem ?? _sourceFileSystem, cancellationToken));
	}

	// Same bytes as Render. The audio and any Lyrics3 block are copied; the tags are rendered.
	FileWriteResult SaveFromSource (string destinationPath, IFileSystem? fileSystem, CancellationToken cancellationToken = default)
	{
		OpenedFile? source = null;
		byte[]? chunk = null;
		try {
			if (!PathSave.TryOpen (SourcePath!, fileSystem, out source, out var error))
				return error;

			var opened = source!;
			var fileLength = opened.Length;
			var audioStart = Math.Min ((long)Id3v2Size, fileLength);
			var tail = AacTail.Locate (
				new AacTailSource (opened.Stream, fileLength),
				Math.Min (_audioStart, fileLength),
				readTags: false);
			var audioEnd = Math.Max (tail.AudioEnd, audioStart);
			var id3v2 = Id3v2Tag?.Render () ?? BinaryData.Empty;
			var ape = ApeTag?.RenderWithOptions (includeHeader: !ReferenceEquals (ApeTag, _footerOnlyApeTag)) ?? BinaryData.Empty;
			var id3v1 = Id3v1Tag?.Render () ?? BinaryData.Empty;
			long lyricsStart = 0;
			long lyricsLength = 0;
			if (Id3v1Tag is not null && tail.LyricsStart >= audioEnd && tail.Id3v1Start > tail.LyricsStart) {
				lyricsStart = tail.LyricsStart;
				lyricsLength = tail.Id3v1Start - tail.LyricsStart;
			}

			var audioLength = audioEnd - audioStart;
			if (id3v2.IsEmpty && audioLength <= 0 && ape.IsEmpty && lyricsLength <= 0 && id3v1.IsEmpty)
				return FileWriteResult.Failure ("Failed to render AAC file");

			if (cancellationToken.IsCancellationRequested)
				return FileWriteResult.Failure ("Failed to re-read source file: Operation was cancelled");

			chunk = StreamCopy.Rent ();
			var copyBuffer = chunk;
			var stream = opened.Stream;
			var written = PathSave.Replace (destinationPath, opened, output => {
				StreamCopy.Write (output, id3v2.Span, copyBuffer);
				CopyRange (stream, output, audioStart, audioLength, copyBuffer);
				StreamCopy.Write (output, ape.Span, copyBuffer);
				CopyRange (stream, output, lyricsStart, lyricsLength, copyBuffer);
				StreamCopy.Write (output, id3v1.Span, copyBuffer);
			});
			source = null;
			return written;
		} finally {
			source?.Dispose ();
			if (chunk is not null)
				StreamCopy.Return (chunk);
		}
	}

	static void CopyRange (Stream source, Stream destination, long start, long count, byte[] chunk)
	{
		if (count <= 0)
			return;

		if (source.Position != start)
			source.Position = start;
		StreamCopy.Copy (source, destination, count, chunk);
	}

	/// <summary>
	/// Asynchronously saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Releases resources held by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		Id3v2Tag = null;
		ApeTag = null;
		_footerOnlyApeTag = null;
		Id3v1Tag = null;
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}

/// <summary>
/// Represents the result of reading an AAC file.
/// </summary>
public readonly struct AacFileReadResult : IEquatable<AacFileReadResult>
{
	AacFileReadResult (AacFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public AacFile? File { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static AacFileReadResult Success (AacFile file) => new (file, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static AacFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (AacFileReadResult other) =>
		ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is AacFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (AacFileReadResult left, AacFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (AacFileReadResult left, AacFileReadResult right) =>
		!left.Equals (right);
}
