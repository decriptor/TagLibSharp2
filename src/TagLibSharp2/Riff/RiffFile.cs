// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// Parses and writes RIFF container format files.
/// RIFF (Resource Interchange File Format) is used by WAV, AVI, and other Microsoft formats.
/// </summary>
/// <remarks>
/// RIFF file structure:
/// - Bytes 0-3:  "RIFF" magic
/// - Bytes 4-7:  File size - 8 (32-bit little-endian)
/// - Bytes 8-11: Form type (e.g., "WAVE", "AVI ")
/// - Bytes 12+:  Chunks
///
/// Each chunk is padded to even byte boundaries.
/// </remarks>
public class RiffFile
{
	/// <summary>
	/// Size of the RIFF header (RIFF + size + form type).
	/// </summary>
	public const int HeaderSize = 12;

	/// <summary>
	/// RIFF magic bytes.
	/// </summary>
	public static readonly BinaryData RiffMagic = BinaryData.FromStringLatin1 ("RIFF");

	/// <summary>
	/// WAVE form type.
	/// </summary>
	public static readonly BinaryData WaveType = BinaryData.FromStringLatin1 ("WAVE");

	/// <summary>
	/// Gets whether this file was successfully parsed.
	/// </summary>
	public bool IsValid { get; private set; }

	/// <summary>
	/// Gets the form type (e.g., "WAVE", "AVI ").
	/// </summary>
	public string FormType { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the file size as stored in the RIFF header (file size - 8).
	/// </summary>
	public uint FileSize { get; private set; }

	/// <summary>
	/// Gets the parsed chunks in order.
	/// </summary>
	public IReadOnlyList<RiffChunk> AllChunks => _chunks;

	readonly List<RiffChunk> _chunks = [];

	/// <summary>
	/// Gets a chunk by its FourCC, or null if not found.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <returns>The first chunk with the matching FourCC, or null.</returns>
	public RiffChunk? GetChunk (string fourCC)
	{
		foreach (var chunk in _chunks) {
			if (chunk.FourCC == fourCC)
				return chunk;
		}
		return null;
	}

	/// <summary>
	/// Gets all chunks with the specified FourCC.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <returns>All chunks matching the FourCC.</returns>
	public IEnumerable<RiffChunk> GetChunks (string fourCC)
	{
		foreach (var chunk in _chunks) {
			if (chunk.FourCC == fourCC)
				yield return chunk;
		}
	}

	/// <summary>
	/// Adds or replaces a chunk with the specified FourCC.
	/// </summary>
	/// <param name="chunk">The chunk to add or replace.</param>
	public void SetChunk (RiffChunk chunk)
	{
		for (var i = 0; i < _chunks.Count; i++) {
			if (_chunks[i].FourCC == chunk.FourCC) {
				_chunks[i] = chunk;
				return;
			}
		}
		_chunks.Add (chunk);
	}

	/// <summary>
	/// Removes all chunks with the specified FourCC.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <returns>True if any chunks were removed.</returns>
	public bool RemoveChunks (string fourCC)
	{
		var removed = false;
		for (var i = _chunks.Count - 1; i >= 0; i--) {
			if (_chunks[i].FourCC == fourCC) {
				_chunks.RemoveAt (i);
				removed = true;
			}
		}
		return removed;
	}

	/// <summary>
	/// Attempts to parse RIFF data.
	/// </summary>
	/// <param name="data">The source data.</param>
	/// <param name="file">The parsed file if successful.</param>
	/// <returns>True if parsing succeeded.</returns>
	public static bool TryParse (BinaryData data, out RiffFile file)
	{
		file = new RiffFile ();

		if (data.Length < HeaderSize)
			return false;

		// Check RIFF magic
		if (!data.Slice (0, 4).Equals (RiffMagic))
			return false;

		// Read file size (excludes RIFF + size fields = 8 bytes)
		file.FileSize = data.ToUInt32LE (4);

		// Read form type
		file.FormType = data.Slice (8, 4).ToStringLatin1 ();

		// Parse chunks
		var offset = HeaderSize;
		var limit = Math.Min (data.Length, (int)file.FileSize + 8);

		while (offset + RiffChunk.HeaderSize <= limit) {
			if (!RiffChunk.TryParse (data, offset, out var chunk))
				break;

			file._chunks.Add (chunk);
			offset += chunk.TotalSize;
		}

		file.IsValid = true;
		return true;
	}

	/// <summary>
	/// Renders the RIFF file to binary data.
	/// </summary>
	/// <param name="formType">The form type (defaults to current FormType).</param>
	/// <returns>The complete RIFF file as binary data.</returns>
	public BinaryData Render (string? formType = null)
	{
		formType ??= FormType;
		if (string.IsNullOrEmpty (formType) || formType.Length != 4)
			throw new InvalidOperationException ("Form type must be exactly 4 characters");

		// Calculate total size
		var chunksSize = 0;
		foreach (var chunk in _chunks)
			chunksSize += chunk.TotalSize;

		var totalSize = HeaderSize + chunksSize;
		using var builder = new BinaryDataBuilder (totalSize);

		// RIFF header
		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE ((uint)(totalSize - 8)); // Size excludes RIFF + size field
		builder.AddStringLatin1 (formType);

		// Chunks
		foreach (var chunk in _chunks)
			builder.Add (chunk.Render ());

		return builder.ToBinaryData ();
	}
}
