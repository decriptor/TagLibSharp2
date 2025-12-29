// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

/// <summary>
/// Tests for <see cref="LyricsFrame"/> (USLT frame).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class LyricsFrameTests
{

	[TestMethod]
	public void Constructor_SetsProperties ()
	{
		var frame = new LyricsFrame ("Test lyrics", "eng", "Description");

		Assert.AreEqual ("Test lyrics", frame.Text);
		Assert.AreEqual ("eng", frame.Language);
		Assert.AreEqual ("Description", frame.Description);
		Assert.AreEqual (TextEncodingType.Utf8, frame.Encoding);
	}

	[TestMethod]
	public void Constructor_WithDefaults_SetsEnglishAndEmpty ()
	{
		var frame = new LyricsFrame ("Lyrics only");

		Assert.AreEqual ("Lyrics only", frame.Text);
		Assert.AreEqual ("eng", frame.Language);
		Assert.AreEqual ("", frame.Description);
	}

	[TestMethod]
	public void FrameId_ReturnsUSLT ()
	{
		Assert.AreEqual ("USLT", LyricsFrame.FrameId);
	}



	[TestMethod]
	public void Read_ValidLatin1Frame_ParsesCorrectly ()
	{
		// Build a USLT frame: encoding(1) + language(3) + description + null + lyrics
		var lyrics = "Hello, world!";
		var data = BuildFrame (TextEncodingType.Latin1, "eng", "Desc", lyrics);

		var result = LyricsFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("eng", result.Frame!.Language);
		Assert.AreEqual ("Desc", result.Frame.Description);
		Assert.AreEqual (lyrics, result.Frame.Text);
	}

	[TestMethod]
	public void Read_ValidUtf8Frame_ParsesCorrectly ()
	{
		var lyrics = "日本語の歌詞";
		var data = BuildFrame (TextEncodingType.Utf8, "jpn", "", lyrics);

		var result = LyricsFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("jpn", result.Frame!.Language);
		Assert.AreEqual (lyrics, result.Frame.Text);
	}

	[TestMethod]
	public void Read_EmptyDescription_ParsesCorrectly ()
	{
		var lyrics = "Verse 1\nChorus\nVerse 2";
		var data = BuildFrame (TextEncodingType.Utf8, "eng", "", lyrics);

		var result = LyricsFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Description);
		Assert.AreEqual (lyrics, result.Frame.Text);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x03, (byte)'e', (byte)'n' }; // Only 3 bytes

		var result = LyricsFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_InvalidEncoding_ReturnsFailure ()
	{
		var data = new byte[] { 0x05, (byte)'e', (byte)'n', (byte)'g', 0x00 }; // Invalid encoding 5

		var result = LyricsFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	public void RenderContent_Latin1_RoundTrips ()
	{
		var original = new LyricsFrame (
			"Simple lyrics text",
			"eng",
			"Lyrics",
			TextEncodingType.Latin1);

		var rendered = original.RenderContent ();
		var result = LyricsFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Text, result.Frame!.Text);
		Assert.AreEqual (original.Language, result.Frame.Language);
		Assert.AreEqual (original.Description, result.Frame.Description);
	}

	[TestMethod]
	public void RenderContent_Utf8_RoundTrips ()
	{
		var original = new LyricsFrame (
			"Unicode lyrics: 日本語 中文 한국어 العربية",
			"mul",
			"Multilingual",
			TextEncodingType.Utf8);

		var rendered = original.RenderContent ();
		var result = LyricsFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Text, result.Frame!.Text);
	}

	[TestMethod]
	public void RenderContent_Utf16WithBom_RoundTrips ()
	{
		var original = new LyricsFrame (
			"UTF-16 lyrics",
			"eng",
			"Test",
			TextEncodingType.Utf16WithBom);

		var rendered = original.RenderContent ();
		var result = LyricsFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Text, result.Frame!.Text);
	}

	[TestMethod]
	public void RenderContent_EmptyDescription_RoundTrips ()
	{
		var original = new LyricsFrame ("Just lyrics");

		var rendered = original.RenderContent ();
		var result = LyricsFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Description);
		Assert.AreEqual ("Just lyrics", result.Frame.Text);
	}

	[TestMethod]
	public void RenderContent_MultilineText_RoundTrips ()
	{
		var lyrics = "Verse 1 line 1\nVerse 1 line 2\n\nChorus\nChorus line 2\n\nVerse 2";
		var original = new LyricsFrame (lyrics);

		var rendered = original.RenderContent ();
		var result = LyricsFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (lyrics, result.Frame!.Text);
	}



	[TestMethod]
	public void Id3v2Tag_Lyrics_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Lyrics = "These are the lyrics";

		Assert.AreEqual ("These are the lyrics", tag.Lyrics);
		Assert.HasCount (1, tag.LyricsFrames);
	}

	[TestMethod]
	public void Id3v2Tag_Lyrics_SetNull_ClearsLyrics ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Lyrics = "Some lyrics";

		tag.Lyrics = null;

		Assert.IsNull (tag.Lyrics);
		Assert.IsEmpty (tag.LyricsFrames);
	}

	[TestMethod]
	public void Id3v2Tag_Lyrics_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Lyrics = "Verse 1\nChorus\nVerse 2"
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Verse 1\nChorus\nVerse 2", result.Tag!.Lyrics);
	}

	[TestMethod]
	public void Id3v2Tag_Lyrics_WithUnicode_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Lyrics = "日本語の歌詞\n中文歌词\nКириллица"
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("日本語の歌詞\n中文歌词\nКириллица", result.Tag!.Lyrics);
	}

	[TestMethod]
	public void Id3v2Tag_AddLyrics_AddsToCollection ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AddLyrics (new LyricsFrame ("English lyrics", "eng", ""));
		tag.AddLyrics (new LyricsFrame ("日本語の歌詞", "jpn", ""));

		Assert.HasCount (2, tag.LyricsFrames);
	}

	[TestMethod]
	public void Id3v2Tag_RemoveLyrics_RemovesByLanguage ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddLyrics (new LyricsFrame ("English", "eng", ""));
		tag.AddLyrics (new LyricsFrame ("Japanese", "jpn", ""));

		tag.RemoveLyrics (language: "eng");

		Assert.HasCount (1, tag.LyricsFrames);
		Assert.AreEqual ("jpn", tag.LyricsFrames[0].Language);
	}

	[TestMethod]
	public void Id3v2Tag_GetLyricsFrame_FindsByLanguage ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddLyrics (new LyricsFrame ("English", "eng", ""));
		tag.AddLyrics (new LyricsFrame ("Japanese", "jpn", ""));

		var frame = tag.GetLyricsFrame (language: "jpn");

		Assert.IsNotNull (frame);
		Assert.AreEqual ("Japanese", frame.Text);
	}

	[TestMethod]
	public void Id3v2Tag_Clear_ClearsLyrics ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) {
			Title = "Test",
			Lyrics = "Some lyrics"
		};

		tag.Clear ();

		Assert.IsNull (tag.Lyrics);
		Assert.IsEmpty (tag.LyricsFrames);
	}



	static byte[] BuildFrame (TextEncodingType encoding, string language, string description, string text)
	{
		using var ms = new MemoryStream ();

		// Encoding byte
		ms.WriteByte ((byte)encoding);

		// Language (3 bytes ASCII)
		var langBytes = System.Text.Encoding.ASCII.GetBytes (language.PadRight (3).Substring (0, 3));
		ms.Write (langBytes, 0, 3);

		// Description + null terminator
		var descBytes = EncodeString (description, encoding);
		ms.Write (descBytes, 0, descBytes.Length);
		WriteNullTerminator (ms, encoding);

		// Text
		var textBytes = EncodeString (text, encoding);
		ms.Write (textBytes, 0, textBytes.Length);

		return ms.ToArray ();
	}

	static byte[] EncodeString (string text, TextEncodingType encoding)
	{
		return encoding switch {
			TextEncodingType.Latin1 => System.Text.Encoding.Latin1.GetBytes (text),
			TextEncodingType.Utf8 => System.Text.Encoding.UTF8.GetBytes (text),
			TextEncodingType.Utf16WithBom => System.Text.Encoding.Unicode.GetPreamble ()
				.Concat (System.Text.Encoding.Unicode.GetBytes (text)).ToArray (),
			TextEncodingType.Utf16BE => System.Text.Encoding.BigEndianUnicode.GetBytes (text),
			_ => Array.Empty<byte> ()
		};
	}

	static void WriteNullTerminator (MemoryStream ms, TextEncodingType encoding)
	{
		if (encoding == TextEncodingType.Utf16WithBom || encoding == TextEncodingType.Utf16BE) {
			ms.WriteByte (0);
			ms.WriteByte (0);
		} else {
			ms.WriteByte (0);
		}
	}

}
