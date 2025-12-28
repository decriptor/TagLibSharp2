// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

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
public class AiffFile
{
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
	/// Gets the ID3v2 tag if present.
	/// </summary>
	public Id3v2Tag? Tag { get; private set; }

	/// <summary>
	/// Gets all parsed chunks in order.
	/// </summary>
	public IReadOnlyList<AiffChunk> AllChunks => _chunks;

	readonly List<AiffChunk> _chunks = [];

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
		file = new AiffFile ();

		if (!file.Parse (data)) {
			file = null;
			return false;
		}

		return true;
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
}
