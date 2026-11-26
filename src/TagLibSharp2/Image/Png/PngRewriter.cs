// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Xmp;

namespace TagLibSharp2.Image.Png;

/// <summary>
/// Reads a PNG's text fields and splices edited text into its chunk list: new text before the pixel data, changed
/// keywords dropped, and the XMP and Exif copies of a changed field updated. Every other chunk, and anything after
/// IEND, is copied as it is.
/// </summary>
static class PngRewriter
{
	const string XmpKeyword = "XML:com.adobe.xmp";
	const string RawXmpKeyword = "Raw profile type xmp";
	const string RawExifKeyword = "Raw profile type exif";
	const string RawApp1Keyword = "Raw profile type APP1";

	// Clear keeps what a decoder or colour management needs to show the pixels and drops every other
	// ancillary chunk (rule 7: mapped and unmapped, known and unknown). Kept:
	// - every critical chunk (PNG 3rd ed. §5.4: an uppercase first letter; a decoder must understand it);
	// - tRNS, gAMA, cHRM, sRGB, iCCP, cICP, mDCV, cLLI, sBIT (§11.3.1-§11.3.2: transparency, colour space,
	//   HDR mastering and content light level, significant bits);
	// - bKGD, hIST, pHYs, sPLT (§11.3.5: background, palette histogram, pixel aspect, suggested palette);
	// - acTL, fcTL, fdAT (§11.3.6: APNG animation; fdAT holds frame pixels);
	// - oFFs, pCAL, sCAL, sTER (PNG Extensions 1.6.0: position, pixel calibration, scale, stereo layout).
	// Dropped include tEXt, zTXt, iTXt (XMP and ImageMagick's raw EXIF/IPTC/XMP profiles), eXIf, zxIf, tIME,
	// caBX (a C2PA manifest, which any edit invalidates: C2PA 2.1 §14.3.3), dSIG, and Apple's iDOT.
	static readonly string[] KeptOnClear = [
		"tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "cICP", "mDCV", "cLLI", "sBIT",
		"bKGD", "hIST", "pHYs", "sPLT", "acTL", "fcTL", "fdAT", "oFFs", "pCAL", "sCAL", "sTER",
	];

	static readonly ImageStores[] TextFields = [
		ImageStores.Title, ImageStores.Artist, ImageStores.Description, ImageStores.Copyright, ImageStores.Year, ImageStores.Comment,
	];

	public static void Read (ReadOnlySpan<byte> data, ImageTag tag)
	{
		if (!PngChunks.TryWalk (data, out var chunks))
			return;
		var budget = PngText.MaxInflatedText;
		string? artist = null;
		foreach (var chunk in chunks)
			ReadChunk (data.Slice (chunk.Start, chunk.Length), tag, ref budget, ref artist);

		// exiftool writes its Artist tag to a PNG as the keyword "Artist". PNG 3rd ed. §11.3.3.2 predefines
		// "Author" for the same field, which wins when both are present.
		if (tag.Artist is null && artist is not null)
			tag.ReadField (ImageStores.Artist, artist);
	}

	static void ReadChunk (ReadOnlySpan<byte> chunk, ImageTag tag, ref int budget, ref string? artist)
	{
		if (chunk.Length < 12)
			return;
		var length = PngChunks.ReadLength (chunk, 0);
		if (length < 0 || 12 + length != chunk.Length)
			return;
		var type = chunk.Slice (4, 4);
		if (!PngChunks.IsTextChunk (type) || !PngChunks.CrcMatches (chunk))
			return;
		var payload = chunk.Slice (8, length);
		if (!PngText.TryKeyword (payload, out var keyword, out var rest))
			return;
		var field = PngKeywords.FieldFor (keyword);
		if (field == ImageStores.None && keyword != PngKeywords.ExiftoolArtist)
			return;
		if (!PngText.TryText (type, payload.Slice (rest), ref budget, out var text))
			return;
		if (field == ImageStores.None)
			artist = string.IsNullOrEmpty (text) ? artist : text;
		else
			tag.ReadField (field, text);
	}

	/// <summary>Every text field (or only the changed ones) as new text chunks, in a fixed order.</summary>
	public static void AddFields (BinaryDataBuilder builder, ImageTag tag, bool onlyChanged)
	{
		foreach (var field in TextFields) {
			if ((onlyChanged && !tag.Changed (field)) || tag.Field (field) is not string value)
				continue;
			PngText.Add (builder, PngKeywords.Keyword (field), value);
		}
	}

	public static byte[]? Rewrite (ReadOnlySpan<byte> data, ImageTag tag, out string? error)
	{
		error = null;
		if (!PngChunks.TryWalk (data, out var chunks) || chunks.Count == 0 || !PngChunks.TypeEquals (data, chunks[^1], "IEND"u8)) {
			error = "The PNG chunk list does not reach IEND, so the text cannot be spliced in.";
			return null;
		}

		var layout = Layout.Of (data, chunks);
		var output = TryCopyChunks (data, chunks, tag, layout, out var outIdot, out var outIdat, out error);
		return output is null ? null : DropStaleIdot (output, layout, outIdot, outIdat);
	}

	// Where new text goes: before the pixel data, and before Apple's iDOT, which records where the IDAT chunks are.
	readonly record struct Layout (int Insert, int Idot, int Idat)
	{
		public static Layout Of (ReadOnlySpan<byte> data, List<PngChunk> chunks)
		{
			var insert = chunks[^1].Start;
			var idot = -1;
			var idat = -1;
			foreach (var chunk in chunks) {
				if (PngChunks.TypeEquals (data, chunk, "IDAT"u8)) {
					idat = chunk.Start;
					insert = idot >= 0 ? idot : chunk.Start;
					break;
				}

				if (PngChunks.TypeEquals (data, chunk, "iDOT"u8))
					idot = chunk.Start;
			}

			return new Layout (insert, idot, idat);
		}
	}

	static byte[]? TryCopyChunks (ReadOnlySpan<byte> data, List<PngChunk> chunks, ImageTag tag, Layout layout, out int outIdot, out int outIdat, out string? error)
	{
		(outIdot, outIdat, error) = (-1, -1, null);
		var mirrors = !tag.DropUnmapped && MirrorFields.AnyChanged (tag);
		// One inflation budget for every compressed copy in the file, as when it is read.
		var budget = PngText.MaxInflatedText;
		using var builder = new BinaryDataBuilder (data.Length + 64);
		builder.Add (data.Slice (0, 8));
		foreach (var chunk in chunks) {
			if (chunk.Start == layout.Insert)
				AddFields (builder, tag, onlyChanged: true);
			if (chunk.Start == layout.Idot)
				outIdot = builder.Length;
			if (chunk.Start == layout.Idat)
				outIdat = builder.Length;
			if (IsDropped (data, chunk, tag))
				continue;
			byte[]? edited = null;
			if (mirrors && !TryMirrorChunk (data.Slice (chunk.Start, chunk.Length), tag, ref budget, out edited, out error))
				return null;
			if (edited is not null)
				builder.Add (edited);
			else
				builder.Add (data.Slice (chunk.Start, chunk.Length));
		}

		// PNG 3rd ed. §5.6: IEND is the last chunk. Whatever follows it (an appended archive, padding) is not
		// the PNG's to change, so it is copied as it is.
		builder.Add (data.Slice (chunks[^1].Start + chunks[^1].Length));
		return builder.ToArray ();
	}

	// iDOT holds the offset of the IDAT data from itself. When a chunk between them changed, the offset is wrong,
	// so iDOT is dropped (a decoder then reads the IDAT chunks in order, as it does for any PNG).
	static byte[] DropStaleIdot (byte[] output, Layout layout, int outIdot, int outIdat)
	{
		var kept = outIdot >= 0 && outIdot + 8 <= output.Length && output.AsSpan (outIdot + 4, 4).SequenceEqual ("iDOT"u8);
		if (!kept || layout.Idat < 0 || outIdat - outIdot == layout.Idat - layout.Idot)
			return output;
		var size = 12 + PngChunks.ReadLength (output, outIdot);
		var trimmed = new byte[output.Length - size];
		output.AsSpan (0, outIdot).CopyTo (trimmed);
		output.AsSpan (outIdot + size).CopyTo (trimmed.AsSpan (outIdot));
		return trimmed;
	}

	static bool IsKeptOnClear (ReadOnlySpan<byte> type)
	{
		if ((type[0] & 0x20) == 0)
			return true;
		foreach (var kept in KeptOnClear) {
			if (type[0] == kept[0] && type[1] == kept[1] && type[2] == kept[2] && type[3] == kept[3])
				return true;
		}

		return false;
	}

	static bool IsDropped (ReadOnlySpan<byte> data, PngChunk chunk, ImageTag tag)
	{
		var type = data.Slice (chunk.Start + 4, 4);
		if (tag.DropUnmapped)
			return !IsKeptOnClear (type);
		if (!PngChunks.IsTextChunk (type))
			return false;
		var payload = data.Slice (chunk.Start + 8, chunk.Length - 12);
		if (!PngText.TryKeyword (payload, out var keyword, out _))
			return false;
		var field = PngKeywords.FieldFor (keyword);
		if (field != ImageStores.None)
			return tag.Changed (field);
		return PngKeywords.IsMirror (keyword, tag);
	}

	// A changed field is also changed in the XMP packet and the Exif profile that copy it, so that no reader shows
	// the old value from there. A chunk with a bad CRC is not read by PNG decoders and is copied as it is.
	static bool TryMirrorChunk (ReadOnlySpan<byte> whole, ImageTag tag, ref int budget, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		var type = whole.Slice (4, 4);
		if (!PngChunks.CrcMatches (whole))
			return true;
		var payload = whole.Slice (8, whole.Length - 12);
		if (type.SequenceEqual ("eXIf"u8))
			return new ExifCarrier ("eXIf", exif => PngChunks.Chunk ("eXIf"u8, exif)).TryEdit (payload, tag, out edited, out error);
		if (!PngChunks.IsTextChunk (type) || !PngText.TryKeyword (payload, out var keyword, out var rest))
			return true;
		if (keyword is not (XmpKeyword or RawXmpKeyword or RawExifKeyword or RawApp1Keyword))
			return true;
		// Text no reader can decode (not zlib, or a broken international header) holds no copy: it is kept. Text that
		// would pass the inflation budget may hold one, and cannot be checked.
		if (!PngText.TryTextBody (type, payload.Slice (rest), ref budget, out var body, out var header, out var overBudget)) {
			if (!overBudget)
				return true;
			error = $"The PNG {keyword} chunk does not fit in the {PngText.MaxInflatedText / (1024 * 1024)} MiB that all compressed text in the file shares, so its copy of a changed field cannot be updated.";
			return false;
		}

		var text = new TextChunk (type.ToArray (), keyword, header);
		if (keyword != XmpKeyword)
			return TryMirrorRawProfile (text, body, tag, out edited, out error);
		if (!TryMirrorXmp (Encoding.UTF8.GetString (body), tag, out var packet, out error))
			return false;
		edited = packet is null ? null : text.Build (Encoding.UTF8.GetBytes (packet));
		return true;
	}

	// A text chunk's type, keyword and the header between the keyword and the text, to rebuild it around new text.
	sealed class TextChunk (byte[] type, string keyword, byte[] header)
	{
		public string Keyword { get; } = keyword;

		public byte[] Build (byte[] text) => PngText.Rebuild (type, Keyword, header, text);
	}

	// The updated packet, or null when it did not change.
	static bool TryMirrorXmp (string packet, ImageTag tag, out string? updated, out string? error)
	{
		updated = null;
		if (!XmpMirror.TryUpdate (packet, tag, "PNG XMP packet", out var result, out error))
			return false;
		if (!ReferenceEquals (result, packet))
			updated = result;
		return true;
	}

	static bool TryMirrorRawProfile (TextChunk text, byte[] body, ImageTag tag, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		// A profile that is not hexadecimal is decoded by no reader (exiftool: "Unknown raw profile"): there is no copy
		// in it to update, and it is kept as it is.
		if (!RawProfile.TryDecode (Polyfills.Latin1.GetString (body), out var name, out var profile))
			return true;

		byte[] Encode (byte[] bytes) => text.Build (Polyfills.Latin1.GetBytes (RawProfile.Encode (name, bytes)));
		if (text.Keyword != RawXmpKeyword)
			return new ExifCarrier (text.Keyword, Encode).TryEdit (profile, tag, out edited, out error);
		if (!TryMirrorXmp (Encoding.UTF8.GetString (profile), tag, out var packet, out error))
			return false;
		edited = packet is null ? null : Encode (Encoding.UTF8.GetBytes (packet));
		return true;
	}
}
