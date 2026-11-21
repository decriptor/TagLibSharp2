// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents a Vorbis Comment metadata block used by FLAC and Ogg Vorbis files.
/// </summary>
/// <remarks>
/// <para>
/// Vorbis Comments provide a flexible key-value metadata format with these features:
/// </para>
/// <list type="bullet">
/// <item>Case-insensitive field names (stored uppercase by convention)</item>
/// <item>UTF-8 encoded values</item>
/// <item>Multiple values per field name (e.g., multiple ARTIST entries)</item>
/// <item>Custom field support</item>
/// </list>
/// <para>
/// Standard field mappings:
/// </para>
/// <list type="bullet">
/// <item>Title → TITLE</item>
/// <item>Artist → ARTIST</item>
/// <item>Album → ALBUM</item>
/// <item>Year → DATE</item>
/// <item>Genre → GENRE</item>
/// <item>Track → TRACKNUMBER</item>
/// <item>Comment → COMMENT</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/vorbis/doc/v-comment.html
/// </para>
/// </remarks>
public sealed class VorbisComment : Tag
{
	const string MetadataBlockPictureField = "METADATA_BLOCK_PICTURE";
	readonly List<VorbisCommentField> _fields = new (16);
	readonly List<FlacPicture> _pictures = new (2);

	/// <summary>
	/// Gets or sets the vendor string identifying the encoder/tagger.
	/// </summary>
	public string VendorString { get; set; }

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Xiph;

	/// <summary>
	/// Gets the list of all fields in this comment block.
	/// </summary>
	public IReadOnlyList<VorbisCommentField> Fields => _fields;

	/// <summary>
	/// Gets the list of embedded pictures as FlacPicture objects.
	/// </summary>
	/// <remarks>
	/// For Ogg Vorbis, pictures are stored as base64-encoded METADATA_BLOCK_PICTURE fields.
	/// For FLAC, pictures are stored separately in PICTURE metadata blocks (use FlacFile.Pictures instead).
	/// </remarks>
	public IReadOnlyList<FlacPicture> PictureBlocks => _pictures;

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override IPicture[] Pictures {
		get => [.. _pictures];
		set {
			_pictures.Clear ();
			if (value is not null) {
				foreach (var pic in value) {
					if (pic is FlacPicture flacPic)
						_pictures.Add (flacPic);
					else
						_pictures.Add (new FlacPicture (pic.MimeType, pic.PictureType, pic.Description, pic.PictureData, 0, 0, 0, 0));
				}
			}
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override string? Title {
		get => GetValue ("TITLE");
		set => SetValueProperty ("TITLE", value, Title);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetValue ("ARTIST");
		set => SetValueProperty ("ARTIST", value, Artist);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the ARTIST field. Multiple values are stored as separate fields.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Performers {
		get {
			var values = GetValues ("ARTIST");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			RemoveAll ("ARTIST");
			if (value is null || value.Length == 0)
				return;
			for (var i = 0; i < value.Length; i++)
				AddField ("ARTIST", value[i]);
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override string? Album {
		get => GetValue ("ALBUM");
		set => SetValueProperty ("ALBUM", value, Album);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => GetValue ("DATE");
		set => SetValueProperty ("DATE", value, Year);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses ORIGINALDATE field. Falls back to ORIGINALYEAR if ORIGINALDATE is missing or empty.
	/// Writing back the value already read changes nothing. A new value is written to ORIGINALDATE,
	/// and an existing ORIGINALYEAR is rewritten to its four-digit year (MusicBrainz Picard keeps
	/// both: https://picard-docs.musicbrainz.org/en/v3.0/appendices/tag_mapping.html).
	/// Clearing removes both.
	/// </remarks>
	public override string? OriginalReleaseDate {
		get => GetValueOrFallback ("ORIGINALDATE", "ORIGINALYEAR");
		set => SetValueWithFallback ("ORIGINALDATE", value, "ORIGINALYEAR", LeadingYear);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetValue ("GENRE");
		set => SetValueProperty ("GENRE", value, Genre);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the GENRE field. Multiple values are stored as separate fields.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Genres {
		get {
			var values = GetValues ("GENRE");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			RemoveAll ("GENRE");
			if (value is null || value.Length == 0)
				return;
			for (var i = 0; i < value.Length; i++)
				AddField ("GENRE", value[i]);
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	/// <remarks>
	/// When <c>TRACKNUMBER</c> is <c>n/m</c> and neither <c>TOTALTRACKS</c> nor <c>TRACKTOTAL</c> is numeric,
	/// the total is kept in <c>TOTALTRACKS</c>. The stored track number is the number part only.
	/// </remarks>
	public override uint? Track {
		get => ParseLeadingNumber (GetValue ("TRACKNUMBER"));
		set {
			PreserveEmbeddedTotal ("TRACKNUMBER", "TOTALTRACKS", "TRACKTOTAL");
			SetValue ("TRACKNUMBER", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetValue ("COMMENT");
		set => SetValueProperty ("COMMENT", value, Comment);
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetValue ("ALBUMARTIST");
		set => SetValueProperty ("ALBUMARTIST", value, AlbumArtist);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the ALBUMARTIST field. Multiple values are stored as separate fields.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] AlbumArtists {
		get {
			var values = GetValues ("ALBUMARTIST");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			RemoveAll ("ALBUMARTIST");
			if (value is null || value.Length == 0)
				return;
			for (var i = 0; i < value.Length; i++)
				AddField ("ALBUMARTIST", value[i]);
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	/// <remarks>
	/// When <c>DISCNUMBER</c> is <c>n/m</c> and neither <c>TOTALDISCS</c> nor <c>DISCTOTAL</c> is numeric,
	/// the total is kept in <c>TOTALDISCS</c>. The stored disc number is the number part only.
	/// </remarks>
	public override uint? DiscNumber {
		get => ParseLeadingNumber (GetValue ("DISCNUMBER"));
		set {
			PreserveEmbeddedTotal ("DISCNUMBER", "TOTALDISCS", "DISCTOTAL");
			SetValue ("DISCNUMBER", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => GetValue ("COMPOSER");
		set => SetValueProperty ("COMPOSER", value, Composer);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the COMPOSER field. Multiple values are stored as separate fields.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Composers {
		get {
			var values = GetValues ("COMPOSER");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			RemoveAll ("COMPOSER");
			if (value is null || value.Length == 0)
				return;
			for (var i = 0; i < value.Length; i++)
				AddField ("COMPOSER", value[i]);
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var value = GetValue ("BPM");
			if (string.IsNullOrEmpty (value))
				return null;
			return TryParseUInt (value, out var bpm) ? bpm : null;
		}
		set => SetValue ("BPM", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetValue ("CONDUCTOR");
		set => SetValueProperty ("CONDUCTOR", value, Conductor);
	}

	/// <inheritdoc/>
	public override string? Work {
		get => GetValue ("WORK");
		set => SetValueProperty ("WORK", value, Work);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the MOVEMENT field. Falls back to MOVEMENTNAME when MOVEMENT is missing or empty.
	/// Writing back the value already read changes nothing, so a MOVEMENTNAME that Picard or Mp3tag
	/// wrote beside a MOVEMENT number is kept. A new value is written to MOVEMENT and to an existing
	/// MOVEMENTNAME. Clearing removes both.
	/// </remarks>
	public override string? Movement {
		get => GetValueOrFallback ("MOVEMENT", "MOVEMENTNAME");
		set => SetValueWithFallback ("MOVEMENT", value, "MOVEMENTNAME", static v => v);
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get {
			var value = GetValue ("MOVEMENTNUMBER");
			return TryParseUInt (value, out var num) ? num : null;
		}
		set => SetValue ("MOVEMENTNUMBER", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Reads the first numeric MOVEMENTTOTAL, then MOVEMENTCOUNT. An empty or non-numeric field is skipped.
	/// Writes MOVEMENTTOTAL and removes MOVEMENTCOUNT.
	/// </remarks>
	public override uint? MovementTotal {
		get => ParseUIntField ("MOVEMENTTOTAL") ?? ParseUIntField ("MOVEMENTCOUNT");
		set {
			SetValue ("MOVEMENTTOTAL", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
			RemoveAll ("MOVEMENTCOUNT");
		}
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => GetValue ("COPYRIGHT");
		set => SetValueProperty ("COPYRIGHT", value, Copyright);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => GetValue ("COMPILATION") == "1";
		set {
			if (value)
				SetValue ("COMPILATION", "1");
			else
				SetValue ("COMPILATION", null);
		}
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetValue ("ISRC");
		set => SetValueProperty ("ISRC", value, Isrc);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetValue ("LABEL");
		set => SetValueProperty ("LABEL", value, Publisher);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => GetValue ("ENCODED-BY");
		set => SetValueProperty ("ENCODED-BY", value, EncodedBy);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => GetValue ("ENCODER");
		set => SetValueProperty ("ENCODER", value, EncoderSettings);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => GetValue ("GROUPING");
		set => SetValueProperty ("GROUPING", value, Grouping);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => GetValue ("SUBTITLE");
		set => SetValueProperty ("SUBTITLE", value, Subtitle);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => GetValue ("REMIXER");
		set => SetValueProperty ("REMIXER", value, Remixer);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => GetValue ("KEY");
		set => SetValueProperty ("KEY", value, InitialKey);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => GetValue ("MOOD");
		set => SetValueProperty ("MOOD", value, Mood);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => GetValue ("MEDIA");
		set => SetValueProperty ("MEDIA", value, MediaType);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => GetValue ("LANGUAGE");
		set => SetValueProperty ("LANGUAGE", value, Language);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => GetValue ("BARCODE");
		set => SetValueProperty ("BARCODE", value, Barcode);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => GetValue ("CATALOGNUMBER");
		set => SetValueProperty ("CATALOGNUMBER", value, CatalogNumber);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetValue ("ALBUMSORT");
		set => SetValueProperty ("ALBUMSORT", value, AlbumSort);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetValue ("ARTISTSORT");
		set => SetValueProperty ("ARTISTSORT", value, ArtistSort);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetValue ("TITLESORT");
		set => SetValueProperty ("TITLESORT", value, TitleSort);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetValue ("ALBUMARTISTSORT");
		set => SetValueProperty ("ALBUMARTISTSORT", value, AlbumArtistSort);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the ARTISTSORT field. Multiple values are stored as separate fields
	/// per Vorbis Comment specification.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[]? PerformersSort {
		get {
			var values = GetValues ("ARTISTSORT");
			return values.Count > 0 ? [.. values] : null;
		}
		set {
			RemoveAll ("ARTISTSORT");
			if (value is null || value.Length == 0)
				return;

			for (var i = 0; i < value.Length; i++)
				AddField ("ARTISTSORT", value[i]);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the ALBUMARTISTSORT field. Multiple values are stored as separate fields
	/// per Vorbis Comment specification.
	/// </remarks>
	public override string[]? AlbumArtistsSort {
		get {
			var values = GetValues ("ALBUMARTISTSORT");
			return values.Count > 0 ? [.. values] : null;
		}
		set {
			RemoveAll ("ALBUMARTISTSORT");
			if (value is null || value.Length == 0)
				return;

			for (var i = 0; i < value.Length; i++)
				AddField ("ALBUMARTISTSORT", value[i]);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the COMPOSERSORT field. Multiple values are stored as separate fields
	/// per Vorbis Comment specification.
	/// </remarks>
	public override string[]? ComposersSort {
		get {
			var values = GetValues ("COMPOSERSORT");
			return values.Count > 0 ? [.. values] : null;
		}
		set {
			RemoveAll ("COMPOSERSORT");
			if (value is null || value.Length == 0)
				return;

			for (var i = 0; i < value.Length; i++)
				AddField ("COMPOSERSORT", value[i]);
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetValue ("LYRICS");
		set => SetValueProperty ("LYRICS", value, Lyrics);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetValue ("REPLAYGAIN_TRACK_GAIN");
		set => SetValueProperty ("REPLAYGAIN_TRACK_GAIN", value, ReplayGainTrackGain);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetValue ("REPLAYGAIN_TRACK_PEAK");
		set => SetValueProperty ("REPLAYGAIN_TRACK_PEAK", value, ReplayGainTrackPeak);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetValue ("REPLAYGAIN_ALBUM_GAIN");
		set => SetValueProperty ("REPLAYGAIN_ALBUM_GAIN", value, ReplayGainAlbumGain);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetValue ("REPLAYGAIN_ALBUM_PEAK");
		set => SetValueProperty ("REPLAYGAIN_ALBUM_PEAK", value, ReplayGainAlbumPeak);
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => GetValue ("R128_TRACK_GAIN");
		set => SetValueProperty ("R128_TRACK_GAIN", value, R128TrackGain);
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => GetValue ("R128_ALBUM_GAIN");
		set => SetValueProperty ("R128_ALBUM_GAIN", value, R128AlbumGain);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetValue ("MUSICBRAINZ_TRACKID");
		set => SetValueProperty ("MUSICBRAINZ_TRACKID", value, MusicBrainzTrackId);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the MUSICBRAINZ_TRACKID field. This is the same field as MusicBrainzTrackId
	/// because MusicBrainz historically used "Track ID" for what is now called "Recording ID".
	/// </remarks>
	public override string? MusicBrainzRecordingId {
		get => GetValue ("MUSICBRAINZ_TRACKID");
		set => SetValueProperty ("MUSICBRAINZ_TRACKID", value, MusicBrainzRecordingId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetValue ("MUSICBRAINZ_ALBUMID");
		set => SetValueProperty ("MUSICBRAINZ_ALBUMID", value, MusicBrainzReleaseId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetValue ("MUSICBRAINZ_ARTISTID");
		set => SetValueProperty ("MUSICBRAINZ_ARTISTID", value, MusicBrainzArtistId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetValue ("MUSICBRAINZ_RELEASEGROUPID");
		set => SetValueProperty ("MUSICBRAINZ_RELEASEGROUPID", value, MusicBrainzReleaseGroupId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetValue ("MUSICBRAINZ_ALBUMARTISTID");
		set => SetValueProperty ("MUSICBRAINZ_ALBUMARTISTID", value, MusicBrainzAlbumArtistId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => GetValue ("MUSICBRAINZ_WORKID");
		set => SetValueProperty ("MUSICBRAINZ_WORKID", value, MusicBrainzWorkId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => GetValue ("MUSICBRAINZ_DISCID");
		set => SetValueProperty ("MUSICBRAINZ_DISCID", value, MusicBrainzDiscId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => GetValue ("RELEASESTATUS");
		set => SetValueProperty ("RELEASESTATUS", value, MusicBrainzReleaseStatus);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => GetValue ("RELEASETYPE");
		set => SetValueProperty ("RELEASETYPE", value, MusicBrainzReleaseType);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => GetValue ("RELEASECOUNTRY");
		set => SetValueProperty ("RELEASECOUNTRY", value, MusicBrainzReleaseCountry);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => GetValue ("COMPOSERSORT");
		set => SetValueProperty ("COMPOSERSORT", value, ComposerSort);
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => GetValue ("DATETAGGED");
		set => SetValueProperty ("DATETAGGED", value, DateTagged);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => GetValue ("DESCRIPTION");
		set => SetValueProperty ("DESCRIPTION", value, Description);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => GetValue ("ASIN");
		set => SetValueProperty ("ASIN", value, AmazonId);
	}

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => GetValue ("ACOUSTID_ID");
		set => SetValueProperty ("ACOUSTID_ID", value, AcoustIdId);
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => GetValue ("ACOUSTID_FINGERPRINT");
		set => SetValueProperty ("ACOUSTID_FINGERPRINT", value, AcoustIdFingerprint);
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => GetValue ("PODCASTFEEDURL");
		set => SetValueProperty ("PODCASTFEEDURL", value, PodcastFeedUrl);
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[]? PerformersRole {
		get {
			var values = GetValues ("PERFORMER_ROLE");
			return values.Count > 0 ? values.ToArray () : null;
		}
		set {
			RemoveAll ("PERFORMER_ROLE");
			if (value is null || value.Length == 0)
				return;

			for (var i = 0; i < value.Length; i++)
				AddField ("PERFORMER_ROLE", value[i]);
		}
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the total number of tracks on the album.
	/// </summary>
	/// <remarks>
	/// Reads the first numeric TOTALTRACKS, then TRACKTOTAL, then a TRACKNUMBER value of the form "5/12".
	/// An empty or non-numeric field is skipped. Writes TOTALTRACKS and removes TRACKTOTAL.
	/// Setting the value canonicalizes the stored total to TOTALTRACKS.
	/// Each TRACKNUMBER of the form n/m is rewritten in place to n so the slash total is not read back.
	/// A value that is not n/m is left unchanged.
	/// </remarks>
	public override uint? TotalTracks {
		get {
			// TOTALTRACKS is what this library writes. TRACKTOTAL is what rippers write.
			// The slash form is last so TRACKTOTAL stays preferred over n/m.
			return ParseUIntField ("TOTALTRACKS")
				?? ParseUIntField ("TRACKTOTAL")
				?? ParseEmbeddedTotal (GetValue ("TRACKNUMBER"));
		}
		set {
			// Setting the value canonicalizes the stored total to TOTALTRACKS.
			SetValue ("TOTALTRACKS", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
			RemoveAll ("TRACKTOTAL");
			StripEmbeddedTotal ("TRACKNUMBER");
		}
	}

	/// <summary>
	/// Gets or sets the total number of discs.
	/// </summary>
	/// <remarks>
	/// Reads the first numeric TOTALDISCS, then DISCTOTAL, then a DISCNUMBER value of the form "2/3".
	/// An empty or non-numeric field is skipped. Writes TOTALDISCS and removes DISCTOTAL.
	/// Setting the value canonicalizes the stored total to TOTALDISCS.
	/// Each DISCNUMBER of the form n/m is rewritten in place to n so the slash total is not read back.
	/// A value that is not n/m is left unchanged.
	/// </remarks>
	public override uint? TotalDiscs {
		get {
			// TOTALDISCS is what this library writes. DISCTOTAL is the alias rippers write.
			// The slash form is last so DISCTOTAL stays preferred over n/m.
			return ParseUIntField ("TOTALDISCS")
				?? ParseUIntField ("DISCTOTAL")
				?? ParseEmbeddedTotal (GetValue ("DISCNUMBER"));
		}
		set {
			// Setting the value canonicalizes the stored total to TOTALDISCS.
			SetValue ("TOTALDISCS", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
			RemoveAll ("DISCTOTAL");
			StripEmbeddedTotal ("DISCNUMBER");
		}
	}

	/// <inheritdoc/>
	public override string? DiscSubtitle {
		get => GetValue ("DISCSUBTITLE");
		set => SetValueProperty ("DISCSUBTITLE", value, DiscSubtitle);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="VorbisComment"/> class with an empty vendor string.
	/// </summary>
	public VorbisComment () : this ("")
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="VorbisComment"/> class.
	/// </summary>
	/// <param name="vendorString">The vendor string identifying the encoder.</param>
	public VorbisComment (string vendorString)
	{
		VendorString = vendorString ?? "";
	}

	/// <summary>
	/// Adds a field to the comment block.
	/// </summary>
	/// <param name="name">The field name (will be uppercased).</param>
	/// <param name="value">The field value.</param>
	/// <remarks>
	/// This method allows adding multiple fields with the same name,
	/// which is valid in Vorbis Comments (e.g., multiple ARTIST fields).
	/// </remarks>
	public void AddField (string name, string value)
	{
		_fields.Add (new VorbisCommentField (name, value));
	}

	/// <summary>
	/// Gets the first value for a field name.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <returns>The first value, or null if not found.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public string? GetValue (string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
		var upperName = name.ToUpperInvariant ();
		// Use explicit check instead of relying on struct default behavior.
		// When no match is found, FirstOrDefault returns default(VorbisCommentField)
		// which has null properties - but we make this explicit for clarity.
		for (var i = 0; i < _fields.Count; i++) {
			if (_fields[i].Name == upperName)
				return _fields[i].Value;
		}
		return null;
	}

	/// <summary>
	/// Reads the first unsigned integer among a field's values.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <returns>The first parsed value, or null when none of the values are an unsigned integer.</returns>
	uint? ParseUIntField (string name)
	{
		var values = GetValues (name);
		for (var i = 0; i < values.Count; i++) {
			if (TryParseUInt (values[i], out var parsed))
				return parsed;
		}

		return null;
	}

	/// <summary>
	/// Parses an invariant unsigned integer. Empty and non-numeric text is rejected.
	/// </summary>
	static bool TryParseUInt (string? value, out uint parsed)
	{
		if (string.IsNullOrEmpty (value)) {
			parsed = 0;
			return false;
		}

		return uint.TryParse (
			value,
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out parsed);
	}

	/// <summary>
	/// Reads the number before a slash, or the whole value when there is no slash.
	/// </summary>
	static uint? ParseLeadingNumber (string? value)
	{
		if (value is null || value.Length == 0)
			return null;

		var slashIndex = IndexOfSlash (value);
		if (slashIndex > 0)
			value = value.Substring (0, slashIndex);

		return TryParseUInt (value, out var number) ? number : null;
	}

	/// <summary>
	/// Reads the total from a value of the form n/m. The slash is required.
	/// </summary>
	static uint? ParseEmbeddedTotal (string? value)
	{
		if (value is null || value.Length == 0)
			return null;

		var slashIndex = IndexOfSlash (value);
		if (slashIndex <= 0 || slashIndex >= value.Length - 1)
			return null;

		return TryParseUInt (value.Substring (slashIndex + 1), out var total) ? total : null;
	}

	/// <summary>
	/// Copies an n/m total onto the canonical field when neither that field nor its alias already parses.
	/// </summary>
	void PreserveEmbeddedTotal (string numberField, string canonicalField, string aliasField)
	{
		if (ParseUIntField (canonicalField) is not null || ParseUIntField (aliasField) is not null)
			return;

		var total = ParseEmbeddedTotal (GetValue (numberField));
		if (total is not uint preserved)
			return;

		SetValue (canonicalField, preserved.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Rewrites each repeated <paramref name="numberField"/> value in place and does not drop the other values.
	/// An n/m value whose total parses becomes n. A value that is not n/m stays, and every other field stays,
	/// so a later read cannot resurrect the slash total.
	/// </summary>
	/// <param name="numberField">The field whose repeated values may embed a total, such as TRACKNUMBER.</param>
	void StripEmbeddedTotal (string numberField)
	{
		var upperName = numberField.ToUpperInvariant ();
		for (var i = 0; i < _fields.Count; i++) {
			var field = _fields[i];
			if (field.Name != upperName)
				continue;

			var value = field.Value;
			if (ParseEmbeddedTotal (value) is null)
				continue;

			var slashIndex = IndexOfSlash (value);
			if (slashIndex <= 0)
				continue;

			_fields[i] = new VorbisCommentField (field.Name, value.Substring (0, slashIndex));
		}
	}

	static int IndexOfSlash (string value)
	{
#if NETSTANDARD2_0
		return value.IndexOf ('/');
#else
		return value.IndexOf ('/', StringComparison.Ordinal);
#endif
	}

	/// <summary>
	/// Gets all values for a field name.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <returns>A list of all values for the field.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public IReadOnlyList<string> GetValues (string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
		var upperName = name.ToUpperInvariant ();
		// Use for loop instead of LINQ for better performance
		var result = new List<string> ();
		for (var i = 0; i < _fields.Count; i++) {
			if (_fields[i].Name == upperName)
				result.Add (_fields[i].Value);
		}
		return result;
	}

	/// <summary>
	/// Sets a single value for a field, replacing all existing values.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <param name="value">The value, or null to remove all fields with this name.</param>
	public void SetValue (string name, string? value)
	{
		RemoveAll (name);
		if (value is not null)
			AddField (name, value);
	}

	/// <summary>
	/// Writes a single-value property. Writing back the value its getter returned is a no-op, so
	/// a field that appears more than once (https://xiph.org/vorbis/doc/v-comment.html allows
	/// repeated names) keeps every value. Any other value replaces them all; null removes them.
	/// </summary>
	void SetValueProperty (string name, string? value, string? current)
	{
		if (value is not null && string.Equals (value, current, StringComparison.Ordinal))
			return;
		SetValue (name, value);
	}

	/// <summary>
	/// The first value of <paramref name="name"/>, or of <paramref name="fallback"/> when the first is
	/// missing or empty. An empty field is a valid comment (https://xiph.org/vorbis/doc/v-comment.html),
	/// but it must not hide a filled fallback. Returns "" when the only fields present are empty.
	/// </summary>
	string? GetValueOrFallback (string name, string fallback)
	{
		var primary = GetValue (name);
		if (!string.IsNullOrEmpty (primary))
			return primary;

		var secondary = GetValue (fallback);
		return string.IsNullOrEmpty (secondary) ? primary ?? secondary : secondary;
	}

	/// <summary>
	/// Sets a field whose getter falls back to <paramref name="fallback"/>.
	/// The value the getter already returns is a no-op, so neither field is lost; that includes "",
	/// which the getter returns only when every field present is empty.
	/// Otherwise a null or empty value clears the fallback too, so it cannot come back.
	/// Any other value goes to <paramref name="name"/>, and to <paramref name="fallback"/>
	/// when that field exists and <paramref name="toFallback"/> gives a value for it.
	/// </summary>
	void SetValueWithFallback (string name, string? value, string fallback, Func<string, string?> toFallback)
	{
		var current = GetValueOrFallback (name, fallback);
		if (value is not null && string.Equals (current, value, StringComparison.Ordinal))
			return;

		if (string.IsNullOrEmpty (value)) {
			RemoveAll (fallback);
			SetValue (name, value);
			return;
		}

		SetValue (name, value);
		if (GetValue (fallback) is null)
			return;

		var fallbackValue = toFallback (value!);
		if (fallbackValue is not null)
			SetValue (fallback, fallbackValue);
	}

	/// <summary>
	/// The leading four-digit year of a date such as "1969-09-26", or null when it has none.
	/// </summary>
	static string? LeadingYear (string date)
	{
		if (date.Length < 4)
			return null;
		for (var i = 0; i < 4; i++) {
			if (date[i] < '0' || date[i] > '9')
				return null;
		}
		return date.Length == 4 || date[4] is < '0' or > '9' ? date.Substring (0, 4) : null;
	}

	/// <summary>
	/// Removes all fields with the specified name.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public void RemoveAll (string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
		var upperName = name.ToUpperInvariant ();
		_fields.RemoveAll (f => f.Name == upperName);
	}

	/// <inheritdoc/>
	public override void Clear ()
	{
		_fields.Clear ();
		_pictures.Clear ();
	}

	/// <summary>
	/// Adds a picture to this comment block.
	/// </summary>
	/// <param name="picture">The picture to add.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="picture"/> is null.</exception>
	/// <remarks>
	/// For Ogg Vorbis, pictures are stored as base64-encoded METADATA_BLOCK_PICTURE fields.
	/// When rendering, each picture will be encoded and added as a field.
	/// </remarks>
	public void AddPicture (FlacPicture picture)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (picture is null)
			throw new ArgumentNullException (nameof (picture));
#else
		ArgumentNullException.ThrowIfNull (picture);
#endif
		_pictures.Add (picture);
	}

	/// <summary>
	/// Removes all pictures of a specific type.
	/// </summary>
	/// <param name="pictureType">The picture type to remove.</param>
	public void RemovePictures (PictureType pictureType)
	{
		_pictures.RemoveAll (p => p.PictureType == pictureType);
	}

	/// <summary>
	/// Removes all pictures from this comment block.
	/// </summary>
	public void RemoveAllPictures ()
	{
		_pictures.Clear ();
	}

	/// <summary>
	/// Attempts to read a Vorbis Comment block from binary data.
	/// </summary>
	/// <param name="data">The binary data (little-endian format).</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <remarks>
	/// <para>
	/// Binary format:
	/// </para>
	/// <code>
	/// [vendor_len:4 LE][vendor:n UTF-8]
	/// [field_count:4 LE]
	/// For each field:
	///   [field_len:4 LE][FIELDNAME=value:n UTF-8]
	/// </code>
	/// </remarks>
	public static VorbisCommentReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < 8)
			return VorbisCommentReadResult.Failure ("Data too short for Vorbis Comment header");

		var offset = 0;

		// Vendor string length (4 bytes, little-endian)
		var vendorLen = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		// Overflow protection: check if length exceeds int.MaxValue
		if (vendorLen > int.MaxValue)
			return VorbisCommentReadResult.Failure ("Vendor string length overflow (exceeds maximum)");

		if (offset + (int)vendorLen > data.Length)
			return VorbisCommentReadResult.Failure ("Invalid vendor string length");

		var vendorString = System.Text.Encoding.UTF8.GetString (data.Slice (offset, (int)vendorLen));
		offset += (int)vendorLen;

		if (offset + 4 > data.Length)
			return VorbisCommentReadResult.Failure ("Data too short for field count");

		// Field count (4 bytes, little-endian)
		var fieldCount = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		var comment = new VorbisComment (vendorString);

		// Read each field
		for (var i = 0; i < fieldCount; i++) {
			if (offset + 4 > data.Length)
				return VorbisCommentReadResult.Failure ($"Data too short for field {i} length");

			var fieldLen = ReadUInt32LE (data.Slice (offset, 4));
			offset += 4;

			// Overflow protection: check if length exceeds int.MaxValue
			if (fieldLen > int.MaxValue)
				return VorbisCommentReadResult.Failure ($"Field {i} length overflow (exceeds maximum)");

			if (offset + (int)fieldLen > data.Length)
				return VorbisCommentReadResult.Failure ($"Invalid field {i} length");

			var fieldString = System.Text.Encoding.UTF8.GetString (data.Slice (offset, (int)fieldLen));
			offset += (int)fieldLen;

			var parseResult = VorbisCommentField.Parse (fieldString);
			if (parseResult.IsSuccess) {
				// Check for METADATA_BLOCK_PICTURE and parse as picture
				if (parseResult.Field.Name == MetadataBlockPictureField) {
					var picture = TryParseMetadataBlockPicture (parseResult.Field.Value);
					if (picture is not null) {
						// Successfully parsed - add to pictures only, not fields
						comment._pictures.Add (picture);
						continue;
					}
					// Failed to parse - preserve in fields so data isn't lost
				}
				comment._fields.Add (parseResult.Field);
			}
			// Skip malformed fields per spec - don't fail the entire block
		}

		return VorbisCommentReadResult.Success (comment, offset);
	}

	static FlacPicture? TryParseMetadataBlockPicture (string base64Value)
	{
		try {
			var pictureData = Convert.FromBase64String (base64Value);
			var result = FlacPicture.Read (pictureData);
			return result.IsSuccess ? result.Picture : null;
		} catch (FormatException) {
			// Invalid base64 - skip this picture
			return null;
		}
	}

	/// <inheritdoc/>
	public override BinaryData Render ()
	{
		// Encode vendor string
		var vendorBytes = System.Text.Encoding.UTF8.GetBytes (VendorString);

		// Single pass: filter fields, encode, and calculate size simultaneously
		// This avoids LINQ overhead and multiple intermediate allocations
		var fieldBytesList = new List<byte[]> (_fields.Count);
		var fieldBytesSize = 0;
		for (var i = 0; i < _fields.Count; i++) {
			var field = _fields[i];
			if (field.Name == MetadataBlockPictureField)
				continue; // Skip - we'll regenerate from _pictures
			var fieldBytes = System.Text.Encoding.UTF8.GetBytes (field.ToString ());
			fieldBytesList.Add (fieldBytes);
			fieldBytesSize += 4 + fieldBytes.Length;
		}

		// Generate METADATA_BLOCK_PICTURE fields for pictures
		var pictureFieldBytesList = new List<byte[]> (_pictures.Count);
		var pictureBytesSize = 0;
		for (var i = 0; i < _pictures.Count; i++) {
			var pictureData = _pictures[i].RenderContent ();
			var base64 = Convert.ToBase64String (pictureData.ToArray ());
			var fieldString = $"{MetadataBlockPictureField}={base64}";
			var fieldBytes = System.Text.Encoding.UTF8.GetBytes (fieldString);
			pictureFieldBytesList.Add (fieldBytes);
			pictureBytesSize += 4 + fieldBytes.Length;
		}

		var totalFieldCount = fieldBytesList.Count + pictureFieldBytesList.Count;
		var totalSize = 4 + vendorBytes.Length + 4 + fieldBytesSize + pictureBytesSize;

		using var builder = new BinaryDataBuilder (totalSize);

		// Vendor string - explicit little-endian (spec requirement)
		WriteUInt32LE (builder, (uint)vendorBytes.Length);
		builder.Add (vendorBytes);

		// Field count - explicit little-endian
		WriteUInt32LE (builder, (uint)totalFieldCount);

		// Regular fields
		for (var i = 0; i < fieldBytesList.Count; i++) {
			var fieldBytes = fieldBytesList[i];
			WriteUInt32LE (builder, (uint)fieldBytes.Length);
			builder.Add (fieldBytes);
		}

		// Picture fields
		for (var i = 0; i < pictureFieldBytesList.Count; i++) {
			var fieldBytes = pictureFieldBytesList[i];
			WriteUInt32LE (builder, (uint)fieldBytes.Length);
			builder.Add (fieldBytes);
		}

		return builder.ToBinaryData ();
	}

	static void WriteUInt32LE (BinaryDataBuilder builder, uint value)
	{
		builder.Add ((byte)(value & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 24) & 0xFF));
	}

	static uint ReadUInt32LE (ReadOnlySpan<byte> data)
	{
		return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
	}
}

/// <summary>
/// Represents the result of reading a <see cref="VorbisComment"/> from binary data.
/// </summary>
public readonly struct VorbisCommentReadResult : IEquatable<VorbisCommentReadResult>
{
	/// <summary>
	/// Gets the parsed tag, or null if parsing failed.
	/// </summary>
	public VorbisComment? Tag { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Tag is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	VorbisCommentReadResult (VorbisComment? tag, string? error, int bytesConsumed)
	{
		Tag = tag;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="tag">The parsed tag.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static VorbisCommentReadResult Success (VorbisComment tag, int bytesConsumed) =>
		new (tag, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static VorbisCommentReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (VorbisCommentReadResult other) =>
		ReferenceEquals (Tag, other.Tag) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is VorbisCommentReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Tag, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (VorbisCommentReadResult left, VorbisCommentReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (VorbisCommentReadResult left, VorbisCommentReadResult right) =>
		!left.Equals (right);
}
