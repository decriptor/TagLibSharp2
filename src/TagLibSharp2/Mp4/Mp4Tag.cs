// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Globalization;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents the content rating for an MP4 file (Clean/Explicit/None).
/// </summary>
/// <remarks>
/// Used by iTunes and other media players to indicate whether content
/// contains explicit material.
/// </remarks>
public enum Mp4ContentRating
{
	/// <summary>
	/// No content rating specified.
	/// </summary>
	None = 0,

	/// <summary>
	/// Content is explicit (contains adult language or themes).
	/// </summary>
	Explicit = 1,

	/// <summary>
	/// Content is clean (edited version without explicit material).
	/// </summary>
	Clean = 2
}

/// <summary>
/// Represents iTunes-style metadata in MP4/M4A files.
/// </summary>
/// <remarks>
/// <para>
/// iTunes metadata is stored in the moov → udta → meta → ilst container.
/// Each metadata item consists of an atom identifier (4-byte code) containing
/// one or more data atoms with type-specific values.
/// </para>
/// <para>
/// This is a clean-room implementation based on the MP4 specification and
/// reverse-engineered iTunes behavior, NOT derived from TagLib#.
/// </para>
/// </remarks>
public sealed class Mp4Tag : Tag
{
	readonly Dictionary<string, List<Mp4DataAtom>> _atoms = new ();
	readonly List<Mp4Picture> _pictures = new ();

	// mean and name stay separate so a colon in either field is not a delimiter.
	// The list keeps render order; the index finds a field without scanning, so a file
	// with many freeform atoms reads in linear time.
	readonly LinkedList<FreeformField> _freeform = new ();
	readonly Dictionary<(string Mean, string Name), LinkedListNode<FreeformField>> _freeformByKey = new ();

	// ilst children that are not rebuilt from parsed fields, such as "orch".
	// Indexed by 4-byte type so one edit does not scan every child.
	readonly List<PreservedBox?> _preservedAtoms = new ();
	readonly Dictionary<uint, List<int>> _preservedByType = new ();

	// Preserved freeform boxes that carry a mean and a name, so a set drops its twin directly.
	readonly Dictionary<(string Mean, string Name), List<int>> _preservedFreeformByKey = new ();

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Apple;

	/// <inheritdoc/>
	public override string? Title {
		get => GetText (Mp4AtomMapping.Title);
		set => SetText (Mp4AtomMapping.Title, value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetText (Mp4AtomMapping.Artist);
		set => SetText (Mp4AtomMapping.Artist, value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => GetText (Mp4AtomMapping.Album);
		set => SetText (Mp4AtomMapping.Album, value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => GetText (Mp4AtomMapping.Year);
		set => SetText (Mp4AtomMapping.Year, value);
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetText (Mp4AtomMapping.Comment);
		set => SetText (Mp4AtomMapping.Comment, value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get {
			var text = GetText (Mp4AtomMapping.Genre);
			if (!string.IsNullOrEmpty (text))
				return text;

			// gnre is a 1-based ID3v1 code. A non-empty ©gen wins over it.
			return GetGnreName ();
		}
		set {
			SetText (Mp4AtomMapping.Genre, value);
			// A leftover gnre would still be returned after ©gen is cleared.
			RemoveAtom (Mp4AtomMapping.GenreId3);
		}
	}

	/// <inheritdoc/>
	public override uint? Track {
		get {
			if (GetTrackDisc (Mp4AtomMapping.TrackNumber, out var number, out _))
				return number;
			return null;
		}
		set {
			var total = TotalTracks ?? 0;
			if (value.HasValue)
				SetTrackDisc (Mp4AtomMapping.TrackNumber, value.Value, total);
			else if (total == 0)
				RemoveAtom (Mp4AtomMapping.TrackNumber);
		}
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get {
			if (GetTrackDisc (Mp4AtomMapping.TrackNumber, out _, out var total))
				return total > 0 ? total : null;
			return null;
		}
		set {
			var number = Track ?? 0;
			if (value.HasValue || number > 0)
				SetTrackDisc (Mp4AtomMapping.TrackNumber, number, value ?? 0);
			else
				RemoveAtom (Mp4AtomMapping.TrackNumber);
		}
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetText (Mp4AtomMapping.AlbumArtist);
		set => SetText (Mp4AtomMapping.AlbumArtist, value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get {
			if (GetTrackDisc (Mp4AtomMapping.DiscNumber, out var number, out _))
				return number;
			return null;
		}
		set {
			var total = TotalDiscs ?? 0;
			if (value.HasValue)
				SetTrackDisc (Mp4AtomMapping.DiscNumber, value.Value, total);
			else if (total == 0)
				RemoveAtom (Mp4AtomMapping.DiscNumber);
		}
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get {
			if (GetTrackDisc (Mp4AtomMapping.DiscNumber, out _, out var total))
				return total > 0 ? total : null;
			return null;
		}
		set {
			var number = DiscNumber ?? 0;
			if (value.HasValue || number > 0)
				SetTrackDisc (Mp4AtomMapping.DiscNumber, number, value ?? 0);
			else
				RemoveAtom (Mp4AtomMapping.DiscNumber);
		}
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => GetText (Mp4AtomMapping.Composer);
		set => SetText (Mp4AtomMapping.Composer, value);
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get => GetInteger (Mp4AtomMapping.BeatsPerMinute);
		set => SetInteger (Mp4AtomMapping.BeatsPerMinute, value);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get {
			var text = GetText (Mp4AtomMapping.Copyright);
			if (!string.IsNullOrEmpty (text))
				return text;

			// ©cpy is an alias. A non-empty cprt wins over it.
			return GetText (Mp4AtomMapping.CopyrightAlias);
		}
		set {
			SetText (Mp4AtomMapping.Copyright, value);
			// A leftover ©cpy would still be returned after cprt is cleared.
			RemoveAtom (Mp4AtomMapping.CopyrightAlias);
		}
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => GetBoolean (Mp4AtomMapping.Compilation);
		set => SetBoolean (Mp4AtomMapping.Compilation, value);
	}

	/// <summary>
	/// Gets or sets whether gapless playback should be used.
	/// </summary>
	public bool IsGapless {
		get => GetBoolean (Mp4AtomMapping.GaplessPlayback);
		set => SetBoolean (Mp4AtomMapping.GaplessPlayback, value);
	}

	/// <summary>
	/// Gets or sets the content rating (Clean/Explicit/None).
	/// </summary>
	/// <remarks>
	/// Uses the "rtng" atom. Used by iTunes to indicate whether content
	/// contains explicit material (value 1), is clean/edited (value 2),
	/// or has no rating (value 0).
	/// </remarks>
	public Mp4ContentRating ContentRating {
		get {
			var value = GetInteger (Mp4AtomMapping.ContentRating);
			return value.HasValue ? (Mp4ContentRating)value.Value : Mp4ContentRating.None;
		}
		set {
			if (value == Mp4ContentRating.None)
				RemoveAtom (Mp4AtomMapping.ContentRating);
			else
				SetInteger (Mp4AtomMapping.ContentRating, (uint)value);
		}
	}

	/// <summary>
	/// Gets or sets the gapless playback info (iTunSMPB).
	/// </summary>
	/// <remarks>
	/// This is an iTunes-specific freeform atom containing encoder delay and padding
	/// information for sample-accurate gapless playback.
	/// Format: " 00000000 XXXXXXXX YYYYYYYY ZZZZZZZZZZZZZZZZ 00000000 00000000 ..."
	/// where X is encoder delay, Y is padding, Z is sample count.
	/// </remarks>
	public string? GaplessInfo {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.GaplessInfo);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.GaplessInfo, value);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => GetText (Mp4AtomMapping.Grouping);
		set => SetText (Mp4AtomMapping.Grouping, value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetText (Mp4AtomMapping.Lyrics);
		set => SetText (Mp4AtomMapping.Lyrics, value);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => GetText (Mp4AtomMapping.Encoder);
		set => SetText (Mp4AtomMapping.Encoder, value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetText (Mp4AtomMapping.Publisher);
		set => SetText (Mp4AtomMapping.Publisher, value);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => GetText (Mp4AtomMapping.EncodedBy);
		set => SetText (Mp4AtomMapping.EncodedBy, value);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => GetText (Mp4AtomMapping.Description);
		set => SetText (Mp4AtomMapping.Description, value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetText (Mp4AtomMapping.AlbumSort);
		set => SetText (Mp4AtomMapping.AlbumSort, value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetText (Mp4AtomMapping.ArtistSort);
		set => SetText (Mp4AtomMapping.ArtistSort, value);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetText (Mp4AtomMapping.TitleSort);
		set => SetText (Mp4AtomMapping.TitleSort, value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetText (Mp4AtomMapping.AlbumArtistSort);
		set => SetText (Mp4AtomMapping.AlbumArtistSort, value);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => GetText (Mp4AtomMapping.ComposerSort);
		set => SetText (Mp4AtomMapping.ComposerSort, value);
	}

	/// <inheritdoc/>
	public override string? Work {
		get => GetText (Mp4AtomMapping.WorkName);
		set => SetText (Mp4AtomMapping.WorkName, value);
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => GetText (Mp4AtomMapping.MovementName);
		set => SetText (Mp4AtomMapping.MovementName, value);
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get => GetInteger (Mp4AtomMapping.MovementNumber);
		set => SetInteger (Mp4AtomMapping.MovementNumber, value);
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get => GetInteger (Mp4AtomMapping.MovementCount);
		set => SetInteger (Mp4AtomMapping.MovementCount, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzTrackId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzTrackId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzAlbumId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzAlbumId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzArtistId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzArtistId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzAlbumArtistId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzAlbumArtistId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseGroupId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseGroupId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzWorkId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzWorkId, value);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Isrc);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Isrc, value);
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Conductor);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Conductor, value);
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.OriginalYear);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.OriginalYear, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainTrackGain);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainTrackGain, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainTrackPeak);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainTrackPeak, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainAlbumGain);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainAlbumGain, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainAlbumPeak);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.ReplayGainAlbumPeak, value);
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.R128TrackGain);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.R128TrackGain, value);
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.R128AlbumGain);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.R128AlbumGain, value);
	}

	// AcoustID properties

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.AcoustIdId);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.AcoustIdId, value);
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.AcoustIdFingerprint);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.AcoustIdFingerprint, value);
	}

	// Extended MusicBrainz properties

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzRecordingId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzRecordingId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzDiscId);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzDiscId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseStatus);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseStatus, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseType);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseType, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => GetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseCountry);
		set => SetFreeform (Mp4AtomMapping.MusicBrainzNamespace, Mp4AtomMapping.MusicBrainzReleaseCountry, value);
	}

	// DJ and remix properties

	/// <inheritdoc/>
	public override string? InitialKey {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.InitialKey);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.InitialKey, value);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Remixer);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Remixer, value);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Mood);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Mood, value);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Subtitle);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Subtitle, value);
	}

	// Collector properties

	/// <inheritdoc/>
	public override string? Barcode {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Barcode);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Barcode, value);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.CatalogNumber);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.CatalogNumber, value);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.AmazonId);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.AmazonId, value);
	}

	// Library management properties

	/// <inheritdoc/>
	public override string? DateTagged {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.DateTagged);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.DateTagged, value);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Language);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Language, value);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.MediaType);
		set => SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.MediaType, value);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the standard "purl" podcast URL atom.
	/// </remarks>
	public override string? PodcastFeedUrl {
		get => GetText (Mp4AtomMapping.PodcastUrl);
		set => SetText (Mp4AtomMapping.PodcastUrl, value);
	}

	/// <summary>
	/// Gets or sets whether this file is a podcast episode.
	/// </summary>
	/// <remarks>
	/// Uses the "pcst" podcast flag atom. When true, indicates this file
	/// should be treated as a podcast episode in media players.
	/// </remarks>
	public bool IsPodcast {
		get => GetBoolean (Mp4AtomMapping.PodcastFlag);
		set => SetBoolean (Mp4AtomMapping.PodcastFlag, value);
	}

	/// <summary>
	/// Gets or sets the podcast episode GUID.
	/// </summary>
	/// <remarks>
	/// Uses the "egid" atom. A globally unique identifier for this episode,
	/// typically matching the GUID from the podcast RSS feed.
	/// </remarks>
	public string? PodcastEpisodeGuid {
		get => GetText (Mp4AtomMapping.PodcastGuid);
		set => SetText (Mp4AtomMapping.PodcastGuid, value);
	}

	/// <summary>
	/// Gets or sets the podcast category.
	/// </summary>
	/// <remarks>
	/// Uses the "catg" atom. The category or genre classification
	/// for this podcast (e.g., "Technology", "Comedy", "News").
	/// </remarks>
	public string? PodcastCategory {
		get => GetText (Mp4AtomMapping.Category);
		set => SetText (Mp4AtomMapping.Category, value);
	}

	/// <summary>
	/// Gets or sets the podcast keywords.
	/// </summary>
	/// <remarks>
	/// Uses the "keyw" atom. Comma-separated keywords for podcast search
	/// and discovery (e.g., "programming, coding, tech").
	/// </remarks>
	public string? PodcastKeywords {
		get => GetText (Mp4AtomMapping.Keywords);
		set => SetText (Mp4AtomMapping.Keywords, value);
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override IPicture[] Pictures {
		get => [.. _pictures];
		set {
			_pictures.Clear ();
			DropPreserved (Mp4AtomMapping.CoverArt);
			if (value is not null) {
				foreach (var pic in value) {
					if (pic is Mp4Picture mp4Pic)
						_pictures.Add (mp4Pic);
					else
						_pictures.Add (new Mp4Picture (pic.MimeType, pic.PictureType, pic.Description, pic.PictureData));
				}
			}
		}
	}
#pragma warning restore CA1819

	string? GetGnreName ()
	{
		var code = GetInteger (Mp4AtomMapping.GenreId3);
		if (code is not uint value || value == 0)
			return null;

		var index = value - 1;
		if (index > byte.MaxValue)
			return null;

		return Id3v1Genre.GetName ((byte)index);
	}

	string? GetText (string atomId)
	{
		if (!_atoms.TryGetValue (atomId, out var dataAtoms) || dataAtoms.Count == 0)
			return null;

		// If there's only one value, return it directly
		if (dataAtoms.Count == 1)
			return dataAtoms[0].ToUtf8String ();

		// Multiple values: join with separator (common for multiple artists, genres, etc.)
		var values = new List<string> (dataAtoms.Count);
		for (var i = 0; i < dataAtoms.Count; i++) {
			var text = dataAtoms[i].ToUtf8String ();
			if (!string.IsNullOrEmpty (text))
				values.Add (text!);
		}
		return values.Count > 0 ? string.Join ("; ", values) : null;
	}

	/// <summary>
	/// Gets all text values for an atom (for multi-value fields).
	/// </summary>
	IReadOnlyList<string> GetTexts (string atomId)
	{
		if (!_atoms.TryGetValue (atomId, out var dataAtoms) || dataAtoms.Count == 0)
			return Array.Empty<string> ();

		var values = new List<string> (dataAtoms.Count);
		for (var i = 0; i < dataAtoms.Count; i++) {
			var text = dataAtoms[i].ToUtf8String ();
			if (!string.IsNullOrEmpty (text))
				values.Add (text!);
		}
		return values;
	}

	void SetText (string atomId, string? value)
	{
		if (string.IsNullOrEmpty (value)) {
			RemoveAtom (atomId);
			return;
		}

		var dataAtom = new Mp4DataAtom (Mp4AtomMapping.TypeUtf8, BinaryData.FromStringUtf8 (value!));
		SetAtoms (atomId, new List<Mp4DataAtom> { dataAtom });
	}

	uint? GetInteger (string atomId)
	{
		if (!_atoms.TryGetValue (atomId, out var dataAtoms) || dataAtoms.Count == 0)
			return null;

		return dataAtoms[0].ToUInt32 ();
	}

	void SetInteger (string atomId, uint? value)
	{
		if (!value.HasValue) {
			RemoveAtom (atomId);
			return;
		}

		var dataAtom = new Mp4DataAtom (Mp4AtomMapping.TypeInteger, BinaryData.FromUInt32BE (value.Value));
		SetAtoms (atomId, new List<Mp4DataAtom> { dataAtom });
	}

	bool GetBoolean (string atomId)
	{
		if (!_atoms.TryGetValue (atomId, out var dataAtoms) || dataAtoms.Count == 0)
			return false;

		return dataAtoms[0].ToBoolean ();
	}

	void SetBoolean (string atomId, bool value)
	{
		if (!value) {
			RemoveAtom (atomId);
			return;
		}

		var dataAtom = new Mp4DataAtom (Mp4AtomMapping.TypeInteger, new BinaryData ([1]));
		SetAtoms (atomId, new List<Mp4DataAtom> { dataAtom });
	}

	bool GetTrackDisc (string atomId, out uint number, out uint total)
	{
		number = 0;
		total = 0;

		if (!_atoms.TryGetValue (atomId, out var dataAtoms) || dataAtoms.Count == 0)
			return false;

		return dataAtoms[0].TryParseTrackDisc (out number, out total);
	}

	void SetTrackDisc (string atomId, uint number, uint total)
	{
		var data = new byte[8];
		data[2] = (byte)((number >> 8) & 0xFF);
		data[3] = (byte)(number & 0xFF);
		data[4] = (byte)((total >> 8) & 0xFF);
		data[5] = (byte)(total & 0xFF);

		var dataAtom = new Mp4DataAtom (Mp4AtomMapping.TypeBinary, new BinaryData (data));
		SetAtoms (atomId, new List<Mp4DataAtom> { dataAtom });
	}

	internal string? GetFreeform (string mean, string name)
	{
		var field = FindFreeform (mean, name);
		if (field is null || field.Atoms.Count == 0)
			return null;

		return field.Atoms[0].ToUtf8String ();
	}

	internal void SetFreeform (string mean, string name, string? value)
	{
		if (string.IsNullOrEmpty (value)) {
			SetFreeformAtoms (mean, name, new List<Mp4DataAtom> ());
			return;
		}

		var dataAtom = new Mp4DataAtom (Mp4AtomMapping.TypeUtf8, BinaryData.FromStringUtf8 (value!));
		SetFreeformAtoms (mean, name, new List<Mp4DataAtom> { dataAtom });
	}

	internal void SetFreeformAtoms (string mean, string name, List<Mp4DataAtom> dataAtoms)
	{
		var key = (mean, name);
		if (_freeformByKey.TryGetValue (key, out var existing)) {
			_freeform.Remove (existing);
			_freeformByKey.Remove (key);
		}

		DropPreservedFreeform (mean, name);
		if (dataAtoms is null || dataAtoms.Count == 0)
			return;

		_freeformByKey.Add (key, _freeform.AddLast (new FreeformField (mean, name, dataAtoms)));
	}

	FreeformField? FindFreeform (string mean, string name) =>
		_freeformByKey.TryGetValue ((mean, name), out var node) ? node.Value : null;

	void RemoveAtom (string atomId)
	{
		_atoms.Remove (atomId);
		DropPreserved (atomId);
	}

	/// <inheritdoc/>
	public override void Clear ()
	{
		_atoms.Clear ();
		_pictures.Clear ();
		_freeform.Clear ();
		_freeformByKey.Clear ();
		_preservedAtoms.Clear ();
		_preservedByType.Clear ();
		_preservedFreeformByKey.Clear ();
	}

	/// <inheritdoc/>
	public override BinaryData Render ()
	{
		using var builder = new BinaryDataBuilder ();

		foreach (var kvp in _atoms)
			RenderStandardAtom (builder, kvp.Key, kvp.Value);

		foreach (var field in _freeform)
			RenderFreeformAtom (builder, field.Mean, field.Name, field.Atoms);

		if (_pictures.Count > 0)
			RenderPictures (builder);

		foreach (var preserved in _preservedAtoms) {
			if (preserved is not null)
				builder.Add (preserved.Box);
		}

		return builder.ToBinaryData ();
	}

	static void RenderStandardAtom (BinaryDataBuilder builder, string atomId, List<Mp4DataAtom> dataAtoms)
	{
		var dataSize = 0;
		foreach (var dataAtom in dataAtoms)
			dataSize += 8 + 8 + dataAtom.Data.Length;

		var totalSize = 8 + dataSize;

		builder.Add (BinaryData.FromUInt32BE ((uint)totalSize));
		builder.Add (BinaryData.FromStringLatin1 (atomId.PadRight (4).Substring (0, 4)));

		foreach (var dataAtom in dataAtoms) {
			// size = header(8) + version+flags(4) + locale(4) + data
			var dataAtomSize = 8 + 8 + dataAtom.Data.Length;
			builder.Add (BinaryData.FromUInt32BE ((uint)dataAtomSize));
			builder.Add (BinaryData.FromStringLatin1 ("data"));

			builder.Add ((byte)0);
			builder.Add ((byte)((dataAtom.TypeIndicator >> 16) & 0xFF));
			builder.Add ((byte)((dataAtom.TypeIndicator >> 8) & 0xFF));
			builder.Add ((byte)(dataAtom.TypeIndicator & 0xFF));

			builder.Add (BinaryData.FromUInt32BE (dataAtom.Locale));

			builder.Add (dataAtom.Data);
		}
	}

	void RenderPictures (BinaryDataBuilder builder)
	{
		var payloads = new BinaryData[_pictures.Count];
		var dataSize = 0;
		for (var i = 0; i < _pictures.Count; i++) {
			payloads[i] = Mp4DataAtom.CreateImage (_pictures[i].PictureData, _pictures[i].RenderTypeIndicator, _pictures[i].RenderLocale);
			dataSize += 8 + payloads[i].Length;
		}

		builder.Add (BinaryData.FromUInt32BE ((uint)(8 + dataSize)));
		builder.Add (BinaryData.FromStringLatin1 (Mp4AtomMapping.CoverArt));

		foreach (var payload in payloads) {
			builder.Add (BinaryData.FromUInt32BE ((uint)(8 + payload.Length)));
			builder.Add (BinaryData.FromStringLatin1 ("data"));
			builder.Add (payload);
		}
	}

	static void RenderFreeformAtom (BinaryDataBuilder builder, string mean, string name, List<Mp4DataAtom> dataAtoms)
	{
		var meanBytes = System.Text.Encoding.UTF8.GetBytes (mean);
		var nameBytes = System.Text.Encoding.UTF8.GetBytes (name);

		var meanAtomSize = 8 + 4 + meanBytes.Length;
		var nameAtomSize = 8 + 4 + nameBytes.Length;

		var dataSize = 0;
		foreach (var dataAtom in dataAtoms)
			dataSize += 8 + 8 + dataAtom.Data.Length;

		var totalSize = 8 + meanAtomSize + nameAtomSize + dataSize;

		builder.Add (BinaryData.FromUInt32BE ((uint)totalSize));
		builder.Add (BinaryData.FromStringLatin1 ("----"));

		builder.Add (BinaryData.FromUInt32BE ((uint)meanAtomSize));
		builder.Add (BinaryData.FromStringLatin1 ("mean"));
		builder.Add (BinaryData.FromUInt32BE (0));
		builder.Add (new BinaryData (meanBytes));

		builder.Add (BinaryData.FromUInt32BE ((uint)nameAtomSize));
		builder.Add (BinaryData.FromStringLatin1 ("name"));
		builder.Add (BinaryData.FromUInt32BE (0));
		builder.Add (new BinaryData (nameBytes));

		foreach (var dataAtom in dataAtoms) {
			// size = header(8) + version+flags(4) + locale(4) + data
			var dataAtomSize = 8 + 8 + dataAtom.Data.Length;
			builder.Add (BinaryData.FromUInt32BE ((uint)dataAtomSize));
			builder.Add (BinaryData.FromStringLatin1 ("data"));

			builder.Add ((byte)0);
			builder.Add ((byte)((dataAtom.TypeIndicator >> 16) & 0xFF));
			builder.Add ((byte)((dataAtom.TypeIndicator >> 8) & 0xFF));
			builder.Add ((byte)(dataAtom.TypeIndicator & 0xFF));

			builder.Add (BinaryData.FromUInt32BE (dataAtom.Locale));

			builder.Add (dataAtom.Data);
		}
	}

	internal void SetAtoms (string atomId, List<Mp4DataAtom> dataAtoms)
	{
		if (dataAtoms is null || dataAtoms.Count == 0) {
			RemoveAtom (atomId);
			return;
		}

		DropPreserved (atomId);
		_atoms[atomId] = dataAtoms;
	}

	internal void PreserveIlstAtom (BinaryData box, string? mean = null, string? name = null)
	{
		if (box.IsEmpty)
			return;

		var typeKey = TypeKey (box.Span);
		var index = _preservedAtoms.Count;
		_preservedAtoms.Add (new PreservedBox (box));
		if (!_preservedByType.TryGetValue (typeKey, out var indexes)) {
			indexes = new List<int> ();
			_preservedByType.Add (typeKey, indexes);
		}

		indexes.Add (index);

		if (mean is null || name is null)
			return;

		if (!_preservedFreeformByKey.TryGetValue ((mean, name), out var twins)) {
			twins = new List<int> ();
			_preservedFreeformByKey.Add ((mean, name), twins);
		}

		twins.Add (index);
	}

	void DropPreserved (string atomId)
	{
		// Freeform keys are longer than 4 characters and are matched by mean and name.
		if (atomId.Length != 4)
			return;

		var typeKey = TypeKey (atomId);
		if (!_preservedByType.TryGetValue (typeKey, out var indexes))
			return;

		foreach (var index in indexes)
			_preservedAtoms[index] = null;

		_preservedByType.Remove (typeKey);
	}

	void DropPreservedFreeform (string mean, string name)
	{
		// The ---- entries in _preservedByType keep these indexes; nulled slots are skipped on render.
		if (!_preservedFreeformByKey.TryGetValue ((mean, name), out var twins))
			return;

		foreach (var index in twins)
			_preservedAtoms[index] = null;

		_preservedFreeformByKey.Remove ((mean, name));
	}

	static uint TypeKey (string atomId) =>
		((uint)(byte)atomId[0] << 24) |
		((uint)(byte)atomId[1] << 16) |
		((uint)(byte)atomId[2] << 8) |
		(byte)atomId[3];

	static uint TypeKey (ReadOnlySpan<byte> box)
	{
		if (box.Length < 8)
			return 0;

		return BinaryPrimitives.ReadUInt32BigEndian (box.Slice (4, 4));
	}

	internal IReadOnlyList<Mp4Picture> GetPictureList () => _pictures;

	sealed class FreeformField
	{
		public FreeformField (string mean, string name, List<Mp4DataAtom> atoms)
		{
			Mean = mean;
			Name = name;
			Atoms = atoms;
		}

		public string Mean { get; }

		public string Name { get; }

		public List<Mp4DataAtom> Atoms { get; }
	}

	sealed class PreservedBox
	{
		public PreservedBox (BinaryData box)
		{
			Box = box;
		}

		public BinaryData Box { get; }
	}
}
