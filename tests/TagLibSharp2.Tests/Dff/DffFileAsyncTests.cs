// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Dff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Dff;

[TestClass]
public class DffFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Dff.CreateMinimal ();
		mockFs.AddFile ("/test.dff", data);

		var result = await DffFile.ReadFromFileAsync ("/test.dff", mockFs);

		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.dff", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await DffFile.ReadFromFileAsync ("/missing.dff", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Dff.CreateMinimal ();
		mockFs.AddFile ("/test.dff", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await DffFile.ReadFromFileAsync ("/test.dff", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Dff.CreateMinimal ();
		mockFs.AddFile ("/source.dff", originalData);

		var readResult = await DffFile.ReadFromFileAsync ("/source.dff", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.dff", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.dff"));

		var verifyResult = await DffFile.ReadFromFileAsync ("/dest.dff", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Id3v2Tag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Dff.CreateMinimal ();
		mockFs.AddFile ("/test.dff", originalData);

		var readResult = await DffFile.ReadFromFileAsync ("/test.dff", mockFs);
		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await DffFile.ReadFromFileAsync ("/test.dff", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.Id3v2Tag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.Dff.CreateMinimal ();
		var result = DffFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
