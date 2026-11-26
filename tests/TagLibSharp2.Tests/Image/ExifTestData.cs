// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Image.Xmp;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>The Exif inputs, carriers and reader-visible checks the Exif tests share.</summary>
static class ExifTestData
{
	// The carriers of an Exif TIFF: a JPEG APP1 segment, a PNG eXIf chunk, and ImageMagick's raw EXIF profile in PNG
	// text (with its "Exif\0\0" signature); each file also holds a comment of its own (COM, or tEXt).
	internal static byte[] InCarrier (string carrier, byte[] tiff) => carrier switch {
		"jpeg" or "jpeg APP1" => Jpeg (JpegExif (tiff), JpegSegment (0xFE, "c"u8.ToArray ())),
		"png" or "png eXIf" => Png (PngChunk ("eXIf", tiff), Text ("Comment", "c")),
		_ => Png (RawProfile ("exif", Concat ("Exif\0\0"u8.ToArray (), tiff)), Text ("Comment", "c")),
	};

	/// <summary>The Exif TIFF a file holds in a carrier (without the "Exif\0\0" signature), or null when it holds none.</summary>
	internal static byte[]? ExifIn (string carrier, byte[] file)
	{
		if (carrier.StartsWith ("jpeg", StringComparison.Ordinal)) {
			var at = file.AsSpan ().IndexOf ("Exif\0\0"u8);
			if (at < 0)
				return null;
			var length = (file[at - 2] << 8) | file[at - 1];
			return file[(at + 6)..(at - 2 + length)];
		}

		var raw = carrier.Contains ("raw", StringComparison.OrdinalIgnoreCase);
		var texts = raw ? PngTexts (file, "Raw profile type exif") : [];
		var tiff = raw ? (texts.Count == 0 ? null : DecodeRawProfile (texts[0])) : PngChunks (file).FirstOrDefault (c => c.Type == "eXIf").Payload;
		return tiff is not null && tiff.AsSpan ().StartsWith ("Exif\0\0"u8) ? tiff[6..] : tiff;
	}

	/// <summary>The Exif TIFF of a PNG eXIf chunk or a JPEG APP1 segment.</summary>
	internal static byte[] ExifOf (byte[] file) => ExifIn (file[0] == 0x89 ? "png" : "jpeg", file)!;

	/// <summary>The ASCII values a reader shows from the Exif a file holds in a carrier.</summary>
	internal static Dictionary<int, string> ExifStrings (string carrier, byte[] file) => ExifReaderModel.Of (ExifIn (carrier, file)!).Strings ();

	// The full reader-visible result, before vs after, per the tests' own reader: exactly the given tags are gone
	// from the tables read with the Exif tags, and every other table, entry, value and next offset any reader sees is
	// unchanged (including tables no reader can read, which stay unread). Nothing is masked.
	internal static void AssertOnlyTheCopiesAreGone (byte[] before, byte[] after, string message, params int[] removed) =>
		Assert.AreEqual (ExifReaderModel.AnyReader (before).Visible ([.. removed]), ExifReaderModel.AnyReader (after).Visible ([]), message);

	// UserComment and XPComment, the comment copies, wherever the tests' reader finds them.
	internal static List<long> UserCommentsInEveryExifIfd (byte[] tiff) => ExifReaderModel.Of (tiff).CopiesOf (0x9286, 0x9C9C);

	internal static readonly byte[] OldOne = "ASCII\0\0\0old comment one"u8.ToArray ();
	internal static readonly byte[] OldTwo = "ASCII\0\0\0old comment two"u8.ToArray ();

	internal static readonly byte[] SecretUc = "ASCII\0\0\0secret-uc\u0001"u8.ToArray ();
	internal static readonly byte[] HiddenUc = "ASCII\0\0\0hidden-uc"u8.ToArray ();
}
