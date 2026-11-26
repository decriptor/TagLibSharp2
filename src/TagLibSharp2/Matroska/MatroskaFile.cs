// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// Result of reading a Matroska or WebM file.
/// </summary>
public readonly struct MatroskaFileReadResult : IEquatable<MatroskaFileReadResult>
{
	MatroskaFileReadResult (MatroskaFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>Gets the parsed file, or null when reading failed.</summary>
	public MatroskaFile? File { get; }

	/// <summary>Gets the error, or null when reading succeeded.</summary>
	public string? Error { get; }

	/// <summary>Gets a value indicating whether a file was parsed.</summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>Creates a successful result.</summary>
	public static MatroskaFileReadResult Success (MatroskaFile file) => new (file, null);

	/// <summary>Creates a failed result.</summary>
	public static MatroskaFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (MatroskaFileReadResult other) => Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) => obj is MatroskaFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>Compares two results.</summary>
	public static bool operator == (MatroskaFileReadResult left, MatroskaFileReadResult right) => left.Equals (right);

	/// <summary>Compares two results.</summary>
	public static bool operator != (MatroskaFileReadResult left, MatroskaFileReadResult right) => !left.Equals (right);
}

/// <summary>
/// A Matroska or WebM file. Reading maps <c>Tags</c> onto <see cref="Tag"/> and image attachments
/// onto <see cref="MatroskaTag.Pictures"/>.
/// Saving splices <see cref="MatroskaTag.Render()"/> into the first <c>Segment</c>, the
/// <c>Attachments</c> element too when the pictures were set, and <c>Chapters</c> onto <see cref="Chapters"/>
/// when the chapters were edited. An element that was not edited keeps its original bytes.
/// Any <see cref="MatroskaTrack.IsEnabled"/> the caller set is written first.
/// </summary>
public sealed class MatroskaFile : IMediaFile
{
	const ulong DefaultTimestampScale = 1_000_000;

	/// <summary>
	/// The most <see cref="Tracks"/> entries kept. RFC 9559 sets no maxOccurs on TrackEntry, so a crafted
	/// file could hold millions; every entry still counts when the main audio and video track is chosen.
	/// </summary>
	internal const int MaxTracks = 4096;
	IFileSystem? _sourceFileSystem;

	MatroskaFile (MatroskaTag tag, MatroskaChapters chapters, TimeSpan? duration, TrackLayout layout)
	{
		Tag = tag;
		Chapters = chapters;
		Duration = duration;
		MediaTypes = layout.Types;
		_sampleRate = layout.SampleRate;
		_channels = layout.Channels;
		_bitsPerSample = layout.BitsPerSample;
		_audioCodec = layout.AudioCodec;
		_width = layout.Width;
		_height = layout.Height;
		_frameRate = layout.FrameRate;
		_videoCodec = layout.VideoCodec;
		Tracks = layout.Tracks is null ? [] : layout.Tracks.ToArray ();
		IsTrackListTruncated = layout.IsTrackListTruncated;
	}

	readonly int _sampleRate;
	readonly int _channels;
	readonly int _bitsPerSample;
	readonly string? _audioCodec;
	readonly int _width;
	readonly int _height;
	readonly double _frameRate;
	readonly string? _videoCodec;

	/// <summary>Gets the Matroska tags.</summary>
	public MatroskaTag Tag { get; }

	Tag? IMediaFile.Tag => Tag;

	/// <summary>
	/// Gets the editions and chapters from the first <c>Chapters</c> element. Chapter titles never become <see cref="MatroskaTag.Title"/>.
	/// A save rewrites <c>Chapters</c> only when these differ from what was read.
	/// </summary>
	public MatroskaChapters Chapters { get; }

	/// <summary>Gets the segment duration, when <c>Info/Duration</c> is present.</summary>
	public TimeSpan? Duration { get; }

	/// <summary>Gets audio and video flags from <c>TrackType</c>.</summary>
	public MediaTypes MediaTypes { get; }

	/// <summary>
	/// Gets the first 4096 known-size <c>TrackEntry</c> elements in file order. RFC 9559 §5.1.4.1.
	/// <see cref="MatroskaTrack.IsEnabled"/> can be changed and is written on save.
	/// </summary>
	/// <remarks>
	/// RFC 9559 sets no limit on the number of TrackEntry elements, so the list stops at 4096 to bound
	/// what a crafted file can allocate. Entries past it still count when the main audio and video track
	/// is chosen. <see cref="IsTrackListTruncated"/> tells when entries were left out.
	/// </remarks>
	public IReadOnlyList<MatroskaTrack> Tracks { get; }

	/// <summary>
	/// Gets a value indicating whether the file has more known-size <c>TrackEntry</c> elements than
	/// <see cref="Tracks"/> lists (more than 4096).
	/// </summary>
	public bool IsTrackListTruncated { get; }

	IMediaProperties? IMediaFile.AudioProperties =>
		(MediaTypes & MediaTypes.Audio) != 0
			? new AudioProperties (Duration ?? TimeSpan.Zero, 0, _sampleRate, _bitsPerSample, _channels, _audioCodec ?? "Matroska")
			: null;

	VideoProperties? IMediaFile.VideoProperties =>
		(MediaTypes & MediaTypes.Video) != 0
			? new VideoProperties (Duration ?? TimeSpan.Zero, _width, _height, 0, _frameRate, _videoCodec ?? "Matroska")
			: null;

	ImageProperties? IMediaFile.ImageProperties => null;

	/// <summary>Gets the path passed to a file read, when one was used.</summary>
	public string? SourcePath { get; private set; }

	/// <summary>Gets <see cref="MediaFormat.Matroska"/>.</summary>
	public MediaFormat Format => MediaFormat.Matroska;

	/// <summary>Returns true when the data starts with the EBML magic.</summary>
	public static bool IsValidFormat (ReadOnlySpan<byte> data) =>
		data.Length >= 4
		&& data[0] == 0x1A
		&& data[1] == 0x45
		&& data[2] == 0xDF
		&& data[3] == 0xA3;

	/// <summary>Reads tags and segment duration from a Matroska or WebM file.</summary>
	public static MatroskaFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (!IsValidFormat (data))
			return MatroskaFileReadResult.Failure ("Not a Matroska file: missing EBML header");

		if (!EbmlReader.TryRead (data, 0, data.Length, out var header) || header.Id != MatroskaIds.Ebml || header.UnknownSize)
			return MatroskaFileReadResult.Failure ("Not a Matroska file: EBML header is truncated");

		var docType = ReadDocType (data, header);
		if (docType is null)
			return MatroskaFileReadResult.Failure ("Not a Matroska file: DocType is missing");
		if (!docType.Equals ("matroska", StringComparison.OrdinalIgnoreCase)
			&& !docType.Equals ("webm", StringComparison.OrdinalIgnoreCase))
			return MatroskaFileReadResult.Failure ("Not a Matroska file: DocType is " + docType);

		var offset = header.End;
		EbmlElement segment = default;
		var foundSegment = false;
		while (offset < data.Length) {
			if (!EbmlReader.TryRead (data, offset, data.Length, out var element))
				break;
			if (element.Id == MatroskaIds.Segment) {
				segment = element;
				foundSegment = true;
				break;
			}

			if (element.UnknownSize)
				return MatroskaFileReadResult.Failure ("Matroska element size is unknown before Segment");
			offset = element.End;
		}

		if (!foundSegment)
			return MatroskaFileReadResult.Failure ("Matroska file has no Segment");

		var tag = new MatroskaTag ();
		var isWebM = docType.Equals ("webm", StringComparison.OrdinalIgnoreCase);
		MatroskaChapters? chapters = null;
		var scale = DefaultTimestampScale;
		double? durationTicks = null;
		var layout = new TrackLayout ();
		var segmentEnd = PayloadEnd (data, segment);
		offset = segment.DataOffset;
		while (offset < segmentEnd) {
			// Padding and a short illegal ID are skipped. A live segment can put
			// 0x0A bytes between an unknown size and the first real element.
			if (!EbmlReader.TryRead (data, offset, segmentEnd, out var element)) {
				offset++;
				continue;
			}

			if (element.Id == MatroskaIds.Info)
				ReadInfo (data, Range (data, element, segmentEnd), ref scale, ref durationTicks, tag);
			else if (element.Id == MatroskaIds.Tracks)
				ReadTracks (Range (data, element, segmentEnd), ref layout);
			else if (element.Id == MatroskaIds.Tags)
				ReadTags (data, Range (data, element, segmentEnd), tag);
			else if (element.Id == MatroskaIds.Attachments)
				ReadAttachments (Range (data, element, segmentEnd), tag);
			else if (element.Id == MatroskaIds.Chapters && chapters is null) {
				// Chapters is identically recurring (RFC 8794 section 11.1.17), so the first copy is read.
				chapters = MatroskaChapters.Read (Range (data, element, segmentEnd), isWebM);
			}

			if (element.UnknownSize)
				offset = NextSibling (data, element.DataOffset, segmentEnd);
			else
				offset = element.End;
		}

		TimeSpan? duration = null;
		if (durationTicks is double ticks && scale > 0 && !double.IsNaN (ticks) && !double.IsInfinity (ticks)) {
			var seconds = ticks * scale / 1_000_000_000d;
			if (seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
				duration = TimeSpan.FromSeconds (seconds);
		}

		tag.MarkRead ();
		return MatroskaFileReadResult.Success (new MatroskaFile (tag, chapters ?? MatroskaChapters.Empty (isWebM), duration, layout));
	}

	/// <summary>
	/// Reads tags and duration from a stream. Cluster payloads stay on disk.
	/// An unknown-size element ends at the next segment child, the same way a byte read does.
	/// The bytes in between are not copied.
	/// </summary>
	internal static MatroskaFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return WithSource (Read (StreamBytes.ReadToEnd (stream)), sourcePath, fileSystem);

		if (stream.Position != 0)
			stream.Position = 0;

		using var kept = new MemoryStream ();
		if (!TryCollect (stream, kept, out var error))
			return MatroskaFileReadResult.Failure (error);

		return WithSource (Read (kept.GetBuffer ().AsSpan (0, (int)kept.Length)), sourcePath, fileSystem);
	}

	static MatroskaFileReadResult WithSource (MatroskaFileReadResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (result.IsSuccess && result.File is not null)
			result.File.SetSourcePath (sourcePath, fileSystem);
		return result;
	}

	static bool TryCollect (Stream stream, MemoryStream kept, out string error)
	{
		error = "";
		var length = stream.Length;
		long pos = 0;
		while (pos + 2 <= length) {
			if (!TryPeek (stream, pos, out var id, out var headerLength, out var dataLength))
				break;

			var known = dataLength >= 0;
			if (id == MatroskaIds.Segment) {
				StreamBytes.Append (kept, new byte[] { 0x18, 0x53, 0x80, 0x67, 0xFF });
				var childEnd = known ? pos + headerLength + dataLength : length;
				if (childEnd > length)
					childEnd = length;
				CollectChildren (stream, pos + headerLength, childEnd, kept);
				return true;
			}

			if (!known) {
				error = "Matroska element size is unknown before Segment";
				return false;
			}

			var end = pos + headerLength + dataLength;
			if (end > length || end < pos)
				break;
			if (!Copy (stream, pos, end, kept))
				break;
			pos = end;
		}

		error = "Matroska file has no Segment";
		return false;
	}

	static void CollectChildren (Stream stream, long start, long end, MemoryStream kept)
	{
		var pos = start;
		while (pos + 2 <= end) {
			if (!TryPeek (stream, pos, out var id, out var headerLength, out var dataLength))
				break;
			if (id == MatroskaIds.Cluster) {
				// Known clusters are seeked past. An unknown-size cluster ends at the next
				// segment child, matching the byte reader, and its payload is not copied.
				var clusterEnd = dataLength < 0
					? NextSegmentChild (stream, pos + headerLength, end)
					: pos + headerLength + dataLength;
				if (clusterEnd > end || clusterEnd <= pos)
					break;
				pos = clusterEnd;
				continue;
			}

			if (dataLength < 0) {
				var bounded = NextSegmentChild (stream, pos + headerLength, end);
				if (bounded > end || bounded <= pos)
					break;
				if (!Copy (stream, pos, bounded, kept))
					break;
				pos = bounded;
				continue;
			}

			var childEnd = pos + headerLength + dataLength;
			if (childEnd > end || childEnd < pos)
				break;
			if (!Copy (stream, pos, childEnd, kept))
				break;
			pos = childEnd;
		}
	}

	static bool TryPeek (Stream stream, long pos, out ulong id, out int headerLength, out long dataLength)
	{
		id = 0;
		headerLength = 0;
		dataLength = -1;
		if (pos < 0 || pos >= stream.Length)
			return false;

		stream.Position = pos;
		var probe = StreamBytes.ReadAtMost (stream, 12);
		return EbmlReader.TryPeekHeader (probe, out id, out headerLength, out dataLength);
	}

	/// <summary>
	/// The next segment-level element at or after <paramref name="start"/>.
	/// Matches <see cref="NextSibling"/>: a payload that is not an element ends the walk.
	/// </summary>
	static long NextSegmentChild (Stream stream, long start, long end)
	{
		var cursor = start;
		while (cursor + 2 <= end) {
			if (!TryPeek (stream, cursor, out var id, out var headerLength, out var dataLength))
				return end;
			if (MatroskaIds.IsSegmentChild (id))
				return cursor;
			if (dataLength < 0)
				return end;
			var childEnd = cursor + headerLength + dataLength;
			if (childEnd > end || childEnd <= cursor)
				return end;
			cursor = childEnd;
		}

		return end;
	}

	static bool Copy (Stream stream, long start, long end, MemoryStream kept)
	{
		var length = end - start;
		if (length < 0 || length > int.MaxValue)
			return false;

		stream.Position = start;
		var bytes = StreamBytes.ReadAtMost (stream, (int)length);
		if (bytes.Length != length)
			return false;
		StreamBytes.Append (kept, bytes);
		return true;
	}

	/// <summary>Reads a file without an error string.</summary>
	public static bool TryRead (ReadOnlySpan<byte> data, out MatroskaFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Writes each <see cref="MatroskaTrack.IsEnabled"/> the caller set, then the edited <c>Attachments</c>,
	/// <c>Chapters</c>, and <c>Tags</c> elements into a copy of <paramref name="originalData"/>.
	/// </summary>
	/// <returns>
	/// The rewritten file, or empty when the save is refused: the container cannot be bounded, a picture
	/// cannot be written (see <see cref="MatroskaTag.Pictures"/>), a track flag cannot be written, or an
	/// edited chapter breaks an RFC 9559 timing rule (see <see cref="MatroskaChapters"/>).
	/// <see cref="SaveToFile(string, ReadOnlySpan{byte}, IFileSystem?)"/> returns the reason.
	/// </returns>
	public BinaryData Render (ReadOnlySpan<byte> originalData) =>
		TryRender (originalData, out var rewritten, out _)
			? new BinaryData (rewritten)
			: BinaryData.Empty;

	/// <summary>
	/// Writes each <see cref="MatroskaTrack.IsEnabled"/> the caller set, then the edited attachments
	/// (when the pictures were set), chapters, and tags. With no track set, or every set track already
	/// holding that value, the container goes straight to that rewrite.
	/// Any refusal leaves <paramref name="rewritten"/> empty and names the reason in <paramref name="error"/>.
	/// </summary>
	bool TryRender (ReadOnlySpan<byte> originalData, out byte[] rewritten, out string error)
	{
		List<TrackFlagEdit>? edits = null;
		foreach (var track in Tracks) {
			if (track.IsEnabledAssigned)
				(edits ??= new List<TrackFlagEdit> ()).Add (new TrackFlagEdit (track.Ordinal, track.TrackNumber, track.TrackUid, track.IsEnabled));
		}

		if (edits is null)
			return MatroskaRewrite.TryRewrite (originalData, Tag, Chapters, out rewritten, out error);

		if (!MatroskaRewrite.TryWriteTrackFlags (originalData, edits, out var flagged, out error)) {
			rewritten = [];
			return false;
		}

		return flagged is null
			? MatroskaRewrite.TryRewrite (originalData, Tag, Chapters, out rewritten, out error)
			: MatroskaRewrite.TryRewrite (flagged, Tag, Chapters, out rewritten, out error);
	}

	/// <summary>Saves tags into <paramref name="path"/> using <paramref name="originalData"/> as the container.</summary>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		if (!TryRender (originalData, out var rewritten, out var error))
			return FileWriteResult.Failure (error);

		fileSystem ??= _sourceFileSystem;
		return AtomicFileWriter.Write (path, rewritten, fileSystem);
	}

	/// <summary>Saves tags into <paramref name="path"/>, copying the container from <see cref="SourcePath"/>.</summary>
	/// <remarks>Cluster bytes are copied in chunks. <see cref="Render"/> and the span overloads still build one buffer.</remarks>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveStreaming (path, fileSystem ?? _sourceFileSystem, CancellationToken.None);
	}

	/// <summary>Saves tags back to <see cref="SourcePath"/>.</summary>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>Saves tags into <paramref name="path"/> using <paramref name="originalData"/> as the container.</summary>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (!TryRender (originalData.Span, out var rewritten, out var error))
			return Task.FromResult (FileWriteResult.Failure (error));

		fileSystem ??= _sourceFileSystem;
		return AtomicFileWriter.WriteAsync (path, rewritten, fileSystem, cancellationToken);
	}

	/// <summary>Saves tags into <paramref name="path"/>, copying the container from <see cref="SourcePath"/>.</summary>
	/// <remarks>Cluster bytes are copied in chunks. The span overload still builds one buffer.</remarks>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return Task.FromResult (SaveStreaming (path, fileSystem ?? _sourceFileSystem, cancellationToken));
	}

	// Cluster payloads stay on the source. The destination is not readable, so a CRC is hashed
	// before it is written.
	FileWriteResult SaveStreaming (string path, IFileSystem? fileSystem, CancellationToken cancellationToken)
	{
		OpenedFile? source = null;
		byte[]? chunk = null;
		try {
			if (!PathSave.TryOpen (SourcePath!, fileSystem, out source, out var openError))
				return openError;

			var opened = source!;
			chunk = StreamCopy.Rent ();
			List<TrackFlagEdit>? edits = null;
			foreach (var track in Tracks) {
				if (track.IsEnabledAssigned)
					(edits ??= new List<TrackFlagEdit> ()).Add (new TrackFlagEdit (track.Ordinal, track.TrackNumber, track.TrackUid, track.IsEnabled));
			}

			MatroskaRewrite.StreamPlan? plan;
			try {
				if (!MatroskaRewrite.TryPlanStream (opened.Stream, opened.Length, chunk, Tag, Chapters, edits, out plan, out var error))
					return FileWriteResult.Failure (error);
			} catch (UnauthorizedAccessException ex) {
				return FileWriteResult.Failure ($"Failed to re-read source file: Access denied: {ex.Message}");
			} catch (IOException ex) {
				return FileWriteResult.Failure ($"Failed to re-read source file: Failed to read file: {ex.Message}");
			}

			if (cancellationToken.IsCancellationRequested)
				return FileWriteResult.Failure ("Failed to re-read source file: Operation was cancelled");

			var copy = chunk;
			var rewrite = plan!;
			var written = PathSave.Replace (path, opened, output => MatroskaRewrite.WritePlan (opened.Stream, output, rewrite, copy));
			source = null;
			return written;
		} finally {
			source?.Dispose ();
			if (chunk is not null)
				StreamCopy.Return (chunk);
		}
	}

	/// <summary>Saves tags back to <see cref="SourcePath"/>.</summary>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>Reads a Matroska or WebM file from disk.</summary>
	public static MatroskaFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return MatroskaFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess)
			result.File!.SetSourcePath (path, fileSystem);
		return result;
	}

	/// <summary>Reads a Matroska or WebM file from disk.</summary>
	public static async Task<MatroskaFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return MatroskaFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess)
			result.File!.SetSourcePath (path, fileSystem);
		return result;
	}

	/// <inheritdoc/>
	public void Dispose ()
	{
	}

	internal void SetSourcePath (string? path, IFileSystem? fileSystem = null)
	{
		SourcePath = path;
		_sourceFileSystem = fileSystem;
	}

	static string? ReadDocType (ReadOnlySpan<byte> data, EbmlElement header)
	{
		var offset = header.DataOffset;
		var end = header.End;
		while (offset < end) {
			if (!EbmlReader.TryRead (data, offset, end, out var element) || element.UnknownSize)
				break;
			if (element.Id == MatroskaIds.DocType)
				return EbmlReader.ReadString (data.Slice (element.DataOffset, (int)element.DataLength));
			offset = element.End;
		}

		return null;
	}

	static void ReadInfo (ReadOnlySpan<byte> data, ReadOnlySpan<byte> body, ref ulong scale, ref double? durationTicks, MatroskaTag tag)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TimestampScale && payload.Length > 0)
				scale = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.Duration && EbmlReader.TryReadFloat (payload, out var duration))
				durationTicks = duration;
			else if (element.Id == MatroskaIds.SegmentTitle)
				tag.SetSegmentTitle (EbmlReader.ReadString (payload));
			offset = element.End;
		}
	}

	static void ReadTracks (ReadOnlySpan<byte> body, ref TrackLayout layout)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.TrackEntry && !element.UnknownSize)
				ReadTrackEntry (body.Slice (element.DataOffset, (int)element.DataLength), ref layout);
			if (element.UnknownSize)
				break;
			offset = element.End;
		}
	}

	static void ReadTrackEntry (ReadOnlySpan<byte> entry, ref TrackLayout layout)
	{
		ulong kind = 0;
		ulong number = 0;
		ulong uid = 0;
		string? codecId = null;
		string? codecName = null;
		var isDefault = true;
		var isEnabled = true;
		var privateOffset = -1;
		var privateLength = 0;
		var rate = 0;
		var outputRate = 0;
		var sawOutputRate = false;
		var sawAudio = false;
		var channels = 0;
		var bits = 0;
		var width = 0;
		var height = 0;
		double frameRate = 0;
		var offset = 0;
		while (offset < entry.Length) {
			if (!EbmlReader.TryRead (entry, offset, entry.Length, out var element) || element.UnknownSize)
				break;
			var payload = entry.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TrackType)
				kind = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.TrackNumber)
				number = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.TrackUid)
				uid = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.FlagDefault)
				isDefault = IsFlagSet (payload);
			else if (element.Id == MatroskaIds.FlagEnabled)
				isEnabled = IsFlagSet (payload);
			else if (element.Id == MatroskaIds.CodecId)
				codecId = EbmlReader.ReadString (payload).TrimEnd ('\0');
			else if (element.Id == MatroskaIds.CodecPrivate) {
				privateOffset = element.DataOffset;
				privateLength = (int)element.DataLength;
			} else if (element.Id == MatroskaIds.CodecName)
				codecName = EbmlReader.ReadString (payload).TrimEnd ('\0');
			else if (element.Id == MatroskaIds.DefaultDuration && payload.Length > 0) {
				var nanos = EbmlReader.ReadUInt (payload);
				if (nanos > 0)
					frameRate = 1_000_000_000d / nanos;
			} else if (element.Id == MatroskaIds.Audio) {
				sawAudio = true;
				ReadAudio (payload, ref rate, ref outputRate, ref sawOutputRate, ref channels, ref bits);
			} else if (element.Id == MatroskaIds.Video)
				ReadVideoSize (payload, ref width, ref height);
			offset = element.End;
		}

		var codecPrivate = privateOffset >= 0
			? entry.Slice (privateOffset, privateLength)
			: ReadOnlySpan<byte>.Empty;
		if (sawOutputRate)
			rate = outputRate;
		else if (codecId == "A_OPUS")
			rate = 48000;
		else if (sawAudio && rate == 0)
			rate = 8000;
		var codec = CodecLabel (codecId, codecName, codecPrivate);
		layout.Tracks ??= new List<MatroskaTrack> ();
		if (layout.Tracks.Count >= MaxTracks)
			layout.IsTrackListTruncated = true;
		else
			layout.Tracks.Add (new MatroskaTrack (layout.Tracks.Count, number, uid, kind, codecId, isDefault, isEnabled));
		// FlagEnabled and FlagDefault both default to 1 (RFC 9559 §5.1.4.1.4, §5.1.4.1.5).
		// An enabled track beats a disabled one; among those, a default track beats one that opted out.
		// The first track of the best rank is kept. When every track is disabled, the disabled ones still rank.
		var rank = (isEnabled ? 2 : 0) + (isDefault ? 1 : 0);
		if (kind == 2) {
			layout.Types |= MediaTypes.Audio;
			if (!layout.HasAudio || rank > layout.AudioRank) {
				layout.HasAudio = true;
				layout.AudioRank = rank;
				layout.SampleRate = rate;
				layout.Channels = channels;
				layout.BitsPerSample = bits;
				layout.AudioCodec = codec;
			}
		} else if (kind == 1) {
			layout.Types |= MediaTypes.Video;
			if (!layout.HasVideo || rank > layout.VideoRank) {
				layout.HasVideo = true;
				layout.VideoRank = rank;
				layout.Width = width;
				layout.Height = height;
				layout.FrameRate = frameRate;
				layout.VideoCodec = codec;
			}
		}
	}

	static void ReadAudio (ReadOnlySpan<byte> body, ref int rate, ref int outputRate, ref bool sawOutputRate, ref int channels, ref int bits)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.SamplingFrequency && EbmlReader.TryReadFloat (payload, out var frequency) && frequency > 0)
				rate = (int)frequency;
			else if (element.Id == MatroskaIds.OutputSamplingFrequency && EbmlReader.TryReadFloat (payload, out var output) && output > 0) {
				outputRate = (int)output;
				sawOutputRate = true;
			} else if (element.Id == MatroskaIds.Channels && payload.Length > 0)
				channels = (int)EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.BitDepth && payload.Length > 0)
				bits = (int)EbmlReader.ReadUInt (payload);
			offset = element.End;
		}

		// The Channels element defaults to 1 when an Audio element omits it.
		if (channels == 0)
			channels = 1;
	}

	static void ReadVideoSize (ReadOnlySpan<byte> body, ref int width, ref int height)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.PixelWidth)
				width = (int)EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.PixelHeight)
				height = (int)EbmlReader.ReadUInt (payload);
			offset = element.End;
		}
	}

	static string CodecLabel (string? codecId, string? codecName, ReadOnlySpan<byte> codecPrivate)
	{
		if (codecName is { Length: > 0 } name && name != codecId)
			return name;
		if (codecId == "V_MS/VFW/FOURCC" && TryVfwFourCc (codecPrivate, out var fourcc))
			return fourcc;
		return codecId switch {
			"A_VORBIS" => "Vorbis",
			"A_OPUS" => "Opus",
			"A_AAC" or "A_AAC/MPEG2/LC" or "A_AAC/MPEG4/LC" => "AAC",
			"A_MPEG/L3" => "MP3",
			"A_FLAC" => "FLAC",
			"A_PCM/INT/LIT" or "A_PCM/INT/BIG" => "PCM",
			"A_AC3" => "AC-3",
			"A_DTS" => "DTS",
			"V_VP8" => "VP8",
			"V_VP9" => "VP9",
			"V_AV1" => "AV1",
			"V_MPEG4/ISO/AVC" => "H.264",
			"V_MPEGH/ISO/HEVC" => "HEVC",
			"V_THEORA" => "Theora",
			_ => codecId is { Length: > 0 } id ? id : "Matroska"
		};
	}

	/// <summary>
	/// <c>V_MS/VFW/FOURCC</c> stores a BITMAPINFOHEADER. <c>biCompression</c> at offset 16 is the fourcc.
	/// </summary>
	static bool TryVfwFourCc (ReadOnlySpan<byte> header, out string name)
	{
		name = string.Empty;
		if (header.Length < 20)
			return false;
		var biSize = BinaryPrimitives.ReadUInt32LittleEndian (header);
		if (biSize < 20 || biSize > (uint)header.Length)
			return false;

		for (var i = 0; i < 4; i++) {
			var b = header[16 + i];
			if (b < 0x20 || b > 0x7E)
				return false;
		}

		var bytes = new byte[4];
		header.Slice (16, 4).CopyTo (bytes);
		var fourcc = Encoding.ASCII.GetString (bytes).Trim ();
		if (fourcc.Length == 0)
			return false;

		name = fourcc switch {
			"MP41" or "MP42" or "MP43" or "DIVX" or "DX50" or "XVID" or "FMP4" or "MP4V" or "M4S2" or "DIV3" or "3IV2" => "MPEG-4 Visual",
			_ => fourcc
		};
		return true;
	}

	static void ReadAttachments (ReadOnlySpan<byte> body, MatroskaTag tag)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.AttachedFile && !element.UnknownSize)
				ReadAttachedFile (body.Slice (element.DataOffset, (int)element.DataLength), tag);
			if (element.UnknownSize)
				break;
			offset = element.End;
		}
	}

	/// <summary>
	/// An image attachment is a picture. <see cref="MatroskaAttachments.TypeOf"/> maps its name to a type.
	/// </summary>
	static void ReadAttachedFile (ReadOnlySpan<byte> body, MatroskaTag tag)
	{
		var info = MatroskaAttachments.Read (body);
		if (!info.IsPicture)
			return;

		tag.AddPicture (new MatroskaPicture (
			info.MediaType!,
			info.PictureType,
			info.PictureDescription,
			body.Slice (info.DataOffset, info.DataLength).ToArray ()));
	}

	/// <summary>
	/// A 0/1 flag. An empty element is the default 1 (RFC 8794 §6.1, RFC 9559 §5.1.4.1.4).
	/// </summary>
	internal static bool IsFlagSet (ReadOnlySpan<byte> payload) =>
		payload.Length == 0 || EbmlReader.ReadUInt (payload) != 0;

	struct TrackLayout
	{
		public List<MatroskaTrack>? Tracks;
		public bool IsTrackListTruncated;
		public MediaTypes Types;
		public int SampleRate;
		public int Channels;
		public int BitsPerSample;
		public string? AudioCodec;
		public bool HasAudio;
		public int AudioRank;
		public int Width;
		public int Height;
		public double FrameRate;
		public string? VideoCodec;
		public bool HasVideo;
		public int VideoRank;
	}

	static void ReadTags (ReadOnlySpan<byte> data, ReadOnlySpan<byte> body, MatroskaTag tag)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.Tag && !element.UnknownSize) {
				var raw = body.Slice (element.Position, element.End - element.Position).ToArray ();
				var block = ReadTag (body.Slice (element.DataOffset, (int)element.DataLength));
				if (!block.IsFileLevel)
					block.KeepOriginal (raw);
				tag.AddBlock (block);
			}
			if (element.UnknownSize)
				break;
			offset = element.End;
		}
	}

	static TagBlock ReadTag (ReadOnlySpan<byte> body)
	{
		var block = new TagBlock (50, hasTargetTypeValue: false);
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.Targets)
				ReadTargets (payload, block);
			else if (element.Id == MatroskaIds.SimpleTag)
				block.Tags.Add (ReadSimpleTag (payload, 1));
			offset = element.End;
		}

		block.WasEmpty = block.Tags.Count == 0;
		return block;
	}

	static void ReadTargets (ReadOnlySpan<byte> body, TagBlock block)
	{
		var chapterAll = false;
		var chapterSpecific = false;
		var attachmentAll = false;
		var attachmentSpecific = false;
		var trackAll = false;
		var trackSpecific = false;
		var editionAll = false;
		var editionSpecific = false;
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TargetTypeValue) {
				block.HasTargetTypeValue = true;
				// A missing TargetTypeValue is not an explicit 50.
				// An empty element is present and means the spec default, 50.
				block.Level = payload.Length == 0 ? 50 : EbmlReader.ReadUInt (payload);
			} else {
				block.KeepTarget (body.Slice (element.Position, element.End - element.Position).ToArray ());
				if (element.Id == MatroskaIds.TagChapterUid)
					NoteTargetUid (payload, ref chapterAll, ref chapterSpecific);
				else if (element.Id == MatroskaIds.TagAttachmentUid) {
					NoteTargetUid (payload, ref attachmentAll, ref attachmentSpecific);
					var uid = EbmlReader.ReadUInt (payload);
					if (uid != 0)
						block.AttachmentUids.Add (uid);
				} else if (element.Id == MatroskaIds.TagTrackUid)
					NoteTargetUid (payload, ref trackAll, ref trackSpecific);
				else if (element.Id == MatroskaIds.TagEditionUid)
					NoteTargetUid (payload, ref editionAll, ref editionSpecific);
			}

			offset = element.End;
		}

		// A 0 means every item of that kind and sticks, whatever order the UIDs are in.
		// The block leaves the file only when every UID of that kind is nonzero.
		block.HasChapter = chapterSpecific && !chapterAll;
		block.HasAttachment = attachmentSpecific && !attachmentAll;
		block.HasTrack = trackSpecific && !trackAll;
		block.HasEdition = editionSpecific && !editionAll;
	}

	static void NoteTargetUid (ReadOnlySpan<byte> payload, ref bool all, ref bool specific)
	{
		if (EbmlReader.ReadUInt (payload) == 0)
			all = true;
		else
			specific = true;
	}

	/// <summary>
	/// The deepest <c>SimpleTag</c> nesting parsed into <see cref="SimpleTag.Children"/>. RFC 9559 §5.1.8.1.2
	/// lets a SimpleTag nest without a limit, so the reader stops descending here and keeps each deeper
	/// SimpleTag as its stored bytes, which a save writes back unchanged. No tag mapping reads that deep.
	/// </summary>
	internal const int MaxSimpleTagDepth = 64;

	static SimpleTag ReadSimpleTag (ReadOnlySpan<byte> body, int depth)
	{
		var name = string.Empty;
		string? text = null;
		string? language = null;
		string? languageBcp47 = null;
		byte[]? binary = null;
		var isDefault = true;
		var children = new List<SimpleTag> ();
		List<byte[]>? kept = null;
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TagName)
				name = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.TagString)
				text = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.TagLanguage)
				language = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.TagLanguageBcp47)
				languageBcp47 = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.TagBinary)
				binary = payload.ToArray ();
			else if (element.Id == MatroskaIds.TagDefault || element.Id == MatroskaIds.TagDefaultBogus)
				isDefault = IsFlagSet (payload);
			else if (element.Id == MatroskaIds.SimpleTag) {
				if (depth < MaxSimpleTagDepth)
					children.Add (ReadSimpleTag (payload, depth + 1));
				else
					(kept ??= new List<byte[]> ()).Add (body.Slice (element.Position, element.End - element.Position).ToArray ());
			}
			offset = element.End;
		}

		var tag = new SimpleTag (name, text, isDefault) {
			Language = language,
			LanguageBcp47 = languageBcp47,
			Binary = binary
		};
		foreach (var child in children)
			tag.Children.Add (child);
		if (kept is not null)
			tag.KeptChildren.AddRange (kept);
		return tag;
	}

	/// <summary>
	/// An unknown-size Segment ends at the next EBML header or Segment.
	/// A known-size Segment ends at the size it declares. RFC 8794.
	/// </summary>
	internal static int PayloadEnd (ReadOnlySpan<byte> data, EbmlElement segment) =>
		segment.UnknownSize ? EndAtNextTopLevel (data, segment.DataOffset) : segment.End;

	static int EndAtNextTopLevel (ReadOnlySpan<byte> data, int offset)
	{
		while (offset < data.Length) {
			if (!EbmlReader.TryRead (data, offset, data.Length, out var element))
				return data.Length;
			if (element.Id == MatroskaIds.Ebml || element.Id == MatroskaIds.Segment)
				return element.Position;
			if (element.UnknownSize) {
				if (!TrySkipUnknown (data, element.DataOffset, out var next))
					return data.Length;
				offset = next;
				continue;
			}

			offset = element.End;
		}

		return data.Length;
	}

	static bool TrySkipUnknown (ReadOnlySpan<byte> data, int offset, out int next)
	{
		next = offset;
		while (offset < data.Length) {
			if (!EbmlReader.TryRead (data, offset, data.Length, out var element))
				return false;
			if (MatroskaIds.IsSegmentChild (element.Id)
				|| element.Id == MatroskaIds.Ebml
				|| element.Id == MatroskaIds.Segment) {
				next = element.Position;
				return true;
			}

			if (element.UnknownSize)
				return false;
			offset = element.End;
		}

		next = data.Length;
		return true;
	}

	static ReadOnlySpan<byte> Range (ReadOnlySpan<byte> data, EbmlElement element, int parentEnd)
	{
		if (!element.UnknownSize)
			return data.Slice (element.DataOffset, (int)element.DataLength);

		var end = NextSibling (data, element.DataOffset, parentEnd);
		return data.Slice (element.DataOffset, end - element.DataOffset);
	}

	/// <summary>
	/// An unknown-size element ends at the next sibling ID, not at the next byte that looks like media.
	/// </summary>
	static int NextSibling (ReadOnlySpan<byte> data, int offset, int parentEnd)
	{
		while (offset < parentEnd) {
			if (!EbmlReader.TryRead (data, offset, parentEnd, out var element))
				return parentEnd;
			if (MatroskaIds.IsSegmentChild (element.Id))
				return element.Position;
			if (element.UnknownSize)
				return parentEnd;
			offset = element.End;
		}

		return parentEnd;
	}
}
