// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// Whether an edit changed anything a reader sees besides the removed copies. Walked again with the same rules, the
/// edited TIFF must reach the same tables some reader reads, in the same roles; each must stay readable to exiftool
/// or stay dropped, as its first entry's format decides; each must hold the same entries less
/// the removed ones, in order, with the same next offset; and every kept entry's value and
/// every range an offset/length pair names must keep its bytes. Linear-logarithmic: one walk, one sort.
/// </summary>
static class EditCheck
{
	public static bool KeepsEverythingElse (TiffReader before, long ifd0, IfdGraph graph, RemovalPlan plan, ReadOnlySpan<byte> edited)
	{
		var after = new TiffReader (edited, before.LittleEndian);
		var walked = IfdGraph.Walk (after, ifd0);
		return !walked.OverBudget && SameTables (before, after, graph, walked, plan) && SameKeptBytes (before.Data, edited, plan);
	}

	// The tables some reader reads, in walk order, with their roles and entries. A pointer target no reader could read
	// that becomes readable is a table gained; tables only protected (the next IFD of an Exif IFD) are read by no one.
	static bool SameTables (TiffReader before, TiffReader after, IfdGraph graph, IfdGraph walked, RemovalPlan plan)
	{
		int t = 0, w = 0;
		while (true) {
			t = NextSeen (graph, t);
			w = NextSeen (walked, w);
			if (t == graph.Tables.Count || w == walked.Tables.Count)
				return t == graph.Tables.Count && w == walked.Tables.Count;
			var (at, count, roles) = graph.Tables[t++];
			var (atAfter, countAfter, rolesAfter) = walked.Tables[w++];
			plan.Removing.TryGetValue (at, out var marks);
			if (at != atAfter || (roles & IfdGraph.SeenRoles) != (rolesAfter & IfdGraph.SeenRoles) || !SameEntries (before, after, at, count, countAfter, marks, roles))
				return false;
		}
	}

	static int NextSeen (IfdGraph graph, int from)
	{
		while (from < graph.Tables.Count && (graph.Tables[from].Roles & IfdGraph.SeenRoles) == 0)
			from++;
		return from;
	}

	// The kept entries, in order, and the next offset after them. exiftool drops a whole table when its first
	// entry's format is outside 1-13 and reads an empty table. That change matters when a kept entry it would
	// show then appears or disappears, or when a main table's next IFD (itself a table) starts or stops being
	// followed. A kept entry it already skips changes nothing a reader shows. The final count check is `>=`, not
	// `==`: the loop refuses an entry past the edited count, and an equal check would still refuse it, leaving
	// that loop check dead. The entry past the edited count is still inside the table as it was read, so the
	// comparison does not run off the end even when that table ends on the data's last byte.
	static bool SameEntries (TiffReader before, TiffReader after, long at, int count, int countAfter, CountedList<bool>? marks, int roles)
	{
		if (FirstEntryChangeIsVisible (before, after, at, count, countAfter, marks, roles))
			return false;
		var kept = 0;
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			if (marks is not null && marks[n])
				continue;
			if (kept >= countAfter || !SameEntry (before, after, before.Entry (at, n), after.Entry (at, kept)))
				return false;
			kept++;
		}

		return kept >= countAfter && before.Next (at, count) == after.Next (at, countAfter);
	}

	// An empty table is read. A table with entries is read when its first entry's format is one exiftool reads.
	static bool FirstEntryChangeIsVisible (TiffReader before, TiffReader after, long at, int count, int countAfter, CountedList<bool>? marks, int roles)
	{
		if (ReadsFirst (before, at, count) == ReadsFirst (after, at, countAfter))
			return false;
		for (var n = 0; n < count; n++) {
			if (marks is not null && marks[n])
				continue;
			if (TiffReader.FormatExiftoolReads (before.Type (before.Entry (at, n))))
				return true;
		}

		// exiftool follows the next IFD of a main table only while it reads that table. A next offset that is
		// itself a table then appears or disappears with the change; any other next offset leads nowhere.
		var next = before.Next (at, count);
		return (roles & IfdGraph.MainRole) != 0 && next != 0 && before.TryTable (next, out _);
	}

	static bool ReadsFirst (TiffReader tiff, long at, int count) =>
		count == 0 || TiffReader.FormatExiftoolReads (tiff.Type (tiff.Entry (at, 0)));

	// Tag, type, count and value field byte for byte; but an out-of-line value that runs past the data shows nothing
	// wherever its offset points, so two such offsets are the same to a reader.
	static bool SameEntry (TiffReader before, TiffReader after, long entry, long entryAfter)
	{
		if (!before.Data.Slice ((int)entry, 8).SequenceEqual (after.Data.Slice ((int)entryAfter, 8)))
			return false;
		if (before.Data.Slice ((int)entry + 8, 4).SequenceEqual (after.Data.Slice ((int)entryAfter + 8, 4)))
			return true;
		return before.ValueRange (entry).End > before.Length && after.ValueRange (entryAfter).End > after.Length;
	}

	static bool SameKeptBytes (ReadOnlySpan<byte> before, ReadOnlySpan<byte> after, RemovalPlan plan)
	{
		var ranges = new CountedList<(long Start, long End)> ();
		for (var n = 0; n < plan.Kept.Count; n++) {
			var (start, end, _) = plan.Kept[n];
			ranges.Add ((Math.Max (start, 0), Math.Min (end, before.Length)));
		}

		var merged = ByteRanges.Merge (ranges);
		for (var n = 0; n < merged.Count; n++) {
			var (start, end) = merged[n];
			if (!before.Slice ((int)start, (int)(end - start)).SequenceEqual (after.Slice ((int)start, (int)(end - start))))
				return false;
		}

		return true;
	}
}
