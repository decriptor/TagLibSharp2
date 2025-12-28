// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Id3.Id3v2;

/// <summary>
/// Represents an ID3v2 tag containing frames with metadata.
/// </summary>
public sealed class Id3v2Tag : Tag
{
	const int FrameHeaderSize = 10;
	const int DefaultPaddingSize = 1024;

	readonly List<TextFrame> _frames = new (16);
	readonly List<PictureFrame> _pictures = new (2);
	readonly List<CommentFrame> _comments = new (2);
	readonly List<UserTextFrame> _userTextFrames = new (8);
	readonly List<LyricsFrame> _lyricsFrames = new (2);
	readonly List<UniqueFileIdFrame> _uniqueFileIdFrames = new (2);
	readonly List<InvolvedPeopleFrame> _involvedPeopleFrames = new (2);
	readonly List<PopularimeterFrame> _popularimeterFrames = new (2);
	readonly List<UrlFrame> _urlFrames = new (8);
	readonly List<UserUrlFrame> _userUrlFrames = new (4);
	readonly List<SyncLyricsFrame> _syncLyricsFrames = new (2);
	readonly List<GeneralObjectFrame> _generalObjectFrames = new (2);
	readonly List<PrivateFrame> _privateFrames = new (4);
	readonly List<ChapterFrame> _chapterFrames = new (8);
	readonly List<TableOfContentsFrame> _tableOfContentsFrames = new (2);

	// Cache for PerformersRole to avoid recalculating on each access
	string[]? _performersRoleCache;
	bool _performersRoleCacheValid;

	/// <summary>
	/// Gets the ID3v2 version (2, 3, or 4).
	/// </summary>
	public int Version { get; }

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Id3v2;

	/// <summary>
	/// Gets the list of text frames in this tag.
	/// </summary>
	public IReadOnlyList<TextFrame> Frames => _frames;

	/// <summary>
	/// Gets the list of picture frames (album art) in this tag.
	/// </summary>
	public IReadOnlyList<PictureFrame> PictureFrames => _pictures;

	/// <summary>
	/// Gets a value indicating whether this tag contains any pictures.
	/// </summary>
	public bool HasPictures => _pictures.Count > 0;

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override IPicture[] Pictures {
		get => [.. _pictures];
		set {
			_pictures.Clear ();
			if (value is not null) {
				foreach (var pic in value) {
					if (pic is PictureFrame frame)
						_pictures.Add (frame);
					else
						_pictures.Add (new PictureFrame (pic.MimeType, pic.PictureType, pic.Description, pic.PictureData));
				}
			}
		}
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the front cover art.
	/// </summary>
	/// <remarks>
	/// Getting returns the first <see cref="PictureType.FrontCover"/> picture, or null if none exists.
	/// Setting replaces any existing front cover with the new picture.
	/// Set to null to remove all front cover pictures.
	/// </remarks>
	public PictureFrame? CoverArt {
		get => GetPicture (Core.PictureType.FrontCover);
		set => SetPicture (Core.PictureType.FrontCover, value);
	}

	/// <inheritdoc/>
	public override string? Title {
		get => GetTextFrame ("TIT2");
		set => SetTextFrame ("TIT2", value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetTextFrame ("TPE1");
		set => SetTextFrame ("TPE1", value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => GetTextFrame ("TALB");
		set => SetTextFrame ("TALB", value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => Version == 4 ? GetTextFrame ("TDRC") : GetTextFrame ("TYER");
		set {
			if (Version == 4)
				SetTextFrame ("TDRC", value);
			else
				SetTextFrame ("TYER", value);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// In ID3v2.4, uses the TDOR (Original Release Time) frame.
	/// In ID3v2.3, uses the TORY (Original Release Year) frame.
	/// When reading, falls back to the other version's frame if the primary is not found.
	/// </remarks>
	public override string? OriginalReleaseDate {
		get {
			// Try version-appropriate frame first, then fallback
			if (Version == 4) {
				return GetTextFrame ("TDOR") ?? GetTextFrame ("TORY");
			}
			return GetTextFrame ("TORY") ?? GetTextFrame ("TDOR");
		}
		set {
			if (Version == 4)
				SetTextFrame ("TDOR", value);
			else
				SetTextFrame ("TORY", value);
		}
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetComment ();
		set => SetComment (value);
	}

	/// <summary>
	/// Gets the list of comment frames in this tag.
	/// </summary>
	public IReadOnlyList<CommentFrame> Comments => _comments;

	/// <summary>
	/// Gets the list of user-defined text frames (TXXX) in this tag.
	/// </summary>
	public IReadOnlyList<UserTextFrame> UserTextFrames => _userTextFrames;

	/// <summary>
	/// Gets the list of lyrics frames (USLT) in this tag.
	/// </summary>
	public IReadOnlyList<LyricsFrame> LyricsFrames => _lyricsFrames;

	/// <summary>
	/// Gets the list of unique file identifier frames (UFID) in this tag.
	/// </summary>
	public IReadOnlyList<UniqueFileIdFrame> UniqueFileIdFrames => _uniqueFileIdFrames;

	/// <summary>
	/// Gets the list of popularimeter frames (POPM) in this tag.
	/// </summary>
	public IReadOnlyList<PopularimeterFrame> PopularimeterFrames => _popularimeterFrames;

	/// <summary>
	/// Gets or sets the rating (0-255) for the default email identifier.
	/// </summary>
	/// <remarks>
	/// Uses the first POPM frame if multiple exist.
	/// Setting creates a new POPM frame with a default email if none exists.
	/// </remarks>
	public byte? Rating {
		get => _popularimeterFrames.Count > 0 ? _popularimeterFrames[0].Rating : null;
		set {
			if (value is null) {
				_popularimeterFrames.Clear ();
				return;
			}
			if (_popularimeterFrames.Count == 0)
				_popularimeterFrames.Add (new PopularimeterFrame ("no@email", value.Value, 0));
			else
				_popularimeterFrames[0].Rating = value.Value;
		}
	}

	/// <summary>
	/// Gets or sets the play count for the default email identifier.
	/// </summary>
	/// <remarks>
	/// Uses the first POPM frame if multiple exist.
	/// Setting creates a new POPM frame with a default email if none exists.
	/// </remarks>
	public ulong? PlayCount {
		get => _popularimeterFrames.Count > 0 ? _popularimeterFrames[0].PlayCount : null;
		set {
			if (value is null) {
				if (_popularimeterFrames.Count > 0)
					_popularimeterFrames[0].PlayCount = 0;
				return;
			}
			if (_popularimeterFrames.Count == 0)
				_popularimeterFrames.Add (new PopularimeterFrame ("no@email", 0, value.Value));
			else
				_popularimeterFrames[0].PlayCount = value.Value;
		}
	}

	/// <summary>
	/// Gets the popularimeter frame for a specific email identifier.
	/// </summary>
	/// <param name="email">The email identifier.</param>
	/// <returns>The popularimeter frame, or null if not found.</returns>
	public PopularimeterFrame? GetPopularimeter (string email)
	{
		for (var i = 0; i < _popularimeterFrames.Count; i++) {
			if (string.Equals (_popularimeterFrames[i].Email, email, StringComparison.OrdinalIgnoreCase))
				return _popularimeterFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Sets or adds a popularimeter frame for a specific email identifier.
	/// </summary>
	/// <param name="email">The email identifier.</param>
	/// <param name="rating">The rating value (0-255).</param>
	/// <param name="playCount">The play count (optional).</param>
	public void SetPopularimeter (string email, byte rating, ulong playCount = 0)
	{
		for (var i = 0; i < _popularimeterFrames.Count; i++) {
			if (string.Equals (_popularimeterFrames[i].Email, email, StringComparison.OrdinalIgnoreCase)) {
				_popularimeterFrames[i].Rating = rating;
				_popularimeterFrames[i].PlayCount = playCount;
				return;
			}
		}
		_popularimeterFrames.Add (new PopularimeterFrame (email, rating, playCount));
	}

	/// <summary>
	/// Gets the list of URL frames (W*** except WXXX) in this tag.
	/// </summary>
	public IReadOnlyList<UrlFrame> UrlFrames => _urlFrames;

	/// <summary>
	/// Gets the list of user-defined URL frames (WXXX) in this tag.
	/// </summary>
	public IReadOnlyList<UserUrlFrame> UserUrlFrames => _userUrlFrames;

	/// <summary>
	/// Gets the list of synchronized lyrics frames (SYLT) in this tag.
	/// </summary>
	public IReadOnlyList<SyncLyricsFrame> SyncLyricsFrames => _syncLyricsFrames;

	/// <summary>
	/// Gets the list of general encapsulated object frames (GEOB) in this tag.
	/// </summary>
	public IReadOnlyList<GeneralObjectFrame> GeneralObjectFrames => _generalObjectFrames;

	/// <summary>
	/// Gets the list of private frames (PRIV) in this tag.
	/// </summary>
	public IReadOnlyList<PrivateFrame> PrivateFrames => _privateFrames;

	/// <summary>
	/// Gets the list of chapter frames (CHAP) in this tag.
	/// </summary>
	/// <remarks>
	/// CHAP frames define chapters within audio content, commonly used in podcasts and audiobooks.
	/// Each chapter has a unique element ID, time range, and optional title.
	/// </remarks>
	public IReadOnlyList<ChapterFrame> ChapterFrames => _chapterFrames;

	/// <summary>
	/// Gets the list of table of contents frames (CTOC) in this tag.
	/// </summary>
	/// <remarks>
	/// CTOC frames define the structure of chapters. A tag typically has one top-level CTOC
	/// that references CHAP frames by their element IDs.
	/// </remarks>
	public IReadOnlyList<TableOfContentsFrame> TableOfContentsFrames => _tableOfContentsFrames;

	/// <summary>
	/// Gets the URL for a specific frame ID.
	/// </summary>
	/// <param name="frameId">The frame ID (e.g., "WOAR", "WPUB").</param>
	/// <returns>The URL, or null if not found.</returns>
#pragma warning disable CA1055 // URI return type - ID3v2 URLs may be malformed
	public string? GetUrl (string frameId)
#pragma warning restore CA1055
	{
		for (var i = 0; i < _urlFrames.Count; i++) {
			if (_urlFrames[i].Id == frameId)
				return _urlFrames[i].Url;
		}
		return null;
	}

	/// <summary>
	/// Sets or adds a URL frame.
	/// </summary>
	/// <param name="frameId">The frame ID (e.g., "WOAR", "WPUB").</param>
	/// <param name="url">The URL, or null to remove the frame.</param>
#pragma warning disable CA1054 // URI parameter - ID3v2 URLs may be malformed
	public void SetUrl (string frameId, string? url)
#pragma warning restore CA1054
	{
		for (var i = 0; i < _urlFrames.Count; i++) {
			if (_urlFrames[i].Id == frameId) {
				if (url is null)
					_urlFrames.RemoveAt (i);
				else
					_urlFrames[i].Url = url;
				return;
			}
		}
		if (url is not null)
			_urlFrames.Add (new UrlFrame (frameId, url));
	}

	/// <summary>
	/// Gets a user-defined URL by description.
	/// </summary>
	/// <param name="description">The URL description.</param>
	/// <returns>The URL, or null if not found.</returns>
#pragma warning disable CA1055 // URI return type - ID3v2 URLs may be malformed
	public string? GetUserUrl (string description)
#pragma warning restore CA1055
	{
		for (var i = 0; i < _userUrlFrames.Count; i++) {
			if (string.Equals (_userUrlFrames[i].Description, description, StringComparison.OrdinalIgnoreCase))
				return _userUrlFrames[i].Url;
		}
		return null;
	}

	/// <summary>
	/// Sets or adds a user-defined URL.
	/// </summary>
	/// <param name="description">The URL description.</param>
	/// <param name="url">The URL, or null to remove the frame.</param>
#pragma warning disable CA1054 // URI parameter - ID3v2 URLs may be malformed
	public void SetUserUrl (string description, string? url)
#pragma warning restore CA1054
	{
		for (var i = 0; i < _userUrlFrames.Count; i++) {
			if (string.Equals (_userUrlFrames[i].Description, description, StringComparison.OrdinalIgnoreCase)) {
				if (url is null)
					_userUrlFrames.RemoveAt (i);
				else
					_userUrlFrames[i].Url = url;
				return;
			}
		}
		if (url is not null)
			_userUrlFrames.Add (new UserUrlFrame (url, description));
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetTextFrame ("TCON");
		set => SetTextFrame ("TCON", value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get {
			var trackStr = GetTextFrame ("TRCK");
			if (string.IsNullOrEmpty (trackStr))
				return null;

			// Handle "5/12" format
#if NETSTANDARD2_0
			var slashIndex = trackStr!.IndexOf ('/');
#else
			var slashIndex = trackStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0)
				trackStr = trackStr.Substring (0, slashIndex);

			return uint.TryParse (trackStr, out var track) ? track : null;
		}
		set => SetTextFrame ("TRCK", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetTextFrame ("TPE2");
		set => SetTextFrame ("TPE2", value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get {
			var discStr = GetTextFrame ("TPOS");
			if (string.IsNullOrEmpty (discStr))
				return null;

			// Handle "2/3" format
#if NETSTANDARD2_0
			var slashIndex = discStr!.IndexOf ('/');
#else
			var slashIndex = discStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0)
				discStr = discStr.Substring (0, slashIndex);

			return uint.TryParse (discStr, out var disc) ? disc : null;
		}
		set => SetTextFrame ("TPOS", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => GetTextFrame ("TCOM");
		set => SetTextFrame ("TCOM", value);
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var bpmStr = GetTextFrame ("TBPM");
			if (string.IsNullOrEmpty (bpmStr))
				return null;
			return uint.TryParse (bpmStr, out var bpm) ? bpm : null;
		}
		set => SetTextFrame ("TBPM", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetTextFrame ("TPE3");
		set => SetTextFrame ("TPE3", value);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => GetTextFrame ("TCOP");
		set => SetTextFrame ("TCOP", value);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => GetTextFrame ("TCMP") == "1";
		set {
			if (value)
				SetTextFrame ("TCMP", "1");
			else
				SetTextFrame ("TCMP", null);
		}
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetTextFrame ("TSRC");
		set => SetTextFrame ("TSRC", value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetTextFrame ("TPUB");
		set => SetTextFrame ("TPUB", value);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => GetTextFrame ("TENC");
		set => SetTextFrame ("TENC", value);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => GetTextFrame ("TSSE");
		set => SetTextFrame ("TSSE", value);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => GetTextFrame ("TIT1");
		set => SetTextFrame ("TIT1", value);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => GetTextFrame ("TIT3");
		set => SetTextFrame ("TIT3", value);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => GetTextFrame ("TPE4");
		set => SetTextFrame ("TPE4", value);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => GetTextFrame ("TKEY");
		set => SetTextFrame ("TKEY", value);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => GetTextFrame ("TMOO");
		set => SetTextFrame ("TMOO", value);
	}

	/// <inheritdoc/>
	public override string? MediaType {
		get => GetTextFrame ("TMED");
		set => SetTextFrame ("TMED", value);
	}

	/// <inheritdoc/>
	public override string? Language {
		get => GetTextFrame ("TLAN");
		set => SetTextFrame ("TLAN", value);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => GetUserText ("BARCODE");
		set => SetUserText ("BARCODE", value);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => GetUserText ("CATALOGNUMBER");
		set => SetUserText ("CATALOGNUMBER", value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetTextFrame ("TSOA");
		set => SetTextFrame ("TSOA", value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetTextFrame ("TSOP");
		set => SetTextFrame ("TSOP", value);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetTextFrame ("TSOT");
		set => SetTextFrame ("TSOT", value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetTextFrame ("TSO2");
		set => SetTextFrame ("TSO2", value);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TSOP (Performer sort order) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[]? PerformersSort {
		get {
			var values = GetTextFrameValues ("TSOP");
			return values.Count > 0 ? [.. values] : null;
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TSOP", null);
				return;
			}
			// Join with null separator for ID3v2.4 multi-value support
			SetTextFrame ("TSOP", string.Join ("\0", value));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TSO2 (Album Artist sort order) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec. TSO2 is an iTunes extension.
	/// </remarks>
	public override string[]? AlbumArtistsSort {
		get {
			var values = GetTextFrameValues ("TSO2");
			return values.Count > 0 ? [.. values] : null;
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TSO2", null);
				return;
			}
			// Join with null separator for ID3v2.4 multi-value support
			SetTextFrame ("TSO2", string.Join ("\0", value));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TSOC (Composer sort order) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec. TSOC is an iTunes extension.
	/// </remarks>
	public override string[]? ComposersSort {
		get {
			var values = GetTextFrameValues ("TSOC");
			return values.Count > 0 ? [.. values] : null;
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TSOC", null);
				return;
			}
			// Join with null separator for ID3v2.4 multi-value support
			SetTextFrame ("TSOC", string.Join ("\0", value));
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get {
			var trackStr = GetTextFrame ("TRCK");
			if (string.IsNullOrEmpty (trackStr))
				return null;

#if NETSTANDARD2_0
			var slashIndex = trackStr!.IndexOf ('/');
#else
			var slashIndex = trackStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0) {
				var totalPart = trackStr.Substring (slashIndex + 1);
				if (uint.TryParse (totalPart, out var total))
					return total;
			}
			return null;
		}
		set {
			var currentTrack = Track ?? 1;
			if (value.HasValue)
				SetTextFrame ("TRCK", $"{currentTrack}/{value.Value}");
			else
				SetTextFrame ("TRCK", currentTrack.ToString (System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get {
			var discStr = GetTextFrame ("TPOS");
			if (string.IsNullOrEmpty (discStr))
				return null;

#if NETSTANDARD2_0
			var slashIndex = discStr!.IndexOf ('/');
#else
			var slashIndex = discStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0) {
				var totalPart = discStr.Substring (slashIndex + 1);
				if (uint.TryParse (totalPart, out var total))
					return total;
			}
			return null;
		}
		set {
			var currentDisc = DiscNumber ?? 1;
			if (value.HasValue)
				SetTextFrame ("TPOS", $"{currentDisc}/{value.Value}");
			else
				SetTextFrame ("TPOS", currentDisc.ToString (System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetLyrics ();
		set => SetLyrics (value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetUserText ("REPLAYGAIN_TRACK_GAIN");
		set => SetUserText ("REPLAYGAIN_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetUserText ("REPLAYGAIN_TRACK_PEAK");
		set => SetUserText ("REPLAYGAIN_TRACK_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetUserText ("REPLAYGAIN_ALBUM_GAIN");
		set => SetUserText ("REPLAYGAIN_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetUserText ("REPLAYGAIN_ALBUM_PEAK");
		set => SetUserText ("REPLAYGAIN_ALBUM_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetUserText ("MUSICBRAINZ_TRACKID");
		set => SetUserText ("MUSICBRAINZ_TRACKID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetUserText ("MUSICBRAINZ_ALBUMID");
		set => SetUserText ("MUSICBRAINZ_ALBUMID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetUserText ("MUSICBRAINZ_ARTISTID");
		set => SetUserText ("MUSICBRAINZ_ARTISTID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetUserText ("MUSICBRAINZ_RELEASEGROUPID");
		set => SetUserText ("MUSICBRAINZ_RELEASEGROUPID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetUserText ("MUSICBRAINZ_ALBUMARTISTID");
		set => SetUserText ("MUSICBRAINZ_ALBUMARTISTID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => GetUserText ("MusicBrainz Work Id");
		set => SetUserText ("MusicBrainz Work Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => GetUserText ("MusicBrainz Disc Id");
		set => SetUserText ("MusicBrainz Disc Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseStatus {
		get => GetUserText ("MusicBrainz Album Status");
		set => SetUserText ("MusicBrainz Album Status", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseType {
		get => GetUserText ("MusicBrainz Album Type");
		set => SetUserText ("MusicBrainz Album Type", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseCountry {
		get => GetUserText ("MusicBrainz Album Release Country");
		set => SetUserText ("MusicBrainz Album Release Country", value);
	}

	/// <inheritdoc/>
	public override string? ComposerSort {
		get => GetTextFrame ("TSOC");
		set => SetTextFrame ("TSOC", value);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TDTG (Tagging Time) frame which is ID3v2.4 only.
	/// </remarks>
	public override string? DateTagged {
		get => GetTextFrame ("TDTG");
		set => SetTextFrame ("TDTG", value);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => GetUserText ("DESCRIPTION");
		set => SetUserText ("DESCRIPTION", value);
	}

	/// <inheritdoc/>
	public override string? AmazonId {
		get => GetUserText ("ASIN");
		set => SetUserText ("ASIN", value);
	}

	/// <inheritdoc/>
	[System.Obsolete ("MusicIP PUID is obsolete. MusicIP service was discontinued. Use AcoustID fingerprints instead.")]
	public override string? MusicIpId {
		get => GetUserText ("MusicIP PUID");
		set => SetUserText ("MusicIP PUID", value);
	}

	/// <summary>
	/// Gets or sets the roles of the performers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a parallel array to the <see cref="Performers"/> list. Each element
	/// describes the role of the corresponding performer at the same index.
	/// For example, if Performers = ["John", "Jane"], PerformersRole might be ["vocals", "guitar"].
	/// </para>
	/// <para>
	/// The getter returns roles from the TMCL (Musician Credits List) frame.
	/// The setter combines roles with the current Performers to create role-person pairs.
	/// For full control over TMCL content, use <see cref="InvolvedPeopleFrames"/> directly.
	/// </para>
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[]? PerformersRole {
		get {
			// Return cached value if valid
			if (_performersRoleCacheValid)
				return _performersRoleCache;

			var tmcl = GetInvolvedPeopleFrame ("TMCL");
			if (tmcl is null || tmcl.Count == 0) {
				_performersRoleCache = null;
				_performersRoleCacheValid = true;
				return null;
			}

			var pairs = tmcl.Pairs;
			var roles = new string[pairs.Count];
			for (var i = 0; i < pairs.Count; i++)
				roles[i] = pairs[i].Role;

			_performersRoleCache = roles;
			_performersRoleCacheValid = true;
			return roles;
		}
		set {
			// Remove existing TMCL frames (this also invalidates cache)
			RemoveInvolvedPeopleFrames ("TMCL");

			if (value is null || value.Length == 0)
				return;

			// Create new TMCL frame combining roles with artists
			var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
			var artists = Performers;

			for (var i = 0; i < value.Length; i++) {
				// Pair each role with the corresponding artist (if available)
				var person = (i < artists.Length) ? artists[i] : "";
				frame.Add (value[i], person);
			}

			_involvedPeopleFrames.Add (frame);
		}
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets the list of involved people frames (TIPL/TMCL) in this tag.
	/// </summary>
	public IReadOnlyList<InvolvedPeopleFrame> InvolvedPeopleFrames => _involvedPeopleFrames;

	/// <summary>
	/// Gets an involved people frame by ID.
	/// </summary>
	/// <param name="frameId">The frame ID (TIPL or TMCL).</param>
	/// <returns>The frame, or null if not found.</returns>
	public InvolvedPeopleFrame? GetInvolvedPeopleFrame (string frameId)
	{
		for (var i = 0; i < _involvedPeopleFrames.Count; i++) {
			if (_involvedPeopleFrames[i].Id == frameId)
				return _involvedPeopleFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Adds an involved people frame to the tag.
	/// </summary>
	/// <param name="frame">The frame to add.</param>
	public void AddInvolvedPeopleFrame (InvolvedPeopleFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_involvedPeopleFrames.Add (frame);
		InvalidatePerformersRoleCache ();
	}

	/// <summary>
	/// Removes all involved people frames with the specified ID.
	/// </summary>
	/// <param name="frameId">The frame ID to remove (TIPL or TMCL), or null to remove all.</param>
	public void RemoveInvolvedPeopleFrames (string? frameId = null)
	{
		if (frameId is null)
			_involvedPeopleFrames.Clear ();
		else
			_involvedPeopleFrames.RemoveAll (f => f.Id == frameId);
		InvalidatePerformersRoleCache ();
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses a UFID frame with owner "http://musicbrainz.org".
	/// This is the canonical way to store a MusicBrainz recording ID in ID3v2.
	/// </remarks>
	public override string? MusicBrainzRecordingId {
		get => GetUniqueFileId (UniqueFileIdFrame.MusicBrainzOwner)?.IdentifierString;
		set {
			RemoveUniqueFileIds (UniqueFileIdFrame.MusicBrainzOwner);
			if (!string.IsNullOrEmpty (value))
				_uniqueFileIdFrames.Add (new UniqueFileIdFrame (UniqueFileIdFrame.MusicBrainzOwner, value!));
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Id3v2Tag"/> class.
	/// </summary>
	/// <param name="version">The ID3v2 version to use.</param>
	public Id3v2Tag (Id3v2Version version = Id3v2Version.V24)
	{
		Version = (int)version;
	}

	Id3v2Tag (int version)
	{
		Version = version;
	}

	/// <summary>
	/// Attempts to read an ID3v2 tag from the provided data.
	/// </summary>
	/// <param name="data">The data to parse.</param>
	/// <returns>A result indicating success, failure, or not found.</returns>
	public static TagReadResult<Id3v2Tag> Read (ReadOnlySpan<byte> data)
	{
		var headerResult = Id3v2Header.Read (data);

		if (headerResult.IsNotFound)
			return TagReadResult<Id3v2Tag>.NotFound ();

		if (!headerResult.IsSuccess)
			return TagReadResult<Id3v2Tag>.Failure (headerResult.Error!);

		var header = headerResult.Header;
		var tag = new Id3v2Tag (header.MajorVersion);

		// Skip header and parse frames
		var frameData = data.Slice (Id3v2Header.HeaderSize);

		// Limit remaining to actual available data to handle truncated files gracefully.
		// The header may claim a larger size than what's actually present in the data.
		var availableData = data.Length - Id3v2Header.HeaderSize;
		var remaining = (int)Math.Min (header.TagSize, (uint)availableData);

		// Skip extended header if present
		if (header.HasExtendedHeader && remaining >= 4) {
			var extHeaderSize = GetExtendedHeaderSize (frameData, header.MajorVersion);
			if (extHeaderSize > 0 && extHeaderSize <= remaining) {
				frameData = frameData.Slice (extHeaderSize);
				remaining -= extHeaderSize;
			}
		}

		while (remaining >= FrameHeaderSize && frameData.Length >= FrameHeaderSize) {
			// Check for padding (zeros)
			if (frameData[0] == 0)
				break;

			// Read frame header
			var frameId = GetFrameId (frameData);
			if (string.IsNullOrEmpty (frameId))
				break;

			var frameSize = GetFrameSize (frameData.Slice (4, 4), header.MajorVersion);
			if (frameSize <= 0 ||
				frameSize > remaining - FrameHeaderSize ||
				frameSize > frameData.Length - FrameHeaderSize)
				break;

			// Parse frame content
			var frameContent = frameData.Slice (FrameHeaderSize, frameSize);

			// Handle text frames (T***) - exclude TXXX (user text) and TIPL/TMCL (involved people)
			if (frameId[0] == 'T' && frameId != "TXXX" && frameId != "TIPL" && frameId != "TMCL") {
				var frameResult = TextFrame.Read (frameId, frameContent, (Id3v2Version)header.MajorVersion);
				if (frameResult.IsSuccess)
					tag._frames.Add (frameResult.Frame!);
			}
			// Handle picture frames (APIC)
			else if (frameId == "APIC") {
				var pictureResult = PictureFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (pictureResult.IsSuccess)
					tag._pictures.Add (pictureResult.Frame!);
			}
			// Handle comment frames (COMM)
			else if (frameId == "COMM") {
				var commentResult = CommentFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (commentResult.IsSuccess)
					tag._comments.Add (commentResult.Frame!);
			}
			// Handle user-defined text frames (TXXX)
			else if (frameId == "TXXX") {
				var userTextResult = UserTextFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (userTextResult.IsSuccess)
					tag._userTextFrames.Add (userTextResult.Frame!);
			}
			// Handle lyrics frames (USLT)
			else if (frameId == "USLT") {
				var lyricsResult = LyricsFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (lyricsResult.IsSuccess)
					tag._lyricsFrames.Add (lyricsResult.Frame!);
			}
			// Handle unique file identifier frames (UFID)
			else if (frameId == "UFID") {
				var ufidResult = UniqueFileIdFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (ufidResult.IsSuccess)
					tag._uniqueFileIdFrames.Add (ufidResult.Frame!);
			}
			// Handle involved people frames (TIPL, TMCL, IPLS)
			else if (frameId is "TIPL" or "TMCL" or "IPLS") {
				var involvedResult = InvolvedPeopleFrame.Read (frameId, frameContent, (Id3v2Version)header.MajorVersion);
				if (involvedResult.IsSuccess)
					tag._involvedPeopleFrames.Add (involvedResult.Frame!);
			}
			// Handle popularimeter frames (POPM)
			else if (frameId == "POPM") {
				var popmResult = PopularimeterFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (popmResult.IsSuccess)
					tag._popularimeterFrames.Add (popmResult.Frame!);
			}
			// Handle user URL frames (WXXX)
			else if (frameId == "WXXX") {
				var wxxxResult = UserUrlFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (wxxxResult.IsSuccess)
					tag._userUrlFrames.Add (wxxxResult.Frame!);
			}
			// Handle standard URL frames (W*** except WXXX)
			else if (UrlFrame.IsUrlFrameId (frameId)) {
				var urlResult = UrlFrame.Read (frameId, frameContent, (Id3v2Version)header.MajorVersion);
				if (urlResult.IsSuccess)
					tag._urlFrames.Add (urlResult.Frame!);
			}
			// Handle synchronized lyrics frames (SYLT)
			else if (frameId == "SYLT") {
				var syltResult = SyncLyricsFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (syltResult.IsSuccess)
					tag._syncLyricsFrames.Add (syltResult.Frame!);
			}
			// Handle general encapsulated object frames (GEOB)
			else if (frameId == "GEOB") {
				var geobResult = GeneralObjectFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (geobResult.IsSuccess)
					tag._generalObjectFrames.Add (geobResult.Frame!);
			}
			// Handle private frames (PRIV)
			else if (frameId == "PRIV") {
				var privResult = PrivateFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (privResult.IsSuccess)
					tag._privateFrames.Add (privResult.Frame!);
			}
			// Handle chapter frames (CHAP)
			else if (frameId == "CHAP") {
				var chapResult = ChapterFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (chapResult.IsSuccess)
					tag._chapterFrames.Add (chapResult.Frame!);
			}
			// Handle table of contents frames (CTOC)
			else if (frameId == "CTOC") {
				var ctocResult = TableOfContentsFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (ctocResult.IsSuccess)
					tag._tableOfContentsFrames.Add (ctocResult.Frame!);
			}

			// Move to next frame
			var totalFrameSize = FrameHeaderSize + frameSize;
			frameData = frameData.Slice (totalFrameSize);
			remaining -= totalFrameSize;
		}

		return TagReadResult<Id3v2Tag>.Success (tag, (int)header.TotalSize);
	}

	/// <summary>
	/// Gets the value of a text frame by ID.
	/// </summary>
	/// <param name="frameId">The frame ID (e.g., TIT2, TPE1).</param>
	/// <returns>The text value, or null if not found.</returns>
	public string? GetTextFrame (string frameId)
	{
		// Use for loop instead of LINQ for better performance
		for (var i = 0; i < _frames.Count; i++) {
			if (_frames[i].Id == frameId)
				return _frames[i].Text;
		}
		return null;
	}

	/// <summary>
	/// Gets all values from a text frame (multi-value support).
	/// </summary>
	/// <param name="frameId">The frame ID (e.g., TPE1 for artists).</param>
	/// <returns>A list of values, split by null characters (v2.4) or "/" (v2.3).</returns>
	/// <remarks>
	/// ID3v2.4 uses null characters as value separators. ID3v2.3 has no official
	/// separator, but "/" is commonly used. This method handles both.
	/// </remarks>
	public IReadOnlyList<string> GetTextFrameValues (string frameId)
	{
		var text = GetTextFrame (frameId);
		if (string.IsNullOrEmpty (text))
			return Array.Empty<string> ();

		// ID3v2.4 uses null as separator
#if NETSTANDARD2_0
		if (text!.IndexOf ('\0') >= 0) {
#else
		if (text!.Contains ('\0', StringComparison.Ordinal)) {
#endif
			var parts = text.Split ('\0');
			var result = new List<string> (parts.Length);
			for (var i = 0; i < parts.Length; i++) {
				var trimmed = parts[i].Trim ();
				if (!string.IsNullOrEmpty (trimmed))
					result.Add (trimmed);
			}
			return result;
		}

		// ID3v2.3 commonly uses "/" as separator (not official but widely used)
		// Only split if there are multiple values indicated by "/"
#if NETSTANDARD2_0
		if (text.IndexOf ('/') >= 0) {
#else
		if (text.Contains ('/', StringComparison.Ordinal)) {
#endif
			var parts = text.Split ('/');
			var result = new List<string> (parts.Length);
			for (var i = 0; i < parts.Length; i++) {
				var trimmed = parts[i].Trim ();
				if (!string.IsNullOrEmpty (trimmed))
					result.Add (trimmed);
			}
			return result;
		}

		// Single value
		return new List<string> { text };
	}

	/// <summary>
	/// Sets a text frame with multiple values.
	/// </summary>
	/// <param name="frameId">The frame ID.</param>
	/// <param name="values">The values to set.</param>
	/// <remarks>
	/// Values are joined with null characters for ID3v2.4 compatibility.
	/// </remarks>
	public void SetTextFrameValues (string frameId, IEnumerable<string>? values)
	{
		if (values is null) {
			SetTextFrame (frameId, null);
			return;
		}

		var list = new List<string> ();
		foreach (var v in values) {
			if (!string.IsNullOrEmpty (v))
				list.Add (v);
		}

		if (list.Count == 0) {
			SetTextFrame (frameId, null);
			return;
		}

		// Join with null for ID3v2.4 multi-value support
		var joined = string.Join ("\0", list);
		SetTextFrame (frameId, joined);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TPE1 (Lead performer) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override string[] Performers {
		get {
			var values = GetTextFrameValues ("TPE1");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TPE1", null);
				return;
			}
			SetTextFrame ("TPE1", string.Join ("\0", value));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TPE2 (Album artist) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec.
	/// </remarks>
	public override string[] AlbumArtists {
		get {
			var values = GetTextFrameValues ("TPE2");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TPE2", null);
				return;
			}
			SetTextFrame ("TPE2", string.Join ("\0", value));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TCOM (Composer) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec.
	/// </remarks>
	public override string[] Composers {
		get {
			var values = GetTextFrameValues ("TCOM");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TCOM", null);
				return;
			}
			SetTextFrame ("TCOM", string.Join ("\0", value));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses the TCON (Genre) frame. Multiple values are stored
	/// with null separators per ID3v2.4 spec.
	/// </remarks>
	public override string[] Genres {
		get {
			var values = GetTextFrameValues ("TCON");
			return values.Count > 0 ? [.. values] : [];
		}
		set {
			if (value is null || value.Length == 0) {
				SetTextFrame ("TCON", null);
				return;
			}
			SetTextFrame ("TCON", string.Join ("\0", value));
		}
	}
#pragma warning restore CA1819

	/// <summary>
	/// Sets or creates a text frame.
	/// </summary>
	/// <param name="frameId">The frame ID.</param>
	/// <param name="value">The text value, or null to remove.</param>
	void SetTextFrame (string frameId, string? value)
	{
		// Remove existing frame
		_frames.RemoveAll (f => f.Id == frameId);

		// Add new frame if value is not empty
		if (!string.IsNullOrEmpty (value))
			_frames.Add (new TextFrame (frameId, value!, TextEncodingType.Utf8));
	}

	/// <summary>
	/// Adds a picture frame to the tag.
	/// </summary>
	/// <param name="picture">The picture frame to add.</param>
	public void AddPicture (PictureFrame picture)
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
	public void RemovePictures (Core.PictureType pictureType)
	{
		_pictures.RemoveAll (p => p.PictureType == pictureType);
	}

	/// <summary>
	/// Gets the first picture of a specific type.
	/// </summary>
	/// <param name="pictureType">The picture type to find.</param>
	/// <returns>The picture frame, or null if not found.</returns>
	public PictureFrame? GetPicture (Core.PictureType pictureType)
	{
		// Use for loop instead of LINQ for better performance
		for (var i = 0; i < _pictures.Count; i++) {
			if (_pictures[i].PictureType == pictureType)
				return _pictures[i];
		}
		return null;
	}

	/// <summary>
	/// Sets or removes a picture of a specific type.
	/// </summary>
	/// <param name="pictureType">The picture type to set.</param>
	/// <param name="picture">The picture to set, or null to remove all pictures of this type.</param>
	/// <remarks>
	/// This method performs an atomic replace: it removes all existing pictures of the
	/// specified type and adds the new picture (if not null) in a single operation.
	/// </remarks>
	public void SetPicture (Core.PictureType pictureType, PictureFrame? picture)
	{
		RemovePictures (pictureType);
		if (picture is not null)
			_pictures.Add (picture);
	}

	/// <summary>
	/// Removes all pictures from this tag.
	/// </summary>
	public void RemoveAllPictures ()
	{
		_pictures.Clear ();
	}

	/// <inheritdoc/>
	public override BinaryData Render () => Render (DefaultPaddingSize);

	/// <summary>
	/// Renders the tag to binary data with specified padding.
	/// </summary>
	/// <param name="paddingSize">The amount of padding to include.</param>
	/// <returns>The rendered tag data.</returns>
	public BinaryData Render (int paddingSize)
	{
		// Render all frames and calculate size in single pass
		var frameDataList = new List<BinaryData> ((_frames.Count + _pictures.Count + _comments.Count) * 2);
		var framesSize = 0;

		for (var i = 0; i < _frames.Count; i++) {
			var content = _frames[i].RenderContent ();
			var frameHeader = RenderFrameHeader (_frames[i].Id, content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render picture frames
		for (var i = 0; i < _pictures.Count; i++) {
			var content = _pictures[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("APIC", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render comment frames
		for (var i = 0; i < _comments.Count; i++) {
			var content = _comments[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("COMM", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render user-defined text frames (TXXX)
		for (var i = 0; i < _userTextFrames.Count; i++) {
			var content = _userTextFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("TXXX", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render lyrics frames (USLT)
		for (var i = 0; i < _lyricsFrames.Count; i++) {
			var content = _lyricsFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("USLT", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render unique file identifier frames (UFID)
		for (var i = 0; i < _uniqueFileIdFrames.Count; i++) {
			var content = _uniqueFileIdFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("UFID", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render involved people frames (TIPL, TMCL)
		for (var i = 0; i < _involvedPeopleFrames.Count; i++) {
			var content = _involvedPeopleFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader (_involvedPeopleFrames[i].Id, content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render popularimeter frames (POPM)
		for (var i = 0; i < _popularimeterFrames.Count; i++) {
			var content = _popularimeterFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("POPM", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render URL frames (W*** except WXXX)
		for (var i = 0; i < _urlFrames.Count; i++) {
			var content = _urlFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader (_urlFrames[i].Id, content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render user URL frames (WXXX)
		for (var i = 0; i < _userUrlFrames.Count; i++) {
			var content = _userUrlFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("WXXX", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render synchronized lyrics frames (SYLT)
		for (var i = 0; i < _syncLyricsFrames.Count; i++) {
			var content = _syncLyricsFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("SYLT", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render general encapsulated object frames (GEOB)
		for (var i = 0; i < _generalObjectFrames.Count; i++) {
			var content = _generalObjectFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("GEOB", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render private frames (PRIV)
		for (var i = 0; i < _privateFrames.Count; i++) {
			var content = _privateFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("PRIV", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render chapter frames (CHAP)
		for (var i = 0; i < _chapterFrames.Count; i++) {
			var content = _chapterFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("CHAP", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render table of contents frames (CTOC)
		for (var i = 0; i < _tableOfContentsFrames.Count; i++) {
			var content = _tableOfContentsFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("CTOC", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		var totalSize = framesSize + paddingSize;

		// Render header
		var header = new Id3v2Header (
			(byte)Version,
			0,
			Id3v2HeaderFlags.None,
			(uint)totalSize);

		using var builder = new BinaryDataBuilder (Id3v2Header.HeaderSize + totalSize);
		builder.Add (header.Render ());

		foreach (var frameData in frameDataList)
			builder.Add (frameData);

		builder.AddZeros (paddingSize);

		return builder.ToBinaryData ();
	}

	BinaryData RenderFrameHeader (string frameId, int contentSize)
	{
		using var builder = new BinaryDataBuilder (FrameHeaderSize);

		// Frame ID (4 bytes)
		var idBytes = System.Text.Encoding.ASCII.GetBytes (frameId);
		builder.Add (idBytes.AsSpan ().Slice (0, 4));

		// Size (syncsafe for v2.4, big-endian for v2.3)
		if (Version == 4)
			builder.AddSyncSafeUInt32 ((uint)contentSize);
		else
			builder.AddUInt32BE ((uint)contentSize);

		// Flags (2 bytes, zeros)
		builder.AddUInt16BE (0);

		return builder.ToBinaryData ();
	}

	/// <inheritdoc/>
	public override void Clear ()
	{
		_frames.Clear ();
		_pictures.Clear ();
		_comments.Clear ();
		_userTextFrames.Clear ();
		_lyricsFrames.Clear ();
		_uniqueFileIdFrames.Clear ();
		_involvedPeopleFrames.Clear ();
		_popularimeterFrames.Clear ();
		_urlFrames.Clear ();
		_userUrlFrames.Clear ();
		_syncLyricsFrames.Clear ();
		_generalObjectFrames.Clear ();
		_privateFrames.Clear ();
		_chapterFrames.Clear ();
		_tableOfContentsFrames.Clear ();
		InvalidatePerformersRoleCache ();
	}

	/// <summary>
	/// Invalidates the cached PerformersRole value.
	/// </summary>
	void InvalidatePerformersRoleCache ()
	{
		_performersRoleCacheValid = false;
		_performersRoleCache = null;
	}

	/// <summary>
	/// Gets the first comment with an empty description, or the first comment if none have empty description.
	/// </summary>
	string? GetComment ()
	{
		if (_comments.Count == 0)
			return null;

		// Prefer comments with empty description (default comment)
		for (var i = 0; i < _comments.Count; i++) {
			if (string.IsNullOrEmpty (_comments[i].Description))
				return _comments[i].Text;
		}

		// Fall back to first comment
		return _comments[0].Text;
	}

	/// <summary>
	/// Sets or creates a comment with an empty description.
	/// </summary>
	void SetComment (string? value)
	{
		// Remove existing comments with empty description
		_comments.RemoveAll (c => string.IsNullOrEmpty (c.Description));

		// Add new comment if value is not empty
		if (!string.IsNullOrEmpty (value))
			_comments.Add (new CommentFrame (value!));
	}

	/// <summary>
	/// Adds a comment frame to the tag.
	/// </summary>
	/// <param name="comment">The comment frame to add.</param>
	public void AddComment (CommentFrame comment)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (comment is null)
			throw new ArgumentNullException (nameof (comment));
#else
		ArgumentNullException.ThrowIfNull (comment);
#endif
		_comments.Add (comment);
	}

	/// <summary>
	/// Removes all comments with the specified language and description.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	public void RemoveComments (string? language = null, string? description = null)
	{
		_comments.RemoveAll (c =>
			(language is null || c.Language == language) &&
			(description is null || c.Description == description));
	}

	/// <summary>
	/// Gets the first comment matching the specified criteria.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	/// <returns>The comment frame, or null if not found.</returns>
	public CommentFrame? GetCommentFrame (string? language = null, string? description = null)
	{
		for (var i = 0; i < _comments.Count; i++) {
			var c = _comments[i];
			if ((language is null || c.Language == language) &&
				(description is null || c.Description == description))
				return c;
		}
		return null;
	}

	/// <summary>
	/// Gets the value of a user-defined text frame (TXXX) by description.
	/// </summary>
	/// <param name="description">The description (key) of the TXXX frame.</param>
	/// <returns>The value, or null if not found.</returns>
	public string? GetUserText (string description)
	{
		for (var i = 0; i < _userTextFrames.Count; i++) {
			if (string.Equals (_userTextFrames[i].Description, description, StringComparison.OrdinalIgnoreCase))
				return _userTextFrames[i].Value;
		}
		return null;
	}

	/// <summary>
	/// Sets or removes a user-defined text frame (TXXX) by description.
	/// </summary>
	/// <param name="description">The description (key) of the TXXX frame.</param>
	/// <param name="value">The value to set, or null to remove the frame.</param>
	public void SetUserText (string description, string? value)
	{
		// Remove existing frame with this description
		_userTextFrames.RemoveAll (f =>
			string.Equals (f.Description, description, StringComparison.OrdinalIgnoreCase));

		// Add new frame if value is not empty
		if (!string.IsNullOrEmpty (value))
			_userTextFrames.Add (new UserTextFrame (description, value!, TextEncodingType.Utf8));
	}

	/// <summary>
	/// Adds a user-defined text frame to the tag.
	/// </summary>
	/// <param name="frame">The TXXX frame to add.</param>
	public void AddUserTextFrame (UserTextFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_userTextFrames.Add (frame);
	}

	/// <summary>
	/// Removes all user-defined text frames with the specified description.
	/// </summary>
	/// <param name="description">The description to match (case-insensitive).</param>
	public void RemoveUserTextFrames (string description)
	{
		_userTextFrames.RemoveAll (f =>
			string.Equals (f.Description, description, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Gets the first lyrics with an empty description, or the first lyrics if none have empty description.
	/// </summary>
	string? GetLyrics ()
	{
		if (_lyricsFrames.Count == 0)
			return null;

		// Prefer lyrics with empty description (default lyrics)
		for (var i = 0; i < _lyricsFrames.Count; i++) {
			if (string.IsNullOrEmpty (_lyricsFrames[i].Description))
				return _lyricsFrames[i].Text;
		}

		// Fall back to first lyrics
		return _lyricsFrames[0].Text;
	}

	/// <summary>
	/// Sets or creates lyrics with an empty description.
	/// </summary>
	void SetLyrics (string? value)
	{
		// Remove existing lyrics with empty description
		_lyricsFrames.RemoveAll (l => string.IsNullOrEmpty (l.Description));

		// Add new lyrics if value is not empty
		if (!string.IsNullOrEmpty (value))
			_lyricsFrames.Add (new LyricsFrame (value!));
	}

	/// <summary>
	/// Adds a lyrics frame to the tag.
	/// </summary>
	/// <param name="lyrics">The lyrics frame to add.</param>
	public void AddLyrics (LyricsFrame lyrics)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (lyrics is null)
			throw new ArgumentNullException (nameof (lyrics));
#else
		ArgumentNullException.ThrowIfNull (lyrics);
#endif
		_lyricsFrames.Add (lyrics);
	}

	/// <summary>
	/// Removes all lyrics with the specified language and description.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	public void RemoveLyrics (string? language = null, string? description = null)
	{
		_lyricsFrames.RemoveAll (l =>
			(language is null || l.Language == language) &&
			(description is null || l.Description == description));
	}

	/// <summary>
	/// Gets the first lyrics matching the specified criteria.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	/// <returns>The lyrics frame, or null if not found.</returns>
	public LyricsFrame? GetLyricsFrame (string? language = null, string? description = null)
	{
		for (var i = 0; i < _lyricsFrames.Count; i++) {
			var l = _lyricsFrames[i];
			if ((language is null || l.Language == language) &&
				(description is null || l.Description == description))
				return l;
		}
		return null;
	}

	/// <summary>
	/// Adds a unique file identifier frame to the tag.
	/// </summary>
	/// <param name="frame">The UFID frame to add.</param>
	public void AddUniqueFileId (UniqueFileIdFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_uniqueFileIdFrames.Add (frame);
	}

	/// <summary>
	/// Gets the first unique file identifier frame with the specified owner.
	/// </summary>
	/// <param name="owner">The owner identifier to match (case-insensitive).</param>
	/// <returns>The UFID frame, or null if not found.</returns>
	public UniqueFileIdFrame? GetUniqueFileId (string owner)
	{
		for (var i = 0; i < _uniqueFileIdFrames.Count; i++) {
			if (string.Equals (_uniqueFileIdFrames[i].Owner, owner, StringComparison.OrdinalIgnoreCase))
				return _uniqueFileIdFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Removes all unique file identifier frames with the specified owner.
	/// </summary>
	/// <param name="owner">The owner identifier to match (case-insensitive), or null to remove all.</param>
	public void RemoveUniqueFileIds (string? owner = null)
	{
		if (owner is null)
			_uniqueFileIdFrames.Clear ();
		else
			_uniqueFileIdFrames.RemoveAll (f =>
				string.Equals (f.Owner, owner, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Adds a synchronized lyrics frame to the tag.
	/// </summary>
	/// <param name="frame">The SYLT frame to add.</param>
	public void AddSyncLyrics (SyncLyricsFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_syncLyricsFrames.Add (frame);
	}

	/// <summary>
	/// Gets the first synchronized lyrics frame matching the specified criteria.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	/// <returns>The SYLT frame, or null if not found.</returns>
	public SyncLyricsFrame? GetSyncLyrics (string? language = null, string? description = null)
	{
		for (var i = 0; i < _syncLyricsFrames.Count; i++) {
			var frame = _syncLyricsFrames[i];
			if ((language is null || frame.Language == language) &&
				(description is null || frame.Description == description))
				return frame;
		}
		return null;
	}

	/// <summary>
	/// Removes all synchronized lyrics frames matching the specified criteria.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	public void RemoveSyncLyrics (string? language = null, string? description = null)
	{
		if (language is null && description is null)
			_syncLyricsFrames.Clear ();
		else
			_syncLyricsFrames.RemoveAll (f =>
				(language is null || f.Language == language) &&
				(description is null || f.Description == description));
	}

	/// <summary>
	/// Adds a general encapsulated object frame to the tag.
	/// </summary>
	/// <param name="frame">The GEOB frame to add.</param>
	public void AddGeneralObject (GeneralObjectFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_generalObjectFrames.Add (frame);
	}

	/// <summary>
	/// Gets the first general encapsulated object frame matching the specified criteria.
	/// </summary>
	/// <param name="description">The description to match (case-insensitive), or null to match any.</param>
	/// <returns>The GEOB frame, or null if not found.</returns>
	public GeneralObjectFrame? GetGeneralObject (string? description = null)
	{
		for (var i = 0; i < _generalObjectFrames.Count; i++) {
			if (description is null ||
				string.Equals (_generalObjectFrames[i].Description, description, StringComparison.OrdinalIgnoreCase))
				return _generalObjectFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Removes all general encapsulated object frames matching the specified description.
	/// </summary>
	/// <param name="description">The description to match (case-insensitive), or null to remove all.</param>
	public void RemoveGeneralObjects (string? description = null)
	{
		if (description is null)
			_generalObjectFrames.Clear ();
		else
			_generalObjectFrames.RemoveAll (f =>
				string.Equals (f.Description, description, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Adds a private frame to the tag.
	/// </summary>
	/// <param name="frame">The PRIV frame to add.</param>
	public void AddPrivateFrame (PrivateFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_privateFrames.Add (frame);
	}

	/// <summary>
	/// Gets the first private frame with the specified owner ID.
	/// </summary>
	/// <param name="ownerId">The owner ID to match (case-insensitive), or null to match any.</param>
	/// <returns>The PRIV frame, or null if not found.</returns>
	public PrivateFrame? GetPrivateFrame (string? ownerId = null)
	{
		for (var i = 0; i < _privateFrames.Count; i++) {
			if (ownerId is null ||
				string.Equals (_privateFrames[i].OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
				return _privateFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Removes all private frames with the specified owner ID.
	/// </summary>
	/// <param name="ownerId">The owner ID to match (case-insensitive), or null to remove all.</param>
	public void RemovePrivateFrames (string? ownerId = null)
	{
		if (ownerId is null)
			_privateFrames.Clear ();
		else
			_privateFrames.RemoveAll (f =>
				string.Equals (f.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Adds a chapter frame to the tag.
	/// </summary>
	/// <param name="frame">The CHAP frame to add.</param>
	public void AddChapter (ChapterFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_chapterFrames.Add (frame);
	}

	/// <summary>
	/// Gets a chapter frame by element ID.
	/// </summary>
	/// <param name="elementId">The element ID to match (case-sensitive), or null to get the first chapter.</param>
	/// <returns>The CHAP frame, or null if not found.</returns>
	public ChapterFrame? GetChapter (string? elementId = null)
	{
		for (var i = 0; i < _chapterFrames.Count; i++) {
			if (elementId is null || _chapterFrames[i].ElementId == elementId)
				return _chapterFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Removes a chapter frame by element ID.
	/// </summary>
	/// <param name="elementId">The element ID to match (case-sensitive), or null to remove all chapters.</param>
	public void RemoveChapters (string? elementId = null)
	{
		if (elementId is null)
			_chapterFrames.Clear ();
		else
			_chapterFrames.RemoveAll (f => f.ElementId == elementId);
	}

	/// <summary>
	/// Adds a table of contents frame to the tag.
	/// </summary>
	/// <param name="frame">The CTOC frame to add.</param>
	public void AddTableOfContents (TableOfContentsFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_tableOfContentsFrames.Add (frame);
	}

	/// <summary>
	/// Gets the top-level table of contents frame, or the first one if none is marked top-level.
	/// </summary>
	/// <returns>The CTOC frame, or null if no table of contents exists.</returns>
#pragma warning disable CA1024 // Method has business logic to find top-level TOC first
	public TableOfContentsFrame? GetTableOfContents ()
#pragma warning restore CA1024
	{
		// First try to find the top-level TOC
		for (var i = 0; i < _tableOfContentsFrames.Count; i++) {
			if (_tableOfContentsFrames[i].IsTopLevel)
				return _tableOfContentsFrames[i];
		}

		// Fall back to first TOC if no top-level exists
		return _tableOfContentsFrames.Count > 0 ? _tableOfContentsFrames[0] : null;
	}

	/// <summary>
	/// Gets a table of contents frame by element ID.
	/// </summary>
	/// <param name="elementId">The element ID to match (case-sensitive).</param>
	/// <returns>The CTOC frame, or null if not found.</returns>
	public TableOfContentsFrame? GetTableOfContentsById (string elementId)
	{
		for (var i = 0; i < _tableOfContentsFrames.Count; i++) {
			if (_tableOfContentsFrames[i].ElementId == elementId)
				return _tableOfContentsFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Removes a table of contents frame by element ID.
	/// </summary>
	/// <param name="elementId">The element ID to match (case-sensitive), or null to remove all.</param>
	public void RemoveTableOfContents (string? elementId = null)
	{
		if (elementId is null)
			_tableOfContentsFrames.Clear ();
		else
			_tableOfContentsFrames.RemoveAll (f => f.ElementId == elementId);
	}

	static string GetFrameId (ReadOnlySpan<byte> data)
	{
		if (data.Length < 4)
			return string.Empty;

		// Frame ID must be uppercase A-Z or 0-9
		for (var i = 0; i < 4; i++) {
			var c = data[i];
			if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
				return string.Empty;
		}

		return System.Text.Encoding.ASCII.GetString (data.Slice (0, 4));
	}

	static int GetFrameSize (ReadOnlySpan<byte> data, byte version)
	{
		if (version == 4) {
			// Syncsafe integer (7 bits per byte, max 28 bits = 268MB)
			return ((data[0] & 0x7F) << 21) |
				   ((data[1] & 0x7F) << 14) |
				   ((data[2] & 0x7F) << 7) |
				   (data[3] & 0x7F);
		} else {
			// Big-endian 32-bit unsigned, cast to uint to avoid overflow when MSB >= 0x80.
			// Values > int.MaxValue are unrealistic for frame sizes and will fail
			// bounds checking in the caller, so we safely cast back to int.
			return (int)(((uint)data[0] << 24) |
						 ((uint)data[1] << 16) |
						 ((uint)data[2] << 8) |
						 (uint)data[3]);
		}
	}

	static int GetExtendedHeaderSize (ReadOnlySpan<byte> data, byte version)
	{
		if (data.Length < 4)
			return 0;

		if (version == 4) {
			// v2.4: Size is syncsafe and INCLUDES the size field itself
			return ((data[0] & 0x7F) << 21) |
				   ((data[1] & 0x7F) << 14) |
				   ((data[2] & 0x7F) << 7) |
				   (data[3] & 0x7F);
		} else {
			// v2.3: Size is big-endian and EXCLUDES the size field
			// Total size = 4 (size field) + extended header size
			var extSize = (int)(((uint)data[0] << 24) |
								((uint)data[1] << 16) |
								((uint)data[2] << 8) |
								(uint)data[3]);
			return extSize + 4;
		}
	}
}
