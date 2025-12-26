// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace TagLibSharp2.Core;

/// <summary>
/// Picture types as defined in ID3v2 APIC and FLAC PICTURE specifications.
/// </summary>
/// <remarks>
/// These values are identical across ID3v2.3, ID3v2.4, and FLAC metadata formats.
/// Reference: ID3v2.4.0-frames section 4.14, RFC 9639 section 8.8.
/// </remarks>
[SuppressMessage ("Design", "CA1028:Enum storage should be Int32", Justification = "Matches spec single-byte picture type field")]
public enum PictureType : byte
{
	/// <summary>
	/// Other picture type.
	/// </summary>
	Other = 0x00,

	/// <summary>
	/// 32x32 pixels file icon (PNG only).
	/// </summary>
	FileIcon = 0x01,

	/// <summary>
	/// Other file icon.
	/// </summary>
	OtherFileIcon = 0x02,

	/// <summary>
	/// Cover (front).
	/// </summary>
	FrontCover = 0x03,

	/// <summary>
	/// Cover (back).
	/// </summary>
	BackCover = 0x04,

	/// <summary>
	/// Leaflet page.
	/// </summary>
	LeafletPage = 0x05,

	/// <summary>
	/// Media (e.g., label side of CD).
	/// </summary>
	Media = 0x06,

	/// <summary>
	/// Lead artist/performer/soloist.
	/// </summary>
	LeadArtist = 0x07,

	/// <summary>
	/// Artist/performer.
	/// </summary>
	Artist = 0x08,

	/// <summary>
	/// Conductor.
	/// </summary>
	Conductor = 0x09,

	/// <summary>
	/// Band/Orchestra.
	/// </summary>
	Band = 0x0A,

	/// <summary>
	/// Composer.
	/// </summary>
	Composer = 0x0B,

	/// <summary>
	/// Lyricist/text writer.
	/// </summary>
	Lyricist = 0x0C,

	/// <summary>
	/// Recording location.
	/// </summary>
	RecordingLocation = 0x0D,

	/// <summary>
	/// During recording.
	/// </summary>
	DuringRecording = 0x0E,

	/// <summary>
	/// During performance.
	/// </summary>
	DuringPerformance = 0x0F,

	/// <summary>
	/// Movie/video screen capture.
	/// </summary>
	MovieScreenCapture = 0x10,

	/// <summary>
	/// A bright coloured fish (yes, really - it's in the spec).
	/// </summary>
	ColouredFish = 0x11,

	/// <summary>
	/// Illustration.
	/// </summary>
	Illustration = 0x12,

	/// <summary>
	/// Band/artist logotype.
	/// </summary>
	BandLogo = 0x13,

	/// <summary>
	/// Publisher/studio logotype.
	/// </summary>
	PublisherLogo = 0x14
}
