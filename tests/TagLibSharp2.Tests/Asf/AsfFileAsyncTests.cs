// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
public class AsfFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test");
		mockFs.AddFile ("/test.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/test.wma", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.wma", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await AsfFile.ReadFromFileAsync ("/missing.wma", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test");
		mockFs.AddFile ("/test.wma", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await AsfFile.ReadFromFileAsync ("/test.wma", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = AsfTestBuilder.CreateMinimalWma (title: "Original");
		mockFs.AddFile ("/source.wma", originalData);

		var readResult = await AsfFile.ReadFromFileAsync ("/source.wma", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Tag.Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.wma", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.wma"));

		var verifyResult = await AsfFile.ReadFromFileAsync ("/dest.wma", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Tag.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = AsfTestBuilder.CreateMinimalWma (title: "Original");
		mockFs.AddFile ("/test.wma", originalData);

		var readResult = await AsfFile.ReadFromFileAsync ("/test.wma", mockFs);
		var file = readResult.File!;
		file.Tag.Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await AsfFile.ReadFromFileAsync ("/test.wma", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.Tag.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		// Create a file without source path (parsed from bytes, not from file)
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test");
		var result = AsfFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
