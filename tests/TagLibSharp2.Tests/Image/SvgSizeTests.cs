// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageAssert;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>SVG width and height from the root element or its viewBox, decoding only the root start tag.</summary>
[TestClass]
[TestCategory ("Unit")]
public partial class SvgSizeTests
{
	[TestMethod]
	public void Svg_ReadsScientificLengthsNamespacedRootsUtf16AndALongPrologue ()
	{
		AssertSize (Encoding.UTF8.GetBytes ("""<svg width="1e2" height="50"></svg>"""), "a.svg", 100, 50);
		AssertSize (Encoding.UTF8.GetBytes ("""<svg:svg width="10" height="20"></svg:svg>"""), "a.svg", 10, 20);
		var utf16 = Encoding.Unicode.GetPreamble ().Concat (Encoding.Unicode.GetBytes ("""<svg width="4" height="6"></svg>""")).ToArray ();
		AssertSize (utf16, "a.svg", 4, 6);
		var late = Encoding.UTF8.GetBytes ("<!--" + new string ('x', 9000) + """--><svg width="3" height="4"></svg>""");
		AssertSize (late, "a.svg", 3, 4);
		AssertSize (Encoding.UTF8.GetBytes ("""<svg width="1em" height="1em" viewBox="0 0 9 8"></svg>"""), "a.svg", 9, 8);
	}

	public static IEnumerable<object[]> SvgThatIsLarge ()
	{
		yield return ["root after a 60 KB comment", Encoding.UTF8.GetBytes ("<?xml version='1.0'?><!--" + new string ('x', 60_000) + """--><svg width="7" height="9">""" + new string ('y', Mib) + "</svg>")];
		yield return ["UTF-16 root after a doctype", Concat ([0xFF, 0xFE], Encoding.Unicode.GetBytes ("""<!DOCTYPE svg [<!ENTITY a ">">]><svg width="7" height="9">""" + new string ('y', Mib / 2) + "</svg>"))];
		yield return ["UTF-16BE", Concat ([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes ("""<svg viewBox="0 0 7 9">""" + new string ('y', Mib / 2) + "</svg>"))];
		yield return ["UTF-8 BOM and a prefix", Concat ([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes ("""<svg:svg xmlns:svg="http://www.w3.org/2000/svg" width="7" height="9" title='a > b'>""" + new string ('y', Mib) + "</svg:svg>"))];
	}

	[TestMethod]
	[DynamicData (nameof (SvgThatIsLarge))]
	public void Svg_DetectionCopiesNothing_AndReadingDecodesOnlyTheRootTag (string name, byte[] data)
	{
		_ = MediaFile.DetectFormat (data);
		var format = MediaFile.DetectFormat (data);
		Bounds.AssertAllocatesAtMost (Noise, () => format = MediaFile.DetectFormat (data), name);
		Assert.AreEqual (MediaFormat.Svg, format, name);

		ImageProperties? properties = null;
		_ = ImageHeader.TryRead (data, MediaFormat.Svg, out _);
		var read = false;
		Bounds.AssertAllocatesAtMost (2 * Noise, () => read = ImageHeader.TryRead (data, MediaFormat.Svg, out properties), name);
		Assert.IsTrue (read, name);
		Assert.AreEqual (7, properties!.Value.Width, name);
		Assert.AreEqual (9, properties.Value.Height, name);
	}

	[TestMethod]
	public void Read_Svg_ReportsWidthOrViewBox ()
	{
		var sized = System.Text.Encoding.UTF8.GetBytes ("""<svg stroke-width="1" width="12px" height="8"></svg>""");
		AssertImage (sized, "drawing.svg", MediaFormat.Svg, 12, 8, "SVG");

		var box = System.Text.Encoding.UTF8.GetBytes ("""<?xml version="1.0"?><svg viewBox="0 0 20 11"></svg>""");
		AssertImage (box, "drawing.svg", MediaFormat.Svg, 20, 11, "SVG");

		var bare = System.Text.Encoding.UTF8.GetBytes ("<svg></svg>");
		var opened = MediaFile.ReadFromData (bare, "drawing.svg");
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.Svg, opened.Format);
		Assert.IsNull (((IMediaFile)opened.File!).ImageProperties);

		var percent = System.Text.Encoding.UTF8.GetBytes ("""<svg width="100%" height="50%" viewBox="0 0 30.5 10"></svg>""");
		AssertImage (percent, "drawing.svg", MediaFormat.Svg, 30, 10, "SVG");

		var named = System.Text.Encoding.UTF8.GetBytes ("""<svg stroke-viewBox="0 0 1 1" viewBox="0 0 9 8"></svg>""");
		AssertImage (named, "drawing.svg", MediaFormat.Svg, 9, 8, "SVG");

		var bom = new byte[] { 0xEF, 0xBB, 0xBF };
		var body = System.Text.Encoding.UTF8.GetBytes ("""<svg width="4" height="5"></svg>""");
		var withBom = new byte[bom.Length + body.Length];
		bom.CopyTo (withBom, 0);
		body.CopyTo (withBom, bom.Length);
		AssertImage (withBom, "drawing.svg", MediaFormat.Svg, 4, 5, "SVG");

		var prologue = new string (' ', 80);
		var late = System.Text.Encoding.UTF8.GetBytes ($"""<?xml version="1.0"?>{prologue}<svg width="3" height="4"></svg>""");
		AssertImage (late, "drawing.svg", MediaFormat.Svg, 3, 4, "SVG");

		var comment = System.Text.Encoding.UTF8.GetBytes ("""<!-- <svg width="9" height="9"> --><svg width="6" height="7"></svg>""");
		AssertImage (comment, "drawing.svg", MediaFormat.Svg, 6, 7, "SVG");
		var declared = System.Text.Encoding.UTF8.GetBytes ("""<?xml version="1.0"?><!-- note --><!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"><svg width="2" height="3"></svg>""");
		AssertImage (declared, "drawing.svg", MediaFormat.Svg, 2, 3, "SVG");
		var inches = System.Text.Encoding.UTF8.GetBytes ("""<svg width="1in" height="2in"></svg>""");
		AssertImage (inches, "drawing.svg", MediaFormat.Svg, 96, 192, "SVG");
		var points = System.Text.Encoding.UTF8.GetBytes ("""<svg width="12pt" height="1pc"></svg>""");
		AssertImage (points, "drawing.svg", MediaFormat.Svg, 16, 16, "SVG");
		var relative = System.Text.Encoding.UTF8.GetBytes ("""<svg width="1em" height="1em" viewBox="0 0 9 8"></svg>""");
		AssertImage (relative, "drawing.svg", MediaFormat.Svg, 9, 8, "SVG");
		var commas = System.Text.Encoding.UTF8.GetBytes ("""<svg viewBox="0,0,100,50"></svg>""");
		AssertImage (commas, "drawing.svg", MediaFormat.Svg, 100, 50, "SVG");

		// A '>' inside an earlier quoted attribute is not the end of the start tag.
		var quoted = System.Text.Encoding.UTF8.GetBytes ("""<svg title="a > b" width="7" height="9"></svg>""");
		AssertImage (quoted, "drawing.svg", MediaFormat.Svg, 7, 9, "SVG");
	}
}
