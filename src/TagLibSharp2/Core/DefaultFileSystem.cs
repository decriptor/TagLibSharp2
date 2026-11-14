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
#if NETSTANDARD2_0
		File.WriteAllBytes (path, data.ToArray ());
#else
		using var stream = new FileStream (path, FileMode.Create, FileAccess.Write, FileShare.None);
		stream.Write (data);
		stream.Flush ();
#endif
	}

	/// <inheritdoc/>
	public async Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0
		using var stream = new FileStream (path, FileMode.Create, FileAccess.Write, FileShare.None);
		await stream.WriteAsync (data.ToArray (), 0, data.Length, cancellationToken).ConfigureAwait (false);
		await stream.FlushAsync (cancellationToken).ConfigureAwait (false);
#else
		await File.WriteAllBytesAsync (path, data.ToArray (), cancellationToken).ConfigureAwait (false);
#endif
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

		File.Replace (sourcePath, destinationPath, null);
	}

	/// <inheritdoc/>
	public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

	/// <inheritdoc/>
	public string GetFileName (string path) => Path.GetFileName (path);

	/// <inheritdoc/>
	public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);
}
