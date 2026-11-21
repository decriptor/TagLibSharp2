// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// A tag facade that exposes a unified view over multiple underlying <see cref="Tag"/>
/// instances in priority order.
/// </summary>
/// <remarks>
/// <para>
/// Getters return the first non-null/non-empty value from the ordered list of tags.
/// Pictures are unioned across members and deduplicated on (PictureType, MimeType,
/// PictureData).
/// </para>
/// <para>
/// Setters write through to <b>every</b> non-null underlying tag so that content
/// stays consistent across formats. This matches user expectations on files that
/// carry redundant metadata (e.g. an MP3 with both ID3v2 and ID3v1) and prevents
/// post-save drift where one tag has the new value and the other has the old one.
/// Format-specific limits still apply per tag (ID3v1 truncates to 30 bytes, https://id3.org/ID3v1). Fields ID3v1 cannot store (album artist, lyrics, ReplayGain, MusicBrainz) are written through as well; the ID3v1 base implementation ignores them. Text uses the first non-empty value so a blank ID3v2 frame falls through to ID3v1 (https://id3.org/id3v2.4.0-frames). IsCompilation uses the first tag's value, including false, because ID3v2 TCMP is authoritative when that tag is present.
/// </para>
/// <para>
/// <see cref="Render"/> throws because a <see cref="CombinedTag"/> is a view, not
/// a serializable block: render the owning file or a specific underlying tag instead.
/// <see cref="Clear"/> clears every non-null underlying tag.
/// </para>
/// <para>
/// The motivating case is an MP3 file that carries both an ID3v2 tag
/// (https://id3.org/id3v2.4.0-structure) and an ID3v1 tag (https://id3.org/ID3v1).
/// ID3v2 is authoritative when both are present, but ID3v1-only fields must still
/// surface through the unified view so legacy data is not silently lost.
/// </para>
/// </remarks>
public class CombinedTag : Tag
{
	readonly Tag?[] _tags;

	/// <summary>
	/// Initializes a new <see cref="CombinedTag"/> with the supplied tags in
	/// priority order. The first non-null tag's value wins for each field.
	/// </summary>
	/// <param name="tags">Tags in priority order. Null entries are allowed and ignored.</param>
	public CombinedTag (params Tag?[] tags)
	{
		_tags = tags ?? [];
	}

	/// <summary>
	/// Gets the underlying tags in priority order. Null entries are preserved.
	/// </summary>
	protected IReadOnlyList<Tag?> Tags => _tags;

	T? FirstNonDefault<T> (Func<Tag, T?> selector, Func<T?, bool> isDefault)
	{
		foreach (var tag in _tags) {
			if (tag is null)
				continue;
			var value = selector (tag);
			if (!isDefault (value))
				return value;
		}
		return default;
	}

	string? FirstNonEmptyString (Func<Tag, string?> selector) =>
		FirstNonDefault (selector, string.IsNullOrEmpty);

	uint? FirstNonNullUInt (Func<Tag, uint?> selector) =>
		FirstNonDefault<uint?> (selector, v => !v.HasValue);


	// A non-empty array is a real value. An empty array falls through so a tag
	// that does not store the field (ID3v1 performers collapse to Artist, or the
	// base returns nothing) does not hide a later tag that does.
	string[] FirstNonEmptyArray (Func<Tag, string[]?> selector)
	{
		foreach (var tag in _tags) {
			if (tag is null)
				continue;
			var value = selector (tag);
			if (value is { Length: > 0 })
				return value;
		}
		return [];
	}

	string[]? FirstNonEmptyNullableArray (Func<Tag, string[]?> selector)
	{
		foreach (var tag in _tags) {
			if (tag is null)
				continue;
			var value = selector (tag);
			if (value is { Length: > 0 })
				return value;
		}
		return null;
	}

	// Booleans have no "missing" state on the base Tag (IsCompilation defaults
	// to false). The first tag in priority order wins, including false, so an
	// ID3v2 TCMP of 0 is not replaced by a later tag.
	bool FirstBool (Func<Tag, bool> selector)
	{
		foreach (var tag in _tags) {
			if (tag is not null)
				return selector (tag);
		}
		return false;
	}

	void WriteToAll (Action<Tag> setter)
	{
		foreach (var tag in _tags) {
			if (tag is not null)
				setter (tag);
		}
	}

	/// <inheritdoc/>
	public override TagTypes TagType {
		get {
			var types = TagTypes.None;
			foreach (var tag in _tags) {
				if (tag is not null)
					types |= tag.TagType;
			}
			return types;
		}
	}

	/// <inheritdoc/>
	public override string? Title {
		get => FirstNonEmptyString (t => t.Title);
		set => WriteToAll (t => t.Title = value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => FirstNonEmptyString (t => t.Artist);
		set => WriteToAll (t => t.Artist = value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => FirstNonEmptyString (t => t.Album);
		set => WriteToAll (t => t.Album = value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => FirstNonEmptyString (t => t.Year);
		set => WriteToAll (t => t.Year = value);
	}

	/// <inheritdoc/>
	private protected override string? CopyableDate =>
		FirstNonEmptyString (tag => tag.ReadCopyableDate ());

	/// <inheritdoc/>
	public override string? Comment {
		get => FirstNonEmptyString (t => t.Comment);
		set => WriteToAll (t => t.Comment = value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => FirstNonEmptyString (t => t.Genre);
		set => WriteToAll (t => t.Genre = value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => FirstNonNullUInt (t => t.Track);
		set => WriteToAll (t => t.Track = value);
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => FirstNonEmptyString (t => t.OriginalReleaseDate);
		set => WriteToAll (t => t.OriginalReleaseDate = value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => FirstNonEmptyString (t => t.AlbumArtist);
		set => WriteToAll (t => t.AlbumArtist = value);
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => FirstNonEmptyString (t => t.Composer);
		set => WriteToAll (t => t.Composer = value);
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => FirstNonEmptyString (t => t.Conductor);
		set => WriteToAll (t => t.Conductor = value);
	}

	/// <inheritdoc/>
	public override string? Work {
		get => FirstNonEmptyString (t => t.Work);
		set => WriteToAll (t => t.Work = value);
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => FirstNonEmptyString (t => t.Movement);
		set => WriteToAll (t => t.Movement = value);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => FirstNonEmptyString (t => t.Copyright);
		set => WriteToAll (t => t.Copyright = value);
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => FirstNonEmptyString (t => t.PodcastFeedUrl);
		set => WriteToAll (t => t.PodcastFeedUrl = value);
	}

	/// <inheritdoc/>
	public override string? DiscSubtitle {
		get => FirstNonEmptyString (t => t.DiscSubtitle);
		set => WriteToAll (t => t.DiscSubtitle = value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => FirstNonEmptyString (t => t.Lyrics);
		set => WriteToAll (t => t.Lyrics = value);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => FirstNonEmptyString (t => t.Isrc);
		set => WriteToAll (t => t.Isrc = value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => FirstNonEmptyString (t => t.Publisher);
		set => WriteToAll (t => t.Publisher = value);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => FirstNonEmptyString (t => t.EncodedBy);
		set => WriteToAll (t => t.EncodedBy = value);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => FirstNonEmptyString (t => t.EncoderSettings);
		set => WriteToAll (t => t.EncoderSettings = value);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => FirstNonEmptyString (t => t.Grouping);
		set => WriteToAll (t => t.Grouping = value);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => FirstNonEmptyString (t => t.Subtitle);
		set => WriteToAll (t => t.Subtitle = value);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => FirstNonEmptyString (t => t.Remixer);
		set => WriteToAll (t => t.Remixer = value);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => FirstNonEmptyString (t => t.InitialKey);
		set => WriteToAll (t => t.InitialKey = value);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => FirstNonEmptyString (t => t.Mood);
		set => WriteToAll (t => t.Mood = value);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => FirstNonEmptyString (t => t.MediaType);
		set => WriteToAll (t => t.MediaType = value);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => FirstNonEmptyString (t => t.Language);
		set => WriteToAll (t => t.Language = value);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => FirstNonEmptyString (t => t.Barcode);
		set => WriteToAll (t => t.Barcode = value);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => FirstNonEmptyString (t => t.CatalogNumber);
		set => WriteToAll (t => t.CatalogNumber = value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => FirstNonEmptyString (t => t.AlbumSort);
		set => WriteToAll (t => t.AlbumSort = value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => FirstNonEmptyString (t => t.ArtistSort);
		set => WriteToAll (t => t.ArtistSort = value);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => FirstNonEmptyString (t => t.TitleSort);
		set => WriteToAll (t => t.TitleSort = value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => FirstNonEmptyString (t => t.AlbumArtistSort);
		set => WriteToAll (t => t.AlbumArtistSort = value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => FirstNonEmptyString (t => t.ReplayGainTrackGain);
		set => WriteToAll (t => t.ReplayGainTrackGain = value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => FirstNonEmptyString (t => t.ReplayGainTrackPeak);
		set => WriteToAll (t => t.ReplayGainTrackPeak = value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => FirstNonEmptyString (t => t.ReplayGainAlbumGain);
		set => WriteToAll (t => t.ReplayGainAlbumGain = value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => FirstNonEmptyString (t => t.ReplayGainAlbumPeak);
		set => WriteToAll (t => t.ReplayGainAlbumPeak = value);
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => FirstNonEmptyString (t => t.R128TrackGain);
		set => WriteToAll (t => t.R128TrackGain = value);
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => FirstNonEmptyString (t => t.R128AlbumGain);
		set => WriteToAll (t => t.R128AlbumGain = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => FirstNonEmptyString (t => t.MusicBrainzTrackId);
		set => WriteToAll (t => t.MusicBrainzTrackId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => FirstNonEmptyString (t => t.MusicBrainzReleaseId);
		set => WriteToAll (t => t.MusicBrainzReleaseId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => FirstNonEmptyString (t => t.MusicBrainzArtistId);
		set => WriteToAll (t => t.MusicBrainzArtistId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => FirstNonEmptyString (t => t.MusicBrainzReleaseGroupId);
		set => WriteToAll (t => t.MusicBrainzReleaseGroupId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => FirstNonEmptyString (t => t.MusicBrainzAlbumArtistId);
		set => WriteToAll (t => t.MusicBrainzAlbumArtistId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseArtistId {
		get => FirstNonEmptyString (t => t.MusicBrainzReleaseArtistId);
		set => WriteToAll (t => t.MusicBrainzReleaseArtistId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => FirstNonEmptyString (t => t.MusicBrainzRecordingId);
		set => WriteToAll (t => t.MusicBrainzRecordingId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => FirstNonEmptyString (t => t.MusicBrainzWorkId);
		set => WriteToAll (t => t.MusicBrainzWorkId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => FirstNonEmptyString (t => t.MusicBrainzDiscId);
		set => WriteToAll (t => t.MusicBrainzDiscId = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => FirstNonEmptyString (t => t.MusicBrainzReleaseStatus);
		set => WriteToAll (t => t.MusicBrainzReleaseStatus = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => FirstNonEmptyString (t => t.MusicBrainzReleaseType);
		set => WriteToAll (t => t.MusicBrainzReleaseType = value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => FirstNonEmptyString (t => t.MusicBrainzReleaseCountry);
		set => WriteToAll (t => t.MusicBrainzReleaseCountry = value);
	}

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => FirstNonEmptyString (t => t.AcoustIdId);
		set => WriteToAll (t => t.AcoustIdId = value);
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => FirstNonEmptyString (t => t.AcoustIdFingerprint);
		set => WriteToAll (t => t.AcoustIdFingerprint = value);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => FirstNonEmptyString (t => t.ComposerSort);
		set => WriteToAll (t => t.ComposerSort = value);
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => FirstNonEmptyString (t => t.DateTagged);
		set => WriteToAll (t => t.DateTagged = value);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => FirstNonEmptyString (t => t.Description);
		set => WriteToAll (t => t.Description = value);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => FirstNonEmptyString (t => t.AmazonId);
		set => WriteToAll (t => t.AmazonId = value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => FirstNonNullUInt (t => t.DiscNumber);
		set => WriteToAll (t => t.DiscNumber = value);
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get => FirstNonNullUInt (t => t.BeatsPerMinute);
		set => WriteToAll (t => t.BeatsPerMinute = value);
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get => FirstNonNullUInt (t => t.MovementNumber);
		set => WriteToAll (t => t.MovementNumber = value);
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get => FirstNonNullUInt (t => t.MovementTotal);
		set => WriteToAll (t => t.MovementTotal = value);
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get => FirstNonNullUInt (t => t.TotalTracks);
		set => WriteToAll (t => t.TotalTracks = value);
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => FirstNonNullUInt (t => t.TotalDiscs);
		set => WriteToAll (t => t.TotalDiscs = value);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => FirstBool (t => t.IsCompilation);
		set => WriteToAll (t => t.IsCompilation = value);
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Performers {
		get => FirstNonEmptyArray (t => t.Performers);
		set => WriteToAll (t => t.Performers = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Genres {
		get => FirstNonEmptyArray (t => t.Genres);
		set => WriteToAll (t => t.Genres = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] AlbumArtists {
		get => FirstNonEmptyArray (t => t.AlbumArtists);
		set => WriteToAll (t => t.AlbumArtists = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Composers {
		get => FirstNonEmptyArray (t => t.Composers);
		set => WriteToAll (t => t.Composers = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? PerformersSort {
		get => FirstNonEmptyNullableArray (t => t.PerformersSort);
		set => WriteToAll (t => t.PerformersSort = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? AlbumArtistsSort {
		get => FirstNonEmptyNullableArray (t => t.AlbumArtistsSort);
		set => WriteToAll (t => t.AlbumArtistsSort = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? ComposersSort {
		get => FirstNonEmptyNullableArray (t => t.ComposersSort);
		set => WriteToAll (t => t.ComposersSort = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? PerformersRole {
		get => FirstNonEmptyNullableArray (t => t.PerformersRole);
		set => WriteToAll (t => t.PerformersRole = value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override IPicture[] Pictures {
		get {
			var merged = new List<IPicture> ();
			var seen = new HashSet<(PictureType, string, BinaryData)> ();
			foreach (var tag in _tags) {
				if (tag is null)
					continue;
				foreach (var p in tag.Pictures) {
					if (seen.Add ((p.PictureType, p.MimeType, p.PictureData)))
						merged.Add (p);
				}
			}
			return [.. merged];
		}
		set => WriteToAll (t => t.Pictures = value ?? []);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	/// <remarks>True when any underlying tag supports custom fields.</remarks>
	public override bool SupportsCustomFields {
		get {
			foreach (var tag in _tags) {
				if (tag is { SupportsCustomFields: true })
					return true;
			}
			return false;
		}
	}

	/// <inheritdoc/>
	/// <remarks>Returns the values of the first tag in priority order that holds any.</remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		foreach (var tag in _tags) {
			if (tag is null)
				continue;
			var values = tag.GetCustomField (name);
			if (values.Count > 0)
				return values;
		}
		return CustomFieldValues.None;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Writes every underlying tag that supports custom fields, as the property setters write every tag.
	/// Returns true when at least one of them stored the values. A tag without custom fields, such as
	/// ID3v1, is left as it is.
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		var stored = false;
		foreach (var tag in _tags) {
			if (tag is { SupportsCustomFields: true })
				stored |= tag.SetCustomField (name, values);
		}
		return stored;
	}

	/// <inheritdoc/>
	/// <remarks>Removes the field from every underlying tag. Returns true when any of them held it.</remarks>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		var removed = false;
		foreach (var tag in _tags) {
			if (tag is not null)
				removed |= tag.RemoveCustomField (name);
		}
		return removed;
	}

	/// <inheritdoc/>
	/// <exception cref="NotSupportedException">
	/// <see cref="CombinedTag"/> is a view over multiple tags and does not produce its
	/// own serialized representation. Render the owning file or a specific underlying
	/// tag instead.
	/// </exception>
	public override BinaryData Render () =>
		throw new NotSupportedException (
			"CombinedTag is a view over multiple tags; it has no standalone binary representation. "
			+ "Render the owning file (e.g. Mp3File.Render) or a specific underlying tag instead.");

	/// <inheritdoc/>
	/// <remarks>
	/// Clears every non-null underlying tag, leaving each instance in place but empty.
	/// </remarks>
	public override void Clear () =>
		WriteToAll (t => t.Clear ());
}
