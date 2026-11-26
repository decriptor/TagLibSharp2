// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// A path save that changes chapters copies the audio. It does not read or write the file as one
/// buffer, and the bytes match <see cref="Mp4File.Render(ReadOnlySpan{byte})"/>. Render and the span
/// save stay one buffer.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public sealed class Mp4ChapterSaveStreamsTests
{
	const int AudioSampleSize = 64 * 1024;

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void SaveToFile_CopiesAudioWhenChaptersChange (bool replaceChapterMdat)
	{
		var (file, original, expected, fs) = Open (replaceChapterMdat);
		Check (original, expected, fs, () => file.SaveToFile ("/out.m4a", fs));
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task SaveToFileAsync_CopiesAudioWhenChaptersChange (bool replaceChapterMdat)
	{
		var (file, original, expected, fs) = Open (replaceChapterMdat);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.m4a", fs));
	}

	static (Mp4File File, byte[] Original, byte[] Expected, ChunkBoundedFileSystem Fs) Open (bool replaceChapterMdat)
	{
		var built = Mp4ChapterTestBuilder.Build (new () {
			MoovFirst = true,
			AudioSampleSize = AudioSampleSize,
			Track = [("Intro", 600), ("Next", 600)],
		});
		// A second edit replaces the chapter-only mdat an earlier save wrote after moov. The first
		// edit shares that mdat with the audio, so the old samples stay and a new mdat is inserted.
		var source = built;
		Mp4Chapter[] chapters = [new (TimeSpan.Zero, "One"), new (TimeSpan.FromSeconds (5), "Two")];
		if (replaceChapterMdat) {
			var first = Mp4File.Read (built).File!;
			first.Chapters = chapters;
			source = first.Render (built).ToArray ();
			chapters = [new (TimeSpan.Zero, "Alpha"), new (TimeSpan.FromSeconds (5), "Beta")];
		}

		Assert.IsTrue (source.Length > ChunkBoundedFileSystem.MaxChunk, $"file is {source.Length} bytes");
		var fs = new ChunkBoundedFileSystem (source.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.m4a", source);
		var opened = MediaFile.Read ("/in.m4a", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = (Mp4File)opened.File!;
		file.Chapters = chapters;
		var expected = file.Render (source).ToArray ();
		Assert.IsTrue (expected.Length > 0, "render produced no bytes");
		return (file, source, expected, fs);
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
