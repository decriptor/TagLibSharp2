// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
	/// The stored bytes of each field, kept by a tag in AVI text mode, and whether they were UTF-8.
	/// An unchanged field is written back with these bytes when they match the list's encoding.
	/// </summary>
	readonly Dictionary<string, (string Value, byte[] Bytes, bool Utf8)>? _stored;

	static readonly System.Text.UTF8Encoding StrictUtf8 = new (encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>
	/// Creates an empty tag whose text is Latin-1, as WAV INFO is read and written.
	/// </summary>
	public RiffInfoTag ()
	{
	}

	/// <summary>
	/// Creates an empty tag in AVI text mode when <paramref name="aviText"/> is true.
	/// </summary>
	/// <remarks>
	/// <para>
	/// RIFF INFO strings are ZSTRs in the code page of the file's <c>CSET</c> chunk; "if the CSET
	/// chunk is not present, or if this field has a value of zero, assume standard ISO 8859/1 code
	/// page" (Multimedia Programming Interface and Data Specifications 1.0, "CSET (Character Set)
	/// Chunk"). The common AVI writers, ffmpeg and TagLib#, ignore that and store UTF-8 without a CSET.
	/// </para>
	/// <para>
	/// Reading in AVI text mode: a CSET of code page 65001 means UTF-8, code page 0 or 28591 means
	/// ISO 8859-1. Without a usable CSET, a value whose bytes are valid UTF-8 is read as UTF-8 (the
	/// writers above), and any other value as ISO 8859-1 (the spec default).
	/// </para>
	/// <para>
	/// Writing: when every value is ASCII the list is written plain, with no CSET, which every reader
	/// agrees on. Otherwise every value is UTF-8 and <see cref="NeedsUtf8CharacterSet"/> tells the
	/// writer to put a CSET of code page 65001 before the list, so the encoding is declared (ffprobe,
	/// TagLib# and exiftool read it; mediainfo ignores CSET). An unchanged value keeps its original
	/// bytes when they are ASCII or UTF-8; an unchanged non-ASCII ISO 8859-1 value is re-encoded,
	/// because the CSET declares the whole list UTF-8.
	/// </para>
	/// </remarks>
	internal RiffInfoTag (bool aviText)
	{
		if (aviText)
			_stored = new (StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// True for a tag in AVI text mode whose list is written as UTF-8 with a non-ASCII value, so the
	/// file needs a CSET chunk of code page 65001 before the list.
	/// </summary>
	internal bool NeedsUtf8CharacterSet => _stored is not null && WritesUtf8;

	/// <summary>The list is UTF-8 when any value is not ASCII.</summary>
	bool WritesUtf8 {
		get {
			foreach (var value in _fields.Values) {
				if (!IsAscii (value))
					return true;
			}

			return false;
		}
	}

	static bool IsAscii (string value)
	{
		foreach (var c in value) {
			if (c > '\u007F')
				return false;
		}

		return true;
	}

	static bool IsAscii (byte[] bytes)
	{
		foreach (var b in bytes) {
			if (b > 0x7F)
				return false;
		}

		return true;
	}

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
	public override string? Title {
		get => GetField (INAM);
		set => SetField (INAM, value);
	}

	/// <inheritdoc />
	public override string? Artist {
		get => GetField (IART);
		set => SetField (IART, value);
	}

	/// <inheritdoc />
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Performers {
		get {
			var artist = GetField (IART);
			return string.IsNullOrEmpty (artist)
				? []
				: [artist!];
		}
		set => SetField (IART, FirstNonEmpty (value));
	}
#pragma warning restore CA1819

	/// <inheritdoc />
	public override string? Album {
		get => GetField (IPRD);
		set => SetField (IPRD, value);
	}

	/// <inheritdoc />
	public override string? Comment {
		get => GetField (ICMT);
		set => SetField (ICMT, value);
	}

	/// <inheritdoc />
	public override string? Genre {
		get => GetField (IGNR);
		set => SetField (IGNR, value);
	}

	/// <inheritdoc />
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Genres {
		get {
			var genre = GetField (IGNR);
			return string.IsNullOrEmpty (genre)
				? []
				: [genre!];
		}
		set => SetField (IGNR, FirstNonEmpty (value));
	}
#pragma warning restore CA1819

	/// <inheritdoc />
	public override string? Year {
		get {
			var icrd = GetField (ICRD);
			if (string.IsNullOrEmpty (icrd))
				return null;

			// ICRD can contain full date, return as-is for string format
			return icrd;
		}
		set => SetField (ICRD, value);
	}

	/// <inheritdoc />
	public override uint? Track {
		get {
			var track = GetField (ITRK);
			if (string.IsNullOrEmpty (track))
				return null;

			return uint.TryParse (track, out var t) ? t : null;
		}
		set => SetField (ITRK, value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc />
	public override string? Copyright {
		get => GetField (ICOP);
		set => SetField (ICOP, value);
	}

	/// <summary>
	/// Gets or sets the software field.
	/// </summary>
	public string? Software {
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
			_stored?.Remove (fourCC);
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
	/// Copies fields from a later INFO list that this tag left empty.
	/// A field already set here wins and is not overridden.
	/// </summary>
	/// <param name="later">The later list. Its values fill only missing fields.</param>
	internal void FillMissingFrom (RiffInfoTag later)
	{
		foreach (var id in later.FieldIds) {
			if (!string.IsNullOrEmpty (GetField (id)))
				continue;

			var value = later.GetField (id);
			if (string.IsNullOrEmpty (value))
				continue;
			SetField (id, value);
			if (_stored is not null && later._stored is not null && later._stored.TryGetValue (id, out var stored))
				_stored[id] = stored;
		}
	}

	/// <summary>
	/// Clears all fields from this tag.
	/// </summary>
	public override void Clear ()
	{
		_fields.Clear ();
		_stored?.Clear ();
	}

	/// <summary>
	/// Parses a RIFF INFO tag from a LIST chunk's data.
	/// </summary>
	/// <param name="data">The LIST chunk data (starting after LIST header).</param>
	/// <returns>The parsed tag, or null if invalid.</returns>
	public static RiffInfoTag? Parse (BinaryData data) => Parse (data, null);

	/// <summary>
	/// Parses a RIFF INFO tag. A <paramref name="characterSet"/> selects AVI text mode (see
	/// <see cref="RiffInfoTag(bool)"/>); null reads every value as Latin-1, as WAV does.
	/// </summary>
	internal static RiffInfoTag? Parse (BinaryData data, InfoCharacterSet? characterSet)
	{
		// LIST chunk data starts with type (INFO) then fields
		if (data.Length < 4)
			return null;

		var listType = data.Slice (0, 4).ToStringLatin1 ();
		if (listType != "INFO")
			return null;

		var tag = new RiffInfoTag (characterSet is not null);
		var offset = 4;

		while (offset + 8 <= data.Length) {
			// Read field FourCC
			var fieldId = data.Slice (offset, 4).ToStringLatin1 ();
			offset += 4;

			// Read field size
			var fieldSize = data.ToUInt32LE (offset);
			offset += 4;

			// offset is positive and data.Length fits in an int, so a uint size above int.MaxValue
			// fails this comparison and is not cast. The pad is applied after the field is read.
			if ((long)offset + fieldSize > data.Length)
				break;

			// Read field value (null-terminated string, safe cast after overflow check)
			var fieldData = data.Slice (offset, (int)fieldSize);
			var isUtf8 = false;
			var value = characterSet is { } set
				? DecodeAviText (fieldData, set, out isUtf8)
				: fieldData.ToStringLatin1NullTerminated ();

			if (!string.IsNullOrEmpty (fieldId) && fieldId.Length == 4) {
				tag._fields[fieldId] = value;
				tag._stored?.Remove (fieldId);
				if (tag._stored is not null && value.Length > 0) {
					tag._stored[fieldId] = (value, fieldData.ToArray (), isUtf8);
				}
			}

			// Field size does not include the pad. Advance in long; a size near int.MaxValue must not throw.
			var next = (long)offset + fieldSize + (fieldSize & 1);
			if (next > int.MaxValue)
				break;

			offset = (int)next;
		}

		return tag;
	}

	/// <summary>
	/// Decodes a value up to its first NUL. A declared UTF-8 set, or an undeclared one whose bytes
	/// are valid UTF-8, is UTF-8; everything else is ISO 8859-1.
	/// </summary>
	static string DecodeAviText (BinaryData field, InfoCharacterSet characterSet, out bool utf8)
	{
		utf8 = false;
		if (characterSet == InfoCharacterSet.Latin1)
			return field.ToStringLatin1NullTerminated ();

		var bytes = field.ToArray ();
		var end = Array.IndexOf (bytes, (byte)0);
		if (end < 0)
			end = bytes.Length;
		try {
			var text = StrictUtf8.GetString (bytes, 0, end);
			utf8 = true;
			return text;
		} catch (System.Text.DecoderFallbackException) {
			return field.ToStringLatin1NullTerminated ();
		}
	}

	BinaryData Encode (string id, string value, bool utf8)
	{
		if (_stored is null)
			return BinaryData.FromStringLatin1NullTerminated (value);
		if (_stored.TryGetValue (id, out var stored) && stored.Value == value && (IsAscii (stored.Bytes) || (stored.Utf8 && utf8)))
			return new BinaryData (stored.Bytes);
		if (!utf8)
			return BinaryData.FromStringLatin1NullTerminated (value);

		var text = System.Text.Encoding.UTF8.GetBytes (value);
		var encoded = new byte[text.Length + 1];
		text.CopyTo (encoded, 0);
		return new BinaryData (encoded);
	}

	/// <inheritdoc />
	public override BinaryData Render ()
	{
		if (_fields.Count == 0)
			return BinaryData.Empty;

		var utf8 = _stored is not null && WritesUtf8;

		// Calculate total size needed for INFO fields
		var fieldsSize = 4; // "INFO" type
		foreach (var kvp in _fields) {
			if (string.IsNullOrEmpty (kvp.Key) || kvp.Key.Length != 4)
				continue;

			var encoded = Encode (kvp.Key, kvp.Value, utf8);
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

			var encoded = Encode (kvp.Key, kvp.Value, utf8);

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

/// <summary>The character set an AVI file declares for its INFO text.</summary>
internal enum InfoCharacterSet
{
	/// <summary>No usable CSET chunk: valid UTF-8 is read as UTF-8, anything else as ISO 8859-1.</summary>
	Detect,

	/// <summary>CSET wCodePage 65001.</summary>
	Utf8,

	/// <summary>CSET wCodePage 0 (the spec's default) or 28591: ISO 8859-1.</summary>
	Latin1
}
