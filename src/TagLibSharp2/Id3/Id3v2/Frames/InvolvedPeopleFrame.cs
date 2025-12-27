// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Specifies the type of involved people frame.
/// </summary>
public enum InvolvedPeopleFrameType
{
	/// <summary>
	/// TIPL (Involved People List) - production credits like producer, engineer.
	/// </summary>
	InvolvedPeople,

	/// <summary>
	/// TMCL (Musician Credits List) - musician credits like guitar, drums.
	/// </summary>
	MusicianCredits
}

/// <summary>
/// Represents an ID3v2 involved people frame (TIPL or TMCL).
/// </summary>
/// <remarks>
/// <para>
/// TIPL (Involved People List) and TMCL (Musician Credits List) store pairs
/// of roles/instruments and the people who filled them.
/// </para>
/// <para>
/// Frame format (ID3v2.4):
/// </para>
/// <code>
/// Offset  Size  Field
/// 0       1     Text encoding (0=Latin-1, 1=UTF-16 w/BOM, 2=UTF-16BE, 3=UTF-8)
/// 1       n     Null-separated pairs: role\0person\0role\0person...
/// </code>
/// </remarks>
public sealed class InvolvedPeopleFrame
{
	readonly Dictionary<string, string> _pairs = new (StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Gets the frame ID (TIPL or TMCL).
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// Gets or sets the text encoding used for this frame.
	/// </summary>
	public TextEncodingType Encoding { get; set; } = TextEncodingType.Utf8;

	/// <summary>
	/// Gets the number of role/person pairs in this frame.
	/// </summary>
	public int Count => _pairs.Count;

	/// <summary>
	/// Initializes a new instance of the <see cref="InvolvedPeopleFrame"/> class.
	/// </summary>
	/// <param name="frameType">The type of frame (TIPL or TMCL).</param>
	public InvolvedPeopleFrame (InvolvedPeopleFrameType frameType)
	{
		Id = frameType == InvolvedPeopleFrameType.MusicianCredits ? "TMCL" : "TIPL";
	}

	InvolvedPeopleFrame (string frameId)
	{
		Id = frameId;
	}

	/// <summary>
	/// Adds or updates a role/person pair.
	/// </summary>
	/// <param name="role">The role or instrument.</param>
	/// <param name="person">The person's name.</param>
	public void Add (string role, string person)
	{
		_pairs[role] = person;
	}

	/// <summary>
	/// Gets the person for a given role.
	/// </summary>
	/// <param name="role">The role or instrument (case-insensitive).</param>
	/// <returns>The person's name, or null if not found.</returns>
	public string? GetPerson (string role)
	{
		return _pairs.TryGetValue (role, out var person) ? person : null;
	}

	/// <summary>
	/// Gets all roles in this frame.
	/// </summary>
	/// <returns>A list of all roles.</returns>
	public IReadOnlyList<string> GetRoles ()
	{
		return _pairs.Keys.ToList ();
	}

	/// <summary>
	/// Gets all people in this frame.
	/// </summary>
	/// <returns>A list of all people.</returns>
	public IReadOnlyList<string> GetPeople ()
	{
		return _pairs.Values.ToList ();
	}

	/// <summary>
	/// Removes a role/person pair.
	/// </summary>
	/// <param name="role">The role to remove (case-insensitive).</param>
	/// <returns>True if the pair was removed, false if not found.</returns>
	public bool Remove (string role)
	{
		return _pairs.Remove (role);
	}

	/// <summary>
	/// Clears all role/person pairs from this frame.
	/// </summary>
	public void Clear ()
	{
		_pairs.Clear ();
	}

	/// <summary>
	/// Attempts to read an involved people frame from the provided data.
	/// </summary>
	/// <param name="frameId">The frame ID (TIPL, TMCL, or IPLS).</param>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static InvolvedPeopleFrameReadResult Read (string frameId, ReadOnlySpan<byte> data, Id3v2Version version)
	{
		if (data.IsEmpty)
			return InvolvedPeopleFrameReadResult.Failure ("Frame data is empty");

		var encodingByte = data[0];
		if (encodingByte > 3)
			return InvolvedPeopleFrameReadResult.Failure ($"Invalid text encoding: {encodingByte}");

		var encoding = (TextEncodingType)encodingByte;
		var frame = new InvolvedPeopleFrame (frameId) { Encoding = encoding };

		// If only encoding byte, return empty frame
		if (data.Length == 1)
			return InvolvedPeopleFrameReadResult.Success (frame, data.Length);

		// Parse the null-separated strings
		var contentData = data.Slice (1);
		var strings = ParseNullSeparatedStrings (contentData, encoding);

		// Strings come in pairs: role, person, role, person...
		for (var i = 0; i + 1 < strings.Count; i += 2) {
			var role = strings[i];
			var person = strings[i + 1];
			if (!string.IsNullOrEmpty (role))
				frame._pairs[role] = person;
		}

		return InvolvedPeopleFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content including encoding byte.</returns>
	public BinaryData RenderContent ()
	{
		if (_pairs.Count == 0) {
			using var emptyBuilder = new BinaryDataBuilder (1);
			emptyBuilder.Add ((byte)Encoding);
			return emptyBuilder.ToBinaryData ();
		}

		// Calculate size: encoding byte + all strings with null terminators
		var strings = new List<string> ();
		foreach (var pair in _pairs) {
			strings.Add (pair.Key);
			strings.Add (pair.Value);
		}

		// Build the content
		// Per ID3v2 spec, each text string is null-terminated
		using var builder = new BinaryDataBuilder (1 + EstimateSize (strings));
		builder.Add ((byte)Encoding);

		for (var i = 0; i < strings.Count; i++) {
			var bytes = EncodeText (strings[i], Encoding);
			builder.Add (bytes);
			// Add null terminator after each string (spec says each is null-terminated)
			builder.Add (GetTerminatorBytes (Encoding));
		}

		return builder.ToBinaryData ();
	}

	static int EstimateSize (List<string> strings)
	{
		var size = 0;
		foreach (var s in strings)
			size += s.Length * 4 + 2; // Worst case: UTF-8 4 bytes per char + terminator
		return size;
	}

	static List<string> ParseNullSeparatedStrings (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		var result = new List<string> ();
		var terminatorSize = GetTerminatorSize (encoding);
		var current = 0;

		while (current < data.Length) {
			var nullIndex = FindNullTerminator (data.Slice (current), encoding);
			if (nullIndex < 0) {
				// No null found, take the rest as the last string
				var remaining = data.Slice (current);
				if (!remaining.IsEmpty)
					result.Add (DecodeText (remaining, encoding));
				break;
			}

			var segment = data.Slice (current, nullIndex);
			result.Add (DecodeText (segment, encoding));
			current += nullIndex + terminatorSize;
		}

		return result;
	}

	static int FindNullTerminator (ReadOnlySpan<byte> data, TextEncodingType encoding)
	{
		if (encoding is TextEncodingType.Utf16WithBom or TextEncodingType.Utf16BE) {
			// For UTF-16, need to find double-null (0x00 0x00) on even boundary
			for (var i = 0; i < data.Length - 1; i += 2) {
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
		var enc = isLittleEndian
			? System.Text.Encoding.Unicode
			: System.Text.Encoding.BigEndianUnicode;

		return enc.GetString (data);
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
/// Represents the result of reading an involved people frame.
/// </summary>
public readonly struct InvolvedPeopleFrameReadResult : IEquatable<InvolvedPeopleFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public InvolvedPeopleFrame? Frame { get; }

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

	InvolvedPeopleFrameReadResult (InvolvedPeopleFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static InvolvedPeopleFrameReadResult Success (InvolvedPeopleFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static InvolvedPeopleFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (InvolvedPeopleFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is InvolvedPeopleFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (InvolvedPeopleFrameReadResult left, InvolvedPeopleFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (InvolvedPeopleFrameReadResult left, InvolvedPeopleFrameReadResult right) =>
		!left.Equals (right);
}
