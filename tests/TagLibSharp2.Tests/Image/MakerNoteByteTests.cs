// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A removed value is cleared only on bytes no other structure uses. A maker note's value, and a value its entries
/// name outside that blob, stay. The entry itself is still removed.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class MakerNoteByteTests
{
	static readonly byte[] Marker = "ASCII\0\0\0"u8.ToArray ();
	static readonly byte[] Private = "KEEPKEEP"u8.ToArray ();

	[TestMethod]
	[DataRow ("comment", false, true)]
	[DataRow ("comment", true, true)]
	[DataRow ("artist", false, true)]
	[DataRow ("copyright", false, true)]
	[DataRow ("description", false, true)]
	[DataRow ("xpcomment", false, true)]
	[DataRow ("comment", false, false)]
	public void Edit_KeepsBytesAMakerNoteEntryNames (string field, bool bigEndian, bool shared)
	{
		var tiff = NoteTiff (field, bigEndian, entries: 1, shared);
		var file = Open (Png (PngChunk ("eXIf", tiff)), "a.png");
		SetField (file.Tag!, field);
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{field} {(bigEndian ? "BE" : "LE")} shared={shared}: {save.Error}");
		var output = PngPayload (file.Render ().ToArray (), "eXIf");
		var sharedAt = SharedAt (entries: 1, field);
		Assert.AreEqual (0, CountTag (output, FieldTag (field), bigEndian), $"{field}: the entry is removed");
		CollectionAssert.AreEqual (Marker, output[sharedAt..(sharedAt + Marker.Length)], $"{field}: the named bytes stay");
		if (!shared)
			CollectionAssert.AreEqual (new byte[Private.Length], output[(sharedAt + Marker.Length)..(sharedAt + Marker.Length + Private.Length)], $"{field}: an unshared value is cleared");
	}

	// The note's only out-of-line value uses offset 80 counted from the note, so the bytes sit inside the note.
	// The comment sits at absolute offset 80. Reading 80 from the TIFF start would keep the comment.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_NoteRelativeValue_DoesNotKeepAnOutsideComment (bool bigEndian)
	{
		const int commentAt = 80;
		const int noteAt = 200;
		const int relative = 80;
		const int noteLen = 100;
		var marker = "NOTEDATA"u8.ToArray ();
		var comment = "COMMENT!"u8.ToArray ();
		var tiff = new TiffBuilder (noteAt + noteLen, bigEndian).Header (8);
		tiff.Table (8, [(0x8769, 4, 1L, 26L)]);
		tiff.Table (26, [(0x927C, 7, (long)noteLen, (long)noteAt), (0x9286, 7, (long)comment.Length, (long)commentAt)]);
		tiff.Table (noteAt, [(0x0001, 7, (long)marker.Length, (long)relative)]);
		tiff.Bytes (noteAt + relative, marker);
		tiff.Bytes (commentAt, comment);
		var file = Open (Png (PngChunk ("eXIf", tiff.ToArray ())), "a.png");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = PngPayload (file.Render ().ToArray (), "eXIf");
		CollectionAssert.AreEqual (marker, output[(noteAt + relative)..(noteAt + relative + marker.Length)], "the note's own value stays");
		CollectionAssert.AreEqual (new byte[comment.Length], output[commentAt..(commentAt + comment.Length)], "the comment is not the note");
	}

	// exiftool 13.55 reads this Panasonic note's 0x004E as FaceDetInfo. The comment value is those bytes.
	[TestMethod]
	public void Edit_PanasonicFaceInfo_StaysForExiftool ()
	{
		if (!ExiftoolPresent ())
			Assert.Inconclusive ("exiftool is not on PATH.");
		var path = Path.GetTempFileName ();
		try {
			File.WriteAllBytes (path, Jpeg (JpegExif (NoteTiff ("comment", bigEndian: false, entries: 1, shared: true))));
			var before = Exiftool (path);
			var file = Open (File.ReadAllBytes (path), "a.jpg");
			file.Tag!.Comment = "New";
			Assert.IsTrue (file.SaveToFile (path, new MockFileSystem ()).IsSuccess);
			File.WriteAllBytes (path, file.Render ().ToArray ());
			var after = Exiftool (path);
			Assert.AreEqual (Group (before, "Panasonic", "NumFacePositions"), Group (after, "Panasonic", "NumFacePositions"));
			Assert.AreEqual ("21313", Group (after, "Panasonic", "NumFacePositions"));
			Assert.AreEqual (Group (before, "Panasonic", "Face1Position"), Group (after, "Panasonic", "Face1Position"));
			Assert.IsNull (Group (after, "ExifIFD", "UserComment"), after);
			StringAssert.Contains (after, "New");
		} finally {
			File.Delete (path);
		}
	}

	// Sony tag 0x2001 names the preview after EOI. A longer comment would move it, so the edit is refused.
	[TestMethod]
	public void Edit_SampleSony1_RefusesACommentThatWouldMoveThePreview ()
	{
		if (!ExiftoolPresent ())
			Assert.Inconclusive ("exiftool is not on PATH.");
		var sample = CorpusSample ("sample_sony1.jpg");
		var before = ExiftoolVerbose (sample);
		StringAssert.Contains (before, "PreviewImage", before);
		var original = File.ReadAllBytes (sample);
		var file = Open (original, "sample_sony1.jpg");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsFalse (save.IsSuccess);
		StringAssert.Contains (save.Error, ExifEditor.PastData);
		CollectionAssert.AreEqual (original, file.Render ().ToArray ());
	}

	// The real sample: UserComment's 8 bytes are also Panasonic tag 0x004E. Face positions stay, the comment changes.
	[TestMethod]
	public void Edit_SamplePanasonic_KeepsFacePositions ()
	{
		if (!ExiftoolPresent ())
			Assert.Inconclusive ("exiftool is not on PATH.");
		var sample = CorpusSample ("sample_panasonic.jpg");

		var before = Exiftool (sample);
		var edited = Path.GetTempFileName ();
		try {
			var file = Open (File.ReadAllBytes (sample), "sample_panasonic.jpg");
			file.Tag!.Comment = "New";
			var save = file.SaveToFile (edited, new MockFileSystem ());
			Assert.IsTrue (save.IsSuccess, save.Error);
			File.WriteAllBytes (edited, file.Render ().ToArray ());
			var after = Exiftool (edited);
			foreach (var name in new[] { "NumFacePositions", "Face1Position", "Face2Position", "Face3Position", "Face4Position", "Face5Position" })
				Assert.AreEqual (Group (before, "Panasonic", name), Group (after, "Panasonic", name), name);
			Assert.AreEqual ("21313", Group (after, "Panasonic", "NumFacePositions"));
			Assert.IsNull (Group (after, "ExifIFD", "UserComment"), after);
		} finally {
			File.Delete (edited);
		}
	}

	// A version byte, a type past 12, or a spent entry budget means the note was not fully located, so the whole
	// removed value stays. A header of ASCII and NUL (Panasonic, Sony, Canon at 0) is located: only the bytes
	// the entry names stay, and the tail is cleared.
	[TestMethod]
	[DataRow ("olympus", true)]
	[DataRow ("sanyo", true)]
	[DataRow ("epson", true)]
	[DataRow ("nikon", true)]
	[DataRow ("panasonic", false)]
	[DataRow ("sony", false)]
	[DataRow ("canon", false)]
	[DataRow ("flipped", true)]
	public void Edit_UnlocatedMakerNote_LeavesTheValueOutsideTheTables (string kind, bool keepTail)
	{
		var (tiff, at) = VendorNote (kind, tail: true);
		var file = Open (Png (PngChunk ("eXIf", tiff)), "a.png");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{kind}: {save.Error}");
		var output = PngPayload (file.Render ().ToArray (), "eXIf");
		CollectionAssert.AreEqual (Marker, output[at..(at + Marker.Length)], $"{kind}: the named bytes stay");
		var tail = output[(at + Marker.Length)..(at + Marker.Length + Private.Length)];
		if (keepTail)
			CollectionAssert.AreEqual (Private, tail, $"{kind}: a note that was not located leaves the rest");
		else
			CollectionAssert.AreEqual (new byte[Private.Length], tail, $"{kind}: a located note clears bytes it does not name");
	}

	// The overlapping notes of notes2.py are more entries than the block has bytes, so the walk stops.
	[TestMethod]
	public void Edit_WhenMakerNoteEntriesPassTheBlock_LeavesTheValueOutsideTheTables ()
	{
		var tiff = OverlappingMakerNotes (200, bigEndian: false);
		var file = Open (Png (PngChunk ("eXIf", tiff)), "a.png");
		file.Tag!.Description = "x";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = PngPayload (file.Render ().ToArray (), "eXIf");
		CollectionAssert.AreEqual ("old-desc"u8.ToArray (), output[^8..], "a note past the budget is not located, so the description stays");
	}

	// exiftool 13.55 -v3. The line is the maker-note value measured on the unedited file.
	[TestMethod]
	[DataRow ("olympus", "SpecialMode = ASCII")]
	[DataRow ("sanyo", "SpecialMode = ASCII")]
	[DataRow ("epson", "SpecialMode = ASCII")]
	[DataRow ("nikon", "Quality = ASCII")]
	[DataRow ("panasonic", "NumFacePositions = 21313")]
	[DataRow ("sony", "PreviewImage = ASCII")]
	[DataRow ("canon", "41 53 43 49 49 00 00 00")]
	public void Edit_VendorMakerNote_StaysForExiftool (string kind, string line)
	{
		if (!ExiftoolPresent ())
			Assert.Inconclusive ("exiftool is not on PATH.");
		var path = Path.GetTempFileName ();
		try {
			File.WriteAllBytes (path, Jpeg (JpegExif (VendorNote (kind, tail: false).Tiff)));
			var before = ExiftoolVerbose (path);
			StringAssert.Contains (before, line, $"{kind} before\n{before}");
			var file = Open (File.ReadAllBytes (path), "a.jpg");
			file.Tag!.Comment = "New";
			Assert.IsTrue (file.SaveToFile (path, new MockFileSystem ()).IsSuccess);
			File.WriteAllBytes (path, file.Render ().ToArray ());
			var after = ExiftoolVerbose (path);
			StringAssert.Contains (after, line, $"{kind} after\n{after}");
		} finally {
			File.Delete (path);
		}
	}

	// One pass over the maker-note entries: 4x the entries stays under 6x the steps.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_MakerNoteEntries_AreLinearInTheEntries (bool bigEndian)
	{
		const int n = 500;
		long Steps (int entries)
		{
			var tiff = NoteTiff ("comment", bigEndian, entries, shared: true);
			var file = Open (Png (PngChunk ("eXIf", tiff)), "a.png");
			file.Tag!.Comment = "New";
			IfdSteps.Count = 0;
			var save = file.SaveToFile ("/a.png", new MockFileSystem ());
			var steps = IfdSteps.Count;
			Assert.IsTrue (save.IsSuccess, $"{entries}: {save.Error}");
			var output = PngPayload (file.Render ().ToArray (), "eXIf");
			var at = SharedAt (entries, "comment");
			CollectionAssert.AreEqual (Marker, output[at..(at + Marker.Length)], $"{entries}: the named bytes stay");
			return steps;
		}

		var small = Steps (n);
		var large = Steps (4 * n);
		var row = bigEndian ? "BE" : "LE";
		Assert.IsTrue (large <= Math.Max (6 * small, 64), $"{row}: 4x the entries took {large / (double)Math.Max (small, 1):F1}x the steps ({small} -> {large})");
	}

	// T notes name one blob of T entries (notes.py). Each note used to walk that blob again.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_MakerNotesSharingOneBlob_AreLinearInTheNotes (bool bigEndian)
	{
		const int n = 200;
		long Steps (int count)
		{
			var tiff = SharedMakerNotes (count, bigEndian);
			var file = Open (Png (PngChunk ("eXIf", tiff)), "a.png");
			file.Tag!.Description = "x";
			IfdSteps.Count = 0;
			var save = file.SaveToFile ("/a.png", new MockFileSystem ());
			var steps = IfdSteps.Count;
			Assert.IsTrue (save.IsSuccess, $"{count}: {save.Error}");
			var output = PngPayload (file.Render ().ToArray (), "eXIf");
			var at = output.Length - 8;
			CollectionAssert.AreEqual (new byte[8], output[at..], $"{count}: the description value is cleared");
			return steps;
		}

		var small = Steps (n);
		var large = Steps (4 * n);
		var row = bigEndian ? "BE" : "LE";
		Assert.IsTrue (large <= Math.Max (6 * small, 64), $"{row}: 4x the notes took {large / (double)Math.Max (small, 1):F1}x the steps ({small} -> {large})");
	}

	// Note k is the suffix of one IFD starting at entry k (notes2.py). The ranges overlap, so they are not one blob.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Edit_OverlappingMakerNotes_AreLinearInTheNotes (bool bigEndian)
	{
		const int n = 200;
		long Steps (int count)
		{
			var tiff = OverlappingMakerNotes (count, bigEndian);
			var file = Open (Png (PngChunk ("eXIf", tiff)), "a.png");
			file.Tag!.Description = "x";
			IfdSteps.Count = 0;
			var save = file.SaveToFile ("/a.png", new MockFileSystem ());
			var steps = IfdSteps.Count;
			Assert.IsTrue (save.IsSuccess, $"{count}: {save.Error}");
			return steps;
		}

		var small = Steps (n);
		var large = Steps (4 * n);
		var row = bigEndian ? "BE" : "LE";
		Assert.IsTrue (large <= Math.Max (6 * small, 64), $"{row}: 4x the notes took {large / (double)Math.Max (small, 1):F1}x the steps ({small} -> {large})");
	}

	static void SetField (Tag tag, string field)
	{
		switch (field) {
			case "artist": tag.Artist = "New"; break;
			case "copyright": tag.Copyright = "New"; break;
			case "description": tag.Description = "New"; break;
			default: tag.Comment = "New"; break;
		}
	}

	static int FieldTag (string field) => field switch {
		"artist" => 0x013B,
		"copyright" => 0x8298,
		"description" => 0x010E,
		"xpcomment" => 0x9C9C,
		_ => 0x9286,
	};

	// IFD0, an Exif IFD, a Panasonic-shaped maker note of `entries` entries, then the shared 8 bytes.
	static byte[] NoteTiff (string field, bool bigEndian, int entries, bool shared)
	{
		var commentInExif = field is "comment";
		var ifd0Count = commentInExif ? 1 : 2;
		var exifCount = commentInExif ? 2 : 1;
		var ifd0 = 8;
		var exif = ifd0 + 2 + (12 * ifd0Count) + 4;
		var note = exif + 2 + (12 * exifCount) + 4;
		var noteLen = 12 + 2 + (12 * entries) + 4;
		var sharedAt = note + noteLen;
		var valueLen = Marker.Length + (shared ? 0 : Private.Length);
		var length = sharedAt + valueLen;
		var tiff = new TiffBuilder (length, bigEndian).Header (ifd0);
		var exifPointer = (0x8769, 4, 1L, (long)exif);
		var value = (FieldTag (field), field == "xpcomment" ? 1 : field == "comment" ? 7 : 2, (long)valueLen, (long)sharedAt);
		if (commentInExif)
			tiff.Table (ifd0, [exifPointer]);
		else if (FieldTag (field) < 0x8769)
			tiff.Table (ifd0, [value, exifPointer]);
		else
			tiff.Table (ifd0, [exifPointer, value]);

		var notePointer = (0x927C, 7, (long)noteLen, (long)note);
		var userComment = (0x9286, 7, (long)valueLen, (long)sharedAt);
		if (commentInExif)
			tiff.Table (exif, [notePointer, userComment]);
		else
			tiff.Table (exif, [notePointer]);

		var noteEntries = new (int Tag, int Type, long Count, long Value)[entries];
		for (var i = 0; i < entries - 1; i++)
			noteEntries[i] = (i + 1, 3, 1, 0);
		// The note always names the marker. An unshared edit continues the field past those bytes, and only that tail is cleared.
		noteEntries[^1] = (Math.Max (0x004E, entries), 7, Marker.Length, sharedAt);
		tiff.Bytes (note, "Panasonic\0\0\0"u8.ToArray ()).Table (note + 12, noteEntries);
		tiff.Bytes (sharedAt, Marker);
		if (!shared)
			tiff.Bytes (sharedAt + Marker.Length, Private);
		return tiff.ToArray ();
	}

	static int SharedAt (int entries, string field)
	{
		var commentInExif = field is "comment";
		var ifd0Count = commentInExif ? 1 : 2;
		var exifCount = commentInExif ? 2 : 1;
		var exif = 8 + 2 + (12 * ifd0Count) + 4;
		var note = exif + 2 + (12 * exifCount) + 4;
		return note + 12 + 2 + (12 * entries) + 4;
	}

	static int CountTag (byte[] tiff, int tag, bool bigEndian)
	{
		var type = tag == 0x9C9C ? 1 : tag == 0x9286 ? 7 : 2;
		var needle = bigEndian
			? new[] { (byte)(tag >> 8), (byte)tag, (byte)0, (byte)type }
			: new[] { (byte)tag, (byte)(tag >> 8), (byte)type, (byte)0 };
		return Bytes.Count (tiff, needle);
	}

	static string CorpusSample (string name)
	{
		var roots = Environment.GetEnvironmentVariable ("TAGLIBSHARP2_TEST_CORPUS");
		if (!string.IsNullOrEmpty (roots)) {
			foreach (var root in roots.Split (':', StringSplitOptions.RemoveEmptyEntries)) {
				var path = Path.Combine (root, name);
				if (File.Exists (path))
					return path;
				path = Path.Combine (root, "tests", "TaglibSharp.Tests", "samples", name);
				if (File.Exists (path))
					return path;
			}
		}

		Assert.Inconclusive ($"Set TAGLIBSHARP2_TEST_CORPUS to a directory that contains {name}.");
		return "";
	}

	static bool ExiftoolPresent ()
	{
		var path = Environment.GetEnvironmentVariable ("PATH");
		if (string.IsNullOrEmpty (path))
			return false;
		foreach (var dir in path.Split (Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
			if (File.Exists (Path.Combine (dir, "exiftool")))
				return true;
		}

		return false;
	}

	static string Exiftool (string path)
	{
		using var process = Process.Start (new ProcessStartInfo ("exiftool", ["-a", "-G1", "-s", path]) {
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		})!;
		var text = process.StandardOutput.ReadToEnd ();
		process.WaitForExit ();
		Assert.AreEqual (0, process.ExitCode, process.StandardError.ReadToEnd ());
		return text;
	}

	// The reviewer's vendor headers. A tail after the 8 named bytes shows whether the whole value was left alone.
	static (byte[] Tiff, int Shared) VendorNote (string kind, bool tail)
	{
		var header = kind switch {
			"olympus" => "OLYMP\0\u0001\0"u8.ToArray (),
			"sanyo" => "SANYO\0\u0001\0"u8.ToArray (),
			"epson" => "EPSON\0\u0001\0"u8.ToArray (),
			"nikon" => "Nikon\0\u0001\0"u8.ToArray (),
			"panasonic" or "flipped" => "Panasonic\0\0\0"u8.ToArray (),
			"sony" => "SONY DSC \0\0\0"u8.ToArray (),
			"canon" => [],
			_ => throw new ArgumentException (kind),
		};
		var make = kind switch {
			"olympus" => "OLYMPUS\0"u8.ToArray (),
			"sanyo" => "SANYO Electric Co.,Ltd.\0"u8.ToArray (),
			"epson" => "SEIKO EPSON CORP.\0"u8.ToArray (),
			"nikon" => "NIKON\0"u8.ToArray (),
			"panasonic" or "flipped" => "Panasonic\0"u8.ToArray (),
			"sony" => "SONY\0"u8.ToArray (),
			_ => "Canon\0"u8.ToArray (),
		};
		if (make.Length > 16)
			make = [.. make.AsSpan (0, 15).ToArray (), 0];
		var tag = kind switch {
			"nikon" => 0x0003,
			"panasonic" or "flipped" => 0x004E,
			"sony" => 0x2001,
			"canon" => 0x0004,
			_ => 0x0200,
		};
		const int ifd0 = 8;
		var exif = ifd0 + 2 + (2 * 12) + 4;
		var note = exif + 2 + (2 * 12) + 4;
		var noteLen = header.Length + 2 + 12 + 4;
		var shared = note + noteLen;
		var valueLen = Marker.Length + (tail ? Private.Length : 0);
		var makeAt = Math.Max (200, shared + valueLen);
		var length = makeAt + make.Length;
		var tiff = new TiffBuilder (length).Header (ifd0);
		tiff.Table (ifd0, [(0x010F, 2, make.Length, makeAt), (0x8769, 4, 1, exif)]);
		tiff.Table (exif, [(0x927C, 7, noteLen, note), (0x9286, 7, valueLen, shared)]);
		tiff.Bytes (note, header);
		tiff.Entries (note + header.Length, (tag, kind == "flipped" ? 13 : 7, Marker.Length, shared));
		tiff.Bytes (shared, Marker);
		if (tail)
			tiff.Bytes (shared + Marker.Length, Private);
		tiff.Bytes (makeAt, make);
		return (tiff.ToArray (), shared);
	}

	static string ExiftoolVerbose (string path)
	{
		using var process = Process.Start (new ProcessStartInfo ("exiftool", ["-v3", "-a", "-U", path]) {
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		})!;
		var text = process.StandardOutput.ReadToEnd () + process.StandardError.ReadToEnd ();
		process.WaitForExit ();
		Assert.AreEqual (0, process.ExitCode, text);
		return text;
	}

	// IFD0 (ImageDescription, MakerNote) and T one-entry tables, each naming one blob of T inline SHORT entries.
	static byte[] SharedMakerNotes (int count, bool bigEndian)
	{
		const int ifd0 = 8;
		var first = ifd0 + 2 + (2 * 12) + 4;
		var chain = new int[count];
		for (var k = 0; k < count; k++)
			chain[k] = first + (18 * k);
		var blob = count == 0 ? first : chain[count - 1] + 18;
		var blobLen = 2 + (12 * count) + 4;
		var descAt = blob + blobLen;
		var tiff = new TiffBuilder (descAt + 8, bigEndian).Header (ifd0);
		tiff.Table (ifd0, [(0x010E, 2, 8, descAt), (0x927C, 7, blobLen, blob)], count > 0 ? chain[0] : 0);
		for (var k = 0; k < count; k++)
			tiff.Table (chain[k], [(0x927C, 7, blobLen, blob)], k + 1 < count ? chain[k + 1] : 0);
		var entries = new (int Tag, int Type, long Count, long Value)[count];
		for (var n = 0; n < count; n++)
			entries[n] = (n + 1, 3, 1, 0);
		tiff.Table (blob, entries);
		tiff.Bytes (descAt, "old-desc"u8);
		return tiff.ToArray ();
	}

	// One IFD of C LONG entries whose high half is the count of the suffix IFD that starts at the next entry.
	static byte[] OverlappingMakerNotes (int count, bool bigEndian)
	{
		var first = 8 + 2 + (2 * 12) + 4;
		var chain = new int[count];
		for (var k = 0; k < count; k++)
			chain[k] = first + (18 * k);
		var blob = chain[count - 1] + 18;
		var end = blob + 2 + (12 * count) + 4;
		var tiff = new TiffBuilder (end + 8, bigEndian).Header (8);
		tiff.Table (8, [(0x010E, 2, 8, end), (0x927C, 7, end - blob, blob)], chain[0]);
		for (var k = 0; k < count; k++)
			tiff.Table (chain[k], [(0x927C, 7, end - (blob + (12 * k)), blob + (12 * k))], k + 1 < count ? chain[k + 1] : 0);
		var entries = new (int Tag, int Type, long Count, long Value)[count];
		for (var n = 0; n < count; n++) {
			// The suffix count has to land on the first two bytes of the next entry, which is the high half
			// of this value in little-endian and the low half in big-endian.
			var suffix = count - (n + 1);
			entries[n] = (n + 1, 4, 1, bigEndian ? suffix : (long)suffix << 16);
		}
		tiff.Table (blob, entries);
		tiff.Bytes (end, "old-desc"u8);
		return tiff.ToArray ();
	}

	static string? Group (string dump, string group, string name)
	{
		foreach (var line in dump.Split ('\n')) {
			if (!line.StartsWith ($"[{group}]", StringComparison.Ordinal))
				continue;
			var rest = line[(group.Length + 2)..].TrimStart ();
			if (!rest.StartsWith (name, StringComparison.Ordinal))
				continue;
			var colon = rest.IndexOf (':');
			return colon < 0 ? "" : rest[(colon + 1)..].Trim ();
		}

		return null;
	}
}
