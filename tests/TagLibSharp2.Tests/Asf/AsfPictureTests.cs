// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
public class AsfPictureTests
{
	[TestMethod]
	public void Constructor_InitializesProperties ()
	{
		// Arrange
		var data = new BinaryData ([0xFF, 0xD8, 0xFF, 0xE0]); // JPEG header

		// Act
		var picture = new AsfPicture ("image/jpeg", PictureType.FrontCover, "Album Art", data);

		// Assert
		Assert.AreEqual ("image/jpeg", picture.MimeType);
		Assert.AreEqual (PictureType.FrontCover, picture.PictureType);
		Assert.AreEqual ("Album Art", picture.Description);
		Assert.AreEqual (4, picture.PictureData.Length);
	}

	[TestMethod]
	public void Render_ProducesValidBinaryFormat ()
	{
		// Arrange
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
		var picture = new AsfPicture ("image/png", PictureType.BackCover, "Back", new BinaryData (imageData));

		// Act
		var rendered = picture.Render ();

		// Assert
		Assert.IsTrue (rendered.Length > 9); // Minimum size

		// First byte should be picture type (4 = BackCover)
		Assert.AreEqual (4, rendered[0]);

		// Bytes 1-4 should be image data length (4 bytes, little-endian)
		var dataLength = BitConverter.ToUInt32 (rendered.ToArray (), 1);
		Assert.AreEqual (4u, dataLength);
	}

	[TestMethod]
	public void Parse_RecoversOriginalData ()
	{
		// Arrange
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
		var original = new AsfPicture ("image/jpeg", PictureType.FrontCover, "Front Cover", new BinaryData (imageData));

		// Act
		var rendered = original.Render ();
		var parsed = AsfPicture.Parse (rendered.Span);

		// Assert
		Assert.IsNotNull (parsed);
		Assert.AreEqual (original.MimeType, parsed.MimeType);
		Assert.AreEqual (original.PictureType, parsed.PictureType);
		Assert.AreEqual (original.Description, parsed.Description);
		CollectionAssert.AreEqual (original.PictureData.ToArray (), parsed.PictureData.ToArray ());
	}

	[TestMethod]
	public void Parse_ReturnsNull_ForDataTooShort ()
	{
		// Arrange
		var shortData = new byte[] { 0x03, 0x00, 0x00 }; // Only 3 bytes, minimum is 9

		// Act
		var result = AsfPicture.Parse (shortData);

		// Assert
		Assert.IsNull (result);
	}

	[TestMethod]
	public void Parse_ReturnsNull_WhenMimeTypeNotTerminated ()
	{
		// Arrange - MIME type without null terminator
		var badData = new byte[20];
		badData[0] = 0x03; // FrontCover
		BitConverter.GetBytes ((uint)4).CopyTo (badData, 1); // Data length
															 // Fill rest with non-null bytes (no terminator)
		for (int i = 5; i < 20; i++)
			badData[i] = 0x41; // 'A'

		// Act
		var result = AsfPicture.Parse (badData);

		// Assert
		Assert.IsNull (result);
	}

	[TestMethod]
	public void Parse_HandlesEmptyStrings ()
	{
		// Arrange
		var imageData = new byte[] { 0x01, 0x02, 0x03 };
		var original = new AsfPicture ("", PictureType.Other, "", new BinaryData (imageData));

		// Act
		var rendered = original.Render ();
		var parsed = AsfPicture.Parse (rendered.Span);

		// Assert
		Assert.IsNotNull (parsed);
		Assert.AreEqual ("", parsed.MimeType);
		Assert.AreEqual ("", parsed.Description);
	}

	[TestMethod]
	public void Parse_HandlesUnicodeDescription ()
	{
		// Arrange - Japanese characters
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF };
		var original = new AsfPicture ("image/jpeg", PictureType.FrontCover, "表紙画像", new BinaryData (imageData));

		// Act
		var rendered = original.Render ();
		var parsed = AsfPicture.Parse (rendered.Span);

		// Assert
		Assert.IsNotNull (parsed);
		Assert.AreEqual ("表紙画像", parsed.Description);
	}

	[TestMethod]
	public void FromPicture_ReturnsSameInstance_WhenAlreadyAsfPicture ()
	{
		// Arrange
		var original = new AsfPicture ("image/png", PictureType.FrontCover, "Test", new BinaryData ([0x89, 0x50]));

		// Act
		var result = AsfPicture.FromPicture (original);

		// Assert
		Assert.AreSame (original, result);
	}

	[TestMethod]
	public void FromPicture_CopiesData_FromOtherIPicture ()
	{
		// Arrange
		var data = new BinaryData ([0xFF, 0xD8]);
		var mockPicture = new TestPicture ("image/jpeg", PictureType.BackCover, "Test Pic", data);

		// Act
		var result = AsfPicture.FromPicture (mockPicture);

		// Assert
		Assert.AreEqual ("image/jpeg", result.MimeType);
		Assert.AreEqual (PictureType.BackCover, result.PictureType);
		Assert.AreEqual ("Test Pic", result.Description);
		CollectionAssert.AreEqual (data.ToArray (), result.PictureData.ToArray ());
	}

	[TestMethod]
	public void FromPicture_ThrowsArgumentNullException_WhenNull ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => AsfPicture.FromPicture (null!));
	}

	[TestMethod]
	public void Parse_ValidatesDataLength ()
	{
		// Arrange - Claim large data length but provide short data
		var badData = new byte[30];
		badData[0] = 0x03; // FrontCover
		BitConverter.GetBytes ((uint)1000).CopyTo (badData, 1); // Claim 1000 bytes of data
																// Write null-terminated MIME
		badData[5] = 0x00;
		badData[6] = 0x00;
		// Write null-terminated description
		badData[7] = 0x00;
		badData[8] = 0x00;
		// Only have a few bytes of actual data

		// Act
		var result = AsfPicture.Parse (badData);

		// Assert
		Assert.IsNull (result);
	}

	// Simple test implementation of IPicture
	sealed class TestPicture : IPicture
	{
		public TestPicture (string mimeType, PictureType pictureType, string description, BinaryData data)
		{
			MimeType = mimeType;
			PictureType = pictureType;
			Description = description;
			PictureData = data;
		}

		public string MimeType { get; }
		public PictureType PictureType { get; }
		public string Description { get; }
		public BinaryData PictureData { get; }
	}
}
