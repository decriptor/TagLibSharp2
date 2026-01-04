// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Aiff;

/// <summary>
/// Parses AIFF (Audio Interchange File Format) and AIFC files.
/// </summary>
/// <remarks>
/// AIFF file structure:
/// - Bytes 0-3:   "FORM" magic
/// - Bytes 4-7:   File size - 8 (32-bit big-endian)
/// - Bytes 8-11:  Form type ("AIFF" or "AIFC")
/// - Bytes 12+:   Chunks
///
/// Required chunks: COMM (audio properties), SSND (sound data)
/// Optional chunks: ID3 (metadata), MARK, INST, COMT, etc.
///
/// Unlike RIFF/WAV, AIFF uses big-endian byte order throughout.
/// </remarks>
public sealed class AiffFile : IMediaFile
{
	bool _disposed;

	/// <summary>
	/// Size of the FORM header (FORM + size + form type).
	/// </summary>
	public const int HeaderSize = 12;

	/// <summary>
	/// FORM magic bytes.
	/// </summary>
	public static readonly BinaryData FormMagic = BinaryData.FromStringLatin1 ("FORM");

	/// <summary>
	/// AIFF form type.
	/// </summary>
	public static readonly BinaryData AiffType = BinaryData.FromStringLatin1 ("AIFF");

	/// <summary>
	/// AIFC (compressed AIFF) form type.
	/// </summary>
	public static readonly BinaryData AifcType = BinaryData.FromStringLatin1 ("AIFC");

	/// <summary>
	/// Gets whether this file was successfully parsed.
	/// </summary>
	public bool IsValid { get; private set; }

	/// <summary>
	/// Gets the form type ("AIFF" or "AIFC").
	/// </summary>
	public string FormType { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the file size as stored in the FORM header.
	/// </summary>
	public uint FileSize { get; private set; }

	/// <summary>
	/// Gets the audio properties from the COMM chunk.
	/// </summary>
	public AiffAudioProperties? AudioProperties { get; private set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag.
	/// </summary>
	public Id3v2Tag? Tag { get; set; }

	/// <summary>
	/// Gets a value indicating whether this file has embedded pictures.
	/// </summary>
	public bool HasPictures => Tag?.HasPictures ?? false;

	/// <summary>
	/// Gets the embedded pictures from the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// Pictures are only available when an ID3v2 tag is present.
	/// Returns an empty array if no ID3v2 tag exists.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - API compatibility
	public IPicture[] Pictures => Tag?.Pictures ?? [];
#pragma warning restore CA1819

	/// <summary>
	/// Gets the front cover art from the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// Returns the first picture with type <see cref="PictureType.FrontCover"/>,
	/// or null if no front cover exists or no ID3v2 tag is present.
	/// </remarks>
	public PictureFrame? CoverArt => Tag?.CoverArt;

	/// <summary>
	/// Gets all parsed chunks in order.
	/// </summary>
	public IReadOnlyList<AiffChunk> AllChunks => _chunks;

	/// <summary>
	/// Gets the source file path (not tracked for AIFF files parsed from data).
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <inheritdoc />
	Tag? IMediaFile.Tag => Tag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => AudioProperties is null ? null : new AudioProperties (
		AudioProperties.Duration, AudioProperties.Bitrate, AudioProperties.SampleRate,
		AudioProperties.BitsPerSample, AudioProperties.Channels, "AIFF");

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Aiff;

	readonly List<AiffChunk> _chunks = [];

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		_chunks.Clear ();
		Tag = null;
		AudioProperties = null;
		_disposed = true;
	}

	/// <summary>
	/// Gets a chunk by its FourCC, or null if not found.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <returns>The first chunk with the matching FourCC, or null.</returns>
	public AiffChunk? GetChunk (string fourCC)
	{
		foreach (var chunk in _chunks) {
			if (chunk.FourCC == fourCC)
				return chunk;
		}
		return null;
	}

	/// <summary>
	/// Attempts to parse an AIFF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">The parsed file, or null if parsing failed.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryParse (BinaryData data, out AiffFile? file)
	{
		var result = Read (data.Span);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Reads an AIFF file from binary data with detailed error information.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static AiffFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < HeaderSize)
			return AiffFileReadResult.Failure ("Data too short for AIFF header");

		// Check FORM magic
		if (data[0] != 'F' || data[1] != 'O' || data[2] != 'R' || data[3] != 'M')
			return AiffFileReadResult.Failure ("Invalid AIFF magic (expected 'FORM')");

		// Read form type
		var formType = new BinaryData (data.Slice (8, 4).ToArray ()).ToStringLatin1 ();

		// Validate form type
		if (formType != "AIFF" && formType != "AIFC")
			return AiffFileReadResult.Failure ($"Invalid form type (expected 'AIFF' or 'AIFC', got '{formType}')");

		// Use internal parsing from here
		var file = new AiffFile ();
		if (!file.Parse (new BinaryData (data.ToArray ()))) {
			file.Dispose ();
			return AiffFileReadResult.Failure ("Failed to parse AIFF file structure");
		}

		return AiffFileReadResult.Success (file);
	}

	/// <summary>
	/// Reads an AIFF file from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static AiffFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return AiffFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess)
			result.File!.SourcePath = path;
		return result;
	}

	/// <summary>
	/// Reads an AIFF file from a file path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static async Task<AiffFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);

		if (!readResult.IsSuccess)
			return AiffFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess)
			result.File!.SourcePath = path;
		return result;
	}

	bool Parse (BinaryData data)
	{
		if (data.Length < HeaderSize)
			return false;

		var span = data.Span;

		// Check FORM magic
		if (span[0] != 'F' || span[1] != 'O' || span[2] != 'R' || span[3] != 'M')
			return false;

		// Read file size (big-endian)
		FileSize = (uint)(
			(span[4] << 24) |
			(span[5] << 16) |
			(span[6] << 8) |
			span[7]);

		// Read form type
		FormType = data.Slice (8, 4).ToStringLatin1 ();

		// Validate form type
		if (FormType != "AIFF" && FormType != "AIFC")
			return false;

		IsValid = true;

		// Parse chunks
		int offset = HeaderSize;
		while (offset < data.Length) {
			if (!AiffChunk.TryParse (data, offset, out var chunk))
				break;

			_chunks.Add (chunk!);
			offset += chunk!.TotalSize;

			// Process specific chunk types
			ProcessChunk (chunk);
		}

		return true;
	}

	void ProcessChunk (AiffChunk chunk)
	{
		switch (chunk.FourCC) {
			case "COMM":
				ParseCommChunk (chunk);
				break;
			case "ID3 ":
			case "ID3":
				ParseId3Chunk (chunk);
				break;
		}
	}

	void ParseCommChunk (AiffChunk chunk)
	{
		if (AiffAudioProperties.TryParse (chunk.Data, out var props))
			AudioProperties = props;
	}

	void ParseId3Chunk (AiffChunk chunk)
	{
		// The ID3 chunk contains a complete ID3v2 tag
		var result = Id3v2Tag.Read (chunk.Data.Span);
		if (result.IsSuccess)
			Tag = result.Tag;
	}

	/// <summary>
	/// Renders the AIFF file to binary data.
	/// </summary>
	/// <returns>The complete AIFF file as binary data.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the file is not valid.</exception>
	public BinaryData Render ()
	{
		if (!IsValid)
			throw new InvalidOperationException ("Cannot render invalid AIFF file");

		// Calculate total size of all chunks
		var chunksSize = 0;
		foreach (var chunk in _chunks) {
			// Skip existing ID3 chunks - we'll add updated version if needed
			if (chunk.FourCC is "ID3 " or "ID3")
				continue;
			chunksSize += chunk.TotalSize;
		}

		// Add ID3 tag if present
		BinaryData id3Data = BinaryData.Empty;
		if (Tag is not null && !Tag.IsEmpty) {
			id3Data = Tag.Render ();
			if (id3Data.Length > 0) {
				// ID3 chunk: header (8) + data + padding if odd
				var id3ChunkSize = AiffChunk.HeaderSize + id3Data.Length;
				if (id3Data.Length % 2 == 1)
					id3ChunkSize++;
				chunksSize += id3ChunkSize;
			}
		}

		// Total size = form type (4) + all chunks
		var contentSize = 4 + chunksSize;
		var totalSize = HeaderSize + chunksSize;

		using var builder = new BinaryDataBuilder (totalSize);

		// FORM header
		builder.AddStringLatin1 ("FORM");
		builder.AddUInt32BE ((uint)contentSize);
		builder.AddStringLatin1 (FormType);

		// Write all chunks except ID3 (which we'll add at the end)
		foreach (var chunk in _chunks) {
			if (chunk.FourCC is "ID3 " or "ID3")
				continue;
			builder.Add (chunk.Render ());
		}

		// Add ID3 chunk if present
		if (id3Data.Length > 0) {
			var id3Chunk = new AiffChunk ("ID3 ", id3Data);
			builder.Add (id3Chunk.Render ());
		}

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Saves the AIFF file to the specified path.
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
	/// Saves the AIFF file to the specified path asynchronously.
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
/// Represents the result of reading an AIFF file.
/// </summary>
public readonly struct AiffFileReadResult : IEquatable<AiffFileReadResult>
{
	/// <summary>
	/// Gets the parsed AIFF file, or null if parsing failed.
	/// </summary>
	public AiffFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets whether the parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null;

	AiffFileReadResult (AiffFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static AiffFileReadResult Success (AiffFile file) => new (file, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static AiffFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (AiffFileReadResult other) =>
		ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is AiffFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (AiffFileReadResult left, AiffFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (AiffFileReadResult left, AiffFileReadResult right) =>
		!left.Equals (right);
}
