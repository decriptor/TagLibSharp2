// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Aiff;

/// <summary>
/// View over an AIFF file's ID3 tag and native text chunks.
/// Comment, Title, Artist, Copyright, and Performers prefer ID3 and fall back
/// to native text. Every other field is forwarded to the ID3 tag. Every set
/// writes the ID3 tag; an edited native field's chunks are dropped on save.
/// </summary>
sealed class AiffTag : Tag
{
	readonly AiffFile _file;

	internal AiffTag (AiffFile file) => _file = file;

	/// <inheritdoc/>
	public override TagTypes TagType {
		get {
			// TagTypes has no AIFF member. Native NAME/AUTH/ANNO/(c)/COMT-0 text is
			// reported as RiffInfo, the existing INFO-style chunk type. Notes for other
			// markers are not tag fields, as in IsEmpty.
			var types = TagTypes.None;
			if (Id3 is { IsEmpty: false })
				types |= TagTypes.Id3v2;
			if (_file.HasNativeFields)
				types |= TagTypes.RiffInfo;
			return types;
		}
	}

	/// <inheritdoc/>
	public override string? Title {
		get => _file.Title;
		set => _file.Title = value;
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _file.Artist;
		set => _file.Artist = value;
	}

	/// <inheritdoc/>
	public override string? Album {
		get => Id3?.Album;
		set => Ensure ().Album = value;
	}

	/// <inheritdoc/>
	public override string? Year {
		get => Id3?.Year;
		set => Ensure ().Year = value;
	}

	/// <inheritdoc/>
	private protected override string? CopyableDate => Id3?.ReadCopyableDate ();

	/// <inheritdoc/>
	public override string? Comment {
		get => _file.Comment;
		set => _file.Comment = value;
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => Id3?.Genre;
		set => Ensure ().Genre = value;
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => Id3?.Track;
		set => Ensure ().Track = value;
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => Id3?.OriginalReleaseDate;
		set => Ensure ().OriginalReleaseDate = value;
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => Id3?.AlbumArtist;
		set => Ensure ().AlbumArtist = value;
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => Id3?.Composer;
		set => Ensure ().Composer = value;
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => Id3?.Conductor;
		set => Ensure ().Conductor = value;
	}

	/// <inheritdoc/>
	public override string? Work {
		get => Id3?.Work;
		set => Ensure ().Work = value;
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => Id3?.Movement;
		set => Ensure ().Movement = value;
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => _file.Copyright;
		set => _file.Copyright = value;
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => Id3?.PodcastFeedUrl;
		set => Ensure ().PodcastFeedUrl = value;
	}

	/// <inheritdoc/>
	public override string? DiscSubtitle {
		get => Id3?.DiscSubtitle;
		set => Ensure ().DiscSubtitle = value;
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => Id3?.Lyrics;
		set => Ensure ().Lyrics = value;
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => Id3?.Isrc;
		set => Ensure ().Isrc = value;
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => Id3?.Publisher;
		set => Ensure ().Publisher = value;
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => Id3?.EncodedBy;
		set => Ensure ().EncodedBy = value;
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => Id3?.EncoderSettings;
		set => Ensure ().EncoderSettings = value;
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => Id3?.Grouping;
		set => Ensure ().Grouping = value;
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => Id3?.Subtitle;
		set => Ensure ().Subtitle = value;
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => Id3?.Remixer;
		set => Ensure ().Remixer = value;
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => Id3?.InitialKey;
		set => Ensure ().InitialKey = value;
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => Id3?.Mood;
		set => Ensure ().Mood = value;
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => Id3?.MediaType;
		set => Ensure ().MediaType = value;
	}

	/// <inheritdoc/>
	public override string? Language {
		get => Id3?.Language;
		set => Ensure ().Language = value;
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => Id3?.Barcode;
		set => Ensure ().Barcode = value;
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => Id3?.CatalogNumber;
		set => Ensure ().CatalogNumber = value;
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => Id3?.AlbumSort;
		set => Ensure ().AlbumSort = value;
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => Id3?.ArtistSort;
		set => Ensure ().ArtistSort = value;
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => Id3?.TitleSort;
		set => Ensure ().TitleSort = value;
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => Id3?.AlbumArtistSort;
		set => Ensure ().AlbumArtistSort = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => Id3?.ReplayGainTrackGain;
		set => Ensure ().ReplayGainTrackGain = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => Id3?.ReplayGainTrackPeak;
		set => Ensure ().ReplayGainTrackPeak = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => Id3?.ReplayGainAlbumGain;
		set => Ensure ().ReplayGainAlbumGain = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => Id3?.ReplayGainAlbumPeak;
		set => Ensure ().ReplayGainAlbumPeak = value;
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => Id3?.R128TrackGain;
		set => Ensure ().R128TrackGain = value;
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => Id3?.R128AlbumGain;
		set => Ensure ().R128AlbumGain = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => Id3?.MusicBrainzTrackId;
		set => Ensure ().MusicBrainzTrackId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => Id3?.MusicBrainzReleaseId;
		set => Ensure ().MusicBrainzReleaseId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => Id3?.MusicBrainzArtistId;
		set => Ensure ().MusicBrainzArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => Id3?.MusicBrainzReleaseGroupId;
		set => Ensure ().MusicBrainzReleaseGroupId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => Id3?.MusicBrainzAlbumArtistId;
		set => Ensure ().MusicBrainzAlbumArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseArtistId {
		get => Id3?.MusicBrainzReleaseArtistId;
		set => Ensure ().MusicBrainzReleaseArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => Id3?.MusicBrainzRecordingId;
		set => Ensure ().MusicBrainzRecordingId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => Id3?.MusicBrainzWorkId;
		set => Ensure ().MusicBrainzWorkId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => Id3?.MusicBrainzDiscId;
		set => Ensure ().MusicBrainzDiscId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => Id3?.MusicBrainzReleaseStatus;
		set => Ensure ().MusicBrainzReleaseStatus = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => Id3?.MusicBrainzReleaseType;
		set => Ensure ().MusicBrainzReleaseType = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => Id3?.MusicBrainzReleaseCountry;
		set => Ensure ().MusicBrainzReleaseCountry = value;
	}

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => Id3?.AcoustIdId;
		set => Ensure ().AcoustIdId = value;
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => Id3?.AcoustIdFingerprint;
		set => Ensure ().AcoustIdFingerprint = value;
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => Id3?.ComposerSort;
		set => Ensure ().ComposerSort = value;
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => Id3?.DateTagged;
		set => Ensure ().DateTagged = value;
	}

	/// <inheritdoc/>
	public override string? Description {
		get => Id3?.Description;
		set => Ensure ().Description = value;
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => Id3?.AmazonId;
		set => Ensure ().AmazonId = value;
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => Id3?.DiscNumber;
		set => Ensure ().DiscNumber = value;
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get => Id3?.BeatsPerMinute;
		set => Ensure ().BeatsPerMinute = value;
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get => Id3?.MovementNumber;
		set => Ensure ().MovementNumber = value;
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get => Id3?.MovementTotal;
		set => Ensure ().MovementTotal = value;
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get => Id3?.TotalTracks;
		set => Ensure ().TotalTracks = value;
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => Id3?.TotalDiscs;
		set => Ensure ().TotalDiscs = value;
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => Id3?.IsCompilation ?? false;
		set => Ensure ().IsCompilation = value;
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Performers {
		get {
			if (Id3?.Performers is { Length: > 0 } people)
				return people;
			var artist = _file.Artist;
			return string.IsNullOrEmpty (artist) ? [] : [artist!];
		}
		set => _file.SetPerformers (value);
	}

	/// <inheritdoc/>
	public override string[] Genres {
		get => Id3?.Genres ?? [];
		set => Ensure ().Genres = value;
	}

	/// <inheritdoc/>
	public override string[] AlbumArtists {
		get => Id3?.AlbumArtists ?? [];
		set => Ensure ().AlbumArtists = value;
	}

	/// <inheritdoc/>
	public override string[] Composers {
		get => Id3?.Composers ?? [];
		set => Ensure ().Composers = value;
	}

	/// <inheritdoc/>
	public override string[]? PerformersSort {
		get => Id3?.PerformersSort;
		set => Ensure ().PerformersSort = value;
	}

	/// <inheritdoc/>
	public override string[]? AlbumArtistsSort {
		get => Id3?.AlbumArtistsSort;
		set => Ensure ().AlbumArtistsSort = value;
	}

	/// <inheritdoc/>
	public override string[]? ComposersSort {
		get => Id3?.ComposersSort;
		set => Ensure ().ComposersSort = value;
	}

	/// <inheritdoc/>
	public override string[]? PerformersRole {
		get => Id3?.PerformersRole;
		set => Ensure ().PerformersRole = value;
	}

	/// <inheritdoc/>
	public override IPicture[] Pictures {
		get => Id3?.Pictures ?? [];
		set => Ensure ().Pictures = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override bool IsEmpty =>
		(Id3 is null || Id3.IsEmpty) && !_file.HasNativeFields;

	/// <inheritdoc/>
	public override BinaryData Render () =>
		throw new NotSupportedException (
			"AiffTag is a view over an AIFF file. Render the owning AiffFile instead.");

	/// <inheritdoc/>
	public override void Clear () => _file.ClearMappedFields ();

	/// <inheritdoc/>
	/// <remarks>Custom fields live in the file's ID3 tag, as <c>TXXX</c> frames; the native text chunks have fixed names.</remarks>
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		return Id3?.GetCustomField (name) ?? CustomFieldValues.None;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Writes a <c>TXXX</c> frame in the file's ID3 tag. The tag is created only when the field is stored, so a
	/// refused name or a write of no values does not add an ID3 chunk.
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (Id3 is { } existing)
			return existing.SetCustomField (name, values);

		var created = new Id3v2Tag ();
		if (!created.SetCustomField (name, values))
			return false;
		if (!created.IsEmpty)
			_file.Id3v2Tag = created;
		return true;
	}

	/// <inheritdoc/>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		return Id3?.RemoveCustomField (name) ?? false;
	}

	Id3v2Tag? Id3 => _file.Id3v2Tag;

	/// <inheritdoc/>
	/// <remarks>
	/// This view reads the file's ID3 tag, so a copy between it and that tag (or another view over the
	/// same file) only adds values the target does not show.
	/// </remarks>
	internal override IEnumerable<Tag> Leaves ()
	{
		yield return this;
		if (Id3 is { } id3) {
			foreach (var leaf in id3.Leaves ())
				yield return leaf;
		}
	}

	Id3v2Tag Ensure () => _file.EnsureId3 ();
}
