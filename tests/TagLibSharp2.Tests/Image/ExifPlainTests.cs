// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using static TagLibSharp2.Tests.Image.ExifTestData;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A field edit runs only on plain Exif: SHORT or LONG offset/length pairs, each array named once, every next-IFD
/// field wholly present or wholly absent, and a first entry whose readability does not change. Anything else is
/// refused before a plan is built, and the block is left as it was.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifPlainTests
{
	public static IEnumerable<object[]> PairTypes ()
	{
		(int Offsets, int Lengths)[] pairs = [(0x0111, 0x0117), (0x0144, 0x0145), (0x0201, 0x0202)];
		foreach (var littleEndian in new[] { true, false })
			foreach (var home in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop", "GPS" })
				foreach (var (offsets, lengths) in pairs)
					foreach (var badType in new[] { 7, 8 })
						foreach (var offsetsBad in new[] { true, false })
							yield return [littleEndian, home, offsets, lengths, badType, offsetsBad];
	}

	[TestMethod]
	[DynamicData (nameof (PairTypes))]
	public void Edit_RefusesAPairTypedOtherThanShortOrLong (bool littleEndian, string home, int offsets, int lengths, int badType, bool offsetsBad)
	{
		var tiff = WithPair (littleEndian, home, offsets, lengths, offsetsBad ? badType : 4, offsetsBad ? 4 : badType);
		var copy = (byte[])tiff.Clone ();
		var tag = new ImageTag (MediaFormat.Png, ImageStores.CommentOnly) { Comment = "n" };

		var saved = ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error);

		Assert.IsFalse (saved, $"{home} {(littleEndian ? "LE" : "BE")} {offsets:X4}/{lengths:X4} type {badType}: saved");
		Assert.IsFalse (changed);
		StringAssert.Contains (error, "cannot be updated");
		StringAssert.Contains (error, "SHORT or LONG");
		CollectionAssert.AreEqual (copy, tiff);
	}

	// The pair lives in `home`. A UserComment in the Exif IFD gives the edit something to remove. Every next-IFD
	// field is present, and the other side of the pair is LONG, so the type is the only thing that is not plain.
	static byte[] WithPair (bool littleEndian, string home, int offsets, int lengths, int offsetsType, int lengthsType)
	{
		const int Ifd0 = 8, Exif = 90, Ifd1 = 180, Sub = 270, Iop = 360, Gps = 450, Comment = 540;
		var comment = SecretUc;
		var ifd0 = new List<(int Tag, int Type, long Count, long Value)> { (0x8769, 4, 1, Exif) };
		var exif = new List<(int Tag, int Type, long Count, long Value)> { (0x9286, 7, comment.Length, Comment) };
		(int At, (int Tag, int Type, long Count, long Value)[] Entries)? extra = null;
		long ifd0Next = 0;
		(int, int, long, long)[] pair = [(offsets, offsetsType, 1, 600), (lengths, lengthsType, 1, 8)];
		switch (home) {
			case "IFD0":
				ifd0.AddRange (pair);
				break;
			case "Exif":
				exif.AddRange (pair);
				break;
			case "IFD1":
				extra = (Ifd1, pair);
				ifd0Next = Ifd1;
				break;
			case "SubIFD":
				extra = (Sub, pair);
				ifd0.Add ((0x014A, 4, 1, Sub));
				break;
			case "Interop":
				extra = (Iop, pair);
				exif.Add ((0xA005, 4, 1, Iop));
				break;
			default:
				extra = (Gps, pair);
				ifd0.Add ((0x8825, 4, 1, Gps));
				break;
		}

		var tiff = new TiffBuilder (Comment + comment.Length, bigEndian: !littleEndian).Header (Ifd0).Bytes (Comment, comment)
			.Table (Ifd0, [.. ifd0], ifd0Next)
			.Table (Exif, [.. exif]);
		if (extra is var (at, entries))
			tiff.Table (at, entries);
		return tiff.ToArray ();
	}
}
