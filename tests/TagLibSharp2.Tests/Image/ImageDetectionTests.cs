// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Telling an image format from its first bytes, without copying or decoding what is not an image.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ImageDetectionTests
{
	[TestMethod]
	public void MalformedContainers_FailToOpen ()
	{
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }, "bad.jpg");
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0xFF }, "fill.jpg");
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, "eoi.jpg");
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0xD0 }, "rst.jpg");
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01 }, "short.jpg");
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x11, 0x00 }, "zero.jpg");
		Reject (new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02 }, "sos.jpg");
		Reject (PngIhdr (0, 1, 8, 0), "zero.png");
		Reject (PngIhdr (1, 1, 8, 1), "type.png");
		Assert.AreEqual (8, Open (PngIhdr (2, 2, 8, 0), "g.png").ImageProperties!.Value.ColorDepth);
		Assert.AreEqual (8, Open (PngIhdr (2, 2, 8, 3), "p.png").ImageProperties!.Value.ColorDepth);
		Assert.AreEqual (16, Open (PngIhdr (2, 2, 8, 4), "ga.png").ImageProperties!.Value.ColorDepth);
		Assert.AreEqual (32, Open (PngIhdr (2, 2, 8, 6), "rgba.png").ImageProperties!.Value.ColorDepth);
		Reject (Encoding.ASCII.GetBytes ("GIF89a"), "short.gif");
		Reject (GifScreen (0, 1, flags: 0), "zero.gif");
	}

	static void Reject (byte[] data, string name)
	{
		var result = MediaFile.ReadFromData (data, name);
		Assert.IsFalse (result.IsSuccess, result.Error);
	}

	// A classic TIFF header alone is 8 bytes: exiftool 13.55 reports such a file as TIFF ("Bad IFD0 directory"), and
	// 7 bytes as a file format error.
	[TestMethod]
	[DataRow (new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0, 0 }, MediaFormat.Tiff)]
	[DataRow (new byte[] { 0x4D, 0x4D, 0, 42, 0, 0, 0, 8 }, MediaFormat.Tiff)]
	[DataRow (new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0 }, MediaFormat.Unknown)]
	public void DetectFormat_OfATiffHeaderAlone_IsTiffFromEightBytes (byte[] data, MediaFormat format) =>
		Assert.AreEqual (format, MediaFile.DetectFormat (data));

	[TestMethod]
	public void DetectFormat_DoesNotCopyAnAudioBufferToLookForSvg ()
	{
		var mp3 = new byte[200_000];
		mp3[0] = 0xFF;
		mp3[1] = 0xFB;
		mp3[2] = 0x90;
		var id3 = new byte[200_000];
		Encoding.ASCII.GetBytes ("ID3").CopyTo (id3, 0);
		id3[3] = 3;
		var asf = new byte[200_000];
		new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C }
			.CopyTo (asf, 0);
		MediaFile.DetectFormat (mp3);
		MediaFile.DetectFormat (id3);
		MediaFile.DetectFormat (asf);

		const int limit = 8 * 1024;
		Bounds.AssertAllocatesAtMost (limit, () => _ = MediaFile.DetectFormat (mp3), "mp3");
		Bounds.AssertAllocatesAtMost (limit, () => _ = MediaFile.DetectFormat (id3), "id3");
		Bounds.AssertAllocatesAtMost (limit, () => _ = MediaFile.DetectFormat (asf), "asf");
		Assert.AreNotEqual (MediaFormat.Svg, MediaFile.DetectFormat (mp3));
	}

	public static IEnumerable<object[]> MarkupThatIsNotSvg ()
	{
		yield return ["XML", Encoding.UTF8.GetBytes ("<?xml version='1.0'?><root>" + new string ('x', Mib) + "</root>")];
		yield return ["HTML", Encoding.UTF8.GetBytes ("<!DOCTYPE html><html><body>" + new string ('x', Mib) + "</body></html>")];
		yield return ["UTF-16 XML", Concat ([0xFF, 0xFE], Encoding.Unicode.GetBytes ("<root>" + new string ('x', Mib / 2) + "</root>"))];
		yield return ["UTF-16BE XML", Concat ([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes ("<root>" + new string ('x', Mib / 2) + "</root>"))];
		yield return ["long comment", Encoding.UTF8.GetBytes ("<!--" + new string ('x', Mib))];
		yield return ["unterminated doctype", Encoding.UTF8.GetBytes ("<!DOCTYPE x [" + new string ('"', Mib))];
	}

	[TestMethod]
	[DynamicData (nameof (MarkupThatIsNotSvg))]
	public void DetectFormat_OfMarkup_DoesNotDecodeIt (string name, byte[] data)
	{
		_ = MediaFile.DetectFormat (data);
		Bounds.AssertAllocatesAtMost (Noise, () => Assert.AreNotEqual (MediaFormat.Svg, MediaFile.DetectFormat (data)), name);
		Bounds.AssertAllocatesAtMost (Noise, () => Assert.IsFalse (StillImageFile.IsValidFormat (data)), name);
	}

	[TestMethod]
	public void Read_BytesNamedJpegThatAreNotJpeg_Fails ()
	{
		var opened = MediaFile.ReadFromData (new byte[] { 0x01, 0x02, 0x03 }, "nope.jpg");
		Assert.IsFalse (opened.IsSuccess);
	}

	[TestMethod]
	public void Read_FlacMagicNamedPng_StaysFlac ()
	{
		var data = new byte[] { 0x66, 0x4C, 0x61, 0x43, 0, 0, 0, 0 };
		Assert.AreEqual (MediaFormat.Flac, MediaFile.DetectFormat (data, "cover.png"));
	}
}
