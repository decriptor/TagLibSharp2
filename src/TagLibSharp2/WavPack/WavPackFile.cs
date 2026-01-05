// WavPack (.wv) file support
// Implemented from format specification at www.wavpack.com/file_format.txt

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;

namespace TagLibSharp2.WavPack;

/// <summary>
/// Represents the result of parsing a WavPack file.
/// </summary>
public readonly struct WavPackFileReadResult : IEquatable<WavPackFileReadResult>
{
	/// <summary>
	/// Gets the parsed WavPack file, or null if parsing failed.
	/// </summary>
	public WavPackFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	private WavPackFileReadResult (WavPackFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="file">The parsed WavPack file.</param>
	/// <returns>A successful result containing the file.</returns>
	public static WavPackFileReadResult Success (WavPackFile file) => new (file, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static WavPackFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (WavPackFileReadResult other) =>
		Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is WavPackFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (WavPackFileReadResult left, WavPackFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (WavPackFileReadResult left, WavPackFileReadResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents a WavPack (.wv) file.
/// </summary>
public sealed class WavPackFile : IMediaFile
{
	private bool _disposed;
	private const int MagicSize = 4;
	private const int BlockHeaderSize = 32;
	private static readonly byte[] Magic = "wvpk"u8.ToArray ();

	// Standard sample rate table (index 15 = custom rate stored in metadata sub-block 0x27)
	private static readonly int[] SampleRateTable = [
		6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000,
		32000, 44100, 48000, 64000, 88200, 96000, 192000
	];

	// Metadata sub-block IDs (bits 4-0 of the ID byte)
	// Note: bit 5 (0x20) is the "optional data" flag, not part of the ID
	private const byte MetaIdSampleRate = 0x07; // Custom sample rate (appears as 0x27 with optional flag)
	private const byte MetaIdChannelInfo = 0x0D; // Multi-channel info

	private byte[] _originalData = Array.Empty<byte> ();

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	private IFileSystem? _sourceFileSystem;

	private WavPackFile () { }

	/// <summary>WavPack format version</summary>
	public int Version { get; private set; }

	/// <summary>Block size in bytes (excluding first 8 bytes of header)</summary>
	public uint BlockSize { get; private set; }

	/// <summary>Sample rate in Hz</summary>
	public int SampleRate { get; private set; }

	/// <summary>Number of audio channels</summary>
	public int Channels { get; private set; }

	/// <summary>Bits per sample</summary>
	public int BitsPerSample { get; private set; }

	/// <summary>Total samples in file</summary>
	public uint TotalSamples { get; private set; }

	/// <summary>Audio properties</summary>
	public AudioProperties Properties { get; private set; }

	/// <summary>APEv2 tag (null if not present)</summary>
	public ApeTag? ApeTag { get; private set; }

	/// <inheritdoc />
	public Tag? Tag => ApeTag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is { IsValid: true } ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.WavPack;

	/// <summary>
	/// Parse a WavPack file from byte data.
	/// </summary>
	/// <param name="data">The raw file bytes to parse.</param>
	/// <returns>A result indicating success with the parsed file, or failure with an error message.</returns>
	[SuppressMessage ("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Factory method transfers ownership to caller")]
	public static WavPackFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MagicSize)
			return WavPackFileReadResult.Failure ("Invalid WavPack file: data too short for magic");

		// Verify magic "wvpk"
		if (!data[..MagicSize].SequenceEqual (Magic))
			return WavPackFileReadResult.Failure ("Invalid WavPack file: missing magic (expected 'wvpk')");

		if (data.Length < BlockHeaderSize)
			return WavPackFileReadResult.Failure ("Invalid WavPack file: data too short for block header");

		var file = new WavPackFile ();

		// Parse block header
		// [4-7] Block size
		file.BlockSize = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4, 4));

		// [8-9] Version
		file.Version = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (8, 2));

		// [12-15] Total samples
		file.TotalSamples = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (12, 4));

		// [24-27] Flags
		var flags = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (24, 4));

		// Parse flags
		// Bits 0-1: bytes per sample - 1
		var bytesPerSample = (int)((flags & 0x3) + 1);
		file.BitsPerSample = bytesPerSample * 8;

		// Bit 2: mono
		var isMono = (flags & 0x4) != 0;
		file.Channels = isMono ? 1 : 2;

		// Bits 23-26: sample rate index (0-14 = table lookup, 15 = custom)
		var sampleRateIndex = (int)((flags >> 23) & 0xF);
		if (sampleRateIndex < SampleRateTable.Length) {
			file.SampleRate = SampleRateTable[sampleRateIndex];
		} else {
			// Custom sample rate - parse from metadata sub-block 0x27
			file.SampleRate = ParseCustomSampleRate (data) ?? 44100;
		}

		// Check for multi-channel configuration
		// Bit 10: false stereo (decorrelation disabled)
		// For multi-channel, we need to parse metadata sub-block 0x0D
		var channelCount = ParseMultiChannelCount (data);
		if (channelCount > 0) {
			file.Channels = channelCount;
		}

		// Store original data
		file._originalData = data.ToArray ();

		// Calculate audio properties
		file.CalculateProperties ();

		// Parse APEv2 tag at end of file
		file.ParseApeTag (data);

		return WavPackFileReadResult.Success (file);
	}

	/// <summary>
	/// Attempts to read a WavPack file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out WavPackFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read a WavPack file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out WavPackFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid WavPack file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with "wvpk" magic bytes.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 4 bytes for magic
		if (data.Length < 4)
			return false;

		// Check for "wvpk" magic
		return data[0] == 'w' && data[1] == 'v' && data[2] == 'p' && data[3] == 'k';
	}

	private void CalculateProperties ()
	{
		if (SampleRate <= 0 || TotalSamples == 0) {
			Properties = default;
			return;
		}

		var durationSeconds = (double)TotalSamples / SampleRate;
		var duration = TimeSpan.FromSeconds (durationSeconds);

		// Calculate bitrate from file size
		var bitrate = 0;
		if (_originalData.Length > 0 && durationSeconds > 0) {
			bitrate = (int)(_originalData.Length * 8 / durationSeconds / 1000);
		}

		Properties = new AudioProperties (
			duration,
			bitrate,
			SampleRate,
			BitsPerSample,
			Channels,
			"WavPack"
		);
	}

	private void ParseApeTag (ReadOnlySpan<byte> data)
	{
		var result = ApeTag.Parse (data);
		if (result.IsSuccess) {
			ApeTag = result.Tag;
		}
	}

	/// <summary>
	/// Parse custom sample rate from metadata sub-block 0x07.
	/// Per WavPack spec, when sample rate index is 15, the actual rate is in this sub-block.
	/// </summary>
	private static int? ParseCustomSampleRate (ReadOnlySpan<byte> data)
	{
		// Metadata sub-blocks start after the 32-byte block header
		var offset = BlockHeaderSize;

		// Get block size to know where sub-blocks end
		if (data.Length < 8)
			return null;

		var blockSize = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4, 4));
		// Bounds check: blockSize shouldn't exceed reasonable file size
		if (blockSize > int.MaxValue - 8)
			return null;

		var blockEnd = Math.Min (8 + (int)blockSize, data.Length);

		while (offset + 2 <= blockEnd) {
			// Sub-block header: [0] ID, [1] size in words (2 bytes each)
			var subBlockId = data[offset];

			// Large sub-block flag (ID bit 7 set means 3-byte size field)
			var hasLargeSize = (subBlockId & 0x80) != 0;
			var actualId = (byte)(subBlockId & 0x1F); // Lower 5 bits are the ID per WavPack spec

			int subBlockSize;
			int dataOffset;
			if (hasLargeSize) {
				if (offset + 4 > blockEnd)
					break;
				// 3-byte size (24-bit) in words, multiply by 2 for bytes
				var sizeWords = data[offset + 1] | (data[offset + 2] << 8) | (data[offset + 3] << 16);
				// Bounds check to prevent overflow
				if (sizeWords > (int.MaxValue / 2))
					break;
				subBlockSize = sizeWords * 2;
				dataOffset = offset + 4;
			} else {
				subBlockSize = data[offset + 1] * 2;
				dataOffset = offset + 2;
			}

			// Prevent infinite loop: ensure we make forward progress
			if (subBlockSize < 0 || dataOffset + subBlockSize <= offset)
				break;

			// Bounds check for data access
			if (dataOffset + subBlockSize > blockEnd)
				break;

			// Odd size flag (bit 6) indicates last byte is padding
			var hasOddByte = (subBlockId & 0x40) != 0;
			var actualSize = hasOddByte && subBlockSize > 0 ? subBlockSize - 1 : subBlockSize;

			if (actualId == MetaIdSampleRate && actualSize >= 3) {
				// Sample rate stored as 3-byte little-endian value
				return data[dataOffset] |
					   (data[dataOffset + 1] << 8) |
					   (data[dataOffset + 2] << 16);
			}

			offset = dataOffset + subBlockSize;
		}

		return null;
	}

	/// <summary>
	/// Parse multi-channel count from metadata sub-block 0x0D.
	/// Returns 0 if not a multi-channel file.
	/// </summary>
	private static int ParseMultiChannelCount (ReadOnlySpan<byte> data)
	{
		// Metadata sub-blocks start after the 32-byte block header
		var offset = BlockHeaderSize;

		if (data.Length < 8)
			return 0;

		var blockSize = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4, 4));
		// Bounds check: blockSize shouldn't exceed reasonable file size
		if (blockSize > int.MaxValue - 8)
			return 0;

		var blockEnd = Math.Min (8 + (int)blockSize, data.Length);

		while (offset + 2 <= blockEnd) {
			var subBlockId = data[offset];

			var hasLargeSize = (subBlockId & 0x80) != 0;
			var actualId = (byte)(subBlockId & 0x1F); // Lower 5 bits are the ID

			int subBlockSize;
			int dataOffset;
			if (hasLargeSize) {
				if (offset + 4 > blockEnd)
					break;
				var sizeWords = data[offset + 1] | (data[offset + 2] << 8) | (data[offset + 3] << 16);
				// Bounds check to prevent overflow
				if (sizeWords > (int.MaxValue / 2))
					break;
				subBlockSize = sizeWords * 2;
				dataOffset = offset + 4;
			} else {
				subBlockSize = data[offset + 1] * 2;
				dataOffset = offset + 2;
			}

			// Prevent infinite loop: ensure we make forward progress
			if (subBlockSize < 0 || dataOffset + subBlockSize <= offset)
				break;

			// Bounds check for data access
			if (dataOffset + subBlockSize > blockEnd)
				break;

			if (actualId == MetaIdChannelInfo && subBlockSize >= 1) {
				// First byte contains channel count
				return data[dataOffset];
			}

			offset = dataOffset + subBlockSize;
		}

		return 0;
	}

	/// <summary>
	/// Ensures an APE tag exists, creating one if necessary.
	/// </summary>
	public ApeTag EnsureApeTag ()
	{
		ApeTag ??= new ApeTag ();
		return ApeTag;
	}

	/// <summary>
	/// Removes the APE tag.
	/// </summary>
	public void RemoveApeTag ()
	{
		ApeTag = null;
	}

	/// <summary>
	/// Renders the file to a byte array.
	/// </summary>
	public byte[] Render (ReadOnlySpan<byte> originalData)
	{
		// Find and remove existing APE tag from audio data
		var audioEnd = FindAudioEnd (originalData);
		var audioData = originalData[..audioEnd].ToArray ();

		using var ms = new MemoryStream ();
		ms.Write (audioData, 0, audioData.Length);

		// Append APE tag if present
		if (ApeTag is not null) {
			var tagData = ApeTag.Render ().ToArray ();
			ms.Write (tagData, 0, tagData.Length);
		}

		return ms.ToArray ();
	}

	private static int FindAudioEnd (ReadOnlySpan<byte> data)
	{
		// Look for APEv2 tag footer at end of file
		if (data.Length < ApeTagFooter.Size)
			return data.Length;

		var footerData = data[^ApeTagFooter.Size..];
		var footerResult = ApeTagFooter.Parse (footerData);

		if (!footerResult.IsSuccess)
			return data.Length;

		var footer = footerResult.Footer!;

		// TagSize includes footer but not header
		// Use long to prevent integer overflow for very large tags
		var tagSize = (long)footer.TagSize;

		// Check if there's a header (adds ApeTagFooter.Size bytes before items)
		var hasHeader = footer.HasHeader;
		var totalTagSize = hasHeader ? tagSize + ApeTagFooter.Size : tagSize;

		if (totalTagSize > data.Length)
			return data.Length;

		return data.Length - (int)totalTagSize;
	}

	// ===== File I/O =====

	/// <summary>
	/// Read a WavPack file from disk.
	/// </summary>
	public static WavPackFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return WavPackFileReadResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Read a WavPack file from disk asynchronously.
	/// </summary>
	public static async Task<WavPackFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return WavPackFileReadResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Save the file to a new path using the provided original data.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="originalData">The original file data containing audio content.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The result of the write operation.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (originalData);
		return AtomicFileWriter.Write (path, rendered, fs);
	}

	/// <summary>
	/// Save the file to a new path using internally stored data.
	/// </summary>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null) =>
		SaveToFile (path, _originalData, fileSystem);

	/// <summary>
	/// Save the file back to its source path.
	/// </summary>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Save the file asynchronously using the provided original data.
	/// </summary>
	/// <param name="path">The file path to save to.</param>
	/// <param name="originalData">The original file data containing audio content.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The result of the write operation.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (originalData.Span);
		return await AtomicFileWriter.WriteAsync (path, rendered, fs, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Save the file asynchronously using internally stored data.
	/// </summary>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default) =>
		SaveToFileAsync (path, _originalData, fileSystem, cancellationToken);

	/// <summary>
	/// Save the file back to its source path asynchronously.
	/// </summary>
	public async Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFileAsync(path) instead.");

		return await SaveToFileAsync (SourcePath!, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Releases resources held by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		ApeTag = null;
		Properties = default;
		_originalData = Array.Empty<byte> ();
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}
