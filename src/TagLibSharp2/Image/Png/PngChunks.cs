// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Image.Png;

/// <summary>A chunk of a PNG file: where it starts and its whole length (length, type, data and CRC).</summary>
readonly struct PngChunk (int start, int length)
{
	public int Start { get; } = start;
	public int Length { get; } = length;
}

/// <summary>The chunk list of a PNG file (PNG 3rd ed. §5.3): walking it, checking a CRC, and writing a chunk.</summary>
static class PngChunks
{
	/// <summary>The chunks from the signature up to IEND or the first chunk that runs past the data.</summary>
	public static bool TryWalk (ReadOnlySpan<byte> data, out List<PngChunk> chunks)
	{
		chunks = [];
		if (data.Length < 8 || data[0] != 0x89 || data[1] != (byte)'P' || data[2] != (byte)'N' || data[3] != (byte)'G'
			|| data[4] != 0x0D || data[5] != 0x0A || data[6] != 0x1A || data[7] != 0x0A)
			return false;
		var i = 8;
		while (i + 12 <= data.Length) {
			var length = ReadLength (data, i);
			if (length < 0 || i + 12L + length > data.Length)
				break;
			var size = 12 + length;
			chunks.Add (new PngChunk (i, size));
			if (data.Slice (i + 4, 4).SequenceEqual ("IEND"u8))
				break;
			i += size;
		}

		return true;
	}

	public static bool IsTextChunk (ReadOnlySpan<byte> type) =>
		type.SequenceEqual ("tEXt"u8) || type.SequenceEqual ("zTXt"u8) || type.SequenceEqual ("iTXt"u8);

	public static bool TypeEquals (ReadOnlySpan<byte> data, PngChunk chunk, ReadOnlySpan<byte> type) =>
		data.Slice (chunk.Start + 4, 4).SequenceEqual (type);

	public static bool CrcMatches (ReadOnlySpan<byte> chunk)
	{
		var length = chunk.Length - 12;
		return BinaryData.ComputeCrc32 (chunk.Slice (4, 4 + length)) == (uint)Endian.U32 (chunk, 8 + length, le: false);
	}

	/// <summary>A chunk length field, or -1 above 2^31 - 1 (PNG 3rd ed. §5.3).</summary>
	public static int ReadLength (ReadOnlySpan<byte> data, int offset)
	{
		var value = Endian.U32 (data, offset, le: false);
		return value > int.MaxValue ? -1 : (int)value;
	}

	public static void Add (BinaryDataBuilder builder, ReadOnlySpan<byte> type, byte[] payload)
	{
		builder.AddUInt32BE ((uint)payload.Length);
		builder.Add (type);
		builder.Add (payload);
		var crcInput = new byte[4 + payload.Length];
		type.CopyTo (crcInput);
		payload.CopyTo (crcInput, 4);
		builder.AddUInt32BE (new BinaryData (crcInput).ComputeCrc32 ());
	}

	public static byte[] Chunk (ReadOnlySpan<byte> type, byte[] payload)
	{
		using var builder = new BinaryDataBuilder (payload.Length + 12);
		Add (builder, type, payload);
		return builder.ToArray ();
	}
}
