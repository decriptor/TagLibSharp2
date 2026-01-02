// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Asf;

/// <summary>
/// Specifies the data type of an ASF attribute.
/// </summary>
/// <remarks>
/// Reference: ASF Specification Section 3.11 (Extended Content Description Object).
/// Values match the ASF specification type codes (0-6).
/// </remarks>
public enum AsfAttributeType
{
	/// <summary>
	/// Unicode string (UTF-16 LE, null-terminated). ASF type 0.
	/// </summary>
	UnicodeString = 0,

	/// <summary>
	/// Byte array (binary data). ASF type 1.
	/// </summary>
	Binary = 1,

	/// <summary>
	/// Boolean value stored as 32-bit integer (0 = false, non-zero = true). ASF type 2.
	/// </summary>
	Bool = 2,

	/// <summary>
	/// 32-bit unsigned integer (DWORD), little-endian. ASF type 3.
	/// </summary>
	Dword = 3,

	/// <summary>
	/// 64-bit unsigned integer (QWORD), little-endian. ASF type 4.
	/// </summary>
	Qword = 4,

	/// <summary>
	/// 16-bit unsigned integer (WORD), little-endian. ASF type 5.
	/// </summary>
	Word = 5,

	/// <summary>
	/// 128-bit GUID (only valid in Metadata Library Object). ASF type 6.
	/// </summary>
	UniqueId = 6
}
