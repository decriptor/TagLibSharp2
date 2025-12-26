// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents a single field in a Vorbis Comment block.
/// </summary>
/// <remarks>
/// <para>
/// Vorbis Comment fields follow the format "FIELDNAME=value" where:
/// </para>
/// <list type="bullet">
/// <item>Field names are case-insensitive ASCII (stored uppercase by convention)</item>
/// <item>Field names must not contain the '=' character</item>
/// <item>Values are UTF-8 encoded strings</item>
/// <item>Multiple fields with the same name are allowed (e.g., multiple ARTIST fields)</item>
/// </list>
/// <para>
/// Common field names: TITLE, ARTIST, ALBUM, DATE, TRACKNUMBER, GENRE, COMMENT, DESCRIPTION
/// </para>
/// <para>
/// Reference: https://xiph.org/vorbis/doc/v-comment.html
/// </para>
/// </remarks>
public readonly struct VorbisCommentField : IEquatable<VorbisCommentField>
{
	/// <summary>
	/// Gets the field name (uppercase ASCII).
	/// </summary>
	/// <remarks>
	/// Field names are case-insensitive per the Vorbis Comment spec, but are stored
	/// in uppercase by convention.
	/// </remarks>
	public string Name { get; }

	/// <summary>
	/// Gets the field value (UTF-8 string).
	/// </summary>
	public string Value { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="VorbisCommentField"/> struct.
	/// </summary>
	/// <param name="name">The field name. Will be converted to uppercase.</param>
	/// <param name="value">The field value.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown when <paramref name="name"/> or <paramref name="value"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown when <paramref name="name"/> is empty or contains '=' character.
	/// </exception>
	public VorbisCommentField (string name, string value)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
		if (value is null)
			throw new ArgumentNullException (nameof (value));
#else
		ArgumentNullException.ThrowIfNull (name);
		ArgumentNullException.ThrowIfNull (value);
#endif
		if (string.IsNullOrEmpty (name))
			throw new ArgumentException ("Field name cannot be empty", nameof (name));
#if NETSTANDARD2_0
		if (name.IndexOf ('=') >= 0)
#else
		if (name.Contains ('=', StringComparison.Ordinal))
#endif
			throw new ArgumentException ("Field name cannot contain '=' character", nameof (name));

		// Validate ASCII range per Vorbis Comment spec: 0x20-0x7D (printable ASCII minus DEL)
		if (!IsValidFieldName (name))
			throw new ArgumentException ("Field name contains invalid character. Must be ASCII 0x20-0x7D (printable ASCII), excluding '='.", nameof (name));

		Name = name.ToUpperInvariant ();
		Value = value;
	}

	/// <summary>
	/// Validates that a field name contains only valid ASCII characters.
	/// </summary>
	/// <param name="name">The field name to validate.</param>
	/// <returns>True if valid, false otherwise.</returns>
	/// <remarks>
	/// Per the Vorbis Comment specification, field names must contain only ASCII characters
	/// in the range 0x20-0x7D (printable ASCII), excluding 0x3D ('=').
	/// </remarks>
	static bool IsValidFieldName (string name)
	{
		foreach (var c in name) {
			// Valid range: 0x20 (space) to 0x7D ('}'), excluding 0x3D ('=')
			if (c < 0x20 || c > 0x7D || c == '=')
				return false;
		}
		return true;
	}

	/// <summary>
	/// Returns the field in "NAME=value" format.
	/// </summary>
	/// <returns>The formatted field string.</returns>
	public override string ToString () => $"{Name}={Value}";

	/// <summary>
	/// Parses a Vorbis Comment field from "NAME=value" format.
	/// </summary>
	/// <param name="fieldString">The field string to parse.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static VorbisCommentFieldParseResult Parse (string fieldString)
	{
		if (string.IsNullOrEmpty (fieldString))
			return VorbisCommentFieldParseResult.Failure ("Field string is empty");

#if NETSTANDARD2_0
		var equalsIndex = fieldString.IndexOf ('=');
#else
		var equalsIndex = fieldString.IndexOf ('=', StringComparison.Ordinal);
#endif
		if (equalsIndex < 0)
			return VorbisCommentFieldParseResult.Failure ("Field string does not contain '=' separator");

		if (equalsIndex == 0)
			return VorbisCommentFieldParseResult.Failure ("Field name cannot be empty");

		var name = fieldString.Substring (0, equalsIndex);
		var value = fieldString.Substring (equalsIndex + 1);

		// Validate field name characters before constructing
		if (!IsValidFieldName (name))
			return VorbisCommentFieldParseResult.Failure ("Field name contains invalid character. Must be ASCII 0x20-0x7D.");

		return VorbisCommentFieldParseResult.Success (new VorbisCommentField (name, value));
	}

	/// <inheritdoc/>
	public bool Equals (VorbisCommentField other) =>
		string.Equals (Name, other.Name, StringComparison.Ordinal) &&
		string.Equals (Value, other.Value, StringComparison.Ordinal);

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is VorbisCommentField other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Name, Value);

	/// <summary>
	/// Determines whether two fields are equal.
	/// </summary>
	public static bool operator == (VorbisCommentField left, VorbisCommentField right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two fields are not equal.
	/// </summary>
	public static bool operator != (VorbisCommentField left, VorbisCommentField right) =>
		!left.Equals (right);
}

/// <summary>
/// Represents the result of parsing a <see cref="VorbisCommentField"/> from a string.
/// </summary>
public readonly struct VorbisCommentFieldParseResult : IEquatable<VorbisCommentFieldParseResult>
{
	/// <summary>
	/// Gets the parsed field, or default if parsing failed.
	/// </summary>
	public VorbisCommentField Field { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	VorbisCommentFieldParseResult (VorbisCommentField field, bool isSuccess, string? error)
	{
		Field = field;
		IsSuccess = isSuccess;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="field">The parsed field.</param>
	/// <returns>A successful result.</returns>
	public static VorbisCommentFieldParseResult Success (VorbisCommentField field) =>
		new (field, true, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static VorbisCommentFieldParseResult Failure (string error) =>
		new (default, false, error);

	/// <inheritdoc/>
	public bool Equals (VorbisCommentFieldParseResult other) =>
		Field.Equals (other.Field) &&
		IsSuccess == other.IsSuccess &&
		Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is VorbisCommentFieldParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Field, IsSuccess, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (VorbisCommentFieldParseResult left, VorbisCommentFieldParseResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (VorbisCommentFieldParseResult left, VorbisCommentFieldParseResult right) =>
		!left.Equals (right);
}
