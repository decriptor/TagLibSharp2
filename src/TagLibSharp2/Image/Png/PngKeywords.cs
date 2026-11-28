// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Png;

/// <summary>
/// The PNG text keywords of the tag's fields (PNG 3rd ed. §11.3.3.2 predefined keywords), and the keywords other
/// writers copy a field into: exiftool's "Artist", and ImageMagick's <c>exif:*</c> text.
/// </summary>
static class PngKeywords
{
	/// <summary>exiftool writes its Artist tag to a PNG as this keyword; "Author" wins when both are present.</summary>
	internal const string ExiftoolArtist = "Artist";

	public static ImageStores FieldFor (string keyword) => keyword switch {
		"Title" => ImageStores.Title,
		"Author" => ImageStores.Artist,
		"Description" => ImageStores.Description,
		"Copyright" => ImageStores.Copyright,
		"Creation Time" => ImageStores.Year,
		"Comment" => ImageStores.Comment,
		_ => ImageStores.None
	};

	public static string Keyword (ImageStores field) => field switch {
		ImageStores.Artist => "Author",
		ImageStores.Description => "Description",
		ImageStores.Copyright => "Copyright",
		ImageStores.Year => "Creation Time",
		ImageStores.Comment => "Comment",
		_ => "Title"
	};

	/// <summary>A text keyword that copies a changed field (ImageMagick writes these as plain tEXt).</summary>
	public static bool IsMirror (string keyword, ImageTag tag)
	{
		foreach (var field in MirrorFields.All) {
			if (!tag.Changed (field.Store))
				continue;
			foreach (var text in field.Text) {
				if (string.Equals (text, keyword, StringComparison.Ordinal))
					return true;
			}
		}

		return false;
	}
}
