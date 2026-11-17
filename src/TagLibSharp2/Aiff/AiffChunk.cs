// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Aiff;

/// <summary>
/// Represents a chunk in an AIFF/AIFC file.
/// </summary>
/// <remarks>
/// AIFF chunk structure:
/// - Bytes 0-3: FourCC identifier (ASCII)
/// - Bytes 4-7: Chunk size (32-bit big-endian, excludes header)
/// - Bytes 8+:  Chunk data
///
/// Chunks are padded to even byte boundaries.
/// Unlike RIFF, AIFF uses big-endian byte order.
/// </remarks>
public class AiffChunk
{
	/// <summary>
	/// Size of the chunk header (FourCC + size).
	/// </summary>
	public const int HeaderSize = 8;

	/// <summary>
	/// Gets the 4-character chunk identifier.
	/// </summary>
	public string FourCC { get; }

	/// <summary>
	/// Gets the chunk data size (excludes the 8-byte header).
	/// </summary>
	public uint Size { get; }

	/// <summary>
	/// Gets the chunk data.
	/// </summary>
	public BinaryData Data { get; }

	/// <summary>
	/// Gets the total size including header and padding.
	/// </summary>
	/// <remarks>
	/// The sum is a long so a <see cref="Size"/> within 9 of <see cref="int.MaxValue"/>
	/// (header plus an optional pad byte) does not wrap negative.
	/// </remarks>
	public long TotalSize => HeaderSize + (long)Size + (Size % 2 == 1 ? 1 : 0);

	AiffChunk (string fourCC, uint size, BinaryData data)
	{
		FourCC = fourCC;
		Size = size;
		Data = data;
	}

	/// <summary>
	/// A chunk whose declared size is kept when the bytes were left in the file.
	/// </summary>
	internal static AiffChunk WithDeclaredSize (string fourCC, uint size, BinaryData data) =>
		new (fourCC, size, data);

	/// <summary>
	/// Creates a new AiffChunk with the specified FourCC and data.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <param name="data">The chunk data.</param>
	public AiffChunk (string fourCC, BinaryData data)
	{
		FourCC = fourCC;
		Size = (uint)data.Length;
		Data = data;
	}

	/// <summary>
	/// Renders the chunk to binary data.
	/// </summary>
	/// <returns>The complete chunk including header and padding.</returns>
	public BinaryData Render ()
	{
		var needsPadding = Size % 2 == 1;
		using var builder = new BinaryDataBuilder (HeaderSize + (int)Size + (needsPadding ? 1 : 0));

		// Write FourCC
		builder.AddStringLatin1 (FourCC);

		// Write size (big-endian)
		builder.AddUInt32BE (Size);

		// Write data
		builder.Add (Data);

		// Pad to even boundary if needed
		if (needsPadding)
			builder.Add (0x00);

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Attempts to parse a chunk at the specified offset.
	/// </summary>
	/// <param name="data">The binary data containing the chunk.</param>
	/// <param name="offset">The offset where the chunk starts.</param>
	/// <param name="chunk">The parsed chunk, or null if parsing failed.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryParse (BinaryData data, int offset, out AiffChunk? chunk)
	{
		chunk = null;

		// Need at least 8 bytes for header
		if (data.Length < offset + HeaderSize)
			return false;

		var span = data.Span;

		// Read FourCC
		var fourCC = data.Slice (offset, 4).ToStringLatin1 ();

		// Read size (big-endian)
		uint size = (uint)(
			(span[offset + 4] << 24) |
			(span[offset + 5] << 16) |
			(span[offset + 6] << 8) |
			span[offset + 7]);

		// Overflow protection: reject chunks claiming > int.MaxValue size
		if (size > int.MaxValue)
			return false;

		// Verify we have enough data
		if (data.Length < offset + HeaderSize + size)
			return false;

		// Extract chunk data (safe cast after overflow check)
		var chunkData = data.Slice (offset + HeaderSize, (int)size);

		chunk = new AiffChunk (fourCC, size, chunkData);
		return true;
	}
}
