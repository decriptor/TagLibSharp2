// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
public class AiffFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Aiff.CreateMinimal ();
		mockFs.AddFile ("/test.aiff", data);

		var result = await AiffFile.ReadFromFileAsync ("/test.aiff", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.aiff", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await AiffFile.ReadFromFileAsync ("/missing.aiff", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Aiff.CreateMinimal ();
		mockFs.AddFile ("/test.aiff", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await AiffFile.ReadFromFileAsync ("/test.aiff", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithMetadata_SavesSuccessfully ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Aiff.CreateMinimal ();
		mockFs.AddFile ("/source.aiff", originalData);

		var readResult = await AiffFile.ReadFromFileAsync ("/source.aiff", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Tag = new Id3v2Tag { Title = "Updated Title" };

		var saveResult = await file.SaveToFileAsync ("/dest.aiff", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.aiff"));

		var verifyResult = await AiffFile.ReadFromFileAsync ("/dest.aiff", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Tag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_PreservesStructure ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Aiff.CreateMinimal ();
		mockFs.AddFile ("/test.aiff", originalData);

		var readResult = await AiffFile.ReadFromFileAsync ("/test.aiff", mockFs);
		var file = readResult.File!;
		file.Tag = new Id3v2Tag { Title = "Test", Artist = "Artist" };

		var saveResult = await file.SaveToFileAsync ("/output.aiff", mockFs);
		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await AiffFile.ReadFromFileAsync ("/output.aiff", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("Test", verifyResult.File!.Tag?.Title);
		Assert.AreEqual ("Artist", verifyResult.File.Tag?.Artist);
	}
}
