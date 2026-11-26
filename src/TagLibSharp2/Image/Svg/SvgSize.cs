// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Image.Svg;

/// <summary>
/// The width and height of an SVG: the root element's width and height attributes in absolute units, or its
/// viewBox (SVG 1.1 §7.2, §7.7). Only the root start tag is decoded, never the document.
/// </summary>
static class SvgSize
{
	// Reads only until the first code unit that cannot start a prolog, so an audio buffer stops at byte 0.
	public static bool IsSvg (ReadOnlySpan<byte> data) => TrySvgRoot (new Markup (data), out _);

	public static bool TryRead (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		var markup = new Markup (data);
		if (!TrySvgRoot (markup, out var start))
			return false;

		// Only the root start tag is decoded, never the document.
		var end = TagEnd (markup, start);
		var tag = markup.Decode (start, end);
		if (TrySvgLength (tag, "width", out var width) && TrySvgLength (tag, "height", out var height))
			properties = new ImageProperties (width, height, 0, "SVG");
		else if (TryViewBox (tag, out width, out height))
			properties = new ImageProperties (width, height, 0, "SVG");

		return true;
	}

	// XML 1.0 §2.8 prolog: an XML declaration, then comments, processing instructions, white space and one
	// doctype, then the root element. The scan reads code units in place (UTF-8, or UTF-16 after a BOM) and
	// compares ASCII only, so nothing is decoded or copied.
	static bool TrySvgRoot (Markup text, out int start)
	{
		start = 0;
		var i = 0;
		if (text.StartsAt (i, "<?xml") && !text.SkipTo (ref i, "?>"))
			return false;
		while (i < text.Length) {
			while (i < text.Length && IsXmlSpace (text[i]))
				i++;
			if (i >= text.Length)
				return false;
			if (text.StartsAt (i, "<!--")) {
				if (!text.SkipTo (ref i, "-->"))
					return false;
				continue;
			}

			if (text.StartsAt (i, "<!DOCTYPE")) {
				if (!SkipDoctype (text, ref i))
					return false;
				continue;
			}

			if (text.StartsAt (i, "<?")) {
				if (!text.SkipTo (ref i, "?>"))
					return false;
				continue;
			}

			if (IsSvgRootAt (text, i)) {
				start = i;
				return true;
			}

			return false;
		}

		return false;
	}

	static bool IsSvgRootAt (Markup text, int i)
	{
		if (!text.StartsAt (i, "<svg"))
			return false;
		var n = i + 4;
		if (n < text.Length && text[n] == ':') {
			if (!text.StartsAt (n + 1, "svg"))
				return false;
			n += 4;
		}

		return n >= text.Length || IsXmlSpace (text[n]) || text[n] is '>' or '/';
	}

	// The code unit after the '>' that closes the start tag at start, skipping quoted attribute values, or the
	// end of the scanned window.
	static int TagEnd (Markup text, int start)
	{
		var quote = 0;
		for (var i = start; i < text.Length; i++) {
			var c = text[i];
			if (quote != 0) {
				if (c == quote)
					quote = 0;
			} else if (c is '"' or '\'') {
				quote = c;
			} else if (c == '>') {
				return i;
			}
		}

		return text.Length;
	}

	static bool SkipDoctype (Markup text, ref int index)
	{
		var quote = 0;
		var depth = 0;
		while (index < text.Length) {
			var value = text[index++];
			if (quote != 0) {
				if (value == quote)
					quote = 0;
				continue;
			}

			if (value is '\'' or '"') {
				quote = value;
				continue;
			}

			if (value == '[')
				depth++;
			else if (value == ']' && depth > 0)
				depth--;
			else if (value == '>' && depth == 0)
				return true;
		}

		return false;
	}

	// XML 1.0 §2.3 S: space, tab, carriage return, line feed.
	static bool IsXmlSpace (int value) => value is ' ' or '\t' or '\r' or '\n';

	const int SvgScanBytes = 64 * 1024;

	/// <summary>
	/// The first <see cref="SvgScanBytes"/> of a markup file as code units: bytes for UTF-8 (after an optional
	/// BOM), or 16-bit units after a UTF-16 BOM. A unit above 0x7F never equals an ASCII token.
	/// </summary>
	readonly ref struct Markup
	{
		readonly ReadOnlySpan<byte> _data;
		readonly int _unit;
		readonly bool _bigEndian;

		public Markup (ReadOnlySpan<byte> data)
		{
			var window = data.Slice (0, Math.Min (data.Length, SvgScanBytes));
			if (window.Length >= 2 && window[0] == 0xFF && window[1] == 0xFE) {
				_unit = 2;
				_bigEndian = false;
				window = window.Slice (2);
			} else if (window.Length >= 2 && window[0] == 0xFE && window[1] == 0xFF) {
				_unit = 2;
				_bigEndian = true;
				window = window.Slice (2);
			} else {
				_unit = 1;
				_bigEndian = false;
				if (window.Length >= 3 && window[0] == 0xEF && window[1] == 0xBB && window[2] == 0xBF)
					window = window.Slice (3);
			}

			_data = window;
			Length = window.Length / _unit;
		}

		public int Length { get; }

		public int this[int index] =>
			_unit == 1
				? _data[index]
				: _bigEndian ? (_data[2 * index] << 8) | _data[(2 * index) + 1] : _data[2 * index] | (_data[(2 * index) + 1] << 8);

		/// <summary>ASCII case-insensitive match of <paramref name="token"/> at <paramref name="index"/>.</summary>
		public bool StartsAt (int index, string token)
		{
			if (index < 0 || index > Length - token.Length)
				return false;
			for (var n = 0; n < token.Length; n++) {
				var c = this[index + n];
				if (c is >= 'A' and <= 'Z')
					c += 'a' - 'A';
				var t = token[n];
				if (t is >= 'A' and <= 'Z')
					t = (char)(t + ('a' - 'A'));
				if (c != t)
					return false;
			}

			return true;
		}

		public bool SkipTo (ref int index, string marker)
		{
			for (var i = index; i <= Length - marker.Length; i++) {
				var match = true;
				for (var n = 0; n < marker.Length && match; n++)
					match = this[i + n] == marker[n];
				if (match) {
					index = i + marker.Length;
					return true;
				}
			}

			return false;
		}

		public string Decode (int start, int end)
		{
			var bytes = _data.Slice (start * _unit, (end - start) * _unit);
			if (_unit == 1)
				return Encoding.UTF8.GetString (bytes);
			return (_bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString (bytes);
		}
	}

	static bool TrySvgLength (string tag, string name, out int value)
	{
		value = 0;
		var key = FindAttribute (tag, name);
		if (key < 0)
			return false;
		var i = key + name.Length;
		while (i < tag.Length && (tag[i] == ' ' || tag[i] == '\t' || tag[i] == '='))
			i++;
		if (i >= tag.Length)
			return false;
		char quote = tag[i] is '\'' or '"' ? tag[i++] : '\0';
		var start = i;
		if (!TrySkipNumber (tag, ref i))
			return false;
		var unitStart = i;
		while (i < tag.Length && char.IsLetter (tag[i]))
			i++;
		if (i < tag.Length && tag[i] == '%')
			return false;
		if (!TryParseNumber (tag, start, unitStart - start, out var number) || !TrySvgPixels (number, tag.Substring (unitStart, i - unitStart), out value))
			return false;
		return quote == '\0' || (i < tag.Length && tag[i] == quote);
	}

	// Digits with at most one dot, then an optional exponent with digits: false when no digit was read.
	static bool TrySkipNumber (string tag, ref int i)
	{
		var start = i;
		var sawDigit = false;
		var sawDot = false;
		while (i < tag.Length && (char.IsDigit (tag[i]) || (tag[i] == '.' && !sawDot))) {
			sawDigit |= tag[i] != '.';
			sawDot |= tag[i] == '.';
			i++;
		}

		if (i < tag.Length && tag[i] is 'e' or 'E') {
			var exponent = i + 1;
			if (exponent < tag.Length && tag[exponent] is '+' or '-')
				exponent++;
			var digits = exponent;
			while (digits < tag.Length && char.IsDigit (tag[digits]))
				digits++;
			if (digits > exponent) {
				sawDigit = true;
				i = digits;
			}
		}

		return sawDigit && i != start;
	}

	static bool TryParseNumber (string tag, int start, int length, out double number) =>
#if NETSTANDARD2_0
		double.TryParse (tag.Substring (start, length), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
#else
		double.TryParse (tag.AsSpan (start, length), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
#endif

	static bool TrySvgPixels (double number, string unit, out int value)
	{
		value = 0;
		var pixels = unit.ToUpperInvariant () switch {
			"" or "PX" => number,
			"IN" => number * 96d,
			"CM" => number * 96d / 2.54d,
			"MM" => number * 96d / 25.4d,
			"PT" => number * 96d / 72d,
			"PC" => number * 16d,
			_ => double.NaN
		};
		if (double.IsNaN (pixels) || pixels <= 0 || pixels > int.MaxValue)
			return false;
		value = (int)Math.Round (pixels);
		return value > 0;
	}

	static int FindAttribute (string tag, string name)
	{
		var from = 0;
		while (from < tag.Length) {
			var key = tag.IndexOf (name, from, StringComparison.OrdinalIgnoreCase);
			if (key < 0)
				return -1;
			var before = key == 0 ? ' ' : tag[key - 1];
			if (before is ' ' or '\t' or '\r' or '\n')
				return key;
			from = key + name.Length;
		}

		return -1;
	}

	static bool TryViewBox (string tag, out int width, out int height)
	{
		width = 0;
		height = 0;
		var key = FindAttribute (tag, "viewBox");
		if (key < 0)
			return false;
		var i = key + "viewBox".Length;
		while (i < tag.Length && tag[i] != '"' && tag[i] != '\'')
			i++;
		if (i >= tag.Length)
			return false;
		var quote = tag[i++];
		var end = tag.IndexOf (quote, i);
		if (end < 0)
			return false;
		var parts = tag.Substring (i, end - i).Replace (',', ' ').Split ((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 4 && TryWhole (parts[2], out width) && TryWhole (parts[3], out height);
	}

	static bool TryWhole (string text, out int value)
	{
		var length = 0;
		while (length < text.Length && text[length] != '.')
			length++;
#if NETSTANDARD2_0
		var whole = length == text.Length ? text : text.Substring (0, length);
		return int.TryParse (whole, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
#else
		return int.TryParse (text.AsSpan (0, length), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
#endif
	}
}
