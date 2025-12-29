// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ogg")]
public class OggPageTests
{
	[TestMethod]
	public void Read_ValidPage_ParsesHeader ()
	{
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 12345,
			sequenceNumber: 0,
			segmentData: new byte[] { 0x01, 0x02, 0x03, 0x04 });

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((byte)0, result.Page.Version);
		Assert.AreEqual (OggPageFlags.None, result.Page.Flags);
		Assert.AreEqual (0UL, result.Page.GranulePosition);
		Assert.AreEqual (12345U, result.Page.SerialNumber);
		Assert.AreEqual (0U, result.Page.SequenceNumber);
	}

	[TestMethod]
	public void Read_BeginOfStream_ParsesFlag ()
	{
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.BeginOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 0,
			segmentData: new byte[] { 0x01 });

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Page.IsBeginOfStream);
		Assert.IsFalse (result.Page.IsEndOfStream);
		Assert.IsFalse (result.Page.IsContinuation);
	}

	[TestMethod]
	public void Read_EndOfStream_ParsesFlag ()
	{
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.EndOfStream,
			granulePosition: 44100,
			serialNumber: 1,
			sequenceNumber: 5,
			segmentData: new byte[] { 0xFF });

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Page.IsEndOfStream);
		Assert.IsFalse (result.Page.IsBeginOfStream);
	}

	[TestMethod]
	public void Read_Continuation_ParsesFlag ()
	{
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.Continuation,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 1,
			segmentData: new byte[] { 0x00 });

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Page.IsContinuation);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		// Need at least 27 bytes to pass the length check and reach the magic check
		var data = new byte[OggPage.MinHeaderSize];

		var result = OggPage.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("OggS", result.Error!);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x4F, 0x67, 0x67, 0x53 }; // Just "OggS"

		var result = OggPage.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_GranulePosition_ParsesCorrectly ()
	{
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0x123456789ABCDEF0,
			serialNumber: 1,
			sequenceNumber: 0,
			segmentData: new byte[] { 0x00 });

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0x123456789ABCDEF0UL, result.Page.GranulePosition);
	}

	[TestMethod]
	public void Read_MultipleSegments_ParsesData ()
	{
		// Build page with multiple segments
		var segmentData = new byte[300]; // More than one segment (max segment is 255)
		for (var i = 0; i < 300; i++)
			segmentData[i] = (byte)(i % 256);

		var data = BuildOggPageWithData (segmentData);

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (300, result.Page.Data.Length);
	}

	[TestMethod]
	public void BytesConsumed_ReturnsCorrectSize ()
	{
		var segmentData = new byte[] { 0x01, 0x02, 0x03 };
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 0,
			segmentData: segmentData);

		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// Header (27) + segment table (1 entry) + data (3) = 31
		Assert.AreEqual (31, result.BytesConsumed);
	}


	static byte[] BuildSimpleOggPage (OggPageFlags flags, ulong granulePosition, uint serialNumber,
		uint sequenceNumber, byte[] segmentData)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic: "OggS"
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("OggS"));

		// Version: 0
		builder.Add ((byte)0);

		// Flags
		builder.Add ((byte)flags);

		// Granule position (8 bytes LE)
		builder.Add (BitConverter.GetBytes (granulePosition));

		// Serial number (4 bytes LE)
		builder.Add (BitConverter.GetBytes (serialNumber));

		// Sequence number (4 bytes LE)
		builder.Add (BitConverter.GetBytes (sequenceNumber));

		// CRC (4 bytes) - we'll set to 0 for now (validation skipped in basic tests)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 });

		// Segment count
		builder.Add ((byte)1);

		// Segment table (1 entry with size of data)
		builder.Add ((byte)segmentData.Length);

		// Data
		builder.Add (segmentData);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildOggPageWithData (byte[] data)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("OggS"));

		// Version
		builder.Add ((byte)0);

		// Flags
		builder.Add ((byte)OggPageFlags.None);

		// Granule position
		builder.Add (BitConverter.GetBytes (0UL));

		// Serial number
		builder.Add (BitConverter.GetBytes (1U));

		// Sequence number
		builder.Add (BitConverter.GetBytes (0U));

		// CRC
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 });

		// Build segment table
		var segments = new List<byte> ();
		var remaining = data.Length;
		while (remaining > 0) {
			var segSize = Math.Min (remaining, 255);
			segments.Add ((byte)segSize);
			remaining -= segSize;
		}

		// Segment count
		builder.Add ((byte)segments.Count);

		// Segment table
		foreach (var seg in segments)
			builder.Add (seg);

		// Data
		builder.Add (data);

		return builder.ToBinaryData ().ToArray ();
	}

	[TestMethod]
	public void Read_ValidCrc_Succeeds ()
	{
		// Build a page with correct CRC
		var segmentData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		var data = BuildSimpleOggPageWithCrc (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 12345,
			sequenceNumber: 0,
			segmentData: segmentData);

		var result = OggPage.Read (data, validateCrc: true);

		Assert.IsTrue (result.IsSuccess);
	}

	[TestMethod]
	public void Read_InvalidCrc_ReturnsFailure ()
	{
		// Build a page and corrupt the CRC
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 12345,
			sequenceNumber: 0,
			segmentData: new byte[] { 0x01, 0x02 });

		// Corrupt the CRC bytes (positions 22-25)
		data[22] = 0xFF;
		data[23] = 0xFF;
		data[24] = 0xFF;
		data[25] = 0xFF;

		var result = OggPage.Read (data, validateCrc: true);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("CRC", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_CrcValidationDisabled_IgnoresInvalidCrc ()
	{
		var data = BuildSimpleOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 12345,
			sequenceNumber: 0,
			segmentData: new byte[] { 0x01, 0x02 });

		// Corrupt the CRC
		data[22] = 0xFF;

		// Default is validateCrc: false
		var result = OggPage.Read (data);

		Assert.IsTrue (result.IsSuccess);
	}

	static byte[] BuildSimpleOggPageWithCrc (OggPageFlags flags, ulong granulePosition, uint serialNumber,
		uint sequenceNumber, byte[] segmentData)
	{
		var pageData = BuildSimpleOggPage (flags, granulePosition, serialNumber, sequenceNumber, segmentData);

		// Calculate and insert proper CRC
		// Zero out CRC field first (positions 22-25)
		pageData[22] = 0;
		pageData[23] = 0;
		pageData[24] = 0;
		pageData[25] = 0;

		var crc = OggCrc.Calculate (pageData);
		pageData[22] = (byte)(crc & 0xFF);
		pageData[23] = (byte)((crc >> 8) & 0xFF);
		pageData[24] = (byte)((crc >> 16) & 0xFF);
		pageData[25] = (byte)((crc >> 24) & 0xFF);

		return pageData;
	}

}
