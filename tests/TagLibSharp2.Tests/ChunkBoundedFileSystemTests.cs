// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests;

/// <summary>
/// The chunk bound is what stops a path save from loading the file.
/// A read or write of 64 KB is allowed. One byte more is not.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class ChunkBoundedFileSystemTests
{
	[TestMethod]
	public async Task FullFileRead_Throws_AndAChunkSizedWriteIsKept ()
	{
		var fs = new ChunkBoundedFileSystem (ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.bin", new byte[] { 1, 2, 3, 4 });

		var read = Assert.ThrowsExactly<InvalidOperationException> (() => fs.ReadAllBytes ("/in.bin"));
		Assert.AreEqual ("ReadAllBytes of 4 bytes", read.Message);
		var readAsync = await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await fs.ReadAllBytesAsync ("/in.bin"));
		Assert.AreEqual ("ReadAllBytes of 4 bytes", readAsync.Message);
		Assert.ThrowsExactly<NotSupportedException> (() => fs.OpenReadWrite ("/in.bin"));

		var exact = new byte[ChunkBoundedFileSystem.MaxChunk];
		exact[0] = 9;
		fs.WriteAllBytes ("/exact.bin", exact);
		await fs.WriteAllBytesAsync ("/exact-async.bin", exact);
		Assert.AreEqual (9, fs.Bytes ("/exact.bin")[0]);
		Assert.AreEqual (ChunkBoundedFileSystem.MaxChunk, fs.Bytes ("/exact-async.bin").Length);

		var over = new byte[ChunkBoundedFileSystem.MaxChunk + 1];
		var write = Assert.ThrowsExactly<InvalidOperationException> (() => fs.WriteAllBytes ("/over.bin", over));
		Assert.AreEqual ($"WriteAllBytes of {over.Length} bytes", write.Message);
		var writeAsync = await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await fs.WriteAllBytesAsync ("/over.bin", over));
		Assert.AreEqual ($"WriteAllBytes of {over.Length} bytes", writeAsync.Message);
		Assert.IsFalse (fs.FileExists ("/over.bin"));

		fs.Move ("/exact.bin", "/moved.bin");
		Assert.IsFalse (fs.FileExists ("/exact.bin"));
		Assert.IsTrue (fs.FileExists ("/moved.bin"));
		fs.Delete ("/moved.bin");
		Assert.IsFalse (fs.FileExists ("/moved.bin"));
		Assert.AreEqual ("a.bin", fs.GetFileName ("/dir/a.bin"));
		Assert.AreEqual (Path.GetDirectoryName ("/dir/a.bin"), fs.GetDirectoryName ("/dir/a.bin"));

		using (var source = fs.OpenRead ("/in.bin")) {
			Assert.IsFalse (source.CanWrite);
			Assert.ThrowsExactly<NotSupportedException> (() => source.SetLength (1));
			Assert.ThrowsExactly<NotSupportedException> (() => source.Write (new byte[1], 0, 1));
			var tooBig = new byte[ChunkBoundedFileSystem.MaxChunk + 1];
			var tooBigRead = Assert.ThrowsExactly<InvalidOperationException> (() => source.Read (tooBig, 0, tooBig.Length));
			Assert.AreEqual ($"read of {tooBig.Length} bytes", tooBigRead.Message);
			var spanRead = Assert.ThrowsExactly<InvalidOperationException> (() => source.Read (tooBig.AsSpan ()));
			Assert.AreEqual ($"read of {tooBig.Length} bytes", spanRead.Message);
			Assert.AreEqual (4, source.Read (new byte[4], 0, 4));
		}

		using var created = fs.Create ("/out.bin");
		Assert.IsFalse (created.CanRead);
		Assert.IsTrue (created.CanSeek);
		Assert.IsTrue (created.CanWrite);
		created.Flush ();
		Assert.ThrowsExactly<NotSupportedException> (() => created.Read (new byte[1], 0, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => created.Seek (0, (SeekOrigin)99));
		Assert.ThrowsExactly<IOException> (() => created.Seek (-1, SeekOrigin.Begin));
		Assert.ThrowsExactly<IOException> (() => created.Seek (ChunkBoundedFileSystem.MaxChunk + 1, SeekOrigin.Begin));
		var oversized = Assert.ThrowsExactly<InvalidOperationException> (
			() => created.Write (over, 0, over.Length));
		Assert.AreEqual ($"write of {over.Length} bytes", oversized.Message);
		var spanWrite = Assert.ThrowsExactly<InvalidOperationException> (() => created.Write (over.AsSpan ()));
		Assert.AreEqual ($"write of {over.Length} bytes", spanWrite.Message);

		created.Write (exact, 0, exact.Length);
		created.Position = exact.Length;
		Assert.ThrowsExactly<IOException> (() => created.Write (new byte[] { 1 }, 0, 1));
		created.Seek (0, SeekOrigin.End);
		created.Seek (-1, SeekOrigin.Current);
		created.SetLength (2);
		Assert.AreEqual (2, created.Length);
		created.Position = 0;
		Assert.ThrowsExactly<OverflowException> (() => created.Position = long.MaxValue);
	}
}
