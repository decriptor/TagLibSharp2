// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

#pragma warning disable CA1054 // URI parameters should not be strings
#pragma warning disable CA1056 // URI properties should not be strings

/// <summary>
/// Represents an ID3v2 WXXX (User-defined URL link) frame.
/// </summary>
/// <remarks>
/// WXXX frame format:
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding (0=Latin-1, 1=UTF-16 w/BOM, 2=UTF-16BE, 3=UTF-8)
/// 1       n     Description (null-terminated, uses specified encoding)
/// 1+n     m     URL (always Latin-1, not null-terminated)
/// </code>
/// Unlike other text frames, the URL part is always Latin-1 encoded.
/// </remarks>
public sealed class UserUrlFrame
{
	/// <summary>
	/// Gets the frame ID (always "WXXX").
	/// </summary>
	public static string FrameId => "WXXX";

	/// <summary>
	/// Gets or sets the description identifying this URL.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Gets or sets the URL.
	/// </summary>
	public string Url { get; set; }

	/// <summary>
	/// Gets or sets the text encoding for the description.
	/// </summary>
	/// <remarks>
	/// The URL is always Latin-1 regardless of this setting.
	/// </remarks>
	public TextEncodingType Encoding { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="UserUrlFrame"/> class.
	/// </summary>
	/// <param name="url">The URL.</param>
	/// <param name="description">The description (defaults to empty).</param>
	/// <param name="encoding">The text encoding for the description (defaults to UTF-8).</param>
	public UserUrlFrame (string url, string description = "", TextEncodingType encoding = TextEncodingType.Utf8)
	{
		Url = url;
		Description = description;
		Encoding = encoding;
	}

	/// <summary>
	/// Attempts to read a WXXX frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static UserUrlFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		_ = version; // Currently unused

		// Minimum size: 1 (encoding) + 1 (description terminator)
		if (data.Length < 2)
			return UserUrlFrameReadResult.Failure ("WXXX frame data is too short");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return UserUrlFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;
		var contentData = data.Slice (1);

		var (description, url) = ExtractDescriptionAndUrl (contentData, encoding);

		var frame = new UserUrlFrame (url, description, encoding);
		return UserUrlFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content.</returns>
	public BinaryData RenderContent ()
	{
		var descriptionBytes = EncodeDescription (Description, Encoding);
		var urlBytes = Polyfills.Latin1.GetBytes (Url);
		var terminatorSize = GetTerminatorSize (Encoding);

		var totalSize = 1 + descriptionBytes.Length + terminatorSize + urlBytes.Length;

		using var builder = new BinaryDataBuilder (totalSize);

		builder.Add ((byte)Encoding);
		builder.Add (descriptionBytes);
		builder.AddZeros (terminatorSize);
		builder.Add (urlBytes);

		return builder.ToBinaryData ();
	}

	static (string description, string url) ExtractDescriptionAndUrl (
		ReadOnlySpan<byte> data,
		TextEncodingType encoding)
	{
		int terminatorIndex;
		int terminatorSize;

		if (encoding == TextEncodingType.Utf16WithBom || encoding == TextEncodingType.Utf16BE) {
			terminatorSize = 2;
			terminatorIndex = FindDoubleNullTerminator (data);
		} else {
			terminatorSize = 1;
			terminatorIndex = data.IndexOf ((byte)0);
		}

		if (terminatorIndex < 0) {
			// No terminator found - treat entire content as URL
			return (string.Empty, Polyfills.Latin1.GetString (data));
		}

		var descriptionData = data.Slice (0, terminatorIndex);
		var urlData = data.Slice (terminatorIndex + terminatorSize);

		var description = DecodeText (descriptionData, encoding);
		var url = Polyfills.Latin1.GetString (urlData);

		return (description, url);
	}

	static int FindDoubleNullTerminator (ReadOnlySpan<byte> data)
	{
		for (var i = 0; i < data.Length - 1; i += 2) {
			if (data[i] == 0 && data[i + 1] == 0)
				return i;
		}
		return -1;
	}

	static int GetTerminatorSize (TextEncodingType encoding)
	{
		return (encoding == TextEncodingType.Utf16WithBom || encoding == TextEncodingType.Utf16BE) ? 2 : 1;
	}

	static string DecodeText (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (data.IsEmpty)
			return string.Empty;

		return encoding switch {
			TextEncodingType.Latin1 => Polyfills.Latin1.GetString (data),
			TextEncodingType.Utf8 => System.Text.Encoding.UTF8.GetString (data),
			TextEncodingType.Utf16WithBom => DecodeTextUtf16WithBom (data),
			TextEncodingType.Utf16BE => System.Text.Encoding.BigEndianUnicode.GetString (data),
			_ => string.Empty
		};
	}

	static string DecodeTextUtf16WithBom (ReadOnlySpan<byte> data)
	{
		if (data.Length < 2)
			return string.Empty;

		var isLittleEndian = data[0] == 0xFF && data[1] == 0xFE;
		var isBigEndian = data[0] == 0xFE && data[1] == 0xFF;

		if (!isLittleEndian && !isBigEndian)
			return string.Empty;

		data = data.Slice (2);

		var textEncoding = isLittleEndian
			? System.Text.Encoding.Unicode
			: System.Text.Encoding.BigEndianUnicode;

		return textEncoding.GetString (data);
	}

	static byte[] EncodeDescription (string text, TextEncodingType encoding)
	{
		return encoding switch {
			TextEncodingType.Latin1 => Polyfills.Latin1.GetBytes (text),
			TextEncodingType.Utf8 => System.Text.Encoding.UTF8.GetBytes (text),
			TextEncodingType.Utf16WithBom => EncodeUtf16WithBom (text),
			TextEncodingType.Utf16BE => System.Text.Encoding.BigEndianUnicode.GetBytes (text),
			_ => []
		};
	}

	static byte[] EncodeUtf16WithBom (string text)
	{
		var textBytes = System.Text.Encoding.Unicode.GetBytes (text);
		var result = new byte[2 + textBytes.Length];
		result[0] = 0xFF;
		result[1] = 0xFE;
		textBytes.CopyTo (result, 2);
		return result;
	}
}

/// <summary>
/// Represents the result of reading a WXXX frame.
/// </summary>
public readonly struct UserUrlFrameReadResult : IEquatable<UserUrlFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public UserUrlFrame? Frame { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => Frame is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed.
	/// </summary>
	public int BytesConsumed { get; }

	UserUrlFrameReadResult (UserUrlFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static UserUrlFrameReadResult Success (UserUrlFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static UserUrlFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (UserUrlFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is UserUrlFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (UserUrlFrameReadResult left, UserUrlFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (UserUrlFrameReadResult left, UserUrlFrameReadResult right) =>
		!left.Equals (right);
}
