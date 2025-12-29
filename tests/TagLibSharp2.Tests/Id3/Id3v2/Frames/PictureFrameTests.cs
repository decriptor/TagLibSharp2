// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class PictureFrameTests
{
	// APIC Frame Format:
	// Offset  Size  Field
	// 0       1     Text encoding
	// 1       n     MIME type (null-terminated ASCII)
	// n+1     1     Picture type
	// n+2     m     Description (null-terminated in encoding)
	// n+m+2   rest  Picture data


	[TestMethod]
	public void Read_JpegFrontCover_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
		var data = CreateApicFrame (
			encoding: TextEncodingType.Latin1,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "Cover",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("image/jpeg", result.Frame!.MimeType);
		Assert.AreEqual (PictureType.FrontCover, result.Frame.PictureType);
		Assert.AreEqual ("Cover", result.Frame.Description);
		Assert.AreEqual (4, result.Frame.PictureData.Length);
		Assert.AreEqual (TextEncodingType.Latin1, result.Frame.TextEncoding);
	}

	[TestMethod]
	public void Read_PngBackCover_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
		var data = CreateApicFrame (
			encoding: TextEncodingType.Latin1,
			mimeType: "image/png",
			pictureType: PictureType.BackCover,
			description: "Back",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("image/png", result.Frame!.MimeType);
		Assert.AreEqual (PictureType.BackCover, result.Frame.PictureType);
	}

	[TestMethod]
	public void Read_EmptyDescription_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0x01, 0x02, 0x03 };
		var data = CreateApicFrame (
			encoding: TextEncodingType.Latin1,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Description);
		Assert.AreEqual (3, result.Frame.PictureData.Length);
	}

	[TestMethod]
	public void Read_Utf16Description_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0x01, 0x02 };
		var data = CreateApicFrame (
			encoding: TextEncodingType.Utf16WithBom,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "Album Art",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Album Art", result.Frame!.Description);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x00 }; // Just encoding byte

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_NoMimeTypeTerminator_ReturnsFailure ()
	{
		// Encoding + MIME without null terminator
		var data = new byte[] { 0x00, (byte)'i', (byte)'m', (byte)'a', (byte)'g', (byte)'e' };

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}



	[TestMethod]
	[DataRow (PictureType.Other, (byte)0x00)]
	[DataRow (PictureType.FileIcon, (byte)0x01)]
	[DataRow (PictureType.OtherFileIcon, (byte)0x02)]
	[DataRow (PictureType.FrontCover, (byte)0x03)]
	[DataRow (PictureType.BackCover, (byte)0x04)]
	[DataRow (PictureType.LeafletPage, (byte)0x05)]
	[DataRow (PictureType.Media, (byte)0x06)]
	[DataRow (PictureType.LeadArtist, (byte)0x07)]
	[DataRow (PictureType.Artist, (byte)0x08)]
	[DataRow (PictureType.Conductor, (byte)0x09)]
	[DataRow (PictureType.Band, (byte)0x0A)]
	[DataRow (PictureType.Composer, (byte)0x0B)]
	[DataRow (PictureType.Lyricist, (byte)0x0C)]
	[DataRow (PictureType.RecordingLocation, (byte)0x0D)]
	[DataRow (PictureType.DuringRecording, (byte)0x0E)]
	[DataRow (PictureType.DuringPerformance, (byte)0x0F)]
	[DataRow (PictureType.MovieScreenCapture, (byte)0x10)]
	[DataRow (PictureType.ColouredFish, (byte)0x11)]
	[DataRow (PictureType.Illustration, (byte)0x12)]
	[DataRow (PictureType.BandLogo, (byte)0x13)]
	[DataRow (PictureType.PublisherLogo, (byte)0x14)]
	public void Read_PictureTypes_ParseCorrectly (PictureType expectedType, byte typeValue)
	{
		var imageData = new byte[] { 0x01 };
		var data = CreateApicFrameWithType (typeValue, imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (expectedType, result.Frame!.PictureType);
	}



	[TestMethod]
	public void Render_BasicFrame_CreatesCorrectData ()
	{
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF };
		var frame = new PictureFrame (
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "Test",
			pictureData: imageData);

		var data = frame.RenderContent ();

		// Should start with encoding byte (UTF-8 is default)
		Assert.AreEqual (0x03, data[0]); // UTF-8 encoding

		// Should contain MIME type
		Assert.IsTrue (ContainsSequence (data.Span, "image/jpeg"u8));
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		var original = new PictureFrame (
			mimeType: "image/png",
			pictureType: PictureType.FrontCover,
			description: "Album Cover",
			pictureData: imageData);

		var rendered = original.RenderContent ();
		var result = PictureFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.MimeType, result.Frame!.MimeType);
		Assert.AreEqual (original.PictureType, result.Frame.PictureType);
		Assert.AreEqual (original.Description, result.Frame.Description);
		Assert.AreEqual (original.PictureData.Length, result.Frame.PictureData.Length);

		// Verify image data matches
		for (var i = 0; i < imageData.Length; i++)
			Assert.AreEqual (imageData[i], result.Frame.PictureData.Span[i]);
	}

	[TestMethod]
	public void Render_EmptyDescription_RoundTrips ()
	{
		var imageData = new byte[] { 0x01, 0x02, 0x03 };
		var original = new PictureFrame ("image/jpeg", PictureType.BackCover, "", imageData);

		var rendered = original.RenderContent ();
		var result = PictureFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Description);
	}



	[TestMethod]
	public void Read_Utf8Description_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0x01, 0x02 };
		var data = CreateApicFrameWithEncoding (
			encoding: TextEncodingType.Utf8,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "Test UTF-8 こんにちは",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test UTF-8 こんにちは", result.Frame!.Description);
		Assert.AreEqual (TextEncodingType.Utf8, result.Frame.TextEncoding);
	}

	[TestMethod]
	public void Read_Utf16BEDescription_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0x01, 0x02 };
		var data = CreateApicFrameWithEncoding (
			encoding: TextEncodingType.Utf16BE,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "Test",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Frame!.Description);
		Assert.AreEqual (TextEncodingType.Utf16BE, result.Frame.TextEncoding);
	}

	[TestMethod]
	public void Read_InvalidEncodingV24_ReturnsFailure ()
	{
		// Encoding byte 4 is invalid for any version
		var data = new byte[] { 0x04, (byte)'i', (byte)'m', (byte)'g', 0x00, 0x03, 0x00 };

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "Invalid text encoding");
	}

	[TestMethod]
	public void Read_Utf8InV23_ReturnsFailure ()
	{
		// UTF-8 (0x03) is not valid in ID3v2.3 - only Latin1 and UTF-16 with BOM
		var data = new byte[] { 0x03, (byte)'i', (byte)'m', (byte)'g', 0x00, 0x03, 0x00, 0x01 };

		var result = PictureFrame.Read (data, Id3v2Version.V23);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "Invalid text encoding for ID3v2.3");
	}

	[TestMethod]
	public void Read_Utf16BEInV23_ReturnsFailure ()
	{
		// UTF-16BE (0x02) is not valid in ID3v2.3
		var data = new byte[] { 0x02, (byte)'i', (byte)'m', (byte)'g', 0x00, 0x03, 0x00, 0x00, 0x01 };

		var result = PictureFrame.Read (data, Id3v2Version.V23);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error!, "Invalid text encoding for ID3v2.3");
	}

	[TestMethod]
	public void Read_Latin1InV23_ParsesCorrectly ()
	{
		var imageData = new byte[] { 0x01, 0x02 };
		var data = CreateApicFrame (
			encoding: TextEncodingType.Latin1,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "Cover",
			imageData: imageData);

		var result = PictureFrame.Read (data, Id3v2Version.V23);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Cover", result.Frame!.Description);
	}



	[TestMethod]
	public void Read_ZeroLengthImage_ParsesCorrectly ()
	{
		var data = CreateApicFrame (
			encoding: TextEncodingType.Latin1,
			mimeType: "image/jpeg",
			pictureType: PictureType.FrontCover,
			description: "",
			imageData: []);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.Frame!.PictureData.Length);
	}

	[TestMethod]
	public void Read_LargeImage_ParsesCorrectly ()
	{
		var largeImage = new byte[100_000]; // 100KB image
		for (var i = 0; i < largeImage.Length; i++)
			largeImage[i] = (byte)(i % 256);

		var data = CreateApicFrame (
			encoding: TextEncodingType.Latin1,
			mimeType: "image/png",
			pictureType: PictureType.FrontCover,
			description: "Large image",
			imageData: largeImage);

		var result = PictureFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (100_000, result.Frame!.PictureData.Length);

		// Verify data integrity
		for (var i = 0; i < largeImage.Length; i++)
			Assert.AreEqual (largeImage[i], result.Frame.PictureData.Span[i]);
	}

	[TestMethod]
	public void Read_EmptyMimeType_DefaultsToImage ()
	{
		// Empty MIME type should default to "image/" per spec
		var bytes = new List<byte> {
			0x00, // Latin-1 encoding
			0x00, // Empty MIME type (just null terminator)
			0x03, // Front cover
			0x00, // Empty description
			0x01, 0x02 // Image data
		};

		var result = PictureFrame.Read (bytes.ToArray (), Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("image/", result.Frame!.MimeType);
	}



	[TestMethod]
	public void DetectMimeType_Jpeg_ReturnsCorrectType ()
	{
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
		Assert.AreEqual ("image/jpeg", PictureFrame.DetectMimeType (jpegData));
	}

	[TestMethod]
	public void DetectMimeType_Png_ReturnsCorrectType ()
	{
		var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		Assert.AreEqual ("image/png", PictureFrame.DetectMimeType (pngData));
	}

	[TestMethod]
	public void DetectMimeType_Gif_ReturnsCorrectType ()
	{
		var gifData = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };
		Assert.AreEqual ("image/gif", PictureFrame.DetectMimeType (gifData));
	}

	[TestMethod]
	public void DetectMimeType_Bmp_ReturnsCorrectType ()
	{
		var bmpData = new byte[] { 0x42, 0x4D, 0x00, 0x00 };
		Assert.AreEqual ("image/bmp", PictureFrame.DetectMimeType (bmpData));
	}

	[TestMethod]
	public void DetectMimeType_WebP_ReturnsCorrectType ()
	{
		var webpData = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00 };
		Assert.AreEqual ("image/webp", PictureFrame.DetectMimeType (webpData));
	}

	[TestMethod]
	public void DetectMimeType_Unknown_ReturnsOctetStream ()
	{
		var unknownData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
		Assert.AreEqual ("application/octet-stream", PictureFrame.DetectMimeType (unknownData));
	}

	[TestMethod]
	public void DetectMimeType_FallbackToExtension_Works ()
	{
		var unknownData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
		Assert.AreEqual ("image/jpeg", PictureFrame.DetectMimeType (unknownData, "cover.jpg"));
		Assert.AreEqual ("image/png", PictureFrame.DetectMimeType (unknownData, "cover.PNG"));
		Assert.AreEqual ("image/tiff", PictureFrame.DetectMimeType (unknownData, "image.tiff"));
	}



	[TestMethod]
	public void FromBytes_DetectsMimeType ()
	{
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
		var frame = PictureFrame.FromBytes (jpegData);

		Assert.AreEqual ("image/jpeg", frame.MimeType);
		Assert.AreEqual (PictureType.FrontCover, frame.PictureType);
		Assert.AreEqual ("", frame.Description);
	}

	[TestMethod]
	public void FromBytes_WithCustomParameters_Works ()
	{
		var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		var frame = PictureFrame.FromBytes (pngData, PictureType.BackCover, "Back cover");

		Assert.AreEqual ("image/png", frame.MimeType);
		Assert.AreEqual (PictureType.BackCover, frame.PictureType);
		Assert.AreEqual ("Back cover", frame.Description);
	}



	static byte[] CreateApicFrameWithEncoding (TextEncodingType encoding, string mimeType,
		PictureType pictureType, string description, byte[] imageData)
	{
		var bytes = new List<byte> { (byte)encoding };

		// MIME type (null-terminated ASCII)
		bytes.AddRange (System.Text.Encoding.ASCII.GetBytes (mimeType));
		bytes.Add (0);

		// Picture type
		bytes.Add ((byte)pictureType);

		// Description (null-terminated in encoding)
		switch (encoding) {
			case TextEncodingType.Utf16WithBom:
				bytes.Add (0xFF); // BOM
				bytes.Add (0xFE);
				bytes.AddRange (System.Text.Encoding.Unicode.GetBytes (description));
				bytes.Add (0); // null terminator
				bytes.Add (0);
				break;
			case TextEncodingType.Utf16BE:
				bytes.AddRange (System.Text.Encoding.BigEndianUnicode.GetBytes (description));
				bytes.Add (0);
				bytes.Add (0);
				break;
			case TextEncodingType.Utf8:
				bytes.AddRange (System.Text.Encoding.UTF8.GetBytes (description));
				bytes.Add (0);
				break;
			default: // Latin1
				bytes.AddRange (System.Text.Encoding.Latin1.GetBytes (description));
				bytes.Add (0);
				break;
		}

		// Image data
		bytes.AddRange (imageData);

		return bytes.ToArray ();
	}

	static byte[] CreateApicFrame (TextEncodingType encoding, string mimeType,
		PictureType pictureType, string description, byte[] imageData)
	{
		var bytes = new List<byte> { (byte)encoding };

		// MIME type (null-terminated ASCII)
		bytes.AddRange (System.Text.Encoding.ASCII.GetBytes (mimeType));
		bytes.Add (0);

		// Picture type
		bytes.Add ((byte)pictureType);

		// Description (null-terminated in encoding)
		if (encoding == TextEncodingType.Utf16WithBom) {
			bytes.Add (0xFF); // BOM
			bytes.Add (0xFE);
			bytes.AddRange (System.Text.Encoding.Unicode.GetBytes (description));
			bytes.Add (0); // null terminator
			bytes.Add (0);
		} else {
			bytes.AddRange (System.Text.Encoding.Latin1.GetBytes (description));
			bytes.Add (0);
		}

		// Image data
		bytes.AddRange (imageData);

		return bytes.ToArray ();
	}

	static byte[] CreateApicFrameWithType (byte pictureType, byte[] imageData)
	{
		var bytes = new List<byte> {
			0x00, // Latin-1 encoding
			(byte)'i', (byte)'m', (byte)'g', 0x00, // MIME type
			pictureType, // Picture type
			0x00 // Empty description
		};
		bytes.AddRange (imageData);
		return bytes.ToArray ();
	}

	static bool ContainsSequence (ReadOnlySpan<byte> data, ReadOnlySpan<byte> sequence)
	{
		for (var i = 0; i <= data.Length - sequence.Length; i++) {
			if (data.Slice (i, sequence.Length).SequenceEqual (sequence))
				return true;
		}
		return false;
	}

}
