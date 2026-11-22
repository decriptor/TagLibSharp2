// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

namespace TagLibSharp2.Tests.Kit;

/// <summary>
/// Builders for deeply nested structures, written in one pass so 100,000 levels cost a few megabytes.
/// </summary>
/// <remarks>
/// Each chain is <c>depth</c> copies of one header, every level sized to hold the rest of the chain,
/// then the innermost bytes. The widest size field each format allows is used at every level
/// (8-octet EBML sizes, 64-bit ISO box sizes), so the file is as small as the depth allows only through
/// its headers and a reader cannot reject it by size.
/// </remarks>
public static class Nesting
{
	/// <summary>The depth the class-level tests use: deeper than any stack a recursive reader has.</summary>
	public const int HostileDepth = 100_000;

	delegate void HeaderWriter (Span<byte> header, long total);

	/// <summary>
	/// An EBML element with an 8-octet data size.
	/// </summary>
	/// <remarks>RFC 8794 §4.4: a VINT of any width up to EBMLMaxSizeLength (8) encodes the size.</remarks>
	public static byte[] EbmlElement (ulong id, ReadOnlySpan<byte> payload)
	{
		var idBytes = EbmlId (id);
		var result = new byte[idBytes.Length + 8 + payload.Length];
		idBytes.CopyTo (result, 0);
		WriteEbmlSize (result.AsSpan (idBytes.Length), (ulong)payload.Length);
		payload.CopyTo (result.AsSpan (idBytes.Length + 8));
		return result;
	}

	/// <summary>
	/// An EBML element of unknown size: an 8-octet VINT with every data bit set (RFC 8794 §6.2).
	/// </summary>
	public static byte[] EbmlUnknownSize (ulong id, ReadOnlySpan<byte> payload)
	{
		var idBytes = EbmlId (id);
		var result = new byte[idBytes.Length + 8 + payload.Length];
		idBytes.CopyTo (result, 0);
		result[idBytes.Length] = 0x01;
		result.AsSpan (idBytes.Length + 1, 7).Fill (0xFF);
		payload.CopyTo (result.AsSpan (idBytes.Length + 8));
		return result;
	}

	/// <summary>
	/// <paramref name="depth"/> nested EBML master elements of one ID, each with an 8-octet size,
	/// each starting with <paramref name="prefix"/>, around <paramref name="innermost"/>.
	/// </summary>
	public static byte[] EbmlChain (ulong id, int depth, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> innermost)
	{
		var idBytes = EbmlId (id);
		var prefixBytes = prefix.ToArray ();
		return Chain (depth, idBytes.Length + 8 + prefixBytes.Length, innermost, (header, total) => {
			idBytes.CopyTo (header);
			WriteEbmlSize (header.Slice (idBytes.Length), (ulong)(total - idBytes.Length - 8));
			prefixBytes.CopyTo (header.Slice (idBytes.Length + 8));
		});
	}

	/// <summary>The big-endian bytes of an EBML element ID, marker bits included.</summary>
	public static byte[] EbmlId (ulong id)
	{
		var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)id;
			id >>= 8;
		}

		return bytes;
	}

	/// <summary>
	/// An ISO BMFF box with a 64-bit size: size 1, the type, then largesize (ISO/IEC 14496-12 §4.2).
	/// </summary>
	public static byte[] IsoBox (string type, ReadOnlySpan<byte> payload)
	{
		var result = new byte[16 + payload.Length];
		WriteIsoHeader (result, type, result.Length);
		payload.CopyTo (result.AsSpan (16));
		return result;
	}

	/// <summary>
	/// <paramref name="depth"/> nested ISO BMFF boxes of one type, each with a 64-bit size,
	/// around <paramref name="innermost"/>.
	/// </summary>
	public static byte[] IsoBoxChain (string type, int depth, ReadOnlySpan<byte> innermost) =>
		Chain (depth, 16, innermost, (header, total) => WriteIsoHeader (header, type, total));

	/// <summary>
	/// <paramref name="depth"/> nested RIFF LIST chunks of one list type around <paramref name="innermost"/>.
	/// </summary>
	/// <remarks>
	/// RIFF chunk sizes are 32-bit little-endian and exclude the 8-byte header; a LIST payload starts with
	/// its four-character list type (Microsoft RIFF, "LIST chunk"). Pass an even-length innermost so every
	/// level stays word-aligned.
	/// </remarks>
	public static byte[] RiffListChain (string listType, int depth, ReadOnlySpan<byte> innermost)
	{
		var typeBytes = FourCc (listType);
		return Chain (depth, 12, innermost, (header, total) => {
			"LIST"u8.CopyTo (header);
			BinaryPrimitives.WriteUInt32LittleEndian (header.Slice (4), checked((uint)(total - 8)));
			typeBytes.CopyTo (header.Slice (8));
		});
	}

	/// <summary>
	/// <paramref name="depth"/> ID3v2.4 CHAP frames, each embedding the next as a sub-frame, with a TIT2
	/// of <paramref name="innermostTitle"/> in the deepest one.
	/// </summary>
	/// <remarks>
	/// ID3v2 Chapter Frame Addendum 1.0 §3.1: element ID, start and end time, start and end offset,
	/// then optional embedded frames. Frame sizes are syncsafe in ID3v2.4 (id3v2.4.0-structure §4).
	/// </remarks>
	public static byte[] Id3v2ChapterChain (int depth, string innermostTitle)
	{
		// Element ID "c", then times 0..1000 ms and offsets 0xFFFFFFFF (unused).
		ReadOnlySpan<byte> fields = [(byte)'c', 0, 0, 0, 0, 0, 0, 0, 0x03, 0xE8, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
		return FrameChain ("CHAP", depth, fields, Id3v2TextFrame ("TIT2", innermostTitle));
	}

	/// <summary>
	/// <paramref name="depth"/> ID3v2.4 CTOC frames, each embedding the next as a sub-frame, with a TIT2
	/// of <paramref name="innermostTitle"/> in the deepest one.
	/// </summary>
	/// <remarks>
	/// ID3v2 Chapter Frame Addendum 1.0 §3.2: element ID, flags, entry count, child element IDs,
	/// then optional embedded frames.
	/// </remarks>
	public static byte[] Id3v2TocChain (int depth, string innermostTitle)
	{
		// Element ID "t", flags 0x01 (ordered), one child "c".
		ReadOnlySpan<byte> fields = [(byte)'t', 0, 0x01, 1, (byte)'c', 0];
		return FrameChain ("CTOC", depth, fields, Id3v2TextFrame ("TIT2", innermostTitle));
	}

	/// <summary>An ID3v2.4 text frame in UTF-8.</summary>
	public static byte[] Id3v2TextFrame (string id, string text)
	{
		var body = new byte[1 + Encoding.UTF8.GetByteCount (text)];
		body[0] = 0x03;
		Encoding.UTF8.GetBytes (text, body.AsSpan (1));
		var frame = new byte[10 + body.Length];
		WriteFrameHeader (frame, id, body.Length);
		body.CopyTo (frame, 10);
		return frame;
	}

	/// <summary>An ID3v2.4 tag around <paramref name="frames"/>: no flags, syncsafe size (id3v2.4.0-structure §3.1).</summary>
	public static byte[] Id3v2Tag (ReadOnlySpan<byte> frames)
	{
		var result = new byte[10 + frames.Length];
		"ID3"u8.CopyTo (result);
		result[3] = 4;
		WriteSyncsafe (result.AsSpan (6), frames.Length);
		frames.CopyTo (result.AsSpan (10));
		return result;
	}

	/// <summary>Writes a 28-bit syncsafe integer: seven bits per byte, high bit clear.</summary>
	public static void WriteSyncsafe (Span<byte> destination, int value)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan (value, 0x0FFF_FFFF);
		destination[0] = (byte)((value >> 21) & 0x7F);
		destination[1] = (byte)((value >> 14) & 0x7F);
		destination[2] = (byte)((value >> 7) & 0x7F);
		destination[3] = (byte)(value & 0x7F);
	}

	/// <summary>Joins byte arrays.</summary>
	public static byte[] Concat (params byte[][] parts)
	{
		ArgumentNullException.ThrowIfNull (parts);
		var result = new byte[parts.Sum (p => p.Length)];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}

		return result;
	}

	static byte[] FrameChain (string id, int depth, ReadOnlySpan<byte> fields, ReadOnlySpan<byte> innermost)
	{
		var fieldBytes = fields.ToArray ();
		return Chain (depth, 10 + fieldBytes.Length, innermost, (header, total) => {
			WriteFrameHeader (header, id, checked((int)(total - 10)));
			fieldBytes.CopyTo (header.Slice (10));
		});
	}

	static void WriteFrameHeader (Span<byte> header, string id, int size)
	{
		FourCc (id).CopyTo (header);
		WriteSyncsafe (header.Slice (4), size);
		header[8] = 0;
		header[9] = 0;
	}

	static byte[] Chain (int depth, int headerLength, ReadOnlySpan<byte> innermost, HeaderWriter writeHeader)
	{
		ArgumentOutOfRangeException.ThrowIfNegative (depth);
		var result = new byte[checked((headerLength * depth) + innermost.Length)];
		for (var level = 0; level < depth; level++) {
			var at = level * headerLength;
			writeHeader (result.AsSpan (at, headerLength), result.Length - at);
		}

		innermost.CopyTo (result.AsSpan (headerLength * depth));
		return result;
	}

	static void WriteEbmlSize (Span<byte> destination, ulong size)
	{
		destination[0] = 0x01;
		for (var i = 7; i >= 1; i--) {
			destination[i] = (byte)size;
			size >>= 8;
		}
	}

	static void WriteIsoHeader (Span<byte> header, string type, long total)
	{
		BinaryPrimitives.WriteUInt32BigEndian (header, 1);
		FourCc (type).CopyTo (header.Slice (4));
		BinaryPrimitives.WriteUInt64BigEndian (header.Slice (8), (ulong)total);
	}

	static byte[] FourCc (string value)
	{
		var bytes = Encoding.Latin1.GetBytes (value);
		if (bytes.Length != 4)
			throw new ArgumentException ($"'{value}' is not four characters", nameof (value));
		return bytes;
	}
}
