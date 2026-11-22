// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// One Description Record of a Metadata Object or Metadata Library Object.
/// </summary>
/// <remarks>
/// <para>
/// Both objects use the same record layout (ASF spec rev. 01.20.03 §4.7 and §4.8): a WORD that is Reserved in the
/// Metadata Object and the Language List Index in the Metadata Library Object, then Stream Number (WORD), Name
/// Length (WORD), Data Type (WORD), Data Length (DWORD), the UTF-16LE name with its terminator, and the data.
/// Stream Number 0 means the whole file.
/// </para>
/// <para>
/// Data types: 0 Unicode string, 1 byte array, 2 BOOL (a WORD here, not the DWORD of §3.11), 3 DWORD, 4 QWORD,
/// 5 WORD, 6 GUID.
/// </para>
/// </remarks>
internal sealed class AsfMetadataRecord
{
	const int HeaderSize = 12;

	AsfMetadataRecord (ushort first, ushort stream, byte[] rawName, ushort type, byte[] data)
	{
		First = first;
		Stream = stream;
		RawName = rawName;
		Type = type;
		Data = data;
		Name = TrimTerminator (Encoding.Unicode.GetString (rawName));
	}

	/// <summary>The Reserved WORD (Metadata Object) or Language List Index (Metadata Library Object).</summary>
	public ushort First { get; }

	public ushort Stream { get; }

	/// <summary>The name bytes as stored, so an unchanged record renders the same bytes.</summary>
	public byte[] RawName { get; }

	public ushort Type { get; }

	public byte[] Data { get; }

	public string Name { get; }

	/// <summary>True for a record about the whole file rather than one stream.</summary>
	public bool IsFileLevel => Stream == 0;

	/// <summary>
	/// The value as text: a Unicode string, or a WORD, DWORD, or QWORD in decimal. Null for BOOL, byte array,
	/// GUID, and values shorter than their type.
	/// </summary>
	public string? Text => Type switch {
		0 => TrimTerminator (Encoding.Unicode.GetString (Data)),
		3 when Data.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian (Data).ToString (CultureInfo.InvariantCulture),
		4 when Data.Length >= 8 => BinaryPrimitives.ReadUInt64LittleEndian (Data).ToString (CultureInfo.InvariantCulture),
		5 when Data.Length >= 2 => BinaryPrimitives.ReadUInt16LittleEndian (Data).ToString (CultureInfo.InvariantCulture),
		_ => null
	};

	/// <summary>
	/// The value as a BOOL, which the Metadata and Metadata Library Objects store as a WORD (ASF §4.7, §4.8); a
	/// DWORD-sized BOOL reads too. Null for other types and for data shorter than a WORD.
	/// </summary>
	public bool? Bool => Type == 2 && Data.Length >= 2 ? Array.Exists (Data, b => b != 0) : null;

	/// <summary>The value as a number: a DWORD, or a Unicode string holding one. Null otherwise.</summary>
	public uint? Number => Type switch {
		3 when Data.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian (Data),
		0 => uint.TryParse (Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null,
		_ => null
	};

	/// <summary>A file-level Unicode string record in the default language.</summary>
	public static AsfMetadataRecord CreateString (string name, string value) =>
		new (0, 0, Encoding.Unicode.GetBytes (name + "\0"), 0, Encoding.Unicode.GetBytes (value + "\0"));

	int Size => HeaderSize + RawName.Length + Data.Length;

	static string TrimTerminator (string text)
	{
		var end = text.Length;
		while (end > 0 && text[end - 1] == '\0')
			end--;
		return text.Substring (0, end);
	}

	/// <summary>
	/// Parses the content of a Metadata Object or Metadata Library Object (after its GUID and size).
	/// </summary>
	/// <returns>False when a count or length runs past the object; nothing is returned then.</returns>
	public static bool TryParseObject (ReadOnlySpan<byte> content, out List<AsfMetadataRecord> records)
	{
		records = [];
		if (content.Length < 2)
			return false;

		var count = BinaryPrimitives.ReadUInt16LittleEndian (content);
		var offset = 2;
		for (var i = 0; i < count; i++) {
			if (content.Length - offset < HeaderSize)
				return false;
			var record = content.Slice (offset);
			var nameLength = BinaryPrimitives.ReadUInt16LittleEndian (record.Slice (4));
			var dataLength = BinaryPrimitives.ReadUInt32LittleEndian (record.Slice (8));
			var available = record.Length - HeaderSize;
			if (nameLength > available || dataLength > (uint)(available - nameLength))
				return false;

			records.Add (new AsfMetadataRecord (
				BinaryPrimitives.ReadUInt16LittleEndian (record),
				BinaryPrimitives.ReadUInt16LittleEndian (record.Slice (2)),
				record.Slice (HeaderSize, nameLength).ToArray (),
				BinaryPrimitives.ReadUInt16LittleEndian (record.Slice (6)),
				record.Slice (HeaderSize + nameLength, (int)dataLength).ToArray ()));
			offset += HeaderSize + nameLength + (int)dataLength;
		}

		return true;
	}

	/// <summary>
	/// Renders a whole Metadata Object or Metadata Library Object: GUID, size, record count, records.
	/// </summary>
	public static byte[] RenderObject (AsfGuid guid, IReadOnlyList<AsfMetadataRecord> records)
	{
		var size = 24 + 2;
		foreach (var record in records)
			size += record.Size;

		var result = new byte[size];
		guid.Render ().Span.CopyTo (result);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (16), (ulong)size);
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (24), (ushort)records.Count);
		var offset = 26;
		foreach (var record in records) {
			var span = result.AsSpan (offset);
			BinaryPrimitives.WriteUInt16LittleEndian (span, record.First);
			BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (2), record.Stream);
			BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (4), (ushort)record.RawName.Length);
			BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (6), record.Type);
			BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (8), (uint)record.Data.Length);
			record.RawName.CopyTo (result, offset + HeaderSize);
			record.Data.CopyTo (result, offset + HeaderSize + record.RawName.Length);
			offset += record.Size;
		}

		return result;
	}
}
