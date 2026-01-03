// DSF Data Chunk parsing
// Third chunk: "data" + chunk_size(8) + audio_data

using System;
using System.Buffers.Binary;

namespace TagLibSharp2.Dsf;

/// <summary>
/// Represents the result of parsing a data chunk.
/// </summary>
public readonly struct DsfDataChunkParseResult : IEquatable<DsfDataChunkParseResult>
{
	/// <summary>
	/// Gets the parsed data chunk, or null if parsing failed.
	/// </summary>
	public DsfDataChunk? Chunk { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Chunk is not null && Error is null;

	private DsfDataChunkParseResult (DsfDataChunk? chunk, string? error)
	{
		Chunk = chunk;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="chunk">The parsed data chunk.</param>
	/// <returns>A successful result containing the chunk.</returns>
	public static DsfDataChunkParseResult Success (DsfDataChunk chunk) => new (chunk, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static DsfDataChunkParseResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (DsfDataChunkParseResult other) =>
		Equals (Chunk, other.Chunk) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is DsfDataChunkParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Chunk, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (DsfDataChunkParseResult left, DsfDataChunkParseResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (DsfDataChunkParseResult left, DsfDataChunkParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the data chunk in a DSF file.
/// Contains the actual DSD audio data.
/// </summary>
public sealed class DsfDataChunk
{
	/// <summary>
	/// Data chunk magic bytes: "data".
	/// </summary>
	public static ReadOnlySpan<byte> Magic => "data"u8;

	/// <summary>
	/// Size of the data chunk header (12 bytes).
	/// </summary>
	public const int HeaderSize = 12;

	/// <summary>
	/// Gets the total chunk size including header and audio data.
	/// </summary>
	public ulong ChunkSize { get; }

	/// <summary>
	/// Gets the size of just the header portion.
	/// </summary>
	public static int HeaderSizeValue => HeaderSize;

	/// <summary>
	/// Gets the size of the audio data (ChunkSize - HeaderSize).
	/// </summary>
	public ulong AudioDataSize => ChunkSize > HeaderSize ? ChunkSize - HeaderSize : 0;

	private DsfDataChunk (ulong chunkSize)
	{
		ChunkSize = chunkSize;
	}

	/// <summary>
	/// Parses a data chunk header from binary data.
	/// </summary>
	public static DsfDataChunkParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < HeaderSize) {
			return DsfDataChunkParseResult.Failure (
				$"Data too short for data chunk header: {data.Length} bytes, need {HeaderSize}");
		}

		// Validate magic bytes
		if (!data[..4].SequenceEqual (Magic)) {
			return DsfDataChunkParseResult.Failure (
				"Invalid data chunk magic bytes: expected 'data'");
		}

		var chunkSize = BinaryPrimitives.ReadUInt64LittleEndian (data[4..]);

		return DsfDataChunkParseResult.Success (new DsfDataChunk (chunkSize));
	}

	/// <summary>
	/// Renders the data chunk header to binary data.
	/// </summary>
	public byte[] RenderHeader ()
	{
		var data = new byte[HeaderSize];
		Magic.CopyTo (data);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), ChunkSize);
		return data;
	}

	/// <summary>
	/// Creates a new data chunk with the specified size.
	/// </summary>
	public static DsfDataChunk Create (ulong totalChunkSize)
	{
		return new DsfDataChunk (totalChunkSize);
	}
}
