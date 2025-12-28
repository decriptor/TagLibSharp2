// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public class AiffAudioPropertiesTests
{
	[TestMethod]
	public void TryParse_ValidCommChunk_ReturnsTrue ()
	{
		var commData = CreateCommChunkData (2, 44100, 16, 44100.0);

		var result = AiffAudioProperties.TryParse (commData, out var props);

		Assert.IsTrue (result);
		Assert.IsNotNull (props);
	}

	[TestMethod]
	public void TryParse_TooShort_ReturnsFalse ()
	{
		var data = new BinaryData ([0x00, 0x02, 0x00, 0x00]); // Only 4 bytes

		var result = AiffAudioProperties.TryParse (data, out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void Channels_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 1000, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (2, props!.Channels);
	}

	[TestMethod]
	public void Channels_Mono_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 1, sampleFrames: 1000, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (1, props!.Channels);
	}

	[TestMethod]
	public void SampleFrames_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 123456, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (123456u, props!.SampleFrames);
	}

	[TestMethod]
	public void BitsPerSample_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 1000, bitsPerSample: 24, sampleRate: 48000.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (24, props!.BitsPerSample);
	}

	[TestMethod]
	public void SampleRate_44100_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 1000, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (44100, props!.SampleRate);
	}

	[TestMethod]
	public void SampleRate_48000_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 1000, bitsPerSample: 16, sampleRate: 48000.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (48000, props!.SampleRate);
	}

	[TestMethod]
	public void SampleRate_96000_ParsedCorrectly ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 1000, bitsPerSample: 24, sampleRate: 96000.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (96000, props!.SampleRate);
	}

	[TestMethod]
	public void Duration_OneSecond_CalculatedCorrectly ()
	{
		// 44100 frames at 44100 Hz = 1 second
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 44100, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (TimeSpan.FromSeconds (1), props!.Duration);
	}

	[TestMethod]
	public void Duration_TwoMinutes_CalculatedCorrectly ()
	{
		// 2 minutes = 120 seconds = 120 * 48000 = 5,760,000 frames
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 5760000, bitsPerSample: 24, sampleRate: 48000.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (TimeSpan.FromMinutes (2), props!.Duration);
	}

	[TestMethod]
	public void Duration_ZeroFrames_ReturnsZero ()
	{
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 0, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (TimeSpan.Zero, props!.Duration);
	}

	[TestMethod]
	public void Bitrate_CdQuality_CalculatedCorrectly ()
	{
		// CD quality: 44100 Hz * 16 bit * 2 channels = 1,411,200 bps = 1411 kbps
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 44100, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (1411, props!.Bitrate);
	}

	[TestMethod]
	public void Bitrate_HighRes_CalculatedCorrectly ()
	{
		// High-res: 96000 Hz * 24 bit * 2 channels = 4,608,000 bps = 4608 kbps
		var commData = CreateCommChunkData (channels: 2, sampleFrames: 96000, bitsPerSample: 24, sampleRate: 96000.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (4608, props!.Bitrate);
	}

	[TestMethod]
	public void Bitrate_Mono_CalculatedCorrectly ()
	{
		// Mono 44100 Hz * 16 bit * 1 channel = 705,600 bps = 705 kbps
		var commData = CreateCommChunkData (channels: 1, sampleFrames: 44100, bitsPerSample: 16, sampleRate: 44100.0);

		AiffAudioProperties.TryParse (commData, out var props);

		Assert.AreEqual (705, props!.Bitrate);
	}

	// Helper to create COMM chunk data (without the chunk header)
	static BinaryData CreateCommChunkData (int channels, uint sampleFrames, int bitsPerSample, double sampleRate)
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt16BE ((ushort)channels);
		builder.AddUInt32BE (sampleFrames);
		builder.AddUInt16BE ((ushort)bitsPerSample);
		builder.Add (ExtendedFloat.FromDouble (sampleRate));

		return builder.ToBinaryData ();
	}
}
