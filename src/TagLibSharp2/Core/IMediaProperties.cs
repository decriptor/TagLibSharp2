// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Represents audio/video stream properties such as duration, bitrate, and sample rate.
/// </summary>
public interface IMediaProperties
{
	/// <summary>
	/// Gets the duration of the media.
	/// </summary>
	TimeSpan Duration { get; }

	/// <summary>
	/// Gets the audio bitrate in kilobits per second (kbps).
	/// </summary>
	/// <remarks>
	/// For variable bitrate (VBR) files, this is typically the average bitrate.
	/// Returns 0 if the bitrate cannot be determined.
	/// </remarks>
	int Bitrate { get; }

	/// <summary>
	/// Gets the audio sample rate in Hertz (Hz).
	/// </summary>
	/// <remarks>
	/// Common values: 44100 (CD quality), 48000 (DVD/broadcast), 96000 (high-resolution audio).
	/// Returns 0 if the sample rate cannot be determined.
	/// </remarks>
	int SampleRate { get; }

	/// <summary>
	/// Gets the bits per sample (bit depth).
	/// </summary>
	/// <remarks>
	/// Common values: 16 (CD quality), 24 (high-resolution audio).
	/// Returns 0 for lossy formats where bit depth doesn't apply.
	/// </remarks>
	int BitsPerSample { get; }

	/// <summary>
	/// Gets the number of audio channels.
	/// </summary>
	/// <remarks>
	/// Common values: 1 (mono), 2 (stereo), 6 (5.1 surround).
	/// Returns 0 if the channel count cannot be determined.
	/// </remarks>
	int Channels { get; }

	/// <summary>
	/// Gets the codec or format name.
	/// </summary>
	/// <remarks>
	/// Examples: "FLAC", "Vorbis", "MP3", "AAC".
	/// Returns null if the codec cannot be determined.
	/// </remarks>
	string? Codec { get; }
}
