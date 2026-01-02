// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Asf;

/// <summary>
/// Well-known ASF GUIDs from the specification.
/// </summary>
/// <remarks>
/// Reference: Microsoft ASF Specification v1.2
/// GUIDs are stored in mixed-endian format (Data1-Data3 little-endian, Data4 big-endian).
/// </remarks>
public static class AsfGuids
{
	// ═══════════════════════════════════════════════════════════════
	// Top-Level Objects
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// ASF Header Object GUID: 75B22630-668E-11CF-A6D9-00AA0062CE6C
	/// </summary>
	public static readonly AsfGuid HeaderObject = ParseGuid ([
		0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
		0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
	]);

	/// <summary>
	/// ASF Data Object GUID: 75B22636-668E-11CF-A6D9-00AA0062CE6C
	/// </summary>
	public static readonly AsfGuid DataObject = ParseGuid ([
		0x36, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
		0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
	]);

	/// <summary>
	/// ASF Simple Index Object GUID: 33000890-E5B1-11CF-89F4-00A0C90349CB
	/// </summary>
	public static readonly AsfGuid SimpleIndexObject = ParseGuid ([
		0x90, 0x08, 0x00, 0x33, 0xB1, 0xE5, 0xCF, 0x11,
		0x89, 0xF4, 0x00, 0xA0, 0xC9, 0x03, 0x49, 0xCB
	]);

	// ═══════════════════════════════════════════════════════════════
	// Header Object Sub-Objects
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// File Properties Object GUID: 8CABDCA1-A947-11CF-8EE4-00C00C205365
	/// </summary>
	public static readonly AsfGuid FilePropertiesObject = ParseGuid ([
		0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
		0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
	]);

	/// <summary>
	/// Stream Properties Object GUID: B7DC0791-A9B7-11CF-8EE6-00C00C205365
	/// </summary>
	public static readonly AsfGuid StreamPropertiesObject = ParseGuid ([
		0x91, 0x07, 0xDC, 0xB7, 0xB7, 0xA9, 0xCF, 0x11,
		0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
	]);

	/// <summary>
	/// Content Description Object GUID: 75B22633-668E-11CF-A6D9-00AA0062CE6C
	/// </summary>
	public static readonly AsfGuid ContentDescriptionObject = ParseGuid ([
		0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
		0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
	]);

	/// <summary>
	/// Extended Content Description Object GUID: D2D0A440-E307-11D2-97F0-00A0C95EA850
	/// </summary>
	public static readonly AsfGuid ExtendedContentDescriptionObject = ParseGuid ([
		0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11,
		0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50
	]);

	/// <summary>
	/// Header Extension Object GUID: 5FBF03B5-A92E-11CF-8EE3-00C00C205365
	/// </summary>
	public static readonly AsfGuid HeaderExtensionObject = ParseGuid ([
		0xB5, 0x03, 0xBF, 0x5F, 0x2E, 0xA9, 0xCF, 0x11,
		0x8E, 0xE3, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
	]);

	/// <summary>
	/// Codec List Object GUID: 86D15240-311D-11D0-A3A4-00A0C90348F6
	/// </summary>
	public static readonly AsfGuid CodecListObject = ParseGuid ([
		0x40, 0x52, 0xD1, 0x86, 0x1D, 0x31, 0xD0, 0x11,
		0xA3, 0xA4, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6
	]);

	/// <summary>
	/// Stream Bitrate Properties Object GUID: 7BF875CE-468D-11D1-8D82-006097C9A2B2
	/// </summary>
	public static readonly AsfGuid StreamBitratePropertiesObject = ParseGuid ([
		0xCE, 0x75, 0xF8, 0x7B, 0x8D, 0x46, 0xD1, 0x11,
		0x8D, 0x82, 0x00, 0x60, 0x97, 0xC9, 0xA2, 0xB2
	]);

	/// <summary>
	/// Padding Object GUID: 1806D474-CADF-4509-A4BA-9AABCB96AAE8
	/// </summary>
	public static readonly AsfGuid PaddingObject = ParseGuid ([
		0x74, 0xD4, 0x06, 0x18, 0xDF, 0xCA, 0x09, 0x45,
		0xA4, 0xBA, 0x9A, 0xAB, 0xCB, 0x96, 0xAA, 0xE8
	]);

	// ═══════════════════════════════════════════════════════════════
	// Header Extension Sub-Objects
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Metadata Object GUID: C5F8CBEA-5BAF-4877-8467-AA8C44FA4CCA
	/// </summary>
	public static readonly AsfGuid MetadataObject = ParseGuid ([
		0xEA, 0xCB, 0xF8, 0xC5, 0xAF, 0x5B, 0x77, 0x48,
		0x84, 0x67, 0xAA, 0x8C, 0x44, 0xFA, 0x4C, 0xCA
	]);

	/// <summary>
	/// Metadata Library Object GUID: 44231C94-9498-49D1-A141-1D134E457054
	/// </summary>
	public static readonly AsfGuid MetadataLibraryObject = ParseGuid ([
		0x94, 0x1C, 0x23, 0x44, 0x98, 0x94, 0xD1, 0x49,
		0xA1, 0x41, 0x1D, 0x13, 0x4E, 0x45, 0x70, 0x54
	]);

	/// <summary>
	/// Language List Object GUID: 7C4346A9-EFE0-4BFC-B229-393EDE415C85
	/// </summary>
	public static readonly AsfGuid LanguageListObject = ParseGuid ([
		0xA9, 0x46, 0x43, 0x7C, 0xE0, 0xEF, 0xFC, 0x4B,
		0xB2, 0x29, 0x39, 0x3E, 0xDE, 0x41, 0x5C, 0x85
	]);

	/// <summary>
	/// Extended Stream Properties Object GUID: 14E6A5CB-C672-4332-8399-A96952065B5A
	/// </summary>
	public static readonly AsfGuid ExtendedStreamPropertiesObject = ParseGuid ([
		0xCB, 0xA5, 0xE6, 0x14, 0x72, 0xC6, 0x32, 0x43,
		0x83, 0x99, 0xA9, 0x69, 0x52, 0x06, 0x5B, 0x5A
	]);

	// ═══════════════════════════════════════════════════════════════
	// Stream Type GUIDs
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Audio Media Type GUID: F8699E40-5B4D-11CF-A8FD-00805F5C442B
	/// </summary>
	public static readonly AsfGuid AudioMediaType = ParseGuid ([
		0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11,
		0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
	]);

	/// <summary>
	/// Video Media Type GUID: BC19EFC0-5B4D-11CF-A8FD-00805F5C442B
	/// </summary>
	public static readonly AsfGuid VideoMediaType = ParseGuid ([
		0xC0, 0xEF, 0x19, 0xBC, 0x4D, 0x5B, 0xCF, 0x11,
		0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
	]);

	// ═══════════════════════════════════════════════════════════════
	// Error Correction Type GUIDs
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// No Error Correction GUID: 20FB5700-5B55-11CF-A8FD-00805F5C442B
	/// </summary>
	public static readonly AsfGuid NoErrorCorrection = ParseGuid ([
		0x00, 0x57, 0xFB, 0x20, 0x55, 0x5B, 0xCF, 0x11,
		0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
	]);

	/// <summary>
	/// Audio Spread GUID: BFC3CD50-618F-11CF-8BB2-00AA00B4E220
	/// </summary>
	public static readonly AsfGuid AudioSpread = ParseGuid ([
		0x50, 0xCD, 0xC3, 0xBF, 0x8F, 0x61, 0xCF, 0x11,
		0x8B, 0xB2, 0x00, 0xAA, 0x00, 0xB4, 0xE2, 0x20
	]);

	/// <summary>
	/// Helper to parse bytes into AsfGuid.
	/// </summary>
	static AsfGuid ParseGuid (byte[] bytes)
	{
		var result = AsfGuid.Parse (bytes);
		if (!result.IsSuccess)
			throw new InvalidOperationException ($"Invalid GUID bytes: {result.Error}");
		return result.Value;
	}
}
