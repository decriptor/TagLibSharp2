// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Specifies the type of a FLAC metadata block.
/// </summary>
/// <remarks>
/// <para>
/// FLAC files contain one or more metadata blocks before the audio frames.
/// Each block is identified by a type byte in the block header.
/// </para>
/// <para>
/// Block type values:
/// </para>
/// <list type="bullet">
/// <item><see cref="StreamInfo"/> (0): Required, always first. Contains stream parameters.</item>
/// <item><see cref="Padding"/> (1): Placeholder bytes for future use.</item>
/// <item><see cref="Application"/> (2): Application-specific data with 4-byte ID.</item>
/// <item><see cref="SeekTable"/> (3): Seek points for efficient seeking.</item>
/// <item><see cref="VorbisComment"/> (4): Metadata tags (title, artist, etc.).</item>
/// <item><see cref="CueSheet"/> (5): CD table of contents data.</item>
/// <item><see cref="Picture"/> (6): Album art and other images.</item>
/// </list>
/// <para>
/// Values 7-126 are reserved for future use.
/// Value 127 is invalid to avoid sync confusion.
/// </para>
/// <para>
/// Reference: https://xiph.org/flac/format.html#metadata_block_header
/// </para>
/// </remarks>
[SuppressMessage ("Design", "CA1028:Enum storage should be Int32", Justification = "Matches FLAC spec 7-bit block type field")]
public enum FlacBlockType : byte
{
	/// <summary>
	/// STREAMINFO block containing stream parameters.
	/// </summary>
	/// <remarks>
	/// This is the only mandatory metadata block. It must be the first block
	/// and contains information about the audio stream (sample rate, channels,
	/// bits per sample, total samples, MD5 signature).
	/// </remarks>
	StreamInfo = 0,

	/// <summary>
	/// PADDING block containing zero bytes.
	/// </summary>
	/// <remarks>
	/// Used to reserve space for future metadata expansion without
	/// having to rewrite the entire file.
	/// </remarks>
	Padding = 1,

	/// <summary>
	/// APPLICATION block for third-party application data.
	/// </summary>
	/// <remarks>
	/// Contains a 4-byte application ID followed by application-specific data.
	/// </remarks>
	Application = 2,

	/// <summary>
	/// SEEKTABLE block containing seek points.
	/// </summary>
	/// <remarks>
	/// Provides efficient random access to the audio stream by mapping
	/// sample numbers to byte offsets.
	/// </remarks>
	SeekTable = 3,

	/// <summary>
	/// VORBIS_COMMENT block containing metadata tags.
	/// </summary>
	/// <remarks>
	/// Uses the same format as Ogg Vorbis comments. Contains title, artist,
	/// album, and other user-defined tags.
	/// </remarks>
	VorbisComment = 4,

	/// <summary>
	/// CUESHEET block containing CD table of contents.
	/// </summary>
	/// <remarks>
	/// Used when encoding CD audio to preserve the original track structure.
	/// </remarks>
	CueSheet = 5,

	/// <summary>
	/// PICTURE block containing image data.
	/// </summary>
	/// <remarks>
	/// Contains album art or other images with MIME type, description,
	/// dimensions, and raw image data.
	/// </remarks>
	Picture = 6
}
