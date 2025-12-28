// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 PRIV (Private) frame for storing application-specific data.
/// </summary>
/// <remarks>
/// PRIV frame format:
/// <code>
/// Offset  Size  Field
/// 0       n     Owner identifier (null-terminated Latin-1 string)
/// n       m     Private data
/// </code>
/// The owner identifier should be a unique identifier like a URL or email address
/// to prevent collision with other applications.
/// </remarks>
public sealed class PrivateFrame
{
	/// <summary>
	/// Gets the frame ID (always "PRIV").
	/// </summary>
	public static string FrameId => "PRIV";

	/// <summary>
	/// Gets or sets the owner identifier.
	/// </summary>
	/// <remarks>
	/// Should be a unique identifier such as a URL (e.g., "http://example.com/app")
	/// or email address to prevent conflicts with other applications.
	/// </remarks>
	public string OwnerId { get; set; }

	/// <summary>
	/// Gets or sets the private data.
	/// </summary>
	public BinaryData Data { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PrivateFrame"/> class.
	/// </summary>
	/// <param name="ownerId">The owner identifier.</param>
	/// <param name="data">The private data.</param>
	public PrivateFrame (string ownerId, BinaryData data)
	{
		OwnerId = ownerId;
		Data = data;
	}

	/// <summary>
	/// Attempts to read a PRIV frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static PrivateFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		_ = version; // Currently unused

		if (data.IsEmpty)
			return PrivateFrameReadResult.Failure ("PRIV frame data is empty");

		// Find the null terminator for owner ID
		var nullIndex = data.IndexOf ((byte)0);
		if (nullIndex < 0)
			return PrivateFrameReadResult.Failure ("PRIV frame missing owner ID terminator");

		var ownerId = Polyfills.Latin1.GetString (data.Slice (0, nullIndex));
		var privateData = data.Slice (nullIndex + 1);

		var frame = new PrivateFrame (ownerId, new BinaryData (privateData.ToArray ()));
		return PrivateFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content.</returns>
	public BinaryData RenderContent ()
	{
		var ownerIdBytes = Polyfills.Latin1.GetBytes (OwnerId);
		var totalSize = ownerIdBytes.Length + 1 + Data.Length;

		using var builder = new BinaryDataBuilder (totalSize);
		builder.Add (ownerIdBytes);
		builder.Add ((byte)0x00);
		builder.Add (Data);

		return builder.ToBinaryData ();
	}
}

/// <summary>
/// Represents the result of reading a PRIV frame.
/// </summary>
public readonly struct PrivateFrameReadResult : IEquatable<PrivateFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public PrivateFrame? Frame { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => Frame is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed.
	/// </summary>
	public int BytesConsumed { get; }

	PrivateFrameReadResult (PrivateFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static PrivateFrameReadResult Success (PrivateFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static PrivateFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (PrivateFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is PrivateFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (PrivateFrameReadResult left, PrivateFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (PrivateFrameReadResult left, PrivateFrameReadResult right) =>
		!left.Equals (right);
}
