// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

namespace TagLibSharp2.Id3.Id3v2;

/// <summary>
/// Shared decoding for ID3 text. A trailing terminator is not part of the string.
/// </summary>
internal static class Id3Text
{
	/// <summary>
	/// Drops trailing terminators. A lone UTF-16 byte is not a terminator unit:
	/// pairing it with the previous byte would consume the last character.
	/// </summary>
	internal static ReadOnlySpan<byte> TrimTrailingTerminator (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (encoding is TextEncodingType.Utf16WithBom or TextEncodingType.Utf16BE) {
			var length = data.Length;
			if ((length & 1) != 0)
				length--;

			while (length >= 2 && data[length - 2] == 0 && data[length - 1] == 0)
				length -= 2;

			return data.Slice (0, length);
		}

		while (data.Length > 0 && data[data.Length - 1] == 0)
			data = data.Slice (0, data.Length - 1);
		return data;
	}

	/// <summary>
	/// Ends one comment or lyric at the first NUL. Text-frame values stay separated.
	/// </summary>
	internal static ReadOnlySpan<byte> CutAtFirstNul (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		data = TrimTrailingTerminator (data, encoding);
		if (encoding is TextEncodingType.Utf16WithBom or TextEncodingType.Utf16BE) {
			var split = IndexOfUtf16Nul (data);
			return split < 0 ? data : data.Slice (0, split);
		}

		var index = data.IndexOf ((byte)0);
		return index < 0 ? data : data.Slice (0, index);
	}

	/// <summary>
	/// Decodes UTF-16 text. Each NUL-separated value may start with its own BOM.
	/// A value with no BOM uses the frame's first BOM, or little-endian when there is none.
	/// </summary>
	/// <remarks>The one UTF-16 decoder, <see cref="Frames.Utf16Text"/>, does the work.</remarks>
	internal static string DecodeUtf16BomText (ReadOnlySpan<byte> data) => Frames.Utf16Text.Decode (data);

	static int IndexOfUtf16Nul (ReadOnlySpan<byte> data)
	{
		for (var i = 0; i + 1 < data.Length; i += 2) {
			if (data[i] == 0 && data[i + 1] == 0)
				return i;
		}

		return -1;
	}
}
