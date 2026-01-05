// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Tests for IsValidFormat() static methods on all file format classes.
/// These methods allow quick format detection without full parsing.
/// </summary>
[TestClass]
public class IsValidFormatTests
{
	// ═══════════════════════════════════════════════════════════════
	// FlacFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void FlacFile_IsValidFormat_ValidMagic_ReturnsTrue ()
	{
		// "fLaC" magic
		var data = new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x22, 0x00 };
		Assert.IsTrue (FlacFile.IsValidFormat (data));
	}

	[TestMethod]
	public void FlacFile_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (FlacFile.IsValidFormat (data));
	}

	[TestMethod]
	public void FlacFile_IsValidFormat_TooShort_ReturnsFalse ()
	{
		var data = new byte[] { 0x66, 0x4C, 0x61 }; // Only 3 bytes
		Assert.IsFalse (FlacFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// Mp3File
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Mp3File_IsValidFormat_Id3Magic_ReturnsTrue ()
	{
		// "ID3" magic
		var data = new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsTrue (Mp3File.IsValidFormat (data));
	}

	[TestMethod]
	public void Mp3File_IsValidFormat_MpegSync_ReturnsTrue ()
	{
		// MPEG sync: 0xFF + frame header with valid layer/version bits
		// 0xFF 0xFB = MPEG1 Layer3
		var data = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
		Assert.IsTrue (Mp3File.IsValidFormat (data));
	}

	[TestMethod]
	public void Mp3File_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (Mp3File.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// Mp4File
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Mp4File_IsValidFormat_FtypAtom_ReturnsTrue ()
	{
		// ftyp atom: size (4) + "ftyp" (4)
		var data = new byte[] {
			0x00, 0x00, 0x00, 0x14,  // Size = 20
			0x66, 0x74, 0x79, 0x70,  // "ftyp"
			0x4D, 0x34, 0x41, 0x20,  // "M4A "
			0x00, 0x00, 0x00, 0x00,  // Minor version
			0x4D, 0x34, 0x41, 0x20   // Compatible brand
		};
		Assert.IsTrue (Mp4File.IsValidFormat (data));
	}

	[TestMethod]
	public void Mp4File_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (Mp4File.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// WavFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void WavFile_IsValidFormat_RiffWave_ReturnsTrue ()
	{
		// "RIFF" + size + "WAVE"
		var data = new byte[] {
			0x52, 0x49, 0x46, 0x46,  // "RIFF"
			0x00, 0x00, 0x00, 0x00,  // Size
			0x57, 0x41, 0x56, 0x45   // "WAVE"
		};
		Assert.IsTrue (WavFile.IsValidFormat (data));
	}

	[TestMethod]
	public void WavFile_IsValidFormat_InvalidFormType_ReturnsFalse ()
	{
		// "RIFF" but not "WAVE"
		var data = new byte[] {
			0x52, 0x49, 0x46, 0x46,  // "RIFF"
			0x00, 0x00, 0x00, 0x00,  // Size
			0x41, 0x56, 0x49, 0x20   // "AVI " - not WAV
		};
		Assert.IsFalse (WavFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// AiffFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void AiffFile_IsValidFormat_FormAiff_ReturnsTrue ()
	{
		// "FORM" + size + "AIFF"
		var data = new byte[] {
			0x46, 0x4F, 0x52, 0x4D,  // "FORM"
			0x00, 0x00, 0x00, 0x00,  // Size
			0x41, 0x49, 0x46, 0x46   // "AIFF"
		};
		Assert.IsTrue (AiffFile.IsValidFormat (data));
	}

	[TestMethod]
	public void AiffFile_IsValidFormat_FormAifc_ReturnsTrue ()
	{
		// "FORM" + size + "AIFC"
		var data = new byte[] {
			0x46, 0x4F, 0x52, 0x4D,  // "FORM"
			0x00, 0x00, 0x00, 0x00,  // Size
			0x41, 0x49, 0x46, 0x43   // "AIFC"
		};
		Assert.IsTrue (AiffFile.IsValidFormat (data));
	}

	[TestMethod]
	public void AiffFile_IsValidFormat_InvalidFormType_ReturnsFalse ()
	{
		// "FORM" but not "AIFF" or "AIFC"
		var data = new byte[] {
			0x46, 0x4F, 0x52, 0x4D,  // "FORM"
			0x00, 0x00, 0x00, 0x00,  // Size
			0x58, 0x58, 0x58, 0x58   // "XXXX"
		};
		Assert.IsFalse (AiffFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// OggVorbisFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void OggVorbisFile_IsValidFormat_OggSWithVorbis_ReturnsTrue ()
	{
		// OggS + header + first packet with vorbis identification
		var data = CreateOggPage (new byte[] { 0x01, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 }); // 0x01 + "vorbis"
		Assert.IsTrue (OggVorbisFile.IsValidFormat (data));
	}

	[TestMethod]
	public void OggVorbisFile_IsValidFormat_OggSWithOpus_ReturnsFalse ()
	{
		// OggS with Opus magic should return false for Vorbis check
		var data = CreateOggPage (new byte[] { 0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64 }); // "OpusHead"
		Assert.IsFalse (OggVorbisFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// OggOpusFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void OggOpusFile_IsValidFormat_OggSWithOpus_ReturnsTrue ()
	{
		// OggS + header + first packet with "OpusHead"
		var data = CreateOggPage (new byte[] { 0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64 }); // "OpusHead"
		Assert.IsTrue (OggOpusFile.IsValidFormat (data));
	}

	[TestMethod]
	public void OggOpusFile_IsValidFormat_OggSWithVorbis_ReturnsFalse ()
	{
		// OggS with Vorbis magic should return false for Opus check
		var data = CreateOggPage (new byte[] { 0x01, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 }); // 0x01 + "vorbis"
		Assert.IsFalse (OggOpusFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// OggFlacFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void OggFlacFile_IsValidFormat_OggSWithFlac_ReturnsTrue ()
	{
		// OggS + header + first packet with 0x7F + "FLAC"
		var data = CreateOggPage (new byte[] { 0x7F, 0x46, 0x4C, 0x41, 0x43 }); // 0x7F + "FLAC"
		Assert.IsTrue (OggFlacFile.IsValidFormat (data));
	}

	[TestMethod]
	public void OggFlacFile_IsValidFormat_OggSWithVorbis_ReturnsFalse ()
	{
		// OggS with Vorbis magic should return false for FLAC check
		var data = CreateOggPage (new byte[] { 0x01, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 }); // 0x01 + "vorbis"
		Assert.IsFalse (OggFlacFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// AsfFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void AsfFile_IsValidFormat_AsfHeaderGuid_ReturnsTrue ()
	{
		// ASF Header Object GUID: 30 26 B2 75 8E 66 CF 11 A6 D9 00 AA 00 62 CE 6C
		var data = new byte[] {
			0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
			0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C,
			0x00, 0x00, 0x00, 0x00  // More data
		};
		Assert.IsTrue (AsfFile.IsValidFormat (data));
	}

	[TestMethod]
	public void AsfFile_IsValidFormat_InvalidGuid_ReturnsFalse ()
	{
		var data = new byte[] {
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
		};
		Assert.IsFalse (AsfFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// DsfFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void DsfFile_IsValidFormat_DsdMagic_ReturnsTrue ()
	{
		// "DSD " magic
		var data = new byte[] { 0x44, 0x53, 0x44, 0x20, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsTrue (DsfFile.IsValidFormat (data));
	}

	[TestMethod]
	public void DsfFile_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (DsfFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// DffFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void DffFile_IsValidFormat_Frm8DsdMagic_ReturnsTrue ()
	{
		// "FRM8" + size + "DSD "
		var data = new byte[] {
			0x46, 0x52, 0x4D, 0x38,  // "FRM8"
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // Size (8 bytes)
			0x44, 0x53, 0x44, 0x20   // "DSD "
		};
		Assert.IsTrue (DffFile.IsValidFormat (data));
	}

	[TestMethod]
	public void DffFile_IsValidFormat_InvalidFormType_ReturnsFalse ()
	{
		// "FRM8" but not "DSD "
		var data = new byte[] {
			0x46, 0x52, 0x4D, 0x38,  // "FRM8"
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // Size
			0x58, 0x58, 0x58, 0x58   // "XXXX"
		};
		Assert.IsFalse (DffFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// WavPackFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void WavPackFile_IsValidFormat_WvpkMagic_ReturnsTrue ()
	{
		// "wvpk" magic
		var data = new byte[] { 0x77, 0x76, 0x70, 0x6B, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsTrue (WavPackFile.IsValidFormat (data));
	}

	[TestMethod]
	public void WavPackFile_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (WavPackFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// MonkeysAudioFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MonkeysAudioFile_IsValidFormat_MacMagic_ReturnsTrue ()
	{
		// "MAC " magic
		var data = new byte[] { 0x4D, 0x41, 0x43, 0x20, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsTrue (MonkeysAudioFile.IsValidFormat (data));
	}

	[TestMethod]
	public void MonkeysAudioFile_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (MonkeysAudioFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// MusepackFile
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void MusepackFile_IsValidFormat_Sv8Magic_ReturnsTrue ()
	{
		// "MPCK" magic (SV8)
		var data = new byte[] { 0x4D, 0x50, 0x43, 0x4B, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsTrue (MusepackFile.IsValidFormat (data));
	}

	[TestMethod]
	public void MusepackFile_IsValidFormat_Sv7Magic_ReturnsTrue ()
	{
		// "MP+" magic (SV7)
		var data = new byte[] { 0x4D, 0x50, 0x2B, 0x00, 0x00, 0x00, 0x00, 0x00 };
		Assert.IsTrue (MusepackFile.IsValidFormat (data));
	}

	[TestMethod]
	public void MusepackFile_IsValidFormat_InvalidMagic_ReturnsFalse ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		Assert.IsFalse (MusepackFile.IsValidFormat (data));
	}

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Creates a minimal Ogg page with the given packet data.
	/// </summary>
	static byte[] CreateOggPage (byte[] packetData)
	{
		// Ogg page structure:
		// Magic "OggS" (4) + version (1) + flags (1) + granule (8) +
		// serial (4) + seq (4) + crc (4) + seg_count (1) + segment_table (n) + data
		var segmentCount = 1;
		var headerSize = 27 + segmentCount;
		var totalSize = headerSize + packetData.Length;
		var result = new byte[totalSize];

		// Magic "OggS"
		result[0] = 0x4F; // 'O'
		result[1] = 0x67; // 'g'
		result[2] = 0x67; // 'g'
		result[3] = 0x53; // 'S'

		// Version (0)
		result[4] = 0x00;

		// Flags (BOS = 0x02)
		result[5] = 0x02;

		// Granule position (8 bytes, 0)
		// Already 0 from array initialization

		// Serial number (4 bytes)
		result[14] = 0x01;

		// Sequence number (4 bytes, 0)
		// Already 0 from array initialization

		// CRC (4 bytes, not calculated for test)
		// Already 0 from array initialization

		// Segment count
		result[26] = (byte)segmentCount;

		// Segment table
		result[27] = (byte)packetData.Length;

		// Packet data
		Array.Copy (packetData, 0, result, headerSize, packetData.Length);

		return result;
	}
}
