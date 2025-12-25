// MIT License - Copyright (c) 2024-2025 Stephen Shaw and contributors

using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace TagSharp.Core;

/// <summary>
/// An immutable sequence of bytes with efficient parsing operations.
/// Designed for zero-allocation parsing using Span&lt;T&gt;.
/// </summary>
public readonly struct BinaryData : IEquatable<BinaryData>, IReadOnlyList<byte>, IComparable<BinaryData>
{
	private readonly byte[] _data;

	/// <summary>
	/// Safe accessor that handles default(BinaryData) where _data is null.
	/// </summary>
	private byte[] Data => _data ?? [];

	/// <summary>
	/// Gets an empty BinaryData.
	/// </summary>
	public static BinaryData Empty { get; } = new([]);

	/// <summary>
	/// Creates a BinaryData from the specified byte array.
	/// </summary>
	/// <param name="data">The byte array to wrap. The array is not copied.</param>
	public BinaryData (byte[] data)
	{
		_data = data ?? [];
	}

	/// <summary>
	/// Creates a BinaryData from a ReadOnlySpan, copying the data.
	/// </summary>
	/// <param name="data">The span to copy.</param>
	public BinaryData (ReadOnlySpan<byte> data)
	{
		_data = data.ToArray ();
	}

	/// <summary>
	/// Creates a BinaryData filled with the specified byte value.
	/// </summary>
	/// <param name="length">The length of the data.</param>
	/// <param name="fill">The byte value to fill with.</param>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when length is negative.</exception>
	public BinaryData (int length, byte fill = 0)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (length < 0)
			throw new ArgumentOutOfRangeException (nameof (length), "Length cannot be negative");
#else
		ArgumentOutOfRangeException.ThrowIfNegative (length);
#endif
		_data = new byte[length];
		if (fill != 0) {
#if NETSTANDARD2_0
			Polyfills.ArrayFill (_data, fill);
#else
			Array.Fill (_data, fill);
#endif
		}
	}

	/// <summary>
	/// Gets the number of bytes.
	/// </summary>
	public int Length => Data.Length;

	/// <summary>
	/// Gets whether this instance is empty.
	/// </summary>
	public bool IsEmpty => Data.Length == 0;

	/// <summary>
	/// Gets the byte at the specified index.
	/// </summary>
	/// <param name="index">The zero-based index.</param>
	public byte this[int index] => Data[index];

	/// <summary>
	/// Gets a slice of the data using a Range.
	/// </summary>
	/// <param name="range">The range to slice.</param>
	public BinaryData this[Range range] => new (Data.AsSpan ()[range]);

	/// <summary>
	/// Gets the number of bytes. Alias for Length (implements IReadOnlyCollection).
	/// </summary>
	public int Count => Data.Length;

	/// <summary>
	/// Gets a read-only span over the data.
	/// </summary>
	public ReadOnlySpan<byte> Span => Data.AsSpan ();

	/// <summary>
	/// Gets a read-only memory over the data.
	/// </summary>
	public ReadOnlyMemory<byte> Memory => Data.AsMemory ();

	/// <summary>
	/// Returns a slice starting at the specified index.
	/// </summary>
	/// <param name="start">The start index.</param>
	public BinaryData Slice (int start) => new (Data.AsSpan (start));

	/// <summary>
	/// Returns a slice of the specified length starting at the specified index.
	/// </summary>
	/// <param name="start">The start index.</param>
	/// <param name="length">The length of the slice.</param>
	public BinaryData Slice (int start, int length) => new (Data.AsSpan (start, length));

	/// <summary>
	/// Reads a big-endian 16-bit unsigned integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public ushort ToUInt16BE (int offset = 0) =>
		BinaryPrimitives.ReadUInt16BigEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a little-endian 16-bit unsigned integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public ushort ToUInt16LE (int offset = 0) =>
		BinaryPrimitives.ReadUInt16LittleEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a big-endian 32-bit unsigned integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public uint ToUInt32BE (int offset = 0) =>
		BinaryPrimitives.ReadUInt32BigEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a little-endian 32-bit unsigned integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public uint ToUInt32LE (int offset = 0) =>
		BinaryPrimitives.ReadUInt32LittleEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a big-endian 64-bit unsigned integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public ulong ToUInt64BE (int offset = 0) =>
		BinaryPrimitives.ReadUInt64BigEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a little-endian 64-bit unsigned integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public ulong ToUInt64LE (int offset = 0) =>
		BinaryPrimitives.ReadUInt64LittleEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a big-endian 16-bit signed integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public short ToInt16BE (int offset = 0) =>
		BinaryPrimitives.ReadInt16BigEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a little-endian 16-bit signed integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public short ToInt16LE (int offset = 0) =>
		BinaryPrimitives.ReadInt16LittleEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a big-endian 32-bit signed integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public int ToInt32BE (int offset = 0) =>
		BinaryPrimitives.ReadInt32BigEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a little-endian 32-bit signed integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public int ToInt32LE (int offset = 0) =>
		BinaryPrimitives.ReadInt32LittleEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a big-endian 64-bit signed integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public long ToInt64BE (int offset = 0) =>
		BinaryPrimitives.ReadInt64BigEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a little-endian 64-bit signed integer from the specified offset.
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public long ToInt64LE (int offset = 0) =>
		BinaryPrimitives.ReadInt64LittleEndian (Data.AsSpan (offset));

	/// <summary>
	/// Reads a syncsafe integer (ID3v2 format: 7 bits per byte, MSB always 0).
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public uint ToSyncSafeUInt32 (int offset = 0)
	{
		var span = Data.AsSpan (offset, 4);
		return (uint)((span[0] << 21) | (span[1] << 14) | (span[2] << 7) | span[3]);
	}

	/// <summary>
	/// Reads a 24-bit big-endian unsigned integer (used in FLAC).
	/// </summary>
	/// <param name="offset">The byte offset to read from.</param>
	public uint ToUInt24BE (int offset = 0)
	{
		var span = Data.AsSpan (offset, 3);
		return (uint)((span[0] << 16) | (span[1] << 8) | span[2]);
	}

	/// <summary>
	/// Creates a BinaryData from a big-endian 16-bit unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromUInt16BE (ushort value)
	{
		Span<byte> data = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16BigEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a little-endian 16-bit unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromUInt16LE (ushort value)
	{
		Span<byte> data = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a big-endian 32-bit unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromUInt32BE (uint value)
	{
		Span<byte> data = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a little-endian 32-bit unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromUInt32LE (uint value)
	{
		Span<byte> data = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a big-endian 64-bit unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromUInt64BE (ulong value)
	{
		Span<byte> data = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64BigEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a little-endian 64-bit unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromUInt64LE (ulong value)
	{
		Span<byte> data = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a big-endian 16-bit signed integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromInt16BE (short value)
	{
		Span<byte> data = stackalloc byte[2];
		BinaryPrimitives.WriteInt16BigEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a little-endian 16-bit signed integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromInt16LE (short value)
	{
		Span<byte> data = stackalloc byte[2];
		BinaryPrimitives.WriteInt16LittleEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a big-endian 32-bit signed integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromInt32BE (int value)
	{
		Span<byte> data = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a little-endian 32-bit signed integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromInt32LE (int value)
	{
		Span<byte> data = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a big-endian 64-bit signed integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromInt64BE (long value)
	{
		Span<byte> data = stackalloc byte[8];
		BinaryPrimitives.WriteInt64BigEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a little-endian 64-bit signed integer.
	/// </summary>
	/// <param name="value">The value to convert.</param>
	public static BinaryData FromInt64LE (long value)
	{
		Span<byte> data = stackalloc byte[8];
		BinaryPrimitives.WriteInt64LittleEndian (data, value);
		return new BinaryData (data);
	}

	/// <summary>
	/// Creates a BinaryData from a syncsafe integer (ID3v2 format).
	/// </summary>
	/// <param name="value">The value to convert (max 0x0FFFFFFF / 268,435,455).</param>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when value exceeds syncsafe maximum.</exception>
	public static BinaryData FromSyncSafeUInt32 (uint value)
	{
		if (value > 0x0FFFFFFF)
			throw new ArgumentOutOfRangeException (nameof (value), "Syncsafe integers cannot exceed 0x0FFFFFFF (268,435,455)");

		return new ([
			(byte)((value >> 21) & 0x7F),
			(byte)((value >> 14) & 0x7F),
			(byte)((value >> 7) & 0x7F),
			(byte)(value & 0x7F)
		]);
	}

	/// <summary>
	/// Creates a BinaryData from a 24-bit big-endian unsigned integer.
	/// </summary>
	/// <param name="value">The value to convert (max 0xFFFFFF / 16,777,215).</param>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when value exceeds 24-bit maximum.</exception>
	public static BinaryData FromUInt24BE (uint value)
	{
		if (value > 0xFFFFFF)
			throw new ArgumentOutOfRangeException (nameof (value), "24-bit integers cannot exceed 0xFFFFFF (16,777,215)");

		return new ([
			(byte)((value >> 16) & 0xFF),
			(byte)((value >> 8) & 0xFF),
			(byte)(value & 0xFF)
		]);
	}

	/// <summary>
	/// Decodes the data as a string using the specified encoding.
	/// </summary>
	/// <param name="encoding">The encoding to use.</param>
	public string ToString (Encoding encoding)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (encoding, nameof (encoding));
#else
		ArgumentNullException.ThrowIfNull (encoding);
#endif
		return encoding.GetString (Data);
	}

	/// <summary>
	/// Decodes the data as a Latin-1 (ISO-8859-1) string.
	/// </summary>
	public string ToStringLatin1 () => Polyfills.Latin1.GetString (Data);

	/// <summary>
	/// Decodes the data as a UTF-8 string.
	/// </summary>
	public string ToStringUtf8 () => Encoding.UTF8.GetString (Data);

	/// <summary>
	/// Decodes the data as a UTF-16 (Little-Endian) string, handling BOM if present.
	/// </summary>
	public string ToStringUtf16 ()
	{
		var span = Data.AsSpan ();

		// Check for BOM
		if (span.Length >= 2) {
			if (span[0] == 0xFF && span[1] == 0xFE)
				return GetString (Encoding.Unicode, span[2..]);
			if (span[0] == 0xFE && span[1] == 0xFF)
				return GetString (Encoding.BigEndianUnicode, span[2..]);
		}

		// Default to little-endian
		return Encoding.Unicode.GetString (Data);
	}

	/// <summary>
	/// Creates a BinaryData from a string using the specified encoding.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	/// <param name="encoding">The encoding to use.</param>
	public static BinaryData FromString (string value, Encoding encoding)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (encoding, nameof (encoding));
#else
		ArgumentNullException.ThrowIfNull (encoding);
#endif
		return new (encoding.GetBytes (value ?? string.Empty));
	}

	/// <summary>
	/// Creates a BinaryData from a Latin-1 string.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	public static BinaryData FromStringLatin1 (string value) =>
		new (Polyfills.Latin1.GetBytes (value ?? string.Empty));

	/// <summary>
	/// Creates a BinaryData from a UTF-8 string.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	public static BinaryData FromStringUtf8 (string value) =>
		new (Encoding.UTF8.GetBytes (value ?? string.Empty));

	/// <summary>
	/// Creates a BinaryData from a UTF-16LE string with optional BOM.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	/// <param name="includeBom">Whether to include the byte order mark.</param>
	public static BinaryData FromStringUtf16 (string value, bool includeBom = true)
	{
		var encoded = Encoding.Unicode.GetBytes (value ?? string.Empty);
		if (!includeBom)
			return new BinaryData (encoded);

		var result = new byte[encoded.Length + 2];
		result[0] = 0xFF;
		result[1] = 0xFE;
		encoded.CopyTo (result, 2);
		return new BinaryData (result);
	}

	/// <summary>
	/// Decodes the data as a null-terminated Latin-1 string.
	/// Stops at the first null byte.
	/// </summary>
	public string ToStringLatin1NullTerminated ()
	{
		var nullIndex = IndexOf ((byte)0);
		var span = nullIndex >= 0 ? Data.AsSpan (0, nullIndex) : Data.AsSpan ();
		return GetString (Polyfills.Latin1, span);
	}

	/// <summary>
	/// Decodes the data as a null-terminated UTF-8 string.
	/// Stops at the first null byte.
	/// </summary>
	public string ToStringUtf8NullTerminated ()
	{
		var nullIndex = IndexOf ((byte)0);
		var span = nullIndex >= 0 ? Data.AsSpan (0, nullIndex) : Data.AsSpan ();
		return GetString (Encoding.UTF8, span);
	}

	/// <summary>
	/// Decodes the data as a null-terminated UTF-16 string.
	/// Stops at the first double-null (0x00 0x00) terminator.
	/// </summary>
	public string ToStringUtf16NullTerminated ()
	{
		var span = Data.AsSpan ();

		// Check for and skip BOM
		var offset = 0;
		var encoding = Encoding.Unicode;

		if (span.Length >= 2) {
			if (span[0] == 0xFF && span[1] == 0xFE) {
				offset = 2;
				encoding = Encoding.Unicode;
			} else if (span[0] == 0xFE && span[1] == 0xFF) {
				offset = 2;
				encoding = Encoding.BigEndianUnicode;
			}
		}

		// Find double-null terminator (must be on even boundary)
		for (var i = offset; i + 1 < span.Length; i += 2) {
			if (span[i] == 0 && span[i + 1] == 0)
				return GetString (encoding, span[offset..i]);
		}

		return GetString (encoding, span[offset..]);
	}

	/// <summary>
	/// Creates a BinaryData from a Latin-1 string, null-terminated.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	public static BinaryData FromStringLatin1NullTerminated (string value)
	{
		var encoded = Polyfills.Latin1.GetBytes (value ?? string.Empty);
		var result = new byte[encoded.Length + 1];
		encoded.CopyTo (result, 0);
		result[encoded.Length] = 0;
		return new BinaryData (result);
	}

	/// <summary>
	/// Helper method to get string from span, compatible with older frameworks.
	/// </summary>
	static string GetString (Encoding encoding, ReadOnlySpan<byte> span)
	{
#if NETSTANDARD2_0
		return encoding.GetString (span.ToArray ());
#else
		return encoding.GetString (span);
#endif
	}

	/// <summary>
	/// Creates a BinaryData from a UTF-8 string, null-terminated.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	public static BinaryData FromStringUtf8NullTerminated (string value)
	{
		var encoded = Encoding.UTF8.GetBytes (value ?? string.Empty);
		var result = new byte[encoded.Length + 1];
		encoded.CopyTo (result, 0);
		result[encoded.Length] = 0;
		return new BinaryData (result);
	}

	/// <summary>
	/// Creates a BinaryData from a UTF-16 string, double-null terminated.
	/// </summary>
	/// <param name="value">The string to encode.</param>
	/// <param name="includeBom">Whether to include the byte order mark.</param>
	public static BinaryData FromStringUtf16NullTerminated (string value, bool includeBom = true)
	{
		var encoded = Encoding.Unicode.GetBytes (value ?? string.Empty);
		var bomSize = includeBom ? 2 : 0;
		var result = new byte[bomSize + encoded.Length + 2]; // +2 for double-null

		var offset = 0;
		if (includeBom) {
			result[0] = 0xFF;
			result[1] = 0xFE;
			offset = 2;
		}

		encoded.CopyTo (result, offset);
		// Last two bytes are already 0 from array initialization
		return new BinaryData (result);
	}

	/// <summary>
	/// Pads the data to the specified length with the given byte value.
	/// If already at or longer than the target length, returns this instance.
	/// </summary>
	/// <param name="length">The target length.</param>
	/// <param name="padByte">The byte value to pad with.</param>
	public BinaryData PadRight (int length, byte padByte = 0)
	{
		if (Data.Length >= length)
			return this;

		var result = new byte[length];
		Data.CopyTo (result, 0);
		if (padByte != 0) {
#if NETSTANDARD2_0
			Polyfills.ArrayFill (result, padByte, Data.Length, length - Data.Length);
#else
			Array.Fill (result, padByte, Data.Length, length - Data.Length);
#endif
		}
		return new BinaryData (result);
	}

	/// <summary>
	/// Pads the data on the left to the specified length with the given byte value.
	/// If already at or longer than the target length, returns this instance.
	/// </summary>
	/// <param name="length">The target length.</param>
	/// <param name="padByte">The byte value to pad with.</param>
	public BinaryData PadLeft (int length, byte padByte = 0)
	{
		if (Data.Length >= length)
			return this;

		var result = new byte[length];
		var padLength = length - Data.Length;
		if (padByte != 0) {
#if NETSTANDARD2_0
			Polyfills.ArrayFill (result, padByte, 0, padLength);
#else
			Array.Fill (result, padByte, 0, padLength);
#endif
		}
		Data.CopyTo (result, padLength);
		return new BinaryData (result);
	}

	/// <summary>
	/// Removes trailing bytes that match the specified value.
	/// </summary>
	/// <param name="trimByte">The byte value to trim.</param>
	public BinaryData TrimEnd (byte trimByte = 0)
	{
		var end = Data.Length;
		while (end > 0 && Data[end - 1] == trimByte)
			end--;
		return end == Data.Length ? this : new BinaryData (Data.AsSpan (0, end));
	}

	/// <summary>
	/// Removes leading bytes that match the specified value.
	/// </summary>
	/// <param name="trimByte">The byte value to trim.</param>
	public BinaryData TrimStart (byte trimByte = 0)
	{
		var start = 0;
		while (start < Data.Length && Data[start] == trimByte)
			start++;
		return start == 0 ? this : new BinaryData (Data.AsSpan (start));
	}

	/// <summary>
	/// Removes leading and trailing bytes that match the specified value.
	/// </summary>
	/// <param name="trimByte">The byte value to trim.</param>
	public BinaryData Trim (byte trimByte = 0)
	{
		var start = 0;
		while (start < Data.Length && Data[start] == trimByte)
			start++;

		var end = Data.Length;
		while (end > start && Data[end - 1] == trimByte)
			end--;

		if (start == 0 && end == Data.Length)
			return this;

		return new BinaryData (Data.AsSpan (start, end - start));
	}

	/// <summary>
	/// Truncates or pads the data to exactly the specified length.
	/// </summary>
	/// <param name="length">The exact target length.</param>
	/// <param name="padByte">The byte value to pad with if extending.</param>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when length is negative.</exception>
	public BinaryData Resize (int length, byte padByte = 0)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (length < 0)
			throw new ArgumentOutOfRangeException (nameof (length), "Length cannot be negative");
#else
		ArgumentOutOfRangeException.ThrowIfNegative (length);
#endif
		if (length == Data.Length)
			return this;

		if (length < Data.Length)
			return new BinaryData (Data.AsSpan (0, length));

		return PadRight (length, padByte);
	}

	/// <summary>
	/// Finds the first occurrence of the pattern.
	/// </summary>
	/// <param name="pattern">The pattern to find.</param>
	/// <param name="startIndex">The index to start searching from.</param>
	/// <returns>The index of the pattern, or -1 if not found.</returns>
	public int IndexOf (ReadOnlySpan<byte> pattern, int startIndex = 0)
	{
		if (pattern.IsEmpty || startIndex >= Data.Length)
			return -1;

		var searchSpan = Data.AsSpan (startIndex);
		var index = searchSpan.IndexOf (pattern);
		return index >= 0 ? index + startIndex : -1;
	}

	/// <summary>
	/// Finds the first occurrence of a single byte.
	/// </summary>
	/// <param name="value">The byte to find.</param>
	/// <param name="startIndex">The index to start searching from.</param>
	/// <returns>The index of the byte, or -1 if not found.</returns>
	public int IndexOf (byte value, int startIndex = 0)
	{
		if (startIndex >= Data.Length)
			return -1;

		var searchSpan = Data.AsSpan (startIndex);
		var index = searchSpan.IndexOf (value);
		return index >= 0 ? index + startIndex : -1;
	}

	/// <summary>
	/// Finds the last occurrence of the pattern.
	/// </summary>
	/// <param name="pattern">The pattern to find.</param>
	/// <returns>The index of the pattern, or -1 if not found.</returns>
	public int LastIndexOf (ReadOnlySpan<byte> pattern)
	{
		if (pattern.IsEmpty || Data.Length == 0)
			return -1;

		return Data.AsSpan ().LastIndexOf (pattern);
	}

	/// <summary>
	/// Finds the last occurrence of a single byte.
	/// </summary>
	/// <param name="value">The byte to find.</param>
	/// <returns>The index of the byte, or -1 if not found.</returns>
	public int LastIndexOf (byte value)
	{
		if (Data.Length == 0)
			return -1;

		return Data.AsSpan ().LastIndexOf (value);
	}

	/// <summary>
	/// Checks if the data contains the specified pattern.
	/// </summary>
	/// <param name="pattern">The pattern to find.</param>
	public bool Contains (ReadOnlySpan<byte> pattern) =>
		IndexOf (pattern) >= 0;

	/// <summary>
	/// Checks if the data contains the specified byte.
	/// </summary>
	/// <param name="value">The byte to find.</param>
	public bool Contains (byte value) =>
		IndexOf (value) >= 0;

	/// <summary>
	/// Checks if this data starts with the specified pattern.
	/// </summary>
	/// <param name="pattern">The pattern to check.</param>
	public bool StartsWith (ReadOnlySpan<byte> pattern) =>
		Data.AsSpan ().StartsWith (pattern);

	/// <summary>
	/// Checks if this data ends with the specified pattern.
	/// </summary>
	/// <param name="pattern">The pattern to check.</param>
	public bool EndsWith (ReadOnlySpan<byte> pattern) =>
		Data.AsSpan ().EndsWith (pattern);

	/// <summary>
	/// Concatenates this data with another. Alias for the + operator.
	/// </summary>
	/// <param name="other">The data to append.</param>
	public BinaryData Add (BinaryData other)
	{
		if (IsEmpty) return other;
		if (other.IsEmpty) return this;

		var result = new byte[Data.Length + other.Data.Length];
		Data.CopyTo (result, 0);
		other.Data.CopyTo (result, Data.Length);
		return new BinaryData (result);
	}

	/// <summary>
	/// Concatenates multiple BinaryData instances.
	/// </summary>
	/// <param name="items">The items to concatenate.</param>
	public static BinaryData Concat (params BinaryData[] items)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (items, nameof (items));
#else
		ArgumentNullException.ThrowIfNull (items);
#endif

		var totalLength = 0;
		foreach (var v in items)
			totalLength += v.Length;

		if (totalLength == 0)
			return Empty;

		var result = new byte[totalLength];
		var offset = 0;
		foreach (var v in items) {
			v.Data.CopyTo (result, offset);
			offset += v.Length;
		}
		return new BinaryData (result);
	}

	/// <summary>
	/// Concatenates two BinaryData instances.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static BinaryData operator + (BinaryData left, BinaryData right) =>
		left.Add (right);

	/// <summary>
	/// Converts the data to a lowercase hexadecimal string.
	/// </summary>
	public string ToHexString ()
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		return Polyfills.ToHexStringLower (Data);
#elif NET9_0_OR_GREATER
		return Convert.ToHexStringLower (Data);
#else
		return string.Create (Data.Length * 2, Data, static (chars, data) =>
		{
			const string hex = "0123456789abcdef";
			for (var i = 0; i < data.Length; i++) {
				chars[i * 2] = hex[data[i] >> 4];
				chars[i * 2 + 1] = hex[data[i] & 0xF];
			}
		});
#endif
	}

	/// <summary>
	/// Converts the data to an uppercase hexadecimal string.
	/// </summary>
	public string ToHexStringUpper ()
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		return Polyfills.ToHexString (Data);
#else
		return Convert.ToHexString (Data);
#endif
	}

	/// <summary>
	/// Creates a BinaryData from a hexadecimal string.
	/// </summary>
	/// <param name="hex">The hexadecimal string (with or without spaces).</param>
	public static BinaryData FromHexString (string hex)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (hex, nameof (hex));
#else
		ArgumentNullException.ThrowIfNull (hex);
#endif

		if (hex.Length == 0)
			return Empty;

		// Only allocate new string if spaces are present
#if NETSTANDARD2_0
		var hasSpace = hex.IndexOf (' ') >= 0;
		if (hasSpace)
			hex = Polyfills.Replace (hex, " ", "", StringComparison.Ordinal);
#else
		if (hex.Contains (' ', StringComparison.Ordinal))
			hex = hex.Replace (" ", "", StringComparison.Ordinal);
#endif

		if (hex.Length == 0)
			return Empty;

#if NETSTANDARD2_0 || NETSTANDARD2_1
		return new BinaryData (Polyfills.FromHexString (hex));
#else
		return new BinaryData (Convert.FromHexString (hex));
#endif
	}

	static readonly uint[] Crc32Table = BuildCrc32Table ();
	static readonly byte[] Crc8Table = BuildCrc8Table ();
	static readonly ushort[] Crc16CcittTable = BuildCrc16CcittTable ();

	static uint[] BuildCrc32Table ()
	{
		var table = new uint[256];
		for (uint i = 0; i < 256; i++) {
			var crc = i;
			for (var j = 0; j < 8; j++)
				crc = (crc >> 1) ^ ((crc & 1) * 0xEDB88320);
			table[i] = crc;
		}
		return table;
	}

	static byte[] BuildCrc8Table ()
	{
		var table = new byte[256];
		for (var i = 0; i < 256; i++) {
			byte crc = (byte)i;
			for (var j = 0; j < 8; j++)
				crc = (byte)((crc << 1) ^ ((crc & 0x80) != 0 ? 0x07 : 0));
			table[i] = crc;
		}
		return table;
	}

	static ushort[] BuildCrc16CcittTable ()
	{
		var table = new ushort[256];
		for (var i = 0; i < 256; i++) {
			var crc = (ushort)(i << 8);
			for (var j = 0; j < 8; j++)
				crc = (ushort)((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0));
			table[i] = crc;
		}
		return table;
	}

	/// <summary>
	/// Computes CRC-32 using the standard polynomial (0xEDB88320).
	/// This is compatible with zlib/PNG CRC.
	/// </summary>
	public uint ComputeCrc32 ()
	{
		var crc = 0xFFFFFFFF;
		for (var i = 0; i < Data.Length; i++)
			crc = Crc32Table[(crc ^ Data[i]) & 0xFF] ^ (crc >> 8);
		return ~crc;
	}

	/// <summary>
	/// Computes CRC-8 using the standard polynomial (0x07).
	/// Used in some audio format headers.
	/// </summary>
	public byte ComputeCrc8 ()
	{
		byte crc = 0;
		for (var i = 0; i < Data.Length; i++)
			crc = Crc8Table[crc ^ Data[i]];
		return crc;
	}

	/// <summary>
	/// Computes CRC-16 using CCITT polynomial (0x1021).
	/// Used in FLAC frame headers.
	/// </summary>
	public ushort ComputeCrc16Ccitt ()
	{
		ushort crc = 0;
		for (var i = 0; i < Data.Length; i++)
			crc = (ushort)((crc << 8) ^ Crc16CcittTable[(crc >> 8) ^ Data[i]]);
		return crc;
	}

	/// <inheritdoc />
	public bool Equals (BinaryData other) =>
		Data.AsSpan ().SequenceEqual (other.Data.AsSpan ());

	/// <inheritdoc />
	public override bool Equals (object? obj) =>
		obj is BinaryData other && Equals (other);

	/// <inheritdoc />
	public override int GetHashCode ()
	{
		// For small data, hash all bytes
		if (Data.Length <= 32) {
			var hash = new HashCode ();
			for (var i = 0; i < Data.Length; i++)
				hash.Add (Data[i]);
			return hash.ToHashCode ();
		}

		// For larger data, sample strategically to avoid O(n) hashing
		return HashCode.Combine (
			Data.Length,
			Data[0],
			Data[Data.Length / 4],
			Data[Data.Length / 2],
			Data[Data.Length * 3 / 4],
			Data[Data.Length - 1]
		);
	}

	/// <summary>
	/// Determines whether two BinaryData instances are equal.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static bool operator == (BinaryData left, BinaryData right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two BinaryData instances are not equal.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static bool operator != (BinaryData left, BinaryData right) =>
		!left.Equals (right);

	/// <summary>
	/// Returns a copy of the underlying data as an array.
	/// </summary>
	public byte[] ToArray () => Data.ToArray ();

	/// <summary>
	/// Returns a ReadOnlySpan over the data. Alias for the Span property.
	/// </summary>
	public ReadOnlySpan<byte> ToReadOnlySpan () => Data.AsSpan ();

	/// <inheritdoc />
	public override string ToString () => $"BinaryData[{Data.Length}]";

	/// <summary>
	/// Creates a BinaryData from a byte array.
	/// </summary>
	/// <param name="data">The byte array.</param>
	public static BinaryData FromByteArray (byte[] data) => new (data);

	/// <summary>
	/// Implicit conversion from byte array.
	/// </summary>
	/// <param name="data">The byte array.</param>
	public static implicit operator BinaryData (byte[] data) => new (data);

	/// <summary>
	/// Implicit conversion to ReadOnlySpan.
	/// </summary>
	/// <param name="data">The BinaryData instance.</param>
	public static implicit operator ReadOnlySpan<byte> (BinaryData data) => data.Span;

	/// <summary>
	/// Returns an enumerator that iterates through the bytes.
	/// </summary>
	public IEnumerator<byte> GetEnumerator ()
	{
		foreach (var b in Data)
			yield return b;
	}

	/// <inheritdoc />
	IEnumerator IEnumerable.GetEnumerator () => GetEnumerator ();

	/// <summary>
	/// Compares this instance to another BinaryData lexicographically.
	/// </summary>
	/// <param name="other">The other BinaryData to compare to.</param>
	/// <returns>A value indicating the relative order.</returns>
	public int CompareTo (BinaryData other) =>
		Data.AsSpan ().SequenceCompareTo (other.Data.AsSpan ());

	/// <summary>
	/// Less than operator.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static bool operator < (BinaryData left, BinaryData right) =>
		left.CompareTo (right) < 0;

	/// <summary>
	/// Less than or equal operator.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static bool operator <= (BinaryData left, BinaryData right) =>
		left.CompareTo (right) <= 0;

	/// <summary>
	/// Greater than operator.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static bool operator > (BinaryData left, BinaryData right) =>
		left.CompareTo (right) > 0;

	/// <summary>
	/// Greater than or equal operator.
	/// </summary>
	/// <param name="left">The first operand.</param>
	/// <param name="right">The second operand.</param>
	public static bool operator >= (BinaryData left, BinaryData right) =>
		left.CompareTo (right) >= 0;

}
