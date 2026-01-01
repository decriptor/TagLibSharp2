// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Parses the Elementary Stream Descriptor (esds) box for AAC audio.
/// </summary>
/// <remarks>
/// <para>
/// The esds box contains decoder configuration for MPEG-4 audio streams,
/// including AAC. It uses a hierarchical descriptor structure defined
/// in ISO/IEC 14496-1 (MPEG-4 Systems).
/// </para>
/// <para>
/// Structure:
/// - ES Descriptor (tag 0x03)
///   - Decoder Config Descriptor (tag 0x04)
///     - Audio Specific Config (raw bytes)
///   - SL Config Descriptor (tag 0x06)
/// </para>
/// <para>
/// Reference: ISO/IEC 14496-14 §5.6.1
/// </para>
/// </remarks>
internal static class EsdsParser
{
	private const byte ES_DescrTag = 0x03;
	private const byte DecoderConfigDescrTag = 0x04;
	private const byte DecSpecificInfoTag = 0x05;

	/// <summary>
	/// Sampling frequency index lookup table for AAC.
	/// </summary>
	/// <remarks>
	/// Index 15 (0x0F) means the frequency is explicitly coded in the next 24 bits.
	/// </remarks>
	private static readonly int[] SamplingFrequencies =
	{
		96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050,
		16000, 12000, 11025, 8000, 7350, 0, 0, 0
	};

	/// <summary>
	/// Parses the esds box to extract AAC audio configuration.
	/// </summary>
	/// <param name="data">The esds box data (after box header and FullBox version/flags).</param>
	/// <returns>The parsed configuration, or null if parsing failed.</returns>
	public static EsdsConfig? Parse(ReadOnlySpan<byte> data)
	{
		if (data.Length < 4)
			return null;

		var offset = 0;

		// Parse ES Descriptor
		if (data[offset] != ES_DescrTag)
			return null;
		offset++;

		var esDescriptorSize = ReadDescriptorSize(data, ref offset);
		if (offset + esDescriptorSize > data.Length)
			return null;

		// Skip ES_ID (2 bytes) and flags (1 byte)
		offset += 3;

		// Find Decoder Config Descriptor
		if (offset >= data.Length || data[offset] != DecoderConfigDescrTag)
			return null;
		offset++;

		var decoderConfigSize = ReadDescriptorSize(data, ref offset);
		if (offset + decoderConfigSize > data.Length)
			return null;

		// Read decoder config
		if (offset + 13 > data.Length)
			return null;

		var objectTypeIndication = data[offset];
		var streamType = data[offset + 1];
		var bufferSizeDB = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 2, 3).Prepend((byte)0));
		var maxBitrate = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 5, 4));
		var avgBitrate = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 9, 4));
		offset += 13;

		// Find DecoderSpecificInfo (Audio Specific Config)
		if (offset >= data.Length || data[offset] != DecSpecificInfoTag)
			return new EsdsConfig
			{
				ObjectTypeIndication = objectTypeIndication,
				MaxBitrate = maxBitrate,
				AvgBitrate = avgBitrate
			};

		offset++;
		var audioSpecificConfigSize = ReadDescriptorSize(data, ref offset);
		if (offset + audioSpecificConfigSize > data.Length)
			return null;

		var audioSpecificConfig = data.Slice(offset, audioSpecificConfigSize);

		// Parse Audio Specific Config
		var (sampleRate, channels) = ParseAudioSpecificConfig(audioSpecificConfig);

		return new EsdsConfig
		{
			ObjectTypeIndication = objectTypeIndication,
			MaxBitrate = maxBitrate,
			AvgBitrate = avgBitrate,
			SampleRate = sampleRate,
			Channels = channels
		};
	}

	/// <summary>
	/// Reads a descriptor size using the expandable size encoding.
	/// </summary>
	/// <remarks>
	/// Each byte has 7 data bits and 1 continuation bit (MSB).
	/// Size can be 1-4 bytes depending on continuation bits.
	/// </remarks>
	private static int ReadDescriptorSize(ReadOnlySpan<byte> data, ref int offset)
	{
		var size = 0;
		var count = 0;

		while (offset < data.Length && count < 4)
		{
			var b = data[offset++];
			size = (size << 7) | (b & 0x7F);
			if ((b & 0x80) == 0)
				break;
			count++;
		}

		return size;
	}

	/// <summary>
	/// Parses the Audio Specific Config to extract sample rate and channel count.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Audio Specific Config bit layout:
	/// - Bits 0-4: audioObjectType (5 bits)
	/// - Bits 5-8: samplingFrequencyIndex (4 bits)
	/// - If index == 15: next 24 bits are explicit sample rate
	/// - Next 4 bits: channelConfiguration
	/// </para>
	/// <para>
	/// Reference: ISO/IEC 14496-3 §1.6.2.1
	/// </para>
	/// </remarks>
	private static (int sampleRate, int channels) ParseAudioSpecificConfig(ReadOnlySpan<byte> config)
	{
		if (config.Length < 2)
			return (0, 0);

		// Read first 16 bits
		var bits = BinaryPrimitives.ReadUInt16BigEndian(config);

		// Extract samplingFrequencyIndex (4 bits starting at bit 5)
		var samplingFrequencyIndex = (bits >> 7) & 0x0F;

		int sampleRate;
		int channelOffset;

		if (samplingFrequencyIndex == 15)
		{
			// Explicit 24-bit sample rate follows
			if (config.Length < 5)
				return (0, 0);

			// Read 24-bit sample rate (bits 9-32)
			sampleRate = ((config[1] & 0x7F) << 17) | (config[2] << 9) | (config[3] << 1) | (config[4] >> 7);
			channelOffset = 4;
		}
		else
		{
			// Use lookup table
			sampleRate = samplingFrequencyIndex < SamplingFrequencies.Length
				? SamplingFrequencies[samplingFrequencyIndex]
				: 0;
			channelOffset = 1;
		}

		// Extract channelConfiguration (4 bits)
		int channels;
		if (samplingFrequencyIndex == 15)
		{
			// Channel config starts at bit 33
			channels = (config[channelOffset] >> 3) & 0x0F;
		}
		else
		{
			// Channel config starts at bit 11
			channels = (config[channelOffset] >> 3) & 0x0F;
		}

		// Convert channel configuration to actual channel count
		// 0 = defined in AOT Specific Config (rare, treat as 0)
		// 1-7 = standard configurations
		// 7 is actually 8 channels (7.1)
		var actualChannels = channels switch
		{
			0 => 0, // AOT-specific, fallback needed
			1 => 1, // Mono
			2 => 2, // Stereo
			3 => 3, // L, R, C
			4 => 4, // L, R, C, rear
			5 => 5, // L, R, C, LS, RS
			6 => 6, // 5.1: L, R, C, LFE, LS, RS
			7 => 8, // 7.1: L, R, C, LFE, LS, RS, Lsr, Rsr
			_ => channels
		};

		return (sampleRate, actualChannels);
	}
}

/// <summary>
/// Represents the parsed configuration from an esds box.
/// </summary>
internal struct EsdsConfig
{
	/// <summary>
	/// Gets the object type indication (codec type).
	/// </summary>
	/// <remarks>
	/// Common values:
	/// - 0x40 = AAC-LC (Low Complexity)
	/// - 0x66 = AAC Main
	/// - 0x67 = AAC LC
	/// - 0x68 = AAC SSR
	/// - 0x69 = AAC LTP
	/// - 0x6B = MP3
	/// </remarks>
	public byte ObjectTypeIndication { get; set; }

	/// <summary>
	/// Gets the maximum bitrate in bits per second.
	/// </summary>
	public uint MaxBitrate { get; set; }

	/// <summary>
	/// Gets the average bitrate in bits per second (0 if VBR or unknown).
	/// </summary>
	public uint AvgBitrate { get; set; }

	/// <summary>
	/// Gets the sample rate in Hz (0 if not available).
	/// </summary>
	public int SampleRate { get; set; }

	/// <summary>
	/// Gets the number of audio channels (0 if not available).
	/// </summary>
	public int Channels { get; set; }
}

/// <summary>
/// Extension methods for Span operations.
/// </summary>
internal static class SpanExtensions
{
	/// <summary>
	/// Prepends a byte to a span by creating a new array.
	/// </summary>
	public static ReadOnlySpan<byte> Prepend(this ReadOnlySpan<byte> span, byte value)
	{
		var result = new byte[span.Length + 1];
		result[0] = value;
		span.CopyTo(result.AsSpan(1));
		return result;
	}
}
