// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// A stored terminator is not part of the text.
/// Text frames keep internal NULs between values. A comment or lyric ends at the first NUL.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2TrailingTerminatorTests
{
	[TestMethod]
	public void Comment_Utf16YepaWithTerminator_DropsTrailingNul ()
	{
		var tag = ReadFrame (TestConstants.Id3v2.Version4, "COMM", Utf16Comment ("eng", "", "yepa", terminated: true));

		Assert.AreEqual ("yepa", tag.Comment);
		AssertNoNul (tag.Comment);
		Assert.AreEqual ("yepa", tag.Comments[0].Text);
		Assert.AreEqual ("eng", tag.Comments[0].Language);
	}

	[TestMethod]
	public void Comment_Latin1WithTrailingNul_DropsTerminator ()
	{
		var content = Latin1DescribedText (TextEncodingType.Latin1, "eng", "", "yepa", terminated: true);
		var tag = ReadFrame (TestConstants.Id3v2.Version4, "COMM", content);

		Assert.AreEqual ("yepa", tag.Comment);
		AssertNoNul (tag.Comment);
	}

	[TestMethod]
	public void Comment_Utf16BeWithTerminator_DropsTrailingNul ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16BE);
		builder.Add ("eng"u8);
		builder.Add (0, 0);
		builder.Add (Encoding.BigEndianUnicode.GetBytes ("yepa"));
		builder.Add (0, 0);

		var frame = CommentFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("yepa", frame.Frame!.Text);
		AssertNoNul (frame.Frame.Text);
	}

	[TestMethod]
	public void Comment_InternalNul_EndsTheText ()
	{
		// Description terminator, then ye + NUL + pa, then a trailing terminator.
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("eng"u8);
		builder.Add (0);
		builder.Add ((byte)'y', (byte)'e', 0, (byte)'p', (byte)'a', 0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "COMM", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("ye", tag.Comment);
		AssertNoNul (tag.Comment);
	}

	[TestMethod]
	public void Comment_Utf16InternalNul_EndsTheText ()
	{
		var tag = ReadFrame (TestConstants.Id3v2.Version4, "COMM", Utf16Comment ("eng", "", "ye\0pa", terminated: true));

		Assert.AreEqual ("ye", tag.Comment);
		AssertNoNul (tag.Comment);
	}

	[TestMethod]
	public void Title_Utf16EmbeddedNul_ReturnsFirstValue ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("T\0T"));

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("T\0T", tag.Frames[0].Text);
		Assert.AreEqual ("T", tag.Title);
		Assert.AreEqual ("T", tag.GetTextFrame ("TIT2"));

		var values = tag.GetTextFrameValues ("TIT2");
		Assert.HasCount (2, values);
		Assert.AreEqual ("T", values[0]);
		Assert.AreEqual ("T", values[1]);
	}

	[TestMethod]
	public void Title_TrailingNul_HasNoEmptySecondValue ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("Title"u8);
		builder.Add (0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("Title", tag.Title);
		AssertNoNul (tag.Title);
		var values = tag.GetTextFrameValues ("TIT2");
		Assert.HasCount (1, values);
		Assert.AreEqual ("Title", values[0]);
	}

	[TestMethod]
	public void Title_RoundTrip_DoesNotKeepTrailingNul ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Title = "Title" };

		var result = Id3v2Tag.Read (original.Render ().Span);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Title", result.Tag!.Title);
		AssertNoNul (result.Tag.Title);
		var values = result.Tag.GetTextFrameValues ("TIT2");
		Assert.HasCount (1, values);
		Assert.AreEqual ("Title", values[0]);
	}

	[TestMethod]
	public void Performers_Utf16NullSeparated_KeepsBothNames ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("Alice\0Bob"));
		builder.Add (0, 0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("Alice\0Bob", tag.Frames[0].Text);
		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("Alice", tag.Performers[0]);
		Assert.AreEqual ("Bob", tag.Performers[1]);
		Assert.AreEqual ("Alice", tag.Artist);
	}

	[TestMethod]
	public void Performers_V23SlashSeparated_SingleGetterDoesNotSplit ()
	{
		var frame = TestBuilders.Id3v2.CreateTextFrame ("TPE1", "A/B", TestConstants.Id3v2.Version3);
		var tag = ReadTag (TestConstants.Id3v2.Version3, frame);

		Assert.AreEqual ("A/B", tag.GetTextFrame ("TPE1"));
		Assert.AreEqual ("A/B", tag.Artist);
		var values = tag.GetTextFrameValues ("TPE1");
		Assert.HasCount (2, values);
		Assert.AreEqual ("A", values[0]);
		Assert.AreEqual ("B", values[1]);
		Assert.HasCount (2, tag.Performers);
	}

	[TestMethod]
	public void Lyrics_TrailingNul_DropsTerminator ()
	{
		var tag = ReadFrame (TestConstants.Id3v2.Version4, "USLT", Utf16Comment ("eng", "", "yepa", terminated: true));

		Assert.AreEqual ("yepa", tag.Lyrics);
		AssertNoNul (tag.Lyrics);
	}

	[TestMethod]
	public void Lyrics_InternalNul_EndsTheText ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("eng"u8);
		builder.Add (0);
		builder.Add ((byte)'y', (byte)'e', 0, (byte)'p', (byte)'a', 0);

		var frame = LyricsFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("ye", frame.Frame!.Text);
		AssertNoNul (frame.Frame.Text);
	}

	[TestMethod]
	public void Lyrics_Utf16InternalNul_EndsTheText ()
	{
		var frame = LyricsFrame.Read (Utf16Comment ("eng", "", "ye\0pa", terminated: true), Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("ye", frame.Frame!.Text);
		AssertNoNul (frame.Frame.Text);
	}

	[TestMethod]
	public void UserText_TrailingNul_DropsTerminator ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf8);
		builder.Add ("MOOD"u8);
		builder.Add (0);
		builder.Add ("yepa"u8);
		builder.Add (0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TXXX", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("MOOD", tag.UserTextFrames[0].Description);
		AssertNoNul (tag.UserTextFrames[0].Description);
		Assert.AreEqual ("yepa", tag.GetUserText ("MOOD"));
		AssertNoNul (tag.GetUserText ("MOOD"));
	}

	[TestMethod]
	public void UserText_InternalNul_IsKeptAndTrailingTerminatorIsNot ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf8);
		builder.Add ("MOOD"u8);
		builder.Add (0);
		builder.Add ((byte)'y', (byte)'e', 0, (byte)'p', (byte)'a', 0);

		var frame = UserTextFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("ye\0pa", frame.Frame!.Value);
	}

	[TestMethod]
	public void UserText_Utf16ValueWithTerminator_DropsTrailingNul ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("MOOD"));
		builder.Add (0, 0);
		builder.Add (Encoding.Unicode.GetBytes ("yepa"));
		builder.Add (0, 0);

		var frame = UserTextFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("MOOD", frame.Frame!.Description);
		AssertNoNul (frame.Frame.Description);
		Assert.AreEqual ("yepa", frame.Frame.Value);
		AssertNoNul (frame.Frame.Value);
	}

	[TestMethod]
	public void UserUrl_DescriptionAndUrl_DropTrailingTerminator ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("yepa"));
		builder.Add (0, 0);
		builder.Add ("https://example.com"u8);
		builder.Add (0);

		var frame = UserUrlFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("yepa", frame.Frame!.Description);
		AssertNoNul (frame.Frame.Description);
		Assert.AreEqual ("https://example.com", frame.Frame.Url);
		AssertNoNul (frame.Frame.Url);
	}

	[TestMethod]
	public void UserUrl_Latin1Description_DropsTrailingUrlNul ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("site"u8);
		builder.Add (0);
		builder.Add ("https://example.com"u8);
		builder.Add (0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "WXXX", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("site", tag.UserUrlFrames[0].Description);
		AssertNoNul (tag.UserUrlFrames[0].Description);
		Assert.AreEqual ("https://example.com", tag.GetUserUrl ("site"));
		AssertNoNul (tag.GetUserUrl ("site"));
	}

	[TestMethod]
	public void InvolvedPeople_Utf16OddLength_DoesNotDropTheLastCharacter ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("guitar"));
		builder.Add (0, 0);
		builder.Add (0x41, 0x00); // A
		builder.Add (0x00); // lone byte; pairing it with the low byte of A would eat the name

		var frame = InvolvedPeopleFrame.Read ("TIPL", builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual (1, frame.Frame!.Count);
		Assert.AreEqual ("A", frame.Frame.GetPerson ("guitar"));
	}

	[TestMethod]
	public void Performers_Utf16BomOnEachValue_ReturnsBothNames ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("a"));
		builder.Add (0, 0);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("b"));
		builder.Add (0, 0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("a\0b", tag.Frames[0].Text);
		Assert.AreEqual (-1, tag.Frames[0].Text.IndexOf ('\uFEFF'));
		Assert.AreEqual ("a", tag.GetTextFrame ("TPE1"));
		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("a", tag.Performers[0]);
		Assert.AreEqual ("b", tag.Performers[1]);
		Assert.AreEqual ("a", tag.Artist);
	}

	[TestMethod]
	public void Performers_Utf16SecondValueBigEndianBom_ReturnsBothNames ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (0x61, 0x00);
		builder.Add (0, 0);
		builder.Add (0xFE, 0xFF);
		builder.Add (0x00, 0x62);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("a", tag.Performers[0]);
		Assert.AreEqual ("b", tag.Performers[1]);
		Assert.AreEqual (-1, tag.Performers[1].IndexOf ('\uFEFF'));
	}

	[TestMethod]
	public void Performers_Utf16SecondValueWithoutBom_UsesFirstBom ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFE, 0xFF);
		builder.Add (0x00, 0x61);
		builder.Add (0, 0);
		builder.Add (0x00, 0x62);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("a", tag.Performers[0]);
		Assert.AreEqual ("b", tag.Performers[1]);
	}

	[TestMethod]
	public void Title_Utf16OddLength_DoesNotDropLastCharacter ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (0x41, 0x00);
		builder.Add (0x42, 0x00);
		builder.Add (0x00);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("AB", tag.Title);
		Assert.AreEqual ("AB", tag.Frames[0].Text);
	}

	[TestMethod]
	public void Text_LeadingNul_FirstValueIsEmpty ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add (0, (byte)'b', 0);
		var leading = builder.ToBinaryData ().ToArray ();

		var v23 = ReadFrame (TestConstants.Id3v2.Version3, "TIT2", leading);
		Assert.AreEqual ("", v23.GetTextFrame ("TIT2"));
		Assert.AreEqual ("\0b", v23.Frames[0].Text);
		var values23 = v23.GetTextFrameValues ("TIT2");
		Assert.HasCount (1, values23);
		Assert.AreEqual ("b", values23[0]);

		var v24 = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", leading);
		Assert.AreEqual ("", v24.GetTextFrame ("TIT2"));
		var values24 = v24.GetTextFrameValues ("TIT2");
		Assert.HasCount (1, values24);
		Assert.AreEqual ("b", values24[0]);

		using var skipped = new BinaryDataBuilder ();
		skipped.Add ((byte)TextEncodingType.Latin1);
		skipped.Add (0, 0, (byte)'T');
		var allEmpty = ReadFrame (TestConstants.Id3v2.Version3, "TIT2", skipped.ToBinaryData ().ToArray ());
		Assert.AreEqual ("", allEmpty.GetTextFrame ("TIT2"));
		Assert.AreEqual ("T", allEmpty.GetTextFrameValues ("TIT2")[0]);
		Assert.IsNull (new Id3v2Tag ().GetTextFrame ("TIT2"));
	}

	[TestMethod]
	public void CatalogNumber_EmbeddedNul_ReturnsFirstValue ()
	{
		var catalog = UserText ("CATALOGNUMBER", "A\0B");
		var barcode = UserText ("BARCODE", "A\0B");
		var tag = ReadTag (
			TestConstants.Id3v2.Version4,
			TestBuilders.Id3v2.CreateFrame ("TXXX", catalog, TestConstants.Id3v2.Version4),
			TestBuilders.Id3v2.CreateFrame ("TXXX", barcode, TestConstants.Id3v2.Version4));

		Assert.AreEqual ("A", tag.CatalogNumber);
		Assert.AreEqual ("A", tag.Barcode);
		Assert.AreEqual ("A", tag.GetUserText ("CATALOGNUMBER"));
		AssertNoNul (tag.CatalogNumber);

		string? stored = null;
		for (var i = 0; i < tag.UserTextFrames.Count; i++) {
			if (tag.UserTextFrames[i].Description == "CATALOGNUMBER")
				stored = tag.UserTextFrames[i].Value;
		}

		Assert.AreEqual ("A\0B", stored);
		Assert.IsNull (tag.GetUserText ("MISSING"));
	}

	[TestMethod]
	public void Title_Utf16OddLengthAndRepeatedTerminator_DropsOnlyTerminatorUnits ()
	{
		using var oddAndNul = new BinaryDataBuilder ();
		oddAndNul.Add ((byte)TextEncodingType.Utf16WithBom);
		oddAndNul.Add (0xFF, 0xFE);
		oddAndNul.Add (0x41, 0x00);
		oddAndNul.Add (0, 0);
		oddAndNul.Add (0x00);

		var kept = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", oddAndNul.ToBinaryData ().ToArray ());
		Assert.AreEqual ("A", kept.Title);
		Assert.AreEqual ("A", kept.Frames[0].Text);
		Assert.HasCount (1, kept.GetTextFrameValues ("TIT2"));
		Assert.AreEqual ("A", kept.GetTextFrameValues ("TIT2")[0]);

		using var repeated = new BinaryDataBuilder ();
		repeated.Add ((byte)TextEncodingType.Utf16WithBom);
		repeated.Add (0xFF, 0xFE);
		repeated.Add (0x41, 0x00);
		repeated.Add (0, 0);
		repeated.Add (0, 0);

		var once = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", repeated.ToBinaryData ().ToArray ());
		Assert.AreEqual ("A", once.Title);
		Assert.AreEqual ("A", once.Frames[0].Text);
		Assert.HasCount (1, once.GetTextFrameValues ("TIT2"));

		using var onlyOdd = new BinaryDataBuilder ();
		onlyOdd.Add ((byte)TextEncodingType.Utf16WithBom);
		onlyOdd.Add (0x41);

		var dropped = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", onlyOdd.ToBinaryData ().ToArray ());
		Assert.AreEqual ("", dropped.Title);
		Assert.AreEqual ("", dropped.Frames[0].Text);
		Assert.HasCount (0, dropped.GetTextFrameValues ("TIT2"));

		using var beOdd = new BinaryDataBuilder ();
		beOdd.Add ((byte)TextEncodingType.Utf16BE);
		beOdd.Add (0x00, 0x41);
		beOdd.Add (0x00, 0x42);
		beOdd.Add (0x00);

		var be = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", beOdd.ToBinaryData ().ToArray ());
		Assert.AreEqual ("AB", be.Title);
		Assert.AreEqual ("AB", be.Frames[0].Text);
	}

	[TestMethod]
	public void Title_Utf16EmptyBom_IsAnEmptyString ()
	{
		using var little = new BinaryDataBuilder ();
		little.Add ((byte)TextEncodingType.Utf16WithBom);
		little.Add (0xFF, 0xFE);

		var le = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", little.ToBinaryData ().ToArray ());
		Assert.AreEqual ("", le.Title);
		Assert.AreEqual ("", le.Frames[0].Text);
		Assert.AreEqual (-1, le.Frames[0].Text.IndexOf ('\uFEFF'));
		Assert.HasCount (0, le.GetTextFrameValues ("TIT2"));

		using var big = new BinaryDataBuilder ();
		big.Add ((byte)TextEncodingType.Utf16WithBom);
		big.Add (0xFE, 0xFF);

		var be = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", big.ToBinaryData ().ToArray ());
		Assert.AreEqual ("", be.Title);
		Assert.AreEqual ("", be.Frames[0].Text);
		Assert.AreEqual (-1, be.Frames[0].Text.IndexOf ('\uFEFF'));

		using var onlyTerminator = new BinaryDataBuilder ();
		onlyTerminator.Add ((byte)TextEncodingType.Utf16WithBom);
		onlyTerminator.Add (0, 0);

		var trimmed = ReadFrame (TestConstants.Id3v2.Version4, "TIT2", onlyTerminator.ToBinaryData ().ToArray ());
		Assert.AreEqual ("", trimmed.Title);
		Assert.AreEqual ("", trimmed.Frames[0].Text);
	}

	[TestMethod]
	public void Performers_Utf16WithoutBom_KeepsBothNames ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (Encoding.Unicode.GetBytes ("Alice\0Bob"));

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("Alice\0Bob", tag.Frames[0].Text);
		Assert.AreEqual ("Alice", tag.Artist);
		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("Alice", tag.Performers[0]);
		Assert.AreEqual ("Bob", tag.Performers[1]);
	}

	[TestMethod]
	public void Performers_Utf16LeadingNul_FirstValueIsEmpty ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0, 0);
		builder.Add (0x62, 0x00);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("\0b", tag.Frames[0].Text);
		Assert.AreEqual ("", tag.GetTextFrame ("TPE1"));
		Assert.AreEqual ("", tag.Artist);
		var values = tag.GetTextFrameValues ("TPE1");
		Assert.HasCount (1, values);
		Assert.AreEqual ("b", values[0]);
		Assert.HasCount (1, tag.Performers);
		Assert.AreEqual ("b", tag.Performers[0]);
	}

	[TestMethod]
	public void Performers_Utf16EmptyBomBetweenNames_SkipsThatValue ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (0x61, 0x00);
		builder.Add (0, 0);
		builder.Add (0xFF, 0xFE);
		builder.Add (0, 0);
		builder.Add (0x62, 0x00);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "TPE1", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("a\0\0b", tag.Frames[0].Text);
		Assert.AreEqual (-1, tag.Frames[0].Text.IndexOf ('\uFEFF'));
		Assert.AreEqual ("a", tag.GetTextFrame ("TPE1"));
		Assert.AreEqual ("a", tag.Artist);
		var values = tag.GetTextFrameValues ("TPE1");
		Assert.HasCount (2, values);
		Assert.AreEqual ("a", values[0]);
		Assert.AreEqual ("b", values[1]);
		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("b", tag.Performers[1]);
	}

	[TestMethod]
	public void Comment_Utf16OddLength_KeepsTheLastCharacterAndEndsAtTheNul ()
	{
		using var odd = new BinaryDataBuilder ();
		odd.Add ((byte)TextEncodingType.Utf16WithBom);
		odd.Add ("eng"u8);
		odd.Add (0xFF, 0xFE);
		odd.Add (0, 0);
		odd.Add (0xFF, 0xFE);
		odd.Add (0x41, 0x00);
		odd.Add (0x42, 0x00);
		odd.Add (0x00);

		var kept = CommentFrame.Read (odd.ToBinaryData ().Span, Id3v2Version.V24);
		Assert.IsTrue (kept.IsSuccess, kept.Error);
		Assert.AreEqual ("AB", kept.Frame!.Text);
		AssertNoNul (kept.Frame.Text);

		using var split = new BinaryDataBuilder ();
		split.Add ((byte)TextEncodingType.Utf16WithBom);
		split.Add ("eng"u8);
		split.Add (0xFF, 0xFE);
		split.Add (0, 0);
		split.Add (0xFF, 0xFE);
		split.Add (Encoding.Unicode.GetBytes ("ye"));
		split.Add (0, 0);
		split.Add (Encoding.Unicode.GetBytes ("pa"));
		split.Add (0x00);

		var ended = CommentFrame.Read (split.ToBinaryData ().Span, Id3v2Version.V24);
		Assert.IsTrue (ended.IsSuccess, ended.Error);
		Assert.AreEqual ("ye", ended.Frame!.Text);
		AssertNoNul (ended.Frame.Text);

		using var terminator = new BinaryDataBuilder ();
		terminator.Add ((byte)TextEncodingType.Utf16WithBom);
		terminator.Add ("eng"u8);
		terminator.Add (0xFF, 0xFE);
		terminator.Add (0, 0);
		terminator.Add (0, 0);

		var empty = CommentFrame.Read (terminator.ToBinaryData ().Span, Id3v2Version.V24);
		Assert.IsTrue (empty.IsSuccess, empty.Error);
		Assert.AreEqual ("", empty.Frame!.Text);
	}

	static byte[] UserText (string description, string value)
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf8);
		builder.Add (Encoding.UTF8.GetBytes (description));
		builder.Add (0);
		builder.Add (Encoding.UTF8.GetBytes (value));
		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] Utf16Comment (string language, string description, string text, bool terminated)
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (Encoding.ASCII.GetBytes (language));
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes (description));
		builder.Add (0, 0);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes (text));
		if (terminated)
			builder.Add (0, 0);
		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] Latin1DescribedText (TextEncodingType encoding, string language, string description, string text, bool terminated)
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)encoding);
		builder.Add (Encoding.ASCII.GetBytes (language));
		builder.Add (Encoding.Latin1.GetBytes (description));
		builder.Add (0);
		builder.Add (Encoding.Latin1.GetBytes (text));
		if (terminated)
			builder.Add (0);
		return builder.ToBinaryData ().ToArray ();
	}

	static Id3v2Tag ReadFrame (byte version, string frameId, byte[] content)
	{
		var frame = TestBuilders.Id3v2.CreateFrame (frameId, content, version);
		return ReadTag (version, frame);
	}

	static Id3v2Tag ReadTag (byte version, params byte[][] frames)
	{
		var bodyLength = 0;
		for (var i = 0; i < frames.Length; i++)
			bodyLength += frames[i].Length;

		var header = TestBuilders.Id3v2.CreateHeader (version, (uint)bodyLength);
		var bytes = new byte[header.Length + bodyLength];
		header.CopyTo (bytes, 0);
		var offset = header.Length;
		for (var i = 0; i < frames.Length; i++) {
			frames[i].CopyTo (bytes, offset);
			offset += frames[i].Length;
		}

		var result = Id3v2Tag.Read (bytes);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}

	static void AssertNoNul (string? value)
	{
		Assert.IsNotNull (value);
		Assert.AreEqual (-1, value.IndexOf ('\0'));
	}
}
