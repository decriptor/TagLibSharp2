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
	public const string Year = "©day";
	public const string Comment = "©cmt";
	public const string Composer = "©wrt";
	public const string Grouping = "©grp";
	public const string Lyrics = "©lyr";
	public const string Encoder = "©too";
	public const string Copyright = "cprt";

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

	/// <summary>
	/// The tag's key for a freeform atom: <c>----:</c>, the mean, <c>:</c>, then the name. A <c>:</c> or
	/// <c>\</c> in the mean is escaped with <c>\</c>, so the first unescaped <c>:</c> after the prefix ends the
	/// mean and both parts round-trip whatever they hold. Means without either character, such as
	/// <c>com.apple.iTunes</c>, give the plain <c>----:mean:name</c> key.
	/// </summary>
	public static string FreeformKey (string mean, string name)
	{
		var escaped = new System.Text.StringBuilder (FreeformAtom.Length + mean.Length + name.Length + 2);
		escaped.Append (FreeformAtom).Append (':');
		foreach (var c in mean) {
			if (c is ':' or '\\')
				escaped.Append ('\\');
			escaped.Append (c);
		}
		return escaped.Append (':').Append (name).ToString ();
	}

	/// <summary>
	/// Splits a key made by <see cref="FreeformKey"/>. False when <paramref name="key"/> is not a freeform key.
	/// </summary>
	public static bool TrySplitFreeformKey (string key, out string mean, out string name)
	{
		mean = name = "";
		var prefix = FreeformAtom.Length + 1;
		if (key.Length < prefix || !key.StartsWith (FreeformAtom + ":", StringComparison.Ordinal))
			return false;

		var unescaped = new System.Text.StringBuilder ();
		for (var i = prefix; i < key.Length; i++) {
			var c = key[i];
			if (c == '\\' && i + 1 < key.Length) {
				unescaped.Append (key[++i]);
				continue;
			}
			if (c == ':') {
				mean = unescaped.ToString ();
				name = key.Substring (i + 1);
				return true;
			}
			unescaped.Append (c);
		}
		return false;
	}

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
	public const int TypeJpeg = 13;
	public const int TypePng = 14;
	public const int TypeInteger = 21;
}
