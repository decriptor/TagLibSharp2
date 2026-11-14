// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class RiffChunkTests
{
	[TestMethod]
	public void Constructor_WithValidData_SetsProperties ()
	{
		var data = new BinaryData ([0x01, 0x02, 0x03, 0x04]);
		var chunk = new RiffChunk ("test", data);

		Assert.AreEqual ("test", chunk.FourCC);
		Assert.AreEqual (4u, chunk.DataSize);
		Assert.IsTrue (chunk.IsValid);
	}

	[TestMethod]
	public void TotalSize_EvenData_NoPadding ()
	{
		var chunk = new RiffChunk ("test", new BinaryData ([1, 2, 3, 4]));
		// TotalSize = HeaderSize(8) + DataSize(4) + padding(0)
		Assert.AreEqual (12, chunk.TotalSize);
	}

	[TestMethod]
	public void TotalSize_OddData_IncludesPaddingByte ()
	{
		var chunk = new RiffChunk ("test", new BinaryData ([1, 2, 3]));
		// TotalSize = HeaderSize(8) + DataSize(3) + padding(1) = 12
		Assert.AreEqual (12, chunk.TotalSize);
	}

	[TestMethod]
	public void TotalSize_DataSizeNearIntMax_IsNotNegative ()
	{
		// Header (8) plus a pad byte overflows Int32 when DataSize is within 9 of int.MaxValue.
		var declared = (uint)(int.MaxValue - 4);
		var raw = new byte[RiffChunk.HeaderSize];
		"data"u8.CopyTo (raw);
		BinaryPrimitives.WriteUInt32LittleEndian (raw.AsSpan (4), declared);

		Assert.IsTrue (RiffChunk.TryParse (new BinaryData (raw), 0, out var chunk));
		Assert.AreEqual (declared, chunk.DataSize);
		var expected = RiffChunk.HeaderSize + (long)declared + (declared & 1);
		Assert.AreEqual (expected, chunk.TotalSize);
		Assert.IsTrue (chunk.TotalSize > 0, $"TotalSize wrapped to {chunk.TotalSize}");
	}

	[TestMethod]
	public void TryParse_ValidChunk_ParsesCorrectly ()
	{
		byte[] rawData = [
			(byte)'t', (byte)'e', (byte)'s', (byte)'t',
			0x04, 0x00, 0x00, 0x00,
			0xDE, 0xAD, 0xBE, 0xEF
		];

		var result = RiffChunk.TryParse (new BinaryData (rawData), 0, out var chunk);

		Assert.IsTrue (result);
		Assert.AreEqual ("test", chunk.FourCC);
		Assert.AreEqual (4u, chunk.DataSize);
	}

	[TestMethod]
	public void TryParse_AtOffset_ParsesCorrectly ()
	{
		byte[] rawData = [
			0xFF, 0xFF, 0xFF, 0xFF,
			(byte)'d', (byte)'a', (byte)'t', (byte)'a',
			0x02, 0x00, 0x00, 0x00,
			0xAB, 0xCD
		];

		var result = RiffChunk.TryParse (new BinaryData (rawData), 4, out var chunk);

		Assert.IsTrue (result);
		Assert.AreEqual ("data", chunk.FourCC);
	}

	[TestMethod]
	public void TryParse_NotEnoughDataForHeader_ReturnsFalse ()
	{
		byte[] rawData = [1, 2, 3, 4, 5, 6];
		var result = RiffChunk.TryParse (new BinaryData (rawData), 0, out _);
		Assert.IsFalse (result);
	}

	[TestMethod]
	public void Render_ProducesValidChunk ()
	{
		var data = new BinaryData ([0x01, 0x02, 0x03, 0x04]);
		var chunk = new RiffChunk ("fmt ", data);
		var rendered = chunk.Render ();

		Assert.AreEqual (12, rendered.Length);
		Assert.AreEqual ((byte)'f', rendered[0]);
		Assert.AreEqual ((byte)0x04, rendered[4]);
		Assert.AreEqual ((byte)0x01, rendered[8]);
	}

	[TestMethod]
	public void Render_OddLengthData_AddsPaddingByte ()
	{
		var data = new BinaryData ([0x01, 0x02, 0x03]);
		var chunk = new RiffChunk ("test", data);
		var rendered = chunk.Render ();

		Assert.AreEqual (12, rendered.Length);
		Assert.AreEqual ((byte)0x00, rendered[11]);
	}

	[TestMethod]
	public void Render_RoundTrip_ProducesSameData ()
	{
		var originalData = new BinaryData ([0xDE, 0xAD, 0xBE, 0xEF]);
		var original = new RiffChunk ("DATA", originalData);

		var rendered = original.Render ();
		Assert.IsTrue (RiffChunk.TryParse (rendered, 0, out var roundTripped));

		Assert.AreEqual (original.FourCC, roundTripped.FourCC);
		Assert.AreEqual (original.DataSize, roundTripped.DataSize);
	}

	[TestMethod]
	public void Equals_SameChunks_AreEqual ()
	{
		var data = new BinaryData ([1, 2, 3]);
		var chunk1 = new RiffChunk ("test", data);
		var chunk2 = new RiffChunk ("test", data);

		Assert.IsTrue (chunk1.Equals (chunk2));
		Assert.IsTrue (chunk1 == chunk2);
	}

	[TestMethod]
	public void Equals_DifferentFourCC_AreNotEqual ()
	{
		var data = new BinaryData ([1, 2, 3]);
		var chunk1 = new RiffChunk ("test", data);
		var chunk2 = new RiffChunk ("data", data);

		Assert.IsFalse (chunk1.Equals (chunk2));
		Assert.IsTrue (chunk1 != chunk2);
	}

	[TestMethod]
	public void IsValid_DefaultStruct_ReturnsFalse ()
	{
		var chunk = default (RiffChunk);
		Assert.IsFalse (chunk.IsValid);
	}
}
