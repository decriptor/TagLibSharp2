// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	const ulong Second = 1_000_000_000;

	// EditionUID, EditionFlagHidden, EditionFlagDefault, EditionFlagOrdered, ChapterFlagEnabled, ChapLanguageBCP47.
	static readonly ulong[] WebMUnsupported = [0x45BC, 0x45BD, 0x45DB, 0x45DD, 0x4598, 0x437D];

	[TestMethod]
	public void ReadChapters_EditionAndAtoms_MapEveryField ()
	{
		var chapters = ChaptersElement (EditionEntry (
			UInt (0x45BC, 77),
			UInt (0x45DB, 1),
			UInt (0x45DD, 1),
			UInt (0x45BD, 1),
			Atom (11, 0, 5 * Second,
				Element (0x5654, "cue-1"u8),
				UInt (0x98, 1),
				UInt (0x4598, 0),
				Display ("Intro", "fre", "fr"),
				Master (0x80, Concat (Element (0x85, "Einleitung"u8), Element (0x437D, "de-DE"u8)))),
			Atom (12, 5 * Second, null, Display ("Main"))));
		var file = MatroskaFile.Read (FileWith (chapters)).File!;

		Assert.AreEqual (1, file.Chapters.Editions.Count);
		var edition = file.Chapters.Editions[0];
		Assert.AreEqual (77UL, edition.Uid);
		Assert.IsTrue (edition.IsDefault);
		Assert.IsTrue (edition.IsOrdered);
		Assert.IsTrue (edition.IsHidden);
		Assert.AreSame (edition, file.Chapters.DefaultEdition);
		Assert.AreEqual (2, edition.Chapters.Count);

		var intro = edition.Chapters[0];
		Assert.AreEqual (11UL, intro.Uid);
		Assert.AreEqual ("cue-1", intro.StringUid);
		Assert.AreEqual (0UL, intro.StartNanoseconds);
		Assert.AreEqual (5 * Second, intro.EndNanoseconds);
		Assert.AreEqual (TimeSpan.FromSeconds (5), intro.End);
		Assert.IsTrue (intro.IsHidden);
		Assert.IsFalse (intro.IsEnabled);
		Assert.AreEqual ("Intro", intro.Title);
		Assert.AreEqual (2, intro.Displays.Count);
		AssertValues (intro.Displays[0].Languages, "fre");
		AssertValues (intro.Displays[0].Countries, "fr");
		Assert.AreEqual ("Einleitung", intro.Displays[1].Text);
		AssertValues (intro.Displays[1].LanguagesBcp47, "de-DE");

		var main = edition.Chapters[1];
		Assert.AreEqual (TimeSpan.FromSeconds (5), main.Start);
		Assert.IsNull (main.EndNanoseconds);
		Assert.IsNull (main.End);
	}

	[TestMethod]
	public void ReadChapters_MissingFlags_UseTheSpecDefaults ()
	{
		// RFC 9559: EditionFlag* default 0, ChapterFlagHidden 0, ChapterFlagEnabled 1, ChapLanguage "eng".
		var chapters = ChaptersElement (EditionEntry (Atom (1, 0, null, Master (0x80, Element (0x85, "Only"u8)))));
		var file = MatroskaFile.Read (FileWith (chapters)).File!;

		var edition = file.Chapters.Editions[0];
		Assert.AreEqual (0UL, edition.Uid);
		Assert.IsFalse (edition.IsDefault);
		Assert.IsFalse (edition.IsOrdered);
		Assert.IsFalse (edition.IsHidden);
		var chapter = edition.Chapters[0];
		Assert.IsFalse (chapter.IsHidden);
		Assert.IsTrue (chapter.IsEnabled);
		AssertValues (chapter.Displays[0].Languages, "eng");
	}

	[TestMethod]
	public void ReadChapters_EmptyFlagPayloads_UseTheDefaults ()
	{
		// RFC 8794 section 6.1: an empty element with a default value reads as that default.
		var chapters = ChaptersElement (EditionEntry (
			Element (0x45DB, Array.Empty<byte> ()),
			Atom (1, 0, null, Element (0x4598, Array.Empty<byte> ()), Element (0x98, Array.Empty<byte> ()))));
		var chapter = MatroskaFile.Read (FileWith (chapters)).File!.Chapters.Editions[0].Chapters[0];

		Assert.IsTrue (chapter.IsEnabled);
		Assert.IsFalse (chapter.IsHidden);
	}

	[TestMethod]
	public void ReadChapters_NestedAtoms_KeepTheirParent ()
	{
		var chapters = ChaptersElement (EditionEntry (
			Atom (1, 0, 10 * Second, Display ("Part"),
				Atom (2, 0, 4 * Second, Display ("Scene A")),
				Atom (3, 4 * Second, 10 * Second, Display ("Scene B"),
					Atom (4, 5 * Second, 6 * Second, Display ("Shot"))))));
		var part = MatroskaFile.Read (FileWith (chapters)).File!.Chapters.Editions[0].Chapters[0];

		Assert.AreEqual ("Part", part.Title);
		Assert.AreEqual (2, part.Chapters.Count);
		Assert.AreEqual ("Scene B", part.Chapters[1].Title);
		Assert.AreEqual ("Shot", part.Chapters[1].Chapters[0].Title);
		Assert.AreEqual (4UL, part.Chapters[1].Chapters[0].Uid);
	}

	[TestMethod]
	public void ReadChapters_ChapterTitles_NeverBecomeTheFileTitle ()
	{
		var chapters = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Chapter One"))));
		var file = MatroskaFile.Read (FileWith (chapters)).File!;

		Assert.IsNull (file.Tag.Title);
		Assert.IsNull (((IMediaFile)file).Tag!.Title);
		Assert.AreEqual ("Chapter One", file.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void ReadChapters_NoChaptersElement_IsEmpty ()
	{
		var file = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("TITLE", "Movie"))))).File!;

		Assert.IsTrue (file.Chapters.IsEmpty);
		Assert.AreEqual (0, file.Chapters.Editions.Count);
		Assert.IsNull (file.Chapters.DefaultEdition);
		Assert.IsFalse (file.Chapters.IsWebM);
	}

	[TestMethod]
	public void ReadChapters_DefaultEdition_IsTheFirstFlaggedOrTheFirst ()
	{
		var none = MatroskaFile.Read (FileWith (ChaptersElement (
			EditionEntry (UInt (0x45BC, 1), Atom (1, 0, null)),
			EditionEntry (UInt (0x45BC, 2), Atom (2, 0, null))))).File!.Chapters;
		var second = MatroskaFile.Read (FileWith (ChaptersElement (
			EditionEntry (UInt (0x45BC, 1), Atom (1, 0, null)),
			EditionEntry (UInt (0x45BC, 2), UInt (0x45DB, 1), Atom (2, 0, null)),
			EditionEntry (UInt (0x45BC, 3), UInt (0x45DB, 1), Atom (3, 0, null))))).File!.Chapters;

		Assert.AreEqual (1UL, none.DefaultEdition!.Uid);
		Assert.AreEqual (2UL, second.DefaultEdition!.Uid);
	}

	[TestMethod]
	public void ReadChapters_MissingOrZeroUid_GetsANonzeroUid ()
	{
		var chapters = ChaptersElement (EditionEntry (
			Master (0xB6, UInt (0x91, 0)),
			Master (0xB6, Concat (UInt (0x73C4, 0), UInt (0x91, 0)))));
		var edition = MatroskaFile.Read (FileWith (chapters)).File!.Chapters.Editions[0];

		Assert.AreNotEqual (0UL, edition.Chapters[0].Uid);
		Assert.AreNotEqual (0UL, edition.Chapters[1].Uid);
	}

	[TestMethod]
	public void ReadChapters_WebM_IsFlagged ()
	{
		var original = Matroska (Master (0x18538067, ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("A"))))), "webm");
		var file = MatroskaFile.Read (original).File!;

		Assert.IsTrue (file.Chapters.IsWebM);
		Assert.AreEqual ("A", file.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void ReadChapters_SecondChaptersElement_IsNotMerged ()
	{
		var first = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("First"))));
		var second = ChaptersElement (EditionEntry (Atom (2, 0, null, Display ("Second"))));
		var file = MatroskaFile.Read (FileWith (first, second)).File!;

		Assert.AreEqual (1, file.Chapters.Editions.Count);
		Assert.AreEqual ("First", file.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void ReadChapters_StreamAndBytes_Agree ()
	{
		var original = FileWith (
			Master (0x1549A966, Element (0x7BA9, "Info"u8)),
			Master (0x1F43B675, new byte[] { 0x01, 0x02 }),
			ChaptersElement (EditionEntry (Atom (9, Second, 2 * Second, Display ("Late")))));
		using var stream = new MemoryStream (original);
		var fromStream = MediaFile.Read (stream, "stream.mkv");

		Assert.IsTrue (fromStream.IsSuccess, fromStream.Error);
		var streamed = (MatroskaFile)fromStream.File!;
		Assert.AreEqual ("Late", streamed.Chapters.Editions[0].Chapters[0].Title);
		Assert.AreEqual (9UL, streamed.Chapters.Editions[0].Chapters[0].Uid);
	}

	[TestMethod]
	public void ReadChapters_DeepNesting_IsBoundedAndKept ()
	{
		// 5000 levels would overflow a recursive parser. Atoms past the limit stay as bytes.
		var atom = Concat (UInt (0x73C4, 1), UInt (0x91, 0));
		for (var level = 0; level < 5000; level++)
			atom = Concat (UInt (0x73C4, (ulong)(level + 2)), UInt (0x91, 0), BigElement (0xB6, atom));
		var chapters = BigElement (0x1043A770, BigElement (0x45B9, BigElement (0xB6, atom)));
		var data = Matroska (BigElement (0x18538067, Concat (chapters, Master (0x1F43B675, new byte[] { 0x42 }))));

		var result = MatroskaFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var depth = 0;
		var chapter = result.File!.Chapters.Editions[0].Chapters[0];
		while (chapter.Chapters.Count > 0) {
			chapter = chapter.Chapters[0];
			depth++;
		}

		Assert.IsTrue (depth < 64, depth.ToString (System.Globalization.CultureInfo.InvariantCulture));

		// An edit writes the unparsed levels back as they were.
		result.File.Chapters.Editions[0].Chapters[0].Title = "Top";
		var fs = new MockFileSystem ();
		fs.AddFile ("/deep.mkv", data);
		var saved = result.File.SaveToFile ("/deep.mkv", data, fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/deep.mkv");
		Assert.IsTrue (bytes.AsSpan ().IndexOf (BigElement (0xB6, Concat (UInt (0x73C4, 1), UInt (0x91, 0)))) >= 0);
		Assert.AreEqual ("Top", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void ReadChapters_PastTheEntryLimit_AreKeptAsBytes ()
	{
		// Each edition and atom is one entry. Editions past the limit are not parsed but are written back.
		var count = 40_000;
		var editions = new byte[count][];
		for (var i = 0; i < count; i++)
			editions[i] = EditionEntry (Atom ((ulong)i + 1, 0, null));
		var last = editions[count - 1];
		var data = Matroska (BigElement (0x18538067, Concat (
			BigElement (0x1043A770, Concat (editions)),
			Master (0x1F43B675, new byte[] { 0x42 }))));

		var file = MatroskaFile.Read (data).File!;

		Assert.IsTrue (file.Chapters.Editions.Count < count);
		file.Chapters.Editions[0].Chapters[0].Title = "First";
		var bytes = Save (file, data);
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual ("First", again.Chapters.Editions[0].Chapters[0].Title);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (last) >= 0);
	}

	[TestMethod]
	public void ReadChapters_TruncatedAtEveryByte_DoesNotThrow ()
	{
		var chapters = ChaptersElement (EditionEntry (
			UInt (0x45BC, 5),
			Atom (1, 0, Second, Display ("One", "eng", "us"), Atom (2, 0, null, Display ("Two")))));
		var full = FileWith (chapters);
		for (var length = 0; length < full.Length; length++) {
			var cut = full.AsSpan (0, length).ToArray ();
			var result = MatroskaFile.Read (cut);
			if (result.IsSuccess)
				_ = result.File!.Chapters.IsEmpty;
		}

		// A Chapters element whose children are cut mid-way still yields what parsed.
		var body = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Kept")), new byte[] { 0xB6, 0x8F, 0x73 }));
		var file = MatroskaFile.Read (FileWith (body)).File!;
		Assert.AreEqual ("Kept", file.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_TagEditOnly_KeepsTheOriginalChaptersBytes ()
	{
		// A CRC-32, a Void, an 8-byte size, and padded integers would not survive a re-render.
		var atom = Concat (
			Element (0x73C4, new byte[] { 0, 0, 0, 0, 0, 0, 0, 3 }),
			Element (0x91, new byte[] { 0, 0 }),
			Display ("Keep me"));
		var editionBody = Concat (Element (0xEC, new byte[] { 0, 0 }), Master (0xB6, atom));
		var edition = Concat (Id (0x45B9), new byte[] { 0x01, 0, 0, 0, 0, 0, 0, (byte)editionBody.Length }, editionBody);
		var chapters = Master (0x1043A770, Concat (Element (0xBF, new byte[] { 1, 2, 3, 4 }), edition));
		var marker = new byte[] { 0x51, 0x52, 0x53 };
		var original = FileWith (chapters, Master (0x1F43B675, marker), Tags (Tag (50, Simple ("TITLE", "Movie"))));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual ("Keep me", parsed.Chapters.Editions[0].Chapters[0].Title);
		parsed.Tag.Artist = "Someone";

		var bytes = Save (parsed, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (chapters) >= 0);
		Assert.AreEqual (SegmentOffsetOf (original, chapters), SegmentOffsetOf (bytes, chapters));
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.AreEqual ("Someone", MatroskaFile.Read (bytes).File!.Tag.Artist);
	}

	[TestMethod]
	public void SaveChapters_EditThenRevert_KeepsTheOriginalBytes ()
	{
		var chapters = ChaptersElement (EditionEntry (Element (0xBF, new byte[] { 9, 9, 9, 9 }), Atom (1, 0, null, Display ("Same"))));
		var original = FileWith (chapters, Master (0x1F43B675, new byte[] { 0x01 }));
		var parsed = MatroskaFile.Read (original).File!;
		var chapter = parsed.Chapters.Editions[0].Chapters[0];
		chapter.Title = "Changed";
		chapter.Title = "Same";

		var bytes = Save (parsed, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (chapters) >= 0);
	}

	[TestMethod]
	public void SaveChapters_EditedChapter_RoundTripsAndLeavesTheClusterBytes ()
	{
		var marker = new byte[] { 0xC1, 0xC2, 0xC3, 0xC4 };
		var cluster = Master (0x1F43B675, marker);
		var chapters = ChaptersElement (EditionEntry (
			UInt (0x45BC, 7),
			Atom (1, 0, 3 * Second, Display ("Old"), Master (0x6944, UInt (0x6955, 0))),
			Atom (2, 3 * Second, null, Display ("Two"))));
		var original = FileWith (Master (0x1549A966, Element (0x7BA9, "Movie"u8)), chapters, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		var first = parsed.Chapters.Editions[0].Chapters[0];
		first.Title = "A much longer chapter title than the old one";
		first.End = TimeSpan.FromSeconds (4);
		first.IsHidden = true;
		parsed.Chapters.Editions[0].Chapters[1].IsEnabled = false;

		var bytes = Save (parsed, original);

		Assert.AreEqual (1, CountOf (bytes, marker));
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual ("Movie", again.Tag.Title);
		var edition = again.Chapters.Editions[0];
		Assert.AreEqual (7UL, edition.Uid);
		Assert.AreEqual ("A much longer chapter title than the old one", edition.Chapters[0].Title);
		Assert.AreEqual (1UL, edition.Chapters[0].Uid);
		Assert.AreEqual (4 * Second, edition.Chapters[0].EndNanoseconds);
		Assert.IsTrue (edition.Chapters[0].IsHidden);
		Assert.IsFalse (edition.Chapters[1].IsEnabled);
		// The ChapProcess child this API does not model is written back.
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Master (0x6944, UInt (0x6955, 0))) >= 0);
		Assert.AreEqual (1, TopLevelCount (bytes, MatroskaIds.Chapters));
	}

	[TestMethod]
	public void SaveChapters_AddToAFileWithout_KeepsTheTitleOnTheTag ()
	{
		var marker = new byte[] { 0xA1, 0xA2, 0xA3 };
		var original = FileWith (
			Tags (Tag (50, Simple ("TITLE", "Movie"))),
			Master (0x1F43B675, marker));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.IsTrue (parsed.Chapters.IsEmpty);

		var intro = parsed.Chapters.AddChapter (TimeSpan.Zero, TimeSpan.FromSeconds (30), "Intro");
		var credits = parsed.Chapters.AddChapter (TimeSpan.FromMinutes (5), null, "Credits", "fre");
		intro.AddChapter (TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (20), "Inside");

		var bytes = Save (parsed, original);

		Assert.AreEqual (1, CountOf (bytes, marker));
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual ("Movie", again.Tag.Title);
		Assert.AreEqual ("Movie", again.Tag.Album);
		var edition = again.Chapters.Editions[0];
		Assert.AreNotEqual (0UL, edition.Uid);
		Assert.AreEqual (2, edition.Chapters.Count);
		Assert.AreEqual (intro.Uid, edition.Chapters[0].Uid);
		Assert.AreEqual ("Intro", edition.Chapters[0].Title);
		Assert.AreEqual ("Inside", edition.Chapters[0].Chapters[0].Title);
		Assert.AreEqual (credits.Uid, edition.Chapters[1].Uid);
		Assert.AreEqual (TimeSpan.FromMinutes (5), edition.Chapters[1].Start);
		AssertValues (edition.Chapters[1].Displays[0].Languages, "fre");
	}

	[TestMethod]
	public void SaveChapters_AddChapter_DoesNotTouchTheTagTitle ()
	{
		var original = FileWith (Master (0x1549A966, Element (0x7BA9, "Segment Title"u8)), Master (0x1F43B675, new byte[] { 1 }));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.AddChapter (TimeSpan.Zero, null, "Chapter Title");
		Assert.AreEqual ("Segment Title", parsed.Tag.Title);

		var again = MatroskaFile.Read (Save (parsed, original)).File!;

		Assert.AreEqual ("Segment Title", again.Tag.Title);
		Assert.AreEqual ("Chapter Title", again.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_NewUids_AreRandomAndNonzero ()
	{
		var chapters = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!.Chapters;
		var seen = new HashSet<ulong> ();
		for (var i = 0; i < 64; i++) {
			var chapter = chapters.AddChapter (TimeSpan.FromSeconds (i));
			Assert.AreNotEqual (0UL, chapter.Uid);
			Assert.IsTrue (seen.Add (chapter.Uid));
		}

		Assert.AreNotEqual (0UL, chapters.Editions[0].Uid);
		Assert.AreNotEqual (0UL, new MatroskaChapter ().Uid);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new MatroskaChapter ().Uid = 0);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new MatroskaChapter ().Start = TimeSpan.FromSeconds (-1));
	}

	[TestMethod]
	public void SaveChapters_Clear_RemovesTheElementAndItsSeek ()
	{
		var chapters = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Gone"))));
		var info = Master (0x1549A966, Element (0x7BA9, "I"u8));
		var probe = Master (0x114D9B74, Concat (SeekEntry (MatroskaIds.Info, 1), SeekEntry (MatroskaIds.Chapters, 1)));
		var seek = Master (0x114D9B74, Concat (
			SeekEntry (MatroskaIds.Info, (ulong)probe.Length),
			SeekEntry (MatroskaIds.Chapters, (ulong)(probe.Length + info.Length))));
		var marker = new byte[] { 0x77, 0x78 };
		var original = FileWith (seek, info, chapters, Master (0x1F43B675, marker));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.IsTrue (IsElementAt (original, ReadSeeks (original).Find (s => s.Id == MatroskaIds.Chapters).Position, MatroskaIds.Chapters));
		parsed.Chapters.Clear ();

		var bytes = Save (parsed, original);

		Assert.AreEqual (original.Length, bytes.Length);
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.AreEqual (0, TopLevelCount (bytes, MatroskaIds.Chapters));
		Assert.IsTrue (MatroskaFile.Read (bytes).File!.Chapters.IsEmpty);
		Assert.IsFalse (ReadSeeks (bytes).Exists (s => s.Id == MatroskaIds.Chapters));
		Assert.IsTrue (ReadSeeks (bytes).Exists (s => s.Id == MatroskaIds.Info));
	}

	[TestMethod]
	public void SaveChapters_RemoveEveryChapter_RemovesTheEmptyEdition ()
	{
		var chapters = ChaptersElement (
			EditionEntry (UInt (0x45BC, 1), Atom (1, 0, null)),
			EditionEntry (UInt (0x45BC, 2), Atom (2, 0, null)));
		var original = FileWith (chapters, Master (0x1F43B675, new byte[] { 1 }));
		var parsed = MatroskaFile.Read (original).File!;
		var first = parsed.Chapters.Editions[0];
		Assert.IsTrue (first.RemoveChapter (first.Chapters[0]));

		var again = MatroskaFile.Read (Save (parsed, original)).File!;

		// EditionEntry needs at least one ChapterAtom (RFC 9559 section 20.1).
		Assert.AreEqual (1, again.Chapters.Editions.Count);
		Assert.AreEqual (2UL, again.Chapters.Editions[0].Uid);
	}

	[TestMethod]
	public void SaveChapters_Grown_MovesAndRetargetsTheSeek ()
	{
		var chapters = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("A"))));
		var info = Master (0x1549A966, Element (0x7BA9, "I"u8));
		var cluster = Master (0x1F43B675, new byte[] { 0x99, 0x98, 0x97 });
		var probe = Master (0x114D9B74, Concat (SeekEntry (MatroskaIds.Info, 1), SeekEntry (MatroskaIds.Chapters, 1), SeekEntry (MatroskaIds.Cluster, 1)));
		var seek = Master (0x114D9B74, Concat (
			SeekEntry (MatroskaIds.Info, (ulong)probe.Length),
			SeekEntry (MatroskaIds.Chapters, (ulong)(probe.Length + info.Length)),
			SeekEntry (MatroskaIds.Cluster, (ulong)(probe.Length + info.Length + chapters.Length))));
		var original = FileWith (seek, info, chapters, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		for (var i = 0; i < 4; i++)
			parsed.Chapters.AddChapter (TimeSpan.FromSeconds (i + 1), null, "Added chapter " + i);

		var bytes = Save (parsed, original);

		var seeks = ReadSeeks (bytes);
		var chaptersSeek = seeks.Find (s => s.Id == MatroskaIds.Chapters);
		Assert.AreEqual (MatroskaIds.Chapters, chaptersSeek.Id);
		Assert.IsTrue (IsElementAt (bytes, chaptersSeek.Position, MatroskaIds.Chapters));
		Assert.IsTrue (IsElementAt (bytes, seeks.Find (s => s.Id == MatroskaIds.Cluster).Position, MatroskaIds.Cluster));
		Assert.AreEqual (SegmentOffsetOf (original, cluster), SegmentOffsetOf (bytes, cluster));
		Assert.AreEqual (5, MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters.Count);
	}

	[TestMethod]
	public void SaveChapters_Shrunk_StaysInPlaceWithAVoid ()
	{
		var chapters = ChaptersElement (EditionEntry (
			Atom (1, 0, null, Display (new string ('x', 60))),
			Atom (2, Second, null, Display ("Two"))));
		var cluster = Master (0x1F43B675, new byte[] { 0x10, 0x20 });
		var original = FileWith (chapters, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.Editions[0].Chapters[0].Title = "x";

		var bytes = Save (parsed, original);

		Assert.AreEqual (original.Length, bytes.Length);
		Assert.AreEqual (SegmentOffsetOf (original, cluster), SegmentOffsetOf (bytes, cluster));
		Assert.AreEqual ("x", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_BeforeAnUnknownSizeCluster_ShiftsTheCues ()
	{
		var marker = new byte[] { 0xFA, 0xCE };
		var cluster = Unknown (0x1F43B675, Element (0xE7, marker));
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		// The cues sit before the cluster, so the cluster's segment offset is info plus cues.
		var clusterAt = info.Length + CueTo (0).Length;
		var original = FileWith (info, CueTo ((byte)clusterAt), cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.AddChapter (TimeSpan.Zero, null, "Live");

		var bytes = Save (parsed, original);

		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.IsTrue (TryReadCueClusterPosition (bytes, out var position, out _));
		Assert.IsTrue (IsElementAt (bytes, position, MatroskaIds.Cluster));
		Assert.AreEqual ("Live", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_WebM_LeavesOutWhatWebMDoesNotSupport ()
	{
		var original = Matroska (Master (0x18538067, Master (0x1F43B675, new byte[] { 1 })), "webm");
		var parsed = MatroskaFile.Read (original).File!;
		var chapter = parsed.Chapters.AddChapter (TimeSpan.Zero, TimeSpan.FromSeconds (1), "Web");
		chapter.StringUid = "vtt-1";
		chapter.IsHidden = true;
		chapter.IsEnabled = false;
		chapter.Displays[0].LanguagesBcp47.Add ("en-US");
		chapter.Displays[0].Countries.Add ("us");
		var edition = parsed.Chapters.Editions[0];
		edition.IsDefault = true;
		edition.IsOrdered = true;
		edition.IsHidden = true;

		var bytes = Save (parsed, original);

		foreach (var unsupported in WebMUnsupported)
			Assert.AreEqual (-1, IndexOf (bytes, Id (unsupported)), unsupported.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
		var again = MatroskaFile.Read (bytes).File!;
		var read = again.Chapters.Editions[0].Chapters[0];
		Assert.AreEqual ("Web", read.Title);
		Assert.AreEqual ("vtt-1", read.StringUid);
		Assert.AreEqual (chapter.Uid, read.Uid);
		Assert.AreEqual (TimeSpan.FromSeconds (1), read.End);
		Assert.IsFalse (read.IsHidden);
		AssertValues (read.Displays[0].Countries, "us");
	}

	[TestMethod]
	public void SaveChapters_Matroska_WritesTheFlagsAndBcp47 ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 1 }));
		var parsed = MatroskaFile.Read (original).File!;
		// An ordered edition needs an end on every lowest-level chapter (RFC 9559 section 20.2.4).
		var chapter = parsed.Chapters.AddChapter (TimeSpan.Zero, TimeSpan.FromSeconds (1), "Flagged");
		chapter.IsHidden = true;
		chapter.IsEnabled = false;
		chapter.Displays[0].LanguagesBcp47.Add ("en-GB");
		var edition = parsed.Chapters.Editions[0];
		edition.IsDefault = true;
		edition.IsOrdered = true;
		edition.IsHidden = true;

		var again = MatroskaFile.Read (Save (parsed, original)).File!.Chapters.Editions[0];

		Assert.IsTrue (again.IsDefault);
		Assert.IsTrue (again.IsOrdered);
		Assert.IsTrue (again.IsHidden);
		Assert.AreEqual (edition.Uid, again.Uid);
		Assert.IsTrue (again.Chapters[0].IsHidden);
		Assert.IsFalse (again.Chapters[0].IsEnabled);
		AssertValues (again.Chapters[0].Displays[0].LanguagesBcp47, "en-GB");
	}

	[TestMethod]
	public void SaveChapters_RemoveEdition_AndTitleNull_AreWritten ()
	{
		var chapters = ChaptersElement (
			EditionEntry (UInt (0x45BC, 1), Atom (1, 0, null, Display ("One"))),
			EditionEntry (UInt (0x45BC, 2), Atom (2, 0, null, Display ("Two"), Display ("Deux", "fre"))));
		var original = FileWith (chapters, Master (0x1F43B675, new byte[] { 1 }));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.IsTrue (parsed.Chapters.RemoveEdition (parsed.Chapters.Editions[0]));
		parsed.Chapters.Editions[0].Chapters[0].Title = null;

		var again = MatroskaFile.Read (Save (parsed, original)).File!.Chapters;

		Assert.AreEqual (1, again.Editions.Count);
		Assert.AreEqual (2UL, again.Editions[0].Uid);
		Assert.IsNull (again.Editions[0].Chapters[0].Title);
		Assert.AreEqual (0, again.Editions[0].Chapters[0].Displays.Count);
	}

	[TestMethod]
	public async Task SaveChaptersAsync_WritesTheSameBytesAsTheSyncSave ()
	{
		var original = FileWith (
			Tags (Tag (50, Simple ("TITLE", "Movie"))),
			Master (0x1F43B675, new byte[] { 5, 6, 7 }));
		var parsed = MatroskaFile.Read (original).File!;
		var chapter = parsed.Chapters.AddChapter (TimeSpan.FromSeconds (2), null, "Async");
		var sync = Save (parsed, original);

		var fs = new MockFileSystem ();
		fs.AddFile ("/async.mkv", original);
		var saved = await parsed.SaveToFileAsync ("/async.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/async.mkv");
		CollectionAssert.AreEqual (sync, bytes);
		CollectionAssert.AreEqual (sync, parsed.Render (original).ToArray ());
		Assert.AreEqual (chapter.Uid, MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Uid);
	}

	[TestMethod]
	public async Task SaveChapters_FromSourcePath_SyncAndAsync ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 3, 3 }));
		var fs = new MockFileSystem ();
		fs.AddFile ("/path.mkv", original);
		fs.AddFile ("/path2.mkv", original);
		var sync = MatroskaFile.ReadFromFile ("/path.mkv", fs).File!;
		var async = (await MatroskaFile.ReadFromFileAsync ("/path2.mkv", fs)).File!;
		sync.Chapters.AddChapter (TimeSpan.Zero, null, "S");
		async.Chapters.AddChapter (TimeSpan.Zero, null, "A");

		Assert.IsTrue (sync.SaveToFile ().IsSuccess);
		Assert.IsTrue ((await async.SaveToFileAsync ()).IsSuccess);

		Assert.AreEqual ("S", MatroskaFile.Read (fs.ReadAllBytes ("/path.mkv")).File!.Chapters.Editions[0].Chapters[0].Title);
		Assert.AreEqual ("A", MatroskaFile.Read (fs.ReadAllBytes ("/path2.mkv")).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_SecondCopy_IsVoidedWithTheFirst ()
	{
		var first = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Copy"))));
		var original = FileWith (first, Master (0x1F43B675, new byte[] { 1 }), first);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.Editions[0].Chapters[0].Title = "Edited";

		var bytes = Save (parsed, original);

		Assert.AreEqual (1, TopLevelCount (bytes, MatroskaIds.Chapters));
		Assert.AreEqual ("Edited", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_FullSeekHead_PutsThemAfterItAndListsThem ()
	{
		// RFC 9559 section 6.1: the SeekHead MUST list an added element. This SeekHead has no Void
		// to grow into, so appending the chapters after the cluster would leave them unlisted.
		var marker = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5 };
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tags = Tags (Tag (50, Simple ("TITLE", "Movie")));
		var seekLength = WithCrc (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), Seek2 (MatroskaIds.Tags, 0), Seek2 (MatroskaIds.Cluster, 0), Seek2 (MatroskaIds.Cues, 0))).Length;
		var infoAt = seekLength;
		var tagsAt = infoAt + info.Length;
		var clusterAt = tagsAt + tags.Length;
		var cluster = ClusterWithPosition (clusterAt, marker);
		var cuesAt = clusterAt + cluster.Length;
		var seek = WithCrc (MatroskaIds.SeekHead, Concat (
			Seek2 (MatroskaIds.Info, (ulong)infoAt),
			Seek2 (MatroskaIds.Tags, (ulong)tagsAt),
			Seek2 (MatroskaIds.Cluster, (ulong)clusterAt),
			Seek2 (MatroskaIds.Cues, (ulong)cuesAt)));
		var cues = WithCrc (MatroskaIds.Cues, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new[] { (byte)(clusterAt >> 8), (byte)clusterAt }))))));
		var original = FileWith (seek, info, tags, cluster, cues);
		Assert.AreEqual (seekLength, seek.Length);
		Assert.IsTrue (EveryCrcMatches (original));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.AddChapter (TimeSpan.Zero, TimeSpan.FromSeconds (1), "Listed");

		var bytes = Save (parsed, original);

		var seeks = ReadSeeks (bytes);
		var chapters = seeks.Find (entry => entry.Id == MatroskaIds.Chapters);
		Assert.AreEqual (MatroskaIds.Chapters, chapters.Id);
		Assert.IsTrue (IsElementAt (bytes, chapters.Position, MatroskaIds.Chapters));
		Assert.IsTrue (chapters.Position < (ulong)SegmentOffsetOf (bytes, marker));
		foreach (var entry in seeks)
			Assert.IsTrue (IsElementAt (bytes, entry.Position, entry.Id), entry.Id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
		Assert.IsTrue (TryReadCueClusterPosition (bytes, out var cuePosition, out var cueCrcOk));
		Assert.IsTrue (cueCrcOk);
		Assert.IsTrue (IsElementAt (bytes, cuePosition, MatroskaIds.Cluster));
		Assert.AreEqual (cuePosition, ClusterPositionOf (bytes));
		Assert.IsTrue (EveryCrcMatches (bytes));
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.IsTrue (bytes.AsSpan ().IndexOf (tags) >= 0);
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual ("Movie", again.Tag.Album);
		Assert.AreEqual ("Listed", again.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_SeekTooNarrowForTheEnd_StaysListed ()
	{
		// The old Seek has a one-byte position. The grown chapters do not fit in place, and the
		// end of the segment is past 255. Whichever placement the save picks, the Seek follows it.
		var chapters = ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("A"))));
		var info = Master (0x1549A966, Element (0x7BA9, "I"u8));
		var probe = Master (0x114D9B74, Concat (SeekEntry (MatroskaIds.Info, 1), SeekEntry (MatroskaIds.Chapters, 1)));
		var seek = Master (0x114D9B74, Concat (
			SeekEntry (MatroskaIds.Info, (ulong)(probe.Length + chapters.Length)),
			SeekEntry (MatroskaIds.Chapters, (ulong)probe.Length)));
		var marker = new byte[] { 0x3C, 0x3D, 0x3E };
		var cluster = Master (0x1F43B675, Concat (Element (0xA3, marker), new byte[300]));
		// The old chapters sit right after the SeekHead, where the new ones are inserted.
		var original = FileWith (seek, chapters, info, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		for (var i = 0; i < 3; i++)
			parsed.Chapters.AddChapter (TimeSpan.FromSeconds (i + 1), null, "Grown chapter " + i);

		var bytes = Save (parsed, original);

		var listed = ReadSeeks (bytes).FindAll (entry => entry.Id == MatroskaIds.Chapters);
		Assert.AreEqual (1, listed.Count);
		Assert.IsTrue (IsElementAt (bytes, listed[0].Position, MatroskaIds.Chapters));
		Assert.IsTrue (IsElementAt (bytes, ReadSeeks (bytes).Find (entry => entry.Id == MatroskaIds.Info).Position, MatroskaIds.Info));
		Assert.AreEqual (4, MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters.Count);
		Assert.AreEqual (1, TopLevelCount (bytes, MatroskaIds.Chapters));
	}

	[TestMethod]
	public void SaveChapters_FullSeekHead_WidensTheOtherSeeks ()
	{
		// The move pushes the Cues past offset 255. The SeekHead is written again, so the Cues Seek widens.
		var info = Master (0x1549A966, Element (0x7BA9, "I"u8));
		var probe = Master (0x114D9B74, Concat (SeekEntry (MatroskaIds.Info, 1), SeekEntry (MatroskaIds.Cues, 1)));
		var marker = new byte[] { 0x5A, 0x5B };
		var cluster = Master (0x1F43B675, Concat (Element (0xA3, marker), new byte[200]));
		var clusterAt = probe.Length + info.Length;
		var cuesAt = clusterAt + cluster.Length;
		Assert.IsTrue (cuesAt is > 200 and < 256);
		var seek = Master (0x114D9B74, Concat (SeekEntry (MatroskaIds.Info, (ulong)probe.Length), SeekEntry (MatroskaIds.Cues, (ulong)cuesAt)));
		var original = FileWith (seek, info, cluster, CueTo ((byte)clusterAt));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.AddChapter (TimeSpan.Zero, null, "Kept out of the way");

		var bytes = Save (parsed, original);

		var cues = ReadSeeks (bytes).Find (entry => entry.Id == MatroskaIds.Cues);
		Assert.AreEqual (MatroskaIds.Cues, cues.Id);
		Assert.IsTrue (IsElementAt (bytes, cues.Position, MatroskaIds.Cues));
		Assert.IsTrue (cues.Position > 255);
		var listed = ReadSeeks (bytes).Find (entry => entry.Id == MatroskaIds.Chapters);
		Assert.IsTrue (IsElementAt (bytes, listed.Position, MatroskaIds.Chapters));
		Assert.IsTrue (listed.Position < (ulong)SegmentOffsetOf (bytes, cluster));
		Assert.AreEqual (1, CountOf (bytes, cluster));
		Assert.AreEqual ("Kept out of the way", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void Cookbook_MatroskaChaptersExample_SavesWhatItShows ()
	{
		// docs/Cookbook.md, "Work with Matroska/WebM Chapters".
		var fs = new MockFileSystem ();
		fs.AddFile ("/movie.mkv", FileWith (Tags (Tag (50, Simple ("TITLE", "Movie"))), Master (0x1F43B675, new byte[] { 1 })));
		var mkv = MatroskaFile.ReadFromFile ("/movie.mkv", fs).File!;

		var intro = mkv.Chapters.AddChapter (TimeSpan.Zero, TimeSpan.FromMinutes (2), "Introduction");
		intro.AddChapter (TimeSpan.FromSeconds (30), TimeSpan.FromSeconds (60), "Opening titles");
		mkv.Chapters.DefaultEdition!.Chapters[0].Title = "Cold open";
		Assert.IsTrue (mkv.SaveToFile ().IsSuccess);

		var again = MatroskaFile.ReadFromFile ("/movie.mkv", fs).File!;
		Assert.AreEqual ("Movie", again.Tag.Title);
		var chapter = again.Chapters.Editions[0].Chapters[0];
		Assert.AreEqual ("Cold open", chapter.Title);
		Assert.AreEqual (TimeSpan.FromMinutes (2), chapter.End);
		Assert.AreEqual ("Opening titles", chapter.Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_NoSeekHead_GoBeforeTheCluster ()
	{
		var marker = new byte[] { 0x61, 0x62 };
		var cluster = Master (0x1F43B675, Element (0xA3, marker));
		var original = FileWith (Master (0x1549A966, Element (0x7BA9, "I"u8)), cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.AddChapter (TimeSpan.Zero, null, "End");

		var bytes = Save (parsed, original);

		// RFC 9559 section 25.3.4: without a SeekHead a reader looks before the first Cluster.
		Assert.AreEqual (1, CountOf (bytes, cluster));
		Assert.IsTrue (SegmentOffsetOf (bytes, Id (0x1043A770)) < SegmentOffsetOf (bytes, cluster));
		Assert.AreEqual ("End", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void Save_NoEdits_ReturnsTheSameBytes ()
	{
		// The Void inside the Tag would not survive a re-render.
		var tags = Master (0x1254C367, Master (0x7373, Concat (
			Master (0x63C0, Element (0x68CA, new byte[] { 50 })),
			Element (0xEC, new byte[] { 0, 0, 0 }),
			Simple ("TITLE", "Movie"))));
		var original = FileWith (Master (0x1549A966, Element (0x7BA9, "I"u8)), tags, Master (0x1F43B675, new byte[] { 1, 2 }));
		var parsed = MatroskaFile.Read (original).File!;

		CollectionAssert.AreEqual (original, Save (parsed, original));
		CollectionAssert.AreEqual (original, parsed.Render (original).ToArray ());
	}

	[TestMethod]
	public void SaveChapters_ChapterEditOnly_KeepsTheOriginalTagsBytes ()
	{
		var tags = Master (0x1254C367, Master (0x7373, Concat (
			Master (0x63C0, Element (0x68CA, new byte[] { 50 })),
			Element (0xEC, new byte[] { 0, 0, 0 }),
			Simple ("TITLE", "Movie"))));
		var original = FileWith (
			ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Old")))),
			tags,
			Master (0x1F43B675, new byte[] { 1, 2 }));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Chapters.Editions[0].Chapters[0].Title = "A new and much longer chapter title";

		var bytes = Save (parsed, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (tags) >= 0);
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual ("Movie", again.Tag.Title);
		Assert.AreEqual ("A new and much longer chapter title", again.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void Save_NoEdits_StillRejectsAContainerThatDoesNotParse ()
	{
		var parsed = MatroskaFile.Read (FileWith (Master (0x1F43B675, new byte[] { 1 }))).File!;
		var fs = new MockFileSystem ();

		AssertFailure (parsed, fs, "/bad.mkv", new byte[] { 1, 2, 3, 4 }, "Not a Matroska file");
	}

	static void AssertValues (IList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	/// <summary>The offset from the segment payload. The segment size field can widen when the body grows.</summary>
	static int SegmentOffsetOf (byte[] file, byte[] pattern)
	{
		Assert.IsTrue (TrySegmentBody (file, out _, out var bodyAt));
		var at = IndexOf (file, pattern);
		Assert.IsTrue (at >= 0);
		return at - bodyAt;
	}

	static int TopLevelCount (byte[] file, ulong id)
	{
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		var count = 0;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == id)
				count++;
			offset = element.End;
		}

		return count;
	}

	static byte[] CueTo (byte clusterAt) =>
		Master (0x1C53BB6B, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new[] { clusterAt }))))));

	static byte[] ChaptersElement (params byte[][] editions) => Master (0x1043A770, Concat (editions));

	static byte[] EditionEntry (params byte[][] children) => Master (0x45B9, Concat (children));

	static byte[] Atom (ulong uid, ulong start, ulong? end, params byte[][] children) =>
		Master (0xB6, Concat (
			UInt (0x73C4, uid),
			UInt (0x91, start),
			end is ulong value ? UInt (0x92, value) : Array.Empty<byte> (),
			Concat (children)));

	static byte[] Display (string text, string? language = null, string? country = null)
	{
		var body = Element (0x85, Encoding.UTF8.GetBytes (text));
		if (language is not null)
			body = Concat (body, Element (0x437C, Encoding.ASCII.GetBytes (language)));
		if (country is not null)
			body = Concat (body, Element (0x437E, Encoding.ASCII.GetBytes (country)));
		return Master (0x80, body);
	}

	static byte[] UInt (ulong id, ulong value)
	{
		var width = 1;
		for (var probe = value >> 8; probe != 0; probe >>= 8)
			width++;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)value;
			value >>= 8;
		}

		return Element (id, bytes);
	}

	/// <summary>An element with a four-byte size, for payloads past what <see cref="Element"/> encodes.</summary>
	static byte[] BigElement (ulong id, byte[] payload)
	{
		var length = payload.Length;
		return Concat (Id (id), new[] { (byte)(0x10 | (length >> 24)), (byte)(length >> 16), (byte)(length >> 8), (byte)length }, payload);
	}
}
