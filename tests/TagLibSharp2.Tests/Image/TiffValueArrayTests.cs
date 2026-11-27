// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// One decoder locates a SHORT or LONG array, and one comparison decides the value budget. Both the plainness
/// check and the removal plan use those, so a second copy cannot drift.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class TiffValueArrayTests
{
	// IFD at 8, two entries. The SHORT count of 1 is the entry's own value field (byte 18). The LONG count of 2
	// is eight bytes, so it is the offset the entry stores (40).
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void ShortAndLongArrays_AreTheEntryValueOrTheOffsetTheyName (bool bigEndian)
	{
		var tiff = new TiffBuilder (48, bigEndian).Header (8)
			.Table (8, [(0x0111, 3, 1, 1), (0x0117, 4, 2, 40)])
			.ToArray ();
		Assert.IsTrue (TiffReader.TryClassic (tiff, out var reader, out var ifd0));
		Assert.AreEqual (8, ifd0);

		Assert.IsTrue (reader.TryValueArray (reader.Entry (8, 0), out var inline));
		Assert.AreEqual (18, inline.At, bigEndian ? "big-endian" : "little-endian");
		Assert.AreEqual (1, inline.Count);
		Assert.AreEqual (2, inline.Size);

		Assert.IsTrue (reader.TryValueArray (reader.Entry (8, 1), out var outside));
		Assert.AreEqual (40, outside.At);
		Assert.AreEqual (2, outside.Count);
		Assert.AreEqual (4, outside.Size);
	}

	// Offset 40 and eight bytes, in a block that ends at 44.
	[TestMethod]
	public void AnArrayThatRunsPastTheData_IsNotLocated ()
	{
		var tiff = new TiffBuilder (44).Header (8)
			.Table (8, [(0x0111, 4, 2, 40)])
			.ToArray ();
		Assert.IsTrue (TiffReader.TryClassic (tiff, out var reader, out _));

		Assert.IsFalse (reader.TryValueArray (reader.Entry (8, 0), out _));
	}

	[TestMethod]
	[DataRow (1)]
	[DataRow (7)]
	[DataRow (8)]
	public void ATypeOtherThanShortOrLong_IsNotAValueArray (int type)
	{
		var tiff = new TiffBuilder (40).Header (8)
			.Table (8, [(0x0111, type, 1, 1)])
			.ToArray ();
		Assert.IsTrue (TiffReader.TryClassic (tiff, out var reader, out _));

		Assert.IsFalse (reader.TryValueArray (reader.Entry (8, 0), out _), $"type {type}");
	}

	[TestMethod]
	public void AZeroCount_IsNotAValueArray ()
	{
		var tiff = new TiffBuilder (40).Header (8)
			.Table (8, [(0x0111, 3, 0, 0)])
			.ToArray ();
		Assert.IsTrue (TiffReader.TryClassic (tiff, out var reader, out _));

		Assert.IsFalse (reader.TryValueArray (reader.Entry (8, 0), out _));
	}

	// Half the bytes still fits (two arrays can). One more value, from either side of the sum, does not.
	[TestMethod]
	public void HalfTheBytesStillFits_OneMoreDoesNot ()
	{
		Assert.IsFalse (ValueBudget.PastHalf (0, 100, 200));
		Assert.IsTrue (ValueBudget.PastHalf (0, 101, 200));
		Assert.IsFalse (ValueBudget.PastHalf (50, 50, 200));
		Assert.IsTrue (ValueBudget.PastHalf (50, 51, 200));
		Assert.IsTrue (ValueBudget.PastHalf (101, 0, 200));
	}
}
