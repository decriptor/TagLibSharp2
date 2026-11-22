// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>The character set an AVI file's <c>CSET</c> chunk declares for its INFO text.</summary>
internal enum InfoCharacterSet
{
	/// <summary>No CSET chunk (or one too short to hold wCodePage).</summary>
	None,

	/// <summary>CSET wCodePage 65001.</summary>
	Utf8,

	/// <summary>CSET wCodePage 0 (the spec's ISO 8859/1 default) or 28591.</summary>
	Latin1,

	/// <summary>CSET wCodePage 1252.</summary>
	Windows1252,

	/// <summary>Any other code page, which this library does not decode (for example 932 or 1251).</summary>
	Other
}

/// <summary>
/// Decodes and encodes AVI INFO values for <see cref="RiffInfoTag"/> in AVI text mode.
/// </summary>
/// <remarks>
/// <para>
/// MPIDS 1.0, "CSET (Character Set) Chunk": the CSET chunk "defines the code page ... for the file";
/// "if the CSET chunk is not present, or if this field has a value of zero, assume standard ISO 8859/1
/// code page (identical to code page 1004 without code points defined in hex columns 0, 1, 8, and 9)".
/// ffmpeg and TagLib# write UTF-8 without a CSET, and Windows tools write the ANSI code page.
/// </para>
/// <para>
/// Reading: CSET 65001 is UTF-8, 1252 is Windows-1252, 0 and 28591 are ISO 8859-1. With no CSET, valid
/// UTF-8 is UTF-8 and anything else is read as Windows-1252, which fills the columns 8 and 9 that the
/// spec's default leaves undefined (exiftool reads it the same way). Another code page is read the same
/// way, as a best effort, and its non-ASCII values are marked as not understood.
/// </para>
/// </remarks>
internal static class InfoText
{
	static readonly System.Text.UTF8Encoding StrictUtf8 = new (encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>Windows-1252 0x80-0x9F. The five unassigned bytes map to the same C1 code point.</summary>
	static readonly char[] Windows1252High = [
		'\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
		'\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
		'\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
		'\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178'
	];

	/// <summary>
	/// The text of a field up to its first NUL. <paramref name="utf8"/> is true when the bytes were read
	/// as UTF-8 under a CSET of 65001 or no CSET, so they can share a UTF-8 list unchanged.
	/// </summary>
	public static string Decode (ReadOnlySpan<byte> field, InfoCharacterSet characterSet, out bool utf8)
	{
		utf8 = false;
		var end = field.IndexOf ((byte)0);
		var text = end < 0 ? field : field.Slice (0, end);
		switch (characterSet) {
			case InfoCharacterSet.Latin1:
				return Latin1 (text);
			case InfoCharacterSet.Windows1252:
				return Windows1252 (text);
		}

		if (TryUtf8 (text, out var decoded)) {
			utf8 = characterSet is InfoCharacterSet.Utf8 or InfoCharacterSet.None;
			return decoded;
		}

		return Windows1252 (text);
	}

	/// <summary>True when every character of <paramref name="value"/> has a byte in <paramref name="characterSet"/>.</summary>
	/// <remarks>With no CSET, or a code page this library does not encode, only ASCII is written without declaring a code page.</remarks>
	public static bool CanEncode (string value, InfoCharacterSet characterSet)
	{
		foreach (var c in value) {
			if (c <= '\u007F')
				continue;
			var fits = characterSet switch {
				InfoCharacterSet.Utf8 => true,
				InfoCharacterSet.Latin1 => c <= '\u00FF',
				InfoCharacterSet.Windows1252 => Windows1252Byte (c) >= 0,
				_ => false
			};
			if (!fits)
				return false;
		}

		return true;
	}

	/// <summary>The value and a NUL terminator in <paramref name="characterSet"/>. The value must pass <see cref="CanEncode"/>.</summary>
	public static byte[] Encode (string value, InfoCharacterSet characterSet)
	{
		if (characterSet == InfoCharacterSet.Utf8) {
			var text = System.Text.Encoding.UTF8.GetBytes (value);
			var utf8 = new byte[text.Length + 1];
			text.CopyTo (utf8, 0);
			return utf8;
		}

		var bytes = new byte[value.Length + 1];
		for (var i = 0; i < value.Length; i++) {
			var c = value[i];
			bytes[i] = characterSet == InfoCharacterSet.Windows1252 && c > '\u007F'
				? (byte)Windows1252Byte (c)
				: (byte)c;
		}

		return bytes;
	}

	public static bool IsAscii (ReadOnlySpan<byte> bytes)
	{
		foreach (var b in bytes) {
			if (b > 0x7F)
				return false;
		}

		return true;
	}

	static bool TryUtf8 (ReadOnlySpan<byte> text, out string decoded)
	{
		var bytes = text.ToArray ();
		try {
			decoded = StrictUtf8.GetString (bytes, 0, bytes.Length);
			return true;
		} catch (System.Text.DecoderFallbackException) {
			decoded = "";
			return false;
		}
	}

	static string Latin1 (ReadOnlySpan<byte> text)
	{
		var chars = new char[text.Length];
		for (var i = 0; i < text.Length; i++)
			chars[i] = (char)text[i];
		return new string (chars);
	}

	static string Windows1252 (ReadOnlySpan<byte> text)
	{
		var chars = new char[text.Length];
		for (var i = 0; i < text.Length; i++) {
			var b = text[i];
			chars[i] = b is >= 0x80 and <= 0x9F ? Windows1252High[b - 0x80] : (char)b;
		}

		return new string (chars);
	}

	static int Windows1252Byte (char c)
	{
		if (c is >= '\u00A0' and <= '\u00FF')
			return c;
		var index = Array.IndexOf (Windows1252High, c);
		return index < 0 ? -1 : 0x80 + index;
	}
}
