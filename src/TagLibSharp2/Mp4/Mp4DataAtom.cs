// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents an iTunes-style MP4 data atom.
/// </summary>
/// <remarks>
/// <para>
/// Data atoms contain the actual metadata values. Structure:
/// </para>
/// <code>
/// Bytes 0-3:   version (1 byte) + flags (3 bytes) - flags indicate data type
/// Bytes 4-7:   reserved (locale, typically 0)
/// Bytes 8+:    actual value (format depends on type indicator in flags)
/// </code>
/// </remarks>
internal readonly struct Mp4DataAtom
{
	/// <summary>
	/// Gets the data type indicator from the flags field.
	/// </summary>
	public int TypeIndicator { get; }

	/// <summary>
	/// Gets the raw data value.
	/// </summary>
	public BinaryData Data { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="Mp4DataAtom"/> struct.
	/// </summary>
	/// <param name="typeIndicator">The data type indicator.</param>
	/// <param name="data">The raw data.</param>
	public Mp4DataAtom (int typeIndicator, BinaryData data)
	{
		TypeIndicator = typeIndicator;
		Data = data;
	}

	/// <summary>
	/// Parses a data atom from binary data.
	/// </summary>
	/// <param name="data">The binary data (must be at least 8 bytes).</param>
	/// <returns>The parsed data atom.</returns>
	public static Mp4DataAtom Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < 8)
			return new Mp4DataAtom (Mp4AtomMapping.TypeBinary, BinaryData.Empty);

		// Bytes 0-3: version (1 byte) + flags (3 bytes) - flags are the type indicator
		var typeIndicator = (data[1] << 16) | (data[2] << 8) | data[3];

		// Bytes 4-7: reserved (locale)
		// Bytes 8+: actual value
		var valueData = data.Length > 8 ? new BinaryData (data[8..]) : BinaryData.Empty;

		return new Mp4DataAtom (typeIndicator, valueData);
	}

	/// <summary>
	/// Converts the data to a UTF-8 string.
	/// </summary>
	/// <returns>The decoded string, or null if empty.</returns>
	public string? ToUtf8String ()
	{
		if (Data.IsEmpty)
			return null;
		return Data.ToStringUtf8 ();
	}

	/// <summary>
	/// Converts the data to an integer value.
	/// </summary>
	/// <returns>The integer value, or null if the data is empty or invalid.</returns>
	public uint? ToUInt32 ()
	{
		if (Data.IsEmpty)
			return null;

		// Variable-length integer (1, 2, 3, 4, or 8 bytes, big-endian)
		return Data.Length switch {
			1 => Data[0],
			2 => Data.ToUInt16BE (),
			3 => Data.ToUInt24BE (),
			4 => Data.ToUInt32BE (),
			8 => (uint)Data.ToUInt64BE (), // Truncate if needed
			_ => null
		};
	}

	/// <summary>
	/// Converts the data to a boolean value.
	/// </summary>
	/// <returns>True if the value is non-zero, false otherwise.</returns>
	public bool ToBoolean ()
	{
		if (Data.IsEmpty)
			return false;
		return Data[0] != 0;
	}

	/// <summary>
	/// Parses track or disc number data (8-byte structure).
	/// </summary>
	/// <param name="number">The track/disc number (output).</param>
	/// <param name="total">The total tracks/discs (output).</param>
	/// <returns>True if parsing succeeded, false otherwise.</returns>
	public bool TryParseTrackDisc (out uint number, out uint total)
	{
		number = 0;
		total = 0;

		if (Data.Length < 6) // Minimum: 2 bytes reserved + 2 bytes number + 2 bytes total
			return false;

		// Structure: [0][0][number:2][total:2][0][0]
		// Bytes 0-1: reserved
		// Bytes 2-3: track/disc number (big-endian uint16)
		// Bytes 4-5: total tracks/discs (big-endian uint16)
		// Bytes 6-7: reserved (optional)

		number = Data.ToUInt16BE (2);
		total = Data.ToUInt16BE (4);
		return true;
	}

	/// <summary>
	/// Creates a data atom from a UTF-8 string.
	/// </summary>
	/// <param name="value">The string value.</param>
	/// <returns>The binary representation of the data atom.</returns>
	public static BinaryData Create (string value)
	{
		var textBytes = Encoding.UTF8.GetBytes (value ?? string.Empty);
		var data = new byte[8 + textBytes.Length];

		// Version: 0
		data[0] = 0;

		// Flags: type indicator (1 = UTF-8)
		data[1] = 0;
		data[2] = 0;
		data[3] = Mp4AtomMapping.TypeUtf8;

		// Reserved (locale): 0
		data[4] = 0;
		data[5] = 0;
		data[6] = 0;
		data[7] = 0;

		// Value
		if (textBytes.Length > 0)
			textBytes.CopyTo (data, 8);

		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a data atom from an integer value.
	/// </summary>
	/// <param name="value">The integer value.</param>
	/// <param name="byteCount">The number of bytes to use (1, 2, or 4).</param>
	/// <returns>The binary representation of the data atom.</returns>
	public static BinaryData Create (uint value, int byteCount = 4)
	{
		var data = new byte[8 + byteCount];

		// Version: 0
		data[0] = 0;

		// Flags: type indicator (21 = integer)
		data[1] = 0;
		data[2] = 0;
		data[3] = Mp4AtomMapping.TypeInteger;

		// Reserved (locale): 0
		data[4] = 0;
		data[5] = 0;
		data[6] = 0;
		data[7] = 0;

		// Value (big-endian)
		switch (byteCount) {
			case 1:
				data[8] = (byte)value;
				break;
			case 2:
				data[8] = (byte)((value >> 8) & 0xFF);
				data[9] = (byte)(value & 0xFF);
				break;
			case 4:
				data[8] = (byte)((value >> 24) & 0xFF);
				data[9] = (byte)((value >> 16) & 0xFF);
				data[10] = (byte)((value >> 8) & 0xFF);
				data[11] = (byte)(value & 0xFF);
				break;
		}

		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a data atom from a boolean value.
	/// </summary>
	/// <param name="value">The boolean value.</param>
	/// <returns>The binary representation of the data atom.</returns>
	public static BinaryData Create (bool value)
	{
		return Create (value ? 1u : 0u, 1);
	}

	/// <summary>
	/// Creates a data atom for track/disc numbers.
	/// </summary>
	/// <param name="number">The track/disc number.</param>
	/// <param name="total">The total tracks/discs.</param>
	/// <returns>The binary representation of the data atom.</returns>
	public static BinaryData CreateTrackDisc (uint number, uint total)
	{
		var data = new byte[16]; // 8 header + 8 value

		// Version: 0
		data[0] = 0;

		// Flags: type indicator (0 = binary/implicit)
		data[1] = 0;
		data[2] = 0;
		data[3] = 0;

		// Reserved (locale): 0
		data[4] = 0;
		data[5] = 0;
		data[6] = 0;
		data[7] = 0;

		// Value structure: [0][0][number:2][total:2][0][0]
		data[8] = 0;  // Reserved
		data[9] = 0;  // Reserved
		data[10] = (byte)((number >> 8) & 0xFF);
		data[11] = (byte)(number & 0xFF);
		data[12] = (byte)((total >> 8) & 0xFF);
		data[13] = (byte)(total & 0xFF);
		data[14] = 0;  // Reserved
		data[15] = 0;  // Reserved

		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a data atom for image data.
	/// </summary>
	/// <param name="imageData">The image binary data.</param>
	/// <param name="isJpeg">True if JPEG, false if PNG.</param>
	/// <returns>The binary representation of the data atom.</returns>
	public static BinaryData CreateImage (BinaryData imageData, bool isJpeg)
	{
		var data = new byte[8 + imageData.Length];

		// Version: 0
		data[0] = 0;

		// Flags: type indicator (13 = JPEG, 14 = PNG)
		data[1] = 0;
		data[2] = 0;
		data[3] = (byte)(isJpeg ? Mp4AtomMapping.TypeJpeg : Mp4AtomMapping.TypePng);

		// Reserved (locale): 0
		data[4] = 0;
		data[5] = 0;
		data[6] = 0;
		data[7] = 0;

		// Image data
		imageData.Span.CopyTo (data.AsSpan (8));

		return new BinaryData (data);
	}
}
