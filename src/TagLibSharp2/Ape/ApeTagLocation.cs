// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Ape;

/// <summary>
/// Locates an APE tag at the end of a buffer. This is the only EOF-32 / EOF-160 rule.
/// A full footer (magic, version, reserved) at EOF is used when it parses and bit 29 is clear.
/// Bit 29 means the structure is a header. A header has the same layout as a footer but is not one,
/// so locating continues with the other candidate or the ID3v1-only path.
/// Otherwise, if the last 128 bytes start with "TAG", the footer is the 32 bytes at EOF-160.
/// When that structure is not a footer, or its size does not fit, the suffix is still ID3v1
/// and audio ends at length-128.
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
	/// Gets a value indicating whether a footer was parsed.
	/// </summary>
	public bool Found { get; }

	/// <summary>
	/// Gets the parsed footer when <see cref="Found"/> is true.
	/// </summary>
	public ApeTagFooter? Footer { get; }

	/// <summary>
	/// Gets the exclusive end of the APE tag. ID3v1 bytes, when present, start here.
	/// </summary>
	public int TagEnd { get; }

	/// <summary>
	/// Gets the first byte of the tag. A header is included only when its first eight bytes are APETAGEX.
	/// When only ID3v1 is present, this is length-128. Equals the buffer length when no tag suffix was found.
	/// </summary>
	public int AudioEnd { get; }

	/// <summary>
	/// Gets the offset of a trailing ID3v1 tag that is not part of the audio, or -1.
	/// </summary>
	public int Id3v1Offset { get; }

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
		string? error)
	{
		Found = found;
		Footer = footer;
		TagEnd = tagEnd;
		AudioEnd = audioEnd;
		Id3v1Offset = id3v1Offset;
		Error = error;
	}

	/// <summary>
	/// Finds the APE footer and the audio offset in front of that tag.
	/// </summary>
	/// <param name="data">The file bytes.</param>
	/// <returns>
	/// The located tag, or a miss. <see cref="AudioEnd"/> is length-128 when only ID3v1 follows the audio,
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
	/// Writes audio, then the replacement tag, then a trailing ID3v1 block when <paramref name="location"/> has one.
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
		var result = new byte[audioEnd + tag.Length + id3Length];
		original[..audioEnd].CopyTo (result);
		tag.CopyTo (result.AsSpan (audioEnd));
		if (id3Length > 0) {
			original.Slice (id3Offset, id3Length).CopyTo (result.AsSpan (audioEnd + tag.Length));
		}

		return result;
	}

	private static bool HasTrailingId3v1 (ReadOnlySpan<byte> data) =>
		data.Length >= Id3v1Size &&
		data.Slice (data.Length - Id3v1Size, 3).SequenceEqual ("TAG"u8);

	private static bool IsFooter (ApeTagFooterParseResult parsed) =>
		parsed.IsSuccess && !parsed.Footer!.IsHeader;

	private static ApeTagLocation FoundAt (ReadOnlySpan<byte> data, ApeTagFooter footer, int footerOffset)
	{
		var tagEnd = footerOffset + ApeTagFooter.Size;
		// TagSize includes the footer and excludes a header. Add the header only when those bytes are APETAGEX.
		var total = (long)footer.TagSize + HeaderSpan (data, footer, tagEnd);
		var bounded = total >= ApeTagFooter.Size && total <= tagEnd;
		if (!bounded && tagEnd == data.Length - Id3v1Size && HasTrailingId3v1 (data)) {
			return Id3Only (data.Length, $"APE tag size ({footer.TagSize}) exceeds data length ({tagEnd})");
		}

		var audioEnd = bounded ? tagEnd - (int)total : data.Length;
		var id3Offset = bounded && tagEnd < data.Length ? tagEnd : -1;
		return new ApeTagLocation (true, footer, tagEnd, audioEnd, id3Offset, null);
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
		new (false, null, length, length, -1, error);

	private static ApeTagLocation Id3Only (int length, string error)
	{
		var id3Offset = length - Id3v1Size;
		return new (false, null, id3Offset, id3Offset, id3Offset, error);
	}
}
