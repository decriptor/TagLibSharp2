// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Riff;

/// <summary>
/// An AVI file: a RIFF container with the <c>AVI </c> form type.
/// </summary>
/// <remarks>
/// <para>
/// The first RIFF form holds <c>LIST hdrl</c> (the main header and one <c>strl</c> per stream),
/// the <c>LIST movi</c> sample data, and an optional <c>idx1</c> index. OpenDML files add a
/// <c>LIST odml</c> with the total frame count, an <c>indx</c> super index per stream, and
/// <c>RIFF 'AVIX'</c> forms that continue the sample data.
/// </para>
/// <para>
/// Tags are read from every <c>LIST INFO</c> in the first form (the first list wins, a later
/// list fills empty fields, as in <see cref="WavFile"/>) and from an <c>ID32</c> or <c>id3 </c>
/// chunk holding an ID3v2 tag. The INFO list is the standard AVI tag and the one other tools read.
/// </para>
/// <para>
/// Saving never changes the <c>movi</c> sample bytes. The new tag chunks go into the space the
/// old tag chunks and <c>JUNK</c> chunks before <c>movi</c> used, so <c>movi</c> keeps its offset
/// and every index stays valid. When the tags do not fit, the data after the tags is moved and
/// every absolute offset that points past them is shifted: the RIFF size, <c>idx1</c> entries
/// when they are absolute, each <c>indx</c> super index entry, and each standard index's base offset.
/// A save that leaves the tags unchanged writes the original bytes.
/// </para>
/// <para>References:</para>
/// <list type="bullet">
/// <item>AVI RIFF File Reference: https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference</item>
/// <item>OpenDML AVI File Format Extensions, Version 1.02 (OpenDML AVI M-JPEG File Format Subcommittee, 1996)</item>
/// <item>Multimedia Programming Interface and Data Specifications 1.0 (IBM and Microsoft, 1991), RIFF and the INFO list</item>
/// </list>
/// </remarks>
public sealed class AviFile : IMediaFile
{
	/// <summary>
	/// A tag chunk larger than this is not read. An INFO list or an ID3v2 tag (at most 256 MiB) fits.
	/// </summary>
	internal const uint MaxTagChunkBytes = 256 * 1024 * 1024;

	IFileSystem? _sourceFileSystem;
	bool _disposed;

	AviFile (RiffInfoTag info, Id3v2Tag? id3, TimeSpan duration, AudioProperties? audio, VideoProperties? video)
	{
		InfoTag = info;
		Id3v2Tag = id3;
		Duration = duration;
		AudioProperties = audio;
		VideoProperties = video;
	}

	/// <summary>
	/// Gets the RIFF INFO tag. It is empty, not null, when the file has no <c>LIST INFO</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every INFO list in the first RIFF form is merged into this tag. Save writes one INFO list
	/// between <c>hdrl</c> and <c>movi</c> and turns the others into <c>JUNK</c>. Clearing the tag removes the list.
	/// </para>
	/// <para>
	/// INFO text is in the code page of the file's <c>CSET</c> chunk, ISO 8859-1 when there is none
	/// (Multimedia Programming Interface and Data Specifications 1.0, "CSET (Character Set) Chunk").
	/// A CSET of 65001 (UTF-8), 1252 (Windows-1252), 0 or 28591 (ISO 8859-1) is honoured. ffmpeg and
	/// TagLib# write UTF-8 without a CSET, so with no CSET valid UTF-8 is read as UTF-8 and other text
	/// as Windows-1252; another code page is read as a best effort.
	/// </para>
	/// <para>
	/// On save, a value that was not changed keeps its exact bytes, and the file keeps its CSET (or its
	/// absence). Changed values are written in the file's code page when they all fit it; with no CSET,
	/// or one this library does not encode, only ASCII fits. Otherwise the list becomes UTF-8 with a CSET
	/// of code page 65001 in front of it: unchanged UTF-8 and ASCII values keep their bytes, and unchanged
	/// text in a declared code page this library decodes exactly (1252, 28591, 0) is written as UTF-8 of
	/// the same text; the new CSET keeps the old one's country, language and dialect codes. The save is
	/// refused, with an error naming the field and the reason, rather than changing an unchanged value's
	/// text when its code page is a guess (no CSET and not UTF-8), is not decoded here (932 or 1251, say),
	/// or the value holds a byte its declared code page leaves undefined (0x81, 0x8D, 0x8F, 0x90 or 0x9D
	/// in 1252; 0x80-0x9F under code page 0). mediainfo ignores CSET.
	/// </para>
	/// </remarks>
	public RiffInfoTag InfoTag { get; }

	/// <summary>
	/// Gets or sets the ID3v2 tag stored in an <c>ID32</c> (or <c>id3 </c>) chunk, or null when there is none.
	/// </summary>
	/// <remarks>
	/// Few players read an ID3v2 tag in AVI. Assigning a tag adds an <c>ID32</c> chunk; assigning
	/// null removes the chunk the tag was read from. A chunk this reader could not parse is kept
	/// while this property stays null.
	/// </remarks>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>Gets the duration of the file.</summary>
	/// <remarks>
	/// Timed by the video stream's strh dwRate / dwScale. The frame count is the OpenDML
	/// <c>dmlh</c> dwTotalFrames when present, because avih dwTotalFrames counts only the first RIFF form.
	/// </remarks>
	public TimeSpan Duration { get; }

	/// <summary>Gets the first audio stream's properties, or null when the file has no audio stream.</summary>
	public AudioProperties? AudioProperties { get; }

	/// <summary>Gets the first video stream's properties, or null when the file has no video stream.</summary>
	/// <remarks><see cref="VideoProperties.Codec"/> is the BITMAPINFOHEADER biCompression FourCC, such as <c>FMP4</c> or <c>MJPG</c>.</remarks>
	public VideoProperties? VideoProperties { get; }

	/// <inheritdoc />
	public MediaTypes MediaTypes =>
		(AudioProperties is null ? MediaTypes.None : MediaTypes.Audio)
		| (VideoProperties is null ? MediaTypes.None : MediaTypes.Video);

	/// <summary>Gets the path the file was read from, when it was read from a path.</summary>
	public string? SourcePath { get; private set; }

	/// <summary>Gets <see cref="MediaFormat.Avi"/>.</summary>
	public MediaFormat Format => MediaFormat.Avi;

	/// <inheritdoc />
	/// <remarks>The INFO tag, combined with the ID3v2 tag when there is one. The INFO tag comes first.</remarks>
	Tag? IMediaFile.Tag => UnifiedTag;

	internal Tag UnifiedTag => Id3v2Tag is null ? InfoTag : new CombinedTag (InfoTag, Id3v2Tag);

	IMediaProperties? IMediaFile.AudioProperties => AudioProperties;

	ImageProperties? IMediaFile.ImageProperties => null;

	/// <summary>Returns true when the data starts with a RIFF header whose form type is <c>AVI </c>.</summary>
	/// <param name="data">The start of the file, at least 12 bytes.</param>
	public static bool IsValidFormat (ReadOnlySpan<byte> data) => AviChunks.IsAvi (data);

	/// <summary>Reads an AVI file from its bytes.</summary>
	/// <param name="data">The whole file, or at least every chunk before <c>movi</c>.</param>
	/// <returns>The parsed file, or a failure when the data is not an AVI file.</returns>
	public static AviFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (!IsValidFormat (data))
			return AviFileReadResult.Failure ("Not an AVI file: missing RIFF 'AVI ' header");

		RiffInfoTag? info = null;
		Id3v2Tag? id3 = null;
		var sawHeaders = false;
		var sawId3 = false;
		AviHeaders? headers = null;
		var characterSet = AviChunks.CharacterSet (data);
		var codePage = AviChunks.CodePage (data);
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var chunk)) {
			if (!AviChunks.IsReadable (chunk))
				continue;
			if (!sawHeaders && chunk.IsList (AviChunks.Hdrl)) {
				sawHeaders = true;
				headers = AviHeaders.Parse (AviChunks.Body (data, chunk).Slice (4));
			} else if (chunk.IsList (AviChunks.Info)) {
				var parsed = RiffInfoTag.Parse (new BinaryData (AviChunks.Body (data, chunk)), characterSet, codePage);
				if (parsed is null)
					continue;
				if (info is null)
					info = parsed;
				else
					info.FillMissingFrom (parsed);
			} else if (!sawId3 && chunk.IsId3) {
				sawId3 = true;
				var result = Id3v2Tag.Read (AviChunks.Body (data, chunk));
				if (result.IsSuccess)
					id3 = result.Tag;
			}
		}

		if (headers is null)
			return AviFileReadResult.Failure ("Invalid AVI file: no hdrl list");

		// A streaming writer that never finalized the file leaves RIFF size 0xFFFFFFFF and
		// placeholder frame counts in avih and strh, so the header lengths are not durations.
		if (AviChunks.IsUnfinalized (data))
			headers.ForgetLengths ();

		var duration = AviProperties.Duration (headers);
		return AviFileReadResult.Success (new AviFile (
			info ?? new RiffInfoTag (characterSet, codePage),
			id3,
			duration,
			AviProperties.Audio (headers, duration),
			AviProperties.Video (headers, duration)));
	}

	/// <summary>
	/// Reads an AVI file from a stream. Only the header list and the tag chunks of the first
	/// RIFF form are copied; <c>movi</c>, <c>idx1</c>, <c>JUNK</c> and any <c>AVIX</c> form stay in the stream.
	/// A later save reloads the file from <paramref name="sourcePath"/>.
	/// </summary>
	internal static AviFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return WithSource (Read (StreamBytes.ReadToEnd (stream)), sourcePath, fileSystem);

		if (stream.Position != 0)
			stream.Position = 0;

		var header = StreamBytes.ReadAtMost (stream, RiffFile.HeaderSize);
		if (!IsValidFormat (header))
			return AviFileReadResult.Failure ("Not an AVI file: missing RIFF 'AVI ' header");

		using var kept = new MemoryStream ();
		StreamBytes.Append (kept, header);
		var limit = AviChunks.FormEnd (BinaryPrimitives.ReadUInt32LittleEndian (header.AsSpan (4)), stream.Length);
		var probe = new byte[12];
		long position = RiffFile.HeaderSize;
		while (position + RiffChunk.HeaderSize <= limit) {
			stream.Position = position;
			var probed = StreamBytes.Read (stream, probe, 0, probe.Length);
			if (probed < RiffChunk.HeaderSize)
				break;

			var size = BinaryPrimitives.ReadUInt32LittleEndian (probe.AsSpan (4));
			var next = position + RiffChunk.HeaderSize + size + (size & 1);
			if (IsKept (probe, probed) && size <= MaxTagChunkBytes) {
				var length = Math.Min (next, limit) - position;
				stream.Position = position;
				var bytes = StreamBytes.ReadAtMost (stream, (int)length);
				StreamBytes.Append (kept, bytes);
				if (bytes.Length < length)
					break;
			}

			if (next >= limit)
				break;
			position = next;
		}

		return WithSource (Read (kept.GetBuffer ().AsSpan (0, (int)kept.Length)), sourcePath, fileSystem);
	}

	/// <summary>
	/// The asynchronous twin of <see cref="Read(Stream, string, IFileSystem)"/>: the same chunks are
	/// copied, with asynchronous reads.
	/// </summary>
	internal static async Task<AviFileReadResult> ReadAsync (
		Stream stream,
		string? sourcePath,
		IFileSystem? fileSystem,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		if (!stream.CanSeek)
			return WithSource (Read (await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false)), sourcePath, fileSystem);

		if (stream.Position != 0)
			stream.Position = 0;

		var header = await StreamBytes.ReadAtMostAsync (stream, RiffFile.HeaderSize, cancellationToken).ConfigureAwait (false);
		if (!IsValidFormat (header))
			return AviFileReadResult.Failure ("Not an AVI file: missing RIFF 'AVI ' header");

		using var kept = new MemoryStream ();
		StreamBytes.Append (kept, header);
		var limit = AviChunks.FormEnd (BinaryPrimitives.ReadUInt32LittleEndian (header.AsSpan (4)), stream.Length);
		var probe = new byte[12];
		long position = RiffFile.HeaderSize;
		while (position + RiffChunk.HeaderSize <= limit) {
			stream.Position = position;
			var probed = await StreamBytes.ReadAsync (stream, probe, 0, probe.Length, cancellationToken).ConfigureAwait (false);
			if (probed < RiffChunk.HeaderSize)
				break;

			var size = BinaryPrimitives.ReadUInt32LittleEndian (probe.AsSpan (4));
			var next = position + RiffChunk.HeaderSize + size + (size & 1);
			if (IsKept (probe, probed) && size <= MaxTagChunkBytes) {
				var length = Math.Min (next, limit) - position;
				stream.Position = position;
				var bytes = await StreamBytes.ReadAtMostAsync (stream, (int)length, cancellationToken).ConfigureAwait (false);
				StreamBytes.Append (kept, bytes);
				if (bytes.Length < length)
					break;
			}

			if (next >= limit)
				break;
			position = next;
		}

		return WithSource (Read (kept.GetBuffer ().AsSpan (0, (int)kept.Length)), sourcePath, fileSystem);
	}

	/// <summary>The chunks <see cref="Read(ReadOnlySpan{byte})"/> uses: <c>LIST hdrl</c>, <c>LIST INFO</c>, <c>CSET</c> (which decides how INFO text decodes), and an ID3v2 chunk.</summary>
	static bool IsKept (byte[] probe, int length)
	{
		var id = BinaryPrimitives.ReadUInt32LittleEndian (probe);
		if (id == AviChunks.Id32 || id == AviChunks.Id3Lower || id == AviChunks.Cset)
			return true;
		if (id != AviChunks.List || length < 12)
			return false;
		var listType = BinaryPrimitives.ReadUInt32LittleEndian (probe.AsSpan (8));
		return listType == AviChunks.Hdrl || listType == AviChunks.Info;
	}

	static AviFileReadResult WithSource (AviFileReadResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (result.IsSuccess && result.File is not null) {
			result.File.SourcePath = sourcePath;
			result.File._sourceFileSystem = fileSystem;
		}

		return result;
	}

	/// <summary>Reads an AVI file from its bytes without an error string.</summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">The parsed file when the read succeeds.</param>
	/// <returns>True when the data is an AVI file.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out AviFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>Reads an AVI file from disk.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The parsed file, or a failure.</returns>
	/// <remarks>Only the header list and the tag chunks are read; <c>movi</c> stays on disk.</remarks>
	public static AviFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (OpenFailure (path, fs) is { } missing)
			return missing;

		try {
			using var stream = fs.OpenRead (path);
			return Read (stream, path, fileSystem);
		} catch (IOException ex) {
			return AviFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return AviFileReadResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	/// <summary>Reads an AVI file from disk asynchronously.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The parsed file, or a failure.</returns>
	/// <remarks>Only the header list and the tag chunks are read; <c>movi</c> stays on disk.</remarks>
	public static async Task<AviFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (OpenFailure (path, fs) is { } missing)
			return missing;

		try {
			using var stream = fs.OpenRead (path);
			return await ReadAsync (stream, path, fileSystem, cancellationToken).ConfigureAwait (false);
		} catch (IOException ex) {
			return AviFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return AviFileReadResult.Failure ($"Access denied: {ex.Message}");
		} catch (OperationCanceledException) {
			return AviFileReadResult.Failure ("Operation was cancelled");
		}
	}

	static AviFileReadResult? OpenFailure (string path, IFileSystem fileSystem)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		return fileSystem.FileExists (path) ? null : AviFileReadResult.Failure ($"File not found: {path}");
	}

	/// <summary>
	/// Writes the current tags into a copy of <paramref name="originalData"/>.
	/// </summary>
	/// <param name="originalData">The file the tags are written into, normally the file this instance was read from.</param>
	/// <returns>The rewritten file, or empty when the tags cannot be written (see <see cref="SaveToFile(string, ReadOnlySpan{byte}, IFileSystem)"/>).</returns>
	public BinaryData Render (ReadOnlySpan<byte> originalData) =>
		AviRewrite.TryRewrite (originalData, InfoTag, Id3v2Tag, out var rewritten, out _)
			? BinaryData.WrapUnsafe (rewritten)
			: BinaryData.Empty;

	/// <summary>Saves the tags into <paramref name="path"/> using <paramref name="originalData"/> as the container.</summary>
	/// <param name="path">The destination path.</param>
	/// <param name="originalData">The file the tags are written into.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>
	/// The write result. Fails without writing when the data is not an AVI file, has no <c>movi</c>
	/// list, or the tags need more room and an index cannot be verified before the data is moved.
	/// </returns>
	/// <remarks>
	/// The rewritten file is built in memory: one copy of the file when nothing moves, a second when
	/// the data has to move. A path save copies the sample data in chunks instead. The RIFF size is
	/// 32-bit, so a file that would grow past that limit is refused.
	/// </remarks>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		if (!AviRewrite.TryRewrite (originalData, InfoTag, Id3v2Tag, out var rewritten, out var error))
			return FileWriteResult.Failure (error);

		return AtomicFileWriter.Write (path, rewritten, fileSystem ?? _sourceFileSystem);
	}

	/// <summary>Saves the tags into <paramref name="path"/>, copying the container from <see cref="SourcePath"/>.</summary>
	/// <param name="path">The destination path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The write result.</returns>
	/// <remarks>Sample bytes are copied in chunks. <see cref="Render"/> and the span overloads still build one buffer.</remarks>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveStreaming (path, fileSystem ?? _sourceFileSystem, CancellationToken.None);
	}

	/// <summary>Saves the tags back to <see cref="SourcePath"/>.</summary>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The write result.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>Saves the tags into <paramref name="path"/> using <paramref name="originalData"/> as the container.</summary>
	/// <param name="path">The destination path.</param>
	/// <param name="originalData">The file the tags are written into.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (!AviRewrite.TryRewrite (originalData.Span, InfoTag, Id3v2Tag, out var rewritten, out var error))
			return Task.FromResult (FileWriteResult.Failure (error));

		return AtomicFileWriter.WriteAsync (path, rewritten, fileSystem ?? _sourceFileSystem, cancellationToken);
	}

	/// <summary>Saves the tags into <paramref name="path"/>, copying the container from <see cref="SourcePath"/>.</summary>
	/// <param name="path">The destination path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	/// <remarks>Sample bytes are copied in chunks. The span overload still builds one buffer.</remarks>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return Task.FromResult (SaveStreaming (path, fileSystem ?? _sourceFileSystem, cancellationToken));
	}

	// Copies the file in chunks. movi payload is not read into one buffer. Indexes are patched
	// from values taken off the source, because the destination stream is not readable.
	FileWriteResult SaveStreaming (string path, IFileSystem? fileSystem, CancellationToken cancellationToken)
	{
		OpenedFile? source = null;
		byte[]? chunk = null;
		try {
			if (!PathSave.TryOpen (SourcePath!, fileSystem, out source, out var openError))
				return openError;

			var opened = source!;
			chunk = StreamCopy.Rent ();
			AviRewrite.StreamPlan? plan;
			try {
				if (!AviRewrite.TryPlanStream (opened.Stream, opened.Length, InfoTag, Id3v2Tag, chunk, out plan, out var error))
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
			var written = PathSave.Replace (path, opened, output => AviRewrite.WritePlan (opened.Stream, output, rewrite, copy));
			source = null;
			return written;
		} finally {
			source?.Dispose ();
			if (chunk is not null)
				StreamCopy.Return (chunk);
		}
	}

	/// <summary>Saves the tags back to <see cref="SourcePath"/> asynchronously.</summary>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>Releases the tags held by this instance.</summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		Id3v2Tag = null;
		_disposed = true;
	}
}

/// <summary>
/// The result of reading an AVI file.
/// </summary>
public readonly struct AviFileReadResult : IEquatable<AviFileReadResult>
{
	AviFileReadResult (AviFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>Gets the parsed file, or null when the read failed.</summary>
	public AviFile? File { get; }

	/// <summary>Gets the error message when the read failed.</summary>
	public string? Error { get; }

	/// <summary>Gets whether the read succeeded.</summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>Creates a successful result.</summary>
	/// <param name="file">The parsed file.</param>
	/// <returns>The result.</returns>
	public static AviFileReadResult Success (AviFile file) => new (file, null);

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">The error message.</param>
	/// <returns>The result.</returns>
	public static AviFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc />
	public bool Equals (AviFileReadResult other) => ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc />
	public override bool Equals (object? obj) => obj is AviFileReadResult other && Equals (other);

	/// <inheritdoc />
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>Determines whether two results are equal.</summary>
	/// <param name="left">The first result.</param>
	/// <param name="right">The second result.</param>
	/// <returns>True when both results hold the same file and error.</returns>
	public static bool operator == (AviFileReadResult left, AviFileReadResult right) => left.Equals (right);

	/// <summary>Determines whether two results differ.</summary>
	/// <param name="left">The first result.</param>
	/// <param name="right">The second result.</param>
	/// <returns>True when the results differ.</returns>
	public static bool operator != (AviFileReadResult left, AviFileReadResult right) => !left.Equals (right);
}
