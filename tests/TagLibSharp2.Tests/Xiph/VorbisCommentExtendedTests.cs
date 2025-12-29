// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

/// <summary>
/// Tests for extended metadata fields in VorbisComment (ReplayGain, MusicBrainz, etc.).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class VorbisCommentExtendedTests
{

	[TestMethod]
	public void ReplayGainTrackGain_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();

		comment.ReplayGainTrackGain = "-6.50 dB";

		Assert.AreEqual ("-6.50 dB", comment.ReplayGainTrackGain);
		Assert.AreEqual ("-6.50 dB", comment.GetValue ("REPLAYGAIN_TRACK_GAIN"));
	}

	[TestMethod]
	public void ReplayGainTrackPeak_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();

		comment.ReplayGainTrackPeak = "0.988547";

		Assert.AreEqual ("0.988547", comment.ReplayGainTrackPeak);
		Assert.AreEqual ("0.988547", comment.GetValue ("REPLAYGAIN_TRACK_PEAK"));
	}

	[TestMethod]
	public void ReplayGainAlbumGain_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();

		comment.ReplayGainAlbumGain = "-5.20 dB";

		Assert.AreEqual ("-5.20 dB", comment.ReplayGainAlbumGain);
		Assert.AreEqual ("-5.20 dB", comment.GetValue ("REPLAYGAIN_ALBUM_GAIN"));
	}

	[TestMethod]
	public void ReplayGainAlbumPeak_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();

		comment.ReplayGainAlbumPeak = "1.000000";

		Assert.AreEqual ("1.000000", comment.ReplayGainAlbumPeak);
		Assert.AreEqual ("1.000000", comment.GetValue ("REPLAYGAIN_ALBUM_PEAK"));
	}

	[TestMethod]
	public void ReplayGain_SetNull_RemovesField ()
	{
		var comment = new VorbisComment ();
		comment.ReplayGainTrackGain = "-6.50 dB";

		comment.ReplayGainTrackGain = null;

		Assert.IsNull (comment.ReplayGainTrackGain);
		Assert.IsNull (comment.GetValue ("REPLAYGAIN_TRACK_GAIN"));
	}

	[TestMethod]
	public void ReplayGain_AllFields_RenderAndReadBack ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.ReplayGainTrackGain = "-6.50 dB";
		comment.ReplayGainTrackPeak = "0.988547";
		comment.ReplayGainAlbumGain = "-5.20 dB";
		comment.ReplayGainAlbumPeak = "1.000000";

		var rendered = comment.Render ();
		var readResult = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("-6.50 dB", readResult.Tag!.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", readResult.Tag.ReplayGainTrackPeak);
		Assert.AreEqual ("-5.20 dB", readResult.Tag.ReplayGainAlbumGain);
		Assert.AreEqual ("1.000000", readResult.Tag.ReplayGainAlbumPeak);
	}



	[TestMethod]
	public void MusicBrainzTrackId_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();
		var trackId = "f4e7c9d8-1234-5678-9abc-def012345678";

		comment.MusicBrainzTrackId = trackId;

		Assert.AreEqual (trackId, comment.MusicBrainzTrackId);
		Assert.AreEqual (trackId, comment.GetValue ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void MusicBrainzReleaseId_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();
		var releaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

		comment.MusicBrainzReleaseId = releaseId;

		Assert.AreEqual (releaseId, comment.MusicBrainzReleaseId);
		Assert.AreEqual (releaseId, comment.GetValue ("MUSICBRAINZ_ALBUMID"));
	}

	[TestMethod]
	public void MusicBrainzArtistId_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();
		var artistId = "12345678-90ab-cdef-1234-567890abcdef";

		comment.MusicBrainzArtistId = artistId;

		Assert.AreEqual (artistId, comment.MusicBrainzArtistId);
		Assert.AreEqual (artistId, comment.GetValue ("MUSICBRAINZ_ARTISTID"));
	}

	[TestMethod]
	public void MusicBrainzReleaseGroupId_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();
		var releaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";

		comment.MusicBrainzReleaseGroupId = releaseGroupId;

		Assert.AreEqual (releaseGroupId, comment.MusicBrainzReleaseGroupId);
		Assert.AreEqual (releaseGroupId, comment.GetValue ("MUSICBRAINZ_RELEASEGROUPID"));
	}

	[TestMethod]
	public void MusicBrainzAlbumArtistId_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ();
		var albumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		comment.MusicBrainzAlbumArtistId = albumArtistId;

		Assert.AreEqual (albumArtistId, comment.MusicBrainzAlbumArtistId);
		Assert.AreEqual (albumArtistId, comment.GetValue ("MUSICBRAINZ_ALBUMARTISTID"));
	}

	[TestMethod]
	public void MusicBrainz_SetNull_RemovesField ()
	{
		var comment = new VorbisComment ();
		comment.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";

		comment.MusicBrainzTrackId = null;

		Assert.IsNull (comment.MusicBrainzTrackId);
		Assert.IsNull (comment.GetValue ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void MusicBrainz_AllFields_RenderAndReadBack ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		comment.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";
		comment.MusicBrainzArtistId = "12345678-90ab-cdef-1234-567890abcdef";
		comment.MusicBrainzReleaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";
		comment.MusicBrainzAlbumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		var rendered = comment.Render ();
		var readResult = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", readResult.Tag!.MusicBrainzTrackId);
		Assert.AreEqual ("a1b2c3d4-5678-90ab-cdef-1234567890ab", readResult.Tag.MusicBrainzReleaseId);
		Assert.AreEqual ("12345678-90ab-cdef-1234-567890abcdef", readResult.Tag.MusicBrainzArtistId);
		Assert.AreEqual ("abcdef12-3456-7890-abcd-ef1234567890", readResult.Tag.MusicBrainzReleaseGroupId);
		Assert.AreEqual ("fedcba98-7654-3210-fedc-ba9876543210", readResult.Tag.MusicBrainzAlbumArtistId);
	}



	[TestMethod]
	public void AllExtendedFields_WithStandardFields_RenderAndReadBack ()
	{
		var comment = new VorbisComment ("TagLibSharp2");

		// Standard fields
		comment.Title = "Test Song";
		comment.Artist = "Test Artist";
		comment.Album = "Test Album";

		// ReplayGain
		comment.ReplayGainTrackGain = "-6.50 dB";
		comment.ReplayGainTrackPeak = "0.988547";

		// MusicBrainz
		comment.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		comment.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

		var rendered = comment.Render ();
		var readResult = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		var read = readResult.Tag!;

		// Verify standard fields
		Assert.AreEqual ("Test Song", read.Title);
		Assert.AreEqual ("Test Artist", read.Artist);
		Assert.AreEqual ("Test Album", read.Album);

		// Verify ReplayGain
		Assert.AreEqual ("-6.50 dB", read.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", read.ReplayGainTrackPeak);

		// Verify MusicBrainz
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", read.MusicBrainzTrackId);
		Assert.AreEqual ("a1b2c3d4-5678-90ab-cdef-1234567890ab", read.MusicBrainzReleaseId);
	}

}
