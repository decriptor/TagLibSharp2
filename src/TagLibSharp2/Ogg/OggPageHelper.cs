// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Ogg;

/// <summary>
/// Shared utilities for parsing and building Ogg pages.
/// </summary>
/// <remarks>
/// This class contains common functionality used by both <see cref="OggVorbisFile"/>
/// and <see cref="OggOpusFile"/> to avoid code duplication.
/// </remarks>
internal static class OggPageHelper
{
	/// <summary>
	/// Maximum number of segments per Ogg page.
	/// </summary>
	/// <remarks>
	/// Per RFC 3533 Section 6, the segment count field is a single byte (0-255).
	/// Each segment can hold up to 255 bytes, so the maximum page payload is 65,025 bytes.
	/// Packets larger than this must be split across multiple pages using continuation.
	/// </remarks>
	const int MaxSegmentsPerPage = 255;
	/// <summary>
	/// Reads an Ogg page and extracts individual packets based on segment table.
	/// </summary>
	/// <param name="data">The binary data to parse.</param>
	/// <param name="validateCrc">Whether to validate CRC checksum.</param>
	/// <returns>The parse result containing page and extracted packets.</returns>
	public static OggPageWithSegmentsResult ReadOggPageWithSegments (ReadOnlySpan<byte> data, bool validateCrc = false)
	{
		const int minHeaderSize = 27;

		if (data.Length < minHeaderSize)
			return OggPageWithSegmentsResult.Failure ("Data too short");

		// Read basic page structure (validates magic and CRC if requested)
		var pageResult = OggPage.Read (data, validateCrc);
		if (!pageResult.IsSuccess)
			return OggPageWithSegmentsResult.Failure (pageResult.Error ?? "Unknown error");

		// Extract segment table info - magic already validated by OggPage.Read
		var segmentCount = data[26];
		if (data.Length < 27 + segmentCount)
			return OggPageWithSegmentsResult.Failure ("Data too short for segment table");

		var segmentTable = data.Slice (27, segmentCount);

		// Extract packets from segments
		// A segment of 255 bytes means the packet continues
		// A segment < 255 bytes ends the packet
		var segments = new List<byte[]> ();
		var isPacketComplete = new List<bool> ();
		var currentPacket = new List<byte> ();
		var pageDataOffset = 0;

		for (var i = 0; i < segmentCount; i++) {
			var segmentSize = segmentTable[i];
			if (pageDataOffset + segmentSize > pageResult.Page.Data.Length)
				break;

			// Add segment to current packet using efficient batch copy
			var segmentData = pageResult.Page.Data.Span.Slice (pageDataOffset, segmentSize);
			AppendSpanToList (currentPacket, segmentData);

			pageDataOffset += segmentSize;

			// If segment < 255, packet is complete
			if (segmentSize < 255) {
				segments.Add (currentPacket.ToArray ());
				isPacketComplete.Add (true);
				currentPacket.Clear ();
			}
		}

		// If there's remaining data in currentPacket, it continues to next page
		if (currentPacket.Count > 0) {
			segments.Add (currentPacket.ToArray ());
			isPacketComplete.Add (false);
		}

		return OggPageWithSegmentsResult.Success (pageResult.Page, pageResult.BytesConsumed, segments, isPacketComplete);
	}

	/// <summary>
	/// Finds the granule position from the last Ogg page to calculate total samples.
	/// </summary>
	/// <param name="data">The file data to scan.</param>
	/// <returns>The granule position from the last valid page.</returns>
	/// <remarks>
	/// Scans the entire file to find all Ogg pages and returns the granule position
	/// from the page with the EOS flag, or the last valid page if no EOS is found.
	/// Per RFC 3533, the EOS flag marks the last page of a logical stream.
	/// </remarks>
	public static ulong FindLastGranulePosition (ReadOnlySpan<byte> data)
	{
		// Scan from the beginning to find all pages
		// Track both the last valid granule and the EOS page's granule
		ulong lastGranulePosition = 0;
		ulong eosGranulePosition = 0;
		var foundEos = false;
		var offset = 0;

		while (offset < data.Length - 27) {
			// Look for "OggS" magic
			if (data[offset] == 'O' && data[offset + 1] == 'g' &&
				data[offset + 2] == 'g' && data[offset + 3] == 'S') {

				// Read flags (byte 5)
				var flags = data[offset + 5];
				var isEos = (flags & 0x04) != 0;

				// Read granule position (8 bytes little-endian at offset 6)
				var granule = (ulong)data[offset + 6] |
					((ulong)data[offset + 7] << 8) |
					((ulong)data[offset + 8] << 16) |
					((ulong)data[offset + 9] << 24) |
					((ulong)data[offset + 10] << 32) |
					((ulong)data[offset + 11] << 40) |
					((ulong)data[offset + 12] << 48) |
					((ulong)data[offset + 13] << 56);

				// Only update if this looks like a valid granule position
				// (not -1 which is used for non-audio pages)
				if (granule != 0xFFFFFFFFFFFFFFFF) {
					lastGranulePosition = granule;

					// Per RFC 3533, prefer granule from EOS page
					if (isEos) {
						eosGranulePosition = granule;
						foundEos = true;
					}
				}

				// Skip to after this page to find next page
				var segmentCount = data[offset + 26];
				if (offset + 27 + segmentCount <= data.Length) {
					var pageDataSize = 0;
					for (var i = 0; i < segmentCount; i++)
						pageDataSize += data[offset + 27 + i];

					var totalPageSize = 27 + segmentCount + pageDataSize;
					// Verify the entire page fits in the data (handles truncated files)
					if (offset + totalPageSize > data.Length)
						break;

					offset += totalPageSize;
					continue;
				}
			}

			offset++;
		}

		// Prefer EOS page's granule if found, otherwise use last valid
		return foundEos ? eosGranulePosition : lastGranulePosition;
	}

	/// <summary>
	/// Builds an Ogg page from multiple packets with proper segment boundaries.
	/// </summary>
	/// <param name="packets">The packets to include in the page.</param>
	/// <param name="flags">Page flags (BOS, EOS, continuation).</param>
	/// <param name="granulePosition">The granule position for this page.</param>
	/// <param name="serialNumber">The stream serial number.</param>
	/// <param name="sequenceNumber">The page sequence number.</param>
	/// <returns>The built Ogg page as a byte array.</returns>
	/// <exception cref="ArgumentException">
	/// Thrown when packets require more than 255 segments. Per RFC 3533, the segment
	/// count field is a single byte, limiting each page to 255 segments maximum.
	/// Large packets must be split across multiple pages.
	/// </exception>
	public static byte[] BuildOggPage (byte[][] packets, OggPageFlags flags, ulong granulePosition,
		uint serialNumber, uint sequenceNumber)
	{
		// Build segment table - each packet needs proper segmentation
		var segments = new List<byte> ();
		var totalDataSize = 0;

		foreach (var packet in packets) {
			var remaining = packet.Length;
			while (remaining >= 255) {
				segments.Add (255);
				remaining -= 255;
			}
			// Final segment < 255 marks end of this packet
			segments.Add ((byte)remaining);
			totalDataSize += packet.Length;
		}

		// Ensure at least one segment
		if (segments.Count == 0)
			segments.Add (0);

		// RFC 3533: Segment count is a single byte, max 255 segments per page
		if (segments.Count > MaxSegmentsPerPage)
			throw new ArgumentException (
				$"Packets require {segments.Count} segments, but Ogg pages support at most {MaxSegmentsPerPage}. " +
				"Large packets must be split across multiple pages.", nameof (packets));

		var headerSize = 27 + segments.Count;
		var page = new byte[headerSize + totalDataSize];

		// Magic "OggS"
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';

		// Version
		page[4] = 0;

		// Flags
		page[5] = (byte)flags;

		// Granule position (8 bytes LE)
		for (var i = 0; i < 8; i++)
			page[6 + i] = (byte)(granulePosition >> (i * 8));

		// Serial number (4 bytes LE)
		for (var i = 0; i < 4; i++)
			page[14 + i] = (byte)(serialNumber >> (i * 8));

		// Sequence number (4 bytes LE)
		for (var i = 0; i < 4; i++)
			page[18 + i] = (byte)(sequenceNumber >> (i * 8));

		// CRC placeholder (will be calculated)
		page[22] = 0;
		page[23] = 0;
		page[24] = 0;
		page[25] = 0;

		// Segment count
		page[26] = (byte)segments.Count;

		// Segment table
		for (var i = 0; i < segments.Count; i++)
			page[27 + i] = segments[i];

		// Data - concatenate all packets
		var dataOffset = headerSize;
		foreach (var packet in packets) {
			packet.CopyTo (page, dataOffset);
			dataOffset += packet.Length;
		}

		// Calculate and set CRC
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		return page;
	}

	/// <summary>
	/// Builds multiple Ogg pages for a large packet that exceeds the single-page segment limit.
	/// </summary>
	/// <remarks>
	/// Per RFC 3533, a single Ogg page can have at most 255 segments. For packets larger than
	/// 65025 bytes (255 × 255), the packet must be split across multiple pages. The first page
	/// contains the specified flags, and continuation pages have the continuation flag set.
	/// </remarks>
	/// <param name="packet">The packet data to split across pages.</param>
	/// <param name="firstPageFlags">Flags for the first page (subsequent pages use Continuation).</param>
	/// <param name="granulePosition">The granule position (typically 0 for header pages).</param>
	/// <param name="serialNumber">The stream serial number.</param>
	/// <param name="startSequence">The starting sequence number.</param>
	/// <returns>Array of Ogg pages containing the packet, and the next sequence number to use.</returns>
	public static (byte[][] Pages, uint NextSequence) BuildMultiPagePacket (
		byte[] packet,
		OggPageFlags firstPageFlags,
		ulong granulePosition,
		uint serialNumber,
		uint startSequence)
	{
		const int maxSegmentsPerPage = 255;
		const int maxBytesPerSegment = 255;
		const int maxBytesPerPage = maxSegmentsPerPage * maxBytesPerSegment; // 65025

		// If packet fits in one page, use BuildOggPage
		var segmentsNeeded = (packet.Length / maxBytesPerSegment) + (packet.Length % maxBytesPerSegment != 0 ? 1 : 0);
		if (packet.Length % maxBytesPerSegment == 0 && packet.Length > 0)
			segmentsNeeded++; // Need a 0-byte terminator segment

		if (segmentsNeeded <= maxSegmentsPerPage) {
			var page = BuildOggPage ([packet], firstPageFlags, granulePosition, serialNumber, startSequence);
			return ([page], startSequence + 1);
		}

		// Split packet across multiple pages
		var pages = new List<byte[]> ();
		var packetOffset = 0;
		var sequenceNumber = startSequence;

		while (packetOffset < packet.Length) {
			var isFirstPage = packetOffset == 0;
			var remaining = packet.Length - packetOffset;
			var pageDataSize = Math.Min (remaining, maxBytesPerPage);

			// Build segment table for this page
			var segments = new List<byte> ();
			var bytesInPage = pageDataSize;
			while (bytesInPage >= maxBytesPerSegment) {
				segments.Add (maxBytesPerSegment);
				bytesInPage -= maxBytesPerSegment;
			}

			// Check if packet ends on this page
			var isLastPage = packetOffset + pageDataSize >= packet.Length;
			if (isLastPage) {
				// Packet ends here - add final segment (< 255 bytes)
				segments.Add ((byte)bytesInPage);
			} else {
				// Packet continues to next page - all segments must be 255
				// (we've already filled with 255s, just need to ensure no partial)
				if (bytesInPage > 0) {
					// Should not happen with our chunking logic
					segments.Add ((byte)bytesInPage);
				}
			}

			// Build page header
			var pageFlags = isFirstPage ? firstPageFlags : OggPageFlags.Continuation;
			var headerSize = 27 + segments.Count;
			var page = new byte[headerSize + pageDataSize];

			// Magic "OggS"
			page[0] = (byte)'O';
			page[1] = (byte)'g';
			page[2] = (byte)'g';
			page[3] = (byte)'S';

			// Version
			page[4] = 0;

			// Flags
			page[5] = (byte)pageFlags;

			// Granule position (8 bytes little-endian) - 0 for header pages
			var gp = granulePosition;
			for (var i = 0; i < 8; i++) {
				page[6 + i] = (byte)(gp & 0xFF);
				gp >>= 8;
			}

			// Serial number (4 bytes little-endian)
			page[14] = (byte)(serialNumber & 0xFF);
			page[15] = (byte)((serialNumber >> 8) & 0xFF);
			page[16] = (byte)((serialNumber >> 16) & 0xFF);
			page[17] = (byte)((serialNumber >> 24) & 0xFF);

			// Sequence number (4 bytes little-endian)
			page[18] = (byte)(sequenceNumber & 0xFF);
			page[19] = (byte)((sequenceNumber >> 8) & 0xFF);
			page[20] = (byte)((sequenceNumber >> 16) & 0xFF);
			page[21] = (byte)((sequenceNumber >> 24) & 0xFF);

			// CRC placeholder (will be calculated below)
			page[22] = 0;
			page[23] = 0;
			page[24] = 0;
			page[25] = 0;

			// Segment count
			page[26] = (byte)segments.Count;

			// Segment table
			for (var i = 0; i < segments.Count; i++)
				page[27 + i] = segments[i];

			// Copy packet data for this page
			Array.Copy (packet, packetOffset, page, headerSize, pageDataSize);

			// Calculate CRC
			var crc = OggCrc.Calculate (page);
			page[22] = (byte)(crc & 0xFF);
			page[23] = (byte)((crc >> 8) & 0xFF);
			page[24] = (byte)((crc >> 16) & 0xFF);
			page[25] = (byte)((crc >> 24) & 0xFF);

			pages.Add (page);
			packetOffset += pageDataSize;
			sequenceNumber++;
		}

		return (pages.ToArray (), sequenceNumber);
	}

	/// <summary>
	/// Renumbers audio pages with sequential sequence numbers and ensures EOS flag on last page.
	/// </summary>
	/// <param name="audioData">The raw audio page data.</param>
	/// <param name="serialNumber">The stream serial number to set on all pages.</param>
	/// <param name="startSequence">The starting sequence number (typically 2 after header pages).</param>
	/// <returns>The fixed audio pages with correct serial/sequence numbers and EOS flag.</returns>
	public static byte[] RenumberAudioPages (ReadOnlySpan<byte> audioData, uint serialNumber, uint startSequence)
	{
		// First pass: collect all pages to know which is the last one
		var pages = new List<(int Offset, int Length)> ();
		var offset = 0;

		while (offset < audioData.Length - 27) {
			// Check for OggS magic
			if (audioData[offset] != 'O' || audioData[offset + 1] != 'g' ||
				audioData[offset + 2] != 'g' || audioData[offset + 3] != 'S')
				break;

			var segmentCount = audioData[offset + 26];
			if (offset + 27 + segmentCount > audioData.Length)
				break;

			var pageDataSize = 0;
			for (var i = 0; i < segmentCount; i++)
				pageDataSize += audioData[offset + 27 + i];

			var totalPageSize = 27 + segmentCount + pageDataSize;
			if (offset + totalPageSize > audioData.Length)
				break;

			pages.Add ((offset, totalPageSize));
			offset += totalPageSize;
		}

		if (pages.Count == 0)
			return Array.Empty<byte> ();

		// Second pass: copy pages with renumbering and EOS fix
		var result = new byte[offset];
		var sequence = startSequence;

		for (var i = 0; i < pages.Count; i++) {
			var (pageOffset, pageLength) = pages[i];
			var isLastPage = (i == pages.Count - 1);

			// Copy the page
			audioData.Slice (pageOffset, pageLength).CopyTo (result.AsSpan (pageOffset));

			// Fix serial number (bytes 14-17, little-endian)
			result[pageOffset + 14] = (byte)(serialNumber & 0xFF);
			result[pageOffset + 15] = (byte)((serialNumber >> 8) & 0xFF);
			result[pageOffset + 16] = (byte)((serialNumber >> 16) & 0xFF);
			result[pageOffset + 17] = (byte)((serialNumber >> 24) & 0xFF);

			// Fix sequence number (bytes 18-21, little-endian)
			result[pageOffset + 18] = (byte)(sequence & 0xFF);
			result[pageOffset + 19] = (byte)((sequence >> 8) & 0xFF);
			result[pageOffset + 20] = (byte)((sequence >> 16) & 0xFF);
			result[pageOffset + 21] = (byte)((sequence >> 24) & 0xFF);

			// Fix EOS flag on last page (byte 5)
			if (isLastPage)
				result[pageOffset + 5] |= 0x04; // Set EOS bit

			// Recalculate CRC (must zero CRC bytes first)
			result[pageOffset + 22] = 0;
			result[pageOffset + 23] = 0;
			result[pageOffset + 24] = 0;
			result[pageOffset + 25] = 0;

			var crc = OggCrc.Calculate (result.AsSpan (pageOffset, pageLength));
			result[pageOffset + 22] = (byte)(crc & 0xFF);
			result[pageOffset + 23] = (byte)((crc >> 8) & 0xFF);
			result[pageOffset + 24] = (byte)((crc >> 16) & 0xFF);
			result[pageOffset + 25] = (byte)((crc >> 24) & 0xFF);

			sequence++;
		}

		return result;
	}

	/// <summary>
	/// Extracts header packets from an Ogg stream.
	/// </summary>
	/// <param name="data">The binary data to parse.</param>
	/// <param name="maxPackets">Maximum number of packets to extract.</param>
	/// <param name="validateCrc">Whether to validate CRC checksums.</param>
	/// <returns>A result containing the extracted packets, or an error.</returns>
	/// <remarks>
	/// This method reads Ogg pages and reassembles packets that may span multiple pages.
	/// It handles continuation flags and multi-page packets correctly.
	/// </remarks>
	public static HeaderPacketsResult ExtractHeaderPackets (
		ReadOnlySpan<byte> data,
		int maxPackets,
		bool validateCrc = false)
	{
		var packets = new List<byte[]> ();
		var packetBuffer = new List<byte> ();
		var offset = 0;
		var pageCount = 0;
		uint serialNumber = 0;
		var isFirstPage = true;

		while (offset < data.Length && packets.Count < maxPackets && pageCount < 50) {
			var pageResult = ReadOggPageWithSegments (data.Slice (offset), validateCrc);
			if (!pageResult.IsSuccess) {
				if (pageCount == 0)
					return HeaderPacketsResult.Failure ($"Invalid Ogg file: {pageResult.Error}");
				break;
			}

			offset += pageResult.BytesConsumed;
			pageCount++;

			// First page must have BOS flag
			if (isFirstPage) {
				if (!pageResult.Page.IsBeginOfStream)
					return HeaderPacketsResult.Failure ("First page must have BOS flag");
				serialNumber = pageResult.Page.SerialNumber;
				isFirstPage = false;
			}

			// Handle continuation from previous page
			var segmentIndex = 0;
			if (pageResult.Page.IsContinuation && packetBuffer.Count > 0) {
				if (pageResult.Segments.Count > 0) {
					packetBuffer.AddRange (pageResult.Segments[0]);
					if (pageResult.IsPacketComplete[0]) {
						packets.Add (packetBuffer.ToArray ());
						packetBuffer.Clear ();
					}
					segmentIndex = 1;
				}
			}

			// Process remaining segments
			for (var i = segmentIndex; i < pageResult.Segments.Count && packets.Count < maxPackets; i++) {
				if (pageResult.IsPacketComplete[i]) {
					if (packetBuffer.Count > 0) {
						// Shouldn't happen if continuation logic is correct, but handle it
						packetBuffer.AddRange (pageResult.Segments[i]);
						packets.Add (packetBuffer.ToArray ());
						packetBuffer.Clear ();
					} else {
						packets.Add (pageResult.Segments[i]);
					}
				} else {
					packetBuffer.Clear ();
					packetBuffer.AddRange (pageResult.Segments[i]);
				}
			}
		}

		return HeaderPacketsResult.Success (packets, serialNumber);
	}

	/// <summary>
	/// Efficiently appends a span to a list without unnecessary allocations.
	/// </summary>
	/// <param name="list">The target list.</param>
	/// <param name="span">The span to append.</param>
	/// <remarks>
	/// On .NET 5+, uses CollectionsMarshal for zero-copy append.
	/// On older frameworks, pre-allocates capacity and adds bytes individually to avoid
	/// the allocation that would occur from calling span.ToArray().
	/// </remarks>
	internal static void AppendSpanToList (List<byte> list, ReadOnlySpan<byte> span)
	{
		if (span.IsEmpty)
			return;

#if NET5_0_OR_GREATER
		var oldCount = list.Count;
		list.EnsureCapacity (oldCount + span.Length);
		System.Runtime.InteropServices.CollectionsMarshal.SetCount (list, oldCount + span.Length);
		span.CopyTo (System.Runtime.InteropServices.CollectionsMarshal.AsSpan (list).Slice (oldCount));
#else
		// Pre-allocate to avoid multiple resizes during the loop
		var requiredCapacity = list.Count + span.Length;
		if (list.Capacity < requiredCapacity)
			list.Capacity = requiredCapacity;

		// Add bytes individually (avoids span.ToArray() allocation)
		foreach (var b in span)
			list.Add (b);
#endif
	}
}

/// <summary>
/// Result of reading an Ogg page with extracted packet segments.
/// </summary>
internal readonly struct OggPageWithSegmentsResult
{
	public OggPage Page { get; }
	public bool IsSuccess { get; }
	public string? Error { get; }
	public int BytesConsumed { get; }
	public IReadOnlyList<byte[]> Segments { get; }
	public IReadOnlyList<bool> IsPacketComplete { get; }

	OggPageWithSegmentsResult (OggPage page, bool isSuccess, string? error, int bytesConsumed,
		IReadOnlyList<byte[]> segments, IReadOnlyList<bool> isPacketComplete)
	{
		Page = page;
		IsSuccess = isSuccess;
		Error = error;
		BytesConsumed = bytesConsumed;
		Segments = segments;
		IsPacketComplete = isPacketComplete;
	}

	public static OggPageWithSegmentsResult Success (OggPage page, int bytesConsumed,
		List<byte[]> segments, List<bool> isPacketComplete) =>
		new (page, true, null, bytesConsumed, segments, isPacketComplete);

	public static OggPageWithSegmentsResult Failure (string error) =>
		new (default, false, error, 0, Array.Empty<byte[]> (), Array.Empty<bool> ());
}

/// <summary>
/// Result of extracting header packets from an Ogg stream.
/// </summary>
internal readonly struct HeaderPacketsResult
{
	/// <summary>
	/// Gets a value indicating whether the extraction was successful.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets the error message if extraction failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the extracted packets.
	/// </summary>
	public IReadOnlyList<byte[]> Packets { get; }

	/// <summary>
	/// Gets the serial number from the first page.
	/// </summary>
	public uint SerialNumber { get; }

	HeaderPacketsResult (bool isSuccess, string? error, IReadOnlyList<byte[]> packets, uint serialNumber)
	{
		IsSuccess = isSuccess;
		Error = error;
		Packets = packets;
		SerialNumber = serialNumber;
	}

	public static HeaderPacketsResult Success (List<byte[]> packets, uint serialNumber) =>
		new (true, null, packets, serialNumber);

	public static HeaderPacketsResult Failure (string error) =>
		new (false, error, Array.Empty<byte[]> (), 0);
}
