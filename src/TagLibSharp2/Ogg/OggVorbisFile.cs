// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Ogg;

/// <summary>
/// Represents an Ogg Vorbis audio file with its metadata.
/// </summary>
/// <remarks>
/// <para>
/// Ogg Vorbis files contain Vorbis audio data encapsulated in an Ogg container.
/// The Vorbis stream begins with three required header packets:
/// </para>
/// <list type="number">
/// <item>Identification header (packet type 1) - codec parameters</item>
/// <item>Comment header (packet type 3) - Vorbis Comments metadata</item>
/// <item>Setup header (packet type 5) - codebook data</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/vorbis/doc/Vorbis_I_spec.html
/// </para>
/// </remarks>
public sealed class OggVorbisFile
{
	const int MinVorbisHeaderSize = 7; // 1 byte type + 6 bytes "vorbis"
	static readonly byte[] VorbisMagic = { 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 }; // "vorbis"

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	/// <summary>
	/// Gets or sets the Vorbis Comment block containing metadata tags.
	/// </summary>
	public VorbisComment? VorbisComment { get; set; }

	/// <summary>
	/// Gets the audio properties (duration, bitrate, sample rate, etc.).
	/// </summary>
	/// <remarks>
	/// Duration is calculated from the granule position of the last Ogg page.
	/// If the file cannot be fully scanned, duration will be zero.
	/// </remarks>
	public AudioProperties Properties { get; private set; }

	/// <summary>
	/// Gets or sets the title tag.
	/// </summary>
	public string? Title {
		get => VorbisComment?.Title;
		set => EnsureVorbisComment ().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist tag.
	/// </summary>
	public string? Artist {
		get => VorbisComment?.Artist;
		set => EnsureVorbisComment ().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album tag.
	/// </summary>
	public string? Album {
		get => VorbisComment?.Album;
		set => EnsureVorbisComment ().Album = value;
	}

	/// <summary>
	/// Gets or sets the year tag.
	/// </summary>
	public string? Year {
		get => VorbisComment?.Year;
		set => EnsureVorbisComment ().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre tag.
	/// </summary>
	public string? Genre {
		get => VorbisComment?.Genre;
		set => EnsureVorbisComment ().Genre = value;
	}

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public uint? Track {
		get => VorbisComment?.Track;
		set => EnsureVorbisComment ().Track = value;
	}

	/// <summary>
	/// Gets or sets the comment tag.
	/// </summary>
	public string? Comment {
		get => VorbisComment?.Comment;
		set => EnsureVorbisComment ().Comment = value;
	}

	OggVorbisFile (AudioProperties properties)
	{
		Properties = properties;
	}

	/// <summary>
	/// Attempts to read an Ogg Vorbis file from a file path.
	/// </summary>
	/// <param name="path">The path to the Ogg Vorbis file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums. Defaults to false for performance.</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static OggVorbisFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null, bool validateCrc = false)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return OggVorbisFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!, validateCrc);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Asynchronously attempts to read an Ogg Vorbis file from a file path.
	/// </summary>
	/// <param name="path">The path to the Ogg Vorbis file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums. Defaults to false for performance.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static async Task<OggVorbisFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		bool validateCrc = false,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return OggVorbisFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!, validateCrc);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Attempts to read an Ogg Vorbis file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums. Defaults to false for performance.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static OggVorbisFileReadResult Read (ReadOnlySpan<byte> data, bool validateCrc = false)
	{
		var offset = 0;
		var pageCount = 0;
		var foundIdentification = false;
		var foundComment = false;

		// Audio properties from identification header
		var sampleRate = 0;
		var channels = 0;
		var bitrateNominal = 0;

		// For packet reassembly across pages
		var packetBuffer = new List<byte> ();
		var currentPacketIndex = 0; // 0=ident, 1=comment, 2=setup

		VorbisComment? vorbisComment = null;

		// Read Ogg pages until we find the Vorbis comment header
		while (offset < data.Length && pageCount < 50) { // Limit to prevent infinite loop
			var pageResult = ReadOggPageWithSegments (data.Slice (offset), validateCrc);
			if (!pageResult.IsSuccess) {
				if (pageCount == 0)
					return OggVorbisFileReadResult.Failure ($"Invalid Ogg file: {pageResult.Error}");
				break; // No more valid pages
			}

			offset += pageResult.BytesConsumed;
			pageCount++;

			// First page must be BOS with Vorbis identification header
			if (pageCount == 1) {
				if (!pageResult.Page.IsBeginOfStream)
					return OggVorbisFileReadResult.Failure ("First page must have BOS flag");

				// First packet should be identification header
				if (pageResult.Segments.Count == 0)
					return OggVorbisFileReadResult.Failure ("First page has no segments");

				var firstPacket = pageResult.Segments[0];
				if (!IsVorbisIdentificationHeader (firstPacket))
					return OggVorbisFileReadResult.Failure ("Not a Vorbis stream (expected identification header)");

				// Parse identification header for audio properties
				(sampleRate, channels, bitrateNominal) = ParseIdentificationHeader (firstPacket);

				foundIdentification = true;
				currentPacketIndex = 1; // Next packet will be comment

				// Check if there are more complete packets on this page
				for (var i = 1; i < pageResult.Segments.Count; i++) {
					if (pageResult.IsPacketComplete[i]) {
						// Complete packet on first page - rare but possible
						if (currentPacketIndex == 1) {
							var commentResult = TryParseCommentPacket (pageResult.Segments[i]);
							if (commentResult.IsSuccess) {
								vorbisComment = commentResult.Tag;
								foundComment = true;
							} else if (IsVorbisCommentHeader (pageResult.Segments[i])) {
								// It's a comment header but parsing failed (e.g., invalid framing bit)
								return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
							}
							currentPacketIndex = 2;
						}
					} else {
						// Packet continues to next page
						packetBuffer.AddRange (pageResult.Segments[i]);
					}
				}

				continue;
			}

			// Process subsequent pages
			if (foundIdentification && !foundComment) {
				// Handle continuation from previous page
				var segmentIndex = 0;
				if (pageResult.Page.IsContinuation && packetBuffer.Count > 0) {
					// Continuation of previous packet
					if (pageResult.Segments.Count > 0) {
						packetBuffer.AddRange (pageResult.Segments[0]);
						if (pageResult.IsPacketComplete[0]) {
							// Packet is now complete
							if (currentPacketIndex == 1) {
								var packet = packetBuffer.ToArray ();
								var commentResult = TryParseCommentPacket (packet);
								if (commentResult.IsSuccess) {
									vorbisComment = commentResult.Tag;
									foundComment = true;
								} else if (IsVorbisCommentHeader (packet)) {
									// It's a comment header but parsing failed
									return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
								}
								currentPacketIndex = 2;
							}
							packetBuffer.Clear ();
						}
						segmentIndex = 1;
					}
				}

				// Process remaining segments on this page
				for (var i = segmentIndex; i < pageResult.Segments.Count && !foundComment; i++) {
					if (pageResult.IsPacketComplete[i]) {
						// Complete packet
						if (currentPacketIndex == 1) {
							var commentResult = TryParseCommentPacket (pageResult.Segments[i]);
							if (commentResult.IsSuccess) {
								vorbisComment = commentResult.Tag;
								foundComment = true;
							} else if (IsVorbisCommentHeader (pageResult.Segments[i])) {
								// It's a comment header but parsing failed
								return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
							}
							currentPacketIndex = 2;
						} else {
							currentPacketIndex++;
						}
					} else {
						// Packet continues to next page
						packetBuffer.Clear ();
						packetBuffer.AddRange (pageResult.Segments[i]);
					}
				}

				if (foundComment)
					break;
			}
		}

		if (!foundIdentification)
			return OggVorbisFileReadResult.Failure ("No Vorbis identification header found");

		// Find the last page to get total samples from granule position
		var totalSamples = FindLastGranulePosition (data);

		// Create audio properties
		var properties = AudioProperties.FromVorbis (totalSamples, sampleRate, channels, bitrateNominal);

		// Create the file and set properties
		var file = new OggVorbisFile (properties);
		file.VorbisComment = vorbisComment;

		return OggVorbisFileReadResult.Success (file, offset);
	}

	static VorbisCommentReadResult TryParseCommentPacket (ReadOnlySpan<byte> packet)
	{
		if (!IsVorbisCommentHeader (packet))
			return VorbisCommentReadResult.Failure ("Not a Vorbis comment header");

		// Validate framing bit (last byte must be 1)
		if (packet.Length < MinVorbisHeaderSize + 1)
			return VorbisCommentReadResult.Failure ("Comment header too short for framing bit");

		var framingBit = packet[packet.Length - 1];
		if (framingBit != 1)
			return VorbisCommentReadResult.Failure ($"Invalid framing bit (expected 1, got {framingBit})");

		// Parse the Vorbis comment
		// Skip packet type (1) + "vorbis" (6) = 7 bytes
		// Also skip framing bit at the end (1 byte)
		var commentSpan = packet.Slice (MinVorbisHeaderSize, packet.Length - MinVorbisHeaderSize - 1);
		return VorbisComment.Read (commentSpan);
	}

	static VorbisCommentReadResult TryParseCommentPacket (byte[] packet) =>
		TryParseCommentPacket (packet.AsSpan ());

	/// <summary>
	/// Result of reading an Ogg page with segment information.
	/// </summary>
	readonly struct OggPageWithSegmentsResult
	{
		public OggPage Page { get; }
		public bool IsSuccess { get; }
		public string? Error { get; }
		public int BytesConsumed { get; }
		public List<byte[]> Segments { get; }
		public List<bool> IsPacketComplete { get; }

		public OggPageWithSegmentsResult (OggPage page, bool isSuccess, string? error, int bytesConsumed,
			List<byte[]> segments, List<bool> isPacketComplete)
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
			new (default, false, error, 0, new List<byte[]> (), new List<bool> ());
	}

	/// <summary>
	/// Reads an Ogg page and extracts individual packets based on segment table.
	/// </summary>
	static OggPageWithSegmentsResult ReadOggPageWithSegments (ReadOnlySpan<byte> data, bool validateCrc = false)
	{
		const int minHeaderSize = 27;

		if (data.Length < minHeaderSize)
			return OggPageWithSegmentsResult.Failure ("Data too short");

		// Read basic page structure
		var pageResult = OggPage.Read (data, validateCrc);
		if (!pageResult.IsSuccess)
			return OggPageWithSegmentsResult.Failure (pageResult.Error ?? "Unknown error");

		// Now re-parse to get segment table info
		if (data[0] != 'O' || data[1] != 'g' || data[2] != 'g' || data[3] != 'S')
			return OggPageWithSegmentsResult.Failure ("Invalid Ogg magic");

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
#if NET8_0_OR_GREATER
			// Use CollectionsMarshal for zero-copy append on modern .NET
			var oldCount = currentPacket.Count;
			System.Runtime.InteropServices.CollectionsMarshal.SetCount (currentPacket, oldCount + segmentSize);
			segmentData.CopyTo (System.Runtime.InteropServices.CollectionsMarshal.AsSpan (currentPacket).Slice (oldCount));
#else
			currentPacket.AddRange (segmentData.ToArray ());
#endif

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

	static bool IsVorbisIdentificationHeader (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinVorbisHeaderSize)
			return false;

		// Packet type 1 + "vorbis"
		return data[0] == 1 &&
			data[1] == VorbisMagic[0] && data[2] == VorbisMagic[1] &&
			data[3] == VorbisMagic[2] && data[4] == VorbisMagic[3] &&
			data[5] == VorbisMagic[4] && data[6] == VorbisMagic[5];
	}

	static bool IsVorbisCommentHeader (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinVorbisHeaderSize)
			return false;

		// Packet type 3 + "vorbis"
		return data[0] == 3 &&
			data[1] == VorbisMagic[0] && data[2] == VorbisMagic[1] &&
			data[3] == VorbisMagic[2] && data[4] == VorbisMagic[3] &&
			data[5] == VorbisMagic[4] && data[6] == VorbisMagic[5];
	}

	/// <summary>
	/// Parses the Vorbis identification header for audio properties.
	/// </summary>
	/// <remarks>
	/// Identification header layout (after 7-byte common header):
	/// <code>
	/// Bytes 0-3:   vorbis_version (must be 0)
	/// Byte 4:      audio_channels
	/// Bytes 5-8:   audio_sample_rate (little-endian)
	/// Bytes 9-12:  bitrate_maximum (little-endian, signed)
	/// Bytes 13-16: bitrate_nominal (little-endian, signed)
	/// Bytes 17-20: bitrate_minimum (little-endian, signed)
	/// Byte 21:     blocksize_0 (4 bits) | blocksize_1 (4 bits)
	/// Byte 22:     framing_flag (1 bit)
	/// </code>
	/// </remarks>
	static (int sampleRate, int channels, int bitrateNominal) ParseIdentificationHeader (ReadOnlySpan<byte> data)
	{
		// Minimum size: 7 (common header) + 23 (identification data) = 30 bytes
		if (data.Length < 30)
			return (0, 0, 0);

		// Skip common header (1 byte type + 6 bytes "vorbis")
		var ident = data.Slice (MinVorbisHeaderSize);

		// vorbis_version must be 0
		var version = ident[0] | (ident[1] << 8) | (ident[2] << 16) | (ident[3] << 24);
		if (version != 0)
			return (0, 0, 0);

		var channels = ident[4];
		var sampleRate = ident[5] | (ident[6] << 8) | (ident[7] << 16) | (ident[8] << 24);

		// Bitrate nominal (signed 32-bit little-endian) at offset 13-16 from ident start
		var bitrateNominal = ident[13] | (ident[14] << 8) | (ident[15] << 16) | (ident[16] << 24);

		return (sampleRate, channels, bitrateNominal);
	}

	/// <summary>
	/// Finds the granule position from the last Ogg page to calculate total samples.
	/// </summary>
	/// <remarks>
	/// Scans backwards from the end of the file to find the last valid Ogg page.
	/// The granule position on this page represents the total number of audio samples.
	/// </remarks>
	static ulong FindLastGranulePosition (ReadOnlySpan<byte> data)
	{
		// Scan backwards from the end to find the last page
		// Ogg pages start with "OggS" magic
		// We look for the pattern and validate the page structure

		// Start scanning from near the end (last 64KB should be enough)
		var searchStart = Math.Max (0, data.Length - 65536);

		ulong lastGranulePosition = 0;
		var offset = searchStart;

		while (offset < data.Length - 27) {
			// Look for "OggS" magic
			if (data[offset] == 'O' && data[offset + 1] == 'g' &&
				data[offset + 2] == 'g' && data[offset + 3] == 'S') {

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
				if (granule != 0xFFFFFFFFFFFFFFFF)
					lastGranulePosition = granule;

				// Skip to after this page header to find next page
				var segmentCount = data[offset + 26];
				if (offset + 27 + segmentCount < data.Length) {
					var pageSize = 27 + segmentCount;
					for (var i = 0; i < segmentCount && offset + 27 + i < data.Length; i++)
						pageSize += data[offset + 27 + i];

					offset += pageSize;
					continue;
				}
			}

			offset++;
		}

		return lastGranulePosition;
	}

	VorbisComment EnsureVorbisComment ()
	{
		VorbisComment ??= new VorbisComment ("TagLibSharp2");
		return VorbisComment;
	}

	/// <summary>
	/// Renders the complete Ogg Vorbis file with updated metadata.
	/// </summary>
	/// <param name="originalData">The original file data.</param>
	/// <returns>The rendered file data, or empty if rendering failed.</returns>
	/// <remarks>
	/// The Vorbis stream structure is preserved:
	/// <list type="number">
	/// <item>Identification header (unchanged)</item>
	/// <item>Comment header (rebuilt with current VorbisComment)</item>
	/// <item>Setup header (unchanged)</item>
	/// <item>Audio data (unchanged)</item>
	/// </list>
	/// </remarks>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		// Parse original to extract header packets and find where audio starts
		var headerInfo = ExtractHeaderInfo (originalData);
		if (!headerInfo.IsSuccess)
			return BinaryData.Empty;

		// Build new comment packet
		var commentData = EnsureVorbisComment ().Render ();
		var commentPacket = new byte[7 + commentData.Length + 1];
		commentPacket[0] = 3; // Type 3 = comment
		commentPacket[1] = (byte)'v';
		commentPacket[2] = (byte)'o';
		commentPacket[3] = (byte)'r';
		commentPacket[4] = (byte)'b';
		commentPacket[5] = (byte)'i';
		commentPacket[6] = (byte)'s';
		commentData.Span.CopyTo (commentPacket.AsSpan (7));
		commentPacket[commentPacket.Length - 1] = 1; // Framing bit

		// Build the output
		using var builder = new BinaryDataBuilder ();

		// Page 1: Identification header (BOS)
		var page1 = BuildOggPage (
			[headerInfo.IdentificationPacket],
			OggPageFlags.BeginOfStream,
			0, // Granule position
			headerInfo.SerialNumber,
			0); // Sequence 0
		builder.Add (page1);

		// Page 2: Comment header + Setup header (with proper segment boundaries)
		var page2 = BuildOggPage (
			[commentPacket, headerInfo.SetupPacket],
			OggPageFlags.None,
			0, // Granule position (header pages have 0)
			headerInfo.SerialNumber,
			1); // Sequence 1
		builder.Add (page2);

		// Append remaining audio pages (unchanged)
		if (headerInfo.AudioDataStart < originalData.Length) {
			var audioPages = originalData.Slice (headerInfo.AudioDataStart);
			builder.Add (audioPages);
		}

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var rendered = Render (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render Ogg Vorbis file");

		return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var rendered = Render (originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("Failed to render Ogg Vorbis file"));

		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// This convenience method re-reads the original file data from <see cref="SourcePath"/>
	/// and saves to the specified path. Requires that the file was read using
	/// <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return SaveToFile (path, readResult.Data!, fileSystem);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Header extraction result containing packets and position info.
	/// </summary>
	readonly struct HeaderInfo
	{
		public bool IsSuccess { get; }
		public byte[] IdentificationPacket { get; }
		public byte[] SetupPacket { get; }
		public uint SerialNumber { get; }
		public int AudioDataStart { get; }

		public HeaderInfo (byte[] identPacket, byte[] setupPacket, uint serialNumber, int audioDataStart)
		{
			IsSuccess = true;
			IdentificationPacket = identPacket;
			SetupPacket = setupPacket;
			SerialNumber = serialNumber;
			AudioDataStart = audioDataStart;
		}

		public static HeaderInfo Failure () => new ();
	}

	/// <summary>
	/// Extracts the header packets from the original file.
	/// </summary>
	static HeaderInfo ExtractHeaderInfo (ReadOnlySpan<byte> data)
	{
		byte[]? identPacket = null;
		byte[]? setupPacket = null;
		uint serialNumber = 0;
		var offset = 0;
		var pageCount = 0;
		var packetBuffer = new List<byte> ();
		var currentPacketIndex = 0; // 0=ident, 1=comment, 2=setup

		while (offset < data.Length && pageCount < 50) {
			var pageResult = ReadOggPageWithSegments (data.Slice (offset));
			if (!pageResult.IsSuccess)
				break;

			var pageStart = offset;
			offset += pageResult.BytesConsumed;
			pageCount++;

			if (pageCount == 1) {
				serialNumber = pageResult.Page.SerialNumber;

				if (pageResult.Segments.Count > 0 && IsVorbisIdentificationHeader (pageResult.Segments[0]))
					identPacket = pageResult.Segments[0];
				else
					return HeaderInfo.Failure ();

				currentPacketIndex = 1;

				// Check for additional packets on first page
				for (var i = 1; i < pageResult.Segments.Count; i++) {
					if (pageResult.IsPacketComplete[i]) {
						if (currentPacketIndex == 2 && IsVorbisSetupHeader (pageResult.Segments[i])) {
							setupPacket = pageResult.Segments[i];
							// Found all headers, next pages are audio
							return new HeaderInfo (identPacket!, setupPacket, serialNumber, offset);
						}
						currentPacketIndex++;
					} else {
						packetBuffer.Clear ();
						packetBuffer.AddRange (pageResult.Segments[i]);
					}
				}
				continue;
			}

			// Process subsequent pages looking for setup header
			var segmentIndex = 0;
			if (pageResult.Page.IsContinuation && packetBuffer.Count > 0) {
				if (pageResult.Segments.Count > 0) {
					packetBuffer.AddRange (pageResult.Segments[0]);
					if (pageResult.IsPacketComplete[0]) {
						if (currentPacketIndex == 2) {
							var packet = packetBuffer.ToArray ();
							if (IsVorbisSetupHeader (packet)) {
								setupPacket = packet;
								return new HeaderInfo (identPacket!, setupPacket, serialNumber, offset);
							}
						}
						currentPacketIndex++;
						packetBuffer.Clear ();
					}
					segmentIndex = 1;
				}
			}

			for (var i = segmentIndex; i < pageResult.Segments.Count; i++) {
				if (pageResult.IsPacketComplete[i]) {
					if (currentPacketIndex == 2 && IsVorbisSetupHeader (pageResult.Segments[i])) {
						setupPacket = pageResult.Segments[i];
						return new HeaderInfo (identPacket!, setupPacket, serialNumber, offset);
					}
					currentPacketIndex++;
				} else {
					packetBuffer.Clear ();
					packetBuffer.AddRange (pageResult.Segments[i]);
				}
			}
		}

		// Reached page limit without finding all headers
		return HeaderInfo.Failure ();
	}

	static bool IsVorbisSetupHeader (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinVorbisHeaderSize)
			return false;

		// Packet type 5 + "vorbis"
		return data[0] == 5 &&
			data[1] == VorbisMagic[0] && data[2] == VorbisMagic[1] &&
			data[3] == VorbisMagic[2] && data[4] == VorbisMagic[3] &&
			data[5] == VorbisMagic[4] && data[6] == VorbisMagic[5];
	}

	static bool IsVorbisSetupHeader (byte[] data) =>
		IsVorbisSetupHeader (data.AsSpan ());

	/// <summary>
	/// Builds an Ogg page from multiple packets with proper segment boundaries.
	/// </summary>
	static byte[] BuildOggPage (byte[][] packets, OggPageFlags flags, ulong granulePosition,
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
}

/// <summary>
/// Represents the result of reading an <see cref="OggVorbisFile"/> from binary data.
/// </summary>
public readonly struct OggVorbisFileReadResult : IEquatable<OggVorbisFileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public OggVorbisFile? File { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	OggVorbisFileReadResult (OggVorbisFile? file, string? error, int bytesConsumed)
	{
		File = file;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="file">The parsed file.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static OggVorbisFileReadResult Success (OggVorbisFile file, int bytesConsumed) =>
		new (file, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static OggVorbisFileReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (OggVorbisFileReadResult other) =>
		ReferenceEquals (File, other.File) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is OggVorbisFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (OggVorbisFileReadResult left, OggVorbisFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (OggVorbisFileReadResult left, OggVorbisFileReadResult right) =>
		!left.Equals (right);
}
