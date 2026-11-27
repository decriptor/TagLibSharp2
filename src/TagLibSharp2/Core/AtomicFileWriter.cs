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
/// before the replace. That flush-to-disk adds a disk sync on every save (on the order
/// of milliseconds). If the write fails before the replace, the target is left unchanged
/// and the temp file is removed.
/// </para>
/// <para>
/// A crash can still leave the temporary file, including a partial one if it happens
/// during the write. A later save deletes abandoned temp files in that directory whose
/// names are <c>.taglib_</c> plus exactly 32 hexadecimal digits plus <c>.tmp</c> and whose
/// last-write time is at least 24 hours old. Each directory is scanned at most once per hour,
/// and only names matching that pattern. Legacy names are not deleted, including when the
/// destination file is missing. A file system that cannot report last-write time is not swept.
/// The temp file this save is about to create is never deleted. When the path is a symlink to
/// a file, the temp file is created in the target's directory and flushed before the target
/// is replaced. A replace can also fail after the destination has already been removed. The
/// temp file is moved back only when that destination existed before the replace and is missing
/// afterward; otherwise the temp file is deleted. If the move back fails, the temp file is
/// renamed to the destination file name plus <c>.taglib-recovered</c> and that path is reported.
/// When that recovered name already exists, the temp is moved to another name that is not a
/// sweep name and that path is reported. If that move also fails, the original temp path is reported.
/// </para>
/// </remarks>
public static class AtomicFileWriter
{
	const string TempFilePrefix = ".taglib_";
	const string TempFileSuffix = ".tmp";
	const string TempSearchPattern = ".taglib_*.tmp";
	const string RecoveredSuffix = ".taglib-recovered";
	const int TempGuidHexLength = 32;
	static readonly TimeSpan AbandonedTempFileAge = TimeSpan.FromHours (24);
	static readonly TimeSpan SweepInterval = TimeSpan.FromHours (1);
	static readonly StringComparer SweepPathComparer = CreateSweepPathComparer ();
	static readonly object SweepGate = new ();
	static readonly SweepClock ProcessSweepClock = new ();
	static readonly AsyncLocal<SweepClock?> SweepClockOverride = new ();

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

		string? tempPath = null;
		var destinationExisted = false;

		try {
			tempPath = GetTempPath (path, fileSystem);
			// The sweep sits inside this try so a listing fault becomes Failure instead of escaping.
			var sweepError = SweepOrFailure (path, tempPath, fileSystem);
			if (sweepError is not null)
				return FileWriteResult.Failure (sweepError);

			// Write to temp file
			fileSystem.WriteAllBytes (tempPath, data);

			// Atomically replace the target file
			destinationExisted = fileSystem.FileExists (path);
			fileSystem.Move (tempPath, path);

			return FileWriteResult.Success (data.Length);
		} catch (IOException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"I/O error writing file: {ex.Message}",
				destinationExisted);
		} catch (UnauthorizedAccessException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"Access denied: {ex.Message}",
				destinationExisted);
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

		string? tempPath = null;
		var destinationExisted = false;

		try {
			tempPath = GetTempPath (path, fileSystem);
			// The sweep sits inside this try so a listing fault becomes Failure instead of escaping.
			// It runs before the write, and a directory already swept this hour is not scanned again.
			var sweepError = SweepOrFailure (path, tempPath, fileSystem);
			if (sweepError is not null)
				return FileWriteResult.Failure (sweepError);

			// Write to temp file
			await fileSystem.WriteAllBytesAsync (tempPath, data, cancellationToken).ConfigureAwait (false);

			// Check for cancellation before the rename
			cancellationToken.ThrowIfCancellationRequested ();

			// Atomically replace the target file
			destinationExisted = fileSystem.FileExists (path);
			fileSystem.Move (tempPath, path);

			return FileWriteResult.Success (data.Length);
		} catch (OperationCanceledException) {
			if (tempPath is not null)
				CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ("Operation was cancelled");
		} catch (IOException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"I/O error writing file: {ex.Message}",
				destinationExisted);
		} catch (UnauthorizedAccessException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				data.Length,
				$"Access denied: {ex.Message}",
				destinationExisted);
		}
	}

	/// <summary>
	/// Writes a file atomically from a callback. The callback must finish before the temp file
	/// replaces the destination. A <see cref="FileStream"/> is flushed to disk first.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">The file system to use. Uses the default when null.</param>
	/// <param name="write">Writes the temp file. The stream is disposed after it returns.</param>
	/// <returns>A result indicating success or failure.</returns>
	internal static FileWriteResult WriteStream (string path, IFileSystem? fileSystem, Action<Stream> write)
	{
		fileSystem ??= DefaultFileSystem.Instance;

		string? tempPath = null;
		var destinationExisted = false;
		long bytesWritten = 0;

		try {
			tempPath = GetTempPath (path, fileSystem);
			var sweepError = SweepOrFailure (path, tempPath, fileSystem);
			if (sweepError is not null)
				return FileWriteResult.Failure (sweepError);

			using (var stream = fileSystem.Create (tempPath)) {
				write (stream);
				FlushWritten (stream);
				if (stream.CanSeek)
					bytesWritten = stream.Length;
			}

			destinationExisted = fileSystem.FileExists (path);
			fileSystem.Move (tempPath, path);
			return FileWriteResult.Success (ReportedBytes (bytesWritten));
		} catch (OperationCanceledException) {
			if (tempPath is not null)
				CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure ("Operation was cancelled");
		} catch (EndOfStreamException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				ReportedBytes (bytesWritten),
				ex.Message,
				destinationExisted);
		} catch (IOException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				ReportedBytes (bytesWritten),
				$"I/O error writing file: {ex.Message}",
				destinationExisted);
		} catch (UnauthorizedAccessException ex) {
			return FailWrite (
				tempPath,
				path,
				fileSystem,
				ReportedBytes (bytesWritten),
				$"Access denied: {ex.Message}",
				destinationExisted);
		}
	}

	static void FlushWritten (Stream stream)
	{
		if (stream is FileStream file)
			file.Flush (flushToDisk: true);
		else
			stream.Flush ();
	}

	static int ReportedBytes (long bytes)
	{
		if (bytes <= 0)
			return 0;
		if (bytes > int.MaxValue)
			return int.MaxValue;
		return (int)bytes;
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
			keptTempError = DescribeKeptTemp (tempPath, destinationPath, fileSystem, ex.Message);
			return false;
		} catch (UnauthorizedAccessException ex) {
			keptTempError = DescribeKeptTemp (tempPath, destinationPath, fileSystem, ex.Message);
			return false;
		}
	}

	static string DescribeKeptTemp (
		string tempPath,
		string destinationPath,
		IFileSystem fileSystem,
		string exceptionMessage)
	{
		var reported = fileSystem.FileExists (tempPath)
			? MoveKeptTempOffSweepName (tempPath, destinationPath, fileSystem)
			: tempPath;
		return $"Temporary file kept at '{reported}': {exceptionMessage}";
	}

	// Moves the temp off a sweep name. An existing recovered file is left unchanged.
	static string MoveKeptTempOffSweepName (string tempPath, string destinationPath, IFileSystem fileSystem)
	{
		var recoveredPath = GetRecoveredPath (destinationPath, fileSystem);
		if (SamePath (tempPath, recoveredPath))
			return tempPath;

		if (!fileSystem.FileExists (recoveredPath)) {
			// A failed move leaves the new bytes in the temp. Do not report recoveredPath.
			if (TryMoveKeptTemp (fileSystem, tempPath, recoveredPath))
				return recoveredPath;
			return tempPath;
		}

		var sparePath = GetSpareRecoveredPath (destinationPath, fileSystem);
		if (TryMoveKeptTemp (fileSystem, tempPath, sparePath))
			return sparePath;

		return tempPath;
	}

	static bool TryMoveKeptTemp (IFileSystem fileSystem, string sourcePath, string destinationPath)
	{
		try {
			fileSystem.Move (sourcePath, destinationPath);
			return true;
		} catch (IOException) {
			return false;
		} catch (UnauthorizedAccessException) {
			return false;
		}
	}

	static string GetRecoveredPath (string destinationPath, IFileSystem fileSystem)
	{
		var recoveredName = fileSystem.GetFileName (destinationPath) + RecoveredSuffix;
		return PathInDestinationDirectory (destinationPath, fileSystem, recoveredName);
	}

	static string GetSpareRecoveredPath (string destinationPath, IFileSystem fileSystem)
	{
		// Not a sweep name: sweep names are ".taglib_" plus 32 hex digits plus ".tmp".
		var spareName = fileSystem.GetFileName (destinationPath)
			+ RecoveredSuffix
			+ "."
			+ Guid.NewGuid ().ToString ("N");
		return PathInDestinationDirectory (destinationPath, fileSystem, spareName);
	}

	static string PathInDestinationDirectory (string destinationPath, IFileSystem fileSystem, string fileName)
	{
		var directory = fileSystem.GetDirectoryName (destinationPath);
		if (string.IsNullOrEmpty (directory))
			return fileName;

		return fileSystem.CombinePath (directory!, fileName);
	}

	static FileWriteResult FinishAfterFailure (
		string tempPath,
		string destinationPath,
		IFileSystem fileSystem,
		int bytesWritten,
		string message,
		bool destinationExisted)
	{
		// A failure before Move leaves destinationExisted false. A destination that did not
		// exist has no original to restore, so a failed publish must not leave the temp behind.
		if (!destinationExisted) {
			CleanupTempFile (tempPath, fileSystem);
			return FileWriteResult.Failure (message);
		}

		if (TryRecoverTempFile (tempPath, destinationPath, fileSystem, out var keptTempError))
			return FileWriteResult.Success (bytesWritten);

		if (keptTempError is not null)
			return FileWriteResult.Failure (keptTempError);

		return FileWriteResult.Failure (message);
	}

	static FileWriteResult FailWrite (
		string? tempPath,
		string destinationPath,
		IFileSystem fileSystem,
		int bytesWritten,
		string message,
		bool destinationExisted)
	{
		if (tempPath is null)
			return FileWriteResult.Failure (message);

		return FinishAfterFailure (
			tempPath,
			destinationPath,
			fileSystem,
			bytesWritten,
			message,
			destinationExisted);
	}

	static string GetTempPath (string targetPath, IFileSystem fileSystem)
	{
		var directory = GetTempDirectory (targetPath, fileSystem);
		// Fixed short name. Including the destination name overflows the 255-unit component limit.
		var tempFileName = $"{TempFilePrefix}{Guid.NewGuid ():N}{TempFileSuffix}";
		return fileSystem.CombinePath (directory, tempFileName);
	}

	static string GetTempDirectory (string targetPath, IFileSystem fileSystem)
	{
		var original = DirectoryOrCurrent (fileSystem.GetDirectoryName (targetPath));
#if NET7_0_OR_GREATER
		if (OperatingSystem.IsWindows ())
			return original;

		try {
			var resolved = File.ResolveLinkTarget (targetPath, returnFinalTarget: true);
			// A directory link is not a file save. A missing final target is a dangling link.
			if (resolved is null || Directory.Exists (resolved.FullName) || !File.Exists (resolved.FullName))
				return original;

			var resolvedDirectory = Path.GetDirectoryName (resolved.FullName);
			return string.IsNullOrEmpty (resolvedDirectory) ? original : resolvedDirectory;
		} catch (IOException) {
			return original;
		} catch (UnauthorizedAccessException) {
			return original;
		} catch (ArgumentException) {
			return original;
		}
#else
		return original;
#endif
	}

	static string DirectoryOrCurrent (string? directory) =>
		// netstandard2.0's nullable annotations do not narrow IsNullOrEmpty.
		string.IsNullOrEmpty (directory) ? "." : directory!;

	static string? SweepOrFailure (string destinationPath, string reservedTempPath, IFileSystem fileSystem)
	{
		try {
			DeleteAbandonedTempFiles (destinationPath, reservedTempPath, fileSystem);
			return null;
		} catch (OperationCanceledException) {
			throw;
		}
#pragma warning disable CA1031 // A sweep fault must be FileWriteResult.Failure, not an exception for the caller.
		catch (Exception ex) {
			return $"I/O error writing file: {ex.Message}";
		}
#pragma warning restore CA1031
	}

	/// <summary>
	/// Points the sweep clock at <paramref name="utcNow"/> for the current async context.
	/// </summary>
	/// <param name="utcNow">The clock the next sweeps read.</param>
	/// <param name="interval">How long a directory is left alone after a scan. Production uses one hour.</param>
	/// <returns>A scope that restores the previous clock.</returns>
	internal static IDisposable OverrideSweepClockForTests (Func<DateTime> utcNow, TimeSpan interval)
	{
		var previous = SweepClockOverride.Value;
		SweepClockOverride.Value = new SweepClock { UtcNow = utcNow, Interval = interval };
		return new RestoreSweepClock (previous);
	}

	static void DeleteAbandonedTempFiles (string destinationPath, string reservedTempPath, IFileSystem fileSystem)
	{
		// Per-file delete errors are ignored. Listing errors propagate and become Failure.
		if (fileSystem is not IFileSystemTimestamps timestamps)
			return;

		var directory = DirectoryOrCurrent (fileSystem.GetDirectoryName (reservedTempPath));
		if (!ShouldSweep (directory))
			return;

		var listed = timestamps.EnumerateFiles (directory, TempSearchPattern);
		var candidates = listed is null ? [] : listed.ToArray ();
		var destinationName = fileSystem.GetFileName (destinationPath);
		var reservedName = fileSystem.GetFileName (reservedTempPath);
		var staleBefore = CurrentUtcNow () - AbandonedTempFileAge;
		foreach (var candidate in candidates) {
			try {
				if (!IsAbandonedTemp (
					candidate,
					destinationPath,
					destinationName,
					reservedTempPath,
					reservedName,
					staleBefore,
					fileSystem,
					timestamps))
					continue;
				fileSystem.Delete (candidate);
			} catch (IOException) {
			} catch (UnauthorizedAccessException) {
			} catch (ArgumentException) {
			}
		}
	}

	static bool ShouldSweep (string directory)
	{
		var key = SweepKey (directory);
		var clock = SweepClockOverride.Value ?? ProcessSweepClock;
		var now = clock.UtcNow ();
		lock (SweepGate) {
			if (clock.Last.TryGetValue (key, out var last) && now - last < clock.Interval)
				return false;

			clock.Last[key] = now;
			return true;
		}
	}

	static DateTime CurrentUtcNow () =>
		(SweepClockOverride.Value ?? ProcessSweepClock).UtcNow ();

	static string SweepKey (string directory)
	{
		try {
			return Path.GetFullPath (directory);
		} catch (ArgumentException) {
			return directory;
		} catch (NotSupportedException) {
			return directory;
		} catch (IOException) {
			return directory;
		}
	}

	static StringComparer CreateSweepPathComparer ()
	{
#if NET7_0_OR_GREATER
		return OperatingSystem.IsWindows () ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
#else
		return Path.DirectorySeparatorChar == '\\'
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;
#endif
	}

	static bool IsAbandonedTemp (
		string candidate,
		string destinationPath,
		string destinationName,
		string reservedTempPath,
		string reservedName,
		DateTime staleBefore,
		IFileSystem fileSystem,
		IFileSystemTimestamps timestamps)
	{
		if (string.Equals (candidate, destinationPath, StringComparison.Ordinal)
			|| string.Equals (candidate, reservedTempPath, StringComparison.Ordinal))
			return false;

		var name = fileSystem.GetFileName (candidate);
		if (string.IsNullOrEmpty (name)
			|| string.Equals (name, destinationName, StringComparison.Ordinal)
			|| string.Equals (name, reservedName, StringComparison.Ordinal))
			return false;
		if (!IsSweepTempName (name))
			return false;

		// A legacy temp was ".taglib_" plus the destination file name plus ".tmp".
		// Never delete that name when the destination file is missing; it may be the only copy.
		var legacyName = TempFilePrefix + destinationName + TempFileSuffix;
		if (string.Equals (name, legacyName, StringComparison.Ordinal)
			&& !fileSystem.FileExists (destinationPath))
			return false;

		var lastWrite = timestamps.GetLastWriteTimeUtc (candidate);
		return lastWrite is not null && lastWrite.Value <= staleBefore;
	}

	static bool IsSweepTempName (string name)
	{
		if (name.Length != TempFilePrefix.Length + TempGuidHexLength + TempFileSuffix.Length)
			return false;
		if (!name.StartsWith (TempFilePrefix, StringComparison.Ordinal)
			|| !name.EndsWith (TempFileSuffix, StringComparison.Ordinal))
			return false;

		for (var i = TempFilePrefix.Length; i < TempFilePrefix.Length + TempGuidHexLength; i++) {
			if (!IsHex (name[i]))
				return false;
		}

		return true;
	}

	static bool IsHex (char value) =>
		value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

	static bool SamePath (string left, string right)
	{
		if (string.Equals (left, right, StringComparison.Ordinal))
			return true;

		try {
			return string.Equals (Path.GetFullPath (left), Path.GetFullPath (right), StringComparison.Ordinal);
		} catch (ArgumentException) {
			return false;
		} catch (NotSupportedException) {
			return false;
		} catch (IOException) {
			return false;
		}
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

	sealed class SweepClock
	{
		public Func<DateTime> UtcNow { get; init; } = DefaultUtcNow;

		public TimeSpan Interval { get; init; } = SweepInterval;

		public Dictionary<string, DateTime> Last { get; } = new (SweepPathComparer);

		static DateTime DefaultUtcNow () => DateTime.UtcNow;
	}

	sealed class RestoreSweepClock (SweepClock? previous) : IDisposable
	{
		public void Dispose () => SweepClockOverride.Value = previous;
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
