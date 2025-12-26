// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class FlacPictureTests
{
	[TestMethod]
	public void Constructor_SetsAllProperties ()
	{
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var picture = new FlacPicture (
			"image/jpeg",
			PictureType.FrontCover,
			"Album Cover",
			new BinaryData (imageData),
			500, 500, 24, 0);

		Assert.AreEqual ("image/jpeg", picture.MimeType);
		Assert.AreEqual (PictureType.FrontCover, picture.PictureType);
		Assert.AreEqual ("Album Cover", picture.Description);
		Assert.AreEqual (4, picture.PictureData.Length);
		Assert.AreEqual (500u, picture.Width);
		Assert.AreEqual (500u, picture.Height);
		Assert.AreEqual (24u, picture.ColorDepth);
		Assert.AreEqual (0u, picture.ColorCount);
	}

	[TestMethod]
	public void Constructor_InheritsPictureMethods ()
	{
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		var picture = new FlacPicture (
			"image/png",
			PictureType.BackCover,
			"",
			new BinaryData (imageData),
			100, 100, 32, 0);

		// Test inherited ToStream method
		using var stream = picture.ToStream ();
		Assert.AreEqual (8, stream.Length);

		// Cast to base Picture type
		Picture basePicture = picture;
		Assert.AreEqual ("image/png", basePicture.MimeType);
	}

	[TestMethod]
	public void Read_ValidPicture_ParsesCorrectly ()
	{
		// Build a FLAC PICTURE block manually (big-endian format)
		using var builder = new BinaryDataBuilder ();

		// Picture type (4 bytes BE) - FrontCover = 3
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x03 });

		// MIME type length (4 bytes BE)
		var mimeBytes = System.Text.Encoding.ASCII.GetBytes ("image/jpeg");
		builder.Add (new byte[] { 0x00, 0x00, 0x00, (byte)mimeBytes.Length });
		builder.Add (mimeBytes);

		// Description length (4 bytes BE)
		var descBytes = System.Text.Encoding.UTF8.GetBytes ("Test Cover");
		builder.Add (new byte[] { 0x00, 0x00, 0x00, (byte)descBytes.Length });
		builder.Add (descBytes);

		// Width (4 bytes BE)
		builder.Add (new byte[] { 0x00, 0x00, 0x01, 0xF4 }); // 500

		// Height (4 bytes BE)
		builder.Add (new byte[] { 0x00, 0x00, 0x01, 0xF4 }); // 500

		// Color depth (4 bytes BE)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x18 }); // 24

		// Color count (4 bytes BE)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 }); // 0 for non-indexed

		// Picture data length (4 bytes BE)
		var pictureData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
		builder.Add (new byte[] { 0x00, 0x00, 0x00, (byte)pictureData.Length });
		builder.Add (pictureData);

		var data = builder.ToBinaryData ();
		var result = FlacPicture.Read (data.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("image/jpeg", result.Picture!.MimeType);
		Assert.AreEqual (PictureType.FrontCover, result.Picture.PictureType);
		Assert.AreEqual ("Test Cover", result.Picture.Description);
		Assert.AreEqual (500u, result.Picture.Width);
		Assert.AreEqual (500u, result.Picture.Height);
		Assert.AreEqual (24u, result.Picture.ColorDepth);
		Assert.AreEqual (0u, result.Picture.ColorCount);
		Assert.AreEqual (6, result.Picture.PictureData.Length);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var result = FlacPicture.Read (new byte[] { 0x00, 0x00, 0x00, 0x03 });

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Render_ProducesValidData ()
	{
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var picture = new FlacPicture (
			"image/jpeg",
			PictureType.FrontCover,
			"My Cover",
			new BinaryData (imageData),
			640, 480, 24, 0);

		var rendered = picture.RenderContent ();

		// Parse it back
		var result = FlacPicture.Read (rendered.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("image/jpeg", result.Picture!.MimeType);
		Assert.AreEqual (PictureType.FrontCover, result.Picture.PictureType);
		Assert.AreEqual ("My Cover", result.Picture.Description);
		Assert.AreEqual (640u, result.Picture.Width);
		Assert.AreEqual (480u, result.Picture.Height);
		Assert.AreEqual (24u, result.Picture.ColorDepth);
		Assert.AreEqual (0u, result.Picture.ColorCount);
		CollectionAssert.AreEqual (imageData, result.Picture.PictureData.ToArray ());
	}

	[TestMethod]
	public void FromFile_CreatesCorrectPicture ()
	{
		// Create a temp file with JPEG magic bytes
		var tempPath = Path.GetTempFileName ();
		try {
			var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
			File.WriteAllBytes (tempPath, jpegData);

			var picture = FlacPicture.FromFile (tempPath, PictureType.FrontCover, "From File");

			Assert.AreEqual ("image/jpeg", picture.MimeType);
			Assert.AreEqual (PictureType.FrontCover, picture.PictureType);
			Assert.AreEqual ("From File", picture.Description);
			Assert.AreEqual (10, picture.PictureData.Length);
			// Dimensions default to 0 when loading from file (no image parsing)
			Assert.AreEqual (0u, picture.Width);
			Assert.AreEqual (0u, picture.Height);
		} finally {
			File.Delete (tempPath);
		}
	}

	[TestMethod]
	public void FromBytes_AutoDetectsMimeType ()
	{
		var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

		var picture = FlacPicture.FromBytes (pngData);

		Assert.AreEqual ("image/png", picture.MimeType);
		Assert.AreEqual (PictureType.FrontCover, picture.PictureType);
	}

	[TestMethod]
	public void Read_EmptyDescription_Works ()
	{
		using var builder = new BinaryDataBuilder ();

		// Picture type
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x03 });

		// MIME type
		var mimeBytes = System.Text.Encoding.ASCII.GetBytes ("image/png");
		builder.Add (new byte[] { 0x00, 0x00, 0x00, (byte)mimeBytes.Length });
		builder.Add (mimeBytes);

		// Empty description
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 });

		// Dimensions
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x64 }); // Width 100
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x64 }); // Height 100
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x18 }); // Depth 24
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Colors 0

		// Picture data
		var pictureData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
		builder.Add (new byte[] { 0x00, 0x00, 0x00, (byte)pictureData.Length });
		builder.Add (pictureData);

		var data = builder.ToBinaryData ();
		var result = FlacPicture.Read (data.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Picture!.Description);
	}

	[TestMethod]
	public void Read_MimeLengthOverflow_ReturnsFailure ()
	{
		using var builder = new BinaryDataBuilder ();

		// Picture type
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x03 });

		// MIME type length > int.MaxValue (0xFFFFFFFF)
		builder.Add (new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

		// Pad to minimum 32 bytes
		builder.AddZeros (24);

		var result = FlacPicture.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("overflow", result.Error!.ToLowerInvariant ());
	}

	[TestMethod]
	public void Read_DescriptionLengthOverflow_ReturnsFailure ()
	{
		using var builder = new BinaryDataBuilder ();

		// Picture type
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x03 });

		// MIME type (valid, 0 bytes)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 });

		// Description length > int.MaxValue (0xFFFFFFFF)
		builder.Add (new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

		// Pad to minimum 32 bytes (4+4+4 = 12 used, need 20 more)
		builder.AddZeros (20);

		var result = FlacPicture.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("overflow", result.Error!.ToLowerInvariant ());
	}

	[TestMethod]
	public void Read_PictureDataLengthOverflow_ReturnsFailure ()
	{
		using var builder = new BinaryDataBuilder ();

		// Picture type
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x03 });

		// MIME type
		var mimeBytes = System.Text.Encoding.ASCII.GetBytes ("image/png");
		builder.Add (new byte[] { 0x00, 0x00, 0x00, (byte)mimeBytes.Length });
		builder.Add (mimeBytes);

		// Description (empty)
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 });

		// Dimensions
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x64 }); // Width 100
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x64 }); // Height 100
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x18 }); // Depth 24
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Colors 0

		// Picture data length > int.MaxValue (0xFFFFFFFF)
		builder.Add (new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

		var result = FlacPicture.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("overflow", result.Error!.ToLowerInvariant ());
	}
}
