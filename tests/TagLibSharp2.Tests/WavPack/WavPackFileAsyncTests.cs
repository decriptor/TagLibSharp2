// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Tests.Core;
using TagLibSharp2.WavPack;

namespace TagLibSharp2.Tests.WavPack;

[TestClass]
public class WavPackFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.WavPack.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.wv", data);

		var result = await WavPackFile.ReadFromFileAsync ("/test.wv", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.wv", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await WavPackFile.ReadFromFileAsync ("/missing.wv", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.WavPack.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.wv", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await WavPackFile.ReadFromFileAsync ("/test.wv", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.WavPack.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/source.wv", originalData);

		var readResult = await WavPackFile.ReadFromFileAsync ("/source.wv", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.wv", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.wv"));

		var verifyResult = await WavPackFile.ReadFromFileAsync ("/dest.wv", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.ApeTag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.WavPack.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/test.wv", originalData);

		var readResult = await WavPackFile.ReadFromFileAsync ("/test.wv", mockFs);
		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await WavPackFile.ReadFromFileAsync ("/test.wv", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.ApeTag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.WavPack.CreateWithMetadata (title: "Test");
		var result = WavPackFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
