// DSF Format Chunk parsing
// Second chunk: "fmt " + chunk_size(8) + format fields

using System;
using System.Buffers.Binary;

namespace TagLibSharp2.Dsf;

/// <summary>
/// Represents the result of parsing a format chunk.
/// </summary>
public readonly struct DsfFmtChunkParseResult : IEquatable<DsfFmtChunkParseResult>
{
	/// <summary>
	/// Gets the parsed format chunk, or null if parsing failed.
	/// </summary>
	public DsfFmtChunk? Chunk { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Chunk is not null && Error is null;

	private DsfFmtChunkParseResult (DsfFmtChunk? chunk, string? error)
	{
		Chunk = chunk;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="chunk">The parsed format chunk.</param>
	/// <returns>A successful result containing the chunk.</returns>
	public static DsfFmtChunkParseResult Success (DsfFmtChunk chunk) => new (chunk, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static DsfFmtChunkParseResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (DsfFmtChunkParseResult other) =>
		Equals (Chunk, other.Chunk) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is DsfFmtChunkParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Chunk, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (DsfFmtChunkParseResult left, DsfFmtChunkParseResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (DsfFmtChunkParseResult left, DsfFmtChunkParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the format chunk in a DSF file.
/// Contains audio format information.
/// </summary>
public sealed class DsfFmtChunk
{
	/// <summary>
	/// Format chunk magic bytes: "fmt ".
	/// </summary>
	public static ReadOnlySpan<byte> Magic => "fmt "u8;

	/// <summary>
	/// Standard size of the format chunk (52 bytes).
	/// </summary>
	public const int Size = 52;

	/// <summary>
	/// Gets the chunk size.
	/// </summary>
	public ulong ChunkSize { get; }

	/// <summary>
	/// Gets the format version (typically 1).
	/// </summary>
	public uint FormatVersion { get; }

	/// <summary>
	/// Gets the format ID (0 = DSD raw).
	/// </summary>
	public uint FormatId { get; }

	/// <summary>
	/// Gets the channel type identifier.
	/// </summary>
	public DsfChannelType ChannelType { get; }

	/// <summary>
	/// Gets the number of audio channels.
	/// </summary>
	public uint ChannelCount { get; }

	/// <summary>
	/// Gets the sample rate in Hz (e.g., 2822400 for DSD64).
	/// </summary>
	public uint SampleRate { get; }

	/// <summary>
	/// Gets the bits per sample (always 1 for DSD).
	/// </summary>
	public uint BitsPerSample { get; }

	/// <summary>
	/// Gets the total number of samples per channel.
	/// </summary>
	public ulong SampleCount { get; }

	/// <summary>
	/// Gets the block size per channel in bytes.
	/// </summary>
	public uint BlockSizePerChannel { get; }

	/// <summary>
	/// Gets the DSD rate classification based on sample rate.
	/// </summary>
	public DsfSampleRate DsdRate {
		get {
			return SampleRate switch {
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
	/// Gets the audio duration.
	/// </summary>
	public TimeSpan Duration {
		get {
			if (SampleRate == 0)
				return TimeSpan.Zero;

			var seconds = (double)SampleCount / SampleRate;
			return TimeSpan.FromSeconds (seconds);
		}
	}

	private DsfFmtChunk (
		ulong chunkSize,
		uint formatVersion,
		uint formatId,
		DsfChannelType channelType,
		uint channelCount,
		uint sampleRate,
		uint bitsPerSample,
		ulong sampleCount,
		uint blockSizePerChannel)
	{
		ChunkSize = chunkSize;
		FormatVersion = formatVersion;
		FormatId = formatId;
		ChannelType = channelType;
		ChannelCount = channelCount;
		SampleRate = sampleRate;
		BitsPerSample = bitsPerSample;
		SampleCount = sampleCount;
		BlockSizePerChannel = blockSizePerChannel;
	}

	/// <summary>
	/// Parses a format chunk from binary data.
	/// </summary>
	public static DsfFmtChunkParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < Size) {
			return DsfFmtChunkParseResult.Failure (
				$"Data too short for format chunk: {data.Length} bytes, need {Size}");
		}

		// Validate magic bytes
		if (!data[..4].SequenceEqual (Magic)) {
			return DsfFmtChunkParseResult.Failure (
				"Invalid format chunk magic bytes: expected 'fmt '");
		}

		var chunkSize = BinaryPrimitives.ReadUInt64LittleEndian (data[4..]);
		var formatVersion = BinaryPrimitives.ReadUInt32LittleEndian (data[12..]);
		var formatId = BinaryPrimitives.ReadUInt32LittleEndian (data[16..]);
		var channelType = BinaryPrimitives.ReadUInt32LittleEndian (data[20..]);
		var channelCount = BinaryPrimitives.ReadUInt32LittleEndian (data[24..]);
		var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian (data[28..]);
		var bitsPerSample = BinaryPrimitives.ReadUInt32LittleEndian (data[32..]);
		var sampleCount = BinaryPrimitives.ReadUInt64LittleEndian (data[36..]);
		var blockSizePerChannel = BinaryPrimitives.ReadUInt32LittleEndian (data[44..]);
		// Reserved 4 bytes at offset 48

		// Validate DSD requirements
		if (bitsPerSample != 1) {
			return DsfFmtChunkParseResult.Failure (
				$"DSD requires 1 bit per sample, got {bitsPerSample}");
		}

		if (formatId != 0) {
			return DsfFmtChunkParseResult.Failure (
				$"Unknown format ID: {formatId}");
		}

		return DsfFmtChunkParseResult.Success (
			new DsfFmtChunk (
				chunkSize,
				formatVersion,
				formatId,
				(DsfChannelType)channelType,
				channelCount,
				sampleRate,
				bitsPerSample,
				sampleCount,
				blockSizePerChannel));
	}

	/// <summary>
	/// Renders the format chunk to binary data.
	/// </summary>
	public byte[] Render ()
	{
		var data = new byte[Size];
		Magic.CopyTo (data);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), ChunkSize);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (12), FormatVersion);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (16), FormatId);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (20), (uint)ChannelType);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (24), ChannelCount);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (28), SampleRate);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (32), BitsPerSample);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (36), SampleCount);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (44), BlockSizePerChannel);
		// Reserved bytes 48-51 stay zero
		return data;
	}

	/// <summary>
	/// Creates a new format chunk.
	/// </summary>
	public static DsfFmtChunk Create (
		uint channelCount,
		uint sampleRate,
		ulong sampleCount,
		uint blockSizePerChannel = 4096)
	{
		var channelType = channelCount switch {
			1 => DsfChannelType.Mono,
			2 => DsfChannelType.Stereo,
			6 => DsfChannelType.Surround51,
			_ => (DsfChannelType)channelCount
		};

		return new DsfFmtChunk (
			Size,
			1, // format version
			0, // DSD raw
			channelType,
			channelCount,
			sampleRate,
			1, // bits per sample (always 1 for DSD)
			sampleCount,
			blockSizePerChannel);
	}
}
