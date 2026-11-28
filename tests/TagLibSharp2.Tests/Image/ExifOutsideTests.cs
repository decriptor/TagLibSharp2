// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Exif;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A length-changing edit refuses when a kept entry or a located maker note names bytes past the Exif block,
/// or when a maker note was not located and bytes follow the JPEG EOI. The file is left as it was.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifOutsideTests
{
	const string JpegReason = "The JPEG Exif block names bytes past its own data that a length change before them would move, so its copy of Comment cannot be updated.";
	const string PngReason = "The eXIf block names bytes past its own data that a length change before them would move, so its copy of Comment cannot be updated.";

	static readonly byte[] Trailer = "TRAILER!"u8.ToArray ();

	[TestMethod]
	public void Comment_OfAJpegWhoseMakerNoteNamesTheTrailer_IsRefused ()
	{
		var fileBytes = JpegWithExternalNote (located: true, trailer: true, comment: null);
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (JpegReason, save.Error);
		CollectionAssert.AreEqual (fileBytes, file.Render ().ToArray ());
	}

	[TestMethod]
	public void Comment_OfTheSameLength_LeavesTheTrailerWhereTheNoteNamesIt ()
	{
		var fileBytes = JpegWithExternalNote (located: true, trailer: true, comment: "old");
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "new";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = file.Render ().ToArray ();
		CollectionAssert.AreEqual (Trailer, output[^Trailer.Length..]);
		Assert.AreEqual (fileBytes.Length, output.Length);
	}

	[TestMethod]
	public void Comment_OfAJpegWithAnUnlocatedNoteAndATrailer_IsRefused ()
	{
		var fileBytes = JpegWithExternalNote (located: false, trailer: true, comment: null);
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (JpegReason, save.Error);
		CollectionAssert.AreEqual (fileBytes, file.Render ().ToArray ());
	}

	[TestMethod]
	public void Comment_OfAJpegWithAnUnlocatedNoteAndNoTrailer_IsSaved ()
	{
		var fileBytes = JpegWithExternalNote (located: false, trailer: false, comment: null);
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsTrue (file.Render ().Span.IndexOf ("New"u8) >= 0);
	}

	[TestMethod]
	public void Comment_ThatGrowsTheXmpPacket_IsRefusedWhenTheNoteNamesTheTrailer ()
	{
		var packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:UserComment><rdf:Alt><rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></exif:UserComment></rdf:Description></rdf:RDF></x:xmpmeta>";
		var fileBytes = JpegWithExternalNote (located: true, trailer: true, comment: "abcde", xmp: packet);
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "newer";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (JpegReason, save.Error);
		CollectionAssert.AreEqual (fileBytes, file.Render ().ToArray ());
	}

	[TestMethod]
	public void Comment_OfAPngWhoseMakerNoteNamesBytesAfterIend_IsRefused ()
	{
		var fileBytes = PngWithExternalNote ();
		var file = Open (fileBytes, "a.png");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (PngReason, save.Error);
		CollectionAssert.AreEqual (fileBytes, file.Render ().ToArray ());
	}

	[TestMethod]
	public void ArtistAndComment_OfAPngWhoseNoteNamesBytesAfterIend_NamesBothFields ()
	{
		var fileBytes = PngWithExternalNote ();
		var file = Open (fileBytes, "a.png");
		file.Tag!.Artist = "Ada";
		file.Tag.Comment = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (
			"The eXIf block names bytes past its own data that a length change before them would move, so its copy of Artist, Comment cannot be updated.",
			save.Error);
		CollectionAssert.AreEqual (fileBytes, file.Render ().ToArray ());
	}

	[TestMethod]
	public void Comment_OfAPngWithAnUnlocatedNoteAndBytesAfterIend_IsRefused ()
	{
		var fileBytes = PngWithUnlocatedNote ();
		var file = Open (fileBytes, "a.png");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (PngReason, save.Error);
		CollectionAssert.AreEqual (fileBytes, file.Render ().ToArray ());
	}

	[TestMethod]
	public void Comment_OfTheSameLength_IsSavedWhenAnUnlocatedNoteNamesNothing ()
	{
		var fileBytes = JpegWithExternalNote (located: false, trailer: true, comment: "old");
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "new";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = file.Render ().ToArray ();
		CollectionAssert.AreEqual (Trailer, output[^Trailer.Length..]);
		Assert.AreEqual (fileBytes.Length, output.Length);
	}

	[TestMethod]
	public void Comment_OfAJpegWithAnUnlocatedNoteAndNoEndOfImage_IsSaved ()
	{
		var fileBytes = JpegWithExternalNote (located: false, trailer: false, comment: null);
		// Drop the EOI and leave a marker prefix with no second byte, so the scan has no end of image.
		fileBytes = [.. fileBytes[..^2], 0xFF];
		var file = Open (fileBytes, "a.jpg");
		file.Tag!.Comment = "New";
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsTrue (file.Render ().Span.IndexOf ("New"u8) >= 0);
	}

	[TestMethod]
	public void Comment_OfAGifWithBytesAfterTheTrailer_IsSaved ()
	{
		var gif = Concat (Gif (GifComment ("old")), "AFTER"u8.ToArray ());
		var file = Open (gif, "a.gif");
		file.Tag!.Comment = "new";
		var save = file.SaveToFile ("/a.gif", new MockFileSystem ());

		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = file.Render ().ToArray ();
		CollectionAssert.AreEqual ("AFTER"u8.ToArray (), output[^5..]);
	}

	// Maker note at the start of its blob. When located, its one entry names the trailer. The comment segment,
	// when present, is the same length as a later "newer" or "old"/"new" replacement.
	static byte[] JpegWithExternalNote (bool located, bool trailer, string? comment, string? xmp = null)
	{
		byte[] Tiff (int offset)
		{
			var header = located ? Array.Empty<byte> () : "OLYMP\0\u0001\0"u8.ToArray ();
			const int ifd0 = 8;
			var exif = ifd0 + 2 + 12 + 4;
			var note = exif + 2 + (2 * 12) + 4;
			var noteLen = header.Length + 2 + 12 + 4;
			var commentAt = note + noteLen;
			var length = commentAt + 8;
			var tiff = new TiffBuilder (length).Header (ifd0);
			tiff.Table (ifd0, [(0x8769, 4, 1, exif)]);
			tiff.Table (exif, [(0x927C, 7, noteLen, note), (0x9286, 7, 8, commentAt)]);
			tiff.Bytes (note, header);
			tiff.Entries (note + header.Length, (0x0001, 7, Trailer.Length, offset));
			tiff.Bytes (commentAt, "COMMENT!"u8);
			return tiff.ToArray ();
		}

		var segments = new List<byte[]> ();
		if (comment is not null)
			segments.Add (JpegSegment (0xFE, System.Text.Encoding.ASCII.GetBytes (comment)));
		if (xmp is not null)
			segments.Add (JpegXmp (xmp));
		byte[] Wrap (byte[] tiff)
		{
			var parts = new List<byte[]> { JpegExif (tiff) };
			parts.AddRange (segments);
			return Jpeg (parts.ToArray ());
		}

		var wrapped = Wrap (Tiff (0));
		var offset = wrapped.Length - 12;
		var jpeg = Wrap (Tiff (offset));
		return trailer ? Concat (jpeg, Trailer) : jpeg;
	}

	static byte[] PngWithExternalNote ()
	{
		byte[] Tiff (int offset)
		{
			const int ifd0 = 8;
			var exif = ifd0 + 2 + 12 + 4;
			var note = exif + 2 + (2 * 12) + 4;
			var noteLen = 2 + 12 + 4;
			var length = note + noteLen;
			var tiff = new TiffBuilder (length).Header (ifd0);
			tiff.Table (ifd0, [(0x8769, 4, 1, exif)]);
			tiff.Table (exif, [(0x927C, 7, noteLen, note), (0x9286, 7, 4, 0x41424344)]);
			tiff.Entries (note, (0x0001, 7, Trailer.Length, offset));
			return tiff.ToArray ();
		}

		byte[] Wrap (byte[] tiff) => Concat (Png (PngChunk ("eXIf", tiff)), Trailer);
		var probe = Wrap (Tiff (0));
		var marker = "II*"u8.ToArray ();
		var tiffBase = 0;
		for (var i = 0; i + 3 <= probe.Length; i++) {
			if (probe[i] == marker[0] && probe[i + 1] == marker[1] && probe[i + 2] == marker[2]) {
				tiffBase = i;
				break;
			}
		}

		var offset = probe.Length - Trailer.Length - tiffBase;
		return Wrap (Tiff (offset));
	}

	// The note header is not a classic IFD, so the note is not located. Bytes after IEND are the anchor.
	static byte[] PngWithUnlocatedNote ()
	{
		var header = "OLYMP\0\u0001\0"u8.ToArray ();
		const int ifd0 = 8;
		var exif = ifd0 + 2 + 12 + 4;
		var note = exif + 2 + (2 * 12) + 4;
		var noteLen = header.Length + 2 + 12 + 4;
		var length = note + noteLen;
		var tiff = new TiffBuilder (length).Header (ifd0);
		tiff.Table (ifd0, [(0x8769, 4, 1, exif)]);
		tiff.Table (exif, [(0x927C, 7, noteLen, note), (0x9286, 7, 4, 0x41424344)]);
		tiff.Bytes (note, header);
		tiff.Entries (note + header.Length, (0x0001, 7, 4, 0));
		return Concat (Png (PngChunk ("eXIf", tiff.ToArray ())), Trailer);
	}
}
