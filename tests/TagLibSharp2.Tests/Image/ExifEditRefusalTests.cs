// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Image.Xmp;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ExifTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>When an Exif edit is refused and when it must save: overlapping tables, pointers into a table, the bounds, and blocks that cannot hold the field.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifEditRefusalTests
{
	// IFD tables that overlap hold more entries than the data could: refused with its own reason before any of
	// them is walked. Exif IFDs (whose interoperability pointers would be read) and GPS IFDs (protected tables).
	// The copy is an XPComment (BYTE) or a UserComment (UNDEFINED, whose tag bytes differ when read the other way), or
	// a UserComment of a type exiftool does not show (0, 19, 255) but TagLib# reads as the comment.
	[TestMethod]
	[DataRow (0x8769, 0x9C9C, 1)]
	[DataRow (0x8825, 0x9C9C, 1)]
	[DataRow (0x8769, 0x9286, 7)]
	[DataRow (0x8769, 0x9286, 0)]
	[DataRow (0x8769, 0x9286, 19)]
	[DataRow (0x8769, 0x9286, 255)]
	public void Edit_IfdsThatOverlap_AreRefusedBeforeTheyAreWalked (int tag, int copy, int type)
	{
		// 400 IFDs two bytes apart, each read as a table of 200 entries: 80,000 entries in 9 KB.
		var entries = Enumerable.Range (0, 400).Select (n => (tag, 4, 1, 4820 + (2 * n))).ToList ();
		entries.Add ((copy, type, 4, 0x41424344));
		var tiff = new TiffBuilder (9100).Header (8).Entries (8, [.. entries]);
		for (var at = 4820; at < 9100 - 2; at += 2)
			tiff.Bytes (at, [0xC8, 0x00]);
		var file = Open (Png (PngChunk ("eXIf", tiff.ToArray ()), Text ("Comment", "c")), "a.png");
		file.Tag!.Comment = "New";

		IfdSteps.Count = 0;
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		StringAssert.Contains (save.Error, "beyond what its data holds", "refused by the entry bound, before any table is compared");
		Assert.IsTrue (IfdSteps.Count <= 9100, $"{IfdSteps.Count} steps before the refusal");
	}

	// The same overlapping tables with no copy of the field: Title's tag 0x9C9B is in no byte of the block, or only
	// as a value (9B 9C 00 00: type 0, which no reader shows as an entry). No table can hold a copy, so the edit
	// saves and the block keeps every byte, in linear steps (the walk to the bound, then one pass over the bytes).
	[TestMethod]
	[DataRow (0x8769, false)]
	[DataRow (0x8825, false)]
	[DataRow (0x8769, true)]
	public void Edit_IfdsThatOverlap_WithNoCopyOfTheField_SaveUnchanged (int tag, bool tagAsAValue)
	{
		var entries = Enumerable.Range (0, 400).Select (n => (tag, 4, 1, 4820 + (2 * n))).ToList ();
		entries.Add ((0x9C9C, 1, 4, 0x41424344));
		if (tagAsAValue)
			entries.Add ((0x9003, 4, 1, 0x9C9B));
		var tiff = new TiffBuilder (9100).Header (8).Entries (8, [.. entries]);
		for (var at = 4820; at < 9100 - 2; at += 2)
			tiff.Bytes (at, [0xC8, 0x00]);
		var exif = tiff.ToArray ();
		var file = Open (Png (PngChunk ("eXIf", exif), Text ("Comment", "c")), "a.png");
		file.Tag!.Title = "New";

		IfdSteps.Count = 0;
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (exif, ExifOf (file.Render ().ToArray ()));
		Assert.IsTrue (IfdSteps.Count <= 3 * 9100, $"{IfdSteps.Count} steps");
	}

	// A block that is not classic TIFF (BigTIFF in either byte order, or an unknown byte order mark) is refused only
	// when a reader of another form could find the field in it: when one of the field's tags is in its bytes, in
	// either byte order. Otherwise no form can hold a copy, and the block is kept byte for byte.
	[TestMethod]
	[DataRow ("png", "BigTIFF MM", "Title", false)]
	[DataRow ("png", "BigTIFF II", "Title", false)]
	[DataRow ("png", "unknown order", "Title", false)]
	[DataRow ("png", "BigTIFF MM", "Comment", false)]
	[DataRow ("jpeg", "BigTIFF II", "Comment", false)]
	[DataRow ("jpeg", "unknown order", "Comment", false)]
	[DataRow ("png", "BigTIFF MM", "Title", true)]
	[DataRow ("png", "BigTIFF II", "Artist", true)]
	[DataRow ("png", "unknown order", "Comment", true)]
	[DataRow ("jpeg", "BigTIFF MM", "Comment", true)]
	[DataRow ("jpeg", "unknown order", "Comment", true)]
	[DataRow ("png", "BigTIFF II", "Comment", true, 1)]
	[DataRow ("png", "BigTIFF MM", "Comment", true, 18)]
	[DataRow ("png", "BigTIFF MM", "Comment", false, 19)]
	[DataRow ("png", "BigTIFF MM", "Comment", false, 0)]
	[DataRow ("png", "BigTIFF II", "Comment", false, 19)]
	[DataRow ("png", "BigTIFF II", "Comment", false, 0)]
	public void Edit_OfABlockThatIsNotClassicTiff_IsRefusedOnlyWhenItMayHoldTheField (string carrier, string form, string field, bool holdsTag, int type = 2)
	{
		// With a type no TIFF or BigTIFF reader knows (0, 19), the field's tag bytes are no entry: nothing to remove.
		var block = new byte[72];
		var bigEndian = form != "BigTIFF II";
		block[0] = block[1] = form switch { "BigTIFF MM" => (byte)'M', "BigTIFF II" => (byte)'I', _ => (byte)'X' };
		block[bigEndian ? 3 : 2] = form == "unknown order" ? (byte)42 : (byte)0x2B;
		block[bigEndian ? 5 : 4] = 8;
		block[bigEndian ? 15 : 8] = 16;
		block[bigEndian ? 23 : 16] = 1; // an 8-byte entry count at 16, then 20-byte entries
		block[bigEndian ? 24 : 25] = 1; // tag 0x0100 ImageWidth
		block[bigEndian ? 27 : 26] = 3;
		if (holdsTag || type != 2) {
			// A second entry with one of the field's tags (Title 0x9C9B, Artist 0x013B, Comment 0x9286).
			var fieldTag = field switch { "Title" => 0x9C9B, "Artist" => 0x013B, _ => 0x9286 };
			block[bigEndian ? 44 : 45] = (byte)(fieldTag >> 8);
			block[bigEndian ? 45 : 44] = (byte)fieldTag;
			block[bigEndian ? 47 : 46] = (byte)type;
			block[bigEndian ? 23 : 16] = 2;
		}

		var input = InCarrier (carrier, block);
		var file = Open (input, carrier == "png" ? "a.png" : "a.jpg");
		typeof (Tag).GetProperty (field)!.SetValue (file.Tag, "New");

		var save = file.SaveToFile ("/a", new MockFileSystem ());

		if (holdsTag) {
			Assert.IsFalse (save.IsSuccess, $"{form}: a reader of that form may hold the field");
			StringAssert.Contains (save.Error, "is not a TIFF structure", form);
			CollectionAssert.AreEqual (input, file.Render ().ToArray (), "a refused save changes nothing");
		} else {
			Assert.IsTrue (save.IsSuccess, $"{form}: {save.Error}");
			CollectionAssert.AreEqual (block, ExifOf (file.Render ().ToArray ()), $"{form}: the block keeps every byte");
		}
	}

	// The scan reaches both ends of a block that is not classic TIFF: a UserComment tag and type as its first or last
	// 4 bytes may be an entry (refused); the tag as the last 2 bytes, with no type after it, is not (saved).
	[TestMethod]
	[DataRow ("first 4 bytes", false)]
	[DataRow ("last 4 bytes", false)]
	[DataRow ("last 2 bytes", true)]
	public void Edit_OfABlockThatIsNotClassicTiff_ReadsTheTagAtEitherEnd (string where, bool saves)
	{
		var block = new byte[24];
		"XX\0*"u8.CopyTo (block);
		byte[] entry = [0x92, 0x86, 0x00, 0x07];
		switch (where) {
			case "first 4 bytes":
				entry.CopyTo (block, 0);
				break;
			case "last 4 bytes":
				entry.CopyTo (block, block.Length - 4);
				break;
			default:
				entry[..2].CopyTo (block, block.Length - 2);
				break;
		}

		var input = InCarrier ("png", block);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.AreEqual (saves, save.IsSuccess, $"{where}: {save.Error}");
		CollectionAssert.AreEqual (saves ? block : input, saves ? ExifOf (file.Render ().ToArray ()) : file.Render ().ToArray (), where);
	}

	// Two Exif IFDs that share bytes (the second starts inside the first, its count read from the first's
	// ExifVersion), as exiftool reads them: compacting one would rewrite the other, so the edit is refused with its
	// own reason and nothing is written, however few entries they hold.
	[TestMethod]
	[DataRow ("png", "the outer table first")]
	[DataRow ("png", "the inner table first")]
	[DataRow ("jpeg", "the outer table first")]
	public void Edit_ExifIfdsThatShareBytes_AreRefused (string carrier, string order)
	{
		const int Ifd0 = 8, A = 40, B = A + 12, Values = 100;
		// A: ExifVersion "02" with 0x0002 in its last two bytes (B's count), UserComment, DateTimeOriginal.
		var tiff = new TiffBuilder (Values + 48).Header (8)
			.Entries (A, (0x9000, 7, 4, 0x00023230), (0x9286, 7, OldOne.Length, Values), (0x9003, 2, 20, Values + 24))
			.Bytes (Values, OldOne).Bytes (Values + 24, "2001:01:01 01:01:01\0"u8);
		tiff.Entries (Ifd0, order == "the outer table first" ? [(0x8769, 4, 1, A), (0x8769, 4, 1, B)] : [(0x8769, 4, 1, B), (0x8769, 4, 1, A)]);
		var data = tiff.ToArray ();
		var input = carrier == "png" ? Png (PngChunk ("eXIf", data), Text ("Comment", "c")) : Jpeg (JpegExif (data), JpegSegment (0xFE, "c"u8.ToArray ()));
		var file = Open (input, carrier == "png" ? "a.png" : "a.jpg");
		file.Tag!.Comment = "New";

		var fs = new MockFileSystem ();
		var save = file.SaveToFile ("/a", fs);

		Assert.IsFalse (save.IsSuccess, $"{carrier} {order}");
		StringAssert.Contains (save.Error, "overlap");
		StringAssert.Contains (save.Error, "its copy of Comment cannot be updated", "the refusal names the field");
		Assert.IsFalse (fs.FileExists ("/a"));
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), "the file is unchanged");
	}

	// A pointer whose table does not fit lands inside a table the edit compacts. That unread range overlaps the
	// compacted table whether or not the bytes moved under it would read as a table, so both shapes are refused
	// and nothing is written.
	[TestMethod]
	[DataRow (0x8769, false)]
	[DataRow (0x8825, false)]
	[DataRow (0x8769, true)]
	[DataRow (0x8825, true)]
	public void Edit_APointerIntoTheMiddleOfAnEditedTable_IsRefused (int tag, bool appears)
	{
		const int Ifd0 = 8, A = 40, Values = 100;
		// The pointer lands on the tag of A's second entry (0x9003): read as a count, 36,867 entries do not fit. The
		// third entry moves there: its tag 0x9004 does not fit either; a tag 0x0001 reads as a table of one entry.
		var tiff = new TiffBuilder (Values + 48).Header (8)
			.Entries (A, (0x9286, 7, OldOne.Length, Values), (0x9003, 2, 20, Values + 24), (appears ? 0x0001 : 0x9004, 2, 4, 0x00383952))
			.Entries (Ifd0, (0x8769, 4, 1, A), (tag, 4, 1, A + 14))
			.Bytes (Values, OldOne).Bytes (Values + 24, "2001:01:01 01:01:01\0"u8).ToArray ();
		var input = Png (PngChunk ("eXIf", tiff), Text ("Comment", "c"));
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var fs = new MockFileSystem ();
		var save = file.SaveToFile ("/a.png", fs);

		Assert.IsFalse (save.IsSuccess, $"{tag:X4} appears {appears}: {save.Error}");
		StringAssert.Contains (save.Error, "into the middle of another IFD table");
		Assert.IsFalse (fs.FileExists ("/a.png"));
		CollectionAssert.AreEqual (input, file.Render ().ToArray ());
	}

	// A refusal is scoped to its reason: only a table the edit rewrites may not share bytes with another table, and
	// an unread pointer's count may not lie in one. Tables that overlap but are not rewritten, or an edit with no
	// copy to remove, save; the GPS, IFD1 and interoperability tables keep their bytes.
	[TestMethod]
	[DataRow ("png", "two Exif IFDs that overlap, with no copy", "Comment")]
	[DataRow ("jpeg", "two Exif IFDs that overlap, with no copy", "Comment")]
	[DataRow ("png", "IFD1 starts inside the GPS table", "Comment")]
	[DataRow ("png", "IFD1 starts inside the GPS table", "Title")]
	[DataRow ("jpeg", "IFD1 starts inside the GPS table", "Comment")]
	[DataRow ("png", "an interoperability pointer into the GPS table's values", "Comment")]
	[DataRow ("jpeg", "an interoperability pointer into the GPS table's values", "Comment")]
	public void Edit_SavesWhenNoTableItRewritesOverlapsAnother (string carrier, string shape, string field)
	{
		const int Ifd0 = 8, E = 60, Gps = 100, Value = 200, Other = 240;
		var comment = (0x9286, 7, SecretUc.Length, Value);
		var date = (0x9003, 2, 20, Other);
		var tiff = new TiffBuilder (Other + 24, bigEndian: false).Header (Ifd0).Bytes (Value, SecretUc).Bytes (Other, "2001:01:01 01:01:01\0"u8);
		(int Start, int End) untouched;
		switch (shape) {
			case "two Exif IFDs that overlap, with no copy":
				// B starts 12 bytes into A: B's count is the last two bytes of A's first entry.
				tiff.Table (Ifd0, [(0x8769, 4, 1, E), (0x8769, 4, 1, E + 12)]).Table (E, [(0x9000, 7, 4, 0x00023230), date, date]);
				untouched = (0, Other + 24);
				break;
			case "IFD1 starts inside the GPS table":
				tiff.Table (Ifd0, [(0x8769, 4, 1, E), (0x8825, 4, 1, Gps)], next: Gps + 12).Table (E, [comment])
					.Table (Gps, [(0x0001, 2, 2, 0x4E), (0x0002, 2, 2, 0x45), (0x0003, 2, 2, 0x4E)]);
				untouched = (Gps, Gps + 2 + 36 + 4);
				break;
			default:
				tiff.Table (Ifd0, [(0x8769, 4, 1, E), (0x8825, 4, 1, Gps)]).Table (E, [comment, (0xA005, 4, 1, Gps + 2 + 24 + 8)])
					.Table (Gps, [(0x0001, 2, 2, 0x4E), (0x0002, 2, 2, 0x45), (0x0003, 7, 4, 0x0000FFFF)]);
				untouched = (Gps, Gps + 2 + 36 + 4);
				break;
		}

		var data = tiff.ToArray ();
		var input = InCarrier (carrier, data);
		var file = Open (input, carrier == "png" ? "a.png" : "a.jpg");
		if (field == "Title")
			file.Tag!.Title = "New";
		else
			file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a", new MockFileSystem ());

		// The interoperability pointer's value sits inside the GPS table. Compaction of the Exif IFD does not rewrite
		// the GPS table, but the pointer range is protected structure the edit would leave inconsistent, so it refuses.
		if (shape == "an interoperability pointer into the GPS table's values") {
			Assert.IsFalse (save.IsSuccess, $"{carrier} {shape}: {save.Error}");
			StringAssert.Contains (save.Error, "cannot be updated");
			CollectionAssert.AreEqual (input, file.Render ().ToArray (), shape);
			return;
		}

		Assert.IsTrue (save.IsSuccess, $"{carrier} {shape} {field}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (field == "Title" ? UserCommentsInEveryExifIfd (data).Count : 0, UserCommentsInEveryExifIfd (exif).Count, shape);
		if (field == "Title")
			AssertOnlyTheCopiesAreGone (data, exif, shape, 0x9C9B);
		else
			AssertOnlyTheCopiesAreGone (data, exif, shape, 0x9286, 0x9C9C);
		CollectionAssert.AreEqual (data[untouched.Start..untouched.End], exif[untouched.Start..untouched.End], $"{shape}: the tables not rewritten keep their bytes");
	}

	// Tables at the edge of a table the edit compacts (A holds the UserComment): A's compaction moves its 4-byte
	// next-IFD offset and clears where it was, so a table B whose count lies there would change (saving would lose B's
	// DateTimeDigitized): refused, nothing written. An unread pointer whose count's second byte is A's first byte is
	// a pointer into the middle of the compacted table, so that edit is refused too.
	[TestMethod]
	[DataRow ("B's count is the last two bytes of A's next-IFD offset", true)]
	[DataRow ("B starts on the last byte of A's next-IFD offset", true)]
	[DataRow ("a pointer whose count's second byte is A's first byte", true)]
	public void Edit_TablesThatTouchAnEditedTableAtItsEdge_AreRefusedWhenTheEditChangesThem (string shape, bool refused)
	{
		const int Ifd0 = 8, A = 40, Value = 140;
		var aEnd = A + 2 + 24 + 4;
		var comment = (0x9286, 7, SecretUc.Length, Value);
		var digitized = (0x9004, 2, 20, Value + 24);
		var tiff = new TiffBuilder (Value + 48, bigEndian: false).Header (Ifd0).Bytes (Value, SecretUc).Bytes (Value + 24, "2001:01:01 01:01:01\0"u8);
		switch (shape) {
			case "B's count is the last two bytes of A's next-IFD offset":
				tiff.Table (A, [comment, (0x9003, 2, 20, Value + 24)], next: 0x00020000).Table (aEnd - 2, [digitized, digitized]);
				tiff.Table (Ifd0, [(0x8769, 4, 1, A), (0x8769, 4, 1, aEnd - 2)]);
				break;
			case "B starts on the last byte of A's next-IFD offset":
				tiff.Table (A, [comment, (0x9003, 2, 20, Value + 24)], next: 0x02000000).Table (aEnd - 1, [digitized, digitized]);
				tiff.U16 (aEnd - 1, 2);
				tiff.Table (Ifd0, [(0x8769, 4, 1, A), (0x8769, 4, 1, aEnd - 1)]);
				break;
			default:
				// Byte 39 is 0xFF: with A's count 2 as the second byte, the pointer reads 767 entries, which do not fit.
				tiff.Table (A, [comment, (0x9003, 2, 20, Value + 24)]).Bytes (A - 1, [0xFF]);
				tiff.Table (Ifd0, [(0x8769, 4, 1, A), (0x8769, 4, 1, A - 1)]);
				break;
		}

		var data = tiff.ToArray ();
		var input = InCarrier ("png", data);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var fs = new MockFileSystem ();
		var save = file.SaveToFile ("/a.png", fs);

		Assert.AreEqual (!refused, save.IsSuccess, $"{shape}: {save.Error}");
		if (!refused) {
			// A's count 1 leaves the pointer at 511 entries, which still do not fit: only the copy changes.
			AssertOnlyTheCopiesAreGone (data, ExifOf (file.Render ().ToArray ()), shape, 0x9286);
			return;
		}

		Assert.IsFalse (fs.FileExists ("/a.png"));
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{shape}: the file is unchanged");
	}

	// Tables that overlap (nothing compacted) and hold exactly as many entries as the bound allows (one per 12
	// bytes of data) are within it: an edit with nothing to remove saves.
	[TestMethod]
	public void Edit_TablesExactlyAtTheEntryBound_Save ()
	{
		// IFD0 (2 entries) at 8; A (2 entries) at 38, on IFD0's next offset; B at 50, inside A, its count 1 taken
		// from A's first entry. 5 entries in 68 bytes: 68 / 12 = 5.
		var tiff = new TiffBuilder (68, bigEndian: false).Header (8)
			.Table (8, [(0x8769, 4, 1, 38), (0x8769, 4, 1, 50)])
			.Table (38, [(0x9003, 7, 4, 0x00010000), (0x9004, 7, 4, 0x31303032)])
			.ToArray ();
		Assert.AreEqual (68 / 12, ExifReaderModel.AnyReader (tiff).Entries, "the tables hold exactly the bound");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (tiff, ExifOf (file.Render ().ToArray ()));
	}

	// The same bound with a copy to remove: IFD0 (3 entries, one an inline UserComment) at 8; A (2 entries) at 50, on
	// IFD0's next offset; B at 62, inside A, its count 1 taken from A's first entry. 6 entries in 80 bytes: 80 / 12 = 6.
	// Within the bound the walk runs and the copy is removed (past it, the block's UserComment tag would refuse it).
	[TestMethod]
	public void Edit_RemovesTheCopy_InTablesExactlyAtTheEntryBound ()
	{
		var tiff = new TiffBuilder (80, bigEndian: false).Header (8)
			.Table (8, [(0x8769, 4, 1, 50), (0x8769, 4, 1, 62), (0x9286, 7, 4, 0x64636261)], next: 50)
			.Table (50, [(0x9003, 7, 4, 0x00010000), (0x9004, 7, 4, 0x31303032)])
			.ToArray ();
		Assert.AreEqual (80 / 12, ExifReaderModel.AnyReader (tiff).Entries, "the tables hold exactly the bound");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		AssertOnlyTheCopiesAreGone (tiff, ExifOf (file.Render ().ToArray ()), "at the bound", 0x9286);
	}

	// Pointer offsets exactly at the bound (one per 4 bytes of data) are within it: the edit saves and removes the
	// copy. The Exif IFD pointer, two SubIFD arrays over the zeroed tail (32 and 30 offsets) and one inline SubIFD
	// offset: 64 in 256 bytes.
	[TestMethod]
	public void Edit_PointerOffsetsExactlyAtTheBound_Save ()
	{
		const int Ifd0 = 8, Exif = 70, Values = 96, Tail = 128;
		var tiff = new TiffBuilder (256, bigEndian: false).Header (Ifd0)
			.Table (Ifd0, [(0x8769, 4, 1, Exif), (0x014A, 4, 32, Tail), (0x014A, 4, 30, Tail + 4), (0x014A, 4, 1, 0)])
			.Table (Exif, [(0x9286, 7, SecretUc.Length, Values)]).Bytes (Values, SecretUc).ToArray ();
		Assert.AreEqual (256 / 4, ExifReaderModel.AnyReader (tiff).PointerValues, "the pointer offsets are exactly the bound");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		AssertOnlyTheCopiesAreGone (tiff, ExifOf (file.Render ().ToArray ()), "at the SubIFD bound", 0x9286);
	}

	// A refusal names every changed field whose copy it could not update.
	[TestMethod]
	public void Edit_RefusalNamesEveryFieldItCouldNotUpdate ()
	{
		const int Ifd0 = 8, A = 40, B = A + 12, Values = 100;
		var tiff = new TiffBuilder (Values + 48, bigEndian: false).Header (Ifd0)
			.Table (A, [(0x9000, 7, 4, 0x00023230), (0x9286, 7, SecretUc.Length, Values), (0x013B, 2, 6, Values + 24)])
			.Table (Ifd0, [(0x8769, 4, 1, A), (0x8769, 4, 1, B)]).Bytes (Values, SecretUc).Bytes (Values + 24, "Jane\0\0"u8).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Artist = "New";
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		StringAssert.Contains (save.Error, "its copy of Artist, Comment cannot be updated");
	}
	// A table some reader reads (B, at 44) lies inside the Exif IFD the edit compacts (A, at 40), its one entry's
	// value offset on A's removed UserComment entry: 0x00079286, past the data, so no reader shows that value. Once A
	// is compacted, A's next offset (200) lands there, and B's value would be the 20 bytes at 200, which every reader
	// would show. The edit is refused, and nothing is written.
	[TestMethod]
	public void Edit_ThatMovesAValueOffsetFromPastTheDataIntoIt_IsRefused ()
	{
		const int A = 40, B = 44;
		var tiff = new TiffBuilder (260).Header (8).Table (8, [(0x8769, 4, 1, A), (0x8769, 4, 1, B)])
			.Table (A, [(0x0100, 1, 0x00029003, 20), (0x9286, 7, 0, 0)], next: 200).ToArray ();
		var model = ExifReaderModel.Of (tiff);
		Assert.AreEqual (1, model.Tables.Single (t => t.At == B).Count, "B holds one entry");
		Assert.AreEqual (0x00079286, model.U32 (B + 2 + 8), "B's value offset is A's UserComment tag and type");
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, "saved");
		CollectionAssert.AreEqual (tiff, ExifOf (file.Render ().ToArray ()), "nothing written");
	}
	// The same shape at the data's end, B's entry typed SSHORT (A's first entry then counts 0x00089003 bytes, past the
	// data). B's 40-byte value moves from past the data (at 0x00079286) to 200, ending at the data's last byte, where
	// every reader shows it; or from ending at the data's last byte (the data is 0x00079286 + 40 bytes) to past it (A's
	// next offset 0x7FFF0000), so a value readers show is gone. Both are refused.
	[TestMethod]
	[DataRow (240, 200)]
	[DataRow (0x00079286 + 40, 0x7FFF0000)]
	public void Edit_ThatMovesAValueOffsetAcrossTheDataEnd_IsRefused (int length, long next)
	{
		const int A = 40, B = 44;
		var tiff = new TiffBuilder (length).Header (8).Table (8, [(0x8769, 4, 1, A), (0x8769, 4, 1, B)])
			.Table (A, [(0x0100, 1, 0x00089003, 20), (0x9286, 7, 0, 0)], next: next).ToArray ();
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, "saved");
		CollectionAssert.AreEqual (tiff, ExifOf (file.Render ().ToArray ()), "nothing written");
	}

	// The reason names the structure the edit would rewrite, whatever else overlaps. IFD0 names the Exif IFD (at 200,
	// count 2) and also the byte before it, where the count reads 512 and the table does not fit; compacting the
	// Exif IFD to one entry makes that count 256, which fits. A GPS table and IFD1 overlap each other elsewhere, but
	// neither is rewritten, so the reason is the pointer into the middle of the Exif IFD, not overlapping tables.
	[TestMethod]
	public void Edit_RefusalReason_IsThePointerIntoTheTableItCompacts_NotOtherOverlaps ()
	{
		const int Gps = 100, Exif = 200, Value = 300;
		var tiff = new TiffBuilder (4000).Header (8)
			.Table (8, [(0x8769, 4, 1, Exif), (0x8769, 4, 1, Exif - 1), (0x8825, 4, 1, Gps)], next: Gps + 2)
			.Table (Gps, [(0x0001, 2, 2, 0x4E)], next: null).U32 (Gps + 14, 0).U16 (Gps + 18, 0)
			.Table (Exif, [(0x9286, 7, SecretUc.Length, Value), (0x9003, 2, 4, 0x30303032)]).Bytes (Value, SecretUc).ToArray ();
		var model = ExifReaderModel.Of (tiff);
		Assert.IsTrue (model.Tables.Any (t => t.At == Gps + 2), "fixture: IFD1 overlaps the GPS table");
		CollectionAssert.Contains (model.Unread, (long)Exif - 1, "fixture: no table fits at 199");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, "the edit would make a table readable at 199");
		StringAssert.Contains (save.Error, "an IFD pointer into the middle of another IFD table");
	}
	// A refusal with no shape that names it gets the general reason: the Exif IFD's GPS pointer holds four SHORT
	// values out of line, inside the Exif IFD on its third entry, whose tag (100) is the GPS table's offset. Removing
	// the UserComment, the first entry, moves the third entry down, and the first value becomes the next-IFD offset
	// (0): the GPS table would no longer be read. (In an Exif IFD, no reader takes the pointer's field as an offset.)
	[TestMethod]
	public void Edit_ThatWouldLoseATableNoNamedShapeExplains_IsRefusedWithTheGeneralReason ()
	{
		const int Exif = 40, Gps = 100, Value = 140;
		var tiff = new TiffBuilder (Value + 40).Header (8).Table (8, [(0x8769, 4, 1, Exif)])
			.Table (Exif, [(0x9286, 7, SecretUc.Length, Value), (0x8825, 3, 4, Exif + 2 + 24), (Gps, 3, 1, 1)])
			.Table (Gps, [(0x0001, 2, 2, 0x4E)]).Bytes (Value, SecretUc).ToArray ();
		Assert.IsTrue (ExifReaderModel.Of (tiff).Tables.Any (t => t.At == Gps), "fixture: the GPS table is read");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		StringAssert.Contains (save.Error, "has IFD structure that removing its copy of Comment would change");
	}
}
