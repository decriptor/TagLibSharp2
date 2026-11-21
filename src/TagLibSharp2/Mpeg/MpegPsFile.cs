// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Mpeg;

/// <summary>
/// The kind of stream an <see cref="MpegPsFile"/> holds.
/// </summary>
public enum MpegPsStreamType
{
	/// <summary>
	/// An MPEG-1 system stream (ISO/IEC 11172-1) or MPEG-2 program stream (ISO/IEC 13818-1 2.5):
	/// packs that start with 0x000001BA.
	/// </summary>
	ProgramStream,

	/// <summary>
	/// An MPEG-1 or MPEG-2 video elementary stream (ISO/IEC 11172-2, ISO/IEC 13818-2) that starts
	/// with a sequence header, 0x000001B3.
	/// </summary>
	VideoElementaryStream
}

/// <summary>
/// The result of reading an <see cref="MpegPsFile"/>.
/// </summary>
public readonly struct MpegPsFileReadResult : IEquatable<MpegPsFileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public MpegPsFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	MpegPsFileReadResult (MpegPsFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="file">The parsed file.</param>
	/// <returns>A result holding <paramref name="file"/>.</returns>
	public static MpegPsFileReadResult Success (MpegPsFile file) => new (file, null);

	/// <summary>
	/// Creates a failed result.
	/// </summary>
	/// <param name="error">Why parsing failed.</param>
	/// <returns>A result holding <paramref name="error"/>.</returns>
	public static MpegPsFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (MpegPsFileReadResult other) =>
		ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) => obj is MpegPsFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (MpegPsFileReadResult left, MpegPsFileReadResult right) => left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (MpegPsFileReadResult left, MpegPsFileReadResult right) => !left.Equals (right);
}

/// <summary>
/// An MPEG-1 or MPEG-2 program stream (<c>.mpg</c>, <c>.mpeg</c>, <c>.mpe</c>, <c>.vob</c>) or an
/// MPEG video elementary stream (<c>.mpv</c>, <c>.m2v</c>), with an optional ID3v2 tag in front of
/// the stream and an optional ID3v1 tag after it.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 11172-1 and ISO/IEC 13818-1 define no tag container for a program stream. The tags
/// read and written here are the ones other tools read: exiftool reads an ID3v2 tag in front
/// of the first pack and an ID3v1 tag at the end. ffmpeg and ffprobe skip both and still
/// demux and decode the stream. ffprobe and mediainfo report no tags for either.
/// </para>
/// <para>
/// File structure:
/// </para>
/// <code>
/// [ID3v2 tag]        optional
/// [MPEG stream]      packs (0x000001BA) or a video sequence (0x000001B3); never modified
/// [ID3v1 tag]        optional, last 128 bytes
/// </code>
/// <para>
/// Saving rewrites only the tags. The stream bytes, and anything else between the stream and
/// the ID3v1 tag (an APEv2 tag, for example), are copied unchanged. A save with no tag change
/// writes the original bytes. A new tag is ID3v2: a trailing ID3v1 tag after a video elementary
/// stream becomes part of its last picture to a decoder that does not look for it, so ID3v1 is
/// only kept or written when the file already has one or the caller sets
/// <see cref="Id3v1Tag"/>. ffmpeg probes the first megabyte, so an ID3v2 tag larger than about
/// 1 MiB (large cover art) can make ffmpeg take the file for MP3.
/// </para>
/// <para>
/// Reading uses the first and the last 256 KiB of the stream (4 MiB when those lack a codec
/// header or a time stamp). See <see cref="Duration"/> for how the length is measured.
/// </para>
/// </remarks>
public sealed class MpegPsFile : IMediaFile
{
	const int Id3v1Size = 128;

	IFileSystem? _sourceFileSystem;
	bool _disposed;

	// Layout of the file that was read. Render checks the original bytes against it.
	long _fileLength;
	long _streamEnd;
	bool _hadId3v1;
	Id3v2Tag? _readId3v2;
	Id3v1Tag? _readId3v1;

	MpegPsFile ()
	{
	}

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <summary>
	/// Gets whether the file is a program stream or a video elementary stream.
	/// </summary>
	public MpegPsStreamType StreamType { get; private set; }

	/// <summary>
	/// Gets the systems layer version from the first pack header: 1 for ISO/IEC 11172-1
	/// (marker '0010'), 2 for ISO/IEC 13818-1 (marker '01'), or 0 for an elementary stream.
	/// </summary>
	public int SystemsVersion { get; private set; }

	/// <summary>
	/// Gets the video coding version: 1 for ISO/IEC 11172-2, 2 when the sequence header is
	/// followed by an ISO/IEC 13818-2 sequence extension, or 0 when no sequence header was found.
	/// An MPEG-1 system stream may carry MPEG-2 video.
	/// </summary>
	public int VideoVersion { get; private set; }

	/// <summary>
	/// Gets the duration, or null when it cannot be measured.
	/// </summary>
	/// <remarks>
	/// For a program stream this is the span from the earliest presentation time stamp in the
	/// head to the latest one in the tail, plus the length of the frame that stamp starts
	/// (13818-1 2.4.3.7; 90 kHz, modulo 2^33, so one wrap is handled). It matches ffprobe's
	/// container duration. LPCM and DTS frames add no length, so a stream whose last packet is
	/// LPCM can read up to one packet short. Without time stamps, the SCR of the first and the
	/// last pack is used. For an elementary video stream, the pictures are counted from the
	/// GOP time codes (13818-2 6.3.8) and the pictures after the last GOP. When the time codes
	/// do not advance, it is the stream size over the sequence header's bit rate, which is the
	/// peak rate for a variable-rate stream.
	/// </remarks>
	public TimeSpan? Duration { get; private set; }

	/// <summary>
	/// Gets the properties of the first video stream, or null when no sequence header was found.
	/// The bitrate is the sequence header's bit_rate (the peak for a variable-rate stream), or
	/// 0 when it holds the all-ones code (11172-2 2.4.3.2: variable rate), which MPEG-2
	/// encoders also write when they set no peak rate.
	/// </summary>
	public VideoProperties? VideoProperties { get; private set; }

	/// <summary>
	/// Gets the properties of the first audio stream, or null when there is none.
	/// MPEG audio (stream_id 0xC0 to 0xDF) is read from its frame header; AC-3, DTS and LPCM
	/// in private_stream_1 are identified by their DVD sub-stream id. AC-3 reads its sample rate,
	/// bit rate and channels (ATSC A/52), LPCM its sample rate, sample size and channels. DTS is
	/// identified only.
	/// </summary>
	public AudioProperties? AudioProperties { get; private set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag in front of the stream. Set to null to remove it.
	/// </summary>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>
	/// Gets or sets the ID3v1 tag after the stream. Set to null to remove it.
	/// </summary>
	public Id3v1Tag? Id3v1Tag { get; set; }

	/// <summary>
	/// Gets the size in bytes of the ID3v2 tag that was read, including its padding and footer,
	/// or 0 when the file had none. The stream starts here.
	/// </summary>
	public int Id3v2Size { get; private set; }

	/// <summary>
	/// Gets a view over the ID3v2 and ID3v1 tags: ID3v2 wins when it has a value, and a write
	/// goes to both. Null when the file has neither; call <see cref="EnsureId3v2Tag"/> to add one.
	/// </summary>
	public Tag? Tag =>
		(Id3v2Tag is null && Id3v1Tag is null) ? null : new CombinedTag (Id3v2Tag, Id3v1Tag);

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => AudioProperties;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	public MediaTypes MediaTypes { get; private set; }

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.MpegPs;

	/// <summary>
	/// Returns the ID3v2 tag, creating an empty one when the file has none.
	/// </summary>
	/// <returns>The ID3v2 tag.</returns>
	public Id3v2Tag EnsureId3v2Tag ()
	{
		Id3v2Tag ??= new Id3v2Tag ();
		return Id3v2Tag;
	}

	/// <summary>
	/// Checks whether <paramref name="data"/> starts with a pack header or a video sequence
	/// header, after an ID3v2 tag when one is present.
	/// </summary>
	/// <param name="data">The first bytes of the file. With an ID3v2 tag, the bytes after it are needed.</param>
	/// <returns>True when the data starts an MPEG program stream or video elementary stream.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		var tag = TagLength (data, data.Length);
		return tag < data.Length && MpegPsScanner.StreamStart (data.Slice (tag)) >= 0;
	}

	/// <summary>
	/// Reads a program stream or video elementary stream from its bytes. Only the tags and the
	/// first and last windows of the stream are parsed.
	/// </summary>
	/// <param name="data">The file bytes.</param>
	/// <returns>The file, or why it could not be read.</returns>
	public static MpegPsFileReadResult Read (ReadOnlySpan<byte> data)
	{
		var length = data.Length;
		var tagLength = TagLength (data, length);
		var window = MpegPsScanner.SmallWindow;
		while (true) {
			var head = data.Slice (tagLength, (int)Math.Min (window, length - tagLength));
			var footer = length - tagLength >= Id3v1Size ? data.Slice (length - Id3v1Size) : [];
			var streamEnd = StreamEnd (footer, length);
			var tailStart = TailStart (tagLength, streamEnd, window);
			var tail = data.Slice ((int)tailStart, (int)(streamEnd - tailStart));
			var result = Build (data.Slice (0, tagLength), head, footer, tail, length, tailStart, window, out var again);
			if (!again)
				return result;
			window = MpegPsScanner.LargeWindow;
		}
	}

	/// <summary>
	/// Reads a stream by its tags and the windows at both ends of the MPEG stream.
	/// A stream that cannot seek is copied once.
	/// </summary>
	internal static MpegPsFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return WithSource (Read (StreamBytes.ReadToEnd (stream)), sourcePath, fileSystem);

		var length = stream.Length;
		var first = ReadAt (stream, 0, (int)Math.Min (Id3v2Header.HeaderSize, length));
		var tagLength = TagLength (first, length);
		var tag = tagLength > 0 ? ReadAt (stream, 0, tagLength) : [];
		if (tag.Length != tagLength)
			return MpegPsFileReadResult.Failure ("The stream ended inside the ID3v2 tag");
		var footer = length - tagLength >= Id3v1Size ? ReadAt (stream, length - Id3v1Size, Id3v1Size) : [];
		var streamEnd = StreamEnd (footer, length);
		var window = MpegPsScanner.SmallWindow;
		while (true) {
			var head = ReadAt (stream, tagLength, (int)Math.Min (window, length - tagLength));
			var tailStart = TailStart (tagLength, streamEnd, window);
			var tail = ReadAt (stream, tailStart, (int)(streamEnd - tailStart));
			var result = Build (tag, head, footer, tail, length, tailStart, window, out var again);
			if (!again)
				return WithSource (result, sourcePath, fileSystem);
			window = MpegPsScanner.LargeWindow;
		}
	}

	/// <summary>
	/// Asynchronously reads a stream by its tags and the windows at both ends of the MPEG stream.
	/// </summary>
	internal static async Task<MpegPsFileReadResult> ReadAsync (
		Stream stream,
		string? sourcePath,
		IFileSystem? fileSystem,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		if (!stream.CanSeek) {
			var all = await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false);
			return WithSource (Read (all), sourcePath, fileSystem);
		}

		var length = stream.Length;
		var first = await ReadAtAsync (stream, 0, (int)Math.Min (Id3v2Header.HeaderSize, length), cancellationToken).ConfigureAwait (false);
		var tagLength = TagLength (first, length);
		var tag = tagLength > 0 ? await ReadAtAsync (stream, 0, tagLength, cancellationToken).ConfigureAwait (false) : [];
		if (tag.Length != tagLength)
			return MpegPsFileReadResult.Failure ("The stream ended inside the ID3v2 tag");
		var footer = length - tagLength >= Id3v1Size
			? await ReadAtAsync (stream, length - Id3v1Size, Id3v1Size, cancellationToken).ConfigureAwait (false)
			: [];
		var streamEnd = StreamEnd (footer, length);
		var window = MpegPsScanner.SmallWindow;
		while (true) {
			var head = await ReadAtAsync (stream, tagLength, (int)Math.Min (window, length - tagLength), cancellationToken).ConfigureAwait (false);
			var tailStart = TailStart (tagLength, streamEnd, window);
			var tail = await ReadAtAsync (stream, tailStart, (int)(streamEnd - tailStart), cancellationToken).ConfigureAwait (false);
			var result = Build (tag, head, footer, tail, length, tailStart, window, out var again);
			if (!again)
				return WithSource (result, sourcePath, fileSystem);
			window = MpegPsScanner.LargeWindow;
		}
	}

	/// <summary>
	/// Reads an MPEG program stream or video elementary stream from disk. Only the tags and
	/// the windows at both ends of the stream are read.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>The file, or why it could not be read.</returns>
	public static MpegPsFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (!fs.FileExists (path))
			return MpegPsFileReadResult.Failure ($"File not found: {path}");

		try {
			using var stream = fs.OpenRead (path);
			return Read (stream, path, fileSystem);
		} catch (IOException ex) {
			return MpegPsFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return MpegPsFileReadResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	/// <summary>
	/// Asynchronously reads an MPEG program stream or video elementary stream from disk.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the read.</param>
	/// <returns>The file, or why it could not be read.</returns>
	public static async Task<MpegPsFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (!fs.FileExists (path))
			return MpegPsFileReadResult.Failure ($"File not found: {path}");

		try {
			using var stream = fs.OpenRead (path);
			return await ReadAsync (stream, path, fileSystem, cancellationToken).ConfigureAwait (false);
		} catch (IOException ex) {
			return MpegPsFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return MpegPsFileReadResult.Failure ($"Access denied: {ex.Message}");
		} catch (OperationCanceledException) {
			return MpegPsFileReadResult.Failure ("Operation was cancelled");
		}
	}

	/// <summary>
	/// Reads a file without an error string.
	/// </summary>
	/// <param name="data">The file bytes.</param>
	/// <param name="file">The file when reading succeeded.</param>
	/// <returns>True when reading succeeded.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out MpegPsFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// True when the bytes after the ID3v2 tag at the start of <paramref name="stream"/> start
	/// an MPEG stream. Reads the tag header and the first bytes after the tag.
	/// </summary>
	internal static bool StartsAfterId3v2 (Stream stream)
	{
		var length = stream.Length;
		var first = ReadAt (stream, 0, (int)Math.Min (Id3v2Header.HeaderSize, length));
		var tag = TagLength (first, length);
		if (tag <= 0)
			return false;
		var next = ReadAt (stream, tag, (int)Math.Min (64, length - tag));
		return MpegPsScanner.StreamStart (next) >= 0;
	}

	/// <summary>
	/// The bytes of the ID3v2 tag at the start, or 0. A declared size past the end of the file
	/// is clamped, so nothing after it is read as the stream.
	/// </summary>
	/// <remarks>
	/// ID3v2.4 structure 3.1: "ID3", a version and revision below 0xFF, flags, and a syncsafe
	/// size whose bytes are below 0x80. A version this library does not parse is still skipped,
	/// as the specification asks, and its bytes are kept on save. A v2.4 footer adds 10 bytes.
	/// https://id3.org/id3v2.4.0-structure
	/// </remarks>
	static int TagLength (ReadOnlySpan<byte> first, long length)
	{
		if (first.Length < Id3v2Header.HeaderSize || first[0] != 'I' || first[1] != 'D' || first[2] != '3')
			return 0;
		if (first[3] == 0xFF || first[4] == 0xFF)
			return 0;
		if ((first[6] | first[7] | first[8] | first[9]) >= 0x80)
			return 0;

		var size = ((long)first[6] << 21) | ((long)first[7] << 14) | ((long)first[8] << 7) | first[9];
		var footer = first[3] >= 4 && (first[5] & 0x10) != 0 ? Id3v2Header.FooterSize : 0;
		return (int)Math.Min (Id3v2Header.HeaderSize + size + footer, length);
	}

	static long StreamEnd (ReadOnlySpan<byte> footer, long length) =>
		footer.Length == Id3v1Size && Id3v1Tag.Read (footer).IsSuccess ? length - Id3v1Size : length;

	static long TailStart (long streamStart, long streamEnd, int window) =>
		Math.Max (streamStart, streamEnd - window);

	static byte[] ReadAt (Stream stream, long offset, int count)
	{
		if (count <= 0)
			return [];
		stream.Position = offset;
		return StreamBytes.ReadAtMost (stream, count);
	}

	static async Task<byte[]> ReadAtAsync (Stream stream, long offset, int count, CancellationToken cancellationToken)
	{
		if (count <= 0)
			return [];
		stream.Position = offset;
		return await StreamBytes.ReadAtMostAsync (stream, count, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Builds the file from the tag bytes, the head of the stream, the last 128 bytes, and the
	/// tail of the stream. <paramref name="again"/> asks for larger windows.
	/// </summary>
	static MpegPsFileReadResult Build (
		ReadOnlySpan<byte> tag,
		ReadOnlySpan<byte> head,
		ReadOnlySpan<byte> footer,
		ReadOnlySpan<byte> tail,
		long length,
		long tailStart,
		int window,
		out bool again)
	{
		again = false;
		var zeros = MpegPsScanner.StreamStart (head);
		if (zeros < 0)
			return MpegPsFileReadResult.Failure ("Not an MPEG program stream or video elementary stream: no pack or sequence header");

		var streamStart = tag.Length + zeros;
		var streamEnd = StreamEnd (footer, length);
		var analysis = MpegPsScanner.Analyze (
			head.Slice (zeros),
			tail,
			Math.Max (0, streamEnd - streamStart),
			tailStart - streamStart);

		var headIsAll = tag.Length + head.Length >= length;
		var tailIsAll = tailStart <= tag.Length;
		if (window < MpegPsScanner.LargeWindow
			&& ((analysis.NeedsLargerHead && !headIsAll) || (analysis.NeedsLargerTail && !tailIsAll))) {
			again = true;
			return default;
		}

		var file = new MpegPsFile {
			StreamType = analysis.StreamType,
			SystemsVersion = analysis.SystemsVersion,
			Duration = analysis.Duration,
			Id3v2Size = tag.Length,
			_fileLength = length,
			_streamEnd = streamEnd
		};

		if (tag.Length > 0) {
			var parsed = Id3v2Tag.Read (tag);
			if (parsed.IsSuccess)
				file.Id3v2Tag = file._readId3v2 = parsed.Tag;
		}

		if (streamEnd < length) {
			file._hadId3v1 = true;
			var parsed = Id3v1Tag.Read (footer);
			if (parsed.IsSuccess)
				file.Id3v1Tag = file._readId3v1 = parsed.Tag;
		}

		var duration = analysis.Duration ?? TimeSpan.Zero;
		if (analysis.Video is MpegVideoSequenceHeader video) {
			file.VideoVersion = video.Version;
			file.VideoProperties = new VideoProperties (
				duration,
				video.Width,
				video.Height,
				(int)Math.Min (int.MaxValue, video.BitRate / 1000),
				video.FrameRate,
				video.Version == 2 ? "MPEG-2 Video" : "MPEG-1 Video");
		}

		if (analysis.Audio is MpegPsAudio audio) {
			file.AudioProperties = new AudioProperties (
				duration,
				audio.Bitrate,
				audio.SampleRate,
				audio.BitsPerSample,
				audio.Channels,
				audio.Codec);
		}

		var types = MediaTypes.None;
		if (analysis.HasVideo)
			types |= MediaTypes.Video;
		if (analysis.HasAudio)
			types |= MediaTypes.Audio;
		file.MediaTypes = types;
		return MpegPsFileReadResult.Success (file);
	}

	static MpegPsFileReadResult WithSource (MpegPsFileReadResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (result.IsSuccess && result.File is not null) {
			result.File.SourcePath = sourcePath;
			result.File._sourceFileSystem = fileSystem;
		}

		return result;
	}

	/// <summary>
	/// Renders the file with the current tags around the original stream bytes.
	/// </summary>
	/// <param name="originalData">The bytes of the file this instance was read from.</param>
	/// <returns>The new file bytes, or empty when <paramref name="originalData"/> is not that file.</returns>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		if (originalData.Length != _fileLength || _streamEnd > originalData.Length || Id3v2Size > _streamEnd)
			return BinaryData.Empty;
		if (MpegPsScanner.StreamStart (originalData.Slice (Id3v2Size, (int)Math.Min (64, _streamEnd - Id3v2Size))) < 0)
			return BinaryData.Empty;

		var head = RenderId3v2 (originalData);
		var foot = RenderId3v1 (originalData);
		var stream = originalData.Slice (Id3v2Size, (int)(_streamEnd - Id3v2Size));
		using var builder = new BinaryDataBuilder (head.Length + stream.Length + foot.Length);
		builder.Add (head);
		builder.Add (stream);
		builder.Add (foot);
		return builder.ToBinaryData ();
	}

	// The tag that was read and still renders as it did keeps its original bytes, padding included.
	// A tag whose bytes did not parse stays when no tag replaced it.
	ReadOnlySpan<byte> RenderId3v2 (ReadOnlySpan<byte> originalData)
	{
		var original = originalData.Slice (0, Id3v2Size);
		if (Id3v2Tag is null)
			return Id3v2Size > 0 && _readId3v2 is null ? original : [];

		var rendered = Id3v2Tag.Render ();
		if (ReferenceEquals (Id3v2Tag, _readId3v2)) {
			var reread = Id3v2Tag.Read (original);
			if (reread.IsSuccess && reread.Tag!.Render ().Span.SequenceEqual (rendered.Span))
				return original;
		}

		return rendered.Span;
	}

	ReadOnlySpan<byte> RenderId3v1 (ReadOnlySpan<byte> originalData)
	{
		var original = _hadId3v1 ? originalData.Slice (originalData.Length - Id3v1Size) : [];
		if (Id3v1Tag is null)
			return _hadId3v1 && _readId3v1 is null ? original : [];

		var rendered = Id3v1Tag.Render ();
		if (ReferenceEquals (Id3v1Tag, _readId3v1)) {
			var reread = Id3v1Tag.Read (original);
			if (reread.IsSuccess && reread.Tag!.Render ().Span.SequenceEqual (rendered.Span))
				return original;
		}

		return rendered.Span;
	}

	/// <summary>
	/// Saves the file to <paramref name="path"/> using <paramref name="originalData"/> for the stream.
	/// </summary>
	/// <param name="path">The target path.</param>
	/// <param name="originalData">The bytes of the file this instance was read from.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>Whether the write succeeded.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var rendered = Render (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("The original data is not the MPEG stream that was read");

		return AtomicFileWriter.Write (path, rendered.Span, fileSystem ?? _sourceFileSystem);
	}

	/// <summary>
	/// Saves the file to <paramref name="path"/>, re-reading the stream from <see cref="SourcePath"/>.
	/// </summary>
	/// <param name="path">The target path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>Whether the write succeeded.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var read = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!read.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {read.Error}");

		return SaveToFile (path, read.Data!.Value.Span, fs);
	}

	/// <summary>
	/// Saves the file back to <see cref="SourcePath"/>.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>Whether the write succeeded.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to <paramref name="path"/> using <paramref name="originalData"/> for the stream.
	/// </summary>
	/// <param name="path">The target path.</param>
	/// <param name="originalData">The bytes of the file this instance was read from.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the write.</param>
	/// <returns>Whether the write succeeded.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var rendered = Render (originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("The original data is not the MPEG stream that was read"));

		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem ?? _sourceFileSystem, cancellationToken);
	}

	/// <summary>
	/// Asynchronously saves the file to <paramref name="path"/>, re-reading the stream from <see cref="SourcePath"/>.
	/// </summary>
	/// <param name="path">The target path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the write.</param>
	/// <returns>Whether the write succeeded.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var read = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken).ConfigureAwait (false);
		if (!read.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {read.Error}");

		return await SaveToFileAsync (path, read.Data!.Value, fs, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Asynchronously saves the file back to <see cref="SourcePath"/>.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the write.</param>
	/// <returns>Whether the write succeeded.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Releases the tags and the source path.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		Id3v2Tag = null;
		Id3v1Tag = null;
		_readId3v2 = null;
		_readId3v1 = null;
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}
