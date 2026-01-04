// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// TDD tests for ASF extended metadata gaps.
/// </summary>
[TestClass]
public class AsfTagExtendedMetadataTests
{
	// ═══════════════════════════════════════════════════════════════
	// Extended Metadata Fields
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Subtitle_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Subtitle = "Live at Madison Square Garden";

		Assert.AreEqual ("Live at Madison Square Garden", tag.Subtitle);
	}

	[TestMethod]
	public void Subtitle_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Subtitle = "Test";

		tag.Subtitle = null;

		Assert.IsNull (tag.Subtitle);
	}

	[TestMethod]
	public void Language_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Language = "English";

		Assert.AreEqual ("English", tag.Language);
	}

	[TestMethod]
	public void Language_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Language = "French";

		tag.Language = null;

		Assert.IsNull (tag.Language);
	}

	[TestMethod]
	public void OriginalReleaseDate_GetSet ()
	{
		var tag = new AsfTag ();

		tag.OriginalReleaseDate = "1969-09-26";

		Assert.AreEqual ("1969-09-26", tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.OriginalReleaseDate = "2000-01-01";

		tag.OriginalReleaseDate = null;

		Assert.IsNull (tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void Barcode_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Barcode = "0123456789012";

		Assert.AreEqual ("0123456789012", tag.Barcode);
	}

	[TestMethod]
	public void Barcode_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Barcode = "9876543210123";

		tag.Barcode = null;

		Assert.IsNull (tag.Barcode);
	}

	[TestMethod]
	public void CatalogNumber_GetSet ()
	{
		var tag = new AsfTag ();

		tag.CatalogNumber = "CAT-2024-001";

		Assert.AreEqual ("CAT-2024-001", tag.CatalogNumber);
	}

	[TestMethod]
	public void CatalogNumber_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.CatalogNumber = "ABC-123";

		tag.CatalogNumber = null;

		Assert.IsNull (tag.CatalogNumber);
	}

	[TestMethod]
	public void TotalTracks_GetSet ()
	{
		var tag = new AsfTag ();

		tag.TotalTracks = 12;

		Assert.AreEqual (12u, tag.TotalTracks);
	}

	[TestMethod]
	public void TotalTracks_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.TotalTracks = 10;

		tag.TotalTracks = null;

		Assert.IsNull (tag.TotalTracks);
	}

	[TestMethod]
	public void Grouping_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Grouping = "Summer Hits 2024";

		Assert.AreEqual ("Summer Hits 2024", tag.Grouping);
	}

	[TestMethod]
	public void Grouping_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Grouping = "Test";

		tag.Grouping = null;

		Assert.IsNull (tag.Grouping);
	}

	[TestMethod]
	public void Remixer_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Remixer = "DJ Shadow";

		Assert.AreEqual ("DJ Shadow", tag.Remixer);
	}

	[TestMethod]
	public void Remixer_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Remixer = "Test";

		tag.Remixer = null;

		Assert.IsNull (tag.Remixer);
	}

	[TestMethod]
	public void InitialKey_GetSet ()
	{
		var tag = new AsfTag ();

		tag.InitialKey = "Am";

		Assert.AreEqual ("Am", tag.InitialKey);
	}

	[TestMethod]
	public void InitialKey_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.InitialKey = "F#m";

		tag.InitialKey = null;

		Assert.IsNull (tag.InitialKey);
	}

	[TestMethod]
	public void Mood_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Mood = "Energetic";

		Assert.AreEqual ("Energetic", tag.Mood);
	}

	[TestMethod]
	public void Mood_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Mood = "Melancholic";

		tag.Mood = null;

		Assert.IsNull (tag.Mood);
	}

	[TestMethod]
	public void MediaType_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MediaType = "CD";

		Assert.AreEqual ("CD", tag.MediaType);
	}

	[TestMethod]
	public void MediaType_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MediaType = "Vinyl";

		tag.MediaType = null;

		Assert.IsNull (tag.MediaType);
	}

	[TestMethod]
	public void EncodedBy_GetSet ()
	{
		var tag = new AsfTag ();

		tag.EncodedBy = "LAME Encoder";

		Assert.AreEqual ("LAME Encoder", tag.EncodedBy);
	}

	[TestMethod]
	public void EncodedBy_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.EncodedBy = "Test";

		tag.EncodedBy = null;

		Assert.IsNull (tag.EncodedBy);
	}

	[TestMethod]
	public void EncoderSettings_GetSet ()
	{
		var tag = new AsfTag ();

		tag.EncoderSettings = "320kbps CBR";

		Assert.AreEqual ("320kbps CBR", tag.EncoderSettings);
	}

	[TestMethod]
	public void EncoderSettings_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.EncoderSettings = "VBR Q5";

		tag.EncoderSettings = null;

		Assert.IsNull (tag.EncoderSettings);
	}

	[TestMethod]
	public void Description_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Description = "A detailed description of this track.";

		Assert.AreEqual ("A detailed description of this track.", tag.Description);
	}

	[TestMethod]
	public void Description_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Description = "Test";

		tag.Description = null;

		Assert.IsNull (tag.Description);
	}

	[TestMethod]
	public void DateTagged_GetSet ()
	{
		var tag = new AsfTag ();

		tag.DateTagged = "2025-12-27";

		Assert.AreEqual ("2025-12-27", tag.DateTagged);
	}

	[TestMethod]
	public void DateTagged_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.DateTagged = "2020-01-01";

		tag.DateTagged = null;

		Assert.IsNull (tag.DateTagged);
	}

	[TestMethod]
	public void AmazonId_GetSet ()
	{
		var tag = new AsfTag ();

		tag.AmazonId = "B000002UAL";

		Assert.AreEqual ("B000002UAL", tag.AmazonId);
	}

	[TestMethod]
	public void AmazonId_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.AmazonId = "B123456789";

		tag.AmazonId = null;

		Assert.IsNull (tag.AmazonId);
	}

	[TestMethod]
	public void PodcastFeedUrl_GetSet ()
	{
		var tag = new AsfTag ();

		tag.PodcastFeedUrl = "https://example.com/podcast.rss";

		Assert.AreEqual ("https://example.com/podcast.rss", tag.PodcastFeedUrl);
	}

	[TestMethod]
	public void PodcastFeedUrl_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.PodcastFeedUrl = "https://test.com/feed.xml";

		tag.PodcastFeedUrl = null;

		Assert.IsNull (tag.PodcastFeedUrl);
	}

	// ═══════════════════════════════════════════════════════════════
	// MusicBrainz Extended IDs
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MusicBrainzReleaseGroupId_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzReleaseGroupId = "89ad4ac3-39f7-470e-963a-56509c546377";

		Assert.AreEqual ("89ad4ac3-39f7-470e-963a-56509c546377", tag.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void MusicBrainzReleaseGroupId_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzReleaseGroupId = "test-guid";

		tag.MusicBrainzReleaseGroupId = null;

		Assert.IsNull (tag.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void MusicBrainzWorkId_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzWorkId = "b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d";

		Assert.AreEqual ("b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d", tag.MusicBrainzWorkId);
	}

	[TestMethod]
	public void MusicBrainzWorkId_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzWorkId = "test-guid";

		tag.MusicBrainzWorkId = null;

		Assert.IsNull (tag.MusicBrainzWorkId);
	}

	[TestMethod]
	public void MusicBrainzDiscId_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzDiscId = "XzPS7vW.HPHsYemQh0HBUGr8vuU-";

		Assert.AreEqual ("XzPS7vW.HPHsYemQh0HBUGr8vuU-", tag.MusicBrainzDiscId);
	}

	[TestMethod]
	public void MusicBrainzDiscId_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzDiscId = "test-disc-id";

		tag.MusicBrainzDiscId = null;

		Assert.IsNull (tag.MusicBrainzDiscId);
	}

	// ═══════════════════════════════════════════════════════════════
	// Classical Music Fields
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Work_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Work = "Symphony No. 9 in D minor, Op. 125";

		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", tag.Work);
	}

	[TestMethod]
	public void Work_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Work = "Test Work";

		tag.Work = null;

		Assert.IsNull (tag.Work);
	}

	[TestMethod]
	public void Movement_GetSet ()
	{
		var tag = new AsfTag ();

		tag.Movement = "IV. Presto - Allegro assai";

		Assert.AreEqual ("IV. Presto - Allegro assai", tag.Movement);
	}

	[TestMethod]
	public void Movement_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.Movement = "Test Movement";

		tag.Movement = null;

		Assert.IsNull (tag.Movement);
	}

	[TestMethod]
	public void MovementNumber_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MovementNumber = 4;

		Assert.AreEqual (4u, tag.MovementNumber);
	}

	[TestMethod]
	public void MovementNumber_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MovementNumber = 3;

		tag.MovementNumber = null;

		Assert.IsNull (tag.MovementNumber);
	}

	[TestMethod]
	public void MovementTotal_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MovementTotal = 4;

		Assert.AreEqual (4u, tag.MovementTotal);
	}

	[TestMethod]
	public void MovementTotal_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MovementTotal = 5;

		tag.MovementTotal = null;

		Assert.IsNull (tag.MovementTotal);
	}

	// ═══════════════════════════════════════════════════════════════
	// Round-Trip Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void ExtendedMetadata_RoundTrip ()
	{
		// Arrange - Create minimal WMA file with metadata
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.Subtitle = "Live Version";
		result.File!.Tag.Language = "German";
		result.File!.Tag.OriginalReleaseDate = "1985-07-13";
		result.File!.Tag.Barcode = "1234567890123";
		result.File!.Tag.CatalogNumber = "DG-4530-2";

		// Act - Render and re-parse
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Assert
		Assert.AreEqual ("Live Version", reparsed.File!.Tag.Subtitle);
		Assert.AreEqual ("German", reparsed.File!.Tag.Language);
		Assert.AreEqual ("1985-07-13", reparsed.File!.Tag.OriginalReleaseDate);
		Assert.AreEqual ("1234567890123", reparsed.File!.Tag.Barcode);
		Assert.AreEqual ("DG-4530-2", reparsed.File!.Tag.CatalogNumber);
	}

	[TestMethod]
	public void MusicBrainzExtended_RoundTrip ()
	{
		// Arrange
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.MusicBrainzReleaseGroupId = "89ad4ac3-39f7-470e-963a-56509c546377";
		result.File!.Tag.MusicBrainzWorkId = "b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d";
		result.File!.Tag.MusicBrainzDiscId = "XzPS7vW.HPHsYemQh0HBUGr8vuU-";

		// Act - Render and re-parse
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Assert
		Assert.AreEqual ("89ad4ac3-39f7-470e-963a-56509c546377", reparsed.File!.Tag.MusicBrainzReleaseGroupId);
		Assert.AreEqual ("b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d", reparsed.File!.Tag.MusicBrainzWorkId);
		Assert.AreEqual ("XzPS7vW.HPHsYemQh0HBUGr8vuU-", reparsed.File!.Tag.MusicBrainzDiscId);
	}

	[TestMethod]
	public void ClassicalMusic_RoundTrip ()
	{
		// Arrange
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.Work = "Symphony No. 5 in C minor, Op. 67";
		result.File!.Tag.Movement = "I. Allegro con brio";
		result.File!.Tag.MovementNumber = 1;
		result.File!.Tag.MovementTotal = 4;

		// Act - Render and re-parse
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Assert
		Assert.AreEqual ("Symphony No. 5 in C minor, Op. 67", reparsed.File!.Tag.Work);
		Assert.AreEqual ("I. Allegro con brio", reparsed.File!.Tag.Movement);
		Assert.AreEqual (1u, reparsed.File!.Tag.MovementNumber);
		Assert.AreEqual (4u, reparsed.File!.Tag.MovementTotal);
	}

	// ═══════════════════════════════════════════════════════════════
	// MusicBrainz Extended IDs (Recording, Status, Type, Country)
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MusicBrainzRecordingId_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzRecordingId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", tag.MusicBrainzRecordingId);
	}

	[TestMethod]
	public void MusicBrainzRecordingId_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzRecordingId = "test-guid";

		tag.MusicBrainzRecordingId = null;

		Assert.IsNull (tag.MusicBrainzRecordingId);
	}

	[TestMethod]
	public void MusicBrainzReleaseStatus_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzReleaseStatus = "Official";

		Assert.AreEqual ("Official", tag.MusicBrainzReleaseStatus);
	}

	[TestMethod]
	public void MusicBrainzReleaseStatus_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzReleaseStatus = "Bootleg";

		tag.MusicBrainzReleaseStatus = null;

		Assert.IsNull (tag.MusicBrainzReleaseStatus);
	}

	[TestMethod]
	public void MusicBrainzReleaseType_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzReleaseType = "Album";

		Assert.AreEqual ("Album", tag.MusicBrainzReleaseType);
	}

	[TestMethod]
	public void MusicBrainzReleaseType_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzReleaseType = "Single";

		tag.MusicBrainzReleaseType = null;

		Assert.IsNull (tag.MusicBrainzReleaseType);
	}

	[TestMethod]
	public void MusicBrainzReleaseCountry_GetSet ()
	{
		var tag = new AsfTag ();

		tag.MusicBrainzReleaseCountry = "US";

		Assert.AreEqual ("US", tag.MusicBrainzReleaseCountry);
	}

	[TestMethod]
	public void MusicBrainzReleaseCountry_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.MusicBrainzReleaseCountry = "GB";

		tag.MusicBrainzReleaseCountry = null;

		Assert.IsNull (tag.MusicBrainzReleaseCountry);
	}

	// ═══════════════════════════════════════════════════════════════
	// AcoustId Fields
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void AcoustIdId_GetSet ()
	{
		var tag = new AsfTag ();

		tag.AcoustIdId = "f1e2d3c4-b5a6-7890-1234-567890abcdef";

		Assert.AreEqual ("f1e2d3c4-b5a6-7890-1234-567890abcdef", tag.AcoustIdId);
	}

	[TestMethod]
	public void AcoustIdId_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.AcoustIdId = "test-id";

		tag.AcoustIdId = null;

		Assert.IsNull (tag.AcoustIdId);
	}

	[TestMethod]
	public void AcoustIdFingerprint_GetSet ()
	{
		var tag = new AsfTag ();

		tag.AcoustIdFingerprint = "AQADtNQSJUmSJEkS";

		Assert.AreEqual ("AQADtNQSJUmSJEkS", tag.AcoustIdFingerprint);
	}

	[TestMethod]
	public void AcoustIdFingerprint_SetNull_Clears ()
	{
		var tag = new AsfTag ();
		tag.AcoustIdFingerprint = "test-fingerprint";

		tag.AcoustIdFingerprint = null;

		Assert.IsNull (tag.AcoustIdFingerprint);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional Round-Trip Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void ExtendedMetadata_AllFields_RoundTrip ()
	{
		// Arrange
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.TotalTracks = 15;
		result.File!.Tag.Grouping = "Best of 2024";
		result.File!.Tag.Remixer = "DJ Premier";
		result.File!.Tag.InitialKey = "Cm";
		result.File!.Tag.Mood = "Chill";
		result.File!.Tag.MediaType = "Digital";
		result.File!.Tag.EncodedBy = "Windows Media Encoder";
		result.File!.Tag.EncoderSettings = "VBR Q8";
		result.File!.Tag.Description = "A fantastic remix.";
		result.File!.Tag.DateTagged = "2025-01-03";
		result.File!.Tag.AmazonId = "B00EXAMPLE";

		// Act - Render and re-parse
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Assert
		Assert.AreEqual (15u, reparsed.File!.Tag.TotalTracks);
		Assert.AreEqual ("Best of 2024", reparsed.File!.Tag.Grouping);
		Assert.AreEqual ("DJ Premier", reparsed.File!.Tag.Remixer);
		Assert.AreEqual ("Cm", reparsed.File!.Tag.InitialKey);
		Assert.AreEqual ("Chill", reparsed.File!.Tag.Mood);
		Assert.AreEqual ("Digital", reparsed.File!.Tag.MediaType);
		Assert.AreEqual ("Windows Media Encoder", reparsed.File!.Tag.EncodedBy);
		Assert.AreEqual ("VBR Q8", reparsed.File!.Tag.EncoderSettings);
		Assert.AreEqual ("A fantastic remix.", reparsed.File!.Tag.Description);
		Assert.AreEqual ("2025-01-03", reparsed.File!.Tag.DateTagged);
		Assert.AreEqual ("B00EXAMPLE", reparsed.File!.Tag.AmazonId);
	}

	[TestMethod]
	public void MusicBrainzExtended_AllFields_RoundTrip ()
	{
		// Arrange
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.MusicBrainzRecordingId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
		result.File!.Tag.MusicBrainzReleaseStatus = "Official";
		result.File!.Tag.MusicBrainzReleaseType = "Album";
		result.File!.Tag.MusicBrainzReleaseCountry = "US";

		// Act - Render and re-parse
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Assert
		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", reparsed.File!.Tag.MusicBrainzRecordingId);
		Assert.AreEqual ("Official", reparsed.File!.Tag.MusicBrainzReleaseStatus);
		Assert.AreEqual ("Album", reparsed.File!.Tag.MusicBrainzReleaseType);
		Assert.AreEqual ("US", reparsed.File!.Tag.MusicBrainzReleaseCountry);
	}

	[TestMethod]
	public void AcoustId_AllFields_RoundTrip ()
	{
		// Arrange
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.AcoustIdId = "f1e2d3c4-b5a6-7890-1234-567890abcdef";
		result.File!.Tag.AcoustIdFingerprint = "AQADtNQSJUmSJEkS";

		// Act - Render and re-parse
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Assert
		Assert.AreEqual ("f1e2d3c4-b5a6-7890-1234-567890abcdef", reparsed.File!.Tag.AcoustIdId);
		Assert.AreEqual ("AQADtNQSJUmSJEkS", reparsed.File!.Tag.AcoustIdFingerprint);
	}
}
