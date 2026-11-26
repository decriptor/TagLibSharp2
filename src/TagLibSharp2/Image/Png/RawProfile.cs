// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace TagLibSharp2.Image.Png;

/// <summary>
/// An ImageMagick raw profile in a PNG text chunk ("Raw profile type exif" and the like): a line feed, the profile
/// name, a line feed, the byte count, a line feed, then the bytes as hexadecimal pairs split across lines.
/// </summary>
static class RawProfile
{
	internal static bool TryDecode (string text, out string name, out byte[] bytes)
	{
		name = "";
		bytes = [];
		var i = 0;
		while (i < text.Length && text[i] == '\n')
			i++;
		var nameEnd = text.IndexOf ('\n', i);
		if (nameEnd < 0)
			return false;
		name = text.Substring (i, nameEnd - i);
		i = nameEnd + 1;
		while (i < text.Length && text[i] == ' ')
			i++;
		var countEnd = text.IndexOf ('\n', i);
		if (countEnd < 0 || !TryCount (text, i, countEnd, out var count))
			return false;
		bytes = new byte[count];
		return TryHexBytes (text, countEnd + 1, bytes);
	}

	static bool TryCount (string text, int from, int end, out int count)
	{
#if NETSTANDARD2_0
		if (!int.TryParse (text.Substring (from, end - from), NumberStyles.None, CultureInfo.InvariantCulture, out count))
#else
		if (!int.TryParse (text.AsSpan (from, end - from), NumberStyles.None, CultureInfo.InvariantCulture, out count))
#endif
			return false;
		return count >= 0 && count <= (text.Length - end) / 2;
	}

	static bool TryHexBytes (string text, int i, byte[] bytes)
	{
		for (var n = 0; n < bytes.Length; n++) {
			while (i < text.Length && text[i] is '\n' or '\r' or ' ')
				i++;
			if (i + 1 >= text.Length || !TryHex (text[i], out var high) || !TryHex (text[i + 1], out var low))
				return false;
			bytes[n] = (byte)((high << 4) | low);
			i += 2;
		}

		return true;
	}

	internal static string Encode (string name, byte[] bytes)
	{
		const string Digits = "0123456789abcdef";
		var builder = new StringBuilder ((bytes.Length * 2) + (bytes.Length / 36) + name.Length + 16);
		builder.Append ('\n').Append (name).Append ('\n');
		builder.Append (bytes.Length.ToString (CultureInfo.InvariantCulture).PadLeft (8)).Append ('\n');
		for (var n = 0; n < bytes.Length; n++) {
			builder.Append (Digits[bytes[n] >> 4]).Append (Digits[bytes[n] & 0x0F]);
			if (n % 36 == 35 || n == bytes.Length - 1)
				builder.Append ('\n');
		}

		return builder.ToString ();
	}

	static bool TryHex (char c, out int value)
	{
		value = c switch {
			>= '0' and <= '9' => c - '0',
			>= 'a' and <= 'f' => c - 'a' + 10,
			>= 'A' and <= 'F' => c - 'A' + 10,
			_ => -1
		};
		return value >= 0;
	}
}
