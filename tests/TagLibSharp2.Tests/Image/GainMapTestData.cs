// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>The gain-map packets and images the Clear tests of HDR files share.</summary>
static class GainMapTestData
{
	// The gain-map image's XMP as ImageIO writes it for an HDRGainMap auxiliary image.
	internal const string AppleGainMapXmp = """
		<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="XMP Core 6.0.0">
		   <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
		      <rdf:Description rdf:about=""
		            xmlns:apdi="http://ns.apple.com/pixeldatainfo/1.0/"
		            xmlns:HDRGainMap="http://ns.apple.com/HDRGainMap/1.0/">
		         <apdi:NativeFormat>1278226488</apdi:NativeFormat>
		         <apdi:StoredFormat>1278226488</apdi:StoredFormat>
		         <apdi:AuxiliaryImageType>urn:com:apple:photo:2020:aux:hdrgainmap</apdi:AuxiliaryImageType>
		         <HDRGainMap:HDRGainMapVersion>65536</HDRGainMap:HDRGainMapVersion>
		      </rdf:Description>
		   </rdf:RDF>
		</x:xmpmeta>

		""";

	internal static byte[] JpegXmpBytes (string packet, byte[]? tail = null) =>
		JpegSegment (0xE1, Concat ("http://ns.adobe.com/xap/1.0/\0"u8.ToArray (), Encoding.UTF8.GetBytes (packet), tail ?? []));

	internal static byte[] GpsExif () => JpegExif (ExifTiff ([(0x010F, "Canon"), (0x013B, "Jane Private")], [(0x8827, "GPS")]));

	internal static List<string> Packets (byte[] jpeg)
	{
		var packets = new List<string> ();
		var at = 0;
		while (true) {
			var found = jpeg.AsSpan (at).IndexOf ("http://ns.adobe.com/xap/1.0/\0"u8);
			if (found < 0)
				return packets;
			at += found;
			var length = (jpeg[at - 2] << 8) | jpeg[at - 1];
			packets.Add (Encoding.UTF8.GetString (jpeg, at + 29, length - 2 - 29).TrimEnd ('\0', ' ', '\n'));
			at += 29;
		}
	}

	internal const string UltraHdrGainMap = """
		<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
		<rdf:Description rdf:about="" xmlns:hdrgm="http://ns.adobe.com/hdr-gain-map/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/"
		 hdrgm:Version="1.0" hdrgm:GainMapMin="-0.57609993" hdrgm:GainMapMax="4.7090998" hdrgm:Gamma="1" hdrgm:OffsetSDR="0.015625"
		 hdrgm:OffsetHDR="0.015625" hdrgm:HDRCapacityMin="0" hdrgm:HDRCapacityMax="4.7090998" hdrgm:BaseRenditionIsHDR="False">
		<dc:creator><rdf:Seq><rdf:li>Jane Private</rdf:li></rdf:Seq></dc:creator>
		</rdf:Description></rdf:RDF></x:xmpmeta>
		""";

	// Compact directory items grow when written in the format's own form; past 64 KB the packet is dropped.
	internal static string OutgrowingPacket (string attributes = "")
	{
		var items = string.Concat (Enumerable.Range (0, 800).Select (_ => "<r:li><C:Item I:Semantic='GainMap' I:Mime='image/jpeg' I:Length='1'/></r:li>"));
		return "<x:xmpmeta xmlns:x='adobe:ns:meta/'><r:RDF xmlns:r='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns:C='http://ns.google.com/photos/1.0/container/' xmlns:I='http://ns.google.com/photos/1.0/container/item/' xmlns:h='http://ns.adobe.com/hdr-gain-map/1.0/'>" +
			$"<r:Description h:Version='1.0' {attributes}><C:Directory><r:Seq>{items}</r:Seq></C:Directory></r:Description></r:RDF></x:xmpmeta>";
	}
}
