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
		var stream = new Stored (this, path);
		_files[path] = [];
		return stream;
	}

	sealed class Stored : MemoryStream
	{
		readonly MockFileSystem _owner;
		readonly string _path;
		bool _committed;

		public Stored (MockFileSystem owner, string path)
		{
			_owner = owner;
			_path = path;
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing && !_committed) {
				_committed = true;
				_owner._files[_path] = ToArray ();
			}

			base.Dispose (disposing);
		}
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
sealed class TempPathRecordingFileSystem (DefaultFileSystem inner, List<string> tempPaths)
	: IFileSystem, IFileSystemTimestamps
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

	public IEnumerable<string> EnumerateFiles (string directory, string searchPattern) =>
		((IFileSystemTimestamps)inner).EnumerateFiles (directory, searchPattern);

	public DateTime? GetLastWriteTimeUtc (string path) =>
		((IFileSystemTimestamps)inner).GetLastWriteTimeUtc (path);
}

/// <summary>
/// Delegates to an inner file system while recording writes and optionally replacing moves.
/// </summary>
sealed class RecordingFileSystem (IFileSystem inner) : IFileSystem, IFileSystemTimestamps
{
	readonly Queue<Action<IFileSystem, string, string>> _moveScripts = new ();

	public List<string> Writes { get; } = [];

	public bool ThrowAfterWrite { get; set; }

	public bool ThrowOnEnumerate { get; set; }

	public int EnumerateCount { get; private set; }

	public string? EnumeratePattern { get; private set; }

	public Action<string>? DuringWrite { get; set; }

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
		DuringWrite?.Invoke (path);
		if (ThrowAfterWrite)
			throw new IOException ("write failed");
	}

	public async Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		Writes.Add (path);
		if (ThrowAfterWrite) {
			inner.WriteAllBytes (path, data.Span);
			DuringWrite?.Invoke (path);
			throw new IOException ("write failed");
		}

		await inner.WriteAllBytesAsync (path, data, cancellationToken).ConfigureAwait (false);
		DuringWrite?.Invoke (path);
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

	public IEnumerable<string> EnumerateFiles (string directory, string searchPattern)
	{
		EnumerateCount++;
		EnumeratePattern = searchPattern;
		if (ThrowOnEnumerate)
			throw new IOException ("sweep failed");

		return inner is IFileSystemTimestamps timestamps
			? timestamps.EnumerateFiles (directory, searchPattern)
			: [];
	}

	public DateTime? GetLastWriteTimeUtc (string path) =>
		inner is IFileSystemTimestamps timestamps ? timestamps.GetLastWriteTimeUtc (path) : null;
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
sealed class FlushOrderingFileSystem (List<string> events) : IFileSystem, IFileSystemTimestamps
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

	public IEnumerable<string> EnumerateFiles (string directory, string searchPattern) =>
		((IFileSystemTimestamps)_inner).EnumerateFiles (directory, searchPattern);

	public DateTime? GetLastWriteTimeUtc (string path) =>
		((IFileSystemTimestamps)_inner).GetLastWriteTimeUtc (path);
}

/// <summary>
/// Lists files but never reports a last-write time, so a sweep must not delete by name.
/// </summary>
sealed class NullTimestampFileSystem (MockFileSystem inner) : IFileSystem, IFileSystemTimestamps
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

	public void Move (string sourcePath, string destinationPath) => inner.Move (sourcePath, destinationPath);

	public string? GetDirectoryName (string path) => inner.GetDirectoryName (path);

	public string GetFileName (string path) => inner.GetFileName (path);

	public string CombinePath (string path1, string path2) => inner.CombinePath (path1, path2);

	public IEnumerable<string> EnumerateFiles (string directory, string searchPattern) =>
		inner.GetAllFiles ().Keys
			.Where (path => string.Equals (
				inner.GetDirectoryName (path),
				directory,
				StringComparison.OrdinalIgnoreCase))
			.Where (path => NameMatchesSearch (inner.GetFileName (path), searchPattern))
			.ToArray ();

	static bool NameMatchesSearch (string name, string searchPattern)
	{
		var star = searchPattern.IndexOf ('*', StringComparison.Ordinal);
		if (star < 0)
			return string.Equals (name, searchPattern, StringComparison.OrdinalIgnoreCase);

		var prefix = searchPattern[..star];
		var suffix = searchPattern[(star + 1)..];
		return name.StartsWith (prefix, StringComparison.OrdinalIgnoreCase)
			&& name.EndsWith (suffix, StringComparison.OrdinalIgnoreCase)
			&& name.Length >= prefix.Length + suffix.Length;
	}

	public DateTime? GetLastWriteTimeUtc (string path) => null;
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
	public void WriteAllBytes_FlushesTheStreamItIsGiven ()
	{
		var path = Path.Combine (Path.GetTempPath (), "tls-flush-" + Guid.NewGuid ().ToString ("N"));
		try {
			using (var stream = new FlushRecordingFileStream (path)) {
				DefaultFileSystem.WriteAllBytes (stream, [1, 2, 3]);
				Assert.IsTrue (stream.FlushedToDisk);
			}

			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (path));
		} finally {
			if (File.Exists (path))
				File.Delete (path);
		}
	}

	[TestMethod]
	public async Task WriteAllBytesAsync_FlushesTheStreamItIsGiven ()
	{
		var path = Path.Combine (Path.GetTempPath (), "tls-flush-" + Guid.NewGuid ().ToString ("N"));
		try {
			using (var stream = new FlushRecordingFileStream (path)) {
				await DefaultFileSystem.WriteAllBytesAsync (stream, new byte[] { 1, 2, 3 });
				Assert.IsTrue (stream.FlushedToDisk);
			}

			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (path));
		} finally {
			if (File.Exists (path))
				File.Delete (path);
		}
	}

	[TestMethod]
	public void WriteAllBytes_PublicMethod_FlushesTheStreamItOpens ()
	{
		var path = Path.Combine (Path.GetTempPath (), "tls-flush-" + Guid.NewGuid ().ToString ("N"));
		FlushRecordingFileStream? recorded = null;
		try {
			using (DefaultFileSystem.OverrideWriteStreamForTests (candidate => recorded = new FlushRecordingFileStream (candidate)))
				DefaultFileSystem.Instance.WriteAllBytes (path, [1, 2, 3]);

			Assert.IsNotNull (recorded);
			Assert.IsTrue (recorded.FlushedToDisk);
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (path));
		} finally {
			if (File.Exists (path))
				File.Delete (path);
		}
	}

	[TestMethod]
	public async Task WriteAllBytesAsync_PublicMethod_FlushesTheStreamItOpens ()
	{
		var path = Path.Combine (Path.GetTempPath (), "tls-flush-" + Guid.NewGuid ().ToString ("N"));
		FlushRecordingFileStream? recorded = null;
		try {
			using (DefaultFileSystem.OverrideWriteStreamForTests (candidate => recorded = new FlushRecordingFileStream (candidate)))
				await DefaultFileSystem.Instance.WriteAllBytesAsync (path, new byte[] { 1, 2, 3 });

			Assert.IsNotNull (recorded);
			Assert.IsTrue (recorded.FlushedToDisk);
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
	public void Move_FailedReplaceAfterTempMovedBesideTarget_RestoresTempAndLeavesDestination ()
	{
		var root = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		var sourceDir = Path.Combine (root, "source");
		var destinationDir = Path.Combine (root, "dest");
		Directory.CreateDirectory (sourceDir);
		Directory.CreateDirectory (destinationDir);
		try {
			var source = Path.Combine (sourceDir, ".taglib_" + new string ('a', 32) + ".tmp");
			var destination = Path.Combine (destinationDir, "song.bin");
			File.WriteAllBytes (source, [4, 5]);
			File.WriteAllBytes (destination, [1, 2, 3]);

			using (DefaultFileSystem.BeforeReplaceForTests ((replacing, dest) => {
				Assert.AreEqual (Path.GetFullPath (destinationDir), Path.GetFullPath (Path.GetDirectoryName (replacing)!));
				Assert.IsTrue (File.Exists (replacing));
				Assert.IsFalse (File.Exists (source));
				Assert.AreEqual (Path.GetFullPath (destination), Path.GetFullPath (dest));
				throw new IOException ("replace failed");
			})) {
				var ex = Assert.ThrowsExactly<IOException> (() => DefaultFileSystem.Instance.Move (source, destination));
				Assert.AreEqual ("replace failed", ex.Message);
			}

			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (destination));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (source));
			Assert.IsFalse (Directory.EnumerateFiles (destinationDir).Any (path =>
				Path.GetFileName (path).StartsWith (".taglib_", StringComparison.Ordinal)));
		} finally {
			Directory.Delete (root, true);
		}
	}

	[TestMethod]
	public void EnumerateFiles_SearchPatternSkipsOtherNames ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			File.WriteAllBytes (Path.Combine (dir, ".taglib_old.tmp"), [1]);
			File.WriteAllBytes (Path.Combine (dir, "notes.tmp"), [2]);
			var names = ((IFileSystemTimestamps)DefaultFileSystem.Instance)
				.EnumerateFiles (dir, ".taglib_*.tmp")
				.Select (Path.GetFileName)
				.ToArray ();

			CollectionAssert.Contains (names, ".taglib_old.tmp");
			CollectionAssert.DoesNotContain (names, "notes.tmp");
		} finally {
			Directory.Delete (dir, true);
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
	public void Write_CallsWriteAllBytesBeforeMove ()
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
	public async Task WriteAsync_CallsWriteAllBytesBeforeMove ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var events = new List<string> ();
			var fs = new FlushOrderingFileSystem (events);

			var result = await AtomicFileWriter.WriteAsync (destination, new byte[] { 1, 2, 3 }, fs);

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
		var recovered = RecoveredPath ("/song.bin");
		var files = mock.GetAllFiles ();
		Assert.HasCount (1, files);
		Assert.IsTrue (files.ContainsKey (recovered), string.Join (", ", files.Keys));
		AssertKeptTempError (result.Error, "retry failed", recovered);
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, files[recovered]);
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
	public async Task WriteAsync_WhenWriteThrowsBeforeReplace_DeletesTemp ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance) { ThrowAfterWrite = true };

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

		var recovered = RecoveredPath ("/song.bin");
		Assert.IsFalse (restored);
		Assert.IsNotNull (keptTempError);
		AssertKeptTempError (keptTempError, "retry failed", recovered);
		Assert.IsTrue (mock.FileExists (recovered));
		Assert.IsFalse (mock.FileExists ("/.taglib_temp.tmp"));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes (recovered));
	}

	[TestMethod]
	public void Write_SymlinkToFile_ReplacesTargetAndLeavesLink ()
	{
		if (!IsUnix ())
			return;

		var root = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		var dataDir = Path.Combine (root, "data");
		var linkDir = Path.Combine (root, "links");
		Directory.CreateDirectory (dataDir);
		Directory.CreateDirectory (linkDir);
		try {
			var target = Path.Combine (dataDir, "song.bin");
			var link = Path.Combine (linkDir, "song.bin");
			File.WriteAllBytes (target, [1, 2, 3]);
			File.CreateSymbolicLink (link, Path.GetRelativePath (linkDir, target));

			var result = AtomicFileWriter.Write (link, [4, 5]);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.IsNotNull (new FileInfo (link).LinkTarget);
			var resolved = File.ResolveLinkTarget (link, returnFinalTarget: true);
			Assert.IsNotNull (resolved);
			Assert.AreEqual (Path.GetFullPath (target), Path.GetFullPath (resolved.FullName));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (target));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (link));
			AssertNoTempFiles (dataDir);
			AssertNoTempFiles (linkDir);
		} finally {
			Directory.Delete (root, true);
		}
	}

	[TestMethod]
	public void Write_SymlinkToFileInSameDirectory_ReplacesTargetAndLeavesLink ()
	{
		if (!IsUnix ())
			return;

		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var target = Path.Combine (dir, "song.bin");
			var link = Path.Combine (dir, "link.bin");
			File.WriteAllBytes (target, [1, 2, 3]);
			File.CreateSymbolicLink (link, "song.bin");

			var result = AtomicFileWriter.Write (link, [4, 5]);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.AreEqual ("song.bin", new FileInfo (link).LinkTarget);
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (target));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (link));
			AssertNoTempFiles (dir);
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_DanglingSymlink_BecomesRegularFileWithNewBytes ()
	{
		if (!IsUnix ())
			return;

		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.CreateSymbolicLink (destination, "missing-target.bin");

			var result = AtomicFileWriter.Write (destination, [4, 5]);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.IsNull (new FileInfo (destination).LinkTarget);
			Assert.IsFalse (File.GetAttributes (destination).HasFlag (FileAttributes.ReparsePoint));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (destination));
			AssertNoTempFiles (dir);
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_ExistingFile_PreservesUnixFileMode ()
	{
		// Unix file mode is unsupported on Windows, and missing on netstandard2.0.
		if (OperatingSystem.IsWindows ())
			return;

		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.WriteAllBytes (destination, [1, 2, 3]);
			const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
			File.SetUnixFileMode (destination, mode);

			var result = AtomicFileWriter.Write (destination, [4, 5]);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.AreEqual (mode, File.GetUnixFileMode (destination));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_DeletesStaleTempFilesOlderThanOneDay ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			CreateStaleTempFixture (dir);
			var destination = Path.Combine (dir, "song.bin");

			var result = AtomicFileWriter.Write (destination, [4, 5]);

			Assert.IsTrue (result.IsSuccess, result.Error);
			AssertStaleTempsSwept (dir, destination);
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public async Task WriteAsync_DeletesStaleTempFilesOlderThanOneDay ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			CreateStaleTempFixture (dir);
			var destination = Path.Combine (dir, "song.bin");

			var result = await AtomicFileWriter.WriteAsync (destination, new byte[] { 4, 5 });

			Assert.IsTrue (result.IsSuccess, result.Error);
			AssertStaleTempsSwept (dir, destination);
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_WhenDestinationNameMatchesTempPattern_DoesNotDeleteItBeforeFailure ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, ".taglib_song.tmp");
			File.WriteAllBytes (destination, [1, 2, 3]);
			File.SetLastWriteTimeUtc (destination, DateTime.UtcNow.AddHours (-48));
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance) { ThrowAfterWrite = true };

			var result = AtomicFileWriter.Write (destination, [4, 5], fs);

			Assert.IsFalse (result.IsSuccess);
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_WhenDestinationNameIsSweepTemp_DoesNotDeleteItBeforeFailure ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			// Doubled separator: the listed path is not ordinal-equal to the destination,
			// so the name exclusion is what keeps these bytes.
			var destination = dir + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar + HexTempName ('a');
			File.WriteAllBytes (destination, [1, 2, 3]);
			File.SetLastWriteTimeUtc (destination, DateTime.UtcNow.AddHours (-48));
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance) { ThrowAfterWrite = true };

			var result = AtomicFileWriter.Write (destination, [4, 5], fs);

			Assert.IsFalse (result.IsSuccess);
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (destination));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_FileSystemWithoutLastWriteTime_KeepsTempFiles ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/dir/.taglib_old.tmp", [1]);
		mock.AddFile ("/dir/.taglib_fresh.tmp", [2]);
		mock.AddFile ("/dir/notes.tmp", [3]);
		mock.AddFile ("/dir/.taglib_old.txt", [6]);

		var result = AtomicFileWriter.Write ("/dir/song.bin", [4, 5], mock);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (mock.FileExists ("/dir/.taglib_old.tmp"));
		Assert.IsTrue (mock.FileExists ("/dir/.taglib_fresh.tmp"));
		Assert.IsTrue (mock.FileExists ("/dir/notes.tmp"));
		Assert.IsTrue (mock.FileExists ("/dir/.taglib_old.txt"));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes ("/dir/song.bin"));
		Assert.IsFalse (mock.GetAllFiles ().Keys.Any (path => {
			var name = Path.GetFileName (path);
			return name.StartsWith (".taglib_", StringComparison.Ordinal)
				&& name.EndsWith (".tmp", StringComparison.Ordinal)
				&& name is not ".taglib_old.tmp" and not ".taglib_fresh.tmp";
		}));
	}

	[TestMethod]
	public void Write_NullLastWriteTime_DoesNotDeleteMatchingNames ()
	{
		var mock = new MockFileSystem ();
		mock.AddFile ("/dir/.taglib_old.tmp", [1]);
		mock.AddFile ("/dir/notes.tmp", [3]);
		mock.AddFile ("/other/.taglib_old.tmp", [9]);
		var fs = new NullTimestampFileSystem (mock);

		var result = AtomicFileWriter.Write ("/dir/song.bin", [4, 5], fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (mock.FileExists ("/dir/.taglib_old.tmp"));
		Assert.IsTrue (mock.FileExists ("/dir/notes.tmp"));
		Assert.IsTrue (mock.FileExists ("/other/.taglib_old.tmp"));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, mock.ReadAllBytes ("/dir/song.bin"));
	}

	[TestMethod]
	public void Write_SweepThrows_ReturnsFailureInsteadOfThrowing ()
	{
		var fs = new RecordingFileSystem (new MockFileSystem ()) { ThrowOnEnumerate = true };
		var destination = "/dir-" + Guid.NewGuid ().ToString ("N") + "/song.bin";

		FileWriteResult result;
		try {
			result = AtomicFileWriter.Write (destination, [1, 2, 3], fs);
		} catch (Exception ex) {
			Assert.Fail ("sweep threw to the caller: " + ex);
			return;
		}

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "sweep failed");
		Assert.IsFalse (fs.FileExists (destination));
	}

	[TestMethod]
	public async Task WriteAsync_SweepThrows_ReturnsFailureInsteadOfThrowing ()
	{
		var fs = new RecordingFileSystem (new MockFileSystem ()) { ThrowOnEnumerate = true };
		var destination = "/dir-" + Guid.NewGuid ().ToString ("N") + "/song.bin";

		FileWriteResult result;
		try {
			result = await AtomicFileWriter.WriteAsync (destination, new byte[] { 1, 2, 3 }, fs);
		} catch (Exception ex) {
			Assert.Fail ("sweep threw to the caller: " + ex);
			return;
		}

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "sweep failed");
		Assert.IsFalse (fs.FileExists (destination));
	}

	[TestMethod]
	public void Write_SecondSaveInSameDirectory_DoesNotDeleteTempAgedAfterFirstSave ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.WriteAllBytes (destination, [1]);
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			var now = DateTime.UtcNow;
			using (AtomicFileWriter.OverrideSweepClockForTests (() => now, TimeSpan.FromHours (1))) {
				var first = AtomicFileWriter.Write (destination, [1, 2], fs);
				Assert.IsTrue (first.IsSuccess, first.Error);
				Assert.AreEqual (1, fs.EnumerateCount);
				Assert.AreEqual (".taglib_*.tmp", fs.EnumeratePattern);

				var aged = Path.Combine (dir, HexTempName ('a'));
				var legacy = Path.Combine (dir, ".taglib_song.bin.tmp");
				File.WriteAllBytes (aged, [9]);
				File.WriteAllBytes (legacy, [7]);
				File.SetLastWriteTimeUtc (aged, now.AddHours (-25));
				File.SetLastWriteTimeUtc (legacy, now.AddHours (-48));

				var second = AtomicFileWriter.Write (destination, [3, 4], fs);

				Assert.IsTrue (second.IsSuccess, second.Error);
				Assert.AreEqual (1, fs.EnumerateCount);
				Assert.IsTrue (File.Exists (aged), "second save swept a temp that aged after the first sweep");
				CollectionAssert.AreEqual (new byte[] { 9 }, File.ReadAllBytes (aged));
				Assert.IsTrue (File.Exists (legacy));
				CollectionAssert.AreEqual (new byte[] { 3, 4 }, File.ReadAllBytes (destination));

				now = now.AddHours (2);
				var third = AtomicFileWriter.Write (destination, [5], fs);
				Assert.IsTrue (third.IsSuccess, third.Error);
				Assert.AreEqual (2, fs.EnumerateCount);
				Assert.AreEqual (".taglib_*.tmp", fs.EnumeratePattern);
				Assert.IsFalse (File.Exists (aged));
				Assert.IsTrue (File.Exists (legacy), "legacy temp was deleted while its file was not the only name filter");
				CollectionAssert.AreEqual (new byte[] { 7 }, File.ReadAllBytes (legacy));
			}
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public async Task WriteAsync_SecondSaveInSameDirectory_DoesNotScanAgainInsideTheInterval ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.WriteAllBytes (destination, [1]);
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			var now = DateTime.UtcNow;
			using (AtomicFileWriter.OverrideSweepClockForTests (() => now, TimeSpan.FromHours (1))) {
				var first = await AtomicFileWriter.WriteAsync (destination, new byte[] { 1, 2 }, fs);
				Assert.IsTrue (first.IsSuccess, first.Error);
				var aged = Path.Combine (dir, HexTempName ('a'));
				File.WriteAllBytes (aged, [9]);
				File.SetLastWriteTimeUtc (aged, now.AddHours (-25));

				var second = await AtomicFileWriter.WriteAsync (destination, new byte[] { 3, 4 }, fs);

				Assert.IsTrue (second.IsSuccess, second.Error);
				Assert.AreEqual (1, fs.EnumerateCount);
				Assert.IsTrue (File.Exists (aged));
			}
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_AgedKeptTempThatIsTheOnlyCopy_IsNotDeleted ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.WriteAllBytes (destination, [1, 2, 3]);
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			fs.OnNextMove ((inner, _, dest) => {
				inner.Delete (dest);
				throw new IOException ("Unable to move replacement");
			});
			fs.OnNextMove ((_, _, _) => throw new IOException ("retry failed"));
			var now = DateTime.UtcNow;
			using var clock = AtomicFileWriter.OverrideSweepClockForTests (() => now, TimeSpan.FromHours (1));

			var failed = AtomicFileWriter.Write (destination, [4, 5], fs);

			var recovered = RecoveredPath (destination);
			Assert.IsFalse (failed.IsSuccess);
			Assert.IsNotNull (failed.Error);
			AssertKeptTempError (failed.Error, "retry failed", recovered);
			Assert.IsTrue (File.Exists (recovered));
			Assert.IsFalse (File.Exists (destination));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (recovered));

			File.SetLastWriteTimeUtc (recovered, now.AddHours (-25));
			var sibling = Path.Combine (dir, HexTempName ('f'));
			File.WriteAllBytes (sibling, [8]);
			File.SetLastWriteTimeUtc (sibling, now.AddHours (-25));
			now = now.AddHours (2);

			var swept = AtomicFileWriter.Write (Path.Combine (dir, "other.bin"), [9], fs);

			Assert.IsTrue (swept.IsSuccess, swept.Error);
			Assert.IsTrue (File.Exists (recovered), "aged kept temp was deleted");
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (recovered));
			Assert.IsFalse (File.Exists (sibling));
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_SecondFailedReplace_KeepsNewBytesOffTheSweepName ()
	{
		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var destination = Path.Combine (dir, "song.bin");
			File.WriteAllBytes (destination, [1, 2, 3]);
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			var now = DateTime.UtcNow;
			using (AtomicFileWriter.OverrideSweepClockForTests (() => now, TimeSpan.FromHours (1))) {
				FailReplaceAfterRemovingDestination (fs);
				var first = AtomicFileWriter.Write (destination, [4, 5], fs);

				var recovered = RecoveredPath (destination);
				Assert.IsFalse (first.IsSuccess);
				Assert.IsNotNull (first.Error);
				AssertKeptTempError (first.Error, "retry failed", recovered);
				CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (recovered));

				// The first failure removed the destination. Put it back so this is also a failed replace.
				File.WriteAllBytes (destination, [1, 2, 3]);
				FailReplaceAfterRemovingDestination (fs);
				var second = AtomicFileWriter.Write (destination, [6, 7], fs);

				Assert.IsFalse (second.IsSuccess);
				Assert.IsNotNull (second.Error);
				CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (recovered));
				var kept = PathReportedByKeptTempError (second.Error);
				Assert.AreNotEqual (recovered, kept);
				Assert.IsFalse (IsSweepTempName (Path.GetFileName (kept)), kept);
				CollectionAssert.AreEqual (new byte[] { 6, 7 }, File.ReadAllBytes (kept));

				now = now.AddHours (2);
				foreach (var candidate in Directory.GetFiles (dir)) {
					if (IsSweepTempName (Path.GetFileName (candidate)))
						File.SetLastWriteTimeUtc (candidate, now.AddHours (-25));
				}

				var swept = AtomicFileWriter.Write (Path.Combine (dir, "other.bin"), [9], fs);

				Assert.IsTrue (swept.IsSuccess, swept.Error);
				Assert.IsTrue (File.Exists (kept), kept);
				CollectionAssert.AreEqual (new byte[] { 6, 7 }, File.ReadAllBytes (kept));
				CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (recovered));
			}
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_SymlinkToOtherDirectory_TempAppearsBesideTarget ()
	{
		// Same volume: the link and the target are two directories. A second volume is not mounted.
		if (!IsUnix ())
			return;

		var root = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		var dataDir = Path.Combine (root, "data");
		var linkDir = Path.Combine (root, "links");
		Directory.CreateDirectory (dataDir);
		Directory.CreateDirectory (linkDir);
		try {
			var target = Path.Combine (dataDir, "song.bin");
			var link = Path.Combine (linkDir, "song.bin");
			File.WriteAllBytes (target, [1, 2, 3]);
			File.CreateSymbolicLink (link, Path.GetRelativePath (linkDir, target));
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			fs.DuringWrite = path => {
				Assert.AreEqual (Path.GetFullPath (dataDir), Path.GetFullPath (Path.GetDirectoryName (path)!));
				Assert.IsTrue (File.Exists (path));
				Assert.IsFalse (Directory.EnumerateFiles (linkDir).Any (candidate =>
					Path.GetFileName (candidate).StartsWith (".taglib_", StringComparison.Ordinal)));
			};

			var result = AtomicFileWriter.Write (link, [4, 5], fs);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.HasCount (1, fs.Writes);
			Assert.IsNotNull (new FileInfo (link).LinkTarget);
			var resolved = File.ResolveLinkTarget (link, returnFinalTarget: true);
			Assert.IsNotNull (resolved);
			Assert.AreEqual (Path.GetFullPath (target), Path.GetFullPath (resolved.FullName));
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (target));
			AssertNoTempFiles (dataDir);
			AssertNoTempFiles (linkDir);
		} finally {
			Directory.Delete (root, true);
		}
	}

	[TestMethod]
	public async Task WriteAsync_SymlinkToOtherDirectory_TempAppearsBesideTarget ()
	{
		if (!IsUnix ())
			return;

		var root = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		var dataDir = Path.Combine (root, "data");
		var linkDir = Path.Combine (root, "links");
		Directory.CreateDirectory (dataDir);
		Directory.CreateDirectory (linkDir);
		try {
			var target = Path.Combine (dataDir, "song.bin");
			var link = Path.Combine (linkDir, "song.bin");
			File.WriteAllBytes (target, [1, 2, 3]);
			File.CreateSymbolicLink (link, Path.GetRelativePath (linkDir, target));
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			fs.DuringWrite = path => {
				Assert.AreEqual (Path.GetFullPath (dataDir), Path.GetFullPath (Path.GetDirectoryName (path)!));
				Assert.IsTrue (File.Exists (path));
			};

			var result = await AtomicFileWriter.WriteAsync (link, new byte[] { 4, 5 }, fs);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.IsNotNull (new FileInfo (link).LinkTarget);
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (target));
			AssertNoTempFiles (dataDir);
			AssertNoTempFiles (linkDir);
		} finally {
			Directory.Delete (root, true);
		}
	}

	[TestMethod]
	public void Write_SymlinkInSameDirectory_TempAppearsBesideTarget ()
	{
		if (!IsUnix ())
			return;

		var dir = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		try {
			var target = Path.Combine (dir, "song.bin");
			var link = Path.Combine (dir, "link.bin");
			File.WriteAllBytes (target, [1, 2, 3]);
			File.CreateSymbolicLink (link, "song.bin");
			var fs = new RecordingFileSystem (DefaultFileSystem.Instance);
			fs.DuringWrite = path => {
				Assert.AreEqual (Path.GetFullPath (dir), Path.GetFullPath (Path.GetDirectoryName (path)!));
				Assert.IsTrue (File.Exists (path));
				Assert.AreNotEqual (Path.GetFullPath (link), Path.GetFullPath (path));
				Assert.AreNotEqual (Path.GetFullPath (target), Path.GetFullPath (path));
			};

			var result = AtomicFileWriter.Write (link, [4, 5], fs);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.AreEqual ("song.bin", new FileInfo (link).LinkTarget);
			CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (target));
			AssertNoTempFiles (dir);
		} finally {
			Directory.Delete (dir, true);
		}
	}

	[TestMethod]
	public void Write_SymlinkTargetDirectoryNotWritable_LeavesLinkAndTargetUnchanged ()
	{
		if (OperatingSystem.IsWindows ())
			return;

		var root = Path.Combine (Path.GetTempPath (), "tls-" + Guid.NewGuid ().ToString ("N"));
		var dataDir = Path.Combine (root, "data");
		var linkDir = Path.Combine (root, "links");
		Directory.CreateDirectory (dataDir);
		Directory.CreateDirectory (linkDir);
		var target = Path.Combine (dataDir, "song.bin");
		var link = Path.Combine (linkDir, "song.bin");
		File.WriteAllBytes (target, [1, 2, 3]);
		File.CreateSymbolicLink (link, Path.GetRelativePath (linkDir, target));
		var dirMode = File.GetUnixFileMode (dataDir);
		File.SetUnixFileMode (
			dataDir,
			dirMode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
		try {
			var result = AtomicFileWriter.Write (link, [4, 5]);

			Assert.IsFalse (result.IsSuccess, result.Error);
			Assert.IsNotNull (new FileInfo (link).LinkTarget);
			var resolved = File.ResolveLinkTarget (link, returnFinalTarget: true);
			Assert.IsNotNull (resolved);
			Assert.AreEqual (Path.GetFullPath (target), Path.GetFullPath (resolved.FullName));
			CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, File.ReadAllBytes (target));
			AssertNoTempFiles (dataDir);
			AssertNoTempFiles (linkDir);
		} finally {
			File.SetUnixFileMode (dataDir, dirMode);
			Directory.Delete (root, true);
		}
	}

	static void FailReplaceAfterRemovingDestination (RecordingFileSystem fs)
	{
		fs.OnNextMove ((inner, _, dest) => {
			inner.Delete (dest);
			throw new IOException ("Unable to move replacement");
		});
		fs.OnNextMove ((_, _, _) => throw new IOException ("retry failed"));
	}

	static string PathReportedByKeptTempError (string error)
	{
		const string prefix = "Temporary file kept at '";
		var start = error.IndexOf (prefix, StringComparison.Ordinal);
		Assert.IsTrue (start >= 0, error);
		start += prefix.Length;
		var end = error.IndexOf ("': ", start, StringComparison.Ordinal);
		Assert.IsTrue (end > start, error);
		return error.Substring (start, end - start);
	}

	static bool IsSweepTempName (string name)
	{
		const string prefix = ".taglib_";
		const string suffix = ".tmp";
		if (name.Length != prefix.Length + 32 + suffix.Length)
			return false;
		if (!name.StartsWith (prefix, StringComparison.Ordinal)
			|| !name.EndsWith (suffix, StringComparison.Ordinal))
			return false;

		for (var i = prefix.Length; i < prefix.Length + 32; i++) {
			var value = name[i];
			if (value is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
				return false;
		}

		return true;
	}

	static string HexTempName (char digit) => ".taglib_" + new string (digit, 32) + ".tmp";

	static string RecoveredPath (string destination)
	{
		var directory = Path.GetDirectoryName (destination);
		var name = Path.GetFileName (destination) + ".taglib-recovered";
		return string.IsNullOrEmpty (directory) ? name : Path.Combine (directory, name);
	}

	static void AssertKeptTempError (string error, string exceptionMessage, string recoveredPath)
	{
		Assert.AreEqual (1, CountOf (error, exceptionMessage), error);
		Assert.AreEqual (1, CountOf (error, recoveredPath), error);
	}

	static int CountOf (string text, string value)
	{
		var count = 0;
		var index = 0;
		while ((index = text.IndexOf (value, index, StringComparison.Ordinal)) >= 0) {
			count++;
			index += value.Length;
		}

		return count;
	}

	static void CreateStaleTempFixture (string dir)
	{
		var aged25 = Path.Combine (dir, HexTempName ('a'));
		var aged23 = Path.Combine (dir, HexTempName ('b'));
		var freshTemp = Path.Combine (dir, HexTempName ('c'));
		var legacyMissing = Path.Combine (dir, ".taglib_missing.bin.tmp");
		var legacyShort = Path.Combine (dir, ".taglib_old.tmp");
		var shortHex = Path.Combine (dir, ".taglib_" + new string ('d', 31) + ".tmp");
		var longHex = Path.Combine (dir, ".taglib_" + new string ('e', 33) + ".tmp");
		var nonHex = Path.Combine (dir, ".taglib_" + new string ('g', 32) + ".tmp");
		var notes = Path.Combine (dir, "notes.tmp");
		var prefixOnly = Path.Combine (dir, ".taglib_old.txt");
		var missingDot = Path.Combine (dir, "taglib_old.tmp");
		var nestedDir = Path.Combine (dir, "nested");
		Directory.CreateDirectory (nestedDir);
		var nestedTemp = Path.Combine (nestedDir, ".taglib_old.tmp");
		File.WriteAllBytes (Path.Combine (dir, "song.bin"), [1, 2, 3]);
		File.WriteAllBytes (aged25, [1]);
		File.WriteAllBytes (aged23, [4]);
		File.WriteAllBytes (freshTemp, [2]);
		File.WriteAllBytes (legacyMissing, [5]);
		File.WriteAllBytes (legacyShort, [6]);
		File.WriteAllBytes (shortHex, [7]);
		File.WriteAllBytes (longHex, [8]);
		File.WriteAllBytes (nonHex, [9]);
		File.WriteAllBytes (notes, [3]);
		File.WriteAllBytes (prefixOnly, [6]);
		File.WriteAllBytes (missingDot, [7]);
		File.WriteAllBytes (nestedTemp, [8]);
		var stale = DateTime.UtcNow.AddHours (-48);
		File.SetLastWriteTimeUtc (aged25, DateTime.UtcNow.AddHours (-25));
		File.SetLastWriteTimeUtc (aged23, DateTime.UtcNow.AddHours (-23));
		File.SetLastWriteTimeUtc (freshTemp, DateTime.UtcNow);
		File.SetLastWriteTimeUtc (legacyMissing, stale);
		File.SetLastWriteTimeUtc (legacyShort, stale);
		File.SetLastWriteTimeUtc (shortHex, stale);
		File.SetLastWriteTimeUtc (longHex, stale);
		File.SetLastWriteTimeUtc (nonHex, stale);
		File.SetLastWriteTimeUtc (notes, stale);
		File.SetLastWriteTimeUtc (prefixOnly, stale);
		File.SetLastWriteTimeUtc (missingDot, stale);
		File.SetLastWriteTimeUtc (nestedTemp, stale);
	}

	static void AssertStaleTempsSwept (string dir, string destination)
	{
		Assert.IsFalse (File.Exists (Path.Combine (dir, HexTempName ('a'))));
		Assert.IsTrue (File.Exists (Path.Combine (dir, HexTempName ('b'))));
		Assert.IsTrue (File.Exists (Path.Combine (dir, HexTempName ('c'))));
		Assert.IsTrue (File.Exists (Path.Combine (dir, ".taglib_missing.bin.tmp")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, ".taglib_old.tmp")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, ".taglib_" + new string ('d', 31) + ".tmp")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, ".taglib_" + new string ('e', 33) + ".tmp")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, ".taglib_" + new string ('g', 32) + ".tmp")));
		Assert.IsFalse (File.Exists (Path.Combine (dir, "missing.bin")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, "notes.tmp")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, ".taglib_old.txt")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, "taglib_old.tmp")));
		Assert.IsTrue (File.Exists (Path.Combine (dir, "nested", ".taglib_old.tmp")));
		CollectionAssert.AreEqual (new byte[] { 4 }, File.ReadAllBytes (Path.Combine (dir, HexTempName ('b'))));
		CollectionAssert.AreEqual (new byte[] { 2 }, File.ReadAllBytes (Path.Combine (dir, HexTempName ('c'))));
		CollectionAssert.AreEqual (new byte[] { 5 }, File.ReadAllBytes (Path.Combine (dir, ".taglib_missing.bin.tmp")));
		CollectionAssert.AreEqual (new byte[] { 3 }, File.ReadAllBytes (Path.Combine (dir, "notes.tmp")));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, File.ReadAllBytes (destination));
	}

	static void AssertNoTempFiles (string directory)
	{
		var leftovers = Directory.EnumerateFiles (directory)
			.Where (path => Path.GetFileName (path).StartsWith (".taglib_", StringComparison.Ordinal))
			.ToArray ();
		Assert.AreEqual (0, leftovers.Length, string.Join (", ", leftovers));
	}

	static bool IsUnix () => OperatingSystem.IsWindows () == false;
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
