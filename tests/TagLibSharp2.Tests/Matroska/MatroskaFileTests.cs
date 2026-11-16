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
