// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

namespace TagLibSharp2.Image;

/// <summary>
/// Reads and writes unsigned integers in either byte order over spans: the one byte-order helper of the image
/// code (TIFF and Exif "II"/"MM", the MPF index, PNG chunk fields, BMP headers).
/// </summary>
static class Endian
{
	public static int U16 (ReadOnlySpan<byte> data, int at, bool le) =>
		le ? BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (at)) : BinaryPrimitives.ReadUInt16BigEndian (data.Slice (at));

	public static long U32 (ReadOnlySpan<byte> data, int at, bool le) =>
		le ? BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (at)) : BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at));

	public static int I32 (ReadOnlySpan<byte> data, int at, bool le) =>
		le ? BinaryPrimitives.ReadInt32LittleEndian (data.Slice (at)) : BinaryPrimitives.ReadInt32BigEndian (data.Slice (at));

	/// <summary>False outside the data, or for a value above <see cref="long.MaxValue"/>.</summary>
	public static bool TryU64 (ReadOnlySpan<byte> data, int at, bool le, out long value)
	{
		value = 0;
		if (at < 0 || at > data.Length - 8)
			return false;
		var raw = le ? BinaryPrimitives.ReadUInt64LittleEndian (data.Slice (at)) : BinaryPrimitives.ReadUInt64BigEndian (data.Slice (at));
		if (raw > long.MaxValue)
			return false;
		value = (long)raw;
		return true;
	}

	public static void WriteU16 (Span<byte> data, int at, int value, bool le)
	{
		if (le)
			BinaryPrimitives.WriteUInt16LittleEndian (data.Slice (at), (ushort)value);
		else
			BinaryPrimitives.WriteUInt16BigEndian (data.Slice (at), (ushort)value);
	}

	public static void WriteU32 (Span<byte> data, int at, long value, bool le)
	{
		if (le)
			BinaryPrimitives.WriteUInt32LittleEndian (data.Slice (at), (uint)value);
		else
			BinaryPrimitives.WriteUInt32BigEndian (data.Slice (at), (uint)value);
	}
}
