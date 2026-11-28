// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A file system that fails. <see cref="RootDirectory"/> answers a directory lookup and throws on every other
/// call, counting both. <see cref="Io"/> reports the file present and throws the given error on every read or
/// write. Neither reports timestamps, so an atomic save does not list the directory.
/// </summary>
sealed class FaultFileSystem : IFileSystem
{
	readonly Exception? _io;

	readonly bool _rootLookup;

	FaultFileSystem (Exception? io, bool rootLookup)
	{
		_io = io;
		_rootLookup = rootLookup;
	}

	public int Calls { get; private set; }

	/// <summary>A directory lookup returns <c>"/"</c>. Any other call throws and is counted.</summary>
	public static FaultFileSystem RootDirectory () => new (null, true);

	/// <summary>The file exists. Opening, reading, writing, or deleting it throws <paramref name="error"/>.</summary>
	public static FaultFileSystem Io (Exception error) => new (error, false);

	public bool FileExists (string path) => _rootLookup ? Fail<bool> (path) : true;

	public Stream OpenRead (string path) => Fail<Stream> (path);

	public Stream OpenReadWrite (string path) => Fail<Stream> (path);

	public Stream Create (string path) => Fail<Stream> (path);

	public byte[] ReadAllBytes (string path) => Fail<byte[]> (path);

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
		Fail<Task<byte[]>> (path);

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => Fail<bool> (path);

	public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
		Fail<Task> (path);

	public void Delete (string path) => Fail<bool> (path);

	public void Move (string sourcePath, string destinationPath) => Fail<bool> (sourcePath);

	public string? GetDirectoryName (string path)
	{
		if (!_rootLookup)
			return null;
		Calls++;
		return "/";
	}

	public string GetFileName (string path) => _rootLookup ? Fail<string> (path) : path;

	public string CombinePath (string path1, string path2) => _rootLookup ? Fail<string> (path1) : path1 + path2;

	T Fail<T> (string path)
	{
		Calls++;
		throw _io ?? new InvalidOperationException (path);
	}
}
