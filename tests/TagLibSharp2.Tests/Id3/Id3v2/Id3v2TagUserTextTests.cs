// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Tests for TXXX (user-defined text) frame support in <see cref="Id3v2Tag"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class Id3v2TagUserTextTests
{
	[TestMethod]
	public void GetUserText_NoFrames_ReturnsNull ()
	{
		var tag = new Id3v2Tag ();

		Assert.IsNull (tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
	}

	[TestMethod]
	public void SetUserText_AddsFrame ()
	{
		var tag = new Id3v2Tag ();

		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		Assert.AreEqual ("-6.50 dB", tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
		Assert.HasCount (1, tag.UserTextFrames);
	}

	[TestMethod]
	public void SetUserText_Null_RemovesFrame ()
	{
		var tag = new Id3v2Tag ();
		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", null);

		Assert.IsNull (tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
		Assert.IsEmpty (tag.UserTextFrames);
	}

	[TestMethod]
	public void SetUserText_CaseInsensitive ()
	{
		var tag = new Id3v2Tag ();
		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		// Setting with different case should replace
		tag.SetUserText ("replaygain_track_gain", "-7.00 dB");

		Assert.AreEqual ("-7.00 dB", tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
		Assert.HasCount (1, tag.UserTextFrames);
	}

	[TestMethod]
	public void GetUserText_CaseInsensitive ()
	{
		var tag = new Id3v2Tag ();
		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		Assert.AreEqual ("-6.50 dB", tag.GetUserText ("replaygain_track_gain"));
	}

	[TestMethod]
	public void AddUserTextFrame_AddsToCollection ()
	{
		var tag = new Id3v2Tag ();
		var frame = new UserTextFrame ("CUSTOM_FIELD", "custom value");

		tag.AddUserTextFrame (frame);

		Assert.HasCount (1, tag.UserTextFrames);
		Assert.AreEqual ("custom value", tag.GetUserText ("CUSTOM_FIELD"));
	}

	[TestMethod]
	public void RemoveUserTextFrames_RemovesMatching ()
	{
		var tag = new Id3v2Tag ();
		tag.SetUserText ("FIELD1", "value1");
		tag.SetUserText ("FIELD2", "value2");

		tag.RemoveUserTextFrames ("FIELD1");

		Assert.IsNull (tag.GetUserText ("FIELD1"));
		Assert.AreEqual ("value2", tag.GetUserText ("FIELD2"));
	}

	[TestMethod]
	public void MultipleUserTextFrames_AllPreserved ()
	{
		var tag = new Id3v2Tag ();
		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");
		tag.SetUserText ("REPLAYGAIN_TRACK_PEAK", "0.988547");
		tag.SetUserText ("MUSICBRAINZ_TRACKID", "f4e7c9d8-1234-5678-9abc-def012345678");

		Assert.HasCount (3, tag.UserTextFrames);
		Assert.AreEqual ("-6.50 dB", tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
		Assert.AreEqual ("0.988547", tag.GetUserText ("REPLAYGAIN_TRACK_PEAK"));
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", tag.GetUserText ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void Clear_RemovesAllUserTextFrames ()
	{
		var tag = new Id3v2Tag ();
		tag.SetUserText ("FIELD1", "value1");
		tag.SetUserText ("FIELD2", "value2");

		tag.Clear ();

		Assert.IsEmpty (tag.UserTextFrames);
	}

	[TestMethod]
	public void RenderAndRead_PreservesUserTextFrames ()
	{
		var tag = new Id3v2Tag ();
		tag.Title = "Test Song";
		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");
		tag.SetUserText ("MUSICBRAINZ_TRACKID", "f4e7c9d8-1234-5678-9abc-def012345678");

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		var read = readResult.Tag!;
		Assert.AreEqual ("Test Song", read.Title);
		Assert.AreEqual ("-6.50 dB", read.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", read.GetUserText ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void RenderAndRead_V23_PreservesUserTextFrames ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V23);
		tag.SetUserText ("REPLAYGAIN_ALBUM_GAIN", "-5.20 dB");

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("-5.20 dB", readResult.Tag!.GetUserText ("REPLAYGAIN_ALBUM_GAIN"));
	}

	[TestMethod]
	public void RenderAndRead_WithStandardAndUserTextFrames_AllPreserved ()
	{
		var tag = new Id3v2Tag ();
		tag.Title = "Test Song";
		tag.Artist = "Test Artist";
		tag.Album = "Test Album";
		tag.Year = "2024";
		tag.Track = 5;
		tag.Comment = "A comment";
		tag.SetUserText ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");
		tag.SetUserText ("REPLAYGAIN_TRACK_PEAK", "0.988547");
		tag.SetUserText ("REPLAYGAIN_ALBUM_GAIN", "-5.20 dB");
		tag.SetUserText ("REPLAYGAIN_ALBUM_PEAK", "1.000000");
		tag.SetUserText ("MUSICBRAINZ_TRACKID", "track-id");
		tag.SetUserText ("MUSICBRAINZ_ALBUMID", "album-id");

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		var read = readResult.Tag!;

		// Standard frames
		Assert.AreEqual ("Test Song", read.Title);
		Assert.AreEqual ("Test Artist", read.Artist);
		Assert.AreEqual ("Test Album", read.Album);
		Assert.AreEqual ("2024", read.Year);
		Assert.AreEqual (5u, read.Track);
		Assert.AreEqual ("A comment", read.Comment);

		// User text frames
		Assert.AreEqual ("-6.50 dB", read.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
		Assert.AreEqual ("0.988547", read.GetUserText ("REPLAYGAIN_TRACK_PEAK"));
		Assert.AreEqual ("-5.20 dB", read.GetUserText ("REPLAYGAIN_ALBUM_GAIN"));
		Assert.AreEqual ("1.000000", read.GetUserText ("REPLAYGAIN_ALBUM_PEAK"));
		Assert.AreEqual ("track-id", read.GetUserText ("MUSICBRAINZ_TRACKID"));
		Assert.AreEqual ("album-id", read.GetUserText ("MUSICBRAINZ_ALBUMID"));
	}
}
