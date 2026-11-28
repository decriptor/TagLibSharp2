// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageAssert;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>PNG, GIF, BMP, PCX and Netpbm width, height and bit depth from their headers.</summary>
[TestClass]
[TestCategory ("Unit")]
public class RasterSizeTests
{
	[TestMethod]
	public void Netpbm_WidthThatOverflowsInt32_Fails ()
	{
		Assert.IsFalse (MediaFile.ReadFromData (Encoding.ASCII.GetBytes ("P5\n4300000000 1\n255\n"), "a.pgm").IsSuccess);
		Assert.IsFalse (MediaFile.ReadFromData (Encoding.ASCII.GetBytes ("P5\n5000000000 1\n255\n"), "a.pgm").IsSuccess);
		Assert.IsFalse (MediaFile.ReadFromData (Encoding.ASCII.GetBytes ("P5\n2147483648 1\n255\n"), "a.pgm").IsSuccess);
		var opened = MediaFile.ReadFromData (Encoding.ASCII.GetBytes ("P5\n2 3\n255\n"), "a.pgm");
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var image = (IMediaFile)opened.File!;
		Assert.AreEqual (2, image.ImageProperties!.Value.Width);
		Assert.AreEqual (3, image.ImageProperties.Value.Height);
	}

	[TestMethod]
	public void Read_Png_ReportsIhdrSize ()
	{
		var data = PngIhdrWithoutCrc (width: 8, height: 5, depth: 8, color: 2);
		AssertImage (data, "photo.png", MediaFormat.Png, 8, 5, "PNG");
		Assert.AreEqual (24, ImageOf (data, "photo.png").ImageProperties!.Value.ColorDepth);
	}

	[TestMethod]
	public void Read_Gif_ReportsLogicalScreen ()
	{
		var data = GifScreen (width: 16, height: 9);
		AssertImage (data, "photo.gif", MediaFormat.Gif, 16, 9, "GIF");
	}

	[TestMethod]
	public void Read_Bmp_ReportsInfoHeaderAndAbsoluteHeight ()
	{
		AssertImage (Bmp (width: 7, height: -3), "photo.bmp", MediaFormat.Bmp, 7, 3, "BMP");
		Assert.AreEqual (24, ImageOf (Bmp (width: 7, height: -3), "photo.bmp").ImageProperties!.Value.ColorDepth);
	}

	[TestMethod]
	public void Read_Bmp_Os2CoreHeader_ReportsSizeAndBitCount ()
	{
		var data = new byte[26];
		"BM"u8.CopyTo (data);
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (14), 12);
		data[18] = 11;
		data[20] = 6;
		data[24] = 4;
		AssertImage (data, "photo.bmp", MediaFormat.Bmp, 11, 6, "BMP");
		Assert.AreEqual (4, ImageOf (data, "photo.bmp").ImageProperties!.Value.ColorDepth);
	}

	[TestMethod]
	public void Read_Bmp_ExtremeTopDownHeight_Fails ()
	{
		var opened = MediaFile.ReadFromData (Bmp (width: 1, height: int.MinValue), "photo.bmp");
		Assert.IsFalse (opened.IsSuccess);
	}

	[TestMethod]
	public void Read_Pcx_ReportsWindow ()
	{
		AssertImage (Pcx (xmin: 1, ymin: 2, xmax: 10, ymax: 6), "photo.pcx", MediaFormat.Pcx, 10, 5, "PCX");
	}

	[TestMethod]
	[DataRow ("P1", "PBM", "dot.pbm")]
	[DataRow ("P2", "PGM", "dot.pgm")]
	[DataRow ("P3", "PPM", "dot.ppm")]
	[DataRow ("P4", "PBM", "dot.pbm")]
	[DataRow ("P5", "PGM", "dot.pgm")]
	[DataRow ("P6", "PPM", "dot.ppm")]
	[DataRow ("P6", "PPM", "dot.pnm")]
	public void Read_Netpbm_ReportsRasterSize (string magic, string kind, string name)
	{
		var data = System.Text.Encoding.ASCII.GetBytes (magic + "\n# note\n12 9\n255\n");
		AssertImage (data, name, MediaFormat.Netpbm, 12, 9, kind);
	}
}
