// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Ape;

/// <summary>
/// Reads an APEv2 tag from the end of a seekable stream without the bytes in front of it.
/// A footer at the very end is used. When the file ends with ID3v1, the footer 128 bytes earlier is used.
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

		if (stream.Length < ApeTagLocation.Id3v1Size + ApeTagFooter.Size || !EndsWithId3v1 (stream))
			return false;

		return TryReadEndingAt (stream, stream.Length - ApeTagLocation.Id3v1Size, out tag, out offset);
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

	static bool EndsWithId3v1 (Stream stream)
	{
		if (stream.Length < ApeTagLocation.Id3v1Size)
			return false;

		stream.Position = stream.Length - ApeTagLocation.Id3v1Size;
		var marker = StreamBytes.ReadAtMost (stream, 3);
		return marker.Length == 3
			&& marker[0] == (byte)'T'
			&& marker[1] == (byte)'A'
			&& marker[2] == (byte)'G';
	}
}
