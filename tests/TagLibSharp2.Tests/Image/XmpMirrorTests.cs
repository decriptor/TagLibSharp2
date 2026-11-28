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
using static TagLibSharp2.Tests.Image.GainMapTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Editing a field inside an XMP packet: every property shape, compression, the packet tail, and one pass over the text.</summary>
[TestClass]
[TestCategory ("Unit")]
public class XmpMirrorTests
{
	public static IEnumerable<object[]> PacketShapes ()
	{
		yield return ["attribute form", XmpPacket ("", " dc:title='Old &amp; attr' xmp:Label='keep'"), "keep"];
		yield return ["empty description with attribute", XmpPacket ("").Replace ("'>\n  </rdf:Description>", "' dc:title='Old'/>", StringComparison.Ordinal), null!];
		yield return ["two descriptions", XmpPacket (Alt ("dc:title", "One")).Replace ("  </rdf:Description>\n", "  </rdf:Description>\n  <rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/'>" + Alt ("dc:title", "Two") + "  </rdf:Description>\n", StringComparison.Ordinal), null!];
		yield return ["CRLF and a wide character first", XmpPacket ("   <xmp:Label>🎵 日本</xmp:Label>\r\n\t" + Alt ("dc:title", "Old")).Replace ("\n", "\r\n", StringComparison.Ordinal), null!];
		yield return ["rdf as the default namespace", "<x:xmpmeta xmlns:x='adobe:ns:meta/'><RDF xmlns='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><Description about='' xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title><Alt><li xml:lang='x-default'>Old</li></Alt></dc:title></Description></RDF></x:xmpmeta>", null!];
		yield return ["empty property element", XmpPacket ("   <dc:title/>\n"), null!];
		yield return ["byte order mark before the packet", "\uFEFF" + XmpPacket (Alt ("dc:title", "Old")), null!];
		yield return ["simple property text", XmpPacket ("   <dc:title>Old</dc:title>\n"), null!];
		yield return ["property declares its own namespace", XmpPacket ("   <dc:title xmlns:dc='http://purl.org/dc/elements/1.1/'><rdf:Alt><rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></dc:title>\n"), "xmlns:dc='http://purl.org/dc/elements/1.1/'>"];
		yield return ["RDF prefix declared on the property", XmpPacket ("   <dc:title xmlns:r='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><r:Alt><r:li xml:lang='x-default'>Old</r:li></r:Alt></dc:title>\n"), "xmlns:r="];
		yield return ["rdf prefix rebound on the property", XmpPacket ("   <dc:title xmlns:rdf='urn:other'><rdf:x>Old</rdf:x></dc:title>\n"), "xmlns:rdf='urn:other'"];
		yield return ["resource attribute on the property", XmpPacket ("   <dc:title rdf:resource='http://example.com/old'/>\n"), null!];
		yield return ["parseType on the property", XmpPacket ("   <dc:title rdf:parseType='Literal'><b>Old</b></dc:title>\n"), null!];
		yield return ["end tag with white space", XmpPacket ("   <dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></dc:title\n   >\n"), "</dc:title\n   >"];
		yield return ["nested description is not a property", XmpPacket ("   <xmp:Thing><rdf:Description><dc:title>nested</dc:title></rdf:Description></xmp:Thing>\n" + Alt ("dc:title", "Old")), "nested"];
	}

	[TestMethod]
	[DynamicData (nameof (PacketShapes))]
	public void Edit_UpdatesTheTitleInEveryPacketShape (string shape, string packet, string? kept)
	{
		var file = Open (Png (Xmp (packet)), "a.png");
		file.Tag!.Title = "New";

		var save = file.SaveToFile ("/a.png", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var updated = PngTexts (file.Render ().ToArray (), "XML:com.adobe.xmp").Single ();
		Assert.AreEqual (packet.StartsWith ('\uFEFF'), updated.StartsWith ('\uFEFF'), "the byte order mark is kept");
		var doc = XDocument.Parse (updated.TrimStart ('\uFEFF'));
		var titles = doc.Descendants ()
			.Where (e => e.Name.LocalName == "Description" && e.Name.Namespace == Rdf)
			.Where (e => e.Parent?.Name.LocalName == "RDF")
			.SelectMany (d => d.Elements (Dc + "title").Select (t => t.Value).Concat (d.Attributes (Dc + "title").Select (a => a.Value)))
			.ToList ();
		Assert.AreEqual (1, titles.Count, $"{shape}: {updated}");
		Assert.AreEqual ("New", titles[0], shape);
		foreach (var element in doc.Descendants (Dc + "title").Where (e => e.Parent?.Parent?.Name.LocalName == "RDF")) {
			Assert.AreEqual ("New", element.Element (Rdf + "Alt")?.Element (Rdf + "li")?.Value, $"{shape}: an rdf:Alt in the RDF namespace");
			Assert.IsFalse (element.Attributes ().Any (a => a.Name.Namespace == Rdf), $"{shape}: no rdf attribute contradicts the new content");
		}
		if (kept is not null)
			StringAssert.Contains (updated, kept, shape);

		file.Tag.Title = null;
		var cleared = PngTexts (file.Render ().ToArray (), "XML:com.adobe.xmp").Single ();
		var left = XDocument.Parse (cleared.TrimStart ('\uFEFF')).Descendants ()
			.Where (e => e.Name.LocalName == "Description" && e.Parent?.Name.LocalName == "RDF")
			.SelectMany (d => d.Elements (Dc + "title").Cast<object> ().Concat (d.Attributes (Dc + "title")))
			.Count ();
		Assert.AreEqual (0, left, $"{shape}: {cleared}");
	}

	[TestMethod]
	public void Edit_ZtxtXmpPacket_IsUpdatedAndStaysCompressed ()
	{
		var packet = XmpPacket (Alt ("dc:title", "Old"));
		var file = Open (Png (CompressedText ("XML:com.adobe.xmp", Encoding.UTF8.GetBytes (packet))), "a.png");
		file.Tag!.Title = "New";
		var output = file.Render ().ToArray ();
		Assert.AreEqual ("zTXt", PngChunks (output).Single (c => c.Type is "zTXt" or "iTXt" or "tEXt" && Encoding.Latin1.GetString (c.Payload).StartsWith ("XML:", StringComparison.Ordinal)).Type);
		StringAssert.Contains (PngTexts (output, "XML:com.adobe.xmp").Single (), ">New<");
	}

	// The splice writes each character of the new packet once, however many copies it replaces.
	[TestMethod]
	public void Edit_XmpSplice_IsOnePass ()
	{
		foreach (var n in new[] { 1000, 4000 }) {
			var descriptions = string.Concat (Enumerable.Range (0, n).Select (_ => "<rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/' dc:title='Old'/>"));
			var packet = $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>{descriptions}</rdf:RDF></x:xmpmeta>";
			foreach (var value in new[] { null, "New" }) {
				var tag = new ImageTag (MediaFormat.Png, (ImageStores)0x3F);
				tag.ReadField ((ImageStores)1, "Old");
				tag.Title = value;
				XmpMirror.SpliceWrites = 0;
				Assert.IsTrue (XmpMirror.TryUpdate (packet, tag, "PNG XMP packet", out var updated, out var error), error);
				Assert.AreEqual (updated.Length, XmpMirror.SpliceWrites, $"n={n} value={value}");
				Assert.AreEqual (value is null ? 0 : 1, XDocument.Parse (updated).Descendants ().Count (e => e.Attribute (Dc + "title") is not null));
			}
		}
	}

	// N mirrored XMP copies sit next to each other, so each one is a new piece appended to the last. Appending
	// copies the bytes gathered so far; four times the copies then adds about four times as much, and the bound
	// is three. A list of pieces adds about twice as much.
	[TestMethod]
	[DataRow ("png")]
	[DataRow ("gif")]
	[DataRow ("jpeg")]
	public void Edit_ManyMirroredXmpCopies_AllocatesLinearly (string format)
	{
		var sample = ManyMirroredXmp (format, 2);
		var check = Open (sample, SampleName (format));
		SetMirroredField (check.Tag!, format);
		var saved = check.SaveToFile ("/a" + SampleName (format), new MockFileSystem ());
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var output = check.Render ().ToArray ();
		Assert.AreEqual (2, CountPacketTitles (output, "New"), $"{format}: both packets hold the new value");
		Assert.AreEqual (0, CountPacketTitles (output, "Old"), $"{format}: the old value is gone");
		if (format == "png")
			CollectionAssert.AreEqual (PngPayload (sample, "IDAT"), PngPayload (output, "IDAT"));

		const int n = 200;
		Bounds.AssertLinearAllocation (count => ManyMirroredXmp (format, count), data => {
			var copies = CountPacketTitles (data, "Old");
			var file = Open (data, SampleName (format));
			SetMirroredField (file.Tag!, format);
			var save = file.SaveToFile ("/a" + SampleName (format), new MockFileSystem ());
			Assert.IsTrue (save.IsSuccess, $"{format}: {save.Error}");
			Assert.AreEqual (copies, CountPacketTitles (file.Render ().ToArray (), "New"), $"{format} N={copies}");
		}, n, runs: 3);
	}

	// PNG stores a title, so dc:title is the mirrored copy. JPEG and GIF store a comment, so the copy is UserComment.
	static void SetMirroredField (Tag tag, string format)
	{
		if (format == "png")
			tag.Title = "New";
		else
			tag.Comment = "New";
	}

	static string SampleName (string format) => format switch {
		"gif" => "a.gif",
		"jpeg" => "a.jpg",
		_ => "a.png",
	};

	// Uncompressed copies of one dc:title packet: PNG tEXt, GIF application extension, JPEG APP1.
	static byte[] ManyMirroredXmp (string format, int n)
	{
		var packet = format == "png"
			? "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta>"
			: "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:UserComment><rdf:Alt><rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></exif:UserComment></rdf:Description></rdf:RDF></x:xmpmeta>";
		var chunks = new byte[n][];
		for (var i = 0; i < n; i++) {
			chunks[i] = format switch {
				"gif" => GifXmp (packet),
				"jpeg" => JpegXmp (packet),
				_ => Text ("XML:com.adobe.xmp", packet),
			};
		}

		return format switch {
			"gif" => Gif (chunks),
			"jpeg" => Jpeg (chunks),
			_ => Png (chunks),
		};
	}

	static int CountPacketTitles (byte[] data, string title) =>
		Bytes.Count (data, Encoding.ASCII.GetBytes (">" + title + "<"));

	public static IEnumerable<object[]> PacketTails ()
	{
		yield return ["jpeg", "NUL after the trailer (ImageIO)", new byte[] { 0 }];
		yield return ["jpeg", "line feed and NUL (sample_xmpnullended.jpg)", new byte[] { 0x0A, 0 }];
		yield return ["jpeg", "several NULs and spaces", new byte[] { 0x20, 0, 0, 0 }];
		yield return ["png", "NUL after the trailer", new byte[] { 0 }];
	}

	// XMP Part 1 §7.3: the packet ends with its trailer; bytes after it are not XML and keep their place.
	[TestMethod]
	[DynamicData (nameof (PacketTails))]
	public void CommentEdit_OfAPacketFollowedByNulOrPadding_UpdatesItAndKeepsTheTail (string format, string shape, byte[] tail)
	{
		var packet = XmpPacket (Alt ("exif:UserComment", "Old"));
		byte[] input;
		if (format == "jpeg")
			input = Jpeg (JpegXmpBytes (packet, tail), JpegSegment (0xFE, "Old"u8.ToArray ()));
		else
			input = Png (RawItxt ("XML:com.adobe.xmp", Concat (Encoding.UTF8.GetBytes (packet), tail)), Text ("Comment", "Old"));
		var name = format == "jpeg" ? "a.jpg" : "a.png";
		var file = Open (input, name);
		file.Tag!.Comment = "New";

		var save = file.SaveToFile ("/" + name, new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var output = file.Render ().ToArray ();
		var end = output.AsSpan ().IndexOf ("<?xpacket end='w'?>"u8) + 19;
		Assert.IsTrue (output.AsSpan (end, tail.Length).SequenceEqual (tail), $"{shape}: the tail is kept after the packet");
		Assert.IsTrue (output.AsSpan ().IndexOf (">New<"u8) > 0, shape);
	}
}
