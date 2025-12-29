// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class WavAudioPropertiesTests
{
	static BinaryData CreateFmtChunk (
		ushort formatCode = 1,
		ushort channels = 2,
		uint sampleRate = 44100,
		ushort bitsPerSample = 16)
	{
		var blockAlign = (ushort)(channels * bitsPerSample / 8);
		var byteRate = sampleRate * blockAlign;

		using var builder = new BinaryDataBuilder (16);
		builder.AddUInt16LE (formatCode);
		builder.AddUInt16LE (channels);
		builder.AddUInt32LE (sampleRate);
		builder.AddUInt32LE (byteRate);
		builder.AddUInt16LE (blockAlign);
		builder.AddUInt16LE (bitsPerSample);

		return builder.ToBinaryData ();
	}

	[TestMethod]
	public void Parse_ValidPcmFmt_ReturnsProperties ()
	{
		var props = WavAudioPropertiesParser.Parse (CreateFmtChunk ());

		Assert.IsNotNull (props);
		Assert.AreEqual (44100, props.SampleRate);
		Assert.AreEqual (2, props.Channels);
		Assert.AreEqual (16, props.BitsPerSample);
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsNull ()
	{
		var data = new BinaryData ([1, 2, 3, 4, 5]);
		Assert.IsNull (WavAudioPropertiesParser.Parse (data));
	}

	[TestMethod]
	public void Parse_ZeroChannels_ReturnsNull ()
	{
		var data = CreateFmtChunk (channels: 0);
		Assert.IsNull (WavAudioPropertiesParser.Parse (data));
	}

	[TestMethod]
	public void Parse_WithDataSize_CalculatesDuration ()
	{
		var fmtData = CreateFmtChunk ();
		var props = WavAudioPropertiesParser.Parse (fmtData, 176400); // 1 second

		Assert.IsNotNull (props);
		Assert.AreEqual (1.0, props.Duration.TotalSeconds, 0.001);
	}

	[TestMethod]
	public void Parse_CalculatesBitrate ()
	{
		var props = WavAudioPropertiesParser.Parse (CreateFmtChunk ());

		Assert.IsNotNull (props);
		Assert.AreEqual (1411, props.Bitrate);
	}

	[TestMethod]
	public void Parse_MonoAudio_SingleChannel ()
	{
		var props = WavAudioPropertiesParser.Parse (CreateFmtChunk (channels: 1));

		Assert.IsNotNull (props);
		Assert.AreEqual (1, props.Channels);
	}

	[TestMethod]
	public void GetFormatDescription_Pcm_ReturnsPcm ()
	{
		Assert.AreEqual ("PCM", WavAudioPropertiesParser.GetFormatDescription (1));
	}

	[TestMethod]
	public void GetFormatDescription_IeeeFloat_ReturnsIeeeFloat ()
	{
		Assert.AreEqual ("IEEE Float", WavAudioPropertiesParser.GetFormatDescription (3));
	}

	[TestMethod]
	public void GetFormatDescription_Unknown_ReturnsWavWithCode ()
	{
		var desc = WavAudioPropertiesParser.GetFormatDescription (99);
		Assert.IsTrue (desc.StartsWith ("WAV"));
	}

	[TestMethod]
	public void Parse_IeeeFloat_ReturnsCorrectCodec ()
	{
		var props = WavAudioPropertiesParser.Parse (CreateFmtChunk (formatCode: 3));

		Assert.IsNotNull (props);
		Assert.AreEqual ("IEEE Float", props.Codec);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void Parse_FormatExtensible_ReturnsExtensibleCodec ()
	{
		var props = WavAudioPropertiesParser.Parse (CreateFmtChunk (formatCode: 0xFFFE));

		Assert.IsNotNull (props);
		Assert.AreEqual ("Extensible", props.Codec);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ParseExtended_ValidExtensible_ReturnsProperties ()
	{
		byte[] fmtData = [
			0xFE, 0xFF, // Format = Extensible (0xFFFE)
			0x06, 0x00, // Channels = 6 (5.1 surround)
			0x80, 0xBB, 0x00, 0x00, // SampleRate = 48000
			0x00, 0xDC, 0x05, 0x00, // ByteRate = 384000
			0x0C, 0x00, // BlockAlign = 12
			0x10, 0x00, // BitsPerSample = 16
			0x16, 0x00, // cbSize = 22
			0x10, 0x00, // wValidBitsPerSample = 16
			0x3F, 0x00, 0x00, 0x00, // dwChannelMask = 5.1 (FL|FR|FC|LFE|BL|BR)
			// SubFormat GUID for PCM
			0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
			0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71
		];

		var result = WavAudioPropertiesParser.ParseExtended (new BinaryData (fmtData));

		Assert.IsTrue (result.HasValue);
		Assert.AreEqual (6, result.Value.Channels);
		Assert.AreEqual (16, result.Value.ValidBitsPerSample);
		Assert.AreEqual (0x3Fu, result.Value.ChannelMask);
		Assert.AreEqual (WavSubFormat.Pcm, result.Value.SubFormat);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ParseExtended_IeeeFloatSubFormat_ReturnsCorrectSubFormat ()
	{
		byte[] fmtData = [
			0xFE, 0xFF, // Format = Extensible
			0x02, 0x00, // Channels = 2
			0x80, 0xBB, 0x00, 0x00, // SampleRate = 48000
			0x00, 0xEE, 0x02, 0x00, // ByteRate = 192000
			0x08, 0x00, // BlockAlign = 8
			0x20, 0x00, // BitsPerSample = 32
			0x16, 0x00, // cbSize = 22
			0x20, 0x00, // wValidBitsPerSample = 32
			0x03, 0x00, 0x00, 0x00, // dwChannelMask = FRONT_LEFT | FRONT_RIGHT
			// SubFormat GUID for IEEE Float
			0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
			0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71
		];

		var result = WavAudioPropertiesParser.ParseExtended (new BinaryData (fmtData));

		Assert.IsTrue (result.HasValue);
		Assert.AreEqual (WavSubFormat.IeeeFloat, result.Value.SubFormat);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ParseExtended_NotExtensible_ReturnsNull ()
	{
		// Standard PCM format, not extensible
		var data = CreateFmtChunk ();

		var result = WavAudioPropertiesParser.ParseExtended (data);

		Assert.IsFalse (result.HasValue);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ParseExtended_TooShort_ReturnsNull ()
	{
		// Only 20 bytes, missing SubFormat GUID
		byte[] fmtData = [
			0xFE, 0xFF, // Format = Extensible
			0x02, 0x00, // Channels = 2
			0x80, 0xBB, 0x00, 0x00, // SampleRate = 48000
			0x00, 0x77, 0x01, 0x00, // ByteRate = 96000
			0x02, 0x00, // BlockAlign = 2
			0x10, 0x00, // BitsPerSample = 16
			0x16, 0x00, // cbSize = 22
			0x10, 0x00  // wValidBitsPerSample = 16
			// Missing dwChannelMask and SubFormat
		];

		var result = WavAudioPropertiesParser.ParseExtended (new BinaryData (fmtData));

		Assert.IsFalse (result.HasValue);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ChannelMask_5_1_Surround_CorrectValue ()
	{
		// 5.1 surround = FL(1) + FR(2) + FC(4) + LFE(8) + BL(16) + BR(32) = 0x3F
		uint mask = WavChannelMask.FrontLeft | WavChannelMask.FrontRight |
			WavChannelMask.FrontCenter | WavChannelMask.LowFrequency |
			WavChannelMask.BackLeft | WavChannelMask.BackRight;

		Assert.AreEqual (0x3Fu, mask);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ChannelMask_7_1_Surround_CorrectValue ()
	{
		// 7.1 surround = 5.1 + SL + SR
		uint mask = WavChannelMask.FrontLeft | WavChannelMask.FrontRight |
			WavChannelMask.FrontCenter | WavChannelMask.LowFrequency |
			WavChannelMask.BackLeft | WavChannelMask.BackRight |
			WavChannelMask.SideLeft | WavChannelMask.SideRight;

		Assert.AreEqual (0x63Fu, mask);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ParseExtended_UnknownSubFormat_ReturnsUnknown ()
	{
		byte[] fmtData = [
			0xFE, 0xFF, // Format = Extensible
			0x02, 0x00, // Channels = 2
			0x80, 0xBB, 0x00, 0x00, // SampleRate = 48000
			0x00, 0x77, 0x01, 0x00, // ByteRate = 96000
			0x02, 0x00, // BlockAlign = 2
			0x10, 0x00, // BitsPerSample = 16
			0x16, 0x00, // cbSize = 22
			0x10, 0x00, // wValidBitsPerSample = 16
			0x03, 0x00, 0x00, 0x00, // dwChannelMask
			// Unknown SubFormat GUID
			0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
			0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71
		];

		var result = WavAudioPropertiesParser.ParseExtended (new BinaryData (fmtData));

		Assert.IsTrue (result.HasValue);
		Assert.AreEqual (WavSubFormat.Unknown, result.Value.SubFormat);
	}
}
