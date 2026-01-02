// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents the ASF Extended Content Description Object containing WM/* attributes.
/// </summary>
/// <remarks>
/// Reference: ASF Specification Section 3.11.
/// Contains a list of typed descriptors (key-value pairs).
/// </remarks>
public sealed class AsfExtendedContentDescription
{
	/// <summary>
	/// Gets the list of descriptors.
	/// </summary>
	public IReadOnlyList<AsfDescriptor> Descriptors { get; }

	/// <summary>
	/// Creates a new Extended Content Description with the specified descriptors.
	/// </summary>
	public AsfExtendedContentDescription (IReadOnlyList<AsfDescriptor> descriptors)
	{
		Descriptors = descriptors;
	}

	/// <summary>
	/// Gets a descriptor by name (case-insensitive).
	/// </summary>
	/// <param name="name">The descriptor name to find.</param>
	/// <returns>The descriptor, or null if not found.</returns>
	public AsfDescriptor? GetDescriptor (string name)
	{
		for (int i = 0; i < Descriptors.Count; i++) {
			if (string.Equals (Descriptors[i].Name, name, StringComparison.OrdinalIgnoreCase))
				return Descriptors[i];
		}
		return null;
	}

	/// <summary>
	/// Gets a string value by descriptor name.
	/// </summary>
	public string? GetString (string name) => GetDescriptor (name)?.StringValue;

	/// <summary>
	/// Gets a DWORD value by descriptor name.
	/// </summary>
	public uint? GetDword (string name) => GetDescriptor (name)?.DwordValue;

	/// <summary>
	/// Gets a QWORD value by descriptor name.
	/// </summary>
	public ulong? GetQword (string name) => GetDescriptor (name)?.QwordValue;

	/// <summary>
	/// Gets a boolean value by descriptor name.
	/// </summary>
	public bool? GetBool (string name) => GetDescriptor (name)?.BoolValue;

	/// <summary>
	/// Parses an Extended Content Description Object from binary data.
	/// </summary>
	/// <param name="data">The object content (after GUID and size).</param>
	public static AsfExtendedContentDescriptionParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < 2)
			return AsfExtendedContentDescriptionParseResult.Failure ("Insufficient data for descriptor count");

		var descriptorCount = BinaryPrimitives.ReadUInt16LittleEndian (data);
		var offset = 2;
		var descriptors = new List<AsfDescriptor> (descriptorCount);

		for (int i = 0; i < descriptorCount; i++) {
			// Read name length
			if (offset + 2 > data.Length)
				return AsfExtendedContentDescriptionParseResult.Failure ($"Truncated at descriptor {i} name length");

			var nameLength = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;

			// Read name
			if (offset + nameLength > data.Length)
				return AsfExtendedContentDescriptionParseResult.Failure ($"Truncated at descriptor {i} name");

			var name = ReadUtf16String (data.Slice (offset, nameLength));
			offset += nameLength;

			// Read value type
			if (offset + 2 > data.Length)
				return AsfExtendedContentDescriptionParseResult.Failure ($"Truncated at descriptor {i} type");

			var type = (AsfAttributeType)BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;

			// Read value length
			if (offset + 2 > data.Length)
				return AsfExtendedContentDescriptionParseResult.Failure ($"Truncated at descriptor {i} value length");

			var valueLength = BinaryPrimitives.ReadUInt16LittleEndian (data[offset..]);
			offset += 2;

			// Read value
			if (offset + valueLength > data.Length)
				return AsfExtendedContentDescriptionParseResult.Failure ($"Truncated at descriptor {i} value");

			var valueData = data.Slice (offset, valueLength).ToArray ();
			offset += valueLength;

			var descriptor = CreateDescriptor (name, type, valueData);
			descriptors.Add (descriptor);
		}

		var result = new AsfExtendedContentDescription (descriptors);
		return AsfExtendedContentDescriptionParseResult.Success (result, offset);
	}

	/// <summary>
	/// Renders the Extended Content Description to binary data.
	/// </summary>
	public BinaryData Render ()
	{
		// Calculate total size
		var totalSize = 2; // Descriptor count
		foreach (var desc in Descriptors) {
			var nameBytes = desc.RenderName ();
			var valueBytes = desc.RenderValue ();
			totalSize += 2; // Name length
			totalSize += nameBytes.Length;
			totalSize += 2; // Value type
			totalSize += 2; // Value length
			totalSize += valueBytes.Length;
		}

		var result = new byte[totalSize];
		var offset = 0;

		// Write descriptor count
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)Descriptors.Count);
		offset += 2;

		// Write each descriptor
		foreach (var desc in Descriptors) {
			var nameBytes = desc.RenderName ().ToArray ();
			var valueBytes = desc.RenderValue ().ToArray ();

			// Name length
			BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)nameBytes.Length);
			offset += 2;

			// Name
			Array.Copy (nameBytes, 0, result, offset, nameBytes.Length);
			offset += nameBytes.Length;

			// Value type
			BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)desc.Type);
			offset += 2;

			// Value length
			BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)valueBytes.Length);
			offset += 2;

			// Value
			Array.Copy (valueBytes, 0, result, offset, valueBytes.Length);
			offset += valueBytes.Length;
		}

		return new BinaryData (result);
	}

	static string ReadUtf16String (ReadOnlySpan<byte> data)
	{
		if (data.Length == 0)
			return string.Empty;

		// Remove null terminator if present
		var length = data.Length;
		if (length >= 2 && data[length - 1] == 0 && data[length - 2] == 0)
			length -= 2;

		if (length == 0)
			return string.Empty;

		return Encoding.Unicode.GetString (data[..length]);
	}

	static AsfDescriptor CreateDescriptor (string name, AsfAttributeType type, byte[] rawValue)
	{
		return type switch {
			AsfAttributeType.UnicodeString => CreateStringDescriptor (name, rawValue),
			AsfAttributeType.Dword => AsfDescriptor.CreateDword (name, BinaryPrimitives.ReadUInt32LittleEndian (rawValue)),
			AsfAttributeType.Qword => AsfDescriptor.CreateQword (name, BinaryPrimitives.ReadUInt64LittleEndian (rawValue)),
			AsfAttributeType.Word => AsfDescriptor.CreateWord (name, BinaryPrimitives.ReadUInt16LittleEndian (rawValue)),
			AsfAttributeType.Bool => AsfDescriptor.CreateBool (name, BinaryPrimitives.ReadUInt32LittleEndian (rawValue) != 0),
			AsfAttributeType.Binary => AsfDescriptor.CreateBinary (name, rawValue),
			_ => AsfDescriptor.CreateBinary (name, rawValue) // Unknown types treated as binary
		};
	}

	static AsfDescriptor CreateStringDescriptor (string name, byte[] rawValue)
	{
		var value = ReadUtf16String (rawValue);
		return AsfDescriptor.CreateString (name, value);
	}
}

/// <summary>
/// Result of parsing an Extended Content Description Object.
/// </summary>
public readonly struct AsfExtendedContentDescriptionParseResult : IEquatable<AsfExtendedContentDescriptionParseResult>
{
	/// <summary>
	/// Gets the parsed Extended Content Description.
	/// </summary>
	public AsfExtendedContentDescription Value { get; }

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

	AsfExtendedContentDescriptionParseResult (AsfExtendedContentDescription value, string? error, int bytesConsumed)
	{
		Value = value;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	public static AsfExtendedContentDescriptionParseResult Success (AsfExtendedContentDescription value, int bytesConsumed)
		=> new (value, null, bytesConsumed);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	public static AsfExtendedContentDescriptionParseResult Failure (string error)
		=> new (null!, error, 0);

	/// <inheritdoc/>
	public bool Equals (AsfExtendedContentDescriptionParseResult other)
		=> BytesConsumed == other.BytesConsumed && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfExtendedContentDescriptionParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode ()
		=> HashCode.Combine (BytesConsumed, Error);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfExtendedContentDescriptionParseResult left, AsfExtendedContentDescriptionParseResult right)
		=> left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfExtendedContentDescriptionParseResult left, AsfExtendedContentDescriptionParseResult right)
		=> !left.Equals (right);
}
