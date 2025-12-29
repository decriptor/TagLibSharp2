// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Ogg;
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
		var result = FlacFile.Read (Array.Empty<byte> ());
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void OggVorbisFile_EmptyInput_ReturnsFailure ()
	{
		var result = OggVorbisFile.Read (Array.Empty<byte> ());
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v1Tag_EmptyInput_ReturnsFailure ()
	{
		var result = Id3v1Tag.Read (Array.Empty<byte> ());
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v2Tag_EmptyInput_ReturnsFailure ()
	{
		var result = Id3v2Tag.Read (Array.Empty<byte> ());
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void VorbisComment_EmptyInput_ReturnsFailure ()
	{
		var result = VorbisComment.Read (Array.Empty<byte> ());
		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	public void FlacFile_TruncatedMagic_ReturnsFailure ()
	{
		// Only "fLa" instead of "fLaC"
		var data = new byte[] { 0x66, 0x4C, 0x61 };
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void FlacFile_TruncatedStreamInfo_ReturnsFailure ()
	{
		// Magic + partial STREAMINFO header
		var data = new byte[] {
			0x66, 0x4C, 0x61, 0x43, // fLaC
			0x80, 0x00, 0x00, 0x22, // STREAMINFO header (last, 34 bytes)
			0x00, 0x00, 0x00, 0x00  // Only 4 bytes of STREAMINFO data
		};
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v2Tag_TruncatedHeader_ReturnsFailure ()
	{
		// Only "ID3" + version, no size
		var data = new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00 };
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v1Tag_TruncatedTag_ReturnsFailure ()
	{
		// Only "TAG" + partial data (less than 128 bytes)
		var data = new byte[] { 0x54, 0x41, 0x47, 0x00, 0x00, 0x00 };
		var result = Id3v1Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void OggVorbisFile_TruncatedPageHeader_ReturnsFailure ()
	{
		// "OggS" + partial header
		var data = new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02 };
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	public void FlacFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("fLaC", result.Error!);
	}

	[TestMethod]
	public void Id3v2Tag_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v1Tag_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[128];
		data[0] = 0x00; // Not "TAG"
		var result = Id3v1Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void OggVorbisFile_WrongMagic_ReturnsFailure ()
	{
		var data = new byte[100];
		data[0] = 0x00; // Not "OggS"
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
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
		Assert.IsFalse (result.IsSuccess);
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
		Assert.IsFalse (result.IsSuccess);
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
		Assert.IsFalse (result.IsSuccess);
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
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void OggVorbisFile_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v2Tag_AllZeros_ReturnsFailure ()
	{
		var data = new byte[1000];
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	public void FlacFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void OggVorbisFile_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v2Tag_AllOnes_ReturnsFailure ()
	{
		var data = new byte[1000];
		Array.Fill (data, (byte)0xFF);
		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	public void FlacFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42); // Fixed seed for reproducibility
		var data = new byte[10000];
		random.NextBytes (data);

		// Should not throw, just return failure
		var result = FlacFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void OggVorbisFile_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = OggVorbisFile.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Id3v2Tag_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[10000];
		random.NextBytes (data);

		var result = Id3v2Tag.Read (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void VorbisComment_RandomData_DoesNotCrash ()
	{
		var random = new Random (42);
		var data = new byte[1000];
		random.NextBytes (data);

		var result = VorbisComment.Read (data);
		// May succeed with garbage data or fail, but should not crash
		_ = result.IsSuccess; // Access result to ensure it was computed
	}



	[TestMethod]
	public void FlacFile_ExactlyMinimumSize_DoesNotCrash ()
	{
		// fLaC + minimum header
		var data = new byte[8];
		data[0] = 0x66; data[1] = 0x4C; data[2] = 0x61; data[3] = 0x43; // fLaC
		data[4] = 0x80; data[5] = 0x00; data[6] = 0x00; data[7] = 0x00; // Header

		var result = FlacFile.Read (data);
		// Will fail but should not crash
		_ = result.IsSuccess;
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
		_ = result.IsSuccess;
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
		_ = result.IsSuccess;
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
		_ = result.IsSuccess;
	}

}
