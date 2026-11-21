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
	[TestMethod]
	[DoNotParallelize]
	public void Read_ThousandsOfUnknownIlstChildren_FinishesQuickly ()
	{
		// Unknown children are preserved. Each later ©nam used to scan that whole list.
		const int unknownCount = 8000;
		const int knownCount = 30000;
		var payload = new byte[120];
		var unknown = TestBuilders.Mp4.CreateBox ("orch", payload);
		var title = TestBuilders.Mp4.CreateIlstChild ("©nam", typeIndicator: 1, Encoding.UTF8.GetBytes ("T"));
		var atoms = new byte[unknownCount + knownCount][];
		for (var i = 0; i < unknownCount; i++)
			atoms[i] = unknown;
		for (var i = 0; i < knownCount; i++)
			atoms[unknownCount + i] = title;

		var data = TestBuilders.Mp4.CreateWithIlstAtoms (atoms);
		Assert.IsGreaterThan (1_000_000, data.Length);

		var watch = Stopwatch.StartNew ();
		var result = Mp4File.Read (data);
		watch.Stop ();

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("T", result.File!.Title);
		Assert.IsLessThan (2000L, watch.ElapsedMilliseconds, $"read took {watch.ElapsedMilliseconds} ms");
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
