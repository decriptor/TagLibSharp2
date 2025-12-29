// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class TextFrameTests
{
	// Text Frame Format:
	// Offset  Size  Field
	// 0       1     Text encoding (0=Latin-1, 1=UTF-16 w/BOM, 2=UTF-16BE, 3=UTF-8)
	// 1       n     Text content (may be null-terminated)


	[TestMethod]
	public void Read_Latin1Encoding_ParsesCorrectly ()
	{
		// Encoding byte (0x00 = Latin-1) + "Test Title" in Latin-1
		var data = CreateTextFrameData (TextEncodingType.Latin1, "Test Title");

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TIT2", result.Frame!.Id);
		Assert.AreEqual ("Test Title", result.Frame.Text);
		Assert.AreEqual (TextEncodingType.Latin1, result.Frame.Encoding);
	}

	[TestMethod]
	public void Read_Utf16WithBom_ParsesCorrectly ()
	{
		// UTF-16 LE with BOM (0xFF 0xFE)
		var text = "Unicode: Привет";
		var data = CreateTextFrameData (TextEncodingType.Utf16WithBom, text);

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (text, result.Frame!.Text);
		Assert.AreEqual (TextEncodingType.Utf16WithBom, result.Frame.Encoding);
	}

	[TestMethod]
	public void Read_Utf16BE_ParsesCorrectly ()
	{
		var text = "Big Endian Test";
		var data = CreateTextFrameData (TextEncodingType.Utf16BE, text);

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (text, result.Frame!.Text);
		Assert.AreEqual (TextEncodingType.Utf16BE, result.Frame.Encoding);
	}

	[TestMethod]
	public void Read_Utf8_ParsesCorrectly ()
	{
		var text = "UTF-8: 日本語テスト";
		var data = CreateTextFrameData (TextEncodingType.Utf8, text);

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (text, result.Frame!.Text);
		Assert.AreEqual (TextEncodingType.Utf8, result.Frame.Encoding);
	}

	[TestMethod]
	public void Read_InvalidEncoding_ReturnsFailure ()
	{
		var data = new byte[] { 0x05, (byte)'T', (byte)'e', (byte)'s', (byte)'t' };

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}



	[TestMethod]
	public void Read_TIT2_ParsesTitle ()
	{
		var data = CreateTextFrameData (TextEncodingType.Latin1, "Song Title");

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TIT2", result.Frame!.Id);
		Assert.AreEqual ("Song Title", result.Frame.Text);
	}

	[TestMethod]
	public void Read_TPE1_ParsesArtist ()
	{
		var data = CreateTextFrameData (TextEncodingType.Latin1, "Artist Name");

		var result = TextFrame.Read ("TPE1", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TPE1", result.Frame!.Id);
		Assert.AreEqual ("Artist Name", result.Frame.Text);
	}

	[TestMethod]
	public void Read_TALB_ParsesAlbum ()
	{
		var data = CreateTextFrameData (TextEncodingType.Latin1, "Album Name");

		var result = TextFrame.Read ("TALB", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TALB", result.Frame!.Id);
		Assert.AreEqual ("Album Name", result.Frame.Text);
	}

	[TestMethod]
	public void Read_TYER_ParsesYear ()
	{
		var data = CreateTextFrameData (TextEncodingType.Latin1, "2024");

		var result = TextFrame.Read ("TYER", data, Id3v2Version.V23);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TYER", result.Frame!.Id);
		Assert.AreEqual ("2024", result.Frame.Text);
	}

	[TestMethod]
	public void Read_TDRC_ParsesRecordingDate ()
	{
		// TDRC is the v2.4 replacement for TYER
		var data = CreateTextFrameData (TextEncodingType.Latin1, "2024-12-25");

		var result = TextFrame.Read ("TDRC", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TDRC", result.Frame!.Id);
		Assert.AreEqual ("2024-12-25", result.Frame.Text);
	}

	[TestMethod]
	public void Read_TCON_ParsesGenre ()
	{
		var data = CreateTextFrameData (TextEncodingType.Latin1, "Rock");

		var result = TextFrame.Read ("TCON", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TCON", result.Frame!.Id);
		Assert.AreEqual ("Rock", result.Frame.Text);
	}

	[TestMethod]
	public void Read_TCON_NumericGenre_ParsesCorrectly ()
	{
		// ID3v2 can encode genres as "(17)" meaning genre index 17 (Rock)
		var data = CreateTextFrameData (TextEncodingType.Latin1, "(17)");

		var result = TextFrame.Read ("TCON", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("(17)", result.Frame!.Text);
	}

	[TestMethod]
	public void Read_TRCK_ParsesTrack ()
	{
		var data = CreateTextFrameData (TextEncodingType.Latin1, "5/12");

		var result = TextFrame.Read ("TRCK", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TRCK", result.Frame!.Id);
		Assert.AreEqual ("5/12", result.Frame.Text);
	}



	[TestMethod]
	public void Read_EmptyText_ReturnsEmptyString ()
	{
		var data = new byte[] { 0x00 }; // Just encoding byte, no text

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Text);
	}

	[TestMethod]
	public void Read_NullTerminatedText_TrimsNull ()
	{
		// Latin-1 with null terminator
		var data = new byte[] { 0x00, (byte)'A', (byte)'B', 0x00 };

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("AB", result.Frame!.Text);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = Array.Empty<byte> ();

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_MultipleNullSeparatedValues_PreservesAll ()
	{
		// ID3v2.4 uses null separators for multi-value text frames
		// Example: "Value1\0Value2\0" (trailing null stripped)
		var data = new byte[] { 0x00, (byte)'A', 0x00, (byte)'B', 0x00 };

		var result = TextFrame.Read ("TIT2", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		// Preserves all values with null separators (trailing null stripped)
		Assert.AreEqual ("A\0B", result.Frame!.Text);
	}



	[TestMethod]
	public void Render_Latin1_CreatesCorrectData ()
	{
		var frame = new TextFrame ("TIT2", "Test", TextEncodingType.Latin1);

		var data = frame.RenderContent ();

		Assert.AreEqual (0x00, data[0]); // Latin-1 encoding
		Assert.AreEqual ((byte)'T', data[1]);
		Assert.AreEqual ((byte)'e', data[2]);
		Assert.AreEqual ((byte)'s', data[3]);
		Assert.AreEqual ((byte)'t', data[4]);
	}

	[TestMethod]
	public void Render_Utf8_CreatesCorrectData ()
	{
		var frame = new TextFrame ("TIT2", "Test", TextEncodingType.Utf8);

		var data = frame.RenderContent ();

		Assert.AreEqual (0x03, data[0]); // UTF-8 encoding
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var original = new TextFrame ("TPE1", "Test Artist", TextEncodingType.Utf8);

		var rendered = original.RenderContent ();
		var result = TextFrame.Read ("TPE1", rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Id, result.Frame!.Id);
		Assert.AreEqual (original.Text, result.Frame.Text);
		Assert.AreEqual (original.Encoding, result.Frame.Encoding);
	}

	[TestMethod]
	public void Render_Utf16_RoundTrip_PreservesData ()
	{
		var original = new TextFrame ("TIT2", "日本語", TextEncodingType.Utf16WithBom);

		var rendered = original.RenderContent ();
		var result = TextFrame.Read ("TIT2", rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Text, result.Frame!.Text);
	}



	static byte[] CreateTextFrameData (TextEncodingType encoding, string text)
	{
		var bytes = new List<byte> { (byte)encoding };

		switch (encoding) {
			case TextEncodingType.Latin1:
				bytes.AddRange (System.Text.Encoding.Latin1.GetBytes (text));
				break;
			case TextEncodingType.Utf16WithBom:
				bytes.Add (0xFF); // BOM LE
				bytes.Add (0xFE);
				bytes.AddRange (System.Text.Encoding.Unicode.GetBytes (text));
				break;
			case TextEncodingType.Utf16BE:
				bytes.AddRange (System.Text.Encoding.BigEndianUnicode.GetBytes (text));
				break;
			case TextEncodingType.Utf8:
				bytes.AddRange (System.Text.Encoding.UTF8.GetBytes (text));
				break;
		}

		return bytes.ToArray ();
	}

}
