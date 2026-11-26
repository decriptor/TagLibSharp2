// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

#pragma warning disable CA1513 // Use ObjectDisposedException.ThrowIf (not available in netstandard)

namespace TagLibSharp2.Dsf;

/// <summary>
/// Audio properties specific to DSF (DSD) files.
/// </summary>
public sealed class DsfAudioProperties : Core.IMediaProperties
{
	/// <summary>Gets the audio duration.</summary>
	public TimeSpan Duration { get; }

	/// <summary>Gets the sample rate in Hz (e.g., 2822400 for DSD64).</summary>
	public int SampleRate { get; }

	/// <summary>Gets the number of audio channels.</summary>
	public int Channels { get; }

	/// <summary>Gets the bits per sample (always 1 for DSD).</summary>
	public int BitsPerSample { get; }

	/// <summary>Gets the bitrate in kbps.</summary>
	/// <remarks>
	/// For DSD, bitrate is calculated as: sampleRate * channels / 1000.
	/// Returns 0 as DSD bitrate calculation differs from PCM formats.
	/// </remarks>
	public int Bitrate { get; }

	/// <summary>Gets the codec name.</summary>
	public string? Codec => "DSD";

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

		// Calculate bitrate: sampleRate * channels / 1000 (1 bit per sample for DSD)
		Bitrate = SampleRate > 0 ? (int)((long)SampleRate * Channels / 1000) : 0;
	}
}

/// <summary>
/// Represents the result of parsing a DSF file.
/// </summary>
public readonly struct DsfFileReadResult : IEquatable<DsfFileReadResult>
{
	/// <summary>
	/// Gets the parsed DSF file, or null if parsing failed.
	/// </summary>
	public DsfFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	private DsfFileReadResult (DsfFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="file">The parsed DSF file.</param>
	/// <returns>A successful result containing the file.</returns>
	public static DsfFileReadResult Success (DsfFile file) => new (file, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static DsfFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (DsfFileReadResult other) =>
		Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is DsfFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (DsfFileReadResult left, DsfFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (DsfFileReadResult left, DsfFileReadResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents a DSF (DSD Stream File) audio file.
/// </summary>
public sealed class DsfFile : IMediaFile
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

	/// <inheritdoc />
	public Tag? Tag => Id3v2Tag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is not null ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Dsf;

	private DsfFile (
		byte[]? originalData,
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
	public static DsfFileReadResult Read (ReadOnlySpan<byte> data)
	{
		const int minSize = DsfDsdChunk.Size + DsfFmtChunk.Size + DsfDataChunk.HeaderSize;

		if (data.Length < minSize) {
			return DsfFileReadResult.Failure (
				$"Data too short for DSF file: {data.Length} bytes, need at least {minSize}");
		}

		// Parse DSD chunk
		var dsdResult = DsfDsdChunk.Parse (data);
		if (!dsdResult.IsSuccess) {
			return DsfFileReadResult.Failure ($"Failed to parse DSD chunk: {dsdResult.Error}");
		}
		var dsdChunk = dsdResult.Chunk!;

		// Parse fmt chunk
		var fmtOffset = DsfDsdChunk.Size;
		var fmtResult = DsfFmtChunk.Parse (data[fmtOffset..]);
		if (!fmtResult.IsSuccess) {
			return DsfFileReadResult.Failure ($"Failed to parse format chunk: {fmtResult.Error}");
		}
		var fmtChunk = fmtResult.Chunk!;

		// Parse data chunk header
		var dataOffset = fmtOffset + DsfFmtChunk.Size;
		var dataResult = DsfDataChunk.Parse (data[dataOffset..]);
		if (!dataResult.IsSuccess) {
			return DsfFileReadResult.Failure ($"Failed to parse data chunk: {dataResult.Error}");
		}
		var dataChunk = dataResult.Chunk!;

		var audioDataOffset = dataOffset + DsfDataChunk.HeaderSize;

		// Validate data chunk size against available data
		var audioDataSize = dataChunk.AudioDataSize;
		var dataEnd = (ulong)audioDataOffset + audioDataSize;

		if (dsdChunk.HasMetadata) {
			// Data chunk should not extend beyond metadata offset
			if (dataEnd > dsdChunk.MetadataOffset) {
				return DsfFileReadResult.Failure (
					$"Data chunk extends beyond metadata offset: data ends at {dataEnd}, metadata at {dsdChunk.MetadataOffset}");
			}
		} else {
			// Data chunk should not exceed file size
			if (dataEnd > (ulong)data.Length) {
				return DsfFileReadResult.Failure (
					$"Data chunk size exceeds available data: claims {audioDataSize} bytes, but only {data.Length - audioDataOffset} available");
			}
		}

		// Validate metadata offset if present
		Id3v2Tag? tag = null;
		if (dsdChunk.HasMetadata) {
			if (dsdChunk.MetadataOffset > (ulong)data.Length) {
				return DsfFileReadResult.Failure (
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
		return DsfFileReadResult.Success (file);
	}

	/// <summary>
	/// Reads the DSD, fmt, and data headers, then the ID3 tag. Sample bytes stay on disk.
	/// </summary>
	internal static DsfFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek) {
			var copied = Read (StreamBytes.ReadToEnd (stream));
			if (sourcePath is not null && copied.IsSuccess && copied.File is not null)
				copied.File._originalData = null;
			return WithSource (copied, sourcePath, fileSystem);
		}

		stream.Position = 0;
		var headerBytes = DsfDsdChunk.Size + DsfFmtChunk.Size + DsfDataChunk.HeaderSize;
		var headers = StreamBytes.ReadAtMost (stream, headerBytes);
		if (headers.Length < headerBytes)
			return DsfFileReadResult.Failure ($"Data too short for DSF file: {stream.Length} bytes, need at least {headerBytes}");

		var dsdResult = DsfDsdChunk.Parse (headers);
		if (!dsdResult.IsSuccess)
			return DsfFileReadResult.Failure ($"Failed to parse DSD chunk: {dsdResult.Error}");
		var dsdChunk = dsdResult.Chunk!;

		var fmtResult = DsfFmtChunk.Parse (headers.AsSpan (DsfDsdChunk.Size));
		if (!fmtResult.IsSuccess)
			return DsfFileReadResult.Failure ($"Failed to parse format chunk: {fmtResult.Error}");
		var fmtChunk = fmtResult.Chunk!;

		var dataOffset = DsfDsdChunk.Size + DsfFmtChunk.Size;
		var dataResult = DsfDataChunk.Parse (headers.AsSpan (dataOffset));
		if (!dataResult.IsSuccess)
			return DsfFileReadResult.Failure ($"Failed to parse data chunk: {dataResult.Error}");
		var dataChunk = dataResult.Chunk!;
		var audioDataOffset = dataOffset + DsfDataChunk.HeaderSize;
		var dataEnd = (ulong)audioDataOffset + dataChunk.AudioDataSize;
		if (dsdChunk.HasMetadata) {
			if (dataEnd > dsdChunk.MetadataOffset)
				return DsfFileReadResult.Failure (
					$"Data chunk extends beyond metadata offset: data ends at {dataEnd}, metadata at {dsdChunk.MetadataOffset}");
			if (dsdChunk.MetadataOffset > (ulong)stream.Length)
				return DsfFileReadResult.Failure (
					$"Metadata offset {dsdChunk.MetadataOffset} beyond file size {stream.Length}");
		} else if (dataEnd > (ulong)stream.Length) {
			return DsfFileReadResult.Failure (
				$"Data chunk size exceeds available data: claims {dataChunk.AudioDataSize} bytes, but only {stream.Length - audioDataOffset} available");
		}

		Id3v2Tag? tag = null;
		if (dsdChunk.HasMetadata) {
			stream.Position = (long)dsdChunk.MetadataOffset;
			var id3 = StreamBytes.ReadToEnd (stream);
			var id3Result = Id3v2Tag.Read (id3);
			if (id3Result.IsSuccess)
				tag = id3Result.Tag;
		}

		var parsed = new DsfFile (null, dsdChunk, fmtChunk, dataChunk, audioDataOffset, tag) {
			Properties = new DsfAudioProperties (fmtChunk),
			SourcePath = sourcePath,
			_sourceFileSystem = fileSystem
		};
		return DsfFileReadResult.Success (parsed);
	}

	static DsfFileReadResult WithSource (DsfFileReadResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (result.IsSuccess && result.File is not null) {
			result.File.SourcePath = sourcePath;
			result.File._sourceFileSystem = fileSystem;
		}

		return result;
	}

	/// <summary>
	/// Attempts to read a DSF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out DsfFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read a DSF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out DsfFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid DSF file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with "DSD " magic bytes.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 4 bytes for magic
		if (data.Length < 4)
			return false;

		// Check for "DSD " magic
		return data[0] == 'D' && data[1] == 'S' && data[2] == 'D' && data[3] == ' ';
	}

	/// <summary>
	/// Reads a DSF file from the specified path.
	/// </summary>
	/// <param name="path">The path to the file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	public static DsfFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess) {
			return DsfFileReadResult.Failure ($"Failed to read file: {readResult.Error}");
		}

		var parseResult = Read (readResult.Data!.Value.Span);
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
	public static async Task<DsfFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess) {
			return DsfFileReadResult.Failure ($"Failed to read file: {readResult.Error}");
		}

		var parseResult = Read (readResult.Data!.Value.Span);
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

		var fs = fileSystem ?? _sourceFileSystem;
		var rendered = RenderWith (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render DSF file");

		return AtomicFileWriter.Write (path, rendered.Span, fs);
	}

	BinaryData RenderWith (ReadOnlySpan<byte> originalData)
	{
		if (_originalData is not null)
			return Render ();

		_originalData = originalData.ToArray ();
		try {
			return Render ();
		} finally {
			_originalData = null;
		}
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

		var fs = fileSystem ?? _sourceFileSystem;
		var rendered = RenderWith (originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("Failed to render DSF file"));

		cancellationToken.ThrowIfCancellationRequested ();
		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fs, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// When the sample bytes were left on disk, copies that range from <see cref="SourcePath"/>
	/// and writes the current chunks and ID3 tag. A file that still holds its original bytes
	/// writes those bytes and does not open the source.
	/// </remarks>
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
		if (_originalData is not null)
			return WriteHeld (path, fs);

		return SaveFromSource (path, fs);
	}

	// Same bytes as Render. The audio slice is the held sample bytes, written in chunks.
	// The source file is not opened: a later edit of the file on disk does not change this save.
	FileWriteResult WriteHeld (string destinationPath, IFileSystem? fileSystem, CancellationToken cancellationToken = default)
	{
		var audioDataEnd = _dsdChunk.HasMetadata
			? (int)_dsdChunk.MetadataOffset
			: _originalData!.Length;
		var audioDataSize = audioDataEnd - _audioDataOffset;
		if (audioDataSize < 0)
			return FileWriteResult.Failure ("Failed to render DSF file");

		var id3 = Id3v2Tag is not null ? Id3v2Tag.Render () : BinaryData.Empty;
		var preamble = (long)DsfDsdChunk.Size + DsfFmtChunk.Size + DsfDataChunk.HeaderSize;
		var newFileSize = preamble + audioDataSize + id3.Length;
		var metadataOffset = id3.Length > 0 ? preamble + audioDataSize : 0L;
		var dsd = DsfDsdChunk.Create ((ulong)newFileSize, (ulong)metadataOffset).Render ();
		var fmt = _fmtChunk.Render ();
		var dataHeader = DsfDataChunk.Create ((ulong)(DsfDataChunk.HeaderSize + audioDataSize)).RenderHeader ();

		if (cancellationToken.IsCancellationRequested)
			return FileWriteResult.Failure ("Failed to re-read source file: Operation was cancelled");

		var chunk = StreamCopy.Rent ();
		try {
			var audioStart = _audioDataOffset;
			var audioCount = audioDataSize;
			return AtomicFileWriter.WriteStream (destinationPath, fileSystem, output => {
				StreamCopy.Write (output, dsd, chunk);
				StreamCopy.Write (output, fmt, chunk);
				StreamCopy.Write (output, dataHeader, chunk);
				if (audioCount > 0)
					StreamCopy.Write (output, _originalData.AsSpan (audioStart, audioCount), chunk);
				if (id3.Length > 0)
					StreamCopy.Write (output, id3.Span, chunk);
			});
		} finally {
			StreamCopy.Return (chunk);
		}
	}

	// Same bytes as Render: DSD, fmt, and data headers, then the audio range, then the ID3 tag.
	FileWriteResult SaveFromSource (string destinationPath, IFileSystem? fileSystem, CancellationToken cancellationToken = default)
	{
		OpenedFile? source = null;
		byte[]? chunk = null;
		try {
			if (!PathSave.TryOpen (SourcePath!, fileSystem, out source, out var error))
				return error;

			var fileLength = source!.Length;
			var audioDataEnd = _dsdChunk.HasMetadata ? MetadataEnd () : fileLength;
			var audioDataSize = audioDataEnd - _audioDataOffset;
			if (audioDataSize < 0)
				return FileWriteResult.Failure ("Failed to render DSF file");

			var id3 = Id3v2Tag is not null ? Id3v2Tag.Render () : BinaryData.Empty;
			var preamble = (long)DsfDsdChunk.Size + DsfFmtChunk.Size + DsfDataChunk.HeaderSize;
			var newFileSize = preamble + audioDataSize + id3.Length;
			var metadataOffset = id3.Length > 0 ? preamble + audioDataSize : 0L;
			var dsd = DsfDsdChunk.Create ((ulong)newFileSize, (ulong)metadataOffset).Render ();
			var fmt = _fmtChunk.Render ();
			var dataHeader = DsfDataChunk.Create ((ulong)(DsfDataChunk.HeaderSize + audioDataSize)).RenderHeader ();

			if (cancellationToken.IsCancellationRequested)
				return FileWriteResult.Failure ("Failed to re-read source file: Operation was cancelled");

			chunk = StreamCopy.Rent ();
			var copyBuffer = chunk;
			var opened = source;
			var audioStart = (long)_audioDataOffset;
			var written = PathSave.Replace (destinationPath, opened, output => {
				StreamCopy.Write (output, dsd, copyBuffer);
				StreamCopy.Write (output, fmt, copyBuffer);
				StreamCopy.Write (output, dataHeader, copyBuffer);
				if (audioDataSize > 0) {
					if (opened.Stream.Position != audioStart)
						opened.Stream.Position = audioStart;
					StreamCopy.Copy (opened.Stream, output, audioDataSize, copyBuffer);
				}

				if (id3.Length > 0)
					StreamCopy.Write (output, id3.Span, copyBuffer);
			});
			source = null;
			return written;
		} finally {
			source?.Dispose ();
			if (chunk is not null)
				StreamCopy.Return (chunk);
		}
	}

	// Render casts a metadata offset to int. Values that fit use that same cast.
	long MetadataEnd ()
	{
		var offset = _dsdChunk.MetadataOffset;
		if (offset > int.MaxValue)
			return (long)offset;

		return (int)offset;
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
	/// <remarks>
	/// When the sample bytes were left on disk, copies that range from <see cref="SourcePath"/>
	/// and writes the current chunks and ID3 tag. A file that still holds its original bytes
	/// writes those bytes and does not open the source.
	/// A source that cannot be opened or rendered is reported before cancellation.
	/// A token that is already cancelled returns before the destination is replaced. The copy itself runs to completion.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DsfFile));

		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		if (_originalData is not null)
			return Task.FromResult (WriteHeld (path, fileSystem ?? _sourceFileSystem, cancellationToken));

		return Task.FromResult (SaveFromSource (path, fileSystem ?? _sourceFileSystem, cancellationToken));
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
