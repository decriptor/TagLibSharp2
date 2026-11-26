// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ogg;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
public class OggCrcTests
{
	[TestMethod]
	public void Calculate_EmptySpan_ReturnsZero ()
	{
		var crc = OggCrc.Calculate (ReadOnlySpan<byte>.Empty);
		Assert.AreEqual (0u, crc);
	}

	[TestMethod]
	public void Calculate_EmptyArray_ReturnsZero ()
	{
		var crc = OggCrc.Calculate (Array.Empty<byte> ());
		Assert.AreEqual (0u, crc);
	}

	[TestMethod]
	public void Calculate_SingleByte_IsDeterministic ()
	{
		// Verify CRC calculation is deterministic
		var crc1 = OggCrc.Calculate (new byte[] { 0x00 });
		var crc2 = OggCrc.Calculate (new byte[] { 0x00 });
		Assert.AreEqual (crc1, crc2);

		// Different byte values should produce different CRCs
		var crc3 = OggCrc.Calculate (new byte[] { 0x01 });
		Assert.AreNotEqual (crc1, crc3);
	}

	[TestMethod]
	public void Calculate_SpanOverload_MatchesArrayOverload ()
	{
		byte[] data = [0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00];
		var crcSpan = OggCrc.Calculate (data.AsSpan ());
		var crcArray = OggCrc.Calculate (data);
		Assert.AreEqual (crcSpan, crcArray);
	}

	[TestMethod]
	public void Calculate_DifferentData_ReturnsDifferentCrc ()
	{
		var crc1 = OggCrc.Calculate (new byte[] { 0x01, 0x02, 0x03 });
		var crc2 = OggCrc.Calculate (new byte[] { 0x01, 0x02, 0x04 });
		Assert.AreNotEqual (crc1, crc2);
	}

	[TestMethod]
	public void Calculate_SameData_ReturnsSameCrc ()
	{
		byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
		var crc1 = OggCrc.Calculate (data);
		var crc2 = OggCrc.Calculate (data);
		Assert.AreEqual (crc1, crc2);
	}

	[TestMethod]
	public void Calculate_LargeData_CompletesSuccessfully ()
	{
		var data = new byte[64 * 1024]; // 64KB
		new Random (42).NextBytes (data);
		var crc = OggCrc.Calculate (data);
		// Just verify it completes and returns a non-zero value for random data
		Assert.IsTrue (crc != 0 || data.All (b => b == 0)); // Zero CRC only expected if all zeros
	}

	[TestMethod]
	public void Validate_ValidPage_ReturnsTrue ()
	{
		// Create a minimal valid Ogg page header (27 bytes minimum)
		byte[] page = new byte[27];
		// OggS capture pattern
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';
		page[4] = 0; // Version
		page[5] = 0x02; // Header type (BOS)
						// Granule position (bytes 6-13)
						// Serial number (bytes 14-17)
						// Page sequence (bytes 18-21)
						// CRC (bytes 22-25) - set to zero for calculation
		page[26] = 0; // Number of segments

		// Calculate correct CRC (with CRC field zeroed)
		var crc = OggCrc.Calculate (page);

		// Set CRC in little-endian
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		Assert.IsTrue (OggCrc.Validate (page));
	}

	[TestMethod]
	public void Validate_CorruptedPage_ReturnsFalse ()
	{
		// Create a valid page first
		byte[] page = new byte[27];
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';
		page[4] = 0;
		page[5] = 0x02;
		page[26] = 0;

		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		// Corrupt one byte (not the CRC field)
		page[5] = 0x04;

		Assert.IsFalse (OggCrc.Validate (page));
	}

	[TestMethod]
	public void Validate_TooShort_ReturnsFalse ()
	{
		byte[] shortData = new byte[26]; // Less than minimum 27 bytes
		Assert.IsFalse (OggCrc.Validate (shortData));
	}

	[TestMethod]
	public void Validate_WrongCrc_ReturnsFalse ()
	{
		byte[] page = new byte[27];
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';
		// Set an incorrect CRC value
		page[22] = 0xFF;
		page[23] = 0xFF;
		page[24] = 0xFF;
		page[25] = 0xFF;

		Assert.IsFalse (OggCrc.Validate (page));
	}

	[TestMethod]
	public void Calculate_KnownVector_ReturnsExpectedCrc ()
	{
		// Test with a known Ogg page header pattern
		// "OggS" followed by zeros (with CRC field zeroed)
		byte[] data = [
			(byte)'O', (byte)'g', (byte)'g', (byte)'S', // Capture pattern
			0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // Version, type, granule
			0, 0, 0, 0, // Serial
			0, 0, 0, 0, // Page sequence
			0, 0, 0, 0, // CRC (zeroed)
			0 // Segments
		];

		var crc = OggCrc.Calculate (data);

		// Pinned so a faster folding of the same polynomial cannot drift.
		Assert.AreEqual (0x9EA1A511u, crc);
		Assert.AreEqual (SpecCrc (data), crc);
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (7)]
	[DataRow (8)]
	[DataRow (9)]
	[DataRow (27)]
	[DataRow (64)]
	[DataRow (1000)]
	public void Calculate_AnyLength_MatchesBitDefinition (int length)
	{
		var data = new byte[length];
		var rng = new Random (length + 1);
		rng.NextBytes (data);
		Assert.AreEqual (SpecCrc (data), OggCrc.Calculate (data));
	}

	/// <summary>
	/// The Ogg definition: non-reflected polynomial 0x04C11DB7, init 0, no final XOR.
	/// </summary>
	static uint SpecCrc (ReadOnlySpan<byte> data)
	{
		const uint polynomial = 0x04C11DB7;
		uint crc = 0;
		foreach (var b in data) {
			crc ^= (uint)b << 24;
			for (var i = 0; i < 8; i++)
				crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ polynomial : crc << 1;
		}

		return crc;
	}

	[TestMethod]
	public void Calculate_AllOnes_ReturnsExpectedCrc ()
	{
		byte[] data = [0xFF, 0xFF, 0xFF, 0xFF];
		var crc = OggCrc.Calculate (data);

		// Verify determinism
		Assert.AreEqual (crc, OggCrc.Calculate (data));
	}

	[TestMethod]
	public void Validate_PageWithData_WorksCorrectly ()
	{
		// Create a page with one segment of data
		byte[] header = new byte[28]; // 27 header + 1 segment table entry
		header[0] = (byte)'O';
		header[1] = (byte)'g';
		header[2] = (byte)'g';
		header[3] = (byte)'S';
		header[4] = 0;
		header[5] = 0x02;
		header[26] = 1; // One segment
		header[27] = 5; // Segment is 5 bytes

		// Add the segment data
		byte[] page = new byte[33]; // 28 + 5 bytes of data
		Array.Copy (header, page, 28);
		page[28] = 0x01;
		page[29] = 0x02;
		page[30] = 0x03;
		page[31] = 0x04;
		page[32] = 0x05;

		// Calculate CRC (with CRC field zeroed)
		var crc = OggCrc.Calculate (page);

		// Set CRC
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		Assert.IsTrue (OggCrc.Validate (page));
	}
}
