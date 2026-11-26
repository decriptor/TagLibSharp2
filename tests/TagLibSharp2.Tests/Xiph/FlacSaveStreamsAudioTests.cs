// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

/// <summary>
/// A path save copies the FLAC audio. It does not read or write the file as one buffer,
/// and the bytes match <see cref="FlacFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Flac")]
public sealed class FlacSaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var original = Build ();
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.flac", original);

		var read = MediaFile.Read ("/in.flac", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = (FlacFile)read.File!;
		file.Title = "Changed";
		var expected = file.Render (original).ToArray ();

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = file.SaveToFile ("/out.flac", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.flac"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var original = Build ();
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.flac", original);

		var read = MediaFile.Read ("/in.flac", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = (FlacFile)read.File!;
		file.Title = "Changed";
		var expected = file.Render (original).ToArray ();

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await file.SaveToFileAsync ("/out.flac", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.flac"));
	}

	static byte[] Build ()
	{
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);
		return TestBuilders.Flac.CreateWithAudioData (audio);
	}

	static void AssertSameBytes (byte[] expected, byte[] actual)
	{
		Assert.AreEqual (expected.Length, actual.Length, "saved length");
		var mismatch = -1;
		var length = Math.Min (expected.Length, actual.Length);
		for (var i = 0; i < length; i++) {
			if (expected[i] == actual[i])
				continue;
			mismatch = i;
			break;
		}

		Assert.AreEqual (-1, mismatch, $"first mismatched byte at {mismatch}");
	}
}
