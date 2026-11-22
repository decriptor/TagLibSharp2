// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TagLibSharp2.Core;

#pragma warning disable CA1307 // Specify StringComparison for clarity - IndexOf with char doesn't need it

namespace TagLibSharp2.Ape;

/// <summary>
/// Represents the result of parsing an APE tag.
/// </summary>
public readonly struct ApeTagParseResult : IEquatable<ApeTagParseResult>
{
	/// <summary>
	/// Gets the parsed APE tag, or null if parsing failed.
	/// </summary>
	public ApeTag? Tag { get; }

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Tag is not null && Error is null;

	private ApeTagParseResult (ApeTag? tag, string? error)
	{
		Tag = tag;
		Error = error;
	}

	/// <summary>
	/// Creates a successful parse result.
	/// </summary>
	/// <param name="tag">The parsed APE tag.</param>
	/// <returns>A successful result containing the tag.</returns>
	public static ApeTagParseResult Success (ApeTag tag) => new (tag, null);

	/// <summary>
	/// Creates a failed parse result.
	/// </summary>
	/// <param name="error">The error message describing the failure.</param>
	/// <returns>A failed result containing the error.</returns>
	public static ApeTagParseResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (ApeTagParseResult other) =>
		Equals (Tag, other.Tag) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is ApeTagParseResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Tag, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (ApeTagParseResult left, ApeTagParseResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (ApeTagParseResult left, ApeTagParseResult right) =>
		!left.Equals (right);
}

/// <summary>
/// APE Tag v2 implementation.
/// </summary>
public sealed class ApeTag : Tag
{
	private readonly Dictionary<string, ApeTagItem> _items;

	/// <summary>
	/// Standard APE key names (Title-case per spec).
	/// </summary>
	private static class Keys
	{
		public const string Title = "Title";
		public const string Artist = "Artist";
		public const string Album = "Album";
		public const string AlbumArtist = "Album Artist";
		public const string AlbumArtistAlias = "ALBUMARTIST";
		public const string Year = "Year";
		public const string Date = "Date"; // Alternative to Year
		public const string Comment = "Comment";
		public const string Genre = "Genre";
		public const string Track = "Track";
		public const string Disc = "Disc";
		public const string Composer = "Composer";
		public const string Conductor = "Conductor";
		public const string Copyright = "Copyright";
		public const string Subtitle = "Subtitle";
		public const string Publisher = "Publisher";
		public const string Isrc = "ISRC";
		public const string Barcode = "Barcode";
		public const string CatalogNumber = "CatalogNumber";
		public const string Language = "Language";
		public const string CoverArtFront = "Cover Art (Front)";
		public const string CoverArtBack = "Cover Art (Back)";
		public const string ReplayGainTrackGain = "REPLAYGAIN_TRACK_GAIN";
		public const string ReplayGainTrackPeak = "REPLAYGAIN_TRACK_PEAK";
		public const string ReplayGainAlbumGain = "REPLAYGAIN_ALBUM_GAIN";
		public const string ReplayGainAlbumPeak = "REPLAYGAIN_ALBUM_PEAK";
		public const string MusicBrainzTrackId = "MUSICBRAINZ_TRACKID";
		public const string MusicBrainzAlbumId = "MUSICBRAINZ_ALBUMID";
		public const string MusicBrainzArtistId = "MUSICBRAINZ_ARTISTID";
		public const string MusicBrainzAlbumArtistId = "MUSICBRAINZ_ALBUMARTISTID";
		public const string MusicBrainzReleaseGroupId = "MUSICBRAINZ_RELEASEGROUPID";
		public const string MusicBrainzWorkId = "MUSICBRAINZ_WORKID";
		public const string MusicBrainzDiscId = "MUSICBRAINZ_DISCID";
		public const string Bpm = "BPM";
		public const string Compilation = "Compilation";
		public const string OriginalDate = "OriginalDate";
		public const string Work = "WORK";
		public const string Movement = "MOVEMENT";
		public const string MovementNumber = "MOVEMENTNUMBER";
		public const string MovementName = "MOVEMENTNAME";
		public const string MovementTotal = "MOVEMENTTOTAL";
		public const string Grouping = "Grouping";
		public const string Remixer = "MixArtist";
		public const string InitialKey = "Key";
		public const string Mood = "Mood";
		public const string MediaType = "Media";
		public const string EncodedBy = "EncodedBy";
		public const string EncoderSettings = "EncoderSettings";
		public const string Description = "Description";
		public const string DateTagged = "DateTagged";
		public const string AmazonId = "ASIN";
		public const string MusicBrainzRecordingId = "MUSICBRAINZ_RECORDINGID";
		public const string MusicBrainzReleaseStatus = "MUSICBRAINZ_ALBUMSTATUS";
		public const string MusicBrainzReleaseType = "MUSICBRAINZ_ALBUMTYPE";
		public const string MusicBrainzReleaseCountry = "RELEASECOUNTRY";
		public const string AcoustIdId = "ACOUSTID_ID";
		public const string AcoustIdFingerprint = "ACOUSTID_FINGERPRINT";
		public const string PodcastFeedUrl = "PODCASTFEEDURL";
	}

	/// <summary>
	/// Creates a new empty APE tag.
	/// </summary>
	public ApeTag ()
	{
		_items = new Dictionary<string, ApeTagItem> (StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Creates an APE tag with the specified items.
	/// </summary>
	private ApeTag (Dictionary<string, ApeTagItem> items)
	{
		_items = items;
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Ape;

	/// <summary>
	/// Gets the number of items in the tag.
	/// </summary>
	public int ItemCount => _items.Count;

	/// <inheritdoc/>
	public override string? Title {
		get => GetValue (Keys.Title);
		set => SetOrRemoveProperty (Keys.Title, value, Title);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The first performer. Other names stay on <see cref="Performers"/>; a comma does not join them.
	/// </remarks>
	public override string? Artist {
		get => ReadFirstName (Keys.Artist);
		set => SetOrRemoveProperty (Keys.Artist, value, Artist);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Names share one item. A NUL separates them, and a single name has no extra NUL.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Performers {
		get => GetTextList (Keys.Artist);
		set => SetTextList (Keys.Artist, value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override string? Album {
		get => GetValue (Keys.Album);
		set => SetOrRemoveProperty (Keys.Album, value, Album);
	}

	/// <summary>
	/// Gets or sets the album artist.
	/// </summary>
	/// <remarks>
	/// The first album artist. A text name on "Album Artist" wins, then a text name on
	/// "ALBUMARTIST". A locator is used only when neither item has text.
	/// Setting or clearing removes both items and stores the name in "Album Artist".
	/// </remarks>
	public override string? AlbumArtist {
		get => ReadFirstName (Keys.AlbumArtist, Keys.AlbumArtistAlias);
		set => SetOrRemoveProperty (Keys.AlbumArtist, value, AlbumArtist, Keys.AlbumArtistAlias);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Names share one "Album Artist" item, separated by NUL. A single name has no extra NUL.
	/// Reading also accepts "ALBUMARTIST". Setting or clearing removes both items.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] AlbumArtists {
		get {
			var values = GetTextList (Keys.AlbumArtist);
			return values.Length > 0 ? values : GetTextList (Keys.AlbumArtistAlias);
		}
		set => SetTextList (Keys.AlbumArtist, value, Keys.AlbumArtistAlias);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	/// <remarks>
	/// Reads "Year", then "Date" when "Year" is missing or empty. Writing back the value already read changes nothing.
	/// A new value is written to "Year" and to an existing "Date". Clearing removes both.
	/// </remarks>
	public override string? Year {
		get {
			var year = GetValue (Keys.Year);
			if (!string.IsNullOrEmpty (year))
				return year;
			var date = GetValue (Keys.Date);
			return string.IsNullOrEmpty (date) ? year ?? date : date;
		}
		set {
			// The value read changes nothing, in a copy too: Date is its own item and keeps its full date.
			if (IsValueRead (value, Year))
				return;

			if (string.IsNullOrEmpty (value)) {
				_items.Remove (Keys.Year);
				_items.Remove (Keys.Date);
				return;
			}

			SetValue (Keys.Year, value!);
			if (_items.ContainsKey (Keys.Date))
				SetValue (Keys.Date, value!);
		}
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetValue (Keys.Comment);
		set => SetOrRemoveProperty (Keys.Comment, value, Comment);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The first genre. Other names stay on <see cref="Genres"/>; a comma does not join them.
	/// </remarks>
	public override string? Genre {
		get => ReadFirstName (Keys.Genre);
		set => SetOrRemoveProperty (Keys.Genre, value, Genre);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Names share one item. A NUL separates them, and a single name has no extra NUL.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Genres {
		get => GetTextList (Keys.Genre);
		set => SetTextList (Keys.Genre, value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override uint? Track {
		get => ParseTrackDisc (GetValue (Keys.Track))?.Number;
		set {
			var total = TotalTracks;
			if (value is null) {
				_items.Remove (Keys.Track);
			} else if (total is not null) {
				SetValue (Keys.Track, $"{value}/{total}");
			} else {
				SetValue (Keys.Track, value.Value.ToString (CultureInfo.InvariantCulture));
			}
		}
	}

	/// <summary>
	/// Gets or sets the total number of tracks.
	/// </summary>
	public override uint? TotalTracks {
		get => ParseTrackDisc (GetValue (Keys.Track))?.Total;
		set {
			var track = Track;
			if (track is not null && value is not null) {
				SetValue (Keys.Track, $"{track}/{value}");
			} else if (track is not null) {
				SetValue (Keys.Track, track.Value.ToString (CultureInfo.InvariantCulture));
			}
		}
	}

	/// <summary>
	/// Gets or sets the disc number.
	/// </summary>
	public uint? Disc {
		get => ParseTrackDisc (GetValue (Keys.Disc))?.Number;
		set {
			var total = TotalDiscs;
			if (value is null) {
				_items.Remove (Keys.Disc);
			} else if (total is not null) {
				SetValue (Keys.Disc, $"{value}/{total}");
			} else {
				SetValue (Keys.Disc, value.Value.ToString (CultureInfo.InvariantCulture));
			}
		}
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => Disc;
		set => Disc = value;
	}

	/// <summary>
	/// Gets or sets the total number of discs.
	/// </summary>
	public override uint? TotalDiscs {
		get => ParseTrackDisc (GetValue (Keys.Disc))?.Total;
		set {
			var disc = Disc;
			if (disc is not null && value is not null) {
				SetValue (Keys.Disc, $"{disc}/{value}");
			} else if (disc is not null) {
				SetValue (Keys.Disc, disc.Value.ToString (CultureInfo.InvariantCulture));
			}
		}
	}

	/// <inheritdoc/>
	public override string? DiscSubtitle {
		get => GetValue ("DiscSubtitle");
		set => SetOrRemoveProperty ("DiscSubtitle", value, DiscSubtitle);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The first composer. Other names stay on <see cref="Composers"/>; a comma does not join them.
	/// </remarks>
	public override string? Composer {
		get => ReadFirstName (Keys.Composer);
		set => SetOrRemoveProperty (Keys.Composer, value, Composer);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Names share one item. A NUL separates them, and a single name has no extra NUL.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Composers {
		get => GetTextList (Keys.Composer);
		set => SetTextList (Keys.Composer, value);
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetValue (Keys.Conductor);
		set => SetOrRemoveProperty (Keys.Conductor, value, Conductor);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => GetValue (Keys.Copyright);
		set => SetOrRemoveProperty (Keys.Copyright, value, Copyright);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetValue (Keys.MusicBrainzTrackId);
		set => SetOrRemoveProperty (Keys.MusicBrainzTrackId, value, MusicBrainzTrackId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetValue (Keys.MusicBrainzAlbumId);
		set => SetOrRemoveProperty (Keys.MusicBrainzAlbumId, value, MusicBrainzReleaseId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetValue (Keys.MusicBrainzArtistId);
		set => SetOrRemoveProperty (Keys.MusicBrainzArtistId, value, MusicBrainzArtistId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetValue (Keys.MusicBrainzAlbumArtistId);
		set => SetOrRemoveProperty (Keys.MusicBrainzAlbumArtistId, value, MusicBrainzAlbumArtistId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetValue (Keys.MusicBrainzReleaseGroupId);
		set => SetOrRemoveProperty (Keys.MusicBrainzReleaseGroupId, value, MusicBrainzReleaseGroupId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => GetValue (Keys.MusicBrainzWorkId);
		set => SetOrRemoveProperty (Keys.MusicBrainzWorkId, value, MusicBrainzWorkId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => GetValue (Keys.MusicBrainzDiscId);
		set => SetOrRemoveProperty (Keys.MusicBrainzDiscId, value, MusicBrainzDiscId);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetValue (Keys.ReplayGainTrackGain);
		set => SetOrRemoveProperty (Keys.ReplayGainTrackGain, value, ReplayGainTrackGain);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetValue (Keys.ReplayGainTrackPeak);
		set => SetOrRemoveProperty (Keys.ReplayGainTrackPeak, value, ReplayGainTrackPeak);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetValue (Keys.ReplayGainAlbumGain);
		set => SetOrRemoveProperty (Keys.ReplayGainAlbumGain, value, ReplayGainAlbumGain);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetValue (Keys.ReplayGainAlbumPeak);
		set => SetOrRemoveProperty (Keys.ReplayGainAlbumPeak, value, ReplayGainAlbumPeak);
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => GetValue ("R128_TRACK_GAIN");
		set => SetOrRemoveProperty ("R128_TRACK_GAIN", value, R128TrackGain);
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => GetValue ("R128_ALBUM_GAIN");
		set => SetOrRemoveProperty ("R128_ALBUM_GAIN", value, R128AlbumGain);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => GetValue (Keys.Subtitle);
		set => SetOrRemoveProperty (Keys.Subtitle, value, Subtitle);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetValue (Keys.Publisher);
		set => SetOrRemoveProperty (Keys.Publisher, value, Publisher);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetValue (Keys.Isrc);
		set => SetOrRemoveProperty (Keys.Isrc, value, Isrc);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => GetValue (Keys.Barcode);
		set => SetOrRemoveProperty (Keys.Barcode, value, Barcode);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => GetValue (Keys.CatalogNumber);
		set => SetOrRemoveProperty (Keys.CatalogNumber, value, CatalogNumber);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => GetValue (Keys.Language);
		set => SetOrRemoveProperty (Keys.Language, value, Language);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetValue ("Lyrics");
		set => SetOrRemoveProperty ("Lyrics", value, Lyrics);
	}

	// ═══════════════════════════════════════════════════════════════
	// Extended Metadata Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var value = GetValue (Keys.Bpm);
			if (string.IsNullOrEmpty (value))
				return null;
			return uint.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bpm) ? bpm : null;
		}
		set {
			if (value.HasValue)
				SetValue (Keys.Bpm, value.Value.ToString (CultureInfo.InvariantCulture));
			else
				_items.Remove (Keys.Bpm);
		}
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get {
			var value = GetValue (Keys.Compilation);
			return value == "1" || string.Equals (value, "true", StringComparison.OrdinalIgnoreCase);
		}
		set {
			if (value)
				SetValue (Keys.Compilation, "1");
			else
				_items.Remove (Keys.Compilation);
		}
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => GetValue (Keys.OriginalDate);
		set => SetOrRemoveProperty (Keys.OriginalDate, value, OriginalReleaseDate);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => GetValue (Keys.Grouping);
		set => SetOrRemoveProperty (Keys.Grouping, value, Grouping);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => GetValue (Keys.Remixer);
		set => SetOrRemoveProperty (Keys.Remixer, value, Remixer);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => GetValue (Keys.InitialKey);
		set => SetOrRemoveProperty (Keys.InitialKey, value, InitialKey);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => GetValue (Keys.Mood);
		set => SetOrRemoveProperty (Keys.Mood, value, Mood);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => GetValue (Keys.MediaType);
		set => SetOrRemoveProperty (Keys.MediaType, value, MediaType);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => GetValue (Keys.EncodedBy);
		set => SetOrRemoveProperty (Keys.EncodedBy, value, EncodedBy);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => GetValue (Keys.EncoderSettings);
		set => SetOrRemoveProperty (Keys.EncoderSettings, value, EncoderSettings);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => GetValue (Keys.Description);
		set => SetOrRemoveProperty (Keys.Description, value, Description);
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => GetValue (Keys.DateTagged);
		set => SetOrRemoveProperty (Keys.DateTagged, value, DateTagged);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => GetValue (Keys.AmazonId);
		set => SetOrRemoveProperty (Keys.AmazonId, value, AmazonId);
	}

	// ═══════════════════════════════════════════════════════════════
	// MusicBrainz Extended IDs
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => GetValue (Keys.MusicBrainzRecordingId);
		set => SetOrRemoveProperty (Keys.MusicBrainzRecordingId, value, MusicBrainzRecordingId);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => GetValue (Keys.MusicBrainzReleaseStatus);
		set => SetOrRemoveProperty (Keys.MusicBrainzReleaseStatus, value, MusicBrainzReleaseStatus);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => GetValue (Keys.MusicBrainzReleaseType);
		set => SetOrRemoveProperty (Keys.MusicBrainzReleaseType, value, MusicBrainzReleaseType);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => GetValue (Keys.MusicBrainzReleaseCountry);
		set => SetOrRemoveProperty (Keys.MusicBrainzReleaseCountry, value, MusicBrainzReleaseCountry);
	}

	// ═══════════════════════════════════════════════════════════════
	// AcoustId Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => GetValue (Keys.AcoustIdId);
		set => SetOrRemoveProperty (Keys.AcoustIdId, value, AcoustIdId);
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => GetValue (Keys.AcoustIdFingerprint);
		set => SetOrRemoveProperty (Keys.AcoustIdFingerprint, value, AcoustIdFingerprint);
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => GetValue (Keys.PodcastFeedUrl);
		set => SetOrRemoveProperty (Keys.PodcastFeedUrl, value, PodcastFeedUrl);
	}

	// ═══════════════════════════════════════════════════════════════
	// Classical Music Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Work {
		get => GetValue (Keys.Work);
		set => SetOrRemoveProperty (Keys.Work, value, Work);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Follows MusicBrainz Picard's tag mapping (https://picard-docs.musicbrainz.org/en/v3.0/appendices/tag_mapping.html):
	/// the movement name is MOVEMENTNAME and the movement number is MOVEMENT. A MOVEMENT that is not a
	/// number is still read as the name when there is no MOVEMENTNAME, as earlier versions wrote it.
	/// Writing back the value read changes nothing. A new name goes to MOVEMENTNAME, and also to MOVEMENT
	/// when MOVEMENT holds a name. Clearing removes MOVEMENTNAME and a MOVEMENT that holds a name; a
	/// numeric MOVEMENT stays the number, so a clear never changes how the other half reads.
	/// </remarks>
	public override string? Movement {
		get {
			var name = GetValue (Keys.MovementName);
			if (!string.IsNullOrEmpty (name))
				return name;
			var legacy = LegacyMovementName;
			return string.IsNullOrEmpty (legacy) ? name ?? legacy : legacy;
		}
		set {
			if (IsWriteBack (value, Movement))
				return;
			var legacy = LegacyMovementName;
			if (string.IsNullOrEmpty (value)) {
				_items.Remove (Keys.MovementName);
				if (legacy is not null)
					_items.Remove (Keys.Movement);
				return;
			}
			SetValue (Keys.MovementName, value!);
			// A name in MOVEMENT is kept in step, unless the new name is a number, which MOVEMENT would
			// turn into the movement number.
			if (legacy is not null) {
				if (uint.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
					_items.Remove (Keys.Movement);
				else
					SetValue (Keys.Movement, value!);
			}
		}
	}

	/// <summary>
	/// MOVEMENT when it holds a movement name (it is not a number), as earlier versions wrote it; otherwise null.
	/// </summary>
	string? LegacyMovementName {
		get {
			var value = GetValue (Keys.Movement);
			return value is not null && ParseMovementNumber (Keys.Movement) is null ? value : null;
		}
	}

	uint? ParseMovementNumber (string key)
	{
		var value = GetValue (key);
		if (string.IsNullOrEmpty (value))
			return null;
		return uint.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) ? num : null;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Reads the MOVEMENTNUMBER item, then a numeric MOVEMENT (Picard's movement number). A write goes to MOVEMENT,
	/// and to an existing MOVEMENTNUMBER, unless MOVEMENT holds a movement name; then it goes to
	/// MOVEMENTNUMBER. Clearing removes MOVEMENTNUMBER and a numeric MOVEMENT.
	/// </remarks>
	public override uint? MovementNumber {
		get => ParseMovementNumber (Keys.MovementNumber) ?? ParseMovementNumber (Keys.Movement);
		set {
			if (IsWriteBack (value, MovementNumber))
				return;
			if (LegacyMovementName is not null) {
				SetNumber (Keys.MovementNumber, value);
				return;
			}
			SetNumber (Keys.Movement, value);
			if (value is null || _items.ContainsKey (Keys.MovementNumber))
				SetNumber (Keys.MovementNumber, value);
		}
	}

	void SetNumber (string key, uint? value)
	{
		if (value.HasValue)
			SetValue (key, value.Value.ToString (CultureInfo.InvariantCulture));
		else
			_items.Remove (key);
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get {
			var value = GetValue (Keys.MovementTotal);
			if (string.IsNullOrEmpty (value))
				return null;
			return uint.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) ? num : null;
		}
		set {
			if (value.HasValue)
				SetValue (Keys.MovementTotal, value.Value.ToString (CultureInfo.InvariantCulture));
			else
				_items.Remove (Keys.MovementTotal);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Sort Order Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetValue ("TitleSort") ?? GetValue ("TITLESORT");
		set => SetOrRemoveProperty ("TitleSort", value, TitleSort);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetValue ("ArtistSort") ?? GetValue ("ARTISTSORT");
		set => SetOrRemoveProperty ("ArtistSort", value, ArtistSort);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetValue ("AlbumSort") ?? GetValue ("ALBUMSORT");
		set => SetOrRemoveProperty ("AlbumSort", value, AlbumSort);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetValue ("AlbumArtistSort") ?? GetValue ("ALBUMARTISTSORT");
		set => SetOrRemoveProperty ("AlbumArtistSort", value, AlbumArtistSort);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => GetValue ("ComposerSort") ?? GetValue ("COMPOSERSORT");
		set => SetOrRemoveProperty ("ComposerSort", value, ComposerSort);
	}

	// ═══════════════════════════════════════════════════════════════
	// Pictures (Cover Art)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override IPicture[] Pictures {
		get {
			var pictures = new List<IPicture> ();

			// Check standard cover art keys
			if (_items.TryGetValue (Keys.CoverArtFront, out var front) && front.BinaryValue is not null)
				pictures.Add (ApePicture.FromBinaryData (Keys.CoverArtFront, front.BinaryValue));

			if (_items.TryGetValue (Keys.CoverArtBack, out var back) && back.BinaryValue is not null)
				pictures.Add (ApePicture.FromBinaryData (Keys.CoverArtBack, back.BinaryValue));

			// Check for other cover art keys
			foreach (var kvp in _items) {
				if (kvp.Key.StartsWith ("Cover Art", StringComparison.OrdinalIgnoreCase) &&
					!string.Equals (kvp.Key, Keys.CoverArtFront, StringComparison.OrdinalIgnoreCase) &&
					!string.Equals (kvp.Key, Keys.CoverArtBack, StringComparison.OrdinalIgnoreCase) &&
					kvp.Value.BinaryValue is not null) {
					pictures.Add (ApePicture.FromBinaryData (kvp.Key, kvp.Value.BinaryValue));
				}
			}

			return [.. pictures];
		}
		set {
			// Remove existing cover art
			var keysToRemove = _items.Keys
				.Where (k => k.StartsWith ("Cover Art", StringComparison.OrdinalIgnoreCase))
				.ToList ();
			foreach (var key in keysToRemove)
				_items.Remove (key);

			// Add new pictures
			if (value is null || value.Length == 0)
				return;

			foreach (var pic in value) {
				var apePic = ApePicture.FromPicture (pic);
				var key = apePic.GetKey ();
				var data = apePic.PictureData.ToArray ();
				SetBinaryItem (key, apePic.Filename, data);
			}
		}
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets a text value by key (case-insensitive).
	/// </summary>
	public string? GetValue (string key)
	{
		return _items.TryGetValue (key, out var item) ? item.ValueAsString : null;
	}

	/// <summary>
	/// Sets a text value by key.
	/// </summary>
	public void SetValue (string key, string value)
	{
		_items[key] = ApeTagItem.CreateText (key, value);
	}

	/// <summary>
	/// Reads every non-empty NUL-separated value. Internal NULs are separators, not stripped text.
	/// </summary>
	string[] GetTextList (string key)
	{
		if (!_items.TryGetValue (key, out var item) || item.ItemType != ApeItemType.Text)
			return [];

		var text = Encoding.UTF8.GetString (item.RawValue);
		if (text.Length == 0)
			return [];

		var parts = text.Split ('\0');
		var result = new List<string> (parts.Length);
		for (var i = 0; i < parts.Length; i++) {
			if (parts[i].Length != 0)
				result.Add (parts[i]);
		}

		return result.Count == 0 ? [] : result.ToArray ();
	}

	/// <summary>
	/// The first text name on <paramref name="key"/>, or on <paramref name="alias"/>
	/// when the primary item has none. A locator, an empty text item, or a binary
	/// item is used only when neither key has a text name, and then it matches
	/// <see cref="GetValue"/>.
	/// </summary>
	string? ReadFirstName (string key, string? alias = null)
	{
		var text = FirstTextName (key);
		if (text is null && alias is not null)
			text = FirstTextName (alias);
		if (text is not null)
			return text;

		var primary = ReadListedOrRaw (key);
		if (!string.IsNullOrEmpty (primary) || alias is null)
			return primary;

		var secondary = ReadListedOrRaw (alias);
		return string.IsNullOrEmpty (secondary) ? primary ?? secondary : secondary;
	}

	string? FirstTextName (string key)
	{
		var names = GetTextList (key);
		return names.Length == 0 ? null : names[0];
	}

	string? ReadListedOrRaw (string key)
	{
		if (!_items.TryGetValue (key, out var item))
			return null;

		if (item.ItemType == ApeItemType.Text) {
			var names = GetTextList (key);
			if (names.Length > 0)
				return names[0];
		}

		return item.ValueAsString;
	}

	/// <summary>
	/// Stores names in one item. A single name has no extra NUL.
	/// <paramref name="alias"/> is removed on set and on clear so a second item cannot outlive the write.
	/// </summary>
	void SetTextList (string key, string[]? values, string? alias = null)
	{
		if (alias is not null)
			_items.Remove (alias);

		if (values is null || values.Length == 0) {
			_items.Remove (key);
			return;
		}

		var kept = new List<string> (values.Length);
		for (var i = 0; i < values.Length; i++) {
			if (!string.IsNullOrEmpty (values[i]))
				kept.Add (values[i]);
		}

		if (kept.Count == 0)
			_items.Remove (key);
		else
			SetValue (key, string.Join ("\0", kept));
	}

	/// <summary>
	/// Gets a binary item by key (e.g., cover art).
	/// </summary>
	public ApeBinaryData? GetBinaryItem (string key)
	{
		return _items.TryGetValue (key, out var item) ? item.BinaryValue : null;
	}

	/// <summary>
	/// Sets a binary item by key (e.g., cover art).
	/// </summary>
	public void SetBinaryItem (string key, string filename, byte[] data)
	{
		_items[key] = ApeTagItem.CreateBinary (key, filename, data);
	}

	/// <summary>
	/// Writes a single-value property. Writing back the value its getter returned is a no-op, so a
	/// text item that holds several values (APEv2 items separate them with a zero byte), an empty item,
	/// or the alias item the getter read stays as it is (<see cref="Tag.IsWriteBack(string?, string?)"/>).
	/// Any other value goes through <see cref="SetOrRemove"/>. The getters skip empty values, so they return ""
	/// only when no value holds text, and "" over a list with text is not a write-back.
	/// </summary>
	void SetOrRemoveProperty (string key, string? value, string? current, string? alias = null)
	{
		if (!IsWriteBack (value, current))
			SetOrRemove (key, value, alias);
	}

	/// <summary>
	/// Removes an item by key.
	/// </summary>
	public bool Remove (string key) => _items.Remove (key);

	/// <inheritdoc/>
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	/// <remarks>
	/// Reads a text or external locator item. Keys compare case-insensitively, and a zero byte separates
	/// values (https://wiki.hydrogenaud.io/index.php?title=APE_key and
	/// https://wiki.hydrogenaud.io/index.php?title=APE_Item_Value). A binary item has no text and returns
	/// no values.
	/// </remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsCustomFieldKey (name) || !_items.TryGetValue (name, out var item))
			return CustomFieldValues.None;
		if (item.ItemType is not (ApeItemType.Text or ApeItemType.ExternalLocator))
			return CustomFieldValues.None;

		var values = new List<string> ();
		CustomFieldValues.AddSplit (System.Text.Encoding.UTF8.GetString (item.RawValue), values);
		return values;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Writes one UTF-8 text item with the values separated by a zero byte
	/// (https://wiki.hydrogenaud.io/index.php?title=APE_Item_Value). The key replaces any item whose key
	/// differs only in case, because APEv2 does not allow two such keys
	/// (https://wiki.hydrogenaud.io/index.php?title=APE_key).
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!IsCustomFieldKey (name) || !CustomFieldValues.TryNormalize (values, out var kept))
			return false;

		_items.Remove (name);
		if (kept.Count > 0)
			_items[name] = ApeTagItem.CreateText (name, string.Join ("\0", kept));
		return true;
	}

	/// <inheritdoc/>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		return IsCustomFieldKey (name) && _items.Remove (name);
	}

	/// <summary>
	/// A valid APEv2 key that is not one of the <c>Cover Art</c> keys <see cref="Pictures"/> owns.
	/// </summary>
	static bool IsCustomFieldKey (string name) =>
		ApeTagItem.IsValidKey (name) &&
		!name.StartsWith ("Cover Art", StringComparison.OrdinalIgnoreCase);

	private void SetOrRemove (string key, string? value, string? alias = null)
	{
		if (alias is not null)
			_items.Remove (alias);

		if (string.IsNullOrEmpty (value)) {
			_items.Remove (key);
		} else {
			SetValue (key, value!);
		}
	}

	/// <summary>
	/// Parses an APE tag from binary data.
	/// A full footer at EOF is used when it parses.
	/// Otherwise, when ID3v1 follows, a Lyrics3 block immediately before that tag is not the footer.
	/// v2 ends with LYRICS200 and a 6-digit size. v1 ends with LYRICSEND and has no size field;
	/// LYRICSBEGIN must sit within 64 KB. The footer is the 32 bytes before LYRICSBEGIN,
	/// or at EOF-160 when ID3v1 follows and no Lyrics3 block is present.
	/// </summary>
	public static ApeTagParseResult Parse (ReadOnlySpan<byte> data)
	{
		var location = ApeTagLocation.Locate (data);
		if (!location.Found) {
			return ApeTagParseResult.Failure (location.Error!);
		}

		var footer = location.Footer!;
		var apeRegionLength = location.TagEnd;

		// Validate tag size against the bytes that belong to the APE tag.
		if (footer.TagSize > (uint)apeRegionLength) {
			return ApeTagParseResult.Failure (
				$"APE tag size ({footer.TagSize}) exceeds data length ({apeRegionLength})");
		}

		// TagSize includes the footer and not a header.
		var itemsSize = (int)footer.TagSize - ApeTagFooter.Size;
		var itemsStart = location.TagEnd - (int)footer.TagSize;

		if (itemsStart < 0 || itemsSize < 0) {
			return ApeTagParseResult.Failure ("Invalid APE tag layout");
		}

		// An item is the 8-byte header, a key of at least 2 bytes, and a NUL (11), not one byte.
		// A count above int.MaxValue cannot fit that budget inside an int-sized item region.
		const int minItemSize = ApeTagItem.MinHeaderSize + ApeTagItem.MinKeyLength + 1;
		if ((ulong)footer.ItemCount * (ulong)minItemSize > (ulong)itemsSize) {
			return ApeTagParseResult.Failure (
				$"APE item count ({footer.ItemCount}) exceeds item data ({itemsSize} bytes)");
		}

		// Do not size the dictionary from ItemCount. A count that fits the 11-byte minimum
		// can still allocate more than a megabyte; 0xFF payloads fail in the item parser either way.
		var items = new Dictionary<string, ApeTagItem> (
			(int)Math.Min (footer.ItemCount, 256u),
			StringComparer.OrdinalIgnoreCase);

		var itemsData = data.Slice (itemsStart, itemsSize);
		var offset = 0;
		var itemsParsed = 0;

		while (offset < itemsData.Length && itemsParsed < footer.ItemCount) {
			var itemResult = ApeTagItem.Parse (itemsData[offset..]);

			if (!itemResult.IsSuccess) {
				return ApeTagParseResult.Failure (itemResult.Error!);
			}

			items[itemResult.Item!.Key] = itemResult.Item;
			offset += itemResult.BytesConsumed;
			itemsParsed++;
		}

		if (itemsParsed != footer.ItemCount && (offset != itemsData.Length || itemsParsed == 0)) {
			return ApeTagParseResult.Failure (
				$"Item count mismatch: expected {footer.ItemCount}, parsed {itemsParsed}");
		}

		return ApeTagParseResult.Success (new ApeTag (items));
	}

	/// <summary>
	/// Clears all items from the tag.
	/// </summary>
	public override void Clear () => _items.Clear ();

	/// <summary>
	/// Renders the tag to binary data (without header).
	/// </summary>
	public override BinaryData Render () => RenderWithOptions (includeHeader: false);

	/// <summary>
	/// Renders the tag to binary data with optional header.
	/// </summary>
	public BinaryData RenderWithOptions (bool includeHeader = false)
	{
		// Render items (optionally sorted by size per spec recommendation)
		var renderedItems = _items.Values
			.Select (item => item.Render ())
			.OrderBy (data => data.Length)
			.ToList ();

		var itemsSize = renderedItems.Sum (data => data.Length);
		var tagSize = (uint)(itemsSize + ApeTagFooter.Size);

		// Create footer
		var footer = ApeTagFooter.Create (tagSize, (uint)_items.Count, isHeader: false, hasHeader: includeHeader);
		var footerData = footer.Render ();

		// Calculate total size
		var totalSize = includeHeader
			? ApeTagHeader.Size + itemsSize + ApeTagFooter.Size
			: itemsSize + ApeTagFooter.Size;

		var result = new byte[totalSize];
		var offset = 0;

		// Write header if requested
		if (includeHeader) {
			var header = ApeTagHeader.Create (tagSize, (uint)_items.Count);
			var headerData = header.Render ();
			headerData.CopyTo (result, offset);
			offset += ApeTagHeader.Size;
		}

		// Write items
		foreach (var itemData in renderedItems) {
			itemData.CopyTo (result, offset);
			offset += itemData.Length;
		}

		// Write footer
		footerData.CopyTo (result, offset);

		return new BinaryData (result);
	}

	/// <summary>
	/// Parses "N/M" format strings (track or disc numbers).
	/// </summary>
	private static (uint Number, uint? Total)? ParseTrackDisc (string? value)
	{
		if (string.IsNullOrEmpty (value))
			return null;

		var slashIndex = value!.IndexOf ('/');

		if (slashIndex < 0) {
			// Just the number
			return uint.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
				? (n, (uint?)null)
				: null;
		}

		// Number/Total format
		var numberPart = value.Substring (0, slashIndex);
		var totalPart = value.Substring (slashIndex + 1);

		if (!uint.TryParse (numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
			return null;

		if (!uint.TryParse (totalPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
			return (number, (uint?)null);

		return (number, total);
	}
}
