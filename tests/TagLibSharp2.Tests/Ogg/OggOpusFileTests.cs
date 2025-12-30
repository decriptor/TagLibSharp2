// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ogg")]
[TestCategory ("Opus")]
public class OggOpusFileTests
{
	[TestMethod]
	public void Read_ValidOpusFile_ParsesVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("Test Title", "Test Artist");

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Title", result.File.VorbisComment.Title);
		Assert.AreEqual ("Test Artist", result.File.VorbisComment.Artist);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[100]; // All zeros, no valid Ogg pages

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("Ogg", result.Error!);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x4F, 0x67, 0x67, 0x53 }; // Just "OggS"

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_NotOpusCodec_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreatePageWithNonOpusData ();

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("Opus", result.Error!);
	}

	[TestMethod]
	public void Read_ValidCrc_SucceedsWithValidation ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile (calculateCrc: true);

		var result = OggOpusFile.Read (data, validateCrc: true);

		Assert.IsTrue (result.IsSuccess, $"Expected success but got: {result.Error}");
	}

	[TestMethod]
	public void Read_InvalidCrc_WithValidationEnabled_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile (calculateCrc: true);

		// Corrupt the CRC on the first page (bytes 22-25)
		data[22] ^= 0xFF;

		var result = OggOpusFile.Read (data, validateCrc: true);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("CRC", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_InvalidCrc_WithValidationDisabled_Succeeds ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile (calculateCrc: true);

		// Corrupt the CRC on the first page (bytes 22-25)
		data[22] ^= 0xFF;

		var result = OggOpusFile.Read (data, validateCrc: false);

		// Should still succeed since CRC validation is disabled
		Assert.IsTrue (result.IsSuccess);
	}

	[TestMethod]
	public void Title_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("My Song", "");

		var result = OggOpusFile.Read (data);

		Assert.AreEqual ("My Song", result.File!.Title);
	}

	[TestMethod]
	public void Title_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("", "");
		var result = OggOpusFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		Assert.AreEqual ("New Title", file.Title);
		Assert.IsNotNull (file.VorbisComment);
	}

	[TestMethod]
	public void Artist_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("", "Test Artist");

		var result = OggOpusFile.Read (data);

		Assert.AreEqual ("Test Artist", result.File!.Artist);
	}

	[TestMethod]
	public void Properties_SampleRateIsAlways48kHz ()
	{
		// Opus always outputs at 48kHz regardless of input sample rate
		var data = TestBuilders.Opus.CreateFileWithProperties (
			inputSampleRate: 44100); // Even with 44100 input, output is 48kHz

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (48000, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void Properties_ParsesChannelsFromOpusHead ()
	{
		var data = TestBuilders.Opus.CreateFileWithProperties (channels: 2);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Properties_MonoChannel ()
	{
		var data = TestBuilders.Opus.CreateFileWithProperties (channels: 1);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Properties_CodecIsOpus ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Opus", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Read_MultiPageOpusTags_ParsesCorrectly ()
	{
		// OpusTags spanning multiple pages (common with embedded album art)
		var data = TestBuilders.Opus.CreateFileWithMultiPageOpusTags (
			"Multi-Page Title",
			"Multi-Page Artist",
			paddingSize: 300);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("Multi-Page Title", result.File!.Title);
		Assert.AreEqual ("Multi-Page Artist", result.File.Artist);
	}

	[TestMethod]
	public void Properties_BitsPerSampleIsZero ()
	{
		// Opus is lossy, doesn't have a fixed bits per sample
		var data = TestBuilders.Opus.CreateMinimalFile ();

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	public void PreSkip_ParsedFromOpusHead ()
	{
		var data = TestBuilders.Opus.CreateFileWithProperties (preSkip: 500);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((ushort)500, result.File!.PreSkip);
	}

	[TestMethod]
	public void InputSampleRate_ParsedFromOpusHead ()
	{
		var data = TestBuilders.Opus.CreateFileWithProperties (inputSampleRate: 44100);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (44100u, result.File!.InputSampleRate);
	}

	[TestMethod]
	public void Duration_CalculatedFromGranulePosition ()
	{
		// 480000 samples at 48kHz = 10 seconds
		// Pre-skip of 312 samples is subtracted
		var granulePosition = 480312ul; // 480000 + 312 pre-skip
		var data = TestBuilders.Opus.CreateFileWithProperties (
			preSkip: 312,
			granulePosition: granulePosition);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		var expectedDuration = TimeSpan.FromSeconds (10);
		var actualDuration = result.File!.Properties.Duration;
		Assert.IsTrue (Math.Abs ((expectedDuration - actualDuration).TotalMilliseconds) < 10,
			$"Expected ~10 seconds, got {actualDuration.TotalSeconds} seconds");
	}

	[TestMethod]
	public void Duration_GranulePositionLessThanPreSkip_ReturnsZeroDuration ()
	{
		// Edge case: granulePosition < preSkip should result in zero duration, not underflow
		var data = TestBuilders.Opus.CreateFileWithProperties (
			preSkip: 1000,
			granulePosition: 500); // Less than preSkip

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.Zero, result.File!.Properties.Duration,
			"Duration should be zero when granulePosition < preSkip");
	}

	[TestMethod]
	public void Duration_GranulePositionEqualsPreSkip_ReturnsZeroDuration ()
	{
		// Edge case: granulePosition == preSkip should result in zero duration
		var data = TestBuilders.Opus.CreateFileWithProperties (
			preSkip: 1000,
			granulePosition: 1000);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.Zero, result.File!.Properties.Duration,
			"Duration should be zero when granulePosition equals preSkip");
	}

	[TestMethod]
	[DataRow ("日本語タイトル 🎵", "Художник")]
	[DataRow ("Café", "Naïve")]
	[DataRow ("", "   ")]
	public void Read_Utf8EdgeCases_ParsesCorrectly (string title, string artist)
	{
		var data = TestBuilders.Opus.CreateMinimalFile (title, artist);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		if (string.IsNullOrEmpty (title))
			Assert.IsNull (result.File!.Title);
		else
			Assert.AreEqual (title, result.File!.Title);
		Assert.AreEqual (artist, result.File!.Artist);
	}

	[TestMethod]
	public void Render_PreservesOpusHeadAndUpdatesOpusTags ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("Original", "Artist");
		var result = OggOpusFile.Read (data);
		var file = result.File!;

		file.Title = "Updated Title";
		var rendered = file.Render (data);

		Assert.IsFalse (rendered.IsEmpty);

		// Re-parse to verify
		var rereadResult = OggOpusFile.Read (rendered.Span);
		Assert.IsTrue (rereadResult.IsSuccess);
		Assert.AreEqual ("Updated Title", rereadResult.File!.Title);
	}

	[TestMethod]
	public void Render_NoFramingBitInOpusTags ()
	{
		// Verify that rendered OpusTags does NOT have a framing bit (unlike Vorbis)
		var data = TestBuilders.Opus.CreateMinimalFile ("Test", "Artist");
		var result = OggOpusFile.Read (data);
		var file = result.File!;

		var rendered = file.Render (data);

		// Re-parse should succeed (no framing bit validation issues)
		var rereadResult = OggOpusFile.Read (rendered.Span);
		Assert.IsTrue (rereadResult.IsSuccess);
	}

	[TestMethod]
	public void MediaFile_DetectsOpusVsVorbis ()
	{
		var opusData = TestBuilders.Opus.CreateMinimalFile ();
		var vorbisData = TestBuilders.Ogg.CreateMinimalFile ();

		var opusFormat = MediaFile.DetectFormat (opusData);
		var vorbisFormat = MediaFile.DetectFormat (vorbisData);

		Assert.AreEqual (MediaFormat.Opus, opusFormat);
		Assert.AreEqual (MediaFormat.OggVorbis, vorbisFormat);
	}

	[TestMethod]
	public void MediaFile_Open_Opus ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("Opus Title", "Opus Artist");

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Opus, result.Format);
		Assert.AreEqual ("Opus Title", result.Tag?.Title);
	}

	[TestMethod]
	public void OggOpusFileReadResult_Equality ()
	{
		var result1 = OggOpusFileReadResult.Failure ("error");
		var result2 = OggOpusFileReadResult.Failure ("error");
		var result3 = OggOpusFileReadResult.Failure ("different");

		Assert.AreEqual (result1, result2);
		Assert.AreNotEqual (result1, result3);
		Assert.IsTrue (result1 == result2);
		Assert.IsTrue (result1 != result3);
	}

	[TestMethod]
	public void OggOpusFileReadResult_GetHashCode_ConsistentWithEquals ()
	{
		var result1 = OggOpusFileReadResult.Failure ("error");
		var result2 = OggOpusFileReadResult.Failure ("error");

		Assert.AreEqual (result1.GetHashCode (), result2.GetHashCode ());
	}

	// ==========================================================================
	// RFC 7845 Compliance: Version validation (accept 0-15, reject 16+)
	// ==========================================================================

	[TestMethod]
	[DataRow ((byte)0)]
	[DataRow ((byte)1)]
	[DataRow ((byte)2)]
	[DataRow ((byte)5)]
	[DataRow ((byte)10)]
	[DataRow ((byte)15)]
	public void Read_Version0To15_AcceptsAsVersion1 (byte version)
	{
		// RFC 7845: "Implementations SHOULD treat a file with a version of 0-15 as if it were version 1"
		var data = TestBuilders.Opus.CreateFileWithVersion (version);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess, $"Version {version} should be accepted");
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	[DataRow ((byte)16)]
	[DataRow ((byte)50)]
	[DataRow ((byte)100)]
	[DataRow ((byte)255)]
	public void Read_Version16AndAbove_ReturnsFailure (byte version)
	{
		// RFC 7845: "MUST reject files with versions 16 or higher"
		var data = TestBuilders.Opus.CreateFileWithVersion (version);

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess, $"Version {version} should be rejected");
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "version", StringComparison.OrdinalIgnoreCase);
	}

	// ==========================================================================
	// Channel Mapping Family Tests (RFC 7845 §5.1.1.2)
	// ==========================================================================

	[TestMethod]
	public void Read_ChannelMappingFamily0_Mono_Succeeds ()
	{
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 1, mappingFamily: 0);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Read_ChannelMappingFamily0_Stereo_Succeeds ()
	{
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 2, mappingFamily: 0);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Read_ChannelMappingFamily0_MoreThan2Channels_ReturnsFailure ()
	{
		// Family 0 only allows 1 or 2 channels
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 3, mappingFamily: 0);

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("channel", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_ChannelMappingFamily1_8Channels_Succeeds ()
	{
		// Family 1 allows 1-8 channels with Vorbis order
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 8, mappingFamily: 1);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (8, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Read_ChannelMappingFamily255_DiscreteChannels_Succeeds ()
	{
		// Family 255 allows any number of discrete channels
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 16, mappingFamily: 255);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.File!.Properties.Channels);
	}

	[TestMethod]
	[DataRow (2)]
	[DataRow (100)]
	[DataRow (254)]
	public void Read_ReservedChannelMappingFamily_FailsWithError (int mappingFamily)
	{
		// RFC 7845 §5.1.1.2: Families 2-254 are reserved and SHOULD NOT be used
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 4, mappingFamily: (byte)mappingFamily);

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "reserves families 2-254");
	}

	// ==========================================================================
	// Tag Property Tests (Album, Year, Genre, Track, Comment)
	// ==========================================================================

	[TestMethod]
	public void Album_GetSet_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);

		result.File!.Album = "Test Album";

		Assert.AreEqual ("Test Album", result.File.Album);
		Assert.AreEqual ("Test Album", result.File.VorbisComment!.Album);
	}

	[TestMethod]
	public void Year_GetSet_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);

		result.File!.Year = "2025";

		Assert.AreEqual ("2025", result.File.Year);
		Assert.AreEqual ("2025", result.File.VorbisComment!.Year);
	}

	[TestMethod]
	public void Genre_GetSet_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);

		result.File!.Genre = "Jazz";

		Assert.AreEqual ("Jazz", result.File.Genre);
		Assert.AreEqual ("Jazz", result.File.VorbisComment!.Genre);
	}

	[TestMethod]
	public void Track_GetSet_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);

		result.File!.Track = 5;

		Assert.AreEqual (5u, result.File.Track);
		Assert.AreEqual (5u, result.File.VorbisComment!.Track);
	}

	[TestMethod]
	public void Comment_GetSet_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);

		result.File!.Comment = "Great song";

		Assert.AreEqual ("Great song", result.File.Comment);
		Assert.AreEqual ("Great song", result.File.VorbisComment!.Comment);
	}

	// ==========================================================================
	// OutputGain Tests (including dB conversion)
	// ==========================================================================

	[TestMethod]
	public void OutputGain_ParsedFromOpusHead ()
	{
		// -256 in Q7.8 = -1.0 dB
		var data = TestBuilders.Opus.CreateFileWithOutputGain (-256);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((short)-256, result.File!.OutputGain);
	}

	[TestMethod]
	public void OutputGainDb_ConvertsQ78ToDecibels ()
	{
		// 256 in Q7.8 = +1.0 dB
		var data = TestBuilders.Opus.CreateFileWithOutputGain (256);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1.0, result.File!.OutputGainDb, 0.001);
	}

	[TestMethod]
	public void OutputGainDb_NegativeGain ()
	{
		// -512 in Q7.8 = -2.0 dB
		var data = TestBuilders.Opus.CreateFileWithOutputGain (-512);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (-2.0, result.File!.OutputGainDb, 0.001);
	}

	[TestMethod]
	public void OutputGainDb_ZeroGain ()
	{
		var data = TestBuilders.Opus.CreateFileWithOutputGain (0);

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0.0, result.File!.OutputGainDb, 0.001);
	}

	// ==========================================================================
	// File I/O Tests
	// ==========================================================================

	[TestMethod]
	public void ReadFromFile_ValidPath_ReturnsSuccess ()
	{
		var tempPath = Path.GetTempFileName ();
		var data = TestBuilders.Opus.CreateMinimalFile ("Title", "Artist");
		File.WriteAllBytes (tempPath, data);

		try {
			var result = OggOpusFile.ReadFromFile (tempPath);

			Assert.IsTrue (result.IsSuccess);
			Assert.AreEqual ("Title", result.File!.Title);
			Assert.AreEqual (tempPath, result.File.SourcePath);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public void SaveToFile_WritesCorrectly ()
	{
		var originalData = TestBuilders.Opus.CreateMinimalFile ("Original", "Artist");
		var result = OggOpusFile.Read (originalData);
		result.File!.Title = "Modified";

		var tempPath = Path.GetTempFileName ();
		try {
			var saveResult = result.File.SaveToFile (tempPath, originalData);

			Assert.IsTrue (saveResult.IsSuccess);
			Assert.IsTrue (File.Exists (tempPath));

			// Verify by re-reading
			var reread = OggOpusFile.ReadFromFile (tempPath);
			Assert.AreEqual ("Modified", reread.File!.Title);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidPath_ReturnsSuccess ()
	{
		var tempPath = Path.GetTempFileName ();
		var data = TestBuilders.Opus.CreateMinimalFile ("Async Title", "Async Artist");
		await File.WriteAllBytesAsync (tempPath, data);

		try {
			var result = await OggOpusFile.ReadFromFileAsync (tempPath);

			Assert.IsTrue (result.IsSuccess);
			Assert.AreEqual ("Async Title", result.File!.Title);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesCorrectly ()
	{
		var originalData = TestBuilders.Opus.CreateMinimalFile ("Original", "Artist");
		var result = OggOpusFile.Read (originalData);
		result.File!.Title = "Async Modified";

		var tempPath = Path.GetTempFileName ();
		try {
			var saveResult = await result.File.SaveToFileAsync (tempPath, originalData);

			Assert.IsTrue (saveResult.IsSuccess);

			var reread = await OggOpusFile.ReadFromFileAsync (tempPath);
			Assert.AreEqual ("Async Modified", reread.File!.Title);
		} finally {
			File.Delete (tempPath);
		}
	}

	// ==========================================================================
	// Cancellation Token Tests
	// ==========================================================================

	[TestMethod]
	public async Task ReadFromFileAsync_Cancellation_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ("Test", "Artist");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.opus", data);
		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await OggOpusFile.ReadFromFileAsync ("/test.opus", mockFs, cancellationToken: cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancel", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public async Task SaveToFileAsync_Cancellation_ReturnsFailure ()
	{
		var originalData = TestBuilders.Opus.CreateMinimalFile ("Test", "Artist");
		var result = OggOpusFile.Read (originalData);
		var mockFs = new MockFileSystem ();
		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var saveResult = await result.File!.SaveToFileAsync ("/test.opus", originalData, mockFs, cts.Token);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsNotNull (saveResult.Error);
		StringAssert.Contains (saveResult.Error, "cancel", StringComparison.OrdinalIgnoreCase);
	}

	// ==========================================================================
	// Render: EOS Flag Tests (RFC 3533 compliance)
	// ==========================================================================

	[TestMethod]
	public void Render_LastPageHasEosFlag ()
	{
		// RFC 3533: "The last page of a logical Ogg stream MUST have the EOS flag set"
		var data = TestBuilders.Opus.CreateMinimalFile ("Test", "Artist");
		var result = OggOpusFile.Read (data);

		var rendered = result.File!.Render (data);

		Assert.IsFalse (rendered.IsEmpty);

		// Find the last OggS page and check its flags
		var lastPageOffset = FindLastOggPageOffset (rendered.Span);
		Assert.IsTrue (lastPageOffset >= 0, "Should find at least one Ogg page");

		var flags = rendered.Span[lastPageOffset + 5];
		var hasEos = (flags & 0x04) != 0; // EOS flag is bit 2
		Assert.IsTrue (hasEos, "Last page should have EOS flag set");
	}

	[TestMethod]
	public void Render_SequenceNumbersAreSequential ()
	{
		// RFC 3533: Sequence numbers must be monotonically increasing
		var data = TestBuilders.Opus.CreateMinimalFile ("Test", "Artist");
		var result = OggOpusFile.Read (data);

		var rendered = result.File!.Render (data);

		var sequenceNumbers = ExtractSequenceNumbers (rendered.Span);
		Assert.IsTrue (sequenceNumbers.Count >= 2, "Should have at least 2 pages (OpusHead + OpusTags)");

		// Verify sequence numbers are sequential starting from 0
		for (var i = 0; i < sequenceNumbers.Count; i++) {
			Assert.AreEqual ((uint)i, sequenceNumbers[i],
				$"Page {i} should have sequence number {i}, got {sequenceNumbers[i]}");
		}
	}

	// ==========================================================================
	// RFC 7845 Compliance: OpusHead validation
	// ==========================================================================

	[TestMethod]
	public void Read_OpusHeadTooShort_ReturnsFailure ()
	{
		// RFC 7845 §5.1.1: OpusHead must be at least 19 bytes
		// Create OpusHead with only 18 bytes (missing channel mapping family)
		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.OpusHead); // 8 bytes
		builder.Add ((byte)1); // Version
		builder.Add ((byte)2); // Channels
		builder.AddUInt16LE (312); // Pre-skip
		builder.AddUInt32LE (48000); // Input sample rate
		builder.AddUInt16LE (0); // Output gain
		// Missing channel mapping family byte at position 18

		var page = TestBuilders.Ogg.CreatePage (builder.ToArray (), 0, OggPageFlags.BeginOfStream);
		var result = OggOpusFile.Read (page);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "short", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_ChannelMappingFamily1_MoreThan8Channels_ReturnsFailure ()
	{
		// RFC 7845 §5.1.1.2: Family 1 (Vorbis order) only allows 1-8 channels
		var data = TestBuilders.Opus.CreateFileWithChannelMapping (channels: 9, mappingFamily: 1);

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "channel", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_ChannelMappingFamily1_MissingMappingTable_ReturnsFailure ()
	{
		// RFC 7845 §5.1.1.2: Mapping table REQUIRED for family > 0
		var builder = new BinaryDataBuilder ();
		builder.Add (TestConstants.Magic.OpusHead);
		builder.Add ((byte)1); // Version
		builder.Add ((byte)4); // 4 channels
		builder.AddUInt16LE (312);
		builder.AddUInt32LE (48000);
		builder.AddUInt16LE (0);
		builder.Add ((byte)1); // Mapping family 1 (requires table)
		// Missing: stream count, coupled count, and 4-byte mapping table

		var page = TestBuilders.Ogg.CreatePage (builder.ToArray (), 0, OggPageFlags.BeginOfStream);
		var result = OggOpusFile.Read (page);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "short", StringComparison.OrdinalIgnoreCase);
	}

	// ==========================================================================
	// Error Path Tests
	// ==========================================================================

	[TestMethod]
	public void Read_InvalidChannelCount_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreateFileWithInvalidChannels (0);

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "channel", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_FirstPageNotBOS_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreateFileWithoutBosFlag ();

		var result = OggOpusFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "BOS", StringComparison.OrdinalIgnoreCase);
	}

	// ==========================================================================
	// SaveToFile Convenience Overload Tests
	// ==========================================================================

	[TestMethod]
	public void SaveToFile_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);
		// File was read from byte array, not from disk, so SourcePath is null

		var saveResult = result.File!.SaveToFile ();

		Assert.IsFalse (saveResult.IsSuccess);
		StringAssert.Contains (saveResult.Error!, "source path", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void SaveToFile_WithSourcePath_SavesBack ()
	{
		var tempPath = Path.GetTempFileName ();
		var data = TestBuilders.Opus.CreateMinimalFile ("Original", "Artist");
		File.WriteAllBytes (tempPath, data);

		try {
			var result = OggOpusFile.ReadFromFile (tempPath);
			result.File!.Title = "Updated";

			// Use the convenience overload that saves back to SourcePath
			var saveResult = result.File.SaveToFile ();

			Assert.IsTrue (saveResult.IsSuccess);

			var reread = OggOpusFile.ReadFromFile (tempPath);
			Assert.AreEqual ("Updated", reread.File!.Title);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);
		// File was read from byte array, not from disk, so SourcePath is null

		var saveResult = await result.File!.SaveToFileAsync ();

		Assert.IsFalse (saveResult.IsSuccess);
		StringAssert.Contains (saveResult.Error!, "source path", StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_SavesBack ()
	{
		var tempPath = Path.GetTempFileName ();
		var data = TestBuilders.Opus.CreateMinimalFile ("Original", "Artist");
		await File.WriteAllBytesAsync (tempPath, data);

		try {
			var result = await OggOpusFile.ReadFromFileAsync (tempPath);
			result.File!.Title = "Async Updated";

			// Use the convenience overload that saves back to SourcePath
			var saveResult = await result.File.SaveToFileAsync ();

			Assert.IsTrue (saveResult.IsSuccess, $"Save failed: {saveResult.Error}");

			var reread = await OggOpusFile.ReadFromFileAsync (tempPath);
			Assert.AreEqual ("Async Updated", reread.File!.Title);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task SaveToFileAsync_ToNewPath_SavesCorrectly ()
	{
		var originalPath = Path.GetTempFileName ();
		var newPath = Path.GetTempFileName ();
		var data = TestBuilders.Opus.CreateMinimalFile ("Original", "Artist");
		await File.WriteAllBytesAsync (originalPath, data);

		try {
			var result = await OggOpusFile.ReadFromFileAsync (originalPath);
			result.File!.Title = "Saved To New Path";

			// Use SaveToFileAsync with explicit path
			var saveResult = await result.File.SaveToFileAsync (newPath);

			Assert.IsTrue (saveResult.IsSuccess, $"Save failed: {saveResult.Error}");
			Assert.IsTrue (File.Exists (newPath));

			var reread = await OggOpusFile.ReadFromFileAsync (newPath);
			Assert.AreEqual ("Saved To New Path", reread.File!.Title);
		} finally {
			File.Delete (originalPath);
			File.Delete (newPath);
		}
	}

	// ==========================================================================
	// Large Multi-Page OpusTags Tests
	// ==========================================================================

	[TestMethod]
	public void Read_LargeMultiPageOpusTags_ParsesCorrectly ()
	{
		// Test with truly large OpusTags that span multiple pages (>65KB)
		// A typical Ogg page can hold ~65025 bytes (255 segments × 255 bytes)
		var data = TestBuilders.Opus.CreateFileWithMultiPageOpusTags (
			"Large Multi-Page Title",
			"Large Multi-Page Artist",
			paddingSize: 70000); // 70KB of padding to force multi-page

		var result = OggOpusFile.Read (data);

		Assert.IsTrue (result.IsSuccess, $"Failed to parse large multi-page OpusTags: {result.Error}");
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("Large Multi-Page Title", result.File!.Title);
		Assert.AreEqual ("Large Multi-Page Artist", result.File.Artist);
	}

	[TestMethod]
	public void Render_LargeMultiPageOpusTags_RoundTripsCorrectly ()
	{
		// Create file with large OpusTags, modify, render, and re-read
		var data = TestBuilders.Opus.CreateFileWithMultiPageOpusTags (
			"Original Title",
			"Original Artist",
			paddingSize: 70000);

		var result = OggOpusFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Modified Title After Large Padding";
		var rendered = result.File.Render (data);

		Assert.IsFalse (rendered.IsEmpty);

		var reread = OggOpusFile.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, $"Failed to re-read rendered file: {reread.Error}");
		Assert.AreEqual ("Modified Title After Large Padding", reread.File!.Title);
		Assert.AreEqual ("Original Artist", reread.File.Artist);
	}

	// ==========================================================================
	// Helper Methods
	// ==========================================================================

	static int FindLastOggPageOffset (ReadOnlySpan<byte> data)
	{
		var lastOffset = -1;
		for (var i = 0; i <= data.Length - 4; i++) {
			if (data[i] == 'O' && data[i + 1] == 'g' && data[i + 2] == 'g' && data[i + 3] == 'S')
				lastOffset = i;
		}
		return lastOffset;
	}

	static List<uint> ExtractSequenceNumbers (ReadOnlySpan<byte> data)
	{
		var sequenceNumbers = new List<uint> ();
		var offset = 0;

		while (offset <= data.Length - 27) {
			if (data[offset] == 'O' && data[offset + 1] == 'g' &&
				data[offset + 2] == 'g' && data[offset + 3] == 'S') {

				// Sequence number is at offset 18-21 (little-endian)
				var seqNum = (uint)(data[offset + 18] |
					(data[offset + 19] << 8) |
					(data[offset + 20] << 16) |
					(data[offset + 21] << 24));
				sequenceNumbers.Add (seqNum);

				// Skip to next page
				var segmentCount = data[offset + 26];
				var pageDataSize = 0;
				for (var i = 0; i < segmentCount && offset + 27 + i < data.Length; i++)
					pageDataSize += data[offset + 27 + i];

				offset += 27 + segmentCount + pageDataSize;
			} else {
				offset++;
			}
		}

		return sequenceNumbers;
	}
}
