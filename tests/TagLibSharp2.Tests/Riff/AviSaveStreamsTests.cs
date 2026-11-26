// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

/// <summary>
/// A path save copies AVI sample bytes. It does not read or write the file as one buffer,
/// and the bytes match <see cref="AviFile.Render"/>. An absolute <c>idx1</c> shifts when the
/// tags do not fit; a relative <c>idx1</c> that fits in the reserved <c>JUNK</c> does not move.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Avi")]
public sealed class AviSaveStreamsTests
{
	// One frame is larger than a chunk, so a single read of that sample fails.
	// Several frames keep fixed save overhead under half the file.
	const int Width = 400;
	const int Height = 200;
	const int Frames = 8;

	// 0: the title fits in JUNK and the relative idx1 stays. 1: absolute idx1 shifts when the
	// data moves. 2: a relative idx1 stays put while OpenDML indexes move with the samples.
	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	public void SaveToFile_CopiesSamplesWhenTheTitleChanges (int mode)
	{
		var (file, original, expected, fs) = Open (mode);
		Check (original, expected, fs, () => file.SaveToFile ("/out.avi", fs));
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	public async Task SaveToFileAsync_CopiesSamplesWhenTheTitleChanges (int mode)
	{
		var (file, original, expected, fs) = Open (mode);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.avi", fs));
	}

	[TestMethod]
	public void SaveToFile_ManyTinyChunks_DoesNotKeepEveryChunk ()
	{
		var (file, original, expected, fs) = OpenTiny ();
		Check (original, expected, fs, () => file.SaveToFile ("/out.avi", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_ManyTinyChunks_DoesNotKeepEveryChunk ()
	{
		var (file, original, expected, fs) = OpenTiny ();
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.avi", fs));
	}

	static (AviFile File, byte[] Original, byte[] Expected, ChunkBoundedFileSystem Fs) OpenTiny ()
	{
		var original = AviFileTests.WithTinyChunks (100_000);
		Assert.IsTrue (original.Length > ChunkBoundedFileSystem.MaxChunk, $"file is {original.Length} bytes");
		var expected = Expected (original, "y");
		Assert.AreEqual (original.Length, expected.Length, "empty JUNK before movi holds the new title");
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.avi", original);
		var opened = MediaFile.Read ("/in.avi", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = (AviFile)opened.File!;
		file.InfoTag.Title = "y";
		fs.ResetSourceLengthGets ();
		return (file, original, expected, fs);
	}

	static (AviFile File, byte[] Original, byte[] Expected, ChunkBoundedFileSystem Fs) Open (int mode)
	{
		var moves = mode != 0;
		var builder = new AviTestBuilder {
			Width = Width,
			Height = Height,
			Frames = Frames,
			Audio = false,
			JunkBeforeMovi = moves ? 0 : 1016,
			OldIndex = mode == 1 ? AviOldIndex.Absolute : AviOldIndex.Relative,
			OpenDml = mode == 2,
		};
		builder.Info.Add (("INAM", "Movie"));
		var original = builder.Build ();
		Assert.IsTrue (original.Length > ChunkBoundedFileSystem.MaxChunk, $"file is {original.Length} bytes");
		Assert.IsTrue (Width * Height > ChunkBoundedFileSystem.MaxChunk, "a frame must not fit in one chunk");

		var expected = Expected (original);
		if (moves)
			Assert.IsTrue (expected.Length > original.Length, "the tags did not fit, so the file must grow");
		else
			Assert.AreEqual (original.Length, expected.Length, "a tag that fits stays in place");
		var fs = new ChunkBoundedFileSystem (Math.Max (original.Length, expected.Length) + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.avi", original);
		var opened = MediaFile.Read ("/in.avi", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = (AviFile)opened.File!;
		file.InfoTag.Title = "Changed";
		fs.ResetSourceLengthGets ();
		return (file, original, expected, fs);
	}

	static byte[] Expected (byte[] original) => Expected (original, "Changed");

	static byte[] Expected (byte[] original, string title)
	{
		var oracle = AviFile.Read (original).File!;
		oracle.InfoTag.Title = title;
		return oracle.Render (original).ToArray ();
	}

	static void Check (byte[] original, byte[] expected, ChunkBoundedFileSystem fs, Func<FileWriteResult> save)
	{
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
		AssertSameBytes (expected, fs.Bytes ("/out.avi"));
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
