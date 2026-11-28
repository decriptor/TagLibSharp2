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
	internal const string SpliceError = "The PNG chunk list does not reach IEND, so the text cannot be spliced in.";

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

	/// <summary>The edited file as new pieces and ranges of <paramref name="data"/>.</summary>
	public static ImagePlan? Plan (ReadOnlySpan<byte> data, ImageTag tag, out string? error)
	{
		error = null;
		var walked = PngChunks.TryWalk (data, out var chunks);
		var ends = walked && chunks.Count > 0 && PngChunks.TypeEquals (data, chunks[^1], "IEND"u8);
		if (!ends) {
			error = SpliceError;
			return null;
		}

		return CopyChunks (data, chunks, tag, Layout.Of (data, chunks), out error);
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

	static ImagePlan? CopyChunks (
		ReadOnlySpan<byte> data,
		List<PngChunk> chunks,
		ImageTag tag,
		Layout layout,
		out string? error)
	{
		error = null;
		var plan = new ImagePlan ();
		var outside = new Outside { FileLength = data.Length };
		var mirrors = !tag.DropUnmapped && MirrorFields.AnyChanged (tag);
		// One inflation budget for every compressed copy in the file, as when it is read.
		var budget = PngText.MaxInflatedText;
		long outIdot = -1;
		long outIdat = -1;
		var copiedIdot = false;
		plan.Copy (0, 8);
		foreach (var chunk in chunks) {
			if (chunk.Start == layout.Insert)
				plan.Add (ChangedFields (tag));
			if (chunk.Start == layout.Idot)
				outIdot = plan.Length;
			if (chunk.Start == layout.Idat)
				outIdat = plan.Length;
			if (IsDropped (data, chunk, tag))
				continue;
			byte[]? edited = null;
			var whole = data.Slice (chunk.Start, chunk.Length);
			if (mirrors && !TryMirrorChunk (whole, tag, ref budget, chunk.Start, outside, out edited, out error))
				return null;
			if (edited is not null) {
				plan.Add (edited);
				continue;
			}

			if (chunk.Start == layout.Idot)
				copiedIdot = true;
			plan.Copy (chunk.Start, chunk.Length);
		}

		// PNG 3rd ed. §5.6: IEND is the last chunk. Whatever follows it (an appended archive, padding) is not
		// the PNG's to change, so it is copied as it is.
		var tail = chunks[^1].Start + chunks[^1].Length;
		plan.Copy (tail, data.Length - tail);
		DropStaleIdot (plan, data, layout, outIdot, outIdat, copiedIdot);
		if (outside.Unlocated && tail < data.Length)
			outside.Anchors.Add (tail);
		if (Moved (plan, outside.Anchors)) {
			var what = MirrorFields.ChangedNames (tag);
			error = what.Length == 0 ? SpliceError : ExifEditor.PastDataReason (outside.Carrier, what);
			return null;
		}

		return plan;
	}

	// A located entry's file position past its chunk, or the byte after a chunk whose note was not located.
	sealed class Outside
	{
		public List<long> Anchors { get; } = [];

		public bool Unlocated { get; set; }

		public string Carrier { get; set; } = "eXIf";

		public int FileLength { get; set; }
	}

	static bool Moved (ImagePlan plan, List<long> anchors)
	{
		if (ExifEditor.SkipCheck || anchors.Count == 0)
			return false;
		var anchor = long.MaxValue;
		foreach (var at in anchors) {
			if (at < anchor)
				anchor = at;
		}

		return plan.OutputAt (anchor) != anchor;
	}

	static byte[] ChangedFields (ImageTag tag)
	{
		using var builder = new BinaryDataBuilder ();
		AddFields (builder, tag, onlyChanged: true);
		return builder.ToArray ();
	}

	// iDOT holds the offset of the IDAT data from itself. When a chunk between them changed, the offset is wrong,
	// so iDOT is dropped (a decoder then reads the IDAT chunks in order, as it does for any PNG).
	static void DropStaleIdot (
		ImagePlan plan,
		ReadOnlySpan<byte> data,
		Layout layout,
		long outIdot,
		long outIdat,
		bool copiedIdot)
	{
		var kept = copiedIdot && outIdot >= 0 && outIdot + 8 <= plan.Length && IsIdot (data, layout.Idot);
		if (!kept || layout.Idat < 0 || outIdat - outIdot == layout.Idat - layout.Idot)
			return;
		var length = PngChunks.ReadLength (data, layout.Idot);
		if (length < 0)
			return;
		plan.Cut (outIdot, 12L + length);
	}

	static bool IsIdot (ReadOnlySpan<byte> data, int start) =>
		data.Slice (start + 4, 4).SequenceEqual ("iDOT"u8);

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
	static bool TryMirrorChunk (ReadOnlySpan<byte> whole, ImageTag tag, ref int budget, int chunkStart, Outside outside, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		var type = whole.Slice (4, 4);
		if (!PngChunks.CrcMatches (whole))
			return true;
		var payload = whole.Slice (8, whole.Length - 12);
		if (type.SequenceEqual ("eXIf"u8)) {
			var carrier = new ExifCarrier ("eXIf", exif => PngChunks.Chunk ("eXIf"u8, exif));
			var ok = carrier.TryEdit (payload, tag, out edited, out error);
			Note (chunkStart, whole.Length, payload, carrier.Anchors, outside, contiguous: true, "eXIf");
			return ok;
		}
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
			return TryMirrorRawProfile (text, body, tag, chunkStart, whole.Length, outside, out edited, out error);
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

	static bool TryMirrorRawProfile (TextChunk text, byte[] body, ImageTag tag, int chunkStart, int chunkLength, Outside outside, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		// A profile that is not hexadecimal is decoded by no reader (exiftool: "Unknown raw profile"): there is no copy
		// in it to update, and it is kept as it is.
		if (!RawProfile.TryDecode (Polyfills.Latin1.GetString (body), out var name, out var profile))
			return true;

		byte[] Encode (byte[] bytes) => text.Build (Polyfills.Latin1.GetBytes (RawProfile.Encode (name, bytes)));
		if (text.Keyword != RawXmpKeyword) {
			var carrier = new ExifCarrier (text.Keyword, Encode);
			var ok = carrier.TryEdit (profile, tag, out edited, out error);
			Note (chunkStart, chunkLength, profile, carrier.Anchors, outside, contiguous: false, text.Keyword);
			return ok;
		}
		if (!TryMirrorXmp (Encoding.UTF8.GetString (profile), tag, out var packet, out error))
			return false;
		edited = packet is null ? null : Encode (Encoding.UTF8.GetBytes (packet));
		return true;
	}

	// Contiguous Exif (an eXIf chunk) names a file byte. A hex profile does not, so the byte after its chunk stands
	// for every offset past the decoded TIFF.
	static void Note (int chunkStart, int chunkLength, ReadOnlySpan<byte> payload, ExifEditor.ExifAnchors anchors, Outside outside, bool contiguous, string carrier)
	{
		if (!anchors.NotesLocated)
			outside.Unlocated = true;
		if (anchors.PastBlock.Length == 0)
			return;
		// A hex-encoded profile's offsets address the decoded TIFF, not bytes in the file. Only a contiguous
		// eXIf chunk names a file position past its own chunk.
		if (!contiguous)
			return;
		outside.Carrier = carrier;
		var chunkEnd = chunkStart + chunkLength;
		var fileLength = outside.FileLength;
		var payloadAt = chunkStart + 8;
		var tiffAt = payload.StartsWith ("Exif\0\0"u8) ? payloadAt + 6 : payloadAt;
		foreach (var range in anchors.PastBlock) {
			var start = tiffAt + range.Start;
			var end = tiffAt + range.End;
			if (end <= chunkEnd || start >= fileLength)
				continue;
			var at = start < chunkEnd ? chunkEnd : start;
			if (at < fileLength)
				outside.Anchors.Add (at);
		}
	}
}
