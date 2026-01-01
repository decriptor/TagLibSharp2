// DSF (DSD Stream File) implementation
// Container for DSD audio with ID3v2 metadata at the end
//
// File structure:
// - DSD Chunk (28 bytes): Magic + file size + metadata offset
// - fmt Chunk (52 bytes): Audio format parameters
// - data Chunk: Audio data
// - Optional ID3v2 tag at end (pointed to by metadata offset)
//
// Expert input:
// - DSF Expert: Little-endian throughout, ID3v2 at end (unlike FLAC)
// - Audiophile: Preserve full DSD resolution, support DSD64/128/256/512
// - C# Expert: Use Span<T> for parsing, avoid copying audio data

using System;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable CA2231 // Overload operator equals on overriding value type Equals
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CA1513 // Use ObjectDisposedException.ThrowIf (not available in netstandard)

namespace TagLibSharp2.Dsf;

/// <summary>
/// Audio properties specific to DSF (DSD) files.
/// </summary>
public sealed class DsfAudioProperties
{
	/// <summary>Gets the audio duration.</summary>
	public TimeSpan Duration { get; }

	/// <summary>Gets the sample rate in Hz (e.g., 2822400 for DSD64).</summary>
	public int SampleRate { get; }

	/// <summary>Gets the number of audio channels.</summary>
	public int Channels { get; }

	/// <summary>Gets the bits per sample (always 1 for DSD).</summary>
	public int BitsPerSample { get; }

	/// <summary>Gets the DSD rate classification.</summary>
	public DsfSampleRate DsdRate { get; }

	/// <summary>Gets the channel type (Mono, Stereo, Surround).</summary>
	public DsfChannelType ChannelType { get; }

	/// <summary>Gets the block size per channel in bytes.</summary>
	public int BlockSizePerChannel { get; }

	internal DsfAudioProperties (DsfFmtChunk fmtChunk)
	{
		// Handle potential overflow in Duration calculation gracefully
		try {
			Duration = fmtChunk.Duration;
		} catch (OverflowException) {
			Duration = TimeSpan.MaxValue;
		}
		SampleRate = (int)fmtChunk.SampleRate;
		Channels = (int)fmtChunk.ChannelCount;
		BitsPerSample = (int)fmtChunk.BitsPerSample;
		DsdRate = fmtChunk.DsdRate;
		ChannelType = fmtChunk.ChannelType;
		BlockSizePerChannel = (int)fmtChunk.BlockSizePerChannel;
	}
}

/// <summary>
/// Represents the result of parsing a DSF file.
/// </summary>
public readonly struct DsfFileParseResult : IEquatable<DsfFileParseResult>
{
	public DsfFile? File { get; }
	public string? Error { get; }
	public bool IsSuccess => File is not null && Error is null;

	private DsfFileParseResult (DsfFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	public static DsfFileParseResult Success (DsfFile file) => new (file, null);
	public static DsfFileParseResult Failure (string error) => new (null, error);

	public bool Equals (DsfFileParseResult other) =>
		Equals (File, other.File) && Error == other.Error;

	public override bool Equals (object? obj) =>
		obj is DsfFileParseResult other && Equals (other);

	public override int GetHashCode () => HashCode.Combine (File, Error);
}

/// <summary>
/// Represents a DSF (DSD Stream File) audio file.
/// </summary>
public sealed class DsfFile : IDisposable
{
	private byte[]? _originalData;
	private bool _disposed;
	private readonly DsfDsdChunk _dsdChunk;
	private readonly DsfFmtChunk _fmtChunk;
	private readonly DsfDataChunk _dataChunk;
	private readonly int _audioDataOffset;
	private IFileSystem? _sourceFileSystem;

	/// <summary>
	/// Gets the path to the source file, if available.
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag containing metadata.
	/// May be null if the file has no metadata.
	/// </summary>
	public Id3v2Tag? Id3v2Tag { get; set; }

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v2 tag.
	/// </summary>
	public bool HasId3v2Tag => Id3v2Tag is not null;

	/// <summary>
	/// Gets the sample rate in Hz.
	/// </summary>
	public int SampleRate => (int)_fmtChunk.SampleRate;

	/// <summary>
	/// Gets the number of audio channels.
	/// </summary>
	public int Channels => (int)_fmtChunk.ChannelCount;

	/// <summary>
	/// Gets the bits per sample (always 1 for DSD).
	/// </summary>
	public int BitsPerSample => (int)_fmtChunk.BitsPerSample;

	/// <summary>
	/// Gets the total number of samples per channel.
	/// </summary>
	public ulong SampleCount => _fmtChunk.SampleCount;

	/// <summary>
	/// Gets the audio duration.
	/// </summary>
	public TimeSpan Duration => _fmtChunk.Duration;

	/// <summary>
	/// Gets the DSD rate classification.
	/// </summary>
	public DsfSampleRate DsdRate => _fmtChunk.DsdRate;

	/// <summary>
	/// Gets the channel type.
	/// </summary>
	public DsfChannelType ChannelType => _fmtChunk.ChannelType;

	/// <summary>
	/// Gets the block size per channel.
	/// </summary>
	public int BlockSizePerChannel => (int)_fmtChunk.BlockSizePerChannel;

	/// <summary>
	/// Gets the audio properties for this file.
	/// </summary>
	public DsfAudioProperties? Properties { get; private set; }

	private DsfFile (
		byte[] originalData,
		DsfDsdChunk dsdChunk,
		DsfFmtChunk fmtChunk,
		DsfDataChunk dataChunk,
		int audioDataOffset,
		Id3v2Tag? tag)
	{
		_originalData = originalData;
		_dsdChunk = dsdChunk;
		_fmtChunk = fmtChunk;
		_dataChunk = dataChunk;
		_audioDataOffset = audioDataOffset;
		Id3v2Tag = tag;
	}

	/// <summary>
	/// Parses a DSF file from binary data.
	/// </summary>
	public static DsfFileParseResult Parse (ReadOnlySpan<byte> data)
	{
		const int minSize = DsfDsdChunk.Size + DsfFmtChunk.Size + DsfDataChunk.HeaderSize;

		if (data.Length < minSize) {
			return DsfFileParseResult.Failure (
				$"Data too short for DSF file: {data.Length} bytes, need at least {minSize}");
		}

		// Parse DSD chunk
		var dsdResult = DsfDsdChunk.Parse (data);
		if (!dsdResult.IsSuccess) {
			return DsfFileParseResult.Failure ($"Failed to parse DSD chunk: {dsdResult.Error}");
		}
		var dsdChunk = dsdResult.Chunk!;

		// Parse fmt chunk
		var fmtOffset = DsfDsdChunk.Size;
		var fmtResult = DsfFmtChunk.Parse (data[fmtOffset..]);
		if (!fmtResult.IsSuccess) {
			return DsfFileParseResult.Failure ($"Failed to parse format chunk: {fmtResult.Error}");
		}
		var fmtChunk = fmtResult.Chunk!;

		// Parse data chunk header
		var dataOffset = fmtOffset + DsfFmtChunk.Size;
		var dataResult = DsfDataChunk.Parse (data[dataOffset..]);
		if (!dataResult.IsSuccess) {
			return DsfFileParseResult.Failure ($"Failed to parse data chunk: {dataResult.Error}");
		}
		var dataChunk = dataResult.Chunk!;

		var audioDataOffset = dataOffset + DsfDataChunk.HeaderSize;

		// Validate data chunk size against available data
		var audioDataSize = dataChunk.AudioDataSize;
		var dataEnd = (ulong)audioDataOffset + audioDataSize;

		if (dsdChunk.HasMetadata) {
			// Data chunk should not extend beyond metadata offset
			if (dataEnd > dsdChunk.MetadataOffset) {
				return DsfFileParseResult.Failure (
					$"Data chunk extends beyond metadata offset: data ends at {dataEnd}, metadata at {dsdChunk.MetadataOffset}");
			}
		} else {
			// Data chunk should not exceed file size
			if (dataEnd > (ulong)data.Length) {
				return DsfFileParseResult.Failure (
					$"Data chunk size exceeds available data: claims {audioDataSize} bytes, but only {data.Length - audioDataOffset} available");
			}
		}

		// Validate metadata offset if present
		Id3v2Tag? tag = null;
		if (dsdChunk.HasMetadata) {
			if (dsdChunk.MetadataOffset > (ulong)data.Length) {
				return DsfFileParseResult.Failure (
					$"Metadata offset {dsdChunk.MetadataOffset} beyond file size {data.Length}");
			}

			// Parse ID3v2 tag at the end
			var metadataStart = (int)dsdChunk.MetadataOffset;
			if (metadataStart < data.Length) {
				var id3Result = Id3v2Tag.Read (data[metadataStart..]);
				if (id3Result.IsSuccess) {
					tag = id3Result.Tag;
				}
				// If ID3v2 parsing fails, we continue without metadata
				// rather than failing the entire file
			}
		}

		// Make a copy of the data for later rendering
		var originalData = data.ToArray ();

		var file = new DsfFile (originalData, dsdChunk, fmtChunk, dataChunk, audioDataOffset, tag);
		file.Properties = new DsfAudioProperties (fmtChunk);
		return DsfFileParseResult.Success (file);
	}

	/// <summary>
	/// Reads a DSF file from the specified path.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	public static DsfFileParseResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess) {
			return DsfFileParseResult.Failure ($"Failed to read file: {readResult.Error}");
		}

		var parseResult = Parse (readResult.Data!);
		if (parseResult.IsSuccess) {
			parseResult.File!.SourcePath = path;
			parseResult.File._sourceFileSystem = fs;
		}
		return parseResult;
	}

	/// <summary>
	/// Asynchronously reads a DSF file from the specified path.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result with the parsed file or an error.</returns>
	public static async Task<DsfFileParseResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess) {
			return DsfFileParseResult.Failure ($"Failed to read file: {readResult.Error}");
		}

		var parseResult = Parse (readResult.Data!);
		if (parseResult.IsSuccess) {
			parseResult.File!.SourcePath = path;
			parseResult.File._sourceFileSystem = fs;
		}
		return parseResult;
	}

	/// <summary>
	/// Ensures an ID3v2 tag exists, creating one if necessary.
	/// </summary>
	/// <returns>The existing or newly created ID3v2 tag.</returns>
	public Id3v2Tag EnsureId3v2Tag ()
	{
		Id3v2Tag ??= new Id3v2Tag ();
		return Id3v2Tag;
	}

	/// <summary>
	/// Renders the DSF file to binary data.
	/// </summary>
	/// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
	public BinaryData Render ()
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		// Calculate audio data size
		var audioDataEnd = _dsdChunk.HasMetadata
			? (int)_dsdChunk.MetadataOffset
			: _originalData!.Length;
		var audioDataSize = audioDataEnd - _audioDataOffset;

		// Render ID3v2 tag if present
		byte[] id3Data = Array.Empty<byte> ();
		if (Id3v2Tag is not null) {
			var renderedTag = Id3v2Tag.Render ();
			id3Data = renderedTag.ToArray ();
		}

		// Calculate new file size
		var newFileSize = DsfDsdChunk.Size + DsfFmtChunk.Size +
						  DsfDataChunk.HeaderSize + audioDataSize + id3Data.Length;
		var metadataOffset = id3Data.Length > 0
			? DsfDsdChunk.Size + DsfFmtChunk.Size + DsfDataChunk.HeaderSize + audioDataSize
			: 0;

		// Create new DSD chunk with updated values
		var newDsdChunk = DsfDsdChunk.Create ((ulong)newFileSize, (ulong)metadataOffset);

		// Build result
		var result = new byte[newFileSize];
		var offset = 0;

		// Write DSD chunk
		newDsdChunk.Render ().CopyTo (result, offset);
		offset += DsfDsdChunk.Size;

		// Write fmt chunk (unchanged)
		_fmtChunk.Render ().CopyTo (result, offset);
		offset += DsfFmtChunk.Size;

		// Write data chunk header
		var newDataChunk = DsfDataChunk.Create ((ulong)(DsfDataChunk.HeaderSize + audioDataSize));
		newDataChunk.RenderHeader ().CopyTo (result, offset);
		offset += DsfDataChunk.HeaderSize;

		// Copy audio data
		Array.Copy (_originalData!, _audioDataOffset, result, offset, audioDataSize);
		offset += audioDataSize;

		// Write ID3v2 tag if present
		if (id3Data.Length > 0) {
			id3Data.CopyTo (result, offset);
		}

		return new BinaryData (result);
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data containing audio frames.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		var rendered = Render ();
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render DSF file");

		return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data containing audio frames.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		cancellationToken.ThrowIfCancellationRequested ();

		var rendered = Render ();
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("Failed to render DSF file"));

		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return SaveToFile (path, readResult.Data!, fileSystem);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return await SaveToFileAsync (path, readResult.Data!, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Asynchronously saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	/// <remarks>
	/// Releases the internal byte array reference to allow garbage collection.
	/// After disposal, the <see cref="Render"/> method will throw <see cref="ObjectDisposedException"/>.
	/// </remarks>
	public void Dispose ()
	{
		if (_disposed)
			return;

		_originalData = null;
		Id3v2Tag = null;
		Properties = null;
		_disposed = true;
	}
}
