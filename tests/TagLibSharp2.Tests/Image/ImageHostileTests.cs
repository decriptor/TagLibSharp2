// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Truncated, flipped and growing inputs through every read and save: never a throw, and work that grows linearly.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ImageHostileTests
{
	static readonly byte[] ZeroHeightJpeg = Concat (
		[0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x00, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00],
		[0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00],
		[0x12, 0xFF, 0x00, 0xFF, 0xD0, 0x34],
		[0xFF, 0xDC, 0x00, 0x04, 0x00, 0x05, 0xFF, 0xD9]);

	public static IEnumerable<object[]> Samples ()
	{
		var exif = ExifTiff ([(0x010E, "desc"), (0x013B, "artist")], [(0x9286, "ASCII\0\0\0c")]);
		yield return ["png", "a.png", Png (
			Text ("Title", "t"),
			Xmp (XmpPacket (Alt ("dc:title", "x") + Seq ("dc:creator", "a"))),
			PngChunk ("eXIf", exif),
			CompressedText ("Comment", "c"u8.ToArray ()),
			RawProfile ("xmp", Encoding.UTF8.GetBytes (XmpPacket (Alt ("dc:rights", "r")))),
			RawProfile ("exif", Concat ("Exif\0\0"u8.ToArray (), exif)),
			InternationalText ("Description", "d ✓", compressed: true),
			Text ("Artist", "exiftool artist"),
			PngChunk ("caBX", C2paStore ()),
			PngChunk ("iDOT", new byte[28]))];
		yield return ["gif", "a.gif", Gif (GifComment ("c"), GifApplication ("XMP DataXMP", "<x/>"u8.ToArray ()), GifApplication ("MGKIPTC0000", [1, 2]), GifApplication ("NETSCAPE2.0", [1, 0, 0]))];
		yield return ["jpeg", "a.jpg", Jpeg (JpegSegment (0xFE, "c"u8.ToArray ()), JpegApp (0xE1, "Exif\0\0", "MM\0*"), JpegApp (0xE1, "http://ns.adobe.com/xmp/extension/\0", "x"), JpegApp (0xEC, "Ducky\0", "x"))];
		yield return ["jpeg-dnl", "a.jpg", ZeroHeightJpeg];
		yield return ["jpeg-mirrors", "a.jpg", MpfJpeg (
			Concat (JpegExif (ExifTiff ([(0x010F, "Canon"), (0x9C9C, "xp")], [(0x9286, "ASCII\0\0\0uc")])), JpegXmp (XmpPacket (Alt ("exif:UserComment", "x")))),
			Concat (Jumbf (3, 1, C2paStore ()), JpegSegment (0xFE, "c"u8.ToArray ())), out _)];
		// A version-65536 Apple gain map: Clear keeps the primary's maker-note headroom (AppleHeadroom.Keep).
		var appleNote = AppleMakerNote ([(0x000B, 2, 5, "Jane\0"u8.ToArray ()), (0x0021, 10, 1, SRational (4, 5)), (0x0030, 10, 1, SRational (0, 1))]);
		yield return ["jpeg-apple-v1", "a.jpg", MpfJpeg (
			JpegExif (AppleExifTiff (appleNote)),
			[],
			Jpeg (JpegXmp ("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:HDRGainMap='http://ns.apple.com/HDRGainMap/1.0/' HDRGainMap:HDRGainMapVersion='65536'/></rdf:RDF></x:xmpmeta>")),
			out _)];
		yield return ["gif-xmp", "a.gif", Gif (GifComment ("c"), GifXmp (XmpPacket (Alt ("exif:UserComment", "x"))))];
		yield return ["bmp", "a.bmp", Bmp (2, -3, length: 58)];
		yield return ["pcx", "a.pcx", Pcx (0, 0, 3, 2)];
		yield return ["netpbm", "a.pgm", Encoding.ASCII.GetBytes ("P5\n# comment\n2 3\n255\n\0\0\0\0\0\0")];
		yield return ["tiff", "a.tif", TiffChain ([[(254, 1), (256, 4), (257, 3), (330, 8 + 2 + 48 + 4)], [(254, 0), (256, 40), (257, 30)]])];
		yield return ["dng", "a.dng", TiffChain ([[(254, 1), (256, 4), (257, 3), (330, 8 + 2 + 60 + 4), (0xC612, 1)], [(254, 0), (256, 40), (257, 30)]])];
		yield return ["bigtiff", "a.tif", BigTiff (70, 46, littleEndian: false)];
		yield return ["svg", "a.svg", Encoding.UTF8.GetBytes ("""<?xml version="1.0"?><!-- c --><!DOCTYPE svg [<!ENTITY a "]>">]><?pi x?><svg width="3cm" height="2in" viewBox="0 0 9 8"></svg>""")];
		yield return ["svg-utf16", "a.svg", Concat ([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes ("""<svg:svg viewBox="0 0 9 8"/>"""))];
	}

	static void ReadEverywhere (byte[] data, string name)
	{
		_ = MediaFile.DetectFormat (data, name);
		_ = StillImageFile.IsValidFormat (data);
		Exercise (MediaFile.ReadFromData (data, name).File as StillImageFile);
		Exercise (StillImageFile.Read (data).File);
		using var stream = new MemoryStream (data, writable: false);
		Exercise (MediaFile.Read (stream, name).File as StillImageFile);
	}

	static void Exercise (StillImageFile? file)
	{
		if (file?.Tag is not Tag tag)
			return;
		tag.Title = "t2";
		tag.Artist = "a2";
		tag.Comment = null;
		_ = file.Render ();
		tag.Clear ();
		_ = file.Render ();
	}

	[TestMethod]
	[DynamicData (nameof (Samples))]
	public void EveryTruncation_NeverThrows (string format, string name, byte[] valid)
	{
		var cases = Hostile.EveryTruncation (valid, data => ReadEverywhere (data, name));
		Assert.IsTrue (cases > 0, format);
	}

	[TestMethod]
	[DynamicData (nameof (Samples))]
	public void ByteFlips_NeverThrow (string format, string name, byte[] valid)
	{
		var cases = Hostile.ByteFlips (valid, data => ReadEverywhere (data, name));
		Assert.IsTrue (cases > 0, format);
	}

	// A flipped PNG byte usually fails its chunk CRC and the chunk is skipped. Recomputing the CRCs sends the
	// flipped bytes into the text, XMP and Exif parsers instead.
	[TestMethod]
	public void Png_ByteFlipsWithValidCrcs_NeverThrow ()
	{
		var valid = (byte[])Samples ().First ()[2];
		Hostile.ByteFlips (valid, data => {
			var fixedCrcs = (byte[])data.Clone ();
			FixPngCrcs (fixedCrcs);
			ReadEverywhere (fixedCrcs, "a.png");
		});
		Hostile.EveryTruncation (valid, data => {
			FixPngCrcs (data);
			ReadEverywhere (data, "a.png");
		});
	}

	static void FixPngCrcs (byte[] png)
	{
		var i = 8;
		while (i + 12 <= png.Length) {
			var length = BinaryPrimitives.ReadInt32BigEndian (png.AsSpan (i));
			if (length < 0 || i + 12L + length > png.Length)
				return;
			BinaryPrimitives.WriteUInt32BigEndian (png.AsSpan (i + 8 + length), BinaryData.ComputeCrc32 (png.AsSpan (i + 4, 4 + length)));
			i += 12 + length;
		}
	}

	public static IEnumerable<object[]> GrowingInputs ()
	{
		yield return ["png text chunks", (Func<int, byte[]>)(n => Png ([.. Enumerable.Range (0, n).Select (i => Text ("Comment", "value " + i))])), "a.png", 200];
		yield return ["png XMP properties", (Func<int, byte[]>)(n => Png (Xmp (XmpPacket (Alt ("dc:title", "t") + string.Concat (Enumerable.Range (0, n).Select (i => $"   <xmp:Label>{i}</xmp:Label>\n")))))), "a.png", 200];
		yield return ["png eXIf entries", (Func<int, byte[]>)(n => Png (PngChunk ("eXIf", ExifTiff ([.. Enumerable.Range (0, n).Select (i => (0x0100 + i, "v" + i)), (0x013B, "artist")], [])))), "a.png", 200];
		yield return ["gif comment sub-blocks", (Func<int, byte[]>)(n => Gif ([.. Enumerable.Range (0, n).Select (i => GifComment (new string ('c', 200)))])), "a.gif", 200];
		yield return ["gif application extensions", (Func<int, byte[]>)(n => Gif ([.. Enumerable.Range (0, n).Select (i => GifApplication ("MGKIPTC0000", new byte[200]))])), "a.gif", 200];
		yield return ["jpeg APP segments", (Func<int, byte[]>)(n => Jpeg ([.. Enumerable.Range (0, n).Select (i => JpegApp (0xE1, "Exif\0\0", new string ('e', 200)))])), "a.jpg", 200];
		yield return ["jpeg DNL scan", (Func<int, byte[]>)(n => Concat (ZeroHeightJpeg.AsSpan (0, 25).ToArray (), Enumerable.Repeat (new byte[] { 0x11, 0xFF, 0x00 }, n * 50).SelectMany (b => b).ToArray (), [0xFF, 0xDC, 0x00, 0x04, 0x00, 0x05])), "a.jpg", 200];
		yield return ["tiff IFD chain", (Func<int, byte[]>)(n => TiffChain ([[(254, 1), (256, 2), (257, 2)], .. Enumerable.Range (0, n).Select (_ => Array.Empty<(int, int)> ())])), "a.tif", 200];
		yield return ["svg prolog", (Func<int, byte[]>)(n => Encoding.UTF8.GetBytes ("<!--" + new string ('x', n * 50) + """--><svg width="2" height="3"/>""")), "a.svg", 100];
		yield return ["netpbm comments", (Func<int, byte[]>)(n => Encoding.ASCII.GetBytes ("P5\n" + string.Concat (Enumerable.Repeat ("# comment line\n", n)) + "2 3\n255\n")), "a.pgm", 200];
	}

	[TestMethod]
	[DoNotParallelize]
	[DynamicData (nameof (GrowingInputs))]
	public void ReadAndRewrite_AllocateLinearly (string shape, Func<int, byte[]> build, string name, int n)
	{
		Bounds.AssertLinearAllocation (build, data => {
			var file = (StillImageFile)MediaFile.ReadFromData (data, name).File!;
			if (file.Tag is not Tag tag)
				return;
			tag.Artist = "new artist";
			tag.Comment = "new comment";
			_ = file.Render ();
			tag.Clear ();
			_ = file.Render ();
		}, n, runs: 3);
		Assert.IsNotNull (shape);
	}
}
