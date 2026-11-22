// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Where a UTF-16 value with several strings puts the byte order marks of its later strings.
/// </summary>
internal enum Utf16BomPlacement : byte
{
	/// <summary>One BOM at the start of the value only.</summary>
	OneBom,

	/// <summary>A BOM before every non-empty string. An empty string has none.</summary>
	BomOnNonEmpty,

	/// <summary>A BOM before every string, empty ones included (ID3v2.3 §3.3, and the layout mutagen writes).</summary>
	BomOnEvery,
}

/// <summary>
/// The bytes of a UTF-16 value that its text does not hold: where its BOMs are, and how many terminators end it.
/// A frame read from a file keeps its layout, so an unchanged frame renders the bytes it was read from.
/// </summary>
internal readonly struct Utf16Layout : IEquatable<Utf16Layout>
{
	// Two bytes, so a frame that holds a layout is no larger than one that held a placement alone.
	readonly byte _terminators;

	public Utf16Layout (Utf16BomPlacement boms, int terminators)
	{
		Boms = boms;
		_terminators = (byte)Math.Min (terminators, byte.MaxValue);
	}

	public Utf16BomPlacement Boms { get; }

	/// <summary>The 00 00 pairs after the last string, up to 255. They are not text.</summary>
	public int Terminators => _terminators;

	public static Utf16Layout OneBom => default;

	public static Utf16Layout BomOnNonEmpty => new (Utf16BomPlacement.BomOnNonEmpty, 0);

	public static Utf16Layout BomOnEvery => new (Utf16BomPlacement.BomOnEvery, 0);

	public bool Equals (Utf16Layout other) => Boms == other.Boms && Terminators == other.Terminators;

	public override bool Equals (object? obj) => obj is Utf16Layout other && Equals (other);

	public override int GetHashCode () => ((int)Boms * 397) ^ Terminators;

	public static bool operator == (Utf16Layout left, Utf16Layout right) => left.Equals (right);

	public static bool operator != (Utf16Layout left, Utf16Layout right) => !left.Equals (right);
}

/// <summary>
/// The one decoder for encoding $01, "UTF-16 encoded Unicode with BOM" (https://id3.org/id3v2.4.0-structure §4).
/// </summary>
/// <remarks>
/// <para>
/// A value with several strings separates them with 00 00 on an even offset. Each string may begin with its own
/// BOM: ID3v2.3 §3.3 says "Unicode strings must begin with the Unicode BOM" (https://id3.org/id3v2.3.0), and
/// ID3v2.4 §4 says all strings in a frame share one byte order. A BOM is never part of the text. A string without
/// one uses the frame's first BOM, wherever it is, or little-endian when the frame has none.
/// </para>
/// <para>
/// A lone byte at the end is not a code unit and is dropped, so it cannot take the last character with it. The
/// 00 00 pairs after the last string are terminators, not an empty value. In <see cref="DecodeStrings"/>, a lone
/// byte right after a terminator still starts an empty last string.
/// </para>
/// <para>
/// <see cref="Decode(ReadOnlySpan{byte}, out Utf16Layout)"/> runs one code path on every target framework, so the
/// tests that run it on net8.0 and net10.0 also cover the netstandard2.0 build. It decodes into pooled buffers; the
/// only allocation is the result. <see cref="DecodeStrings"/> decodes each string with
/// <c>Encoding.GetString</c>, which on netstandard2.0 copies the span to an array first.
/// </para>
/// </remarks>
internal static class Utf16Text
{
	/// <summary>
	/// Decodes every string and joins them with U+0000.
	/// </summary>
	public static string Decode (ReadOnlySpan<byte> data) => Decode (data, out _);

	/// <inheritdoc cref="Decode(ReadOnlySpan{byte})"/>
	/// <param name="data">The encoded text.</param>
	/// <param name="layout">Where the value puts its BOMs and how many terminators end it.</param>
	public static string Decode (ReadOnlySpan<byte> data, out Utf16Layout layout)
	{
		data = WithoutLoneByte (data);
		var terminators = 0;
		while (data.Length >= 2 && data[data.Length - 2] == 0 && data[data.Length - 1] == 0) {
			data = data.Slice (0, data.Length - 2);
			terminators++;
		}

		if (data.IsEmpty) {
			layout = new Utf16Layout (Utf16BomPlacement.OneBom, terminators);
			return string.Empty;
		}

		var stats = new LayoutStats ();
		var text = DecodeJoined (data, ref stats);
		layout = new Utf16Layout (stats.Placement, terminators);
		return text;
	}

	/// <summary>
	/// Splits the value into its strings, each decoded as <see cref="Decode(ReadOnlySpan{byte})"/> decodes it.
	/// </summary>
	/// <returns>
	/// One entry per string. A final terminator ends the last string and adds no entry; an empty string between
	/// two terminators is kept. Data shorter than two bytes is one empty string.
	/// </returns>
	public static List<string> DecodeStrings (ReadOnlySpan<byte> data)
	{
		// A lone last byte is never text, but after a terminator it starts one more string, an empty one, so the
		// role before it still pairs (as the TIPL reader of the trailing-NUL fix reads it).
		var loneByte = (data.Length & 1) != 0;
		data = WithoutLoneByte (data);
		var strings = new List<string> ();
		var littleEndian = FrameIsLittleEndian (data);
		var start = 0;
		while (true) {
			var end = NextTerminator (data, start);
			var segment = end < 0 ? data.Slice (start) : data.Slice (start, end - start);
			if (end >= 0 || !segment.IsEmpty || start == 0 || loneByte)
				strings.Add (DecodeSegment (segment, littleEndian));
			if (end < 0)
				return strings;
			start = end + 2;
		}
	}

	/// <summary>
	/// Encodes little-endian with a BOM at the start, BOMs on later strings as <paramref name="layout"/> says,
	/// and its terminators after the last string.
	/// </summary>
	/// <param name="text">The text; U+0000 separates strings.</param>
	/// <param name="layout">Where to put the BOMs of later strings, and how many terminators to write.</param>
	public static BinaryData Encode (string text, Utf16Layout layout)
	{
		var terminators = layout.Terminators;
		if (terminators == 0 && (!HasSeparator (text) || layout.Boms == Utf16BomPlacement.OneBom && !LaterStringStartsLikeBom (text)))
			return BinaryData.FromStringUtf16 (text, includeBom: true);

		// A BOM for the first string, one for each later string that gets one, two bytes per char (a separator is
		// one char, 00 00), and the terminators.
		var boms = 1;
		for (var i = 0; i < text.Length; i++) {
			if (text[i] == '\0' && WantsBom (layout.Boms, text, i + 1))
				boms++;
		}

		var bytes = new byte[(boms + text.Length + terminators) * 2];
		bytes[0] = 0xFF;
		bytes[1] = 0xFE;
		var at = 2;
		for (var i = 0; i < text.Length; i++) {
			var c = text[i];
			bytes[at++] = (byte)c;
			bytes[at++] = (byte)(c >> 8);
			if (c == '\0' && WantsBom (layout.Boms, text, i + 1)) {
				bytes[at++] = 0xFF;
				bytes[at++] = 0xFE;
			}
		}
		// The rest of the array is already zero: the terminators.
		return new BinaryData (bytes);
	}

	/// <summary>
	/// Whether the string that starts at <paramref name="start"/> gets a BOM. A string whose text begins with
	/// U+FEFF or U+FFFE always gets one; without it, a reader takes that char for the string's BOM.
	/// </summary>
	static bool WantsBom (Utf16BomPlacement placement, string text, int start)
	{
		if (StartsLikeBom (text, start))
			return true;
		var stringIsEmpty = start == text.Length || text[start] == '\0';
		return placement == Utf16BomPlacement.BomOnEvery || (!stringIsEmpty && placement == Utf16BomPlacement.BomOnNonEmpty);
	}

	static bool StartsLikeBom (string text, int start) =>
		start < text.Length && text[start] is '\uFEFF' or '\uFFFE';

	static bool LaterStringStartsLikeBom (string text)
	{
		for (var i = 0; i < text.Length; i++) {
			if (text[i] == '\0' && StartsLikeBom (text, i + 1))
				return true;
		}
		return false;
	}

	static bool HasSeparator (string text)
	{
		foreach (var c in text) {
			if (c == '\0')
				return true;
		}
		return false;
	}

	/// <summary>
	/// A lone byte at the end is not a UTF-16 code unit. Pairing it with the byte before would eat the last
	/// character, so it is dropped.
	/// </summary>
	static ReadOnlySpan<byte> WithoutLoneByte (ReadOnlySpan<byte> data) =>
		(data.Length & 1) != 0 ? data.Slice (0, data.Length - 1) : data;

	/// <summary>
	/// The offset of the next 00 00 at an even offset from the start of the value, or -1.
	/// </summary>
	static int NextTerminator (ReadOnlySpan<byte> data, int start)
	{
		for (var i = start; i + 1 < data.Length; i += 2) {
			if (data[i] == 0 && data[i + 1] == 0)
				return i;
		}
		return -1;
	}

	static bool HasBom (ReadOnlySpan<byte> data) =>
		data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF));

	/// <summary>
	/// The byte order of the frame's first BOM at the start of any string, or little-endian when there is none.
	/// </summary>
	static bool FrameIsLittleEndian (ReadOnlySpan<byte> data)
	{
		var start = 0;
		while (start + 1 < data.Length) {
			if (HasBom (data.Slice (start)))
				return data[start] == 0xFF;
			var end = NextTerminator (data, start);
			if (end < 0)
				break;
			start = end + 2;
		}
		return true;
	}

	/// <summary>
	/// Decodes every string of <paramref name="data"/> into one string, the strings separated by U+0000.
	/// </summary>
	static string DecodeJoined (ReadOnlySpan<byte> data, ref LayoutStats stats)
	{
		var frameLittleEndian = FrameIsLittleEndian (data);

		// Every byte pair is at most one char, and every separator is one char.
		var bytes = ArrayPool<byte>.Shared.Rent (data.Length);
		var chars = ArrayPool<char>.Shared.Rent (data.Length / 2 + 1);
		try {
			data.CopyTo (bytes);
			var length = 0;
			var start = 0;
			while (true) {
				var end = NextTerminator (data, start);
				var segmentLength = (end < 0 ? data.Length : end) - start;
				var segment = data.Slice (start, segmentLength);
				if (start > 0)
					chars[length++] = '\0';
				stats.Count (start > 0, segment);

				var littleEndian = frameLittleEndian;
				var offset = start;
				if (HasBom (segment)) {
					littleEndian = segment[0] == 0xFF;
					offset += 2;
					segmentLength -= 2;
				}
				if (segmentLength > 0) {
					var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
					length += encoding.GetChars (bytes, offset, segmentLength, chars, length);
				}

				if (end < 0)
					break;
				start = end + 2;
			}
			return new string (chars, 0, length);
		} finally {
			ArrayPool<char>.Shared.Return (chars);
			ArrayPool<byte>.Shared.Return (bytes);
		}
	}

	static string DecodeSegment (ReadOnlySpan<byte> segment, bool frameLittleEndian)
	{
		var littleEndian = frameLittleEndian;
		if (HasBom (segment)) {
			littleEndian = segment[0] == 0xFF;
			segment = segment.Slice (2);
		}
		if (segment.IsEmpty)
			return string.Empty;
		return (littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString (segment);
	}

	/// <summary>
	/// Counts which strings after the first carry a BOM, to name the value's <see cref="Utf16BomPlacement"/>.
	/// </summary>
	struct LayoutStats
	{
		int _nonEmpty;
		int _nonEmptyWithBom;
		int _empty;
		int _emptyWithBom;

		public void Count (bool later, ReadOnlySpan<byte> segment)
		{
			if (!later)
				return;
			var hasBom = HasBom (segment);
			if (segment.Length > (hasBom ? 2 : 0)) {
				_nonEmpty++;
				if (hasBom)
					_nonEmptyWithBom++;
			} else {
				_empty++;
				if (hasBom)
					_emptyWithBom++;
			}
		}

		public readonly Utf16BomPlacement Placement {
			get {
				var everyEmptyHasBom = _empty > 0 && _emptyWithBom == _empty;
				if (_nonEmpty > 0 && _nonEmptyWithBom == _nonEmpty)
					return everyEmptyHasBom ? Utf16BomPlacement.BomOnEvery : Utf16BomPlacement.BomOnNonEmpty;
				return _nonEmpty == 0 && everyEmptyHasBom ? Utf16BomPlacement.BomOnEvery : Utf16BomPlacement.OneBom;
			}
		}
	}
}
