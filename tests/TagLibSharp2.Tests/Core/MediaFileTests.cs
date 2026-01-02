// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Asf;
using TagLibSharp2.Tests.Musepack;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class MediaFileTests
{
	[TestMethod]
	public void DetectFormat_FlacMagic_ReturnsFlac ()
	{
		var data = new byte[8];
		TestConstants.Magic.Flac.CopyTo (data, 0);
		data[4] = 0x00; data[5] = 0x00; data[6] = 0x00; data[7] = TestConstants.Flac.StreamInfoSize;

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Flac, format);
	}

	[TestMethod]
	public void DetectFormat_OggMagic_ReturnsOggVorbis ()
	{
		var data = new byte[8];
		TestConstants.Magic.Ogg.CopyTo (data, 0);
		data[4] = 0x00;
		data[5] = TestConstants.Ogg.FlagBos;

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.OggVorbis, format);
	}

	[TestMethod]
	public void DetectFormat_Id3v2Magic_ReturnsMp3 ()
	{
		var data = new byte[8];
		TestConstants.Magic.Id3.CopyTo (data, 0);
		data[3] = TestConstants.Id3v2.Version4;

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Mp3, format);
	}

	[TestMethod]
	public void DetectFormat_Mp3FrameSync_ReturnsMp3 ()
	{
		// MP3 frame sync (0xFF 0xFB)
		var data = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Mp3, format);
	}

	[TestMethod]
	public void DetectFormat_RiffWaveMagic_ReturnsWav ()
	{
		var data = new byte[12];
		TestConstants.Magic.Riff.CopyTo (data, 0);
		// bytes 4-7: size placeholder (zeros)
		TestConstants.Magic.Wave.CopyTo (data, 8);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Wav, format);
	}

	[TestMethod]
	public void DetectFormat_FormAiffMagic_ReturnsAiff ()
	{
		var data = new byte[12];
		TestConstants.Magic.Form.CopyTo (data, 0);
		// bytes 4-7: size placeholder (zeros)
		TestConstants.Magic.Aiff.CopyTo (data, 8);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Aiff, format);
	}

	[TestMethod]
	public void DetectFormat_FormAifcMagic_ReturnsAiff ()
	{
		var data = new byte[12];
		TestConstants.Magic.Form.CopyTo (data, 0);
		// bytes 4-7: size placeholder (zeros)
		TestConstants.Magic.Aifc.CopyTo (data, 8);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Aiff, format);
	}

	[TestMethod]
	public void DetectFormat_DsfMagic_ReturnsDsf ()
	{
		// DSF: starts with "DSD "
		var data = new byte[28]; // DSF DSD chunk minimum
		TestConstants.Magic.Dsf.CopyTo (data, 0);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Dsf, format);
	}

	[TestMethod]
	public void DetectFormat_DffMagic_ReturnsDff ()
	{
		// DFF: starts with "FRM8" + 8 bytes size + "DSD "
		var data = new byte[16];
		TestConstants.Magic.Frm8.CopyTo (data, 0);
		// bytes 4-11: 64-bit size (zeros ok)
		TestConstants.Magic.DsdType.CopyTo (data, 12);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Dff, format);
	}

	[TestMethod]
	public void DetectFormat_AsfMagic_ReturnsAsf ()
	{
		// ASF: starts with 16-byte Header Object GUID
		var data = new byte[30]; // Minimum ASF header size
		TestConstants.Magic.Asf.CopyTo (data, 0);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Asf, format);
	}

	[TestMethod]
	public void DetectFormat_UnknownMagic_UsesExtension ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		Assert.AreEqual (MediaFormat.Flac, MediaFile.DetectFormat (data, "song.flac"));
		Assert.AreEqual (MediaFormat.Mp3, MediaFile.DetectFormat (data, "song.mp3"));
		Assert.AreEqual (MediaFormat.OggVorbis, MediaFile.DetectFormat (data, "song.ogg"));
		Assert.AreEqual (MediaFormat.Wav, MediaFile.DetectFormat (data, "song.wav"));
		Assert.AreEqual (MediaFormat.Aiff, MediaFile.DetectFormat (data, "song.aiff"));
		Assert.AreEqual (MediaFormat.Aiff, MediaFile.DetectFormat (data, "song.aif"));
		Assert.AreEqual (MediaFormat.Dsf, MediaFile.DetectFormat (data, "song.dsf"));
		Assert.AreEqual (MediaFormat.Dff, MediaFile.DetectFormat (data, "song.dff"));
		Assert.AreEqual (MediaFormat.Asf, MediaFile.DetectFormat (data, "song.wma"));
		Assert.AreEqual (MediaFormat.Asf, MediaFile.DetectFormat (data, "video.asf"));
	}

	[TestMethod]
	public void DetectFormat_UnknownWithNoHint_ReturnsUnknown ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Unknown, format);
	}

	[TestMethod]
	public void OpenFromData_ValidFlac_ReturnsFlacFile ()
	{
		// Build a minimal valid FLAC file
		var data = CreateMinimalFlac ();

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Flac, result.Format);
		Assert.IsNotNull (result.File);
		Assert.IsInstanceOfType<FlacFile> (result.File);
	}

	[TestMethod]
	public void OpenFromData_ValidMp3WithId3v2_ReturnsMp3File ()
	{
		// Build a minimal valid MP3 with ID3v2 tag
		var data = CreateMinimalMp3WithId3v2 ();

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Mp3, result.Format);
		Assert.IsNotNull (result.File);
		Assert.IsInstanceOfType<Mp3File> (result.File);
	}

	[TestMethod]
	public void OpenFromData_UnknownFormat_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		var result = MediaFile.OpenFromData (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Unknown, result.Format);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void GetFileAs_CorrectType_ReturnsFile ()
	{
		var data = CreateMinimalFlac ();
		var result = MediaFile.OpenFromData (data);

		var flacFile = result.GetFileAs<FlacFile> ();

		Assert.IsNotNull (flacFile);
	}

	[TestMethod]
	public void GetFileAs_WrongType_ReturnsNull ()
	{
		var data = CreateMinimalFlac ();
		var result = MediaFile.OpenFromData (data);

		var mp3File = result.GetFileAs<Mp3File> ();

		Assert.IsNull (mp3File);
	}

	[TestMethod]
	public void MediaFileResult_FailedResult_HasCorrectProperties ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		var result = MediaFile.OpenFromData (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNull (result.Tag);
		Assert.AreEqual (MediaFormat.Unknown, result.Format);
		StringAssert.Contains (result.Error!, "Unknown");
	}

	static byte[] CreateMinimalFlac ()
	{
		// Create minimal valid FLAC file:
		// fLaC magic (4) + STREAMINFO block header (4) + STREAMINFO data (34)
		var data = new byte[4 + 4 + 34];

		// Magic: "fLaC"
		data[0] = 0x66;
		data[1] = 0x4C;
		data[2] = 0x61;
		data[3] = 0x43;

		// STREAMINFO block header: type=0, is_last=true (0x80), size=34 (0x000022)
		data[4] = 0x80; // type 0 (STREAMINFO) + is_last flag
		data[5] = 0x00;
		data[6] = 0x00;
		data[7] = 0x22; // size = 34

		// STREAMINFO data (34 bytes) - minimal valid values
		// min/max block size (2+2), min/max frame size (3+3),
		// sample rate/channels/bits/samples (8), MD5 (16)
		data[8] = 0x00;
		data[9] = 0x10; // min block size
		data[10] = 0xFF;
		data[11] = 0xFF; // max block size

		return data;
	}

	static byte[] CreateMinimalMp3WithId3v2 ()
	{
		// Create minimal ID3v2.4 tag with empty content
		// Header: "ID3" (3) + version (2) + flags (1) + syncsafe size (4)
		var data = new byte[10];

		// Magic: "ID3"
		data[0] = 0x49;
		data[1] = 0x44;
		data[2] = 0x33;

		// Version: 2.4.0
		data[3] = 0x04;
		data[4] = 0x00;

		// Flags: none
		data[5] = 0x00;

		// Size: 0 (syncsafe)
		data[6] = 0x00;
		data[7] = 0x00;
		data[8] = 0x00;
		data[9] = 0x00;

		return data;
	}

	[TestMethod]
	public void OpenFromData_ValidDsf_ReturnsDsfFile ()
	{
		var data = CreateMinimalDsf ();

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Dsf, result.Format);
		Assert.IsNotNull (result.File);
		Assert.IsInstanceOfType<DsfFile> (result.File);
	}

	[TestMethod]
	public void OpenFromData_ValidDff_ReturnsDffFile ()
	{
		var data = CreateMinimalDff ();

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Dff, result.Format);
		Assert.IsNotNull (result.File);
		Assert.IsInstanceOfType<DffFile> (result.File);
	}

	[TestMethod]
	public void GetFileAs_DsfFile_ReturnsCorrectType ()
	{
		var data = CreateMinimalDsf ();
		var result = MediaFile.OpenFromData (data);

		var dsfFile = result.GetFileAs<DsfFile> ();

		Assert.IsNotNull (dsfFile);
	}

	[TestMethod]
	public void GetFileAs_DffFile_ReturnsCorrectType ()
	{
		var data = CreateMinimalDff ();
		var result = MediaFile.OpenFromData (data);

		var dffFile = result.GetFileAs<DffFile> ();

		Assert.IsNotNull (dffFile);
	}

	[TestMethod]
	public void OpenFromData_ValidAsf_ReturnsAsfFile ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "MediaFile Test");

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Asf, result.Format);
		Assert.IsNotNull (result.File);
		Assert.IsInstanceOfType<AsfFile> (result.File);
		Assert.AreEqual ("MediaFile Test", result.Tag?.Title);
	}

	[TestMethod]
	public void GetFileAs_AsfFile_ReturnsCorrectType ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = MediaFile.OpenFromData (data);

		var asfFile = result.GetFileAs<AsfFile> ();

		Assert.IsNotNull (asfFile);
	}

	[TestMethod]
	public void DetectFormat_MusepackSV7Magic_ReturnsMusepack ()
	{
		// Musepack SV7: starts with "MP+"
		var data = new byte[20];
		TestConstants.Magic.MusepackSV7.CopyTo (data, 0);
		data[3] = 0x70; // Version 7 in upper nibble

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Musepack, format);
	}

	[TestMethod]
	public void DetectFormat_MusepackSV8Magic_ReturnsMusepack ()
	{
		// Musepack SV8: starts with "MPCK"
		var data = new byte[20];
		TestConstants.Magic.MusepackSV8.CopyTo (data, 0);

		var format = MediaFile.DetectFormat (data);

		Assert.AreEqual (MediaFormat.Musepack, format);
	}

	[TestMethod]
	public void DetectFormat_UnknownMagic_MusepackExtensions ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		Assert.AreEqual (MediaFormat.Musepack, MediaFile.DetectFormat (data, "song.mpc"));
		Assert.AreEqual (MediaFormat.Musepack, MediaFile.DetectFormat (data, "song.mp+"));
		Assert.AreEqual (MediaFormat.Musepack, MediaFile.DetectFormat (data, "song.mpp"));
	}

	[TestMethod]
	public void OpenFromData_ValidMusepack_ReturnsMusepackFile ()
	{
		var data = MusepackFileTests.CreateMinimalMusepackSV7FilePublic ();

		var result = MediaFile.OpenFromData (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Musepack, result.Format);
		Assert.IsNotNull (result.File);
		Assert.IsInstanceOfType<MusepackFile> (result.File);
	}

	[TestMethod]
	public void GetFileAs_MusepackFile_ReturnsCorrectType ()
	{
		var data = MusepackFileTests.CreateMinimalMusepackSV7FilePublic ();
		var result = MediaFile.OpenFromData (data);

		var mpcFile = result.GetFileAs<MusepackFile> ();

		Assert.IsNotNull (mpcFile);
	}

	static byte[] CreateMinimalDsf ()
	{
		// DSF structure matches DsfFileTests.CreateMinimalDsfFile
		// DSD chunk (28) + fmt chunk (52) + data chunk (12 header + 88 audio = 100)
		// Total: 28 + 52 + 100 = 180 bytes

		// Build DSD chunk (28 bytes)
		var dsd = new byte[28];
		dsd[0] = (byte)'D';
		dsd[1] = (byte)'S';
		dsd[2] = (byte)'D';
		dsd[3] = (byte)' ';
		BitConverter.GetBytes (28UL).CopyTo (dsd, 4);  // chunk size
		BitConverter.GetBytes (180UL).CopyTo (dsd, 12); // file size
		BitConverter.GetBytes (0UL).CopyTo (dsd, 20);  // metadata offset (none)

		// Build fmt chunk (52 bytes)
		var fmt = new byte[52];
		fmt[0] = (byte)'f';
		fmt[1] = (byte)'m';
		fmt[2] = (byte)'t';
		fmt[3] = (byte)' ';
		BitConverter.GetBytes (52UL).CopyTo (fmt, 4);  // chunk size
		BitConverter.GetBytes (1U).CopyTo (fmt, 12);   // format version
		BitConverter.GetBytes (0U).CopyTo (fmt, 16);   // format id (DSD raw)
		BitConverter.GetBytes (2U).CopyTo (fmt, 20);   // channel type (stereo)
		BitConverter.GetBytes (2U).CopyTo (fmt, 24);   // channel count
		BitConverter.GetBytes (2822400U).CopyTo (fmt, 28); // sample rate (DSD64)
		BitConverter.GetBytes (1U).CopyTo (fmt, 32);   // bits per sample
		BitConverter.GetBytes (2822400UL).CopyTo (fmt, 36); // sample count
		BitConverter.GetBytes (4096U).CopyTo (fmt, 44); // block size per channel
														// reserved 4 bytes at end are already 0

		// Build data chunk header (12 bytes) + audio data (88 bytes) = 100 bytes
		var dataHeader = new byte[12];
		dataHeader[0] = (byte)'d';
		dataHeader[1] = (byte)'a';
		dataHeader[2] = (byte)'t';
		dataHeader[3] = (byte)'a';
		BitConverter.GetBytes (100UL).CopyTo (dataHeader, 4); // chunk size (header + data)

		var audioData = new byte[88]; // padding to make data chunk 100 bytes total

		// Combine all parts
		var result = new byte[180];
		dsd.CopyTo (result, 0);
		fmt.CopyTo (result, 28);
		dataHeader.CopyTo (result, 80);
		audioData.CopyTo (result, 92);

		return result;
	}

	static byte[] CreateMinimalDff ()
	{
		// DFF (DSDIFF) structure: FRM8 + FVER + PROP + DSD
		using var ms = new MemoryStream ();

		// FRM8 container header
		ms.Write (TestConstants.Magic.Frm8, 0, 4); // "FRM8"
		var sizePos = ms.Position;
		WriteUInt64BE (ms, 0); // placeholder for size
		ms.Write (TestConstants.Magic.DsdType, 0, 4); // "DSD "

		// FVER chunk
		var fver = new byte[] { 0x46, 0x56, 0x45, 0x52 }; // "FVER"
		ms.Write (fver, 0, 4);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000); // Version 1.5

		// PROP chunk
		var prop = new byte[] { 0x50, 0x52, 0x4F, 0x50 }; // "PROP"
		ms.Write (prop, 0, 4);
		var propSizePos = ms.Position;
		WriteUInt64BE (ms, 0); // placeholder
		var snd = new byte[] { 0x53, 0x4E, 0x44, 0x20 }; // "SND "
		ms.Write (snd, 0, 4);

		// FS sub-chunk
		var fs = new byte[] { 0x46, 0x53, 0x20, 0x20 }; // "FS  "
		ms.Write (fs, 0, 4);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400); // DSD64

		// CHNL sub-chunk
		var chnl = new byte[] { 0x43, 0x48, 0x4E, 0x4C }; // "CHNL"
		ms.Write (chnl, 0, 4);
		WriteUInt64BE (ms, 10); // 2 + 2*4
		WriteUInt16BE (ms, 2);  // channel count
		var slft = new byte[] { 0x53, 0x4C, 0x46, 0x54 }; // "SLFT"
		var srgt = new byte[] { 0x53, 0x52, 0x47, 0x54 }; // "SRGT"
		ms.Write (slft, 0, 4);
		ms.Write (srgt, 0, 4);

		// CMPR sub-chunk
		var cmpr = new byte[] { 0x43, 0x4D, 0x50, 0x52 }; // "CMPR"
		ms.Write (cmpr, 0, 4);
		WriteUInt64BE (ms, 14); // 4 + 1 + 9
		ms.Write (TestConstants.Magic.DsdType, 0, 4); // "DSD "
		ms.WriteByte (9); // compressionName length
		var notCompressed = System.Text.Encoding.ASCII.GetBytes ("not compr");
		ms.Write (notCompressed, 0, 9);

		// Update PROP size
		var propSize = ms.Position - propSizePos - 8;
		var currentPos = ms.Position;
		ms.Position = propSizePos;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = currentPos;

		// DSD chunk (audio data)
		var dsd = new byte[] { 0x44, 0x53, 0x44, 0x20 }; // "DSD "
		ms.Write (dsd, 0, 4);
		WriteUInt64BE (ms, 100); // chunk size
		var audioData = new byte[100];
		ms.Write (audioData, 0, audioData.Length);

		// Update FRM8 size
		var frm8Size = ms.Length - 12; // excludes "FRM8" and size field
		currentPos = ms.Position;
		ms.Position = sizePos;
		WriteUInt64BE (ms, (ulong)frm8Size);
		ms.Position = currentPos;

		return ms.ToArray ();
	}

	static void WriteUInt64BE (Stream s, ulong value)
	{
		var bytes = BitConverter.GetBytes (value);
		if (BitConverter.IsLittleEndian)
			Array.Reverse (bytes);
		s.Write (bytes, 0, 8);
	}

	static void WriteUInt32BE (Stream s, uint value)
	{
		var bytes = BitConverter.GetBytes (value);
		if (BitConverter.IsLittleEndian)
			Array.Reverse (bytes);
		s.Write (bytes, 0, 4);
	}

	static void WriteUInt16BE (Stream s, ushort value)
	{
		var bytes = BitConverter.GetBytes (value);
		if (BitConverter.IsLittleEndian)
			Array.Reverse (bytes);
		s.Write (bytes, 0, 2);
	}
}
