// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Image;

/// <summary>
/// A still image: JPEG, PNG, GIF, BMP, PCX, Netpbm, TIFF, DNG, or SVG. Open one with <see cref="Read(ReadOnlySpan{byte})"/>,
/// <see cref="ReadFromFile(string, IFileSystem?)"/>, <see cref="ReadFromFileAsync(string, IFileSystem?, CancellationToken)"/>,
/// or <see cref="MediaFile"/>.
/// </summary>
/// <remarks>
/// PNG text keywords, the GIF comment, and the JPEG COM marker can be edited
/// through <see cref="Tag"/>. <see cref="Render"/> copies every other byte,
/// including the pixels. BMP, PCX, Netpbm, TIFF, DNG, and SVG have no text
/// block here yet, so <see cref="Tag"/> is null and <see cref="Render"/> is
/// the file that was read.
/// </remarks>
public sealed class StillImageFile : IMediaFile
{
	readonly byte[] _data;
	readonly ImageTag? _tag;
	IFileSystem? _fileSystem;

	internal StillImageFile (byte[] data, MediaFormat format, ImageProperties? properties, ImageTag? tag)
	{
		_data = data;
		_tag = tag;
		Format = format;
		ImageProperties = properties is { IsValid: true } ? properties : null;
	}

	/// <summary>Returns true when the data starts with the signature of a still-image format read here.</summary>
	/// <param name="data">The start of the file.</param>
	/// <remarks>Reads at most the first 64 KiB of markup for SVG and the IFD chain for TIFF; nothing is copied.</remarks>
	public static bool IsValidFormat (ReadOnlySpan<byte> data) => ImageHeader.TryDetect (data, out _);

	/// <summary>Reads a still image from its bytes. The bytes are copied once.</summary>
	/// <param name="data">The whole file.</param>
	/// <returns>The parsed file, or a failure naming why it could not be read.</returns>
	/// <remarks>A TIFF without the DNG version tag reads as TIFF; <see cref="ReadFromFile(string, IFileSystem?)"/> also treats a <c>.dng</c> name as DNG.</remarks>
	public static StillImageFileReadResult Read (ReadOnlySpan<byte> data) => Open (data.ToArray (), null);

	/// <summary>Reads a still image from its bytes without an error string.</summary>
	/// <param name="data">The whole file.</param>
	/// <param name="file">The parsed file when the read succeeds.</param>
	/// <returns>True when the data is a still image read here.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out StillImageFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>Reads a still image from disk. The file is read into memory once.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The parsed file, or a failure.</returns>
	public static StillImageFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (OpenFailure (path, fs) is { } missing)
			return missing;

		try {
			byte[] bytes;
			using (var stream = fs.OpenRead (path))
				bytes = StreamBytes.ReadToEnd (stream);
			return Remember (Open (bytes, path), path, fileSystem);
		} catch (IOException ex) {
			return StillImageFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return StillImageFileReadResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	/// <summary>Reads a still image from disk asynchronously. The file is read into memory once.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The parsed file, or a failure.</returns>
	public static async Task<StillImageFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		if (OpenFailure (path, fs) is { } missing)
			return missing;

		try {
			byte[] bytes;
			using (var stream = fs.OpenRead (path))
				bytes = await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false);
			return Remember (Open (bytes, path), path, fileSystem);
		} catch (IOException ex) {
			return StillImageFileReadResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return StillImageFileReadResult.Failure ($"Access denied: {ex.Message}");
		} catch (OperationCanceledException) {
			return StillImageFileReadResult.Failure ("Operation was cancelled");
		}
	}

	static StillImageFileReadResult? OpenFailure (string path, IFileSystem fileSystem)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		return fileSystem.FileExists (path) ? null : StillImageFileReadResult.Failure ($"File not found: {path}");
	}

	static StillImageFileReadResult Remember (StillImageFileReadResult result, string path, IFileSystem? fileSystem)
	{
		result.File?.SetSource (path, fileSystem);
		return result;
	}

	static StillImageFileReadResult Open (byte[] data, string? path)
	{
		if (!ImageHeader.TryDetect (data, out var format))
			return StillImageFileReadResult.Failure ("Not a still image: no JPEG, PNG, GIF, BMP, PCX, Netpbm, TIFF, or SVG signature");
		if (format == MediaFormat.Tiff && HasDngName (path))
			format = MediaFormat.Dng;
		return Open (data, format);
	}

	internal static bool HasDngName (string? path) =>
		!string.IsNullOrEmpty (path) && Path.GetExtension (path).Equals (".dng", StringComparison.OrdinalIgnoreCase);

	/// <summary>Opens <paramref name="data"/> as <paramref name="format"/>. The array is kept, not copied.</summary>
	internal static StillImageFileReadResult Open (byte[] data, MediaFormat format)
	{
		if (!ImageHeader.TryRead (data, format, out var properties))
			return StillImageFileReadResult.Failure ($"Not a {format} image");
		var tag = ImageRewrite.CreateTag (data, format);
		return StillImageFileReadResult.Success (new StillImageFile (data, format, properties, tag));
	}

	/// <inheritdoc/>
	public Tag? Tag => _tag;

	/// <inheritdoc/>
	public IMediaProperties? AudioProperties => null;

	/// <inheritdoc/>
	public VideoProperties? VideoProperties => null;

	/// <inheritdoc/>
	public ImageProperties? ImageProperties { get; }

	/// <inheritdoc/>
	public MediaTypes MediaTypes => MediaTypes.Image;

	/// <inheritdoc/>
	public string? SourcePath { get; private set; }

	/// <inheritdoc/>
	public MediaFormat Format { get; }

	/// <summary>
	/// The file bytes after any text edit. Pixel data is not rewritten.
	/// A container that cannot be updated safely is returned unchanged.
	/// <see cref="SaveToFile(string, IFileSystem?)"/> fails instead of writing that unchanged file, with the reason.
	/// </summary>
	public ReadOnlyMemory<byte> Render ()
	{
		if (!TryPrepare (out var bytes, out _))
			return _data;
		return bytes;
	}

	/// <summary>Writes the edited file to <paramref name="path"/>.</summary>
	/// <remarks>Fails, and does not write, when a text edit cannot be spliced into the container.</remarks>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		if (!TryPrepare (out var bytes, out var error))
			return FileWriteResult.Failure (error!);
		var fs = fileSystem ?? _fileSystem ?? DefaultFileSystem.Instance;
		return AtomicFileWriter.Write (path, bytes.Span, fs);
	}

	/// <summary>Writes <see cref="Render"/> back to <see cref="SourcePath"/>.</summary>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead.");
		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>Writes <see cref="Render"/> to <paramref name="path"/>.</summary>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		if (!TryPrepare (out var bytes, out var error))
			return Task.FromResult (FileWriteResult.Failure (error!));
		var fs = fileSystem ?? _fileSystem ?? DefaultFileSystem.Instance;
		return AtomicFileWriter.WriteAsync (path, bytes, fs, cancellationToken);
	}

	bool TryPrepare (out ReadOnlyMemory<byte> bytes, out string? error)
	{
		error = null;
		if (_tag is null || !_tag.AffectsFile) {
			bytes = _data;
			return true;
		}

		if (ImageRewrite.Rewrite (_data, _tag, out error) is byte[] rewritten) {
			bytes = rewritten;
			return true;
		}

		bytes = default;
		error ??= "The image container could not be updated.";
		return false;
	}

	/// <summary>Writes <see cref="Render"/> back to <see cref="SourcePath"/>.</summary>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead."));
		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <inheritdoc/>
	public void Dispose ()
	{
	}

	internal void SetSource (string path, IFileSystem? fileSystem)
	{
		SourcePath = path;
		_fileSystem = fileSystem;
	}
}

/// <summary>
/// The result of reading a still image.
/// </summary>
public readonly struct StillImageFileReadResult : IEquatable<StillImageFileReadResult>
{
	StillImageFileReadResult (StillImageFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>Gets the parsed file, or null when the read failed.</summary>
	public StillImageFile? File { get; }

	/// <summary>Gets the error message when the read failed.</summary>
	public string? Error { get; }

	/// <summary>Gets whether the read succeeded.</summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>Creates a successful result.</summary>
	/// <param name="file">The parsed file.</param>
	/// <returns>The result.</returns>
	public static StillImageFileReadResult Success (StillImageFile file) => new (file, null);

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">The error message.</param>
	/// <returns>The result.</returns>
	public static StillImageFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc />
	public bool Equals (StillImageFileReadResult other) => ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc />
	public override bool Equals (object? obj) => obj is StillImageFileReadResult other && Equals (other);

	/// <inheritdoc />
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>Determines whether two results are equal.</summary>
	/// <param name="left">The first result.</param>
	/// <param name="right">The second result.</param>
	/// <returns>True when both results hold the same file and error.</returns>
	public static bool operator == (StillImageFileReadResult left, StillImageFileReadResult right) => left.Equals (right);

	/// <summary>Determines whether two results differ.</summary>
	/// <param name="left">The first result.</param>
	/// <param name="right">The second result.</param>
	/// <returns>True when the results differ.</returns>
	public static bool operator != (StillImageFileReadResult left, StillImageFileReadResult right) => !left.Equals (right);
}
