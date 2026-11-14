// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Provides atomic file write operations to prevent data corruption.
/// </summary>
/// <remarks>
/// <para>
/// Atomic writes work by writing to a temporary file first, then renaming
/// the temp file to the target path. This ensures that either the original
/// file remains intact (if the write fails) or the new file is complete
/// (if the write succeeds). There is never a partial or corrupted file.
/// </para>
/// <para>
/// The temp file is created in the same directory as the target to ensure
/// the rename operation is atomic (same filesystem).
/// </para>
/// </remarks>
public static class AtomicFileWriter
{
	const string TempFilePrefix = ".taglib_";
	const string TempFileSuffix = ".tmp";

	/// <summary>
	/// Writes data to a file atomically.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="data">The data to write.</param>
	/// <param name="fileSystem">The file system to use. Uses default if null.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static FileWriteResult Write (string path, ReadOnlySpan<byte> data, IFileSystem? fileSystem = null)
	{
		fileSystem ??= DefaultFileSystem.Instance;

		var tempPath = GetTempPath (path, fileSystem);

		try {
			// Write to temp file
			fileSystem.WriteAllBytes (tempPath, data);

			// Atomically replace the target file
			fileSystem.Move (tempPath, path);

			return FileWriteResult.Success (data.Length);
		} catch (IOException ex) {
			// Clean up temp file on failure
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ($"I/O error writing file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	/// <summary>
	/// Writes data to a file atomically and asynchronously.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="data">The data to write.</param>
	/// <param name="fileSystem">The file system to use. Uses default if null.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing the result indicating success or failure.</returns>
	public static async Task<FileWriteResult> WriteAsync (
		string path,
		ReadOnlyMemory<byte> data,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		fileSystem ??= DefaultFileSystem.Instance;

		var tempPath = GetTempPath (path, fileSystem);

		try {
			// Write to temp file
			await fileSystem.WriteAllBytesAsync (tempPath, data, cancellationToken).ConfigureAwait (false);

			// Check for cancellation before the rename
			cancellationToken.ThrowIfCancellationRequested ();

			// Atomically replace the target file
			fileSystem.Move (tempPath, path);

			return FileWriteResult.Success (data.Length);
		} catch (OperationCanceledException) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ("Operation was cancelled");
		} catch (IOException ex) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ($"I/O error writing file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	static string GetTempPath (string targetPath, IFileSystem fileSystem)
	{
		var directory = fileSystem.GetDirectoryName (targetPath) ?? ".";
		var fileName = fileSystem.GetFileName (targetPath);
		var tempFileName = $"{TempFilePrefix}{Guid.NewGuid ():N}_{fileName}{TempFileSuffix}";
		return fileSystem.CombinePath (directory, tempFileName);
	}

	static void CleanupTempFile (string tempPath, IFileSystem fileSystem)
	{
		try {
			fileSystem.Delete (tempPath);
		}
#pragma warning disable CA1031 // Best effort cleanup - failure is not actionable
		catch {
			// Intentionally ignore - we can't do anything useful if cleanup fails
		}
#pragma warning restore CA1031
	}
}

/// <summary>
/// Represents the result of a file write operation.
/// </summary>
public readonly struct FileWriteResult : IEquatable<FileWriteResult>
{
	/// <summary>
	/// Gets a value indicating whether the write succeeded.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets the error message if the write failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes written.
	/// </summary>
	public int BytesWritten { get; }

	FileWriteResult (bool isSuccess, string? error, int bytesWritten)
	{
		IsSuccess = isSuccess;
		Error = error;
		BytesWritten = bytesWritten;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static FileWriteResult Success (int bytesWritten) =>
		new (true, null, bytesWritten);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static FileWriteResult Failure (string error) =>
		new (false, error, 0);

	/// <inheritdoc/>
	public bool Equals (FileWriteResult other) =>
		IsSuccess == other.IsSuccess &&
		Error == other.Error &&
		BytesWritten == other.BytesWritten;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FileWriteResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (IsSuccess, Error, BytesWritten);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (FileWriteResult left, FileWriteResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (FileWriteResult left, FileWriteResult right) =>
		!left.Equals (right);
}
