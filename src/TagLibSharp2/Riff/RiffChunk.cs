// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// Represents a RIFF chunk with a 4-character code (FourCC), size, and data.
/// RIFF chunks are the building blocks of RIFF-based formats (WAV, AVI, etc.).
/// </summary>
/// <remarks>
/// Chunk structure:
/// - Bytes 0-3: FourCC identifier (4 ASCII characters)
/// - Bytes 4-7: Chunk data size (32-bit little-endian, excludes header)
/// - Bytes 8+:  Chunk data (padded to even length)
/// </remarks>
public readonly struct RiffChunk : IEquatable<RiffChunk>
{
	/// <summary>
	/// Size of the chunk header (FourCC + size field).
	/// </summary>
	public const int HeaderSize = 8;

	/// <summary>
	/// Gets the 4-character code identifying the chunk type.
	/// </summary>
	public string FourCC { get; }

	/// <summary>
	/// Gets the chunk data (excluding the 8-byte header).
	/// </summary>
	public BinaryData Data { get; }

	/// <summary>
	/// Gets the size of the data as stored in the chunk header.
	/// </summary>
	public uint DataSize { get; }

	/// <summary>
	/// Gets the total size of the chunk including header and padding.
	/// RIFF chunks are padded to even byte boundaries.
	/// </summary>
	public int TotalSize => HeaderSize + (int)DataSize + ((int)DataSize & 1);

	/// <summary>
	/// Gets whether this chunk represents a valid RIFF chunk.
	/// </summary>
	public bool IsValid => !string.IsNullOrEmpty (FourCC) && FourCC.Length == 4;

	/// <summary>
	/// Creates a new RIFF chunk with the specified FourCC and data.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <param name="data">The chunk data.</param>
	public RiffChunk (string fourCC, BinaryData data)
	{
		FourCC = fourCC;
		Data = data;
		DataSize = (uint)data.Length;
	}

	/// <summary>
	/// Creates a RIFF chunk from parsed values.
	/// </summary>
	RiffChunk (string fourCC, uint dataSize, BinaryData data)
	{
		FourCC = fourCC;
		DataSize = dataSize;
		Data = data;
	}

	/// <summary>
	/// Attempts to parse a RIFF chunk from binary data at the specified offset.
	/// </summary>
	/// <param name="data">The source data containing the chunk.</param>
	/// <param name="offset">The offset where the chunk begins.</param>
	/// <param name="chunk">The parsed chunk if successful.</param>
	/// <returns>True if a valid chunk was parsed; false otherwise.</returns>
	public static bool TryParse (BinaryData data, int offset, out RiffChunk chunk)
	{
		chunk = default;

		if (offset + HeaderSize > data.Length)
			return false;

		// Read FourCC (4 ASCII characters)
		var fourCC = data.Slice (offset, 4).ToStringLatin1 ();
		if (string.IsNullOrEmpty (fourCC) || fourCC.Length != 4)
			return false;

		// Read chunk data size (32-bit little-endian)
		var dataSize = data.ToUInt32LE (offset + 4);

		// Validate that we have enough data
		// Note: We don't require padding byte to be present in source data
		var availableData = data.Length - offset - HeaderSize;
		var actualDataSize = Math.Min ((int)dataSize, availableData);

		if (actualDataSize < 0)
			return false;

		var chunkData = actualDataSize > 0
			? data.Slice (offset + HeaderSize, actualDataSize)
			: BinaryData.Empty;

		chunk = new RiffChunk (fourCC, dataSize, chunkData);
		return true;
	}

	/// <summary>
	/// Renders this chunk to binary data including header and padding.
	/// </summary>
	/// <returns>The complete chunk as binary data.</returns>
	public BinaryData Render ()
	{
		using var builder = new BinaryDataBuilder (TotalSize);

		// FourCC
		builder.AddStringLatin1 (FourCC);

		// Data size (little-endian)
		builder.AddUInt32LE ((uint)Data.Length);

		// Data
		builder.Add (Data);

		// Padding byte if needed
		if ((Data.Length & 1) != 0)
			builder.Add (0);

		return builder.ToBinaryData ();
	}

	/// <inheritdoc />
	public bool Equals (RiffChunk other) =>
		FourCC == other.FourCC && DataSize == other.DataSize && Data.Equals (other.Data);

	/// <inheritdoc />
	public override bool Equals (object? obj) =>
		obj is RiffChunk other && Equals (other);

	/// <inheritdoc />
	public override int GetHashCode () =>
		HashCode.Combine (FourCC, DataSize, Data);

	/// <summary>
	/// Determines whether two chunks are equal.
	/// </summary>
	public static bool operator == (RiffChunk left, RiffChunk right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two chunks are not equal.
	/// </summary>
	public static bool operator != (RiffChunk left, RiffChunk right) =>
		!left.Equals (right);

	/// <inheritdoc />
	public override string ToString () => $"RiffChunk[{FourCC}, {DataSize} bytes]";
}
