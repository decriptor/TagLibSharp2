// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// The test side's TIFF writer: a header, IFD tables, values and raw bytes at chosen offsets, in either byte order,
/// classic TIFF or BigTIFF. It writes exactly what it is told (overlaps, bad counts and missing next offsets
/// included) and shares no code with the library.
/// </summary>
/// <param name="length">The size of the TIFF in bytes.</param>
/// <param name="bigEndian">"MM" (big-endian) instead of "II".</param>
/// <param name="big">BigTIFF (version 43: 8-byte counts and offsets, 20-byte entries) instead of classic TIFF.</param>
sealed class TiffBuilder (int length, bool bigEndian = false, bool big = false)
{
	readonly byte[] _data = new byte[length];

	public int Length => _data.Length;

	/// <summary>The byte-order mark and version (42, or 43 for BigTIFF), then the first IFD's offset.</summary>
	public TiffBuilder Header (long ifd0)
	{
		(bigEndian ? "MM"u8 : "II"u8).CopyTo (_data);
		U16 (2, big ? 43 : 42);
		if (!big)
			return U32 (4, ifd0);
		return U16 (4, 8).U16 (6, 0).U64 (8, ifd0);
	}

	/// <summary>
	/// A table at <paramref name="at"/>: the count, each entry's tag, type, count and value (or value offset), then
	/// the next-IFD offset, which is not written when <paramref name="next"/> is null.
	/// </summary>
	public TiffBuilder Table (int at, (int Tag, int Type, long Count, long Value)[] entries, long? next = 0)
	{
		var entrySize = big ? 20 : 12;
		var countSize = big ? 8 : 2;
		Word (at, entries.Length, countSize);
		for (var n = 0; n < entries.Length; n++) {
			var e = at + countSize + (entrySize * n);
			U16 (e, entries[n].Tag).U16 (e + 2, entries[n].Type);
			Word (e + 4, entries[n].Count, big ? 8 : 4);
			Word (e + (big ? 12 : 8), entries[n].Value, big ? 8 : 4);
		}

		if (next is long offset)
			Word (at + countSize + (entrySize * entries.Length), offset, big ? 8 : 4);
		return this;
	}

	/// <summary>A table's count and entries only: whatever is after them stays as it is.</summary>
	public TiffBuilder Entries (int at, params (int Tag, int Type, long Count, long Value)[] entries) =>
		Table (at, entries, next: null);

	public TiffBuilder Bytes (int at, ReadOnlySpan<byte> bytes)
	{
		bytes.CopyTo (_data.AsSpan (at));
		return this;
	}

	public TiffBuilder U16 (int at, int value)
	{
		if (bigEndian)
			BinaryPrimitives.WriteUInt16BigEndian (_data.AsSpan (at), (ushort)value);
		else
			BinaryPrimitives.WriteUInt16LittleEndian (_data.AsSpan (at), (ushort)value);
		return this;
	}

	public TiffBuilder U32 (int at, long value)
	{
		if (bigEndian)
			BinaryPrimitives.WriteUInt32BigEndian (_data.AsSpan (at), (uint)value);
		else
			BinaryPrimitives.WriteUInt32LittleEndian (_data.AsSpan (at), (uint)value);
		return this;
	}

	public TiffBuilder U64 (int at, long value)
	{
		if (bigEndian)
			BinaryPrimitives.WriteUInt64BigEndian (_data.AsSpan (at), (ulong)value);
		else
			BinaryPrimitives.WriteUInt64LittleEndian (_data.AsSpan (at), (ulong)value);
		return this;
	}

	public byte[] ToArray () => _data;

	TiffBuilder Word (int at, long value, int size) => size switch {
		2 => U16 (at, (int)value),
		4 => U32 (at, value),
		_ => U64 (at, value)
	};
}
