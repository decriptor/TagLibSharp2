// Copyright (c) 2025 Stephen Shaw and contributors
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
public sealed class Mp3File
{
	const int Id3v1Size = 128;

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read"/>.
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
	public MpegAudioProperties? AudioProperties { get; private set; }

	/// <summary>
	/// Gets the duration of the audio. Convenience property that delegates to AudioProperties.
	/// </summary>
	public TimeSpan? Duration => AudioProperties?.Duration;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v1 tag.
	/// </summary>
	public bool HasId3v1Tag => Id3v1Tag is not null;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v2 tag.
	/// </summary>
	public bool HasId3v2Tag => Id3v2Tag is not null;

	/// <summary>
	/// Gets or sets the title. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public string? Title {
		get => Id3v2Tag?.Title ?? Id3v1Tag?.Title;
		set => EnsureId3v2Tag ().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public string? Artist {
		get => Id3v2Tag?.Artist ?? Id3v1Tag?.Artist;
		set => EnsureId3v2Tag ().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public string? Album {
		get => Id3v2Tag?.Album ?? Id3v1Tag?.Album;
		set => EnsureId3v2Tag ().Album = value;
	}

	/// <summary>
	/// Gets or sets the year. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public string? Year {
		get => Id3v2Tag?.Year ?? Id3v1Tag?.Year;
		set => EnsureId3v2Tag ().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public string? Genre {
		get => Id3v2Tag?.Genre ?? Id3v1Tag?.Genre;
		set => EnsureId3v2Tag ().Genre = value;
	}

	/// <summary>
	/// Gets or sets the track number. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public uint? Track {
		get => Id3v2Tag?.Track ?? Id3v1Tag?.Track;
		set => EnsureId3v2Tag ().Track = value;
	}

	/// <summary>
	/// Gets or sets the comment. Reads from ID3v2 first, then ID3v1. Sets to ID3v2.
	/// </summary>
	public string? Comment {
		get => Id3v2Tag?.Comment ?? Id3v1Tag?.Comment;
		set => EnsureId3v2Tag ().Comment = value;
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

		var result = Read (readResult.Data!);
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

		var result = Read (readResult.Data!);
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
			return Mp3FileReadResult.Failure ("Data too short for MP3 file");

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
			file.AudioProperties = audioProps;

		return Mp3FileReadResult.Success (file);
	}

	Id3v2Tag EnsureId3v2Tag ()
	{
		Id3v2Tag ??= new Id3v2Tag ();
		return Id3v2Tag;
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

		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken);
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

		return SaveToFile (path, readResult.Data!, fileSystem);
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
