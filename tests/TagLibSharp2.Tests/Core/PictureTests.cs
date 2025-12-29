// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Core")]
public class PictureTests
{

	[TestMethod]
	public void DetectMimeType_Jpeg_ReturnsCorrectType ()
	{
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
		Assert.AreEqual ("image/jpeg", Picture.DetectMimeType (jpegData));
	}

	[TestMethod]
	public void DetectMimeType_Png_ReturnsCorrectType ()
	{
		var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		Assert.AreEqual ("image/png", Picture.DetectMimeType (pngData));
	}

	[TestMethod]
	public void DetectMimeType_Gif_ReturnsCorrectType ()
	{
		var gifData = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };
		Assert.AreEqual ("image/gif", Picture.DetectMimeType (gifData));
	}

	[TestMethod]
	public void DetectMimeType_Bmp_ReturnsCorrectType ()
	{
		var bmpData = new byte[] { 0x42, 0x4D, 0x00, 0x00 };
		Assert.AreEqual ("image/bmp", Picture.DetectMimeType (bmpData));
	}

	[TestMethod]
	public void DetectMimeType_WebP_ReturnsCorrectType ()
	{
		var webpData = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00 };
		Assert.AreEqual ("image/webp", Picture.DetectMimeType (webpData));
	}

	[TestMethod]
	public void DetectMimeType_Unknown_ReturnsOctetStream ()
	{
		var unknownData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
		Assert.AreEqual ("application/octet-stream", Picture.DetectMimeType (unknownData));
	}

	[TestMethod]
	public void DetectMimeType_FallbackToExtension_Works ()
	{
		var unknownData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
		Assert.AreEqual ("image/jpeg", Picture.DetectMimeType (unknownData, "cover.jpg"));
		Assert.AreEqual ("image/png", Picture.DetectMimeType (unknownData, "cover.PNG"));
		Assert.AreEqual ("image/tiff", Picture.DetectMimeType (unknownData, "image.tiff"));
	}

	[TestMethod]
	public void DetectMimeType_EmptyData_ReturnsOctetStream ()
	{
		Assert.AreEqual ("application/octet-stream", Picture.DetectMimeType ([]));
	}



	[TestMethod]
	public void SaveToFile_WritesCorrectData ()
	{
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
		var picture = new TestPicture ("image/jpeg", PictureType.FrontCover, "Test", new BinaryData (imageData));

		var tempPath = Path.GetTempFileName ();
		try {
			picture.SaveToFile (tempPath);
			var savedData = File.ReadAllBytes (tempPath);
			CollectionAssert.AreEqual (imageData, savedData);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public void ToStream_ReturnsCorrectData ()
	{
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		var picture = new TestPicture ("image/png", PictureType.BackCover, "Test PNG", new BinaryData (imageData));

		using var stream = picture.ToStream ();
		var streamData = stream.ToArray ();
		CollectionAssert.AreEqual (imageData, streamData);
	}

	[TestMethod]
	public void ToStream_IsNotWritable ()
	{
		var imageData = new byte[] { 0x01, 0x02, 0x03 };
		var picture = new TestPicture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (imageData));

		using var stream = picture.ToStream ();
		Assert.IsFalse (stream.CanWrite);
	}



	/// <summary>
	/// Concrete implementation for testing the abstract Picture class.
	/// </summary>
	sealed class TestPicture : Picture
	{
		public TestPicture (string mimeType, PictureType pictureType, string description, BinaryData pictureData)
		{
			MimeType = mimeType;
			PictureType = pictureType;
			Description = description;
			PictureData = pictureData;
		}

		public override string MimeType { get; }
		public override PictureType PictureType { get; }
		public override string Description { get; }
		public override BinaryData PictureData { get; }
	}

}
