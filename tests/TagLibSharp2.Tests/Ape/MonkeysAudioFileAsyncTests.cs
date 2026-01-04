// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Ape;

[TestClass]
public class MonkeysAudioFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.MonkeysAudio.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.ape", data);

		var result = await MonkeysAudioFile.ReadFromFileAsync ("/test.ape", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.ape", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await MonkeysAudioFile.ReadFromFileAsync ("/missing.ape", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.MonkeysAudio.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.ape", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await MonkeysAudioFile.ReadFromFileAsync ("/test.ape", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.MonkeysAudio.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/source.ape", originalData);

		var readResult = await MonkeysAudioFile.ReadFromFileAsync ("/source.ape", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.ape", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.ape"));

		var verifyResult = await MonkeysAudioFile.ReadFromFileAsync ("/dest.ape", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.ApeTag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.MonkeysAudio.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/test.ape", originalData);

		var readResult = await MonkeysAudioFile.ReadFromFileAsync ("/test.ape", mockFs);
		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await MonkeysAudioFile.ReadFromFileAsync ("/test.ape", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.ApeTag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.MonkeysAudio.CreateWithMetadata (title: "Test");
		var result = MonkeysAudioFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
