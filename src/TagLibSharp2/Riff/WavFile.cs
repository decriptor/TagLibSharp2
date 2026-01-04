// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Riff;

/// <summary>
/// Represents a WAV audio file with its metadata.
/// </summary>
/// <remarks>
/// <para>
/// WAV files are RIFF containers with "WAVE" form type. They contain:
/// </para>
/// <list type="bullet">
/// <item><description>fmt chunk - audio format information (required)</description></item>
/// <item><description>data chunk - audio samples (required)</description></item>
/// <item><description>LIST INFO chunk - native metadata (optional)</description></item>
/// <item><description>id3  chunk - ID3v2 tag for richer metadata (optional)</description></item>
/// </list>
/// <para>
/// Reference: Microsoft RIFF Specification
/// </para>
/// </remarks>
public sealed class WavFile : IMediaFile
{
	bool _disposed;

	/// <summary>
	/// FourCC for the fmt (format) chunk.
	/// </summary>
	public const string FmtChunkId = "fmt ";

	/// <summary>
	/// FourCC for the data chunk.
	/// </summary>
	public const string DataChunkId = "data";

	/// <summary>
	/// FourCC for the LIST chunk.
	/// </summary>
	public const string ListChunkId = "LIST";

	/// <summary>
	/// FourCC for the ID3v2 chunk.
	/// </summary>
	public const string Id3ChunkId = "id3 ";

	/// <summary>
	/// FourCC for the BWF bext chunk.
	/// </summary>
	public const string BextChunkId = "bext";

	RiffFile _riff;

	/// <summary>
	/// Gets or sets the RIFF INFO tag.
	/// </summary>
	/// <remarks>
	/// This is the native metadata format for WAV files.
	/// May be null if no LIST INFO chunk is present.
	/// </remarks>
	public RiffInfoTag? InfoTag { get; set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// ID3v2 tags provide richer metadata than RIFF INFO.
	/// May be null if no id3 chunk is present.
	/// </remarks>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>
	/// Gets or sets the BWF bext tag (Broadcast Wave Extension).
	/// </summary>
	/// <remarks>
	/// The bext chunk contains broadcast metadata including description,
	/// originator, timestamps, and time reference. Commonly used in
	/// professional audio production.
	/// May be null if no bext chunk is present.
	/// </remarks>
	public BextTag? BextTag { get; set; }

	/// <summary>
	/// Gets the extended audio properties from WAVEFORMATEXTENSIBLE.
	/// </summary>
	/// <remarks>
	/// Only populated if the file uses WAVEFORMATEXTENSIBLE format (0xFFFE).
	/// Contains speaker channel mapping and actual sub-format.
	/// </remarks>
	public WavExtendedProperties? ExtendedProperties { get; private set; }

	/// <summary>
	/// Gets the audio properties.
	/// </summary>
	public AudioProperties? Properties { get; private set; }

	/// <summary>
	/// Gets whether this file was successfully parsed.
	/// </summary>
	public bool IsValid => _riff?.IsValid == true && Properties is not null;

	/// <summary>
	/// Gets the source file path (not tracked for WAV files parsed from data).
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <inheritdoc />
	Tag? IMediaFile.Tag => (Tag?)Id3v2Tag ?? InfoTag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Wav;

	WavFile (RiffFile riff)
	{
		_riff = riff;
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		InfoTag = null;
		Id3v2Tag = null;
		BextTag = null;
		Properties = null;
		ExtendedProperties = null;
		_riff = null!;
		_disposed = true;
	}

	/// <summary>
	/// Gets the title from available tags, preferring ID3v2.
	/// </summary>
	public string? Title => Id3v2Tag?.Title ?? InfoTag?.Title;

	/// <summary>
	/// Gets the performers/artists from available tags, preferring ID3v2.
	/// </summary>
#pragma warning disable CA1819 // Properties should not return arrays - API compatibility
	public string[]? Performers => Id3v2Tag?.Performers ?? InfoTag?.Performers;
#pragma warning restore CA1819

	/// <summary>
	/// Gets the album from available tags, preferring ID3v2.
	/// </summary>
	public string? Album => Id3v2Tag?.Album ?? InfoTag?.Album;

	/// <summary>
	/// Gets the year from available tags, preferring ID3v2.
	/// </summary>
	public string? Year => Id3v2Tag?.Year ?? InfoTag?.Year;

	/// <summary>
	/// Gets the track number from available tags, preferring ID3v2.
	/// </summary>
	public uint? Track => Id3v2Tag?.Track ?? InfoTag?.Track;

	/// <summary>
	/// Gets the comment from available tags, preferring ID3v2.
	/// </summary>
	public string? Comment => Id3v2Tag?.Comment ?? InfoTag?.Comment;

	/// <summary>
	/// Gets the genres from available tags, preferring ID3v2.
	/// </summary>
#pragma warning disable CA1819 // Properties should not return arrays - API compatibility
	public string[]? Genres => Id3v2Tag?.Genres ?? InfoTag?.Genres;
#pragma warning restore CA1819

	/// <summary>
	/// Gets a value indicating whether this file has embedded pictures.
	/// </summary>
	public bool HasPictures => Id3v2Tag?.HasPictures ?? false;

	/// <summary>
	/// Gets the embedded pictures from the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// Pictures are only available when an ID3v2 tag is present.
	/// Returns an empty array if no ID3v2 tag exists.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - API compatibility
	public IPicture[] Pictures => Id3v2Tag?.Pictures ?? [];
#pragma warning restore CA1819

	/// <summary>
	/// Gets the front cover art from the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// Returns the first picture with type <see cref="PictureType.FrontCover"/>,
	/// or null if no front cover exists or no ID3v2 tag is present.
	/// </remarks>
	public PictureFrame? CoverArt => Id3v2Tag?.CoverArt;

	/// <summary>
	/// Reads a WAV file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>The parsed WAV file, or null if invalid.</returns>
	public static WavFile? ReadFromData (BinaryData data) =>
		Read (data.Span).File;

	/// <summary>
	/// Reads a WAV file from binary data with detailed error information.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static WavFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (!RiffFile.TryParse (new BinaryData (data.ToArray ()), out var riff))
			return WavFileReadResult.Failure ("Invalid RIFF file structure");

		if (riff.FormType != "WAVE")
			return WavFileReadResult.Failure ($"Invalid form type (expected 'WAVE', got '{riff.FormType}')");

		var wav = new WavFile (riff);

		// Parse fmt chunk for audio properties
		var fmtChunk = riff.GetChunk (FmtChunkId);
		var dataChunk = riff.GetChunk (DataChunkId);

		if (fmtChunk.HasValue) {
			var dataSize = dataChunk?.DataSize ?? 0;
			wav.Properties = WavAudioPropertiesParser.Parse (fmtChunk.Value.Data, dataSize);

			// Parse WAVEFORMATEXTENSIBLE if present
			wav.ExtendedProperties = WavAudioPropertiesParser.ParseExtended (fmtChunk.Value.Data);
		}

		// Parse LIST INFO chunk
		foreach (var listChunk in riff.GetChunks (ListChunkId)) {
			// Check if this is an INFO list
			if (listChunk.Data.Length >= 4) {
				var listType = listChunk.Data.Slice (0, 4).ToStringLatin1 ();
				if (listType == "INFO") {
					wav.InfoTag = RiffInfoTag.Parse (listChunk.Data);
					break;
				}
			}
		}

		// Parse ID3v2 chunk
		var id3Chunk = riff.GetChunk (Id3ChunkId);
		if (id3Chunk.HasValue && id3Chunk.Value.Data.Length > 0) {
			var id3Result = Id3v2Tag.Read (id3Chunk.Value.Data.Span);
			if (id3Result.IsSuccess)
				wav.Id3v2Tag = id3Result.Tag;
		}

		// Parse bext chunk (BWF Broadcast Extension)
		var bextChunk = riff.GetChunk (BextChunkId);
		if (bextChunk.HasValue && bextChunk.Value.Data.Length >= BextTag.MinimumSize)
			wav.BextTag = Riff.BextTag.Parse (bextChunk.Value.Data);

		return WavFileReadResult.Success (wav);
	}

	/// <summary>
	/// Reads a WAV file from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static WavFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return WavFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess)
			result.File!.SourcePath = path;
		return result;
	}

	/// <summary>
	/// Reads a WAV file from a file path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static async Task<WavFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);

		if (!readResult.IsSuccess)
			return WavFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess)
			result.File!.SourcePath = path;
		return result;
	}

	/// <summary>
	/// Renders the WAV file to binary data.
	/// </summary>
	/// <returns>The complete WAV file as binary data.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the file is not valid.</exception>
	public BinaryData Render ()
	{
		if (!IsValid)
			throw new InvalidOperationException ("Cannot render invalid WAV file");

		// Start with a new RIFF structure
		var riff = new RiffFile ();

		// Copy all chunks from original, except metadata chunks we'll update
		foreach (var chunk in _riff.AllChunks) {
			// Skip chunks we'll replace with updated versions
			if (chunk.FourCC == ListChunkId || chunk.FourCC == Id3ChunkId || chunk.FourCC == BextChunkId)
				continue;

			riff.SetChunk (chunk);
		}

		// Add bext tag if present
		if (BextTag is not null && !BextTag.IsEmpty) {
			var bextData = BextTag.Render ();
			if (bextData.Length > 0)
				riff.SetChunk (new RiffChunk (BextChunkId, bextData));
		}

		// Add INFO tag if present
		if (InfoTag is not null && !InfoTag.IsEmpty) {
			var infoData = InfoTag.Render ();
			if (infoData.Length > 0) {
				// RiffInfoTag.Render() returns complete LIST chunk
				// We need to extract just the data portion
				if (infoData.Length > 8) {
					var listData = infoData.Slice (8); // Skip "LIST" + size
					riff.SetChunk (new RiffChunk (ListChunkId, listData));
				}
			}
		}

		// Add ID3v2 tag if present
		if (Id3v2Tag is not null && !Id3v2Tag.IsEmpty) {
			var id3Data = Id3v2Tag.Render ();
			if (id3Data.Length > 0)
				riff.SetChunk (new RiffChunk (Id3ChunkId, id3Data));
		}

		return riff.Render ("WAVE");
	}

	/// <summary>
	/// Saves the WAV file to the specified path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The write result.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		var data = Render ();
		return AtomicFileWriter.Write (path, data.Span, fileSystem);
	}

	/// <summary>
	/// Saves the WAV file to the specified path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var data = Render ();
		return AtomicFileWriter.WriteAsync (path, data.Memory, fileSystem, cancellationToken);
	}
}

/// <summary>
/// Represents the result of reading a WAV file.
/// </summary>
public readonly struct WavFileReadResult : IEquatable<WavFileReadResult>
{
	/// <summary>
	/// Gets the parsed WAV file, or null if parsing failed.
	/// </summary>
	public WavFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets whether the parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null;

	WavFileReadResult (WavFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static WavFileReadResult Success (WavFile file) => new (file, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static WavFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (WavFileReadResult other) =>
		ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is WavFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (WavFileReadResult left, WavFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (WavFileReadResult left, WavFileReadResult right) =>
		!left.Equals (right);
}
