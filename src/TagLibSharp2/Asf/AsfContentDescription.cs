// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents the ASF Content Description Object containing 5 fixed metadata fields.
/// </summary>
/// <remarks>
/// Reference: ASF Specification Section 3.10.
/// Contains: Title, Author, Copyright, Description, Rating.
/// All strings are UTF-16 LE with null terminator.
/// </remarks>
public sealed class AsfContentDescription
{
	/// <summary>
	/// Gets the title field.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Gets the author field.
	/// </summary>
	public string Author { get; }

	/// <summary>
	/// Gets the copyright field.
	/// </summary>
	public string Copyright { get; }

	/// <summary>
	/// Gets the description field.
	/// </summary>
	public string Description { get; }

	/// <summary>
	/// Gets the rating field.
	/// </summary>
	public string Rating { get; }

	/// <summary>
	/// Creates a new Content Description with the specified fields.
	/// </summary>
	public AsfContentDescription (
		string title,
		string author,
		string copyright,
		string description,
		string rating)
	{
		Title = title;
		Author = author;
		Copyright = copyright;
		Description = description;
		Rating = rating;
	}

	/// <summary>
	/// Parses a Content Description Object from binary data.
	/// </summary>
	/// <param name="data">The object content (after GUID and size).</param>
	public static AsfContentDescriptionParseResult Parse (ReadOnlySpan<byte> data)
	{
		// Need at least 10 bytes for 5 length fields
		if (data.Length < 10)
			return AsfContentDescriptionParseResult.Failure ("Insufficient data for Content Description lengths");

		// Read the 5 length fields
		var titleLength = BinaryPrimitives.ReadUInt16LittleEndian (data);
		var authorLength = BinaryPrimitives.ReadUInt16LittleEndian (data[2..]);
		var copyrightLength = BinaryPrimitives.ReadUInt16LittleEndian (data[4..]);
		var descriptionLength = BinaryPrimitives.ReadUInt16LittleEndian (data[6..]);
		var ratingLength = BinaryPrimitives.ReadUInt16LittleEndian (data[8..]);

		var totalDataLength = titleLength + authorLength + copyrightLength + descriptionLength + ratingLength;
		if (data.Length < 10 + totalDataLength)
			return AsfContentDescriptionParseResult.Failure ("Content Description data truncated");

		var offset = 10;

		var title = ReadUtf16String (data.Slice (offset, titleLength));
		offset += titleLength;

		var author = ReadUtf16String (data.Slice (offset, authorLength));
		offset += authorLength;

		var copyright = ReadUtf16String (data.Slice (offset, copyrightLength));
		offset += copyrightLength;

		var description = ReadUtf16String (data.Slice (offset, descriptionLength));
		offset += descriptionLength;

		var rating = ReadUtf16String (data.Slice (offset, ratingLength));
		offset += ratingLength;

		var result = new AsfContentDescription (title, author, copyright, description, rating);
		return AsfContentDescriptionParseResult.Success (result, offset);
	}

	/// <summary>
	/// Renders the Content Description to binary data.
	/// </summary>
	public BinaryData Render ()
	{
		var titleBytes = CreateUtf16String (Title);
		var authorBytes = CreateUtf16String (Author);
		var copyrightBytes = CreateUtf16String (Copyright);
		var descriptionBytes = CreateUtf16String (Description);
		var ratingBytes = CreateUtf16String (Rating);

		var totalSize = 10 + titleBytes.Length + authorBytes.Length +
			copyrightBytes.Length + descriptionBytes.Length + ratingBytes.Length;
		var result = new byte[totalSize];
		var offset = 0;

		// Write lengths
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)titleBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)authorBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)copyrightBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)descriptionBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)ratingBytes.Length);
		offset += 2;

		// Write strings
		Array.Copy (titleBytes, 0, result, offset, titleBytes.Length);
		offset += titleBytes.Length;
		Array.Copy (authorBytes, 0, result, offset, authorBytes.Length);
		offset += authorBytes.Length;
		Array.Copy (copyrightBytes, 0, result, offset, copyrightBytes.Length);
		offset += copyrightBytes.Length;
		Array.Copy (descriptionBytes, 0, result, offset, descriptionBytes.Length);
		offset += descriptionBytes.Length;
		Array.Copy (ratingBytes, 0, result, offset, ratingBytes.Length);

		return new BinaryData (result);
	}

	static string ReadUtf16String (ReadOnlySpan<byte> data)
	{
		if (data.Length == 0)
			return string.Empty;

		// Remove null terminator if present
		var length = data.Length;
		if (length >= 2 && data[length - 1] == 0 && data[length - 2] == 0)
			length -= 2;

		if (length == 0)
			return string.Empty;

		return Encoding.Unicode.GetString (data[..length]);
	}

	static byte[] CreateUtf16String (string value)
	{
		var bytes = Encoding.Unicode.GetBytes (value);
		var result = new byte[bytes.Length + 2]; // +2 for null terminator
		Array.Copy (bytes, result, bytes.Length);
		return result;
	}
}

/// <summary>
/// Result of parsing a Content Description Object.
/// </summary>
public readonly struct AsfContentDescriptionParseResult : IEquatable<AsfContentDescriptionParseResult>
{
	/// <summary>
	/// Gets the parsed Content Description.
	/// </summary>
	public AsfContentDescription Value { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed during parsing.
	/// </summary>
	public int BytesConsumed { get; }

	/// <summary>
	/// Gets whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Error is null;

	AsfContentDescriptionParseResult (AsfContentDescription value, string? error, int bytesConsumed)
	{
		Value = value;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	public static AsfContentDescriptionParseResult Success (AsfContentDescription value, int bytesConsumed)
		=> new (value, null, bytesConsumed);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	public static AsfContentDescriptionParseResult Failure (string error)
		=> new (null!, error, 0);

	/// <inheritdoc/>
	public bool Equals (AsfContentDescriptionParseResult other)
		=> BytesConsumed == other.BytesConsumed && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfContentDescriptionParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode ()
		=> HashCode.Combine (BytesConsumed, Error);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfContentDescriptionParseResult left, AsfContentDescriptionParseResult right)
		=> left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfContentDescriptionParseResult left, AsfContentDescriptionParseResult right)
		=> !left.Equals (right);
}
