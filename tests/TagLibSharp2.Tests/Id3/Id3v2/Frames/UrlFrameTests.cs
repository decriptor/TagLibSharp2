// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class UrlFrameTests
{
	[TestMethod]
	public void Constructor_SetsProperties ()
	{
		var frame = new UrlFrame ("WOAR", "https://example.com/artist");

		Assert.AreEqual ("WOAR", frame.Id);
		Assert.AreEqual ("https://example.com/artist", frame.Url);
	}

	[TestMethod]
	public void Read_SimpleUrl_ParsesCorrectly ()
	{
		byte[] data = System.Text.Encoding.Latin1.GetBytes ("https://example.com");

		var result = UrlFrame.Read ("WOAR", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("WOAR", result.Frame!.Id);
		Assert.AreEqual ("https://example.com", result.Frame.Url);
	}

	[TestMethod]
	public void Read_WithTrailingNull_TrimsCorrectly ()
	{
		var urlBytes = System.Text.Encoding.Latin1.GetBytes ("https://example.com");
		var data = new byte[urlBytes.Length + 1];
		urlBytes.CopyTo (data, 0);
		data[urlBytes.Length] = 0x00;

		var result = UrlFrame.Read ("WPUB", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("https://example.com", result.Frame!.Url);
	}

	[TestMethod]
	public void Read_EmptyData_ReturnsFailure ()
	{
		var result = UrlFrame.Read ("WCOM", [], Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var original = new UrlFrame ("WOAR", "https://artist.example.com/page");

		var rendered = original.RenderContent ();
		var parsed = UrlFrame.Read ("WOAR", rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("https://artist.example.com/page", parsed.Frame!.Url);
	}

	[TestMethod]
	public void KnownFrameIds_AreValid ()
	{
		// Verify all standard URL frame IDs
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WCOM")); // Commercial information
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WCOP")); // Copyright information
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WOAF")); // Official audio file webpage
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WOAR")); // Official artist webpage
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WOAS")); // Official audio source webpage
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WORS")); // Official internet radio station
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WPAY")); // Payment
		Assert.IsTrue (UrlFrame.IsUrlFrameId ("WPUB")); // Publishers official webpage

		// Non-URL frames
		Assert.IsFalse (UrlFrame.IsUrlFrameId ("TIT2"));
		Assert.IsFalse (UrlFrame.IsUrlFrameId ("WXXX")); // User URL is separate
	}
}

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class UserUrlFrameTests
{
	[TestMethod]
	public void Constructor_SetsProperties ()
	{
		var frame = new UserUrlFrame ("https://example.com", "Homepage");

		Assert.AreEqual ("https://example.com", frame.Url);
		Assert.AreEqual ("Homepage", frame.Description);
		Assert.AreEqual (TextEncodingType.Utf8, frame.Encoding);
	}

	[TestMethod]
	public void FrameId_ReturnsWXXX ()
	{
		Assert.AreEqual ("WXXX", UserUrlFrame.FrameId);
	}

	[TestMethod]
	public void Read_Latin1_ParsesCorrectly ()
	{
		var data = BuildWxxxFrame (TextEncodingType.Latin1, "Homepage", "https://example.com");

		var result = UserUrlFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Homepage", result.Frame!.Description);
		Assert.AreEqual ("https://example.com", result.Frame.Url);
	}

	[TestMethod]
	public void Read_Utf8_ParsesCorrectly ()
	{
		var data = BuildWxxxFrame (TextEncodingType.Utf8, "Hôme Pàge", "https://example.com");

		var result = UserUrlFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Hôme Pàge", result.Frame!.Description);
		Assert.AreEqual ("https://example.com", result.Frame.Url);
	}

	[TestMethod]
	public void Read_EmptyDescription_ParsesCorrectly ()
	{
		var data = BuildWxxxFrame (TextEncodingType.Latin1, "", "https://example.com");

		var result = UserUrlFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Description);
		Assert.AreEqual ("https://example.com", result.Frame.Url);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var result = UserUrlFrame.Read ([0x00], Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var original = new UserUrlFrame ("https://custom.example.com", "Custom Link", TextEncodingType.Utf8);

		var rendered = original.RenderContent ();
		var parsed = UserUrlFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Custom Link", parsed.Frame!.Description);
		Assert.AreEqual ("https://custom.example.com", parsed.Frame.Url);
	}

	[TestMethod]
	public void RenderContent_Utf16_RoundTrips ()
	{
		var original = new UserUrlFrame (
			"https://example.com",
			"Unicode Description 日本語",
			TextEncodingType.Utf16WithBom);

		var rendered = original.RenderContent ();
		var parsed = UserUrlFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Unicode Description 日本語", parsed.Frame!.Description);
		Assert.AreEqual ("https://example.com", parsed.Frame.Url);
	}

	static byte[] BuildWxxxFrame (TextEncodingType encoding, string description, string url)
	{
		using var builder = new BinaryDataBuilder ();

		builder.Add ((byte)encoding);

		if (encoding == TextEncodingType.Utf16WithBom || encoding == TextEncodingType.Utf16BE) {
			var enc = encoding == TextEncodingType.Utf16WithBom
				? System.Text.Encoding.Unicode
				: System.Text.Encoding.BigEndianUnicode;

			if (encoding == TextEncodingType.Utf16WithBom)
				builder.Add (new byte[] { 0xFF, 0xFE }); // BOM

			builder.Add (enc.GetBytes (description));
			builder.Add (new byte[] { 0x00, 0x00 }); // Double null terminator
		} else {
			var enc = encoding == TextEncodingType.Latin1
				? System.Text.Encoding.Latin1
				: System.Text.Encoding.UTF8;

			builder.Add (enc.GetBytes (description));
			builder.Add ((byte)0x00); // Single null terminator
		}

		// URL is always Latin-1
		builder.Add (System.Text.Encoding.Latin1.GetBytes (url));

		return builder.ToBinaryData ().ToArray ();
	}
}
