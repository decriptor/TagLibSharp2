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
/// </remarks>
public sealed class MatroskaTag : Tag
{
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
			// An empty TITLE is a real value and blocks the segment title.
			// Any other tag does not.
			var values = Values ("TITLE");
			if (values.Count > 0)
				return First ("TITLE");
			return _segmentTitle;
		}
		set => SetItem ("TITLE", value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => First ("ARTIST");
		set => SetItem ("ARTIST", value);
	}

	/// <inheritdoc/>
	public override string[] Performers {
		get {
			var artists = All ("ARTIST");
			return artists.Length > 0 ? artists : All ("ACTOR");
		}
		set => SetItemValues ("ARTIST", value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => ItemLevel >= AlbumLevel ? null : FirstAt (AlbumLevel, "TITLE");
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
		get => ParseNumber (FirstAt (TrackLevel, "PART_NUMBER"));
		set => SetAt (TrackLevel, "PART_NUMBER", value?.ToString (CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get => ParseNumber (FirstAt (AlbumLevel, "TOTAL_PARTS"));
		set => SetAt (AlbumLevel, "TOTAL_PARTS", value?.ToString (CultureInfo.InvariantCulture));
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
		get => ParseNumber (First ("BPM"));
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
		get => NestedSort ("TITLE") ?? First ("TITLE_SORT");
		set => SetItem ("TITLE_SORT", value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => NestedSort ("ARTIST") ?? First ("ARTIST_SORT");
		set => SetItem ("ARTIST_SORT", value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => (ItemLevel < AlbumLevel ? NestedSortAt (AlbumLevel, "TITLE") : null) ?? First ("ALBUM_SORT");
		set => SetItem ("ALBUM_SORT", value);
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

	void SetItem (string name, string? value) => SetAt (ItemLevel, name, value);

	void SetItemValues (string name, string[]? values) => SetAtValues (ItemLevel, name, values);

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
		RemoveAt (level, name);
		if (values is null || values.Length == 0)
			return;

		var block = FileBlock (level);
		foreach (var value in values) {
			if (string.IsNullOrEmpty (value))
				continue;
			block.Tags.Add (new SimpleTag (name, value));
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
}

/// <summary>One <c>Tag</c> element and the target it describes.</summary>
internal sealed class TagBlock
{
	public TagBlock (ulong level) => Level = level;

	public ulong Level { get; set; }

	public bool HasChapter { get; set; }

	public bool HasAttachment { get; set; }

	public bool HasTrack { get; set; }

	public bool HasEdition { get; set; }

	// A track, edition, chapter, or attachment target describes that part, not the file.
	// The element is enough, including a UID of 0.
	public bool IsFileLevel => !HasChapter && !HasAttachment && !HasTrack && !HasEdition;

	public List<SimpleTag> Tags { get; } = new ();

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
		using var body = new BinaryDataBuilder ();
		body.Add (EbmlWriter.Unsigned (MatroskaIds.TargetTypeValue, Level));
		var targets = EbmlWriter.Master (MatroskaIds.Targets, body.ToBinaryData ().Span);
		using var tag = new BinaryDataBuilder ();
		tag.Add (targets);
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

	public List<SimpleTag> Children { get; } = new ();

	public BinaryData Render ()
	{
		using var body = new BinaryDataBuilder ();
		body.Add (EbmlWriter.String (MatroskaIds.TagName, Name));
		if (!IsDefault)
			body.Add (EbmlWriter.Unsigned (MatroskaIds.TagDefault, 0));
		if (Text is not null)
			body.Add (EbmlWriter.String (MatroskaIds.TagString, Text));
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
