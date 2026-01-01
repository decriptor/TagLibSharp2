// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Represents audio stream properties extracted from a media file.
/// </summary>
public sealed class AudioProperties : IMediaProperties
{
	/// <inheritdoc/>
	public TimeSpan Duration { get; }

	/// <inheritdoc/>
	public int Bitrate { get; }

	/// <inheritdoc/>
	public int SampleRate { get; }

	/// <inheritdoc/>
	public int BitsPerSample { get; }

	/// <inheritdoc/>
	public int Channels { get; }

	/// <inheritdoc/>
	public string? Codec { get; }

	/// <summary>
	/// Gets a value indicating whether this instance contains valid audio properties.
	/// </summary>
	/// <remarks>
	/// Returns true if at least duration and sample rate are non-zero.
	/// </remarks>
	public bool IsValid => Duration > TimeSpan.Zero && SampleRate > 0;

	/// <summary>
	/// Gets an empty instance with no audio properties.
	/// </summary>
	public static AudioProperties Empty { get; } = new (TimeSpan.Zero, 0, 0, 0, 0, null);

	/// <summary>
	/// Initializes a new instance of the <see cref="AudioProperties"/> class.
	/// </summary>
	/// <param name="duration">The duration of the audio.</param>
	/// <param name="bitrate">The bitrate in kbps.</param>
	/// <param name="sampleRate">The sample rate in Hz.</param>
	/// <param name="bitsPerSample">The bits per sample (0 for lossy formats).</param>
	/// <param name="channels">The number of audio channels.</param>
	/// <param name="codec">The codec name.</param>
	public AudioProperties (
		TimeSpan duration,
		int bitrate,
		int sampleRate,
		int bitsPerSample,
		int channels,
		string? codec)
	{
		Duration = duration;
		Bitrate = bitrate;
		SampleRate = sampleRate;
		BitsPerSample = bitsPerSample;
		Channels = channels;
		Codec = codec;
	}

	/// <summary>
	/// Creates audio properties from FLAC stream info.
	/// </summary>
	/// <param name="totalSamples">The total number of audio samples.</param>
	/// <param name="sampleRate">The sample rate in Hz.</param>
	/// <param name="bitsPerSample">The bits per sample.</param>
	/// <param name="channels">The number of audio channels.</param>
	/// <returns>A new <see cref="AudioProperties"/> instance.</returns>
	public static AudioProperties FromFlac (
		ulong totalSamples,
		int sampleRate,
		int bitsPerSample,
		int channels)
	{
		var duration = sampleRate > 0
			? TimeSpan.FromSeconds ((double)totalSamples / sampleRate)
			: TimeSpan.Zero;

		// Calculate bitrate: (samples * bits * channels) / duration in seconds / 1000
		var bitrate = duration > TimeSpan.Zero
			? (int)((double)totalSamples * bitsPerSample * channels / duration.TotalSeconds / 1000)
			: 0;

		return new AudioProperties (duration, bitrate, sampleRate, bitsPerSample, channels, "FLAC");
	}

	/// <summary>
	/// Creates audio properties from Vorbis identification header.
	/// </summary>
	/// <param name="totalSamples">The total number of audio samples (from granule position).</param>
	/// <param name="sampleRate">The sample rate in Hz.</param>
	/// <param name="channels">The number of audio channels.</param>
	/// <param name="bitrateNominal">The nominal bitrate from Vorbis header (0 if VBR).</param>
	/// <returns>A new <see cref="AudioProperties"/> instance.</returns>
	public static AudioProperties FromVorbis (
		ulong totalSamples,
		int sampleRate,
		int channels,
		int bitrateNominal)
	{
		var duration = sampleRate > 0
			? TimeSpan.FromSeconds ((double)totalSamples / sampleRate)
			: TimeSpan.Zero;

		// Use nominal bitrate if available, otherwise 0 (VBR without average)
		var bitrate = bitrateNominal > 0 ? bitrateNominal / 1000 : 0;

		return new AudioProperties (duration, bitrate, sampleRate, 0, channels, "Vorbis");
	}

	/// <summary>
	/// Creates audio properties from Opus identification header.
	/// </summary>
	/// <param name="granulePosition">The granule position from the last page (at 48kHz).</param>
	/// <param name="preSkip">The pre-skip samples to subtract.</param>
	/// <param name="inputSampleRate">The original input sample rate (informational).</param>
	/// <param name="channels">The number of audio channels.</param>
	/// <param name="fileSize">The file size in bytes (for bitrate calculation).</param>
	/// <returns>A new <see cref="AudioProperties"/> instance.</returns>
	/// <remarks>
	/// Opus always outputs at 48kHz regardless of the input sample rate.
	/// The inputSampleRate is stored for informational purposes only.
	/// </remarks>
	public static AudioProperties FromOpus (
		ulong granulePosition,
		ushort preSkip,
		uint inputSampleRate,
		int channels,
		long fileSize)
	{
		// Opus always outputs at 48kHz
		const int OutputSampleRate = 48000;

		// Subtract pre-skip for accurate sample count
		var totalSamples = granulePosition > preSkip ? granulePosition - preSkip : 0;

		var duration = totalSamples > 0
			? TimeSpan.FromSeconds ((double)totalSamples / OutputSampleRate)
			: TimeSpan.Zero;

		// Calculate bitrate from file size and duration
		var bitrate = duration.TotalSeconds > 0
			? (int)(fileSize * 8 / duration.TotalSeconds / 1000)
			: 0;

		return new AudioProperties (duration, bitrate, OutputSampleRate, 0, channels, "Opus");
	}

	/// <summary>
	/// Creates audio properties from DSF file information.
	/// </summary>
	/// <param name="duration">The audio duration.</param>
	/// <param name="sampleRate">The sample rate in Hz (e.g., 2822400 for DSD64).</param>
	/// <param name="channels">The number of audio channels.</param>
	/// <returns>A new <see cref="AudioProperties"/> instance.</returns>
	/// <remarks>
	/// DSD always uses 1 bit per sample. Bitrate is calculated as:
	/// sampleRate * channels / 1000 (since 1 bit per sample).
	/// </remarks>
	public static AudioProperties FromDsf (
		TimeSpan duration,
		int sampleRate,
		int channels)
	{
		// DSD bitrate: sampleRate * 1 bit * channels / 1000
		// Use long arithmetic to prevent overflow with high sample rates + many channels
		var bitrate = sampleRate > 0 ? (int)((long)sampleRate * channels / 1000) : 0;

		return new AudioProperties (duration, bitrate, sampleRate, 1, channels, "DSD");
	}

	/// <summary>
	/// Creates audio properties from DFF file information.
	/// </summary>
	/// <param name="duration">The audio duration.</param>
	/// <param name="sampleRate">The sample rate in Hz (e.g., 2822400 for DSD64).</param>
	/// <param name="channels">The number of audio channels.</param>
	/// <param name="isDst">True if the audio uses DST compression.</param>
	/// <returns>A new <see cref="AudioProperties"/> instance.</returns>
	/// <remarks>
	/// DFF uses DSD audio (1 bit per sample), optionally with DST compression.
	/// Bitrate is calculated as: sampleRate * channels / 1000.
	/// </remarks>
	public static AudioProperties FromDff (
		TimeSpan duration,
		int sampleRate,
		int channels,
		bool isDst = false)
	{
		// DSD bitrate: sampleRate * 1 bit * channels / 1000
		// Use long arithmetic to prevent overflow with high sample rates + many channels
		var bitrate = sampleRate > 0 ? (int)((long)sampleRate * channels / 1000) : 0;
		var codec = isDst ? "DST" : "DSD";

		return new AudioProperties (duration, bitrate, sampleRate, 1, channels, codec);
	}

	/// <inheritdoc/>
	public override string ToString ()
	{
		var parts = new List<string> ();

		if (Duration > TimeSpan.Zero)
			parts.Add ($"{Duration:mm\\:ss}");
		if (Bitrate > 0)
			parts.Add ($"{Bitrate}kbps");
		if (SampleRate > 0)
			parts.Add ($"{SampleRate}Hz");
		if (BitsPerSample > 0)
			parts.Add ($"{BitsPerSample}bit");
		if (Channels > 0)
			parts.Add (Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels}ch");
		if (!string.IsNullOrEmpty (Codec))
			parts.Add (Codec!);

		return parts.Count > 0 ? string.Join (", ", parts) : "No audio properties";
	}
}
