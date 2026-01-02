// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents the ASF Stream Properties Object containing stream information.
/// </summary>
/// <remarks>
/// Reference: ASF Specification Section 3.3.
/// For audio streams, contains WAVEFORMATEX structure with codec info.
/// </remarks>
public sealed class AsfStreamProperties
{
	/// <summary>
	/// Minimum size for Stream Properties content (before type-specific data).
	/// </summary>
	public const int MinContentSize = 54;

	/// <summary>
	/// Gets the stream type GUID.
	/// </summary>
	public AsfGuid StreamType { get; }

	/// <summary>
	/// Gets the error correction type GUID.
	/// </summary>
	public AsfGuid ErrorCorrectionType { get; }

	/// <summary>
	/// Gets the time offset in 100-nanosecond units.
	/// </summary>
	public ulong TimeOffset { get; }

	/// <summary>
	/// Gets the stream number.
	/// </summary>
	public int StreamNumber { get; }

	/// <summary>
	/// Gets the sample rate for audio streams.
	/// </summary>
	public uint SampleRate { get; }

	/// <summary>
	/// Gets the number of channels for audio streams.
	/// </summary>
	public int Channels { get; }

	/// <summary>
	/// Gets the bits per sample for audio streams.
	/// </summary>
	public int BitsPerSample { get; }

	/// <summary>
	/// Gets the audio codec ID from WAVEFORMATEX.
	/// </summary>
	public int CodecId { get; }

	/// <summary>
	/// Gets the average bytes per second.
	/// </summary>
	public uint AvgBytesPerSec { get; }

	/// <summary>
	/// Gets the block alignment.
	/// </summary>
	public int BlockAlign { get; }

	/// <summary>
	/// Gets whether this is an audio stream.
	/// </summary>
	public bool IsAudio => StreamType == AsfGuids.AudioMediaType;

	/// <summary>
	/// Gets whether this is a video stream.
	/// </summary>
	public bool IsVideo => StreamType == AsfGuids.VideoMediaType;

	/// <summary>
	/// Gets the codec name based on the codec ID.
	/// </summary>
	public string CodecName => CodecId switch {
		0x0161 => "WMA",
		0x0162 => "WMA Pro",
		0x0163 => "WMA Lossless",
		0x0164 => "WMA Voice",
		_ => $"Unknown (0x{CodecId:X4})"
	};

	AsfStreamProperties (
		AsfGuid streamType,
		AsfGuid errorCorrectionType,
		ulong timeOffset,
		int streamNumber,
		uint sampleRate,
		int channels,
		int bitsPerSample,
		int codecId,
		uint avgBytesPerSec,
		int blockAlign)
	{
		StreamType = streamType;
		ErrorCorrectionType = errorCorrectionType;
		TimeOffset = timeOffset;
		StreamNumber = streamNumber;
		SampleRate = sampleRate;
		Channels = channels;
		BitsPerSample = bitsPerSample;
		CodecId = codecId;
		AvgBytesPerSec = avgBytesPerSec;
		BlockAlign = blockAlign;
	}

	/// <summary>
	/// Parses a Stream Properties Object from binary data.
	/// </summary>
	/// <param name="data">The object content (after GUID and size).</param>
	public static AsfStreamPropertiesParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinContentSize)
			return AsfStreamPropertiesParseResult.Failure ($"Insufficient data: expected at least {MinContentSize} bytes, got {data.Length}");

		var offset = 0;

		// Stream Type GUID
		var streamTypeResult = AsfGuid.Parse (data[offset..]);
		if (!streamTypeResult.IsSuccess)
			return AsfStreamPropertiesParseResult.Failure ($"Failed to parse Stream Type: {streamTypeResult.Error}");
		var streamType = streamTypeResult.Value;
		offset += 16;

		// Error Correction Type GUID
		var errorTypeResult = AsfGuid.Parse (data[offset..]);
		if (!errorTypeResult.IsSuccess)
			return AsfStreamPropertiesParseResult.Failure ($"Failed to parse Error Correction Type: {errorTypeResult.Error}");
		var errorCorrectionType = errorTypeResult.Value;
		offset += 16;

		// Time Offset
		var timeOffset = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		// Type-Specific Data Length
		var typeSpecificLength = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Error Correction Data Length
		var errorCorrectionLength = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Flags (includes stream number in low 7 bits)
		var flags = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
		var streamNumber = flags & 0x7F;
		offset += 2;

		// Reserved
		offset += 4;

		// Initialize audio properties
		uint sampleRate = 0;
		int channels = 0;
		int bitsPerSample = 0;
		int codecId = 0;
		uint avgBytesPerSec = 0;
		int blockAlign = 0;

		// Parse type-specific data for audio streams
		if (streamType == AsfGuids.AudioMediaType && typeSpecificLength >= 18) {
			if (offset + 18 > data.Length)
				return AsfStreamPropertiesParseResult.Failure ("Truncated WAVEFORMATEX data");

			// WAVEFORMATEX structure
			codecId = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;

			channels = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;

			sampleRate = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
			offset += 4;

			avgBytesPerSec = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
			offset += 4;

			blockAlign = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;

			bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;
		}

		var result = new AsfStreamProperties (
			streamType, errorCorrectionType, timeOffset, streamNumber,
			sampleRate, channels, bitsPerSample, codecId, avgBytesPerSec, blockAlign);

		return AsfStreamPropertiesParseResult.Success (result, offset);
	}
}

/// <summary>
/// Result of parsing a Stream Properties Object.
/// </summary>
public readonly struct AsfStreamPropertiesParseResult : IEquatable<AsfStreamPropertiesParseResult>
{
	/// <summary>
	/// Gets the parsed Stream Properties.
	/// </summary>
	public AsfStreamProperties Value { get; }

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

	AsfStreamPropertiesParseResult (AsfStreamProperties value, string? error, int bytesConsumed)
	{
		Value = value;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	public static AsfStreamPropertiesParseResult Success (AsfStreamProperties value, int bytesConsumed)
		=> new (value, null, bytesConsumed);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	public static AsfStreamPropertiesParseResult Failure (string error)
		=> new (null!, error, 0);

	/// <inheritdoc/>
	public bool Equals (AsfStreamPropertiesParseResult other)
		=> BytesConsumed == other.BytesConsumed && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfStreamPropertiesParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode ()
		=> HashCode.Combine (BytesConsumed, Error);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfStreamPropertiesParseResult left, AsfStreamPropertiesParseResult right)
		=> left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfStreamPropertiesParseResult left, AsfStreamPropertiesParseResult right)
		=> !left.Equals (right);
}
