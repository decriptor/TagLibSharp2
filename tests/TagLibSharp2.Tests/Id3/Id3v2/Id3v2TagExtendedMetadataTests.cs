// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;

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
	// Conductor (TPE3) Tests

	[TestMethod]
	public void Conductor_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Conductor = "Herbert von Karajan";

		Assert.AreEqual ("Herbert von Karajan", tag.Conductor);
		Assert.AreEqual ("Herbert von Karajan", tag.GetTextFrame ("TPE3"));
	}

	[TestMethod]
	public void Conductor_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Conductor = "Herbert von Karajan";

		tag.Conductor = null;

		Assert.IsNull (tag.Conductor);
		Assert.IsNull (tag.GetTextFrame ("TPE3"));
	}

	[TestMethod]
	public void Conductor_FromFile_ParsesCorrectly ()
	{
		var data = CreateTagWithTextFrame ("TPE3", "Leonard Bernstein", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Leonard Bernstein", result.Tag!.Conductor);
	}

	[TestMethod]
	public void Conductor_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Conductor = "Sir Simon Rattle" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Sir Simon Rattle", result.Tag!.Conductor);
	}

	// Copyright (TCOP) Tests

	[TestMethod]
	public void Copyright_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Copyright = "2024 Acme Records";

		Assert.AreEqual ("2024 Acme Records", tag.Copyright);
		Assert.AreEqual ("2024 Acme Records", tag.GetTextFrame ("TCOP"));
	}

	[TestMethod]
	public void Copyright_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Copyright = "2024 Acme Records";

		tag.Copyright = null;

		Assert.IsNull (tag.Copyright);
		Assert.IsNull (tag.GetTextFrame ("TCOP"));
	}

	[TestMethod]
	public void Copyright_FromFile_ParsesCorrectly ()
	{
		var data = CreateTagWithTextFrame ("TCOP", "2023 Universal Music", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("2023 Universal Music", result.Tag!.Copyright);
	}

	[TestMethod]
	public void Copyright_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Copyright = "2025 Independent" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("2025 Independent", result.Tag!.Copyright);
	}

	// Compilation (TCMP) Tests

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
	public void IsCompilation_FromFile_ParsesOne ()
	{
		var data = CreateTagWithTextFrame ("TCMP", "1", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Tag!.IsCompilation);
	}

	[TestMethod]
	public void IsCompilation_FromFile_ParsesZero ()
	{
		var data = CreateTagWithTextFrame ("TCMP", "0", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.Tag!.IsCompilation);
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

	// ISRC (TSRC) Tests

	[TestMethod]
	public void Isrc_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Isrc = "USRC17607839";

		Assert.AreEqual ("USRC17607839", tag.Isrc);
		Assert.AreEqual ("USRC17607839", tag.GetTextFrame ("TSRC"));
	}

	[TestMethod]
	public void Isrc_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Isrc = "USRC17607839";

		tag.Isrc = null;

		Assert.IsNull (tag.Isrc);
		Assert.IsNull (tag.GetTextFrame ("TSRC"));
	}

	[TestMethod]
	public void Isrc_FromFile_ParsesCorrectly ()
	{
		var data = CreateTagWithTextFrame ("TSRC", "GBAYE0000351", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("GBAYE0000351", result.Tag!.Isrc);
	}

	[TestMethod]
	public void Isrc_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Isrc = "BRBMG0300729" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("BRBMG0300729", result.Tag!.Isrc);
	}

	// Publisher (TPUB) Tests

	[TestMethod]
	public void Publisher_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Publisher = "Atlantic Records";

		Assert.AreEqual ("Atlantic Records", tag.Publisher);
		Assert.AreEqual ("Atlantic Records", tag.GetTextFrame ("TPUB"));
	}

	[TestMethod]
	public void Publisher_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Publisher = "Atlantic Records";

		tag.Publisher = null;

		Assert.IsNull (tag.Publisher);
		Assert.IsNull (tag.GetTextFrame ("TPUB"));
	}

	[TestMethod]
	public void Publisher_FromFile_ParsesCorrectly ()
	{
		var data = CreateTagWithTextFrame ("TPUB", "Sub Pop Records", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Sub Pop Records", result.Tag!.Publisher);
	}

	[TestMethod]
	public void Publisher_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Publisher = "4AD Records" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("4AD Records", result.Tag!.Publisher);
	}

	// Sort Tags Tests

	[TestMethod]
	public void AlbumSort_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AlbumSort = "White Album, The";

		Assert.AreEqual ("White Album, The", tag.AlbumSort);
		Assert.AreEqual ("White Album, The", tag.GetTextFrame ("TSOA"));
	}

	[TestMethod]
	public void AlbumSort_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { AlbumSort = "Abbey Road" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Abbey Road", result.Tag!.AlbumSort);
	}

	[TestMethod]
	public void ArtistSort_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.ArtistSort = "Beatles, The";

		Assert.AreEqual ("Beatles, The", tag.ArtistSort);
		Assert.AreEqual ("Beatles, The", tag.GetTextFrame ("TSOP"));
	}

	[TestMethod]
	public void ArtistSort_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { ArtistSort = "Radiohead" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Radiohead", result.Tag!.ArtistSort);
	}

	[TestMethod]
	public void TitleSort_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.TitleSort = "Love Is All You Need";

		Assert.AreEqual ("Love Is All You Need", tag.TitleSort);
		Assert.AreEqual ("Love Is All You Need", tag.GetTextFrame ("TSOT"));
	}

	[TestMethod]
	public void TitleSort_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { TitleSort = "Yesterday" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Yesterday", result.Tag!.TitleSort);
	}

	[TestMethod]
	public void AlbumArtistSort_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AlbumArtistSort = "Various";

		Assert.AreEqual ("Various", tag.AlbumArtistSort);
		Assert.AreEqual ("Various", tag.GetTextFrame ("TSO2"));
	}

	[TestMethod]
	public void AlbumArtistSort_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { AlbumArtistSort = "Compilation Artists" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Compilation Artists", result.Tag!.AlbumArtistSort);
	}

	// TotalTracks Tests

	[TestMethod]
	public void TotalTracks_GetFromSlashFormat_Works ()
	{
		var data = CreateTagWithTextFrame ("TRCK", "5/12", version: 4);

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

		// When setting total without track, track defaults to 1
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

	// TotalDiscs Tests

	[TestMethod]
	public void TotalDiscs_GetFromSlashFormat_Works ()
	{
		var data = CreateTagWithTextFrame ("TPOS", "2/3", version: 4);

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

	// Original Release Date (TDOR/TORY) Tests

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
		// Create a V24 tag but manually set TORY (e.g., from a converted file)
		var data = CreateTagWithTextFrame ("TORY", "1969", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1969", result.Tag!.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_V24_PrefersTDOROverTORY ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		// Set OriginalReleaseDate (writes TDOR), then add TORY as well
		tag.OriginalReleaseDate = "1969-09-26";
		var toryValues = new List<string> { "1969" };
		tag.SetTextFrameValues ("TORY", toryValues);

		// Should prefer TDOR for v2.4
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
	public void OriginalReleaseDate_RoundTrip_V24 ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { OriginalReleaseDate = "1969-09-26" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1969-09-26", result.Tag!.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_RoundTrip_V23 ()
	{
		var original = new Id3v2Tag (Id3v2Version.V23) { OriginalReleaseDate = "1969" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1969", result.Tag!.OriginalReleaseDate);
	}

	// Multi-Value Tests

	[TestMethod]
	public void Artists_SingleValue_ReturnsOneElement ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "The Beatles" };

		var artists = tag.Artists;

		Assert.HasCount (1, artists);
		Assert.AreEqual ("The Beatles", artists[0]);
	}

	[TestMethod]
	public void Artists_NullSeparated_ReturnsMultipleElements ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var artistValues = new List<string> { "John Lennon", "Paul McCartney" };
		tag.SetTextFrameValues ("TPE1", artistValues);

		var artists = tag.Artists;

		Assert.HasCount (2, artists);
		Assert.AreEqual ("John Lennon", artists[0]);
		Assert.AreEqual ("Paul McCartney", artists[1]);
	}

	[TestMethod]
	public void Artists_SlashSeparated_ReturnsMultipleElements ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "Daft Punk / The Weeknd" };

		var artists = tag.Artists;

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
		Assert.IsEmpty (tag.Artists);
	}

	[TestMethod]
	public void SetTextFrameValues_Empty_RemovesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Artist = "Test Artist" };

		tag.SetTextFrameValues ("TPE1", Array.Empty<string> ());

		Assert.IsNull (tag.Artist);
	}

	[TestMethod]
	public void Genres_MultiValue_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var genreValues = new List<string> { "Rock", "Pop", "Electronic" };
		tag.SetTextFrameValues ("TCON", genreValues);

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
		var composerValues = new List<string> { "Lennon", "McCartney" };
		tag.SetTextFrameValues ("TCOM", composerValues);

		var composers = tag.Composers;

		Assert.HasCount (2, composers);
		Assert.AreEqual ("Lennon", composers[0]);
		Assert.AreEqual ("McCartney", composers[1]);
	}

	[TestMethod]
	public void AlbumArtists_MultiValue_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var albumArtistValues = new List<string> { "Various Artists", "Compilation" };
		tag.SetTextFrameValues ("TPE2", albumArtistValues);

		var albumArtists = tag.AlbumArtists;

		Assert.HasCount (2, albumArtists);
		Assert.AreEqual ("Various Artists", albumArtists[0]);
		Assert.AreEqual ("Compilation", albumArtists[1]);
	}

	[TestMethod]
	public void MultiValue_SlashSeparated_RoundTrip_PreservesValues ()
	{
		// Use slash-separated values for round-trip as it's more widely compatible
		var original = new Id3v2Tag (Id3v2Version.V24) { Artist = "Artist One / Artist Two / Artist Three" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		var artists = result.Tag!.Artists;
		Assert.HasCount (3, artists);
		Assert.AreEqual ("Artist One", artists[0]);
		Assert.AreEqual ("Artist Two", artists[1]);
		Assert.AreEqual ("Artist Three", artists[2]);
	}

	// EncodedBy (TENC) Tests

	[TestMethod]
	public void EncodedBy_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.EncodedBy = "LAME 3.100";

		Assert.AreEqual ("LAME 3.100", tag.EncodedBy);
		Assert.AreEqual ("LAME 3.100", tag.GetTextFrame ("TENC"));
	}

	[TestMethod]
	public void EncodedBy_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { EncodedBy = "iTunes 12.0" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("iTunes 12.0", result.Tag!.EncodedBy);
	}

	// EncoderSettings (TSSE) Tests

	[TestMethod]
	public void EncoderSettings_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.EncoderSettings = "LAME 320kbps CBR";

		Assert.AreEqual ("LAME 320kbps CBR", tag.EncoderSettings);
		Assert.AreEqual ("LAME 320kbps CBR", tag.GetTextFrame ("TSSE"));
	}

	[TestMethod]
	public void EncoderSettings_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { EncoderSettings = "VBR V0" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("VBR V0", result.Tag!.EncoderSettings);
	}

	// Grouping (TIT1) Tests

	[TestMethod]
	public void Grouping_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Grouping = "Summer Hits 2024";

		Assert.AreEqual ("Summer Hits 2024", tag.Grouping);
		Assert.AreEqual ("Summer Hits 2024", tag.GetTextFrame ("TIT1"));
	}

	[TestMethod]
	public void Grouping_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Grouping = "Workout Mix" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Workout Mix", result.Tag!.Grouping);
	}

	// Subtitle (TIT3) Tests

	[TestMethod]
	public void Subtitle_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Subtitle = "Radio Edit";

		Assert.AreEqual ("Radio Edit", tag.Subtitle);
		Assert.AreEqual ("Radio Edit", tag.GetTextFrame ("TIT3"));
	}

	[TestMethod]
	public void Subtitle_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Subtitle = "Extended Mix" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Extended Mix", result.Tag!.Subtitle);
	}

	// Remixer (TPE4) Tests

	[TestMethod]
	public void Remixer_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Remixer = "Tiësto";

		Assert.AreEqual ("Tiësto", tag.Remixer);
		Assert.AreEqual ("Tiësto", tag.GetTextFrame ("TPE4"));
	}

	[TestMethod]
	public void Remixer_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Remixer = "David Guetta" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("David Guetta", result.Tag!.Remixer);
	}

	// InitialKey (TKEY) Tests

	[TestMethod]
	public void InitialKey_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.InitialKey = "Am";

		Assert.AreEqual ("Am", tag.InitialKey);
		Assert.AreEqual ("Am", tag.GetTextFrame ("TKEY"));
	}

	[TestMethod]
	public void InitialKey_SharpKey_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.InitialKey = "F#m";

		Assert.AreEqual ("F#m", tag.InitialKey);
	}

	[TestMethod]
	public void InitialKey_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { InitialKey = "Ebm" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Ebm", result.Tag!.InitialKey);
	}

	// Mood (TMOO) Tests - ID3v2.4 only

	[TestMethod]
	public void Mood_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Mood = "Energetic";

		Assert.AreEqual ("Energetic", tag.Mood);
		Assert.AreEqual ("Energetic", tag.GetTextFrame ("TMOO"));
	}

	[TestMethod]
	public void Mood_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Mood = "Melancholic" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Melancholic", result.Tag!.Mood);
	}

	// MediaType (TMED) Tests

	[TestMethod]
	public void MediaType_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MediaType = "CD";

		Assert.AreEqual ("CD", tag.MediaType);
		Assert.AreEqual ("CD", tag.GetTextFrame ("TMED"));
	}

	[TestMethod]
	public void MediaType_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MediaType = "DIG/A" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("DIG/A", result.Tag!.MediaType);
	}

	// Language (TLAN) Tests

	[TestMethod]
	public void Language_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Language = "eng";

		Assert.AreEqual ("eng", tag.Language);
		Assert.AreEqual ("eng", tag.GetTextFrame ("TLAN"));
	}

	[TestMethod]
	public void Language_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Language = "jpn" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("jpn", result.Tag!.Language);
	}

	// Barcode (TXXX:BARCODE) Tests

	[TestMethod]
	public void Barcode_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Barcode = "012345678901";

		Assert.AreEqual ("012345678901", tag.Barcode);
		Assert.AreEqual ("012345678901", tag.GetUserText ("BARCODE"));
	}

	[TestMethod]
	public void Barcode_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Barcode = "5099749534728" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("5099749534728", result.Tag!.Barcode);
	}

	// CatalogNumber (TXXX:CATALOGNUMBER) Tests

	[TestMethod]
	public void CatalogNumber_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.CatalogNumber = "WPCR-80001";

		Assert.AreEqual ("WPCR-80001", tag.CatalogNumber);
		Assert.AreEqual ("WPCR-80001", tag.GetUserText ("CATALOGNUMBER"));
	}

	[TestMethod]
	public void CatalogNumber_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { CatalogNumber = "ECM 1064/65" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("ECM 1064/65", result.Tag!.CatalogNumber);
	}

	// ComposerSort (TSOC) Tests

	[TestMethod]
	public void ComposerSort_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.ComposerSort = "Bach, Johann Sebastian";

		Assert.AreEqual ("Bach, Johann Sebastian", tag.ComposerSort);
		Assert.AreEqual ("Bach, Johann Sebastian", tag.GetTextFrame ("TSOC"));
	}

	[TestMethod]
	public void ComposerSort_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { ComposerSort = "Mozart, Wolfgang Amadeus" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Mozart, Wolfgang Amadeus", result.Tag!.ComposerSort);
	}

	// DateTagged (TDTG) Tests - ID3v2.4 only

	[TestMethod]
	public void DateTagged_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.DateTagged = "2025-12-27T10:30:00";

		Assert.AreEqual ("2025-12-27T10:30:00", tag.DateTagged);
		Assert.AreEqual ("2025-12-27T10:30:00", tag.GetTextFrame ("TDTG"));
	}

	[TestMethod]
	public void DateTagged_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { DateTagged = "2025-12-27" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("2025-12-27", result.Tag!.DateTagged);
	}

	// Description (TXXX:DESCRIPTION) Tests

	[TestMethod]
	public void Description_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Description = "A story about love and loss";

		Assert.AreEqual ("A story about love and loss", tag.Description);
		Assert.AreEqual ("A story about love and loss", tag.GetUserText ("DESCRIPTION"));
	}

	[TestMethod]
	public void Description_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { Description = "Epic adventure through space" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Epic adventure through space", result.Tag!.Description);
	}

	// AmazonId (TXXX:ASIN) Tests

	[TestMethod]
	public void AmazonId_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AmazonId = "B000002UAL";

		Assert.AreEqual ("B000002UAL", tag.AmazonId);
		Assert.AreEqual ("B000002UAL", tag.GetUserText ("ASIN"));
	}

	[TestMethod]
	public void AmazonId_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { AmazonId = "B00005NQ6Z" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("B00005NQ6Z", result.Tag!.AmazonId);
	}

	// MusicIpId (TXXX:MusicIP PUID) Tests - Obsolete

#pragma warning disable CS0618 // Type or member is obsolete
	[TestMethod]
	public void MusicIpId_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicIpId = "f3b89eb0-c53d-4a0c-b4e8-1a0b9c3e2d4f";

		Assert.AreEqual ("f3b89eb0-c53d-4a0c-b4e8-1a0b9c3e2d4f", tag.MusicIpId);
		Assert.AreEqual ("f3b89eb0-c53d-4a0c-b4e8-1a0b9c3e2d4f", tag.GetUserText ("MusicIP PUID"));
	}

	[TestMethod]
	public void MusicIpId_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MusicIpId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", result.Tag!.MusicIpId);
	}
#pragma warning restore CS0618

	// MusicBrainzWorkId (TXXX:MusicBrainz Work Id) Tests

	[TestMethod]
	public void MusicBrainzWorkId_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicBrainzWorkId = "1a2b3c4d-5e6f-7890-abcd-ef1234567890";

		Assert.AreEqual ("1a2b3c4d-5e6f-7890-abcd-ef1234567890", tag.MusicBrainzWorkId);
		Assert.AreEqual ("1a2b3c4d-5e6f-7890-abcd-ef1234567890", tag.GetUserText ("MusicBrainz Work Id"));
	}

	[TestMethod]
	public void MusicBrainzWorkId_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MusicBrainzWorkId = "deadbeef-1234-5678-90ab-cdef12345678" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("deadbeef-1234-5678-90ab-cdef12345678", result.Tag!.MusicBrainzWorkId);
	}

	// MusicBrainzDiscId (TXXX:MusicBrainz Disc Id) Tests

	[TestMethod]
	public void MusicBrainzDiscId_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicBrainzDiscId = "XHLQnC.F3SJ5XpDPLt7gLfHAy_A-";

		Assert.AreEqual ("XHLQnC.F3SJ5XpDPLt7gLfHAy_A-", tag.MusicBrainzDiscId);
		Assert.AreEqual ("XHLQnC.F3SJ5XpDPLt7gLfHAy_A-", tag.GetUserText ("MusicBrainz Disc Id"));
	}

	[TestMethod]
	public void MusicBrainzDiscId_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MusicBrainzDiscId = "IbhKz8W2xPbLqA1F5nPKz8xLUBc-" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("IbhKz8W2xPbLqA1F5nPKz8xLUBc-", result.Tag!.MusicBrainzDiscId);
	}

	// MusicBrainzReleaseStatus (TXXX:MusicBrainz Album Status) Tests

	[TestMethod]
	public void MusicBrainzReleaseStatus_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicBrainzReleaseStatus = "official";

		Assert.AreEqual ("official", tag.MusicBrainzReleaseStatus);
		Assert.AreEqual ("official", tag.GetUserText ("MusicBrainz Album Status"));
	}

	[TestMethod]
	public void MusicBrainzReleaseStatus_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MusicBrainzReleaseStatus = "promotional" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("promotional", result.Tag!.MusicBrainzReleaseStatus);
	}

	// MusicBrainzReleaseType (TXXX:MusicBrainz Album Type) Tests

	[TestMethod]
	public void MusicBrainzReleaseType_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicBrainzReleaseType = "album";

		Assert.AreEqual ("album", tag.MusicBrainzReleaseType);
		Assert.AreEqual ("album", tag.GetUserText ("MusicBrainz Album Type"));
	}

	[TestMethod]
	public void MusicBrainzReleaseType_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MusicBrainzReleaseType = "compilation" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("compilation", result.Tag!.MusicBrainzReleaseType);
	}

	// MusicBrainzReleaseCountry (TXXX:MusicBrainz Album Release Country) Tests

	[TestMethod]
	public void MusicBrainzReleaseCountry_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.MusicBrainzReleaseCountry = "US";

		Assert.AreEqual ("US", tag.MusicBrainzReleaseCountry);
		Assert.AreEqual ("US", tag.GetUserText ("MusicBrainz Album Release Country"));
	}

	[TestMethod]
	public void MusicBrainzReleaseCountry_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) { MusicBrainzReleaseCountry = "GB" };

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("GB", result.Tag!.MusicBrainzReleaseCountry);
	}

	// PerformersRole (TMCL frame) Tests

	[TestMethod]
	public void PerformersRole_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.PerformersRole = new[] { "lead vocals", "guitar" };

		Assert.IsNotNull (tag.PerformersRole);
		Assert.HasCount (2, tag.PerformersRole);
		Assert.AreEqual ("lead vocals", tag.PerformersRole[0]);
		Assert.AreEqual ("guitar", tag.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = new[] { "lead vocals" };

		tag.PerformersRole = null;

		Assert.IsTrue (tag.PerformersRole is null || tag.PerformersRole.Length == 0);
	}

	[TestMethod]
	public void PerformersRole_SetEmpty_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.PerformersRole = new[] { "lead vocals" };

		tag.PerformersRole = Array.Empty<string> ();

		Assert.IsTrue (tag.PerformersRole is null || tag.PerformersRole.Length == 0);
	}

	[TestMethod]
	public void PerformersRole_RoundTrip_PreservesValue ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24);
		original.PerformersRole = new[] { "lead vocals", "rhythm guitar", "bass" };

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

		// Set artists and their roles in parallel
		var artists = new[] { "John Smith", "Jane Doe" };
		tag.SetTextFrameValues ("TPE1", artists);
		tag.PerformersRole = new[] { "vocals", "guitar" };

		// Both arrays should have matching lengths
		Assert.HasCount (2, tag.Artists);
		Assert.HasCount (2, tag.PerformersRole!);
		Assert.AreEqual ("John Smith", tag.Artists[0]);
		Assert.AreEqual ("vocals", tag.PerformersRole[0]);
		Assert.AreEqual ("Jane Doe", tag.Artists[1]);
		Assert.AreEqual ("guitar", tag.PerformersRole[1]);
	}

	[TestMethod]
	public void PerformersRole_UnicodeCharacters_PreservesText ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.PerformersRole = new[] { "ボーカル", "ギター" }; // Japanese: "vocals", "guitar"

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

		tag.PerformersRole = new[] { "all instruments" };

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.PerformersRole);
		Assert.HasCount (1, result.Tag.PerformersRole);
		Assert.AreEqual ("all instruments", result.Tag.PerformersRole[0]);
	}

	// Helper Methods

	static byte[] CreateTagWithTextFrame (string frameId, string text, byte version)
	{
		// Frame content: encoding (1) + text
		var textBytes = System.Text.Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = 0; // Latin-1 encoding
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Frame header (10 bytes) + content
		var frameSize = frameContent.Length;
		var frame = new byte[10 + frameSize];

		// Frame ID
		var idBytes = System.Text.Encoding.ASCII.GetBytes (frameId);
		Array.Copy (idBytes, 0, frame, 0, 4);

		// Size (big-endian for v2.3, syncsafe for v2.4)
		if (version == 4) {
			// Syncsafe
			frame[4] = (byte)((frameSize >> 21) & 0x7F);
			frame[5] = (byte)((frameSize >> 14) & 0x7F);
			frame[6] = (byte)((frameSize >> 7) & 0x7F);
			frame[7] = (byte)(frameSize & 0x7F);
		} else {
			// Big-endian
			frame[4] = (byte)((frameSize >> 24) & 0xFF);
			frame[5] = (byte)((frameSize >> 16) & 0xFF);
			frame[6] = (byte)((frameSize >> 8) & 0xFF);
			frame[7] = (byte)(frameSize & 0xFF);
		}

		// Flags (2 bytes, zeroes)
		frame[8] = 0;
		frame[9] = 0;

		// Content
		Array.Copy (frameContent, 0, frame, 10, frameSize);

		// Combine header + frame
		var totalSize = (uint)frame.Length;
		var header = CreateMinimalTag (version, totalSize);

		var result = new byte[header.Length + frame.Length];
		Array.Copy (header, result, header.Length);
		Array.Copy (frame, 0, result, header.Length, frame.Length);

		return result;
	}

	static byte[] CreateMinimalTag (byte version, uint size)
	{
		var data = new byte[10];
		data[0] = (byte)'I';
		data[1] = (byte)'D';
		data[2] = (byte)'3';
		data[3] = version;
		data[4] = 0; // revision
		data[5] = 0; // flags

		// Syncsafe size
		data[6] = (byte)((size >> 21) & 0x7F);
		data[7] = (byte)((size >> 14) & 0x7F);
		data[8] = (byte)((size >> 7) & 0x7F);
		data[9] = (byte)(size & 0x7F);

		return data;
	}
}
