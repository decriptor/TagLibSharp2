// WavPack (.wv) file support
// Implemented from format specification at www.wavpack.com/file_format.txt

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace TagLibSharp2.WavPack;

/// <summary>
/// Represents the result of parsing a WavPack file.
/// </summary>
public readonly struct WavPackFileParseResult : IEquatable<WavPackFileParseResult>
{
	public WavPackFile? File { get; }
	public string? Error { get; }
	public bool IsSuccess => File is not null && Error is null;

	private WavPackFileParseResult (WavPackFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	public static WavPackFileParseResult Success (WavPackFile file) => new (file, null);
	public static WavPackFileParseResult Failure (string error) => new (null, error);

	public bool Equals (WavPackFileParseResult other) =>
		Equals (File, other.File) && Error == other.Error;

	public override bool Equals (object? obj) =>
		obj is WavPackFileParseResult other && Equals (other);

	public override int GetHashCode () => HashCode.Combine (File, Error);

	public static bool operator == (WavPackFileParseResult left, WavPackFileParseResult right) =>
		left.Equals (right);

	public static bool operator != (WavPackFileParseResult left, WavPackFileParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents a WavPack (.wv) file.
/// </summary>
public sealed class WavPackFile
{
	private const int MagicSize = 4;
	private const int BlockHeaderSize = 32;
	private static readonly byte[] Magic = "wvpk"u8.ToArray ();

	// Standard sample rate table (index 15 = custom rate stored in metadata sub-block 0x27)
	private static readonly int[] SampleRateTable = [
		6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000,
		32000, 44100, 48000, 64000, 88200, 96000, 192000
	];

	// Metadata sub-block IDs
	private const byte MetaIdSampleRate = 0x27; // Custom sample rate
	private const byte MetaIdChannelInfo = 0x0D; // Multi-channel info

	private byte[] _originalData = Array.Empty<byte> ();
	private string? _sourcePath;
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
	public AudioProperties? Properties { get; private set; }

	/// <summary>APEv2 tag (null if not present)</summary>
	public ApeTag? ApeTag { get; private set; }

	/// <summary>
	/// Parse a WavPack file from byte data.
	/// </summary>
	public static WavPackFileParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < MagicSize)
			return WavPackFileParseResult.Failure ("File too short to contain wvpk magic");

		// Verify magic "wvpk"
		if (!data[..MagicSize].SequenceEqual (Magic))
			return WavPackFileParseResult.Failure ("Invalid magic: expected 'wvpk'");

		if (data.Length < BlockHeaderSize)
			return WavPackFileParseResult.Failure ("File too short for WavPack block header");

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
		if (sampleRateIndex < SampleRateTable.Length)
		{
			file.SampleRate = SampleRateTable[sampleRateIndex];
		}
		else
		{
			// Custom sample rate - parse from metadata sub-block 0x27
			file.SampleRate = ParseCustomSampleRate (data) ?? 44100;
		}

		// Check for multi-channel configuration
		// Bit 10: false stereo (decorrelation disabled)
		// For multi-channel, we need to parse metadata sub-block 0x0D
		var channelCount = ParseMultiChannelCount (data);
		if (channelCount > 0)
		{
			file.Channels = channelCount;
		}

		// Store original data
		file._originalData = data.ToArray ();

		// Calculate audio properties
		file.CalculateProperties ();

		// Parse APEv2 tag at end of file
		file.ParseApeTag (data);

		return WavPackFileParseResult.Success (file);
	}

	private void CalculateProperties ()
	{
		if (SampleRate <= 0 || TotalSamples == 0)
		{
			Properties = null;
			return;
		}

		var durationSeconds = (double)TotalSamples / SampleRate;
		var duration = TimeSpan.FromSeconds (durationSeconds);

		// Calculate bitrate from file size
		var bitrate = 0;
		if (_originalData.Length > 0 && durationSeconds > 0)
		{
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
		if (result.IsSuccess)
		{
			ApeTag = result.Tag;
		}
	}

	/// <summary>
	/// Parse custom sample rate from metadata sub-block 0x27.
	/// Per WavPack spec, when sample rate index is 15, the actual rate is in this sub-block.
	/// </summary>
	private static int? ParseCustomSampleRate (ReadOnlySpan<byte> data)
	{
		// Metadata sub-blocks start after the 32-byte block header
		var offset = BlockHeaderSize;

		// Get block size to know where sub-blocks end
		if (data.Length < 8)
			return null;

		var blockSize = (int)BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4, 4));
		var blockEnd = Math.Min (8 + blockSize, data.Length);

		while (offset + 2 <= blockEnd)
		{
			// Sub-block header: [0] ID, [1] size in words (2 bytes each)
			var subBlockId = data[offset];
			var sizeWords = data[offset + 1];

			// Large sub-block flag (ID bit 7 set means 3-byte size field)
			var hasLargeSize = (subBlockId & 0x80) != 0;
			var actualId = (byte)(subBlockId & 0x3F); // Lower 6 bits are the ID

			int subBlockSize;
			int dataOffset;
			if (hasLargeSize)
			{
				if (offset + 4 > blockEnd)
					break;
				// 3-byte size (24-bit) in words
				subBlockSize = ((data[offset + 1] | (data[offset + 2] << 8) | (data[offset + 3] << 16)) * 2);
				dataOffset = offset + 4;
			}
			else
			{
				subBlockSize = sizeWords * 2;
				dataOffset = offset + 2;
			}

			// Odd size flag (bit 6) indicates last byte is padding
			var hasOddByte = (subBlockId & 0x40) != 0;
			var actualSize = hasOddByte ? subBlockSize - 1 : subBlockSize;

			if (actualId == MetaIdSampleRate && actualSize >= 3)
			{
				// Sample rate stored as 3-byte little-endian value
				if (dataOffset + 3 <= data.Length)
				{
					return data[dataOffset] |
					       (data[dataOffset + 1] << 8) |
					       (data[dataOffset + 2] << 16);
				}
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

		var blockSize = (int)BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4, 4));
		var blockEnd = Math.Min (8 + blockSize, data.Length);

		while (offset + 2 <= blockEnd)
		{
			var subBlockId = data[offset];
			var sizeWords = data[offset + 1];

			var hasLargeSize = (subBlockId & 0x80) != 0;
			var actualId = (byte)(subBlockId & 0x3F);

			int subBlockSize;
			int dataOffset;
			if (hasLargeSize)
			{
				if (offset + 4 > blockEnd)
					break;
				subBlockSize = ((data[offset + 1] | (data[offset + 2] << 8) | (data[offset + 3] << 16)) * 2);
				dataOffset = offset + 4;
			}
			else
			{
				subBlockSize = sizeWords * 2;
				dataOffset = offset + 2;
			}

			if (actualId == MetaIdChannelInfo && subBlockSize >= 1)
			{
				// First byte contains channel count
				if (dataOffset < data.Length)
				{
					return data[dataOffset];
				}
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
		if (ApeTag is not null)
		{
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

	#region File I/O

	/// <summary>
	/// Read a WavPack file from disk.
	/// </summary>
	public static WavPackFileParseResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return WavPackFileParseResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Parse (readResult.Data!);
		if (result.IsSuccess)
		{
			result.File!._sourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Read a WavPack file from disk asynchronously.
	/// </summary>
	public static async Task<WavPackFileParseResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return WavPackFileParseResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Parse (readResult.Data!);
		if (result.IsSuccess)
		{
			result.File!._sourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Save the file to a new path.
	/// </summary>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (_originalData);
		return AtomicFileWriter.Write (path, rendered, fs);
	}

	/// <summary>
	/// Save the file back to its source path.
	/// </summary>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (_sourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFile(path) instead.");

		return SaveToFile (_sourcePath!, fileSystem);
	}

	/// <summary>
	/// Save the file asynchronously.
	/// </summary>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		var rendered = Render (_originalData);
		return await AtomicFileWriter.WriteAsync (path, rendered, fs, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Save the file back to its source path asynchronously.
	/// </summary>
	public async Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (_sourcePath))
			return FileWriteResult.Failure ("No source path available. Use SaveToFileAsync(path) instead.");

		return await SaveToFileAsync (_sourcePath!, fileSystem, cancellationToken).ConfigureAwait (false);
	}

	#endregion
}
