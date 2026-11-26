// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// What an Exif removal changes: the entries to remove from each table, the values they free, and the bytes that
/// must never be zeroed.
/// </summary>
/// <remarks>
/// Never zeroed: the header (a value is cleared only from byte 8), every table (count, entries, next offset), every
/// kept entry's value (a SubIFD offset array is the value of the 0x014A entry that names it, which is never
/// removed), the data that offset/length pairs name (the IFD1 thumbnail, strips, tiles), and the structure of every
/// table no reader reads (its count, entries and next offset as far as the data goes): zeroing a removed value must
/// never change a byte a reader could parse as IFD structure, such as an unread table's count.
/// </remarks>
sealed class RemovalPlan
{
	// TIFF 6.0 offset/length pairs: JPEGInterchangeFormat and its length (0x0201/0x0202, the IFD1 thumbnail,
	// Exif 2.32 §4.6.5), StripOffsets/StripByteCounts (0x0111/0x0117) and TileOffsets/TileByteCounts (0x0144/0x0145).
	static readonly (int Offsets, int Lengths)[] DataPairs = [(0x0201, 0x0202), (0x0111, 0x0117), (0x0144, 0x0145)];

	public CountedList<(long Start, long End)> Protected { get; } = new ();
	public CountedList<(long Start, long End)> Removed { get; } = new ();

	/// <summary>
	/// The kept entries' values and the data their offset/length pairs name, in tables some reader reads and wholly
	/// inside the data (a value past the data is read by no one: exiftool reports a bad offset), with the tag that
	/// names each.
	/// </summary>
	public CountedList<(long Start, long End, int Tag)> Kept { get; } = new ();

	/// <summary>For each table that loses an entry, which of its entries go.</summary>
	public Dictionary<long, CountedList<bool>> Removing { get; } = new ();

	/// <summary>An offset/length pair is given twice, so which bytes it names is unknown: nothing is zeroed.</summary>
	public bool Ambiguous { get; private set; }

	public static RemovalPlan Mark (TiffReader tiff, IfdGraph graph, HashSet<int> tags)
	{
		var plan = new RemovalPlan ();
		for (var t = 0; t < graph.Tables.Count; t++) {
			var (at, count, roles) = graph.Tables[t];
			var seen = (roles & IfdGraph.SeenRoles) != 0;
			plan.Protected.Add (TiffReader.TableRange (at, count));
			plan.Ambiguous |= !plan.AddDataRanges (tiff, at, count, seen);
			plan.MarkEntries (tiff, at, count, edited: (roles & IfdGraph.ReadRoles) != 0, seen, tags);
		}

		for (var n = 0; n < graph.Unread.Count; n++) {
			var at = graph.Unread[n];
			var entries = at + 2 <= tiff.Length ? tiff.U16 (at) : 0;
			plan.Protected.Add ((at, Math.Min (at + 2 + (entries * 12L) + 4, tiff.Length)));
		}

		return plan;
	}

	// Kept values of a table some reader reads are compared after a checked edit; every kept value is protected. The
	// out-of-line value of an Exif, GPS or interoperability IFD pointer (count 2, or a wide type) is shown by no reader
	// (exiftool: "Bad value"); what it leads to is compared as tables.
	void MarkEntries (TiffReader tiff, long at, int count, bool edited, bool seen, HashSet<int> tags)
	{
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (at, n);
			var range = tiff.ValueRange (entry);
			if (edited && tags.Contains (tiff.Tag (entry))) {
				if (!Removing.TryGetValue (at, out var marks))
					Removing[at] = marks = CountedList<bool>.Filled (count);
				marks[n] = true;
				if (range.End > range.Start)
					Removed.Add (range);
			} else if (range.End > range.Start) {
				Protected.Add (range);
				if (seen && range.End <= tiff.Length && tiff.Tag (entry) is not (TiffReader.ExifIfdPointer or TiffReader.GpsIfdPointer or TiffReader.InteropIfdPointer))
					Kept.Add ((range.Start, range.End, tiff.Tag (entry)));
			}
		}
	}

	/// <summary>
	/// Whether the structure is clean: no table, value, offset/length data or would-be table that a pointer names
	/// shares a byte with another, and no offset/length pair is given twice. Then compaction and zeroing change
	/// nothing but the copies (a would-be table keeps its bytes, so it stays unreadable), and the edit needs no check.
	/// Editing only clean structure would mean refusing the rest here.
	/// </summary>
	public bool IsClean ()
	{
		if (Ambiguous)
			return false;
		var all = new CountedList<(long Start, long End)> ();
		for (var n = 0; n < Protected.Count; n++)
			all.Add (Protected[n]);
		for (var n = 0; n < Removed.Count; n++)
			all.Add (Removed[n]);
		all.Sort ((a, b) => a.Start.CompareTo (b.Start));
		var end = long.MinValue;
		for (var n = 0; n < all.Count; n++) {
			var range = all[n];
			if (range.Start < end)
				return false;
			end = range.End;
		}

		return true;
	}

	/// <summary>Compacts every table that loses an entry, then zeroes the freed values no other structure uses.</summary>
	public void Apply (Span<byte> data, TiffReader tiff, IfdGraph graph)
	{
		for (var t = 0; t < graph.Tables.Count; t++) {
			var (at, count, _) = graph.Tables[t];
			if (Removing.TryGetValue (at, out var marks))
				CompactTable (data, tiff, at, count, marks);
		}

		if (Ambiguous)
			return;

		// One sort, then a binary search per removed value: linear-logarithmic in the entries, whatever their number.
		var merged = ByteRanges.Merge (Protected);
		for (var n = 0; n < Removed.Count; n++) {
			var range = Removed[n];
			if (range.Start >= TiffReader.HeaderSize && range.End <= data.Length && !ByteRanges.Overlaps (merged, range))
				data.Slice ((int)range.Start, (int)(range.End - range.Start)).Clear ();
		}
	}

	// Moves the kept entries up and the next-IFD offset after them (0 when it was missing or cut short), then clears
	// the freed bytes up to the end of the old offset, or of the data when the table ended there.
	static void CompactTable (Span<byte> data, TiffReader tiff, long ifd, int count, CountedList<bool> removing)
	{
		var at = (int)ifd;
		var table = at + 2;
		var kept = 0;
		for (var n = 0; n < count; n++) {
			if (removing[n])
				continue;
			if (kept != n)
				data.Slice (table + (n * 12), 12).CopyTo (data.Slice (table + (kept * 12)));
			kept++;
		}

		var nextAt = table + (count * 12);
		var next = nextAt + 4 <= data.Length ? Endian.U32 (data, nextAt, tiff.LittleEndian) : 0;
		Endian.WriteU16 (data, at, kept, tiff.LittleEndian);
		var newNext = table + (kept * 12);
		Endian.WriteU32 (data, newNext, next, tiff.LittleEndian);
		data.Slice (newNext + 4, Math.Min (nextAt + 4, data.Length) - (newNext + 4)).Clear ();
	}

	// False when a tag of a pair appears more than once, so its ranges cannot be told.
	bool AddDataRanges (TiffReader tiff, long ifd, int count, bool seen)
	{
		var single = true;
		foreach (var (offsetsTag, lengthsTag) in DataPairs) {
			var offsets = EntryValues (tiff, ifd, count, offsetsTag, ref single);
			var lengths = EntryValues (tiff, ifd, count, lengthsTag, ref single);
			for (var n = 0; n < offsets.Count && n < lengths.Count; n++) {
				Protected.Add ((offsets[n], offsets[n] + lengths[n]));
				if (seen && offsets[n] + lengths[n] <= tiff.Length)
					Kept.Add ((offsets[n], offsets[n] + lengths[n], offsetsTag));
			}
		}

		return single;
	}

	// The SHORT or LONG values of the first entry with the given tag, or none; bounded by the bytes present.
	static CountedList<long> EntryValues (TiffReader tiff, long ifd, int count, int tag, ref bool single)
	{
		var values = new CountedList<long> ();
		var found = FindEntry (tiff, ifd, count, tag, ref single);
		if (found < 0)
			return values;
		var entry = tiff.Entry (ifd, found);
		var size = tiff.Type (entry) switch { 3 => 2, 4 => 4, _ => 0 };
		var items = tiff.Count (entry);
		if (size == 0)
			return values;
		var at = items * size <= 4 ? entry + 8 : tiff.Value (entry);
		if (at + (items * size) > tiff.Length)
			return values;
		for (var k = 0; k < items; k++) {
			IfdSteps.Count++;
			var p = (int)at + (int)(k * size);
			values.Add (size == 2 ? tiff.U16 (p) : tiff.U32 (p));
		}

		return values;
	}

	// The first entry with the tag, or -1; <paramref name="single"/> turns false when a second entry has it.
	static int FindEntry (TiffReader tiff, long ifd, int count, int tag, ref bool single)
	{
		var found = -1;
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			if (tiff.Tag (tiff.Entry (ifd, n)) != tag)
				continue;
			if (found >= 0) {
				single = false;
				break;
			}

			found = n;
		}

		return found;
	}
}
