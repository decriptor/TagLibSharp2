// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Png;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

public partial class PngTextTests
{
	[TestMethod]
	public void Png_EmptyArtistText_DoesNotEraseTheEarlierArtist ()
	{
		var png = Png (Text ("Artist", "Ada"), Text ("Artist", ""));
		var opened = StillImageFile.Read (png);

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual ("Ada", opened.File!.Tag!.Artist);
	}

	[TestMethod]
	public void Png_SignatureOnly_RefusesTheSpliceWithoutThrowing ()
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Title);
		tag.Title = "t";

		var plan = TagLibSharp2.Image.Png.PngRewriter.Plan (PngSignature, tag, out var error);

		Assert.IsNull (plan);
		Assert.AreEqual (TagLibSharp2.Image.Png.PngRewriter.SpliceError, error);
	}

	[TestMethod]
	public void PngClear_KeepsGama_AndDropsALookalikeAncillaryChunk ()
	{
		var cleared = Open (Png (PngChunk ("gAMA", [0, 0, 0, 1]), PngChunk ("aaaS", [1]), Text ("Comment", "hi")), "a.png");
		cleared.Tag!.Clear ();
		var afterClear = cleared.Render ().ToArray ();
		Assert.IsTrue (afterClear.AsSpan ().IndexOf ("gAMA"u8) >= 0);
		Assert.IsTrue (afterClear.AsSpan ().IndexOf ("aaaS"u8) < 0);
	}

	[TestMethod]
	public void Png_DropsAnEmptyIdotWhenAChunkBetweenItAndIdatChanges ()
	{
		var file = Open (Png (PngChunk ("iDOT", []), Text ("Comment", "old")), "b.png");
		file.Tag!.Comment = "Z";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("iDOT"u8) < 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("IDAT"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("old"u8) < 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("Comment"u8) >= 0);
	}

	[TestMethod]
	public void PngWithoutIdat_KeepsIdotWhenTextIsInserted ()
	{
		var file = Open (PngEndingAtIend (PngChunk ("iDOT", [0, 0, 0, 1])), "a.png");
		file.Tag!.Title = "t";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("iDOT"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("Title"u8) >= 0);
	}

	[TestMethod]
	public void PngText_IsInsertedBeforeTheFirstIdat ()
	{
		var file = Open (Png (PngChunk ("IDAT", [1]), PngChunk ("iDOT", [0, 0, 0, 1])), "a.png");
		file.Tag!.Title = "t";
		var output = file.Render ().ToArray ();
		var title = output.AsSpan ().IndexOf ("Title"u8);
		var idat = output.AsSpan ().IndexOf ("IDAT"u8);

		Assert.IsTrue (title >= 0);
		Assert.IsTrue (idat > title);
	}

	[TestMethod]
	public void Png_BadCrcChunk_IsKeptWhenAnotherFieldChanges ()
	{
		var gama = PngChunk ("gAMA", [0, 0, 0, 1]);
		gama[^1] ^= 0xFF;
		var file = Open (Png (gama), "a.png");
		file.Tag!.Title = "t";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("Title"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("gAMA"u8) >= 0);
	}

	[TestMethod]
	public void Png_ExifThatIsNotTiff_NamesTheExifChunk ()
	{
		var data = Png (PngChunk ("eXIf", [1, 2, 3, 4, 0x01, 0x3B, 0, 2, 9]));
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Artist);
		tag.Artist = "N";

		Assert.IsNull (PngRewriter.Plan (data, tag, out var error));
		Assert.AreEqual ("The eXIf block is not a TIFF structure, so its copy of Artist cannot be updated.", error);
	}

	[TestMethod]
	public void Png_CompressedXmpPastTheSharedBudget_NamesOneMebibyte ()
	{
		var data = Png (Xmp (new string (' ', PngText.MaxInflatedText + 1), compressed: true));
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Title);
		tag.Title = "t";

		Assert.IsNull (PngRewriter.Plan (data, tag, out var error));
		Assert.AreEqual (
			"The PNG XML:com.adobe.xmp chunk does not fit in the 1 MiB that all compressed text in the file shares, so its copy of a changed field cannot be updated.",
			error);
	}

	[TestMethod]
	public void PngClear_DropsAChunkWhoseThirdByteMatchesAKeptName ()
	{
		// "xxNS" shares tRNS's last two letters. A clear keeps tRNS and drops this ancillary chunk.
		var png = Png (PngChunk ("gAMA", [0, 0, 0, 1]), PngChunk ("xxNS", [1]));
		var file = Open (png, "a.png");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("gAMA"u8) > 0);
		Assert.AreEqual (-1, output.AsSpan ().IndexOf ("xxNS"u8));
	}

	[TestMethod]
	public void Png_TextWithoutAKeyword_StaysWhenAnotherFieldChanges ()
	{
		var marker = new byte[] { 0xAB, 0xCD, 0xEF, 0x11 };
		var file = Open (Png (PngChunk ("tEXt", marker), Text ("Comment", "old")), "a.png");
		file.Tag!.Comment = "Z";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf (marker) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("old"u8) < 0);
	}

	[TestMethod]
	public void Png_TextWithABadCrc_IsNotAComment ()
	{
		var chunk = Text ("Comment", "Secret");
		chunk[^1] ^= 0xFF;
		var opened = StillImageFile.Read (Png (chunk));

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsNull (opened.File!.Tag!.Comment);
	}

	[TestMethod]
	public void Png_RawXmpThatNamesTheEditedFieldAndIsNotXml_IsRefused ()
	{
		var packet = XmpPacket (Alt ("dc:title", "Old")).Replace ("</rdf:RDF>", "", StringComparison.Ordinal);
		var file = Open (Png (RawProfile ("xmp", Encoding.UTF8.GetBytes (packet))), "a.png");
		file.Tag!.Title = "New";

		var saved = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (saved.IsSuccess, saved.Error);
		StringAssert.Contains (saved.Error, "not well-formed");
	}

	static byte[] PngEndingAtIend (params byte[][] chunks) =>
			Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0]), Concat (chunks), PngChunk ("IEND", []));
}
