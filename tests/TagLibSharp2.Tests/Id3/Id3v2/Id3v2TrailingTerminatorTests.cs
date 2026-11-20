// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// A stored terminator is not part of the text. Internal NULs still separate values.
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
	public void Comment_InternalNul_IsNotASeparator ()
	{
		// Description terminator, then ye + NUL + pa, then a trailing terminator.
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("eng"u8);
		builder.Add (0);
		builder.Add ((byte)'y', (byte)'e', 0, (byte)'p', (byte)'a', 0);

		var tag = ReadFrame (TestConstants.Id3v2.Version4, "COMM", builder.ToBinaryData ().ToArray ());

		Assert.AreEqual ("ye\0pa", tag.Comment);
		Assert.AreEqual (2, tag.Comment!.IndexOf ('\0'));
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
	public void Lyrics_InternalNul_IsNotASeparator ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("eng"u8);
		builder.Add (0);
		builder.Add ((byte)'y', (byte)'e', 0, (byte)'p', (byte)'a', 0);

		var frame = LyricsFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("ye\0pa", frame.Frame!.Text);
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
	public void SyncLyrics_TrailingTerminator_IsNotPartOfTheLine ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("eng"u8);
		builder.Add ((byte)TimestampFormat.Milliseconds);
		builder.Add ((byte)SyncLyricsType.Lyrics);
		builder.Add (0);
		builder.Add ("yepa"u8);
		builder.Add (0);
		builder.Add (0, 0, 0, 1);
		builder.Add ("next"u8);
		builder.Add (0);
		builder.Add (0, 0, 0, 2);

		var frame = SyncLyricsFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("", frame.Frame!.Description);
		Assert.HasCount (2, frame.Frame.SyncItems);
		Assert.AreEqual ("yepa", frame.Frame.SyncItems[0].Text);
		AssertNoNul (frame.Frame.SyncItems[0].Text);
		Assert.AreEqual (1u, frame.Frame.SyncItems[0].Timestamp);
		Assert.AreEqual ("next", frame.Frame.SyncItems[1].Text);
		AssertNoNul (frame.Frame.SyncItems[1].Text);
		Assert.AreEqual (2u, frame.Frame.SyncItems[1].Timestamp);
	}

	[TestMethod]
	public void SyncLyrics_Utf16LineWithTerminator_DropsTrailingNul ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add ("eng"u8);
		builder.Add ((byte)TimestampFormat.Milliseconds);
		builder.Add ((byte)SyncLyricsType.Lyrics);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("desc"));
		builder.Add (0, 0);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("yepa"));
		builder.Add (0, 0);
		builder.Add (0, 0, 0, 5);

		var frame = SyncLyricsFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("desc", frame.Frame!.Description);
		AssertNoNul (frame.Frame.Description);
		Assert.HasCount (1, frame.Frame.SyncItems);
		Assert.AreEqual ("yepa", frame.Frame.SyncItems[0].Text);
		AssertNoNul (frame.Frame.SyncItems[0].Text);
		Assert.AreEqual (5u, frame.Frame.SyncItems[0].Timestamp);
	}

	[TestMethod]
	public void GeneralObject_TrailingTerminator_IsNotPartOfTheText ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Latin1);
		builder.Add ("text/plain"u8);
		builder.Add (0);
		builder.Add ("yepa.txt"u8);
		builder.Add (0);
		builder.Add ("cover"u8);
		builder.Add (0);
		builder.Add (0, 1, 0);

		var frame = GeneralObjectFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("yepa.txt", frame.Frame!.FileName);
		AssertNoNul (frame.Frame.FileName);
		Assert.AreEqual ("cover", frame.Frame.Description);
		AssertNoNul (frame.Frame.Description);
		CollectionAssert.AreEqual (new byte[] { 0, 1, 0 }, frame.Frame.Data.ToArray ());
	}

	[TestMethod]
	public void GeneralObject_Utf16NamesWithTerminator_DropTrailingNul ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add ("application/octet-stream"u8);
		builder.Add (0);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("yepa"));
		builder.Add (0, 0);
		builder.Add (Encoding.Unicode.GetBytes ("note"));
		builder.Add (0, 0);
		builder.Add (9);

		var frame = GeneralObjectFrame.Read (builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual ("yepa", frame.Frame!.FileName);
		AssertNoNul (frame.Frame.FileName);
		Assert.AreEqual ("note", frame.Frame.Description);
		AssertNoNul (frame.Frame.Description);
		CollectionAssert.AreEqual (new byte[] { 9 }, frame.Frame.Data.ToArray ());
	}

	[TestMethod]
	public void InvolvedPeople_TrailingTerminator_DoesNotCollapsePairs ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf8);
		builder.Add ("guitar"u8);
		builder.Add (0);
		builder.Add ("Jane"u8);
		builder.Add (0);
		builder.Add ("bass"u8);
		builder.Add (0);
		builder.Add ("Bob"u8);
		builder.Add (0);

		var frame = InvolvedPeopleFrame.Read ("TMCL", builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual (2, frame.Frame!.Count);
		Assert.AreEqual ("Jane", frame.Frame.GetPerson ("guitar"));
		Assert.AreEqual ("Bob", frame.Frame.GetPerson ("bass"));
		AssertNoNul (frame.Frame.Pairs[0].Role);
		AssertNoNul (frame.Frame.Pairs[0].Person);
		AssertNoNul (frame.Frame.Pairs[1].Role);
		AssertNoNul (frame.Frame.Pairs[1].Person);
	}

	[TestMethod]
	public void InvolvedPeople_Utf16PairsWithTerminator_KeepBothPairs ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add ((byte)TextEncodingType.Utf16WithBom);
		builder.Add (0xFF, 0xFE);
		builder.Add (Encoding.Unicode.GetBytes ("guitar"));
		builder.Add (0, 0);
		builder.Add (Encoding.Unicode.GetBytes ("Jane"));
		builder.Add (0, 0);
		builder.Add (Encoding.Unicode.GetBytes ("bass"));
		builder.Add (0, 0);
		builder.Add (Encoding.Unicode.GetBytes ("Bob"));
		builder.Add (0, 0);

		var frame = InvolvedPeopleFrame.Read ("TIPL", builder.ToBinaryData ().Span, Id3v2Version.V24);

		Assert.IsTrue (frame.IsSuccess, frame.Error);
		Assert.AreEqual (2, frame.Frame!.Count);
		Assert.AreEqual (("guitar", "Jane"), frame.Frame.Pairs[0]);
		Assert.AreEqual (("bass", "Bob"), frame.Frame.Pairs[1]);
		AssertNoNul (frame.Frame.Pairs[0].Person);
		AssertNoNul (frame.Frame.Pairs[1].Person);
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
