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

/// <summary>JPEG width, height and bit depth from the start-of-frame segment, or the DNL segment after the first scan.</summary>
[TestClass]
[TestCategory ("Unit")]
public class JpegSizeTests
{
	// ITU-T T.81 §B.2.5: the DNL segment "shall immediately follow the first scan".
	static readonly byte[] ZeroHeightSof = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x00, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00];
	static readonly byte[] Sos = [0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00];
	static readonly byte[] Dnl = [0xFF, 0xDC, 0x00, 0x04, 0x00, 0x05];

	public static IEnumerable<object[]> DnlLayouts ()
	{
		yield return ["spec layout", Concat (ZeroHeightSof, Sos, [0x12, 0x34], Dnl, [0xFF, 0xD9]), 5];
		yield return ["stuffed 0xFF in the scan", Concat (ZeroHeightSof, Sos, [0xFF, 0x00, 0xFF, 0x00, 0x7F], Dnl, [0xFF, 0xD9]), 5];
		yield return ["restart markers in the scan", Concat (ZeroHeightSof, Sos, [0x01, 0xFF, 0xD0, 0x02, 0xFF, 0xD7, 0x03], Dnl, [0xFF, 0xD9]), 5];
		yield return ["fill bytes before DNL", Concat (ZeroHeightSof, Sos, [0x01, 0xFF, 0xFF, 0xFF], Dnl, [0xFF, 0xD9]), 5];
		yield return ["empty scan", Concat (ZeroHeightSof, Sos, Dnl), 5];
		yield return ["no EOI", Concat (ZeroHeightSof, Sos, [0x01], Dnl), 5];
		yield return ["DNL before the scan", Concat (ZeroHeightSof, Dnl, Sos, [0x01], [0xFF, 0xD9]), 0];
		yield return ["no DNL", Concat (ZeroHeightSof, Sos, [0x01, 0x02], [0xFF, 0xD9]), 0];
		yield return ["another marker before DNL", Concat (ZeroHeightSof, Sos, [0x01], [0xFF, 0xC4, 0x00, 0x02], Dnl, [0xFF, 0xD9]), 0];
		yield return ["DNL of zero lines", Concat (ZeroHeightSof, Sos, [0x01], [0xFF, 0xDC, 0x00, 0x04, 0x00, 0x00]), 0];
		yield return ["DNL with a wrong length", Concat (ZeroHeightSof, Sos, [0x01], [0xFF, 0xDC, 0x00, 0x05, 0x00, 0x05, 0x00]), 0];
		yield return ["DNL cut short", Concat (ZeroHeightSof, Sos, [0x01], [0xFF, 0xDC, 0x00, 0x04, 0x00]), 0];
		yield return ["scan ends in 0xFF", Concat (ZeroHeightSof, Sos, [0x01, 0xFF]), 0];
		yield return ["scan header cut short", Concat (ZeroHeightSof, [0xFF, 0xDA, 0x00, 0x08, 0x01]), 0];
	}

	[TestMethod]
	[DynamicData (nameof (DnlLayouts))]
	public void Jpeg_ZeroSofHeight_ReadsTheDnlAfterTheFirstScan (string layout, byte[] data, int height)
	{
		var opened = MediaFile.ReadFromData (data, "a.jpg");
		if (height == 0) {
			Assert.IsFalse (opened.IsSuccess, layout);
			return;
		}

		Assert.IsTrue (opened.IsSuccess, $"{layout}: {opened.Error}");
		var image = (IMediaFile)opened.File!;
		Assert.AreEqual (8, image.ImageProperties!.Value.Width, layout);
		Assert.AreEqual (height, image.ImageProperties.Value.Height, layout);
	}

	[TestMethod]
	public void Jpeg_DnlScan_DoesNotAllocatePerScanByte ()
	{
		static byte[] Build (int n) => Concat (ZeroHeightSof, Sos, Enumerable.Repeat (new byte[] { 0xFF, 0x00, 0x11 }, n).SelectMany (b => b).ToArray (), Dnl);
		var data = Build (200_000);
		_ = MediaFile.ReadFromData (data, "a.jpg");
		var allocated = Bounds.AllocatedBytes (() => Assert.IsTrue (MediaFile.ReadFromData (data, "a.jpg").IsSuccess));
		Assert.IsTrue (allocated < 16 * 1024, $"a 600 KB scan allocated {allocated} bytes");
	}

	[TestMethod]
	public void Read_Jpeg_ReportsSofSize ()
	{
		// APP0 before SOF, so the size is not in the first 32 bytes.
		var data = Jpeg (width: 320, height: 240, app0: 40);
		AssertImage (data, "photo.jpg", MediaFormat.Jpeg, 320, 240, "JPEG");
		Assert.AreEqual (8, ImageOf (data, "photo.jpg").ImageProperties!.Value.ColorDepth);
	}

	[TestMethod]
	public void Read_Jpeg_ColorDepthIsPrecisionTimesComponents ()
	{
		var data = Jpeg (width: 10, height: 4, app0: 0, components: 3);
		AssertImage (data, "photo.jpg", MediaFormat.Jpeg, 10, 4, "JPEG");
		Assert.AreEqual (24, ImageOf (data, "photo.jpg").ImageProperties!.Value.ColorDepth);
	}

	static byte[] Jpeg (int width, int height, int app0, int components = 1)
	{
		using var ms = new MemoryStream ();
		ms.Write ([0xFF, 0xD8, 0xFF, 0xE0]);
		ushort length = (ushort)(2 + app0);
		ms.Write ([(byte)(length >> 8), (byte)length]);
		ms.Write (new byte[app0]);
		var sofLength = 8 + (components * 3);
		ms.Write ([0xFF, 0xC0, (byte)(sofLength >> 8), (byte)sofLength, 0x08]);
		ms.Write ([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
		ms.Write ([(byte)components]);
		for (var component = 0; component < components; component++)
			ms.Write ([0x01, 0x11, 0x00]);
		return ms.ToArray ();
	}
}
