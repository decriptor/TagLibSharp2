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

	/// <summary>CSET wCodePage 28591 (ISO 8859-1, with its C1 controls).</summary>
	Latin1,

	/// <summary>
	/// CSET wCodePage 0: the spec's "standard ISO 8859/1 code page ... without code points defined in hex
	/// columns 0, 1, 8, and 9". Read as ISO 8859-1; bytes 0x80-0x9F have no defined character.
	/// </summary>
	Latin1Default,

	/// <summary>CSET wCodePage 1252.</summary>
	Windows1252,

	/// <summary>Any other code page, which this library does not decode (for example 932 or 1251).</summary>
	Other
}

/// <summary>How surely a stored INFO value's text is known.</summary>
internal enum InfoTextKind
{
	/// <summary>Only ASCII bytes: the same text in every code page this library meets.</summary>
	Ascii,

	/// <summary>Valid UTF-8, under a CSET of 65001 or with no CSET.</summary>
	Utf8,

	/// <summary>
	/// Decoded exactly in a declared code page (CSET 28591, 0 or 1252) with no byte that code page
	/// leaves undefined, so the text can be written as UTF-8 without changing it.
	/// </summary>
	Exact,

	/// <summary>
	/// Text whose code page is a guess: no CSET and not UTF-8 (read as Windows-1252), a code page this
	/// library does not decode, or a byte the declared code page leaves undefined.
	/// </summary>
	Guess
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
/// way, as a best effort. <see cref="InfoTextKind"/> records which of these readings are exact.
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
	/// The text of a field up to its first NUL, and how surely it is known (<paramref name="kind"/>).
	/// </summary>
	public static string Decode (byte[] field, InfoCharacterSet characterSet, out InfoTextKind kind)
	{
		var end = Array.IndexOf (field, (byte)0);
		var length = end < 0 ? field.Length : end;
		var text = field.AsSpan (0, length);
		if (IsAscii (text)) {
			kind = InfoTextKind.Ascii;
			return Latin1 (text);
		}

		switch (characterSet) {
			case InfoCharacterSet.Latin1:
				kind = InfoTextKind.Exact;
				return Latin1 (text);
			case InfoCharacterSet.Latin1Default:
				kind = HasByteIn (text, IsC1) ? InfoTextKind.Guess : InfoTextKind.Exact;
				return Latin1 (text);
			case InfoCharacterSet.Windows1252:
				kind = HasByteIn (text, IsUnassigned1252) ? InfoTextKind.Guess : InfoTextKind.Exact;
				return Windows1252 (text);
		}

		if (TryUtf8 (field, length, out var decoded)) {
			kind = characterSet is InfoCharacterSet.Utf8 or InfoCharacterSet.None ? InfoTextKind.Utf8 : InfoTextKind.Guess;
			return decoded;
		}

		kind = InfoTextKind.Guess;
		return Windows1252 (text);
	}

	static bool IsC1 (byte b) => b is >= 0x80 and <= 0x9F;

	static bool IsUnassigned1252 (byte b) => b is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D;

	static bool HasByteIn (ReadOnlySpan<byte> text, Func<byte, bool> test)
	{
		foreach (var b in text) {
			if (test (b))
				return true;
		}

		return false;
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
				InfoCharacterSet.Latin1Default => c is >= '\u00A0' and <= '\u00FF',
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

	static bool TryUtf8 (byte[] bytes, int length, out string decoded)
	{
		try {
			decoded = StrictUtf8.GetString (bytes, 0, length);
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

	/// <summary>
	/// The Windows-1252 byte for <paramref name="c"/>, or -1. The five unassigned bytes (CP1252.TXT:
	/// 0x81, 0x8D, 0x8F, 0x90, 0x9D are UNDEFINED) are never written, because <see cref="Decode"/> reads
	/// them as undefined: whatever the encoder writes, the decoder reads back as exact.
	/// </summary>
	static int Windows1252Byte (char c)
	{
		if (c is >= '\u00A0' and <= '\u00FF')
			return c;
		var index = Array.IndexOf (Windows1252High, c);
		return index < 0 || IsUnassigned1252 ((byte)(0x80 + index)) ? -1 : 0x80 + index;
	}

	/// <summary>
	/// Why an unchanged value of kind <see cref="InfoTextKind.Guess"/> cannot be rewritten as UTF-8,
	/// for the refusal message. <paramref name="codePage"/> is the CSET's wCodePage, or -1 for none.
	/// </summary>
	public static string GuessReason (ReadOnlySpan<byte> field, InfoCharacterSet characterSet, int codePage)
	{
		var end = field.IndexOf ((byte)0);
		if (end >= 0)
			field = field.Slice (0, end);
		switch (characterSet) {
			case InfoCharacterSet.Latin1Default:
				foreach (var b in field) {
					if (IsC1 (b))
						return $"holds byte 0x{b:X2}, which the CSET's code page 0 (ISO 8859/1 without columns 8 and 9) leaves undefined";
				}

				break;
			case InfoCharacterSet.Windows1252:
				foreach (var b in field) {
					if (IsUnassigned1252 (b))
						return $"holds byte 0x{b:X2}, which code page 1252 leaves undefined";
				}

				break;
			case InfoCharacterSet.Other:
				return $"is in code page {codePage}, which this library does not decode";
		}

		return codePage < 0
			? "is not UTF-8 and the file has no CSET, so its code page is only a guess"
			: $"is not valid in the declared code page {codePage}";
	}

	/// <summary>The value with control characters (C0, DEL, C1) written as \uXXXX, for a message.</summary>
	public static string Escape (string value)
	{
		var builder = new System.Text.StringBuilder (value.Length);
		foreach (var c in value) {
			if (c < '\u0020' || c is >= '\u007F' and <= '\u009F')
				builder.Append ("\\u").Append (((int)c).ToString ("X4", System.Globalization.CultureInfo.InvariantCulture));
			else
				builder.Append (c);
		}

		return builder.ToString ();
	}
}
