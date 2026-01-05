// Ogg FLAC (.oga/.ogg) file support
// Ogg encapsulation of FLAC audio with Vorbis Comment metadata
// Spec: https://xiph.org/flac/ogg_mapping.html

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
		// Search for metadata blocks in subsequent pages/packets
		// Also preserve non-VorbisComment blocks (PICTURE, CUESHEET, etc.)
		_metadataBlocks.Clear ();

		var offset = 0;
		while (offset < data.Length - 27) {
			// Skip to next Ogg page
			if (!data.Slice (offset, 4).SequenceEqual (OggMagic)) {
				offset++;
				continue;
			}

			// Get number of segments
			var numSegments = data[offset + 26];
			var segmentTableEnd = offset + 27 + numSegments;

			if (segmentTableEnd >= data.Length)
				break;

			// Calculate packet start
			var packetStart = segmentTableEnd;

			// Check if this packet contains a FLAC metadata block
			if (packetStart < data.Length) {
				var blockHeader = data[packetStart];
				var blockType = (byte)(blockHeader & 0x7F);

				// Only process known metadata block types (0-6)
				if (blockType <= 6) {
					var blockSizeOffset = packetStart + 1;
					if (blockSizeOffset + 3 < data.Length) {
						var blockSize = (data[blockSizeOffset] << 16) |
										(data[blockSizeOffset + 1] << 8) |
										data[blockSizeOffset + 2];

						var blockDataStart = blockSizeOffset + 3;
						if (blockDataStart + blockSize <= data.Length) {
							var blockData = data.Slice (blockDataStart, blockSize);

							if (blockType == 4) // VORBIS_COMMENT
							{
								var result = VorbisComment.Read (blockData);
								if (result.IsSuccess) {
									VorbisComment = result.Tag;
								}
							} else if (blockType != 0) // Don't store STREAMINFO (it's in the header packet)
							  {
								// Preserve other blocks (PADDING, APPLICATION, SEEKTABLE, CUESHEET, PICTURE)
								_metadataBlocks.Add (new FlacMetadataBlock (blockType, blockData.ToArray ()));
							}
						}
					}
				}
			}

			// Move to next page
			var pageSize = CalculatePageSize (data, offset);
			if (pageSize <= 0)
				break;
			offset += pageSize;
		}
	}

	private static int CalculatePageSize (ReadOnlySpan<byte> data, int offset)
	{
		if (offset + 27 > data.Length)
			return -1;

		int numSegments = data[offset + 26];
		if (offset + 27 + numSegments > data.Length)
			return -1;

		// Sum segment sizes (max 255 segments * 255 bytes = 65,025 bytes per page)
		var dataSize = 0;
		for (int i = 0; i < numSegments; i++) {
			dataSize += data[offset + 27 + i];
		}

		return 27 + numSegments + dataSize;
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
	public byte[] Render (ReadOnlySpan<byte> originalData)
	{
		// Extract header info from original data
		var headerInfo = ExtractHeaderInfo (originalData);
		if (!headerInfo.IsSuccess)
			return originalData.ToArray ();

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

		// Build list of metadata blocks to write
		var blocksToWrite = new List<(byte type, byte[] data)> ();

		// Add VorbisComment if present
		if (VorbisComment is not null) {
			var commentData = VorbisComment.Render ();
			blocksToWrite.Add ((4, commentData.ToArray ()));
		}

		// Add preserved metadata blocks (PICTURE, CUESHEET, SEEKTABLE, etc.)
		foreach (var block in _metadataBlocks) {
			blocksToWrite.Add ((block.Type, block.Data));
		}

		// Write each metadata block as a separate Ogg page
		for (int i = 0; i < blocksToWrite.Count; i++) {
			var (blockType, blockData) = blocksToWrite[i];
			var isLastBlock = (i == blocksToWrite.Count - 1);

			// Build FLAC metadata block: type (1) + size (3) + data
			// Set last-block flag (0x80) only on the final block
			var packet = new byte[4 + blockData.Length];
			packet[0] = (byte)(blockType | (isLastBlock ? 0x80 : 0x00));
			packet[1] = (byte)((blockData.Length >> 16) & 0xFF);
			packet[2] = (byte)((blockData.Length >> 8) & 0xFF);
			packet[3] = (byte)(blockData.Length & 0xFF);
			blockData.CopyTo (packet, 4);

			var page = OggPageHelper.BuildOggPage (
				new[] { packet },
				OggPageFlags.None,
				0,
				headerInfo.SerialNumber,
				nextSequence);
			builder.Write (page, 0, page.Length);
			nextSequence++;
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
		if (data.Length < 27)
			return OggFlacHeaderInfo.Failure ("Data too short");

		// Read first page to get header packet
		var pageResult = OggPageHelper.ReadOggPageWithSegments (data);
		if (!pageResult.IsSuccess || pageResult.Segments.Count == 0)
			return OggFlacHeaderInfo.Failure ("Failed to read first page");

		// Combine segments into header packet
		var headerPacket = CombineSegments (pageResult.Segments);
		var serialNumber = pageResult.Page.SerialNumber;
		var pageSize = pageResult.BytesConsumed;

		// Find where audio data starts (after header and comment pages)
		var offset = pageSize;
		while (offset < data.Length - 27) {
			if (!data.Slice (offset, 4).SequenceEqual (OggMagic))
				break;

			var nextPageResult = OggPageHelper.ReadOggPageWithSegments (data.Slice (offset));
			if (!nextPageResult.IsSuccess)
				break;

			// Check if this page contains audio data (EOS flag or page after metadata)
			// For FLAC in Ogg, audio pages have granule position > 0 typically
			if (nextPageResult.Segments.Count > 0 && nextPageResult.Segments[0].Length > 0) {
				var firstByte = nextPageResult.Segments[0][0];
				// FLAC metadata blocks have type in lower 7 bits (0-6)
				// Audio frames start with sync code 0xFF (or continuation)
				if ((firstByte & 0x7F) > 6)
					break; // This is audio data
			}

			offset += nextPageResult.BytesConsumed;
		}

		return OggFlacHeaderInfo.Success (headerPacket, serialNumber, offset);
	}

	private static byte[] CombineSegments (IReadOnlyList<byte[]> segments)
	{
		var totalSize = 0;
		for (int i = 0; i < segments.Count; i++)
			totalSize += segments[i].Length;

		var result = new byte[totalSize];
		var offset = 0;
		for (int i = 0; i < segments.Count; i++) {
			segments[i].CopyTo (result, offset);
			offset += segments[i].Length;
		}
		return result;
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
