// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image;

/// <summary>
/// The fields an image tag stores whose copies other blocks of the same file hold, and where each copy is. macOS
/// ImageIO shows the XMP (<c>dc:title</c>, <c>dc:creator</c>, <c>dc:description</c>, <c>dc:rights</c>) and the Exif
/// IFD0 values (<c>ImageDescription</c>, <c>Artist</c>, <c>Copyright</c>) in preference to PNG text, so an edit that
/// left them would not show there.
/// </summary>
/// <remarks>
/// XMP: Adobe XMP Specification Part 1 §7.9 (language alternatives, rdf:Alt with <c>x-default</c>) and §8.2.2.4
/// (Dublin Core: title, description and rights are Lang Alt, creator is an ordered array). Exif: CIPA DC-008 (Exif
/// 2.32) §4.6.4 Table 4 (IFD0 tags 0x010E, 0x013B, 0x8298) and §4.6.5 (UserComment 0x9286 in the Exif IFD, reached
/// through tag 0x8769). The Windows XP tags 0x9C9B-0x9C9D are read by Windows and exiftool as title, comment and
/// author. Text: the keywords exiftool and ImageMagick write for the same fields.
/// </remarks>
static class MirrorFields
{
	internal const string DcNamespace = "http://purl.org/dc/elements/1.1/";
	internal const string ExifNamespace = "http://ns.adobe.com/exif/1.0/";

	internal static readonly MirrorField[] All = [
		new (ImageStores.Title, "Title", DcNamespace, "title", ordered: false, ifd0: [0x9C9B], exifIfd: [], text: []),
		new (ImageStores.Artist, "Artist", DcNamespace, "creator", ordered: true, ifd0: [0x013B, 0x9C9D], exifIfd: [], text: ["exif:Artist", "Artist"]),
		new (ImageStores.Description, "Description", DcNamespace, "description", ordered: false, ifd0: [0x010E], exifIfd: [], text: ["exif:ImageDescription"]),
		new (ImageStores.Copyright, "Copyright", DcNamespace, "rights", ordered: false, ifd0: [0x8298], exifIfd: [], text: ["exif:Copyright"]),
		new (ImageStores.Comment, "Comment", ExifNamespace, "UserComment", ordered: false, ifd0: [0x9C9C], exifIfd: [0x9286], text: ["exif:UserComment"]),
	];

	internal static bool AnyChanged (ImageTag tag)
	{
		foreach (var field in All) {
			if (tag.Changed (field.Store))
				return true;
		}

		return false;
	}

	internal sealed class MirrorField (ImageStores store, string name, string xmpNamespace, string xmpName, bool ordered, int[] ifd0, int[] exifIfd, string[] text)
	{
		public ImageStores Store { get; } = store;
		public string Name { get; } = name;
		public string XmpNamespace { get; } = xmpNamespace;
		public string XmpName { get; } = xmpName;
		public bool Ordered { get; } = ordered;
		public int[] Ifd0 { get; } = ifd0;
		public int[] ExifIfd { get; } = exifIfd;
		public string[] Text { get; } = text;
	}
}
