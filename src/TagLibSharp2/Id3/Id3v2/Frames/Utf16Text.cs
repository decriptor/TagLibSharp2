// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Text in encoding $01, "UTF-16 encoded Unicode with BOM" (https://id3.org/id3v2.4.0-structure §4).
/// </summary>
/// <remarks>
/// A frame with several strings separates them with 00 00. Each string may begin with its own BOM: ID3v2.3 §3.3
/// says "Unicode strings must begin with the Unicode BOM" (https://id3.org/id3v2.3.0), and ID3v2.4 §4 says only
/// that all strings in a frame share one byte order. A BOM is never part of the text.
/// </remarks>
internal static class Utf16Text
{
	/// <summary>
	/// Decodes every string, honouring the BOM that leads it. A string without one uses the byte order of the
	/// string before it, and the first string without one is little-endian. Strings are joined with U+0000.
	/// </summary>
	public static string Decode (ReadOnlySpan<byte> data)
	{
		if (data.Length < 2)
			return string.Empty;

		var littleEndian = true;
		var text = new StringBuilder (data.Length / 2);
		var start = 0;
		for (var i = 0; ; i += 2) {
			var end = i + 1 >= data.Length;
			if (!end && (data[i] != 0 || data[i + 1] != 0))
				continue;

			if (start > 0)
				text.Append ('\0');
			AppendString (data.Slice (start, (end ? data.Length : i) - start), ref littleEndian, text);
			if (end)
				break;
			start = i + 2;
		}

		return text.ToString ();
	}

	static void AppendString (ReadOnlySpan<byte> data, ref bool littleEndian, StringBuilder text)
	{
		if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) {
			littleEndian = true;
			data = data.Slice (2);
		} else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) {
			littleEndian = false;
			data = data.Slice (2);
		}

		if (data.IsEmpty)
			return;
		var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
		text.Append (encoding.GetString (data.ToArray ()));
	}

	/// <summary>
	/// Encodes little-endian with a BOM before every string. Text without U+0000 encodes as one string.
	/// </summary>
	public static BinaryData Encode (string text)
	{
		var parts = text.Split ('\0');
		if (parts.Length == 1)
			return BinaryData.FromStringUtf16 (text, includeBom: true);

		using var builder = new BinaryDataBuilder (text.Length * 2 + parts.Length * 4);
		for (var i = 0; i < parts.Length; i++) {
			if (i > 0)
				builder.Add (new byte[] { 0, 0 });
			builder.Add (BinaryData.FromStringUtf16 (parts[i], includeBom: true));
		}
		return builder.ToBinaryData ();
	}
}
