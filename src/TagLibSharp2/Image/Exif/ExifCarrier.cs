// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// A block that holds an Exif TIFF: a JPEG APP1 segment, a PNG eXIf chunk, or an ImageMagick raw EXIF profile.
/// Each may start with the "Exif\0\0" signature (CIPA DC-008 §4.7.2); the edited TIFF is wrapped back the way the
/// block stores it, so <see cref="ExifEditor"/> never depends on the format.
/// </summary>
/// <param name="name">Names the block in a refusal, such as "eXIf".</param>
/// <param name="wrap">Builds the block's new bytes around the edited TIFF (with its signature, when it had one).</param>
sealed class ExifCarrier (string name, Func<byte[], byte[]> wrap)
{
	public string Name { get; } = name;

	/// <summary>Removes the changed fields' copies from the TIFF in <paramref name="block"/>.</summary>
	/// <param name="block">The Exif bytes, with or without the signature.</param>
	/// <param name="tag">The tag whose changed fields are removed.</param>
	/// <param name="rewritten">The block's new bytes, or null when nothing changed.</param>
	/// <param name="error">Why the copy cannot be updated.</param>
	public bool TryEdit (ReadOnlySpan<byte> block, ImageTag tag, out byte[]? rewritten, out string? error)
	{
		rewritten = null;
		var exif = block.ToArray ();
		var start = block.StartsWith ("Exif\0\0"u8) ? 6 : 0;
		if (!ExifEditor.TryRemove (exif, start, tag, Name, out var changed, out error))
			return false;
		if (changed)
			rewritten = wrap (exif);
		return true;
	}
}
