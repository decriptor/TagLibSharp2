// Copyright (c) 2025 Stephen Shaw and contributors

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace TagLibSharp2.Core;

/// <summary>
/// A mutable builder for efficiently constructing <see cref="BinaryData"/> instances.
/// Optimized for sequential appends with minimal allocations.
/// Implements <see cref="IDisposable"/> to return pooled buffers.
/// </summary>
public sealed class BinaryDataBuilder : IDisposable
{
	const int DefaultCapacity = 256;
	const int MaxArrayLength = 0x7FFFFFC7; // Array.MaxLength
	const int PoolThreshold = 1024; // Use ArrayPool for buffers >= 1KB

	byte[] _buffer;
	int _length;
	bool _isPooled;

	/// <summary>
	/// Creates a new builder with default initial capacity (256 bytes).
	/// </summary>
	public BinaryDataBuilder () : this (DefaultCapacity) { }

	/// <summary>
	/// Creates a new builder with the specified initial capacity.
	/// </summary>
	/// <param name="initialCapacity">The initial buffer capacity in bytes.</param>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is negative.</exception>
	public BinaryDataBuilder (int initialCapacity)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (initialCapacity < 0)
			throw new ArgumentOutOfRangeException (nameof (initialCapacity), "Capacity cannot be negative");
#else
		ArgumentOutOfRangeException.ThrowIfNegative (initialCapacity);
#endif
		if (initialCapacity >= PoolThreshold) {
			_buffer = ArrayPool<byte>.Shared.Rent (initialCapacity);
			_isPooled = true;
		} else {
			_buffer = initialCapacity > 0 ? new byte[initialCapacity] : [];
			_isPooled = false;
		}
		_length = 0;
	}

	/// <summary>
	/// Gets the current length of the data.
	/// </summary>
	public int Length => _length;

	/// <summary>
	/// Gets the current buffer capacity.
	/// </summary>
	public int Capacity => _buffer.Length;

	/// <summary>
	/// Gets a read-only span over the current data.
	/// </summary>
	public ReadOnlySpan<byte> Span => _buffer.AsSpan (0, _length);

	/// <summary>
	/// Gets a read-only memory over the current data.
	/// </summary>
	public ReadOnlyMemory<byte> Memory => _buffer.AsMemory (0, _length);

	/// <summary>
	/// Gets or sets the byte at the specified index.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
	public byte this[int index] {
		get {
			if ((uint)index >= (uint)_length)
				throw new ArgumentOutOfRangeException (nameof (index));
			return _buffer[index];
		}
		set {
			if ((uint)index >= (uint)_length)
				throw new ArgumentOutOfRangeException (nameof (index));
			_buffer[index] = value;
		}
	}

	/// <summary>
	/// Ensures the buffer has at least the specified capacity.
	/// </summary>
	/// <param name="capacity">The minimum capacity required.</param>
	public void EnsureCapacity (int capacity)
	{
		if (capacity > _buffer.Length)
			Grow (capacity);
	}

	void Grow (int minimumRequired)
	{
		// Double the buffer using long to prevent overflow, ensure minimum floor, then clamp to max
		var doubled = Math.Max ((long)_buffer.Length * 2, DefaultCapacity);
		var newCapacity = (int)Math.Min (Math.Max (doubled, minimumRequired), MaxArrayLength);

		if (newCapacity < minimumRequired)
			throw new InvalidOperationException ("Required capacity exceeds maximum array length");

		// Allocate new buffer (use pool for larger buffers)
		byte[] newBuffer;
		bool newIsPooled;
		if (newCapacity >= PoolThreshold) {
			newBuffer = ArrayPool<byte>.Shared.Rent (newCapacity);
			newIsPooled = true;
		} else {
			newBuffer = new byte[newCapacity];
			newIsPooled = false;
		}

		// Copy existing data
		_buffer.AsSpan (0, _length).CopyTo (newBuffer);

		// Return old buffer to pool if it was rented
		if (_isPooled)
			ArrayPool<byte>.Shared.Return (_buffer);

		_buffer = newBuffer;
		_isPooled = newIsPooled;
	}

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	int CheckedAdd (int additionalLength)
	{
		// Prevent integer overflow when calculating new length
		if (_length > MaxArrayLength - additionalLength)
			throw new InvalidOperationException ("Resulting size would exceed maximum array length");
		return _length + additionalLength;
	}

	/// <summary>
	/// Adds a single byte.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder Add (byte value)
	{
		var newLength = CheckedAdd (1);
		if (newLength > _buffer.Length)
			Grow (newLength);

		_buffer[_length] = value;
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds multiple bytes.
	/// </summary>
	public BinaryDataBuilder Add (params byte[] data)
	{
		if (data is null || data.Length == 0)
			return this;

		return Add (data.AsSpan ());
	}

	/// <summary>
	/// Adds bytes from a span.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder Add (ReadOnlySpan<byte> data)
	{
		if (data.IsEmpty)
			return this;

		var newLength = CheckedAdd (data.Length);
		if (newLength > _buffer.Length)
			Grow (newLength);

		data.CopyTo (_buffer.AsSpan (_length));
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds bytes from a BinaryData instance.
	/// </summary>
	public BinaryDataBuilder Add (BinaryData data) => Add (data.Span);

	/// <summary>
	/// Adds a specified number of zero bytes.
	/// </summary>
	public BinaryDataBuilder AddZeros (int count)
	{
		if (count <= 0)
			return this;

		var newLength = CheckedAdd (count);
		if (newLength > _buffer.Length)
			Grow (newLength);

		_buffer.AsSpan (_length, count).Clear ();
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a specified number of bytes with the given value.
	/// </summary>
	public BinaryDataBuilder AddFill (byte value, int count)
	{
		if (count <= 0)
			return this;

		var newLength = CheckedAdd (count);
		if (newLength > _buffer.Length)
			Grow (newLength);

		_buffer.AsSpan (_length, count).Fill (value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 16-bit unsigned integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddUInt16BE (ushort value)
	{
		var newLength = CheckedAdd (2);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteUInt16BigEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 16-bit unsigned integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddUInt16LE (ushort value)
	{
		var newLength = CheckedAdd (2);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteUInt16LittleEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 32-bit unsigned integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddUInt32BE (uint value)
	{
		var newLength = CheckedAdd (4);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteUInt32BigEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 32-bit unsigned integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddUInt32LE (uint value)
	{
		var newLength = CheckedAdd (4);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteUInt32LittleEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 64-bit unsigned integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddUInt64BE (ulong value)
	{
		var newLength = CheckedAdd (8);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteUInt64BigEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 64-bit unsigned integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddUInt64LE (ulong value)
	{
		var newLength = CheckedAdd (8);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteUInt64LittleEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 16-bit signed integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddInt16BE (short value)
	{
		var newLength = CheckedAdd (2);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteInt16BigEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 16-bit signed integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddInt16LE (short value)
	{
		var newLength = CheckedAdd (2);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteInt16LittleEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 32-bit signed integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddInt32BE (int value)
	{
		var newLength = CheckedAdd (4);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteInt32BigEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 32-bit signed integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddInt32LE (int value)
	{
		var newLength = CheckedAdd (4);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteInt32LittleEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 64-bit signed integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddInt64BE (long value)
	{
		var newLength = CheckedAdd (8);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteInt64BigEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 64-bit signed integer.
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public BinaryDataBuilder AddInt64LE (long value)
	{
		var newLength = CheckedAdd (8);
		if (newLength > _buffer.Length)
			Grow (newLength);

		BinaryPrimitives.WriteInt64LittleEndian (_buffer.AsSpan (_length), value);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a big-endian 24-bit unsigned integer.
	/// </summary>
	/// <param name="value">Value must not exceed 0xFFFFFF (16,777,215).</param>
	public BinaryDataBuilder AddUInt24BE (uint value)
	{
		if (value > 0xFFFFFF)
			throw new ArgumentOutOfRangeException (nameof (value), "24-bit integers cannot exceed 0xFFFFFF");

		var newLength = CheckedAdd (3);
		if (newLength > _buffer.Length)
			Grow (newLength);

		_buffer[_length] = (byte)(value >> 16);
		_buffer[_length + 1] = (byte)(value >> 8);
		_buffer[_length + 2] = (byte)value;
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a little-endian 24-bit unsigned integer.
	/// </summary>
	/// <param name="value">Value must not exceed 0xFFFFFF (16,777,215).</param>
	public BinaryDataBuilder AddUInt24LE (uint value)
	{
		if (value > 0xFFFFFF)
			throw new ArgumentOutOfRangeException (nameof (value), "24-bit integers cannot exceed 0xFFFFFF");

		var newLength = CheckedAdd (3);
		if (newLength > _buffer.Length)
			Grow (newLength);

		_buffer[_length] = (byte)value;
		_buffer[_length + 1] = (byte)(value >> 8);
		_buffer[_length + 2] = (byte)(value >> 16);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a syncsafe 32-bit unsigned integer (ID3v2 format).
	/// </summary>
	/// <param name="value">Value must not exceed 0x0FFFFFFF (268,435,455).</param>
	public BinaryDataBuilder AddSyncSafeUInt32 (uint value)
	{
		if (value > 0x0FFFFFFF)
			throw new ArgumentOutOfRangeException (nameof (value), "Syncsafe integers cannot exceed 0x0FFFFFFF");

		var newLength = CheckedAdd (4);
		if (newLength > _buffer.Length)
			Grow (newLength);

		_buffer[_length] = (byte)((value >> 21) & 0x7F);
		_buffer[_length + 1] = (byte)((value >> 14) & 0x7F);
		_buffer[_length + 2] = (byte)((value >> 7) & 0x7F);
		_buffer[_length + 3] = (byte)(value & 0x7F);
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a string using the specified encoding.
	/// </summary>
	public BinaryDataBuilder AddString (string value, Encoding encoding)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (encoding, nameof (encoding));
#else
		ArgumentNullException.ThrowIfNull (encoding, nameof (encoding));
#endif
		if (string.IsNullOrEmpty (value))
			return this;

		var byteCount = encoding.GetByteCount (value);
		var newLength = CheckedAdd (byteCount);
		if (newLength > _buffer.Length)
			Grow (newLength);

#if NETSTANDARD2_0
		// Write directly to buffer without intermediate allocation
		encoding.GetBytes (value, 0, value.Length, _buffer, _length);
#else
		encoding.GetBytes (value, _buffer.AsSpan (_length));
#endif
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Adds a Latin-1 (ISO-8859-1) encoded string.
	/// </summary>
	public BinaryDataBuilder AddStringLatin1 (string value) =>
		AddString (value, Polyfills.Latin1);

	/// <summary>
	/// Adds a UTF-8 encoded string.
	/// </summary>
	public BinaryDataBuilder AddStringUtf8 (string value) =>
		AddString (value, Encoding.UTF8);

	/// <summary>
	/// Adds a UTF-16LE encoded string with optional BOM.
	/// </summary>
	public BinaryDataBuilder AddStringUtf16 (string value, bool includeBom = true)
	{
		if (includeBom)
			Add ((ReadOnlySpan<byte>)[0xFF, 0xFE]); // BOM as single operation
		return AddString (value, Encoding.Unicode);
	}

	/// <summary>
	/// Adds a Latin-1 encoded string followed by a null terminator.
	/// </summary>
	public BinaryDataBuilder AddStringLatin1NullTerminated (string value) =>
		AddStringLatin1 (value).Add (0x00);

	/// <summary>
	/// Adds a UTF-8 encoded string followed by a null terminator.
	/// </summary>
	public BinaryDataBuilder AddStringUtf8NullTerminated (string value) =>
		AddStringUtf8 (value).Add (0x00);

	/// <summary>
	/// Adds a UTF-16LE encoded string followed by a double-null terminator.
	/// </summary>
	public BinaryDataBuilder AddStringUtf16NullTerminated (string value, bool includeBom = true)
	{
		AddStringUtf16 (value, includeBom);
		return Add ((ReadOnlySpan<byte>)[0x00, 0x00]);
	}

	/// <summary>
	/// Inserts bytes at the specified index.
	/// </summary>
	/// <param name="index">The index at which to insert.</param>
	/// <param name="data">The data to insert.</param>
	public BinaryDataBuilder Insert (int index, ReadOnlySpan<byte> data)
	{
		if ((uint)index > (uint)_length)
			throw new ArgumentOutOfRangeException (nameof (index));

		if (data.IsEmpty)
			return this;

		var newLength = CheckedAdd (data.Length);
		if (newLength > _buffer.Length)
			Grow (newLength);

		// Shift existing data to make room
		var span = _buffer.AsSpan ();
		span.Slice (index, _length - index).CopyTo (span.Slice (index + data.Length));

		// Copy new data
		data.CopyTo (span.Slice (index));
		_length = newLength;
		return this;
	}

	/// <summary>
	/// Inserts a BinaryData at the specified index.
	/// </summary>
	public BinaryDataBuilder Insert (int index, BinaryData data) =>
		Insert (index, data.Span);

	/// <summary>
	/// Removes a range of bytes.
	/// </summary>
	/// <param name="index">The starting index.</param>
	/// <param name="count">The number of bytes to remove.</param>
	public BinaryDataBuilder RemoveRange (int index, int count)
	{
		if (index < 0 || index > _length)
			throw new ArgumentOutOfRangeException (nameof (index));
		if (count < 0 || count > _length - index)
			throw new ArgumentOutOfRangeException (nameof (count));

		if (count == 0)
			return this;

		// Shift remaining data left
		var span = _buffer.AsSpan ();
		span.Slice (index + count, _length - index - count).CopyTo (span.Slice (index));
		_length -= count;
		return this;
	}

	/// <summary>
	/// Clears all data but retains the buffer capacity.
	/// </summary>
	public BinaryDataBuilder Clear ()
	{
		_length = 0;
		return this;
	}

	/// <summary>
	/// Clears all data and releases the buffer, resetting to minimum capacity.
	/// Returns any pooled buffer to the pool.
	/// </summary>
	public BinaryDataBuilder Reset ()
	{
		if (_isPooled) {
			ArrayPool<byte>.Shared.Return (_buffer);
			_isPooled = false;
		}
		_buffer = [];
		_length = 0;
		return this;
	}

	/// <summary>
	/// Creates an immutable BinaryData from the current contents.
	/// </summary>
	public BinaryData ToBinaryData () => new (_buffer.AsSpan (0, _length));

	/// <summary>
	/// Creates a new byte array from the current contents.
	/// </summary>
	public byte[] ToArray () => _buffer.AsSpan (0, _length).ToArray ();

	/// <summary>
	/// Returns any pooled buffer to the pool.
	/// The builder can still be used after disposal, but will allocate a new buffer.
	/// </summary>
	public void Dispose ()
	{
		if (_isPooled) {
			ArrayPool<byte>.Shared.Return (_buffer);
			_buffer = [];
			_isPooled = false;
		}
		_length = 0;
	}
}
