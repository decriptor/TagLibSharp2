// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// A path save copies MP4 audio. It does not read or write the file as one buffer,
/// and the bytes match <see cref="Mp4File.Render(ReadOnlySpan{byte})"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public sealed class Mp4SaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Title = "Changed";
		Check (original, file.Render (original).ToArray (), fs, () => file.SaveToFile ("/out.m4a", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Title = "Changed";
		var expected = file.Render (original).ToArray ();
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.m4a", fs));
	}

	static (Mp4File File, byte[] Original, ChunkBoundedFileSystem Fs) Open ()
	{
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);

		var original = TestBuilders.Mp4.CreateWithAudioData (audio);
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.m4a", original);
		var opened = MediaFile.Read ("/in.m4a", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		return ((Mp4File)opened.File!, original, fs);
	}

	static void Check (byte[] original, byte[] expected, ChunkBoundedFileSystem fs, Func<FileWriteResult> save)
	{
		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = save ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		AssertSaved (original, expected, fs, result, allocated);
	}

	static async Task CheckAsync (
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		Func<Task<FileWriteResult>> save)
	{
		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = await save ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		AssertSaved (original, expected, fs, result, allocated);
	}

	static void AssertSaved (
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		FileWriteResult save,
		long allocated)
	{
		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.m4a"));
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
