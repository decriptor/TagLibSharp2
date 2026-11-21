// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Audible;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Audible;

/// <summary>
/// Each bound in the reader and the save planner, with an input that only that bound stops.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Audible")]
public class AudibleGuardTests
{
	const int TocAt = AudibleTestBuilder.HeaderSize;

	static int TocField (int index, int field) => TocAt + (index * 12) + (field * 4);

	// A file cut right after the dictionary, so TOC entries past it may point anywhere.
	static byte[] Truncated ()
	{
		var full = AudibleTestBuilder.Create ();
		var (_, offset, size) = AudibleTestBuilder.Toc (full, 2);
		return full.AsSpan (0, (int)(offset + size)).ToArray ();
	}

	static AudibleFile Grown (byte[] data)
	{
		var file = AudibleFile.Read (data).File!;
		file.Tag.Title = new string ('x', 200);
		return file;
	}

	[TestMethod]
	[DataRow (0xFFFFFFF0u, 4u, DisplayName = "offset past 32 bits after the shift")]
	[DataRow (0xFFFFFF00u, 0xF0u, DisplayName = "offset + size past 32 bits after the shift")]
	public void Render_ChunkMovedPast32Bits_ReturnsEmpty (uint offset, uint size)
	{
		var data = Truncated ();
		AudibleTestBuilder.SetU32 (data, TocField (7, 1), offset);
		AudibleTestBuilder.SetU32 (data, TocField (7, 2), size);
		var file = Grown (data);

		Assert.IsTrue (file.Render (data).IsEmpty);
		Assert.IsFalse (file.SaveToFile ("/out.aa", data, new MockFileSystem ()).IsSuccess);
	}

	[TestMethod]
	public void Render_WholeFileEntryGrownPast32Bits_ReturnsEmpty ()
	{
		var data = Truncated ();
		AudibleTestBuilder.SetU32 (data, TocField (0, 2), 0xFFFFFFF8u);

		Assert.IsTrue (Grown (data).Render (data).IsEmpty);
	}

	[TestMethod]
	[DataRow (0xFFFFFFF8u, true)]
	[DataRow (5u, false)]
	public void Render_DeclaredSizeOutOf32Bits_ReturnsEmpty (uint declared, bool grow)
	{
		var data = Truncated ();
		AudibleTestBuilder.SetU32 (data, 0, declared);
		var file = AudibleFile.Read (data).File!;
		if (grow)
			file.Tag.Title = new string ('x', 200);
		else
			file.Tag.Clear ();

		Assert.IsTrue (file.Render (data).IsEmpty);
	}

	[TestMethod]
	public void Read_DictionaryOverTheCap_Fails ()
	{
		var value = new string ('v', AudibleFile.MaxDictionarySize);
		var data = AudibleTestBuilder.Create ([("title", value), ("codec", "acelp85")]);

		var result = AudibleFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "claims");
	}

	[TestMethod]
	public void Read_CoverChunkInsideTheToc_HasNoPicture ()
	{
		// Four TOC entries end at 64. The cover chunk (type 11) starts at 52, inside entry 3, whose
		// type, offset, and size fields read as a valid cover header: length 4, position 60, image = entry 3's size.
		var dictionary = AudibleTestBuilder.Dictionary ([("title", "T"), ("codec", "acelp85")]);
		var data = new byte[64 + dictionary.Length];
		AudibleTestBuilder.SetU32 (data, 0, (uint)data.Length);
		AudibleTestBuilder.SetU32 (data, 4, AudibleTestBuilder.Magic);
		AudibleTestBuilder.SetU32 (data, 8, 4);
		SetEntry (data, 0, 0, 0, (uint)data.Length);
		SetEntry (data, 1, 2, 64, (uint)dictionary.Length);
		SetEntry (data, 2, 11, 52, 20);
		SetEntry (data, 3, 4, 60, 0xFFD8FFE0);
		dictionary.CopyTo (data, 64);

		var result = AudibleFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsEmpty (result.File!.Tag.Pictures);
	}

	[TestMethod]
	public void Read_CoverPositionInsideItsOwnHeader_HasNoPicture ()
	{
		// Position = chunk + 4 points at the position field itself: inside the chunk but before the image.
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var (_, offset, _) = AudibleTestBuilder.Toc (data, 11);
		AudibleTestBuilder.SetU32 (data, (int)offset, 4);
		AudibleTestBuilder.SetU32 (data, (int)offset + 4, offset + 4);

		var result = AudibleFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsEmpty (result.File!.Tag.Pictures);
	}

	[TestMethod]
	[DataRow (true, DisplayName = "source denied")]
	[DataRow (false, DisplayName = "source deleted")]
	public async Task SaveToAnotherPath_SourceUnreadable_StillSucceeds (bool denied)
	{
		// After a save the source's header is re-read to see whether the save rewrote it. A source that
		// cannot be read then (denied or deleted) leaves the layout alone and does not fail the save.
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create ());
		var file = AudibleFile.ReadFromFile ("/book.aa", fs).File!;
		file.Tag.Title = "Copy with a longer title";
		var original = fs.ReadAllBytes ("/book.aa");
		if (denied)
			fs.MarkInaccessible ("/book.aa");
		else
			fs.Delete ("/book.aa");

		Assert.IsTrue (file.SaveToFile ("/a.aa", original, fs).IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/b.aa", original, fs)).IsSuccess);

		Assert.AreEqual ("Copy with a longer title", AudibleFile.Read (fs.ReadAllBytes ("/a.aa")).File!.Tag.Title);
		CollectionAssert.AreEqual (fs.ReadAllBytes ("/a.aa"), fs.ReadAllBytes ("/b.aa"));
	}

	[TestMethod]
	public async Task SaveToAnotherPath_SourceReplacedMeanwhile_NextSaveStillRefuses ()
	{
		// Only a source that now holds the layout this object wrote is adopted. A source another writer
		// replaced is not, so the next save does not put this dictionary into someone else's file.
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create ());
		var file = AudibleFile.ReadFromFile ("/book.aa", fs).File!;
		var original = fs.ReadAllBytes ("/book.aa");
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create (audio: AudibleTestBuilder.Audio (777)));
		file.Tag.Title = "Mine";

		var copy = file.SaveToFile ("/copy.aa", original, fs);
		Assert.IsTrue (copy.IsSuccess, copy.Error);
		var copy2 = await file.SaveToFileAsync ("/copy2.aa", original, fs);
		Assert.IsTrue (copy2.IsSuccess, copy2.Error);
		Assert.IsFalse (file.SaveToFile (fs).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync (fs)).IsSuccess);
	}

	[TestMethod]
	public async Task SaveToFileAsync_CancelledAfterTheCommit_ReturnsTheWriteAndCanSaveAgain ()
	{
		// The token is cancelled inside the atomic rename, after the new file is in place. The save has
		// happened, so it must report success, and the layout must follow it so the next save works.
		var inner = new MockFileSystem ();
		inner.AddFile ("/book.aa", AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ()));
		using var cts = new CancellationTokenSource ();
		var fs = new CancelOnMoveFileSystem (inner, cts);
		var file = (await AudibleFile.ReadFromFileAsync ("/book.aa", fs)).File!;

		file.Tag.Title = "Committed with a longer title than before";
		var saved = await file.SaveToFileAsync (fs, cts.Token);

		Assert.IsTrue (cts.IsCancellationRequested, "the rename did not run");
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Committed with a longer title than before", AudibleFile.Read (inner.ReadAllBytes ("/book.aa")).File!.Tag.Title);

		file.Tag.Title = "Next";
		var next = await file.SaveToFileAsync (inner);
		Assert.IsTrue (next.IsSuccess, next.Error);
		Assert.AreEqual ("Next", AudibleFile.Read (inner.ReadAllBytes ("/book.aa")).File!.Tag.Title);
	}

	[TestMethod]
	public async Task SaveToFile_SourceHeaderReadFailsUnexpectedly_StillSucceeds ()
	{
		// Any failure while re-reading the source after the commit leaves the layout alone; the save stands.
		var inner = new MockFileSystem ();
		inner.AddFile ("/book.aa", AudibleTestBuilder.Create ());
		var file = AudibleFile.ReadFromFile ("/book.aa", inner).File!;
		var original = inner.ReadAllBytes ("/book.aa");
		var fs = new ThrowOnReadFileSystem (inner);

		foreach (var failure in new Exception[] { new NotSupportedException (), new ArgumentException ("bad path"), new ObjectDisposedException ("stream"), new InvalidOperationException (), new OperationCanceledException () }) {
			fs.Failure = failure;
			file.Tag.Title = "Saved despite " + failure.GetType ().Name;
			var sync = file.SaveToFile ("/a.aa", original, fs);
			var async = await file.SaveToFileAsync ("/b.aa", original, fs);
			Assert.IsTrue (sync.IsSuccess, sync.Error);
			Assert.IsTrue (async.IsSuccess, async.Error);
		}
	}

	sealed class CancelOnMoveFileSystem (MockFileSystem inner, CancellationTokenSource cts) : ForwardingFileSystem (inner)
	{
		public override void Move (string sourcePath, string destinationPath)
		{
			base.Move (sourcePath, destinationPath);
			cts.Cancel ();
		}
	}

	sealed class ThrowOnReadFileSystem (MockFileSystem inner) : ForwardingFileSystem (inner)
	{
		public Exception? Failure { get; set; }

		public override Stream OpenRead (string path) => Failure is not null && path == "/book.aa" ? throw Failure : base.OpenRead (path);
	}

	class ForwardingFileSystem (MockFileSystem inner) : IFileSystem
	{
		public bool FileExists (string path) => inner.FileExists (path);
		public virtual Stream OpenRead (string path) => inner.OpenRead (path);
		public Stream OpenReadWrite (string path) => inner.OpenReadWrite (path);
		public Stream Create (string path) => inner.Create (path);
		public byte[] ReadAllBytes (string path) => inner.ReadAllBytes (path);
		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) => inner.ReadAllBytesAsync (path, cancellationToken);
		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => inner.WriteAllBytes (path, data);
		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => inner.WriteAllBytesAsync (path, data, cancellationToken);
		public void Delete (string path) => inner.Delete (path);
		public virtual void Move (string sourcePath, string destinationPath) => inner.Move (sourcePath, destinationPath);
		public string? GetDirectoryName (string path) => inner.GetDirectoryName (path);
		public string GetFileName (string path) => inner.GetFileName (path);
		public string CombinePath (string path1, string path2) => inner.CombinePath (path1, path2);
	}

	static void SetEntry (byte[] data, int index, uint type, uint offset, uint size)
	{
		AudibleTestBuilder.SetU32 (data, TocField (index, 0), type);
		AudibleTestBuilder.SetU32 (data, TocField (index, 1), offset);
		AudibleTestBuilder.SetU32 (data, TocField (index, 2), size);
	}
}
