// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Aac;

/// <summary>
/// The 56-bit ADTS fixed and variable header at the start of every ADTS frame.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 13818-7 §6.2 (adts_fixed_header, adts_variable_header) and ISO/IEC 14496-3 §1.A.2.2:
/// </para>
/// <code>
/// syncword                            12  0xFFF
/// ID                                   1  0 = MPEG-4, 1 = MPEG-2
/// layer                                2  always '00'
/// protection_absent                    1  0 = a 16-bit CRC follows the header
/// profile                              2
/// sampling_frequency_index             4
/// private_bit                          1
/// channel_configuration                3  0 = a program_config_element in the payload
/// original_copy, home                  2
/// copyright_identification_bit, start  2
/// aac_frame_length                    13  bytes, including this header
/// adts_buffer_fullness                11
/// number_of_raw_data_blocks_in_frame   2  blocks minus one
/// </code>
/// <para>
/// The layer bits are what separate an ADTS sync from an MPEG audio frame sync
/// (ISO/IEC 11172-3 §2.4.2.3), where layer '00' is reserved.
/// </para>
/// </remarks>
internal readonly struct AdtsHeader
{
	/// <summary>
	/// The header without the optional CRC.
	/// </summary>
	public const int Size = 7;

	/// <summary>
	/// Samples per raw data block. ADTS has no 960-sample frames (ISO/IEC 14496-3 §1.A.2.2.1).
	/// </summary>
	public const int SamplesPerBlock = 1024;

	// ISO/IEC 13818-7 Table 35, ISO/IEC 14496-3 Table 1.18. Index 12 exists only in MPEG-4.
	static readonly int[] SampleRates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

	AdtsHeader (bool isMpeg2, bool hasCrc, int profileBits, int sampleRateIndex, int channelConfiguration, int frameLength, int rawDataBlocks)
	{
		IsMpeg2 = isMpeg2;
		HasCrc = hasCrc;
		ProfileBits = profileBits;
		SampleRateIndex = sampleRateIndex;
		ChannelConfiguration = channelConfiguration;
		FrameLength = frameLength;
		RawDataBlocks = rawDataBlocks;
	}

	/// <summary>Gets a value indicating whether ID is 1 (MPEG-2 AAC).</summary>
	public bool IsMpeg2 { get; }

	/// <summary>Gets a value indicating whether a CRC follows the header (protection_absent = 0).</summary>
	public bool HasCrc { get; }

	/// <summary>Gets the 2-bit profile field.</summary>
	public int ProfileBits { get; }

	/// <summary>Gets sampling_frequency_index.</summary>
	public int SampleRateIndex { get; }

	/// <summary>Gets channel_configuration.</summary>
	public int ChannelConfiguration { get; }

	/// <summary>Gets aac_frame_length: the whole frame, header included.</summary>
	public int FrameLength { get; }

	/// <summary>Gets the number of raw data blocks in the frame (the field plus one).</summary>
	public int RawDataBlocks { get; }

	/// <summary>Gets the sample rate in Hz.</summary>
	public int SampleRate => SampleRates[SampleRateIndex];

	/// <summary>
	/// Gets the rate for a sampling_frequency_index, or 0 for a reserved or escape index.
	/// </summary>
	public static int SampleRateFor (int index) =>
		index >= 0 && index < SampleRates.Length ? SampleRates[index] : 0;

	/// <summary>
	/// Gets the bytes in front of the first raw data block: the header, then with a CRC one 16-bit
	/// raw_data_block_position per extra block and the 16-bit crc_check (adts_header_error_check).
	/// </summary>
	public int PayloadOffset => Size + (HasCrc ? 2 * RawDataBlocks : 0);

	/// <summary>
	/// Gets the profile. A 3 is LTP in MPEG-4 and reserved in MPEG-2.
	/// </summary>
	public AacProfile Profile => IsMpeg2 && ProfileBits == 3 ? AacProfile.Unknown : (AacProfile)(ProfileBits + 1);

	/// <summary>
	/// Gets the channel count that channel_configuration names (ISO/IEC 14496-3 Table 1.19), or 0 when
	/// the layout is in a program config element. 7 is 7.1, eight channels.
	/// </summary>
	public int Channels => ChannelConfiguration == 7 ? 8 : ChannelConfiguration;

	/// <summary>
	/// True when the two headers agree on every adts_fixed_header field that names the stream.
	/// The fixed header does not change from frame to frame (ISO/IEC 13818-7 §6.2.1).
	/// </summary>
	public bool SameStream (AdtsHeader other) =>
		IsMpeg2 == other.IsMpeg2
		&& ProfileBits == other.ProfileBits
		&& SampleRateIndex == other.SampleRateIndex
		&& ChannelConfiguration == other.ChannelConfiguration;

	/// <summary>
	/// True when <paramref name="data"/> starts with the 12-bit sync and layer '00'.
	/// </summary>
	public static bool HasSync (ReadOnlySpan<byte> data) =>
		data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xF6) == 0xF0;

	/// <summary>
	/// Parses the header at the start of <paramref name="data"/>. A reserved sample rate index or a frame
	/// length shorter than the header is not a header.
	/// </summary>
	public static bool TryParse (ReadOnlySpan<byte> data, out AdtsHeader header)
	{
		header = default;
		if (data.Length < Size || !HasSync (data))
			return false;

		var isMpeg2 = (data[1] & 0x08) != 0;
		var hasCrc = (data[1] & 0x01) == 0;
		var profileBits = data[2] >> 6;
		var sampleRateIndex = (data[2] >> 2) & 0x0F;
		var channelConfiguration = ((data[2] & 0x01) << 2) | (data[3] >> 6);
		var frameLength = ((data[3] & 0x03) << 11) | (data[4] << 3) | (data[5] >> 5);
		var rawDataBlocks = (data[6] & 0x03) + 1;

		var maxIndex = isMpeg2 ? 11 : 12;
		if (sampleRateIndex > maxIndex)
			return false;

		var parsed = new AdtsHeader (isMpeg2, hasCrc, profileBits, sampleRateIndex, channelConfiguration, frameLength, rawDataBlocks);
		if (frameLength < parsed.PayloadOffset)
			return false;

		header = parsed;
		return true;
	}
}
