// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Mpeg;

/// <summary>
/// The parts of an AC-3 sync frame that give its sample rate, bit rate, and channel count.
/// </summary>
/// <remarks>
/// ATSC A/52 5.3.1 syncinfo: syncword 0x0B77 (16 bits), crc1 (16), fscod (2), frmsizecod (6).
/// A/52 5.3.2 bsi: bsid (5), bsmod (3), acmod (3), cmixlev (2) when acmod has a centre and
/// is not mono, surmixlev (2) when acmod has a surround, dsurmod (2) when acmod is 2/0,
/// then lfeon (1). fscod 0, 1, 2 are 48, 44.1, 32 kHz (Table 5.6). frmsizecod / 2 indexes the
/// nominal bit rate (Table 5.18). acmod gives the full-bandwidth channels (Table 5.8).
/// A bsid above 10 is not AC-3 (E-AC-3 uses 16), so those frames are not decoded here.
/// https://www.atsc.org/atsc-documents/a522012-digital-audio-compression-ac-3-e-ac-3-standard-12172012/
/// </remarks>
internal readonly struct Ac3SyncFrame
{
	/// <summary>Every AC-3 sync frame holds six blocks of 256 samples (A/52 5.1).</summary>
	public const int SamplesPerFrame = 1536;

	static readonly int[] BitRates = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640];
	static readonly int[] FullBandwidthChannels = [2, 1, 2, 3, 3, 4, 4, 5];

	Ac3SyncFrame (int sampleRate, int bitrate, int channels)
	{
		SampleRate = sampleRate;
		Bitrate = bitrate;
		Channels = channels;
	}

	public int SampleRate { get; }

	/// <summary>The nominal bit rate in kbps.</summary>
	public int Bitrate { get; }

	public int Channels { get; }

	/// <summary>Parses the first sync frame in <paramref name="data"/>.</summary>
	public static bool TryFind (ReadOnlySpan<byte> data, out Ac3SyncFrame frame)
	{
		frame = default;
		for (var i = 0; i + 8 <= data.Length; i++) {
			if (data[i] == 0x0B && data[i + 1] == 0x77 && TryParse (data.Slice (i), out frame))
				return true;
		}

		return false;
	}

	static bool TryParse (ReadOnlySpan<byte> data, out Ac3SyncFrame frame)
	{
		frame = default;
		var fscod = data[4] >> 6;
		var frmsizecod = data[4] & 0x3F;
		var bsid = data[5] >> 3;
		if (fscod == 3 || frmsizecod / 2 >= BitRates.Length || bsid > 10)
			return false;

		var acmod = data[6] >> 5;
		// Bit position of lfeon: 5 bits into byte 6 (bsid, bsmod, acmod), then the optional fields.
		var bit = (6 * 8) + 3;
		if ((acmod & 0x1) != 0 && acmod != 0x1)
			bit += 2;
		if ((acmod & 0x4) != 0)
			bit += 2;
		if (acmod == 0x2)
			bit += 2;
		var lfe = (data[bit / 8] >> (7 - (bit % 8))) & 0x01;

		var sampleRate = fscod switch {
			0 => 48000,
			1 => 44100,
			_ => 32000
		};
		frame = new Ac3SyncFrame (sampleRate, BitRates[frmsizecod / 2], FullBandwidthChannels[acmod] + lfe);
		return true;
	}
}
