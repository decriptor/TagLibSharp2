// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
	IFileSystem? _sourceFileSystem;

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
	/// Every LIST INFO is merged into this tag. The first list wins, and save writes exactly one INFO list.
	/// </remarks>
	public RiffInfoTag? InfoTag { get; set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// ID3v2 tags provide richer metadata than RIFF INFO.
	/// May be null if no id3 chunk is present.
	/// Assigning null removes an id3 chunk that was parsed into this property.
	/// An id3 chunk this reader could not parse is kept only while this property
	/// stays null. Assigning an <see cref="Id3v2Tag"/>, including <c>new Id3v2Tag()</c>,
	/// replaces that unparsed chunk.
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
	/// Assigning null removes a bext chunk that was parsed into this property.
	/// A bext chunk shorter than <see cref="BextTag.MinimumSize"/> is kept only
	/// while this property stays null. Assigning a <see cref="BextTag"/>,
	/// including <c>new BextTag()</c>, replaces that unparsed chunk.
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
	public AudioProperties Properties { get; private set; }

	/// <summary>
	/// Gets whether this file was successfully parsed.
	/// </summary>
	/// <remarks>
	/// A file is valid if the RIFF structure is valid and the fmt chunk was parsed.
	/// This does not require audio data to be present (Duration may be zero).
	/// </remarks>
	public bool IsValid => _riff?.IsValid == true && Properties.SampleRate > 0;

	/// <summary>
	/// Gets the source file path (not tracked for WAV files parsed from data).
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <inheritdoc />
	Tag? IMediaFile.Tag => (Tag?)Id3v2Tag ?? InfoTag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is { IsValid: true } ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Wav;

	/// <summary>
	/// True when an id3 chunk was present and <see cref="Id3v2Tag.Read"/> did not accept it.
	/// A parsed tag that the caller later clears is not this case.
	/// </summary>
	bool _keepUnparsedId3;

	/// <summary>
	/// True when a bext chunk was present and shorter than <see cref="BextTag.MinimumSize"/>.
	/// </summary>
	bool _keepUnparsedBext;

	/// <summary>
	/// The <see cref="BextTag"/> instance produced by <see cref="Read"/>.
	/// Original bytes are copied only while <see cref="BextTag"/> is still this instance.
	/// </summary>
	BextTag? _parsedBext;

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
		Properties = default;
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
	/// Reads a WAV file from binary data with detailed error information.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static WavFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (!RiffFile.TryParse (new BinaryData (data.ToArray ()), out var riff))
			return WavFileReadResult.Failure ("Invalid WAV file: invalid RIFF structure");

		if (riff.FormType != "WAVE")
			return WavFileReadResult.Failure ($"Invalid WAV file: invalid form type (expected 'WAVE', got '{riff.FormType}')");

		var wav = new WavFile (riff);

		// Parse fmt chunk for audio properties
		var fmtChunk = riff.GetChunk (FmtChunkId);
		var dataChunk = riff.GetChunk (DataChunkId);

		if (fmtChunk.HasValue) {
			// Duration uses the sample bytes held. A declared ckSize larger than
			// those bytes is not the audio that was parsed.
			var dataBytes = dataChunk is { } present ? present.Data.Length : 0;
			wav.Properties = WavAudioPropertiesParser.Parse (fmtChunk.Value.Data, dataBytes) ?? default;

			// Parse WAVEFORMATEXTENSIBLE if present
			wav.ExtendedProperties = WavAudioPropertiesParser.ParseExtended (fmtChunk.Value.Data);
		}

		// Merge every LIST INFO into one tag. The first list wins; a later list
		// fills only fields that list left empty. Other LIST types stay in the chunk list.
		RiffInfoTag? info = null;
		foreach (var listChunk in riff.GetChunks (ListChunkId)) {
			if (!IsInfoList (listChunk))
				continue;

			var parsed = RiffInfoTag.Parse (listChunk.Data);
			if (parsed is null)
				continue;

			if (info is null)
				info = parsed;
			else
				info.FillMissingFrom (parsed);
		}

		wav.InfoTag = info;

		// Parse ID3v2 chunk
		var id3Chunk = riff.GetChunk (Id3ChunkId);
		if (id3Chunk.HasValue) {
			if (id3Chunk.Value.Data.Length > 0) {
				var id3Result = Id3v2Tag.Read (id3Chunk.Value.Data.Span);
				if (id3Result.IsSuccess)
					wav.Id3v2Tag = id3Result.Tag;
				else
					wav._keepUnparsedId3 = true;
			} else {
				wav._keepUnparsedId3 = true;
			}
		}

		// Parse bext chunk (BWF Broadcast Extension)
		var bextChunk = riff.GetChunk (BextChunkId);
		if (bextChunk.HasValue && bextChunk.Value.Data.Length >= BextTag.MinimumSize) {
			wav.BextTag = Riff.BextTag.Parse (bextChunk.Value.Data);
			wav._parsedBext = wav.BextTag;
		} else if (bextChunk.HasValue) {
			wav._keepUnparsedBext = true;
		}

		return WavFileReadResult.Success (wav);
	}

	/// <summary>
	/// Attempts to read a WAV file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out WavFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read a WAV file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out WavFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid WAV file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with "RIFF" and contains "WAVE" form type.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 12 bytes: RIFF (4) + size (4) + WAVE (4)
		if (data.Length < 12)
			return false;

		// Check "RIFF" magic
		if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
			return false;

		// Check "WAVE" form type at offset 8
		return data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';
	}

	/// <summary>
	/// Reads a WAV file from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static WavFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return WavFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
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
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken)
			.ConfigureAwait (false);

		if (!readResult.IsSuccess)
			return WavFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// True when <paramref name="chunk"/> is a LIST whose data begins with the INFO list type.
	/// </summary>
	static bool IsInfoList (RiffChunk chunk) =>
		chunk.FourCC == ListChunkId
		&& chunk.Data.Length >= 4
		&& chunk.Data.Slice (0, 4).ToStringLatin1 () == "INFO";

	/// <summary>
	/// Renders the WAV file to binary data.
	/// </summary>
	/// <returns>The complete WAV file as binary data.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the file is not valid or has no data chunk.
	/// </exception>
	public BinaryData Render ()
	{
		if (!IsValid)
			throw new InvalidOperationException ("Cannot render invalid WAV file");

		if (!HasDataChunk)
			throw new InvalidOperationException ("Cannot render a WAV file that has no data chunk.");

		// Start with a new RIFF structure
		var riff = new RiffFile ();
		var copyBext = ShouldCopyBextChunk ();

		// Rewrite id3, bext, and every LIST INFO. Copy every other chunk,
		// including a second chunk with the same FourCC. One INFO list is written below.
		foreach (var chunk in _riff.AllChunks) {
			if (IsInfoList (chunk))
				continue;

			if (chunk.FourCC == Id3ChunkId) {
				if (_keepUnparsedId3 && Id3v2Tag is null)
					riff.AddChunk (chunk);
				continue;
			}

			if (chunk.FourCC == BextChunkId) {
				if (copyBext)
					riff.AddChunk (chunk);
				continue;
			}

			riff.AddChunk (chunk);
		}

		// Add bext tag if present. An unmodified parsed bext is copied above.
		if (!copyBext && BextTag is not null && !BextTag.IsEmpty) {
			var bextData = BextTag.Render ();
			if (bextData.Length > 0)
				riff.SetChunk (new RiffChunk (BextChunkId, bextData));
		}

		// Add INFO tag if present
		if (InfoTag is not null && !InfoTag.IsEmpty) {
			var infoData = InfoTag.Render ();
			if (infoData.Length > 0) {
				// RiffInfoTag.Render() returns a complete LIST chunk, so skip that header.
				// Append. SetChunk would replace a non-INFO LIST copied above.
				if (infoData.Length > 8) {
					riff.AddChunk (new RiffChunk (ListChunkId, infoData.Slice (8)));
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
	/// True when a data chunk was parsed. A short payload still counts.
	/// A ckSize above <see cref="int.MaxValue"/> is not a chunk.
	/// </summary>
	bool HasDataChunk => _riff.GetChunk (DataChunkId) is not null;

	/// <summary>
	/// Copy the original bext bytes only for the instance <see cref="Read"/> parsed,
	/// and only while that instance still matches its source.
	/// A <see cref="BextTag"/> assigned from another file is rendered instead.
	/// Null removes a parsed bext. An unparsed bext is kept while the property stays null.
	/// </summary>
	bool ShouldCopyBextChunk () =>
		_riff.GetChunk (BextChunkId) is not null
		&& ((_keepUnparsedBext && BextTag is null)
			|| (ReferenceEquals (BextTag, _parsedBext) && BextTag is { } tag && tag.MatchesSource ()));

	FileWriteResult? MissingDataChunkFailure ()
	{
		if (!IsValid || HasDataChunk)
			return null;

		return FileWriteResult.Failure ("Cannot save a WAV file that has no data chunk.");
	}

	/// <summary>
	/// Saves the WAV file to the specified path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>
	/// The write result. Fails without writing when the file has no data chunk.
	/// </returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (MissingDataChunkFailure () is { } failure)
			return failure;

		var data = Render ();
		return AtomicFileWriter.Write (path, data.Span, fileSystem);
	}

	/// <summary>
	/// Saves the WAV file to the specified path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>
	/// The write result. Fails without writing when the file has no data chunk.
	/// </returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (MissingDataChunkFailure () is { } failure)
			return Task.FromResult (failure);

		var data = Render ();
		return AtomicFileWriter.WriteAsync (path, data.Memory, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the WAV file back to its source path asynchronously.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		var fs = fileSystem ?? _sourceFileSystem;
		return SaveToFileAsync (SourcePath!, fs, cancellationToken);
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
