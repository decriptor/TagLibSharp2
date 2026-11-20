// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 COMM (Comment) frame.
/// </summary>
/// <remarks>
/// COMM frame format:
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding (0=Latin-1, 1=UTF-16 w/BOM, 2=UTF-16BE, 3=UTF-8)
/// 1       3     Language (ISO-639-2, e.g., "eng", "deu")
/// 4       n     Short content description (null-terminated)
/// 4+n     m     The actual comment text
/// </code>
/// </remarks>
public sealed class CommentFrame
{
	const string DefaultLanguage = "eng";
	const int LanguageSize = 3;

	/// <summary>
	/// Gets the frame ID (always "COMM").
	/// </summary>
	public static string FrameId => "COMM";

	/// <summary>
	/// Gets or sets the language code (ISO-639-2, 3 characters).
	/// </summary>
	/// <remarks>
	/// Common values: "eng" (English), "deu" (German), "fra" (French), "spa" (Spanish).
	/// Defaults to "eng" if not set.
	/// </remarks>
	public string Language { get; set; }

	/// <summary>
	/// Gets or sets the short content description.
	/// </summary>
	/// <remarks>
	/// Used to distinguish between multiple comments in the same language.
	/// Often left empty or set to "Comment" or a specific identifier like "iTunes".
	/// </remarks>
	public string Description { get; set; }

	/// <summary>
	/// Gets or sets the comment text.
	/// </summary>
	public string Text { get; set; }

	/// <summary>
	/// Gets or sets the text encoding used for this frame.
	/// </summary>
	public TextEncodingType Encoding { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentFrame"/> class.
	/// </summary>
	/// <param name="text">The comment text.</param>
	/// <param name="language">The language code (defaults to "eng").</param>
	/// <param name="description">The content description (defaults to empty).</param>
	/// <param name="encoding">The text encoding (defaults to UTF-8).</param>
	public CommentFrame (
		string text,
		string language = DefaultLanguage,
		string description = "",
		TextEncodingType encoding = TextEncodingType.Utf8)
	{
		Text = text;
		Language = NormalizeLanguage (language);
		Description = description;
		Encoding = encoding;
	}

	/// <summary>
	/// Attempts to read a COMM frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static CommentFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		// Minimum size: 1 (encoding) + 3 (language) + 1 (description terminator) = 5 bytes
		if (data.Length < 5)
			return CommentFrameReadResult.Failure ("COMM frame data is too short");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return CommentFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;

		// Extract language (3 bytes ASCII)
		var languageBytes = data.Slice (1, LanguageSize);
		var language = Polyfills.Latin1.GetString (languageBytes);

		// Find the description terminator and extract description and text
		var contentData = data.Slice (1 + LanguageSize);
		var (description, text) = ExtractDescriptionAndText (contentData, encoding);

		var frame = new CommentFrame (text, language, description, encoding);
		return CommentFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content including encoding byte, language, description, and text.</returns>
	public BinaryData RenderContent ()
	{
		var descriptionBytes = EncodeText (Description, Encoding);
		var textBytes = EncodeText (Text, Encoding);
		var terminatorSize = GetTerminatorSize (Encoding);

		var totalSize = 1 + LanguageSize + descriptionBytes.Length + terminatorSize + textBytes.Length;

		using var builder = new BinaryDataBuilder (totalSize);

		// Encoding byte
		builder.Add ((byte)Encoding);

		// Language (3 bytes, ASCII, padded with spaces)
		var langBytes = System.Text.Encoding.ASCII.GetBytes (NormalizeLanguage (Language));
		builder.Add (langBytes.AsSpan ().Slice (0, Math.Min (langBytes.Length, LanguageSize)));
		if (langBytes.Length < LanguageSize)
			builder.AddZeros (LanguageSize - langBytes.Length);

		// Description with null terminator
		builder.Add (descriptionBytes);
		builder.AddZeros (terminatorSize);

		// Comment text
		builder.Add (textBytes);

		return builder.ToBinaryData ();
	}

	static (string description, string text) ExtractDescriptionAndText (
		ReadOnlySpan<byte> data,
		TextEncodingType encoding)
	{
		int terminatorIndex;
		int terminatorSize;

		if (encoding == TextEncodingType.Utf16WithBom || encoding == TextEncodingType.Utf16BE) {
			// Double-null terminator for UTF-16
			terminatorSize = 2;
			terminatorIndex = FindDoubleNullTerminator (data);
		} else {
			// Single null terminator for Latin-1 and UTF-8
			terminatorSize = 1;
			terminatorIndex = data.IndexOf ((byte)0);
		}

		if (terminatorIndex < 0) {
			// No terminator found - treat entire content as text
			return (string.Empty, DecodeText (data, encoding));
		}

		var descriptionData = data.Slice (0, terminatorIndex);
		var textData = data.Slice (terminatorIndex + terminatorSize);

		return (DecodeText (descriptionData, encoding), DecodeText (textData, encoding));
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

	static string NormalizeLanguage (string language)
	{
		if (string.IsNullOrEmpty (language))
			return DefaultLanguage;

		// Ensure exactly 3 characters, lowercase (ISO 639-2 codes are lowercase)
#pragma warning disable CA1308 // ISO 639-2 language codes are lowercase by convention
		language = language.ToLowerInvariant ();
#pragma warning restore CA1308
		if (language.Length > LanguageSize)
			return language.Substring (0, LanguageSize);
		if (language.Length < LanguageSize)
			return language.PadRight (LanguageSize);
		return language;
	}

	static string DecodeText (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (data.IsEmpty)
			return string.Empty;

		// A comment is one string. Drop only a trailing terminator.
		data = Id3Text.TrimTrailingTerminator (data, encoding);

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

		// Check BOM to determine endianness
		var isLittleEndian = data[0] == 0xFF && data[1] == 0xFE;
		var isBigEndian = data[0] == 0xFE && data[1] == 0xFF;

		if (!isLittleEndian && !isBigEndian)
			return string.Empty;

		// Skip BOM
		data = data.Slice (2);

		var textEncoding = isLittleEndian
			? System.Text.Encoding.Unicode
			: System.Text.Encoding.BigEndianUnicode;

		return textEncoding.GetString (data);
	}

	static BinaryData EncodeText (string text, TextEncodingType encoding)
	{
		return encoding switch {
			TextEncodingType.Latin1 => BinaryData.FromStringLatin1 (text),
			TextEncodingType.Utf8 => BinaryData.FromStringUtf8 (text),
			TextEncodingType.Utf16WithBom => BinaryData.FromStringUtf16 (text, includeBom: true),
			TextEncodingType.Utf16BE => new BinaryData (System.Text.Encoding.BigEndianUnicode.GetBytes (text)),
			_ => BinaryData.Empty
		};
	}
}

/// <summary>
/// Represents the result of reading a COMM frame.
/// </summary>
public readonly struct CommentFrameReadResult : IEquatable<CommentFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public CommentFrame? Frame { get; }

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

	CommentFrameReadResult (CommentFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static CommentFrameReadResult Success (CommentFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static CommentFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (CommentFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is CommentFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (CommentFrameReadResult left, CommentFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (CommentFrameReadResult left, CommentFrameReadResult right) =>
		!left.Equals (right);
}
