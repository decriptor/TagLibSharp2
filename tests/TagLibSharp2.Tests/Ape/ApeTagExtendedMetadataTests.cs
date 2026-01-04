// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// TDD tests for APE extended metadata gaps.
/// </summary>
[TestClass]
public class ApeTagExtendedMetadataTests
{
	// ═══════════════════════════════════════════════════════════════
	// Extended Metadata Fields
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void BeatsPerMinute_GetSet ()
	{
		var tag = new ApeTag ();

		tag.BeatsPerMinute = 120;

		Assert.AreEqual (120u, tag.BeatsPerMinute);
	}

	[TestMethod]
	public void BeatsPerMinute_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.BeatsPerMinute = 140;

		tag.BeatsPerMinute = null;

		Assert.IsNull (tag.BeatsPerMinute);
	}

	[TestMethod]
	public void BeatsPerMinute_ParsesFromString ()
	{
		var tag = new ApeTag ();
		tag.SetValue ("BPM", "128");

		Assert.AreEqual (128u, tag.BeatsPerMinute);
	}

	[TestMethod]
	public void BeatsPerMinute_InvalidString_ReturnsNull ()
	{
		var tag = new ApeTag ();
		tag.SetValue ("BPM", "not-a-number");

		Assert.IsNull (tag.BeatsPerMinute);
	}

	[TestMethod]
	public void IsCompilation_GetSet_True ()
	{
		var tag = new ApeTag ();

		tag.IsCompilation = true;

		Assert.IsTrue (tag.IsCompilation);
	}

	[TestMethod]
	public void IsCompilation_GetSet_False ()
	{
		var tag = new ApeTag ();
		tag.IsCompilation = true;

		tag.IsCompilation = false;

		Assert.IsFalse (tag.IsCompilation);
	}

	[TestMethod]
	public void IsCompilation_ParsesFromString ()
	{
		var tag = new ApeTag ();
		tag.SetValue ("Compilation", "1");

		Assert.IsTrue (tag.IsCompilation);
	}

	[TestMethod]
	public void IsCompilation_ZeroIsFalse ()
	{
		var tag = new ApeTag ();
		tag.SetValue ("Compilation", "0");

		Assert.IsFalse (tag.IsCompilation);
	}

	[TestMethod]
	public void OriginalReleaseDate_GetSet ()
	{
		var tag = new ApeTag ();

		tag.OriginalReleaseDate = "1969-09-26";

		Assert.AreEqual ("1969-09-26", tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.OriginalReleaseDate = "2000-01-01";

		tag.OriginalReleaseDate = null;

		Assert.IsNull (tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void Grouping_GetSet ()
	{
		var tag = new ApeTag ();

		tag.Grouping = "Summer Hits 2024";

		Assert.AreEqual ("Summer Hits 2024", tag.Grouping);
	}

	[TestMethod]
	public void Grouping_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.Grouping = "Test";

		tag.Grouping = null;

		Assert.IsNull (tag.Grouping);
	}

	[TestMethod]
	public void Remixer_GetSet ()
	{
		var tag = new ApeTag ();

		tag.Remixer = "DJ Shadow";

		Assert.AreEqual ("DJ Shadow", tag.Remixer);
	}

	[TestMethod]
	public void Remixer_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.Remixer = "Test";

		tag.Remixer = null;

		Assert.IsNull (tag.Remixer);
	}

	[TestMethod]
	public void InitialKey_GetSet ()
	{
		var tag = new ApeTag ();

		tag.InitialKey = "Am";

		Assert.AreEqual ("Am", tag.InitialKey);
	}

	[TestMethod]
	public void InitialKey_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.InitialKey = "F#m";

		tag.InitialKey = null;

		Assert.IsNull (tag.InitialKey);
	}

	[TestMethod]
	public void Mood_GetSet ()
	{
		var tag = new ApeTag ();

		tag.Mood = "Energetic";

		Assert.AreEqual ("Energetic", tag.Mood);
	}

	[TestMethod]
	public void Mood_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.Mood = "Melancholic";

		tag.Mood = null;

		Assert.IsNull (tag.Mood);
	}

	[TestMethod]
	public void MediaType_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MediaType = "CD";

		Assert.AreEqual ("CD", tag.MediaType);
	}

	[TestMethod]
	public void MediaType_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MediaType = "Vinyl";

		tag.MediaType = null;

		Assert.IsNull (tag.MediaType);
	}

	[TestMethod]
	public void EncodedBy_GetSet ()
	{
		var tag = new ApeTag ();

		tag.EncodedBy = "LAME Encoder";

		Assert.AreEqual ("LAME Encoder", tag.EncodedBy);
	}

	[TestMethod]
	public void EncodedBy_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.EncodedBy = "Test";

		tag.EncodedBy = null;

		Assert.IsNull (tag.EncodedBy);
	}

	[TestMethod]
	public void EncoderSettings_GetSet ()
	{
		var tag = new ApeTag ();

		tag.EncoderSettings = "320kbps CBR";

		Assert.AreEqual ("320kbps CBR", tag.EncoderSettings);
	}

	[TestMethod]
	public void EncoderSettings_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.EncoderSettings = "VBR Q5";

		tag.EncoderSettings = null;

		Assert.IsNull (tag.EncoderSettings);
	}

	[TestMethod]
	public void Description_GetSet ()
	{
		var tag = new ApeTag ();

		tag.Description = "A detailed description of this track.";

		Assert.AreEqual ("A detailed description of this track.", tag.Description);
	}

	[TestMethod]
	public void Description_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.Description = "Test";

		tag.Description = null;

		Assert.IsNull (tag.Description);
	}

	[TestMethod]
	public void DateTagged_GetSet ()
	{
		var tag = new ApeTag ();

		tag.DateTagged = "2025-12-27";

		Assert.AreEqual ("2025-12-27", tag.DateTagged);
	}

	[TestMethod]
	public void DateTagged_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.DateTagged = "2020-01-01";

		tag.DateTagged = null;

		Assert.IsNull (tag.DateTagged);
	}

	[TestMethod]
	public void AmazonId_GetSet ()
	{
		var tag = new ApeTag ();

		tag.AmazonId = "B000002UAL";

		Assert.AreEqual ("B000002UAL", tag.AmazonId);
	}

	[TestMethod]
	public void AmazonId_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.AmazonId = "B123456789";

		tag.AmazonId = null;

		Assert.IsNull (tag.AmazonId);
	}

	[TestMethod]
	public void PodcastFeedUrl_GetSet ()
	{
		var tag = new ApeTag ();

		tag.PodcastFeedUrl = "https://example.com/podcast.rss";

		Assert.AreEqual ("https://example.com/podcast.rss", tag.PodcastFeedUrl);
	}

	[TestMethod]
	public void PodcastFeedUrl_SetNull_Clears ()
	{
		var tag = new ApeTag ();
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
		var tag = new ApeTag ();

		tag.MusicBrainzReleaseGroupId = "89ad4ac3-39f7-470e-963a-56509c546377";

		Assert.AreEqual ("89ad4ac3-39f7-470e-963a-56509c546377", tag.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void MusicBrainzReleaseGroupId_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MusicBrainzReleaseGroupId = "test-guid";

		tag.MusicBrainzReleaseGroupId = null;

		Assert.IsNull (tag.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void MusicBrainzWorkId_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MusicBrainzWorkId = "b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d";

		Assert.AreEqual ("b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d", tag.MusicBrainzWorkId);
	}

	[TestMethod]
	public void MusicBrainzWorkId_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MusicBrainzWorkId = "test-guid";

		tag.MusicBrainzWorkId = null;

		Assert.IsNull (tag.MusicBrainzWorkId);
	}

	[TestMethod]
	public void MusicBrainzDiscId_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MusicBrainzDiscId = "XzPS7vW.HPHsYemQh0HBUGr8vuU-";

		Assert.AreEqual ("XzPS7vW.HPHsYemQh0HBUGr8vuU-", tag.MusicBrainzDiscId);
	}

	[TestMethod]
	public void MusicBrainzDiscId_SetNull_Clears ()
	{
		var tag = new ApeTag ();
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
		var tag = new ApeTag ();

		tag.Work = "Symphony No. 9 in D minor, Op. 125";

		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", tag.Work);
	}

	[TestMethod]
	public void Work_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.Work = "Test Work";

		tag.Work = null;

		Assert.IsNull (tag.Work);
	}

	[TestMethod]
	public void Movement_GetSet ()
	{
		var tag = new ApeTag ();

		tag.Movement = "IV. Presto - Allegro assai";

		Assert.AreEqual ("IV. Presto - Allegro assai", tag.Movement);
	}

	[TestMethod]
	public void Movement_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.Movement = "Test Movement";

		tag.Movement = null;

		Assert.IsNull (tag.Movement);
	}

	[TestMethod]
	public void MovementNumber_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MovementNumber = 4;

		Assert.AreEqual (4u, tag.MovementNumber);
	}

	[TestMethod]
	public void MovementNumber_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MovementNumber = 3;

		tag.MovementNumber = null;

		Assert.IsNull (tag.MovementNumber);
	}

	[TestMethod]
	public void MovementTotal_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MovementTotal = 4;

		Assert.AreEqual (4u, tag.MovementTotal);
	}

	[TestMethod]
	public void MovementTotal_SetNull_Clears ()
	{
		var tag = new ApeTag ();
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
		// Arrange
		var tag = new ApeTag ();
		tag.BeatsPerMinute = 128;
		tag.IsCompilation = true;
		tag.OriginalReleaseDate = "1985-07-13";

		// Act - Render and re-parse
		var rendered = tag.RenderWithOptions (includeHeader: true);
		var parsed = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (128u, parsed.Tag!.BeatsPerMinute);
		Assert.IsTrue (parsed.Tag!.IsCompilation);
		Assert.AreEqual ("1985-07-13", parsed.Tag!.OriginalReleaseDate);
	}

	[TestMethod]
	public void MusicBrainzExtended_RoundTrip ()
	{
		// Arrange
		var tag = new ApeTag ();
		tag.MusicBrainzWorkId = "b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d";
		tag.MusicBrainzDiscId = "XzPS7vW.HPHsYemQh0HBUGr8vuU-";

		// Act - Render and re-parse
		var rendered = tag.RenderWithOptions (includeHeader: true);
		var parsed = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("b1a9c0e8-5c0f-4a0b-9c0d-1e2f3a4b5c6d", parsed.Tag!.MusicBrainzWorkId);
		Assert.AreEqual ("XzPS7vW.HPHsYemQh0HBUGr8vuU-", parsed.Tag!.MusicBrainzDiscId);
	}

	[TestMethod]
	public void ClassicalMusic_RoundTrip ()
	{
		// Arrange
		var tag = new ApeTag ();
		tag.Work = "Symphony No. 5 in C minor, Op. 67";
		tag.Movement = "I. Allegro con brio";
		tag.MovementNumber = 1;
		tag.MovementTotal = 4;

		// Act - Render and re-parse
		var rendered = tag.RenderWithOptions (includeHeader: true);
		var parsed = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Symphony No. 5 in C minor, Op. 67", parsed.Tag!.Work);
		Assert.AreEqual ("I. Allegro con brio", parsed.Tag!.Movement);
		Assert.AreEqual (1u, parsed.Tag!.MovementNumber);
		Assert.AreEqual (4u, parsed.Tag!.MovementTotal);
	}

	// ═══════════════════════════════════════════════════════════════
	// MusicBrainz Extended IDs (Recording, Status, Type, Country)
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MusicBrainzRecordingId_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MusicBrainzRecordingId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", tag.MusicBrainzRecordingId);
	}

	[TestMethod]
	public void MusicBrainzRecordingId_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MusicBrainzRecordingId = "test-guid";

		tag.MusicBrainzRecordingId = null;

		Assert.IsNull (tag.MusicBrainzRecordingId);
	}

	[TestMethod]
	public void MusicBrainzReleaseStatus_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MusicBrainzReleaseStatus = "Official";

		Assert.AreEqual ("Official", tag.MusicBrainzReleaseStatus);
	}

	[TestMethod]
	public void MusicBrainzReleaseStatus_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MusicBrainzReleaseStatus = "Bootleg";

		tag.MusicBrainzReleaseStatus = null;

		Assert.IsNull (tag.MusicBrainzReleaseStatus);
	}

	[TestMethod]
	public void MusicBrainzReleaseType_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MusicBrainzReleaseType = "Album";

		Assert.AreEqual ("Album", tag.MusicBrainzReleaseType);
	}

	[TestMethod]
	public void MusicBrainzReleaseType_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.MusicBrainzReleaseType = "Single";

		tag.MusicBrainzReleaseType = null;

		Assert.IsNull (tag.MusicBrainzReleaseType);
	}

	[TestMethod]
	public void MusicBrainzReleaseCountry_GetSet ()
	{
		var tag = new ApeTag ();

		tag.MusicBrainzReleaseCountry = "US";

		Assert.AreEqual ("US", tag.MusicBrainzReleaseCountry);
	}

	[TestMethod]
	public void MusicBrainzReleaseCountry_SetNull_Clears ()
	{
		var tag = new ApeTag ();
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
		var tag = new ApeTag ();

		tag.AcoustIdId = "f1e2d3c4-b5a6-7890-1234-567890abcdef";

		Assert.AreEqual ("f1e2d3c4-b5a6-7890-1234-567890abcdef", tag.AcoustIdId);
	}

	[TestMethod]
	public void AcoustIdId_SetNull_Clears ()
	{
		var tag = new ApeTag ();
		tag.AcoustIdId = "test-id";

		tag.AcoustIdId = null;

		Assert.IsNull (tag.AcoustIdId);
	}

	[TestMethod]
	public void AcoustIdFingerprint_GetSet ()
	{
		var tag = new ApeTag ();

		tag.AcoustIdFingerprint = "AQADtNQSJUmSJEkS";

		Assert.AreEqual ("AQADtNQSJUmSJEkS", tag.AcoustIdFingerprint);
	}

	[TestMethod]
	public void AcoustIdFingerprint_SetNull_Clears ()
	{
		var tag = new ApeTag ();
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
		var tag = new ApeTag ();
		tag.Grouping = "Best of 2024";
		tag.Remixer = "DJ Premier";
		tag.InitialKey = "Cm";
		tag.Mood = "Chill";
		tag.MediaType = "Digital";
		tag.EncodedBy = "MAC Encoder";
		tag.EncoderSettings = "Extra High";
		tag.Description = "A fantastic remix.";
		tag.DateTagged = "2025-01-03";
		tag.AmazonId = "B00EXAMPLE";

		// Act - Render and re-parse
		var rendered = tag.RenderWithOptions (includeHeader: true);
		var parsed = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Best of 2024", parsed.Tag!.Grouping);
		Assert.AreEqual ("DJ Premier", parsed.Tag!.Remixer);
		Assert.AreEqual ("Cm", parsed.Tag!.InitialKey);
		Assert.AreEqual ("Chill", parsed.Tag!.Mood);
		Assert.AreEqual ("Digital", parsed.Tag!.MediaType);
		Assert.AreEqual ("MAC Encoder", parsed.Tag!.EncodedBy);
		Assert.AreEqual ("Extra High", parsed.Tag!.EncoderSettings);
		Assert.AreEqual ("A fantastic remix.", parsed.Tag!.Description);
		Assert.AreEqual ("2025-01-03", parsed.Tag!.DateTagged);
		Assert.AreEqual ("B00EXAMPLE", parsed.Tag!.AmazonId);
	}

	[TestMethod]
	public void MusicBrainzExtended_AllFields_RoundTrip ()
	{
		// Arrange
		var tag = new ApeTag ();
		tag.MusicBrainzReleaseGroupId = "89ad4ac3-39f7-470e-963a-56509c546377";
		tag.MusicBrainzRecordingId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
		tag.MusicBrainzReleaseStatus = "Official";
		tag.MusicBrainzReleaseType = "Album";
		tag.MusicBrainzReleaseCountry = "US";

		// Act - Render and re-parse
		var rendered = tag.RenderWithOptions (includeHeader: true);
		var parsed = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("89ad4ac3-39f7-470e-963a-56509c546377", parsed.Tag!.MusicBrainzReleaseGroupId);
		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", parsed.Tag!.MusicBrainzRecordingId);
		Assert.AreEqual ("Official", parsed.Tag!.MusicBrainzReleaseStatus);
		Assert.AreEqual ("Album", parsed.Tag!.MusicBrainzReleaseType);
		Assert.AreEqual ("US", parsed.Tag!.MusicBrainzReleaseCountry);
	}

	[TestMethod]
	public void AcoustId_AllFields_RoundTrip ()
	{
		// Arrange
		var tag = new ApeTag ();
		tag.AcoustIdId = "f1e2d3c4-b5a6-7890-1234-567890abcdef";
		tag.AcoustIdFingerprint = "AQADtNQSJUmSJEkS";

		// Act - Render and re-parse
		var rendered = tag.RenderWithOptions (includeHeader: true);
		var parsed = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("f1e2d3c4-b5a6-7890-1234-567890abcdef", parsed.Tag!.AcoustIdId);
		Assert.AreEqual ("AQADtNQSJUmSJEkS", parsed.Tag!.AcoustIdFingerprint);
	}
}
