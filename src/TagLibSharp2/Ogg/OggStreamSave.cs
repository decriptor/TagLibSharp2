// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Ogg;

/// <summary>
/// Saves an Ogg file by rewriting header pages and copying audio one page at a time.
/// </summary>
internal static class OggStreamSave
{
	/// <summary>
	/// Renders <paramref name="kind"/> headers from the source file and copies the audio pages
	/// that follow them. The caller already holds any in-memory bytes for the span save overloads;
	/// this path is the one that must not load the audio.
	/// </summary>
	/// <param name="sourcePath">File whose header pages are rewritten and whose audio pages are copied.</param>
	/// <param name="destinationPath">File replaced by the atomic write.</param>
	/// <param name="fileSystem">File system used for both paths. The default file system is used when null.</param>
	/// <param name="kind">Which header pages to keep before the audio.</param>
	/// <param name="renderHeaders">Builds the replacement header pages from the header prefix. Null or empty is failure.</param>
	/// <param name="renderError">Error returned when <paramref name="renderHeaders"/> produces no pages.</param>
	/// <param name="cancellationToken">Checked after the source has been opened and the headers rendered, before the destination is replaced.</param>
	public static FileWriteResult Save (
		string sourcePath,
		string destinationPath,
		IFileSystem? fileSystem,
		OggHeaderKind kind,
		Func<byte[], byte[]?> renderHeaders,
		string renderError,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (!fs.FileExists (sourcePath))
			return FileWriteResult.Failure ($"Failed to re-read source file: File not found: {sourcePath}");

		Stream source;
		try {
			source = fs.OpenRead (sourcePath);
		} catch (IOException ex) {
			return FileWriteResult.Failure ($"Failed to re-read source file: Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return FileWriteResult.Failure ($"Failed to re-read source file: Access denied: {ex.Message}");
		}

		var closed = false;
		void Close ()
		{
			if (closed)
				return;
			closed = true;
			source.Dispose ();
		}

		try {
			if (!source.CanSeek)
				return FileWriteResult.Failure ("Failed to re-read source file: source is not seekable");

			byte[] headers;
			try {
				headers = OggWindow.ReadHeaders (source, kind);
			} catch (IOException ex) {
				return FileWriteResult.Failure ($"Failed to re-read source file: Failed to read file: {ex.Message}");
			} catch (UnauthorizedAccessException ex) {
				return FileWriteResult.Failure ($"Failed to re-read source file: Access denied: {ex.Message}");
			}

			var rendered = renderHeaders (headers);
			if (rendered is null || rendered.Length == 0 || !TryDescribe (rendered, out var serial, out var startSequence))
				return FileWriteResult.Failure (renderError);

			if (cancellationToken.IsCancellationRequested)
				return FileWriteResult.Failure ("Operation was cancelled");

			// Close the source before the replace. Windows will not replace a file that is still open.
			return AtomicFileWriter.WriteStream (destinationPath, fs, output => {
				try {
					WritePages (output, rendered);
					OggPageHelper.CopyRenumberedPages (source, output, serial, startSequence);
				} finally {
					Close ();
				}
			});
		} finally {
			Close ();
		}
	}

	static bool TryDescribe (byte[] rendered, out uint serial, out uint pageCount)
	{
		serial = 0;
		pageCount = 0;
		var offset = 0;
		while (offset < rendered.Length) {
			if (!TryPageLength (rendered, offset, out var length))
				return false;
			if (pageCount == 0) {
				serial = rendered[offset + 14]
					| ((uint)rendered[offset + 15] << 8)
					| ((uint)rendered[offset + 16] << 16)
					| ((uint)rendered[offset + 17] << 24);
			}

			pageCount++;
			offset += length;
		}

		return pageCount > 0;
	}

	static void WritePages (Stream destination, byte[] rendered)
	{
		var offset = 0;
		while (offset < rendered.Length) {
			if (!TryPageLength (rendered, offset, out var length))
				throw new IOException ("Rendered Ogg header is not a page");
			destination.Write (rendered, offset, length);
			offset += length;
		}
	}

	static bool TryPageLength (byte[] data, int offset, out int length)
	{
		length = 0;
		if ((uint)offset > (uint)data.Length || data.Length - offset < 27)
			return false;
		if (data[offset] != (byte)'O' || data[offset + 1] != (byte)'g' || data[offset + 2] != (byte)'g' || data[offset + 3] != (byte)'S')
			return false;

		var segmentCount = data[offset + 26];
		if (data.Length - offset < 27 + segmentCount)
			return false;

		var body = 0;
		for (var i = 0; i < segmentCount; i++)
			body += data[offset + 27 + i];
		length = 27 + segmentCount + body;
		return offset + length <= data.Length;
	}
}
