// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Default implementation of <see cref="IFileSystem"/> that wraps the real file system.
/// </summary>
public sealed class DefaultFileSystem : IFileSystem
{
	/// <summary>
	/// Gets the singleton instance of the default file system.
	/// </summary>
	public static DefaultFileSystem Instance { get; } = new ();

	DefaultFileSystem () { }

	/// <inheritdoc/>
	public bool FileExists (string path) => File.Exists (path);

	/// <inheritdoc/>
	public Stream OpenRead (string path) => File.OpenRead (path);

	/// <inheritdoc/>
	public Stream OpenReadWrite (string path) =>
		new FileStream (path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

	/// <inheritdoc/>
	public Stream Create (string path) => File.Create (path);

	/// <inheritdoc/>
	public byte[] ReadAllBytes (string path) => File.ReadAllBytes (path);

	/// <inheritdoc/>
	public async Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0
		// File.ReadAllBytesAsync doesn't exist in netstandard2.0.
		// Use CopyToAsync instead of ReadAsync to ensure complete reads,
		// as ReadAsync may return fewer bytes than requested (partial reads).
		using var stream = File.OpenRead (path);
		using var ms = new MemoryStream ();
		await stream.CopyToAsync (ms, 81920, cancellationToken).ConfigureAwait (false);
		return ms.ToArray ();
#else
		return await File.ReadAllBytesAsync (path, cancellationToken).ConfigureAwait (false);
#endif
	}

	/// <inheritdoc/>
	public void WriteAllBytes (string path, ReadOnlySpan<byte> data)
	{
		using var stream = new FileStream (path, FileMode.Create, FileAccess.Write, FileShare.None);
		WriteAll (stream, data);
		FlushToDisk (stream);
	}

	/// <inheritdoc/>
	public async Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		using var stream = new FileStream (
			path,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			4096,
			FileOptions.Asynchronous);
#if NETSTANDARD2_0
		var buffer = data.ToArray ();
		await stream.WriteAsync (buffer, 0, buffer.Length, cancellationToken).ConfigureAwait (false);
#else
		await stream.WriteAsync (data, cancellationToken).ConfigureAwait (false);
#endif
		// Flush(bool) has no async overload; the replace must not start before the flush returns.
		FlushToDisk (stream);
	}

	static void WriteAll (FileStream stream, ReadOnlySpan<byte> data)
	{
#if NETSTANDARD2_0
		var buffer = data.ToArray ();
		stream.Write (buffer, 0, buffer.Length);
#else
		stream.Write (data);
#endif
	}

	/// <summary>
	/// Flushes a temp file to disk before it replaces the destination.
	/// </summary>
	internal static void FlushToDisk (FileStream stream)
	{
		// Stream.Flush(bool) does not exist on these targets. FileStream.Flush(bool) does,
		// including netstandard2.0 and netstandard2.1, so every build can request a disk flush.
		stream.Flush (flushToDisk: true);
	}

	/// <inheritdoc/>
	public void Delete (string path)
	{
		if (File.Exists (path))
			File.Delete (path);
	}

	/// <inheritdoc/>
	public void Move (string sourcePath, string destinationPath)
	{
		// Replace the destination in one step. Deleting it first leaves a window
		// where a crash or a failed move destroys the original.
		if (!File.Exists (destinationPath)) {
			File.Move (sourcePath, destinationPath);
			return;
		}

		// ignoreMetadataErrors avoids failing the replace on ACL or attribute merge errors.
		File.Replace (sourcePath, destinationPath, null, ignoreMetadataErrors: true);
	}

	/// <inheritdoc/>
	public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

	/// <inheritdoc/>
	public string GetFileName (string path) => Path.GetFileName (path);

	/// <inheritdoc/>
	public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);
}
