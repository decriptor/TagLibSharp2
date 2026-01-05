// Copyright (c) 2025 Stephen Shaw and contributors
// Extended APE Tag tests for additional coverage

using TagLibSharp2.Ape;

namespace TagLibSharp2.Tests.Ape;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ape")]
public class ApeTagExtendedTests
{
	[TestMethod]
	public void Remove_ExistingKey_ReturnsTrue ()
	{
		// Arrange
		var tag = new ApeTag { Title = "Test" };

		// Act
		var result = tag.Remove ("Title");

		// Assert
		Assert.IsTrue (result);
		Assert.IsNull (tag.Title);
	}

	[TestMethod]
	public void Remove_NonExistentKey_ReturnsFalse ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		var result = tag.Remove ("NonExistent");

		// Assert
		Assert.IsFalse (result);
	}

	[TestMethod]
	public void Subtitle_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { };
		tag.SetValue ("Subtitle", "Live Version");

		Assert.AreEqual ("Live Version", tag.GetValue ("Subtitle"));
	}

	[TestMethod]
	public void Publisher_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { };
		tag.SetValue ("Publisher", "Acme Records");

		Assert.AreEqual ("Acme Records", tag.GetValue ("Publisher"));
	}

	[TestMethod]
	public void Isrc_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { };
		tag.SetValue ("ISRC", "USRC17607839");

		Assert.AreEqual ("USRC17607839", tag.GetValue ("ISRC"));
	}

	[TestMethod]
	public void Barcode_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { };
		tag.SetValue ("Barcode", "123456789012");

		Assert.AreEqual ("123456789012", tag.GetValue ("Barcode"));
	}

	[TestMethod]
	public void CatalogNumber_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { };
		tag.SetValue ("CatalogNumber", "CAT-001");

		Assert.AreEqual ("CAT-001", tag.GetValue ("CatalogNumber"));
	}

	[TestMethod]
	public void Language_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { };
		tag.SetValue ("Language", "eng");

		Assert.AreEqual ("eng", tag.GetValue ("Language"));
	}

	[TestMethod]
	public void Track_SetWithoutTotal_StoresJustNumber ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		tag.Track = 7;

		// Assert
		Assert.AreEqual (7u, tag.Track);
		Assert.IsNull (tag.TotalTracks);
		Assert.AreEqual ("7", tag.GetValue ("Track"));
	}

	[TestMethod]
	public void Track_SetWithTotal_StoresSlashFormat ()
	{
		// Arrange - set track first, then total
		var tag = new ApeTag { Track = 5 };

		// Act
		tag.TotalTracks = 12;

		// Assert
		Assert.AreEqual (5u, tag.Track);
		Assert.AreEqual (12u, tag.TotalTracks);
		Assert.AreEqual ("5/12", tag.GetValue ("Track"));
	}

	[TestMethod]
	public void Track_SetNull_RemovesField ()
	{
		// Arrange
		var tag = new ApeTag { Track = 5 };

		// Act
		tag.Track = null;

		// Assert
		Assert.IsNull (tag.Track);
		Assert.IsNull (tag.GetValue ("Track"));
	}

	[TestMethod]
	public void TotalTracks_SetWithTrack_UpdatesSlashFormat ()
	{
		// Arrange
		var tag = new ApeTag { Track = 3 };

		// Act
		tag.TotalTracks = 15;

		// Assert
		Assert.AreEqual ("3/15", tag.GetValue ("Track"));
	}

	[TestMethod]
	public void TotalTracks_SetWithoutTrack_DoesNotUpdate ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		tag.TotalTracks = 10;

		// Assert - no track number, so no update
		Assert.IsNull (tag.GetValue ("Track"));
	}

	[TestMethod]
	public void Disc_SetWithoutTotal_StoresJustNumber ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		tag.Disc = 2;

		// Assert
		Assert.AreEqual (2u, tag.Disc);
		Assert.IsNull (tag.TotalDiscs);
		Assert.AreEqual ("2", tag.GetValue ("Disc"));
	}

	[TestMethod]
	public void Disc_SetWithTotal_StoresSlashFormat ()
	{
		// Arrange - set disc first, then total
		var tag = new ApeTag { Disc = 1 };

		// Act
		tag.TotalDiscs = 3;

		// Assert
		Assert.AreEqual ("1/3", tag.GetValue ("Disc"));
	}

	[TestMethod]
	public void Disc_SetNull_RemovesField ()
	{
		// Arrange
		var tag = new ApeTag { Disc = 2 };

		// Act
		tag.Disc = null;

		// Assert
		Assert.IsNull (tag.Disc);
		Assert.IsNull (tag.GetValue ("Disc"));
	}

	[TestMethod]
	public void TotalDiscs_SetWithDisc_UpdatesSlashFormat ()
	{
		// Arrange
		var tag = new ApeTag { Disc = 1 };

		// Act
		tag.TotalDiscs = 4;

		// Assert
		Assert.AreEqual ("1/4", tag.GetValue ("Disc"));
	}

	[TestMethod]
	public void TotalDiscs_SetWithoutDisc_DoesNotUpdate ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		tag.TotalDiscs = 3;

		// Assert
		Assert.IsNull (tag.GetValue ("Disc"));
	}

	[TestMethod]
	public void DiscNumber_MatchesDisc ()
	{
		// Arrange
		var tag = new ApeTag { Disc = 2 };

		// Assert - DiscNumber is alias for Disc
		Assert.AreEqual (2u, tag.DiscNumber);

		// Act
		tag.DiscNumber = 3;

		// Assert
		Assert.AreEqual (3u, tag.Disc);
	}

	[TestMethod]
	public void GetBinaryItem_NonExistent_ReturnsNull ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		var result = tag.GetBinaryItem ("Cover Art (Front)");

		// Assert
		Assert.IsNull (result);
	}

	[TestMethod]
	public void GetBinaryItem_ExistingItem_ReturnsData ()
	{
		// Arrange
		var tag = new ApeTag ();
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
		tag.SetBinaryItem ("Cover Art (Front)", "cover.png", imageData);

		// Act
		var result = tag.GetBinaryItem ("Cover Art (Front)");

		// Assert
		Assert.IsNotNull (result);
		CollectionAssert.AreEqual (imageData, result.Data);
		Assert.AreEqual ("cover.png", result.Filename);
	}

	[TestMethod]
	public void Year_WithDate_FallsBackToDate ()
	{
		// Arrange - set Date instead of Year
		var tag = new ApeTag ();
		tag.SetValue ("Date", "2020-05-15");

		// Act - Year should fall back to Date
		var year = tag.Year;

		// Assert
		Assert.AreEqual ("2020-05-15", year);
	}

	[TestMethod]
	public void Year_PreferYearOverDate ()
	{
		// Arrange - both Year and Date set
		var tag = new ApeTag ();
		tag.SetValue ("Year", "2024");
		tag.SetValue ("Date", "2020-05-15");

		// Act
		var year = tag.Year;

		// Assert - Year takes precedence
		Assert.AreEqual ("2024", year);
	}

	[TestMethod]
	public void SetValue_OverwritesExisting ()
	{
		// Arrange
		var tag = new ApeTag { Title = "Original" };

		// Act
		tag.SetValue ("Title", "Updated");

		// Assert
		Assert.AreEqual ("Updated", tag.Title);
	}

	[TestMethod]
	public void Clear_RemovesAllItems ()
	{
		// Arrange
		var tag = new ApeTag {
			Title = "Test",
			Artist = "Artist",
			Album = "Album"
		};

		// Act
		tag.Clear ();

		// Assert
		Assert.AreEqual (0, tag.ItemCount);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Artist);
	}

	[TestMethod]
	public void ReplayGain_Properties_GetSet ()
	{
		// Arrange
		var tag = new ApeTag {
			ReplayGainTrackGain = "-6.5 dB",
			ReplayGainTrackPeak = "0.988",
			ReplayGainAlbumGain = "-5.2 dB",
			ReplayGainAlbumPeak = "0.995"
		};

		// Assert
		Assert.AreEqual ("-6.5 dB", tag.ReplayGainTrackGain);
		Assert.AreEqual ("0.988", tag.ReplayGainTrackPeak);
		Assert.AreEqual ("-5.2 dB", tag.ReplayGainAlbumGain);
		Assert.AreEqual ("0.995", tag.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void MusicBrainz_Properties_GetSet ()
	{
		// Arrange
		var tag = new ApeTag {
			MusicBrainzTrackId = "track-123",
			MusicBrainzReleaseId = "release-456",
			MusicBrainzArtistId = "artist-789",
			MusicBrainzAlbumArtistId = "albumartist-abc",
			MusicBrainzReleaseGroupId = "rg-def"
		};

		// Assert
		Assert.AreEqual ("track-123", tag.MusicBrainzTrackId);
		Assert.AreEqual ("release-456", tag.MusicBrainzReleaseId);
		Assert.AreEqual ("artist-789", tag.MusicBrainzArtistId);
		Assert.AreEqual ("albumartist-abc", tag.MusicBrainzAlbumArtistId);
		Assert.AreEqual ("rg-def", tag.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void Composer_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { Composer = "John Williams" };
		Assert.AreEqual ("John Williams", tag.Composer);
	}

	[TestMethod]
	public void Conductor_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { Conductor = "Leonard Bernstein" };
		Assert.AreEqual ("Leonard Bernstein", tag.Conductor);
	}

	[TestMethod]
	public void Copyright_GetSet_WorksCorrectly ()
	{
		var tag = new ApeTag { Copyright = "2024 Test Records" };
		Assert.AreEqual ("2024 Test Records", tag.Copyright);
	}

	// ===========================================
	// AlbumArtist Fallback Tests
	// ===========================================

	[TestMethod]
	public void AlbumArtist_GetSet_UsesAlbumArtistWithSpace ()
	{
		var tag = new ApeTag { AlbumArtist = "Various Artists" };

		Assert.AreEqual ("Various Artists", tag.AlbumArtist);
		Assert.AreEqual ("Various Artists", tag.GetValue ("Album Artist"));
	}

	[TestMethod]
	public void AlbumArtist_FallsBackToAlbumArtistNoSpace ()
	{
		var tag = new ApeTag ();

		// Some taggers use ALBUMARTIST (no space) instead of "Album Artist"
		tag.SetValue ("ALBUMARTIST", "Various Artists");

		Assert.AreEqual ("Various Artists", tag.AlbumArtist);
	}

	[TestMethod]
	public void AlbumArtist_PrefersAlbumArtistWithSpace ()
	{
		var tag = new ApeTag ();

		// When both exist, prefer "Album Artist" (with space) per APE spec
		tag.SetValue ("Album Artist", "Preferred Artist");
		tag.SetValue ("ALBUMARTIST", "Fallback Artist");

		Assert.AreEqual ("Preferred Artist", tag.AlbumArtist);
	}

	// ===========================================
	// DiscSubtitle Tests
	// ===========================================

	[TestMethod]
	public void DiscSubtitle_GetSet_Works ()
	{
		var tag = new ApeTag { DiscSubtitle = "The Early Years" };

		Assert.AreEqual ("The Early Years", tag.DiscSubtitle);
		Assert.AreEqual ("The Early Years", tag.GetValue ("DiscSubtitle"));
	}

	[TestMethod]
	public void DiscSubtitle_RoundTrip_PreservesValue ()
	{
		var original = new ApeTag { DiscSubtitle = "Disc 1: Origins" };

		var rendered = original.Render ();
		var result = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Disc 1: Origins", result.Tag!.DiscSubtitle);
	}
}
