// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents a 128-bit GUID used for ASF object identification.
/// </summary>
/// <remarks>
/// ASF GUIDs are stored in a mixed-endian format:
/// - Data1 (4 bytes): little-endian
/// - Data2 (2 bytes): little-endian
/// - Data3 (2 bytes): little-endian
/// - Data4 (8 bytes): big-endian (byte array)
/// </remarks>
public readonly struct AsfGuid : IEquatable<AsfGuid>
{
	/// <summary>
	/// Size of an ASF GUID in bytes.
	/// </summary>
	public const int Size = 16;

	readonly ulong _a; // First 8 bytes
	readonly ulong _b; // Last 8 bytes

	/// <summary>
	/// Creates an AsfGuid from raw bytes.
	/// </summary>
	/// <param name="a">First 8 bytes as ulong.</param>
	/// <param name="b">Last 8 bytes as ulong.</param>
	AsfGuid (ulong a, ulong b)
	{
		_a = a;
		_b = b;
	}

	/// <summary>
	/// Parses an AsfGuid from 16 bytes.
	/// </summary>
	/// <param name="data">The byte span to parse from.</param>
	/// <returns>A result containing the parsed GUID or an error.</returns>
	public static AsfGuidParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < Size)
			return AsfGuidParseResult.Failure ($"Insufficient data for GUID: need {Size} bytes, got {data.Length}");

		// Read as two 64-bit values for efficient comparison
		var a = BinaryPrimitives.ReadUInt64LittleEndian (data);
		var b = BinaryPrimitives.ReadUInt64LittleEndian (data.Slice (8));

		return AsfGuidParseResult.Success (new AsfGuid (a, b), Size);
	}

	/// <summary>
	/// Renders this GUID to binary data.
	/// </summary>
	/// <returns>A 16-byte BinaryData containing the GUID.</returns>
	public BinaryData Render ()
	{
		var bytes = new byte[Size];
		BinaryPrimitives.WriteUInt64LittleEndian (bytes, _a);
		BinaryPrimitives.WriteUInt64LittleEndian (bytes.AsSpan (8), _b);
		return new BinaryData (bytes);
	}

	/// <inheritdoc />
	public bool Equals (AsfGuid other)
	{
		return _a == other._a && _b == other._b;
	}

	/// <inheritdoc />
	public override bool Equals (object? obj)
	{
		return obj is AsfGuid other && Equals (other);
	}

	/// <inheritdoc />
	public override int GetHashCode ()
	{
		return HashCode.Combine (_a, _b);
	}

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfGuid left, AsfGuid right)
	{
		return left.Equals (right);
	}

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfGuid left, AsfGuid right)
	{
		return !left.Equals (right);
	}
}

/// <summary>
/// Result type for AsfGuid parsing operations.
/// </summary>
public readonly struct AsfGuidParseResult : IEquatable<AsfGuidParseResult>
{
	/// <summary>
	/// Gets the parsed GUID if successful.
	/// </summary>
	public AsfGuid Value { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed during parsing.
	/// </summary>
	public int BytesConsumed { get; }

	/// <summary>
	/// Gets whether the parse operation was successful.
	/// </summary>
	public bool IsSuccess => Error is null;

	AsfGuidParseResult (AsfGuid value, string? error, int bytesConsumed)
	{
		Value = value;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	public static AsfGuidParseResult Success (AsfGuid value, int bytesConsumed)
	{
		return new AsfGuidParseResult (value, null, bytesConsumed);
	}

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	public static AsfGuidParseResult Failure (string error)
	{
		return new AsfGuidParseResult (default, error, 0);
	}

	/// <inheritdoc />
	public bool Equals (AsfGuidParseResult other)
	{
		return Value.Equals (other.Value) && Error == other.Error && BytesConsumed == other.BytesConsumed;
	}

	/// <inheritdoc />
	public override bool Equals (object? obj)
	{
		return obj is AsfGuidParseResult other && Equals (other);
	}

	/// <inheritdoc />
	public override int GetHashCode ()
	{
		return HashCode.Combine (Value, Error, BytesConsumed);
	}

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfGuidParseResult left, AsfGuidParseResult right)
	{
		return left.Equals (right);
	}

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfGuidParseResult left, AsfGuidParseResult right)
	{
		return !left.Equals (right);
	}
}
