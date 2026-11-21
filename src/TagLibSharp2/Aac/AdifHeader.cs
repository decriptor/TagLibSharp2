// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Aac;

/// <summary>
/// The ADIF header in front of the raw data blocks of an ADIF stream.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 13818-7 §6.1 (Table 2) and ISO/IEC 14496-3 §1.A.2.1 (Table 1.A.2):
/// </para>
/// <code>
/// adif_id                         32  "ADIF"
/// copyright_id_present             1
/// copyright_id                    72  when present
/// original_copy, home              2
/// bitstream_type                   1  0 = constant rate, 1 = variable rate
/// bitrate                         23  bits per second; the peak per frame when variable
/// num_program_config_elements      4  elements minus one
/// per element:
///   adif_buffer_fullness          20  constant rate only
///   program_config_element()
/// </code>
/// <para>
/// ADIF has no frame headers, so its duration comes from the payload size and this bitrate.
/// The profile, sample rate, and channels come from the first program config element.
/// </para>
/// </remarks>
internal readonly struct AdifHeader
{
	/// <summary>
	/// The longest header that 16 program config elements can describe, rounded up.
	/// One element is at most 2465 bits: fixed fields, 45 front/side/back elements, 3 LFE,
	/// 7 data, 15 coupling elements, alignment, and a 255-byte comment.
	/// </summary>
	public const int MaxSize = 8192;

	/// <summary>The "ADIF" magic.</summary>
	public static ReadOnlySpan<byte> Magic => "ADIF"u8;

	AdifHeader (int size, bool isVariableRate, int bitrate, AacProgramConfig program)
	{
		Size = size;
		IsVariableRate = isVariableRate;
		Bitrate = bitrate;
		Program = program;
	}

	/// <summary>Gets the header length in bytes, through the last program config element.</summary>
	public int Size { get; }

	/// <summary>Gets a value indicating whether bitstream_type is 1 (variable rate).</summary>
	public bool IsVariableRate { get; }

	/// <summary>Gets the bitrate field in bits per second.</summary>
	public int Bitrate { get; }

	/// <summary>Gets the first program config element.</summary>
	public AacProgramConfig Program { get; }

	/// <summary>True when <paramref name="data"/> starts with "ADIF".</summary>
	public static bool HasMagic (ReadOnlySpan<byte> data) =>
		data.Length >= 4 && data[..4].SequenceEqual (Magic);

	/// <summary>
	/// Parses the header at the start of <paramref name="data"/>. A header that runs past the data,
	/// or whose first element names a reserved sample rate, is not a header.
	/// </summary>
	public static bool TryParse (ReadOnlySpan<byte> data, out AdifHeader header)
	{
		header = default;
		if (!HasMagic (data))
			return false;

		var reader = new AacBitReader (data);
		reader.Skip (32);
		if (reader.Read (1) == 1)
			reader.Skip (72); // copyright_id
		reader.Skip (2); // original_copy, home
		var isVariableRate = reader.Read (1) == 1;
		var bitrate = (int)reader.Read (23);
		var elements = (int)reader.Read (4) + 1;

		AacProgramConfig first = default;
		for (var i = 0; i < elements; i++) {
			if (!isVariableRate)
				reader.Skip (20); // adif_buffer_fullness
			if (!AacProgramConfig.TryRead (ref reader, out var program))
				return false;
			if (i == 0)
				first = program;
		}

		// ISO/IEC 13818-7 Table 35 has no rate at index 12 and above.
		if (reader.Overrun || first.SampleRateIndex > 11)
			return false;

		header = new AdifHeader ((int)((reader.Position + 7) / 8), isVariableRate, bitrate, first);
		return true;
	}
}
