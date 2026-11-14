// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

#pragma warning disable CA2000 // Dispose objects before losing scope - factory method pattern

namespace TagLibSharp2.Ogg;

/// <summary>
/// Represents the result of parsing an Ogg FLAC file.
/// </summary>
public readonly struct OggFlacFileReadResult : IEquatable<OggFlacFileReadResult>
{
	/// <summary>
	/// Gets the parsed Ogg FLAC file, or null if parsing failed.
	/// </summary>
	public OggFlacFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	private OggFlacFileReadResult (OggFlacFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="file">The parsed Ogg FLAC file.</param>
	/// <returns>A successful result containing the file.</returns>
	public static OggFlacFileReadResult Success (OggFlacFile file) => new (file, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static OggFlacFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (OggFlacFileReadResult other) =>
		Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is OggFlacFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (OggFlacFileReadResult left, OggFlacFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (OggFlacFileReadResult left, OggFlacFileReadResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents an Ogg FLAC (.oga/.ogg) file.
/// </summary>
public sealed class OggFlacFile : IMediaFile
{
	private const int MinHeaderSize = 27; // Minimum Ogg page header

	// RFC 9639 section 8.1: the length after the 4-byte header is 24 bits. Do not wrap it.
	private const int MaxFlacBlockDataLength = 0xFFFFFF;
	private const int MaxFlacMetadataPacketSize = 4 + MaxFlacBlockDataLength;

	private static readonly byte[] OggMagic = "OggS"u8.ToArray ();
	private static readonly byte[] FlacMagic = "FLAC"u8.ToArray ();

	private byte[] _originalData = Array.Empty<byte> ();
	private string? _sourcePath;
	private IFileSystem? _sourceFileSystem;
	private readonly List<FlacMetadataBlock> _metadataBlocks = new ();
	private bool _disposed;

	private OggFlacFile () { }

	/// <summary>Sample rate in Hz</summary>
	public int SampleRate { get; private set; }

	/// <summary>Number of audio channels</summary>
	public int Channels { get; private set; }

	/// <summary>Bits per sample</summary>
	public int BitsPerSample { get; private set; }

	/// <summary>Total number of samples</summary>
	public ulong TotalSamples { get; private set; }

	/// <summary>Audio properties</summary>
	public AudioProperties Properties { get; private set; }

	/// <summary>Vorbis Comment metadata (null if not present)</summary>
	public VorbisComment? VorbisComment { get; private set; }

	/// <summary>
	/// Returns the payload of the first preserved metadata block of <paramref name="type"/>,
	/// excluding the block header. Pictures are type 6.
	/// </summary>
	internal bool TryGetMetadataBlock (byte type, out byte[] data)
	{
		foreach (var block in _metadataBlocks) {
			if (block.Type == type) {
				data = block.Data;
				return true;
			}
		}

		data = Array.Empty<byte> ();
		return false;
	}

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath => _sourcePath;

	/// <inheritdoc />
	public Tag? Tag => VorbisComment;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is { IsValid: true } ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.OggFlac;

	/// <summary>
	/// Parse an Ogg FLAC file from byte data.
	/// </summary>
	public static OggFlacFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinHeaderSize)
			return OggFlacFileReadResult.Failure ("Invalid Ogg FLAC file: data too short for header");

		// Verify Ogg magic
		if (!data[..4].SequenceEqual (OggMagic))
			return OggFlacFileReadResult.Failure ("Invalid Ogg FLAC file: missing Ogg magic");

		// Find first packet and verify it's FLAC
		var firstPacketResult = ExtractFirstPacket (data);
		if (!firstPacketResult.IsSuccess)
			return OggFlacFileReadResult.Failure (firstPacketResult.Error!);

		var firstPacket = firstPacketResult.Data!;

		// Verify FLAC stream marker: 0x7F "FLAC"
		if (firstPacket.Length < 9 || firstPacket[0] != 0x7F)
			return OggFlacFileReadResult.Failure ("Invalid Ogg FLAC file: missing 0x7F marker");

		if (!firstPacket.AsSpan (1, 4).SequenceEqual (FlacMagic))
			return OggFlacFileReadResult.Failure ("Invalid Ogg FLAC file: missing FLAC magic");

		var file = new OggFlacFile ();

		// Parse FLAC mapping header
		// [0] 0x7F
		// [1-4] "FLAC"
		// [5] Major version
		// [6] Minor version
		// [7-8] Number of header packets (big-endian)
		// [9-12] "fLaC" (native FLAC marker)
		// [13+] STREAMINFO block

		if (firstPacket.Length < 13 + 4 + 34) // 13 + block header + STREAMINFO
			return OggFlacFileReadResult.Failure ("FLAC header too short for STREAMINFO");

		// Verify native FLAC marker
		if (!firstPacket.AsSpan (9, 4).SequenceEqual ("fLaC"u8))
			return OggFlacFileReadResult.Failure ("Missing native FLAC marker");

		// Parse STREAMINFO block
		var blockOffset = 13;
		var blockHeader = firstPacket[blockOffset];
		var blockType = blockHeader & 0x7F;

		if (blockType != 0) // Type 0 = STREAMINFO
			return OggFlacFileReadResult.Failure ($"Expected STREAMINFO block, got type {blockType}");

		var blockSize = (firstPacket[blockOffset + 1] << 16) |
						(firstPacket[blockOffset + 2] << 8) |
						firstPacket[blockOffset + 3];

		if (blockSize != 34)
			return OggFlacFileReadResult.Failure ($"Invalid STREAMINFO size: {blockSize}");

		var streamInfoOffset = blockOffset + 4;
		ParseStreamInfo (firstPacket.AsSpan (streamInfoOffset, 34), file);

		// Store original data
		file._originalData = data.ToArray ();

		// Calculate audio properties
		file.CalculateProperties ();

		// Parse Vorbis Comment from subsequent Ogg pages
		file.ParseVorbisComment (data);

		return OggFlacFileReadResult.Success (file);
	}

	/// <summary>
	/// Attempts to read an Ogg FLAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out OggFlacFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read an Ogg FLAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out OggFlacFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid Ogg FLAC file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with OggS and contains a FLAC mapping header.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 33 bytes: OggS header (27) + segment table (1) + FLAC header (5)
		if (data.Length < 33)
			return false;

		// Check "OggS" magic
		if (data[0] != 'O' || data[1] != 'g' || data[2] != 'g' || data[3] != 'S')
			return false;

		// Get segment count (at offset 26)
		var segmentCount = data[26];
		if (segmentCount == 0)
			return false;

		// First packet starts after header (27) + segment table (segmentCount)
		var packetOffset = 27 + segmentCount;
		if (data.Length < packetOffset + 5)
			return false;

		// Check for FLAC mapping header: 0x7F + "FLAC"
		return data[packetOffset] == 0x7F &&
			   data[packetOffset + 1] == 'F' &&
			   data[packetOffset + 2] == 'L' &&
			   data[packetOffset + 3] == 'A' &&
			   data[packetOffset + 4] == 'C';
	}

	private static void ParseStreamInfo (ReadOnlySpan<byte> data, OggFlacFile file)
	{
		// STREAMINFO layout (34 bytes):
		// [0-1] Min block size
		// [2-3] Max block size
		// [4-6] Min frame size
		// [7-9] Max frame size
		// [10-17] Sample rate (20 bits) + channels-1 (3 bits) + bits-1 (5 bits) + total samples (36 bits)
		// [18-33] MD5 signature

		// Read the packed 64-bit field at offset 10
		ulong packed = BinaryPrimitives.ReadUInt64BigEndian (data.Slice (10, 8));

		// Extract fields
		file.SampleRate = (int)((packed >> 44) & 0xFFFFF);
		file.Channels = (int)(((packed >> 41) & 0x7) + 1);
		file.BitsPerSample = (int)(((packed >> 36) & 0x1F) + 1);
		file.TotalSamples = packed & 0xFFFFFFFFF;
	}

	private void CalculateProperties ()
	{
		if (SampleRate <= 0 || TotalSamples == 0) {
			Properties = default;
			return;
		}

		var durationSeconds = (double)TotalSamples / SampleRate;
		var duration = TimeSpan.FromSeconds (durationSeconds);

		var bitrate = 0;
		if (_originalData.Length > 0 && durationSeconds > 0) {
			bitrate = (int)(_originalData.Length * 8 / durationSeconds / 1000);
		}

		Properties = new AudioProperties (
			duration,
			bitrate,
			SampleRate,
			BitsPerSample,
			Channels,
			"FLAC"
		);
	}

	private void ParseVorbisComment (ReadOnlySpan<byte> data)
	{
		_metadataBlocks.Clear ();
		var walk = WalkPackets (data);

		// A later page can fail after a comment or block was already closed. Keep those.
		// A walk that fails before any of them finishes leaves the comment unset.
		if (walk.Comment is not null)
			VorbisComment = walk.Comment;
		_metadataBlocks.AddRange (walk.Blocks);
	}

	private static ExtractPacketResult ExtractFirstPacket (ReadOnlySpan<byte> data)
	{
		if (data.Length < 27)
			return ExtractPacketResult.Failure ("File too short");

		// Check for BOS flag
		var flags = data[5];
		if ((flags & 0x02) == 0)
			return ExtractPacketResult.Failure ("First page is not beginning of stream");

		var numSegments = data[26];
		var segmentTableEnd = 27 + numSegments;

		if (data.Length < segmentTableEnd)
			return ExtractPacketResult.Failure ("File too short for segment table");

		// Calculate packet size from segment table
		var packetSize = 0;
		for (int i = 0; i < numSegments; i++) {
			packetSize += data[27 + i];
		}

		if (data.Length < segmentTableEnd + packetSize)
			return ExtractPacketResult.Failure ("File too short for packet data");

		var packet = data.Slice (segmentTableEnd, packetSize).ToArray ();
		return ExtractPacketResult.Success (packet);
	}

	/// <summary>
	/// Ensures a Vorbis Comment exists, creating one if necessary.
	/// </summary>
	public VorbisComment EnsureVorbisComment ()
	{
		VorbisComment ??= new VorbisComment ();
		return VorbisComment;
	}

	/// <summary>
	/// Removes the Vorbis Comment.
	/// </summary>
	public void RemoveVorbisComment ()
	{
		VorbisComment = null;
	}

	/// <summary>
	/// Renders the file to a byte array.
	/// </summary>
	/// <returns>
	/// The rendered file, or an empty array when packets cannot be reassembled or a metadata
	/// block is longer than the 24-bit length field.
	/// </returns>
	public byte[] Render (ReadOnlySpan<byte> originalData)
	{
		// Do not splice metadata in front of a continuation we failed to reassemble.
		var headerInfo = ExtractHeaderInfo (originalData);
		if (!headerInfo.IsSuccess)
			return Array.Empty<byte> ();

		// Build list of metadata blocks to write. Reject before replacing any bytes:
		// SaveToFile treats an empty render as failure.
		var blocksToWrite = new List<(byte type, byte[] data)> ();

		if (VorbisComment is not null) {
			var commentData = VorbisComment.Render ();
			if (commentData.Length > MaxFlacBlockDataLength)
				return Array.Empty<byte> ();
			blocksToWrite.Add ((4, commentData.ToArray ()));
		}

		// Preserved blocks (PICTURE, CUESHEET, SEEKTABLE, etc.) use the same 24-bit length.
		foreach (var block in _metadataBlocks) {
			if (block.Data.Length > MaxFlacBlockDataLength)
				return Array.Empty<byte> ();
			blocksToWrite.Add ((block.Type, block.Data));
		}

		using var builder = new MemoryStream ();

		// Page 1: FLAC header packet (BOS)
		var page1 = OggPageHelper.BuildOggPage (
			new[] { headerInfo.HeaderPacket! },
			OggPageFlags.BeginOfStream,
			0,
			headerInfo.SerialNumber,
			0);
		builder.Write (page1, 0, page1.Length);

		uint nextSequence = 1;

		// Write each metadata block as a separate Ogg page.
		// Length bytes come from FlacMetadataBlockHeader, which refuses a value above 24 bits.
		for (int i = 0; i < blocksToWrite.Count; i++) {
			var (blockType, blockData) = blocksToWrite[i];
			var isLastBlock = (i == blocksToWrite.Count - 1);
			var blockHeader = new FlacMetadataBlockHeader (isLastBlock, (FlacBlockType)blockType, blockData.Length);
			var headerBytes = blockHeader.Render ();
			var packet = new byte[headerBytes.Length + blockData.Length];
			headerBytes.Span.CopyTo (packet);
			blockData.CopyTo (packet, headerBytes.Length);

			// A picture larger than 255 Ogg segments must span pages. BuildOggPage throws
			// past that limit. Vorbis and Opus already use the multi-page helper.
			var (pages, next) = OggPageHelper.BuildMultiPagePacket (
				packet,
				OggPageFlags.None,
				0,
				headerInfo.SerialNumber,
				nextSequence);
			foreach (var page in pages)
				builder.Write (page, 0, page.Length);
			nextSequence = next;
		}

		// Remaining pages: Audio data (renumbered)
		if (headerInfo.AudioDataStart < originalData.Length) {
			var audioPages = originalData.Slice (headerInfo.AudioDataStart);
			var fixedAudio = OggPageHelper.RenumberAudioPages (audioPages, headerInfo.SerialNumber, startSequence: nextSequence);
			builder.Write (fixedAudio, 0, fixedAudio.Length);
		}

		return builder.ToArray ();
	}

	private static OggFlacHeaderInfo ExtractHeaderInfo (ReadOnlySpan<byte> data)
	{
		var walk = WalkPackets (data);
		if (!walk.IsSuccess || walk.HeaderPacket is null)
			return OggFlacHeaderInfo.Failure (walk.Error ?? "Failed to reassemble Ogg FLAC packets");

		return OggFlacHeaderInfo.Success (walk.HeaderPacket, walk.SerialNumber, walk.AudioDataStart);
	}

	/// <summary>
	/// Reassembles Ogg packets before FLAC metadata is parsed.
	/// A lacing value of 255 continues a packet; a shorter value, including 0, ends it.
	/// Audio starts at the first packet whose first byte is 0xFF, which begins a fresh page.
	/// </summary>
	private static PacketWalkResult WalkPackets (ReadOnlySpan<byte> data)
	{
		const int maxPacketSize = MaxFlacMetadataPacketSize;
		const int maxPages = 10000;

		var blocks = new List<FlacMetadataBlock> ();
		var buffer = new List<byte> ();
		byte[]? headerPacket = null;
		VorbisComment? comment = null;
		uint serialNumber = 0;
		var open = false;
		var offset = 0;
		var audioStart = -1;
		var pageCount = 0;
		string? error = null;

		while (error is null && audioStart < 0 && offset + 27 <= data.Length) {
			var pageResult = OggPageHelper.ReadOggPageWithSegments (data.Slice (offset));
			if (!pageResult.IsSuccess) {
				if (open)
					error = "Ogg FLAC packet continues past a readable page";
				else if (headerPacket is null)
					error = pageResult.Error ?? "Failed to read Ogg page";
				else
					audioStart = offset;
				break;
			}

			pageCount++;
			if (pageCount > maxPages) {
				error = "Ogg FLAC metadata exceeds the page limit";
				break;
			}

			if (offset == 0)
				serialNumber = pageResult.Page.SerialNumber;

			var pageOffset = offset;
			if (pageResult.Page.IsContinuation) {
				if (!open) {
					error = "Ogg continuation page has no open packet";
					break;
				}

				if (pageResult.Segments.Count == 0) {
					error = "Ogg continuation page has no lacing values";
					break;
				}

				if (!AppendFragment (buffer, pageResult.Segments[0], maxPacketSize, out error))
					break;

				if (pageResult.IsPacketComplete[0]) {
					if (!ClosePacket (buffer, blocks, ref headerPacket, ref comment, ref open, out error))
						break;
				}

				if (!ReadNewPackets (pageResult, 1, pageOffset, buffer, blocks, ref headerPacket, ref comment, ref open, ref audioStart, maxPacketSize, ref error))
					break;
			} else if (open) {
				error = "Ogg FLAC packet was not finished before the next page";
				break;
			} else if (!ReadNewPackets (pageResult, 0, pageOffset, buffer, blocks, ref headerPacket, ref comment, ref open, ref audioStart, maxPacketSize, ref error)) {
				break;
			}

			offset += pageResult.BytesConsumed;
		}

		if (error is null && open)
			error = "Ogg FLAC packet is missing its terminating lacing value";

		if (error is not null)
			return PacketWalkResult.Failure (error, comment, blocks);

		if (headerPacket is null)
			return PacketWalkResult.Failure ("Missing Ogg FLAC header packet");

		if (audioStart < 0)
			audioStart = offset;

		return PacketWalkResult.Success (headerPacket, serialNumber, audioStart, comment, blocks);
	}

	private static bool ReadNewPackets (
		OggPageWithSegmentsResult pageResult,
		int startIndex,
		int pageOffset,
		List<byte> buffer,
		List<FlacMetadataBlock> blocks,
		ref byte[]? headerPacket,
		ref VorbisComment? comment,
		ref bool open,
		ref int audioStart,
		int maxPacketSize,
		ref string? error)
	{
		for (var i = startIndex; i < pageResult.Segments.Count; i++) {
			if (open) {
				error = "Unexpected lacing after an open packet";
				return false;
			}

			var segment = pageResult.Segments[i];
			if (segment.Length > 0 && segment[0] == 0xFF) {
				// The first audio packet begins a fresh page. A continuation, or a packet
				// that follows metadata on the same page, cannot be spliced safely.
				if (pageResult.Page.IsContinuation || i != 0) {
					error = "FLAC audio packet does not begin a fresh page";
					return false;
				}

				audioStart = pageOffset;
				return true;
			}

			buffer.Clear ();
			if (!AppendFragment (buffer, segment, maxPacketSize, out error))
				return false;

			open = !pageResult.IsPacketComplete[i];
			if (!open && !ClosePacket (buffer, blocks, ref headerPacket, ref comment, ref open, out error))
				return false;
		}

		return true;
	}

	private static bool AppendFragment (List<byte> buffer, byte[] fragment, int maxPacketSize, out string? error)
	{
		if ((long)buffer.Count + fragment.Length > maxPacketSize) {
			error = "Ogg FLAC packet exceeds the maximum size";
			return false;
		}

		OggPageHelper.AppendSpanToList (buffer, fragment);
		error = null;
		return true;
	}

	private static bool ClosePacket (
		List<byte> buffer,
		List<FlacMetadataBlock> blocks,
		ref byte[]? headerPacket,
		ref VorbisComment? comment,
		ref bool open,
		out string? error)
	{
		var packet = buffer.ToArray ();
		buffer.Clear ();
		open = false;
		error = null;

		if (headerPacket is null) {
			if (packet.Length == 0) {
				error = "Empty Ogg FLAC header packet";
				return false;
			}

			headerPacket = packet;
			return true;
		}

		if (packet.Length == 0)
			return true;

		if (packet.Length < 4) {
			error = "Ogg FLAC metadata packet is shorter than a block header";
			return false;
		}

		var blockType = (byte)(packet[0] & 0x7F);
		var blockSize = (packet[1] << 16) | (packet[2] << 8) | packet[3];
		if (blockSize < 0 || packet.Length < 4 + blockSize) {
			error = "Ogg FLAC metadata block extends outside its packet";
			return false;
		}

		if (blockType == 4) {
			var commentResult = VorbisComment.Read (packet.AsSpan (4, blockSize));
			if (commentResult.IsSuccess)
				comment = commentResult.Tag;
		} else if (blockType != 0) {
			var blockData = new byte[blockSize];
			Array.Copy (packet, 4, blockData, 0, blockSize);
			blocks.Add (new FlacMetadataBlock (blockType, blockData));
		}

		return true;
	}

	private readonly struct OggFlacHeaderInfo
	{
		public bool IsSuccess { get; }
		public byte[]? HeaderPacket { get; }
		public uint SerialNumber { get; }
		public int AudioDataStart { get; }
		public string? Error { get; }

		private OggFlacHeaderInfo (bool success, byte[]? headerPacket, uint serialNumber, int audioStart, string? error)
		{
			IsSuccess = success;
			HeaderPacket = headerPacket;
			SerialNumber = serialNumber;
			AudioDataStart = audioStart;
			Error = error;
		}

		public static OggFlacHeaderInfo Success (byte[] headerPacket, uint serialNumber, int audioStart)
			=> new (true, headerPacket, serialNumber, audioStart, null);

		public static OggFlacHeaderInfo Failure (string error)
			=> new (false, null, 0, 0, error);
	}

	private readonly struct PacketWalkResult
	{
		public bool IsSuccess { get; }
		public string? Error { get; }
		public byte[]? HeaderPacket { get; }
		public uint SerialNumber { get; }
		public int AudioDataStart { get; }
		public VorbisComment? Comment { get; }
		public List<FlacMetadataBlock> Blocks { get; }

		private PacketWalkResult (bool success, string? error, byte[]? headerPacket, uint serialNumber, int audioStart, VorbisComment? comment, List<FlacMetadataBlock> blocks)
		{
			IsSuccess = success;
			Error = error;
			HeaderPacket = headerPacket;
			SerialNumber = serialNumber;
			AudioDataStart = audioStart;
			Comment = comment;
			Blocks = blocks;
		}

		public static PacketWalkResult Success (byte[] headerPacket, uint serialNumber, int audioStart, VorbisComment? comment, List<FlacMetadataBlock> blocks)
			=> new (true, null, headerPacket, serialNumber, audioStart, comment, blocks);

		public static PacketWalkResult Failure (string error, VorbisComment? comment = null, List<FlacMetadataBlock>? blocks = null)
			=> new (false, error, null, 0, 0, comment, blocks ?? new List<FlacMetadataBlock> ());
	}

	// ===== File I/O =====

	/// <summary>
	/// Read an Ogg FLAC file from disk.
	/// </summary>
	public static OggFlacFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return OggFlacFileReadResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!._sourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Read an Ogg FLAC file from disk asynchronously.
	/// </summary>
	public static async Task<OggFlacFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return OggFlacFileReadResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!._sourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Save the file to a new path using the provided original data.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="originalData">The original file data containing audio content.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The result of the write operation.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (originalData);
		if (rendered.Length == 0)
			return FileWriteResult.Failure ("Failed to render Ogg FLAC file");

		return AtomicFileWriter.Write (path, rendered, fs);
	}

	/// <summary>
	/// Save the file to a new path using internally stored data.
	/// </summary>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null) =>
		SaveToFile (path, _originalData, fileSystem);

	/// <summary>
	/// Save the file back to its source path.
	/// </summary>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (_sourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead.");

		return SaveToFile (_sourcePath!, fileSystem);
	}

	/// <summary>
	/// Save the file asynchronously using the provided original data.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="originalData">The original file data containing audio content.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The result of the write operation.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (originalData.Span);
		if (rendered.Length == 0)
			return FileWriteResult.Failure ("Failed to render Ogg FLAC file");

		return await AtomicFileWriter.WriteAsync (path, rendered, fs, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Save the file asynchronously using internally stored data.
	/// </summary>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default) =>
		SaveToFileAsync (path, _originalData, fileSystem, cancellationToken);

	/// <summary>
	/// Save the file back to its source path asynchronously.
	/// </summary>
	public async Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (_sourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFileAsync(path) instead.");

		return await SaveToFileAsync (_sourcePath!, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	private readonly struct ExtractPacketResult
	{
		public bool IsSuccess { get; }
		public byte[]? Data { get; }
		public string? Error { get; }

		private ExtractPacketResult (byte[]? data, string? error)
		{
			Data = data;
			Error = error;
			IsSuccess = data is not null;
		}

		public static ExtractPacketResult Success (byte[] data) => new (data, null);
		public static ExtractPacketResult Failure (string error) => new (null, error);
	}

	/// <summary>
	/// Releases resources held by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		VorbisComment = null;
		Properties = default;
		_originalData = Array.Empty<byte> ();
		_sourcePath = null;
		_sourceFileSystem = null;
		_metadataBlocks.Clear ();
		_disposed = true;
	}

	private readonly struct FlacMetadataBlock
	{
		/// <summary>Block type (0=STREAMINFO, 1=PADDING, 2=APPLICATION, 3=SEEKTABLE, 4=VORBIS_COMMENT, 5=CUESHEET, 6=PICTURE)</summary>
		public byte Type { get; }

		/// <summary>Raw block data (without the 4-byte header)</summary>
		public byte[] Data { get; }

		public FlacMetadataBlock (byte type, byte[] data)
		{
			Type = type;
			Data = data;
		}
	}
}
