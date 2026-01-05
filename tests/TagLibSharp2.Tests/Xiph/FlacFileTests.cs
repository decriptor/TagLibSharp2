// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class FlacFileTests
{
	[TestMethod]
	public void Read_ValidFlac_ParsesMagicAndBlocks ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		var result = FlacFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("fLaC", result.Error!);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x66, 0x4C, 0x61 }; // "fLa" - incomplete

		var result = FlacFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_WithVorbisComment_ParsesMetadata ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (TestConstants.Metadata.Title, TestConstants.Metadata.Artist);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File.VorbisComment.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.File.VorbisComment.Artist);
	}

	[TestMethod]
	public void Read_WithPicture_ParsesPicture ()
	{
		var data = TestBuilders.Flac.CreateWithPicture ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotEmpty (result.File!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, result.File.Pictures[0].PictureType);
	}

	[TestMethod]
	public void Title_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (title: "My Song");

		var result = FlacFile.Read (data);

		Assert.AreEqual ("My Song", result.File!.Title);
	}

	[TestMethod]
	public void Title_Set_CreatesVorbisComment ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		Assert.IsNotNull (file.VorbisComment);
		Assert.AreEqual ("New Title", file.Title);
	}

	[TestMethod]
	public void Artist_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (artist: TestConstants.Metadata.Artist);

		var result = FlacFile.Read (data);

		Assert.AreEqual (TestConstants.Metadata.Artist, result.File!.Artist);
	}

	[TestMethod]
	public void Pictures_ReturnsAllPictures ()
	{
		var data = TestBuilders.Flac.CreateWithMultiplePictures ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (2, result.File!.Pictures);
	}

	[TestMethod]
	public void AddPicture_AddsToPictureList ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		var picture = FlacPicture.FromBytes (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
		file.AddPicture (picture);

		Assert.HasCount (1, file.Pictures);
	}

	[TestMethod]
	public void RemovePictures_RemovesMatchingType ()
	{
		var data = TestBuilders.Flac.CreateWithPicture ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.RemovePictures (PictureType.FrontCover);

		Assert.IsEmpty (file.Pictures);
	}

	[TestMethod]
	public void MetadataSize_ReturnsCorrectSize ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment ("Title", "Artist");

		var result = FlacFile.Read (data);

		Assert.IsGreaterThan (0, result.File!.MetadataSize);
	}

	[TestMethod]
	public void Read_StreamInfoTooSmall_ReturnsFailure ()
	{
		var data = TestBuilders.Flac.CreateWithInvalidStreamInfoSize (33);

		var result = FlacFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("STREAMINFO", result.Error!);
	}

	[TestMethod]
	public void Read_StreamInfoTooLarge_ReturnsFailure ()
	{
		var data = TestBuilders.Flac.CreateWithInvalidStreamInfoSize (35);

		var result = FlacFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("STREAMINFO", result.Error!);
	}

	[TestMethod]
	public void Properties_ParsesSampleRateFromStreamInfo ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (44100, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void Properties_ParsesChannelsFromStreamInfo ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Properties_ParsesBitsPerSampleFromStreamInfo ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	public void Properties_MinimalFile_HasZeroDuration ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.Zero, result.File!.Properties.Duration);
	}

	[TestMethod]
	public void Properties_WithSamples_CalculatesDuration ()
	{
		// 88200 samples at 44100 Hz = 2 seconds
		var data = TestBuilders.Flac.CreateWithStreamInfo (44100, 2, 16, 88200);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File!.Properties.Duration);
		Assert.AreEqual (44100, result.File.Properties.SampleRate);
		Assert.AreEqual (2, result.File.Properties.Channels);
		Assert.AreEqual (16, result.File.Properties.BitsPerSample);
	}

	[TestMethod]
	public void Properties_HighResAudio_ParsesCorrectly ()
	{
		// 96kHz, 24-bit, stereo - 5 seconds
		var totalSamples = 96000UL * 5;
		var data = TestBuilders.Flac.CreateWithStreamInfo (96000, 2, 24, totalSamples);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (96000, result.File!.Properties.SampleRate);
		Assert.AreEqual (24, result.File.Properties.BitsPerSample);
		Assert.AreEqual (2, result.File.Properties.Channels);
		Assert.AreEqual (TimeSpan.FromSeconds (5), result.File.Properties.Duration);
	}

	[TestMethod]
	public void Properties_CodecIsFlac ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("FLAC", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Render_PreservesAudioData ()
	{
		var originalData = TestBuilders.Flac.CreateWithAudioData (new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
		var result = FlacFile.Read (originalData);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.File!.Render (originalData);

		var reResult = FlacFile.Read (rendered.Span);
		Assert.IsTrue (reResult.IsSuccess);

		var audioStart = reResult.File!.MetadataSize;
		var audioData = rendered.Span.Slice (audioStart);
		CollectionAssert.AreEqual (new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, audioData.ToArray ());
	}

	[TestMethod]
	public void Render_WritesVorbisComment ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";
		file.Artist = "New Artist";

		var rendered = file.Render (data);
		var reResult = FlacFile.Read (rendered.Span);

		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("New Title", reResult.File!.Title);
		Assert.AreEqual ("New Artist", reResult.File.Artist);
	}

	[TestMethod]
	public void Render_WritesPictures ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		var pictureData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var picture = FlacPicture.FromBytes (pictureData);
		file.AddPicture (picture);

		var rendered = file.Render (data);
		var reResult = FlacFile.Read (rendered.Span);

		Assert.IsTrue (reResult.IsSuccess);
		Assert.HasCount (1, reResult.File!.Pictures);
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesAllMetadata ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment ("Original Title", "Original Artist");
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "Modified Title";
		file.Album = "New Album";

		var rendered = file.Render (data);
		var reResult = FlacFile.Read (rendered.Span);

		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Modified Title", reResult.File!.Title);
		Assert.AreEqual ("Original Artist", reResult.File.Artist);
		Assert.AreEqual ("New Album", reResult.File.Album);
	}

	[TestMethod]
	public void SaveToFile_WritesToFileSystem ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;
		file.Title = "Saved Title";

		var fs = new MockFileSystem ();
		var writeResult = file.SaveToFile ("/output.flac", data, fs);

		Assert.IsTrue (writeResult.IsSuccess);
		Assert.IsTrue (fs.FileExists ("/output.flac"));

		var savedData = fs.ReadAllBytes ("/output.flac");
		var reResult = FlacFile.Read (savedData);
		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Saved Title", reResult.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesToFileSystem ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;
		file.Artist = "Async Artist";

		var fs = new MockFileSystem ();
		var writeResult = await file.SaveToFileAsync ("/output.flac", data, fs);

		Assert.IsTrue (writeResult.IsSuccess);

		var savedData = fs.ReadAllBytes ("/output.flac");
		var reResult = FlacFile.Read (savedData);
		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Async Artist", reResult.File!.Artist);
	}

	[TestMethod]
	public void Render_IncludesPaddingBlock ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		var rendered = file.Render (data);

		Assert.IsGreaterThan (data.Length, rendered.Length);
	}

	[TestMethod]
	public void Read_WithSeekTable_PreservesBlock ()
	{
		var data = TestBuilders.Flac.CreateWithSeekTable ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.File!.PreservedBlocks);
		Assert.AreEqual (FlacBlockType.SeekTable, result.File.PreservedBlocks[0].BlockType);
	}

	[TestMethod]
	public void Read_WithApplicationBlock_PreservesBlock ()
	{
		var data = TestBuilders.Flac.CreateWithApplicationBlock ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.File!.PreservedBlocks);
		Assert.AreEqual (FlacBlockType.Application, result.File.PreservedBlocks[0].BlockType);
	}

	[TestMethod]
	public void Render_WithSeekTable_PreservesSeekTable ()
	{
		var data = TestBuilders.Flac.CreateWithSeekTable ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		var rendered = file.Render (data);
		var reResult = FlacFile.Read (rendered.Span);

		Assert.IsTrue (reResult.IsSuccess);
		Assert.HasCount (1, reResult.File!.PreservedBlocks);
		Assert.AreEqual (FlacBlockType.SeekTable, reResult.File.PreservedBlocks[0].BlockType);
		Assert.AreEqual (result.File!.PreservedBlocks[0].Data.Length, reResult.File.PreservedBlocks[0].Data.Length);
	}

	[TestMethod]
	public void Render_WithApplicationBlock_PreservesApplicationBlock ()
	{
		var data = TestBuilders.Flac.CreateWithApplicationBlock ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		var rendered = file.Render (data);
		var reResult = FlacFile.Read (rendered.Span);

		Assert.IsTrue (reResult.IsSuccess);
		Assert.HasCount (1, reResult.File!.PreservedBlocks);
		Assert.AreEqual (FlacBlockType.Application, reResult.File.PreservedBlocks[0].BlockType);
		CollectionAssert.AreEqual (
			result.File!.PreservedBlocks[0].Data.Span.Slice (0, 4).ToArray (),
			reResult.File.PreservedBlocks[0].Data.Span.Slice (0, 4).ToArray ());
	}

	[TestMethod]
	public void Render_WithMultiplePreservedBlocks_PreservesAll ()
	{
		var data = TestBuilders.Flac.CreateWithSeekTableAndApplication ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "Modified";

		var rendered = file.Render (data);
		var reResult = FlacFile.Read (rendered.Span);

		Assert.IsTrue (reResult.IsSuccess);
		Assert.HasCount (2, reResult.File!.PreservedBlocks);
	}

	// ===========================================================================
	// MD5 Signature Tests
	// ===========================================================================

	[TestMethod]
	public void AudioMd5Signature_WithZeroMd5_ReturnsZeros ()
	{
		var data = TestBuilders.Flac.CreateWithMd5 ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		var md5 = result.File!.AudioMd5Signature;
		Assert.AreEqual (16, md5.Length);
		for (var i = 0; i < 16; i++)
			Assert.AreEqual (0, md5[i]);
	}

	[TestMethod]
	public void AudioMd5Signature_WithNonZeroMd5_ReturnsCorrectBytes ()
	{
		var expectedMd5 = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 };
		var data = TestBuilders.Flac.CreateWithMd5 (expectedMd5);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		var md5 = result.File!.AudioMd5Signature;
		Assert.AreEqual (16, md5.Length);
		for (var i = 0; i < 16; i++)
			Assert.AreEqual (expectedMd5[i], md5[i]);
	}

	[TestMethod]
	public void AudioMd5SignatureHex_WithZeroMd5_ReturnsZeroString ()
	{
		var data = TestBuilders.Flac.CreateWithMd5 ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("00000000000000000000000000000000", result.File!.AudioMd5SignatureHex);
	}

	[TestMethod]
	public void AudioMd5SignatureHex_WithNonZeroMd5_ReturnsCorrectHexString ()
	{
		var md5 = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 };
		var data = TestBuilders.Flac.CreateWithMd5 (md5);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("0123456789abcdeffedcba9876543210", result.File!.AudioMd5SignatureHex);
	}

	[TestMethod]
	public void HasAudioMd5Signature_WithZeroMd5_ReturnsFalse ()
	{
		var data = TestBuilders.Flac.CreateWithMd5 ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.File!.HasAudioMd5Signature);
	}

	[TestMethod]
	public void HasAudioMd5Signature_WithNonZeroMd5_ReturnsTrue ()
	{
		var md5 = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 };
		var data = TestBuilders.Flac.CreateWithMd5 (md5);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.File!.HasAudioMd5Signature);
	}

	[TestMethod]
	public void HasAudioMd5Signature_WithSingleNonZeroByte_ReturnsTrue ()
	{
		// MD5 with only the last byte non-zero
		var md5 = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01 };
		var data = TestBuilders.Flac.CreateWithMd5 (md5);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.File!.HasAudioMd5Signature);
	}

	// ===========================================================================
	// STREAMINFO Block/Frame Size Tests
	// ===========================================================================

	[TestMethod]
	public void MinBlockSize_ReturnsCorrectValue ()
	{
		// Default test files use 4096 for block sizes
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4096, result.File!.MinBlockSize);
	}

	[TestMethod]
	public void MaxBlockSize_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4096, result.File!.MaxBlockSize);
	}

	[TestMethod]
	public void MinFrameSize_WithZeroValue_ReturnsZero ()
	{
		// Default test files use 0 for frame sizes (unknown)
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.MinFrameSize);
	}

	[TestMethod]
	public void MaxFrameSize_WithZeroValue_ReturnsZero ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.MaxFrameSize);
	}

	[TestMethod]
	public void MinBlockSize_WithCustomValue_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Flac.CreateWithBlockAndFrameSizes (
			minBlockSize: 576,
			maxBlockSize: 4608,
			minFrameSize: 1234,
			maxFrameSize: 56789);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (576, result.File!.MinBlockSize);
	}

	[TestMethod]
	public void MaxBlockSize_WithCustomValue_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Flac.CreateWithBlockAndFrameSizes (
			minBlockSize: 576,
			maxBlockSize: 4608,
			minFrameSize: 1234,
			maxFrameSize: 56789);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4608, result.File!.MaxBlockSize);
	}

	[TestMethod]
	public void MinFrameSize_WithCustomValue_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Flac.CreateWithBlockAndFrameSizes (
			minBlockSize: 576,
			maxBlockSize: 4608,
			minFrameSize: 1234,
			maxFrameSize: 56789);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1234, result.File!.MinFrameSize);
	}

	[TestMethod]
	public void MaxFrameSize_WithCustomValue_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Flac.CreateWithBlockAndFrameSizes (
			minBlockSize: 576,
			maxBlockSize: 4608,
			minFrameSize: 1234,
			maxFrameSize: 56789);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (56789, result.File!.MaxFrameSize);
	}

	[TestMethod]
	public void MaxFrameSize_WithMaxValue_ReturnsCorrectValue ()
	{
		// Max frame size is 24 bits = 0xFFFFFF = 16777215
		var data = TestBuilders.Flac.CreateWithBlockAndFrameSizes (
			minBlockSize: 16,
			maxBlockSize: 65535,
			minFrameSize: 0xFFFFFF,
			maxFrameSize: 0xFFFFFF);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16777215, result.File!.MinFrameSize);
		Assert.AreEqual (16777215, result.File!.MaxFrameSize);
	}

	[TestMethod]
	public void BlockSizes_WithMaxValue_ReturnsCorrectValue ()
	{
		// Block sizes are 16 bits = 0xFFFF = 65535
		var data = TestBuilders.Flac.CreateWithBlockAndFrameSizes (
			minBlockSize: 65535,
			maxBlockSize: 65535,
			minFrameSize: 0,
			maxFrameSize: 0);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (65535, result.File!.MinBlockSize);
		Assert.AreEqual (65535, result.File!.MaxBlockSize);
	}
}
