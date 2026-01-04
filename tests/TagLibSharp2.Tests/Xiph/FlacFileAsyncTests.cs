// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Tests.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
public class FlacFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Flac.CreateMinimal ();
		mockFs.AddFile ("/test.flac", data);

		var result = await FlacFile.ReadFromFileAsync ("/test.flac", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.flac", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await FlacFile.ReadFromFileAsync ("/missing.flac", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Flac.CreateMinimal ();
		mockFs.AddFile ("/test.flac", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await FlacFile.ReadFromFileAsync ("/test.flac", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithOriginalData_SavesSuccessfully ()
	{
		// FlacFile requires originalData for SaveToFileAsync
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Flac.CreateWithVorbisComment (title: "Original");
		var readResult = FlacFile.Read (originalData);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.VorbisComment!.Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/output.flac", originalData, mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/output.flac"));

		var verifyResult = await FlacFile.ReadFromFileAsync ("/output.flac", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_PreservesAudioData ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Flac.CreateWithVorbisComment (title: "Test");
		var readResult = FlacFile.Read (originalData);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.VorbisComment!.Title = "New Title";

		var saveResult = await file.SaveToFileAsync ("/output.flac", originalData, mockFs);
		Assert.IsTrue (saveResult.IsSuccess);

		// Verify file was written and can be re-read
		var verifyResult = await FlacFile.ReadFromFileAsync ("/output.flac", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("New Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		// Create a file without source path (parsed from bytes, not from file)
		var data = TestBuilders.Flac.CreateWithVorbisComment (title: "Test");
		var result = FlacFile.Read (data);
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
		var originalData = TestBuilders.Flac.CreateWithVorbisComment (title: "Original");
		mockFs.AddFile ("/source.flac", originalData);

		var readResult = await FlacFile.ReadFromFileAsync ("/source.flac", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.VorbisComment!.Title = "Updated Title";

		// Save to different path, re-reading from source
		var saveResult = await file.SaveToFileAsync ("/dest.flac", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.flac"));

		var verifyResult = await FlacFile.ReadFromFileAsync ("/dest.flac", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Flac.CreateWithVorbisComment (title: "Original");
		mockFs.AddFile ("/test.flac", originalData);

		var readResult = await FlacFile.ReadFromFileAsync ("/test.flac", mockFs);
		var file = readResult.File!;
		file.VorbisComment!.Title = "Overwritten Title";

		// Save back to same file
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await FlacFile.ReadFromFileAsync ("/test.flac", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.VorbisComment?.Title);
	}
}
