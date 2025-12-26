// Copyright (c) 2025 Stephen Shaw and contributors
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
/// </remarks>
public static class OggCrc
{
	static readonly uint[] CrcTable = GenerateCrcTable ();

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

		foreach (var b in data)
			crc = (crc << 8) ^ CrcTable[((crc >> 24) ^ b) & 0xFF];

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

	static uint[] GenerateCrcTable ()
	{
		const uint polynomial = 0x04C11DB7;
		var table = new uint[256];

		for (uint i = 0; i < 256; i++) {
			var crc = i << 24;
			for (var j = 0; j < 8; j++) {
				if ((crc & 0x80000000) != 0)
					crc = (crc << 1) ^ polynomial;
				else
					crc <<= 1;
			}
			table[i] = crc;
		}

		return table;
	}
}
