// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Image.Png;

/// <summary>
/// The PNG text chunks (PNG 3rd ed. §11.3.3): tEXt (Latin-1), zTXt (zlib-compressed Latin-1) and iTXt (UTF-8,
/// optionally compressed, with a language tag and a translated keyword). Inflation shares one budget per file.
/// </summary>
static class PngText
{
	/// <summary>All the text a file's compressed chunks may inflate to, together.</summary>
	internal const int MaxInflatedText = 1024 * 1024;

	/// <summary>The keyword (1-79 Latin-1 characters, then a null) and where the rest of the payload starts.</summary>
	public static bool TryKeyword (ReadOnlySpan<byte> payload, out string keyword, out int rest)
	{
		keyword = "";
		rest = 0;
		var end = 0;
		while (end < payload.Length && payload[end] != 0)
			end++;
		if (end == 0 || end >= payload.Length || end > 79)
			return false;
		keyword = Polyfills.Latin1.GetString (payload.Slice (0, end));
		rest = end + 1;
		return true;
	}

	/// <summary>The text of a chunk after its keyword's null, decoded.</summary>
	public static bool TryText (ReadOnlySpan<byte> type, ReadOnlySpan<byte> rest, ref int budget, out string? text)
	{
		text = null;
		if (type.SequenceEqual ("tEXt"u8)) {
			text = Polyfills.Latin1.GetString (rest);
			return true;
		}

		if (type.SequenceEqual ("zTXt"u8)) {
			if (rest.Length < 1 || rest[0] != 0 || !TryInflate (rest.Slice (1), ref budget, out var raw))
				return false;
			text = Polyfills.Latin1.GetString (raw);
			return true;
		}

		if (!TryInternationalHeader (rest, out var compressed, out var i))
			return false;
		var body = rest.Slice (i);
		if (compressed) {
			if (!TryInflate (body, ref budget, out var raw))
				return false;
			body = raw;
		}

		text = Encoding.UTF8.GetString (body.ToArray ());
		return true;
	}

	// The text after the keyword's null, inflated when compressed. header is what sits between the keyword's null
	// and the text: the zTXt method byte, or the iTXt flag, method, language tag and translated keyword. overBudget
	// is set when the text was not read because it would pass the inflation budget.
	public static bool TryTextBody (ReadOnlySpan<byte> type, ReadOnlySpan<byte> rest, ref int budget, out byte[] body, out byte[] header, out bool overBudget)
	{
		body = [];
		header = [];
		overBudget = false;
		if (type.SequenceEqual ("tEXt"u8)) {
			body = rest.ToArray ();
			return true;
		}

		if (type.SequenceEqual ("zTXt"u8)) {
			if (rest.Length < 1 || rest[0] != 0 || !TryInflate (rest.Slice (1), ref budget, out body, out overBudget))
				return false;
			header = [0];
			return true;
		}

		if (!TryInternationalHeader (rest, out var compressed, out var i))
			return false;
		header = rest.Slice (0, i).ToArray ();
		if (compressed)
			return TryInflate (rest.Slice (i), ref budget, out body, out overBudget);
		body = rest.Slice (i).ToArray ();
		return true;
	}

	// iTXt after the keyword: compression flag, method, language tag and null, translated keyword and null.
	static bool TryInternationalHeader (ReadOnlySpan<byte> rest, out bool compressed, out int textAt)
	{
		compressed = false;
		textAt = 2;
		if (rest.Length < 2)
			return false;
		compressed = rest[0] == 1;
		return SkipToNull (rest, ref textAt) && SkipToNull (rest, ref textAt);
	}

	static bool SkipToNull (ReadOnlySpan<byte> data, ref int i)
	{
		while (i < data.Length && data[i] != 0)
			i++;
		if (i >= data.Length)
			return false;
		i++;
		return true;
	}

	/// <summary>Inflates a zlib stream (RFC 1950), taking its size from the shared budget.</summary>
	public static bool TryInflate (ReadOnlySpan<byte> zlib, ref int budget, out byte[] raw) => TryInflate (zlib, ref budget, out raw, out _);

	// overBudget: set when the data was not inflated because it would pass the budget.
	public static bool TryInflate (ReadOnlySpan<byte> zlib, ref int budget, out byte[] raw, out bool overBudget)
	{
		raw = [];
		overBudget = budget <= 0;
		if (overBudget || zlib.Length < 6 || (zlib[0] & 0x0F) != 8)
			return false;
		var header = (zlib[1] & 0x20) == 0 ? 2 : 6;
		if (zlib.Length < header + 4)
			return false;
		try {
			using var input = new MemoryStream (zlib.Slice (header, zlib.Length - header - 4).ToArray ());
			using var stream = new DeflateStream (input, CompressionMode.Decompress);
			if (!TryReadAll (stream, budget, out raw)) {
				overBudget = true;
				return false;
			}

			budget -= raw.Length;
			return true;
		} catch (InvalidDataException) {
			return false;
		} catch (IOException) {
			return false;
		}
	}

	static bool TryReadAll (Stream stream, int budget, out byte[] raw)
	{
		raw = [];
		using var output = new MemoryStream ();
		var buffer = new byte[8192];
		var total = 0;
		while (true) {
			var read = stream.Read (buffer, 0, buffer.Length);
			if (read == 0)
				break;
			if ((long)total + read > budget)
				return false;
			total += read;
			output.Write (buffer, 0, read);
		}

		raw = output.ToArray ();
		return true;
	}

	/// <summary>Compresses <paramref name="raw"/> as a zlib stream (RFC 1950) for zTXt or a compressed iTXt.</summary>
	internal static byte[] Zlib (byte[] raw)
	{
		using var output = new MemoryStream ();
		output.WriteByte (0x78);
		output.WriteByte (0x9C);
		using (var deflate = new DeflateStream (output, CompressionLevel.Optimal, leaveOpen: true))
			deflate.Write (raw, 0, raw.Length);
		uint a = 1, b = 0;
		foreach (var value in raw) {
			a = (a + value) % 65521;
			b = (b + a) % 65521;
		}

		var adler = (b << 16) | a;
		output.WriteByte ((byte)(adler >> 24));
		output.WriteByte ((byte)(adler >> 16));
		output.WriteByte ((byte)(adler >> 8));
		output.WriteByte ((byte)adler);
		return output.ToArray ();
	}

	/// <summary>Rebuilds a text chunk of the same type around new text. Compression and the iTXt language tags are kept.</summary>
	public static byte[] Rebuild (ReadOnlySpan<byte> type, string keyword, byte[] header, byte[] text)
	{
		var key = Polyfills.Latin1.GetBytes (keyword);
		var compressed = type.SequenceEqual ("zTXt"u8) || (type.SequenceEqual ("iTXt"u8) && header.Length > 0 && header[0] == 1);
		var body = compressed ? Zlib (text) : text;
		var payload = new byte[key.Length + 1 + header.Length + body.Length];
		key.CopyTo (payload, 0);
		header.CopyTo (payload, key.Length + 1);
		body.CopyTo (payload, key.Length + 1 + header.Length);
		return PngChunks.Chunk (type, payload);
	}

	/// <summary>A new text chunk: tEXt when the value is Latin-1, otherwise an uncompressed iTXt.</summary>
	public static void Add (BinaryDataBuilder builder, string keyword, string value)
	{
		if (IsLatin1 (value))
			PngChunks.Add (builder, "tEXt"u8, Latin1Text (keyword, value));
		else
			PngChunks.Add (builder, "iTXt"u8, InternationalText (keyword, value));
	}

	static byte[] Latin1Text (string keyword, string value)
	{
		var key = Polyfills.Latin1.GetBytes (keyword);
		var text = Polyfills.Latin1.GetBytes (value);
		var payload = new byte[key.Length + 1 + text.Length];
		key.CopyTo (payload, 0);
		text.CopyTo (payload, key.Length + 1);
		return payload;
	}

	static byte[] InternationalText (string keyword, string value)
	{
		var key = Encoding.ASCII.GetBytes (keyword);
		var text = Encoding.UTF8.GetBytes (value);
		var payload = new byte[key.Length + 5 + text.Length];
		key.CopyTo (payload, 0);
		text.CopyTo (payload, key.Length + 5);
		return payload;
	}

	static bool IsLatin1 (string value)
	{
		foreach (var c in value) {
			if (c > 0xFF)
				return false;
		}

		return true;
	}
}
