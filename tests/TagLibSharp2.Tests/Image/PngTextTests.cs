// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.IO.Compression;
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

/// <summary>PNG text chunks: reading and writing the mapped keywords, compression, the shared inflation budget, and keyword copies.</summary>
[TestClass]
[TestCategory ("Unit")]
public partial class PngTextTests
{
	[TestMethod]
	public void Png_InflationBudget_SpentLeavesLaterChunksUncompressed ()
	{
		// One Title chunk inflates to exactly the 1 MiB budget; every later compressed text chunk must be skipped
		// without decompressing anything.
		var bomb = CompressedText ("Title", Enumerable.Repeat ((byte)'a', Mib).ToArray ());
		var small = CompressedText ("Title", "x"u8.ToArray ());
		static byte[] With (byte[] bomb, byte[] small, int n) => Png ([bomb, .. Enumerable.Repeat (small, n)]);
		var none = With (bomb, small, 0);
		var many = With (bomb, small, 2000);
		_ = MediaFile.ReadFromData (none, "a.png");
		_ = MediaFile.ReadFromData (many, "a.png");

		var baseline = Bounds.AllocatedBytes (() => MediaFile.ReadFromData (none, "a.png"));
		var withMore = Bounds.AllocatedBytes (() => MediaFile.ReadFromData (many, "a.png"));

		Assert.IsTrue (withMore - baseline < 2000 * 256, $"2000 chunks after the budget cost {withMore - baseline} bytes");
		Assert.AreEqual (Mib, MediaFile.ReadFromData (many, "a.png").Tag!.Title!.Length);
	}

	[TestMethod]
	public void Png_RoundTripsMappedText_AndLeavesPixelsAndUntouchedChunks ()
	{
		var idat = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		var software = Text ("Software", "camera");
		var data = MinimalPng (
			Text ("Title", "Door"),
			Text ("Author", "Ada"),
			software,
			PngChunk ("IDAT", idat));
		var file = Open (data, "photo.png");
		Assert.IsNotNull (file.Tag);
		Assert.AreEqual ("Door", file.Tag.Title);
		Assert.AreEqual ("Ada", file.Tag.Artist);

		file.Tag.Album = "Not stored";
		file.Tag.Genre = "Rock";
		file.Tag.Track = 4;
		Assert.IsNull (file.Tag.Album);
		Assert.IsNull (file.Tag.Genre);
		Assert.IsNull (file.Tag.Track);
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());

		file.Tag.Description = "A door";
		file.Tag.Copyright = "Ada";
		file.Tag.Year = "2020-01-02";
		file.Tag.Comment = "hi";
		file.Tag.Title = "Tür";
		var rendered = file.Render ().ToArray ();
		AssertPng (rendered);
		CollectionAssert.AreEqual (idat, PngPayload (rendered, "IDAT"));
		CollectionAssert.AreEqual (software, PngTextChunk (rendered, "tEXt", "Software"));

		var again = Open (rendered, "photo.png");
		Assert.AreEqual ("Tür", again.Tag!.Title);
		Assert.AreEqual ("Ada", again.Tag.Artist);
		Assert.AreEqual ("A door", again.Tag.Description);
		Assert.AreEqual ("Ada", again.Tag.Copyright);
		Assert.AreEqual ("2020-01-02", again.Tag.Year);
		Assert.AreEqual ("hi", again.Tag.Comment);
		Assert.IsNull (again.Tag.Album);
		Assert.IsFalse (HasPngChunk (rendered, "iTXt"));

		again.Tag.Title = null;
		var cleared = again.Render ().ToArray ();
		Assert.IsNull (Open (cleared, "photo.png").Tag!.Title);
		Assert.AreEqual ("Ada", Open (cleared, "photo.png").Tag!.Artist);
		CollectionAssert.AreEqual (idat, PngPayload (cleared, "IDAT"));
	}

	[TestMethod]
	public void Png_UnicodeAndCompressedText_RoundTrip ()
	{
		var title = Encoding.Latin1.GetBytes ("Door");
		var zlib = Zlib (title, CompressionLevel.SmallestSize);
		var compressed = new byte[1 + 1 + "Title".Length + zlib.Length];
		Encoding.ASCII.GetBytes ("Title").CopyTo (compressed, 0);
		compressed["Title".Length + 1] = 0;
		zlib.CopyTo (compressed, "Title".Length + 2);
		var ztxt = PngChunk ("zTXt", compressed);
		var data = MinimalPng (ztxt, PngChunk ("IDAT", [0x09]));
		var file = Open (data, "photo.png");
		Assert.AreEqual ("Door", file.Tag!.Title);

		file.Tag.Comment = "note";
		var kept = file.Render ().ToArray ();
		CollectionAssert.AreEqual (ztxt, PngTextChunk (kept, "zTXt", "Title"));
		Assert.AreEqual ("Door", Open (kept, "photo.png").Tag!.Title);
		Assert.AreEqual ("note", Open (kept, "photo.png").Tag!.Comment);

		file.Tag.Title = "門";
		var replaced = file.Render ().ToArray ();
		Assert.IsFalse (HasPngChunk (replaced, "zTXt"));
		CollectionAssert.AreEqual (new byte[] { 0xE9, 0x96, 0x80 }, TextBytes (replaced, "iTXt", "Title"));
		Assert.AreEqual ("門", Open (replaced, "photo.png").Tag!.Title);
		Assert.AreEqual ("note", Open (replaced, "photo.png").Tag!.Comment);
		CollectionAssert.AreEqual (new byte[] { 0x09 }, PngPayload (replaced, "IDAT"));
	}

	[TestMethod]
	public void Png_CompressedTextOverTheCap_IsLeftUnread ()
	{
		var huge = new byte[1024 * 1024 + 1];
		var ztxt = MinimalPng (CompressedTextChunk ("zTXt", "Title", huge, utf8: false), PngChunk ("IDAT", [0x11]));
		var itxt = MinimalPng (CompressedTextChunk ("iTXt", "Comment", huge, utf8: true), PngChunk ("IDAT", [0x11]));
		var zTitle = Open (ztxt, "bomb.png").Tag!.Title;
		var iComment = Open (itxt, "bomb.png").Tag!.Comment;
		Assert.IsTrue (zTitle is null, $"zTXt inflated to {zTitle?.Length}");
		Assert.IsTrue (iComment is null, $"iTXt inflated to {iComment?.Length}");
		Assert.AreEqual (1, Open (ztxt, "bomb.png").ImageProperties!.Value.Width);
		CollectionAssert.AreEqual (new byte[] { 0x11 }, PngPayload (ztxt, "IDAT"));
	}

	[TestMethod]
	public void Png_SmallCompressedInternationalText_IsRead ()
	{
		var data = MinimalPng (CompressedTextChunk ("iTXt", "Title", System.Text.Encoding.UTF8.GetBytes ("門"), utf8: true));
		Assert.AreEqual ("門", Open (data, "photo.png").Tag!.Title);
	}

	[TestMethod]
	public void Png_NewText_IsWrittenBeforeThePixelData ()
	{
		var data = MinimalPng (PngChunk ("IDAT", [0x0A, 0x0B]));
		var file = Open (data, "photo.png");
		file.Tag!.Title = "Door";
		var rendered = file.Render ().ToArray ();

		var text = ChunkAt (rendered, "tEXt");
		var pixels = ChunkAt (rendered, "IDAT");
		Assert.IsTrue (text >= 0 && pixels > text, $"text {text} idat {pixels}");
		Assert.AreEqual ("Door", Open (rendered, "photo.png").Tag!.Title);
		CollectionAssert.AreEqual (new byte[] { 0x0A, 0x0B }, PngPayload (rendered, "IDAT"));
	}

	[TestMethod]
	public void Png_TextWithABadCrc_IsNotLoaded ()
	{
		var chunk = Text ("Title", "Door");
		chunk[^1] ^= 0xFF;
		var data = MinimalPng (chunk, PngChunk ("IDAT", [0x01]));

		var opened = Open (data, "photo.png");

		Assert.IsNull (opened.Tag!.Title);
		Assert.AreEqual (1, opened.ImageProperties!.Value.Width);
	}

	[TestMethod]
	public void Png_InflationBudget_IsSharedAcrossChunks ()
	{
		var author = new byte[700_000];
		var description = new byte[700_000];
		var comment = new byte[1000];
		Array.Fill (author, (byte)'A');
		Array.Fill (description, (byte)'D');
		Array.Fill (comment, (byte)'n');
		var data = MinimalPng (
			Text ("Title", "one"),
			CompressedTextChunk ("zTXt", "Author", author, utf8: false),
			CompressedTextChunk ("zTXt", "Description", description, utf8: false),
			CompressedTextChunk ("zTXt", "Comment", comment, utf8: false),
			PngChunk ("IDAT", [0x01]));

		var tag = Open (data, "photo.png").Tag!;

		Assert.AreEqual ("one", tag.Title);
		Assert.AreEqual (700_000, tag.Artist?.Length);
		Assert.IsNull (tag.Description);
		Assert.AreEqual (new string ('n', 1000), tag.Comment);
	}

	static void AssertPng (byte[] file)
	{
		var i = 8;
		while (i + 12 <= file.Length) {
			var length = BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (i));
			var crcInput = new byte[4 + length];
			Array.Copy (file, i + 4, crcInput, 0, crcInput.Length);
			var actual = BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (i + 8 + length));
			Assert.AreEqual (new BinaryData (crcInput).ComputeCrc32 (), (uint)actual);
			i += 12 + length;
			if (Encoding.ASCII.GetString (file, i - 12 - length + 4, 4) == "IEND")
				return;
		}

		Assert.Fail ("PNG has no IEND");
	}

	static byte[] TextBytes (byte[] file, string type, string keyword)
	{
		var chunk = PngTextChunk (file, type, keyword);
		var key = Encoding.ASCII.GetBytes (keyword);
		var start = 8 + key.Length + 1;
		if (type == "iTXt") {
			start += 2;
			while (start < chunk.Length - 4 && chunk[start] != 0)
				start++;
			start++;
			while (start < chunk.Length - 4 && chunk[start] != 0)
				start++;
			start++;
		}

		var count = chunk.Length - 4 - start;
		var text = new byte[count];
		Array.Copy (chunk, start, text, 0, count);
		return text;
	}

	static int ChunkAt (byte[] file, string type)
	{
		var i = 8;
		while (i + 12 <= file.Length) {
			if (Encoding.ASCII.GetString (file, i + 4, 4) == type)
				return i;
			i += 12 + BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (i));
		}

		return -1;
	}

	static byte[] CompressedTextChunk (string type, string keyword, byte[] raw, bool utf8)
	{
		var key = System.Text.Encoding.ASCII.GetBytes (keyword);
		var zlib = Zlib (raw, CompressionLevel.SmallestSize);
		if (type == "zTXt") {
			var payload = new byte[key.Length + 2 + zlib.Length];
			key.CopyTo (payload, 0);
			zlib.CopyTo (payload, key.Length + 2);
			return PngChunk ("zTXt", payload);
		}

		var text = new byte[key.Length + 5 + zlib.Length];
		key.CopyTo (text, 0);
		text[key.Length + 1] = 1;
		zlib.CopyTo (text, key.Length + 5);
		return PngChunk (utf8 ? "iTXt" : type, text);
	}

	[TestMethod]
	public void Read_ExiftoolArtistKeyword_IsTheArtistWhenThereIsNoAuthor ()
	{
		Assert.AreEqual ("From exiftool", Open (Png (Text ("Artist", "From exiftool")), "a.png").Tag!.Artist);
		Assert.AreEqual ("Author wins", Open (Png (Text ("Artist", "From exiftool"), Text ("Author", "Author wins")), "a.png").Tag!.Artist);
		Assert.AreEqual ("Author wins", Open (Png (Text ("Author", "Author wins"), Text ("Artist", "From exiftool")), "a.png").Tag!.Artist);

		var file = Open (Png (Text ("Artist", "From exiftool")), "a.png");
		file.Tag!.Artist = null;
		var output = file.Render ().ToArray ();
		Assert.AreEqual (0, PngTexts (output, "Artist").Count, "null clears the exiftool keyword");
		Assert.IsNull (Open (output, "a.png").Tag!.Artist);
	}

	[TestMethod]
	public void Edit_ImageMagickExifTextKeyword_IsDroppedWithTheField ()
	{
		var file = Open (Png (Text ("exif:Artist", "Old"), Text ("exif:ImageDescription", "Old"), Text ("exif:Make", "Canon")), "a.png");
		file.Tag!.Artist = "New";
		var output = file.Render ().ToArray ();
		Assert.AreEqual (0, PngTexts (output, "exif:Artist").Count);
		Assert.AreSequenceEqual (["Old"], PngTexts (output, "exif:ImageDescription"));
		Assert.AreSequenceEqual (["Canon"], PngTexts (output, "exif:Make"));
	}

	static byte[] ManyCompressedPackets (int n, int spaces) =>
		Png ([Text ("Title", "Old"), .. Enumerable.Range (0, n).Select (_ => Xmp (XmpPacket (Alt ("dc:title", "Old") + new string (' ', spaces)), compressed: true))]);

	// One 1 MiB inflation budget for the whole file, shared by every compressed copy, as when it is read.
	[TestMethod]
	public void Edit_CompressedCopies_ShareOneInflationBudget ()
	{
		var bombs = ManyCompressedPackets (10, 1_000_000);
		var file = Open (bombs, "a.png");
		file.Tag!.Title = "New";
		_ = file.SaveToFile ("/a.png", new MockFileSystem ());
		var allocated = Bounds.AllocatedBytes (() => {
			var result = file.SaveToFile ("/a.png", new MockFileSystem ());
			Assert.IsFalse (result.IsSuccess);
			StringAssert.Contains (result.Error, "all compressed text");
		});
		Assert.IsTrue (allocated < 24L * 1024 * 1024, $"ten 1 MB packets cost {allocated} bytes");

		Bounds.AssertLinearAllocation (n => ManyCompressedPackets (n, 2000), data => {
			var png = Open (data, "a.png");
			png.Tag!.Title = "New";
			Assert.IsTrue (png.SaveToFile ("/a.png", new MockFileSystem ()).IsSuccess);
		}, 20);
	}
}
