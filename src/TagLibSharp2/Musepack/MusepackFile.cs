// Musepack (.mpc, .mp+, .mpp) file support
// Implemented from format specification at wiki.hydrogenaud.io/index.php?title=Musepack

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;

#pragma warning disable CA2000 // Dispose objects before losing scope - factory method pattern

namespace TagLibSharp2.Musepack;

/// <summary>
/// Represents the result of parsing a Musepack file.
/// </summary>
public readonly struct MusepackFileReadResult : IEquatable<MusepackFileReadResult>
{
	/// <summary>
	/// Gets the parsed Musepack file, or null if parsing failed.
	/// </summary>
	public MusepackFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	private MusepackFileReadResult (MusepackFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="file">The parsed Musepack file.</param>
	/// <returns>A successful result containing the file.</returns>
	public static MusepackFileReadResult Success (MusepackFile file) => new (file, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static MusepackFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (MusepackFileReadResult other) =>
		Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is MusepackFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (MusepackFileReadResult left, MusepackFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (MusepackFileReadResult left, MusepackFileReadResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents a Musepack (.mpc, .mp+, .mpp) audio file.
/// Supports both SV7 (MP+) and SV8 (MPCK) stream formats.
/// </summary>
public sealed class MusepackFile : IMediaFile
{
	private bool _disposed;
	private const int MinHeaderSize = 4;

	// SV7 magic: "MP+" followed by version nibble
	private static readonly byte[] SV7Magic = "MP+"u8.ToArray ();

	// SV8 magic: "MPCK"
	private static readonly byte[] SV8Magic = "MPCK"u8.ToArray ();

	// SV7 sample rate table
	private static readonly int[] SampleRateTable = [44100, 48000, 37800, 32000];

	// Samples per frame for SV7
	private const int SamplesPerFrameSV7 = 1152;

	private byte[] _originalData = [];

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	private IFileSystem? _sourceFileSystem;

	private MusepackFile () { }

	/// <summary>Stream version (7 for SV7, 8 for SV8)</summary>
	public int StreamVersion { get; private set; }

	/// <summary>Sample rate in Hz</summary>
	public int SampleRate { get; private set; }

	/// <summary>Number of audio channels</summary>
	public int Channels { get; private set; }

	/// <summary>Frame count (SV7 only)</summary>
	public uint FrameCount { get; private set; }

	/// <summary>Total samples (SV8, or calculated from frame count for SV7)</summary>
	public ulong TotalSamples { get; private set; }

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
	public MediaFormat Format => MediaFormat.Musepack;

	/// <summary>
	/// Parse a Musepack file from byte data.
	/// </summary>
	public static MusepackFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinHeaderSize)
			return MusepackFileReadResult.Failure ("Invalid Musepack file: data too short for magic");

		// Check for SV7 magic "MP+"
		if (data[..3].SequenceEqual (SV7Magic))
			return ParseSV7 (data);

		// Check for SV8 magic "MPCK"
		if (data.Length >= 4 && data[..4].SequenceEqual (SV8Magic))
			return ParseSV8 (data);

		return MusepackFileReadResult.Failure ("Invalid Musepack file: missing magic (expected 'MP+' or 'MPCK')");
	}

	/// <summary>
	/// Parse SV7 format (MP+ magic).
	/// </summary>
	private static MusepackFileReadResult ParseSV7 (ReadOnlySpan<byte> data)
	{
		// SV7 Header structure:
		// [0-2] Magic "MP+"
		// [3] Version (upper 4 bits) | flags (lower 4 bits)
		// [4-7] Frame count (32-bit LE)
		// [8-9] Max level
		// [10-13] Flags with sample rate

		if (data.Length < 16)
			return MusepackFileReadResult.Failure ("Invalid Musepack file: data too short for SV7 header");

		var file = new MusepackFile ();

		// Extract version from byte 3 (upper 4 bits)
		file.StreamVersion = (data[3] >> 4) & 0x0F;

		// Validate version (should be 7 for SV7)
		if (file.StreamVersion < 4 || file.StreamVersion > 7)
			return MusepackFileReadResult.Failure ($"Invalid Musepack file: unsupported SV7 version ({file.StreamVersion})");

		// Frame count (bytes 4-7, LE)
		file.FrameCount = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4, 4));

		// Flags (bytes 10-13, LE) - contains sample rate index in upper 2 bits
		var flags = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (10, 4));

		// Sample rate index is in bits 30-31
		var sampleRateIndex = (int)((flags >> 30) & 0x3);
		file.SampleRate = SampleRateTable[sampleRateIndex];

		// Channels: SV7 is always stereo unless mid-side only flag set
		// For simplicity, default to 2 channels
		file.Channels = 2;

		// Calculate total samples from frame count
		file.TotalSamples = (ulong)file.FrameCount * SamplesPerFrameSV7;

		// Store original data
		file._originalData = data.ToArray ();

		// Calculate audio properties
		file.CalculateProperties ();

		// Parse APEv2 tag at end of file
		file.ParseApeTag (data);

		return MusepackFileReadResult.Success (file);
	}

	/// <summary>
	/// Parse SV8 format (MPCK magic).
	/// </summary>
	private static MusepackFileReadResult ParseSV8 (ReadOnlySpan<byte> data)
	{
		// SV8 is packet-based format
		// Magic "MPCK" followed by packets
		// We need to find the SH (Stream Header) packet

		if (data.Length < 8)
			return MusepackFileReadResult.Failure ("Invalid Musepack file: data too short for SV8 header");

		var file = new MusepackFile {
			StreamVersion = 8
		};

		// Parse packets starting after magic
		var offset = 4;
		while (offset + 3 <= data.Length) {
			// Packet key (2 bytes)
			var key = data.Slice (offset, 2);
			offset += 2;

			// Packet size (variable-length integer)
			var sizeResult = ReadVarInt (data, offset);
			if (!sizeResult.Success)
				break;

			offset = sizeResult.NewOffset;
			var packetSize = (int)sizeResult.Value;

			// Validate packet size
			if (packetSize < 3 || offset + packetSize - 3 > data.Length)
				break;

			// Parse SH (Stream Header) packet
			if (key[0] == 'S' && key[1] == 'H') {
				var payloadSize = packetSize - 3; // Size includes key(2) + size byte(1+)
				if (payloadSize > 0 && offset + payloadSize <= data.Length) {
					ParseSV8StreamHeader (data.Slice (offset, payloadSize), file);
				}
				break; // Found what we need
			}

			// Skip to next packet
			offset += packetSize - 3;
		}

		// Defaults if SH packet not found
		if (file.SampleRate == 0)
			file.SampleRate = 44100;
		if (file.Channels == 0)
			file.Channels = 2;

		// Store original data
		file._originalData = data.ToArray ();

		// Calculate audio properties
		file.CalculateProperties ();

		// Parse APEv2 tag at end of file
		file.ParseApeTag (data);

		return MusepackFileReadResult.Success (file);
	}

	/// <summary>
	/// Attempts to read a Musepack file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out MusepackFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read a Musepack file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out MusepackFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid Musepack file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with "MPCK" (SV8) or "MP+" (SV7) magic bytes.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 3 bytes for minimum magic (MP+)
		if (data.Length < 3)
			return false;

		// Check for SV8 magic "MPCK"
		if (data.Length >= 4 && data[0] == 'M' && data[1] == 'P' && data[2] == 'C' && data[3] == 'K')
			return true;

		// Check for SV7 magic "MP+"
		return data[0] == 'M' && data[1] == 'P' && data[2] == '+';
	}

	/// <summary>
	/// Parse SV8 Stream Header (SH) packet payload.
	/// </summary>
	private static void ParseSV8StreamHeader (ReadOnlySpan<byte> payload, MusepackFile file)
	{
		if (payload.IsEmpty)
			return;

		var offset = 0;

		// Skip CRC32 (4 bytes) + Stream version (1 byte)
		// Per Musepack SV8 spec, SH payload starts with these 5 bytes
		if (payload.Length < 5)
			return;
		offset += 5;

		// Sample count (variable-length integer)
		var sampleCountResult = ReadVarInt (payload, offset);
		if (sampleCountResult.Success) {
			file.TotalSamples = sampleCountResult.Value;
			offset = sampleCountResult.NewOffset;
		} else {
			return;
		}

		// Beginning silence (variable-length integer)
		var silenceResult = ReadVarInt (payload, offset);
		if (silenceResult.Success) {
			offset = silenceResult.NewOffset;
		} else {
			return;
		}

		// Sample frequency index (next byte, bits 2-0)
		if (offset >= payload.Length) return;
		var sampleRateIndex = payload[offset] & 0x07;
		file.SampleRate = sampleRateIndex switch {
			0 => 44100,
			1 => 48000,
			2 => 37800,
			3 => 32000,
			_ => 44100
		};
		offset++;

		// Channels (next byte + 1)
		if (offset < payload.Length) {
			file.Channels = (payload[offset] & 0x0F) + 1;
		} else {
			file.Channels = 2;
		}
	}

	/// <summary>
	/// Read a variable-length integer from the data.
	/// </summary>
	private static (bool Success, ulong Value, int NewOffset) ReadVarInt (ReadOnlySpan<byte> data, int offset)
	{
		if (offset >= data.Length)
			return (false, 0, offset);

		ulong value = 0;
		int bytesRead = 0;
		const int maxBytes = 9; // Prevent infinite loop

		while (offset < data.Length && bytesRead < maxBytes) {
			var b = data[offset++];
			bytesRead++;

			// Shift existing value and add lower 7 bits
			value = (value << 7) | (uint)(b & 0x7F);

			// If high bit not set, this is the last byte
			if ((b & 0x80) == 0)
				return (true, value, offset);
		}

		return (false, value, offset);
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
			16, // Musepack doesn't expose bits per sample directly, assume 16
			Channels,
			"Musepack"
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
		var tagSize = (long)footer.TagSize;

		// Check if there's a header
		var hasHeader = footer.HasHeader;
		var totalTagSize = hasHeader ? tagSize + ApeTagFooter.Size : tagSize;

		if (totalTagSize > data.Length)
			return data.Length;

		return data.Length - (int)totalTagSize;
	}

	// ===== File I/O =====

	/// <summary>
	/// Read a Musepack file from disk.
	/// </summary>
	public static MusepackFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return MusepackFileReadResult.Failure ($"Failed to read file: {readResult.Error}");

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Read a Musepack file from disk asynchronously.
	/// </summary>
	public static async Task<MusepackFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return MusepackFileReadResult.Failure ($"Failed to read file: {readResult.Error}");

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
		_originalData = [];
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
	}
}
