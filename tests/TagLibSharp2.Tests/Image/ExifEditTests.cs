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

/// <summary>Removing a changed field from Exif: every table a reader reads loses its copy, and no byte other structure uses is changed.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifEditTests
{
	public static IEnumerable<object[]> OverlappingExif ()
	{
		// Make and Artist share value bytes; Artist points into IFD0's table; Artist points at the header.
		yield return ["shared value", (Action<byte[], int, int>)((tiff, artist, make) => Array.Copy (tiff, make + 4, tiff, artist + 4, 8))];
		yield return ["into the IFD table", (Action<byte[], int, int>)((tiff, artist, _) => { BitConverter.GetBytes (20).CopyTo (tiff, artist + 4); BitConverter.GetBytes (8).CopyTo (tiff, artist + 8); })];
		yield return ["into the header", (Action<byte[], int, int>)((tiff, artist, _) => { BitConverter.GetBytes (8).CopyTo (tiff, artist + 4); BitConverter.GetBytes (0).CopyTo (tiff, artist + 8); })];
	}

	// A removed value is zeroed only where its bytes belong to it alone (rule 6: the other values keep theirs).
	[TestMethod]
	[DynamicData (nameof (OverlappingExif))]
	public void Edit_RemovedExifValue_NeverZeroesBytesItShares (string shape, Action<byte[], int, int> corrupt)
	{
		var tiff = ExifTiff ([(0x010F, "CanonMake"), (0x0110, "ModelName"), (0x013B, "OldArtist")], []);
		corrupt (tiff, 8 + 2 + (2 * 12), 8 + 2);
		var file = Open (Png (PngChunk ("eXIf", tiff), Text ("Author", "a")), "a.png");
		file.Tag!.Artist = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = PngChunks (file.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload;
		CollectionAssert.AreEqual (tiff[..8], exif[..8], $"{shape}: header");
		var values = ExifReaderModel.Of (exif).Strings ();
		Assert.AreEqual ("CanonMake", values[0x010F], shape);
		Assert.AreEqual ("ModelName", values[0x0110], shape);
		Assert.IsFalse (values.ContainsKey (0x013B), shape);
	}

	// IFD0 (Make, Artist, GPS pointer) -> IFD1, and a GPS IFD; Artist's value is also named by IFD1 or GPS.
	static byte[] SharedArtistTiff (string shape)
	{
		var data = new byte[256];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		const int Ifd0 = 8, Ifd1 = 8 + 2 + (3 * 12) + 4, Gps = Ifd1 + 2 + (2 * 12) + 4, Make = Gps + 2 + 12 + 4, Artist = Make + 10;
		BitConverter.GetBytes (Ifd0).CopyTo (data, 4);
		void Entry (int at, int tag, int type, int count, int value)
		{
			BitConverter.GetBytes ((ushort)tag).CopyTo (data, at);
			BitConverter.GetBytes ((ushort)type).CopyTo (data, at + 2);
			BitConverter.GetBytes (count).CopyTo (data, at + 4);
			BitConverter.GetBytes (value).CopyTo (data, at + 8);
		}

		BitConverter.GetBytes ((ushort)3).CopyTo (data, Ifd0);
		Entry (Ifd0 + 2, 0x010F, 2, 10, Make);
		Entry (Ifd0 + 14, 0x013B, 2, 10, Artist);
		Entry (Ifd0 + 26, 0x8825, 4, 1, Gps);
		BitConverter.GetBytes (Ifd1).CopyTo (data, Ifd0 + 38);
		BitConverter.GetBytes ((ushort)2).CopyTo (data, Ifd1);
		if (shape == "thumbnail") {
			Entry (Ifd1 + 2, 0x0201, 4, 1, Artist);
			Entry (Ifd1 + 14, 0x0202, 4, 1, 10);
		} else {
			Entry (Ifd1 + 2, 0x0131, 2, 10, shape == "IFD1 value" ? Artist : Make);
			Entry (Ifd1 + 14, 0x0132, 2, 10, Make);
		}

		BitConverter.GetBytes ((ushort)1).CopyTo (data, Gps);
		Entry (Gps + 2, 0x0012, 2, 10, shape == "GPS value" ? Artist : Make);
		"CanonMake\0"u8.CopyTo (data.AsSpan (Make));
		"OldArtist\0"u8.CopyTo (data.AsSpan (Artist));
		return data[..(Artist + 10)];
	}

	[TestMethod]
	[DataRow ("IFD1 value")]
	[DataRow ("thumbnail")]
	[DataRow ("GPS value")]
	public void Edit_RemovedExifValue_KeepsBytesTheThumbnailIfd1OrGpsUse (string shape)
	{
		var tiff = SharedArtistTiff (shape);
		var file = Open (Png (PngChunk ("eXIf", tiff), Text ("Author", "a")), "a.png");
		file.Tag!.Artist = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = PngChunks (file.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload;
		Assert.IsTrue (exif.AsSpan ().IndexOf ("OldArtist\0"u8) > 0, $"{shape}: the shared bytes are kept");
		Assert.IsFalse (ExifReaderModel.Of (exif).Strings ().ContainsKey (0x013B), $"{shape}: Artist is removed");
		Assert.AreEqual ("CanonMake", ExifReaderModel.Of (exif).Strings ()[0x010F]);
	}

	static readonly byte[] FilledApp1 = [0xFF, 0xFF, 0xFF, 0xFF, 0xE1];

	[TestMethod]
	public void Jpeg_RewrittenExifSegment_KeepsTheFillBytesBeforeIt ()
	{
		var exif = JpegExif (ExifTiff ([(0x010F, "Canon"), (0x9C9C, "Old XP comment")], []));
		var input = Jpeg (Concat ([0xFF, 0xFF, 0xFF], exif), JpegSegment (0xFE, "Old"u8.ToArray ()));
		var file = Open (input, "a.jpg");
		file.Tag!.Comment = "New";
		var output = file.Render ().ToArray ();
		var at = output.AsSpan ().IndexOf ("Exif\0\0"u8) - 4;
		Assert.IsTrue (output.AsSpan (at - 3, 5).SequenceEqual (FilledApp1), "the three fill bytes stay before the rewritten segment");
		Assert.IsTrue (output.AsSpan ().IndexOf ("Old XP comment"u8) < 0);
	}

	// ImageIO merges every Exif IFD IFD0 points at and exiftool lists each, so a Comment edit removes UserComment
	// from all of them. The GPS, interoperability and thumbnail data of every IFD is protected from zeroing, and
	// when an offset/length pair is given twice nothing is zeroed (which bytes it names cannot be told).
	[TestMethod]
	[DataRow ("two Exif IFDs")]
	[DataRow ("two Exif IFDs, the other way round")]
	[DataRow ("the same Exif IFD twice")]
	[DataRow ("the Exif IFD is IFD0")]
	[DataRow ("a second GPS IFD shares the old comment's bytes")]
	[DataRow ("a second Exif IFD's interoperability IFD shares them")]
	[DataRow ("StripOffsets given twice")]
	[DataRow ("an empty Exif IFD pointer before the real one")]
	[DataRow ("StripOffsets given twice in IFD0 and in IFD1")]
	[DataRow ("a kept RATIONAL value shares the old comment's bytes")]
	public void Edit_RemovesTheCommentFromEveryExifIfd (string shape)
	{
		const int Ifd0 = 8, ExifA = 80, ExifB = 120, Gps = 160, Interop = 200, One = 240, Two = 264;
		var tiff = new TiffBuilder (300).Header (8).Bytes (One, OldOne).Bytes (Two, OldTwo);
		(int, int, int, int) Comment (int at, byte[] value) => (0x9286, 7, value.Length, at);
		var shared = false;
		switch (shape) {
			case "two Exif IFDs":
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x8769, 4, 1, ExifB)).Entries (ExifA, Comment (One, OldOne)).Entries (ExifB, Comment (Two, OldTwo));
				break;
			case "two Exif IFDs, the other way round":
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifB), (0x8769, 4, 1, ExifA)).Entries (ExifA, Comment (One, OldOne)).Entries (ExifB, Comment (Two, OldTwo));
				break;
			case "the same Exif IFD twice":
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x8769, 4, 1, ExifA)).Entries (ExifA, Comment (One, OldOne), (0x9003, 7, 4, 0x31303032), Comment (Two, OldTwo), (0x9004, 7, 4, 0x31303032));
				break;
			case "the Exif IFD is IFD0":
				tiff.Entries (Ifd0, (0x8769, 4, 1, Ifd0), Comment (One, OldOne), Comment (Two, OldTwo));
				break;
			case "a second GPS IFD shares the old comment's bytes":
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x8825, 4, 1, Gps), (0x8825, 4, 1, Gps + 20)).Entries (ExifA, Comment (One, OldOne), Comment (Two, OldTwo))
					.Entries (Gps, (0x0001, 2, 2, 0x4E)).Entries (Gps + 20, (0x001B, 7, OldTwo.Length, Two));
				shared = true;
				break;
			case "a second Exif IFD's interoperability IFD shares them":
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x8769, 4, 1, ExifB)).Entries (ExifA, Comment (One, OldOne))
					.Entries (ExifB, Comment (Two, OldTwo), (0xA005, 4, 1, Interop)).Entries (Interop, (0x0002, 7, OldTwo.Length, Two));
				shared = true;
				break;
			case "StripOffsets given twice in IFD0 and in IFD1":
				// Each table alone would stop the zeroing; two of them must too.
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x0111, 4, 1, Gps), (0x0111, 4, 1, Two)).Entries (ExifA, Comment (One, OldOne), Comment (Two, OldTwo));
				tiff.Bytes (Ifd0 + 2 + 36, BitConverter.GetBytes (ExifB)).Entries (ExifB, (0x0111, 4, 1, Gps), (0x0111, 4, 1, Two));
				shared = true;
				break;
			case "a kept RATIONAL value shares the old comment's bytes":
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x8825, 4, 1, Gps)).Entries (ExifA, Comment (One, OldOne), Comment (Two, OldTwo))
					.Entries (Gps, (0x0002, 5, 1, Two + 8));
				shared = true;
				break;
			case "an empty Exif IFD pointer before the real one":
				tiff.Entries (Ifd0, (0x8769, 4, 1, 0), (0x8769, 4, 1, ExifA)).Entries (ExifA, Comment (One, OldOne), Comment (Two, OldTwo));
				break;
			default:
				tiff.Entries (Ifd0, (0x8769, 4, 1, ExifA), (0x0111, 4, 1, Gps), (0x0117, 4, 1, 4), (0x0111, 4, 1, Two), (0x0117, 4, 1, 4)).Entries (ExifA, Comment (One, OldOne), Comment (Two, OldTwo));
				shared = true;
				break;
		}

		var file = Open (Png (PngChunk ("eXIf", tiff.ToArray ()), Text ("Comment", "c")), "a.png");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");

		var exif = PngChunks (file.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload;
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, $"{shape}: a UserComment is left");
		AssertOnlyTheCopiesAreGone (tiff.ToArray (), exif, shape, 0x9286, 0x9C9C);
		if (shape == "two Exif IFDs") {
			// The compacted table's freed bytes are cleared: its one entry is gone, its next offset moves up, and the
			// 12 bytes after it hold nothing of the old entry.
			Assert.AreEqual (0, BitConverter.ToUInt16 (exif, ExifA));
			CollectionAssert.AreEqual (new byte[16], exif[(ExifA + 2)..(ExifA + 18)], "the freed bytes are zero");
		}

		if (shape == "the same Exif IFD twice") {
			// The table is compacted once: the entries kept are the two dates, in order.
			Assert.AreEqual (2, BitConverter.ToUInt16 (exif, ExifA));
			Assert.AreEqual (0x9003, BitConverter.ToUInt16 (exif, ExifA + 2));
			Assert.AreEqual (0x9004, BitConverter.ToUInt16 (exif, ExifA + 14));
		}

		var zeroesNothing = shape.StartsWith ("StripOffsets given twice", StringComparison.Ordinal);
		Assert.AreEqual (zeroesNothing, exif.AsSpan ().IndexOf ("old comment one"u8) >= 0, $"{shape}: the first comment's bytes are zeroed unless nothing can be");
		Assert.AreEqual (shared || zeroesNothing, exif.AsSpan ().IndexOf ("old comment two"u8) >= 0, $"{shape}: the second comment's bytes are kept only when another IFD uses them");
	}

	// A pointer no reader follows (its table is not inside the data) can hold no copy: the edit skips it, as Clear,
	// ImageIO and exiftool do, and updates the readable tables. One row per pointer kind the edit follows.
	[TestMethod]
	[DataRow ("an Exif IFD pointer past the data", 0x8769)]
	[DataRow ("an Exif IFD pointer whose table runs past the data", 0x8769)]
	[DataRow ("a GPS IFD pointer past the data", 0x8825)]
	[DataRow ("an interoperability IFD pointer past the data", 0xA005)]
	[DataRow ("an IFD1 offset past the data", 0)]
	[DataRow ("an IFD0 offset past the data", -1)]
	public void Edit_SkipsAPointerNoReaderFollows (string shape, int tag)
	{
		const int Ifd0 = 8, Exif = 64, One = 120;
		var bad = shape.Contains ("runs past", StringComparison.Ordinal) ? 150 : 0x7FFF_0000;
		var tiff = new TiffBuilder (160).Header (8).Bytes (One, OldOne).Bytes (150, [0xFF, 0x00]);
		var ifd0 = new List<(int, int, int, int)> { (0x8769, 4, 1, Exif) };
		if (tag is 0x8769 or 0x8825)
			ifd0.Add ((tag, 4, 1, bad));
		tiff.Entries (Ifd0, [.. ifd0]);
		var exif = new List<(int, int, int, int)> { (0x9286, 7, OldOne.Length, One) };
		if (tag == 0xA005)
			exif.Add ((0xA005, 4, 1, bad));
		tiff.Entries (Exif, [.. exif]);
		var data = tiff.ToArray ();
		if (tag == 0)
			BitConverter.GetBytes (bad).CopyTo (data, Ifd0 + 2 + (12 * ifd0.Count));
		if (tag == -1) {
			// No reader reads anything of this block (exiftool: "Bad IFD0 directory"), so the edit saves it as it is.
			BitConverter.GetBytes (bad).CopyTo (data, 4);
			var unread = Open (Png (PngChunk ("eXIf", data), Text ("Comment", "c")), "a.png");
			unread.Tag!.Comment = "New";
			Assert.IsTrue (unread.SaveToFile ("/a.png", new MockFileSystem ()).IsSuccess, shape);
			CollectionAssert.AreEqual (data, PngChunks (unread.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload, shape);
			return;
		}

		var file = Open (Png (PngChunk ("eXIf", data), Text ("Comment", "c")), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var exifOut = PngChunks (file.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload;
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exifOut).Count, shape);
		AssertOnlyTheCopiesAreGone (data, exifOut, shape, 0x9286, 0x9C9C);
		Assert.IsTrue (exifOut.AsSpan ().IndexOf ("old comment one"u8) < 0, shape);
	}

	static readonly byte[] OldXp = Encoding.Unicode.GetBytes ("old xp comment\0");

	// TIFF 6.0 §2 puts a 4-byte next-IFD offset after every table, but readers do not need it: exiftool, ImageIO and
	// TagLib# read a table whose entries fit even when the offset is missing or cut short.
	// So a Comment edit removes the copies from such a table, and protects a GPS, interoperability or IFD1 table
	// of that shape from zeroing. The table under test is the last thing in the data, with 0 or 2 of its 4 bytes.
	[TestMethod]
	[DataRow ("IFD0", 4)]
	[DataRow ("IFD0", 2)]
	[DataRow ("Exif IFD", 4)]
	[DataRow ("Exif IFD", 2)]
	[DataRow ("GPS IFD", 4)]
	[DataRow ("GPS IFD", 2)]
	[DataRow ("interoperability IFD", 4)]
	[DataRow ("interoperability IFD", 2)]
	[DataRow ("IFD1", 4)]
	[DataRow ("IFD1", 2)]
	public void Edit_ReadsATableWhoseNextOffsetIsMissing (string table, int cut)
	{
		// Values first, then the tables; the table under test goes last.
		const int One = 8, Xp = One + 24, Tables = 64;
		var exifComment = (0x9286, 7, OldOne.Length, One);
		var xpComment = (0x9C9C, 1, OldXp.Length, Xp);
		var shared = (0x0002, 7, OldOne.Length, One);
		var data = new List<(int At, (int, int, int, int)[] Entries, int Next)> ();
		int At (int index) => Tables + (index * 60);
		switch (table) {
			case "IFD0":
				data.Add ((At (0), [exifComment], 0));
				data.Add ((At (1), [xpComment, (0x8769, 4, 1, At (0))], 0));
				break;
			case "Exif IFD":
				data.Add ((At (0), [xpComment, (0x8769, 4, 1, At (1))], 0));
				data.Add ((At (1), [exifComment], 0));
				break;
			case "GPS IFD":
				data.Add ((At (0), [xpComment, (0x8769, 4, 1, At (1)), (0x8825, 4, 1, At (2))], 0));
				data.Add ((At (1), [exifComment], 0));
				data.Add ((At (2), [shared], 0));
				break;
			case "interoperability IFD":
				data.Add ((At (0), [xpComment, (0x8769, 4, 1, At (1))], 0));
				data.Add ((At (1), [exifComment, (0xA005, 4, 1, At (2))], 0));
				data.Add ((At (2), [shared], 0));
				break;
			default:
				data.Add ((At (0), [xpComment, (0x8769, 4, 1, At (1))], At (2)));
				data.Add ((At (1), [exifComment], 0));
				data.Add ((At (2), [shared], 0));
				break;
		}

		var last = data[^1];
		var length = last.At + 2 + (12 * last.Entries.Length) + 4 - cut;
		var tiff = new TiffBuilder (length + cut).Header (8).Bytes (One, OldOne).Bytes (Xp, OldXp);
		foreach (var (at, entries, next) in data) {
			tiff.Entries (at, [.. entries]);
			tiff.Bytes (at + 2 + (12 * entries.Length), BitConverter.GetBytes (next));
		}

		var bytes = tiff.ToArray ()[..length];
		var ifd0 = table == "IFD0" ? At (1) : At (0);
		BitConverter.GetBytes (ifd0).CopyTo (bytes, 4);
		var file = Open (Png (PngChunk ("eXIf", bytes), Text ("Comment", "c")), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{table}: {save.Error}");
		var exif = PngChunks (file.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload;
		Assert.AreEqual (length, exif.Length, $"{table}: nothing is written past the data");
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, $"{table}: a comment copy is left");
		AssertOnlyTheCopiesAreGone (bytes, exif, table, 0x9286, 0x9C9C);
		var protectedTable = table is "GPS IFD" or "interoperability IFD" or "IFD1";
		Assert.AreEqual (protectedTable, exif.AsSpan ().IndexOf ("old comment one"u8) >= 0, $"{table}: the old comment's bytes are kept only when that table uses them");
	}

	// Zeroing a removed value must not change a byte any reader could parse as IFD structure. Here the removed
	// UserComment's last byte (0x01) is the first count byte of a table P (big-endian 0x0102: 258 entries, which do
	// not fit, so no reader reads it); zeroing it would make the count 2 and P readable, and every reader would show
	// P's "hidden-uc" (measured with exiftool, ImageIO and TagLib#). P's count bytes are kept instead, whichever way the
	// walk reaches P: an IFD0 0x8769, a nested Exif IFD's 0x8769, or IFD0's 0x014A SubIFD.
	[TestMethod]
	[DataRow ("png", "IFD0's Exif IFD pointer")]
	[DataRow ("jpeg", "IFD0's Exif IFD pointer")]
	[DataRow ("png", "a nested Exif IFD pointer")]
	[DataRow ("jpeg", "a nested Exif IFD pointer")]
	[DataRow ("png", "a SubIFD pointer")]
	[DataRow ("jpeg", "a SubIFD pointer")]
	public void Edit_NeverZeroesTheCountOfATableNoReaderReads (string carrier, string shape)
	{
		const int Ifd0 = 8, A = 48, Value = 100;
		var p = Value + SecretUc.Length - 1;
		var hidden = p + 2 + (12 * 2) + 4;
		var tiff = new TiffBuilder (hidden + HiddenUc.Length, bigEndian: true).Header (Ifd0)
			.Bytes (Value, SecretUc)
			.U16 (p, 0x0102)
			.Bytes (p + 2, new byte[24])
			.Bytes (hidden, HiddenUc);
		// P's entries, as they would be read if its count were 2.
		tiff.U16 (p + 2, 0x9286).U16 (p + 4, 7).U32 (p + 6, HiddenUc.Length).U32 (p + 10, hidden);
		var comment = (0x9286, 7, SecretUc.Length, Value);
		_ = shape switch {
			"IFD0's Exif IFD pointer" => tiff.Table (Ifd0, [(0x8769, 4, 1, A), (0x8769, 4, 1, p)]).Table (A, [comment]),
			"a nested Exif IFD pointer" => tiff.Table (Ifd0, [(0x8769, 4, 1, A)]).Table (A, [comment, (0x8769, 4, 1, p)]),
			_ => tiff.Table (Ifd0, [(0x8769, 4, 1, A), (0x014A, 4, 1, p)]).Table (A, [comment]),
		};
		var data = tiff.ToArray ();
		Assert.AreEqual (1, UserCommentsInEveryExifIfd (data).Count, "the input shows its UserComment and not P's");
		var file = Open (InCarrier (carrier, data), carrier == "png" ? "a.png" : "a.jpg");
		file.Tag!.Comment = "New";

		Assert.IsTrue (file.SaveToFile ("/a", new MockFileSystem ()).IsSuccess, shape);
		var exif = ExifOf (file.Render ().ToArray ());

		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, $"{carrier} {shape}: no UserComment is shown");
		AssertOnlyTheCopiesAreGone (data, exif, $"{carrier} {shape}", 0x9286, 0x9C9C);
		Assert.AreEqual (0x01, exif[p], $"{carrier} {shape}: P's count is unchanged");
		Assert.AreEqual (0x02, exif[p + 1]);
	}

	// exiftool reads every table of IFD0's chain (IFD1, IFD2, ...), and from each, from every Exif IFD and every
	// SubIFD it follows 0x8769 and 0x014A, all with the Exif::Main tags (measured: it shows [IFD0] UserComment,
	// [IFD1] Artist, [ExifIFD] XPComment and Artist, [SubIFD] UserComment, XPComment, ImageDescription, Copyright,
	// [IFD2] UserComment, and the nested Exif IFD's UserComment). An edit removes the field's copies from each.
	[TestMethod]
	[DataRow ("png", "nested Exif IFD and the first", 0x9286)]
	[DataRow ("jpeg", "nested Exif IFD and the first", 0x9286)]
	[DataRow ("png", "nested Exif IFD only", 0x9286)]
	[DataRow ("jpeg", "nested Exif IFD only", 0x9286)]
	[DataRow ("png", "IFD1's Exif IFD", 0x9286)]
	[DataRow ("jpeg", "IFD1's Exif IFD", 0x9286)]
	[DataRow ("png", "SubIFD", 0x9286)]
	[DataRow ("jpeg", "SubIFD", 0x9286)]
	[DataRow ("png", "IFD2", 0x9286)]
	[DataRow ("png", "IFD0", 0x9286)]
	[DataRow ("png", "SubIFD", 0x9C9C)]
	[DataRow ("png", "nested Exif IFD only", 0x013B)]
	[DataRow ("png", "IFD1's Exif IFD", 0x8298)]
	public void Edit_RemovesTheCopiesExiftoolReadsInNestedTables (string carrier, string where, int copy)
	{
		const int Ifd0 = 8, E = 60, N = 100, Ifd1 = 140, Ifd2 = 180, Value = 240;
		var copyBytes = copy == 0x9C9C ? Encoding.Unicode.GetBytes ("old copy\0") : copy == 0x9286 ? "ASCII\0\0\0old copy"u8.ToArray () : "old copy\0"u8.ToArray ();
		var entry = (copy, copy switch { 0x9C9C => 1, 0x9286 => 7, _ => 2 }, copyBytes.Length, Value);
		var filler = (0x9003, 7, 4, 0x31303032);
		var tiff = new TiffBuilder (Value + 32, bigEndian: carrier == "jpeg").Header (Ifd0).Bytes (Value, copyBytes);
		_ = where switch {
			"nested Exif IFD and the first" => tiff.Table (Ifd0, [(0x8769, 4, 1, E)]).Table (E, [entry, (0x8769, 4, 1, N)]).Table (N, [(copy, entry.Item2, copyBytes.Length, Value)]),
			"nested Exif IFD only" => tiff.Table (Ifd0, [(0x8769, 4, 1, E)]).Table (E, [filler, (0x8769, 4, 1, N)]).Table (N, [entry]),
			"IFD1's Exif IFD" => tiff.Table (Ifd0, [filler], next: Ifd1).Table (Ifd1, [(0x8769, 4, 1, N)]).Table (N, [entry]),
			"SubIFD" => tiff.Table (Ifd0, [(0x014A, 4, 1, N)]).Table (N, [entry]),
			"IFD2" => tiff.Table (Ifd0, [filler], next: Ifd1).Table (Ifd1, [filler], next: Ifd2).Table (Ifd2, [entry]),
			_ => tiff.Table (Ifd0, [entry, filler]),
		};
		var data = tiff.ToArray ();
		Assert.IsTrue (ExifReaderModel.Of (data).CopiesOf (copy).Count > 0, "the input shows the copy");
		var file = Open (InCarrier (carrier, data), carrier == "png" ? "a.png" : "a.jpg");
		switch (copy) {
			case 0x013B:
				file.Tag!.Artist = "New";
				break;
			case 0x8298:
				file.Tag!.Copyright = "New";
				break;
			default:
				file.Tag!.Comment = "New";
				break;
		}

		var save = file.SaveToFile ("/a", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{carrier} {where}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (0, ExifReaderModel.Of (exif).CopiesOf (copy).Count, $"{carrier} {where}: the copy is left");
		int[] removed = copy switch { 0x013B => [0x013B, 0x9C9D], 0x8298 => [0x8298], _ => [0x9286, 0x9C9C] };
		AssertOnlyTheCopiesAreGone (data, exif, $"{carrier} {where}", removed);
	}

	// An array of SubIFD offsets is IFD structure as well: inside a table the edit compacts it would be rewritten
	// (refused with its own reason, nothing written), and inside a removed value it is not zeroed (the SubIFDs
	// readers reach stay the same). A differential fuzz found both shapes.
	[TestMethod]
	[DataRow ("inside the table the edit compacts")]
	[DataRow ("inside the removed UserComment")]
	[DataRow ("ending inside the table the edit compacts")]
	public void Edit_KeepsSubIfdOffsetArraysAsTheyAre (string shape)
	{
		const int Ifd0 = 8, S1 = 80, S2 = 100, Value = 140;
		var ucAt = Value;
		var tiff = new TiffBuilder (Value + 48, bigEndian: false).Header (Ifd0)
			.Table (S1, [(0x9003, 7, 4, 0x31303032)]).Table (S2, [(0x9004, 7, 4, 0x31303032)]);
		int arrayAt;
		if (shape == "inside the table the edit compacts") {
			// The array is the count and value fields of IFD0's second entry, which moves up when the first goes.
			arrayAt = Ifd0 + 2 + 12 + 4;
			tiff.Table (Ifd0, [(0x9286, 7, SecretUc.Length, ucAt), (0x9003, 7, S1, S2), (0x014A, 4, 2, arrayAt)]);
		} else if (shape == "ending inside the table the edit compacts") {
			// The array starts at 4 (IFD0's own offset, 8) and its second offset is IFD0's count and first tag.
			arrayAt = 4;
			tiff.Table (Ifd0, [(0x9286, 7, SecretUc.Length, ucAt), (0x014A, 4, 2, arrayAt)]);
		} else {
			arrayAt = Value + 8;
			tiff.Table (Ifd0, [(0x9286, 7, 24, ucAt), (0x014A, 4, 2, arrayAt)]).Bytes (ucAt, "ASCII\0\0\0"u8).U32 (arrayAt, S1).U32 (arrayAt + 4, S2);
		}

		var data = tiff.ToArray ();
		var input = InCarrier ("png", data);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var fs = new MockFileSystem ();
		var save = file.SaveToFile ("/a.png", fs);

		if (shape != "inside the removed UserComment") {
			Assert.IsFalse (save.IsSuccess, shape);
			StringAssert.Contains (save.Error, "SubIFD offsets");
			CollectionAssert.AreEqual (input, file.Render ().ToArray ());
		} else {
			Assert.IsTrue (save.IsSuccess, save.Error);
			var exif = ExifOf (file.Render ().ToArray ());
			Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count);
			AssertOnlyTheCopiesAreGone (data, exif, shape, 0x9286, 0x9C9C);
			CollectionAssert.AreEqual (data[arrayAt..(arrayAt + 8)], exif[arrayAt..(arrayAt + 8)], "the SubIFD offsets are kept");
		}
	}

	// What exiftool does not read keeps its bytes: the next IFD of an Exif IFD (exiftool follows only IFD0's chain)
	// and a GPS table (its 0x9286 is not a UserComment), as measured.
	[TestMethod]
	[DataRow ("the next IFD of an Exif IFD")]
	[DataRow ("a GPS table")]
	public void Edit_LeavesTablesExiftoolDoesNotReadAsTheyAre (string where)
	{
		const int Ifd0 = 8, E = 60, X = 100, Value = 160, Other = 200;
		var otherUc = "ASCII\0\0\0other-uc"u8.ToArray ();
		var tiff = new TiffBuilder (Other + otherUc.Length, bigEndian: false).Header (Ifd0).Bytes (Value, SecretUc).Bytes (Other, otherUc)
			.Table (X, [(0x9286, 7, otherUc.Length, Other)]);
		if (where == "a GPS table")
			tiff.Table (Ifd0, [(0x8769, 4, 1, E), (0x8825, 4, 1, X)]).Table (E, [(0x9286, 7, SecretUc.Length, Value)]);
		else
			tiff.Table (Ifd0, [(0x8769, 4, 1, E)]).Table (E, [(0x9286, 7, SecretUc.Length, Value)], next: X);
		var data = tiff.ToArray ();
		var file = Open (InCarrier ("png", data), "a.png");
		file.Tag!.Comment = "New";

		Assert.IsTrue (file.SaveToFile ("/a.png", new MockFileSystem ()).IsSuccess);
		var exif = ExifOf (file.Render ().ToArray ());

		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, where);
		AssertOnlyTheCopiesAreGone (data, exif, where, 0x9286, 0x9C9C);
		CollectionAssert.AreEqual (data[X..(X + 18)], exif[X..(X + 18)], $"{where}: the table is kept");
		CollectionAssert.AreEqual (data[Other..], exif[Other..], $"{where}: and so is its value");
	}

	// With no mirrored field changed there is nothing to remove: the block is left as it is.
	[TestMethod]
	public void RemoveExif_WithNoChangedField_ChangesNothing ()
	{
		var tiff = new TiffBuilder (64, bigEndian: false).Header (8).Table (8, [(0x9286, 7, 8, 40)]).Bytes (40, "ASCII\0\0\0"u8).ToArray ();
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, (ImageStores)0x3F);

		Assert.IsTrue (ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error), error);

		Assert.IsFalse (changed);
		CollectionAssert.AreEqual (copy, tiff);
	}

	// The structure of a table no reader reads is kept whole when a removed value covers it: its count, its entries
	// and its next offset, as far as the data goes (and only that: a removed value past it is still cleared). An
	// unread table at offset 8, pointers into the header or at the data's last byte, and the next IFD of an Exif IFD
	// or a GPS table (one level: the next IFD of that next IFD is named by nothing) are handled too.
	[TestMethod]
	[DataRow ("the removed value covers an unread table's entries")]
	[DataRow ("the removed value covers the last bytes of an unread table")]
	[DataRow ("the removed value lies before an unread table")]
	[DataRow ("an unread table at offset 8")]
	[DataRow ("a pointer into the header")]
	[DataRow ("a pointer to the data's last byte")]
	[DataRow ("the removed value is the next IFD of an Exif IFD")]
	[DataRow ("the removed value is the next IFD of a GPS table")]
	[DataRow ("the removed value is the next IFD of an Exif IFD's next IFD")]
	public void Edit_KeepsTheStructureOfTablesNoReaderReads (string shape)
	{
		const int Value = 100;
		var removed = "ASCII\0\0\0secret-uc-long-text"u8.ToArray ();
		var length = Value + removed.Length + 40;
		var tiff = new TiffBuilder (length, bigEndian: false).Bytes (Value, removed);
		var comment = (0x9286, 7, removed.Length, Value);
		int watchedStart, watchedEnd;
		var cleared = false;
		switch (shape) {
			case "the removed value covers an unread table's entries":
				// P's count (257) sits just before the value; its first entries are the value's bytes.
				tiff.Header (8).Table (8, [comment, (0x8769, 4, 1, Value - 2)]).U16 (Value - 2, 0x0101);
				(watchedStart, watchedEnd) = (Value - 2, length);
				break;
			case "the removed value covers the last bytes of an unread table":
				// P counts 5 entries, which end 2 bytes past the data; the removed value is the data's last bytes.
				length = Value + removed.Length;
				tiff = new TiffBuilder (length, bigEndian: false).Bytes (Value, removed).Header (8).Table (8, [comment, (0x8769, 4, 1, length - (12 * 5))]).U16 (length - (12 * 5), 5);
				(watchedStart, watchedEnd) = (Value, length);
				break;
			case "the removed value lies before an unread table":
				// The unread table's structure starts after the value, so the value is still cleared.
				tiff.Header (8).Table (8, [comment, (0x8769, 4, 1, Value + removed.Length + 4)]).U16 (Value + removed.Length + 4, 0x0101);
				(watchedStart, watchedEnd) = (Value, Value + removed.Length);
				cleared = true;
				break;
			case "an unread table at offset 8":
				// IFD0 lies at 60; the table at 8 counts 257 entries, and its count is the removed value's first bytes.
				tiff.Header (60).Table (60, [(0x9286, 7, 6, 8), (0x8769, 4, 1, 8)]).U16 (8, 0x0101);
				(watchedStart, watchedEnd) = (8, 10);
				break;
			case "the removed value is the next IFD of an Exif IFD":
				// No reader reads the next IFD of an Exif IFD (measured); here it is the removed value, whose first
				// bytes ("AS") count more entries than the data holds.
				tiff.Header (8).Table (8, [(0x8769, 4, 1, 40)]).Table (40, [comment], next: Value);
				(watchedStart, watchedEnd) = (Value, Value + removed.Length);
				break;
			case "the removed value is the next IFD of a GPS table":
				// Nor the next IFD of a GPS table (measured): the same, with the GPS table's next offset.
				tiff.Header (8).Table (8, [(0x8769, 4, 1, 40), (0x8825, 4, 1, 60)]).Table (40, [comment]).Table (60, [(1, 2, 2, 0x4E)], next: Value);
				(watchedStart, watchedEnd) = (Value, Value + removed.Length);
				break;
			case "the removed value is the next IFD of an Exif IFD's next IFD":
				// The Exif IFD's next IFD (at 60) is kept, but nothing names the table after it: the value is cleared.
				tiff.Header (8).Table (8, [(0x8769, 4, 1, 40)]).Table (40, [comment], next: 60).Table (60, [(0x0131, 2, 4, 0x00636261)], next: Value);
				(watchedStart, watchedEnd) = (Value, Value + removed.Length);
				cleared = true;
				break;
			case "a pointer into the header":
				tiff.Header (8).Table (8, [comment, (0x8769, 4, 1, 7)]);
				(watchedStart, watchedEnd) = (0, 8);
				break;
			default:
				tiff.Header (8).Table (8, [comment, (0x8769, 4, 1, length - 1)]).Bytes (length - 1, [0xFF]);
				(watchedStart, watchedEnd) = (length - 1, length);
				break;
		}

		var data = tiff.ToArray ();
		var file = Open (InCarrier ("png", data), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		AssertOnlyTheCopiesAreGone (data, exif, shape, 0x9286, 0x9C9C);
		if (cleared)
			Assert.IsTrue (exif[watchedStart..watchedEnd].All (b => b == 0), $"{shape}: the removed value is cleared");
		else
			CollectionAssert.AreEqual (data[watchedStart..watchedEnd], exif[watchedStart..watchedEnd], $"{shape}: the structure is kept");
	}

	// The edges of the data, in both carriers (a JPEG's TIFF starts 6 bytes into its segment): a table whose last
	// entry runs past the data, a SubIFD array cut short or ending exactly at the end, and a next-IFD offset that is
	// the data's last 4 bytes. Nothing is read past the data, and what fits is read.
	[TestMethod]
	[DataRow ("png", "an Exif IFD whose last entry runs 2 bytes past the data")]
	[DataRow ("jpeg", "an Exif IFD whose last entry runs 2 bytes past the data")]
	[DataRow ("png", "a SubIFD array cut short at the end of the data")]
	[DataRow ("jpeg", "a SubIFD array cut short at the end of the data")]
	[DataRow ("png", "a SubIFD array that ends at the end of the data")]
	[DataRow ("jpeg", "a SubIFD array that ends at the end of the data")]
	[DataRow ("png", "IFD0's next offset is the data's last 4 bytes")]
	[DataRow ("jpeg", "IFD0's next offset is the data's last 4 bytes")]
	[DataRow ("png", "a pointer to the end of the data, after an edited table with no next offset")]
	public void Edit_AtTheEdgesOfTheData_ReadsWhatFitsAndNothingPast (string carrier, string shape)
	{
		const int Value = 120;
		var copy = "ASCII\0\0\0old-copy"u8.ToArray ();
		var comment = (0x9286, 7, copy.Length, Value);
		byte[] data;
		switch (shape) {
			case "an Exif IFD whose last entry runs 2 bytes past the data":
				// The second Exif IFD counts 2 entries; only 22 of their 24 bytes are there, so no reader reads it.
				var buffer = new TiffBuilder (Value + copy.Length + 32, bigEndian: false).Header (8).Table (8, [comment, (0x8769, 4, 1, Value + copy.Length - 2)]).Bytes (Value, copy)
					.Table (Value + copy.Length - 2, [(0x9286, 7, copy.Length, Value), (0x9003, 7, 4, 0x31303032)]).ToArray ();
				data = buffer[..(Value + copy.Length + 22)];
				break;
			case "a SubIFD array cut short at the end of the data":
				data = new TiffBuilder (Value + copy.Length + 4, bigEndian: false).Header (8).Table (8, [comment, (0x014A, 4, 2, Value + copy.Length)]).Bytes (Value, copy)
					.U32 (Value + copy.Length, 60).ToArray ();
				break;
			case "a SubIFD array that ends at the end of the data":
				data = new TiffBuilder (Value + copy.Length + 8, bigEndian: false).Header (8).Table (8, [(0x014A, 4, 2, Value + copy.Length)]).Bytes (Value, copy)
					.Table (60, [comment]).Table (80, [(0x9003, 7, 4, 0x31303032)]).U32 (Value + copy.Length, 60).U32 (Value + copy.Length + 4, 80).ToArray ();
				break;
			case "a pointer to the end of the data, after an edited table with no next offset":
				// IFD0 is last and has no next offset, so its range runs 4 bytes past the data; a second pointer names
				// the offset just past the data, where no table can be. Nothing there is IFD structure: the edit saves.
				var end = Value + copy.Length + 2 + 24;
				data = new TiffBuilder (end + 4, bigEndian: false).Header (Value + copy.Length).Bytes (Value, copy)
					.Table (Value + copy.Length, [comment, (0x8769, 4, 1, end)]).ToArray ()[..end];
				break;
			default:
				// IFD1 at 40 holds the copy; IFD0 is the last thing in the data, its next offset the last 4 bytes.
				data = new TiffBuilder (Value + copy.Length + 18, bigEndian: false).Header (Value + copy.Length).Bytes (Value, copy)
					.Table (40, [comment]).Table (Value + copy.Length, [(0x9003, 7, 4, 0x31303032)], next: 40).ToArray ();
				break;
		}

		var file = Open (InCarrier (carrier, data), carrier == "png" ? "a.png" : "a.jpg");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{carrier} {shape}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (data.Length, exif.Length, "nothing is written past the data");
		AssertOnlyTheCopiesAreGone (data, exif, $"{carrier} {shape}", 0x9286, 0x9C9C);
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, $"{carrier} {shape}: a copy is left");
	}

	[TestMethod]
	public void Edit_ExifWithStripOffsetsPastTheData_DoesNotThrow ()
	{
		// StripOffsets (0x0111) as two LONGs at an offset past the block, and its byte counts likewise.
		var tiff = ExifTiff ([(0x010F, "Canon"), (0x013B, "Old artist")], []);
		var far = Concat (tiff[..10], new byte[12 * 2], tiff[10..]);
		far[8] = 4;
		for (var (at, tag) = (10, 0x0111); at < 34; at += 12, tag = 0x0117) {
			BitConverter.GetBytes ((ushort)tag).CopyTo (far, at);
			BitConverter.GetBytes ((ushort)4).CopyTo (far, at + 2);
			BitConverter.GetBytes (2).CopyTo (far, at + 4);
			BitConverter.GetBytes (0x7FFF0000).CopyTo (far, at + 8);
		}

		// Shift the original entries' out-of-line offsets by the 24 bytes inserted.
		for (var n = 2; n < 4; n++) {
			var entry = 10 + (12 * n);
			var value = BitConverter.ToInt32 (far, entry + 8);
			if (BitConverter.ToInt32 (far, entry + 4) > 4)
				BitConverter.GetBytes (value + 24).CopyTo (far, entry + 8);
		}

		var file = Open (Png (PngChunk ("eXIf", far), Text ("Author", "a")), "a.png");
		file.Tag!.Artist = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual ("Canon", ExifReaderModel.Of (PngChunks (file.Render ().ToArray ()).Single (c => c.Type == "eXIf").Payload).Strings ()[0x010F]);
	}

	// exiftool reads an interoperability IFD with the Exif tags and follows its pointers, and follows a pointer typed
	// BYTE, SHORT, SSHORT, SBYTE, SLONG or UNDEFINED (count 1) as it follows a LONG one (measured, the probes in
	// Golden/exif-reader-probes.txt): every copy it reads that way is removed, and nothing else readers see changes.
	[TestMethod]
	[DataRow ("a UserComment in the interoperability IFD", false)]
	[DataRow ("a UserComment in the interoperability IFD", true)]
	[DataRow ("an Exif IFD under the interoperability IFD", false)]
	[DataRow ("an Exif IFD pointer typed SHORT", false)]
	[DataRow ("an Exif IFD pointer typed SHORT", true)]
	[DataRow ("an Exif IFD pointer typed SSHORT", false)]
	[DataRow ("an Exif IFD pointer typed BYTE", true)]
	[DataRow ("an Exif IFD pointer typed SBYTE", false)]
	[DataRow ("an Exif IFD pointer typed SLONG", true)]
	[DataRow ("an Exif IFD pointer typed UNDEFINED", false)]
	[DataRow ("a SubIFD pointer typed SHORT", false)]
	[DataRow ("a SubIFD array of two SHORT offsets", true)]
	[DataRow ("an Exif IFD pointer typed SHORT in the Exif IFD", false)]
	[DataRow ("an Exif IFD pointer typed SSHORT in the Exif IFD", false)]
	[DataRow ("an Exif IFD pointer typed BYTE in the Exif IFD", false)]
	[DataRow ("an Exif IFD pointer typed SBYTE in the Exif IFD", false)]
	[DataRow ("an Exif IFD pointer typed SLONG in the Exif IFD", false)]
	[DataRow ("an Exif IFD pointer typed UNDEFINED in the Exif IFD", false)]
	public void Edit_RemovesTheCopiesOfEveryTableExiftoolReads_ByItsPointerRules (string shape, bool bigEndian)
	{
		const int Exif = 40, Inner = 100, Values = 160;
		var t = new TiffBuilder (Values + 40, bigEndian).Header (8);
		// In IFD0 a pointer's field read as a LONG may name the same table (TagLib#'s reading); in the Exif IFD only
		// the typed reading can.
		var inExif = shape.EndsWith (" in the Exif IFD", StringComparison.Ordinal);
		var typed = inExif ? shape[..^" in the Exif IFD".Length] : shape;
		(int Tag, int Type, int Size) pointer = typed switch {
			_ when typed.EndsWith ("typed SHORT", StringComparison.Ordinal) && typed.StartsWith ("an Exif", StringComparison.Ordinal) => (0x8769, 3, 2),
			_ when typed.EndsWith ("SSHORT", StringComparison.Ordinal) => (0x8769, 8, 2),
			_ when typed.EndsWith ("typed BYTE", StringComparison.Ordinal) => (0x8769, 1, 1),
			_ when typed.EndsWith ("SBYTE", StringComparison.Ordinal) => (0x8769, 6, 1),
			_ when typed.EndsWith ("SLONG", StringComparison.Ordinal) => (0x8769, 9, 4),
			_ when typed.EndsWith ("UNDEFINED", StringComparison.Ordinal) => (0x8769, 7, 1),
			_ when shape.StartsWith ("a SubIFD pointer", StringComparison.Ordinal) => (0x014A, 3, 2),
			_ => (0, 0, 0),
		};
		if (shape.Contains ("interoperability", StringComparison.Ordinal)) {
			var inner = shape.StartsWith ("an Exif IFD under", StringComparison.Ordinal);
			t.Table (8, [(0x8769, 4, 1, Exif)]).Table (Exif, [(0xA005, 4, 1, Inner)])
				.Table (Inner, inner ? [(0x0001, 2, 4, 0x00383952), (0x8769, 4, 1, Inner + 40)] : [(0x0001, 2, 4, 0x00383952), (0x9286, 7, SecretUc.Length, Values)]);
			if (inner)
				t.Table (Inner + 40, [(0x9286, 7, SecretUc.Length, Values)]);
		} else if (shape.StartsWith ("a SubIFD array", StringComparison.Ordinal)) {
			t.Table (8, [(0x014A, 3, 2, 0)]).U16 (8 + 2 + 8, Exif).U16 (8 + 2 + 10, Inner)
				.Table (Exif, [(0x9286, 7, SecretUc.Length, Values)]).Table (Inner, [(0x9C9C, 1, 4, 0x00410041)]);
		} else {
			// The holder of the typed pointer: IFD0, or an Exif IFD at Inner that IFD0 names.
			var holder = inExif ? Inner : 8;
			if (inExif)
				t.Table (8, [(0x8769, 4, 1, Inner)]);
			t.Table (holder, [(pointer.Tag, pointer.Type, 1, 0)]).Table (Exif, [(0x9286, 7, SecretUc.Length, Values)]);
			var field = holder + 2 + 8;
			if (pointer.Size == 1)
				t.Bytes (field, [Exif]);
			else if (pointer.Size == 2)
				t.U32 (field, 0).U16 (field, Exif);
			else
				t.U32 (field, Exif);
		}

		var tiff = t.Bytes (Values, SecretUc).ToArray ();
		Assert.IsTrue (UserCommentsInEveryExifIfd (tiff).Count > 0, $"{shape}: readers show a copy");
		var file = Open (InCarrier ("jpeg", tiff), "a.jpg");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, $"{shape}: a copy is left");
		AssertOnlyTheCopiesAreGone (tiff, exif, shape, 0x9286, 0x9C9C);
	}

	// Two pointer arrays at one offset with one count but different types name different tables: a SubIFD array of 8
	// LONGs, [0x2864, 0, ...], and one of 8 BYTEs over its first 8 bytes, [0x64, 0x28, 0, ...] (little-endian).
	// exiftool reads each by its own type (measured), so all three tables are read and each copy is removed.
	[TestMethod]
	public void Edit_TwoPointerArraysAtOneOffset_AreEachReadByTheirOwnType ()
	{
		const int Array = 200, Values = 140, Long = 0x2864;
		int[] tables = [Long, 0x64, 0x28];
		var t = new TiffBuilder (Long + 60).Header (8).Table (8, [(0x014A, 4, 8, Array), (0x014A, 1, 8, Array)])
			.U32 (Array, Long).Bytes (Values, SecretUc);
		foreach (var table in tables)
			t.Table (table, [(0x9286, 7, SecretUc.Length, Values), (0x9003, 2, 4, 0x30303032)]);
		var tiff = t.ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = ExifOf (file.Render ().ToArray ());
		foreach (var table in tables)
			Assert.AreEqual (1, U16 (exif, table, false), $"the table at {table} keeps only its other entry");
		AssertOnlyTheCopiesAreGone (tiff, exif, "two arrays at one offset", 0x9286, 0x9C9C);
	}

	// Zeroing a removed value never touches a table's next-IFD offset: IFD0's removed XPComment value starts at IFD1's
	// next offset, which names IFD2; after the edit the chain still reaches IFD2.
	[TestMethod]
	public void Edit_RemovedValue_NeverZeroesATablesNextOffset ()
	{
		const int Ifd1 = 40, Next = Ifd1 + 2 + 12, Ifd2 = 100;
		var tiff = new TiffBuilder (Ifd2 + 40).Header (8).Table (8, [(0x9C9C, 1, 20, Next)], next: Ifd1)
			.Table (Ifd1, [(0x0131, 2, 4, 0x41414141)], next: Ifd2).Table (Ifd2, [(0x0132, 2, 4, 0x32303031)]).ToArray ();
		Assert.AreEqual (3, ExifReaderModel.Of (tiff).Tables.Count, "IFD0, IFD1 and IFD2 are read");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = ExifOf (file.Render ().ToArray ());
		CollectionAssert.AreEqual (tiff[Next..(Next + 4)], exif[Next..(Next + 4)], "IFD1's next offset");
		AssertOnlyTheCopiesAreGone (tiff, exif, "IFD1's next offset inside a removed value", 0x9286, 0x9C9C);
	}

	// A table no reader reads (the next IFD of an Exif IFD: exiftool, ImageIO and TagLib# all ignore it, measured) may
	// have a value inside the Exif IFD the edit compacts: compacting rewrites those bytes, which no reader shows, so
	// the edit saves.
	[TestMethod]
	public void Edit_AValueOfATableNoReaderReads_InsideTheCompactedTable_Saves ()
	{
		const int Exif = 40, Unread = 120, Values = 160;
		var tiff = new TiffBuilder (Values + 40).Header (8).Table (8, [(0x8769, 4, 1, Exif)])
			.Table (Exif, [(0x9286, 7, SecretUc.Length, Values), (0x9003, 2, 20, Values + 20)], next: Unread)
			.Table (Unread, [(0x9004, 2, 20, Exif + 2)]).Bytes (Values, SecretUc).Bytes (Values + 20, "2001:02:03 04:05:06\0"u8).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (1, U16 (exif, Exif, false), "the Exif IFD keeps only DateTimeOriginal");
		AssertOnlyTheCopiesAreGone (tiff, exif, "an unread table's value inside the compacted table", 0x9286, 0x9C9C);
	}

	// exiftool reads an UNDEFINED pointer value as unsigned: 200 names the table at 200 (measured, probe
	// "pointer 8769 in IFD0 type 7 count 1"). In an Exif IFD only that typed reading names it.
	[TestMethod]
	public void Edit_RemovesTheCopyAnUndefinedPointerOf200Names ()
	{
		const int Exif = 40, Target = 200, Values = 140;
		var tiff = new TiffBuilder (Target + 40).Header (8).Table (8, [(0x8769, 4, 1, Exif)])
			.Table (Exif, [(0x9003, 2, 4, 0x30303032), (0x8769, 7, 1, Target)])
			.Table (Target, [(0x9286, 7, SecretUc.Length, Values), (0x9004, 2, 4, 0x30303032)]).Bytes (Values, SecretUc).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (1, U16 (exif, Target, false), "the table at 200 keeps only its other entry");
		AssertOnlyTheCopiesAreGone (tiff, exif, "an UNDEFINED pointer of 200", 0x9286, 0x9C9C);
	}

	// Bytes a kept value uses are never zeroed, though other kept values lie inside it: IFD0's Artist (100 bytes)
	// holds Copyright, Make and Model near its start and, past them, the removed XPComment. Artist keeps every byte,
	// and the edit saves.
	[TestMethod]
	public void Edit_RemovedValueInsideAKeptValueThatHoldsOthers_IsNotZeroed ()
	{
		const int Artist = 100;
		var artist = Encoding.ASCII.GetBytes (new string ('a', 99) + "\0");
		var tiff = new TiffBuilder (Artist + 120).Header (8)
			.Table (8, [(0x013B, 2, 100, Artist), (0x8298, 2, 5, Artist + 10), (0x010F, 2, 5, Artist + 20), (0x0110, 2, 5, Artist + 30), (0x9C9C, 1, 10, Artist + 50)])
			.Bytes (Artist, artist).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = ExifOf (file.Render ().ToArray ());
		CollectionAssert.AreEqual (artist, exif[Artist..(Artist + 100)], "the Artist value keeps its bytes");
		AssertOnlyTheCopiesAreGone (tiff, exif, "a removed value inside a kept one", 0x9286, 0x9C9C);
	}

	// IFD0 at the end of the data, its next offset (IFD1, before it) the data's last 4 bytes: compacting IFD0 keeps the
	// chain, so IFD1 is still read.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_CompactingATableWhoseNextOffsetEndsTheData_KeepsTheChain (bool bigEndian)
	{
		const int Ifd1 = 8, Ifd0 = 26;
		var tiff = new TiffBuilder (Ifd0 + 2 + (2 * 12) + 4, bigEndian).Header (Ifd0)
			.Table (Ifd1, [(0x0131, 2, 4, bigEndian ? 0x78797A00 : 0x007A7978)])
			.Table (Ifd0, [(0x9286, 7, 4, 0x61626364), (0x0110, 2, 4, bigEndian ? 0x61626300 : 0x00636261)], next: Ifd1)
			.ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		AssertOnlyTheCopiesAreGone (tiff, ExifOf (file.Render ().ToArray ()), "the next offset ends the data", 0x9286);
	}

	// Strip offsets and byte counts given as SHORT or LONG arrays out of line: a removed UserComment that shares bytes
	// with a strip they name is not cleared (the strip keeps its bytes). An array that ends at the data's last byte is
	// read; one that runs past it names no strip (exiftool reads none), so the value is cleared.
	[TestMethod]
	[DataRow (3, false, false)]
	[DataRow (4, false, false)]
	[DataRow (3, true, false)]
	[DataRow (4, true, false)]
	[DataRow (3, true, true)]
	public void Edit_KeepsTheBytesOfEveryStripAnArrayNames (int type, bool atTheEnd, bool cut)
	{
		const int V = 200;
		var size = type == 3 ? 2 : 4;
		var length = atTheEnd ? 240 : 300;
		var lengths = atTheEnd ? length - (3 * size) + (cut ? 2 : 0) : 260;
		var offsets = lengths - (3 * size);
		var removed = "ASCII\0\0\0old-comment!"u8.ToArray ();
		var tiff = new TiffBuilder (length).Header (8)
			.Table (8, [(0x0111, type, 3, offsets), (0x0117, type, 3, lengths), (0x9286, 7, removed.Length, V)])
			.Bytes (V, removed);
		for (var n = 0; n < 3; n++) {
			var (strip, bytes) = (new[] { 120, 140, 205 }[n], 10);
			if (size == 2) {
				tiff.U16 (offsets + (2 * n), strip);
				if (lengths + (2 * n) + 2 <= length)
					tiff.U16 (lengths + (2 * n), bytes);
			} else {
				tiff.U32 (offsets + (4 * n), strip).U32 (lengths + (4 * n), bytes);
			}
		}

		var data = tiff.ToArray ();
		var file = Open (InCarrier ("png", data), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var exif = ExifOf (file.Render ().ToArray ());
		AssertOnlyTheCopiesAreGone (data, exif, "strips", 0x9286);
		if (cut) {
			CollectionAssert.AreEqual (new byte[removed.Length], exif[V..(V + removed.Length)], "no strip is named: the value is cleared");
			return;
		}

		CollectionAssert.AreEqual (data[V..(V + removed.Length)], exif[V..(V + removed.Length)], "the third strip shares the value's bytes: they are kept");
	}

	// A data range of length 0 (an empty thumbnail) protects no byte: the removed XPComment around its offset is
	// zeroed.
	[TestMethod]
	public void Edit_AnEmptyThumbnailInsideARemovedValue_DoesNotKeepItsBytes ()
	{
		const int Ifd1 = 60, Xp = 100;
		var tiff = new TiffBuilder (Xp + 40).Header (8).Table (8, [(0x9C9C, 1, 16, Xp)], next: Ifd1)
			.Table (Ifd1, [(0x0201, 4, 1, Xp + 8), (0x0202, 4, 1, 0)]).Bytes (Xp, "x\0p\0-\0o\0l\0d\0!\0!\0"u8).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (new byte[16], ExifOf (file.Render ().ToArray ())[Xp..(Xp + 16)], "the old XPComment is zeroed");
	}

	// Pointers no reader follows name no table: exiftool follows no Exif IFD pointer with count 0 or 2, typed ASCII or
	// RATIONAL, nothing inside a GPS table (it reads GPS tables with the GPS tags), only the first value of an
	// interoperability IFD pointer, no negative value of a signed type and no UNDEFINED pointer of count 2; TagLib#
	// reads only IFD0's pointer field as a LONG, and ImageIO follows a subset of what exiftool follows (measured). The table they name is
	// read by no one; the edit is not refused and leaves it byte for byte.
	[TestMethod]
	[DataRow ("IFD0's Exif IFD pointer with count 2, its field naming nothing as a LONG")]
	[DataRow ("an Exif IFD pointer with count 0 in the Exif IFD")]
	[DataRow ("an Exif IFD pointer typed ASCII in the Exif IFD")]
	[DataRow ("an Exif IFD pointer typed RATIONAL in the Exif IFD")]
	[DataRow ("an Exif IFD pointer inside a GPS table")]
	[DataRow ("a SubIFD pointer inside a GPS table")]
	[DataRow ("the second value of an interoperability IFD pointer in the Exif IFD")]
	[DataRow ("an Exif IFD pointer typed SBYTE with a negative value in the Exif IFD")]
	[DataRow ("an Exif IFD pointer typed SBYTE of -128 in the Exif IFD")]
	[DataRow ("an Exif IFD pointer typed SSHORT of -32768 in the Exif IFD")]
	[DataRow ("a SubIFD pointer typed UNDEFINED with count 2")]
	public void Edit_LeavesATableNoReaderFollowsAPointerTo_AsItIs (string shape)
	{
		const int Holder = 40, Values = 160;
		// SBYTE 0xC8 is -56 and 0x80 is -128, SSHORT 0x8000 is -32768: the table there is named only by a reader that
		// took the value as unsigned.
		var target = shape.Contains ("negative", StringComparison.Ordinal) ? 0xC8 : shape.Contains ("-128", StringComparison.Ordinal) ? 0x80 : shape.Contains ("-32768", StringComparison.Ordinal) ? 0x8000 : 100;
		var t = new TiffBuilder (Math.Max (Values + 40, target + 60)).Header (8);
		(int, int, long, long) pointer = shape switch {
			_ when shape.StartsWith ("IFD0's", StringComparison.Ordinal) => (0x8769, 3, 2, target | (target << 16)),
			_ when shape.StartsWith ("the second value", StringComparison.Ordinal) => (0xA005, 3, 2, (long)target << 16),
			_ when shape.Contains ("SBYTE", StringComparison.Ordinal) => (0x8769, 6, 1, target),
			_ when shape.Contains ("SSHORT", StringComparison.Ordinal) => (0x8769, 8, 1, target),
			_ when shape.Contains ("UNDEFINED", StringComparison.Ordinal) => (0x014A, 7, 2, target),
			_ when shape.Contains ("count 0", StringComparison.Ordinal) => (0x8769, 4, 0, target),
			_ when shape.Contains ("ASCII", StringComparison.Ordinal) => (0x8769, 2, 1, target),
			_ when shape.Contains ("RATIONAL", StringComparison.Ordinal) => (0x8769, 5, 1, target),
			_ when shape.StartsWith ("an Exif", StringComparison.Ordinal) => (0x8769, 4, 1, target),
			_ => (0x014A, 4, 1, target),
		};
		if (shape.EndsWith ("inside a GPS table", StringComparison.Ordinal))
			t.Table (8, [(0x8825, 4, 1, Holder), (0x9C9C, 1, SecretUc.Length, Values)]).Table (Holder, [(0x0001, 2, 2, 0x4E), pointer]);
		else if (shape.EndsWith ("in the Exif IFD", StringComparison.Ordinal))
			t.Table (8, [(0x8769, 4, 1, Holder), (0x9C9C, 1, SecretUc.Length, Values)]).Table (Holder, [(0x9003, 2, 4, 0x30303032), pointer]);
		else
			t.Table (8, [pointer, (0x9C9C, 1, SecretUc.Length, Values)]);
		var tiff = t.Table (target, [(0x9286, 7, 8, 0), (0x9003, 2, 4, 0x30303032)]).Bytes (Values, SecretUc).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		CollectionAssert.AreEqual (tiff[target..(target + 30)], exif[target..(target + 30)], $"{shape}: the table no reader reads keeps its bytes");
		AssertOnlyTheCopiesAreGone (tiff, exif, shape, 0x9286, 0x9C9C);
	}

	// TagLib# reads IFD0's Exif IFD pointer as its 4-byte field taken as a LONG, whatever its type and count (measured
	// with every type and counts 0-2, and on corpus files whose pointer is typed SHORT or has count 2): the copy it
	// shows from that table is removed, as is the one exiftool reads where the two decodings name different tables.
	// With count 2 or RATIONAL the field is the offset of the pointer's own value, so that value is the table's first
	// bytes; no reader shows it (exiftool: "Bad value"), and compacting the table may rewrite it.
	[TestMethod]
	[DataRow ("count 0", false)]
	[DataRow ("typed ASCII", true)]
	[DataRow ("typed SHORT with count 2", false)]
	[DataRow ("typed SHORT, its first value 0", true)]
	[DataRow ("typed BYTE, the field naming another table as a LONG", false)]
	[DataRow ("count 2", false)]
	[DataRow ("typed RATIONAL", true)]
	public void Edit_RemovesTheCopyTagLibSharpReads_ThroughIfd0sExifPointerFieldAsALong (string shape, bool bigEndian)
	{
		// The BYTE row: exiftool reads the byte 100, TagLib# the LONG 0x2864.
		const int Raw = 40, Values = 160, Byte = 100, Long = 0x2864;
		var t = new TiffBuilder (Long + 60, bigEndian).Header (8);
		var (type, count) = shape switch {
			"count 0" => (4, 0),
			"count 2" => (4, 2),
			"typed ASCII" => (2, 4),
			"typed RATIONAL" => (5, 1),
			"typed SHORT with count 2" => (3, 2),
			"typed SHORT, its first value 0" => (3, 1),
			_ => (1, 1),
		};
		var two = type == 1;
		int[] tables = two ? [Byte, Long] : [Raw];
		t.Table (8, [(0x8769, type, count, two ? 0x2800 | Byte : Raw)]).Bytes (Values, SecretUc);
		foreach (var table in tables)
			t.Table (table, [(0x9286, 7, SecretUc.Length, Values), (0x9003, 2, 4, 0x30303032)]);
		var tiff = t.ToArray ();
		var file = Open (InCarrier ("jpeg", tiff), "a.jpg");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		foreach (var table in tables)
			Assert.AreEqual (1, U16 (exif, table, bigEndian), $"{shape}: the table at {table} keeps only its other entry");
		AssertOnlyTheCopiesAreGone (tiff, exif, shape, 0x9286, 0x9C9C);
	}

	static int U16 (byte[] data, int at, bool bigEndian) => bigEndian ? (data[at] << 8) | data[at + 1] : data[at] | (data[at + 1] << 8);

	// Zeroing a removed value must not empty a table a typed pointer names: big-endian, IFD0's Exif IFD (or GPS)
	// pointer typed SHORT names a table whose count is the last two bytes of IFD0's removed UserComment. Read as a
	// LONG the pointer named nothing and the count was zeroed, emptying the table for exiftool. Typed ASCII, the
	// pointer names the same table only for TagLib#, which reads IFD0's pointer field as a LONG.
	[TestMethod]
	[DataRow (0x8769, 3)]
	[DataRow (0x8825, 3)]
	[DataRow (0x8769, 2)]
	[DataRow (0x8825, 2)]
	public void Edit_NeverZeroesATableATypedPointerNames (int tag, int type)
	{
		const int Values = 100;
		var uc = "ASCII\0\0\0secret-uc"u8.ToArray ();
		var target = Values + uc.Length - 2;
		var t = new TiffBuilder (target + 70, bigEndian: true).Header (8)
			.Table (8, [(0x9286, 7, uc.Length, Values), (tag, type, 1, type == 3 ? target << 16 : target)])
			.Bytes (Values, uc);
		var tiff = (tag == 0x8825 ? t.Table (target, [(0x0001, 2, 2, 0x4E000000), (0x0002, 5, 3, target + 40)]) : t.Table (target, [(0x9003, 2, 4, 0x32303030), (0xA420, 2, 4, 0x69642D31)])).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		if (save.IsSuccess)
			AssertOnlyTheCopiesAreGone (tiff, ExifOf (file.Render ().ToArray ()), "the typed pointer's table keeps its count", 0x9286, 0x9C9C);
		else
			StringAssert.Contains (save.Error, "cannot be updated");
		Assert.AreEqual (2, ExifReaderModel.AnyReader (ExifOf (file.Render ().ToArray ())).Tables.Single (x => x.At == target).Count);
	}

	// A kept value, or the thumbnail an offset/length pair names, whose bytes lie inside a table the edit compacts
	// would be rewritten by the compaction: the edit is refused with its own reason, and nothing is written.
	[TestMethod]
	[DataRow ("IFD0's Artist is the Exif IFD's second entry")]
	[DataRow ("IFD1's thumbnail is the Exif IFD's entries after the UserComment")]
	[DataRow ("IFD1's thumbnail, its pair given twice, the first naming the Exif IFD's entries after the UserComment")]
	public void Edit_RefusesToRewriteAValueInsideATableItCompacts (string shape)
	{
		const int Exif = 60, Ifd1 = 120, Values = 180;
		var thumb = shape.StartsWith ("IFD1", StringComparison.Ordinal);
		var t = new TiffBuilder (Values + 40).Header (8)
			.Table (Exif, [(0x9286, 7, SecretUc.Length, Values), (0x9003, 2, 4, 0x41414141), (0x9004, 2, 4, 0x42424242)])
			.Bytes (Values, SecretUc);
		// Given twice, readers take the first pair (exiftool and the tests' reader); the second names bytes past the value.
		if (thumb && shape.Contains ("twice", StringComparison.Ordinal))
			t.Table (8, [(0x8769, 4, 1, Exif)], next: Ifd1).Table (Ifd1, [(0x0201, 4, 1, Exif + 14), (0x0202, 4, 1, 24), (0x0201, 4, 1, Values + 20), (0x0202, 4, 1, 4)]);
		else if (thumb)
			t.Table (8, [(0x8769, 4, 1, Exif)], next: Ifd1).Table (Ifd1, [(0x0201, 4, 1, Exif + 14), (0x0202, 4, 1, 24)]);
		else
			t.Table (8, [(0x8769, 4, 1, Exif), (0x013B, 2, 12, Exif + 14)]);
		var input = InCarrier ("png", t.ToArray ());
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, shape);
		StringAssert.Contains (save.Error, "a value inside an IFD table");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{shape}: nothing is written");
	}

	// The same at the data's end: IFD0 ends the data, and a kept value (Make, 8 bytes) or the thumbnail an
	// offset/length pair names is its last 8 bytes, which compacting IFD0 rewrites (exiftool would show them change).
	[TestMethod]
	[DataRow ("a kept value")]
	[DataRow ("the thumbnail")]
	public void Edit_RefusesToRewriteAValueThatEndsTheDataInsideATableItCompacts (string shape)
	{
		var comment = (0x9286, 7, 4L, 0x64636261L);
		var tiff = shape == "a kept value"
			? new TiffBuilder (38).Header (8).Table (8, [comment, (0x010F, 2, 8, 30)]).ToArray ()
			: new TiffBuilder (50).Header (8).Table (8, [comment, (0x0201, 4, 1, 42), (0x0202, 4, 1, 8)]).ToArray ();
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, shape);
		StringAssert.Contains (save.Error, "a value inside an IFD table");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{shape}: nothing is written");
	}

	// A thumbnail that starts in IFD0's entries and runs past the data is read by no one (exiftool: a bad offset):
	// compacting IFD0 rewrites its first bytes, and the edit saves.
	[TestMethod]
	public void Edit_OfATableHoldingAThumbnailThatRunsPastTheData_Saves ()
	{
		var tiff = new TiffBuilder (50).Header (8).Table (8, [(0x9286, 7, 4, 0x64636261), (0x0201, 4, 1, 42), (0x0202, 4, 1, 20)]).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		AssertOnlyTheCopiesAreGone (tiff, ExifOf (file.Render ().ToArray ()), "a thumbnail past the data", 0x9286);
	}

	// A GPS or interoperability pointer whose first value sits out of line, inside the Exif IFD the edit compacts.
	// Compaction writes the Exif IFD's next offset over that value, so the walk reaches the table at 130 instead of
	// the one at 100. The out-of-line value is not a kept value (a reader follows it, and shows the bytes as a bad
	// value), so only the table offsets catch it. An Exif pointer of count 2 is not followed; a SubIFD array is a
	// kept value and is already refused as one.
	[TestMethod]
	[DataRow (0x8825, false)]
	[DataRow (0xA005, false)]
	[DataRow (0x8825, true)]
	[DataRow (0xA005, true)]
	public void Edit_RefusesWhenAPointerArrayInsideTheCompactedTableNamesAnotherTable (int tag, bool bigEndian)
	{
		const int Exif = 40, Array = 66, Named = 100, Other = 130;
		var craftedTag = bigEndian ? Named >> 16 : Named & 0xFFFF;
		var craftedType = bigEndian ? Named & 0xFFFF : Named >> 16;
		var hill = bigEndian ? 0x48696C6C : 0x6C6C6948;
		var tiff = new TiffBuilder (Other + 18, bigEndian).Header (8)
			.Table (8, [(0x8769, 4, 1, Exif)])
			.Table (Exif, [(0x9286, 7, 4, 0x64636261), (tag, 4, 2, Array), (craftedTag, craftedType, 1, 0)], next: Other)
			.Table (Named, [(0x0001, 2, 4, hill)])
			.Table (Other, [(0x0001, 2, 4, hill)])
			.ToArray ();
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, $"{tag:X4} {(bigEndian ? "big-endian" : "little-endian")}");
		StringAssert.Contains (save.Error, "IFD structure that removing its copy of Comment would change");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), "nothing is written");
	}

	// Two SHORT strip-offset arrays that both fit, in two tables, name more values than half the data holds.
	// Each is the first strip entry of its table, so both are read. The edit is refused and nothing is written.
	[TestMethod]
	[DataRow ("strips")]
	[DataRow ("tiles")]
	[DataRow ("thumb")]
	public void Edit_RefusesWhenSharedValueArraysExceedTheData (string pair)
	{
		var tiff = StripBudgetTiff (pair, 60, 59);
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, pair);
		StringAssert.Contains (save.Error, "more strip, tile or thumbnail values than its data holds");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{pair}: nothing is written");
	}

	// 49 + 49 offset values and one inline length value from each table is 100, exactly half of 200 bytes. A bound
	// that refuses at equality would refuse this, and the comment would stay.
	[TestMethod]
	public void Edit_SavesWhenSharedValueArraysFillTheDataExactly ()
	{
		var tiff = StripBudgetTiff ("strips", 49, 49);
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (ExifOf (file.Render ().ToArray ())).Count);
	}

	// Two tables share one array of a strip pair and name different arrays on the other side. Only the second
	// table's strip covers the UserComment value. Recording that pair on the cache hit keeps the strip; skipping
	// the hit leaves it unprotected, and removing the comment zeroes it. Both byte orders, and either array shared.
	[TestMethod]
	[DataRow ("offsets", false)]
	[DataRow ("offsets", true)]
	[DataRow ("lengths", false)]
	[DataRow ("lengths", true)]
	public void Edit_SavesAStripNamedOnlyByTheSecondTableThatSharesAnArray (string shared, bool bigEndian)
	{
		var tiff = SharedPairTiff (shared, bigEndian);
		var payload = tiff[170..178];
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		var where = $"{shared} {(bigEndian ? "big-endian" : "little-endian")}";
		Assert.IsTrue (save.IsSuccess, $"{where}: {save.Error}");
		var exif = ExifOf (file.Render ().ToArray ());
		Assert.AreEqual (0, UserCommentsInEveryExifIfd (exif).Count, where);
		CollectionAssert.AreEqual (payload, exif[170..178], $"{where}: the second table's strip");
	}

	// exiftool 13.55 reads a type 13 (IFD) entry, so a table whose first remaining entry is type 13 stays
	// readable and the ImageDescription after it stays. A bound that stops at 12 refuses this edit.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_SavesWhenTheFirstKeptEntryHasIfdType (bool bigEndian)
	{
		var hill = bigEndian ? 0x48696C6C : 0x6C6C6948;
		var tiff = new TiffBuilder (80, bigEndian).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x64636261), (0x0001, 13, 1, 0), (0x010E, 2, 4, hill)])
			.ToArray ();
		Assert.AreEqual ("Hill", ExifReaderModel.Exiftool (tiff).Strings ()[0x010E], bigEndian ? "big-endian" : "little-endian");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual ("Hill", ExifReaderModel.Exiftool (ExifOf (file.Render ().ToArray ())).Strings ()[0x010E]);
	}

	// Nothing of the field is in the block. The value-array bound is not a reason to refuse, and the Exif bytes stay.
	[TestMethod]
	public void Edit_OfATitle_SavesWhenValueArraysExceedTheDataAndNothingIsRemoved ()
	{
		var tiff = StripBudgetTiff ("strips", 60, 59);
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Title = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (tiff, ExifOf (file.Render ().ToArray ()));
	}

	// Length 200. IFD0 holds the UserComment and one SHORT offset array; IFD1 holds another. Each length entry is
	// one inline SHORT. 60 + 59 + 2 is past half the data; 49 + 49 + 2 is exactly half.
	static byte[] StripBudgetTiff (string pair, int first, int second)
	{
		var (off, len) = pair switch {
			"tiles" => (0x0144, 0x0145),
			"thumb" => (0x0201, 0x0202),
			_ => (0x0111, 0x0117),
		};
		return new TiffBuilder (200).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x64636261), (off, 3, first, 80), (len, 3, 1, 0)], next: 50)
			.Table (50, [(off, 3, second, 82), (len, 3, 1, 0)])
			.ToArray ();
	}

	// Length 200. The UserComment value is the eight bytes at 170. IFD0's strip ends at 168, so it does not cover
	// them. IFD1 shares one array with IFD0 and its strip is those eight bytes. The arrays are out of line, so a
	// second table hits the value cache.
	static byte[] SharedPairTiff (string shared, bool bigEndian)
	{
		var tiff = new TiffBuilder (200, bigEndian).Header (8);
		if (shared == "offsets") {
			tiff.U32 (100, 160).U32 (104, 160)
				.U16 (108, 4).U16 (110, 4).U16 (112, 4)
				.U16 (114, 20).U16 (116, 20).U16 (118, 20)
				.Table (8, [(0x9286, 7, 8, 170), (0x0111, 4, 2, 100), (0x0117, 3, 3, 108)], next: 50)
				.Table (50, [(0x0111, 4, 2, 100), (0x0117, 3, 3, 114)]);
		} else {
			tiff.U16 (100, 8).U16 (102, 8).U16 (104, 8)
				.U32 (108, 160).U32 (112, 160)
				.U32 (116, 170).U32 (120, 170)
				.Table (8, [(0x9286, 7, 8, 170), (0x0111, 4, 2, 108), (0x0117, 3, 3, 100)], next: 50)
				.Table (50, [(0x0111, 4, 2, 116), (0x0117, 3, 3, 100)]);
		}

		return tiff.Bytes (170, "strips!!"u8).ToArray ();
	}

	// Removing the first entry leaves a format exiftool does not read (0 or 14) in front. Measured with
	// exiftool 13.55 -a -G1 -s -v3: the kept tag (Make, DateTimeOriginal or InteropIndex) is shown before and
	// absent once that format is first, in IFD0, IFD1, the Exif IFD, a SubIFD and an interoperability IFD,
	// both byte orders. The edit is refused and nothing is written. The unchecked edit does drop the tag.
	public static IEnumerable<object[]> FirstEntryLost ()
	{
		foreach (var kind in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop" })
			foreach (var type in new[] { 0, 14 })
				foreach (var big in new[] { false, true })
					yield return new object[] { kind, type, big };
	}

	[TestMethod]
	[DynamicData (nameof (FirstEntryLost))]
	public void Edit_RefusesWhenRemovingTheFirstEntryMakesTheNextOnesFormatUnreadable (string kind, int badType, bool bigEndian)
	{
		var (tiff, tag, text) = TableWhoseFirstEntryChanges (kind, badType, badIsTheComment: false, bigEndian);
		Assert.AreEqual (text, ExifReaderModel.Exiftool (tiff).Strings ()[tag], $"{kind} type {badType}");
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		var order = bigEndian ? "big-endian" : "little-endian";
		Assert.IsFalse (save.IsSuccess, $"{kind} type {badType} {order}");
		StringAssert.Contains (save.Error, "cannot be updated");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{kind}: nothing is written");
		var after = ExifReaderModel.Exiftool (ExifOf (UncheckedComment (input))).Strings ();
		Assert.IsFalse (after.ContainsKey (tag), $"{kind} type {badType}: the unchecked edit drops {text}");
	}

	// The first entry is a UserComment of a format exiftool does not read, so the table is dropped and the tag
	// after it is hidden. Removing that UserComment makes the table readable (type 0 and 19, measured). Refused.
	public static IEnumerable<object[]> FirstEntryGained ()
	{
		foreach (var kind in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop" })
			foreach (var type in new[] { 0, 19 })
				foreach (var big in new[] { false, true })
					yield return new object[] { kind, type, big };
	}

	[TestMethod]
	[DynamicData (nameof (FirstEntryGained))]
	public void Edit_RefusesWhenRemovingABadFirstEntryMakesTheTableReadable (string kind, int badType, bool bigEndian)
	{
		var (tiff, tag, text) = TableWhoseFirstEntryChanges (kind, badType, badIsTheComment: true, bigEndian);
		Assert.IsFalse (ExifReaderModel.Exiftool (tiff).Strings ().ContainsKey (tag), $"{kind} type {badType}: the table is dropped");
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		var order = bigEndian ? "big-endian" : "little-endian";
		Assert.IsFalse (save.IsSuccess, $"{kind} type {badType} {order}");
		StringAssert.Contains (save.Error, "cannot be updated");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{kind}: nothing is written");
		Assert.AreEqual (text, ExifReaderModel.Exiftool (ExifOf (UncheckedComment (input))).Strings ()[tag], $"{kind}: the unchecked edit reveals {text}");
	}

	// A format exiftool skips only when it is not the first entry stays where it is. The comment is removed and
	// DateTimeOriginal stays. Measured: type 14 in the third entry does not drop the Exif IFD.
	[TestMethod]
	[DataRow (0, false)]
	[DataRow (14, false)]
	[DataRow (0, true)]
	[DataRow (14, true)]
	public void Edit_SavesWhenABadFormatEntryStaysOffTheFirstPosition (int badType, bool bigEndian)
	{
		const int TextAt = 300;
		var date = "2009:09:09 09:09:09\0"u8.ToArray ();
		var tiff = new TiffBuilder (340, bigEndian).Header (8)
			.Table (8, [(0x8769, 4, 1, 40)])
			.Table (40, [(0x9003, 2, date.Length, TextAt), (0x9286, 7, SecretUc.Length, 240), (0x0010, badType, 1, 0), (0x9004, 2, date.Length, TextAt)])
			.Bytes (240, SecretUc).Bytes (TextAt, date).ToArray ();
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual ("2009:09:09 09:09:09", ExifReaderModel.Exiftool (ExifOf (file.Render ().ToArray ())).Strings ()[0x9003]);
	}

	// A comment edit does not remove GPS entries. A GPS table exiftool drops for its first entry stays dropped, and
	// one it reads stays read. Measured: GPSLatitudeRef after a type-0 or type-14 entry is absent; in front of a
	// type-14 entry it is "N".
	[TestMethod]
	[DataRow (0)]
	[DataRow (14)]
	public void Edit_KeepsAGpsTableUnreadWhenItsFirstEntryHasAFormatExiftoolDrops (int badType)
	{
		var tiff = new TiffBuilder (80).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x64636261), (0x8825, 4, 1, 40)])
			.Table (40, [(0x0010, badType, 1, 0), (0x0001, 2, 2, 0x4E)])
			.ToArray ();
		Assert.IsFalse (ExifReaderModel.Exiftool (tiff).Summary ().Contains ("G0001", StringComparison.Ordinal), $"type {badType}");
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsFalse (ExifReaderModel.Exiftool (ExifOf (file.Render ().ToArray ())).Summary ().Contains ("G0001", StringComparison.Ordinal), $"type {badType}");
	}

	[TestMethod]
	public void Edit_KeepsAGpsLatitudeRefThatStaysTheFirstEntry ()
	{
		var tiff = new TiffBuilder (80).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x64636261), (0x8825, 4, 1, 40)])
			.Table (40, [(0x0001, 2, 2, 0x4E), (0x0010, 14, 1, 0)])
			.ToArray ();
		Assert.IsTrue (ExifReaderModel.Exiftool (tiff).Summary ().Contains ("G0001=1", StringComparison.Ordinal));
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsTrue (ExifReaderModel.Exiftool (ExifOf (file.Render ().ToArray ())).Summary ().Contains ("G0001=1", StringComparison.Ordinal));
	}

	// IFD0's only entry is a UserComment of type 0, and its next IFD holds Make. exiftool follows a next IFD of an
	// empty table and does not follow one of a table dropped for its first entry (measured, both byte orders).
	// Removing the UserComment would make Make appear. Refused.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_RefusesWhenRemovingTheOnlyBadFirstEntryFollowsTheNextIfd (bool bigEndian)
	{
		const int TextAt = 90;
		var tiff = new TiffBuilder (100, bigEndian).Header (8)
			.Table (8, [(0x9286, 0, SecretUc.Length, 40)], next: 60)
			.Table (60, [(0x010F, 2, 6, TextAt)])
			.Bytes (40, SecretUc).Bytes (TextAt, "Canon\0"u8).ToArray ();
		Assert.IsFalse (ExifReaderModel.Exiftool (tiff).Strings ().ContainsKey (0x010F), bigEndian ? "big-endian" : "little-endian");
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, save.Error);
		StringAssert.Contains (save.Error, "cannot be updated");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), "nothing is written");
		Assert.AreEqual ("Canon", ExifReaderModel.Exiftool (ExifOf (UncheckedComment (input))).Strings ()[0x010F]);
	}

	// UserComment, then a format exiftool does not read, then one tag that kind shows; or the UserComment itself
	// carrying that format, in front of the tag. The strings are what exiftool 13.55 prints for these bytes.
	static (byte[] Tiff, int Tag, string Text) TableWhoseFirstEntryChanges (string kind, int badType, bool badIsTheComment, bool bigEndian)
	{
		const int CommentAt = 240;
		const int TextAt = 300;
		var tag = kind == "Interop" ? 0x0001 : kind == "Exif" ? 0x9003 : 0x010F;
		var text = kind == "Interop" ? "R98" : kind == "Exif" ? "2009:09:09 09:09:09" : "Canon";
		(int Tag, int Type, long Count, long Value) visible = kind switch {
			"Interop" => (0x0001, 2, 4, bigEndian ? 0x52393800L : 0x00383952L),
			"Exif" => (0x9003, 2, 20, TextAt),
			_ => (0x010F, 2, 6, TextAt),
		};
		(int, int, long, long) comment = (0x9286, badIsTheComment ? badType : 7, SecretUc.Length, CommentAt);
		(int, int, long, long)[] entries = badIsTheComment ? [comment, visible] : [comment, (0x0010, badType, 1, 0), visible];
		var tiff = new TiffBuilder (360, bigEndian).Header (8);
		switch (kind) {
			case "IFD0":
				tiff.Table (8, entries);
				break;
			case "IFD1":
				tiff.Table (8, [(0x0100, 4, 1, 1)], next: 40).Table (40, entries);
				break;
			case "SubIFD":
				tiff.Table (8, [(0x014A, 4, 1, 40)]).Table (40, entries);
				break;
			case "Exif":
				tiff.Table (8, [(0x8769, 4, 1, 40)]).Table (40, entries);
				break;
			default:
				tiff.Table (8, [(0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 80)]).Table (80, entries);
				break;
		}

		tiff.Bytes (CommentAt, SecretUc);
		if (kind == "Exif")
			tiff.Bytes (TextAt, "2009:09:09 09:09:09\0"u8);
		else if (kind != "Interop")
			tiff.Bytes (TextAt, "Canon\0"u8);
		return (tiff.ToArray (), tag, text);
	}

	static byte[] UncheckedComment (byte[] input)
	{
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";
		ExifEditor.SkipCheck = true;
		try {
			var save = file.SaveToFile ("/a.png", new MockFileSystem ());
			Assert.IsTrue (save.IsSuccess, save.Error);
			return file.Render ().ToArray ();
		} finally {
			ExifEditor.SkipCheck = false;
		}
	}

	// The next IFD of an Exif, GPS, interoperability or SubIFD table is read by no one. Its thumbnail, strips or
	// tiles lie in the Exif IFD's compacted tail, so those bytes change, and the edit still saves: the pair is not
	// a kept value of a table a reader reads. IFD1's thumbnail is on the main chain and is already refused.
	[TestMethod]
	[DataRow ("Exif", "thumbnail", false)]
	[DataRow ("Exif", "strips", false)]
	[DataRow ("Exif", "tiles", false)]
	[DataRow ("GPS", "thumbnail", false)]
	[DataRow ("GPS", "strips", false)]
	[DataRow ("GPS", "tiles", false)]
	[DataRow ("Interop", "thumbnail", false)]
	[DataRow ("Interop", "strips", false)]
	[DataRow ("Interop", "tiles", false)]
	[DataRow ("SubIFD", "thumbnail", false)]
	[DataRow ("SubIFD", "strips", false)]
	[DataRow ("SubIFD", "tiles", false)]
	[DataRow ("Exif", "thumbnail", true)]
	[DataRow ("Exif", "strips", true)]
	[DataRow ("Exif", "tiles", true)]
	[DataRow ("GPS", "thumbnail", true)]
	[DataRow ("GPS", "strips", true)]
	[DataRow ("GPS", "tiles", true)]
	[DataRow ("Interop", "thumbnail", true)]
	[DataRow ("Interop", "strips", true)]
	[DataRow ("Interop", "tiles", true)]
	[DataRow ("SubIFD", "thumbnail", true)]
	[DataRow ("SubIFD", "strips", true)]
	[DataRow ("SubIFD", "tiles", true)]
	public void Edit_SavesWhenAProtectOnlyTablesDataLiesInTheCompactedTable (string parent, string pair, bool bigEndian)
	{
		var (tiff, exifAt, dataAt, protect, exifNext) = ProtectOnlyData (parent, pair, bigEndian);
		var file = Open (InCarrier ("png", tiff), "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, $"{parent} {pair}: {save.Error}");
		var output = file.Render ().ToArray ();
		var exif = ExifOf (output);
		Assert.AreEqual (1, U16 (exif, exifAt, bigEndian), $"{parent} {pair}: the Exif IFD keeps one entry");
		CollectionAssert.AreEqual (DateTimeOriginalEntry (bigEndian), exif[(exifAt + 2)..(exifAt + 14)], $"{parent} {pair}: DateTimeOriginal");
		CollectionAssert.AreEqual (exifNext, exif[(exifAt + 14)..(exifAt + 18)], $"{parent} {pair}: the Exif IFD's next offset");
		CollectionAssert.AreEqual (new byte[12], exif[dataAt..(dataAt + 12)], $"{parent} {pair}: the protect-only table's data");
		CollectionAssert.AreEqual (tiff[protect..(protect + 30)], exif[protect..(protect + 30)], $"{parent} {pair}: the protect-only table");
		AssertOnlyTheCopiesAreGone (tiff, exif, $"{parent} {pair}", 0x9286, 0x9C9C);
		Assert.AreEqual ("New", Open (output, "a.png").Tag!.Comment, parent);
		var strings = ExifReaderModel.Of (exif).Strings ();
		Assert.AreEqual ("Hill", strings[0x010E], $"{parent} {pair}: ImageDescription");
		Assert.AreEqual ("2024", strings[0x9003], $"{parent} {pair}: DateTimeOriginal");
		Assert.IsFalse (strings.ContainsKey (0x9286), $"{parent} {pair}: UserComment is gone");
	}

	// Compaction of the Exif IFD clears the count of another table. The count is the last two bytes of the Exif
	// IFD's next-offset field, and the entry that follows is one the table's kind shows: ImageDescription "Hill",
	// or GPSLatitudeRef "N" in a GPS table. That entry's tag and type are also the table's next offset, so a check
	// that reads one entry past the edited count sees equal bytes and would save, dropping the tag. The edit is
	// refused, and nothing is written. The unchecked edit does drop the tag.
	[TestMethod]
	[DataRow ("IFD1", false)]
	[DataRow ("GPS", false)]
	[DataRow ("Interop", false)]
	[DataRow ("SubIFD", false)]
	[DataRow ("IFD1", true)]
	[DataRow ("GPS", true)]
	[DataRow ("Interop", true)]
	[DataRow ("SubIFD", true)]
	public void Edit_RefusesWhenCompactionClearsTheCountOfATableThatEndsTheData (string kind, bool bigEndian)
	{
		var tiff = EndingTableWithAVisibleEntry (kind, bigEndian);
		var before = ExifReaderModel.Exiftool (tiff);
		var order = bigEndian ? "big-endian" : "little-endian";
		if (kind == "GPS")
			Assert.IsTrue (before.Summary ().Contains ("G0001=1", StringComparison.Ordinal), $"{kind} {order}");
		else
			Assert.AreEqual ("Hill", before.Strings ()[0x010E], $"{kind} {order}");
		var input = InCarrier ("png", tiff);
		var file = Open (input, "a.png");
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess, $"{kind} {order}");
		StringAssert.Contains (save.Error, "IFD tables that overlap");
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), $"{kind}: nothing is written");
		var after = ExifReaderModel.Exiftool (ExifOf (UncheckedComment (input)));
		if (kind == "GPS")
			Assert.IsFalse (after.Summary ().Contains ("G0001", StringComparison.Ordinal), $"{kind}: the unchecked edit drops GPSLatitudeRef");
		else
			Assert.IsFalse (after.Strings ().ContainsKey (0x010E), $"{kind}: the unchecked edit drops ImageDescription");
	}

	// Exif at 60 (two entries). Its next-offset field is bytes 86-89, and the last two of those are a count of 1
	// at 88. The entry starts at 90, past the bytes compaction clears, and the table's next offset equals that
	// entry's tag and type. Both byte orders. Table() is not used for the ending table: it would overwrite the count.
	static byte[] EndingTableWithAVisibleEntry (string kind, bool bigEndian)
	{
		const int Exif = 60;
		const int Table = 88;
		var exifNext = bigEndian ? 0x00000001L : 0x00010000L;
		byte[] entry;
		long tableNext;
		if (kind == "GPS") {
			entry = bigEndian
				? [0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x4E, 0x00, 0x00, 0x00]
				: [0x01, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00, 0x00, 0x4E, 0x00, 0x00, 0x00];
			tableNext = bigEndian ? 0x00010002L : 0x00020001L;
		} else {
			entry = bigEndian
				? [0x01, 0x0E, 0x00, 0x02, 0x00, 0x00, 0x00, 0x04, 0x48, 0x69, 0x6C, 0x6C]
				: [0x0E, 0x01, 0x02, 0x00, 0x04, 0x00, 0x00, 0x00, 0x48, 0x69, 0x6C, 0x6C];
			tableNext = bigEndian ? 0x010E0002L : 0x0002010EL;
		}

		var t = new TiffBuilder (106, bigEndian).Header (8);
		if (kind == "IFD1")
			t.Table (8, [(0x8769, 4, 1, Exif)], next: Table);
		else {
			var pointer = kind switch { "GPS" => 0x8825, "Interop" => 0xA005, _ => 0x014A };
			t.Table (8, [(0x8769, 4, 1, Exif), (pointer, 4, 1, Table)]);
		}

		return t.Table (Exif, [(0x9286, 7, 4, 0x64636261), (0x9003, 2, 4, bigEndian ? 0x32303234 : 0x34323032)], next: exifNext)
			.Bytes (90, entry).U32 (102, tableNext).ToArray ();
	}

	// ImageDescription "Hill", an Exif IFD whose UserComment is removed, and a protect-only table whose
	// offset/length pair names the twelve bytes compaction clears. The Exif IFD's own next is that table; a GPS,
	// interoperability or SubIFD table's next is that table, and the Exif IFD's next is 0.
	static (byte[] Tiff, int ExifAt, int DataAt, int Protect, byte[] ExifNext) ProtectOnlyData (string parent, string pair, bool bigEndian)
	{
		var (offsetsTag, lengthsTag) = pair switch {
			"strips" => (0x0111, 0x0117),
			"tiles" => (0x0144, 0x0145),
			_ => (0x0201, 0x0202),
		};
		var hill = bigEndian ? 0x48696C6C : 0x6C6C6948;
		var date = bigEndian ? 0x32303234 : 0x34323032;
		var exifNext = parent == "Exif";
		var exifAt = exifNext ? 40 : 60;
		var dataAt = exifNext ? 58 : 78;
		var protect = exifNext ? 80 : 140;
		var t = new TiffBuilder (protect + 30, bigEndian).Header (8);
		if (exifNext)
			t.Table (8, [(0x010E, 2, 4, hill), (0x8769, 4, 1, exifAt)]);
		else {
			var pointer = parent switch { "GPS" => 0x8825, "Interop" => 0xA005, _ => 0x014A };
			t.Table (8, [(0x010E, 2, 4, hill), (0x8769, 4, 1, exifAt), (pointer, 4, 1, 100)]);
			t.Table (100, [(0x0000, 1, 4, 0x00000002)], next: protect);
		}

		t.Table (exifAt, [(0x9286, 7, 4, 0x64636261), (0x9003, 2, 4, date)], next: exifNext ? protect : 0)
			.Table (protect, [(offsetsTag, 4, 1, dataAt), (lengthsTag, 4, 1, 12)]);
		// The Exif IFD's next is 80 when that next is the protect-only table, and 0 when another table's next is.
		byte[] nextBytes = exifNext
			? (bigEndian ? [0x00, 0x00, 0x00, 0x50] : [0x50, 0x00, 0x00, 0x00])
			: [0x00, 0x00, 0x00, 0x00];
		return (t.ToArray (), exifAt, dataAt, protect, nextBytes);
	}

	static byte[] DateTimeOriginalEntry (bool bigEndian) => bigEndian
		? [0x90, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00, 0x04, 0x32, 0x30, 0x32, 0x34]
		: [0x03, 0x90, 0x02, 0x00, 0x04, 0x00, 0x00, 0x00, 0x32, 0x30, 0x32, 0x34];
}
