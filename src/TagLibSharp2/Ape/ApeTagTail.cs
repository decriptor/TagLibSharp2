// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Ape;

/// <summary>
/// Reads an APEv2 tag from the end of a seekable stream without the bytes in front of it.
/// </summary>
internal static class ApeTagTail
{
	public static bool TryRead (Stream stream, out byte[] tag, out long offset)
	{
		tag = [];
		offset = stream.Length;
		if (stream.Length < ApeTagFooter.Size)
			return false;

		stream.Position = stream.Length - ApeTagFooter.Size;
		var footerBytes = StreamBytes.ReadAtMost (stream, ApeTagFooter.Size);
		var parsed = ApeTagFooter.Parse (footerBytes);
		if (!parsed.IsSuccess || parsed.Footer is not { } footer || footer.IsHeader)
			return false;

		var header = (footer.Flags & 0x80000000) != 0 ? ApeTagFooter.Size : 0;
		var total = (long)footer.TagSize + header;
		if (total < ApeTagFooter.Size || total > stream.Length || total > int.MaxValue)
			return false;

		offset = stream.Length - total;
		stream.Position = offset;
		tag = StreamBytes.ReadAtMost (stream, (int)total);
		return tag.Length == total;
	}
}
