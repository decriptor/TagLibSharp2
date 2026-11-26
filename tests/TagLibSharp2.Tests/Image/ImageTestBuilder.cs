// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// Builds small still-image files in code: PNG chunks, GIF blocks, JPEG segments, TIFF IFDs, and the
/// XMP and Exif blocks that copy a PNG text field.
/// </summary>
static class ImageTestBuilder
{
	public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	public static byte[] Png (params byte[][] chunks) =>
		Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0]), Concat (chunks), PngChunk ("IDAT", [0x78, 0x9C, 0x03, 0x00]), PngChunk ("IEND", []));

	public static byte[] PngChunk (string type, byte[] payload)
	{
		var typeBytes = Encoding.ASCII.GetBytes (type);
		var chunk = new byte[12 + payload.Length];
		BinaryPrimitives.WriteInt32BigEndian (chunk, payload.Length);
		typeBytes.CopyTo (chunk, 4);
		payload.CopyTo (chunk, 8);
		BinaryPrimitives.WriteUInt32BigEndian (chunk.AsSpan (8 + payload.Length), BinaryData.ComputeCrc32 (chunk.AsSpan (4, 4 + payload.Length)));
		return chunk;
	}

	public static byte[] Text (string keyword, string text) =>
		PngChunk ("tEXt", Concat (Encoding.Latin1.GetBytes (keyword), [0], Encoding.Latin1.GetBytes (text)));

	public static byte[] CompressedText (string keyword, byte[] raw) =>
		PngChunk ("zTXt", Concat (Encoding.Latin1.GetBytes (keyword), [0, 0], Zlib (raw)));

	public static byte[] InternationalText (string keyword, string text, bool compressed = false)
	{
		var body = Encoding.UTF8.GetBytes (text);
		return PngChunk ("iTXt", Concat (Encoding.Latin1.GetBytes (keyword), [0, (byte)(compressed ? 1 : 0), 0, 0, 0], compressed ? Zlib (body) : body));
	}

	public static byte[] Xmp (string packet, bool compressed = false) => InternationalText ("XML:com.adobe.xmp", packet, compressed);

	/// <summary>ImageMagick's form: zTXt "Raw profile type xmp" holding the packet as hexadecimal lines.</summary>
	public static byte[] RawProfile (string name, byte[] profile)
	{
		var text = new StringBuilder ();
		text.Append ('\n').Append (name).Append ('\n').Append (profile.Length.ToString (System.Globalization.CultureInfo.InvariantCulture).PadLeft (8)).Append ('\n');
		for (var n = 0; n < profile.Length; n++) {
			text.Append (profile[n].ToString ("x2", System.Globalization.CultureInfo.InvariantCulture));
			if (n % 36 == 35 || n == profile.Length - 1)
				text.Append ('\n');
		}

		return CompressedText ("Raw profile type " + name, Encoding.ASCII.GetBytes (text.ToString ()));
	}

	public static string XmpPacket (string properties, string descriptionAttributes = "") =>
		"<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?>\n" +
		"<x:xmpmeta xmlns:x='adobe:ns:meta/'>\n" +
		" <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>\n" +
		"  <rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/' xmlns:exif='http://ns.adobe.com/exif/1.0/' xmlns:xmp='http://ns.adobe.com/xap/1.0/'" + descriptionAttributes + ">\n" +
		properties +
		"  </rdf:Description>\n" +
		" </rdf:RDF>\n" +
		"</x:xmpmeta>\n" +
		"<?xpacket end='w'?>";

	public static string Alt (string name, string value) =>
		$"   <{name}><rdf:Alt><rdf:li xml:lang='x-default'>{value}</rdf:li><rdf:li xml:lang='de'>{value} de</rdf:li></rdf:Alt></{name}>\n";

	public static string Seq (string name, params string[] values) =>
		$"   <{name}><rdf:Seq>{string.Concat (values.Select (v => $"<rdf:li>{v}</rdf:li>"))}</rdf:Seq></{name}>\n";

	/// <summary>A little-endian TIFF with IFD0 ASCII entries, and an Exif IFD with the given ASCII entries.</summary>
	public static byte[] ExifTiff (IReadOnlyList<(int Tag, string Value)> ifd0, IReadOnlyList<(int Tag, string Value)> exifIfd)
	{
		var entries0 = ifd0.Count + (exifIfd.Count > 0 ? 1 : 0);
		var ifd0Size = 2 + (12 * entries0) + 4;
		var exifSize = exifIfd.Count == 0 ? 0 : 2 + (12 * exifIfd.Count) + 4;
		var valuesAt = 8 + ifd0Size + exifSize;
		var values = new List<byte> ();
		var data = new byte[valuesAt];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (4), 8);
		var all = ifd0.Select (e => (e.Tag, (string?)e.Value)).ToList ();
		if (exifIfd.Count > 0)
			all.Add ((0x8769, null));
		all.Sort ((a, b) => a.Item1.CompareTo (b.Item1));
		WriteIfd (data, 8, all, valuesAt, values, exifAt: 8 + ifd0Size);
		if (exifIfd.Count > 0)
			WriteIfd (data, 8 + ifd0Size, exifIfd.Select (e => (e.Tag, (string?)e.Value)).ToList (), valuesAt, values, exifAt: 0);
		return Concat (data, [.. values]);
	}

	static void WriteIfd (byte[] data, int at, List<(int Tag, string? Value)> entries, int valuesAt, List<byte> values, int exifAt)
	{
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (at), (ushort)entries.Count);
		for (var n = 0; n < entries.Count; n++) {
			var e = at + 2 + (12 * n);
			BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (e), (ushort)entries[n].Tag);
			if (entries[n].Value is not string value) {
				BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (e + 2), 4);
				BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (e + 4), 1);
				BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (e + 8), exifAt);
				continue;
			}

			var bytes = Concat (Encoding.ASCII.GetBytes (value), [0]);
			BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (e + 2), 2);
			BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (e + 4), bytes.Length);
			if (bytes.Length <= 4) {
				bytes.CopyTo (data, e + 8);
			} else {
				BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (e + 8), valuesAt + values.Count);
				values.AddRange (bytes);
			}
		}
	}

	public static List<(string Type, byte[] Payload)> PngChunks (byte[] file)
	{
		var chunks = new List<(string, byte[])> ();
		var i = 8;
		while (i + 12 <= file.Length) {
			var length = BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (i));
			var type = Encoding.ASCII.GetString (file, i + 4, 4);
			Assert.AreEqual (BinaryData.ComputeCrc32 (file.AsSpan (i + 4, 4 + length)), BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (i + 8 + length)), $"{type} CRC");
			chunks.Add ((type, file.AsSpan (i + 8, length).ToArray ()));
			i += 12 + length;
			if (type == "IEND")
				break;
		}

		return chunks;
	}

	/// <summary>The decoded text of every PNG text chunk with <paramref name="keyword"/>.</summary>
	public static List<string> PngTexts (byte[] file, string keyword)
	{
		var texts = new List<string> ();
		foreach (var (type, payload) in PngChunks (file)) {
			if (type is not ("tEXt" or "zTXt" or "iTXt"))
				continue;
			var zero = Array.IndexOf (payload, (byte)0);
			if (Encoding.Latin1.GetString (payload, 0, zero) != keyword)
				continue;
			var rest = payload.AsSpan (zero + 1);
			if (type == "tEXt") {
				texts.Add (Encoding.Latin1.GetString (rest));
			} else if (type == "zTXt") {
				texts.Add (Encoding.Latin1.GetString (Inflate (rest.Slice (1).ToArray ())));
			} else {
				var compressed = rest[0] == 1;
				var i = 2;
				i += rest.Slice (i).IndexOf ((byte)0) + 1;
				i += rest.Slice (i).IndexOf ((byte)0) + 1;
				var body = rest.Slice (i).ToArray ();
				texts.Add (Encoding.UTF8.GetString (compressed ? Inflate (body) : body));
			}
		}

		return texts;
	}

	/// <summary>Decodes an ImageMagick raw profile's hexadecimal lines.</summary>
	public static byte[] DecodeRawProfile (string text)
	{
		var lines = text.Split ('\n', StringSplitOptions.RemoveEmptyEntries);
		var length = int.Parse (lines[1].Trim (), System.Globalization.CultureInfo.InvariantCulture);
		var hex = string.Concat (lines.Skip (2));
		Assert.AreEqual (length * 2, hex.Length, "raw profile length");
		return Convert.FromHexString (hex);
	}

	public static byte[] Gif (params byte[][] blocks)
	{
		var header = new byte[13];
		Encoding.ASCII.GetBytes ("GIF89a").CopyTo (header, 0);
		header[6] = 1;
		header[8] = 1;
		byte[] image = [0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 0x01, 0];
		return Concat (header, Concat (blocks), image, [0x3B]);
	}

	public static byte[] GifComment (string text)
	{
		var bytes = Encoding.UTF8.GetBytes (text);
		return Concat ([0x21, 0xFE, (byte)bytes.Length], bytes, [0]);
	}

	public static byte[] GifApplication (string identifier, byte[] data) =>
		Concat ([0x21, 0xFF, 11], Encoding.ASCII.GetBytes (identifier), [(byte)data.Length], data, [0]);

	public static byte[] Jpeg (params byte[][] segments) =>
		Concat (
			[0xFF, 0xD8],
			Concat (segments),
			JpegSegment (0xC0, [0x08, 0x00, 0x03, 0x00, 0x02, 0x01, 0x01, 0x11, 0x00]),
			JpegSegment (0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]),
			[0x12, 0x34, 0xFF, 0x00, 0x56],
			[0xFF, 0xD9]);

	public static byte[] JpegSegment (byte marker, byte[] payload)
	{
		var segment = new byte[4 + payload.Length];
		segment[0] = 0xFF;
		segment[1] = marker;
		BinaryPrimitives.WriteUInt16BigEndian (segment.AsSpan (2), (ushort)(payload.Length + 2));
		payload.CopyTo (segment, 4);
		return segment;
	}

	public static byte[] JpegApp (byte marker, string signature, string body) =>
		JpegSegment (marker, Encoding.ASCII.GetBytes (signature + body));

	/// <summary>A classic little-endian TIFF: IFD0 has the given entries, all LONG with one value.</summary>
	public static byte[] Tiff (params (int Tag, int Value)[] entries) => TiffChain ([entries]);

	/// <summary>A chain of IFDs linked by their next-IFD offsets, each with LONG entries.</summary>
	public static byte[] TiffChain (IReadOnlyList<(int Tag, int Value)[]> ifds)
	{
		var size = 8 + ifds.Sum (e => 2 + (12 * e.Length) + 4);
		var data = new byte[size];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (4), 8);
		var at = 8;
		for (var n = 0; n < ifds.Count; n++) {
			var entries = ifds[n];
			BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (at), (ushort)entries.Length);
			for (var e = 0; e < entries.Length; e++)
				WriteLong (data, at + 2 + (12 * e), entries[e].Tag, entries[e].Value);
			var next = at + 2 + (12 * entries.Length) + 4;
			BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (next - 4), n + 1 < ifds.Count ? next : 0);
			at = next;
		}

		return data;
	}

	public static void WriteLong (byte[] data, int at, int tag, int value)
	{
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (at), (ushort)tag);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (at + 2), 4);
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (at + 4), 1);
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (at + 8), value);
	}

	public static byte[] Zlib (byte[] raw) => Zlib (raw, CompressionLevel.Optimal);

	public static byte[] Zlib (byte[] raw, CompressionLevel level)
	{
		using var output = new MemoryStream ();
		using (var z = new ZLibStream (output, level, leaveOpen: true))
			z.Write (raw);
		return output.ToArray ();
	}

	/// <summary>A PNG text chunk around raw bytes: compressed (zTXt, or iTXt when <paramref name="itxt"/>) at a given level.</summary>
	public static byte[] CompressedChunk (string keyword, byte[] raw, CompressionLevel level, bool itxt) =>
		itxt
			? PngChunk ("iTXt", Concat (Encoding.Latin1.GetBytes (keyword), [0, 1, 0, 0, 0], Zlib (raw, level)))
			: PngChunk ("zTXt", Concat (Encoding.Latin1.GetBytes (keyword), [0, 0], Zlib (raw, level)));

	/// <summary>An uncompressed iTXt chunk around raw bytes, which need not be valid UTF-8.</summary>
	public static byte[] RawItxt (string keyword, byte[] raw) =>
		PngChunk ("iTXt", Concat (Encoding.Latin1.GetBytes (keyword), [0, 0, 0, 0, 0], raw));

	/// <summary>ImageMagick's raw profile with <paramref name="perLine"/> bytes per hex line, compressed at a given level.</summary>
	public static byte[] RawProfile (string name, byte[] profile, int perLine, CompressionLevel level)
	{
		var text = new StringBuilder ();
		text.Append ('\n').Append (name).Append ('\n').Append (profile.Length.ToString (System.Globalization.CultureInfo.InvariantCulture).PadLeft (8)).Append ('\n');
		for (var n = 0; n < profile.Length; n++) {
			text.Append (profile[n].ToString ("x2", System.Globalization.CultureInfo.InvariantCulture));
			if (n % perLine == perLine - 1 || n == profile.Length - 1)
				text.Append ('\n');
		}

		return CompressedChunk ("Raw profile type " + name, Encoding.ASCII.GetBytes (text.ToString ()), level, itxt: false);
	}

	public static byte[] Inflate (byte[] zlib)
	{
		using var input = new MemoryStream (zlib);
		using var z = new ZLibStream (input, CompressionMode.Decompress);
		using var output = new MemoryStream ();
		z.CopyTo (output);
		return output.ToArray ();
	}

	/// <summary>A GIF XMP application extension: the packet bytes, then the 258-byte magic trailer (XMP Part 3 §1.1.2).</summary>
	public static byte[] GifXmp (string packet)
	{
		var trailer = new byte[258];
		trailer[0] = 0x01;
		for (var n = 0; n < 256; n++)
			trailer[1 + n] = (byte)(0xFF - n);
		return Concat ([0x21, 0xFF, 11], "XMP DataXMP"u8.ToArray (), Encoding.UTF8.GetBytes (packet), trailer);
	}

	/// <summary>A GIF XMP application extension written as ordinary data sub-blocks, as ImageIO writes it.</summary>
	public static byte[] GifSubBlockXmp (string packet)
	{
		var bytes = Encoding.UTF8.GetBytes (packet);
		var blocks = new List<byte> ();
		for (var at = 0; at < bytes.Length; at += 255) {
			var take = Math.Min (255, bytes.Length - at);
			blocks.Add ((byte)take);
			blocks.AddRange (bytes.AsSpan (at, take).ToArray ());
		}

		return Concat ([0x21, 0xFF, 11], "XMP DataXMP"u8.ToArray (), [.. blocks], [0]);
	}

	public static byte[] JpegXmp (string packet) =>
		JpegSegment (0xE1, Concat ("http://ns.adobe.com/xap/1.0/\0"u8.ToArray (), Encoding.UTF8.GetBytes (packet)));

	public static byte[] JpegExif (byte[] tiff) => JpegSegment (0xE1, Concat ("Exif\0\0"u8.ToArray (), tiff));

	/// <summary>An Exif SRATIONAL (TIFF 6.0 type 10), big-endian.</summary>
	public static byte[] SRational (int numerator, int denominator)
	{
		var value = new byte[8];
		BinaryPrimitives.WriteInt32BigEndian (value, numerator);
		BinaryPrimitives.WriteInt32BigEndian (value.AsSpan (4), denominator);
		return value;
	}

	public static readonly byte[] AppleMakerNoteHeader = "Apple iOS\0\0\u0001MM"u8.ToArray ();

	/// <summary>
	/// An Apple maker note as iPhones write it: "Apple iOS", NUL, version 1, "MM", then a big-endian IFD whose
	/// value offsets count from the start of the maker note.
	/// </summary>
	public static byte[] AppleMakerNote (IReadOnlyList<(int Tag, int Type, int Count, byte[] Value)> entries, byte[]? header = null)
	{
		header ??= AppleMakerNoteHeader;
		var valuesAt = header.Length + 2 + (12 * entries.Count) + 4;
		var table = new byte[valuesAt];
		header.CopyTo (table, 0);
		BinaryPrimitives.WriteUInt16BigEndian (table.AsSpan (header.Length), (ushort)entries.Count);
		var values = new List<byte> ();
		for (var n = 0; n < entries.Count; n++) {
			var e = header.Length + 2 + (12 * n);
			var (tag, type, count, value) = entries[n];
			BinaryPrimitives.WriteUInt16BigEndian (table.AsSpan (e), (ushort)tag);
			BinaryPrimitives.WriteUInt16BigEndian (table.AsSpan (e + 2), (ushort)type);
			BinaryPrimitives.WriteInt32BigEndian (table.AsSpan (e + 4), count);
			if (value.Length <= 4) {
				value.CopyTo (table, e + 8);
			} else {
				BinaryPrimitives.WriteInt32BigEndian (table.AsSpan (e + 8), valuesAt + values.Count);
				values.AddRange (value);
			}
		}

		return Concat (table, [.. values]);
	}

	/// <summary>
	/// An Exif TIFF shaped like an iPhone's (big-endian unless asked): IFD0 with Make, Model, DateTime and the Exif
	/// and GPS IFD pointers, a GPS IFD with a latitude, and an Exif IFD with DateTimeOriginal and the maker note.
	/// </summary>
	public static byte[] AppleExifTiff (byte[] makerNote, int makerNoteType = 7, int? makerNoteCount = null, int? exifIfd = null, bool littleEndian = false)
	{
		static byte[] Ascii (string s) => Concat (Encoding.ASCII.GetBytes (s), [0]);
		const int Ifd0 = 8;
		const int Gps = Ifd0 + 2 + (12 * 5) + 4;
		const int Exif = Gps + 2 + (12 * 2) + 4;
		const int ValuesAt = Exif + 2 + (12 * 2) + 4;
		var data = new byte[ValuesAt];
		(littleEndian ? "II*\0"u8 : "MM\0*"u8).CopyTo (data);
		U32 (data.AsSpan (4), Ifd0);
		var values = new List<byte> ();
		void Ifd (int at, (int Tag, int Type, int Count, byte[] Value)[] entries)
		{
			U16 (data.AsSpan (at), entries.Length);
			for (var n = 0; n < entries.Length; n++) {
				var e = at + 2 + (12 * n);
				var (tag, type, count, value) = entries[n];
				U16 (data.AsSpan (e), tag);
				U16 (data.AsSpan (e + 2), type);
				U32 (data.AsSpan (e + 4), count);
				if (value.Length <= 4) {
					value.CopyTo (data, e + 8);
				} else {
					U32 (data.AsSpan (e + 8), ValuesAt + values.Count);
					values.AddRange (value);
					if (values.Count % 2 == 1)
						values.Add (0);
				}
			}
		}

		void U16 (Span<byte> at, int value)
		{
			if (littleEndian)
				BinaryPrimitives.WriteUInt16LittleEndian (at, (ushort)value);
			else
				BinaryPrimitives.WriteUInt16BigEndian (at, (ushort)value);
		}

		void U32 (Span<byte> at, int value)
		{
			if (littleEndian)
				BinaryPrimitives.WriteInt32LittleEndian (at, value);
			else
				BinaryPrimitives.WriteInt32BigEndian (at, value);
		}

		byte[] Long (int value)
		{
			var bytes = new byte[4];
			U32 (bytes, value);
			return bytes;
		}

		// RATIONAL values in the TIFF's byte order (the maker note keeps its own big-endian order).
		byte[] Rational (int numerator, int denominator)
		{
			var bytes = new byte[8];
			U32 (bytes, numerator);
			U32 (bytes.AsSpan (4), denominator);
			return bytes;
		}

		Ifd (Ifd0, [
			(0x010F, 2, 6, Ascii ("Apple")),
			(0x0110, 2, 15, Ascii ("iPhone 13 Jane")),
			(0x0132, 2, 20, Ascii ("2024:05:01 10:00:00")),
			(0x8769, 4, 1, Long (exifIfd ?? Exif)),
			(0x8825, 4, 1, Long (Gps))]);
		Ifd (Gps, [(0x0001, 2, 2, Ascii ("N")), (0x0002, 5, 3, Concat (Rational (47, 1), Rational (30, 1), Rational (0, 1)))]);
		Ifd (Exif, [(0x9003, 2, 20, Ascii ("2024:05:01 10:00:00")), (0x927C, makerNoteType, makerNoteCount ?? makerNote.Length, makerNote)]);
		return Concat (data, [.. values]);
	}

	/// <summary>An APP11 JUMBF segment (ISO/IEC 19566-5): "JP", box instance En, sequence Z, then the box bytes.</summary>
	public static byte[] Jumbf (int en, int z, byte[] box)
	{
		var head = new byte[8];
		head[0] = (byte)'J';
		head[1] = (byte)'P';
		BinaryPrimitives.WriteUInt16BigEndian (head.AsSpan (2), (ushort)en);
		BinaryPrimitives.WriteInt32BigEndian (head.AsSpan (4), z);
		return JpegSegment (0xEB, Concat (head, box));
	}

	public static byte[] Box (string type, byte[] payload)
	{
		var box = new byte[8 + payload.Length];
		BinaryPrimitives.WriteInt32BigEndian (box, box.Length);
		Encoding.ASCII.GetBytes (type).CopyTo (box, 4);
		payload.CopyTo (box, 8);
		return box;
	}

	/// <summary>A JUMBF superbox (ISO/IEC 19566-5) whose description box has the given content type, holding one box.</summary>
	public static byte[] JumbfStore (byte[] type, string label, byte[] content) =>
		Box ("jumb", Concat (Box ("jumd", Concat (type, [3], Encoding.ASCII.GetBytes (label + "\0"))), content));

	public static readonly byte[] JsonUuid = [0x6A, 0x73, 0x6F, 0x6E, 0x00, 0x11, 0x00, 0x10, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];
	public static readonly byte[] XmlUuid = [0x78, 0x6D, 0x6C, 0x20, 0x00, 0x11, 0x00, 0x10, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

	public static readonly byte[] C2paUuid = [0x63, 0x32, 0x70, 0x61, 0x00, 0x11, 0x00, 0x10, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

	/// <summary>A C2PA manifest store: a jumb superbox whose jumd has the C2PA type, holding a JSON assertion.</summary>
	public static byte[] C2paStore (byte[]? type = null) =>
		Box ("jumb", Concat (
			Box ("jumd", Concat (type ?? C2paUuid, [3], "c2pa\0"u8.ToArray ())),
			Box ("json", """{"dc:creator":"Jane Private","exif:GPSLatitude":"47,30.0N"}"""u8.ToArray ())));

	/// <summary>
	/// A two-image MPF file (CIPA DC-007): the first image's APP2 MPF index points at the second image after
	/// its EOI. <paramref name="between"/> sits between the MPF segment and the frame.
	/// </summary>
	public static byte[] MpfJpeg (byte[] before, byte[] between, out byte[] second) =>
		MpfJpeg (before, between, Jpeg (JpegSegment (0xFE, "second image"u8.ToArray ())), out second);

	public static byte[] MpfJpeg (byte[] before, byte[] between, byte[] secondImage, out byte[] second)
	{
		second = secondImage;
		return MpfJpegMany (before, between, secondImage);
	}

	/// <summary>A multi-picture file whose first image's MPF index lists <paramref name="later"/> after its EOI.</summary>
	public static byte[] MpfJpegMany (byte[] before, byte[] between, params byte[][] later)
	{
		var count = later.Length + 1;
		// MP header (big-endian TIFF), IFD with one entry: MPEntry (0xB002), UNDEFINED, 16 bytes per image at offset 26.
		var mp = new byte[8 + 2 + 12 + 4 + (16 * count)];
		"MM\0*"u8.CopyTo (mp);
		BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (4), 8);
		BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (8), 1);
		BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (10), 0xB002);
		BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (12), 7);
		BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (14), 16 * count);
		BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (18), 26);
		var segment = JpegSegment (0xE2, Concat ("MPF\0"u8.ToArray (), mp));
		var first = Concat ([0xFF, 0xD8], before, segment, between,
			JpegSegment (0xC0, [0x08, 0x00, 0x03, 0x00, 0x02, 0x01, 0x01, 0x11, 0x00]),
			JpegSegment (0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]), [0x12, 0x34], [0xFF, 0xD9]);
		var header = 2 + before.Length + 4 + 4;
		var entries = header + 26;
		BinaryPrimitives.WriteInt32BigEndian (first.AsSpan (entries + 4), first.Length);
		var at = first.Length;
		for (var n = 0; n < later.Length; n++) {
			var entry = entries + (16 * (n + 1));
			BinaryPrimitives.WriteInt32BigEndian (first.AsSpan (entry + 4), later[n].Length);
			BinaryPrimitives.WriteInt32BigEndian (first.AsSpan (entry + 8), at - header);
			at += later[n].Length;
		}

		return Concat ([first, .. later]);
	}

	/// <summary>Writes MP entry <paramref name="n"/>'s size and offset (relative to the MP header).</summary>
	public static void SetMpEntry (byte[] jpeg, int n, int size, int offset)
	{
		var entry = jpeg.AsSpan ().IndexOf ("MPF\0"u8) + 4 + 26 + (16 * n);
		BinaryPrimitives.WriteInt32BigEndian (jpeg.AsSpan (entry + 4), size);
		BinaryPrimitives.WriteInt32BigEndian (jpeg.AsSpan (entry + 8), offset);
	}

	public static (int Size, int Offset) GetMpEntry (byte[] jpeg, int n)
	{
		var entry = jpeg.AsSpan ().IndexOf ("MPF\0"u8) + 4 + 26 + (16 * n);
		return (BinaryPrimitives.ReadInt32BigEndian (jpeg.AsSpan (entry + 4)), BinaryPrimitives.ReadInt32BigEndian (jpeg.AsSpan (entry + 8)));
	}

	/// <summary>The MP entries of <paramref name="jpeg"/>: each image's absolute start and its size.</summary>
	public static List<(int Start, int Size)> MpfEntries (byte[] jpeg)
	{
		var header = jpeg.AsSpan ().IndexOf ("MPF\0"u8) + 4;
		var entries = header + 26;
		var list = new List<(int, int)> ();
		for (var n = 0; n < 2; n++) {
			var size = BinaryPrimitives.ReadInt32BigEndian (jpeg.AsSpan (entries + (16 * n) + 4));
			var offset = BinaryPrimitives.ReadInt32BigEndian (jpeg.AsSpan (entries + (16 * n) + 8));
			list.Add ((offset == 0 ? 0 : header + offset, size));
		}

		return list;
	}

	/// <summary>Where the MPF index of <paramref name="jpeg"/> says the second image starts.</summary>
	public static int MpfSecondImage (byte[] jpeg)
	{
		var at = jpeg.AsSpan ().IndexOf ("MPF\0"u8);
		var header = at + 4;
		var entries = header + 26;
		return header + BinaryPrimitives.ReadInt32BigEndian (jpeg.AsSpan (entries + 16 + 8));
	}

	public static StillImageFile Open (byte[] data, string name)
	{
		var result = MediaFile.ReadFromData (data, name);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return (StillImageFile)result.File!;
	}

	// The XMP namespaces the tests read packets with.
	public static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
	public static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
	public static readonly XNamespace Exif = "http://ns.adobe.com/exif/1.0/";
	public static readonly XNamespace Hdrgm = "http://ns.adobe.com/hdr-gain-map/1.0/";
	public static readonly XNamespace Container = "http://ns.google.com/photos/1.0/container/";
	public static readonly XNamespace Item = "http://ns.google.com/photos/1.0/container/item/";
	public static readonly XNamespace AppleGainMap = "http://ns.apple.com/HDRGainMap/1.0/";

	/// <summary>A PNG IHDR payload (PNG 3rd ed. §11.2.1).</summary>
	public static byte[] PngHeader (int width, int height, byte depth, byte color)
	{
		var ihdr = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian (ihdr, width);
		BinaryPrimitives.WriteInt32BigEndian (ihdr.AsSpan (4), height);
		ihdr[8] = depth;
		ihdr[9] = color;
		return ihdr;
	}

	/// <summary>The signature and an IHDR chunk, and nothing else.</summary>
	public static byte[] PngIhdr (int width, int height, byte depth, byte color) =>
		Concat (PngSignature, PngChunk ("IHDR", PngHeader (width, height, depth, color)));

	/// <summary>The signature, then an IHDR chunk without its CRC: all the size reader looks at.</summary>
	public static byte[] PngIhdrWithoutCrc (int width, int height, byte depth, byte color) =>
		Concat (PngSignature, [0, 0, 0, 13], "IHDR"u8.ToArray (), PngHeader (width, height, depth, color));

	/// <summary>A 1x1 grayscale PNG with the given chunks between IHDR and IEND, and no IDAT.</summary>
	public static byte[] MinimalPng (params byte[][] chunks) =>
		Concat (PngSignature, PngChunk ("IHDR", PngHeader (1, 1, 8, 0)), Concat (chunks), PngChunk ("IEND", []));

	/// <summary>A 1x1 RGB PNG with a tEXt Title.</summary>
	public static byte[] TitledPng (string title) =>
		Concat (PngSignature, PngChunk ("IHDR", PngHeader (1, 1, 8, 2)), Text ("Title", title), PngChunk ("IEND", []));

	/// <summary>A GIF89a header and logical screen descriptor (GIF89a §18) and nothing else.</summary>
	public static byte[] GifScreen (int width, int height, byte flags = 0x80 | (7 << 4) | 7)
	{
		var data = new byte[13];
		"GIF89a"u8.CopyTo (data);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (6), (ushort)width);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (8), (ushort)height);
		data[10] = flags;
		return data;
	}

	/// <summary>A BMP file header and BITMAPINFOHEADER, 24 bits per pixel.</summary>
	public static byte[] Bmp (int width, int height, int length = 54)
	{
		var data = new byte[length];
		"BM"u8.CopyTo (data);
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (14), 40);
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (18), width);
		BinaryPrimitives.WriteInt32LittleEndian (data.AsSpan (22), height);
		data[28] = 24;
		return data;
	}

	/// <summary>A 128-byte PCX header: version 5, RLE, 8 bits, the window, and the colour planes.</summary>
	public static byte[] Pcx (int xmin, int ymin, int xmax, int ymax, byte planes = 3)
	{
		var data = new byte[128];
		data[0] = 0x0A;
		data[1] = 5;
		data[2] = 1;
		data[3] = 8;
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (4), (ushort)xmin);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (6), (ushort)ymin);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (8), (ushort)xmax);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (10), (ushort)ymax);
		data[65] = planes;
		return data;
	}

	/// <summary>A TIFF whose IFD0 holds LONG ImageWidth and ImageLength (and the DNG version tag).</summary>
	public static byte[] TiffImage (int width, int height, bool littleEndian = true, bool dng = false)
	{
		(int, int, long, long)[] entries = dng ? [(256, 4, 1, width), (257, 4, 1, height), (0xC612, 4, 1, 1)] : [(256, 4, 1, width), (257, 4, 1, height)];
		return new TiffBuilder (8 + 2 + (12 * entries.Length) + 4, bigEndian: !littleEndian).Header (8).Table (8, entries).ToArray ();
	}

	/// <summary>A BigTIFF whose IFD0 holds SHORT ImageWidth and ImageLength.</summary>
	public static byte[] BigTiff (int width, int height, bool littleEndian) =>
		new TiffBuilder (72, bigEndian: !littleEndian, big: true).Header (16)
			.Table (16, [(256, 3, 1, 0), (257, 3, 1, 0)]).U16 (24 + 12, width).U16 (44 + 12, height).ToArray ();

	/// <summary>The data of the first PNG chunk of a type, or empty.</summary>
	public static byte[] PngPayload (byte[] file, string type) =>
		PngChunkAt (file, (chunkType, _) => chunkType == type) is var (at, length) && at >= 0 ? file[(at + 8)..(at + 8 + length)] : [];

	public static bool HasPngChunk (byte[] file, string type)
	{
		var i = 8;
		while (i + 8 <= file.Length) {
			if (Encoding.ASCII.GetString (file, i + 4, 4) == type)
				return true;
			i += 12 + BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (i));
		}

		return false;
	}

	/// <summary>The whole first text chunk of a type with a keyword, or empty.</summary>
	public static byte[] PngTextChunk (byte[] file, string type, string keyword)
	{
		var key = Encoding.ASCII.GetBytes (keyword);
		var (at, length) = PngChunkAt (file, (chunkType, at) => chunkType == type && BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (at)) >= key.Length + 1
			&& file.AsSpan (at + 8, key.Length).SequenceEqual (key) && file[at + 8 + key.Length] == 0);
		return at < 0 ? [] : file[at..(at + 12 + length)];
	}

	// The first chunk the test matches, walking lengths without checking them: (-1, 0) when there is none.
	static (int At, int Length) PngChunkAt (byte[] file, Func<string, int, bool> matches)
	{
		var i = 8;
		while (i + 12 <= file.Length) {
			var length = BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (i));
			if (matches (Encoding.ASCII.GetString (file, i + 4, 4), i))
				return (i, length);
			i += 12 + length;
		}

		return (-1, 0);
	}

	/// <summary>The first byte equal to <paramref name="first"/> followed by <paramref name="second"/> (any byte when negative).</summary>
	public static int IndexOf (byte[] data, byte first, int second)
	{
		for (var i = 0; i + 1 < data.Length; i++) {
			if (data[i] == first && (second < 0 || data[i + 1] == second))
				return i;
		}

		return -1;
	}

	/// <summary>The data of the first sub-block of a GIF's last image.</summary>
	public static byte[] GifImageSubBlock (byte[] gif)
	{
		var i = gif.Length - 1;
		while (i > 0 && gif[i] != 0x2C)
			i--;
		i += 11;
		var size = gif[i++];
		return gif[i..(i + size)];
	}

	public static byte[] Concat (params byte[][] parts)
	{
		var data = new byte[parts.Sum (p => p.Length)];
		var i = 0;
		foreach (var part in parts) {
			part.CopyTo (data, i);
			i += part.Length;
		}

		return data;
	}

	internal const int Mib = 1024 * 1024;

	// The runtime itself can allocate about 1 KB on the thread (tiering, net8.0). The regressions these pin cost
	// 49 KB (TIFF scratch) to 131 KB (a decoded 64 KB markup window).
	internal const int Noise = 4 * 1024;

	internal static byte[] GifWithComment (string comment, byte[] trap)
	{
		var text = Encoding.UTF8.GetBytes (comment);
		var data = new byte[13 + 2 + 1 + text.Length + 1 + 10 + 1 + 1 + trap.Length + 1 + 1];
		Encoding.ASCII.GetBytes ("GIF89a").CopyTo (data, 0);
		data[6] = 1;
		data[8] = 1;
		var i = 13;
		data[i++] = 0x21;
		data[i++] = 0xFE;
		data[i++] = (byte)text.Length;
		text.CopyTo (data, i);
		i += text.Length;
		data[i++] = 0;
		data[i++] = 0x2C;
		data[i + 4] = 1;
		data[i + 6] = 1;
		i += 9;
		data[i++] = 2;
		data[i++] = (byte)trap.Length;
		trap.CopyTo (data, i);
		i += trap.Length;
		data[i++] = 0;
		data[i] = 0x3B;
		return data;
	}
}
