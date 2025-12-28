// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 GEOB (General Encapsulated Object) frame.
/// </summary>
/// <remarks>
/// GEOB frame format:
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding
/// 1       n     MIME type (null-terminated Latin-1)
/// 1+n     m     Filename (null-terminated, encoding-dependent)
/// 1+n+m   p     Content description (null-terminated, encoding-dependent)
/// 1+n+m+p q     Encapsulated object data
/// </code>
/// Used to store arbitrary file attachments in an ID3v2 tag.
/// </remarks>
public sealed class GeneralObjectFrame
{
	/// <summary>
	/// Gets the frame ID (always "GEOB").
	/// </summary>
	public static string FrameId => "GEOB";

	/// <summary>
	/// Gets or sets the MIME type of the encapsulated object.
	/// </summary>
	public string MimeType { get; set; }

	/// <summary>
	/// Gets or sets the filename.
	/// </summary>
	public string FileName { get; set; }

	/// <summary>
	/// Gets or sets the content description.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Gets or sets the encapsulated object data.
	/// </summary>
	public BinaryData Data { get; set; }

	/// <summary>
	/// Gets or sets the text encoding for filename and description.
	/// </summary>
	public TextEncodingType Encoding { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="GeneralObjectFrame"/> class.
	/// </summary>
	/// <param name="mimeType">The MIME type.</param>
	/// <param name="fileName">The filename.</param>
	/// <param name="description">The content description.</param>
	/// <param name="data">The encapsulated object data.</param>
	/// <param name="encoding">The text encoding (defaults to UTF-8).</param>
	public GeneralObjectFrame (
		string mimeType,
		string fileName,
		string description,
		BinaryData data,
		TextEncodingType encoding = TextEncodingType.Utf8)
	{
		MimeType = mimeType;
		FileName = fileName;
		Description = description;
		Data = data;
		Encoding = encoding;
	}

	/// <summary>
	/// Attempts to read a GEOB frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static GeneralObjectFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		_ = version; // Currently unused

		if (data.Length < 4)
			return GeneralObjectFrameReadResult.Failure ("GEOB frame data is too short");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return GeneralObjectFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;
		var remaining = data.Slice (1);

		// MIME type (always Latin-1, null-terminated)
		var mimeNullIndex = remaining.IndexOf ((byte)0);
		if (mimeNullIndex < 0)
			return GeneralObjectFrameReadResult.Failure ("GEOB frame missing MIME type terminator");

		var mimeType = Polyfills.Latin1.GetString (remaining.Slice (0, mimeNullIndex));
		remaining = remaining.Slice (mimeNullIndex + 1);

		// Filename (encoding-dependent, null-terminated)
		var (fileName, fileNameLength) = ReadNullTerminatedString (remaining, encoding);
		remaining = remaining.Slice (fileNameLength);

		// Description (encoding-dependent, null-terminated)
		var (description, descriptionLength) = ReadNullTerminatedString (remaining, encoding);
		remaining = remaining.Slice (descriptionLength);

		// Object data is the rest
		var objectData = new BinaryData (remaining.ToArray ());

		var frame = new GeneralObjectFrame (mimeType, fileName, description, objectData, encoding);
		return GeneralObjectFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content.</returns>
	public BinaryData RenderContent ()
	{
		var mimeTypeBytes = Polyfills.Latin1.GetBytes (MimeType);
		var fileNameBytes = EncodeString (FileName, Encoding);
		var descriptionBytes = EncodeString (Description, Encoding);
		var terminatorSize = GetTerminatorSize (Encoding);

		var totalSize = 1 + mimeTypeBytes.Length + 1 +
						fileNameBytes.Length + terminatorSize +
						descriptionBytes.Length + terminatorSize +
						Data.Length;

		using var builder = new BinaryDataBuilder (totalSize);

		// Encoding
		builder.Add ((byte)Encoding);

		// MIME type (null-terminated)
		builder.Add (mimeTypeBytes);
		builder.Add ((byte)0x00);

		// Filename (null-terminated)
		builder.Add (fileNameBytes);
		builder.AddZeros (terminatorSize);

		// Description (null-terminated)
		builder.Add (descriptionBytes);
		builder.AddZeros (terminatorSize);

		// Object data
		builder.Add (Data);

		return builder.ToBinaryData ();
	}

	static (string text, int bytesConsumed) ReadNullTerminatedString (ReadOnlySpan<byte> data, TextEncodingType encoding)
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

		if (terminatorIndex < 0)
			return (string.Empty, 0);

		var textData = data.Slice (0, terminatorIndex);
		var text = DecodeText (textData, encoding);

		return (text, terminatorIndex + terminatorSize);
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
			return System.Text.Encoding.Unicode.GetString (data);

		data = data.Slice (2);

		var textEncoding = isLittleEndian
			? System.Text.Encoding.Unicode
			: System.Text.Encoding.BigEndianUnicode;

		return textEncoding.GetString (data);
	}

	static byte[] EncodeString (string text, TextEncodingType encoding)
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
/// Represents the result of reading a GEOB frame.
/// </summary>
public readonly struct GeneralObjectFrameReadResult : IEquatable<GeneralObjectFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public GeneralObjectFrame? Frame { get; }

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

	GeneralObjectFrameReadResult (GeneralObjectFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static GeneralObjectFrameReadResult Success (GeneralObjectFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static GeneralObjectFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (GeneralObjectFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is GeneralObjectFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (GeneralObjectFrameReadResult left, GeneralObjectFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (GeneralObjectFrameReadResult left, GeneralObjectFrameReadResult right) =>
		!left.Equals (right);
}
