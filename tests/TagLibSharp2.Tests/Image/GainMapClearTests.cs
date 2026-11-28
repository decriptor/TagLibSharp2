// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.GainMapTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Clear keeps only the typed gain-map values an HDR decoder needs from the XMP of Ultra HDR, Apple and motion-photo files.</summary>
[TestClass]
[TestCategory ("Unit")]
public class GainMapClearTests
{
	[TestMethod]
	public void Clear_KeepsTheAppleGainMapXmp_AndDropsThePrimarysOther ()
	{
		var secondImage = Jpeg (GpsExif (), JpegXmpBytes (AppleGainMapXmp));
		var input = MpfJpeg (
			Concat (GpsExif (), JpegXmpBytes (XmpPacket (Seq ("dc:creator", "Jane Private")), [0])),
			[], secondImage, out _);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var packets = Packets (output);
		Assert.AreEqual (1, packets.Count, "only the gain-map image keeps XMP");
		StringAssert.Contains (packets[0], "urn:com:apple:photo:2020:aux:hdrgainmap");
		var at = MpfSecondImage (output);
		// Clear writes a new packet with only the Apple gain-map properties ImageIO reads.
		var apple = XDocument.Parse (packets[0].TrimStart ('\uFEFF')).Descendants (Rdf + "Description").Single ();
		XNamespace apdi = "http://ns.apple.com/pixeldatainfo/1.0/";
		XNamespace gainMap = "http://ns.apple.com/HDRGainMap/1.0/";
		Assert.AreEqual ("urn:com:apple:photo:2020:aux:hdrgainmap", apple.Element (apdi + "AuxiliaryImageType")?.Value);
		Assert.AreEqual ("1278226488", apple.Element (apdi + "NativeFormat")?.Value);
		Assert.AreEqual ("1278226488", apple.Element (apdi + "StoredFormat")?.Value);
		Assert.AreEqual ("65536", apple.Element (gainMap + "HDRGainMapVersion")?.Value);
		Assert.AreEqual ("", apple.Attribute (Rdf + "about")?.Value);
		Assert.IsTrue (at > 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("Jane Private"u8) < 0 && output.AsSpan ().IndexOf ("Exif\0\0"u8) < 0);
		Assert.AreEqual (output.Length - at, MpfEntries (output)[1].Size);
	}

	static string UltraHdrPrimary (int gainMapLength) =>
		XmpPacket (
			"   <Container:Directory><rdf:Seq>" +
			"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='Primary' Item:Mime='image/jpeg'/></rdf:li>" +
			$"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Mime='image/jpeg' Item:Length='{gainMapLength}'/></rdf:li>" +
			"</rdf:Seq></Container:Directory>\n" + Seq ("dc:creator", "Jane Private"),
			" xmlns:Container='http://ns.google.com/photos/1.0/container/' xmlns:Item='http://ns.google.com/photos/1.0/container/item/'" +
			" xmlns:hdrgm='http://ns.adobe.com/hdr-gain-map/1.0/' hdrgm:Version='1.0' xmp:Label='private label'");

	// Ultra HDR v1.0 (https://developer.android.com/media/platform/hdr-image-format): the primary's hdrgm:Version
	// and Container:Directory locate the gain map, Item:Length is the gain map's length, and the gain map's
	// hdrgm:* properties are its parameters. Clear keeps exactly those and rewrites Item:Length.
	[TestMethod]
	public void Clear_KeepsUltraHdrGainMapXmp_AndRewritesItemLength ()
	{
		var secondImage = Jpeg (GpsExif (), JpegXmpBytes (UltraHdrGainMap));
		var input = MpfJpeg (Concat (GpsExif (), JpegXmpBytes (UltraHdrPrimary (secondImage.Length))), [], secondImage, out _);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("Jane Private"u8) < 0 && output.AsSpan ().IndexOf ("private label"u8) < 0, "no personal field in any image");
		var packets = Packets (output).Select (p => XDocument.Parse (p.TrimStart ('﻿'))).ToList ();
		Assert.AreEqual (2, packets.Count);
		var primary = packets[0].Descendants (Rdf + "Description").Single ();
		Assert.AreEqual ("1.0", primary.Attribute (Hdrgm + "Version")?.Value);
		var items = packets[0].Descendants (Container + "Item").ToList ();
		Assert.AreEqual (2, items.Count);
		var at = MpfSecondImage (output);
		Assert.AreEqual ((output.Length - at).ToString (System.Globalization.CultureInfo.InvariantCulture), items[1].Attribute (Item + "Length")?.Value, "Item:Length is the new gain-map length");
		Assert.AreEqual (output.Length - at, MpfEntries (output)[1].Size);
		var gainMap = packets[1].Descendants (Rdf + "Description").Single ();
		foreach (var name in new[] { "Version", "GainMapMin", "GainMapMax", "Gamma", "OffsetSDR", "OffsetHDR", "HDRCapacityMin", "HDRCapacityMax", "BaseRenditionIsHDR" })
			Assert.IsNotNull (gainMap.Attribute (Hdrgm + name), name);
		Assert.IsFalse (packets.Any (p => p.Descendants (Dc + "creator").Any ()));
	}

	[TestMethod]
	public void Clear_DropsAnXmpPacketWithNoGainMapProperty ()
	{
		var input = Jpeg (JpegXmpBytes (XmpPacket (Seq ("dc:creator", "Jane"))), JpegSegment (0xFE, "c"u8.ToArray ()));
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();
		Assert.AreEqual (0, Packets (file.Render ().ToArray ()).Count);
	}

	const string Ns = "xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns:hdrgm='http://ns.adobe.com/hdr-gain-map/1.0/' " +
		"xmlns:dc='http://purl.org/dc/elements/1.1/' xmlns:exif='http://ns.adobe.com/exif/1.0/' " +
		"xmlns:Container='http://ns.google.com/photos/1.0/container/' xmlns:Item='http://ns.google.com/photos/1.0/container/item/' " +
		"xmlns:apdi='http://ns.apple.com/pixeldatainfo/1.0/' xmlns:HDRGainMap='http://ns.apple.com/HDRGainMap/1.0/' " +
		"xmlns:Camera='http://ns.google.com/photos/1.0/camera/'";
	const string Kept = "hdrgm:Version='1.0' hdrgm:GainMapMax='2.0' hdrgm:HDRCapacityMax='2.0'";

	static string RdfPacket (string body) => $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF {Ns}>{body}</rdf:RDF></x:xmpmeta>";

	// The reviewer's hostile packets: duplicates, personal data nested inside kept properties, typed nodes,
	// rdf:about text, comments and a second RDF, foreign prefixes. Clear never throws, keeps the gain-map
	// values and nothing else.
	public static IEnumerable<object[]> HostilePackets ()
	{
		yield return ["duplicate attribute across Descriptions", RdfPacket ($"<rdf:Description rdf:about='' {Kept}/><rdf:Description rdf:about='' hdrgm:Version='9.9' dc:format='image/jpeg'/>")];
		yield return ["foreign attributes on a directory item", RdfPacket ($"<rdf:Description rdf:about='' {Kept}><Container:Directory><rdf:Seq><rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='Primary' Item:Mime='image/jpeg' exif:GPSLatitude='47,30.0N' dc:rights='Jane Private'/></rdf:li></rdf:Seq></Container:Directory></rdf:Description>")];
		yield return ["a struct nested in a kept array", RdfPacket ("<rdf:Description rdf:about='' hdrgm:Version='1.0' hdrgm:HDRCapacityMax='2.0'><hdrgm:GainMapMax><rdf:Seq><rdf:li>2.0</rdf:li><rdf:li rdf:parseType='Resource'><dc:creator>Jane Private</dc:creator><exif:GPSLatitude>47,30.0N</exif:GPSLatitude></rdf:li></rdf:Seq></hdrgm:GainMapMax></rdf:Description>")];
		yield return ["a typed node next to the gain map", RdfPacket ($"<rdf:Description rdf:about='' {Kept}/><exif:Thing rdf:about='' exif:GPSLatitude='47,30.0N' dc:rights='Jane Private'/>")];
		yield return ["rdf:about text, a comment and trailing text", "<?xpacket begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'?>" + RdfPacket ($"<!-- Jane Private 47N --><rdf:Description rdf:about='Jane Private' {Kept}/>") + "<?xpacket end='w'?>Jane Private 47N"];
		yield return ["rdf:ID on the Description", RdfPacket ($"<rdf:Description rdf:about='' rdf:ID='JanePrivate' {Kept}/>")];
		yield return ["a second RDF", $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF {Ns}><rdf:Description rdf:about='' {Kept}/></rdf:RDF><rdf:RDF {Ns}><rdf:Description rdf:about='' dc:rights='Jane Private'/></rdf:RDF></x:xmpmeta>"];
		yield return ["hdrgm prefix bound to another namespace", "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:hdrgm='http://example.com/other/' xmlns:g='http://ns.adobe.com/hdr-gain-map/1.0/' hdrgm:Creator='Jane Private' g:Version='1.0' g:GainMapMax='2.0' g:HDRCapacityMax='2.0'/></rdf:RDF></x:xmpmeta>"];
		yield return ["attribute and element forms of one property", RdfPacket ($"<rdf:Description rdf:about='' {Kept} dc:format='x'/><rdf:Description rdf:about=''><hdrgm:Version>9.9</hdrgm:Version></rdf:Description>")];
		yield return ["1,500 foreign properties", RdfPacket ($"<rdf:Description rdf:about='' {Kept}>" + string.Concat (Enumerable.Range (0, 1500).Select (n => $"<dc:x{n}>Jane Private</dc:x{n}>")) + "</rdf:Description>")];
		yield return ["a value with markup inside", RdfPacket ($"<rdf:Description rdf:about='' hdrgm:Version='1.0' hdrgm:HDRCapacityMax='2.0'><hdrgm:GainMapMax>2.0<dc:creator>Jane Private</dc:creator></hdrgm:GainMapMax></rdf:Description>")];
	}

	[TestMethod]
	[DynamicData (nameof (HostilePackets))]
	public void Clear_OfAHostileGainMapPacket_KeepsOnlyTypedGainMapValues (string shape, string packet)
	{
		var input = Jpeg (JpegXmpBytes (packet), JpegSegment (0xFE, "c"u8.ToArray ()));
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var output = file.Render ().ToArray ();
		Assert.IsNotNull (Open (output, "a.jpg").ImageProperties, shape);
		foreach (var personal in new[] { "Jane", "47,30.0N", "47N", "image/jpeg' dc", "dc:", "exif:", "9.9", "rdf:ID", "Creator" })
			Assert.IsTrue (output.AsSpan ().IndexOf (Encoding.ASCII.GetBytes (personal)) < 0, $"{shape}: '{personal}' survived Clear");
		var kept = XDocument.Parse (Packets (output).Single ().TrimStart ('\uFEFF'));
		var description = kept.Descendants (Rdf + "Description").Single ();
		Assert.AreEqual ("", description.Attribute (Rdf + "about")?.Value, shape);
		Assert.AreEqual ("1.0", description.Attribute (Hdrgm + "Version")?.Value, shape);
		Assert.IsTrue (description.Attributes ().All (a => a.IsNamespaceDeclaration || a.Name.Namespace == Hdrgm || a.Name == Rdf + "about"), shape);
		Assert.IsTrue (description.Elements ().All (e => e.Name.Namespace == Hdrgm || e.Name.Namespace == Container), shape);
	}

	[TestMethod]
	public void Clear_KeepsAPerChannelArray_AndDropsAMalformedOne ()
	{
		var packet = RdfPacket ("<rdf:Description rdf:about='' hdrgm:Version='1.0' hdrgm:HDRCapacityMax='2.5'>" +
			"<hdrgm:GainMapMin><rdf:Seq><rdf:li>0</rdf:li><rdf:li>0.1</rdf:li><rdf:li>0</rdf:li></rdf:Seq></hdrgm:GainMapMin>" +
			"<hdrgm:GainMapMax><rdf:Seq><rdf:li>2</rdf:li><rdf:li>2.5</rdf:li></rdf:Seq></hdrgm:GainMapMax>" +
			"<hdrgm:Gamma>not a number</hdrgm:Gamma><hdrgm:OffsetSDR>0.1<rdf:Description dc:x='y'/></hdrgm:OffsetSDR></rdf:Description>");
		var file = Open (Jpeg (JpegXmpBytes (packet)), "a.jpg");
		file.Tag!.Clear ();
		var description = XDocument.Parse (Packets (file.Render ().ToArray ()).Single ().TrimStart ('\uFEFF')).Descendants (Rdf + "Description").Single ();
		Assert.AreSequenceEqual (["0", "0.1", "0"], description.Element (Hdrgm + "GainMapMin")!.Descendants (Rdf + "li").Select (l => l.Value).ToList ());
		Assert.IsNull (description.Element (Hdrgm + "GainMapMax"), "two values is neither one nor three");
		Assert.IsNull (description.Element (Hdrgm + "Gamma"));
		Assert.IsNull (description.Attribute (Hdrgm + "Gamma"));
		Assert.IsNull (description.Attribute (Hdrgm + "OffsetSDR"), "a value with markup inside is not a Real");
	}

	[TestMethod]
	public void Clear_IgnoresGainMapPropertiesOfANestedDescription ()
	{
		// Only a top-level rdf:Description describes the image; one nested in another property does not.
		var packet = RdfPacket ("<rdf:Description rdf:about=''><dc:source><rdf:Description hdrgm:Version='1.0' hdrgm:GainMapMax='2' hdrgm:HDRCapacityMax='2'/></dc:source></rdf:Description>");
		var file = Open (Jpeg (JpegXmpBytes (packet), JpegSegment (0xFE, "c"u8.ToArray ())), "a.jpg");
		file.Tag!.Clear ();
		Assert.AreEqual (0, Packets (file.Render ().ToArray ()).Count);
	}

	[TestMethod]
	public void Clear_RewritesAnItemLengthWrittenAsAnElement ()
	{
		// exiftool writes Container:Item as a resource with Item:* child elements.
		var gainMap = Jpeg (GpsExif (), JpegXmpBytes (UltraHdrGainMap));
		var primary = RdfPacket ("<rdf:Description rdf:about='' hdrgm:Version='1.0'><Container:Directory><rdf:Seq>" +
			"<rdf:li rdf:parseType='Resource'><Container:Item rdf:parseType='Resource'><Item:Semantic>Primary</Item:Semantic><Item:Mime>image/jpeg</Item:Mime></Container:Item></rdf:li>" +
			$"<rdf:li rdf:parseType='Resource'><Container:Item rdf:parseType='Resource'><Item:Semantic>GainMap</Item:Semantic><Item:Mime>image/jpeg</Item:Mime><Item:Length>{gainMap.Length}</Item:Length></Container:Item></rdf:li>" +
			"</rdf:Seq></Container:Directory></rdf:Description>");
		var input = MpfJpeg (JpegXmpBytes (primary), [], gainMap, out _);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();
		var items = XDocument.Parse (Packets (output)[0].TrimStart ('\uFEFF')).Descendants (Container + "Item").ToList ();
		Assert.AreEqual ((output.Length - MpfSecondImage (output)).ToString (System.Globalization.CultureInfo.InvariantCulture), items[1].Attribute (Item + "Length")?.Value);
		Assert.AreEqual ("GainMap", items[1].Attribute (Item + "Semantic")?.Value);
	}

	// Ultra HDR with a motion photo: Primary, GainMap (the MPF image), MotionPhoto (a video after the last MPF
	// image, which Clear copies as it is). Readers find the video by summing the primary's length and the
	// preceding items' lengths, so the gain map's Item:Length follows its new length and the video's stays.
	[TestMethod]
	public void Clear_OfAMotionPhoto_RewritesTheGainMapLength_AndKeepsTheVideosLength ()
	{
		var video = Enumerable.Range (0, 64).Select (n => (byte)(n + 1)).ToArray ();
		var gainMap = Jpeg (GpsExif (), JpegXmpBytes (UltraHdrGainMap));
		var primary = RdfPacket ("<rdf:Description rdf:about='' hdrgm:Version='1.0' Camera:MotionPhoto='1' Camera:MotionPhotoVersion='1' Camera:MotionPhotoPresentationTimestampUs='-1'><Container:Directory><rdf:Seq>" +
			"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='Primary' Item:Mime='image/jpeg'/></rdf:li>" +
			$"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Mime='image/jpeg' Item:Length='{gainMap.Length}'/></rdf:li>" +
			"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='MotionPhoto' Item:Mime='video/mp4' Item:Length='64'/></rdf:li>" +
			"</rdf:Seq></Container:Directory></rdf:Description>");
		var input = Concat (MpfJpeg (JpegXmpBytes (primary), [], gainMap, out _), video);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var items = XDocument.Parse (Packets (output)[0].TrimStart ('\uFEFF')).Descendants (Container + "Item").ToList ();
		Assert.AreEqual (3, items.Count);
		var gainMapLength = int.Parse (items[1].Attribute (Item + "Length")!.Value, System.Globalization.CultureInfo.InvariantCulture);
		Assert.AreEqual ("64", items[2].Attribute (Item + "Length")?.Value);
		Assert.AreSequenceEqual (["Primary", "GainMap", "MotionPhoto"], items.Select (i => i.Attribute (Item + "Semantic")?.Value).ToList ());
		Assert.AreSequenceEqual (["image/jpeg", "image/jpeg", "video/mp4"], items.Select (i => i.Attribute (Item + "Mime")?.Value).ToList ());
		var primaryLength = MpfSecondImage (output);
		Assert.IsTrue (gainMapLength < gainMap.Length, "the gain map shrank");
		CollectionAssert.AreEqual (video, output[(primaryLength + gainMapLength)..], "summing the lengths lands on the video");
		var description = XDocument.Parse (Packets (output)[0].TrimStart ('\uFEFF')).Descendants (Rdf + "Description").Single ();
		Assert.AreEqual ("1", description.Attribute (Camera + "MotionPhoto")?.Value, "a cleared motion photo is still one");
		Assert.AreEqual ("1", description.Attribute (Camera + "MotionPhotoVersion")?.Value);
		Assert.AreEqual ("-1", description.Attribute (Camera + "MotionPhotoPresentationTimestampUs")?.Value);
	}

	[TestMethod]
	public void Clear_KeepsTheGainMapBehindASpacedXmpSignature ()
	{
		var segment = JpegSegment (0xE1, Concat ("http://ns.adobe.com/xap/1.0/ \0"u8.ToArray (), Encoding.UTF8.GetBytes (RdfPacket ($"<rdf:Description rdf:about='' {Kept} dc:format='x'/>"))));
		var file = Open (Jpeg (segment, JpegSegment (0xFE, "c"u8.ToArray ())), "a.jpg");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();
		var at = output.AsSpan ().IndexOf ("http://ns.adobe.com/xap/1.0/ \0"u8);
		Assert.IsTrue (at > 0, "the gain-map packet is kept behind the same signature");
		Assert.IsTrue (output.AsSpan ().IndexOf ("hdrgm:Version=\"1.0\""u8) > at);
		Assert.IsTrue (output.AsSpan ().IndexOf ("dc:format"u8) < 0);
	}

	[TestMethod]
	public void Clear_OfAGainMapPacketThatWouldOutgrowItsSegment_DropsItAndStillReadsBack ()
	{
		var input = Jpeg (JpegXmpBytes (OutgrowingPacket ()), JpegSegment (0xFE, "c"u8.ToArray ()));
		Assert.IsTrue (input.Length < 65_000);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();
		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, save.Error);
		var output = file.Render ().ToArray ();
		Assert.IsNotNull (Open (output, "a.jpg").ImageProperties);
		Assert.AreEqual (0, Packets (output).Count);
	}

	static readonly XNamespace Camera = "http://ns.google.com/photos/1.0/camera/";
	// RDF allows an rdf:li's struct value as rdf:li > rdf:Description (libultrahdr reads it, as does exiftool). The
	// item keeps its slot: the first item is the primary and readers sum the lengths before an item to find it.
	[TestMethod]
	public void Clear_ReadsADirectoryItemInsideAnRdfDescription_AndKeepsItsSlot ()
	{
		var gainMap = Jpeg (GpsExif (), JpegXmpBytes (UltraHdrGainMap));
		var primary = RdfPacket ("<rdf:Description rdf:about='' hdrgm:Version='1.0'><Container:Directory><rdf:Seq>" +
			"<rdf:li><rdf:Description><Container:Item Item:Semantic='Primary' Item:Mime='image/jpeg'/></rdf:Description></rdf:li>" +
			$"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Mime='image/jpeg' Item:Length='{gainMap.Length}'/></rdf:li>" +
			"</rdf:Seq></Container:Directory></rdf:Description>");
		var file = Open (MpfJpeg (Concat (GpsExif (), JpegXmpBytes (primary)), [], gainMap, out _), "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var items = XDocument.Parse (Packets (output)[0].TrimStart ('\uFEFF')).Descendants (Container + "Item").ToList ();
		Assert.AreSequenceEqual (["Primary", "GainMap"], items.Select (i => i.Attribute (Item + "Semantic")?.Value).ToList ());
		var gainMapLength = output.Length - MpfSecondImage (output);
		Assert.IsTrue (gainMapLength < gainMap.Length, "the gain map shrank");
		Assert.AreEqual (gainMapLength.ToString (System.Globalization.CultureInfo.InvariantCulture), items[1].Attribute (Item + "Length")?.Value);
	}

	// Item:Semantic and Item:Mime are enumerated (Ultra HDR v1.0, Motion Photo 1.0). Any other value is free text:
	// it is dropped, and the item keeps its slot and its length so the offsets of the later items stay right.
	[TestMethod]
	public void Clear_DropsAnItemValueTheFormatsDoNotEnumerate_AndKeepsTheItemsLength ()
	{
		var video = Enumerable.Range (0, 64).Select (n => (byte)(n + 1)).ToArray ();
		var gainMap = Jpeg (GpsExif (), JpegXmpBytes (UltraHdrGainMap));
		var primary = RdfPacket ("<rdf:Description rdf:about='' hdrgm:Version='1.0'><Container:Directory><rdf:Seq>" +
			"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='JanePrivate' Item:Mime='jane.private/lat-47.30N'/></rdf:li>" +
			$"<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Mime='image/jpeg' Item:Length='{gainMap.Length}'/></rdf:li>" +
			"<rdf:li rdf:parseType='Resource'><Container:Item rdf:parseType='Resource'><Item:Semantic>HomeAtElmStreet</Item:Semantic><Item:Mime>video/mp4</Item:Mime><Item:Length>64</Item:Length></Container:Item></rdf:li>" +
			"</rdf:Seq></Container:Directory></rdf:Description>");
		var file = Open (Concat (MpfJpeg (JpegXmpBytes (primary), [], gainMap, out _), video), "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		foreach (var personal in new[] { "Jane", "jane", "47.30N", "HomeAtElm" })
			Assert.IsTrue (output.AsSpan ().IndexOf (Encoding.ASCII.GetBytes (personal)) < 0, $"'{personal}' survived Clear");
		var items = XDocument.Parse (Packets (output)[0].TrimStart ('\uFEFF')).Descendants (Container + "Item").ToList ();
		Assert.AreEqual (3, items.Count, "every slot is kept");
		Assert.IsFalse (items[0].Attributes ().Any (), "the primary keeps its slot with no free text");
		Assert.AreEqual ("video/mp4", items[2].Attribute (Item + "Mime")?.Value);
		Assert.AreEqual ("64", items[2].Attribute (Item + "Length")?.Value);
		var gainMapLength = int.Parse (items[1].Attribute (Item + "Length")!.Value, System.Globalization.CultureInfo.InvariantCulture);
		CollectionAssert.AreEqual (video, output[(MpfSecondImage (output) + gainMapLength)..], "summing the lengths lands on the video");
	}

	// One row per value check of the rebuild: each check alone keeps "Jane Private" (or NaN, a DTD, a second
	// directory) out of the cleared file. Every row keeps the valid gain-map values next to the hostile one.
	public static IEnumerable<object[]> WrongShapes ()
	{
		const string Max = "hdrgm:GainMapMax='2.0' hdrgm:HDRCapacityMax='2.0'";
		const string DirectoryFormat = "<Container:Directory><rdf:Seq><rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='Primary' Item:Mime='image/jpeg'/></rdf:li>{0}</rdf:Seq></Container:Directory>";
		static string Directory (string items) => string.Format (System.Globalization.CultureInfo.InvariantCulture, DirectoryFormat, items);
		yield return ["Item:Mime", RdfPacket ($"<rdf:Description rdf:about='' {Kept}>{Directory ("<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Mime='Jane/Private' Item:Length='9'/></rdf:li>")}</rdf:Description>"), "Jane", true];
		yield return ["Item:Semantic", RdfPacket ($"<rdf:Description rdf:about='' {Kept}>{Directory ("<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='JanePrivate' Item:Mime='image/jpeg' Item:Length='9'/></rdf:li>")}</rdf:Description>"), "Jane", true];
		yield return ["Item:Length", RdfPacket ($"<rdf:Description rdf:about='' {Kept}>{Directory ("<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Mime='image/jpeg' Item:Length='JanePrivate'/></rdf:li>")}</rdf:Description>"), "Jane", true];
		yield return ["apdi:AuxiliaryImageType", RdfPacket ($"<rdf:Description rdf:about='' {Kept} apdi:AuxiliaryImageType='JanePrivate'/>"), "Jane", true];
		yield return ["apdi:NativeFormat (Integer)", RdfPacket ($"<rdf:Description rdf:about='' {Kept}><apdi:NativeFormat>Jane Private</apdi:NativeFormat></rdf:Description>"), "Jane", true];
		yield return ["hdrgm:BaseRenditionIsHDR (Boolean)", RdfPacket ($"<rdf:Description rdf:about='' {Kept} hdrgm:BaseRenditionIsHDR='JanePrivate'/>"), "Jane", true];
		yield return ["hdrgm:Version", RdfPacket ($"<rdf:Description rdf:about='' {Max} hdrgm:Version='Jane Private'/>"), "Jane", true];
		yield return ["NaN as a Real", RdfPacket ($"<rdf:Description rdf:about='' {Kept} hdrgm:HDRCapacityMin='NaN'/>"), "NaN", true];
		yield return ["Infinity as a Real", RdfPacket ($"<rdf:Description rdf:about='' {Kept} hdrgm:Gamma='Infinity'/>"), "Infinity", true];
		yield return ["a second Container:Directory", RdfPacket ($"<rdf:Description rdf:about='' {Kept}><Container:Directory><rdf:Seq/></Container:Directory>{Directory ("<rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Length='424242'/></rdf:li>")}</rdf:Description>"), "424242", true];
		yield return ["a directory slot with no item", RdfPacket ($"<rdf:Description rdf:about='' {Kept}>{Directory ("<rdf:li rdf:parseType='Resource'><dc:title>Jane Private</dc:title></rdf:li><rdf:li rdf:parseType='Resource'><Container:Item Item:Semantic='GainMap' Item:Length='424242'/></rdf:li>")}</rdf:Description>"), "424242", true];
		yield return ["Camera:MotionPhoto (Integer)", RdfPacket ($"<rdf:Description rdf:about='' {Kept} Camera:MotionPhoto='-1' Camera:MotionPhotoVersion='Jane'/>"), "Camera", true];
		yield return ["Camera:MotionPhotoPresentationTimestampUs (Long)", RdfPacket ($"<rdf:Description rdf:about='' {Kept} Camera:MotionPhotoPresentationTimestampUs='-Jane'/>"), "Camera", true];
		yield return ["HDRGainMap:HDRGainMapHeadroom (Real)", RdfPacket ($"<rdf:Description rdf:about='' {Kept}><HDRGainMap:HDRGainMapHeadroom>Jane Private</HDRGainMap:HDRGainMapHeadroom></rdf:Description>"), "Jane", true];
		yield return ["a DTD", "<!DOCTYPE x:xmpmeta [<!ENTITY e 'Jane Private'>]>" + RdfPacket ($"<rdf:Description rdf:about='' {Kept}/>"), "Jane", false];
	}

	[TestMethod]
	[DynamicData (nameof (WrongShapes))]
	public void Clear_DropsAValueOfTheWrongShape_AndKeepsTheRest (string shape, string packet, string marker, bool keepsPacket)
	{
		var file = Open (Jpeg (JpegXmpBytes (packet), JpegSegment (0xFE, "c"u8.ToArray ())), "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf (Encoding.ASCII.GetBytes (marker)) < 0, $"{shape}: '{marker}' survived Clear");
		var packets = Packets (output);
		Assert.AreEqual (keepsPacket ? 1 : 0, packets.Count, shape);
		if (keepsPacket)
			Assert.AreEqual ("2.0", XDocument.Parse (packets[0].TrimStart ('\uFEFF')).Descendants (Rdf + "Description").Single ().Attribute (Hdrgm + "GainMapMax")?.Value, shape);
	}

	// Motion Photo 1.0: Camera:MotionPhoto and Camera:MotionPhotoVersion are Integers and
	// Camera:MotionPhotoPresentationTimestampUs a Long (-1 when unset). Clear keeps them so the photo is still one.
	[TestMethod]
	[DataRow ("MotionPhoto", "1", true)]
	[DataRow ("MotionPhoto", "-1", false)]
	[DataRow ("MotionPhotoVersion", "1", true)]
	[DataRow ("MotionPhotoVersion", "1.0", false)]
	[DataRow ("MotionPhotoPresentationTimestampUs", "-1", true)]
	[DataRow ("MotionPhotoPresentationTimestampUs", "1500000", true)]
	[DataRow ("MotionPhotoPresentationTimestampUs", "-", false)]
	[DataRow ("MotionPhotoPresentationTimestampUs", "--1", false)]
	[DataRow ("MotionPhotoPresentationTimestampUs", "1.5", false)]
	public void Clear_KeepsATypedMotionPhotoProperty (string name, string value, bool kept)
	{
		var file = Open (Jpeg (JpegXmpBytes (RdfPacket ($"<rdf:Description rdf:about='' Camera:{name}='{value}' dc:title='x'/>")), JpegSegment (0xFE, "c"u8.ToArray ())), "a.jpg");
		file.Tag!.Clear ();

		var packets = Packets (file.Render ().ToArray ());

		Assert.AreEqual (kept ? 1 : 0, packets.Count);
		if (kept)
			Assert.AreEqual (value, XDocument.Parse (packets[0].TrimStart ('\uFEFF')).Descendants (Rdf + "Description").Single ().Attribute (Camera + name)?.Value);
	}

	// Ultra HDR v1.0 and Motion Photo 1.0 enumerate Item:Semantic and Item:Mime; each enumerated value survives Clear.
	[TestMethod]
	[DataRow ("Mime", "image/jpeg")]
	[DataRow ("Mime", "image/heic")]
	[DataRow ("Mime", "image/avif")]
	[DataRow ("Mime", "video/mp4")]
	[DataRow ("Mime", "video/quicktime")]
	[DataRow ("Semantic", "Primary")]
	[DataRow ("Semantic", "GainMap")]
	[DataRow ("Semantic", "MotionPhoto")]
	public void Clear_KeepsEveryEnumeratedItemValue (string field, string value)
	{
		var packet = RdfPacket ($"<rdf:Description rdf:about='' {Kept}><Container:Directory><rdf:Seq><rdf:li rdf:parseType='Resource'><Container:Item Item:{field}='{value}' Item:Length='9'/></rdf:li></rdf:Seq></Container:Directory></rdf:Description>");
		var file = Open (Jpeg (JpegXmpBytes (packet), JpegSegment (0xFE, "c"u8.ToArray ())), "a.jpg");
		file.Tag!.Clear ();

		var item = XDocument.Parse (Packets (file.Render ().ToArray ()).Single ().TrimStart ('﻿')).Descendants (Container + "Item").Single ();

		Assert.AreEqual (value, item.Attribute (Item + field)?.Value);
	}
}
