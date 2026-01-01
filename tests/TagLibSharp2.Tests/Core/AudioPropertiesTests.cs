// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

[TestClass]
public class AudioPropertiesTests
{
	[TestMethod]
	public void FromFlac_ValidInput_CalculatesDurationAndBitrate ()
	{
		// 44100 samples/sec * 180 seconds = 7,938,000 total samples
		var totalSamples = 7_938_000UL;
		var sampleRate = 44100;
		var bitsPerSample = 16;
		var channels = 2;

		var props = AudioProperties.FromFlac (totalSamples, sampleRate, bitsPerSample, channels);

		Assert.AreEqual (TimeSpan.FromSeconds (180), props.Duration);
		Assert.AreEqual (sampleRate, props.SampleRate);
		Assert.AreEqual (bitsPerSample, props.BitsPerSample);
		Assert.AreEqual (channels, props.Channels);
		Assert.AreEqual ("FLAC", props.Codec);
		Assert.IsTrue (props.IsValid);
		// Bitrate for FLAC: (samples * bits * channels) / seconds / 1000
		// = (7938000 * 16 * 2) / 180 / 1000 = 1411 kbps
		Assert.AreEqual (1411, props.Bitrate);
	}

	[TestMethod]
	public void FromFlac_ZeroSampleRate_ReturnsZeroDuration ()
	{
		var props = AudioProperties.FromFlac (1000, 0, 16, 2);

		Assert.AreEqual (TimeSpan.Zero, props.Duration);
		Assert.AreEqual (0, props.Bitrate);
		Assert.IsFalse (props.IsValid);
	}

	[TestMethod]
	public void FromVorbis_ValidInput_CalculatesDuration ()
	{
		// 48000 samples/sec * 240 seconds = 11,520,000 total samples
		var totalSamples = 11_520_000UL;
		var sampleRate = 48000;
		var channels = 2;
		var bitrateNominal = 192000; // 192 kbps in bits

		var props = AudioProperties.FromVorbis (totalSamples, sampleRate, channels, bitrateNominal);

		Assert.AreEqual (TimeSpan.FromSeconds (240), props.Duration);
		Assert.AreEqual (sampleRate, props.SampleRate);
		Assert.AreEqual (0, props.BitsPerSample); // Lossy format
		Assert.AreEqual (channels, props.Channels);
		Assert.AreEqual ("Vorbis", props.Codec);
		Assert.AreEqual (192, props.Bitrate); // Nominal / 1000
		Assert.IsTrue (props.IsValid);
	}

	[TestMethod]
	public void FromVorbis_ZeroBitrate_ReturnsZeroBitrate ()
	{
		var props = AudioProperties.FromVorbis (1_000_000, 44100, 2, 0);

		Assert.AreEqual (0, props.Bitrate);
	}

	[TestMethod]
	public void Empty_ReturnsInvalidProperties ()
	{
		var props = AudioProperties.Empty;

		Assert.AreEqual (TimeSpan.Zero, props.Duration);
		Assert.AreEqual (0, props.Bitrate);
		Assert.AreEqual (0, props.SampleRate);
		Assert.AreEqual (0, props.BitsPerSample);
		Assert.AreEqual (0, props.Channels);
		Assert.IsNull (props.Codec);
		Assert.IsFalse (props.IsValid);
	}

	[TestMethod]
	public void IsValid_RequiresDurationAndSampleRate ()
	{
		// Valid: both duration and sample rate > 0
		var validProps = AudioProperties.FromFlac (44100, 44100, 16, 2);
		Assert.IsTrue (validProps.IsValid);

		// Invalid: zero duration
		var zeroDuration = new AudioProperties (TimeSpan.Zero, 128, 44100, 16, 2, "Test");
		Assert.IsFalse (zeroDuration.IsValid);

		// Invalid: zero sample rate
		var zeroSampleRate = new AudioProperties (TimeSpan.FromSeconds (60), 128, 0, 16, 2, "Test");
		Assert.IsFalse (zeroSampleRate.IsValid);
	}

	[TestMethod]
	public void ToString_FormatsAllProperties ()
	{
		var props = new AudioProperties (
			TimeSpan.FromMinutes (3) + TimeSpan.FromSeconds (30),
			320,
			44100,
			16,
			2,
			"MP3");

		var result = props.ToString ();

		StringAssert.Contains (result, "03:30");
		StringAssert.Contains (result, "320kbps");
		StringAssert.Contains (result, "44100Hz");
		StringAssert.Contains (result, "16bit");
		StringAssert.Contains (result, "Stereo");
		StringAssert.Contains (result, "MP3");
	}

	[TestMethod]
	public void ToString_MonoChannel_ShowsMono ()
	{
		var props = new AudioProperties (TimeSpan.FromSeconds (60), 128, 44100, 16, 1, "Test");

		var result = props.ToString ();

		StringAssert.Contains (result, "Mono");
	}

	[TestMethod]
	public void ToString_MultiChannel_ShowsChannelCount ()
	{
		var props = new AudioProperties (TimeSpan.FromSeconds (60), 128, 48000, 24, 6, "DTS");

		var result = props.ToString ();

		StringAssert.Contains (result, "6ch");
	}

	[TestMethod]
	public void ToString_Empty_ReturnsNoAudioProperties ()
	{
		var result = AudioProperties.Empty.ToString ();

		Assert.AreEqual ("No audio properties", result);
	}

	[TestMethod]
	public void FromFlac_LargeFile_HandlesUlongSamples ()
	{
		// Test with a very large sample count (24-bit, 96kHz, ~2 hours)
		var totalSamples = 691_200_000UL; // 96000 * 7200 seconds (2 hours)
		var sampleRate = 96000;
		var bitsPerSample = 24;
		var channels = 2;

		var props = AudioProperties.FromFlac (totalSamples, sampleRate, bitsPerSample, channels);

		Assert.AreEqual (TimeSpan.FromHours (2), props.Duration);
		Assert.IsTrue (props.IsValid);
		// Bitrate: (691200000 * 24 * 2) / 7200 / 1000 = 4608 kbps
		Assert.AreEqual (4608, props.Bitrate);
	}

	#region DSD Format Tests

	[TestMethod]
	public void FromDsf_DSD64Stereo_CalculatesCorrectBitrate ()
	{
		// DSD64 = 2.8224 MHz sample rate, 1 bit per sample
		// Bitrate = 2822400 * 2 / 1000 = 5644 kbps
		var props = AudioProperties.FromDsf (
			TimeSpan.FromMinutes (5),
			sampleRate: 2822400,
			channels: 2);

		Assert.AreEqual (5644, props.Bitrate);
		Assert.AreEqual (2822400, props.SampleRate);
		Assert.AreEqual (1, props.BitsPerSample);
		Assert.AreEqual (2, props.Channels);
		Assert.AreEqual ("DSD", props.Codec);
	}

	[TestMethod]
	public void FromDsf_DSD1024With6Channels_NoOverflow ()
	{
		// DSD1024 = 45.1584 MHz sample rate
		// With 6 channels: 45158400 * 6 = 270,950,400 (fits in int)
		// Bitrate = 45158400 * 6 / 1000 = 270,950 kbps
		// This tests potential integer overflow in multiplication before division
		var props = AudioProperties.FromDsf (
			TimeSpan.FromMinutes (3),
			sampleRate: 45158400,
			channels: 6);

		// Without overflow protection, 45158400 * 6 = 270,950,400 which fits
		// But if we had 8 channels: 45158400 * 8 = 361,267,200 (still fits)
		// The concern is order of operations. Let's verify correct result.
		Assert.AreEqual (270950, props.Bitrate);
		Assert.AreEqual (45158400, props.SampleRate);
		Assert.AreEqual (6, props.Channels);
		Assert.IsTrue (props.IsValid);
	}

	[TestMethod]
	public void FromDsf_ZeroSampleRate_ReturnsZeroBitrate ()
	{
		var props = AudioProperties.FromDsf (
			TimeSpan.FromMinutes (5),
			sampleRate: 0,
			channels: 2);

		Assert.AreEqual (0, props.Bitrate);
	}

	[TestMethod]
	public void FromDff_DSD128_CalculatesCorrectBitrate ()
	{
		// DSD128 = 5.6448 MHz sample rate
		// Bitrate = 5644800 * 2 / 1000 = 11289 kbps
		var props = AudioProperties.FromDff (
			TimeSpan.FromMinutes (4),
			sampleRate: 5644800,
			channels: 2,
			isDst: false);

		Assert.AreEqual (11289, props.Bitrate);
		Assert.AreEqual ("DSD", props.Codec);
	}

	[TestMethod]
	public void FromDff_WithDstCompression_UsesDstCodec ()
	{
		var props = AudioProperties.FromDff (
			TimeSpan.FromMinutes (4),
			sampleRate: 2822400,
			channels: 2,
			isDst: true);

		Assert.AreEqual ("DST", props.Codec);
	}

	[TestMethod]
	public void FromDff_DSD1024With6Channels_NoOverflow ()
	{
		// Same test as DSF - verify no overflow in bitrate calculation
		var props = AudioProperties.FromDff (
			TimeSpan.FromMinutes (3),
			sampleRate: 45158400,
			channels: 6,
			isDst: false);

		Assert.AreEqual (270950, props.Bitrate);
		Assert.IsTrue (props.IsValid);
	}

	#endregion
}
