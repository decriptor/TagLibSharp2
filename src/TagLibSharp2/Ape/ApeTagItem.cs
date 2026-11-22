// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Text;

#pragma warning disable CA1819 // Properties should not return arrays - by design for binary data access
#pragma warning disable CA1054 // URI parameters should not be strings - external locator uses string by design
#pragma warning disable CA1062 // Validate arguments of public methods - validated in ValidateKey

namespace TagLibSharp2.Ape;

/// <summary>
/// Represents the result of parsing an APE tag item.
/// </summary>
public readonly struct ApeTagItemParseResult : IEquatable<ApeTagItemParseResult>
{
	/// <summary>
	/// Gets the parsed APE tag item, or null if parsing failed.
	/// </summary>
	public ApeTagItem? Item { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed during parsing.
	/// </summary>
	public int BytesConsumed { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Item is not null && Error is null;

	private ApeTagItemParseResult (ApeTagItem? item, string? error, int bytesConsumed)
	{
		Item = item;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="item">The parsed APE tag item.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result containing the item.</returns>
	public static ApeTagItemParseResult Success (ApeTagItem item, int bytesConsumed) =>
		new (item, null, bytesConsumed);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static ApeTagItemParseResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (ApeTagItemParseResult other) =>
		Equals (Item, other.Item) && Error == other.Error && BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is ApeTagItemParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Item, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (ApeTagItemParseResult left, ApeTagItemParseResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (ApeTagItemParseResult left, ApeTagItemParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents binary data from an APE tag item (e.g., cover art).
/// </summary>
public sealed class ApeBinaryData
{
	/// <summary>
	/// Optional filename or description (may be empty).
	/// </summary>
	public string Filename { get; }

	/// <summary>
	/// The binary data.
	/// </summary>
	public byte[] Data { get; }

	/// <summary>
	/// Creates a new instance of <see cref="ApeBinaryData"/>.
	/// </summary>
	/// <param name="filename">The filename or description.</param>
	/// <param name="data">The binary data.</param>
	public ApeBinaryData (string filename, byte[] data)
	{
		Filename = filename;
		Data = data;
	}
}

/// <summary>
/// Represents an APE tag item (key-value pair).
/// </summary>
public sealed class ApeTagItem
{
	/// <summary>
	/// Reserved keys that cannot be used.
	/// </summary>
	private static readonly string[] ReservedKeys = ["ID3", "TAG", "OggS", "MP+"];

	/// <summary>
	/// Minimum item header size (valueSize + flags).
	/// </summary>
	public const int MinHeaderSize = 8;

	/// <summary>
	/// Minimum key length.
	/// </summary>
	public const int MinKeyLength = 2;

	/// <summary>
	/// Maximum key length.
	/// </summary>
	public const int MaxKeyLength = 255;

	/// <summary>
	/// The item key (ASCII, 2-255 characters).
	/// </summary>
	public string Key { get; }

	/// <summary>
	/// The raw value bytes.
	/// </summary>
	public byte[] RawValue { get; }

	/// <summary>
	/// Raw flags value.
	/// </summary>
	public uint Flags { get; }

	/// <summary>
	/// The type of this item (text, binary, or external locator).
	/// </summary>
	public ApeItemType ItemType => (ApeItemType)((Flags >> 1) & 0x03);

	/// <summary>
	/// True if this item is read-only.
	/// </summary>
	public bool IsReadOnly => (Flags & 0x01) != 0;

	/// <summary>
	/// Gets the value as a UTF-8 string (for text and external locator items).
	/// </summary>
	public string? ValueAsString {
		get {
			if (ItemType != ApeItemType.Text && ItemType != ApeItemType.ExternalLocator)
				return null;

			// A text value is not NUL-terminated. A zero byte separates values.
			// An empty part, including a trailing zero, is not a value.
			var text = Encoding.UTF8.GetString (RawValue);
			var parts = text.Split ('\0');
			string? joined = null;
			foreach (var part in parts) {
				if (part.Length == 0)
					continue;
				joined = joined is null ? part : joined + ", " + part;
			}

			return joined ?? string.Empty;
		}
	}

	/// <summary>
	/// Gets the value as binary data (for binary items like cover art).
	/// Returns null for non-binary items.
	/// </summary>
	public ApeBinaryData? BinaryValue {
		get {
			if (ItemType != ApeItemType.Binary)
				return null;

			// Binary format: filename + null + data
			var nullIndex = Array.IndexOf (RawValue, (byte)0);
			if (nullIndex < 0) {
				// No filename separator, treat entire value as data
				return new ApeBinaryData ("", RawValue);
			}

			var filename = Encoding.ASCII.GetString (RawValue, 0, nullIndex);
			var data = new byte[RawValue.Length - nullIndex - 1];
			Array.Copy (RawValue, nullIndex + 1, data, 0, data.Length);
			return new ApeBinaryData (filename, data);
		}
	}

	private ApeTagItem (string key, byte[] rawValue, uint flags)
	{
		Key = key;
		RawValue = rawValue;
		Flags = flags;
	}

	/// <summary>
	/// Parses an APE tag item from binary data.
	/// </summary>
	public static ApeTagItemParseResult Parse (ReadOnlySpan<byte> data)
	{
		var error = Measure (data, data.Length, out var keyLength, out var valueSize, out var flags, out var consumed);
		if (error is not null)
			return ApeTagItemParseResult.Failure (error);

		var key = Encoding.ASCII.GetString (data.Slice (MinHeaderSize, keyLength).ToArray ());
		var valueStart = MinHeaderSize + keyLength + 1;
		var rawValue = data.Slice (valueStart, (int)valueSize).ToArray ();
		return ApeTagItemParseResult.Success (new ApeTagItem (key, rawValue, flags), (int)consumed);
	}

	/// <summary>
	/// The item rules of <see cref="Parse"/> without copying anything: true when an item starts at the front of
	/// <paramref name="head"/>, and <paramref name="consumed"/> is its length. <paramref name="head"/> holds at
	/// least the header and the key (or every remaining byte); <paramref name="remaining"/> is how many bytes the
	/// item region has from this item to its end, so the value is bounded without being read.
	/// </summary>
	internal static bool TryMeasure (ReadOnlySpan<byte> head, long remaining, out long consumed)
	{
		var ok = Measure (head, remaining, out _, out _, out _, out consumed) is null;
		if (!ok)
			consumed = 0;
		return ok;
	}

	/// <summary>
	/// The bytes <see cref="TryMeasure"/> needs in its window: the header, the longest key, and its NUL.
	/// </summary>
	internal const int MeasureWindow = MinHeaderSize + MaxKeyLength + 1;

	// Every accept rule of an item, shared by Parse and TryMeasure. Returns null when the item is valid.
	static string? Measure (ReadOnlySpan<byte> head, long remaining, out int keyLength, out uint valueSize, out uint flags, out long consumed)
	{
		keyLength = 0;
		valueSize = 0;
		flags = 0;
		consumed = 0;
		if (head.Length < MinHeaderSize || remaining < MinHeaderSize)
			return $"Data too short for APE item header: {Math.Min (head.Length, remaining)} bytes";

		valueSize = BinaryPrimitives.ReadUInt32LittleEndian (head);
		flags = BinaryPrimitives.ReadUInt32LittleEndian (head[4..]);

		// Validate flags - only bits 0-2 should be used
		if ((flags & 0xFFFFFFF8) != 0) {
			// Some implementations ignore this, so we'll just mask it
			flags &= 0x07;
		}

		// Find key: starts at offset 8, ends with null byte. TryMeasure passes only the first MeasureWindow bytes:
		// a key that has no NUL there is longer than MaxKeyLength, or not terminated, and fails either way.
		var keyEnd = -1;
		var searchEnd = (int)Math.Min (head.Length, remaining);
		for (var i = MinHeaderSize; i < searchEnd; i++) {
			if (head[i] == 0) {
				keyEnd = i;
				break;
			}
		}

		if (keyEnd < 0) {
			return "APE item key not null-terminated";
		}

		keyLength = keyEnd - MinHeaderSize;

		// The one copy of the key rules (KeyError). The key is widened into a stack buffer only when its
		// length is in range, so measuring copies nothing to the heap.
		var keySpan = head.Slice (MinHeaderSize, keyLength);
		string? keyError;
		if (keyLength is >= MinKeyLength and <= MaxKeyLength) {
			Span<char> keyChars = stackalloc char[keyLength];
			for (var i = 0; i < keyLength; i++)
				keyChars[i] = (char)keySpan[i];
			keyError = KeyError (keyLength, keyChars);
		} else {
			keyError = KeyError (keyLength, []);
		}
		if (keyError is not null)
			return keyError;

		// Value length is a uint32. Compare it with the bytes still left before any int add or Slice:
		// 0x80000000 and 0xFFFFFFF0 cast negative, and int.MaxValue overflows the end offset.
		var valueStart = keyEnd + 1;
		var available = remaining - valueStart;
		if (valueSize > available) {
			return $"APE item value extends beyond data: value size {valueSize} exceeds {available} bytes remaining";
		}

		consumed = valueStart + valueSize;
		return null;
	}

	/// <summary>
	/// Creates a text item.
	/// </summary>
	public static ApeTagItem CreateText (string key, string value, bool isReadOnly = false)
	{
		ValidateKey (key);
		var rawValue = Encoding.UTF8.GetBytes (value);
		uint flags = 0; // Text type
		if (isReadOnly) flags |= 0x01;
		return new ApeTagItem (key, rawValue, flags);
	}

	/// <summary>
	/// Creates a binary item (e.g., cover art).
	/// </summary>
	public static ApeTagItem CreateBinary (string key, string filename, byte[] data, bool isReadOnly = false)
	{
		ValidateKey (key);

		// Binary format: filename + null + data
		var filenameBytes = Encoding.ASCII.GetBytes (filename);
		var rawValue = new byte[filenameBytes.Length + 1 + data.Length];
		filenameBytes.CopyTo (rawValue, 0);
		// Null separator at filenameBytes.Length is already zero
		data.CopyTo (rawValue, filenameBytes.Length + 1);

		uint flags = 0x02; // Binary type (bits 2-1 = 1)
		if (isReadOnly) flags |= 0x01;
		return new ApeTagItem (key, rawValue, flags);
	}

	/// <summary>
	/// Creates an external locator item.
	/// </summary>
	public static ApeTagItem CreateExternalLocator (string key, string url, bool isReadOnly = false)
	{
		ValidateKey (key);
		var rawValue = Encoding.UTF8.GetBytes (url);
		uint flags = 0x04; // External locator type (bits 2-1 = 2)
		if (isReadOnly) flags |= 0x01;
		return new ApeTagItem (key, rawValue, flags);
	}

	/// <summary>
	/// Renders the item to binary data.
	/// </summary>
	public byte[] Render ()
	{
		var keyBytes = Encoding.ASCII.GetBytes (Key);
		var totalSize = 8 + keyBytes.Length + 1 + RawValue.Length;
		var data = new byte[totalSize];

		BinaryPrimitives.WriteUInt32LittleEndian (data, (uint)RawValue.Length);
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (4), Flags);
		keyBytes.CopyTo (data, 8);
		// Null terminator at 8 + keyBytes.Length is already zero
		RawValue.CopyTo (data, 8 + keyBytes.Length + 1);

		return data;
	}

	/// <summary>
	/// True when <paramref name="key"/> follows the APEv2 item key rules
	/// (https://wiki.hydrogenaud.io/index.php?title=APE_key).
	/// </summary>
	internal static bool IsValidKey (string key) => KeyError (key.Length, key.AsSpan ()) is null;

	/// <summary>
	/// The one copy of the APEv2 key rules, shared by <see cref="Parse"/>, <see cref="TryMeasure"/>, the Create
	/// methods, and <see cref="IsValidKey"/>: 2 to 255 characters from 0x20 to 0x7E, and not ID3, TAG, OggS, or
	/// MP+ in any case (https://wiki.hydrogenaud.io/index.php?title=APE_key).
	/// </summary>
	/// <param name="length">The key's length; the characters are checked only when it is in range.</param>
	/// <param name="key">The key's characters, one per byte for a key read from a file.</param>
	/// <returns>Why the key is invalid, or null when it is valid.</returns>
	static string? KeyError (int length, ReadOnlySpan<char> key)
	{
		if (length < MinKeyLength)
			return $"APE item key too short: {length} chars, minimum is {MinKeyLength}";

		if (length > MaxKeyLength)
			return $"APE item key too long: {length} chars, maximum is {MaxKeyLength}";

		foreach (var c in key) {
			if (c < 0x20 || c > 0x7E)
				return $"APE item key contains invalid character: 0x{(int)c:X2}";
		}

		foreach (var reserved in ReservedKeys) {
			if (key.Equals (reserved.AsSpan (), StringComparison.OrdinalIgnoreCase))
				return $"APE item key '{reserved}' is reserved and cannot be used";
		}

		return null;
	}

	private static void ValidateKey (string key)
	{
		var error = KeyError (key.Length, key.AsSpan ());
		if (error is not null)
			throw new ArgumentException (error, nameof (key));
	}
}
