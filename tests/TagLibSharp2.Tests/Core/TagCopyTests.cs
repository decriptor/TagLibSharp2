// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Mp4;
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
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist,
			Album = TestConstants.Metadata.Album,
			Year = "2024",
			Comment = TestConstants.Metadata.Comment,
			Genre = "Rock",
			Track = 5
		};
		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.AreEqual (TestConstants.Metadata.Title, target.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, target.Artist);
		Assert.AreEqual (TestConstants.Metadata.Album, target.Album);
		Assert.AreEqual ("2024", target.Year);
		Assert.AreEqual (TestConstants.Metadata.Comment, target.Comment);
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
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist,
			Composer = "Should Not Copy"
		};
		var target = new Id3v2Tag ();

		source.CopyTo (target, TagCopyOptions.Basic);

		Assert.AreEqual (TestConstants.Metadata.Title, target.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, target.Artist);
		Assert.IsNull (target.Composer);
	}

	[TestMethod]
	public void CopyTo_ExcludingPictures_DoesNotCopyPictures ()
	{
		var source = new Id3v2Tag {
			Title = TestConstants.Metadata.Title
		};
		source.Pictures = [
			new PictureFrame ("image/png", PictureType.FrontCover, "", [0x89, 0x50, 0x4E, 0x47])
		];
		var target = new Id3v2Tag ();

		source.CopyTo (target, TagCopyOptions.Basic | TagCopyOptions.Extended);

		Assert.AreEqual (TestConstants.Metadata.Title, target.Title);
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
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist,
			Album = TestConstants.Metadata.Album,
			Year = "2024",
			Track = 5,
			TotalTracks = 12
		};
		var target = new VorbisComment ();

		source.CopyTo (target);

		Assert.AreEqual (TestConstants.Metadata.Title, target.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, target.Artist);
		Assert.AreEqual (TestConstants.Metadata.Album, target.Album);
		Assert.AreEqual ("2024", target.Year);
		Assert.AreEqual ((uint)5, target.Track);
		Assert.AreEqual ((uint)12, target.TotalTracks);
	}

	[TestMethod]
	public void CopyTo_CrossFormat_VorbisToId3v2 ()
	{
		var source = new VorbisComment ();
		source.SetValue (TestConstants.VorbisFields.Title, TestConstants.Metadata.Title);
		source.SetValue (TestConstants.VorbisFields.Artist, TestConstants.Metadata.Artist);
		source.SetValue (TestConstants.VorbisFields.Album, TestConstants.Metadata.Album);
		source.SetValue (TestConstants.VorbisFields.Date, "2024");
		source.SetValue (TestConstants.VorbisFields.TrackNumber, "5");
		source.SetValue ("TOTALTRACKS", "12");
		source.SetValue ("REPLAYGAIN_TRACK_GAIN", "-6.50 dB");

		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.AreEqual (TestConstants.Metadata.Title, target.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, target.Artist);
		Assert.AreEqual (TestConstants.Metadata.Album, target.Album);
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
		source.AlbumArtists = ["Album Artist 1", "Album Artist 2"];
		source.Genres = ["Rock", "Pop"];
		source.Composers = ["Composer 1", "Composer 2"];

		var target = new Id3v2Tag ();

		source.CopyTo (target);

		Assert.HasCount (2, target.Performers);
		Assert.AreEqual ("Artist 1", target.Performers[0]);
		Assert.AreEqual ("Artist 2", target.Performers[1]);
		Assert.HasCount (2, target.AlbumArtists);
		Assert.AreEqual ("Album Artist 1", target.AlbumArtist);
		Assert.HasCount (2, target.Genres);
		Assert.HasCount (2, target.Composers);
		Assert.AreEqual ("Composer 1", target.Composer);
	}

	[TestMethod]
	public void CopyTo_Id3v2ToApe_KeepsAlbumArtistsAndComposers ()
	{
		var source = new Id3v2Tag {
			AlbumArtists = ["Ada", "Bea"],
			Composers = ["Cara", "Dan"],
			Performers = ["Eve", "Fay"],
			Genres = ["Jazz", "Soul"]
		};

		// The single-value getter stays the first name, not the NUL-joined blob.
		Assert.AreEqual ("Ada", source.AlbumArtist);
		Assert.AreEqual ("Cara", source.Composer);
		Assert.AreEqual ("Eve", source.Artist);
		Assert.AreEqual ("Jazz", source.Genre);
		Assert.AreEqual (-1, source.GetTextFrame ("TPE2")!.IndexOf ('\0'));

		var target = new ApeTag ();
		source.CopyTo (target);

		var parsed = ApeTag.Parse (target.Render ().Span);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		var round = parsed.Tag!;

		Assert.HasCount (2, round.AlbumArtists);
		Assert.AreEqual ("Ada", round.AlbumArtists[0]);
		Assert.AreEqual ("Bea", round.AlbumArtists[1]);
		Assert.HasCount (2, round.Composers);
		Assert.AreEqual ("Cara", round.Composers[0]);
		Assert.AreEqual ("Dan", round.Composers[1]);
		Assert.HasCount (2, round.Performers);
		Assert.AreEqual ("Eve", round.Performers[0]);
		Assert.AreEqual ("Fay", round.Performers[1]);
		Assert.HasCount (2, round.Genres);
		Assert.AreEqual ("Jazz", round.Genres[0]);
		Assert.AreEqual ("Soul", round.Genres[1]);
	}

	[TestMethod]
	public void CopyTo_ApeToId3_KeepsEveryNameAndBasicKeepsTheFirst ()
	{
		var source = new ApeTag {
			Performers = ["A", "B"],
			AlbumArtists = ["Ada", "Bea"],
			Composers = ["Cara", "Dan"],
			Genres = ["Jazz", "Soul"]
		};

		Assert.AreEqual ("A", source.Artist);
		Assert.AreEqual ("Ada", source.AlbumArtist);
		Assert.AreEqual ("Cara", source.Composer);
		Assert.AreEqual ("Jazz", source.Genre);
		Assert.HasCount (2, source.Performers);
		Assert.AreEqual ("B", source.Performers[1]);

		var target = new Id3v2Tag ();
		source.CopyTo (target);

		Assert.HasCount (2, target.Performers);
		Assert.AreEqual ("A", target.Performers[0]);
		Assert.AreEqual ("B", target.Performers[1]);
		Assert.AreEqual ("A", target.Artist);
		Assert.HasCount (2, target.AlbumArtists);
		Assert.AreEqual ("Ada", target.AlbumArtists[0]);
		Assert.AreEqual ("Bea", target.AlbumArtists[1]);
		Assert.HasCount (2, target.Composers);
		Assert.AreEqual ("Cara", target.Composers[0]);
		Assert.AreEqual ("Dan", target.Composers[1]);
		Assert.HasCount (2, target.Genres);
		Assert.AreEqual ("Jazz", target.Genres[0]);
		Assert.AreEqual ("Soul", target.Genres[1]);
		Assert.AreEqual ("Jazz", target.Genre);

		var basic = new Id3v2Tag ();
		source.CopyTo (basic, TagCopyOptions.Basic);
		Assert.AreEqual ("A", basic.Artist);
		Assert.AreEqual ("Jazz", basic.Genre);
		Assert.HasCount (1, basic.Performers);
		Assert.AreEqual ("A", basic.Performers[0]);
		Assert.IsNull (basic.AlbumArtist);
		Assert.IsNull (basic.Composer);
	}

	[TestMethod]
	public void CopyTo_ApeToMp4AndAsf_KeepsOnlyTheFirstName ()
	{
		var source = new ApeTag {
			Performers = ["A", "B"],
			AlbumArtists = ["Ada", "Bea"],
			Composers = ["Cara", "Dan"],
			Genres = ["Jazz", "Soul"]
		};

		var mp4 = new Mp4Tag ();
		source.CopyTo (mp4);
		Assert.AreEqual ("A", mp4.Artist);
		Assert.HasCount (1, mp4.Performers);
		Assert.AreEqual ("A", mp4.Performers[0]);
		Assert.AreEqual ("Ada", mp4.AlbumArtist);
		Assert.HasCount (1, mp4.AlbumArtists);
		Assert.AreEqual ("Cara", mp4.Composer);
		Assert.HasCount (1, mp4.Composers);
		Assert.AreEqual ("Jazz", mp4.Genre);
		Assert.HasCount (1, mp4.Genres);

		var asf = new AsfTag ();
		source.CopyTo (asf);
		Assert.AreEqual ("A", asf.Artist);
		Assert.HasCount (1, asf.Performers);
		Assert.AreEqual ("A", asf.Performers[0]);
		Assert.AreEqual ("Ada", asf.AlbumArtist);
		Assert.HasCount (1, asf.AlbumArtists);
		Assert.AreEqual ("Cara", asf.Composer);
		Assert.HasCount (1, asf.Composers);
		Assert.AreEqual ("Jazz", asf.Genre);
		Assert.HasCount (1, asf.Genres);
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
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist
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
			Title = TestConstants.Metadata.Title,
			MusicBrainzTrackId = "track-id-123"
		};
		var target = new VorbisComment ();

		source.CopyTo (target, TagCopyOptions.MusicBrainz);

		Assert.IsNull (target.Title);
		Assert.AreEqual ("track-id-123", target.MusicBrainzTrackId);
	}
}
