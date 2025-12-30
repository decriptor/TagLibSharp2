// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Ogg;

/// <summary>
/// Represents an Ogg page with its header and segment data.
/// </summary>
/// <remarks>
/// <para>
/// An Ogg page is the basic unit of data in an Ogg bitstream. Each page has a 27+ byte header:
/// </para>
/// <code>
/// [magic:4 "OggS"][ver:1][flags:1][granule:8 LE]
/// [serial:4 LE][seq:4 LE][crc:4 LE][seg_count:1]
/// [segment_table:n][data:m]
/// </code>
/// <para>
/// The segment table specifies the sizes of data segments (0-255 bytes each).
/// Packets are reconstructed by concatenating segments until a segment &lt; 255 is encountered.
/// </para>
/// <para>
/// Reference: https://xiph.org/ogg/doc/framing.html
/// </para>
/// </remarks>
public readonly struct OggPage : IEquatable<OggPage>
{
	/// <summary>
	/// The minimum header size in bytes (27 bytes without segment table).
	/// </summary>
	public const int MinHeaderSize = 27;

	static readonly byte[] OggMagic = [(byte)'O', (byte)'g', (byte)'g', (byte)'S'];

	/// <summary>
	/// Gets the Ogg version (always 0).
	/// </summary>
	public byte Version { get; }

	/// <summary>
	/// Gets the page flags.
	/// </summary>
	public OggPageFlags Flags { get; }

	/// <summary>
	/// Gets the granule position (sample count for audio streams).
	/// </summary>
	/// <remarks>
	/// For Vorbis, this is the sample number of the last completed sample on the page.
	/// A value of -1 (0xFFFFFFFFFFFFFFFF) indicates no packets finish on this page.
	/// </remarks>
	public ulong GranulePosition { get; }

	/// <summary>
	/// Gets the serial number identifying the logical bitstream.
	/// </summary>
	public uint SerialNumber { get; }

	/// <summary>
	/// Gets the sequence number of this page within the logical bitstream.
	/// </summary>
	public uint SequenceNumber { get; }

	/// <summary>
	/// Gets the CRC-32 checksum of the page.
	/// </summary>
	public uint Checksum { get; }

	/// <summary>
	/// Gets the page data (concatenated segments).
	/// </summary>
	public ReadOnlyMemory<byte> Data { get; }

	/// <summary>
	/// Gets a value indicating whether this is the first page of a logical bitstream.
	/// </summary>
	public bool IsBeginOfStream => (Flags & OggPageFlags.BeginOfStream) != 0;

	/// <summary>
	/// Gets a value indicating whether this is the last page of a logical bitstream.
	/// </summary>
	public bool IsEndOfStream => (Flags & OggPageFlags.EndOfStream) != 0;

	/// <summary>
	/// Gets a value indicating whether this page continues a packet from the previous page.
	/// </summary>
	public bool IsContinuation => (Flags & OggPageFlags.Continuation) != 0;

	/// <summary>
	/// Initializes a new instance of the <see cref="OggPage"/> struct.
	/// </summary>
	public OggPage (byte version, OggPageFlags flags, ulong granulePosition,
		uint serialNumber, uint sequenceNumber, uint checksum, ReadOnlyMemory<byte> data)
	{
		Version = version;
		Flags = flags;
		GranulePosition = granulePosition;
		SerialNumber = serialNumber;
		SequenceNumber = sequenceNumber;
		Checksum = checksum;
		Data = data;
	}

	/// <summary>
	/// Attempts to read an Ogg page from binary data.
	/// </summary>
	/// <param name="data">The binary data.</param>
	/// <param name="validateCrc">Whether to validate the CRC-32 checksum. Default is false for performance.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static OggPageReadResult Read (ReadOnlySpan<byte> data, bool validateCrc = false)
	{
		if (data.Length < MinHeaderSize)
			return OggPageReadResult.Failure ("Data too short for Ogg page header");

		// Verify magic
		if (data[0] != OggMagic[0] || data[1] != OggMagic[1] ||
			data[2] != OggMagic[2] || data[3] != OggMagic[3])
			return OggPageReadResult.Failure ("Invalid Ogg magic (expected 'OggS')");

		var offset = 4;

		// Version (must be 0)
		var version = data[offset++];
		if (version != 0)
			return OggPageReadResult.Failure ($"Unsupported Ogg version: {version}");

		// Flags
		var flags = (OggPageFlags)data[offset++];

		// Granule position (8 bytes LE)
		var granulePosition = ReadUInt64LE (data.Slice (offset, 8));
		offset += 8;

		// Serial number (4 bytes LE)
		var serialNumber = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		// Sequence number (4 bytes LE)
		var sequenceNumber = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		// CRC (4 bytes LE)
		var checksum = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		// Segment count
		var segmentCount = data[offset++];

		if (offset + segmentCount > data.Length)
			return OggPageReadResult.Failure ("Data too short for segment table");

		// Read segment table and calculate total data size
		var segmentTable = data.Slice (offset, segmentCount);
		var dataSize = 0;
		for (var i = 0; i < segmentCount; i++)
			dataSize += segmentTable[i];

		offset += segmentCount;

		if (offset + dataSize > data.Length)
			return OggPageReadResult.Failure ("Data too short for page data");

		var bytesConsumed = offset + dataSize;

		// Validate CRC if requested
		if (validateCrc) {
			var pageSpan = data.Slice (0, bytesConsumed);
			if (!OggCrc.Validate (pageSpan))
				return OggPageReadResult.Failure ("CRC checksum validation failed");
		}

		// Copy page data
		var pageData = data.Slice (offset, dataSize).ToArray ();

		var page = new OggPage (version, flags, granulePosition, serialNumber,
			sequenceNumber, checksum, pageData);

		return OggPageReadResult.Success (page, bytesConsumed);
	}

	static ulong ReadUInt64LE (ReadOnlySpan<byte> data)
	{
		return (ulong)data[0] |
			((ulong)data[1] << 8) |
			((ulong)data[2] << 16) |
			((ulong)data[3] << 24) |
			((ulong)data[4] << 32) |
			((ulong)data[5] << 40) |
			((ulong)data[6] << 48) |
			((ulong)data[7] << 56);
	}

	static uint ReadUInt32LE (ReadOnlySpan<byte> data)
	{
		return (uint)data[0] |
			((uint)data[1] << 8) |
			((uint)data[2] << 16) |
			((uint)data[3] << 24);
	}

	/// <inheritdoc/>
	public bool Equals (OggPage other) =>
		Version == other.Version &&
		Flags == other.Flags &&
		GranulePosition == other.GranulePosition &&
		SerialNumber == other.SerialNumber &&
		SequenceNumber == other.SequenceNumber &&
		Checksum == other.Checksum &&
		Data.Span.SequenceEqual (other.Data.Span);

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is OggPage other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Version, Flags, GranulePosition, SerialNumber, SequenceNumber, Checksum);

	/// <summary>
	/// Determines whether two pages are equal.
	/// </summary>
	public static bool operator == (OggPage left, OggPage right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two pages are not equal.
	/// </summary>
	public static bool operator != (OggPage left, OggPage right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the result of reading an <see cref="OggPage"/> from binary data.
/// </summary>
public readonly struct OggPageReadResult : IEquatable<OggPageReadResult>
{
	/// <summary>
	/// Gets the parsed page.
	/// </summary>
	public OggPage Page { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	OggPageReadResult (OggPage page, bool isSuccess, string? error, int bytesConsumed)
	{
		Page = page;
		IsSuccess = isSuccess;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="page">The parsed page.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static OggPageReadResult Success (OggPage page, int bytesConsumed) =>
		new (page, true, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static OggPageReadResult Failure (string error) =>
		new (default, false, error, 0);

	/// <inheritdoc/>
	public bool Equals (OggPageReadResult other) =>
		Page.Equals (other.Page) &&
		IsSuccess == other.IsSuccess &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is OggPageReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Page, IsSuccess, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (OggPageReadResult left, OggPageReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (OggPageReadResult left, OggPageReadResult right) =>
		!left.Equals (right);
}
