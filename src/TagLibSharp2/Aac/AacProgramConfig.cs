// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Aac;

/// <summary>
/// The fields of a program_config_element that name the stream: profile, sample rate, and channels.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 13818-7 §6.3 (program_config_element) and ISO/IEC 14496-3 §4.4.1.1:
/// element_instance_tag(4), object_type(2), sampling_frequency_index(4),
/// num_front_channel_elements(4), num_side_channel_elements(4), num_back_channel_elements(4),
/// num_lfe_channel_elements(2), num_assoc_data_elements(3), num_valid_cc_elements(4),
/// three optional mixdown fields, one (is_cpe(1), tag_select(4)) per front, side and back element,
/// tag_select(4) per LFE and data element, (is_ind_sw(1), tag_select(4)) per coupling element,
/// byte_alignment(), comment_field_bytes(8) and the comment.
/// </para>
/// <para>
/// A channel pair element is two channels and a single channel or LFE element is one.
/// </para>
/// </remarks>
internal readonly struct AacProgramConfig
{
	AacProgramConfig (int profileBits, int sampleRateIndex, int channels)
	{
		ProfileBits = profileBits;
		SampleRateIndex = sampleRateIndex;
		Channels = channels;
	}

	/// <summary>Gets the 2-bit object_type (profile) field.</summary>
	public int ProfileBits { get; }

	/// <summary>Gets sampling_frequency_index.</summary>
	public int SampleRateIndex { get; }

	/// <summary>Gets the output channels, LFE included.</summary>
	public int Channels { get; }

	/// <summary>
	/// Reads one program_config_element. Returns false when the element runs past the data.
	/// </summary>
	public static bool TryRead (ref AacBitReader reader, out AacProgramConfig config)
	{
		config = default;
		reader.Skip (4); // element_instance_tag
		var profileBits = (int)reader.Read (2);
		var sampleRateIndex = (int)reader.Read (4);
		var front = (int)reader.Read (4);
		var side = (int)reader.Read (4);
		var back = (int)reader.Read (4);
		var lfe = (int)reader.Read (2);
		var assoc = (int)reader.Read (3);
		var cc = (int)reader.Read (4);

		if (reader.Read (1) == 1)
			reader.Skip (4); // mono_mixdown_element_number
		if (reader.Read (1) == 1)
			reader.Skip (4); // stereo_mixdown_element_number
		if (reader.Read (1) == 1)
			reader.Skip (3); // matrix_mixdown_idx, pseudo_surround_enable

		var channels = ReadElements (ref reader, front)
			+ ReadElements (ref reader, side)
			+ ReadElements (ref reader, back)
			+ lfe;
		reader.Skip (4L * lfe);
		reader.Skip (4L * assoc);
		reader.Skip (5L * cc);
		reader.AlignToByte ();
		var commentBytes = reader.Read (8);
		reader.Skip (8L * commentBytes);

		if (reader.Overrun)
			return false;

		config = new AacProgramConfig (profileBits, sampleRateIndex, channels);
		return true;
	}

	static int ReadElements (ref AacBitReader reader, int count)
	{
		var channels = 0;
		for (var i = 0; i < count; i++) {
			channels += reader.Read (1) == 1 ? 2 : 1;
			reader.Skip (4);
		}
		return channels;
	}
}
