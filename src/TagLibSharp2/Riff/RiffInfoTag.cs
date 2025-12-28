// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// Represents RIFF INFO metadata stored in a LIST chunk with "INFO" type.
/// This is the native metadata format for WAV files.
/// </summary>
/// <remarks>
/// LIST INFO structure:
/// - "LIST" (4 bytes)
/// - Chunk size (4 bytes, LE)
/// - "INFO" (4 bytes)
/// - Info fields: FourCC + size + null-terminated string (padded to even)
///
/// Common INFO fields:
/// - INAM = Title
/// - IART = Artist
/// - IPRD = Album/Product
/// - ICMT = Comment
/// - IGNR = Genre
/// - ICRD = Creation date (year)
/// - ITRK = Track number
/// - ISFT = Software
/// - ICOP = Copyright
/// </remarks>
public class RiffInfoTag : Tag
{
	readonly Dictionary<string, string> _fields = new (StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// INFO field ID for Title.
	/// </summary>
	public const string INAM = "INAM";

	/// <summary>
	/// INFO field ID for Artist.
	/// </summary>
	public const string IART = "IART";

	/// <summary>
	/// INFO field ID for Album/Product.
	/// </summary>
	public const string IPRD = "IPRD";

	/// <summary>
	/// INFO field ID for Comment.
	/// </summary>
	public const string ICMT = "ICMT";

	/// <summary>
	/// INFO field ID for Genre.
	/// </summary>
	public const string IGNR = "IGNR";

	/// <summary>
	/// INFO field ID for Creation Date (year).
	/// </summary>
	public const string ICRD = "ICRD";

	/// <summary>
	/// INFO field ID for Track Number.
	/// </summary>
	public const string ITRK = "ITRK";

	/// <summary>
	/// INFO field ID for Software.
	/// </summary>
	public const string ISFT = "ISFT";

	/// <summary>
	/// INFO field ID for Copyright.
	/// </summary>
	public const string ICOP = "ICOP";

	/// <summary>
	/// INFO field ID for Engineer.
	/// </summary>
	public const string IENG = "IENG";

	/// <summary>
	/// INFO field ID for Technician.
	/// </summary>
	public const string ITCH = "ITCH";

	/// <summary>
	/// INFO field ID for Keywords.
	/// </summary>
	public const string IKEY = "IKEY";

	/// <summary>
	/// INFO field ID for Subject.
	/// </summary>
	public const string ISBJ = "ISBJ";

	/// <summary>
	/// INFO field ID for Source.
	/// </summary>
	public const string ISRC = "ISRC";

	/// <inheritdoc />
	public override TagTypes TagType => TagTypes.RiffInfo;

	/// <inheritdoc />
	public override string? Title
	{
		get => GetField (INAM);
		set => SetField (INAM, value);
	}

	/// <inheritdoc />
	public override string? Artist
	{
		get => GetField (IART);
		set => SetField (IART, value);
	}

	/// <inheritdoc />
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Performers
	{
		get
		{
			var artist = GetField (IART);
			return string.IsNullOrEmpty (artist)
				? []
				: [artist!];
		}
		set => SetField (IART, value?.Length > 0 ? value[0] : null);
	}
#pragma warning restore CA1819

	/// <inheritdoc />
	public override string? Album
	{
		get => GetField (IPRD);
		set => SetField (IPRD, value);
	}

	/// <inheritdoc />
	public override string? Comment
	{
		get => GetField (ICMT);
		set => SetField (ICMT, value);
	}

	/// <inheritdoc />
	public override string? Genre
	{
		get => GetField (IGNR);
		set => SetField (IGNR, value);
	}

	/// <inheritdoc />
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Genres
	{
		get
		{
			var genre = GetField (IGNR);
			return string.IsNullOrEmpty (genre)
				? []
				: [genre!];
		}
		set => SetField (IGNR, value?.Length > 0 ? value[0] : null);
	}
#pragma warning restore CA1819

	/// <inheritdoc />
	public override string? Year
	{
		get
		{
			var icrd = GetField (ICRD);
			if (string.IsNullOrEmpty (icrd))
				return null;

			// ICRD can contain full date, return as-is for string format
			return icrd;
		}
		set => SetField (ICRD, value);
	}

	/// <inheritdoc />
	public override uint? Track
	{
		get
		{
			var track = GetField (ITRK);
			if (string.IsNullOrEmpty (track))
				return null;

			return uint.TryParse (track, out var t) ? t : null;
		}
		set => SetField (ITRK, value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc />
	public override string? Copyright
	{
		get => GetField (ICOP);
		set => SetField (ICOP, value);
	}

	/// <summary>
	/// Gets or sets the software field.
	/// </summary>
	public string? Software
	{
		get => GetField (ISFT);
		set => SetField (ISFT, value);
	}

	/// <summary>
	/// Gets a field value by its FourCC.
	/// </summary>
	/// <param name="fourCC">The 4-character field identifier.</param>
	/// <returns>The field value, or null if not present.</returns>
	public string? GetField (string fourCC)
	{
		return _fields.TryGetValue (fourCC, out var value) ? value : null;
	}

	/// <summary>
	/// Sets a field value by its FourCC.
	/// </summary>
	/// <param name="fourCC">The 4-character field identifier.</param>
	/// <param name="value">The value to set, or null to remove the field.</param>
	public void SetField (string fourCC, string? value)
	{
		if (string.IsNullOrEmpty (value)) {
			_fields.Remove (fourCC);
		} else {
#pragma warning disable CS8601 // Possible null reference assignment - checked above
			_fields[fourCC] = value;
#pragma warning restore CS8601
		}
	}

	/// <summary>
	/// Gets all field identifiers present in this tag.
	/// </summary>
	public IEnumerable<string> FieldIds => _fields.Keys;

	/// <summary>
	/// Gets whether this tag has any fields.
	/// </summary>
	public override bool IsEmpty => _fields.Count == 0;

	/// <summary>
	/// Clears all fields from this tag.
	/// </summary>
	public override void Clear () => _fields.Clear ();

	/// <summary>
	/// Parses a RIFF INFO tag from a LIST chunk's data.
	/// </summary>
	/// <param name="data">The LIST chunk data (starting after LIST header).</param>
	/// <returns>The parsed tag, or null if invalid.</returns>
	public static RiffInfoTag? Parse (BinaryData data)
	{
		// LIST chunk data starts with type (INFO) then fields
		if (data.Length < 4)
			return null;

		var listType = data.Slice (0, 4).ToStringLatin1 ();
		if (listType != "INFO")
			return null;

		var tag = new RiffInfoTag ();
		var offset = 4;

		while (offset + 8 <= data.Length) {
			// Read field FourCC
			var fieldId = data.Slice (offset, 4).ToStringLatin1 ();
			offset += 4;

			// Read field size
			var fieldSize = data.ToUInt32LE (offset);
			offset += 4;

			if (offset + fieldSize > data.Length)
				break;

			// Read field value (null-terminated string)
			var fieldData = data.Slice (offset, (int)fieldSize);
			var value = fieldData.ToStringLatin1NullTerminated ();

			if (!string.IsNullOrEmpty (fieldId) && fieldId.Length == 4)
				tag._fields[fieldId] = value;

			// Move to next field (with padding to even boundary)
			offset += (int)fieldSize + ((int)fieldSize & 1);
		}

		return tag;
	}

	/// <inheritdoc />
	public override BinaryData Render ()
	{
		if (_fields.Count == 0)
			return BinaryData.Empty;

		// Calculate total size needed for INFO fields
		var fieldsSize = 4; // "INFO" type
		foreach (var kvp in _fields) {
			if (string.IsNullOrEmpty (kvp.Key) || kvp.Key.Length != 4)
				continue;

			var encoded = BinaryData.FromStringLatin1NullTerminated (kvp.Value);
			fieldsSize += 8 + encoded.Length; // FourCC + size + data
			if ((encoded.Length & 1) != 0)
				fieldsSize++; // Padding
		}

		using var builder = new BinaryDataBuilder (8 + fieldsSize);

		// LIST chunk header
		builder.AddStringLatin1 ("LIST");
		builder.AddUInt32LE ((uint)fieldsSize);
		builder.AddStringLatin1 ("INFO");

		// Fields
		foreach (var kvp in _fields) {
			if (string.IsNullOrEmpty (kvp.Key) || kvp.Key.Length != 4)
				continue;

			var encoded = BinaryData.FromStringLatin1NullTerminated (kvp.Value);

			builder.AddStringLatin1 (kvp.Key);
			builder.AddUInt32LE ((uint)encoded.Length);
			builder.Add (encoded);

			// Padding to even boundary
			if ((encoded.Length & 1) != 0)
				builder.Add (0);
		}

		return builder.ToBinaryData ();
	}
}
