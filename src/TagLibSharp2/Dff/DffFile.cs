// DFF (DSDIFF) file format implementation
// Based on DSDIFF 1.5 specification
// https://www.sonicstudio.com/pdf/dsd/DSDIFF_1.5_Spec.pdf
//
// Key characteristics:
// - IFF-based container format (FRM8)
// - Big-endian byte order
// - Chunks: FVER (version), PROP (properties), DSD/DST (audio)
// - Optional ID3v2 chunk for metadata (unofficial extension)

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Core;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3.Id3v2;

#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable CA2231 // Overload operator equals on overriding value type Equals
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable CA1513 // Use ObjectDisposedException.ThrowIf
#pragma warning disable CA2000 // Dispose objects before losing scope

namespace TagLibSharp2.Dff;

/// <summary>
/// Result of parsing a DFF file.
/// </summary>
public readonly struct DffFileReadResult : IEquatable<DffFileReadResult>
{
	/// <summary>Gets the parsed file, if successful.</summary>
	public DffFile? File { get; }

	/// <summary>Gets the error message, if failed.</summary>
	public string? Error { get; }

	/// <summary>Gets whether parsing was successful.</summary>
	public bool IsSuccess => File is not null && Error is null;

	private DffFileReadResult (DffFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>Creates a successful result.</summary>
	public static DffFileReadResult Success (DffFile file) => new (file, null);

	/// <summary>Creates a failure result.</summary>
	public static DffFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (DffFileReadResult other) =>
		Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is DffFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);
}

/// <summary>
/// Audio properties for DFF files.
/// </summary>
public sealed class DffAudioProperties : Core.IMediaProperties
{
	/// <summary>Gets the audio duration.</summary>
	public TimeSpan Duration { get; }

	/// <summary>Gets the sample rate in Hz.</summary>
	public int SampleRate { get; }

	/// <summary>Gets the number of audio channels.</summary>
	public int Channels { get; }

	/// <summary>Gets the bits per sample (always 1 for DSD).</summary>
	public int BitsPerSample => 1;

	/// <summary>Gets the bitrate in kbps.</summary>
	/// <remarks>
	/// For DSD, bitrate is calculated as: sampleRate * channels / 1000.
	/// </remarks>
	public int Bitrate { get; }

	/// <summary>Gets the codec name.</summary>
	/// <remarks>
	/// Returns "DSD" for uncompressed or "DST" for DST-compressed files.
	/// </remarks>
	public string? Codec { get; }

	/// <summary>Gets the DSD rate classification.</summary>
	public DsfSampleRate DsdRate { get; }

	/// <summary>Gets the compression type.</summary>
	public DffCompressionType CompressionType { get; }

	internal DffAudioProperties (
		uint sampleRate,
		uint channelCount,
		ulong sampleCount,
		DffCompressionType compressionType)
	{
		SampleRate = (int)sampleRate;
		Channels = (int)channelCount;
		CompressionType = compressionType;
		Codec = compressionType == DffCompressionType.Dst ? "DST" : "DSD";

		// Calculate bitrate: sampleRate * channels / 1000 (1 bit per sample for DSD)
		Bitrate = sampleRate > 0 ? (int)((long)sampleRate * channelCount / 1000) : 0;

		// Calculate duration
		if (sampleRate > 0 && sampleCount > 0) {
			try {
				var seconds = (double)sampleCount / sampleRate;
				Duration = TimeSpan.FromSeconds (seconds);
			} catch (OverflowException) {
				Duration = TimeSpan.MaxValue;
			}
		}

		// Classify DSD rate
		DsdRate = sampleRate switch {
			2822400 => DsfSampleRate.DSD64,
			5644800 => DsfSampleRate.DSD128,
			11289600 => DsfSampleRate.DSD256,
			22579200 => DsfSampleRate.DSD512,
			45158400 => DsfSampleRate.DSD1024,
			_ => DsfSampleRate.Unknown
		};
	}
}

/// <summary>
/// Represents a DFF (DSDIFF) audio file.
/// </summary>
public sealed class DffFile : IMediaFile
{
	private const int MinHeaderSize = 20; // FRM8(4) + size(8) + DSD (4) + some content
	private static readonly byte[] Frm8Magic = { 0x46, 0x52, 0x4D, 0x38 }; // "FRM8"
	private static readonly byte[] DsdFormType = { 0x44, 0x53, 0x44, 0x20 }; // "DSD "

	private byte[]? _originalData;
	private bool _disposed;
	private IFileSystem? _sourceFileSystem;

	/// <summary>Gets the source file path, if read from disk.</summary>
	public string? SourcePath { get; private set; }

	/// <summary>Gets the format version major number.</summary>
	public int FormatVersionMajor { get; private set; }

	/// <summary>Gets the format version minor number.</summary>
	public int FormatVersionMinor { get; private set; }

	/// <summary>Gets the sample rate in Hz.</summary>
	public int SampleRate { get; private set; }

	/// <summary>Gets the number of audio channels.</summary>
	public int Channels { get; private set; }

	/// <summary>Gets the bits per sample (always 1 for DSD).</summary>
	public int BitsPerSample => 1;

	/// <summary>Gets the total sample count.</summary>
	public ulong SampleCount => _sampleCount;

	/// <summary>Gets the DSD rate classification.</summary>
	public DsfSampleRate DsdRate { get; private set; }

	/// <summary>Gets the audio duration.</summary>
	public TimeSpan Duration { get; private set; }

	/// <summary>Gets the compression type.</summary>
	public DffCompressionType CompressionType { get; private set; }

	/// <summary>Gets whether the audio is compressed (DST).</summary>
	public bool IsCompressed => CompressionType == DffCompressionType.Dst;

	/// <summary>Gets the audio properties.</summary>
	public DffAudioProperties? Properties { get; private set; }

	/// <summary>Gets or sets the ID3v2 tag (unofficial extension).</summary>
	public Id3v2Tag? Id3v2Tag { get; set; }

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
	public MediaFormat Format => MediaFormat.Dff;

	/// <summary>
	/// Gets a value indicating whether this file has an ID3v2 tag.
	/// </summary>
	public bool HasId3v2Tag => Id3v2Tag is not null;

	// Internal state for rendering
	private int _fverOffset;
	private int _propOffset;
	private int _propSize;
	private int _dsdOffset;
	private int _dsdSize;
	private int _id3Offset;
	private int _id3Size;
	private ulong _sampleCount;

	private DffFile () { }

	/// <summary>
	/// Parses a DFF file from binary data.
	/// </summary>
	public static DffFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinHeaderSize)
			return DffFileReadResult.Failure ($"Data too short for DFF file: {data.Length} bytes");

		// Check FRM8 magic
		if (!data[..4].SequenceEqual (Frm8Magic))
			return DffFileReadResult.Failure ("Invalid DFF file: missing FRM8 magic");

		// Read form size (big-endian)
		var formSize = BinaryPrimitives.ReadUInt64BigEndian (data[4..12]);

		// Check DSD form type
		if (!data[12..16].SequenceEqual (DsdFormType))
			return DffFileReadResult.Failure ("Invalid DFF file: expected DSD form type");

		var file = new DffFile {
			_originalData = data.ToArray ()
		};

		// Parse chunks
		var offset = 16;
		var foundFver = false;
		var foundProp = false;
		var foundAudio = false;
		var isFirstChunk = true;

		// Track PROP sub-chunks
		var foundFs = false;
		var foundChnl = false;
		var foundCmpr = false;

		while (offset + 12 <= data.Length) {
			var chunkId = System.Text.Encoding.ASCII.GetString (data.Slice (offset, 4));
			var chunkSize = BinaryPrimitives.ReadUInt64BigEndian (data.Slice (offset + 4, 8));
			var availableData = (ulong)(data.Length - offset - 12);
			var chunkExtendsData = chunkSize > availableData;

			// FVER must be the first chunk per DSDIFF spec
			if (isFirstChunk && chunkId != "FVER")
				return DffFileReadResult.Failure ("Invalid DFF file: FVER must be the first chunk");

			switch (chunkId) {
				case "FVER":
					file._fverOffset = offset;
					if (chunkSize >= 4 && availableData >= 4) {
						var version = BinaryPrimitives.ReadUInt32BigEndian (data.Slice (offset + 12, 4));
						file.FormatVersionMajor = (int)((version >> 24) & 0xFF);
						file.FormatVersionMinor = (int)((version >> 16) & 0xFF);
					}
					foundFver = true;
					break;

				case "PROP":
					file._propOffset = offset;
					file._propSize = (int)chunkSize;
					if (!chunkExtendsData)
						ParsePropChunk (data.Slice (offset + 12, (int)chunkSize), file, ref foundFs, ref foundChnl, ref foundCmpr);
					foundProp = true;
					break;

				case "DSD ":
					// PROP must precede audio data per spec
					if (!foundProp)
						return DffFileReadResult.Failure ("Invalid DFF file: PROP chunk must precede audio data");

					file._dsdOffset = offset;
					file._dsdSize = (int)chunkSize;
					// Sample count = data size * 8 / channels (1 bit per sample)
					// Use chunk SIZE (not available data) to calculate samples
					if (file.Channels > 0) {
						file._sampleCount = chunkSize * 8 / (ulong)file.Channels;
					}
					foundAudio = true;
					break;

				case "DST ":
					// PROP must precede audio data per spec
					if (!foundProp)
						return DffFileReadResult.Failure ("Invalid DFF file: PROP chunk must precede audio data");

					file._dsdOffset = offset;
					file._dsdSize = (int)chunkSize;
					file.CompressionType = DffCompressionType.Dst;
					foundAudio = true;
					break;

				case "ID3 ":
					file._id3Offset = offset;
					file._id3Size = (int)chunkSize;
					if (!chunkExtendsData) {
						var id3Data = data.Slice (offset + 12, (int)chunkSize);
						var id3Result = Id3v2Tag.Read (id3Data);
						if (id3Result.IsSuccess)
							file.Id3v2Tag = id3Result.Tag;
					}
					break;
			}

			isFirstChunk = false;

			// Move to next chunk (IFF chunks are padded to even byte boundaries)
			if (chunkExtendsData) {
				// For truncated chunks (like audio data), skip to end of available data
				// to look for any trailing chunks (like ID3)
				offset += 12 + (int)availableData;
			} else {
				offset += 12 + (int)chunkSize;
				// Add padding for odd-sized chunks (IFF alignment requirement)
				if (chunkSize % 2 != 0 && offset < data.Length)
					offset++;
			}
		}

		if (!foundFver)
			return DffFileReadResult.Failure ("Invalid DFF file: missing FVER chunk");

		if (!foundProp)
			return DffFileReadResult.Failure ("Invalid DFF file: missing PROP chunk");

		// Validate required PROP sub-chunks
		if (!foundFs)
			return DffFileReadResult.Failure ("Invalid DFF file: missing FS (sample rate) in PROP chunk");

		if (!foundChnl)
			return DffFileReadResult.Failure ("Invalid DFF file: missing CHNL (channels) in PROP chunk");

		if (!foundCmpr)
			return DffFileReadResult.Failure ("Invalid DFF file: missing CMPR (compression) in PROP chunk");

		// Audio data chunk is required
		if (!foundAudio)
			return DffFileReadResult.Failure ("Invalid DFF file: missing DSD or DST audio data chunk");

		// Calculate duration
		if (file.SampleRate > 0 && file._sampleCount > 0) {
			try {
				var seconds = (double)file._sampleCount / file.SampleRate;
				file.Duration = TimeSpan.FromSeconds (seconds);
			} catch (OverflowException) {
				file.Duration = TimeSpan.MaxValue;
			}
		}

		// Create properties object
		file.Properties = new DffAudioProperties (
			(uint)file.SampleRate,
			(uint)file.Channels,
			file._sampleCount,
			file.CompressionType);

		return DffFileReadResult.Success (file);
	}

	/// <summary>
	/// Attempts to read a DFF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out DffFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read a DFF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out DffFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid DFF file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with "FRM8" and contains "DSD " form type.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 16 bytes: FRM8 (4) + size (8) + DSD  (4)
		if (data.Length < 16)
			return false;

		// Check "FRM8" magic
		if (data[0] != 'F' || data[1] != 'R' || data[2] != 'M' || data[3] != '8')
			return false;

		// Check "DSD " form type at offset 12
		return data[12] == 'D' && data[13] == 'S' && data[14] == 'D' && data[15] == ' ';
	}

	private static void ParsePropChunk (ReadOnlySpan<byte> data, DffFile file, ref bool foundFs, ref bool foundChnl, ref bool foundCmpr)
	{
		if (data.Length < 4) return;

		// Property type should be "SND "
		var propType = System.Text.Encoding.ASCII.GetString (data[..4]);
		if (propType != "SND ") return;

		var offset = 4;
		while (offset + 12 <= data.Length) {
			var chunkId = System.Text.Encoding.ASCII.GetString (data.Slice (offset, 4));
			var chunkSize = BinaryPrimitives.ReadUInt64BigEndian (data.Slice (offset + 4, 8));

			if (chunkSize > (ulong)(data.Length - offset - 12))
				break;

			switch (chunkId) {
				case "FS  ": // Sample rate
					if (chunkSize >= 4) {
						file.SampleRate = (int)BinaryPrimitives.ReadUInt32BigEndian (data.Slice (offset + 12, 4));
						file.DsdRate = file.SampleRate switch {
							2822400 => DsfSampleRate.DSD64,
							5644800 => DsfSampleRate.DSD128,
							11289600 => DsfSampleRate.DSD256,
							22579200 => DsfSampleRate.DSD512,
							45158400 => DsfSampleRate.DSD1024,
							_ => DsfSampleRate.Unknown
						};
						foundFs = true;
					}
					break;

				case "CHNL": // Channels
					if (chunkSize >= 2) {
						file.Channels = BinaryPrimitives.ReadUInt16BigEndian (data.Slice (offset + 12, 2));
						foundChnl = true;
					}
					break;

				case "CMPR": // Compression type
					if (chunkSize >= 4) {
						var cmprType = System.Text.Encoding.ASCII.GetString (data.Slice (offset + 12, 4));
						file.CompressionType = cmprType switch {
							"DSD " => DffCompressionType.Dsd,
							"DST " => DffCompressionType.Dst,
							_ => DffCompressionType.Unknown
						};
						foundCmpr = true;
					}
					break;
			}

			offset += 12 + (int)chunkSize;
			// Add padding for odd-sized chunks within PROP
			if (chunkSize % 2 != 0 && offset < data.Length)
				offset++;
		}
	}

	/// <summary>
	/// Reads a DFF file from disk.
	/// </summary>
	public static DffFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		fileSystem ??= DefaultFileSystem.Instance;

		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return DffFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}

		return result;
	}

	/// <summary>
	/// Reads a DFF file from disk asynchronously.
	/// </summary>
	public static async Task<DffFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		fileSystem ??= DefaultFileSystem.Instance;

		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);

		if (!readResult.IsSuccess)
			return DffFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}

		return result;
	}

	/// <summary>
	/// Ensures an ID3v2 tag exists, creating one if necessary.
	/// </summary>
	public Id3v2Tag EnsureId3v2Tag ()
	{
		Id3v2Tag ??= new Id3v2Tag ();
		return Id3v2Tag;
	}

	/// <summary>
	/// Renders the DFF file to binary data.
	/// </summary>
	public BinaryData Render ()
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		if (_originalData is null)
			throw new InvalidOperationException ("Cannot render: no original data available");

		// If no ID3v2 tag changes, return original data
		if (Id3v2Tag is null && _id3Offset == 0)
			return new BinaryData (_originalData);

		using var ms = new MemoryStream ();

		// Copy everything up to ID3 chunk (or end if no ID3)
		var copyEnd = _id3Offset > 0 ? _id3Offset : _originalData.Length;
		ms.Write (_originalData, 0, copyEnd);

		// Write ID3 chunk if we have a tag
		if (Id3v2Tag is not null) {
			var id3Data = Id3v2Tag.Render ();
			var id3ChunkId = System.Text.Encoding.ASCII.GetBytes ("ID3 ");
			ms.Write (id3ChunkId, 0, id3ChunkId.Length);
			WriteUInt64BE (ms, (ulong)id3Data.Length);
			var id3Bytes = id3Data.ToArray ();
			ms.Write (id3Bytes, 0, id3Bytes.Length);

			// Pad to even boundary
			if (id3Data.Length % 2 != 0)
				ms.WriteByte (0);
		}

		// Update FRM8 size
		var result = ms.ToArray ();
		var newSize = (ulong)(result.Length - 12);
		for (int i = 0; i < 8; i++)
			result[4 + i] = (byte)(newSize >> (56 - i * 8));

		return new BinaryData (result);
	}

	/// <summary>
	/// Saves the file to disk with explicit original data.
	/// </summary>
	/// <param name="path">The path to save to.</param>
	/// <param name="originalData">The original file data to use for rendering.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		try {
			var rendered = Render ();
			return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
		} catch (Exception ex) {
			return FileWriteResult.Failure ($"Failed to save file: {ex.Message}");
		}
	}

	/// <summary>
	/// Saves the file to disk asynchronously with explicit original data.
	/// </summary>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		return SaveToFileAsyncCore (path, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to a new path.
	/// </summary>
	/// <param name="path">The path to save to.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		if (_originalData is null)
			return FileWriteResult.Failure ("No original data available");

		try {
			var rendered = Render ();
			return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
		} catch (Exception ex) {
			return FileWriteResult.Failure ($"Failed to save file: {ex.Message}");
		}
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction. If null, uses the file system from ReadFromFile.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		fileSystem ??= _sourceFileSystem;
		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Saves the file asynchronously to a new path.
	/// </summary>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		return await SaveToFileAsyncCore (path, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Saves the file asynchronously back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction. If null, uses the file system from ReadFromFile.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (_disposed)
			throw new ObjectDisposedException (nameof (DffFile));

		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		fileSystem ??= _sourceFileSystem;
		return await SaveToFileAsyncCore (SourcePath!, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	private async Task<FileWriteResult> SaveToFileAsyncCore (
		string path,
		IFileSystem? fileSystem,
		CancellationToken cancellationToken)
	{
		try {
			var rendered = Render ();
			return await AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken)
				.ConfigureAwait (false);
		} catch (Exception ex) {
			return FileWriteResult.Failure ($"Failed to save file: {ex.Message}");
		}
	}

	private static void WriteUInt64BE (Stream stream, ulong value)
	{
		for (int i = 7; i >= 0; i--)
			stream.WriteByte ((byte)(value >> (i * 8)));
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed) return;
		_originalData = null;
		Id3v2Tag = null;
		Properties = null;
		_disposed = true;
	}
}
