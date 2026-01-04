// DSF (DSD Stream File) format tests
// Following TDD - tests written first, implementation to follow
// Based on DSF specification for DSD audio files
//
// Expert input:
// - DSF Expert: Focus on little-endian parsing, chunk structure (DSD/fmt/data/metadata)
// - Audiophile: DSD64/128/256 sample rates (2.8/5.6/11.2 MHz), preserve full resolution
// - SDET: Edge cases for malformed files, boundary conditions, security tests
// - C# Expert: Use Span<T>, avoid allocations in hot paths

using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Core;
using TagLibSharp2.Dsf;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Dsf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Dsf")]
public class DsfFileTests
{
	#region DSD Chunk Header Tests

	[TestMethod]
	public void ParseDsdChunk_ValidData_ParsesCorrectly ()
	{
		// Arrange - DSD chunk: "DSD " + chunk_size(8) + file_size(8) + metadata_offset(8)
		var data = CreateDsdChunk (
			chunkSize: 28,
			fileSize: 1000,
			metadataOffset: 800);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (28UL, result.Chunk!.ChunkSize);
		Assert.AreEqual (1000UL, result.Chunk.FileSize);
		Assert.AreEqual (800UL, result.Chunk.MetadataOffset);
	}

	[TestMethod]
	public void ParseDsdChunk_InvalidMagic_ReturnsFailure ()
	{
		// Arrange - wrong magic bytes
		var data = new byte[28];
		data[0] = (byte)'X';
		data[1] = (byte)'X';
		data[2] = (byte)'X';
		data[3] = (byte)' ';

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("magic"));
	}

	[TestMethod]
	public void ParseDsdChunk_TooShort_ReturnsFailure ()
	{
		// Arrange - less than 28 bytes
		var data = new byte[20];
		data[0] = (byte)'D';
		data[1] = (byte)'S';
		data[2] = (byte)'D';
		data[3] = (byte)' ';

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("short") || result.Error.Contains ("size"));
	}

	[TestMethod]
	public void ParseDsdChunk_ZeroMetadataOffset_MeansNoMetadata ()
	{
		// Arrange - metadata offset of 0 means no ID3v2 tag
		var data = CreateDsdChunk (
			chunkSize: 28,
			fileSize: 500,
			metadataOffset: 0);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0UL, result.Chunk!.MetadataOffset);
		Assert.IsFalse (result.Chunk.HasMetadata);
	}

	#endregion

	#region Format Chunk Tests

	[TestMethod]
	public void ParseFmtChunk_Dsd64Stereo_ParsesCorrectly ()
	{
		// Arrange - DSD64 stereo (2.8224 MHz sample rate)
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0, // DSD raw
			channelType: 2, // Stereo
			channelCount: 2,
			sampleRate: 2822400, // DSD64
			bitsPerSample: 1,
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1u, result.Chunk!.FormatVersion);
		Assert.AreEqual (2u, result.Chunk.ChannelCount);
		Assert.AreEqual (2822400u, result.Chunk.SampleRate);
		Assert.AreEqual (1u, result.Chunk.BitsPerSample);
		Assert.AreEqual (DsfSampleRate.DSD64, result.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_Dsd128_ParsesCorrectly ()
	{
		// Arrange - DSD128 (5.6448 MHz)
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 5644800, // DSD128
			bitsPerSample: 1,
			sampleCount: 2000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5644800u, result.Chunk!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD128, result.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_Dsd256_ParsesCorrectly ()
	{
		// Arrange - DSD256 (11.2896 MHz)
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 11289600, // DSD256
			bitsPerSample: 1,
			sampleCount: 4000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (11289600u, result.Chunk!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD256, result.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_Dsd512_ParsesCorrectly ()
	{
		// Arrange - DSD512 (22.5792 MHz) - highest standard rate
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 22579200, // DSD512
			bitsPerSample: 1,
			sampleCount: 8000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (22579200u, result.Chunk!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD512, result.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_MultiChannel51_ParsesCorrectly ()
	{
		// Arrange - 5.1 surround
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 6, // 5.1
			channelCount: 6,
			sampleRate: 2822400,
			bitsPerSample: 1,
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (6u, result.Chunk!.ChannelCount);
		Assert.AreEqual (DsfChannelType.Surround51, result.Chunk.ChannelType);
	}

	[TestMethod]
	public void ParseFmtChunk_InvalidMagic_ReturnsFailure ()
	{
		// Arrange
		var data = new byte[52];
		data[0] = (byte)'X';

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ParseFmtChunk_TooShort_ReturnsFailure ()
	{
		// Arrange
		var data = new byte[20];
		data[0] = (byte)'f';
		data[1] = (byte)'m';
		data[2] = (byte)'t';
		data[3] = (byte)' ';

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ParseFmtChunk_CalculatesDuration_Correctly ()
	{
		// Arrange - 2.8224 MHz, 2,822,400 samples = 1 second
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 2822400,
			bitsPerSample: 1,
			sampleCount: 2822400 * 60, // 60 seconds
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.FromSeconds (60), result.Chunk!.Duration);
	}

	[TestMethod]
	public void ParseFmtChunk_Dsd1024_ParsesCorrectly ()
	{
		// Arrange - DSD1024 (45.1584 MHz) - ultra-high sample rate
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 45158400, // DSD1024
			bitsPerSample: 1,
			sampleCount: 16000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (45158400u, result.Chunk!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD1024, result.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_UnknownSampleRate_ReturnsUnknownRate ()
	{
		// Arrange - non-standard sample rate
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 3000000, // Non-standard rate
			bitsPerSample: 1,
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3000000u, result.Chunk!.SampleRate);
		Assert.AreEqual (DsfSampleRate.Unknown, result.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_ZeroSampleRate_DurationIsZero ()
	{
		// Arrange - edge case: zero sample rate
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 0, // Invalid but handled
			bitsPerSample: 1,
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.Zero, result.Chunk!.Duration);
	}

	[TestMethod]
	public void ParseFmtChunk_Mono_ParsesCorrectly ()
	{
		// Arrange - mono channel
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 1,
			channelCount: 1,
			sampleRate: 2822400,
			bitsPerSample: 1,
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1u, result.Chunk!.ChannelCount);
		Assert.AreEqual (DsfChannelType.Mono, result.Chunk.ChannelType);
	}

	[TestMethod]
	public void FmtChunk_Create_Mono_SetsChannelTypeCorrectly ()
	{
		// Act
		var chunk = DsfFmtChunk.Create (
			channelCount: 1,
			sampleRate: 2822400,
			sampleCount: 1000000);

		// Assert
		Assert.AreEqual (DsfChannelType.Mono, chunk.ChannelType);
		Assert.AreEqual (1u, chunk.ChannelCount);
	}

	[TestMethod]
	public void FmtChunk_Create_Stereo_SetsChannelTypeCorrectly ()
	{
		// Act
		var chunk = DsfFmtChunk.Create (
			channelCount: 2,
			sampleRate: 2822400,
			sampleCount: 1000000);

		// Assert
		Assert.AreEqual (DsfChannelType.Stereo, chunk.ChannelType);
		Assert.AreEqual (2u, chunk.ChannelCount);
	}

	[TestMethod]
	public void FmtChunk_Create_Surround51_SetsChannelTypeCorrectly ()
	{
		// Act
		var chunk = DsfFmtChunk.Create (
			channelCount: 6,
			sampleRate: 2822400,
			sampleCount: 1000000);

		// Assert
		Assert.AreEqual (DsfChannelType.Surround51, chunk.ChannelType);
		Assert.AreEqual (6u, chunk.ChannelCount);
	}

	[TestMethod]
	public void FmtChunk_Create_CustomChannelCount_UsesChannelCountAsType ()
	{
		// Act - 4 channels (not a standard type)
		var chunk = DsfFmtChunk.Create (
			channelCount: 4,
			sampleRate: 2822400,
			sampleCount: 1000000);

		// Assert - falls through to default case
		Assert.AreEqual ((DsfChannelType)4, chunk.ChannelType);
		Assert.AreEqual (4u, chunk.ChannelCount);
	}

	[TestMethod]
	public void FmtChunk_RenderAndParse_RoundTrips ()
	{
		// Arrange
		var original = DsfFmtChunk.Create (
			channelCount: 2,
			sampleRate: 5644800, // DSD128
			sampleCount: 10000000);

		// Act
		var rendered = original.Render ();
		var reparsed = DsfFmtChunk.Parse (rendered);

		// Assert
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (original.SampleRate, reparsed.Chunk!.SampleRate);
		Assert.AreEqual (original.ChannelCount, reparsed.Chunk.ChannelCount);
		Assert.AreEqual (original.SampleCount, reparsed.Chunk.SampleCount);
		Assert.AreEqual (original.DsdRate, reparsed.Chunk.DsdRate);
	}

	[TestMethod]
	public void ParseFmtChunk_NonDsdBitsPerSample_ReturnsFailure ()
	{
		// Arrange - DSD requires 1 bit per sample, other values are invalid
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 2822400,
			bitsPerSample: 16, // Invalid for DSD
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("bit"));
	}

	[TestMethod]
	public void ParseFmtChunk_NonZeroFormatId_ReturnsFailure ()
	{
		// Arrange - only format ID 0 (DSD raw) is supported
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 1, // Unknown format
			channelType: 2,
			channelCount: 2,
			sampleRate: 2822400,
			bitsPerSample: 1,
			sampleCount: 1000000,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("format"));
	}

	#endregion

	#region Data Chunk Tests

	[TestMethod]
	public void ParseDataChunk_ValidHeader_ParsesCorrectly ()
	{
		// Arrange - "data" + chunk_size(8) + data
		var data = CreateDataChunkHeader (chunkSize: 1000);

		// Act
		var result = DsfDataChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1000UL, result.Chunk!.ChunkSize);
		Assert.AreEqual (12, DsfDataChunk.HeaderSize);
	}

	[TestMethod]
	public void ParseDataChunk_InvalidMagic_ReturnsFailure ()
	{
		// Arrange
		var data = new byte[12];
		data[0] = (byte)'X';

		// Act
		var result = DsfDataChunk.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void DsfDataChunk_Create_SetsProperties ()
	{
		var chunk = DsfDataChunk.Create (1000);
		Assert.AreEqual (1000UL, chunk.ChunkSize);
		Assert.AreEqual ((ulong)(1000 - DsfDataChunk.HeaderSize), chunk.AudioDataSize);
	}

	[TestMethod]
	public void DsfDataChunk_AudioDataSize_ChunkSizeLessThanHeader_ReturnsZero ()
	{
		var chunk = DsfDataChunk.Create (10); // Less than HeaderSize (12)
		Assert.AreEqual (0UL, chunk.AudioDataSize);
	}

	[TestMethod]
	public void DsfDataChunk_AudioDataSize_ExactlyHeaderSize_ReturnsZero ()
	{
		var chunk = DsfDataChunk.Create (DsfDataChunk.HeaderSize);
		Assert.AreEqual (0UL, chunk.AudioDataSize);
	}

	[TestMethod]
	public void DsfDataChunk_HeaderSizeValue_Returns12 ()
	{
		Assert.AreEqual (12, DsfDataChunk.HeaderSizeValue);
	}

	[TestMethod]
	public void DsfDataChunk_RenderHeader_RoundTrips ()
	{
		var original = DsfDataChunk.Create (5000);
		var rendered = original.RenderHeader ();
		var reparsed = DsfDataChunk.Parse (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (original.ChunkSize, reparsed.Chunk!.ChunkSize);
	}

	[TestMethod]
	public void DsfDataChunk_Parse_TooShort_Fails ()
	{
		var data = new byte[8]; // Less than HeaderSize (12)
		data[0] = (byte)'d';
		data[1] = (byte)'a';
		data[2] = (byte)'t';
		data[3] = (byte)'a';

		var result = DsfDataChunk.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("short") || result.Error.Contains ("size"));
	}

	[TestMethod]
	public void DsfDataChunkParseResult_OperatorEquals_Works ()
	{
		var failure1 = DsfDataChunkParseResult.Failure ("Error A");
		var failure2 = DsfDataChunkParseResult.Failure ("Error A");

		Assert.IsTrue (failure1 == failure2);
	}

	[TestMethod]
	public void DsfDataChunkParseResult_OperatorNotEquals_Works ()
	{
		var failure1 = DsfDataChunkParseResult.Failure ("Error A");
		var failure2 = DsfDataChunkParseResult.Failure ("Error B");

		Assert.IsTrue (failure1 != failure2);
	}

	#endregion

	#region Full DSF File Parsing Tests

	[TestMethod]
	public void Parse_ValidDsfWithoutMetadata_ParsesCorrectly ()
	{
		// Arrange - complete DSF file without ID3v2
		var data = CreateMinimalDsfFile (
			sampleRate: 2822400,
			channelCount: 2,
			sampleCount: 2822400, // 1 second
			hasMetadata: false);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2822400, result.File!.SampleRate);
		Assert.AreEqual (2, result.File.Channels);
		Assert.IsNull (result.File.Id3v2Tag);
	}

	[TestMethod]
	public void Parse_ValidDsfWithId3v2_ParsesMetadata ()
	{
		// Arrange - DSF with ID3v2 tag
		var data = CreateDsfWithId3v2 (
			sampleRate: 2822400,
			channelCount: 2,
			title: "Test Song",
			artist: "Test Artist");

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Id3v2Tag);
		Assert.AreEqual ("Test Song", result.File.Id3v2Tag.Title);
		Assert.AreEqual ("Test Artist", result.File.Id3v2Tag.Artist);
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsFailure ()
	{
		// Arrange - too short for even DSD chunk
		var data = new byte[10];

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Parse_CalculatesAudioProperties ()
	{
		// Arrange - DSD64 stereo, 1 minute
		var data = CreateMinimalDsfFile (
			sampleRate: 2822400,
			channelCount: 2,
			sampleCount: 2822400 * 60,
			hasMetadata: false);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.FromMinutes (1), result.File!.Duration);
		Assert.AreEqual (2822400, result.File.SampleRate);
		Assert.AreEqual (2, result.File.Channels);
		Assert.AreEqual (1, result.File.BitsPerSample);
	}

	#endregion

	#region Round-Trip Tests

	[TestMethod]
	public void RoundTrip_ModifyAndSave_PreservesAudioData ()
	{
		// Arrange - create DSF with metadata
		var original = CreateDsfWithId3v2 (
			sampleRate: 2822400,
			channelCount: 2,
			title: "Original Title",
			artist: "Original Artist");

		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		// Act - modify metadata
		file.Id3v2Tag!.Title = "Modified Title";
		file.Id3v2Tag.Artist = "Modified Artist";
		var rendered = file.Render ();

		// Assert - can parse back
		var reparsed = DsfFile.Read (rendered.Span);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Modified Title", reparsed.File!.Id3v2Tag!.Title);
		Assert.AreEqual ("Modified Artist", reparsed.File.Id3v2Tag.Artist);
	}

	[TestMethod]
	public void RoundTrip_NoMetadata_ToWithMetadata ()
	{
		// Arrange - DSF without metadata
		var original = CreateMinimalDsfFile (
			sampleRate: 2822400,
			channelCount: 2,
			sampleCount: 2822400,
			hasMetadata: false);

		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;
		Assert.IsNull (file.Id3v2Tag);

		// Act - create tag
		file.EnsureId3v2Tag ();
		file.Id3v2Tag!.Title = "New Title";
		var rendered = file.Render ();

		// Assert
		var reparsed = DsfFile.Read (rendered.Span);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.IsNotNull (reparsed.File!.Id3v2Tag);
		Assert.AreEqual ("New Title", reparsed.File.Id3v2Tag.Title);
	}

	[TestMethod]
	public void RoundTrip_ModifyMetadata_PreservesAudioBytesExactly ()
	{
		// Arrange - create DSF with known audio pattern
		var dsd = CreateDsdChunk (28, 0, 0);
		var fmt = CreateFmtChunk (1, 0, 2, 2, 2822400, 1, 1000, 4096);
		var dataHeader = CreateDataChunkHeader (100);

		// Create recognizable audio pattern: 0xAA, 0xBB, 0xCC repeating
		var audioData = new byte[88];
		for (int i = 0; i < audioData.Length; i++) {
			audioData[i] = (byte)(0xAA + (i % 3) * 0x11);
		}

		var baseSize = dsd.Length + fmt.Length + dataHeader.Length + audioData.Length;
		var baseFile = new byte[baseSize];
		var offset = 0;
		dsd.CopyTo (baseFile, offset); offset += dsd.Length;
		fmt.CopyTo (baseFile, offset); offset += fmt.Length;
		dataHeader.CopyTo (baseFile, offset); offset += dataHeader.Length;
		audioData.CopyTo (baseFile, offset);

		// Update file size
		BinaryPrimitives.WriteUInt64LittleEndian (baseFile.AsSpan (12), (ulong)baseSize);

		// Add ID3v2 tag
		var id3 = CreateSimpleId3v2Tag ("Original", "Artist");
		var fullFile = new byte[baseSize + id3.Length];
		baseFile.CopyTo (fullFile, 0);
		id3.CopyTo (fullFile, baseSize);

		// Update metadata offset and file size in DSD chunk
		BinaryPrimitives.WriteUInt64LittleEndian (fullFile.AsSpan (12), (ulong)fullFile.Length);
		BinaryPrimitives.WriteUInt64LittleEndian (fullFile.AsSpan (20), (ulong)baseSize);

		// Parse and modify
		var parseResult = DsfFile.Read (fullFile);
		Assert.IsTrue (parseResult.IsSuccess, $"Parse failed: {parseResult.Error}");
		var file = parseResult.File!;

		// Act - modify metadata
		file.Id3v2Tag!.Title = "New Title With More Characters";
		file.Id3v2Tag.Album = "Added Album Field";
		var rendered = file.Render ();

		// Assert - audio data should be byte-for-byte identical
		var audioOffset = dsd.Length + fmt.Length + dataHeader.Length;
		var originalAudio = fullFile.AsSpan (audioOffset, audioData.Length);
		var renderedAudio = rendered.Span.Slice (audioOffset, audioData.Length);

		Assert.IsTrue (originalAudio.SequenceEqual (renderedAudio),
			"Audio data was not preserved byte-for-byte during metadata modification");

		// Also verify audio pattern is still correct
		for (int i = 0; i < audioData.Length; i++) {
			var expected = (byte)(0xAA + (i % 3) * 0x11);
			Assert.AreEqual (expected, renderedAudio[i],
				$"Audio byte at offset {i} was modified: expected 0x{expected:X2}, got 0x{renderedAudio[i]:X2}");
		}
	}

	#endregion

	#region Security and Edge Case Tests

	[TestMethod]
	public void Parse_OverflowingSampleCount_HandlesGracefully ()
	{
		// Arrange - sample count that would overflow duration calculation
		var data = CreateMinimalDsfFile (
			sampleRate: 2822400,
			channelCount: 2,
			sampleCount: ulong.MaxValue,
			hasMetadata: false);

		// Act
		var result = DsfFile.Read (data);

		// Assert - should not throw, may fail validation
		// Implementation should handle gracefully
		Assert.IsNotNull (result);
	}

	[TestMethod]
	public void Parse_MetadataOffsetBeyondFile_ReturnsFailure ()
	{
		// Arrange - metadata offset points beyond file size
		var dsd = CreateDsdChunk (28, 100, 200); // offset 200, file size 100
		var fmt = CreateFmtChunk (1, 0, 2, 2, 2822400, 1, 1000, 4096);
		var dataChunk = CreateDataChunkHeader (100);

		var combined = new byte[dsd.Length + fmt.Length + dataChunk.Length];
		dsd.CopyTo (combined, 0);
		fmt.CopyTo (combined, dsd.Length);
		dataChunk.CopyTo (combined, dsd.Length + fmt.Length);

		// Act
		var result = DsfFile.Read (combined);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("offset") || result.Error.Contains ("beyond"));
	}

	[TestMethod]
	public void Parse_ChunkSizeOverflow_HandlesGracefully ()
	{
		// Arrange - chunk claims to be larger than int.MaxValue
		var data = CreateDsdChunk (
			chunkSize: (ulong)int.MaxValue + 100,
			fileSize: 1000,
			metadataOffset: 0);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert - should handle without throwing
		Assert.IsNotNull (result);
	}

	[TestMethod]
	public void Parse_DataChunkSizeExceedsAvailableData_ReturnsFailure ()
	{
		// Arrange - data chunk claims 1000 bytes but file only has 100 bytes of audio
		var dsd = CreateDsdChunk (28, 192, 0); // file size = DSD + fmt + data header + audio
		var fmt = CreateFmtChunk (1, 0, 2, 2, 2822400, 1, 1000, 4096);
		var dataChunk = CreateDataChunkHeader (1000); // claims 1000 bytes of data
		var audioData = new byte[100]; // but only 100 bytes available

		var combined = new byte[dsd.Length + fmt.Length + dataChunk.Length + audioData.Length];
		dsd.CopyTo (combined, 0);
		fmt.CopyTo (combined, dsd.Length);
		dataChunk.CopyTo (combined, dsd.Length + fmt.Length);
		audioData.CopyTo (combined, dsd.Length + fmt.Length + dataChunk.Length);

		// Update DSD file size to match actual
		BinaryPrimitives.WriteUInt64LittleEndian (combined.AsSpan (12), (ulong)combined.Length);

		// Act
		var result = DsfFile.Read (combined);

		// Assert - should fail because data chunk claims more data than exists
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("data") || result.Error.Contains ("size") ||
			result.Error.Contains ("exceeds") || result.Error.Contains ("truncated"),
			$"Expected error about data size, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_DataChunkExtendsIntoMetadata_ReturnsFailure ()
	{
		// Arrange - data chunk size overlaps with metadata offset
		var dsd = CreateDsdChunk (28, 500, 200); // metadata at offset 200
		var fmt = CreateFmtChunk (1, 0, 2, 2, 2822400, 1, 1000, 4096);
		var dataChunk = CreateDataChunkHeader (300); // claims 300 bytes, but metadata starts at 200
		var audioData = new byte[300];

		// DSD(28) + fmt(52) + data header(12) = 92 bytes
		// Data claims to extend to 92 + 300 = 392, but metadata at 200
		var combined = new byte[500];
		dsd.CopyTo (combined, 0);
		fmt.CopyTo (combined, dsd.Length);
		dataChunk.CopyTo (combined, dsd.Length + fmt.Length);
		audioData.CopyTo (combined, dsd.Length + fmt.Length + dataChunk.Length);

		// Act
		var result = DsfFile.Read (combined);

		// Assert - should fail because data chunk overlaps metadata
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("data") || result.Error.Contains ("metadata") ||
			result.Error.Contains ("overlap") || result.Error.Contains ("extends"),
			$"Expected error about data/metadata overlap, got: {result.Error}");
	}

	#endregion

	#region Large File Tests (>4GB Boundary)

	/// <summary>
	/// Tests that DSF correctly handles file sizes greater than 4GB (uint.MaxValue).
	/// DSF uses 64-bit sizes throughout, supporting files up to ~18 exabytes.
	/// </summary>
	[TestMethod]
	public void ParseDsdChunk_FileSize5GB_ParsesCorrectly ()
	{
		// Arrange - 5GB file size (exceeds 32-bit limit)
		const ulong fiveGigabytes = 5UL * 1024 * 1024 * 1024;
		var data = CreateDsdChunk (
			chunkSize: 28,
			fileSize: fiveGigabytes,
			metadataOffset: 0);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (fiveGigabytes, result.Chunk!.FileSize);
	}

	[TestMethod]
	public void ParseDsdChunk_FileSize10GB_ParsesCorrectly ()
	{
		// Arrange - 10GB file size
		const ulong tenGigabytes = 10UL * 1024 * 1024 * 1024;
		var data = CreateDsdChunk (
			chunkSize: 28,
			fileSize: tenGigabytes,
			metadataOffset: 0);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (tenGigabytes, result.Chunk!.FileSize);
	}

	[TestMethod]
	public void ParseDsdChunk_MetadataOffset5GB_ParsesCorrectly ()
	{
		// Arrange - metadata located after 5GB of audio data
		const ulong fiveGigabytes = 5UL * 1024 * 1024 * 1024;
		var data = CreateDsdChunk (
			chunkSize: 28,
			fileSize: fiveGigabytes + 1024, // file larger than offset
			metadataOffset: fiveGigabytes);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (fiveGigabytes, result.Chunk!.MetadataOffset);
		Assert.IsTrue (result.Chunk.HasMetadata);
	}

	[TestMethod]
	public void ParseDsdChunk_BoundaryAt4GB_ParsesCorrectly ()
	{
		// Arrange - exactly at 4GB boundary (uint.MaxValue + 1)
		const ulong fourGBPlusOne = (ulong)uint.MaxValue + 1;
		var data = CreateDsdChunk (
			chunkSize: 28,
			fileSize: fourGBPlusOne,
			metadataOffset: 0);

		// Act
		var result = DsfDsdChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (fourGBPlusOne, result.Chunk!.FileSize);
	}

	[TestMethod]
	public void DsdChunk_RoundTrip_LargeFileSize_PreservesValue ()
	{
		// Arrange - large file size should survive round-trip
		const ulong largeFileSize = 8UL * 1024 * 1024 * 1024; // 8GB
		const ulong largeMetadataOffset = 7UL * 1024 * 1024 * 1024; // 7GB

		// Act - create, render, parse
		var original = DsfDsdChunk.Create (largeFileSize, largeMetadataOffset);
		var rendered = original.Render ();
		var parsed = DsfDsdChunk.Parse (rendered);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (largeFileSize, parsed.Chunk!.FileSize);
		Assert.AreEqual (largeMetadataOffset, parsed.Chunk.MetadataOffset);
	}

	[TestMethod]
	public void DsdChunk_RoundTrip_MaxValue_PreservesValue ()
	{
		// Arrange - maximum possible 64-bit value (theoretical max)
		const ulong maxFileSize = ulong.MaxValue;

		// Act - create, render, parse
		var original = DsfDsdChunk.Create (maxFileSize, 0);
		var rendered = original.Render ();
		var parsed = DsfDsdChunk.Parse (rendered);

		// Assert
		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (maxFileSize, parsed.Chunk!.FileSize);
	}

	[TestMethod]
	public void ParseFmtChunk_LargeSampleCount_ParsesCorrectly ()
	{
		// Arrange - large sample count for long DSD recordings
		// DSD64 at 2.8 MHz, 5 hour recording = ~50 billion samples
		const ulong largeSampleCount = 50_000_000_000UL;
		var data = CreateFmtChunk (
			formatVersion: 1,
			formatId: 0,
			channelType: 2,
			channelCount: 2,
			sampleRate: 2822400, // DSD64
			bitsPerSample: 1,
			sampleCount: largeSampleCount,
			blockSizePerChannel: 4096);

		// Act
		var result = DsfFmtChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (largeSampleCount, result.Chunk!.SampleCount);
	}

	[TestMethod]
	public void ParseDataChunk_LargeChunkSize_ParsesCorrectly ()
	{
		// Arrange - data chunk for 5GB of audio
		const ulong fiveGigabytes = 5UL * 1024 * 1024 * 1024;
		var data = CreateDataChunkHeader (fiveGigabytes);

		// Act
		var result = DsfDataChunk.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (fiveGigabytes, result.Chunk!.ChunkSize);
	}

	#endregion

	#region SaveToFile Tests

	[TestMethod]
	public void SaveToFile_WithPath_WritesFile ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Original", "Artist");
		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		file.Id3v2Tag!.Title = "Modified Title";

		var mockFs = new MockFileSystem ();

		// Act
		var result = file.SaveToFile ("/test/output.dsf", original, mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (mockFs.FileExists ("/test/output.dsf"));

		// Verify content is valid DSF
		var savedData = mockFs.ReadAllBytes ("/test/output.dsf");
		var reparsed = DsfFile.Read (savedData);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Modified Title", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void SaveToFile_WithPathOnly_ReReadsSourceFile ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Original", "Artist");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.dsf", original);

		var readResult = DsfFile.ReadFromFile ("/music/song.dsf", mockFs);
		Assert.IsTrue (readResult.IsSuccess);
		var file = readResult.File!;

		file.Id3v2Tag!.Title = "Updated Title";

		// Act - save to different path, should re-read original
		var result = file.SaveToFile ("/music/copy.dsf", mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (mockFs.FileExists ("/music/copy.dsf"));

		var savedData = mockFs.ReadAllBytes ("/music/copy.dsf");
		var reparsed = DsfFile.Read (savedData);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Updated Title", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void SaveToFile_NoArgs_SavesBackToSource ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Original", "Artist");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.dsf", original);

		var readResult = DsfFile.ReadFromFile ("/music/song.dsf", mockFs);
		Assert.IsTrue (readResult.IsSuccess);
		var file = readResult.File!;

		file.Id3v2Tag!.Title = "In-Place Update";

		// Act - save back to original path
		var result = file.SaveToFile (mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess, result.Error);

		var savedData = mockFs.ReadAllBytes ("/music/song.dsf");
		var reparsed = DsfFile.Read (savedData);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("In-Place Update", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void SaveToFile_NoSourcePath_ReturnsFailure ()
	{
		// Arrange - parsed from memory, no source path
		var original = CreateDsfWithId3v2 (2822400, 2, "Test", "Artist");
		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		var mockFs = new MockFileSystem ();

		// Act - try to save without source path
		var result = file.SaveToFile (mockFs);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("source") || result.Error.Contains ("path"));
	}

	[TestMethod]
	public void SaveToFile_PreservesAudioData ()
	{
		// Arrange - create file with specific audio data pattern
		var original = CreateDsfWithId3v2 (5644800, 2, "DSD128 Test", "Artist");
		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		// Modify metadata
		file.Id3v2Tag!.Title = "New Title";
		file.Id3v2Tag.Album = "New Album";

		var mockFs = new MockFileSystem ();

		// Act
		var result = file.SaveToFile ("/output.dsf", original, mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess);

		var savedData = mockFs.ReadAllBytes ("/output.dsf");
		var reparsed = DsfFile.Read (savedData);

		// Audio properties should be unchanged
		Assert.AreEqual (5644800, reparsed.File!.SampleRate);
		Assert.AreEqual (2, reparsed.File.Channels);
	}

	[TestMethod]
	public void SaveToFile_AddingTagToFileWithoutMetadata_Works ()
	{
		// Arrange - file without metadata
		var original = CreateMinimalDsfFile (2822400, 2, 2822400, hasMetadata: false);
		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;
		Assert.IsNull (file.Id3v2Tag);

		// Add new tag
		file.EnsureId3v2Tag ();
		file.Id3v2Tag!.Title = "Brand New Tag";
		file.Id3v2Tag.Artist = "New Artist";

		var mockFs = new MockFileSystem ();

		// Act
		var result = file.SaveToFile ("/output.dsf", original, mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess);

		var savedData = mockFs.ReadAllBytes ("/output.dsf");
		var reparsed = DsfFile.Read (savedData);
		Assert.IsNotNull (reparsed.File!.Id3v2Tag);
		Assert.AreEqual ("Brand New Tag", reparsed.File.Id3v2Tag.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithPath_WritesFile ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Async Test", "Artist");
		var parseResult = DsfFile.Read (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		file.Id3v2Tag!.Title = "Async Modified";

		var mockFs = new MockFileSystem ();

		// Act
		var result = await file.SaveToFileAsync ("/test/async.dsf", original, mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (mockFs.FileExists ("/test/async.dsf"));

		var savedData = mockFs.ReadAllBytes ("/test/async.dsf");
		var reparsed = DsfFile.Read (savedData);
		Assert.AreEqual ("Async Modified", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoArgs_SavesBackToSource ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Original", "Artist");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/async.dsf", original);

		var readResult = await DsfFile.ReadFromFileAsync ("/music/async.dsf", mockFs);
		Assert.IsTrue (readResult.IsSuccess);
		var file = readResult.File!;

		file.Id3v2Tag!.Title = "Async In-Place";

		// Act
		var result = await file.SaveToFileAsync (mockFs);

		// Assert
		Assert.IsTrue (result.IsSuccess, result.Error);

		var savedData = mockFs.ReadAllBytes ("/music/async.dsf");
		var reparsed = DsfFile.Read (savedData);
		Assert.AreEqual ("Async In-Place", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithCancellation_CanBeCancelled ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Test", "Artist");
		var parseResult = DsfFile.Read (original);
		var file = parseResult.File!;

		var mockFs = new MockFileSystem ();
		var cts = new CancellationTokenSource ();
		cts.Cancel (); // Pre-cancel

		// Act & Assert
		await Assert.ThrowsExactlyAsync<OperationCanceledException> (async () =>
			await file.SaveToFileAsync ("/test/cancelled.dsf", original, mockFs, cts.Token));
	}

	[TestMethod]
	public void SaveToFile_DisposedFile_ThrowsException ()
	{
		// Arrange
		var original = CreateDsfWithId3v2 (2822400, 2, "Test", "Artist");
		var parseResult = DsfFile.Read (original);
		var file = parseResult.File!;

		file.Dispose ();

		var mockFs = new MockFileSystem ();

		// Act & Assert
		Assert.ThrowsExactly<ObjectDisposedException> (() =>
			file.SaveToFile ("/test/disposed.dsf", original, mockFs));
	}

	#endregion

	#region Helper Methods

	private static byte[] CreateDsdChunk (ulong chunkSize, ulong fileSize, ulong metadataOffset)
	{
		var data = new byte[28];
		data[0] = (byte)'D';
		data[1] = (byte)'S';
		data[2] = (byte)'D';
		data[3] = (byte)' ';

		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), chunkSize);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (12), fileSize);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (20), metadataOffset);

		return data;
	}

	private static byte[] CreateFmtChunk (
		uint formatVersion,
		uint formatId,
		uint channelType,
		uint channelCount,
		uint sampleRate,
		uint bitsPerSample,
		ulong sampleCount,
		uint blockSizePerChannel)
	{
		// fmt chunk: "fmt " + chunk_size(8) + format_version(4) + format_id(4) +
		// channel_type(4) + channel_count(4) + sample_rate(4) + bits_per_sample(4) +
		// sample_count(8) + block_size(4) + reserved(4)
		var data = new byte[52];
		data[0] = (byte)'f';
		data[1] = (byte)'m';
		data[2] = (byte)'t';
		data[3] = (byte)' ';

		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), 52UL); // chunk size
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (12), formatVersion);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (16), formatId);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (20), channelType);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (24), channelCount);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (28), sampleRate);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (32), bitsPerSample);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (36), sampleCount);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (44), blockSizePerChannel);
		// reserved 4 bytes at end are already 0

		return data;
	}

	private static byte[] CreateDataChunkHeader (ulong chunkSize)
	{
		var data = new byte[12];
		data[0] = (byte)'d';
		data[1] = (byte)'a';
		data[2] = (byte)'t';
		data[3] = (byte)'a';
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), chunkSize);
		return data;
	}

	private static byte[] CreateMinimalDsfFile (
		uint sampleRate,
		uint channelCount,
		ulong sampleCount,
		bool hasMetadata)
	{
		var dsd = CreateDsdChunk (28, 0, 0); // file size updated later
		var fmt = CreateFmtChunk (1, 0, channelCount, channelCount, sampleRate, 1, sampleCount, 4096);

		// Minimal data chunk - just header plus a few bytes of padding
		var dataHeader = CreateDataChunkHeader (100);
		var audioData = new byte[88]; // padding to make chunk size 100

		var totalSize = dsd.Length + fmt.Length + dataHeader.Length + audioData.Length;

		// Update DSD chunk with correct file size
		BinaryPrimitives.WriteUInt64LittleEndian (dsd.AsSpan (12), (ulong)totalSize);

		var result = new byte[totalSize];
		var offset = 0;

		dsd.CopyTo (result, offset);
		offset += dsd.Length;

		fmt.CopyTo (result, offset);
		offset += fmt.Length;

		dataHeader.CopyTo (result, offset);
		offset += dataHeader.Length;

		audioData.CopyTo (result, offset);

		return result;
	}

	private static byte[] CreateDsfWithId3v2 (
		uint sampleRate,
		uint channelCount,
		string title,
		string artist)
	{
		// Create base file first
		var baseFile = CreateMinimalDsfFile (sampleRate, channelCount, sampleRate, false);

		// Create a simple ID3v2.4 tag
		var id3Data = CreateSimpleId3v2Tag (title, artist);

		// Calculate new file size and metadata offset
		var newFileSize = baseFile.Length + id3Data.Length;
		var metadataOffset = baseFile.Length;

		// Update DSD chunk header with metadata offset
		BinaryPrimitives.WriteUInt64LittleEndian (baseFile.AsSpan (12), (ulong)newFileSize);
		BinaryPrimitives.WriteUInt64LittleEndian (baseFile.AsSpan (20), (ulong)metadataOffset);

		// Combine
		var result = new byte[newFileSize];
		baseFile.CopyTo (result, 0);
		id3Data.CopyTo (result, baseFile.Length);

		return result;
	}

	private static byte[] CreateSimpleId3v2Tag (string title, string artist)
	{
		// Create minimal ID3v2.4 tag with title and artist
		using var builder = new System.IO.MemoryStream ();

		// ID3v2 header
		builder.Write (new byte[] { (byte)'I', (byte)'D', (byte)'3' }, 0, 3);
		builder.WriteByte (4); // version 2.4
		builder.WriteByte (0); // revision
		builder.WriteByte (0); // flags

		// We'll come back to write the syncsafe size
		var sizePos = builder.Position;
		builder.Write (new byte[4], 0, 4); // placeholder

		// TIT2 frame (title)
		var titleBytes = System.Text.Encoding.UTF8.GetBytes (title);
		WriteId3v2Frame (builder, "TIT2", titleBytes);

		// TPE1 frame (artist)
		var artistBytes = System.Text.Encoding.UTF8.GetBytes (artist);
		WriteId3v2Frame (builder, "TPE1", artistBytes);

		// Calculate and write syncsafe size
		var tagSize = (int)(builder.Length - 10);
		var syncsafe = new byte[4];
		syncsafe[0] = (byte)((tagSize >> 21) & 0x7F);
		syncsafe[1] = (byte)((tagSize >> 14) & 0x7F);
		syncsafe[2] = (byte)((tagSize >> 7) & 0x7F);
		syncsafe[3] = (byte)(tagSize & 0x7F);

		builder.Position = sizePos;
		builder.Write (syncsafe, 0, 4);

		return builder.ToArray ();
	}

	private static void WriteId3v2Frame (System.IO.MemoryStream stream, string frameId, byte[] content)
	{
		// Frame ID (4 bytes)
		var idBytes = System.Text.Encoding.ASCII.GetBytes (frameId);
		stream.Write (idBytes, 0, 4);

		// Frame size - syncsafe for 2.4
		var size = content.Length + 1; // +1 for encoding byte
		var syncsafe = new byte[4];
		syncsafe[0] = (byte)((size >> 21) & 0x7F);
		syncsafe[1] = (byte)((size >> 14) & 0x7F);
		syncsafe[2] = (byte)((size >> 7) & 0x7F);
		syncsafe[3] = (byte)(size & 0x7F);
		stream.Write (syncsafe, 0, 4);

		// Flags (2 bytes)
		stream.WriteByte (0);
		stream.WriteByte (0);

		// Encoding byte (UTF-8 = 3)
		stream.WriteByte (3);

		// Content
		stream.Write (content, 0, content.Length);
	}

	#endregion

	#region DsfAudioProperties Tests

	[TestMethod]
	public void Properties_AllProperties_ReturnCorrectValues ()
	{
		// Arrange
		var data = CreateMinimalDsfFile (
			sampleRate: 5644800,
			channelCount: 2,
			sampleCount: 5644800 * 120, // 2 minutes at DSD128
			hasMetadata: false);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		var props = result.File!.Properties;
		Assert.IsNotNull (props);
		Assert.AreEqual (TimeSpan.FromMinutes (2), props!.Duration);
		Assert.AreEqual (5644800, props.SampleRate);
		Assert.AreEqual (2, props.Channels);
		Assert.AreEqual (1, props.BitsPerSample);
		Assert.AreEqual (DsfSampleRate.DSD128, props.DsdRate);
		Assert.AreEqual (DsfChannelType.Stereo, props.ChannelType);
		Assert.AreEqual (4096, props.BlockSizePerChannel);
	}

	[TestMethod]
	public void Properties_MonoChannel_ReturnsCorrectChannelType ()
	{
		// Arrange
		var data = CreateMinimalDsfFile (
			sampleRate: 2822400,
			channelCount: 1,
			sampleCount: 2822400,
			hasMetadata: false);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DsfChannelType.Mono, result.File!.Properties!.ChannelType);
		Assert.AreEqual (1, result.File.Properties.Channels);
	}

	[TestMethod]
	public void Properties_Surround_ReturnsCorrectChannelType ()
	{
		// Arrange
		var data = CreateMinimalDsfFile (
			sampleRate: 2822400,
			channelCount: 6,
			sampleCount: 2822400,
			hasMetadata: false);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DsfChannelType.Surround51, result.File!.Properties!.ChannelType);
		Assert.AreEqual (6, result.File.Properties.Channels);
	}

	#endregion

	#region Async Tests

	[TestMethod]
	public async Task SaveToFileAsync_WritesAndReads_Correctly ()
	{
		// Arrange
		var tempPath = Path.GetTempFileName ();
		try {
			// Create file with metadata, read it, modify, and save
			var data = CreateDsfWithId3v2 (
				sampleRate: 2822400,
				channelCount: 2,
				title: "Async Test",
				artist: "Test Artist");
			File.WriteAllBytes (tempPath, data);

			var readResult = await DsfFile.ReadFromFileAsync (tempPath);
			Assert.IsTrue (readResult.IsSuccess);
			var file = readResult.File!;
			file.Id3v2Tag!.Title = "Modified Async";

			// Act
			await file.SaveToFileAsync ();

			// Assert
			var verifyResult = await DsfFile.ReadFromFileAsync (tempPath);
			Assert.IsTrue (verifyResult.IsSuccess);
			Assert.AreEqual ("Modified Async", verifyResult.File!.Id3v2Tag!.Title);
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithoutSourcePath_ReturnsFailure ()
	{
		// Arrange - parse in-memory (no SourcePath)
		var data = CreateMinimalDsfFile (2822400, 2, 2822400, hasMetadata: false);
		var parseResult = DsfFile.Read (data);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		// Act
		var result = await file.SaveToFileAsync ();

		// Assert - should fail because no SourcePath
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("source path"));
	}

	#endregion

	#region Result Type Tests

	[TestMethod]
	public void DsfFileReadResult_Equality_WorksCorrectly ()
	{
		// Arrange
		var data = CreateMinimalDsfFile (2822400, 2, 2822400, hasMetadata: false);
		var result1 = DsfFile.Read (data);
		var result2 = DsfFile.Read (data);
		var failure1 = DsfFileReadResult.Failure ("Error 1");
		var failure2 = DsfFileReadResult.Failure ("Error 1");
		var failure3 = DsfFileReadResult.Failure ("Error 2");

		// Act & Assert - Success results with different file instances aren't equal
		Assert.IsFalse (result1.Equals (result2));

		// Failures with same error are equal
		Assert.IsTrue (failure1.Equals (failure2));
		Assert.AreEqual (failure1.GetHashCode (), failure2.GetHashCode ());

		// Failures with different errors aren't equal
		Assert.IsFalse (failure1.Equals (failure3));

		// Object equality
		Assert.IsFalse (result1.Equals ((object?)null));
		Assert.IsFalse (result1.Equals ("not a result"));
	}

	[TestMethod]
	public void DsfDsdChunkParseResult_Equality_WorksCorrectly ()
	{
		// Arrange
		var data = CreateDsdChunk (28, 1000, 0);
		var result1 = DsfDsdChunk.Parse (data);
		var result2 = DsfDsdChunk.Parse (data);
		var failure = DsfDsdChunkParseResult.Failure ("Error");

		// Act & Assert
		Assert.IsTrue (result1.IsSuccess);
		Assert.IsTrue (result2.IsSuccess);
		Assert.IsFalse (failure.IsSuccess);
		Assert.IsFalse (result1.Equals ((object?)null));
	}

	[TestMethod]
	public void DsfFmtChunkParseResult_Equality_WorksCorrectly ()
	{
		// Arrange - full CreateFmtChunk signature
		var data = CreateFmtChunk (1, 0, 2, 2, 2822400, 1, 2822400, 4096);
		var result = DsfFmtChunk.Parse (data);
		var failure = DsfFmtChunkParseResult.Failure ("Error");

		// Act & Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (failure.IsSuccess);
		_ = result.GetHashCode ();
		_ = failure.GetHashCode ();
	}

	[TestMethod]
	public void DsfDataChunkParseResult_Equality_WorksCorrectly ()
	{
		// Arrange - use CreateDataChunkHeader
		var headerData = CreateDataChunkHeader (4096 * 2 + 12);
		var fullData = new byte[4096 * 2 + 12];
		headerData.CopyTo (fullData, 0);
		var result = DsfDataChunk.Parse (fullData);
		var failure = DsfDataChunkParseResult.Failure ("Error");

		// Act & Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (failure.IsSuccess);
		_ = result.GetHashCode ();
	}

	#endregion

	#region Disposal Tests

	[TestMethod]
	public void Dispose_ClearsPropertiesConsistently ()
	{
		// Arrange
		var data = CreateDsfWithId3v2 (2822400, 2, "Test", "Artist");
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Verify Properties exists before disposal
		Assert.IsNotNull (file.Properties);
		Assert.IsNotNull (file.Id3v2Tag);

		// Act
		file.Dispose ();

		// Assert - Properties should be null after disposal (consistent with DffFile)
		Assert.IsNull (file.Properties);
		Assert.IsNull (file.Id3v2Tag);
	}

	[TestMethod]
	public void Dispose_MultipleCalls_DoesNotThrow ()
	{
		// Arrange
		var data = CreateDsfWithId3v2 (2822400, 2, "Test", "Artist");
		var result = DsfFile.Read (data);
		var file = result.File!;

		// Act & Assert - should not throw on multiple disposals
		file.Dispose ();
		file.Dispose ();
		file.Dispose ();
	}

	[TestMethod]
	public void Render_AfterDispose_ThrowsObjectDisposedException ()
	{
		// Arrange
		var data = CreateDsfWithId3v2 (2822400, 2, "Test", "Artist");
		var result = DsfFile.Read (data);
		var file = result.File!;
		file.Dispose ();

		// Act & Assert
		var threw = false;
		try {
			file.Render ();
		} catch (ObjectDisposedException) {
			threw = true;
		}
		Assert.IsTrue (threw, "Expected ObjectDisposedException");
	}

	#endregion
}
