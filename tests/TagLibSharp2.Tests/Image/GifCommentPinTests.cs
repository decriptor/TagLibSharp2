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

public partial class GifCommentTests
{
	[TestMethod]
	public void Gif_GlobalColorTable_StaysAheadOfTheComment_AndACharacterIsNotSplit ()
	{
		var gif = GifWithTable ();
		var file = Open (gif, "a.gif");
		file.Tag!.Comment = new string ('a', 254) + "é";
		var saved = file.SaveToFile ("/a.gif", new MockFileSystem ());
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var output = file.Render ().ToArray ();

		CollectionAssert.AreEqual (gif.AsSpan (13, 6).ToArray (), output.AsSpan (13, 6).ToArray ());
		var comment = GifCommentBytes (output);
		Assert.AreEqual (254, comment[2]);
		Assert.AreEqual (2, comment[2 + 1 + 254]);
		Assert.AreEqual (0xC3, comment[2 + 1 + 254 + 1]);
		Assert.AreEqual (0xA9, comment[2 + 1 + 254 + 2]);
	}

	static byte[] GifWithTable ()
	{
		var header = new byte[19];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		header[10] = 0x80;
		for (var i = 0; i < 6; i++)
			header[13 + i] = (byte)(10 + i);
		return Concat (header, [0x3B]);
	}

	static byte[] GifCommentBytes (byte[] gif)
	{
		var at = gif.AsSpan ().IndexOf ((byte)0x21);
		Assert.IsTrue (at > 0);
		return gif[at..];
	}

	[TestMethod]
	public void Gif87aClear_KeepsThe87aSignature ()
	{
		var old = new byte[14];
		"GIF87a"u8.CopyTo (old);
		old[6] = 1;
		old[8] = 1;
		old[13] = 0x3B;
		var file = Open (old, "a.gif");
		file.Tag!.Clear ();

		CollectionAssert.AreEqual ("GIF87a"u8.ToArray (), file.Render ().Span.Slice (0, 6).ToArray ());
	}

	[TestMethod]
	public void Gif_NetscapeIdOnADifferentLabelOrBlockLength_IsDroppedOnClear ()
	{
		var wrongLabel = GifWithExtension (ApplicationWithSize (0x99, 11, "NETSCAPE2.0"));
		var wrongSize = GifWithExtension (ApplicationWithSize (0xFF, 12, "NETSCAPE2.0" + "X"));
		var labelFile = Open (wrongLabel, "a.gif");
		var sizeFile = Open (wrongSize, "b.gif");
		labelFile.Tag!.Clear ();
		sizeFile.Tag!.Clear ();

		CollectionAssert.AreEqual (GifTrailerOnly (), labelFile.Render ().Span.ToArray ());
		CollectionAssert.AreEqual (GifTrailerOnly (), sizeFile.Render ().Span.ToArray ());
	}

	[TestMethod]
	public void Gif87a_GainsThe89aSignature_AndALocalColorTableStaysWithItsImage ()
	{
		var old = new byte[14];
		"GIF87a"u8.CopyTo (old);
		old[6] = 1;
		old[8] = 1;
		old[13] = 0x3B;
		var upgraded = Open (old, "a.gif");
		upgraded.Tag!.Comment = "Z";
		CollectionAssert.AreEqual ("GIF89a"u8.ToArray (), upgraded.Render ().Span.Slice (0, 6).ToArray ());

		var gif = GifWithLocalTable ();
		var file = Open (gif, "b.gif");
		file.Tag!.Comment = "Z";
		var output = file.Render ().ToArray ();
		var image = output.AsSpan ().IndexOf ((byte)0x2C);
		Assert.IsTrue (image > 0);
		CollectionAssert.AreEqual (new byte[] { 0x21, 0xFE, 1, (byte)'Z', 0 }, output.AsSpan (13, 5).ToArray ());
		CollectionAssert.AreEqual (new byte[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }, output.AsSpan (image + 10, 6).ToArray ());
	}

	[TestMethod]
	public void Gif_UnknownIntroducer_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var data = GifWithExtension ([0x2A, 0x00, 0x00]);

		Assert.IsNull (GifRewriter.Plan (data, tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void GifCommentEdit_KeepsAnApplicationBlockThatMentionsUserComment ()
	{
		var block = new byte[3 + 11 + 1 + 11 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		"XXXXXXXXXXX"u8.CopyTo (block.AsSpan (3));
		block[14] = 11;
		"UserComment"u8.CopyTo (block.AsSpan (15));
		var data = GifWithExtension (block);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		var plan = GifRewriter.Plan (data, tag, out var error);

		Assert.IsNotNull (plan, error);
		var output = plan!.Materialize (data);
		CollectionAssert.AreEqual ("XXXXXXXXXXX"u8.ToArray (), output.AsSpan (13 + 5 + 3, 11).ToArray ());
	}

	[TestMethod]
	public void Gif_ShortImageDescriptor_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;

		Assert.IsNull (GifRewriter.Plan (Concat (header, [0x2C, 0, 0, 0, 0]), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_ImageColorTableCutOffBeforeTheTrailer_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var descriptor = new byte[11];
		descriptor[0] = 0x2C;
		descriptor[9] = 0x80;
		descriptor[10] = 0x3B;

		Assert.IsNull (GifRewriter.Plan (Concat (header, descriptor), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_SubBlockLongerThanTheRestOfTheFile_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		Assert.IsNull (GifRewriter.Plan (GifWithExtension ([0x21, 0xFE, 5, 0x3B]), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_CommentThenACutOffImage_DoesNotYieldTheComment ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var data = Concat (header, [0x21, 0xFE, 2, (byte)'H', (byte)'i', 0, 0x2C, 0, 0]);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read (data, tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_CommentThatEndsTheFileWithoutATrailer_IsNotRead ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var data = Concat (header, [0x21, 0xFE, 2, (byte)'H', (byte)'i', 0]);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read (data, tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_ReadOfANonGif_KeepsACommentAlreadySet ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read ([1, 2, 3], tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_TenByteSignature_NamesTheTruncatedHeader ()
	{
		var data = new byte[10];
		"GIF89a"u8.CopyTo (data);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		Assert.IsNull (GifRewriter.Plan (data, tag, out var error));
		Assert.AreEqual (GifRewriter.HeaderError, error);
	}

	[TestMethod]
	public void Gif_SubBlockThatFillsTheFileWithoutATerminator_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;

		Assert.IsNull (GifRewriter.Plan (Concat (header, [0x21, 0xFE, 1, (byte)'A']), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_CommentSubBlockCutOff_KeepsACommentAlreadySet ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read (Concat (header, [0x21, 0xFE, 5, (byte)'A']), tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_CommentLongerThanOneSubBlock_SplitsAt255 ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var data = Concat (header, [0x3B]);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = new string ('A', 300);

		var plan = GifRewriter.Plan (data, tag, out var error);
		Assert.IsNotNull (plan, error);
		var output = plan!.Materialize (data);

		Assert.AreEqual (0x21, output[13]);
		Assert.AreEqual (0xFE, output[14]);
		Assert.AreEqual (255, output[15]);
		Assert.AreEqual ((byte)'A', output[16]);
		Assert.AreEqual (45, output[16 + 255]);
	}

	// A packet whose UTF-8 length is a multiple of 255 fills whole sub-blocks. The extension still ends on one
	// zero; a second zero is the next block and stops the reader before the trailer.
	[TestMethod]
	public void Gif_XmpSubBlockPacketFillingWholeBlocks_EndsTheExtensionOnce ()
	{
		var packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>"
			+ "<rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:UserComment><rdf:Alt>"
			+ "<rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></exif:UserComment></rdf:Description></rdf:RDF></x:xmpmeta>";
		var data = GifWithExtension (GifSubBlockXmp (packet));
		string? comment = null;
		byte[]? output = null;
		for (var n = 1; n <= 255 && output is null; n++) {
			comment = new string ('B', n);
			var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
			tag.Comment = comment;
			var plan = GifRewriter.Plan (data, tag, out var error);
			Assert.IsNotNull (plan, error);
			var rendered = plan!.Materialize (data);
			if (XmpSubBlockPayload (rendered) % 255 == 0)
				output = rendered;
		}

		Assert.IsNotNull (output);
		var read = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		GifRewriter.Read (output, read);
		Assert.AreEqual (comment, read.Comment);
	}

	[TestMethod]
	public void Gif_XmpPacketThatIsNotXml_RefusesTheCommentEdit ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		Assert.IsNull (GifRewriter.Plan (GifWithExtension (GifXmp ("<UserComment")), tag, out var error));
		Assert.IsNotNull (error);
		StringAssert.Contains (error, "GIF XMP packet");
		StringAssert.Contains (error, "not well-formed XML");
	}

	[TestMethod]
	public void Gif_TruncatedHeaderAndAnUnlocatableXmpComment_NameThemselves ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		Assert.IsNull (GifRewriter.Plan (new byte[10], tag, out var headerError));
		Assert.AreEqual (GifRewriter.HeaderError, headerError);

		var xmp = GifWithExtension (XmpUserCommentBlock ());
		Assert.IsNull (GifRewriter.Plan (xmp, tag, out var xmpError));
		Assert.AreEqual (
			"The GIF XMP extension is neither the XMP raw form with its magic trailer nor data sub-blocks, and holds a UserComment, so that copy of Comment cannot be located and updated.",
			xmpError);
	}

	[TestMethod]
	public void GifClear_KeepsTheLoopExtension_AndDropsAnUnknownApplicationExtension ()
	{
		var gif = GifWithExtension (Application ("XXXXXXXXXXX"), Application ("NETSCAPE2.0"));
		var file = Open (gif, "a.gif");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("NETSCAPE2.0"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("XXXXXXXXXXX"u8) < 0);
	}

	static byte[] GifWithLocalTable ()
	{
		var data = new byte[32];
		"GIF89a"u8.CopyTo (data);
		data[6] = 1;
		data[8] = 1;
		data[13] = 0x2C;
		data[18] = 1;
		data[20] = 1;
		data[22] = 0x80;
		for (var i = 0; i < 6; i++)
			data[23 + i] = (byte)(0xA0 + i);
		data[29] = 2;
		data[31] = 0x3B;
		return data;
	}

	static int XmpSubBlockPayload (byte[] data)
	{
		var at = data.AsSpan ().IndexOf ("XMP DataXMP"u8);
		Assert.IsTrue (at >= 0);
		var i = at + 11;
		var payload = 0;
		while (i < data.Length && data[i] != 0) {
			var size = data[i];
			payload += size;
			i += 1 + size;
		}

		return payload;
	}

	static byte[] GifWithExtension (params byte[][] blocks)
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		return Concat (header, Concat (blocks), [0x3B]);
	}

	static byte[] Application (string id)
	{
		var block = new byte[3 + 11 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		Encoding.ASCII.GetBytes (id).CopyTo (block, 3);
		return block;
	}

	/// <summary>An extension whose first sub-block is <paramref name="payload"/>, then a zero terminator.</summary>
	static byte[] ApplicationWithSize (byte label, int size, string payload)
	{
		var block = new byte[3 + size + 1];
		block[0] = 0x21;
		block[1] = label;
		block[2] = (byte)size;
		Encoding.ASCII.GetBytes (payload).CopyTo (block, 3);
		return block;
	}

	static byte[] GifTrailerOnly ()
	{
		var data = new byte[14];
		"GIF89a"u8.CopyTo (data);
		data[6] = 1;
		data[8] = 1;
		data[13] = 0x3B;
		return data;
	}

	static byte[] XmpUserCommentBlock ()
	{
		// A real application extension: the 11-byte id, then one data sub-block, then the terminator.
		// The packet is not sub-block XML and has no magic trailer, and it names UserComment.
		var block = new byte[3 + 11 + 1 + 11 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		"XMP DataXMP"u8.CopyTo (block.AsSpan (3));
		block[14] = 11;
		"UserComment"u8.CopyTo (block.AsSpan (15));
		return block;
	}
}
