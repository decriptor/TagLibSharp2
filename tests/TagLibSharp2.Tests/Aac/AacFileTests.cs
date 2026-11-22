// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aac;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

using static TagLibSharp2.Tests.Aac.AacTestBuilder;

namespace TagLibSharp2.Tests.Aac;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aac")]
public class AacFileTests
{
	static AacAudioProperties Properties (byte[] data)
	{
		var result = AacFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Properties);
		return result.File.Properties!;
	}

	static TimeSpan Samples (long samples, int rate) =>
		TimeSpan.FromTicks ((long)Math.Round ((double)samples * TimeSpan.TicksPerSecond / rate));

	[TestMethod]
	public void Read_Adts_ReadsTheFirstHeader ()
	{
		var props = Properties (Adts (10));

		Assert.AreEqual (44100, props.SampleRate);
		Assert.AreEqual (2, props.Channels);
		Assert.AreEqual (AacProfile.LowComplexity, props.Profile);
		Assert.AreEqual ("AAC-LC", props.Codec);
		Assert.AreEqual (AacStreamFormat.Adts, props.StreamFormat);
		Assert.AreEqual (4, props.MpegVersion);
		Assert.AreEqual (0, props.BitsPerSample);
	}

	[TestMethod]
	public void Read_Adts_DurationCounts1024SamplesPerFrame ()
	{
		var props = Properties (Adts (431));

		Assert.AreEqual (431, props.FrameCount);
		Assert.AreEqual (Samples (431 * 1024, 44100), props.Duration);
	}

	[TestMethod]
	public void Read_Adts_CountsEveryRawDataBlock ()
	{
		var props = Properties (Adts (100, rawBlocks: 3));

		Assert.AreEqual (100, props.FrameCount);
		Assert.AreEqual (Samples (300 * 1024, 44100), props.Duration);
	}

	[TestMethod]
	public void Read_Adts_BitrateIsFrameBytesOverDuration ()
	{
		var props = Properties (Adts (431, length: 400));

		var expected = (int)(431 * 400 * 8 / props.Duration.TotalSeconds / 1000);
		Assert.AreEqual (expected, props.Bitrate);
	}

	[TestMethod]
	public void Read_Mpeg2Adts_ReportsVersion2 ()
	{
		Assert.AreEqual (2, Properties (Adts (4, mpeg2: true)).MpegVersion);
	}

	[TestMethod]
	[DataRow (0, false, AacProfile.Main, "AAC Main")]
	[DataRow (1, false, AacProfile.LowComplexity, "AAC-LC")]
	[DataRow (2, false, AacProfile.ScalableSampleRate, "AAC-SSR")]
	[DataRow (3, false, AacProfile.LongTermPrediction, "AAC-LTP")]
	[DataRow (0, true, AacProfile.Main, "AAC Main")]
	[DataRow (1, true, AacProfile.LowComplexity, "AAC-LC")]
	[DataRow (2, true, AacProfile.ScalableSampleRate, "AAC-SSR")]
	[DataRow (3, true, AacProfile.Unknown, "AAC")]
	public void Read_Adts_MapsTheProfileField (int bits, bool mpeg2, AacProfile profile, string codec)
	{
		var props = Properties (Adts (4, profile: bits, mpeg2: mpeg2));

		Assert.AreEqual (profile, props.Profile);
		Assert.AreEqual (codec, props.Codec);
	}

	[TestMethod]
	[DataRow (0, 96000)]
	[DataRow (1, 88200)]
	[DataRow (2, 64000)]
	[DataRow (3, 48000)]
	[DataRow (4, 44100)]
	[DataRow (5, 32000)]
	[DataRow (6, 24000)]
	[DataRow (7, 22050)]
	[DataRow (8, 16000)]
	[DataRow (9, 12000)]
	[DataRow (10, 11025)]
	[DataRow (11, 8000)]
	[DataRow (12, 7350)]
	public void Read_Adts_MapsTheSampleRateIndex (int index, int rate)
	{
		var props = Properties (Adts (8, sampleRateIndex: index));

		Assert.AreEqual (rate, props.SampleRate);
		Assert.AreEqual (Samples (8 * 1024, rate), props.Duration);
	}

	[TestMethod]
	[DataRow (12, true)]
	[DataRow (13, false)]
	[DataRow (14, false)]
	[DataRow (15, false)]
	[DataRow (13, true)]
	public void Read_ReservedSampleRateIndex_IsNotAdts (int index, bool mpeg2)
	{
		var result = AacFile.Read (Adts (8, sampleRateIndex: index, mpeg2: mpeg2));

		Assert.IsFalse (result.IsSuccess);
		Assert.IsFalse (AacFile.IsValidFormat (Adts (8, sampleRateIndex: index, mpeg2: mpeg2)));
	}

	[TestMethod]
	[DataRow (1, 1)]
	[DataRow (2, 2)]
	[DataRow (3, 3)]
	[DataRow (4, 4)]
	[DataRow (5, 5)]
	[DataRow (6, 6)]
	[DataRow (7, 8)]
	public void Read_Adts_MapsTheChannelConfiguration (int configuration, int channels)
	{
		Assert.AreEqual (channels, Properties (Adts (4, channelConfiguration: configuration)).Channels);
	}

	[TestMethod]
	public void Read_ChannelConfigurationZero_CountsTheProgramConfigElement ()
	{
		int[] front = [1, 2, 2];
		var props = Properties (Adts (4, length: 120, channelConfiguration: 0, payload: PceBlock (front, lfe: 1, comment: "pce")));

		Assert.AreEqual (PceChannels (front, lfe: 1), props.Channels);
	}

	[TestMethod]
	public void Read_ChannelConfigurationZero_WithCrc_ReadsThePceAfterTheCrc ()
	{
		int[] front = [2, 2];
		var props = Properties (Adts (4, length: 120, channelConfiguration: 0, crc: true, payload: PceBlock (front)));

		Assert.AreEqual (PceChannels (front), props.Channels);
	}

	[TestMethod]
	public void Read_ChannelConfigurationZero_WithMultipleBlocksAndCrc_SkipsTheBlockPositions ()
	{
		int[] front = [2];
		var props = Properties (Adts (4, length: 120, channelConfiguration: 0, crc: true, rawBlocks: 3, payload: PceBlock (front)));

		Assert.AreEqual (PceChannels (front), props.Channels);
		Assert.AreEqual (Samples (12 * 1024, 44100), props.Duration);
	}

	[TestMethod]
	public void Read_ChannelConfigurationZero_WithoutAPce_ReportsZeroChannels ()
	{
		Assert.AreEqual (0, Properties (Adts (4, channelConfiguration: 0)).Channels);
	}

	[TestMethod]
	public void Read_ChannelConfigurationZero_TruncatedPce_ReportsZeroChannels ()
	{
		var pce = PceBlock ([2, 2, 2], comment: new string ('c', 200));
		Assert.AreEqual (0, Properties (Adts (4, length: 40, channelConfiguration: 0, payload: pce[..33])).Channels);
	}

	[TestMethod]
	public void Read_CrcProtectedFrames_AreWalked ()
	{
		var props = Properties (Adts (50, crc: true));

		Assert.AreEqual (50, props.FrameCount);
	}

	[TestMethod]
	public void Read_TruncatedLastFrame_IsNotCounted ()
	{
		var data = Concat (Adts (20, length: 100), AdtsFrame (100)[..60]);

		Assert.AreEqual (20, Properties (data).FrameCount);
	}

	[TestMethod]
	public void Read_FrameCutShortByTheId3v1Tag_IsNotCounted ()
	{
		// The last frame claims 200 bytes and only 100 precede ID3v1. The tag bytes are not audio.
		var data = Concat (Adts (20, length: 100), AdtsFrame (200)[..100], Id3v1Bytes ("Back"));

		Assert.AreEqual (20, Properties (data).FrameCount);
	}

	[TestMethod]
	public void Read_FrameCutShortByTheApeTag_IsNotCounted ()
	{
		var ape = ApeBytes ("Ape");
		var data = Concat (Adts (20, length: 100), AdtsFrame (100 + ape.Length)[..100], ape);

		Assert.AreEqual (20, Properties (data).FrameCount);
	}

	[TestMethod]
	public void Read_GarbageBetweenFrames_ResynchronizesOnTheNextConfirmedFrame ()
	{
		var junk = new byte[333];
		for (var i = 0; i < junk.Length; i++)
			junk[i] = (byte)(i % 2 == 0 ? 0xFF : 0xF1); // false syncs that do not parse or confirm
		var data = Concat (Adts (10), junk, Adts (15));

		Assert.AreEqual (25, Properties (data).FrameCount);
	}

	[TestMethod]
	public void Read_GarbageLongerThanASearchChunk_StillResynchronizes ()
	{
		var data = Concat (Adts (10), new byte[20_000], Adts (15));

		Assert.AreEqual (25, Properties (data).FrameCount);
	}

	[TestMethod]
	public void Read_FalseSyncRegion_ReadsTheStreamInChunks ()
	{
		// Same-stream headers every 10 bytes whose 9-byte frame is followed by zeros: every byte
		// is a resync candidate that its successor rejects. The search must read the region in
		// chunks, not seek once per candidate.
		var fake = AdtsFrame (9);
		var region = new byte[1024 * 1024];
		for (var at = 0; at + 10 <= region.Length; at += 10)
			fake.CopyTo (region, at);
		var data = Concat (Adts (5), new byte[3], region, Adts (5));
		using var stream = new CountingStream (data);

		var result = AacFile.Read (stream, null, null);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (stream.Reads < 1000, $"{stream.Reads} reads for {data.Length} bytes");
		Assert.IsTrue (stream.BytesRead < 4L * data.Length, $"{stream.BytesRead} bytes read for {data.Length}");
	}

	[TestMethod]
	public void Read_CleanStream_ReadsHeadersAndAllocatesLittle ()
	{
		// A clean file decides its start at the first byte and walks 7 bytes a frame. The resync buffer
		// is never needed, so it is never allocated.
		var data = Concat (Id3v2Bytes ("Front"), Adts (200, length: 400), Id3v1Bytes ("Back"));
		using var stream = new CountingStream (data);
		_ = AacFile.Read (stream, null, null); // JIT warm-up
		stream.Reset ();

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = AacFile.Read (stream, null, null);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (200, result.File!.Properties!.FrameCount);
		var budget = Id3v2Bytes ("Front").Length + 200 * 7 + 128 + 512;
		Assert.IsTrue (stream.BytesRead < budget, $"read {stream.BytesRead} bytes, budget {budget}");
		Assert.IsTrue (allocated < 12 * 1024, $"allocated {allocated} bytes");
	}

	[TestMethod]
	public void Read_ApeTagReachingIntoTheId3v2Tag_IsNotATrailingTag ()
	{
		// An APEv2 footer at the end whose size reaches back into the ID3v2 tag, where valid items sit in the
		// padding. A trailing tag starts after the leading one; this one is not a tag, and the frames all count.
		var item = new TagLibSharp2.Ape.ApeTag { Title = "Hidden" }.Render ().ToArray ()[..^32]; // the items alone
		var frames = Adts (3, length: 300);
		var padding = new byte[200];
		item.CopyTo (padding, padding.Length - item.Length);
		var id3 = Concat ("ID3"u8.ToArray (), [4, 0, 0, 0, 0, 1, 0x48], padding); // syncsafe 200
		var footer = new TagLibSharp2.Ape.ApeTag { Title = "Hidden" }.Render ().ToArray ()[^32..];
		var tagSize = (uint)(item.Length + frames.Length + 32);
		BitConverter.GetBytes (tagSize).CopyTo (footer, 12);
		var data = Concat (id3, frames, footer);

		var file = AacFile.Read (data).File!;
		Assert.IsNull (file.ApeTag);
		Assert.AreEqual (3, file.Properties!.FrameCount);
		Assert.IsTrue (AacFile.IsValidFormat (data));
	}

	[TestMethod]
	[DataRow (254)]
	[DataRow (255)]
	public void Read_ApeTagWithTheLongestKey_IsATag (int keyLength)
	{
		// APEv2 keys run to 255 characters. A tag whose item has one is a tag on every path, not 255 bytes of audio.
		var key = new string ('K', keyLength);
		var ape = new TagLibSharp2.Ape.ApeTag ();
		ape.SetValue (key, "value");
		var tag = ape.RenderWithOptions (includeHeader: true).ToArray ();
		var data = Concat (Id3v2Bytes ("Front"), Adts (11, length: 300), tag);

		var file = AacFile.Read (data).File!;
		Assert.AreEqual (11, file.Properties!.FrameCount);
		Assert.IsNotNull (file.ApeTag);
		Assert.AreEqual ("value", file.ApeTag!.GetValue (key));
		using var stream = new MemoryStream (data);
		Assert.AreEqual (11, AacFile.Read (stream, null, null).File!.Properties!.FrameCount);
		Assert.AreEqual (MediaFormat.Aac, MediaFile.DetectFormat (data));

		var saved = file.Render (data).ToArray ();
		Assert.AreEqual ("value", AacFile.Read (saved).File!.ApeTag!.GetValue (key));
	}

	[TestMethod]
	public void Read_HeaderForAnotherStream_IsSkippedNotCounted ()
	{
		// The fixed header does not change from frame to frame (ISO/IEC 13818-7 §6.2.1).
		var data = Concat (Adts (10), AdtsFrame (sampleRateIndex: 3), Adts (5));

		var props = Properties (data);
		Assert.AreEqual (15, props.FrameCount);
		Assert.AreEqual (44100, props.SampleRate);
	}

	[TestMethod]
	public void Read_PaddingBeforeTheFirstFrame_IsSkipped ()
	{
		var data = Concat (Id3v2Bytes ("T"), new byte[1000], Adts (10));

		Assert.AreEqual (10, Properties (data).FrameCount);
	}

	[TestMethod]
	public void Read_FirstFramePastTheSearchWindow_Fails ()
	{
		var data = Concat (Id3v2Bytes ("T"), new byte[AacStream.FirstFrameSearchBytes + 10], Adts (10));

		var result = AacFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "ADTS");
	}

	[TestMethod]
	public void Read_OneTruncatedFrame_IsAacWithNoDuration ()
	{
		var props = Properties (AdtsFrame (500)[..300]);

		Assert.AreEqual (0, props.FrameCount);
		Assert.AreEqual (TimeSpan.Zero, props.Duration);
		Assert.AreEqual (0, props.Bitrate);
	}

	[TestMethod]
	public void Read_Adif_ReadsTheFirstProgramConfigElement ()
	{
		var data = Adif (128_000, sampleRateIndex: 3, profile: 0, programs: [[true, false]], lfe: 1, comment: "hello");

		var props = Properties (data);
		Assert.AreEqual (AacStreamFormat.Adif, props.StreamFormat);
		Assert.AreEqual (48000, props.SampleRate);
		Assert.AreEqual (PceChannels ([2, 1], lfe: 1), props.Channels);
		Assert.AreEqual (AacProfile.Main, props.Profile);
		Assert.AreEqual (128, props.Bitrate);
		Assert.AreEqual (0, props.MpegVersion);
		Assert.AreEqual (0, props.FrameCount);
	}

	[TestMethod]
	[DataRow (false, false)]
	[DataRow (true, false)]
	[DataRow (false, true)]
	[DataRow (true, true)]
	public void Read_Adif_DurationIsPayloadBitsOverBitrate (bool variableRate, bool copyright)
	{
		var data = Adif (64_000, variableRate: variableRate, copyright: copyright, payloadBytes: 80_000, programs: [[true], [false]]);
		var header = data.Length - 80_000;

		var props = Properties (data);
		Assert.AreEqual (TimeSpan.FromTicks ((long)Math.Round (80_000.0 * 8 * TimeSpan.TicksPerSecond / 64_000)), props.Duration);
		Assert.IsTrue (header > 10);
	}

	[TestMethod]
	public void Read_Adif_HeaderLongerThanTheFirstRead_IsRead ()
	{
		// Two program config elements with 250-byte comments: a header past the 256-byte first read.
		var data = Adif (64_000, programs: [[true], [true, false]], comment: new string ('c', 250), payloadBytes: 8_000);

		var props = Properties (data);
		Assert.AreEqual (TimeSpan.FromSeconds (1), props.Duration);
		Assert.IsTrue (AacFile.IsValidFormat (data));
		Assert.AreEqual (MediaFormat.Aac, MediaFile.Read (new MemoryStream (data)).Format);
	}

	[TestMethod]
	public void Read_Adif_ProfileThreeIsReserved ()
	{
		Assert.AreEqual (AacProfile.Unknown, Properties (Adif (64_000, profile: 3)).Profile);
	}

	[TestMethod]
	public void Read_Adif_ZeroBitrate_HasNoDuration ()
	{
		Assert.AreEqual (TimeSpan.Zero, Properties (Adif (0)).Duration);
	}

	[TestMethod]
	public void Read_Adif_TagsAreNotPayload ()
	{
		var data = Concat (Id3v2Bytes ("Front"), Adif (8_000, payloadBytes: 1000), ApeBytes ("Ape"), Id3v1Bytes ("Back"));

		var props = Properties (data);
		Assert.AreEqual (TimeSpan.FromSeconds (1), props.Duration);
	}

	[TestMethod]
	public void Read_Adif_TruncatedHeader_Fails ()
	{
		var data = Adif (64_000, comment: "a comment", payloadBytes: 0);

		for (var cut = 4; cut < data.Length; cut++)
			Assert.IsFalse (AacFile.Read (data.AsSpan (0, cut)).IsSuccess, $"cut at {cut}");
		Assert.IsTrue (AacFile.Read (data).IsSuccess);
	}

	[TestMethod]
	public void Read_Adif_ReservedSampleRate_Fails ()
	{
		Assert.IsFalse (AacFile.Read (Adif (64_000, sampleRateIndex: 12)).IsSuccess);
	}

	[TestMethod]
	public void Read_NoTags_TagIsNull ()
	{
		var file = AacFile.Read (Adts (4)).File!;

		Assert.IsNull (file.Tag);
		Assert.IsFalse (file.HasId3v2Tag);
		Assert.IsFalse (file.HasApeTag);
		Assert.IsFalse (file.HasId3v1Tag);
		Assert.AreEqual (0, file.Id3v2Size);
	}

	[TestMethod]
	public void Read_Id3v2InFront_IsTheTag ()
	{
		var tag = Id3v2Bytes ("Front");
		var file = AacFile.Read (Concat (tag, Adts (10))).File!;

		Assert.AreEqual ("Front", file.Tag!.Title);
		Assert.AreEqual (tag.Length, file.Id3v2Size);
		Assert.AreEqual (10, file.Properties!.FrameCount);
	}

	[TestMethod]
	public void Read_ApeAndId3v1AtTheEnd_AreTagsNotFrames ()
	{
		var file = AacFile.Read (Concat (Adts (10), ApeBytes ("Ape"), Id3v1Bytes ("Back"))).File!;

		Assert.AreEqual ("Ape", file.ApeTag!.Title);
		Assert.AreEqual ("Back", file.Id3v1Tag!.Title);
		Assert.AreEqual (10, file.Properties!.FrameCount);
	}

	[TestMethod]
	public void Read_AllThreeTags_PrefersId3v2ThenApeThenId3v1 ()
	{
		var ape = new TagLibSharp2.Ape.ApeTag { Title = "Ape", Album = "ApeAlbum" };
		var id3v1 = new TagLibSharp2.Id3.Id3v1Tag { Title = "V1", Album = "V1Album", Comment = "V1Comment" };
		var data = Concat (Id3v2Bytes ("Front"), Adts (10), ape.Render ().ToArray (), id3v1.Render ().ToArray ());

		var tag = AacFile.Read (data).File!.Tag!;
		Assert.AreEqual ("Front", tag.Title);
		Assert.AreEqual ("ApeAlbum", tag.Album);
		Assert.AreEqual ("V1Comment", tag.Comment);
	}

	[TestMethod]
	public void Format_IsAac ()
	{
		var file = AacFile.Read (Adts (2)).File!;
		IMediaFile media = file;

		Assert.AreEqual (MediaFormat.Aac, file.Format);
		Assert.AreEqual (MediaTypes.Audio, media.MediaTypes);
		Assert.AreSame (file.Properties, media.AudioProperties);
		Assert.IsNull (media.VideoProperties);
		Assert.IsNull (media.ImageProperties);
		Assert.AreEqual (file.Properties!.Duration, file.Duration);
	}

	[TestMethod]
	public void EnsureId3v2Tag_CreatesOnceAndKeeps ()
	{
		var file = AacFile.Read (Adts (2)).File!;

		var tag = file.EnsureId3v2Tag ();
		Assert.AreSame (tag, file.EnsureId3v2Tag ());
		Assert.IsNotNull (file.Tag);
	}

	[TestMethod]
	public void Dispose_ClearsTheTagsAndIsIdempotent ()
	{
		var file = AacFile.Read (Concat (Id3v2Bytes ("Front"), Adts (2), ApeBytes ("A"), Id3v1Bytes ("B"))).File!;

		file.Dispose ();
		file.Dispose ();
		Assert.IsNull (file.Tag);
		Assert.IsNull (file.SourcePath);
	}

	[TestMethod]
	public void TryRead_ReportsSuccessAndFailure ()
	{
		Assert.IsTrue (AacFile.TryRead (Adts (2), out var file));
		Assert.IsNotNull (file);
		Assert.IsFalse (AacFile.TryRead (new byte[10], out var none));
		Assert.IsNull (none);
	}

	[TestMethod]
	public void IsValidFormat_AcceptsAdtsAdifAndId3Prefixed ()
	{
		Assert.IsTrue (AacFile.IsValidFormat (Adts (2)));
		Assert.IsTrue (AacFile.IsValidFormat (Adif (64_000)));
		Assert.IsTrue (AacFile.IsValidFormat (Concat (Id3v2Bytes ("T"), Adts (2))));
		Assert.IsTrue (AacFile.IsValidFormat (Concat (Id3v2Bytes ("T"), Adif (64_000))));
	}

	[TestMethod]
	public void IsValidFormat_RejectsMpegAudioAndNoise ()
	{
		Assert.IsFalse (AacFile.IsValidFormat (Mp3Frames (3)));
		Assert.IsFalse (AacFile.IsValidFormat (Concat (Id3v2Bytes ("T"), Mp3Frames (3))));
		Assert.IsFalse (AacFile.IsValidFormat ([0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00, 0x43, 0x00])); // UTF-16 LE BOM
		Assert.IsFalse (AacFile.IsValidFormat ([]));
		Assert.IsFalse (AacFile.IsValidFormat (Id3v2Bytes ("Only a tag")));
	}

	[TestMethod]
	public void IsValidFormat_AdtsHeaderTheNextFrameContradicts_IsFalse ()
	{
		var data = Concat (AdtsFrame (64), new byte[64]);

		Assert.IsFalse (AacFile.IsValidFormat (data));
	}

	[TestMethod]
	public void ReadResult_Equality ()
	{
		var failure = AacFileReadResult.Failure ("x");
		Assert.AreEqual (failure, AacFileReadResult.Failure ("x"));
		Assert.IsTrue (failure == AacFileReadResult.Failure ("x"));
		Assert.IsTrue (failure != AacFileReadResult.Failure ("y"));
		Assert.IsFalse (failure.Equals ((object)"x"));
		Assert.AreEqual (failure.GetHashCode (), AacFileReadResult.Failure ("x").GetHashCode ());
		var file = AacFile.Read (Adts (2)).File!;
		Assert.IsTrue (AacFileReadResult.Success (file).IsSuccess);
		Assert.AreEqual (AacFileReadResult.Success (file), AacFileReadResult.Success (file));
	}

	[TestMethod]
	public void ReadFromFile_SetsSourcePath ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Concat (Id3v2Bytes ("Disk"), Adts (5)));

		var result = AacFile.ReadFromFile ("/a.aac", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("/a.aac", result.File!.SourcePath);
		Assert.AreEqual ("Disk", result.File.Tag!.Title);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_SetsSourcePath ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Concat (Id3v2Bytes ("Disk"), Adts (5)));

		var result = await AacFile.ReadFromFileAsync ("/a.aac", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("/a.aac", result.File!.SourcePath);
		Assert.AreEqual (5, result.File.Properties!.FrameCount);
	}

	[TestMethod]
	public void ReadFromFile_Missing_Fails ()
	{
		var result = AacFile.ReadFromFile ("/missing.aac", new MockFileSystem ());

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "not found");
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Missing_Fails ()
	{
		var result = await AacFile.ReadFromFileAsync ("/missing.aac", new MockFileSystem ());

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Cancelled_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Adts (5));
		using var cts = new CancellationTokenSource ();
		await cts.CancelAsync ();

		var result = await AacFile.ReadFromFileAsync ("/a.aac", fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
	}
}
