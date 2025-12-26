// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Represents the result of reading file bytes from the file system.
/// </summary>
public readonly struct FileReadResult : IEquatable<FileReadResult>
{
	/// <summary>
	/// Gets the file data if successful, or null if failed.
	/// </summary>
	/// <remarks>
	/// This property returns the raw file bytes for parsing.
	/// The array is owned by the caller and should not be modified.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage ("Performance", "CA1819:Properties should not return arrays",
		Justification = "Result type that transfers ownership of file bytes to caller")]
	public byte[]? Data { get; }

	/// <summary>
	/// Gets a value indicating whether the read was successful.
	/// </summary>
	public bool IsSuccess => Data is not null && Error is null;

	/// <summary>
	/// Gets the error message if failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	FileReadResult (byte[]? data, string? error)
	{
		Data = data;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A successful result.</returns>
	public static FileReadResult Success (byte[] data) => new (data, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static FileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (FileReadResult other) =>
		ReferenceEquals (Data, other.Data) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Data, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (FileReadResult left, FileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (FileReadResult left, FileReadResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Provides helper methods for file I/O operations.
/// </summary>
public static class FileHelper
{
	/// <summary>
	/// Safely reads all bytes from a file with consistent error handling.
	/// </summary>
	/// <param name="path">The file path to read.</param>
	/// <param name="fileSystem">The file system to use (defaults to real file system).</param>
	/// <returns>A result containing the file data or an error message.</returns>
	public static FileReadResult SafeReadAllBytes (string path, IFileSystem? fileSystem = null)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif

		var fs = fileSystem ?? DefaultFileSystem.Instance;

		if (!fs.FileExists (path))
			return FileReadResult.Failure ($"File not found: {path}");

		try {
			var data = fs.ReadAllBytes (path);
			return FileReadResult.Success (data);
		} catch (IOException ex) {
			return FileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return FileReadResult.Failure ($"Access denied: {ex.Message}");
		}
	}
}
