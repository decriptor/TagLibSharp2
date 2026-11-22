// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// Matroska <c>Tags</c> element projected onto the common tag surface.
/// </summary>
/// <remarks>
/// A value applies from its <c>TargetTypeValue</c> down to more specific levels until a level
/// sets that name, including an empty string. <c>TITLE</c> at 50 is the album or movie.
/// <c>TITLE</c> at 30 is the track. Chapter and attachment targets are not the file.
/// <para>
/// <see cref="Track"/> is <c>PART_NUMBER</c> at 30 and <see cref="DiscNumber"/> is <c>PART_NUMBER</c> at 50.
/// <c>TOTAL_PARTS</c> counts the parts of the first lower level, so <see cref="TotalTracks"/> is
/// <c>TOTAL_PARTS</c> at 50 and <see cref="TotalDiscs"/> is <c>TOTAL_PARTS</c> at 60, as in
/// https://www.matroska.org/technical/tagging-audio-example.html. TagLib writes these totals at 30 and 50,
/// so a TagLib-written file with both totals reads its disc total as <see cref="TotalTracks"/>. A 30
/// <c>TOTAL_PARTS</c> is read as <see cref="TotalTracks"/> only when no other source exists, and an untargeted
/// <c>PART_NUMBER</c>, which ffmpeg writes, is read as the track. A <see cref="TotalDiscs"/> set on a file with
/// no 40 or 50 tag and no disc number is still written at 60, where the spec reads it as a count of the 30 tags.
/// </para>
/// </remarks>
public sealed class MatroskaTag : Tag
{
	const ulong CollectionLevel = 70;
	const ulong VolumeLevel = 60;
	const ulong AlbumLevel = 50;
	const ulong PartLevel = 40;
	const ulong TrackLevel = 30;
	const string AlbumArtistName = "ALBUM_ARTIST";
	const string AlbumArtistSortName = "ALBUM_ARTIST_SORT";

	readonly List<TagBlock> _blocks = new ();
	string? _segmentTitle;

	internal MatroskaTag ()
	{
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Matroska;

	/// <inheritdoc/>
	public override string? Title {
		get => FirstOf (TitlesRead ());
		set {
			if (IsWriteBackAt (value, TitlesRead (), TrackLevel, "TITLE"))
				return;
			SetValue (TrackLevel, "TITLE", value, [], ["TITLE_SORT"], shadows: false);
		}
	}

	/// <summary>
	/// The titles Title reads, the first of which it returns; its setter's write-back check reads the same list.
	/// TITLE at 30 is the track, and an empty one blocks every broader title. The segment title is the file
	/// title. Otherwise the most specific file-level TITLE below a collection still displays, so a part,
	/// edition, or lone album title is not a blank name.
	/// </summary>
	string[] TitlesRead ()
	{
		if (HasFileLevelName (TrackLevel, "TITLE"))
			return AllAt (TrackLevel, "TITLE");
		if (!string.IsNullOrEmpty (_segmentTitle))
			return [_segmentTitle!];
		return MostSpecificFileTitleBelowCollection () is ulong level ? AllAt (level, "TITLE") : [];
	}

	static string? FirstOf (string[] values) => values.Length > 0 ? values[0] : null;

	/// <inheritdoc/>
	public override string? Artist {
		get => FirstOf (All ("ARTIST"));
		set {
			if (IsWriteBackAt (value, All ("ARTIST"), TrackLevel, "ARTIST"))
				return;
			SetValue (TrackLevel, "ARTIST", value, ["ARTIST_SORT"], ["ARTIST_SORT"], shadows: true);
		}
	}

	/// <inheritdoc/>
	public override string[] Performers {
		get {
			var artists = All ("ARTIST");
			return artists.Length > 0 ? artists : All ("ACTOR");
		}
		set {
			if (IsListWriteBack (value, Performers, AnyTextAt (TrackLevel, "ARTIST"), clearsOneLevel: true))
				return;
			SetValues (TrackLevel, "ARTIST", value, ["ARTIST_SORT"], ["ARTIST_SORT"], shadows: true);
		}
	}

	/// <inheritdoc/>
	public override string? Album {
		get => FirstOf (AllAt (AlbumLevel, "TITLE"));
		set {
			if (IsWriteBackAt (value, AllAt (AlbumLevel, "TITLE"), AlbumLevel, "TITLE"))
				return;
			SetValue (AlbumLevel, "TITLE", value, [], ["ALBUM_SORT"], shadows: false);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The album artist is <c>ARTIST</c> at the album level (<c>TargetTypeValue</c> 50), per
	/// https://www.matroska.org/technical/tagging.html. A file that carries a flat <c>ALBUM_ARTIST</c>
	/// (the name ffmpeg writes) names its album artist there instead: it is read and written, and
	/// clearing it also removes its flat <c>ALBUM_ARTIST_SORT</c>, so the specification's album-level
	/// <c>ARTIST</c> shows through.
	/// </remarks>
	public override string[] AlbumArtists {
		get {
			if (UsesAlbumArtistName)
				return All (AlbumArtistName);
			return ItemLevel >= AlbumLevel ? All ("ARTIST") : AllAt (AlbumLevel, "ARTIST");
		}
		set {
			// A clear of ALBUM_ARTIST removes every visible level; the spec-level write is one level.
			var byName = UsesAlbumArtistName;
			if (IsListWriteBack (value, AlbumArtists, byName ? AnyText (AlbumArtistName) : AnyTextAt (AlbumLevel, "ARTIST"), clearsOneLevel: !byName))
				return;
			if (!UsesAlbumArtistName) {
				// ALBUM_ARTIST_SORT at 50 is the album artist's whatever the track has, so only ARTIST_SORT can be the track's.
				SetValues (AlbumLevel, "ARTIST", value, ["ARTIST_SORT"], ["ARTIST_SORT", AlbumArtistSortName], shadows: true);
				return;
			}

			if (!HasText (value)) {
				RemoveVisible (AlbumArtistName);
				RemoveVisible (AlbumArtistSortName);
				return;
			}

			SetAtValues (ChosenLevel (AlbumArtistName)!.Value, AlbumArtistName, value);
		}
	}

	/// <inheritdoc/>
	public override string? Year {
		get => First ("DATE_RELEASED");
		set => SetItem ("DATE_RELEASED", value);
	}

	/// <inheritdoc/>
	public override string? OriginalReleaseDate {
		get => First ("DATE_RECORDED");
		set => SetItem ("DATE_RECORDED", value);
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => First ("COMMENT");
		set => SetItem ("COMMENT", value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => First ("GENRE");
		set => SetItem ("GENRE", value);
	}

	/// <inheritdoc/>
	public override string[] Genres {
		get => All ("GENRE");
		set => SetItemValues ("GENRE", value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => ParseLeading (TrackPartNumber ());
		set {
			// Changing the number keeps TotalTracks, including a total read from the old "4/12".
			var total = TrackTotal ();
			if (value is null)
				RemoveWhere (IsTrackTarget, "PART_NUMBER");
			else
				SetTrackNumber (value.Value.ToString (CultureInfo.InvariantCulture));
			if (total is not null && TrackTotal () != total)
				SetTrackTotal (total);
		}
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get => TrackTotal ();
		set => SetTrackTotal (value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		// PART_NUMBER is the disc only when TargetTypeValue is stored and equal to 50.
		get => ParseLeading (DiscPartNumber ());
		set {
			// Changing the number keeps TotalDiscs, including a total read from the old "2/3".
			var total = DiscTotal ();
			RemoveDiscNames ();
			if (value is null) {
				RemoveWhere (IsExplicitAlbum, "PART_NUMBER");
			} else {
				var home = DiscHome ();
				ReplaceNumber (home, "PART_NUMBER", value.Value.ToString (CultureInfo.InvariantCulture));
				RemoveOthers (IsExplicitAlbum, home, "PART_NUMBER");
			}

			if (total is not null && DiscTotal () != total)
				SetDiscTotal (total);
		}
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => DiscTotal ();
		set => SetDiscTotal (value);
	}

	/// <inheritdoc/>
	public override string[] Composers {
		get => All ("COMPOSER");
		set {
			// The write-back check runs first, as in every setter. A clear takes the composers' flat sorts with
			// them; a COMPOSER_SORT with no COMPOSER is left alone.
			if (IsListWriteBack (value, Composers, AnyText ("COMPOSER"), clearsOneLevel: false))
				return;
			if (!HasText (value) && ChosenLevel ("COMPOSER") is not null)
				RemoveVisible ("COMPOSER_SORT");
			SetItemValues ("COMPOSER", value);
		}
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => First ("COPYRIGHT");
		set => SetItem ("COPYRIGHT", value);
	}

	/// <inheritdoc/>
	public override string? Description {
		get => First ("DESCRIPTION");
		set => SetItem ("DESCRIPTION", value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => First ("LYRICS");
		set => SetItem ("LYRICS", value);
	}

	/// <inheritdoc/>
	public override string? Subtitle {
		get => First ("SUBTITLE");
		set => SetItem ("SUBTITLE", value);
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get => ParseWholeOrDecimal (First ("BPM"));
		set {
			// "120.0" reads as 120; writing 120 back is not a change, but a copy replaces a list of them.
			if (value is not null && value == BeatsPerMinute && (!IsCopying || All ("BPM").Length == 1))
				return;
			SetItem ("BPM", value?.ToString (CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => First ("CONDUCTOR");
		set => SetItem ("CONDUCTOR", value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => First ("PUBLISHER") ?? First ("LABEL");
		set => SetItemWithFallback ("PUBLISHER", "LABEL", value, Publisher, replaceFallback: false);
	}

	/// <inheritdoc/>
	public override string? EncodedBy {
		get => First ("ENCODED_BY");
		set => SetItem ("ENCODED_BY", value);
	}

	/// <inheritdoc/>
	public override string? EncoderSettings {
		get => First ("ENCODER_SETTINGS");
		set => SetItem ("ENCODER_SETTINGS", value);
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => First ("GROUPING");
		set => SetItem ("GROUPING", value);
	}

	/// <inheritdoc/>
	public override string? Remixer {
		get => First ("REMIXED_BY");
		set => SetItem ("REMIXED_BY", value);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => First ("ISRC");
		set => SetItem ("ISRC", value);
	}

	/// <inheritdoc/>
	public override string? CatalogNumber {
		get => First ("CATALOG_NUMBER");
		set => SetItem ("CATALOG_NUMBER", value);
	}

	/// <inheritdoc/>
	public override string? Barcode {
		get => First ("BARCODE");
		set => SetItem ("BARCODE", value);
	}

	/// <inheritdoc/>
	public override string? InitialKey {
		get => First ("INITIAL_KEY");
		set => SetItem ("INITIAL_KEY", value);
	}

	/// <inheritdoc/>
	public override string? Mood {
		get => First ("MOOD");
		set => SetItem ("MOOD", value);
	}

	/// <inheritdoc/>
	public override string? DateTagged {
		get => First ("DATE_TAGGED");
		set => SetItem ("DATE_TAGGED", value);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => ReadSort ("TITLE", "TITLE_SORT", TrackLevel);
		set {
			if (IsSortWriteBack (value, "TITLE", "TITLE_SORT", TrackLevel))
				return;
			SetSort ("TITLE", "TITLE_SORT", value, TrackLevel);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <c>SORT_WITH</c> nested in the <c>ARTIST</c> that <see cref="Artist"/> reads, else the flat
	/// <c>ARTIST_SORT</c> that belongs to it: in its Tag, in a broader Tag that repeats the same artist, or
	/// passed down from a broader Tag with no <c>ARTIST</c>. A broader Tag with a different artist keeps its
	/// <c>ARTIST_SORT</c> for that artist.
	/// </remarks>
	public override string? ArtistSort {
		get => ReadSort ("ARTIST", "ARTIST_SORT", null);
		set {
			if (IsSortWriteBack (value, "ARTIST", "ARTIST_SORT", null))
				return;
			SplitSharedArtist (NullIfEmpty (value));
			SetSort ("ARTIST", "ARTIST_SORT", value, null);
		}
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => ReadSort ("TITLE", "ALBUM_SORT", AlbumLevel);
		set {
			if (IsSortWriteBack (value, "TITLE", "ALBUM_SORT", AlbumLevel))
				return;
			SetSort ("TITLE", "ALBUM_SORT", value, AlbumLevel);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// The sort of the album artist that <see cref="AlbumArtists"/> reads: <c>SORT_WITH</c> nested in
	/// that SimpleTag, else the flat <c>ALBUM_ARTIST_SORT</c> that belongs to it: in its Tag, in a broader
	/// Tag that repeats the same artist, or passed down from one with no artist. For a flat <c>ALBUM_ARTIST</c> that is its own
	/// <c>SORT_WITH</c> or <c>ALBUM_ARTIST_SORT</c>.
	/// See https://www.matroska.org/technical/tagging.html ("Nested Information", "Target types")
	/// and RFC 9559 section 5.1.8.
	/// </para>
	/// <para>
	/// When the file has no tag below the album level, the album-level <c>ARTIST</c> is also the
	/// track's artist, and one <c>SORT_WITH</c> is read as both this and <see cref="ArtistSort"/>.
	/// Before a write would change one of them, the <c>ARTIST</c> is copied to the track level (30),
	/// so the track keeps its own sort and the two stay independent.
	/// </para>
	/// </remarks>
	public override string? AlbumArtistSort {
		get => ReadSort (AlbumArtistValueName, AlbumArtistSortName, AlbumArtistSortLevel);
		set {
			if (IsSortWriteBack (value, AlbumArtistValueName, AlbumArtistSortName, AlbumArtistSortLevel))
				return;
			if (UsesAlbumArtistName) {
				SetSort (AlbumArtistName, AlbumArtistSortName, value, null);
				return;
			}

			SplitSharedArtist (NullIfEmpty (value));
			SetSort ("ARTIST", AlbumArtistSortName, value, AlbumArtistSortLevel);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <c>SORT_WITH</c> nested in the <c>COMPOSER</c> that <see cref="Composers"/> reads, else the flat
	/// <c>COMPOSER_SORT</c> that belongs to it: at its level, at a broader level that repeats the same
	/// composer, or passed down from one with no <c>COMPOSER</c>. See https://www.matroska.org/technical/tagging.html.
	/// </remarks>
	public override string? ComposerSort {
		get => ReadSort ("COMPOSER", "COMPOSER_SORT", null);
		set {
			if (IsSortWriteBack (value, "COMPOSER", "COMPOSER_SORT", null))
				return;
			SetSort ("COMPOSER", "COMPOSER_SORT", value, null);
		}
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => First ("REPLAYGAIN_TRACK_GAIN") ?? First ("REPLAYGAIN_GAIN");
		set => SetItemReplacingLegacy ("REPLAYGAIN_TRACK_GAIN", "REPLAYGAIN_GAIN", value, ReplayGainTrackGain);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => First ("REPLAYGAIN_TRACK_PEAK") ?? First ("REPLAYGAIN_PEAK");
		set => SetItemReplacingLegacy ("REPLAYGAIN_TRACK_PEAK", "REPLAYGAIN_PEAK", value, ReplayGainTrackPeak);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => First ("REPLAYGAIN_ALBUM_GAIN");
		set => SetItem ("REPLAYGAIN_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => First ("REPLAYGAIN_ALBUM_PEAK");
		set => SetItem ("REPLAYGAIN_ALBUM_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => First ("MUSICBRAINZ_TRACKID");
		set => SetItem ("MUSICBRAINZ_TRACKID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => First ("MUSICBRAINZ_ALBUMID");
		set => SetItem ("MUSICBRAINZ_ALBUMID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => First ("MUSICBRAINZ_ARTISTID");
		set => SetItem ("MUSICBRAINZ_ARTISTID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => First ("MUSICBRAINZ_RELEASEGROUPID");
		set => SetItem ("MUSICBRAINZ_RELEASEGROUPID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => First ("MUSICBRAINZ_ALBUMARTISTID");
		set => SetItem ("MUSICBRAINZ_ALBUMARTISTID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzWorkId {
		get => First ("MUSICBRAINZ_WORKID");
		set => SetItem ("MUSICBRAINZ_WORKID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzDiscId {
		get => First ("MUSICBRAINZ_DISCID");
		set => SetItem ("MUSICBRAINZ_DISCID", value);
	}

	readonly List<IPicture> _pictures = new ();

	/// <summary>
	/// Gets or sets the image attachments (RFC 9559 section 21).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reading returns every attachment with an <c>image/</c> media type. Fonts and other attachments
	/// are not pictures and saving never removes them.
	/// </para>
	/// <para>
	/// Setting this property makes the next save rewrite the image attachments. An attachment that
	/// matches a picture by media type, type, description and bytes keeps its <c>FileName</c>,
	/// <c>FileUID</c> and element bytes, and its place in storage order. The match uses the media type
	/// as stored, so every picture read from the file matches its own attachment. A removed picture
	/// removes only its attachment. <see cref="Clear"/> removes every picture, so the next save
	/// removes the image attachments too.
	/// </para>
	/// <para>
	/// A new front cover is named <c>cover.jpg</c> or <c>cover.png</c> after its media type; another
	/// image type keeps its own extension, such as <c>cover.gif</c>. It goes first, as the cover art
	/// guidance asks, unless the file keeps its normal cover (<c>cover.*</c>); that one keeps its
	/// place and the new one goes after the kept attachments. A kept <c>small_cover</c> or
	/// <c>cover_land</c> is not the normal cover. Another new picture is named after its type, such
	/// as <c>back.jpg</c> or <c>artist.png</c>, and goes after the existing attachments. The description is written to <c>FileDescription</c>, and reading maps those names
	/// back to the picture type. A new <c>FileUID</c> is random, nonzero and unlike every
	/// <c>FileUID</c> in the file.
	/// </para>
	/// <para>
	/// A picture must have an <c>image/</c> media type; an empty one is detected from the bytes.
	/// Any other media type fails the save. JPEG and PNG are the formats the guidance recommends;
	/// another image type is written with its own media type. WebM has no attachments, so a save
	/// that would add one to a WebM file fails. A new or moved <c>Attachments</c> element goes in a
	/// Void or after the last Cluster with an entry in the first SeekHead (RFC 9559 section 6.3).
	/// When that SeekHead has no room for the entry, the element goes right after the first
	/// SeekHead, which is written again to list it, and everything after it moves once with every
	/// Segment Position following. A file with no SeekHead gets the element before its first
	/// Cluster (section 25.3.4). The save fails when the first SeekHead cannot be read or has an
	/// unknown size, or when the later elements cannot be moved, for example because their Cues
	/// are malformed. A save that does not set this property leaves the <c>Attachments</c> element
	/// byte for byte.
	/// </para>
	/// </remarks>
	public override IPicture[] Pictures {
		get => _pictures.ToArray ();
		set {
			_picturesChanged = true;
			_pictures.Clear ();
			if (value is null)
				return;
			foreach (var picture in value) {
				if (picture is not null)
					_pictures.Add (picture);
			}
		}
	}

	/// <inheritdoc/>
	public override void Clear ()
	{
		_blocks.Clear ();
		_pictures.Clear ();
		_picturesChanged = true;
	}

	bool _picturesChanged;
	byte[] _baseline = [];

	/// <summary>Records the tags as read, so a save can leave an unedited <c>Tags</c> element as it was.</summary>
	internal void MarkRead () => _baseline = Render ().ToArray ();

	/// <summary>Returns true when <paramref name="rendered"/> is the <c>Tags</c> element as read.</summary>
	internal bool IsAsRead (ReadOnlySpan<byte> rendered) => rendered.SequenceEqual (_baseline);

	/// <summary>Gets a value indicating whether <see cref="Pictures"/> was set or cleared since reading.</summary>
	internal bool PicturesChanged => _picturesChanged;

	/// <inheritdoc/>
	public override bool SupportsCustomFields => true;

	/// <inheritdoc/>
	/// <remarks>
	/// Reads the file-level <c>SimpleTag</c> elements whose <c>TagName</c> equals <paramref name="name"/>
	/// exactly, the way <see cref="Comment"/> reads <c>COMMENT</c>: the nearest level at or above the item
	/// level that sets the name wins (RFC 9559 §5.1.8.1.1 and https://www.matroska.org/technical/tagging.html).
	/// A tag with only <c>TagBinary</c> has no text and is skipped.
	/// <para>
	/// A flat sort name (<c>TITLE_SORT</c>, <c>ARTIST_SORT</c>, <c>ALBUM_SORT</c>, <c>ALBUM_ARTIST_SORT</c>,
	/// <c>COMPOSER_SORT</c>) is read, set and removed through its sort property, so it pairs with its value the
	/// same way: a sort belongs to the value it sorts, and a nested <c>SORT_WITH</c> wins over the flat name.
	/// </para>
	/// </remarks>
	public override IReadOnlyList<string> GetCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (IsSortName (name))
			return SortByName (name) is string sort ? [sort] : CustomFieldValues.None;
		return CustomFieldValues.IsUsableName (name) ? All (name) : CustomFieldValues.None;
	}

	static bool IsSortName (string name) =>
		name is "TITLE_SORT" or "ARTIST_SORT" or "ALBUM_SORT" or AlbumArtistSortName or "COMPOSER_SORT";

	string? SortByName (string name) => name switch {
		"TITLE_SORT" => TitleSort,
		"ARTIST_SORT" => ArtistSort,
		"ALBUM_SORT" => AlbumSort,
		AlbumArtistSortName => AlbumArtistSort,
		_ => ComposerSort,
	};

	void SetSortByName (string name, string? value)
	{
		switch (name) {
			case "TITLE_SORT":
				TitleSort = value;
				break;
			case "ARTIST_SORT":
				ArtistSort = value;
				break;
			case "ALBUM_SORT":
				AlbumSort = value;
				break;
			case AlbumArtistSortName:
				AlbumArtistSort = value;
				break;
			default:
				ComposerSort = value;
				break;
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Writes one <c>SimpleTag</c> per value at the item level, the level <see cref="Comment"/> and
	/// <see cref="Genre"/> write. Matroska allows a name to repeat (https://www.matroska.org/technical/tagging.html).
	/// The same name at a broader level stays and is shadowed, as the format defines. The item level is the
	/// most specific file-level target in the file, or 50 (album) when there is none, so on a file with
	/// only album tags <c>"TITLE"</c> writes the album title; <see cref="Title"/> still prefers the segment
	/// title there.
	/// <para>
	/// A value name (<c>ARTIST</c>, <c>TITLE</c>, <c>COMPOSER</c>, <c>ALBUM_ARTIST</c>, ...) is written raw: no flat
	/// sort moves with it, so a sort can be left beside a value it no longer sorts. Use the properties
	/// (<see cref="Performers"/>, <see cref="Title"/>, <see cref="Composers"/>, <see cref="AlbumArtists"/>) to keep each
	/// flat sort with its value. A flat sort name goes through its sort property (see <see cref="GetCustomField"/>).
	/// </para>
	/// </remarks>
	public override bool SetCustomField (string name, params string[] values)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!CustomFieldValues.IsUsableName (name) || !CustomFieldValues.TryNormalize (values, out var kept))
			return false;

		if (IsSortName (name)) {
			// A sort is one name for its value.
			if (kept.Count > 1)
				return false;
			SetSortByName (name, kept.Count == 0 ? null : kept[0]);
			return true;
		}

		if (kept.Count == 0) {
			RemoveCustomField (name);
			return true;
		}

		SetItemValues (name, [.. kept]);
		return true;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Removes the name at every file level the getter can climb to, so a broader value does not show
	/// through afterwards.
	/// </remarks>
	public override bool RemoveCustomField (string name)
	{
		CustomFieldValues.ThrowIfNull (name);
		if (!CustomFieldValues.IsUsableName (name))
			return false;
		if (IsSortName (name)) {
			var had = SortByName (name) is not null;
			SetSortByName (name, null);
			return had;
		}

		var item = ItemLevel;
		var removed = false;
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level < item)
				continue;
			removed |= block.Tags.RemoveAll (tag => tag.Name == name) > 0;
		}
		return removed;
	}

	internal void AddPicture (IPicture picture) => _pictures.Add (picture);

	/// <summary>
	/// Writes the current tags as one <c>Tags</c> element.
	/// </summary>
	/// <returns>The EBML element, or empty when there are no tags.</returns>
	public override BinaryData Render () => RenderWithout (null);

	/// <summary>
	/// Writes the tags without the attachments in <paramref name="removedAttachments"/>.
	/// A <c>TagAttachmentUID</c> MUST match an attachment in the Segment (RFC 9559 section 5.1.8.1.1.6).
	/// A tag left with no target at all is dropped, so it does not become a file-level tag.
	/// </summary>
	internal BinaryData RenderWithout (HashSet<ulong>? removedAttachments)
	{
		using var tags = new BinaryDataBuilder ();
		var written = 0;
		foreach (var block in _blocks) {
			// A Tag contains at least one SimpleTag (RFC 9559 section 5.1.8.1.2). One that a clear emptied is
			// dropped. One that was already empty when read is kept, so a save leaves it as it was.
			if (IsDropped (block))
				continue;
			if (removedAttachments is { Count: > 0 } && block.TargetsAttachment (removedAttachments)) {
				var kept = block.RenderWithout (removedAttachments);
				if (kept is null)
					continue;
				tags.Add (kept.Value);
			} else
				tags.Add (block.Render ());
			written++;
		}

		return written == 0 ? BinaryData.Empty : EbmlWriter.Master (MatroskaIds.Tags, tags.ToBinaryData ().Span);
	}

	static bool IsDropped (TagBlock block) => block.Tags.Count == 0 && !block.WasEmpty;

	internal void SetSegmentTitle (string? title) => _segmentTitle = title;

	internal void AddBlock (TagBlock block) => _blocks.Add (block);

	internal ulong ItemLevel {
		get {
			ulong? level = null;
			foreach (var block in _blocks) {
				t_levelSteps++;
				// A Tag that a clear emptied is not rendered, so it is not the item either.
				if (!block.IsFileLevel || IsDropped (block))
					continue;
				if (level is null || block.Level < level)
					level = block.Level;
			}

			return level ?? AlbumLevel;
		}
	}

	ulong? MostSpecificFileTitleBelowCollection ()
	{
		ulong? best = null;
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level >= CollectionLevel || !block.HasName ("TITLE"))
				continue;
			if (best is null || block.Level < best)
				best = block.Level;
		}

		return best;
	}

	bool HasFileLevelName (ulong level, string name)
	{
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (block.IsFileLevel && block.Level == level && block.HasName (name))
				return true;
		}

		return false;
	}

	// A getter's value is the first of the list its setter's write-back check reads (All, AllAt).
	string? First (string name) => FirstOf (All (name));

	string[] All (string name)
	{
		var values = Values (name);
		var count = 0;
		for (var i = 0; i < values.Count; i++) {
			if (values[i].Length > 0)
				count++;
		}

		if (count == 0)
			return [];

		var result = new string[count];
		var at = 0;
		for (var i = 0; i < values.Count; i++) {
			if (values[i].Length > 0)
				result[at++] = values[i];
		}

		return result;
	}

	string? FirstAt (ulong level, string name) => FirstOf (AllAt (level, name));

	string[] AllAt (ulong level, string name)
	{
		var values = ValuesAt (level, name);
		var count = 0;
		for (var i = 0; i < values.Count; i++) {
			if (values[i].Length > 0)
				count++;
		}

		if (count == 0)
			return [];

		var result = new string[count];
		var at = 0;
		for (var i = 0; i < values.Count; i++) {
			if (values[i].Length > 0)
				result[at++] = values[i];
		}

		return result;
	}

	/// <summary>
	/// The nearest level at or above the item that sets <paramref name="name"/> wins.
	/// An empty string at that level blocks a higher level.
	/// </summary>
	List<string> Values (string name)
	{
		var chosen = ChosenLevel (name);
		return chosen is null ? new List<string> () : ValuesAt (chosen.Value, name);
	}

	ulong? ChosenLevel (string name)
	{
		var item = ItemLevel;
		ulong? chosen = null;
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (!block.IsFileLevel || block.Level < item)
				continue;
			if (!block.HasName (name))
				continue;
			if (chosen is null || block.Level < chosen)
				chosen = block.Level;
		}

		return chosen;
	}

	/// <summary>
	/// <c>SORT_WITH</c> on the same SimpleTag that supplies <paramref name="name"/>.
	/// An empty nested sort does not borrow one from a later tag.
	/// </summary>
	string? NestedSort (string name)
	{
		var chosen = ChosenLevel (name);
		return chosen is null ? null : NestedSortAt (chosen.Value, name);
	}

	void SetSort (string name, string flatName, string? value, ulong? level)
	{
		// An empty sort is no sort, the way an empty parent value removes the parent.
		if (string.IsNullOrEmpty (value))
			value = null;
		var replaced = level is ulong fixedLevel
			? ReplaceNestedSortAt (fixedLevel, name, value)
			: ReplaceNestedSort (name, value);
		// One sort per value: a nested sort that took the value, or a clear, leaves no flat copy of the old one
		// for another reader to show; a flat set replaces the copy the getter reads and drops the rest of its run.
		if (level is ulong fixedAt) {
			// The flat sorts of the level's run: a set keeps one, in the value's Tag, and a clear none.
			var flats = new HashSet<ulong> ();
			foreach (var holding in RunFrom (fixedAt, name, flatName)) {
				if (holding.HasFlat && (value is null || replaced || holding.Level != fixedAt))
					flats.Add (holding.Level);
			}

			RemoveAtLevels (flats, flatName);
			if (value is not null && !replaced)
				SetAt (fixedAt, flatName, value);
		} else if (value is null || replaced) {
			RemovePairedFlat (name, flatName);
		} else {
			var target = PairedFlatWriteLevel (name);
			RemovePairedFlat (name, flatName, target);
			SetAtValues (target, flatName, [value]);
		}
	}

	[ThreadStatic]
	static long t_levelSteps;

	/// <summary>
	/// The Tags the sort pairing visits, counted per thread. Pairing a sort is linear in the Tags of the
	/// file, so tests bound this count instead of a wall-clock time.
	/// </summary>
	internal static long LevelSteps {
		get => t_levelSteps;
		set => t_levelSteps = value;
	}

	sealed class LevelHolding (ulong level)
	{
		public ulong Level { get; } = level;
		public bool HasFlat { get; set; }
		public List<string> Values { get; } = [];
		public List<string> DefaultValues { get; } = [];
		public bool HasValue => Values.Count > 0;

		// The values a getter reads at this level: the default-language ones when there are any.
		public List<string> Read => DefaultValues.Count > 0 ? DefaultValues : Values;
	}

	/// <summary>
	/// One pass over the Tags: each level the getters climb (from the item up, skipping a Tag that is not
	/// rendered), in order, with its values of <paramref name="name"/> and whether it holds <paramref name="flatName"/>.
	/// </summary>
	List<LevelHolding> LevelsHolding (string name, string flatName)
	{
		var item = ItemLevel;
		var levels = new SortedDictionary<ulong, LevelHolding> ();
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (!block.IsFileLevel || IsDropped (block) || block.Level < item)
				continue;
			if (!levels.TryGetValue (block.Level, out var holding))
				levels[block.Level] = holding = new LevelHolding (block.Level);
			foreach (var tag in block.Tags) {
				if (tag.Name == flatName)
					holding.HasFlat = true;
				if (tag.Name != name)
					continue;
				holding.Values.Add (tag.Text ?? string.Empty);
				if (tag.IsDefault)
					holding.DefaultValues.Add (tag.Text ?? string.Empty);
			}
		}

		return [.. levels.Values];
	}

	/// <summary>
	/// The levels whose flat sort belongs to the <paramref name="name"/> the getter reads, item first: its run.
	/// A flat sort sorts the value in its own Tag (the ffmpeg convention), and a Tag with no value of its own
	/// passes its sort down to the value below it (tagging.html, "Target types": a tag applies to every
	/// narrower target). A broader Tag that repeats the same value is the same value, which the spec allows
	/// ("you can also repeat the value for each track"), so the run goes on through it. The run ends below
	/// the first broader Tag whose value differs: that Tag's sort, and every one above it, is another value's.
	/// </summary>
	List<LevelHolding> PairedLevels (string name, string flatName)
	{
		var paired = new List<LevelHolding> ();
		List<string>? value = null;
		foreach (var holding in LevelsHolding (name, flatName)) {
			if (holding.HasValue) {
				if (value is null)
					value = holding.Read;
				else if (!SameText (value, holding.Read))
					break;
			}

			paired.Add (holding);
		}

		return paired;
	}

	static bool SameText (List<string> a, List<string> b)
	{
		if (a.Count != b.Count)
			return false;
		for (var i = 0; i < a.Count; i++) {
			if (a[i] != b[i])
				return false;
		}

		return true;
	}

	/// <summary>The level of the flat sort that belongs to the <paramref name="name"/> the getter reads.</summary>
	ulong? PairedFlatLevel (string name, string flatName)
	{
		foreach (var holding in PairedLevels (name, flatName)) {
			if (holding.HasFlat)
				return holding.Level;
		}

		return null;
	}

	string? PairedFlat (string name, string flatName) =>
		PairedFlatLevel (name, flatName) is ulong level ? FirstAt (level, flatName) : null;

	/// <summary>
	/// Removes the flat sort at every level <see cref="PairedFlat"/> can reach, and none that belongs to another value.
	/// </summary>
	void RemovePairedFlat (string name, string flatName, ulong? keep = null)
	{
		// A copy at the level about to be written is replaced in place there, so its position stays.
		var levels = new HashSet<ulong> ();
		foreach (var holding in PairedLevels (name, flatName)) {
			if (holding.HasFlat && holding.Level != keep)
				levels.Add (holding.Level);
		}

		RemoveAtLevels (levels, flatName);
	}

	// One pass, so a clear over many levels stays linear in the Tags.
	void RemoveAtLevels (HashSet<ulong> levels, string name)
	{
		if (levels.Count == 0)
			return;
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (block.IsFileLevel && levels.Contains (block.Level))
				block.Remove (name);
		}
	}

	/// <summary>
	/// Where a new flat sort goes: in the Tag of the value it sorts, the ffmpeg pairing. A value above the album
	/// (where ordinary fields are not written), or none, has its sort at the ordinary level, which is still in
	/// its run. The caller removes the run's other copies first, so one copy is left for every reader.
	/// </summary>
	ulong PairedFlatWriteLevel (string name) =>
		ChosenLevel (name) is ulong parent && parent <= AlbumLevel ? parent : OrdinaryWriteLevel ();

	/// <summary>
	/// Removes <paramref name="name"/> at every file level the getters can still climb to.
	/// </summary>
	void RemoveVisible (string name)
	{
		var item = ItemLevel;
		foreach (var block in _blocks) {
			if (block.IsFileLevel && block.Level >= item)
				block.Remove (name);
		}
	}

	/// <summary>
	/// The flat <c>ALBUM_ARTIST</c> names the album artist when any level the item can see has one.
	/// </summary>
	bool UsesAlbumArtistName => ChosenLevel (AlbumArtistName) is not null;

	/// <summary>
	/// When the track has no <c>ARTIST</c> of its own, <see cref="ArtistSort"/> inherits the album's,
	/// and both it and <see cref="AlbumArtistSort"/> read the <c>SORT_WITH</c> of one SimpleTag.
	/// Before a write that would change that shared sort, copy every <c>ARTIST</c> at the inherited
	/// level to the track level, and to the album level when it came from above the album, so each
	/// property keeps its own sort and every parent reads the same names as before.
	/// </summary>
	void SplitSharedArtist (string? value)
	{
		if (UsesAlbumArtistName || ChosenLevel ("ARTIST") is not ulong level)
			return;
		var shared = SortParentAt (level, "ARTIST");
		if (shared is null || !shared.Children.Exists (child => child.Name == "SORT_WITH"))
			return;
		if (ItemLevel < AlbumLevel && level != AlbumLevel)
			return;
		if (value is not null && value == NestedSortAt (level, "ARTIST"))
			return;

		// Each copy repeats the same value, so the runs of ARTIST_SORT and ALBUM_ARTIST_SORT still reach the
		// original's flat sorts: no flat sort is copied, and none is left as a second copy.
		var artists = TagsNamedAt (level, "ARTIST");

		CopyTags (artists, TrackLevel);
		if (level > AlbumLevel)
			CopyTags (artists, AlbumLevel);
	}

	List<SimpleTag> TagsNamedAt (ulong level, string name)
	{
		var found = new List<SimpleTag> ();
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (!block.IsFileLevel || block.Level != level)
				continue;
			foreach (var tag in block.Tags) {
				if (tag.Name == name)
					found.Add (tag);
			}
		}

		return found;
	}

	void CopyTags (List<SimpleTag> tags, ulong level)
	{
		var block = FileBlock (level);
		foreach (var tag in tags)
			block.Tags.Add (CopyTag (tag, tag.Text));
	}

	static string? NullIfEmpty (string? value) => string.IsNullOrEmpty (value) ? null : value;

	static List<string> NonEmpty (string[]? values)
	{
		var kept = new List<string> ();
		if (values is not null) {
			foreach (var value in values) {
				if (!string.IsNullOrEmpty (value))
					kept.Add (value);
			}
		}

		return kept;
	}

	// The non-empty values, in order, are what the getter already returns: writing them back changes nothing.
	static bool SameValues (string[]? values, string[] current)
	{
		var at = 0;
		if (values is not null) {
			foreach (var value in values) {
				if (string.IsNullOrEmpty (value))
					continue;
				if (at >= current.Length || current[at] != value)
					return false;
				at++;
			}
		}

		return at == current.Length;
	}

	static bool HasText (string[]? values)
	{
		if (values is null)
			return false;
		foreach (var value in values) {
			if (!string.IsNullOrEmpty (value))
				return true;
		}

		return false;
	}

	bool ReplaceNestedSort (string name, string? value)
	{
		var chosen = ChosenLevel (name);
		return chosen is not null && ReplaceNestedSortAt (chosen.Value, name, value);
	}

	/// <summary>
	/// Rewrites <c>SORT_WITH</c> on the tag the getter reads. Returns false when that tag has none.
	/// </summary>
	bool ReplaceNestedSortAt (ulong level, string name, string? value)
	{
		var tag = SortParentAt (level, name);
		if (tag is null || !tag.Children.Exists (child => child.Name == "SORT_WITH"))
			return false;
		tag.Children.RemoveAll (child => child.Name == "SORT_WITH");
		if (value is not null)
			tag.Children.Add (new SimpleTag ("SORT_WITH", value));
		return true;
	}

	string? NestedSortAt (ulong level, string name)
	{
		var tag = SortParentAt (level, name);
		if (tag is null)
			return null;
		foreach (var child in tag.Children) {
			if (child.Name == "SORT_WITH" && !string.IsNullOrEmpty (child.Text))
				return child.Text;
		}

		return null;
	}

	/// <summary>
	/// The SimpleTag whose <c>SORT_WITH</c> the sort getters read: the first non-empty
	/// <paramref name="name"/> at <paramref name="level"/>, preferring a default-language one.
	/// </summary>
	SimpleTag? SortParentAt (ulong level, string name)
	{
		var preferDefault = false;
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level != level)
				continue;
			if (block.HasDefault (name))
				preferDefault = true;
		}

		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level != level)
				continue;
			foreach (var tag in block.Tags) {
				if (tag.Name == name && (!preferDefault || tag.IsDefault) && !string.IsNullOrEmpty (tag.Text))
					return tag;
			}
		}

		return null;
	}

	List<string> ValuesAt (ulong level, string name)
	{
		var preferDefault = false;
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level != level)
				continue;
			if (block.HasDefault (name))
				preferDefault = true;
		}

		var values = new List<string> ();
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level != level)
				continue;
			block.Collect (name, preferDefault, values);
		}

		return values;
	}

	// A target broader than the album is not where ordinary fields are written. A Tag that holds only ARTIST
	// and its sort, such as the track copy that keeps ArtistSort apart from AlbumArtistSort or a track artist
	// whose sort was just set, does not move new fields to its level: a sort write does not change where
	// later fields land, and they reach the track by inheritance.
	ulong OrdinaryWriteLevel ()
	{
		ulong? level = null;
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || IsDropped (block) || IsArtistOnly (block))
				continue;
			if (level is null || block.Level < level)
				level = block.Level;
		}

		var found = level ?? ItemLevel;
		return found > AlbumLevel ? AlbumLevel : found;
	}

	static bool IsArtistOnly (TagBlock block) =>
		block.Tags.Count > 0 && block.Tags.TrueForAll (tag => tag.Name is "ARTIST" or "ARTIST_SORT");

	/// <summary>
	/// Writes an item field where its getter reads it. A value inherited from a broader target
	/// (tagging.html, "Target types") is replaced in place, so a set is not hidden behind a copy at the
	/// item level and readers that look only at the broader target see it too. A clear removes the name at
	/// every level the getter climbs to, so no inherited value shows through.
	/// </summary>
	void SetItem (string name, string? value)
	{
		// Writing back the value read changes nothing. A clear always runs, so nothing a broader level
		// holds is left for another reader to show.
		if (IsWriteBackAt (value, All (name), null, name))
			return;
		if (string.IsNullOrEmpty (value)) {
			RemoveVisible (name);
			return;
		}

		SetAtValues (ItemWriteLevel (name), name, [value!]);
	}

	void SetItemValues (string name, string[]? values)
	{
		if (IsListWriteBack (values, All (name), AnyText (name), clearsOneLevel: false))
			return;
		if (!HasText (values)) {
			RemoveVisible (name);
			return;
		}

		SetAtValues (ItemWriteLevel (name), name, values);
	}

	void SetItemReplacingLegacy (string name, string legacy, string? value, string? current) =>
		SetItemWithFallback (name, legacy, value, current, replaceFallback: true);

	/// <summary>
	/// A field read from <paramref name="name"/>, else from <paramref name="fallback"/>. Writing back the value
	/// read changes nothing, in a copy too, as the fallback is its own field; a copy still drops the other
	/// values of <paramref name="name"/> (Tag.IsWriteBack, the Vorbis rule). A clear removes both names, so the
	/// getter reads null. A set writes <paramref name="name"/>; an older copy of the same value under
	/// <paramref name="fallback"/> goes when <paramref name="replaceFallback"/>, so no reader shows the old one.
	/// "" counts as the value read only when no value of either name holds text (#136), so a clear has nothing to remove.
	/// </summary>
	void SetItemWithFallback (string name, string fallback, string? value, string? current, bool replaceFallback)
	{
		if (IsValueRead (value, current) || (value is { Length: 0 } && !AnyText (name) && !AnyText (fallback))) {
			if (IsCopying && !string.IsNullOrEmpty (value) && All (name).Length > 1 && First (name) == value)
				SetAtValues (ItemWriteLevel (name), name, [value!]);
			return;
		}

		if (string.IsNullOrEmpty (value) || replaceFallback)
			RemoveVisible (fallback);
		SetItem (name, value);
	}

	/// <summary>
	/// The write-back rule of <see cref="Tag.IsWriteBack(string?, string?)"/> (#136): handed exactly the value
	/// its getter read, a setter changes nothing. A copy replaces the target's list, so while
	/// <see cref="Tag.CopyTo"/> runs a value is unchanged only when the target already holds exactly that one
	/// value. <paramref name="held"/> is the list the getter reads, whose first value it returns, so the check
	/// cannot read anything the getter does not. "" is the value read only when no value of the name holds text
	/// where the setter clears (<paramref name="level"/>, or every visible level), as #136 has it, so the clear
	/// would remove nothing. A null over a field that reads null is a no-op for a setter that writes one level,
	/// whose clear could only uncover a broader value; otherwise null clears every level the getter climbs.
	/// </summary>
	bool IsWriteBackAt (string? value, string[] held, ulong? level, string name)
	{
		var current = FirstOf (held);
		if (value is null)
			return current is null && level is not null;
		if (value.Length == 0)
			return current is null && !(level is ulong at ? AnyTextAt (at, name) : AnyText (name));
		if (!IsValueRead (value, current))
			return false;
		return !IsCopying || (held.Length == 1 && held[0] == value);
	}

	/// <summary>
	/// A sort handed the value its getter read changes nothing (Tag.IsWriteBack), in a copy too, unless the flat
	/// sort read sits beside other texts at its level, which a copy replaces so the target holds one sort. A
	/// nested sort read is the value's own, and a flat sort elsewhere in its run is another field (as a Vorbis
	/// fallback field is), so a copy of a nested sort leaves the file as it is: a sort the item inherits stays
	/// where it is and its value is not split.
	/// </summary>
	bool IsSortWriteBack (string? value, string name, string flatName, ulong? level)
	{
		var (nested, flatLevel) = SortSource (name, flatName, level);
		var current = nested ?? (flatLevel is ulong read ? FirstAt (read, flatName) : null);
		// A sort never reads "", so "" is never the value read and always clears.
		if (!IsValueRead (value, current))
			return false;
		if (!IsCopying || nested is not null)
			return true;
		foreach (var held in AllAt (flatLevel!.Value, flatName)) {
			if (held != value)
				return false;
		}

		return true;
	}

	/// <summary>
	/// Where a sort is read: the <c>SORT_WITH</c> nested in the value the getter reads, and the level of the flat
	/// sort that belongs to it. A sort of a value the getter reads at one level (<paramref name="level"/>: the track
	/// title, the album title, the album-level <c>ARTIST</c>) belongs to that level's run, the level and each
	/// broader one up to a different value (<see cref="RunFrom"/>), so a value-less broader sort passes down to it
	/// as every other sort does. Any other sort belongs to the run of the value the item reads (<see cref="PairedLevels"/>).
	/// </summary>
	(string? Nested, ulong? FlatLevel) SortSource (string name, string flatName, ulong? level)
	{
		if (level is not ulong at)
			return (NestedSort (name), PairedFlatLevel (name, flatName));
		foreach (var holding in RunFrom (at, name, flatName)) {
			if (holding.HasFlat)
				return (NestedSortAt (at, name), holding.Level);
		}

		return (NestedSortAt (at, name), null);
	}

	string? ReadSort (string name, string flatName, ulong? level)
	{
		var (nested, flatLevel) = SortSource (name, flatName, level);
		return nested ?? (flatLevel is ulong read ? FirstAt (read, flatName) : null);
	}

	string AlbumArtistValueName => UsesAlbumArtistName ? AlbumArtistName : "ARTIST";

	// The album artist is the album-level ARTIST once the file has a track level (AlbumArtists reads level 50
	// then), so its sort belongs to that level's run; otherwise it is the value the item reads.
	ulong? AlbumArtistSortLevel => UsesAlbumArtistName || ItemLevel >= AlbumLevel ? null : AlbumLevel;

	/// <summary>
	/// The list form: the values read, in order, are a no-op, in a copy too, as the target then holds exactly
	/// that list. An empty list is the value read only when no value holds text where the setter clears; null
	/// over an empty list is a no-op only for a one-level setter (<paramref name="clearsOneLevel"/>).
	/// </summary>
	static bool IsListWriteBack (string[]? values, string[] current, bool anyText, bool clearsOneLevel)
	{
		if (values is null)
			return clearsOneLevel && current.Length == 0;
		if (!HasText (values))
			return current.Length == 0 && !anyText;
		return SameValues (values, current);
	}

	// Whether any visible file-level value of the name holds text.
	bool AnyText (string name)
	{
		var item = ItemLevel;
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (!block.IsFileLevel || block.Level < item)
				continue;
			foreach (var tag in block.Tags) {
				if (tag.Name == name && !string.IsNullOrEmpty (tag.Text))
					return true;
			}
		}

		return false;
	}

	bool AnyTextAt (ulong level, string name)
	{
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (!block.IsFileLevel || block.Level != level)
				continue;
			foreach (var tag in block.Tags) {
				if (tag.Name == name && !string.IsNullOrEmpty (tag.Text))
					return true;
			}
		}

		return false;
	}

	// The level the getter reads, unless that is broader than the album, where ordinary fields are not written.
	ulong ItemWriteLevel (string name) =>
		ChosenLevel (name) is ulong level && level <= AlbumLevel ? level : OrdinaryWriteLevel ();

	string? TrackPartNumber ()
	{
		var atTrack = FirstAt (TrackLevel, "PART_NUMBER");
		if (atTrack is not null)
			return atTrack;
		// By the spec an omitted TargetTypeValue is 50, which would make this the disc. ffmpeg writes the
		// track number here and omits TargetTypeValue, so an untargeted PART_NUMBER is read as the track.
		return TextWhere (block => block.IsFileLevel && !block.HasTargetTypeValue, "PART_NUMBER");
	}

	/// <summary>
	/// Writes the track number where the getter reads it: the level 30 or untargeted <c>PART_NUMBER</c>.
	/// </summary>
	void SetTrackNumber (string text)
	{
		var home = FirstBlock (block => block.IsFileLevel && block.Level == TrackLevel && block.HasName ("PART_NUMBER"))
			?? FirstBlock (block => block.IsFileLevel && !block.HasTargetTypeValue && block.HasName ("PART_NUMBER"));
		if (home is null) {
			SetAt (TrackLevel, "PART_NUMBER", text);
			return;
		}

		ReplaceNumber (home, "PART_NUMBER", text);
		RemoveOthers (IsTrackTarget, home, "PART_NUMBER");
	}

	// TOTAL_PARTS is the "total number of parts defined at the first lower level"
	// (https://www.matroska.org/technical/tagging.html, Organization Information), so the album's
	// track count is TOTAL_PARTS at 50 and the set's disc count is TOTAL_PARTS at 60
	// (https://www.matroska.org/technical/tagging-audio-example.html, "Collection of CD sets").
	// A block that omits TargetTypeValue is 50. TRACKTOTAL and TOTALTRACKS are Vorbis names that ffmpeg
	// copies from FLAC; the spec does not name them. TOTAL_PARTS at 30 counts a track's own parts, but
	// TagLib writes the track total there, so it is read last and never written.
	uint? TrackTotal () =>
		SpecTrackTotal () ?? FirstNumberWhere (IsTrackLevel, "TOTAL_PARTS");

	uint? SpecTrackTotal () =>
		ParseLeading (AlbumTotalBlock () is { } album ? Text (album, "TOTAL_PARTS") : null)
		?? ParseEmbeddedTotal (TrackPartNumber ())
		?? ParseLeading (LowestLevelText ("TRACKTOTAL", "TOTALTRACKS"));

	void SetTrackTotal (uint? value)
	{
		// The level 30 fallback goes when it is what the getter shows, and on a clear, which would otherwise expose it.
		if (value is null || SpecTrackTotal () is null)
			RemoveWhere (IsTrackLevel, "TOTAL_PARTS");
		RemoveFileLevel ("TRACKTOTAL");
		RemoveFileLevel ("TOTALTRACKS");
		if (value is null) {
			RemoveWhere (IsAlbumTarget, "TOTAL_PARTS");
			StripEmbeddedTotal (IsTrackTarget, "PART_NUMBER");
			return;
		}

		var home = TrackTotalHome ();
		ReplaceOrAdd (home, "TOTAL_PARTS", value.Value.ToString (CultureInfo.InvariantCulture));
		RemoveOthers (IsAlbumTarget, home, "TOTAL_PARTS");
		StripEmbeddedTotal (IsTrackTarget, "PART_NUMBER");
	}

	// The album the file's disc belongs to first, then any stored 50, then a block that omits TargetTypeValue.
	TagBlock? AlbumTotalBlock () =>
		FirstBlock (block => IsExplicitAlbum (block) && Text (block, "PART_NUMBER") is not null && HasNumber (block, "TOTAL_PARTS"))
		?? FirstBlock (block => IsExplicitAlbum (block) && HasNumber (block, "TOTAL_PARTS"))
		?? FirstBlock (block => IsUntargeted (block) && HasNumber (block, "TOTAL_PARTS"));

	TagBlock TrackTotalHome () =>
		AlbumTotalBlock ()
		?? FirstBlock (block => IsAlbumTarget (block) && block.HasName ("TOTAL_PARTS"))
		?? FirstBlock (block => IsExplicitAlbum (block) && block.HasName ("PART_NUMBER"))
		?? FirstBlock (IsExplicitAlbum)
		?? FirstBlock (IsUntargeted)
		?? FileBlock (AlbumLevel);

	string? DiscPartNumber () => TextWhere (IsExplicitAlbum, "PART_NUMBER") ?? LowestLevelText ("DISCNUMBER", "DISC");

	uint? DiscTotal () =>
		FirstNumberWhere (IsVolume, "TOTAL_PARTS")
		?? ParseEmbeddedTotal (DiscPartNumber ())
		?? ParseLeading (LowestLevelText ("DISCTOTAL", "TOTALDISCS"));

	void SetDiscTotal (uint? value)
	{
		// 60 TOTAL_PARTS counts "the first lower TargetTypeValue that is specified in the file"
		// (tagging.html, Target Types Parts). Without a 40 or 50 tag that is the track, so a known disc
		// number moves to 50 PART_NUMBER first and the 60 total then counts discs.
		if (value is not null && !HasAlbumOrPartTarget () && DiscNumber is { } disc)
			DiscNumber = disc;
		RemoveFileLevel ("DISCTOTAL");
		RemoveFileLevel ("TOTALDISCS");
		if (value is null) {
			RemoveWhere (IsVolume, "TOTAL_PARTS");
		} else {
			var home = FirstBlock (block => IsVolume (block) && HasNumber (block, "TOTAL_PARTS"))
				?? FirstBlock (block => IsVolume (block) && block.HasName ("TOTAL_PARTS"))
				?? FileBlock (VolumeLevel);
			ReplaceOrAdd (home, "TOTAL_PARTS", value.Value.ToString (CultureInfo.InvariantCulture));
			RemoveOthers (IsVolume, home, "TOTAL_PARTS");
		}

		StripEmbeddedTotal (IsExplicitAlbum, "PART_NUMBER");
		StripEmbeddedTotal (block => block.IsFileLevel, "DISC");
		StripEmbeddedTotal (block => block.IsFileLevel, "DISCNUMBER");
	}

	/// <summary>
	/// The explicit album target that stores the disc. An omitted <c>TargetTypeValue</c> is not this target.
	/// </summary>
	TagBlock DiscHome ()
	{
		var withPart = FirstBlock (block => IsExplicitAlbum (block) && block.HasName ("PART_NUMBER"));
		if (withPart is not null)
			return withPart;
		var withTotal = FirstBlock (block => IsExplicitAlbum (block) && block.HasName ("TOTAL_PARTS"));
		if (withTotal is not null)
			return withTotal;
		var album = FirstBlock (IsExplicitAlbum);
		if (album is not null)
			return album;
		return AddFileBlock (new TagBlock (AlbumLevel));
	}

	static bool IsTrackTarget (TagBlock block) =>
		block.IsFileLevel && (block.Level == TrackLevel || !block.HasTargetTypeValue);

	static bool IsExplicitAlbum (TagBlock block) =>
		block.IsFileLevel && block.HasTargetTypeValue && block.Level == AlbumLevel;

	// A missing TargetTypeValue is 50, so this is an explicit or an untargeted album block.
	static bool IsAlbumTarget (TagBlock block) => block.IsFileLevel && block.Level == AlbumLevel;

	static bool IsUntargeted (TagBlock block) => block.IsFileLevel && !block.HasTargetTypeValue;

	static bool IsTrackLevel (TagBlock block) => block.IsFileLevel && block.Level == TrackLevel;

	static bool IsVolume (TagBlock block) => block.IsFileLevel && block.Level == VolumeLevel;

	bool HasAlbumOrPartTarget () => FirstBlock (block => block.IsFileLevel && block.Level is AlbumLevel or PartLevel) is not null;

	static bool HasNumber (TagBlock block, string name) => ParseLeading (Text (block, name)) is not null;

	void RemoveWhere (Func<TagBlock, bool> match, string name)
	{
		foreach (var block in _blocks) {
			if (match (block))
				block.Remove (name);
		}
	}

	void RemoveOthers (Func<TagBlock, bool> match, TagBlock keep, string name)
	{
		foreach (var block in _blocks) {
			if (match (block) && !ReferenceEquals (block, keep))
				block.Remove (name);
		}
	}

	void RemoveFileLevel (string name) => RemoveWhere (block => block.IsFileLevel, name);

	void StripEmbeddedTotal (Func<TagBlock, bool> match, string name)
	{
		foreach (var block in _blocks) {
			if (!match (block))
				continue;
			for (var i = 0; i < block.Tags.Count; i++) {
				var tag = block.Tags[i];
				var text = tag.Text;
				if (tag.Name != name || text is null || text.Length == 0)
					continue;
				var slash = SlashAt (text);
				if (slash <= 0)
					continue;
				block.Tags[i] = CopyTag (tag, text.Substring (0, slash));
			}
		}
	}

	uint? FirstNumberWhere (Func<TagBlock, bool> match, string name)
	{
		foreach (var block in _blocks) {
			if (!match (block))
				continue;
			var parsed = ParseLeading (Text (block, name));
			if (parsed is not null)
				return parsed;
		}

		return null;
	}

	// A name outside the tagging spec has no defined level, so the most specific file-level target wins.
	string? LowestLevelText (string name, string alias)
	{
		ulong? bestLevel = null;
		string? best = null;
		foreach (var block in _blocks) {
			if (!block.IsFileLevel)
				continue;
			var text = Text (block, name) ?? Text (block, alias);
			if (text is null)
				continue;
			if (bestLevel is null || block.Level < bestLevel.Value) {
				bestLevel = block.Level;
				best = text;
			}
		}

		return best;
	}

	void RemoveDiscNames ()
	{
		RemoveFileLevel ("DISC");
		RemoveFileLevel ("DISCNUMBER");
	}

	// The new number keeps the old element's "/total", so a reader of the slash form still sees the total.
	static void ReplaceNumber (TagBlock block, string name, string number)
	{
		var old = Text (block, name);
		var total = ParseEmbeddedTotal (old);
		ReplaceOrAdd (block, name, total is null ? number : number + "/" + total.Value.ToString (CultureInfo.InvariantCulture));
	}

	static void ReplaceOrAdd (TagBlock block, string name, string value)
	{
		for (var i = 0; i < block.Tags.Count; i++) {
			if (block.Tags[i].Name != name)
				continue;
			block.Tags[i] = CopyTag (block.Tags[i], value);
			for (var j = block.Tags.Count - 1; j > i; j--) {
				if (block.Tags[j].Name == name)
					block.Tags.RemoveAt (j);
			}

			return;
		}

		block.Tags.Add (new SimpleTag (name, value));
	}

	string? TextWhere (Func<TagBlock, bool> match, string name)
	{
		foreach (var block in _blocks) {
			if (!match (block))
				continue;
			var text = Text (block, name);
			if (text is not null)
				return text;
		}

		return null;
	}

	TagBlock? FirstBlock (Func<TagBlock, bool> match)
	{
		foreach (var block in _blocks) {
			if (match (block))
				return block;
		}

		return null;
	}

	static string? Text (TagBlock block, string name)
	{
		foreach (var tag in block.Tags) {
			if (tag.Name == name && !string.IsNullOrEmpty (tag.Text))
				return tag.Text;
		}

		return null;
	}

	void SetValue (ulong level, string name, string? value, string[] inherited, string[] owned, bool shadows) =>
		SetValues (level, name, string.IsNullOrEmpty (value) ? null : [value!], inherited, owned, shadows);

	/// <summary>
	/// Writes a value at one level and keeps each flat sort with the value it sorts (tagging.html, "Target
	/// types"; the ffmpeg convention keeps a value and its flat sort in one Tag). A new value in a Tag whose
	/// <paramref name="inherited"/> flat sort a narrower value reads moves that sort to the narrower value. A
	/// value removed from a Tag takes that Tag's <paramref name="owned"/> flat sorts with it. When the getters
	/// climb through this level (<paramref name="shadows"/>), a value equal to the broader one it now shadows is
	/// a repeat of it and copies its SimpleTags, nested <c>SORT_WITH</c> included, so the narrower levels that
	/// read the broader value keep reading the same sort.
	/// </summary>
	void SetValues (ulong level, string name, string[]? values, string[] inherited, string[] owned, bool shadows)
	{
		var had = HasFileLevelName (level, name);
		// The narrower value and the sorts of its run that sit in this Tag or above: the write must not take them.
		var narrower = ChosenLevel (name) is ulong n && n < level ? n : (ulong?)null;
		var kept = new List<(string Flat, ulong At)> ();
		if (narrower is not null) {
			foreach (var flat in inherited) {
				if (PairedFlatLevel (name, flat) is ulong at && at >= level)
					kept.Add ((flat, at));
			}
		}

		// A value that leaves this Tag takes the flat sorts of its run with it: its own Tag's, and a broader
		// value-less Tag's that passed down to it, which would otherwise pass on to another value.
		var run = new List<(string Flat, ulong At)> ();
		if (had && !HasText (values)) {
			foreach (var flat in owned) {
				foreach (var at in OwnSortLevels (level, name, flat))
					run.Add ((flat, at));
			}
		}

		if (!HasText (values))
			RemoveAt (level, name);
		else if (shadows && !had && ShadowedLevel (level, name, values!) is ulong shadowed)
			CopyTags (TagsNamedAt (shadowed, name), level);
		else
			SetAtValues (level, name, values);
		if (had && !HasFileLevelName (level, name)) {
			foreach (var flat in owned) {
				var levels = new HashSet<ulong> ();
				foreach (var (runFlat, at) in run) {
					if (runFlat == flat && !kept.Contains ((flat, at)))
						levels.Add (at);
				}

				RemoveAtLevels (levels, flat);
			}
		}

		// A value placed between the narrower value and its sort, or a repeat of it renamed here, would leave
		// the sort with another value; it moves down to the value it sorts.
		foreach (var (flat, at) in kept) {
			if (PairedFlatLevel (name, flat) != at)
				MoveTags (at, narrower!.Value, flat);
		}
	}

	/// <summary>
	/// The levels whose flat sorts belong to the value at <paramref name="level"/> alone: its own Tag, and each
	/// value-less broader Tag up to the next one that holds a value. A broader Tag that repeats the same value
	/// keeps its own sort, which the narrower levels read once this value goes (tagging.html, "Target types": a
	/// tag applies to every narrower target).
	/// </summary>
	List<ulong> OwnSortLevels (ulong level, string name, string flatName)
	{
		var own = new List<ulong> ();
		foreach (var holding in LevelsHolding (name, flatName)) {
			if (holding.Level < level)
				continue;
			if (holding.Level > level && holding.HasValue)
				break;
			own.Add (holding.Level);
		}

		return own;
	}

	// The nearest broader level with a value of the name, when that value is the same list of texts as values.
	ulong? ShadowedLevel (ulong level, string name, string[] values)
	{
		var written = NonEmpty (values);
		foreach (var holding in LevelsHolding (name, name)) {
			if (holding.Level <= level || !holding.HasValue)
				continue;
			return SameText (NonEmpty ([.. holding.Read]), written) ? holding.Level : null;
		}

		return null;
	}

	/// <summary>The levels of the run that starts at <paramref name="level"/>: it, and each broader level up to a different value.</summary>
	List<LevelHolding> RunFrom (ulong level, string name, string flatName)
	{
		var run = new List<LevelHolding> ();
		List<string>? value = null;
		foreach (var holding in LevelsHolding (name, flatName)) {
			if (holding.Level < level)
				continue;
			if (holding.Level == level)
				value = holding.Read;
			else if (holding.HasValue && (value is null || !SameText (value, holding.Read)))
				break;
			run.Add (holding);
		}

		return run;
	}

	void MoveTags (ulong from, ulong to, string name)
	{
		// Only the sort the narrower value read moves, so the narrower level holds none of its own.
		var moving = TagsNamedAt (from, name);
		RemoveAt (from, name);
		var home = FileBlock (to);
		foreach (var tag in moving)
			home.Tags.Add (tag);
	}

	void SetAt (ulong level, string name, string? value)
	{
		if (string.IsNullOrEmpty (value)) {
			RemoveAt (level, name);
			return;
		}

		SetAtValues (level, name, new[] { value! });
	}

	void SetAtValues (ulong level, string name, string[]? values)
	{
		var matches = new List<SimpleTag> ();
		foreach (var block in _blocks) {
			if (!block.IsFileLevel || block.Level != level)
				continue;
			foreach (var tag in block.Tags) {
				if (tag.Name == name)
					matches.Add (tag);
			}
		}

		var preferDefault = false;
		foreach (var tag in matches) {
			if (tag.IsDefault)
				preferDefault = true;
		}

		var primary = new List<SimpleTag> ();
		foreach (var tag in matches) {
			if (!preferDefault || tag.IsDefault)
				primary.Add (tag);
		}

		var incoming = new List<string> ();
		if (values is not null) {
			foreach (var value in values) {
				if (!string.IsNullOrEmpty (value))
					incoming.Add (value);
			}
		}

		if (incoming.Count == 0) {
			RemoveTags (primary);
			return;
		}

		if (primary.Count == 0) {
			var block = FileBlock (level);
			foreach (var value in incoming)
				block.Tags.Add (new SimpleTag (name, value));
			return;
		}

		var home = BlockContaining (primary[0]);
		ReplaceTag (primary[0], CopyTag (primary[0], incoming[0]));
		if (primary.Count > 1)
			RemoveTags (primary.GetRange (1, primary.Count - 1));
		for (var i = 1; i < incoming.Count; i++) {
			var source = i < primary.Count ? primary[i] : primary[0];
			home.Tags.Add (CopyTag (source, incoming[i]));
		}
	}

	static SimpleTag CopyTag (SimpleTag previous, string? text)
	{
		var tag = new SimpleTag (previous.Name, text, previous.IsDefault) {
			Language = previous.Language,
			LanguageBcp47 = previous.LanguageBcp47,
			Binary = previous.Binary
		};
		tag.Children.AddRange (previous.Children);
		return tag;
	}

	void ReplaceTag (SimpleTag previous, SimpleTag replacement)
	{
		foreach (var block in _blocks) {
			var index = block.Tags.IndexOf (previous);
			if (index < 0)
				continue;
			block.Tags[index] = replacement;
			return;
		}
	}

	TagBlock BlockContaining (SimpleTag tag)
	{
		foreach (var block in _blocks) {
			if (block.Tags.Contains (tag))
				return block;
		}

		return FileBlock (ItemLevel);
	}

	void RemoveTags (List<SimpleTag> tags)
	{
		foreach (var block in _blocks) {
			block.Tags.RemoveAll (tag => tags.Contains (tag));
		}
	}

	void RemoveAt (ulong level, string name)
	{
		foreach (var block in _blocks) {
			t_levelSteps++;
			if (block.IsFileLevel && block.Level == level)
				block.Remove (name);
		}
	}

	// ffmpeg's untargeted tag is the file's level 50 tag. A disc-only 50 tag added before it is not the home of new fields.
	TagBlock FileBlock (ulong level) =>
		FirstBlock (block => block.IsFileLevel && block.Level == level && !block.HasTargetTypeValue)
		?? FirstBlock (block => block.IsFileLevel && block.Level == level)
		?? AddFileBlock (new TagBlock (level));

	// A new target goes before the file's narrower targets, as in the spec's examples (70, 60, 50, 30).
	// Readers that keep the last PART_NUMBER or TOTAL_PARTS they meet then still see the narrowest one.
	// ffmpeg's untargeted tag holds the track, so a new album or broader target goes before it too.
	TagBlock AddFileBlock (TagBlock created)
	{
		for (var i = 0; i < _blocks.Count; i++) {
			var block = _blocks[i];
			if (block.IsFileLevel && (block.Level < created.Level || (!block.HasTargetTypeValue && created.Level >= AlbumLevel))) {
				_blocks.Insert (i, created);
				return created;
			}
		}

		_blocks.Add (created);
		return created;
	}

	static uint? ParseNumber (string? text)
	{
		if (text is null)
			return null;
		return uint.TryParse (text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
			? value
			: null;
	}

	// The number before '/' is the part. The number after it is the total, not another part.
	static uint? ParseLeading (string? text)
	{
		if (text is null)
			return null;
		var slash = SlashAt (text);
		var head = slash > 0 ? text.Substring (0, slash) : text;
		return ParseNumber (head);
	}

	static uint? ParseEmbeddedTotal (string? text)
	{
		if (text is null)
			return null;
		var slash = SlashAt (text);
		if (slash <= 0 || slash >= text.Length - 1)
			return null;
		return ParseNumber (text.Substring (slash + 1));
	}

	static int SlashAt (string text)
	{
#if NETSTANDARD2_0
		return text.IndexOf ('/');
#else
		return text.IndexOf ('/', StringComparison.Ordinal);
#endif
	}

	static uint? ParseWholeOrDecimal (string? text)
	{
		var whole = ParseNumber (text);
		if (whole is not null || text is null)
			return whole;
		return decimal.TryParse (text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
			&& number >= 0
			&& number <= uint.MaxValue
			? (uint)number
			: null;
	}
}

/// <summary>One <c>Tag</c> element and the target it describes.</summary>
internal sealed class TagBlock
{
	public TagBlock (ulong level, bool hasTargetTypeValue = true)
	{
		Level = level;
		HasTargetTypeValue = hasTargetTypeValue;
	}

	public ulong Level { get; set; }

	public bool HasTargetTypeValue { get; set; }

	public bool HasChapter { get; set; }

	public bool HasAttachment { get; set; }

	public bool HasTrack { get; set; }

	public bool HasEdition { get; set; }

	// The block leaves the file only when every UID of a kind is nonzero.
	// A zero means every part of that kind and is not cancelled by another UID.
	public bool IsFileLevel => !HasChapter && !HasAttachment && !HasTrack && !HasEdition;

	public List<SimpleTag> Tags { get; } = new ();

	/// <summary>The Tag had no SimpleTag when it was read.</summary>
	public bool WasEmpty { get; set; }

	readonly List<byte[]> _targetChildren = [];

	byte[]? _original;

	internal void KeepOriginal (byte[] element) => _original = element;

	internal void KeepTarget (byte[] element) => _targetChildren.Add (element);

	/// <summary>Gets the nonzero <c>TagAttachmentUID</c> values in the targets.</summary>
	public List<ulong> AttachmentUids { get; } = [];

	public bool TargetsAttachment (HashSet<ulong> uids)
	{
		foreach (var uid in AttachmentUids) {
			if (uids.Contains (uid))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Renders the tag with the <c>TagAttachmentUID</c> elements in <paramref name="uids"/> removed.
	/// </summary>
	/// <returns>The element, or null when no target UID is left.</returns>
	public BinaryData? RenderWithout (HashSet<ulong> uids)
	{
		var children = new List<byte[]> ();
		var targetsLeft = 0;
		foreach (var child in _targetChildren) {
			if (!EbmlReader.TryRead (child, 0, child.Length, out var element) || element.UnknownSize) {
				children.Add (child);
				continue;
			}

			var isUid = element.Id is MatroskaIds.TagAttachmentUid or MatroskaIds.TagTrackUid
				or MatroskaIds.TagChapterUid or MatroskaIds.TagEditionUid;
			if (element.Id == MatroskaIds.TagAttachmentUid
				&& uids.Contains (EbmlReader.ReadUInt (child.AsSpan (element.DataOffset, (int)element.DataLength))))
				continue;
			if (isUid)
				targetsLeft++;
			children.Add (child);
		}

		if (targetsLeft == 0)
			return null;

		return Render (children);
	}

	public bool HasName (string name)
	{
		foreach (var tag in Tags) {
			if (tag.Name == name)
				return true;
		}

		return false;
	}

	public bool HasDefault (string name)
	{
		foreach (var tag in Tags) {
			if (tag.Name == name && tag.IsDefault)
				return true;
		}

		return false;
	}

	public void Collect (string name, bool preferDefault, List<string> values)
	{
		foreach (var tag in Tags) {
			if (tag.Name != name)
				continue;
			if (preferDefault && !tag.IsDefault)
				continue;
			values.Add (tag.Text ?? string.Empty);
		}
	}

	public void Remove (string name) => Tags.RemoveAll (tag => tag.Name == name);

	public BinaryData Render () =>
		_original is not null ? new BinaryData (_original) : Render (_targetChildren);

	BinaryData Render (List<byte[]> targetChildren)
	{
		using var body = new BinaryDataBuilder ();
		if (HasTargetTypeValue)
			body.Add (EbmlWriter.Unsigned (MatroskaIds.TargetTypeValue, Level));
		foreach (var child in targetChildren)
			body.Add (child);
		using var tag = new BinaryDataBuilder ();
		var targets = body.ToBinaryData ();
		// Targets is mandatory. A block with no target type and no UIDs still writes an empty master.
		tag.Add (EbmlWriter.Master (MatroskaIds.Targets, targets.Span));
		foreach (var simple in Tags)
			tag.Add (simple.Render ());
		return EbmlWriter.Master (MatroskaIds.Tag, tag.ToBinaryData ().Span);
	}
}

/// <summary>One <c>SimpleTag</c>. Nested tags are attributes of this one.</summary>
internal sealed class SimpleTag
{
	public SimpleTag (string name, string? text, bool isDefault = true)
	{
		Name = name;
		Text = text;
		IsDefault = isDefault;
	}

	public string Name { get; }

	public string? Text { get; }

	public bool IsDefault { get; }

	public string? Language { get; set; }

	public string? LanguageBcp47 { get; set; }

	public byte[]? Binary { get; set; }

	public List<SimpleTag> Children { get; } = new ();

	/// <summary>
	/// Nested SimpleTags below <see cref="MatroskaFile.MaxSimpleTagDepth"/>, as their stored bytes.
	/// They are written back after <see cref="Children"/>, so the nesting is kept without being parsed.
	/// </summary>
	public List<byte[]> KeptChildren { get; } = new ();

	public BinaryData Render ()
	{
		using var body = new BinaryDataBuilder ();
		body.Add (EbmlWriter.String (MatroskaIds.TagName, Name));
		if (Language is not null)
			body.Add (EbmlWriter.String (MatroskaIds.TagLanguage, Language));
		if (LanguageBcp47 is not null)
			body.Add (EbmlWriter.String (MatroskaIds.TagLanguageBcp47, LanguageBcp47));
		if (!IsDefault)
			body.Add (EbmlWriter.Unsigned (MatroskaIds.TagDefault, 0));
		if (Text is not null)
			body.Add (EbmlWriter.String (MatroskaIds.TagString, Text));
		if (Binary is not null)
			body.Add (EbmlWriter.Master (MatroskaIds.TagBinary, Binary));
		foreach (var child in Children)
			body.Add (child.Render ());
		foreach (var child in KeptChildren)
			body.Add (child);
		return EbmlWriter.Master (MatroskaIds.SimpleTag, body.ToBinaryData ().Span);
	}
}

/// <summary>One image attachment.</summary>
sealed class MatroskaPicture : IPicture
{
	public MatroskaPicture (string mimeType, PictureType pictureType, string description, byte[] data)
	{
		MimeType = mimeType;
		PictureType = pictureType;
		Description = description;
		PictureData = new BinaryData (data);
	}

	public string MimeType { get; }

	public PictureType PictureType { get; }

	public string Description { get; }

	public BinaryData PictureData { get; }
}
