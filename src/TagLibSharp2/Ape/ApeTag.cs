// APE Tag v2 implementation
// Implements the abstract Tag class for APE format metadata
//
// Expert input:
// - C# Expert: Use Dictionary<string, ApeTagItem> for O(1) lookups with case-insensitive keys
// - Audio Expert: Support standard keys (Artist, Title, Album) plus extended (ReplayGain, MusicBrainz)
// - Audiophile: Preserve binary cover art with proper filename handling
// - QA Manager: Full round-trip capability, preserve unknown items

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
		set => SetOrRemove (Keys.Title, value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetValue (Keys.Artist);
		set => SetOrRemove (Keys.Artist, value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => GetValue (Keys.Album);
		set => SetOrRemove (Keys.Album, value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetValue (Keys.AlbumArtist);
		set => SetOrRemove (Keys.AlbumArtist, value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => GetValue (Keys.Year) ?? GetValue (Keys.Date);
		set => SetOrRemove (Keys.Year, value);
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetValue (Keys.Comment);
		set => SetOrRemove (Keys.Comment, value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetValue (Keys.Genre);
		set => SetOrRemove (Keys.Genre, value);
	}

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
	public override string? Composer {
		get => GetValue (Keys.Composer);
		set => SetOrRemove (Keys.Composer, value);
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetValue (Keys.Conductor);
		set => SetOrRemove (Keys.Conductor, value);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => GetValue (Keys.Copyright);
		set => SetOrRemove (Keys.Copyright, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetValue (Keys.MusicBrainzTrackId);
		set => SetOrRemove (Keys.MusicBrainzTrackId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetValue (Keys.MusicBrainzAlbumId);
		set => SetOrRemove (Keys.MusicBrainzAlbumId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetValue (Keys.MusicBrainzArtistId);
		set => SetOrRemove (Keys.MusicBrainzArtistId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetValue (Keys.MusicBrainzAlbumArtistId);
		set => SetOrRemove (Keys.MusicBrainzAlbumArtistId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetValue (Keys.MusicBrainzReleaseGroupId);
		set => SetOrRemove (Keys.MusicBrainzReleaseGroupId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => GetValue (Keys.MusicBrainzWorkId);
		set => SetOrRemove (Keys.MusicBrainzWorkId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => GetValue (Keys.MusicBrainzDiscId);
		set => SetOrRemove (Keys.MusicBrainzDiscId, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetValue (Keys.ReplayGainTrackGain);
		set => SetOrRemove (Keys.ReplayGainTrackGain, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetValue (Keys.ReplayGainTrackPeak);
		set => SetOrRemove (Keys.ReplayGainTrackPeak, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetValue (Keys.ReplayGainAlbumGain);
		set => SetOrRemove (Keys.ReplayGainAlbumGain, value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetValue (Keys.ReplayGainAlbumPeak);
		set => SetOrRemove (Keys.ReplayGainAlbumPeak, value);
	}

	/// <inheritdoc/>
	public override string? R128TrackGain {
		get => GetValue ("R128_TRACK_GAIN");
		set => SetOrRemove ("R128_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? R128AlbumGain {
		get => GetValue ("R128_ALBUM_GAIN");
		set => SetOrRemove ("R128_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => GetValue (Keys.Subtitle);
		set => SetOrRemove (Keys.Subtitle, value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetValue (Keys.Publisher);
		set => SetOrRemove (Keys.Publisher, value);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetValue (Keys.Isrc);
		set => SetOrRemove (Keys.Isrc, value);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => GetValue (Keys.Barcode);
		set => SetOrRemove (Keys.Barcode, value);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => GetValue (Keys.CatalogNumber);
		set => SetOrRemove (Keys.CatalogNumber, value);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => GetValue (Keys.Language);
		set => SetOrRemove (Keys.Language, value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetValue ("Lyrics");
		set => SetOrRemove ("Lyrics", value);
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
		set => SetOrRemove (Keys.OriginalDate, value);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => GetValue (Keys.Grouping);
		set => SetOrRemove (Keys.Grouping, value);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => GetValue (Keys.Remixer);
		set => SetOrRemove (Keys.Remixer, value);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => GetValue (Keys.InitialKey);
		set => SetOrRemove (Keys.InitialKey, value);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => GetValue (Keys.Mood);
		set => SetOrRemove (Keys.Mood, value);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => GetValue (Keys.MediaType);
		set => SetOrRemove (Keys.MediaType, value);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => GetValue (Keys.EncodedBy);
		set => SetOrRemove (Keys.EncodedBy, value);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => GetValue (Keys.EncoderSettings);
		set => SetOrRemove (Keys.EncoderSettings, value);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => GetValue (Keys.Description);
		set => SetOrRemove (Keys.Description, value);
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => GetValue (Keys.DateTagged);
		set => SetOrRemove (Keys.DateTagged, value);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => GetValue (Keys.AmazonId);
		set => SetOrRemove (Keys.AmazonId, value);
	}

	// ═══════════════════════════════════════════════════════════════
	// MusicBrainz Extended IDs
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => GetValue (Keys.MusicBrainzRecordingId);
		set => SetOrRemove (Keys.MusicBrainzRecordingId, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => GetValue (Keys.MusicBrainzReleaseStatus);
		set => SetOrRemove (Keys.MusicBrainzReleaseStatus, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => GetValue (Keys.MusicBrainzReleaseType);
		set => SetOrRemove (Keys.MusicBrainzReleaseType, value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => GetValue (Keys.MusicBrainzReleaseCountry);
		set => SetOrRemove (Keys.MusicBrainzReleaseCountry, value);
	}

	// ═══════════════════════════════════════════════════════════════
	// AcoustId Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? AcoustIdId {
		get => GetValue (Keys.AcoustIdId);
		set => SetOrRemove (Keys.AcoustIdId, value);
	}

	/// <inheritdoc/>
	public override string? AcoustIdFingerprint {
		get => GetValue (Keys.AcoustIdFingerprint);
		set => SetOrRemove (Keys.AcoustIdFingerprint, value);
	}

	/// <inheritdoc/>
	public override string? PodcastFeedUrl {
		get => GetValue (Keys.PodcastFeedUrl);
		set => SetOrRemove (Keys.PodcastFeedUrl, value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Classical Music Fields
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Work {
		get => GetValue (Keys.Work);
		set => SetOrRemove (Keys.Work, value);
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => GetValue (Keys.Movement);
		set => SetOrRemove (Keys.Movement, value);
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get {
			var value = GetValue (Keys.MovementNumber);
			if (string.IsNullOrEmpty (value))
				return null;
			return uint.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) ? num : null;
		}
		set {
			if (value.HasValue)
				SetValue (Keys.MovementNumber, value.Value.ToString (CultureInfo.InvariantCulture));
			else
				_items.Remove (Keys.MovementNumber);
		}
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
		set => SetOrRemove ("TitleSort", value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetValue ("ArtistSort") ?? GetValue ("ARTISTSORT");
		set => SetOrRemove ("ArtistSort", value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetValue ("AlbumSort") ?? GetValue ("ALBUMSORT");
		set => SetOrRemove ("AlbumSort", value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetValue ("AlbumArtistSort") ?? GetValue ("ALBUMARTISTSORT");
		set => SetOrRemove ("AlbumArtistSort", value);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => GetValue ("ComposerSort") ?? GetValue ("COMPOSERSORT");
		set => SetOrRemove ("ComposerSort", value);
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
	/// Removes an item by key.
	/// </summary>
	public bool Remove (string key) => _items.Remove (key);

	private void SetOrRemove (string key, string? value)
	{
		if (string.IsNullOrEmpty (value)) {
			_items.Remove (key);
		} else {
			SetValue (key, value!);
		}
	}

	/// <summary>
	/// Parses an APE tag from binary data.
	/// </summary>
	public static ApeTagParseResult Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < ApeTagFooter.Size) {
			return ApeTagParseResult.Failure (
				$"Data too short for APE tag: {data.Length} bytes");
		}

		// Try to parse footer from end of data
		var footerData = data[^ApeTagFooter.Size..];
		var footerResult = ApeTagFooter.Parse (footerData);

		if (!footerResult.IsSuccess) {
			return ApeTagParseResult.Failure (footerResult.Error!);
		}

		var footer = footerResult.Footer!;

		// Validate tag size
		if (footer.TagSize > data.Length) {
			return ApeTagParseResult.Failure (
				$"APE tag size ({footer.TagSize}) exceeds data length ({data.Length})");
		}

		if (footer.TagSize > int.MaxValue) {
			return ApeTagParseResult.Failure (
				$"APE tag size overflow: {footer.TagSize}");
		}

		// Calculate items data location
		// TagSize includes footer but not header
		var itemsSize = (int)footer.TagSize - ApeTagFooter.Size;
		var itemsStart = data.Length - (int)footer.TagSize;

		if (itemsStart < 0 || itemsSize < 0) {
			return ApeTagParseResult.Failure ("Invalid APE tag layout");
		}

		// Parse items
		var items = new Dictionary<string, ApeTagItem> (
			(int)footer.ItemCount,
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

		if (itemsParsed != footer.ItemCount) {
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
