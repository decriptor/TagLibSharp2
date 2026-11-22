// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;

namespace TagLibSharp2.Aac;

/// <summary>
/// The bytes of a file, from a span or a seekable stream, read the same way. A span is sliced and a stream is
/// read into a new array of exactly the requested length, so one tail rule serves both without a copy of the span.
/// </summary>
internal readonly ref struct AacTailSource
{
	readonly ReadOnlySpan<byte> _span;
	readonly Stream? _stream;

	public AacTailSource (ReadOnlySpan<byte> span)
	{
		_span = span;
		_stream = null;
		Length = span.Length;
	}

	public AacTailSource (Stream stream)
	{
		_span = default;
		_stream = stream;
		Length = stream.Length;
	}

	/// <summary>Gets the length of the data.</summary>
	public long Length { get; }

	/// <summary>
	/// Up to <paramref name="buffer"/>.Length bytes at <paramref name="position"/>, copied into the caller's buffer,
	/// so a walk over many small reads allocates nothing. Returns the bytes that lie inside the data.
	/// </summary>
	public ReadOnlySpan<byte> ReadInto (long position, byte[] buffer, int count)
	{
		count = (int)Math.Min (Math.Min (count, buffer.Length), Length - position);
		if (count <= 0 || position < 0)
			return [];
		if (_stream is null) {
			_span.Slice ((int)position, count).CopyTo (buffer);
			return buffer.AsSpan (0, count);
		}

		_stream.Position = position;
		var read = StreamBytes.Read (_stream, buffer, 0, count);
		return buffer.AsSpan (0, read);
	}

	/// <summary>
	/// The <paramref name="count"/> bytes at <paramref name="position"/>, or an empty span when they do not all lie
	/// inside the data.
	/// </summary>
	public ReadOnlySpan<byte> Read (long position, int count)
	{
		if (count <= 0 || position < 0 || position > Length - count)
			return [];
		if (_stream is null)
			return _span.Slice ((int)position, count);

		_stream.Position = position;
		var bytes = StreamBytes.ReadAtMost (_stream, count);
		return bytes.Length == count ? bytes : [];
	}
}

/// <summary>
/// The trailing tags of a raw AAC file and where its audio ends. This is the one rule detection, the span read and
/// the stream read use, so they always judge the same bytes.
/// </summary>
/// <remarks>
/// <para>
/// The audio ends at the first of: an APEv2 tag that parses, a Lyrics3 block in front of ID3v1, ID3v1, or the end of
/// the file. ID3v1 is the last 128 bytes and starts with "TAG" (https://id3.org/ID3v1). Lyrics3 v2 ends with a 6-digit
/// body size and "LYRICS200" right before ID3v1, and its body starts with "LYRICSBEGIN" (https://id3.org/Lyrics3v2).
/// Lyrics3 v1 ends with "LYRICSEND" right before ID3v1 and starts at the nearest "LYRICSBEGIN" at most 64 KB
/// before it (https://id3.org/Lyrics3).
/// </para>
/// <para>
/// An APEv2 footer (https://wiki.hydrogenaud.io/index.php?title=APEv2_specification) is looked for at the end of
/// the file, else in front of Lyrics3, else in front of ID3v1. Its tag size counts the items and the footer. The 32
/// header bytes in front of the items count only when they are there: the footer's "contains a header" flag (bit 31)
/// is believed only when those bytes start with "APETAGEX", as <see cref="ApeTagLocation"/> and Render read it.
/// A tag that does not parse, or that would start before the audio, is not a tag.
/// </para>
/// </remarks>
internal readonly struct AacTail
{
	const int Id3v1Size = 128;
	const int LyricsBeginLength = 11;
	const int LyricsSizeLength = 6;
	const int LyricsEndLength = 9;
	const int LyricsV1SearchLimit = 64 * 1024;

	AacTail (long audioEnd, long apeStart, long lyricsStart, long id3v1Start, ApeTag? apeTag, bool apeHasHeader, Id3v1Tag? id3v1Tag)
	{
		AudioEnd = audioEnd;
		ApeStart = apeStart;
		LyricsStart = lyricsStart;
		Id3v1Start = id3v1Start;
		ApeTag = apeTag;
		ApeHasHeader = apeHasHeader;
		Id3v1Tag = id3v1Tag;
	}

	/// <summary>Gets the end of the audio region.</summary>
	public long AudioEnd { get; }

	/// <summary>Gets the first byte of the trailing APEv2 tag (its header when it has one), or -1.</summary>
	public long ApeStart { get; }

	/// <summary>Gets the first byte of a Lyrics3 block in front of ID3v1, or -1. It runs to <see cref="Id3v1Start"/>.</summary>
	public long LyricsStart { get; }

	/// <summary>Gets the first byte of the ID3v1 tag, or -1.</summary>
	public long Id3v1Start { get; }

	/// <summary>Gets the trailing APEv2 tag when it was asked for and parsed, or null.</summary>
	public ApeTag? ApeTag { get; }

	/// <summary>Gets a value indicating whether the APEv2 tag had a header in front of its items.</summary>
	public bool ApeHasHeader { get; }

	/// <summary>Gets the ID3v1 tag, or null.</summary>
	public Id3v1Tag? Id3v1Tag { get; }

	/// <summary>
	/// Locates the trailing tags. <paramref name="audioStart"/> is the first byte after a leading ID3v2 tag;
	/// nothing before it is a trailing tag. Placing a tag reads only its footer, header magic, and item headers
	/// and keys: no item value is copied. <paramref name="readTags"/> also builds the tags for a read; detection
	/// and save pass false.
	/// </summary>
	public static AacTail Locate (AacTailSource source, long audioStart, bool readTags)
	{
		var length = source.Length;
		if (!HasMarker (source))
			return new AacTail (length, -1, -1, -1, null, false, null);

		var scratch = new byte[ApeTagItem.MeasureWindow];

		// An APEv2 footer that ends the file decides first: nothing follows it, so "TAG" 128 bytes before the end
		// is inside the APE tag, not an ID3v1 tag (https://id3.org/ID3v1: ID3v1 is the last 128 bytes).
		if (TryApe (source, length, audioStart, readTags, scratch, out var eofApe, out var eofHeader, out var eofStart))
			return new AacTail (eofStart >= 0 ? eofStart : length, eofStart, -1, -1, eofApe, eofHeader, null);

		Id3v1Tag? id3v1 = null;
		long id3Start = -1;
		if (length - Id3v1Size >= audioStart) {
			var read = Id3v1Tag.Read (source.Read (length - Id3v1Size, Id3v1Size));
			if (read.IsSuccess && read.Tag is not null) {
				id3v1 = read.Tag;
				id3Start = length - Id3v1Size;
			}
		}

		var lyricsStart = id3Start >= 0 ? FindLyricsStart (source, id3Start, audioStart) : -1;

		// The APEv2 footer in front of Lyrics3, else in front of ID3v1.
		ApeTag? ape = null;
		var apeHasHeader = false;
		long apeStart = -1;
		if (id3Start >= 0)
			TryApe (source, lyricsStart >= 0 ? lyricsStart : id3Start, audioStart, readTags, scratch, out ape, out apeHasHeader, out apeStart);

		var audioEnd = length;
		if (apeStart >= 0)
			audioEnd = Math.Min (audioEnd, apeStart);
		if (lyricsStart >= 0)
			audioEnd = Math.Min (audioEnd, lyricsStart);
		if (id3Start >= 0)
			audioEnd = Math.Min (audioEnd, id3Start);
		return new AacTail (audioEnd, apeStart, lyricsStart, id3Start, ape, apeHasHeader, id3v1);
	}

	// "TAG" opens the last 128 bytes, or an APEv2 footer ends the file. Without either there is no trailing tag:
	// an APEv2 tag in front of ID3v1 or Lyrics3 needs ID3v1 there.
	static bool HasMarker (AacTailSource source) =>
		source.Read (source.Length - Id3v1Size, 3).SequenceEqual ("TAG"u8)
		|| source.Read (source.Length - ApeTagFooter.Size, 8).SequenceEqual (ApeTagFooter.Magic);

	// True when a valid footer (not a header) ends at tagEnd. A footer that is found decides, whether or not its tag
	// is valid; a footer that is not found leaves the next placement to try. The tag is placed only when its items
	// pass the same rules ApeTag.Parse applies (ItemsAreValid); its object is built only for a read.
	static bool TryApe (AacTailSource source, long tagEnd, long audioStart, bool readTags, byte[] scratch, out ApeTag? tag, out bool hasHeader, out long start)
	{
		tag = null;
		hasHeader = false;
		start = -1;
		var footerBytes = source.Read (tagEnd - ApeTagFooter.Size, ApeTagFooter.Size);
		if (footerBytes.IsEmpty)
			return false;

		var footer = ApeTagFooter.Parse (footerBytes);
		if (!footer.IsSuccess || footer.Footer is not { IsHeader: false } parsed)
			return false;

		var total = (long)parsed.TagSize;
		if (parsed.HasHeader && total <= tagEnd - ApeTagFooter.Size
			&& source.Read (tagEnd - total - ApeTagFooter.Size, 8).SequenceEqual (ApeTagFooter.Magic))
			total += ApeTagFooter.Size;

		if (total < ApeTagFooter.Size || total > tagEnd - audioStart || total > int.MaxValue)
			return true;

		var itemsSize = (long)parsed.TagSize - ApeTagFooter.Size;
		if (!ItemsAreValid (source, tagEnd - parsed.TagSize, itemsSize, parsed.ItemCount, scratch))
			return true;

		start = tagEnd - total;
		hasHeader = total > parsed.TagSize;
		if (readTags) {
			var result = ApeTag.Parse (source.Read (start, (int)total));
			if (result.IsSuccess)
				tag = result.Tag;
		}

		return true;
	}

	/// <summary>
	/// The item checks of <see cref="ApeTag.Parse"/>, without copying a value: the 11-byte-per-item budget, each
	/// item by <see cref="ApeTagItem.TryMeasure"/> (the rules <see cref="ApeTagItem.Parse"/> uses), and the count rule.
	/// Each item costs one read of at most <see cref="ApeTagItem.MeasureWindow"/> bytes into <paramref name="scratch"/>.
	/// </summary>
	static bool ItemsAreValid (AacTailSource source, long itemsStart, long itemsSize, uint count, byte[] scratch)
	{
		const int minItemSize = ApeTagItem.MinHeaderSize + ApeTagItem.MinKeyLength + 1;
		if (itemsSize < 0 || (ulong)count * minItemSize > (ulong)itemsSize)
			return false;

		long offset = 0;
		uint parsed = 0;
		while (offset < itemsSize && parsed < count) {
			var remaining = itemsSize - offset;
			var head = source.ReadInto (itemsStart + offset, scratch, (int)Math.Min (ApeTagItem.MeasureWindow, remaining));
			if (!ApeTagItem.TryMeasure (head, remaining, out var consumed) || consumed <= 0)
				return false;
			offset += consumed;
			parsed++;
		}

		return parsed == count || (offset == itemsSize && parsed > 0);
	}

	// The first byte of a Lyrics3 v2 or v1 block that ends right before ID3v1, or -1.
	static long FindLyricsStart (AacTailSource source, long id3Start, long audioStart)
	{
		var endMarker = id3Start - LyricsEndLength;
		var marker = source.Read (endMarker, LyricsEndLength);
		if (marker.SequenceEqual ("LYRICS200"u8)) {
			var sizeAt = endMarker - LyricsSizeLength;
			var digits = source.Read (sizeAt, LyricsSizeLength);
			if (digits.IsEmpty)
				return -1;

			long size = 0;
			foreach (var digit in digits) {
				if (digit is < (byte)'0' or > (byte)'9')
					return -1;
				size = (size * 10) + (digit - (byte)'0');
			}

			var begin = sizeAt - size;
			if (size < LyricsBeginLength || begin < audioStart)
				return -1;
			return source.Read (begin, LyricsBeginLength).SequenceEqual ("LYRICSBEGIN"u8) ? begin : -1;
		}

		if (marker.SequenceEqual ("LYRICSEND"u8)) {
			var from = Math.Max (audioStart, endMarker - LyricsV1SearchLimit);
			var window = source.Read (from, (int)(endMarker - from));
			for (var i = window.Length - LyricsBeginLength; i >= 0; i--) {
				if (window.Slice (i, LyricsBeginLength).SequenceEqual ("LYRICSBEGIN"u8))
					return from + i;
			}
		}

		return -1;
	}
}
