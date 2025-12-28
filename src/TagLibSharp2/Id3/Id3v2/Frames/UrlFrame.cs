// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

#pragma warning disable CA1054 // URI parameters should not be strings
#pragma warning disable CA1056 // URI properties should not be strings

/// <summary>
/// Represents an ID3v2 URL link frame (W*** frames except WXXX).
/// </summary>
/// <remarks>
/// URL frames contain a single URL encoded in Latin-1 (ISO-8859-1).
/// Standard frame IDs:
/// <list type="bullet">
/// <item>WCOM: Commercial information</item>
/// <item>WCOP: Copyright/legal information</item>
/// <item>WOAF: Official audio file webpage</item>
/// <item>WOAR: Official artist/performer webpage</item>
/// <item>WOAS: Official audio source webpage</item>
/// <item>WORS: Official internet radio station homepage</item>
/// <item>WPAY: Payment</item>
/// <item>WPUB: Publishers official webpage</item>
/// </list>
/// </remarks>
public sealed class UrlFrame
{
	static readonly HashSet<string> KnownUrlFrameIds = new (StringComparer.Ordinal)
	{
		"WCOM", "WCOP", "WOAF", "WOAR", "WOAS", "WORS", "WPAY", "WPUB"
	};

	/// <summary>
	/// Gets the frame ID (e.g., "WOAR", "WPUB").
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// Gets or sets the URL.
	/// </summary>
	public string Url { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="UrlFrame"/> class.
	/// </summary>
	/// <param name="id">The frame ID (e.g., "WOAR").</param>
	/// <param name="url">The URL.</param>
	public UrlFrame (string id, string url)
	{
		Id = id;
		Url = url;
	}

	/// <summary>
	/// Determines if the given frame ID is a standard URL frame ID.
	/// </summary>
	/// <param name="frameId">The frame ID to check.</param>
	/// <returns>True if it's a known URL frame ID.</returns>
	public static bool IsUrlFrameId (string frameId)
	{
		return KnownUrlFrameIds.Contains (frameId);
	}

	/// <summary>
	/// Attempts to read a URL frame from the provided data.
	/// </summary>
	/// <param name="frameId">The frame ID.</param>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static UrlFrameReadResult Read (string frameId, ReadOnlySpan<byte> data, Id3v2Version version)
	{
		_ = version; // Currently unused

		if (data.IsEmpty)
			return UrlFrameReadResult.Failure ("URL frame data is empty");

		// Find null terminator if present (trim trailing nulls)
		var nullIndex = data.IndexOf ((byte)0);
		var urlData = nullIndex >= 0 ? data.Slice (0, nullIndex) : data;

		var url = Polyfills.Latin1.GetString (urlData);
		var frame = new UrlFrame (frameId, url);

		return UrlFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content (URL in Latin-1).</returns>
	public BinaryData RenderContent ()
	{
		return BinaryData.FromStringLatin1 (Url);
	}
}

/// <summary>
/// Represents the result of reading a URL frame.
/// </summary>
public readonly struct UrlFrameReadResult : IEquatable<UrlFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public UrlFrame? Frame { get; }

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

	UrlFrameReadResult (UrlFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static UrlFrameReadResult Success (UrlFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static UrlFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (UrlFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is UrlFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (UrlFrameReadResult left, UrlFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (UrlFrameReadResult left, UrlFrameReadResult right) =>
		!left.Equals (right);
}
