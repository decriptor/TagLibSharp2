// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.WavPack;
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
	/// Tests that R128 dB setter clamps extreme values to valid Q7.8 range.
	/// The Q7.8 format uses a signed 16-bit integer (-32768 to 32767).
	/// Valid dB range is approximately -128 to +127.99 dB.
	/// </summary>
	[TestMethod]
	public void R128GainDb_ExtremePositiveValue_ClampedToMax ()
	{
		var tag = new Id3v2Tag ();

		// 200 dB would be 51200 in Q7.8, exceeding short.MaxValue (32767)
		tag.R128TrackGainDb = 200.0;

		// Should clamp to short.MaxValue (32767) = ~127.99 dB
		Assert.AreEqual ("32767", tag.R128TrackGain);
		Assert.IsTrue (tag.R128TrackGainDb!.Value < 128.0);
	}

	/// <summary>
	/// Tests that R128 dB setter clamps extreme negative values to valid Q7.8 range.
	/// </summary>
	[TestMethod]
	public void R128GainDb_ExtremeNegativeValue_ClampedToMin ()
	{
		var tag = new Id3v2Tag ();

		// -200 dB would be -51200 in Q7.8, below short.MinValue (-32768)
		tag.R128AlbumGainDb = -200.0;

		// Should clamp to short.MinValue (-32768) = -128 dB
		Assert.AreEqual ("-32768", tag.R128AlbumGain);
		Assert.IsTrue (tag.R128AlbumGainDb!.Value >= -128.0);
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

	// ═══════════════════════════════════════════════════════════════
	// APE Tag Cross-Tagger Compatibility
	// Used by: WavPack, Monkey's Audio, Musepack
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Tests that APE tag basic fields use Title-case names per APE spec.
	/// APE uses Title-case for standard fields (Title, Artist, Album, etc.)
	/// but UPPERCASE for extended fields like ReplayGain and MusicBrainz.
	/// </summary>
	[TestMethod]
	public void ApeTag_BasicMetadata_UsesTitleCaseNames ()
	{
		var tag = new ApeTag ();
		tag.Title = "Test Title";
		tag.Artist = "Test Artist";
		tag.Album = "Test Album";
		tag.Year = "2024";
		tag.Genre = "Rock";
		tag.Track = 5;

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		// APE standard fields use Title-case per spec
		Assert.AreEqual ("Test Title", parsed.Tag!.GetValue ("Title"));
		Assert.AreEqual ("Test Artist", parsed.Tag.GetValue ("Artist"));
		Assert.AreEqual ("Test Album", parsed.Tag.GetValue ("Album"));
		Assert.AreEqual ("2024", parsed.Tag.GetValue ("Year"));
		Assert.AreEqual ("Rock", parsed.Tag.GetValue ("Genre"));
		Assert.AreEqual ("5", parsed.Tag.GetValue ("Track"));
	}

	/// <summary>
	/// Tests that APE tag MusicBrainz fields use the standard field names.
	/// </summary>
	[TestMethod]
	public void ApeTag_MusicBrainzFields_UseStandardNames ()
	{
		var tag = new ApeTag ();
		tag.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		tag.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-123456789012";
		tag.MusicBrainzArtistId = "12345678-90ab-cdef-1234-567890abcdef";
		tag.MusicBrainzReleaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";
		tag.MusicBrainzAlbumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		// APE tags use MUSICBRAINZ_ prefix with underscores (Picard compatible)
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", parsed.Tag!.GetValue ("MUSICBRAINZ_TRACKID"));
		Assert.AreEqual ("a1b2c3d4-5678-90ab-cdef-123456789012", parsed.Tag.GetValue ("MUSICBRAINZ_ALBUMID"));
		Assert.AreEqual ("12345678-90ab-cdef-1234-567890abcdef", parsed.Tag.GetValue ("MUSICBRAINZ_ARTISTID"));
		Assert.AreEqual ("abcdef12-3456-7890-abcd-ef1234567890", parsed.Tag.GetValue ("MUSICBRAINZ_RELEASEGROUPID"));
		Assert.AreEqual ("fedcba98-7654-3210-fedc-ba9876543210", parsed.Tag.GetValue ("MUSICBRAINZ_ALBUMARTISTID"));
	}

	/// <summary>
	/// Tests that APE tag ReplayGain fields use the standard format.
	/// </summary>
	[TestMethod]
	public void ApeTag_ReplayGain_UsesStandardFormat ()
	{
		var tag = new ApeTag ();
		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.20 dB";
		tag.ReplayGainAlbumPeak = "0.995123";

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		Assert.AreEqual ("-6.50 dB", parsed.Tag!.GetValue ("REPLAYGAIN_TRACK_GAIN"));
		Assert.AreEqual ("0.988547", parsed.Tag.GetValue ("REPLAYGAIN_TRACK_PEAK"));
		Assert.AreEqual ("-7.20 dB", parsed.Tag.GetValue ("REPLAYGAIN_ALBUM_GAIN"));
		Assert.AreEqual ("0.995123", parsed.Tag.GetValue ("REPLAYGAIN_ALBUM_PEAK"));
	}

	/// <summary>
	/// Tests that APE tag extended metadata fields use the correct names.
	/// APE standard fields are Title-case per spec.
	/// </summary>
	[TestMethod]
	public void ApeTag_ExtendedMetadata_UsesTitleCaseNames ()
	{
		var tag = new ApeTag ();
		tag.AlbumArtist = "Various Artists";
		tag.Composer = "Test Composer";
		tag.Conductor = "Test Conductor";
		tag.Comment = "Test Comment";
		tag.DiscNumber = 2;
		tag.TotalDiscs = 3;

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		// APE standard fields use Title-case per spec
		Assert.AreEqual ("Various Artists", parsed.Tag!.GetValue ("Album Artist"));
		Assert.AreEqual ("Test Composer", parsed.Tag.GetValue ("Composer"));
		Assert.AreEqual ("Test Conductor", parsed.Tag.GetValue ("Conductor"));
		Assert.AreEqual ("Test Comment", parsed.Tag.GetValue ("Comment"));
		// APE uses "2/3" format for disc with total
		Assert.AreEqual ("2/3", parsed.Tag.GetValue ("Disc"));
	}

	/// <summary>
	/// Tests that APE tag supports custom classical music fields via SetValue.
	/// APE tags store custom fields directly - unlike ID3v2 with TXXX frames.
	/// </summary>
	[TestMethod]
	public void ApeTag_ClassicalMusic_CustomFieldsWork ()
	{
		var tag = new ApeTag ();
		// APE tags support arbitrary field names directly
		tag.SetValue ("WORK", "Symphony No. 9 in D minor, Op. 125");
		tag.SetValue ("MOVEMENT", "Allegro ma non troppo");
		tag.SetValue ("MOVEMENTNUMBER", "1");
		tag.SetValue ("MOVEMENTTOTAL", "4");

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", parsed.Tag!.GetValue ("WORK"));
		Assert.AreEqual ("Allegro ma non troppo", parsed.Tag.GetValue ("MOVEMENT"));
		Assert.AreEqual ("1", parsed.Tag.GetValue ("MOVEMENTNUMBER"));
		Assert.AreEqual ("4", parsed.Tag.GetValue ("MOVEMENTTOTAL"));
	}

	/// <summary>
	/// Tests that APE tag can read case-insensitive field names.
	/// Some tools write lowercase, some uppercase.
	/// </summary>
	[TestMethod]
	public void ApeTag_CaseInsensitiveRead_Works ()
	{
		var tag = new ApeTag ();

		// Set using lowercase directly
		tag.SetValue ("title", "Case Test");
		tag.SetValue ("artist", "Case Artist");
		tag.SetValue ("album", "Case Album");

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);

		// Should read back via properties (case-insensitive)
		Assert.AreEqual ("Case Test", parsed.Tag!.Title);
		Assert.AreEqual ("Case Artist", parsed.Tag.Artist);
		Assert.AreEqual ("Case Album", parsed.Tag.Album);
	}

	/// <summary>
	/// Tests full round-trip of all APE tag metadata.
	/// </summary>
	[TestMethod]
	public void ApeTag_AllMetadata_RoundTripsCorrectly ()
	{
		var tag = new ApeTag ();

		// Basic metadata
		tag.Title = "Symphony No. 9";
		tag.Artist = "Berlin Philharmonic";
		tag.Album = "Beethoven: Complete Symphonies";
		tag.Year = "2024";
		tag.Genre = "Classical";
		tag.Track = 9;
		tag.TotalTracks = 12;
		tag.Comment = "Live recording";

		// Extended metadata
		tag.AlbumArtist = "Various Artists";
		tag.Composer = "Ludwig van Beethoven";
		tag.Conductor = "Herbert von Karajan";
		tag.DiscNumber = 2;
		tag.TotalDiscs = 5;

		// Classical music (via SetValue since APE doesn't have built-in Work/Movement properties)
		tag.SetValue ("WORK", "Symphony No. 9 in D minor, Op. 125");
		tag.SetValue ("MOVEMENT", "Allegro ma non troppo");
		tag.SetValue ("MOVEMENTNUMBER", "1");
		tag.SetValue ("MOVEMENTTOTAL", "4");

		// ReplayGain
		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainAlbumGain = "-5.80 dB";

		// MusicBrainz
		tag.MusicBrainzTrackId = "12345678-1234-1234-1234-123456789012";
		tag.MusicBrainzReleaseId = "abcdefab-cdef-abcd-efab-cdefabcdefab";

		var rendered = tag.Render ();
		var parsed = ApeTag.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		var result = parsed.Tag!;

		Assert.AreEqual ("Symphony No. 9", result.Title);
		Assert.AreEqual ("Berlin Philharmonic", result.Artist);
		Assert.AreEqual ("Beethoven: Complete Symphonies", result.Album);
		Assert.AreEqual ("2024", result.Year);
		Assert.AreEqual ("Classical", result.Genre);
		Assert.AreEqual (9u, result.Track);
		Assert.AreEqual (12u, result.TotalTracks);
		Assert.AreEqual ("Live recording", result.Comment);
		Assert.AreEqual ("Various Artists", result.AlbumArtist);
		Assert.AreEqual ("Ludwig van Beethoven", result.Composer);
		Assert.AreEqual ("Herbert von Karajan", result.Conductor);
		Assert.AreEqual (2u, result.DiscNumber);
		Assert.AreEqual (5u, result.TotalDiscs);
		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", result.GetValue ("WORK"));
		Assert.AreEqual ("Allegro ma non troppo", result.GetValue ("MOVEMENT"));
		Assert.AreEqual ("1", result.GetValue ("MOVEMENTNUMBER"));
		Assert.AreEqual ("4", result.GetValue ("MOVEMENTTOTAL"));
		Assert.AreEqual ("-6.50 dB", result.ReplayGainTrackGain);
		Assert.AreEqual ("-5.80 dB", result.ReplayGainAlbumGain);
		Assert.AreEqual ("12345678-1234-1234-1234-123456789012", result.MusicBrainzTrackId);
		Assert.AreEqual ("abcdefab-cdef-abcd-efab-cdefabcdefab", result.MusicBrainzReleaseId);
	}

	// ═══════════════════════════════════════════════════════════════
	// File-Level Cross-Tagger Compatibility Tests
	// Tests complete file parsing with industry-standard field names
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Tests that WavPack files with APE tags preserve metadata correctly.
	/// WavPack uses APE tags for metadata, which should use standard field names.
	/// </summary>
	[TestMethod]
	[TestCategory ("WavPack")]
	public void WavPackFile_MetadataRoundTrip_PreservesStandardFieldNames ()
	{
		var data = TestBuilders.WavPack.CreateWithMetadata (
			title: "Test Title",
			artist: "Test Artist",
			album: "Test Album");

		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);

		// Verify standard APE field names
		Assert.AreEqual ("Test Title", result.File.ApeTag!.Title);
		Assert.AreEqual ("Test Artist", result.File.ApeTag.Artist);
		Assert.AreEqual ("Test Album", result.File.ApeTag.Album);

		// Verify case-insensitive lookup works (Picard compatibility)
		Assert.AreEqual ("Test Title", result.File.ApeTag.GetValue ("Title"));
		Assert.AreEqual ("Test Title", result.File.ApeTag.GetValue ("TITLE"));
		Assert.AreEqual ("Test Title", result.File.ApeTag.GetValue ("title"));
	}

	/// <summary>
	/// Tests that Monkey's Audio files with APE tags preserve metadata correctly.
	/// </summary>
	[TestMethod]
	[TestCategory ("MonkeysAudio")]
	public void MonkeysAudioFile_MetadataRoundTrip_PreservesStandardFieldNames ()
	{
		var data = TestBuilders.MonkeysAudio.CreateWithMetadata (
			title: "Monkey Title",
			artist: "Monkey Artist",
			album: "Monkey Album");

		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);

		Assert.AreEqual ("Monkey Title", result.File.ApeTag!.Title);
		Assert.AreEqual ("Monkey Artist", result.File.ApeTag.Artist);
		Assert.AreEqual ("Monkey Album", result.File.ApeTag.Album);
	}

	/// <summary>
	/// Tests that Musepack files with APE tags preserve metadata correctly.
	/// Musepack SV7/SV8 uses APE tags at end of file.
	/// </summary>
	[TestMethod]
	[TestCategory ("Musepack")]
	public void MusepackFile_MetadataRoundTrip_PreservesStandardFieldNames ()
	{
		var data = TestBuilders.Musepack.CreateWithMetadata (
			title: "Musepack Title",
			artist: "Musepack Artist",
			album: "Musepack Album");

		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);

		Assert.AreEqual ("Musepack Title", result.File.ApeTag!.Title);
		Assert.AreEqual ("Musepack Artist", result.File.ApeTag.Artist);
		Assert.AreEqual ("Musepack Album", result.File.ApeTag.Album);
	}

	/// <summary>
	/// Tests that Ogg FLAC files with VorbisComment preserve metadata correctly.
	/// Ogg FLAC uses Vorbis Comment which should use uppercase field names per spec.
	/// </summary>
	[TestMethod]
	[TestCategory ("OggFlac")]
	public void OggFlacFile_MetadataRoundTrip_PreservesStandardFieldNames ()
	{
		var data = TestBuilders.OggFlac.CreateWithMetadata (
			title: "OggFlac Title",
			artist: "OggFlac Artist",
			album: "OggFlac Album");

		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.VorbisComment);

		Assert.AreEqual ("OggFlac Title", result.File.VorbisComment!.Title);
		Assert.AreEqual ("OggFlac Artist", result.File.VorbisComment.Artist);
		Assert.AreEqual ("OggFlac Album", result.File.VorbisComment.Album);

		// Verify uppercase field names per Vorbis Comment spec
		Assert.AreEqual ("OggFlac Title", result.File.VorbisComment.GetValue ("TITLE"));
		Assert.AreEqual ("OggFlac Artist", result.File.VorbisComment.GetValue ("ARTIST"));
		Assert.AreEqual ("OggFlac Album", result.File.VorbisComment.GetValue ("ALBUM"));
	}

	/// <summary>
	/// Tests that Ogg Opus files with VorbisComment preserve metadata correctly.
	/// </summary>
	[TestMethod]
	[TestCategory ("OggOpus")]
	public void OggOpusFile_MetadataRoundTrip_PreservesStandardFieldNames ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile (
			title: "Opus Title",
			artist: "Opus Artist");

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.VorbisComment);

		Assert.AreEqual ("Opus Title", result.File.VorbisComment!.Title);
		Assert.AreEqual ("Opus Artist", result.File.VorbisComment.Artist);

		// Verify uppercase field names
		Assert.AreEqual ("Opus Title", result.File.VorbisComment.GetValue ("TITLE"));
		Assert.AreEqual ("Opus Artist", result.File.VorbisComment.GetValue ("ARTIST"));
	}

	/// <summary>
	/// Tests that WAV files with INFO tags use standard RIFF INFO chunk codes.
	/// RIFF INFO uses 4-character codes: INAM (title), IART (artist), IPRD (album).
	/// </summary>
	[TestMethod]
	[TestCategory ("Riff")]
	public void WavFile_InfoTag_UsesStandardRiffInfoCodes ()
	{
		// Create minimal WAV file
		var data = TestBuilders.Wav.CreateMinimal ();
		var wavFile = WavFile.ReadFromData (new BinaryData (data));

		Assert.IsNotNull (wavFile);

		// Set INFO tag metadata
		wavFile.InfoTag = new RiffInfoTag {
			Title = "WAV Title",
			Artist = "WAV Artist",
			Album = "WAV Album"
		};

		// Render and re-read to verify round-trip
		var rendered = wavFile.Render ();
		var reparsed = WavFile.ReadFromData (rendered);

		Assert.IsNotNull (reparsed?.InfoTag);

		// Verify standard metadata extraction
		Assert.AreEqual ("WAV Title", reparsed.InfoTag.Title);
		Assert.AreEqual ("WAV Artist", reparsed.InfoTag.Artist);
		Assert.AreEqual ("WAV Album", reparsed.InfoTag.Album);
	}

	/// <summary>
	/// Tests that AIFF files with ID3v2 tags preserve metadata correctly.
	/// AIFF stores ID3v2 tags in an "ID3 " chunk.
	/// </summary>
	[TestMethod]
	[TestCategory ("Aiff")]
	public void AiffFile_Id3v2Tag_PreservesStandardMetadata ()
	{
		// Create minimal AIFF file
		var data = TestBuilders.Aiff.CreateMinimal ();
		var result = AiffFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File);

		// Set ID3v2 tag metadata (AIFF uses 'Tag' property for ID3v2)
		result.File!.Tag = new Id3v2Tag {
			Title = "AIFF Title",
			Artist = "AIFF Artist",
			Album = "AIFF Album"
		};

		// Render and re-read to verify round-trip
		var rendered = result.File.Render ();
		var reparsed = AiffFile.Read (rendered.Span);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.IsNotNull (reparsed.File?.Tag);

		Assert.AreEqual ("AIFF Title", reparsed.File.Tag!.Title);
		Assert.AreEqual ("AIFF Artist", reparsed.File.Tag.Artist);
		Assert.AreEqual ("AIFF Album", reparsed.File.Tag.Album);
	}

	/// <summary>
	/// Tests that DSF files with ID3v2 tags preserve metadata correctly.
	/// DSF (DSD Stream File) stores ID3v2 at the end of the file.
	/// </summary>
	[TestMethod]
	[TestCategory ("Dsf")]
	public void DsfFile_Id3v2Tag_PreservesStandardMetadata ()
	{
		var data = TestBuilders.Dsf.CreateWithMetadata (
			title: "DSF Title",
			artist: "DSF Artist",
			album: "DSF Album");

		var result = DsfFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Id3v2Tag);

		Assert.AreEqual ("DSF Title", result.File.Id3v2Tag!.Title);
		Assert.AreEqual ("DSF Artist", result.File.Id3v2Tag.Artist);
		Assert.AreEqual ("DSF Album", result.File.Id3v2Tag.Album);
	}

	/// <summary>
	/// Tests that DFF files with ID3v2 tags preserve metadata correctly.
	/// DFF (DSDIFF) supports ID3v2 as an unofficial extension for metadata.
	/// </summary>
	[TestMethod]
	[TestCategory ("Dff")]
	public void DffFile_Id3v2Tag_PreservesStandardMetadata ()
	{
		// Create minimal DFF file
		var data = TestBuilders.Dff.CreateMinimal ();
		var result = TagLibSharp2.Dff.DffFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File);

		// Add ID3v2 tag metadata
		result.File!.Id3v2Tag = new Id3v2Tag {
			Title = "DFF Title",
			Artist = "DFF Artist",
			Album = "DFF Album"
		};

		// Render and re-read to verify round-trip
		var rendered = result.File.Render ();
		var reparsed = TagLibSharp2.Dff.DffFile.Parse (rendered.Span);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.IsNotNull (reparsed.File?.Id3v2Tag);

		Assert.AreEqual ("DFF Title", reparsed.File.Id3v2Tag!.Title);
		Assert.AreEqual ("DFF Artist", reparsed.File.Id3v2Tag.Artist);
		Assert.AreEqual ("DFF Album", reparsed.File.Id3v2Tag.Album);
	}

	/// <summary>
	/// Tests that APE tag format files (WavPack, Monkey's Audio, Musepack)
	/// use MusicBrainz field names compatible with Picard.
	/// </summary>
	[TestMethod]
	[TestCategory ("WavPack")]
	public void WavPackFile_MusicBrainzFields_UsePicardCompatibleNames ()
	{
		// Create file and add MusicBrainz fields
		var data = TestBuilders.WavPack.CreateMinimal ();
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		var tag = file.EnsureApeTag ();

		tag.MusicBrainzTrackId = "12345678-1234-1234-1234-123456789012";
		tag.MusicBrainzReleaseId = "abcdefab-cdef-abcd-efab-cdefabcdefab";
		tag.MusicBrainzArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		// Render and reparse
		var rendered = file.Render (data);
		var reparsed = WavPackFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Verify MusicBrainz fields use UPPERCASE with underscores (Picard format)
		Assert.AreEqual ("12345678-1234-1234-1234-123456789012",
			reparsed.File!.ApeTag!.GetValue ("MUSICBRAINZ_TRACKID"));
		Assert.AreEqual ("abcdefab-cdef-abcd-efab-cdefabcdefab",
			reparsed.File.ApeTag.GetValue ("MUSICBRAINZ_ALBUMID"));
		Assert.AreEqual ("fedcba98-7654-3210-fedc-ba9876543210",
			reparsed.File.ApeTag.GetValue ("MUSICBRAINZ_ARTISTID"));
	}

	/// <summary>
	/// Tests that APE tag files use ReplayGain field names compatible with foobar2000.
	/// </summary>
	[TestMethod]
	[TestCategory ("MonkeysAudio")]
	public void MonkeysAudioFile_ReplayGain_UsesFoobar2000CompatibleNames ()
	{
		var data = TestBuilders.MonkeysAudio.CreateMinimal ();
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		var tag = file.EnsureApeTag ();

		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.20 dB";
		tag.ReplayGainAlbumPeak = "0.995123";

		var rendered = file.Render (data);
		var reparsed = MonkeysAudioFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Verify ReplayGain fields use UPPERCASE with underscores
		Assert.AreEqual ("-6.50 dB", reparsed.File!.ApeTag!.GetValue ("REPLAYGAIN_TRACK_GAIN"));
		Assert.AreEqual ("0.988547", reparsed.File.ApeTag.GetValue ("REPLAYGAIN_TRACK_PEAK"));
		Assert.AreEqual ("-7.20 dB", reparsed.File.ApeTag.GetValue ("REPLAYGAIN_ALBUM_GAIN"));
		Assert.AreEqual ("0.995123", reparsed.File.ApeTag.GetValue ("REPLAYGAIN_ALBUM_PEAK"));
	}

	/// <summary>
	/// Tests that Ogg FLAC files support MusicBrainz IDs with standard field names.
	/// </summary>
	[TestMethod]
	[TestCategory ("OggFlac")]
	public void OggFlacFile_MusicBrainzFields_UseStandardNames ()
	{
		var data = TestBuilders.OggFlac.CreateMinimal ();
		var result = OggFlacFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		var comment = file.EnsureVorbisComment ();

		comment.MusicBrainzTrackId = "12345678-1234-1234-1234-123456789012";
		comment.MusicBrainzReleaseId = "abcdefab-cdef-abcd-efab-cdefabcdefab";

		var rendered = file.Render (data);
		var reparsed = OggFlacFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		// Verify VorbisComment uses MUSICBRAINZ_ prefix
		Assert.AreEqual ("12345678-1234-1234-1234-123456789012",
			reparsed.File!.VorbisComment!.GetValue ("MUSICBRAINZ_TRACKID"));
		Assert.AreEqual ("abcdefab-cdef-abcd-efab-cdefabcdefab",
			reparsed.File.VorbisComment.GetValue ("MUSICBRAINZ_ALBUMID"));
	}

	/// <summary>
	/// Tests full metadata round-trip for WavPack files including extended fields.
	/// </summary>
	[TestMethod]
	[TestCategory ("WavPack")]
	public void WavPackFile_ExtendedMetadata_RoundTripsCorrectly ()
	{
		var data = TestBuilders.WavPack.CreateMinimal ();
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		var tag = file.EnsureApeTag ();

		// Set all standard fields
		tag.Title = "Symphony No. 9";
		tag.Artist = "Berlin Philharmonic";
		tag.Album = "Complete Symphonies";
		tag.Year = "2024";
		tag.Genre = "Classical";
		tag.Track = 9;
		tag.TotalTracks = 12;
		tag.AlbumArtist = "Various Artists";
		tag.Composer = "Beethoven";
		tag.Conductor = "Karajan";
		tag.DiscNumber = 2;
		tag.TotalDiscs = 5;
		tag.Comment = "Live recording";

		var rendered = file.Render (data);
		var reparsed = WavPackFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);

		var resultTag = reparsed.File!.ApeTag!;
		Assert.AreEqual ("Symphony No. 9", resultTag.Title);
		Assert.AreEqual ("Berlin Philharmonic", resultTag.Artist);
		Assert.AreEqual ("Complete Symphonies", resultTag.Album);
		Assert.AreEqual ("2024", resultTag.Year);
		Assert.AreEqual ("Classical", resultTag.Genre);
		Assert.AreEqual (9u, resultTag.Track);
		Assert.AreEqual (12u, resultTag.TotalTracks);
		Assert.AreEqual ("Various Artists", resultTag.AlbumArtist);
		Assert.AreEqual ("Beethoven", resultTag.Composer);
		Assert.AreEqual ("Karajan", resultTag.Conductor);
		Assert.AreEqual (2u, resultTag.DiscNumber);
		Assert.AreEqual (5u, resultTag.TotalDiscs);
		Assert.AreEqual ("Live recording", resultTag.Comment);
	}
}
