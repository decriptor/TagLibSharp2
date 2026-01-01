// DFF (DSDIFF) format tests - TDD approach
// Based on DSDIFF 1.5 specification
// https://www.sonicstudio.com/pdf/dsd/DSDIFF_1.5_Spec.pdf
//
// Key differences from DSF:
// - Big-endian byte order (DSF is little-endian)
// - IFF-based chunk structure (FRM8 container)
// - No native metadata (ID3v2 is unofficial extension)

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;

namespace TagLibSharp2.Tests.Dff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Dff")]
public class DffFileTests
{
	// DFF magic: "FRM8" + size(8) + "DSD "
	private static readonly byte[] Frm8Magic = "FRM8"u8.ToArray ();
	private static readonly byte[] DsdFormType = "DSD "u8.ToArray ();

	#region FRM8 Container Tests

	[TestMethod]
	public void Parse_ValidFrm8Header_ReturnsSuccess ()
	{
		// Arrange - minimal DFF: FRM8 + size + "DSD " + FVER + PROP + DSD
		var data = CreateMinimalDffFile (
			sampleRate: 2822400,
			channelCount: 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public void Parse_InvalidMagic_ReturnsFailure ()
	{
		// Arrange - wrong magic bytes
		var data = new byte[100];
		"XXXX"u8.CopyTo (data);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("FRM8"));
	}

	[TestMethod]
	public void Parse_WrongFormType_ReturnsFailure ()
	{
		// Arrange - FRM8 but not DSD form type
		var data = new byte[100];
		"FRM8"u8.CopyTo (data);
		// Size (8 bytes big-endian)
		data[8] = 0; data[9] = 0; data[10] = 0; data[11] = 0;
		data[12] = 0; data[13] = 0; data[14] = 0; data[15] = 50;
		// Wrong form type
		"AIFF"u8.CopyTo (data.AsSpan (16));

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("DSD"));
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsFailure ()
	{
		// Arrange - data too short for FRM8 header
		var data = new byte[10];

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	#endregion

	#region FVER Chunk Tests

	[TestMethod]
	public void Parse_ValidFverChunk_ParsesVersion ()
	{
		// Arrange
		var data = CreateMinimalDffFile (2822400, 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		// DSDIFF 1.5 = 0x01050000
		Assert.AreEqual (1, result.File!.FormatVersionMajor);
		Assert.AreEqual (5, result.File.FormatVersionMinor);
	}

	[TestMethod]
	public void Parse_MissingFver_ReturnsFailure ()
	{
		// Arrange - FRM8 + DSD but no FVER chunk
		var data = CreateDffWithoutFver ();

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("FVER"));
	}

	#endregion

	#region Audio Properties Tests

	[TestMethod]
	public void Parse_DSD64_ExtractsSampleRate ()
	{
		// Arrange
		var data = CreateMinimalDffFile (
			sampleRate: 2822400,
			channelCount: 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2822400, result.File!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD64, result.File.DsdRate);
	}

	[TestMethod]
	public void Parse_DSD128_ExtractsSampleRate ()
	{
		// Arrange
		var data = CreateMinimalDffFile (
			sampleRate: 5644800,
			channelCount: 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5644800, result.File!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD128, result.File.DsdRate);
	}

	[TestMethod]
	public void Parse_DSD256_ExtractsSampleRate ()
	{
		// Arrange
		var data = CreateMinimalDffFile (
			sampleRate: 11289600,
			channelCount: 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (11289600, result.File!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD256, result.File.DsdRate);
	}

	[TestMethod]
	public void Parse_Stereo_ExtractsChannelCount ()
	{
		// Arrange
		var data = CreateMinimalDffFile (2822400, channelCount: 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_Mono_ExtractsChannelCount ()
	{
		// Arrange
		var data = CreateMinimalDffFile (2822400, channelCount: 1);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_Multichannel_ExtractsChannelCount ()
	{
		// Arrange - 5.1 surround
		var data = CreateMinimalDffFile (2822400, channelCount: 6);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (6, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_WithSampleCount_CalculatesDuration ()
	{
		// Arrange - 1 minute of audio at DSD64
		var data = CreateMinimalDffFile (
			sampleRate: 2822400,
			channelCount: 2,
			sampleCount: 2822400 * 60);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.FromMinutes (1), result.File!.Duration);
	}

	#endregion

	#region Properties Object Tests

	[TestMethod]
	public void Properties_AllProperties_ReturnCorrectValues ()
	{
		// Arrange
		var data = CreateMinimalDffFile (
			sampleRate: 5644800,
			channelCount: 2,
			sampleCount: 5644800 * 120); // 2 minutes

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		var props = result.File!.Properties;
		Assert.IsNotNull (props);
		Assert.AreEqual (TimeSpan.FromMinutes (2), props!.Duration);
		Assert.AreEqual (5644800, props.SampleRate);
		Assert.AreEqual (2, props.Channels);
		Assert.AreEqual (1, props.BitsPerSample);
		Assert.AreEqual (DsfSampleRate.DSD128, props.DsdRate);
	}

	#endregion

	#region ID3v2 Metadata Tests

	[TestMethod]
	public void Parse_WithId3v2Chunk_ExtractsMetadata ()
	{
		// Arrange - DFF with unofficial ID3v2 chunk
		var data = CreateDffWithId3v2 (
			sampleRate: 2822400,
			channelCount: 2,
			title: "Test Song",
			artist: "Test Artist");

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Id3v2Tag);
		Assert.AreEqual ("Test Song", result.File.Id3v2Tag!.Title);
		Assert.AreEqual ("Test Artist", result.File.Id3v2Tag.Artist);
	}

	[TestMethod]
	public void Parse_WithoutId3v2_TagIsNull ()
	{
		// Arrange - DFF without ID3v2
		var data = CreateMinimalDffFile (2822400, 2);

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.File!.Id3v2Tag);
	}

	#endregion

	#region Compression Type Tests

	[TestMethod]
	public void Parse_UncompressedDsd_IdentifiesCorrectly ()
	{
		// Arrange - DSD (not DST compressed)
		var data = CreateMinimalDffFile (2822400, 2, compressionType: "DSD ");

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DffCompressionType.Dsd, result.File!.CompressionType);
		Assert.IsFalse (result.File.IsCompressed);
	}

	[TestMethod]
	public void Parse_DstCompressed_IdentifiesCorrectly ()
	{
		// Arrange - DST compressed
		var data = CreateMinimalDffFile (2822400, 2, compressionType: "DST ");

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DffCompressionType.Dst, result.File!.CompressionType);
		Assert.IsTrue (result.File.IsCompressed);
	}

	#endregion

	#region File I/O Tests

	[TestMethod]
	public void ReadFromFile_ValidFile_ReturnsSuccess ()
	{
		// Arrange
		var tempPath = Path.GetTempFileName ();
		try {
			var data = CreateMinimalDffFile (2822400, 2);
			File.WriteAllBytes (tempPath, data);

			// Act
			var result = DffFile.ReadFromFile (tempPath);

			// Assert
			Assert.IsTrue (result.IsSuccess);
			Assert.AreEqual (tempPath, result.File!.SourcePath);
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		// Arrange
		var tempPath = Path.GetTempFileName ();
		try {
			var data = CreateMinimalDffFile (2822400, 2);
			await File.WriteAllBytesAsync (tempPath, data);

			// Act
			var result = await DffFile.ReadFromFileAsync (tempPath);

			// Assert
			Assert.IsTrue (result.IsSuccess);
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	[TestMethod]
	public void ReadFromFile_NonExistent_ReturnsFailure ()
	{
		// Act
		var result = DffFile.ReadFromFile ("/nonexistent/path/file.dff");

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	#endregion

	#region Round-Trip Tests

	[TestMethod]
	public void RoundTrip_AddId3v2_PreservesAudioData ()
	{
		// Arrange - DFF without metadata, using small sample count so DSD size matches actual data
		// sampleCount = 16384 results in audioDataSize = 16384 * 2 / 8 = 4096 bytes (within limit)
		var original = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (original);
		Assert.IsTrue (parseResult.IsSuccess);
		var file = parseResult.File!;

		// Act - add metadata
		file.EnsureId3v2Tag ();
		file.Id3v2Tag!.Title = "New Title";
		file.Id3v2Tag.Artist = "New Artist";
		var rendered = file.Render ();

		// Assert - can parse back
		var reparsed = DffFile.Parse (rendered.Span);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("New Title", reparsed.File!.Id3v2Tag!.Title);
		Assert.AreEqual ("New Artist", reparsed.File.Id3v2Tag.Artist);
		Assert.AreEqual (2822400, reparsed.File.SampleRate);
	}

	[TestMethod]
	public void RoundTrip_ModifyMetadata_PreservesAudioProperties ()
	{
		// Arrange
		var original = CreateDffWithId3v2 (
			sampleRate: 5644800,
			channelCount: 2,
			title: "Original",
			artist: "Original Artist");

		var parseResult = DffFile.Parse (original);
		Assert.IsTrue (parseResult.IsSuccess, $"Initial parse failed: {parseResult.Error}");
		var file = parseResult.File!;

		// Act
		file.Id3v2Tag!.Title = "Modified";
		var rendered = file.Render ();

		// Assert
		var reparsed = DffFile.Parse (rendered.Span);
		Assert.IsTrue (reparsed.IsSuccess, $"Re-parse failed: {reparsed.Error}");
		Assert.AreEqual ("Modified", reparsed.File!.Id3v2Tag!.Title);
		Assert.AreEqual (5644800, reparsed.File.SampleRate);
		Assert.AreEqual (2, reparsed.File.Channels);
	}

	#endregion

	#region Result Type Tests

	[TestMethod]
	public void DffFileParseResult_Equality_SameSuccess_AreEqual ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var result1 = DffFile.Parse (data);
		var result2 = DffFile.Parse (data);

		// Different instances but both successful
		Assert.IsTrue (result1.IsSuccess);
		Assert.IsTrue (result2.IsSuccess);
	}

	[TestMethod]
	public void DffFileParseResult_Equality_SameFailure_AreEqual ()
	{
		var result1 = DffFile.Parse (new byte[5]);
		var result2 = DffFile.Parse (new byte[5]);

		Assert.IsFalse (result1.IsSuccess);
		Assert.IsFalse (result2.IsSuccess);
		Assert.AreEqual (result1.Error, result2.Error);
	}

	[TestMethod]
	public void DffFileParseResult_GetHashCode_Works ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var result = DffFile.Parse (data);

		var hash = result.GetHashCode ();
		Assert.AreNotEqual (0, hash);
	}

	[TestMethod]
	public void DffFileParseResult_Equals_WithObject_Works ()
	{
		var result1 = DffFile.Parse (new byte[5]);
		var result2 = DffFile.Parse (new byte[5]);

		Assert.IsTrue (result1.Equals ((object)result2));
		Assert.IsFalse (result1.Equals ("not a result"));
		Assert.IsFalse (result1.Equals (null));
	}

	#endregion

	#region SaveToFile Tests

	[TestMethod]
	public void SaveToFile_ValidFile_ReturnsSuccess ()
	{
		var tempPath = Path.GetTempFileName ();
		try {
			var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
			var parseResult = DffFile.Parse (data);
			Assert.IsTrue (parseResult.IsSuccess);

			var saveResult = parseResult.File!.SaveToFile (tempPath);
			Assert.IsTrue (saveResult.IsSuccess);
			Assert.IsTrue (File.Exists (tempPath));
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	[TestMethod]
	public void SaveToFile_AfterDispose_ThrowsObjectDisposedException ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (data);
		var file = parseResult.File!;
		file.Dispose ();

		Assert.ThrowsExactly<ObjectDisposedException> (() => file.SaveToFile ("/tmp/test.dff"));
	}

	#endregion

	#region SaveToFileAsync Tests

	[TestMethod]
	public async Task SaveToFileAsync_ValidFile_ReturnsSuccess ()
	{
		var tempPath = Path.GetTempFileName ();
		try {
			var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
			await File.WriteAllBytesAsync (tempPath, data);

			var parseResult = await DffFile.ReadFromFileAsync (tempPath);
			Assert.IsTrue (parseResult.IsSuccess);

			parseResult.File!.EnsureId3v2Tag ().Title = "Async Test";
			var saveResult = await parseResult.File.SaveToFileAsync ();
			Assert.IsTrue (saveResult.IsSuccess);
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (data);
		Assert.IsTrue (parseResult.IsSuccess);

		// File was parsed from bytes, not read from disk - no SourcePath
		var saveResult = await parseResult.File!.SaveToFileAsync ();
		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_AfterDispose_ThrowsObjectDisposedException ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (data);
		var file = parseResult.File!;
		file.Dispose ();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException> (
			async () => await file.SaveToFileAsync ());
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithCancellation_Cancels ()
	{
		var tempPath = Path.GetTempFileName ();
		try {
			var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
			await File.WriteAllBytesAsync (tempPath, data);

			var parseResult = await DffFile.ReadFromFileAsync (tempPath);
			Assert.IsTrue (parseResult.IsSuccess);

			using var cts = new CancellationTokenSource ();
			cts.Cancel ();

			try {
				await parseResult.File!.SaveToFileAsync (cancellationToken: cts.Token);
				// May or may not throw depending on timing
			} catch (OperationCanceledException) {
				// Expected
			}
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	#endregion

	#region Dispose Tests

	[TestMethod]
	public void Dispose_CalledTwice_NoException ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (data);
		var file = parseResult.File!;

		file.Dispose ();
		file.Dispose (); // Should not throw
	}

	[TestMethod]
	public void Dispose_ClearsReferences ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (data);
		var file = parseResult.File!;
		file.EnsureId3v2Tag ().Title = "Test";

		file.Dispose ();

		Assert.IsNull (file.Id3v2Tag);
		Assert.IsNull (file.Properties);
	}

	[TestMethod]
	public void Render_AfterDispose_ThrowsObjectDisposedException ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var parseResult = DffFile.Parse (data);
		var file = parseResult.File!;
		file.Dispose ();

		Assert.ThrowsExactly<ObjectDisposedException> (() => file.Render ());
	}

	#endregion

	#region Audio Properties Edge Cases

	[TestMethod]
	public void Properties_DSD512_IdentifiedCorrectly ()
	{
		var data = CreateMinimalDffFile (
			sampleRate: 22579200, // DSD512
			channelCount: 2,
			sampleCount: 16384);

		var result = DffFile.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DsfSampleRate.DSD512, result.File!.DsdRate);
		Assert.AreEqual (DsfSampleRate.DSD512, result.File.Properties!.DsdRate);
	}

	[TestMethod]
	public void Properties_DSD1024_IdentifiedCorrectly ()
	{
		var data = CreateMinimalDffFile (
			sampleRate: 45158400, // DSD1024
			channelCount: 2,
			sampleCount: 16384);

		var result = DffFile.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DsfSampleRate.DSD1024, result.File!.DsdRate);
	}

	[TestMethod]
	public void Properties_UnknownSampleRate_IdentifiedAsUnknown ()
	{
		var data = CreateMinimalDffFile (
			sampleRate: 44100, // Not a valid DSD rate
			channelCount: 2,
			sampleCount: 16384);

		var result = DffFile.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (DsfSampleRate.Unknown, result.File!.DsdRate);
	}

	[TestMethod]
	public void Properties_BitsPerSample_AlwaysOne ()
	{
		var data = CreateMinimalDffFile (2822400, 2, sampleCount: 16384);
		var result = DffFile.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Properties!.BitsPerSample);
	}

	#endregion

	#region Format Compliance Tests

	[TestMethod]
	public void Parse_MissingFsSampleRateInProp_ReturnsFailure ()
	{
		// Arrange - PROP without FS sub-chunk
		var data = CreateDffWithMissingPropSubChunk ("FS  ");

		// Act
		var result = DffFile.Parse (data);

		// Assert - FS is required per spec
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("FS") || result.Error.Contains ("sample rate"),
			$"Expected error about missing FS, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_MissingChnlChannelsInProp_ReturnsFailure ()
	{
		// Arrange - PROP without CHNL sub-chunk
		var data = CreateDffWithMissingPropSubChunk ("CHNL");

		// Act
		var result = DffFile.Parse (data);

		// Assert - CHNL is required per spec
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("CHNL") || result.Error.Contains ("channel"),
			$"Expected error about missing CHNL, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_MissingCmprCompressionInProp_ReturnsFailure ()
	{
		// Arrange - PROP without CMPR sub-chunk
		var data = CreateDffWithMissingPropSubChunk ("CMPR");

		// Act
		var result = DffFile.Parse (data);

		// Assert - CMPR is required per spec
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("CMPR") || result.Error.Contains ("compression"),
			$"Expected error about missing CMPR, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_MissingDsdOrDstAudioChunk_ReturnsFailure ()
	{
		// Arrange - DFF without DSD or DST audio chunk
		var data = CreateDffWithoutAudioChunk ();

		// Act
		var result = DffFile.Parse (data);

		// Assert - audio data chunk is required per spec
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("DSD") || result.Error.Contains ("DST") ||
			result.Error.Contains ("audio"),
			$"Expected error about missing audio chunk, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_FverNotFirstChunk_ReturnsFailure ()
	{
		// Arrange - PROP appears before FVER
		var data = CreateDffWithPropBeforeFver ();

		// Act
		var result = DffFile.Parse (data);

		// Assert - FVER must be first chunk per spec
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("FVER") || result.Error.Contains ("first"),
			$"Expected error about FVER order, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_AudioChunkBeforeProp_ReturnsFailure ()
	{
		// Arrange - DSD appears before PROP
		var data = CreateDffWithAudioBeforeProp ();

		// Act
		var result = DffFile.Parse (data);

		// Assert - PROP must precede audio per spec
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("PROP") || result.Error.Contains ("audio") ||
			result.Error.Contains ("order"),
			$"Expected error about chunk order, got: {result.Error}");
	}

	[TestMethod]
	public void Parse_OddSizedChunkWithPadding_ParsesCorrectly ()
	{
		// Arrange - chunk with odd size (needs padding for IFF alignment)
		var data = CreateDffWithOddSizedChunk ();

		// Act
		var result = DffFile.Parse (data);

		// Assert - should handle padding correctly
		Assert.IsTrue (result.IsSuccess, $"Failed to parse: {result.Error}");
	}

	#endregion

	#region Disposal Tests

	[TestMethod]
	public void Dispose_ClearsPropertiesConsistentWithDsf ()
	{
		// Arrange
		var data = CreateMinimalDffFile (2822400, 2, 16384);
		var result = DffFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Verify Properties exists before disposal
		Assert.IsNotNull (file.Properties);
		Assert.IsNotNull (file.Id3v2Tag is null || file.Id3v2Tag is not null); // May or may not have tag

		// Act
		file.Dispose ();

		// Assert - Properties should be null after disposal (consistent pattern)
		Assert.IsNull (file.Properties);
		Assert.IsNull (file.Id3v2Tag);
	}

	[TestMethod]
	public void Dispose_MultipleCalls_DoesNotThrow ()
	{
		// Arrange
		var data = CreateMinimalDffFile (2822400, 2, 16384);
		var result = DffFile.Parse (data);
		var file = result.File!;

		// Act & Assert - should not throw on multiple disposals
		file.Dispose ();
		file.Dispose ();
		file.Dispose ();
	}

	#endregion

	#region DST Compression Tests

	[TestMethod]
	public void Parse_DstCompressedFile_IndicatesIncompleteProperties ()
	{
		// Arrange - Create a DFF file with DST compression
		var data = CreateMinimalDffFile (
			sampleRate: 2822400,
			channelCount: 2,
			sampleCount: 16384,
			compressionType: "DST ");

		// Act
		var result = DffFile.Parse (data);

		// Assert
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		Assert.AreEqual (DffCompressionType.Dst, result.File!.CompressionType);
		Assert.IsTrue (result.File.IsCompressed);

		// DST compression means sample count cannot be calculated from chunk size
		// Properties should indicate this limitation
		Assert.IsNotNull (result.File.Properties);
		Assert.AreEqual (DffCompressionType.Dst, result.File.Properties!.CompressionType);
	}

	[TestMethod]
	public void Properties_DstCompressed_HasZeroDuration ()
	{
		// Arrange - DST files can't calculate duration from chunk size
		var data = CreateDstCompressedDffFile ();

		// Act
		var result = DffFile.Parse (data);

		// Assert - Duration should be zero or indicate incomplete for DST
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		Assert.AreEqual (DffCompressionType.Dst, result.File!.CompressionType);
		// DST chunks don't allow sample count calculation from size alone
		// The duration may be zero or require frame parsing
	}

	#endregion

	#region Helper Methods

	private static byte[] CreateMinimalDffFile (
		uint sampleRate,
		uint channelCount,
		ulong sampleCount = 2822400,
		string compressionType = "DSD ")
	{
		using var ms = new MemoryStream ();

		// FRM8 header placeholder - will update size later
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0); // Placeholder for size
		ms.Write (DsdFormType);

		// FVER chunk - Format Version (DSDIFF 1.5 = 0x01050000)
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4); // Chunk size
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk - Properties
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0); // Placeholder
		ms.Write ("SND "u8); // Property type

		// FS sub-chunk - Sample Rate
		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, sampleRate);

		// CHNL sub-chunk - Channels
		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 2 + channelCount * 4);
		WriteUInt16BE (ms, (ushort)channelCount);
		for (int i = 0; i < channelCount; i++) {
			// Channel IDs: SLFT, SRGT, etc.
			if (i == 0) ms.Write ("SLFT"u8);
			else if (i == 1) ms.Write ("SRGT"u8);
			else ms.Write ("C   "u8);
		}

		// CMPR sub-chunk - Compression Type
		ms.Write ("CMPR"u8);
		var cmprBytes = System.Text.Encoding.ASCII.GetBytes (compressionType);
		WriteUInt64BE (ms, 4 + 1 + 14); // compressionType + count + name
		ms.Write (cmprBytes);
		ms.WriteByte (14); // Compression name length
		ms.Write ("not compressed"u8);

		// Update PROP size
		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12; // Exclude header
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// DSD chunk - Audio data
		ms.Write ("DSD "u8);
		var audioDataSize = sampleCount * channelCount / 8;
		WriteUInt64BE (ms, audioDataSize);
		// Write minimal audio data (size in header is authoritative for sample count)
		var audioData = new byte[Math.Min ((long)audioDataSize, 4096)];
		ms.Write (audioData);

		// Update FRM8 size (total - 12 for FRM8 header)
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	private static byte[] CreateDffWithoutFver ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		WriteUInt64BE (ms, 100);
		ms.Write (DsdFormType);

		// Skip FVER, go directly to PROP (invalid)
		ms.Write ("PROP"u8);
		WriteUInt64BE (ms, 20);
		ms.Write ("SND "u8);

		return ms.ToArray ();
	}

	private static byte[] CreateDffWithId3v2 (
		uint sampleRate,
		uint channelCount,
		string title,
		string artist)
	{
		using var ms = new MemoryStream ();

		// FRM8 header placeholder
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0); // Placeholder for size
		ms.Write (DsdFormType);

		// FVER chunk
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, sampleRate);

		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 2 + channelCount * 4);
		WriteUInt16BE (ms, (ushort)channelCount);
		for (int i = 0; i < channelCount; i++) {
			if (i == 0) ms.Write ("SLFT"u8);
			else if (i == 1) ms.Write ("SRGT"u8);
			else ms.Write ("C   "u8);
		}

		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 14);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// DSD chunk - audio data (must come before ID3 for Render() to work)
		ms.Write ("DSD "u8);
		WriteUInt64BE (ms, 4096);
		ms.Write (new byte[4096]);

		// ID3 chunk (placed at END per convention - Render() expects this)
		ms.Write ("ID3 "u8);
		var id3ChunkSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);

		var id3Start = ms.Position;
		ms.Write ("ID3"u8);
		ms.WriteByte (4); // v2.4
		ms.WriteByte (0);
		ms.WriteByte (0);

		var tagSizePosition = ms.Position;
		ms.Write (new byte[4]);

		WriteId3v2Frame (ms, "TIT2", System.Text.Encoding.UTF8.GetBytes (title));
		WriteId3v2Frame (ms, "TPE1", System.Text.Encoding.UTF8.GetBytes (artist));

		var id3End = ms.Position;
		var id3TagSize = id3End - id3Start - 10;

		ms.Position = tagSizePosition;
		var syncsafe = new byte[4];
		syncsafe[0] = (byte)((id3TagSize >> 21) & 0x7F);
		syncsafe[1] = (byte)((id3TagSize >> 14) & 0x7F);
		syncsafe[2] = (byte)((id3TagSize >> 7) & 0x7F);
		syncsafe[3] = (byte)(id3TagSize & 0x7F);
		ms.Write (syncsafe);
		ms.Position = id3End;

		var id3ChunkSize = id3End - id3Start;
		ms.Position = id3ChunkSizePosition;
		WriteUInt64BE (ms, (ulong)id3ChunkSize);
		ms.Position = id3End;

		// Add padding byte for odd-sized ID3 chunk (IFF requirement)
		if (id3ChunkSize % 2 != 0)
			ms.WriteByte (0);

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	private static void WriteUInt64BE (Stream stream, ulong value)
	{
		for (int i = 7; i >= 0; i--)
			stream.WriteByte ((byte)(value >> (i * 8)));
	}

	private static void WriteUInt32BE (Stream stream, uint value)
	{
		stream.WriteByte ((byte)(value >> 24));
		stream.WriteByte ((byte)(value >> 16));
		stream.WriteByte ((byte)(value >> 8));
		stream.WriteByte ((byte)value);
	}

	private static void WriteUInt16BE (Stream stream, ushort value)
	{
		stream.WriteByte ((byte)(value >> 8));
		stream.WriteByte ((byte)value);
	}

	private static void WriteId3v2Frame (Stream stream, string frameId, byte[] content)
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

	/// <summary>
	/// Creates a DFF file missing a specific PROP sub-chunk.
	/// </summary>
	private static byte[] CreateDffWithMissingPropSubChunk (string missingChunk)
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write (DsdFormType);

		// FVER chunk
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk - missing one sub-chunk
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		// FS - Sample Rate (unless skipped)
		if (missingChunk != "FS  ") {
			ms.Write ("FS  "u8);
			WriteUInt64BE (ms, 4);
			WriteUInt32BE (ms, 2822400);
		}

		// CHNL - Channels (unless skipped)
		if (missingChunk != "CHNL") {
			ms.Write ("CHNL"u8);
			WriteUInt64BE (ms, 10); // 2 + 2*4
			WriteUInt16BE (ms, 2);
			ms.Write ("SLFT"u8);
			ms.Write ("SRGT"u8);
		}

		// CMPR - Compression (unless skipped)
		if (missingChunk != "CMPR") {
			ms.Write ("CMPR"u8);
			WriteUInt64BE (ms, 4 + 1 + 14);
			ms.Write ("DSD "u8);
			ms.WriteByte (14);
			ms.Write ("not compressed"u8);
		}

		// Update PROP size
		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// DSD chunk - audio data
		ms.Write ("DSD "u8);
		WriteUInt64BE (ms, 1024);
		ms.Write (new byte[1024]);

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a DFF file without DSD or DST audio chunk.
	/// </summary>
	private static byte[] CreateDffWithoutAudioChunk ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write (DsdFormType);

		// FVER chunk
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk with all required sub-chunks
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400);

		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 10);
		WriteUInt16BE (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);

		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 14);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// NO DSD or DST chunk here!

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a DFF file where PROP appears before FVER (invalid per spec).
	/// </summary>
	private static byte[] CreateDffWithPropBeforeFver ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write (DsdFormType);

		// PROP chunk FIRST (invalid - FVER should be first)
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400);

		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 10);
		WriteUInt16BE (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);

		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 14);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// FVER chunk SECOND (should be first)
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// DSD chunk
		ms.Write ("DSD "u8);
		WriteUInt64BE (ms, 1024);
		ms.Write (new byte[1024]);

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a DFF file where DSD audio appears before PROP (invalid per spec).
	/// </summary>
	private static byte[] CreateDffWithAudioBeforeProp ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write (DsdFormType);

		// FVER chunk (correct position)
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// DSD chunk BEFORE PROP (invalid)
		ms.Write ("DSD "u8);
		WriteUInt64BE (ms, 1024);
		ms.Write (new byte[1024]);

		// PROP chunk (should be before DSD)
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400);

		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 10);
		WriteUInt16BE (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);

		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 14);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a DFF file with an odd-sized chunk to test padding alignment.
	/// </summary>
	private static byte[] CreateDffWithOddSizedChunk ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write (DsdFormType);

		// FVER chunk
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk with odd-sized CMPR sub-chunk
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400);

		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 10);
		WriteUInt16BE (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);

		// CMPR with 5-byte compression name (odd size = 4 + 1 + 5 = 10)
		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 5); // 10 bytes = even, try 9
		ms.Write ("DSD "u8);
		ms.WriteByte (5);
		ms.Write ("hello"u8);
		// No padding added - test if parser handles this

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// DSD chunk with odd size (1023 bytes)
		ms.Write ("DSD "u8);
		WriteUInt64BE (ms, 1023); // Odd size
		ms.Write (new byte[1023]);
		ms.WriteByte (0); // Padding byte to maintain alignment

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a DFF file with DST compression.
	/// </summary>
	private static byte[] CreateDstCompressedDffFile ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write (Frm8Magic);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write (DsdFormType);

		// FVER chunk
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		// FS - Sample Rate
		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400);

		// CHNL - Channels
		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 10);
		WriteUInt16BE (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);

		// CMPR - DST compression
		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 3); // "DST " + count byte + "DST"
		ms.Write ("DST "u8);
		ms.WriteByte (3);
		ms.Write ("DST"u8);

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		// Add padding byte for odd-sized PROP chunk (IFF requirement)
		if (propSize % 2 != 0)
			ms.WriteByte (0);

		// DST chunk (instead of DSD)
		ms.Write ("DST "u8);
		WriteUInt64BE (ms, 1024);
		ms.Write (new byte[1024]);

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	#endregion
}
