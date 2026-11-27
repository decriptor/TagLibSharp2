// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// Whether a field edit may run. One pass over the tables already walked, before a <see cref="RemovalPlan"/> records
/// a range or reads a value array. The structure is not plain when a strip, tile or thumbnail offset or length is
/// not SHORT or LONG, when one of those arrays is named twice, when a complete pair of them names more values than
/// half the bytes, when a classic next-IFD field has only one, two or three of its four bytes (TIFF 6.0 §2), or
/// when removing an entry would change whether exiftool reads that table's first entry. An absent next field is
/// plain. An empty table is read. A first-entry change that shows nothing new is still refused.
/// </summary>
static class PlainExif
{
	/// <summary>A plain block, whether the edit removes an entry, and why it is not plain.</summary>
	public readonly record struct Verdict (bool Plain, bool Removes, string? Why);

	/// <summary>
	/// Strip, tile and thumbnail offset/length tags (TIFF 6.0 §3, tags 0x0111/0x0117, 0x0144/0x0145, and
	/// JPEGInterchangeFormat 0x0201/0x0202) are plain only as SHORT (3) or LONG (4), and only when each array is
	/// named once. A complete pair whose values, counted from the entry headers, exceed half the bytes is not
	/// plain. A next-IFD field that is partly in the data is not plain. The first entry exiftool will see is
	/// plain only when <see cref="TiffReader.FormatExiftoolReads"/> does not change; GPS tables are not edited.
	/// </summary>
	public static Verdict Judge (TiffReader tiff, IfdGraph graph, HashSet<int> tags)
	{
		string? why = null;
		var removes = false;
		long budget = 0;
		HashSet<ValueArray>? named = null;
		HashSet<ValueArray>? counted = null;
		// One slot per side of the three pairs, reused for every table. Bits of `got` say which sides this table has.
		var slots = new ValueArray[6];
		for (var t = 0; t < graph.Tables.Count; t++) {
			var (at, count, roles) = graph.Tables[t];
			var edited = (roles & IfdGraph.ReadRoles) != 0;
			// Entry(at, count) is the first byte after the entries: the classic 4-byte next-IFD field.
			var nextAt = tiff.Entry (at, count);
			if (why is null && nextAt < tiff.Length && nextAt + 4 > tiff.Length)
				why = "has an IFD whose next offset is cut short";
			var firstType = 0;
			var keptType = 0;
			var keptOne = false;
			var loses = false;
			var got = 0;
			for (var n = 0; n < count; n++) {
				IfdSteps.Count++;
				var entry = tiff.Entry (at, n);
				var tag = tiff.Tag (entry);
				if (n == 0)
					firstType = tiff.Type (entry);
				if (edited && tags.Contains (tag)) {
					removes = true;
					loses = true;
				} else if (!keptOne) {
					keptOne = true;
					keptType = tiff.Type (entry);
				}

				if (why is not null || !IsPairTag (tag) || tiff.Count (entry) == 0)
					continue;
				if (tiff.Type (entry) is not (3 or 4))
					why = "has a strip, tile or thumbnail offset or length that is not SHORT or LONG";
				else if (TryValueArray (tiff, entry, out var key)) {
					// One pass over entries. A second name of the same array is a shared or cross-paired pair.
					named ??= [];
					if (!named.Add (key))
						why = "names one strip, tile or thumbnail array more than once";
					var slot = PairSlot (tag);
					slots[slot] = key;
					got |= 1 << slot;
				}
			}

			// Both sides of a pair, from this table's headers. A lone offset or length is not a value the plan reads.
			if (why is null && got != 0) {
				for (var pair = 0; pair < 6; pair += 2) {
					if ((got & (3 << pair)) != (3 << pair))
						continue;
					counted ??= [];
					if (counted.Add (slots[pair]))
						budget += slots[pair].Count;
					if (counted.Add (slots[pair + 1]))
						budget += slots[pair + 1].Count;
				}
			}

			// An empty table is read. A table with entries is read when its first format is one exiftool reads.
			if (why is null && loses) {
				var before = count == 0 || TiffReader.FormatExiftoolReads (firstType);
				var after = !keptOne || TiffReader.FormatExiftoolReads (keptType);
				if (before != after)
					why = "would change whether exiftool reads a table's first entry";
			}

			if (why is not null && removes)
				break;
		}

		// Counts come from the entry headers. Equality with half the bytes still fits, as two arrays can.
		if (why is null && removes && budget > tiff.Length / 2)
			why = "names more strip, tile or thumbnail values than its data holds";

		return new Verdict (why is null, removes, why);
	}

	static bool IsPairTag (int tag) => tag is 0x0111 or 0x0117 or 0x0144 or 0x0145 or 0x0201 or 0x0202;

	// Strip, tile, then thumbnail; the offset side then the length side.
	static int PairSlot (int tag) => tag switch {
		0x0111 => 0,
		0x0117 => 1,
		0x0144 => 2,
		0x0145 => 3,
		0x0201 => 4,
		0x0202 => 5,
		_ => 0
	};

	// A SHORT or LONG array that lies wholly in the data. Inline values are the entry's own value field.
	static bool TryValueArray (TiffReader tiff, long entry, out ValueArray key)
	{
		key = default;
		var size = tiff.Type (entry) == 3 ? 2 : 4;
		var items = tiff.Count (entry);
		if (items == 0)
			return false;
		var at = items * size <= 4 ? entry + 8 : tiff.Value (entry);
		if (at < 0 || at > tiff.Length || items > (tiff.Length - at) / size)
			return false;
		key = new ValueArray (at, items, size);
		return true;
	}
}
