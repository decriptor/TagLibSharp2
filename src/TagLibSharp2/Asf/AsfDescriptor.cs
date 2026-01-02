// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents an ASF metadata descriptor with typed value.
/// </summary>
/// <remarks>
/// ASF descriptors (called "attributes" in the ASF spec) store metadata values
/// with various data types including strings, integers, booleans, and binary data.
/// </remarks>
public sealed class AsfDescriptor
{
	/// <summary>
	/// Gets the descriptor name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the descriptor data type.
	/// </summary>
	public AsfAttributeType Type { get; }

	/// <summary>
	/// Gets the raw binary value.
	/// </summary>
	public BinaryData RawValue { get; }

	/// <summary>
	/// Gets the stream number this descriptor applies to (0 = file-level).
	/// </summary>
	public ushort StreamNumber { get; }

	/// <summary>
	/// Gets the language index for this descriptor.
	/// </summary>
	public ushort LanguageIndex { get; }

	AsfDescriptor (string name, AsfAttributeType type, BinaryData rawValue, ushort streamNumber = 0, ushort languageIndex = 0)
	{
		Name = name;
		Type = type;
		RawValue = rawValue;
		StreamNumber = streamNumber;
		LanguageIndex = languageIndex;
	}

	// ═══════════════════════════════════════════════════════════════
	// Typed Accessors
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Gets the value as a string, or null if not a string type.
	/// </summary>
	public string? StringValue {
		get {
			if (Type != AsfAttributeType.UnicodeString)
				return null;

			var bytes = RawValue.ToArray ();
			// Remove null terminator if present
			int length = bytes.Length;
			if (length >= 2 && bytes[length - 1] == 0 && bytes[length - 2] == 0)
				length -= 2;

			return Encoding.Unicode.GetString (bytes, 0, length);
		}
	}

	/// <summary>
	/// Gets the value as a DWORD (uint32), or null if not a DWORD type.
	/// </summary>
	public uint? DwordValue {
		get {
			if (Type != AsfAttributeType.Dword || RawValue.Length < 4)
				return null;
			return BinaryPrimitives.ReadUInt32LittleEndian (RawValue.Span);
		}
	}

	/// <summary>
	/// Gets the value as a QWORD (uint64), or null if not a QWORD type.
	/// </summary>
	public ulong? QwordValue {
		get {
			if (Type != AsfAttributeType.Qword || RawValue.Length < 8)
				return null;
			return BinaryPrimitives.ReadUInt64LittleEndian (RawValue.Span);
		}
	}

	/// <summary>
	/// Gets the value as a WORD (uint16), or null if not a WORD type.
	/// </summary>
	public ushort? WordValue {
		get {
			if (Type != AsfAttributeType.Word || RawValue.Length < 2)
				return null;
			return BinaryPrimitives.ReadUInt16LittleEndian (RawValue.Span);
		}
	}

	/// <summary>
	/// Gets the value as a boolean, or null if not a boolean type.
	/// </summary>
	public bool? BoolValue {
		get {
			if (Type != AsfAttributeType.Bool || RawValue.Length < 4)
				return null;
			var val = BinaryPrimitives.ReadUInt32LittleEndian (RawValue.Span);
			return val != 0;
		}
	}

	/// <summary>
	/// Gets the value as binary data, or null if not a binary type.
	/// </summary>
	public BinaryData? BinaryValue {
		get {
			if (Type != AsfAttributeType.Binary)
				return null;
			return RawValue;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Factory Methods
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Creates a string descriptor.
	/// </summary>
	public static AsfDescriptor CreateString (string name, string value)
	{
		var bytes = Encoding.Unicode.GetBytes (value);
		var withNull = new byte[bytes.Length + 2]; // Add null terminator
		Array.Copy (bytes, withNull, bytes.Length);
		return new AsfDescriptor (name, AsfAttributeType.UnicodeString, new BinaryData (withNull));
	}

	/// <summary>
	/// Creates a DWORD (32-bit unsigned integer) descriptor.
	/// </summary>
	public static AsfDescriptor CreateDword (string name, uint value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (bytes, value);
		return new AsfDescriptor (name, AsfAttributeType.Dword, new BinaryData (bytes));
	}

	/// <summary>
	/// Creates a QWORD (64-bit unsigned integer) descriptor.
	/// </summary>
	public static AsfDescriptor CreateQword (string name, ulong value)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian (bytes, value);
		return new AsfDescriptor (name, AsfAttributeType.Qword, new BinaryData (bytes));
	}

	/// <summary>
	/// Creates a WORD (16-bit unsigned integer) descriptor.
	/// </summary>
	public static AsfDescriptor CreateWord (string name, ushort value)
	{
		var bytes = new byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian (bytes, value);
		return new AsfDescriptor (name, AsfAttributeType.Word, new BinaryData (bytes));
	}

	/// <summary>
	/// Creates a boolean descriptor.
	/// </summary>
	public static AsfDescriptor CreateBool (string name, bool value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (bytes, value ? 1u : 0u);
		return new AsfDescriptor (name, AsfAttributeType.Bool, new BinaryData (bytes));
	}

	/// <summary>
	/// Creates a binary descriptor.
	/// </summary>
	public static AsfDescriptor CreateBinary (string name, byte[] value)
	{
		return new AsfDescriptor (name, AsfAttributeType.Binary, new BinaryData (value));
	}

	/// <summary>
	/// Creates a binary descriptor from BinaryData.
	/// </summary>
	public static AsfDescriptor CreateBinary (string name, BinaryData value)
	{
		return new AsfDescriptor (name, AsfAttributeType.Binary, value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Rendering
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Renders the descriptor value to binary data.
	/// </summary>
	public BinaryData RenderValue ()
	{
		return RawValue;
	}

	/// <summary>
	/// Renders the descriptor name to UTF-16LE with null terminator.
	/// </summary>
	public BinaryData RenderName ()
	{
		var bytes = Encoding.Unicode.GetBytes (Name);
		var withNull = new byte[bytes.Length + 2];
		Array.Copy (bytes, withNull, bytes.Length);
		return new BinaryData (withNull);
	}
}
