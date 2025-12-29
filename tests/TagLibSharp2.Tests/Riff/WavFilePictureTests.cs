// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class WavFilePictureTests
{
	static BinaryData CreateMinimalWav ()
	{
		using var builder = new BinaryDataBuilder (512);

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (36);
		builder.AddStringLatin1 ("WAVE");

		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);
		builder.AddUInt16LE (2);
		builder.AddUInt32LE (44100);
		builder.AddUInt32LE (176400);
		builder.AddUInt16LE (4);
		builder.AddUInt16LE (16);

		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	[TestMethod]
	public void Pictures_ReturnsEmptyWhenNoId3Tag ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		var pictures = wav.Pictures;

		Assert.IsNotNull (pictures);
		Assert.AreEqual (0, pictures.Length);
	}

	[TestMethod]
	public void Pictures_ReturnsId3Pictures ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag ();
		var pictureData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
		var picture = new PictureFrame ("image/jpeg", PictureType.FrontCover, "", new BinaryData (pictureData));
		wav.Id3v2Tag.AddPicture (picture);

		var pictures = wav.Pictures;

		Assert.IsNotNull (pictures);
		Assert.AreEqual (1, pictures.Length);
		Assert.AreEqual (PictureType.FrontCover, pictures[0].PictureType);
	}

	[TestMethod]
	public void Pictures_ReturnsMultipleId3Pictures ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag ();
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

		wav.Id3v2Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.FrontCover, "", new BinaryData (jpegData)));
		wav.Id3v2Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.BackCover, "", new BinaryData (jpegData)));

		var pictures = wav.Pictures;

		Assert.IsNotNull (pictures);
		Assert.AreEqual (2, pictures.Length);
	}

	[TestMethod]
	public void HasPictures_ReturnsTrueWhenPicturesExist ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag ();
		wav.Id3v2Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.FrontCover, "", new BinaryData ([0xFF, 0xD8])));

		Assert.IsTrue (wav.HasPictures);
	}

	[TestMethod]
	public void HasPictures_ReturnsFalseWhenNoPictures ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		Assert.IsFalse (wav.HasPictures);
	}

	[TestMethod]
	public void CoverArt_ReturnsFirstFrontCover ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag ();
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		wav.Id3v2Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.BackCover, "back", new BinaryData (jpegData)));
		wav.Id3v2Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.FrontCover, "front", new BinaryData (jpegData)));

		var coverArt = wav.CoverArt;

		Assert.IsNotNull (coverArt);
		Assert.AreEqual (PictureType.FrontCover, coverArt.PictureType);
	}

	[TestMethod]
	public void CoverArt_ReturnsNullWhenNoFrontCover ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag ();
		wav.Id3v2Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.BackCover, "", new BinaryData ([0xFF, 0xD8])));

		Assert.IsNull (wav.CoverArt);
	}
}
