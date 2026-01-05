// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Tests for extended metadata properties: Conductor, Copyright, Compilation,
/// ISRC, Publisher, Lyrics, TotalTracks, TotalDiscs, and UFID frames.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2TagExtendedMetadataTests
{
	static readonly string[] SingleTestValue = ["Test Value"];

	// ===========================================
	// Data-Driven Text Frame Property Tests
	// ===========================================
	// These tests cover properties that map directly to ID3v2 text frames.

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.Conductor), "Herbert von Karajan", "TPE3")]
	[DataRow (nameof (Id3v2Tag.Copyright), "2024 Acme Records", "TCOP")]
	[DataRow (nameof (Id3v2Tag.Isrc), "USRC17607839", "TSRC")]
	[DataRow (nameof (Id3v2Tag.Publisher), "Atlantic Records", "TPUB")]
	[DataRow (nameof (Id3v2Tag.AlbumSort), "White Album, The", "TSOA")]
	[DataRow (nameof (Id3v2Tag.ArtistSort), "Beatles, The", "TSOP")]
	[DataRow (nameof (Id3v2Tag.TitleSort), "Love Is All You Need", "TSOT")]
	[DataRow (nameof (Id3v2Tag.AlbumArtistSort), "Various", "TSO2")]
	[DataRow (nameof (Id3v2Tag.EncodedBy), "LAME 3.100", "TENC")]
	[DataRow (nameof (Id3v2Tag.EncoderSettings), "LAME 320kbps CBR", "TSSE")]
	[DataRow (nameof (Id3v2Tag.Grouping), "Summer Hits 2024", "TIT1")]
	[DataRow (nameof (Id3v2Tag.Subtitle), "Radio Edit", "TIT3")]
	[DataRow (nameof (Id3v2Tag.Remixer), "Tiësto", "TPE4")]
	[DataRow (nameof (Id3v2Tag.InitialKey), "Am", "TKEY")]
	[DataRow (nameof (Id3v2Tag.Mood), "Energetic", "TMOO")]
	[DataRow (nameof (Id3v2Tag.MediaType), "CD", "TMED")]
	[DataRow (nameof (Id3v2Tag.Language), "eng", "TLAN")]
	[DataRow (nameof (Id3v2Tag.ComposerSort), "Bach, Johann Sebastian", "TSOC")]
	[DataRow (nameof (Id3v2Tag.DateTagged), "2025-12-27T10:30:00", "TDTG")]
	public void TextFrameProperty_GetSet_Works (string propertyName, string testValue, string frameId)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;

		property.SetValue (tag, testValue);

		Assert.AreEqual (testValue, property.GetValue (tag));
		Assert.AreEqual (testValue, tag.GetTextFrame (frameId));
	}

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.Conductor), "TPE3")]
	[DataRow (nameof (Id3v2Tag.Copyright), "TCOP")]
	[DataRow (nameof (Id3v2Tag.Isrc), "TSRC")]
	[DataRow (nameof (Id3v2Tag.Publisher), "TPUB")]
	[DataRow (nameof (Id3v2Tag.AlbumSort), "TSOA")]
	[DataRow (nameof (Id3v2Tag.ArtistSort), "TSOP")]
	[DataRow (nameof (Id3v2Tag.TitleSort), "TSOT")]
	[DataRow (nameof (Id3v2Tag.AlbumArtistSort), "TSO2")]
	[DataRow (nameof (Id3v2Tag.EncodedBy), "TENC")]
	[DataRow (nameof (Id3v2Tag.EncoderSettings), "TSSE")]
	[DataRow (nameof (Id3v2Tag.Grouping), "TIT1")]
	[DataRow (nameof (Id3v2Tag.Subtitle), "TIT3")]
	[DataRow (nameof (Id3v2Tag.Remixer), "TPE4")]
	[DataRow (nameof (Id3v2Tag.InitialKey), "TKEY")]
	[DataRow (nameof (Id3v2Tag.Mood), "TMOO")]
	[DataRow (nameof (Id3v2Tag.MediaType), "TMED")]
	[DataRow (nameof (Id3v2Tag.Language), "TLAN")]
	[DataRow (nameof (Id3v2Tag.ComposerSort), "TSOC")]
	[DataRow (nameof (Id3v2Tag.DateTagged), "TDTG")]
	public void TextFrameProperty_SetNull_ClearsField (string propertyName, string frameId)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;

		property.SetValue (tag, "Test Value");
		property.SetValue (tag, null);

		Assert.IsNull (property.GetValue (tag));
		Assert.IsNull (tag.GetTextFrame (frameId));
	}

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.Conductor), "Sir Simon Rattle")]
	[DataRow (nameof (Id3v2Tag.Copyright), "2025 Independent")]
	[DataRow (nameof (Id3v2Tag.Isrc), "BRBMG0300729")]
	[DataRow (nameof (Id3v2Tag.Publisher), "4AD Records")]
	[DataRow (nameof (Id3v2Tag.AlbumSort), "Abbey Road")]
	[DataRow (nameof (Id3v2Tag.ArtistSort), "Radiohead")]
	[DataRow (nameof (Id3v2Tag.TitleSort), "Yesterday")]
	[DataRow (nameof (Id3v2Tag.AlbumArtistSort), "Compilation Artists")]
	[DataRow (nameof (Id3v2Tag.EncodedBy), "iTunes 12.0")]
	[DataRow (nameof (Id3v2Tag.EncoderSettings), "VBR V0")]
	[DataRow (nameof (Id3v2Tag.Grouping), "Workout Mix")]
	[DataRow (nameof (Id3v2Tag.Subtitle), "Extended Mix")]
	[DataRow (nameof (Id3v2Tag.Remixer), "David Guetta")]
	[DataRow (nameof (Id3v2Tag.InitialKey), "Ebm")]
	[DataRow (nameof (Id3v2Tag.Mood), "Melancholic")]
	[DataRow (nameof (Id3v2Tag.MediaType), "DIG/A")]
	[DataRow (nameof (Id3v2Tag.Language), "jpn")]
	[DataRow (nameof (Id3v2Tag.ComposerSort), "Mozart, Wolfgang Amadeus")]
	[DataRow (nameof (Id3v2Tag.DateTagged), "2025-12-27")]
	public void TextFrameProperty_RoundTrip_PreservesValue (string propertyName, string testValue)
	{
		var original = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		property.SetValue (original, testValue);

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (testValue, property.GetValue (result.Tag));
	}

	[TestMethod]
	[DataRow ("TPE3", "Leonard Bernstein", nameof (Id3v2Tag.Conductor))]
	[DataRow ("TCOP", "2023 Universal Music", nameof (Id3v2Tag.Copyright))]
	[DataRow ("TSRC", "GBAYE0000351", nameof (Id3v2Tag.Isrc))]
	[DataRow ("TPUB", "Sub Pop Records", nameof (Id3v2Tag.Publisher))]
	public void TextFrameProperty_FromFile_ParsesCorrectly (string frameId, string text, string propertyName)
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (frameId, text, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		Assert.AreEqual (text, property.GetValue (result.Tag));
	}

	// ===========================================
	// User Text Frame (TXXX) Property Tests
	// ===========================================

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.Barcode), "012345678901", "BARCODE")]
	[DataRow (nameof (Id3v2Tag.CatalogNumber), "WPCR-80001", "CATALOGNUMBER")]
	[DataRow (nameof (Id3v2Tag.Description), "A story about love and loss", "DESCRIPTION")]
	[DataRow (nameof (Id3v2Tag.AmazonId), "B000002UAL", "ASIN")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzWorkId), "1a2b3c4d-5e6f-7890-abcd-ef1234567890", "MusicBrainz Work Id")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzDiscId), "XHLQnC.F3SJ5XpDPLt7gLfHAy_A-", "MusicBrainz Disc Id")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzReleaseStatus), "official", "MusicBrainz Album Status")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzReleaseType), "album", "MusicBrainz Album Type")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzReleaseCountry), "US", "MusicBrainz Album Release Country")]
	public void UserTextProperty_GetSet_Works (string propertyName, string testValue, string description)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;

		property.SetValue (tag, testValue);

		Assert.AreEqual (testValue, property.GetValue (tag));
		Assert.AreEqual (testValue, tag.GetUserText (description));
	}

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.Barcode), "5099749534728")]
	[DataRow (nameof (Id3v2Tag.CatalogNumber), "ECM 1064/65")]
	[DataRow (nameof (Id3v2Tag.Description), "Epic adventure through space")]
	[DataRow (nameof (Id3v2Tag.AmazonId), "B00005NQ6Z")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzWorkId), "deadbeef-1234-5678-90ab-cdef12345678")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzDiscId), "IbhKz8W2xPbLqA1F5nPKz8xLUBc-")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzReleaseStatus), "promotional")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzReleaseType), "compilation")]
	[DataRow (nameof (Id3v2Tag.MusicBrainzReleaseCountry), "GB")]
	public void UserTextProperty_RoundTrip_PreservesValue (string propertyName, string testValue)
	{
		var original = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		property.SetValue (original, testValue);

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (testValue, property.GetValue (result.Tag));
	}

	// ===========================================
	// Boolean Property Tests (IsCompilation)
	// ===========================================

	[TestMethod]
	public void IsCompilation_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.IsCompilation = true;

		Assert.IsTrue (tag.IsCompilation);
		Assert.AreEqual ("1", tag.GetTextFrame ("TCMP"));
	}

	[TestMethod]
	public void IsCompilation_SetFalse_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.IsCompilation = true;

		tag.IsCompilation = false;

		Assert.IsFalse (tag.IsCompilation);
		Assert.IsNull (tag.GetTextFrame ("TCMP"));
	}

	[TestMethod]
	[DataRow ("1", true)]
	[DataRow ("0", false)]
	public void IsCompilation_FromFile_Parses (string frameValue, bool expected)
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame ("TCMP", frameValue, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (expected, result.Tag!.IsCompilation);
	}

	[TestMethod]
	public void IsCompilation_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { IsCompilation = true };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Tag!.IsCompilation);
	}

	// ===========================================
	// TotalTracks/TotalDiscs Tests
	// ===========================================

	[TestMethod]
	public void TotalTracks_GetFromSlashFormat_Works ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame ("TRCK", "5/12", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5u, result.Tag!.Track);
		Assert.AreEqual (12u, result.Tag.TotalTracks);
	}

	[TestMethod]
	public void TotalTracks_SetWithTrack_FormatsCorrectly ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Track = 3;
		tag.TotalTracks = 10;

		Assert.AreEqual ("3/10", tag.GetTextFrame ("TRCK"));
	}

	[TestMethod]
	public void TotalTracks_SetWithoutTrack_SetsTrackToOne ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.TotalTracks = 15;

		Assert.AreEqual ("1/15", tag.GetTextFrame ("TRCK"));
	}

	[TestMethod]
	public void TotalTracks_SetNull_PreservesTrackOnly ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Track = 5;
		tag.TotalTracks = 12;

		tag.TotalTracks = null;

		Assert.AreEqual ("5", tag.GetTextFrame ("TRCK"));
		Assert.AreEqual (5u, tag.Track);
	}

	[TestMethod]
	public void TotalTracks_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Track = 7,
			TotalTracks = 14
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (7u, result.Tag!.Track);
		Assert.AreEqual (14u, result.Tag.TotalTracks);
	}

	[TestMethod]
	public void TotalDiscs_GetFromSlashFormat_Works ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame ("TPOS", "2/3", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2u, result.Tag!.DiscNumber);
		Assert.AreEqual (3u, result.Tag.TotalDiscs);
	}

	[TestMethod]
	public void TotalDiscs_SetWithDisc_FormatsCorrectly ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.DiscNumber = 1;
		tag.TotalDiscs = 2;

		Assert.AreEqual ("1/2", tag.GetTextFrame ("TPOS"));
	}

	[TestMethod]
	public void TotalDiscs_SetWithoutDisc_SetsDiscToOne ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.TotalDiscs = 3;

		Assert.AreEqual ("1/3", tag.GetTextFrame ("TPOS"));
	}

	[TestMethod]
	public void TotalDiscs_SetNull_PreservesDiscOnly ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.DiscNumber = 2;
		tag.TotalDiscs = 3;

		tag.TotalDiscs = null;

		Assert.AreEqual ("2", tag.GetTextFrame ("TPOS"));
		Assert.AreEqual (2u, tag.DiscNumber);
	}

	[TestMethod]
	public void TotalDiscs_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			DiscNumber = 2,
			TotalDiscs = 4
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2u, result.Tag!.DiscNumber);
		Assert.AreEqual (4u, result.Tag.TotalDiscs);
	}

	// ===========================================
	// Original Release Date (TDOR/TORY) Tests
	// ===========================================

	[TestMethod]
	public void OriginalReleaseDate_V24_UseTDOR ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.OriginalReleaseDate = "1969-09-26";

		Assert.AreEqual ("1969-09-26", tag.OriginalReleaseDate);
		Assert.AreEqual ("1969-09-26", tag.GetTextFrame ("TDOR"));
		Assert.IsNull (tag.GetTextFrame ("TORY"));
	}

	[TestMethod]
	public void OriginalReleaseDate_V23_UseTORY ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V23);

		tag.OriginalReleaseDate = "1969";

		Assert.AreEqual ("1969", tag.OriginalReleaseDate);
		Assert.AreEqual ("1969", tag.GetTextFrame ("TORY"));
		Assert.IsNull (tag.GetTextFrame ("TDOR"));
	}

	[TestMethod]
	public void OriginalReleaseDate_V24_FallsBackToTORY ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame ("TORY", "1969", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1969", result.Tag!.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_V24_PrefersTDOROverTORY ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.OriginalReleaseDate = "1969-09-26";
		tag.SetTextFrameValues ("TORY", ["1969"]);

		Assert.AreEqual ("1969-09-26", tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.OriginalReleaseDate = "1969-09-26";

		tag.OriginalReleaseDate = null;

		Assert.IsNull (tag.OriginalReleaseDate);
		Assert.IsNull (tag.GetTextFrame ("TDOR"));
	}

	[TestMethod]
	[DataRow ("V24", "1969-09-26")]
	[DataRow ("V23", "1969")]
	public void OriginalReleaseDate_RoundTrip (string versionName, string testValue)
	{
		var version = versionName == "V24" ? Id3v2Version.V24 : Id3v2Version.V23;
		var original = new Id3v2Tag (version) { OriginalReleaseDate = testValue };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (testValue, result.Tag!.OriginalReleaseDate);
	}

	// ===========================================
	// Special Cases - InitialKey Sharp
	// ===========================================

	[TestMethod]
	public void InitialKey_SharpKey_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.InitialKey = "F#m";

		Assert.AreEqual ("F#m", tag.InitialKey);
	}

	// ===========================================
	// Multi-Value String Array Tests
	// ===========================================

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.PerformersSort))]
	[DataRow (nameof (Id3v2Tag.AlbumArtistsSort))]
	[DataRow (nameof (Id3v2Tag.ComposersSort))]
	public void StringArrayProperty_SingleValue_GetSet_Works (string propertyName)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		var testValue = new[] { "Test Value" };

		property.SetValue (tag, testValue);

		var result = (string[]?)property.GetValue (tag);
		Assert.IsNotNull (result);
		Assert.HasCount (1, result);
		Assert.AreEqual ("Test Value", result[0]);
	}

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.PerformersSort))]
	[DataRow (nameof (Id3v2Tag.AlbumArtistsSort))]
	[DataRow (nameof (Id3v2Tag.ComposersSort))]
	public void StringArrayProperty_MultipleValues_GetSet_Works (string propertyName)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		var testValues = new[] { "Value One", "Value Two" };

		property.SetValue (tag, testValues);

		var result = (string[]?)property.GetValue (tag);
		Assert.IsNotNull (result);
		Assert.HasCount (2, result);
		Assert.AreEqual ("Value One", result[0]);
		Assert.AreEqual ("Value Two", result[1]);
	}

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.PerformersSort))]
	[DataRow (nameof (Id3v2Tag.AlbumArtistsSort))]
	[DataRow (nameof (Id3v2Tag.ComposersSort))]
	public void StringArrayProperty_SetNull_ClearsValue (string propertyName)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		property.SetValue (tag, SingleTestValue);

		property.SetValue (tag, null);

		Assert.IsNull (property.GetValue (tag));
	}

	[TestMethod]
	[DataRow (nameof (Id3v2Tag.PerformersSort))]
	[DataRow (nameof (Id3v2Tag.AlbumArtistsSort))]
	[DataRow (nameof (Id3v2Tag.ComposersSort))]
	public void StringArrayProperty_RoundTrip_PreservesMultipleValues (string propertyName)
	{
		var original = new Id3v2Tag (Id3v2Version.V24);
		var property = typeof (Id3v2Tag).GetProperty (propertyName)!;
		var testValues = new[] { "Value One", "Value Two", "Value Three" };
		property.SetValue (original, testValues);

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		var resultValues = (string[]?)property.GetValue (result.Tag);
		Assert.IsNotNull (resultValues);
		Assert.HasCount (3, resultValues);
		Assert.AreEqual ("Value One", resultValues[0]);
		Assert.AreEqual ("Value Two", resultValues[1]);
		Assert.AreEqual ("Value Three", resultValues[2]);
	}

	// ===========================================
	// Multi-Value Text Frame Tests
	// ===========================================

	[TestMethod]
	public void Artists_SingleValue_ReturnsOneElement ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "The Beatles" };

		var artists = tag.Performers;

		Assert.HasCount (1, artists);
		Assert.AreEqual ("The Beatles", artists[0]);
	}

	[TestMethod]
	public void Artists_NullSeparated_ReturnsMultipleElements ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.SetTextFrameValues ("TPE1", ["John Lennon", "Paul McCartney"]);

		var artists = tag.Performers;

		Assert.HasCount (2, artists);
		Assert.AreEqual ("John Lennon", artists[0]);
		Assert.AreEqual ("Paul McCartney", artists[1]);
	}

	[TestMethod]
	public void Artists_SlashSeparated_ReturnsMultipleElements ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "Daft Punk / The Weeknd" };

		var artists = tag.Performers;

		Assert.HasCount (2, artists);
		Assert.AreEqual ("Daft Punk", artists[0]);
		Assert.AreEqual ("The Weeknd", artists[1]);
	}

	[TestMethod]
	public void GetTextFrameValues_EmptyFrame_ReturnsEmptyList ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		var artists = tag.GetTextFrameValues ("TPE1");

		Assert.IsEmpty (artists);
	}

	[TestMethod]
	public void SetTextFrameValues_Null_RemovesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "Test Artist" };

		tag.SetTextFrameValues ("TPE1", null);

		Assert.IsNull (tag.Artist);
		Assert.IsEmpty (tag.Performers);
	}

	[TestMethod]
	public void SetTextFrameValues_Empty_RemovesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "Test Artist" };

		tag.SetTextFrameValues ("TPE1", []);

		Assert.IsNull (tag.Artist);
	}

	[TestMethod]
	public void Genres_MultiValue_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.SetTextFrameValues ("TCON", ["Rock", "Pop", "Electronic"]);

		var genres = tag.Genres;

		Assert.HasCount (3, genres);
		Assert.AreEqual ("Rock", genres[0]);
		Assert.AreEqual ("Pop", genres[1]);
		Assert.AreEqual ("Electronic", genres[2]);
	}

	[TestMethod]
	public void Composers_MultiValue_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.SetTextFrameValues ("TCOM", ["Lennon", "McCartney"]);

		var composers = tag.Composers;

		Assert.HasCount (2, composers);
		Assert.AreEqual ("Lennon", composers[0]);
		Assert.AreEqual ("McCartney", composers[1]);
	}

	[TestMethod]
	public void AlbumArtists_MultiValue_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.SetTextFrameValues ("TPE2", ["Various Artists", "Compilation"]);

		var albumArtists = tag.AlbumArtists;

		Assert.HasCount (2, albumArtists);
		Assert.AreEqual ("Various Artists", albumArtists[0]);
		Assert.AreEqual ("Compilation", albumArtists[1]);
	}

	[TestMethod]
	public void MultiValue_SlashSeparated_RoundTrip_PreservesValues ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Artist = "Artist One / Artist Two / Artist Three" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		var artists = result.Tag!.Performers;
		Assert.HasCount (3, artists);
		Assert.AreEqual ("Artist One", artists[0]);
		Assert.AreEqual ("Artist Two", artists[1]);
		Assert.AreEqual ("Artist Three", artists[2]);
	}

	// ===========================================
	// MusicBrainz Recording ID (UFID) Tests
	// ===========================================

	[TestMethod]
	public void MusicBrainzRecordingId_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicBrainzRecordingId = "c6b36210-7812-4b57-a48a-8bf78e0d7f82";

		Assert.AreEqual ("c6b36210-7812-4b57-a48a-8bf78e0d7f82", tag.MusicBrainzRecordingId);
		var ufid = tag.GetUniqueFileId (UniqueFileIdFrame.MusicBrainzOwner);
		Assert.IsNotNull (ufid);
		Assert.AreEqual ("c6b36210-7812-4b57-a48a-8bf78e0d7f82", ufid.IdentifierString);
	}

	[TestMethod]
	public void MusicBrainzRecordingId_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.MusicBrainzRecordingId = "c6b36210-7812-4b57-a48a-8bf78e0d7f82";

		tag.MusicBrainzRecordingId = null;

		Assert.IsNull (tag.MusicBrainzRecordingId);
		Assert.IsNull (tag.GetUniqueFileId (UniqueFileIdFrame.MusicBrainzOwner));
	}

	[TestMethod]
	public void MusicBrainzRecordingId_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			MusicBrainzRecordingId = "deadbeef-1234-5678-90ab-cdef12345678"
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("deadbeef-1234-5678-90ab-cdef12345678", result.Tag!.MusicBrainzRecordingId);
	}

	// ===========================================
	// PerformersRole (TMCL frame) Tests
	// ===========================================

	[TestMethod]
	public void PerformersRole_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.PerformersRole = ["lead vocals", "guitar"];

		Assert.IsNotNull (tag.PerformersRole);
		Assert.HasCount (2, tag.PerformersRole);
		Assert.AreEqual ("lead vocals", tag.PerformersRole[0]);
		Assert.AreEqual ("guitar", tag.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["lead vocals"];

		tag.PerformersRole = null;

		Assert.IsTrue (tag.PerformersRole is null || tag.PerformersRole.Length == 0);
	}

	[TestMethod]
	public void PerformersRole_SetEmpty_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["lead vocals"];

		tag.PerformersRole = [];

		Assert.IsTrue (tag.PerformersRole is null || tag.PerformersRole.Length == 0);
	}

	[TestMethod]
	public void PerformersRole_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24);
		original.PerformersRole = ["lead vocals", "rhythm guitar", "bass"];

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (3, result.Tag.PerformersRole);
		Assert.AreEqual ("lead vocals", result.Tag.PerformersRole[0]);
		Assert.AreEqual ("rhythm guitar", result.Tag.PerformersRole[1]);
		Assert.AreEqual ("bass", result.Tag.PerformersRole[2]);
	}

	[TestMethod]
	public void PerformersRole_WithArtists_MaintainsParallelArrays ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.SetTextFrameValues ("TPE1", ["John Smith", "Jane Doe"]);
		tag.PerformersRole = ["vocals", "guitar"];

		Assert.HasCount (2, tag.Performers);
		Assert.HasCount (2, tag.PerformersRole!);
		Assert.AreEqual ("John Smith", tag.Performers[0]);
		Assert.AreEqual ("vocals", tag.PerformersRole[0]);
		Assert.AreEqual ("Jane Doe", tag.Performers[1]);
		Assert.AreEqual ("guitar", tag.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_UnicodeCharacters_PreservesText ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.PerformersRole = ["ボーカル", "ギター"]; // Japanese: "vocals", "guitar"

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (2, result.Tag.PerformersRole);
		Assert.AreEqual ("ボーカル", result.Tag.PerformersRole[0]);
		Assert.AreEqual ("ギター", result.Tag.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_SingleValue_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.PerformersRole = ["all instruments"];

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (1, result.Tag.PerformersRole);
		Assert.AreEqual ("all instruments", result.Tag.PerformersRole[0]);
	}

	// ===========================================
	// PerformersRole Caching Tests
	// ===========================================

	[TestMethod]
	public void PerformersRole_IsCached_ReturnsSameInstance ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["vocals", "guitar"];

		var first = tag.PerformersRole;
		var second = tag.PerformersRole;

		Assert.AreSame (first, second);
	}

	[TestMethod]
	public void PerformersRole_CacheInvalidated_WhenSetterCalled ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["vocals", "guitar"];

		var first = tag.PerformersRole;
		tag.PerformersRole = ["drums"];
		var second = tag.PerformersRole;

		Assert.AreNotSame (first, second);
		Assert.HasCount (1, second!);
		Assert.AreEqual ("drums", second[0]);
	}

	[TestMethod]
	public void PerformersRole_CacheInvalidated_WhenFrameAdded ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["vocals"];

		var first = tag.PerformersRole;

		var newFrame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		newFrame.Add ("bass", "Paul McCartney");
		tag.AddInvolvedPeopleFrame (newFrame);

		var second = tag.PerformersRole;

		Assert.AreNotSame (first, second);
	}

	[TestMethod]
	public void PerformersRole_CacheInvalidated_WhenFramesRemoved ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["vocals", "guitar"];

		var first = tag.PerformersRole;
		tag.RemoveInvolvedPeopleFrames ("TMCL");
		var second = tag.PerformersRole;

		Assert.AreNotSame (first, second);
		Assert.IsNull (second);
	}

	[TestMethod]
	public void PerformersRole_CacheInvalidated_WhenCleared ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = ["vocals", "guitar"];

		var first = tag.PerformersRole;
		tag.Clear ();
		var second = tag.PerformersRole;

		Assert.AreNotSame (first, second);
		Assert.IsNull (second);
	}
}
