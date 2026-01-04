// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ogg;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
public class OggOpusFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Opus.CreateMinimalFile ();
		mockFs.AddFile ("/test.opus", data);

		var result = await OggOpusFile.ReadFromFileAsync ("/test.opus", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.opus", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await OggOpusFile.ReadFromFileAsync ("/missing.opus", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Opus.CreateMinimalFile ();
		mockFs.AddFile ("/test.opus", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		// OggOpusFile.ReadFromFileAsync signature: (path, fileSystem?, validateCrc, cancellationToken)
		var result = await OggOpusFile.ReadFromFileAsync ("/test.opus", mockFs, false, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_ReReadsAndSaves ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Opus.CreateMinimalFile ();
		mockFs.AddFile ("/source.opus", originalData);

		var readResult = await OggOpusFile.ReadFromFileAsync ("/source.opus", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.VorbisComment!.Title = "Updated Title";

		var saveResult = await file.SaveToFileAsync ("/dest.opus", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.opus"));

		var verifyResult = await OggOpusFile.ReadFromFileAsync ("/dest.opus", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_Succeeds ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Opus.CreateMinimalFile ();
		mockFs.AddFile ("/test.opus", originalData);

		var readResult = await OggOpusFile.ReadFromFileAsync ("/test.opus", mockFs);
		var file = readResult.File!;
		file.VorbisComment!.Title = "Overwritten Title";

		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await OggOpusFile.ReadFromFileAsync ("/test.opus", mockFs);
		Assert.AreEqual ("Overwritten Title", verifyResult.File!.VorbisComment?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		// Create a file without source path (parsed from bytes, not from file)
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);
		var file = result.File!;

		var mockFs = new MockFileSystem ();
		var saveResult = await file.SaveToFileAsync (mockFs);

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("source path", StringComparison.OrdinalIgnoreCase));
	}
}
