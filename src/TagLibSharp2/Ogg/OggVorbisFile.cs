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
	static readonly byte[] VorbisMagic = [(byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];

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
		// Extract the first 2 header packets (identification and comment)
		// We don't need setup header for metadata reading
		var packetsResult = OggPageHelper.ExtractHeaderPackets (data, maxPackets: 2, validateCrc);
		if (!packetsResult.IsSuccess)
			return OggVorbisFileReadResult.Failure (packetsResult.Error!);

		if (packetsResult.Packets.Count < 1)
			return OggVorbisFileReadResult.Failure ("No packets found in Ogg stream");

		// Packet 0: Vorbis identification header
		var identPacket = packetsResult.Packets[0];
		if (!IsVorbisIdentificationHeader (identPacket))
			return OggVorbisFileReadResult.Failure ("Not a Vorbis stream (expected identification header)");

		var (sampleRate, channels, bitrateNominal) = ParseIdentificationHeader (identPacket);

		// Packet 1: Vorbis comment header
		VorbisComment? vorbisComment = null;
		if (packetsResult.Packets.Count >= 2) {
			var commentPacket = packetsResult.Packets[1];
			if (IsVorbisCommentHeader (commentPacket)) {
				var commentResult = TryParseCommentPacket (commentPacket);
				if (!commentResult.IsSuccess)
					return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
				vorbisComment = commentResult.Tag;
			}
		}

		// Find the last page to get total samples from granule position
		// Use shared helper which properly finds EOS page per RFC 3533
		var totalSamples = OggPageHelper.FindLastGranulePosition (data);

		// Create audio properties
		var properties = AudioProperties.FromVorbis (totalSamples, sampleRate, channels, bitrateNominal);

		// Create the file and set properties
		var file = new OggVorbisFile (properties) {
			VorbisComment = vorbisComment
		};

		return OggVorbisFileReadResult.Success (file, data.Length);
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
		// Uses shared OggPageHelper which validates segment table limits per RFC 3533
		var page1 = OggPageHelper.BuildOggPage (
			new[] { headerInfo.IdentificationPacket },
			OggPageFlags.BeginOfStream,
			0, // Granule position
			headerInfo.SerialNumber,
			0); // Sequence 0
		builder.Add (page1);

		// Pages 2+: Comment header (may span multiple pages) + Setup header
		// Try to fit comment and setup on one page; if comment is too large, split across pages
		var combinedSegments = CalculateSegmentsNeeded (commentPacket.Length) + CalculateSegmentsNeeded (headerInfo.SetupPacket.Length);
		uint nextSequence;

		if (combinedSegments <= 255) {
			// Both packets fit on one page
			var page2 = OggPageHelper.BuildOggPage (
				new[] { commentPacket, headerInfo.SetupPacket },
				OggPageFlags.None,
				0, // Granule position (header pages have 0)
				headerInfo.SerialNumber,
				1); // Sequence 1
			builder.Add (page2);
			nextSequence = 2;
		} else {
			// Comment is too large - split it, then add setup on its own page
			var (commentPages, seqAfterComment) = OggPageHelper.BuildMultiPagePacket (
				commentPacket,
				OggPageFlags.None,
				0,
				headerInfo.SerialNumber,
				1);
			foreach (var page in commentPages)
				builder.Add (page);

			// Setup packet on its own page
			var setupPage = OggPageHelper.BuildOggPage (
				new[] { headerInfo.SetupPacket },
				OggPageFlags.None,
				0,
				headerInfo.SerialNumber,
				seqAfterComment);
			builder.Add (setupPage);
			nextSequence = seqAfterComment + 1;
		}

		// Renumber and fix audio pages (sequence numbers + EOS flag)
		if (headerInfo.AudioDataStart < originalData.Length) {
			var audioPages = originalData.Slice (headerInfo.AudioDataStart);
			var fixedAudio = OggPageHelper.RenumberAudioPages (audioPages, headerInfo.SerialNumber, startSequence: nextSequence);
			builder.Add (fixedAudio);
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
	/// Asynchronously saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return await SaveToFileAsync (path, readResult.Data!, fileSystem, cancellationToken)
			.ConfigureAwait (false);
	}

	/// <summary>
	/// Asynchronously saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Calculates the number of Ogg segments needed for a packet of the given size.
	/// </summary>
	/// <remarks>
	/// Per the Ogg specification, segment values of 255 signal continuation.
	/// A segment &lt; 255 bytes signals packet end. Therefore:
	/// - A 254-byte packet needs 1 segment (value 254)
	/// - A 255-byte packet needs 2 segments (255, then 0 to signal end)
	/// - A 256-byte packet needs 2 segments (255, then 1)
	/// The formula works out to (length / 255) + 1 for all non-zero lengths.
	/// </remarks>
	static int CalculateSegmentsNeeded (int packetLength)
	{
		if (packetLength == 0)
			return 1; // Empty packet needs one 0-byte segment

		// Both cases (remainder == 0 and remainder != 0) require segments + 1:
		// - remainder == 0: need extra 0-byte segment to signal packet end
		// - remainder != 0: need one more segment for the remaining bytes
		return (packetLength / 255) + 1;
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
	/// <remarks>
	/// Uses the shared <see cref="OggPageHelper.ExtractHeaderPackets"/> for packet reassembly.
	/// Vorbis has 3 header packets: identification (0), comment (1), setup (2).
	/// Audio data starts after the page containing the setup header.
	/// </remarks>
	static HeaderInfo ExtractHeaderInfo (ReadOnlySpan<byte> data)
	{
		// Extract all 3 header packets: identification, comment, setup
		// BytesConsumed gives us where audio data starts
		var packetsResult = OggPageHelper.ExtractHeaderPackets (data, maxPackets: 3);
		if (!packetsResult.IsSuccess || packetsResult.Packets.Count < 3)
			return HeaderInfo.Failure ();

		var identPacket = packetsResult.Packets[0];
		if (!IsVorbisIdentificationHeader (identPacket))
			return HeaderInfo.Failure ();

		// Skip packet 1 (comment) - we rebuild it
		var setupPacket = packetsResult.Packets[2];
		if (!IsVorbisSetupHeader (setupPacket))
			return HeaderInfo.Failure ();

		return new HeaderInfo (identPacket, setupPacket, packetsResult.SerialNumber, packetsResult.BytesConsumed);
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
