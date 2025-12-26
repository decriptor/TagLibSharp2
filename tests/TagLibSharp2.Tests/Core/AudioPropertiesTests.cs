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
}
