// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfStreamPropertiesTests
{
	// ═══════════════════════════════════════════════════════════════
	// Parsing Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_AudioStream_ExtractsSampleRate ()
	{
		var data = CreateAudioStreamData (sampleRate: 44100);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (44100u, result.Value.SampleRate);
	}

	[TestMethod]
	public void Parse_AudioStream_ExtractsChannels ()
	{
		var data = CreateAudioStreamData (channels: 2);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.Value.Channels);
	}

	[TestMethod]
	public void Parse_AudioStream_ExtractsBitsPerSample ()
	{
		var data = CreateAudioStreamData (bitsPerSample: 16);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.Value.BitsPerSample);
	}

	[TestMethod]
	public void Parse_AudioStream_ExtractsCodecId ()
	{
		var data = CreateAudioStreamData (codecId: 0x0161); // WMA Standard

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0x0161, result.Value.CodecId);
	}

	[TestMethod]
	public void Parse_HighSampleRate_ExtractsCorrectly ()
	{
		var data = CreateAudioStreamData (sampleRate: 96000);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (96000u, result.Value.SampleRate);
	}

	[TestMethod]
	public void Parse_MonoAudio_ExtractsCorrectly ()
	{
		var data = CreateAudioStreamData (channels: 1);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.Channels);
	}

	[TestMethod]
	public void Parse_24BitAudio_ExtractsCorrectly ()
	{
		var data = CreateAudioStreamData (bitsPerSample: 24);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (24, result.Value.BitsPerSample);
	}

	[TestMethod]
	public void Parse_WmaPro_CodecDetected ()
	{
		var data = CreateAudioStreamData (codecId: 0x0162); // WMA Pro

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0x0162, result.Value.CodecId);
		Assert.AreEqual ("WMA Pro", result.Value.CodecName);
	}

	[TestMethod]
	public void Parse_WmaLossless_CodecDetected ()
	{
		var data = CreateAudioStreamData (codecId: 0x0163); // WMA Lossless

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0x0163, result.Value.CodecId);
		Assert.AreEqual ("WMA Lossless", result.Value.CodecName);
	}

	[TestMethod]
	public void Parse_IsAudioStream_ReturnsTrue ()
	{
		var data = CreateAudioStreamData ();

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Value.IsAudio);
	}

	[TestMethod]
	public void Parse_TruncatedInput_ReturnsFailure ()
	{
		var data = new byte[10];

		var result = AsfStreamProperties.Parse (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Parse_ExtractsStreamNumber ()
	{
		var data = CreateAudioStreamData (streamNumber: 1);

		var result = AsfStreamProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.StreamNumber);
	}

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	static byte[] CreateAudioStreamData (
		uint sampleRate = 44100,
		ushort channels = 2,
		ushort bitsPerSample = 16,
		ushort codecId = 0x0161,
		ushort streamNumber = 1)
	{
		// Build the content using the test builder's structure
		var waveFormatSize = 18;
		var contentSize = 54 + waveFormatSize;
		var data = new byte[contentSize];
		var offset = 0;

		// Stream Type GUID (Audio)
		var audioGuid = AsfGuids.AudioMediaType.Render ().ToArray ();
		Array.Copy (audioGuid, 0, data, offset, 16);
		offset += 16;

		// Error Correction Type GUID
		var noErrorGuid = AsfGuids.NoErrorCorrection.Render ().ToArray ();
		Array.Copy (noErrorGuid, 0, data, offset, 16);
		offset += 16;

		// Time Offset - 8 bytes
		offset += 8;

		// Type-Specific Data Length
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (offset), (uint)waveFormatSize);
		offset += 4;

		// Error Correction Data Length
		offset += 4;

		// Flags (stream number)
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (offset), streamNumber);
		offset += 2;

		// Reserved
		offset += 4;

		// WAVEFORMATEX structure
		// wFormatTag (codec ID)
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (offset), codecId);
		offset += 2;

		// nChannels
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (offset), channels);
		offset += 2;

		// nSamplesPerSec
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (offset), sampleRate);
		offset += 4;

		// nAvgBytesPerSec
		var bytesPerSec = sampleRate * channels * (uint)(bitsPerSample / 8);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (offset), bytesPerSec);
		offset += 4;

		// nBlockAlign
		var blockAlign = (ushort)(channels * bitsPerSample / 8);
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (offset), blockAlign);
		offset += 2;

		// wBitsPerSample
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (offset), bitsPerSample);
		offset += 2;

		// cbSize (extra bytes)
		BinaryPrimitives.WriteUInt16LittleEndian (data.AsSpan (offset), 0);

		return data;
	}
}
