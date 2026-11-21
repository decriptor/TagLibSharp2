// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Ape;

/// <summary>
/// Reads an APEv2 tag from the end of a seekable stream without the audio in front of it.
/// Placement matches <see cref="ApeTagLocation.Locate"/>: a footer at EOF, a footer before
/// Lyrics3 v1 or v2, or the 32 bytes at EOF-160 when no Lyrics3 block is present.
/// </summary>
internal static class ApeTagTail
{
	public static bool TryRead (Stream stream, out byte[] tag, out long offset)
	{
		tag = [];
		offset = stream.Length;
		if (stream.Length < ApeTagFooter.Size)
			return false;

		if (TryReadEndingAt (stream, stream.Length, out tag, out offset))
			return true;

		if (!ApeTagLocation.TryTagEnd (stream, out var tagEnd))
			return false;

		return TryReadEndingAt (stream, tagEnd, out tag, out offset);
	}

	static bool TryReadEndingAt (Stream stream, long end, out byte[] tag, out long offset)
	{
		tag = [];
		offset = stream.Length;
		if (end < ApeTagFooter.Size || end > stream.Length)
			return false;

		stream.Position = end - ApeTagFooter.Size;
		var footerBytes = StreamBytes.ReadAtMost (stream, ApeTagFooter.Size);
		var parsed = ApeTagFooter.Parse (footerBytes);
		if (!parsed.IsSuccess || parsed.Footer is not { } footer || footer.IsHeader)
			return false;

		var header = (footer.Flags & 0x80000000) != 0 ? ApeTagFooter.Size : 0;
		var total = (long)footer.TagSize + header;
		if (total < ApeTagFooter.Size || total > end || total > int.MaxValue)
			return false;

		offset = end - total;
		stream.Position = offset;
		tag = StreamBytes.ReadAtMost (stream, (int)total);
		return tag.Length == total;
	}
}
