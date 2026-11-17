// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Mpeg;

/// <summary>
/// Represents an MP3 audio file with its metadata tags.
/// </summary>
/// <remarks>
/// <para>
/// MP3 files can contain both ID3v2 tags (at the beginning) and ID3v1 tags (at the end).
/// This class provides unified access to metadata, preferring ID3v2 values when both exist.
/// </para>
/// <para>
/// File structure:
/// </para>
/// <code>
/// [ID3v2 tag] (optional, at beginning)
/// [MPEG audio frames...]
/// [ID3v1 tag] (optional, last 128 bytes)
/// </code>
/// </remarks>
public sealed class Mp3File : IMediaFile
{
	const int Id3v1Size = 128;
	bool _disposed;

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read(ReadOnlySpan{byte})"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	/// <summary>
	/// Gets or sets the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// May be null if the file has no ID3v2 tag. Will be automatically created
	/// when setting tag properties if no ID3v2 tag exists.
	/// </remarks>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>
	/// Gets or sets the ID3v1 tag.
	/// </summary>
	/// <remarks>
	/// May be null if the file has no ID3v1 tag.
	/// </remarks>
	public Id3v1Tag? Id3v1Tag { get; set; }

	/// <summary>
	/// Gets the size of the ID3v2 tag in bytes (0 if no tag).
	/// </summary>
	public int Id3v2Size { get; private set; }

	/// <summary>
	/// Gets the audio properties (duration, bitrate, sample rate, etc.).
	/// </summary>
	/// <remarks>
	/// For VBR files with Xing or VBRI headers, duration is calculated accurately.
	/// For CBR files, duration is estimated from file size.
	/// May be null if audio properties could not be determined.
	/// </remarks>
	public MpegAudioProperties? Properties { get; private set; }

	/// <summary>
	/// Gets the duration of the audio. Convenience property that delegates to Properties.
	/// </summary>
	public TimeSpan? Duration => Properties?.Duration;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v1 tag.
	/// </summary>
	public bool HasId3v1Tag => Id3v1Tag is not null;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v2 tag.
	/// </summary>
	public bool HasId3v2Tag => Id3v2Tag is not null;

	/// <inheritdoc />
	/// <remarks>
	/// Returns a <see cref="CombinedTag"/> composing the ID3v2 and ID3v1 tags when at
	/// least one is present. ID3v2 takes priority; ID3v1 supplies fallback values for
	/// fields not set in v2 (and receives mirrored writes so both tags stay in sync
	/// on save). Returns null when neither tag is present.
	/// </remarks>
	public Tag? Tag =>
		(Id3v2Tag is null && Id3v1Tag is null) ? null : new CombinedTag (Id3v2Tag, Id3v1Tag);

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is not null ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Mp3;

	/// <summary>
	/// Gets or sets the title. Same rules as <see cref="Tag"/>: ID3v2 wins when
	/// it has a value, otherwise ID3v1. The setter writes every tag the file already has.
	/// </summary>
	public string? Title {
		get => Tag?.Title;
		set => EnsureTag ().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist. Same rules as <see cref="Tag"/>.
	/// </summary>
	public string? Artist {
		get => Tag?.Artist;
		set => EnsureTag ().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album. Same rules as <see cref="Tag"/>.
	/// </summary>
	public string? Album {
		get => Tag?.Album;
		set => EnsureTag ().Album = value;
	}

	/// <summary>
	/// Gets or sets the year. Same rules as <see cref="Tag"/>.
	/// </summary>
	public string? Year {
		get => Tag?.Year;
		set => EnsureTag ().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre. Same rules as <see cref="Tag"/>.
	/// </summary>
	public string? Genre {
		get => Tag?.Genre;
		set => EnsureTag ().Genre = value;
	}

	/// <summary>
	/// Gets or sets the track number. Same rules as <see cref="Tag"/>.
	/// </summary>
	public uint? Track {
		get => Tag?.Track;
		set => EnsureTag ().Track = value;
	}

	/// <summary>
	/// Gets or sets the comment. Same rules as <see cref="Tag"/>.
	/// </summary>
	public string? Comment {
		get => Tag?.Comment;
		set => EnsureTag ().Comment = value;
	}

	/// <summary>
	/// Gets or sets the album artist. Only available in ID3v2.
	/// </summary>
	public string? AlbumArtist {
		get => Id3v2Tag?.AlbumArtist;
		set => EnsureId3v2Tag ().AlbumArtist = value;
	}

	/// <summary>
	/// Gets or sets the disc number. Only available in ID3v2.
	/// </summary>
	public uint? DiscNumber {
		get => Id3v2Tag?.DiscNumber;
		set => EnsureId3v2Tag ().DiscNumber = value;
	}

	/// <summary>
	/// Gets or sets the composer. Only available in ID3v2.
	/// </summary>
	public string? Composer {
		get => Id3v2Tag?.Composer;
		set => EnsureId3v2Tag ().Composer = value;
	}

	/// <summary>
	/// Gets or sets the BPM. Only available in ID3v2.
	/// </summary>
	public uint? BeatsPerMinute {
		get => Id3v2Tag?.BeatsPerMinute;
		set => EnsureId3v2Tag ().BeatsPerMinute = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain track gain. Only available in ID3v2.
	/// </summary>
	public string? ReplayGainTrackGain {
		get => Id3v2Tag?.ReplayGainTrackGain;
		set => EnsureId3v2Tag ().ReplayGainTrackGain = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain track peak. Only available in ID3v2.
	/// </summary>
	public string? ReplayGainTrackPeak {
		get => Id3v2Tag?.ReplayGainTrackPeak;
		set => EnsureId3v2Tag ().ReplayGainTrackPeak = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain album gain. Only available in ID3v2.
	/// </summary>
	public string? ReplayGainAlbumGain {
		get => Id3v2Tag?.ReplayGainAlbumGain;
		set => EnsureId3v2Tag ().ReplayGainAlbumGain = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain album peak. Only available in ID3v2.
	/// </summary>
	public string? ReplayGainAlbumPeak {
		get => Id3v2Tag?.ReplayGainAlbumPeak;
		set => EnsureId3v2Tag ().ReplayGainAlbumPeak = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Track ID. Only available in ID3v2.
	/// </summary>
	public string? MusicBrainzTrackId {
		get => Id3v2Tag?.MusicBrainzTrackId;
		set => EnsureId3v2Tag ().MusicBrainzTrackId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Release ID. Only available in ID3v2.
	/// </summary>
	public string? MusicBrainzReleaseId {
		get => Id3v2Tag?.MusicBrainzReleaseId;
		set => EnsureId3v2Tag ().MusicBrainzReleaseId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Artist ID. Only available in ID3v2.
	/// </summary>
	public string? MusicBrainzArtistId {
		get => Id3v2Tag?.MusicBrainzArtistId;
		set => EnsureId3v2Tag ().MusicBrainzArtistId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Release Group ID. Only available in ID3v2.
	/// </summary>
	public string? MusicBrainzReleaseGroupId {
		get => Id3v2Tag?.MusicBrainzReleaseGroupId;
		set => EnsureId3v2Tag ().MusicBrainzReleaseGroupId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Album Artist ID. Only available in ID3v2.
	/// </summary>
	public string? MusicBrainzAlbumArtistId {
		get => Id3v2Tag?.MusicBrainzAlbumArtistId;
		set => EnsureId3v2Tag ().MusicBrainzAlbumArtistId = value;
	}

	Mp3File ()
	{
	}

	/// <summary>
	/// Attempts to read an MP3 file from a file path.
	/// </summary>
	/// <param name="path">The path to the MP3 file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static Mp3FileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return Mp3FileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Asynchronously attempts to read an MP3 file from a file path.
	/// </summary>
	/// <param name="path">The path to the MP3 file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public static async Task<Mp3FileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return Mp3FileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Attempts to read an MP3 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static Mp3FileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < 4)
			return Mp3FileReadResult.Failure ("Invalid MP3 file: data too short");

		var file = new Mp3File ();
		var binaryData = new BinaryData (data);

		// Try to read ID3v2 from the beginning
		var id3v2Result = Id3v2Tag.Read (data);
		if (id3v2Result.IsSuccess) {
			file.Id3v2Tag = id3v2Result.Tag;
			file.Id3v2Size = id3v2Result.BytesConsumed;
		}

		// Try to read ID3v1 from the end (last 128 bytes)
		if (data.Length >= Id3v1Size) {
			var id3v1Data = data.Slice (data.Length - Id3v1Size, Id3v1Size);
			var id3v1Result = Id3v1Tag.Read (id3v1Data);
			if (id3v1Result.IsSuccess)
				file.Id3v1Tag = id3v1Result.Tag;
		}

		// Parse audio properties (duration, bitrate, etc.)
		// Audio starts after ID3v2 tag
		if (MpegAudioProperties.TryParse (binaryData, file.Id3v2Size, out var audioProps))
			file.Properties = audioProps;

		return Mp3FileReadResult.Success (file);
	}

	/// <summary>
	/// Reads an MP3 from a stream. A seekable stream supplies the tag, the first
	/// audio frames, and the ID3v1 footer. The bytes between them stay unread.
	/// </summary>
	internal static Mp3FileReadResult Read (Stream stream)
	{
		if (!stream.CanSeek)
			return Read (StreamBytes.ReadToEnd (stream));

		return ReadWindow (stream, static (source, count) => StreamBytes.ReadAtMost (source, count));
	}

	internal static async Task<Mp3FileReadResult> ReadAsync (Stream stream, CancellationToken cancellationToken)
	{
		if (!stream.CanSeek)
			return Read (await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false));

		if (stream.Position != 0)
			stream.Position = 0;

		var length = stream.Length;
		if (length < 4)
			return Mp3FileReadResult.Failure ("Invalid MP3 file: data too short");

		var window = new Mp3Window (length);
		while (true) {
			cancellationToken.ThrowIfCancellationRequested ();
			switch (window.Next (stream.Position, out var count, out var seek)) {
				case Mp3WindowStep.ReadPrefix:
					window.AddPrefix (await StreamBytes.ReadAtMostAsync (stream, count, cancellationToken).ConfigureAwait (false));
					break;
				case Mp3WindowStep.ReadTail:
					var asyncTail = await StreamBytes.ReadAtMostAsync (stream, count, cancellationToken).ConfigureAwait (false);
					if (TryFinishWindow (window, asyncTail, out var asyncParsed))
						return asyncParsed;
					stream.Position = 0;
					return Read (await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false));
				case Mp3WindowStep.Seek:
					stream.Position = seek;
					window.NoteSeek ();
					break;
				default:
					return Read (window.Prefix ());
			}
		}
	}

	static Mp3FileReadResult ReadWindow (Stream stream, Func<Stream, int, byte[]> read)
	{
		if (stream.Position != 0)
			stream.Position = 0;

		var length = stream.Length;
		if (length < 4)
			return Mp3FileReadResult.Failure ("Invalid MP3 file: data too short");

		var window = new Mp3Window (length);
		while (true) {
			switch (window.Next (stream.Position, out var count, out var seek)) {
				case Mp3WindowStep.ReadPrefix:
					window.AddPrefix (read (stream, count));
					break;
				case Mp3WindowStep.ReadTail:
					if (TryFinishWindow (window, read (stream, count), out var parsed))
						return parsed;
					stream.Position = 0;
					return Read (StreamBytes.ReadToEnd (stream));
				case Mp3WindowStep.Seek:
					stream.Position = seek;
					window.NoteSeek ();
					break;
				default:
					return Read (window.Prefix ());
			}
		}
	}

	static bool TryFinishWindow (Mp3Window window, byte[] tail, out Mp3FileReadResult result)
	{
		result = ReadWindows (window.Prefix (), tail, window.Length, out var needsEntireFile);
		return !needsEntireFile;
	}

	enum Mp3WindowStep
	{
		ReadPrefix,
		ReadTail,
		Seek,
		ParsePrefix
	}

	/// <summary>
	/// The seekable MP3 read, shared by the sync and async entry points.
	/// The audio between the probe and the ID3v1 footer stays unread.
	/// </summary>
	sealed class Mp3Window
	{
		const int AudioProbeBytes = 32 * 1024;

		readonly List<byte> _prefix = [];
		readonly long _length;
		int _stage;

		public Mp3Window (long length) => _length = length;

		public long Length => _length;

		public byte[] Prefix () => _prefix.ToArray ();

		public Mp3WindowStep Next (long position, out int count, out long seek)
		{
			count = 0;
			seek = 0;
			switch (_stage) {
				case 0:
					count = Id3v2Header.HeaderSize;
					return Mp3WindowStep.ReadPrefix;
				case 1:
					count = Id3Remainder ();
					if (count > 0)
						return Mp3WindowStep.ReadPrefix;
					_stage = 2;
					goto case 2;
				case 2:
					count = AudioProbeBytes;
					return Mp3WindowStep.ReadPrefix;
				case 3:
					if (position < _length && _length - position <= Id3v1Size) {
						count = (int)(_length - position);
						return Mp3WindowStep.ReadPrefix;
					}

					_stage = 4;
					goto case 4;
				case 4:
					if (position >= _length)
						return Mp3WindowStep.ParsePrefix;
					seek = _length - Id3v1Size;
					return Mp3WindowStep.Seek;
				default:
					count = Id3v1Size;
					return Mp3WindowStep.ReadTail;
			}
		}

		public void AddPrefix (byte[] data)
		{
			if (data.Length > 0)
				_prefix.AddRange (data);
			_stage++;
		}

		public void NoteSeek () => _stage++;

		int Id3Remainder ()
		{
			var header = Id3v2Header.Read (_prefix.ToArray ());
			if (!header.IsSuccess)
				return 0;
			var total = Math.Min ((long)header.Header.TotalSize, _length);
			return (int)Math.Max (0, total - _prefix.Count);
		}
	}

	static Mp3FileReadResult ReadWindows (byte[] prefix, byte[] tail, long fileLength, out bool needsEntireFile)
	{
		needsEntireFile = false;
		var file = new Mp3File ();
		var id3v2Result = Id3v2Tag.Read (prefix);
		if (id3v2Result.IsSuccess) {
			file.Id3v2Tag = id3v2Result.Tag;
			file.Id3v2Size = id3v2Result.BytesConsumed;
		}

		if (tail.Length >= Id3v1Size) {
			var id3v1Result = Id3v1Tag.Read (tail.AsSpan (tail.Length - Id3v1Size, Id3v1Size));
			if (id3v1Result.IsSuccess)
				file.Id3v1Tag = id3v1Result.Tag;
		}

		if (MpegAudioProperties.TryParse (new BinaryData (prefix), file.Id3v2Size, fileLength, out var audioProps, out needsEntireFile))
			file.Properties = audioProps;
		else if (needsEntireFile) {
			file.Dispose ();
			return Mp3FileReadResult.Failure ("The MPEG frame count has to be read from the audio");
		}

		return Mp3FileReadResult.Success (file);
	}

	/// <summary>
	/// Attempts to read an MP3 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out Mp3File? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read an MP3 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out Mp3File? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid MP3 file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with ID3 magic or a complete MPEG frame.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		if (data.Length < 2)
			return false;

		// Check for ID3v2 magic "ID3"
		if (data.Length >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
			return true;

		return MpegFrame.IsStartOfAudio (data);
	}

	Id3v2Tag EnsureId3v2Tag ()
	{
		Id3v2Tag ??= new Id3v2Tag ();
		return Id3v2Tag;
	}

	// A file with no tag gets an ID3v2 tag, which can store the full field.
	// A file that already has only ID3v1 keeps that tag; the combined view
	// writes through to it instead of leaving the footer stale.
	Tag EnsureTag ()
	{
		if (Id3v2Tag is null && Id3v1Tag is null)
			Id3v2Tag = new Id3v2Tag ();
		return Tag!;
	}

	/// <summary>
	/// Renders the complete MP3 file with updated tags.
	/// </summary>
	/// <param name="originalData">The original file data.</param>
	/// <returns>The rendered file data.</returns>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		// Calculate audio data bounds
		var audioStart = Id3v2Size;
		var audioEnd = HasId3v1Tag
			? originalData.Length - Id3v1Size
			: originalData.Length;

		var audioLength = audioEnd - audioStart;
		if (audioLength < 0)
			audioLength = 0;

		// Render new ID3v2 tag
		var newId3v2Data = Id3v2Tag?.Render () ?? BinaryData.Empty;

		// Render new ID3v1 tag (if present)
		var newId3v1Data = Id3v1Tag?.Render () ?? BinaryData.Empty;

		// Build new file
		var totalSize = newId3v2Data.Length + audioLength + newId3v1Data.Length;
		using var builder = new BinaryDataBuilder (totalSize);

		if (newId3v2Data.Length > 0)
			builder.Add (newId3v2Data);

		if (audioLength > 0)
			builder.Add (originalData.Slice (audioStart, audioLength));

		if (newId3v1Data.Length > 0)
			builder.Add (newId3v1Data);

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
			return FileWriteResult.Failure ("Failed to render MP3 file");

		fileSystem ??= _sourceFileSystem;
		return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
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
			return Task.FromResult (FileWriteResult.Failure ("Failed to render MP3 file"));

		fileSystem ??= _sourceFileSystem;
		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path asynchronously, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// This convenience method re-reads the original file data from <see cref="SourcePath"/>
	/// and saves to the specified path. Requires that the file was read using
	/// <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return await SaveToFileAsync (path, readResult.Data!.Value, fileSystem, cancellationToken)
			.ConfigureAwait (false);
	}

	/// <summary>
	/// Saves the file back to its source path asynchronously.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
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
	/// Saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// This convenience method re-reads the original file data from <see cref="SourcePath"/>
	/// and saves to the specified path. Requires that the file was read using
	/// <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return SaveToFile (path, readResult.Data!.Value.Span, fileSystem);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Releases resources held by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		Id3v2Tag = null;
		Id3v1Tag = null;
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}

/// <summary>
/// Represents the result of reading an MP3 file.
/// </summary>
public readonly struct Mp3FileReadResult : IEquatable<Mp3FileReadResult>
{
	/// <summary>
	/// Gets the parsed MP3 file, or null if parsing failed.
	/// </summary>
	public Mp3File? File { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	Mp3FileReadResult (Mp3File? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static Mp3FileReadResult Success (Mp3File file) => new (file, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static Mp3FileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (Mp3FileReadResult other) =>
		ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is Mp3FileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (Mp3FileReadResult left, Mp3FileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (Mp3FileReadResult left, Mp3FileReadResult right) =>
		!left.Equals (right);
}
