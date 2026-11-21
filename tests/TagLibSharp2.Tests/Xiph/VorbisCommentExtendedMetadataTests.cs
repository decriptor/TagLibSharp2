// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

/// <summary>
/// Tests for extended metadata properties in VorbisComment: Conductor, Copyright,
/// Compilation, ISRC, Publisher, and Lyrics.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class VorbisCommentExtendedMetadataTests
{
	static readonly string[] SingleTestValue = ["Test Value"];

	// ===========================================
	// Data-Driven String Property Tests
	// ===========================================
	// These tests cover properties that map directly to Vorbis Comment fields.
	// Each property is tested for get/set, null clearing, and round-trip serialization.

	[TestMethod]
	[DataRow (nameof (VorbisComment.Conductor), "Herbert von Karajan", "CONDUCTOR")]
	[DataRow (nameof (VorbisComment.Copyright), "2024 Acme Records", "COPYRIGHT")]
	[DataRow (nameof (VorbisComment.Isrc), "USRC17607839", "ISRC")]
	[DataRow (nameof (VorbisComment.Publisher), "Sub Pop Records", "LABEL")]
	[DataRow (nameof (VorbisComment.AlbumSort), "White Album, The", "ALBUMSORT")]
	[DataRow (nameof (VorbisComment.ArtistSort), "Beatles, The", "ARTISTSORT")]
	[DataRow (nameof (VorbisComment.TitleSort), "Love Is All You Need", "TITLESORT")]
	[DataRow (nameof (VorbisComment.AlbumArtistSort), "Various", "ALBUMARTISTSORT")]
	[DataRow (nameof (VorbisComment.Lyrics), "Hello world, these are the lyrics", "LYRICS")]
	[DataRow (nameof (VorbisComment.EncodedBy), "LAME 3.100", "ENCODED-BY")]
	[DataRow (nameof (VorbisComment.EncoderSettings), "Lavf58.29.100", "ENCODER")]
	[DataRow (nameof (VorbisComment.Grouping), "Summer Hits 2024", "GROUPING")]
	[DataRow (nameof (VorbisComment.Subtitle), "Radio Edit", "SUBTITLE")]
	[DataRow (nameof (VorbisComment.Remixer), "Tiësto", "REMIXER")]
	[DataRow (nameof (VorbisComment.InitialKey), "Am", "KEY")]
	[DataRow (nameof (VorbisComment.Mood), "Energetic", "MOOD")]
	[DataRow (nameof (VorbisComment.MediaType), "CD", "MEDIA")]
	[DataRow (nameof (VorbisComment.Language), "eng", "LANGUAGE")]
	[DataRow (nameof (VorbisComment.Barcode), "012345678901", "BARCODE")]
	[DataRow (nameof (VorbisComment.CatalogNumber), "WPCR-80001", "CATALOGNUMBER")]
	[DataRow (nameof (VorbisComment.ComposerSort), "Bach, Johann Sebastian", "COMPOSERSORT")]
	[DataRow (nameof (VorbisComment.DateTagged), "2025-12-27T10:30:00", "DATETAGGED")]
	[DataRow (nameof (VorbisComment.Description), "A story about love and loss", "DESCRIPTION")]
	[DataRow (nameof (VorbisComment.AmazonId), "B000002UAL", "ASIN")]
	[DataRow (nameof (VorbisComment.MusicBrainzWorkId), "1a2b3c4d-5e6f-7890-abcd-ef1234567890", "MUSICBRAINZ_WORKID")]
	[DataRow (nameof (VorbisComment.MusicBrainzDiscId), "XHLQnC.F3SJ5XpDPLt7gLfHAy_A-", "MUSICBRAINZ_DISCID")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseStatus), "official", "RELEASESTATUS")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseType), "album", "RELEASETYPE")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseCountry), "US", "RELEASECOUNTRY")]
	[DataRow (nameof (VorbisComment.PodcastFeedUrl), "https://example.com/feed.rss", "PODCASTFEEDURL")]
	public void StringProperty_GetSet_Works (string propertyName, string testValue, string fieldName)
	{
		var comment = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;

		property.SetValue (comment, testValue);

		Assert.AreEqual (testValue, property.GetValue (comment));
		Assert.AreEqual (testValue, comment.GetValue (fieldName));
	}

	[TestMethod]
	[DataRow (nameof (VorbisComment.Conductor), "CONDUCTOR")]
	[DataRow (nameof (VorbisComment.Copyright), "COPYRIGHT")]
	[DataRow (nameof (VorbisComment.Isrc), "ISRC")]
	[DataRow (nameof (VorbisComment.Publisher), "LABEL")]
	[DataRow (nameof (VorbisComment.AlbumSort), "ALBUMSORT")]
	[DataRow (nameof (VorbisComment.ArtistSort), "ARTISTSORT")]
	[DataRow (nameof (VorbisComment.TitleSort), "TITLESORT")]
	[DataRow (nameof (VorbisComment.AlbumArtistSort), "ALBUMARTISTSORT")]
	[DataRow (nameof (VorbisComment.Lyrics), "LYRICS")]
	[DataRow (nameof (VorbisComment.EncodedBy), "ENCODED-BY")]
	[DataRow (nameof (VorbisComment.EncoderSettings), "ENCODER")]
	[DataRow (nameof (VorbisComment.Grouping), "GROUPING")]
	[DataRow (nameof (VorbisComment.Subtitle), "SUBTITLE")]
	[DataRow (nameof (VorbisComment.Remixer), "REMIXER")]
	[DataRow (nameof (VorbisComment.InitialKey), "KEY")]
	[DataRow (nameof (VorbisComment.Mood), "MOOD")]
	[DataRow (nameof (VorbisComment.MediaType), "MEDIA")]
	[DataRow (nameof (VorbisComment.Language), "LANGUAGE")]
	[DataRow (nameof (VorbisComment.Barcode), "BARCODE")]
	[DataRow (nameof (VorbisComment.CatalogNumber), "CATALOGNUMBER")]
	[DataRow (nameof (VorbisComment.ComposerSort), "COMPOSERSORT")]
	[DataRow (nameof (VorbisComment.DateTagged), "DATETAGGED")]
	[DataRow (nameof (VorbisComment.Description), "DESCRIPTION")]
	[DataRow (nameof (VorbisComment.AmazonId), "ASIN")]
	[DataRow (nameof (VorbisComment.MusicBrainzWorkId), "MUSICBRAINZ_WORKID")]
	[DataRow (nameof (VorbisComment.MusicBrainzDiscId), "MUSICBRAINZ_DISCID")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseStatus), "RELEASESTATUS")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseType), "RELEASETYPE")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseCountry), "RELEASECOUNTRY")]
	[DataRow (nameof (VorbisComment.PodcastFeedUrl), "PODCASTFEEDURL")]
	public void StringProperty_SetNull_ClearsField (string propertyName, string fieldName)
	{
		var comment = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;

		property.SetValue (comment, "Test Value");
		property.SetValue (comment, null);

		Assert.IsNull (property.GetValue (comment));
		Assert.IsNull (comment.GetValue (fieldName));
	}

	[TestMethod]
	[DataRow (nameof (VorbisComment.Conductor), "Sir Simon Rattle")]
	[DataRow (nameof (VorbisComment.Copyright), "2025 Independent")]
	[DataRow (nameof (VorbisComment.Isrc), "GBAYE0000351")]
	[DataRow (nameof (VorbisComment.Publisher), "4AD Records")]
	[DataRow (nameof (VorbisComment.AlbumSort), "Abbey Road")]
	[DataRow (nameof (VorbisComment.ArtistSort), "Radiohead")]
	[DataRow (nameof (VorbisComment.TitleSort), "Yesterday")]
	[DataRow (nameof (VorbisComment.AlbumArtistSort), "Compilation Artists")]
	[DataRow (nameof (VorbisComment.Lyrics), "Verse 1\nChorus\nVerse 2")]
	[DataRow (nameof (VorbisComment.EncodedBy), "flac 1.4.0")]
	[DataRow (nameof (VorbisComment.EncoderSettings), "libFLAC 1.3.2")]
	[DataRow (nameof (VorbisComment.Grouping), "Workout Mix")]
	[DataRow (nameof (VorbisComment.Subtitle), "Extended Mix")]
	[DataRow (nameof (VorbisComment.Remixer), "David Guetta")]
	[DataRow (nameof (VorbisComment.InitialKey), "F#m")]
	[DataRow (nameof (VorbisComment.Mood), "Melancholic")]
	[DataRow (nameof (VorbisComment.MediaType), "Vinyl")]
	[DataRow (nameof (VorbisComment.Language), "jpn")]
	[DataRow (nameof (VorbisComment.Barcode), "5099749534728")]
	[DataRow (nameof (VorbisComment.CatalogNumber), "ECM 1064/65")]
	[DataRow (nameof (VorbisComment.ComposerSort), "Mozart, Wolfgang Amadeus")]
	[DataRow (nameof (VorbisComment.DateTagged), "2025-12-27")]
	[DataRow (nameof (VorbisComment.Description), "Epic adventure through space")]
	[DataRow (nameof (VorbisComment.AmazonId), "B00005NQ6Z")]
	[DataRow (nameof (VorbisComment.MusicBrainzWorkId), "deadbeef-1234-5678-90ab-cdef12345678")]
	[DataRow (nameof (VorbisComment.MusicBrainzDiscId), "IbhKz8W2xPbLqA1F5nPKz8xLUBc-")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseStatus), "promotional")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseType), "compilation")]
	[DataRow (nameof (VorbisComment.MusicBrainzReleaseCountry), "GB")]
	public void StringProperty_RoundTrip_PreservesValue (string propertyName, string testValue)
	{
		var original = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		property.SetValue (original, testValue);

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (testValue, property.GetValue (result.Tag));
	}

	// ===========================================
	// Boolean Property Tests (IsCompilation)
	// ===========================================

	[TestMethod]
	public void IsCompilation_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.IsCompilation = true;

		Assert.IsTrue (comment.IsCompilation);
		Assert.AreEqual ("1", comment.GetValue ("COMPILATION"));
	}

	[TestMethod]
	public void IsCompilation_SetFalse_ClearsField ()
	{
		var comment = new VorbisComment ("test");
		comment.IsCompilation = true;

		comment.IsCompilation = false;

		Assert.IsFalse (comment.IsCompilation);
		Assert.IsNull (comment.GetValue ("COMPILATION"));
	}

	[TestMethod]
	public void IsCompilation_RoundTrip_PreservesValue ()
	{
		var original = new VorbisComment ("test") { IsCompilation = true };

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Tag!.IsCompilation);
	}

	// ===========================================
	// Uint Property Tests (TotalTracks, TotalDiscs)
	// ===========================================

	[TestMethod]
	public void TotalTracks_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.TotalTracks = 12;

		Assert.AreEqual (12u, comment.TotalTracks);
		Assert.AreEqual ("12", comment.GetValue ("TOTALTRACKS"));
	}

	[TestMethod]
	public void TotalTracks_FromTrackNumber_ParsesSlashFormat ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("TRACKNUMBER", "5/12");

		Assert.AreEqual (5u, comment.Track);
		Assert.AreEqual (12u, comment.TotalTracks);
	}

	[TestMethod]
	public void TotalDiscs_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.TotalDiscs = 3;

		Assert.AreEqual (3u, comment.TotalDiscs);
		Assert.AreEqual ("3", comment.GetValue ("TOTALDISCS"));
	}

	[TestMethod]
	public void TotalDiscs_FallsBackToDiscTotal ()
	{
		var comment = new VorbisComment ("test");

		// Some taggers use DISCTOTAL instead of TOTALDISCS
		comment.SetValue ("DISCTOTAL", "5");

		Assert.AreEqual (5u, comment.TotalDiscs);
	}

	[TestMethod]
	public void TotalDiscs_PrefersTotalDiscsOverDiscTotal ()
	{
		var comment = new VorbisComment ("test");

		// When both exist, prefer TOTALDISCS
		comment.SetValue ("TOTALDISCS", "3");
		comment.SetValue ("DISCTOTAL", "5");

		Assert.AreEqual (3u, comment.TotalDiscs);
	}

	[TestMethod]
	public void TotalDiscs_Set_RemovesDiscTotal ()
	{
		var comment = new VorbisComment ("test");
		comment.SetValue ("DISCTOTAL", "4");

		comment.TotalDiscs = 2;

		Assert.AreEqual (2u, comment.TotalDiscs);
		Assert.AreEqual ("2", comment.GetValue ("TOTALDISCS"));
		Assert.IsNull (comment.GetValue ("DISCTOTAL"));
	}

	[TestMethod]
	public void TotalDiscs_SetNull_ClearsDiscTotal ()
	{
		var comment = new VorbisComment ("test");
		comment.SetValue ("DISCTOTAL", "4");

		comment.TotalDiscs = null;

		Assert.IsNull (comment.TotalDiscs);
		Assert.IsNull (comment.GetValue ("TOTALDISCS"));
		Assert.IsNull (comment.GetValue ("DISCTOTAL"));
	}

	[TestMethod]
	public void TotalDiscs_UnusableTotalDiscs_FallsBackToDiscTotal ()
	{
		var comment = new VorbisComment ("test");
		comment.SetValue ("TOTALDISCS", "abc");
		comment.SetValue ("DISCTOTAL", "4");

		Assert.AreEqual (4u, comment.TotalDiscs);
	}

	[TestMethod]
	public void TotalDiscs_EmptyTotalDiscs_FallsBackToDiscTotal ()
	{
		var comment = new VorbisComment ("test");
		comment.SetValue ("TOTALDISCS", "");
		comment.SetValue ("DISCTOTAL", "4");

		Assert.AreEqual (4u, comment.TotalDiscs);
	}

	// ===========================================
	// DiscSubtitle Tests
	// ===========================================

	[TestMethod]
	public void DiscSubtitle_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.DiscSubtitle = "The Early Years";

		Assert.AreEqual ("The Early Years", comment.DiscSubtitle);
		Assert.AreEqual ("The Early Years", comment.GetValue ("DISCSUBTITLE"));
	}

	[TestMethod]
	public void DiscSubtitle_RoundTrip_PreservesValue ()
	{
		var original = new VorbisComment ("test") { DiscSubtitle = "Disc 1: Origins" };

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Disc 1: Origins", result.Tag!.DiscSubtitle);
	}

	// ===========================================
	// Original Release Date (with fallback logic)
	// ===========================================

	[TestMethod]
	public void OriginalReleaseDate_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.OriginalReleaseDate = "1969-09-26";

		Assert.AreEqual ("1969-09-26", comment.OriginalReleaseDate);
		Assert.AreEqual ("1969-09-26", comment.GetValue ("ORIGINALDATE"));
	}

	[TestMethod]
	public void OriginalReleaseDate_FallsBackToOriginalYear ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ORIGINALYEAR", "1969");

		Assert.AreEqual ("1969", comment.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_PrefersOriginalDateOverYear ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ORIGINALDATE", "1969-09-26");
		comment.AddField ("ORIGINALYEAR", "1969");

		Assert.AreEqual ("1969-09-26", comment.OriginalReleaseDate);
	}

	static void AssertSingle (VorbisComment comment, string name, string expected)
	{
		var values = comment.GetValues (name);
		Assert.HasCount (1, values, name);
		Assert.AreEqual (expected, values[0], name);
	}

	[TestMethod]
	public void OriginalReleaseDate_Set_RewritesOriginalYear_ClearRemovesBoth ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ORIGINALYEAR", "1969");
		comment.OriginalReleaseDate = "2001-01-01";

		Assert.AreEqual ("2001-01-01", comment.OriginalReleaseDate);
		AssertSingle (comment, "ORIGINALDATE", "2001-01-01");
		AssertSingle (comment, "ORIGINALYEAR", "2001");

		// No four-digit year to copy: ORIGINALYEAR keeps its value; the getter reads ORIGINALDATE.
		comment.OriginalReleaseDate = "Summer";
		Assert.AreEqual ("Summer", comment.OriginalReleaseDate);
		AssertSingle (comment, "ORIGINALYEAR", "2001");

		comment.OriginalReleaseDate = null;
		Assert.IsNull (comment.OriginalReleaseDate);
		Assert.HasCount (0, comment.GetValues ("ORIGINALYEAR"));
		Assert.HasCount (0, comment.GetValues ("ORIGINALDATE"));
	}

	[TestMethod]
	[DataRow ("1970", "1970")]
	[DataRow ("1970-02", "1970")]
	[DataRow ("1970/02/03", "1970")]
	[DataRow ("197", "1969")]
	[DataRow ("19701", "1969")]
	[DataRow ("19x0-01-01", "1969")]
	public void OriginalReleaseDate_Set_CopiesOnlyALeadingFourDigitYear (string date, string expectedYear)
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ORIGINALYEAR", "1969");
		comment.OriginalReleaseDate = date;

		Assert.AreEqual (date, comment.OriginalReleaseDate);
		AssertSingle (comment, "ORIGINALYEAR", expectedYear);
	}

	[TestMethod]
	public void OriginalReleaseDate_SetWithoutOriginalYear_DoesNotAddIt ()
	{
		var comment = new VorbisComment ("test");
		comment.OriginalReleaseDate = "2001-01-01";

		Assert.HasCount (0, comment.GetValues ("ORIGINALYEAR"));
	}

	[TestMethod]
	public void OriginalReleaseDate_SetEmpty_RemovesOriginalYear ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ORIGINALYEAR", "1969");
		comment.OriginalReleaseDate = "";

		Assert.AreEqual ("", comment.OriginalReleaseDate);
		Assert.HasCount (0, comment.GetValues ("ORIGINALYEAR"));
	}

	[TestMethod]
	public void Movement_Set_WritesMovementName_ClearRemovesIt ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("MOVEMENTNAME", "Adagio");
		comment.Movement = "Allegro";

		Assert.AreEqual ("Allegro", comment.Movement);
		Assert.HasCount (0, comment.GetValues ("MOVEMENT"));
		AssertSingle (comment, "MOVEMENTNAME", "Allegro");

		comment.Movement = null;
		Assert.IsNull (comment.Movement);
		Assert.HasCount (0, comment.GetValues ("MOVEMENT"));
		Assert.HasCount (0, comment.GetValues ("MOVEMENTNAME"));
	}

	[TestMethod]
	public void Movement_SetOnAnEmptyTag_WritesPicardsMovementName ()
	{
		var comment = new VorbisComment ("test");
		comment.Movement = "Allegro";

		AssertSingle (comment, "MOVEMENTNAME", "Allegro");
		Assert.HasCount (0, comment.GetValues ("MOVEMENT"));
	}

	[TestMethod]
	public void Movement_LegacyNameInMovement_IsReadAndKeptInStep ()
	{
		// Earlier versions wrote the name to MOVEMENT. A MOVEMENT that is not a number still reads
		// as the name; a new name updates it, and a clear removes it.
		var comment = new VorbisComment ("test");
		comment.AddField ("MOVEMENT", "Allegro");
		Assert.AreEqual ("Allegro", comment.Movement);
		Assert.IsNull (comment.MovementNumber);

		comment.Movement = "Adagio";
		AssertSingle (comment, "MOVEMENT", "Adagio");
		AssertSingle (comment, "MOVEMENTNAME", "Adagio");

		comment.MovementNumber = 2;
		AssertSingle (comment, "MOVEMENTNUMBER", "2");
		AssertSingle (comment, "MOVEMENT", "Adagio");

		comment.Movement = null;
		Assert.HasCount (0, comment.GetValues ("MOVEMENT"));
		Assert.HasCount (0, comment.GetValues ("MOVEMENTNAME"));
		Assert.AreEqual (2u, comment.MovementNumber);
	}

	[TestMethod]
	public void Movement_SetEmpty_LeavesAnEmptyNameAndRemovesALegacyName ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("MOVEMENTNAME", "Adagio");
		comment.AddField ("MOVEMENT", "Adagio");
		comment.Movement = "";

		Assert.AreEqual ("", comment.Movement);
		AssertSingle (comment, "MOVEMENTNAME", "");
		Assert.HasCount (0, comment.GetValues ("MOVEMENT"));
	}

	[TestMethod]
	public void PicardTags_UnchangedWriteBack_KeepsEveryField ()
	{
		// Picard's mapping: MOVEMENTNAME is the name, MOVEMENT the number, ORIGINALYEAR the year
		// beside ORIGINALDATE. Writing back what the getters return must not lose any of them.
		var comment = new VorbisComment ("test");
		comment.AddField ("MOVEMENTNAME", "Allegro");
		comment.AddField ("MOVEMENT", "2");
		comment.AddField ("MOVEMENTTOTAL", "4");
		comment.AddField ("ORIGINALDATE", "1969-09-26");
		comment.AddField ("ORIGINALYEAR", "1969");

		comment.Movement = comment.Movement;
		comment.OriginalReleaseDate = comment.OriginalReleaseDate;

		var reparsed = VorbisComment.Read (comment.Render ().Span);
		Assert.IsTrue (reparsed.IsSuccess, reparsed.Error);
		var tag = reparsed.Tag!;
		AssertSingle (tag, "MOVEMENTNAME", "Allegro");
		AssertSingle (tag, "MOVEMENT", "2");
		AssertSingle (tag, "MOVEMENTTOTAL", "4");
		AssertSingle (tag, "ORIGINALDATE", "1969-09-26");
		AssertSingle (tag, "ORIGINALYEAR", "1969");
	}

	[TestMethod]
	[DataRow ("ORIGINALDATE", "ORIGINALYEAR", "1969")]
	public void EmptyPrimary_UnchangedWriteBack_KeepsBothFields (string primary, string fallback, string fallbackValue)
	{
		// An empty field is a valid comment (xiph.org/vorbis/doc/v-comment.html: NAME=value, any length),
		// but it must not hide the filled fallback, and writing back what was read must not clear it.
		var comment = new VorbisComment ("test");
		comment.AddField (primary, "");
		comment.AddField (fallback, fallbackValue);
		var property = primary == "MOVEMENT" ? nameof (VorbisComment.Movement) : nameof (VorbisComment.OriginalReleaseDate);
		var info = typeof (VorbisComment).GetProperty (property)!;
		Assert.AreEqual (fallbackValue, info.GetValue (comment));

		info.SetValue (comment, info.GetValue (comment));

		var reparsed = VorbisComment.Read (comment.Render ().Span);
		Assert.IsTrue (reparsed.IsSuccess, reparsed.Error);
		AssertSingle (reparsed.Tag!, primary, "");
		AssertSingle (reparsed.Tag!, fallback, fallbackValue);

		// An explicit empty value is not what the getter returned, so it is still a clear.
		info.SetValue (comment, "");
		AssertSingle (comment, primary, "");
		Assert.HasCount (0, comment.GetValues (fallback));
	}

	[TestMethod]
	[DataRow ("MOVEMENT", "MOVEMENTNAME")]
	[DataRow ("ORIGINALDATE", "ORIGINALYEAR")]
	public void EmptyFieldsOnly_ReadEmptyAndWriteBackKeepsThem (string primary, string fallback)
	{
		var comment = new VorbisComment ("test");
		comment.AddField (primary, "");
		comment.AddField (fallback, "");
		var property = primary == "MOVEMENT" ? nameof (VorbisComment.Movement) : nameof (VorbisComment.OriginalReleaseDate);
		var info = typeof (VorbisComment).GetProperty (property)!;
		Assert.AreEqual ("", info.GetValue (comment));

		info.SetValue (comment, info.GetValue (comment));

		AssertSingle (comment, primary, "");
		AssertSingle (comment, fallback, "");
	}

	[TestMethod]
	public void FallbackOnly_UnchangedWriteBack_KeepsFallbackField ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("MOVEMENTNAME", "Allegro");
		comment.AddField ("ORIGINALYEAR", "1969");

		comment.Movement = comment.Movement;
		comment.OriginalReleaseDate = comment.OriginalReleaseDate;

		Assert.AreEqual ("Allegro", comment.Movement);
		Assert.AreEqual ("1969", comment.OriginalReleaseDate);
		AssertSingle (comment, "MOVEMENTNAME", "Allegro");
		AssertSingle (comment, "ORIGINALYEAR", "1969");
	}

	[TestMethod]
	public void OriginalReleaseDate_SetNull_ClearsField ()
	{
		var comment = new VorbisComment ("test");
		comment.OriginalReleaseDate = "1969-09-26";

		comment.OriginalReleaseDate = null;

		Assert.IsNull (comment.OriginalReleaseDate);
		Assert.IsNull (comment.GetValue ("ORIGINALDATE"));
	}

	[TestMethod]
	public void OriginalReleaseDate_RoundTrip_PreservesValue ()
	{
		var original = new VorbisComment ("test") { OriginalReleaseDate = "1969-09-26" };

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1969-09-26", result.Tag!.OriginalReleaseDate);
	}

	// ===========================================
	// Multi-Value String Array Properties
	// ===========================================

	[TestMethod]
	[DataRow (nameof (VorbisComment.PerformersSort), "ARTISTSORT")]
	[DataRow (nameof (VorbisComment.AlbumArtistsSort), "ALBUMARTISTSORT")]
	[DataRow (nameof (VorbisComment.ComposersSort), "COMPOSERSORT")]
	public void StringArrayProperty_SingleValue_GetSet_Works (string propertyName, string fieldName)
	{
		var comment = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		var testValue = new[] { "Test Value" };

		property.SetValue (comment, testValue);

		var result = (string[]?)property.GetValue (comment);
		Assert.IsNotNull (result);
		Assert.HasCount (1, result);
		Assert.AreEqual ("Test Value", result[0]);
	}

	[TestMethod]
	[DataRow (nameof (VorbisComment.PerformersSort), "ARTISTSORT")]
	[DataRow (nameof (VorbisComment.AlbumArtistsSort), "ALBUMARTISTSORT")]
	[DataRow (nameof (VorbisComment.ComposersSort), "COMPOSERSORT")]
	public void StringArrayProperty_MultipleValues_GetSet_Works (string propertyName, string fieldName)
	{
		var comment = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		var testValues = new[] { "Value One", "Value Two" };

		property.SetValue (comment, testValues);

		var result = (string[]?)property.GetValue (comment);
		Assert.IsNotNull (result);
		Assert.HasCount (2, result);
		Assert.AreEqual ("Value One", result[0]);
		Assert.AreEqual ("Value Two", result[1]);
		Assert.HasCount (2, comment.GetValues (fieldName));
	}

	[TestMethod]
	[DataRow (nameof (VorbisComment.PerformersSort), "ARTISTSORT")]
	[DataRow (nameof (VorbisComment.AlbumArtistsSort), "ALBUMARTISTSORT")]
	[DataRow (nameof (VorbisComment.ComposersSort), "COMPOSERSORT")]
	public void StringArrayProperty_SetNull_ClearsValue (string propertyName, string fieldName)
	{
		var comment = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		property.SetValue (comment, SingleTestValue);

		property.SetValue (comment, null);

		Assert.IsNull (property.GetValue (comment));
		Assert.HasCount (0, comment.GetValues (fieldName));
	}

	[TestMethod]
	[DataRow (nameof (VorbisComment.PerformersSort))]
	[DataRow (nameof (VorbisComment.AlbumArtistsSort))]
	[DataRow (nameof (VorbisComment.ComposersSort))]
	public void StringArrayProperty_RoundTrip_PreservesMultipleValues (string propertyName)
	{
		var original = new VorbisComment ("test");
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		var testValues = new[] { "Value One", "Value Two", "Value Three" };
		property.SetValue (original, testValues);

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		var resultValues = (string[]?)property.GetValue (result.Tag);
		Assert.IsNotNull (resultValues);
		Assert.HasCount (3, resultValues);
		Assert.AreEqual ("Value One", resultValues[0]);
		Assert.AreEqual ("Value Two", resultValues[1]);
		Assert.AreEqual ("Value Three", resultValues[2]);
	}

	// ===========================================
	// MusicBrainz Recording ID (alias for TrackId)
	// ===========================================

	[TestMethod]
	public void MusicBrainzRecordingId_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.MusicBrainzRecordingId = "c6b36210-7812-4b57-a48a-8bf78e0d7f82";

		Assert.AreEqual ("c6b36210-7812-4b57-a48a-8bf78e0d7f82", comment.MusicBrainzRecordingId);
		Assert.AreEqual ("c6b36210-7812-4b57-a48a-8bf78e0d7f82", comment.GetValue ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void MusicBrainzRecordingId_IsAliasForTrackId ()
	{
		var comment = new VorbisComment ("test");

		comment.MusicBrainzRecordingId = "c6b36210-7812-4b57-a48a-8bf78e0d7f82";

		Assert.AreEqual ("c6b36210-7812-4b57-a48a-8bf78e0d7f82", comment.MusicBrainzTrackId);
		Assert.AreEqual (comment.MusicBrainzRecordingId, comment.MusicBrainzTrackId);
	}

	[TestMethod]
	public void MusicBrainzRecordingId_SetNull_ClearsField ()
	{
		var comment = new VorbisComment ("test");
		comment.MusicBrainzRecordingId = "c6b36210-7812-4b57-a48a-8bf78e0d7f82";

		comment.MusicBrainzRecordingId = null;

		Assert.IsNull (comment.MusicBrainzRecordingId);
		Assert.IsNull (comment.MusicBrainzTrackId);
		Assert.IsNull (comment.GetValue ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void MusicBrainzRecordingId_RoundTrip_PreservesValue ()
	{
		var original = new VorbisComment ("test") {
			MusicBrainzRecordingId = "deadbeef-1234-5678-90ab-cdef12345678"
		};

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("deadbeef-1234-5678-90ab-cdef12345678", result.Tag!.MusicBrainzRecordingId);
		Assert.AreEqual ("deadbeef-1234-5678-90ab-cdef12345678", result.Tag.MusicBrainzTrackId);
	}

	// ===========================================
	// PerformersRole Tests
	// ===========================================

	[TestMethod]
	public void PerformersRole_GetSet_Works ()
	{
		var comment = new VorbisComment ("test");

		comment.PerformersRole = ["lead vocals", "guitar"];

		Assert.IsNotNull (comment.PerformersRole);
		Assert.HasCount (2, comment.PerformersRole);
		Assert.AreEqual ("lead vocals", comment.PerformersRole[0]);
		Assert.AreEqual ("guitar", comment.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_SetNull_ClearsField ()
	{
		var comment = new VorbisComment ("test");
		comment.PerformersRole = ["lead vocals"];

		comment.PerformersRole = null;

		Assert.IsTrue (comment.PerformersRole is null || comment.PerformersRole.Length == 0);
	}

	[TestMethod]
	public void PerformersRole_SetEmpty_ClearsField ()
	{
		var comment = new VorbisComment ("test");
		comment.PerformersRole = ["lead vocals"];

		comment.PerformersRole = [];

		Assert.IsTrue (comment.PerformersRole is null || comment.PerformersRole.Length == 0);
	}

	[TestMethod]
	public void PerformersRole_RoundTrip_PreservesValue ()
	{
		var original = new VorbisComment ("test");
		original.PerformersRole = ["lead vocals", "rhythm guitar", "bass"];

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (3, result.Tag.PerformersRole);
		Assert.AreEqual ("lead vocals", result.Tag.PerformersRole[0]);
		Assert.AreEqual ("rhythm guitar", result.Tag.PerformersRole[1]);
		Assert.AreEqual ("bass", result.Tag.PerformersRole[2]);
	}

	[TestMethod]
	public void PerformersRole_UnicodeCharacters_PreservesText ()
	{
		var comment = new VorbisComment ("test");
		comment.PerformersRole = ["ボーカル", "ギター"]; // Japanese: "vocals", "guitar"

		var rendered = comment.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (2, result.Tag.PerformersRole);
		Assert.AreEqual ("ボーカル", result.Tag.PerformersRole[0]);
		Assert.AreEqual ("ギター", result.Tag.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_SingleValue_Works ()
	{
		var comment = new VorbisComment ("test");
		comment.PerformersRole = ["all instruments"];

		var rendered = comment.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (1, result.Tag.PerformersRole);
		Assert.AreEqual ("all instruments", result.Tag.PerformersRole[0]);
	}

	// ===========================================
	// Lyrics Unicode Test
	// ===========================================

	[TestMethod]
	public void Lyrics_WithUnicode_PreservesValue ()
	{
		var original = new VorbisComment ("test") {
			Lyrics = "日本語の歌詞\n中文歌词\nКириллица"
		};

		var rendered = original.Render ();
		var result = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("日本語の歌詞\n中文歌词\nКириллица", result.Tag!.Lyrics);
	}
}
