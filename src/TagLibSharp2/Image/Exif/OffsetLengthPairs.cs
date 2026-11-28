// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// The offset/length pairs a plain edit may read: StripOffsets/StripByteCounts (0x0111/0x0117),
/// TileOffsets/TileByteCounts (0x0144/0x0145) and JPEGInterchangeFormat/JPEGInterchangeFormatLength (0x0201/0x0202,
/// TIFF 6.0 §3 and Exif 2.32 §4.6.5). Slot order is strip, tile, then thumbnail, the offset side then the length side.
/// </summary>
static class OffsetLengthPairs
{
	public static readonly (int Offsets, int Lengths)[] All = [(0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202)];

	public static int Slot (int tag) => tag switch {
		0x0111 => 0,
		0x0117 => 1,
		0x0144 => 2,
		0x0145 => 3,
		0x0201 => 4,
		0x0202 => 5,
		_ => -1
	};
}
