// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
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
/// MPEG video elementary stream (<c>.mpv</c>, <c>.m1v</c>, <c>.m2v</c>), with an optional ID3v2 tag
/// in front of or after the stream and an optional ID3v1 tag at the end.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 11172-1 and ISO/IEC 13818-1 define no tag container for a program stream. The tags
/// read and written here are the ones other tools read: exiftool reads an ID3v2 tag in front
/// of the first pack and an ID3v1 tag at the end. ffmpeg and ffprobe skip both and still
/// demux and decode the stream. ffprobe and mediainfo report no tags for either. TagLib#
/// writes an ID3v2.4 tag appended after the stream, with the footer ID3v2.4 structure 3.4
/// requires, before ID3v1; it reads that tag, and so does this class.
/// </para>
/// <para>
/// File structure:
/// </para>
/// <code>
/// [ID3v2 tag]        optional, prepended
/// [MPEG stream]      packs (0x000001BA) or a video sequence (0x000001B3); never modified
/// [ID3v2.4 tag]      optional, appended, ends with a "3DI" footer
/// [APEv2 tag]        optional, kept unchanged (it may also come before the appended tag)
/// [ID3v1 tag]        optional, last 128 bytes
/// </code>
/// <para>
/// ID3v2.4 section 5 searches for a prepended tag, then for a footer from the back of the file,
/// and "for every new tag that is found, the old tag should be discarded unless the update flag
/// in the extended header (section 3.2) is set". So an appended tag is <see cref="Id3v2Tag"/>
/// when present; with the update flag, the prepended tag supplies the values it lacks.
/// A tag is saved where it was read: an appended tag stays appended (TagLib# reads it there,
/// and cannot read a prepended ID3v2 tag next to ID3v1), with a footer and no padding (3.3).
/// A prepended tag that the appended one replaced is dropped once the appended tag changes, so
/// one ID3v2 tag remains. A tag that cannot carry a footer (v2.2, v2.3) is written in front.
/// </para>
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

	// Layout of the file that was read. Render checks the original bytes against it:
	// [prepended ID3v2][stream][appended ID3v2][APEv2 or other][ID3v1].
	// Without an appended tag, _streamEnd == _appendedEnd.
	long _fileLength;
	long _streamEnd;
	long _appendedEnd;
	long _footerStart;
	Id3v2Tag? _readId3v2;
	Id3v2Tag? _readAppended;
	Id3v2Tag? _earlierId3v2;
	bool _appendedIsUpdate;
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
	/// Gets or sets the ID3v2 tag: the appended tag when the file has one (ID3v2.4 section 5),
	/// otherwise the tag in front of the stream. Set to null to remove every ID3v2 tag the file has.
	/// </summary>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>
	/// Gets or sets the ID3v1 tag after the stream. Set to null to remove it.
	/// </summary>
	public Id3v1Tag? Id3v1Tag { get; set; }

	/// <summary>
	/// Gets the size in bytes of the ID3v2 tag in front of the stream, including its padding and
	/// footer, or 0 when the file had none. The stream starts here.
	/// </summary>
	public int Id3v2Size { get; private set; }

	/// <summary>
	/// Gets a view over the ID3v2 and ID3v1 tags: ID3v2 wins when it has a value, and a write
	/// goes to every tag in the view. An appended update tag is followed by the prepended tag it
	/// updates. Null when the file has neither; call <see cref="EnsureId3v2Tag"/> to add one.
	/// </summary>
	public Tag? Tag {
		get {
			if (Id3v2Tag is null && Id3v1Tag is null)
				return null;
			var earlier = EarlierTag;
			return earlier is null ? new CombinedTag (Id3v2Tag, Id3v1Tag) : new CombinedTag (Id3v2Tag, earlier, Id3v1Tag);
		}
	}

	/// <summary>
	/// The prepended tag that an appended update tag extends, while the appended tag is still the one read.
	/// </summary>
	Id3v2Tag? EarlierTag =>
		_earlierId3v2 is not null && _readAppended is not null && ReferenceEquals (Id3v2Tag, _readAppended) ? _earlierId3v2 : null;

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
		var footer = length - tagLength >= Id3v1Size ? data.Slice (length - Id3v1Size) : [];
		var footerStart = FooterStart (footer, length);

		// Section 5 of ID3v2.4: an appended tag ends just before ID3v1, or before an APEv2 tag
		// ("before tags from other tagging systems"); its footer is found from the back.
		var appendedEnd = footerStart;
		var appendedLength = AppendedLength (Before (data, appendedEnd, Id3v2Header.FooterSize), appendedEnd, tagLength);
		if (appendedLength == 0) {
			var ape = ApeLength (Before (data, footerStart, ApeTagFooter.Size), footerStart, tagLength);
			if (ape > 0) {
				appendedEnd = footerStart - ape;
				appendedLength = AppendedLength (Before (data, appendedEnd, Id3v2Header.FooterSize), appendedEnd, tagLength);
			}
		}

		var appended = appendedLength > 0 ? data.Slice ((int)(appendedEnd - appendedLength), appendedLength) : [];
		var window = MpegPsScanner.SmallWindow;
		while (true) {
			var head = data.Slice (tagLength, (int)Math.Min (window, length - tagLength));
			var streamEnd = StreamEnd (appended, appendedEnd, footerStart);
			var tailStart = TailStart (tagLength, streamEnd, window);
			var tail = data.Slice ((int)tailStart, (int)(streamEnd - tailStart));
			var result = Build (data.Slice (0, tagLength), head, footer, appended, appendedEnd, tail, length, tailStart, window, out var again);
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
		var footerStart = FooterStart (footer, length);

		var appendedEnd = footerStart;
		var appendedLength = AppendedLength (ReadBefore (stream, appendedEnd, Id3v2Header.FooterSize), appendedEnd, tagLength);
		if (appendedLength == 0) {
			var ape = ApeLength (ReadBefore (stream, footerStart, ApeTagFooter.Size), footerStart, tagLength);
			if (ape > 0) {
				appendedEnd = footerStart - ape;
				appendedLength = AppendedLength (ReadBefore (stream, appendedEnd, Id3v2Header.FooterSize), appendedEnd, tagLength);
			}
		}

		var appended = appendedLength > 0 ? ReadAt (stream, appendedEnd - appendedLength, appendedLength) : [];
		var streamEnd = StreamEnd (appended, appendedEnd, footerStart);
		var window = MpegPsScanner.SmallWindow;
		while (true) {
			var head = ReadAt (stream, tagLength, (int)Math.Min (window, length - tagLength));
			var tailStart = TailStart (tagLength, streamEnd, window);
			var tail = ReadAt (stream, tailStart, (int)(streamEnd - tailStart));
			var result = Build (tag, head, footer, appended, appendedEnd, tail, length, tailStart, window, out var again);
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
		var footerStart = FooterStart (footer, length);

		var appendedEnd = footerStart;
		var probe = await ReadBeforeAsync (stream, appendedEnd, Id3v2Header.FooterSize, cancellationToken).ConfigureAwait (false);
		var appendedLength = AppendedLength (probe, appendedEnd, tagLength);
		if (appendedLength == 0) {
			var apeFooter = await ReadBeforeAsync (stream, footerStart, ApeTagFooter.Size, cancellationToken).ConfigureAwait (false);
			var ape = ApeLength (apeFooter, footerStart, tagLength);
			if (ape > 0) {
				appendedEnd = footerStart - ape;
				probe = await ReadBeforeAsync (stream, appendedEnd, Id3v2Header.FooterSize, cancellationToken).ConfigureAwait (false);
				appendedLength = AppendedLength (probe, appendedEnd, tagLength);
			}
		}

		var appended = appendedLength > 0
			? await ReadAtAsync (stream, appendedEnd - appendedLength, appendedLength, cancellationToken).ConfigureAwait (false)
			: [];
		var streamEnd = StreamEnd (appended, appendedEnd, footerStart);
		var window = MpegPsScanner.SmallWindow;
		while (true) {
			var head = await ReadAtAsync (stream, tagLength, (int)Math.Min (window, length - tagLength), cancellationToken).ConfigureAwait (false);
			var tailStart = TailStart (tagLength, streamEnd, window);
			var tail = await ReadAtAsync (stream, tailStart, (int)(streamEnd - tailStart), cancellationToken).ConfigureAwait (false);
			var result = Build (tag, head, footer, appended, appendedEnd, tail, length, tailStart, window, out var again);
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

	/// <summary>Where a trailing ID3v1 tag starts, or the file length when there is none.</summary>
	static long FooterStart (ReadOnlySpan<byte> footer, long length) =>
		footer.Length == Id3v1Size && Id3v1Tag.Read (footer).IsSuccess ? length - Id3v1Size : length;

	/// <summary>The stream ends where an appended ID3v2 tag starts, or at the trailing ID3v1 tag.</summary>
	static long StreamEnd (ReadOnlySpan<byte> appended, long appendedEnd, long footerStart) =>
		appended.IsEmpty ? footerStart : appendedEnd - appended.Length;

	/// <summary>
	/// The length of an appended ID3v2 tag whose footer is <paramref name="footer"/>, the 10 bytes
	/// before <paramref name="end"/>, or 0. The tag may not reach back past <paramref name="floor"/>.
	/// </summary>
	/// <remarks>
	/// ID3v2.4 structure 3.4: "It is REQUIRED to add a footer to an appended tag". The footer is a
	/// copy of the header with the identifier "3DI": version $04 00, flags with the footer bit
	/// (3.1 flag d) set, and the syncsafe size, which excludes the header and the footer.
	/// The tag spans size + 20 bytes. Footers exist only in v2.4.
	/// https://id3.org/id3v2.4.0-structure
	/// </remarks>
	static int AppendedLength (ReadOnlySpan<byte> footer, long end, long floor)
	{
		if (footer.Length != Id3v2Header.FooterSize || footer[0] != '3' || footer[1] != 'D' || footer[2] != 'I')
			return 0;
		if (footer[3] != 4 || footer[4] == 0xFF || (footer[5] & 0x10) == 0)
			return 0;
		if ((footer[6] | footer[7] | footer[8] | footer[9]) >= 0x80)
			return 0;

		var size = ((long)footer[6] << 21) | ((long)footer[7] << 14) | ((long)footer[8] << 7) | footer[9];
		var total = Id3v2Header.HeaderSize + size + Id3v2Header.FooterSize;
		return total <= end - floor ? (int)total : 0;
	}

	/// <summary>
	/// The length of an APEv2 tag whose footer is <paramref name="footer"/>, the 32 bytes before
	/// <paramref name="end"/>, or 0. The tag size counts the footer; a header adds 32 bytes
	/// when the footer's bit 31 says there is one.
	/// </summary>
	static int ApeLength (ReadOnlySpan<byte> footer, long end, long floor)
	{
		if (footer.Length != ApeTagFooter.Size)
			return 0;
		var parsed = ApeTagFooter.Parse (footer);
		if (!parsed.IsSuccess || parsed.Footer is not { } ape || ape.IsHeader)
			return 0;
		var total = (long)ape.TagSize + ((ape.Flags & 0x80000000) != 0 ? ApeTagFooter.Size : 0);
		return total >= ApeTagFooter.Size && total <= end - floor ? (int)total : 0;
	}

	/// <summary>
	/// True when <paramref name="tag"/> starts with an "ID3" header that matches its "3DI" footer.
	/// </summary>
	static bool IsAppendedTag (ReadOnlySpan<byte> tag)
	{
		if (tag.Length < Id3v2Header.HeaderSize + Id3v2Header.FooterSize || tag[0] != 'I' || tag[1] != 'D' || tag[2] != '3')
			return false;
		return tag.Slice (3, 7).SequenceEqual (tag.Slice (tag.Length - 7, 7));
	}

	/// <summary>
	/// ID3v2.4 structure 3.2: the extended header (flag b of the header) holds its size, the number
	/// of flag bytes, and the flags; flag b there marks the tag as an update of an earlier one.
	/// </summary>
	static bool IsUpdate (ReadOnlySpan<byte> tag) =>
		tag.Length >= 16 && tag[3] == 4 && (tag[5] & 0x40) != 0 && tag[14] >= 1 && (tag[15] & 0x40) != 0;

	static ReadOnlySpan<byte> Before (ReadOnlySpan<byte> data, long end, int count) =>
		end >= count ? data.Slice ((int)(end - count), count) : [];

	static byte[] ReadBefore (Stream stream, long end, int count) =>
		end >= count ? ReadAt (stream, end - count, count) : [];

	static Task<byte[]> ReadBeforeAsync (Stream stream, long end, int count, CancellationToken cancellationToken) =>
		end >= count ? ReadAtAsync (stream, end - count, count, cancellationToken) : Task.FromResult (Array.Empty<byte> ());

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
		ReadOnlySpan<byte> appended,
		long appendedEnd,
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
		var footerStart = FooterStart (footer, length);
		if (!appended.IsEmpty && !IsAppendedTag (appended))
			appended = [];
		var streamEnd = StreamEnd (appended, appendedEnd, footerStart);
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
			_streamEnd = streamEnd,
			_appendedEnd = appended.IsEmpty ? streamEnd : appendedEnd,
			_footerStart = footerStart
		};

		if (tag.Length > 0) {
			var parsed = Id3v2Tag.Read (tag);
			if (parsed.IsSuccess)
				file.Id3v2Tag = file._readId3v2 = parsed.Tag;
		}

		// ID3v2.4 section 5: "For every new tag that is found, the old tag should be discarded
		// unless the update flag in the extended header (section 3.2) is set." The appended tag
		// is found after the prepended one, so it replaces it, or updates it.
		if (!appended.IsEmpty) {
			var parsed = Id3v2Tag.Read (appended);
			if (parsed.IsSuccess) {
				file._readAppended = parsed.Tag;
				file._appendedIsUpdate = IsUpdate (appended);
				if (file._appendedIsUpdate)
					file._earlierId3v2 = file._readId3v2;
				file.Id3v2Tag = parsed.Tag;
			}
		}

		if (footerStart < length) {
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
		if (originalData.Length != _fileLength || _footerStart > originalData.Length
			|| _appendedEnd > _footerStart || _streamEnd > _appendedEnd || Id3v2Size > _streamEnd)
			return BinaryData.Empty;
		if (MpegPsScanner.StreamStart (originalData.Slice (Id3v2Size, (int)Math.Min (64, _streamEnd - Id3v2Size))) < 0)
			return BinaryData.Empty;

		var appended = RenderAppendedId3v2 (originalData, out var appendedUnchanged);
		var head = RenderPrependedId3v2 (originalData, appendedUnchanged);
		var foot = RenderId3v1 (originalData);
		var stream = originalData.Slice (Id3v2Size, (int)(_streamEnd - Id3v2Size));
		var between = originalData.Slice ((int)_appendedEnd, (int)(_footerStart - _appendedEnd));
		using var builder = new BinaryDataBuilder (head.Length + stream.Length + appended.Length + between.Length + foot.Length);
		builder.Add (head);
		builder.Add (stream);
		builder.Add (appended);
		builder.Add (between);
		builder.Add (foot);
		return builder.ToBinaryData ();
	}

	/// <summary>
	/// A tag read from the end of the file is written back there while it is a v2.4 tag:
	/// only v2.4 has the footer an appended tag needs (ID3v2.4 structure 3.4). It has no padding,
	/// which 3.3 forbids with a footer. Any other tag goes in front of the stream.
	/// </summary>
	bool WritesAppended => _readAppended is not null && Id3v2Tag is { Version: 4 };

	// The tag that was read and still renders as it did keeps its original bytes, padding included.
	// An ID3v2 tag whose bytes did not parse stays where it is.
	ReadOnlySpan<byte> RenderPrependedId3v2 (ReadOnlySpan<byte> originalData, bool appendedUnchanged)
	{
		var original = originalData.Slice (0, Id3v2Size);
		if (Id3v2Size > 0 && _readId3v2 is null)
			return original;

		if (WritesAppended) {
			if (EarlierTag is { } earlier)
				return KeepIfUnchanged (earlier, _readId3v2, original, earlier.Render (), static tag => tag.Render ());

			// The appended tag replaced this one (section 5). An unchanged save keeps the file as it was.
			return appendedUnchanged ? original : [];
		}

		if (Id3v2Tag is null)
			return [];
		return KeepIfUnchanged (Id3v2Tag, _readId3v2, original, Id3v2Tag.Render (), static tag => tag.Render ());
	}

	ReadOnlySpan<byte> RenderAppendedId3v2 (ReadOnlySpan<byte> originalData, out bool unchanged)
	{
		var original = originalData.Slice ((int)_streamEnd, (int)(_appendedEnd - _streamEnd));
		unchanged = true;
		if (original.IsEmpty || _readAppended is null)
			return original;

		unchanged = false;
		if (!WritesAppended)
			return [];

		var update = EarlierTag is not null;
		var rendered = RenderForEnd (Id3v2Tag!, update);
		var kept = KeepIfUnchanged (Id3v2Tag!, _readAppended, original, rendered, tag => RenderForEnd (tag, update));
		unchanged = kept == original;
		return kept;
	}

	static ReadOnlySpan<byte> KeepIfUnchanged (
		Id3v2Tag tag,
		Id3v2Tag? read,
		ReadOnlySpan<byte> original,
		BinaryData rendered,
		Func<Id3v2Tag, BinaryData> render)
	{
		if (ReferenceEquals (tag, read)) {
			var reread = Id3v2Tag.Read (original);
			if (reread.IsSuccess && render (reread.Tag!).Span.SequenceEqual (rendered.Span))
				return original;
		}

		return rendered.Span;
	}

	/// <summary>
	/// An appended tag: v2.4 with a footer and no padding. An update tag keeps the extended
	/// header that says so (3.2): size 7, one flag byte with flag b set, and that flag's empty data.
	/// </summary>
	static BinaryData RenderForEnd (Id3v2Tag tag, bool update)
	{
		var rendered = tag.Render (0, withFooter: true);
		if (!update)
			return rendered;

		ReadOnlySpan<byte> extended = [0x00, 0x00, 0x00, 0x07, 0x01, 0x40, 0x00];
		var bytes = rendered.Span;
		var body = bytes.Slice (Id3v2Header.HeaderSize, bytes.Length - Id3v2Header.HeaderSize - Id3v2Header.FooterSize);
		var size = body.Length + extended.Length;
		Span<byte> header = stackalloc byte[Id3v2Header.HeaderSize];
		bytes.Slice (0, Id3v2Header.HeaderSize).CopyTo (header);
		header[5] |= 0x40;
		header[6] = (byte)((size >> 21) & 0x7F);
		header[7] = (byte)((size >> 14) & 0x7F);
		header[8] = (byte)((size >> 7) & 0x7F);
		header[9] = (byte)(size & 0x7F);
		using var builder = new BinaryDataBuilder (size + Id3v2Header.HeaderSize + Id3v2Header.FooterSize);
		builder.Add (header);
		builder.Add (extended);
		builder.Add (body);
		header[0] = (byte)'3';
		header[1] = (byte)'D';
		header[2] = (byte)'I';
		builder.Add (header);
		return builder.ToBinaryData ();
	}

	ReadOnlySpan<byte> RenderId3v1 (ReadOnlySpan<byte> originalData)
	{
		if (Id3v1Tag is null)
			return [];

		var rendered = Id3v1Tag.Render ();
		if (ReferenceEquals (Id3v1Tag, _readId3v1)) {
			var original = originalData.Slice (originalData.Length - Id3v1Size);
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
		_readAppended = null;
		_earlierId3v2 = null;
		_readId3v1 = null;
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}
