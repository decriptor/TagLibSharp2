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
/// removed), the data that offset/length pairs name (the IFD1 thumbnail, strips, tiles), the structure of every
/// table no reader reads (its count, entries and next offset as far as the data goes), and the bytes a maker note
/// (Exif 2.32 §4.6.5 tag 0x927C) or one of its entries names. A removed value inside the note's own block still
/// overlaps that kept value, so the edit is refused. A value the note's entries name past the block is kept and
/// the edit runs: zeroing it would change the note, and refusing would reject a plain file.
/// </remarks>
sealed class RemovalPlan
{
	// TIFF 6.0 offset/length pairs: JPEGInterchangeFormat and its length (0x0201/0x0202, the IFD1 thumbnail,
	// Exif 2.32 §4.6.5), StripOffsets/StripByteCounts (0x0111/0x0117) and TileOffsets/TileByteCounts (0x0144/0x0145).
	static readonly (int Offsets, int Lengths)[] DataPairs = [(0x0201, 0x0202), (0x0111, 0x0117), (0x0144, 0x0145)];

	// Exif 2.32 §4.6.5. The block is undefined; a classic IFD in it is the note's own directory.
	const int MakerNoteTag = 0x927C;
	const int MakerNotePrefix = 32;

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

	/// <summary>
	/// The offset and length arrays name more values, across every table, than half the bytes of the block. The
	/// plan then does not hold every range, so an edit that has something to remove is refused rather than applied.
	/// </summary>
	public bool ValueBudgetExceeded { get; private set; }

	// Each distinct SHORT or LONG array once. Keyed as a pointer array is: where it starts, how many values, and
	// how wide each value is. A plain edit names each array once; the cache still keeps a skipped check from
	// reading one array twice.
	readonly Dictionary<ValueArray, long[]> _values = new ();
	long _valuesRead;

	// The maker-note block and the values its entries name. Apply skips these bytes. They are not protected:
	// a share with them is not a reason to refuse.
	readonly CountedList<(long Start, long End)> _makerOwned = new ();

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

	// Every kept value is protected. The out-of-line value of an Exif, GPS or interoperability IFD pointer (count 2,
	// or a wide type) is shown by no reader (exiftool: "Bad value"); what it leads to is a table.
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
				if (tiff.Tag (entry) == MakerNoteTag && tiff.Type (entry) == 7)
					RecordMakerNote (tiff, range);
			}
		}
	}

	/// <summary>
	/// Whether zeroing the removed values would leave every protected byte alone, and no offset/length pair is given
	/// twice. Two protected ranges may share a byte: that share is not zeroed. A table being compacted is a separate
	/// question, answered where the edit is applied.
	/// </summary>
	public bool IsClean ()
	{
		if (Ambiguous || ValueBudgetExceeded)
			return false;
		if (Removed.Count == 0 || Protected.Count == 0)
			return true;
		var guarded = new CountedList<(long Start, long End)> ();
		for (var n = 0; n < Protected.Count; n++)
			guarded.Add (Protected[n]);
		var merged = ByteRanges.Merge (guarded);
		for (var n = 0; n < Removed.Count; n++) {
			if (ByteRanges.Overlaps (merged, Removed[n]))
				return false;
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
		var maker = ByteRanges.Merge (_makerOwned);
		for (var n = 0; n < Removed.Count; n++) {
			var range = Removed[n];
			if (range.Start >= TiffReader.HeaderSize && range.End <= data.Length && !ByteRanges.Overlaps (merged, range))
				ClearExcept (data, range, maker);
		}
	}

	// Zeroes [range) except where a maker-note range covers it. Both lists are sorted and disjoint, so the covered
	// part is one forward pass from the first note range that ends after the value starts.
	static void ClearExcept (Span<byte> data, (long Start, long End) range, CountedList<(long Start, long End)> maker)
	{
		var cursor = range.Start;
		int low = 0, high = maker.Count;
		while (low < high) {
			IfdSteps.Count++;
			var mid = low + ((high - low) / 2);
			if (maker[mid].End <= range.Start)
				low = mid + 1;
			else
				high = mid;
		}

		for (var n = low; n < maker.Count; n++) {
			var keep = maker[n];
			if (keep.Start >= range.End)
				break;
			var keepStart = Math.Max (keep.Start, range.Start);
			var keepEnd = Math.Min (keep.End, range.End);
			if (cursor < keepStart)
				data.Slice ((int)cursor, (int)(keepStart - cursor)).Clear ();
			if (keepEnd > cursor)
				cursor = keepEnd;
		}

		if (cursor < range.End)
			data.Slice ((int)cursor, (int)(range.End - cursor)).Clear ();
	}

	// The note's own bytes, then any value a classic IFD in the note names outside that block. The IFD may sit at
	// the start or after at most 32 bytes of ASCII or NUL (Panasonic's "Panasonic" header is 12). One pass over
	// the entries. Offsets that all land inside the block are counted from the block, so nothing outside it is
	// claimed. Offsets that all land inside the TIFF are counted from the TIFF, and only those outside the block
	// are kept as well. A mix that fits neither is left to the block alone.
	void RecordMakerNote (TiffReader tiff, (long Start, long End) blob)
	{
		var end = Math.Min (blob.End, tiff.Length);
		if (blob.Start < 0 || end <= blob.Start)
			return;
		_makerOwned.Add ((blob.Start, end));
		if (TryMakerTargets (tiff, blob.Start, end, out var outside)) {
			for (var n = 0; n < outside.Count; n++)
				_makerOwned.Add (outside[n]);
		}
	}

	static bool TryMakerTargets (TiffReader tiff, long blobStart, long blobEnd, out CountedList<(long Start, long End)> outside)
	{
		outside = new CountedList<(long Start, long End)> ();
		var last = blobStart + MakerNotePrefix;
		for (var at = blobStart; at + 2 <= blobEnd && at <= last; at++) {
			if (TryNoteIfd (tiff, at, blobStart, blobEnd, out outside))
				return true;
			if (at < 0 || at >= tiff.Length || !IsAsciiOrNul (tiff.Data[(int)at]))
				break;
		}

		return false;
	}

	// A classic IFD wholly inside the block: a count, sorted tags, types 1-12 (TIFF 6.0 §2). False leaves
	// <paramref name="outside"/> unused.
	static bool TryNoteIfd (
		TiffReader tiff,
		long at,
		long blobStart,
		long blobEnd,
		out CountedList<(long Start, long End)> outside)
	{
		outside = new CountedList<(long Start, long End)> ();
		if (at < 0 || at > tiff.Length - 2)
			return false;
		var count = tiff.U16 (at);
		if (count <= 0)
			return false;
		var tableEnd = at + 2 + (count * 12L) + 4;
		if (tableEnd < at || tableEnd > blobEnd)
			return false;
		var fromNote = true;
		var fromTiff = true;
		var extra = new CountedList<(long Start, long End)> ();
		long previous = -1;
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (at, n);
			var tag = tiff.Tag (entry);
			var type = tiff.Type (entry);
			if (tag <= previous || type is < 1 or > 12)
				return false;
			previous = tag;
			var range = tiff.ValueRange (entry);
			var size = range.End - range.Start;
			if (size <= 0)
				continue;
			var offset = range.Start;
			if (!Fits (blobStart, offset, size, blobEnd))
				fromNote = false;
			if (!Fits (0, offset, size, tiff.Length))
				fromTiff = false;
			else if (!WhollyInside (offset, size, blobStart, blobEnd))
				extra.Add ((offset, offset + size));
		}

		if (!fromNote && fromTiff)
			outside = extra;
		return true;
	}

	static bool Fits (long origin, long offset, long size, long limit)
	{
		if (offset < 0 || size <= 0 || origin > long.MaxValue - offset)
			return false;
		var start = origin + offset;
		return start <= limit && size <= limit - start;
	}

	static bool WhollyInside (long start, long size, long low, long high) =>
		start >= low && start <= high && size <= high - start;

	static bool IsAsciiOrNul (byte value) => value == 0 || value is >= 0x20 and <= 0x7E;

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

	// False when a tag of a pair appears more than once, so its ranges cannot be told. Each array is read once.
	// Values read across every table are bounded by half the bytes present: two arrays that both fit can pass that
	// only by overlapping, and past it the plan stops reading rather than allocating. A plain edit never reaches
	// that bound; it is refused from the entry headers first.
	bool AddDataRanges (TiffReader tiff, long ifd, int count, bool seen)
	{
		var single = true;
		foreach (var (offsetsTag, lengthsTag) in DataPairs) {
			var hasOffsets = TryKey (tiff, ifd, count, offsetsTag, ref single, out var offsetsKey);
			var hasLengths = TryKey (tiff, ifd, count, lengthsTag, ref single, out var lengthsKey);
			if (!hasOffsets || !hasLengths || ValueBudgetExceeded)
				continue;
			if (!TryValues (tiff, offsetsKey, out var offsets) || !TryValues (tiff, lengthsKey, out var lengths))
				continue;
			var shared = Math.Min (offsets.Length, lengths.Length);
			for (var n = 0; n < shared; n++) {
				Protected.Add ((offsets[n], offsets[n] + lengths[n]));
				if (seen && offsets[n] + lengths[n] <= tiff.Length)
					Kept.Add ((offsets[n], offsets[n] + lengths[n], offsetsTag));
			}
		}

		return single;
	}

	// The first SHORT or LONG entry with the tag, when every value lies in the data.
	static bool TryKey (TiffReader tiff, long ifd, int count, int tag, ref bool single, out ValueArray key)
	{
		key = default;
		var found = FindEntry (tiff, ifd, count, tag, ref single);
		if (found < 0)
			return false;
		var entry = tiff.Entry (ifd, found);
		var size = tiff.Type (entry) switch { 3 => 2, 4 => 4, _ => 0 };
		var items = tiff.Count (entry);
		if (size == 0 || items == 0)
			return false;
		var at = items * size <= 4 ? entry + 8 : tiff.Value (entry);
		if (at < 0 || at > tiff.Length || items > (tiff.Length - at) / size)
			return false;
		key = new ValueArray (at, items, size);
		return true;
	}

	// The values of one array, from the cache when a table already named it. Past the bound, or a count no array
	// can hold, nothing is allocated and the plan is incomplete.
	bool TryValues (TiffReader tiff, ValueArray key, out long[] values)
	{
		if (_values.TryGetValue (key, out var cached)) {
			values = cached;
			return true;
		}
		if (key.Count > int.MaxValue || key.Count > tiff.Length / 2 - _valuesRead) {
			ValueBudgetExceeded = true;
			values = [];
			return false;
		}

		var count = (int)key.Count;
		values = new long[count];
		for (var k = 0; k < count; k++) {
			IfdSteps.Count++;
			var p = (int)key.At + (k * key.Size);
			values[k] = key.Size == 2 ? tiff.U16 (p) : tiff.U32 (p);
		}

		_valuesRead += count;
		_values[key] = values;
		return true;
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

/// <summary>One SHORT or LONG value array: where it starts, how many values, and the size of each.</summary>
readonly record struct ValueArray (long At, long Count, int Size);
