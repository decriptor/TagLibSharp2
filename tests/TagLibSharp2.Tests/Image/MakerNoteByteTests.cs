// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
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
			return;
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

	// The real sample: UserComment's 8 bytes are also Panasonic tag 0x004E. Face positions stay, the comment changes.
	[TestMethod]
	public void Edit_SamplePanasonic_KeepsFacePositions ()
	{
		var sample = PanasonicSample ();
		if (sample is null || !ExiftoolPresent ())
			return;

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
		var needle = bigEndian
			? new[] { (byte)(tag >> 8), (byte)tag, (byte)0, (byte)(tag == 0x9C9C ? 1 : tag == 0x9286 ? 7 : 2) }
			: new[] { (byte)tag, (byte)(tag >> 8), (byte)(tag == 0x9C9C ? 1 : tag == 0x9286 ? 7 : 2), (byte)0 };
		var count = 0;
		for (var i = 0; i + 4 <= tiff.Length; i++) {
			if (tiff[i] == needle[0] && tiff[i + 1] == needle[1] && tiff[i + 2] == needle[2] && tiff[i + 3] == needle[3])
				count++;
		}

		return count;
	}

	static string? PanasonicSample ()
	{
		var direct = "/Users/sshaw/code/decriptor/taglib-sharp/tests/TaglibSharp.Tests/samples/sample_panasonic.jpg";
		if (File.Exists (direct))
			return direct;
		var roots = Environment.GetEnvironmentVariable ("TAGLIBSHARP2_TEST_CORPUS");
		if (string.IsNullOrEmpty (roots))
			return null;
		foreach (var root in roots.Split (':', StringSplitOptions.RemoveEmptyEntries)) {
			var path = Path.Combine (root, "sample_panasonic.jpg");
			if (File.Exists (path))
				return path;
			path = Path.Combine (root, "tests", "TaglibSharp.Tests", "samples", "sample_panasonic.jpg");
			if (File.Exists (path))
				return path;
		}

		return null;
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
