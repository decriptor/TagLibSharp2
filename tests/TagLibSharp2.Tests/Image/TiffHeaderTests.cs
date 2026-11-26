// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;

namespace TagLibSharp2.Tests.Image;

/// <summary>The TIFF and BigTIFF headers a file is detected by, and the 8-byte reads BigTIFF needs.</summary>
[TestClass]
[TestCategory ("Unit")]
public class TiffHeaderTests
{
	// A BigTIFF header is "II" 43 0 or "MM" 0 43 (byte order, then version 43 in that order), then offset size 8,
	// reserved 0 and the first IFD's 8-byte offset: 16 bytes. Anything else, or fewer bytes, is not TIFF.
	[TestMethod]
	[DataRow (new byte[] { 0x49, 0x49, 0x2B, 0x00, 8, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0 }, true)]
	[DataRow (new byte[] { 0x4D, 0x4D, 0x00, 0x2B, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 16 }, true)]
	[DataRow (new byte[] { 0x49, 0x49, 0x2B, 0x00, 8, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0 }, false)]
	[DataRow (new byte[] { 0x4D, 0x4D, 0x2B, 0x00, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 16 }, false)]
	[DataRow (new byte[] { 0x49, 0x49, 0x00, 0x2B, 8, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0 }, false)]
	[DataRow (new byte[] { 0x4D, 0x49, 0x00, 0x2B, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 16 }, false)]
	[DataRow (new byte[] { 0x49, 0x4D, 0x2B, 0x00, 8, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0 }, false)]
	[DataRow (new byte[] { 0x4D, 0x4D, 0x01, 0x2B, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 16 }, false)]
	[DataRow (new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
	[DataRow (new byte[] { 0x4D, 0x4D, 0x2A, 0x00, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
	[DataRow (new byte[] { 0x49, 0x4D, 0x2A, 0x00, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
	public void Detection_OfATiffHeader (byte[] header, bool isTiff) =>
		Assert.AreEqual (isTiff ? MediaFormat.Tiff : MediaFormat.Unknown, MediaFile.DetectFormat (header));

	// A file named .tif that is not TIFF has no size, whatever its length.
	[TestMethod]
	[DataRow (new byte[0])]
	[DataRow (new byte[] { 0x49, 0x49, 0x2B, 0x00 })]
	[DataRow (new byte[] { 0x4D, 0x4D, 0x00, 0x2B, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
	public void Size_OfDataThatIsNotTiff_IsNone (byte[] data) =>
		Assert.IsFalse (TagLibSharp2.Image.Tiff.TiffSize.TryRead (data, dng: false, out _));

	// An 8-byte value is read only when all 8 bytes are there, in the file's byte order, and only up to 2^63 - 1.
	[TestMethod]
	public void TryU64_ReadsEightBytesInEitherOrder_UpToTheLargestLong ()
	{
		byte[] data = [0, 1, 2, 3, 4, 5, 6, 7, 8];

		Assert.IsTrue (Endian.TryU64 (data, 1, le: true, out var little));
		Assert.AreEqual (0x0807060504030201, little);
		Assert.IsTrue (Endian.TryU64 (data, 1, le: false, out var big));
		Assert.AreEqual (0x0102030405060708, big);
		Assert.IsFalse (Endian.TryU64 (data, 2, le: true, out _), "one byte short");
		Assert.IsFalse (Endian.TryU64 (data, -1, le: true, out _), "before the data");
		Assert.IsTrue (Endian.TryU64 ([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F], 0, le: true, out var largest));
		Assert.AreEqual (long.MaxValue, largest);
		Assert.IsFalse (Endian.TryU64 ([0, 0, 0, 0, 0, 0, 0, 0x80], 0, le: true, out var past), "above 2^63 - 1");
		Assert.AreEqual (0, past);
	}
}

/// <summary>The walks' counted list: indexes past its count are refused, read or written, though its array is larger.</summary>
[TestClass]
[TestCategory ("Unit")]
public class CountedListTests
{
	[TestMethod]
	public void Index_PastTheCount_IsRefused ()
	{
		var list = new TagLibSharp2.Image.Tiff.CountedList<int> ();
		list.Add (5);

		Assert.AreEqual (5, list[0]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => list[1]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => list[1] = 2);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => list[-1]);
	}
}
