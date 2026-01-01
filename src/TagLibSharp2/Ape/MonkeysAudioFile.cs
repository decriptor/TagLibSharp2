// Monkey's Audio (.ape) file support
// Implemented from format specification at wiki.hydrogenaud.io/index.php?title=APE

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TagLibSharp2.Core;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace TagLibSharp2.Ape;

/// <summary>
/// Represents the result of parsing a Monkey's Audio file.
/// </summary>
public readonly struct MonkeysAudioFileParseResult : IEquatable<MonkeysAudioFileParseResult>
{
	public MonkeysAudioFile? File { get; }
	public string? Error { get; }
	public bool IsSuccess => File is not null && Error is null;

	private MonkeysAudioFileParseResult (MonkeysAudioFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	public static MonkeysAudioFileParseResult Success (MonkeysAudioFile file) => new (file, null);
	public static MonkeysAudioFileParseResult Failure (string error) => new (null, error);

	public bool Equals (MonkeysAudioFileParseResult other) =>
		Equals (File, other.File) && Error == other.Error;

	public override bool Equals (object? obj) =>
		obj is MonkeysAudioFileParseResult other && Equals (other);

	public override int GetHashCode () => HashCode.Combine (File, Error);

	public static bool operator == (MonkeysAudioFileParseResult left, MonkeysAudioFileParseResult right) =>
		left.Equals (right);

	public static bool operator != (MonkeysAudioFileParseResult left, MonkeysAudioFileParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents a Monkey's Audio (.ape) file.
/// </summary>
public sealed class MonkeysAudioFile
{
	private const int MagicSize = 4;
	private static readonly byte[] Magic = "MAC "u8.ToArray ();

	// Version threshold for format change
	private const int NewFormatVersion = 3980;

	// Descriptor size for new format (≥3.98)
	private const int DescriptorSize = 52;
	private const int HeaderSize = 24;

	// Minimum sizes
	private const int MinOldFormatSize = 4 + 2 + 26; // Magic + version + old header
	private const int MinNewFormatSize = 4 + DescriptorSize + HeaderSize;

	private byte[] _originalData = Array.Empty<byte> ();
	private string? _sourcePath;
	private IFileSystem? _sourceFileSystem;

	private MonkeysAudioFile () { }

	/// <summary>File format version (e.g., 3990 = 3.99)</summary>
	public int Version { get; private set; }

	/// <summary>Sample rate in Hz</summary>
	public int SampleRate { get; private set; }

	/// <summary>Number of audio channels</summary>
	public int Channels { get; private set; }

	/// <summary>Bits per sample (typically 8, 16, or 24)</summary>
	public int BitsPerSample { get; private set; }

	/// <summary>Total number of audio frames</summary>
	public uint TotalFrames { get; private set; }

	/// <summary>Blocks per frame</summary>
	public uint BlocksPerFrame { get; private set; }

	/// <summary>Blocks in final frame</summary>
	public uint FinalFrameBlocks { get; private set; }

	/// <summary>Compression level (1000=Fast, 2000=Normal, 3000=High, 4000=Extra High, 5000=Insane)</summary>
	public int CompressionLevel { get; private set; }

	/// <summary>Audio properties</summary>
	public AudioProperties? Properties { get; private set; }

	/// <summary>APEv2 tag (null if not present)</summary>
	public ApeTag? ApeTag { get; private set; }

	/// <summary>
	/// Parse a Monkey's Audio file from byte data.
	/// </summary>
	public static MonkeysAudioFileParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < MagicSize)
			return MonkeysAudioFileParseResult.Failure ("File too short to contain MAC magic");

		// Verify magic "MAC "
		if (!data[..MagicSize].SequenceEqual (Magic))
			return MonkeysAudioFileParseResult.Failure ("Invalid magic: expected 'MAC '");

		if (data.Length < MagicSize + 2)
			return MonkeysAudioFileParseResult.Failure ("File too short to contain version");

		var version = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (MagicSize, 2));

		var file = new MonkeysAudioFile { Version = version };

		if (version >= NewFormatVersion)
		{
			// New format with descriptor
			var parseResult = ParseNewFormat (data, file);
			if (!parseResult.IsSuccess)
				return MonkeysAudioFileParseResult.Failure (parseResult.Error!);
		}
		else
		{
			// Old format without descriptor
			var parseResult = ParseOldFormat (data, file);
			if (!parseResult.IsSuccess)
				return MonkeysAudioFileParseResult.Failure (parseResult.Error!);
		}

		// Store original data for calculations and rendering
		file._originalData = data.ToArray ();

		// Calculate audio properties
		file.CalculateProperties ();

		// Parse APEv2 tag at end of file
		file.ParseApeTag (data);

		return MonkeysAudioFileParseResult.Success (file);
	}

	private static ParseResult ParseNewFormat (ReadOnlySpan<byte> data, MonkeysAudioFile file)
	{
		// Minimum size check for new format
		if (data.Length < MinNewFormatSize)
			return ParseResult.Failure ("File too short for new format APE header");

		// APE_DESCRIPTOR layout (starts at byte 0):
		// [0-3]   Magic "MAC "
		// [4-5]   Version
		// [6-7]   Padding
		// [8-11]  nDescriptorBytes (size of descriptor = offset where header starts)
		// [12-15] nHeaderBytes
		// [16-19] nSeekTableBytes
		// [20-23] nWaveHeaderDataBytes
		// [24-27] nAudioDataBytes
		// [28-31] nAudioDataBytesHigh
		// [32-35] nTerminatingDataBytes
		// [36-51] MD5 (16 bytes)

		// Read descriptor size (this is the offset where header begins)
		var descriptorBytes = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (8, 4));

		// Ensure we can read the header
		if (data.Length < (int)descriptorBytes + HeaderSize)
			return ParseResult.Failure ("File too short for APE header");

		// APE_HEADER starts at descriptorBytes offset (absolute from file start)
		var headerStart = (int)descriptorBytes;

		// [2] compression type
		file.CompressionLevel = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (headerStart, 2));

		// [2] format flags (skip)

		// [4] blocks per frame
		file.BlocksPerFrame = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (headerStart + 4, 4));

		// [4] final frame blocks
		file.FinalFrameBlocks = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (headerStart + 8, 4));

		// [4] total frames
		file.TotalFrames = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (headerStart + 12, 4));

		// [2] bits per sample
		file.BitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (headerStart + 16, 2));

		// [2] channels
		file.Channels = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (headerStart + 18, 2));

		// [4] sample rate
		file.SampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (headerStart + 20, 4));

		return ParseResult.Success ();
	}

	private static ParseResult ParseOldFormat (ReadOnlySpan<byte> data, MonkeysAudioFile file)
	{
		if (data.Length < MinOldFormatSize)
			return ParseResult.Failure ("File too short for old format APE header");

		var offset = MagicSize + 2; // After magic and version

		// APE_HEADER_OLD format:
		// [2] compression type
		file.CompressionLevel = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (offset, 2));
		offset += 2;

		// [2] format flags
		offset += 2;

		// [2] channels
		file.Channels = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (offset, 2));
		offset += 2;

		// [4] sample rate
		file.SampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (offset, 4));
		offset += 4;

		// [4] header bytes
		offset += 4;

		// [4] terminating bytes
		offset += 4;

		// [4] total frames
		file.TotalFrames = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (offset, 4));
		offset += 4;

		// [4] final frame blocks
		file.FinalFrameBlocks = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (offset, 4));
		offset += 4;

		// [2] bits per sample
		file.BitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian (data.Slice (offset, 2));

		// Old format has fixed blocks per frame based on version/compression
		file.BlocksPerFrame = 73728; // Default for old format

		return ParseResult.Success ();
	}

	private void CalculateProperties ()
	{
		if (SampleRate <= 0 || TotalFrames == 0)
		{
			Properties = null;
			return;
		}

		// Total samples = (TotalFrames - 1) * BlocksPerFrame + FinalFrameBlocks
		var totalBlocks = (TotalFrames > 0)
			? (ulong)(TotalFrames - 1) * BlocksPerFrame + FinalFrameBlocks
			: 0UL;

		var durationSeconds = (double)totalBlocks / SampleRate;
		var duration = TimeSpan.FromSeconds (durationSeconds);

		// Calculate bitrate from file size if we have original data
		var bitrate = 0;
		if (_originalData.Length > 0 && durationSeconds > 0)
		{
			bitrate = (int)(_originalData.Length * 8 / durationSeconds / 1000);
		}

		// AudioProperties: duration, bitrate, sampleRate, bitsPerSample, channels, codec
		Properties = new AudioProperties (
			duration,
			bitrate,
			SampleRate,
			BitsPerSample,
			Channels,
			"Monkey's Audio"
		);
	}

	private void ParseApeTag (ReadOnlySpan<byte> data)
	{
		// APEv2 tag is at end of file, look for footer
		var result = ApeTag.Parse (data);
		if (result.IsSuccess)
		{
			ApeTag = result.Tag;
		}
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
	/// Read a Monkey's Audio file from disk.
	/// </summary>
	public static MonkeysAudioFileParseResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return MonkeysAudioFileParseResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Parse (readResult.Data!);
		if (result.IsSuccess)
		{
			result.File!._sourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Read a Monkey's Audio file from disk asynchronously.
	/// </summary>
	public static async Task<MonkeysAudioFileParseResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return MonkeysAudioFileParseResult.Failure ($"Failed to read file: {readResult.Error}");

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

	private readonly struct ParseResult
	{
		public bool IsSuccess { get; }
		public string? Error { get; }

		private ParseResult (bool success, string? error)
		{
			IsSuccess = success;
			Error = error;
		}

		public static ParseResult Success () => new (true, null);
		public static ParseResult Failure (string error) => new (false, error);
	}
}
