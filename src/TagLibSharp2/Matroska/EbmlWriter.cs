// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// Writes EBML element headers. The ID value already contains its width marker.
/// </summary>
internal static class EbmlWriter
{
	public static BinaryData Master (ulong id, ReadOnlySpan<byte> payload)
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add (Id (id));
		builder.Add (Size (payload.Length));
		builder.Add (payload);
		return builder.ToBinaryData ();
	}

	public static BinaryData Unsigned (ulong id, ulong value)
	{
		var payload = new byte[WidthOf (value)];
		for (var i = payload.Length - 1; i >= 0; i--) {
			payload[i] = (byte)value;
			value >>= 8;
		}

		return Master (id, payload);
	}

	public static BinaryData String (ulong id, string value) =>
		Master (id, System.Text.Encoding.UTF8.GetBytes (value));

	static byte[] Id (ulong id)
	{
		var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)id;
			id >>= 8;
		}

		return bytes;
	}

	static byte[] Size (int value)
	{
		if (value < 0x7F)
			return new[] { (byte)(0x80 | value) };
		if (value < 0x3FFF)
			return new[] { (byte)(0x40 | (value >> 8)), (byte)value };
		if (value < 0x1FFFFF)
			return new[] { (byte)(0x20 | (value >> 16)), (byte)(value >> 8), (byte)value };
		return new[] {
			(byte)(0x10 | (value >> 24)),
			(byte)(value >> 16),
			(byte)(value >> 8),
			(byte)value
		};
	}

	static int WidthOf (ulong value)
	{
		if (value <= 0xFF)
			return 1;
		var width = 0;
		while (value > 0) {
			width++;
			value >>= 8;
		}

		return width;
	}
}
