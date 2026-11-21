// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Ogg;

/// <summary>Which header packets a windowed Ogg read keeps.</summary>
enum OggHeaderKind
{
	Vorbis,
	Opus,
	Flac
}

/// <summary>
/// Reads Ogg header pages and, when duration needs it, the last page.
/// Audio pages in between are not copied.
/// </summary>
internal static class OggWindow
{
	public const int MaxPageBytes = 27 + 255 + (255 * 255);

	readonly struct PageHeader
	{
		public PageHeader (byte[] prefix, int bodySize, bool continued, byte[] lacing)
		{
			Prefix = prefix;
			BodySize = bodySize;
			Continued = continued;
			Lacing = lacing;
		}

		public byte[] Prefix { get; }

		public int BodySize { get; }

		public bool Continued { get; }

		public byte[] Lacing { get; }
	}

	public static byte[] ReadHeaders (Stream stream, OggHeaderKind kind)
	{
		if (stream.Position != 0)
			stream.Position = 0;

		using var kept = new MemoryStream ();
		var complete = 0;
		var open = false;
		while (stream.Position < stream.Length) {
			var pageAt = stream.Position;
			if (!TryReadHeader (stream, out var header))
				break;

			var bodyAt = stream.Position;
			if (ShouldStop (stream, header, kind, complete, open)) {
				stream.Position = pageAt;
				break;
			}

			stream.Position = bodyAt;
			var body = StreamBytes.ReadAtMost (stream, header.BodySize);
			if (body.Length != header.BodySize)
				break;

			StreamBytes.Append (kept, header.Prefix);
			StreamBytes.Append (kept, body);
			CountPackets (header, ref complete, ref open);
		}

		return kept.ToArray ();
	}

	/// <summary>
	/// The Ogg page that ends at EOF, or empty when the tail is not a page.
	/// At most one maximum page is read.
	/// </summary>
	public static byte[] ReadLastPage (Stream stream)
	{
		if (stream.Length < 27)
			return [];

		var window = (int)Math.Min (stream.Length, MaxPageBytes);
		stream.Position = stream.Length - window;
		var tail = StreamBytes.ReadAtMost (stream, window);
		for (var i = tail.Length - 27; i >= 0; i--) {
			if (tail[i] != (byte)'O' || tail[i + 1] != (byte)'g' || tail[i + 2] != (byte)'g' || tail[i + 3] != (byte)'S')
				continue;
			if (!TryDeclaredPageLength (tail, i, out var length, out var complete))
				continue;

			// The rightmost header is the last page. A cut-off body still has its granule.
			if (!complete)
				return tail.AsSpan (i).ToArray ();
			return tail.AsSpan (i, length).ToArray ();
		}

		return [];
	}

	static bool TryDeclaredPageLength (byte[] data, int offset, out int length, out bool complete)
	{
		length = 0;
		complete = false;
		if (offset + 27 > data.Length || data[offset + 4] != 0)
			return false;

		var segments = data[offset + 26];
		if (offset + 27 + segments > data.Length)
			return false;

		var body = 0;
		for (var i = 0; i < segments; i++)
			body += data[offset + 27 + i];
		length = 27 + segments + body;
		complete = offset + length <= data.Length;
		return true;
	}

	static bool ShouldStop (Stream stream, PageHeader header, OggHeaderKind kind, int complete, bool open)
	{
		if (header.Continued || open)
			return false;
		if (kind == OggHeaderKind.Opus)
			return complete >= 2;
		if (kind == OggHeaderKind.Vorbis && complete < 2)
			return false;
		if (header.BodySize <= 0)
			return kind == OggHeaderKind.Vorbis;

		var one = StreamBytes.ReadAtMost (stream, 1);
		if (one.Length == 0)
			return true;
		if (kind == OggHeaderKind.Flac)
			return one[0] == 0xFF;
		return one[0] is not (1 or 3 or 5);
	}

	static void CountPackets (PageHeader header, ref int complete, ref bool open)
	{
		var index = 0;
		if (header.Continued) {
			if (!open || header.Lacing.Length == 0)
				return;
			if (header.Lacing[0] < 255) {
				complete++;
				open = false;
			}

			index = 1;
		} else if (open) {
			// Only an empty segment table keeps this clear. Lacing below sets it again.
			open = false;
		}

		for (; index < header.Lacing.Length; index++) {
			if (header.Lacing[index] < 255) {
				complete++;
				open = false;
			} else {
				open = true;
			}
		}
	}

	static bool TryReadHeader (Stream stream, out PageHeader header)
	{
		header = default;
		var start = stream.Position;
		var head = StreamBytes.ReadAtMost (stream, 27);
		if (head.Length < 27 || head[0] != (byte)'O' || head[1] != (byte)'g' || head[2] != (byte)'g' || head[3] != (byte)'S' || head[4] != 0) {
			stream.Position = start;
			return false;
		}

		var segments = head[26];
		var table = StreamBytes.ReadAtMost (stream, segments);
		if (table.Length != segments) {
			stream.Position = start;
			return false;
		}

		var body = 0;
		for (var i = 0; i < table.Length; i++)
			body += table[i];

		var prefix = new byte[27 + segments];
		head.CopyTo (prefix, 0);
		table.CopyTo (prefix, 27);
		header = new PageHeader (prefix, body, (head[5] & 0x01) != 0, table);
		return true;
	}
}
