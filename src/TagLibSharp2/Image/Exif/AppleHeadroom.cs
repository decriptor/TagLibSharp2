// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// The HDR headroom a version-65536 Apple gain map takes from the primary image's Apple maker note, and the minimal
/// Exif that keeps it after Clear.
/// </summary>
static class AppleHeadroom
{
	const int MakerNoteTag = 0x927C;
	const int HeadroomTag = 0x0021;
	const int GainTag = 0x0030;
	const int SRationalType = 10;

	static ReadOnlySpan<byte> MakerNoteHeader => "Apple iOS\0\0\u0001MM"u8;

	/// <summary>
	/// The Exif a version-65536 Apple gain map needs after Clear: a big-endian TIFF with IFD0 -> Exif IFD ->
	/// MakerNote, and in the Apple maker note only tags 0x0021 and 0x0030, copied as stored. Null when the
	/// values ImageIO would apply are not both there as one SRATIONAL each.
	/// </summary>
	/// <remarks>
	/// <para>Apple, "Applying Apple HDR effect to your photos"
	/// (https://developer.apple.com/documentation/appkit/applying-apple-hdr-effect-to-your-photos): a gain map
	/// whose metadata has <c>HDRGainMapVersion</c> takes its headroom from the <c>MakerNote</c> keys 33 and 48.
	/// They are decode parameters, not personal data. The maker note (Exif 2.32 §4.6.5, tag 0x927C, UNDEFINED)
	/// starts "Apple iOS", NUL, version 1, "MM", then a big-endian IFD whose value offsets count from the start of
	/// the maker note.</para>
	/// <para>TIFF 6.0 does not say which of two entries with one tag wins, or what a reader does with one it cannot
	/// use, so the values kept are the ones ImageIO applies, measured on ImageIO-written files with every level
	/// duplicated and broken. Where libultrahdr differs, this follows ImageIO, Apple's decoder for Apple's format.
	/// At every level an occurrence that cannot be used is skipped and a later usable one overrides an earlier one:
	/// <list type="bullet">
	/// <item>IFD0 0x8769 (Exif IFD pointer): usable when LONG or IFD, count 1, and its table lies inside the data.
	/// Two Exif IFDs merge tag by tag: a later one's maker-note value replaces an earlier one's, and a later Exif
	/// IFD without one leaves the earlier one's (libultrahdr reads only the first pointer and refuses the file
	/// when that one is out of range);</item>
	/// <item>Exif IFD 0x927C (MakerNote): usable when UNDEFINED, more than 4 bytes, inside the data, and either not
	/// an Apple maker note (it then wins with no values, so nothing is kept) or one whose header is exactly as above
	/// and whose entry table fits; an Apple maker note with another header or a table that does not fit is skipped.
	/// The last usable one of an Exif IFD is that IFD's maker note, whole (libultrahdr reads the first);</item>
	/// <item>maker-note 0x0021 and 0x0030: usable when SRATIONAL, count at least 1 and the 8 bytes of each value
	/// inside the maker note, at any offset (its header and entry table included). The last usable one of each tag
	/// wins. When a winner's count is not 1 ImageIO renders no HDR image at all, so nothing is kept (libultrahdr
	/// reads the first value regardless of the count, and refuses a value outside the maker note).</item>
	/// </list>
	/// The Exif segment is the first one (both decoders use the first), and its TIFF header must carry the magic 42
	/// (ImageIO ignores an Exif without it).</para>
	/// <para>The result is a minimal carrier for the two decode parameters, not a conformant Exif: it deliberately
	/// omits the tags Exif marks mandatory (ExifVersion, ComponentsConfiguration, ColorSpace, the pixel dimensions,
	/// YCbCrPositioning and the resolution tags, CIPA DC-008 §4.6.8), consistent with Clear removing all other
	/// Exif, so <c>exiftool -validate</c> warns about them. ImageIO, libultrahdr, ffmpeg, exiftool and TagLib# read
	/// it.</para>
	/// <para>Each distinct Exif IFD and maker note is walked once, from the last, and every entry visit is counted
	/// in <see cref="IfdSteps"/>; the entries walked are bounded by the data (tables that overlap past it keep
	/// nothing); nothing recurses and nothing throws.</para>
	/// </remarks>
	internal static byte[]? Keep (ReadOnlySpan<byte> data)
	{
		// A header that is not classic TIFF (byte order and magic 42) keeps nothing: ImageIO ignores that Exif.
		if (!TiffReader.TryClassic (data, out var tiff, out var ifd0) || !tiff.TryTable (ifd0, out var ifd0Count))
			return null;
		var pointers = ExifIfdPointers (tiff, ifd0, ifd0Count);
		var (headroom, gain) = FindWinners (tiff, pointers);
		if (headroom.Count != 1 || gain.Count != 1)
			return null;
		return HeadroomTiff (data.Slice ((int)headroom.At, 8), data.Slice ((int)gain.At, 8));
	}

	// The last usable entry of a tag: its value's offset in the TIFF and its count (0 when there is none).
	readonly record struct Winner (long At, long Count)
	{
		public bool Found => Count > 0;
	}

	// Later Exif IFDs override earlier ones tag by tag: walking from the last, the first value found for a tag is
	// the one that wins, and an Exif IFD seen again adds nothing. The walk stops when both are found, or when the
	// entries walked pass the bound of one per 12 bytes (tables that overlap past it keep nothing: every value needs a
	// maker note entry, so none can be found past the bound).
	static (Winner Headroom, Winner Gain) FindWinners (TiffReader tiff, CountedList<long> pointers)
	{
		var (headroom, gain) = (default (Winner), default (Winner));
		var budget = tiff.EntryBound;
		var seen = new HashSet<long> ();
		for (var p = pointers.Count - 1; p >= 0 && (!headroom.Found || !gain.Found); p--) {
			var ifd = pointers[p];
			if (!seen.Add (ifd))
				continue;
			_ = tiff.TryTable (ifd, out var count);
			budget -= count;
			var found = ReadMakerNote (tiff, ifd, count, ref budget);
			if (budget < 0)
				return (default (Winner), default (Winner));
			if (!headroom.Found)
				headroom = found.Headroom;
			if (!gain.Found)
				gain = found.Gain;
		}

		return (headroom, gain);
	}

	// The headroom entries of an Exif IFD's Apple maker note, with offsets in the TIFF (none when its maker note is
	// not Apple's); its entries are taken from the budget.
	static (Winner Headroom, Winner Gain) ReadMakerNote (TiffReader tiff, long ifd, int count, ref long budget)
	{
		// A usable maker note that starts with Apple's header has an entry table that fits (IsUsable).
		if (LastMakerNote (tiff, ifd, count) is not var (noteAt, noteSize))
			return default;
		var note = tiff.Data.Slice ((int)noteAt, (int)noteSize);
		if (!note.StartsWith (MakerNoteHeader))
			return default;
		// The entry count IsUsable already required to fit. It is the same count the maker-note walk reads.
		var noteReader = new TiffReader (note, littleEndian: false);
		if (!noteReader.TryTable (MakerNoteHeader.Length, out var entries))
			return default;
		budget -= entries;
		var (headroom, gain) = HeadroomEntries (noteReader);
		return (headroom with { At = noteAt + headroom.At }, gain with { At = noteAt + gain.At });
	}

	// The Exif IFD pointers ImageIO follows: LONG or IFD, count 1, a table inside the data; in IFD0 order.
	static CountedList<long> ExifIfdPointers (TiffReader tiff, long ifd0, int ifd0Count)
	{
		var pointers = new CountedList<long> ();
		for (var n = 0; n < ifd0Count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (ifd0, n);
			if (tiff.Tag (entry) != TiffReader.ExifIfdPointer || tiff.Type (entry) is not (4 or 13) || tiff.Count (entry) != 1)
				continue;
			var at = tiff.Value (entry);
			if (tiff.TryTable (at, out _))
				pointers.Add (at);
		}

		return pointers;
	}

	// The maker note of an Exif IFD as ImageIO takes it: the last usable 0x927C entry (UNDEFINED, more than 4
	// bytes, inside the data; an Apple maker note only with its exact header and a table that fits), or null.
	static (long At, long Size)? LastMakerNote (TiffReader tiff, long ifd, int count)
	{
		(long At, long Size)? note = null;
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (ifd, n);
			if (tiff.Tag (entry) != MakerNoteTag || tiff.Type (entry) != 7)
				continue;
			var size = tiff.Count (entry);
			var at = tiff.Value (entry);
			if (size <= 4 || size > tiff.Length - at)
				continue;
			if (IsUsable (tiff.Data.Slice ((int)at, (int)size)))
				note = (at, size);
		}

		return note;
	}

	// Another maker note is usable as it is; an Apple one only with its exact header and an entry table that fits.
	// The maker note is a big-endian IFD at offset 14 of its own bytes (14 is past the classic header).
	static bool IsUsable (ReadOnlySpan<byte> bytes)
	{
		if (!bytes.StartsWith (MakerNoteHeader.Slice (0, 10)))
			return true;
		if (!bytes.StartsWith (MakerNoteHeader))
			return false;
		return new TiffReader (bytes, littleEndian: false).TryTable (MakerNoteHeader.Length, out _);
	}

	// The last usable 0x0021 and 0x0030 entries of an Apple maker note: SRATIONAL, count at least 1, every value
	// inside the maker note (any offset). Offsets count from the maker note. The IFD is the one TiffReader reads.
	static (Winner Headroom, Winner Gain) HeadroomEntries (TiffReader tiff)
	{
		if (!tiff.TryTable (MakerNoteHeader.Length, out var count))
			return default;
		var (headroom, gain) = (default (Winner), default (Winner));
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (MakerNoteHeader.Length, n);
			var tag = tiff.Tag (entry);
			if ((tag != HeadroomTag && tag != GainTag) || tiff.Type (entry) != SRationalType)
				continue;
			var values = tiff.Count (entry);
			var offset = tiff.Value (entry);
			if (values == 0 || values > (tiff.Length - offset) / 8)
				continue;
			if (tag == HeadroomTag)
				headroom = new Winner (offset, values);
			else
				gain = new Winner (offset, values);
		}

		return (headroom, gain);
	}

	// TIFF header, IFD0 at 8 (the Exif IFD pointer), the Exif IFD at 26 (the maker note), the maker note at 44:
	// its header, two entries, a zero next-IFD offset, and the two values at 44 and 52 of the maker note.
	static byte[] HeadroomTiff (ReadOnlySpan<byte> headroom, ReadOnlySpan<byte> gain)
	{
		const int ExifAt = 26;
		const int NoteAt = 44;
		const int NoteLength = 14 + 2 + 24 + 4 + 16;
		var output = new byte[NoteAt + NoteLength];
		"MM\0*"u8.CopyTo (output);
		Endian.WriteU32 (output, 4, 8, le: false);
		Endian.WriteU16 (output, 8, 1, le: false);
		WriteEntry (output, 10, TiffReader.ExifIfdPointer, 4, 1, ExifAt);
		Endian.WriteU16 (output, ExifAt, 1, le: false);
		WriteEntry (output, ExifAt + 2, MakerNoteTag, 7, NoteLength, NoteAt);
		MakerNoteHeader.CopyTo (output.AsSpan (NoteAt));
		Endian.WriteU16 (output, NoteAt + 14, 2, le: false);
		WriteEntry (output, NoteAt + 16, HeadroomTag, SRationalType, 1, 44);
		WriteEntry (output, NoteAt + 28, GainTag, SRationalType, 1, 52);
		headroom.CopyTo (output.AsSpan (NoteAt + 44));
		gain.CopyTo (output.AsSpan (NoteAt + 52));
		return output;

		static void WriteEntry (byte[] data, int at, int tag, int type, int count, int value)
		{
			Endian.WriteU16 (data, at, tag, le: false);
			Endian.WriteU16 (data, at + 2, type, le: false);
			Endian.WriteU32 (data, at + 4, count, le: false);
			Endian.WriteU32 (data, at + 8, value, le: false);
		}
	}
}
