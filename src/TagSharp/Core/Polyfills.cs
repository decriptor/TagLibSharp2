// MIT License - Copyright (c) 2025 Stephen Shaw and contributors
// Polyfills for netstandard2.0/2.1 compatibility

using System.Text;

namespace TagSharp.Core;

/// <summary>
/// Internal polyfills for older framework compatibility.
/// </summary>
internal static class Polyfills
{
#if NETSTANDARD2_0 || NETSTANDARD2_1
	/// <summary>
	/// Polyfill for ArgumentNullException.ThrowIfNull (available in .NET 6+).
	/// </summary>
	public static void ThrowIfNull (object? argument, string? paramName = null)
	{
		if (argument is null)
			throw new ArgumentNullException (paramName);
	}
#endif

#if NETSTANDARD2_0
	/// <summary>
	/// Polyfill for Array.Fill (available in .NET Core 2.0+ / netstandard2.1).
	/// </summary>
	public static void ArrayFill<T> (T[] array, T value, int startIndex, int count)
	{
		for (var i = startIndex; i < startIndex + count; i++)
			array[i] = value;
	}

	/// <summary>
	/// Polyfill for Array.Fill (available in .NET Core 2.0+ / netstandard2.1).
	/// </summary>
	public static void ArrayFill<T> (T[] array, T value)
	{
		for (var i = 0; i < array.Length; i++)
			array[i] = value;
	}
#endif

#if NETSTANDARD2_0 || NETSTANDARD2_1
	static readonly Encoding _latin1 = Encoding.GetEncoding (28591);
#endif

	/// <summary>
	/// Gets Latin1 (ISO-8859-1) encoding, available as Encoding.Latin1 in .NET 5+.
	/// </summary>
	public static Encoding Latin1 =>
#if NETSTANDARD2_0 || NETSTANDARD2_1
		_latin1;
#else
		Encoding.Latin1;
#endif

#if NETSTANDARD2_0 || NETSTANDARD2_1
	/// <summary>
	/// Polyfill for Convert.ToHexString (available in .NET 5+).
	/// </summary>
	public static string ToHexString (byte[] data)
	{
		if (data.Length == 0)
			return string.Empty;

		var chars = new char[data.Length * 2];
		const string hex = "0123456789ABCDEF";
		for (var i = 0; i < data.Length; i++) {
			chars[i * 2] = hex[data[i] >> 4];
			chars[i * 2 + 1] = hex[data[i] & 0xF];
		}
		return new string (chars);
	}

	/// <summary>
	/// Polyfill for Convert.ToHexStringLower (available in .NET 9+).
	/// </summary>
	public static string ToHexStringLower (byte[] data)
	{
		if (data.Length == 0)
			return string.Empty;

		var chars = new char[data.Length * 2];
		const string hex = "0123456789abcdef";
		for (var i = 0; i < data.Length; i++) {
			chars[i * 2] = hex[data[i] >> 4];
			chars[i * 2 + 1] = hex[data[i] & 0xF];
		}
		return new string (chars);
	}

	/// <summary>
	/// Polyfill for Convert.FromHexString (available in .NET 5+).
	/// </summary>
	public static byte[] FromHexString (string hex)
	{
		if (hex.Length == 0)
			return [];

		if (hex.Length % 2 != 0)
			throw new FormatException ("Hex string must have even length");

		var data = new byte[hex.Length / 2];
		for (var i = 0; i < data.Length; i++) {
			data[i] = (byte)((GetHexValue (hex[i * 2]) << 4) | GetHexValue (hex[i * 2 + 1]));
		}
		return data;
	}

	static int GetHexValue (char c) => c switch {
		>= '0' and <= '9' => c - '0',
		>= 'a' and <= 'f' => c - 'a' + 10,
		>= 'A' and <= 'F' => c - 'A' + 10,
		_ => throw new FormatException ($"Invalid hex character: {c}")
	};
#endif

#if NETSTANDARD2_0
	/// <summary>
	/// Polyfill for string.Replace with StringComparison (available in .NET Standard 2.1+).
	/// </summary>
	public static string Replace (string str, string oldValue, string newValue, StringComparison comparison)
	{
		if (comparison != StringComparison.Ordinal)
			throw new NotSupportedException ("Only Ordinal comparison is supported in polyfill");

		return str.Replace (oldValue, newValue);
	}

	/// <summary>
	/// Gets string from span using the specified encoding.
	/// </summary>
	public static string GetString (this Encoding encoding, ReadOnlySpan<byte> bytes) =>
		encoding.GetString (bytes.ToArray ());
#endif
}
