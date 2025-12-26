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
		var data = BuildMinimalFlacFile ();

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
		var data = BuildFlacWithVorbisComment ("Test Title", "Test Artist");

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Title", result.File.VorbisComment.Title);
		Assert.AreEqual ("Test Artist", result.File.VorbisComment.Artist);
	}

	[TestMethod]
	public void Read_WithPicture_ParsesPicture ()
	{
		var data = BuildFlacWithPicture ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotEmpty (result.File!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, result.File.Pictures[0].PictureType);
	}

	[TestMethod]
	public void Title_DelegatesToVorbisComment ()
	{
		var data = BuildFlacWithVorbisComment ("My Song", "");

		var result = FlacFile.Read (data);

		Assert.AreEqual ("My Song", result.File!.Title);
	}

	[TestMethod]
	public void Title_Set_CreatesVorbisComment ()
	{
		var data = BuildMinimalFlacFile ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		Assert.IsNotNull (file.VorbisComment);
		Assert.AreEqual ("New Title", file.Title);
	}

	[TestMethod]
	public void Artist_DelegatesToVorbisComment ()
	{
		var data = BuildFlacWithVorbisComment ("", "Test Artist");

		var result = FlacFile.Read (data);

		Assert.AreEqual ("Test Artist", result.File!.Artist);
	}

	[TestMethod]
	public void Pictures_ReturnsAllPictures ()
	{
		var data = BuildFlacWithMultiplePictures ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (2, result.File!.Pictures);
	}

	[TestMethod]
	public void AddPicture_AddsToPictureList ()
	{
		var data = BuildMinimalFlacFile ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		var picture = FlacPicture.FromBytes (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
		file.AddPicture (picture);

		Assert.HasCount (1, file.Pictures);
	}

	[TestMethod]
	public void RemovePictures_RemovesMatchingType ()
	{
		var data = BuildFlacWithPicture ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		file.RemovePictures (PictureType.FrontCover);

		Assert.IsEmpty (file.Pictures);
	}

	[TestMethod]
	public void MetadataSize_ReturnsCorrectSize ()
	{
		var data = BuildFlacWithVorbisComment ("Title", "Artist");

		var result = FlacFile.Read (data);

		Assert.IsGreaterThan (0, result.File!.MetadataSize);
	}

	[TestMethod]
	public void Read_StreamInfoTooSmall_ReturnsFailure ()
	{
		// Build FLAC with STREAMINFO block that's too small (33 bytes instead of 34)
		var data = BuildFlacWithInvalidStreamInfoSize (33);

		var result = FlacFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("STREAMINFO", result.Error!);
	}

	[TestMethod]
	public void Read_StreamInfoTooLarge_ReturnsFailure ()
	{
		// Build FLAC with STREAMINFO block that's too large (35 bytes instead of 34)
		var data = BuildFlacWithInvalidStreamInfoSize (35);

		var result = FlacFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("STREAMINFO", result.Error!);
	}

	[TestMethod]
	public void Properties_ParsesSampleRateFromStreamInfo ()
	{
		// Minimal file has 44100 Hz encoded in STREAMINFO
		var data = BuildMinimalFlacFile ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (44100, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void Properties_ParsesChannelsFromStreamInfo ()
	{
		// Minimal file has 2 channels (stereo) encoded in STREAMINFO
		var data = BuildMinimalFlacFile ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Properties_ParsesBitsPerSampleFromStreamInfo ()
	{
		// Minimal file has 16 bits per sample encoded in STREAMINFO
		var data = BuildMinimalFlacFile ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	public void Properties_MinimalFile_HasZeroDuration ()
	{
		// Minimal file has 0 total samples, so duration is zero
		var data = BuildMinimalFlacFile ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.Zero, result.File!.Properties.Duration);
	}

	[TestMethod]
	public void Properties_WithSamples_CalculatesDuration ()
	{
		// Build file with 88200 samples at 44100 Hz = 2 seconds
		var data = BuildFlacWithDuration (88200, 44100, 2, 16);

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
		var totalSamples = 96000UL * 5; // 480000 samples
		var data = BuildFlacWithDuration (totalSamples, 96000, 2, 24);

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
		var data = BuildMinimalFlacFile ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("FLAC", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Render_PreservesAudioData ()
	{
		// Build a minimal FLAC with some audio data
		var originalData = BuildFlacWithAudioData (new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
		var result = FlacFile.Read (originalData);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.File!.Render (originalData);

		// Re-read and verify audio data is preserved after metadata
		var reResult = FlacFile.Read (rendered.Span);
		Assert.IsTrue (reResult.IsSuccess);

		// Audio data should be after metadata
		var audioStart = reResult.File!.MetadataSize;
		var audioData = rendered.Span.Slice (audioStart);
		CollectionAssert.AreEqual (new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, audioData.ToArray ());
	}

	[TestMethod]
	public void Render_WritesVorbisComment ()
	{
		var data = BuildMinimalFlacFile ();
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
		var data = BuildMinimalFlacFile ();
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
		var data = BuildFlacWithVorbisComment ("Original Title", "Original Artist");
		var result = FlacFile.Read (data);
		var file = result.File!;

		// Modify
		file.Title = "Modified Title";
		file.Album = "New Album";

		// Render and re-read
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
		var data = BuildMinimalFlacFile ();
		var result = FlacFile.Read (data);
		var file = result.File!;
		file.Title = "Saved Title";

		var fs = new MockFileSystem ();
		var writeResult = file.SaveToFile ("/output.flac", data, fs);

		Assert.IsTrue (writeResult.IsSuccess);
		Assert.IsTrue (fs.FileExists ("/output.flac"));

		// Verify saved content
		var savedData = fs.ReadAllBytes ("/output.flac");
		var reResult = FlacFile.Read (savedData);
		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Saved Title", reResult.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesToFileSystem ()
	{
		var data = BuildMinimalFlacFile ();
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
		var data = BuildMinimalFlacFile ();
		var result = FlacFile.Read (data);
		var file = result.File!;

		var rendered = file.Render (data);

		// Rendered size should be larger than original due to padding
		Assert.IsGreaterThan (data.Length, rendered.Length);
	}

	#region Helper Methods

	static byte[] BuildMinimalFlacFile ()
	{
		using var builder = new BinaryDataBuilder ();

		// Magic: "fLaC"
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO block (required, always first)
		// Header: last=true, type=0, size=34
		builder.Add (new byte[] { 0x80, 0x00, 0x00, 0x22 });

		// Minimal STREAMINFO content (34 bytes)
		// min/max block size: 4096
		builder.Add (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		// min/max frame size: 0 (unknown)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
		// sample rate (20 bits), channels (3 bits), bits per sample (5 bits), total samples (36 bits)
		// 44100 Hz, 2 channels, 16 bits, 0 samples
		builder.Add (new byte[] { 0x0A, 0xC4, 0x42, 0xF0, 0x00, 0x00, 0x00, 0x00 });
		// MD5 signature (16 bytes - all zeros for minimal)
		builder.AddZeros (16);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildFlacWithVorbisComment (string title, string artist)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO (not last)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
		builder.Add (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
		builder.Add (new byte[] { 0x0A, 0xC4, 0x42, 0xF0, 0x00, 0x00, 0x00, 0x00 });
		builder.AddZeros (16);

		// VORBIS_COMMENT block
		var comment = new VorbisComment ("TagLibSharp2");
		if (!string.IsNullOrEmpty (title))
			comment.Title = title;
		if (!string.IsNullOrEmpty (artist))
			comment.Artist = artist;

		var commentData = comment.Render ();

		// Header: last=true, type=4
		var header = new FlacMetadataBlockHeader (true, FlacBlockType.VorbisComment, commentData.Length);
		builder.Add (header.Render ());
		builder.Add (commentData);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildFlacWithPicture ()
	{
		using var builder = new BinaryDataBuilder ();

		// Magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO (not last)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
		builder.Add (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
		builder.Add (new byte[] { 0x0A, 0xC4, 0x42, 0xF0, 0x00, 0x00, 0x00, 0x00 });
		builder.AddZeros (16);

		// PICTURE block
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (jpegData),
			100, 100, 24, 0);
		var pictureData = picture.RenderContent ();

		// Header: last=true, type=6
		var header = new FlacMetadataBlockHeader (true, FlacBlockType.Picture, pictureData.Length);
		builder.Add (header.Render ());
		builder.Add (pictureData);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildFlacWithMultiplePictures ()
	{
		using var builder = new BinaryDataBuilder ();

		// Magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO (not last)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
		builder.Add (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
		builder.Add (new byte[] { 0x0A, 0xC4, 0x42, 0xF0, 0x00, 0x00, 0x00, 0x00 });
		builder.AddZeros (16);

		// Picture 1 (front cover, not last)
		var pic1Data = new FlacPicture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (new byte[] { 0xFF, 0xD8 }),
			100, 100, 24, 0).RenderContent ();
		var header1 = new FlacMetadataBlockHeader (false, FlacBlockType.Picture, pic1Data.Length);
		builder.Add (header1.Render ());
		builder.Add (pic1Data);

		// Picture 2 (back cover, last)
		var pic2Data = new FlacPicture ("image/jpeg", PictureType.BackCover, "", new BinaryData (new byte[] { 0xFF, 0xD9 }),
			200, 200, 24, 0).RenderContent ();
		var header2 = new FlacMetadataBlockHeader (true, FlacBlockType.Picture, pic2Data.Length);
		builder.Add (header2.Render ());
		builder.Add (pic2Data);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildFlacWithInvalidStreamInfoSize (int size)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic: "fLaC"
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO block header: last=true, type=0, with custom size
		// First byte: 0x80 = last flag + type 0
		builder.Add ((byte)0x80);
		// Size in big-endian 3 bytes
		builder.Add ((byte)((size >> 16) & 0xFF));
		builder.Add ((byte)((size >> 8) & 0xFF));
		builder.Add ((byte)(size & 0xFF));

		// Add the data (with whatever size was requested)
		builder.AddZeros (size);

		return builder.ToBinaryData ().ToArray ();
	}

	/// <summary>
	/// Builds a FLAC file with specific audio properties encoded in STREAMINFO.
	/// </summary>
	/// <param name="totalSamples">Total number of audio samples.</param>
	/// <param name="sampleRate">Sample rate in Hz (1-655350).</param>
	/// <param name="channels">Number of channels (1-8).</param>
	/// <param name="bitsPerSample">Bits per sample (4-32).</param>
	static byte[] BuildFlacWithDuration (ulong totalSamples, int sampleRate, int channels, int bitsPerSample)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic: "fLaC"
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO block header: last=true, type=0, size=34
		builder.Add (new byte[] { 0x80, 0x00, 0x00, 0x22 });

		// min/max block size: 4096
		builder.Add (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		// min/max frame size: 0 (unknown)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

		// Bytes 10-17: sample rate (20 bits), channels-1 (3 bits), bps-1 (5 bits), total samples (36 bits)
		// Encode sample rate into bits 0-19 (big-endian, starting from bit 0 of byte 10)
		var sr = sampleRate & 0xFFFFF; // 20 bits
		var ch = (channels - 1) & 0x07; // 3 bits
		var bps = (bitsPerSample - 1) & 0x1F; // 5 bits
		var samplesUpper = (int)((totalSamples >> 32) & 0x0F); // upper 4 bits
		var samplesLower = (uint)(totalSamples & 0xFFFFFFFF); // lower 32 bits

		// Byte 10: sample rate bits 19-12
		builder.Add ((byte)((sr >> 12) & 0xFF));
		// Byte 11: sample rate bits 11-4
		builder.Add ((byte)((sr >> 4) & 0xFF));
		// Byte 12: sample rate bits 3-0, channels bits 2-0 shifted, bps bit 4
		builder.Add ((byte)(((sr & 0x0F) << 4) | ((ch & 0x07) << 1) | ((bps >> 4) & 0x01)));
		// Byte 13: bps bits 3-0, total samples upper 4 bits
		builder.Add ((byte)(((bps & 0x0F) << 4) | (samplesUpper & 0x0F)));

		// Bytes 14-17: total samples lower 32 bits (big-endian)
		builder.Add ((byte)((samplesLower >> 24) & 0xFF));
		builder.Add ((byte)((samplesLower >> 16) & 0xFF));
		builder.Add ((byte)((samplesLower >> 8) & 0xFF));
		builder.Add ((byte)(samplesLower & 0xFF));

		// MD5 signature (16 bytes - all zeros)
		builder.AddZeros (16);

		return builder.ToBinaryData ().ToArray ();
	}

	/// <summary>
	/// Builds a FLAC file with audio data after the metadata.
	/// </summary>
	static byte[] BuildFlacWithAudioData (byte[] audioData)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic: "fLaC"
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("fLaC"));

		// STREAMINFO block (last=true)
		builder.Add (new byte[] { 0x80, 0x00, 0x00, 0x22 });
		builder.Add (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
		builder.Add (new byte[] { 0x0A, 0xC4, 0x42, 0xF0, 0x00, 0x00, 0x00, 0x00 });
		builder.AddZeros (16);

		// Audio data
		builder.Add (audioData);

		return builder.ToBinaryData ().ToArray ();
	}

	#endregion
}
