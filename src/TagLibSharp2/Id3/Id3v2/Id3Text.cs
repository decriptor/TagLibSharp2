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
	internal static string DecodeUtf16BomText (ReadOnlySpan<byte> data)
	{
		data = TrimTrailingTerminator (data, TextEncodingType.Utf16WithBom);
		if (data.IsEmpty)
			return string.Empty;

		var littleEndian = FrameIsLittleEndian (data);
		if (IndexOfUtf16Nul (data) < 0)
			return DecodeOneUtf16Value (data, littleEndian);

		var parts = new List<string> ();
		var start = 0;
		for (var i = 0; i + 1 < data.Length; i += 2) {
			if (data[i] != 0 || data[i + 1] != 0)
				continue;

			parts.Add (DecodeOneUtf16Value (data.Slice (start, i - start), littleEndian));
			start = i + 2;
		}

		parts.Add (DecodeOneUtf16Value (data.Slice (start), littleEndian));
		return parts.Count == 1 ? parts[0] : string.Join ("\0", parts);
	}

	static bool FrameIsLittleEndian (ReadOnlySpan<byte> data)
	{
		var start = 0;
		while (start + 1 < data.Length) {
			if (data[start] == 0xFF && data[start + 1] == 0xFE)
				return true;
			if (data[start] == 0xFE && data[start + 1] == 0xFF)
				return false;

			var rel = IndexOfUtf16Nul (data.Slice (start));
			if (rel < 0)
				break;
			start += rel + 2;
		}

		return true;
	}

	static string DecodeOneUtf16Value (ReadOnlySpan<byte> data, bool fallbackLittleEndian)
	{
		if (data.IsEmpty)
			return string.Empty;

		var littleEndian = fallbackLittleEndian;
		if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) {
			littleEndian = true;
			data = data.Slice (2);
		} else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) {
			littleEndian = false;
			data = data.Slice (2);
		}

		if (data.IsEmpty)
			return string.Empty;

		var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
#if NETSTANDARD2_0
		return encoding.GetString (data.ToArray ());
#else
		return encoding.GetString (data);
#endif
	}

	static int IndexOfUtf16Nul (ReadOnlySpan<byte> data)
	{
		for (var i = 0; i + 1 < data.Length; i += 2) {
			if (data[i] == 0 && data[i + 1] == 0)
				return i;
		}

		return -1;
	}
}
