// Copyright (c) 2025 Stephen Shaw and contributors
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

[TestClass]
public sealed class FileSystemTests
{
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
