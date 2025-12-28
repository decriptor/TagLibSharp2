// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class TagCopyTests
{
	[TestMethod]
	public void CopyTo_CopiesBasicMetadata ()
	{
		var source = new Id3v2Tag {
			Title = "Test Song",
			Artist = "Test Artist",
			Album = "Test Album",
			Year = "2024",
			Comment = "Test Comment",
			Genre = "Rock",
			Track = 5
		};
		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.AreEqual ("Test Song", target.Title);
		Assert.AreEqual ("Test Artist", target.Artist);
		Assert.AreEqual ("Test Album", target.Album);
		Assert.AreEqual ("2024", target.Year);
		Assert.AreEqual ("Test Comment", target.Comment);
		Assert.AreEqual ("Rock", target.Genre);
		Assert.AreEqual ((uint)5, target.Track);
	}

	[TestMethod]
	public void CopyTo_CopiesExtendedMetadata ()
	{
		var source = new Id3v2Tag {
			AlbumArtist = "Album Artist",
			Composer = "Composer Name",
			Conductor = "Conductor Name",
			Copyright = "2024 Test Records",
			DiscNumber = 2,
			TotalDiscs = 3,
			TotalTracks = 12,
			BeatsPerMinute = 128,
			Lyrics = "Test lyrics",
			Publisher = "Test Publisher",
			Grouping = "Test Group",
			Subtitle = "Remix",
			InitialKey = "Am"
		};
		source.Isrc = "USRC17607839";
		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.AreEqual ("Album Artist", target.AlbumArtist);
		Assert.AreEqual ("Composer Name", target.Composer);
		Assert.AreEqual ("Conductor Name", target.Conductor);
		Assert.AreEqual ("2024 Test Records", target.Copyright);
		Assert.AreEqual ((uint)2, target.DiscNumber);
		Assert.AreEqual ((uint)3, target.TotalDiscs);
		Assert.AreEqual ((uint)12, target.TotalTracks);
		Assert.AreEqual ((uint)128, target.BeatsPerMinute);
		Assert.AreEqual ("Test lyrics", target.Lyrics);
		Assert.AreEqual ("USRC17607839", target.Isrc);
		Assert.AreEqual ("Test Publisher", target.Publisher);
		Assert.AreEqual ("Test Group", target.Grouping);
		Assert.AreEqual ("Remix", target.Subtitle);
		Assert.AreEqual ("Am", target.InitialKey);
	}

	[TestMethod]
	public void CopyTo_CopiesSortOrder ()
	{
		var source = new Id3v2Tag {
			AlbumSort = "Album, The",
			ArtistSort = "Artist, The",
			TitleSort = "Song, The",
			AlbumArtistSort = "Various"
		};
		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.AreEqual ("Album, The", target.AlbumSort);
		Assert.AreEqual ("Artist, The", target.ArtistSort);
		Assert.AreEqual ("Song, The", target.TitleSort);
		Assert.AreEqual ("Various", target.AlbumArtistSort);
	}

	[TestMethod]
	public void CopyTo_CopiesReplayGain ()
	{
		var source = new VorbisComment {
			ReplayGainTrackGain = "-6.50 dB",
			ReplayGainTrackPeak = "0.988547",
			ReplayGainAlbumGain = "-5.20 dB",
			ReplayGainAlbumPeak = "0.995123"
		};
		var target = new VorbisComment ();

		source.CopyTo (target);

		Assert.AreEqual ("-6.50 dB", target.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", target.ReplayGainTrackPeak);
		Assert.AreEqual ("-5.20 dB", target.ReplayGainAlbumGain);
		Assert.AreEqual ("0.995123", target.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void CopyTo_CopiesMusicBrainzIds ()
	{
		var source = new VorbisComment {
			MusicBrainzTrackId = "track-id-123",
			MusicBrainzReleaseId = "release-id-456",
			MusicBrainzArtistId = "artist-id-789",
			MusicBrainzReleaseGroupId = "rg-id-abc",
			MusicBrainzReleaseStatus = "official",
			MusicBrainzReleaseType = "album"
		};
		var target = new VorbisComment ();

		source.CopyTo (target);

		Assert.AreEqual ("track-id-123", target.MusicBrainzTrackId);
		Assert.AreEqual ("release-id-456", target.MusicBrainzReleaseId);
		Assert.AreEqual ("artist-id-789", target.MusicBrainzArtistId);
		Assert.AreEqual ("rg-id-abc", target.MusicBrainzReleaseGroupId);
		Assert.AreEqual ("official", target.MusicBrainzReleaseStatus);
		Assert.AreEqual ("album", target.MusicBrainzReleaseType);
	}

	[TestMethod]
	public void CopyTo_CopiesPictures ()
	{
		var source = new Id3v2Tag ();
		source.Pictures = [
			new PictureFrame ("image/png", PictureType.FrontCover, "Cover", [0x89, 0x50, 0x4E, 0x47])
		];
		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.HasCount (1, target.Pictures);
		Assert.AreEqual (PictureType.FrontCover, target.Pictures[0].PictureType);
		Assert.AreEqual ("Cover", target.Pictures[0].Description);
	}

	[TestMethod]
	public void CopyTo_BasicOnly_OnlyCopiesBasicMetadata ()
	{
		var source = new Id3v2Tag {
			Title = "Test Song",
			Artist = "Test Artist",
			Composer = "Should Not Copy"
		};
		var target = new Id3v2Tag ();

		source.CopyTo (target, TagCopyOptions.Basic);

		Assert.AreEqual ("Test Song", target.Title);
		Assert.AreEqual ("Test Artist", target.Artist);
		Assert.IsNull (target.Composer);
	}

	[TestMethod]
	public void CopyTo_ExcludingPictures_DoesNotCopyPictures ()
	{
		var source = new Id3v2Tag {
			Title = "Test Song"
		};
		source.Pictures = [
			new PictureFrame ("image/png", PictureType.FrontCover, "", [0x89, 0x50, 0x4E, 0x47])
		];
		var target = new Id3v2Tag ();

		source.CopyTo (target, TagCopyOptions.Basic | TagCopyOptions.Extended);

		Assert.AreEqual ("Test Song", target.Title);
		Assert.IsEmpty (target.Pictures);
	}

	[TestMethod]
	public void CopyTo_PreservesExistingValuesWhenSourceIsNull ()
	{
		var source = new Id3v2Tag {
			Title = "New Title"
			// Artist is null
		};
		var target = new Id3v2Tag {
			Title = "Old Title",
			Artist = "Existing Artist"
		};

		source.CopyTo (target);

		Assert.AreEqual ("New Title", target.Title);
		Assert.AreEqual ("Existing Artist", target.Artist);
	}

	[TestMethod]
	public void CopyTo_CrossFormat_Id3v2ToVorbis ()
	{
		var source = new Id3v2Tag {
			Title = "Test Song",
			Artist = "Test Artist",
			Album = "Test Album",
			Year = "2024",
			Track = 5,
			TotalTracks = 12
		};
		var target = new VorbisComment ();

		source.CopyTo (target);

		Assert.AreEqual ("Test Song", target.Title);
		Assert.AreEqual ("Test Artist", target.Artist);
		Assert.AreEqual ("Test Album", target.Album);
		Assert.AreEqual ("2024", target.Year);
		Assert.AreEqual ((uint)5, target.Track);
		Assert.AreEqual ((uint)12, target.TotalTracks);
	}

	[TestMethod]
	public void CopyTo_CrossFormat_VorbisToId3v2 ()
	{
		var source = new VorbisComment ();
		source.SetValue ("TITLE", "Test Song");
		source.SetValue ("ARTIST", "Test Artist");
		source.SetValue ("ALBUM", "Test Album");
		source.SetValue ("DATE", "2024");
		source.SetValue ("TRACKNUMBER", "5");
		source.SetValue ("TOTALTRACKS", "12");
		source.SetValue ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.AreEqual ("Test Song", target.Title);
		Assert.AreEqual ("Test Artist", target.Artist);
		Assert.AreEqual ("Test Album", target.Album);
		Assert.AreEqual ("2024", target.Year);
		Assert.AreEqual ((uint)5, target.Track);
		Assert.AreEqual ((uint)12, target.TotalTracks);
		// Note: Id3v2 ReplayGain support depends on implementation
	}

	[TestMethod]
	public void CopyTo_NullTarget_ThrowsArgumentNullException ()
	{
		var source = new Id3v2Tag ();

		Assert.ThrowsExactly<ArgumentNullException> (() => source.CopyTo (null!));
	}

	[TestMethod]
	public void CopyTo_CopiesArrayProperties ()
	{
		var source = new Id3v2Tag ();
		source.Performers = ["Artist 1", "Artist 2"];
		source.Genres = ["Rock", "Pop"];
		source.Composers = ["Composer 1", "Composer 2"];

		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.HasCount (2, target.Performers);
		Assert.AreEqual ("Artist 1", target.Performers[0]);
		Assert.AreEqual ("Artist 2", target.Performers[1]);
		Assert.HasCount (2, target.Genres);
		Assert.HasCount (2, target.Composers);
	}

	[TestMethod]
	public void CopyTo_CopiesCompilationFlag ()
	{
		var source = new Id3v2Tag ();
		source.IsCompilation = true;

		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.IsTrue (target.IsCompilation);
	}

	[TestMethod]
	public void CopyTo_None_CopiesNothing ()
	{
		var source = new Id3v2Tag {
			Title = "Test Song",
			Artist = "Test Artist"
		};
		var target = new Id3v2Tag ();

		source.CopyTo (target, TagCopyOptions.None);

		Assert.IsNull (target.Title);
		Assert.IsNull (target.Artist);
	}

	[TestMethod]
	public void CopyTo_MusicBrainzOnly_OnlyCopiesMusicBrainzIds ()
	{
		var source = new VorbisComment {
			Title = "Test Song",
			MusicBrainzTrackId = "track-id-123"
		};
		var target = new VorbisComment ();

		source.CopyTo (target, TagCopyOptions.MusicBrainz);

		Assert.IsNull (target.Title);
		Assert.AreEqual ("track-id-123", target.MusicBrainzTrackId);
	}
}
