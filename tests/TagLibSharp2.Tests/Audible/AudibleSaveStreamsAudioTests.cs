// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Audible;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Audible;

/// <summary>
/// A path save copies Audible audio. It does not read or write the file as one buffer,
/// and the bytes match <see cref="AudibleFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Audible")]
public sealed class AudibleSaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Tag.Title = "Changed";
		Check (original, file.Render (original).ToArray (), fs, () => file.SaveToFile ("/out.aa", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Tag.Title = "Changed";
		var expected = file.Render (original).ToArray ();
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.aa", fs));
	}

	static (AudibleFile File, byte[] Original, ChunkBoundedFileSystem Fs) Open ()
	{
		var original = AudibleTestBuilder.Create (audio: AudibleTestBuilder.Audio (AudioBytes), cover: AudibleTestBuilder.Jpeg ());
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.aa", original);
		var opened = MediaFile.Read ("/in.aa", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		return ((AudibleFile)opened.File!, original, fs);
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
		// Open records the length once. The save then re-reads the source header to refresh the layout.
		Assert.AreEqual (2, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.aa"));
	}

	/// <summary>
	/// A buffer that is not this file fails the render. An already cancelled token does not replace that failure,
	/// and the destination is not created.
	/// </summary>
	[TestMethod]
	public async Task SaveToFileAsync_BufferIsNotThisFile_BeatsAnAlreadyCancelledToken ()
	{
		var original = AudibleTestBuilder.Create (audio: AudibleTestBuilder.Audio (32));
		var read = AudibleFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = read.File!;
		var fs = new MockFileSystem ();
		var wrong = new byte[] { 1, 2, 3 };

		var sync = file.SaveToFile ("/out.aa", wrong, fs);
		Assert.IsFalse (sync.IsSuccess);
		Assert.IsFalse (fs.FileExists ("/out.aa"));

		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		var save = await file.SaveToFileAsync ("/out.aa", wrong, fs, cancelled.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (sync.Error, save.Error);
		Assert.IsFalse (fs.FileExists ("/out.aa"));
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
