// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents the 4-byte header of a FLAC metadata block.
/// </summary>
/// <remarks>
/// <para>
/// Every FLAC metadata block begins with a 4-byte header:
/// </para>
/// <code>
/// Byte 0: [is_last:1 bit][block_type:7 bits]
/// Bytes 1-3: [data_length:24 bits BE]
/// </code>
/// <para>
/// The is_last flag indicates whether this is the final metadata block before audio data.
/// The data_length specifies the size of the block data following the header.
/// </para>
/// <para>
/// Reference: https://xiph.org/flac/format.html#metadata_block_header
/// </para>
/// </remarks>
public readonly struct FlacMetadataBlockHeader : IEquatable<FlacMetadataBlockHeader>
{
	/// <summary>
	/// The size of the header in bytes.
	/// </summary>
	public const int HeaderSize = 4;

	/// <summary>
	/// Gets a value indicating whether this is the last metadata block before audio data.
	/// </summary>
	public bool IsLast { get; }

	/// <summary>
	/// Gets the type of this metadata block.
	/// </summary>
	public FlacBlockType BlockType { get; }

	/// <summary>
	/// Gets the length of the block data in bytes (not including this header).
	/// </summary>
	/// <remarks>
	/// Maximum value is 16777215 (24 bits = 0xFFFFFF).
	/// </remarks>
	public int DataLength { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="FlacMetadataBlockHeader"/> struct.
	/// </summary>
	/// <param name="isLast">Whether this is the last metadata block.</param>
	/// <param name="blockType">The type of metadata block.</param>
	/// <param name="dataLength">The length of the block data in bytes.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown when <paramref name="dataLength"/> exceeds 24 bits (16777215).
	/// </exception>
	public FlacMetadataBlockHeader (bool isLast, FlacBlockType blockType, int dataLength)
	{
		if (dataLength < 0 || dataLength > 0xFFFFFF)
			throw new ArgumentOutOfRangeException (nameof (dataLength), "Data length must be 0-16777215 (24 bits)");

		IsLast = isLast;
		BlockType = blockType;
		DataLength = dataLength;
	}

	/// <summary>
	/// Attempts to read a metadata block header from binary data.
	/// </summary>
	/// <param name="data">The binary data (must be at least 4 bytes).</param>
	/// <returns>A result indicating success or failure.</returns>
	public static FlacMetadataBlockHeaderReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < HeaderSize)
			return FlacMetadataBlockHeaderReadResult.Failure ("Data too short for FLAC metadata block header");

		// Byte 0: [is_last:1][block_type:7]
		var isLast = (data[0] & 0x80) != 0;
		var blockType = (FlacBlockType)(data[0] & 0x7F);

		// Bytes 1-3: data length (24-bit big-endian)
		var dataLength = (data[1] << 16) | (data[2] << 8) | data[3];

		var header = new FlacMetadataBlockHeader (isLast, blockType, dataLength);
		return FlacMetadataBlockHeaderReadResult.Success (header);
	}

	/// <summary>
	/// Renders the header to binary data.
	/// </summary>
	/// <returns>The 4-byte header data.</returns>
	public BinaryData Render ()
	{
		using var builder = new BinaryDataBuilder (HeaderSize);

		// Byte 0: [is_last:1][block_type:7]
		var firstByte = (byte)((IsLast ? 0x80 : 0x00) | ((byte)BlockType & 0x7F));
		builder.Add (firstByte);

		// Bytes 1-3: data length (24-bit big-endian)
		builder.Add ((byte)((DataLength >> 16) & 0xFF));
		builder.Add ((byte)((DataLength >> 8) & 0xFF));
		builder.Add ((byte)(DataLength & 0xFF));

		return builder.ToBinaryData ();
	}

	/// <inheritdoc/>
	public bool Equals (FlacMetadataBlockHeader other) =>
		IsLast == other.IsLast &&
		BlockType == other.BlockType &&
		DataLength == other.DataLength;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FlacMetadataBlockHeader other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (IsLast, BlockType, DataLength);

	/// <summary>
	/// Determines whether two headers are equal.
	/// </summary>
	public static bool operator == (FlacMetadataBlockHeader left, FlacMetadataBlockHeader right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two headers are not equal.
	/// </summary>
	public static bool operator != (FlacMetadataBlockHeader left, FlacMetadataBlockHeader right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the result of reading a <see cref="FlacMetadataBlockHeader"/> from binary data.
/// </summary>
public readonly struct FlacMetadataBlockHeaderReadResult : IEquatable<FlacMetadataBlockHeaderReadResult>
{
	/// <summary>
	/// Gets the parsed header.
	/// </summary>
	public FlacMetadataBlockHeader Header { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	FlacMetadataBlockHeaderReadResult (FlacMetadataBlockHeader header, bool isSuccess, string? error)
	{
		Header = header;
		IsSuccess = isSuccess;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="header">The parsed header.</param>
	/// <returns>A successful result.</returns>
	public static FlacMetadataBlockHeaderReadResult Success (FlacMetadataBlockHeader header) =>
		new (header, true, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static FlacMetadataBlockHeaderReadResult Failure (string error) =>
		new (default, false, error);

	/// <inheritdoc/>
	public bool Equals (FlacMetadataBlockHeaderReadResult other) =>
		Header.Equals (other.Header) &&
		IsSuccess == other.IsSuccess &&
		Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FlacMetadataBlockHeaderReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Header, IsSuccess, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (FlacMetadataBlockHeaderReadResult left, FlacMetadataBlockHeaderReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (FlacMetadataBlockHeaderReadResult left, FlacMetadataBlockHeaderReadResult right) =>
		!left.Equals (right);
}
