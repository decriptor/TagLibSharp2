// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Mock file system for testing.
/// </summary>
internal sealed class MockFileSystem : IFileSystem
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
		_files[path] = Array.Empty<byte> ();
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
		fs.AddFile ("/test.txt", new byte[] { 1, 2, 3 });

		Assert.IsTrue (fs.FileExists ("/test.txt"));
		Assert.IsFalse (fs.FileExists ("/other.txt"));
	}

	[TestMethod]
	public void MockFileSystem_ReadAllBytes_ReturnsData ()
	{
		var fs = new MockFileSystem ();
		var data = new byte[] { 1, 2, 3, 4, 5 };
		fs.AddFile ("/test.bin", data);

		var result = fs.ReadAllBytes ("/test.bin");

		CollectionAssert.AreEqual (data, result);
	}

	[TestMethod]
	public void MockFileSystem_ReadAllBytes_ThrowsForInaccessible ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/test.bin", new byte[] { 1 });
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
		var data = new byte[] { 0x66, 0x4C, 0x61, 0x43 }; // fLaC
		fs.AddFile ("/test.flac", data);

		var result = FileHelper.SafeReadAllBytes ("/test.flac", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
		CollectionAssert.AreEqual (data, result.Data);
	}

	[TestMethod]
	public void SafeReadAllBytes_AccessDenied_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/protected.txt", new byte[] { 1, 2, 3 });
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
}

[TestClass]
public sealed class FileReadResultTests
{
	[TestMethod]
	public void Success_HasCorrectProperties ()
	{
		var data = new byte[] { 1, 2, 3 };
		var result = FileReadResult.Success (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Error);
		CollectionAssert.AreEqual (data, result.Data);
	}

	[TestMethod]
	public void Failure_HasCorrectProperties ()
	{
		var result = FileReadResult.Failure ("Something went wrong");

		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Something went wrong", result.Error);
		Assert.IsNull (result.Data);
	}

	[TestMethod]
	public void Equals_SameData_ReturnsTrue ()
	{
		var data = new byte[] { 1, 2, 3 };
		var result1 = FileReadResult.Success (data);
		var result2 = FileReadResult.Success (data);

		Assert.AreEqual (result1, result2);
		Assert.IsTrue (result1 == result2);
		Assert.IsFalse (result1 != result2);
	}
}
