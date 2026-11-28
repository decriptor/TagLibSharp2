// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// Whether a field edit may run. One pass over the tables already walked, before a <see cref="RemovalPlan"/> records
/// a range or reads a value array. The structure is not plain when a classic next-IFD field has only one, two or
/// three of its four bytes (TIFF 6.0 §2), when one table names the same strip, tile or thumbnail offset or length
/// twice, when one of those is not SHORT or LONG, when one of those arrays does not lie wholly in the data, when
/// one array is named twice, when removing an entry would change whether exiftool reads that table's first entry,
/// or when a complete pair names more values than half the bytes. The first of these reasons stands; a later check
/// does not replace it. An absent next field is plain. An empty table is read. A first-entry change that shows
/// nothing new is still refused.
/// </summary>
static class PlainExif
{
	/// <summary>A plain block, whether the edit removes an entry, and why it is not plain.</summary>
	public readonly record struct Verdict (bool Plain, bool Removes, string? Why);

	/// <summary>A classic next-IFD field has some, but not all, of its four bytes.</summary>
	internal const string NextCutShort = "has an IFD whose next offset is cut short";

	/// <summary>One table names the same strip, tile or thumbnail offset or length tag twice.</summary>
	internal const string PairTagRepeated = "names a strip, tile or thumbnail offset or length more than once";

	/// <summary>A strip, tile or thumbnail offset or length is not SHORT or LONG.</summary>
	internal const string PairTypeWrong = "has a strip, tile or thumbnail offset or length that is not SHORT or LONG";

	/// <summary>Two entries name one strip, tile or thumbnail array.</summary>
	internal const string PairArrayRepeated = "names one strip, tile or thumbnail array more than once";

	/// <summary>A strip, tile or thumbnail array does not lie wholly in the data.</summary>
	internal const string PairDoesNotFit = "has a strip, tile or thumbnail array that does not fit in its data";

	/// <summary>Removing an entry would change whether exiftool reads the table's first entry.</summary>
	internal const string FirstEntryChanges = "would change whether exiftool reads a table's first entry";

	/// <summary>Complete offset/length pairs name more values than half the bytes.</summary>
	internal const string OverBudget = "names more strip, tile or thumbnail values than its data holds";

	/// <summary>
	/// Strip, tile and thumbnail offset/length tags (TIFF 6.0 §3, tags 0x0111/0x0117, 0x0144/0x0145, and
	/// JPEGInterchangeFormat 0x0201/0x0202) are plain only as SHORT (3) or LONG (4), and only when each array lies
	/// wholly in the data and is named once. A complete pair whose values, counted from the entry headers, exceed
	/// half the bytes is not plain. A next-IFD field that is partly in the data is not plain. The first entry exiftool will see is
	/// plain only when <see cref="TiffReader.FormatExiftoolReads"/> does not change; GPS tables are not edited.
	/// </summary>
	public static Verdict Judge (TiffReader tiff, IfdGraph graph, HashSet<int> tags)
	{
		string? why = null;
		var removes = false;
		long budget = 0;
		HashSet<ValueArray>? named = null;
		HashSet<ValueArray>? counted = null;
		var slots = new ValueArray[6];
		for (var t = 0; t < graph.Tables.Count; t++) {
			var (at, count, roles) = graph.Tables[t];
			why ??= NextFieldCutShort (tiff, at, count);
			var notes = NoteEntries (tiff, at, count, (roles & IfdGraph.ReadRoles) != 0, tags, slots, ref named, ref why);
			removes |= notes.Removes;
			if (why is null)
				budget += CompletePairCounts (slots, notes.Got, ref counted);
			why ??= FirstEntryReadability (count, notes);
			if (why is not null && removes)
				break;
		}

		// Counts come from the entry headers. The first reason stands: a cut-short next field, a pair tag given
		// twice, a type other than SHORT or LONG, an array that does not fit, one array named twice, then a first
		// entry whose readability would change. The budget is reported only when none of those was found.
		if (why is null && removes && ValueBudget.PastHalf (0, budget, tiff.Length))
			why = OverBudget;
		return new Verdict (why is null, removes, why);
	}

	// Entry(at, count) is the first byte after the entries: the classic 4-byte next-IFD field.
	static string? NextFieldCutShort (TiffReader tiff, long at, int count)
	{
		var nextAt = tiff.Entry (at, count);
		return nextAt < tiff.Length && nextAt + 4 > tiff.Length ? NextCutShort : null;
	}

	static TableNotes NoteEntries (
		TiffReader tiff,
		long at,
		int count,
		bool edited,
		HashSet<int> tags,
		ValueArray[] slots,
		ref HashSet<ValueArray>? named,
		ref string? why)
	{
		var notes = new TableNotes ();
		var seenSide = 0;
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (at, n);
			var tag = tiff.Tag (entry);
			if (n == 0)
				notes.FirstType = tiff.Type (entry);
			if (edited && tags.Contains (tag)) {
				notes.Removes = true;
				notes.Loses = true;
			} else if (!notes.KeptOne) {
				notes.KeptOne = true;
				notes.KeptType = tiff.Type (entry);
			}

			// Once a reason stands it is kept, and later entries are not read for another one.
			if (why is null)
				why = PairEntry (tiff, entry, tag, ref seenSide, ref notes, slots, ref named);
		}

		return notes;
	}

	// A second entry with the same pair tag names a different array. Which bytes it means is unknown.
	static string? PairEntry (
		TiffReader tiff,
		long entry,
		int tag,
		ref int seenSide,
		ref TableNotes notes,
		ValueArray[] slots,
		ref HashSet<ValueArray>? named)
	{
		var slot = OffsetLengthPairs.Slot (tag);
		if (slot < 0)
			return null;
		var repeated = (seenSide & (1 << slot)) != 0;
		seenSide |= 1 << slot;
		if (tiff.Count (entry) == 0 || repeated)
			return repeated ? PairTagRepeated : null;
		if (tiff.Type (entry) is not (3 or 4))
			return PairTypeWrong;
		if (!tiff.TryValueArray (entry, out var key))
			return PairDoesNotFit;
		named ??= [];
		if (!named.Add (key))
			return PairArrayRepeated;
		slots[slot] = key;
		notes.Got |= 1 << slot;
		return null;
	}

	// Both sides of a pair, from this table's headers. A lone offset or length is not a value the plan reads.
	static long CompletePairCounts (ValueArray[] slots, int got, ref HashSet<ValueArray>? counted)
	{
		if (got == 0)
			return 0;
		long added = 0;
		for (var pair = 0; pair < 6; pair += 2) {
			if ((got & (3 << pair)) != (3 << pair))
				continue;
			counted ??= [];
			if (counted.Add (slots[pair]))
				added += slots[pair].Count;
			if (counted.Add (slots[pair + 1]))
				added += slots[pair + 1].Count;
		}

		return added;
	}

	// An empty table is read. A table with entries is read when its first format is one exiftool reads.
	static string? FirstEntryReadability (int count, TableNotes notes)
	{
		if (!notes.Loses)
			return null;
		var before = count == 0 || TiffReader.FormatExiftoolReads (notes.FirstType);
		var after = !notes.KeptOne || TiffReader.FormatExiftoolReads (notes.KeptType);
		return before != after ? FirstEntryChanges : null;
	}

	struct TableNotes
	{
		public bool Removes;
		public bool Loses;
		public int FirstType;
		public int KeptType;
		public bool KeptOne;
		public int Got;
	}
}
