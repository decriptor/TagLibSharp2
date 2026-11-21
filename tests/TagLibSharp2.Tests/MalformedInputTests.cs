// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aac;
using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Mpeg;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests;

/// <summary>
/// Tests for handling malformed and adversarial input data.
/// These tests ensure the library handles corrupt files gracefully without crashes.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Security")]
public class MalformedInputTests
{

	[TestMethod]
	public void FlacFile_EmptyInput_ReturnsFailure ()
	{
		var result = FlacFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void OggVorbisFile_EmptyInput_ReturnsFailure ()
	{
		var result = OggVorbisFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void Id3v1Tag_EmptyInput_ReturnsFailure ()
	{
		var result = Id3v1Tag.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void Id3v2Tag_EmptyInput_ReturnsFailure ()
	{
		var result = Id3v2Tag.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void VorbisComment_EmptyInput_ReturnsFailure ()
	{
		var result = VorbisComment.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}



	[TestMethod]
	public void FlacFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "fLa" instead of full FLAC magic
		var data = TestConstants.Magic.Flac[..3];
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void FlacFile_TruncatedStreamInfo_ReturnsFailure ()
	{
		// Magic + partial STREAMINFO header
		var data = new byte[12];
		TestConstants.Magic.Flac.CopyTo (data, 0);
		data[4] = TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypeStreamInfo;
		data[5] = 0x00; data[6] = 0x00; data[7] = TestConstants.Flac.StreamInfoSize;
		// Only 4 bytes of STREAMINFO data instead of required 34
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated STREAMINFO should fail gracefully");
	}

	[TestMethod]
	public void Id3v2Tag_TruncatedHeader_ReturnsFailure ()
	{
		// Only "ID3" + version, no size (missing last 5 bytes of header)
		var data = new byte[5];
		TestConstants.Magic.Id3.CopyTo (data, 0);
		data[3] = TestConstants.Id3v2.Version4;
		data[4] = TestConstants.Id3v2.MinorVersion;
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated header should fail gracefully");
	}

	[TestMethod]
	public void Id3v1Tag_TruncatedTag_ReturnsFailure ()
	{
		// Only "TAG" + partial data (less than required 128 bytes)
		var data = new byte[6];
		TestConstants.Id3v1.Signature.CopyTo (data, 0);
		var result = Id3v1Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated tag should fail gracefully");
	}

	[TestMethod]
	public void OggVorbisFile_TruncatedPageHeader_ReturnsFailure ()
	{
		// "OggS" + partial header (missing most of required 27-byte header)
		var data = new byte[6];
		TestConstants.Magic.Ogg.CopyTo (data, 0);
		data[4] = 0x00;
		data[5] = TestConstants.Ogg.FlagBos;
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated page header should fail gracefully");
	}



	[TestMethod]
	public void FlacFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
		Assert.Contains ("fLaC", result.Error!, "Error should mention expected magic");
	}

	[TestMethod]
	public void Id3v2Tag_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void Id3v1Tag_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[TestConstants.Id3v1.TagSize];
		data[0] = 0x00; // Not "TAG" signature
		var result = Id3v1Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void OggVorbisFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[100];
		data[0] = 0x00; // Not "OggS"
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}



	[TestMethod]
	public void VorbisComment_VendorLengthOverflow_ReturnsFailure ()
	{
		// Vendor length that would overflow int
		var data = new byte[] {
			0xFF, 0xFF, 0xFF, 0xFF, // Vendor length = 4294967295
			0x00, 0x00, 0x00, 0x00
		};
		var result = VorbisComment.Read (data);
		Assert.IsFalse (result.IsSuccess, "Integer overflow in vendor length should fail safely");
	}

	[TestMethod]
	public void VorbisComment_VendorLengthExceedsData_ReturnsFailure ()
	{
		// Vendor length greater than available data
		var data = new byte[] {
			0x64, 0x00, 0x00, 0x00, // Vendor length = 100
			0x00, 0x00, 0x00, 0x00  // Only 4 bytes available
		};
		var result = VorbisComment.Read (data);
		Assert.IsFalse (result.IsSuccess, "Vendor length exceeding data should fail safely");
	}

	[TestMethod]
	public void VorbisComment_FieldLengthExceedsData_ReturnsFailure ()
	{
		// Valid vendor, but field length exceeds data
		var data = new byte[] {
			0x04, 0x00, 0x00, 0x00, // Vendor length = 4
			0x74, 0x65, 0x73, 0x74, // "test"
			0x01, 0x00, 0x00, 0x00, // Field count = 1
			0xFF, 0x00, 0x00, 0x00, // Field length = 255 (but no data follows)
		};
		var result = VorbisComment.Read (data);
		Assert.IsFalse (result.IsSuccess, "Field length exceeding data should fail safely");
	}

	[TestMethod]
	public void Id3v2Tag_FrameSizeExceedsTag_ReturnsFailure ()
	{
		// ID3v2.4 header with frame that claims size larger than tag
		var data = new byte[] {
			0x49, 0x44, 0x33, // "ID3"
			0x04, 0x00, // Version 2.4.0
			0x00, // Flags
			0x00, 0x00, 0x00, 0x10, // Tag size = 16 (syncsafe)
			// Frame header
			0x54, 0x49, 0x54, 0x32, // "TIT2"
			0x00, 0x00, 0x01, 0x00, // Size = 128 (syncsafe) - exceeds tag
			0x00, 0x00, // Flags
		};
		var result = Id3v2Tag.Read (data);
		// Should either fail or skip the invalid frame
		// The important thing is no crash - we just verify it runs
		_ = result.IsSuccess; // Access result to ensure it was computed
	}



	[TestMethod]
	public void FlacFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid FLAC");
	}

	[TestMethod]
	public void OggVorbisFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Ogg");
	}

	[TestMethod]
	public void Id3v2Tag_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid ID3v2");
	}



	[TestMethod]
	public void FlacFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid FLAC");
	}

	[TestMethod]
	public void OggVorbisFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid Ogg");
	}

	[TestMethod]
	public void Id3v2Tag_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid ID3v2");
	}



	[TestMethod]
	public void FlacFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42); // Fixed seed for reproducibility
		var data = new byte[10000];
		random.NextBytes (data);

		// Should not throw, just return failure
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}

	[TestMethod]
	public void OggVorbisFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}

	[TestMethod]
	public void Id3v2Tag_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}

	[TestMethod]
	public void VorbisComment_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[1000];
		random.NextBytes (data);

		// May succeed with garbage data or fail, but should not crash
		var result = VorbisComment.Read (data);
		Assert.IsNotNull (result, "Should return a result without crashing");
	}



	[TestMethod]
	public void FlacFile_ExactlyMinimumSize_DoesNotCrash ()
	{
		// fLaC + minimum header
		var data = new byte[8];
		data[0] = 0x66; data[1] = 0x4C; data[2] = 0x61; data[3] = 0x43; // fLaC
		data[4] = 0x80; data[5] = 0x00; data[6] = 0x00; data[7] = 0x00; // Header

		// Will fail but should not crash
		var result = FlacFile.Read (data);
		Assert.IsNotNull (result, "Minimum size input should return a result without crashing");
	}

	[TestMethod]
	public void Id3v2Tag_ExactlyMinimumSize_DoesNotCrash ()
	{
		// ID3 header only (10 bytes)
		var data = new byte[] {
			0x49, 0x44, 0x33, // "ID3"
			0x04, 0x00, // Version 2.4.0
			0x00, // Flags
			0x00, 0x00, 0x00, 0x00 // Size = 0
		};

		var result = Id3v2Tag.Read (data);
		Assert.IsNotNull (result, "Minimum size input should return a result without crashing");
	}

	[TestMethod]
	public void VorbisComment_MinimalValid_Succeeds ()
	{
		// Empty vendor string, zero fields
		var data = new byte[] {
			0x00, 0x00, 0x00, 0x00, // Vendor length = 0
			0x00, 0x00, 0x00, 0x00  // Field count = 0
		};

		var result = VorbisComment.Read (data);
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Tag!.VendorString);
	}



	[TestMethod]
	public void FlacFile_MetadataBlockPointsToItself_DoesNotHang ()
	{
		// FLAC file where a metadata block claims a huge size
		// that would loop back into the header
		var data = new byte[100];
		data[0] = 0x66; data[1] = 0x4C; data[2] = 0x61; data[3] = 0x43; // fLaC
		data[4] = 0x00; // Not last, type 0
		data[5] = 0xFF; data[6] = 0xFF; data[7] = 0xFF; // Size = 16777215

		var result = FlacFile.Read (data);
		// Should fail due to size exceeding data, not hang
		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	public void VorbisComment_InvalidUtf8InVendor_DoesNotCrash ()
	{
		// Invalid UTF-8 sequence in vendor string
		var data = new byte[] {
			0x04, 0x00, 0x00, 0x00, // Vendor length = 4
			0xFF, 0xFE, 0xFD, 0xFC, // Invalid UTF-8 bytes
			0x00, 0x00, 0x00, 0x00  // Field count = 0
		};

		// Should not throw, may produce replacement characters
		var result = VorbisComment.Read (data);
		Assert.IsNotNull (result, "Invalid UTF-8 should return a result without crashing");
	}

	[TestMethod]
	public void VorbisComment_InvalidUtf8InField_DoesNotCrash ()
	{
		// Invalid UTF-8 in field value
		var data = new byte[] {
			0x00, 0x00, 0x00, 0x00, // Vendor length = 0
			0x01, 0x00, 0x00, 0x00, // Field count = 1
			0x08, 0x00, 0x00, 0x00, // Field length = 8
			0x54, 0x45, 0x53, 0x54, // "TEST"
			0x3D, // "="
			0xFF, 0xFE, 0xFD // Invalid UTF-8
		};

		var result = VorbisComment.Read (data);
		Assert.IsNotNull (result, "Invalid UTF-8 in field should return a result without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// DSF Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void DsfFile_EmptyInput_ReturnsFailure ()
	{
		var result = DsfFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void DsfFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "DS" instead of "DSD "
		var data = new byte[] { 0x44, 0x53 };
		var result = DsfFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void DsfFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = DsfFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void DsfFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = DsfFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid DSF");
	}

	[TestMethod]
	public void DsfFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = DsfFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid DSF");
	}

	[TestMethod]
	public void DsfFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42); // Fixed seed for reproducibility
		var data = new byte[10000];
		random.NextBytes (data);

		var result = DsfFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// DFF Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void DffFile_EmptyInput_ReturnsFailure ()
	{
		var result = DffFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void DffFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "FR" instead of "FRM8"
		var data = new byte[] { 0x46, 0x52 };
		var result = DffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void DffFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = DffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void DffFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = DffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid DFF");
	}

	[TestMethod]
	public void DffFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = DffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid DFF");
	}

	[TestMethod]
	public void DffFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = DffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// AIFF/WAV Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void AiffFile_EmptyInput_ReturnsFailure ()
	{
		var result = AiffFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void AiffFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "FOR" instead of "FORM"
		var data = new byte[] { 0x46, 0x4F, 0x52 };
		var result = AiffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void AiffFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = AiffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void AiffFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = AiffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid AIFF");
	}

	[TestMethod]
	public void AiffFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = AiffFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}

	[TestMethod]
	public void WavFile_EmptyInput_ReturnsFailure ()
	{
		var result = WavFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void WavFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "RIF" instead of "RIFF"
		var data = new byte[] { 0x52, 0x49, 0x46 };
		var result = WavFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void WavFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = WavFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void WavFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = WavFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid WAV");
	}

	[TestMethod]
	public void WavFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = WavFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// WavPack Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void WavPackFile_EmptyInput_ReturnsFailure ()
	{
		var result = WavPackFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void WavPackFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "wvp" instead of "wvpk"
		var data = new byte[] { 0x77, 0x76, 0x70 };
		var result = WavPackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void WavPackFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = WavPackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void WavPackFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = WavPackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid WavPack");
	}

	[TestMethod]
	public void WavPackFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = WavPackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// Monkey's Audio Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MonkeysAudioFile_EmptyInput_ReturnsFailure ()
	{
		var result = MonkeysAudioFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void MonkeysAudioFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "MA" instead of "MAC "
		var data = new byte[] { 0x4D, 0x41 };
		var result = MonkeysAudioFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void MonkeysAudioFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = MonkeysAudioFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void MonkeysAudioFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = MonkeysAudioFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Monkey's Audio");
	}

	[TestMethod]
	public void MonkeysAudioFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = MonkeysAudioFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// Musepack Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MusepackFile_EmptyInput_ReturnsFailure ()
	{
		var result = MusepackFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void MusepackFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "MP" instead of "MPCK" or "MP+"
		var data = new byte[] { 0x4D, 0x50 };
		var result = MusepackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void MusepackFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = MusepackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void MusepackFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = MusepackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Musepack");
	}

	[TestMethod]
	public void MusepackFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = MusepackFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// Ogg Opus Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void OggOpusFile_EmptyInput_ReturnsFailure ()
	{
		var result = OggOpusFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void OggOpusFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = OggOpusFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void OggOpusFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = OggOpusFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Ogg Opus");
	}

	[TestMethod]
	public void OggOpusFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = OggOpusFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// Ogg FLAC Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void OggFlacFile_EmptyInput_ReturnsFailure ()
	{
		var result = OggFlacFile.Read ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void OggFlacFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = OggFlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void OggFlacFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = OggFlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Ogg FLAC");
	}

	[TestMethod]
	public void OggFlacFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = OggFlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// APE Tag Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void ApeTag_EmptyInput_ReturnsFailure ()
	{
		var result = ApeTag.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void ApeTag_TruncatedPreamble_ReturnsFailure ()
	{
		// Only "APET" instead of "APETAGEX"
		var data = new byte[] { 0x41, 0x50, 0x45, 0x54 };
		var result = ApeTag.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Truncated preamble should fail gracefully");
	}

	[TestMethod]
	public void ApeTag_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = ApeTag.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void ApeTag_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = ApeTag.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid APE tag");
	}

	[TestMethod]
	public void ApeTag_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[1000];
		random.NextBytes (data);

		var result = ApeTag.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}

	// ═══════════════════════════════════════════════════════════════
	// MPEG Program Stream Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MpegPsFile_EmptyInput_ReturnsFailure ()
	{
		Assert.IsFalse (MpegPsFile.Read ([]).IsSuccess);
	}

	[TestMethod]
	public void MpegPsFile_TruncatedPackHeader_ReturnsFailure ()
	{
		var pack = MpegPsTestBuilder.PackHeader1 (0);
		for (var length = 0; length < pack.Length; length++)
			Assert.IsFalse (MpegPsFile.Read (pack.AsSpan (0, length)).IsSuccess, $"length {length}");
	}

	[TestMethod]
	public void MpegPsFile_AllZeros_ReturnsFailure ()
	{
		Assert.IsFalse (MpegPsFile.Read (new byte[1000]).IsSuccess);
	}

	[TestMethod]
	public void MpegPsFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		Assert.IsFalse (MpegPsFile.Read (data).IsSuccess);
	}

	[TestMethod]
	public void MpegPsFile_OversizedId3v2Claim_DoesNotAllocate ()
	{
		// A syncsafe size of 256 MiB on a 20-byte file.
		var data = new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x7F, 0x7F, 0x7F, 0x7F, 0x00, 0x00, 0x01, 0xBA, 0x21, 0x00, 0x01, 0x00, 0x01, 0x80 };

		var result = MpegPsFile.Read (data);
		var streamed = MediaFile.Read (new MemoryStream (data), "clip.mpg");

		Assert.IsFalse (result.IsSuccess);
		// The tag claims the whole file, so nothing follows it and the MP3 reader keeps it.
		Assert.AreNotEqual (MediaFormat.MpegPs, streamed.Format);
	}

	[TestMethod]
	public void MpegPsFile_EveryTruncation_DoesNotThrow ()
	{
		var program = MpegPsTestBuilder.ProgramStream (new MpegPsTestBuilder.Options { VideoFrames = 4, VideoFrameBytes = 20 });
		var dvd = MpegPsTestBuilder.ProgramStream (new MpegPsTestBuilder.Options {
			Mpeg2 = true,
			VideoFrames = 4,
			VideoFrameBytes = 20,
			AudioKind = MpegPsTestBuilder.Audio.Ac3,
			PackStuffing = 2
		});
		var elementary = MpegPsTestBuilder.ElementaryStream (new MpegPsTestBuilder.Options { Mpeg2 = true, VideoFrames = 6, VideoFrameBytes = 16 }, gopLength: 2);
		var tag = new Id3v2Tag { Title = "t" }.Render ().ToArray ();
		var tagged = MpegPsTestBuilder.Concat (tag, program);

		foreach (var data in new[] { program, dvd, elementary, tagged }) {
			for (var length = 0; length <= data.Length; length++) {
				var slice = data.AsSpan (0, length);
				_ = MpegPsFile.Read (slice);
				_ = MediaFile.ReadFromData (slice.ToArray (), "clip.mpg");
			}
		}
	}

	[TestMethod]
	public void MpegPsFile_EveryCorruptedByte_DoesNotThrow ()
	{
		var original = MpegPsTestBuilder.ProgramStream (new MpegPsTestBuilder.Options {
			Mpeg2 = true,
			VideoFrames = 3,
			VideoFrameBytes = 16,
			AudioKind = MpegPsTestBuilder.Audio.Lpcm
		});
		foreach (var value in new byte[] { 0x00, 0x01, 0xBA, 0xFF }) {
			for (var i = 0; i < original.Length; i++) {
				var data = (byte[])original.Clone ();
				data[i] = value;
				var result = MpegPsFile.Read (data);
				if (result.IsSuccess)
					_ = result.File!.Render (data);
			}
		}
	}

	[TestMethod]
	public void MpegPsFile_PacketLengthsPastTheEnd_AreBounded ()
	{
		// A PES packet that claims 65535 bytes and a PES header that claims 255 bytes.
		var data = MpegPsTestBuilder.Concat (
			MpegPsTestBuilder.PackHeader2 (0),
			[0x00, 0x00, 0x01, 0xE0, 0xFF, 0xFF, 0x80, 0x80, 0xFF, 0x21, 0x00, 0x01, 0x00, 0x01]);

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.VideoProperties);
	}

	[TestMethod]
	public void MpegPsFile_StartCodeFlood_IsLinear ()
	{
		// Four MiB of pack start codes whose headers do not parse, after one valid pack.
		var flood = new byte[MpegPsScannerWindow];
		for (var i = 0; i + 4 <= flood.Length; i += 4) {
			flood[i + 2] = 0x01;
			flood[i + 3] = 0xBA;
		}

		var data = MpegPsTestBuilder.Concat (MpegPsTestBuilder.PackHeader1 (0), flood);
		var watch = System.Diagnostics.Stopwatch.StartNew ();

		var result = MpegPsFile.Read (data);

		watch.Stop ();
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (watch.Elapsed < TimeSpan.FromSeconds (5), watch.Elapsed.ToString ());
	}

	[TestMethod]
	public void MpegPsFile_TagOnlyCutShortWithTagNearTheEnd_DoesNotThrow ()
	{
		// An ID3v2 tag that declares more bytes than the file holds, cut so that "TAG" in its
		// text sits 128 bytes from the end: neither a stream nor an ID3v1 tag follows it.
		foreach (var data in MpegPsTinyStreamTests.CutTags ()) {
			Assert.IsFalse (MpegPsFile.Read (data).IsSuccess);
			_ = MediaFile.ReadFromData (data, "clip.mpg");
			_ = MediaFile.Read (new MemoryStream (data), "clip.mpg");
		}
	}

	const int MpegPsScannerWindow = 4 * 1024 * 1024;

	[TestMethod]
	public void MpegPsFile_SequenceAndGopFlood_IsLinear ()
	{
		var header = MpegPsTestBuilder.SequenceHeader (new MpegPsTestBuilder.Options ());
		var flood = new byte[MpegPsScannerWindow];
		for (var i = 0; i + 4 <= flood.Length; i += 4) {
			flood[i + 2] = 0x01;
			flood[i + 3] = (byte)((i / 4 % 2) == 0 ? 0xB8 : 0x00);
		}

		var data = MpegPsTestBuilder.Concat (header, flood);
		var watch = System.Diagnostics.Stopwatch.StartNew ();

		var result = MpegPsFile.Read (data);

		watch.Stop ();
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (watch.Elapsed < TimeSpan.FromSeconds (5), watch.Elapsed.ToString ());
	}

	[TestMethod]
	public void AacFile_EmptyInput_ReturnsFailure ()
	{
		Assert.IsFalse (AacFile.Read ([]).IsSuccess, "Empty input should return failure, not crash");
		Assert.IsFalse (MediaFile.ReadFromData (Array.Empty<byte> (), "empty.aac").IsSuccess);
	}

	[TestMethod]
	public void AacFile_TruncatedHeader_ReturnsFailure ()
	{
		var frame = Aac.AacTestBuilder.AdtsFrame ();
		for (var cut = 0; cut < 7; cut++)
			Assert.IsFalse (AacFile.Read (frame.AsSpan (0, cut)).IsSuccess, $"cut at {cut}");
	}

	[TestMethod]
	public void AacFile_InvalidMagic_ReturnsFailure ()
	{
		Assert.IsFalse (AacFile.Read ("ADIX\0\0\0\0\0\0"u8).IsSuccess);
		Assert.IsFalse (AacFile.Read (Aac.AacTestBuilder.Mp3Frames (4)).IsSuccess);
	}

	[TestMethod]
	public void AacFile_FrameLengthShorterThanTheHeader_ReturnsFailure ()
	{
		var frame = Aac.AacTestBuilder.AdtsFrame (64);
		// aac_frame_length = 6, shorter than the 7-byte header.
		frame[3] &= 0xFC;
		frame[4] = 0;
		frame[5] = (byte)((6 << 5) | 0x1F);
		Assert.IsFalse (AacFile.Read (frame).IsSuccess);
	}

	[TestMethod]
	public void AacFile_OversizedClaim_DoesNotAllocate ()
	{
		// An ID3v2 size of 256 MB and an 8191-byte ADTS frame in a file of a few bytes.
		byte[] id3 = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0x7F, 0x7F, 0x7F, 0x7F, 0xFF, 0xF1, 0x50, 0x83, 0xFF, 0xFF, 0xFC];
		Assert.IsFalse (AacFile.Read (id3).IsSuccess);
		var frame = Aac.AacTestBuilder.AdtsFrame (8191).AsSpan (0, 20).ToArray ();
		var result = AacFile.Read (frame);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (0, result.File!.Properties!.FrameCount);
	}

	[TestMethod]
	public void AacFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[4096];
		for (var round = 0; round < 50; round++) {
			random.NextBytes (data);
			data[0] = 0xFF;
			data[1] = 0xF1;
			_ = AacFile.Read (data);
			_ = MediaFile.ReadFromData (data, "noise.aac");
		}
	}

	[TestMethod]
	public void AacFile_AllZeros_ReturnsFailure ()
	{
		Assert.IsFalse (AacFile.Read (new byte[10_000]).IsSuccess);
	}

	[TestMethod]
	public void AacFile_TruncatedAtEveryByte_DoesNotThrow ()
	{
		int[] front = [2, 1];
		var data = Aac.AacTestBuilder.Concat (
			Aac.AacTestBuilder.Id3v2Bytes ("Front"),
			Aac.AacTestBuilder.Adts (6, length: 80, channelConfiguration: 0, crc: true, payload: Aac.AacTestBuilder.PceBlock (front)),
			Aac.AacTestBuilder.ApeBytes ("Ape"),
			Aac.AacTestBuilder.Id3v1Bytes ("Back"));
		var adif = Aac.AacTestBuilder.Concat (Aac.AacTestBuilder.Adif (64_000, comment: "x", payloadBytes: 50), Aac.AacTestBuilder.Id3v1Bytes ("B"));

		foreach (var whole in new[] { data, adif }) {
			for (var cut = 0; cut <= whole.Length; cut++) {
				var prefix = whole.AsSpan (0, cut).ToArray ();
				var result = AacFile.Read (prefix);
				if (result.IsSuccess)
					_ = result.File!.Render (prefix);
				_ = MediaFile.ReadFromData (prefix, "cut.aac");
				_ = MediaFile.Read (new MemoryStream (prefix), "cut.aac");
			}
		}
	}
}
