// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Preserved ilst children, freeform names, copyright aliases, and cover types.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4PreservationTests
{
	// Reads must stay linear in the number of ilst children, whatever their kind, because the
	// file is untrusted. Each shape is read at n and 8n children: a linear read takes 8 to 14 times
	// as long (cache and GC), a quadratic one about 64 times. The ratio cancels out machine and
	// build speed, and the best of several runs filters out scheduler noise.
	[TestMethod]
	[DoNotParallelize]
	[DataRow ("unknown-then-title")]
	[DataRow ("malformed-title-alternating")]
	[DataRow ("malformed-cover-alternating")]
	[DataRow ("distinct-unknown-types")]
	[DataRow ("distinct-freeforms")]
	[DataRow ("invalid-then-valid-freeforms")]
	public void Read_ManyIlstChildren_ScalesLinearly (string shape)
	{
		const int small = 4000;
		const int factor = 8;
		var smallFile = TestBuilders.Mp4.CreateWithIlstAtoms (IlstShape (shape, small));
		var largeFile = TestBuilders.Mp4.CreateWithIlstAtoms (IlstShape (shape, small * factor));

		// Warm up so JIT time is not charged to the small read.
		Assert.IsTrue (Mp4File.Read (smallFile).IsSuccess);

		var smallTicks = long.MaxValue;
		var largeTicks = long.MaxValue;
		for (var run = 0; run < 7; run++) {
			smallTicks = Math.Min (smallTicks, TimeRead (smallFile));
			largeTicks = Math.Min (largeTicks, TimeRead (largeFile));
		}

		var ratio = (double)largeTicks / Math.Max (1, smallTicks);
		Assert.IsLessThan (factor * 4.0, ratio,
			$"{shape}: {small} children took {smallTicks} ticks, {small * factor} took {largeTicks} ticks (ratio {ratio:F1})");
	}

	[TestMethod]
	public void Freeform_NameWithColon_RoundTripsOnTitleSave ()
	{
		const string colonName = "UFIDhttp://www.cddb.com/id3/taginfo1";
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Old")),
			TestBuilders.Mp4.CreateFreeformAtom ("com.apple.iTunes", colonName, "disc-id"),
			TestBuilders.Mp4.CreateFreeformAtom ("com.apple.iTunes", "ORCHID", "kept"));

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual ("disc-id", file.GetFreeformTag ("com.apple.iTunes", colonName));
		Assert.AreEqual ("kept", file.GetFreeformTag ("com.apple.iTunes", "ORCHID"));

		file.Title = "New";

		var rendered = file.Render (data);
		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New", reread.File!.Title);
		Assert.AreEqual ("disc-id", reread.File.GetFreeformTag ("com.apple.iTunes", colonName));
		Assert.AreEqual ("kept", reread.File.GetFreeformTag ("com.apple.iTunes", "ORCHID"));
		Assert.AreEqual (2, Count (rendered.Span, "----"u8));
	}

	[TestMethod]
	public void Freeform_Set_DropsEmptyPreservedTwin ()
	{
		var valued = TestBuilders.Mp4.CreateFreeformAtom ("com.apple.iTunes", "CUSTOM", "old");
		var empty = FreeformWithoutData ("com.apple.iTunes", "CUSTOM");
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (valued, empty);

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual ("old", file.GetFreeformTag ("com.apple.iTunes", "CUSTOM"));

		file.SetFreeformTag ("com.apple.iTunes", "CUSTOM", "updated");

		var rendered = file.Render (data);
		Assert.AreEqual (1, Count (rendered.Span, "----"u8));
		Assert.AreEqual (1, Count (rendered.Span, "CUSTOM"u8));

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("updated", reread.File!.GetFreeformTag ("com.apple.iTunes", "CUSTOM"));
	}

	[TestMethod]
	public void Copyright_CopyAliasOnly_ReturnsText ()
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©cpy", 1, Encoding.UTF8.GetBytes ("Alias Only")));

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Alias Only", result.File!.Tag!.Copyright);
	}

	[TestMethod]
	public void Copyright_CprtWinsOverCopyAlias ()
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©cpy", 1, Encoding.UTF8.GetBytes ("Alias")),
			TestBuilders.Mp4.CreateIlstChild ("cprt", 1, Encoding.UTF8.GetBytes ("Standard")));

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Standard", result.File!.Tag!.Copyright);
	}

	[TestMethod]
	public void Copyright_Set_WritesCprtAndDropsAlias ()
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©cpy", 1, Encoding.UTF8.GetBytes ("Alias Only")));

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		file.Tag!.Copyright = "New Rights";

		Assert.AreEqual ("New Rights", file.Tag.Copyright);
		var rendered = file.Render (data);
		Assert.AreEqual (0, Count (rendered.Span, CopyType ()));
		Assert.AreEqual (1, Count (rendered.Span, "cprt"u8));

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New Rights", reread.File!.Tag!.Copyright);
	}

	[TestMethod]
	public void Copyright_Clear_RemovesCprtAndAlias ()
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©cpy", 1, Encoding.UTF8.GetBytes ("Alias")),
			TestBuilders.Mp4.CreateIlstChild ("cprt", 1, Encoding.UTF8.GetBytes ("Standard")));

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		file.Tag!.Copyright = null;

		Assert.IsNull (file.Tag.Copyright);
		var rendered = file.Render (data);
		Assert.AreEqual (0, Count (rendered.Span, CopyType ()));
		Assert.AreEqual (0, Count (rendered.Span, "cprt"u8));

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		// An ilst with no children is omitted, so the reread tag itself may be absent.
		Assert.IsNull (reread.File!.Tag?.Copyright);
	}

	[TestMethod]
	public void MalformedName_TitleEdit_WritesOneNameAtom ()
	{
		var parsed = TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("OldTitle"));
		var malformed = TestBuilders.Mp4.CreateBox ("©nam", [0x01, 0x02, 0x03, 0x04]);
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (parsed, malformed);

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual ("OldTitle", file.Title);

		file.Title = "NewTitle";

		var rendered = file.Render (data);
		Assert.AreEqual (1, Count (rendered.Span, NameType ()));

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("NewTitle", reread.File!.Title);
	}

	[TestMethod]
	public void NewBmpPicture_RoundTripsAsBmp ()
	{
		var bmp = new byte[] { (byte)'B', (byte)'M', 0x00, 0x00 };
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		file.AddPicture (new Mp4Picture ("image/bmp", PictureType.FrontCover, "", new BinaryData (bmp)));

		var rendered = file.Render (data);
		Assert.AreEqual (Mp4AtomMapping.TypeBmp, CoverTypeIndicator (rendered.Span));

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.HasCount (1, reread.File!.Pictures);
		Assert.AreEqual ("image/bmp", reread.File.Pictures[0].MimeType);
		CollectionAssert.AreEqual (bmp, reread.File.Pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void NewGifAndUnknownPicture_AreNotWrittenAsPng ()
	{
		var gif = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' };
		var unknown = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var gifFile = Mp4File.Read (data).File!;
		gifFile.AddPicture (new Mp4Picture ("image/gif", PictureType.FrontCover, "", new BinaryData (gif)));
		var gifRendered = gifFile.Render (data);
		Assert.AreEqual (Mp4AtomMapping.TypeGif, CoverTypeIndicator (gifRendered.Span));
		var gifRead = Mp4File.Read (gifRendered.Span);
		Assert.AreEqual ("image/gif", gifRead.File!.Pictures[0].MimeType);

		var other = Mp4File.Read (data).File!;
		other.AddPicture (new Mp4Picture ("image/tiff", PictureType.FrontCover, "", new BinaryData (unknown)));
		var otherRendered = other.Render (data);
		Assert.AreNotEqual (Mp4AtomMapping.TypePng, CoverTypeIndicator (otherRendered.Span));
		var otherRead = Mp4File.Read (otherRendered.Span);
		Assert.AreNotEqual ("image/png", otherRead.File!.Pictures[0].MimeType);
		Assert.AreEqual ("image/unknown", otherRead.File.Pictures[0].MimeType);
	}

	// Bytes that are not recognised fall back to the MIME type. 0 (no type) is used only when
	// neither identifies the image, because ffmpeg does not expose a type-0 cover.
	[TestMethod]
	[DataRow ("image/jpeg", Mp4AtomMapping.TypeJpeg)]
	[DataRow ("image/JPG", Mp4AtomMapping.TypeJpeg)]
	[DataRow ("image/png", Mp4AtomMapping.TypePng)]
	[DataRow ("image/gif", Mp4AtomMapping.TypeGif)]
	[DataRow ("image/bmp", Mp4AtomMapping.TypeBmp)]
	[DataRow ("image/x-ms-bmp", Mp4AtomMapping.TypeBmp)]
	[DataRow ("image/tiff", Mp4AtomMapping.TypeBinary)]
	[DataRow ("", Mp4AtomMapping.TypeBinary)]
	public void NewPicture_UnrecognisedBytes_FallsBackToMimeType (string mimeType, int expected)
	{
		var junk = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (data).File!;
		file.AddPicture (new Mp4Picture (mimeType, PictureType.FrontCover, "", new BinaryData (junk)));

		Assert.AreEqual (expected, CoverTypeIndicator (file.Render (data).Span));
	}

	[TestMethod]
	public void NewPicture_RecognisedBytes_WinOverMimeType ()
	{
		var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (data).File!;
		file.AddPicture (new Mp4Picture ("image/png", PictureType.FrontCover, "", new BinaryData (jpeg)));

		Assert.AreEqual (Mp4AtomMapping.TypeJpeg, CoverTypeIndicator (file.Render (data).Span));
	}

	[TestMethod]
	[DataRow (true, Mp4AtomMapping.TypeJpeg)]
	[DataRow (false, Mp4AtomMapping.TypePng)]
	public void NewPicture_IsJpegConstructor_WritesThatTypeForUnrecognisedBytes (bool isJpeg, int expected)
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (data).File!;
		file.AddPicture (new Mp4Picture ([0x01, 0x02, 0x03, 0x04], isJpeg));

		Assert.AreEqual (expected, CoverTypeIndicator (file.Render (data).Span));
	}

	[TestMethod]
	public void ParsedBmp_TitleEdit_KeepsTypeCode ()
	{
		var bmp = new byte[] { (byte)'B', (byte)'M', 0x11, 0x22 };
		var data = TestBuilders.Mp4.CreateWithCoverArt (bmp, Mp4AtomMapping.TypeBmp);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual ("image/bmp", file.Pictures[0].MimeType);

		file.Title = "Retitled";

		var rendered = file.Render (data);
		Assert.AreEqual (Mp4AtomMapping.TypeBmp, CoverTypeIndicator (rendered.Span));
		var reread = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("image/bmp", reread.File!.Pictures[0].MimeType);
		Assert.AreEqual ("Retitled", reread.File.Title);
	}

	[TestMethod]
	public void Clear_ThenSet_WritesOnlyTheNewFields ()
	{
		const string mean = "com.apple.iTunes";
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateBox ("orch", [0x0A, 0x0B]),
			TestBuilders.Mp4.CreateBox ("©nam", [0x01, 0x02, 0x03, 0x04]),
			TestBuilders.Mp4.CreateIlstChild ("©ART", 1, Encoding.UTF8.GetBytes ("Artist")),
			TestBuilders.Mp4.CreateFreeformAtom (mean, "CUSTOM", "old"),
			TestBuilders.Mp4.CreateFreeformAtom (mean, "OTHER", "gone"),
			FreeformWithoutData (mean, "EMPTYF"));

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;

		file.Tag!.Clear ();
		file.Title = "New";
		file.SetFreeformTag (mean, "CUSTOM", "again");
		file.SetFreeformTag (mean, "EMPTYF", "filled");

		var rendered = file.Render (data);
		Assert.AreEqual (0, Count (rendered.Span, "orch"u8));
		Assert.AreEqual (1, Count (rendered.Span, NameType ()));
		Assert.AreEqual (0, Count (rendered.Span, "Artist"u8));
		Assert.AreEqual (2, Count (rendered.Span, "----"u8));
		Assert.AreEqual (0, Count (rendered.Span, "OTHER"u8));

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New", reread.File!.Title);
		Assert.AreEqual ("again", reread.File.GetFreeformTag (mean, "CUSTOM"));
		Assert.AreEqual ("filled", reread.File.GetFreeformTag (mean, "EMPTYF"));
		Assert.IsNull (reread.File.Tag!.Artist);
	}

	[TestMethod]
	public void MalformedTitle_TitleCleared_LeavesNoTitleAtom ()
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("OldTitle")),
			TestBuilders.Mp4.CreateBox ("©nam", [0x01, 0x02, 0x03, 0x04]),
			TestBuilders.Mp4.CreateIlstChild ("©ART", 1, Encoding.UTF8.GetBytes ("Artist")));

		var file = Mp4File.Read (data).File!;
		file.Title = null;

		var rendered = file.Render (data);
		Assert.AreEqual (0, Count (rendered.Span, NameType ()));
		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.IsNull (reread.File!.Title);
		Assert.AreEqual ("Artist", reread.File.Tag!.Artist);
	}

	[TestMethod]
	public void Freeform_Remove_DropsEmptyPreservedTwin ()
	{
		const string mean = "com.apple.iTunes";
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateFreeformAtom (mean, "CUSTOM", "old"),
			FreeformWithoutData (mean, "CUSTOM"),
			TestBuilders.Mp4.CreateIlstChild ("©ART", 1, Encoding.UTF8.GetBytes ("Artist")));

		var file = Mp4File.Read (data).File!;
		file.SetFreeformTag (mean, "CUSTOM", null);

		var rendered = file.Render (data);
		Assert.AreEqual (0, Count (rendered.Span, "----"u8));
		Assert.AreEqual (0, Count (rendered.Span, "CUSTOM"u8));
	}

	[TestMethod]
	public void MalformedCover_PicturesReplaced_WritesOnlyTheNewCover ()
	{
		var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateBox ("covr", [0x01, 0x02, 0x03, 0x04]),
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("T")));

		var replaced = Mp4File.Read (data).File!;
		Assert.IsEmpty (replaced.Pictures);
		replaced.Tag!.Pictures = [new Mp4Picture (jpeg, isJpeg: true)];
		var rendered = replaced.Render (data);
		Assert.AreEqual (1, Count (rendered.Span, "covr"u8));
		var reread = Mp4File.Read (rendered.Span);
		Assert.HasCount (1, reread.File!.Pictures);
		CollectionAssert.AreEqual (jpeg, reread.File.Pictures[0].PictureData.ToArray ());

		var removed = Mp4File.Read (data).File!;
		removed.Tag!.Pictures = [];
		Assert.AreEqual (0, Count (removed.Render (data).Span, "covr"u8));
	}

	[TestMethod]
	public void MalformedCoverAndInvalidFreeforms_TitleSave_KeptByteForByte ()
	{
		var malformedCover = TestBuilders.Mp4.CreateBox ("covr", [0x01, 0x02, 0x03, 0x04]);
		var noData = FreeformWithoutData ("com.apple.iTunes", "EMPTYF");
		var noName = TestBuilders.Mp4.CreateBox ("----", TestBuilders.Mp4.CreateBox ("data", [0, 0, 0, 1, 0, 0, 0, 0, (byte)'x']));
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Old")),
			malformedCover,
			noData,
			noName);

		var file = Mp4File.Read (data).File!;
		file.Title = "New";

		var rendered = file.Render (data);
		Assert.AreEqual (1, Count (rendered.Span, malformedCover));
		Assert.AreEqual (1, Count (rendered.Span, noData));
		Assert.AreEqual (1, Count (rendered.Span, noName));
		Assert.AreEqual ("New", Mp4File.Read (rendered.Span).File!.Title);
	}

	static long TimeRead (byte[] file)
	{
		GC.Collect ();
		GC.WaitForPendingFinalizers ();
		var watch = Stopwatch.StartNew ();
		var result = Mp4File.Read (file);
		watch.Stop ();
		Assert.IsTrue (result.IsSuccess, result.Error);
		return watch.ElapsedTicks;
	}

	static byte[][] IlstShape (string shape, int count)
	{
		var atoms = new byte[count][];
		var half = count / 2;
		var title = TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("T"));
		var malformedTitle = TestBuilders.Mp4.CreateBox ("©nam", [0x01, 0x02, 0x03, 0x04]);
		var cover = TestBuilders.Mp4.CreateCovrAtom ([0xFF, 0xD8, 0xFF], Mp4AtomMapping.TypeJpeg);
		var malformedCover = TestBuilders.Mp4.CreateBox ("covr", [0x01, 0x02, 0x03, 0x04]);
		for (var i = 0; i < count; i++) {
			atoms[i] = shape switch {
				"unknown-then-title" => i < half ? TestBuilders.Mp4.CreateBox ("orch", new byte[16]) : title,
				"malformed-title-alternating" => i % 2 == 0 ? malformedTitle : title,
				"malformed-cover-alternating" => i % 2 == 0 ? malformedCover : cover,
				"distinct-unknown-types" => TestBuilders.Mp4.CreateBox (DistinctType (i), new byte[16]),
				"distinct-freeforms" => TestBuilders.Mp4.CreateFreeformAtom ("com.example", $"N{i}", "v"),
				"invalid-then-valid-freeforms" => i < half
					? FreeformWithoutData ("com.example", $"E{i}")
					: TestBuilders.Mp4.CreateFreeformAtom ("com.example", $"N{i}", "v"),
				_ => throw new ArgumentOutOfRangeException (nameof (shape), shape, null)
			};
		}

		return atoms;
	}

	// Four Latin-1 characters, none of them a type the parser knows.
	static string DistinctType (int i) => new ([
		(char)('z' - (i / 17576 % 26)),
		(char)('a' + (i / 676 % 26)),
		(char)('a' + (i / 26 % 26)),
		(char)('a' + (i % 26))]);

	static byte[] FreeformWithoutData (string mean, string name)
	{
		using var meanContent = new BinaryDataBuilder ();
		meanContent.AddUInt32BE (0);
		meanContent.Add (Encoding.UTF8.GetBytes (mean));

		using var nameContent = new BinaryDataBuilder ();
		nameContent.AddUInt32BE (0);
		nameContent.Add (Encoding.UTF8.GetBytes (name));

		using var body = new BinaryDataBuilder ();
		body.Add (TestBuilders.Mp4.CreateBox ("mean", meanContent.ToArray ()));
		body.Add (TestBuilders.Mp4.CreateBox ("name", nameContent.ToArray ()));
		return TestBuilders.Mp4.CreateBox ("----", body.ToArray ());
	}

	static byte[] CopyType () => [0xA9, (byte)'c', (byte)'p', (byte)'y'];

	static byte[] NameType () => [0xA9, (byte)'n', (byte)'a', (byte)'m'];

	static int Count (ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
	{
		var count = 0;
		var start = 0;
		while (start < data.Length) {
			var at = data.Slice (start).IndexOf (pattern);
			if (at < 0)
				break;
			count++;
			start += at + pattern.Length;
		}

		return count;
	}

	static int CoverTypeIndicator (ReadOnlySpan<byte> file)
	{
		var covr = file.IndexOf ("covr"u8);
		Assert.IsTrue (covr >= 4, "covr missing");
		var size = (file[covr - 4] << 24) | (file[covr - 3] << 16) | (file[covr - 2] << 8) | file[covr - 1];
		var box = file.Slice (covr - 4, size);
		var data = box.IndexOf ("data"u8);
		Assert.IsTrue (data >= 4, "covr data atom missing");
		return file[covr - 4 + data + 7];
	}
}
