// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Mp4;

/// <summary>
/// Specifies the audio codec used in an MP4/M4A file.
/// </summary>
/// <remarks>
/// MP4 files can contain various audio codecs identified by their 4-character code.
/// Reference: ISO 14496-12 (MP4 base format) and ISO 14496-14 (MP4 file format).
/// </remarks>
public enum Mp4AudioCodec
{
	/// <summary>
	/// Unknown or unsupported codec.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// AAC (Advanced Audio Coding) - ISO/IEC 14496-3.
	/// </summary>
	/// <remarks>
	/// 4CC: "mp4a" with object type 0x40 (MPEG-4 AAC).
	/// Most common codec for M4A files.
	/// </remarks>
	Aac,

	/// <summary>
	/// ALAC (Apple Lossless Audio Codec).
	/// </summary>
	/// <remarks>
	/// 4CC: "alac".
	/// Apple's proprietary lossless codec.
	/// </remarks>
	Alac,

	/// <summary>
	/// MP3 audio in MP4 container.
	/// </summary>
	/// <remarks>
	/// 4CC: "mp4a" with object type 0x6B (MPEG-1 Layer 3).
	/// Less common in MP4 containers.
	/// </remarks>
	Mp3,

	/// <summary>
	/// Dolby Digital (AC-3).
	/// </summary>
	/// <remarks>
	/// 4CC: "ac-3".
	/// Used in some video files.
	/// </remarks>
	Ac3,

	/// <summary>
	/// Dolby Digital Plus (Enhanced AC-3).
	/// </summary>
	/// <remarks>
	/// 4CC: "ec-3".
	/// Enhanced version of AC-3.
	/// </remarks>
	Eac3,

	/// <summary>
	/// FLAC (Free Lossless Audio Codec) in MP4 container.
	/// </summary>
	/// <remarks>
	/// 4CC: "fLaC".
	/// Non-standard but supported by some players.
	/// </remarks>
	Flac,

	/// <summary>
	/// Opus codec in MP4 container.
	/// </summary>
	/// <remarks>
	/// 4CC: "Opus".
	/// Modern low-latency codec.
	/// </remarks>
	Opus
}
