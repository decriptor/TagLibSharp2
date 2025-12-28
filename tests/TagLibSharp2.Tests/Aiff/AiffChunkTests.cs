// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public class AiffChunkTests
{
	[TestMethod]
	public void TryParse_ValidChunk_ReturnsTrue ()
	{
		byte[] data = [
			0x43, 0x4F, 0x4D, 0x4D, // "COMM"
			0x00, 0x00, 0x00, 0x04, // Size = 4 (big-endian)
			0x01, 0x02, 0x03, 0x04  // Data
		];

		var result = AiffChunk.TryParse (new BinaryData (data), 0, out var chunk);

		Assert.IsTrue (result);
		Assert.IsNotNull (chunk);
	}

	[TestMethod]
	public void TryParse_TooShort_ReturnsFalse ()
	{
		byte[] data = [0x43, 0x4F, 0x4D, 0x4D]; // Just FourCC, no size

		var result = AiffChunk.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void FourCC_ParsedCorrectly ()
	{
		byte[] data = [
			0x53, 0x53, 0x4E, 0x44, // "SSND"
			0x00, 0x00, 0x00, 0x08, // Size = 8
			0x00, 0x00, 0x00, 0x00, // offset
			0x00, 0x00, 0x00, 0x00  // blockSize
		];

		AiffChunk.TryParse (new BinaryData (data), 0, out var chunk);

		Assert.AreEqual ("SSND", chunk!.FourCC);
	}

	[TestMethod]
	public void Size_BigEndian_ParsedCorrectly ()
	{
		// Create chunk with size = 256 (0x00000100 big-endian)
		var dataBuilder = new BinaryDataBuilder ();
		dataBuilder.Add (0x43, 0x4F, 0x4D, 0x4D); // "COMM"
		dataBuilder.Add (0x00, 0x00, 0x01, 0x00); // Size = 256 (big-endian)
		dataBuilder.Add (new byte[256]); // 256 bytes of data

		var result = AiffChunk.TryParse (dataBuilder.ToBinaryData (), 0, out var chunk);

		Assert.IsTrue (result);
		Assert.AreEqual (256u, chunk!.Size);
	}

	[TestMethod]
	public void Data_ContainsChunkContent ()
	{
		byte[] data = [
			0x41, 0x4E, 0x4E, 0x4F, // "ANNO"
			0x00, 0x00, 0x00, 0x05, // Size = 5
			0x48, 0x65, 0x6C, 0x6C, 0x6F // "Hello"
		];

		AiffChunk.TryParse (new BinaryData (data), 0, out var chunk);

		Assert.AreEqual (5, chunk!.Data.Length);
		Assert.AreEqual ("Hello", chunk.Data.ToStringLatin1 ());
	}

	[TestMethod]
	public void TryParse_AtOffset_WorksCorrectly ()
	{
		byte[] data = [
			0xFF, 0xFF, 0xFF, 0xFF, // Garbage at offset 0
			0x43, 0x4F, 0x4D, 0x4D, // "COMM" at offset 4
			0x00, 0x00, 0x00, 0x02, // Size = 2
			0xAB, 0xCD              // Data
		];

		var result = AiffChunk.TryParse (new BinaryData (data), 4, out var chunk);

		Assert.IsTrue (result);
		Assert.AreEqual ("COMM", chunk!.FourCC);
		Assert.AreEqual (2u, chunk.Size);
	}

	[TestMethod]
	public void TotalSize_IncludesHeaderAndData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x43, 0x4F, 0x4D, 0x4D); // "COMM"
		builder.Add (0x00, 0x00, 0x00, 0x12); // Size = 18
		builder.Add (new byte[18]); // 18 bytes of data

		var result = AiffChunk.TryParse (builder.ToBinaryData (), 0, out var chunk);

		Assert.IsTrue (result);
		// Total = 8 (header) + 18 (data) = 26
		Assert.AreEqual (26, chunk!.TotalSize);
	}

	[TestMethod]
	public void TotalSize_OddSize_IncludesPadding ()
	{
		byte[] data = [
			0x41, 0x4E, 0x4E, 0x4F, // "ANNO"
			0x00, 0x00, 0x00, 0x05, // Size = 5 (odd)
			0x48, 0x65, 0x6C, 0x6C, 0x6F, // "Hello"
			0x00                    // Padding byte
		];

		AiffChunk.TryParse (new BinaryData (data), 0, out var chunk);

		// Total = 8 (header) + 5 (data) + 1 (padding) = 14
		Assert.AreEqual (14, chunk!.TotalSize);
	}

	[TestMethod]
	public void TryParse_SizeExceedsData_ReturnsFalse ()
	{
		byte[] data = [
			0x43, 0x4F, 0x4D, 0x4D, // "COMM"
			0x00, 0x00, 0x01, 0x00, // Size = 256 (but we don't have that much data)
			0x01, 0x02              // Only 2 bytes
		];

		var result = AiffChunk.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void HeaderSize_IsEight ()
	{
		Assert.AreEqual (8, AiffChunk.HeaderSize);
	}
}
