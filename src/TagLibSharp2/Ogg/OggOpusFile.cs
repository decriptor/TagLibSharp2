// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Ogg;

/// <summary>
/// Represents an Ogg Opus audio file with its metadata.
/// </summary>
/// <remarks>
/// <para>
/// Ogg Opus files contain Opus audio data encapsulated in an Ogg container.
/// The Opus stream begins with two required header packets:
/// </para>
/// <list type="number">
/// <item>OpusHead - identification header with codec parameters</item>
/// <item>OpusTags - Vorbis Comment metadata (without framing bit)</item>
/// </list>
/// <para>
/// Unlike Vorbis, Opus always outputs at 48kHz regardless of the original sample rate.
/// The pre-skip value indicates samples to skip at the start for encoder delay compensation.
/// </para>
/// <para>
/// Reference: RFC 6716 (Opus codec), RFC 7845 (Ogg Opus encapsulation)
/// </para>
/// </remarks>
public sealed class OggOpusFile : IMediaFile
{
	const int OpusHeadMinSize = 19; // Minimum size for OpusHead header
	bool _disposed;

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;
	long _fileSize;

	/// <summary>
	/// Gets or sets the Vorbis Comment block containing metadata tags.
	/// </summary>
	/// <remarks>
	/// Opus uses the same Vorbis Comment format as Ogg Vorbis files,
	/// but with "OpusTags" magic and no framing bit.
	/// </remarks>
	public VorbisComment? VorbisComment { get; set; }

	/// <summary>
	/// Gets the audio properties (duration, bitrate, sample rate, etc.).
	/// </summary>
	/// <remarks>
	/// Duration is calculated from the granule position of the last Ogg page,
	/// minus the pre-skip samples. Opus always outputs at 48kHz.
	/// </remarks>
	public AudioProperties Properties { get; private set; }

	/// <summary>
	/// Gets the pre-skip sample count from the OpusHead header.
	/// </summary>
	/// <remarks>
	/// This indicates the number of samples to skip at the start of the stream
	/// to compensate for encoder delay. Already factored into duration calculation.
	/// </remarks>
	public ushort PreSkip { get; private set; }

	/// <summary>
	/// Gets the original input sample rate from the OpusHead header.
	/// </summary>
	/// <remarks>
	/// This is informational only. Opus always outputs at 48kHz regardless
	/// of the original sample rate. A value of 0 indicates "unspecified".
	/// </remarks>
	public uint InputSampleRate { get; private set; }

	/// <summary>
	/// Gets the output gain from the OpusHead header.
	/// </summary>
	/// <remarks>
	/// Q7.8 fixed-point value representing the gain adjustment in dB.
	/// Should be applied to the decoder output before playing.
	/// To convert to decibels, use <see cref="OutputGainDb"/>.
	/// </remarks>
	public short OutputGain { get; private set; }

	/// <summary>
	/// Gets the output gain in decibels (converted from Q7.8 fixed-point).
	/// </summary>
	/// <remarks>
	/// This value should be applied to decoder output. Positive values increase volume.
	/// If both header gain and R128_TRACK_GAIN tags are present, header gain takes precedence per RFC 7845.
	/// </remarks>
	public double OutputGainDb => OutputGain / 256.0;

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

	/// <inheritdoc />
	public Tag? Tag => VorbisComment;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties.IsValid ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Opus;

	OggOpusFile (AudioProperties properties)
	{
		Properties = properties;
	}

	/// <summary>
	/// Attempts to read an Ogg Opus file from a file path.
	/// </summary>
	/// <param name="path">The path to the Ogg Opus file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums. Defaults to false for performance.</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static OggOpusFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null, bool validateCrc = false)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return OggOpusFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span, validateCrc);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
			result.File._fileSize = readResult.Data!.Value.Length;
		}
		return result;
	}

	/// <summary>
	/// Asynchronously attempts to read an Ogg Opus file from a file path.
	/// </summary>
	/// <param name="path">The path to the Ogg Opus file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums. Defaults to false for performance.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static async Task<OggOpusFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		bool validateCrc = false,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return OggOpusFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span, validateCrc);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
			result.File._fileSize = readResult.Data!.Value.Length;
		}
		return result;
	}

	/// <summary>
	/// Attempts to read an Ogg Opus file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums. Defaults to false for performance.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static OggOpusFileReadResult Read (ReadOnlySpan<byte> data, bool validateCrc = false)
	{
		// Extract the first 2 header packets (OpusHead and OpusTags)
		var packetsResult = OggPageHelper.ExtractHeaderPackets (data, maxPackets: 2, validateCrc);
		if (!packetsResult.IsSuccess)
			return OggOpusFileReadResult.Failure (packetsResult.Error!);

		if (packetsResult.Packets.Count < 1)
			return OggOpusFileReadResult.Failure ("Invalid Ogg Opus file: no packets found");

		// Packet 0: OpusHead identification header
		var opusHeadPacket = packetsResult.Packets[0];
		if (!IsOpusHead (opusHeadPacket))
			return OggOpusFileReadResult.Failure ("Invalid Ogg Opus file: not an Opus stream (expected OpusHead)");

		var headResult = ParseOpusHead (opusHeadPacket);
		if (!headResult.IsSuccess)
			return OggOpusFileReadResult.Failure (headResult.Error!);

		var channels = headResult.Channels;
		var preSkip = headResult.PreSkip;
		var inputSampleRate = headResult.InputSampleRate;
		var outputGain = headResult.OutputGain;

		// Packet 1: OpusTags (Vorbis Comment)
		VorbisComment? vorbisComment = null;
		if (packetsResult.Packets.Count >= 2) {
			var opusTagsPacket = packetsResult.Packets[1];
			if (IsOpusTags (opusTagsPacket)) {
				var commentResult = TryParseOpusTags (opusTagsPacket);
				if (!commentResult.IsSuccess)
					return OggOpusFileReadResult.Failure (commentResult.Error ?? "Failed to parse OpusTags header");
				vorbisComment = commentResult.Tag;
			}
		}

		// Find the last page to get total samples from granule position
		var granulePosition = OggPageHelper.FindLastGranulePosition (data);

		// Create audio properties (Opus always outputs at 48kHz)
		var properties = AudioProperties.FromOpus (granulePosition, preSkip, inputSampleRate, channels, data.Length);

		// Create the file and set properties
		var file = new OggOpusFile (properties) {
			VorbisComment = vorbisComment,
			PreSkip = preSkip,
			InputSampleRate = inputSampleRate,
			OutputGain = outputGain,
			_fileSize = data.Length
		};

		return OggOpusFileReadResult.Success (file, data.Length);
	}

	/// <summary>
	/// Attempts to read an Ogg Opus file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out OggOpusFile? file, bool validateCrc = false)
	{
		var result = Read (data, validateCrc);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read an Ogg Opus file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <param name="validateCrc">Whether to validate CRC-32 checksums.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out OggOpusFile? file, bool validateCrc = false) =>
		TryRead (data.Span, out file, validateCrc);

	/// <summary>
	/// Checks if the data appears to be a valid Ogg Opus file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with OggS and contains an OpusHead identification header.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 36 bytes: OggS header (27) + segment table (1) + OpusHead (8)
		if (data.Length < 36)
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
		if (data.Length < packetOffset + 8)
			return false;

		// Check for OpusHead magic: "OpusHead"
		return data[packetOffset] == 'O' &&
			   data[packetOffset + 1] == 'p' &&
			   data[packetOffset + 2] == 'u' &&
			   data[packetOffset + 3] == 's' &&
			   data[packetOffset + 4] == 'H' &&
			   data[packetOffset + 5] == 'e' &&
			   data[packetOffset + 6] == 'a' &&
			   data[packetOffset + 7] == 'd';
	}

	/// <summary>
	/// Result of parsing OpusHead header.
	/// </summary>
	readonly struct OpusHeadResult
	{
		public bool IsSuccess { get; }
		public string? Error { get; }
		public int Channels { get; }
		public ushort PreSkip { get; }
		public uint InputSampleRate { get; }
		public short OutputGain { get; }

		OpusHeadResult (bool isSuccess, string? error, int channels, ushort preSkip, uint inputSampleRate, short outputGain)
		{
			IsSuccess = isSuccess;
			Error = error;
			Channels = channels;
			PreSkip = preSkip;
			InputSampleRate = inputSampleRate;
			OutputGain = outputGain;
		}

		public static OpusHeadResult Success (int channels, ushort preSkip, uint inputSampleRate, short outputGain) =>
			new (true, null, channels, preSkip, inputSampleRate, outputGain);

		public static OpusHeadResult Failure (string error) =>
			new (false, error, 0, 0, 0, 0);
	}

	/// <summary>
	/// Parses the OpusHead identification header.
	/// </summary>
	/// <remarks>
	/// OpusHead format (RFC 7845 Section 5.1):
	/// <code>
	/// Offset Size  Description
	/// 0      8     Magic "OpusHead"
	/// 8      1     Version (0-15 accepted per RFC 7845 §5.1.1.1)
	/// 9      1     Channel count (1-255)
	/// 10     2     Pre-skip (little-endian, samples at 48kHz)
	/// 12     4     Input sample rate (little-endian, informational)
	/// 16     2     Output gain (little-endian, Q7.8 dB)
	/// 18     1     Channel mapping family
	/// 19+    var   Channel mapping table (if family > 0)
	/// </code>
	/// </remarks>
	static OpusHeadResult ParseOpusHead (ReadOnlySpan<byte> data)
	{
		if (data.Length < OpusHeadMinSize)
			return OpusHeadResult.Failure ("OpusHead too short");

		// Skip magic (8 bytes)
		// RFC 7845 §5.1.1.1: Major version in high nibble (must be 0), minor in low nibble
		// Decoders MUST reject version >= 16, SHOULD accept 0-15 treating as version 1
		var version = data[8];
		if (version > 15)
			return OpusHeadResult.Failure ($"Unsupported Opus version {version}. Per RFC 7845, versions 0-15 are accepted; version {version} has major version {version >> 4} which is not supported");

		var channels = data[9];
		if (channels == 0)
			return OpusHeadResult.Failure ("Invalid channel count: 0");

		var preSkip = (ushort)(data[10] | (data[11] << 8));
		var inputSampleRate = (uint)(data[12] | (data[13] << 8) | (data[14] << 16) | (data[15] << 24));
		var outputGain = (short)(data[16] | (data[17] << 8));

		var channelMappingFamily = data[18];

		// Channel mapping family 0: mono or stereo, no mapping table
		// Family 1: Vorbis channel order (max 8 channels), mapping table present
		// Family 255: Discrete channels, mapping table present
		if (channelMappingFamily == 0) {
			// RFC 7845 §5.1.1.2: Family 0 only allows 1 or 2 channels
			if (channels > 2)
				return OpusHeadResult.Failure ($"Invalid channel count {channels} for mapping family 0. Per RFC 7845, only 1 or 2 channels are allowed");
		} else if (channelMappingFamily == 1) {
			// RFC 7845 §5.1.1.2: Family 1 (Vorbis order) allows 1-8 channels
			if (channels < 1 || channels > 8)
				return OpusHeadResult.Failure ($"Invalid channel count {channels} for mapping family 1. Per RFC 7845, family 1 allows 1-8 channels");
			// Validate mapping table is present: stream count + coupled count + channel mapping
			var expectedSize = 19 + 2 + channels; // header + stream counts + mapping
			if (data.Length < expectedSize)
				return OpusHeadResult.Failure ("OpusHead too short for channel mapping table");

			// RFC 7845 §5.1.1.2: Validate stream count and coupled count
			var streamCount = data[19];
			var coupledCount = data[20];
			if (streamCount == 0)
				return OpusHeadResult.Failure ("Invalid stream count: 0. Per RFC 7845 §5.1.1.2, stream count must be > 0");
			if (coupledCount > streamCount)
				return OpusHeadResult.Failure ($"Invalid coupled count {coupledCount} > stream count {streamCount}. Per RFC 7845 §5.1.1.2, coupled count must be <= stream count");
		} else if (channelMappingFamily == 255) {
			// RFC 7845 §5.1.1.2: Family 255 allows discrete channels with no defined order
			// Validate mapping table is present: stream count + coupled count + channel mapping
			var expectedSize = 19 + 2 + channels; // header + stream counts + mapping
			if (data.Length < expectedSize)
				return OpusHeadResult.Failure ("OpusHead too short for channel mapping table");

			// RFC 7845 §5.1.1.2: Validate stream count and coupled count
			var streamCount = data[19];
			var coupledCount = data[20];
			if (streamCount == 0)
				return OpusHeadResult.Failure ("Invalid stream count: 0. Per RFC 7845 §5.1.1.2, stream count must be > 0");
			if (coupledCount > streamCount)
				return OpusHeadResult.Failure ($"Invalid coupled count {coupledCount} > stream count {streamCount}. Per RFC 7845 §5.1.1.2, coupled count must be <= stream count");
		} else {
			// RFC 7845 §5.1.1.2: Families 2-254 are reserved and SHOULD NOT be used
			// Reject these to ensure forward compatibility and spec compliance
			return OpusHeadResult.Failure ($"Unsupported channel mapping family {channelMappingFamily}. RFC 7845 reserves families 2-254.");
		}

		return OpusHeadResult.Success (channels, preSkip, inputSampleRate, outputGain);
	}

	static VorbisCommentReadResult TryParseOpusTags (ReadOnlySpan<byte> packet)
	{
		if (!IsOpusTags (packet))
			return VorbisCommentReadResult.Failure ("Not an OpusTags header");

		// OpusTags: "OpusTags" (8 bytes) + Vorbis Comment data (NO framing bit!)
		if (packet.Length < 8)
			return VorbisCommentReadResult.Failure ("OpusTags header too short");

		// Parse the Vorbis comment portion (skip "OpusTags" magic)
		var commentSpan = packet.Slice (8);
		return VorbisComment.Read (commentSpan);
	}

	static VorbisCommentReadResult TryParseOpusTags (byte[] packet) =>
		TryParseOpusTags (packet.AsSpan ());

	static bool IsOpusHead (ReadOnlySpan<byte> data) =>
		data.Length >= 8 && data[..8].SequenceEqual ("OpusHead"u8);

	static bool IsOpusTags (ReadOnlySpan<byte> data) =>
		data.Length >= 8 && data[..8].SequenceEqual ("OpusTags"u8);

	VorbisComment EnsureVorbisComment ()
	{
		VorbisComment ??= new VorbisComment ("TagLibSharp2");
		return VorbisComment;
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		VorbisComment = null;
		_sourceFileSystem = null;
		SourcePath = null;
		_disposed = true;
	}

	/// <summary>
	/// Renders the complete Ogg Opus file with updated metadata.
	/// </summary>
	/// <param name="originalData">The original file data.</param>
	/// <returns>The rendered file data, or empty if rendering failed.</returns>
	/// <remarks>
	/// The Opus stream structure is preserved:
	/// <list type="number">
	/// <item>OpusHead (unchanged)</item>
	/// <item>OpusTags (rebuilt with current VorbisComment)</item>
	/// <item>Audio data (unchanged)</item>
	/// </list>
	/// </remarks>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		// Parse original to extract header packets and find where audio starts
		var headerInfo = ExtractHeaderInfo (originalData);
		if (!headerInfo.IsSuccess)
			return BinaryData.Empty;

		// Build new OpusTags packet (no framing bit!)
		var commentData = EnsureVorbisComment ().Render ();
		var tagsPacket = new byte[8 + commentData.Length];
		tagsPacket[0] = (byte)'O';
		tagsPacket[1] = (byte)'p';
		tagsPacket[2] = (byte)'u';
		tagsPacket[3] = (byte)'s';
		tagsPacket[4] = (byte)'T';
		tagsPacket[5] = (byte)'a';
		tagsPacket[6] = (byte)'g';
		tagsPacket[7] = (byte)'s';
		commentData.Span.CopyTo (tagsPacket.AsSpan (8));
		// Note: NO framing bit for Opus (unlike Vorbis)

		// Build the output
		using var builder = new BinaryDataBuilder ();

		// Page 1: OpusHead (BOS)
		var page1 = OggPageHelper.BuildOggPage (
			[headerInfo.OpusHeadPacket],
			OggPageFlags.BeginOfStream,
			0, // Granule position (header pages have 0)
			headerInfo.SerialNumber,
			0); // Sequence 0
		builder.Add (page1);

		// Page 2+: OpusTags (comment header) - may span multiple pages for large metadata
		var (tagsPages, nextSequence) = OggPageHelper.BuildMultiPagePacket (
			tagsPacket,
			OggPageFlags.None,
			0, // Granule position (header pages have 0)
			headerInfo.SerialNumber,
			1); // Start at sequence 1
		foreach (var page in tagsPages)
			builder.Add (page);

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
			return FileWriteResult.Failure ("Failed to render Ogg Opus file");

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
			return Task.FromResult (FileWriteResult.Failure ("Failed to render Ogg Opus file"));

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

		return SaveToFile (path, readResult.Data!.Value.Span, fileSystem);
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

		return await SaveToFileAsync (path, readResult.Data!.Value, fileSystem, cancellationToken)
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
	/// Header extraction result containing packets and position info.
	/// </summary>
	readonly struct HeaderInfo
	{
		public bool IsSuccess { get; }
		public byte[] OpusHeadPacket { get; }
		public uint SerialNumber { get; }
		public int AudioDataStart { get; }

		HeaderInfo (byte[] opusHeadPacket, uint serialNumber, int audioDataStart)
		{
			IsSuccess = true;
			OpusHeadPacket = opusHeadPacket;
			SerialNumber = serialNumber;
			AudioDataStart = audioDataStart;
		}

		public static HeaderInfo Success (byte[] opusHeadPacket, uint serialNumber, int audioDataStart) =>
			new (opusHeadPacket, serialNumber, audioDataStart);

		public static HeaderInfo Failure () => new ();
	}

	/// <summary>
	/// Extracts the header packets from the original file.
	/// </summary>
	/// <remarks>
	/// Uses the shared <see cref="OggPageHelper.ExtractHeaderPackets"/> for packet reassembly.
	/// Audio data starts after the page containing OpusTags (packet 1).
	/// </remarks>
	static HeaderInfo ExtractHeaderInfo (ReadOnlySpan<byte> data)
	{
		// Extract OpusHead (packet 0) and OpusTags (packet 1)
		// BytesConsumed gives us where audio data starts
		var packetsResult = OggPageHelper.ExtractHeaderPackets (data, maxPackets: 2);
		if (!packetsResult.IsSuccess || packetsResult.Packets.Count < 1)
			return HeaderInfo.Failure ();

		var opusHeadPacket = packetsResult.Packets[0];
		if (!IsOpusHead (opusHeadPacket))
			return HeaderInfo.Failure ();

		return HeaderInfo.Success (opusHeadPacket, packetsResult.SerialNumber, packetsResult.BytesConsumed);
	}
}

/// <summary>
/// Represents the result of reading an <see cref="OggOpusFile"/> from binary data.
/// </summary>
public readonly struct OggOpusFileReadResult : IEquatable<OggOpusFileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public OggOpusFile? File { get; }

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

	OggOpusFileReadResult (OggOpusFile? file, string? error, int bytesConsumed)
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
	public static OggOpusFileReadResult Success (OggOpusFile file, int bytesConsumed) =>
		new (file, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static OggOpusFileReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (OggOpusFileReadResult other) =>
		ReferenceEquals (File, other.File) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is OggOpusFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (OggOpusFileReadResult left, OggOpusFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (OggOpusFileReadResult left, OggOpusFileReadResult right) =>
		!left.Equals (right);
}
