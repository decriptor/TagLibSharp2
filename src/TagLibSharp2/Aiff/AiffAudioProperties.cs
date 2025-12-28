// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Aiff;

/// <summary>
/// Audio properties parsed from an AIFF COMM (Common) chunk.
/// </summary>
/// <remarks>
/// COMM chunk structure (18 bytes minimum):
/// - Bytes 0-1:  Number of channels (16-bit big-endian)
/// - Bytes 2-5:  Number of sample frames (32-bit big-endian)
/// - Bytes 6-7:  Bits per sample (16-bit big-endian)
/// - Bytes 8-17: Sample rate (80-bit extended precision float)
///
/// AIFC adds compression type and name after the sample rate.
/// </remarks>
public class AiffAudioProperties
{
	/// <summary>
	/// Minimum size of the COMM chunk data.
	/// </summary>
	public const int MinCommSize = 18;

	/// <summary>
	/// Gets the number of audio channels.
	/// </summary>
	public int Channels { get; }

	/// <summary>
	/// Gets the total number of sample frames.
	/// </summary>
	public uint SampleFrames { get; }

	/// <summary>
	/// Gets the bits per sample.
	/// </summary>
	public int BitsPerSample { get; }

	/// <summary>
	/// Gets the sample rate in Hz.
	/// </summary>
	public int SampleRate { get; }

	/// <summary>
	/// Gets the audio duration.
	/// </summary>
	public TimeSpan Duration { get; }

	/// <summary>
	/// Gets the bitrate in kbps.
	/// </summary>
	public int Bitrate { get; }

	AiffAudioProperties (int channels, uint sampleFrames, int bitsPerSample, int sampleRate)
	{
		Channels = channels;
		SampleFrames = sampleFrames;
		BitsPerSample = bitsPerSample;
		SampleRate = sampleRate;

		// Calculate duration
		if (sampleRate > 0 && sampleFrames > 0)
			Duration = TimeSpan.FromSeconds ((double)sampleFrames / sampleRate);
		else
			Duration = TimeSpan.Zero;

		// Calculate bitrate: (sampleRate * bitsPerSample * channels) / 1000
		if (sampleRate > 0)
			Bitrate = (sampleRate * bitsPerSample * channels) / 1000;
	}

	/// <summary>
	/// Attempts to parse audio properties from COMM chunk data.
	/// </summary>
	/// <param name="commData">The COMM chunk data (without header).</param>
	/// <param name="properties">The parsed properties, or null if parsing failed.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryParse (BinaryData commData, out AiffAudioProperties? properties)
	{
		properties = null;

		if (commData.Length < MinCommSize)
			return false;

		var span = commData.Span;

		// Parse channels (big-endian)
		int channels = (span[0] << 8) | span[1];

		// Parse sample frames (big-endian)
		uint sampleFrames = (uint)(
			(span[2] << 24) |
			(span[3] << 16) |
			(span[4] << 8) |
			span[5]);

		// Parse bits per sample (big-endian)
		int bitsPerSample = (span[6] << 8) | span[7];

		// Parse sample rate from 80-bit extended float
		var sampleRateDouble = ExtendedFloat.ToDouble (commData.Slice (8, 10));
		int sampleRate = (int)Math.Round (sampleRateDouble);

		properties = new AiffAudioProperties (channels, sampleFrames, bitsPerSample, sampleRate);
		return true;
	}
}
