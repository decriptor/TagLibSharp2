// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents an ASF/WMA tag with metadata from Content Description and Extended Content Description objects.
/// </summary>
public sealed class AsfTag : Tag
{
	AsfContentDescription _contentDescription;
	readonly List<AsfDescriptor> _descriptors;

	/// <summary>
	/// Creates a new empty ASF tag.
	/// </summary>
	public AsfTag ()
	{
		_contentDescription = new AsfContentDescription ("", "", "", "", "");
		_descriptors = [];
	}

	/// <summary>
	/// Creates an ASF tag from parsed Content Description and Extended Content Description objects.
	/// </summary>
	public AsfTag (AsfContentDescription? contentDescription, AsfExtendedContentDescription? extendedContent)
	{
		_contentDescription = contentDescription ?? new AsfContentDescription ("", "", "", "", "");
		_descriptors = extendedContent?.Descriptors.ToList () ?? [];
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Asf;

	// ═══════════════════════════════════════════════════════════════
	// Content Description Mappings (5 fixed fields)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Title {
		get => _contentDescription.Title;
		set => ReplaceContentDescriptionField ("Title", value ?? "");
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _contentDescription.Author;
		set => ReplaceContentDescriptionField ("Author", value ?? "");
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => _contentDescription.Copyright;
		set => ReplaceContentDescriptionField ("Copyright", value ?? "");
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => _contentDescription.Description;
		set => ReplaceContentDescriptionField ("Description", value ?? "");
	}

	/// <summary>
	/// Gets or sets the rating field from Content Description.
	/// </summary>
	public string Rating {
		get => _contentDescription.Rating;
		set => ReplaceContentDescriptionField ("Rating", value ?? "");
	}

	// ═══════════════════════════════════════════════════════════════
	// Extended Content Description Mappings (WM/* attributes)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Album {
		get => GetString ("WM/AlbumTitle") ?? "";
		set => SetString ("WM/AlbumTitle", value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => GetString ("WM/Year");
		set => SetString ("WM/Year", value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetString ("WM/Genre") ?? "";
		set => SetString ("WM/Genre", value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get {
			var desc = GetDescriptor ("WM/TrackNumber");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			// Try parsing string value
			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var track))
				return track;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/TrackNumber", value.Value);
			else
				RemoveDescriptor ("WM/TrackNumber");
		}
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetString ("WM/AlbumArtist");
		set => SetString ("WM/AlbumArtist", value);
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => GetString ("WM/Composer");
		set => SetString ("WM/Composer", value);
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetString ("WM/Conductor");
		set => SetString ("WM/Conductor", value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => ParsePartOfSet ().disc;
		set {
			var (_, count) = ParsePartOfSet ();
			SetPartOfSet (value, count);
		}
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => ParsePartOfSet ().count;
		set {
			var (disc, _) = ParsePartOfSet ();
			SetPartOfSet (disc, value);
		}
	}

	/// <summary>
	/// Gets the disc count from WM/PartOfSet.
	/// </summary>
	public uint? DiscCount => TotalDiscs;

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var desc = GetDescriptor ("WM/BeatsPerMinute");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bpm))
				return bpm;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/BeatsPerMinute", value.Value);
			else
				RemoveDescriptor ("WM/BeatsPerMinute");
		}
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetString ("WM/Publisher");
		set => SetString ("WM/Publisher", value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetString ("WM/Lyrics");
		set => SetString ("WM/Lyrics", value);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetString ("WM/ISRC");
		set => SetString ("WM/ISRC", value);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get {
			var desc = GetDescriptor ("WM/IsCompilation");
			return desc?.BoolValue == true;
		}
		set => SetBool ("WM/IsCompilation", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetString ("MusicBrainz/Track Id");
		set => SetString ("MusicBrainz/Track Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetString ("MusicBrainz/Album Id");
		set => SetString ("MusicBrainz/Album Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetString ("MusicBrainz/Artist Id");
		set => SetString ("MusicBrainz/Artist Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetString ("MusicBrainz/Album Artist Id");
		set => SetString ("MusicBrainz/Album Artist Id", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetString ("REPLAYGAIN_TRACK_GAIN");
		set => SetString ("REPLAYGAIN_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetString ("REPLAYGAIN_TRACK_PEAK");
		set => SetString ("REPLAYGAIN_TRACK_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetString ("REPLAYGAIN_ALBUM_GAIN");
		set => SetString ("REPLAYGAIN_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetString ("REPLAYGAIN_ALBUM_PEAK");
		set => SetString ("REPLAYGAIN_ALBUM_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => GetString ("R128_TRACK_GAIN");
		set => SetString ("R128_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => GetString ("R128_ALBUM_GAIN");
		set => SetString ("R128_ALBUM_GAIN", value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Extended Metadata Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Subtitle {
		get => GetString ("WM/SubTitle");
		set => SetString ("WM/SubTitle", value);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => GetString ("WM/Language");
		set => SetString ("WM/Language", value);
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => GetString ("WM/OriginalReleaseDate");
		set => SetString ("WM/OriginalReleaseDate", value);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => GetString ("WM/Barcode");
		set => SetString ("WM/Barcode", value);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => GetString ("WM/CatalogNo");
		set => SetString ("WM/CatalogNo", value);
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get {
			var desc = GetDescriptor ("WM/TrackTotal");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
				return num;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/TrackTotal", value.Value);
			else
				RemoveDescriptor ("WM/TrackTotal");
		}
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => GetString ("WM/ContentGroupDescription");
		set => SetString ("WM/ContentGroupDescription", value);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => GetString ("WM/ModifiedBy");
		set => SetString ("WM/ModifiedBy", value);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => GetString ("WM/InitialKey");
		set => SetString ("WM/InitialKey", value);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => GetString ("WM/Mood");
		set => SetString ("WM/Mood", value);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => GetString ("WM/Media");
		set => SetString ("WM/Media", value);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => GetString ("WM/EncodedBy");
		set => SetString ("WM/EncodedBy", value);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => GetString ("WM/EncodingSettings");
		set => SetString ("WM/EncodingSettings", value);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => GetString ("WM/Description");
		set => SetString ("WM/Description", value);
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => GetString ("WM/DateTagged");
		set => SetString ("WM/DateTagged", value);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => GetString ("ASIN");
		set => SetString ("ASIN", value);
	}

	// ═══════════════════════════════════════════════════════════════
	// MusicBrainz Extended IDs
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetString ("MusicBrainz/Release Group Id");
		set => SetString ("MusicBrainz/Release Group Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => GetString ("MusicBrainz/Work Id");
		set => SetString ("MusicBrainz/Work Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => GetString ("MusicBrainz/Disc Id");
		set => SetString ("MusicBrainz/Disc Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => GetString ("MusicBrainz/Recording Id");
		set => SetString ("MusicBrainz/Recording Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => GetString ("MusicBrainz/Album Status");
		set => SetString ("MusicBrainz/Album Status", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => GetString ("MusicBrainz/Album Type");
		set => SetString ("MusicBrainz/Album Type", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => GetString ("MusicBrainz/Album Release Country");
		set => SetString ("MusicBrainz/Album Release Country", value);
	}

	// ═══════════════════════════════════════════════════════════════
	// AcoustId Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => GetString ("Acoustid/Id");
		set => SetString ("Acoustid/Id", value);
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => GetString ("Acoustid/Fingerprint");
		set => SetString ("Acoustid/Fingerprint", value);
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => GetString ("WM/PodcastFeedUrl");
		set => SetString ("WM/PodcastFeedUrl", value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Classical Music Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Work {
		get => GetString ("WM/Work");
		set => SetString ("WM/Work", value);
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => GetString ("WM/Movement");
		set => SetString ("WM/Movement", value);
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get {
			var desc = GetDescriptor ("WM/MovementNumber");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
				return num;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/MovementNumber", value.Value);
			else
				RemoveDescriptor ("WM/MovementNumber");
		}
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get {
			var desc = GetDescriptor ("WM/MovementTotal");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
				return num;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/MovementTotal", value.Value);
			else
				RemoveDescriptor ("WM/MovementTotal");
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Sort Order Fields (WM/*)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetString ("WM/TitleSortOrder");
		set => SetString ("WM/TitleSortOrder", value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetString ("WM/ArtistSortOrder");
		set => SetString ("WM/ArtistSortOrder", value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetString ("WM/AlbumSortOrder");
		set => SetString ("WM/AlbumSortOrder", value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetString ("WM/AlbumArtistSortOrder");
		set => SetString ("WM/AlbumArtistSortOrder", value);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => GetString ("WM/ComposerSortOrder");
		set => SetString ("WM/ComposerSortOrder", value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Pictures (WM/Picture)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override IPicture[] Pictures {
		get {
			var pictures = new List<IPicture> ();
			for (int i = 0; i < _descriptors.Count; i++) {
				var desc = _descriptors[i];
				if (string.Equals (desc.Name, AsfPicture.AttributeName, StringComparison.OrdinalIgnoreCase) &&
					desc.Type == AsfAttributeType.Binary) {
					var pic = AsfPicture.Parse (desc.RawValue.Span);
					if (pic is not null)
						pictures.Add (pic);
				}
			}
			return [.. pictures];
		}
		set {
			// Remove existing pictures
			for (int i = _descriptors.Count - 1; i >= 0; i--) {
				if (string.Equals (_descriptors[i].Name, AsfPicture.AttributeName, StringComparison.OrdinalIgnoreCase))
					_descriptors.RemoveAt (i);
			}

			// Add new pictures
			if (value is not null) {
				foreach (var pic in value) {
					var asfPic = AsfPicture.FromPicture (pic);
					_descriptors.Add (AsfDescriptor.CreateBinary (AsfPicture.AttributeName, asfPic.Render ()));
				}
			}
		}
	}
#pragma warning restore CA1819

	// ═══════════════════════════════════════════════════════════════
	// Tag Interface Implementation
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override bool IsEmpty =>
		string.IsNullOrEmpty (Title) &&
		string.IsNullOrEmpty (Artist) &&
		string.IsNullOrEmpty (Album) &&
		string.IsNullOrEmpty (Year) &&
		string.IsNullOrEmpty (Comment) &&
		string.IsNullOrEmpty (Genre) &&
		Track is null &&
		_descriptors.Count == 0;

	/// <inheritdoc/>
	/// <remarks>
	/// Also removes the file-level records of the Metadata and Metadata Library objects, which hold the extra
	/// values of multi-valued attributes. Records about one stream stay.
	/// </remarks>
	public override void Clear ()
	{
		_contentDescription = new AsfContentDescription ("", "", "", "", "");
		_descriptors.Clear ();
		DropExtraValues (null);
	}

	/// <inheritdoc/>
	public override BinaryData Render ()
	{
		// This renders just the extended content description
		// The Content Description is rendered separately when needed
		var extended = new AsfExtendedContentDescription (_descriptors);
		return extended.Render ();
	}

	/// <summary>
	/// Renders the Content Description object.
	/// </summary>
	public BinaryData RenderContentDescription () => _contentDescription.Render ();

	/// <summary>
	/// Gets the Content Description object.
	/// </summary>
	public AsfContentDescription ContentDescription => _contentDescription;

	/// <summary>
	/// Gets the Extended Content Description object.
	/// </summary>
	public AsfExtendedContentDescription ExtendedContentDescription => new (_descriptors);

	// ═══════════════════════════════════════════════════════════════
	// Custom Fields
	// ═══════════════════════════════════════════════════════════════

	// ASF spec rev. 01.20.03 §3.10 and §3.11: names and values carry 16-bit byte lengths, and
	// strings are UTF-16LE with a two-byte terminator.
	const int MaxLengthField = ushort.MaxValue;

	/// <inheritdoc/>
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Reads every Extended Content Description attribute named <paramref name="name"/>, compared
	/// case-insensitively as the mapped properties compare them (ASF spec rev. 01.20.03 §3.11). A string
	/// attribute is its text. A WORD, DWORD, or QWORD attribute is its decimal value. BOOL and binary
	/// attributes have no text and are skipped.
	/// </para>
	/// <para>
	/// <c>Title</c>, <c>Author</c>, <c>Copyright</c>, <c>Description</c>, and <c>Rating</c> are the Content
	/// Description Object's fields (§3.10), the same ones the Windows Media Format SDK exposes under those
	/// attribute names.
	/// </para>
	/// <para>
	/// The file-level records with that name in the Metadata Object and the Metadata Library Object (§4.7 and
	/// §4.8, inside the Header Extension Object §3.4) follow, in any language. Windows writes the first value of
	/// a multi-valued attribute to the Content Description or Extended Content Description and the rest there
	/// (https://mutagen-specs.readthedocs.io/en/latest/asf/index.html, "Multiple Values").
	/// </para>
	/// </remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsCustomFieldName (name))
			return CustomFieldValues.None;

		var values = new List<string> ();
		if (TryGetContentDescriptionField (name, out var fixedValue)) {
			if (!string.IsNullOrEmpty (fixedValue))
				values.Add (fixedValue);
			AddExtraValues (name, values);
			return values;
		}

		foreach (var desc in _descriptors) {
			if (!string.Equals (desc.Name, name, StringComparison.OrdinalIgnoreCase))
				continue;
			var text = desc.Type switch {
				AsfAttributeType.UnicodeString => desc.StringValue,
				AsfAttributeType.Word => desc.WordValue?.ToString (CultureInfo.InvariantCulture),
				AsfAttributeType.Dword => desc.DwordValue?.ToString (CultureInfo.InvariantCulture),
				AsfAttributeType.Qword => desc.QwordValue?.ToString (CultureInfo.InvariantCulture),
				_ => null
			};
			if (!string.IsNullOrEmpty (text))
				values.Add (text!);
		}
		AddExtraValues (name, values);
		return values;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Writes the first value as a Unicode string attribute in the Extended Content Description Object (ASF spec
	/// rev. 01.20.03 §3.11), at the position of the first attribute it replaces, or in the Content Description
	/// field (§3.10) for its five names. The other values become file-level records of the Metadata Library
	/// Object (§4.8), the layout Windows writes; a Header Extension Object (§3.4) is added when the file has
	/// none. Every earlier value of the name is removed from all four places.
	/// </para>
	/// <para>
	/// Returns false when the Metadata or Metadata Library Object in the file could not be parsed, because a
	/// value stored there would survive the write.
	/// </para>
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsCustomFieldName (name) || !CustomFieldValues.TryNormalize (values, out var kept))
			return false;
		foreach (var value in kept) {
			if (Utf16Length (value) > MaxLengthField)
				return false;
		}

		if (_extensionUnreadable || ExtraRecordCount () + kept.Count > ushort.MaxValue)
			return false;

		if (TryGetContentDescriptionField (name, out _)) {
			ReplaceContentDescriptionField (name, kept.Count == 0 ? "" : kept[0]);
		} else {
			var at = _descriptors.FindIndex (d => string.Equals (d.Name, name, StringComparison.OrdinalIgnoreCase));
			RemoveDescriptor (name);
			if (kept.Count > 0)
				_descriptors.Insert (at < 0 ? _descriptors.Count : at, AsfDescriptor.CreateString (name, kept[0]));
		}

		if (kept.Count > 1) {
			_library ??= [];
			for (var i = 1; i < kept.Count; i++)
				_library.Add (AsfMetadataRecord.CreateString (name, kept[i]));
			HeaderExtensionChanged = true;
		}
		return true;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Removes the name from the Content Description or Extended Content Description and its file-level
	/// records from the Metadata and Metadata Library objects. Returns false, and changes nothing, when one of
	/// those objects could not be parsed.
	/// </remarks>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsCustomFieldName (name) || _extensionUnreadable)
			return false;

		if (TryGetContentDescriptionField (name, out var fixedValue)) {
			var dropped = DropExtraValues (name);
			SetContentDescriptionField (name, "");
			return !string.IsNullOrEmpty (fixedValue) || dropped;
		}

		var before = _descriptors.Count;
		var droppedExtra = DropExtraValues (name);
		RemoveDescriptorOnly (name);
		return _descriptors.Count != before || droppedExtra;
	}

	// ═══════════════════════════════════════════════════════════════
	// Metadata Object and Metadata Library Object (Header Extension)
	// ═══════════════════════════════════════════════════════════════

	List<AsfMetadataRecord>? _metadata;
	List<AsfMetadataRecord>? _library;
	bool _extensionUnreadable;

	/// <summary>
	/// Records of the Metadata Object (§4.7), or null when the file has none or it could not be parsed.
	/// </summary>
	internal IReadOnlyList<AsfMetadataRecord>? MetadataRecords => _metadata;

	/// <summary>
	/// Records of the Metadata Library Object (§4.8), or null when the file has none or it could not be parsed.
	/// </summary>
	internal IReadOnlyList<AsfMetadataRecord>? LibraryRecords => _library;

	/// <summary>
	/// True after a write changed the records, so the Header Extension Object must be rebuilt on save.
	/// An unchanged tag keeps the object's bytes.
	/// </summary>
	internal bool HeaderExtensionChanged { get; private set; }

	/// <summary>
	/// Attaches the records read from the file's Header Extension Object.
	/// </summary>
	/// <param name="metadata">Metadata Object records, or null when absent.</param>
	/// <param name="library">Metadata Library Object records, or null when absent.</param>
	/// <param name="unreadable">True when one of the objects was present but could not be parsed.</param>
	internal void AttachHeaderExtension (List<AsfMetadataRecord>? metadata, List<AsfMetadataRecord>? library, bool unreadable)
	{
		_metadata = unreadable ? null : metadata;
		_library = unreadable ? null : library;
		_extensionUnreadable = unreadable;
		HeaderExtensionChanged = false;
	}

	int ExtraRecordCount () => (_library?.Count ?? 0);

	void AddExtraValues (string name, List<string> values)
	{
		foreach (var records in new[] { _metadata, _library }) {
			if (records is null)
				continue;
			foreach (var record in records) {
				if (!record.IsFileLevel || !string.Equals (record.Name, name, StringComparison.OrdinalIgnoreCase))
					continue;
				var text = record.Text;
				if (!string.IsNullOrEmpty (text))
					values.Add (text!);
			}
		}
	}

	/// <summary>
	/// Removes the file-level records named <paramref name="name"/>, or every file-level record when it is null.
	/// </summary>
	bool DropExtraValues (string? name)
	{
		var removed = 0;
		foreach (var records in new[] { _metadata, _library }) {
			removed += records?.RemoveAll (r => r.IsFileLevel &&
				(name is null || string.Equals (r.Name, name, StringComparison.OrdinalIgnoreCase))) ?? 0;
		}
		if (removed > 0)
			HeaderExtensionChanged = true;
		return removed > 0;
	}

	/// <summary>
	/// Sets a Content Description field and removes the extra values stored under its name, so the name holds
	/// one value afterwards.
	/// </summary>
	void ReplaceContentDescriptionField (string name, string value)
	{
		DropExtraValues (name);
		SetContentDescriptionField (name, value);
	}

	/// <summary>
	/// A name that fits its 16-bit length field and is not <c>WM/Picture</c>, which <see cref="Pictures"/> owns.
	/// </summary>
	static bool IsCustomFieldName (string name) =>
		CustomFieldValues.IsUsableName (name) &&
		Utf16Length (name) <= MaxLengthField &&
		!string.Equals (name, AsfPicture.AttributeName, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Bytes of UTF-16LE text plus its two-byte terminator.
	/// </summary>
	static long Utf16Length (string text) => ((long)text.Length + 1) * 2;

	bool TryGetContentDescriptionField (string name, out string value)
	{
		if (string.Equals (name, "Title", StringComparison.OrdinalIgnoreCase))
			value = _contentDescription.Title;
		else if (string.Equals (name, "Author", StringComparison.OrdinalIgnoreCase))
			value = _contentDescription.Author;
		else if (string.Equals (name, "Copyright", StringComparison.OrdinalIgnoreCase))
			value = _contentDescription.Copyright;
		else if (string.Equals (name, "Description", StringComparison.OrdinalIgnoreCase))
			value = _contentDescription.Description;
		else if (string.Equals (name, "Rating", StringComparison.OrdinalIgnoreCase))
			value = _contentDescription.Rating;
		else {
			value = "";
			return false;
		}
		return true;
	}

	void SetContentDescriptionField (string name, string value)
	{
		var cd = _contentDescription;
		_contentDescription = name.ToUpperInvariant () switch {
			"TITLE" => new AsfContentDescription (value, cd.Author, cd.Copyright, cd.Description, cd.Rating),
			"AUTHOR" => new AsfContentDescription (cd.Title, value, cd.Copyright, cd.Description, cd.Rating),
			"COPYRIGHT" => new AsfContentDescription (cd.Title, cd.Author, value, cd.Description, cd.Rating),
			"DESCRIPTION" => new AsfContentDescription (cd.Title, cd.Author, cd.Copyright, value, cd.Rating),
			_ => new AsfContentDescription (cd.Title, cd.Author, cd.Copyright, cd.Description, value)
		};
	}

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	AsfDescriptor? GetDescriptor (string name)
	{
		for (int i = 0; i < _descriptors.Count; i++) {
			if (string.Equals (_descriptors[i].Name, name, StringComparison.OrdinalIgnoreCase))
				return _descriptors[i];
		}
		return null;
	}

	string? GetString (string name) => GetDescriptor (name)?.StringValue;

	void SetString (string name, string? value)
	{
		RemoveDescriptor (name);
		if (!string.IsNullOrEmpty (value))
			_descriptors.Add (AsfDescriptor.CreateString (name, value!));
	}

	void SetDword (string name, uint value)
	{
		RemoveDescriptor (name);
		_descriptors.Add (AsfDescriptor.CreateDword (name, value));
	}

	void SetBool (string name, bool value)
	{
		RemoveDescriptor (name);
		_descriptors.Add (AsfDescriptor.CreateBool (name, value));
	}

	/// <summary>
	/// Removes every value of an attribute: its Extended Content Description descriptors and its file-level
	/// Metadata and Metadata Library records. Every mapped setter goes through here.
	/// </summary>
	void RemoveDescriptor (string name)
	{
		DropExtraValues (name);
		RemoveDescriptorOnly (name);
	}

	void RemoveDescriptorOnly (string name)
	{
		for (int i = _descriptors.Count - 1; i >= 0; i--) {
			if (string.Equals (_descriptors[i].Name, name, StringComparison.OrdinalIgnoreCase))
				_descriptors.RemoveAt (i);
		}
	}

	(uint? disc, uint? count) ParsePartOfSet ()
	{
		var value = GetString ("WM/PartOfSet");
		if (string.IsNullOrEmpty (value))
			return (null, null);

		var parts = value!.Split ('/');
		uint? disc = null;
		uint? count = null;

		if (parts.Length >= 1 && uint.TryParse (parts[0].Trim (), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
			disc = d;
		if (parts.Length >= 2 && uint.TryParse (parts[1].Trim (), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
			count = c;

		return (disc, count);
	}

	void SetPartOfSet (uint? disc, uint? count)
	{
		if (!disc.HasValue && !count.HasValue) {
			RemoveDescriptor ("WM/PartOfSet");
			return;
		}

		string value;
		if (count.HasValue)
			value = $"{disc?.ToString (CultureInfo.InvariantCulture) ?? ""}/{count.Value}";
		else
			value = disc?.ToString (CultureInfo.InvariantCulture) ?? "";

		SetString ("WM/PartOfSet", value);
	}
}
