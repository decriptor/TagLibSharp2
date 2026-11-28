// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// Removes each changed field's Exif entries (all its tags) from every table exiftool reads with its Exif tags:
/// IFD0 and the chain of IFDs after it, and every Exif IFD (0x8769), SubIFD (0x014A) and interoperability IFD
/// (0xA005) any of those points at, however deep, each pointer decoded as exiftool decodes it
/// (<see cref="TiffReader.TryPointers"/>, <see cref="IfdGraph"/>; ImageIO reads a subset), and the Exif IFD that
/// IFD0's 0x8769 field names read as a LONG whatever its type and count, as TagLib# reads it. GPS tables are
/// read with GPS tags and left as they are. Each table is walked once. The entry table shrinks in place, so no offset
/// moves. A field edit runs only when <see cref="PlainExif"/> calls the structure plain and
/// <see cref="RemovalPlan.IsClean"/> agrees. Anything else is refused and nothing is written.
/// </summary>
/// <remarks>
/// A field edit is refused when a removed value shares a protected byte, or when compacting a table would rewrite
/// any protected range other than that table. Two protected ranges may share a byte; zeroing does not touch them.
/// A pair given twice is not plain, and only the plainness check decides that. Skipping that check still applies
/// the edit from the first of each pair. The plan's value-budget stop stays, so a skipped check cannot read every
/// offset past half the bytes.
/// </remarks>
static class ExifEditor
{
	/// <summary>Compacting the edited tables would rewrite a protected range.</summary>
	internal const string StructureWouldChange = "has IFD structure that removing the copy would change";

	/// <summary>Two IFD tables share a byte, so neither can be compacted.</summary>
	internal const string TablesOverlap = "has IFD tables that overlap each other";

	/// <summary>A SubIFD offset array lies in a table the edit rewrites.</summary>
	internal const string SubIfdInsideTable = "has SubIFD offsets inside an IFD table";

	/// <summary>A kept value lies in a table the edit rewrites.</summary>
	internal const string ValueInsideTable = "has a value inside an IFD table that the edit rewrites";

	/// <summary>An IFD pointer names the middle of another table.</summary>
	internal const string PointerIntoTable = "has an IFD pointer into the middle of another IFD table";

	/// <summary>
	/// A kept entry or a located maker note names bytes past this block, and the edit would move them.
	/// </summary>
	internal const string PastData = "names bytes past its own data that a length change before them would move";

	/// <summary>Where a located maker note points outside the block, and whether every note was located.</summary>
	internal readonly record struct ExifAnchors (bool NotesLocated, (long Start, long End)[] PastBlock);

	/// <summary>The refusal for <see cref="PastData"/>, naming <paramref name="what"/> once.</summary>
	internal static string PastDataReason (string carrier, string what) =>
		$"The {carrier} block {PastData}, so its copy of {what} cannot be updated.";

	/// <summary>Set by tests to see the edit an unchecked removal would make (the matrix judges refusals by it).</summary>
	[ThreadStatic]
	internal static bool SkipCheck;

	/// <summary>Removes the changed fields' entries from the TIFF at <paramref name="start"/>, in place.</summary>
	/// <returns>
	/// False, with an error and nothing written, when the block is not classic TIFF or its tables overlap past the
	/// bound that keeps the walk linear, and its bytes could hold an entry with one of the fields' tags; and when
	/// something is to be removed from structure that is not plain or not clean.
	/// </returns>
	internal static bool TryRemove (byte[] block, int start, ImageTag tag, string carrier, out bool changed, out string? error) =>
		TryRemove (block, start, tag, carrier, out changed, out error, out _);

	internal static bool TryRemove (byte[] block, int start, ImageTag tag, string carrier, out bool changed, out string? error, out ExifAnchors anchors)
	{
		changed = false;
		error = null;
		anchors = new (true, []);
		if (!TryRequest (tag, out var tags, out var what))
			return true;
		var data = block.AsSpan (start);

		// Refused, not skipped: a block that is not classic TIFF (another byte order mark, or the magic 43 of
		// BigTIFF) may still be read by a reader that knows that form, so a copy in it cannot be known to be gone,
		// unless none of the field's tags is in its bytes in either byte order: then no form can hold one.
		if (!TiffReader.TryClassic (data, out var tiff, out var ifd0))
			return (!HoldsAnyTag (data, tags, littleEndian: true, anyType: false) && !HoldsAnyTag (data, tags, littleEndian: false, anyType: false))
				|| Refuse ($"The {carrier} block is not a TIFF structure, so its copy of {what} cannot be updated.", out error);

		// An IFD0 whose entries do not fit is read by no one (exiftool reports a bad IFD0 directory), and every other
		// IFD is reached through it: there is no copy to update.
		if (!tiff.TryTable (ifd0, out _))
			return true;
		var graph = IfdGraph.Walk (tiff, ifd0);

		// Past the bound the walk stops, so a copy beyond it cannot be ruled out, unless none of the field's tags is
		// in the block's bytes at all: then no table can hold one, and there is nothing to remove.
		if (graph.OverBudget)
			return !HoldsAnyTag (data, tags, tiff.LittleEndian, anyType: true)
				|| Refuse ($"The {carrier} block has IFD tables that overlap each other beyond what its data holds, so its copy of {what} cannot be updated.", out error);

		// Plain structure is decided before any range is recorded or any value array is read. Nothing to remove
		// leaves the block as it is, including when the structure is not plain.
		var verdict = PlainExif.Judge (tiff, graph, tags);
		if (!verdict.Removes)
			return true;
		if (!verdict.Plain && !SkipCheck)
			return Refuse ($"The {carrier} block {verdict.Why}, so its copy of {what} cannot be updated.", out error);

		var plan = RemovalPlan.Mark (tiff, graph, tags);
		anchors = new (plan.AllNotesLocated, plan.PastBlock);
		if (plan.Removing.Count == 0)
			return true;
		// The plan stopped before it had read every offset and length, so applying it could zero a strip, tile or
		// thumbnail it never recorded. Nothing to remove never reaches here.
		if (plan.ValueBudgetExceeded)
			return Refuse ($"The {carrier} block {PlainExif.OverBudget}, so its copy of {what} cannot be updated.", out error);
		// A removed value that shares a byte, or a table whose compaction would rewrite something a reader sees.
		// Nothing is written. Two protected ranges may share a byte; zeroing does not touch them.
		if (!SkipCheck && (!plan.IsClean () || CompactionConflict (graph, plan, carrier, what) is not null))
			return Refuse (Reason (graph, plan, carrier, what), out error);
		plan.Apply (block.AsSpan (start), tiff, graph);
		changed = true;
		return true;
	}

	static bool Refuse (string reason, out string? error)
	{
		error = reason;
		return false;
	}

	// The tags of every changed field, and their names for a refusal; false when no changed field has Exif tags.
	static bool TryRequest (ImageTag tag, out HashSet<int> tags, out string what)
	{
		tags = [];
		var names = new List<string> ();
		foreach (var field in MirrorFields.All) {
			if (!tag.Changed (field.Store))
				continue;
			tags.UnionWith (field.Ifd0);
			tags.UnionWith (field.ExifIfd);
			names.Add (field.Name);
		}

		what = string.Join (", ", names);
		return tags.Count > 0;
	}

	// Whether the bytes, at any offset, in the given byte order, could be an entry with one of the tags: the tag
	// followed by any type (TagLib# reads a UserComment of any type) or, for a block that is not classic TIFF, by a
	// field type a TIFF or BigTIFF reader knows (1-18). One pass.
	static bool HoldsAnyTag (ReadOnlySpan<byte> data, HashSet<int> tags, bool littleEndian, bool anyType)
	{
		for (var at = 0; at + 4 <= data.Length; at++) {
			IfdSteps.Count++;
			if (tags.Contains (Endian.U16 (data, at, littleEndian)) && (anyType || Endian.U16 (data, at + 2, littleEndian) is >= 1 and <= 18))
				return true;
		}

		return false;
	}

	// Why a checked edit was refused: the structure the edit would rewrite, named from its shape.
	static string Reason (IfdGraph graph, RemovalPlan plan, string carrier, string what) =>
		CompactionConflict (graph, plan, carrier, what)
		?? StructureChange (carrier, what);

	// The shape that explains a refusal, or null: a table being compacted shares a byte with another table, or an
	// unread table's count, a pointer array or another kept value or data range lies in a table being compacted.
	static string? CompactionConflict (IfdGraph graph, RemovalPlan plan, string carrier, string what)
	{
		if (CompactedRanges (graph, plan.Removing) is not CountedList<(long Start, long End)> compacted)
			return Refusal (carrier, what, TablesOverlap);
		for (var n = 0; n < graph.Unread.Count; n++) {
			var at = graph.Unread[n];
			if (ByteRanges.Overlaps (compacted, (at, at + 2)))
				return Refusal (carrier, what, PointerIntoTable);
		}

		// A kept value inside a table being compacted would be rewritten: a SubIFD offset array (readers would follow it
		// elsewhere), another entry's value, or the data an offset/length pair names.
		for (var n = 0; n < plan.Kept.Count; n++) {
			var (start, end, kind) = plan.Kept[n];
			if (ByteRanges.Overlaps (compacted, (start, end)))
				return Refusal (carrier, what, kind == TiffReader.SubIfdsPointer ? SubIfdInsideTable : ValueInsideTable);
		}

		// A protected range inside a table being compacted (a pointer's value, another table's bytes) would be
		// rewritten. The table's own range is one of those protected ranges and is the edit, so it does not count.
		for (var n = 0; n < plan.Protected.Count; n++) {
			var range = plan.Protected[n];
			if (!ByteRanges.Overlaps (compacted, range) || IsCompactedTable (compacted, range))
				continue;
			return StructureChange (carrier, what);
		}

		return null;
	}

	static string StructureChange (string carrier, string what) =>
		Refusal (carrier, what, StructureWouldChange);

	static string Refusal (string carrier, string what, string reason) =>
		$"The {carrier} block {reason}, so its copy of {what} cannot be updated.";

	// Compacted tables are sorted and disjoint, so the table a protected range names is one binary search.
	static bool IsCompactedTable (CountedList<(long Start, long End)> compacted, (long Start, long End) range)
	{
		var low = ByteRanges.FirstEndingAfter (compacted, range.Start);
		return low < compacted.Count && compacted[low].Start == range.Start && compacted[low].End == range.End;
	}

	// The sorted ranges of the tables being compacted, or null when one of them shares a byte with another table.
	static CountedList<(long Start, long End)>? CompactedRanges (IfdGraph graph, Dictionary<long, CountedList<bool>> removing)
	{
		var ranges = new CountedList<(long Start, long End, bool Compacted)> ();
		for (var t = 0; t < graph.Tables.Count; t++) {
			var (at, count, _) = graph.Tables[t];
			var (rangeStart, rangeEnd) = TiffReader.TableRange (at, count);
			ranges.Add ((rangeStart, rangeEnd, removing.ContainsKey (at)));
		}

		ranges.Sort ((a, b) => a.Start.CompareTo (b.Start));
		var compacted = new CountedList<(long Start, long End)> ();
		long furthest = long.MinValue, furthestCompacted = long.MinValue;
		for (var n = 0; n < ranges.Count; n++) {
			var range = ranges[n];
			if (range.Start < furthestCompacted || (range.Compacted && range.Start < furthest))
				return null;
			furthest = Math.Max (furthest, range.End);
			if (range.Compacted) {
				furthestCompacted = Math.Max (furthestCompacted, range.End);
				compacted.Add ((range.Start, range.End));
			}
		}

		return compacted;
	}
}
