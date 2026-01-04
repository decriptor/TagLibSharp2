// Copyright (c) 2025 Stephen Shaw and contributors
// Format detection tests for MediaFile

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("FormatDetection")]
public class MediaFileFormatDetectionTests
{
	[TestMethod]
	public void DetectFormat_Mp4Ftyp_ReturnsMp4 ()
	{
		// Arrange - MP4 starts with size(4) + "ftyp"(4) + brand
		var data = new byte[12];
		data[0] = 0x00;
		data[1] = 0x00;
		data[2] = 0x00;
		data[3] = 0x0C; // box size
		data[4] = (byte)'f';
		data[5] = (byte)'t';
		data[6] = (byte)'y';
		data[7] = (byte)'p';
		data[8] = (byte)'M'; // brand
		data[9] = (byte)'4';
		data[10] = (byte)'A';
		data[11] = (byte)' ';

		// Act
		var format = MediaFile.DetectFormat (data);

		// Assert
		Assert.AreEqual (MediaFormat.Mp4, format);
	}

	[TestMethod]
	public void DetectFormat_OggWithOpusHead_ReturnsOpus ()
	{
		// Arrange - Ogg container with OpusHead magic
		var data = CreateOggWithCodec ("OpusHead");

		// Act
		var format = MediaFile.DetectFormat (data);

		// Assert
		Assert.AreEqual (MediaFormat.Opus, format);
	}

	[TestMethod]
	public void DetectFormat_OggWithVorbis_ReturnsOggVorbis ()
	{
		// Arrange - Ogg container with Vorbis identification header
		var data = CreateOggWithVorbisId ();

		// Act
		var format = MediaFile.DetectFormat (data);

		// Assert
		Assert.AreEqual (MediaFormat.OggVorbis, format);
	}

	[TestMethod]
	public void DetectFormat_OggTooShort_DefaultsToVorbis ()
	{
		// Arrange - Ogg magic but too short for codec detection
		var data = new byte[20];
		data[0] = (byte)'O';
		data[1] = (byte)'g';
		data[2] = (byte)'g';
		data[3] = (byte)'S';

		// Act
		var format = MediaFile.DetectFormat (data);

		// Assert - defaults to OggVorbis for short data
		Assert.AreEqual (MediaFormat.OggVorbis, format);
	}

	[TestMethod]
	public void DetectFormat_OggUnknownCodec_DefaultsToVorbis ()
	{
		// Arrange - Ogg container with unknown codec
		var data = CreateOggWithCodec ("Unknown!");

		// Act
		var format = MediaFile.DetectFormat (data);

		// Assert
		Assert.AreEqual (MediaFormat.OggVorbis, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionOpus_ReturnsOpus ()
	{
		// Arrange - unknown bytes with .opus extension
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "song.opus");

		// Assert
		Assert.AreEqual (MediaFormat.Opus, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionM4a_ReturnsMp4 ()
	{
		// Arrange
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "song.m4a");

		// Assert
		Assert.AreEqual (MediaFormat.Mp4, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionM4b_ReturnsMp4 ()
	{
		// Arrange - audiobook extension
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "audiobook.m4b");

		// Assert
		Assert.AreEqual (MediaFormat.Mp4, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionM4p_ReturnsMp4 ()
	{
		// Arrange - protected iTunes
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "protected.m4p");

		// Assert
		Assert.AreEqual (MediaFormat.Mp4, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionM4v_ReturnsMp4 ()
	{
		// Arrange - video
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "video.m4v");

		// Assert
		Assert.AreEqual (MediaFormat.Mp4, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionMp4_ReturnsMp4 ()
	{
		// Arrange
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "movie.mp4");

		// Assert
		Assert.AreEqual (MediaFormat.Mp4, format);
	}

	[TestMethod]
	public void DetectFormat_ExtensionAifc_ReturnsAiff ()
	{
		// Arrange
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "song.aifc");

		// Assert
		Assert.AreEqual (MediaFormat.Aiff, format);
	}

	[TestMethod]
	public void DetectFormat_UnknownExtension_ReturnsUnknown ()
	{
		// Arrange
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "file.xyz");

		// Assert
		Assert.AreEqual (MediaFormat.Unknown, format);
	}

	[TestMethod]
	public void DetectFormat_EmptyPath_ReturnsUnknown ()
	{
		// Arrange
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var format = MediaFile.DetectFormat (data, "");

		// Assert
		Assert.AreEqual (MediaFormat.Unknown, format);
	}

	[TestMethod]
	public void OpenFromData_InvalidFlac_ReturnsFailure ()
	{
		// Arrange - FLAC magic but invalid structure
		var data = new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x00 };

		// Act
		var result = MediaFile.ReadFromData (data);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void OpenFromData_UnknownWithPath_IncludesPathInError ()
	{
		// Arrange
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

		// Act
		var result = MediaFile.ReadFromData (data, "testfile.xyz");

		// Assert
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "testfile.xyz");
	}

	[TestMethod]
	public void OpenFromData_ValidOggVorbis_ReturnsOggVorbisFile ()
	{
		// Arrange
		var data = TestBuilders.Ogg.CreateMinimalFile ();

		// Act
		var result = MediaFile.ReadFromData (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.OggVorbis, result.Format);
	}

	[TestMethod]
	public void OpenFromData_ValidOpus_ReturnsOpusFile ()
	{
		// Arrange
		var data = TestBuilders.Opus.CreateMinimalFile ();

		// Act
		var result = MediaFile.ReadFromData (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Opus, result.Format);
	}

	[TestMethod]
	public void OpenFromData_ValidWav_ReturnsWavFile ()
	{
		// Arrange
		var data = TestBuilders.Wav.CreateMinimal ();

		// Act
		var result = MediaFile.ReadFromData (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Wav, result.Format);
	}

	[TestMethod]
	public void OpenFromData_ValidAiff_ReturnsAiffFile ()
	{
		// Arrange
		var data = TestBuilders.Aiff.CreateMinimal ();

		// Act
		var result = MediaFile.ReadFromData (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Aiff, result.Format);
	}

	[TestMethod]
	public void OpenFromData_ValidMp4_ReturnsMp4File ()
	{
		// Arrange
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);

		// Act
		var result = MediaFile.ReadFromData (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (MediaFormat.Mp4, result.Format);
	}

	static byte[] CreateOggWithCodec (string codecMagic)
	{
		// Build minimal Ogg page with codec magic in first packet
		var data = new byte[100];
		// OggS magic
		data[0] = (byte)'O';
		data[1] = (byte)'g';
		data[2] = (byte)'g';
		data[3] = (byte)'S';
		data[4] = 0; // version
		data[5] = 0x02; // BOS flag
						// granule position (8 bytes) - zeros
						// serial number (4 bytes) - zeros
						// page sequence (4 bytes) - zeros
						// CRC (4 bytes) - zeros
		data[26] = 1; // segment count
		data[27] = (byte)codecMagic.Length; // segment size

		// Copy codec magic at data start
		for (var i = 0; i < codecMagic.Length && i < 8; i++)
			data[28 + i] = (byte)codecMagic[i];

		return data;
	}

	static byte[] CreateOggWithVorbisId ()
	{
		// Build minimal Ogg page with Vorbis identification header
		var data = new byte[100];
		// OggS magic
		data[0] = (byte)'O';
		data[1] = (byte)'g';
		data[2] = (byte)'g';
		data[3] = (byte)'S';
		data[4] = 0; // version
		data[5] = 0x02; // BOS flag
		data[26] = 1; // segment count
		data[27] = 30; // segment size (Vorbis ID header)

		// Vorbis identification header: packet type 1 + "vorbis"
		data[28] = 1; // packet type
		data[29] = (byte)'v';
		data[30] = (byte)'o';
		data[31] = (byte)'r';
		data[32] = (byte)'b';
		data[33] = (byte)'i';
		data[34] = (byte)'s';

		return data;
	}
}
