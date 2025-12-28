// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class Id3v2TagAdvancedFramesTests
{
	[TestMethod]
	public void SyncLyricsFrames_EmptyByDefault ()
	{
		var tag = new Id3v2Tag ();

		Assert.IsEmpty (tag.SyncLyricsFrames);
	}

	[TestMethod]
	public void AddSyncLyrics_AddsFrame ()
	{
		var tag = new Id3v2Tag ();
		var frame = new SyncLyricsFrame {
			Language = "eng",
			ContentType = SyncLyricsType.Lyrics,
			TimestampFormat = TimestampFormat.Milliseconds,
			Description = "Test"
		};
		frame.AddSyncItem ("Hello", 0);
		frame.AddSyncItem ("World", 500);

		tag.AddSyncLyrics (frame);

		Assert.HasCount (1, tag.SyncLyricsFrames);
		Assert.HasCount (2, tag.SyncLyricsFrames[0].SyncItems);
	}

	[TestMethod]
	public void GetSyncLyrics_ByLanguage_ReturnsMatchingFrame ()
	{
		var tag = new Id3v2Tag ();
		var engFrame = new SyncLyricsFrame { Language = "eng", Description = "English" };
		var fraFrame = new SyncLyricsFrame { Language = "fra", Description = "French" };
		tag.AddSyncLyrics (engFrame);
		tag.AddSyncLyrics (fraFrame);

		var result = tag.GetSyncLyrics (language: "fra");

		Assert.IsNotNull (result);
		Assert.AreEqual ("French", result.Description);
	}

	[TestMethod]
	public void RemoveSyncLyrics_ByLanguage_RemovesMatchingFrames ()
	{
		var tag = new Id3v2Tag ();
		tag.AddSyncLyrics (new SyncLyricsFrame { Language = "eng" });
		tag.AddSyncLyrics (new SyncLyricsFrame { Language = "fra" });
		tag.AddSyncLyrics (new SyncLyricsFrame { Language = "eng" });

		tag.RemoveSyncLyrics (language: "eng");

		Assert.HasCount (1, tag.SyncLyricsFrames);
		Assert.AreEqual ("fra", tag.SyncLyricsFrames[0].Language);
	}

	[TestMethod]
	public void SyncLyrics_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var frame = new SyncLyricsFrame {
			Language = "eng",
			ContentType = SyncLyricsType.Lyrics,
			TimestampFormat = TimestampFormat.Milliseconds,
			Description = "Karaoke"
		};
		frame.AddSyncItem ("Line one", 0);
		frame.AddSyncItem ("Line two", 2000);
		frame.AddSyncItem ("Line three", 4000);
		tag.AddSyncLyrics (frame);

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.Tag!.SyncLyricsFrames);
		var parsed = result.Tag.SyncLyricsFrames[0];
		Assert.AreEqual ("eng", parsed.Language);
		Assert.AreEqual (SyncLyricsType.Lyrics, parsed.ContentType);
		Assert.AreEqual ("Karaoke", parsed.Description);
		Assert.HasCount (3, parsed.SyncItems);
		Assert.AreEqual ("Line one", parsed.SyncItems[0].Text);
		Assert.AreEqual ((uint)0, parsed.SyncItems[0].Timestamp);
		Assert.AreEqual ("Line three", parsed.SyncItems[2].Text);
		Assert.AreEqual ((uint)4000, parsed.SyncItems[2].Timestamp);
	}

	[TestMethod]
	public void GeneralObjectFrames_EmptyByDefault ()
	{
		var tag = new Id3v2Tag ();

		Assert.IsEmpty (tag.GeneralObjectFrames);
	}

	[TestMethod]
	public void AddGeneralObject_AddsFrame ()
	{
		var tag = new Id3v2Tag ();
		var frame = new GeneralObjectFrame (
			"application/json",
			"config.json",
			"Configuration file",
			new BinaryData ([0x7B, 0x7D]));

		tag.AddGeneralObject (frame);

		Assert.HasCount (1, tag.GeneralObjectFrames);
		Assert.AreEqual ("config.json", tag.GeneralObjectFrames[0].FileName);
	}

	[TestMethod]
	public void GetGeneralObject_ByDescription_ReturnsMatchingFrame ()
	{
		var tag = new Id3v2Tag ();
		tag.AddGeneralObject (new GeneralObjectFrame ("text/plain", "a.txt", "First", new BinaryData ([1])));
		tag.AddGeneralObject (new GeneralObjectFrame ("text/plain", "b.txt", "Second", new BinaryData ([2])));

		var result = tag.GetGeneralObject ("Second");

		Assert.IsNotNull (result);
		Assert.AreEqual ("b.txt", result.FileName);
	}

	[TestMethod]
	public void RemoveGeneralObjects_ByDescription_RemovesMatchingFrames ()
	{
		var tag = new Id3v2Tag ();
		tag.AddGeneralObject (new GeneralObjectFrame ("text/plain", "a.txt", "Keep", new BinaryData ([1])));
		tag.AddGeneralObject (new GeneralObjectFrame ("text/plain", "b.txt", "Remove", new BinaryData ([2])));
		tag.AddGeneralObject (new GeneralObjectFrame ("text/plain", "c.txt", "Keep", new BinaryData ([3])));

		tag.RemoveGeneralObjects ("Remove");

		Assert.HasCount (2, tag.GeneralObjectFrames);
		Assert.IsTrue (tag.GeneralObjectFrames.All (f => f.Description == "Keep"));
	}

	[TestMethod]
	public void GeneralObject_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var data = new BinaryData ([0xDE, 0xAD, 0xBE, 0xEF]);
		tag.AddGeneralObject (new GeneralObjectFrame (
			"application/octet-stream",
			"binary.dat",
			"Binary blob",
			data));

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.Tag!.GeneralObjectFrames);
		var parsed = result.Tag.GeneralObjectFrames[0];
		Assert.AreEqual ("application/octet-stream", parsed.MimeType);
		Assert.AreEqual ("binary.dat", parsed.FileName);
		Assert.AreEqual ("Binary blob", parsed.Description);
		Assert.AreEqual (4, parsed.Data.Length);
		Assert.AreEqual (0xDE, parsed.Data.Span[0]);
	}

	[TestMethod]
	public void PrivateFrames_EmptyByDefault ()
	{
		var tag = new Id3v2Tag ();

		Assert.IsEmpty (tag.PrivateFrames);
	}

	[TestMethod]
	public void AddPrivateFrame_AddsFrame ()
	{
		var tag = new Id3v2Tag ();
		var frame = new PrivateFrame ("com.example.app", new BinaryData ([1, 2, 3]));

		tag.AddPrivateFrame (frame);

		Assert.HasCount (1, tag.PrivateFrames);
		Assert.AreEqual ("com.example.app", tag.PrivateFrames[0].OwnerId);
	}

	[TestMethod]
	public void GetPrivateFrame_ByOwnerId_ReturnsMatchingFrame ()
	{
		var tag = new Id3v2Tag ();
		tag.AddPrivateFrame (new PrivateFrame ("org.first", new BinaryData ([1])));
		tag.AddPrivateFrame (new PrivateFrame ("org.second", new BinaryData ([2])));

		var result = tag.GetPrivateFrame ("org.second");

		Assert.IsNotNull (result);
		Assert.AreEqual (2, result.Data.Span[0]);
	}

	[TestMethod]
	public void RemovePrivateFrames_ByOwnerId_RemovesMatchingFrames ()
	{
		var tag = new Id3v2Tag ();
		tag.AddPrivateFrame (new PrivateFrame ("org.keep", new BinaryData ([1])));
		tag.AddPrivateFrame (new PrivateFrame ("org.remove", new BinaryData ([2])));
		tag.AddPrivateFrame (new PrivateFrame ("org.keep", new BinaryData ([3])));

		tag.RemovePrivateFrames ("org.remove");

		Assert.HasCount (2, tag.PrivateFrames);
		Assert.IsTrue (tag.PrivateFrames.All (f => f.OwnerId == "org.keep"));
	}

	[TestMethod]
	public void PrivateFrame_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var data = new BinaryData ([0xCA, 0xFE, 0xBA, 0xBE]);
		tag.AddPrivateFrame (new PrivateFrame ("com.musicbrainz.fingerprint", data));

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.Tag!.PrivateFrames);
		var parsed = result.Tag.PrivateFrames[0];
		Assert.AreEqual ("com.musicbrainz.fingerprint", parsed.OwnerId);
		Assert.AreEqual (4, parsed.Data.Length);
		Assert.AreEqual (0xCA, parsed.Data.Span[0]);
		Assert.AreEqual (0xBE, parsed.Data.Span[3]);
	}

	[TestMethod]
	public void Clear_RemovesAllAdvancedFrames ()
	{
		var tag = new Id3v2Tag ();
		tag.AddSyncLyrics (new SyncLyricsFrame ());
		tag.AddGeneralObject (new GeneralObjectFrame ("text/plain", "test.txt", "Test", new BinaryData ([1])));
		tag.AddPrivateFrame (new PrivateFrame ("test.owner", new BinaryData ([1])));

		tag.Clear ();

		Assert.IsEmpty (tag.SyncLyricsFrames);
		Assert.IsEmpty (tag.GeneralObjectFrames);
		Assert.IsEmpty (tag.PrivateFrames);
	}

	[TestMethod]
	public void MultipleFrameTypes_RoundTrip ()
	{
		var tag = new Id3v2Tag ();

		// Add various frame types
		tag.Title = "Test Song";
		tag.Artist = "Test Artist";

		var syncLyrics = new SyncLyricsFrame { Language = "eng" };
		syncLyrics.AddSyncItem ("Verse 1", 0);
		tag.AddSyncLyrics (syncLyrics);

		tag.AddGeneralObject (new GeneralObjectFrame (
			"image/png",
			"icon.png",
			"Application Icon",
			new BinaryData ([0x89, 0x50, 0x4E, 0x47])));

		tag.AddPrivateFrame (new PrivateFrame (
			"com.myapp.session",
			new BinaryData ([0x01, 0x02, 0x03])));

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Song", result.Tag!.Title);
		Assert.AreEqual ("Test Artist", result.Tag.Artist);
		Assert.HasCount (1, result.Tag.SyncLyricsFrames);
		Assert.HasCount (1, result.Tag.GeneralObjectFrames);
		Assert.HasCount (1, result.Tag.PrivateFrames);
	}
}
