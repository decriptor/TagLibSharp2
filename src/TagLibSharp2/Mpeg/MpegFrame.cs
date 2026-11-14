// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mpeg;

/// <summary>
/// Represents an MPEG audio frame header.
/// </summary>
/// <remarks>
/// <para>
/// MPEG audio frame header is 4 bytes (32 bits):
/// </para>
/// <list type="bullet">
/// <item>Bits 0-10: Frame sync (11 ones)</item>
/// <item>Bits 11-12: MPEG version</item>
/// <item>Bits 13-14: Layer</item>
/// <item>Bit 15: Protection (0=CRC, 1=none)</item>
/// <item>Bits 16-19: Bitrate index</item>
/// <item>Bits 20-21: Sample rate index</item>
/// <item>Bit 22: Padding</item>
/// <item>Bit 23: Private</item>
/// <item>Bits 24-25: Channel mode</item>
/// <item>Bits 26-27: Mode extension</item>
/// <item>Bit 28: Copyright</item>
/// <item>Bit 29: Original</item>
/// <item>Bits 30-31: Emphasis</item>
/// </list>
/// </remarks>
public sealed class MpegFrame
{
	/// <summary>
	/// The size of an MPEG frame header in bytes.
	/// </summary>
	public const int HeaderSize = 4;

	// Bitrate tables indexed by [version][layer][bitrateIndex]
	// Version: 0=V1, 1=V2/V2.5
	// Layer: 0=L1, 1=L2, 2=L3
	// All values in kbps, 0 = free, -1 = invalid
	static readonly int[][] BitrateTableV1 = [
		// Layer 1
		[0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, -1],
		// Layer 2
		[0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, -1],
		// Layer 3
		[0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, -1]
	];

	static readonly int[][] BitrateTableV2 = [
		// Layer 1
		[0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, -1],
		// Layer 2
		[0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, -1],
		// Layer 3
		[0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, -1]
	];

	// Sample rate tables indexed by [version][sampleRateIndex]
	static readonly int[] SampleRateTableV1 = [44100, 48000, 32000];
	static readonly int[] SampleRateTableV2 = [22050, 24000, 16000];
	static readonly int[] SampleRateTableV25 = [11025, 12000, 8000];

	// Samples per frame indexed by [version][layer]
	// Version: 0=V1, 1=V2/V2.5
	// Layer: 0=L1, 1=L2, 2=L3
	// MPEG1: L1=384, L2=1152, L3=1152
	// MPEG2/2.5: L1=384, L2=1152, L3=576
	static readonly int[] SamplesPerFrameV1 = [384, 1152, 1152];
	static readonly int[] SamplesPerFrameV2 = [384, 1152, 576];

	/// <summary>
	/// Gets the MPEG version.
	/// </summary>
	public MpegVersion Version { get; }

	/// <summary>
	/// Gets the MPEG layer.
	/// </summary>
	public MpegLayer Layer { get; }

	/// <summary>
	/// Gets the bitrate in kbps.
	/// </summary>
	public int Bitrate { get; }

	/// <summary>
	/// Gets the sample rate in Hz.
	/// </summary>
	public int SampleRate { get; }

	/// <summary>
	/// Gets the channel mode.
	/// </summary>
	public ChannelMode ChannelMode { get; }

	/// <summary>
	/// Gets a value indicating whether this frame has CRC protection.
	/// </summary>
	public bool HasCrc { get; }

	/// <summary>
	/// Gets a value indicating whether this frame has padding.
	/// </summary>
	public bool HasPadding { get; }

	/// <summary>
	/// Gets the number of samples per frame.
	/// </summary>
	public int SamplesPerFrame { get; }

	/// <summary>
	/// Gets the frame size in bytes (including header).
	/// </summary>
	public int FrameSize { get; }

	MpegFrame (MpegVersion version, MpegLayer layer, int bitrate, int sampleRate,
		ChannelMode channelMode, bool hasCrc, bool hasPadding)
	{
		Version = version;
		Layer = layer;
		Bitrate = bitrate;
		SampleRate = sampleRate;
		ChannelMode = channelMode;
		HasCrc = hasCrc;
		HasPadding = hasPadding;

		// Calculate samples per frame
		var samplesTable = version == MpegVersion.Version1 ? SamplesPerFrameV1 : SamplesPerFrameV2;
		var layerIndex = layer switch {
			MpegLayer.Layer1 => 0,
			MpegLayer.Layer2 => 1,
			MpegLayer.Layer3 => 2,
			_ => 0
		};
		SamplesPerFrame = samplesTable[layerIndex];

		// Calculate frame size
		FrameSize = CalculateFrameSize (version, layer, bitrate, sampleRate, hasPadding);
	}

	/// <summary>
	/// Attempts to parse an MPEG frame header from binary data.
	/// </summary>
	/// <param name="data">The binary data containing the frame.</param>
	/// <param name="offset">The offset at which to start parsing.</param>
	/// <param name="frame">The parsed frame, or null if parsing failed.</param>
	/// <returns>True if parsing succeeded, false otherwise.</returns>
	public static bool TryParse (BinaryData data, int offset, out MpegFrame? frame)
	{
		frame = null;

		if (data.Length < offset + HeaderSize)
			return false;

		var span = data.Span.Slice (offset, HeaderSize);

		// Check sync word (11 bits = 0xFF + upper 3 bits of second byte)
		if (span[0] != 0xFF || (span[1] & 0xE0) != 0xE0)
			return false;

		// Extract version (bits 11-12)
		var versionBits = (span[1] >> 3) & 0x03;
		var version = (MpegVersion)versionBits;
		if (version == MpegVersion.Invalid)
			return false;

		// Extract layer (bits 13-14)
		var layerBits = (span[1] >> 1) & 0x03;
		var layer = (MpegLayer)layerBits;
		if (layer == MpegLayer.Invalid)
			return false;

		// Extract protection bit (bit 15)
		var hasCrc = (span[1] & 0x01) == 0;

		// Extract bitrate index (bits 16-19)
		var bitrateIndex = (span[2] >> 4) & 0x0F;
		var bitrateTable = version == MpegVersion.Version1 ? BitrateTableV1 : BitrateTableV2;
		var layerIndex = layer switch {
			MpegLayer.Layer1 => 0,
			MpegLayer.Layer2 => 1,
			MpegLayer.Layer3 => 2,
			_ => 0
		};
		var bitrate = bitrateTable[layerIndex][bitrateIndex];
		if (bitrate < 0)
			return false; // Invalid bitrate index

		// Extract sample rate index (bits 20-21)
		var sampleRateIndex = (span[2] >> 2) & 0x03;
		if (sampleRateIndex == 3)
			return false; // Reserved

		var sampleRateTable = version switch {
			MpegVersion.Version1 => SampleRateTableV1,
			MpegVersion.Version2 => SampleRateTableV2,
			MpegVersion.Version25 => SampleRateTableV25,
			_ => SampleRateTableV1
		};
		var sampleRate = sampleRateTable[sampleRateIndex];

		// Extract padding bit (bit 22)
		var hasPadding = ((span[2] >> 1) & 0x01) == 1;

		// Extract channel mode (bits 24-25)
		var channelModeBits = (span[3] >> 6) & 0x03;
		var channelMode = (ChannelMode)channelModeBits;

		frame = new MpegFrame (version, layer, bitrate, sampleRate, channelMode, hasCrc, hasPadding);
		return true;
	}

	/// <summary>
	/// Calculates the size of an MPEG frame based on its parameters.
	/// </summary>
	/// <remarks>
	/// ISO 11172-3 / 13818-3: frame bytes = samplesPerFrame / 8 * bitrate / sampleRate + padding.
	/// Layer I is (12 * bitrate / sampleRate + padding) * 4. Layer II is 144 on every version
	/// (1152 samples). Layer III MPEG-1 is 144 (1152 samples). Layer III MPEG-2 and MPEG-2.5
	/// is 72 (576 samples). Padding is 1 byte for layer II/III.
	/// </remarks>
	static int CalculateFrameSize (MpegVersion version, MpegLayer layer, int bitrate, int sampleRate, bool hasPadding)
	{
		if (bitrate == 0 || sampleRate == 0)
			return 0;

		int paddingSize;
		int coefficient;

		switch (layer) {
			case MpegLayer.Layer1:
				// Layer 1: frame size = (12 * bitrate / sampleRate + padding) * 4
				return ((12 * bitrate * 1000 / sampleRate) + (hasPadding ? 1 : 0)) * 4;

			case MpegLayer.Layer2:
			case MpegLayer.Layer3:
				// 72 only for Layer III when the version is not MPEG-1.
				paddingSize = hasPadding ? 1 : 0;
				coefficient = layer == MpegLayer.Layer3 && version != MpegVersion.Version1 ? 72 : 144;
				return (coefficient * bitrate * 1000 / sampleRate) + paddingSize;

			default:
				return 0;
		}
	}

	/// <summary>
	/// Gets the offset within a frame where the Xing header might be located.
	/// </summary>
	/// <remarks>
	/// The Xing header is located after the side information in an MPEG frame.
	/// The side information size depends on version and channel mode.
	/// </remarks>
	public int XingHeaderOffset {
		get {
			// Side info size:
			// MPEG1 Stereo/JointStereo/DualChannel: 32 bytes
			// MPEG1 Mono: 17 bytes
			// MPEG2/2.5 Stereo/JointStereo/DualChannel: 17 bytes
			// MPEG2/2.5 Mono: 9 bytes
			var sideInfoSize = Version == MpegVersion.Version1
				? (ChannelMode == ChannelMode.Mono ? 17 : 32)
				: (ChannelMode == ChannelMode.Mono ? 9 : 17);

			// Header (4) + CRC (2 if present) + side info
			var crcSize = HasCrc ? 2 : 0;
			return HeaderSize + crcSize + sideInfoSize;
		}
	}

	/// <summary>
	/// The fixed offset where a VBRI header would be located (32 bytes after frame header).
	/// </summary>
	public const int VbriHeaderOffset = HeaderSize + 32;
}
