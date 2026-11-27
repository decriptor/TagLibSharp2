// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image.Xmp;

namespace TagLibSharp2.Image.Gif;

/// <summary>
/// Reads a GIF's comment extensions and splices a new one after the header (GIF89a §24). Every other block, and
/// anything after the trailer, is copied as it is; the XMP copy of a changed comment is updated.
/// </summary>
static class GifRewriter
{
	internal const string HeaderError = "The GIF header is truncated, so the comment cannot be spliced in.";
	internal const string SpliceError =
		"The GIF block list is truncated or holds an unknown block, so the comment cannot be spliced in.";

	public static void Read (ReadOnlySpan<byte> data, ImageTag tag)
	{
		if (TryComments (data, out var text))
			tag.ReadField (ImageStores.Comment, text);
	}

	/// <summary>The edited file as new pieces and ranges of <paramref name="data"/>.</summary>
	public static ImagePlan? Plan (ReadOnlySpan<byte> data, ImageTag tag, out string? error)
	{
		error = null;
		var replaceComment = tag.Changed (ImageStores.Comment);
		if (!replaceComment && !tag.DropUnmapped)
			return null;
		if (!TryHeader (data, out var header)) {
			error = HeaderError;
			return null;
		}

		var plan = new ImagePlan ();
		var comment = replaceComment ? tag.Field (ImageStores.Comment) : null;
		if (comment is not null && data.StartsWith ("GIF87a"u8)) {
			plan.Add ("GIF89a"u8);
			plan.Copy (6, header - 6);
		} else {
			plan.Copy (0, header);
		}

		if (comment is not null)
			plan.Add (CommentBytes (comment));
		if (TryCopyBlocks (data, header, tag, replaceComment, plan, out error))
			return plan;
		error ??= SpliceError;
		return null;
	}

	public static byte[]? Rewrite (ReadOnlySpan<byte> data, ImageTag tag, out string? error)
	{
		var plan = Plan (data, tag, out error);
		return plan?.Materialize (data);
	}

	static byte[] CommentBytes (string comment)
	{
		using var builder = new BinaryDataBuilder ();
		AddComment (builder, comment);
		return builder.ToArray ();
	}

	// Copies the blocks after the header through the trailer and what follows it. False at a truncated or unknown
	// block (error null), or when the XMP copy cannot be updated (error set).
	static bool TryCopyBlocks (
		ReadOnlySpan<byte> data,
		int header,
		ImageTag tag,
		bool replaceComment,
		ImagePlan plan,
		out string? error)
	{
		error = null;
		var mirrors = replaceComment && !tag.DropUnmapped;
		var i = header;
		while (i < data.Length) {
			var start = i;
			var introducer = data[i];
			if (introducer == 0x3B) {
				plan.Copy (start, data.Length - start);
				return true;
			}

			if (introducer == 0x2C) {
				if (!SkipImage (data, ref i))
					return false;
				plan.Copy (start, i - start);
				continue;
			}

			if (introducer != 0x21 || i + 2 > data.Length || !SkipSubBlocks (data, ref i, startAtLabel: true))
				return false;
			if (!TryCopyExtension (data, start, i - start, tag, replaceComment, mirrors, plan, out error))
				return false;
		}

		return false;
	}

	static bool TryCopyExtension (
		ReadOnlySpan<byte> data,
		int start,
		int length,
		ImageTag tag,
		bool replaceComment,
		bool mirrors,
		ImagePlan plan,
		out string? error)
	{
		error = null;
		var block = data.Slice (start, length);
		if ((replaceComment && block[1] == 0xFE) || (tag.DropUnmapped && !IsKeptOnClear (block)))
			return true;
		if (!mirrors || !IsXmp (block)) {
			plan.Copy (start, length);
			return true;
		}

		if (!TryMirrorXmp (block, tag, out var edited, out error))
			return false;
		if (edited is not null)
			plan.Add (edited);
		else
			plan.Copy (start, length);
		return true;
	}

	// GIF89a defines four extensions: graphic control (0xF9), plain text (0x01), comment (0xFE) and
	// application (0xFF). Clear keeps graphic control and plain text (they are drawn), and the application
	// extensions a decoder or colour management needs: NETSCAPE2.0 and ANIMEXTS1.0 (the loop count) and
	// ICCRGBG1012 (the ICC profile). Every other application extension (XMP, ImageMagick's IPTC and 8BIM, and
	// unknown ones) and any extension with an undefined label is dropped; the comment goes through the tag.
	static bool IsKeptOnClear (ReadOnlySpan<byte> block)
	{
		var label = block[1];
		if (label is 0xF9 or 0x01 or 0xFE)
			return true;
		if (label != 0xFF || block.Length < 14 || block[2] != 11)
			return false;
		var id = block.Slice (3, 11);
		return id.SequenceEqual ("NETSCAPE2.0"u8) || id.SequenceEqual ("ANIMEXTS1.0"u8) || id.SequenceEqual ("ICCRGBG1012"u8);
	}

	static bool IsXmp (ReadOnlySpan<byte> block) =>
		block.Length >= 14 && block[1] == 0xFF && block[2] == 11 && block.Slice (3, 11).SequenceEqual ("XMP DataXMP"u8);

	// XMP Specification Part 3 §1.1.2: the GIF XMP extension holds the packet bytes as they are, then a
	// 258-byte "magic trailer" (0x01, 0xFF down to 0x00, then 0x00) that makes the bytes parse as sub-blocks.
	static byte[] XmpTrailer ()
	{
		var trailer = new byte[258];
		trailer[0] = 0x01;
		for (var n = 0; n < 256; n++)
			trailer[1 + n] = (byte)(0xFF - n);
		return trailer;
	}

	static bool TryMirrorXmp (ReadOnlySpan<byte> block, ImageTag tag, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		var trailer = XmpTrailer ();
		if (block.Length < 14 + trailer.Length || !block.Slice (block.Length - trailer.Length).SequenceEqual (trailer))
			return TryMirrorXmpSubBlocks (block, tag, out edited, out error);

		var packet = Encoding.UTF8.GetString (block.Slice (14, block.Length - 14 - trailer.Length));
		if (!XmpMirror.TryUpdate (packet, tag, "GIF XMP packet", out var updated, out error))
			return false;
		if (ReferenceEquals (updated, packet))
			return true;
		var bytes = Encoding.UTF8.GetBytes (updated);
		edited = new byte[14 + bytes.Length + trailer.Length];
		block.Slice (0, 14).CopyTo (edited);
		bytes.CopyTo (edited, 14);
		trailer.CopyTo (edited, 14 + bytes.Length);
		return true;
	}

	// ImageIO writes the GIF XMP packet as ordinary data sub-blocks (GIF89a §15) instead of the raw bytes and the
	// magic trailer. The packet is then the sub-blocks joined, and is written back the same way.
	static bool TryMirrorXmpSubBlocks (ReadOnlySpan<byte> block, ImageTag tag, out byte[]? edited, out string? error)
	{
		edited = null;
		error = null;
		var joined = JoinSubBlocks (block, out var end);
		if (end != block.Length - 1 || !joined.Contains ((byte)'<')) {
			if (block.IndexOf ("UserComment"u8) < 0)
				return true;
			error = "The GIF XMP extension is neither the XMP raw form with its magic trailer nor data sub-blocks, and holds a UserComment, so that copy of Comment cannot be located and updated.";
			return false;
		}

		var packet = Encoding.UTF8.GetString ([.. joined]);
		if (!XmpMirror.TryUpdate (packet, tag, "GIF XMP packet", out var updated, out error))
			return false;
		if (ReferenceEquals (updated, packet))
			return true;
		using var builder = new BinaryDataBuilder (block.Length + 64);
		builder.Add (block.Slice (0, 14));
		var bytes = Encoding.UTF8.GetBytes (updated);
		for (var at = 0; at < bytes.Length; at += 255) {
			var take = Math.Min (255, bytes.Length - at);
			builder.Add ((byte)take);
			builder.Add (bytes.AsSpan (at, take));
		}

		builder.Add (0);
		edited = builder.ToArray ();
		return true;
	}

	// The data of the sub-blocks after the 14-byte application header, and where the walk stopped.
	static List<byte> JoinSubBlocks (ReadOnlySpan<byte> block, out int end)
	{
		var joined = new List<byte> (block.Length);
		var i = 14;
		while (i < block.Length && block[i] != 0) {
			var size = block[i];
			if (i + 1 + size > block.Length)
				break;
			for (var n = 0; n < size; n++)
				joined.Add (block[i + 1 + n]);
			i += 1 + size;
		}

		end = i;
		return joined;
	}

	static bool TryComments (ReadOnlySpan<byte> data, out string? text)
	{
		text = null;
		if (!TryHeader (data, out var header))
			return false;
		var collected = new List<byte> ();
		var i = header;
		while (i < data.Length) {
			var introducer = data[i];
			if (introducer == 0x3B) {
				text = collected.Count == 0 ? null : Encoding.UTF8.GetString (collected.ToArray ());
				return true;
			}

			var skipped = introducer switch {
				0x21 => i + 2 <= data.Length && SkipSubBlocks (data, ref i, startAtLabel: true, data[i + 1] == 0xFE ? collected : null),
				0x2C => SkipImage (data, ref i),
				_ => false
			};
			if (!skipped)
				return false;
		}

		return false;
	}

	static bool TryHeader (ReadOnlySpan<byte> data, out int header)
	{
		header = 0;
		if (data.Length < 13 || (!data.StartsWith ("GIF87a"u8) && !data.StartsWith ("GIF89a"u8)))
			return false;
		var colors = (data[10] & 0x80) == 0 ? 0 : 3 * (1 << ((data[10] & 0x07) + 1));
		header = 13 + colors;
		return header <= data.Length;
	}

	static bool SkipImage (ReadOnlySpan<byte> data, ref int i)
	{
		if (i + 10 > data.Length)
			return false;
		var packed = data[i + 9];
		i += 10;
		if ((packed & 0x80) != 0) {
			var table = 3 * (1 << ((packed & 0x07) + 1));
			if (i + table > data.Length)
				return false;
			i += table;
		}

		if (i >= data.Length)
			return false;
		i++;
		return SkipSubBlocks (data, ref i, startAtLabel: false);
	}

	static bool SkipSubBlocks (ReadOnlySpan<byte> data, ref int i, bool startAtLabel, List<byte>? payload = null)
	{
		if (startAtLabel) {
			if (i + 2 > data.Length)
				return false;
			i += 2;
		}

		while (i < data.Length) {
			var size = data[i++];
			if (size == 0)
				return true;
			if (i + size > data.Length)
				return false;
			if (payload is not null) {
				for (var n = 0; n < size; n++)
					payload.Add (data[i + n]);
			}

			i += size;
		}

		return false;
	}

	/// <summary>A comment extension (GIF89a §24): sub-blocks of at most 255 bytes, split between UTF-8 sequences.</summary>
	public static void AddComment (BinaryDataBuilder builder, string comment)
	{
		var bytes = Encoding.UTF8.GetBytes (comment);
		builder.Add (0x21);
		builder.Add (0xFE);
		var offset = 0;
		while (offset < bytes.Length) {
			var take = Math.Min (255, bytes.Length - offset);
			while (take > 0 && offset + take < bytes.Length && (bytes[offset + take] & 0xC0) == 0x80)
				take--;
			if (take == 0)
				take = Math.Min (255, bytes.Length - offset);
			builder.Add ((byte)take);
			builder.Add (bytes.AsSpan (offset, take));
			offset += take;
		}

		builder.Add (0);
	}
}
