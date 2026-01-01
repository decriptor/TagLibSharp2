// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
public sealed class Mp4File
{
	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	Mp4File(Mp4Tag? tag, AudioProperties properties, Mp4AudioCodec audioCodec)
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
	public TimeSpan? Duration => Properties?.Duration;

	/// <summary>
	/// Gets or sets the title.
	/// </summary>
	public string? Title {
		get => Tag?.Title;
		set => EnsureTag().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist.
	/// </summary>
	public string? Artist {
		get => Tag?.Artist;
		set => EnsureTag().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album.
	/// </summary>
	public string? Album {
		get => Tag?.Album;
		set => EnsureTag().Album = value;
	}

	/// <summary>
	/// Gets or sets the year.
	/// </summary>
	public string? Year {
		get => Tag?.Year;
		set => EnsureTag().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre.
	/// </summary>
	public string? Genre {
		get => Tag?.Genre;
		set => EnsureTag().Genre = value;
	}

	/// <summary>
	/// Gets or sets the comment.
	/// </summary>
	public string? Comment {
		get => Tag?.Comment;
		set => EnsureTag().Comment = value;
	}

	/// <summary>
	/// Gets or sets the album artist.
	/// </summary>
	public string? AlbumArtist {
		get => Tag?.AlbumArtist;
		set => EnsureTag().AlbumArtist = value;
	}

	/// <summary>
	/// Gets or sets the composer.
	/// </summary>
	public string? Composer {
		get => Tag?.Composer;
		set => EnsureTag().Composer = value;
	}

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public uint? Track {
		get => Tag?.Track;
		set => EnsureTag().Track = value;
	}

	/// <summary>
	/// Gets or sets the total track count.
	/// </summary>
	public uint? TrackCount {
		get => Tag?.TotalTracks;
		set => EnsureTag().TotalTracks = value;
	}

	/// <summary>
	/// Gets or sets the disc number.
	/// </summary>
	public uint? DiscNumber {
		get => Tag?.DiscNumber;
		set => EnsureTag().DiscNumber = value;
	}

	/// <summary>
	/// Gets or sets the total disc count.
	/// </summary>
	public uint? DiscCount {
		get => Tag?.TotalDiscs;
		set => EnsureTag().TotalDiscs = value;
	}

	// Sort order properties

	/// <summary>
	/// Gets or sets the album sort order.
	/// </summary>
	public string? AlbumSort {
		get => Tag?.AlbumSort;
		set => EnsureTag().AlbumSort = value;
	}

	/// <summary>
	/// Gets or sets the artist sort order.
	/// </summary>
	public string? ArtistSort {
		get => Tag?.ArtistSort;
		set => EnsureTag().ArtistSort = value;
	}

	/// <summary>
	/// Gets or sets the title sort order.
	/// </summary>
	public string? TitleSort {
		get => Tag?.TitleSort;
		set => EnsureTag().TitleSort = value;
	}

	/// <summary>
	/// Gets or sets the album artist sort order.
	/// </summary>
	public string? AlbumArtistSort {
		get => Tag?.AlbumArtistSort;
		set => EnsureTag().AlbumArtistSort = value;
	}

	/// <summary>
	/// Gets or sets the composer sort order.
	/// </summary>
	public string? ComposerSort {
		get => Tag?.ComposerSort;
		set => EnsureTag().ComposerSort = value;
	}

	// Classical music properties

	/// <summary>
	/// Gets or sets the work name (for classical music).
	/// </summary>
	public string? Work {
		get => Tag?.Work;
		set => EnsureTag().Work = value;
	}

	/// <summary>
	/// Gets or sets the movement name (for classical music).
	/// </summary>
	public string? Movement {
		get => Tag?.Movement;
		set => EnsureTag().Movement = value;
	}

	/// <summary>
	/// Gets or sets the movement number (for classical music).
	/// </summary>
	public uint? MovementNumber {
		get => Tag?.MovementNumber;
		set => EnsureTag().MovementNumber = value;
	}

	/// <summary>
	/// Gets or sets the total movement count (for classical music).
	/// </summary>
	public uint? MovementTotal {
		get => Tag?.MovementTotal;
		set => EnsureTag().MovementTotal = value;
	}

	// Additional metadata properties

	/// <summary>
	/// Gets or sets whether gapless playback is enabled.
	/// </summary>
	public bool IsGapless {
		get => Tag?.IsGapless ?? false;
		set => EnsureTag().IsGapless = value;
	}

	/// <summary>
	/// Gets or sets the ISRC (International Standard Recording Code).
	/// </summary>
	public string? Isrc {
		get => Tag?.Isrc;
		set => EnsureTag().Isrc = value;
	}

	/// <summary>
	/// Gets or sets the conductor.
	/// </summary>
	public string? Conductor {
		get => Tag?.Conductor;
		set => EnsureTag().Conductor = value;
	}

	/// <summary>
	/// Gets or sets the original release date.
	/// </summary>
	public string? OriginalReleaseDate {
		get => Tag?.OriginalReleaseDate;
		set => EnsureTag().OriginalReleaseDate = value;
	}

	// ReplayGain properties

	/// <summary>
	/// Gets or sets the ReplayGain track gain value (e.g., "-6.50 dB").
	/// </summary>
	public string? ReplayGainTrackGain {
		get => Tag?.ReplayGainTrackGain;
		set => EnsureTag().ReplayGainTrackGain = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain track peak value (e.g., "0.988547").
	/// </summary>
	public string? ReplayGainTrackPeak {
		get => Tag?.ReplayGainTrackPeak;
		set => EnsureTag().ReplayGainTrackPeak = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain album gain value (e.g., "-5.20 dB").
	/// </summary>
	public string? ReplayGainAlbumGain {
		get => Tag?.ReplayGainAlbumGain;
		set => EnsureTag().ReplayGainAlbumGain = value;
	}

	/// <summary>
	/// Gets or sets the ReplayGain album peak value (e.g., "0.995123").
	/// </summary>
	public string? ReplayGainAlbumPeak {
		get => Tag?.ReplayGainAlbumPeak;
		set => EnsureTag().ReplayGainAlbumPeak = value;
	}

	// R128 loudness properties

	/// <summary>
	/// Gets or sets the R128 track gain value (Q7.8 fixed-point, e.g., "-512" for -2 dB).
	/// </summary>
	public string? R128TrackGain {
		get => Tag?.R128TrackGain;
		set => EnsureTag().R128TrackGain = value;
	}

	/// <summary>
	/// Gets or sets the R128 album gain value (Q7.8 fixed-point, e.g., "256" for +1 dB).
	/// </summary>
	public string? R128AlbumGain {
		get => Tag?.R128AlbumGain;
		set => EnsureTag().R128AlbumGain = value;
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
		set => EnsureTag().R128TrackGainDb = value;
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
		set => EnsureTag().R128AlbumGainDb = value;
	}

	// AcoustID properties

	/// <summary>
	/// Gets or sets the AcoustID identifier.
	/// </summary>
	public string? AcoustIdId {
		get => Tag?.AcoustIdId;
		set => EnsureTag().AcoustIdId = value;
	}

	/// <summary>
	/// Gets or sets the AcoustID audio fingerprint.
	/// </summary>
	public string? AcoustIdFingerprint {
		get => Tag?.AcoustIdFingerprint;
		set => EnsureTag().AcoustIdFingerprint = value;
	}

	// Extended MusicBrainz properties

	/// <summary>
	/// Gets or sets the MusicBrainz Recording ID.
	/// </summary>
	public string? MusicBrainzRecordingId {
		get => Tag?.MusicBrainzRecordingId;
		set => EnsureTag().MusicBrainzRecordingId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Disc ID.
	/// </summary>
	public string? MusicBrainzDiscId {
		get => Tag?.MusicBrainzDiscId;
		set => EnsureTag().MusicBrainzDiscId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz release status (e.g., "official", "bootleg").
	/// </summary>
	public string? MusicBrainzReleaseStatus {
		get => Tag?.MusicBrainzReleaseStatus;
		set => EnsureTag().MusicBrainzReleaseStatus = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz release type (e.g., "album", "single", "ep").
	/// </summary>
	public string? MusicBrainzReleaseType {
		get => Tag?.MusicBrainzReleaseType;
		set => EnsureTag().MusicBrainzReleaseType = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz release country (ISO 3166-1 alpha-2).
	/// </summary>
	public string? MusicBrainzReleaseCountry {
		get => Tag?.MusicBrainzReleaseCountry;
		set => EnsureTag().MusicBrainzReleaseCountry = value;
	}

	// DJ and remix properties

	/// <summary>
	/// Gets or sets the initial musical key (e.g., "Am", "F#m", "Cmaj").
	/// </summary>
	public string? InitialKey {
		get => Tag?.InitialKey;
		set => EnsureTag().InitialKey = value;
	}

	/// <summary>
	/// Gets or sets the remixer or modifier of the track.
	/// </summary>
	public string? Remixer {
		get => Tag?.Remixer;
		set => EnsureTag().Remixer = value;
	}

	/// <summary>
	/// Gets or sets the mood of the track (e.g., "Energetic", "Melancholic").
	/// </summary>
	public string? Mood {
		get => Tag?.Mood;
		set => EnsureTag().Mood = value;
	}

	/// <summary>
	/// Gets or sets the subtitle (e.g., "Radio Edit", "Live Version").
	/// </summary>
	public string? Subtitle {
		get => Tag?.Subtitle;
		set => EnsureTag().Subtitle = value;
	}

	// Collector properties

	/// <summary>
	/// Gets or sets the barcode (UPC/EAN) of the release.
	/// </summary>
	public string? Barcode {
		get => Tag?.Barcode;
		set => EnsureTag().Barcode = value;
	}

	/// <summary>
	/// Gets or sets the label's catalog number.
	/// </summary>
	public string? CatalogNumber {
		get => Tag?.CatalogNumber;
		set => EnsureTag().CatalogNumber = value;
	}

	/// <summary>
	/// Gets or sets the Amazon Standard Identification Number (ASIN).
	/// </summary>
	public string? AmazonId {
		get => Tag?.AmazonId;
		set => EnsureTag().AmazonId = value;
	}

	// Library management properties

	/// <summary>
	/// Gets or sets the date/time when the file was tagged.
	/// </summary>
	public string? DateTagged {
		get => Tag?.DateTagged;
		set => EnsureTag().DateTagged = value;
	}

	/// <summary>
	/// Gets or sets the language of the audio content (ISO 639-2).
	/// </summary>
	public string? Language {
		get => Tag?.Language;
		set => EnsureTag().Language = value;
	}

	/// <summary>
	/// Gets or sets the original media type (e.g., "CD", "Vinyl", "Digital").
	/// </summary>
	public string? MediaType {
		get => Tag?.MediaType;
		set => EnsureTag().MediaType = value;
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
	public void AddPicture(IPicture picture)
	{
		var tag = EnsureTag();
		var current = tag.Pictures;
		var newList = new IPicture[current.Length + 1];
		current.CopyTo(newList, 0);
		newList[^1] = picture;
		tag.Pictures = newList;
	}

	/// <summary>
	/// Removes all pictures from this file.
	/// </summary>
	public void RemovePictures() => Tag?.Clear();

	/// <summary>
	/// Clears all metadata from this file.
	/// </summary>
	public void ClearAllMetadata() => Tag?.Clear();

	/// <summary>
	/// Gets a freeform (----) metadata value.
	/// </summary>
	/// <param name="mean">The mean (namespace) identifier.</param>
	/// <param name="name">The name identifier.</param>
	/// <returns>The value, or null if not present.</returns>
	public string? GetFreeformTag(string mean, string name)
	{
		if (Tag is null)
			return null;

		var key = $"{Mp4AtomMapping.FreeformAtom}:{mean}:{name}";
		var atoms = Tag.GetAtoms();
		if (!atoms.TryGetValue(key, out var dataAtoms) || dataAtoms.Count == 0)
			return null;

		return dataAtoms[0].ToUtf8String();
	}

	/// <summary>
	/// Sets a freeform (----) metadata value.
	/// </summary>
	/// <param name="mean">The mean (namespace) identifier.</param>
	/// <param name="name">The name identifier.</param>
	/// <param name="value">The value to set, or null to remove.</param>
	public void SetFreeformTag(string mean, string name, string? value)
	{
		var tag = EnsureTag();
		var key = $"{Mp4AtomMapping.FreeformAtom}:{mean}:{name}";

		if (string.IsNullOrEmpty(value)) {
			tag.SetAtoms(key, null!);
			return;
		}

		var dataAtom = new Mp4DataAtom(Mp4AtomMapping.TypeUtf8, BinaryData.FromStringUtf8(value!));
		tag.SetAtoms(key, new List<Mp4DataAtom> { dataAtom });
	}

	Mp4Tag EnsureTag()
	{
		Tag ??= new Mp4Tag();
		return Tag;
	}

	/// <summary>
	/// Attempts to read an MP4 file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static Mp4FileReadResult Read(ReadOnlySpan<byte> data)
	{
		if (data.Length < Mp4Box.HeaderSize)
			return Mp4FileReadResult.Failure("File too small to contain valid MP4 boxes");

		// Parse all top-level boxes
		var boxes = new List<Mp4Box>();
		var offset = 0;
		while (offset < data.Length) {
			var remaining = data[offset..];
			if (remaining.Length < Mp4Box.HeaderSize)
				break;

			var result = Mp4Box.Parse(remaining);
			if (!result.IsSuccess)
				break;

			boxes.Add(result.Box!);
			offset += result.BytesConsumed;
		}

		// Find key boxes
		Mp4Box? moov = null;
		var fileType = string.Empty;

		for (int i = 0; i < boxes.Count; i++) {
			var box = boxes[i];
			if (box.Type == "ftyp")
				fileType = ExtractFileType(box);
			else if (box.Type == "moov")
				moov = box;
		}

		if (moov is null)
			return Mp4FileReadResult.Failure("Missing moov box - not a valid MP4 file");

		// Extract audio properties from moov/trak/mdia
		var (properties, audioCodec) = ExtractAudioProperties(moov);

		// Extract iTunes metadata from moov/udta/meta/ilst
		var tag = ExtractMetadata(moov);

		var file = new Mp4File(tag, properties, audioCodec) {
			FileType = fileType
		};

		return Mp4FileReadResult.Success(file, offset);
	}

	static string ExtractFileType(Mp4Box ftypBox)
	{
		if (ftypBox.Data.Length < 4)
			return string.Empty;
		return ftypBox.Data.Slice(0, 4).ToStringLatin1().TrimEnd();
	}

	static (AudioProperties, Mp4AudioCodec) ExtractAudioProperties(Mp4Box moov)
	{
		// First try to get duration from mvhd (movie header) for overall duration
		uint movieTimescale = 0;
		ulong movieDuration = 0;
		var mvhd = moov.FindChild("mvhd");
		if (mvhd is not null && mvhd.Data.Length >= 20) {
			var mvhdData = mvhd.Data;
			var mvhdVersion = mvhdData[0];
			if (mvhdVersion == 0) {
				// Version 0: 32-bit values
				// version(1) + flags(3) + creation(4) + modification(4) + timescale(4) + duration(4)
				movieTimescale = mvhdData.ToUInt32BE(12);
				movieDuration = mvhdData.ToUInt32BE(16);
			} else {
				// Version 1: 64-bit values
				if (mvhdData.Length >= 32) {
					movieTimescale = mvhdData.ToUInt32BE(20);
					movieDuration = mvhdData.ToUInt64BE(24);
				}
			}
		}

		// Navigate to moov/trak/mdia for track info
		var trak = moov.FindChild("trak");
		if (trak is null)
			return (AudioProperties.Empty, Mp4AudioCodec.Unknown);

		var mdia = trak.FindChild("mdia");
		if (mdia is null)
			return (AudioProperties.Empty, Mp4AudioCodec.Unknown);

		// Parse mdhd for track-level timescale (used for sample rate if not found in stsd)
		uint trackTimescale = movieTimescale;
		var mdhd = mdia.FindChild("mdhd");
		if (mdhd is not null && mdhd.Data.Length >= 20) {
			var mdhdData = mdhd.Data;
			var mdhdVersion = mdhdData[0];
			if (mdhdVersion == 0) {
				trackTimescale = mdhdData.ToUInt32BE(12);
			} else if (mdhdData.Length >= 28) {
				trackTimescale = mdhdData.ToUInt32BE(20);
			}
		}

		// Navigate to moov/trak/mdia/minf/stbl/stsd for codec info
		var minf = mdia.FindChild("minf");
		var stbl = minf?.FindChild("stbl");
		var stsd = stbl?.FindChild("stsd");

		var codec = Mp4AudioCodec.Unknown;
		var channels = 0;
		var sampleRate = 0;
		var bitsPerSample = 0;
		var bitrate = 0;

		if (stsd is not null && stsd.Data.Length >= 16) {
			// stsd is a FullBox: version(1) + flags(3) + entry_count(4) + sample_entries
			var entryData = stsd.Data.Slice(8);
			if (entryData.Length >= 36) {
				// Sample entry structure:
				// size(4) + type(4) + reserved(6) + data_ref_idx(2) + reserved(8)
				// + channelcount(2) + samplesize(2) + pre_defined(2) + reserved(2)
				// + samplerate(4, 16.16 fixed)
				var codecType = entryData.Slice(4, 4).ToStringLatin1();
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
				channels = entryData.ToUInt16BE(24);

				// For lossy codecs like AAC, bits per sample is meaningless
				// Only report it for lossless codecs like ALAC
				if (codec == Mp4AudioCodec.Alac || codec == Mp4AudioCodec.Flac)
					bitsPerSample = entryData.ToUInt16BE(26);

				// Sample rate is stored as 16.16 fixed-point, which overflows for rates > 65535
				// For high sample rates like 96kHz, 176.4kHz, 192kHz, use mdhd timescale instead
				if (trackTimescale > 65535) {
					// mdhd has a sample rate that cannot fit in 16.16 fixed-point
					sampleRate = (int)trackTimescale;
				} else {
					var rawSampleRate = entryData.ToUInt32BE(32);
					var stsdSampleRate = (int)(rawSampleRate >> 16);
					sampleRate = stsdSampleRate > 0 ? stsdSampleRate : (int)trackTimescale;
				}

				// Parse codec-specific extensions for bitrate
				var sampleEntrySize = entryData.ToUInt32BE(0);
				if (sampleEntrySize > 36 && entryData.Length > 36) {
					bitrate = ExtractBitrateFromCodecBox(entryData.Slice(36), codec);
				}
			}
		}

		// Fall back to track timescale if sample rate not found
		if (sampleRate == 0 && trackTimescale > 0)
			sampleRate = (int)trackTimescale;

		// Calculate duration from movie header
		var durationSeconds = movieTimescale > 0 ? (double)movieDuration / movieTimescale : 0;
		var audioDuration = TimeSpan.FromSeconds(durationSeconds);

		var codecName = codec switch {
			Mp4AudioCodec.Aac => "AAC",
			Mp4AudioCodec.Alac => "ALAC",
			Mp4AudioCodec.Flac => "FLAC",
			Mp4AudioCodec.Opus => "Opus",
			Mp4AudioCodec.Ac3 => "AC-3",
			Mp4AudioCodec.Eac3 => "E-AC-3",
			_ => null
		};

		var properties = new AudioProperties(audioDuration, bitrate, sampleRate, bitsPerSample, channels, codecName);
		return (properties, codec);
	}

	/// <summary>
	/// Extracts bitrate from codec-specific boxes (esds for AAC, alac for ALAC).
	/// </summary>
	static int ExtractBitrateFromCodecBox(BinaryData codecExtensions, Mp4AudioCodec codec)
	{
		if (codecExtensions.Length < 8)
			return 0;

		// Look for nested boxes within the sample entry extensions
		var offset = 0;
		while (offset + 8 <= codecExtensions.Length) {
			var boxSize = codecExtensions.ToUInt32BE(offset);
			if (boxSize < 8 || boxSize > codecExtensions.Length - offset)
				break;

			var boxType = codecExtensions.Slice(offset + 4, 4).ToStringLatin1();

			if (boxType == "esds" && codec == Mp4AudioCodec.Aac) {
				// esds box: version(1) + flags(3) + ES_Descriptor
				// Parse ES_Descriptor to find DecoderConfigDescriptor with bitrate
				return ParseEsdsBitrate(codecExtensions.Slice(offset + 8, (int)boxSize - 8));
			}

			offset += (int)boxSize;
		}

		return 0;
	}

	/// <summary>
	/// Parses bitrate from esds (Elementary Stream Descriptor) box.
	/// </summary>
	static int ParseEsdsBitrate(BinaryData esdsData)
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
		offset += GetDescriptorSizeLength(esdsData.Slice(offset));
		if (offset + 3 > esdsData.Length)
			return 0;

		// ES_ID (2 bytes) + flags (1 byte)
		offset += 3;

		// DecoderConfigDescriptor tag 0x04
		if (offset >= esdsData.Length || esdsData[offset] != 0x04)
			return 0;
		offset++;

		// Skip size
		offset += GetDescriptorSizeLength(esdsData.Slice(offset));
		if (offset + 13 > esdsData.Length)
			return 0;

		// objectTypeIndication (1) + streamType (1) + bufferSizeDB (3) + maxBitrate (4) + avgBitrate (4)
		// Skip to avgBitrate at offset + 1 + 1 + 3 + 4 = 9
		var avgBitrate = esdsData.ToUInt32BE(offset + 9);

		// Convert from bits/second to kbits/second
		return (int)(avgBitrate / 1000);
	}

	/// <summary>
	/// Gets the length of a descriptor size field (1-4 bytes, expandable).
	/// </summary>
	static int GetDescriptorSizeLength(BinaryData data)
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

	static Mp4Tag? ExtractMetadata(Mp4Box moov)
	{
		// Navigate: moov -> udta -> meta -> ilst
		var udta = moov.FindChild("udta");
		if (udta is null)
			return null;

		var meta = udta.FindChild("meta");
		if (meta is null)
			return null;

		// meta is a FullBox, so children start after version(1) + flags(3) = 4 bytes
		// We need to re-parse meta's data as children, skipping the first 4 bytes
		if (meta.Data.Length < 4)
			return null;

		// Find ilst within meta's children (already parsed by Mp4BoxParser)
		var ilst = meta.FindChild("ilst");
		if (ilst is null)
			return null;

		return ParseIlst(ilst);
	}

	static Mp4Tag ParseIlst(Mp4Box ilst)
	{
		var tag = new Mp4Tag();

		// Each child of ilst is a metadata atom (©nam, ©ART, etc.)
		foreach (var atomBox in ilst.Children) {
			var atomId = atomBox.Type;

			// Handle cover art specially
			if (atomId == Mp4AtomMapping.CoverArt) {
				ParseCoverArt(atomBox, tag);
				continue;
			}

			// Handle freeform atoms (----)
			if (atomId == Mp4AtomMapping.FreeformAtom) {
				ParseFreeformAtom(atomBox, tag);
				continue;
			}

			// Standard atoms contain data sub-atoms
			var dataAtoms = new List<Mp4DataAtom>();
			foreach (var child in atomBox.Children) {
				if (child.Type == "data" && child.Data.Length >= 8) {
					var dataAtom = Mp4DataAtom.Parse(child.Data.Span);
					dataAtoms.Add(dataAtom);
				}
			}

			if (dataAtoms.Count > 0)
				tag.SetAtoms(atomId, dataAtoms);
		}

		return tag;
	}

	static void ParseCoverArt(Mp4Box covrBox, Mp4Tag tag)
	{
		var pictures = new List<Mp4Picture>();

		foreach (var child in covrBox.Children) {
			if (child.Type == "data" && child.Data.Length >= 8) {
				var dataAtom = Mp4DataAtom.Parse(child.Data.Span);
				var picture = Mp4Picture.FromDataAtom(dataAtom);
				pictures.Add(picture);
			}
		}

		if (pictures.Count > 0)
			tag.Pictures = [.. pictures];
	}

	static void ParseFreeformAtom(Mp4Box freeformBox, Mp4Tag tag)
	{
		// Freeform atoms have mean, name, and data children
		string? mean = null;
		string? name = null;
		var dataAtoms = new List<Mp4DataAtom>();

		foreach (var child in freeformBox.Children) {
			if (child.Type == "mean" && child.Data.Length >= 4) {
				// Skip version(1) + flags(3)
				mean = child.Data.Slice(4).ToStringUtf8();
			} else if (child.Type == "name" && child.Data.Length >= 4) {
				// Skip version(1) + flags(3)
				name = child.Data.Slice(4).ToStringUtf8();
			} else if (child.Type == "data" && child.Data.Length >= 8) {
				var dataAtom = Mp4DataAtom.Parse(child.Data.Span);
				dataAtoms.Add(dataAtom);
			}
		}

		if (!string.IsNullOrEmpty(mean) && !string.IsNullOrEmpty(name) && dataAtoms.Count > 0) {
			var key = $"{Mp4AtomMapping.FreeformAtom}:{mean}:{name}";
			tag.SetAtoms(key, dataAtoms);
		}
	}

	/// <summary>
	/// Attempts to read an MP4 file from a file path.
	/// </summary>
	/// <param name="path">The path to the MP4 file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static Mp4FileReadResult ReadFromFile(string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes(path, fileSystem);
		if (!readResult.IsSuccess)
			return Mp4FileReadResult.Failure(readResult.Error!);

		var result = Read(readResult.Data!);
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
	public static async Task<Mp4FileReadResult> ReadFromFileAsync(
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync(path, fileSystem, cancellationToken)
			.ConfigureAwait(false);
		if (!readResult.IsSuccess)
			return Mp4FileReadResult.Failure(readResult.Error!);

		var result = Read(readResult.Data!);
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
	public BinaryData Render(ReadOnlySpan<byte> originalData)
	{
		if (originalData.Length < Mp4Box.HeaderSize)
			return BinaryData.Empty;

		// Parse all top-level boxes to find their positions and content
		var boxes = new List<(Mp4Box Box, int Start, int Length)>();
		var offset = 0;
		while (offset < originalData.Length) {
			var remaining = originalData[offset..];
			if (remaining.Length < Mp4Box.HeaderSize)
				break;

			var result = Mp4Box.Parse(remaining);
			if (!result.IsSuccess)
				break;

			boxes.Add((result.Box!, offset, result.BytesConsumed));
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

		// Build the new moov box with updated metadata
		var newMoov = RebuildMoovWithMetadata(boxes[moovIndex].Box);

		// Reassemble the file
		using var builder = new BinaryDataBuilder();

		for (int i = 0; i < boxes.Count; i++) {
			if (i == moovIndex) {
				// Use our rebuilt moov
				builder.Add(newMoov);
			} else {
				// Copy original box data
				var (_, start, length) = boxes[i];
				builder.Add(new BinaryData(originalData.Slice(start, length)));
			}
		}

		return builder.ToBinaryData();
	}

	/// <summary>
	/// Rebuilds the moov box with updated metadata in udta/meta/ilst.
	/// </summary>
	BinaryData RebuildMoovWithMetadata(Mp4Box originalMoov)
	{
		using var moovContent = new BinaryDataBuilder();

		// Copy all children except udta
		bool hasUdta = false;
		foreach (var child in originalMoov.Children) {
			if (child.Type == "udta") {
				hasUdta = true;
				continue; // We'll add our own udta
			}
			moovContent.Add(child.Render());
		}

		// Build new udta with updated metadata (or create one if Tag exists)
		if (Tag is not null || hasUdta) {
			var newUdta = BuildUdtaBox();
			if (newUdta.Length > 0)
				moovContent.Add(newUdta);
		}

		// Build the moov box header
		using var moovBuilder = new BinaryDataBuilder();
		var moovContentData = moovContent.ToBinaryData();
		var totalSize = Mp4Box.HeaderSize + moovContentData.Length;

		moovBuilder.AddUInt32BE((uint)totalSize);
		moovBuilder.AddStringLatin1("moov");
		moovBuilder.Add(moovContentData);

		return moovBuilder.ToBinaryData();
	}

	/// <summary>
	/// Builds the udta box containing meta and ilst.
	/// </summary>
	BinaryData BuildUdtaBox()
	{
		if (Tag is null)
			return BinaryData.Empty;

		// Build ilst content
		var ilstContent = Tag.Render();
		if (ilstContent.IsEmpty)
			return BinaryData.Empty;

		// Build ilst box
		using var ilstBuilder = new BinaryDataBuilder();
		ilstBuilder.AddUInt32BE((uint)(Mp4Box.HeaderSize + ilstContent.Length));
		ilstBuilder.AddStringLatin1("ilst");
		ilstBuilder.Add(ilstContent);
		var ilstBox = ilstBuilder.ToBinaryData();

		// Build hdlr box for meta (handler type: mdir/appl)
		using var hdlrContent = new BinaryDataBuilder();
		hdlrContent.AddUInt32BE(0); // version + flags
		hdlrContent.AddUInt32BE(0); // pre_defined
		hdlrContent.AddStringLatin1("mdir"); // handler_type
		hdlrContent.AddStringLatin1("appl"); // manufacturer
		hdlrContent.AddUInt32BE(0); // reserved
		hdlrContent.AddUInt32BE(0); // reserved
		hdlrContent.Add((byte)0); // null-terminated name

		using var hdlrBuilder = new BinaryDataBuilder();
		var hdlrContentData = hdlrContent.ToBinaryData();
		hdlrBuilder.AddUInt32BE((uint)(Mp4Box.HeaderSize + hdlrContentData.Length));
		hdlrBuilder.AddStringLatin1("hdlr");
		hdlrBuilder.Add(hdlrContentData);
		var hdlrBox = hdlrBuilder.ToBinaryData();

		// Build meta box (FullBox: version + flags prefix)
		using var metaContent = new BinaryDataBuilder();
		metaContent.AddUInt32BE(0); // version(1) + flags(3) = 0
		metaContent.Add(hdlrBox);
		metaContent.Add(ilstBox);

		using var metaBuilder = new BinaryDataBuilder();
		var metaContentData = metaContent.ToBinaryData();
		metaBuilder.AddUInt32BE((uint)(Mp4Box.HeaderSize + metaContentData.Length));
		metaBuilder.AddStringLatin1("meta");
		metaBuilder.Add(metaContentData);
		var metaBox = metaBuilder.ToBinaryData();

		// Build udta box
		using var udtaBuilder = new BinaryDataBuilder();
		udtaBuilder.AddUInt32BE((uint)(Mp4Box.HeaderSize + metaBox.Length));
		udtaBuilder.AddStringLatin1("udta");
		udtaBuilder.Add(metaBox);

		return udtaBuilder.ToBinaryData();
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile(string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var rendered = Render(originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure("Failed to render MP4 file");

		return AtomicFileWriter.Write(path, rendered.Span, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync(
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var rendered = Render(originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult(FileWriteResult.Failure("Failed to render MP4 file"));

		return AtomicFileWriter.WriteAsync(path, rendered.Memory, fileSystem, cancellationToken);
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
	public FileWriteResult SaveToFile(string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty(SourcePath))
			return FileWriteResult.Failure("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = FileHelper.SafeReadAllBytes(SourcePath!, fs);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure($"Failed to re-read source file: {readResult.Error}");

		return SaveToFile(path, readResult.Data!, fileSystem);
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
	public FileWriteResult SaveToFile(IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty(SourcePath))
			return FileWriteResult.Failure("No source path available. File was not read from disk.");

		return SaveToFile(SourcePath!, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync(
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(SourcePath))
			return FileWriteResult.Failure("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = await FileHelper.SafeReadAllBytesAsync(SourcePath!, fs, cancellationToken)
			.ConfigureAwait(false);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure($"Failed to re-read source file: {readResult.Error}");

		return await SaveToFileAsync(path, readResult.Data!, fileSystem, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Asynchronously saves the file back to its source path.
	/// </summary>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync(
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(SourcePath))
			return Task.FromResult(FileWriteResult.Failure("No source path available. File was not read from disk."));

		return SaveToFileAsync(SourcePath!, fileSystem, cancellationToken);
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

	Mp4FileReadResult(Mp4File? file, string? error, int bytesConsumed)
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
	public static Mp4FileReadResult Success(Mp4File file, int bytesConsumed) =>
		new(file, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static Mp4FileReadResult Failure(string error) =>
		new(null, error, 0);

	/// <inheritdoc/>
	public bool Equals(Mp4FileReadResult other) =>
		ReferenceEquals(File, other.File) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals(object? obj) =>
		obj is Mp4FileReadResult other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(File, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator ==(Mp4FileReadResult left, Mp4FileReadResult right) =>
		left.Equals(right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator !=(Mp4FileReadResult left, Mp4FileReadResult right) =>
		!left.Equals(right);
}
