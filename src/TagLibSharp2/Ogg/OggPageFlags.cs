// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace TagLibSharp2.Ogg;

/// <summary>
/// Flags for an Ogg page header.
/// </summary>
/// <remarks>
/// <para>
/// These flags indicate the state of the logical bitstream at this page:
/// </para>
/// <list type="bullet">
/// <item><see cref="Continuation"/>: This page continues a packet from the previous page.</item>
/// <item><see cref="BeginOfStream"/>: This is the first page of a logical bitstream (BOS).</item>
/// <item><see cref="EndOfStream"/>: This is the last page of a logical bitstream (EOS).</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/ogg/doc/framing.html
/// </para>
/// </remarks>
[Flags]
[SuppressMessage ("Design", "CA1028:Enum storage should be Int32", Justification = "Matches Ogg spec single-byte flags field")]
[SuppressMessage ("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "This is a flags enum")]
public enum OggPageFlags : byte
{
	/// <summary>
	/// No flags set. This page starts a new packet.
	/// </summary>
	None = 0,

	/// <summary>
	/// This page contains data that continues from the previous page.
	/// </summary>
	/// <remarks>
	/// When set, the first packet on this page is a continuation of the
	/// last packet from the previous page.
	/// </remarks>
	Continuation = 0x01,

	/// <summary>
	/// This is the first page of a logical bitstream (BOS).
	/// </summary>
	/// <remarks>
	/// Each logical bitstream in an Ogg file begins with a page that has this flag set.
	/// </remarks>
	BeginOfStream = 0x02,

	/// <summary>
	/// This is the last page of a logical bitstream (EOS).
	/// </summary>
	/// <remarks>
	/// Each logical bitstream in an Ogg file ends with a page that has this flag set.
	/// </remarks>
	EndOfStream = 0x04
}
