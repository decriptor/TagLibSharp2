// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace TagLibSharp2.Core;

/// <summary>
/// Represents the result of a tag parsing operation.
/// Uses result type pattern to avoid exceptions for validation errors.
/// </summary>
/// <typeparam name="T">The tag type being parsed.</typeparam>
[SuppressMessage ("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory pattern for result type")]
public readonly struct TagReadResult<T> : IEquatable<TagReadResult<T>> where T : Tag
{
	/// <summary>
	/// Gets the parsed tag, or null if parsing failed or no tag was found.
	/// </summary>
	public T? Tag { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded with a valid tag.
	/// </summary>
	public bool IsSuccess => Tag is not null && Error is null;

	/// <summary>
	/// Gets a value indicating whether no tag was found (not an error).
	/// </summary>
	public bool IsNotFound => Tag is null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful or not found.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed during parsing.
	/// </summary>
	public int BytesConsumed { get; }

	TagReadResult (T? tag, string? error, int bytesConsumed)
	{
		Tag = tag;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result with the parsed tag.
	/// </summary>
	/// <param name="tag">The parsed tag.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static TagReadResult<T> Success (T tag, int bytesConsumed) =>
		new (tag, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result with an error message.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static TagReadResult<T> Failure (string error) =>
		new (null, error, 0);

	/// <summary>
	/// Creates a not-found result (no tag present, but not an error).
	/// </summary>
	/// <returns>A not-found result.</returns>
	public static TagReadResult<T> NotFound () =>
		new (null, null, 0);

	/// <inheritdoc/>
	public bool Equals (TagReadResult<T> other) =>
		EqualityComparer<T?>.Default.Equals (Tag, other.Tag) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is TagReadResult<T> other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Tag, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (TagReadResult<T> left, TagReadResult<T> right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (TagReadResult<T> left, TagReadResult<T> right) =>
		!left.Equals (right);
}
