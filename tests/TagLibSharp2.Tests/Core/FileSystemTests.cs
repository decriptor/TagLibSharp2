// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Mock file system for testing.
/// </summary>
public sealed class MockFileSystem : IFileSystem
{
	readonly Dictionary<string, byte[]> _files = new (StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _inaccessibleFiles = new (StringComparer.OrdinalIgnoreCase);

	public void AddFile (string path, byte[] data)
	{
		_files[path] = data;
	}

	public void MarkInaccessible (string path)
	{
		_inaccessibleFiles.Add (path);
	}

	public bool FileExists (string path) => _files.ContainsKey (path);

	public Stream OpenRead (string path)
	{
		if (_inaccessibleFiles.Contains (path))
			throw new UnauthorizedAccessException ("Access denied");
		if (!_files.TryGetValue (path, out var data))
			throw new FileNotFoundException ("File not found", path);
		return new MemoryStream (data, writable: false);
	}

	public Stream OpenReadWrite (string path)
	{
		if (_inaccessibleFiles.Contains (path))
			throw new UnauthorizedAccessException ("Access denied");
		if (!_files.TryGetValue (path, out var data))
			throw new FileNotFoundException ("File not found", path);
		return new MemoryStream (data);
	}

	public Stream Create (string path)
	{
		if (_inaccessibleFiles.Contains (path))
			throw new UnauthorizedAccessException ("Access denied");
		var stream = new MemoryStream ();
		_files[path] = [];
		return stream;
	}

	public byte[] ReadAllBytes (string path)
	{
		if (_inaccessibleFiles.Contains (path))
			throw new UnauthorizedAccessException ("Access denied");
		if (!_files.TryGetValue (path, out var data))
			throw new FileNotFoundException ("File not found", path);
		return data;
	}

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		return Task.FromResult (ReadAllBytes (path));
	}

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data)
	{
		if (_inaccessibleFiles.Contains (path))
			throw new UnauthorizedAccessException ("Access denied");
		_files[path] = data.ToArray ();
	}

	public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		WriteAllBytes (path, data.Span);
		return Task.CompletedTask;
	}

	public void Delete (string path)
	{
		if (_inaccessibleFiles.Contains (path))
			throw new UnauthorizedAccessException ("Access denied");
		_files.Remove (path);
	}

	public void Move (string sourcePath, string destinationPath)
	{
		if (_inaccessibleFiles.Contains (sourcePath) || _inaccessibleFiles.Contains (destinationPath))
			throw new UnauthorizedAccessException ("Access denied");
		if (!_files.TryGetValue (sourcePath, out var data))
			throw new FileNotFoundException ("Source file not found", sourcePath);
		_files[destinationPath] = data;
		_files.Remove (sourcePath);
	}

	public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

	public string GetFileName (string path) => Path.GetFileName (path);

	public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);

	/// <summary>
	/// Gets a copy of all files in the mock file system (for testing).
	/// </summary>
	public IReadOnlyDictionary<string, byte[]> GetAllFiles () => new Dictionary<string, byte[]> (_files);
}

/// <summary>
/// Records temp paths passed to <see cref="IFileSystem.Move"/> and performs the move on <see cref="DefaultFileSystem"/>.
/// </summary>
sealed class TempPathRecordingFileSystem (DefaultFileSystem inner, List<string> tempPaths) : IFileSystem
{
	public bool FileExists (string path) => inner.FileExists (path);

	public Stream OpenRead (string path) => inner.OpenRead (path);

	public Stream OpenReadWrite (string path) => inner.OpenReadWrite (path);

	public Stream Create (string path) => inner.Create (path);

	public byte[] ReadAllBytes (string path) => inner.ReadAllBytes (path);

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
		inner.ReadAllBytesAsync (path, cancellationToken);

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => inner.WriteAllBytes (path, data);

	public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
		inner.WriteAllBytesAsync (path, data, cancellationToken);

	public void Delete (string path) => inner.Delete (path);

	public void Move (string sourcePath, string destinationPath)
	{
		tempPaths.Add (sourcePath);
		inner.Move (sourcePath, destinationPath);
	}

	public string? GetDirectoryName (string path) => inner.GetDirectoryName (path);

	public string GetFileName (string path) => inner.GetFileName (path);

	public string CombinePath (string path1, string path2) => inner.CombinePath (path1, path2);
}

/// <summary>
/// Delegates to an inner file system while recording writes and optionally replacing moves.
/// </summary>
sealed class RecordingFileSystem (IFileSystem inner) : IFileSystem
{
	readonly Queue<Action<IFileSystem, string, string>> _moveScripts = new ();

	public List<string> Writes { get; } = [];

	public bool ThrowAfterWrite { get; set; }

	public void OnNextMove (Action<IFileSystem, string, string> action) =>
		_moveScripts.Enqueue (action);

	public bool FileExists (string path) => inner.FileExists (path);

	public Stream OpenRead (string path) => inner.OpenRead (path);

	public Stream OpenReadWrite (string path) => inner.OpenReadWrite (path);

	public Stream Create (string path) => inner.Create (path);

	public byte[] ReadAllBytes (string path) => inner.ReadAllBytes (path);

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
		inner.ReadAllBytesAsync (path, cancellationToken);

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data)
	{
		Writes.Add (path);
		inner.WriteAllBytes (path, data);
		if (ThrowAfterWrite)
			throw new IOException ("write failed");
	}

	public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		Writes.Add (path);
		if (ThrowAfterWrite) {
			inner.WriteAllBytes (path, data.Span);
			throw new IOException ("write failed");
		}

		return inner.WriteAllBytesAsync (path, data, cancellationToken);
	}

	public void Delete (string path) => inner.Delete (path);

	public void Move (string sourcePath, string destinationPath)
	{
		if (_moveScripts.Count > 0) {
			_moveScripts.Dequeue () (inner, sourcePath, destinationPath);
			return;
		}

		inner.Move (sourcePath, destinationPath);
	}

	public string? GetDirectoryName (string path) => inner.GetDirectoryName (path);

	public string GetFileName (string path) => inner.GetFileName (path);

	public string CombinePath (string path1, string path2) => inner.CombinePath (path1, path2);
}

/// <summary>
/// Records whether <see cref="FileStream.Flush(bool)"/> was asked to flush to disk.
/// </summary>
sealed class FlushRecordingFileStream : FileStream
{
	public FlushRecordingFileStream (string path)
		: base (path, FileMode.Create, FileAccess.ReadWrite, FileShare.None)
	{
	}

	public bool FlushedToDisk { get; private set; }

	public override void Flush (bool flushToDisk)
	{
		if (flushToDisk)
			FlushedToDisk = true;
		base.Flush (flushToDisk);
	}

	protected override void Dispose (bool disposing)
	{
		var flushed = FlushedToDisk;
		base.Dispose (disposing);
		if (disposing && !flushed)
			throw new InvalidOperationException ("Flush(true) was not called.");
	}
}

/// <summary>
/// Writes through <see cref="DefaultFileSystem.WriteAndFlush"/> and records that call before move.
/// </summary>
sealed class FlushOrderingFileSystem (List<string> events) : IFileSystem
{
	readonly DefaultFileSystem _inner = DefaultFileSystem.Instance;

	public bool FileExists (string path) => _inner.FileExists (path);

	public Stream OpenRead (string path) => _inner.OpenRead (path);

	public Stream OpenReadWrite (string path) => _inner.OpenReadWrite (path);

	public Stream Create (string path) => _inner.Create (path);

	public byte[] ReadAllBytes (string path) => _inner.ReadAllBytes (path);

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
		_inner.ReadAllBytesAsync (path, cancellationToken);

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data)
	{
		using var stream = new FlushRecordingFileStream (path);
		DefaultFileSystem.WriteAndFlush (stream, data);
		events.Add (stream.FlushedToDisk ? "flush-to-disk" : "not-flushed");
	}

	public async Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		using var stream = new FlushRecordingFileStream (path);
		await DefaultFileSystem.WriteAndFlushAsync (stream, data, cancellationToken).ConfigureAwait (false);
		events.Add (stream.FlushedToDisk ? "flush-to-disk" : "not-flushed");
	}

	public void Delete (string path) => _inner.Delete (path);

	public void Move (string sourcePath, string destinationPath)
	{
		events.Add ("move");
		_inner.Move (sourcePath, destinationPath);
	}

	public string? GetDirectoryName (string path) => _inner.GetDirectoryName (path);

	public string GetFileName (string path) => _inner.GetFileName (path);

	public string CombinePath (string path1, string path2) => _inner.CombinePath (path1, path2);
}

[TestClass]
public sealed class FileSystemTests
{
	[TestMethod]
	public void WriteAndFlush_WritesBytesAndFlushesToDisk ()
	{
		var path = Path.Combine (Path.GetTempPath (), "tls-flush-" + Guid.NewGuid ().ToString ("N"));
		try {
			using (var stream = new FlushRecordingFileStream (path)) {
				DefaultFileSystem.WriteAndFlush (stream, [1, 2, 3]);
				Assert.IsTrue (stream.FlushedToDisk);
			}

			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (path));
		} finally {
			if (File.Exists (path))
				File.Delete (path);
		}
	}

	[TestMethod]
	public async Task WriteAndFlushAsync_WritesBytesAndFlushesToDisk ()
	{
		var path = Path.Combine (Path.GetTempPath (), "tls-flush-" + Guid.NewGuid ().ToString ("N"));
		try {
			using (var stream = new FlushRecordingFileStream (path)) {
				await DefaultFileSystem.WriteAndFlushAsync (stream, new byte[] { 1, 2, 3 });
				Assert.IsTrue (stream.FlushedToDisk);
			}

			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (path));
		} finally {
			if (File.Exists (path))
				File.Delete (path);
		}
	}

	[TestMethod]
	public void DefaultFileSystem_Move_ReplacesExistingFile ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var source = Path.Combine (dir, "new.bin");
			File.WriteAllBytes (destination, [1, 2, 3]);
			File.WriteAllBytes (source, [4, 5]);
			using var reader = new FileStream (
				destination,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete);

			DefaultFileSystem.Instance.Move (source, destination);

			Assert.IsTrue (File.Exists (destination));
			Assert.IsFalse (File.Exists (source));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (destination));

			// The open handle still refers to the inode that was replaced, so it sees the old bytes.
			reader.Position = 0;
			var stillOpen = new byte[3];
			Assert.AreEqual (3, reader.Read (stillOpen, 0, stillOpen.Length));
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, stillOpen);
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void DefaultFileSystem_Move_MissingSource_LeavesDestinationUnchanged ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var source = Path.Combine (dir, "new.bin");
			byte[] original = [1, 2, 3];
			File.WriteAllBytes (destination, original);
			File.WriteAllBytes (source, [4, 5]);
			File.Delete (source);

			Assert.ThrowsExactly<FileNotFoundException> (() => DefaultFileSystem.Instance.Move (source, destination));

			Assert.IsTrue (File.Exists (destination));
			CollectionAssert.AreEqual (original, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void DefaultFileSystem_Move_MovesWhenDestinationDoesNotExist ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var source = Path.Combine (dir, "new.bin");
			File.WriteAllBytes (source, [4, 5]);

			DefaultFileSystem.Instance.Move (source, destination);

			Assert.IsTrue (File.Exists (destination));
			Assert.IsFalse (File.Exists (source));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void DefaultFileSystem_Instance_IsSingleton ()
	{
		var instance1 = DefaultFileSystem.Instance;
		var instance2 = DefaultFileSystem.Instance;
		Assert.AreSame (instance1, instance2);
	}

	[TestMethod]
	public void MockFileSystem_FileExists_ReturnsTrueForAddedFile ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/test.txt", [1, 2, 3]);

		Assert.IsTrue (fs.FileExists ("/test.txt"));
		Assert.IsFalse (fs.FileExists ("/other.txt"));
	}

	[TestMethod]
	public void MockFileSystem_ReadAllBytes_ReturnsData ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [1, 2, 3, 4, 5];
		fs.AddFile ("/test.bin", data);

		var result = fs.ReadAllBytes ("/test.bin");

		CollectionAssert.AreEqual (data, result);
	}

	[TestMethod]
	public void MockFileSystem_ReadAllBytes_ThrowsForInaccessible ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/test.bin", [1]);
		fs.MarkInaccessible ("/test.bin");

		Assert.ThrowsExactly<UnauthorizedAccessException> (() => fs.ReadAllBytes ("/test.bin"));
	}
}

[TestClass]
public sealed class FileHelperTests
{
	[TestMethod]
	public void SafeReadAllBytes_FileNotFound_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		var result = FileHelper.SafeReadAllBytes ("/nonexistent.txt", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "File not found");
	}

	[TestMethod]
	public void SafeReadAllBytes_Success_ReturnsData ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [0x66, 0x4C, 0x61, 0x43]; // fLaC
		fs.AddFile ("/test.flac", data);

		var result = FileHelper.SafeReadAllBytes ("/test.flac", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
		CollectionAssert.AreEqual (data, result.Data!.Value.ToArray ());
	}

	[TestMethod]
	public void SafeReadAllBytes_AccessDenied_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/protected.txt", [1, 2, 3]);
		fs.MarkInaccessible ("/protected.txt");

		var result = FileHelper.SafeReadAllBytes ("/protected.txt", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "Access denied");
	}

	[TestMethod]
	public void SafeReadAllBytes_NullPath_ThrowsArgumentNullException ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => FileHelper.SafeReadAllBytes (null!));
	}

	[TestMethod]
	public async Task SafeReadAllBytesAsync_FileNotFound_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		var result = await FileHelper.SafeReadAllBytesAsync ("/nonexistent.txt", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "File not found");
	}

	[TestMethod]
	public async Task SafeReadAllBytesAsync_Success_ReturnsData ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [0x66, 0x4C, 0x61, 0x43]; // fLaC
		fs.AddFile ("/test.flac", data);

		var result = await FileHelper.SafeReadAllBytesAsync ("/test.flac", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
		CollectionAssert.AreEqual (data, result.Data!.Value.ToArray ());
	}

	[TestMethod]
	public async Task SafeReadAllBytesAsync_AccessDenied_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/protected.txt", [1, 2, 3]);
		fs.MarkInaccessible ("/protected.txt");

		var result = await FileHelper.SafeReadAllBytesAsync ("/protected.txt", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "Access denied");
	}

	[TestMethod]
	public async Task SafeReadAllBytesAsync_Cancellation_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/test.txt", [1, 2, 3]);
		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await FileHelper.SafeReadAllBytesAsync ("/test.txt", fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}
}

[TestClass]
public sealed class FileReadResultTests
{
	[TestMethod]
	public void Success_HasCorrectProperties ()
	{
		byte[] data = [1, 2, 3];
		var result = FileReadResult.Success (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
		Assert.IsTrue (result.Data.HasValue);
		CollectionAssert.AreEqual (data, result.Data.Value.ToArray ());
	}

	[TestMethod]
	public void Success_DataIsImmutable ()
	{
		byte[] data = [1, 2, 3];
		var result = FileReadResult.Success (data);

		// Verify that Data is ReadOnlyMemory<byte>, not byte[]
		// This compile-time check ensures immutability
		ReadOnlyMemory<byte> immutableData = result.Data!.Value;
		Assert.AreEqual (3, immutableData.Length);
	}

	[TestMethod]
	public void Failure_HasCorrectProperties ()
	{
		var result = FileReadResult.Failure ("Something went wrong");

		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Something went wrong", result.Error);
		Assert.IsFalse (result.Data.HasValue);
	}

	[TestMethod]
	public void Equals_SameData_ReturnsTrue ()
	{
		byte[] data = [1, 2, 3];
		var result1 = FileReadResult.Success (data);
		var result2 = FileReadResult.Success (data);

		Assert.AreEqual (result1, result2);
		Assert.IsTrue (result1 == result2);
		Assert.IsFalse (result1 != result2);
	}
}

[TestClass]
public sealed class AtomicFileWriterTests
{
	[TestMethod]
	public void Write_FlushesTempToDiskBeforeMove ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var events = new List<string> ();
			var fs = new FlushOrderingFileSystem (events);

			var result = AtomicFileWriter.Write (destination, [1, 2, 3], fs);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.HasCount (2, events);
			Assert.AreEqual ("flush-to-disk", events[0]);
			Assert.AreEqual ("move", events[1]);
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_TwoSaves_UseDistinctTempPaths ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var tempPaths = new List<string> ();
			var fs = new TempPathRecordingFileSystem (DefaultFileSystem.Instance, tempPaths);

			var first = AtomicFileWriter.Write (destination, [1, 2, 3], fs);
			var second = AtomicFileWriter.Write (destination, [4, 5], fs);

			Assert.IsTrue (first.IsSuccess);
			Assert.IsTrue (second.IsSuccess);
			Assert.HasCount (2, tempPaths);
			Assert.AreNotEqual (tempPaths[0], tempPaths[1]);
			Assert.AreEqual (dir, Path.GetDirectoryName (tempPaths[0]));
			Assert.AreEqual (dir, Path.GetDirectoryName (tempPaths[1]));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (destination));
			Assert.IsFalse (File.Exists (tempPaths[0]));
			Assert.IsFalse (File.Exists (tempPaths[1]));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_NewFile_CreatesFile ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [1, 2, 3, 4, 5];

		var result = AtomicFileWriter.Write ("/dir/test.bin", data, fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5, result.BytesWritten);
		Assert.IsTrue (fs.FileExists ("/dir/test.bin"));
		CollectionAssert.AreEqual (data, fs.ReadAllBytes ("/dir/test.bin"));
	}

	[TestMethod]
	public void Write_ExistingFile_OverwritesFile ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/test.bin", [9, 9, 9]);
		byte[] newData = [1, 2, 3];

		var result = AtomicFileWriter.Write ("/test.bin", newData, fs);

		Assert.IsTrue (result.IsSuccess);
		CollectionAssert.AreEqual (newData, fs.ReadAllBytes ("/test.bin"));
	}

	[TestMethod]
	public void Write_TempFileCleanedUp_OnSuccess ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [1, 2, 3];

		AtomicFileWriter.Write ("/test.bin", data, fs);

		// Verify no temp files remain
		var files = fs.GetAllFiles ();
		Assert.HasCount (1, files);
		Assert.IsTrue (files.ContainsKey ("/test.bin"));
	}

	[TestMethod]
	public void Write_AccessDenied_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		fs.MarkInaccessible ("/test.bin");
		byte[] data = [1, 2, 3];

		var result = AtomicFileWriter.Write ("/test.bin", data, fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "Access denied");
	}

	[TestMethod]
	public async Task WriteAsync_NewFile_CreatesFile ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [1, 2, 3, 4, 5];

		var result = await AtomicFileWriter.WriteAsync ("/dir/test.bin", data, fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5, result.BytesWritten);
		Assert.IsTrue (fs.FileExists ("/dir/test.bin"));
		CollectionAssert.AreEqual (data, fs.ReadAllBytes ("/dir/test.bin"));
	}

	[TestMethod]
	public async Task WriteAsync_Cancellation_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		byte[] data = [1, 2, 3];
		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await AtomicFileWriter.WriteAsync ("/test.bin", data, fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public void Write_DestinationName230Characters_SucceedsWithShortTempName ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var fileName = new string ('a', 230);
			var destination = Path.Combine (dir, fileName);
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);

			var result = AtomicFileWriter.Write (destination, [1, 2, 3], fs);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.HasCount (1, fs.Writes);
			var tempName = Path.GetFileName (fs.Writes[0]);
			Assert.IsTrue (tempName.Length < 80, tempName);
			Assert.IsFalse (tempName.Contains (fileName, StringComparison.Ordinal), tempName);
			StringAssert.StartsWith (tempName, ".taglib_");
			StringAssert.EndsWith (tempName, ".tmp");
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_WhenReplaceRemovesDestination_MovesTempBack ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/song.bin", [1, 2, 3]);
		var fs = new RecordingFileSystem (mock);
		fs.OnNextMove ((inner, _, dest) => {
			inner.Delete (dest);
			throw new IOException ("Unable to move replacement");
		});

		var result = AtomicFileWriter.Write ("/song.bin", [4, 5], fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes ("/song.bin"));
		Assert.HasCount (1, mock.GetAllFiles ());
	}

	[TestMethod]
	public async Task WriteAsync_WhenReplaceRemovesDestination_MovesTempBack ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/song.bin", [1, 2, 3]);
		var fs = new RecordingFileSystem (mock);
		fs.OnNextMove ((inner, _, dest) => {
			inner.Delete (dest);
			throw new IOException ("Unable to move replacement");
		});

		var result = await AtomicFileWriter.WriteAsync ("/song.bin", new byte[] { 4, 5 }, fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes ("/song.bin"));
		Assert.HasCount (1, mock.GetAllFiles ());
	}

	[TestMethod]
	public void Write_WhenReplaceRemovesDestinationAndRetryFails_KeepsTempAndReportsPath ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/song.bin", [1, 2, 3]);
		var fs = new RecordingFileSystem (mock);
		fs.OnNextMove ((inner, _, dest) => {
			inner.Delete (dest);
			throw new IOException ("Unable to move replacement");
		});
		fs.OnNextMove ((_, _, _) => throw new IOException ("retry failed"));

		var result = AtomicFileWriter.Write ("/song.bin", [4, 5], fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		var files = mock.GetAllFiles ();
		Assert.HasCount (1, files);
		var tempPath = files.Keys.Single ();
		StringAssert.Contains (result.Error, tempPath);
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, files[tempPath]);
		Assert.IsFalse (mock.FileExists ("/song.bin"));
	}

	[TestMethod]
	public void Write_WhenMoveThrowsAndDestinationRemains_DeletesTempOnly ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/song.bin", [1, 2, 3]);
		var fs = new RecordingFileSystem (mock);
		fs.OnNextMove ((_, _, _) => throw new IOException ("disk full"));

		var result = AtomicFileWriter.Write ("/song.bin", [4, 5], fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		Assert.HasCount (1, fs.Writes);
		Assert.IsFalse (result.Error.Contains (fs.Writes[0], StringComparison.Ordinal));
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, mock.ReadAllBytes ("/song.bin"));
		Assert.HasCount (1, mock.GetAllFiles ());
	}

	[TestMethod]
	public void Write_AbsentDestination_WhenMoveThrowsAfterTempExists_DeletesTemp ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			// The second failure is the recovery move. It must not leave the temp behind.
			fs.OnNextMove ((_, _, _) => throw new IOException ("cannot replace"));
			fs.OnNextMove ((_, _, _) => throw new IOException ("cannot replace"));

			var result = AtomicFileWriter.Write (destination, [4, 5], fs);

			Assert.IsFalse (result.IsSuccess);
			Assert.IsFalse (File.Exists (destination));
			var leftovers = Directory.GetFiles (dir);
			Assert.AreEqual (0, leftovers.Length, string.Join (", ", leftovers));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public async Task WriteAsync_AbsentDestination_WhenMoveThrowsAfterTempExists_DeletesTemp ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			// The second failure is the recovery move. It must not leave the temp behind.
			fs.OnNextMove ((_, _, _) => throw new IOException ("cannot replace"));
			fs.OnNextMove ((_, _, _) => throw new IOException ("cannot replace"));

			var result = await AtomicFileWriter.WriteAsync (destination, new byte[] { 4, 5 }, fs);

			Assert.IsFalse (result.IsSuccess);
			Assert.IsFalse (File.Exists (destination));
			var leftovers = Directory.GetFiles (dir);
			Assert.AreEqual (0, leftovers.Length, string.Join (", ", leftovers));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_WhenWriteThrowsBeforeReplace_DeletesTemp ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance) { ThrowAfterWrite = true };

			var result = AtomicFileWriter.Write (destination, [4, 5], fs);

			Assert.IsFalse (result.IsSuccess);
			Assert.IsFalse (File.Exists (destination));
			var leftovers = Directory.GetFiles (dir);
			Assert.AreEqual (0, leftovers.Length, string.Join (", ", leftovers));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_WhenDefaultFileSystemMoveThrows_LeavesDestinationAndDeletesTemp ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.WriteAllBytes (destination, [1, 2, 3]);
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			fs.OnNextMove ((inner, _, dest) => {
				var missing = Path.Combine (dir, "missing-" + Guid.NewGuid ().ToString ("N"));
				inner.Move (missing, dest);
			});

			var result = AtomicFileWriter.Write (destination, [4, 5], fs);

			Assert.IsFalse (result.IsSuccess);
			Assert.IsNotNull (result.Error);
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (destination));
			Assert.IsFalse (Directory.EnumerateFiles (dir).Any (path => Path.GetFileName (path).StartsWith (".taglib_", StringComparison.Ordinal)));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void TryRecoverTempFile_DestinationExists_DeletesTemp ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/song.bin", [1, 2, 3]);
		mock.AddFile ("/.taglib_temp.tmp", [4, 5]);

		var restored = AtomicFileWriter.TryRecoverTempFile ("/.taglib_temp.tmp", "/song.bin", mock, out var keptTempError);

		Assert.IsFalse (restored);
		Assert.IsNull (keptTempError);
		Assert.IsFalse (mock.FileExists ("/.taglib_temp.tmp"));
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, mock.ReadAllBytes ("/song.bin"));
	}

	[TestMethod]
	public void TryRecoverTempFile_DestinationMissing_MovesTemp ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/.taglib_temp.tmp", [4, 5]);

		var restored = AtomicFileWriter.TryRecoverTempFile ("/.taglib_temp.tmp", "/song.bin", mock, out var keptTempError);

		Assert.IsTrue (restored);
		Assert.IsNull (keptTempError);
		Assert.IsFalse (mock.FileExists ("/.taglib_temp.tmp"));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes ("/song.bin"));
	}

	[TestMethod]
	public void TryRecoverTempFile_DestinationMissingAndMoveFails_KeepsTemp ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/.taglib_temp.tmp", [4, 5]);
		var fs = new RecordingFileSystem (mock);
		fs.OnNextMove ((_, _, _) => throw new IOException ("retry failed"));

		var restored = AtomicFileWriter.TryRecoverTempFile ("/.taglib_temp.tmp", "/song.bin", fs, out var keptTempError);

		Assert.IsFalse (restored);
		Assert.IsNotNull (keptTempError);
		StringAssert.Contains (keptTempError, "/.taglib_temp.tmp");
		Assert.IsTrue (mock.FileExists ("/.taglib_temp.tmp"));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes ("/.taglib_temp.tmp"));
	}
}

[TestClass]
public sealed class FileWriteResultTests
{
	[TestMethod]
	public void Success_HasCorrectProperties ()
	{
		var result = FileWriteResult.Success (100);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
		Assert.AreEqual (100, result.BytesWritten);
	}

	[TestMethod]
	public void Failure_HasCorrectProperties ()
	{
		var result = FileWriteResult.Failure ("Write failed");

		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Write failed", result.Error);
		Assert.AreEqual (0, result.BytesWritten);
	}

	[TestMethod]
	public void Equals_SameValues_ReturnsTrue ()
	{
		var result1 = FileWriteResult.Success (50);
		var result2 = FileWriteResult.Success (50);

		Assert.AreEqual (result1, result2);
		Assert.IsTrue (result1 == result2);
		Assert.IsFalse (result1 != result2);
	}

	[TestMethod]
	public void Equals_DifferentValues_ReturnsFalse ()
	{
		var result1 = FileWriteResult.Success (50);
		var result2 = FileWriteResult.Success (100);

		Assert.AreNotEqual (result1, result2);
		Assert.IsFalse (result1 == result2);
		Assert.IsTrue (result1 != result2);
	}

	[TestMethod]
	public void GetHashCode_SameValues_ReturnsSameHash ()
	{
		var result1 = FileWriteResult.Success (50);
		var result2 = FileWriteResult.Success (50);

		Assert.AreEqual (result1.GetHashCode (), result2.GetHashCode ());
	}
}
