// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 POPM (Popularimeter) frame for storing ratings and play counts.
/// </summary>
/// <remarks>
/// POPM frame format:
/// <code>
/// Offset  Size  Field
/// 0       n     Email to user (null-terminated Latin-1 string)
/// n       1     Rating (0=unknown, 1=worst, 255=best)
/// n+1     m     Counter (big-endian integer, variable length, optional)
/// </code>
/// Rating interpretations:
/// <list type="bullet">
/// <item>0: Unknown/unrated</item>
/// <item>1-31: 1 star</item>
/// <item>32-95: 2 stars</item>
/// <item>96-159: 3 stars</item>
/// <item>160-223: 4 stars</item>
/// <item>224-255: 5 stars</item>
/// </list>
/// </remarks>
public sealed class PopularimeterFrame
{
	/// <summary>
	/// Gets the frame ID (always "POPM").
	/// </summary>
	public static string FrameId => "POPM";

	/// <summary>
	/// Gets or sets the email address identifying the user or application that created this rating.
	/// </summary>
	/// <remarks>
	/// Common values include:
	/// <list type="bullet">
	/// <item>"Windows Media Player 9 Series" (WMP)</item>
	/// <item>"no@email" (generic)</item>
	/// <item>"rating@winamp.com" (Winamp)</item>
	/// </list>
	/// </remarks>
	public string Email { get; set; }

	/// <summary>
	/// Gets or sets the rating value (0-255).
	/// </summary>
	/// <remarks>
	/// 0 means unknown/unrated. 1 is worst, 255 is best.
	/// Use <see cref="RatingToStars"/> and <see cref="StarsToRating"/> for 5-star conversions.
	/// </remarks>
	public byte Rating { get; set; }

	/// <summary>
	/// Gets or sets the play count.
	/// </summary>
	public ulong PlayCount { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PopularimeterFrame"/> class.
	/// </summary>
	/// <param name="email">The email address identifying the rater.</param>
	/// <param name="rating">The rating value (0-255).</param>
	/// <param name="playCount">The play count.</param>
	public PopularimeterFrame (string email, byte rating = 0, ulong playCount = 0)
	{
		Email = email;
		Rating = rating;
		PlayCount = playCount;
	}

	/// <summary>
	/// Attempts to read a POPM frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static PopularimeterFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		_ = version; // Currently unused but kept for consistency with other frames

		if (data.IsEmpty)
			return PopularimeterFrameReadResult.Failure ("POPM frame data is empty");

		// Find the null terminator for email
		int emailEndIndex = data.IndexOf ((byte)0);
		if (emailEndIndex < 0)
			return PopularimeterFrameReadResult.Failure ("POPM frame missing email terminator");

		// Need at least 1 byte for rating after email
		if (emailEndIndex + 1 >= data.Length)
			return PopularimeterFrameReadResult.Failure ("POPM frame missing rating byte");

		var email = Polyfills.Latin1.GetString (data.Slice (0, emailEndIndex));
		var rating = data[emailEndIndex + 1];

		// Play counter is optional, variable-length big-endian integer
		ulong playCount = 0;
		var counterData = data.Slice (emailEndIndex + 2);
		if (!counterData.IsEmpty) {
			playCount = ReadBigEndianUlong (counterData);
		}

		var frame = new PopularimeterFrame (email, rating, playCount);
		return PopularimeterFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content.</returns>
	public BinaryData RenderContent ()
	{
		var emailBytes = Polyfills.Latin1.GetBytes (Email);
		var counterBytes = GetCounterBytes (PlayCount);

		var totalSize = emailBytes.Length + 1 + 1 + counterBytes.Length;

		using var builder = new BinaryDataBuilder (totalSize);

		// Email (null-terminated)
		builder.Add (emailBytes);
		builder.Add ((byte)0x00);

		// Rating
		builder.Add (Rating);

		// Counter (only if non-zero)
		if (counterBytes.Length > 0) {
			builder.Add (counterBytes);
		}

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Converts a 0-255 rating to a 0-5 star rating.
	/// </summary>
	/// <param name="rating">The raw rating value.</param>
	/// <returns>Star rating from 0 (unknown) to 5.</returns>
	public static int RatingToStars (byte rating)
	{
		return rating switch {
			0 => 0,              // Unknown
			< 32 => 1,           // 1 star
			< 96 => 2,           // 2 stars
			< 160 => 3,          // 3 stars
			< 224 => 4,          // 4 stars
			_ => 5               // 5 stars
		};
	}

	/// <summary>
	/// Converts a 0-5 star rating to a 0-255 rating value.
	/// </summary>
	/// <param name="stars">The star rating (0-5).</param>
	/// <returns>A rating byte value.</returns>
	public static byte StarsToRating (int stars)
	{
		return stars switch {
			0 => 0,
			1 => 1,
			2 => 64,
			3 => 128,
			4 => 196,
			5 => 255,
			_ => 0
		};
	}

	static ulong ReadBigEndianUlong (ReadOnlySpan<byte> data)
	{
		ulong result = 0;
		foreach (var b in data) {
			result = (result << 8) | b;
		}
		return result;
	}

	static byte[] GetCounterBytes (ulong value)
	{
		if (value == 0)
			return [];

		// Determine minimum bytes needed
		int byteCount;
		if (value <= 0xFF) byteCount = 1;
		else if (value <= 0xFFFF) byteCount = 2;
		else if (value <= 0xFFFFFF) byteCount = 3;
		else if (value <= 0xFFFFFFFF) byteCount = 4;
		else if (value <= 0xFFFFFFFFFF) byteCount = 5;
		else if (value <= 0xFFFFFFFFFFFF) byteCount = 6;
		else if (value <= 0xFFFFFFFFFFFFFF) byteCount = 7;
		else byteCount = 8;

		var result = new byte[byteCount];
		for (int i = byteCount - 1; i >= 0; i--) {
			result[byteCount - 1 - i] = (byte)((value >> (i * 8)) & 0xFF);
		}

		return result;
	}
}

/// <summary>
/// Represents the result of reading a POPM frame.
/// </summary>
public readonly struct PopularimeterFrameReadResult : IEquatable<PopularimeterFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public PopularimeterFrame? Frame { get; }

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

	PopularimeterFrameReadResult (PopularimeterFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static PopularimeterFrameReadResult Success (PopularimeterFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static PopularimeterFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (PopularimeterFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is PopularimeterFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (PopularimeterFrameReadResult left, PopularimeterFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (PopularimeterFrameReadResult left, PopularimeterFrameReadResult right) =>
		!left.Equals (right);
}
