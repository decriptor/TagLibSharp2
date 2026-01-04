// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Musepack;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Musepack;

[TestClass]
public class MusepackFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Musepack.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.mpc", data);

		var result = await MusepackFile.ReadFromFileAsync ("/test.mpc", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.mpc", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await MusepackFile.ReadFromFileAsync ("/missing.mpc", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Musepack.CreateWithMetadata (title: "Test");
		mockFs.AddFile ("/test.mpc", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await MusepackFile.ReadFromFileAsync ("/test.mpc", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Musepack.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/source.mpc", originalData);

		var readResult = await MusepackFile.ReadFromFileAsync ("/source.mpc", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.mpc", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.mpc"));

		var verifyResult = await MusepackFile.ReadFromFileAsync ("/dest.mpc", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.ApeTag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Musepack.CreateWithMetadata (title: "Original");
		mockFs.AddFile ("/test.mpc", originalData);

		var readResult = await MusepackFile.ReadFromFileAsync ("/test.mpc", mockFs);
		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await MusepackFile.ReadFromFileAsync ("/test.mpc", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.ApeTag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = TestBuilders.Musepack.CreateWithMetadata (title: "Test");
		var result = MusepackFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
