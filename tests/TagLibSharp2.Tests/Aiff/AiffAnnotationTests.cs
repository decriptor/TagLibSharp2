// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public partial class AiffAnnotationTests
{
	[TestMethod]
	public void Read_Anno_ReturnsCommentAndDuration ()
	{
		var file = ReadAiff (Form (
			Text ("ANNO", "Creator: Logic"),
			Comm (),
			Ssnd ()));

		Assert.AreEqual ("Creator: Logic", file.Tag!.Comment);
		Assert.IsNull (file.Id3v2Tag);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
	}

	[TestMethod]
	public void Read_OddAnno_DoesNotConsumeTheFollowingChunk ()
	{
		// Pad byte is outside ckSize and is not text. A non-zero pad makes a swallowed byte visible.
		var file = ReadAiff (Form (
			Text ("ANNO", "Hello", pad: 0x58),
			Text ("NAME", "Song"),
			Comm (),
			Ssnd ()));

		Assert.AreEqual ("Hello", file.Tag!.Comment);
		Assert.AreEqual ("Song", file.Tag.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
		Assert.IsNotNull (file.GetChunk ("COMM"));
	}

	[TestMethod]
	public void Read_Anno_StripsTrailingNulsAndKeepsInternalBytes ()
	{
		var spaced = ReadAiff (Form (Text ("ANNO", "Creator:  Logic ", trailingNul: true), Comm ()));
		Assert.AreEqual ("Creator:  Logic ", spaced.Tag!.Comment);

		var internalNul = ReadAiff (Form (Text ("ANNO", "A\0B"), Comm ()));
		Assert.AreEqual ("A\0B", internalNul.Tag!.Comment);
	}

	[TestMethod]
	public void Read_ComtWithoutAnno_ReturnsCommentText ()
	{
		var file = ReadAiff (Form (Comt ("Take 2"), Comm ()));

		Assert.AreEqual ("Take 2", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Comt_JoinsCommentsInOrder ()
	{
		var file = ReadAiff (Form (Comt ("Take 2", "", "Take 3"), Comm ()));

		Assert.AreEqual ("Take 2\nTake 3", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Comt_OddTextPadIsNotPartOfTheNextComment ()
	{
		var file = ReadAiff (Form (Comt ("A", "B"), Comm ()));

		Assert.AreEqual ("A\nB", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_TruncatedComt_KeepsCompleteComments ()
	{
		var tail = ReadAiff (Form (TruncatedComt (), Comm ()));
		Assert.AreEqual ("Take 2", tail.Tag!.Comment);

		var overrun = ReadAiff (Form (OverrunComt (), Comm ()));
		Assert.AreEqual ("Take 2", overrun.Tag!.Comment);

		var empty = ReadAiff (Form (Chunk ("COMT", [0x00]), Comm ()));
		Assert.IsNull (empty.Tag);
	}

	[TestMethod]
	public void Read_TwoAnnoChunks_KeepsBoth ()
	{
		var file = ReadAiff (Form (Text ("ANNO", "Creator: Logic"), Text ("ANNO", "Take 2"), Comm ()));

		Assert.AreEqual ("Creator: Logic\nTake 2", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_TwoComtChunks_KeepsBoth ()
	{
		var file = ReadAiff (Form (Comt ("Take 1"), Comt ("Take 2"), Comm ()));

		Assert.AreEqual ("Take 1\nTake 2", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Text_KeepsSurroundingSpace ()
	{
		var comment = ReadAiff (Form (Comt (" Creator: Logic"), Comm ()));
		Assert.AreEqual (" Creator: Logic", comment.Tag!.Comment);

		var trailing = ReadAiff (Form (Text ("ANNO", "Logic "), Comm ()));
		Assert.AreEqual ("Logic ", trailing.Tag!.Comment);

		var anno = ReadAiff (Form (Text ("ANNO", " Creator: Logic"), Comm ()));
		Assert.AreEqual (" Creator: Logic", anno.Tag!.Comment);
	}

	[TestMethod]
	public void Read_AnnoAndComt_PrefersMarkerZero ()
	{
		var annoFirst = ReadAiff (Form (Text ("ANNO", "Creator: Logic"), Comt ("Take 2"), Comm ()));
		Assert.AreEqual ("Take 2", annoFirst.Tag!.Comment);

		var comtFirst = ReadAiff (Form (Comt ("Take 2"), Text ("ANNO", "Creator: Logic"), Comm ()));
		Assert.AreEqual ("Take 2", comtFirst.Tag!.Comment);
	}

	[TestMethod]
	public void Read_NonZeroMarker_IsNotTheFileComment ()
	{
		var only = ReadAiff (Form (Marked ((4, "Cue")), Comm ()));
		Assert.IsNull (only.Tag?.Comment);
		Assert.IsNull (only.Id3v2Tag);

		var annoFirst = ReadAiff (Form (Text ("ANNO", "Ann"), Marked ((4, "Cue")), Comm ()));
		Assert.AreEqual ("Ann", annoFirst.Tag!.Comment);
		var annoSecond = ReadAiff (Form (Marked ((4, "Cue")), Text ("ANNO", "Ann"), Comm ()));
		Assert.AreEqual ("Ann", annoSecond.Tag!.Comment);

		var joined = ReadAiff (Form (Marked ((0, "One"), (8, "Skip"), (0, "Two")), Comm ()));
		Assert.AreEqual ("One\nTwo", joined.Tag!.Comment);

		var annosUsed = ReadAiff (Form (Text ("ANNO", "A"), Text ("ANNO", "B"), Marked ((3, "Skip")), Comm ()));
		Assert.AreEqual ("A\nB", annosUsed.Tag!.Comment);

		var hiddenFirst = ReadAiff (Form (Text ("ANNO", "A"), Text ("ANNO", "B"), Marked ((0, "File")), Comm ()));
		Assert.AreEqual ("File", hiddenFirst.Tag!.Comment);
		var hiddenSecond = ReadAiff (Form (Marked ((0, "File")), Text ("ANNO", "A"), Text ("ANNO", "B"), Comm ()));
		Assert.AreEqual ("File", hiddenSecond.Tag!.Comment);
	}

	[TestMethod]
	public void Read_MacRomanAndUtf8_DecodeTextChunks ()
	{
		var ost = new byte[] { 0x85, 0x73, 0x74 };
		Assert.AreEqual ("\u00D6st", ReadAiff (Form (TextBytes ("ANNO", ost), Comm ())).Tag!.Comment);
		Assert.AreEqual ("\u00D6st", ReadAiff (Form (ComtBytes (0, ost), Comm ())).Tag!.Comment);
		Assert.AreEqual ("\u00D6st", ReadAiff (Form (TextBytes ("NAME", ost), Comm ())).Tag!.Title);
		Assert.AreEqual ("\u00D6st", ReadAiff (Form (TextBytes ("AUTH", ost), Comm ())).Tag!.Artist);
		Assert.AreEqual ("\u00D6st", ReadAiff (Form (TextBytes ("(c) ", ost), Comm ())).Tag!.Copyright);

		var dagger = ReadAiff (Form (TextBytes ("ANNO", [0xA0, 0x41]), Comm ()));
		Assert.AreEqual ("\u2020A", dagger.Tag!.Comment);

		var hello = System.Text.Encoding.UTF8.GetBytes ("héllo");
		Assert.AreEqual ("héllo", ReadAiff (Form (TextBytes ("ANNO", hello), Comm ())).Tag!.Comment);
		Assert.AreEqual ("héllo", ReadAiff (Form (ComtBytes (0, hello), Comm ())).Tag!.Comment);
	}

	[TestMethod]
	public void Save_ComtOnly_DoesNotAddId3 ()
	{
		var file = ReadAiff (Form (Comt ("Take 2"), Comm (), Ssnd ()));
		Assert.IsNull (file.Id3v2Tag);
		Assert.AreEqual ("Take 2", file.Tag!.Comment);

		var rendered = file.Render ().ToArray ();
		Assert.IsFalse (HasId3 (rendered));
		var again = ReadAiff (rendered);
		Assert.IsNull (again.Id3v2Tag);
		Assert.AreEqual ("Take 2", again.Tag!.Comment);
	}

	[TestMethod]
	public void ClearComment_DoesNotRestoreNativeText ()
	{
		var both = ReadAiff (Form (
			Id3 (new Id3v2Tag { Comment = "from id3" }),
			Comt ("native"),
			Text ("ANNO", "ann"),
			Comm (),
			Ssnd ()));
		Assert.AreEqual ("from id3", both.Tag!.Comment);
		Assert.AreEqual ("from id3", both.Id3v2Tag!.Comment);
		both.Tag.Comment = null;

		var cleared = ReadAiff (both.Render ().ToArray ());
		Assert.IsNull (cleared.Tag?.Comment);
		Assert.IsNull (cleared.Id3v2Tag?.Comment);

		var native = ReadAiff (Form (Comt ("native"), Comm (), Ssnd ()));
		Assert.IsNull (native.Id3v2Tag);
		native.Comment = null;
		var gone = ReadAiff (native.Render ().ToArray ());
		Assert.IsNull (gone.Tag?.Comment);
		Assert.IsFalse (HasId3 (native.Render ().ToArray ()));
	}

	[TestMethod]
	public void SetComment_OnExistingId3_WritesId3AndKeepsLyrics ()
	{
		var titleOnly = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "Kept", Lyrics = "la la la" }),
			Comt ("native"),
			Comm (),
			Ssnd ()));
		titleOnly.Tag!.Comment = "updated";
		Assert.AreEqual ("updated", titleOnly.Id3v2Tag!.Comment);
		Assert.AreEqual ("la la la", titleOnly.Id3v2Tag.Lyrics);

		var again = ReadAiff (titleOnly.Render ().ToArray ());
		Assert.AreEqual ("updated", again.Tag!.Comment);
		Assert.AreEqual ("updated", again.Id3v2Tag!.Comment);
		Assert.AreEqual ("la la la", again.Tag.Lyrics);
		Assert.AreEqual ("la la la", again.Id3v2Tag.Lyrics);
		Assert.AreEqual ("Kept", again.Tag.Title);
		Assert.IsNull (again.GetChunk ("COMT"));
	}

	[TestMethod]
	public void SetComment_OnRatingOnlyId3_KeepsTheRating ()
	{
		var id3 = new Id3v2Tag { Rating = 200 };
		var file = ReadAiff (Form (Id3 (id3), Comm (), Ssnd ()));
		Assert.IsFalse (file.Id3v2Tag!.IsEmpty);
		file.Tag!.Comment = "note";

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("note", again.Id3v2Tag!.Comment);
		Assert.AreEqual ((byte)200, again.Id3v2Tag.Rating);
	}

	[TestMethod]
	public void NewFields_RoundTripThroughId3NotNativeChunks ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));
		file.Title = "Song";
		file.Artist = "A Person";
		file.Comment = "Take 2";
		file.Copyright = "Copyright 2020";

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("Song", again.Id3v2Tag!.Title);
		Assert.AreEqual ("A Person", again.Id3v2Tag.Artist);
		Assert.AreEqual ("Take 2", again.Id3v2Tag.Comment);
		Assert.AreEqual ("Copyright 2020", again.Id3v2Tag.Copyright);
		Assert.AreEqual ("Take 2", again.Tag!.Comment);
		Assert.IsNull (again.GetChunk ("NAME"));
		Assert.IsNull (again.GetChunk ("AUTH"));
		Assert.IsNull (again.GetChunk ("ANNO"));
		Assert.IsNull (again.GetChunk ("COMT"));
		Assert.IsNull (again.GetChunk ("(c) "));
	}

	[TestMethod]
	public void SetComment_ReplacesNativeTextWithId3 ()
	{
		var fromComt = ReadAiff (Form (Comt ("Take 2"), Comm (), Ssnd ()));
		fromComt.Tag!.Comment = "Take 3";
		var comtAgain = ReadAiff (fromComt.Render ().ToArray ());
		Assert.AreEqual ("Take 3", comtAgain.Id3v2Tag!.Comment);
		Assert.AreEqual ("Take 3", comtAgain.Tag!.Comment);
		Assert.IsNull (comtAgain.GetChunk ("COMT"));

		var fromAnno = ReadAiff (Form (Text ("ANNO", "Old"), Comm (), Ssnd ()));
		fromAnno.Tag!.Comment = "New";
		var annoAgain = ReadAiff (fromAnno.Render ().ToArray ());
		Assert.AreEqual ("New", annoAgain.Id3v2Tag!.Comment);
		Assert.IsNull (annoAgain.GetChunk ("ANNO"));
		Assert.IsNull (annoAgain.GetChunk ("COMT"));
	}

	[TestMethod]
	public void SetTitleArtistCopyright_WriteId3AndDropOverriddenNative ()
	{
		var native = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Text ("(c) ", "Copyright 2020"),
			Comm (),
			Ssnd ()));
		native.Tag!.Title = "Renamed";
		native.Tag.Artist = "Someone";
		native.Tag.Copyright = "Copyright 2021";
		var nativeAgain = ReadAiff (native.Render ().ToArray ());
		Assert.AreEqual ("Renamed", nativeAgain.Id3v2Tag!.Title);
		Assert.AreEqual ("Someone", nativeAgain.Id3v2Tag.Artist);
		Assert.AreEqual ("Copyright 2021", nativeAgain.Id3v2Tag.Copyright);
		Assert.IsNull (nativeAgain.GetChunk ("NAME"));
		Assert.IsNull (nativeAgain.GetChunk ("AUTH"));
		Assert.IsNull (nativeAgain.GetChunk ("(c) "));

		var both = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Text ("(c) ", "Copyright 2020"),
			Id3 (new Id3v2Tag { Title = "From Id3", Artist = "Id3 Artist", Copyright = "Owned" }),
			Comm (),
			Ssnd ()));
		both.Tag!.Title = "Edited";
		Assert.AreEqual ("Edited", both.Id3v2Tag!.Title);
		both.Id3v2Tag.Title = null;
		Assert.IsNull (both.Tag.Title);

		both.Tag.Title = null;
		both.Tag.Artist = null;
		both.Tag.Copyright = null;
		var cleared = ReadAiff (both.Render ().ToArray ());
		Assert.IsNull (cleared.Tag?.Title);
		Assert.IsNull (cleared.Tag?.Artist);
		Assert.IsNull (cleared.Tag?.Copyright);
	}

	[TestMethod]
	public void Save_CommentLongerThan64K_WritesId3AndDoesNotThrow ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));
		file.Comment = new string ('q', 70_000);
		var files = new MockFileSystem ();
		var saved = file.SaveToFile ("/song.aiff", files);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var again = ReadAiff (files.ReadAllBytes ("/song.aiff"));
		Assert.AreEqual (70_000, again.Tag!.Comment!.Length);
		Assert.AreEqual (70_000, again.Id3v2Tag!.Comment!.Length);
		Assert.IsNull (again.GetChunk ("COMT"));
		Assert.IsNull (again.GetChunk ("ANNO"));
	}

	[TestMethod]
	public void Comment_NotInMacRoman_GoesToId3 ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));
		file.Comment = "Łódź";

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("Łódź", again.Id3v2Tag!.Comment);
		Assert.IsNull (again.GetChunk ("ANNO"));
		Assert.IsNull (again.GetChunk ("COMT"));
	}

	[TestMethod]
	public void NativeAnno_Ost_StaysMacRomanWhenNotOverridden ()
	{
		var ost = new byte[] { 0x85, 0x73, 0x74 };
		var file = ReadAiff (Form (TextBytes ("ANNO", ost), Comm (), Ssnd ()));
		file.Tag!.Album = "Album";

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("\u00D6st", again.Tag!.Comment);
		Assert.IsNull (again.Id3v2Tag?.Comment);
		CollectionAssert.AreEqual (ost, again.GetChunk ("ANNO")!.Data.ToArray ());
		Assert.AreEqual ("Album", again.Tag.Album);
	}

	[TestMethod]
	public void SetComment_KeepsMarkerCommentsAndMacRomanBytes ()
	{
		var ost = new byte[] { 0x85, 0x73, 0x74 };
		var file = ReadAiff (Form (
			Marked ((0, "File"), (5, "Cue")),
			ComtBytes (5, ost),
			Comm (),
			Ssnd ()));
		file.Tag!.Comment = "Edited";

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("Edited", again.Id3v2Tag!.Comment);
		Assert.AreEqual ("Edited", again.Tag!.Comment);
		var comt = again.GetChunk ("COMT");
		Assert.IsNotNull (comt);
		var data = comt.Data;
		Assert.IsTrue (data.Span.IndexOf ((byte)0x85) >= 0, "MacRoman Ö was rewritten as UTF-8");
		Assert.IsTrue (data.Span.IndexOf (new byte[] { 0xC3, 0x96 }) < 0, "MacRoman Ö was rewritten as UTF-8");

		var sawCue = false;
		var count = data.ToUInt16BE ();
		var offset = 2;
		for (var i = 0; i < count; i++) {
			var timestamp = data.ToUInt32BE (offset);
			var marker = data.ToUInt16BE (offset + 4);
			var length = data.ToUInt16BE (offset + 6);
			var text = data.Slice (offset + 8, length).ToArray ();
			offset += 8 + length;
			if ((length & 1) == 1)
				offset++;
			if (marker == 5 && System.Text.Encoding.Latin1.GetString (text) == "Cue") {
				Assert.AreEqual (1u, timestamp);
				sawCue = true;
			}

			Assert.AreNotEqual ("File", System.Text.Encoding.Latin1.GetString (text));
		}

		Assert.IsTrue (sawCue);
	}

	[TestMethod]
	public void Performers_FollowArtist_AndEmptyClearsAuth ()
	{
		var cleared = ReadAiff (Form (Text ("AUTH", "A Person"), Comm (), Ssnd ()));
		cleared.Tag!.Performers = [];
		Assert.IsNull (cleared.Tag.Artist);
		Assert.AreEqual (0, cleared.Tag.Performers.Length);
		var nulled = ReadAiff (Form (Text ("AUTH", "A Person"), Comm (), Ssnd ()));
		nulled.Tag!.Performers = null!;
		Assert.IsNull (nulled.Tag.Artist);
		var gone = ReadAiff (cleared.Render ().ToArray ());
		Assert.IsNull (gone.GetChunk ("AUTH"));
		Assert.IsNull (gone.Tag?.Artist);

		var set = ReadAiff (Form (Text ("AUTH", "A Person"), Comm (), Ssnd ()));
		var people = new[] { "New", "Other" };
		set.Tag!.Performers = people;
		var again = ReadAiff (set.Render ().ToArray ());
		Assert.IsNull (again.GetChunk ("AUTH"));
		CollectionAssert.AreEqual (people, again.Tag!.Performers);
		CollectionAssert.AreEqual (people, again.Id3v2Tag!.Performers);
	}

	[TestMethod]
	public void ComposersAndPublisher_ForwardThroughTheCombinedTag ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));
		var composers = new[] { "Ada", "Grace" };
		file.Id3v2Tag = new Id3v2Tag ();
		file.Tag!.Composers = composers;
		file.Tag.Publisher = "Label";
		file.Tag.MusicBrainzTrackId = "mbid-1";
		Assert.AreEqual (2, file.Tag.Composers.Length);
		Assert.AreEqual ("Ada", file.Tag.Composers[0]);
		Assert.AreEqual ("Grace", file.Tag.Composers[1]);

		var copy = new Id3v2Tag ();
		file.Tag.CopyTo (copy);
		Assert.AreEqual ("Label", copy.Publisher);
		Assert.AreEqual ("mbid-1", copy.MusicBrainzTrackId);
		CollectionAssert.AreEqual (composers, copy.Composers);

		var again = ReadAiff (file.Render ().ToArray ());
		CollectionAssert.AreEqual (composers, again.Tag!.Composers);
		Assert.AreEqual ("Label", again.Tag.Publisher);
		Assert.AreEqual ("mbid-1", again.Tag.MusicBrainzTrackId);
	}

	[TestMethod]
	public void Tag_IsCachedGetOnlyView_AndDoesNotDropId3 ()
	{
		var file = ReadAiff (Form (Id3 (new Id3v2Tag { Title = "Kept", Lyrics = "words" }), Comm (), Ssnd ()));
		var view = file.Tag;
		Assert.IsNotNull (view);
		Assert.AreSame (view, file.Tag);
		Assert.IsFalse (view is Id3v2Tag);
		Assert.IsFalse (typeof (AiffFile).GetProperty (nameof (AiffFile.Tag))!.CanWrite);
		Assert.AreEqual ("Kept", file.Id3v2Tag!.Title);
		Assert.IsTrue (file.Id3v2Tag.Render ().Length > 0);

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("Kept", again.Id3v2Tag!.Title);
		Assert.AreEqual ("words", again.Tag!.Lyrics);
	}

	[TestMethod]
	public void ClearLastField_KeepsTheTagObject ()
	{
		var file = ReadAiff (Form (Text ("ANNO", "Hi"), Comm (), Ssnd ()));
		var tag = file.Tag!;
		tag.Comment = null;
		Assert.AreSame (tag, file.Tag);
		tag.Title = "x";
		Assert.AreEqual ("x", tag.Title);

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("x", again.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void NativeOnly_TagType_IsNotNone ()
	{
		var native = ReadAiff (Form (Text ("ANNO", "Hi"), Comm ()));
		Assert.AreEqual (TagTypes.RiffInfo, native.Tag!.TagType);

		var id3 = ReadAiff (Form (Id3 (new Id3v2Tag { Title = "T" }), Comm ()));
		Assert.AreEqual (TagTypes.Id3v2, id3.Tag!.TagType);

		var both = ReadAiff (Form (
			Text ("NAME", "Song"),
			Id3 (new Id3v2Tag { Title = "From Id3" }),
			Comm ()));
		Assert.AreEqual (TagTypes.Id3v2 | TagTypes.RiffInfo, both.Tag!.TagType);
	}

	[TestMethod]
	public void Read_ThousandsOfAnnoChunks_AllocatesLinearly ()
	{
		// Joining n chunks by repeated concatenation allocates about n squared bytes
		// (32 MB here). A linear read allocates about 25 times the input (1 MB). Counting
		// this thread's allocations is deterministic where a wall-clock bound is not.
		const int extra = 4000;
		var chunks = new List<byte[]> (extra + 3) {
			Text ("ANNO", " Creator: Logic"),
			TextBytes ("ANNO", [0x85])
		};
		for (var i = 0; i < extra; i++)
			chunks.Add (Text ("ANNO", "x"));
		chunks.Add (Comm ());
		var data = Form ([.. chunks]);

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var file = ReadAiff (data);
		var comment = file.Tag!.Comment!;
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (allocated < 128L * data.Length, $"{allocated:N0} bytes allocated reading {data.Length:N0} bytes");
		Assert.IsTrue (comment.StartsWith (" Creator: Logic\n\u00D6\n", StringComparison.Ordinal));
		Assert.AreEqual (extra, comment.Count (ch => ch == 'x'));
		Assert.AreEqual (TagTypes.RiffInfo, file.Tag.TagType);
	}

	[TestMethod]
	public void SetComment_KeepsEveryMarkerNoteByteForByte ()
	{
		// A comment edit rewrites COMT without the marker-0 entry. The other notes were not
		// edited, so their bytes stay as read: UTF-8, Mac OS Roman, and a trailing NUL.
		var cafe = new byte[] { 0x43, 0x61, 0x66, 0xC3, 0xA9 };
		var ost = new byte[] { 0x85, 0x73, 0x74 };
		var cue = new byte[] { 0x63, 0x75, 0x65, 0x00 };
		var file = ReadAiff (Form (
			ComtEntries ((0, 0, System.Text.Encoding.Latin1.GetBytes ("File")), (11, 3, cafe), (12, 4, ost), (13, 5, cue)),
			Comm (),
			Ssnd ()));
		Assert.AreEqual ("File", file.Tag!.Comment);

		file.Tag.Comment = "Edited";

		var again = ReadAiff (file.Render ().ToArray ());
		Assert.AreEqual ("Edited", again.Id3v2Tag!.Comment);
		var notes = ReadNotes (again.GetChunk ("COMT")!.Data);
		Assert.AreEqual (3, notes.Count);
		AssertNote (notes[0], 11, 3, cafe);
		AssertNote (notes[1], 12, 4, ost);
		AssertNote (notes[2], 13, 5, cue);
	}

	[TestMethod]
	[DataRow (new byte[] { 0x41, 0xC3 }, "A\u221A", DisplayName = "two-byte lead at the end")]
	[DataRow (new byte[] { 0x41, 0xE1, 0x80 }, "A\u00B7\u00C4", DisplayName = "three-byte lead, one continuation at the end")]
	[DataRow (new byte[] { 0x41, 0xF1, 0x80, 0x80 }, "A\u00D2\u00C4\u00C4", DisplayName = "four-byte lead, two continuations at the end")]
	[DataRow (new byte[] { 0xE0, 0x80, 0x80 }, "\u2021\u00C4\u00C4", DisplayName = "E0 overlong")]
	[DataRow (new byte[] { 0xED, 0xA0, 0x80 }, "\u00CC\u2020\u00C4", DisplayName = "ED surrogate")]
	[DataRow (new byte[] { 0xF0, 0x80, 0x80, 0x80 }, "\uF8FF\u00C4\u00C4\u00C4", DisplayName = "F0 overlong")]
	[DataRow (new byte[] { 0xF4, 0x90, 0x80, 0x80 }, "\u00D9\u00EA\u00C4\u00C4", DisplayName = "F4 above U+10FFFF")]
	[DataRow (new byte[] { 0xC3, 0x28 }, "\u221A(", DisplayName = "bad continuation")]
	[DataRow (new byte[] { 0xE1, 0x80, 0x28 }, "\u00B7\u00C4(", DisplayName = "bad third byte")]
	[DataRow (new byte[] { 0xE0, 0xA0, 0x80 }, "\u0800", DisplayName = "E0 valid")]
	[DataRow (new byte[] { 0xE1, 0x80, 0x80 }, "\u1000", DisplayName = "E1 valid")]
	[DataRow (new byte[] { 0xED, 0x9F, 0xBF }, "\uD7FF", DisplayName = "ED valid")]
	[DataRow (new byte[] { 0xF0, 0x90, 0x80, 0x80 }, "\U00010000", DisplayName = "F0 valid")]
	[DataRow (new byte[] { 0xF1, 0x80, 0x80, 0x80 }, "\U00040000", DisplayName = "F1 valid")]
	[DataRow (new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }, "\U0010FFFF", DisplayName = "F4 valid")]
	public void Read_Text_IsUtf8OnlyWhenEverySequenceIsComplete (byte[] bytes, string expected)
	{
		// RFC 3629 section 4 lists the valid UTF-8 byte sequences. Anything else, including a
		// sequence cut off by the end of the chunk, is Mac OS Roman.
		var file = ReadAiff (Form (TextBytes ("ANNO", bytes), Comm ()));
		Assert.AreEqual (expected, file.Tag!.Comment);
	}

	[TestMethod]
	public void ClearComment_KeepsMarkerNotes ()
	{
		var file = ReadAiff (Form (Marked ((0, "File"), (7, "Mark")), Comm (), Ssnd ()));
		file.Tag!.Comment = null;
		var again = ReadAiff (file.Render ().ToArray ());
		Assert.IsNull (again.Tag?.Comment);
		var comt = again.GetChunk ("COMT");
		Assert.IsNotNull (comt);
		var text = comt.Data.ToStringLatin1 ();
		Assert.IsTrue (text.Contains ("Mark"));
		Assert.IsFalse (text.Contains ("File"));
	}

	[TestMethod]
	public void Read_EmptyAnno_UsesComt ()
	{
		var file = ReadAiff (Form (Text ("ANNO", "", trailingNul: true), Comt ("Take 2"), Comm ()));

		Assert.AreEqual ("Take 2", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Id3Comment_WinsOverAnno ()
	{
		var id3First = ReadAiff (Form (
			Id3 (new Id3v2Tag { Comment = "from id3" }),
			Text ("ANNO", "Creator: Logic"),
			Comm ()));
		Assert.AreEqual ("from id3", id3First.Tag!.Comment);

		var annoFirst = ReadAiff (Form (
			Text ("ANNO", "Creator: Logic"),
			Id3 (new Id3v2Tag { Comment = "from id3" }),
			Comm ()));
		Assert.AreEqual ("from id3", annoFirst.Tag!.Comment);

		var blank = ReadAiff (Form (
			Id3 (new Id3v2Tag { Comment = " " }),
			Text ("ANNO", "Creator: Logic"),
			Comm ()));
		Assert.AreEqual (" ", blank.Tag!.Comment);
	}

	[TestMethod]
	public void Read_AnnoAroundId3WithoutComment_KeepsAnnotationAndOtherFields ()
	{
		var annoFirst = ReadAiff (Form (
			Text ("ANNO", "Creator: Logic"),
			Id3 (new Id3v2Tag { Title = "Kept Title", Artist = "Kept Artist" }),
			Comm ()));
		Assert.AreEqual ("Creator: Logic", annoFirst.Tag!.Comment);
		Assert.IsNull (annoFirst.Id3v2Tag!.Comment);
		Assert.AreEqual ("Kept Title", annoFirst.Tag.Title);
		Assert.AreEqual ("Kept Artist", annoFirst.Tag.Artist);

		var id3First = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "Kept Title", Artist = "Kept Artist" }),
			Text ("ANNO", "Creator: Logic"),
			Comm ()));
		Assert.AreEqual ("Creator: Logic", id3First.Tag!.Comment);
		Assert.AreEqual ("Kept Title", id3First.Tag.Title);
		Assert.AreEqual ("Kept Artist", id3First.Tag.Artist);
	}

	[TestMethod]
	public void Read_NameAndAuth_FillTitleAndArtist ()
	{
		var file = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person", trailingNul: true),
			Comm ()));

		Assert.AreEqual ("Song", file.Tag!.Title);
		Assert.AreEqual ("A Person", file.Tag.Artist);
		Assert.IsNull (file.Tag.Comment);
	}

	[TestMethod]
	public void Read_Id3Title_WinsOverName ()
	{
		var nameFirst = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Id3 (new Id3v2Tag { Title = "From Id3", Artist = "Id3 Artist" }),
			Comm ()));
		Assert.AreEqual ("From Id3", nameFirst.Tag!.Title);
		Assert.AreEqual ("Id3 Artist", nameFirst.Tag.Artist);

		var id3First = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "From Id3" }),
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Comm ()));
		Assert.AreEqual ("From Id3", id3First.Tag!.Title);
		Assert.AreEqual ("A Person", id3First.Tag.Artist);
	}

	[TestMethod]
	public void Read_CopyrightChunk_FillsCopyright ()
	{
		var file = ReadAiff (Form (Text ("(c) ", "Copyright 2020"), Comm ()));

		Assert.AreEqual ("Copyright 2020", file.Tag!.Copyright);
		Assert.IsNotNull (file.GetChunk ("(c) "));
	}

	[TestMethod]
	public void Read_Id3Copyright_WinsOverCopyrightChunk ()
	{
		var chunkFirst = ReadAiff (Form (
			Text ("(c) ", "Copyright 2020"),
			Id3 (new Id3v2Tag { Title = "Kept Title", Copyright = "Owned" }),
			Comm ()));
		Assert.AreEqual ("Owned", chunkFirst.Tag!.Copyright);
		Assert.AreEqual ("Kept Title", chunkFirst.Tag.Title);

		var id3WithoutCopyright = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "Kept Title" }),
			Text ("(c) ", "Copyright 2020"),
			Comm ()));
		Assert.AreEqual ("Copyright 2020", id3WithoutCopyright.Tag!.Copyright);
		Assert.AreEqual ("Kept Title", id3WithoutCopyright.Tag.Title);
	}

	[TestMethod]
	public void Read_OnlyComm_HasNullComment ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));

		Assert.IsNull (file.Tag);
		Assert.IsNull (file.Tag?.Comment);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
	}

	[TestMethod]
	public void Read_SeekableStreamAndMediaFile_ReturnAnnotation ()
	{
		// ANNO follows a real SSND payload so a reader that stops at audio, or loads it, fails.
		var bytes = Form (
			Comm (),
			Ssnd (64),
			Text ("ANNO", "Creator: Logic"),
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Text ("(c) ", "Copyright 2020"));

		var parsed = AiffFile.Read (new MemoryStream (bytes), null, null);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		var file = parsed.File!;
		AssertAnnotation (file);
		var ssnd = file.GetChunk ("SSND");
		Assert.IsNotNull (ssnd);
		Assert.AreEqual (72u, ssnd.Size);
		Assert.AreEqual (0, ssnd.Data.Length);

		var fromStream = MediaFile.Read (new MemoryStream (bytes), "song.aiff");
		Assert.IsTrue (fromStream.IsSuccess, fromStream.Error);
		Assert.AreEqual ("Creator: Logic", fromStream.Tag!.Comment);
		Assert.AreEqual ("Song", fromStream.Tag.Title);
		Assert.AreEqual ("A Person", fromStream.Tag.Artist);
		Assert.AreEqual ("Copyright 2020", fromStream.Tag.Copyright);
		Assert.AreEqual (TimeSpan.FromSeconds (2), ((IMediaFile)fromStream.File!).AudioProperties!.Duration);

		var files = new MockFileSystem ();
		files.AddFile ("/song.aiff", bytes);
		var fromPath = MediaFile.Read ("/song.aiff", files);
		Assert.IsTrue (fromPath.IsSuccess, fromPath.Error);
		Assert.AreEqual ("Creator: Logic", fromPath.Tag!.Comment);
		Assert.AreEqual (0, fromPath.GetFileAs<AiffFile> ()!.GetChunk ("SSND")!.Data.Length);
	}

	[TestMethod]
	public void Render_AnnoComment_SurvivesRoundTrip ()
	{
		var file = ReadAiff (Form (Text ("ANNO", "Creator: Logic"), Comm (), Ssnd ()));

		var again = ReadAiff (file.Render ().ToArray ());

		Assert.AreEqual ("Creator: Logic", again.Tag!.Comment);
		Assert.AreEqual (TimeSpan.FromSeconds (2), again.Properties!.Duration);
	}

	static void AssertAnnotation (AiffFile file)
	{
		Assert.AreEqual ("Creator: Logic", file.Tag!.Comment);
		Assert.AreEqual ("Song", file.Tag.Title);
		Assert.AreEqual ("A Person", file.Tag.Artist);
		Assert.AreEqual ("Copyright 2020", file.Tag.Copyright);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
	}

	static AiffFile ReadAiff (byte[] data)
	{
		var result = AiffFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	static byte[] Form (params byte[][] chunks)
	{
		var contentSize = 4;
		foreach (var chunk in chunks)
			contentSize += chunk.Length;

		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 ("FORM");
		builder.AddUInt32BE ((uint)contentSize);
		builder.AddStringLatin1 ("AIFF");
		foreach (var chunk in chunks)
			builder.Add (chunk);
		return builder.ToArray ();
	}

	static byte[] Comm (int sampleRate = 44100, uint frames = 88200)
	{
		using var data = new BinaryDataBuilder ();
		data.AddUInt16BE (2);
		data.AddUInt32BE (frames);
		data.AddUInt16BE (16);
		data.Add (ExtendedFloat.FromDouble (sampleRate));
		return Chunk ("COMM", data.ToArray ());
	}

	static byte[] Ssnd (int sampleBytes = 0)
	{
		using var data = new BinaryDataBuilder ();
		data.AddUInt32BE (0);
		data.AddUInt32BE (0);
		if (sampleBytes > 0)
			data.Add (new byte[sampleBytes]);
		return Chunk ("SSND", data.ToArray ());
	}

	static byte[] Id3 (Id3v2Tag tag) => Chunk ("ID3 ", tag.Render ().ToArray ());

	static byte[] Text (string fourCc, string text, bool trailingNul = false, byte pad = 0) =>
		TextBytes (fourCc, System.Text.Encoding.Latin1.GetBytes (text), trailingNul, pad);

	static byte[] TextBytes (string fourCc, byte[] bytes, bool trailingNul = false, byte pad = 0)
	{
		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 (fourCc);
		builder.AddUInt32BE ((uint)(bytes.Length + (trailingNul ? 1 : 0)));
		if (bytes.Length > 0)
			builder.Add (bytes);
		if (trailingNul)
			builder.Add (0);
		if (((bytes.Length + (trailingNul ? 1 : 0)) & 1) == 1)
			builder.Add (pad);
		return builder.ToArray ();
	}

	static byte[] Marked (params (ushort Marker, string Text)[] entries)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE ((ushort)entries.Length);
		for (var i = 0; i < entries.Length; i++) {
			var text = System.Text.Encoding.Latin1.GetBytes (entries[i].Text);
			body.AddUInt32BE ((uint)i);
			body.AddUInt16BE (entries[i].Marker);
			body.AddUInt16BE ((ushort)text.Length);
			if (text.Length > 0)
				body.Add (text);
			if ((text.Length & 1) == 1)
				body.Add (0);
		}

		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] ComtBytes (ushort marker, byte[] text)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE (1);
		body.AddUInt32BE (0);
		body.AddUInt16BE (marker);
		body.AddUInt16BE ((ushort)text.Length);
		if (text.Length > 0)
			body.Add (text);
		if ((text.Length & 1) == 1)
			body.Add (0);
		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] ComtEntries (params (uint Timestamp, ushort Marker, byte[] Text)[] entries)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE ((ushort)entries.Length);
		foreach (var (timestamp, marker, text) in entries) {
			body.AddUInt32BE (timestamp);
			body.AddUInt16BE (marker);
			body.AddUInt16BE ((ushort)text.Length);
			if (text.Length > 0)
				body.Add (text);
			if ((text.Length & 1) == 1)
				body.Add (0);
		}

		return Chunk ("COMT", body.ToArray ());
	}

	static List<(uint Timestamp, ushort Marker, byte[] Text)> ReadNotes (BinaryData data)
	{
		var notes = new List<(uint, ushort, byte[])> ();
		var count = data.ToUInt16BE ();
		var offset = 2;
		for (var i = 0; i < count; i++) {
			var length = data.ToUInt16BE (offset + 6);
			notes.Add ((data.ToUInt32BE (offset), data.ToUInt16BE (offset + 4), data.Slice (offset + 8, length).ToArray ()));
			offset += 8 + length + (length & 1);
		}

		return notes;
	}

	static void AssertNote ((uint Timestamp, ushort Marker, byte[] Text) note, uint timestamp, ushort marker, byte[] text)
	{
		Assert.AreEqual (timestamp, note.Timestamp);
		Assert.AreEqual (marker, note.Marker);
		CollectionAssert.AreEqual (text, note.Text, BitConverter.ToString (note.Text));
	}

	static bool HasId3 (byte[] data)
	{
		var offset = 12;
		while (offset + 8 <= data.Length) {
			var id = System.Text.Encoding.ASCII.GetString (data, offset, 4);
			var size = (data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7];
			if (id.StartsWith ("ID3", StringComparison.Ordinal))
				return true;
			var step = 8 + size + (size & 1);
			if (step <= 0)
				break;
			offset += step;
		}

		return false;
	}

	static byte[] Comt (params string[] texts)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE ((ushort)texts.Length);
		for (var i = 0; i < texts.Length; i++) {
			var text = System.Text.Encoding.Latin1.GetBytes (texts[i]);
			body.AddUInt32BE ((uint)i);
			body.AddUInt16BE (0);
			body.AddUInt16BE ((ushort)text.Length);
			if (text.Length > 0)
				body.Add (text);
			if ((text.Length & 1) == 1)
				body.Add (0);
		}

		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] TruncatedComt ()
	{
		var text = System.Text.Encoding.Latin1.GetBytes ("Take 2");
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE (2);
		body.AddUInt32BE (0);
		body.AddUInt16BE (0);
		body.AddUInt16BE ((ushort)text.Length);
		body.Add (text);
		body.Add (0x00, 0x00, 0x00);
		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] OverrunComt ()
	{
		var text = System.Text.Encoding.Latin1.GetBytes ("Take 2");
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE (2);
		body.AddUInt32BE (0);
		body.AddUInt16BE (0);
		body.AddUInt16BE ((ushort)text.Length);
		body.Add (text);
		body.AddUInt32BE (1);
		body.AddUInt16BE (0);
		body.AddUInt16BE (50);
		body.Add (0x41, 0x42);
		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] Chunk (string fourCc, byte[] data)
	{
		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 (fourCc);
		builder.AddUInt32BE ((uint)data.Length);
		if (data.Length > 0)
			builder.Add (data);
		if ((data.Length & 1) == 1)
			builder.Add (0);
		return builder.ToArray ();
	}
}
