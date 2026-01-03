// APE Tag Item parsing and rendering
// Item structure: valueSize(4) + flags(4) + key + null + value

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
	public string? ValueAsString =>
		ItemType == ApeItemType.Text || ItemType == ApeItemType.ExternalLocator
			? Encoding.UTF8.GetString (RawValue)
			: null;

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
		if (data.Length < MinHeaderSize) {
			return ApeTagItemParseResult.Failure (
				$"Data too short for APE item header: {data.Length} bytes");
		}

		var valueSize = BinaryPrimitives.ReadUInt32LittleEndian (data);
		var flags = BinaryPrimitives.ReadUInt32LittleEndian (data[4..]);

		// Validate flags - only bits 0-2 should be used
		if ((flags & 0xFFFFFFF8) != 0) {
			// Some implementations ignore this, so we'll just mask it
			flags &= 0x07;
		}

		// Find key: starts at offset 8, ends with null byte
		var keyStart = 8;
		var keyEnd = -1;

		for (var i = keyStart; i < data.Length; i++) {
			if (data[i] == 0) {
				keyEnd = i;
				break;
			}
		}

		if (keyEnd < 0) {
			return ApeTagItemParseResult.Failure ("APE item key not null-terminated");
		}

		var keyLength = keyEnd - keyStart;

		// Validate key length
		if (keyLength < MinKeyLength) {
			return ApeTagItemParseResult.Failure (
				$"APE item key too short: {keyLength} chars, minimum is {MinKeyLength}");
		}

		if (keyLength > MaxKeyLength) {
			return ApeTagItemParseResult.Failure (
				$"APE item key too long: {keyLength} chars, maximum is {MaxKeyLength}");
		}

		// Validate key characters (ASCII 0x20-0x7E)
		var keySpan = data.Slice (keyStart, keyLength);
		foreach (var b in keySpan) {
			if (b < 0x20 || b > 0x7E) {
				return ApeTagItemParseResult.Failure (
					$"APE item key contains invalid character: 0x{b:X2}");
			}
		}

		var key = Encoding.ASCII.GetString (keySpan.ToArray ());

		// Check for reserved keys
		foreach (var reserved in ReservedKeys) {
			if (key.Equals (reserved, StringComparison.OrdinalIgnoreCase)) {
				return ApeTagItemParseResult.Failure (
					$"APE item key '{key}' is reserved and cannot be used");
			}
		}

		// Extract value
		var valueStart = keyEnd + 1;
		var expectedEnd = valueStart + (int)valueSize;

		if (expectedEnd > data.Length) {
			return ApeTagItemParseResult.Failure (
				$"APE item value extends beyond data: need {expectedEnd} bytes, have {data.Length}");
		}

		var rawValue = data.Slice (valueStart, (int)valueSize).ToArray ();

		var item = new ApeTagItem (key, rawValue, flags);
		return ApeTagItemParseResult.Success (item, expectedEnd);
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

	private static void ValidateKey (string key)
	{
		if (key.Length < MinKeyLength)
			throw new ArgumentException ($"Key too short: minimum is {MinKeyLength} characters", nameof (key));

		if (key.Length > MaxKeyLength)
			throw new ArgumentException ($"Key too long: maximum is {MaxKeyLength} characters", nameof (key));

		foreach (var c in key) {
			if (c < 0x20 || c > 0x7E)
				throw new ArgumentException ($"Key contains invalid character: '{c}'", nameof (key));
		}

		foreach (var reserved in ReservedKeys) {
			if (key.Equals (reserved, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException ($"Key '{key}' is reserved", nameof (key));
		}
	}
}
