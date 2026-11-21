// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Unified tag view over a <see cref="FlacFile"/>'s metadata.
/// </summary>
/// <remarks>
/// <para>
/// FLAC can store pictures in two spec-defined locations:
/// </para>
/// <list type="bullet">
///   <item>Native PICTURE metadata blocks (block type 6), per RFC 9639 §8.8.</item>
///   <item>METADATA_BLOCK_PICTURE fields inside the VORBIS_COMMENT block (base64-encoded),
///     per https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE.</item>
/// </list>
/// <para>
/// This class provides a single Tag view that surfaces both so callers do not need
/// to know FLAC's internal storage layout.
/// </para>
/// </remarks>
public sealed class FlacTag : Tag
{
	readonly FlacFile _file;

	internal FlacTag (FlacFile file)
	{
		_file = file;
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Xiph | TagTypes.FlacMetadata;

	/// <inheritdoc/>
	public override string? Title {
		get => _file.VorbisComment?.Title;
		set => EnsureVorbisComment ().Title = value;
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _file.VorbisComment?.Artist;
		set => EnsureVorbisComment ().Artist = value;
	}

	/// <inheritdoc/>
	public override string? Album {
		get => _file.VorbisComment?.Album;
		set => EnsureVorbisComment ().Album = value;
	}

	/// <inheritdoc/>
	public override string? Year {
		get => _file.VorbisComment?.Year;
		set => EnsureVorbisComment ().Year = value;
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => _file.VorbisComment?.Comment;
		set => EnsureVorbisComment ().Comment = value;
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => _file.VorbisComment?.Genre;
		set => EnsureVorbisComment ().Genre = value;
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => _file.VorbisComment?.Track;
		set => EnsureVorbisComment ().Track = value;
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => _file.VorbisComment?.OriginalReleaseDate;
		set => EnsureVorbisComment ().OriginalReleaseDate = value;
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => _file.VorbisComment?.AlbumArtist;
		set => EnsureVorbisComment ().AlbumArtist = value;
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => _file.VorbisComment?.Composer;
		set => EnsureVorbisComment ().Composer = value;
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => _file.VorbisComment?.Conductor;
		set => EnsureVorbisComment ().Conductor = value;
	}

	/// <inheritdoc/>
	public override string? Work {
		get => _file.VorbisComment?.Work;
		set => EnsureVorbisComment ().Work = value;
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => _file.VorbisComment?.Movement;
		set => EnsureVorbisComment ().Movement = value;
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => _file.VorbisComment?.Copyright;
		set => EnsureVorbisComment ().Copyright = value;
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => _file.VorbisComment?.PodcastFeedUrl;
		set => EnsureVorbisComment ().PodcastFeedUrl = value;
	}

	/// <inheritdoc/>
	public override string? DiscSubtitle {
		get => _file.VorbisComment?.DiscSubtitle;
		set => EnsureVorbisComment ().DiscSubtitle = value;
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => _file.VorbisComment?.Lyrics;
		set => EnsureVorbisComment ().Lyrics = value;
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => _file.VorbisComment?.Isrc;
		set => EnsureVorbisComment ().Isrc = value;
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => _file.VorbisComment?.Publisher;
		set => EnsureVorbisComment ().Publisher = value;
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => _file.VorbisComment?.EncodedBy;
		set => EnsureVorbisComment ().EncodedBy = value;
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => _file.VorbisComment?.EncoderSettings;
		set => EnsureVorbisComment ().EncoderSettings = value;
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => _file.VorbisComment?.Grouping;
		set => EnsureVorbisComment ().Grouping = value;
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => _file.VorbisComment?.Subtitle;
		set => EnsureVorbisComment ().Subtitle = value;
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => _file.VorbisComment?.Remixer;
		set => EnsureVorbisComment ().Remixer = value;
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => _file.VorbisComment?.InitialKey;
		set => EnsureVorbisComment ().InitialKey = value;
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => _file.VorbisComment?.Mood;
		set => EnsureVorbisComment ().Mood = value;
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => _file.VorbisComment?.MediaType;
		set => EnsureVorbisComment ().MediaType = value;
	}

	/// <inheritdoc/>
	public override string? Language {
		get => _file.VorbisComment?.Language;
		set => EnsureVorbisComment ().Language = value;
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => _file.VorbisComment?.Barcode;
		set => EnsureVorbisComment ().Barcode = value;
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => _file.VorbisComment?.CatalogNumber;
		set => EnsureVorbisComment ().CatalogNumber = value;
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => _file.VorbisComment?.AlbumSort;
		set => EnsureVorbisComment ().AlbumSort = value;
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => _file.VorbisComment?.ArtistSort;
		set => EnsureVorbisComment ().ArtistSort = value;
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => _file.VorbisComment?.TitleSort;
		set => EnsureVorbisComment ().TitleSort = value;
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => _file.VorbisComment?.AlbumArtistSort;
		set => EnsureVorbisComment ().AlbumArtistSort = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => _file.VorbisComment?.ReplayGainTrackGain;
		set => EnsureVorbisComment ().ReplayGainTrackGain = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => _file.VorbisComment?.ReplayGainTrackPeak;
		set => EnsureVorbisComment ().ReplayGainTrackPeak = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => _file.VorbisComment?.ReplayGainAlbumGain;
		set => EnsureVorbisComment ().ReplayGainAlbumGain = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => _file.VorbisComment?.ReplayGainAlbumPeak;
		set => EnsureVorbisComment ().ReplayGainAlbumPeak = value;
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => _file.VorbisComment?.R128TrackGain;
		set => EnsureVorbisComment ().R128TrackGain = value;
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => _file.VorbisComment?.R128AlbumGain;
		set => EnsureVorbisComment ().R128AlbumGain = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => _file.VorbisComment?.MusicBrainzTrackId;
		set => EnsureVorbisComment ().MusicBrainzTrackId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => _file.VorbisComment?.MusicBrainzReleaseId;
		set => EnsureVorbisComment ().MusicBrainzReleaseId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => _file.VorbisComment?.MusicBrainzArtistId;
		set => EnsureVorbisComment ().MusicBrainzArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => _file.VorbisComment?.MusicBrainzReleaseGroupId;
		set => EnsureVorbisComment ().MusicBrainzReleaseGroupId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => _file.VorbisComment?.MusicBrainzAlbumArtistId;
		set => EnsureVorbisComment ().MusicBrainzAlbumArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseArtistId {
		get => _file.VorbisComment?.MusicBrainzReleaseArtistId;
		set => EnsureVorbisComment ().MusicBrainzReleaseArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => _file.VorbisComment?.MusicBrainzRecordingId;
		set => EnsureVorbisComment ().MusicBrainzRecordingId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => _file.VorbisComment?.MusicBrainzWorkId;
		set => EnsureVorbisComment ().MusicBrainzWorkId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => _file.VorbisComment?.MusicBrainzDiscId;
		set => EnsureVorbisComment ().MusicBrainzDiscId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => _file.VorbisComment?.MusicBrainzReleaseStatus;
		set => EnsureVorbisComment ().MusicBrainzReleaseStatus = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => _file.VorbisComment?.MusicBrainzReleaseType;
		set => EnsureVorbisComment ().MusicBrainzReleaseType = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => _file.VorbisComment?.MusicBrainzReleaseCountry;
		set => EnsureVorbisComment ().MusicBrainzReleaseCountry = value;
	}

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => _file.VorbisComment?.AcoustIdId;
		set => EnsureVorbisComment ().AcoustIdId = value;
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => _file.VorbisComment?.AcoustIdFingerprint;
		set => EnsureVorbisComment ().AcoustIdFingerprint = value;
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => _file.VorbisComment?.ComposerSort;
		set => EnsureVorbisComment ().ComposerSort = value;
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => _file.VorbisComment?.DateTagged;
		set => EnsureVorbisComment ().DateTagged = value;
	}

	/// <inheritdoc/>
	public override string? Description {
		get => _file.VorbisComment?.Description;
		set => EnsureVorbisComment ().Description = value;
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => _file.VorbisComment?.AmazonId;
		set => EnsureVorbisComment ().AmazonId = value;
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => _file.VorbisComment?.DiscNumber;
		set => EnsureVorbisComment ().DiscNumber = value;
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get => _file.VorbisComment?.BeatsPerMinute;
		set => EnsureVorbisComment ().BeatsPerMinute = value;
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get => _file.VorbisComment?.MovementNumber;
		set => EnsureVorbisComment ().MovementNumber = value;
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get => _file.VorbisComment?.MovementTotal;
		set => EnsureVorbisComment ().MovementTotal = value;
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get => _file.VorbisComment?.TotalTracks;
		set => EnsureVorbisComment ().TotalTracks = value;
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => _file.VorbisComment?.TotalDiscs;
		set => EnsureVorbisComment ().TotalDiscs = value;
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => _file.VorbisComment?.IsCompilation ?? false;
		set => EnsureVorbisComment ().IsCompilation = value;
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Performers {
		get => _file.VorbisComment?.Performers ?? [];
		set => EnsureVorbisComment ().Performers = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Genres {
		get => _file.VorbisComment?.Genres ?? [];
		set => EnsureVorbisComment ().Genres = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] AlbumArtists {
		get => _file.VorbisComment?.AlbumArtists ?? [];
		set => EnsureVorbisComment ().AlbumArtists = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[] Composers {
		get => _file.VorbisComment?.Composers ?? [];
		set => EnsureVorbisComment ().Composers = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? PerformersSort {
		get => _file.VorbisComment?.PerformersSort;
		set => EnsureVorbisComment ().PerformersSort = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? AlbumArtistsSort {
		get => _file.VorbisComment?.AlbumArtistsSort;
		set => EnsureVorbisComment ().AlbumArtistsSort = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? ComposersSort {
		get => _file.VorbisComment?.ComposersSort;
		set => EnsureVorbisComment ().ComposersSort = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override string[]? PerformersRole {
		get => _file.VorbisComment?.PerformersRole;
		set => EnsureVorbisComment ().PerformersRole = value;
	}
#pragma warning restore CA1819

	// Text, ids, and ReplayGain live in the VORBIS_COMMENT block
	// (https://xiph.org/vorbis/doc/v-comment.html, https://wiki.xiph.org/VorbisComment).
	// PICTURE blocks stay on FlacFile; this view only special-cases Pictures.

	VorbisComment EnsureVorbisComment () =>
		_file.VorbisComment ??= new VorbisComment ("TagLibSharp2");

	/// <inheritdoc/>
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	/// <remarks>Reads the VORBIS_COMMENT block (RFC 9639 §8.6).</remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		return _file.VorbisComment?.GetCustomField (name) ?? CustomFieldValues.None;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Writes the VORBIS_COMMENT block (RFC 9639 §8.6). The block is created only when the field is stored,
	/// so a refused name does not add an empty block to the file.
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (_file.VorbisComment is { } existing)
			return existing.SetCustomField (name, values);

		var created = new VorbisComment ("TagLibSharp2");
		if (!created.SetCustomField (name, values))
			return false;
		if (created.Fields.Count > 0)
			_file.VorbisComment = created;
		return true;
	}

	/// <inheritdoc/>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		return _file.VorbisComment?.RemoveCustomField (name) ?? false;
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override IPicture[] Pictures {
		get {
			var blockPictures = _file.Pictures;
			var embedded = _file.VorbisComment?.Pictures ?? [];
			var merged = new List<IPicture> (blockPictures.Count + embedded.Length);
			var seen = new HashSet<(PictureType, string, BinaryData)> ();
			foreach (var p in blockPictures) {
				if (seen.Add ((p.PictureType, p.MimeType, p.PictureData)))
					merged.Add (p);
			}
			foreach (var p in embedded) {
				if (seen.Add ((p.PictureType, p.MimeType, p.PictureData)))
					merged.Add (p);
			}
			return [.. merged];
		}
		set {
			_file.RemoveAllPictures ();
			_file.VorbisComment?.RemoveAllPictures ();
			if (value is null)
				return;
			foreach (var p in value) {
				var flacPic = p as FlacPicture ?? new FlacPicture (
					p.MimeType, p.PictureType, p.Description, p.PictureData, 0, 0, 0, 0);
				_file.AddPicture (flacPic);
			}
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	/// <exception cref="NotSupportedException">
	/// A FLAC file's tag state lives in multiple metadata blocks (VORBIS_COMMENT plus
	/// zero or more PICTURE blocks) and is rendered as part of the full file layout.
	/// It has no standalone binary representation. Render the owning
	/// <see cref="FlacFile"/> instead.
	/// </exception>
	public override BinaryData Render () =>
		throw new NotSupportedException (
			"FlacTag is a view over multiple FLAC metadata blocks and has no standalone binary "
			+ "representation. Render the owning FlacFile instead (FlacFile.Render).");

	/// <inheritdoc/>
	/// <remarks>
	/// Clears every metadata source this view surfaces: the VorbisComment fields
	/// (including any METADATA_BLOCK_PICTURE entries) and the native PICTURE blocks
	/// held on the <see cref="FlacFile"/>.
	/// </remarks>
	public override void Clear ()
	{
		_file.VorbisComment?.Clear ();
		_file.RemoveAllPictures ();
	}
}
