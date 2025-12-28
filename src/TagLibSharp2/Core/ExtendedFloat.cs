// Copyright (c) 2025 Stephen Shaw and contributors

namespace TagLibSharp2.Core;

/// <summary>
/// Converts between 80-bit IEEE 754 extended precision floating point format
/// and .NET double (64-bit). This format is used by AIFF files for sample rates.
/// </summary>
/// <remarks>
/// 80-bit extended precision format (big-endian):
/// - 1 bit sign
/// - 15 bits exponent (bias 16383)
/// - 64 bits mantissa (with explicit integer bit at position 63)
/// </remarks>
public static class ExtendedFloat
{
	const int ExtendedSize = 10;
	const int ExponentBias = 16383;

	/// <summary>
	/// Converts a 10-byte 80-bit extended precision float to a double.
	/// </summary>
	/// <param name="data">10 bytes in big-endian format.</param>
	/// <returns>The value as a double.</returns>
	/// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
	/// <exception cref="ArgumentException">Thrown when data is less than 10 bytes.</exception>
	public static double ToDouble (byte[] data)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (data is null)
			throw new ArgumentNullException (nameof (data));
#else
		ArgumentNullException.ThrowIfNull (data);
#endif
		return ToDouble (data.AsSpan ());
	}

	/// <summary>
	/// Converts a 10-byte 80-bit extended precision float to a double.
	/// </summary>
	/// <param name="data">10 bytes in big-endian format.</param>
	/// <returns>The value as a double.</returns>
	/// <exception cref="ArgumentException">Thrown when data is less than 10 bytes.</exception>
	public static double ToDouble (ReadOnlySpan<byte> data)
	{
		if (data.Length < ExtendedSize)
			throw new ArgumentException ($"Data must be at least {ExtendedSize} bytes.", nameof (data));

		// Extract sign (1 bit) and exponent (15 bits) from first two bytes
		int signAndExponent = (data[0] << 8) | data[1];
		int sign = (signAndExponent >> 15) & 1;
		int exponent = signAndExponent & 0x7FFF;

		// Extract 64-bit mantissa (big-endian)
		ulong mantissa = 0;
		for (int i = 0; i < 8; i++)
			mantissa = (mantissa << 8) | data[2 + i];

		// Handle zero
		if (exponent == 0 && mantissa == 0)
			return 0.0;

		// The 80-bit format has an explicit integer bit (bit 63 of mantissa)
		// For normalized numbers, this bit is 1
		// The actual fraction is the remaining 63 bits

		// Calculate the value:
		// value = (-1)^sign * 2^(exponent - bias) * (mantissa / 2^63)
		// Note: mantissa includes the integer bit, so we divide by 2^63 (not 2^64)

		double value = mantissa / (double)(1UL << 63);
		value *= Math.Pow (2, exponent - ExponentBias);

		return sign == 1 ? -value : value;
	}

	/// <summary>
	/// Converts a 10-byte 80-bit extended precision float to a double.
	/// </summary>
	/// <param name="data">BinaryData containing at least 10 bytes.</param>
	/// <returns>The value as a double.</returns>
	/// <exception cref="ArgumentException">Thrown when data is less than 10 bytes.</exception>
	public static double ToDouble (BinaryData data) => ToDouble (data.Span);

	/// <summary>
	/// Converts a double to 10-byte 80-bit extended precision format.
	/// </summary>
	/// <param name="value">The double value to convert.</param>
	/// <returns>A 10-byte array in big-endian format.</returns>
	public static byte[] FromDouble (double value)
	{
		byte[] result = new byte[ExtendedSize];

		if (value == 0.0)
			return result;

		int sign = 0;
		if (value < 0) {
			sign = 1;
			value = -value;
		}

		// Find exponent by taking log2
		int exponent = (int)Math.Floor (Math.Log (value, 2));

		// Normalize to range [1, 2)
		double normalized = value / Math.Pow (2, exponent);

		// Convert to mantissa with explicit integer bit
		// The mantissa is normalized * 2^63 (since bit 63 is the integer part)
		ulong mantissa = (ulong)(normalized * (1UL << 63));

		// Bias the exponent
		int biasedExponent = exponent + ExponentBias;

		// Combine sign and exponent
		int signAndExponent = (sign << 15) | (biasedExponent & 0x7FFF);

		// Write to result (big-endian)
		result[0] = (byte)(signAndExponent >> 8);
		result[1] = (byte)(signAndExponent & 0xFF);

		// Write mantissa (big-endian)
		for (int i = 0; i < 8; i++)
			result[2 + i] = (byte)(mantissa >> (56 - i * 8));

		return result;
	}
}
