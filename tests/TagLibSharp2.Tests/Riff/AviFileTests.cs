// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Avi")]
public class AviFileTests
{
	[TestMethod]
	public void Read_VideoAndAudio_ReadsStreamProperties ()
	{
		var bytes = new AviTestBuilder { Width = 6, Height = 4, Rate = 30000, Scale = 1001, Frames = 9 }.Build ();

		var result = AviFile.Read (bytes);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual (MediaFormat.Avi, file.Format);
		Assert.AreEqual (MediaTypes.Audio | MediaTypes.Video, file.MediaTypes);
		Assert.AreEqual (TimeSpan.FromTicks (9 * 1001 * TimeSpan.TicksPerSecond / 30000), file.Duration);
		var video = file.VideoProperties!.Value;
		Assert.AreEqual (6, video.Width);
		Assert.AreEqual (4, video.Height);
		Assert.AreEqual (30000d / 1001, video.FrameRate, 1e-9);
		Assert.AreEqual ("Y800", video.Codec);
		Assert.AreEqual (file.Duration, video.Duration);
		var audio = file.AudioProperties!.Value;
		Assert.AreEqual (8000, audio.SampleRate);
		Assert.AreEqual (1, audio.Channels);
		Assert.AreEqual (16, audio.BitsPerSample);
		Assert.AreEqual (128, audio.Bitrate);
		Assert.AreEqual ("PCM", audio.Codec);
	}

	[TestMethod]
	public void Read_OpenDml_CountsFramesPastTheFirstRiff ()
	{
		// avih and strh both stop at the first RIFF; only dmlh counts the AVIX frames.
		var bytes = new AviTestBuilder { OpenDml = true, Frames = 5, AvixFrames = 20, MainTotalFrames = 5, VideoLength = 5 }.Build ();

		var file = AviFile.Read (bytes).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (1), file.Duration);
	}

	[TestMethod]
	public void Read_NoDmlh_UsesTheVideoStreamLength ()
	{
		var bytes = new AviTestBuilder { Frames = 5, MainTotalFrames = 3, VideoLength = 50 }.Build ();

		Assert.AreEqual (TimeSpan.FromSeconds (2), AviFile.Read (bytes).File!.Duration);
	}

	[TestMethod]
	public void Read_NoStreamLength_UsesTheMainHeaderFrameCount ()
	{
		var bytes = new AviTestBuilder { Frames = 5, MainTotalFrames = 75, VideoLength = 0 }.Build ();

		Assert.AreEqual (TimeSpan.FromSeconds (3), AviFile.Read (bytes).File!.Duration);
	}

	[TestMethod]
	public void Read_ZeroStreamRate_UsesMicroSecondsPerFrame ()
	{
		var bytes = new AviTestBuilder { Rate = 0, Audio = false, Frames = 10, MicroSecondsPerFrame = 100_000 }.Build ();

		var file = AviFile.Read (bytes).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (1), file.Duration);
		Assert.AreEqual (10d, file.VideoProperties!.Value.FrameRate, 1e-9);
	}

	[TestMethod]
	public void Read_HugeStreamLength_DoesNotOverflowTheDuration ()
	{
		var bytes = new AviTestBuilder { Rate = 1, Scale = uint.MaxValue, VideoLength = uint.MaxValue }.Build ();

		var result = AviFile.Read (bytes);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (TimeSpan.Zero, result.File!.Duration);
	}

	[TestMethod]
	public void Read_AudioOnly_TimesTheAudioStream ()
	{
		var bytes = new AviTestBuilder { Video = false, Frames = 50 }.Build ();

		var file = AviFile.Read (bytes).File!;

		Assert.AreEqual (MediaTypes.Audio, file.MediaTypes);
		Assert.IsNull (file.VideoProperties);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.AudioProperties!.Value.Duration);
		Assert.IsNull (((IMediaFile)file).VideoProperties);
		Assert.IsNotNull (((IMediaFile)file).AudioProperties);
		Assert.IsNull (((IMediaFile)file).ImageProperties);
	}

	[TestMethod]
	public void Read_VideoOnly_HasNoAudioProperties ()
	{
		var file = AviFile.Read (new AviTestBuilder { Audio = false }.Build ()).File!;

		Assert.AreEqual (MediaTypes.Video, file.MediaTypes);
		Assert.IsNull (((IMediaFile)file).AudioProperties);
	}

	[TestMethod]
	public void Read_TopDownBitmap_ReportsAPositiveHeight ()
	{
		var file = AviFile.Read (new AviTestBuilder { Height = 8, TopDown = true }.Build ()).File!;

		Assert.AreEqual (8, file.VideoProperties!.Value.Height);
	}

	[TestMethod]
	public void Read_ShortBitmapHeader_FallsBackToTheMainHeaderSize ()
	{
		var bytes = new AviTestBuilder { Width = 12, Height = 10 }.Build ();
		// Zero biWidth: the size comes from avih dwWidth / dwHeight.
		var strf = IndexOf (bytes, "strf"u8);
		BinaryPrimitives.WriteInt32LittleEndian (bytes.AsSpan (strf + 8 + 4), 0);

		var video = AviFile.Read (bytes).File!.VideoProperties!.Value;

		Assert.AreEqual (12, video.Width);
		Assert.AreEqual (10, video.Height);
	}

	[TestMethod]
	[DataRow (0u, "RGB")]
	[DataRow (1u, "RLE8")]
	[DataRow (2u, "RLE4")]
	[DataRow (3u, "RGB")]
	[DataRow (0x47504A4Du, "MJPG")]
	[DataRow (0x20424944u, "DIB")]
	public void Read_Compression_NamesTheCodec (uint compression, string expected)
	{
		var bytes = new AviTestBuilder ().Build ();
		var strf = IndexOf (bytes, "strf"u8);
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (strf + 8 + 16), compression);

		Assert.AreEqual (expected, AviFile.Read (bytes).File!.VideoProperties!.Value.Codec);
	}

	[TestMethod]
	public void Read_UnprintableCompression_UsesTheStreamHandler ()
	{
		var bytes = new AviTestBuilder ().Build ();
		var strf = IndexOf (bytes, "strf"u8);
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (strf + 8 + 16), 0x01020304);

		Assert.AreEqual ("Y800", AviFile.Read (bytes).File!.VideoProperties!.Value.Codec);
	}

	[TestMethod]
	[DataRow ((ushort)0x0055, "MP3")]
	[DataRow ((ushort)0x2000, "AC-3")]
	[DataRow ((ushort)0x00FF, "AAC")]
	[DataRow ((ushort)0x0003, "IEEE Float")]
	[DataRow ((ushort)0x1234, "0x1234")]
	public void Read_AudioFormatTag_NamesTheCodec (ushort tag, string expected)
	{
		var bytes = new AviTestBuilder ().Build ();
		var strf = LastIndexOf (bytes, "strf"u8);
		BinaryPrimitives.WriteUInt16LittleEndian (bytes.AsSpan (strf + 8), tag);

		Assert.AreEqual (expected, AviFile.Read (bytes).File!.AudioProperties!.Value.Codec);
	}

	[TestMethod]
	public void Read_InfoList_FillsTheInfoTag ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.AddRange ([("INAM", "Movie"), ("IART", "Director"), ("ICMT", "Notes"), ("ICRD", "2024"), ("IGNR", "Drama")]);

		var result = MediaFile.ReadFromData (builder.Build (), "movie.avi");

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Avi, result.Format);
		Assert.AreEqual ("Movie", result.Tag!.Title);
		Assert.AreEqual ("Director", result.Tag.Artist);
		Assert.AreEqual ("Notes", result.Tag.Comment);
		Assert.AreEqual ("2024", result.Tag.Year);
		Assert.AreEqual ("Drama", result.Tag.Genre);
		Assert.AreSame (result.GetFileAs<AviFile> ()!.InfoTag, result.Tag);
	}

	[TestMethod]
	public void Read_TwoInfoLists_FirstListWins ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "First"));
		builder.InfoAfterMovi.AddRange ([("INAM", "Second"), ("IART", "Later")]);

		var info = AviFile.Read (builder.Build ()).File!.InfoTag;

		Assert.AreEqual ("First", info.Title);
		Assert.AreEqual ("Later", info.Artist);
	}

	[TestMethod]
	public void Read_NoInfoList_GivesAnEmptyTag ()
	{
		var file = AviFile.Read (new AviTestBuilder ().Build ()).File!;

		Assert.IsNotNull (file.InfoTag);
		Assert.IsTrue (file.InfoTag.IsEmpty);
		Assert.IsNull (file.Id3v2Tag);
		Assert.AreSame (file.InfoTag, ((IMediaFile)file).Tag);
	}

	[TestMethod]
	public void Read_Id32Chunk_ReadsTheId3v2Tag ()
	{
		var builder = new AviTestBuilder { Id3 = Id3 ("Tag Title", "Tag Album") };
		builder.Info.Add (("INAM", "Info Title"));

		var result = MediaFile.ReadFromData (builder.Build ());
		var file = result.GetFileAs<AviFile> ()!;

		Assert.AreEqual ("Tag Title", file.Id3v2Tag!.Title);
		Assert.AreEqual ("Info Title", result.Tag!.Title, "The INFO list is the tag other tools read, so it comes first.");
		Assert.AreEqual ("Tag Album", result.Tag.Album, "A field INFO lacks falls through to ID3v2.");
		Assert.AreEqual ("Info Title", ((IMediaFile)file).Tag!.Title);
	}

	[TestMethod]
	public void Read_UnparsableId32_LeavesTheTagNull ()
	{
		var file = AviFile.Read (new AviTestBuilder { Id3 = [1, 2, 3, 4] }.Build ()).File!;

		Assert.IsNull (file.Id3v2Tag);
	}

	[TestMethod]
	public void Read_WaveForm_IsNotAvi ()
	{
		var wav = TestBuilders.Wav.CreateMinimal ();

		Assert.IsFalse (AviFile.IsValidFormat (wav));
		Assert.IsFalse (AviFile.Read (wav).IsSuccess);
		Assert.AreEqual (MediaFormat.Wav, MediaFile.DetectFormat (wav));
		Assert.AreEqual (MediaFormat.Wav, MediaFile.ReadFromData (wav, "x.avi").Format);
	}

	[TestMethod]
	public void DetectFormat_AviForm_IsAvi ()
	{
		var bytes = new AviTestBuilder ().Build ();

		Assert.IsTrue (AviFile.IsValidFormat (bytes));
		Assert.AreEqual (MediaFormat.Avi, MediaFile.DetectFormat (bytes.AsSpan (0, 12)));
		Assert.AreEqual (MediaFormat.Avi, MediaFile.DetectFormat (bytes, "x.wav"));
	}

	[TestMethod]
	public void DetectFormat_AviExtension_IsTheFallback ()
	{
		Assert.AreEqual (MediaFormat.Avi, MediaFile.DetectFormat ([0, 0, 0, 0], "clip.AVI"));
		var failed = MediaFile.ReadFromData (new byte[16], "clip.avi");
		Assert.IsFalse (failed.IsSuccess);
	}

	[TestMethod]
	public void Read_NoHeaderList_Fails ()
	{
		var bytes = Riff (Chunk ("JUNK", new byte[4]));

		var result = AviFile.Read (bytes);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "hdrl");
	}

	[TestMethod]
	public void TryRead_ReportsSuccessAndFailure ()
	{
		Assert.IsTrue (AviFile.TryRead (new AviTestBuilder ().Build (), out var file));
		Assert.IsNotNull (file);
		Assert.IsFalse (AviFile.TryRead ([1, 2, 3], out var none));
		Assert.IsNull (none);
	}

	[TestMethod]
	public void Read_Stream_MatchesTheByteRead ()
	{
		var builder = new AviTestBuilder { OpenDml = true, AvixFrames = 3, Id3 = Id3 ("Streamed", "Album") };
		builder.Info.Add (("INAM", "Window"));
		builder.InfoAfterMovi.Add (("IART", "After"));
		var bytes = builder.Build ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.avi", bytes);

		var loaded = AviFile.Read (bytes).File!;
		var streamed = MediaFile.Read ("/clip.avi", fs);

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		var file = streamed.GetFileAs<AviFile> ()!;
		Assert.AreEqual ("/clip.avi", file.SourcePath);
		Assert.AreEqual (loaded.Duration, file.Duration);
		Assert.AreEqual (loaded.VideoProperties, file.VideoProperties);
		Assert.AreEqual (loaded.AudioProperties, file.AudioProperties);
		Assert.AreEqual ("Window", file.InfoTag.Title);
		Assert.AreEqual ("After", file.InfoTag.Artist);
		Assert.AreEqual ("Streamed", file.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task ReadAsync_Stream_MatchesTheByteRead ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Async"));
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.avi", builder.Build ());

		var result = await MediaFile.ReadAsync ("/clip.avi", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Avi, result.Format);
		Assert.AreEqual ("Async", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_NonSeekableStream_ReadsTheCopy ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Pipe"));

		var result = MediaFile.Read (new NonSeekableStream (builder.Build ()));

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Pipe", result.Tag!.Title);
	}

	[TestMethod]
	public async Task ReadFromFile_SetsTheSourcePath ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.avi", new AviTestBuilder ().Build ());

		var sync = AviFile.ReadFromFile ("/a.avi", fs);
		var async = await AviFile.ReadFromFileAsync ("/a.avi", fs);

		Assert.AreEqual ("/a.avi", sync.File!.SourcePath);
		Assert.AreEqual ("/a.avi", async.File!.SourcePath);
		Assert.IsFalse (AviFile.ReadFromFile ("/missing.avi", fs).IsSuccess);
		Assert.IsFalse ((await AviFile.ReadFromFileAsync ("/missing.avi", fs)).IsSuccess);
	}

	[TestMethod]
	public async Task ReadAsync_EveryTruncation_MatchesTheSyncRead ()
	{
		var builder = new AviTestBuilder { OpenDml = true, AvixFrames = 2, Id3 = Id3 ("T", "A") };
		builder.Info.Add (("INAM", "Cut"));
		var bytes = builder.Build ();

		for (var length = 0; length <= bytes.Length; length += 3) {
			var prefix = bytes.AsSpan (0, length).ToArray ();
			var sync = AviFile.Read (new MemoryStream (prefix), null, null);
			var async = await AviFile.ReadAsync (new MemoryStream (prefix), null, null, CancellationToken.None);
			Assert.AreEqual (sync.IsSuccess, async.IsSuccess, $"length {length}");
			Assert.AreEqual (sync.File?.InfoTag.Title, async.File?.InfoTag.Title, $"length {length}");
			Assert.AreEqual (sync.File?.Duration, async.File?.Duration, $"length {length}");
		}
	}

	[TestMethod]
	public async Task ReadAsync_NonSeekableStream_ReadsTheCopy ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Pipe"));

		var result = await AviFile.ReadAsync (new NonSeekableStream (builder.Build ()) { Position = 0 }, "/p.avi", null, CancellationToken.None);
		var seekable = await AviFile.ReadAsync (new MemoryStream (builder.Build ()) { Position = 5 }, null, null, CancellationToken.None);

		Assert.AreEqual ("Pipe", result.File!.InfoTag.Title);
		Assert.AreEqual ("/p.avi", result.File.SourcePath);
		Assert.AreEqual ("Pipe", seekable.File!.InfoTag.Title);
	}

	[TestMethod]
	public async Task ReadFromFile_UnreadableFile_Fails ()
	{
		var fs = new WholeFileCountingFileSystem ();
		fs.AddFile ("/a.avi", new AviTestBuilder ().Build ());

		fs.OpenFailure = new UnauthorizedAccessException ("denied");
		StringAssert.Contains (AviFile.ReadFromFile ("/a.avi", fs).Error, "Access denied");
		StringAssert.Contains ((await AviFile.ReadFromFileAsync ("/a.avi", fs)).Error, "Access denied");
		fs.OpenFailure = new IOException ("disk");
		StringAssert.Contains (AviFile.ReadFromFile ("/a.avi", fs).Error, "Failed to read file");
		StringAssert.Contains ((await AviFile.ReadFromFileAsync ("/a.avi", fs)).Error, "Failed to read file");
		Assert.ThrowsExactly<ArgumentNullException> (() => AviFile.ReadFromFile (null!, fs));
	}

	[TestMethod]
	public void Read_EveryTruncation_DoesNotThrow ()
	{
		var builder = new AviTestBuilder { OpenDml = true, AvixFrames = 2, Id3 = Id3 ("T", "A") };
		builder.Info.Add (("INAM", "Cut"));
		var bytes = builder.Build ();

		for (var length = 0; length <= bytes.Length; length++) {
			var prefix = bytes.AsSpan (0, length).ToArray ();
			var result = AviFile.Read (prefix);
			if (length >= 12)
				Assert.AreEqual (result.IsSuccess, MediaFile.Read (new MemoryStream (prefix)).IsSuccess, $"length {length}");
		}
	}

	[TestMethod]
	public void Read_ChunkSizesPastTheEnd_AreClamped ()
	{
		var bytes = new AviTestBuilder ().Build ();
		var hdrl = IndexOf (bytes, "hdrl"u8) - 8;
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (4), 0xFFFFFFFF);
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (hdrl + 4), (uint)bytes.Length * 4);

		var result = AviFile.Read (bytes);
		var streamed = MediaFile.Read (new MemoryStream (bytes));

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual (result.File!.VideoProperties, streamed.GetFileAs<AviFile> ()!.VideoProperties);
	}

	[TestMethod]
	public void Read_HeaderListLargerThanTheTagLimit_IsNotRead ()
	{
		var bytes = new AviTestBuilder ().Build ();
		var hdrl = IndexOf (bytes, "hdrl"u8) - 8;
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (hdrl + 4), AviFile.MaxTagChunkBytes + 2);

		Assert.IsFalse (AviFile.Read (bytes).IsSuccess);
		Assert.IsFalse (MediaFile.Read (new MemoryStream (bytes)).IsSuccess);
	}

	[TestMethod]
	public void Dispose_ReleasesTheId3Tag ()
	{
		var file = AviFile.Read (new AviTestBuilder { Id3 = Id3 ("T", "A") }.Build ()).File!;

		file.Dispose ();
		file.Dispose ();

		Assert.IsNull (file.Id3v2Tag);
	}

	[TestMethod]
	public void ReadResult_Equality ()
	{
		var failure = AviFileReadResult.Failure ("x");
		Assert.AreEqual (failure, AviFileReadResult.Failure ("x"));
		Assert.IsTrue (failure == AviFileReadResult.Failure ("x"));
		Assert.IsTrue (failure != AviFileReadResult.Failure ("y"));
		Assert.IsFalse (failure.Equals ((object)"x"));
		Assert.AreEqual (failure.GetHashCode (), AviFileReadResult.Failure ("x").GetHashCode ());
	}

	[TestMethod]
	public void Read_UnfinalizedRiff_ReportsNoDuration ()
	{
		// A piped writer leaves RIFF size 0xFFFFFFFF and placeholder stream lengths it never patches.
		var bytes = new AviTestBuilder { VideoLength = 0x40000000 }.Build ();
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (4), 0xFFFFFFFF);

		var file = AviFile.Read (bytes).File!;
		var streamed = MediaFile.Read (new MemoryStream (bytes)).GetFileAs<AviFile> ()!;

		Assert.AreEqual (TimeSpan.Zero, file.Duration);
		Assert.AreEqual (TimeSpan.Zero, file.VideoProperties!.Value.Duration);
		Assert.AreEqual (TimeSpan.Zero, file.AudioProperties!.Value.Duration);
		Assert.AreEqual (4, file.VideoProperties.Value.Width);
		Assert.AreEqual (TimeSpan.Zero, streamed.Duration);
	}

	[TestMethod]
	public void Read_Utf8InfoText_DecodesAsUtf8 ()
	{
		var builder = new AviTestBuilder { InfoEncoding = System.Text.Encoding.UTF8 };
		builder.Info.AddRange ([("INAM", "Café ☕"), ("IART", "Björk")]);

		var info = AviFile.Read (builder.Build ()).File!.InfoTag;

		Assert.AreEqual ("Café ☕", info.Title);
		Assert.AreEqual ("Björk", info.Artist);
	}

	[TestMethod]
	public void Read_Utf8Cset_DecodesUtf8 ()
	{
		var builder = new AviTestBuilder { CharacterSet = 65001, InfoEncoding = System.Text.Encoding.UTF8 };
		builder.Info.Add (("INAM", "Café ☕"));
		var bytes = builder.Build ();

		Assert.AreEqual ("Café ☕", AviFile.Read (bytes).File!.InfoTag.Title);
		Assert.AreEqual ("Café ☕", MediaFile.Read (new MemoryStream (bytes)).Tag!.Title);
	}

	[TestMethod]
	[DataRow ((ushort)0)]
	[DataRow ((ushort)28591)]
	public void Read_Latin1Cset_ReadsEveryByteAsLatin1 (ushort codePage)
	{
		// The CSET says ISO 8859-1, so bytes that happen to be valid UTF-8 are still Latin-1.
		var builder = new AviTestBuilder { CharacterSet = codePage, InfoEncoding = System.Text.Encoding.UTF8 };
		builder.Info.Add (("INAM", "Café"));

		var bytes = builder.Build ();

		Assert.AreEqual ("CafÃ©", AviFile.Read (bytes).File!.InfoTag.Title);
		Assert.AreEqual ("CafÃ©", MediaFile.Read (new MemoryStream (bytes)).Tag!.Title, "A stream read keeps the CSET.");
	}

	[TestMethod]
	public void Read_ShortCset_FallsBackToDetection ()
	{
		var builder = new AviTestBuilder { InfoEncoding = System.Text.Encoding.UTF8, ChunkBeforeJunk = ("CSET", [0xE9]) };
		builder.Info.Add (("INAM", "Café"));

		Assert.AreEqual ("Café", AviFile.Read (builder.Build ()).File!.InfoTag.Title);
	}

	[TestMethod]
	public void Read_Latin1InfoText_FallsBackToLatin1 ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Café"));

		Assert.AreEqual ("Café", AviFile.Read (builder.Build ()).File!.InfoTag.Title);
	}

	[TestMethod]
	public void Read_ManyTinyChunks_AllocatesLessThanTheFile ()
	{
		var file = WithTinyChunks (100_000);

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var read = AviFile.Read (file);
		var spanAllocated = GC.GetAllocatedBytesForCurrentThread () - before;
		before = GC.GetAllocatedBytesForCurrentThread ();
		var streamed = MediaFile.Read (new MemoryStream (file), "x.avi");
		var streamAllocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.IsLessThan ((long)file.Length, spanAllocated, $"span read allocated {spanAllocated} for {file.Length}");
		Assert.IsLessThan ((long)file.Length, streamAllocated, $"stream read allocated {streamAllocated} for {file.Length}");
	}

	[TestMethod]
	public async Task ReadFromFile_DoesNotLoadTheWholeFile ()
	{
		var builder = new AviTestBuilder { Frames = 200, Width = 64, Height = 64 };
		builder.Info.Add (("INAM", "Window"));
		var bytes = builder.Build ();
		var fs = new WholeFileCountingFileSystem ();
		fs.AddFile ("/clip.avi", bytes);

		var sync = AviFile.ReadFromFile ("/clip.avi", fs);
		var async = await AviFile.ReadFromFileAsync ("/clip.avi", fs);

		Assert.AreEqual ("Window", sync.File!.InfoTag.Title);
		Assert.AreEqual ("Window", async.File!.InfoTag.Title);
		Assert.AreEqual ("/clip.avi", async.File.SourcePath);
		Assert.AreEqual (0, fs.WholeFileReads);
		Assert.IsLessThan (bytes.Length / 4L, fs.BytesRead);
		Assert.IsFalse ((await AviFile.ReadFromFileAsync ("/clip.avi", fs, new CancellationToken (true))).IsSuccess);
	}

	[TestMethod]
	public void Read_ManyInfoFields_AllocatesInProportion ()
	{
		// Each field costs its id, its bytes and its text: linear in the list, never more.
		static long Allocated (int fields)
		{
			var builder = new AviTestBuilder ();
			for (var i = 0; i < fields; i++)
				builder.InfoBytes.Add (("ICMT", [(byte)'x', 0]));
			var bytes = builder.Build ();
			var before = GC.GetAllocatedBytesForCurrentThread ();
			Assert.IsTrue (AviFile.Read (bytes).IsSuccess);
			return GC.GetAllocatedBytesForCurrentThread () - before;
		}

		var small = Allocated (20_000);
		var large = Allocated (80_000);

		Assert.IsLessThan (80_000L * 10 * 16, large, $"allocated {large} for 80,000 ten-byte fields");
		Assert.IsLessThan (small * 6, large, $"20,000 fields: {small}; 80,000 fields: {large}");
	}

	internal static byte[] WithTinyChunks (int count)
	{
		var builder = new AviTestBuilder { JunkBeforeMovi = -1 };
		builder.Info.Add (("INAM", "x"));
		var b = builder.Build ();
		var movi = b.AsSpan ().IndexOf ("movi"u8) - 8;
		var filler = new byte[count * 8];
		for (var i = 0; i < count; i++)
			"JUNK"u8.CopyTo (filler.AsSpan (i * 8));
		var file = b.AsSpan (0, movi).ToArray ().Concat (filler).Concat (b.AsSpan (movi).ToArray ()).ToArray ();
		BinaryPrimitives.WriteUInt32LittleEndian (file.AsSpan (4), (uint)(file.Length - 8));
		return file;
	}

	internal static byte[] AppendChunk (byte[] file, string id, byte[] data)
	{
		var full = file.Concat (Chunk (id, data)).ToArray ();
		BinaryPrimitives.WriteUInt32LittleEndian (full.AsSpan (4), (uint)(full.Length - 8));
		return full;
	}

	sealed class WholeFileCountingFileSystem : IFileSystem
	{
		readonly MockFileSystem _inner = new ();

		public int WholeFileReads { get; private set; }

		public long BytesRead { get; private set; }

		public void AddFile (string path, byte[] data) => _inner.AddFile (path, data);

		public bool FileExists (string path) => _inner.FileExists (path);

		public Exception? OpenFailure { get; set; }

		public Stream OpenRead (string path) => OpenFailure is { } failure ? throw failure : new Counting (_inner.OpenRead (path), this);

		public Stream OpenReadWrite (string path) => _inner.OpenReadWrite (path);

		public Stream Create (string path) => _inner.Create (path);

		public byte[] ReadAllBytes (string path)
		{
			WholeFileReads++;
			return _inner.ReadAllBytes (path);
		}

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default)
		{
			WholeFileReads++;
			return _inner.ReadAllBytesAsync (path, cancellationToken);
		}

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => _inner.WriteAllBytes (path, data);

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			_inner.WriteAllBytesAsync (path, data, cancellationToken);

		public void Delete (string path) => _inner.Delete (path);

		public void Move (string sourcePath, string destinationPath) => _inner.Move (sourcePath, destinationPath);

		public string? GetDirectoryName (string path) => _inner.GetDirectoryName (path);

		public string GetFileName (string path) => _inner.GetFileName (path);

		public string CombinePath (string path1, string path2) => _inner.CombinePath (path1, path2);

		sealed class Counting (Stream inner, WholeFileCountingFileSystem owner) : Stream
		{
			public override bool CanRead => true;
			public override bool CanSeek => inner.CanSeek;
			public override bool CanWrite => false;
			public override long Length => inner.Length;
			public override long Position { get => inner.Position; set => inner.Position = value; }
			public override void Flush () { }
			public override int Read (byte[] buffer, int offset, int count)
			{
				var read = inner.Read (buffer, offset, count);
				owner.BytesRead += read;
				return read;
			}
			public override long Seek (long offset, SeekOrigin origin) => inner.Seek (offset, origin);
			public override void SetLength (long value) => throw new NotSupportedException ();
			public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
			protected override void Dispose (bool disposing)
			{
				if (disposing)
					inner.Dispose ();
				base.Dispose (disposing);
			}
		}
	}

	internal static byte[] Id3 (string title, string album)
	{
		var tag = new Id3v2Tag { Title = title, Album = album };
		return tag.Render (0).ToArray ();
	}

	internal static int IndexOf (byte[] data, ReadOnlySpan<byte> needle) => data.AsSpan ().IndexOf (needle);

	internal static int LastIndexOf (byte[] data, ReadOnlySpan<byte> needle) => data.AsSpan ().LastIndexOf (needle);

	internal static byte[] Chunk (string id, byte[] data)
	{
		var chunk = new byte[8 + data.Length + (data.Length & 1)];
		System.Text.Encoding.Latin1.GetBytes (id).CopyTo (chunk, 0);
		BinaryPrimitives.WriteUInt32LittleEndian (chunk.AsSpan (4), (uint)data.Length);
		data.CopyTo (chunk, 8);
		return chunk;
	}

	internal static byte[] Riff (params byte[][] chunks)
	{
		var body = chunks.SelectMany (c => c).ToArray ();
		var file = new byte[12 + body.Length];
		"RIFF"u8.CopyTo (file);
		BinaryPrimitives.WriteUInt32LittleEndian (file.AsSpan (4), (uint)(body.Length + 4));
		"AVI "u8.CopyTo (file.AsSpan (8));
		body.CopyTo (file, 12);
		return file;
	}

	sealed class NonSeekableStream (byte[] data) : MemoryStream (data)
	{
		public override bool CanSeek => false;
	}
}
