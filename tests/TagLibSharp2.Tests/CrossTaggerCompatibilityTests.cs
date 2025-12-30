// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests;

/// <summary>
/// Tests for cross-tagger compatibility. Verifies that TagLibSharp2 uses field names
/// and formats compatible with popular tagging tools (MusicBrainz Picard, foobar2000,
/// Mp3tag, iTunes, etc.).
/// </summary>
/// <remarks>
/// <para>
/// These tests verify that:
/// 1. TagLibSharp2 can read tags written by other tools
/// 2. Tags written by TagLibSharp2 use standard field names other tools recognize
/// </para>
/// <para>
/// Field name standards tested:
/// - ID3v2 TXXX frame descriptions (case-sensitive in some tools)
/// - Vorbis Comment field names (case-insensitive per spec)
/// - MusicBrainz field mappings
/// - ReplayGain field formats
/// - Classical music metadata
/// </para>
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
public sealed class CrossTaggerCompatibilityTests
{
	/// <summary>
	/// Tests that MusicBrainz IDs use the field names that Picard expects.
	/// Picard uses uppercase field names for TXXX descriptions.
	/// </summary>
	[TestMethod]
	public void Id3v2_MusicBrainzFields_UsePicardCompatibleNames ()
	{
		var tag = new Id3v2Tag ();
		tag.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		tag.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-123456789012";
		tag.MusicBrainzArtistId = "12345678-90ab-cdef-1234-567890abcdef";
		tag.MusicBrainzReleaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";
		tag.MusicBrainzAlbumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		// Verify TXXX descriptions match Picard expectations
		var userTextFrames = parsed.Tag!.UserTextFrames;

		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MUSICBRAINZ_TRACKID"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MUSICBRAINZ_ALBUMID"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MUSICBRAINZ_ARTISTID"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MUSICBRAINZ_RELEASEGROUPID"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MUSICBRAINZ_ALBUMARTISTID"));
	}

	/// <summary>
	/// Tests that Vorbis Comment MusicBrainz fields use standard names.
	/// </summary>
	[TestMethod]
	public void VorbisComment_MusicBrainzFields_UseStandardNames ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		comment.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-123456789012";

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", parsed.Tag!.GetValue ("MUSICBRAINZ_TRACKID"));
		Assert.AreEqual ("a1b2c3d4-5678-90ab-cdef-123456789012", parsed.Tag.GetValue ("MUSICBRAINZ_ALBUMID"));
	}

	/// <summary>
	/// Tests that ReplayGain fields use the standard uppercase format with underscores.
	/// This format is recognized by foobar2000, Mp3tag, and most other tools.
	/// </summary>
	[TestMethod]
	public void Id3v2_ReplayGain_UsesStandardFormat ()
	{
		var tag = new Id3v2Tag ();
		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.20 dB";
		tag.ReplayGainAlbumPeak = "0.995123";

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		var userTextFrames = parsed.Tag!.UserTextFrames;

		Assert.IsTrue (HasUserTextFrame (userTextFrames, "REPLAYGAIN_TRACK_GAIN"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "REPLAYGAIN_TRACK_PEAK"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "REPLAYGAIN_ALBUM_GAIN"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "REPLAYGAIN_ALBUM_PEAK"));
	}

	/// <summary>
	/// Tests that Vorbis Comment ReplayGain fields use the standard uppercase format.
	/// </summary>
	[TestMethod]
	public void VorbisComment_ReplayGain_UsesStandardFormat ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.ReplayGainTrackGain = "-6.50 dB";
		comment.ReplayGainTrackPeak = "0.988547";

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("-6.50 dB", parsed.Tag!.GetValue ("REPLAYGAIN_TRACK_GAIN"));
		Assert.AreEqual ("0.988547", parsed.Tag.GetValue ("REPLAYGAIN_TRACK_PEAK"));
	}

	/// <summary>
	/// Tests that classical music fields use Picard-compatible names.
	/// </summary>
	[TestMethod]
	public void Id3v2_ClassicalMusic_UsesPicardCompatibleNames ()
	{
		var tag = new Id3v2Tag ();
		tag.Work = "Symphony No. 9 in D minor, Op. 125";
		tag.Movement = "Allegro ma non troppo";
		tag.MovementNumber = 1;
		tag.MovementTotal = 4;

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		var userTextFrames = parsed.Tag!.UserTextFrames;

		Assert.IsTrue (HasUserTextFrame (userTextFrames, "WORK"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MOVEMENT"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MOVEMENTNUMBER"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "MOVEMENTTOTAL"));
	}

	/// <summary>
	/// Tests that Vorbis Comment classical music fields use standard names.
	/// </summary>
	[TestMethod]
	public void VorbisComment_ClassicalMusic_UsesStandardNames ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.Work = "Symphony No. 9";
		comment.Movement = "Allegro";
		comment.MovementNumber = 1;
		comment.MovementTotal = 4;

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Symphony No. 9", parsed.Tag!.GetValue ("WORK"));
		Assert.AreEqual ("Allegro", parsed.Tag.GetValue ("MOVEMENT"));
		Assert.AreEqual ("1", parsed.Tag.GetValue ("MOVEMENTNUMBER"));
		Assert.AreEqual ("4", parsed.Tag.GetValue ("MOVEMENTTOTAL"));
	}

	/// <summary>
	/// Tests that Vorbis Comment can read alternative classical field names.
	/// Some tools use MOVEMENTNAME instead of MOVEMENT.
	/// </summary>
	[TestMethod]
	public void VorbisComment_ClassicalMusic_ReadsAlternativeFieldNames ()
	{
		var comment = new VorbisComment ("OtherTagger");
		comment.AddField ("MOVEMENTNAME", "Adagio");
		comment.AddField ("MOVEMENTCOUNT", "4");

		// Our Movement getter should find MOVEMENTNAME as fallback
		Assert.AreEqual ("Adagio", comment.Movement);
		Assert.AreEqual (4u, comment.MovementTotal);
	}

	/// <summary>
	/// Tests that AcoustID fields use the correct TXXX descriptions.
	/// </summary>
	[TestMethod]
	public void Id3v2_AcoustId_UsesCorrectNames ()
	{
		var tag = new Id3v2Tag ();
		tag.AcoustIdId = "abc123-def456-ghi789";
		tag.AcoustIdFingerprint = "AQAA...fingerprint...";

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		var userTextFrames = parsed.Tag!.UserTextFrames;

		Assert.IsTrue (HasUserTextFrame (userTextFrames, "ACOUSTID_ID"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "ACOUSTID_FINGERPRINT"));
	}

	/// <summary>
	/// Tests that R128 loudness normalization fields use standard names.
	/// </summary>
	[TestMethod]
	public void Id3v2_R128Gain_UsesStandardNames ()
	{
		var tag = new Id3v2Tag ();
		tag.R128TrackGain = "-512"; // Q7.8 format: -2 dB
		tag.R128AlbumGain = "256";  // Q7.8 format: +1 dB

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		var userTextFrames = parsed.Tag!.UserTextFrames;

		Assert.IsTrue (HasUserTextFrame (userTextFrames, "R128_TRACK_GAIN"));
		Assert.IsTrue (HasUserTextFrame (userTextFrames, "R128_ALBUM_GAIN"));
	}

	/// <summary>
	/// Tests that R128 dB convenience properties correctly convert Q7.8 values.
	/// </summary>
	[TestMethod]
	public void R128GainDb_ConvertsQ78ToDecibels ()
	{
		var tag = new Id3v2Tag ();

		// Test positive value: 256 in Q7.8 = +1.0 dB
		tag.R128TrackGain = "256";
		Assert.IsNotNull (tag.R128TrackGainDb);
		Assert.AreEqual (1.0, tag.R128TrackGainDb.Value, 0.001);

		// Test negative value: -512 in Q7.8 = -2.0 dB
		tag.R128AlbumGain = "-512";
		Assert.IsNotNull (tag.R128AlbumGainDb);
		Assert.AreEqual (-2.0, tag.R128AlbumGainDb.Value, 0.001);

		// Test zero: 0 in Q7.8 = 0.0 dB
		tag.R128TrackGain = "0";
		Assert.IsNotNull (tag.R128TrackGainDb);
		Assert.AreEqual (0.0, tag.R128TrackGainDb.Value, 0.001);

		// Test null returns null
		tag.R128TrackGain = null;
		Assert.IsNull (tag.R128TrackGainDb);

		// Test invalid string returns null
		tag.R128TrackGain = "not a number";
		Assert.IsNull (tag.R128TrackGainDb);
	}

	/// <summary>
	/// Tests that R128 dB convenience properties correctly set Q7.8 values.
	/// </summary>
	[TestMethod]
	public void R128GainDb_SetsQ78FromDecibels ()
	{
		var tag = new Id3v2Tag ();

		// Test setting +1.0 dB stores "256"
		tag.R128TrackGainDb = 1.0;
		Assert.AreEqual ("256", tag.R128TrackGain);

		// Test setting -2.0 dB stores "-512"
		tag.R128AlbumGainDb = -2.0;
		Assert.AreEqual ("-512", tag.R128AlbumGain);

		// Test setting 0.0 dB stores "0"
		tag.R128TrackGainDb = 0.0;
		Assert.AreEqual ("0", tag.R128TrackGain);

		// Test setting null clears the value
		tag.R128TrackGainDb = null;
		Assert.IsNull (tag.R128TrackGain);

		// Test rounding: 1.5 dB = 384 (1.5 * 256)
		tag.R128TrackGainDb = 1.5;
		Assert.AreEqual ("384", tag.R128TrackGain);
	}

	/// <summary>
	/// Tests that standard text frames use the correct frame IDs.
	/// These are critical for basic compatibility with all taggers.
	/// </summary>
	[TestMethod]
	public void Id3v2_BasicMetadata_UsesStandardFrameIds ()
	{
		var tag = new Id3v2Tag ();
		tag.Title = TestConstants.Metadata.Title;
		tag.Artist = TestConstants.Metadata.Artist;
		tag.Album = TestConstants.Metadata.Album;
		tag.Year = TestConstants.Metadata.Year;
		tag.Genre = "Classical";
		tag.Track = 5;
		tag.Composer = "Test Composer";

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, parsed.Tag!.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, parsed.Tag.Artist);
		Assert.AreEqual (TestConstants.Metadata.Album, parsed.Tag.Album);
		Assert.AreEqual (TestConstants.Metadata.Year, parsed.Tag.Year);
		Assert.AreEqual ("Classical", parsed.Tag.Genre);
		Assert.AreEqual (5u, parsed.Tag.Track);
		Assert.AreEqual ("Test Composer", parsed.Tag.Composer);
	}

	/// <summary>
	/// Tests that Vorbis Comment basic fields use uppercase names per spec.
	/// </summary>
	[TestMethod]
	public void VorbisComment_BasicMetadata_UsesUppercaseNames ()
	{
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2);
		comment.Title = TestConstants.Metadata.Title;
		comment.Artist = TestConstants.Metadata.Artist;
		comment.Album = TestConstants.Metadata.Album;
		comment.Year = TestConstants.Metadata.Year;
		comment.Genre = "Classical";
		comment.Track = 5;

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		// Verify uppercase field names per Vorbis Comment spec
		Assert.AreEqual (TestConstants.Metadata.Title, parsed.Tag!.GetValue (TestConstants.VorbisFields.Title));
		Assert.AreEqual (TestConstants.Metadata.Artist, parsed.Tag.GetValue (TestConstants.VorbisFields.Artist));
		Assert.AreEqual (TestConstants.Metadata.Album, parsed.Tag.GetValue (TestConstants.VorbisFields.Album));
		Assert.AreEqual (TestConstants.Metadata.Year, parsed.Tag.GetValue (TestConstants.VorbisFields.Date));
		Assert.AreEqual ("Classical", parsed.Tag.GetValue (TestConstants.VorbisFields.Genre));
		Assert.AreEqual ("5", parsed.Tag.GetValue ("TRACKNUMBER"));
	}

	/// <summary>
	/// Tests that compilation flag uses the iTunes-compatible "1" value.
	/// </summary>
	[TestMethod]
	public void VorbisComment_Compilation_UsesItunesCompatibleValue ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.IsCompilation = true;

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("1", parsed.Tag!.GetValue ("COMPILATION"));
		Assert.IsTrue (parsed.Tag.IsCompilation);
	}

	/// <summary>
	/// Tests round-trip preservation of all metadata through render/parse cycle.
	/// </summary>
	[TestMethod]
	public void Id3v2_AllMetadata_RoundTripsCorrectly ()
	{
		var tag = new Id3v2Tag ();

		// Basic metadata
		tag.Title = "Symphony No. 9";
		tag.Artist = "Berlin Philharmonic";
		tag.Album = "Beethoven: Complete Symphonies";
		tag.Year = "2024";
		tag.Genre = "Classical";
		tag.Track = 9;
		tag.Comment = "Live recording";

		// Extended metadata
		tag.AlbumArtist = "Various Artists";
		tag.Composer = "Ludwig van Beethoven";
		tag.Conductor = "Herbert von Karajan";
		tag.DiscNumber = 2;

		// Classical music
		tag.Work = "Symphony No. 9 in D minor, Op. 125";
		tag.Movement = "Allegro ma non troppo";
		tag.MovementNumber = 1;
		tag.MovementTotal = 4;

		// ReplayGain
		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainAlbumGain = "-5.80 dB";

		// MusicBrainz
		tag.MusicBrainzTrackId = "12345678-1234-1234-1234-123456789012";
		tag.MusicBrainzReleaseId = "abcdefab-cdef-abcd-efab-cdefabcdefab";

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		var result = parsed.Tag!;

		Assert.AreEqual ("Symphony No. 9", result.Title);
		Assert.AreEqual ("Berlin Philharmonic", result.Artist);
		Assert.AreEqual ("Beethoven: Complete Symphonies", result.Album);
		Assert.AreEqual ("2024", result.Year);
		Assert.AreEqual ("Classical", result.Genre);
		Assert.AreEqual (9u, result.Track);
		Assert.AreEqual ("Live recording", result.Comment);
		Assert.AreEqual ("Various Artists", result.AlbumArtist);
		Assert.AreEqual ("Ludwig van Beethoven", result.Composer);
		Assert.AreEqual ("Herbert von Karajan", result.Conductor);
		Assert.AreEqual (2u, result.DiscNumber);
		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", result.Work);
		Assert.AreEqual ("Allegro ma non troppo", result.Movement);
		Assert.AreEqual (1u, result.MovementNumber);
		Assert.AreEqual (4u, result.MovementTotal);
		Assert.AreEqual ("-6.50 dB", result.ReplayGainTrackGain);
		Assert.AreEqual ("-5.80 dB", result.ReplayGainAlbumGain);
		Assert.AreEqual ("12345678-1234-1234-1234-123456789012", result.MusicBrainzTrackId);
		Assert.AreEqual ("abcdefab-cdef-abcd-efab-cdefabcdefab", result.MusicBrainzReleaseId);
	}

	/// <summary>
	/// Tests round-trip preservation of Vorbis Comment metadata.
	/// </summary>
	[TestMethod]
	public void VorbisComment_AllMetadata_RoundTripsCorrectly ()
	{
		var comment = new VorbisComment ("TagLibSharp2 Test");

		// Basic metadata
		comment.Title = "Symphony No. 9";
		comment.Artist = "Berlin Philharmonic";
		comment.Album = "Beethoven: Complete Symphonies";
		comment.Year = "2024";
		comment.Genre = "Classical";
		comment.Track = 9;
		comment.Comment = "Live recording";

		// Extended metadata
		comment.AlbumArtist = "Various Artists";
		comment.Composer = "Ludwig van Beethoven";
		comment.Conductor = "Herbert von Karajan";
		comment.DiscNumber = 2;

		// Classical music
		comment.Work = "Symphony No. 9 in D minor, Op. 125";
		comment.Movement = "Allegro ma non troppo";
		comment.MovementNumber = 1;
		comment.MovementTotal = 4;

		// ReplayGain
		comment.ReplayGainTrackGain = "-6.50 dB";
		comment.ReplayGainAlbumGain = "-5.80 dB";

		// MusicBrainz
		comment.MusicBrainzTrackId = "12345678-1234-1234-1234-123456789012";
		comment.MusicBrainzReleaseId = "abcdefab-cdef-abcd-efab-cdefabcdefab";

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		var result = parsed.Tag!;

		Assert.AreEqual ("Symphony No. 9", result.Title);
		Assert.AreEqual ("Berlin Philharmonic", result.Artist);
		Assert.AreEqual ("Beethoven: Complete Symphonies", result.Album);
		Assert.AreEqual ("2024", result.Year);
		Assert.AreEqual ("Classical", result.Genre);
		Assert.AreEqual (9u, result.Track);
		Assert.AreEqual ("Live recording", result.Comment);
		Assert.AreEqual ("Various Artists", result.AlbumArtist);
		Assert.AreEqual ("Ludwig van Beethoven", result.Composer);
		Assert.AreEqual ("Herbert von Karajan", result.Conductor);
		Assert.AreEqual (2u, result.DiscNumber);
		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", result.Work);
		Assert.AreEqual ("Allegro ma non troppo", result.Movement);
		Assert.AreEqual (1u, result.MovementNumber);
		Assert.AreEqual (4u, result.MovementTotal);
		Assert.AreEqual ("-6.50 dB", result.ReplayGainTrackGain);
		Assert.AreEqual ("-5.80 dB", result.ReplayGainAlbumGain);
		Assert.AreEqual ("12345678-1234-1234-1234-123456789012", result.MusicBrainzTrackId);
		Assert.AreEqual ("abcdefab-cdef-abcd-efab-cdefabcdefab", result.MusicBrainzReleaseId);
	}

	static bool HasUserTextFrame (IEnumerable<UserTextFrame> frames, string description)
	{
		foreach (var frame in frames) {
			if (string.Equals (frame.Description, description, StringComparison.Ordinal))
				return true;
		}
		return false;
	}
}
