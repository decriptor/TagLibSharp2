// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Mpeg;

/// <summary>
/// An MPEG-1 or MPEG-2 video sequence header, with the MPEG-2 sequence extension when present.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 11172-2 2.4.2.3 and ISO/IEC 13818-2 6.2.2.1: after the 0x000001B3 start code come
/// horizontal_size_value (12 bits), vertical_size_value (12), aspect_ratio_information (4),
/// frame_rate_code (4), bit_rate_value (18, units of 400 bit/s), marker (1),
/// vbv_buffer_size_value (10), constrained_parameters_flag (1), and two load-matrix flags,
/// each followed by 64 bytes when set.
/// </para>
/// <para>
/// ISO/IEC 13818-2 6.2.2.3: an MPEG-2 sequence header is followed by a sequence_extension
/// (0x000001B5, identifier '0001') carrying the two high bits of each size, twelve high bits
/// of the bit rate, and frame_rate_extension_n/d. Its presence is what makes the stream MPEG-2
/// (13818-2 6.1.1.6).
/// </para>
/// </remarks>
internal readonly struct MpegVideoSequenceHeader
{
	/// <summary>The MPEG-1 bit_rate_value that means a variable bit rate (11172-2 2.4.3.2).</summary>
	const int VariableBitRate = 0x3FFFF;

	MpegVideoSequenceHeader (int version, int width, int height, int aspectRatio, double frameRate, long bitRate)
	{
		Version = version;
		Width = width;
		Height = height;
		AspectRatio = aspectRatio;
		FrameRate = frameRate;
		BitRate = bitRate;
	}

	/// <summary>1 for ISO/IEC 11172-2, 2 when a sequence extension follows (ISO/IEC 13818-2).</summary>
	public int Version { get; }

	public int Width { get; }

	public int Height { get; }

	/// <summary>The aspect_ratio_information code: pel aspect for MPEG-1, display aspect for MPEG-2.</summary>
	public int AspectRatio { get; }

	/// <summary>Frames per second, including frame_rate_extension_n/d.</summary>
	public double FrameRate { get; }

	/// <summary>
	/// The coded bit rate in bit/s (the peak for a variable-rate MPEG-2 stream), or 0 when an
	/// MPEG-1 stream marks it variable or an MPEG-2 stream leaves the same all-ones code.
	/// </summary>
	public long BitRate { get; }

	/// <summary>
	/// The frame period for frame_rate_code 1 to 8 (13818-2 Table 6-4). 0 and 9 to 15 are reserved.
	/// </summary>
	static double FrameRateFromCode (int code) => code switch {
		1 => 24000d / 1001,
		2 => 24d,
		3 => 25d,
		4 => 30000d / 1001,
		5 => 30d,
		6 => 50d,
		7 => 60000d / 1001,
		8 => 60d,
		_ => 0d
	};

	/// <summary>
	/// True when <paramref name="data"/> starts with a sequence header whose sizes and codes are not reserved.
	/// </summary>
	public static bool LooksLikeStart (ReadOnlySpan<byte> data)
	{
		if (data.Length < 8 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0xB3)
			return false;
		var width = (data[4] << 4) | (data[5] >> 4);
		var height = ((data[5] & 0x0F) << 8) | data[6];
		var aspect = data[7] >> 4;
		var rate = data[7] & 0x0F;
		return width != 0 && height != 0 && aspect != 0 && aspect != 15 && FrameRateFromCode (rate) > 0;
	}

	/// <summary>
	/// Parses the first sequence header in <paramref name="data"/>.
	/// </summary>
	public static bool TryFind (ReadOnlySpan<byte> data, out MpegVideoSequenceHeader header)
	{
		header = default;
		var at = 0;
		while (true) {
			var found = MpegStartCode.Find (data, at, 0xB3);
			if (found < 0)
				return false;
			if (TryParse (data.Slice (found), out header))
				return true;
			at = found + 4;
		}
	}

	static bool TryParse (ReadOnlySpan<byte> data, out MpegVideoSequenceHeader header)
	{
		header = default;
		if (!LooksLikeStart (data) || data.Length < 12)
			return false;

		// The marker bit after bit_rate_value must be set.
		if ((data[10] & 0x20) == 0)
			return false;

		var width = (data[4] << 4) | (data[5] >> 4);
		var height = ((data[5] & 0x0F) << 8) | data[6];
		var aspect = data[7] >> 4;
		var frameRate = FrameRateFromCode (data[7] & 0x0F);
		var bitRateValue = (data[8] << 10) | (data[9] << 2) | (data[10] >> 6);

		var end = 12;
		if ((data[11] & 0x02) != 0) {
			// load_intra_quantiser_matrix: 64 bytes, then load_non_intra in the last bit of the matrix.
			end += 64;
			if (data.Length < end)
				return false;
			if ((data[end - 1] & 0x01) != 0)
				end += 64;
		} else if ((data[11] & 0x01) != 0) {
			end += 64;
		}

		if (data.Length < end)
			return false;

		// The next start code says whether a sequence extension follows. Without one in view
		// the version is not known yet.
		var next = MpegStartCode.FindAny (data, end);
		if (next < 0)
			return false;
		var extension = data[next + 3] == 0xB5 ? next : -1;
		if (extension >= 0 && data.Length < extension + 10)
			return false;
		if (extension >= 0 && (data[extension + 4] >> 4) == 1) {
			var e = data.Slice (extension);
			var hExt = ((e[5] & 0x01) << 1) | (e[6] >> 7);
			var vExt = (e[6] >> 5) & 0x03;
			var bitRateExt = ((e[6] & 0x1F) << 7) | (e[7] >> 1);
			var n = (e[9] >> 5) & 0x03;
			var d = e[9] & 0x1F;
			// The MPEG-1 variable-rate code with no extension bits is what encoders write
			// when they set no peak rate; ffprobe reports no rate for it either.
			var unset = bitRateValue == VariableBitRate && (bitRateExt == 0 || bitRateExt == 0xFFF);
			var bitRate = unset ? 0 : (((long)bitRateExt << 18) | (uint)bitRateValue) * 400;
			header = new MpegVideoSequenceHeader (
				2,
				(hExt << 12) | width,
				(vExt << 12) | height,
				aspect,
				frameRate * (n + 1) / (d + 1),
				bitRate);
			return true;
		}

		header = new MpegVideoSequenceHeader (
			1,
			width,
			height,
			aspect,
			frameRate,
			bitRateValue == VariableBitRate ? 0 : (long)bitRateValue * 400);
		return true;
	}
}

/// <summary>
/// Start code search. A start code is the prefix 0x000001 and one code byte
/// (ISO/IEC 11172-1 2.4.4, ISO/IEC 13818-2 5.3).
/// </summary>
internal static class MpegStartCode
{
	/// <summary>The offset of the next prefix followed by <paramref name="code"/>, or -1.</summary>
	public static int Find (ReadOnlySpan<byte> data, int from, byte code)
	{
		var at = from;
		while (true) {
			var found = FindAny (data, at);
			if (found < 0)
				return -1;
			if (data[found + 3] == code)
				return found;
			at = found + 3;
		}
	}

	/// <summary>The offset of the next prefix that has a code byte after it, or -1.</summary>
	public static int FindAny (ReadOnlySpan<byte> data, int from)
	{
		if (from < 0)
			from = 0;
		var i = from;
		while (i + 3 < data.Length) {
			var rest = data.Slice (i, data.Length - i - 1);
			var one = rest.IndexOf ((byte)0x01);
			if (one < 0)
				return -1;
			var at = i + one;
			if (at >= i + 2 && data[at - 1] == 0 && data[at - 2] == 0)
				return at - 2;
			i = at + 1;
		}

		return -1;
	}
}
