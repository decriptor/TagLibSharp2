// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;

using static TagLibSharp2.Tests.Mpeg.MpegPsFileTests;
using static TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// A path save copies an MPEG program stream. It does not read or write the file as one buffer,
/// and the bytes match <see cref="MpegPsFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsSaveStreamsAudioTests
{
	[TestMethod]
	public void SaveToFile_CopiesTheStreamWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Tag!.Title = "Changed";
		Check (original, file.Render (original).ToArray (), fs, () => file.SaveToFile ("/out.mpg", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesTheStreamWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Tag!.Title = "Changed";
		var expected = file.Render (original).ToArray ();
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.mpg", fs));
	}

	static (MpegPsFile File, byte[] Original, ChunkBoundedFileSystem Fs) Open ()
	{
		var original = Build ();
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.MaxRead = MpegPsScanner.SmallWindow;
		fs.Add ("/in.mpg", original);
		var opened = MediaFile.Read ("/in.mpg", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		fs.MaxRead = ChunkBoundedFileSystem.MaxChunk;
		return ((MpegPsFile)opened.File!, original, fs);
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
		AssertSameBytes (expected, fs.Bytes ("/out.mpg"));
	}

	static byte[] Build ()
	{
		var stream = ProgramStream (new Options {
			VideoFrames = 3,
			AudioKind = Audio.None,
			AudioFrames = 0,
			PaddingBytes = 400 * 1024,
			PaddingPack = 1
		});
		var ape = TestBuilders.Ape.CreateTag ("Kept");
		return Concat (Id3v2 ("Front"), stream, ape, Id3v1 ("Back"));
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
