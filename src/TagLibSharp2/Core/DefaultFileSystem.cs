// Copyright (c) 2025 Stephen Shaw and contributors
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
}
