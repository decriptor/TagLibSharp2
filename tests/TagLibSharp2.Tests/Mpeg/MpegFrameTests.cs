// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;

namespace TagLibSharp2.Tests.Mpeg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mpeg")]
public class MpegFrameTests
{
	// MPEG frame header is 4 bytes:
	// Bits 0-10:  Sync word (0x7FF = 11 ones)
	// Bits 11-12: Version (00=2.5, 01=reserved, 10=2, 11=1)
	// Bits 13-14: Layer (00=reserved, 01=III, 10=II, 11=I)
	// Bit 15:     Protection bit (0=CRC, 1=no CRC)
	// Bits 16-19: Bitrate index
	// Bits 20-21: Sample rate index
	// Bit 22:     Padding
	// Bit 23:     Private
	// Bits 24-25: Channel mode
	// Bits 26-27: Mode extension
	// Bit 28:     Copyright
	// Bit 29:     Original
	// Bits 30-31: Emphasis

	[TestMethod]
	public void TryParse_ValidMpeg1Layer3Frame_ReturnsTrue ()
	{
		// MPEG1 Layer 3, 128 kbps, 44100 Hz, stereo
		// 0xFF 0xFB = sync + MPEG1 + Layer3 + no CRC
		// 0x90 = 128 kbps (index 9) + 44100 Hz (index 0) + no padding
		// 0x00 = stereo + mode ext 0 + no copyright + no original + emphasis none
		byte[] data = [0xFF, 0xFB, 0x90, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (result);
		Assert.IsNotNull (frame);
		Assert.AreEqual (MpegVersion.Version1, frame.Version);
		Assert.AreEqual (MpegLayer.Layer3, frame.Layer);
		Assert.AreEqual (128, frame.Bitrate);
		Assert.AreEqual (44100, frame.SampleRate);
		Assert.AreEqual (ChannelMode.Stereo, frame.ChannelMode);
	}

	[TestMethod]
	public void TryParse_ValidMpeg2Layer3Frame_ReturnsTrue ()
	{
		// MPEG2 Layer 3, 64 kbps, 22050 Hz
		// 0xFF 0xF3 = sync + MPEG2 + Layer3 + no CRC
		// 0x80 = 64 kbps (index 8 for MPEG2 L3) + 22050 Hz (index 0 for MPEG2)
		byte[] data = [0xFF, 0xF3, 0x80, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (result);
		Assert.IsNotNull (frame);
		Assert.AreEqual (MpegVersion.Version2, frame.Version);
		Assert.AreEqual (MpegLayer.Layer3, frame.Layer);
		Assert.AreEqual (64, frame.Bitrate);
		Assert.AreEqual (22050, frame.SampleRate);
	}

	[TestMethod]
	public void TryParse_NoSyncWord_ReturnsFalse ()
	{
		byte[] data = [0x00, 0x00, 0x00, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_TooShort_ReturnsFalse ()
	{
		byte[] data = [0xFF, 0xFB, 0x90]; // Only 3 bytes

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_InvalidVersion_ReturnsFalse ()
	{
		// Byte 1 format: [SSS VV LL P] where VV=01 is invalid
		// 0xEB = 1110 1011 = sync(111) + version(01 = invalid) + layer(01 = L3) + prot(1)
		byte[] data = [0xFF, 0xEB, 0x90, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_InvalidLayer_ReturnsFalse ()
	{
		// Byte 1 format: [SSS VV LL P] where LL=00 is invalid
		// 0xF9 = 1111 1001 = sync(111) + version(11 = V1) + layer(00 = invalid) + prot(1)
		byte[] data = [0xFF, 0xF9, 0x90, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_InvalidBitrate_ReturnsFalse ()
	{
		// 0xFF 0xFB 0xF0 = 128kbps header but bitrate index 15 (invalid)
		byte[] data = [0xFF, 0xFB, 0xF0, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_FreeBitrate_ReturnsTrue ()
	{
		// Bitrate index 0 = "free" format (valid but bitrate = 0)
		byte[] data = [0xFF, 0xFB, 0x00, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (result);
		Assert.AreEqual (0, frame!.Bitrate);
	}

	[TestMethod]
	public void TryParse_AtOffset_WorksCorrectly ()
	{
		byte[] data = [0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 3, out var frame);

		Assert.IsTrue (result);
		Assert.AreEqual (MpegVersion.Version1, frame!.Version);
	}

	[TestMethod]
	public void FrameSize_CalculatedCorrectly ()
	{
		// MPEG1 Layer 3, 128 kbps, 44100 Hz, no padding
		// Frame size = 144 * bitrate / sample_rate
		// = 144 * 128000 / 44100 = 417.something, truncated = 417 bytes
		byte[] data = [0xFF, 0xFB, 0x90, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (417, frame!.FrameSize);
	}

	[TestMethod]
	public void FrameSize_Mpeg2Layer3_CalculatedCorrectly ()
	{
		// MPEG-2 Layer III, 64 kbps, 22050 Hz, no padding
		// Frame size = 72 * bitrate / sample_rate
		// = 72 * 64000 / 22050 = 208.97..., truncated = 208 bytes
		byte[] data = [0xFF, 0xF3, 0x80, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (208, frame!.FrameSize);
	}

	[TestMethod]
	public void FrameSize_Mpeg2Layer3_WithPadding_CalculatedCorrectly ()
	{
		// MPEG-2 Layer III, 64 kbps, 22050 Hz, padding set.
		// 0xFF 0xF3 = sync + MPEG-2 + Layer III + no CRC
		// 0x82 = 64 kbps (index 8) + 22050 Hz (index 0) + padding
		// Layer III slot is 1 byte. MPEG-2 Layer III coefficient is 72.
		// 72 * 64000 / 22050 = 208, plus one padding slot = 209
		byte[] data = [0xFF, 0xF3, 0x82, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (MpegVersion.Version2, frame!.Version);
		Assert.AreEqual (MpegLayer.Layer3, frame.Layer);
		Assert.AreEqual (64, frame.Bitrate);
		Assert.AreEqual (22050, frame.SampleRate);
		Assert.IsTrue (frame.HasPadding);
		Assert.AreEqual (209, frame.FrameSize);
	}

	[TestMethod]
	public void FrameSize_Mpeg25Layer3_CalculatedCorrectly ()
	{
		// MPEG-2.5 Layer III, 64 kbps, 11025 Hz, no padding.
		// 0xFF 0xE3 = sync + MPEG-2.5 + Layer III + no CRC
		// 0x80 = 64 kbps (index 8) + 11025 Hz (index 0) + no padding
		// Coefficient 72, slot 1: 72 * 64000 / 11025 = 417
		byte[] data = [0xFF, 0xE3, 0x80, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (MpegVersion.Version25, frame!.Version);
		Assert.AreEqual (MpegLayer.Layer3, frame.Layer);
		Assert.AreEqual (64, frame.Bitrate);
		Assert.AreEqual (11025, frame.SampleRate);
		Assert.IsFalse (frame.HasPadding);
		Assert.AreEqual (417, frame.FrameSize);
	}

	[TestMethod]
	public void FrameSize_Mpeg2Layer2_CalculatedCorrectly ()
	{
		// MPEG-2 Layer II, 64 kbps, 22050 Hz, no padding.
		// 0xFF 0xF5 = sync + MPEG-2 + Layer II + no CRC
		// 0x80 = 64 kbps (index 8) + 22050 Hz (index 0) + no padding
		// Layer II coefficient is 144 on every version (1152 samples), slot 1.
		// 144 * 64000 / 22050 = 417. A coefficient of 72 would yield 208.
		byte[] data = [0xFF, 0xF5, 0x80, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (MpegVersion.Version2, frame!.Version);
		Assert.AreEqual (MpegLayer.Layer2, frame.Layer);
		Assert.AreEqual (64, frame.Bitrate);
		Assert.AreEqual (22050, frame.SampleRate);
		Assert.IsFalse (frame.HasPadding);
		Assert.AreEqual (417, frame.FrameSize);
	}

	[TestMethod]
	public void FrameSize_WithPadding_CalculatedCorrectly ()
	{
		// MPEG1 Layer 3, 128 kbps, 44100 Hz, WITH padding
		// 0x92 = 128 kbps + 44100 Hz + padding bit set
		byte[] data = [0xFF, 0xFB, 0x92, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (418, frame!.FrameSize); // 417 + 1 padding
	}

	[TestMethod]
	public void SamplesPerFrame_Mpeg1Layer3_Returns1152 ()
	{
		byte[] data = [0xFF, 0xFB, 0x90, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (1152, frame!.SamplesPerFrame);
	}

	[TestMethod]
	public void SamplesPerFrame_Mpeg2Layer3_Returns576 ()
	{
		byte[] data = [0xFF, 0xF3, 0x80, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (576, frame!.SamplesPerFrame);
	}

	[TestMethod]
	public void TryParse_ChannelModeMono_ParsedCorrectly ()
	{
		// 0xFF 0xFB 0x90 0xC0 = mono channel mode (bits 24-25 = 11)
		byte[] data = [0xFF, 0xFB, 0x90, 0xC0];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (ChannelMode.Mono, frame!.ChannelMode);
	}

	[TestMethod]
	public void TryParse_ChannelModeJointStereo_ParsedCorrectly ()
	{
		// 0xFF 0xFB 0x90 0x40 = joint stereo (bits 24-25 = 01)
		byte[] data = [0xFF, 0xFB, 0x90, 0x40];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (ChannelMode.JointStereo, frame!.ChannelMode);
	}

	[TestMethod]
	public void TryParse_HasProtection_ParsedCorrectly ()
	{
		// 0xFF 0xFA = protection bit = 0 (CRC present)
		byte[] data = [0xFF, 0xFA, 0x90, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (frame!.HasCrc);
	}

	[TestMethod]
	public void HeaderSize_ReturnsCorrectValue ()
	{
		Assert.AreEqual (4, MpegFrame.HeaderSize);
	}

	// Common sample rates for testing
	// Byte 2: [BBBB RR PP] where BBBB=bitrate, RR=sample rate, PP=padding+private
	[TestMethod]
	[DataRow (0x90, 44100)] // Sample rate index 0 (bits 3-2 = 00)
	[DataRow (0x94, 48000)] // Sample rate index 1 (bits 3-2 = 01)
	[DataRow (0x98, 32000)] // Sample rate index 2 (bits 3-2 = 10)
	public void SampleRate_Mpeg1_AllIndexes (int byte2, int expectedRate)
	{
		byte[] data = [0xFF, 0xFB, (byte)byte2, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (expectedRate, frame!.SampleRate);
	}

	[TestMethod]
	public void TryParse_Mpeg25Layer3_ReturnsTrue ()
	{
		// MPEG2.5 Layer 3: version bits = 00
		// 0xFF 0xE3 = sync(0xFF) + 111 00 01 1 = MPEG2.5 + Layer3 + no CRC
		// 0x80 = 64 kbps for MPEG2/2.5 L3 + 11025 Hz (index 0 for MPEG2.5)
		byte[] data = [0xFF, 0xE3, 0x80, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (result);
		Assert.AreEqual (MpegVersion.Version25, frame!.Version);
		Assert.AreEqual (MpegLayer.Layer3, frame.Layer);
		Assert.AreEqual (11025, frame.SampleRate);
	}

	[TestMethod]
	[DataRow (0x80, 22050)] // Sample rate index 0 for MPEG2
	[DataRow (0x84, 24000)] // Sample rate index 1 for MPEG2
	[DataRow (0x88, 16000)] // Sample rate index 2 for MPEG2
	public void SampleRate_Mpeg2_AllIndexes (int byte2, int expectedRate)
	{
		// 0xFF 0xF3 = MPEG2 Layer3
		byte[] data = [0xFF, 0xF3, (byte)byte2, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (expectedRate, frame!.SampleRate);
	}

	[TestMethod]
	[DataRow (0x80, 11025)] // Sample rate index 0 for MPEG2.5
	[DataRow (0x84, 12000)] // Sample rate index 1 for MPEG2.5
	[DataRow (0x88, 8000)]  // Sample rate index 2 for MPEG2.5
	public void SampleRate_Mpeg25_AllIndexes (int byte2, int expectedRate)
	{
		// 0xFF 0xE3 = MPEG2.5 Layer3
		byte[] data = [0xFF, 0xE3, (byte)byte2, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (expectedRate, frame!.SampleRate);
	}

	[TestMethod]
	public void TryParse_ReservedSampleRate_ReturnsFalse ()
	{
		// Sample rate index 3 is reserved
		// 0x9C = 128kbps + sample rate index 3 (bits 3-2 = 11)
		byte[] data = [0xFF, 0xFB, 0x9C, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_Layer1_ReturnsTrue ()
	{
		// MPEG1 Layer 1: layer bits = 11
		// 0xFF 0xFF = sync + MPEG1 + Layer1 + no CRC
		// 0x50 = 160 kbps (index 5 for Layer1) + 44100 Hz
		byte[] data = [0xFF, 0xFF, 0x50, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (result);
		Assert.AreEqual (MpegVersion.Version1, frame!.Version);
		Assert.AreEqual (MpegLayer.Layer1, frame.Layer);
		Assert.AreEqual (160, frame.Bitrate);
	}

	[TestMethod]
	public void TryParse_Layer2_ReturnsTrue ()
	{
		// MPEG1 Layer 2: layer bits = 10
		// 0xFF 0xFD = sync + MPEG1 + Layer2 + no CRC
		// 0x80 = 128 kbps (index 8 for Layer2) + 44100 Hz
		byte[] data = [0xFF, 0xFD, 0x80, 0x00];

		var result = MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (result);
		Assert.AreEqual (MpegVersion.Version1, frame!.Version);
		Assert.AreEqual (MpegLayer.Layer2, frame.Layer);
		Assert.AreEqual (128, frame.Bitrate); // Index 8 for L2 = 128kbps
	}

	[TestMethod]
	public void SamplesPerFrame_Layer1_Returns384 ()
	{
		// Layer 1 always has 384 samples per frame
		byte[] data = [0xFF, 0xFF, 0x50, 0x00]; // MPEG1 Layer1

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (384, frame!.SamplesPerFrame);
	}

	[TestMethod]
	public void SamplesPerFrame_Mpeg1Layer2_Returns1152 ()
	{
		byte[] data = [0xFF, 0xFD, 0x90, 0x00]; // MPEG1 Layer2

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (1152, frame!.SamplesPerFrame);
	}

	[TestMethod]
	public void SamplesPerFrame_Mpeg2Layer2_Returns1152 ()
	{
		// MPEG2 Layer2 also has 1152 samples per frame
		byte[] data = [0xFF, 0xF5, 0x80, 0x00]; // MPEG2 Layer2

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (1152, frame!.SamplesPerFrame);
	}

	[TestMethod]
	public void FrameSize_Layer1_CalculatedCorrectly ()
	{
		// Layer 1: frame size = (12 * bitrate / sampleRate + padding) * 4
		// 384 kbps, 44100 Hz, no padding
		// = (12 * 384000 / 44100) * 4 = 104.4... * 4 = 417.6 truncated = 416 bytes
		// Actually: (12 * 384 * 1000 / 44100) * 4 = 104 * 4 = 416
		byte[] data = [0xFF, 0xFF, 0xC0, 0x00]; // MPEG1 Layer1 384kbps 44100Hz

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (MpegLayer.Layer1, frame!.Layer);
		Assert.AreEqual (384, frame.Bitrate);
		// (12 * 384 * 1000 / 44100) * 4 = 104 * 4 = 416
		Assert.AreEqual (416, frame.FrameSize);
	}

	[TestMethod]
	public void FrameSize_Layer1_WithPadding_CalculatedCorrectly ()
	{
		// Layer 1 with padding: padding slot = 4 bytes
		byte[] data = [0xFF, 0xFF, 0xC2, 0x00]; // MPEG1 Layer1 384kbps 44100Hz WITH padding

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (MpegLayer.Layer1, frame!.Layer);
		Assert.IsTrue (frame.HasPadding);
		// (12 * 384 * 1000 / 44100 + 1) * 4 = 105 * 4 = 420
		Assert.AreEqual (420, frame.FrameSize);
	}

	[TestMethod]
	public void FrameSize_Layer2_CalculatedCorrectly ()
	{
		// Layer 2: frame size = 144 * bitrate / sampleRate + padding
		// 192 kbps, 44100 Hz, no padding
		// Index 10 (0xA0) = 192 kbps for Layer 2
		// = 144 * 192000 / 44100 = 626.9... truncated = 626
		byte[] data = [0xFF, 0xFD, 0xA0, 0x00]; // MPEG1 Layer2 192kbps 44100Hz

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (MpegLayer.Layer2, frame!.Layer);
		Assert.AreEqual (192, frame.Bitrate);
		// 144 * 192 * 1000 / 44100 = 626
		Assert.AreEqual (626, frame.FrameSize);
	}

	[TestMethod]
	public void TryParse_ChannelModeDualChannel_ParsedCorrectly ()
	{
		// Dual channel: bits 24-25 = 10
		byte[] data = [0xFF, 0xFB, 0x90, 0x80];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (ChannelMode.DualChannel, frame!.ChannelMode);
	}

	[TestMethod]
	public void XingHeaderOffset_Mpeg1Stereo_Returns36 ()
	{
		// MPEG1 Stereo: header(4) + side info(32) = 36
		byte[] data = [0xFF, 0xFB, 0x90, 0x00]; // Stereo

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (36, frame!.XingHeaderOffset);
	}

	[TestMethod]
	public void XingHeaderOffset_Mpeg1Mono_Returns21 ()
	{
		// MPEG1 Mono: header(4) + side info(17) = 21
		byte[] data = [0xFF, 0xFB, 0x90, 0xC0]; // Mono

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (21, frame!.XingHeaderOffset);
	}

	[TestMethod]
	public void XingHeaderOffset_Mpeg1StereoWithCrc_Returns38 ()
	{
		// MPEG1 Stereo with CRC: header(4) + CRC(2) + side info(32) = 38
		byte[] data = [0xFF, 0xFA, 0x90, 0x00]; // Stereo with CRC

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.IsTrue (frame!.HasCrc);
		Assert.AreEqual (38, frame.XingHeaderOffset);
	}

	[TestMethod]
	public void XingHeaderOffset_Mpeg2Stereo_Returns21 ()
	{
		// MPEG2 Stereo: header(4) + side info(17) = 21
		byte[] data = [0xFF, 0xF3, 0x80, 0x00]; // MPEG2 Stereo

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (21, frame!.XingHeaderOffset);
	}

	[TestMethod]
	public void XingHeaderOffset_Mpeg2Mono_Returns13 ()
	{
		// MPEG2 Mono: header(4) + side info(9) = 13
		byte[] data = [0xFF, 0xF3, 0x80, 0xC0]; // MPEG2 Mono

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (13, frame!.XingHeaderOffset);
	}

	[TestMethod]
	public void VbriHeaderOffset_IsConstant36 ()
	{
		// VBRI header is always at fixed offset 32 bytes after frame header
		// = header(4) + 32 = 36
		Assert.AreEqual (36, MpegFrame.VbriHeaderOffset);
	}

	[TestMethod]
	public void FrameSize_FreeBitrate_ReturnsZero ()
	{
		// Free bitrate (index 0) - frame size cannot be calculated
		byte[] data = [0xFF, 0xFB, 0x00, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (0, frame!.Bitrate);
		Assert.AreEqual (0, frame.FrameSize);
	}

	[TestMethod]
	[DataRow (0x10, 32)]  // Index 1
	[DataRow (0x20, 40)]  // Index 2
	[DataRow (0x30, 48)]  // Index 3
	[DataRow (0x40, 56)]  // Index 4
	[DataRow (0x50, 64)]  // Index 5
	[DataRow (0x60, 80)]  // Index 6
	[DataRow (0x70, 96)]  // Index 7
	[DataRow (0x80, 112)] // Index 8
	[DataRow (0x90, 128)] // Index 9
	[DataRow (0xA0, 160)] // Index 10
	[DataRow (0xB0, 192)] // Index 11
	[DataRow (0xC0, 224)] // Index 12
	[DataRow (0xD0, 256)] // Index 13
	[DataRow (0xE0, 320)] // Index 14
	public void Bitrate_Mpeg1Layer3_AllValidIndexes (int byte2, int expectedBitrate)
	{
		byte[] data = [0xFF, 0xFB, (byte)byte2, 0x00];

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (expectedBitrate, frame!.Bitrate);
	}

	[TestMethod]
	[DataRow (0x10, 8)]   // Index 1 - MPEG2 L3 bitrates are different
	[DataRow (0x20, 16)]  // Index 2
	[DataRow (0x30, 24)]  // Index 3
	[DataRow (0x40, 32)]  // Index 4
	[DataRow (0x50, 40)]  // Index 5
	[DataRow (0x60, 48)]  // Index 6
	[DataRow (0x70, 56)]  // Index 7
	[DataRow (0x80, 64)]  // Index 8
	[DataRow (0x90, 80)]  // Index 9
	[DataRow (0xA0, 96)]  // Index 10
	[DataRow (0xB0, 112)] // Index 11
	[DataRow (0xC0, 128)] // Index 12
	[DataRow (0xD0, 144)] // Index 13
	[DataRow (0xE0, 160)] // Index 14
	public void Bitrate_Mpeg2Layer3_AllValidIndexes (int byte2, int expectedBitrate)
	{
		byte[] data = [0xFF, 0xF3, (byte)byte2, 0x00]; // MPEG2 Layer3

		MpegFrame.TryParse (new BinaryData (data), 0, out var frame);

		Assert.AreEqual (expectedBitrate, frame!.Bitrate);
	}
}
