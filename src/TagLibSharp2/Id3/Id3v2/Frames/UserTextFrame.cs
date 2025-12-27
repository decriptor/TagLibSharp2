// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 user-defined text frame (TXXX).
/// </summary>
/// <remarks>
/// <para>
/// TXXX frames allow storing custom metadata not covered by standard frames.
/// Common uses include ReplayGain values and MusicBrainz IDs.
/// </para>
/// <para>
/// Frame format:
/// </para>
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding (0=Latin-1, 1=UTF-16 w/BOM, 2=UTF-16BE, 3=UTF-8)
/// 1       n     Description (null-terminated string)
/// n+1     m     Value (string, not null-terminated)
/// </code>
/// </remarks>
public sealed class UserTextFrame
{
	/// <summary>
	/// The frame ID for user-defined text frames.
	/// </summary>
	public const string FrameId = "TXXX";

	/// <summary>
	/// Gets or sets the description identifying this user text frame.
	/// </summary>
	/// <remarks>
	/// Common descriptions include:
	/// - REPLAYGAIN_TRACK_GAIN, REPLAYGAIN_TRACK_PEAK
	/// - REPLAYGAIN_ALBUM_GAIN, REPLAYGAIN_ALBUM_PEAK
	/// - MUSICBRAINZ_TRACKID, MUSICBRAINZ_ALBUMID, MUSICBRAINZ_ARTISTID
	/// </remarks>
	public string Description { get; set; }

	/// <summary>
	/// Gets or sets the value of this user text frame.
	/// </summary>
	public string Value { get; set; }

	/// <summary>
	/// Gets or sets the text encoding used for this frame.
	/// </summary>
	public TextEncodingType Encoding { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="UserTextFrame"/> class.
	/// </summary>
	/// <param name="description">The description identifying this frame.</param>
	/// <param name="value">The value of this frame.</param>
	/// <param name="encoding">The text encoding (default: UTF-8).</param>
	public UserTextFrame (string description, string value, TextEncodingType encoding = TextEncodingType.Utf8)
	{
		Description = description ?? "";
		Value = value ?? "";
		Encoding = encoding;
	}

	/// <summary>
	/// Attempts to read a user text frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static UserTextFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		if (data.Length < 2) // Need at least encoding byte and null terminator
			return UserTextFrameReadResult.Failure ("Frame data is too short");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return UserTextFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;
		var contentData = data.Slice (1);

		// Find the null terminator between description and value
		var nullIndex = FindNullTerminator (contentData, encoding);
		if (nullIndex < 0)
			return UserTextFrameReadResult.Failure ("Missing null terminator between description and value");

		// Decode description (before null terminator)
		var descriptionData = contentData.Slice (0, nullIndex);
		var description = DecodeText (descriptionData, encoding);

		// Decode value (after null terminator)
		var terminatorSize = GetTerminatorSize (encoding);
		var valueStart = nullIndex + terminatorSize;
		var valueData = valueStart < contentData.Length
			? contentData.Slice (valueStart)
			: ReadOnlySpan<byte>.Empty;
		var value = DecodeText (valueData, encoding);

		var frame = new UserTextFrame (description, value, encoding);
		return UserTextFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content including encoding byte, description, null terminator, and value.</returns>
	public BinaryData RenderContent ()
	{
		var descriptionBytes = EncodeText (Description, Encoding);
		var terminatorBytes = GetTerminatorBytes (Encoding);
		var valueBytes = EncodeText (Value, Encoding);

		using var builder = new BinaryDataBuilder (1 + descriptionBytes.Length + terminatorBytes.Length + valueBytes.Length);
		builder.Add ((byte)Encoding);
		builder.Add (descriptionBytes);
		builder.Add (terminatorBytes);
		builder.Add (valueBytes);
		return builder.ToBinaryData ();
	}

	static int FindNullTerminator (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (encoding == TextEncodingType.Utf16WithBom || encoding == TextEncodingType.Utf16BE) {
			// For UTF-16, need to find double-null (0x00 0x00)
			// But need to account for BOM in UTF-16 with BOM
			var startOffset = 0;
			if (encoding == TextEncodingType.Utf16WithBom && data.Length >= 2) {
				// Skip BOM
				startOffset = 2;
			}

			for (var i = startOffset; i < data.Length - 1; i += 2) {
				if (data[i] == 0 && data[i + 1] == 0)
					return i;
			}
			return -1;
		} else {
			// For Latin1 and UTF-8, find single null byte
			return data.IndexOf ((byte)0);
		}
	}

	static int GetTerminatorSize (TextEncodingType encoding) =>
		encoding is TextEncodingType.Utf16WithBom or TextEncodingType.Utf16BE ? 2 : 1;

	static byte[] GetTerminatorBytes (TextEncodingType encoding) =>
		encoding is TextEncodingType.Utf16WithBom or TextEncodingType.Utf16BE
			? new byte[] { 0, 0 }
			: new byte[] { 0 };

	static string DecodeText (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (data.IsEmpty)
			return string.Empty;

		return encoding switch {
			TextEncodingType.Latin1 => Polyfills.Latin1.GetString (data),
			TextEncodingType.Utf8 => System.Text.Encoding.UTF8.GetString (data),
			TextEncodingType.Utf16WithBom => DecodeUtf16WithBom (data),
			TextEncodingType.Utf16BE => System.Text.Encoding.BigEndianUnicode.GetString (data),
			_ => string.Empty
		};
	}

	static string DecodeUtf16WithBom (ReadOnlySpan<byte> data)
	{
		if (data.Length < 2)
			return string.Empty;

		// Check BOM
		var isLittleEndian = data[0] == 0xFF && data[1] == 0xFE;
		var isBigEndian = data[0] == 0xFE && data[1] == 0xFF;

		if (!isLittleEndian && !isBigEndian)
			return System.Text.Encoding.Unicode.GetString (data); // Default to LE if no BOM

		// Skip BOM and decode
		data = data.Slice (2);
		var encoding = isLittleEndian
			? System.Text.Encoding.Unicode
			: System.Text.Encoding.BigEndianUnicode;

		return encoding.GetString (data);
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
/// Represents the result of reading a user text frame.
/// </summary>
public readonly struct UserTextFrameReadResult : IEquatable<UserTextFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public UserTextFrame? Frame { get; }

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

	UserTextFrameReadResult (UserTextFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static UserTextFrameReadResult Success (UserTextFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static UserTextFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (UserTextFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is UserTextFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (UserTextFrameReadResult left, UserTextFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (UserTextFrameReadResult left, UserTextFrameReadResult right) =>
		!left.Equals (right);
}
