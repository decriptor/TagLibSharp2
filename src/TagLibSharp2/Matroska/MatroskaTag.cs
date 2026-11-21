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
/// https://www.matroska.org/technical/tagging-audio-example.html. TagLib writes these totals at 30 and 50.
/// </para>
/// </remarks>
public sealed class MatroskaTag : Tag
{
	const ulong CollectionLevel = 70;
	const ulong VolumeLevel = 60;
	const ulong AlbumLevel = 50;
	const ulong TrackLevel = 30;

	readonly List<TagBlock> _blocks = new ();
	string? _segmentTitle;

	internal MatroskaTag ()
	{
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Matroska;

	/// <inheritdoc/>
	public override string? Title {
		get {
			// TITLE at 30 is the track. An empty one blocks every broader title.
			if (HasFileLevelName (TrackLevel, "TITLE"))
				return FirstAt (TrackLevel, "TITLE");
			// The segment title is the file title. Otherwise the most specific
			// file-level TITLE below a collection still displays, so a part,
			// edition, or lone album title is not a blank name.
			if (!string.IsNullOrEmpty (_segmentTitle))
				return _segmentTitle;
			var level = MostSpecificFileTitleBelowCollection ();
			return level is ulong found ? FirstAt (found, "TITLE") : null;
		}
		set => SetAt (TrackLevel, "TITLE", value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => First ("ARTIST");
		set => SetAt (TrackLevel, "ARTIST", value);
	}

	/// <inheritdoc/>
	public override string[] Performers {
		get {
			var artists = All ("ARTIST");
			return artists.Length > 0 ? artists : All ("ACTOR");
		}
		set => SetAtValues (TrackLevel, "ARTIST", value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => FirstAt (AlbumLevel, "TITLE");
		set => SetAt (AlbumLevel, "TITLE", value);
	}

	/// <inheritdoc/>
	public override string[] AlbumArtists {
		get => ItemLevel >= AlbumLevel ? All ("ARTIST") : AllAt (AlbumLevel, "ARTIST");
		set => SetAtValues (AlbumLevel, "ARTIST", value);
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
		set => SetItemValues ("COMPOSER", value);
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
		set => SetItem ("BPM", value?.ToString (CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => First ("CONDUCTOR");
		set => SetItem ("CONDUCTOR", value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => First ("PUBLISHER") ?? First ("LABEL");
		set => SetItem ("PUBLISHER", value);
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
		get => NestedSortAt (TrackLevel, "TITLE") ?? FirstAt (TrackLevel, "TITLE_SORT");
		set => SetSort ("TITLE", "TITLE_SORT", value, TrackLevel);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => NestedSort ("ARTIST") ?? First ("ARTIST_SORT");
		set => SetSort ("ARTIST", "ARTIST_SORT", value, null);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => NestedSortAt (AlbumLevel, "TITLE") ?? FirstAt (AlbumLevel, "ALBUM_SORT");
		set => SetSort ("TITLE", "ALBUM_SORT", value, AlbumLevel);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => First ("REPLAYGAIN_TRACK_GAIN") ?? First ("REPLAYGAIN_GAIN");
		set => SetItem ("REPLAYGAIN_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => First ("REPLAYGAIN_TRACK_PEAK") ?? First ("REPLAYGAIN_PEAK");
		set => SetItem ("REPLAYGAIN_TRACK_PEAK", value);
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

	/// <inheritdoc/>
	public override IPicture[] Pictures {
		get => _pictures.ToArray ();
		set {
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
	}

	internal void AddPicture (IPicture picture) => _pictures.Add (picture);

	/// <summary>
	/// Writes the current tags as one <c>Tags</c> element.
	/// </summary>
	/// <returns>The EBML element, or empty when there are no tags.</returns>
	public override BinaryData Render ()
	{
		if (_blocks.Count == 0)
			return BinaryData.Empty;

		using var tags = new BinaryDataBuilder ();
		foreach (var block in _blocks)
			tags.Add (block.Render ());
		return EbmlWriter.Master (MatroskaIds.Tags, tags.ToBinaryData ().Span);
	}

	internal void SetSegmentTitle (string? title) => _segmentTitle = title;

	internal void AddBlock (TagBlock block) => _blocks.Add (block);

	internal ulong ItemLevel {
		get {
			ulong? level = null;
			foreach (var block in _blocks) {
				if (!block.IsFileLevel)
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
			if (block.IsFileLevel && block.Level == level && block.HasName (name))
				return true;
		}

		return false;
	}

	string? First (string name)
	{
		var values = Values (name);
		for (var i = 0; i < values.Count; i++) {
			if (values[i].Length > 0)
				return values[i];
		}

		return null;
	}

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

	string? FirstAt (ulong level, string name)
	{
		var values = ValuesAt (level, name);
		for (var i = 0; i < values.Count; i++) {
			if (values[i].Length > 0)
				return values[i];
		}

		return null;
	}

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
		var replaced = level is ulong fixedLevel
			? ReplaceNestedSortAt (fixedLevel, name, value)
			: ReplaceNestedSort (name, value);
		if (value is null) {
			if (level is ulong clearLevel)
				RemoveAt (clearLevel, flatName);
			else
				RemoveFlatSort (flatName);
		} else if (!replaced) {
			if (level is ulong writeLevel)
				SetAt (writeLevel, flatName, value);
			else
				SetItem (flatName, value);
		}
	}

	/// <summary>
	/// A null sort removes the flat name at every file level the getter can still climb to.
	/// </summary>
	void RemoveFlatSort (string flatName)
	{
		var item = ItemLevel;
		foreach (var block in _blocks) {
			if (block.IsFileLevel && block.Level >= item)
				block.Remove (flatName);
		}
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
				if (tag.Name != name || (preferDefault && !tag.IsDefault) || string.IsNullOrEmpty (tag.Text))
					continue;
				if (!tag.Children.Exists (child => child.Name == "SORT_WITH"))
					return false;
				tag.Children.RemoveAll (child => child.Name == "SORT_WITH");
				if (value is not null)
					tag.Children.Add (new SimpleTag ("SORT_WITH", value));
				return true;
			}
		}

		return false;
	}

	string? NestedSortAt (ulong level, string name)
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
				if (tag.Name != name || (preferDefault && !tag.IsDefault) || string.IsNullOrEmpty (tag.Text))
					continue;
				foreach (var child in tag.Children) {
					if (child.Name == "SORT_WITH" && !string.IsNullOrEmpty (child.Text))
						return child.Text;
				}

				return null;
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

	// A target broader than the album is not where ordinary fields are written.
	ulong OrdinaryWriteLevel ()
	{
		var level = ItemLevel;
		return level > AlbumLevel ? AlbumLevel : level;
	}

	void SetItem (string name, string? value) => SetAt (OrdinaryWriteLevel (), name, value);

	void SetItemValues (string name, string[]? values) => SetAtValues (OrdinaryWriteLevel (), name, values);

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
		var created = new TagBlock (AlbumLevel);
		_blocks.Add (created);
		return created;
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

	static SimpleTag CopyTag (SimpleTag previous, string text)
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
			if (block.IsFileLevel && block.Level == level)
				block.Remove (name);
		}
	}

	TagBlock FileBlock (ulong level)
	{
		foreach (var block in _blocks) {
			if (block.IsFileLevel && block.Level == level)
				return block;
		}

		var created = new TagBlock (level);
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

	readonly List<byte[]> _targetChildren = [];

	byte[]? _original;

	internal void KeepOriginal (byte[] element) => _original = element;

	internal void KeepTarget (byte[] element) => _targetChildren.Add (element);

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

	public BinaryData Render ()
	{
		if (_original is not null)
			return new BinaryData (_original);

		using var body = new BinaryDataBuilder ();
		if (HasTargetTypeValue)
			body.Add (EbmlWriter.Unsigned (MatroskaIds.TargetTypeValue, Level));
		foreach (var child in _targetChildren)
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
