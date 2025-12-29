// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

/// <summary>
/// Tests for <see cref="UserTextFrame"/> (TXXX frames).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class UserTextFrameTests
{

	[TestMethod]
	public void Constructor_WithDescriptionAndValue_SetsProperties ()
	{
		var frame = new UserTextFrame ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		Assert.AreEqual ("REPLAYGAIN_TRACK_GAIN", frame.Description);
		Assert.AreEqual ("-6.50 dB", frame.Value);
		Assert.AreEqual (TextEncodingType.Utf8, frame.Encoding);
	}

	[TestMethod]
	public void Constructor_WithEncoding_SetsEncoding ()
	{
		var frame = new UserTextFrame ("TEST", "value", TextEncodingType.Latin1);

		Assert.AreEqual (TextEncodingType.Latin1, frame.Encoding);
	}



	[TestMethod]
	public void Read_Latin1Encoding_ReadsCorrectly ()
	{
		// TXXX frame content: encoding + description + null + value
		// Latin1 encoding (0x00), description "TEST", null, value "hello"
		var data = new byte[] {
			0x00, // Latin1 encoding
			0x54, 0x45, 0x53, 0x54, 0x00, // "TEST\0"
			0x68, 0x65, 0x6C, 0x6C, 0x6F // "hello"
		};

		var result = UserTextFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TEST", result.Frame!.Description);
		Assert.AreEqual ("hello", result.Frame.Value);
	}

	[TestMethod]
	public void Read_Utf8Encoding_ReadsCorrectly ()
	{
		// UTF-8 encoding (0x03), description "MUSICBRAINZ_TRACKID", null, UUID value
		var data = new byte[] {
			0x03, // UTF-8 encoding
			0x4D, 0x55, 0x53, 0x49, 0x43, 0x42, 0x52, 0x41, 0x49, 0x4E, 0x5A, 0x5F,
			0x54, 0x52, 0x41, 0x43, 0x4B, 0x49, 0x44, 0x00, // "MUSICBRAINZ_TRACKID\0"
			0x66, 0x34, 0x65, 0x37, 0x63, 0x39, 0x64, 0x38 // "f4e7c9d8" (partial UUID)
		};

		var result = UserTextFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("MUSICBRAINZ_TRACKID", result.Frame!.Description);
		Assert.AreEqual ("f4e7c9d8", result.Frame.Value);
	}

	[TestMethod]
	public void Read_EmptyData_ReturnsFailure ()
	{
		var result = UserTextFrame.Read (Array.Empty<byte> (), Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_NoNullTerminator_ReturnsFailure ()
	{
		// No null terminator between description and value
		var data = new byte[] {
			0x00, // Latin1 encoding
			0x54, 0x45, 0x53, 0x54 // "TEST" without null
		};

		var result = UserTextFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_Utf16LEWithBom_ReadsCorrectly ()
	{
		// UTF-16 LE with BOM (0x01)
		var data = new byte[] {
			0x01, // UTF-16 with BOM
			0xFF, 0xFE, // BOM (little-endian)
			0x54, 0x00, 0x45, 0x00, 0x53, 0x00, 0x54, 0x00, // "TEST" in UTF-16 LE
			0x00, 0x00, // Null terminator (2 bytes for UTF-16)
			0xFF, 0xFE, // BOM for value
			0x56, 0x00, 0x41, 0x00, 0x4C, 0x00 // "VAL" in UTF-16 LE
		};

		var result = UserTextFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TEST", result.Frame!.Description);
		Assert.AreEqual ("VAL", result.Frame.Value);
	}



	[TestMethod]
	public void RenderContent_Latin1_RendersCorrectly ()
	{
		var frame = new UserTextFrame ("TEST", "hello", TextEncodingType.Latin1);

		var rendered = frame.RenderContent ();

		// encoding + "TEST" + null + "hello"
		Assert.AreEqual (11, rendered.Length);
		Assert.AreEqual (0x00, rendered.Span[0]); // Latin1 encoding
		Assert.AreEqual ((byte)'T', rendered.Span[1]);
		Assert.AreEqual ((byte)'E', rendered.Span[2]);
		Assert.AreEqual ((byte)'S', rendered.Span[3]);
		Assert.AreEqual ((byte)'T', rendered.Span[4]);
		Assert.AreEqual (0x00, rendered.Span[5]); // Null terminator
		Assert.AreEqual ((byte)'h', rendered.Span[6]);
	}

	[TestMethod]
	public void RenderContent_Utf8_RendersCorrectly ()
	{
		var frame = new UserTextFrame ("DESC", "value");

		var rendered = frame.RenderContent ();

		Assert.AreEqual (0x03, rendered.Span[0]); // UTF-8 encoding
	}

	[TestMethod]
	public void RenderContent_RoundTrip_PreservesData ()
	{
		var original = new UserTextFrame ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		var rendered = original.RenderContent ();
		var readResult = UserTextFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual (original.Description, readResult.Frame!.Description);
		Assert.AreEqual (original.Value, readResult.Frame.Value);
	}



	[TestMethod]
	public void EmptyDescription_WorksCorrectly ()
	{
		var frame = new UserTextFrame ("", "value");

		var rendered = frame.RenderContent ();
		var readResult = UserTextFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("", readResult.Frame!.Description);
		Assert.AreEqual ("value", readResult.Frame.Value);
	}

	[TestMethod]
	public void EmptyValue_WorksCorrectly ()
	{
		var frame = new UserTextFrame ("DESC", "");

		var rendered = frame.RenderContent ();
		var readResult = UserTextFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("DESC", readResult.Frame!.Description);
		Assert.AreEqual ("", readResult.Frame.Value);
	}

	[TestMethod]
	public void UnicodeCharacters_PreservedInRoundTrip ()
	{
		var frame = new UserTextFrame ("DESCRIPTION", "Test with unicode: \u00e9\u00e8\u00ea");

		var rendered = frame.RenderContent ();
		var readResult = UserTextFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("Test with unicode: \u00e9\u00e8\u00ea", readResult.Frame!.Value);
	}

}
