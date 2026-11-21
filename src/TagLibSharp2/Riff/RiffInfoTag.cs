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
	/// The stored bytes of each field, kept by a tag in AVI text mode. A field whose value is
	/// unchanged is written back with these bytes, whatever their encoding.
	/// </summary>
	readonly Dictionary<string, (string Value, byte[] Bytes)>? _stored;

	static readonly System.Text.UTF8Encoding StrictUtf8 = new (encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>
	/// Creates an empty tag whose text is Latin-1, as WAV INFO is read and written.
	/// </summary>
	public RiffInfoTag ()
	{
	}

	/// <summary>
	/// Creates an empty tag in AVI text mode when <paramref name="utf8"/> is true.
	/// </summary>
	/// <remarks>
	/// RIFF INFO strings (Multimedia Programming Interface and Data Specifications 1.0, "INFO List
	/// Chunk") are ZSTRs in the character set of a <c>CSET</c> chunk, which AVI writers do not emit.
	/// ffmpeg, the common AVI writer, stores UTF-8, and readers such as ffprobe decode UTF-8.
	/// In AVI text mode a value is read as UTF-8 when its bytes are valid UTF-8, else as Latin-1,
	/// and written as UTF-8. An unchanged value is written back with its original bytes.
	/// </remarks>
	internal RiffInfoTag (bool utf8)
	{
		if (utf8)
			_stored = new (StringComparer.OrdinalIgnoreCase);
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
	public static RiffInfoTag? Parse (BinaryData data) => Parse (data, utf8: false);

	/// <summary>
	/// Parses a RIFF INFO tag, in AVI text mode when <paramref name="utf8"/> is true
	/// (see <see cref="RiffInfoTag(bool)"/>).
	/// </summary>
	internal static RiffInfoTag? Parse (BinaryData data, bool utf8)
	{
		// LIST chunk data starts with type (INFO) then fields
		if (data.Length < 4)
			return null;

		var listType = data.Slice (0, 4).ToStringLatin1 ();
		if (listType != "INFO")
			return null;

		var tag = new RiffInfoTag (utf8);
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
			var value = utf8 ? DecodeAviText (fieldData) : fieldData.ToStringLatin1NullTerminated ();

			if (!string.IsNullOrEmpty (fieldId) && fieldId.Length == 4) {
				tag._fields[fieldId] = value;
				tag._stored?.Remove (fieldId);
				if (tag._stored is not null && value.Length > 0)
					tag._stored[fieldId] = (value, fieldData.ToArray ());
			}

			// Field size does not include the pad. Advance in long; a size near int.MaxValue must not throw.
			var next = (long)offset + fieldSize + (fieldSize & 1);
			if (next > int.MaxValue)
				break;

			offset = (int)next;
		}

		return tag;
	}

	/// <summary>Valid UTF-8 up to the first NUL is UTF-8; anything else is Latin-1.</summary>
	static string DecodeAviText (BinaryData field)
	{
		var bytes = field.ToArray ();
		var end = Array.IndexOf (bytes, (byte)0);
		if (end < 0)
			end = bytes.Length;
		try {
			return StrictUtf8.GetString (bytes, 0, end);
		} catch (System.Text.DecoderFallbackException) {
			return field.ToStringLatin1NullTerminated ();
		}
	}

	BinaryData Encode (string id, string value)
	{
		if (_stored is null)
			return BinaryData.FromStringLatin1NullTerminated (value);
		if (_stored.TryGetValue (id, out var stored) && stored.Value == value)
			return new BinaryData (stored.Bytes);

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

		// Calculate total size needed for INFO fields
		var fieldsSize = 4; // "INFO" type
		foreach (var kvp in _fields) {
			if (string.IsNullOrEmpty (kvp.Key) || kvp.Key.Length != 4)
				continue;

			var encoded = Encode (kvp.Key, kvp.Value);
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

			var encoded = Encode (kvp.Key, kvp.Value);

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
