// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public class AiffFilePictureTests
{
	static BinaryData CreateMinimalAiff ()
	{
		using var builder = new BinaryDataBuilder (512);

		// FORM header
		builder.AddStringLatin1 ("FORM");
		builder.AddUInt32BE (30);
		builder.AddStringLatin1 ("AIFF");

		// COMM chunk
		builder.AddStringLatin1 ("COMM");
		builder.AddUInt32BE (18);
		builder.AddUInt16BE (2);      // channels
		builder.AddUInt32BE (0);      // sample frames
		builder.AddUInt16BE (16);     // bits per sample
		// 80-bit extended sample rate (44100 Hz)
		builder.Add (new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

		// SSND chunk (empty)
		builder.AddStringLatin1 ("SSND");
		builder.AddUInt32BE (8);
		builder.AddUInt32BE (0);  // offset
		builder.AddUInt32BE (0);  // block size

		return builder.ToBinaryData ();
	}

	[TestMethod]
	public void Pictures_ReturnsEmptyWhenNoId3Tag ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		var pictures = aiff.Pictures;

		Assert.IsNotNull (pictures);
		Assert.AreEqual (0, pictures.Length);
	}

	[TestMethod]
	public void Pictures_ReturnsId3Pictures ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		aiff.Tag = new Id3v2Tag ();
		var pictureData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
		var picture = new PictureFrame ("image/jpeg", PictureType.FrontCover, "", new BinaryData (pictureData));
		aiff.Tag.AddPicture (picture);

		var pictures = aiff.Pictures;

		Assert.IsNotNull (pictures);
		Assert.AreEqual (1, pictures.Length);
		Assert.AreEqual (PictureType.FrontCover, pictures[0].PictureType);
	}

	[TestMethod]
	public void Pictures_ReturnsMultipleId3Pictures ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		aiff.Tag = new Id3v2Tag ();
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

		aiff.Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.FrontCover, "", new BinaryData (jpegData)));
		aiff.Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.BackCover, "", new BinaryData (jpegData)));

		var pictures = aiff.Pictures;

		Assert.IsNotNull (pictures);
		Assert.AreEqual (2, pictures.Length);
	}

	[TestMethod]
	public void HasPictures_ReturnsTrueWhenPicturesExist ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		aiff.Tag = new Id3v2Tag ();
		aiff.Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.FrontCover, "", new BinaryData ([0xFF, 0xD8])));

		Assert.IsTrue (aiff.HasPictures);
	}

	[TestMethod]
	public void HasPictures_ReturnsFalseWhenNoPictures ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		Assert.IsFalse (aiff.HasPictures);
	}

	[TestMethod]
	public void CoverArt_ReturnsFirstFrontCover ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		aiff.Tag = new Id3v2Tag ();
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		aiff.Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.BackCover, "back", new BinaryData (jpegData)));
		aiff.Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.FrontCover, "front", new BinaryData (jpegData)));

		var coverArt = aiff.CoverArt;

		Assert.IsNotNull (coverArt);
		Assert.AreEqual (PictureType.FrontCover, coverArt.PictureType);
	}

	[TestMethod]
	public void CoverArt_ReturnsNullWhenNoFrontCover ()
	{
		AiffFile.TryParse (CreateMinimalAiff (), out var aiff);
		Assert.IsNotNull (aiff);

		aiff.Tag = new Id3v2Tag ();
		aiff.Tag.AddPicture (new PictureFrame ("image/jpeg", PictureType.BackCover, "", new BinaryData ([0xFF, 0xD8])));

		Assert.IsNull (aiff.CoverArt);
	}
}
