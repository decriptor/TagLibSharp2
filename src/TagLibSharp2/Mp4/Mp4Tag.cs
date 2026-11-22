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

	// Set when the ilst belongs to a QuickTime meta whose handler is "mdta". Items are then typed
	// by key index, so a field remembers the key it was read from and a new field gets a key.
	Mp4QuickTimeKeys? _keys;
	bool _cleared;
	readonly Dictionary<string, uint> _keyIndexByField = new (StringComparer.Ordinal);

	// Items a field was read from when they are kept byte for byte: every item of an "mdta" meta
	// that names a field (keyed, iTunes-typed, or with a value the reader rejects), and the items
	// of a second meta (moov/meta beside udta/meta). While a field's value comes from them, an
	// unedited save writes them back as they were; setting the field drops all of them.
	readonly Dictionary<string, List<(bool Movie, int Index)>> _sources = new (StringComparer.Ordinal);
	readonly HashSet<string> _valueFromSources = new (StringComparer.Ordinal);
	readonly Dictionary<string, Mp4SourceRank> _valueRank = new (StringComparer.Ordinal);
	readonly Dictionary<string, int> _slotByField = new (StringComparer.Ordinal);

	// Items of a second meta (moov/meta beside udta/meta), in file order. Null marks one a set
	// dropped. A field whose value came from it is written back there when set: QTFF gives
	// metadata precedence over user data that declares the same information.
	readonly List<BinaryData?> _movieItems = new ();
	readonly HashSet<string> _movieFields = new (StringComparer.Ordinal);
	readonly Dictionary<string, int> _movieSlotByField = new (StringComparer.Ordinal);
	readonly Dictionary<string, uint> _movieKeyIndexByField = new (StringComparer.Ordinal);
	Mp4QuickTimeKeys? _movieKeys;
	bool _hasMovieMeta;

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Apple;

	/// <summary>
	/// The keys of an "mdta" meta, or null for an iTunes (mdir) ilst.
	/// </summary>
	internal Mp4QuickTimeKeys? QuickTimeKeys => _keys;

	/// <summary>
	/// Makes this tag render its ilst as items typed by key index.
	/// </summary>
	internal void UseQuickTimeKeys (Mp4QuickTimeKeys keys) => _keys = keys;

	/// <summary>
	/// True once the tag has been cleared, so a save drops a keys atom the tag could not read.
	/// </summary>
	internal bool DropsUnreadKeys => _cleared;

	/// <inheritdoc/>
	public override string? Title {
		get => GetText (Mp4AtomMapping.Title) ?? GetText (Mp4AtomMapping.TitleAlias);
		set => SetTextDroppingAlias (Mp4AtomMapping.Title, Mp4AtomMapping.TitleAlias, value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetText (Mp4AtomMapping.Artist);
		set => SetText (Mp4AtomMapping.Artist, value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => GetText (Mp4AtomMapping.Album) ?? GetText (Mp4AtomMapping.AlbumAlias);
		set => SetTextDroppingAlias (Mp4AtomMapping.Album, Mp4AtomMapping.AlbumAlias, value);
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
		get => GetText (Mp4AtomMapping.Composer) ?? GetText (Mp4AtomMapping.ComposerAlias);
		set => SetTextDroppingAlias (Mp4AtomMapping.Composer, Mp4AtomMapping.ComposerAlias, value);
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
		get => GetText (Mp4AtomMapping.Encoder) ?? GetText (Mp4AtomMapping.EncoderAlias);
		set => SetTextDroppingAlias (Mp4AtomMapping.Encoder, Mp4AtomMapping.EncoderAlias, value);
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
		get => GetText (Mp4AtomMapping.Description) ?? GetText (Mp4AtomMapping.DescriptionAlias);
		set => SetTextDroppingAlias (Mp4AtomMapping.Description, Mp4AtomMapping.DescriptionAlias, value);
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
		get => GetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Subtitle) ?? GetText (Mp4AtomMapping.SubtitleAlias);
		set {
			SetFreeform (Mp4AtomMapping.AppleNamespace, Mp4AtomMapping.Subtitle, value);
			RemoveAtom (Mp4AtomMapping.SubtitleAlias);
		}
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

	/// <summary>
	/// Gets or sets the narrator.
	/// </summary>
	/// <remarks>
	/// Uses the "©nrt" atom that Audible writes in .aax audiobooks. ExifTool reports it as Narrator.
	/// </remarks>
	public string? Narrator {
		get => GetText (Mp4AtomMapping.Narrator);
		set => SetText (Mp4AtomMapping.Narrator, value);
	}

	/// <summary>
	/// Gets or sets the short title.
	/// </summary>
	/// <remarks>
	/// Uses the "@sti" atom that Audible writes in .aax audiobooks. ExifTool reports it as ShortTitle.
	/// </remarks>
	public string? ShortTitle {
		get => GetText (Mp4AtomMapping.AudibleShortTitle);
		set => SetText (Mp4AtomMapping.AudibleShortTitle, value);
	}

	/// <summary>
	/// Gets or sets the parent (series or collection) title.
	/// </summary>
	/// <remarks>
	/// Uses the "@pti" atom that Audible writes in .aax audiobooks. ExifTool reports it as ParentTitle.
	/// </remarks>
	public string? ParentTitle {
		get => GetText (Mp4AtomMapping.AudibleParentTitle);
		set => SetText (Mp4AtomMapping.AudibleParentTitle, value);
	}

	/// <summary>
	/// Gets or sets the parent short title.
	/// </summary>
	/// <remarks>
	/// Uses the "@PST" atom that Audible writes in .aax audiobooks. ExifTool reports it as ParentShortTitle.
	/// </remarks>
	public string? ParentShortTitle {
		get => GetText (Mp4AtomMapping.AudibleParentShortTitle);
		set => SetText (Mp4AtomMapping.AudibleParentShortTitle, value);
	}

	/// <summary>
	/// Gets or sets the parent product identifier.
	/// </summary>
	/// <remarks>
	/// Uses the "@ppi" atom that Audible writes in .aax audiobooks. ExifTool reports it as ParentProductID.
	/// </remarks>
	public string? ParentProductId {
		get => GetText (Mp4AtomMapping.AudibleParentProductId);
		set => SetText (Mp4AtomMapping.AudibleParentProductId, value);
	}

	/// <summary>
	/// Gets or sets the product identifier.
	/// </summary>
	/// <remarks>
	/// Uses the "prID" atom that Audible writes in .aax audiobooks. ExifTool reports it as ProductID.
	/// </remarks>
	public string? ProductId {
		get => GetText (Mp4AtomMapping.AudibleProductId);
		set => SetText (Mp4AtomMapping.AudibleProductId, value);
	}

	/// <summary>
	/// Gets or sets the release date, as stored (for example <c>02-Jul-2012</c>).
	/// </summary>
	/// <remarks>
	/// Uses the "rldt" atom that Audible writes in .aax audiobooks. ExifTool reports it as ReleaseDate.
	/// </remarks>
	public string? ReleaseDate {
		get => GetText (Mp4AtomMapping.AudibleReleaseDate);
		set => SetText (Mp4AtomMapping.AudibleReleaseDate, value);
	}

	/// <summary>
	/// Gets or sets the product version.
	/// </summary>
	/// <remarks>
	/// Uses the "VERS" atom that Audible writes in .aax audiobooks. ExifTool reports it as ProductVersion.
	/// </remarks>
	public string? ProductVersion {
		get => GetText (Mp4AtomMapping.AudibleProductVersion);
		set => SetText (Mp4AtomMapping.AudibleProductVersion, value);
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

	// The setter removes every atom the getter reads, so readers that list them all see one value.
	void SetTextDroppingAlias (string atomId, string alias, string? value)
	{
		SetText (atomId, value);
		RemoveAtom (alias);
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
		DropFreeformTwins (mean, name);
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
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	/// <remarks>
	/// Reads the freeform <c>----</c> field whose <c>mean</c> is <c>com.apple.iTunes</c> and whose <c>name</c>
	/// equals <paramref name="name"/> exactly, in the iTunes <c>ilst</c> or a QuickTime <c>mdta</c> meta. Each
	/// <c>data</c> atom of type 1 (UTF-8) or type 0 (implicit, read as UTF-8 as <see cref="Mp4File.GetFreeformTag"/>
	/// reads it) is one value; other types are not text (Apple, QuickTime File Format, "Well-known types"). The name
	/// is the whole name: <c>"a:b"</c> is a name with a colon, not a mean and a name. Use
	/// <see cref="Mp4File.GetFreeformTag"/> for another mean.
	/// </remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!CustomFieldValues.IsUsableName (name) || FindFreeform (Mp4AtomMapping.AppleNamespace, name) is not { } field)
			return CustomFieldValues.None;

		var values = new List<string> (field.Atoms.Count);
		foreach (var dataAtom in field.Atoms) {
			if (dataAtom.TypeIndicator is not (Mp4AtomMapping.TypeBinary or Mp4AtomMapping.TypeUtf8))
				continue;
			var text = dataAtom.ToUtf8String ();
			if (!string.IsNullOrEmpty (text))
				values.Add (text!);
		}
		return values;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Writes one freeform <c>----</c> field with <c>mean</c> <c>com.apple.iTunes</c>, <c>name</c>
	/// <paramref name="name"/>, and one UTF-8 <c>data</c> atom per value, the layout iTunes uses for its own
	/// <c>com.apple.iTunes</c> names. It goes where the tag's freeform properties go: the iTunes <c>ilst</c>, or the
	/// keyed <c>mdta</c> meta the tag was read from, and replaces every item the name was read from.
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!CustomFieldValues.IsUsableName (name) || !CustomFieldValues.TryNormalize (values, out var kept))
			return false;

		var dataAtoms = new List<Mp4DataAtom> (kept.Count);
		foreach (var value in kept)
			dataAtoms.Add (new Mp4DataAtom (Mp4AtomMapping.TypeUtf8, BinaryData.FromStringUtf8 (value)));
		SetFreeformAtoms (Mp4AtomMapping.AppleNamespace, name, dataAtoms);
		return true;
	}

	/// <inheritdoc/>
	/// <remarks>Removes the field and every item it was read from, in either meta.</remarks>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!CustomFieldValues.IsUsableName (name))
			return false;

		const string mean = Mp4AtomMapping.AppleNamespace;
		var held = FindFreeform (mean, name) is not null ||
			_preservedFreeformByKey.ContainsKey ((mean, name)) ||
			_sources.ContainsKey (FreeformId (mean, name));
		SetFreeformAtoms (mean, name, new List<Mp4DataAtom> ());
		return held;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Removes all metadata the tag holds: every field, every item it was read from, and every item
	/// it does not map, in both metas. That includes unknown iTunes atoms (such as an iTunes Store
	/// purchase's account and IDs) and Apple's make, model, location and creation date keys, whose
	/// keys go too, as does a keys atom that could not be read. It also removes the iTunes gapless
	/// info (iTunSMPB, <see cref="GaplessInfo"/>): a file without an edit list then has no record
	/// of its encoder priming and padding, and players that rely on it play those samples.
	/// </remarks>
	public override void Clear ()
	{
		_cleared = true;
		_atoms.Clear ();
		_pictures.Clear ();
		_freeform.Clear ();
		_freeformByKey.Clear ();
		_preservedAtoms.Clear ();
		_preservedByType.Clear ();
		_preservedFreeformByKey.Clear ();
		_keys?.Clear ();
		_keyIndexByField.Clear ();
		_sources.Clear ();
		_valueFromSources.Clear ();
		_valueRank.Clear ();
		_slotByField.Clear ();
		_movieItems.Clear ();
		_movieFields.Clear ();
		_movieSlotByField.Clear ();
		_movieKeyIndexByField.Clear ();
		_movieKeys?.Clear ();
	}

	/// <summary>
	/// Removes every field and every item a field was read from, including a malformed atom of a
	/// mapped field, before an assigned tag's values are taken. Items the tag does not map, such
	/// as Apple's make, model, location and creation date keys or an unknown atom like "orch",
	/// are kept with the keys they use.
	/// </summary>
	internal void ClearMappedFields ()
	{
		foreach (var fieldId in new List<string> (_sources.Keys))
			DropSources (fieldId);

		_atoms.Clear ();
		_pictures.Clear ();
		_freeform.Clear ();
		_freeformByKey.Clear ();
		for (var i = 0; i < _preservedAtoms.Count; i++) {
			if (_preservedAtoms[i] is { } kept && IsMappedType (kept.Box.Span))
				_preservedAtoms[i] = null;
		}

		_preservedFreeformByKey.Clear ();
		_valueFromSources.Clear ();
		_valueRank.Clear ();
		_movieFields.Clear ();
	}

	static bool IsMappedType (ReadOnlySpan<byte> box)
	{
		if (box.Length < 8)
			return false;

		var type = Polyfills.Latin1.GetString (box.Slice (4, 4).ToArray ());
		return type == Mp4AtomMapping.FreeformAtom || Mp4AtomMapping.IsTagAtom (type);
	}

	/// <summary>
	/// Takes every value of another tag through the setters, so each replaces this tag's items for
	/// that field. An iTunes tag's own unknown atoms come along only into an iTunes tag (replacing
	/// atoms of the same type): a keyed meta takes no iTunes atom, and a keyed tag's items mean
	/// nothing without its keys.
	/// </summary>
	internal void TakeValuesFrom (Mp4Tag other)
	{
		foreach (var field in other.ValuedFields ()) {
			switch (field.Field.Kind) {
				case Mp4KeyedKind.Atom:
					SetAtoms (field.Id, new List<Mp4DataAtom> (field.Atoms));
					break;
				case Mp4KeyedKind.Freeform:
					SetFreeformAtoms (field.Field.Mean!, field.Field.Name!, new List<Mp4DataAtom> (field.Atoms));
					break;
				default:
					Pictures = [.. other._pictures];
					break;
			}
		}

		if (_keys is not null || other._keys is not null)
			return;

		foreach (var box in other.KeptUnknownAtoms ())
			DropPreserved (TypeKey (box.Span));
		foreach (var box in other.KeptUnknownAtoms ())
			PreserveIlstAtom (box);
	}

	// Kept atoms of an iTunes ilst that are neither a field's item nor of a mapped type.
	IEnumerable<BinaryData> KeptUnknownAtoms ()
	{
		var fieldItems = new HashSet<int> ();
		foreach (var list in _sources.Values) {
			foreach (var (movie, index) in list) {
				if (!movie)
					fieldItems.Add (index);
			}
		}

		for (var i = 0; i < _preservedAtoms.Count; i++) {
			if (_preservedAtoms[i] is { } kept && !fieldItems.Contains (i) && !IsMappedType (kept.Box.Span))
				yield return kept.Box;
		}
	}

	/// <inheritdoc/>
	public override BinaryData Render ()
	{
		if (_keys is not null)
			return RenderKeyed (_keys);

		using var builder = new BinaryDataBuilder ();

		// A field whose value still comes from kept items is not written here. A field set after it
		// was read from a second meta is written both there and here, so every reader shows it.
		foreach (var field in ValuedFields ()) {
			if (!_valueFromSources.Contains (field.Id))
				builder.Add (field.Itunes ());
		}

		foreach (var preserved in _preservedAtoms) {
			if (preserved is not null)
				builder.Add (preserved.Box);
		}

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Converts a keyed item's data to the iTunes form of its field. A text key's number or flag is
	/// read from its text, and a text field takes only UTF-8 values (value type 1).
	/// </summary>
	/// <returns>Empty when no data is a value of that field.</returns>
	internal static List<Mp4DataAtom> FromKeyed (string atomId, List<Mp4DataAtom> dataAtoms, bool textKey)
	{
		if (!textKey)
			return dataAtoms;
		if (IsTextKeyedNumber (atomId))
			return FromKeyedText (atomId, dataAtoms);
		return dataAtoms.FindAll (atom => atom.TypeIndicator == Mp4AtomMapping.TypeUtf8);
	}

	/// <summary>
	/// True when the tag has read a second meta (moov/meta beside udta/meta).
	/// </summary>
	internal bool HasMovieMeta => _hasMovieMeta;

	internal void UseMovieMeta (Mp4QuickTimeKeys? keys)
	{
		_hasMovieMeta = true;
		_movieKeys = keys;
	}

	/// <summary>
	/// The second meta's keys, including any a render added, or null when it has no keys atom.
	/// </summary>
	internal BinaryData? RenderMovieKeys () => _movieKeys?.Render ();

	/// <summary>
	/// A copy holding this tag's values and its kept iTunes atoms, without where they were read
	/// from (keys, kept items, a second meta). It is what an assigned tag is saved as in a file
	/// that had no tag, where it is written as a fresh tag.
	/// </summary>
	internal Mp4Tag Detached ()
	{
		var copy = new Mp4Tag ();
		copy.TakeValuesFrom (this);
		return copy;
	}

	/// <summary>
	/// Keeps an item of the second meta that names no field.
	/// </summary>
	internal void AddMovieItem (BinaryData box) => _movieItems.Add (box);

	/// <summary>
	/// Records an item that names a field, keeping it byte for byte. <paramref name="value"/> is
	/// null when the reader rejects it. The field's value is taken by rank: an iTunes atom beats a
	/// keyed item (the last iTunes atom wins), the first keyed item beats the rest, and the second
	/// meta (moov/meta) beats both for the fields it declares.
	/// </summary>
	internal void AddSourceAtom (string atomId, List<Mp4DataAtom>? value, BinaryData box, uint keyIndex, Mp4SourceRank rank, bool movie)
	{
		if (!AddSource (atomId, box, keyIndex, movie) || value is null || value.Count == 0 || !TakesValue (atomId, _atoms.ContainsKey (atomId), rank))
			return;

		_atoms[atomId] = value;
		TookValue (atomId, rank, movie);
	}

	internal void AddSourceFreeform (string mean, string name, List<Mp4DataAtom>? value, BinaryData box, uint keyIndex, Mp4SourceRank rank, bool movie)
	{
		var id = FreeformId (mean, name);
		if (!AddSource (id, box, keyIndex, movie) || value is null || value.Count == 0 || !TakesValue (id, FindFreeform (mean, name) is not null, rank))
			return;

		if (_freeformByKey.TryGetValue ((mean, name), out var existing)) {
			_freeform.Remove (existing);
			_freeformByKey.Remove ((mean, name));
		}

		_freeformByKey.Add ((mean, name), _freeform.AddLast (new FreeformField (mean, name, value)));
		TookValue (id, rank, movie);
	}

	internal void AddSourcePictures (Mp4Picture[]? value, BinaryData box, uint keyIndex, Mp4SourceRank rank, bool movie)
	{
		var id = Mp4AtomMapping.CoverArt;
		if (!AddSource (id, box, keyIndex, movie) || value is null || value.Length == 0 || !TakesValue (id, _pictures.Count > 0, rank))
			return;

		_pictures.Clear ();
		_pictures.AddRange (value);
		TookValue (id, rank, movie);
	}

	bool AddSource (string fieldId, BinaryData box, uint keyIndex, bool movie)
	{
		if (box.IsEmpty)
			return false;

		int index;
		if (movie) {
			index = _movieItems.Count;
			_movieItems.Add (box);
			if (!_movieSlotByField.ContainsKey (fieldId))
				_movieSlotByField[fieldId] = index;
			if (keyIndex != 0 && !_movieKeyIndexByField.ContainsKey (fieldId))
				_movieKeyIndexByField[fieldId] = keyIndex;
		} else {
			index = PreserveIlstAtom (box);
			if (!_slotByField.ContainsKey (fieldId))
				_slotByField[fieldId] = index;
			if (keyIndex != 0 && !_keyIndexByField.ContainsKey (fieldId))
				_keyIndexByField[fieldId] = keyIndex;
		}

		if (!_sources.TryGetValue (fieldId, out var list)) {
			list = new List<(bool, int)> ();
			_sources[fieldId] = list;
		}

		list.Add ((movie, index));
		return true;
	}

	bool TakesValue (string fieldId, bool hasValue, Mp4SourceRank rank)
	{
		if (!hasValue)
			return true;

		// A value read the ordinary way (an iTunes ilst) ranks as an iTunes atom.
		var current = _valueRank.TryGetValue (fieldId, out var known) ? known : Mp4SourceRank.Atom;
		var takes = rank switch {
			Mp4SourceRank.Atom => current <= Mp4SourceRank.Atom,
			Mp4SourceRank.MovieAtom or Mp4SourceRank.MovieKeyed => current < rank,
			_ => false
		};

		// The value being replaced is kept as an item, so an unedited save still writes it.
		if (takes && !_valueFromSources.Contains (fieldId))
			KeepCurrentValue (fieldId);
		return takes;
	}

	void TookValue (string fieldId, Mp4SourceRank rank, bool movie)
	{
		_valueRank[fieldId] = rank;
		_valueFromSources.Add (fieldId);
		if (movie)
			_movieFields.Add (fieldId);
		else
			_movieFields.Remove (fieldId);
	}

	void KeepCurrentValue (string fieldId)
	{
		foreach (var field in ValuedFields ()) {
			if (field.Id != fieldId)
				continue;

			var index = PreserveIlstAtom (field.Itunes (), field.Field.Mean, field.Field.Name);
			if (!_slotByField.ContainsKey (fieldId))
				_slotByField[fieldId] = index;
			if (!_sources.TryGetValue (fieldId, out var list)) {
				list = new List<(bool, int)> ();
				_sources[fieldId] = list;
			}

			list.Add ((false, index));
			return;
		}
	}

	// A set or a removal replaces every item the field was read from.
	void DropSources (string fieldId)
	{
		_valueFromSources.Remove (fieldId);
		_valueRank.Remove (fieldId);
		if (!_sources.TryGetValue (fieldId, out var list))
			return;

		_sources.Remove (fieldId);
		foreach (var (movie, index) in list) {
			if (movie)
				_movieItems[index] = null;
			else
				_preservedAtoms[index] = null;
		}
	}

	/// <summary>
	/// Renders the second meta's ilst: its items as they were, less those a set dropped, and the
	/// fields that live there and were set, where their first item was.
	/// </summary>
	internal BinaryData RenderMovieIlst () =>
		RenderOrdered (_movieItems, _movieKeys, _movieSlotByField, _movieKeyIndexByField, movie: true);

	// Every field that has a value, with its iTunes form and its keyed form.
	IEnumerable<(string Id, Mp4KeyedField Field, List<Mp4DataAtom> Atoms, Func<BinaryData> Itunes)> ValuedFields ()
	{
		foreach (var kvp in _atoms) {
			var (atomId, atoms) = (kvp.Key, kvp.Value);
			yield return (atomId, Mp4KeyedField.Atom (atomId), atoms, () => Rendered (b => RenderStandardAtom (b, atomId, atoms)));
		}

		foreach (var field in _freeform) {
			var f = field;
			yield return (FreeformId (f.Mean, f.Name), Mp4KeyedField.Freeform (f.Mean, f.Name), f.Atoms,
				() => Rendered (b => RenderFreeformAtom (b, f.Mean, f.Name, f.Atoms)));
		}

		if (_pictures.Count > 0) {
			var covers = new List<Mp4DataAtom> (_pictures.Count);
			foreach (var picture in _pictures)
				covers.Add (new Mp4DataAtom (picture.RenderTypeIndicator, picture.PictureData, picture.RenderLocale));
			yield return (Mp4AtomMapping.CoverArt, Mp4KeyedField.Artwork (), covers, () => Rendered (RenderPictures));
		}
	}

	static BinaryData Rendered (Action<BinaryDataBuilder> render)
	{
		using var builder = new BinaryDataBuilder ();
		render (builder);
		return builder.ToBinaryData ();
	}

	static string FreeformId (string mean, string name) => Mp4AtomMapping.FreeformAtom + "\n" + mean + "\n" + name;

	BinaryData RenderKeyed (Mp4QuickTimeKeys keys) =>
		RenderOrdered (_preservedAtoms.ConvertAll (preserved => preserved?.Box), keys, _slotByField, _keyIndexByField, movie: false);

	// Every item is written in file order. A kept item goes back as it was; a field that was set
	// is written where its first item was, and in a keyed meta as an item typed by its key index,
	// never as an iTunes atom: "The atom type for each metadata item atom should be set equal to the
	// index of the key" (https://developer.apple.com/documentation/quicktime-file-format/metadata_item_atom).
	BinaryData RenderOrdered (List<BinaryData?> kept, Mp4QuickTimeKeys? keys, Dictionary<string, int> slots, Dictionary<string, uint> keyIndexes, bool movie)
	{
		var items = new List<(long Order, BinaryData Box)> ();
		var appended = 0L;
		foreach (var field in ValuedFields ()) {
			if (_valueFromSources.Contains (field.Id) || (movie && !_movieFields.Contains (field.Id)))
				continue;

			BinaryData box;
			if (keys is null) {
				box = field.Itunes ();
			} else {
				if (!keyIndexes.TryGetValue (field.Id, out var index)) {
					index = keys.IndexFor (field.Field);
					keyIndexes[field.Id] = index;
				}

				var atoms = field.Field.Kind == Mp4KeyedKind.Atom && keys.IsTextKey (index) ? ToKeyedText (field.Id, field.Atoms) : field.Atoms;
				box = RenderItem (index, atoms);
			}

			var position = slots.TryGetValue (field.Id, out var slot) ? (2L * slot) + 1 : (long.MaxValue / 2) + appended++;
			items.Add ((position, box));
		}

		// An item whose type is no key's index has no meaning. If a key added now took that index,
		// the item would gain one, so it is dropped.
		for (var i = 0; i < kept.Count; i++) {
			var box = kept[i];
			if (box is not null && (keys is null || !keys.IsAdded (TypeKey (box.Value.Span))))
				items.Add ((2L * i, box.Value));
		}

		var order = new int[items.Count];
		for (var i = 0; i < order.Length; i++)
			order[i] = i;
		Array.Sort (order, (x, y) => {
			var byOrder = items[x].Order.CompareTo (items[y].Order);
			return byOrder != 0 ? byOrder : x.CompareTo (y);
		});

		using var builder = new BinaryDataBuilder ();
		foreach (var i in order)
			builder.Add (items[i].Box);
		return builder.ToBinaryData ();
	}

	static BinaryData RenderItem (uint type, List<Mp4DataAtom> dataAtoms)
	{
		using var builder = new BinaryDataBuilder ();
		var name = new string ([(char)(type >> 24), (char)((type >> 16) & 0xFF), (char)((type >> 8) & 0xFF), (char)(type & 0xFF)]);
		RenderStandardAtom (builder, name, dataAtoms);
		return builder.ToBinaryData ();
	}

	// Track and disc are "n/m" text under an "mdta" key; flags and the rating are a number.
	static List<Mp4DataAtom> FromKeyedText (string atomId, List<Mp4DataAtom> dataAtoms)
	{
		if (!IsTextKeyedNumber (atomId) || dataAtoms.Count == 0 || dataAtoms[0].TypeIndicator != Mp4AtomMapping.TypeUtf8)
			return dataAtoms;

		var text = dataAtoms[0].ToUtf8String ()?.Trim () ?? string.Empty;
		var locale = dataAtoms[0].Locale;
		if (atomId is Mp4AtomMapping.TrackNumber or Mp4AtomMapping.DiscNumber) {
			var slash = text.AsSpan ().IndexOf ('/');
			var numberText = slash < 0 ? text : text.Substring (0, slash);
			var totalText = slash < 0 ? string.Empty : text.Substring (slash + 1);
			if (!ushort.TryParse (numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
				return [];
			ushort.TryParse (totalText, NumberStyles.None, CultureInfo.InvariantCulture, out var total);

			var data = new byte[8];
			data[2] = (byte)(number >> 8);
			data[3] = (byte)number;
			data[4] = (byte)(total >> 8);
			data[5] = (byte)total;
			return [new Mp4DataAtom (Mp4AtomMapping.TypeBinary, new BinaryData (data), locale)];
		}

		if (!byte.TryParse (text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
			return [];
		return [new Mp4DataAtom (Mp4AtomMapping.TypeInteger, new BinaryData ([value]), locale)];
	}

	static List<Mp4DataAtom> ToKeyedText (string atomId, List<Mp4DataAtom> dataAtoms)
	{
		if (!IsTextKeyedNumber (atomId) || dataAtoms.Count == 0 || dataAtoms[0].TypeIndicator == Mp4AtomMapping.TypeUtf8)
			return dataAtoms;

		string text;
		if (atomId is Mp4AtomMapping.TrackNumber or Mp4AtomMapping.DiscNumber) {
			if (!dataAtoms[0].TryParseTrackDisc (out var number, out var total))
				return dataAtoms;
			text = total > 0
				? string.Format (CultureInfo.InvariantCulture, "{0}/{1}", number, total)
				: number.ToString (CultureInfo.InvariantCulture);
		} else {
			text = (dataAtoms[0].ToUInt32 () ?? 0).ToString (CultureInfo.InvariantCulture);
		}

		return [new Mp4DataAtom (Mp4AtomMapping.TypeUtf8, BinaryData.FromStringUtf8 (text), dataAtoms[0].Locale)];
	}

	static bool IsTextKeyedNumber (string atomId) => atomId is
		Mp4AtomMapping.TrackNumber or Mp4AtomMapping.DiscNumber or Mp4AtomMapping.Compilation or
		Mp4AtomMapping.GaplessPlayback or Mp4AtomMapping.PodcastFlag or Mp4AtomMapping.ContentRating;

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

	internal int PreserveIlstAtom (BinaryData box, string? mean = null, string? name = null)
	{
		if (box.IsEmpty)
			return -1;

		var typeKey = TypeKey (box.Span);
		var index = _preservedAtoms.Count;
		_preservedAtoms.Add (new PreservedBox (box));
		if (!_preservedByType.TryGetValue (typeKey, out var indexes)) {
			indexes = new List<int> ();
			_preservedByType.Add (typeKey, indexes);
		}

		indexes.Add (index);

		if (mean is null || name is null)
			return index;

		if (!_preservedFreeformByKey.TryGetValue ((mean, name), out var twins)) {
			twins = new List<int> ();
			_preservedFreeformByKey.Add ((mean, name), twins);
		}

		twins.Add (index);
		return index;
	}

	void DropPreserved (string atomId)
	{
		// Freeform keys are longer than 4 characters and are matched by mean and name.
		if (atomId.Length != 4)
			return;

		DropPreserved (TypeKey (atomId));
		DropSources (atomId);
	}

	void DropPreserved (uint typeKey)
	{
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

	void DropFreeformTwins (string mean, string name) => DropSources (FreeformId (mean, name));

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
