// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Provides atomic file write operations to prevent data corruption.
/// </summary>
/// <remarks>
/// <para>
/// Data is written to a temporary file in the same directory as the target, then that
/// file replaces the target. The default file system flushes the temp file to disk
/// before the replace. If the write fails before the replace and the target is still
/// present, the target is left unchanged and the temp file is removed.
/// </para>
/// <para>
/// A crash can still leave the temporary file, including a partial one if it happens
/// during the write. A replace can also fail after the destination has already been
/// removed; the temp file is then moved onto the destination, or kept (and its path
/// reported) if that move also fails.
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
		var replaceAttempted = false;

		try {
			// Write to temp file
			fileSystem.WriteAllBytes (tempPath, data);

			// Atomically replace the target file
			replaceAttempted = true;
			fileSystem.Move (tempPath, path);

			return FileWriteResult.Success (data.Length);
		} catch (IOException ex) {
			return FinishAfterFailure (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"I/O error writing file: {ex.Message}",
				replaceAttempted);
		} catch (UnauthorizedAccessException ex) {
			return FinishAfterFailure (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"Access denied: {ex.Message}",
				replaceAttempted);
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
		var replaceAttempted = false;

		try {
			// Write to temp file
			await fileSystem.WriteAllBytesAsync (tempPath, data, cancellationToken).ConfigureAwait (false);

			// Check for cancellation before the rename
			cancellationToken.ThrowIfCancellationRequested ();

			// Atomically replace the target file
			replaceAttempted = true;
			fileSystem.Move (tempPath, path);

			return FileWriteResult.Success (data.Length);
		} catch (OperationCanceledException) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ("Operation was cancelled");
		} catch (IOException ex) {
			return FinishAfterFailure (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"I/O error writing file: {ex.Message}",
				replaceAttempted);
		} catch (UnauthorizedAccessException ex) {
			return FinishAfterFailure (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"Access denied: {ex.Message}",
				replaceAttempted);
		}
	}

	/// <summary>
	/// After a failed replace, deletes the temp file only when the destination still exists.
	/// When the destination is missing, moves the temp file onto it.
	/// </summary>
	/// <returns><c>true</c> when the temp file was moved onto the missing destination.</returns>
	internal static bool TryRecoverTempFile (
		string tempPath,
		string destinationPath,
		IFileSystem fileSystem,
		out string? keptTempError)
	{
		keptTempError = null;
		if (fileSystem.FileExists (destinationPath)) {
			CleanupTempFile (tempPath, fileSystem);
			return false;
		}

		try {
			fileSystem.Move (tempPath, destinationPath);
			return true;
		} catch (IOException ex) {
			keptTempError = $"Temporary file kept at '{tempPath}'. {ex.Message}";
			return false;
		} catch (UnauthorizedAccessException ex) {
			keptTempError = $"Temporary file kept at '{tempPath}'. {ex.Message}";
			return false;
		}
	}

	static FileWriteResult FinishAfterFailure (
		string tempPath,
		string destinationPath,
		IFileSystem fileSystem,
		int bytesWritten,
		string message,
		bool replaceAttempted)
	{
		// A failure before Move cannot have removed the destination, so the temp file is not the only copy.
		if (!replaceAttempted) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure (message);
		}

		if (TryRecoverTempFile (tempPath, destinationPath, fileSystem, out var keptTempError))
			return FileWriteResult.Success (bytesWritten);

		if (keptTempError is not null)
			return FileWriteResult.Failure ($"{message}. {keptTempError}");

		return FileWriteResult.Failure (message);
	}

	static string GetTempPath (string targetPath, IFileSystem fileSystem)
	{
		var directory = fileSystem.GetDirectoryName (targetPath) ?? ".";
		// Fixed short name. Including the destination name overflows the 255-unit component limit.
		var tempFileName = $"{TempFilePrefix}{Guid.NewGuid ():N}{TempFileSuffix}";
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
