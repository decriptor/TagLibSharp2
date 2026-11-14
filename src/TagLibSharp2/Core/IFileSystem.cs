// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Abstracts file system operations for testability and flexibility.
/// </summary>
public interface IFileSystem
{
	/// <summary>
	/// Determines whether the specified file exists.
	/// </summary>
	/// <param name="path">The file path to check.</param>
	/// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
	bool FileExists (string path);

	/// <summary>
	/// Opens a file for reading.
	/// </summary>
	/// <param name="path">The file path to open.</param>
	/// <returns>A stream for reading the file.</returns>
	/// <exception cref="FileNotFoundException">The file was not found.</exception>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	Stream OpenRead (string path);

	/// <summary>
	/// Opens a file for reading and writing.
	/// </summary>
	/// <param name="path">The file path to open.</param>
	/// <returns>A stream for reading and writing the file.</returns>
	/// <exception cref="FileNotFoundException">The file was not found.</exception>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	Stream OpenReadWrite (string path);

	/// <summary>
	/// Creates or overwrites a file.
	/// </summary>
	/// <param name="path">The file path to create.</param>
	/// <returns>A stream for writing the file.</returns>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	Stream Create (string path);

	/// <summary>
	/// Reads all bytes from a file.
	/// </summary>
	/// <param name="path">The file path to read.</param>
	/// <returns>The file contents as a byte array.</returns>
	/// <exception cref="FileNotFoundException">The file was not found.</exception>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	byte[] ReadAllBytes (string path);

	/// <summary>
	/// Asynchronously reads all bytes from a file.
	/// </summary>
	/// <param name="path">The file path to read.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing the file contents as a byte array.</returns>
	/// <exception cref="FileNotFoundException">The file was not found.</exception>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes all bytes to a file, creating or overwriting it.
	/// </summary>
	/// <param name="path">The file path to write.</param>
	/// <param name="data">The bytes to write.</param>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	void WriteAllBytes (string path, ReadOnlySpan<byte> data);

	/// <summary>
	/// Asynchronously writes all bytes to a file, creating or overwriting it.
	/// </summary>
	/// <param name="path">The file path to write.</param>
	/// <param name="data">The bytes to write.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the specified file.
	/// </summary>
	/// <param name="path">The file path to delete.</param>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	void Delete (string path);

	/// <summary>
	/// Moves a file from one path to another, overwriting the destination if it exists.
	/// Replacing an existing file must be one step, or the destination must be left untouched
	/// if the replace fails. Replacing an existing file is same-volume only.
	/// </summary>
	/// <param name="sourcePath">The source file path.</param>
	/// <param name="destinationPath">The destination file path.</param>
	/// <exception cref="IOException">An I/O error occurred.</exception>
	void Move (string sourcePath, string destinationPath);

	/// <summary>
	/// Gets the directory name from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <returns>The directory name, or null if the path has no directory.</returns>
	string? GetDirectoryName (string path);

	/// <summary>
	/// Gets the file name from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <returns>The file name.</returns>
	string GetFileName (string path);

	/// <summary>
	/// Combines path components into a single path.
	/// </summary>
	/// <param name="path1">The first path component.</param>
	/// <param name="path2">The second path component.</param>
	/// <returns>The combined path.</returns>
	string CombinePath (string path1, string path2);
}
