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
	public void Save_ChapterMdatNextToFragmentData_IsKeptWhenATrafPointsIntoIt ()
	{
		// A traf whose explicit base_data_offset lands in the chapter mdat after moov: that mdat is not chapter-only.
		var options = ThreeChapterTrack ();
		options.Mvex = true;
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (options), 1);
		var chapterPayload = edited.AsSpan ().LastIndexOf ("mdat"u8) + 4;
		var baseOffset = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian (baseOffset, (ulong)chapterPayload);
		byte[] tfhd = [0, 0, 0, 24, (byte)'t', (byte)'f', (byte)'h', (byte)'d', 0, 0, 0, 1, 0, 0, 0, (byte)Mp4ChapterTestBuilder.AudioTrackId,
			.. baseOffset];
		byte[] trun = [0, 0, 0, 20, (byte)'t', (byte)'r', (byte)'u', (byte)'n', 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 4];
		var moof = Mp4TestBuilder.CreateBox ("moof", Mp4TestBuilder.CreateBox ("traf", [.. tfhd, .. trun]));
		byte[] withMoof = [.. edited, .. moof];

		var again = EditChapters (withMoof, 2);

		Assert.AreEqual (3, Mp4ChapterTestBuilder.TopLevelTypes (again).Count (t => t == "mdat"));
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
	public void Save_ChapterMdatReferencedByAnotherTrack_IsKept ()
	{
		// The audio track's chunk offset is moved into the chapter mdat. That mdat is no longer chapter-only.
		var edited = EditChapters (Mp4ChapterTestBuilder.Build (new ()), 1);
		var chapterPayload = edited.AsSpan ().LastIndexOf ("mdat"u8) + 4;
		var audioStco = edited.AsSpan ().IndexOf ("stco"u8) - 4;
		BinaryPrimitives.WriteUInt32BigEndian (edited.AsSpan (audioStco + 16), (uint)chapterPayload);

		var again = EditChapters (edited, 2);

		Assert.AreEqual (3, Mp4ChapterTestBuilder.TopLevelTypes (again).Count (t => t == "mdat"));
		var audioAt = BinaryPrimitives.ReadUInt32BigEndian (Mp4ChapterTestBuilder.Trak (Mp4ChapterTestBuilder.Moov (again), 1)!
			.FindDescendant ("mdia", "minf", "stbl", "stco")!.Data.Span.Slice (8));
		var moved = again.AsSpan ((int)audioAt, 8).ToArray ();
		CollectionAssert.AreEqual (edited.AsSpan (chapterPayload, 8).ToArray (), moved, "the audio offset still finds the same bytes");
	}

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
