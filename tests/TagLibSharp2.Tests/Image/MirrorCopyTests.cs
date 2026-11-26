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
using static TagLibSharp2.Tests.Image.ExifTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>The copies of a changed field in other blocks of the same file (XMP, Exif, keyword copies) change with it, and only those.</summary>
[TestClass]
[TestCategory ("Unit")]
public class MirrorCopyTests
{
	// field, XMP property, Exif tags that copy it
	static readonly (string Field, XName Xmp, bool Ordered, int[] ExifTags)[] Fields = [
		("Title", Dc + "title", false, [0x9C9B]),
		("Artist", Dc + "creator", true, [0x013B, 0x9C9D]),
		("Description", Dc + "description", false, [0x010E]),
		("Copyright", Dc + "rights", false, [0x8298]),
		("Comment", Exif + "UserComment", false, [0x9C9C, 0x9286]),
	];

	static string AllProperties () =>
		Alt ("dc:title", "Old title") + Seq ("dc:creator", "Old one", "Old two") + Alt ("dc:description", "Old desc") +
		Alt ("dc:rights", "Old rights") + Alt ("exif:UserComment", "Old comment") + "   <xmp:Label>Keep label</xmp:Label>\n";

	static byte[] AllExif () => ExifTiff (
		[(0x010E, "Old desc"), (0x010F, "Canon"), (0x013B, "Old artist"), (0x8298, "Old rights"), (0x9C9B, "XP title"), (0x9C9C, "XP comment"), (0x9C9D, "XP author")],
		[(0x9003, "2001:01:01 00:00:00"), (0x9286, "ASCII\0\0\0Old comment")]);

	static readonly (string Name, Func<byte[]> Build)[] Carriers = [
		("iTXt XMP", () => Xmp (XmpPacket (AllProperties ()))),
		("compressed iTXt XMP", () => Xmp (XmpPacket (AllProperties ()), compressed: true)),
		("ImageMagick raw XMP", () => RawProfile ("xmp", Encoding.UTF8.GetBytes (XmpPacket (AllProperties ())))),
		("eXIf", () => PngChunk ("eXIf", AllExif ())),
		("eXIf with Exif prefix", () => PngChunk ("eXIf", Concat ("Exif\0\0"u8.ToArray (), AllExif ()))),
		("ImageMagick raw EXIF", () => RawProfile ("exif", Concat ("Exif\0\0"u8.ToArray (), AllExif ()))),
		("exiftool Artist keyword", () => Text ("Artist", "Old artist")),
	];

	public static IEnumerable<object[]> Cases () =>
		from carrier in Carriers
		from field in Fields
		from value in new[] { "New ✓ <&>", null }
		select new object?[] { carrier.Name, field.Field, value };

	[TestMethod]
	[DynamicData (nameof (Cases))]
	public void Edit_ChangesTheMirroredCopy_AndOnlyThatOne (string carrier, string field, string? value)
	{
		// Each field holds a tEXt value too, so that null is a change (writing back the null read is not).
		var input = Png (
			Carriers.Single (c => c.Name == carrier).Build (), Text ("Software", "keep"), Text ("Title", "t"), Text ("Author", "a"),
			Text ("Description", "d"), Text ("Copyright", "c"), Text ("Comment", "m"));
		var file = Open (input, "a.png");
		typeof (Tag).GetProperty (field)!.SetValue (file.Tag, value);

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = file.Render ().ToArray ();

		var reread = Open (output, "a.png");
		Assert.AreEqual (value, typeof (Tag).GetProperty (field)!.GetValue (reread.Tag));
		Assert.AreSequenceEqual (["keep"], PngTexts (output, "Software"));
		var spec = Fields.Single (f => f.Field == field);
		if (carrier.StartsWith ("exiftool", StringComparison.Ordinal))
			Assert.AreSequenceEqual (field == "Artist" ? [] : ["Old artist"], PngTexts (output, "Artist"));
		else if (carrier.Contains ("XMP", StringComparison.Ordinal))
			AssertXmp (Packet (input, carrier), Packet (output, carrier), spec.Xmp, spec.Ordered, value);
		else
			AssertExif (ExifStrings (carrier, input), ExifStrings (carrier, output), spec.ExifTags, ExifIn (carrier, output)!);
	}

	static void AssertXmp (string before, string after, XName property, bool ordered, string? value)
	{
		var old = XDocument.Parse (before);
		var updated = XDocument.Parse (after);
		var found = updated.Descendants (property).ToList ();
		if (value is null) {
			Assert.AreEqual (0, found.Count, $"{property} should be removed");
		} else {
			Assert.AreEqual (1, found.Count, $"{property} should be written once");
			var items = found[0].Element (Rdf + (ordered ? "Seq" : "Alt"))!.Elements (Rdf + "li").ToList ();
			Assert.AreEqual (1, items.Count, "every old alternative or array item is replaced");
			Assert.AreEqual (value, items[0].Value);
			if (!ordered)
				Assert.AreEqual ("x-default", items[0].Attribute (XNamespace.Xml + "lang")!.Value);
		}

		// Every other property keeps its exact text.
		foreach (var other in old.Descendants ().Where (e => e.Parent?.Name == Rdf + "Description" && e.Name != property)) {
			var text = Slice (before, other.Name);
			StringAssert.Contains (after, text, $"{other.Name} changed");
		}
	}

	static string Slice (string packet, XName name)
	{
		var prefix = name.Namespace == Dc ? "dc" : name.Namespace == Exif ? "exif" : "xmp";
		var open = $"<{prefix}:{name.LocalName}>";
		var close = $"</{prefix}:{name.LocalName}>";
		var start = packet.IndexOf (open, StringComparison.Ordinal);
		var end = packet.IndexOf (close, start, StringComparison.Ordinal) + close.Length;
		return packet[start..end];
	}

	static void AssertExif (Dictionary<int, string> before, Dictionary<int, string> after, int[] removed, byte[] tiff)
	{
		foreach (var (tag, text) in before) {
			if (removed.Contains (tag)) {
				Assert.IsFalse (after.ContainsKey (tag), $"Exif tag 0x{tag:X4} should be removed");
				Assert.IsTrue (tiff.AsSpan ().IndexOf (Encoding.ASCII.GetBytes (text)) < 0, $"the removed value of 0x{tag:X4} is still in the block");
			} else
				Assert.AreEqual (text, after[tag], $"Exif tag 0x{tag:X4} changed");
		}
	}

	static string Packet (byte[] png, string carrier)
	{
		if (carrier.Contains ("raw", StringComparison.OrdinalIgnoreCase))
			return Encoding.UTF8.GetString (DecodeRawProfile (PngTexts (png, "Raw profile type xmp").Single ()));
		return PngTexts (png, "XML:com.adobe.xmp").Single ();
	}

	[TestMethod]
	public void Edit_LeavesTheMirrorsOfUnchangedFieldsByteForByte ()
	{
		var xmp = Xmp (XmpPacket (AllProperties ()));
		var exif = PngChunk ("eXIf", AllExif ());
		var file = Open (Png (xmp, exif), "a.png");
		file.Tag!.Year = "2024";

		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf (xmp) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf (exif) >= 0);
	}

	public static IEnumerable<object[]> CopiesWithoutTheField ()
	{
		// Each block would change if it were rebuilt: the packet holds a byte that is not UTF-8 (0xE9, as a
		// Latin-1 writer leaves it), the compressed ones use another compressor level, and the raw profiles
		// another hex line width.
		var marked = Encoding.UTF8.GetBytes (XmpPacket (Seq ("dc:creator", "Someone") + "   <xmp:Label>caf#</xmp:Label>\n"));
		var packet = marked.Select (b => b == (byte)'#' ? (byte)0xE9 : b).ToArray ();
		var utf8 = Encoding.UTF8.GetBytes (XmpPacket (Seq ("dc:creator", "Someone")));
		var exif = ExifTiff ([(0x010F, "Canon"), (0x013B, "Someone")], []);
		var fast = System.IO.Compression.CompressionLevel.Fastest;
		var itxt = RawItxt ("XML:com.adobe.xmp", packet);
		var itxtZ = CompressedChunk ("XML:com.adobe.xmp", utf8, fast, itxt: true);
		var ztxt = CompressedChunk ("XML:com.adobe.xmp", utf8, fast, itxt: false);
		var rawXmp = RawProfile ("xmp", utf8, 32, fast);
		var rawExif = RawProfile ("exif", Concat ("Exif\0\0"u8.ToArray (), exif), 32, fast);
		var jpegXmp = JpegSegment (0xE1, Concat ("http://ns.adobe.com/xap/1.0/\0"u8.ToArray (), packet));
		var gifXmp = Concat (GifXmp ("").AsSpan (0, 14).ToArray (), packet, GifXmp ("").AsSpan (14).ToArray ());
		yield return ["png", "iTXt XMP with a Latin-1 byte", Png (itxt), itxt];
		yield return ["png", "compressed iTXt XMP", Png (itxtZ), itxtZ];
		yield return ["png", "zTXt XMP", Png (ztxt), ztxt];
		yield return ["png", "ImageMagick raw XMP", Png (rawXmp), rawXmp];
		yield return ["png", "eXIf", Png (PngChunk ("eXIf", exif)), PngChunk ("eXIf", exif)];
		yield return ["png", "ImageMagick raw EXIF", Png (rawExif), rawExif];
		yield return ["jpeg", "APP1 XMP with a Latin-1 byte", Jpeg (jpegXmp, JpegSegment (0xFE, "c"u8.ToArray ())), jpegXmp];
		yield return ["jpeg", "APP1 Exif", Jpeg (JpegExif (exif), JpegSegment (0xFE, "c"u8.ToArray ())), JpegExif (exif)];
		// T.81 §B.1.1.2: fill bytes (0xFF) may precede a marker; they belong to the kept bytes.
		var filled = Concat ([0xFF, 0xFF], JpegExif (exif));
		yield return ["jpeg", "APP1 Exif after fill bytes", Jpeg (filled, JpegSegment (0xFE, "c"u8.ToArray ())), filled];
		yield return ["gif", "XMP with a Latin-1 byte", Gif (GifComment ("c"), gifXmp), gifXmp];
	}

	// The field edited (Title on PNG, Comment on JPEG and GIF) has no copy in the block, which keeps its bytes:
	// a compressed chunk is not recompressed, and an Exif block is not rewritten.
	[TestMethod]
	[DynamicData (nameof (CopiesWithoutTheField))]
	public void Edit_OfAFieldTheBlockDoesNotHold_KeepsTheBlockBytes (string format, string carrier, byte[] input, byte[] block)
	{
		var file = Open (input, format == "png" ? "a.png" : format == "jpeg" ? "a.jpg" : "a.gif");
		if (format == "png")
			file.Tag!.Title = "New";
		else
			file.Tag!.Comment = "New";
		Assert.IsTrue (file.Render ().ToArray ().AsSpan ().IndexOf (block) >= 0, carrier);
	}

	// A copy no reader decodes holds nothing to update, so the edit saves and keeps its bytes: an ImageMagick raw
	// profile that is not hexadecimal (exiftool: "Unknown raw profile"), and an XMP packet that is not well-formed
	// (exiftool: "Empty XMP") whose text names none of the changed field's properties. A malformed packet that does
	// name it is refused. Compressed XMP that is not zlib data, or an iTXt whose header is cut short, is decoded by
	// no reader either (exiftool: "Error inflating"; no packet), and is kept.
	[TestMethod]
	[DataRow ("XMP that is not zlib data", "Title", true)]
	[DataRow ("XMP whose iTXt header is cut short", "Comment", true)]
	[DataRow ("raw EXIF profile that is not hex", "Title", true)]
	[DataRow ("raw EXIF profile that is not hex", "Comment", true)]
	[DataRow ("raw EXIF profile that is not hex", "Artist", true)]
	[DataRow ("raw XMP profile that is not hex", "Title", true)]
	[DataRow ("malformed XMP with only a creator", "Title", true)]
	[DataRow ("malformed XMP with only a creator", "Comment", true)]
	[DataRow ("malformed XMP with only a creator", "Artist", false)]
	public void Edit_OfACopyNoReaderDecodes_SavesAndKeepsItsBytes (string shape, string field, bool saves)
	{
		var (chunk, type, keyword) = shape switch {
			"XMP that is not zlib data" => (PngChunk ("zTXt", Concat ("XML:com.adobe.xmp\0\0"u8.ToArray (), [0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0])), "zTXt", "XML:com.adobe.xmp"),
			"XMP whose iTXt header is cut short" => (PngChunk ("iTXt", Concat ("XML:com.adobe.xmp\0"u8.ToArray (), [1, 0, (byte)'e', (byte)'n'])), "iTXt", "XML:com.adobe.xmp"),
			"raw EXIF profile that is not hex" => (CompressedText ("Raw profile type exif", "\nexif\n      4\nzzzz\n"u8.ToArray ()), "zTXt", "Raw profile type exif"),
			"raw XMP profile that is not hex" => (CompressedText ("Raw profile type xmp", "\nxmp\n      4\nzzzz\n"u8.ToArray ()), "zTXt", "Raw profile type xmp"),
			_ => (Xmp ("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:creator><rdf:Seq><rdf:li>Jane</rdf:li></rdf:Seq></dc:creator></rdf:Description></rdf:RDF>"), "iTXt", "XML:com.adobe.xmp"),
		};
		var input = Png (chunk);
		var file = Open (input, "a.png");
		typeof (Tag).GetProperty (field)!.SetValue (file.Tag, "New");

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.AreEqual (saves, save.IsSuccess, $"{shape} {field}: {save.Error}");
		if (saves)
			CollectionAssert.AreEqual (chunk, PngTextChunk (file.Render ().ToArray (), type, keyword), $"{shape} {field}: the block keeps its bytes");
		else
			StringAssert.Contains (save.Error, "not well-formed");
	}

	public static IEnumerable<object[]> Refusals ()
	{
		yield return ["not well-formed", Png (Xmp (XmpPacket (Alt ("dc:title", "Old")).Replace ("</rdf:RDF>", "", StringComparison.Ordinal))), "Title", "New", "XMP"];
		yield return ["DTD", Png (Xmp ("<!DOCTYPE x [<!ENTITY e 'x'>]>" + XmpPacket (Alt ("dc:title", "&e;")))), "Title", "New", "DTD"];
		yield return ["value XML cannot store", Png (Xmp (XmpPacket (Alt ("dc:title", "Old")))), "Title", "bad\u0001", "XML cannot store"];
		yield return ["lone surrogate", Png (Xmp (XmpPacket (Alt ("dc:title", "Old")))), "Title", "bad\uD800", "XML cannot store"];
		// Not TIFF, and an entry for Artist (tag 0x013B, type 2) is in its bytes: a reader of another form could hold a copy.
		yield return ["eXIf that is not TIFF", Png (PngChunk ("eXIf", [1, 2, 3, 4, 0x01, 0x3B, 0, 2, 9])), "Artist", "New", "not a TIFF structure"];
		yield return ["compressed XMP past the inflation cap", Png (Xmp (XmpPacket (Alt ("dc:title", "Old") + new string (' ', 1_100_000)), compressed: true)), "Title", "New", "all compressed text"];
	}

	[TestMethod]
	[DynamicData (nameof (Refusals))]
	public void Edit_ThatCannotUpdateAMirror_IsRefusedWithItsOwnReason (string shape, byte[] input, string field, string value, string reason)
	{
		var file = Open (input, "a.png");
		typeof (Tag).GetProperty (field)!.SetValue (file.Tag, value);

		var fs = new MockFileSystem ();
		var save = file.SaveToFile ("/a.png", fs);
		var saveAsync = file.SaveToFileAsync ("/b.png", fs).GetAwaiter ().GetResult ();

		Assert.IsFalse (save.IsSuccess, shape);
		Assert.IsFalse (saveAsync.IsSuccess, shape);
		StringAssert.Contains (save.Error, reason, shape);
		Assert.AreEqual (save.Error, saveAsync.Error);
		Assert.IsFalse (fs.FileExists ("/a.png"));
		CollectionAssert.AreEqual (input, file.Render ().ToArray (), "Render returns the unchanged file");

		file.Tag!.Clear ();
		Assert.IsTrue (file.SaveToFile ("/a.png", fs).IsSuccess, $"{shape}: Clear drops the block instead");
	}

	[TestMethod]
	public void Edit_BadCrcMirror_IsCopiedAsIs ()
	{
		var xmp = Xmp (XmpPacket (Alt ("dc:title", "Old")));
		xmp[^1] ^= 0xFF;
		var file = Open (Png (xmp), "a.png");
		file.Tag!.Title = "New";
		Assert.IsTrue (file.Render ().ToArray ().AsSpan ().IndexOf (xmp) >= 0);
	}

	static readonly string CommentPacket = XmpPacket (Alt ("exif:UserComment", "Old xmp comment") + Alt ("dc:title", "Keep title"));
	static byte[] CommentExif () => ExifTiff ([(0x010F, "Canon"), (0x9C9C, "Old XP comment")], [(0x9286, "ASCII\0\0\0Old user comment")]);

	public static IEnumerable<object[]> CommentCopies ()
	{
		foreach (var value in new[] { "New ✓ <&>", null }) {
			yield return ["jpeg", "APP1 Exif", Jpeg (JpegExif (CommentExif ()), JpegSegment (0xFE, "Old"u8.ToArray ())), value!];
			yield return ["jpeg", "APP1 XMP", Jpeg (JpegXmp (CommentPacket), JpegSegment (0xFE, "Old"u8.ToArray ())), value!];
			yield return ["jpeg", "APP1 XMP with a space in its signature", Jpeg (JpegSegment (0xE1, Concat ("http://ns.adobe.com/xap/1.0/ \0"u8.ToArray (), Encoding.UTF8.GetBytes (CommentPacket))), JpegSegment (0xFE, "Old"u8.ToArray ())), value!];
			yield return ["gif", "XMP", Gif (GifComment ("Old"), GifXmp (CommentPacket)), value!];
			yield return ["gif", "XMP as data sub-blocks (ImageIO)", Gif (GifComment ("Old"), GifSubBlockXmp (CommentPacket)), value!];
		}
	}

	// A JPEG or GIF stores only the comment, and its Exif and XMP copies are changed with it (rule 4).
	[TestMethod]
	[DynamicData (nameof (CommentCopies))]
	public void JpegAndGif_CommentEdit_ChangesTheExifAndXmpCopies (string format, string carrier, byte[] input, string? value)
	{
		var name = format == "jpeg" ? "a.jpg" : "a.gif";
		var file = Open (input, name);
		file.Tag!.Comment = value;
		var save = file.SaveToFile ("/" + name, new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = file.Render ().ToArray ();
		Assert.AreEqual (value, Open (output, name).Tag!.Comment);

		if (carrier == "APP1 Exif") {
			var at = output.AsSpan ().IndexOf ("Exif\0\0"u8) + 6;
			var exif = ExifReaderModel.Of (output[at..]).Strings ();
			Assert.IsFalse (exif.ContainsKey (0x9286) || exif.ContainsKey (0x9C9C), "UserComment and XPComment are removed");
			Assert.AreEqual ("Canon", exif[0x010F]);
			Assert.IsTrue (output.AsSpan ().IndexOf ("Old user comment"u8) < 0 && output.AsSpan ().IndexOf ("Old XP comment"u8) < 0);
			return;
		}

		var text = Encoding.UTF8.GetString (carrier.Contains ("sub-blocks", StringComparison.Ordinal) ? JoinGifXmpSubBlocks (output) : output);
		var packet = text.Split ("<?xpacket begin=", StringSplitOptions.None)[1];
		packet = "<?xpacket begin=" + packet[..(packet.IndexOf ("<?xpacket end='w'?>", StringComparison.Ordinal) + 19)];
		var doc = XDocument.Parse (packet);
		var comments = doc.Descendants (Exif + "UserComment").ToList ();
		if (value is null) {
			Assert.AreEqual (0, comments.Count);
		} else {
			Assert.AreEqual (1, comments.Count);
			Assert.AreEqual (value, comments[0].Value);
		}

		Assert.AreEqual ("Keep title", doc.Descendants (Dc + "title").Single ().Element (Rdf + "Alt")!.Elements ().First ().Value);
		if (carrier == "XMP")
			Assert.IsTrue (output.AsSpan ().IndexOf (GifXmp ("").AsSpan (GifXmp ("").Length - 258)) > 0, "the magic trailer follows the new packet");
	}

	[TestMethod]
	public void Jpeg_CommentEdit_ThatCannotUpdateACopy_IsRefusedWithItsOwnReason ()
	{
		var extended = Jpeg (JpegApp (0xE1, "http://ns.adobe.com/xmp/extension/\0", "0123456789ABCDEF0123456789ABCDEF\0\0\0\x10\0\0\0\0<exif:UserComment/>"), JpegSegment (0xFE, "Old"u8.ToArray ()));
		var full = Jpeg (JpegXmp (XmpPacket (Alt ("exif:UserComment", "Old"))), JpegSegment (0xFE, "Old"u8.ToArray ()));
		var gif = Gif (GifComment ("Old"), GifApplication ("XMP DataXMP", "<x exif:UserComment='Old'/>"u8.ToArray ()));
		foreach (var (input, name, value, reason) in new[] {
			(extended, "a.jpg", "New", "extended XMP"),
			(full, "a.jpg", new string ('x', 70_000), "no longer fit"),
			(gif, "a.gif", "New", "GIF XMP packet is not well-formed"),
		}) {
			var file = Open (input, name);
			file.Tag!.Comment = value;
			var result = file.SaveToFile ("/" + name, new MockFileSystem ());
			Assert.IsFalse (result.IsSuccess, reason);
			StringAssert.Contains (result.Error, reason);
		}
	}

	[TestMethod]
	public void JpegAndGif_TitleIsNotStored_SoItsCopiesAreNotTouched ()
	{
		var jpeg = Jpeg (JpegXmp (CommentPacket), JpegSegment (0xFE, "Old"u8.ToArray ()));
		var file = Open (jpeg, "a.jpg");
		file.Tag!.Title = "New";
		Assert.IsNull (file.Tag.Title);
		CollectionAssert.AreEqual (jpeg, file.Render ().ToArray ());
	}

	static byte[] JoinGifXmpSubBlocks (byte[] gif)
	{
		var i = gif.AsSpan ().IndexOf ("XMP DataXMP"u8) + 11;
		var joined = new List<byte> ();
		while (gif[i] != 0) {
			joined.AddRange (gif.AsSpan (i + 1, gif[i]).ToArray ());
			i += 1 + gif[i];
		}

		return [.. joined];
	}
}
