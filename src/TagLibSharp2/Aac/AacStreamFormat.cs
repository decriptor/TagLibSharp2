// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Aac;

/// <summary>
/// The transport that carries a raw AAC file.
/// </summary>
public enum AacStreamFormat
{
	/// <summary>
	/// Audio Data Transport Stream: every frame starts with a sync header
	/// (ISO/IEC 13818-7 §6.2, ISO/IEC 14496-3 §1.A.2.2).
	/// </summary>
	Adts = 0,

	/// <summary>
	/// Audio Data Interchange Format: one "ADIF" header in front of the raw data blocks
	/// (ISO/IEC 13818-7 §6.1, ISO/IEC 14496-3 §1.A.2.1).
	/// </summary>
	Adif = 1
}
