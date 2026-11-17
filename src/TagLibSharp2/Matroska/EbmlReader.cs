// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Matroska;

/// <summary>
/// One EBML element. RFC 8794. A data length of -1 is an unknown-size element.
/// </summary>
internal readonly struct EbmlElement
{
	public EbmlElement (ulong id, int position, int headerLength, int dataOffset, long dataLength)
	{
		Id = id;
		Position = position;
		HeaderLength = headerLength;
		DataOffset = dataOffset;
		DataLength = dataLength;
	}

	public ulong Id { get; }

	public int Position { get; }

	public int HeaderLength { get; }

	public int DataOffset { get; }

	/// <summary>Gets the payload length, or -1 when the size is unknown.</summary>
	public long DataLength { get; }

	public bool UnknownSize => DataLength < 0;

	public int End => UnknownSize ? -1 : DataOffset + (int)DataLength;
}

/// <summary>
/// Reads EBML variable-length integers and element headers without copying the payload.
/// </summary>
internal static class EbmlReader
{
	/// <summary>
	/// Reads an element header. The payload does not have to be in <paramref name="data"/>.
	/// <paramref name="dataLength"/> is -1 when the size is unknown.
	/// </summary>
	public static bool TryPeekHeader (ReadOnlySpan<byte> data, out ulong id, out int headerLength, out long dataLength)
	{
		id = 0;
		headerLength = 0;
		dataLength = -1;
		if (!TryReadVint (data, 0, data.Length, keepMarker: true, maxWidth: 4, out id, out var idWidth, out _))
			return false;
		if (!TryReadVint (data, idWidth, data.Length, keepMarker: false, maxWidth: 8, out var size, out var sizeWidth, out var unknown))
			return false;

		headerLength = idWidth + sizeWidth;
		if (!unknown)
			dataLength = (long)size;
		return true;
	}

	public static bool TryRead (ReadOnlySpan<byte> data, int offset, int limit, out EbmlElement element)
	{
		element = default;
		if ((uint)offset >= (uint)limit || limit > data.Length)
			return false;

		// Element IDs keep the width marker. The default maximum ID length is 4 octets.
		if (!TryReadVint (data, offset, limit, keepMarker: true, maxWidth: 4, out var id, out var idWidth, out _))
			return false;

		// Sizes drop the width marker. All ones means the element runs until the parent ends.
		if (!TryReadVint (data, offset + idWidth, limit, keepMarker: false, maxWidth: 8, out var size, out var sizeWidth, out var unknown))
			return false;

		var dataOffset = offset + idWidth + sizeWidth;
		if (unknown) {
			element = new EbmlElement (id, offset, idWidth + sizeWidth, dataOffset, -1);
			return true;
		}

		if (size > (ulong)(limit - dataOffset))
			return false;

		element = new EbmlElement (id, offset, idWidth + sizeWidth, dataOffset, (long)size);
		return true;
	}

	public static bool TryReadVint (
		ReadOnlySpan<byte> data,
		int offset,
		int limit,
		bool keepMarker,
		int maxWidth,
		out ulong value,
		out int width,
		out bool unknown)
	{
		value = 0;
		width = 0;
		unknown = false;
		if ((uint)offset >= (uint)limit)
			return false;

		width = Width (data[offset]);
		if (width < 1 || width > maxWidth || offset > limit - width)
			return false;

		ulong raw = 0;
		for (var i = 0; i < width; i++)
			raw = (raw << 8) | data[offset + i];

		if (keepMarker) {
			value = raw;
			return true;
		}

		var valueBits = width * 7;
		var mask = valueBits == 0 ? 0UL : (1UL << valueBits) - 1;
		value = raw & mask;
		unknown = value == mask;
		return true;
	}

	public static ulong ReadUInt (ReadOnlySpan<byte> data)
	{
		ulong value = 0;
		var length = data.Length > 8 ? 8 : data.Length;
		for (var i = 0; i < length; i++)
			value = (value << 8) | data[i];
		return value;
	}

	public static bool TryReadFloat (ReadOnlySpan<byte> data, out double value)
	{
		value = 0;
		if (data.Length == 4) {
			var bytes = new byte[4];
			data.CopyTo (bytes);
			if (BitConverter.IsLittleEndian)
				Array.Reverse (bytes);
			value = BitConverter.ToSingle (bytes, 0);
			return true;
		}

		if (data.Length == 8) {
			var bytes = new byte[8];
			data.CopyTo (bytes);
			if (BitConverter.IsLittleEndian)
				Array.Reverse (bytes);
			value = BitConverter.ToDouble (bytes, 0);
			return true;
		}

		return false;
	}

	public static string ReadString (ReadOnlySpan<byte> data)
	{
		// RFC 8794: the first NUL and every byte after it are not part of the string.
		var end = data.IndexOf ((byte)0);
		if (end >= 0)
			data = data.Slice (0, end);
		return data.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString (data.ToArray ());
	}

	static int Width (byte first)
	{
		if (first == 0)
			return 0;

		var width = 1;
		var mask = 0x80;
		while ((first & mask) == 0 && width < 8) {
			width++;
			mask >>= 1;
		}

		return width;
	}
}
