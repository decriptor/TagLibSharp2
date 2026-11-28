// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ExifTestData;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A field edit runs only on plain Exif. A strip, tile or thumbnail offset or length that is not SHORT or LONG,
/// an array of those values that does not lie wholly in the data, an array of those values named more than once,
/// a complete pair that names more values than half the bytes,
/// a next-IFD field with only one, two or three of its four bytes, or a first entry whose readability would
/// change, is refused before a plan is built. The block is left as it was. A wholly absent next field is still
/// edited. A first-entry change that shows nothing new is still refused.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public partial class ExifPlainTests
{
	public static IEnumerable<object[]> PairTypes ()
	{
		(int Offsets, int Lengths)[] pairs = [(0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202)];
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var (offsets, lengths) in pairs)
					foreach (var badType in new[] { 7, 8 })
						foreach (var offsetsBad in new[] { true, false })
							yield return [littleEndian, home, offsets, lengths, badType, offsetsBad];
	}

	[TestMethod]
	[DynamicData (nameof (PairTypes))]
	public void Edit_RefusesAPairTypedOtherThanShortOrLong (bool littleEndian, string home, int offsets, int lengths, int badType, bool offsetsBad)
	{
		var tiff = WithPair (littleEndian, home, offsets, lengths, offsetsBad ? badType : 4, offsetsBad ? 4 : badType);
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{home} {(littleEndian ? "LE" : "BE")} {offsets:X4}/{lengths:X4} type {badType}: saved");
		Assert.IsFalse (changed);
		StringAssert.Contains (error, "cannot be updated");
		StringAssert.Contains (error, "SHORT or LONG");
		CollectionAssert.AreEqual (copy, tiff);
	}

	// The pair lives in `home`. A UserComment in the Exif IFD gives the edit something to remove. Every next-IFD
	// field is present, and the other side of the pair is LONG, so the type is the only thing that is not plain.
	static byte[] WithPair (bool littleEndian, string home, int offsets, int lengths, int offsetsType, int lengthsType)
	{
		const int Ifd0 = 8, Exif = 90, Ifd1 = 180, Sub = 270, Iop = 360, Gps = 450, Comment = 540;
		var comment = SecretUc;
		var ifd0 = new List<(int Tag, int Type, long Count, long Value)> { (0x8769, 4, 1, Exif) };
		var exif = new List<(int Tag, int Type, long Count, long Value)> { (0x9286, 7, comment.Length, Comment) };
		(int At, (int Tag, int Type, long Count, long Value)[] Entries)? extra = null;
		long ifd0Next = 0;
		(int, int, long, long)[] pair = [(offsets, offsetsType, 1, 600), (lengths, lengthsType, 1, 8)];
		switch (home) {
			case "IFD0":
				ifd0.AddRange (pair);
				break;
			case "Exif":
				exif.AddRange (pair);
				break;
			case "IFD1":
				extra = (Ifd1, pair);
				ifd0Next = Ifd1;
				break;
			case "SubIFD":
				extra = (Sub, pair);
				ifd0.Add ((0x014A, 4, 1, Sub));
				break;
			case "Interop":
				extra = (Iop, pair);
				exif.Add ((0xA005, 4, 1, Iop));
				break;
			default:
				extra = (Gps, pair);
				ifd0.Add ((0x8825, 4, 1, Gps));
				break;
		}

		var tiff = new TiffBuilder (Comment + comment.Length, bigEndian: !littleEndian).Header (Ifd0).Bytes (Comment, comment)
			.Table (Ifd0, [.. ifd0], ifd0Next)
			.Table (Exif, [.. exif]);
		if (extra is var (at, entries))
			tiff.Table (at, entries);
		return tiff.ToArray ();
	}

	public static IEnumerable<object[]> CutNextFields ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var present in new[] { 0, 1, 2, 3, 4 })
					yield return [littleEndian, home, present, false];
		// An odd-aligned IFD0 still has 1, 2 or 3 next bytes. exiftool's directory-size rule is about those counts.
		foreach (var littleEndian in new[] { true, false })
			foreach (var present in new[] { 1, 2, 3 })
				yield return [littleEndian, "IFD0", present, true];
	}

	// Zero or four next bytes are a whole field (absent or present). One, two or three bytes are cut short.
	[TestMethod]
	[DynamicData (nameof (CutNextFields))]
	public void Edit_RefusesWhenANextFieldIsCutShort (bool littleEndian, string home, int present, bool odd)
	{
		var tiff = CutNext (littleEndian, home, present, odd);
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };
		var where = $"{home} {(littleEndian ? "LE" : "BE")} {present} byte{(present == 1 ? "" : "s")}{(odd ? " odd" : "")}";

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		if (present is 0 or 4) {
			Assert.IsTrue (saved, $"{where}: {error}");
			Assert.IsTrue (changed, where);
			return;
		}

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "cut short", where);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	// A cut-short next field is not plain, but a field the block does not hold is not removed, so the block is kept.
	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Edit_OfATitle_SavesWhenTheNextFieldIsCutShort (bool littleEndian)
	{
		var tiff = CutNext (littleEndian, "GPS", 1, odd: false);
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, (ImageStores)0x3F);
		tag.ReadField (ImageStores.Title, "Old");
		tag.Title = "New";

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsTrue (saved, error);
		Assert.IsFalse (changed);
		CollectionAssert.AreEqual (copy, tiff);
	}

	public static IEnumerable<object[]> ReadableThenUnreadHomes ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop" })
				yield return [littleEndian, home];
	}

	// The only entry exiftool reads is the UserComment. After it comes a type-0 entry and a next field of 0, so
	// removing the comment drops the table and shows nothing new. That flip is still not plain.
	[TestMethod]
	[DynamicData (nameof (ReadableThenUnreadHomes))]
	public void Edit_RefusesWhenTheOnlyReadableEntryGoesAndLeavesAnUnreadFirstEntry (bool littleEndian, string home)
	{
		var tiff = ReadableThenUnread (littleEndian, home);
		var copy = (byte[])tiff.Clone ();
		var where = $"{home} {(littleEndian ? "LE" : "BE")}";
		Assert.IsFalse (ExifReaderModel.Exiftool (tiff).Strings ().ContainsKey (0x010F), where);
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "first entry", where);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	// UserComment, then a type-0 entry the table would hide once it is first. The next field is present and zero.
	static byte[] ReadableThenUnread (bool littleEndian, string home)
	{
		(int, int, long, long) comment = (0x9286, 7, 4, 0x61626364);
		(int, int, long, long) hidden = (0x010F, 0, 1, 0);
		static int Span (int entries) => 2 + (12 * entries) + 4;
		var ifd0 = new List<(int, int, long, long)> ();
		(int At, (int, int, long, long)[] Entries)? middle = null;
		(int At, (int, int, long, long)[] Entries) edited;
		long ifd0Next = 0;
		switch (home) {
			case "IFD0":
				edited = (8, [comment, hidden]);
				break;
			case "IFD1": {
					var ifd1 = 8 + Span (1);
					ifd0.Add ((0x0100, 4, 1, 1));
					ifd0Next = ifd1;
					edited = (ifd1, [comment, hidden]);
					break;
				}
			case "Exif": {
					var exif = 8 + Span (1);
					ifd0.Add ((0x8769, 4, 1, exif));
					edited = (exif, [comment, hidden]);
					break;
				}
			case "SubIFD": {
					var sub = 8 + Span (1);
					ifd0.Add ((0x014A, 4, 1, sub));
					edited = (sub, [comment, hidden]);
					break;
				}
			default: {
					var exif = 8 + Span (1);
					var iop = exif + Span (1);
					ifd0.Add ((0x8769, 4, 1, exif));
					middle = (exif, [(0xA005, 4, 1, iop)]);
					edited = (iop, [comment, hidden]);
					break;
				}
		}

		var end = edited.At + Span (edited.Entries.Length);
		var tiff = new TiffBuilder (end, bigEndian: !littleEndian).Header (8);
		if (home == "IFD0")
			tiff.Table (8, edited.Entries);
		else
			tiff.Table (8, [.. ifd0], ifd0Next);
		if (middle is var (midAt, midEntries))
			tiff.Table (midAt, midEntries);
		if (home != "IFD0")
			tiff.Table (edited.At, edited.Entries);
		return tiff.ToArray ();
	}

	// The home table is the last bytes of the file. Its entries fit; `present` bytes of its 4-byte next field do.
	static byte[] CutNext (bool littleEndian, string home, int present, bool odd)
	{
		var ifd0 = odd ? 9 : 8;
		(int, int, long, long) desc = (0x010E, 2, 2, 0x4142);
		(int, int, long, long) comment = (0x9286, 7, 4, 0x61626364);
		(int, int, long, long) date = (0x9003, 2, 4, 0x31323334);
		(int, int, long, long) make = (0x010F, 2, 2, 0x4D);
		(int, int, long, long) index = (0x0001, 2, 2, 0x5239);
		static int Span (int entries) => 2 + (12 * entries) + 4;

		var ifd0Entries = new List<(int, int, long, long)> ();
		(int At, (int, int, long, long)[] Entries)? middle = null;
		(int At, (int, int, long, long)[] Entries) cut;
		long ifd0Next = 0;
		switch (home) {
			case "IFD0":
				cut = (ifd0, [desc, comment]);
				break;
			case "Exif": {
					var exif = ifd0 + Span (1);
					ifd0Entries.Add ((0x8769, 4, 1, exif));
					cut = (exif, [date, comment]);
					break;
				}
			case "IFD1": {
					var ifd1 = ifd0 + Span (2);
					ifd0Entries.Add (desc);
					ifd0Entries.Add (comment);
					ifd0Next = ifd1;
					cut = (ifd1, [make]);
					break;
				}
			case "SubIFD": {
					var sub = ifd0 + Span (3);
					ifd0Entries.Add (desc);
					ifd0Entries.Add (comment);
					ifd0Entries.Add ((0x014A, 4, 1, sub));
					cut = (sub, [make]);
					break;
				}
			case "Interop": {
					var exif = ifd0 + Span (1);
					var iop = exif + Span (3);
					ifd0Entries.Add ((0x8769, 4, 1, exif));
					middle = (exif, [date, comment, (0xA005, 4, 1, iop)]);
					cut = (iop, [index]);
					break;
				}
			default: {
					var gps = ifd0 + Span (3);
					ifd0Entries.Add (desc);
					ifd0Entries.Add (comment);
					ifd0Entries.Add ((0x8825, 4, 1, gps));
					cut = (gps, [index]);
					break;
				}
		}

		var full = cut.At + Span (cut.Entries.Length);
		var kept = cut.At + 2 + (12 * cut.Entries.Length) + present;
		var tiff = new TiffBuilder (full, bigEndian: !littleEndian).Header (ifd0);
		if (home == "IFD0")
			tiff.Table (ifd0, cut.Entries);
		else
			tiff.Table (ifd0, [.. ifd0Entries], ifd0Next);
		if (middle is var (midAt, midEntries))
			tiff.Table (midAt, midEntries);
		if (home != "IFD0")
			tiff.Table (cut.At, cut.Entries);
		return tiff.ToArray ()[..kept];
	}

	public static IEnumerable<object[]> SharedArrays ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
					yield return [littleEndian, home, offsets, lengths];
	}

	[TestMethod]
	[DynamicData (nameof (SharedArrays))]
	public void Edit_RefusesWhenTwoTablesNameOneArray (bool littleEndian, string home, int offsets, int lengths)
	{
		var tiff = Shared (littleEndian, home, offsets, lengths);
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{home} {(littleEndian ? "LE" : "BE")} {offsets:X4}: saved");
		Assert.IsFalse (changed);
		StringAssert.Contains (error, "cannot be updated");
		StringAssert.Contains (error, "more than once");
		CollectionAssert.AreEqual (copy, tiff);
	}

	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Edit_RefusesWhenOneArrayIsBothSidesOfAPair (bool littleEndian)
	{
		const int Arr = 80;
		var tiff = new TiffBuilder (220, bigEndian: !littleEndian).Header (8)
			.U16 (Arr, 100).U16 (Arr + 2, 100).U16 (Arr + 4, 100)
			.Table (8, [(0x9286, 7, 4, 0x61626364), (0x0111, 3, 3, Arr), (0x0117, 3, 3, Arr)])
			.ToArray ();
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out _, out var error);

		Assert.IsFalse (saved, error);
		StringAssert.Contains (error, "more than once");
		CollectionAssert.AreEqual (copy, tiff);
	}

	// s = 10, 20, 40, 80 is the reported series (about 11 KB, 41 KB, 164 KB, 653 KB). Recording one range per
	// cross-pair is superlinear and exhausts a 1 GB heap at s = 80. The refusal counts names and stops.
	[TestMethod]
	public void Edit_CrossPairedSeries_IsRefusedInBoundedMemory ()
	{
		var crossRefused = true;
		Bounds.AssertLinearAllocation (
			tables => Cross ((int)Math.Ceiling (Math.Sqrt (tables)), 3, littleEndian: true),
			tiff => crossRefused &= !Edit (tiff).Saved,
			100);
		Assert.IsTrue (crossRefused);

		foreach (var (s, least) in new[] { (10, 8_000), (20, 30_000), (40, 100_000), (80, 600_000) }) {
			var tiff = Cross (s, 17 * s, littleEndian: true);
			Assert.IsTrue (tiff.Length > least, $"s={s}: {tiff.Length} bytes");
			_ = Edit (tiff);
			(bool Saved, string? Error) edit = default;
			var allocated = Bounds.AssertAllocatesAtMost (Math.Max (8_000_000L, tiff.Length * 40L), () => edit = Edit (tiff), $"s={s}");
			Assert.IsFalse (edit.Saved, edit.Error);
			StringAssert.Contains (edit.Error, "more than once");
			Assert.IsTrue (allocated < tiff.Length * 40L, $"s={s} allocated {allocated}");
		}

		var child = KitChild.Run (TimeSpan.FromSeconds (60), "kit-exif-cross", "80");
		Assert.AreEqual (0, child.ExitCode, child.Describe ());
		StringAssert.Contains (child.Output, "more than once");
	}

	/// <summary>The s = 80 cross-pair file, edited under the child's 1 GB heap. Exit 0 when the edit is refused.</summary>
	internal static int CrossInChild (int s)
	{
		var tiff = Cross (s, 17 * s, littleEndian: true);
		var edit = Edit (tiff);
		Console.WriteLine ($"bytes={tiff.Length}");
		Console.WriteLine (edit.Saved ? "saved" : edit.Error);
		return edit.Saved ? 1 : 0;
	}

	static (bool Saved, string? Error) Edit (byte[] tiff)
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };
		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out _, out var error);
		return (saved, error);
	}

	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Edit_RefusesCrossPairedArrays (bool littleEndian)
	{
		var tiff = Cross (2, 3, littleEndian);
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out _, out var error);

		Assert.IsFalse (saved, littleEndian ? "LE saved" : "BE saved");
		StringAssert.Contains (error, "more than once");
		CollectionAssert.AreEqual (copy, tiff);
	}

	// Two tables name one offsets array and different length arrays. The comment gives the edit a copy to remove.
	static byte[] Shared (bool littleEndian, string home, int offsets, int lengths)
	{
		const int Ifd0 = 8, Exif = 120, Other = 200, Arr = 320;
		var tiff = new TiffBuilder (400, bigEndian: !littleEndian).Header (Ifd0)
			.U16 (Arr, 360).U16 (Arr + 2, 360).U16 (Arr + 4, 360)
			.U16 (Arr + 8, 4).U16 (Arr + 10, 4).U16 (Arr + 12, 4)
			.U16 (Arr + 16, 4).U16 (Arr + 18, 4).U16 (Arr + 20, 4);
		var ifd0 = new List<(int, int, long, long)> { (0x9286, 7, 4, 0x61626364), (offsets, 3, 3, Arr), (lengths, 3, 3, Arr + 8) };
		(int, int, long, long)[] second = [(offsets, 3, 3, Arr), (lengths, 3, 3, Arr + 16)];
		switch (home) {
			case "IFD1":
				tiff.Table (Ifd0, [.. ifd0], Other).Table (Other, second);
				break;
			case "Exif":
				ifd0.Add ((0x8769, 4, 1, Exif));
				tiff.Table (Ifd0, [.. ifd0]).Table (Exif, second);
				break;
			case "SubIFD":
				ifd0.Add ((0x014A, 4, 1, Other));
				tiff.Table (Ifd0, [.. ifd0]).Table (Other, second);
				break;
			case "Interop":
				ifd0.Add ((0x8769, 4, 1, Exif));
				tiff.Table (Ifd0, [.. ifd0]).Table (Exif, [(0xA005, 4, 1, Other)]).Table (Other, second);
				break;
			default:
				ifd0.Add ((0x8825, 4, 1, Other));
				tiff.Table (Ifd0, [.. ifd0]).Table (Other, second);
				break;
		}

		return tiff.ToArray ();
	}

	// s offset arrays and s length arrays, and s² SubIFDs. Table (i, j) names offset array i and length array j.
	static byte[] Cross (int s, int k, bool littleEndian)
	{
		var tables = s * s;
		const int Ifd0 = 8;
		var exif = Ifd0 + 2 + 24 + 4;
		var offsets = exif + 2 + 12 + 4;
		var tabs = offsets + (4 * tables);
		var arrayA = tabs + (30 * tables);
		var arrayB = arrayA + (2 * k * s);
		var commentAt = arrayB + (2 * k * s);
		var comment = SecretUc;
		var tiff = new TiffBuilder (commentAt + comment.Length, bigEndian: !littleEndian).Header (Ifd0).Bytes (commentAt, comment);
		for (var n = 0; n < tables; n++)
			tiff.U32 (offsets + (4 * n), tabs + (30 * n));
		for (var n = 0; n < k * s; n++) {
			tiff.U16 (arrayA + (2 * n), 8);
			tiff.U16 (arrayB + (2 * n), 4);
		}

		tiff.Table (Ifd0, [(0x8769, 4, 1, exif), (0x014A, 4, tables, offsets)])
			.Table (exif, [(0x9286, 7, comment.Length, commentAt)]);
		for (var i = 0; i < s; i++)
			for (var j = 0; j < s; j++)
				tiff.Table (tabs + (30 * ((i * s) + j)), [(0x0111, 3, k, arrayA + (2 * k * i)), (0x0117, 3, k, arrayB + (2 * k * j))]);
		return tiff.ToArray ();
	}

	public static IEnumerable<object[]> OverBudgetPairs ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
					yield return [littleEndian, home, offsets, lengths];
	}

	// Two complete pairs name more values than half the bytes. The refusal reads no value array: the same
	// entries a title edit walks, and nothing more.
	[TestMethod]
	[DynamicData (nameof (OverBudgetPairs))]
	public void Edit_RefusesAnOverBudgetPairBeforeReadingItsValues (bool littleEndian, string home, int offsets, int lengths)
	{
		var tiff = OverBudget (littleEndian, home, offsets, lengths);
		var copy = (byte[])tiff.Clone ();
		var where = $"{home} {(littleEndian ? "LE" : "BE")} {offsets:X4}";
		var title = new ImageTag (MediaFormat.Png, ImageStores.Png);
		title.ReadField (ImageStores.Title, "Old");
		title.Title = "New";

		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove ((byte[])tiff.Clone (), 0, title, "eXIf", out _, out var titleError), $"{where}: {titleError}");
		var idle = IfdSteps.Count;

		IfdSteps.Count = 0;
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };
		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "more strip, tile or thumbnail values than its data holds", where);
		CollectionAssert.AreEqual (copy, tiff, where);
		Assert.AreEqual (idle, IfdSteps.Count, $"{where}: a refusal read value arrays ({IfdSteps.Count} steps, a title edit took {idle})");
	}

	// 60 + 59 offset values and one inline length from each table. The arrays overlap in the file and still fit,
	// and their counts sum to more than half the bytes. Both tables are walked.
	static byte[] OverBudget (bool littleEndian, string home, int offsets, int lengths)
	{
		const int Ifd0 = 8;
		var big = new (int, int, long, long)[] { (offsets, 3, 60, 0), (lengths, 3, 1, 1) };
		var small = new (int, int, long, long)[] { (offsets, 3, 59, 0), (lengths, 3, 1, 1) };
		var comment = (0x9286, 7, 4L, 0x61626364L);
		int length, arrayAt;
		TiffBuilder tiff;
		switch (home) {
			case "IFD0":
				length = 200;
				arrayAt = 80;
				tiff = new TiffBuilder (length, bigEndian: !littleEndian).Header (Ifd0);
				big[0].Item4 = arrayAt;
				small[0].Item4 = arrayAt + 2;
				tiff.Table (Ifd0, [comment, big[0], big[1]], 50).Table (50, small);
				break;
			case "IFD1":
				length = 200;
				arrayAt = 80;
				tiff = new TiffBuilder (length, bigEndian: !littleEndian).Header (Ifd0);
				big[0].Item4 = arrayAt;
				small[0].Item4 = arrayAt + 2;
				tiff.Table (Ifd0, [comment, small[0], small[1]], 50).Table (50, big);
				break;
			case "Interop":
				length = 230;
				arrayAt = 110;
				tiff = new TiffBuilder (length, bigEndian: !littleEndian).Header (Ifd0);
				big[0].Item4 = arrayAt;
				small[0].Item4 = arrayAt + 2;
				tiff.Table (Ifd0, [comment, small[0], small[1], (0x8769, 4, 1, 62)])
					.Table (62, [(0xA005, 4, 1, 80)])
					.Table (80, big);
				break;
			default:
				length = 212;
				arrayAt = 92;
				var pointer = home switch { "Exif" => 0x8769, "SubIFD" => 0x014A, _ => 0x8825 };
				tiff = new TiffBuilder (length, bigEndian: !littleEndian).Header (Ifd0);
				big[0].Item4 = arrayAt;
				small[0].Item4 = arrayAt + 2;
				tiff.Table (Ifd0, [comment, small[0], small[1], (pointer, 4, 1, 62)]).Table (62, big);
				break;
		}

		return tiff.ToArray ();
	}

	public static IEnumerable<object[]> LonePairSides ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
				yield return [littleEndian, offsets, lengths];
	}

	// Each table names one side of a pair, and each array fits. A lone offset or length is not summed, so the
	// counts stay under half the bytes and the comment is removed.
	[TestMethod]
	[DynamicData (nameof (LonePairSides))]
	public void Edit_SavesWhenEachTableNamesOnlyOneSideOfAPair (bool littleEndian, int offsets, int lengths)
	{
		var tiff = LoneSides (littleEndian, offsets, lengths);
		var copy = (byte[])tiff.Clone ();
		var where = $"{(littleEndian ? "LE" : "BE")} {offsets:X4}";
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsTrue (saved, $"{where}: {error}");
		Assert.IsTrue (changed, where);
		CollectionAssert.AreNotEqual (copy, tiff, where);
	}

	// IFD0 names the offset side (60 values at 80) and IFD1 names the length side (41 values at 80). 60 + 41 is
	// over half of 200, and neither table has both sides.
	static byte[] LoneSides (bool littleEndian, int offsets, int lengths) =>
		new TiffBuilder (200, bigEndian: !littleEndian).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x61626364), (offsets, 3, 60, 80)], 40)
			.Table (40, [(lengths, 3, 41, 80)])
			.ToArray ();

	public static IEnumerable<object[]> InlineFourBytePairs ()
	{
		foreach (var littleEndian in new[] { true, false }) {
			foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145) })
				yield return [littleEndian, 3, 2, offsets, lengths];
			foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
				yield return [littleEndian, 4, 1, offsets, lengths];
		}
	}

	// Four bytes is still the entry's own value field: a SHORT count of 2, or a LONG count of 1. Two such entries
	// may hold the same word and stay two arrays. The comment is removed.
	[TestMethod]
	[DynamicData (nameof (InlineFourBytePairs))]
	public void Edit_SavesWhenAnInlinePairValueIsExactlyFourBytes (bool littleEndian, int type, int count, int offsets, int lengths)
	{
		var tiff = InlineFourBytes (littleEndian, type, count, offsets, lengths);
		var copy = (byte[])tiff.Clone ();
		var where = $"{(littleEndian ? "LE" : "BE")} type {type} count {count} {offsets:X4}";
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsTrue (saved, $"{where}: {error}");
		Assert.IsTrue (changed, where);
		CollectionAssert.AreNotEqual (copy, tiff, where);
	}

	// Both sides store the word 100. That word is inside each entry. The bytes it names, 100..200, sit past the table.
	static byte[] InlineFourBytes (bool littleEndian, int type, int count, int offsets, int lengths) =>
		new TiffBuilder (220, bigEndian: !littleEndian).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x61626364), (offsets, type, count, 100), (lengths, type, count, 100)])
			.ToArray ();

	public static IEnumerable<object[]> ArraysAtZero ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
				yield return [littleEndian, offsets, lengths];
	}

	// Both sides name the same three SHORT values at offset 0. That array fits, and it is one array, so the edit is
	// refused before a plan is built. A budget refusal is a different reason.
	[TestMethod]
	[DynamicData (nameof (ArraysAtZero))]
	public void Edit_RefusesWhenBothSidesNameTheArrayAtOffsetZero (bool littleEndian, int offsets, int lengths)
	{
		var tiff = ArrayAtZero (littleEndian, offsets, lengths);
		var copy = (byte[])tiff.Clone ();
		var where = $"{(littleEndian ? "LE" : "BE")} {offsets:X4}";
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "one strip, tile or thumbnail array more than once", where);
		Assert.IsFalse (error.Contains ("more strip, tile or thumbnail values than its data holds"), where + ": " + error);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	// Three SHORT values at offset 0. Six bytes do not fit in an entry, the array lies in the block, and both sides name it.
	static byte[] ArrayAtZero (bool littleEndian, int offsets, int lengths) =>
		new TiffBuilder (80, bigEndian: !littleEndian).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x61626364), (offsets, 3, 3, 0), (lengths, 3, 3, 0)])
			.ToArray ();

	public static IEnumerable<object[]> TruncatedPairArrays ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
					foreach (var offsetsBad in new[] { true, false })
						yield return [littleEndian, home, offsets, lengths, offsetsBad];
	}

	// A SHORT array that starts in the block and runs past its end does not fit. The gate refuses that on its own,
	// before the value budget, whichever side and whichever table names it.
	[TestMethod]
	[DynamicData (nameof (TruncatedPairArrays))]
	public void Edit_RefusesWhenAPairArrayDoesNotFit (bool littleEndian, string home, int offsets, int lengths, bool offsetsBad)
	{
		var tiff = TruncatedPair (littleEndian, home, offsets, lengths, offsetsBad);
		var copy = (byte[])tiff.Clone ();
		var where = $"{home} {(littleEndian ? "LE" : "BE")} {(offsetsBad ? offsets : lengths):X4}";
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "does not fit in its data", where);
		Assert.IsFalse (error.Contains ("more strip, tile or thumbnail values than its data holds"), where + ": " + error);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	// Six SHORT values at offset 96 need 12 bytes, and the block ends four bytes later. The other side is one inline value.
	static byte[] TruncatedPair (bool littleEndian, string home, int offsets, int lengths, bool offsetsBad)
	{
		const int Ifd0 = 8;
		var comment = (0x9286, 7, 4L, 0x61626364L);
		(int, int, long, long) side = offsetsBad ? (offsets, 3, 6, 96) : (lengths, 3, 6, 96);
		(int, int, long, long) other = offsetsBad ? (lengths, 3, 1, 1) : (offsets, 3, 1, 1);
		var tiff = new TiffBuilder (100, bigEndian: !littleEndian).Header (Ifd0);
		switch (home) {
			case "IFD0":
				tiff.Table (Ifd0, [comment, side, other]);
				break;
			case "IFD1":
				tiff.Table (Ifd0, [comment], 40).Table (40, [side, other]);
				break;
			case "Exif":
				tiff.Table (Ifd0, [comment, (0x8769, 4, 1, 40)]).Table (40, [side, other]);
				break;
			case "SubIFD":
				tiff.Table (Ifd0, [comment, (0x014A, 4, 1, 40)]).Table (40, [side, other]);
				break;
			case "Interop":
				tiff.Table (Ifd0, [comment, (0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 60)]).Table (60, [side, other]);
				break;
			default:
				tiff.Table (Ifd0, [comment, (0x8825, 4, 1, 40)]).Table (40, [side, other]);
				break;
		}

		return tiff.ToArray ();
	}

	public static IEnumerable<object[]> PairTagsGivenTwice ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var tag in new[] { 0x0111, 0x0117, 0x0144, 0x0145, 0x0201, 0x0202 })
					yield return [littleEndian, home, tag];
	}

	// One table names a strip, tile or thumbnail offset or length twice, as two different inline values.
	// Which bytes the pair names cannot be told, so the edit is refused before a plan is built.
	[TestMethod]
	[DynamicData (nameof (PairTagsGivenTwice))]
	public void Edit_RefusesWhenAPairTagIsGivenTwice (bool littleEndian, string home, int pairTag)
	{
		var tiff = GivenTwice (littleEndian, home, pairTag);
		var copy = (byte[])tiff.Clone ();
		var where = $"{home} {(littleEndian ? "LE" : "BE")} {pairTag:X4}";
		var title = new ImageTag (MediaFormat.Png, ImageStores.Png);
		title.ReadField (ImageStores.Title, "Old");
		title.Title = "New";
		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove ((byte[])tiff.Clone (), 0, title, "eXIf", out _, out var titleError), $"{where}: {titleError}");
		var idle = IfdSteps.Count;

		IfdSteps.Count = 0;
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };
		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "offset or length more than once", where);
		CollectionAssert.AreEqual (copy, tiff, where);
		Assert.AreEqual (idle, IfdSteps.Count, $"{where}: a refusal built a plan ({IfdSteps.Count} steps, a title edit took {idle})");
	}

	// The UserComment value is also a GPS entry's value. Removing the comment would share those bytes, so the
	// structure is not clean and the edit is refused. Both byte orders.
	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Edit_RefusesWhenARemovedValueSharesBytesWithAnotherTable (bool littleEndian)
	{
		const int Ifd0 = 8, Exif = 50, Gps = 90, Value = 120;
		var comment = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
		var tiff = new TiffBuilder (140, bigEndian: !littleEndian).Header (Ifd0)
			.Bytes (Value, comment)
			.Table (Ifd0, [(0x8769, 4, 1, Exif), (0x8825, 4, 1, Gps)])
			.Table (Exif, [(0x9286, 7, comment.Length, Value)])
			.Table (Gps, [(0x001B, 7, comment.Length, Value)])
			.ToArray ();
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, littleEndian ? "LE: saved" : "BE: saved");
		Assert.IsFalse (changed);
		StringAssert.Contains (error, "cannot be updated");
		StringAssert.Contains (error, "IFD structure that removing");
		CollectionAssert.AreEqual (copy, tiff);
	}

	// A comment that sits only in a GPS table, or only in the next IFD of an Exif table (protected, not read with
	// Exif tags), is not a copy the field edit removes. The block is left as it is.
	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Edit_OfAComment_LeavesACommentThatSitsOnlyOutsideAReadTable (bool littleEndian)
	{
		var order = littleEndian ? "LE" : "BE";
		// The comment is the table's first entry. Behind it is a type exiftool does not read, so taking the comment
		// out would hide the table. These tables are not edited, so the comment stays and the block is unchanged.
		var gps = new TiffBuilder (90, bigEndian: !littleEndian).Header (8)
			.Table (8, [(0x010E, 2, 4, 0x41424344), (0x8825, 4, 1, 40)])
			.Table (40, [(0x9286, 7, 4, 0x61626364), (0x010F, 0, 1, 0)])
			.ToArray ();
		var protect = new TiffBuilder (110, bigEndian: !littleEndian).Header (8)
			.Table (8, [(0x8769, 4, 1, 40)])
			.Table (40, [(0x9003, 2, 4, 0x31323334)], next: 70)
			.Table (70, [(0x9286, 7, 4, 0x61626364), (0x010F, 0, 1, 0)])
			.ToArray ();

		foreach (var (name, tiff) in new[] { ("GPS", gps), ("protect-only", protect) })
			AssertLeftAsItIs (tiff, $"{order} {name}");
	}

	static void AssertLeftAsItIs (byte[] tiff, string where)
	{
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsTrue (saved, $"{where}: {error}");
		Assert.IsFalse (changed, where);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	// The next field has one byte, and taking the comment out would hide the table. The cut-short reason is the one
	// reported. The first-entry reason is a later check and does not replace it.
	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Edit_ReportsTheCutShortNextWhenTheFirstEntryWouldAlsoChange (bool littleEndian)
	{
		var tiff = new TiffBuilder (35, bigEndian: !littleEndian).Header (8)
			.Entries (8, (0x9286, 7, 4, 0x61626364), (0x010F, 0, 1, 0))
			.ToArray ();
		var copy = (byte[])tiff.Clone ();
		var where = littleEndian ? "LE" : "BE";
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "cut short", where);
		Assert.IsFalse (error.Contains ("first entry"), where + ": " + error);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	public static IEnumerable<object[]> BudgetAndFirstEntry ()
	{
		foreach (var littleEndian in new[] { true, false })
			foreach (var (offsets, lengths) in new[] { (0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202) })
				yield return [littleEndian, offsets, lengths];
	}

	// The comment is the only entry exiftool reads, and the pair names more values than half the bytes. The
	// first-entry reason is reported. The value budget is used only when no earlier reason was found.
	[TestMethod]
	[DynamicData (nameof (BudgetAndFirstEntry))]
	public void Edit_ReportsTheFirstEntryWhenTheValueBudgetAlsoApplies (bool littleEndian, int offsets, int lengths)
	{
		var tiff = new TiffBuilder (200, bigEndian: !littleEndian).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x61626364), (0x010F, 0, 1, 0), (offsets, 3, 100, 0), (lengths, 3, 1, 1)])
			.ToArray ();
		var copy = (byte[])tiff.Clone ();
		var where = $"{(littleEndian ? "LE" : "BE")} {offsets:X4}";
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{where}: saved");
		Assert.IsFalse (changed, where);
		StringAssert.Contains (error, "cannot be updated", where);
		StringAssert.Contains (error, "first entry", where);
		Assert.IsFalse (error.Contains ("more strip, tile or thumbnail values than its data holds"), where + ": " + error);
		CollectionAssert.AreEqual (copy, tiff, where);
	}

	static byte[] GivenTwice (bool littleEndian, string home, int pairTag)
	{
		const int Ifd0 = 8;
		var comment = (0x9286, 7, 4L, 0x61626364L);
		var first = (pairTag, 4, 1L, 100L);
		var second = (pairTag, 4, 1L, 200L);
		var tiff = new TiffBuilder (120, bigEndian: !littleEndian).Header (Ifd0);
		switch (home) {
			case "IFD0":
				tiff.Table (Ifd0, [comment, first, second]);
				break;
			case "IFD1":
				tiff.Table (Ifd0, [comment], 40).Table (40, [first, second]);
				break;
			case "Exif":
				tiff.Table (Ifd0, [comment, (0x8769, 4, 1, 40)]).Table (40, [first, second]);
				break;
			case "SubIFD":
				tiff.Table (Ifd0, [comment, (0x014A, 4, 1, 40)]).Table (40, [first, second]);
				break;
			case "Interop":
				tiff.Table (Ifd0, [comment, (0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 70)]).Table (70, [first, second]);
				break;
			default:
				tiff.Table (Ifd0, [comment, (0x8825, 4, 1, 40)]).Table (40, [first, second]);
				break;
		}

		return tiff.ToArray ();
	}
}
