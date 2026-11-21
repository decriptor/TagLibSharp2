// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2TagTests
{
	// ID3v2 Tag Structure:
	// - Header (10 bytes)
	// - Extended header (optional, variable size)
	// - Frames (variable, until padding or end)
	// - Padding (optional, zeros until tag size)
	// - Footer (optional, 10 bytes, v2.4 only)

	// Frame Header (v2.3/v2.4):
	// Offset  Size  Field
	// 0       4     Frame ID (ASCII)
	// 4       4     Size (big-endian v2.3, syncsafe v2.4)
	// 8       2     Flags
	// 10+     n     Frame data


	[TestMethod]
	public void Read_ValidV24Tag_ParsesHeader ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_ValidV23Tag_ParsesHeader ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_TagWithTitleFrame_ParsesTitle ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, TestConstants.Metadata.Title, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.Title);
	}

	[TestMethod]
	public void Read_TagWithArtistFrame_ParsesArtist ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Artist, TestConstants.Metadata.Artist, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.Tag!.Artist);
	}

	[TestMethod]
	public void Read_TagWithAlbumFrame_ParsesAlbum ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Album, TestConstants.Metadata.Album, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Album, result.Tag!.Album);
	}

	[TestMethod]
	public void Read_TagWithMultipleFrames_ParsesAll ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithFrames (TestConstants.Id3v2.Version4,
			(TestConstants.FrameIds.Title, "Title"),
			(TestConstants.FrameIds.Artist, "Artist"),
			(TestConstants.FrameIds.Album, "Album"));

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Tag!.Title);
		Assert.AreEqual ("Artist", result.Tag.Artist);
		Assert.AreEqual ("Album", result.Tag.Album);
	}

	[TestMethod]
	public void Read_TagWithYear_ParsesYear ()
	{
		// v2.3 uses TYER
		var dataV23 = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Year, "2024", TestConstants.Id3v2.Version3);
		var resultV23 = Id3v2Tag.Read (dataV23);

		Assert.IsTrue (resultV23.IsSuccess);
		Assert.AreEqual ("2024", resultV23.Tag!.Year);

		// v2.4 uses TDRC
		var dataV24 = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.RecordingTime, "2024", TestConstants.Id3v2.Version4);
		var resultV24 = Id3v2Tag.Read (dataV24);

		Assert.IsTrue (resultV24.IsSuccess);
		Assert.AreEqual ("2024", resultV24.Tag!.Year);
	}

	[TestMethod]
	public void Year_SetSameYearOnV24_KeepsExistingTimestamp ()
	{
		// TDRC is yyyy-MM-ddTHH:mm:ss; a shorter prefix is still a timestamp.
		// Setting the year to that same four digits must not chop the frame.
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.RecordingTime,
			"1999-11-13T12:00",
			TestConstants.Id3v2.Version4);
		var tag = Id3v2Tag.Read (data).Tag!;

		tag.Year = "1999";

		Assert.AreEqual ("1999-11-13T12:00", tag.GetTextFrame ("TDRC"));
		Assert.AreEqual ("1999", tag.Year);
	}

	[TestMethod]
	public void Year_SetInvalidOnV24_LeavesExistingTimestamp ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.RecordingTime,
			"1999-11-13T12:00",
			TestConstants.Id3v2.Version4);
		var tag = Id3v2Tag.Read (data).Tag!;

		tag.Year = "nope";

		Assert.AreEqual ("1999-11-13T12:00", tag.GetTextFrame ("TDRC"));
		Assert.AreEqual ("1999", tag.Year);
	}

	[TestMethod]
	public void Year_SetOnV23_StoresFourDigitYear ()
	{
		// TYER is always four characters.
		var tag = new Id3v2Tag (Id3v2Version.V23);

		tag.Year = "1999";

		Assert.AreEqual ("1999", tag.GetTextFrame ("TYER"));
		Assert.AreEqual ("1999", tag.Year);
	}

	[TestMethod]
	public void Year_SetTimestampOnV24_RendersAndRereadsTimestamp ()
	{
		// TDRC accepts a reduced ISO-8601 timestamp, not only yyyy.
		var tag = new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-05-01T10:00"
		};

		var reread = Id3v2Tag.Read (tag.Render ().Span).Tag!;

		Assert.AreEqual ("2024-05-01T10:00", reread.GetTextFrame ("TDRC"));
		Assert.AreEqual ("2024", reread.Year);
	}

	[TestMethod]
	public void CopyTo_V24Date_RoundTripsFullTimestamp ()
	{
		var source = new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-05-01"
		};
		var target = new Id3v2Tag (Id3v2Version.V24);

		source.CopyTo (target);

		var reread = Id3v2Tag.Read (target.Render ().Span).Tag!;
		Assert.AreEqual ("2024", reread.Year);
		Assert.AreEqual ("2024-05-01", reread.GetTextFrame ("TDRC"));
	}

	[TestMethod]
	public void CopyTo_V24Date_StoresFullDateOnVorbisComment ()
	{
		var source = new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-05-01"
		};
		var target = new VorbisComment ();

		source.CopyTo (target);

		Assert.AreEqual ("2024-05-01", target.Year);
	}

	[TestMethod]
	public void CopyTo_V24ItunesTimestamp_CopiesFullTimestampNotYear ()
	{
		// iTunes stores TDRC with a trailing Z. The year setter keeps that string.
		// Copy must write the same string, not the four-digit year.
		const string timestamp = "2024-05-01T07:00:00Z";
		var source = new Id3v2Tag (Id3v2Version.V24) {
			Year = timestamp
		};
		var id3Target = new Id3v2Tag (Id3v2Version.V24);
		var vorbisTarget = new VorbisComment ();

		source.CopyTo (id3Target);
		source.CopyTo (vorbisTarget);

		var reread = Id3v2Tag.Read (id3Target.Render ().Span).Tag!;
		Assert.AreEqual (timestamp, reread.GetTextFrame ("TDRC"));
		Assert.AreNotEqual ("2024", reread.GetTextFrame ("TDRC"));
		Assert.AreEqual (timestamp, vorbisTarget.Year);
	}

	[TestMethod]
	public void CopyTo_V24Date_ReplacesSameYearTimestamp ()
	{
		// Copying only the four-digit year would leave the target's May timestamp.
		var source = new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-12-25"
		};
		var target = new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-05-01"
		};

		source.CopyTo (target);

		Assert.AreEqual ("2024-12-25", target.GetTextFrame ("TDRC"));
		Assert.AreEqual ("2024", target.Year);
	}

	[TestMethod]
	public void CopyTo_CombinedV24Date_ReplacesSameYearTimestamp ()
	{
		var source = new CombinedTag (new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-12-25"
		});
		var target = new Id3v2Tag (Id3v2Version.V24) {
			Year = "2024-05-01"
		};

		source.CopyTo (target);

		Assert.AreEqual ("2024-12-25", target.GetTextFrame ("TDRC"));
	}

	[TestMethod]
	[DataRow (" 13")]
	[DataRow ("+13")]
	[DataRow ("-1")]
	[DataRow (" 199")]
	[DataRow ("+199")]
	public void Read_YearWithSignOrSpace_IsNotAYear (string raw)
	{
		// A numeric string is digits only. A sign or surrounding space is not a year.
		var v23 = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Year, raw, TestConstants.Id3v2.Version3));
		var v24 = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.RecordingTime, raw, TestConstants.Id3v2.Version4));

		Assert.IsTrue (v23.IsSuccess);
		Assert.IsNull (v23.Tag!.Year, raw);
		Assert.IsTrue (v24.IsSuccess);
		Assert.IsNull (v24.Tag!.Year, raw);
	}

	[TestMethod]
	[DataRow (" 13")]
	[DataRow ("+13")]
	[DataRow ("-1")]
	public void Read_GenreWithSignOrSpace_IsNotAnIndex (string raw)
	{
		// "(13)" and v2.4 "13" are genre 13 (Pop). A sign or space is not that index.
		var bare = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Genre, raw, TestConstants.Id3v2.Version4));
		var wrapped = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Genre, "(" + raw + ")", TestConstants.Id3v2.Version3));

		Assert.IsTrue (bare.IsSuccess);
		Assert.AreEqual (raw, bare.Tag!.Genre);
		Assert.IsFalse (bare.Tag.Genres.Contains ("Pop"));

		Assert.IsTrue (wrapped.IsSuccess);
		Assert.AreEqual ("(" + raw + ")", wrapped.Tag!.Genre);
		Assert.IsFalse (wrapped.Tag.Genres.Contains ("Pop"));
	}

	[TestMethod]
	public void Comment_ItunesToolFramesOnly_IsNull ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddComment (new CommentFrame ("0", "eng", "iTunPGAP"));
		tag.AddComment (new CommentFrame ("00000000 00000000", "eng", "iTunNORM"));
		tag.AddComment (new CommentFrame ("00000000 00000840", "eng", "iTunSMPB"));

		Assert.IsNull (tag.Comment);

		tag.AddComment (new CommentFrame ("Liner notes", "eng", "Description"));

		Assert.AreEqual ("Liner notes", tag.Comment);
	}

	[TestMethod]
	public void Comment_DescriptionStartingWithItun_IsIgnored ()
	{
		var ignored = new Id3v2Tag (Id3v2Version.V24);
		ignored.AddComment (new CommentFrame ("secret", "eng", "iTunXYZ"));
		Assert.IsNull (ignored.Comment);

		var kept = new Id3v2Tag (Id3v2Version.V24);
		kept.AddComment (new CommentFrame ("visible", "eng", "Liner"));
		Assert.AreEqual ("visible", kept.Comment);
	}

	[TestMethod]
	public void Read_TagWithTrack_ParsesTrack ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Track, "5/12", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5u, result.Tag!.Track);
	}

	[TestMethod]
	public void Read_TagWithGenre_ParsesGenre ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "Rock", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Rock", result.Tag!.Genre);
	}

	[TestMethod]
	public void Read_TagWithNumericGenreReference_ResolvesId3v1Name ()
	{
		// ID3v2.3 TCON "(13)" is genre 13 in the ID3v1 list, which is Pop.
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(13)", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Pop", result.Tag!.Genre);
	}

	[TestMethod]
	public void Read_MultipleGenreReferences_ResolvesEachName ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(51)(39)", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreNotEqual ("(39)", result.Tag!.Genre);
		Assert.AreEqual ("Techno-Industrial", result.Tag.Genre);
		Assert.HasCount (2, result.Tag.Genres);
		Assert.AreEqual ("Techno-Industrial", result.Tag.Genres[0]);
		Assert.AreEqual ("Noise", result.Tag.Genres[1]);
	}

	[TestMethod]
	public void Read_EscapedGenreRefinement_IsNotAnotherReference ()
	{
		// "((" starts a refinement whose text begins with '(', not a second reference.
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(55)((I think...)", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("(I think...)", result.Tag!.Genre);
		Assert.HasCount (2, result.Tag.Genres);
		Assert.AreEqual ("Dream", result.Tag.Genres[0]);
		Assert.AreEqual ("(I think...)", result.Tag.Genres[1]);
	}

	[TestMethod]
	public void Read_GenreReferenceWithRefinement_IncludesParentAndRefinement ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(4)Eurodisco", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Eurodisco", result.Tag!.Genre);
		Assert.HasCount (2, result.Tag.Genres);
		Assert.AreEqual ("Disco", result.Tag.Genres[0]);
		Assert.AreEqual ("Eurodisco", result.Tag.Genres[1]);
	}

	[TestMethod]
	public void Read_RemixAndCoverTokens_ResolveNames ()
	{
		var remix = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(RX)", TestConstants.Id3v2.Version3));
		var cover = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(CR)", TestConstants.Id3v2.Version3));

		Assert.IsTrue (remix.IsSuccess);
		Assert.AreEqual ("Remix", remix.Tag!.Genre);
		Assert.HasCount (1, remix.Tag.Genres);
		Assert.AreEqual ("Remix", remix.Tag.Genres[0]);

		Assert.IsTrue (cover.IsSuccess);
		Assert.AreEqual ("Cover", cover.Tag!.Genre);
		Assert.HasCount (1, cover.Tag.Genres);
		Assert.AreEqual ("Cover", cover.Tag.Genres[0]);
	}

	[TestMethod]
	public void Read_V24NumericGenre_ResolvesId3v1Name ()
	{
		var numeric = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "13", TestConstants.Id3v2.Version4));
		var freeText = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "Industrial Metal", TestConstants.Id3v2.Version4));

		Assert.IsTrue (numeric.IsSuccess);
		Assert.AreEqual ("Pop", numeric.Tag!.Genre);
		Assert.HasCount (1, numeric.Tag.Genres);
		Assert.AreEqual ("Pop", numeric.Tag.Genres[0]);

		Assert.IsTrue (freeText.IsSuccess);
		Assert.AreEqual ("Industrial Metal", freeText.Tag!.Genre);
		Assert.HasCount (1, freeText.Tag.Genres);
		Assert.AreEqual ("Industrial Metal", freeText.Tag.Genres[0]);
	}

	[TestMethod]
	public void Read_ZeroLengthFrame_KeepsTheFramesAfterIt ()
	{
		var title = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Kept", TestConstants.Id3v2.Version3);
		var zero = new byte[10];
		"WOAF"u8.CopyTo (zero);
		var frames = new byte[zero.Length + title.Length];
		zero.CopyTo (frames, 0);
		title.CopyTo (frames, zero.Length);
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, (uint)frames.Length);
		var data = new byte[header.Length + frames.Length];
		header.CopyTo (data, 0);
		frames.CopyTo (data, header.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Kept", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_NoMagic_ReturnsNotFound ()
	{
		var data = new byte[] { (byte)'X', (byte)'Y', (byte)'Z', 4, 0, 0, 0, 0, 0, 0 };

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsNotFound);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { (byte)'I', (byte)'D', (byte)'3' };

		var result = Id3v2Tag.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_TagWithPadding_StopsAtPadding ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithPadding (TestConstants.Id3v2.Version4, 50);
		// Add a frame before the padding
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Title", TestConstants.Id3v2.Version4);
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, (uint)(frame.Length + 50));
		var fullData = new byte[header.Length + frame.Length + 50];
		Array.Copy (header, fullData, header.Length);
		Array.Copy (frame, 0, fullData, header.Length, frame.Length);

		var result = Id3v2Tag.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Tag!.Title);
	}



	[TestMethod]
	public void Read_V23_UsesBigEndianFrameSize ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V24_UsesSyncsafeFrameSize ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}



	[TestMethod]
	public void Frames_ReturnsAllFrames ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithFrames (TestConstants.Id3v2.Version4,
			(TestConstants.FrameIds.Title, "Title"),
			(TestConstants.FrameIds.Artist, "Artist"),
			(TestConstants.FrameIds.Album, "Album"));

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsGreaterThanOrEqualTo (3, result.Tag!.Frames.Count);
	}

	[TestMethod]
	public void GetTextFrame_ExistingFrame_ReturnsValue ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, TestConstants.Metadata.Title, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.GetTextFrame (TestConstants.FrameIds.Title));
	}

	[TestMethod]
	public void GetTextFrame_NonExistingFrame_ReturnsNull ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.GetTextFrame (TestConstants.FrameIds.Title));
	}



	[TestMethod]
	public void Title_SetValue_CreatesOrUpdatesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Title = "New Title";

		Assert.AreEqual ("New Title", tag.Title);
		Assert.AreEqual ("New Title", tag.GetTextFrame (TestConstants.FrameIds.Title));
	}

	[TestMethod]
	public void Artist_SetValue_CreatesOrUpdatesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Artist = "New Artist";

		Assert.AreEqual ("New Artist", tag.Artist);
		Assert.AreEqual ("New Artist", tag.GetTextFrame (TestConstants.FrameIds.Artist));
	}

	[TestMethod]
	public void Clear_RemovesAllFrames ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) {
			Title = "Title",
			Artist = "Artist"
		};

		tag.Clear ();

		Assert.IsTrue (tag.IsEmpty);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Artist);
	}

	[TestMethod]
	public void IsEmpty_NoFrames_ReturnsTrue ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_IsFalseForEverySettableMember_AndTrueAfterClear ()
	{
		// IsEmpty decides whether WAV and AIFF write the ID3 chunk at all, so it must see
		// every frame kind, not only the basic fields the Tag base class checks.
		foreach (var p in TagMemberSamples.Settable) {
			var tag = new Id3v2Tag (Id3v2Version.V24);
			p.SetValue (tag, TagMemberSamples.Sample (p));
			Assert.IsFalse (tag.IsEmpty, p.Name);
			tag.Clear ();
			Assert.IsTrue (tag.IsEmpty, p.Name + " after Clear");
		}

		var rated = new Id3v2Tag (Id3v2Version.V24);
		rated.SetPopularimeter ("user@example.com", 196, 3);
		Assert.IsFalse (rated.IsEmpty, "POPM");
	}

	[TestMethod]
	public void IsEmpty_WithFrames_ReturnsFalse ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "X" };

		Assert.IsFalse (tag.IsEmpty);
	}



	[TestMethod]
	public void Render_EmptyTag_CreatesValidHeader ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		var data = tag.Render ();

		// Should have at least header (10 bytes)
		Assert.IsGreaterThanOrEqualTo (10, data.Length);
		Assert.IsTrue (data.StartsWith ("ID3"u8));
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist,
			Album = TestConstants.Metadata.Album
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Title, result.Tag!.Title);
		Assert.AreEqual (original.Artist, result.Tag.Artist);
		Assert.AreEqual (original.Album, result.Tag.Album);
	}

	[TestMethod]
	public void Render_WithPadding_IncludesPadding ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "X" };

		var data = tag.Render (paddingSize: 100);

		// Should include padding in total size
		Assert.IsGreaterThan (100, data.Length);
	}

	// ═══════════════════════════════════════════════════════════════
	// Footer Tests (TDD - ID3v2.4 footer support)
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_WithFooter_Includes10ByteFooter ()
	{
		// ID3v2.4 only - footer is 10 bytes with "3DI" magic
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Test" };

		var dataWithoutFooter = tag.Render (paddingSize: 0, withFooter: false);
		var dataWithFooter = tag.Render (paddingSize: 0, withFooter: true);

		// Footer adds exactly 10 bytes
		Assert.AreEqual (dataWithoutFooter.Length + 10, dataWithFooter.Length);
	}

	[TestMethod]
	public void Render_WithFooter_EndsWithMagic3DI ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Test" };

		var data = tag.Render (paddingSize: 0, withFooter: true);

		// Footer magic "3DI" at end
		Assert.AreEqual ((byte)'3', data[data.Length - 10]);
		Assert.AreEqual ((byte)'D', data[data.Length - 9]);
		Assert.AreEqual ((byte)'I', data[data.Length - 8]);
	}

	[TestMethod]
	public void Render_WithFooter_SetsFooterFlagInHeader ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Test" };

		var data = tag.Render (paddingSize: 0, withFooter: true);

		// Header flags at offset 5, footer flag is 0x10 (bit 4)
		Assert.IsTrue ((data[5] & 0x10) != 0, "Footer flag should be set in header");
	}

	[TestMethod]
	public void Render_WithFooter_RoundTripPreservesData ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Title = "Test Title",
			Artist = "Test Artist"
		};

		var rendered = original.Render (paddingSize: 0, withFooter: true);
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Title", result.Tag!.Title);
		Assert.AreEqual ("Test Artist", result.Tag.Artist);
	}

	[TestMethod]
	public void Render_V23WithFooter_DoesNotIncludeFooter ()
	{
		// Footer is only valid for v2.4, v2.3 should ignore
		var tag = new Id3v2Tag (Id3v2Version.V23) { Title = "Test" };

		var dataWithoutFooter = tag.Render (paddingSize: 0, withFooter: false);
		var dataWithFooter = tag.Render (paddingSize: 0, withFooter: true);

		// V2.3 should NOT add footer even when requested
		Assert.AreEqual (dataWithoutFooter.Length, dataWithFooter.Length);
	}

	[TestMethod]
	public void AlbumArtist_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AlbumArtist = "Various Artists";

		Assert.AreEqual ("Various Artists", tag.AlbumArtist);
		Assert.AreEqual ("Various Artists", tag.GetTextFrame (TestConstants.FrameIds.AlbumArtist));
	}

	[TestMethod]
	public void AlbumArtist_FromFile_ParsesCorrectly ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.AlbumArtist, "Compilation Artist", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Compilation Artist", result.Tag!.AlbumArtist);
	}

	[TestMethod]
	public void DiscNumber_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.DiscNumber = 2;

		Assert.AreEqual (2u, tag.DiscNumber);
		Assert.AreEqual ("2", tag.GetTextFrame (TestConstants.FrameIds.DiscNumber));
	}

	[TestMethod]
	public void DiscNumber_WithSlashFormat_ParsesCorrectly ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.DiscNumber, "2/3", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2u, result.Tag!.DiscNumber);
	}

	[TestMethod]
	public void DiscNumber_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.DiscNumber = 2;

		tag.DiscNumber = null;

		Assert.IsNull (tag.DiscNumber);
		Assert.IsNull (tag.GetTextFrame (TestConstants.FrameIds.DiscNumber));
	}

	[TestMethod]
	public void Composer_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Composer = "Johann Sebastian Bach";

		Assert.AreEqual ("Johann Sebastian Bach", tag.Composer);
		Assert.AreEqual ("Johann Sebastian Bach", tag.GetTextFrame (TestConstants.FrameIds.Composer));
	}

	[TestMethod]
	public void BeatsPerMinute_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.BeatsPerMinute = 120;

		Assert.AreEqual (120u, tag.BeatsPerMinute);
		Assert.AreEqual ("120", tag.GetTextFrame (TestConstants.FrameIds.Bpm));
	}



	[TestMethod]
	public void Read_V24_WithExtendedHeader_SkipsExtendedHeader ()
	{
		// Create a v2.4 tag with extended header flag set
		var header = new byte[10];
		header[0] = (byte)'I';
		header[1] = (byte)'D';
		header[2] = (byte)'3';
		header[3] = 4; // v2.4
		header[4] = 0; // revision
		header[5] = 0x40; // Extended header flag

		// Extended header: 6 bytes total (syncsafe size includes itself)
		var extHeader = new byte[6];
		extHeader[0] = 0x00;
		extHeader[1] = 0x00;
		extHeader[2] = 0x00;
		extHeader[3] = 0x06;
		extHeader[4] = 0x01; // number of flag bytes
		extHeader[5] = 0x00; // flags

		// Frame: TIT2 with "Test"
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version4);

		// Tag size (syncsafe): extended header + frame
		var tagSize = (uint)(extHeader.Length + frame.Length);
		header[6] = (byte)((tagSize >> 21) & 0x7F);
		header[7] = (byte)((tagSize >> 14) & 0x7F);
		header[8] = (byte)((tagSize >> 7) & 0x7F);
		header[9] = (byte)(tagSize & 0x7F);

		var data = new byte[header.Length + extHeader.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (extHeader, 0, data, header.Length, extHeader.Length);
		Array.Copy (frame, 0, data, header.Length + extHeader.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V23_WithExtendedHeader_SkipsExtendedHeader ()
	{
		// Create a v2.3 tag with extended header flag set
		var header = new byte[10];
		header[0] = (byte)'I';
		header[1] = (byte)'D';
		header[2] = (byte)'3';
		header[3] = 3; // v2.3
		header[4] = 0; // revision
		header[5] = 0x40; // Extended header flag

		// Extended header: size(4) + flags(2) + padding(4) = 10 bytes
		var extHeader = new byte[10];
		extHeader[0] = 0x00;
		extHeader[1] = 0x00;
		extHeader[2] = 0x00;
		extHeader[3] = 0x06; // 6 bytes after size field
		extHeader[4] = 0x00; // flags byte 1
		extHeader[5] = 0x00; // flags byte 2
		extHeader[6] = 0x00; // padding size
		extHeader[7] = 0x00;
		extHeader[8] = 0x00;
		extHeader[9] = 0x00;

		// Frame: TIT2 with "Test"
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version3);

		// Tag size (syncsafe): extended header + frame
		var tagSize = (uint)(extHeader.Length + frame.Length);
		header[6] = (byte)((tagSize >> 21) & 0x7F);
		header[7] = (byte)((tagSize >> 14) & 0x7F);
		header[8] = (byte)((tagSize >> 7) & 0x7F);
		header[9] = (byte)(tagSize & 0x7F);

		var data = new byte[header.Length + extHeader.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (extHeader, 0, data, header.Length, extHeader.Length);
		Array.Copy (frame, 0, data, header.Length + extHeader.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}



	[TestMethod]
	public void Read_V23_FrameSizeWithHighByte0x80_DoesNotOverflow ()
	{
		// This test verifies the fix for integer overflow bug.
		// When data[0] = 0x80, the expression (data[0] << 24) produces -2147483648
		// (negative) instead of 2147483648 (positive) due to signed integer promotion.
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 20);
		var frame = new byte[20];

		// Frame ID "TIT2"
		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size with MSB set: 0x80 0x00 0x00 0x05 = 2,147,483,653 bytes
		frame[4] = 0x80;
		frame[5] = 0x00;
		frame[6] = 0x00;
		frame[7] = 0x05;

		// Flags
		frame[8] = 0x00;
		frame[9] = 0x00;

		// Frame content (won't be reached due to size overflow)
		frame[10] = 0x00;
		frame[11] = (byte)'X';

		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		// Should succeed with no frames parsed, NOT crash or throw.
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}

	[TestMethod]
	public void Read_V23_FrameSizeWithHighByte0xFF_DoesNotOverflow ()
	{
		// Test with highest possible byte value (0xFF)
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 20);
		var frame = new byte[20];

		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size: 0xFF 0xFF 0xFF 0x05 = 4,294,967,045
		frame[4] = 0xFF;
		frame[5] = 0xFF;
		frame[6] = 0xFF;
		frame[7] = 0x05;

		frame[8] = 0x00;
		frame[9] = 0x00;
		frame[10] = 0x00;
		frame[11] = (byte)'X';

		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		// Should not crash; frame size exceeds data, so no frames parsed
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}

	[TestMethod]
	public void Read_V23_NormalFrameSize_ParsesCorrectly ()
	{
		// Baseline test: Normal frame size should still work
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 20);
		var frame = new byte[20];

		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size: 0x00 0x00 0x00 0x05 = 5 bytes (normal)
		frame[4] = 0x00;
		frame[5] = 0x00;
		frame[6] = 0x00;
		frame[7] = 0x05;

		frame[8] = 0x00;
		frame[9] = 0x00;

		// Frame content: encoding (Latin-1) + "Test"
		frame[10] = 0x00;
		frame[11] = (byte)'T';
		frame[12] = (byte)'e';
		frame[13] = (byte)'s';
		frame[14] = (byte)'t';

		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_TruncatedFile_HandlesGracefully ()
	{
		// Create a tag that claims to be 1000 bytes but only has 50 bytes of data.
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 1000);

		// Add a valid frame that fits in the truncated data
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version4);

		// Total data is header (10) + frame (~16), but header claims 1000 bytes
		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		// Should succeed and parse the frame that's actually present
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_HeaderClaimsLargeSize_NoFramesAvailable ()
	{
		// Header claims 10000 bytes but there's no frame data at all
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 10000);

		var result = Id3v2Tag.Read (data);

		// Should succeed with no frames (header-only is valid, just empty)
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}
}
