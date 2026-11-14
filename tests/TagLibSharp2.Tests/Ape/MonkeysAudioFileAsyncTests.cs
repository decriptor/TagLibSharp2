// Copyright (c) 2025-2026 Stephen Shaw and contributors
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
	public async Task SaveToFileAsync_MismatchedLyricsSizeBeforeId3_DoesNotDeleteAudio ()
	{
		// Lyrics3 v2 size digits do not land on LYRICSBEGIN. An async save must not eat the audio.
		var audio = TestBuilders.MonkeysAudio.CreateMinimal ();
		var pad = new byte[40];
		Array.Fill (pad, (byte)0x51);
		var lyrics = new byte[11 + 6 + 9];
		"LYRICSBEGIN"u8.CopyTo (lyrics);
		"000099"u8.CopyTo (lyrics.AsSpan (11));
		"LYRICS200"u8.CopyTo (lyrics.AsSpan (17));
		var id3 = new byte[128];
		"TAG"u8.CopyTo (id3);
		"KeepMe"u8.CopyTo (id3.AsSpan (3));

		var data = new byte[audio.Length + pad.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		pad.CopyTo (data, audio.Length);
		lyrics.CopyTo (data, audio.Length + pad.Length);
		id3.CopyTo (data, audio.Length + pad.Length + lyrics.Length);

		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/song.ape", data);

		var read = await MonkeysAudioFile.ReadFromFileAsync ("/song.ape", mockFs);
		Assert.IsTrue (read.IsSuccess, read.Error);

		read.File!.EnsureApeTag ().Title = "New";
		var save = await read.File.SaveToFileAsync (mockFs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		var saved = mockFs.ReadAllBytes ("/song.ape");
		Assert.IsTrue (saved.Length >= audio.Length, "save shrank the file below the audio");
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
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
