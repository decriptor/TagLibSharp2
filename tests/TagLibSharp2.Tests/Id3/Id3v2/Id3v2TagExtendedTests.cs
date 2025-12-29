// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Tests for extended metadata fields in Id3v2Tag (ReplayGain, MusicBrainz, etc.).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class Id3v2TagExtendedTests
{

	[TestMethod]
	public void ReplayGainTrackGain_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();

		tag.ReplayGainTrackGain = "-6.50 dB";

		Assert.AreEqual ("-6.50 dB", tag.ReplayGainTrackGain);
		Assert.AreEqual ("-6.50 dB", tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
	}

	[TestMethod]
	public void ReplayGainTrackPeak_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();

		tag.ReplayGainTrackPeak = "0.988547";

		Assert.AreEqual ("0.988547", tag.ReplayGainTrackPeak);
		Assert.AreEqual ("0.988547", tag.GetUserText ("REPLAYGAIN_TRACK_PEAK"));
	}

	[TestMethod]
	public void ReplayGainAlbumGain_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();

		tag.ReplayGainAlbumGain = "-5.20 dB";

		Assert.AreEqual ("-5.20 dB", tag.ReplayGainAlbumGain);
		Assert.AreEqual ("-5.20 dB", tag.GetUserText ("REPLAYGAIN_ALBUM_GAIN"));
	}

	[TestMethod]
	public void ReplayGainAlbumPeak_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();

		tag.ReplayGainAlbumPeak = "1.000000";

		Assert.AreEqual ("1.000000", tag.ReplayGainAlbumPeak);
		Assert.AreEqual ("1.000000", tag.GetUserText ("REPLAYGAIN_ALBUM_PEAK"));
	}

	[TestMethod]
	public void ReplayGain_SetNull_RemovesField ()
	{
		var tag = new Id3v2Tag ();
		tag.ReplayGainTrackGain = "-6.50 dB";

		tag.ReplayGainTrackGain = null;

		Assert.IsNull (tag.ReplayGainTrackGain);
		Assert.IsNull (tag.GetUserText ("REPLAYGAIN_TRACK_GAIN"));
	}

	[TestMethod]
	public void ReplayGain_AllFields_RenderAndReadBack ()
	{
		var tag = new Id3v2Tag ();
		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-5.20 dB";
		tag.ReplayGainAlbumPeak = "1.000000";

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("-6.50 dB", readResult.Tag!.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", readResult.Tag.ReplayGainTrackPeak);
		Assert.AreEqual ("-5.20 dB", readResult.Tag.ReplayGainAlbumGain);
		Assert.AreEqual ("1.000000", readResult.Tag.ReplayGainAlbumPeak);
	}



	[TestMethod]
	public void MusicBrainzTrackId_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var trackId = "f4e7c9d8-1234-5678-9abc-def012345678";

		tag.MusicBrainzTrackId = trackId;

		Assert.AreEqual (trackId, tag.MusicBrainzTrackId);
		Assert.AreEqual (trackId, tag.GetUserText ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void MusicBrainzReleaseId_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var releaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

		tag.MusicBrainzReleaseId = releaseId;

		Assert.AreEqual (releaseId, tag.MusicBrainzReleaseId);
		Assert.AreEqual (releaseId, tag.GetUserText ("MUSICBRAINZ_ALBUMID"));
	}

	[TestMethod]
	public void MusicBrainzArtistId_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var artistId = "12345678-90ab-cdef-1234-567890abcdef";

		tag.MusicBrainzArtistId = artistId;

		Assert.AreEqual (artistId, tag.MusicBrainzArtistId);
		Assert.AreEqual (artistId, tag.GetUserText ("MUSICBRAINZ_ARTISTID"));
	}

	[TestMethod]
	public void MusicBrainzReleaseGroupId_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var releaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";

		tag.MusicBrainzReleaseGroupId = releaseGroupId;

		Assert.AreEqual (releaseGroupId, tag.MusicBrainzReleaseGroupId);
		Assert.AreEqual (releaseGroupId, tag.GetUserText ("MUSICBRAINZ_RELEASEGROUPID"));
	}

	[TestMethod]
	public void MusicBrainzAlbumArtistId_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var albumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		tag.MusicBrainzAlbumArtistId = albumArtistId;

		Assert.AreEqual (albumArtistId, tag.MusicBrainzAlbumArtistId);
		Assert.AreEqual (albumArtistId, tag.GetUserText ("MUSICBRAINZ_ALBUMARTISTID"));
	}

	[TestMethod]
	public void MusicBrainz_SetNull_RemovesField ()
	{
		var tag = new Id3v2Tag ();
		tag.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";

		tag.MusicBrainzTrackId = null;

		Assert.IsNull (tag.MusicBrainzTrackId);
		Assert.IsNull (tag.GetUserText ("MUSICBRAINZ_TRACKID"));
	}

	[TestMethod]
	public void MusicBrainz_AllFields_RenderAndReadBack ()
	{
		var tag = new Id3v2Tag ();
		tag.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		tag.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";
		tag.MusicBrainzArtistId = "12345678-90ab-cdef-1234-567890abcdef";
		tag.MusicBrainzReleaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";
		tag.MusicBrainzAlbumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", readResult.Tag!.MusicBrainzTrackId);
		Assert.AreEqual ("a1b2c3d4-5678-90ab-cdef-1234567890ab", readResult.Tag.MusicBrainzReleaseId);
		Assert.AreEqual ("12345678-90ab-cdef-1234-567890abcdef", readResult.Tag.MusicBrainzArtistId);
		Assert.AreEqual ("abcdef12-3456-7890-abcd-ef1234567890", readResult.Tag.MusicBrainzReleaseGroupId);
		Assert.AreEqual ("fedcba98-7654-3210-fedc-ba9876543210", readResult.Tag.MusicBrainzAlbumArtistId);
	}



	[TestMethod]
	public void R128TrackGain_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var gain = "-512"; // -2 dB in Q7.8 format

		tag.R128TrackGain = gain;

		Assert.AreEqual (gain, tag.R128TrackGain);
		Assert.AreEqual (gain, tag.GetUserText ("R128_TRACK_GAIN"));
	}

	[TestMethod]
	public void R128AlbumGain_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var gain = "256"; // +1 dB in Q7.8 format

		tag.R128AlbumGain = gain;

		Assert.AreEqual (gain, tag.R128AlbumGain);
		Assert.AreEqual (gain, tag.GetUserText ("R128_ALBUM_GAIN"));
	}

	[TestMethod]
	public void R128_AllFields_RenderAndReadBack ()
	{
		var tag = new Id3v2Tag ();
		tag.R128TrackGain = "-512";
		tag.R128AlbumGain = "256";

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("-512", readResult.Tag!.R128TrackGain);
		Assert.AreEqual ("256", readResult.Tag.R128AlbumGain);
	}



	[TestMethod]
	public void AcoustIdId_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var acoustId = "f4e7c9d8-1234-5678-9abc-def012345678";

		tag.AcoustIdId = acoustId;

		Assert.AreEqual (acoustId, tag.AcoustIdId);
		Assert.AreEqual (acoustId, tag.GetUserText ("ACOUSTID_ID"));
	}

	[TestMethod]
	public void AcoustIdFingerprint_GetSet_RoundTrips ()
	{
		var tag = new Id3v2Tag ();
		var fingerprint = "AQADtNQyRUkSRZEGAAAAAA";

		tag.AcoustIdFingerprint = fingerprint;

		Assert.AreEqual (fingerprint, tag.AcoustIdFingerprint);
		Assert.AreEqual (fingerprint, tag.GetUserText ("ACOUSTID_FINGERPRINT"));
	}

	[TestMethod]
	public void AcoustId_SetNull_RemovesField ()
	{
		var tag = new Id3v2Tag ();
		tag.AcoustIdId = "f4e7c9d8-1234-5678-9abc-def012345678";

		tag.AcoustIdId = null;

		Assert.IsNull (tag.AcoustIdId);
		Assert.IsNull (tag.GetUserText ("ACOUSTID_ID"));
	}

	[TestMethod]
	public void AcoustId_AllFields_RenderAndReadBack ()
	{
		var tag = new Id3v2Tag ();
		tag.AcoustIdId = "f4e7c9d8-1234-5678-9abc-def012345678";
		tag.AcoustIdFingerprint = "AQADtNQyRUkSRZEGAAAAAA";

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (readResult.IsSuccess);
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", readResult.Tag!.AcoustIdId);
		Assert.AreEqual ("AQADtNQyRUkSRZEGAAAAAA", readResult.Tag.AcoustIdFingerprint);
	}



	[TestMethod]
	public void AllExtendedFields_WithStandardFields_RenderAndReadBack ()
	{
		var tag = new Id3v2Tag ();

		// Standard fields
		tag.Title = "Test Song";
		tag.Artist = "Test Artist";
		tag.Album = "Test Album";

		// ReplayGain
		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";

		// MusicBrainz
		tag.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
		tag.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

		var rendered = tag.Render ();
		var readResult = Id3v2Tag.Read (rendered.Span);

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
