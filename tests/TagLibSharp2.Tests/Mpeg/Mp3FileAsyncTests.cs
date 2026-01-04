// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Mpeg;

[TestClass]
public class Mp3FileAsyncTests
{
	static byte[] CreateMp3WithId3v2 (string? title = null)
	{
		var tag = new Id3v2Tag ();
		if (!string.IsNullOrEmpty (title))
			tag.Title = title;
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var result = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (result);
		audioData.CopyTo (result.AsSpan (tagData.Length));
		return result;
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = CreateMp3WithId3v2 (title: "Test");
		mockFs.AddFile ("/test.mp3", data);

		var result = await Mp3File.ReadFromFileAsync ("/test.mp3", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.mp3", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await Mp3File.ReadFromFileAsync ("/missing.mp3", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = CreateMp3WithId3v2 (title: "Test");
		mockFs.AddFile ("/test.mp3", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await Mp3File.ReadFromFileAsync ("/test.mp3", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithOriginalData_SavesSuccessfully ()
	{
		// Mp3File requires originalData for SaveToFileAsync
		var mockFs = new MockFileSystem ();
		var originalData = CreateMp3WithId3v2 (title: "Original");
		var readResult = Mp3File.Read (originalData);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/output.mp3", originalData, mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/output.mp3"));

		var verifyResult = await Mp3File.ReadFromFileAsync ("/output.mp3", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_PreservesMetadata ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = CreateMp3WithId3v2 (title: "Test");
		var readResult = Mp3File.Read (originalData);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Title = "New Title";
		file.Artist = "New Artist";

		var saveResult = await file.SaveToFileAsync ("/output.mp3", originalData, mockFs);
		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await Mp3File.ReadFromFileAsync ("/output.mp3", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("New Title", verifyResult.File!.Title);
		Assert.AreEqual ("New Artist", verifyResult.File.Artist);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		// Create a file without source path (parsed from bytes, not from file)
		var data = CreateMp3WithId3v2 (title: "Test");
		var result = Mp3File.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = CreateMp3WithId3v2 (title: "Original");
		mockFs.AddFile ("/source.mp3", originalData);

		var readResult = await Mp3File.ReadFromFileAsync ("/source.mp3", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Title = "Updated Title";

		// Save to different path, re-reading from source
		var saveResult = await file.SaveToFileAsync ("/dest.mp3", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.mp3"));

		var verifyResult = await Mp3File.ReadFromFileAsync ("/dest.mp3", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = CreateMp3WithId3v2 (title: "Original");
		mockFs.AddFile ("/test.mp3", originalData);

		var readResult = await Mp3File.ReadFromFileAsync ("/test.mp3", mockFs);
		var file = readResult.File!;
		file.Title = "Overwritten Title";

		// Save back to same file
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await Mp3File.ReadFromFileAsync ("/test.mp3", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.Title);
	}
}
