// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

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
public sealed class WavFile
{
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

	readonly RiffFile _riff;

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
	/// Gets the audio properties.
	/// </summary>
	public AudioProperties? Properties { get; private set; }

	/// <summary>
	/// Gets whether this file was successfully parsed.
	/// </summary>
	public bool IsValid => _riff.IsValid && Properties is not null;

	WavFile (RiffFile riff)
	{
		_riff = riff;
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
	/// Reads a WAV file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>The parsed WAV file, or null if invalid.</returns>
	public static WavFile? ReadFromData (BinaryData data)
	{
		if (!RiffFile.TryParse (data, out var riff))
			return null;

		if (riff.FormType != "WAVE")
			return null;

		var wav = new WavFile (riff);

		// Parse fmt chunk for audio properties
		var fmtChunk = riff.GetChunk (FmtChunkId);
		var dataChunk = riff.GetChunk (DataChunkId);

		if (fmtChunk.HasValue) {
			var dataSize = dataChunk?.DataSize ?? 0;
			wav.Properties = WavAudioPropertiesParser.Parse (fmtChunk.Value.Data, dataSize);
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

		return wav;
	}

	/// <summary>
	/// Reads a WAV file from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The parsed WAV file, or null on error.</returns>
	public static WavFile? ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var result = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!result.IsSuccess || result.Data is null)
			return null;

		return ReadFromData (new BinaryData (result.Data));
	}

	/// <summary>
	/// Reads a WAV file from a file path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The parsed WAV file, or null on error.</returns>
	public static async Task<WavFile?> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var result = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);

		if (!result.IsSuccess || result.Data is null)
			return null;

		return ReadFromData (new BinaryData (result.Data));
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

		// Start with a copy of the RIFF structure
		var riff = new RiffFile ();

		// Copy essential chunks from original (fmt and data)
		var fmtChunk = _riff.GetChunk (FmtChunkId);
		var dataChunk = _riff.GetChunk (DataChunkId);

		if (fmtChunk.HasValue)
			riff.SetChunk (fmtChunk.Value);

		if (dataChunk.HasValue)
			riff.SetChunk (dataChunk.Value);

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
