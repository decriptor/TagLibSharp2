// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ExifTestData;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A field edit runs only on plain Exif. A strip, tile or thumbnail offset or length that is not SHORT or LONG,
/// or an array of those values named more than once, is refused before a plan is built. The block is left as it was.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifPlainTests
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
		Bounds.AssertLinearAllocation (
			tables => Cross ((int)Math.Ceiling (Math.Sqrt (tables)), 3, littleEndian: true),
			tiff => Assert.IsFalse (Edit (tiff).Saved),
			100);

		foreach (var (s, least) in new[] { (10, 8_000), (20, 30_000), (40, 100_000), (80, 600_000) }) {
			var tiff = Cross (s, 17 * s, littleEndian: true);
			Assert.IsTrue (tiff.Length > least, $"s={s}: {tiff.Length} bytes");
			var allocated = Bounds.AssertAllocatesAtMost (Math.Max (8_000_000L, tiff.Length * 40L), () => {
				var edit = Edit (tiff);
				Assert.IsFalse (edit.Saved, edit.Error);
				StringAssert.Contains (edit.Error, "more than once");
			}, $"s={s}");
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
}
