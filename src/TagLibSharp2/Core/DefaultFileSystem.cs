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
		WriteAllBytes (stream, data);
	}

	/// <summary>
	/// Writes <paramref name="data"/> through <see cref="WriteAndFlush"/> so a caller can supply the stream.
	/// </summary>
	internal static void WriteAllBytes (FileStream stream, ReadOnlySpan<byte> data) =>
		WriteAndFlush (stream, data);

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
		await WriteAllBytesAsync (stream, data, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Writes <paramref name="data"/> through <see cref="WriteAndFlushAsync"/> so a caller can supply the stream.
	/// </summary>
	internal static Task WriteAllBytesAsync (FileStream stream, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
		WriteAndFlushAsync (stream, data, cancellationToken);

	/// <summary>
	/// Writes <paramref name="data"/> and flushes it to disk before the caller replaces the destination.
	/// </summary>
	internal static void WriteAndFlush (FileStream stream, ReadOnlySpan<byte> data)
	{
		WriteAll (stream, data);
		FlushToDisk (stream);
	}

	/// <summary>
	/// Writes <paramref name="data"/> and flushes it to disk before the caller replaces the destination.
	/// </summary>
	internal static async Task WriteAndFlushAsync (
		FileStream stream,
		ReadOnlyMemory<byte> data,
		CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0
		var buffer = data.ToArray ();
		await stream.WriteAsync (buffer, 0, buffer.Length, cancellationToken).ConfigureAwait (false);
#else
		await stream.WriteAsync (data, cancellationToken).ConfigureAwait (false);
#endif
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
	static void FlushToDisk (FileStream stream)
	{
		// Stream.Flush(bool) does not exist on these targets. FileStream.Flush(bool) does,
		// including netstandard2.0 and netstandard2.1, so every build can request a disk flush.
		// Flush(bool) has no async overload; the replace must not start before the flush returns.
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
		if (TryMoveThroughUnixSymlink (sourcePath, destinationPath))
			return;

		if (!File.Exists (destinationPath)) {
			File.Move (sourcePath, destinationPath);
			return;
		}

		ReplaceExisting (sourcePath, destinationPath);
	}

	/// <summary>
	/// When <paramref name="destinationPath"/> is a symlink to a file, replaces that file from a temp
	/// in the file's directory and leaves the link in place. A dangling symlink is removed by renaming
	/// <paramref name="sourcePath"/> onto it.
	/// </summary>
	/// <returns><c>true</c> when the path was a Unix symlink and the move handled it.</returns>
	static bool TryMoveThroughUnixSymlink (string sourcePath, string destinationPath)
	{
#if NET7_0_OR_GREATER
		if (!IsUnix ())
			return false;

		FileSystemInfo? target;
		try {
			target = File.ResolveLinkTarget (destinationPath, returnFinalTarget: true);
		} catch (IOException) {
			return false;
		} catch (UnauthorizedAccessException) {
			return false;
		}

		if (target is null)
			return false;

		// Directory links are not file saves. A missing final target is a dangling link.
		if (Directory.Exists (target.FullName))
			return false;

		if (!File.Exists (target.FullName)) {
			File.Move (sourcePath, destinationPath, overwrite: true);
			return true;
		}

		ReplaceExisting (sourcePath, target.FullName);
		return true;
#else
		_ = sourcePath;
		_ = destinationPath;
		return false;
#endif
	}

	static void ReplaceExisting (string sourcePath, string destinationPath)
	{
		var replacingPath = PlaceTempBesideDestination (sourcePath, destinationPath);
		try {
			CopyUnixFileMode (destinationPath, replacingPath);
			// ignoreMetadataErrors avoids failing the replace on ACL or attribute merge errors.
			File.Replace (replacingPath, destinationPath, null, ignoreMetadataErrors: true);
		} catch (IOException) {
			MoveTempBack (sourcePath, replacingPath);
			throw;
		} catch (UnauthorizedAccessException) {
			MoveTempBack (sourcePath, replacingPath);
			throw;
		}
	}

	/// <summary>
	/// Moves the temp next to <paramref name="destinationPath"/> when it is not already there,
	/// so the replace stays on one volume.
	/// </summary>
	static string PlaceTempBesideDestination (string sourcePath, string destinationPath)
	{
		var destinationDirectory = Path.GetDirectoryName (Path.GetFullPath (destinationPath));
		var sourceDirectory = Path.GetDirectoryName (Path.GetFullPath (sourcePath));
		if (string.IsNullOrEmpty (destinationDirectory)
			|| string.Equals (destinationDirectory, sourceDirectory, StringComparison.Ordinal))
			return sourcePath;

		var relocated = Path.Combine (destinationDirectory, Path.GetFileName (sourcePath));
		File.Move (sourcePath, relocated);
		return relocated;
	}

	static void MoveTempBack (string sourcePath, string replacingPath)
	{
		if (string.Equals (Path.GetFullPath (sourcePath), Path.GetFullPath (replacingPath), StringComparison.Ordinal))
			return;
		if (!File.Exists (replacingPath))
			return;

		try {
			File.Move (replacingPath, sourcePath);
		} catch (IOException) {
			// The temp is no longer at the caller's path. Leave it beside the target.
		} catch (UnauthorizedAccessException) {
		}
	}

	static void CopyUnixFileMode (string fromPath, string toPath)
	{
#if NET7_0_OR_GREATER
		if (!OperatingSystem.IsLinux () && !OperatingSystem.IsMacOS () && !OperatingSystem.IsFreeBSD ())
			return;

		// Modes belong to the file, not to a symlink path. Following a link would chmod the target.
		if (File.ResolveLinkTarget (fromPath, returnFinalTarget: false) is not null)
			return;

		File.SetUnixFileMode (toPath, File.GetUnixFileMode (fromPath));
#endif
	}

#if NET7_0_OR_GREATER
	static bool IsUnix () =>
		OperatingSystem.IsLinux () || OperatingSystem.IsMacOS () || OperatingSystem.IsFreeBSD ();
#endif

	/// <inheritdoc/>
	public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

	/// <inheritdoc/>
	public string GetFileName (string path) => Path.GetFileName (path);

	/// <inheritdoc/>
	public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);
}
