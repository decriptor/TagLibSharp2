// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Tests for MP4 audio properties extraction.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4AudioPropertiesTests
{
	[TestMethod]
	public void ExtractDuration_FromMvhd_ReturnsCorrectValue ()
	{
		var duration = TimeSpan.FromMinutes (3);
		var data = TestBuilders.Mp4.CreateWithDuration (duration);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Properties);
		Assert.IsTrue (Math.Abs ((result.File.Properties.Duration - duration).TotalSeconds) < 1);
	}

	[TestMethod]
	public void ExtractSampleRate_FromEsdsAac_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 128);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (44100, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void ExtractSampleRate_FromAlacCookie_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Alac,
			sampleRate: 48000,
			channels: 2,
			bitrate: 0);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (48000, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void ExtractChannelCount_Stereo_ReturnsTwo ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 128);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void ExtractChannelCount_Mono_ReturnsOne ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 1,
			bitrate: 64);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void ExtractChannelCount_FivePointOne_ReturnsSix ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 48000,
			channels: 6,
			bitrate: 320);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (6, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void ExtractBitrate_Aac128_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 128);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (128, result.File!.Properties.Bitrate);
	}

	[TestMethod]
	public void ExtractBitrate_Aac256_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 256);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (256, result.File!.Properties.Bitrate);
	}

	[TestMethod]
	public void DetectCodec_Aac_ReturnsCorrectString ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 128);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("AAC", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void DetectCodec_Alac_ReturnsCorrectString ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Alac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 0);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("ALAC", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Duration_ZeroTimescale_ReturnsZero ()
	{
		var data = TestBuilders.Mp4.CreateWithInvalidTimescale ();

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.Zero, result.File!.Properties.Duration);
	}

	[TestMethod]
	public void SampleRate_HighRes96kHz_ParsesCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Alac,
			sampleRate: 96000,
			channels: 2,
			bitrate: 0);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (96000, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void SampleRate_HighRes192kHz_ParsesCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Alac,
			sampleRate: 192000,
			channels: 2,
			bitrate: 0);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (192000, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void Properties_MissingMdia_ReturnsDefaultValues ()
	{
		var data = TestBuilders.Mp4.CreateWithoutMdia ();

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.Properties.SampleRate);
		Assert.AreEqual (0, result.File.Properties.Channels);
	}

	[TestMethod]
	public void BitsPerSample_Alac16_Returns16 ()
	{
		var data = TestBuilders.Mp4.CreateWithBitsPerSample (Mp4CodecType.Alac, 16);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	public void BitsPerSample_Alac24_Returns24 ()
	{
		var data = TestBuilders.Mp4.CreateWithBitsPerSample (Mp4CodecType.Alac, 24);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (24, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	public void BitsPerSample_Aac_ReturnsZero ()
	{
		// AAC is lossy, bits per sample doesn't apply
		var data = TestBuilders.Mp4.CreateWithAudioProperties (
			codec: Mp4CodecType.Aac,
			sampleRate: 44100,
			channels: 2,
			bitrate: 128);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	public void Duration_LongFile_CalculatesCorrectly ()
	{
		var duration = TimeSpan.FromHours (2); // 2 hour file
		var data = TestBuilders.Mp4.CreateWithDuration (duration);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (Math.Abs ((result.File!.Properties.Duration - duration).TotalSeconds) < 1);
	}

	[TestMethod]
	public void Properties_MultipleStsd_UsesFirst ()
	{
		var data = TestBuilders.Mp4.CreateWithMultipleStsdEntries ();

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// Should use properties from first stsd entry
		Assert.IsGreaterThan (0, result.File!.Properties.SampleRate);
	}
}
