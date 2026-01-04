// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ogg;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
public class OggVorbisFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Ogg.CreateMinimalFile ();
		mockFs.AddFile ("/test.ogg", data);

		var result = await OggVorbisFile.ReadFromFileAsync ("/test.ogg", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.ogg", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await OggVorbisFile.ReadFromFileAsync ("/missing.ogg", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Ogg.CreateMinimalFile ();
		mockFs.AddFile ("/test.ogg", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		// OggVorbisFile.ReadFromFileAsync signature: (path, fileSystem?, validateCrc, cancellationToken)
		var result = await OggVorbisFile.ReadFromFileAsync ("/test.ogg", mockFs, false, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Ogg.CreateMinimalFile ();
		mockFs.AddFile ("/source.ogg", originalData);

		var readResult = await OggVorbisFile.ReadFromFileAsync ("/source.ogg", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.VorbisComment!.Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.ogg", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.ogg"));

		var verifyResult = await OggVorbisFile.ReadFromFileAsync ("/dest.ogg", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Ogg.CreateMinimalFile ();
		mockFs.AddFile ("/test.ogg", originalData);

		var readResult = await OggVorbisFile.ReadFromFileAsync ("/test.ogg", mockFs);
		var file = readResult.File!;
		file.VorbisComment!.Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await OggVorbisFile.ReadFromFileAsync ("/test.ogg", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		// Create a file without source path (parsed from bytes, not from file)
		var data = TestBuilders.Ogg.CreateMinimalFile ();
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
