// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image;
using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Jpeg;

/// <summary>One later image of a multi-picture file: its MP entry number, where it starts and its size.</summary>
readonly record struct MpImage (int Entry, int Start, int Size);

/// <summary>
/// The first image's MP index (CIPA DC-007 §5.2.3.3): an MP Entry per image, each 16 bytes of attribute, size,
/// and offset from the MP header (0 for the first image). The MP index IFD and its entry list are a TIFF structure
/// inside the APP2 "MPF" segment, read with <see cref="TiffReader"/> over that segment's bytes.
/// </summary>
sealed class MpfIndex
{
	const int MpEntryTag = 0xB002;

	public bool Unreadable { get; init; }
	public List<MpImage> Images { get; init; } = [];

	/// <summary>
	/// The later images, sorted. For Clear, every later image must be a whole JPEG inside the file, after the first
	/// image's end (its EOI), without overlapping another; otherwise the index is unreadable.
	/// </summary>
	public static MpfIndex Read (ReadOnlySpan<byte> data)
	{
		if (FindHeader (data) is not var (header, limit, firstEnd))
			return new MpfIndex ();
		var entries = new List<(long Offset, long Size)> ();
		if (!TryReadEntries (data, header, limit, entries))
			return new MpfIndex { Unreadable = true };
		var images = new List<MpImage> ();
		for (var n = 0; n < entries.Count; n++) {
			var (offset, size) = entries[n];
			if (offset == 0)
				continue;
			var start = header + offset;
			if (start < firstEnd || size < 4 || start + size > data.Length || data[(int)start] != 0xFF || data[(int)start + 1] != 0xD8)
				return new MpfIndex { Unreadable = true };
			images.Add (new MpImage (n, (int)start, (int)size));
		}

		images.Sort ((a, b) => a.Start.CompareTo (b.Start));
		for (var n = 1; n < images.Count; n++) {
			if (images[n].Start < images[n - 1].Start + images[n - 1].Size)
				return new MpfIndex { Unreadable = true };
		}

		return new MpfIndex { Images = images };
	}

	// The MP header of the first APP2 "MPF" segment, the end of that segment's payload, and where the first image
	// ends (a later image cannot start before it); null without an MPF segment before the scan.
	static (int Header, int Limit, int Scan)? FindHeader (ReadOnlySpan<byte> data)
	{
		(int Header, int Limit)? found = null;
		var i = 2;
		while (i + 4 <= data.Length && data[i] == 0xFF) {
			var marker = data[i + 1];
			if (marker is 0xDA or 0xD9)
				return found is var (header, limit) ? (header, limit, FirstImageEnd (data, i)) : null;
			if (marker is 0x01 or (>= 0xD0 and <= 0xD8) or 0xFF) {
				i += marker == 0xFF ? 1 : 2;
				continue;
			}

			var length = Endian.U16 (data, i + 2, le: false);
			if (length < 2 || i + 2 + length > data.Length)
				break;
			if (marker == 0xE2 && data.Slice (i + 4, length - 2).StartsWith ("MPF\0"u8))
				found ??= (i + 8, i + 2 + length);
			i += 2 + length;
		}

		return found is var (h, l) ? (h, l, data.Length) : null;
	}

	// The end of the first image: after its EOI, the first FF D9 from its first scan on (entropy-coded data holds no
	// FF D9: an FF there is followed by 00 or a restart marker); the data's end without one.
	static int FirstImageEnd (ReadOnlySpan<byte> data, int scan) =>
		data.Slice (scan).IndexOf (Eoi) is var eoi and >= 0 ? scan + eoi + 2 : data.Length;

	static ReadOnlySpan<byte> Eoi => [0xFF, 0xD9];

	static bool TryReadEntries (ReadOnlySpan<byte> data, int header, int limit, List<(long Offset, long Size)> entries)
	{
		if (!TryEntryList (data, header, limit, out var le, out var list, out var count))
			return false;
		for (var n = 0; n < count; n++) {
			var at = (int)list + (16 * n);
			entries.Add ((Endian.U32 (data, at + 8, le), Endian.U32 (data, at + 4, le)));
		}

		return true;
	}

	/// <summary>Rewrites every MP entry's offset and size with update (entry number, offset, size).</summary>
	public static bool TryRewriteEntries (byte[] output, int header, int limit, Func<int, long, long, (long Offset, long Size)> update)
	{
		if (!TryEntryList (output, header, limit, out var le, out var list, out var count))
			return false;
		var span = output.AsSpan ();
		for (var n = 0; n < count; n++) {
			var at = (int)list + (16 * n);
			var (offset, size) = update (n, Endian.U32 (span, at + 8, le), Endian.U32 (span, at + 4, le));
			if (offset < 0 || offset > uint.MaxValue || size < 0 || size > uint.MaxValue)
				return false;
			Endian.WriteU32 (span, at + 8, offset, le);
			Endian.WriteU32 (span, at + 4, size, le);
		}

		return true;
	}

	// CIPA DC-007 §5.2: the MP index IFD and the MP entry list it points to are inside the APP2 segment. An index
	// that points outside it is unreadable: following it would read or overwrite other bytes. The list is where the
	// entries start in the file; count 0 when the index has no MP Entry tag. The header and the segment's end are
	// where the segment was found in these bytes.
	static bool TryEntryList (ReadOnlySpan<byte> data, int header, int limit, out bool le, out long list, out int count)
	{
		(le, list, count) = (default, 0, 0);
		if (header + 8 > limit)
			return false;
		var segment = data.Slice (header, limit - header);
		if (!TiffReader.TryByteOrder (segment, out le))
			return false;
		var tiff = new TiffReader (segment, le);
		var ifd = tiff.U32 (4);
		if (!tiff.TryTable (ifd, out var tags))
			return false;
		for (var n = 0; n < tags; n++) {
			var entry = tiff.Entry (ifd, n);
			if (tiff.Tag (entry) != MpEntryTag)
				continue;
			var bytes = tiff.Count (entry);
			var at = tiff.Value (entry);
			list = header + at;
			if (bytes % 16 != 0 || at < TiffReader.HeaderSize || at + bytes > segment.Length)
				return false;
			count = (int)(bytes / 16);
			return true;
		}

		return true;
	}
}
