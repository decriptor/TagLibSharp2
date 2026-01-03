// DSF DSD Chunk parsing
// First chunk in DSF file: "DSD " + chunk_size(8) + file_size(8) + metadata_offset(8)

using System;
using System.Buffers.Binary;

namespace TagLibSharp2.Dsf;

/// <summary>
/// Represents the result of parsing a DSD chunk.
/// </summary>
public readonly struct DsfDsdChunkParseResult : IEquatable<DsfDsdChunkParseResult>
{
	/// <summary>
	/// Gets the parsed DSD chunk, or null if parsing failed.
	/// </summary>
	public DsfDsdChunk? Chunk { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Chunk is not null && Error is null;

	private DsfDsdChunkParseResult (DsfDsdChunk? chunk, string? error)
	{
		Chunk = chunk;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="chunk">The parsed DSD chunk.</param>
	/// <returns>A successful result containing the chunk.</returns>
	public static DsfDsdChunkParseResult Success (DsfDsdChunk chunk) => new (chunk, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static DsfDsdChunkParseResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (DsfDsdChunkParseResult other) =>
		Equals (Chunk, other.Chunk) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is DsfDsdChunkParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Chunk, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (DsfDsdChunkParseResult left, DsfDsdChunkParseResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (DsfDsdChunkParseResult left, DsfDsdChunkParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the DSD chunk header in a DSF file.
/// This is always the first chunk and contains file-level metadata.
/// </summary>
public sealed class DsfDsdChunk
{
	/// <summary>
	/// DSD chunk magic bytes: "DSD ".
	/// </summary>
	public static ReadOnlySpan<byte> Magic => "DSD "u8;

	/// <summary>
	/// Standard size of the DSD chunk (28 bytes).
	/// </summary>
	public const int Size = 28;

	/// <summary>
	/// Gets the chunk size (should always be 28 for DSD chunk).
	/// </summary>
	public ulong ChunkSize { get; }

	/// <summary>
	/// Gets the total file size in bytes.
	/// </summary>
	public ulong FileSize { get; }

	/// <summary>
	/// Gets the offset to ID3v2 metadata, or 0 if no metadata.
	/// </summary>
	public ulong MetadataOffset { get; }

	/// <summary>
	/// Gets whether the file contains ID3v2 metadata.
	/// </summary>
	public bool HasMetadata => MetadataOffset > 0;

	private DsfDsdChunk (ulong chunkSize, ulong fileSize, ulong metadataOffset)
	{
		ChunkSize = chunkSize;
		FileSize = fileSize;
		MetadataOffset = metadataOffset;
	}

	/// <summary>
	/// Parses a DSD chunk from binary data.
	/// </summary>
	public static DsfDsdChunkParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < Size) {
			return DsfDsdChunkParseResult.Failure (
				$"Data too short for DSD chunk: {data.Length} bytes, need {Size}");
		}

		// Validate magic bytes
		if (!data[..4].SequenceEqual (Magic)) {
			return DsfDsdChunkParseResult.Failure (
				"Invalid DSD chunk magic bytes: expected 'DSD '");
		}

		var chunkSize = BinaryPrimitives.ReadUInt64LittleEndian (data[4..]);
		var fileSize = BinaryPrimitives.ReadUInt64LittleEndian (data[12..]);
		var metadataOffset = BinaryPrimitives.ReadUInt64LittleEndian (data[20..]);

		// Basic validation
		if (chunkSize != Size) {
			// Some implementations may have different chunk sizes
			// We'll accept it but log it could be non-standard
		}

		return DsfDsdChunkParseResult.Success (
			new DsfDsdChunk (chunkSize, fileSize, metadataOffset));
	}

	/// <summary>
	/// Renders the DSD chunk to binary data.
	/// </summary>
	public byte[] Render ()
	{
		var data = new byte[Size];
		Magic.CopyTo (data);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), ChunkSize);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (12), FileSize);
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (20), MetadataOffset);
		return data;
	}

	/// <summary>
	/// Creates a new DSD chunk with the specified properties.
	/// </summary>
	public static DsfDsdChunk Create (ulong fileSize, ulong metadataOffset = 0)
	{
		return new DsfDsdChunk (Size, fileSize, metadataOffset);
	}
}
