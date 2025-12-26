// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
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

	#endregion
}
