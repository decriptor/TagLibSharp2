// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
public class WavFileAsyncTests
{
	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Wav.CreateMinimal ();
		mockFs.AddFile ("/test.wav", data);

		var result = await WavFile.ReadFromFileAsync ("/test.wav", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("/test.wav", result.File.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await WavFile.ReadFromFileAsync ("/missing.wav", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithCancellation_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		var data = TestBuilders.Wav.CreateMinimal ();
		mockFs.AddFile ("/test.wav", data);

		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await WavFile.ReadFromFileAsync ("/test.wav", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithMetadata_SavesSuccessfully ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Wav.CreateMinimal ();
		mockFs.AddFile ("/source.wav", originalData);

		var readResult = await WavFile.ReadFromFileAsync ("/source.wav", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "Updated Title" };

		var saveResult = await file.SaveToFileAsync ("/dest.wav", mockFs);

		Assert.IsTrue (saveResult.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/dest.wav"));

		var verifyResult = await WavFile.ReadFromFileAsync ("/dest.wav", mockFs);
		Assert.AreEqual ("Updated Title", verifyResult.File!.Id3v2Tag?.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_PreservesStructure ()
	{
		var mockFs = new MockFileSystem ();
		var originalData = TestBuilders.Wav.CreateMinimal ();
		mockFs.AddFile ("/test.wav", originalData);

		var readResult = await WavFile.ReadFromFileAsync ("/test.wav", mockFs);
		var file = readResult.File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "Test", Artist = "Artist" };

		var saveResult = await file.SaveToFileAsync ("/output.wav", mockFs);
		Assert.IsTrue (saveResult.IsSuccess);

		var verifyResult = await WavFile.ReadFromFileAsync ("/output.wav", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("Test", verifyResult.File!.Id3v2Tag?.Title);
		Assert.AreEqual ("Artist", verifyResult.File.Id3v2Tag?.Artist);
	}
}
