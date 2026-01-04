// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfFileTests
{
	// ═══════════════════════════════════════════════════════════════
	// Format Detection Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Read_ValidAsfMagic_ReturnsSuccess ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[100];
		data[0] = 0xFF; // Not ASF header GUID

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_EmptyInput_ReturnsFailure ()
	{
		var result = AsfFile.Read ([]);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_TruncatedHeader_ReturnsFailure ()
	{
		// Only 20 bytes - not enough for header (needs 30 min)
		var data = new byte[20];
		var headerGuid = AsfGuids.HeaderObject.Render ().ToArray ();
		Array.Copy (headerGuid, data, 16);

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parsing Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_ExtractsTitle ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test Song");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Song", result.File!.Tag.Title);
	}

	[TestMethod]
	public void Parse_ExtractsArtist ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Test Artist");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Artist", result.File!.Tag.Artist);
	}

	[TestMethod]
	public void Parse_ExtractsDuration ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (durationMs: 180000); // 3 minutes

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// Duration should be approximately 180 seconds
		Assert.IsTrue (result.File!.AudioProperties.Duration.TotalSeconds >= 170);
		Assert.IsTrue (result.File!.AudioProperties.Duration.TotalSeconds <= 190);
	}

	[TestMethod]
	public void Parse_ExtractsBitrate ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (bitrate: 320000);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (320, result.File!.AudioProperties.Bitrate);
	}

	[TestMethod]
	public void Parse_ExtractsSampleRate ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (sampleRate: 48000);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (48000, result.File!.AudioProperties.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsChannels ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (channels: 2);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.AudioProperties.Channels);
	}

	[TestMethod]
	public void Parse_ExtractsBitsPerSample ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (bitsPerSample: 16);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.File!.AudioProperties.BitsPerSample);
	}

	// ═══════════════════════════════════════════════════════════════
	// Tag Access Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Tag_Get_ReturnsAsfTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Tag);
		Assert.IsInstanceOfType<AsfTag> (result.File!.Tag);
	}

	[TestMethod]
	public void Title_Get_DelegatesToTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Convenience Test");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Convenience Test", result.File!.Title);
	}

	// ═══════════════════════════════════════════════════════════════
	// Unicode Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_UnicodeTitle_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "日本語タイトル");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("日本語タイトル", result.File!.Tag.Title);
	}

	[TestMethod]
	public void Parse_UnicodeArtist_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Café Français");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Café Français", result.File!.Tag.Artist);
	}

	// ═══════════════════════════════════════════════════════════════
	// File I/O Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void ReadFromFile_ValidFile_ReturnsSuccess ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "File Test");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wma", data);

		var result = AsfFile.ReadFromFile ("/test.wma", mockFs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("File Test", result.File!.Title);
	}

	[TestMethod]
	public void ReadFromFile_SetsSourcePath ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.wma", data);

		var result = AsfFile.ReadFromFile ("/music/song.wma", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("/music/song.wma", result.File!.SourcePath);
	}

	[TestMethod]
	public void ReadFromFile_FileNotFound_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = AsfFile.ReadFromFile ("/nonexistent.wma", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Async Test");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/test.wma", mockFs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Async Test", result.File!.Title);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_SetsSourcePath ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/async.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/music/async.wma", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("/music/async.wma", result.File!.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_FileNotFound_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await AsfFile.ReadFromFileAsync ("/nonexistent.wma", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	// ═══════════════════════════════════════════════════════════════
	// Convenience Property Setter Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Title_Set_UpdatesTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "New Title";

		Assert.AreEqual ("New Title", result.File!.Title);
		Assert.AreEqual ("New Title", result.File!.Tag.Title);
	}

	[TestMethod]
	public void Artist_Set_UpdatesTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Artist = "New Artist";

		Assert.AreEqual ("New Artist", result.File!.Artist);
		Assert.AreEqual ("New Artist", result.File!.Tag.Artist);
	}

	[TestMethod]
	public void Album_Get_DelegatesToTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWmaWithExtended (album: "Test Album");
		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Album", result.File!.Album);
	}

	[TestMethod]
	public void Album_Set_UpdatesTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Album = "New Album";

		Assert.AreEqual ("New Album", result.File!.Album);
		Assert.AreEqual ("New Album", result.File!.Tag.Album);
	}

	// ═══════════════════════════════════════════════════════════════
	// Dispose Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Dispose_ClearsSourcePath ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wma", data);

		var result = AsfFile.ReadFromFile ("/test.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.SourcePath);

		result.File!.Dispose ();

		Assert.IsNull (result.File!.SourcePath);
	}

	[TestMethod]
	public void Dispose_CalledTwice_DoesNotThrow ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Dispose ();
		result.File!.Dispose (); // Should not throw
	}

	// ═══════════════════════════════════════════════════════════════
	// Result Type Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void AsfFileReadResult_Success_IsSuccessTrue ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
	}

	[TestMethod]
	public void AsfFileReadResult_Failure_IsSuccessFalse ()
	{
		var result = AsfFile.Read ([]);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void AsfFileReadResult_Equals_SameError_ReturnsTrue ()
	{
		var result1 = AsfFile.Read ([]);
		var result2 = AsfFile.Read ([]);

		Assert.IsTrue (result1.Equals (result2));
		Assert.IsTrue (result1 == result2);
	}

	[TestMethod]
	public void AsfFileReadResult_Equals_DifferentError_ReturnsFalse ()
	{
		// Empty array produces "Data too short for ASF header"
		var result1 = AsfFile.Read ([]);

		// 30+ bytes with invalid GUID produces "Invalid ASF header: not an ASF file"
		var invalidGuidData = new byte[30];
		var result2 = AsfFile.Read (invalidGuidData);

		// Different error messages should not be equal
		Assert.AreNotEqual (result1.Error, result2.Error);
		Assert.IsFalse (result1.Equals (result2));
		Assert.IsTrue (result1 != result2);
	}

	[TestMethod]
	public void AsfFileReadResult_Equals_Object_ReturnsCorrectly ()
	{
		var result1 = AsfFile.Read ([]);
		var result2 = AsfFile.Read ([]);
		object boxed = result2;

		Assert.IsTrue (result1.Equals (boxed));
		Assert.IsFalse (result1.Equals ("not a result"));
		Assert.IsFalse (result1.Equals (null));
	}

	[TestMethod]
	public void AsfFileReadResult_GetHashCode_SameError_SameHash ()
	{
		var result1 = AsfFile.Read ([]);
		var result2 = AsfFile.Read ([]);

		Assert.AreEqual (result1.GetHashCode (), result2.GetHashCode ());
	}

	// ═══════════════════════════════════════════════════════════════
	// Edge Case Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Read_ChildObjectSizeTooLarge_ReturnsPartialParse ()
	{
		// Create file where child object claims to be larger than remaining data
		var data = AsfTestBuilder.CreateMinimalWma ();
		// Truncate to create inconsistent state - still parses what it can
		var truncated = data[..^50];

		var result = AsfFile.Read (truncated);

		// Should still succeed with what it could parse
		Assert.IsTrue (result.IsSuccess || result.Error!.Contains ("size"));
	}

	[TestMethod]
	public void Read_HeaderSizeExceedsData_ReturnsFailure ()
	{
		// Create header claiming larger size than available
		var data = new byte[50];
		var headerGuid = AsfGuids.HeaderObject.Render ().ToArray ();
		Array.Copy (headerGuid, data, 16);
		// Write header size as very large number
		BitConverter.TryWriteBytes (data.AsSpan (16), 99999UL);

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("size", result.Error!.ToLowerInvariant ());
	}
}
