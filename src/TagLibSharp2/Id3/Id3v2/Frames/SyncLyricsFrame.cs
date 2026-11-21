// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

#pragma warning disable CA1008 // Enums should have zero value - ID3v2 spec doesn't define 0
#pragma warning disable CA1028 // Enum storage should be Int32 - ID3v2 spec uses bytes

/// <summary>
/// Specifies the format of timestamps in synchronized lyrics.
/// </summary>
public enum TimestampFormat : byte
{
	/// <summary>
	/// Timestamps are MPEG frame numbers.
	/// </summary>
	MpegFrames = 1,

	/// <summary>
	/// Timestamps are in milliseconds.
	/// </summary>
	Milliseconds = 2
}

/// <summary>
/// Specifies the type of synchronized text content.
/// </summary>
public enum SyncLyricsType : byte
{
	/// <summary>
	/// Other type of content.
	/// </summary>
	Other = 0,

	/// <summary>
	/// Song lyrics.
	/// </summary>
	Lyrics = 1,

	/// <summary>
	/// Text transcription.
	/// </summary>
	TextTranscription = 2,

	/// <summary>
	/// Part names (e.g., "Verse", "Chorus").
	/// </summary>
	PartNames = 3,

	/// <summary>
	/// Event descriptions.
	/// </summary>
	Events = 4,

	/// <summary>
	/// Chord progressions.
	/// </summary>
	Chords = 5,

	/// <summary>
	/// Trivia or "pop-up" information.
	/// </summary>
	Trivia = 6,

	/// <summary>
	/// URLs for web pages.
	/// </summary>
	WebPageUrls = 7,

	/// <summary>
	/// URLs for images.
	/// </summary>
	ImageUrls = 8
}

/// <summary>
/// Represents a synchronized text item with its timestamp.
/// </summary>
public readonly struct SyncLyricsItem : IEquatable<SyncLyricsItem>
{
	/// <summary>
	/// Gets the text content.
	/// </summary>
	public string Text { get; }

	/// <summary>
	/// Gets the timestamp (interpretation depends on TimestampFormat).
	/// </summary>
	public uint Timestamp { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="SyncLyricsItem"/> struct.
	/// </summary>
	/// <param name="text">The text content.</param>
	/// <param name="timestamp">The timestamp.</param>
	public SyncLyricsItem (string text, uint timestamp)
	{
		Text = text;
		Timestamp = timestamp;
	}

	/// <inheritdoc/>
	public bool Equals (SyncLyricsItem other) =>
		Text == other.Text && Timestamp == other.Timestamp;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is SyncLyricsItem other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Text, Timestamp);

	/// <summary>
	/// Determines whether two items are equal.
	/// </summary>
	public static bool operator == (SyncLyricsItem left, SyncLyricsItem right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two items are not equal.
	/// </summary>
	public static bool operator != (SyncLyricsItem left, SyncLyricsItem right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents an ID3v2 SYLT (Synchronized Lyrics/Text) frame.
/// </summary>
/// <remarks>
/// SYLT frame format:
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding
/// 1       3     Language (ISO-639-2)
/// 4       1     Timestamp format
/// 5       1     Content type
/// 6       n     Content descriptor (null-terminated)
/// 6+n     m     Synchronized text (text + null + timestamp, repeated)
/// </code>
/// Each sync item consists of: text + null terminator + 4-byte big-endian timestamp.
/// </remarks>
public sealed class SyncLyricsFrame
{
	const string DefaultLanguage = "eng";
	const int LanguageSize = 3;

	readonly List<SyncLyricsItem> _syncItems = new ();

	/// <summary>
	/// Gets the frame ID (always "SYLT").
	/// </summary>
	public static string FrameId => "SYLT";

	/// <summary>
	/// Gets or sets the language code (ISO-639-2).
	/// </summary>
	public string Language { get; set; }

	/// <summary>
	/// Gets or sets the timestamp format.
	/// </summary>
	public TimestampFormat TimestampFormat { get; set; }

	/// <summary>
	/// Gets or sets the content type.
	/// </summary>
	public SyncLyricsType ContentType { get; set; }

	/// <summary>
	/// Gets or sets the content descriptor.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Gets or sets the text encoding.
	/// </summary>
	public TextEncodingType Encoding { get; set; }

	/// <summary>
	/// Gets the synchronized items.
	/// </summary>
	public IReadOnlyList<SyncLyricsItem> SyncItems => _syncItems;

	/// <summary>
	/// Initializes a new instance of the <see cref="SyncLyricsFrame"/> class.
	/// </summary>
	public SyncLyricsFrame ()
	{
		Language = DefaultLanguage;
		TimestampFormat = TimestampFormat.Milliseconds;
		ContentType = SyncLyricsType.Lyrics;
		Description = "";
		Encoding = TextEncodingType.Utf8;
	}

	/// <summary>
	/// Adds a synchronized text item.
	/// </summary>
	/// <param name="text">The text content.</param>
	/// <param name="timestamp">The timestamp.</param>
	public void AddSyncItem (string text, uint timestamp)
	{
		_syncItems.Add (new SyncLyricsItem (text, timestamp));
	}

	/// <summary>
	/// Clears all synchronized items.
	/// </summary>
	public void ClearSyncItems ()
	{
		_syncItems.Clear ();
	}

	/// <summary>
	/// Attempts to read a SYLT frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static SyncLyricsFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		_ = version; // Currently unused

		// Minimum size: encoding(1) + lang(3) + timestamp format(1) + content type(1) = 6 bytes
		if (data.Length < 6)
			return SyncLyricsFrameReadResult.Failure ("SYLT frame data is too short");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return SyncLyricsFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;
		var language = Polyfills.Latin1.GetString (data.Slice (1, LanguageSize));
		var timestampFormat = (TimestampFormat)data[4];
		var contentType = (SyncLyricsType)data[5];

		var remaining = data.Slice (6);

		// Read content descriptor
		var (description, descBytes) = ReadNullTerminatedString (remaining, encoding);
		remaining = remaining.Slice (descBytes);

		var frame = new SyncLyricsFrame {
			Encoding = encoding,
			Language = language,
			TimestampFormat = timestampFormat,
			ContentType = contentType,
			Description = description
		};

		// A NUL ends that line only. Later timestamps are still read.
		while (remaining.Length > 4) {
			var (text, textBytes) = ReadNullTerminatedString (remaining, encoding);
			remaining = remaining.Slice (textBytes);

			if (remaining.Length < 4)
				break;

			// Read timestamp (4 bytes big-endian)
			var timestamp = ((uint)remaining[0] << 24) |
							((uint)remaining[1] << 16) |
							((uint)remaining[2] << 8) |
							(uint)remaining[3];
			remaining = remaining.Slice (4);

			frame.AddSyncItem (text, timestamp);
		}

		return SyncLyricsFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content.</returns>
	public BinaryData RenderContent ()
	{
		using var builder = new BinaryDataBuilder ();

		// Encoding
		builder.Add ((byte)Encoding);

		// Language (3 bytes)
		var normalizedLang = NormalizeLanguage (Language);
		var langBytes = System.Text.Encoding.ASCII.GetBytes (normalizedLang);
		builder.Add (langBytes.AsSpan ().Slice (0, Math.Min (langBytes.Length, LanguageSize)));
		if (langBytes.Length < LanguageSize)
			builder.AddZeros (LanguageSize - langBytes.Length);

		// Timestamp format
		builder.Add ((byte)TimestampFormat);

		// Content type
		builder.Add ((byte)ContentType);

		// Description (null-terminated)
		var descBytes = EncodeString (Description, Encoding);
		var terminatorSize = GetTerminatorSize (Encoding);
		builder.Add (descBytes);
		builder.AddZeros (terminatorSize);

		// Synchronized items
		foreach (var item in _syncItems) {
			var textBytes = EncodeString (item.Text, Encoding);
			builder.Add (textBytes);
			builder.AddZeros (terminatorSize);

			// Timestamp (4 bytes big-endian)
			builder.Add ((byte)((item.Timestamp >> 24) & 0xFF));
			builder.Add ((byte)((item.Timestamp >> 16) & 0xFF));
			builder.Add ((byte)((item.Timestamp >> 8) & 0xFF));
			builder.Add ((byte)(item.Timestamp & 0xFF));
		}

		return builder.ToBinaryData ();
	}

	static string NormalizeLanguage (string language)
	{
		if (string.IsNullOrEmpty (language))
			return DefaultLanguage;

#pragma warning disable CA1308 // ISO 639-2 language codes are lowercase by convention
		language = language.ToLowerInvariant ();
#pragma warning restore CA1308
		if (language.Length > LanguageSize)
			return language.Substring (0, LanguageSize);
		if (language.Length < LanguageSize)
			return language.PadRight (LanguageSize);
		return language;
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

		// ReadNullTerminatedString already ended this line at the first NUL.
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
/// Represents the result of reading a SYLT frame.
/// </summary>
public readonly struct SyncLyricsFrameReadResult : IEquatable<SyncLyricsFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public SyncLyricsFrame? Frame { get; }

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

	SyncLyricsFrameReadResult (SyncLyricsFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static SyncLyricsFrameReadResult Success (SyncLyricsFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static SyncLyricsFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (SyncLyricsFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is SyncLyricsFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (SyncLyricsFrameReadResult left, SyncLyricsFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (SyncLyricsFrameReadResult left, SyncLyricsFrameReadResult right) =>
		!left.Equals (right);
}
