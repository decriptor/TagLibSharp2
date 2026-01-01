// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Mp4;

/// <summary>
/// Result of parsing a single box from binary data.
/// Contains the parsed box and the number of bytes consumed.
/// </summary>
public readonly struct Mp4BoxReadResult : IEquatable<Mp4BoxReadResult>
{
	/// <summary>
	/// Gets the parsed box, or null if parsing failed.
	/// </summary>
	public Mp4Box? Box { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// This includes the box header and all box data.
	/// </summary>
	public int BytesConsumed { get; }

	/// <summary>
	/// Gets whether the parse was successful.
	/// </summary>
	public bool IsSuccess => Box is not null;

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	public Mp4BoxReadResult (Mp4Box box, int bytesConsumed)
	{
		Box = box;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	public static Mp4BoxReadResult Failure () => default;

	/// <inheritdoc/>
	public override bool Equals (object? obj) => obj is Mp4BoxReadResult other && Equals (other);

	/// <inheritdoc/>
	public bool Equals (Mp4BoxReadResult other) =>
		ReferenceEquals (Box, other.Box) && BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Box, BytesConsumed);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (Mp4BoxReadResult left, Mp4BoxReadResult right) => left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (Mp4BoxReadResult left, Mp4BoxReadResult right) => !left.Equals (right);
}
