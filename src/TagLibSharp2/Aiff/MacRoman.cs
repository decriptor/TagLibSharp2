// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

namespace TagLibSharp2.Aiff;

/// <summary>
/// Decodes AIFF text without the Macintosh code page.
/// Bytes 0x00-0x7F are ASCII. Bytes 0x80-0xFF are Mac OS Roman
/// (0x85 is Ö, 0xA0 is dagger) unless the whole value is valid UTF-8
/// and contains a multibyte sequence.
/// </summary>
static class MacRoman
{
	// Mac OS Roman 0x80-0xFF, from the Unicode vendor mapping.
	static readonly char[] High = [
		'\u00C4', '\u00C5', '\u00C7', '\u00C9', '\u00D1', '\u00D6', '\u00DC', '\u00E1',
		'\u00E0', '\u00E2', '\u00E4', '\u00E3', '\u00E5', '\u00E7', '\u00E9', '\u00E8',
		'\u00EA', '\u00EB', '\u00ED', '\u00EC', '\u00EE', '\u00EF', '\u00F1', '\u00F3',
		'\u00F2', '\u00F4', '\u00F6', '\u00F5', '\u00FA', '\u00F9', '\u00FB', '\u00FC',
		'\u2020', '\u00B0', '\u00A2', '\u00A3', '\u00A7', '\u2022', '\u00B6', '\u00DF',
		'\u00AE', '\u00A9', '\u2122', '\u00B4', '\u00A8', '\u2260', '\u00C6', '\u00D8',
		'\u221E', '\u00B1', '\u2264', '\u2265', '\u00A5', '\u00B5', '\u2202', '\u2211',
		'\u220F', '\u03C0', '\u222B', '\u00AA', '\u00BA', '\u03A9', '\u00E6', '\u00F8',
		'\u00BF', '\u00A1', '\u00AC', '\u221A', '\u0192', '\u2248', '\u2206', '\u00AB',
		'\u00BB', '\u2026', '\u00A0', '\u00C0', '\u00C3', '\u00D5', '\u0152', '\u0153',
		'\u2013', '\u2014', '\u201C', '\u201D', '\u2018', '\u2019', '\u00F7', '\u25CA',
		'\u00FF', '\u0178', '\u2044', '\u20AC', '\u2039', '\u203A', '\uFB01', '\uFB02',
		'\u2021', '\u00B7', '\u201A', '\u201E', '\u2030', '\u00C2', '\u00CA', '\u00C1',
		'\u00CB', '\u00C8', '\u00CD', '\u00CE', '\u00CF', '\u00CC', '\u00D3', '\u00D4',
		'\uF8FF', '\u00D2', '\u00DA', '\u00DB', '\u00D9', '\u0131', '\u02C6', '\u02DC',
		'\u00AF', '\u02D8', '\u02D9', '\u02DA', '\u00B8', '\u02DD', '\u02DB', '\u02C7'
	];

	/// <summary>
	/// Decodes chunk text. A trailing NUL inside the chunk size is padding, not text.
	/// </summary>
	public static string? Decode (ReadOnlySpan<byte> data)
	{
		var end = data.Length;
		while (end > 0 && data[end - 1] == 0)
			end--;
		if (end == 0)
			return null;

		if (end != data.Length)
			data = data.Slice (0, end);

		if (TryUtf8 (data, out var utf8))
			return utf8;

		var chars = new char[data.Length];
		for (var i = 0; i < data.Length; i++) {
			var b = data[i];
			chars[i] = b < 0x80 ? (char)b : High[b - 0x80];
		}

		return new string (chars);
	}

	static readonly Dictionary<char, byte> ToByte = BuildMap ();

	static Dictionary<char, byte> BuildMap ()
	{
		var map = new Dictionary<char, byte> (High.Length);
		for (var i = 0; i < High.Length; i++) {
			if (!map.ContainsKey (High[i]))
				map[High[i]] = (byte)(i + 0x80);
		}

		return map;
	}

	/// <summary>
	/// Encodes text for a native chunk using the same Mac OS Roman table as <see cref="Decode"/>.
	/// ASCII stays ASCII. A character that is not in the table returns false so the caller
	/// can store the value in ID3 instead of writing a different encoding.
	/// </summary>
	public static bool TryEncode (string text, out byte[] bytes)
	{
		bytes = new byte[text.Length];
		for (var i = 0; i < text.Length; i++) {
			var ch = text[i];
			if (ch <= '\u007F') {
				bytes[i] = (byte)ch;
				continue;
			}

			if (!ToByte.TryGetValue (ch, out var mapped)) {
				bytes = [];
				return false;
			}

			bytes[i] = mapped;
		}

		return true;
	}

	static bool TryUtf8 (ReadOnlySpan<byte> data, out string? text)
	{
		text = null;
		var multibyte = false;
		for (var i = 0; i < data.Length;) {
			var b = data[i];
			if (b <= 0x7F) {
				i++;
				continue;
			}

			int extra;
			int min;
			int max;
			if (b is >= 0xC2 and <= 0xDF) {
				extra = 1;
				min = 0x80;
				max = 0xBF;
			} else if (b == 0xE0) {
				extra = 2;
				min = 0xA0;
				max = 0xBF;
			} else if (b is >= 0xE1 and <= 0xEC or >= 0xEE and <= 0xEF) {
				extra = 2;
				min = 0x80;
				max = 0xBF;
			} else if (b == 0xED) {
				extra = 2;
				min = 0x80;
				max = 0x9F;
			} else if (b == 0xF0) {
				extra = 3;
				min = 0x90;
				max = 0xBF;
			} else if (b is >= 0xF1 and <= 0xF3) {
				extra = 3;
				min = 0x80;
				max = 0xBF;
			} else if (b == 0xF4) {
				extra = 3;
				min = 0x80;
				max = 0x8F;
			} else {
				return false;
			}

			if (i + extra >= data.Length)
				return false;

			var next = data[i + 1];
			if (next < min || next > max)
				return false;

			for (var c = 2; c <= extra; c++) {
				var cont = data[i + c];
				if (cont < 0x80 || cont > 0xBF)
					return false;
			}

			multibyte = true;
			i += extra + 1;
		}

		if (!multibyte)
			return false;

		text = Encoding.UTF8.GetString (data.ToArray ());
		return true;
	}
}
