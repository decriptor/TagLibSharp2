// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Opens a source file for a path save and replaces the destination from a callback.
/// </summary>
internal static class PathSave
{
	/// <summary>
	/// Opens <paramref name="sourcePath"/> and records its length once.
	/// </summary>
	/// <param name="sourcePath">File to read.</param>
	/// <param name="fileSystem">File system for both paths. The default is used when null.</param>
	/// <param name="source">The open file when this returns true.</param>
	/// <param name="error">The failure when this returns false.</param>
	/// <returns>True when <paramref name="source"/> is open and seekable.</returns>
	public static bool TryOpen (
		string sourcePath,
		IFileSystem? fileSystem,
		out OpenedFile? source,
		out FileWriteResult error)
	{
		source = null;
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (!fs.FileExists (sourcePath)) {
			error = FileWriteResult.Failure ($"Failed to re-read source file: File not found: {sourcePath}");
			return false;
		}

		Stream stream;
		try {
			stream = fs.OpenRead (sourcePath);
		} catch (IOException ex) {
			error = FileWriteResult.Failure ($"Failed to re-read source file: Failed to read file: {ex.Message}");
			return false;
		} catch (UnauthorizedAccessException ex) {
			error = FileWriteResult.Failure ($"Failed to re-read source file: Access denied: {ex.Message}");
			return false;
		}

		if (!stream.CanSeek) {
			stream.Dispose ();
			error = FileWriteResult.Failure ("Failed to re-read source file: source is not seekable");
			return false;
		}

		long length;
		try {
			length = stream.Length;
		} catch (IOException ex) {
			stream.Dispose ();
			error = ReadFailure (ex);
			return false;
		} catch (UnauthorizedAccessException ex) {
			stream.Dispose ();
			error = FileWriteResult.Failure ($"Failed to re-read source file: Access denied: {ex.Message}");
			return false;
		}

		source = new OpenedFile (stream, length, fs);
		error = default;
		return true;
	}

	/// <summary>
	/// Writes <paramref name="destinationPath"/> from <paramref name="write"/> and closes
	/// <paramref name="source"/> before the destination is replaced.
	/// </summary>
	/// <param name="destinationPath">File replaced by the atomic write.</param>
	/// <param name="source">Open source. Disposed before the replace, including when <paramref name="write"/> throws. Also disposed when the destination is never opened.</param>
	/// <param name="write">Writes the destination. The source is still open for the duration of this callback.</param>
	/// <returns>The result of the atomic write.</returns>
	public static FileWriteResult Replace (string destinationPath, OpenedFile source, Action<Stream> write)
	{
		// The callback dispose runs before the destination is replaced. WriteStream does not
		// call the callback when the temp file is never created, so dispose again here.
		try {
			return AtomicFileWriter.WriteStream (destinationPath, source.FileSystem, output => {
				try {
					write (output);
				} finally {
					source.Dispose ();
				}
			});
		} finally {
			source.Dispose ();
		}
	}

	static FileWriteResult ReadFailure (IOException ex) =>
		FileWriteResult.Failure ($"Failed to re-read source file: Failed to read file: {ex.Message}");
}

/// <summary>
/// A seekable source opened for a path save. <see cref="Length"/> is the length
/// recorded when the file was opened.
/// </summary>
internal sealed class OpenedFile : IDisposable
{
	Stream? _stream;

	/// <summary>
	/// Opens a source that is already known to be seekable.
	/// </summary>
	/// <param name="stream">Open read stream.</param>
	/// <param name="length">Length recorded once at open.</param>
	/// <param name="fileSystem">File system that opened the stream and will create the destination.</param>
	public OpenedFile (Stream stream, long length, IFileSystem fileSystem)
	{
		_stream = stream;
		Length = length;
		FileSystem = fileSystem;
	}

	/// <summary>
	/// Gets the open stream.
	/// </summary>
	public Stream Stream => _stream ?? throw new ObjectDisposedException (nameof (OpenedFile));

	/// <summary>
	/// Gets the length recorded at open.
	/// </summary>
	public long Length { get; }

	/// <summary>
	/// Gets the file system used to open this file.
	/// </summary>
	public IFileSystem FileSystem { get; }

	/// <summary>
	/// Closes the stream. A second call does nothing.
	/// </summary>
	public void Dispose ()
	{
		var stream = _stream;
		if (stream is null)
			return;

		_stream = null;
		stream.Dispose ();
	}
}
