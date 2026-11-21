// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents an MP4/M4A audio file with its metadata and properties.
/// </summary>
/// <remarks>
/// <para>
/// MP4 files follow the ISO base media file format (ISO 14496-12).
/// The file is composed of nested boxes (atoms) with a hierarchical structure.
/// </para>
/// <para>
/// Key boxes:
/// </para>
/// <list type="bullet">
/// <item>ftyp - File type identification</item>
/// <item>moov - Movie metadata container</item>
/// <item>moov.mvhd - Movie header (duration, timescale)</item>
/// <item>moov.trak - Track container</item>
/// <item>moov.trak.mdia - Media information</item>
/// <item>moov.udta.meta.ilst - iTunes-style metadata</item>
/// <item>mdat - Media data (audio frames)</item>
/// </list>
/// <para>
/// Reference: ISO 14496-12 (MP4 base format), ISO 14496-14 (MP4 file format).
/// </para>
/// </remarks>
public sealed class Mp4File : IMediaFile
{
	bool _disposed;

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read(ReadOnlySpan{byte})"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	Mp4File (Mp4Tag? tag, AudioProperties properties, Mp4AudioCodec audioCodec)
	{
		Tag = tag;
		_readTag = tag;
		Properties = properties;
		AudioCodec = audioCodec;
	}

	/// <summary>
	/// Gets or sets the iTunes-style metadata tags.
	/// </summary>
	/// <remarks>
	/// May be null if the file has no ilst atom.
	/// Will be automatically created when setting any tag property.
	/// </remarks>
	public Mp4Tag? Tag { get; set; }

	// The tag as read. A different tag assigned to Tag replaces the mapped fields on save, in
	// every meta, and the items the tag does not map stay; null removes all metadata, as Clear.
	readonly Mp4Tag? _readTag;

	/// <summary>
	/// Gets the audio properties (duration, bitrate, sample rate, etc.).
	/// </summary>
	public AudioProperties Properties { get; }

	/// <summary>
	/// Gets the audio codec used in this file.
	/// </summary>
	public Mp4AudioCodec AudioCodec { get; }

	/// <summary>
	/// Gets the file type identifier from ftyp box (e.g., "M4A", "mp42").
	/// </summary>
	public string FileType { get; private set; } = string.Empty;

	// Where the tag was read from. A save writes it back there.
	Mp4MetaPlacement MetaPlacement { get; init; }

	/// <summary>
	/// Gets the audio duration from audio properties.
	/// </summary>
	public TimeSpan Duration => Properties.Duration;

	/// <inheritdoc />
	Tag? IMediaFile.Tag => Tag;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties.IsValid ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Mp4;

	/// <summary>
	/// Gets or sets the title.
	/// </summary>
	public string? Title {
		get => Tag?.Title;
		set => EnsureTag ().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist.
	/// </summary>
	public string? Artist {
		get => Tag?.Artist;
		set => EnsureTag ().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album.
	/// </summary>
	public string? Album {
		get => Tag?.Album;
		set => EnsureTag ().Album = value;
	}

	/// <summary>
	/// Gets or sets the year.
	/// </summary>
	public string? Year {
		get => Tag?.Year;
		set => EnsureTag ().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre.
	/// </summary>
	public string? Genre {
		get => Tag?.Genre;
		set => EnsureTag ().Genre = value;
	}

	/// <summary>
	/// Gets or sets the comment.
	/// </summary>
	public string? Comment {
		get => Tag?.Comment;
		set => EnsureTag ().Comment = value;
	}

	/// <summary>
	/// Gets or sets the album artist.
	/// </summary>
	public string? AlbumArtist {
		get => Tag?.AlbumArtist;
		set => EnsureTag ().AlbumArtist = value;
	}

	/// <summary>
	/// Gets or sets the composer.
	/// </summary>
	public string? Composer {
		get => Tag?.Composer;
		set => EnsureTag ().Composer = value;
	}

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public uint? Track {
		get => Tag?.Track;
		set => EnsureTag ().Track = value;
	}

	/// <summary>
	/// Gets or sets the total number of tracks.
	/// </summary>
	public uint? TotalTracks {
		get => Tag?.TotalTracks;
		set => EnsureTag ().TotalTracks = value;
	}

	/// <summary>
	/// Gets or sets the disc number.
	/// </summary>
	public uint? DiscNumber {
		get => Tag?.DiscNumber;
		set => EnsureTag ().DiscNumber = value;
	}

	/// <summary>
	/// Gets or sets the total number of discs.
	/// </summary>
	public uint? TotalDiscs {
		get => Tag?.TotalDiscs;
		set => EnsureTag ().TotalDiscs = value;
	}

	// Sort order properties

	/// <summary>
	/// Gets or sets the album sort order.
	/// </summary>
	public string? AlbumSort {
		get => Tag?.AlbumSort;
		set => EnsureTag ().AlbumSort = value;
	}

	/// <summary>
	/// Gets or sets the artist sort order.
	/// </summary>
	public string? ArtistSort {
		get => Tag?.ArtistSort;
		set => EnsureTag ().ArtistSort = value;
	}

	/// <summary>
	/// Gets or sets the title sort order.
	/// </summary>
	public string? TitleSort {
		get => Tag?.TitleSort;
		set => EnsureTag ().TitleSort = value;
	}

	/// <summary>
	/// Gets or sets the album artist sort order.
	/// </summary>
	public string? AlbumArtistSort {
		get => Tag?.AlbumArtistSort;
		set => EnsureTag ().AlbumArtistSort = value;
	}

	/// <summary>
	/// Gets or sets the composer sort order.
	/// </summary>
	public string? ComposerSort {
		get => Tag?.ComposerSort;
		set => EnsureTag ().ComposerSort = value;
	}

	// Classical music properties

	/// <summary>
	/// Gets or sets the work name (for classical music).
	/// </summary>
	public string? Work {
		get => Tag?.Work;
		set => EnsureTag ().Work = value;
	}

	/// <summary>
	/// Gets or sets the movement name (for classical music).
	/// </summary>
	public string? Movement {
		get => Tag?.Movement;
		set => EnsureTag ().Movement = value;
	}

	/// <summary>
	/// Gets or sets the movement number (for classical music).
	/// </summary>
	public uint? MovementNumber {
		get => Tag?.MovementNumber;
		set => EnsureTag ().MovementNumber = value;
	}

	/// <summary>
	/// Gets or sets the total movement count (for classical music).
	/// </summary>
	public uint? MovementTotal {
		get => Tag?.MovementTotal;
		set => EnsureTag ().MovementTotal = value;
	}

	// Additional metadata properties

	/// <summary>
	/// Gets or sets whether gapless playback is enabled.
	/// </summary>
	public bool IsGapless {
		get => Tag?.IsGapless ?? false;
		set => EnsureTag ().IsGapless = value;
	}

	/// <summary>
	/// Gets or sets the ISRC (International Standard Recording Code).
	/// </summary>
	public string? Isrc {
		get => Tag?.Isrc;
		set => EnsureTag ().Isrc = value;
	}

	/// <summary>
	/// Gets or sets the conductor.
	/// </summary>
	public string? Conductor {
		get => Tag?.Conductor;
		set => EnsureTag ().Conductor = value;
	}

	/// <summary>
	/// Gets or sets the original release date.
	/// </summary>
	public string? OriginalReleaseDate {
		get => Tag?.OriginalReleaseDate;
		set => EnsureTag ().OriginalReleaseDate = value;
	}

	// ReplayGain properties

	/// <summary>
	/// Gets or sets the ReplayGain track gain value (e.g., "-6.50 dB").
	/// </summary>
	public string? ReplayGainTrackGain {
		get => Tag?.ReplayGainTrackGain;
		set => EnsureTag ().ReplayGainTrackGain = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain track peak value (e.g., "0.988547").
	/// </summary>
	public string? ReplayGainTrackPeak {
		get => Tag?.ReplayGainTrackPeak;
		set => EnsureTag ().ReplayGainTrackPeak = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain album gain value (e.g., "-5.20 dB").
	/// </summary>
	public string? ReplayGainAlbumGain {
		get => Tag?.ReplayGainAlbumGain;
		set => EnsureTag ().ReplayGainAlbumGain = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain album peak value (e.g., "0.995123").
	/// </summary>
	public string? ReplayGainAlbumPeak {
		get => Tag?.ReplayGainAlbumPeak;
		set => EnsureTag ().ReplayGainAlbumPeak = value;
	}

	// R128 loudness properties

	/// <summary>
	/// Gets or sets the R128 track gain value (Q7.8 fixed-point, e.g., "-512" for -2 dB).
	/// </summary>
	public string? R128TrackGain {
		get => Tag?.R128TrackGain;
		set => EnsureTag ().R128TrackGain = value;
	}

	/// <summary>
	/// Gets or sets the R128 album gain value (Q7.8 fixed-point, e.g., "256" for +1 dB).
	/// </summary>
	public string? R128AlbumGain {
		get => Tag?.R128AlbumGain;
		set => EnsureTag ().R128AlbumGain = value;
	}

	/// <summary>
	/// Gets or sets the R128 track gain value in decibels.
	/// </summary>
	/// <remarks>
	/// This is a convenience property that converts the Q7.8 fixed-point integer value
	/// stored in <see cref="R128TrackGain"/> to/from decibels as a double.
	/// The conversion formula is: dB = stored_value / 256.0
	/// </remarks>
	public double? R128TrackGainDb {
		get => Tag?.R128TrackGainDb;
		set => EnsureTag ().R128TrackGainDb = value;
	}

	/// <summary>
	/// Gets or sets the R128 album gain value in decibels.
	/// </summary>
	/// <remarks>
	/// This is a convenience property that converts the Q7.8 fixed-point integer value
	/// stored in <see cref="R128AlbumGain"/> to/from decibels as a double.
	/// The conversion formula is: dB = stored_value / 256.0
	/// </remarks>
	public double? R128AlbumGainDb {
		get => Tag?.R128AlbumGainDb;
		set => EnsureTag ().R128AlbumGainDb = value;
	}

	// AcoustID properties

	/// <summary>
	/// Gets or sets the AcoustID identifier.
	/// </summary>
	public string? AcoustIdId {
		get => Tag?.AcoustIdId;
		set => EnsureTag ().AcoustIdId = value;
	}

	/// <summary>
	/// Gets or sets the AcoustID audio fingerprint.
	/// </summary>
	public string? AcoustIdFingerprint {
		get => Tag?.AcoustIdFingerprint;
		set => EnsureTag ().AcoustIdFingerprint = value;
	}

	// Extended MusicBrainz properties

	/// <summary>
	/// Gets or sets the MusicBrainz Recording ID.
	/// </summary>
	public string? MusicBrainzRecordingId {
		get => Tag?.MusicBrainzRecordingId;
		set => EnsureTag ().MusicBrainzRecordingId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Disc ID.
	/// </summary>
	public string? MusicBrainzDiscId {
		get => Tag?.MusicBrainzDiscId;
		set => EnsureTag ().MusicBrainzDiscId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz release status (e.g., "official", "bootleg").
	/// </summary>
	public string? MusicBrainzReleaseStatus {
		get => Tag?.MusicBrainzReleaseStatus;
		set => EnsureTag ().MusicBrainzReleaseStatus = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz release type (e.g., "album", "single", "ep").
	/// </summary>
	public string? MusicBrainzReleaseType {
		get => Tag?.MusicBrainzReleaseType;
		set => EnsureTag ().MusicBrainzReleaseType = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz release country (ISO 3166-1 alpha-2).
	/// </summary>
	public string? MusicBrainzReleaseCountry {
		get => Tag?.MusicBrainzReleaseCountry;
		set => EnsureTag ().MusicBrainzReleaseCountry = value;
	}

	// DJ and remix properties

	/// <summary>
	/// Gets or sets the initial musical key (e.g., "Am", "F#m", "Cmaj").
	/// </summary>
	public string? InitialKey {
		get => Tag?.InitialKey;
		set => EnsureTag ().InitialKey = value;
	}

	/// <summary>
	/// Gets or sets the remixer or modifier of the track.
	/// </summary>
	public string? Remixer {
		get => Tag?.Remixer;
		set => EnsureTag ().Remixer = value;
	}

	/// <summary>
	/// Gets or sets the mood of the track (e.g., "Energetic", "Melancholic").
	/// </summary>
	public string? Mood {
		get => Tag?.Mood;
		set => EnsureTag ().Mood = value;
	}

	/// <summary>
	/// Gets or sets the subtitle (e.g., "Radio Edit", "Live Version").
	/// </summary>
	public string? Subtitle {
		get => Tag?.Subtitle;
		set => EnsureTag ().Subtitle = value;
	}

	// Collector properties

	/// <summary>
	/// Gets or sets the barcode (UPC/EAN) of the release.
	/// </summary>
	public string? Barcode {
		get => Tag?.Barcode;
		set => EnsureTag ().Barcode = value;
	}

	/// <summary>
	/// Gets or sets the label's catalog number.
	/// </summary>
	public string? CatalogNumber {
		get => Tag?.CatalogNumber;
		set => EnsureTag ().CatalogNumber = value;
	}

	/// <summary>
	/// Gets or sets the Amazon Standard Identification Number (ASIN).
	/// </summary>
	public string? AmazonId {
		get => Tag?.AmazonId;
		set => EnsureTag ().AmazonId = value;
	}

	// Library management properties

	/// <summary>
	/// Gets or sets the date/time when the file was tagged.
	/// </summary>
	public string? DateTagged {
		get => Tag?.DateTagged;
		set => EnsureTag ().DateTagged = value;
	}

	/// <summary>
	/// Gets or sets the language of the audio content (ISO 639-2).
	/// </summary>
	public string? Language {
		get => Tag?.Language;
		set => EnsureTag ().Language = value;
	}

	/// <summary>
	/// Gets or sets the original media type (e.g., "CD", "Vinyl", "Digital").
	/// </summary>
	public string? MediaType {
		get => Tag?.MediaType;
		set => EnsureTag ().MediaType = value;
	}

	/// <summary>
	/// Gets or sets the podcast feed URL.
	/// </summary>
#pragma warning disable CA1056 // URI properties should not be strings - API compatibility
	public string? PodcastFeedUrl {
		get => Tag?.PodcastFeedUrl;
		set => EnsureTag ().PodcastFeedUrl = value;
	}
#pragma warning restore CA1056

	/// <summary>
	/// Gets or sets whether this file is a podcast episode.
	/// </summary>
	public bool IsPodcast {
		get => Tag?.IsPodcast ?? false;
		set => EnsureTag ().IsPodcast = value;
	}

	/// <summary>
	/// Gets or sets the podcast episode GUID.
	/// </summary>
	public string? PodcastEpisodeGuid {
		get => Tag?.PodcastEpisodeGuid;
		set => EnsureTag ().PodcastEpisodeGuid = value;
	}

	/// <summary>
	/// Gets or sets the podcast category.
	/// </summary>
	public string? PodcastCategory {
		get => Tag?.PodcastCategory;
		set => EnsureTag ().PodcastCategory = value;
	}

	/// <summary>
	/// Gets or sets the podcast keywords.
	/// </summary>
	public string? PodcastKeywords {
		get => Tag?.PodcastKeywords;
		set => EnsureTag ().PodcastKeywords = value;
	}

	/// <summary>
	/// Gets or sets the narrator ("©nrt", written by Audible in .aax files).
	/// </summary>
	public string? Narrator {
		get => Tag?.Narrator;
		set => EnsureTag ().Narrator = value;
	}

	/// <summary>
	/// Gets or sets the short title ("@sti", written by Audible in .aax files).
	/// </summary>
	public string? ShortTitle {
		get => Tag?.ShortTitle;
		set => EnsureTag ().ShortTitle = value;
	}

	/// <summary>
	/// Gets or sets the parent (series or collection) title ("@pti", written by Audible in .aax files).
	/// </summary>
	public string? ParentTitle {
		get => Tag?.ParentTitle;
		set => EnsureTag ().ParentTitle = value;
	}

	/// <summary>
	/// Gets or sets the parent short title ("@PST", written by Audible in .aax files).
	/// </summary>
	public string? ParentShortTitle {
		get => Tag?.ParentShortTitle;
		set => EnsureTag ().ParentShortTitle = value;
	}

	/// <summary>
	/// Gets or sets the parent product identifier ("@ppi", written by Audible in .aax files).
	/// </summary>
	public string? ParentProductId {
		get => Tag?.ParentProductId;
		set => EnsureTag ().ParentProductId = value;
	}

	/// <summary>
	/// Gets or sets the product identifier ("prID", written by Audible in .aax files).
	/// </summary>
	public string? ProductId {
		get => Tag?.ProductId;
		set => EnsureTag ().ProductId = value;
	}

	/// <summary>
	/// Gets or sets the release date, as stored (for example <c>02-Jul-2012</c>) ("rldt", written by Audible in .aax files).
	/// </summary>
	public string? ReleaseDate {
		get => Tag?.ReleaseDate;
		set => EnsureTag ().ReleaseDate = value;
	}

	/// <summary>
	/// Gets or sets the product version ("VERS", written by Audible in .aax files).
	/// </summary>
	public string? ProductVersion {
		get => Tag?.ProductVersion;
		set => EnsureTag ().ProductVersion = value;
	}

	/// <summary>
	/// Gets or sets the content rating (Clean/Explicit/None).
	/// </summary>
	public Mp4ContentRating ContentRating {
		get => Tag?.ContentRating ?? Mp4ContentRating.None;
		set => EnsureTag ().ContentRating = value;
	}

	/// <summary>
	/// Gets the pictures (cover art) in this file.
	/// </summary>
#pragma warning disable CA1819 // Properties should not return arrays - API compatibility
	public IPicture[] Pictures => Tag?.Pictures ?? [];
#pragma warning restore CA1819

	/// <summary>
	/// Adds a picture to this file.
	/// </summary>
	/// <param name="picture">The picture to add.</param>
	public void AddPicture (IPicture picture)
	{
		var tag = EnsureTag ();
		var current = tag.Pictures;
		var newList = new IPicture[current.Length + 1];
		current.CopyTo (newList, 0);
		newList[^1] = picture;
		tag.Pictures = newList;
	}

	/// <summary>
	/// Removes all pictures from this file.
	/// </summary>
	public void RemovePictures ()
	{
		// Only the covers: every other field stays.
		if (Tag is not null)
			Tag.Pictures = [];
	}

	/// <summary>
	/// Clears all metadata from this file.
	/// </summary>
	public void ClearAllMetadata () => Tag?.Clear ();

	/// <summary>
	/// Gets a freeform (----) metadata value.
	/// </summary>
	/// <param name="mean">The mean (namespace) identifier.</param>
	/// <param name="name">The name identifier.</param>
	/// <returns>The value, or null if not present.</returns>
	public string? GetFreeformTag (string mean, string name)
	{
		if (Tag is null)
			return null;

		return Tag.GetFreeform (mean, name);
	}

	/// <summary>
	/// Sets a freeform (----) metadata value.
	/// </summary>
	/// <param name="mean">The mean (namespace) identifier.</param>
	/// <param name="name">The name identifier.</param>
	/// <param name="value">The value to set, or null to remove.</param>
	/// <remarks>
	/// In a QuickTime "mdta" meta the field is a key named by <paramref name="name"/>, or
	/// "mean:name" for a mean other than the name's own (com.apple.iTunes, or org.musicbrainz for
	/// the MusicBrainz names). Such a mean reads back only if it is made of letters, digits, '.',
	/// '-' and '_' with at least one dot (such as "org.example"); any other mean comes back as part
	/// of the name.
	/// </remarks>
	public void SetFreeformTag (string mean, string name, string? value)
	{
		EnsureTag ().SetFreeform (mean, name, value);
	}

	Mp4Tag EnsureTag ()
	{
		Tag ??= new Mp4Tag ();
		return Tag;
	}

	IReadOnlyList<Mp4Chapter> _chapters = [];
	bool _chaptersChanged;

	/// <summary>
	/// Gets or sets the chapter list, ordered by start time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reading takes the QuickTime chapter track (the text track a <c>tref</c>/<c>chap</c> reference names),
	/// then the Nero <c>chpl</c> box when there is no readable chapter track, or when a chapter sample lies
	/// outside the file or is not valid UTF-8 or UTF-16 text. When both hold the same titles,
	/// the start times come from <c>chpl</c>, which keeps 100 ns precision and a first chapter that starts
	/// after zero. Each chapter's <see cref="Mp4Chapter.EndTime"/> is the next start, or the end of the
	/// chapter track or file for the last one.
	/// </para>
	/// <para>
	/// Setting the list copies it, sorts it by start time, and derives end times. On save, both forms are
	/// written: <c>chpl</c> gets the first 255 chapters with titles cut to 255 UTF-8 bytes, and the chapter
	/// track gets every chapter. An existing chapter track is replaced under its track_ID; otherwise a new
	/// disabled text track is added and every sound and video track references it (except a track that is
	/// itself chapter media, such as Apple chapter images). The track's first sample starts at zero, as a
	/// QuickTime chapter list covers the whole timeline.
	/// </para>
	/// <para>
	/// The new samples go in their own <c>mdat</c> right after <c>moov</c>, and every chunk offset after it
	/// moves in the same update that a tag edit makes. That <c>mdat</c> replaces the one an earlier chapter save
	/// wrote, so repeated edits do not grow the file. Old chapter samples that share an <c>mdat</c> with other
	/// media stay there unreferenced. A new track follows its brand: an MP4 file gets a null media header and a
	/// 3GPP <c>tx3g</c> sample entry (Apple TN2174), a QuickTime file (major brand <c>qt  </c>, or no
	/// <c>ftyp</c>) a <c>gmhd</c> and the QTFF <c>text</c> sample description. A replaced track keeps its language, handler, and track flags.
	/// </para>
	/// <para>
	/// Fragmented movies (<c>moov/mvex</c>): chapter samples in movie fragments are read. No track is added to a
	/// fragmented movie or to a file with no track; <c>chpl</c> carries the list alone there. When a chapter
	/// track has samples in movie fragments, its chapters cannot be changed: the save fails with an error that
	/// says so, because the fragment samples would remain as a second chapter list.
	/// </para>
	/// <para>
	/// An empty list removes <c>chpl</c>, every track a <c>chap</c> reference names (the titles, and Apple chapter
	/// URL and image tracks), and those references. A save that leaves the list as read, or sets it to the list
	/// the file already holds, keeps the chapter bytes unchanged.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentException">The list contains a null chapter.</exception>
	public IReadOnlyList<Mp4Chapter> Chapters {
		get => _chapters;
		set {
			var list = new List<Mp4Chapter> ();
			foreach (var chapter in value ?? []) {
				if (chapter is null)
					throw new ArgumentException ("A chapter list cannot contain null.", nameof (value));
				list.Add (chapter);
			}

			var sorted = Mp4ChapterCodec.SortByStart (list);
			if (SameChapters (sorted, _chapters))
				return;

			_chapters = Mp4ChapterCodec.WithEndTimes (sorted, Duration);
			_chaptersChanged = true;
		}
	}

	static bool SameChapters (List<Mp4Chapter> left, IReadOnlyList<Mp4Chapter> right)
	{
		if (left.Count != right.Count)
			return false;

		for (var i = 0; i < left.Count; i++) {
			if (!left[i].Equals (right[i]))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		Tag = null;
		_chapters = [];
		_sourceFileSystem = null;
		SourcePath = null;
		_disposed = true;
	}

	/// <summary>
	/// Attempts to read an MP4 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static Mp4FileReadResult Read (ReadOnlySpan<byte> data) => Read (data, null);

	/// <summary>
	/// Reads from <paramref name="data"/>. Chapter samples are read from <paramref name="sampleSource"/>
	/// when it is set, because <paramref name="data"/> then holds the boxes without <c>mdat</c>, and
	/// <paramref name="positions"/> gives each kept box's file offset; otherwise <paramref name="data"/> is the
	/// whole file.
	/// </summary>
	static Mp4FileReadResult Read (ReadOnlySpan<byte> data, Stream? sampleSource, List<long>? positions = null)
	{
		if (data.Length < Mp4Box.HeaderSize)
			return Mp4FileReadResult.Failure ("Invalid MP4 file: data too short");

		// Parse top-level boxes. mdat is the media payload: keep its size, not its bytes.
		var boxes = new List<Mp4Box> ();
		var fragments = new List<(Mp4Box Moof, long Start)> ();
		var offset = 0;
		while (offset < data.Length) {
			var remaining = data.Length - offset;
			if (remaining < Mp4Box.HeaderSize)
				break;
			if (!TryMeasure (data.Slice (offset, remaining), out var type, out var boxSize, out _))
				break;
			if (boxSize > remaining)
				break;
			if (type == "mdat") {
				offset += (int)boxSize;
				continue;
			}

			var result = Mp4Box.Parse (data[offset..]);
			if (!result.IsSuccess)
				break;

			// Movie fragments can hold chapter samples; their data offsets can be relative to the moof.
			if (result.Box!.Type == "moof") {
				var at = positions is not null && boxes.Count < positions.Count ? positions[boxes.Count] : offset;
				fragments.Add ((result.Box, at));
			}

			boxes.Add (result.Box!);
			offset += result.BytesConsumed;
		}

		// Find key boxes
		Mp4Box? moov = null;
		var fileType = string.Empty;

		for (int i = 0; i < boxes.Count; i++) {
			var box = boxes[i];
			if (box.Type == "ftyp")
				fileType = ExtractFileType (box);
			else if (box.Type == "moov")
				moov = box;
		}

		if (moov is null)
			return Mp4FileReadResult.Failure ("Invalid MP4 file: missing moov box");

		// Extract audio properties from moov/trak/mdia
		var (properties, audioCodec) = ExtractAudioProperties (moov);

		// Extract metadata from moov/udta/meta/ilst or moov/meta/ilst
		var (tag, placement) = ExtractMetadata (moov);

		var file = new Mp4File (tag, properties, audioCodec) {
			FileType = fileType,
			MetaPlacement = placement
		};
		file._chapters = sampleSource is null
			? Mp4ChapterCodec.Read (moov, fragments, data, properties.Duration)
			: Mp4ChapterCodec.Read (moov, fragments, sampleSource, properties.Duration);

		return Mp4FileReadResult.Success (file, offset);
	}

	/// <summary>
	/// Attempts to read an MP4 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out Mp4File? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read an MP4 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out Mp4File? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid MP4 file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data contains an ftyp box at the expected location.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 8 bytes for a minimal box header
		if (data.Length < 8)
			return false;

		// First box should be ftyp - check at offset 4 for "ftyp"
		// Box format: size (4 bytes) + type (4 bytes)
		return data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p';
	}

	static string ExtractFileType (Mp4Box ftypBox)
	{
		if (ftypBox.Data.Length < 4)
			return string.Empty;
		return ftypBox.Data.Slice (0, 4).ToStringLatin1 ().TrimEnd ();
	}

	static (AudioProperties, Mp4AudioCodec) ExtractAudioProperties (Mp4Box moov)
	{
		// First try to get duration from mvhd (movie header) for overall duration
		uint movieTimescale = 0;
		ulong movieDuration = 0;
		var mvhd = moov.FindChild ("mvhd");
		if (mvhd is not null && mvhd.Data.Length >= 20) {
			var mvhdData = mvhd.Data;
			var mvhdVersion = mvhdData[0];
			if (mvhdVersion == 0) {
				// Version 0: 32-bit values
				// version(1) + flags(3) + creation(4) + modification(4) + timescale(4) + duration(4)
				movieTimescale = mvhdData.ToUInt32BE (12);
				movieDuration = mvhdData.ToUInt32BE (16);
			} else {
				// Version 1: 64-bit values
				if (mvhdData.Length >= 32) {
					movieTimescale = mvhdData.ToUInt32BE (20);
					movieDuration = mvhdData.ToUInt64BE (24);
				}
			}
		}

		// Navigate to moov/trak/mdia for track info
		var trak = moov.FindChild ("trak");
		if (trak is null)
			return (AudioProperties.Empty, Mp4AudioCodec.Unknown);

		var mdia = trak.FindChild ("mdia");
		if (mdia is null)
			return (AudioProperties.Empty, Mp4AudioCodec.Unknown);

		// Parse mdhd for track-level timescale (used for sample rate if not found in stsd)
		uint trackTimescale = movieTimescale;
		var mdhd = mdia.FindChild ("mdhd");
		if (mdhd is not null && mdhd.Data.Length >= 20) {
			var mdhdData = mdhd.Data;
			var mdhdVersion = mdhdData[0];
			if (mdhdVersion == 0) {
				trackTimescale = mdhdData.ToUInt32BE (12);
			} else if (mdhdData.Length >= 28) {
				trackTimescale = mdhdData.ToUInt32BE (20);
			}
		}

		// Navigate to moov/trak/mdia/minf/stbl/stsd for codec info
		var minf = mdia.FindChild ("minf");
		var stbl = minf?.FindChild ("stbl");
		var stsd = stbl?.FindChild ("stsd");

		var codec = Mp4AudioCodec.Unknown;
		var channels = 0;
		var sampleRate = 0;
		var bitsPerSample = 0;
		var bitrate = 0;

		if (stsd is not null && stsd.Data.Length >= 16) {
			// stsd is a FullBox: version(1) + flags(3) + entry_count(4) + sample_entries
			var entryData = stsd.Data.Slice (8);
			if (entryData.Length >= 36) {
				// Sample entry structure:
				// size(4) + type(4) + reserved(6) + data_ref_idx(2) + reserved(8)
				// + channelcount(2) + samplesize(2) + pre_defined(2) + reserved(2)
				// + samplerate(4, 16.16 fixed)
				var codecType = entryData.Slice (4, 4).ToStringLatin1 ();
				codec = codecType switch {
					// Audible .aax stores AAC in an "aavd" entry: the mp4a layout, esds included, with encrypted samples.
					"mp4a" or "aavd" => Mp4AudioCodec.Aac,
					"alac" => Mp4AudioCodec.Alac,
					"fLaC" => Mp4AudioCodec.Flac,
					"Opus" => Mp4AudioCodec.Opus,
					"ac-3" => Mp4AudioCodec.Ac3,
					"ec-3" => Mp4AudioCodec.Eac3,
					_ => Mp4AudioCodec.Unknown
				};

				// Audio sample entry fields (offsets from entryData start):
				// 8: reserved (6 bytes)
				// 14: data_reference_index (2 bytes)
				// 16: reserved (8 bytes)
				// 24: channelcount (2 bytes)
				// 26: samplesize (2 bytes)
				// 28: pre_defined (2 bytes)
				// 30: reserved (2 bytes)
				// 32: samplerate (4 bytes, 16.16 fixed-point)
				channels = entryData.ToUInt16BE (24);

				// For lossy codecs like AAC, bits per sample is meaningless
				// Only report it for lossless codecs like ALAC
				if (codec == Mp4AudioCodec.Alac || codec == Mp4AudioCodec.Flac)
					bitsPerSample = entryData.ToUInt16BE (26);

				// Sample rate is stored as 16.16 fixed-point, which overflows for rates > 65535
				// For high sample rates like 96kHz, 176.4kHz, 192kHz, use mdhd timescale instead
				if (trackTimescale > 65535) {
					// mdhd has a sample rate that cannot fit in 16.16 fixed-point
					sampleRate = (int)trackTimescale;
				} else {
					var rawSampleRate = entryData.ToUInt32BE (32);
					var stsdSampleRate = (int)(rawSampleRate >> 16);
					sampleRate = stsdSampleRate > 0 ? stsdSampleRate : (int)trackTimescale;
				}

				// Parse codec-specific extensions for bitrate
				var sampleEntrySize = entryData.ToUInt32BE (0);
				if (sampleEntrySize > 36 && entryData.Length > 36) {
					bitrate = ExtractBitrateFromCodecBox (entryData.Slice (36), codec);
				}
			}
		}

		// Fall back to track timescale if sample rate not found
		if (sampleRate == 0 && trackTimescale > 0)
			sampleRate = (int)trackTimescale;

		// Calculate duration from movie header
		var durationSeconds = movieTimescale > 0 ? (double)movieDuration / movieTimescale : 0;
		var audioDuration = Mp4AudioPropertiesParser.FromSeconds (durationSeconds);

		var codecName = codec switch {
			Mp4AudioCodec.Aac => "AAC",
			Mp4AudioCodec.Alac => "ALAC",
			Mp4AudioCodec.Flac => "FLAC",
			Mp4AudioCodec.Opus => "Opus",
			Mp4AudioCodec.Ac3 => "AC-3",
			Mp4AudioCodec.Eac3 => "E-AC-3",
			_ => null
		};

		var properties = new AudioProperties (audioDuration, bitrate, sampleRate, bitsPerSample, channels, codecName);
		return (properties, codec);
	}

	/// <summary>
	/// Extracts bitrate from codec-specific boxes (esds for AAC, alac for ALAC).
	/// </summary>
	static int ExtractBitrateFromCodecBox (BinaryData codecExtensions, Mp4AudioCodec codec)
	{
		if (codecExtensions.Length < 8)
			return 0;

		// Look for nested boxes within the sample entry extensions
		var offset = 0;
		while (offset + 8 <= codecExtensions.Length) {
			var boxSize = codecExtensions.ToUInt32BE (offset);
			if (boxSize < 8 || boxSize > codecExtensions.Length - offset)
				break;

			var boxType = codecExtensions.Slice (offset + 4, 4).ToStringLatin1 ();

			if (boxType == "esds" && codec == Mp4AudioCodec.Aac) {
				// esds box: version(1) + flags(3) + ES_Descriptor
				// Parse ES_Descriptor to find DecoderConfigDescriptor with bitrate
				return ParseEsdsBitrate (codecExtensions.Slice (offset + 8, (int)boxSize - 8));
			}

			offset += (int)boxSize;
		}

		return 0;
	}

	/// <summary>
	/// Parses bitrate from esds (Elementary Stream Descriptor) box.
	/// </summary>
	static int ParseEsdsBitrate (BinaryData esdsData)
	{
		if (esdsData.Length < 12)
			return 0;

		// Skip version(1) + flags(3)
		var offset = 4;

		// ES_Descriptor starts with tag 0x03
		if (offset >= esdsData.Length || esdsData[offset] != 0x03)
			return 0;
		offset++;

		// Skip size (variable length, typically 1 byte for small descriptors)
		offset += GetDescriptorSizeLength (esdsData.Slice (offset));
		if (offset + 3 > esdsData.Length)
			return 0;

		// ES_ID (2 bytes) + flags (1 byte)
		offset += 3;

		// DecoderConfigDescriptor tag 0x04
		if (offset >= esdsData.Length || esdsData[offset] != 0x04)
			return 0;
		offset++;

		// Skip size
		offset += GetDescriptorSizeLength (esdsData.Slice (offset));
		if (offset + 13 > esdsData.Length)
			return 0;

		// objectTypeIndication (1) + streamType (1) + bufferSizeDB (3) + maxBitrate (4) + avgBitrate (4)
		// Skip to avgBitrate at offset + 1 + 1 + 3 + 4 = 9
		var avgBitrate = esdsData.ToUInt32BE (offset + 9);

		// Convert from bits/second to kbits/second
		return (int)(avgBitrate / 1000);
	}

	/// <summary>
	/// Gets the length of a descriptor size field (1-4 bytes, expandable).
	/// </summary>
	static int GetDescriptorSizeLength (BinaryData data)
	{
		// Expandable size encoding: high bit = more bytes follow
		var length = 0;
		for (int i = 0; i < 4 && i < data.Length; i++) {
			length++;
			if ((data[i] & 0x80) == 0)
				break;
		}
		return length;
	}

	static (Mp4Tag? Tag, Mp4MetaPlacement Placement) ExtractMetadata (Mp4Box moov)
	{
		// iTunes and ffmpeg write moov/udta/meta. QuickTime and Apple devices write moov/meta
		// (https://developer.apple.com/documentation/quicktime-file-format/metadata_atom).
		// udta/meta is the tag when it holds an ilst. A moov/meta beside it is merged in and wins
		// for the fields it declares: QTFF gives metadata precedence over user data that declares
		// the same information. A set writes where the winning value lives and drops the other copy.
		var userDataMeta = moov.FindChild ("udta")?.FindChild ("meta");
		var userDataList = userDataMeta?.FindChild ("ilst");
		var movieMeta = moov.FindChild ("meta");
		var movieList = movieMeta?.FindChild ("ilst");

		if (userDataList is not null) {
			var tag = ParseIlst (userDataList, userDataMeta!, inMovie: false);
			if (movieList is not null)
				ReadMovieMeta (tag, movieList, movieMeta!);
			return (tag, Mp4MetaPlacement.UserData);
		}

		if (movieList is not null)
			return (ParseIlst (movieList, movieMeta!, inMovie: true), Mp4MetaPlacement.Movie);

		return (null, Mp4MetaPlacement.UserData);
	}

	static Mp4Tag ParseIlst (Mp4Box ilst, Mp4Box meta, bool inMovie)
	{
		var tag = new Mp4Tag ();
		var keys = Mp4QuickTimeKeys.Read (meta);
		if (keys is not null)
			tag.UseQuickTimeKeys (keys);

		// Each child of ilst is a metadata atom (©nam, ©ART, etc.)
		foreach (var atomBox in ilst.Children) {
			var atomId = atomBox.Type;

			// In an "mdta" meta every item that names a field is kept as it is until that field is set.
			if (keys is not null) {
				if (ReadSourceItem (atomBox, keys, tag, movie: false, inMovie))
					continue;
				if (atomId == Mp4AtomMapping.FreeformAtom)
					ParseFreeformAtom (atomBox, tag);
				else
					tag.PreserveIlstAtom (atomBox.Render ());
				continue;
			}

			// Handle cover art specially
			if (atomId == Mp4AtomMapping.CoverArt) {
				if (!ParseCoverArt (atomBox, tag))
					tag.PreserveIlstAtom (atomBox.Render ());
				continue;
			}

			// Handle freeform atoms (----). ParseFreeformAtom keeps a failed box, including its mean and name.
			if (atomId == Mp4AtomMapping.FreeformAtom) {
				ParseFreeformAtom (atomBox, tag);
				continue;
			}

			// Standard atoms contain data sub-atoms. A mapped atom is read from its payload even when
			// the box parser left it unparsed, so no property depends on ContainerBoxes; anything
			// else is kept as it is.
			var dataAtoms = TagAtomValue (atomBox);
			if (dataAtoms.Count > 0)
				tag.SetAtoms (atomId, dataAtoms);
			else
				tag.PreserveIlstAtom (atomBox.Render ());
		}

		return tag;
	}

	static void ReadMovieMeta (Mp4Tag tag, Mp4Box ilst, Mp4Box meta)
	{
		// Only an "mdta" meta is QuickTime metadata with precedence over user data. An iTunes
		// (mdir) ilst in moov/meta is read by no common reader beside udta/meta except ffprobe, so
		// its atoms give no value; an atom of a mapped field is still that field's item, kept as
		// it is until the field is set or cleared, so no stale copy survives.
		var keys = Mp4QuickTimeKeys.Read (meta);
		tag.UseMovieMeta (keys);
		foreach (var item in ilst.Children) {
			var named = keys is null ? AddItunesTwin (item, tag) : ReadSourceItem (item, keys, tag, movie: true, inMovieMeta: true);
			if (!named)
				tag.AddMovieItem (item.Render ());
		}
	}

	static bool AddItunesTwin (Mp4Box item, Mp4Tag tag)
	{
		if (item.Type == Mp4AtomMapping.CoverArt) {
			tag.AddSourcePictures (null, item.Render (), 0, Mp4SourceRank.MovieAtom, movie: true);
			return true;
		}

		if (item.Type == Mp4AtomMapping.FreeformAtom) {
			if (!TryReadFreeform (item, out var mean, out var name, out _))
				return false;
			tag.AddSourceFreeform (mean, name, null, item.Render (), 0, Mp4SourceRank.MovieAtom, movie: true);
			return true;
		}

		if (!Mp4AtomMapping.IsTagAtom (item.Type))
			return false;
		tag.AddSourceAtom (item.Type, null, item.Render (), 0, Mp4SourceRank.MovieAtom, movie: true);
		return true;
	}

	// Records an item that names a field as a kept source of that field: a keyed item, or an
	// iTunes atom found in an "mdta" meta or in a second meta. Returns false for any other item.
	// movie: the item belongs to a second meta. inMovieMeta: the meta is moov/meta, where a keyed
	// item beats an iTunes atom (AVFoundation reads only the keyed one there).
	static bool ReadSourceItem (Mp4Box item, Mp4QuickTimeKeys? keys, Mp4Tag tag, bool movie, bool inMovieMeta)
	{
		var type = item.Type.Length == 4
			? ((uint)item.Type[0] << 24) | ((uint)item.Type[1] << 16) | ((uint)item.Type[2] << 8) | item.Type[3]
			: 0;

		if (keys is not null && keys.TryResolve (type, out var field)) {
			var rank = inMovieMeta ? Mp4SourceRank.MovieKeyed : Mp4SourceRank.Keyed;
			var keyIndex = type;
			var dataAtoms = DataAtomsIn (item.Data.Span);
			switch (field.Kind) {
				case Mp4KeyedKind.Artwork:
					tag.AddSourcePictures (PicturesOf (dataAtoms), item.Render (), keyIndex, rank, movie);
					break;
				case Mp4KeyedKind.Freeform:
					tag.AddSourceFreeform (field.Mean!, field.Name!, dataAtoms, item.Render (), keyIndex, rank, movie);
					break;
				default:
					tag.AddSourceAtom (field.AtomId, Mp4Tag.FromKeyed (field.AtomId, dataAtoms, keys.IsTextKey (type)), item.Render (), keyIndex, rank, movie);
					break;
			}

			return true;
		}

		var atomRank = inMovieMeta ? Mp4SourceRank.MovieAtom : Mp4SourceRank.Atom;
		if (item.Type == Mp4AtomMapping.CoverArt) {
			var pictures = PicturesOf (DataAtomsOf (item));
			if (pictures is null)
				return false;
			tag.AddSourcePictures (pictures, item.Render (), 0, atomRank, movie);
			return true;
		}

		if (item.Type == Mp4AtomMapping.FreeformAtom) {
			if (!TryReadFreeform (item, out var mean, out var name, out var atoms))
				return false;
			tag.AddSourceFreeform (mean, name, atoms, item.Render (), 0, atomRank, movie);
			return true;
		}

		if (!Mp4AtomMapping.IsTagAtom (item.Type))
			return false;

		var value = TagAtomValue (item);
		if (value.Count == 0)
			return false;
		tag.AddSourceAtom (item.Type, value, item.Render (), 0, atomRank, movie);
		return true;
	}

	static List<Mp4DataAtom> TagAtomValue (Mp4Box atomBox) =>
		atomBox.Children.Count == 0 && Mp4AtomMapping.IsTagAtom (atomBox.Type)
			? DataAtomsIn (atomBox.Data.Span)
			: DataAtomsOf (atomBox);

	static Mp4Picture[]? PicturesOf (List<Mp4DataAtom> dataAtoms)
	{
		if (dataAtoms.Count == 0)
			return null;

		var pictures = new Mp4Picture[dataAtoms.Count];
		for (var i = 0; i < pictures.Length; i++)
			pictures[i] = Mp4Picture.FromDataAtom (dataAtoms[i]);
		return pictures;
	}

	static List<Mp4DataAtom> DataAtomsOf (Mp4Box box)
	{
		var dataAtoms = new List<Mp4DataAtom> ();
		foreach (var child in box.Children) {
			if (child.Type == "data" && child.Data.Length >= 8)
				dataAtoms.Add (Mp4DataAtom.Parse (child.Data.Span));
		}

		return dataAtoms;
	}

	static List<Mp4DataAtom> DataAtomsIn (ReadOnlySpan<byte> payload)
	{
		var dataAtoms = new List<Mp4DataAtom> ();
		foreach (var child in Mp4BoxParser.ParseChildren (payload)) {
			if (child.Type == "data" && child.Data.Length >= 8)
				dataAtoms.Add (Mp4DataAtom.Parse (child.Data.Span));
		}

		return dataAtoms;
	}

	static bool ParseCoverArt (Mp4Box covrBox, Mp4Tag tag)
	{
		var pictures = new List<Mp4Picture> ();

		foreach (var child in covrBox.Children) {
			if (child.Type == "data" && child.Data.Length >= 8) {
				var dataAtom = Mp4DataAtom.Parse (child.Data.Span);
				var picture = Mp4Picture.FromDataAtom (dataAtom);
				pictures.Add (picture);
			}
		}

		if (pictures.Count == 0)
			return false;

		tag.Pictures = [.. pictures];
		return true;
	}

	static void ParseFreeformAtom (Mp4Box freeformBox, Mp4Tag tag)
	{
		// Mean and name are kept even when the data child is missing, so a later set can drop
		// that preserved twin.
		if (!TryReadFreeform (freeformBox, out var mean, out var name, out var dataAtoms)) {
			tag.PreserveIlstAtom (freeformBox.Render (), mean, name);
			return;
		}

		tag.SetFreeformAtoms (mean, name, dataAtoms);
	}

	// Freeform atoms have mean, name, and data children.
	static bool TryReadFreeform (Mp4Box freeformBox, out string mean, out string name, out List<Mp4DataAtom> dataAtoms)
	{
		string? meanText = null;
		string? nameText = null;
		dataAtoms = new List<Mp4DataAtom> ();

		foreach (var child in freeformBox.Children) {
			if (child.Type == "mean" && child.Data.Length >= 4) {
				// Skip version(1) + flags(3)
				meanText = child.Data.Slice (4).ToStringUtf8 ();
			} else if (child.Type == "name" && child.Data.Length >= 4) {
				// Skip version(1) + flags(3)
				nameText = child.Data.Slice (4).ToStringUtf8 ();
			} else if (child.Type == "data" && child.Data.Length >= 8) {
				dataAtoms.Add (Mp4DataAtom.Parse (child.Data.Span));
			}
		}

		mean = meanText!;
		name = nameText!;
		return !string.IsNullOrEmpty (meanText) && !string.IsNullOrEmpty (nameText) && dataAtoms.Count > 0;
	}

	/// <summary>
	/// Reads an MP4 file from a stream. <c>mdat</c> payloads are not copied.
	/// </summary>
	internal static Mp4FileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return WithSource (Read (StreamBytes.ReadToEnd (stream)), sourcePath, fileSystem);

		if (stream.Position != 0)
			stream.Position = 0;

		using var kept = new MemoryStream ();
		var positions = new List<long> ();
		while (stream.Position + Mp4Box.HeaderSize <= stream.Length) {
			var start = stream.Position;
			var probe = StreamBytes.ReadAtMost (stream, Mp4Box.ExtendedHeaderSize);
			if (!TryMeasureStream (probe, start, stream.Length, out var type, out var boxEnd))
				break;
			if (boxEnd < start)
				break;

			var end = boxEnd > stream.Length ? stream.Length : boxEnd;
			if (type == "mdat") {
				stream.Position = end;
				continue;
			}

			var length = end - start;
			if (length > int.MaxValue)
				break;

			stream.Position = start;
			var box = StreamBytes.ReadAtMost (stream, (int)length);
			if (box.Length == 0)
				break;
			StreamBytes.Append (kept, box);
			positions.Add (start);
			if (box.Length < length)
				break;
		}

		return WithSource (Read (kept.ToArray (), stream, positions), sourcePath, fileSystem);
	}

	static Mp4FileReadResult WithSource (Mp4FileReadResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (result.IsSuccess && result.File is not null) {
			result.File.SourcePath = sourcePath;
			result.File._sourceFileSystem = fileSystem;
		}

		return result;
	}

	static bool TryMeasure (ReadOnlySpan<byte> data, out string type, out long boxSize, out int headerSize)
	{
		type = "";
		boxSize = 0;
		headerSize = 0;
		if (data.Length < Mp4Box.HeaderSize)
			return false;

		var size32 = BinaryPrimitives.ReadUInt32BigEndian (data);
		type = FourCC (data.Slice (4, 4));
		if (size32 == 1) {
			if (data.Length < Mp4Box.ExtendedHeaderSize)
				return false;
			boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian (data.Slice (8, 8));
			headerSize = Mp4Box.ExtendedHeaderSize;
		} else if (size32 == 0) {
			boxSize = data.Length;
			headerSize = Mp4Box.HeaderSize;
		} else {
			boxSize = size32;
			headerSize = Mp4Box.HeaderSize;
		}

		return boxSize >= headerSize;
	}

	static bool TryMeasureStream (ReadOnlySpan<byte> probe, long start, long fileLength, out string type, out long boxEnd)
	{
		type = "";
		boxEnd = start;
		if (probe.Length < Mp4Box.HeaderSize)
			return false;

		var size32 = BinaryPrimitives.ReadUInt32BigEndian (probe);
		type = FourCC (probe.Slice (4, 4));
		if (size32 == 1) {
			if (probe.Length < Mp4Box.ExtendedHeaderSize)
				return false;
			var boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian (probe.Slice (8, 8));
			if (boxSize < Mp4Box.ExtendedHeaderSize)
				return false;
			boxEnd = start + boxSize;
			return true;
		}

		if (size32 == 0) {
			boxEnd = fileLength;
			return true;
		}

		if (size32 < Mp4Box.HeaderSize)
			return false;

		boxEnd = start + size32;
		return true;
	}

	static string FourCC (ReadOnlySpan<byte> data) =>
		new ([(char)data[0], (char)data[1], (char)data[2], (char)data[3]]);

	/// <summary>
	/// Attempts to read an MP4 file from a file path.
	/// </summary>
	/// <param name="path">The path to the MP4 file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static Mp4FileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return Mp4FileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Asynchronously attempts to read an MP4 file from a file path.
	/// </summary>
	/// <param name="path">The path to the MP4 file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static async Task<Mp4FileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return Mp4FileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Renders the complete MP4 file with updated metadata.
	/// </summary>
	/// <param name="originalData">The original file data.</param>
	/// <returns>The complete file data, or empty if rendering failed.</returns>
	/// <remarks>
	/// The moov.udta.meta.ilst atom is rebuilt with current tag values.
	/// All other atoms are preserved from the original file.
	/// Rendering fails when <see cref="Chapters"/> was changed and the chapter track has samples in movie
	/// fragments (see <see cref="Chapters"/>); <see cref="SaveToFile(string, ReadOnlySpan{byte}, IFileSystem?)"/> reports why.
	/// </remarks>
	public BinaryData Render (ReadOnlySpan<byte> originalData) => Render (originalData, out _);

	BinaryData Render (ReadOnlySpan<byte> originalData, out string? error)
	{
		error = null;
		if (originalData.Length < Mp4Box.HeaderSize)
			return BinaryData.Empty;

		// Parse all top-level boxes to find their positions and content
		var boxes = new List<(Mp4Box Box, int Start, int Length)> ();
		var offset = 0;
		var tailStart = -1;
		while (offset < originalData.Length) {
			var remaining = originalData[offset..];
			if (remaining.Length < Mp4Box.HeaderSize) {
				tailStart = offset;
				break;
			}

			var result = Mp4Box.Parse (remaining);
			if (!result.IsSuccess) {
				tailStart = offset;
				break;
			}

			boxes.Add ((result.Box!, offset, result.BytesConsumed));
			offset += result.BytesConsumed;
		}

		// Find moov box
		int moovIndex = -1;
		for (int i = 0; i < boxes.Count; i++) {
			if (boxes[i].Box.Type == "moov") {
				moovIndex = i;
				break;
			}
		}

		if (moovIndex < 0)
			return BinaryData.Empty;

		var oldMoovStart = boxes[moovIndex].Start;
		var oldMoovLength = boxes[moovIndex].Length;
		var chapterPlan = PlanChapters (boxes, moovIndex, originalData);
		if (chapterPlan?.Error is not null) {
			error = chapterPlan.Error;
			return BinaryData.Empty;
		}

		var newMoov = RebuildMoovWithMetadata (boxes[moovIndex].Box, chapterPlan);
		// New chapter samples get their own mdat right after moov, so they move the rest of the file too.
		var chapterData = chapterPlan is null || chapterPlan.TrackId == 0
			? BinaryData.Empty
			: WrapBox ("mdat", new BinaryData (chapterPlan.SampleData));
		// The mdat an earlier chapter save wrote sits right after moov and holds only the samples this save
		// replaces. It goes, so repeated chapter edits do not leave a dead copy each time.
		var droppedIndex = FindReplacedChapterData (boxes, moovIndex, chapterPlan, tailStart >= 0 ? originalData[tailStart..] : []);
		var replacedLength = oldMoovLength + (droppedIndex < 0 ? 0 : boxes[droppedIndex].Length);
		var delta = newMoov.Length + chapterData.Length - replacedLength;
		var oldMoovEnd = oldMoovStart + replacedLength;
		// Offsets are absolute file positions. Anything at or after the old moov moves with it,
		// including media that is not stored in a box named mdat. Entries inside moov stay put.
		if (delta != 0)
			newMoov = AdjustChunkOffsets (newMoov, oldMoovEnd, delta);
		if (!chapterData.IsEmpty) {
			// The chapter track was built with offset 0, which no shift moves. Point it at the new mdat now.
			var moovBytes = newMoov.ToArray ();
			var samplesAt = (uint)(oldMoovStart + moovBytes.Length + Mp4Box.HeaderSize);
			Mp4ChapterCodec.WriteChunkOffset (moovBytes, chapterPlan!.TrackId, samplesAt);
			newMoov = new BinaryData (moovBytes);
		}

		// Reassemble the file
		using var builder = new BinaryDataBuilder ();

		for (int i = 0; i < boxes.Count; i++) {
			if (i == moovIndex) {
				builder.Add (newMoov);
				builder.Add (chapterData);
			} else if (i == droppedIndex) {
				continue;
			} else {
				var (box, start, length) = boxes[i];
				var slice = originalData.Slice (start, length);
				if (delta != 0 && HoldsShiftedFileOffset (box.Type)) {
					var copy = slice.ToArray ();
					PatchChunkOffsets (copy, oldMoovEnd, delta);
					builder.Add (copy);
				} else {
					builder.Add (new BinaryData (slice));
				}
			}
		}

		if (tailStart >= 0)
			builder.Add (new BinaryData (originalData[tailStart..]));

		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Adds <paramref name="delta"/> to stco and co64 entries that point at or
	/// after the old end of moov. ISO 14496-12 chunk offsets are file offsets.
	/// §8.7.4 maps each chunk through stsc to a sample description. That entry's
	/// data_reference_index (§8.5.2) selects a dref entry. An external entry
	/// (§8.7.2, self-contained flag clear) stays where it is.
	/// When stsc rows and sample-entry indexes are both present, each chunk is resolved
	/// by binary search on first_chunk. Otherwise the trak shifts each offset at or after
	/// <paramref name="threshold"/> in one pass, unless every referenced entry is external.
	/// With no stsc, a trak is unchanged only when every referenced entry is external.
	/// A missing stsd, an empty stsd, or no indexes still leaves the trak unchanged
	/// when any dref entry is external. No dref means self-contained.
	/// Indexes read before a truncated sample entry are kept. A description index past
	/// the last complete entry does not move when any dref entry is external.
	/// A row with no description, or a data reference that does not resolve, does not
	/// move an external-only trak.
	/// Offsets below <paramref name="threshold"/> do not move.
	/// </summary>
	static BinaryData AdjustChunkOffsets (BinaryData moov, int threshold, int delta)
	{
		var bytes = moov.ToArray ();
		PatchChunkOffsets (bytes, threshold, delta);
		return new BinaryData (bytes);
	}

	static void PatchChunkOffsets (Span<byte> box, int threshold, int delta, ChunkAdjust adjust = default)
	{
		if (!TryMeasureBox (box, out var size, out var header))
			return;
		if (size < header || size > box.Length)
			return;

		var type = box.Slice (4, 4);
		var payload = box.Slice (header, size - header);
		if (type.SequenceEqual ("stco"u8)) {
			if (!adjust.LeavesChunkTable)
				PatchOffsets (payload, 4, threshold, delta, adjust);
		} else if (type.SequenceEqual ("co64"u8)) {
			if (!adjust.LeavesChunkTable)
				PatchOffsets (payload, 8, threshold, delta, adjust);
		} else if (type.SequenceEqual ("tfhd"u8))
			PatchTfhd (payload, threshold, delta);
		else if (type.SequenceEqual ("tfra"u8))
			PatchTfra (payload, threshold, delta);
		else if (type.SequenceEqual ("saio"u8))
			PatchSaio (payload, threshold, delta);
		else if (type.SequenceEqual ("iloc"u8))
			PatchIloc (payload, threshold, delta);
		else if (type.SequenceEqual ("trak"u8))
			PatchTrakOffsets (payload, threshold, delta);
		else if (adjust.InTrak && type.SequenceEqual ("stbl"u8))
			PatchStblOffsets (payload, threshold, delta, adjust);
		else if (type.SequenceEqual ("meta"u8)) {
			// ISO meta keeps version and flags in front of its children.
			if (Mp4BoxParser.IsIsoMeta (payload))
				WalkBoxes (payload.Slice (4), threshold, delta, adjust);
			else
				WalkBoxes (payload, threshold, delta, adjust);
		} else if (IsOffsetContainer (type))
			WalkBoxes (payload, threshold, delta, adjust);
	}

	static bool HoldsShiftedFileOffset (string type) =>
		type is "moof" or "mfra" or "meta" or "udta";

	/// <summary>
	/// One stsc table applies to the chunk offset box in the same sample table.
	/// No rows keeps the trak-level rule: skip only when the trak is entirely external.
	/// </summary>
	static void PatchTrakOffsets (Span<byte> payload, int threshold, int delta)
	{
		var external = new List<bool> ();
		var references = new List<int> ();
		CollectTrackLayout (payload, external, references, null);
		var trakExternal = ReferencedEntriesAreExternal (external, references);
		// No sample entries and an external dref: the chunks are not in this file.
		if (trakExternal && references.Count == 0)
			return;

		WalkBoxes (payload, threshold, delta, new ChunkAdjust {
			InTrak = true,
			TrakExternal = trakExternal,
			External = external,
		});
	}

	static void PatchStblOffsets (Span<byte> payload, int threshold, int delta, ChunkAdjust adjust)
	{
		var references = new List<int> ();
		var rows = new List<StscRow> ();
		// dref lives in dinf, not in this stbl. A dref found here must not shift the trak's indexes.
		CollectTrackLayout (payload, new List<bool> (), references, rows);
		if (rows.Count > 0 && references.Count > 0) {
			adjust = new ChunkAdjust {
				InTrak = true,
				PerChunk = true,
				TrakExternal = adjust.TrakExternal,
				External = adjust.External,
				References = references,
				Rows = rows,
			};
		} else if (adjust.TrakExternal) {
			return;
		}

		WalkBoxes (payload, threshold, delta, adjust);
	}

	static bool HasExternalEntry (List<bool>? external)
	{
		if (external is null)
			return false;

		foreach (var isExternal in external) {
			if (isExternal)
				return true;
		}

		return false;
	}

	static bool IsOffsetContainer (ReadOnlySpan<byte> type) =>
		type.SequenceEqual ("moov"u8)
		|| type.SequenceEqual ("mdia"u8)
		|| type.SequenceEqual ("minf"u8)
		|| type.SequenceEqual ("stbl"u8)
		|| type.SequenceEqual ("edts"u8)
		|| type.SequenceEqual ("udta"u8)
		|| type.SequenceEqual ("moof"u8)
		|| type.SequenceEqual ("traf"u8)
		|| type.SequenceEqual ("mfra"u8);

	/// <summary>
	/// True when every sample entry this trak references is an external dref.
	/// ISO 14496-12 §8.5.2 stores a 1-based data_reference_index in each sample entry.
	/// §8.7.2: the indexed dref entry is external when its self-contained flag is clear.
	/// This is the rule for a trak with no stsc rows. §8.7.4 picks the entry per chunk when rows exist.
	/// A missing or out-of-range index is self-contained, so the trak is not entirely external.
	/// An entry that does not contain its flags is unknown and is not external by itself.
	/// If stsd is missing, empty, or yields no data_reference_index values, any
	/// external dref entry leaves the trak unchanged. No dref still means self-contained.
	/// </summary>
	static bool ReferencedEntriesAreExternal (List<bool> external, List<int> references)
	{
		// No stsd, an empty stsd, or a description that produced no indexes: do not assume
		// the samples are in this file when any dref entry is external.
		if (references.Count == 0) {
			foreach (var isExternal in external) {
				if (isExternal)
					return true;
			}

			return false;
		}

		foreach (var index in references) {
			if (index < 1 || index > external.Count || !external[index - 1])
				return false;
		}

		return true;
	}

	static void CollectTrackLayout (ReadOnlySpan<byte> payload, List<bool> external, List<int> references, List<StscRow>? rows)
	{
		var offset = 0;
		while (offset + 8 <= payload.Length) {
			if (!TryMeasureBox (payload.Slice (offset), out var size, out var header))
				break;
			if (size <= 0 || size > payload.Length - offset)
				break;
			if (size < header) {
				offset += size;
				continue;
			}

			var type = payload.Slice (offset + 4, 4);
			var body = payload.Slice (offset + header, size - header);
			if (type.SequenceEqual ("dref"u8))
				ReadDrefEntries (body, external);
			else if (type.SequenceEqual ("stsd"u8))
				ReadStsdReferences (body, references);
			else if (rows is not null && type.SequenceEqual ("stsc"u8))
				ReadStscRows (body, rows);
			else if (IsTrackLayoutParent (type))
				CollectTrackLayout (body, external, references, rows);

			offset += size;
		}
	}

	/// <summary>
	/// True when the 32-bit size is explicit and larger than <paramref name="box"/>.
	/// Size 0 extends to the end of the parent, and size 1 is largesize; <see cref="TryMeasureBox"/> decides those.
	/// </summary>
	static bool ExplicitSizePastParent (ReadOnlySpan<byte> box)
	{
		if (box.Length < 4)
			return false;

		var declared = BinaryPrimitives.ReadUInt32BigEndian (box);
		return declared > 1 && declared > (uint)box.Length;
	}

	static bool IsTrackLayoutParent (ReadOnlySpan<byte> type) =>
		type.SequenceEqual ("mdia"u8)
		|| type.SequenceEqual ("minf"u8)
		|| type.SequenceEqual ("dinf"u8)
		|| type.SequenceEqual ("stbl"u8);

	static void ReadDrefEntries (ReadOnlySpan<byte> payload, List<bool> external)
	{
		if (payload.Length < 8)
			return;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0u; i < count; i++) {
			if (pos >= payload.Length || payload.Length - pos < 8)
				break;
			// An explicit size past this dref is truncated. The next box's bytes are not flags.
			if (ExplicitSizePastParent (payload.Slice (pos)))
				break;
			if (!TryMeasureBox (payload.Slice (pos), out var size, out var header))
				break;
			if (size < header || size > payload.Length - pos)
				break;

			// FullBox flags follow this entry's header. Bit 0 set means the samples live in this file.
			// A shorter entry does not contain flags; the next entry's bytes are not this entry's flags.
			var isExternal = false;
			if (size >= header + 4) {
				var flags = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos + header, 4));
				isExternal = (flags & 1) == 0;
			}

			external.Add (isExternal);
			pos += size;
		}
	}

	static void ReadStsdReferences (ReadOnlySpan<byte> payload, List<int> references)
	{
		if (payload.Length < 8)
			return;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0u; i < count; i++) {
			// The entry is not in this box. Keep indexes already read. Do not invent one.
			// With none read, any external dref still leaves the trak unchanged.
			if (pos >= payload.Length || payload.Length - pos < 8)
				return;

			// Truncated stsd: the declared size runs past this box.
			// Do not read the next box as data_reference_index, and do not append 0.
			if (ExplicitSizePastParent (payload.Slice (pos)))
				return;

			if (!TryMeasureBox (payload.Slice (pos), out var size, out var header)
				|| size < header
				|| size > payload.Length - pos)
				return;

			// §8.5.2: 6 reserved bytes, then uint16 data_reference_index, after the box header.
			// The field must sit inside this entry. A short entry's index is missing.
			if (size < header + 8)
				references.Add (0);
			else
				references.Add (BinaryPrimitives.ReadUInt16BigEndian (payload.Slice (pos + header + 6, 2)));

			pos += size;
		}
	}

	static void WalkBoxes (Span<byte> payload, int threshold, int delta, ChunkAdjust adjust)
	{
		var offset = 0;
		while (offset + 8 <= payload.Length) {
			if (!TryMeasureBox (payload.Slice (offset), out var size, out var header))
				break;

			// A size below the header is not "extends to the end" (that is size 0 only).
			// Step over the declared length so a later sibling stays visible.
			if (size < header) {
				if (size <= 0)
					break;
				offset += size;
				continue;
			}

			PatchChunkOffsets (payload.Slice (offset, size), threshold, delta, adjust);
			offset += size;
		}
	}

	/// <summary>
	/// Measures one box. Size 0 runs to the end of <paramref name="data"/> (the parent, in a nested walk).
	/// Size 1 is a 64-bit largesize and is not the size-0 rule. An illegal size that still fits in the
	/// parent is returned so the caller can step over it; only an unknown remainder returns false.
	/// </summary>
	static bool TryMeasureBox (ReadOnlySpan<byte> data, out int size, out int header)
	{
		size = 0;
		header = 8;
		if (data.Length < 8)
			return false;

		var size32 = BinaryPrimitives.ReadUInt32BigEndian (data);
		if (size32 == 1) {
			header = 16;
			if (data.Length < 16)
				return false;

			var large = BinaryPrimitives.ReadUInt64BigEndian (data.Slice (8));
			// largesize includes the 16-byte header. A value that does not fit is not "extends to the end".
			if (large == 0 || large > (ulong)data.Length)
				return false;

			size = (int)large;
			return true;
		}

		if (size32 == 0 || size32 > (uint)data.Length) {
			size = data.Length;
			return true;
		}

		size = (int)size32;
		return true;
	}

	static void PatchOffsets (Span<byte> payload, int width, int threshold, int delta, ChunkAdjust adjust)
	{
		if (payload.Length < 8)
			return;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0; i < count && pos + width <= payload.Length; i++) {
			// Chunk numbers in stsc and stco/co64 are 1-based. §8.7.4 / §8.7.5.
			if (adjust.Moves ((uint)i + 1)) {
				if (width == 4) {
					var value = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos));
					if (value >= (uint)threshold)
						BinaryPrimitives.WriteUInt32BigEndian (payload.Slice (pos), (uint)(value + delta));
				} else {
					var value = BinaryPrimitives.ReadUInt64BigEndian (payload.Slice (pos));
					if (value >= (ulong)threshold)
						BinaryPrimitives.WriteUInt64BigEndian (payload.Slice (pos), (ulong)((long)value + delta));
				}
			}

			pos += width;
		}
	}

	/// <summary>
	/// tfhd always stores track_ID. Flag 1 then stores the 64-bit base data offset. ISO 14496-12 §8.8.7.
	/// </summary>
	static void PatchTfhd (Span<byte> payload, int threshold, int delta)
	{
		if (payload.Length < 16)
			return;

		var flags = BinaryPrimitives.ReadUInt32BigEndian (payload) & 0xFFFFFF;
		if ((flags & 0x1) == 0)
			return;

		PatchWide (payload.Slice (8), 8, threshold, delta);
	}

	/// <summary>
	/// tfra moof_offset is 32 bits in version 0 and 64 bits in version 1. ISO 14496-12 §8.8.10.
	/// </summary>
	static void PatchTfra (Span<byte> payload, int threshold, int delta)
	{
		if (payload.Length < 16)
			return;

		var version = payload[0];
		var packed = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (8));
		var trafSize = (int)((packed >> 4) & 0x3) + 1;
		var trunSize = (int)((packed >> 2) & 0x3) + 1;
		var sampleSize = (int)(packed & 0x3) + 1;
		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (12));
		var timeSize = version == 0 ? 4 : 8;
		var pos = 16;
		for (var i = 0u; i < count; i++) {
			if (pos > payload.Length - timeSize)
				return;
			pos += timeSize;
			if (!SkipOrPatch (payload, ref pos, timeSize, true, threshold, delta))
				return;
			pos += trafSize + trunSize + sampleSize;
			if (pos > payload.Length)
				return;
		}
	}

	/// <summary>
	/// Flag 1 inserts 8 bytes before entry_count. Version 0 offsets are 32 bits, later versions 64. §8.7.9.
	/// </summary>
	static void PatchSaio (Span<byte> payload, int threshold, int delta)
	{
		if (payload.Length < 8)
			return;

		var header = BinaryPrimitives.ReadUInt32BigEndian (payload);
		var version = payload[0];
		var pos = 4;
		if ((header & 0x1) != 0) {
			if (payload.Length < pos + 8)
				return;
			pos += 8;
		}

		if (payload.Length < pos + 4)
			return;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos));
		pos += 4;
		var width = version == 0 ? 4 : 8;
		for (var i = 0u; i < count; i++) {
			if (!SkipOrPatch (payload, ref pos, width, true, threshold, delta))
				return;
		}
	}

	/// <summary>
	/// iloc base_offset and extent_offset move only for construction method 0 in this file. §8.11.3.
	/// </summary>
	static void PatchIloc (Span<byte> payload, int threshold, int delta)
	{
		if (payload.Length < 8)
			return;

		var version = payload[0];
		var offsetSize = payload[4] >> 4;
		var lengthSize = payload[4] & 0x0F;
		var baseSize = payload[5] >> 4;
		var indexSize = version is 1 or 2 ? payload[5] & 0x0F : 0;
		var pos = 6;
		uint count;
		if (version < 2) {
			if (payload.Length < pos + 2)
				return;
			count = BinaryPrimitives.ReadUInt16BigEndian (payload.Slice (pos));
			pos += 2;
		} else {
			if (payload.Length < pos + 4)
				return;
			count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos));
			pos += 4;
		}

		for (var i = 0u; i < count; i++) {
			var idSize = version < 2 ? 2 : 4;
			if (pos > payload.Length - idSize)
				return;
			pos += idSize;

			var construction = 0;
			if (version is 1 or 2) {
				if (pos > payload.Length - 2)
					return;
				construction = BinaryPrimitives.ReadUInt16BigEndian (payload.Slice (pos)) & 0xF;
				pos += 2;
			}

			if (pos > payload.Length - 2)
				return;
			var dataReference = BinaryPrimitives.ReadUInt16BigEndian (payload.Slice (pos));
			pos += 2;
			var inThisFile = dataReference == 0 && construction == 0;
			var basePos = pos;
			if (!TryReadWide (payload, pos, baseSize, out var baseValue))
				return;
			pos += WidthBytes (baseSize);
			if (pos > payload.Length - 2)
				return;
			var extents = BinaryPrimitives.ReadUInt16BigEndian (payload.Slice (pos));
			pos += 2;
			// Construction method 0: the file position is base_offset + extent_offset.
			// A base at or past the old moov is the item's file position, so it moves once
			// and the extent stays relative. A zero base makes each extent a file offset.
			var moveBase = inThisFile && baseSize != 0 && baseValue >= (ulong)threshold;
			for (var extent = 0; extent < extents; extent++) {
				if (indexSize > 0 && !SkipOrPatch (payload, ref pos, indexSize, false, threshold, delta))
					return;
				var extentPos = pos;
				if (!TryReadWide (payload, pos, offsetSize, out var extentValue))
					return;
				pos += WidthBytes (offsetSize);
				if (inThisFile && !moveBase && offsetSize != 0) {
					var absolute = baseValue + extentValue;
					if (absolute >= (ulong)threshold)
						AddDelta (payload.Slice (extentPos, offsetSize), offsetSize, delta);
				}
				if (!SkipOrPatch (payload, ref pos, lengthSize, false, threshold, delta))
					return;
			}
			if (moveBase)
				AddDelta (payload.Slice (basePos, baseSize), baseSize, delta);
		}
	}

	static int WidthBytes (int width) => width is 4 or 8 ? width : 0;

	static bool TryReadWide (ReadOnlySpan<byte> payload, int pos, int width, out ulong value)
	{
		value = 0;
		if (width == 0)
			return true;
		if (width != 4 && width != 8)
			return false;
		if (pos < 0 || pos > payload.Length - width)
			return false;
		value = width == 4
			? BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos))
			: BinaryPrimitives.ReadUInt64BigEndian (payload.Slice (pos));
		return true;
	}

	static void AddDelta (Span<byte> field, int width, int delta)
	{
		if (width == 4) {
			var value = BinaryPrimitives.ReadUInt32BigEndian (field);
			BinaryPrimitives.WriteUInt32BigEndian (field, (uint)(value + delta));
		} else {
			var value = BinaryPrimitives.ReadUInt64BigEndian (field);
			BinaryPrimitives.WriteUInt64BigEndian (field, (ulong)((long)value + delta));
		}
	}

	static bool SkipOrPatch (Span<byte> payload, ref int pos, int width, bool patch, int threshold, int delta)
	{
		if (width == 0)
			return true;
		if (width != 4 && width != 8)
			return false;
		if (pos > payload.Length - width)
			return false;
		if (patch)
			PatchWide (payload.Slice (pos, width), width, threshold, delta);
		pos += width;
		return true;
	}

	static void PatchWide (Span<byte> field, int width, int threshold, int delta)
	{
		if (width == 4) {
			var value = BinaryPrimitives.ReadUInt32BigEndian (field);
			if (value >= (uint)threshold)
				BinaryPrimitives.WriteUInt32BigEndian (field, (uint)(value + delta));
		} else {
			var value = BinaryPrimitives.ReadUInt64BigEndian (field);
			if (value >= (ulong)threshold)
				BinaryPrimitives.WriteUInt64BigEndian (field, (ulong)((long)value + delta));
		}
	}

	/// <summary>
	/// §8.7.4: entry_count, then first_chunk, samples_per_chunk, sample_description_index.
	/// A short table stops at the last complete row. samples_per_chunk does not select the file.
	/// </summary>
	static void ReadStscRows (ReadOnlySpan<byte> payload, List<StscRow> rows)
	{
		if (payload.Length < 8)
			return;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0u; i < count; i++) {
			if (payload.Length - pos < 12)
				break;

			rows.Add (new StscRow (
				BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos)),
				BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos + 8))));
			pos += 12;
		}
	}

	/// <summary>
	/// The row with the greatest first_chunk that is still &lt;= <paramref name="chunk"/>.
	/// §8.7.4 orders rows by first_chunk. A later row wins when first_chunk ties.
	/// No such row means the description is missing.
	/// </summary>
	static bool TrySampleDescription (List<StscRow> rows, uint chunk, out uint description)
	{
		description = 0;
		var lo = 0;
		var hi = rows.Count - 1;
		var found = -1;
		while (lo <= hi) {
			var mid = lo + ((hi - lo) / 2);
			if (rows[mid].FirstChunk <= chunk) {
				found = mid;
				lo = mid + 1;
			} else {
				hi = mid - 1;
			}
		}

		if (found < 0)
			return false;

		description = rows[found].SampleDescriptionIndex;
		return true;
	}

	/// <summary>
	/// One stsc row. first_chunk and sample_description_index are 1-based.
	/// </summary>
	readonly record struct StscRow (uint FirstChunk, uint SampleDescriptionIndex);

	/// <summary>
	/// How to shift chunk offsets inside one trak. Per-chunk uses that trak's dref table
	/// and the enclosing stbl's stsd and stsc. Outside a trak every in-range offset moves.
	/// </summary>
	readonly struct ChunkAdjust
	{
		public bool InTrak { get; init; }

		public bool TrakExternal { get; init; }

		public bool PerChunk { get; init; }

		public List<bool>? External { get; init; }

		public List<int>? References { get; init; }

		public List<StscRow>? Rows { get; init; }

		public bool LeavesChunkTable => InTrak && TrakExternal && !PerChunk;

		public bool Moves (uint chunk)
		{
			if (!PerChunk || Rows is null || References is null || External is null)
				return true;

			// No covering row, description 0, or a data reference that does not resolve.
			// Self-contained unless this trak is external-only. §8.7.4 / §8.7.2.
			if (!TrySampleDescription (Rows, chunk, out var description))
				return !TrakExternal;
			if (description < 1)
				return !TrakExternal;

			// Past the last complete sample entry. Do not treat that unread entry as
			// self-contained when any dref entry is external. §8.5.2 / §8.7.2.
			if (description > (uint)References.Count)
				return !HasExternalEntry (External);

			var dataReference = References[(int)description - 1];
			if (dataReference < 1 || dataReference > External.Count)
				return !TrakExternal;

			return !External[dataReference - 1];
		}
	}

	// The tag as it is written. A tag assigned in place of the one read replaces the mapped
	// fields only: the file's tag is read again, its mapped fields cleared (the items the tag does
	// not map, such as Apple's make, model and location, stay), and given the assigned values.
	// Null removes all metadata of every meta, the same as Clear on the tag read.
	Mp4Tag? TagToSave (Mp4Box originalMoov)
	{
		if (Tag is not null && ReferenceEquals (Tag, _readTag))
			return Tag;

		var (fileTag, _) = ExtractMetadata (originalMoov);
		if (fileTag is null)
			return Tag?.Detached ();

		if (Tag is null) {
			fileTag.Clear ();
			return fileTag;
		}

		fileTag.ClearMappedFields ();
		fileTag.TakeValuesFrom (Tag);
		return fileTag;
	}

	/// <summary>
	/// The index of the top-level mdat right after moov when it holds only the old samples of the chapter
	/// tracks <paramref name="plan"/> replaces, or -1. An mdat whose old chapter samples share it with other
	/// media is kept, and those samples stay in the file unreferenced.
	/// </summary>
	static int FindReplacedChapterData (
		List<(Mp4Box Box, int Start, int Length)> boxes,
		int moovIndex,
		Mp4ChapterCodec.ChapterPlan? plan,
		ReadOnlySpan<byte> unreadTail)
	{
		var next = moovIndex + 1;
		if (plan is null || next >= boxes.Count || boxes[next].Box.Type != "mdat")
			return -1;

		// Unread bytes after the last box could hold a movie fragment, or an iloc (§8.11.3), that points into the
		// mdat. Fail closed.
		if (unreadTail.Length > 0 && (boxes[moovIndex].Box.FindChild ("mvex") is not null || unreadTail.IndexOf ("iloc"u8) >= 0))
			return -1;

		var (box, start, length) = boxes[next];
		var header = box.UsesExtendedSize ? Mp4Box.ExtendedHeaderSize : Mp4Box.HeaderSize;
		var moov = boxes[moovIndex].Box;
		var fragments = new List<(Mp4Box Moof, long Start)> ();
		foreach (var (top, at, _) in boxes) {
			if (top.Type == "moof")
				fragments.Add ((top, at));
		}

		var topLevel = boxes.ConvertAll (b => b.Box);
		return Mp4ChapterCodec.HoldsOnlyReplacedSamples (moov, fragments, plan, start, start + header, start + length, topLevel) ? next : -1;
	}

	/// <summary>
	/// The chapter rewrite for this save, or null when the list was not set or matches the file's.
	/// </summary>
	Mp4ChapterCodec.ChapterPlan? PlanChapters (List<(Mp4Box Box, int Start, int Length)> boxes, int moovIndex, ReadOnlySpan<byte> originalData)
	{
		if (!_chaptersChanged)
			return null;

		var moov = boxes[moovIndex].Box;
		var fragments = new List<(Mp4Box Moof, long Start)> ();
		Mp4Box? ftyp = null;
		foreach (var (box, start, _) in boxes) {
			if (box.Type == "moof")
				fragments.Add ((box, start));
			else if (box.Type == "ftyp")
				ftyp ??= box;
		}

		var duration = Mp4ChapterCodec.MovieDuration (moov);
		if (SameChapters (Mp4ChapterCodec.Read (moov, fragments, originalData, duration), _chapters))
			return null;

		// QuickTime evidence: the "qt  " major brand, or no ftyp at all. ISO 14496-12 §4.3 requires ftyp in
		// an ISO file; QuickTime movies may omit it (QTFF, "File type compatibility atom").
		var quickTime = ftyp is null || ExtractFileType (ftyp) == "qt";
		return Mp4ChapterCodec.Plan (moov, fragments, _chapters, duration, quickTime);
	}

	/// <summary>
	/// Rebuilds the moov box with updated metadata in udta/meta/ilst, and the chapter plan when there is one.
	/// </summary>
	BinaryData RebuildMoovWithMetadata (Mp4Box originalMoov, Mp4ChapterCodec.ChapterPlan? chapterPlan)
	{
		using var moovContent = new BinaryDataBuilder ();

		// A tag read from moov/meta is written back there, and udta is copied as it was.
		var inMovieMeta = MetaPlacement == Mp4MetaPlacement.Movie;
		var tag = TagToSave (originalMoov);
		var movieSource = !inMovieMeta && tag?.HasMovieMeta == true ? tag : null;
		// chpl lives in udta, so a chapter save rewrites udta even when the tag is in moov/meta;
		// then udta's own meta is copied as it was.
		var chpl = chapterPlan?.Chpl;
		var copyUdta = inMovieMeta && chpl is null;
		var newUdta = copyUdta ? BinaryData.Empty : BuildUdtaBox (originalMoov, inMovieMeta ? null : tag, chpl, keepMeta: inMovieMeta);
		var replacedMeta = false;
		var emittedChild = false;
		var placedUdta = false;
		foreach (var child in chapterPlan?.Children ?? originalMoov.Children) {
			if (child.Type == "udta") {
				if (copyUdta) {
					moovContent.Add (WrapBox ("udta", child.Data));
					emittedChild = true;
				} else if (!placedUdta && newUdta.Length > 0) {
					// The new udta takes the old one's place. Moving it behind a moov/meta hides it
					// from readers (mutagen) that stop at a meta they cannot parse.
					moovContent.Add (newUdta);
					placedUdta = true;
					emittedChild = true;
				}
				continue;
			}

			if (!replacedMeta && child.Type == "meta" && (inMovieMeta || movieSource is not null)) {
				replacedMeta = true;
				var (ilst, keys) = inMovieMeta ? RenderTag (child, tag) : (movieSource!.RenderMovieIlst (), movieSource.RenderMovieKeys ());
				var rebuilt = RebuildMeta (child, ilst, keys, tag?.DropsUnreadKeys == true);
				if (!rebuilt.IsEmpty)
					moovContent.Add (rebuilt);
				emittedChild = true;
				continue;
			}

			moovContent.Add (child.Render ());
			emittedChild = true;
		}

		// Child parsing stops on a bad box. Keep that unread span so a later trak survives.
		// ISO 14496-12 allows one udta in moov, so a replacement drops any udta still in the tail.
		var tail = originalMoov.UnparsedTail.Span;
		if (emittedChild) {
			if (newUdta.Length > 0 && !placedUdta)
				moovContent.Add (newUdta);
			if (tail.Length > 0)
				AppendMoovTail (moovContent, tail, newUdta.Length > 0, ReadOnlySpan<byte>.Empty);
		} else if (tail.Length > 0) {
			// Nothing before the bad child was readable, so do not emit udta ahead of mvhd.
			AppendMoovTail (moovContent, tail, newUdta.Length > 0, newUdta.Span);
		} else if (newUdta.Length > 0) {
			moovContent.Add (newUdta);
		}

		// Build the moov box header
		using var moovBuilder = new BinaryDataBuilder ();
		var moovContentData = moovContent.ToBinaryData ();
		var totalSize = Mp4Box.HeaderSize + moovContentData.Length;

		moovBuilder.AddUInt32BE ((uint)totalSize);
		moovBuilder.AddStringLatin1 ("moov");
		moovBuilder.Add (moovContentData);

		return moovBuilder.ToBinaryData ();
	}

	/// <summary>
	/// Copies unread moov children. When <paramref name="omitUdta"/> is set, top-level udta boxes are
	/// dropped. <paramref name="replacementUdta"/> is inserted after mvhd, or in the old udta's place
	/// when mvhd is not in the tail.
	/// </summary>
	static void AppendMoovTail (BinaryDataBuilder dest, ReadOnlySpan<byte> tail, bool omitUdta, ReadOnlySpan<byte> replacementUdta)
	{
		var place = replacementUdta.Length > 0;
		var hasMvhd = place && TailHasBoxType (tail, "mvhd"u8);
		var inserted = false;
		var offset = 0;
		while (offset + 8 <= tail.Length) {
			if (!TryMeasureBox (tail.Slice (offset), out var size, out _))
				break;
			if (size <= 0 || size > tail.Length - offset)
				break;

			var isUdta = size >= 8 && tail.Slice (offset + 4, 4).SequenceEqual ("udta"u8);
			if (omitUdta && isUdta) {
				if (place && !hasMvhd && !inserted) {
					dest.Add (replacementUdta);
					inserted = true;
				}
				offset += size;
				continue;
			}

			var isMvhd = size >= 8 && tail.Slice (offset + 4, 4).SequenceEqual ("mvhd"u8);
			dest.Add (tail.Slice (offset, size));
			offset += size;
			if (place && hasMvhd && isMvhd && !inserted) {
				dest.Add (replacementUdta);
				inserted = true;
			}
		}

		if (offset < tail.Length)
			dest.Add (tail.Slice (offset));
		if (place && !inserted)
			dest.Add (replacementUdta);
	}

	static bool TailHasBoxType (ReadOnlySpan<byte> tail, ReadOnlySpan<byte> type)
	{
		var offset = 0;
		while (offset + 8 <= tail.Length) {
			if (!TryMeasureBox (tail.Slice (offset), out var size, out _))
				break;
			if (size <= 0 || size > tail.Length - offset)
				break;
			if (size >= 8 && tail.Slice (offset + 4, 4).SequenceEqual (type))
				return true;
			offset += size;
		}

		return false;
	}

	/// <summary>
	/// Builds moov/udta. An existing udta keeps every child except meta, and meta keeps
	/// every child except ilst. Chapters and the meta version/flags survive a tag edit.
	/// A non-null <paramref name="chpl"/> replaces the first chpl and drops the rest; empty removes it.
	/// With <paramref name="keepMeta"/> (the tag is saved to moov/meta), udta's meta is copied as it was.
	/// </summary>
	static BinaryData BuildUdtaBox (Mp4Box originalMoov, Mp4Tag? tag, BinaryData? chpl, bool keepMeta = false)
	{
		var original = originalMoov.FindChild ("udta");
		var (ilstContent, keysContent) = RenderTag (original?.FindChild ("meta"), tag);
		if (original is null) {
			var newChpl = chpl ?? BinaryData.Empty;
			if (ilstContent.IsEmpty && newChpl.IsEmpty)
				return BinaryData.Empty;

			using var body = new BinaryDataBuilder ();
			if (!ilstContent.IsEmpty)
				body.Add (FreshMeta (ilstContent));
			body.Add (newChpl);
			return WrapBox ("udta", body.ToBinaryData ());
		}

		return RebuildUdta (original, ilstContent, keysContent, tag?.DropsUnreadKeys == true, chpl, keepMeta);
	}

	/// <summary>
	/// Renders the ilst, and the keys when the target meta's handler is "mdta". A tag that was not
	/// read from such a meta takes its keys, so no iTunes atom is written into it.
	/// </summary>
	static (BinaryData Ilst, BinaryData? Keys) RenderTag (Mp4Box? targetMeta, Mp4Tag? tag)
	{
		if (tag is null)
			return (BinaryData.Empty, null);

		if (tag.QuickTimeKeys is null && targetMeta is not null && Mp4QuickTimeKeys.Read (targetMeta) is { } keys)
			tag.UseQuickTimeKeys (keys);

		var ilst = tag.Render ();
		return (ilst, tag.QuickTimeKeys?.Render ());
	}

	static BinaryData RebuildUdta (Mp4Box udta, BinaryData ilstContent, BinaryData? keysContent, bool dropUnreadKeys, BinaryData? chpl, bool keepMeta)
	{
		using var content = new BinaryDataBuilder ();
		var payload = udta.Data.Span;
		var offset = 0;
		var replacedMeta = false;
		var placedChpl = false;
		while (offset + 8 <= payload.Length) {
			if (!TryMeasureBox (payload.Slice (offset), out var size, out _))
				break;
			if (size <= 0 || size > payload.Length - offset)
				break;

			// A size below the header stops the parser. Put the new meta ahead of it,
			// matching moov, so the following read can see the tag.
			if (size < 8) {
				if (!replacedMeta && !ilstContent.IsEmpty) {
					content.Add (FreshMeta (ilstContent));
					replacedMeta = true;
				}
				content.Add (payload.Slice (offset, size));
				offset += size;
				continue;
			}

			if (chpl is not null && payload.Slice (offset + 4, 4).SequenceEqual ("chpl"u8)) {
				if (!placedChpl)
					content.Add (chpl.Value);
				placedChpl = true;
				offset += size;
				continue;
			}

			var isMeta = payload.Slice (offset + 4, 4).SequenceEqual ("meta"u8);
			if (isMeta && keepMeta) {
				// The tag lives in moov/meta; only chpl changes here.
				content.Add (payload.Slice (offset, size));
			} else if (isMeta && !replacedMeta) {
				var meta = udta.FindChild ("meta");
				if (meta is not null) {
					var rebuilt = RebuildMeta (meta, ilstContent, keysContent, dropUnreadKeys);
					if (!rebuilt.IsEmpty)
						content.Add (rebuilt);
				} else if (!ilstContent.IsEmpty) {
					content.Add (FreshMeta (ilstContent));
				} else {
					content.Add (payload.Slice (offset, size));
				}
				replacedMeta = true;
			} else if (isMeta && udta.FindChild ("meta") is null) {
				// The original sits after the box that stopped parsing. It is not readable.
			} else {
				content.Add (payload.Slice (offset, size));
			}

			offset += size;
		}

		if (offset < payload.Length)
			content.Add (payload.Slice (offset));
		if (!replacedMeta && !ilstContent.IsEmpty)
			content.Add (FreshMeta (ilstContent));
		if (chpl is not null && !placedChpl)
			content.Add (chpl.Value);
		if (content.Length == 0)
			return BinaryData.Empty;

		return WrapBox ("udta", content.ToBinaryData ());
	}

	// A keys atom the tag could not read is written back as it was, unless the tag was cleared:
	// then no item is left that could use it, and a damaged one would keep ffmpeg from opening
	// the file (QTFF Metadata Item Keys atom: each key size counts its size and namespace).
	static BinaryData RebuildMeta (Mp4Box meta, BinaryData ilstContent, BinaryData? keysContent, bool dropUnreadKeys)
	{
		var children = new List<Mp4Box> ();
		var sawHandler = false;
		var placedList = false;
		var placedKeys = keysContent is null;
		foreach (var child in meta.Children) {
			if (child.Type == "hdlr")
				sawHandler = true;
			if (child.Type == "keys" && keysContent is null && dropUnreadKeys)
				continue;
			if (child.Type == "keys" && keysContent is not null) {
				if (!placedKeys) {
					children.Add (new Mp4Box ("keys", keysContent.Value));
					placedKeys = true;
				}
				continue;
			}

			// The keys come before the item list.
			if (child.Type == "ilst" && !placedKeys) {
				children.Add (new Mp4Box ("keys", keysContent!.Value));
				placedKeys = true;
			}
			if (child.Type == "ilst") {
				if (!placedList && !ilstContent.IsEmpty) {
					children.Add (new Mp4Box ("ilst", ilstContent));
					placedList = true;
				}
				continue;
			}

			// Other children (hdlr, keys, free padding) go back as their original bytes. A parsed
			// container such as free would otherwise be rebuilt from whatever its padding parsed as.
			children.Add (child.IsContainer ? new Mp4Box (child.Type, child.Data, usesExtendedSize: child.UsesExtendedSize) : child);
		}

		if (!sawHandler && !ilstContent.IsEmpty)
			children.Insert (0, HandlerBox ());
		if (!placedKeys)
			children.Add (new Mp4Box ("keys", keysContent!.Value));
		if (!placedList && !ilstContent.IsEmpty)
			children.Add (new Mp4Box ("ilst", ilstContent));
		if (children.Count == 0 && meta.UnparsedTail.Length == 0)
			return BinaryData.Empty;

		var rebuilt = new Mp4Box (
			"meta",
			meta.Data,
			children,
			meta.UsesExtendedSize,
			meta.UnparsedTail,
			meta.HeaderPrefix);
		return rebuilt.Render ();
	}

	static BinaryData FreshMeta (BinaryData ilstContent)
	{
		var prefix = new byte[4];
		var meta = new Mp4Box (
			"meta",
			BinaryData.Empty,
			[HandlerBox (), new Mp4Box ("ilst", ilstContent)],
			headerPrefix: new BinaryData (prefix));
		return meta.Render ();
	}

	static Mp4Box HandlerBox ()
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		body.AddStringLatin1 ("mdir");
		body.AddStringLatin1 ("appl");
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		body.Add ((byte)0);
		return new Mp4Box ("hdlr", body.ToBinaryData ());
	}

	static BinaryData WrapBox (string type, BinaryData body)
	{
		using var builder = new BinaryDataBuilder ();
		builder.AddUInt32BE ((uint)(Mp4Box.HeaderSize + body.Length));
		builder.AddStringLatin1 (type);
		builder.Add (body);
		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var rendered = Render (originalData, out var error);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure (error ?? "Failed to render MP4 file");

		fileSystem ??= _sourceFileSystem;
		return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var rendered = Render (originalData.Span, out var error);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure (error ?? "Failed to render MP4 file"));

		fileSystem ??= _sourceFileSystem;
		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// This convenience method re-reads the original file data from <see cref="SourcePath"/>
	/// and saves to the specified path. Requires that the file was read using
	/// <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return SaveToFile (path, readResult.Data!.Value.Span, fs);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return await SaveToFileAsync (path, readResult.Data!.Value, fs, cancellationToken)
			.ConfigureAwait (false);
	}

	/// <summary>
	/// Asynchronously saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}
}

/// <summary>
/// Represents the result of reading an <see cref="Mp4File"/> from binary data.
/// </summary>
public readonly struct Mp4FileReadResult : IEquatable<Mp4FileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public Mp4File? File { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	Mp4FileReadResult (Mp4File? file, string? error, int bytesConsumed)
	{
		File = file;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="file">The parsed file.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static Mp4FileReadResult Success (Mp4File file, int bytesConsumed) =>
		new (file, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static Mp4FileReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (Mp4FileReadResult other) =>
		ReferenceEquals (File, other.File) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is Mp4FileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (Mp4FileReadResult left, Mp4FileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (Mp4FileReadResult left, Mp4FileReadResult right) =>
		!left.Equals (right);
}
