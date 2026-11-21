// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// Reading MPEG program streams and MPEG video elementary streams.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsFileTests
{
	const long Wrap = 1L << 33;

	[TestMethod]
	public void Read_Mpeg1ProgramStream_ReadsVideoAudioAndDuration ()
	{
		var data = ProgramStream ();

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual (MpegPsStreamType.ProgramStream, file.StreamType);
		Assert.AreEqual (1, file.SystemsVersion);
		Assert.AreEqual (1, file.VideoVersion);
		Assert.AreEqual (MediaFormat.MpegPs, file.Format);
		Assert.AreEqual (MediaTypes.Audio | MediaTypes.Video, ((IMediaFile)file).MediaTypes);
		var video = file.VideoProperties!.Value;
		Assert.AreEqual (352, video.Width);
		Assert.AreEqual (288, video.Height);
		Assert.AreEqual (25d, video.FrameRate, 1e-9);
		Assert.AreEqual ("MPEG-1 Video", video.Codec);
		Assert.AreEqual (1000, video.Bitrate);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
		Assert.AreEqual (TimeSpan.FromSeconds (2), video.Duration);
		var audio = file.AudioProperties!.Value;
		Assert.AreEqual (48000, audio.SampleRate);
		Assert.AreEqual (2, audio.Channels);
		Assert.AreEqual (192, audio.Bitrate);
		Assert.AreEqual ("MPEG-1 Layer II", audio.Codec);
		Assert.AreEqual (TimeSpan.FromSeconds (2), audio.Duration);
		Assert.IsNull (file.Tag);
	}

	[TestMethod]
	public void Read_Mpeg2ProgramStream_UsesTheSequenceExtension ()
	{
		var data = ProgramStream (new Options {
			Mpeg2 = true,
			Width = 1920,
			Height = 1080,
			FrameRateCode = 4,
			PackStuffing = 3,
			VideoFrames = 60
		});

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual (2, file.SystemsVersion);
		Assert.AreEqual (2, file.VideoVersion);
		var video = file.VideoProperties!.Value;
		Assert.AreEqual (1920, video.Width);
		Assert.AreEqual (1080, video.Height);
		Assert.AreEqual (30000d / 1001, video.FrameRate, 1e-9);
		Assert.AreEqual ("MPEG-2 Video", video.Codec);
		// 60 frames at 29.97 fps: the last frame ends 60 * 3003 ticks after the first.
		Assert.AreEqual (60 * 3003d / TicksPerSecond, file.Duration!.Value.TotalSeconds, 0.0001);
	}

	[TestMethod]
	public void Read_StreamCutMidGop_FindsTheSequenceHeaderSplitAcrossPackets ()
	{
		var o = new Options { Mpeg2 = true, Width = 720, Height = 576 };
		var header = Concat (SequenceHeader (o), GopHeader (0, 0, 1, 0, false), Picture (32));
		using var output = new MemoryStream ();
		for (var i = 0; i < 40; i++) {
			var payload = i switch {
				20 => header.AsSpan (0, 7).ToArray (),
				21 => header.AsSpan (7).ToArray (),
				_ => Picture (1000)
			};
			var bytes = Concat (PackHeader2 (i * 3600L), Pes (0xE0, i == 20 ? null : 45_000 + (i * 3600L), payload, mpeg2: true));
			output.Write (bytes, 0, bytes.Length);
		}

		var file = MpegPsFile.Read (output.ToArray ()).File!;

		Assert.AreEqual (2, file.VideoVersion);
		Assert.AreEqual (720, file.VideoProperties!.Value.Width);
		Assert.AreEqual (576, file.VideoProperties!.Value.Height);
		Assert.AreEqual (TimeSpan.FromSeconds (40 * 3600 / (double)TicksPerSecond), file.Duration);
	}

	[TestMethod]
	public void Read_SizeExtension_AddsTheHighBits ()
	{
		var data = ProgramStream (new Options { Mpeg2 = true, Width = 4096 + 64, Height = 4096 + 16 });

		var video = MpegPsFile.Read (data).File!.VideoProperties!.Value;

		Assert.AreEqual (4096 + 64, video.Width);
		Assert.AreEqual (4096 + 16, video.Height);
	}

	[TestMethod]
	public void Read_FrameRateExtension_ScalesTheFrameRate ()
	{
		var data = ProgramStream (new Options { Mpeg2 = true, FrameRateCode = 3, FrameRateExtensionN = 1, FrameRateExtensionD = 0 });

		var video = MpegPsFile.Read (data).File!.VideoProperties!.Value;

		Assert.AreEqual (50d, video.FrameRate, 1e-9);
	}

	[TestMethod]
	public void Read_Mpeg2AllOnesBitRate_IsNotARate ()
	{
		// Encoders with no peak rate write the MPEG-1 variable-rate code and no extension bits.
		var data = ProgramStream (new Options { Mpeg2 = true, BitRateValue = 0x3FFFF });

		Assert.AreEqual (0, MpegPsFile.Read (data).File!.VideoProperties!.Value.Bitrate);
	}

	[TestMethod]
	public void Read_AudioStartsBeforeTheFirstStamp_MeasuresFromTheEarliest ()
	{
		// The first packet is video at 0.5 s; the audio after it is stamped 1000 ticks earlier.
		var data = ProgramStream (new Options { AudioLeadTicks = 1000 });

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromTicks ((long)Math.Round ((180_000 + 1000) / (double)TicksPerSecond * TimeSpan.TicksPerSecond)), file.Duration);
	}

	[TestMethod]
	public void Read_PtsWrapsBetweenHeadAndTail_StillMeasuresTheSpan ()
	{
		var data = ProgramStream (new Options { StartPts = Wrap - 45_000, VideoFrames = 50 });

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
	}

	[TestMethod]
	public void Read_LongStream_MeasuresFromTheTailWithoutTheMiddle ()
	{
		// 1500 frames: the head window holds only the first ones, the tail only the last.
		var data = ProgramStream (new Options { VideoFrames = 1500, VideoFrameBytes = 2000 });
		Assert.IsTrue (data.Length > 2 * 1024 * 1024);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (60), file.Duration);
	}

	[TestMethod]
	public void Read_PaddingAtTheEnd_GrowsTheTailWindow ()
	{
		// The last 300 KB hold no time stamp, so the first tail window has none.
		var data = ProgramStream (new Options { PaddingBytes = 300 * 1024, PaddingPack = -1 });

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
	}

	[TestMethod]
	public void Read_PaddingAtTheStart_GrowsTheHeadWindow ()
	{
		var data = ProgramStream (new Options { LeadingPaddingBytes = 300 * 1024 });

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (352, file.VideoProperties!.Value.Width);
		Assert.AreEqual (48000, file.AudioProperties!.Value.SampleRate);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
	}

	[TestMethod]
	public async Task ReadAsync_PaddingAtBothEnds_GrowsBothWindows ()
	{
		var data = ProgramStream (new Options { LeadingPaddingBytes = 300 * 1024, PaddingBytes = 300 * 1024, PaddingPack = -1 });

		var opened = await MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg");
		var streamed = MediaFile.Read (new MemoryStream (data), "clip.mpg");

		Assert.AreEqual (TimeSpan.FromSeconds (2), ((MpegPsFile)opened.File!).Duration);
		Assert.AreEqual (TimeSpan.FromSeconds (2), ((MpegPsFile)streamed.File!).Duration);
		Assert.AreEqual (352, ((MpegPsFile)opened.File!).VideoProperties!.Value.Width);
	}

	[TestMethod]
	public void Read_NoTimeStamps_FallsBackToTheScrSpan ()
	{
		var data = ProgramStream (new Options { WritePts = false, AudioKind = Audio.None, VideoFrames = 26 });

		var file = MpegPsFile.Read (data).File!;

		// The first pack is at SCR 0 and the last at 25 frames.
		Assert.AreEqual (TimeSpan.FromSeconds (1), file.Duration);
	}

	[TestMethod]
	public void Read_Mpeg2ScrWraps_StillMeasuresTheSpan ()
	{
		var data = ProgramStream (new Options {
			Mpeg2 = true,
			WritePts = false,
			AudioKind = Audio.None,
			VideoFrames = 26,
			StartScr = Wrap - 3600
		});

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (1), file.Duration);
	}

	[TestMethod]
	public void Read_Ac3InPrivateStream1_ReadsSampleRateAndChannels ()
	{
		var data = ProgramStream (new Options { Mpeg2 = true, AudioKind = Audio.Ac3, Ac3Acmod = 7, Ac3Lfe = true });

		var file = MpegPsFile.Read (data).File!;

		var audio = file.AudioProperties!.Value;
		Assert.AreEqual ("AC-3", audio.Codec);
		Assert.AreEqual (48000, audio.SampleRate);
		Assert.AreEqual (6, audio.Channels);
		Assert.AreEqual (192, audio.Bitrate);
	}

	[TestMethod]
	[DataRow (1, 1)]
	[DataRow (0, 2)]
	[DataRow (2, 2)]
	[DataRow (3, 3)]
	[DataRow (6, 4)]
	public void Read_Ac3Acmod_MapsTheChannelCount (int acmod, int channels)
	{
		var data = ProgramStream (new Options { Mpeg2 = true, AudioKind = Audio.Ac3, Ac3Acmod = acmod });

		var audio = MpegPsFile.Read (data).File!.AudioProperties!.Value;

		Assert.AreEqual (channels, audio.Channels);
	}

	[TestMethod]
	public void Read_LpcmInPrivateStream1_IsIdentified ()
	{
		var data = ProgramStream (new Options { Mpeg2 = true, AudioKind = Audio.Lpcm });

		var file = MpegPsFile.Read (data).File!;

		var audio = file.AudioProperties!.Value;
		Assert.AreEqual ("LPCM", audio.Codec);
		Assert.AreEqual (48000, audio.SampleRate);
		Assert.AreEqual (2, audio.Channels);
		Assert.AreEqual (16, audio.BitsPerSample);
		Assert.AreEqual (1536, audio.Bitrate);
		Assert.IsTrue ((((IMediaFile)file).MediaTypes & MediaTypes.Audio) != 0);
	}

	[TestMethod]
	public void Read_AudioOnlyProgramStream_HasNoVideo ()
	{
		var data = ProgramStream (new Options { Video = false, AudioFrames = 100 });

		var file = MpegPsFile.Read (data).File!;

		Assert.IsNull (file.VideoProperties);
		Assert.AreEqual (0, file.VideoVersion);
		Assert.AreEqual (MediaTypes.Audio, ((IMediaFile)file).MediaTypes);
		Assert.AreEqual (100 * Mp2FrameTicks / (double)TicksPerSecond, file.Duration!.Value.TotalSeconds, 0.0001);
	}

	[TestMethod]
	public void Read_ElementaryStream_CountsFramesFromTheTimeCodes ()
	{
		var data = ElementaryStream (new Options { Mpeg2 = true, Width = 720, Height = 576, VideoFrames = 90 });

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual (MpegPsStreamType.VideoElementaryStream, file.StreamType);
		Assert.AreEqual (0, file.SystemsVersion);
		Assert.AreEqual (2, file.VideoVersion);
		Assert.AreEqual (MediaTypes.Video, ((IMediaFile)file).MediaTypes);
		Assert.IsNull (file.AudioProperties);
		Assert.AreEqual (720, file.VideoProperties!.Value.Width);
		Assert.AreEqual (TimeSpan.FromSeconds (90 / 25d), file.Duration);
	}

	[TestMethod]
	public void Read_LongElementaryStream_ReadsTheLastGopFromTheTail ()
	{
		var data = ElementaryStream (new Options { VideoFrames = 3000, VideoFrameBytes = 1500 });
		Assert.IsTrue (data.Length > 4 * 1024 * 1024);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (120), file.Duration);
	}

	[TestMethod]
	public void Read_DropFrameTimeCodes_SkipTheDroppedNumbers ()
	{
		// 2100 frames at 29.97 fps, starting at 01:00:00;00, crosses two minute marks.
		var data = ElementaryStream (
			new Options { Mpeg2 = true, FrameRateCode = 4, VideoFrames = 2100, VideoFrameBytes = 16 },
			gopLength: 15,
			dropFrame: true,
			startHours: 1);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (2100 * 1001d / 30000, file.Duration!.Value.TotalSeconds, 0.0001);
	}

	[TestMethod]
	public void Read_ElementaryStreamWithZeroTimeCodes_EstimatesFromTheBitRate ()
	{
		// bit_rate_value is in units of 400 bit/s.
		var data = ElementaryStream (new Options { VideoFrames = 300, VideoFrameBytes = 200 }, zeroTimeCodes: true, bitRateValue: 100);

		var file = MpegPsFile.Read (data).File!;

		var expected = data.Length * 8d / (100 * 400);
		Assert.AreEqual (expected, file.Duration!.Value.TotalSeconds, 0.001);
	}

	[TestMethod]
	public void Read_Mpeg1VariableBitRate_HasNoBitrateEstimate ()
	{
		var data = ElementaryStream (new Options { VideoFrames = 300, VideoFrameBytes = 200 }, zeroTimeCodes: true, bitRateValue: 0x3FFFF);

		var file = MpegPsFile.Read (data).File!;

		Assert.IsNull (file.Duration);
		Assert.AreEqual (0, file.VideoProperties!.Value.Bitrate);
	}

	[TestMethod]
	public void Read_Id3v2PrefixedProgramStream_ReadsTheTag ()
	{
		var stream = ProgramStream ();
		var data = Concat (Id3v2 ("Prefixed"), stream);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Prefixed", file.Tag!.Title);
		Assert.AreEqual ("Prefixed", file.Id3v2Tag!.Title);
		Assert.AreEqual (data.Length - stream.Length, file.Id3v2Size);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
	}

	[TestMethod]
	public void Read_TrailingId3v1_ReadsTheTag ()
	{
		var data = Concat (ProgramStream (), Id3v1 ("Footer"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Footer", file.Tag!.Title);
		Assert.AreEqual ("Footer", file.Id3v1Tag!.Title);
		Assert.IsNull (file.Id3v2Tag);
	}

	[TestMethod]
	public void Read_BothTags_PrefersId3v2 ()
	{
		var data = Concat (Id3v2 ("Head"), ProgramStream (), Id3v1 ("Foot"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Head", file.Tag!.Title);
	}

	[TestMethod]
	public void Read_NotAStream_Fails ()
	{
		Assert.IsFalse (MpegPsFile.Read ([]).IsSuccess);
		Assert.IsFalse (MpegPsFile.Read ([0x00, 0x00, 0x01]).IsSuccess);
		Assert.IsFalse (MpegPsFile.Read ([0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00]).IsSuccess);
		Assert.IsFalse (MpegPsFile.Read (Concat (Id3v2 ("x"), [0xFF, 0xFB, 0x90, 0x00])).IsSuccess);
		Assert.IsFalse (MpegPsFile.Read ([0x00, 0x00, 0x01, 0xBA, 0x00, 0x00]).IsSuccess);
	}

	[TestMethod]
	public void IsValidFormat_StartCodes ()
	{
		Assert.IsTrue (MpegPsFile.IsValidFormat (ProgramStream ()));
		Assert.IsTrue (MpegPsFile.IsValidFormat (ProgramStream (new Options { Mpeg2 = true })));
		Assert.IsTrue (MpegPsFile.IsValidFormat (ElementaryStream ()));
		Assert.IsTrue (MpegPsFile.IsValidFormat (Concat (Id3v2 ("t"), ProgramStream ())));
		Assert.IsFalse (MpegPsFile.IsValidFormat ([0x00, 0x00, 0x01, 0xBA, 0x00, 0x00, 0x00, 0x00]));
		Assert.IsFalse (MpegPsFile.IsValidFormat ([0x00, 0x00, 0x01, 0xE0]));
		Assert.IsFalse (MpegPsFile.IsValidFormat ([0xFF, 0xFB, 0x90, 0x00]));
		Assert.IsFalse (MpegPsFile.IsValidFormat ([]));
	}

	[TestMethod]
	public void DetectFormat_ProgramAndElementaryStreams_AreMpegPs ()
	{
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (ProgramStream ()));
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (ProgramStream (new Options { Mpeg2 = true })));
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (ElementaryStream ()));
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (ProgramStream (), "clip.mp3"));
	}

	[TestMethod]
	public void DetectFormat_Id3v2ThenProgramStream_IsMpegPsNotMp3 ()
	{
		var data = Concat (Id3v2 ("Prefixed"), ProgramStream ());

		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (data));
		var opened = MediaFile.ReadFromData (data);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		Assert.IsInstanceOfType<MpegPsFile> (opened.File);
		Assert.AreEqual ("Prefixed", opened.Tag!.Title);
	}

	[TestMethod]
	public void Read_StreamWithId3v2ThenProgramStream_IsMpegPs ()
	{
		var data = Concat (Id3v2 ("Prefixed"), ProgramStream ());

		var opened = MediaFile.Read (new MemoryStream (data), "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		Assert.AreEqual ("Prefixed", opened.Tag!.Title);
	}

	[TestMethod]
	public async Task ReadAsync_StreamWithId3v2ThenProgramStream_IsMpegPs ()
	{
		var data = Concat (Id3v2 ("Prefixed"), ProgramStream ());

		var opened = await MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		Assert.AreEqual ("Prefixed", opened.Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), ((MpegPsFile)opened.File!).Duration);
	}

	[TestMethod]
	public void DetectFormat_Id3v2ThenMpegAudio_StaysMp3 ()
	{
		var mp3 = Concat (Id3v2 ("Song"), Mp3Frame (), Mp3Frame ());

		Assert.AreEqual (MediaFormat.Mp3, MediaFile.DetectFormat (mp3));
		Assert.AreEqual (MediaFormat.Mp3, MediaFile.Read (new MemoryStream (mp3), "song.mp3").Format);
		Assert.AreEqual (MediaFormat.Mp3, MediaFile.DetectFormat (Concat (Id3v2 ("x"), [0xFF, 0xFB, 0x90, 0x00])));
	}

	[TestMethod]
	public void DetectFormat_Mp4WhoseFirstBoxSizeLooksLikeAPackCode_StaysMp4 ()
	{
		// A 0x1BA-byte ftyp box starts 00 00 01 BA 'f' 't' 'y' 'p'. 'f' passes the MPEG-2 marker check.
		var mp4 = new byte[0x1BA + 16];
		mp4[2] = 0x01;
		mp4[3] = 0xBA;
		"ftypisom"u8.CopyTo (mp4.AsSpan (4));

		Assert.AreEqual (MediaFormat.Mp4, MediaFile.DetectFormat (mp4));
	}

	[TestMethod]
	public void DetectFormat_FreeBoxWhoseSizeLooksLikeAPackCode_IsNotMpegPs ()
	{
		// 00 00 01 BA "free" and 0xFF bytes pass every pack header marker bit,
		// but no start code follows the header.
		var data = new byte[64];
		data[2] = 0x01;
		data[3] = 0xBA;
		"free"u8.CopyTo (data.AsSpan (4));
		data.AsSpan (8).Fill (0xFF);

		Assert.IsFalse (MpegPsFile.IsValidFormat (data));
		Assert.AreNotEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (data));
	}

	[TestMethod]
	[DataRow ("clip.mpg")]
	[DataRow ("clip.MPEG")]
	[DataRow ("clip.mpe")]
	[DataRow ("clip.mpv")]
	[DataRow ("clip.m2v")]
	[DataRow ("clip.vob")]
	[DataRow ("clip.m1v")]
	public void DetectFormat_Extension_FallsBackToMpegPs (string name)
	{
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (new byte[] { 1, 2, 3, 4 }, name));
	}

	[TestMethod]
	public void MediaFileRead_ProgramStream_ReturnsTheFile ()
	{
		var opened = MediaFile.ReadFromData (Concat (ProgramStream (), Id3v1 ("Footer")), "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		Assert.AreEqual ("Footer", opened.Tag!.Title);
		var media = (IMediaFile)opened.File!;
		Assert.AreEqual (MediaFormat.MpegPs, media.Format);
		Assert.AreEqual (48000, media.AudioProperties!.SampleRate);
		Assert.AreEqual (352, media.VideoProperties!.Value.Width);
		Assert.IsNull (media.ImageProperties);
	}

	[TestMethod]
	public void ReadFromFile_ReadsAndRemembersTheSource ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.mpg", Concat (Id3v2 ("Disk"), ProgramStream ()));

		var result = MpegPsFile.ReadFromFile ("/clip.mpg", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("/clip.mpg", result.File!.SourcePath);
		Assert.AreEqual ("Disk", result.File.Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File.Duration);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ReadsAndRemembersTheSource ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.mpg", Concat (Id3v2 ("Disk"), ProgramStream ()));

		var result = await MpegPsFile.ReadFromFileAsync ("/clip.mpg", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("/clip.mpg", result.File!.SourcePath);
		Assert.AreEqual ("Disk", result.File.Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File.Duration);
	}

	[TestMethod]
	public void ReadFromFile_Missing_Fails ()
	{
		var result = MpegPsFile.ReadFromFile ("/missing.mpg", new MockFileSystem ());

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Cancelled_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.mpg", ProgramStream ());
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await MpegPsFile.ReadFromFileAsync ("/clip.mpg", fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public void Read_SpanAndStream_Agree ()
	{
		var data = Concat (Id3v2 ("Same"), ProgramStream (new Options { Mpeg2 = true, AudioKind = Audio.Ac3, VideoFrames = 400 }), Id3v1 ("Same"));

		var span = MpegPsFile.Read (data).File!;
		var opened = MediaFile.Read (new MemoryStream (data), "clip.vob");
		var streamed = (MpegPsFile)opened.File!;

		Assert.AreEqual (span.Duration, streamed.Duration);
		Assert.AreEqual (span.VideoProperties, streamed.VideoProperties);
		Assert.AreEqual (span.AudioProperties, streamed.AudioProperties);
		Assert.AreEqual (span.Id3v2Size, streamed.Id3v2Size);
	}

	[TestMethod]
	public void Read_NonSeekableStream_ReadsTheBytes ()
	{
		var data = Concat (Id3v2 ("Pipe"), ProgramStream ());

		var opened = MediaFile.Read (new NonSeekable (data), "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		Assert.AreEqual ("Pipe", opened.Tag!.Title);
	}

	[TestMethod]
	public void Read_SeekableStreamLongerThanAnArray_ReadsOnlyTheEnds ()
	{
		var (stream, expected) = HugeProgramStream ();

		var opened = MediaFile.Read (stream, "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (expected, ((MpegPsFile)opened.File!).Duration);
		Assert.IsTrue (stream.BytesRead < 2 * 1024 * 1024, stream.BytesRead.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	[TestMethod]
	public async Task ReadAsync_SeekableStreamLongerThanAnArray_ReadsOnlyTheEnds ()
	{
		var (stream, expected) = HugeProgramStream ();

		var opened = await MediaFile.ReadAsync (stream, "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (expected, ((MpegPsFile)opened.File!).Duration);
		Assert.IsTrue (stream.BytesRead < 2 * 1024 * 1024, stream.BytesRead.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	// Three hours apart: three frames at the start of a 5 GB file and three at its end.
	static (Sparse Stream, TimeSpan Duration) HugeProgramStream ()
	{
		const long Start = 45_000;
		const long Later = Start + (3L * 3600 * TicksPerSecond);
		var head = ProgramStream (new Options { StartPts = Start, VideoFrames = 3, EndCode = false });
		var tail = ProgramStream (new Options { StartPts = Later, StartScr = Later - Start, VideoFrames = 3 });
		var length = 5L * 1024 * 1024 * 1024;
		var stream = new Sparse (length, head, length - tail.Length, tail);
		return (stream, TimeSpan.FromSeconds ((Later + (3 * 3600) - Start) / (double)TicksPerSecond));
	}

	[TestMethod]
	public async Task Read_StreamEndingInsideTheTag_Fails ()
	{
		// The ID3v2 header claims 500 bytes; the stream reports 1000 but holds 100.
		var data = new byte[100];
		"ID3"u8.CopyTo (data);
		data[3] = 4;
		data[8] = 3;
		data[9] = 0x74;

		var result = MpegPsFile.Read (new LongerThanItsData (data, 1000), null, null);
		var asyncResult = await MpegPsFile.ReadAsync (new LongerThanItsData (data, 1000), null, null, CancellationToken.None);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsFalse (asyncResult.IsSuccess);
		StringAssert.Contains (result.Error, "ID3v2");
		StringAssert.Contains (asyncResult.Error, "ID3v2");
	}

	sealed class LongerThanItsData (byte[] data, long length) : MemoryStream (data)
	{
		public override long Length => length;
	}

	[TestMethod]
	public void Tag_IsACombinedViewThatWritesBothTags ()
	{
		var data = Concat (Id3v2 ("Head"), ProgramStream (), Id3v1 ("Foot"));
		var file = MpegPsFile.Read (data).File!;

		file.Tag!.Artist = "Both";

		Assert.AreEqual ("Both", file.Id3v2Tag!.Artist);
		Assert.AreEqual ("Both", file.Id3v1Tag!.Artist);
	}

	[TestMethod]
	public void EnsureId3v2Tag_CreatesOnce ()
	{
		var file = MpegPsFile.Read (ProgramStream ()).File!;
		Assert.IsNull (file.Tag);

		var tag = file.EnsureId3v2Tag ();

		Assert.AreSame (tag, file.EnsureId3v2Tag ());
		Assert.IsNotNull (file.Tag);
	}

	[TestMethod]
	public void TryRead_ReportsSuccess ()
	{
		Assert.IsTrue (MpegPsFile.TryRead (ProgramStream (), out var file));
		Assert.IsNotNull (file);
		Assert.IsFalse (MpegPsFile.TryRead ([1, 2, 3], out var none));
		Assert.IsNull (none);
	}

	[TestMethod]
	public void ReadResult_Equality ()
	{
		var file = MpegPsFile.Read (ProgramStream ()).File!;
		var a = MpegPsFileReadResult.Success (file);
		var b = MpegPsFileReadResult.Success (file);
		var c = MpegPsFileReadResult.Failure ("x");

		Assert.IsTrue (a == b);
		Assert.IsTrue (a != c);
		Assert.IsTrue (a.Equals ((object)b));
		Assert.AreEqual (a.GetHashCode (), b.GetHashCode ());
		Assert.IsFalse (c.IsSuccess);
		Assert.AreEqual ("x", c.Error);
	}

	[TestMethod]
	public void Dispose_ClearsTheTags ()
	{
		var file = MpegPsFile.Read (Concat (Id3v2 ("x"), ProgramStream ())).File!;

		file.Dispose ();
		file.Dispose ();

		Assert.IsNull (file.Id3v2Tag);
		Assert.IsNull (file.Tag);
	}

	internal static byte[] Id3v2 (string title)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = title };
		return tag.Render ().ToArray ();
	}

	// MPEG-1 Layer III, 128 kbps, 44.1 kHz: 417 bytes.
	static byte[] Mp3Frame ()
	{
		var frame = new byte[417];
		frame[0] = 0xFF;
		frame[1] = 0xFB;
		frame[2] = 0x90;
		return frame;
	}

	internal static byte[] Id3v1 (string title) => new Id3v1Tag { Title = title }.Render ().ToArray ();

	/// <summary>A seekable stream of zeros with bytes at its start and its end.</summary>
	sealed class Sparse (long length, byte[] head, long tailAt, byte[] tail) : Stream
	{
		public long BytesRead { get; private set; }

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => length;
		public override long Position { get; set; }

		public override void Flush () { }

		public override int Read (byte[] buffer, int offset, int count)
		{
			var n = (int)Math.Max (0, Math.Min (count, length - Position));
			Array.Clear (buffer, offset, n);
			for (var i = 0; i < n; i++) {
				var at = Position + i;
				if (at < head.Length)
					buffer[offset + i] = head[at];
				else if (at >= tailAt && at - tailAt < tail.Length)
					buffer[offset + i] = tail[at - tailAt];
			}

			Position += n;
			BytesRead += n;
			return n;
		}

		public override long Seek (long offset, SeekOrigin origin) => Position = origin switch {
			SeekOrigin.Current => Position + offset,
			SeekOrigin.End => length + offset,
			_ => offset
		};

		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}

	sealed class NonSeekable (byte[] data) : Stream
	{
		readonly MemoryStream _inner = new (data);

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException ();
		public override long Position {
			get => throw new NotSupportedException ();
			set => throw new NotSupportedException ();
		}

		public override void Flush () { }
		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);
		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_inner.Dispose ();
			base.Dispose (disposing);
		}
	}
}
