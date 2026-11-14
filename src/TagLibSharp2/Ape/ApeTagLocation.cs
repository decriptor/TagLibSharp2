// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Ape;

/// <summary>
/// Locates an APE tag at the end of a buffer. This is the only EOF placement rule.
/// A full footer (magic, version, reserved) at EOF is used when it parses and bit 29 is clear.
/// Bit 29 means the structure is a header. A header has the same layout as a footer but is not one,
/// so locating continues with the other candidate or the ID3v1-only path.
/// Otherwise, if the last 128 bytes start with "TAG", those bytes are ID3v1 and are not audio.
/// When LYRICS200 sits immediately before that tag and the 6 digits before it measure a body
/// that starts at LYRICSBEGIN, the Lyrics3 v2 block is not audio and is not the APE footer.
/// The APE footer, if any, is the 32 bytes immediately before LYRICSBEGIN.
/// With no Lyrics3 block, the footer is the 32 bytes at EOF-160.
/// When that structure is not a footer, or its size does not fit, the suffix is still ID3v1
/// and audio ends at length-128, or at LYRICSBEGIN when a Lyrics3 block is present.
/// Tag size includes the footer and excludes a header. The extra 32 header bytes count only when
/// the computed header start is "APETAGEX".
/// "APETAGEX" contains the letters TAG at offset 3, not offset 0, so the preamble is not ID3v1.
/// </summary>
internal readonly struct ApeTagLocation
{
	/// <summary>
	/// ID3v1 tags are a fixed 128-byte suffix.
	/// </summary>
	public const int Id3v1Size = 128;

	/// <summary>
	/// Lyrics3 v2 starts with this many bytes ("LYRICSBEGIN"). The size field includes them.
	/// </summary>
	private const int LyricsBeginLength = 11;

	/// <summary>
	/// Lyrics3 v2 stores its body size as 6 ASCII digits immediately before "LYRICS200".
	/// </summary>
	private const int LyricsSizeLength = 6;

	/// <summary>
	/// Lyrics3 v2 ends with this many bytes ("LYRICS200"), immediately before ID3v1.
	/// </summary>
	private const int LyricsEndLength = 9;

	/// <summary>
	/// Gets a value indicating whether a usable footer was found.
	/// </summary>
	public bool Found { get; }

	/// <summary>
	/// Gets the parsed footer when <see cref="Found"/> is true.
	/// </summary>
	public ApeTagFooter? Footer { get; }

	/// <summary>
	/// Gets the exclusive end of the APE tag.
	/// ID3v1 bytes start here when no Lyrics3 block is present. Otherwise the Lyrics3 block starts here.
	/// </summary>
	public int TagEnd { get; }

	/// <summary>
	/// Gets the first byte of the tag. A header is included only when its first eight bytes are APETAGEX.
	/// When only ID3v1 is present, this is length-128.
	/// When Lyrics3 precedes ID3v1 and no APE footer precedes LYRICSBEGIN, this is the start of that block.
	/// Equals the buffer length when no tag suffix was found.
	/// </summary>
	public int AudioEnd { get; }

	/// <summary>
	/// Gets the offset of a trailing ID3v1 tag that is not part of the audio, or -1.
	/// Lyrics3 bytes, when present, are not part of this tag.
	/// </summary>
	public int Id3v1Offset { get; }

	/// <summary>
	/// Gets the offset of a Lyrics3 v2 block (LYRICSBEGIN through LYRICS200) between the APE tag and ID3v1, or -1.
	/// </summary>
	public int LyricsOffset { get; }

	/// <summary>
	/// Gets the length of the Lyrics3 block at <see cref="LyricsOffset"/>, or 0 when there is none.
	/// </summary>
	public int LyricsLength { get; }

	/// <summary>
	/// Gets the footer error when <see cref="Found"/> is false.
	/// </summary>
	public string? Error { get; }

	private ApeTagLocation (
		bool found,
		ApeTagFooter? footer,
		int tagEnd,
		int audioEnd,
		int id3v1Offset,
		int lyricsOffset,
		int lyricsLength,
		string? error)
	{
		Found = found;
		Footer = footer;
		TagEnd = tagEnd;
		AudioEnd = audioEnd;
		Id3v1Offset = id3v1Offset;
		LyricsOffset = lyricsOffset;
		LyricsLength = lyricsLength;
		Error = error;
	}

	/// <summary>
	/// Finds the APE footer and the audio offset in front of that tag.
	/// </summary>
	/// <param name="data">The file bytes.</param>
	/// <returns>
	/// The located tag, or a miss. <see cref="AudioEnd"/> is length-128 when only ID3v1 follows the audio,
	/// the start of a Lyrics3 block when that block sits in front of ID3v1 and no APE tag does,
	/// otherwise the buffer length when no bounded tag was found.
	/// </returns>
	public static ApeTagLocation Locate (ReadOnlySpan<byte> data)
	{
		if (data.Length < ApeTagFooter.Size) {
			return Missing (data.Length, $"Data too short for APE tag: {data.Length} bytes");
		}

		// Prefer a full footer at EOF over a footer planted in front of a trailing "TAG".
		// Bit 29 marks a header, which is not a footer even though the bytes parse.
		var eofFooter = ApeTagFooter.Parse (data.Slice (data.Length - ApeTagFooter.Size, ApeTagFooter.Size));
		if (IsFooter (eofFooter)) {
			return FoundAt (data, eofFooter.Footer!, data.Length - ApeTagFooter.Size);
		}

		if (HasTrailingId3v1 (data)) {
			var error = eofFooter.IsSuccess
				? "APE structure at end of file is a header, not a footer"
				: eofFooter.Error!;

			// Lyrics3 v2, when present, occupies the bytes between the APE footer and ID3v1.
			// LYRICS200 is the 9 bytes immediately before TAG. The footer is not at EOF-160.
			if (TryReadLyrics3 (data, out var lyricsOffset, out var lyricsLength)) {
				if (lyricsOffset >= ApeTagFooter.Size) {
					var footerOffset = lyricsOffset - ApeTagFooter.Size;
					var lyricsFooter = ApeTagFooter.Parse (data.Slice (footerOffset, ApeTagFooter.Size));
					if (IsFooter (lyricsFooter)) {
						return FoundAt (data, lyricsFooter.Footer!, footerOffset, lyricsOffset, lyricsLength);
					}

					error = lyricsFooter.IsSuccess
						? "APE structure before Lyrics3 is a header, not a footer"
						: lyricsFooter.Error!;
				}

				return LyricsAndId3 (data.Length, lyricsOffset, lyricsLength, error);
			}

			if (data.Length >= Id3v1Size + ApeTagFooter.Size) {
				var beforeId3 = data.Length - Id3v1Size - ApeTagFooter.Size;
				var id3Footer = ApeTagFooter.Parse (data.Slice (beforeId3, ApeTagFooter.Size));
				if (IsFooter (id3Footer)) {
					return FoundAt (data, id3Footer.Footer!, beforeId3);
				}

				error = id3Footer.IsSuccess
					? "APE structure before ID3v1 is a header, not a footer"
					: id3Footer.Error!;
			}

			return Id3Only (data.Length, error);
		}

		return Missing (
			data.Length,
			eofFooter.IsSuccess
				? "APE structure at end of file is a header, not a footer"
				: eofFooter.Error!);
	}

	/// <summary>
	/// Writes audio, then the replacement tag, then the original Lyrics3 block when
	/// <paramref name="location"/> has one, then a trailing ID3v1 block.
	/// </summary>
	/// <param name="original">The original file bytes.</param>
	/// <param name="location">The location from <see cref="Locate"/> on <paramref name="original"/>.</param>
	/// <param name="tag">The replacement APE tag, or empty when the tag is being removed.</param>
	/// <returns>The rewritten file.</returns>
	public static byte[] Rewrite (ReadOnlySpan<byte> original, ApeTagLocation location, ReadOnlySpan<byte> tag)
	{
		var audioEnd = location.AudioEnd;
		var id3Offset = location.Id3v1Offset;
		var id3Length = id3Offset >= 0 ? Id3v1Size : 0;
		var lyricsOffset = location.LyricsOffset;
		var lyricsLength = lyricsOffset >= 0 ? location.LyricsLength : 0;
		var result = new byte[audioEnd + tag.Length + lyricsLength + id3Length];
		original[..audioEnd].CopyTo (result);
		tag.CopyTo (result.AsSpan (audioEnd));
		var cursor = audioEnd + tag.Length;
		if (lyricsLength > 0) {
			original.Slice (lyricsOffset, lyricsLength).CopyTo (result.AsSpan (cursor));
			cursor += lyricsLength;
		}

		if (id3Length > 0) {
			original.Slice (id3Offset, id3Length).CopyTo (result.AsSpan (cursor));
		}

		return result;
	}

	private static bool HasTrailingId3v1 (ReadOnlySpan<byte> data) =>
		data.Length >= Id3v1Size &&
		data.Slice (data.Length - Id3v1Size, 3).SequenceEqual ("TAG"u8);

	private static bool IsFooter (ApeTagFooterParseResult parsed) =>
		parsed.IsSuccess && !parsed.Footer!.IsHeader;

	private static ApeTagLocation FoundAt (
		ReadOnlySpan<byte> data,
		ApeTagFooter footer,
		int footerOffset,
		int lyricsOffset = -1,
		int lyricsLength = 0)
	{
		var tagEnd = footerOffset + ApeTagFooter.Size;
		// TagSize includes the footer and excludes a header. Add the header only when those bytes are APETAGEX.
		var total = (long)footer.TagSize + HeaderSpan (data, footer, tagEnd);
		var bounded = total >= ApeTagFooter.Size && total <= tagEnd;
		if (!bounded && lyricsOffset >= 0) {
			return LyricsAndId3 (
				data.Length,
				lyricsOffset,
				lyricsLength,
				$"APE tag size ({footer.TagSize}) exceeds data length ({tagEnd})");
		}

		if (!bounded && tagEnd == data.Length - Id3v1Size && HasTrailingId3v1 (data)) {
			return Id3Only (data.Length, $"APE tag size ({footer.TagSize}) exceeds data length ({tagEnd})");
		}

		var audioEnd = bounded ? tagEnd - (int)total : data.Length;
		var id3Offset = -1;
		if (bounded && lyricsOffset >= 0) {
			id3Offset = data.Length - Id3v1Size;
		} else if (bounded && tagEnd < data.Length) {
			id3Offset = tagEnd;
		}

		if (lyricsOffset < 0) {
			lyricsLength = 0;
		}

		return new ApeTagLocation (true, footer, tagEnd, audioEnd, id3Offset, lyricsOffset, lyricsLength, null);
	}

	/// <summary>
	/// Lyrics3 v2 sits immediately before ID3v1. The 9 bytes before TAG are LYRICS200.
	/// The 6 ASCII digits before that are the decimal size of the body, which includes LYRICSBEGIN
	/// and excludes the size field and LYRICS200.
	/// </summary>
	private static bool TryReadLyrics3 (ReadOnlySpan<byte> data, out int lyricsOffset, out int lyricsLength)
	{
		lyricsOffset = -1;
		lyricsLength = 0;

		var id3Offset = data.Length - Id3v1Size;
		var endOffset = id3Offset - LyricsEndLength;
		var sizeOffset = endOffset - LyricsSizeLength;
		if (sizeOffset < LyricsBeginLength)
			return false;

		if (!data.Slice (endOffset, LyricsEndLength).SequenceEqual ("LYRICS200"u8))
			return false;

		var bodySize = 0;
		var sizeDigits = data.Slice (sizeOffset, LyricsSizeLength);
		foreach (var digit in sizeDigits) {
			if (digit is < (byte)'0' or > (byte)'9')
				return false;

			bodySize = (bodySize * 10) + (digit - (byte)'0');
		}

		// The body must contain at least LYRICSBEGIN and must start inside the buffer.
		if (bodySize < LyricsBeginLength || bodySize > sizeOffset)
			return false;

		var begin = sizeOffset - bodySize;
		if (!data.Slice (begin, LyricsBeginLength).SequenceEqual ("LYRICSBEGIN"u8))
			return false;

		lyricsOffset = begin;
		lyricsLength = id3Offset - begin;
		return true;
	}

	private static int HeaderSpan (ReadOnlySpan<byte> data, ApeTagFooter footer, int tagEnd)
	{
		if (!footer.HasHeader)
			return 0;

		var headerStart = (long)tagEnd - footer.TagSize - ApeTagFooter.Size;
		if (headerStart < 0 || headerStart + ApeTagFooter.Magic.Length > data.Length)
			return 0;

		return data.Slice ((int)headerStart, ApeTagFooter.Magic.Length).SequenceEqual (ApeTagFooter.Magic)
			? ApeTagFooter.Size
			: 0;
	}

	private static ApeTagLocation Missing (int length, string error) =>
		new (false, null, length, length, -1, -1, 0, error);

	private static ApeTagLocation Id3Only (int length, string error)
	{
		var id3Offset = length - Id3v1Size;
		return new (false, null, id3Offset, id3Offset, id3Offset, -1, 0, error);
	}

	private static ApeTagLocation LyricsAndId3 (int length, int lyricsOffset, int lyricsLength, string error)
	{
		var id3Offset = length - Id3v1Size;
		return new (false, null, lyricsOffset, lyricsOffset, id3Offset, lyricsOffset, lyricsLength, error);
	}
}
