// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
	/// Appends a chunk without replacing an existing chunk of the same FourCC.
	/// </summary>
	/// <param name="chunk">The chunk to append.</param>
	/// <remarks>
	/// RIFF allows more than one LIST chunk (INFO and adtl, for example).
	/// <see cref="SetChunk"/> keeps a single chunk per FourCC and would overwrite the other list.
	/// </remarks>
	internal void AddChunk (RiffChunk chunk) => _chunks.Add (chunk);

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

		// 0xFFFFFFFF is a placeholder from streamed writers. RF64 is unsupported.
		var formEnd = file.FileSize == 0xFFFFFFFF ? (long)data.Length : (long)file.FileSize + 8;
		var limit = (int)Math.Min ((long)data.Length, formEnd);

		var offset = HeaderSize;
		while (offset + RiffChunk.HeaderSize <= limit) {
			if (!RiffChunk.TryParse (data, offset, out var chunk))
				break;

			// ckSize is the data length and does not include the pad byte.
			// A short read is kept; TryParse stored only the bytes in the buffer.
			var dataSize = chunk.DataSize;
			var dataEnd = (long)offset + RiffChunk.HeaderSize + dataSize;
			file._chunks.Add (chunk);

			var pad = (dataSize & 1) != 0 ? 1L : 0L;
			var next = dataEnd + pad;
			// limit is an int, so next >= limit also rejects a next past int.MaxValue.
			if (next >= limit)
				break;

			offset = (int)next;
		}

		file.IsValid = true;
		return true;
	}

	/// <summary>
	/// A chunk whose bytes were left in the file. <see cref="PresentBytes"/> can be
	/// shorter than the declared ckSize when the file ends first.
	/// </summary>
	internal readonly struct SkippedChunk
	{
		public SkippedChunk (int index, long offset, uint presentBytes)
		{
			Index = index;
			Offset = offset;
			PresentBytes = presentBytes;
		}

		public int Index { get; }

		public long Offset { get; }

		public uint PresentBytes { get; }
	}

	/// <summary>
	/// Reads a RIFF file from a seekable stream. Sample bytes, and any other chunk
	/// whose ckSize does not fit in an int, stay in the file. Each of those chunks
	/// is one <see cref="SkippedChunk"/>, in file order. A later chunk is still parsed.
	/// </summary>
	internal static bool TryRead (Stream stream, out RiffFile file, out List<SkippedChunk> skipped)
	{
		file = new RiffFile ();
		skipped = [];

		var header = StreamBytes.ReadAtMost (stream, HeaderSize);
		if (header.Length < HeaderSize || !IsRiff (header))
			return false;

		file.FileSize = (uint)header[4] | ((uint)header[5] << 8) | ((uint)header[6] << 16) | ((uint)header[7] << 24);
		file.FormType = FourCharacters (header, 8);

		var limit = Math.Min (stream.Length, (long)file.FileSize + 8);
		while (stream.Position + RiffChunk.HeaderSize <= limit) {
			var chunkHeader = StreamBytes.ReadAtMost (stream, RiffChunk.HeaderSize);
			if (chunkHeader.Length < RiffChunk.HeaderSize)
				break;

			var fourCC = FourCharacters (chunkHeader, 0);
			var size = (uint)chunkHeader[4] | ((uint)chunkHeader[5] << 8) | ((uint)chunkHeader[6] << 16) | ((uint)chunkHeader[7] << 24);
			var remaining = limit - stream.Position;
			var available = remaining < size ? (uint)Math.Max (0, remaining) : size;
			// A data chunk is reloaded on save. A ckSize past int.MaxValue cannot be
			// copied here; skipping it in long arithmetic keeps the chunks after it.
			if (fourCC == "data" || size > int.MaxValue) {
				if (available > 0)
					skipped.Add (new SkippedChunk (file._chunks.Count, stream.Position, available));
				var pad = (size & 1) == 1 && available == size ? 1L : 0L;
				var skip = (long)available + pad;
				if (stream.Position > stream.Length - skip)
					skip = Math.Max (0, stream.Length - stream.Position);
				stream.Position += skip;
				file._chunks.Add (RiffChunk.WithDeclaredSize (fourCC, size, BinaryData.Empty));
				if (available < size)
					break;
				continue;
			}

			var payload = StreamBytes.ReadAtMost (stream, (int)available);
			if ((size & 1) == 1 && payload.Length == size && stream.Position < limit)
				StreamBytes.ReadAtMost (stream, 1);
			file._chunks.Add (RiffChunk.WithDeclaredSize (fourCC, size, new BinaryData (payload)));
			if (payload.Length < size)
				break;
		}

		file.IsValid = true;
		return true;
	}

	internal void ReplaceChunkAt (int index, RiffChunk chunk) => _chunks[index] = chunk;

	static bool IsRiff (byte[] header) =>
		header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F';

	static string FourCharacters (byte[] data, int offset) =>
		new ([(char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3]]);

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

		// Form size is the form type plus each chunk as stored (header + bytes present + pad).
		var chunksSize = 0;
		foreach (var chunk in _chunks)
			chunksSize += chunk.RenderedSize;

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
