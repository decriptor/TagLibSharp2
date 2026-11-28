// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>What a save keeps: every byte it does not edit, the bytes after the image, byte identity of a no-op save, and refusals with their own reason.</summary>
[TestClass]
[TestCategory ("Unit")]
public partial class ImageSaveContractTests
{
	[TestMethod]
	public void SaveToFile_WhenTheContainerCannotBeUpdated_FailsAndDoesNotWrite ()
	{
		var data = Concat (PngSignature, PngChunk ("IHDR", PngHeader (1, 1, 8, 0)), Text ("Title", "Door"));
		var fs = new MockFileSystem ();
		fs.AddFile ("/photo.png", data);
		var file = Open (data, "photo.png");
		file.Tag!.Title = "Other";
		var wrote = file.SaveToFile ("/photo.png", fs);
		Assert.IsFalse (wrote.IsSuccess, wrote.Error);
		CollectionAssert.AreEqual (data, fs.ReadAllBytes ("/photo.png"));
		CollectionAssert.AreEqual (data, file.Render ().ToArray (), "Render returns the unchanged file");
		Assert.AreEqual ("Other", file.Tag.Title);

		var asyncWrote = file.SaveToFileAsync ("/photo.png", fs).GetAwaiter ().GetResult ();
		Assert.IsFalse (asyncWrote.IsSuccess, asyncWrote.Error);
		CollectionAssert.AreEqual (data, fs.ReadAllBytes ("/photo.png"));
	}

	[TestMethod]
	public void FormatsWithoutATextStore_KeepTheOriginalBytes ()
	{
		AssertNoStore (Bmp (4, 2), "a.bmp");
		AssertNoStore (Pcx (0, 0, 1, 1, planes: 1), "a.pcx");
		AssertNoStore (Encoding.ASCII.GetBytes ("P4\n1 1\n"), "a.pbm");
		AssertNoStore (TiffImage (2, 3), "a.tif");
		AssertNoStore (TiffImage (2, 3), "a.dng");
		AssertNoStore (Encoding.UTF8.GetBytes ("<svg width=\"2\" height=\"3\"></svg>"), "a.svg");
	}

	static void AssertNoStore (byte[] data, string name)
	{
		var file = Open (data, name);
		Assert.IsNull (file.Tag);
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());
	}

	static readonly (string Name, byte[] Bytes)[] Trailers = [
		("zip", Concat ("PK\u0003\u0004"u8.ToArray (), "appended-archive-bytes......"u8.ToArray ())),
		("text", "TAIL"u8.ToArray ()),
		("zeros", new byte[100]),
		("one", [0x00]),
		("chunk-shaped", PngChunk ("tEXt", Concat ("Title"u8.ToArray (), [0], "after IEND"u8.ToArray ()))),
	];

	static readonly (string Name, Func<byte[]> Build, string File)[] Images = [
		("png", () => Png (Text ("Title", "Old"), Text ("Comment", "Old")), "a.png"),
		("gif", () => Gif (GifComment ("Old")), "a.gif"),
		("jpeg", () => Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ())), "a.jpg"),
	];

	static readonly (string Name, Action<Tag> Apply)[] Edits = [
		("title", t => t.Title = "New"),
		("comment", t => t.Comment = "New"),
		("null", t => t.Comment = null),
		("clear", t => t.Clear ()),
	];

	public static IEnumerable<object[]> TrailerCases () =>
		from image in Images
		from trailer in Trailers
		from edit in Edits
		select new object[] { image.Name, trailer.Name, edit.Name };

	[TestMethod]
	[DynamicData (nameof (TrailerCases))]
	public void Save_KeepsTheBytesAfterTheImage (string image, string trailer, string edit)
	{
		var (_, build, name) = Images.Single (i => i.Name == image);
		var tail = Trailers.Single (t => t.Name == trailer).Bytes;
		var input = Concat (build (), tail);
		var file = Open (input, name);
		Edits.Single (e => e.Name == edit).Apply (file.Tag!);

		var output = file.Render ().ToArray ();

		CollectionAssert.AreEqual (tail, output.AsSpan (output.Length - tail.Length).ToArray (), $"{image} {edit} lost the {trailer} trailer");
		var reread = Open (output, name);
		if (edit == "clear" || edit == "null")
			Assert.IsNull (reread.Tag!.Comment);
		else if (edit == "comment")
			Assert.AreEqual ("New", reread.Tag!.Comment);
		if (image == "png" && edit == "title")
			Assert.AreEqual ("New", reread.Tag!.Title);
		Assert.IsTrue (file.SaveToFile ("/out" + name, new MockFileSystem ()).IsSuccess);
	}

	[TestMethod]
	public void Png_AChunkAfterIend_IsNotReadOrEdited ()
	{
		var after = Text ("Title", "after IEND");
		var input = Concat (Png (Text ("Title", "Before")), after);
		var file = Open (input, "a.png");
		Assert.AreEqual ("Before", file.Tag!.Title);

		file.Tag.Clear ();
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().EndsWith (after));
		Assert.IsNull (Open (output, "a.png").Tag!.Title);
	}

	[TestMethod]
	public void Png_WithoutIend_RefusesTheSaveWithItsOwnReason ()
	{
		var png = Png (Text ("Title", "Old"));
		var truncated = png.AsSpan (0, png.Length - 12).ToArray ();
		var file = Open (truncated, "a.png");
		file.Tag!.Title = "New";

		var result = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "IEND");
		CollectionAssert.AreEqual (truncated, file.Render ().ToArray ());
	}

	[TestMethod]
	[DynamicData (nameof (ImageClearTests.ClearCases), typeof (ImageClearTests))]
	public void FieldEdit_KeepsEveryOtherBlockByteForByte (string format, string carrier, byte[] input, byte[] block, bool dropped)
	{
		_ = dropped;
		var name = "a." + (format == "jpeg" ? "jpg" : format);
		var file = Open (input, name);
		// PNG Year has no copy in XMP or Exif (a Comment edit also changes Exif UserComment); GIF and JPEG store
		// only the comment.
		if (format == "png")
			file.Tag!.Year = "2024";
		else
			file.Tag!.Comment = "edited";

		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf (block) >= 0, $"{format} {carrier} lost on a field edit");
		var reread = Open (output, name);
		Assert.AreEqual (format == "png" ? "2024" : "edited", format == "png" ? reread.Tag!.Year : reread.Tag!.Comment);
	}

	[TestMethod]
	public void NoOpSave_IsByteIdentical_ForEveryRewriter ()
	{
		foreach (var (name, build, file) in Images) {
			var input = Concat (build (), "TAIL"u8.ToArray ());
			var image = Open (input, file);
			image.Tag!.Comment = image.Tag.Comment;
			image.Tag.Title = image.Tag.Title;
			CollectionAssert.AreEqual (input, image.Render ().ToArray (), name);
		}
	}

	// A container whose blocks cannot be walked refuses the save, and the reason names the format: a GIF block with
	// an unknown introducer, and a JPEG whose marker list runs into bytes that are not a marker.
	[TestMethod]
	[DataRow ("gif", false)]
	[DataRow ("jpeg", true)]
	public void UnspliceableSave_NamesTheFormat (string format, bool saveAsync)
	{
		var gif = Gif (GifComment ("c"));
		gif[^1] = 0x99;
		var jpeg = Concat ([0xFF, 0xD8], JpegSegment (0xFE, "c"u8.ToArray ()), JpegSegment (0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]), [0x00, 0x01]);
		var name = format == "gif" ? "a.gif" : "a.jpg";
		var file = Open (format == "gif" ? gif : jpeg, name);
		file.Tag!.Comment = "x";

		var result = saveAsync ? file.SaveToFileAsync ("/" + name, new MockFileSystem ()).GetAwaiter ().GetResult () : file.SaveToFile ("/" + name, new MockFileSystem ());

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, format.ToUpperInvariant ());
	}

	static byte[] IdotPng (params byte[][] between) =>
		Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0]), Text ("Title", "Old"),
			PngChunk ("iDOT", new byte[28]), Concat (between), PngChunk ("IDAT", [0x78, 0x9C, 0x03, 0x00]), PngChunk ("IEND", []));

	[TestMethod]
	public void Png_AppleIdot_IsKeptOnlyWhileItStillPointsAtTheImageData ()
	{
		// New text goes before iDOT, so the distance from iDOT to IDAT is unchanged and iDOT stays.
		var file = Open (IdotPng (), "a.png");
		file.Tag!.Title = "New";
		Assert.IsTrue (PngChunks (file.Render ().ToArray ()).Exists (c => c.Type == "iDOT"));

		// A changed chunk between iDOT and IDAT would leave iDOT pointing at the wrong place, so iDOT goes.
		var between = Open (IdotPng (Text ("Comment", "Old")), "a.png");
		between.Tag!.Comment = "A longer comment";
		var output = between.Render ().ToArray ();
		Assert.IsFalse (PngChunks (output).Exists (c => c.Type == "iDOT"));
		Assert.AreEqual ("A longer comment", Open (output, "a.png").Tag!.Comment);
	}
}
