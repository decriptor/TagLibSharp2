// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml;

namespace TagLibSharp2.Image.Xmp;

/// <summary>
/// The XML 1.0 rules the XMP splice needs on packet text: characters and white space (§2.2, §2.3), escaping, where
/// a tag or an attribute value ends, and offsets from the reader's line and column (§2.11).
/// </summary>
static class XmlText
{
	// IXmlLineInfo counts a line break as CR LF, CR or LF (XML 1.0 §2.11) and columns from 1.
	public static int[] LineStarts (string text)
	{
		var starts = new List<int> { 0 };
		for (var i = 0; i < text.Length; i++) {
			if (text[i] == '\r') {
				if (i + 1 < text.Length && text[i + 1] == '\n')
					i++;
				starts.Add (i + 1);
			} else if (text[i] == '\n') {
				starts.Add (i + 1);
			}
		}

		return [.. starts];
	}

	public static int Offset (int[] lines, int line, int position)
	{
		if (line < 1 || line > lines.Length || position < 1)
			return -1;
		return lines[line - 1] + position - 1;
	}

	public static bool At (string text, int index, string token) =>
		index >= 0 && index <= text.Length - token.Length && string.CompareOrdinal (text, index, token, 0, token.Length) == 0;

	// The index just past the '>' that closes a tag, skipping quoted attribute values.
	public static bool TryTagEnd (string text, int from, out int end)
	{
		end = -1;
		var quote = '\0';
		for (var i = from; i < text.Length; i++) {
			var c = text[i];
			if (quote != '\0') {
				if (c == quote)
					quote = '\0';
			} else if (c is '"' or '\'') {
				quote = c;
			} else if (c == '>') {
				end = i + 1;
				return true;
			}
		}

		return false;
	}

	public static bool TryAttributeEnd (string text, int from, out int end)
	{
		end = -1;
		var i = from;
		while (i < text.Length && IsXmlSpace (text[i]))
			i++;
		if (i >= text.Length || text[i] != '=')
			return false;
		i++;
		while (i < text.Length && IsXmlSpace (text[i]))
			i++;
		if (i >= text.Length || text[i] is not ('"' or '\''))
			return false;
		var close = text.IndexOf (text[i], i + 1);
		if (close < 0)
			return false;
		end = close + 1;
		return true;
	}

	public static bool IsXmlSpace (char c) => c is ' ' or '\t' or '\r' or '\n';

	public static bool IsWellFormed (string packet)
	{
		var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
		try {
			using var reader = XmlReader.Create (new StringReader (packet), settings);
			while (reader.Read ()) {
			}

			return true;
		} catch (XmlException) {
			return false;
		}
	}

	// XML 1.0 §2.2 Char: tab, LF, CR, U+0020-U+D7FF, U+E000-U+FFFD and paired surrogates.
	public static bool IsXmlText (string value)
	{
		for (var i = 0; i < value.Length; i++) {
			var c = value[i];
			if (char.IsHighSurrogate (c)) {
				if (i + 1 >= value.Length || !char.IsLowSurrogate (value[i + 1]))
					return false;
				i++;
				continue;
			}

			if (c is '\t' or '\n' or '\r')
				continue;
			if (c < 0x20 || char.IsLowSurrogate (c) || c is '￾' or '￿')
				return false;
		}

		return true;
	}

	public static string EscapeXml (string value, bool attribute)
	{
		var builder = new StringBuilder (value.Length + 16);
		foreach (var c in value) {
			switch (c) {
				case '&':
					builder.Append ("&amp;");
					break;
				case '<':
					builder.Append ("&lt;");
					break;
				case '>':
					builder.Append ("&gt;");
					break;
				case '"' when attribute:
					builder.Append ("&quot;");
					break;
				case '\r':
					builder.Append ("&#xD;");
					break;
				case '\n' when attribute:
					builder.Append ("&#xA;");
					break;
				case '\t' when attribute:
					builder.Append ("&#x9;");
					break;
				default:
					builder.Append (c);
					break;
			}
		}

		return builder.ToString ();
	}
}
