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

	/// <summary>A plan for a format whose path save copies original ranges. False when the container cannot be updated.</summary>
	internal static bool TryPlan (
		ReadOnlySpan<byte> data,
		MediaFormat format,
		ImageTag? tag,
		out ImagePlan? plan,
		out string? error)
	{
		error = null;
		if (format == MediaFormat.Png)
			return TryEditedPlan (data, tag, PngRewriter.Plan, PngRewriter.SpliceError, out plan, out error);
		if (format == MediaFormat.Gif)
			return TryEditedPlan (data, tag, GifRewriter.Plan, GifRewriter.SpliceError, out plan, out error);
		if (format == MediaFormat.Jpeg)
			return TryEditedPlan (data, tag, JpegRewriter.Plan, JpegRewriter.SpliceError, out plan, out error);
		if (CopiesWholeFile (format)) {
			plan = ImagePlan.Whole (data.Length);
			return true;
		}

		plan = null;
		error = "The image container could not be updated.";
		return false;
	}

	/// <summary>
	/// An unchanged file is one range of the original. An edit is that format's plan, or its splice error.
	/// </summary>
	static bool TryEditedPlan (
		ReadOnlySpan<byte> data,
		ImageTag? tag,
		RewriterPlan build,
		string spliceError,
		out ImagePlan? plan,
		out string? error)
	{
		error = null;
		if (tag is null || !tag.AffectsFile) {
			plan = ImagePlan.Whole (data.Length);
			return true;
		}

		plan = build (data, tag, out error);
		if (plan is null && error is null)
			error = spliceError;
		return plan is not null;
	}

	delegate ImagePlan? RewriterPlan (ReadOnlySpan<byte> data, ImageTag tag, out string? error);

	/// <summary>These formats have no metadata rewriter, so the plan is the original file.</summary>
	static bool CopiesWholeFile (MediaFormat format) =>
		format is MediaFormat.Tiff or MediaFormat.Dng
			or MediaFormat.Bmp or MediaFormat.Pcx
			or MediaFormat.Netpbm or MediaFormat.Svg;
}
