// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Dsf;

/// <summary>
/// A path save copies the DSF audio. It does not read or write the file as one buffer,
/// and the bytes match <see cref="DsfFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Dsf")]
public sealed class DsfSaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var original = Build ();
		var expected = Expected (original);
		var fs = new ChunkBoundedFileSystem (expected.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.dsf", original);

		var read = MediaFile.Read ("/in.dsf", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = (DsfFile)read.File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "Changed" };

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = file.SaveToFile ("/out.dsf", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.dsf"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var original = Build ();
		var expected = Expected (original);
		var fs = new ChunkBoundedFileSystem (expected.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.dsf", original);

		var read = MediaFile.Read ("/in.dsf", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = (DsfFile)read.File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "Changed" };

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await file.SaveToFileAsync ("/out.dsf", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.dsf"));
	}

	// A file that still holds its sample bytes writes those bytes. The copy on disk can differ,
	// and this save does not open it. Render and SaveToFile(path, span) stay one buffer.
	[TestMethod]
	public void SaveToFile_WritesHeldBytesWithoutRereadingTheSource ()
	{
		var held = HeldFile ();
		var expected = held.File.Render ().ToArray ();
		var fs = DiskThatDiffers (held.Original, expected.Length);

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = held.File.SaveToFile ("/out.dsf", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (0, fs.SourceLengthGets, "held bytes are not re-read from disk");
		Assert.IsTrue (
			allocated < held.Original.Length / 2,
			$"save allocated {allocated} bytes for a {held.Original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.dsf"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesHeldBytesWithoutRereadingTheSource ()
	{
		var held = HeldFile ();
		var expected = held.File.Render ().ToArray ();
		var fs = DiskThatDiffers (held.Original, expected.Length);

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await held.File.SaveToFileAsync ("/out.dsf", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (0, fs.SourceLengthGets, "held bytes are not re-read from disk");
		Assert.IsTrue (
			allocated < held.Original.Length / 2,
			$"save allocated {allocated} bytes for a {held.Original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.dsf"));
	}

	/// <summary>
	/// A windowed file rendered from an empty span fails the same way as the sync overload.
	/// An already cancelled token does not hide that failure, and the destination is not created.
	/// </summary>
	[TestMethod]
	public async Task SaveToFileAsync_EmptySpan_CancelledToken_MatchesTheSyncFailure ()
	{
		var original = TestBuilders.Dsf.CreateMinimal ();
		using var stream = new MemoryStream (original, writable: false);
		var read = DsfFile.Read (stream, "/in.dsf", null);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = read.File!;
		var fs = new MockFileSystem ();

		var sync = Assert.ThrowsExactly<ArgumentException> (
			() => file.SaveToFile ("/out.dsf", ReadOnlySpan<byte>.Empty, fs));
		Assert.IsFalse (fs.FileExists ("/out.dsf"));

		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		var asyncError = await Assert.ThrowsExactlyAsync<ArgumentException> (
			async () => await file.SaveToFileAsync ("/out.dsf", ReadOnlyMemory<byte>.Empty, fs, cancelled.Token));

		Assert.AreEqual (sync.Message, asyncError.Message);
		Assert.IsFalse (fs.FileExists ("/out.dsf"));
	}

	/// <summary>
	/// A file that still holds its bytes renders successfully. An already cancelled token
	/// cancels that save and does not create the destination.
	/// </summary>
	[TestMethod]
	public async Task SaveToFileAsync_HeldBytes_AlreadyCancelled_DoesNotWrite ()
	{
		var held = HeldFile ();
		var fs = new MockFileSystem ();
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();

		await Assert.ThrowsExactlyAsync<OperationCanceledException> (
			async () => await held.File.SaveToFileAsync ("/out.dsf", held.Original, fs, cancelled.Token));

		Assert.IsFalse (fs.FileExists ("/out.dsf"));
	}

	static (byte[] Original, DsfFile File) HeldFile ()
	{
		var original = Build ();
		var mock = new MockFileSystem ();
		mock.AddFile ("/in.dsf", original);
		var read = DsfFile.ReadFromFile ("/in.dsf", mock);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = read.File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "Changed" };
		return (original, file);
	}

	// The path names the same source, with one audio byte flipped. A save that copies the disk
	// file, or that calls ReadAllBytes, cannot match Render of the held bytes.
	static ChunkBoundedFileSystem DiskThatDiffers (byte[] original, int outputLength)
	{
		var onDisk = (byte[])original.Clone ();
		onDisk[^1] ^= 0xFF;
		var fs = new ChunkBoundedFileSystem (outputLength + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.dsf", onDisk);
		return fs;
	}

	static byte[] Build ()
	{
		var header = TestBuilders.Dsf.CreateMinimal ();
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);

		var file = new byte[header.Length + audio.Length];
		header.CopyTo (file, 0);
		audio.CopyTo (file, header.Length);
		return file;
	}

	static byte[] Expected (byte[] original)
	{
		var read = DsfFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.Id3v2Tag = new Id3v2Tag { Title = "Changed" };
		return read.File.Render ().ToArray ();
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
