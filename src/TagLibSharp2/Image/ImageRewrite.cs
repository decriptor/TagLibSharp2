// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Jpeg;
using TagLibSharp2.Image.Png;

namespace TagLibSharp2.Image;

/// <summary>
/// Reads and splices the text blocks an image format already defines: PNG tEXt, zTXt and iTXt, the GIF comment
/// extension, and the JPEG COM marker before the scan. Pixel bytes are copied through.
/// </summary>
static class ImageRewrite
{
	internal static ImageTag? CreateTag (ReadOnlySpan<byte> data, MediaFormat format)
	{
		var stores = format switch {
			MediaFormat.Png => ImageStores.Png,
			MediaFormat.Gif or MediaFormat.Jpeg => ImageStores.CommentOnly,
			_ => ImageStores.None
		};
		if (stores == ImageStores.None)
			return null;

		var tag = new ImageTag (format, stores);
		if (format == MediaFormat.Png)
			PngRewriter.Read (data, tag);
		else if (format == MediaFormat.Gif)
			GifRewriter.Read (data, tag);
		else
			JpegRewriter.Read (data, tag);
		return tag;
	}

	internal static byte[] RenderTag (ImageTag tag)
	{
		using var builder = new BinaryDataBuilder ();
		if (tag.Format == MediaFormat.Png)
			PngRewriter.AddFields (builder, tag, onlyChanged: false);
		else if (tag.Field (ImageStores.Comment) is string comment) {
			if (tag.Format == MediaFormat.Gif)
				GifRewriter.AddComment (builder, comment);
			else
				builder.Add (JpegRewriter.EncodeComment (comment));
		}

		return builder.ToArray ();
	}

	/// <summary>
	/// Splices the edited text into <paramref name="data"/>. Every byte the edit does not concern is copied,
	/// including anything after the last chunk, the GIF trailer, or the JPEG EOI.
	/// </summary>
	/// <returns>The new file, or null with <paramref name="error"/> naming why the container cannot be updated.</returns>
	internal static byte[]? Rewrite (ReadOnlySpan<byte> data, ImageTag tag, out string? error)
	{
		error = null;
		if (!tag.AffectsFile)
			return null;
		var rewritten = tag.Format switch {
			MediaFormat.Png => PngRewriter.Rewrite (data, tag, out error),
			MediaFormat.Gif => GifRewriter.Rewrite (data, tag, out error),
			MediaFormat.Jpeg => JpegRewriter.Rewrite (data, tag, out error),
			_ => null
		};
		if (rewritten is null && error is null)
			error = tag.Format switch {
				MediaFormat.Gif => "The GIF block list is truncated or holds an unknown block, so the comment cannot be spliced in.",
				MediaFormat.Jpeg => "The JPEG marker list before the scan is truncated or malformed, so the comment cannot be spliced in.",
				_ => "The image container could not be updated."
			};
		return rewritten;
	}
}
