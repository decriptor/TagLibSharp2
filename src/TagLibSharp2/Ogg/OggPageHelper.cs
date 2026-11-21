// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Ogg;

/// <summary>
/// Shared utilities for parsing and building Ogg pages.
/// </summary>
/// <remarks>
/// This class contains common functionality used by <see cref="OggVorbisFile"/>,
/// <see cref="OggOpusFile"/>, and <see cref="OggFlacFile"/> to avoid code duplication.
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
	internal const int MaxSegmentsPerPage = 255;

	/// <summary>
	/// Maximum payload of one Ogg page, in bytes.
	/// </summary>
	const int MaxBytesPerPage = MaxSegmentsPerPage * 255;

	/// <summary>
	/// Maximum allowed packet size when extracting headers (16 MB).
	/// </summary>
	/// <remarks>
	/// This limit prevents DoS attacks via memory exhaustion from malicious files that
	/// claim packets spanning many pages. 16 MB is generous for audio metadata - even
	/// large album art rarely exceeds 10 MB.
	/// </remarks>
	const int MaxPacketSize = 16 * 1024 * 1024;

	/// <summary>
	/// Pages read while reassembling Vorbis and Opus header packets.
	/// </summary>
	/// <remarks>
	/// https://xiph.org/ogg/doc/framing.html: a lacing value of 255 continues a packet, and a
	/// full page holds 255 such segments (<see cref="MaxBytesPerPage"/> bytes). A packet of
	/// <see cref="MaxPacketSize"/> fills (<see cref="MaxPacketSize"/> / <see cref="MaxBytesPerPage"/>)
	/// of those pages and needs one more page to terminate it, including a lacing value of 0 when
	/// the length is an exact multiple of <see cref="MaxBytesPerPage"/>. Vorbis places that packet
	/// after a one-page identification header and before a one-page setup header. One less stops
	/// on the comment and drops the setup page. FLAC does not use this cap;
	/// see <see cref="MaxFlacHeaderPages"/>.
	/// </remarks>
	internal const int MaxHeaderPages = (MaxPacketSize / MaxBytesPerPage) + 3;

	/// <summary>
	/// Pages the Ogg FLAC metadata walk may read before the first audio page.
	/// </summary>
	/// <remarks>
	/// RFC 9639 section 10.1 lets metadata packets span pages. A picture from flac --ogg can use
	/// far more pages than <see cref="MaxHeaderPages"/>. The per-packet size cap still bounds
	/// memory. Reaching this budget before the audio page is a failed walk, not an audio offset.
	/// </remarks>
	internal const int MaxFlacHeaderPages = 10_000;

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
	/// Per RFC 3533, a page has at most 255 lacing values. A lacing value of 255 means the
	/// packet continues, so a packet whose size is a positive multiple of 65025 ends with a
	/// lacing value of 0 on the next page. Pages that do not finish the packet use granule
	/// position -1. The page that finishes the packet uses <paramref name="granulePosition"/>.
	/// </remarks>
	/// <param name="packet">The packet data to split across pages.</param>
	/// <param name="firstPageFlags">Flags for the first page (subsequent pages use Continuation).</param>
	/// <param name="granulePosition">Granule position of the page that finishes the packet.</param>
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

		// Split packet across multiple pages. A full page is 255 lacing values of 255.
		// When that also ends the packet, the terminating 0 lacing does not fit and is
		// emitted afterwards — storing 256 in the segment-count byte would wrap to 0.
		var pages = new List<byte[]> ();
		var packetOffset = 0;
		var sequenceNumber = startSequence;
		var packetFinished = false;

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

			// A page that already holds 255 segments cannot also carry the terminating 0.
			var consumedAllRemaining = packetOffset + pageDataSize >= packet.Length;
			if (consumedAllRemaining && segments.Count < maxSegmentsPerPage) {
				segments.Add ((byte)bytesInPage);
				packetFinished = true;
			} else if (!consumedAllRemaining && bytesInPage > 0)
				segments.Add ((byte)bytesInPage);

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

			// No packet finishes on this page: granule -1. The finishing page uses the caller's granule.
			var gp = packetFinished ? granulePosition : ulong.MaxValue;
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

		if (!packetFinished) {
			// Positive multiple of 65025: the packet ends with a lacing value of 0 on the next page.
			var terminator = BuildOggPage (
				new[] { Array.Empty<byte> () },
				OggPageFlags.Continuation,
				granulePosition,
				serialNumber,
				sequenceNumber);
			pages.Add (terminator);
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
	/// <param name="maxPacketSize">Maximum allowed packet size in bytes. Defaults to 16 MB.</param>
	/// <returns>A result containing the extracted packets, or an error.</returns>
	/// <remarks>
	/// This method reads Ogg pages and reassembles packets that may span multiple pages.
	/// It handles continuation flags and multi-page packets correctly. Reading stops after
	/// <see cref="MaxHeaderPages"/> pages, which covers one <see cref="MaxPacketSize"/> packet,
	/// the page that terminates it, and the one-page headers on either side.
	/// </remarks>
	public static HeaderPacketsResult ExtractHeaderPackets (
		ReadOnlySpan<byte> data,
		int maxPackets,
		bool validateCrc = false,
		int maxPacketSize = MaxPacketSize) =>
		ScanPackets (data, maxPackets, validateCrc, maxPacketSize, stopAtFlacAudio: false, MaxHeaderPages);

	/// <summary>
	/// Reassembles packets and stops at the first packet whose first byte is 0xFF.
	/// </summary>
	/// <param name="data">The binary data to parse.</param>
	/// <param name="maxPackets">Maximum number of packets to extract before stopping.</param>
	/// <param name="validateCrc">Whether to validate CRC checksums.</param>
	/// <param name="maxPacketSize">Maximum allowed packet size in bytes. Defaults to 16 MB.</param>
	/// <returns>
	/// The packets before audio, and <see cref="HeaderPacketsResult.BytesConsumed"/> set to the
	/// file offset of the audio page. The audio packet itself is not returned.
	/// </returns>
	/// <remarks>
	/// RFC 9639 section 10.1 requires the first FLAC audio packet to start on a fresh page.
	/// A FLAC frame sync begins with 0xFF (section 9.1). A continued metadata packet is not
	/// audio, even when a later byte of that packet is 0xFF. Lacing, granule position, and CRC
	/// follow the same rules as <see cref="ExtractHeaderPackets"/>.
	/// </remarks>
	public static HeaderPacketsResult ExtractPacketsUntilAudio (
		ReadOnlySpan<byte> data,
		int maxPackets,
		bool validateCrc = false,
		int maxPacketSize = MaxPacketSize) =>
		ScanPackets (data, maxPackets, validateCrc, maxPacketSize, stopAtFlacAudio: true, MaxFlacHeaderPages);

	static HeaderPacketsResult ScanPackets (
		ReadOnlySpan<byte> data,
		int maxPackets,
		bool validateCrc,
		int maxPacketSize,
		bool stopAtFlacAudio,
		int maxPages)
	{
		var packets = new List<byte[]> ();
		var packetBuffer = new List<byte> ();
		var offset = 0;
		var pageCount = 0;
		uint serialNumber = 0;
		var isFirstPage = true;

		while (offset < data.Length && packets.Count < maxPackets && pageCount < maxPages) {
			var pageStart = offset;
			var pageResult = ReadOggPageWithSegments (data.Slice (offset), validateCrc);
			if (!pageResult.IsSuccess) {
				if (pageCount == 0)
					return HeaderPacketsResult.Failure ($"Invalid Ogg file: {pageResult.Error}");
				if (stopAtFlacAudio && packetBuffer.Count > 0)
					return HeaderPacketsResult.Failure (
						"Ogg FLAC packet continues past a readable page",
						packets, serialNumber, offset);
				// https://xiph.org/ogg/doc/framing.html: a page starts with OggS.
				// Bytes that are not a page are not the audio packet.
				if (stopAtFlacAudio && offset < data.Length)
					return HeaderPacketsResult.Failure (
						$"Ogg FLAC stopped before a readable audio page: {pageResult.Error}",
						packets, serialNumber, offset);
				break;
			}

			// First page must have BOS flag
			if (isFirstPage) {
				if (!pageResult.Page.IsBeginOfStream)
					return HeaderPacketsResult.Failure ("First page must have BOS flag");
				serialNumber = pageResult.Page.SerialNumber;
				isFirstPage = false;
			}

			if (stopAtFlacAudio && pageResult.Page.IsContinuation && packetBuffer.Count == 0)
				return HeaderPacketsResult.Failure (
					"Ogg continuation page has no open packet",
					packets, serialNumber, pageStart);

			if (stopAtFlacAudio && !pageResult.Page.IsContinuation && packetBuffer.Count > 0)
				return HeaderPacketsResult.Failure (
					"Ogg FLAC packet was not finished before the next page",
					packets, serialNumber, pageStart);

			// RFC 9639 section 10.1: the first audio packet begins a fresh page. Do not consume it.
			if (stopAtFlacAudio
				&& !pageResult.Page.IsContinuation
				&& packetBuffer.Count == 0
				&& pageResult.Segments.Count > 0
				&& pageResult.Segments[0].Length > 0
				&& pageResult.Segments[0][0] == 0xFF)
				return HeaderPacketsResult.Success (packets, serialNumber, pageStart);

			offset += pageResult.BytesConsumed;
			pageCount++;

			// Handle continuation from previous page
			var segmentIndex = 0;
			if (pageResult.Page.IsContinuation && packetBuffer.Count > 0) {
				if (pageResult.Segments.Count == 0) {
					if (stopAtFlacAudio)
						return HeaderPacketsResult.Failure (
							"Ogg continuation page has no lacing values",
							packets, serialNumber, pageStart);
				} else {
					if (ExceedsPacketSize (packetBuffer.Count, pageResult.Segments[0].Length, maxPacketSize))
						return PacketTooLarge (stopAtFlacAudio, packets, serialNumber, offset, maxPacketSize);

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
				var segment = pageResult.Segments[i];
				if (stopAtFlacAudio && packetBuffer.Count > 0)
					return HeaderPacketsResult.Failure (
						"Unexpected lacing after an open packet",
						packets, serialNumber, pageStart);

				// A 0xFF packet that does not start this page cannot be spliced as audio.
				if (stopAtFlacAudio && segment.Length > 0 && segment[0] == 0xFF)
					return HeaderPacketsResult.Failure (
						"FLAC audio packet does not begin a fresh page",
						packets, serialNumber, pageStart);

				if (pageResult.IsPacketComplete[i]) {
					if (packetBuffer.Count > 0) {
						if (ExceedsPacketSize (packetBuffer.Count, segment.Length, maxPacketSize))
							return PacketTooLarge (stopAtFlacAudio, packets, serialNumber, offset, maxPacketSize);

						packetBuffer.AddRange (segment);
						packets.Add (packetBuffer.ToArray ());
						packetBuffer.Clear ();
					} else {
						if (segment.Length > maxPacketSize)
							return PacketTooLarge (stopAtFlacAudio, packets, serialNumber, offset, maxPacketSize);

						packets.Add (segment);
					}
				} else {
					// A lacing value of 255 continues this packet. A later segment on the
					// same page is more of that packet, even when the next page forgot
					// the continuation flag.
					if (ExceedsPacketSize (packetBuffer.Count, segment.Length, maxPacketSize))
						return PacketTooLarge (stopAtFlacAudio, packets, serialNumber, offset, maxPacketSize);

					packetBuffer.AddRange (segment);
				}
			}

			if (packetBuffer.Count > maxPacketSize)
				return PacketTooLarge (stopAtFlacAudio, packets, serialNumber, offset, maxPacketSize);
		}

		if (stopAtFlacAudio && packetBuffer.Count > 0)
			return HeaderPacketsResult.Failure (
				"Ogg FLAC packet is missing its terminating lacing value",
				packets, serialNumber, offset);

		// A finished packet is not the audio page. flac --ogg can end a picture on this page
		// and still have a padding block before audio (RFC 9639 section 10.1).
		if (stopAtFlacAudio && offset < data.Length && (pageCount >= maxPages || packets.Count >= maxPackets))
			return HeaderPacketsResult.Failure (
				"Ogg FLAC metadata exceeds the page limit",
				packets, serialNumber, offset);

		return HeaderPacketsResult.Success (packets, serialNumber, offset);
	}

	static bool ExceedsPacketSize (int buffered, int fragmentLength, int maxPacketSize) =>
		(long)buffered + fragmentLength > maxPacketSize;

	static HeaderPacketsResult PacketTooLarge (
		bool keepPackets,
		List<byte[]> packets,
		uint serialNumber,
		int bytesConsumed,
		int maxPacketSize)
	{
		var error = $"Packet size exceeds maximum allowed size of {maxPacketSize / (1024 * 1024)} MB";
		return keepPackets
			? HeaderPacketsResult.Failure (error, packets, serialNumber, bytesConsumed)
			: HeaderPacketsResult.Failure (error);
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

	/// <summary>
	/// Gets the number of bytes consumed from the input.
	/// </summary>
	/// <remarks>
	/// This indicates where the last extracted packet ends, which is useful
	/// for determining where audio data starts after header packets.
	/// </remarks>
	public int BytesConsumed { get; }

	HeaderPacketsResult (bool isSuccess, string? error, IReadOnlyList<byte[]> packets, uint serialNumber, int bytesConsumed)
	{
		IsSuccess = isSuccess;
		Error = error;
		Packets = packets;
		SerialNumber = serialNumber;
		BytesConsumed = bytesConsumed;
	}

	public static HeaderPacketsResult Success (List<byte[]> packets, uint serialNumber, int bytesConsumed) =>
		new (true, null, packets, serialNumber, bytesConsumed);

	public static HeaderPacketsResult Failure (string error) =>
		new (false, error, Array.Empty<byte[]> (), 0, 0);

	public static HeaderPacketsResult Failure (string error, List<byte[]> packets, uint serialNumber, int bytesConsumed) =>
		new (false, error, packets, serialNumber, bytesConsumed);
}
