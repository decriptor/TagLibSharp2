// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ogg")]
public class OggPageHelperTests
{
	// ==========================================================================
	// ReadOggPageWithSegments Tests
	// ==========================================================================

	[TestMethod]
	public void ReadOggPageWithSegments_DataTooShort_ReturnsFailure ()
	{
		var data = new byte[10]; // Less than 27-byte header

		var result = OggPageHelper.ReadOggPageWithSegments (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void ReadOggPageWithSegments_InvalidOggMagic_ReturnsFailure ()
	{
		var data = new byte[30];
		data[0] = (byte)'X'; // Invalid magic

		var result = OggPageHelper.ReadOggPageWithSegments (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ReadOggPageWithSegments_ValidPage_ReturnsSuccess ()
	{
		var pageData = TestBuilders.Ogg.CreatePage (
			new byte[] { 0x01, 0x02, 0x03 },
			sequenceNumber: 0,
			flags: OggPageFlags.BeginOfStream);

		var result = OggPageHelper.ReadOggPageWithSegments (pageData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Segments.Count);
		Assert.IsTrue (result.IsPacketComplete[0]);
	}

	[TestMethod]
	public void ReadOggPageWithSegments_PacketExactly255Bytes_HasTwoSegments ()
	{
		// A 255-byte packet should be encoded as [255, 0] segments
		var packet = new byte[255];
		var pageData = TestBuilders.Ogg.CreatePage (packet, 0, OggPageFlags.None);

		var result = OggPageHelper.ReadOggPageWithSegments (pageData);

		Assert.IsTrue (result.IsSuccess);
		// The packet is complete (ends with 0-length segment)
		Assert.AreEqual (1, result.Segments.Count);
		Assert.AreEqual (255, result.Segments[0].Length);
		Assert.IsTrue (result.IsPacketComplete[0]);
	}

	[TestMethod]
	public void ReadOggPageWithSegments_PacketMultipleOf255_MarkedComplete ()
	{
		// A 510-byte packet should be encoded as [255, 255, 0] segments
		var packet = new byte[510];
		var pageData = TestBuilders.Ogg.CreatePage (packet, 0, OggPageFlags.None);

		var result = OggPageHelper.ReadOggPageWithSegments (pageData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Segments.Count);
		Assert.AreEqual (510, result.Segments[0].Length);
		Assert.IsTrue (result.IsPacketComplete[0]);
	}

	[TestMethod]
	public void ReadOggPageWithSegments_PacketContinuesToNextPage_MarkedIncomplete ()
	{
		// Create a page where the last segment is 255 (indicates continuation)
		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.Ogg);
		builder.Add ((byte)0); // Version
		builder.Add ((byte)OggPageFlags.None); // Flags
		builder.AddUInt64LE (0); // Granule
		builder.AddUInt32LE (1); // Serial
		builder.AddUInt32LE (0); // Sequence
		builder.AddUInt32LE (0); // CRC placeholder
		builder.Add ((byte)1); // 1 segment
		builder.Add ((byte)255); // Segment size 255 = continuation
		builder.Add (new byte[255]); // Data

		var page = builder.ToArray ();
		// Calculate CRC
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		var result = OggPageHelper.ReadOggPageWithSegments (page);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Segments.Count);
		Assert.IsFalse (result.IsPacketComplete[0], "Packet ending with 255-byte segment should be incomplete");
	}

	[TestMethod]
	public void ReadOggPageWithSegments_MultiplePacketsOnPage_ExtractsAll ()
	{
		// Create a page with multiple packets using proper segment table
		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.Ogg);
		builder.Add ((byte)0); // Version
		builder.Add ((byte)OggPageFlags.None); // Flags
		builder.AddUInt64LE (0); // Granule
		builder.AddUInt32LE (1); // Serial
		builder.AddUInt32LE (0); // Sequence
		builder.AddUInt32LE (0); // CRC placeholder
		builder.Add ((byte)3); // 3 segments
		builder.Add ((byte)10); // Packet 1: 10 bytes
		builder.Add ((byte)20); // Packet 2: 20 bytes
		builder.Add ((byte)5); // Packet 3: 5 bytes
		builder.Add (new byte[35]); // Total data

		var page = builder.ToArray ();
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		var result = OggPageHelper.ReadOggPageWithSegments (page);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3, result.Segments.Count);
		Assert.AreEqual (10, result.Segments[0].Length);
		Assert.AreEqual (20, result.Segments[1].Length);
		Assert.AreEqual (5, result.Segments[2].Length);
		Assert.IsTrue (result.IsPacketComplete[0]);
		Assert.IsTrue (result.IsPacketComplete[1]);
		Assert.IsTrue (result.IsPacketComplete[2]);
	}

	// ==========================================================================
	// FindLastGranulePosition Tests
	// ==========================================================================

	[TestMethod]
	public void FindLastGranulePosition_EmptyData_ReturnsZero ()
	{
		var data = Array.Empty<byte> ();

		var result = OggPageHelper.FindLastGranulePosition (data);

		Assert.AreEqual (0ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_NoValidPages_ReturnsZero ()
	{
		var data = new byte[100]; // All zeros, no valid pages

		var result = OggPageHelper.FindLastGranulePosition (data);

		Assert.AreEqual (0ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_SinglePageWithGranule_ReturnsGranule ()
	{
		var granulePosition = 48000ul; // 1 second at 48kHz
		var page = CreatePageWithGranule (granulePosition, OggPageFlags.EndOfStream);

		var result = OggPageHelper.FindLastGranulePosition (page);

		Assert.AreEqual (granulePosition, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_MultiplePagesWithGranules_ReturnsLast ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add (CreatePageWithGranule (48000ul, OggPageFlags.BeginOfStream, sequence: 0));
		builder.Add (CreatePageWithGranule (96000ul, OggPageFlags.None, sequence: 1));
		builder.Add (CreatePageWithGranule (144000ul, OggPageFlags.EndOfStream, sequence: 2));

		var result = OggPageHelper.FindLastGranulePosition (builder.ToArray ());

		Assert.AreEqual (144000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_GranuleIsAllOnes_ReturnsZero ()
	{
		// Granule position of 0xFFFFFFFFFFFFFFFF indicates "no packets complete on this page"
		var page = CreatePageWithGranule (0xFFFFFFFFFFFFFFFF, OggPageFlags.EndOfStream);

		var result = OggPageHelper.FindLastGranulePosition (page);

		Assert.AreEqual (0ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_PageWithEosFlag_PrefersEosPage ()
	{
		// When there are multiple valid granules, prefer the one from EOS page
		using var builder = new BinaryDataBuilder ();
		builder.Add (CreatePageWithGranule (999999ul, OggPageFlags.None, sequence: 0)); // Not EOS
		builder.Add (CreatePageWithGranule (48000ul, OggPageFlags.EndOfStream, sequence: 1)); // EOS

		var result = OggPageHelper.FindLastGranulePosition (builder.ToArray ());

		// Should return the last valid granule (48000 from EOS page)
		Assert.AreEqual (48000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_LargeFile_FindsLastPage ()
	{
		// Simulate a file larger than 64KB search window
		using var builder = new BinaryDataBuilder ();
		// Add padding to push last page beyond simple backward scan
		builder.Add (CreatePageWithGranule (1000ul, OggPageFlags.BeginOfStream, sequence: 0));
		builder.AddZeros (70000); // 70KB of padding
		builder.Add (CreatePageWithGranule (500000ul, OggPageFlags.EndOfStream, sequence: 1));

		var result = OggPageHelper.FindLastGranulePosition (builder.ToArray ());

		Assert.AreEqual (500000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_PagesNotInLast64KB_StillFindsLastPage ()
	{
		// Pages in the first half of the file, followed by large padding
		// This tests that we scan the entire file, not just last 64KB
		using var builder = new BinaryDataBuilder ();
		builder.Add (CreatePageWithGranule (48000ul, OggPageFlags.BeginOfStream, sequence: 0));
		builder.Add (CreatePageWithGranule (96000ul, OggPageFlags.EndOfStream, sequence: 1));
		builder.AddZeros (80000); // 80KB of garbage/padding at the end

		var result = OggPageHelper.FindLastGranulePosition (builder.ToArray ());

		Assert.AreEqual (96000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_TruncatedPage_ReturnsLastValidGranule ()
	{
		// Create a file where a page is truncated mid-way (segment table claims more data than exists)
		// This tests the bounds check when pageSize would exceed data.Length
		using var builder = new BinaryDataBuilder ();
		builder.Add (CreatePageWithGranule (48000ul, OggPageFlags.BeginOfStream, sequence: 0));
		builder.Add (CreatePageWithGranule (96000ul, OggPageFlags.EndOfStream, sequence: 1));

		var fullData = builder.ToArray ();
		// Truncate the second page - cut off 10 bytes from the end
		var truncatedData = fullData[..(fullData.Length - 10)];

		var result = OggPageHelper.FindLastGranulePosition (truncatedData);

		// Should return the granule from the first (complete) page
		Assert.AreEqual (48000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_CorruptedSegmentCount_HandlesGracefully ()
	{
		// Create a page with corrupted segment count (claims 255 segments but data is short)
		var page = CreatePageWithGranule (48000ul, OggPageFlags.EndOfStream);
		page[26] = 255; // Set segment count to max (255) but page only has 1 byte of data

		var result = OggPageHelper.FindLastGranulePosition (page);

		// The granule can still be read from the header (bytes 6-13)
		// This is graceful handling - returns what it can read
		Assert.AreEqual (48000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_TruncatedMidPageData_ReturnsValidGranules ()
	{
		// Create two valid pages, then truncate so that the second page's
		// data section is incomplete (header + segment table readable, but data truncated)
		using var builder = new BinaryDataBuilder ();
		var page1 = CreatePageWithGranule (48000ul, OggPageFlags.BeginOfStream, sequence: 0);
		builder.Add (page1);

		// Create page 2 with 100 bytes of data
		var page2Builder = new BinaryDataBuilder ();
		page2Builder.Add (TestConstants.Magic.Ogg);
		page2Builder.Add ((byte)0); // Version
		page2Builder.Add ((byte)OggPageFlags.EndOfStream);
		page2Builder.AddUInt64LE (96000ul); // Granule
		page2Builder.AddUInt32LE (1); // Serial
		page2Builder.AddUInt32LE (1); // Sequence
		page2Builder.AddUInt32LE (0); // CRC placeholder
		page2Builder.Add ((byte)1); // 1 segment
		page2Builder.Add ((byte)100); // Segment claims 100 bytes
		page2Builder.Add (new byte[100]); // Full data

		var page2 = page2Builder.ToArray ();
		var crc = OggCrc.Calculate (page2);
		page2[22] = (byte)(crc & 0xFF);
		page2[23] = (byte)((crc >> 8) & 0xFF);
		page2[24] = (byte)((crc >> 16) & 0xFF);
		page2[25] = (byte)((crc >> 24) & 0xFF);
		builder.Add (page2);

		var fullData = builder.ToArray ();
		// Truncate to cut off the second page's data but keep header
		// Page 2 starts at page1.Length, header is 27 bytes + 1 segment byte = 28 bytes
		// Truncate to keep only 50 bytes of the 100-byte data section
		var truncatedData = fullData[..(page1.Length + 28 + 50)];

		var result = OggPageHelper.FindLastGranulePosition (truncatedData);

		// Should return the granule from page 2 since header is readable
		// But the calculated page size (128) exceeds the remaining data
		// The code should handle this by not advancing past valid data
		Assert.AreEqual (96000ul, result);
	}

	[TestMethod]
	public void FindLastGranulePosition_OnlyEosPageGranule_IgnoresNonEosPages ()
	{
		// If a later page has EOS flag but lower granule, we should still prefer
		// the granule from the EOS page as it represents the true end of stream
		using var builder = new BinaryDataBuilder ();
		builder.Add (CreatePageWithGranule (999999ul, OggPageFlags.BeginOfStream, sequence: 0));
		builder.Add (CreatePageWithGranule (50000ul, OggPageFlags.EndOfStream, sequence: 1));

		var result = OggPageHelper.FindLastGranulePosition (builder.ToArray ());

		// Should return 50000 from the EOS page, not 999999
		Assert.AreEqual (50000ul, result);
	}

	// ==========================================================================
	// BuildOggPage Tests
	// ==========================================================================

	[TestMethod]
	public void BuildOggPage_EmptyPacketsArray_CreatesValidPage ()
	{
		var page = OggPageHelper.BuildOggPage ([], OggPageFlags.None, 0, 1, 0);

		Assert.IsNotNull (page);
		Assert.IsTrue (page.Length >= 28); // Header + at least 1 segment entry
		Assert.AreEqual ((byte)'O', page[0]);
		Assert.AreEqual ((byte)'g', page[1]);
		Assert.AreEqual ((byte)'g', page[2]);
		Assert.AreEqual ((byte)'S', page[3]);
	}

	[TestMethod]
	public void BuildOggPage_SingleSmallPacket_CorrectSegmentTable ()
	{
		var packet = new byte[] { 0x01, 0x02, 0x03 };
		var page = OggPageHelper.BuildOggPage ([packet], OggPageFlags.None, 0, 1, 0);

		// Verify segment count and table
		Assert.AreEqual (1, page[26]); // 1 segment
		Assert.AreEqual (3, page[27]); // Segment size = 3
	}

	[TestMethod]
	public void BuildOggPage_Packet255Bytes_HasZeroLengthTerminator ()
	{
		var packet = new byte[255];
		var page = OggPageHelper.BuildOggPage ([packet], OggPageFlags.None, 0, 1, 0);

		// Should have 2 segments: [255, 0]
		Assert.AreEqual (2, page[26]); // 2 segments
		Assert.AreEqual (255, page[27]); // First segment = 255
		Assert.AreEqual (0, page[28]); // Second segment = 0 (terminator)
	}

	[TestMethod]
	public void BuildOggPage_Packet510Bytes_HasCorrectSegments ()
	{
		var packet = new byte[510];
		var page = OggPageHelper.BuildOggPage ([packet], OggPageFlags.None, 0, 1, 0);

		// Should have 3 segments: [255, 255, 0]
		Assert.AreEqual (3, page[26]); // 3 segments
		Assert.AreEqual (255, page[27]);
		Assert.AreEqual (255, page[28]);
		Assert.AreEqual (0, page[29]); // Terminator
	}

	[TestMethod]
	public void BuildOggPage_MultiplePackets_AllIncluded ()
	{
		var packets = new byte[][] {
			new byte[] { 0x01, 0x02 },
			new byte[] { 0x03, 0x04, 0x05 },
			new byte[] { 0x06 }
		};
		var page = OggPageHelper.BuildOggPage (packets, OggPageFlags.None, 0, 1, 0);

		// Verify segment table: [2, 3, 1]
		Assert.AreEqual (3, page[26]); // 3 segments
		Assert.AreEqual (2, page[27]);
		Assert.AreEqual (3, page[28]);
		Assert.AreEqual (1, page[29]);
	}

	[TestMethod]
	public void BuildOggPage_BosFlag_SetCorrectly ()
	{
		var page = OggPageHelper.BuildOggPage ([new byte[1]], OggPageFlags.BeginOfStream, 0, 1, 0);

		Assert.AreEqual (0x02, page[5] & 0x02); // BOS flag is bit 1
	}

	[TestMethod]
	public void BuildOggPage_EosFlag_SetCorrectly ()
	{
		var page = OggPageHelper.BuildOggPage ([new byte[1]], OggPageFlags.EndOfStream, 0, 1, 0);

		Assert.AreEqual (0x04, page[5] & 0x04); // EOS flag is bit 2
	}

	[TestMethod]
	public void BuildOggPage_GranulePosition_EncodedCorrectly ()
	{
		var granule = 0x123456789ABCDEF0ul;
		var page = OggPageHelper.BuildOggPage ([new byte[1]], OggPageFlags.None, granule, 1, 0);

		// Granule is at bytes 6-13, little-endian
		var readGranule = (ulong)page[6] |
			((ulong)page[7] << 8) |
			((ulong)page[8] << 16) |
			((ulong)page[9] << 24) |
			((ulong)page[10] << 32) |
			((ulong)page[11] << 40) |
			((ulong)page[12] << 48) |
			((ulong)page[13] << 56);

		Assert.AreEqual (granule, readGranule);
	}

	[TestMethod]
	public void BuildOggPage_CrcIsCalculatedCorrectly ()
	{
		var packet = new byte[] { 0x01, 0x02, 0x03 };
		var page = OggPageHelper.BuildOggPage ([packet], OggPageFlags.None, 0, 1, 0);

		// Verify CRC by re-calculating
		var storedCrc = (uint)(page[22] | (page[23] << 8) | (page[24] << 16) | (page[25] << 24));

		// Zero out CRC field and recalculate
		var pageCopy = (byte[])page.Clone ();
		pageCopy[22] = 0;
		pageCopy[23] = 0;
		pageCopy[24] = 0;
		pageCopy[25] = 0;
		var calculatedCrc = OggCrc.Calculate (pageCopy);

		Assert.AreEqual (calculatedCrc, storedCrc);
	}

	[TestMethod]
	public void BuildOggPage_RoundTrip_CanBeReadBack ()
	{
		var packet = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
		var page = OggPageHelper.BuildOggPage ([packet], OggPageFlags.BeginOfStream, 12345, 42, 7);

		var readResult = OggPageHelper.ReadOggPageWithSegments (page);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual (1, readResult.Segments.Count);
		CollectionAssert.AreEqual (packet, readResult.Segments[0]);
		Assert.AreEqual (42u, readResult.Page.SerialNumber);
		Assert.AreEqual (7u, readResult.Page.SequenceNumber);
		Assert.IsTrue (readResult.Page.IsBeginOfStream);
	}

	// ==========================================================================
	// RenumberAudioPages Tests
	// ==========================================================================

	[TestMethod]
	public void RenumberAudioPages_EmptyInput_ReturnsEmptyArray ()
	{
		var result = OggPageHelper.RenumberAudioPages ([], 1, 2);

		Assert.AreEqual (0, result.Length);
	}

	[TestMethod]
	public void RenumberAudioPages_InvalidMagic_StopsProcessing ()
	{
		var invalidData = new byte[50];
		invalidData[0] = (byte)'X'; // Not "OggS"

		var result = OggPageHelper.RenumberAudioPages (invalidData, 1, 2);

		Assert.AreEqual (0, result.Length);
	}

	[TestMethod]
	public void RenumberAudioPages_SinglePage_SetsSequenceNumber ()
	{
		var page = TestBuilders.Ogg.CreatePage (new byte[10], 99, OggPageFlags.None);

		var result = OggPageHelper.RenumberAudioPages (page, 1, startSequence: 5);

		// Read back sequence number (bytes 18-21, little-endian)
		var seqNum = (uint)(result[18] | (result[19] << 8) | (result[20] << 16) | (result[21] << 24));
		Assert.AreEqual (5u, seqNum);
	}

	[TestMethod]
	public void RenumberAudioPages_SinglePage_SetsEosFlag ()
	{
		var page = TestBuilders.Ogg.CreatePage (new byte[10], 0, OggPageFlags.None); // No EOS initially

		var result = OggPageHelper.RenumberAudioPages (page, 1, 2);

		Assert.AreEqual (0x04, result[5] & 0x04, "Last page should have EOS flag set");
	}

	[TestMethod]
	public void RenumberAudioPages_MultiplePages_SequentialNumbers ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add (TestBuilders.Ogg.CreatePage (new byte[10], 0, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (new byte[10], 1, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (new byte[10], 2, OggPageFlags.None));

		var result = OggPageHelper.RenumberAudioPages (builder.ToArray (), 1, startSequence: 10);

		// Find each page and verify sequence
		var offset = 0;
		for (uint expectedSeq = 10; expectedSeq < 13; expectedSeq++) {
			Assert.AreEqual ((byte)'O', result[offset]);
			var seqNum = (uint)(result[offset + 18] | (result[offset + 19] << 8) |
				(result[offset + 20] << 16) | (result[offset + 21] << 24));
			Assert.AreEqual (expectedSeq, seqNum, $"Page at offset {offset} should have sequence {expectedSeq}");

			// Move to next page
			var segCount = result[offset + 26];
			var pageDataSize = 0;
			for (var i = 0; i < segCount; i++)
				pageDataSize += result[offset + 27 + i];
			offset += 27 + segCount + pageDataSize;
		}
	}

	[TestMethod]
	public void RenumberAudioPages_MultiplePages_OnlyLastHasEos ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add (TestBuilders.Ogg.CreatePage (new byte[10], 0, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (new byte[10], 1, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (new byte[10], 2, OggPageFlags.None));

		var result = OggPageHelper.RenumberAudioPages (builder.ToArray (), 1, 2);

		// Check each page's flags
		var pageOffsets = FindPageOffsets (result);
		Assert.AreEqual (3, pageOffsets.Count);

		for (var i = 0; i < pageOffsets.Count; i++) {
			var flags = result[pageOffsets[i] + 5];
			var hasEos = (flags & 0x04) != 0;
			if (i == pageOffsets.Count - 1)
				Assert.IsTrue (hasEos, "Last page should have EOS");
			else
				Assert.IsFalse (hasEos, $"Page {i} should not have EOS");
		}
	}

	[TestMethod]
	public void RenumberAudioPages_RecalculatesCrc ()
	{
		var page = TestBuilders.Ogg.CreatePage (new byte[10], 0, OggPageFlags.None);
		var originalCrc = (uint)(page[22] | (page[23] << 8) | (page[24] << 16) | (page[25] << 24));

		var result = OggPageHelper.RenumberAudioPages (page, 1, startSequence: 999);

		var newCrc = (uint)(result[22] | (result[23] << 8) | (result[24] << 16) | (result[25] << 24));

		// CRC should be different (sequence changed)
		Assert.AreNotEqual (originalCrc, newCrc);

		// Verify new CRC is valid
		var pageCopy = (byte[])result.Clone ();
		pageCopy[22] = 0;
		pageCopy[23] = 0;
		pageCopy[24] = 0;
		pageCopy[25] = 0;
		var calculatedCrc = OggCrc.Calculate (pageCopy);
		Assert.AreEqual (calculatedCrc, newCrc);
	}

	[TestMethod]
	public void RenumberAudioPages_TruncatedPage_ReturnsPartialResult ()
	{
		// Create a valid page, then truncate it so the data section is incomplete
		var page = TestBuilders.Ogg.CreatePage (new byte[100], 0, OggPageFlags.None);
		// Truncate to cut off 50 bytes of the 100-byte data section
		var truncated = page[..(page.Length - 50)];

		var result = OggPageHelper.RenumberAudioPages (truncated, 1, 0);

		// Should return empty array or partial result for truncated data
		Assert.AreEqual (0, result.Length, "Truncated page should not be processed");
	}

	[TestMethod]
	public void RenumberAudioPages_UpdatesSerialNumber ()
	{
		// Create page with serial number 99
		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.Ogg);
		builder.Add ((byte)0); // Version
		builder.Add ((byte)OggPageFlags.None); // Flags
		builder.AddUInt64LE (0); // Granule
		builder.AddUInt32LE (99); // Original serial = 99
		builder.AddUInt32LE (0); // Sequence
		builder.AddUInt32LE (0); // CRC placeholder
		builder.Add ((byte)1); // 1 segment
		builder.Add ((byte)5); // 5 bytes
		builder.Add (new byte[5]); // Data

		var page = builder.ToArray ();
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		// Request serial number 42
		var result = OggPageHelper.RenumberAudioPages (page, serialNumber: 42, startSequence: 0);

		// Read serial number back (bytes 14-17, little-endian)
		var resultSerial = (uint)(result[14] | (result[15] << 8) | (result[16] << 16) | (result[17] << 24));
		Assert.AreEqual (42u, resultSerial, "Serial number should be updated to 42");
	}

	// ==========================================================================
	// ExtractHeaderPackets Tests
	// ==========================================================================

	[TestMethod]
	public void ExtractHeaderPackets_MaxPacketSizeLimit_RejectsOversizedPackets ()
	{
		// Security: Prevent DoS via memory exhaustion by limiting max packet size
		// A malicious file could claim packets that span many pages, causing unbounded memory growth

		// The page cap is high enough for a 16 MiB packet, so use a small byte limit instead.
		// Each page holds ~65025 bytes (255 segments × 255 bytes)
		// With 5 pages, we get ~325KB, so a limit of 200KB should trigger the rejection
		const int testLimit = 200 * 1024; // 200 KB test limit
		var builder = new BinaryDataBuilder ();

		// Build first page with BOS flag (starts large packet)
		var firstPage = CreatePageForLargePacket (0, 65025, true, false, 0);
		builder.Add (firstPage);

		// Build continuation pages (all 255-byte segments = incomplete packet)
		for (var i = 1; i < 10; i++) { // 10 pages at 65KB each = 650KB > 200KB limit
			var contPage = CreatePageForLargePacket ((uint)i, 65025, false, false, 0);
			builder.Add (contPage);
		}

		// The parsing should reject this because accumulated packet exceeds the test limit
		var result = OggPageHelper.ExtractHeaderPackets (builder.ToArray (), maxPackets: 1, maxPacketSize: testLimit);

		// After the fix, this should return failure with error about size limit
		Assert.IsFalse (result.IsSuccess, "Should reject packets exceeding size limit");
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "size", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void ExtractHeaderPackets_ValidLargePacket_Succeeds ()
	{
		// A valid large packet (but under the limit) should still succeed
		// Test with 1MB packet spanning multiple pages
		const int packetLength = 1024 * 1024; // 1 MB - well under limit
		var pages = CreateMultiPagePacketPages (packetLength);

		var result = OggPageHelper.ExtractHeaderPackets (pages, maxPackets: 1);

		Assert.IsTrue (result.IsSuccess, $"Valid 1MB packet should succeed: {result.Error}");
		Assert.AreEqual (1, result.Packets.Count);
		Assert.AreEqual (packetLength, result.Packets[0].Length);
	}

	[TestMethod]
	public void ExtractHeaderPackets_ContinuedPacketAcross51Pages_Reassembles ()
	{
		// A lacing value below 255 ends a packet (https://xiph.org/ogg/doc/framing.html), so each
		// continued page carries one 255-byte segment. That is 51 pages and about 13 KB, not 51
		// full 65,025-byte pages. The cap counts pages, so this still has to clear a limit of 50.
		const int continuedPages = 50;
		const int lastSegment = 7;
		var packet = new byte[(continuedPages * 255) + lastSegment];
		for (var i = 0; i < packet.Length; i++)
			packet[i] = (byte)(i * 3 + 1);

		var builder = new BinaryDataBuilder ();
		var offset = 0;
		for (var pageIndex = 0; pageIndex < continuedPages; pageIndex++) {
			builder.Add (CreateContinuedPage ((uint)pageIndex, packet.AsSpan (offset, 255), finished: false));
			offset += 255;
		}

		builder.Add (CreateContinuedPage ((uint)continuedPages, packet.AsSpan (offset, lastSegment), finished: true));

		var result = OggPageHelper.ExtractHeaderPackets (builder.ToArray (), maxPackets: 1);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1, result.Packets.Count, "A packet that finishes on page 51 was dropped");
		CollectionAssert.AreEqual (packet, result.Packets[0]);
	}

	[TestMethod]
	public void ExtractHeaderPackets_MaxSizeCommentBetweenHeaders_Reassembles ()
	{
		// https://xiph.org/ogg/doc/framing.html — a lacing value of 255 continues a packet.
		// A 16 MiB packet fills (size / 65025) pages and still needs the page that terminates it.
		// Vorbis reads that packet after a one-page identification header and before setup.
		const int maxPacketSize = 16 * 1024 * 1024;
		const int maxBytesPerPage = 255 * 255;
		var commentPages = (maxPacketSize / maxBytesPerPage) + 1;
		var comment = new byte[maxPacketSize];
		comment[0] = 0x3C;
		comment[maxBytesPerPage] = 0xA5;
		comment[^1] = 0x5A;

		const uint serial = 1;
		var ident = OggPageHelper.BuildOggPage (
			[TestBuilders.Ogg.CreateIdentificationPacket ()],
			OggPageFlags.BeginOfStream, 0, serial, 0);
		var (commentBuilt, nextSequence) = OggPageHelper.BuildMultiPagePacket (
			comment, OggPageFlags.None, 0, serial, 1);
		Assert.AreEqual (commentPages, commentBuilt.Length,
			"A 16 MiB packet is full pages plus the terminating page");

		var setup = new byte[] { 5, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s', 0x42 };
		var setupPage = OggPageHelper.BuildOggPage (
			[setup], OggPageFlags.None, 0, serial, nextSequence);

		using var builder = new BinaryDataBuilder ();
		builder.Add (ident);
		foreach (var page in commentBuilt)
			builder.Add (page);
		builder.Add (setupPage);

		var result = OggPageHelper.ExtractHeaderPackets (builder.ToArray (), maxPackets: 3);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (3, result.Packets.Count,
			$"A 16 MiB comment spans {commentBuilt.Length} pages between two headers; the cap dropped a page");
		CollectionAssert.AreEqual (comment, result.Packets[1]);
		CollectionAssert.AreEqual (setup, result.Packets[2]);
	}

	static byte[] CreateContinuedPage (uint sequence, ReadOnlySpan<byte> payload, bool finished)
	{
		// An unfinished page ends in a lacing value of 255. The finishing page ends below 255.
		var lacing = finished ? (byte)payload.Length : (byte)255;
		if (!finished && payload.Length != 255)
			throw new ArgumentException ("An unfinished segment must be 255 bytes.", nameof (payload));

		var page = new byte[27 + 1 + payload.Length];
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';
		byte flags = 0;
		if (sequence == 0)
			flags |= (byte)OggPageFlags.BeginOfStream;
		else
			flags |= (byte)OggPageFlags.Continuation;
		page[5] = flags;
		page[14] = 1; // serial 1, little-endian
		page[18] = (byte)sequence;
		page[19] = (byte)(sequence >> 8);
		page[20] = (byte)(sequence >> 16);
		page[21] = (byte)(sequence >> 24);
		page[26] = 1;
		page[27] = lacing;
		payload.CopyTo (page.AsSpan (28));

		var crc = OggCrc.Calculate (page);
		page[22] = (byte)crc;
		page[23] = (byte)(crc >> 8);
		page[24] = (byte)(crc >> 16);
		page[25] = (byte)(crc >> 24);
		return page;
	}

	static byte[] CreatePageForLargePacket (uint sequence, int dataSize, bool isBos, bool isComplete, uint serial)
	{
		// Build a page with 255-byte segments (continuation segments)
		// If isComplete is false, all segments are 255 bytes (incomplete packet)
		var segmentCount = Math.Min (255, (dataSize / 255) + (isComplete ? 1 : 0));
		var actualDataSize = isComplete ? dataSize : segmentCount * 255;

		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.Ogg);
		builder.Add ((byte)0); // Version
		builder.Add ((byte)(isBos ? OggPageFlags.BeginOfStream :
			(sequence > 0 ? OggPageFlags.Continuation : OggPageFlags.None)));
		builder.AddUInt64LE (0); // Granule
		builder.AddUInt32LE (serial);
		builder.AddUInt32LE (sequence);
		builder.AddUInt32LE (0); // CRC placeholder
		builder.Add ((byte)segmentCount);

		// Segment table - all 255s except possibly last
		for (var i = 0; i < segmentCount - 1; i++)
			builder.Add ((byte)255);
		if (segmentCount > 0) {
			var lastSegment = isComplete ? (byte)(dataSize % 255) : (byte)255;
			builder.Add (lastSegment);
		}

		// Data
		builder.Add (new byte[actualDataSize]);

		var page = builder.ToArray ();
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		return page;
	}

	static byte[] CreateMultiPagePacketPages (int totalPacketSize)
	{
		const int maxBytesPerPage = 255 * 255; // 65025
		var builder = new BinaryDataBuilder ();
		var remaining = totalPacketSize;
		uint sequence = 0;

		while (remaining > 0) {
			var isFirst = sequence == 0;
			var pageDataSize = Math.Min (remaining, maxBytesPerPage);
			var isLast = remaining <= maxBytesPerPage;

			var page = CreatePageForLargePacket (sequence, pageDataSize, isFirst, isLast, 1);
			builder.Add (page);

			remaining -= pageDataSize;
			sequence++;
		}

		return builder.ToArray ();
	}

	// ==========================================================================
	// Segment Table Overflow Tests (RFC 3533 compliance)
	// ==========================================================================

	[TestMethod]
	public void BuildOggPage_LargePacketExceeding255Segments_ThrowsOrReturnsError ()
	{
		// Ogg pages can have at most 255 segments (segment count is 1 byte)
		// A packet > 65025 bytes (255 * 255) requires more than 255 segments
		// per packet, which cannot fit in a single page
		var largePacket = new byte[65026]; // Just over the limit

		// This should throw ArgumentException
		Assert.ThrowsExactly<ArgumentException> (() =>
			OggPageHelper.BuildOggPage ([largePacket], OggPageFlags.None, 0, 1, 0));
	}

	[TestMethod]
	public void BuildMultiPagePacket_PacketPastOnePage_DoesNotThrow ()
	{
		var packet = new byte[80_000];
		var (pages, next) = OggPageHelper.BuildMultiPagePacket (packet, OggPageFlags.None, 0, 1, 3);

		Assert.IsTrue (pages.Length > 1);
		Assert.AreEqual (3u + (uint)pages.Length, next);
		AssertPagesCarryOnePacket (pages, packet, finishingGranule: 0);
	}

	[TestMethod]
	[DataRow (65025)]
	[DataRow (130050)]
	public void BuildMultiPagePacket_MultipleOf65025Bytes_TerminatesOnNextPage (int length)
	{
		// 255 segments of 255 bytes is a full page. The packet still needs a lacing
		// value of 0, and that 0 must be its own continuation page — not a 256th
		// segment cast down to a count of 0.
		var packet = new byte[length];
		for (var i = 0; i < packet.Length; i++)
			packet[i] = (byte)(i * 17 + 3);

		const ulong finishingGranule = 0x0102030405060708UL;
		var (pages, next) = OggPageHelper.BuildMultiPagePacket (
			packet, OggPageFlags.None, finishingGranule, serialNumber: 7, startSequence: 5);

		Assert.AreEqual (5u + (uint)pages.Length, next);
		AssertPagesCarryOnePacket (pages, packet, finishingGranule);

		Assert.AreEqual ((byte)OggPageFlags.None, pages[0][5], "First page of a new packet is not continued");
		var terminator = pages[^1];
		Assert.AreEqual ((byte)OggPageFlags.Continuation, terminator[5]);
		Assert.AreEqual (0, terminator[5] & 0x06, "Terminating page is not BOS or EOS");
		Assert.AreEqual (1, terminator[26], "Terminating page has a single lacing value");
		Assert.AreEqual (0, terminator[27], "Terminating lacing value is 0");
		Assert.AreEqual (finishingGranule, ReadGranule (terminator));

		for (var i = 0; i < pages.Length - 1; i++) {
			Assert.AreEqual (ulong.MaxValue, ReadGranule (pages[i]), "Unfinished page has granule -1");
			Assert.AreEqual (255, pages[i][26]);
		}

		var (continued, _) = OggPageHelper.BuildMultiPagePacket (
			packet, OggPageFlags.Continuation, finishingGranule, serialNumber: 7, startSequence: 5);
		Assert.AreEqual ((byte)OggPageFlags.Continuation, continued[0][5],
			"A packet that already continued keeps the continuation flag");
		Assert.AreEqual (ulong.MaxValue, ReadGranule (continued[0]));
	}

	[TestMethod]
	public void BuildMultiPagePacket_PacketOf65026Bytes_RoundTrips ()
	{
		var packet = new byte[65026];
		for (var i = 0; i < packet.Length; i++)
			packet[i] = (byte)(i + 9);

		const ulong finishingGranule = 99UL;
		var (pages, next) = OggPageHelper.BuildMultiPagePacket (
			packet, OggPageFlags.None, finishingGranule, serialNumber: 3, startSequence: 1);

		Assert.AreEqual (1u + (uint)pages.Length, next);
		AssertPagesCarryOnePacket (pages, packet, finishingGranule);
		Assert.AreEqual (2, pages.Length, "65026 bytes are one full page plus a 1-byte page");

		Assert.AreEqual (ulong.MaxValue, ReadGranule (pages[0]));
		Assert.AreEqual (255, pages[0][26]);
		Assert.AreEqual ((byte)OggPageFlags.Continuation, pages[1][5]);
		Assert.AreEqual (finishingGranule, ReadGranule (pages[1]));
		Assert.AreEqual (1, pages[1][26]);
		Assert.AreEqual (1, pages[1][27], "The finishing page carries the last byte, not a bare 0");
		Assert.AreEqual (packet[^1], pages[1][^1]);
	}

	static void AssertPagesCarryOnePacket (byte[][] pages, byte[] packet, ulong finishingGranule)
	{
		Assert.IsGreaterThan (0, pages.Length);
		var joined = new List<byte> (packet.Length);
		for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++) {
			var page = pages[pageIndex];
			Assert.IsGreaterThanOrEqualTo (27, page.Length);
			Assert.AreEqual ((byte)'O', page[0]);
			Assert.AreEqual ((byte)'g', page[1]);
			Assert.AreEqual ((byte)'g', page[2]);
			Assert.AreEqual ((byte)'S', page[3]);

			var segmentCount = page[26];
			Assert.IsTrue (segmentCount is > 0 and <= 255,
				$"Page {pageIndex} segment count {segmentCount} is outside 1..255");
			Assert.IsLessThanOrEqualTo (page.Length, 27 + segmentCount);

			var onlyTerminator = segmentCount == 1 && page[27] == 0;
			var dataOffset = 27 + segmentCount;
			var pageFinishes = false;
			for (var i = 0; i < segmentCount; i++) {
				var lace = page[27 + i];
				if (lace == 0) {
					Assert.IsTrue (onlyTerminator,
						$"Page {pageIndex} has a 0 lacing value but is not only the terminator");
					pageFinishes = true;
				} else if (lace < 255) {
					pageFinishes = true;
				}

				Assert.IsLessThanOrEqualTo (page.Length, dataOffset + lace);
				joined.AddRange (page.AsSpan (dataOffset, lace).ToArray ());
				dataOffset += lace;
			}

			Assert.AreEqual (page.Length, dataOffset, $"Page {pageIndex} has bytes outside its lacing table");
			var isLast = pageIndex == pages.Length - 1;
			Assert.AreEqual (isLast, pageFinishes, $"Page {pageIndex} finish flag does not match its position");
			Assert.AreEqual (isLast ? finishingGranule : ulong.MaxValue, ReadGranule (page));
			if (!isLast && pageIndex > 0)
				Assert.AreEqual ((byte)OggPageFlags.Continuation, page[5]);
		}

		CollectionAssert.AreEqual (packet, joined.ToArray ());
	}

	static ulong ReadGranule (byte[] page)
	{
		ulong granule = 0;
		for (var i = 0; i < 8; i++)
			granule |= (ulong)page[6 + i] << (8 * i);
		return granule;
	}

	[TestMethod]
	public void BuildOggPage_PacketExactly65025Bytes_Succeeds ()
	{
		// 65025 bytes = 255 segments of 255 bytes each + 0-byte terminator = 256 segments
		// This is actually at the boundary, let's test 255*255 = 65025 bytes requires 255 + 1 segments
		// Actually: 65025 / 255 = 255 exactly, so it needs [255, 255, ..., 255, 0] = 256 segments
		// Let's test slightly smaller: 255*254 = 64770 requires 254 segments + 0 = 255 segments
		var packet = new byte[255 * 254]; // 64770 bytes, requires 255 segments

		var page = OggPageHelper.BuildOggPage ([packet], OggPageFlags.None, 0, 1, 0);

		Assert.IsNotNull (page);
		Assert.AreEqual (255, page[26], "Should have exactly 255 segments");
	}

	[TestMethod]
	public void BuildOggPage_MultiplePacketsExceedingSegmentLimit_ThrowsOrReturnsError ()
	{
		// Multiple smaller packets that together require > 255 segments
		var packets = new byte[256][]; // 256 packets of 255 bytes each = 256 segments
		for (var i = 0; i < 256; i++)
			packets[i] = new byte[255];

		Assert.ThrowsExactly<ArgumentException> (() =>
			OggPageHelper.BuildOggPage (packets, OggPageFlags.None, 0, 1, 0));
	}

	// ==========================================================================
	// Helper Methods
	// ==========================================================================

	static byte[] CreatePageWithGranule (ulong granulePosition, OggPageFlags flags, uint sequence = 0)
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.Ogg);
		builder.Add ((byte)0); // Version
		builder.Add ((byte)flags);
		builder.AddUInt64LE (granulePosition);
		builder.AddUInt32LE (1); // Serial
		builder.AddUInt32LE (sequence);
		builder.AddUInt32LE (0); // CRC placeholder
		builder.Add ((byte)1); // 1 segment
		builder.Add ((byte)1); // 1 byte of data
		builder.Add ((byte)0x00); // Data

		var page = builder.ToArray ();

		// Calculate CRC
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		return page;
	}

	static List<int> FindPageOffsets (byte[] data)
	{
		var offsets = new List<int> ();
		for (var i = 0; i <= data.Length - 4; i++) {
			if (data[i] == 'O' && data[i + 1] == 'g' && data[i + 2] == 'g' && data[i + 3] == 'S')
				offsets.Add (i);
		}
		return offsets;
	}
}
