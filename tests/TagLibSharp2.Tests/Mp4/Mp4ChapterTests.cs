// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// MP4 chapters: the Nero chpl box and the QuickTime chapter track.
/// Invariant: every chapter surface (chpl, the chapter track, its tref/chap references) describes the same
/// list after a save, and every other track's samples stay byte-identical at their (moved) offsets.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4ChapterTests
{
	static readonly TimeSpan Movie = TimeSpan.FromSeconds (10);
	static readonly string[] OneTwo = ["One", "Two"];
	static readonly string[] WithChapterData = ["ftyp", "mdat", "moov", "mdat"];
	static readonly string[] WithoutChapterData = ["ftyp", "mdat", "moov"];
	static readonly string[] WithoutChapterDataMoovFirst = ["ftyp", "moov", "mdat"];

	// Positions among moov's traks in the built fixtures: the audio trak, then the chapter text trak.
	const int AudioTrak = 0;
	const int TextTrak = 1;

	static Mp4ChapterTestBuilder.Options ThreeChapterTrack () => new () {
		Track = [("Intro", 900), ("Cafè über 日本", 1200), ("Outro", 3900)],
	};

	static (TimeSpan, string)[] ThreeChpl () =>
		[(TimeSpan.Zero, "Intro"), (TimeSpan.FromSeconds (1.5), "Cafè über 日本"), (TimeSpan.FromSeconds (3.5), "Outro")];

	static void AssertChapters (IReadOnlyList<Mp4Chapter> actual, params (double Start, string Title)[] expected)
	{
		Assert.AreEqual (expected.Length, actual.Count, string.Join (", ", actual));
		for (var i = 0; i < expected.Length; i++) {
			Assert.AreEqual (TimeSpan.FromSeconds (expected[i].Start), actual[i].StartTime, $"start {i}");
			Assert.AreEqual (expected[i].Title, actual[i].Title, $"title {i}");
		}
	}

	static byte[] Save (Mp4File file, byte[] original)
	{
		var rendered = file.Render (original);
		Assert.IsFalse (rendered.IsEmpty, "render succeeded");
		return rendered.ToArray ();
	}

	// ---- read

	[TestMethod]
	public void Read_NoChapters_IsEmpty ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (new ()));

		Assert.AreEqual (0, file.Chapters.Count);
	}

	[TestMethod]
	public void Read_ChplOnly_ReturnsEntriesWithDerivedEndTimes ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (new () { Chpl = ThreeChpl () }));

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
		Assert.AreEqual (TimeSpan.FromSeconds (1.5), file.Chapters[0].EndTime);
		Assert.AreEqual (TimeSpan.FromSeconds (3.5), file.Chapters[1].EndTime);
		Assert.AreEqual (Movie, file.Chapters[2].EndTime, "last chapter ends with the movie");
	}

	[TestMethod]
	public void Read_ChplOutOfOrder_IsSortedByStart ()
	{
		var options = new Mp4ChapterTestBuilder.Options {
			Chpl = [(TimeSpan.FromSeconds (5), "B"), (TimeSpan.FromSeconds (1), "A")],
		};

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (1, "A"), (5, "B"));
		Assert.AreEqual (TimeSpan.FromSeconds (5), file.Chapters[0].EndTime);
	}

	[TestMethod]
	public void Read_ChplKeeps100NanosecondStartTimes ()
	{
		var start = TimeSpan.FromTicks (12_345_678);
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (new () { Chpl = [(start, "A")] }));

		Assert.AreEqual (start, file.Chapters[0].StartTime);
	}

	[TestMethod]
	public void Read_ChplVersion0WithoutReservedWord_ReadsEntries ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (new () { Chpl = ThreeChpl (), ChplVersion = 0 }));

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
	}

	[TestMethod]
	public void Read_TextTrackOnly_ReturnsSamplesTimedByStts ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (ThreeChapterTrack ()));

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
		Assert.AreEqual (TimeSpan.FromSeconds (10), file.Chapters[2].EndTime, "track end");
	}

	[TestMethod]
	public void Read_TextTrackUtf16WithBom_DecodesTitles ()
	{
		var options = ThreeChapterTrack ();
		options.Utf16 = true;

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
	}

	[TestMethod]
	public void Read_SubtitleHandlerChapterTrack_IsRead ()
	{
		var options = ThreeChapterTrack ();
		options.TextHandler = "sbtl";

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		Assert.AreEqual (3, file.Chapters.Count);
	}

	[TestMethod]
	public void Read_ChapReferenceToNonTextTrack_IsNotAChapterTrack ()
	{
		var options = ThreeChapterTrack ();
		options.TextHandler = "vide";

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		Assert.AreEqual (0, file.Chapters.Count);
	}

	[TestMethod]
	public void Read_EmptyEdit_DelaysTrackChapters ()
	{
		var options = ThreeChapterTrack ();
		options.EmptyEdit = 2000;

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (2, "Intro"), (3.5, "Cafè über 日本"), (5.5, "Outro"));
	}

	[TestMethod]
	public void Read_BothFormsSameTitles_UsesChplStartTimes ()
	{
		var options = new Mp4ChapterTestBuilder.Options {
			Track = [("Late", 1800), ("Later", 4200)],
			Chpl = [(TimeSpan.FromSeconds (2), "Late"), (TimeSpan.FromSeconds (3), "Later")],
		};

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (2, "Late"), (3, "Later"));
	}

	[TestMethod]
	public void Read_BothFormsDifferentTitles_UsesTextTrack ()
	{
		var options = ThreeChapterTrack ();
		options.Chpl = [(TimeSpan.FromSeconds (4), "Stale")];

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
	}

	[TestMethod]
	public void Read_TextSampleOutsideFile_FallsBackToChpl ()
	{
		var options = ThreeChapterTrack ();
		options.Chpl = [(TimeSpan.FromSeconds (4), "From chpl")];
		options.TextChunkOffset = 0x7FFF_0000;

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (4, "From chpl"));
	}

	[TestMethod]
	public void Read_TextSampleOutsideFileWithoutChpl_KeepsTimingWithEmptyTitles ()
	{
		var options = ThreeChapterTrack ();
		options.TextChunkOffset = 0x7FFF_0000;

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (0, ""), (1.5, ""), (3.5, ""));
	}

	[TestMethod]
	public void Read_TextTrackCo64_ReadsSamples ()
	{
		var options = ThreeChapterTrack ();
		options.TextCo64 = true;

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
	}

	[TestMethod]
	public void Read_FromStream_MatchesSpanRead ()
	{
		foreach (var moovFirst in new[] { false, true }) {
			var options = ThreeChapterTrack ();
			options.MoovFirst = moovFirst;
			var data = Mp4ChapterTestBuilder.Build (options);

			using var stream = new MemoryStream (data);
			var result = MediaFile.Read (stream, "x.m4a");

			Assert.IsTrue (result.IsSuccess, result.Error);
			var chapters = ((Mp4File)result.File!).Chapters;
			CollectionAssert.AreEqual (Mp4ChapterTestBuilder.ReadFile (data).Chapters.ToArray (), chapters.ToArray (), $"moovFirst={moovFirst}");
			AssertChapters (chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
		}
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ReadsChapters ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.m4a", Mp4ChapterTestBuilder.Build (ThreeChapterTrack ()));

		var result = await Mp4File.ReadFromFileAsync ("/a.m4a", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (3, result.File!.Chapters.Count);
	}

	[TestMethod]
	public void DecodeSample_LengthPastSample_StopsAtSampleEnd ()
	{
		byte[] sample = [0x00, 0x10, (byte)'a', (byte)'b'];

		Assert.AreEqual ("ab", Mp4ChapterCodec.DecodeSample (sample));
		Assert.AreEqual ("", Mp4ChapterCodec.DecodeSample ([0x00]));
	}

	// ---- write

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_NewChapters_WritesChplTrackAndReference (bool moovFirst)
	{
		var original = Mp4ChapterTestBuilder.Build (new () { MoovFirst = moovFirst });
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "One"), new Mp4Chapter (TimeSpan.FromSeconds (2.5), "Two ü")];
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		var ids = Mp4ChapterTestBuilder.TrackIds (moov);
		CollectionAssert.AreEqual (new uint[] { 1, 2 }, ids, "new track takes the next id");
		CollectionAssert.AreEqual (new uint[] { 2 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		var chpl = Mp4ChapterCodec.ReadChpl (moov)!;
		AssertChapters (chpl, (0, "One"), (2.5, "Two ü"));
		var audio = Mp4ChapterTestBuilder.SampleBytes (saved, 1);
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), audio, "audio samples moved with their offsets");
		AssertChapters (Mp4ChapterTestBuilder.ReadFile (saved).Chapters, (0, "One"), (2.5, "Two ü"));
	}

	[TestMethod]
	public void Save_NewChapters_TrackOnlyRead_MatchesChpl ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "One"), new Mp4Chapter (TimeSpan.FromSeconds (2.5), "Two")];
		var saved = Save (file, original);

		// Read the chapter track alone: its samples, its stts timing, its new mdat.
		var moov = Mp4ChapterTestBuilder.Moov (saved);
		var track = Mp4ChapterCodec.FindChapterTrack (moov)!;
		var layout = Mp4ChapterCodec.ReadLayout (track, Mp4ChapterTestBuilder.MovieTimescale, saved.Length)!;
		var titles = layout.Samples.Select (s => Mp4ChapterCodec.DecodeSample (saved.AsSpan ((int)s.Offset, s.ReadLength))).ToArray ();

		CollectionAssert.AreEqual (OneTwo, titles);
		Assert.AreEqual (TimeSpan.Zero, layout.StartOf (0));
		Assert.AreEqual (TimeSpan.FromSeconds (2.5), layout.StartOf (1));
		var last = layout.Samples[^1];
		Assert.AreEqual (Movie, layout.TimeOf (last.Start + last.Duration), "the track runs to the movie end");
		CollectionAssert.AreEqual (WithChapterData, Mp4ChapterTestBuilder.TopLevelTypes (saved));
	}

	[TestMethod]
	public void Save_NewChapters_TrackIsDisabledTextTrackWithSelfContainedData ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "One")];

		var trak = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (Save (file, original)), 2)!;

		var tkhdFlags = BinaryPrimitives.ReadUInt32BigEndian (trak.FindChild ("tkhd")!.Data.Span) & 0xFFFFFF;
		Assert.AreEqual (0u, tkhdFlags & 1, "not enabled");
		Assert.AreEqual ("text", Encoding.ASCII.GetString (trak.FindDescendant ("mdia", "hdlr")!.Data.Span.Slice (8, 4)));
		var dref = trak.FindDescendant ("mdia", "minf", "dinf")!.Data.Span;
		Assert.AreEqual (1u, BinaryPrimitives.ReadUInt32BigEndian (dref.Slice (8 + 8 + 8)) & 1, "url self-contained");
	}

	[TestMethod]
	public void Save_ReplaceChapters_KeepsLanguageHandlerAndTrackFlags ()
	{
		// "eng" packed as three 5-bit letters (§8.4.2): ('e'-0x60)<<10 | ('n'-0x60)<<5 | ('g'-0x60).
		const ushort English = (5 << 10) | (14 << 5) | 7;
		var options = ThreeChapterTrack ();
		options.TextLanguage = English;
		options.TextHandlerName = "Chapter titles";
		options.TextTkhdFlags = 0x6;
		var original = Mp4ChapterTestBuilder.Build (options);
		var before = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (original), 2)!;
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A"), new Mp4Chapter (TimeSpan.FromSeconds (2), "B")];
		var trak = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (Save (file, original)), 2)!;

		var mdhd = trak.FindDescendant ("mdia", "mdhd")!.Data.Span;
		Assert.AreEqual (English, BinaryPrimitives.ReadUInt16BigEndian (mdhd.Slice (20)), "language");
		CollectionAssert.AreEqual (before.FindDescendant ("mdia", "hdlr")!.Data.ToArray (), trak.FindDescendant ("mdia", "hdlr")!.Data.ToArray (), "hdlr");
		Assert.AreEqual (0x6u, BinaryPrimitives.ReadUInt32BigEndian (trak.FindChild ("tkhd")!.Data.Span) & 0xFFFFFF, "tkhd flags");
	}

	[TestMethod]
	public void Save_NewChapters_Mp4BrandWritesNmhdAndTx3g ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var minf = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (Save (file, original)), 2)!.FindDescendant ("mdia", "minf")!;

		Assert.IsNotNull (minf.FindChild ("nmhd"), "TN2174: the null media header");
		Assert.IsNull (minf.FindChild ("gmhd"));
		var stsd = minf.FindDescendant ("stbl", "stsd")!.Data.Span;
		Assert.AreEqual ("tx3g", Encoding.ASCII.GetString (stsd.Slice (12, 4)), "3GPP TextSampleEntry");
	}

	[TestMethod]
	public void Save_NewChapters_NoFtypWritesTheQuickTimeLayout ()
	{
		// ISO files must start with ftyp (ISO 14496-12 §4.3); a QuickTime movie may not have one. The ftyp is
		// turned into a free box of the same size, so every offset stays valid.
		var data = Mp4ChapterTestBuilder.Build (new ());
		"free"u8.CopyTo (data.AsSpan (4));
		var file = Mp4ChapterTestBuilder.ReadFile (data);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var minf = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (Save (file, data)), 2)!.FindDescendant ("mdia", "minf")!;

		Assert.IsNotNull (minf.FindChild ("gmhd"));
		Assert.AreEqual ("text", Encoding.ASCII.GetString (minf.FindDescendant ("stbl", "stsd")!.Data.Span.Slice (12, 4)));
	}

	[TestMethod]
	public void Save_NewChapters_QuickTimeBrandWritesGmhdAndQtffTextDescription ()
	{
		var original = Mp4ChapterTestBuilder.Build (new () { MajorBrand = "qt  " });
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var minf = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (Save (file, original)), 2)!.FindDescendant ("mdia", "minf")!;

		var gmhd = minf.FindChild ("gmhd")!.Data.Span;
		Assert.AreEqual ("gmin", Encoding.ASCII.GetString (gmhd.Slice (4, 4)));
		Assert.IsNull (minf.FindChild ("nmhd"));
		var stsd = minf.FindDescendant ("stbl", "stsd")!.Data.Span;
		Assert.AreEqual ("text", Encoding.ASCII.GetString (stsd.Slice (12, 4)));
		// Entry: size, format, 6 reserved, data_reference_index, 43 bytes of fields, an empty Pascal font name.
		Assert.AreEqual (16 + 43 + 1, (int)BinaryPrimitives.ReadUInt32BigEndian (stsd.Slice (8)));
		Assert.AreEqual (0, stsd[8 + 16 + 43], "text name length");
	}

	[TestMethod]
	public void Save_NewChapters_BumpsMvhdNextTrackId ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "One")];

		var mvhd = Mp4ChapterTestBuilder.Moov (Save (file, original)).FindChild ("mvhd")!.Data.Span;

		Assert.AreEqual (3u, BinaryPrimitives.ReadUInt32BigEndian (mvhd.Slice (96)));
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_ReplaceChapters_KeepsTrackIdAndReferences (bool moovFirst)
	{
		var options = ThreeChapterTrack ();
		options.MoovFirst = moovFirst;
		options.Chpl = ThreeChpl ();
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = [
			new Mp4Chapter (TimeSpan.Zero, "A"),
			new Mp4Chapter (TimeSpan.FromSeconds (4), "B"),
			new Mp4Chapter (TimeSpan.FromSeconds (6), "C"),
			new Mp4Chapter (TimeSpan.FromSeconds (8), "D"),
		];
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		CollectionAssert.AreEqual (new uint[] { 1, 2 }, Mp4ChapterTestBuilder.TrackIds (moov));
		CollectionAssert.AreEqual (new uint[] { 2 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		AssertChapters (Mp4ChapterCodec.ReadChpl (moov)!, (0, "A"), (4, "B"), (6, "C"), (8, "D"));
		AssertChapters (Mp4ChapterTestBuilder.ReadFile (saved).Chapters, (0, "A"), (4, "B"), (6, "C"), (8, "D"));
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (saved, 1));
		Assert.AreEqual (1, moov.FindChild ("udta")!.Children.Count (c => c.Type == "chpl"), "one chpl");
	}

	[TestMethod]
	public void Save_ReplaceChapters_KeepsExistingTimescale ()
	{
		var original = Mp4ChapterTestBuilder.Build (ThreeChapterTrack ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var mdhd = Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (Save (file, original)), 2)!.FindDescendant ("mdia", "mdhd")!.Data.Span;

		Assert.AreEqual (600u, BinaryPrimitives.ReadUInt32BigEndian (mdhd.Slice (12)));
	}

	[TestMethod]
	public void Save_ChplOnlyFile_AddsChapterTrack ()
	{
		var original = Mp4ChapterTestBuilder.Build (new () { Chpl = ThreeChpl () });
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A"), new Mp4Chapter (TimeSpan.FromSeconds (5), "B")];

		var moov = Mp4ChapterTestBuilder.Moov (Save (file, original));

		Assert.IsNotNull (Mp4ChapterCodec.FindChapterTrack (moov));
		AssertChapters (Mp4ChapterCodec.ReadChpl (moov)!, (0, "A"), (5, "B"));
	}

	[TestMethod]
	public void Save_FirstChapterAfterZero_TrackStartsAtZeroAndChplKeepsStart ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.FromSeconds (2), "Late"), new Mp4Chapter (TimeSpan.FromSeconds (3), "Later")];
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		var layout = Mp4ChapterCodec.ReadLayout (Mp4ChapterCodec.FindChapterTrack (moov)!, 1000, saved.Length)!;
		Assert.AreEqual (TimeSpan.Zero, layout.StartOf (0), "the track covers the timeline from zero");
		Assert.AreEqual (TimeSpan.FromSeconds (3), layout.StartOf (1));
		AssertChapters (Mp4ChapterTestBuilder.ReadFile (saved).Chapters, (2, "Late"), (3, "Later"));
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_ClearChapters_RemovesChplTrackAndReferences (bool moovFirst)
	{
		var options = ThreeChapterTrack ();
		options.MoovFirst = moovFirst;
		options.Chpl = ThreeChpl ();
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = [];
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		CollectionAssert.AreEqual (new uint[] { 1 }, Mp4ChapterTestBuilder.TrackIds (moov));
		Assert.IsNull (Mp4ChapterTestBuilder.Trak (moov, 1)!.FindChild ("tref"), "the emptied tref is dropped");
		Assert.IsNull (moov.FindChild ("udta"), "the emptied udta is dropped");
		Assert.AreEqual (0, Mp4ChapterTestBuilder.ReadFile (saved).Chapters.Count);
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (saved, 1));
	}

	[TestMethod]
	public void Save_ClearChapters_KeepsOtherChapReferencesAndTitle ()
	{
		var options = ThreeChapterTrack ();
		options.ExtraChapReferences = [7];
		options.Title = "Kept";
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = null!;
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		CollectionAssert.AreEqual (new uint[] { 7 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		Assert.AreEqual ("Kept", Mp4ChapterTestBuilder.ReadFile (saved).Title);
	}

	[TestMethod]
	public void Save_ClearChapters_RemovesChapterImageAndUrlTracks ()
	{
		// Apple layout: chap names the titles (2), chapter images (3, video), and chapter URLs (4, text).
		// Apple players list chapters from any of them, so clearing removes all three and every chap entry.
		var options = ThreeChapterTrack ();
		options.ImageTrack = true;
		options.UrlTrack = true;
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = [];
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		CollectionAssert.AreEqual (new uint[] { 1 }, Mp4ChapterTestBuilder.TrackIds (moov));
		Assert.IsNull (Mp4ChapterTestBuilder.Trak (moov, 1)!.FindChild ("tref"), "no chap reference is left");
		Assert.AreEqual (0, Mp4ChapterTestBuilder.ReadFile (saved).Chapters.Count);
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (saved, 1));
	}

	[TestMethod]
	public void Save_ClearChapters_KeepsASoundTrackThatChapNames ()
	{
		// chap naming a sound track is not chapter media: clearing must not delete audio.
		var options = ThreeChapterTrack ();
		options.ChapSoundTrack = true;
		var data = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (data);

		file.Chapters = [];
		var moov = Mp4ChapterTestBuilder.Moov (Save (file, data));

		CollectionAssert.AreEqual (new uint[] { 1, Mp4ChapterTestBuilder.SoundTrackId }, Mp4ChapterTestBuilder.TrackIds (moov));
	}

	[TestMethod]
	public void Save_ClearChapters_KeepsATrackThatHoldsItsOwnChapReference ()
	{
		// chap names track 3, which itself references the chapter text track: it is a presentation track.
		var options = ThreeChapterTrack ();
		options.ImageTrack = true;
		var withRef = AddChapReference (Mp4ChapterTestBuilder.Build (options), Mp4ChapterTestBuilder.ImageTrackId, Mp4ChapterTestBuilder.TextTrackId);
		var file = Mp4ChapterTestBuilder.ReadFile (withRef);

		file.Chapters = [];
		var moov = Mp4ChapterTestBuilder.Moov (Save (file, withRef));

		CollectionAssert.AreEqual (new uint[] { 1, 3 }, Mp4ChapterTestBuilder.TrackIds (moov));
	}

	/// <summary>Gives <paramref name="trackId"/> a tref/chap naming <paramref name="target"/>, rebuilding moov.</summary>
	static byte[] AddChapReference (byte[] data, uint trackId, uint target)
	{
		var moov = Mp4ChapterTestBuilder.Moov (data);
		var moovAt = data.AsSpan ().IndexOf ("moov"u8) - 4;
		var children = moov.Children.SelectMany (c => {
			if (c.Type != "trak" || Mp4ChapterTestBuilder.Trak (moov, trackId) != c)
				return c.Render ().ToArray ();
			byte[] chap = [0, 0, 0, 12, (byte)'c', (byte)'h', (byte)'a', (byte)'p', 0, 0, 0, (byte)target];
			var inner = c.Children.SelectMany (k => k.Render ().ToArray ()).ToArray ();
			return Mp4TestBuilder.CreateBox ("trak", [.. Mp4TestBuilder.CreateBox ("tref", chap), .. inner]);
		}).ToArray ();
		var newMoov = Mp4TestBuilder.CreateMoovBox (children);
		var oldLength = moov.Render ().Length;
		Assert.AreEqual (data.Length, moovAt + oldLength, "the test layout has moov last");
		return [.. data.AsSpan (0, moovAt), .. newMoov];
	}

	[TestMethod]
	public void Save_NewTrack_AppendsToExistingChapReferences ()
	{
		// A chap reference to a missing track is not a chapter track. It stays, and the new track joins it.
		var options = new Mp4ChapterTestBuilder.Options { ExtraChapReferences = [9] };
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var moov = Mp4ChapterTestBuilder.Moov (Save (file, original));

		CollectionAssert.AreEqual (new uint[] { 9, 2 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
	}

	[TestMethod]
	public void Save_NewTrack_DanglingReferenceToItsId_IsNotDuplicated ()
	{
		// chap already names track 2, which does not exist. The new chapter track takes id 2.
		var original = Mp4ChapterTestBuilder.Build (new () { ExtraChapReferences = [2] });
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var moov = Mp4ChapterTestBuilder.Moov (Save (file, original));

		CollectionAssert.AreEqual (new uint[] { 1, 2 }, Mp4ChapterTestBuilder.TrackIds (moov));
		CollectionAssert.AreEqual (new uint[] { 2 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
	}

	[TestMethod]
	public void Save_NewTrack_LeavesChapterImageTrackUnreferenced ()
	{
		// Apple files name a video track of chapter images in chap. That track is chapter media, not a presentation
		// track: it keeps no tref, and the audio track keeps its reference to it next to the new chapter track.
		var original = Mp4ChapterTestBuilder.Build (new () { ImageTrack = true });
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var moov = Mp4ChapterTestBuilder.Moov (Save (file, original));

		CollectionAssert.AreEqual (new uint[] { 1, 3, 4 }, Mp4ChapterTestBuilder.TrackIds (moov));
		CollectionAssert.AreEqual (new uint[] { 3, 4 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		Assert.IsNull (Mp4ChapterTestBuilder.Trak (moov, 3)!.FindChild ("tref"));
	}

	[TestMethod]
	public void Save_ReplaceChapters_KeepsChapReferenceOrder ()
	{
		var options = ThreeChapterTrack ();
		options.ImageTrack = true;
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var moov = Mp4ChapterTestBuilder.Moov (Save (file, original));

		CollectionAssert.AreEqual (new uint[] { 2, 3 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		Assert.IsNull (Mp4ChapterTestBuilder.Trak (moov, 3)!.FindChild ("tref"));
	}

	[TestMethod]
	public void Save_MoreThan255Chapters_ChplHolds255TrackHoldsAll ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [.. Enumerable.Range (0, 300).Select (i => new Mp4Chapter (TimeSpan.FromMilliseconds (i * 30), $"C{i}"))];
		var saved = Save (file, original);

		Assert.AreEqual (255, Mp4ChapterCodec.ReadChpl (Mp4ChapterTestBuilder.Moov (saved))!.Count);
		var reread = Mp4ChapterTestBuilder.ReadFile (saved).Chapters;
		Assert.AreEqual (300, reread.Count);
		Assert.AreEqual ("C299", reread[299].Title);
		Assert.AreEqual (TimeSpan.FromMilliseconds (299 * 30), reread[299].StartTime);
	}

	[TestMethod]
	public void Save_LongTitle_ChplCutOnCharacterBoundaryTrackKeepsAll ()
	{
		var title = new string ('é', 200);
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.FromSeconds (1), title)];
		var saved = Save (file, original);

		var chpl = Mp4ChapterCodec.ReadChpl (Mp4ChapterTestBuilder.Moov (saved))!;
		Assert.AreEqual (new string ('é', 127), chpl[0].Title, "254 bytes; the 128th é would split");
		var reread = Mp4ChapterTestBuilder.ReadFile (saved).Chapters;
		Assert.AreEqual (title, reread[0].Title);
		Assert.AreEqual (TimeSpan.FromSeconds (1), reread[0].StartTime, "chpl matches the cut title, so its start is used");
	}

	// ---- fragmented movies (ISO 14496-12 §8.8)

	static Mp4ChapterTestBuilder.Options FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase mode) => new () {
		Track = [("Intro", 900), ("Middle ü", 1200), ("End 日本", 600)],
		Fragment = mode,
	};

	[TestMethod]
	[DataRow ((int)Mp4ChapterTestBuilder.FragmentBase.Moof)]
	[DataRow ((int)Mp4ChapterTestBuilder.FragmentBase.Explicit)]
	[DataRow ((int)Mp4ChapterTestBuilder.FragmentBase.PreviousTraf)]
	public void Read_FragmentedChapterTrack_ReadsSamplesFromMovieFragments (int mode)
	{
		var data = Mp4ChapterTestBuilder.Build (FragmentedTrack ((Mp4ChapterTestBuilder.FragmentBase)mode));

		var file = Mp4ChapterTestBuilder.ReadFile (data);

		AssertChapters (file.Chapters, (0, "Intro"), (1.5, "Middle ü"), (3.5, "End 日本"));
		Assert.AreEqual (TimeSpan.Zero, file.Duration, "mvhd duration is 0, as in an empty_moov file");
		Assert.AreEqual (TimeSpan.FromSeconds (4.5), file.Chapters[2].EndTime, "the last chapter ends with its fragment sample");
	}

	[TestMethod]
	public void Read_FragmentedChapterTrack_TfhdDefaultsAfterSampleDescriptionIndex ()
	{
		// tfhd flags 0x2|0x8|0x10 (§8.8.7): sample_description_index comes first, then the default duration and
		// size. The trun lists no per-sample fields, so every sample takes those defaults.
		var options = new Mp4ChapterTestBuilder.Options {
			Track = [("Ch A", 900), ("Ch B", 900), ("Ch C", 900)],
			Fragment = Mp4ChapterTestBuilder.FragmentBase.TfhdDefaults,
		};

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (0, "Ch A"), (1.5, "Ch B"), (3, "Ch C"));
		Assert.AreEqual (TimeSpan.FromSeconds (4.5), file.Chapters[2].EndTime);
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Read_ChapterSampleThatIsNotText_FallsBackToChpl (bool fragmented)
	{
		// 0xC3 0x28 is not UTF-8; FE FF D8 00 is a lone UTF-16 surrogate. A stale fragment offset reads such bytes.
		var options = new Mp4ChapterTestBuilder.Options {
			RawSamples = [[0, 2, 0xC3, 0x28], [0, 4, 0xFE, 0xFF, 0xD8, 0x00], [0, 1, (byte)'x']],
			Chpl = [(TimeSpan.Zero, "Intro"), (TimeSpan.FromSeconds (1), "Middle"), (TimeSpan.FromSeconds (2), "End")],
			Fragment = fragmented ? Mp4ChapterTestBuilder.FragmentBase.Moof : null,
		};

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (0, "Intro"), (1, "Middle"), (2, "End"));
		Assert.AreEqual (TimeSpan.FromSeconds (3), file.Chapters[2].EndTime, "the track's three 600-unit samples end at 3 s");
	}

	[TestMethod]
	public void Read_ChapterSampleThatIsNotTextWithoutChpl_KeepsTheTrack ()
	{
		var options = new Mp4ChapterTestBuilder.Options { RawSamples = [[0, 2, 0xC3, 0x28], [0, 1, (byte)'x']] };

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		Assert.AreEqual (2, file.Chapters.Count);
		Assert.AreEqual ("\uFFFD(", file.Chapters[0].Title, "the lossy decoding");
		Assert.AreEqual ("x", file.Chapters[1].Title);
	}

	[TestMethod]
	[DataRow (1)]
	[DataRow (2)]
	public void Read_FragmentWithTrailingBytes_StillReadsItsChapters (int tail)
	{
		// Four stray bytes at the end of the moof (1) or traf (2) do not hide the boxes in front of them,
		// as ffprobe, AVFoundation and MediaInfo read such files. Only the save guard fails closed on them.
		var options = FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Moof);
		options.FragmentTail = tail;
		var data = Mp4ChapterTestBuilder.Build (options);

		AssertChapters (Mp4ChapterTestBuilder.ReadFile (data).Chapters, (0, "Intro"), (1.5, "Middle ü"), (3.5, "End 日本"));
		using var stream = new MemoryStream (data);
		AssertChapters (((Mp4File)MediaFile.Read (stream, "x.m4a").File!).Chapters, (0, "Intro"), (1.5, "Middle ü"), (3.5, "End 日本"));
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	public void Read_ImplicitBaseAfterAnUnreadableTraf_IsUnknown (int broken)
	{
		// The chapter traf has no base flags, so it starts where the audio traf's data ends (§8.8.7). When the audio
		// traf has no tfhd (1) or a trun whose count runs past its box (2), that end is unknown: the chapter samples
		// are not read from a guessed position, and chpl gives the titles. Intact (0), the fragment titles win.
		var options = FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.PreviousTraf);
		options.Chpl = [(TimeSpan.Zero, "C1"), (TimeSpan.FromSeconds (2), "C2")];
		var data = Mp4ChapterTestBuilder.Build (options);
		var audioTraf = data.AsSpan ().IndexOf ("traf"u8);
		if (broken == 1)
			"free"u8.CopyTo (data.AsSpan (audioTraf + data.AsSpan (audioTraf).IndexOf ("tfhd"u8)));
		if (broken == 2) {
			var trun = audioTraf + data.AsSpan (audioTraf).IndexOf ("trun"u8) - 4;
			BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (trun + 12), 50);
		}

		var chapters = Mp4ChapterTestBuilder.ReadFile (data).Chapters;

		if (broken == 0)
			AssertChapters (chapters, (0, "Intro"), (1.5, "Middle ü"), (3.5, "End 日本"));
		else
			AssertChapters (chapters, (0, "C1"), (2, "C2"));
	}

	[TestMethod]
	public void Read_FragmentWithAnUnreadableTrafForAnotherTrack_StillReadsTheChapterTraf ()
	{
		// A traf whose tfhd is cut short is skipped; the chapter traf after it is still read.
		var options = FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Moof);
		var data = Mp4ChapterTestBuilder.Build (options);
		var moofAt = data.AsSpan ().IndexOf ("moof"u8) - 4;
		var mfhdEnd = moofAt + 8 + 16;
		byte[] badTraf = Mp4TestBuilder.CreateBox ("traf", [0, 0, 0, 12, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 0, 0, 0]);
		byte[] file = [.. data.AsSpan (0, mfhdEnd), .. badTraf, .. data.AsSpan (mfhdEnd)];
		var moofSize = BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (moofAt));
		BinaryPrimitives.WriteUInt32BigEndian (file.AsSpan (moofAt), moofSize + (uint)badTraf.Length);
		// The chapter trun's data_offset counts from the moof; the data moved by the inserted traf.
		var trunAt = file.AsSpan (moofAt).IndexOf ("trun"u8) + moofAt - 4;
		var dataOffset = BinaryPrimitives.ReadInt32BigEndian (file.AsSpan (trunAt + 16));
		BinaryPrimitives.WriteInt32BigEndian (file.AsSpan (trunAt + 16), dataOffset + badTraf.Length);

		AssertChapters (Mp4ChapterTestBuilder.ReadFile (file).Chapters, (0, "Intro"), (1.5, "Middle ü"), (3.5, "End 日本"));
	}

	[TestMethod]
	public void Read_ManyInvalidChapterSamples_DecodeWithoutExceptions ()
	{
		// 65,535 samples (the track cap) that are not UTF-8 each decode once, with no exception per sample.
		var options = new Mp4ChapterTestBuilder.Options { RawSamples = [.. Enumerable.Repeat (new byte[] { 0, 1, 0xFF }, Mp4ChapterCodec.MaxTrackChapters)] };
		var data = Mp4ChapterTestBuilder.Build (options);
		var thrown = 0;
		void Count (object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
		{
			if (e.Exception is DecoderFallbackException)
				Interlocked.Increment (ref thrown);
		}

		AppDomain.CurrentDomain.FirstChanceException += Count;
		try {
			var file = Mp4ChapterTestBuilder.ReadFile (data);
			Assert.AreEqual (Mp4ChapterCodec.MaxTrackChapters, file.Chapters.Count);
		} finally {
			AppDomain.CurrentDomain.FirstChanceException -= Count;
		}

		Assert.AreEqual (0, thrown, "decoder exceptions");
	}

	[TestMethod]
	public void Read_FragmentedChapterTrack_TfdtSetsTheDecodeTime ()
	{
		// tfdt baseMediaDecodeTime 600 in a 600 Hz track (§8.8.12): the fragment's samples start at 1 s.
		var options = FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Moof);
		options.FragmentDecodeTime = 600;

		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (options));

		AssertChapters (file.Chapters, (1, "Intro"), (2.5, "Middle ü"), (4.5, "End 日本"));
	}

	[TestMethod]
	[DataRow ((int)Mp4ChapterTestBuilder.FragmentBase.Moof)]
	[DataRow ((int)Mp4ChapterTestBuilder.FragmentBase.PreviousTraf)]
	public void Read_FragmentedChapterTrack_FromStream_MatchesSpanRead (int mode)
	{
		var data = Mp4ChapterTestBuilder.Build (FragmentedTrack ((Mp4ChapterTestBuilder.FragmentBase)mode));

		using var stream = new MemoryStream (data);
		var result = MediaFile.Read (stream, "x.m4a");

		Assert.IsTrue (result.IsSuccess, result.Error);
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.ReadFile (data).Chapters.ToArray (), ((Mp4File)result.File!).Chapters.ToArray ());
		Assert.AreEqual (3, ((Mp4File)result.File!).Chapters.Count);
	}

	[TestMethod]
	public void Save_FragmentedMovieWithChapterTrack_RefusesAChapterChange ()
	{
		var fs = new MockFileSystem ();
		var data = Mp4ChapterTestBuilder.Build (FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Moof));
		fs.AddFile ("/a.m4a", data);
		var file = Mp4File.ReadFromFile ("/a.m4a", fs).File!;

		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "One")];
		var write = file.SaveToFile ("/b.m4a", fs);

		Assert.IsFalse (write.IsSuccess, "a chapter track in fragments cannot be rewritten in moov");
		StringAssert.Contains (write.Error, "fragmented movie");
		Assert.IsFalse (fs.FileExists ("/b.m4a"));
		Assert.IsTrue (file.Render (data).IsEmpty);
	}

	[TestMethod]
	public void Save_ChapterTrafInAnUnreadMoofTail_IsRefused ()
	{
		// The chapter traf sits behind a box that does not parse, so no reader sees it, and it may still hold
		// chapter samples. Rewriting the track could leave a second list: the save fails closed.
		var options = FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Moof);
		options.FragmentTail = 3;
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.m4a", Mp4ChapterTestBuilder.Build (options));
		var file = Mp4File.ReadFromFile ("/a.m4a", fs).File!;

		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "One")];
		var write = file.SaveToFile ("/b.m4a", fs);

		Assert.IsFalse (write.IsSuccess);
		StringAssert.Contains (write.Error, "fragmented movie");
	}

	[TestMethod]
	public async Task SaveToFileAsync_FragmentedMovieWithChapterTrack_RefusesClearing ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.m4a", Mp4ChapterTestBuilder.Build (FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Explicit)));
		var file = (await Mp4File.ReadFromFileAsync ("/a.m4a", fs)).File!;

		file.Chapters = [];
		var write = await file.SaveToFileAsync ("/b.m4a", fs);

		Assert.IsFalse (write.IsSuccess);
		StringAssert.Contains (write.Error, "fragmented movie");
	}

	[TestMethod]
	public void Save_FragmentedMovieWithChapterTrack_TagEditStillSavesAndKeepsChapters ()
	{
		var options = FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.Moof);
		options.Title = "Old";
		var data = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (data);
		var chapters = file.Chapters.ToArray ();

		file.Title = "A longer new title";
		file.Chapters = [.. chapters.Select (c => new Mp4Chapter (c.StartTime, c.Title))];
		var saved = Save (file, data);

		var reread = Mp4ChapterTestBuilder.ReadFile (saved);
		Assert.AreEqual ("A longer new title", reread.Title);
		CollectionAssert.AreEqual (chapters, reread.Chapters.ToArray (), "the fragment data offsets moved with the moof");
	}

	[TestMethod]
	public void Save_FragmentedMovieWithChapterSamplesInMoov_ReplacesWithoutGrowing ()
	{
		// mvex, but the chapter track's samples are all in moov and no traf names it: moov is the whole list.
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var first = EditChapters (Mp4ChapterTestBuilder.Build (options), 1);
		var current = first;
		for (var round = 2; round <= 5; round++)
			current = EditChapters (current, round);

		Assert.AreEqual (first.Length, current.Length);
		AssertChapters (Mp4ChapterTestBuilder.ReadFile (current).Chapters, (0, "Opening 5"), (3, "Middle 5"), (8, "Closing 5"));
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (current, 1));
	}

	[TestMethod]
	public void Save_ClearChaptersInFragmentedMovieWithoutChapterFragments_DropsTheTrex ()
	{
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var data = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (data);

		file.Chapters = [];
		var mvex = Mp4ChapterTestBuilder.Moov (Save (file, data)).FindChild ("mvex")!;

		var trexTracks = mvex.Children.Select (t => BinaryPrimitives.ReadUInt32BigEndian (t.Data.Span.Slice (4))).ToArray ();
		CollectionAssert.AreEqual (new uint[] { Mp4ChapterTestBuilder.AudioTrackId }, trexTracks);
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	[DataRow (3)]
	[DataRow (4)]
	[DataRow (5)]
	[DataRow (6)]
	public void Save_ChapterMdatNextToFragmentData_IsKeptWhenATrafPointsIntoIt (int before)
	{
		// A traf whose explicit base_data_offset lands in the chapter mdat after moov: that mdat is not chapter-only.
		// The guard fails closed: a traf it cannot read first (1: a 4-byte tfhd, 3: a traf with no tfhd) keeps the
		// mdat too, and a traf for track_ID 0 (2) does not hide the later run. A moof child that does not parse
		// (4) leaves the traf unread, and so does an unreadable top-level box in front of the moof (5) or a traf
		// child that does not parse in front of the trun (6).
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (options), 1);
		var chapterPayload = edited.AsSpan ().LastIndexOf ("mdat"u8) + 4;
		var baseOffset = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian (baseOffset, (ulong)chapterPayload);
		byte[] tfhd = [0, 0, 0, 24, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 0, 0, 1, 0, 0, 0, (byte)Mp4ChapterTestBuilder.AudioTrackId,
			.. baseOffset];
		byte[] trun = [0, 0, 0, 20, (byte)'t', (byte)'r', (byte)'u', (byte)'n', 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 4];
		byte[] first = before switch {
			1 => Mp4TestBuilder.CreateBox ("traf", [0, 0, 0, 12, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 0, 0, 0]),
			2 => Mp4TestBuilder.CreateBox ("traf", [0, 0, 0, 16, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 0, 0, 0, 0, 0, 0, 0, .. trun]),
			3 => Mp4TestBuilder.CreateBox ("traf", Mp4TestBuilder.CreateBox ("free", [])),
			4 => [0, 0, 0, 3, (byte)'b', (byte)'a', (byte)'d', (byte)'!'],
			_ => [],
		};
		byte[] badChild = before == 6 ? [0, 0, 0, 3, (byte)'b', (byte)'a', (byte)'d', (byte)'!'] : [];
		var moof = Mp4TestBuilder.CreateBox ("moof", [.. first, .. Mp4TestBuilder.CreateBox ("traf", [.. tfhd, .. badChild, .. trun])]);
		byte[] unreadable = before == 5 ? [0, 0, 0, 3, (byte)'b', (byte)'a', (byte)'d', (byte)'!'] : [];
		byte[] withMoof = [.. edited, .. unreadable, .. moof];

		// A traf whose track it cannot tell (1, 3) or an unread moof tail (4) may be a chapter traf, so a chapter
		// change is refused outright; the file is not touched.
		if (before is 1 or 3 or 4) {
			var file = Mp4ChapterTestBuilder.ReadFile (withMoof);
			file.Chapters = Edit (2);
			Assert.IsTrue (file.Render (withMoof).IsEmpty, "refused");
			return;
		}

		var again = EditChapters (withMoof, 2);

		Assert.AreEqual (3, ReadableTopLevelTypes (again).Count (t => t == "mdat"));
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	[DataRow (3)]
	[DataRow (4)]
	public void HoldsOnlyReplacedSamples_UnreadableFragment_FailsClosed (int variant)
	{
		// The guard alone, below the save's refusal: a moof whose tail does not parse (1), a traf whose tail does
		// not parse (2), a short tfhd (3), or a trun whose count runs past its box (4) means "keep the mdat".
		// A readable fragment that points elsewhere (0) does not.
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (options), 1);
		var chapterMdat = edited.AsSpan ().LastIndexOf ("mdat"u8) - 4;
		var chapterEnd = chapterMdat + (int)BinaryPrimitives.ReadUInt32BigEndian (edited.AsSpan (chapterMdat));
		byte[] bad = [0, 0, 0, 3, (byte)'b', (byte)'a', (byte)'d', (byte)'!'];
		byte[] tfhd = [0, 0, 0, 16, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 2, 0, 0, 0, 0, 0, (byte)Mp4ChapterTestBuilder.AudioTrackId];
		byte[] shortTfhd = [0, 0, 0, 12, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 0, 0, 0];
		byte[] trun = [0, 0, 0, 16, (byte)'t', (byte)'r', (byte)'u', (byte)'n', 0, 0, 2, 0, 0, 0, 0, (byte)(variant == 4 ? 9 : 0)];
		byte[] traf = variant switch {
			2 => Mp4TestBuilder.CreateBox ("traf", [.. tfhd, .. trun, .. bad]),
			3 => Mp4TestBuilder.CreateBox ("traf", [.. shortTfhd, .. trun]),
			_ => Mp4TestBuilder.CreateBox ("traf", [.. tfhd, .. trun]),
		};
		var moofBytes = Mp4TestBuilder.CreateBox ("moof", [.. traf, .. (variant == 1 ? bad : [])]);
		var moof = Mp4Box.Parse (moofBytes).Box!;
		var moov = Mp4ChapterTestBuilder.Moov (edited);
		var plan = new Mp4ChapterCodec.ChapterPlan (BinaryData.Empty, [], TextTrak, [], null, [Mp4ChapterCodec.FindChapterTrack (moov)!]);

		var drop = Mp4ChapterCodec.HoldsOnlyReplacedSamples (moov, [(moof, edited.Length)], plan, chapterMdat, chapterMdat + 8, chapterEnd);

		Assert.AreEqual (variant == 0, drop);
	}

	/// <summary>Top-level box types up to the first box that does not parse.</summary>
	static List<string> ReadableTopLevelTypes (byte[] file)
	{
		var types = new List<string> ();
		for (var offset = 0; offset < file.Length;) {
			var result = Mp4Box.Parse (file.AsSpan (offset));
			if (!result.IsSuccess)
				break;
			types.Add (result.Box!.Type);
			offset += result.BytesConsumed;
		}

		return types;
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void Read_FragmentedChapterTrack_TruncatedAtEveryByte_DoesNotThrow ()
	{
		var data = Mp4ChapterTestBuilder.Build (FragmentedTrack (Mp4ChapterTestBuilder.FragmentBase.PreviousTraf));

		for (var length = 0; length <= data.Length; length++) {
			_ = Mp4File.Read (data.AsSpan (0, length));
			using var stream = new MemoryStream (data, 0, length);
			_ = MediaFile.Read (stream, "x.m4a");
		}

		var moofAt = data.AsSpan ().IndexOf ("moof"u8) - 4;
		foreach (var value in new byte[] { 0x00, 0xFF, 0x7F }) {
			for (var at = moofAt; at < data.Length; at++) {
				var copy = (byte[])data.Clone ();
				copy[at] = value;
				_ = Mp4File.Read (copy);
			}
		}
	}

	[TestMethod]
	public void Save_FragmentedMovie_WritesChplOnly ()
	{
		var original = Mp4ChapterTestBuilder.Build (new () { Mvex = true });
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];
		var saved = Save (file, original);

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		CollectionAssert.AreEqual (new uint[] { 1 }, Mp4ChapterTestBuilder.TrackIds (moov));
		AssertChapters (Mp4ChapterTestBuilder.ReadFile (saved).Chapters, (0, "A"));
		CollectionAssert.AreEqual (WithoutChapterData, Mp4ChapterTestBuilder.TopLevelTypes (saved), "no chapter mdat");
	}

	[TestMethod]
	public void Save_NoTrack_WritesChplOnly ()
	{
		var withoutTrak = RemoveTraks (Mp4TestBuilder.CreateMinimalM4a ());
		var file = Mp4ChapterTestBuilder.ReadFile (withoutTrak);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];

		var saved = Save (file, withoutTrak);

		AssertChapters (Mp4ChapterTestBuilder.ReadFile (saved).Chapters, (0, "A"));
		Assert.AreEqual (0, Mp4ChapterTestBuilder.TrackIds (Mp4ChapterTestBuilder.Moov (saved)).Length);
	}

	static byte[] RemoveTraks (byte[] data)
	{
		var moov = Mp4ChapterTestBuilder.Moov (data);
		var children = moov.Children.Where (c => c.Type != "trak").SelectMany (c => c.Render ().ToArray ()).ToArray ();
		return [.. Mp4TestBuilder.CreateFtypBox (), .. Mp4TestBuilder.CreateMoovBox (children)];
	}

	// ---- unchanged saves

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_ChaptersUntouched_IsByteIdentical (bool moovFirst)
	{
		var options = ThreeChapterTrack ();
		options.MoovFirst = moovFirst;
		options.Chpl = ThreeChpl ();
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		var saved = Save (file, original);

		CollectionAssert.AreEqual (original, saved);
	}

	[TestMethod]
	public void Save_SameListAssigned_IsByteIdentical ()
	{
		var options = ThreeChapterTrack ();
		options.Chpl = ThreeChpl ();
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Chapters = [.. file.Chapters.Select (c => new Mp4Chapter (c.StartTime, c.Title))];

		CollectionAssert.AreEqual (original, Save (file, original));
	}

	[TestMethod]
	public void Save_TitleEdit_KeepsChapterBytes ()
	{
		var options = ThreeChapterTrack ();
		options.Chpl = ThreeChpl ();
		options.Title = "Old";
		var original = Mp4ChapterTestBuilder.Build (options);
		var file = Mp4ChapterTestBuilder.ReadFile (original);

		file.Title = "A much longer new title that grows moov";
		var saved = Save (file, original);

		var before = Mp4ChapterTestBuilder.Moov (original);
		var after = Mp4ChapterTestBuilder.Moov (saved);
		CollectionAssert.AreEqual (before.FindDescendant ("udta", "chpl")!.Data.ToArray (), after.FindDescendant ("udta", "chpl")!.Data.ToArray ());
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.SampleBytes (original, 2), Mp4ChapterTestBuilder.SampleBytes (saved, 2), "chapter samples unchanged");
		AssertChapters (Mp4ChapterTestBuilder.ReadFile (saved).Chapters, (0, "Intro"), (1.5, "Cafè über 日本"), (3.5, "Outro"));
	}

	[TestMethod]
	public void Save_SecondSaveOfSameList_IsByteIdentical ()
	{
		var original = Mp4ChapterTestBuilder.Build (new ());
		var file = Mp4ChapterTestBuilder.ReadFile (original);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A"), new Mp4Chapter (TimeSpan.FromSeconds (3), "B")];

		var first = Save (file, original);
		var second = Save (file, first);

		CollectionAssert.AreEqual (first, second, "the file already holds the list, so nothing is rewritten");
	}

	static Mp4Chapter[] Edit (int round) => [
		new Mp4Chapter (TimeSpan.Zero, $"Opening {round}"),
		new Mp4Chapter (TimeSpan.FromSeconds (2 + (round % 2)), $"Middle {round}"),
		new Mp4Chapter (TimeSpan.FromSeconds (8), $"Closing {round}"),
	];

	static byte[] EditChapters (byte[] data, int round)
	{
		var file = Mp4ChapterTestBuilder.ReadFile (data);
		file.Chapters = Edit (round);
		return Save (file, data);
	}

	[TestMethod]
	[DataRow (false, false)]
	[DataRow (true, false)]
	[DataRow (false, true)]
	[DataRow (true, true)]
	public void Save_RepeatedChapterEdits_DoNotGrowTheFile (bool moovFirst, bool startWithChapters)
	{
		var options = startWithChapters ? ThreeChapterTrack () : new Mp4ChapterTestBuilder.Options ();
		options.MoovFirst = moovFirst;
		if (startWithChapters)
			options.Chpl = ThreeChpl ();
		var data = Mp4ChapterTestBuilder.Build (options);

		var first = EditChapters (data, 1);
		var current = first;
		for (var round = 2; round <= 5; round++) {
			current = EditChapters (current, round);
			Assert.AreEqual (first.Length, current.Length, $"edit {round} is the size of edit 1");
			CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (current, 1), $"audio after edit {round}");
			AssertChapters (Mp4ChapterTestBuilder.ReadFile (current).Chapters, (0, $"Opening {round}"), (2 + (round % 2), $"Middle {round}"), (8, $"Closing {round}"));
		}

		// One mdat for the audio, one for the chapter samples; the chapter one sits right after moov.
		var types = Mp4ChapterTestBuilder.TopLevelTypes (current);
		Assert.AreEqual (2, types.Count (t => t == "mdat"), string.Join (",", types));
		Assert.AreEqual ("mdat", types[types.IndexOf ("moov") + 1]);
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_ChapterEditOfGrowingList_ReplacesOwnChapterData (bool moovFirst)
	{
		var data = Mp4ChapterTestBuilder.Build (new () { MoovFirst = moovFirst });
		var file = Mp4ChapterTestBuilder.ReadFile (data);
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A")];
		var once = Save (file, data);

		file = Mp4ChapterTestBuilder.ReadFile (once);
		file.Chapters = [.. Enumerable.Range (0, 40).Select (i => new Mp4Chapter (TimeSpan.FromMilliseconds (i * 200), $"Chapter number {i}"))];
		var twice = Save (file, once);

		Assert.AreEqual (2, Mp4ChapterTestBuilder.TopLevelTypes (twice).Count (t => t == "mdat"));
		Assert.AreEqual (40, Mp4ChapterTestBuilder.ReadFile (twice).Chapters.Count);
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (twice, 1));
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_ClearAfterEdit_DropsOwnChapterData (bool moovFirst)
	{
		var data = Mp4ChapterTestBuilder.Build (new () { MoovFirst = moovFirst });
		var edited = EditChapters (data, 1);
		var file = Mp4ChapterTestBuilder.ReadFile (edited);

		file.Chapters = [];
		var cleared = Save (file, edited);

		CollectionAssert.AreEqual (moovFirst ? WithoutChapterDataMoovFirst : WithoutChapterData, Mp4ChapterTestBuilder.TopLevelTypes (cleared));
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (cleared, 1));
		Assert.AreEqual (0, Mp4ChapterTestBuilder.ReadFile (cleared).Chapters.Count);
	}

	[TestMethod]
	public void Save_ChapterMdatWithExtraBytes_IsKept ()
	{
		// moov last: the chapter mdat is the last box. Four more payload bytes are not chapter samples.
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (new ()), 1);
		var chapterMdat = edited.AsSpan ().LastIndexOf ("mdat"u8) - 4;
		var size = BinaryPrimitives.ReadUInt32BigEndian (edited.AsSpan (chapterMdat));
		byte[] padded = [.. edited, 1, 2, 3, 4];
		BinaryPrimitives.WriteUInt32BigEndian (padded.AsSpan (chapterMdat), size + 4);

		var again = EditChapters (padded, 2);

		Assert.AreEqual (3, Mp4ChapterTestBuilder.TopLevelTypes (again).Count (t => t == "mdat"));
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (again, 1));
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	[DataRow (3)]
	[DataRow (4)]
	[DataRow (5)]
	[DataRow (6)]
	[DataRow (7)]
	[DataRow (8)]
	[DataRow (9)]
	[DataRow (10)]
	[DataRow (11)]
	[DataRow (12)]
	[DataRow (13)]
	[DataRow (14)]
	[DataRow (15)]
	[DataRow (16)]
	[DataRow (17)]
	[DataRow (18)]
	[DataRow (19)]
	[DataRow (20)]
	[DataRow (21)]
	[DataRow (22)]
	[DataRow (23)]
	[DataRow (24)]
	[DataRow (25)]
	[DataRow (26)]
	[DataRow (27)]
	[DataRow (28)]
	[DataRow (29)]
	[DataRow (30)]
	public void Save_ChapterMdatReferencedByAnotherTrack_IsKept (int variant)
	{
		// One rule: the earlier chapter mdat goes only when every byte range anything else references can be
		// computed and none overlaps it. Each row changes the first edit's output, then edits again.
		//  0  the audio chunk moved into the chapter mdat (keep)
		//  1  a bad box in the audio stbl in front of stco; 2 in the trak in front of mdia (keep: tables unread)
		//  3  a trak in moov's unread tail whose chunk is in the mdat (keep)
		//  4  an audio chunk that starts 50 bytes before the mdat and runs into it (keep)
		//  5  a moov meta whose iloc item (construction method 0) is in the mdat (keep)
		//  6  a bad box after the audio stco hides a co64 into the mdat (keep: stbl tail)
		//  7  no stsz or stz2 (keep: chunk extent unknown); 8 stco and 9 stsz counts past their box (keep)
		// 10  a moov meta with only an hdlr (drop: it references nothing)
		// 11  a moov meta with a bad box in front of an iloc into the mdat (keep: the iloc is in the unread tail)
		// 12  a file-level meta with only an hdlr (drop); 13 one whose iloc item is in the mdat (keep)
		// 14  the audio sizes in stz2 (8-bit entries), chunk in the mdat (keep); 15 chunk elsewhere (drop)
		// 16  a second trak with the chapter track's track_ID, its chunk in the mdat (keep: compared by object)
		// 17  sizes and counts of 0xFFFFFFFF: the chunk extent overflows (keep)
		// 18  a moov meta whose iloc item uses construction method 1 (idat), not a file offset (drop)
		// 19  stz2 16-bit sizes, the chunk ending exactly at the mdat (drop); 20 one byte later (keep)
		// 21  stz2 4-bit sizes of 15, the chunk ending exactly at the mdat (drop); 22 one byte later (keep)
		// 23  a moov meta that stops parsing at a bad box, with no iloc in its unread bytes (drop: every box carries
		//     its type code, so no iloc hides there; iTunes files have metas with stray trailing bytes)
		// 24  an unread file tail (after a bad box) that holds a meta whose iloc points into the mdat (keep)
		// 25  a file-level iloc extent of length 0 that starts before the mdat: it runs to the end (keep)
		// 26  stz2 4-bit sizes 0x0F (entry 0 is 0, entry 1 is 15), one sample per chunk, the chunk 10 bytes
		//     before the mdat: its single sample is 0 bytes long (drop; the low nibble first would reach it)
		// 27  audio aux info (saiz + saio, §8.7.8-9) at an offset in the mdat (keep); 28 elsewhere (drop)
		// 29  a version 1 saio (64-bit offsets) into the mdat (keep; read as 32-bit it would point at 0)
		// 30  a saio with no saiz, its offset elsewhere: the aux size is unknown (keep)
		var keep = variant is not (10 or 12 or 15 or 18 or 19 or 21 or 23 or 26 or 28);
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (new ()), 1);
		byte[] bad = [0, 0, 0, 3, (byte)'b', (byte)'a', (byte)'d', (byte)'!'];
		var moovAt = edited.AsSpan ().IndexOf ("moov"u8) - 4;
		var trakAt = moovAt + edited.AsSpan (moovAt).IndexOf ("trak"u8) - 4;
		var mdiaAt = trakAt + edited.AsSpan (trakAt).IndexOf ("mdia"u8) - 4;
		var minfAt = mdiaAt + edited.AsSpan (mdiaAt).IndexOf ("minf"u8) - 4;
		var stblAt = minfAt + edited.AsSpan (minfAt).IndexOf ("stbl"u8) - 4;
		var stcoAt = stblAt + edited.AsSpan (stblAt).IndexOf ("stco"u8) - 4;
		var stszAt = stblAt + edited.AsSpan (stblAt).IndexOf ("stsz"u8) - 4;
		var stscAt = stblAt + edited.AsSpan (stblAt).IndexOf ("stsc"u8) - 4;
		var stszLength = (int)BinaryPrimitives.ReadUInt32BigEndian (edited.AsSpan (stszAt));
		var trakLength = (int)BinaryPrimitives.ReadUInt32BigEndian (edited.AsSpan (trakAt));
		var stblEnd = stblAt + (int)BinaryPrimitives.ReadUInt32BigEndian (edited.AsSpan (stblAt));
		var moovEnd = moovAt + (int)BinaryPrimitives.ReadUInt32BigEndian (edited.AsSpan (moovAt));
		var hdlr = Mp4TestBuilder.CreateFullBox ("hdlr", 0, 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[13]]);
		const int count = Mp4ChapterTestBuilder.AudioSampleCount;
		byte[] stz2 = variant switch {
			19 or 20 => Mp4TestBuilder.CreateFullBox ("stz2", 0, 0, [0, 0, 0, 16, 0, 0, 0, count,
				.. Enumerable.Range (0, count).SelectMany (_ => new byte[] { 0, Mp4ChapterTestBuilder.AudioSampleSize })]),
			21 or 22 => Mp4TestBuilder.CreateFullBox ("stz2", 0, 0, [0, 0, 0, 4, 0, 0, 0, count, .. Enumerable.Repeat ((byte)0xFF, count / 2)]),
			26 => Mp4TestBuilder.CreateFullBox ("stz2", 0, 0, [0, 0, 0, 4, 0, 0, 0, count, .. Enumerable.Repeat ((byte)0x0F, count / 2)]),
			_ => Mp4TestBuilder.CreateFullBox ("stz2", 0, 0, [0, 0, 0, 8, 0, 0, 0, count,
				.. Enumerable.Repeat ((byte)Mp4ChapterTestBuilder.AudioSampleSize, count)]),
		};

		// Where the bytes go, how many original bytes they replace, and the boxes whose size grows with them.
		int[] inStbl = [moovAt, trakAt, mdiaAt, minfAt, stblAt];
		var (at, removed, inserted, containers) = variant switch {
			1 => (stcoAt, 0, bad, inStbl),
			2 => (mdiaAt, 0, bad, new[] { moovAt, trakAt }),
			3 or 16 => (moovEnd, 0, new byte[(variant == 3 ? 8 : 0) + trakLength], new[] { moovAt }),
			5 or 18 => (moovEnd, 0, Meta (hdlr, Iloc (variant == 18 ? 1 : 0, 0)), new[] { moovAt }),
			6 => (stblEnd, 0, new byte[8 + 24], inStbl),
			10 => (moovEnd, 0, Meta (hdlr), new[] { moovAt }),
			11 => (moovEnd, 0, Meta (hdlr, bad, Iloc (0, 0)), new[] { moovAt }),
			23 => (moovEnd, 0, Meta (hdlr, bad, Mp4TestBuilder.CreateBox ("free", [1, 2, 3, 4])), new[] { moovAt }),
			12 => (edited.Length, 0, Meta (hdlr), Array.Empty<int> ()),
			13 => (edited.Length, 0, Meta (hdlr, Iloc (0, 0)), Array.Empty<int> ()),
			14 or 15 or 19 or 20 or 21 or 22 or 26 => (stszAt, stszLength, stz2, inStbl),
			24 => (edited.Length, 0, [.. bad, .. Meta (hdlr, Iloc (0, 0))], Array.Empty<int> ()),
			25 => (edited.Length, 0, Meta (hdlr, Iloc (0, 100, 0)), Array.Empty<int> ()),
			27 or 28 => (stblEnd, 0, new byte[Saiz ().Length + Saio (0).Length], inStbl),
			29 => (stblEnd, 0, new byte[Saiz ().Length + Saio64 (0).Length], inStbl),
			30 => (stblEnd, 0, Saio (120), inStbl),
			_ => (moovEnd, 0, Array.Empty<byte> (), new[] { moovAt }),
		};

		// The chapter mdat follows moov; every byte added to moov moves it.
		var inMoov = containers.Contains (moovAt) ? inserted.Length - removed : 0;
		var chapterPayload = moovEnd + 8 + inMoov;
		var moov = edited.AsSpan (moovAt, moovEnd - moovAt).ToArray ();
		Mp4ChapterCodec.WriteChunkOffset (moov, TextTrak, (uint)chapterPayload);
		// Rows that reach the mdat through another box leave the parsed audio stco alone.
		var chunkLength = variant switch {
			19 or 20 => count * Mp4ChapterTestBuilder.AudioSampleSize,
			21 or 22 => count * 15,
			_ => 0,
		};
		var boxStart = chapterPayload - 8;
		var audioChunk = variant switch {
			4 or 7 or 17 => chapterPayload - 50,
			19 or 21 => boxStart - chunkLength,
			26 => boxStart - 10,
			20 or 22 => boxStart - chunkLength + 1,
			_ => chapterPayload,
		};
		if (variant is 0 or 1 or 2 or 4 or 7 or 14 or 17 or 19 or 20 or 21 or 22 or 26)
			Mp4ChapterCodec.WriteChunkOffset (moov, AudioTrak, (uint)audioChunk);
		moov.CopyTo (edited, moovAt);

		switch (variant) {
			case 3:
			case 16: {
					// A copy of the audio trak whose chunk sits in the mdat: behind a bad box (3), or as track 2 (16).
					var copy = Mp4TestBuilder.CreateMoovBox (edited.AsSpan (trakAt, trakLength).ToArray ());
					Mp4ChapterCodec.WriteChunkOffset (copy, 0, (uint)chapterPayload);
					var trak = copy.AsSpan (8).ToArray ();
					if (variant == 16)
						BinaryPrimitives.WriteUInt32BigEndian (trak.AsSpan (trak.AsSpan ().IndexOf ("tkhd"u8) + 16), Mp4ChapterTestBuilder.TextTrackId);
					inserted = variant == 3 ? [.. bad, .. trak] : trak;
					break;
				}
			case 5:
			case 11:
			case 13:
			case 18:
				inserted = variant switch {
					5 => Meta (hdlr, Iloc (0, chapterPayload)),
					11 => Meta (hdlr, bad, Iloc (0, chapterPayload)),
					13 => Meta (hdlr, Iloc (0, chapterPayload)),
					_ => Meta (hdlr, Iloc (1, chapterPayload)),
				};
				break;
			case 6: {
					var co64 = Mp4TestBuilder.CreateFullBox ("co64", 0, 0, [0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]);
					BinaryPrimitives.WriteUInt64BigEndian (co64.AsSpan (16), (ulong)chapterPayload);
					inserted = [.. bad, .. co64];
					break;
				}
			case 7:
				"free"u8.CopyTo (edited.AsSpan (stszAt + 4));
				break;
			case 8:
				BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (stcoAt + 12), 2);
				break;
			case 9:
				BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (stszAt + 16), Mp4ChapterTestBuilder.AudioSampleCount + 1);
				break;
			case 24:
				inserted = [.. bad, .. Meta (hdlr, Iloc (0, chapterPayload))];
				break;
			case 26:
				// One sample per chunk: the single chunk holds entry 0 only.
				BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (stscAt + 20), 1);
				break;
			case 27:
			case 28:
				inserted = [.. Saiz (), .. Saio (variant == 27 ? chapterPayload : 120)];
				break;
			case 29:
				inserted = [.. Saiz (), .. Saio64 (chapterPayload)];
				break;
			case 17:
				BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (stszAt + 12), uint.MaxValue);
				BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (stszAt + 16), uint.MaxValue);
				BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (stscAt + 20), uint.MaxValue);
				break;
		}

		byte[] file = [.. edited.AsSpan (0, at), .. inserted, .. edited.AsSpan (at + removed)];
		foreach (var box in containers)
			BinaryPrimitives.WriteUInt32BigEndian (file.AsSpan (box), (uint)(BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (box)) + inserted.Length - removed));

		var again = EditChapters (file, 2);

		Assert.AreEqual (keep ? 3 : 2, ReadableTopLevelTypes (again).Count (t => t == "mdat"), keep ? "kept" : "dropped");
		if (!keep)
			Assert.AreEqual (EditChapters (again, 3).Length, again.Length, "a dropped mdat keeps the size constant");
		if (variant is 3 or 5 or 6 or 10 or 12 or 13 or 16 or 18 or 24 or 25 or 27 or 28 or 29 or 30)
			CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (again, 1), "the parsed audio track is intact");
		if (variant == 0) {
			var audioAt = BinaryPrimitives.ReadUInt32BigEndian (Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (again), 1)!
				.FindDescendant ("mdia", "minf", "stbl", "stco")!.Data.Span.Slice (8));
			CollectionAssert.AreEqual (file.AsSpan (chapterPayload, 8).ToArray (), again.AsSpan ((int)audioAt, 8).ToArray (), "the audio offset still finds the same bytes");
		}
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void Save_ZeroWidthIlocExtents_CostOnlyTheirBytes ()
	{
		// iloc (§8.11.3) field sizes may be 0, so an extent can take no bytes: 64 eight-byte items declare
		// 64 x 65,535 extents in about 530 bytes. The drop check tests each item's extent once, as it reads it,
		// and stores nothing per extent. Each extent here covers the whole file, so the chapter mdat is kept.
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (new ()), 1);
		const int items = 64;
		var body = new byte[2 + 2 + (items * 8)];
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (2), items);
		for (var i = 0; i < items; i++) {
			BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (4 + (i * 8)), (ushort)(i + 1));
			BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (4 + (i * 8) + 6), ushort.MaxValue);
		}

		var hdlr = Mp4TestBuilder.CreateFullBox ("hdlr", 0, 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[13]]);
		byte[] file = [.. edited, .. Meta (hdlr, Mp4TestBuilder.CreateFullBox ("iloc", 1, 0, body))];
		var parsed = Mp4ChapterTestBuilder.ReadFile (file);
		parsed.Chapters = Edit (2);

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var again = parsed.Render (file).ToArray ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (3, Mp4ChapterTestBuilder.TopLevelTypes (again).Count (t => t == "mdat"), "kept");
		Assert.IsTrue (allocated < 8 * 1024 * 1024, $"{allocated:N0} bytes allocated for a {file.Length:N0}-byte file");
	}

	[TestMethod]
	[DataRow (false, false)]
	[DataRow (true, false)]
	[DataRow (true, true)]
	public void Save_ClearChapters_SharedTrackId_DropsTheChapReferenceAndKeepsTheTrex (bool keepTref, bool onlyCopyHolds)
	{
		// A sound trak shares the chapter track's track_ID 2 (invalid per §8.3.2.3, but seen). Clearing removes
		// the chapter trak object and every chap entry for ID 2, which meant the chapter track. The sound trak
		// stays, and so does trex 2, which it still uses. With keepTref the copied sound trak keeps its own
		// chap reference to 2: it is a presentation track (a holder) by object, which must not hide the chapter
		// text track that ID 2 resolves to. With onlyCopyHolds trak 1 has no tref, so the copy's chap 2 is the
		// only reference: a self-reference test by ID (the copy's own ID is 2) would wrongly skip it.
		var file = SharedTrackIdFile (keepTref, onlyCopyHolds);
		var parsed = Mp4ChapterTestBuilder.ReadFile (file);

		Assert.AreEqual (3, parsed.Chapters.Count, "ID 2 resolves to the chapter text track first");
		parsed.Chapters = [];
		var saved = Save (parsed, file);
		var cleared = Mp4ChapterTestBuilder.Moov (saved);

		Assert.AreEqual (0, Mp4ChapterTestBuilder.ReadFile (saved).Chapters.Count, "nothing reads back");
		CollectionAssert.AreEqual (new uint[] { 1, 2 }, Mp4ChapterTestBuilder.TrackIds (cleared), "the sound trak with ID 2 stays");
		Assert.IsFalse (cleared.Children.Any (c => c.Type == "trak" && HandlerOf (c) == "text"), "the chapter text trak is gone");
		Assert.IsNull (cleared.Children.Where (c => c.Type == "trak").Last ().FindChild ("tref"), "its own chap entry for 2 is gone too");
		Assert.AreEqual ("soun", Encoding.ASCII.GetString (cleared.Children.Where (c => c.Type == "trak").Last ().FindDescendant ("mdia", "hdlr")!.Data.Span.Slice (8, 4)));
		Assert.AreEqual (0, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (cleared, 1)!).Length, "no chap entry is left");
		var trex = cleared.FindChild ("mvex")!.Children.Select (t => BinaryPrimitives.ReadUInt32BigEndian (t.Data.Span.Slice (4))).ToArray ();
		CollectionAssert.AreEqual (new uint[] { 1, 2 }, trex, "trex 2 stays for the sound trak");
	}

	[TestMethod]
	[DataRow (false, false)]
	[DataRow (true, false)]
	[DataRow (true, true)]
	public void Save_EditChapters_SharedTrackId_RewritesOnlyTheChapterTrack (bool keepTref, bool onlyCopyHolds)
	{
		// The edit path's track_ID lookups (the in-place replacement, the chunk offset written after layout, and
		// the chap entry added to presentation tracks) must all land on the chapter trak that ID 2 resolves to,
		// the first, and leave the later sound trak that shares the ID reading its own audio.
		var file = SharedTrackIdFile (keepTref, onlyCopyHolds);

		var saved = EditChapters (file, 1);
		var moov = Mp4ChapterTestBuilder.Moov (saved);

		CollectionAssert.AreEqual (new uint[] { 1, 2, 2 }, Mp4ChapterTestBuilder.TrackIds (moov), "replaced in place");
		CollectionAssert.AreEqual (Edit (1).Select (c => c.Title).ToArray (), Mp4ChapterTestBuilder.ReadFile (saved).Chapters.Select (c => c.Title).ToArray ());
		var text = Encoding.UTF8.GetString (Mp4ChapterTestBuilder.SampleBytes (saved, Mp4ChapterTestBuilder.TextTrackId));
		Assert.IsTrue (text.Contains ("Opening 1", StringComparison.Ordinal) && text.Contains ("Closing 1", StringComparison.Ordinal), "the chapter trak's chunk offset points at the new samples");
		var sound = moov.Children.Where (c => c.Type == "trak").Last ();
		var stco = sound.FindDescendant ("mdia", "minf", "stbl", "stco")!.Data.Span;
		var audio = Mp4ChapterTestBuilder.SampleBytes (saved, Mp4ChapterTestBuilder.AudioTrackId);
		CollectionAssert.AreEqual (audio, saved.AsSpan ((int)BinaryPrimitives.ReadUInt32BigEndian (stco.Slice (8)), audio.Length).ToArray (), "the sound trak still reads the audio");
		CollectionAssert.AreEqual (keepTref ? new uint[] { 2 } : [], Mp4ChapterTestBuilder.ChapReferences (sound), "its own tref is left as it was");
		CollectionAssert.AreEqual (onlyCopyHolds ? [] : new uint[] { 2 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		Assert.AreEqual (1, moov.Children.Count (c => c.Type == "trak" && HandlerOf (c) == "text"), "one chapter track, not a second one");
	}

	/// <summary>
	/// The chapter-track fixture with a copy of the audio trak appended to moov under the text track's track_ID 2.
	/// With <paramref name="keepTref"/> the copy keeps the audio trak's tref (chap 2); without, it becomes free.
	/// With <paramref name="onlyCopyHolds"/> the audio trak's own tref becomes free, so only the copy holds chap 2.
	/// </summary>
	static byte[] SharedTrackIdFile (bool keepTref, bool onlyCopyHolds = false)
	{
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var data = Mp4ChapterTestBuilder.Build (options);
		var moov = Mp4ChapterTestBuilder.Moov (data);
		var moovAt = data.AsSpan ().IndexOf ("moov"u8) - 4;
		var audio = Mp4ChapterTestBuilder.Trak (moov, Mp4ChapterTestBuilder.AudioTrackId)!.Render ().ToArray ();
		var tkhd = audio.AsSpan ().IndexOf ("tkhd"u8);
		BinaryPrimitives.WriteUInt32BigEndian (audio.AsSpan (tkhd + 16), Mp4ChapterTestBuilder.TextTrackId);
		if (!keepTref)
			"free"u8.CopyTo (audio.AsSpan (audio.AsSpan ().IndexOf ("tref"u8)));
		Assert.AreEqual (data.Length, moovAt + (int)BinaryPrimitives.ReadUInt32BigEndian (data.AsSpan (moovAt)), "moov is last");
		byte[] file = [.. data, .. audio];
		BinaryPrimitives.WriteUInt32BigEndian (file.AsSpan (moovAt), BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (moovAt)) + (uint)audio.Length);
		if (onlyCopyHolds)
			"free"u8.CopyTo (file.AsSpan (moovAt + file.AsSpan (moovAt).IndexOf ("tref"u8)));
		return file;
	}

	static string HandlerOf (Mp4Box trak) =>
		Encoding.ASCII.GetString (trak.FindDescendant ("mdia", "hdlr")!.Data.Span.Slice (8, 4));

	/// <summary>
	/// Shared-ID shapes (track_ID reused, invalid per §8.3.2.3 but seen), built from the chapter-track fixture:
	/// audio trak A (ID 1, chap [2]) and text trak T (ID 2). S is a copy of A under ID 2, without A's tref.
	/// dup [A, T, S]; dup_tref [A, T, S with chap 2]; sonly [A without tref, T, S with chap 2];
	/// cm_img [A, T as vide, S]; cm_sfirst [A, S, T]; cm_audref [A, S, T as ID 3]; cm_only [T as vide, A as ID 2].
	/// </summary>
	static byte[] SharedTrackIdShape (string shape)
	{
		var data = Mp4ChapterTestBuilder.Build (ThreeChapterTrack ());
		var moov = Mp4ChapterTestBuilder.Moov (data);
		var moovAt = data.AsSpan ().IndexOf ("moov"u8) - 4;
		Assert.AreEqual (data.Length, moovAt + (int)BinaryPrimitives.ReadUInt32BigEndian (data.AsSpan (moovAt)), "moov is last");
		var a = Mp4ChapterTestBuilder.Trak (moov, Mp4ChapterTestBuilder.AudioTrackId)!;
		var t = Mp4ChapterTestBuilder.Trak (moov, Mp4ChapterTestBuilder.TextTrackId)!;
		var s = WithTrackId (WithChild (a, "tref", null), Mp4ChapterTestBuilder.TextTrackId);
		var sTref = WithTrackId (a, Mp4ChapterTestBuilder.TextTrackId);
		var tVide = WithChild (t, "mdia", m => WithChild (m, "hdlr", h => {
			var bytes = h.Data.ToArray ();
			"vide"u8.CopyTo (bytes.AsSpan (8));
			return new Mp4Box ("hdlr", new BinaryData (bytes));
		}));
		Mp4Box[] traks = shape switch {
			"dup" => [a, t, s],
			"dup_tref" => [a, t, sTref],
			"sonly" => [WithChild (a, "tref", null), t, sTref],
			"cm_img" => [a, tVide, s],
			"cm_sfirst" => [a, s, t],
			"cm_audref" => [a, s, WithTrackId (t, 3)],
			"cm_only" => [tVide, WithTrackId (a, Mp4ChapterTestBuilder.TextTrackId)],
			_ => throw new ArgumentException (shape),
		};
		var children = moov.Children.Where (c => c.Type != "trak").ToList ();
		children.InsertRange (children.FindIndex (c => c.Type == "mvhd") + 1, traks);
		return [.. data.AsSpan (0, moovAt), .. new Mp4Box ("moov", moov.Data, children).Render ().Span];
	}

	/// <summary>The box with its first <paramref name="type"/> child replaced by <paramref name="change"/>, or removed when that is null.</summary>
	static Mp4Box WithChild (Mp4Box box, string type, Func<Mp4Box, Mp4Box>? change)
	{
		var children = new List<Mp4Box> ();
		var done = false;
		foreach (var child in box.Children) {
			if (!done && child.Type == type) {
				done = true;
				if (change is not null)
					children.Add (change (child));
				continue;
			}

			children.Add (child);
		}

		return new Mp4Box (box.Type, box.Data, children, box.UsesExtendedSize, box.UnparsedTail, box.HeaderPrefix);
	}

	[TestMethod]
	[DataRow ("dup")]
	[DataRow ("dup_tref")]
	[DataRow ("sonly")]
	[DataRow ("cm_img")]
	[DataRow ("cm_sfirst")]
	[DataRow ("cm_audref")]
	[DataRow ("cm_only")]
	public void Save_RepeatedEdits_SharedTrackIdShapes_KeepOneChapterTrackAndTheSize (string shape)
	{
		// Identity is the trak object once IDs collide. Every path must agree on which trak is the chapter track,
		// so each edit replaces it instead of adding another that no chap names.
		var file = SharedTrackIdShape (shape);
		var sizes = new List<int> ();
		for (var round = 1; round <= 4; round++) {
			file = EditChapters (file, round);
			sizes.Add (file.Length);
			var track = Mp4ChapterCodec.FindChapterTrack (Mp4ChapterTestBuilder.Moov (file));
			Assert.IsNotNull (track, $"{shape} round {round}: a chap names the chapter text track");
			CollectionAssert.AreEqual (Edit (round).Select (c => c.Title).ToArray (), Mp4ChapterTestBuilder.ReadFile (file).Chapters.Select (c => c.Title).ToArray (), $"{shape} round {round}");
			var text = Encoding.UTF8.GetString (Mp4ChapterTestBuilder.SampleBytes (file, track.Id));
			Assert.IsTrue (text.Contains ($"Opening {round}", StringComparison.Ordinal), $"{shape} round {round}: the chapter track holds the titles");
		}

		Assert.IsTrue (sizes.All (size => size == sizes[0]), $"{shape}: {string.Join (", ", sizes)}");
		CollectionAssert.AreEqual (file, EditChapters (file, 4), $"{shape}: the same edit again changes nothing");

		var cleared = Mp4ChapterTestBuilder.ReadFile (file);
		cleared.Chapters = [];
		var empty = Save (cleared, file);
		Assert.AreEqual (0, Mp4ChapterTestBuilder.ReadFile (empty).Chapters.Count, $"{shape}: clear");
		Assert.IsNull (Mp4ChapterCodec.FindChapterTrack (Mp4ChapterTestBuilder.Moov (empty)), $"{shape}: no chapter track after the clear");
	}

	[TestMethod]
	[DataRow ("cm_only")]
	[DataRow ("cm_img")]
	[DataRow ("cm_sfirst")]
	[DataRow ("cm_audref")]
	public void Save_NewTrack_EverySoundTrakThatIsNotChapterMedia_GetsTheReference (string shape)
	{
		// A chap names a track_ID, which resolves to the first trak with it. Chapter media is that trak object
		// unless it holds a chap of its own or is a sound trak, the read path's rule. So a sound trak that only
		// shares an ID with a chapter image trak (cm_only, cm_img), or that a chap names (cm_sfirst, cm_audref),
		// is a presentation track and gets the new chapter track's entry. A chapter image trak gets none.
		var original = SharedTrackIdShape (shape);
		var ids = Mp4ChapterTestBuilder.TrackIds (Mp4ChapterTestBuilder.Moov (original));
		var moov = Mp4ChapterTestBuilder.Moov (EditChapters (original, 1));
		var track = Mp4ChapterCodec.FindChapterTrack (moov);

		Assert.IsNotNull (track, "a chap names the new chapter track");
		CollectionAssert.DoesNotContain (ids, track.Id, "a new chapter track");
		foreach (var sound in moov.Children.Where (c => c.Type == "trak" && HandlerOf (c) == "soun"))
			CollectionAssert.Contains (Mp4ChapterTestBuilder.ChapReferences (sound), track.Id, $"{shape}: every sound trak names it");
		foreach (var image in moov.Children.Where (c => c.Type == "trak" && HandlerOf (c) == "vide"))
			Assert.IsNull (image.FindChild ("tref"), "the chapter image trak gets none");
	}

	// ---- extra chapter text tracks (a set replaces every title track with one list)

	const ushort English = 0x15C7;
	const ushort German = 0x10B5;

	/// <summary>
	/// The chapter-track fixture (A: audio, chap [2]; T: titles, ID 2, English) plus a third trak X with ID 3
	/// that A's chap also names. title: X is T in German, with the same samples. url: X's three samples are
	/// URLs that carry an href modifier (3GPP TS 26.245 §5.17), in an mdat before moov. unreadable: X's chunk
	/// lies past the end of the file.
	/// </summary>
	static byte[] ExtraTextTrackShape (string kind)
	{
		var data = Mp4ChapterTestBuilder.Build (ThreeChapterTrack ());
		var moov = Mp4ChapterTestBuilder.Moov (data);
		var moovAt = data.AsSpan ().IndexOf ("moov"u8) - 4;
		Assert.AreEqual (data.Length, moovAt + (int)BinaryPrimitives.ReadUInt32BigEndian (data.AsSpan (moovAt)), "moov is last");
		var a = Mp4ChapterTestBuilder.Trak (moov, Mp4ChapterTestBuilder.AudioTrackId)!;
		var t = WithLanguage (Mp4ChapterTestBuilder.Trak (moov, Mp4ChapterTestBuilder.TextTrackId)!, English);
		var x = WithTrackId (t, 3);
		byte[] before = [];
		switch (kind) {
			case "title":
				x = WithLanguage (x, German);
				break;
			case "url": {
					var samples = new List<byte> ();
					var sizes = new List<uint> ();
					foreach (var url in new[] { "https://example.com/1", "https://example.com/2", "https://example.com/3" }) {
						var text = Encoding.UTF8.GetBytes (url);
						byte[] href = [0, 0, 0, (byte)text.Length, (byte)text.Length, .. text, 0];
						byte[] sample = [(byte)(text.Length >> 8), (byte)text.Length, .. text, .. Mp4TestBuilder.CreateBox ("href", href)];
						samples.AddRange (sample);
						sizes.Add ((uint)sample.Length);
					}

					before = Mp4TestBuilder.CreateBox ("mdat", [.. samples]);
					x = WithSamples (x, (uint)(moovAt + 8), [.. sizes]);
					break;
				}

			case "unreadable":
				x = WithSamples (x, (uint)(data.Length + 1000), [10, 10, 10]);
				break;
			default:
				throw new ArgumentException (kind);
		}

		var chap = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian (chap, Mp4ChapterTestBuilder.TextTrackId);
		BinaryPrimitives.WriteUInt32BigEndian (chap.AsSpan (4), 3);
		a = WithChild (a, "tref", _ => new Mp4Box ("tref", new BinaryData (Mp4TestBuilder.CreateBox ("chap", chap))));
		var children = moov.Children.Where (c => c.Type != "trak").ToList ();
		children.InsertRange (children.FindIndex (c => c.Type == "mvhd") + 1, [a, t, x]);
		return [.. data.AsSpan (0, moovAt), .. before, .. new Mp4Box ("moov", moov.Data, children).Render ().Span];
	}

	static Mp4Box WithLanguage (Mp4Box trak, ushort language) =>
		WithChild (trak, "mdia", m => WithChild (m, "mdhd", h => {
			var bytes = h.Data.ToArray ();
			BinaryPrimitives.WriteUInt16BigEndian (bytes.AsSpan (bytes[0] == 1 ? 32 : 20), language);
			return new Mp4Box ("mdhd", new BinaryData (bytes));
		}));

	/// <summary>The trak with one chunk at <paramref name="offset"/> holding samples of <paramref name="sizes"/>.</summary>
	static Mp4Box WithSamples (Mp4Box trak, uint offset, uint[] sizes)
	{
		var stsz = new byte[8 + (sizes.Length * 4)];
		BinaryPrimitives.WriteUInt32BigEndian (stsz.AsSpan (4), (uint)sizes.Length);
		for (var i = 0; i < sizes.Length; i++)
			BinaryPrimitives.WriteUInt32BigEndian (stsz.AsSpan (8 + (i * 4)), sizes[i]);
		var stco = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian (stco, 1);
		BinaryPrimitives.WriteUInt32BigEndian (stco.AsSpan (4), offset);
		var stsc = new byte[16];
		BinaryPrimitives.WriteUInt32BigEndian (stsc, 1);
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (4), 1);
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (8), (uint)sizes.Length);
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (12), 1);
		Mp4Box Full (string type, byte[] body) => new (type, new BinaryData ([0, 0, 0, 0, .. body]));
		return WithChild (trak, "mdia", m => WithChild (m, "minf", n => WithChild (n, "stbl", b =>
			WithChild (WithChild (WithChild (b, "stsz", _ => Full ("stsz", stsz)), "stco", _ => Full ("stco", stco)), "stsc", _ => Full ("stsc", stsc)))));
	}

	[TestMethod]
	public void Save_SetChapters_SecondTitleTrack_IsReplacedByTheOneList ()
	{
		// A file can hold one chapter title track per language (AVFoundation picks one by locale; ffprobe can
		// take a later one). The list has no locale, so a set leaves one title track: the first, rebuilt. The
		// German one goes the way a clear removes it, with A's chap entry for it.
		var original = ExtraTextTrackShape ("title");
		Assert.AreEqual (2, Mp4ChapterCodec.FindChapterTracks (Mp4ChapterTestBuilder.Moov (original)).Count, "two title tracks before");

		var saved = EditChapters (original, 1);
		var moov = Mp4ChapterTestBuilder.Moov (saved);

		CollectionAssert.AreEqual (new uint[] { 1, 2 }, Mp4ChapterTestBuilder.TrackIds (moov), "the German title trak is gone");
		CollectionAssert.AreEqual (new uint[] { 2 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		Assert.AreEqual (1, Mp4ChapterCodec.FindChapterTracks (Mp4ChapterTestBuilder.Moov (saved)).Count, "one title track after");
		CollectionAssert.AreEqual (Edit (1).Select (c => c.Title).ToArray (), Mp4ChapterTestBuilder.ReadFile (saved).Chapters.Select (c => c.Title).ToArray ());
		var again = EditChapters (saved, 2);
		Assert.AreEqual (saved.Length, again.Length, "the next edit keeps the size");
	}

	[TestMethod]
	public void Save_SetChapters_UrlTrackWithHref_IsKept ()
	{
		// Apple enhanced podcasts keep chapter links in a text track whose samples carry an href modifier.
		// That is not a title track: it stays, with its chap entry and its samples.
		var original = ExtraTextTrackShape ("url");
		var urls = Mp4ChapterTestBuilder.SampleBytes (original, 3);

		var saved = EditChapters (original, 1);
		var moov = Mp4ChapterTestBuilder.Moov (saved);

		CollectionAssert.AreEqual (new uint[] { 1, 2, 3 }, Mp4ChapterTestBuilder.TrackIds (moov));
		CollectionAssert.AreEqual (new uint[] { 2, 3 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
		CollectionAssert.AreEqual (urls, Mp4ChapterTestBuilder.SampleBytes (saved, 3), "the URL samples are unchanged");
		CollectionAssert.AreEqual (Edit (1).Select (c => c.Title).ToArray (), Mp4ChapterTestBuilder.ReadFile (saved).Chapters.Select (c => c.Title).ToArray ());
	}

	[TestMethod]
	public void Save_SetChapters_ExtraTextTrackWithUnreadableSamples_IsKept ()
	{
		// A text track whose samples lie outside the file cannot be told from a URL track, so it stays.
		var saved = EditChapters (ExtraTextTrackShape ("unreadable"), 1);
		var moov = Mp4ChapterTestBuilder.Moov (saved);

		CollectionAssert.AreEqual (new uint[] { 1, 2, 3 }, Mp4ChapterTestBuilder.TrackIds (moov));
		CollectionAssert.AreEqual (new uint[] { 2, 3 }, Mp4ChapterTestBuilder.ChapReferences (Mp4ChapterTestBuilder.Trak (moov, 1)!));
	}

	[TestMethod]
	public void Save_SetChapters_ExtraTitleTrackWithFragmentSamples_IsRefused ()
	{
		// Removing the German title track would leave its fragment samples as a stale list, as a clear would.
		var shape = ExtraTextTrackShape ("title");
		var tfhd = Mp4TestBuilder.CreateFullBox ("tfhd", 0, 0, [0, 0, 0, 3]);
		var trun = Mp4TestBuilder.CreateFullBox ("trun", 0, 0, [0, 0, 0, 0]);
		byte[] data = [.. shape, .. Mp4TestBuilder.CreateBox ("moof", Mp4TestBuilder.CreateBox ("traf", [.. tfhd, .. trun]))];
		var file = Mp4ChapterTestBuilder.ReadFile (data);

		file.Chapters = Edit (1);

		Assert.IsTrue (file.Render (data).IsEmpty, "refused");
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void Plan_ManyExtraTitleTracks_ExamineAtMostTheFileLength ()
	{
		// Each extra text track's samples are examined to tell titles from URLs. A file sets how many tracks and
		// samples there are: here 300 tracks, each one chunk at offset 0 of 65,535 one-byte samples (a constant
		// stsz), every one inside the file until its end. One budget, the file's length, covers every track.
		var data = Mp4ChapterTestBuilder.Build (ThreeChapterTrack ());
		var moov = ManyChapterTracksMoov (300);
		var stsz = new byte[12];
		BinaryPrimitives.WriteUInt32BigEndian (stsz.AsSpan (4), 1);
		BinaryPrimitives.WriteUInt32BigEndian (stsz.AsSpan (8), 65_535);
		var stsc = new byte[20];
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (4), 1);
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (8), 1);
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (12), 65_535);
		BinaryPrimitives.WriteUInt32BigEndian (stsc.AsSpan (16), 1);
		var stco = new byte[12];
		BinaryPrimitives.WriteUInt32BigEndian (stco.AsSpan (4), 1);
		Mp4Box Tables (Mp4Box stbl) =>
			WithChild (WithChild (WithChild (stbl, "stsz", _ => new Mp4Box ("stsz", new BinaryData (stsz))), "stsc", _ => new Mp4Box ("stsc", new BinaryData (stsc))), "stco", _ => new Mp4Box ("stco", new BinaryData (stco)));
		var first = Mp4ChapterCodec.FindChapterTrack (moov)!.Trak;
		var children = moov.Children.Select (c => c.Type == "trak" && c != first && HandlerOf (c) == "text"
			? WithChild (c, "mdia", m => WithChild (m, "minf", n => WithChild (n, "stbl", Tables)))
			: c).ToList ();
		moov = new Mp4Box ("moov", moov.Data, children);

		var plan = Mp4ChapterCodec.Plan (moov, [], Edit (1), TimeSpan.FromSeconds (10), quickTime: false, data);

		Assert.IsNull (plan.Error);
		Assert.IsTrue (plan.SampleSteps > 0 && plan.SampleSteps <= data.Length, $"{plan.SampleSteps:N0} steps for a {data.Length:N0}-byte file");
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void ClearChapters_ManyChapterTracks_LooksEachTrakUpOnce ()
	{
		// A clear removes every chapter-media trak, then walks the other traks' references; both passes ask whether
		// a trak is one of the removed traks. A file sets how many there are (2,000 here), so a scan of the removed
		// list on each question costs traks x chapter tracks. One set lookup per moov child keeps it linear.
		const int ChapterTracks = 2000;
		var moov = ManyChapterTracksMoov (ChapterTracks);

		var plan = Mp4ChapterCodec.Plan (moov, [], [], TimeSpan.FromSeconds (4), quickTime: false);

		Assert.AreEqual (1, plan.Children.Count (c => c.Type == "trak"), "only the audio trak is left");
		Assert.IsTrue (plan.TrakLookups > 0 && plan.TrakLookups <= moov.Children.Count, $"{plan.TrakLookups:N0} lookups for {moov.Children.Count:N0} moov children");

		var replaced = new Mp4ChapterCodec.TrakSet (Mp4ChapterCodec.FindChapterTracks (moov, textOnly: false));
		Assert.IsTrue (Mp4ChapterCodec.TryCollectReferences (moov, [], replaced, new Mp4ChapterCodec.Exclusion (0, 1)));
		Assert.IsTrue (replaced.Steps > 0 && replaced.Steps <= moov.Children.Count, $"{replaced.Steps:N0} lookups for {moov.Children.Count:N0} moov children");
	}

	/// <summary>
	/// The chapter-track fixture's moov with <paramref name="count"/> more copies of the text trak (IDs 3 and up),
	/// and the audio trak's chap reference naming all of them.
	/// </summary>
	static Mp4Box ManyChapterTracksMoov (int count)
	{
		var moov = Mp4ChapterTestBuilder.Moov (Mp4ChapterTestBuilder.Build (ThreeChapterTrack ()));
		var text = Mp4ChapterTestBuilder.Trak (moov, Mp4ChapterTestBuilder.TextTrackId)!;
		var ids = new byte[(count + 1) * 4];
		for (var i = 0; i <= count; i++)
			BinaryPrimitives.WriteUInt32BigEndian (ids.AsSpan (i * 4), (uint)(2 + i));
		var tref = new Mp4Box ("tref", new BinaryData (Mp4TestBuilder.CreateBox ("chap", ids)));
		var children = new List<Mp4Box> ();
		foreach (var child in moov.Children) {
			if (child.Type == "trak" && child.FindChild ("tref") is not null) {
				children.Add (new Mp4Box ("trak", child.Data, [.. child.Children.Select (c => c.Type == "tref" ? tref : c)]));
				continue;
			}

			children.Add (child);
			if (ReferenceEquals (child, text)) {
				for (var i = 0; i < count; i++)
					children.Add (WithTrackId (text, (uint)(3 + i)));
			}
		}

		return new Mp4Box ("moov", moov.Data, children);
	}

	static Mp4Box WithTrackId (Mp4Box trak, uint id)
	{
		var tkhd = trak.FindChild ("tkhd")!;
		var data = tkhd.Data.ToArray ();
		BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (12), id);
		return new Mp4Box ("trak", trak.Data, [.. trak.Children.Select (c => ReferenceEquals (c, tkhd) ? new Mp4Box ("tkhd", new BinaryData (data)) : c)]);
	}

	/// <summary>
	/// iloc version 1 with every field size 0, <paramref name="items"/> items of 65,535 extents each, all with
	/// <paramref name="method"/> as construction method. The FullBox header is included, as a box payload.
	/// </summary>
	static byte[] ZeroWidthIloc (int items, int method)
	{
		var body = new byte[4 + 2 + 2 + (items * 8)];
		body[0] = 1;
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (6), (ushort)items);
		for (var i = 0; i < items; i++) {
			BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (8 + (i * 8)), (ushort)(i + 1));
			BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (8 + (i * 8) + 2), (ushort)method);
			BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (8 + (i * 8) + 6), ushort.MaxValue);
		}

		return body;
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void TryIlocRanges_ZeroWidthExtents_WalkOneExtentPerItem ()
	{
		// Construction method 1 (idat) extents are not file ranges, so nothing overlaps and nothing stops the
		// walk early. With no index, offset, or length fields, each item's 65,535 extents are one extent.
		var exclusion = new Mp4ChapterCodec.Exclusion (0, 1);

		Assert.IsTrue (Mp4ChapterCodec.TryIlocRanges (ZeroWidthIloc (64, 1), exclusion));
		Assert.IsTrue (exclusion.Steps <= 64, $"{exclusion.Steps:N0} steps for 64 items");
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void PatchIloc_ZeroWidthExtents_WalkNone ()
	{
		// The offset patcher's twin: zero-width extents hold nothing to move, so none is walked.
		var payload = ZeroWidthIloc (64, 0);

		var steps = Mp4File.PatchIloc (payload, 0, 8);

		Assert.AreEqual (0, steps);
		CollectionAssert.AreEqual (ZeroWidthIloc (64, 0), payload, "nothing changed");
	}

	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void Save_TrafAuxInfo_IsAReference (bool intoChapterData)
	{
		// A traf for the audio track: one 4-byte run at an explicit base outside the chapter mdat, and aux info
		// (saiz + saio, §8.7.8-9) whose offset, from that base, lands in the chapter mdat (kept) or on the run (dropped).
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (options), 1);
		var chapterPayload = edited.AsSpan ().LastIndexOf ("mdat"u8) + 4;
		const int runAt = 100;
		var tfhd = Mp4TestBuilder.CreateFullBox ("tfhd", 0, 1, [0, 0, 0, (byte)Mp4ChapterTestBuilder.AudioTrackId, 0, 0, 0, 0, 0, 0, 0, runAt]);
		var trun = Mp4TestBuilder.CreateFullBox ("trun", 0, 0x200, [0, 0, 0, 1, 0, 0, 0, 4]);
		var auxAt = intoChapterData ? chapterPayload - runAt : 0;
		var traf = Mp4TestBuilder.CreateBox ("traf", [.. tfhd, .. trun, .. Saiz (), .. Saio (auxAt)]);
		byte[] file = [.. edited, .. Mp4TestBuilder.CreateBox ("moof", traf)];

		var again = EditChapters (file, 2);

		Assert.AreEqual (intoChapterData ? 3 : 2, Mp4ChapterTestBuilder.TopLevelTypes (again).Count (t => t == "mdat"));
	}

	static byte[] Meta (params byte[][] children) => Mp4TestBuilder.CreateFullBox ("meta", 0, 0, [.. children.SelectMany (c => c)]);

	/// <summary>
	/// iloc (§8.11.3) version 1, 4-byte offsets and lengths, no base or index: one item, one 4-byte extent at
	/// <paramref name="offset"/>, with <paramref name="method"/> as its construction method.
	/// </summary>
	static byte[] Iloc (int method, int offset, int length = 4)
	{
		var body = new byte[2 + 2 + 2 + 2 + 2 + 2 + 4 + 4];
		body[0] = 0x44;
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (2), 1);
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (4), 1);
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (6), (ushort)method);
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (10), 1);
		BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (12), (uint)offset);
		BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (16), (uint)length);
		return Mp4TestBuilder.CreateFullBox ("iloc", 1, 0, body);
	}

	/// <summary>saiz (§8.7.8): every sample's aux info is 4 bytes, one sample.</summary>
	static byte[] Saiz () => Mp4TestBuilder.CreateFullBox ("saiz", 0, 0, [4, 0, 0, 0, 1]);

	/// <summary>saio (§8.7.9) version 1: one 64-bit offset.</summary>
	static byte[] Saio64 (int offset)
	{
		var body = new byte[12];
		BinaryPrimitives.WriteUInt32BigEndian (body, 1);
		BinaryPrimitives.WriteUInt64BigEndian (body.AsSpan (4), (ulong)offset);
		return Mp4TestBuilder.CreateFullBox ("saio", 1, 0, body);
	}

	/// <summary>saio (§8.7.9) version 0: one 32-bit offset.</summary>
	static byte[] Saio (int offset) => Mp4TestBuilder.CreateFullBox ("saio", 0, 0, [0, 0, 0, 1, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset]);

	[TestMethod]
	public void Save_ChapterSamplesShareMdatWithAudio_LeavesThatMdatAlone ()
	{
		// The documented case: the old samples sit in the audio mdat, so they stay there unreferenced.
		var original = Mp4ChapterTestBuilder.Build (ThreeChapterTrack ());
		var audioMdat = Mp4ChapterTestBuilder.TopLevelTypes (original).IndexOf ("mdat");

		var edited = EditChapters (original, 1);

		var types = Mp4ChapterTestBuilder.TopLevelTypes (edited);
		Assert.AreEqual (2, types.Count (t => t == "mdat"));
		Assert.AreEqual (audioMdat, types.IndexOf ("mdat"), "the shared mdat keeps its place");
		CollectionAssert.AreEqual (Mp4ChapterTestBuilder.AudioPayload (), Mp4ChapterTestBuilder.SampleBytes (edited, 1));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesChapters ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.m4a", Mp4ChapterTestBuilder.Build (new ()));
		var file = (await Mp4File.ReadFromFileAsync ("/a.m4a", fs)).File!;
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "A"), new Mp4Chapter (TimeSpan.FromSeconds (3), "B")];

		var write = await file.SaveToFileAsync ("/b.m4a", fs);

		Assert.IsTrue (write.IsSuccess, write.Error);
		AssertChapters (Mp4File.ReadFromFile ("/b.m4a", fs).File!.Chapters, (0, "A"), (3, "B"));
	}

	[TestMethod]
	public void SaveToFile_WritesChapters ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.m4a", Mp4ChapterTestBuilder.Build (ThreeChapterTrack ()));
		var file = Mp4File.ReadFromFile ("/a.m4a", fs).File!;
		file.Chapters = [new Mp4Chapter (TimeSpan.Zero, "Only")];

		Assert.IsTrue (file.SaveToFile (fs).IsSuccess);

		AssertChapters (Mp4File.ReadFromFile ("/a.m4a", fs).File!.Chapters, (0, "Only"));
	}

	// ---- setter

	[TestMethod]
	public void Chapters_Set_SortsByStartAndDerivesEndTimes ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (new ()));

		file.Chapters = [new Mp4Chapter (TimeSpan.FromSeconds (4), "B"), new Mp4Chapter (TimeSpan.Zero, "A"), new Mp4Chapter (TimeSpan.FromSeconds (4), "C")];

		AssertChapters (file.Chapters, (0, "A"), (4, "B"), (4, "C"));
		Assert.AreEqual (TimeSpan.FromSeconds (4), file.Chapters[0].EndTime);
		Assert.AreEqual (TimeSpan.FromSeconds (4), file.Chapters[1].EndTime);
		Assert.AreEqual (Movie, file.Chapters[2].EndTime);
	}

	[TestMethod]
	public void Chapters_SetNullElement_Throws ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (new ()));

		Assert.ThrowsExactly<ArgumentException> (() => file.Chapters = [null!]);
	}

	[TestMethod]
	public void Chapter_Constructor_RejectsNegativeStartAndNullTitle ()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new Mp4Chapter (TimeSpan.FromTicks (-1), "x"));
		Assert.ThrowsExactly<ArgumentNullException> (() => new Mp4Chapter (TimeSpan.Zero, null!));
		Assert.IsNull (new Mp4Chapter (TimeSpan.Zero, "x").EndTime);
	}

	[TestMethod]
	public void Chapter_Equality_ComparesStartAndTitle ()
	{
		var a = new Mp4Chapter (TimeSpan.FromSeconds (1), "x");

		Assert.AreEqual (a, new Mp4Chapter (TimeSpan.FromSeconds (1), "x"));
		Assert.AreEqual (a.GetHashCode (), new Mp4Chapter (TimeSpan.FromSeconds (1), "x").GetHashCode ());
		Assert.AreNotEqual (a, new Mp4Chapter (TimeSpan.FromSeconds (1), "y"));
		Assert.AreNotEqual (a, new Mp4Chapter (TimeSpan.FromSeconds (2), "x"));
		Assert.IsFalse (a.Equals (null));
	}

	[TestMethod]
	public void Dispose_ClearsChapters ()
	{
		var file = Mp4ChapterTestBuilder.ReadFile (Mp4ChapterTestBuilder.Build (ThreeChapterTrack ()));

		file.Dispose ();

		Assert.AreEqual (0, file.Chapters.Count);
	}

	// ---- malformed input

	[TestMethod]
	[TestCategory ("Security")]
	public void Read_TruncatedAtEveryByte_DoesNotThrow ()
	{
		foreach (var moovFirst in new[] { false, true }) {
			var options = ThreeChapterTrack ();
			options.MoovFirst = moovFirst;
			options.Chpl = ThreeChpl ();
			var data = Mp4ChapterTestBuilder.Build (options);

			for (var length = 0; length <= data.Length; length++) {
				var result = Mp4File.Read (data.AsSpan (0, length));
				if (result.IsSuccess)
					Assert.IsNotNull (result.File!.Chapters);
				using var stream = new MemoryStream (data, 0, length);
				_ = MediaFile.Read (stream, "x.m4a");
			}
		}
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void Read_CorruptedByteInMoov_DoesNotThrow ()
	{
		var options = ThreeChapterTrack ();
		options.Chpl = ThreeChpl ();
		var data = Mp4ChapterTestBuilder.Build (options);
		var moovStart = data.Length - Mp4ChapterTestBuilder.Moov (data).Render ().Length;

		foreach (var value in new byte[] { 0x00, 0xFF, 0x7F }) {
			for (var at = moovStart; at < data.Length; at++) {
				var copy = (byte[])data.Clone ();
				copy[at] = value;
				var result = Mp4File.Read (copy);
				if (result.IsSuccess)
					_ = result.File!.Render (copy);
			}
		}
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void ParseChpl_CountPastBody_StopsAtLastWholeEntry ()
	{
		var chpl = Mp4ChapterTestBuilder.Chpl ([(TimeSpan.Zero, "A"), (TimeSpan.FromSeconds (1), "B")]);
		var payload = chpl.AsSpan (8).ToArray ();
		payload[8] = 255;

		var chapters = Mp4ChapterCodec.ParseChpl (payload);

		Assert.AreEqual (2, chapters.Count);
		Assert.AreEqual (0, Mp4ChapterCodec.ParseChpl ([]).Count);
		Assert.AreEqual (0, Mp4ChapterCodec.ParseChpl ([1, 0, 0, 0]).Count);
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void ReadLayout_HugeConstantSampleCount_IsCapped ()
	{
		var data = Mp4ChapterTestBuilder.Build (ThreeChapterTrack ());
		var moov = Mp4ChapterTestBuilder.Moov (data);
		var stsz = moov.Children.Where (c => c.Type == "trak").Last ().FindDescendant ("mdia", "minf", "stbl", "stsz")!;
		var at = IndexOf (data, stsz.Render ().ToArray ());
		// The chapter track: sample_size 1, sample_count 0xFFFFFFFF, and one chunk with 0xFFFFFFFF samples.
		BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (at + 12), 1);
		BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (at + 16), uint.MaxValue);
		var stscText = LastIndexOf (data, [.. "stsc"u8]) - 4;
		BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (stscText + 20), uint.MaxValue);

		var file = Mp4ChapterTestBuilder.ReadFile (data);

		Assert.IsTrue (file.Chapters.Count <= Mp4ChapterCodec.MaxTrackChapters);
	}

	[TestMethod]
	[TestCategory ("Security")]
	public void ReadLayout_OverlappingHugeSamples_StopAtReadBudget ()
	{
		// Every sample claims 4 GiB at the same offset. Reading stops once the bytes read reach the file length.
		var options = new Mp4ChapterTestBuilder.Options { RawSamples = [.. Enumerable.Repeat (new byte[] { 0, 1, (byte)'x' }, 50)] };
		var data = Mp4ChapterTestBuilder.Build (options);
		var stszText = LastIndexOf (data, [.. "stsz"u8]) - 4;
		for (var i = 0; i < 50; i++)
			BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (stszText + 20 + (i * 4)), uint.MaxValue);
		var track = Mp4ChapterCodec.FindChapterTrack (Mp4ChapterTestBuilder.Moov (data))!;

		var layout = Mp4ChapterCodec.ReadLayout (track, 1000, 3 * 65_537);

		Assert.IsNotNull (layout);
		Assert.AreEqual (3, layout.Samples.Count);
		Assert.IsTrue (layout.Samples.All (s => s.ReadLength == 65_537));
	}

	static int IndexOf (byte[] data, byte[] pattern) => data.AsSpan ().IndexOf (pattern);

	static int LastIndexOf (byte[] data, byte[] pattern) => data.AsSpan ().LastIndexOf (pattern);
}
