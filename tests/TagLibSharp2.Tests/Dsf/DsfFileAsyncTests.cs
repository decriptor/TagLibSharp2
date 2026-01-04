// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Dsf;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Dsf;

[TestClass]
public class DsfFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Dsf.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.dsf", data);

		var result = await DsfFile.ReadFromFileAsync ("/test.dsf", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.dsf", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await DsfFile.ReadFromFileAsync ("/missing.dsf", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Dsf.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.dsf", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await DsfFile.ReadFromFileAsync ("/test.dsf", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Dsf.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/source.dsf", originalData);

		var readResult = await DsfFile.ReadFromFileAsync ("/source.dsf", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.dsf", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.dsf"));

		var verifyResult = await DsfFile.ReadFromFileAsync ("/dest.dsf", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Id3v2Tag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Dsf.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/test.dsf", originalData);

		var readResult = await DsfFile.ReadFromFileAsync ("/test.dsf", mockFs);
		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await DsfFile.ReadFromFileAsync ("/test.dsf", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.Id3v2Tag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.Dsf.CreateWithMetadata (title: "Test");
		var result = DsfFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
