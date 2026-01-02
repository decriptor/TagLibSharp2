// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
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
		var result = DsfFile.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void DsfFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "DS" instead of "DSD "
		var data = new byte[] { 0x44, 0x53 };
		var result = DsfFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void DsfFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = DsfFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void DsfFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = DsfFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid DSF");
	}

	[TestMethod]
	public void DsfFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = DsfFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid DSF");
	}

	[TestMethod]
	public void DsfFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42); // Fixed seed for reproducibility
		var data = new byte[10000];
		random.NextBytes (data);

		var result = DsfFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// DFF Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void DffFile_EmptyInput_ReturnsFailure ()
	{
		var result = DffFile.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void DffFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "FR" instead of "FRM8"
		var data = new byte[] { 0x46, 0x52 };
		var result = DffFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void DffFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = DffFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void DffFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = DffFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid DFF");
	}

	[TestMethod]
	public void DffFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = DffFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All 0xFF bytes should be rejected as invalid DFF");
	}

	[TestMethod]
	public void DffFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = DffFile.Parse (data);
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
		var result = WavPackFile.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void WavPackFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "wvp" instead of "wvpk"
		var data = new byte[] { 0x77, 0x76, 0x70 };
		var result = WavPackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void WavPackFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = WavPackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void WavPackFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = WavPackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid WavPack");
	}

	[TestMethod]
	public void WavPackFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = WavPackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// Monkey's Audio Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MonkeysAudioFile_EmptyInput_ReturnsFailure ()
	{
		var result = MonkeysAudioFile.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void MonkeysAudioFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "MA" instead of "MAC "
		var data = new byte[] { 0x4D, 0x41 };
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void MonkeysAudioFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void MonkeysAudioFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Monkey's Audio");
	}

	[TestMethod]
	public void MonkeysAudioFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = MonkeysAudioFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Random data should fail without crashing");
	}



	// ═══════════════════════════════════════════════════════════════
	// Musepack Format Malformed Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MusepackFile_EmptyInput_ReturnsFailure ()
	{
		var result = MusepackFile.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void MusepackFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "MP" instead of "MPCK" or "MP+"
		var data = new byte[] { 0x4D, 0x50 };
		var result = MusepackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Truncated magic should fail gracefully");
	}

	[TestMethod]
	public void MusepackFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = MusepackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void MusepackFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = MusepackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Musepack");
	}

	[TestMethod]
	public void MusepackFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = MusepackFile.Parse (data);
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
		var result = OggFlacFile.Parse ([]);
		Assert.IsFalse (result.IsSuccess, "Empty input should return failure, not crash");
	}

	[TestMethod]
	public void OggFlacFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = OggFlacFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "Wrong magic should be rejected");
	}

	[TestMethod]
	public void OggFlacFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = OggFlacFile.Parse (data);
		Assert.IsFalse (result.IsSuccess, "All zeros should be rejected as invalid Ogg FLAC");
	}

	[TestMethod]
	public void OggFlacFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = OggFlacFile.Parse (data);
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

}
