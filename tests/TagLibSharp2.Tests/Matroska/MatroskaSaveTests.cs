// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	[TestMethod]
	public void Read_SecondUnknownSegment_StaysOutOfTheFirst ()
	{
		var first = Unknown (0x18538067, Concat (
			Tags (Tag (50, Simple ("TITLE", "Movie"))),
			Master (0x1F43B675, new byte[] { 0x11 })));
		var second = Unknown (0x18538067, Concat (
			Tags (Tag (30, Simple ("TITLE", "Later"))),
			Master (0x1F43B675, new byte[] { 0x22 })));
		var parsed = MatroskaFile.Read (Matroska (Concat (first, second)));

		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.AreEqual ("Movie", parsed.File!.Tag.Title);
	}

	[TestMethod]
	public void Save_UnknownSizeSegment_StopsBeforeTheNextSegment ()
	{
		var marker = new byte[] { 0x11, 0x11, 0x11, 0x11 };
		var first = Unknown (0x18538067, Concat (
			Tags (Tag (50, Simple ("TITLE", "First"))),
			Master (0x1F43B675, marker)));
		var second = Master (0x18538067, Concat (
			Tags (Tag (50, Simple ("TITLE", "Second"))),
			Master (0x1F43B675, new byte[] { 0x22, 0x22, 0x22, 0x22 })));
		var original = Matroska (Concat (first, second));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Artist = "Added";

		var fs = new MockFileSystem ();
		fs.AddFile ("/two.mkv", original);
		var saved = parsed.SaveToFile ("/two.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/two.mkv");
		Assert.IsTrue (bytes.AsSpan (bytes.Length - second.Length).SequenceEqual (second));
		var head = bytes.AsSpan (0, bytes.Length - second.Length).ToArray ();
		var again = MatroskaFile.Read (head);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("First", again.File!.Tag.Title);
		Assert.AreEqual ("Added", again.File.Tag.Artist);
		Assert.AreEqual (1, CountOf (head, marker));
	}

	[TestMethod]
	public void Save_ChapterTitle_StaysOffTheFile ()
	{
		var original = FileWith (
			Tags (
				Tag (50, Simple ("TITLE", "Movie")),
				ChapterTag (30, Chapter (7), Simple ("TITLE", "Chapter"))),
			Master (0x1F43B675, new byte[] { 0x33 }));
		var parsed = MatroskaFile.Read (original);
		Assert.AreEqual ("Movie", parsed.File!.Tag.Title);

		parsed.File.Tag.Artist = "Someone";
		var fs = new MockFileSystem ();
		fs.AddFile ("/chap.mkv", original);
		var saved = parsed.File.SaveToFile ("/chap.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/chap.mkv");
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual ("Movie", again.Tag.Title);
		Assert.AreEqual ("Someone", again.Tag.Artist);
		Assert.IsNull (again.Tag.Album);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x63C4, new byte[] { 7 })) >= 0);
	}

	[TestMethod]
	public void Save_FileLevelLanguage_IsWrittenBack ()
	{
		var title = Master (0x67C8, Concat (
			Element (0x45A3, "TITLE"u8),
			Element (0x447A, "eng"u8),
			Element (0x4487, "Movie"u8)));
		var original = FileWith (
			Tags (Master (0x7373, Concat (Master (0x63C0, Element (0x68CA, new byte[] { 50 })), title))),
			Master (0x1F43B675, new byte[] { 0x44 }));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Artist = "Someone";

		var fs = new MockFileSystem ();
		fs.AddFile ("/lang.mkv", original);
		var saved = parsed.SaveToFile ("/lang.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/lang.mkv");
		Assert.AreEqual ("Movie", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x447A, "eng"u8)) >= 0);
	}

	[TestMethod]
	public void Save_RewritesCommonFields_AndLeavesTheClusterBytes ()
	{
		var marker = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x11, 0x22 };
		var scale = Element (0x2AD7B1, new byte[] { 0x0F, 0x42, 0x40 });
		var info = Master (0x1549A966, Concat (scale, Element (0x4489, Float32 (1500f))));
		var audio = Master (0x1654AE6B, Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x86, "A_VORBIS"u8),
			Master (0xE1, Element (0xB5, Float32 (44100f))))));
		var pictures = Master (0x1941A469, Master (0x61A7, Concat (
			Element (0x4660, "image/png"u8),
			Element (0x466E, "cover.png"u8),
			Element (0x465C, new byte[] { 9, 8, 7, 6, 5 }))));
		var cluster = Master (0x1F43B675, marker);
		var original = FileWith (
			info,
			audio,
			Tags (
				Tag (50, Simple ("TITLE", "Old Album"), Simple ("ARTIST", "Old Album Artist"), Simple ("TOTAL_PARTS", "8")),
				Tag (30,
					Simple ("TITLE", "Old Title"),
					Simple ("ARTIST", "Old Artist"),
					Simple ("DATE_RELEASED", "1991"),
					Simple ("COMMENT", "Old"),
					Simple ("GENRE", "Old Genre"),
					Simple ("PART_NUMBER", "1"),
					Simple ("COMPOSER", "Old Composer"),
					Simple ("COPYRIGHT", "Old Copy"),
					Simple ("DESCRIPTION", "Old Desc"),
					Simple ("LYRICS", "Old Lyrics"),
					Simple ("CONDUCTOR", "Old Conductor"),
					Simple ("PUBLISHER", "Old Label"))),
			pictures,
			cluster);
		var clusterAt = IndexOf (original, marker);
		Assert.IsTrue (clusterAt > 0);

		var parsed = MatroskaFile.Read (original);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		var tag = parsed.File!.Tag;
		tag.Title = "New Title";
		tag.Artist = "New Artist";
		tag.Performers = new[] { "New Artist", "Guest" };
		tag.Album = "New Album";
		tag.AlbumArtists = new[] { "New Album Artist" };
		tag.Year = "2024";
		tag.Comment = "New Comment";
		tag.Genre = "Jazz";
		tag.Track = 4;
		tag.TotalTracks = 12;
		tag.Composers = new[] { "Ada" };
		tag.Copyright = "New Copy";
		tag.Description = "New Desc";
		tag.Lyrics = "New Lyrics";
		tag.Conductor = "New Conductor";
		tag.Publisher = "New Label";

		var fs = new MockFileSystem ();
		fs.AddFile ("/song.mkv", original);
		parsed.File.SetSourcePath ("/song.mkv", fs);
		var saved = parsed.File.SaveToFile (fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/song.mkv");
		Assert.AreEqual (clusterAt, IndexOf (bytes, marker));
		Assert.AreEqual (1, CountOf (bytes, marker));
		var again = MatroskaFile.Read (bytes);
		Assert.IsTrue (again.IsSuccess, again.Error);
		var round = again.File!.Tag;
		Assert.AreEqual ("New Title", round.Title);
		Assert.AreEqual ("New Artist", round.Artist);
		Assert.AreEqual (2, round.Performers.Length);
		Assert.AreEqual ("Guest", round.Performers[1]);
		Assert.AreEqual ("New Album", round.Album);
		Assert.AreEqual ("New Album Artist", round.AlbumArtists[0]);
		Assert.AreEqual ("2024", round.Year);
		Assert.AreEqual ("New Comment", round.Comment);
		Assert.AreEqual ("Jazz", round.Genre);
		Assert.AreEqual (4u, round.Track);
		Assert.AreEqual (12u, round.TotalTracks);
		Assert.AreEqual ("Ada", round.Composers[0]);
		Assert.AreEqual ("New Copy", round.Copyright);
		Assert.AreEqual ("New Desc", round.Description);
		Assert.AreEqual ("New Lyrics", round.Lyrics);
		Assert.AreEqual ("New Conductor", round.Conductor);
		Assert.AreEqual ("New Label", round.Publisher);
		Assert.AreEqual (TimeSpan.FromSeconds (1.5), again.File.Duration);
		IMediaFile media = again.File;
		Assert.AreEqual (44100, media.AudioProperties!.SampleRate);
		Assert.AreEqual (1, round.Pictures.Length);
		Assert.IsTrue (round.Pictures[0].PictureData.Span.SequenceEqual (new byte[] { 9, 8, 7, 6, 5 }));
	}

	[TestMethod]
	public void Save_InsertsTagsWhenTheFileHasNone ()
	{
		var marker = new byte[] { 0xC0, 0xFF, 0xEE };
		var info = Master (0x1549A966, Element (0x7BA9, "Segment Title"u8));
		var original = FileWith (info, Master (0x1F43B675, marker));
		var parsed = MatroskaFile.Read (original).File!;

		// With no existing tag block the item level is 50, so TITLE there is the title.
		parsed.Tag.Title = "Added";
		parsed.Tag.Artist = "Someone";

		var fs = new MockFileSystem ();
		fs.AddFile ("/new.mkv", original);
		var saved = parsed.SaveToFile ("/new.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/new.mkv");
		Assert.AreEqual (1, CountOf (bytes, marker));
		var tag = MatroskaFile.Read (bytes).File!.Tag;
		Assert.AreEqual ("Added", tag.Title);
		Assert.AreEqual ("Someone", tag.Artist);
		Assert.IsTrue (bytes.AsSpan ().IndexOf ("Segment Title"u8) >= 0);
	}

	[TestMethod]
	public void Save_ClearingAValue_RemovesItAndKeepsTheOthers ()
	{
		var original = FileWith (Tags (Tag (30,
			Simple ("TITLE", "Keep"),
			Simple ("ARTIST", "Drop"),
			Simple ("GENRE", "Drop Genre"),
			Simple ("COMMENT", "Drop Comment"))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Artist = null;
		parsed.Tag.Genre = null;
		parsed.Tag.Comment = "";

		var fs = new MockFileSystem ();
		fs.AddFile ("/cleared.mkv", original);
		var saved = parsed.SaveToFile ("/cleared.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var tag = MatroskaFile.Read (fs.ReadAllBytes ("/cleared.mkv")).File!.Tag;
		Assert.AreEqual ("Keep", tag.Title);
		Assert.IsNull (tag.Artist);
		Assert.AreEqual (0, tag.Performers.Length);
		Assert.IsNull (tag.Genre);
		Assert.IsNull (tag.Comment);
	}

	[TestMethod]
	public void Save_ClearingTheOnlyTitle_LeavesNoTitle ()
	{
		var original = FileWith (Tags (Tag (30, Simple ("TITLE", "Gone"))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = null;

		var fs = new MockFileSystem ();
		fs.AddFile ("/only.mkv", original);
		var saved = parsed.SaveToFile ("/only.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.IsNull (MatroskaFile.Read (fs.ReadAllBytes ("/only.mkv")).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_WebM_RoundTripsTheDocTypeAndTitle ()
	{
		var original = Matroska (Master (0x18538067, Tags (Tag (30, Simple ("TITLE", "Clip")))), "webm");
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Web Clip";
		parsed.Tag.Artist = "Camera";

		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.webm", original);
		var saved = parsed.SaveToFile ("/clip.webm", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/clip.webm");
		Assert.IsTrue (bytes.AsSpan ().IndexOf ("webm"u8) >= 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf ("matroska"u8) < 0);
		var tag = MatroskaFile.Read (bytes).File!.Tag;
		Assert.AreEqual ("Web Clip", tag.Title);
		Assert.AreEqual ("Camera", tag.Artist);
	}

	[TestMethod]
	public void Save_TwoTagsElements_DoNotKeepTheReplacedValues ()
	{
		var original = FileWith (
			Tags (Tag (30, Simple ("TITLE", "First"))),
			Tags (Tag (30, Simple ("ARTIST", "Second"))));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual ("Second", parsed.Tag.Artist);
		parsed.Tag.Title = "Third";

		var fs = new MockFileSystem ();
		fs.AddFile ("/two.mkv", original);
		Assert.IsTrue (parsed.SaveToFile ("/two.mkv", original, fs).IsSuccess);

		var tag = MatroskaFile.Read (fs.ReadAllBytes ("/two.mkv")).File!.Tag;
		Assert.AreEqual ("Third", tag.Title);
		Assert.AreEqual ("Second", tag.Artist);
	}

	[TestMethod]
	public void Save_NoSourcePath_DoesNotWrite ()
	{
		var original = FileWith (Tags (Tag (30, Simple ("TITLE", "Stay"))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Changed";
		var fs = new MockFileSystem ();
		fs.AddFile ("/stay.mkv", original);

		var saved = parsed.SaveToFile (fs);

		Assert.IsFalse (saved.IsSuccess);
		Assert.IsTrue (original.AsSpan ().SequenceEqual (fs.ReadAllBytes ("/stay.mkv")));
	}

	[TestMethod]
	public async Task SaveAsync_RoundTripsBackToTheSourcePath ()
	{
		var original = FileWith (Tags (Tag (30, Simple ("TITLE", "Before"), Simple ("ARTIST", "A"))));
		var fs = new MockFileSystem ();
		fs.AddFile ("/async.mkv", original);
		var opened = await MatroskaFile.ReadFromFileAsync ("/async.mkv", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual ("/async.mkv", opened.File!.SourcePath);
		opened.File.Tag.Title = "After";

		var saved = await opened.File.SaveToFileAsync (fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("After", MatroskaFile.Read (fs.ReadAllBytes ("/async.mkv")).File!.Tag.Title);
		Assert.AreEqual ("A", MatroskaFile.Read (fs.ReadAllBytes ("/async.mkv")).File!.Tag.Artist);
	}

	[TestMethod]
	public void Save_ToAnotherPath_LeavesTheSourceUntouched ()
	{
		var original = FileWith (Tags (Tag (30, Simple ("TITLE", "Source"))));
		var fs = new MockFileSystem ();
		fs.AddFile ("/src.mkv", original);
		var parsed = MatroskaFile.ReadFromFile ("/src.mkv", fs).File!;
		parsed.Tag.Title = "Copy";

		var saved = parsed.SaveToFile ("/dst.mkv", fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.IsTrue (original.AsSpan ().SequenceEqual (fs.ReadAllBytes ("/src.mkv")));
		Assert.AreEqual ("Copy", MatroskaFile.Read (fs.ReadAllBytes ("/dst.mkv")).File!.Tag.Title);
		Assert.AreEqual ("Source", MatroskaFile.Read (fs.ReadAllBytes ("/src.mkv")).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_UnknownSizeThatCannotBeBounded_DoesNotWrite ()
	{
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var unbounded = Concat (new byte[] { 0x1F, 0x43, 0xB6, 0x75, 0xFF }, new byte[] { 0x00, 0x11, 0x22 });
		var original = FileWith (tags, unbounded);
		Assert.AreEqual ("Old", MatroskaFile.Read (original).File!.Tag.Title);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "New";
		var fs = new MockFileSystem ();
		fs.AddFile ("/bad.mkv", original);

		var saved = parsed.SaveToFile ("/bad.mkv", original, fs);

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "bounded");
		Assert.IsTrue (original.AsSpan ().SequenceEqual (fs.ReadAllBytes ("/bad.mkv")));
	}

	[TestMethod]
	public void Save_MediaFileRead_SetsSourcePath ()
	{
		var original = FileWith (Tags (Tag (30, Simple ("TITLE", "Media"))));
		var fs = new MockFileSystem ();
		fs.AddFile ("/media.mkv", original);

		var opened = MediaFile.Read ("/media.mkv", fs);

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = opened.GetFileAs<MatroskaFile> ();
		Assert.IsNotNull (file);
		Assert.AreEqual ("/media.mkv", file!.SourcePath);
		file.Tag.Album = "From MediaFile";
		var saved = file.SaveToFile (fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("From MediaFile", MatroskaFile.Read (fs.ReadAllBytes ("/media.mkv")).File!.Tag.Album);
		Assert.AreEqual ("Media", MatroskaFile.Read (fs.ReadAllBytes ("/media.mkv")).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_SeekHead_KeepsATagsPointerThatStillFits ()
	{
		var marker = new byte[] { 0xAB, 0xCD };
		var cluster = Master (0x1F43B675, marker);
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var probe = Concat (SeekEntry (MatroskaIds.Tags, 0), info, tags, cluster);
		var tagsAt = probe.Length - tags.Length - cluster.Length;
		Assert.IsTrue (tagsAt < 0x80);
		var seek = Master (0x114D9B74, Concat (
			SeekEntry (MatroskaIds.Info, (ulong)SeekEntry (MatroskaIds.Tags, 0).Length),
			SeekEntry (MatroskaIds.Tags, (ulong)tagsAt)));
		// Recompute once the real SeekHead length is known. Both seeks use a one-byte position.
		var body = Concat (seek, info, tags, cluster);
		tagsAt = seek.Length + info.Length;
		var infoAt = seek.Length;
		Assert.IsTrue (tagsAt < 0x80);
		seek = Master (0x114D9B74, Concat (
			SeekEntry (MatroskaIds.Info, (ulong)infoAt),
			SeekEntry (MatroskaIds.Tags, (ulong)tagsAt)));
		Assert.AreEqual (body.Length - info.Length - tags.Length - cluster.Length, seek.Length);
		var original = FileWith (seek, info, tags, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "New";

		var fs = new MockFileSystem ();
		fs.AddFile ("/seek.mkv", original);
		var saved = parsed.SaveToFile ("/seek.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/seek.mkv");
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.AreEqual ("New", MatroskaFile.Read (bytes).File!.Tag.Title);
		var seeks = ReadSeeks (bytes);
		var tagsSeek = seeks.Find (entry => entry.Id == MatroskaIds.Tags);
		Assert.AreNotEqual (0UL, tagsSeek.Id);
		Assert.IsTrue (IsElementAt (bytes, tagsSeek.Position, MatroskaIds.Tags));
		var infoSeek = seeks.Find (entry => entry.Id == MatroskaIds.Info);
		Assert.IsTrue (IsElementAt (bytes, infoSeek.Position, MatroskaIds.Info));
	}

	[TestMethod]
	public void Save_SeekHead_DropsATagsPointerThatWouldPointAtTheWrongOffset ()
	{
		// The new Tags element is appended past offset 255. A one-byte SeekPosition cannot
		// hold that offset, and the Seek element has no spare byte, so the entry is removed.
		var marker = new byte[] { 0x11, 0x22, 0x33, 0x44 };
		var cluster = Master (0x1F43B675, Concat (marker, new byte[240]));
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var info = Master (0x1549A966, Element (0x7BA9, "I"u8));
		var seekProbe = Master (0x114D9B74, SeekEntry (MatroskaIds.Tags, 1));
		var tagsAt = seekProbe.Length + info.Length;
		Assert.IsTrue (tagsAt < 0x80);
		var seek = Master (0x114D9B74, SeekEntry (MatroskaIds.Tags, (ulong)tagsAt));
		var original = FileWith (seek, info, tags, cluster);
		Assert.IsTrue (seek.Length + info.Length + tags.Length + cluster.Length > 255);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('N', 80);

		var fs = new MockFileSystem ();
		fs.AddFile ("/drop.mkv", original);
		var saved = parsed.SaveToFile ("/drop.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/drop.mkv");
		Assert.AreEqual (new string ('N', 80), MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.AreEqual (1, CountOf (bytes, marker));
		foreach (var entry in ReadSeeks (bytes)) {
			if (entry.Id != MatroskaIds.Tags)
				continue;
			Assert.IsTrue (IsElementAt (bytes, entry.Position, MatroskaIds.Tags));
		}

		Assert.IsFalse (ReadSeeks (bytes).Exists (entry => entry.Id == MatroskaIds.Tags && entry.Position == (ulong)tagsAt));
	}

	[TestMethod]
	public void Save_ShiftedCluster_UpdatesCueClusterPositionAndKeepsThePayload ()
	{
		var marker = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
		var clusterPayload = Element (0xEC, marker);
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		var cues = CueElement (0);
		ulong clusterPos = 0;
		for (var i = 0; i < 4; i++) {
			clusterPos = (ulong)(info.Length + cues.Length);
			cues = CueElement (clusterPos);
		}

		Assert.AreEqual (clusterPos, (ulong)(info.Length + cues.Length));
		Assert.IsTrue (clusterPos < 0x80);
		var cluster = Concat (new byte[] { 0x1F, 0x43, 0xB6, 0x75, 0xFF }, clusterPayload);
		var original = FileWith (info, cues, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Shifted";

		var fs = new MockFileSystem ();
		fs.AddFile ("/live.mkv", original);
		var saved = parsed.SaveToFile ("/live.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/live.mkv");
		Assert.AreEqual ("Shifted", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.IsTrue (TryReadCueClusterPosition (bytes, out var position, out var crcOk));
		Assert.IsTrue (crcOk);
		Assert.IsTrue (IsElementAt (bytes, position, MatroskaIds.Cluster));
		Assert.AreNotEqual ((ulong)clusterPos, position);
	}

	[TestMethod]
	public void Render_UnreadableContainer_ReturnsEmpty ()
	{
		var file = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		Assert.AreEqual (0, file.Render (new byte[] { 1, 2, 3 }).Length);
	}

	[TestMethod]
	public void Save_WithoutASourcePath_DoesNotWrite ()
	{
		var file = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		var saved = file.SaveToFile ();
		Assert.IsFalse (saved.IsSuccess);
		Assert.AreEqual ("No source path available. File was not read from disk.", saved.Error);
		saved = file.SaveToFile ("/song.mkv");
		Assert.AreEqual ("No source path available. File was not read from disk.", saved.Error);
	}

	[TestMethod]
	public async Task SaveAsync_WithoutASourcePath_DoesNotWrite ()
	{
		var file = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		var saved = await file.SaveToFileAsync ();
		Assert.AreEqual ("No source path available. File was not read from disk.", saved.Error);
		saved = await file.SaveToFileAsync ("/song.mkv");
		Assert.AreEqual ("No source path available. File was not read from disk.", saved.Error);
	}

	[TestMethod]
	public void Save_MissingSourceFile_DoesNotWrite ()
	{
		var file = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		file.SetSourcePath ("/missing.mkv", new MockFileSystem ());
		var saved = file.SaveToFile ();
		Assert.IsFalse (saved.IsSuccess);
		Assert.IsTrue (saved.Error!.Contains ("Failed to re-read source file", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task SaveAsync_MissingSourceFile_DoesNotWrite ()
	{
		var file = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		file.SetSourcePath ("/missing.mkv", new MockFileSystem ());
		var saved = await file.SaveToFileAsync ();
		Assert.IsFalse (saved.IsSuccess);
		Assert.IsTrue (saved.Error!.Contains ("Failed to re-read source file", StringComparison.Ordinal));
	}

	[TestMethod]
	public void Save_FileLevelLanguageAndBinary_AreWrittenBack ()
	{
		var targets = Master (0x63C0, Concat (
			Element (0x68CA, new byte[] { 50 }),
			Element (0x63CA, "MOVIE"u8)));
		var title = Master (0x67C8, Concat (
			Element (0x45A3, "TITLE"u8),
			Element (0x447B, "en-US"u8),
			Element (0x4485, new byte[] { 1, 2, 3 }),
			Element (0x4487, "Movie"u8)));
		var original = FileWith (Tags (Master (0x7373, Concat (targets, title))), Master (0x1F43B675, new byte[] { 9 }));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual ("Movie", parsed.Tag.Title);
		parsed.Tag.Artist = "Someone";

		var fs = new MockFileSystem ();
		fs.AddFile ("/lang.mkv", original);
		var saved = parsed.SaveToFile ("/lang.mkv", original, fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/lang.mkv");
		Assert.AreEqual ("Movie", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x447B, "en-US"u8)) >= 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x4485, new byte[] { 1, 2, 3 })) >= 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x63CA, "MOVIE"u8)) >= 0);
	}

	[TestMethod]
	public void Read_UnknownClusterEndingAtAnInfoElement_KeepsTheTitle ()
	{
		var cluster = Unknown (0x1F43B675, Element (0xEC, new byte[] { 1, 2, 3, 4 }));
		var info = Master (0x1549A966, Element (0x7BA9, "After"u8));
		var parsed = MatroskaFile.Read (Matroska (Unknown (0x18538067, Concat (cluster, info))));
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.AreEqual ("After", parsed.File!.Tag.Title);
	}

	[TestMethod]
	public void Read_UnknownClusterThatDoesNotParse_StillOpens ()
	{
		var cluster = Unknown (0x1F43B675, new byte[] { 0xFF });
		var parsed = MatroskaFile.Read (Matroska (Unknown (0x18538067, cluster)));
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.IsNull (parsed.File!.Tag.Title);
	}

	[TestMethod]
	public void Read_UnknownClusterOfOnlyAVoid_RunsToTheEnd ()
	{
		var cluster = Unknown (0x1F43B675, Element (0xEC, new byte[] { 9 }));
		var parsed = MatroskaFile.Read (Matroska (Unknown (0x18538067, cluster)));
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
	}

	[TestMethod]
	public void Save_RejectsAContainerThatCannotBeRewritten ()
	{
		var tag = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		var fs = new MockFileSystem ();
		AssertFailure (tag, fs, "/a.mkv", new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, "truncated");
		AssertFailure (tag, fs, "/b.mkv", Matroska (Array.Empty<byte> (), "avi"), "DocType is avi");
		AssertFailure (tag, fs, "/c.mkv", Master (0x1A45DFA3, Element (0x4286, new byte[] { 1 })), "DocType is missing");
		AssertFailure (tag, fs, "/d.mkv", Concat (Master (0x1A45DFA3, Element (0x4282, "matroska"u8)), new byte[] { 0xEC, 0xFF }), "unknown before Segment");
		AssertFailure (tag, fs, "/e.mkv", Master (0x1A45DFA3, Element (0x4282, "matroska"u8)), "no Segment");
	}

	[TestMethod]
	public void Save_LeadingPaddingInsideTheSegment_IsKept ()
	{
		var cluster = Master (0x1F43B675, new byte[] { 0xAB, 0xCD });
		var original = Matroska (Master (0x18538067, Concat (new byte[] { 0x00, 0x00 }, cluster)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Padded";
		var fs = new MockFileSystem ();
		fs.AddFile ("/pad.mkv", original);
		var saved = parsed.SaveToFile ("/pad.mkv", original, fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/pad.mkv");
		Assert.AreEqual ("Padded", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.AreEqual (1, CountOf (bytes, new byte[] { 0xAB, 0xCD }));
	}

	[TestMethod]
	public void Save_GarbageAfterARealElement_DoesNotWrite ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "T"u8));
		var original = Matroska (Master (0x18538067, Concat (info, new byte[] { 0x01 })));
		var parsed = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		var fs = new MockFileSystem ();
		fs.AddFile ("/bad.mkv", new byte[] { 1 });
		var saved = parsed.SaveToFile ("/bad.mkv", original, fs);
		Assert.IsFalse (saved.IsSuccess);
		Assert.IsTrue (saved.Error!.Contains ("cannot be bounded", StringComparison.Ordinal));
		Assert.AreEqual (1, fs.ReadAllBytes ("/bad.mkv").Length);
	}

	static void AssertFailure (MatroskaFile file, MockFileSystem fs, string path, byte[] original, string error)
	{
		fs.AddFile (path, new byte[] { 1 });
		var saved = file.SaveToFile (path, original, fs);
		Assert.IsFalse (saved.IsSuccess, path);
		Assert.IsTrue (saved.Error!.Contains (error, StringComparison.Ordinal), saved.Error);
		Assert.AreEqual (1, fs.ReadAllBytes (path).Length);
	}

	static byte[] Unknown (ulong id, byte[] payload)
	{
		var idBytes = Id (id);
		var result = new byte[idBytes.Length + 1 + payload.Length];
		idBytes.CopyTo (result, 0);
		result[idBytes.Length] = 0xFF;
		payload.CopyTo (result, idBytes.Length + 1);
		return result;
	}

	static byte[] SeekEntry (ulong id, ulong position) =>
		Master (0x4DBB, Concat (Element (0x53AB, Id (id)), Element (0x53AC, UIntBytes (position))));

	static byte[] UIntBytes (ulong value)
	{
		if (value <= 0xFF)
			return new[] { (byte)value };
		if (value <= 0xFFFF)
			return new[] { (byte)(value >> 8), (byte)value };
		return new[] { (byte)(value >> 16), (byte)(value >> 8), (byte)value };
	}

	static byte[] CueElement (ulong clusterPos)
	{
		var inner = Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (
				Element (0xF7, new byte[] { 1 }),
				Element (0xF1, UIntBytes (clusterPos))))));
		return WithCrc (MatroskaIds.Cues, inner);
	}

	static byte[] WithCrc (ulong id, byte[] payload)
	{
		var crc = new BinaryData (payload).ComputeCrc32 ();
		var crcBytes = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (crcBytes, crc);
		return Master (id, Concat (Element (0xBF, crcBytes), payload));
	}

	readonly struct SeekRef
	{
		public SeekRef (ulong id, ulong position)
		{
			Id = id;
			Position = position;
		}

		public ulong Id { get; }

		public ulong Position { get; }
	}

	static List<SeekRef> ReadSeeks (byte[] file)
	{
		var found = new List<SeekRef> ();
		if (!TrySegmentBody (file, out var body, out var bodyAt))
			return found;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.SeekHead)
				ReadSeekHead (body.AsSpan (element.DataOffset, (int)element.DataLength), found);
			offset = element.End;
		}

		return found;
	}

	static void ReadSeekHead (ReadOnlySpan<byte> body, List<SeekRef> found)
	{
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == 0x4DBB)
				found.Add (ReadSeek (body.Slice (element.DataOffset, (int)element.DataLength)));
			offset = element.End;
		}
	}

	static SeekRef ReadSeek (ReadOnlySpan<byte> body)
	{
		ulong id = 0;
		ulong position = 0;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == 0x53AB)
				id = ReadId (payload);
			else if (element.Id == 0x53AC)
				position = EbmlReader.ReadUInt (payload);
			offset = element.End;
		}

		return new SeekRef (id, position);
	}

	static ulong ReadId (ReadOnlySpan<byte> payload)
	{
		ulong id = 0;
		for (var i = 0; i < payload.Length; i++)
			id = (id << 8) | payload[i];
		return id;
	}

	static bool IsElementAt (byte[] file, ulong segmentPosition, ulong id)
	{
		if (!TrySegmentBody (file, out _, out var bodyAt))
			return false;
		var at = bodyAt + (int)segmentPosition;
		var idBytes = Id (id);
		return at >= 0 && at + idBytes.Length <= file.Length && file.AsSpan (at, idBytes.Length).SequenceEqual (idBytes);
	}

	static bool TryReadCueClusterPosition (byte[] file, out ulong position, out bool crcOk)
	{
		position = 0;
		crcOk = false;
		if (!TrySegmentBody (file, out var body, out _))
			return false;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.Cues) {
				var cues = body.AsSpan (element.DataOffset, (int)element.DataLength);
				crcOk = CrcMatches (cues);
				return TryFindCuePosition (cues, ref position);
			}

			offset = element.End;
		}

		return false;
	}

	static bool CrcMatches (ReadOnlySpan<byte> master)
	{
		if (!EbmlReader.TryRead (master, 0, master.Length, out var crc) || crc.Id != MatroskaIds.Crc32 || crc.DataLength != 4)
			return false;
		var stored = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian (master.Slice (crc.DataOffset, 4));
		var computed = new BinaryData (master.Slice (crc.End).ToArray ()).ComputeCrc32 ();
		return stored == computed;
	}

	static bool TryFindCuePosition (ReadOnlySpan<byte> body, ref ulong position)
	{
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.CueClusterPosition && element.DataLength > 0) {
				position = EbmlReader.ReadUInt (body.Slice (element.DataOffset, (int)element.DataLength));
				return true;
			}

			if (element.Id is 0xBB or 0xB7 && TryFindCuePosition (body.Slice (element.DataOffset, (int)element.DataLength), ref position))
				return true;
			offset = element.End;
		}

		return false;
	}

	static bool TrySegmentBody (byte[] file, out byte[] body, out int bodyAt)
	{
		body = Array.Empty<byte> ();
		bodyAt = 0;
		var offset = 0;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id == MatroskaIds.Segment && !element.UnknownSize) {
				bodyAt = element.DataOffset;
				body = file.AsSpan (element.DataOffset, (int)element.DataLength).ToArray ();
				return true;
			}

			if (element.UnknownSize)
				return false;
			offset = element.End;
		}

		return false;
	}

	static int IndexOf (byte[] data, byte[] pattern)
	{
		var found = data.AsSpan ().IndexOf (pattern);
		return found;
	}

	static int CountOf (byte[] data, byte[] pattern)
	{
		var count = 0;
		var span = data.AsSpan ();
		var at = 0;
		while (at <= span.Length - pattern.Length) {
			var found = span.Slice (at).IndexOf (pattern);
			if (found < 0)
				break;
			count++;
			at += found + 1;
		}

		return count;
	}
}
