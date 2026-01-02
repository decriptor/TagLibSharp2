// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents an ASF/WMA tag with metadata from Content Description and Extended Content Description objects.
/// </summary>
public sealed class AsfTag : Tag
{
	AsfContentDescription _contentDescription;
	readonly List<AsfDescriptor> _descriptors;

	/// <summary>
	/// Creates a new empty ASF tag.
	/// </summary>
	public AsfTag ()
	{
		_contentDescription = new AsfContentDescription ("", "", "", "", "");
		_descriptors = [];
	}

	/// <summary>
	/// Creates an ASF tag from parsed Content Description and Extended Content Description objects.
	/// </summary>
	public AsfTag (AsfContentDescription? contentDescription, AsfExtendedContentDescription? extendedContent)
	{
		_contentDescription = contentDescription ?? new AsfContentDescription ("", "", "", "", "");
		_descriptors = extendedContent?.Descriptors.ToList () ?? [];
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Asf;

	// ═══════════════════════════════════════════════════════════════
	// Content Description Mappings (5 fixed fields)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Title {
		get => _contentDescription.Title;
		set => _contentDescription = new AsfContentDescription (
			value ?? "", _contentDescription.Author, _contentDescription.Copyright,
			_contentDescription.Description, _contentDescription.Rating);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _contentDescription.Author;
		set => _contentDescription = new AsfContentDescription (
			_contentDescription.Title, value ?? "", _contentDescription.Copyright,
			_contentDescription.Description, _contentDescription.Rating);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => _contentDescription.Copyright;
		set => _contentDescription = new AsfContentDescription (
			_contentDescription.Title, _contentDescription.Author, value ?? "",
			_contentDescription.Description, _contentDescription.Rating);
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => _contentDescription.Description;
		set => _contentDescription = new AsfContentDescription (
			_contentDescription.Title, _contentDescription.Author, _contentDescription.Copyright,
			value ?? "", _contentDescription.Rating);
	}

	/// <summary>
	/// Gets or sets the rating field from Content Description.
	/// </summary>
	public string Rating {
		get => _contentDescription.Rating;
		set => _contentDescription = new AsfContentDescription (
			_contentDescription.Title, _contentDescription.Author, _contentDescription.Copyright,
			_contentDescription.Description, value ?? "");
	}

	// ═══════════════════════════════════════════════════════════════
	// Extended Content Description Mappings (WM/* attributes)
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override string? Album {
		get => GetString ("WM/AlbumTitle") ?? "";
		set => SetString ("WM/AlbumTitle", value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => GetString ("WM/Year");
		set => SetString ("WM/Year", value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetString ("WM/Genre") ?? "";
		set => SetString ("WM/Genre", value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get {
			var desc = GetDescriptor ("WM/TrackNumber");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			// Try parsing string value
			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var track))
				return track;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/TrackNumber", value.Value);
			else
				RemoveDescriptor ("WM/TrackNumber");
		}
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetString ("WM/AlbumArtist");
		set => SetString ("WM/AlbumArtist", value);
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => GetString ("WM/Composer");
		set => SetString ("WM/Composer", value);
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetString ("WM/Conductor");
		set => SetString ("WM/Conductor", value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => ParsePartOfSet ().disc;
		set {
			var (_, count) = ParsePartOfSet ();
			SetPartOfSet (value, count);
		}
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => ParsePartOfSet ().count;
		set {
			var (disc, _) = ParsePartOfSet ();
			SetPartOfSet (disc, value);
		}
	}

	/// <summary>
	/// Gets the disc count from WM/PartOfSet.
	/// </summary>
	public uint? DiscCount => TotalDiscs;

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var desc = GetDescriptor ("WM/BeatsPerMinute");
			if (desc is null) return null;

			if (desc.Type == AsfAttributeType.Dword)
				return desc.DwordValue;

			if (desc.StringValue is not null &&
				uint.TryParse (desc.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bpm))
				return bpm;

			return null;
		}
		set {
			if (value.HasValue)
				SetDword ("WM/BeatsPerMinute", value.Value);
			else
				RemoveDescriptor ("WM/BeatsPerMinute");
		}
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetString ("WM/Publisher");
		set => SetString ("WM/Publisher", value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetString ("WM/Lyrics");
		set => SetString ("WM/Lyrics", value);
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetString ("WM/ISRC");
		set => SetString ("WM/ISRC", value);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get {
			var desc = GetDescriptor ("WM/IsCompilation");
			return desc?.BoolValue == true;
		}
		set => SetBool ("WM/IsCompilation", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetString ("MusicBrainz/Track Id");
		set => SetString ("MusicBrainz/Track Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetString ("MusicBrainz/Album Id");
		set => SetString ("MusicBrainz/Album Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetString ("MusicBrainz/Artist Id");
		set => SetString ("MusicBrainz/Artist Id", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetString ("MusicBrainz/Album Artist Id");
		set => SetString ("MusicBrainz/Album Artist Id", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetString ("REPLAYGAIN_TRACK_GAIN");
		set => SetString ("REPLAYGAIN_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetString ("REPLAYGAIN_TRACK_PEAK");
		set => SetString ("REPLAYGAIN_TRACK_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetString ("REPLAYGAIN_ALBUM_GAIN");
		set => SetString ("REPLAYGAIN_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetString ("REPLAYGAIN_ALBUM_PEAK");
		set => SetString ("REPLAYGAIN_ALBUM_PEAK", value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Tag Interface Implementation
	// ═══════════════════════════════════════════════════════════════

	/// <inheritdoc/>
	public override bool IsEmpty =>
		string.IsNullOrEmpty (Title) &&
		string.IsNullOrEmpty (Artist) &&
		string.IsNullOrEmpty (Album) &&
		string.IsNullOrEmpty (Year) &&
		string.IsNullOrEmpty (Comment) &&
		string.IsNullOrEmpty (Genre) &&
		Track is null &&
		_descriptors.Count == 0;

	/// <inheritdoc/>
	public override void Clear ()
	{
		_contentDescription = new AsfContentDescription ("", "", "", "", "");
		_descriptors.Clear ();
	}

	/// <inheritdoc/>
	public override BinaryData Render ()
	{
		// This renders just the extended content description
		// The Content Description is rendered separately when needed
		var extended = new AsfExtendedContentDescription (_descriptors);
		return extended.Render ();
	}

	/// <summary>
	/// Renders the Content Description object.
	/// </summary>
	public BinaryData RenderContentDescription () => _contentDescription.Render ();

	/// <summary>
	/// Gets the Content Description object.
	/// </summary>
	public AsfContentDescription ContentDescription => _contentDescription;

	/// <summary>
	/// Gets the Extended Content Description object.
	/// </summary>
	public AsfExtendedContentDescription ExtendedContentDescription => new (_descriptors);

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	AsfDescriptor? GetDescriptor (string name)
	{
		for (int i = 0; i < _descriptors.Count; i++) {
			if (string.Equals (_descriptors[i].Name, name, StringComparison.OrdinalIgnoreCase))
				return _descriptors[i];
		}
		return null;
	}

	string? GetString (string name) => GetDescriptor (name)?.StringValue;

	void SetString (string name, string? value)
	{
		RemoveDescriptor (name);
		if (!string.IsNullOrEmpty (value))
			_descriptors.Add (AsfDescriptor.CreateString (name, value!));
	}

	void SetDword (string name, uint value)
	{
		RemoveDescriptor (name);
		_descriptors.Add (AsfDescriptor.CreateDword (name, value));
	}

	void SetBool (string name, bool value)
	{
		RemoveDescriptor (name);
		_descriptors.Add (AsfDescriptor.CreateBool (name, value));
	}

	void RemoveDescriptor (string name)
	{
		for (int i = _descriptors.Count - 1; i >= 0; i--) {
			if (string.Equals (_descriptors[i].Name, name, StringComparison.OrdinalIgnoreCase))
				_descriptors.RemoveAt (i);
		}
	}

	(uint? disc, uint? count) ParsePartOfSet ()
	{
		var value = GetString ("WM/PartOfSet");
		if (string.IsNullOrEmpty (value))
			return (null, null);

		var parts = value!.Split ('/');
		uint? disc = null;
		uint? count = null;

		if (parts.Length >= 1 && uint.TryParse (parts[0].Trim (), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
			disc = d;
		if (parts.Length >= 2 && uint.TryParse (parts[1].Trim (), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
			count = c;

		return (disc, count);
	}

	void SetPartOfSet (uint? disc, uint? count)
	{
		if (!disc.HasValue && !count.HasValue) {
			RemoveDescriptor ("WM/PartOfSet");
			return;
		}

		string value;
		if (count.HasValue)
			value = $"{disc?.ToString (CultureInfo.InvariantCulture) ?? ""}/{count.Value}";
		else
			value = disc?.ToString (CultureInfo.InvariantCulture) ?? "";

		SetString ("WM/PartOfSet", value);
	}
}
