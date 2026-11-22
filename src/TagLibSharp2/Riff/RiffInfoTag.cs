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
	/// The stored bytes of each field, kept by a tag in AVI text mode, and whether they are UTF-8
	/// (so they can share a UTF-8 list). An unchanged field is written back with exactly these bytes.
	/// </summary>
	readonly Dictionary<string, (string Value, byte[] Bytes, InfoTextKind Kind)>? _stored;

	/// <summary>The code page the file's CSET declared, for a tag in AVI text mode.</summary>
	readonly InfoCharacterSet _characterSet;

	/// <summary>The CSET's wCodePage, or -1 when the file has none, for messages.</summary>
	readonly int _codePage;

	/// <summary>
	/// Creates an empty tag whose text is Latin-1, as WAV INFO is read and written.
	/// </summary>
	public RiffInfoTag ()
	{
	}

	/// <summary>
	/// Creates an empty tag in AVI text mode, for a file whose CSET declares <paramref name="characterSet"/>.
	/// </summary>
	/// <remarks>
	/// <para>Values are decoded as <see cref="InfoText"/> describes.</para>
	/// <para>
	/// Invariant on save: a value the caller did not change keeps its meaning under the file's CSET.
	/// Changed values are encoded in the file's declared code page when they all fit it (with no CSET,
	/// or one this library does not encode, only ASCII fits); unchanged values then keep their exact
	/// bytes and the CSET is kept as it is. When a changed value does not fit, the list becomes UTF-8
	/// under a CSET of code page 65001: ASCII and UTF-8 values keep their bytes, and a value decoded
	/// exactly in its declared code page (<see cref="InfoTextKind.Exact"/>) is written as UTF-8 of the
	/// same text. A value whose code page is a guess (<see cref="InfoTextKind.Guess"/>) cannot be
	/// rewritten, so <see cref="TryRenderAvi"/> refuses instead.
	/// </para>
	/// </remarks>
	internal RiffInfoTag (InfoCharacterSet characterSet, int codePage = -1)
	{
		_characterSet = characterSet;
		_codePage = codePage;
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

	/// <inheritdoc/>
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	/// <remarks>
	/// The name is the four-character code of an INFO subchunk, such as <c>IENG</c> or a code of your own.
	/// Codes compare case-insensitively, as <see cref="GetField"/> compares them. An INFO subchunk holds one
	/// ZSTR (Microsoft RIFF specification, "INFO List Chunk"), so there is at most one value.
	/// </remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsFourCC (name))
			return CustomFieldValues.None;
		var value = GetField (name);
		return string.IsNullOrEmpty (value) ? CustomFieldValues.None : [value!];
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Returns false for a name that is not four ASCII characters from 0x20 to 0x7E (a FOURCC, Microsoft
	/// RIFF specification, "Chunks") and for more than one value. A WAV INFO list is Latin-1, one byte per
	/// character, so text outside ISO-8859-1 is refused there. An AVI INFO list takes any text, as the mapped
	/// fields do: the save encodes it in the file's declared code page when every changed value fits, and
	/// otherwise makes the list UTF-8 under a CSET of code page 65001 (see <see cref="RiffInfoTag(InfoCharacterSet, int)"/>).
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsFourCC (name) || !CustomFieldValues.TryNormalize (values, out var kept) || kept.Count > 1)
			return false;
		if (_stored is null && kept.Count == 1) {
			foreach (var c in kept[0]) {
				if (c > 0xFF)
					return false;
			}
		}

		SetField (name, kept.Count == 0 ? null : kept[0]);
		return true;
	}

	/// <inheritdoc/>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		return IsFourCC (name) && _fields.Remove (name);
	}

	static bool IsFourCC (string name)
	{
		if (name.Length != 4)
			return false;
		foreach (var c in name) {
			if (c < 0x20 || c > 0x7E)
				return false;
		}
		return true;
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
	/// <see cref="RiffInfoTag(InfoCharacterSet, int)"/>); null reads every value as Latin-1, as WAV does.
	/// </summary>
	internal static RiffInfoTag? Parse (BinaryData data, InfoCharacterSet? characterSet, int codePage = -1)
	{
		// LIST chunk data starts with type (INFO) then fields
		if (data.Length < 4)
			return null;

		var listType = data.Slice (0, 4).ToStringLatin1 ();
		if (listType != "INFO")
			return null;

		var tag = characterSet is { } declared ? new RiffInfoTag (declared, codePage) : new RiffInfoTag ();
		var offset = 4;

		var span = data.Span;
		while (offset + 8 <= data.Length) {
			// Read field FourCC
			var fieldId = new string ([(char)span[offset], (char)span[offset + 1], (char)span[offset + 2], (char)span[offset + 3]]);
			offset += 4;

			// Read field size
			var fieldSize = data.ToUInt32LE (offset);
			offset += 4;

			// offset is positive and data.Length fits in an int, so a uint size above int.MaxValue
			// fails this comparison and is not cast. The pad is applied after the field is read.
			if ((long)offset + fieldSize > data.Length)
				break;

			// Read field value (null-terminated string, safe cast after overflow check).
			// AVI text mode copies the field once: that copy is decoded and kept as the stored bytes.
			string value;
			if (characterSet is { } set) {
				var bytes = span.Slice (offset, (int)fieldSize).ToArray ();
				value = InfoText.Decode (bytes, set, out var kind);
				tag._stored!.Remove (fieldId);
				if (value.Length > 0)
					tag._stored[fieldId] = (value, bytes, kind);
			} else {
				value = data.Slice (offset, (int)fieldSize).ToStringLatin1NullTerminated ();
			}

			tag._fields[fieldId] = value;

			// Field size does not include the pad. Advance in long; a size near int.MaxValue must not throw.
			var next = (long)offset + fieldSize + (fieldSize & 1);
			if (next > int.MaxValue)
				break;

			offset = (int)next;
		}

		return tag;
	}

	/// <summary>
	/// True when <paramref name="other"/> holds the same fields with the same values, in the same order.
	/// A save leaves the INFO list alone when nothing changed.
	/// </summary>
	internal bool SameFieldsAs (RiffInfoTag? other)
	{
		if (other is null)
			return _fields.Count == 0;
		if (other._fields.Count != _fields.Count)
			return false;

		using var mine = _fields.GetEnumerator ();
		using var theirs = other._fields.GetEnumerator ();
		while (mine.MoveNext () && theirs.MoveNext ()) {
			if (!string.Equals (mine.Current.Key, theirs.Current.Key, StringComparison.OrdinalIgnoreCase)
				|| mine.Current.Value != theirs.Current.Value)
				return false;
		}

		return true;
	}

	bool IsUnchanged (string id, string value, out (string Value, byte[] Bytes, InfoTextKind Kind) stored) =>
		_stored!.TryGetValue (id, out stored) && stored.Value == value;

	/// <summary>
	/// Renders the list in AVI text mode, keeping the invariant on <see cref="RiffInfoTag(InfoCharacterSet, int)"/>.
	/// <paramref name="declareUtf8"/> is true when the list became UTF-8 and needs a CSET of code page
	/// 65001 in place of the file's own CSET. Returns false, with <paramref name="error"/>, when a changed
	/// value does not fit the file's code page and an unchanged value's code page is a guess.
	/// </summary>
	internal bool TryRenderAvi (out BinaryData list, out bool declareUtf8, out string? error)
	{
		list = BinaryData.Empty;
		declareUtf8 = false;
		error = null;
		if (_stored is null)
			throw new InvalidOperationException ("Not an AVI text mode tag.");

		var target = _characterSet;
		foreach (var kvp in _fields) {
			if (!IsUnchanged (kvp.Key, kvp.Value, out _) && !InfoText.CanEncode (kvp.Value, _characterSet)) {
				// Only a file not already in UTF-8 gets here: UTF-8 encodes every value.
				target = InfoCharacterSet.Utf8;
				declareUtf8 = true;
				break;
			}
		}

		if (declareUtf8) {
			foreach (var kvp in _fields) {
				if (IsUnchanged (kvp.Key, kvp.Value, out var stored) && stored.Kind == InfoTextKind.Guess) {
					var reason = InfoText.GuessReason (stored.Bytes, _characterSet, _codePage);
					error = $"Cannot save the INFO list: a changed value needs UTF-8, and {kvp.Key} ('{InfoText.Escape (kvp.Value)}') "
						+ $"{reason}, so it cannot be rewritten as UTF-8 without guessing its text. Set or remove that field too.";
					return false;
				}
			}
		}

		list = RenderFields (target, keepStored: true);
		return true;
	}

	/// <inheritdoc />
	/// <remarks>
	/// In AVI text mode this is the list <see cref="TryRenderAvi"/> writes. When that save would be
	/// refused, unchanged values keep their bytes and changed values are UTF-8; no CSET describes that list.
	/// </remarks>
	public override BinaryData Render ()
	{
		if (_fields.Count == 0)
			return BinaryData.Empty;
		if (_stored is null)
			return RenderFields (InfoCharacterSet.Latin1, keepStored: false);
		if (TryRenderAvi (out var list, out _, out _))
			return list;
		return RenderFields (InfoCharacterSet.Utf8, keepStored: true);
	}

	BinaryData RenderFields (InfoCharacterSet target, bool keepStored)
	{
		if (_fields.Count == 0)
			return BinaryData.Empty;

		var encodedFields = new List<(string Id, byte[] Bytes)> (_fields.Count);
		var fieldsSize = 4; // "INFO" type
		foreach (var kvp in _fields) {
			if (string.IsNullOrEmpty (kvp.Key) || kvp.Key.Length != 4)
				continue;

			// An unchanged value keeps its bytes, except an exactly decoded legacy value in a list that
			// became UTF-8: that one is written as UTF-8 of the same text.
			var bytes = keepStored && IsUnchanged (kvp.Key, kvp.Value, out var stored)
					&& !(stored.Kind == InfoTextKind.Exact && target == InfoCharacterSet.Utf8)
				? stored.Bytes
				: _stored is null ? BinaryData.FromStringLatin1NullTerminated (kvp.Value).ToArray () : InfoText.Encode (kvp.Value, target);
			encodedFields.Add ((kvp.Key, bytes));
			fieldsSize += 8 + bytes.Length + (bytes.Length & 1);
		}

		using var builder = new BinaryDataBuilder (8 + fieldsSize);
		builder.AddStringLatin1 ("LIST");
		builder.AddUInt32LE ((uint)fieldsSize);
		builder.AddStringLatin1 ("INFO");
		foreach (var (id, bytes) in encodedFields) {
			builder.AddStringLatin1 (id);
			builder.AddUInt32LE ((uint)bytes.Length);
			builder.Add (bytes);
			if ((bytes.Length & 1) != 0)
				builder.Add (0);
		}

		return builder.ToBinaryData ();
	}
}
