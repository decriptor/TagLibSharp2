// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

namespace TagLibSharp2.Tests.Audible;

/// <summary>
/// Builds Audible .aa files with the layout of the real samples: a 16-byte header, a 12-entry
/// table of contents (types 0 to 11 in order), and chunks 1 to 11 back to back.
/// </summary>
internal static class AudibleTestBuilder
{
	public const uint Magic = 0x57907536;
	public const int HeaderSize = 16;
	public const int TocEntries = 12;
	public const int TocEnd = HeaderSize + (TocEntries * 12);

	public static readonly (string Key, string Value)[] DefaultEntries = [
		("product_id", "PF_TEST_000001_acelp85"),
		("title", "A Test Book (Unabridged)"),
		("author", "Ann Author"),
		("provider", "Test Provider"),
		("narrator", "Ned Narrator"),
		("price", "1.98"),
		("pubdate", "20-JAN-2009"),
		("description", "Short description."),
		("keywords", "one, two"),
		("long_description", "A longer description of the book."),
		("copyright", "&#169;2009 Ann Author; (P)2009 Test"),
		("short_title", "A Test Book"),
		("title_id", "PF_TEST_000001"),
		("codec", "acelp85"),
		("HeaderSeed", "74039501"),
		("EncryptedBlocks", "6457"),
		("HeaderKey", "2348022159 2271241904 4180629618 1869089845"),
		("license_list", "55035360"),
		("CPUType", "1"),
		("license_count", "1"),
		("68ec733412b9", "6BBD842E462FE82ED754F4EBC32C096EF3120A54"),
		("parent_short_title", "A Test Book"),
		("parent_title", "A Test Book (Unabridged)"),
		("pub_date_start", "20-JAN-2009"),
		("short_description", "Short description."),
		("user_alias", "someone"),
	];

	/// <summary>A tiny JPEG-looking image (SOI marker, filler, EOI marker).</summary>
	public static byte[] Jpeg ()
	{
		var image = new byte[64];
		image[0] = 0xFF;
		image[1] = 0xD8;
		image[2] = 0xFF;
		image[3] = 0xE0;
		for (var i = 4; i < image.Length - 2; i++)
			image[i] = (byte)(i * 3);
		image[^2] = 0xFF;
		image[^1] = 0xD9;
		return image;
	}

	/// <summary>An audio chunk whose bytes are easy to find after a save.</summary>
	public static byte[] Audio (int length)
	{
		var audio = new byte[length];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)(0x5A ^ (i * 7));
		return audio;
	}

	public static byte[] Dictionary (IEnumerable<(string Key, string Value)> entries)
	{
		using var ms = new MemoryStream ();
		var list = entries.ToList ();
		WriteU32 (ms, (uint)list.Count);
		foreach (var (key, value) in list) {
			var k = Encoding.ASCII.GetBytes (key);
			var v = Encoding.UTF8.GetBytes (value);
			ms.WriteByte (0);
			WriteU32 (ms, (uint)k.Length);
			WriteU32 (ms, (uint)v.Length);
			ms.Write (k, 0, k.Length);
			ms.Write (v, 0, v.Length);
		}
		return ms.ToArray ();
	}

	/// <summary>
	/// Builds a whole file. <paramref name="cover"/> null leaves chunk 11 empty.
	/// </summary>
	public static byte[] Create (
		IEnumerable<(string Key, string Value)>? entries = null,
		byte[]? audio = null,
		byte[]? cover = null,
		string? codec = null)
	{
		var list = (entries ?? DefaultEntries).ToList ();
		if (codec is not null)
			list = list.Select (e => e.Key == "codec" ? (e.Key, codec) : e).ToList ();
		return CreateRaw (Dictionary (list), audio ?? Audio (8500), cover);
	}

	/// <summary>
	/// Builds a whole file around raw dictionary bytes.
	/// </summary>
	public static byte[] CreateRaw (byte[] dictionary, byte[] audio, byte[]? cover)
	{
		var chunks = new byte[12][];
		chunks[1] = Concat (U32 (0x00010000), U32 (Magic), new byte[16]);
		chunks[2] = dictionary;
		chunks[3] = Filler (74, 3);
		chunks[4] = new byte[4];
		chunks[5] = Concat (U32 (1), U32 (0x3288B995));
		chunks[6] = Concat (U32 (1), U32 (3), U32 (0xFFFFFFFF), U32 (0xFFFFFFFF), U32 ((uint)audio.Length), U32 (2), U16 (0xC010), U32 (2), U32 (0), U32 (0), U32 (1), U32 (0x415));
		chunks[7] = Filler (16, 7);
		chunks[8] = new byte[4];
		chunks[9] = [];
		chunks[10] = audio;
		chunks[11] = []; // placeholder, filled once its offset is known

		var offsets = new uint[12];
		var position = (uint)TocEnd;
		for (var t = 1; t <= 11; t++) {
			offsets[t] = position;
			if (t == 11 && cover is not null)
				chunks[11] = Concat (U32 ((uint)cover.Length), U32 (position + 8), cover);
			position += (uint)chunks[t].Length;
		}

		var total = position;
		using var ms = new MemoryStream ();
		WriteU32 (ms, total);
		WriteU32 (ms, Magic);
		WriteU32 (ms, TocEntries);
		WriteU32 (ms, 0);
		WriteU32 (ms, 0);
		WriteU32 (ms, 0);
		WriteU32 (ms, total);
		for (var t = 1; t <= 11; t++) {
			WriteU32 (ms, (uint)t);
			WriteU32 (ms, offsets[t]);
			WriteU32 (ms, (uint)chunks[t].Length);
		}
		for (var t = 1; t <= 11; t++)
			ms.Write (chunks[t], 0, chunks[t].Length);
		return ms.ToArray ();
	}

	public static (uint Type, uint Offset, uint Size) Toc (byte[] file, int index)
	{
		var at = HeaderSize + (index * 12);
		return (
			BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at)),
			BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at + 4)),
			BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at + 8)));
	}

	public static byte[] Chunk (byte[] file, int index)
	{
		var (_, offset, size) = Toc (file, index);
		return file.AsSpan ((int)offset, (int)size).ToArray ();
	}

	public static uint ReadU32 (byte[] file, int offset) =>
		BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (offset));

	public static void SetU32 (byte[] file, int offset, uint value) =>
		BinaryPrimitives.WriteUInt32BigEndian (file.AsSpan (offset), value);

	static byte[] Filler (int length, int seed)
	{
		var data = new byte[length];
		for (var i = 0; i < length; i++)
			data[i] = (byte)(seed + (i * 11));
		return data;
	}

	static byte[] U32 (uint value)
	{
		var data = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian (data, value);
		return data;
	}

	static byte[] U16 (ushort value)
	{
		var data = new byte[2];
		BinaryPrimitives.WriteUInt16BigEndian (data, value);
		return data;
	}

	static void WriteU32 (Stream stream, uint value)
	{
		var data = U32 (value);
		stream.Write (data, 0, 4);
	}

	public static byte[] Concat (params byte[][] parts)
	{
		var result = new byte[parts.Sum (p => p.Length)];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}
		return result;
	}

	public static int IndexOf (byte[] haystack, byte[] needle)
	{
		for (var i = 0; i + needle.Length <= haystack.Length; i++) {
			if (haystack.AsSpan (i, needle.Length).SequenceEqual (needle))
				return i;
		}
		return -1;
	}
}
