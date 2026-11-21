// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Aac;

/// <summary>
/// The AAC profile named by an ADTS header or an ADIF program config element.
/// </summary>
/// <remarks>
/// <para>
/// Both headers store a 2-bit profile. ISO/IEC 13818-7 §6.2 (Table 31): 0 Main, 1 Low Complexity,
/// 2 Scalable Sample Rate, 3 reserved. An MPEG-4 ADTS header (ID = 0) stores the MPEG-4 audio object
/// type minus one (ISO/IEC 14496-3 §1.A.2.2.1), where 3 is Long Term Prediction.
/// </para>
/// <para>
/// Each value is the MPEG-4 audio object type (ISO/IEC 14496-3 Table 1.1), the 2-bit field plus one.
/// HE-AAC signals SBR inside the audio payload, not in this field, so an HE-AAC stream reports
/// <see cref="LowComplexity"/>.
/// </para>
/// </remarks>
public enum AacProfile
{
	/// <summary>
	/// The field holds a value this version of the standard reserves.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// AAC Main.
	/// </summary>
	Main = 1,

	/// <summary>
	/// AAC Low Complexity (AAC-LC).
	/// </summary>
	LowComplexity = 2,

	/// <summary>
	/// AAC Scalable Sample Rate (AAC-SSR).
	/// </summary>
	ScalableSampleRate = 3,

	/// <summary>
	/// AAC Long Term Prediction (AAC-LTP). MPEG-4 ADTS only.
	/// </summary>
	LongTermPrediction = 4
}
