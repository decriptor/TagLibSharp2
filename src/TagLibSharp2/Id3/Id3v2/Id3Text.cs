// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Id3.Id3v2;

/// <summary>
/// Shared decoding for ID3 text. A trailing terminator is not part of the string.
/// </summary>
internal static class Id3Text
{
	/// <summary>
	/// Drops trailing string terminators. Internal NULs stay: ID3v2.4 uses them to
	/// separate values, and a comment or lyric line is one string.
	/// </summary>
	internal static ReadOnlySpan<byte> TrimTrailingTerminator (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (encoding is TextEncodingType.Utf16WithBom or TextEncodingType.Utf16BE) {
			while (data.Length >= 2 && data[data.Length - 2] == 0 && data[data.Length - 1] == 0)
				data = data.Slice (0, data.Length - 2);
			return data;
		}

		while (data.Length > 0 && data[data.Length - 1] == 0)
			data = data.Slice (0, data.Length - 1);
		return data;
	}
}
