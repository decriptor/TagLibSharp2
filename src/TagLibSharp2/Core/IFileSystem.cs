// Copyright (c) 2025 Stephen Shaw and contributors
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
}
