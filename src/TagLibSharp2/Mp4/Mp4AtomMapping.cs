// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Mp4;

/// <summary>
/// Maps tag properties to iTunes-style MP4 atom identifiers.
/// </summary>
/// <remarks>
/// <para>
/// iTunes metadata uses 4-byte atom codes, mostly prefixed with © (0xA9).
/// These atoms are stored in the moov → udta → meta → ilst container.
/// </para>
/// <para>
/// Reference: MP4 Registration Authority (mp4ra.org) and reverse-engineered iTunes behavior.
/// </para>
/// </remarks>
internal static class Mp4AtomMapping
{
	// Standard text metadata atoms (© prefixed)
	public const string Title = "©nam";
	public const string Artist = "©ART";
	public const string Album = "©alb";
	public const string Genre = "©gen";
	public const string GenreId3 = "gnre";
	public const string Year = "©day";
	public const string Comment = "©cmt";
	public const string Composer = "©wrt";
	public const string Grouping = "©grp";
	public const string Lyrics = "©lyr";
	public const string Encoder = "©too";
	public const string Copyright = "cprt";

	// Older files store the same text in ©cpy. cprt wins when both are present.
	public const string CopyrightAlias = "©cpy";

	// Other names that readers fold into a mapped field. A getter falls back to the alias and a
	// setter removes it. QuickTime user data atoms:
	// https://developer.apple.com/documentation/quicktime-file-format/user_data_atoms
	// 3GPP asset atoms: 3GPP TS 26.244, section 8 (titl, albm, dscp).
	public const string ComposerAlias = "©com"; // QuickTime "Name of composer"
	public const string EncoderAlias = "©swr"; // QuickTime "Name and version number of the software"
	public const string SubtitleAlias = "©st3"; // read as subtitle by ffprobe and ExifTool
	public const string TitleAlias = "titl"; // 3GPP title
	public const string AlbumAlias = "albm"; // 3GPP album title
	public const string DescriptionAlias = "dscp"; // 3GPP description

	static readonly HashSet<string> TagAtoms = new (StringComparer.Ordinal) {
		Title, Artist, Album, Genre, GenreId3, Year, Comment, Composer, Grouping, Lyrics, Encoder,
		Copyright, CopyrightAlias, ComposerAlias, EncoderAlias, SubtitleAlias, TitleAlias, AlbumAlias,
		DescriptionAlias, "aART", "trkn", "disk", "tmpo", "cpil", "pgap", "rtng", "covr",
		"soal", "soaa", "soar", "soco", "sonm", "©mvn", "©mvi", "©mvc", "©wrk", "shwm",
		"purl", "egid", "catg", "keyw", "pcst", "©pub", "©enc", "desc", "ldes",
	};

	static readonly HashSet<string> AliasAtoms = new (StringComparer.Ordinal) {
		ComposerAlias, EncoderAlias, SubtitleAlias, TitleAlias, AlbumAlias, DescriptionAlias,
	};

	/// <summary>
	/// True for an ilst atom type the tag reads into a property (not freeform ----).
	/// </summary>
	public static bool IsTagAtom (string atomId) => TagAtoms.Contains (atomId);

	/// <summary>
	/// True for an alias atom that is read like a text atom although the box parser leaves it unparsed.
	/// </summary>
	public static bool IsAliasAtom (string atomId) => AliasAtoms.Contains (atomId);

	// Album artist (not © prefixed)
	public const string AlbumArtist = "aART";

	// Track and disc numbers (binary format)
	public const string TrackNumber = "trkn";
	public const string DiscNumber = "disk";

	// Integer metadata
	public const string BeatsPerMinute = "tmpo";
	public const string Compilation = "cpil";
	public const string GaplessPlayback = "pgap";
	public const string ContentRating = "rtng";

	// Cover art (can contain multiple images)
	public const string CoverArt = "covr";

	// Sort order atoms
	public const string AlbumSort = "soal";
	public const string AlbumArtistSort = "soaa";
	public const string ArtistSort = "soar";
	public const string ComposerSort = "soco";
	public const string TitleSort = "sonm";

	// Classical music metadata
	public const string MovementName = "©mvn";
	public const string MovementNumber = "©mvi";
	public const string MovementCount = "©mvc";
	public const string WorkName = "©wrk";
	public const string ShowMovement = "shwm";

	// Podcast metadata
	public const string PodcastUrl = "purl";
	public const string PodcastGuid = "egid";
	public const string Category = "catg";
	public const string Keywords = "keyw";
	public const string PodcastFlag = "pcst";

	// Additional text metadata
	public const string Publisher = "©pub";
	public const string EncodedBy = "©enc";
	public const string Description = "desc";
	public const string LongDescription = "ldes";

	// Freeform metadata namespace
	public const string FreeformAtom = "----";
	public const string FreeformMean = "mean";
	public const string FreeformName = "name";

	// Common freeform namespaces
	public const string AppleNamespace = "com.apple.iTunes";
	public const string MusicBrainzNamespace = "org.musicbrainz";

	// MusicBrainz freeform tag names (stored in ---- atoms with org.musicbrainz namespace)
	public const string MusicBrainzTrackId = "MusicBrainz Track Id";
	public const string MusicBrainzAlbumId = "MusicBrainz Album Id";
	public const string MusicBrainzArtistId = "MusicBrainz Artist Id";
	public const string MusicBrainzAlbumArtistId = "MusicBrainz Album Artist Id";
	public const string MusicBrainzReleaseGroupId = "MusicBrainz Release Group Id";
	public const string MusicBrainzWorkId = "MusicBrainz Work Id";
	public const string MusicBrainzRecordingId = "MusicBrainz Recording Id";
	public const string MusicBrainzDiscId = "MusicBrainz Disc Id";
	public const string MusicBrainzReleaseStatus = "MusicBrainz Album Status";
	public const string MusicBrainzReleaseType = "MusicBrainz Album Type";
	public const string MusicBrainzReleaseCountry = "MusicBrainz Album Release Country";

	// AcoustID freeform tag names (stored in ---- atoms with com.apple.iTunes namespace)
	public const string AcoustIdId = "Acoustid Id";
	public const string AcoustIdFingerprint = "Acoustid Fingerprint";

	// Additional freeform tag names (stored in ---- atoms with com.apple.iTunes namespace)
	public const string Isrc = "ISRC";
	public const string Conductor = "CONDUCTOR";
	public const string OriginalYear = "ORIGINAL YEAR";
	public const string GaplessInfo = "iTunSMPB"; // Gapless playback info

	// DJ and remix metadata (stored in ---- atoms with com.apple.iTunes namespace)
	public const string InitialKey = "initialkey";
	public const string Remixer = "REMIXER";
	public const string Mood = "MOOD";
	public const string Subtitle = "SUBTITLE";

	// Collector metadata (stored in ---- atoms with com.apple.iTunes namespace)
	public const string Barcode = "BARCODE";
	public const string CatalogNumber = "CATALOGNUMBER";
	public const string AmazonId = "ASIN";

	// Library management (stored in ---- atoms with com.apple.iTunes namespace)
	public const string DateTagged = "date_tagged";
	public const string Language = "LANGUAGE";
	public const string MediaType = "MEDIA";

	// ReplayGain freeform tag names (stored in ---- atoms with com.apple.iTunes namespace)
	// These use lowercase with underscores per the ReplayGain specification
	public const string ReplayGainTrackGain = "replaygain_track_gain";
	public const string ReplayGainTrackPeak = "replaygain_track_peak";
	public const string ReplayGainAlbumGain = "replaygain_album_gain";
	public const string ReplayGainAlbumPeak = "replaygain_album_peak";

	// R128 loudness freeform tag names (EBU R 128 standard)
	// Values are stored as Q7.8 fixed-point integers (value / 256 = dB)
	public const string R128TrackGain = "R128_TRACK_GAIN";
	public const string R128AlbumGain = "R128_ALBUM_GAIN";

	// Data atom type indicators (stored in flags field)
	public const int TypeBinary = 0;
	public const int TypeUtf8 = 1;
	public const int TypeUtf16 = 2;
	public const int TypeGif = 12;
	public const int TypeJpeg = 13;
	public const int TypePng = 14;
	public const int TypeBmp = 27;
	public const int TypeInteger = 21;
}
