// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Tiff;

/// <summary>
/// A TIFF structure over its bytes, every offset counted from the byte-order mark (TIFF 6.0 §2). Classic TIFF has
/// 2-byte entry counts, 12-byte entries and 4-byte offsets; BigTIFF (version 43) has 8-byte counts and offsets and
/// 20-byte entries. This is the one definition of the header, of when a table is read, of entry fields, of the
/// next-IFD offset and of an entry's value bytes; the walks that use it differ only in which pointers they follow.
/// </summary>
readonly ref struct TiffReader
{
	public const int ExifIfdPointer = 0x8769;
	public const int GpsIfdPointer = 0x8825;
	public const int InteropIfdPointer = 0xA005;
	public const int SubIfdsPointer = 0x014A;

	/// <summary>No IFD can start inside the 8-byte classic header.</summary>
	public const int HeaderSize = 8;

	public TiffReader (ReadOnlySpan<byte> data, bool littleEndian, bool big = false)
	{
		Data = data;
		LittleEndian = littleEndian;
		Big = big;
	}

	public ReadOnlySpan<byte> Data { get; }
	public bool LittleEndian { get; }
	public bool Big { get; }
	public int Length => Data.Length;
	public int CountSize => Big ? 8 : 2;
	public int EntrySize => Big ? 20 : 12;

	public int U16 (long at) => Endian.U16 (Data, (int)at, LittleEndian);
	public long U32 (long at) => Endian.U32 (Data, (int)at, LittleEndian);

	/// <summary>"II" (little-endian) or "MM" (big-endian), with nothing else checked.</summary>
	public static bool TryByteOrder (ReadOnlySpan<byte> data, out bool littleEndian)
	{
		littleEndian = data[0] == (byte)'I' && data[1] == (byte)'I';
		return littleEndian || (data[0] == (byte)'M' && data[1] == (byte)'M');
	}

	/// <summary>A classic TIFF header: "II" then 42, or "MM" then 42, then IFD0's offset.</summary>
	public static bool TryClassic (ReadOnlySpan<byte> data, out TiffReader reader, out long ifd0)
	{
		reader = default;
		ifd0 = 0;
		if (!IsClassic (data))
			return false;
		reader = new TiffReader (data, data[0] == (byte)'I');
		ifd0 = reader.U32 (4);
		return true;
	}

	/// <summary>A classic or BigTIFF header and the first IFD's offset (BigTIFF: offset size 8, reserved 0).</summary>
	public static bool TryOpen (ReadOnlySpan<byte> data, out TiffReader reader, out long first)
	{
		if (IsClassic (data))
			return TryClassic (data, out reader, out first);
		reader = new TiffReader (data, data.Length > 0 && data[0] == (byte)'I', big: true);
		first = 0;
		return IsBig (data) && reader.U16 (4) == 8 && reader.U16 (6) == 0 && Endian.TryU64 (data, 8, reader.LittleEndian, out first);
	}

	public static bool IsTiff (ReadOnlySpan<byte> data) => IsClassic (data) || IsBig (data);

	static bool IsClassic (ReadOnlySpan<byte> data) =>
		data.Length >= HeaderSize && (
			(data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 42 && data[3] == 0) ||
			(data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] == 42));

	static bool IsBig (ReadOnlySpan<byte> data) =>
		data.Length >= 16 && (
			(data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 0x2B && data[3] == 0) ||
			(data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] == 0x2B));

	/// <summary>
	/// A table is read when its count and all its entries lie inside the data, as exiftool, ImageIO and TagLib#
	/// read it: the next-IFD offset after the entries may be missing or cut short, and is then 0 (<see cref="Next"/>).
	/// A table whose entries run past the data is read by none of them. No table starts inside the 8-byte classic
	/// header. Any count that fits is read: exiftool reads a classic table of 65,535 entries and a BigTIFF one of
	/// 70,000 (measured); a walk bounds the entries it reads in all by <see cref="EntryBound"/> instead.
	/// </summary>
	public bool TryTable (long at, out int count)
	{
		count = 0;
		if (at < HeaderSize || at > Length - CountSize)
			return false;
		long entries;
		if (!Big)
			entries = U16 (at);
		else if (!Endian.TryU64 (Data, (int)at, LittleEndian, out entries))
			return false;
		if (entries > (Length - at - CountSize) / EntrySize)
			return false;
		count = (int)entries;
		return true;
	}

	/// <summary>
	/// The most entries tables that do not overlap can hold in all, one per entry's bytes: a walk that has read more
	/// has met tables that share their entries, and stops there, so its work stays proportional to the data.
	/// </summary>
	public long EntryBound => Length / EntrySize;

	public long Entry (long table, long n) => table + CountSize + (n * EntrySize);
	public int Tag (long entry) => U16 (entry);
	public int Type (long entry) => U16 (entry + 2);

	/// <summary>The entry's value count; a BigTIFF count above 2^63 - 1, which no data can hold, as 0.</summary>
	public long Count (long entry) => !Big ? U32 (entry + 4) : U64OrZero (entry + 4);

	/// <summary>Where an entry's value, or the offset of its value, starts.</summary>
	public long ValueField (long entry) => entry + (Big ? 12 : 8);

	/// <summary>The bytes of value an entry holds itself: 4 in classic TIFF, 8 in BigTIFF.</summary>
	public int ValueFieldSize => Big ? 8 : 4;

	/// <summary>
	/// The value in the entry, or the offset of its value: a 4-byte field, or in BigTIFF an 8-byte one, of which a
	/// value above 2^63 - 1 is past any data (<see cref="long.MaxValue"/>).
	/// </summary>
	public long Value (long entry) => !Big ? U32 (entry + 8) : Endian.TryU64 (Data, (int)entry + 12, LittleEndian, out var value) ? value : long.MaxValue;

	/// <summary>
	/// Where the offsets a pointer entry (0x8769, 0x8825, 0xA005, 0x014A) names are, as exiftool follows them
	/// (measured): BYTE, SHORT, LONG, SBYTE, SSHORT, SLONG and IFD values; in classic TIFF also UNDEFINED with count
	/// 1, and in BigTIFF also LONG8, SLONG8 and IFD8 (an UNDEFINED byte there is read as text, which names the
	/// header). An Exif IFD pointer only with count 1, a GPS or interoperability pointer by its first value, SubIFDs
	/// by every value. Values that do not fit in the entry are at its offset and must all lie in the data. Any other
	/// type, or count 0, names nothing.
	/// </summary>
	public bool TryPointers (long entry, out PointerValues values)
	{
		values = default;
		var tag = Tag (entry);
		var count = Count (entry);
		var type = Type (entry);
		var size = type switch {
			1 or 6 => 1,
			7 when count == 1 && !Big => 1,
			3 or 8 => 2,
			4 or 9 or 13 => 4,
			16 or 17 or 18 when Big => 8,
			_ => 0
		};
		// SBYTE and SSHORT values are signed. A negative SLONG read unsigned is at least 2^31, past any data, and a
		// negative SLONG8 is above 2^63 - 1, which PointerAt reads as no table, so neither needs its sign.
		var signed = type is 6 or 8;
		if (size == 0 || count == 0 || (tag == ExifIfdPointer && count != 1))
			return false;
		var from = count <= ValueFieldSize / size ? ValueField (entry) : Value (entry);
		// From past the data, the room left is negative: no value fits.
		if (count > (Length - from) / size)
			return false;
		values = new PointerValues (from, tag is GpsIfdPointer or InteropIfdPointer ? 1 : count, size, signed);
		return true;
	}

	/// <summary>
	/// A classic pointer entry's 4-byte field as one LONG, whatever the entry's type and count: how TagLib# reads IFD0's
	/// Exif IFD and GPS IFD pointers (measured: it follows a SHORT, ASCII, RATIONAL or count-0 or count-2 0x8769 or
	/// 0x8825 in IFD0 to the table that LONG names).
	/// </summary>
	public static PointerValues RawPointer (long entry) => new (entry + 8, 1, 4, false);

	/// <summary>The offset a pointer value names, or 0 (no table) when it is negative or above 2^63 - 1.</summary>
	public long PointerAt (PointerValues values, long n)
	{
		var at = values.From + (n * values.Size);
		if (values.Size == 8)
			return U64OrZero (at);
		long value = values.Size switch {
			1 => Data[(int)at],
			2 => U16 (at),
			_ => U32 (at)
		};
		return values.Signed && value >= 1L << ((8 * values.Size) - 1) ? 0 : value;
	}

	/// <summary>
	/// The next-IFD offset after a table's entries (4 bytes, or 8 in BigTIFF), or 0 when its bytes are not all in
	/// the data or, in BigTIFF, it is above 2^63 - 1.
	/// </summary>
	public long Next (long table, long count)
	{
		var at = Entry (table, count);
		if (Big)
			return U64OrZero (at);
		return at + 4 <= Length ? U32 (at) : 0;
	}

	long U64OrZero (long at) => Endian.TryU64 (Data, (int)at, LittleEndian, out var value) ? value : 0;

	/// <summary>
	/// The bytes a classic table occupies: its count, its entries and its next-IFD offset (bytes past the data are
	/// never written or zeroed, so a range that runs past it needs no clipping).
	/// </summary>
	public static (long Start, long End) TableRange (long table, long count) => (table, table + 2 + (count * 12L) + 4);

	/// <summary>
	/// The out-of-line value of a classic entry; empty when the value fits in the entry. Only the Exif edit reads
	/// values out of line, and it reads classic TIFF only.
	/// </summary>
	public (long Start, long End) ValueRange (long entry)
	{
		var bytes = TypeSize (Type (entry)) * Count (entry);
		if (bytes <= 4)
			return (0, 0);
		var offset = Value (entry);
		return (offset, offset + bytes);
	}

	/// <summary>
	/// Whether exiftool reads an entry of this format (measured, exiftool 13.55): TIFF 6.0 §2 types 1-12 and IFD
	/// (13, TIFF Technical Note 1). A first entry of any other format drops the whole table, and none of its
	/// pointers or its next IFD is followed; a later entry of such a format is skipped and the table is read.
	/// </summary>
	public static bool FormatExiftoolReads (int type) => type is >= 1 and <= 13;

	/// <summary>
	/// The size of one value of a classic field type: TIFF 6.0 §2 types 1-12 and IFD (13, TIFF Technical Note 1); 0
	/// for any other type.
	/// </summary>
	static int TypeSize (int type) => type switch {
		1 or 2 or 6 or 7 => 1,
		3 or 8 => 2,
		4 or 9 or 11 or 13 => 4,
		5 or 10 or 12 => 8,
		_ => 0
	};
}

/// <summary>The offsets a pointer entry names: where they start, how many are followed, their size and signedness.</summary>
readonly record struct PointerValues (long From, long Count, int Size, bool Signed);
