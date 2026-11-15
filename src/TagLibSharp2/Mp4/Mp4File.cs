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
	/// It is null when the file was read from binary data using <see cref="Read"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	Mp4File (Mp4Tag? tag, AudioProperties properties, Mp4AudioCodec audioCodec)
	{
		Tag = tag;
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
	public void RemovePictures () => Tag?.Clear ();

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

		var key = $"{Mp4AtomMapping.FreeformAtom}:{mean}:{name}";
		var atoms = Tag.GetAtoms ();
		if (!atoms.TryGetValue (key, out var dataAtoms) || dataAtoms.Count == 0)
			return null;

		return dataAtoms[0].ToUtf8String ();
	}

	/// <summary>
	/// Sets a freeform (----) metadata value.
	/// </summary>
	/// <param name="mean">The mean (namespace) identifier.</param>
	/// <param name="name">The name identifier.</param>
	/// <param name="value">The value to set, or null to remove.</param>
	public void SetFreeformTag (string mean, string name, string? value)
	{
		var tag = EnsureTag ();
		var key = $"{Mp4AtomMapping.FreeformAtom}:{mean}:{name}";

		if (string.IsNullOrEmpty (value)) {
			tag.SetAtoms (key, null!);
			return;
		}

		var dataAtom = new Mp4DataAtom (Mp4AtomMapping.TypeUtf8, BinaryData.FromStringUtf8 (value!));
		tag.SetAtoms (key, new List<Mp4DataAtom> { dataAtom });
	}

	Mp4Tag EnsureTag ()
	{
		Tag ??= new Mp4Tag ();
		return Tag;
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		Tag = null;
		_sourceFileSystem = null;
		SourcePath = null;
		_disposed = true;
	}

	/// <summary>
	/// Attempts to read an MP4 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static Mp4FileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < Mp4Box.HeaderSize)
			return Mp4FileReadResult.Failure ("Invalid MP4 file: data too short");

		// Parse all top-level boxes
		var boxes = new List<Mp4Box> ();
		var offset = 0;
		while (offset < data.Length) {
			var remaining = data[offset..];
			if (remaining.Length < Mp4Box.HeaderSize)
				break;

			var result = Mp4Box.Parse (remaining);
			if (!result.IsSuccess)
				break;

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

		// Extract iTunes metadata from moov/udta/meta/ilst
		var tag = ExtractMetadata (moov);

		var file = new Mp4File (tag, properties, audioCodec) {
			FileType = fileType
		};

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
					"mp4a" => Mp4AudioCodec.Aac,
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
		var audioDuration = TimeSpan.FromSeconds (durationSeconds);

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

	static Mp4Tag? ExtractMetadata (Mp4Box moov)
	{
		// Navigate: moov -> udta -> meta -> ilst
		var udta = moov.FindChild ("udta");
		if (udta is null)
			return null;

		var meta = udta.FindChild ("meta");
		if (meta is null)
			return null;

		// meta is a FullBox, so children start after version(1) + flags(3) = 4 bytes
		// We need to re-parse meta's data as children, skipping the first 4 bytes
		if (meta.Data.Length < 4)
			return null;

		// Find ilst within meta's children (already parsed by Mp4BoxParser)
		var ilst = meta.FindChild ("ilst");
		if (ilst is null)
			return null;

		return ParseIlst (ilst);
	}

	static Mp4Tag ParseIlst (Mp4Box ilst)
	{
		var tag = new Mp4Tag ();

		// Each child of ilst is a metadata atom (©nam, ©ART, etc.)
		foreach (var atomBox in ilst.Children) {
			var atomId = atomBox.Type;

			// Handle cover art specially
			if (atomId == Mp4AtomMapping.CoverArt) {
				ParseCoverArt (atomBox, tag);
				continue;
			}

			// Handle freeform atoms (----)
			if (atomId == Mp4AtomMapping.FreeformAtom) {
				ParseFreeformAtom (atomBox, tag);
				continue;
			}

			// Standard atoms contain data sub-atoms
			var dataAtoms = new List<Mp4DataAtom> ();
			foreach (var child in atomBox.Children) {
				if (child.Type == "data" && child.Data.Length >= 8) {
					var dataAtom = Mp4DataAtom.Parse (child.Data.Span);
					dataAtoms.Add (dataAtom);
				}
			}

			if (dataAtoms.Count > 0)
				tag.SetAtoms (atomId, dataAtoms);
		}

		return tag;
	}

	static void ParseCoverArt (Mp4Box covrBox, Mp4Tag tag)
	{
		var pictures = new List<Mp4Picture> ();

		foreach (var child in covrBox.Children) {
			if (child.Type == "data" && child.Data.Length >= 8) {
				var dataAtom = Mp4DataAtom.Parse (child.Data.Span);
				var picture = Mp4Picture.FromDataAtom (dataAtom);
				pictures.Add (picture);
			}
		}

		if (pictures.Count > 0)
			tag.Pictures = [.. pictures];
	}

	static void ParseFreeformAtom (Mp4Box freeformBox, Mp4Tag tag)
	{
		// Freeform atoms have mean, name, and data children
		string? mean = null;
		string? name = null;
		var dataAtoms = new List<Mp4DataAtom> ();

		foreach (var child in freeformBox.Children) {
			if (child.Type == "mean" && child.Data.Length >= 4) {
				// Skip version(1) + flags(3)
				mean = child.Data.Slice (4).ToStringUtf8 ();
			} else if (child.Type == "name" && child.Data.Length >= 4) {
				// Skip version(1) + flags(3)
				name = child.Data.Slice (4).ToStringUtf8 ();
			} else if (child.Type == "data" && child.Data.Length >= 8) {
				var dataAtom = Mp4DataAtom.Parse (child.Data.Span);
				dataAtoms.Add (dataAtom);
			}
		}

		if (!string.IsNullOrEmpty (mean) && !string.IsNullOrEmpty (name) && dataAtoms.Count > 0) {
			var key = $"{Mp4AtomMapping.FreeformAtom}:{mean}:{name}";
			tag.SetAtoms (key, dataAtoms);
		}
	}

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
	/// </remarks>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
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
		var newMoov = RebuildMoovWithMetadata (boxes[moovIndex].Box);
		var delta = newMoov.Length - oldMoovLength;
		var oldMoovEnd = oldMoovStart + oldMoovLength;
		// Offsets are absolute file positions. Anything at or after the old moov moves with it,
		// including media that is not stored in a box named mdat. Entries inside moov stay put.
		if (delta != 0)
			newMoov = AdjustChunkOffsets (newMoov, oldMoovEnd, delta);

		// Reassemble the file
		using var builder = new BinaryDataBuilder ();

		for (int i = 0; i < boxes.Count; i++) {
			if (i == moovIndex) {
				builder.Add (newMoov);
			} else {
				var (_, start, length) = boxes[i];
				builder.Add (new BinaryData (originalData.Slice (start, length)));
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
		} else if (type.SequenceEqual ("trak"u8))
			PatchTrakOffsets (payload, threshold, delta);
		else if (adjust.InTrak && type.SequenceEqual ("stbl"u8))
			PatchStblOffsets (payload, threshold, delta, adjust);
		else if (IsOffsetContainer (type))
			WalkBoxes (payload, threshold, delta, adjust);
	}

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
		|| type.SequenceEqual ("edts"u8);

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

	/// <summary>
	/// Rebuilds the moov box with updated metadata in udta/meta/ilst.
	/// </summary>
	BinaryData RebuildMoovWithMetadata (Mp4Box originalMoov)
	{
		using var moovContent = new BinaryDataBuilder ();

		var newUdta = BuildUdtaBox ();
		var emittedChild = false;
		foreach (var child in originalMoov.Children) {
			if (child.Type == "udta")
				continue;
			moovContent.Add (child.Render ());
			emittedChild = true;
		}

		// Child parsing stops on a bad box. Keep that unread span so a later trak survives.
		// ISO 14496-12 allows one udta in moov, so a replacement drops any udta still in the tail.
		var tail = originalMoov.UnparsedTail.Span;
		if (emittedChild) {
			if (newUdta.Length > 0)
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
	/// Builds the udta box containing meta and ilst.
	/// </summary>
	BinaryData BuildUdtaBox ()
	{
		if (Tag is null)
			return BinaryData.Empty;

		// Build ilst content
		var ilstContent = Tag.Render ();
		if (ilstContent.IsEmpty)
			return BinaryData.Empty;

		// Build ilst box
		using var ilstBuilder = new BinaryDataBuilder ();
		ilstBuilder.AddUInt32BE ((uint)(Mp4Box.HeaderSize + ilstContent.Length));
		ilstBuilder.AddStringLatin1 ("ilst");
		ilstBuilder.Add (ilstContent);
		var ilstBox = ilstBuilder.ToBinaryData ();

		// Build hdlr box for meta (handler type: mdir/appl)
		using var hdlrContent = new BinaryDataBuilder ();
		hdlrContent.AddUInt32BE (0); // version + flags
		hdlrContent.AddUInt32BE (0); // pre_defined
		hdlrContent.AddStringLatin1 ("mdir"); // handler_type
		hdlrContent.AddStringLatin1 ("appl"); // manufacturer
		hdlrContent.AddUInt32BE (0); // reserved
		hdlrContent.AddUInt32BE (0); // reserved
		hdlrContent.Add ((byte)0); // null-terminated name

		using var hdlrBuilder = new BinaryDataBuilder ();
		var hdlrContentData = hdlrContent.ToBinaryData ();
		hdlrBuilder.AddUInt32BE ((uint)(Mp4Box.HeaderSize + hdlrContentData.Length));
		hdlrBuilder.AddStringLatin1 ("hdlr");
		hdlrBuilder.Add (hdlrContentData);
		var hdlrBox = hdlrBuilder.ToBinaryData ();

		// Build meta box (FullBox: version + flags prefix)
		using var metaContent = new BinaryDataBuilder ();
		metaContent.AddUInt32BE (0); // version(1) + flags(3) = 0
		metaContent.Add (hdlrBox);
		metaContent.Add (ilstBox);

		using var metaBuilder = new BinaryDataBuilder ();
		var metaContentData = metaContent.ToBinaryData ();
		metaBuilder.AddUInt32BE ((uint)(Mp4Box.HeaderSize + metaContentData.Length));
		metaBuilder.AddStringLatin1 ("meta");
		metaBuilder.Add (metaContentData);
		var metaBox = metaBuilder.ToBinaryData ();

		// Build udta box
		using var udtaBuilder = new BinaryDataBuilder ();
		udtaBuilder.AddUInt32BE ((uint)(Mp4Box.HeaderSize + metaBox.Length));
		udtaBuilder.AddStringLatin1 ("udta");
		udtaBuilder.Add (metaBox);

		return udtaBuilder.ToBinaryData ();
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
		var rendered = Render (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render MP4 file");

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
		var rendered = Render (originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("Failed to render MP4 file"));

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

		return SaveToFile (path, readResult.Data!.Value.Span, fileSystem);
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

		return await SaveToFileAsync (path, readResult.Data!.Value, fileSystem, cancellationToken)
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
