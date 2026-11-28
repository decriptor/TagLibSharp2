// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Png;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

public partial class SvgSizeTests
{
	[TestMethod]
	[DataRow ("   ")]
	[DataRow ("<!--x--><!--y--><svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<!DOCTYPE svg><!--y--><svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<!DOCTYPE svg]><svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<!DOCTYPE svg <svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<svg")]
	[DataRow ("<sv")]
	[DataRow ("<svg:foo width=\"1\" height=\"2\"/>")]
	[DataRow ("<svg title=\"a > b\" width=\"3\" height=\"4\"/>")]
	[DataRow ("<svg width=\"2147483647\" height=\"1\"/>")]
	[DataRow ("<svg width=\"0\" height=\"1\" viewBox=\"0 0 0 5\"/>")]
	[DataRow ("<svg width=\"1cm\" height=\"10mm\"/>")]
	[DataRow ("<svg width=\"1in\" height=\"1pt\"/>")]
	[DataRow ("<svg width=\"1pc\" height=\"1px\"/>")]
	public void Svg_BoundaryDocuments_KeepTheirSize (string markup)
	{
		var data = Encoding.UTF8.GetBytes (markup);
		var read = SvgSize.TryRead (data, out var properties);
		var detect = SvgSize.IsSvg (data);
		Assert.AreEqual (ExpectedSvg (markup), Describe (detect, read, properties), markup);
	}

	[TestMethod]
	public void Svg_Utf16Spaces_AreNotAnImage ()
	{
		var data = Encoding.Unicode.GetPreamble ().Concat (Encoding.Unicode.GetBytes ("   ")).ToArray ();

		Assert.IsFalse (SvgSize.IsSvg (data));
		Assert.IsFalse (SvgSize.TryRead (data, out var properties));
		Assert.IsNull (properties);
	}

	static string Describe (bool detect, bool read, ImageProperties? properties) =>
			$"{detect}|{read}|{(properties is { } size ? $"{size.Width}x{size.Height}" : "none")}";

	static string ExpectedSvg (string markup) => markup switch {
		"   " => "False|False|none",
		"<!--x--><!--y--><svg width=\"1\" height=\"2\"/>" => "True|True|1x2",
		"<!DOCTYPE svg><!--y--><svg width=\"1\" height=\"2\"/>" => "True|True|1x2",
		"<!DOCTYPE svg]><svg width=\"1\" height=\"2\"/>" => "True|True|1x2",
		"<!DOCTYPE svg <svg width=\"1\" height=\"2\"/>" => "False|False|none",
		"<svg" => "True|True|none",
		"<sv" => "False|False|none",
		"<svg:foo width=\"1\" height=\"2\"/>" => "False|False|none",
		"<svg title=\"a > b\" width=\"3\" height=\"4\"/>" => "True|True|3x4",
		"<svg width=\"2147483647\" height=\"1\"/>" => "True|True|2147483647x1",
		"<svg width=\"0\" height=\"1\" viewBox=\"0 0 0 5\"/>" => "True|True|none",
		"<svg width=\"1cm\" height=\"10mm\"/>" => "True|True|38x38",
		"<svg width=\"1in\" height=\"1pt\"/>" => "True|True|96x1",
		"<svg width=\"1pc\" height=\"1px\"/>" => "True|True|16x1",
		_ => "?",
	};

	[TestMethod]
	public void Svg_UnclosedXmlDeclaration_IsNotSvg ()
	{
		Assert.IsFalse (SvgSize.TryRead ("<?xml version=\"1.0\" <svg width=\"1\" height=\"1\"/>"u8, out var properties));
		Assert.IsNull (properties);
	}

	[TestMethod]
	public void Svg_ProcessingInstructionBeforeTheRoot_StillHasTheSize ()
	{
		Assert.IsTrue (SvgSize.TryRead ("<?pi?> <svg width=\"1\" height=\"1\"/>"u8, out var properties));
		Assert.AreEqual ("1x1", properties is { } size ? $"{size.Width}x{size.Height}" : "none");
	}

	[TestMethod]
	public void Svg_UnclosedProcessingInstruction_IsNotSvg ()
	{
		Assert.IsFalse (SvgSize.TryRead ("<?pi <svg width=\"1\" height=\"1\"/>"u8, out var properties));
		Assert.IsNull (properties);
	}

	[TestMethod]
	[DataRow ("<svg width=\"1\" height=\"1\"", "True|True|1x1")]
	[DataRow ("<svg width=\"1e2\" height=\"1\"/>", "True|True|100x1")]
	[DataRow ("<svg width=\"1.5\" height=\"1\"/>", "True|True|2x1")]
	[DataRow ("<svg width=\"50%\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<svg width=\"1em\" height=\"1\"/>", "True|True|none")]
	public void Svg_MoreBoundaries (string markup, string expected)
	{
		var data = Encoding.UTF8.GetBytes (markup);
		var read = SvgSize.TryRead (data, out var properties);
		Assert.AreEqual (expected, $"{SvgSize.IsSvg (data)}|{read}|{(properties is { } size ? $"{size.Width}x{size.Height}" : "none")}");
	}

	[TestMethod]
	[DataRow ("<svg width=\"10\" height", "True|True|none")]
	[DataRow ("<svg width=50% height=1>", "True|True|none")]
	[DataRow ("<svg width=\"1px", "True|True|none")]
	[DataRow ("<svg width=\"10\"", "True|True|none")]
	[DataRow ("<svg width=\"0.4\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<svg width=1.2.3 height=1>", "True|True|1x1")]
	[DataRow ("<svg width=\"1e+2\" height=\"1\"/>", "True|True|100x1")]
	[DataRow ("<svg width = \"2\" height = \"3\"/>", "True|True|2x3")]
	[DataRow ("<svg width=10 height=20>", "True|True|10x20")]
	[DataRow ("<svg width=\"10\" height=\"x\"/>", "True|True|none")]
	[DataRow ("<svg width=\"10 px\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<svg width=\"1e20\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<!DOCTYPE svg \"x\"> <svg width=\"1\" height=\"2\"/>", "True|True|1x2")]
	[DataRow ("<!DOCTYPE svg \">\"> <svg width=\"1\" height=\"2\"/>", "True|True|1x2")]
	[DataRow ("<svg viewBox=\"0 0 4 5", "True|True|none")]
	[DataRow ("<svg viewBox=0 0 4 5>", "True|True|none")]
	[DataRow ("<svg width=\"10\">", "True|True|none")]
	[DataRow ("<svg w=20>", "True|True|none")]
	[DataRow ("<svg width=2 3 height=1>", "True|True|2x1")]
	[DataRow ("<svg width=10", "True|True|none")]
	[DataRow ("<svg width=\"1e", "True|True|none")]
	[DataRow ("<svg width=\"1e2", "True|True|none")]
	[DataRow ("<svg ab5 width=9 xheight", "True|True|none")]
	public void Svg_EdgesASurvivingComparisonChanges (string markup, string expected)
	{
		var data = Encoding.UTF8.GetBytes (markup);
		var read = SvgSize.TryRead (data, out var properties);
		Assert.AreEqual (expected, $"{SvgSize.IsSvg (data)}|{read}|{(properties is { } size ? $"{size.Width}x{size.Height}" : "none")}");
	}
}
