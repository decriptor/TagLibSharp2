// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Aac;

/// <summary>
/// Audio properties of a raw AAC stream (ADTS or ADIF).
/// </summary>
/// <remarks>
/// <para>
/// ADTS: every frame header is read. The duration is the sum over complete frames of
/// 1024 samples per raw data block (ISO/IEC 13818-7 §6.2, ISO/IEC 14496-3 §1.A.2.2.1)
/// divided by the sample rate. The bitrate is the frame bytes over that duration.
/// The profile, sample rate, and channels come from the first frame. When its
/// channel_configuration is 0, the channels come from the program config element that
/// opens its first raw data block.
/// </para>
/// <para>
/// ADIF: the duration is the payload after the header divided by the header's bitrate
/// (ISO/IEC 13818-7 §6.1). For a variable-rate stream that bitrate is the peak, so the
/// duration is a lower bound.
/// </para>
/// <para>
/// HE-AAC and HE-AACv2 signal SBR and parametric stereo inside the payload. The headers
/// describe the AAC core, so such a stream reports the core sample rate (half the output
/// rate) and, for HE-AACv2, the core channel count. The duration is the same either way.
/// </para>
/// </remarks>
public sealed class AacAudioProperties : IMediaProperties
{
	internal AacAudioProperties (
		TimeSpan duration,
		int bitrate,
		int sampleRate,
		int channels,
		AacProfile profile,
		AacStreamFormat streamFormat,
		int mpegVersion,
		long frameCount)
	{
		Duration = duration;
		Bitrate = bitrate;
		SampleRate = sampleRate;
		Channels = channels;
		Profile = profile;
		StreamFormat = streamFormat;
		MpegVersion = mpegVersion;
		FrameCount = frameCount;
	}

	/// <summary>
	/// Gets the duration of the audio.
	/// </summary>
	public TimeSpan Duration { get; }

	/// <summary>
	/// Gets the average bitrate in kbps.
	/// </summary>
	public int Bitrate { get; }

	/// <summary>
	/// Gets the sample rate in Hz that the stream header names.
	/// </summary>
	public int SampleRate { get; }

	/// <summary>
	/// Gets the number of channels, LFE included. 0 when the layout is not known.
	/// </summary>
	public int Channels { get; }

	/// <summary>
	/// Gets 0. AAC is lossy and has no fixed bit depth.
	/// </summary>
	public int BitsPerSample => 0;

	/// <summary>
	/// Gets the codec name: "AAC-LC", "AAC Main", "AAC-SSR", "AAC-LTP", or "AAC".
	/// </summary>
	public string Codec => Profile switch {
		AacProfile.Main => "AAC Main",
		AacProfile.LowComplexity => "AAC-LC",
		AacProfile.ScalableSampleRate => "AAC-SSR",
		AacProfile.LongTermPrediction => "AAC-LTP",
		_ => "AAC"
	};

	/// <summary>
	/// Gets the AAC profile.
	/// </summary>
	public AacProfile Profile { get; }

	/// <summary>
	/// Gets the transport: ADTS or ADIF.
	/// </summary>
	public AacStreamFormat StreamFormat { get; }

	/// <summary>
	/// Gets the MPEG version the ADTS ID bit names: 2 (ISO/IEC 13818-7) or 4 (ISO/IEC 14496-3).
	/// 0 for ADIF, whose header does not say.
	/// </summary>
	public int MpegVersion { get; }

	/// <summary>
	/// Gets the number of complete ADTS frames. 0 for ADIF, which has no frame headers.
	/// </summary>
	public long FrameCount { get; }
}
