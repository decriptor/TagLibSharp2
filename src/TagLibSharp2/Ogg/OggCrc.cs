// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Ogg;

/// <summary>
/// Provides CRC-32 calculation for Ogg pages.
/// </summary>
/// <remarks>
/// <para>
/// Ogg uses a specific CRC-32 polynomial (0x04C11DB7) with these parameters:
/// </para>
/// <list type="bullet">
/// <item>Polynomial: 0x04C11DB7</item>
/// <item>Initial value: 0x00000000</item>
/// <item>No final XOR</item>
/// <item>No reflection</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/ogg/doc/framing.html
/// </para>
/// <para>
/// Eight data bytes are folded at a time. The tables are derived from the same
/// one-byte update, so the value matches that definition.
/// </para>
/// </remarks>
public static class OggCrc
{
	const int SliceWidth = 8;

	static readonly uint[][] Tables = GenerateTables ();

	/// <summary>
	/// Calculates the CRC-32 checksum for an Ogg page.
	/// </summary>
	/// <param name="data">The page data (with CRC field zeroed).</param>
	/// <returns>The calculated CRC-32 value.</returns>
	/// <remarks>
	/// Before calling this method, the CRC field (bytes 22-25) in the page
	/// should be set to zero.
	/// </remarks>
	public static uint Calculate (ReadOnlySpan<byte> data)
	{
		uint crc = 0;
		var i = 0;
		var length = data.Length;
		var table = Tables;
		while (i + SliceWidth <= length) {
			crc ^= ((uint)data[i] << 24)
				| ((uint)data[i + 1] << 16)
				| ((uint)data[i + 2] << 8)
				| data[i + 3];
			var low = ((uint)data[i + 4] << 24)
				| ((uint)data[i + 5] << 16)
				| ((uint)data[i + 6] << 8)
				| data[i + 7];
			crc = table[7][crc >> 24]
				^ table[6][(crc >> 16) & 0xFF]
				^ table[5][(crc >> 8) & 0xFF]
				^ table[4][crc & 0xFF]
				^ table[3][low >> 24]
				^ table[2][(low >> 16) & 0xFF]
				^ table[1][(low >> 8) & 0xFF]
				^ table[0][low & 0xFF];
			i += SliceWidth;
		}

		var byteTable = table[0];
		for (; i < length; i++)
			crc = (crc << 8) ^ byteTable[((crc >> 24) ^ data[i]) & 0xFF];

		return crc;
	}

	/// <summary>
	/// Calculates the CRC-32 checksum for an Ogg page.
	/// </summary>
	/// <param name="data">The page data (with CRC field zeroed).</param>
	/// <returns>The calculated CRC-32 value.</returns>
	public static uint Calculate (byte[] data) => Calculate (data.AsSpan ());

	/// <summary>
	/// Validates the CRC-32 checksum of an Ogg page.
	/// </summary>
	/// <param name="data">The complete page data including CRC.</param>
	/// <returns>True if the CRC is valid, false otherwise.</returns>
	public static bool Validate (ReadOnlySpan<byte> data)
	{
		if (data.Length < 27) // Minimum Ogg page header size
			return false;

		// Extract stored CRC (bytes 22-25, little-endian)
		var storedCrc = (uint)(data[22] | (data[23] << 8) | (data[24] << 16) | (data[25] << 24));

		// Copy data and zero CRC field for calculation
		var dataCopy = data.ToArray ();
		dataCopy[22] = 0;
		dataCopy[23] = 0;
		dataCopy[24] = 0;
		dataCopy[25] = 0;

		var calculatedCrc = Calculate (dataCopy);

		return storedCrc == calculatedCrc;
	}

	static uint[][] GenerateTables ()
	{
		const uint polynomial = 0x04C11DB7;
		var tables = new uint[SliceWidth][];
		var byteTable = new uint[256];
		for (uint i = 0; i < 256; i++) {
			var crc = i << 24;
			for (var bit = 0; bit < 8; bit++)
				crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ polynomial : crc << 1;
			byteTable[i] = crc;
		}

		tables[0] = byteTable;
		for (var slice = 1; slice < SliceWidth; slice++) {
			var derived = new uint[256];
			var previous = tables[slice - 1];
			for (var i = 0; i < 256; i++) {
				var crc = previous[i];
				derived[i] = (crc << 8) ^ byteTable[crc >> 24];
			}

			tables[slice] = derived;
		}

		return tables;
	}
}
