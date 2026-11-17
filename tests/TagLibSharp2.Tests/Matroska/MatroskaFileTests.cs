// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;

namespace TagLibSharp2.Tests.Matroska;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Matroska")]
public class MatroskaFileTests
{
	[TestMethod]
	public void Read_AlbumAndTrackTargets_KeepsEachLevel ()
	{
		var file = FileWith (Tags (
			Tag (50,
				Simple ("ARTIST", "Album Artist"),
				Simple ("TITLE", "Album Name"),
				Simple ("TOTAL_PARTS", "10")),
			Tag (30,
				Simple ("TITLE", "Track Name"),
				Simple ("PART_NUMBER", "3"),
				Simple ("ARTIST", ""))));

		var result = MatroskaFile.Read (file);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Track Name", result.File!.Tag.Title);
		Assert.AreEqual ("Album Name", result.File.Tag.Album);
		Assert.IsNull (result.File.Tag.Artist);
		Assert.AreEqual (0, result.File.Tag.Performers.Length);
		Assert.AreEqual (3u, result.File.Tag.Track);
		Assert.AreEqual (10u, result.File.Tag.TotalTracks);
		Assert.AreEqual (1, result.File.Tag.AlbumArtists.Length);
		Assert.AreEqual ("Album Artist", result.File.Tag.AlbumArtists[0]);
	}

	[TestMethod]
	public void Read_ArtistOnlyOnAlbum_AppliesToTheTrack ()
	{
		var file = FileWith (Tags (
			Tag (50, Simple ("ARTIST", "Album Artist"), Simple ("TITLE", "Album Name")),
			Tag (30, Simple ("TITLE", "Track Name"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("Track Name", tag.Title);
		Assert.AreEqual ("Album Name", tag.Album);
		Assert.AreEqual ("Album Artist", tag.Artist);
		Assert.AreEqual (1, tag.AlbumArtists.Length);
		Assert.AreEqual ("Album Artist", tag.AlbumArtists[0]);
	}

	[TestMethod]
	public void Read_SeveralArtistsAtTheTrack_StaySeparate ()
	{
		var file = FileWith (Tags (Tag (30, Simple ("ARTIST", "Ada"), Simple ("ARTIST", "Bea"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("Ada", tag.Artist);
		Assert.AreEqual (2, tag.Performers.Length);
		Assert.AreEqual ("Ada", tag.Performers[0]);
		Assert.AreEqual ("Bea", tag.Performers[1]);
	}

	[TestMethod]
	public void Read_ChapterTitle_IsNotTheFileTitle ()
	{
		var file = FileWith (Tags (
			Tag (50, Simple ("TITLE", "Movie")),
			ChapterTag (30, Chapter (1), Simple ("TITLE", "Chapter"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("Movie", tag.Title);
		Assert.IsNull (tag.Album);
	}

	[TestMethod]
	public void Read_TrackTitle_IsNotTheFileTitle ()
	{
		// Turning Lime stores TITLE on track UID 2. The file title is the segment title.
		var info = Master (0x1549A966, Element (0x7BA9, "Turning Lime"u8));
		var file = FileWith (info, Tags (
			Tag (50, Simple ("GENRE", "Test")),
			ChapterTag (50, Track (2),
				Simple ("TITLE", "The Noise"),
				Simple ("DESCRIPTION", "Useless background noise"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("Turning Lime", tag.Title);
		Assert.AreEqual ("Test", tag.Genre);
		Assert.IsNull (tag.Description);
	}

	[TestMethod]
	public void Read_EditionTitle_IsNotTheFileTitle ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Segment Title"u8));
		var file = FileWith (info, Tags (ChapterTag (50, Edition (1), Simple ("TITLE", "Edition"))));

		Assert.AreEqual ("Segment Title", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_NonDefaultLanguage_LosesToTheDefault ()
	{
		var file = FileWith (Tags (Tag (30,
			Simple ("TITLE", "Bonjour", isDefault: false),
			Simple ("TITLE", "Hello"))));

		Assert.AreEqual ("Hello", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_SegmentTitle_FillsInWhenTagsHaveNoTitle ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Segment Title"u8));
		var tagged = FileWith (info, Tags (Tag (30, Simple ("TITLE", "Real Title"))));
		var bare = Matroska (Master (0x18538067, info));

		Assert.AreEqual ("Real Title", MatroskaFile.Read (tagged).File!.Tag.Title);
		Assert.AreEqual ("Segment Title", MatroskaFile.Read (bare).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_GenreOnly_KeepsSegmentTitle ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Segment Title"u8));
		var file = FileWith (info, Tags (Tag (30, Simple ("GENRE", "Drama"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("Segment Title", tag.Title);
		Assert.AreEqual ("Drama", tag.Genre);
	}

	[TestMethod]
	public void Read_EmptyTitle_BlocksSegmentTitle ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Segment Title"u8));
		var file = FileWith (info, Tags (Tag (30, Simple ("TITLE", ""))));

		Assert.IsNull (MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_HugeDuration_DoesNotThrow ()
	{
		// 1e20 ticks at the default 1 ms scale is 1e17 seconds, past TimeSpan.
		var info = Master (0x1549A966, Element (0x4489, Float32 (1e20f)));
		var file = Matroska (Master (0x18538067, info));

		var result = MatroskaFile.Read (file);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.Duration);
	}

	[TestMethod]
	public void Read_Duration_UsesTimestampScale ()
	{
		// 1500 segment ticks at 1 ms per tick is 1.5 seconds.
		var scale = Element (0x2AD7B1, new byte[] { 0x0F, 0x42, 0x40 });
		var duration = Element (0x4489, Float32 (1500f));
		var info = Master (0x1549A966, Concat (scale, duration));
		var audio = Master (0x1654AE6B, Master (0xAE, Element (0x83, new byte[] { 2 })));
		var video = Master (0x1654AE6B, Master (0xAE, Element (0x83, new byte[] { 1 })));
		var file = Matroska (Master (0x18538067, Concat (info, audio, video, Tags (Tag (30, Simple ("GENRE", "Drama"))))));

		var result = MatroskaFile.Read (file);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (TimeSpan.FromSeconds (1.5), result.File!.Duration);
		Assert.AreEqual (MediaTypes.Audio | MediaTypes.Video, result.File.MediaTypes);
		Assert.AreEqual ("Drama", result.File.Tag.Genre);
	}

	[TestMethod]
	public void Read_PaddingBeforeInfo_StillReadsTheSegment ()
	{
		// A live-style segment uses an unknown size and then padding before the first element.
		var info = Master (0x1549A966, Concat (
			Element (0x7BA9, "After Junk"u8),
			Element (0x4489, Float32 (2000f))));
		var segment = Concat (Id (0x18538067), new byte[] { 0xFF }, new byte[] { 0x0A, 0x0A, 0x0A, 0x0A }, info);
		var result = MatroskaFile.Read (Matroska (segment));

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("After Junk", result.File!.Tag.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File.Duration);
	}

	[TestMethod]
	public void Read_Tracks_ReportAudioAndVideoProperties ()
	{
		var audio = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x86, "A_VORBIS"u8),
			Master (0xE1, Concat (
				Element (0xB5, Float32 (48000f)),
				Element (0x9F, new byte[] { 2 }),
				Element (0x6264, new byte[] { 16 })))));
		var video = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Element (0x86, "V_VP8"u8),
			Master (0xE0, Concat (
				Element (0xB0, new byte[] { 0x02, 0x80 }),
				Element (0xBA, new byte[] { 0x01, 0x68 })))));
		var file = FileWith (Master (0x1654AE6B, Concat (audio, video)));

		IMediaFile media = MatroskaFile.Read (file).File!;

		Assert.AreEqual (48000, media.AudioProperties!.SampleRate);
		Assert.AreEqual (2, media.AudioProperties.Channels);
		Assert.AreEqual (16, media.AudioProperties.BitsPerSample);
		Assert.AreEqual ("Vorbis", media.AudioProperties.Codec);
		var videoProps = media.VideoProperties!.Value;
		Assert.AreEqual (640, videoProps.Width);
		Assert.AreEqual (360, videoProps.Height);
		Assert.AreEqual ("VP8", videoProps.Codec);
	}

	[TestMethod]
	public void Read_DefaultTrack_WinsOverAnEarlierDisabledTrack ()
	{
		// FlagDefault defaults to 1. A commentary track marked 0 must not hide the main track.
		var commentary = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x88, new byte[] { 0 }),
			Master (0xE1, Element (0xB5, Float32 (22050f)))));
		var main = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Master (0xE1, Element (0xB5, Float32 (48000f)))));
		var small = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Element (0x88, new byte[] { 0 }),
			Master (0xE0, Concat (Element (0xB0, new byte[] { 1, 0x40 }), Element (0xBA, new byte[] { 0xF0 })))));
		var picture = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Master (0xE0, Concat (Element (0xB0, new byte[] { 0x02, 0x80 }), Element (0xBA, new byte[] { 0x01, 0x68 })))));
		var file = FileWith (Master (0x1654AE6B, Concat (commentary, main, small, picture)));

		IMediaFile media = MatroskaFile.Read (file).File!;
		var video = media.VideoProperties!.Value;

		Assert.AreEqual (48000, media.AudioProperties!.SampleRate);
		Assert.AreEqual (640, video.Width);
		Assert.AreEqual (360, video.Height);
	}

	[TestMethod]
	public void Read_VfwFourCc_Mp42_IsMpeg4Visual ()
	{
		var header = new byte[40];
		header[0] = 40;
		"MP42"u8.CopyTo (header.AsSpan (16));
		var video = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Element (0x86, "V_MS/VFW/FOURCC"u8),
			Element (0x63A2, header),
			Master (0xE0, Concat (Element (0xB0, new byte[] { 0x03, 0x56 }), Element (0xBA, new byte[] { 0x01, 0xE0 })))));
		var file = FileWith (Master (0x1654AE6B, video));

		var videoProps = ((IMediaFile)MatroskaFile.Read (file).File!).VideoProperties!.Value;

		Assert.AreEqual ("MPEG-4 Visual", videoProps.Codec);
	}

	[TestMethod]
	public void Read_VfwFourCc_UnknownPrintable_IsTheFourCc ()
	{
		var header = new byte[40];
		header[0] = 40;
		"H264"u8.CopyTo (header.AsSpan (16));
		var video = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Element (0x86, "V_MS/VFW/FOURCC"u8),
			Element (0x63A2, header),
			Master (0xE0, Concat (Element (0xB0, new byte[] { 0x02, 0x80 }), Element (0xBA, new byte[] { 0x01, 0x68 })))));
		var file = FileWith (Master (0x1654AE6B, video));

		var videoProps = ((IMediaFile)MatroskaFile.Read (file).File!).VideoProperties!.Value;

		Assert.AreEqual ("H264", videoProps.Codec);
	}

	[TestMethod]
	public void Read_NestedSortWith_FillsTheSortNames ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("TITLE", "The Album", "album")),
			Tag (30,
				SimpleSorted ("TITLE", "The Song", "song"),
				SimpleSorted ("ARTIST", "Ada", "ada"),
				Simple ("TITLE_SORT", "flat"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("song", tag.TitleSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
		Assert.AreEqual ("album", tag.AlbumSort);
		Assert.AreEqual ("The Song", tag.Title);
		Assert.AreEqual ("The Album", tag.Album);
	}

	[TestMethod]
	public void Read_ZeroTargetUid_StaysOnTheFile ()
	{
		foreach (var id in new ulong[] { 0x63C5, 0x63C9, 0x63C4, 0x63C6 }) {
			var file = FileWith (Tags (ChapterTag (30, Element (id, new byte[] { 0 }), Simple ("TITLE", "All"))));
			Assert.AreEqual ("All", MatroskaFile.Read (file).File!.Tag.Title, id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
		}

		var empty = FileWith (Tags (ChapterTag (30, Element (0x63C5, Array.Empty<byte> ()), Simple ("TITLE", "Empty"))));
		Assert.AreEqual ("Empty", MatroskaFile.Read (empty).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_MixedTargetUids_ZeroMeansAll_RegardlessOfOrder ()
	{
		foreach (var id in new ulong[] { 0x63C5, 0x63C9, 0x63C4, 0x63C6 }) {
			var label = id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture);
			var zeroThenSpecific = Concat (Element (id, new byte[] { 0 }), Element (id, new byte[] { 1 }));
			var specificThenZero = Concat (Element (id, new byte[] { 1 }), Element (id, new byte[] { 0 }));
			var twoSpecific = Concat (Element (id, new byte[] { 1 }), Element (id, new byte[] { 2 }));

			Assert.AreEqual ("All", TitleOf (zeroThenSpecific), label);
			Assert.AreEqual ("All", TitleOf (specificThenZero), label);
			Assert.IsNull (TitleOf (twoSpecific), label);
		}
	}

	static string? TitleOf (byte[] targetUids)
	{
		var file = FileWith (Tags (ChapterTag (30, targetUids, Simple ("TITLE", "All"))));
		return MatroskaFile.Read (file).File!.Tag.Title;
	}

	[TestMethod]
	public void Read_DocTypeTerminatedByNul_StillOpens ()
	{
		var file = Matroska (Master (0x18538067, Tags (Tag (30, Simple ("TITLE", "Kept")))), "matroska\0");

		var result = MatroskaFile.Read (file);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Kept", result.File!.Tag.Title);
	}

	[TestMethod]
	public void Read_TagString_StopsAtTheFirstNul ()
	{
		var file = FileWith (Tags (Tag (30, Simple ("TITLE", "eb\0l"))));

		Assert.AreEqual ("eb", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void SortSet_ReplacesTheNestedSort ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("TITLE", "The Album", "album")),
			Tag (30,
				SimpleSorted ("TITLE", "The Song", "song"),
				SimpleSorted ("ARTIST", "Ada", "ada"))));
		var tag = MatroskaFile.Read (file).File!.Tag;

		tag.TitleSort = "other";
		tag.ArtistSort = "other artist";
		tag.AlbumSort = "other album";

		Assert.AreEqual ("other", tag.TitleSort);
		Assert.AreEqual ("other artist", tag.ArtistSort);
		Assert.AreEqual ("other album", tag.AlbumSort);
		Assert.AreEqual ("The Song", tag.Title);
		Assert.AreEqual ("Ada", tag.Artist);
		Assert.AreEqual ("The Album", tag.Album);

		tag.TitleSort = null;
		tag.ArtistSort = null;
		tag.AlbumSort = null;

		Assert.IsNull (tag.TitleSort);
		Assert.IsNull (tag.ArtistSort);
		Assert.IsNull (tag.AlbumSort);
	}

	[TestMethod]
	public void SortSet_SharedAlbumTitle_KeepsBothSorts ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("TITLE", "The Album", "album")),
			Tag (30, Simple ("GENRE", "Jazz"))));
		var tag = MatroskaFile.Read (file).File!.Tag;

		tag.TitleSort = "track sort";

		Assert.AreEqual ("track sort", tag.TitleSort);
		Assert.AreEqual ("track sort", tag.AlbumSort);
		Assert.AreEqual ("The Album", tag.Title);
		Assert.AreEqual ("The Album", tag.Album);

		tag.AlbumSort = "other";

		Assert.AreEqual ("other", tag.TitleSort);
		Assert.AreEqual ("other", tag.AlbumSort);
	}

	[TestMethod]
	public void SortSet_Null_ClearsAFlatSortAboveTheItem ()
	{
		var album = MatroskaFile.Read (FileWith (Tags (
			Tag (50, SimpleSorted ("TITLE", "The Album", "album"), Simple ("ALBUM_SORT", "flat")),
			Tag (30, Simple ("GENRE", "Jazz"))))).File!.Tag;
		Assert.AreEqual ("album", album.AlbumSort);
		album.AlbumSort = null;
		Assert.IsNull (album.AlbumSort);

		var title = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("TITLE", "The Album"), Simple ("TITLE_SORT", "flat")),
			Tag (30, Simple ("TITLE", "The Song"))))).File!.Tag;
		Assert.AreEqual ("flat", title.TitleSort);
		title.TitleSort = null;
		Assert.IsNull (title.TitleSort);

		var artist = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST_SORT", "flat")),
			Tag (30, Simple ("ARTIST", "Ada"))))).File!.Tag;
		Assert.AreEqual ("flat", artist.ArtistSort);
		artist.ArtistSort = null;
		Assert.IsNull (artist.ArtistSort);
	}

	[TestMethod]
	public void Read_DecimalBpm_Truncates ()
	{
		var whole = FileWith (Tags (Tag (30, Simple ("BPM", "120.0"))));
		var fraction = FileWith (Tags (Tag (30, Simple ("BPM", "98.7"))));

		Assert.AreEqual (120u, MatroskaFile.Read (whole).File!.Tag.BeatsPerMinute);
		Assert.AreEqual (98u, MatroskaFile.Read (fraction).File!.Tag.BeatsPerMinute);
	}

	[TestMethod]
	public void Read_OmittedSamplingFrequency_DefaultsTo8000 ()
	{
		var audio = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Master (0xE1, Element (0x9F, new byte[] { 2 }))));
		var file = FileWith (Master (0x1654AE6B, audio));

		var media = (IMediaFile)MatroskaFile.Read (file).File!;

		Assert.AreEqual (8000, media.AudioProperties!.SampleRate);
		Assert.AreEqual (2, media.AudioProperties.Channels);
	}

	[TestMethod]
	public void Read_OutputSamplingFrequency_IsThePlaybackRate ()
	{
		var audio = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x86, "A_AAC"u8),
			Master (0xE1, Concat (
				Element (0xB5, Float32 (24000f)),
				Element (0x78B5, Float32 (48000f))))));
		var file = FileWith (Master (0x1654AE6B, audio));

		Assert.AreEqual (48000, ((IMediaFile)MatroskaFile.Read (file).File!).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Read_Opus_Reports48000UnlessTheOutputFrequencyIsSet ()
	{
		var stored = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x86, "A_OPUS"u8),
			Master (0xE1, Element (0xB5, Float32 (44100f)))));
		var overridden = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x86, "A_OPUS"u8),
			Master (0xE1, Concat (
				Element (0xB5, Float32 (44100f)),
				Element (0x78B5, Float32 (16000f))))));

		Assert.AreEqual (48000, ((IMediaFile)MatroskaFile.Read (FileWith (Master (0x1654AE6B, stored))).File!).AudioProperties!.SampleRate);
		Assert.AreEqual (16000, ((IMediaFile)MatroskaFile.Read (FileWith (Master (0x1654AE6B, overridden))).File!).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Read_ImageAttachment_IsAPicture_AndAFontIsNot ()
	{
		var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
		var cover = Master (0x61A7, Concat (
			Element (0x466E, "cover.png"u8),
			Element (0x4660, "image/png"u8),
			Element (0x467E, "Front"u8),
			Element (0x465C, png)));
		var font = Master (0x61A7, Concat (
			Element (0x466E, "font.ttf"u8),
			Element (0x4660, "application/x-truetype-font"u8),
			Element (0x465C, new byte[] { 1, 2, 3, 4 })));
		var file = FileWith (Master (0x1941A469, Concat (cover, font)));

		var pictures = MatroskaFile.Read (file).File!.Tag.Pictures;

		Assert.AreEqual (1, pictures.Length);
		Assert.AreEqual ("image/png", pictures[0].MimeType);
		Assert.AreEqual (PictureType.FrontCover, pictures[0].PictureType);
		Assert.AreEqual ("Front", pictures[0].Description);
		CollectionAssert.AreEqual (png, pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void Read_OfficialTagNames_FillTheCommonProperties ()
	{
		var file = FileWith (Tags (Tag (30,
			Simple ("DATE_RECORDED", "2017"),
			Simple ("ACTOR", "Lime"),
			Simple ("SUBTITLE", "Theme"),
			Simple ("BPM", "96"),
			Simple ("MUSICBRAINZ_TRACKID", "abc"),
			Simple ("TITLE_SORT", "lime"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("2017", tag.OriginalReleaseDate);
		Assert.IsNull (tag.Artist);
		Assert.AreEqual ("Lime", tag.Performers[0]);
		Assert.AreEqual ("Theme", tag.Subtitle);
		Assert.AreEqual (96u, tag.BeatsPerMinute);
		Assert.AreEqual ("abc", tag.MusicBrainzTrackId);
		Assert.AreEqual ("lime", tag.TitleSort);
	}

	[TestMethod]
	public void Read_TagsAfterKnownCluster_AreRead ()
	{
		var cluster = Master (0x1F43B675, Element (0xEC, new byte[4]));
		var file = Matroska (Master (0x18538067, Concat (cluster, Tags (Tag (30, Simple ("TITLE", "After"))))));

		Assert.AreEqual ("After", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_UnknownSizeCluster_DoesNotSwallowTheFollowingTags ()
	{
		var cluster = Concat (Id (0x1F43B675), new byte[] { 0xFF }, Element (0xEC, new byte[4]));
		var file = Matroska (Master (0x18538067, Concat (cluster, Tags (Tag (30, Simple ("TITLE", "After"))))));

		Assert.AreEqual ("After", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_WebM_ReadsTags ()
	{
		var file = Matroska (Master (0x18538067, Tags (Tag (50, Simple ("TITLE", "Web")))), "webm");

		Assert.AreEqual ("Web", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_TruncatedHeader_ReturnsFailure ()
	{
		byte[] data = [0x1A, 0x45, 0xDF, 0xA3, 0xB2];

		var result = MatroskaFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
	}

	[TestMethod]
	public void DetectFormat_EbmlMagicAndExtensions ()
	{
		var file = Matroska (Master (0x18538067, Tags (Tag (30, Simple ("TITLE", "A")))));

		Assert.AreEqual (MediaFormat.Matroska, MediaFile.DetectFormat (file));
		Assert.AreEqual (MediaFormat.Matroska, MediaFile.DetectFormat (new byte[] { 1, 2, 3, 4 }, "movie.mkv"));
		Assert.AreEqual (MediaFormat.Matroska, MediaFile.DetectFormat (new byte[] { 1, 2, 3, 4 }, "clip.webm"));
		Assert.AreEqual ("A", MediaFile.ReadFromData (file).Tag!.Title);
	}

	[TestMethod]
	public void Title_Set_ReplacesOnlyTheTrackTitle ()
	{
		var file = FileWith (Tags (
			Tag (50, Simple ("TITLE", "Album Name")),
			Tag (30, Simple ("TITLE", "Track Name"))));
		var tag = MatroskaFile.Read (file).File!.Tag;

		tag.Title = "Renamed";

		Assert.AreEqual ("Renamed", tag.Title);
		Assert.AreEqual ("Album Name", tag.Album);
	}

	[TestMethod]
	public void Read_RejectsAFileThatIsNotMatroska ()
	{
		AssertFailure (new byte[] { 1, 2, 3, 4 }, "missing EBML header");
		AssertFailure (new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, "truncated");
		AssertFailure (Matroska (Master (0x18538067), "mkv"), "DocType is mkv");
		AssertFailure (Concat (EbmlWithoutDocType (), Master (0x18538067)), "DocType is missing");
		AssertFailure (Concat (EbmlHeader (), new byte[] { 0xEC, 0xFF }), "unknown before Segment");
		AssertFailure (EbmlHeader (), "no Segment");
	}

	[TestMethod]
	public void Read_ZeroByteBeforeInfo_StillReadsTheTitle ()
	{
		var file = Matroska (Master (0x18538067, Concat (new byte[] { 0 }, Tags (Tag (30, Simple ("TITLE", "Kept"))))));

		Assert.AreEqual ("Kept", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Read_EightByteDuration_IsUsed ()
	{
		var payload = new byte[8];
		BinaryPrimitives.WriteDoubleBigEndian (payload, 1000d);
		var file = FileWith (Master (0x1549A966, Element (0x4489, payload)));

		Assert.AreEqual (TimeSpan.FromSeconds (1), MatroskaFile.Read (file).File!.Duration);
	}

	[TestMethod]
	public void Read_TryReadAndResultEquality_MatchTheReadResult ()
	{
		var bad = new byte[] { 1, 2, 3, 4 };
		var first = MatroskaFile.Read (bad);
		var second = MatroskaFile.Read (bad);

		Assert.IsFalse (MatroskaFile.TryRead (bad, out var file));
		Assert.IsNull (file);
		Assert.IsTrue (first == second);
		Assert.IsFalse (first != second);
		Assert.IsTrue (first.Equals ((object)second));
		Assert.IsFalse (first.Equals (null));
		Assert.AreEqual (first.GetHashCode (), second.GetHashCode ());
	}

	[TestMethod]
	public void Read_AudioOnlyFile_ExposesAudioAndNotVideo ()
	{
		var audio = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x258688, "Pretty"u8),
			Element (0x23E383, new byte[] { 0 }),
			Master (0xE1, Element (0xB5, Float32 (44100f)))));
		var parsed = MatroskaFile.Read (FileWith (Master (0x1654AE6B, audio))).File!;
		parsed.SetSourcePath ("clip.mka");
		IMediaFile media = parsed;

		Assert.AreEqual ("Pretty", media.AudioProperties!.Codec);
		Assert.IsNull (media.VideoProperties);
		Assert.IsNull (media.ImageProperties);
		Assert.AreEqual (MediaFormat.Matroska, parsed.Format);
		Assert.AreEqual ("clip.mka", parsed.SourcePath);
		Assert.AreSame (parsed.Tag, media.Tag);
		parsed.Dispose ();
	}

	[TestMethod]
	public void Read_KnownCodecIds_UseTheFriendlyName ()
	{
		Assert.AreEqual ("MP3", CodecOf ("A_MPEG/L3"));
		Assert.AreEqual ("Opus", CodecOf ("A_OPUS"));
		Assert.AreEqual ("AC-3", CodecOf ("A_AC3"));
		Assert.AreEqual ("DTS", CodecOf ("A_DTS"));
		Assert.AreEqual ("FLAC", CodecOf ("A_FLAC"));
		Assert.AreEqual ("PCM", CodecOf ("A_PCM/INT/BIG"));
		Assert.AreEqual ("AAC", CodecOf ("A_AAC/MPEG2/LC"));
		Assert.AreEqual ("VP9", CodecOf ("V_VP9", video: true));
		Assert.AreEqual ("AV1", CodecOf ("V_AV1", video: true));
		Assert.AreEqual ("HEVC", CodecOf ("V_MPEGH/ISO/HEVC", video: true));
		Assert.AreEqual ("H.264", CodecOf ("V_MPEG4/ISO/AVC", video: true));
		Assert.AreEqual ("Theora", CodecOf ("V_THEORA", video: true));
		Assert.AreEqual ("Matroska", CodecOf (null, video: true));
	}

	[TestMethod]
	public void Read_VfwFourCc_RejectsAHeaderThatIsNotAFourCc ()
	{
		Assert.AreEqual ("V_MS/VFW/FOURCC", FourCcCodec (new byte[10]));
		var shortSize = new byte[40];
		shortSize[0] = 10;
		Assert.AreEqual ("V_MS/VFW/FOURCC", FourCcCodec (shortSize));
		var binary = new byte[40];
		binary[0] = 40;
		binary[16] = 1;
		Assert.AreEqual ("V_MS/VFW/FOURCC", FourCcCodec (binary));
		var blank = new byte[40];
		blank[0] = 40;
		"    "u8.CopyTo (blank.AsSpan (16));
		Assert.AreEqual ("V_MS/VFW/FOURCC", FourCcCodec (blank));
	}

	[TestMethod]
	public void Read_UnknownSizeInsideACluster_StopsAtTheParent ()
	{
		var cluster = Concat (new byte[] { 0x1F, 0x43, 0xB6, 0x75, 0xFF, 0xEC, 0xFF });
		var file = Matroska (Master (0x18538067, Concat (Tags (Tag (30, Simple ("TITLE", "Before"))), cluster)));

		Assert.AreEqual ("Before", MatroskaFile.Read (file).File!.Tag.Title);
	}

	[TestMethod]
	public void Tag_Setters_RoundTripAndRender ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("GENRE", "Jazz"))))).File!.Tag;
		tag.Title = "Song";
		tag.Artist = "Ada";
		tag.Performers = new[] { "Ada", "Bea" };
		tag.Album = "Album";
		tag.AlbumArtists = new[] { "Ada" };
		tag.Year = "2010";
		tag.OriginalReleaseDate = "2009";
		tag.Comment = "note";
		tag.Genre = "Jazz";
		tag.Genres = new[] { "Jazz" };
		tag.Track = 2;
		tag.TotalTracks = 9;
		tag.Composers = new[] { "Ada" };
		tag.Copyright = "c";
		tag.Description = "d";
		tag.Lyrics = "la";
		tag.Subtitle = "sub";
		tag.BeatsPerMinute = 120;
		tag.Conductor = "con";
		tag.Publisher = "pub";
		tag.EncodedBy = "enc";
		tag.EncoderSettings = "set";
		tag.Grouping = "grp";
		tag.Remixer = "mix";
		tag.Isrc = "ISRC";
		tag.CatalogNumber = "CAT";
		tag.Barcode = "BAR";
		tag.InitialKey = "C";
		tag.Mood = "glad";
		tag.DateTagged = "2020";
		tag.TitleSort = "song";
		tag.ArtistSort = "ada";
		tag.AlbumSort = "album";
		tag.ReplayGainTrackGain = "-1";
		tag.ReplayGainTrackPeak = "1";
		tag.ReplayGainAlbumGain = "-2";
		tag.ReplayGainAlbumPeak = "0.9";
		tag.MusicBrainzTrackId = "t";
		tag.MusicBrainzReleaseId = "r";
		tag.MusicBrainzArtistId = "a";
		tag.MusicBrainzReleaseGroupId = "g";
		tag.MusicBrainzAlbumArtistId = "aa";
		tag.MusicBrainzWorkId = "w";
		tag.MusicBrainzDiscId = "d";
		tag.Pictures = new IPicture[] { new MatroskaPicture ("image/png", PictureType.FrontCover, "cover", new byte[] { 1 }) };
		tag.Pictures = null!;

		Assert.AreEqual ("Song", tag.Title);
		Assert.AreEqual (2u, tag.Track);
		Assert.AreEqual (0, tag.Pictures.Length);
		Assert.IsTrue (tag.Render ().Length > 0);

		tag.Comment = new string ('a', 20_000);
		Assert.IsTrue (tag.Render ().Length > 20_000);
		tag.Comment = new string ('b', 0x20_0000);
		Assert.IsTrue (tag.Render ().Length > 0x20_0000);

		var hidden = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("COMMENT", "quiet", isDefault: false))))).File!.Tag;
		Assert.AreEqual ("quiet", hidden.Comment);
		Assert.IsTrue (hidden.Render ().Length > 0);

		var albumOnly = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("TITLE", "Album"))))).File!.Tag;
		albumOnly.AlbumSort = "album sort";
		Assert.AreEqual ("album sort", albumOnly.AlbumSort);
		albumOnly.AlbumSort = null;
		Assert.IsNull (albumOnly.AlbumSort);

		tag.Title = "";
		tag.Performers = Array.Empty<string> ();
		tag.Clear ();
		Assert.AreEqual (0, tag.Render ().Length);
		Assert.IsNull (tag.Title);
	}

	[TestMethod]
	public void Read_RemainingEdges_AreCovered ()
	{
		Assert.IsFalse (EbmlReader.TryRead (new byte[4], 4, 4, out _));
		Assert.IsFalse (EbmlReader.TryRead (new byte[4], 0, 8, out _));
		Assert.IsFalse (EbmlReader.TryReadFloat (new byte[2], out _));
		Assert.IsTrue (EbmlWriter.Unsigned (0x88, 0x1_0000).Length > 2);

		var before = Element (0xEC, Array.Empty<byte> ());
		var withVoid = Matroska (Concat (before, Master (0x18538067, Tags (Tag (30, Simple ("TITLE", "After"))))));
		Assert.AreEqual ("After", MatroskaFile.Read (withVoid).File!.Tag.Title);

		var video = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Element (0x86, "V_VP8"u8),
			Element (0x23E383, new byte[] { 0x02, 0x62, 0x5A, 0x00 }),
			Master (0xE0, Concat (Element (0xB0, new byte[] { 1 }), Element (0xBA, new byte[] { 1 })))));
		var fps = ((IMediaFile)MatroskaFile.Read (FileWith (Master (0x1654AE6B, video))).File!).VideoProperties!.Value.FrameRate;
		Assert.AreEqual (25, fps, 0.1);

		var unknownInfo = Concat (new byte[] { 0x15, 0x49, 0xA9, 0x66, 0xFF }, Element (0xEC, Array.Empty<byte> ()));
		var titled = Matroska (Master (0x18538067, Concat (Tags (Tag (30, Simple ("TITLE", "Kept"))), unknownInfo)));
		Assert.AreEqual ("Kept", MatroskaFile.Read (titled).File!.Tag.Title);

		var broken = Concat (new byte[] { 0x12, 0x54, 0xC3, 0x67, 0xFF, 0x00 });
		var lost = Matroska (Master (0x18538067, broken));
		Assert.IsNull (MatroskaFile.Read (lost).File!.Tag.Title);

		var bare = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("TITLE", "Only"))))).File!.Tag;
		Assert.AreEqual (TagTypes.Matroska, bare.TagType);
		Assert.IsNull (bare.Year);
		Assert.IsNull (bare.Genre);
		Assert.AreEqual (0, bare.Genres.Length);
		Assert.AreEqual (0, bare.Composers.Length);
		Assert.IsNull (bare.Copyright);
		Assert.IsNull (bare.Lyrics);
		Assert.IsNull (bare.Conductor);
		Assert.IsNull (bare.Publisher);
		Assert.IsNull (bare.EncodedBy);
		Assert.IsNull (bare.EncoderSettings);
		Assert.IsNull (bare.Grouping);
		Assert.IsNull (bare.Remixer);
		Assert.IsNull (bare.Isrc);
		Assert.IsNull (bare.CatalogNumber);
		Assert.IsNull (bare.Barcode);
		Assert.IsNull (bare.InitialKey);
		Assert.IsNull (bare.Mood);
		Assert.IsNull (bare.DateTagged);
		Assert.IsNull (bare.ReplayGainTrackGain);
		Assert.IsNull (bare.ReplayGainTrackPeak);
		Assert.IsNull (bare.ReplayGainAlbumGain);
		Assert.IsNull (bare.ReplayGainAlbumPeak);
		Assert.IsNull (bare.MusicBrainzReleaseId);
		Assert.IsNull (bare.MusicBrainzArtistId);
		Assert.IsNull (bare.MusicBrainzReleaseGroupId);
		Assert.IsNull (bare.MusicBrainzAlbumArtistId);
		Assert.IsNull (bare.MusicBrainzWorkId);
		Assert.IsNull (bare.MusicBrainzDiscId);
		Assert.IsNull (bare.Album);
		Assert.AreEqual (0, bare.AlbumArtists.Length);
		Assert.IsNull (bare.AlbumSort);
		Assert.IsNull (bare.BeatsPerMinute);
		bare.AlbumSort = "up";
		Assert.AreEqual ("up", bare.AlbumSort);
		bare.TitleSort = "only";
		Assert.AreEqual ("only", bare.TitleSort);
		Assert.IsTrue (bare.Render ().Length > 0);

		var nested = MatroskaFile.Read (FileWith (Tags (Tag (30, SimpleSorted ("TITLE", "Song", "song"))))).File!.Tag;
		Assert.IsTrue (nested.Render ().Length > 0);
	}

	static void AssertFailure (byte[] data, string fragment)
	{
		var result = MatroskaFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, fragment);
	}

	static byte[] EbmlHeader () => Master (0x1A45DFA3, Element (0x4282, "matroska"u8));

	static byte[] EbmlWithoutDocType () => Master (0x1A45DFA3, Element (0xEC, Array.Empty<byte> ()));

	static string CodecOf (string? codecId, bool video = false)
	{
		var body = new List<byte[]> { Element (0x83, new byte[] { (byte)(video ? 1 : 2) }) };
		if (codecId is not null)
			body.Add (Element (0x86, Encoding.ASCII.GetBytes (codecId)));
		body.Add (video
			? Master (0xE0, Concat (Element (0xB0, new byte[] { 1 }), Element (0xBA, new byte[] { 1 })))
			: Master (0xE1, Element (0xB5, Float32 (44100f))));
		var parsed = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Master (0xAE, Concat (body.ToArray ()))))).File!;
		IMediaFile media = parsed;
		return video ? media.VideoProperties!.Value.Codec! : media.AudioProperties!.Codec!;
	}

	static string FourCcCodec (byte[] header)
	{
		var video = Master (0xAE, Concat (
			Element (0x83, new byte[] { 1 }),
			Element (0x86, "V_MS/VFW/FOURCC"u8),
			Element (0x63A2, header),
			Master (0xE0, Concat (Element (0xB0, new byte[] { 1 }), Element (0xBA, new byte[] { 1 })))));
		IMediaFile media = MatroskaFile.Read (FileWith (Master (0x1654AE6B, video))).File!;
		return media.VideoProperties!.Value.Codec!;
	}

	static byte[] Tags (params byte[][] tags) => Master (0x1254C367, Concat (tags));

	static byte[] FileWith (params byte[][] segmentChildren) =>
		Matroska (Master (0x18538067, Concat (segmentChildren)));

	static byte[] Tag (ulong level, params byte[][] simpleTags) =>
		ChapterTag (level, null, simpleTags);

	static byte[] ChapterTag (ulong level, byte[]? chapterUid, params byte[][] simpleTags)
	{
		var targets = Master (0x63C0, Concat (Element (0x68CA, new[] { (byte)level }), chapterUid ?? Array.Empty<byte> ()));
		return Master (0x7373, Concat (targets, Concat (simpleTags)));
	}

	static byte[] Chapter (ulong uid) => Element (0x63C4, new[] { (byte)uid });

	static byte[] Track (ulong uid) => Element (0x63C5, new[] { (byte)uid });

	static byte[] Edition (ulong uid) => Element (0x63C9, new[] { (byte)uid });

	static byte[] SimpleSorted (string name, string text, string sort)
	{
		var sortTag = Master (0x67C8, Concat (
			Element (0x45A3, "SORT_WITH"u8),
			Element (0x4487, Encoding.UTF8.GetBytes (sort))));
		return Master (0x67C8, Concat (
			Element (0x45A3, Encoding.UTF8.GetBytes (name)),
			Element (0x4487, Encoding.UTF8.GetBytes (text)),
			sortTag));
	}

	static byte[] Simple (string name, string text, bool isDefault = true)
	{
		var body = Concat (Element (0x45A3, Encoding.UTF8.GetBytes (name)), Element (0x4487, Encoding.UTF8.GetBytes (text)));
		if (!isDefault)
			body = Concat (body, Element (0x4484, new byte[] { 0 }));
		return Master (0x67C8, body);
	}

	static byte[] Matroska (byte[] segment, string docType = "matroska")
	{
		var ebml = Master (0x1A45DFA3, Element (0x4282, Encoding.ASCII.GetBytes (docType)));
		return Concat (ebml, segment);
	}

	static byte[] Master (ulong id, params byte[][] children) => Element (id, Concat (children));

	static byte[] Element (ulong id, ReadOnlySpan<byte> payload)
	{
		var size = payload.Length < 0x7F
			? new[] { (byte)(0x80 | payload.Length) }
			: new[] { (byte)(0x40 | (payload.Length >> 8)), (byte)payload.Length };
		var idBytes = Id (id);
		var result = new byte[idBytes.Length + size.Length + payload.Length];
		idBytes.CopyTo (result, 0);
		size.CopyTo (result, idBytes.Length);
		payload.CopyTo (result.AsSpan (idBytes.Length + size.Length));
		return result;
	}

	static byte[] Id (ulong id)
	{
		var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)id;
			id >>= 8;
		}

		return bytes;
	}

	static byte[] Float32 (float value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteSingleBigEndian (bytes, value);
		return bytes;
	}

	static byte[] Concat (params byte[][] parts)
	{
		var length = 0;
		foreach (var part in parts)
			length += part.Length;
		var result = new byte[length];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}

		return result;
	}
}
