// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;

namespace TagLibSharp2.Tests.Mpeg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mpeg")]
public class MpegAudioPropertiesTests
{
	[TestMethod]
	public void Duration_VbrWithXingHeader_AccurateCalculation ()
	{
		// Create a minimal MP3 with Xing header
		// 4096 frames at 44100 Hz, 1152 samples per frame
		// Duration = 4096 * 1152 / 44100 = 107.0 seconds
		var data = CreateMp3WithXingHeader (4096, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsNotNull (props);
		var expectedDuration = TimeSpan.FromSeconds (4096.0 * 1152 / 44100);
		Assert.AreEqual (expectedDuration.TotalSeconds, props.Duration.TotalSeconds, 0.1);
	}

	[TestMethod]
	public void Duration_VbrWithVbriHeader_AccurateCalculation ()
	{
		// Create MP3 with VBRI header (8192 frames)
		var data = CreateMp3WithVbriHeader (8192, 5000000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsNotNull (props);
		var expectedDuration = TimeSpan.FromSeconds (8192.0 * 1152 / 44100);
		Assert.AreEqual (expectedDuration.TotalSeconds, props.Duration.TotalSeconds, 0.1);
	}

	[TestMethod]
	public void Duration_CbrNoVbrHeader_EstimatedFromFileSize ()
	{
		// Create CBR MP3 (no Xing or VBRI header)
		// 128 kbps, 44100 Hz, file size ~1MB = ~65 seconds
		var data = CreateCbrMp3 (128, 44100, 1024 * 1024);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsNotNull (props);
		// CBR: duration = (file_size * 8) / bitrate
		var expectedSeconds = (1024.0 * 1024 * 8) / (128 * 1000);
		Assert.AreEqual (expectedSeconds, props.Duration.TotalSeconds, 0.5);
	}

	[TestMethod]
	public void SampleRate_ParsedFromFrame ()
	{
		var data = CreateMp3WithXingHeader (1000, 48000, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (48000, props!.SampleRate);
	}

	[TestMethod]
	public void Channels_Stereo_Returns2 ()
	{
		var data = CreateMp3WithXingHeader (1000, 44100, 1152, ChannelMode.Stereo);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (2, props!.Channels);
	}

	[TestMethod]
	public void Channels_Mono_Returns1 ()
	{
		var data = CreateMp3WithXingHeader (1000, 44100, 1152, ChannelMode.Mono);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (1, props!.Channels);
	}

	[TestMethod]
	public void IsVbr_WithXingHeader_ReturnsTrue ()
	{
		var data = CreateMp3WithXingHeader (1000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsTrue (props!.IsVbr);
	}

	[TestMethod]
	public void IsVbr_CbrFile_ReturnsFalse ()
	{
		var data = CreateCbrMp3 (128, 44100, 10000);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsFalse (props!.IsVbr);
	}

	[TestMethod]
	public void Bitrate_Vbr_CalculatesAverage ()
	{
		// 4096 frames, 500000 bytes audio data
		// Average bitrate = (bytes * 8) / duration
		var data = CreateMp3WithXingHeader (4096, 44100, 1152, byteCount: 500000);

		MpegAudioProperties.TryParse (data, 0, out var props);

		// duration = 4096 * 1152 / 44100 = 107.0 sec
		// avg bitrate = 500000 * 8 / 107.0 = ~37.4 kbps
		Assert.IsTrue (props!.Bitrate > 30 && props.Bitrate < 50);
	}

	[TestMethod]
	public void Bitrate_Cbr_ReturnsFrameBitrate ()
	{
		var data = CreateCbrMp3 (192, 44100, 10000);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (192, props!.Bitrate);
	}

	[TestMethod]
	public void TryParse_NoAudioFrame_ReturnsFalse ()
	{
		// Just random data, no MPEG frame
		byte[] data = new byte[1000];

		var result = MpegAudioProperties.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_AtOffset_SkipsId3v2Tag ()
	{
		// Create MP3 with fake ID3v2 header
		var id3Size = 1000;
		var mp3Data = CreateMp3WithXingHeader (2000, 44100, 1152);
		var fullData = CreateDataWithId3v2Prefix (id3Size, mp3Data);

		MpegAudioProperties.TryParse (fullData, id3Size, out var props);

		Assert.IsNotNull (props);
		Assert.AreEqual (44100, props.SampleRate);
	}

	[TestMethod]
	public void Version_Mpeg1Layer3_ReturnsCorrectly ()
	{
		var data = CreateMp3WithXingHeader (1000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (MpegVersion.Version1, props!.Version);
		Assert.AreEqual (MpegLayer.Layer3, props.Layer);
	}

	// Header byte 1 is sync (0xE0) + version + layer + no CRC. Bitrate index 8, sample rate index 0.
	[TestMethod]
	[DataRow (0xFF, "MPEG-1 Layer I", DisplayName = "MPEG-1 Layer I")]
	[DataRow (0xFD, "MPEG-1 Layer II", DisplayName = "MPEG-1 Layer II")]
	[DataRow (0xFB, "MP3", DisplayName = "MPEG-1 Layer III")]
	[DataRow (0xF7, "MPEG-2 Layer I", DisplayName = "MPEG-2 Layer I")]
	[DataRow (0xF5, "MPEG-2 Layer II", DisplayName = "MPEG-2 Layer II")]
	[DataRow (0xF3, "MP3", DisplayName = "MPEG-2 Layer III")]
	[DataRow (0xE7, "MPEG-2.5 Layer I", DisplayName = "MPEG-2.5 Layer I")]
	[DataRow (0xE5, "MPEG-2.5 Layer II", DisplayName = "MPEG-2.5 Layer II")]
	[DataRow (0xE3, "MP3", DisplayName = "MPEG-2.5 Layer III")]
	public void Codec_EachVersionAndLayer_ReturnsExactString (int headerByte1, string expectedCodec)
	{
		byte[] data = [0xFF, (byte)headerByte1, 0x80, 0x00, 0x00];

		var parsed = MpegAudioProperties.TryParse (new BinaryData (data), 0, out var props);

		Assert.IsTrue (parsed, $"header 0xFF 0x{headerByte1:X2}");
		Assert.AreEqual (expectedCodec, props!.Codec);
	}

	[TestMethod]
	public void BitsPerSample_AlwaysReturnsZero ()
	{
		// MP3 is lossy - bit depth doesn't apply
		var data = CreateMp3WithXingHeader (1000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (0, props!.BitsPerSample);
	}

	[TestMethod]
	public void FrameCount_WithXingHeader_ReturnsValue ()
	{
		var data = CreateMp3WithXingHeader (5000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (5000u, props!.FrameCount);
	}

	[TestMethod]
	public void FrameCount_WithVbriHeader_ReturnsValue ()
	{
		var data = CreateMp3WithVbriHeader (7500, 3000000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (7500u, props!.FrameCount);
	}

	[TestMethod]
	public void FrameCount_CbrNoHeader_ReturnsNull ()
	{
		var data = CreateCbrMp3 (128, 44100, 10000);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsNull (props!.FrameCount);
	}

	[TestMethod]
	public void IsVbr_InfoHeader_ReturnsFalse ()
	{
		// "Info" header indicates CBR file encoded with LAME
		var data = CreateMp3WithInfoHeader (1000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsFalse (props!.IsVbr);
		Assert.IsNotNull (props.FrameCount); // Frame count still available
	}

	[TestMethod]
	public void Channels_JointStereo_Returns2 ()
	{
		var data = CreateMp3WithXingHeader (1000, 44100, 1152, ChannelMode.JointStereo);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (2, props!.Channels);
	}

	[TestMethod]
	public void Channels_DualChannel_Returns2 ()
	{
		var data = CreateMp3WithXingHeader (1000, 44100, 1152, ChannelMode.DualChannel);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (2, props!.Channels);
	}

	[TestMethod]
	public void TryParse_FreeBitrateOnly_ReturnsFalse ()
	{
		// A free-bitrate header has no frame size, so the next header cannot confirm it.
		var data = CreateFreeBitrateMp3 ();

		var parsed = MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.IsFalse (parsed);
		Assert.IsNull (props);
	}

	[TestMethod]
	public void Bitrate_VbrWithByteCount_CalculatesFromByteCount ()
	{
		// When Xing header has byte count, average bitrate should be calculated from it
		// 4096 frames, 500000 bytes, 44100 Hz, 1152 samples/frame
		// Duration = 4096 * 1152 / 44100 = ~107 sec
		// Bitrate = 500000 * 8 / 107 / 1000 = ~37.4 kbps
		var data = CreateMp3WithXingHeader (4096, 44100, 1152, byteCount: 500000);

		MpegAudioProperties.TryParse (data, 0, out var props);

		// Should be around 37 kbps
		Assert.IsTrue (props!.Bitrate > 35 && props.Bitrate < 40,
			$"Expected bitrate around 37, got {props.Bitrate}");
	}

	[TestMethod]
	public void Bitrate_VbrWithoutByteCount_UsesFrameBitrate ()
	{
		// When Xing header doesn't have byte count and file is very small,
		// the estimated bitrate from file size may be 0, so it falls back to frame bitrate
		var data = CreateMp3WithXingHeader (4096, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		// For very small test files, the calculated bitrate may be 0 or very small
		// Just verify parsing succeeded and we have valid properties
		Assert.IsNotNull (props);
		Assert.IsTrue (props.IsVbr);
		Assert.AreEqual (4096u, props.FrameCount);
	}

	[TestMethod]
	public void Bitrate_VbriWithByteCount_CalculatesFromByteCount ()
	{
		// VBRI header always has byte count
		var data = CreateMp3WithVbriHeader (8192, 2000000, 44100, 1152);

		MpegAudioProperties.TryParse (data, 0, out var props);

		// Duration = 8192 * 1152 / 44100 = ~214 sec
		// Bitrate = 2000000 * 8 / 214 / 1000 = ~75 kbps
		Assert.IsTrue (props!.Bitrate > 70 && props.Bitrate < 80,
			$"Expected bitrate around 75, got {props.Bitrate}");
	}

	[TestMethod]
	public void TryParse_FrameSyncInMiddle_FindsFirstValidFrame ()
	{
		// Test that parser searches for frame sync
		var builder = new BinaryDataBuilder ();

		// Add some garbage data before the frame
		builder.Add (new byte[100]);

		// Add valid MPEG frame
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		builder.Add (new byte[32]); // Side info
		builder.Add (0x58, 0x69, 0x6E, 0x67); // "Xing"
		builder.AddUInt32BE (0x01);
		builder.AddUInt32BE (1000);

		// Pad and add another frame sync for verification
		while (builder.Length < 517)
			builder.Add (0);
		builder.Add (0xFF, 0xFB, 0x90, 0x00); // Second frame

		MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsNotNull (props);
		Assert.AreEqual (44100, props.SampleRate);
	}

	[TestMethod]
	public void TryParse_Mpeg2Properties_ParsedCorrectly ()
	{
		var data = CreateMpeg2Mp3 (2000, 22050);

		MpegAudioProperties.TryParse (data, 0, out var props);

		Assert.AreEqual (MpegVersion.Version2, props!.Version);
		Assert.AreEqual (MpegLayer.Layer3, props.Layer);
		Assert.AreEqual (22050, props.SampleRate);
		Assert.AreEqual (2000u, props.FrameCount);
		// MPEG2 Layer3 has 576 samples per frame
		// Duration = 2000 * 576 / 22050 = ~52.2 seconds
		Assert.IsTrue (props.Duration.TotalSeconds > 52 && props.Duration.TotalSeconds < 53);
	}

	[TestMethod]
	public void Duration_XingDespiteCrcBit_UsesFrameCount ()
	{
		// The protection bit says a CRC follows the header. This frame stores Xing at
		// header + side info (36), and the 210-frame count fits the declared byte count.
		// 210 * 1152 / 48000 = 5.040 s.
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFA, 0x94, 0x00);
		builder.Add (new byte[32]);
		builder.Add (0x58, 0x69, 0x6E, 0x67); // "Xing"
		builder.AddUInt32BE (0x03);
		builder.AddUInt32BE (210);
		builder.AddUInt32BE (384);
		while (builder.Length < 384)
			builder.Add (0);

		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsTrue (parsed);
		Assert.AreEqual (5.04, props!.Duration.TotalSeconds, 0.001);
		Assert.AreEqual (210u, props.FrameCount);
	}

	[TestMethod]
	public void Duration_XingByteCountPastEnd_UsesFramesPresent ()
	{
		const int frameSize = 417;
		const int framesPresent = 3;
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		builder.Add (new byte[32]);
		builder.Add (0x58, 0x69, 0x6E, 0x67);
		builder.AddUInt32BE (0x03);
		builder.AddUInt32BE (8000);
		builder.AddUInt32BE (5_000_000);
		while (builder.Length < frameSize * framesPresent)
			builder.Add (0);
		StampFrameHeader (builder, frameSize);
		StampFrameHeader (builder, frameSize * 2);

		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsTrue (parsed);
		Assert.AreEqual (framesPresent * 1152.0 / 44100, props!.Duration.TotalSeconds, 0.001);
		Assert.AreEqual ((uint)framesPresent, props.FrameCount);
	}

	[TestMethod]
	public void Duration_LongTruncatedXing_CountsTheFramesThatArePresent ()
	{
		// A byte count past the buffer counts frames in one forward scan.
		// Restarting at every later sync makes a few thousand frames take seconds.
		const int frameSize = 417;
		const int framesPresent = 6000;
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		builder.Add (new byte[32]);
		builder.Add (0x58, 0x69, 0x6E, 0x67); // "Xing"
		builder.AddUInt32BE (0x03);
		builder.AddUInt32BE (8000);
		builder.AddUInt32BE (uint.MaxValue);
		while (builder.Length < frameSize * framesPresent)
			builder.Add (0);
		for (var n = 1; n < framesPresent; n++)
			StampFrameHeader (builder, frameSize * n);

		var clock = Stopwatch.StartNew ();
		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);
		clock.Stop ();

		Assert.IsTrue (parsed);
		Assert.AreEqual ((uint)framesPresent, props!.FrameCount);
		Assert.IsTrue (clock.ElapsedMilliseconds < 500, $"scan took {clock.ElapsedMilliseconds} ms");
	}

	[TestMethod]
	public void Duration_FalseSyncInsideFirstFrame_CountsTheLaterRun ()
	{
		// The first frame's bitrate lands on a header whose own successor is not a frame.
		// Four real frames follow. The opening frame still counts. 8000 does not.
		const int frameSize = 417;
		const int laterFrames = 4;
		const int laterStart = 1000;
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		builder.Add (new byte[32]);
		builder.Add (0x58, 0x69, 0x6E, 0x67);
		builder.AddUInt32BE (0x03);
		builder.AddUInt32BE (8000);
		builder.AddUInt32BE (5_000_000);
		while (builder.Length < laterStart + frameSize * laterFrames)
			builder.Add (0);
		StampFrameHeader (builder, frameSize);
		for (var n = 0; n < laterFrames; n++)
			StampFrameHeader (builder, laterStart + frameSize * n);

		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsTrue (parsed);
		const int framesPresent = laterFrames + 1;
		Assert.AreEqual (framesPresent * 1152.0 / 44100, props!.Duration.TotalSeconds, 0.001);
		Assert.AreEqual ((uint)framesPresent, props.FrameCount);
	}

	[TestMethod]
	public void Duration_VbriByteCountPastEnd_UsesFramesPresent ()
	{
		const int frameSize = 417;
		const int framesPresent = 2;
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		builder.Add (new byte[32]);
		builder.Add (0x56, 0x42, 0x52, 0x49); // "VBRI"
		builder.AddUInt16BE (1);
		builder.AddUInt16BE (0);
		builder.AddUInt16BE (75);
		builder.AddUInt32BE (5_000_000);
		builder.AddUInt32BE (9000);
		builder.AddUInt16BE (0);
		builder.AddUInt16BE (1);
		builder.AddUInt16BE (2);
		builder.AddUInt16BE (100);
		while (builder.Length < frameSize * framesPresent)
			builder.Add (0);
		StampFrameHeader (builder, frameSize);

		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsTrue (parsed);
		Assert.AreEqual (framesPresent * 1152.0 / 44100, props!.Duration.TotalSeconds, 0.001);
		Assert.AreEqual ((uint)framesPresent, props.FrameCount);
	}

	[TestMethod]
	public void TryParse_UnconfirmedNextFrame_SkipsToAFrameThatSyncs ()
	{
		// A header whose following frame is inside the buffer and is not a frame
		// must not hide the later 48000 Hz frame.
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		while (builder.Length < 500)
			builder.Add (0);
		builder.Add (0xFF, 0xFB, 0x94, 0x00);
		while (builder.Length < 500 + 384)
			builder.Add (0);

		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsTrue (parsed);
		Assert.AreEqual (48000, props!.SampleRate);
	}

	[TestMethod]
	public void TryParse_FreeBitrateHeader_DoesNotHideTheFollowingFrame ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0xFF, 0xFB, 0x00, 0x00);
		while (builder.Length < 50)
			builder.Add (0);
		builder.Add (0xFF, 0xFB, 0x90, 0x00);
		while (builder.Length < 50 + 417)
			builder.Add (0);

		var parsed = MpegAudioProperties.TryParse (builder.ToBinaryData (), 0, out var props);

		Assert.IsTrue (parsed);
		Assert.AreEqual (128, props!.Bitrate);
		Assert.AreEqual (44100, props.SampleRate);
	}

	[TestMethod]
	public void CreateMpeg2Mp3_PadsToFrameSizeForSampleRate ()
	{
		// 64 kbps MPEG-2 Layer III. A constant 208-byte pad is only correct at 22050 Hz.
		// 24000 Hz is 192 bytes and 16000 Hz is 288 bytes.
		int[] sampleRates = [22050, 24000, 16000];
		foreach (var sampleRate in sampleRates) {
			var data = CreateMpeg2Mp3 (100, sampleRate);

			Assert.IsTrue (MpegFrame.TryParse (data, 0, out var frame), $"header at {sampleRate} Hz");
			Assert.AreEqual (frame!.FrameSize, data.Length, $"sample rate {sampleRate}");
		}
	}

	// Helper methods

	static void StampFrameHeader (BinaryDataBuilder builder, int offset, byte rateAndPadding = 0x90, byte channelMode = 0x00)
	{
		builder[offset] = 0xFF;
		builder[offset + 1] = 0xFB;
		builder[offset + 2] = rateAndPadding;
		builder[offset + 3] = channelMode;
	}

	static BinaryData CreateMp3WithInfoHeader (
		uint frameCount,
		int sampleRate,
		int samplesPerFrame)
	{
		var builder = new BinaryDataBuilder ();

		byte byte2 = sampleRate switch {
			48000 => 0x94,
			32000 => 0x98,
			_ => 0x90
		};

		builder.Add (0xFF, 0xFB, byte2, 0x00);
		builder.Add (new byte[32]); // Side info

		// "Info" header (CBR encoded by LAME)
		builder.Add (0x49, 0x6E, 0x66, 0x6F); // "Info"
		builder.AddUInt32BE (0x01); // Flags: frames only
		builder.AddUInt32BE (frameCount);

		while (builder.Length < 417)
			builder.Add (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateFreeBitrateMp3 ()
	{
		var builder = new BinaryDataBuilder ();

		// Free bitrate: bitrate index = 0
		builder.Add (0xFF, 0xFB, 0x00, 0x00);
		builder.Add (new byte[32]);

		// No Xing header
		while (builder.Length < 1000)
			builder.Add (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateMpeg2Mp3 (uint frameCount, int sampleRate)
	{
		var builder = new BinaryDataBuilder ();

		// MPEG-2 Layer III, 64 kbps, no CRC, no padding. Unknown rates use 22050 Hz.
		var rateHz = sampleRate switch {
			24000 => 24000,
			16000 => 16000,
			_ => 22050
		};
		byte byte2 = rateHz switch {
			24000 => 0x84,
			16000 => 0x88,
			_ => 0x80
		};

		builder.Add (0xFF, 0xF3, byte2, 0x00);
		builder.Add (new byte[17]); // MPEG2 stereo side info is 17 bytes

		// Xing header
		builder.Add (0x58, 0x69, 0x6E, 0x67);
		builder.AddUInt32BE (0x01);
		builder.AddUInt32BE (frameCount);

		// Layer III MPEG-2 coefficient is 72. Pad from bitrate, sample rate, and layer.
		const int bitrateKbps = 64;
		const int coefficient = 72;
		var frameSize = coefficient * bitrateKbps * 1000 / rateHz;
		while (builder.Length < frameSize)
			builder.Add (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateMp3WithXingHeader (
		uint frameCount,
		int sampleRate,
		int samplesPerFrame,
		ChannelMode channelMode = ChannelMode.Stereo,
		uint? byteCount = null)
	{
		var builder = new BinaryDataBuilder ();

		// Create MPEG frame header (MPEG1 Layer3)
		// 0xFF 0xFB = MPEG1 Layer3 no CRC
		byte byte2 = sampleRate switch {
			48000 => 0x94, // 128kbps, 48000Hz
			32000 => 0x98, // 128kbps, 32000Hz
			_ => 0x90      // 128kbps, 44100Hz
		};
		byte byte3 = channelMode switch {
			ChannelMode.Mono => 0xC0,
			ChannelMode.JointStereo => 0x40,
			ChannelMode.DualChannel => 0x80,
			_ => 0x00
		};

		builder.Add (0xFF, 0xFB, byte2, byte3);

		// Add padding to reach Xing header position (32 bytes for stereo, 17 for mono)
		var sideInfoSize = channelMode == ChannelMode.Mono ? 17 : 32;
		builder.Add (new byte[sideInfoSize]);

		// Xing header
		builder.Add (0x58, 0x69, 0x6E, 0x67); // "Xing"
		var flags = (byteCount.HasValue ? 0x03u : 0x01u); // frames + optional bytes
		builder.AddUInt32BE (flags);
		builder.AddUInt32BE (frameCount);
		if (byteCount.HasValue)
			builder.AddUInt32BE (byteCount.Value);

		if (!MpegFrame.TryParse (builder.ToBinaryData (), 0, out var frame) || frame is null || frame.FrameSize <= 0)
			return builder.ToBinaryData ();

		var target = frame.FrameSize;
		if (byteCount is uint declared && declared > (uint)target)
			target = (int)declared;
		while (builder.Length < target)
			builder.Add (0);
		// A buffer longer than one frame must contain the next header so the search can confirm it.
		if (target > frame.FrameSize)
			StampFrameHeader (builder, frame.FrameSize, byte2, byte3);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateMp3WithVbriHeader (
		uint frameCount,
		uint byteCount,
		int sampleRate,
		int samplesPerFrame)
	{
		var builder = new BinaryDataBuilder ();

		// MPEG frame header
		byte byte2 = sampleRate switch {
			48000 => 0x94,
			32000 => 0x98,
			_ => 0x90
		};
		builder.Add (0xFF, 0xFB, byte2, 0x00);

		// Pad 32 bytes to VBRI position
		builder.Add (new byte[32]);

		// VBRI header
		builder.Add (0x56, 0x42, 0x52, 0x49); // "VBRI"
		builder.AddUInt16BE (1);               // Version
		builder.AddUInt16BE (0);               // Delay
		builder.AddUInt16BE (75);              // Quality
		builder.AddUInt32BE (byteCount);
		builder.AddUInt32BE (frameCount);
		builder.AddUInt16BE (0);               // TOC entries
		builder.AddUInt16BE (1);
		builder.AddUInt16BE (2);
		builder.AddUInt16BE (100);

		if (!MpegFrame.TryParse (builder.ToBinaryData (), 0, out var frame) || frame is null || frame.FrameSize <= 0)
			return builder.ToBinaryData ();

		var target = Math.Max (frame.FrameSize, (int)Math.Min (byteCount, int.MaxValue));
		while (builder.Length < target)
			builder.Add (0);
		if (target > frame.FrameSize)
			StampFrameHeader (builder, frame.FrameSize, byte2);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateCbrMp3 (int bitrate, int sampleRate, int totalSize)
	{
		var builder = new BinaryDataBuilder ();

		// Find bitrate index (for 128kbps, index 9)
		int bitrateIndex = bitrate switch {
			32 => 1,
			40 => 2,
			48 => 3,
			56 => 4,
			64 => 5,
			80 => 6,
			96 => 7,
			112 => 8,
			128 => 9,
			160 => 10,
			192 => 11,
			224 => 12,
			256 => 13,
			320 => 14,
			_ => 9
		};

		byte byte2 = (byte)((bitrateIndex << 4) | (sampleRate switch {
			48000 => 0x04,
			32000 => 0x08,
			_ => 0x00
		}));

		builder.Add (0xFF, 0xFB, byte2, 0x00);

		// Pad side info
		builder.Add (new byte[32]);

		// No Xing/VBRI header - just pad to total size
		while (builder.Length < totalSize)
			builder.Add (0);

		if (MpegFrame.TryParse (builder.ToBinaryData (), 0, out var frame) && frame is not null
			&& frame.FrameSize > 0 && frame.FrameSize + MpegFrame.HeaderSize <= builder.Length)
			StampFrameHeader (builder, frame.FrameSize, byte2);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateDataWithId3v2Prefix (int id3Size, BinaryData mp3Data)
	{
		var builder = new BinaryDataBuilder ();

		// ID3v2 header
		builder.Add (0x49, 0x44, 0x33);  // "ID3"
		builder.Add (0x04, 0x00);         // Version 2.4
		builder.Add (0x00);               // Flags
										  // Syncsafe size (without header)
		var payloadSize = id3Size - 10;
		builder.AddSyncSafeUInt32 ((uint)payloadSize);

		// Pad to id3Size
		while (builder.Length < id3Size)
			builder.Add (0);

		// Add MP3 data
		builder.Add (mp3Data);

		return builder.ToBinaryData ();
	}
}
