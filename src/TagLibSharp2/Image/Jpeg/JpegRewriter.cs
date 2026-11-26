// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Xmp;

namespace TagLibSharp2.Image.Jpeg;

/// <summary>
/// Reads a JPEG's COM segments and splices a new comment before the scan (ITU-T T.81 §B.2.4.5). Clear also drops
/// the metadata APPn segments of every image a multi-picture index lists, and rewrites the index. Every byte from
/// the scan on is copied as it is.
/// </summary>
static class JpegRewriter
{
	const int MaxSegment = 65535;
	const string SpliceError = "The JPEG marker list before the scan is truncated or malformed, so the comment cannot be spliced in.";

	public static void Read (ReadOnlySpan<byte> data, ImageTag tag)
	{
		if (TryComment (data, out var text))
			tag.ReadField (ImageStores.Comment, text);
	}

	public static byte[]? Rewrite (ReadOnlySpan<byte> data, ImageTag tag, out string? error)
	{
		error = null;
		if (!tag.Changed (ImageStores.Comment) && !tag.DropUnmapped)
			return null;
		byte[]? replacement = tag.Changed (ImageStores.Comment)
			? tag.Field (ImageStores.Comment) is string comment ? EncodeComment (comment) : []
			: null;
		return TryRewrite (data, tag, replacement, out error);
	}

	static bool IsSoi (ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

	static bool TryComment (ReadOnlySpan<byte> data, out string? text)
	{
		text = null;
		if (!IsSoi (data))
			return false;
		var collected = new List<byte> ();
		var i = 2;
		while (i < data.Length) {
			if (data[i] != 0xFF)
				return collected.Count > 0;
			while (i < data.Length && data[i] == 0xFF)
				i++;
			if (i >= data.Length)
				return false;
			var marker = data[i++];
			if (marker is 0x01 or (>= 0xD0 and <= 0xD9)) {
				if (marker == 0xD9)
					break;
				continue;
			}

			if (i + 1 >= data.Length)
				return false;
			var length = (data[i] << 8) | data[i + 1];
			if (length < 2 || i + length > data.Length)
				return false;
			if (marker == 0xDA)
				break;
			if (marker == 0xFE)
				collected.AddRange (data.Slice (i + 2, length - 2).ToArray ());
			i += length;
		}

		text = collected.Count == 0 ? null : Encoding.UTF8.GetString (collected.ToArray ());
		return true;
	}

	static byte[]? TryRewrite (ReadOnlySpan<byte> data, ImageTag tag, byte[]? comment, out string? error)
	{
		error = null;
		var dropMetadata = tag.DropUnmapped;
		if (!IsSoi (data)) {
			error = SpliceError;
			return null;
		}

		// CIPA DC-007 §5.2: the first image's APP2 "MPF" index lists every image of a multi-picture file. Each
		// later image is a whole JPEG with its own APPn after the first image's EOI; Clear cleans those too.
		var index = MpfIndex.Read (data);
		if (dropMetadata && index.Unreadable) {
			error = "The JPEG MPF index cannot be read, so the metadata of the file's other images cannot be cleared.";
			return null;
		}

		var images = dropMetadata ? index.Images : [];
		if (!TryCleanLaterImages (data, images, tag, out var cleaned, out error))
			return null;

		// A version-65536 Apple gain map takes its headroom from the primary's maker note, so the later images are
		// read first to know whether the primary keeps those two values (AppleHeadroom).
		var options = new ImageOptions (comment, dropMetadata, Mirrors: comment is not null && !dropMetadata,
			ItemLengths: dropMetadata ? cleaned.ConvertAll (c => (long)c.Bytes.Length) : null, AppleHeadroom: cleaned.Exists (c => c.HeadroomInMakerNote));
		var primaryEnd = images.Count > 0 ? images[0].Start : data.Length;
		var primary = RewriteImage (data.Slice (0, primaryEnd), tag, options, out error);
		if (primary is null)
			return null;
		if (primary.InMpf < 0)
			return primary.Bytes;
		return images.Count == 0 ? ShiftMpEntries (primary, out error) : PlaceImages (data, primary, primaryEnd, images, cleaned, out error);
	}

	// The later images are cleaned first: an Ultra HDR primary lists their lengths (Item:Length).
	static bool TryCleanLaterImages (ReadOnlySpan<byte> data, List<MpImage> images, ImageTag tag, out List<JpegImage> cleaned, out string? error)
	{
		error = null;
		cleaned = [];
		var options = new ImageOptions ([], DropMetadata: true, Mirrors: false, ItemLengths: null, AppleHeadroom: false);
		foreach (var image in images) {
			if (RewriteImage (data.Slice (image.Start, image.Size), tag, options, out _) is not JpegImage clean) {
				error = $"Image {image.Entry + 1} of the JPEG MPF index cannot be parsed, so its metadata cannot be cleared.";
				return false;
			}

			cleaned.Add (clean);
		}

		return true;
	}

	// Everything after the scan is copied, so the later images move with the bytes between the MP header and the
	// scan, and image 1's size changes with everything before the scan.
	static byte[]? ShiftMpEntries (JpegImage primary, out string? error)
	{
		error = null;
		var offsetDelta = (primary.OutRest - primary.OutMpf) - (long)(primary.InRest - primary.InMpf);
		var sizeDelta = primary.OutRest - (long)primary.InRest;
		if (offsetDelta == 0 && sizeDelta == 0)
			return primary.Bytes;
		if (MpfIndex.TryRewriteEntries (primary.Bytes, primary.OutMpf, primary.OutMpfEnd, (n, offset, size) => offset == 0 ? (0, size + sizeDelta) : (offset + offsetDelta, size)))
			return primary.Bytes;
		error = "The JPEG MPF index cannot be read, so the offsets of its other images cannot be kept right after the edit.";
		return null;
	}

	// The cleaned primary, then each cleaned later image in place of the old one with the bytes between copied, and
	// every MP entry pointed at its image's new place.
	static byte[]? PlaceImages (ReadOnlySpan<byte> data, JpegImage primary, int primaryEnd, List<MpImage> images, List<JpegImage> cleaned, out string? error)
	{
		error = null;
		using var builder = new BinaryDataBuilder (data.Length);
		builder.Add (primary.Bytes);
		var placed = new Dictionary<int, (long Start, long Size)> ();
		var at = primaryEnd;
		for (var n = 0; n < images.Count; n++) {
			builder.Add (data.Slice (at, images[n].Start - at));
			placed[images[n].Entry] = (builder.Length, cleaned[n].Bytes.Length);
			builder.Add (cleaned[n].Bytes);
			at = images[n].Start + images[n].Size;
		}

		builder.Add (data.Slice (at));
		var output = builder.ToArray ();
		var primaryDelta = primary.Bytes.Length - (long)primaryEnd;
		var header = primary.OutMpf;
		if (MpfIndex.TryRewriteEntries (output, header, primary.OutMpfEnd, (n, offset, size) =>
			placed.TryGetValue (n, out var image) ? (image.Start - header, image.Size) : offset == 0 ? (0, size + primaryDelta) : (offset, size)))
			return output;
		error = "The JPEG MPF index cannot be read, so the offsets of its other images cannot be kept right after Clear.";
		return null;
	}

	// How one image is rewritten: the comment to add (null keeps the COM segments), whether its metadata is
	// dropped (Clear), whether the APP1 copies of a changed comment are updated, the gain-map item lengths to
	// write, and whether its first Exif keeps the Apple maker-note headroom.
	sealed record ImageOptions (byte[]? Comment, bool DropMetadata, bool Mirrors, IReadOnlyList<long>? ItemLengths, bool AppleHeadroom);

	sealed class JpegImage
	{
		public byte[] Bytes { get; init; } = [];
		public int InMpf { get; init; } = -1;
		public int OutMpf { get; init; } = -1;
		public int OutMpfEnd { get; init; } = -1;
		public int InRest { get; init; }
		public int OutRest { get; init; }
		public bool HeadroomInMakerNote { get; init; }
	}

	// Rewrites the marker segments of one image up to its scan (or EOI), adds the new comment there, and copies
	// the rest of the span as it is.
	static JpegImage? RewriteImage (ReadOnlySpan<byte> data, ImageTag tag, ImageOptions options, out string? error)
	{
		error = null;
		if (!IsSoi (data))
			return Fail (out error);
		using var image = new ImageBuilder (data.Length + (options.Comment?.Length ?? 0), tag, options);
		image.Builder.Add (data.Slice (0, 2));
		var i = 2;
		while (i < data.Length) {
			var start = i;
			if (!TryMarker (data, ref i, out var marker))
				return Fail (out error);

			if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) {
				image.Builder.Add (data.Slice (start, i - start));
				continue;
			}

			if (marker == 0xD9)
				return image.Finish (data, start);
			if (!TryLength (data, i, out var length))
				return Fail (out error);

			if (marker == 0xDA)
				return image.Finish (data, start);

			// Fill bytes (0xFF) before the marker (T.81 §B.1.1.2) are part of what is kept or rewritten.
			var segment = new Segment (marker, start, data.Slice (start, i - 2 - start), data.Slice (start, i + length - start), data.Slice (i + 2, length - 2));
			i += length;
			if (!image.TryAdd (segment, out error))
				return null;
		}

		return image.Finish (data, data.Length);
	}

	static JpegImage? Fail (out string? error)
	{
		error = SpliceError;
		return null;
	}

	// At a 0xFF, skips the fill bytes and reads the marker; false when the byte is not 0xFF or the data ends.
	static bool TryMarker (ReadOnlySpan<byte> data, ref int i, out byte marker)
	{
		marker = 0;
		if (data[i] != 0xFF)
			return false;
		while (i < data.Length && data[i] == 0xFF)
			i++;
		if (i >= data.Length)
			return false;
		marker = data[i++];
		return true;
	}

	// The segment length at i (it counts itself), when the segment lies inside the data.
	static bool TryLength (ReadOnlySpan<byte> data, int i, out int length)
	{
		length = 0;
		if (i + 1 >= data.Length)
			return false;
		length = (data[i] << 8) | data[i + 1];
		return length >= 2 && i + length <= data.Length;
	}

	// A marker segment: where it starts, the fill bytes before its marker, the whole segment and its payload.
	readonly ref struct Segment (byte marker, int start, ReadOnlySpan<byte> fill, ReadOnlySpan<byte> whole, ReadOnlySpan<byte> payload)
	{
		public byte Marker { get; } = marker;
		public int Start { get; } = start;
		public ReadOnlySpan<byte> Fill { get; } = fill;
		public ReadOnlySpan<byte> Whole { get; } = whole;
		public ReadOnlySpan<byte> Payload { get; } = payload;
	}

	// The output of one image as its segments are kept, dropped or rewritten.
	sealed class ImageBuilder (int capacity, ImageTag tag, ImageOptions options) : IDisposable
	{
		int _inMpf = -1;
		int _outMpf = -1;
		int _mpfLength;
		bool _headroomInMakerNote;
		bool _firstExif = true;

		public BinaryDataBuilder Builder { get; } = new (capacity);

		public void Dispose () => Builder.Dispose ();

		public bool TryAdd (Segment segment, out string? error)
		{
			error = null;
			var marker = segment.Marker;
			var payload = segment.Payload;
			if (options.DropMetadata && marker == 0xE1 && TryClearApp1 (segment))
				return true;
			if ((options.Comment is not null && marker == 0xFE) || (options.DropMetadata && marker is >= 0xE0 and <= 0xEF && !IsKeptOnClear (marker, payload)))
				return true;
			if (marker == 0xE2 && _inMpf < 0 && payload.StartsWith ("MPF\0"u8)) {
				_inMpf = segment.Start + (segment.Whole.Length - payload.Length) + 4;
				_outMpf = Builder.Length + (segment.Whole.Length - payload.Length) + 4;
				_mpfLength = payload.Length - 4;
			}

			if (options.Mirrors && marker == 0xE1) {
				if (!TryMirrorApp1 (payload, tag, out var edited, out error))
					return false;
				if (edited is not null) {
					Add (segment.Fill, edited);
					return true;
				}
			}

			Builder.Add (segment.Whole);
			return true;
		}

		// Clear keeps only the gain-map XMP an HDR decoder needs (ImageGainMap), and of the first Exif (the one
		// readers use) only the Apple maker-note headroom. True when the segment was handled (kept or dropped).
		bool TryClearApp1 (Segment segment)
		{
			var payload = segment.Payload;
			if (TryXmpSignature (payload, out var signature)) {
				var kept = ImageGainMap.KeepForClear (payload.Slice (signature), options.ItemLengths, out var makerNote);
				if (kept is null || 2 + signature + kept.Length > MaxSegment)
					return true;
				_headroomInMakerNote |= makerNote;
				Add (segment.Fill, SegmentBytes (0xE1, [.. payload.Slice (0, signature), .. kept]));
				return true;
			}

			if (!payload.StartsWith ("Exif\0\0"u8) || !_firstExif)
				return false;
			_firstExif = false;
			if (options.AppleHeadroom && AppleHeadroom.Keep (payload.Slice (6)) is byte[] headroom)
				Add (segment.Fill, SegmentBytes (0xE1, [.. "Exif\0\0"u8, .. headroom]));
			return true;
		}

		void Add (ReadOnlySpan<byte> fill, byte[] segment)
		{
			Builder.Add (fill);
			Builder.Add (segment);
		}

		public JpegImage Finish (ReadOnlySpan<byte> data, int rest)
		{
			if (options.Comment is { Length: > 0 })
				Builder.Add (options.Comment);
			var outRest = Builder.Length;
			Builder.Add (data.Slice (rest));
			return new JpegImage {
				Bytes = Builder.ToArray (),
				InMpf = _inMpf,
				OutMpf = _outMpf,
				OutMpfEnd = _outMpf < 0 ? -1 : _outMpf + _mpfLength,
				InRest = rest,
				OutRest = outRest,
				HeadroomInMakerNote = _headroomInMakerNote
			};
		}
	}

	// "http://ns.adobe.com/xap/1.0/" then NUL (XMP Part 3 §1.1.3), or with a space before the NUL as some writers
	// do; exiftool and ImageIO read that packet too.
	static bool TryXmpSignature (ReadOnlySpan<byte> payload, out int length)
	{
		length = payload.StartsWith ("http://ns.adobe.com/xap/1.0/\0"u8) ? 29 : payload.StartsWith ("http://ns.adobe.com/xap/1.0/ \0"u8) ? 30 : 0;
		return length > 0;
	}

	// A Comment edit also changes the copies of the comment in APP1: Exif UserComment and XPComment, and XMP
	// exif:UserComment. Extended XMP is spread over several segments and keyed by a digest of its content; when
	// it may hold the comment, the save is refused rather than leave a stale copy.
	static bool TryMirrorApp1 (ReadOnlySpan<byte> payload, ImageTag tag, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		if (payload.StartsWith ("Exif\0\0"u8))
			return new ExifCarrier ("JPEG Exif", exif => SegmentBytes (0xE1, exif)).TryEdit (payload, tag, out edited, out error);
		if (TryXmpSignature (payload, out var signature))
			return TryMirrorXmp (payload, signature, tag, out edited, out error);
		if (payload.StartsWith ("http://ns.adobe.com/xmp/extension/\0"u8) && payload.IndexOf ("UserComment"u8) >= 0) {
			error = "The JPEG extended XMP holds a UserComment, a copy of Comment that cannot be updated here.";
			return false;
		}

		return true;
	}

	static bool TryMirrorXmp (ReadOnlySpan<byte> payload, int signature, ImageTag tag, out byte[]? edited, out string? error)
	{
		edited = null;
		var packet = Encoding.UTF8.GetString (payload.Slice (signature));
		if (!XmpMirror.TryUpdate (packet, tag, "JPEG XMP packet", out var updated, out error))
			return false;
		if (ReferenceEquals (updated, packet))
			return true;
		var bytes = Encoding.UTF8.GetBytes (updated);
		if (2 + signature + bytes.Length > MaxSegment) {
			error = "The JPEG XMP packet would no longer fit in one APP1 segment after the comment edit.";
			return false;
		}

		var body = new byte[signature + bytes.Length];
		payload.Slice (0, signature).CopyTo (body);
		bytes.CopyTo (body, signature);
		edited = SegmentBytes (0xE1, body);
		return true;
	}

	static byte[] SegmentBytes (byte marker, byte[] payload)
	{
		var segment = new byte[4 + payload.Length];
		segment[0] = 0xFF;
		segment[1] = marker;
		segment[2] = (byte)((payload.Length + 2) >> 8);
		segment[3] = (byte)(payload.Length + 2);
		payload.CopyTo (segment, 4);
		return segment;
	}

	// Clear drops every APPn segment except the ones a decoder or colour management needs, or that carry the
	// file's structure:
	// - APP0 "JFIF" and "JFXX" (JFIF 1.02: pixel density and the format of the file, and its thumbnail);
	// - APP2 "ICC_PROFILE" (ICC.1 Annex B.4: the colour profile);
	// - APP2 "MPF" (CIPA DC-007 §5.2: the index of the images of a multi-picture file, and their attributes);
	// - APP2 "urn:iso:std:iso:ts:21496:-1" (ISO 21496-1: the gain map version and parameters an HDR decoder
	//   applies to the gain-map image);
	// - APP14 "Adobe" (the colour transform flag that says whether the components are YCbCr or YCCK);
	// - APP11 JPEG XT boxes a JPEG XT decoder uses to rebuild the image. Each APP11 segment holds "JP", the box
	//   instance En, the sequence number Z, then the box's LBox and TBox, repeated in every segment of a box
	//   (ISO/IEC 18477-3 Annex A). The decoding boxes are those named in A. Artusi et al., "JPEG XT: A Compression
	//   Standard for HDR and WCG Images", IEEE Signal Processing Magazine 33(2), 2016, §III and Fig. 3: ftyp (file
	//   type), TONE (inverse tone mapping), RTRF and LTRF (residual and base colour transforms), LCHK (legacy data
	//   checksum), SPEC (the superbox that says how to reassemble the image) and RESI (the residual codestream).
	//   A legacy decoder skips every APP11 box (same paper, §III), so the base image never needs them. Every other
	//   box goes: JUMBF superboxes "jumb" (ISO/IEC 19566-5: C2PA, JSON, XML), "xml ", "uuid", "json", "Exif", and
	//   unknown types. (Annex B of 18477-3, the full box list, is not public; refinement and alpha boxes are not
	//   named there and are dropped.)
	// Exif (but for the Apple maker-note headroom a version-65536 gain map needs, AppleHeadroom), XMP and extended
	// XMP, Photoshop/IPTC, Ducky, FlashPix, maker segments such as SCALADO, and unknown APPn are dropped. Segments
	// other than APPn (tables, frame, restart interval) are kept.
	static bool IsKeptOnClear (byte marker, ReadOnlySpan<byte> payload) => marker switch {
		0xE0 => payload.StartsWith ("JFIF\0"u8) || payload.StartsWith ("JFXX\0"u8),
		0xE2 => payload.StartsWith ("ICC_PROFILE\0"u8) || payload.StartsWith ("MPF\0"u8) || payload.StartsWith ("urn:iso:std:iso:ts:21496:-1\0"u8),
		0xEB => payload.Length >= 16 && payload[0] == (byte)'J' && payload[1] == (byte)'P' && IsXtDecodingBox (payload.Slice (12, 4)),
		0xEE => payload.StartsWith ("Adobe"u8),
		_ => false
	};

	static readonly string[] XtDecodingBoxes = ["ftyp", "TONE", "RTRF", "LTRF", "LCHK", "SPEC", "RESI"];

	static bool IsXtDecodingBox (ReadOnlySpan<byte> type)
	{
		foreach (var box in XtDecodingBoxes) {
			if (type[0] == box[0] && type[1] == box[1] && type[2] == box[2] && type[3] == box[3])
				return true;
		}

		return false;
	}

	/// <summary>COM segments of at most 65,533 bytes each, split between UTF-8 sequences.</summary>
	public static byte[] EncodeComment (string comment)
	{
		var bytes = Encoding.UTF8.GetBytes (comment);
		using var builder = new BinaryDataBuilder (bytes.Length + 8);
		var offset = 0;
		while (offset < bytes.Length) {
			var take = Math.Min (65533, bytes.Length - offset);
			while (take > 0 && offset + take < bytes.Length && (bytes[offset + take] & 0xC0) == 0x80)
				take--;
			if (take == 0)
				take = Math.Min (65533, bytes.Length - offset);
			builder.Add (0xFF);
			builder.Add (0xFE);
			builder.AddUInt16BE ((ushort)(take + 2));
			builder.Add (bytes.AsSpan (offset, take));
			offset += take;
		}

		return builder.ToArray ();
	}
}
