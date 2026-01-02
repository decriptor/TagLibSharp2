// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents the ASF File Properties Object containing file-level information.
/// </summary>
/// <remarks>
/// Reference: ASF Specification Section 3.2.
/// Contains: File ID, file size, creation date, duration, bitrate, etc.
/// </remarks>
public sealed class AsfFileProperties
{
	/// <summary>
	/// Minimum size in bytes for the File Properties Object content.
	/// </summary>
	public const int MinContentSize = 80;

	/// <summary>
	/// Gets the file ID (GUID).
	/// </summary>
	public AsfGuid FileId { get; }

	/// <summary>
	/// Gets the file size in bytes.
	/// </summary>
	public ulong FileSize { get; }

	/// <summary>
	/// Gets the creation date as Windows FILETIME.
	/// </summary>
	public ulong CreationDateFiletime { get; }

	/// <summary>
	/// Gets the number of data packets in the file.
	/// </summary>
	public ulong DataPacketsCount { get; }

	/// <summary>
	/// Gets the play duration in 100-nanosecond units.
	/// </summary>
	public ulong PlayDurationNs { get; }

	/// <summary>
	/// Gets the send duration in 100-nanosecond units.
	/// </summary>
	public ulong SendDurationNs { get; }

	/// <summary>
	/// Gets the preroll time in milliseconds.
	/// </summary>
	public ulong PrerollMs { get; }

	/// <summary>
	/// Gets the file flags.
	/// </summary>
	public uint Flags { get; }

	/// <summary>
	/// Gets the minimum data packet size.
	/// </summary>
	public uint MinPacketSize { get; }

	/// <summary>
	/// Gets the maximum data packet size.
	/// </summary>
	public uint MaxPacketSize { get; }

	/// <summary>
	/// Gets the maximum bitrate in bits per second.
	/// </summary>
	public uint MaxBitrate { get; }

	/// <summary>
	/// Gets the content duration (PlayDuration - Preroll).
	/// </summary>
	public TimeSpan Duration {
		get {
			// Preroll is in ms, PlayDuration is in 100ns units
			var prerollNs = PrerollMs * 10_000;
			var effectiveDurationNs = PlayDurationNs > prerollNs ? PlayDurationNs - prerollNs : 0;
			return TimeSpan.FromTicks ((long)effectiveDurationNs);
		}
	}

	/// <summary>
	/// Gets whether the file is seekable.
	/// </summary>
	public bool IsSeekable => (Flags & 0x02) != 0;

	AsfFileProperties (
		AsfGuid fileId,
		ulong fileSize,
		ulong creationDate,
		ulong dataPackets,
		ulong playDuration,
		ulong sendDuration,
		ulong preroll,
		uint flags,
		uint minPacketSize,
		uint maxPacketSize,
		uint maxBitrate)
	{
		FileId = fileId;
		FileSize = fileSize;
		CreationDateFiletime = creationDate;
		DataPacketsCount = dataPackets;
		PlayDurationNs = playDuration;
		SendDurationNs = sendDuration;
		PrerollMs = preroll;
		Flags = flags;
		MinPacketSize = minPacketSize;
		MaxPacketSize = maxPacketSize;
		MaxBitrate = maxBitrate;
	}

	/// <summary>
	/// Parses a File Properties Object from binary data.
	/// </summary>
	/// <param name="data">The object content (after GUID and size).</param>
	public static AsfFilePropertiesParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinContentSize)
			return AsfFilePropertiesParseResult.Failure ($"Insufficient data: expected {MinContentSize} bytes, got {data.Length}");

		var offset = 0;

		// File ID (GUID)
		var fileIdResult = AsfGuid.Parse (data[offset..]);
		if (!fileIdResult.IsSuccess)
			return AsfFilePropertiesParseResult.Failure ($"Failed to parse File ID: {fileIdResult.Error}");
		var fileId = fileIdResult.Value;
		offset += 16;

		// File size
		var fileSize = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Creation date
		var creationDate = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Data packets count
		var dataPackets = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Play duration
		var playDuration = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Send duration
		var sendDuration = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Preroll
		var preroll = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Flags
		var flags = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Minimum packet size
		var minPacketSize = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Maximum packet size
		var maxPacketSize = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Maximum bitrate
		var maxBitrate = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		var result = new AsfFileProperties (
			fileId, fileSize, creationDate, dataPackets,
			playDuration, sendDuration, preroll,
			flags, minPacketSize, maxPacketSize, maxBitrate);

		return AsfFilePropertiesParseResult.Success (result, offset);
	}
}

/// <summary>
/// Result of parsing a File Properties Object.
/// </summary>
public readonly struct AsfFilePropertiesParseResult : IEquatable<AsfFilePropertiesParseResult>
{
	/// <summary>
	/// Gets the parsed File Properties.
	/// </summary>
	public AsfFileProperties Value { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed during parsing.
	/// </summary>
	public int BytesConsumed { get; }

	/// <summary>
	/// Gets whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Error is null;

	AsfFilePropertiesParseResult (AsfFileProperties value, string? error, int bytesConsumed)
	{
		Value = value;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	public static AsfFilePropertiesParseResult Success (AsfFileProperties value, int bytesConsumed)
		=> new (value, null, bytesConsumed);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	public static AsfFilePropertiesParseResult Failure (string error)
		=> new (null!, error, 0);

	/// <inheritdoc/>
	public bool Equals (AsfFilePropertiesParseResult other)
		=> BytesConsumed == other.BytesConsumed && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfFilePropertiesParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode ()
		=> HashCode.Combine (BytesConsumed, Error);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfFilePropertiesParseResult left, AsfFilePropertiesParseResult right)
		=> left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfFilePropertiesParseResult left, AsfFilePropertiesParseResult right)
		=> !left.Equals (right);
}
