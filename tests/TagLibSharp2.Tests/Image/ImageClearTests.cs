// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Clear: every metadata carrier is dropped and what a decoder or colour management needs is kept, in every format.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ImageClearTests
{
	[TestMethod]
	public void Png_Clear_DropsUnmappedText ()
	{
		var data = MinimalPng (Text ("Software", "camera"), PngChunk ("IDAT", [0x01]));
		var file = Open (data, "photo.png");
		Assert.IsNull (file.Tag!.Title);

		file.Tag.Clear ();
		var rendered = file.Render ().ToArray ();

		Assert.IsFalse (HasPngChunk (rendered, "tEXt"));
		CollectionAssert.AreEqual (new byte[] { 0x01 }, PngPayload (rendered, "IDAT"));
		Assert.IsNull (Open (rendered, "photo.png").Tag!.Title);
	}

	[TestMethod]
	public void Gif_Clear_DropsTheXmpExtension ()
	{
		var trap = new byte[] { 0x11, 0x22 };
		var data = GifWithXmp ("hello", trap);
		var file = Open (data, "anim.gif");
		Assert.AreEqual ("hello", file.Tag!.Comment);

		file.Tag.Comment = "next";
		var edited = file.Render ().ToArray ();
		Assert.IsTrue (edited.AsSpan ().IndexOf ("XMP Data"u8) >= 0);
		Assert.AreEqual ("next", Open (edited, "anim.gif").Tag!.Comment);

		file.Tag.Clear ();
		var cleared = file.Render ().ToArray ();
		Assert.IsTrue (cleared.AsSpan ().IndexOf ("XMP Data"u8) < 0);
		Assert.IsNull (Open (cleared, "anim.gif").Tag!.Comment);
		CollectionAssert.AreEqual (trap, GifImageSubBlock (cleared));
	}

	[TestMethod]
	public void Jpeg_Clear_DropsExifXmpIptc_AndUnknownApp1 ()
	{
		var data = JpegWithMetadata ();
		var file = Open (data, "photo.jpg");
		Assert.AreEqual ("hello", file.Tag!.Comment);

		file.Tag.Clear ();
		var rendered = file.Render ().ToArray ();

		Assert.IsTrue (rendered.AsSpan ().IndexOf ("Exif\0\0"u8) < 0);
		Assert.IsTrue (rendered.AsSpan ().IndexOf ("http://ns.adobe.com/xap/1.0/\0"u8) < 0);
		Assert.IsTrue (IndexOf (rendered, 0xFF, 0xED) < 0);
		Assert.IsTrue (rendered.AsSpan ().IndexOf ("keep"u8) < 0, "rule 7: an unknown APP1 is unmapped metadata");
		Assert.IsNull (Open (rendered, "photo.jpg").Tag!.Comment);
		Assert.AreEqual (8, Open (rendered, "photo.jpg").ImageProperties!.Value.Width);
	}

	static byte[] GifWithXmp (string comment, byte[] trap)
	{
		var gif = GifWithComment (comment, trap);
		var trailer = gif.Length - 1;
		var xmp = Encoding.ASCII.GetBytes ("XMP DataXMP");
		var block = new byte[3 + xmp.Length + 1 + 3 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		xmp.CopyTo (block, 3);
		block[3 + xmp.Length] = 3;
		block[^1] = 0;
		return Concat (gif.AsSpan (0, trailer).ToArray (), block, [0x3B]);
	}

	static byte[] JpegWithMetadata ()
	{
		var exif = Encoding.ASCII.GetBytes ("Exif\0\0GPS");
		var xmp = Encoding.ASCII.GetBytes ("http://ns.adobe.com/xap/1.0/\0<x/>");
		var iptc = Encoding.ASCII.GetBytes ("8BIM");
		var other = Encoding.ASCII.GetBytes ("Other\0keep");
		return Concat (
			[0xFF, 0xD8],
			JpegSegment (0xE1, exif),
			JpegSegment (0xE1, xmp),
			JpegSegment (0xED, iptc),
			JpegSegment (0xE1, other),
			JpegSegment (0xFE, Encoding.ASCII.GetBytes ("hello")),
			JpegSegment (0xC0, [0x08, 0x00, 0x01, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00]),
			JpegSegment (0xDA, []),
			[0xFF, 0xD9]);
	}

	static byte[] JpegSegment (byte marker, byte[] payload)
	{
		var length = payload.Length + 2;
		var segment = new byte[4 + payload.Length];
		segment[0] = 0xFF;
		segment[1] = marker;
		segment[2] = (byte)(length >> 8);
		segment[3] = (byte)length;
		payload.CopyTo (segment, 4);
		return segment;
	}

	public static IEnumerable<object[]> ClearCases ()
	{
		var exif = ExifTiff ([(0x010F, "Canon"), (0x0110, "EOS")], [(0x9286, "ASCII\0\0\0hi")]);
		yield return Png ("eXIf", PngChunk ("eXIf", exif), dropped: true);
		yield return Png ("zxIf", PngChunk ("zxIf", Concat ([0, 0], Zlib (exif))), dropped: true);
		yield return Png ("tIME", PngChunk ("tIME", [0x07, 0xEA, 11, 22, 10, 0, 0]), dropped: true);
		yield return Png ("iTXt XMP", Xmp (XmpPacket (Alt ("dc:title", "T"))), dropped: true);
		yield return Png ("raw profile exif", RawProfile ("exif", Concat ("Exif\0\0"u8.ToArray (), exif)), dropped: true);
		yield return Png ("raw profile iptc", RawProfile ("iptc", [0x1C, 0x02, 0x19, 0x00, 0x02, 0x6B, 0x77]), dropped: true);
		yield return Png ("unmapped tEXt", Text ("Software", "GIMP"), dropped: true);
		yield return Png ("iCCP", PngChunk ("iCCP", Concat ("icc"u8.ToArray (), [0, 0], Zlib ([1, 2, 3]))), dropped: false);
		yield return Png ("pHYs", PngChunk ("pHYs", [0, 0, 0x0B, 0x13, 0, 0, 0x0B, 0x13, 1]), dropped: false);
		yield return Png ("caBX C2PA manifest", PngChunk ("caBX", C2paStore ()), dropped: true);
		yield return Png ("dSIG", PngChunk ("dSIG", [1, 2, 3]), dropped: true);
		yield return Png ("iDOT", PngChunk ("iDOT", new byte[28]), dropped: true);
		yield return Png ("unknown ancillary", PngChunk ("vpAg", [0, 0, 0, 9, 0, 0, 0, 9, 0]), dropped: true);
		yield return Png ("sRGB", PngChunk ("sRGB", [0]), dropped: false);
		yield return Png ("cICP", PngChunk ("cICP", [1, 13, 0, 1]), dropped: false);
		yield return Png ("acTL", PngChunk ("acTL", [0, 0, 0, 1, 0, 0, 0, 0]), dropped: false);
		yield return Png ("sTER", PngChunk ("sTER", [0]), dropped: false);
		yield return Png ("unknown critical", PngChunk ("CgBI", [0x50, 0x00, 0x20, 0x06]), dropped: false);

		yield return Jpeg ("APP1 Exif", JpegExif (ExifTiff ([(0x010F, "Canon")], [(0x8827, "GPS")])), dropped: true);
		yield return Jpeg ("APP1 XMP", JpegXmp (XmpPacket (Alt ("dc:title", "T"))), dropped: true);
		yield return Jpeg ("APP1 extended XMP", JpegApp (0xE1, "http://ns.adobe.com/xmp/extension/\0", "0123456789ABCDEF0123456789ABCDEF\0\0\0\x10\0\0\0\0<rdf/>"), dropped: true);
		yield return Jpeg ("APP12 Ducky", JpegApp (0xEC, "Ducky\0", "\x03\0\x08\0\0\0\x02\0C\0o"), dropped: true);
		yield return Jpeg ("APP12 PictureInfo", JpegApp (0xEC, "[picture info]", "Camera=X"), dropped: true);
		yield return Jpeg ("APP13 IPTC", JpegApp (0xED, "Photoshop 3.0\0", "8BIM\x04\x04"), dropped: true);
		yield return Jpeg ("APP2 FPXR", JpegApp (0xE2, "FPXR\0", "\0\x01contents"), dropped: true);
		yield return Jpeg ("APP0 JFIF", JpegApp (0xE0, "JFIF\0", "\x01\x01\0\0\x01\0\x01\0\0"), dropped: false);
		yield return Jpeg ("APP2 ICC", JpegApp (0xE2, "ICC_PROFILE\0", "\x01\x01profile"), dropped: false);
		yield return Jpeg ("APP14 Adobe", JpegApp (0xEE, "Adobe", "\0\x64\0\0\0\0\x01"), dropped: false);
		// A JPEG XT box is any APP11 box that is not a JUMBF superbox ("jumb"); LCHK is the legacy checksum box.
		yield return Jpeg ("APP11 JPEG XT box", Jumbf (1, 1, Box ("LCHK", [0, 0, 0, 1])), dropped: false);
		yield return Jpeg ("APP11 JUMBF JSON", Jumbf (2, 1, JumbfStore (JsonUuid, "stds.exif", Box ("json", """{"exif:GPSLatitude":"47,30.0N"}"""u8.ToArray ()))), dropped: true);
		yield return Jpeg ("APP11 JUMBF XML", Jumbf (3, 1, JumbfStore (XmlUuid, "xmp", Box ("xml ", "<dc:creator>Jane</dc:creator>"u8.ToArray ()))), dropped: true);
		yield return ["jpeg", "APP11 JPEG XT box sharing En with a C2PA store", ImageTestBuilder.Jpeg (Jumbf (7, 1, C2paStore ()), Jumbf (7, 1, Box ("LCHK", [0, 0, 0, 2])), JpegSegment (0xFE, "c"u8.ToArray ())), Jumbf (7, 1, Box ("LCHK", [0, 0, 0, 2])), false];
		yield return Jpeg ("APP11 without JP", JpegApp (0xEB, "XX", "\0\0\0\0\0\0\0\0\0\0\0\0\0\0"), dropped: true);
		// JPEG XT decoding boxes (Artusi et al. 2016, Fig. 3) are kept; any other box type in APP11 is metadata or unknown.
		yield return Jpeg ("APP11 JPEG XT SPEC superbox", Jumbf (4, 1, Box ("SPEC", Box ("TONE", [0, 1]))), dropped: false);
		yield return Jpeg ("APP11 JPEG XT RESI box", Jumbf (5, 1, Box ("RESI", [0xFF, 0xD8, 0xFF, 0xD9])), dropped: false);
		yield return Jpeg ("APP11 JPEG XT ftyp box", Jumbf (6, 1, Box ("ftyp", "jpxt"u8.ToArray ())), dropped: false);
		yield return Jpeg ("APP11 xml box", Jumbf (8, 1, Box ("xml ", "<x:xmpmeta><dc:creator>Jane</dc:creator></x:xmpmeta>"u8.ToArray ())), dropped: true);
		yield return Jpeg ("APP11 uuid box (XMP)", Jumbf (9, 1, Box ("uuid", Concat (new byte[] { 0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC }, "<x/>"u8.ToArray ()))), dropped: true);
		yield return Jpeg ("APP11 json box", Jumbf (10, 1, Box ("json", """{"Gps":"47N"}"""u8.ToArray ())), dropped: true);
		yield return Jpeg ("APP11 Exif box", Jumbf (11, 1, Box ("Exif", Concat (new byte[4], "MM\0*"u8.ToArray ()))), dropped: true);
		yield return Jpeg ("APP11 unknown box", Jumbf (12, 1, Box ("ABCD", [1, 2, 3])), dropped: true);
		yield return Jpeg ("APP11 C2PA", Jumbf (7, 1, C2paStore ()), dropped: true);
		yield return ["jpeg", "APP11 C2PA continuation", ImageTestBuilder.Jpeg (Jumbf (7, 1, C2paStore ()), Jumbf (7, 2, Box ("jumb", [1, 2, 3])), JpegSegment (0xFE, "c"u8.ToArray ())), Jumbf (7, 2, Box ("jumb", [1, 2, 3])), true];
		yield return Jpeg ("APP1 other", JpegApp (0xE1, "Other\0", "drop"), dropped: true);
		yield return Jpeg ("APP4 SCALADO", JpegApp (0xE4, "SCALADO\0", "maker data"), dropped: true);
		yield return Jpeg ("APP0 AVI1", JpegApp (0xE0, "AVI1", "\0\0"), dropped: true);
		yield return Jpeg ("APP15 unknown", JpegApp (0xEF, "??", "x"), dropped: true);
		yield return Jpeg ("DRI (not APPn)", JpegSegment (0xDD, [0, 4]), dropped: false);

		yield return Gif ("XMP", GifApplication ("XMP DataXMP", "<x/>"u8.ToArray ()), dropped: true);
		yield return Gif ("ImageMagick IPTC", GifApplication ("MGKIPTC0000", [0x1C, 0x02, 0x19, 0x00, 0x03, 0x6B, 0x77, 0x31]), dropped: true);
		yield return Gif ("ImageMagick 8BIM", GifApplication ("MGK8BIM0000", "8BIM\x04\x04"u8.ToArray ()), dropped: true);
		yield return Gif ("NETSCAPE loop", GifApplication ("NETSCAPE2.0", [1, 0, 0]), dropped: false);
		yield return Gif ("ICC", GifApplication ("ICCRGBG1012", [1, 2, 3]), dropped: false);
		yield return Gif ("XMP with magic trailer", GifXmp (XmpPacket (Alt ("dc:title", "T"))), dropped: true);
		yield return Gif ("unknown application", GifApplication ("XYZWABCD1.0", [1, 2]), dropped: true);
		yield return Gif ("undefined label", [0x21, 0x77, 2, 1, 2, 0], dropped: true);
		yield return Gif ("graphic control", [0x21, 0xF9, 4, 0, 0, 0, 0, 0], dropped: false);

		static object[] Png (string name, byte[] carrier, bool dropped) =>
			["png", name, ImageTestBuilder.Png (carrier, Text ("Title", "T")), carrier, dropped];
		static object[] Jpeg (string name, byte[] carrier, bool dropped) =>
			["jpeg", name, ImageTestBuilder.Jpeg (carrier, JpegSegment (0xFE, "c"u8.ToArray ())), carrier, dropped];
		static object[] Gif (string name, byte[] carrier, bool dropped) =>
			["gif", name, ImageTestBuilder.Gif (GifComment ("c"), carrier), carrier, dropped];
	}

	[TestMethod]
	[DynamicData (nameof (ClearCases))]
	public void Clear_DropsEveryMetadataCarrier_AndKeepsWhatTheDecoderNeeds (string format, string carrier, byte[] input, byte[] block, bool dropped)
	{
		var name = "a." + (format == "jpeg" ? "jpg" : format);
		var file = Open (input, name);
		Assert.IsTrue (input.AsSpan ().IndexOf (block) >= 0, carrier);

		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();

		Assert.AreEqual (!dropped, output.AsSpan ().IndexOf (block) >= 0, $"{format} {carrier}: expected {(dropped ? "dropped" : "kept")}");
		var reread = Open (output, name);
		Assert.IsTrue (reread.Tag!.IsEmpty);
		Assert.IsNotNull (reread.ImageProperties);
	}

	[TestMethod]
	public void Clear_OfAShortApp11Segment_DropsItWithoutThrowing ()
	{
		var input = Jpeg (JpegSegment (0xEB, "JP\0\u0001\0\0\0\u0001"u8.ToArray ()), JpegSegment (0xFE, "c"u8.ToArray ()));
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();
		Assert.IsTrue (output.AsSpan ().IndexOf (new byte[] { 0xFF, 0xEB }) < 0);
	}
}
