// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Parses the ALAC (Apple Lossless Audio Codec) magic cookie.
/// </summary>
/// <remarks>
/// <para>
/// The ALAC magic cookie is a 36-byte structure containing codec configuration.
/// It appears in the 'alac' box within the AudioSampleEntry in MP4/M4A files.
/// </para>
/// <para>
/// Structure (36 bytes):
/// - Offset 0 (4): Frame length (samples per frame, typically 4096)
/// - Offset 4 (4): Compatible version (0)
/// - Offset 8 (1): Sample size (bits: 16, 20, 24, 32)
/// - Offset 9 (1): Rice history mult (40)
/// - Offset 10 (1): Rice initial history (10)
/// - Offset 11 (1): Rice parameter limit (14)
/// - Offset 12 (1): Channels (1-8)
/// - Offset 13 (2): Max run (255)
/// - Offset 15 (4): Max coded frame size (0 = unknown)
/// - Offset 19 (4): Average bitrate (0 = unknown)
/// - Offset 23 (4): Sample rate (32-bit, big-endian)
/// </para>
/// <para>
/// Reference: https://github.com/macosforge/alac/blob/master/ALACMagicCookieDescription.txt
/// </para>
/// </remarks>
internal static class AlacMagicCookie
{
	/// <summary>
	/// The minimum size of the ALAC magic cookie (sample rate at offset 23-26 is required).
	/// </summary>
	public const int MinCookieSize = 27;

	/// <summary>
	/// Parses the ALAC magic cookie to extract audio configuration.
	/// </summary>
	/// <param name="data">The magic cookie data (must be exactly 36 bytes).</param>
	/// <returns>The parsed configuration, or null if parsing failed.</returns>
	public static AlacConfig? Parse(ReadOnlySpan<byte> data)
	{
		if (data.Length < MinCookieSize)
			return null;

		// Sample size at offset 8
		var sampleSize = data[8];
		if (sampleSize != 16 && sampleSize != 20 && sampleSize != 24 && sampleSize != 32)
			return null; // Invalid sample size

		// Channels at offset 12
		var channels = data[12];
		if (channels < 1 || channels > 8)
			return null; // Invalid channel count

		// Average bitrate at offset 19 (4 bytes, big-endian)
		var avgBitrate = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(19, 4));

		// Sample rate at offset 23 (4 bytes, big-endian)
		var sampleRate = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(23, 4));

		return new AlacConfig
		{
			SampleSize = sampleSize,
			Channels = channels,
			AvgBitrate = avgBitrate,
			SampleRate = (int)sampleRate
		};
	}
}

/// <summary>
/// Represents the parsed configuration from an ALAC magic cookie.
/// </summary>
internal struct AlacConfig
{
	/// <summary>
	/// Gets the sample size in bits (16, 20, 24, or 32).
	/// </summary>
	public int SampleSize { get; set; }

	/// <summary>
	/// Gets the number of audio channels (1-8).
	/// </summary>
	public int Channels { get; set; }

	/// <summary>
	/// Gets the average bitrate in bits per second (0 if unknown).
	/// </summary>
	public uint AvgBitrate { get; set; }

	/// <summary>
	/// Gets the sample rate in Hz.
	/// </summary>
	public int SampleRate { get; set; }
}
