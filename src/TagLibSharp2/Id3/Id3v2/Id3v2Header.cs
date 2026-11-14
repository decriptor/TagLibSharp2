// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2;

/// <summary>
/// Represents an ID3v2 tag header (10 bytes).
/// </summary>
/// <remarks>
/// Header format:
/// <code>
/// Offset  Size  Field
/// 0       3     "ID3" magic
/// 3       1     Major version (2, 3, or 4)
/// 4       1     Minor version (revision)
/// 5       1     Flags
/// 6       4     Size (syncsafe integer, excludes header/footer)
/// </code>
/// </remarks>
public readonly struct Id3v2Header : IEquatable<Id3v2Header>
{
	/// <summary>
	/// The size of an ID3v2 header in bytes.
	/// </summary>
	public const int HeaderSize = 10;

	/// <summary>
	/// The size of an ID3v2 footer in bytes (same as header).
	/// </summary>
	public const int FooterSize = 10;

	/// <summary>
	/// Gets the major version (2, 3, or 4).
	/// </summary>
	public byte MajorVersion { get; }

	/// <summary>
	/// Gets the minor version (revision number).
	/// </summary>
	public byte MinorVersion { get; }

	/// <summary>
	/// Gets the header flags.
	/// </summary>
	public Id3v2HeaderFlags Flags { get; }

	/// <summary>
	/// Gets the size of the tag data (excludes header and footer).
	/// </summary>
	public uint TagSize { get; }

	/// <summary>
	/// Gets a value indicating whether unsynchronization is applied.
	/// </summary>
	public bool IsUnsynchronized => Flags.HasFlag (Id3v2HeaderFlags.Unsynchronization);

	/// <summary>
	/// Gets a value indicating whether an extended header is present.
	/// </summary>
	public bool HasExtendedHeader => Flags.HasFlag (Id3v2HeaderFlags.ExtendedHeader);

	/// <summary>
	/// Gets a value indicating whether this tag is experimental.
	/// </summary>
	public bool IsExperimental => Flags.HasFlag (Id3v2HeaderFlags.Experimental);

	/// <summary>
	/// Gets a value indicating whether a footer is present (v2.4 only).
	/// </summary>
	public bool HasFooter => MajorVersion == 4 && Flags.HasFlag (Id3v2HeaderFlags.Footer);

	/// <summary>
	/// Gets the total size of the tag including header and optional footer.
	/// </summary>
	public uint TotalSize => (uint)HeaderSize + TagSize + (HasFooter ? (uint)FooterSize : 0u);

	/// <summary>
	/// Initializes a new instance of the <see cref="Id3v2Header"/> struct.
	/// </summary>
	/// <param name="majorVersion">The major version.</param>
	/// <param name="minorVersion">The minor version.</param>
	/// <param name="flags">The header flags.</param>
	/// <param name="tagSize">The tag data size.</param>
	public Id3v2Header (byte majorVersion, byte minorVersion, Id3v2HeaderFlags flags, uint tagSize)
	{
		MajorVersion = majorVersion;
		MinorVersion = minorVersion;
		Flags = flags;
		TagSize = tagSize;
	}

	/// <summary>
	/// Attempts to read an ID3v2 header from the provided data.
	/// </summary>
	/// <param name="data">The data to parse (must be at least 10 bytes).</param>
	/// <returns>A result indicating success, failure, or not found.</returns>
	public static Id3v2HeaderReadResult Read (ReadOnlySpan<byte> data)
	{
		// Check minimum length
		if (data.Length < HeaderSize)
			return Id3v2HeaderReadResult.Failure ("Data is too short for ID3v2 header");

		// Check for "ID3" magic
		if (data[0] != 'I' || data[1] != 'D' || data[2] != '3')
			return Id3v2HeaderReadResult.NotFound ();

		var majorVersion = data[3];
		var minorVersion = data[4];
		var flags = (Id3v2HeaderFlags)data[5];

		// Validate version (only 2.2, 2.3, 2.4 are valid)
		if (majorVersion < 2 || majorVersion > 4)
			return Id3v2HeaderReadResult.Failure ($"Unsupported ID3v2 version: 2.{majorVersion}");

		// Validate syncsafe size bytes (MSB must be 0)
		if ((data[6] & 0x80) != 0 || (data[7] & 0x80) != 0 ||
			(data[8] & 0x80) != 0 || (data[9] & 0x80) != 0)
			return Id3v2HeaderReadResult.Failure ("Invalid syncsafe integer in size field");

		// Decode syncsafe integer
		var tagSize = DecodeSyncsafe (data.Slice (6, 4));

		var header = new Id3v2Header (majorVersion, minorVersion, flags, tagSize);
		return Id3v2HeaderReadResult.Success (header, HeaderSize);
	}

	/// <summary>
	/// Renders this header to binary data.
	/// </summary>
	/// <returns>A 10-byte header.</returns>
	public BinaryData Render ()
	{
		using var builder = new BinaryDataBuilder (HeaderSize);

		// Magic "ID3"
		builder.Add ((byte)'I');
		builder.Add ((byte)'D');
		builder.Add ((byte)'3');

		// Version
		builder.Add (MajorVersion);
		builder.Add (MinorVersion);

		// Flags
		builder.Add ((byte)Flags);

		// Size as syncsafe integer
		builder.Add ((byte)((TagSize >> 21) & 0x7F));
		builder.Add ((byte)((TagSize >> 14) & 0x7F));
		builder.Add ((byte)((TagSize >> 7) & 0x7F));
		builder.Add ((byte)(TagSize & 0x7F));

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Renders this header as a footer (ID3v2.4 only).
	/// </summary>
	/// <returns>A 10-byte footer with "3DI" magic.</returns>
	/// <remarks>
	/// The footer is identical to the header except the magic bytes are "3DI" instead of "ID3".
	/// </remarks>
	public BinaryData RenderFooter ()
	{
		using var builder = new BinaryDataBuilder (FooterSize);

		// Magic "3DI" (reverse of "ID3")
		builder.Add ((byte)'3');
		builder.Add ((byte)'D');
		builder.Add ((byte)'I');

		// Version
		builder.Add (MajorVersion);
		builder.Add (MinorVersion);

		// Flags
		builder.Add ((byte)Flags);

		// Size as syncsafe integer
		builder.Add ((byte)((TagSize >> 21) & 0x7F));
		builder.Add ((byte)((TagSize >> 14) & 0x7F));
		builder.Add ((byte)((TagSize >> 7) & 0x7F));
		builder.Add ((byte)(TagSize & 0x7F));

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Attempts to read an ID3v2 footer from the provided data.
	/// </summary>
	/// <param name="data">The data to parse (must be at least 10 bytes).</param>
	/// <returns>A result indicating success, failure, or not found.</returns>
	/// <remarks>
	/// Footers are only valid in ID3v2.4. The footer has "3DI" magic instead of "ID3".
	/// </remarks>
	public static Id3v2HeaderReadResult ReadFooter (ReadOnlySpan<byte> data)
	{
		// Check minimum length
		if (data.Length < FooterSize)
			return Id3v2HeaderReadResult.Failure ("Data is too short for ID3v2 footer");

		// Check for "3DI" magic (reverse of "ID3")
		if (data[0] != '3' || data[1] != 'D' || data[2] != 'I')
			return Id3v2HeaderReadResult.NotFound ();

		var majorVersion = data[3];
		var minorVersion = data[4];
		var flags = (Id3v2HeaderFlags)data[5];

		// Footer is only valid for ID3v2.4
		if (majorVersion != 4)
			return Id3v2HeaderReadResult.Failure ($"Footer only valid in ID3v2.4, found version 2.{majorVersion}");

		// Validate syncsafe size bytes (MSB must be 0)
		if ((data[6] & 0x80) != 0 || (data[7] & 0x80) != 0 ||
			(data[8] & 0x80) != 0 || (data[9] & 0x80) != 0)
			return Id3v2HeaderReadResult.Failure ("Invalid syncsafe integer in size field");

		// Decode syncsafe integer
		var tagSize = DecodeSyncsafe (data.Slice (6, 4));

		var header = new Id3v2Header (majorVersion, minorVersion, flags, tagSize);
		return Id3v2HeaderReadResult.Success (header, FooterSize);
	}

	/// <summary>
	/// Decodes a 4-byte syncsafe integer.
	/// </summary>
	static uint DecodeSyncsafe (ReadOnlySpan<byte> data) =>
		((uint)data[0] << 21) |
		((uint)data[1] << 14) |
		((uint)data[2] << 7) |
		data[3];

	/// <inheritdoc/>
	public bool Equals (Id3v2Header other) =>
		MajorVersion == other.MajorVersion &&
		MinorVersion == other.MinorVersion &&
		Flags == other.Flags &&
		TagSize == other.TagSize;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is Id3v2Header other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (MajorVersion, MinorVersion, Flags, TagSize);

	/// <summary>
	/// Determines whether two headers are equal.
	/// </summary>
	public static bool operator == (Id3v2Header left, Id3v2Header right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two headers are not equal.
	/// </summary>
	public static bool operator != (Id3v2Header left, Id3v2Header right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the result of reading an ID3v2 header.
/// </summary>
public readonly struct Id3v2HeaderReadResult : IEquatable<Id3v2HeaderReadResult>
{
	/// <summary>
	/// Gets the parsed header, or default if parsing failed.
	/// </summary>
	public Id3v2Header Header { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets a value indicating whether no header was found (not an error).
	/// </summary>
	public bool IsNotFound => !IsSuccess && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed.
	/// </summary>
	public int BytesConsumed { get; }

	Id3v2HeaderReadResult (Id3v2Header header, bool isSuccess, string? error, int bytesConsumed)
	{
		Header = header;
		IsSuccess = isSuccess;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static Id3v2HeaderReadResult Success (Id3v2Header header, int bytesConsumed) =>
		new (header, true, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static Id3v2HeaderReadResult Failure (string error) =>
		new (default, false, error, 0);

	/// <summary>
	/// Creates a not-found result.
	/// </summary>
	public static Id3v2HeaderReadResult NotFound () =>
		new (default, false, null, 0);

	/// <inheritdoc/>
	public bool Equals (Id3v2HeaderReadResult other) =>
		Header.Equals (other.Header) &&
		IsSuccess == other.IsSuccess &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is Id3v2HeaderReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Header, IsSuccess, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (Id3v2HeaderReadResult left, Id3v2HeaderReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (Id3v2HeaderReadResult left, Id3v2HeaderReadResult right) =>
		!left.Equals (right);
}
