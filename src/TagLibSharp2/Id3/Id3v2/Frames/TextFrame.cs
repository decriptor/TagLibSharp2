// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 text frame (T*** frames like TIT2, TPE1, TALB, etc.).
/// </summary>
/// <remarks>
/// Text frame format:
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding (0=Latin-1, 1=UTF-16 w/BOM, 2=UTF-16BE, 3=UTF-8)
/// 1       n     Text content (may be null-terminated)
/// </code>
/// </remarks>
public sealed class TextFrame
{
	/// <summary>
	/// Gets the frame ID (e.g., TIT2, TPE1, TALB).
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// Gets or sets the text content.
	/// </summary>
	public string Text { get; set; }

	/// <summary>
	/// Gets or sets the text encoding used for this frame.
	/// </summary>
	public TextEncodingType Encoding { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="TextFrame"/> class.
	/// </summary>
	/// <param name="id">The frame ID.</param>
	/// <param name="text">The text content.</param>
	/// <param name="encoding">The text encoding.</param>
	public TextFrame (string id, string text, TextEncodingType encoding = TextEncodingType.Utf8)
	{
		Id = id;
		Text = text;
		Encoding = encoding;
	}

	/// <summary>
	/// Attempts to read a text frame from the provided data.
	/// </summary>
	/// <param name="frameId">The frame ID.</param>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static TextFrameReadResult Read (string frameId, ReadOnlySpan<byte> data, Id3v2Version version)
	{
		if (data.Length < 1)
			return TextFrameReadResult.Failure ("Frame data is too short");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return TextFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;
		var textData = data.Slice (1);

		var text = DecodeText (textData, encoding);

		var frame = new TextFrame (frameId, text, encoding);
		return TextFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content including encoding byte.</returns>
	public BinaryData RenderContent ()
	{
		var textBytes = EncodeText (Text, Encoding);

		using var builder = new BinaryDataBuilder (1 + textBytes.Length);
		builder.Add ((byte)Encoding);
		builder.Add (textBytes);
		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Decodes text from binary data using the specified encoding.
	/// </summary>
	static string DecodeText (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (data.IsEmpty)
			return string.Empty;

		return encoding switch {
			TextEncodingType.Latin1 => DecodeTextLatin1 (data),
			TextEncodingType.Utf16WithBom => DecodeTextUtf16WithBom (data),
			TextEncodingType.Utf16BE => DecodeTextUtf16BE (data),
			TextEncodingType.Utf8 => DecodeTextUtf8 (data),
			_ => string.Empty
		};
	}

	static string DecodeTextLatin1 (ReadOnlySpan<byte> data)
	{
		// Strip trailing null bytes only (preserve internal nulls for multi-value support)
		while (data.Length > 0 && data[data.Length - 1] == 0)
			data = data.Slice (0, data.Length - 1);

		return Polyfills.Latin1.GetString (data);
	}

	static string DecodeTextUtf8 (ReadOnlySpan<byte> data)
	{
		// Strip trailing null bytes only (preserve internal nulls for multi-value support)
		while (data.Length > 0 && data[data.Length - 1] == 0)
			data = data.Slice (0, data.Length - 1);

		return System.Text.Encoding.UTF8.GetString (data);
	}

	static string DecodeTextUtf16WithBom (ReadOnlySpan<byte> data)
	{
		// Strip trailing double-null bytes only (preserve internal nulls for multi-value support)
		while (data.Length >= 2 && data[data.Length - 2] == 0 && data[data.Length - 1] == 0)
			data = data.Slice (0, data.Length - 2);

		// Each string may lead with its own BOM; none is part of the text.
		return Utf16Text.Decode (data);
	}

	static string DecodeTextUtf16BE (ReadOnlySpan<byte> data)
	{
		return DecodeTextUtf16 (data, isLittleEndian: false);
	}

	static string DecodeTextUtf16 (ReadOnlySpan<byte> data, bool isLittleEndian)
	{
		// Strip trailing double-null bytes only (preserve internal nulls for multi-value support)
		while (data.Length >= 2 && data[data.Length - 2] == 0 && data[data.Length - 1] == 0)
			data = data.Slice (0, data.Length - 2);

		var encoding = isLittleEndian
			? System.Text.Encoding.Unicode
			: System.Text.Encoding.BigEndianUnicode;

		return encoding.GetString (data);
	}

	/// <summary>
	/// Encodes text to binary data using the specified encoding.
	/// </summary>
	static BinaryData EncodeText (string text, TextEncodingType encoding)
	{
		return encoding switch {
			TextEncodingType.Latin1 => BinaryData.FromStringLatin1 (text),
			TextEncodingType.Utf8 => BinaryData.FromStringUtf8 (text),
			TextEncodingType.Utf16WithBom => Utf16Text.Encode (text),
			TextEncodingType.Utf16BE => EncodeTextUtf16BE (text),
			_ => BinaryData.Empty
		};
	}

	static BinaryData EncodeTextUtf16BE (string text)
	{
		var bytes = System.Text.Encoding.BigEndianUnicode.GetBytes (text);
		return new BinaryData (bytes);
	}
}

/// <summary>
/// Represents the result of reading a text frame.
/// </summary>
public readonly struct TextFrameReadResult : IEquatable<TextFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public TextFrame? Frame { get; }

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

	TextFrameReadResult (TextFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static TextFrameReadResult Success (TextFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static TextFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (TextFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is TextFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (TextFrameReadResult left, TextFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (TextFrameReadResult left, TextFrameReadResult right) =>
		!left.Equals (right);
}
