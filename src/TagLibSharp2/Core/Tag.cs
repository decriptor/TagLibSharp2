// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace TagLibSharp2.Core;

/// <summary>
/// Indicates the tag types present in a file.
/// </summary>
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute - AllTags is intentionally all bits set
[Flags]
public enum TagTypes
{
	/// <summary>
	/// No tags present.
	/// </summary>
	None = 0,

	/// <summary>
	/// ID3v1 tag.
	/// </summary>
	Id3v1 = 1 << 0,

	/// <summary>
	/// ID3v2 tag.
	/// </summary>
	Id3v2 = 1 << 1,

	/// <summary>
	/// APE tag.
	/// </summary>
	Ape = 1 << 2,

	/// <summary>
	/// Xiph Vorbis Comment (used in FLAC, Ogg Vorbis, Opus, etc.).
	/// </summary>
	Xiph = 1 << 3,

	/// <summary>
	/// Apple/iTunes metadata (MP4/M4A).
	/// </summary>
	Apple = 1 << 4,

	/// <summary>
	/// ASF/WMA metadata.
	/// </summary>
	Asf = 1 << 5,

	/// <summary>
	/// RIFF INFO chunk.
	/// </summary>
	RiffInfo = 1 << 6,

	/// <summary>
	/// Matroska tags.
	/// </summary>
	Matroska = 1 << 7,

	/// <summary>
	/// FLAC metadata blocks (pictures, etc.).
	/// </summary>
	FlacMetadata = 1 << 8,

	/// <summary>
	/// XMP metadata.
	/// </summary>
	Xmp = 1 << 9,

	/// <summary>
	/// All tag types.
	/// </summary>
	AllTags = unchecked((int)0xFFFFFFFF)
}
#pragma warning restore CA2217

/// <summary>
/// Abstract base class for all tag types (ID3v1, ID3v2, Vorbis Comments, etc.).
/// Provides common metadata properties with nullable support.
/// </summary>
public abstract class Tag
{
	/// <summary>
	/// Gets the type of tag represented by this instance.
	/// </summary>
	public abstract TagTypes TagType { get; }

	/// <summary>
	/// Gets or sets the title/song name.
	/// </summary>
	public abstract string? Title { get; set; }

	/// <summary>
	/// Gets or sets the primary artist/performer.
	/// </summary>
	/// <remarks>
	/// This returns the first performer. For multiple performers, use <see cref="Performers"/>.
	/// </remarks>
	public abstract string? Artist { get; set; }

	/// <summary>
	/// Gets or sets the performers/artists for this track.
	/// </summary>
	/// <remarks>
	/// This is the primary array of artists. The <see cref="Artist"/> property returns
	/// the first value from this array.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[] Performers {
		get => string.IsNullOrEmpty (Artist) ? [] : [Artist!];
		set => Artist = value is { Length: > 0 } ? value[0] : null;
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the album/collection name.
	/// </summary>
	public abstract string? Album { get; set; }

	/// <summary>
	/// Gets or sets the year of release.
	/// </summary>
	public abstract string? Year { get; set; }

	/// <summary>
	/// Gets or sets the original release date.
	/// </summary>
	/// <remarks>
	/// For reissues, remasters, or re-releases, this is the date of the original release.
	/// Format is typically YYYY or YYYY-MM-DD depending on tag format support.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? OriginalReleaseDate { get => null; set { } }

	/// <summary>
	/// Gets or sets the comment/description.
	/// </summary>
	public abstract string? Comment { get; set; }

	/// <summary>
	/// Gets or sets the genre name.
	/// </summary>
	/// <remarks>
	/// This returns the first genre. For multiple genres, use <see cref="Genres"/>.
	/// </remarks>
	public abstract string? Genre { get; set; }

	/// <summary>
	/// Gets or sets the genres for this track.
	/// </summary>
	/// <remarks>
	/// The <see cref="Genre"/> property returns the first value from this array.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[] Genres {
		get => string.IsNullOrEmpty (Genre) ? [] : [Genre!];
		set => Genre = value is { Length: > 0 } ? value[0] : null;
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public abstract uint? Track { get; set; }

	/// <summary>
	/// Gets or sets the album artist (for compilations/various artists albums).
	/// </summary>
	/// <remarks>
	/// This returns the first album artist. For multiple album artists, use <see cref="AlbumArtists"/>.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? AlbumArtist { get => null; set { } }

	/// <summary>
	/// Gets or sets the album artists for this track.
	/// </summary>
	/// <remarks>
	/// Used for compilations and various artists albums. The <see cref="AlbumArtist"/>
	/// property returns the first value from this array.
	/// Not all tag formats support this field.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[] AlbumArtists {
		get => string.IsNullOrEmpty (AlbumArtist) ? [] : [AlbumArtist!];
		set => AlbumArtist = value is { Length: > 0 } ? value[0] : null;
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the disc number.
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual uint? DiscNumber { get => null; set { } }

	/// <summary>
	/// Gets or sets the composer.
	/// </summary>
	/// <remarks>
	/// This returns the first composer. For multiple composers, use <see cref="Composers"/>.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Composer { get => null; set { } }

	/// <summary>
	/// Gets or sets the composers for this track.
	/// </summary>
	/// <remarks>
	/// The <see cref="Composer"/> property returns the first value from this array.
	/// Not all tag formats support this field.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[] Composers {
		get => string.IsNullOrEmpty (Composer) ? [] : [Composer!];
		set => Composer = value is { Length: > 0 } ? value[0] : null;
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the beats per minute (BPM) of the track.
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual uint? BeatsPerMinute { get => null; set { } }

	/// <summary>
	/// Gets or sets the conductor or director.
	/// </summary>
	/// <remarks>
	/// For classical music, this is typically the orchestra conductor.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Conductor { get => null; set { } }

	/// <summary>
	/// Gets or sets the name of the work (composition).
	/// </summary>
	/// <remarks>
	/// <para>
	/// For classical music, this is the overall composition name (e.g., "Symphony No. 9 in D minor, Op. 125").
	/// The track title typically contains the movement name, while this field contains the work name.
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "WORK".
	/// In Vorbis Comments, this is stored as WORK.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? Work { get => null; set { } }

	/// <summary>
	/// Gets or sets the movement name.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For classical music, this is the name of the movement (e.g., "Allegro con brio").
	/// Used in conjunction with <see cref="Work"/> to describe the full piece.
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "MOVEMENT".
	/// In Vorbis Comments, this is stored as MOVEMENT or MOVEMENTNAME.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? Movement { get => null; set { } }

	/// <summary>
	/// Gets or sets the movement number within the work.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For classical music, this indicates which movement this is (e.g., 1 for the first movement).
	/// Used with <see cref="MovementTotal"/> to show position like "Movement 2 of 4".
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "MOVEMENTNUMBER".
	/// In Vorbis Comments, this is stored as MOVEMENTNUMBER.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual uint? MovementNumber { get => null; set { } }

	/// <summary>
	/// Gets or sets the total number of movements in the work.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For classical music, this indicates the total movements in the work (e.g., 4 for a typical symphony).
	/// Used with <see cref="MovementNumber"/> to show position like "Movement 2 of 4".
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "MOVEMENTTOTAL".
	/// In Vorbis Comments, this is stored as MOVEMENTTOTAL or MOVEMENTCOUNT.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual uint? MovementTotal { get => null; set { } }

	/// <summary>
	/// Gets or sets the copyright information.
	/// </summary>
	/// <remarks>
	/// Typically in the format "YYYY Label Name" (e.g., "2024 Acme Records").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Copyright { get => null; set { } }

	/// <summary>
	/// Gets or sets whether this track is part of a compilation album.
	/// </summary>
	/// <remarks>
	/// Used by music players to group various artist albums.
	/// Not all tag formats support this field. Default implementation returns false.
	/// </remarks>
	public virtual bool IsCompilation { get => false; set { } }

	/// <summary>
	/// Gets or sets the total number of tracks on the album/disc.
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual uint? TotalTracks { get => null; set { } }

	/// <summary>
	/// Gets or sets the total number of discs in the set.
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual uint? TotalDiscs { get => null; set { } }

	/// <summary>
	/// Gets or sets the lyrics or text content.
	/// </summary>
	/// <remarks>
	/// For song lyrics, poems, or other text content associated with the media.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Lyrics { get => null; set { } }

	/// <summary>
	/// Gets or sets the ISRC (International Standard Recording Code).
	/// </summary>
	/// <remarks>
	/// A 12-character alphanumeric code that uniquely identifies sound recordings.
	/// Format: CC-XXX-YY-NNNNN (country, registrant, year, designation).
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Isrc { get => null; set { } }

	/// <summary>
	/// Gets or sets the publisher or record label.
	/// </summary>
	/// <remarks>
	/// The name of the record label or publisher of this recording.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Publisher { get => null; set { } }

	/// <summary>
	/// Gets or sets the person or organization that encoded the audio.
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? EncodedBy { get => null; set { } }

	/// <summary>
	/// Gets or sets the encoder settings used to encode the audio.
	/// </summary>
	/// <remarks>
	/// Typically contains software name and encoding parameters (e.g., "LAME 320kbps CBR").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? EncoderSettings { get => null; set { } }

	/// <summary>
	/// Gets or sets the content group or grouping for the track.
	/// </summary>
	/// <remarks>
	/// Used to group related tracks (e.g., "Summer Hits 2024", "Workout Mix").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Grouping { get => null; set { } }

	/// <summary>
	/// Gets or sets the subtitle or description refinement.
	/// </summary>
	/// <remarks>
	/// Additional information about the track (e.g., "Radio Edit", "Live Version").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Subtitle { get => null; set { } }

	/// <summary>
	/// Gets or sets the remixer or modifier of the track.
	/// </summary>
	/// <remarks>
	/// The artist who remixed or modified the original track.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Remixer { get => null; set { } }

	/// <summary>
	/// Gets or sets the initial musical key of the track.
	/// </summary>
	/// <remarks>
	/// The key in which the track is performed (e.g., "Am", "F#m", "Cmaj").
	/// Useful for DJs for harmonic mixing.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? InitialKey { get => null; set { } }

	/// <summary>
	/// Gets or sets the mood of the track.
	/// </summary>
	/// <remarks>
	/// Describes the emotional character (e.g., "Energetic", "Melancholic", "Uplifting").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Mood { get => null; set { } }

	/// <summary>
	/// Gets or sets the original media type.
	/// </summary>
	/// <remarks>
	/// Describes the source media (e.g., "CD", "Vinyl", "DIG/A" for digital analog).
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MediaType { get => null; set { } }

	/// <summary>
	/// Gets or sets the language of the audio content.
	/// </summary>
	/// <remarks>
	/// Typically an ISO 639-2 three-letter language code (e.g., "eng", "jpn", "deu").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Language { get => null; set { } }

	/// <summary>
	/// Gets or sets the barcode (UPC/EAN) of the release.
	/// </summary>
	/// <remarks>
	/// The Universal Product Code or European Article Number for the release.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Barcode { get => null; set { } }

	/// <summary>
	/// Gets or sets the catalog number assigned by the label.
	/// </summary>
	/// <remarks>
	/// The label's catalog identifier for the release (e.g., "WPCR-80001").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? CatalogNumber { get => null; set { } }

	// Sort Order properties

	/// <summary>
	/// Gets or sets the sort order for the album title.
	/// </summary>
	/// <remarks>
	/// Used for sorting when the display name should differ from the sort name.
	/// For example, "The White Album" might sort as "White Album, The".
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? AlbumSort { get => null; set { } }

	/// <summary>
	/// Gets or sets the sort order for the artist name.
	/// </summary>
	/// <remarks>
	/// Used for sorting when the display name should differ from the sort name.
	/// For example, "The Beatles" might sort as "Beatles, The".
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? ArtistSort { get => null; set { } }

	/// <summary>
	/// Gets or sets the sort order for the title.
	/// </summary>
	/// <remarks>
	/// Used for sorting when the display name should differ from the sort name.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? TitleSort { get => null; set { } }

	/// <summary>
	/// Gets or sets the sort order for the album artist name.
	/// </summary>
	/// <remarks>
	/// Used for sorting when the display name should differ from the sort name.
	/// For example, "Various Artists" compilations might sort under a specific letter.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? AlbumArtistSort { get => null; set { } }

	/// <summary>
	/// Gets or sets the sort names for the performers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a parallel array to the performers list. Each element provides the sort name
	/// for the corresponding performer at the same index. For example, if performers are
	/// ["The Beatles", "David Bowie"], PerformersSort might be ["Beatles, The", "Bowie, David"].
	/// </para>
	/// <para>
	/// Not all tag formats support multiple performer sort names.
	/// Default implementation returns null.
	/// </para>
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[]? PerformersSort { get => null; set { } }
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the sort names for the album artists.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a parallel array to the album artists list. Each element provides the sort name
	/// for the corresponding album artist at the same index.
	/// </para>
	/// <para>
	/// Not all tag formats support multiple album artist sort names.
	/// Default implementation returns null.
	/// </para>
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[]? AlbumArtistsSort { get => null; set { } }
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the sort names for the composers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a parallel array to the composers list. Each element provides the sort name
	/// for the corresponding composer at the same index. For example, if composers are
	/// ["Johann Sebastian Bach"], ComposersSort might be ["Bach, Johann Sebastian"].
	/// </para>
	/// <para>
	/// Not all tag formats support multiple composer sort names.
	/// Default implementation returns null.
	/// </para>
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[]? ComposersSort { get => null; set { } }
#pragma warning restore CA1819

	// ReplayGain properties

	/// <summary>
	/// Gets or sets the ReplayGain track gain value (e.g., "-6.50 dB").
	/// </summary>
	/// <remarks>
	/// ReplayGain values are stored as strings including the "dB" suffix.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? ReplayGainTrackGain { get => null; set { } }

	/// <summary>
	/// Gets or sets the ReplayGain track peak value (e.g., "0.988547").
	/// </summary>
	/// <remarks>
	/// Peak values are stored as decimal strings between 0.0 and 1.0.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? ReplayGainTrackPeak { get => null; set { } }

	/// <summary>
	/// Gets or sets the ReplayGain album gain value (e.g., "-6.50 dB").
	/// </summary>
	/// <remarks>
	/// ReplayGain values are stored as strings including the "dB" suffix.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? ReplayGainAlbumGain { get => null; set { } }

	/// <summary>
	/// Gets or sets the ReplayGain album peak value (e.g., "0.988547").
	/// </summary>
	/// <remarks>
	/// Peak values are stored as decimal strings between 0.0 and 1.0.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? ReplayGainAlbumPeak { get => null; set { } }

	// R128 Loudness (EBU R 128)

	/// <summary>
	/// Gets or sets the R128 track gain value.
	/// </summary>
	/// <remarks>
	/// <para>
	/// EBU R 128 is a loudness normalization recommendation from the European Broadcasting Union.
	/// The track gain is stored as an integer representing gain in 1/256th of a dB (Q7.8 format).
	/// </para>
	/// <para>
	/// For example, a value of "256" represents +1 dB, "-512" represents -2 dB.
	/// Some implementations store this as a plain dB string like "-14.00 dB".
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "R128_TRACK_GAIN".
	/// In Vorbis Comments, this is stored as R128_TRACK_GAIN.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? R128TrackGain { get => null; set { } }

	/// <summary>
	/// Gets or sets the R128 album gain value.
	/// </summary>
	/// <remarks>
	/// <para>
	/// EBU R 128 is a loudness normalization recommendation from the European Broadcasting Union.
	/// The album gain is stored as an integer representing gain in 1/256th of a dB (Q7.8 format).
	/// </para>
	/// <para>
	/// For example, a value of "256" represents +1 dB, "-512" represents -2 dB.
	/// Some implementations store this as a plain dB string like "-14.00 dB".
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "R128_ALBUM_GAIN".
	/// In Vorbis Comments, this is stored as R128_ALBUM_GAIN.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? R128AlbumGain { get => null; set { } }

	/// <summary>
	/// Gets or sets the R128 track gain value in decibels.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a convenience property that converts the Q7.8 fixed-point integer value
	/// stored in <see cref="R128TrackGain"/> to/from decibels as a double.
	/// </para>
	/// <para>
	/// The conversion formula is: dB = stored_value / 256.0
	/// For example, a stored value of "256" represents +1 dB.
	/// </para>
	/// </remarks>
	public double? R128TrackGainDb {
		get {
			var value = R128TrackGain;
			if (value is null || !short.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q78))
				return null;
			return q78 / 256.0;
		}
		set {
			R128TrackGain = value.HasValue
				? ((short)Math.Round (value.Value * 256.0)).ToString (CultureInfo.InvariantCulture)
				: null;
		}
	}

	/// <summary>
	/// Gets or sets the R128 album gain value in decibels.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a convenience property that converts the Q7.8 fixed-point integer value
	/// stored in <see cref="R128AlbumGain"/> to/from decibels as a double.
	/// </para>
	/// <para>
	/// The conversion formula is: dB = stored_value / 256.0
	/// For example, a stored value of "-512" represents -2 dB.
	/// </para>
	/// </remarks>
	public double? R128AlbumGainDb {
		get {
			var value = R128AlbumGain;
			if (value is null || !short.TryParse (value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q78))
				return null;
			return q78 / 256.0;
		}
		set {
			R128AlbumGain = value.HasValue
				? ((short)Math.Round (value.Value * 256.0)).ToString (CultureInfo.InvariantCulture)
				: null;
		}
	}

	// MusicBrainz IDs

	/// <summary>
	/// Gets or sets the MusicBrainz Track ID (Recording MBID).
	/// </summary>
	/// <remarks>
	/// A UUID identifying this specific recording in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzTrackId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Release ID (Album MBID).
	/// </summary>
	/// <remarks>
	/// A UUID identifying this specific release in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzReleaseId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Artist ID.
	/// </summary>
	/// <remarks>
	/// A UUID identifying the primary artist in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzArtistId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Release Group ID.
	/// </summary>
	/// <remarks>
	/// A UUID identifying the release group (album across all releases) in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzReleaseGroupId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Album Artist ID.
	/// </summary>
	/// <remarks>
	/// A UUID identifying the album artist in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzAlbumArtistId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Release Artist ID.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is an alias for <see cref="MusicBrainzAlbumArtistId"/> for TagLib# compatibility.
	/// Both properties read/write the same underlying data.
	/// </para>
	/// <para>
	/// A UUID identifying the release artist in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? MusicBrainzReleaseArtistId {
		get => MusicBrainzAlbumArtistId;
		set => MusicBrainzAlbumArtistId = value;
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Recording ID.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A UUID identifying the specific recording in the MusicBrainz database.
	/// This differs from TrackId which identifies the track on a specific release.
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a UFID frame with owner "http://musicbrainz.org".
	/// In Vorbis Comments, this is stored as MUSICBRAINZ_TRACKID (same as TrackId).
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? MusicBrainzRecordingId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Work ID.
	/// </summary>
	/// <remarks>
	/// A UUID identifying the musical work (composition) in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzWorkId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz Disc ID.
	/// </summary>
	/// <remarks>
	/// A hash identifying the CD table of contents in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzDiscId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz release status.
	/// </summary>
	/// <remarks>
	/// Values include: official, promotional, bootleg, pseudo-release.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzReleaseStatus { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz release type.
	/// </summary>
	/// <remarks>
	/// Values include: album, single, ep, compilation, soundtrack, live, remix, etc.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzReleaseType { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicBrainz release country.
	/// </summary>
	/// <remarks>
	/// ISO 3166-1 alpha-2 country code (e.g., "US", "GB", "JP").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? MusicBrainzReleaseCountry { get => null; set { } }

	/// <summary>
	/// Gets or sets the AcoustID identifier.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A UUID identifying this audio track in the AcoustID database.
	/// AcoustID uses audio fingerprints to identify tracks regardless of metadata.
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "ACOUSTID_ID".
	/// In Vorbis Comments, this is stored as ACOUSTID_ID.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? AcoustIdId { get => null; set { } }

	/// <summary>
	/// Gets or sets the AcoustID audio fingerprint.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The chromaprint audio fingerprint used by AcoustID for audio identification.
	/// This is a compressed representation of the audio's acoustic characteristics.
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in a TXXX frame with description "ACOUSTID_FINGERPRINT".
	/// In Vorbis Comments, this is stored as ACOUSTID_FINGERPRINT.
	/// Fingerprints are typically generated by the chromaprint/fpcalc utility.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
	public virtual string? AcoustIdFingerprint { get => null; set { } }

	/// <summary>
	/// Gets or sets the sort order for the composer name.
	/// </summary>
	/// <remarks>
	/// Used for sorting when the display name should differ from the sort name.
	/// For example, "Johann Sebastian Bach" might sort as "Bach, Johann Sebastian".
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? ComposerSort { get => null; set { } }

	/// <summary>
	/// Gets or sets the date/time when the file was tagged.
	/// </summary>
	/// <remarks>
	/// Stored as an ISO 8601 formatted string (e.g., "2025-12-27" or "2025-12-27T10:30:00").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? DateTagged { get => null; set { } }

	/// <summary>
	/// Gets or sets the description or synopsis of the content.
	/// </summary>
	/// <remarks>
	/// Used for story summaries, plot descriptions, or detailed content descriptions.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Description { get => null; set { } }

	/// <summary>
	/// Gets or sets the Amazon Standard Identification Number (ASIN).
	/// </summary>
	/// <remarks>
	/// A 10-character alphanumeric identifier assigned by Amazon (e.g., "B000002UAL").
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? AmazonId { get => null; set { } }

	/// <summary>
	/// Gets or sets the MusicIP PUID (Portable Unique Identifier).
	/// </summary>
	/// <remarks>
	/// <b>Obsolete:</b> MusicIP service was discontinued. Use AcoustID fingerprints instead.
	/// This property is maintained for compatibility with legacy tagged files.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	[System.Obsolete ("MusicIP PUID is obsolete. MusicIP service was discontinued. Use AcoustID fingerprints instead.")]
	public virtual string? MusicIpId { get => null; set { } }

	/// <summary>
	/// Gets or sets the roles of the performers.
	/// </summary>
	/// <remarks>
	/// This is a parallel array to the performers (artists) list. Each element
	/// describes the role of the corresponding performer at the same index.
	/// For example, if Artists = ["John", "Jane"], PerformersRole might be ["vocals", "guitar"].
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual string[]? PerformersRole { get => null; set { } }
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the pictures/images attached to this media.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Returns album artwork, cover images, artist photos, etc.
	/// The first image of type <see cref="PictureType.FrontCover"/> is typically
	/// the primary album art.
	/// </para>
	/// <para>
	/// Not all tag formats support embedded images. Default implementation returns empty array.
	/// </para>
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public virtual IPicture[] Pictures { get => []; set { } }
#pragma warning restore CA1819

	/// <summary>
	/// Gets a value indicating whether all standard fields are empty or null.
	/// </summary>
	public virtual bool IsEmpty =>
		string.IsNullOrEmpty (Title) &&
		string.IsNullOrEmpty (Artist) &&
		string.IsNullOrEmpty (Album) &&
		string.IsNullOrEmpty (Year) &&
		string.IsNullOrEmpty (Comment) &&
		string.IsNullOrEmpty (Genre) &&
		Track is null;

	/// <summary>
	/// Serializes the tag to its binary representation.
	/// </summary>
	/// <returns>The binary representation of the tag.</returns>
	public abstract BinaryData Render ();

	/// <summary>
	/// Clears all tag data, resetting to an empty state.
	/// </summary>
	public abstract void Clear ();

	/// <summary>
	/// Validates the tag and returns a list of issues found.
	/// </summary>
	/// <returns>A validation result containing any issues found.</returns>
	/// <remarks>
	/// <para>
	/// Common validations include:
	/// </para>
	/// <list type="bullet">
	/// <item>Track number greater than total tracks</item>
	/// <item>Disc number greater than total discs</item>
	/// <item>Invalid ISRC format</item>
	/// <item>Year outside reasonable range</item>
	/// </list>
	/// <para>
	/// Derived classes should override this method to add format-specific validations.
	/// </para>
	/// </remarks>
	public virtual ValidationResult Validate ()
	{
		var result = new ValidationResult ();

		// Validate track/disc numbers
		if (Track.HasValue && TotalTracks.HasValue && Track.Value > TotalTracks.Value)
			result.AddWarning ("Track", $"Track number ({Track.Value}) exceeds total tracks ({TotalTracks.Value})", "Set TotalTracks to at least the track number");

		if (DiscNumber.HasValue && TotalDiscs.HasValue && DiscNumber.Value > TotalDiscs.Value)
			result.AddWarning ("DiscNumber", $"Disc number ({DiscNumber.Value}) exceeds total discs ({TotalDiscs.Value})", "Set TotalDiscs to at least the disc number");

		// Validate year
		if (!string.IsNullOrEmpty (Year)) {
			var yearSpan = Year!.AsSpan ();
			var yearPart = yearSpan.Slice (0, Math.Min (4, yearSpan.Length));
#if NETSTANDARD2_0
			if (int.TryParse (yearPart.ToString (), out var yearNum)) {
#else
			if (int.TryParse (yearPart, out var yearNum)) {
#endif
				if (yearNum < 1000 || yearNum > 2200)
					result.AddWarning ("Year", $"Year ({Year}) is outside reasonable range (1000-2200)");
			}
		}

		// Validate ISRC format (12 characters: CC-XXX-YY-NNNNN or CCXXXYYNNNNN)
		if (!string.IsNullOrEmpty (Isrc)) {
#if NETSTANDARD2_0
			var isrc = Isrc!.Replace ("-", "").Trim ();
#else
			var isrc = Isrc!.Replace ("-", "", StringComparison.Ordinal).Trim ();
#endif
			if (isrc.Length != 12)
				result.AddError ("ISRC", $"ISRC must be 12 characters (found {Isrc.Length})", "Format: CCXXXYYNNNNN");
		}

		// Validate BPM
		if (BeatsPerMinute.HasValue && (BeatsPerMinute.Value < 20 || BeatsPerMinute.Value > 999))
			result.AddWarning ("BeatsPerMinute", $"BPM ({BeatsPerMinute.Value}) is outside typical range (20-999)");

		return result;
	}

	/// <summary>
	/// Copies metadata from this tag to another tag.
	/// </summary>
	/// <param name="target">The target tag to copy metadata to.</param>
	/// <param name="options">Options controlling what metadata to copy.</param>
	/// <exception cref="ArgumentNullException">Thrown if target is null.</exception>
	/// <remarks>
	/// <para>
	/// Only non-null values are copied. The target tag's existing values are overwritten
	/// for any property that has a non-null value in the source.
	/// </para>
	/// <para>
	/// This is useful for converting between tag formats or synchronizing metadata
	/// between multiple tag types in the same file.
	/// </para>
	/// </remarks>
	public void CopyTo (Tag target, TagCopyOptions options = TagCopyOptions.All)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (target is null)
			throw new ArgumentNullException (nameof (target));
#else
		ArgumentNullException.ThrowIfNull (target);
#endif

		// Basic metadata (always copied unless explicitly excluded)
		if ((options & TagCopyOptions.Basic) != 0) {
			if (Title is not null)
				target.Title = Title;
			if (Artist is not null)
				target.Artist = Artist;
			if (Album is not null)
				target.Album = Album;
			if (Year is not null)
				target.Year = Year;
			if (Comment is not null)
				target.Comment = Comment;
			if (Genre is not null)
				target.Genre = Genre;
			if (Track.HasValue)
				target.Track = Track;
		}

		// Extended metadata
		if ((options & TagCopyOptions.Extended) != 0) {
			// Arrays
			if (Performers is { Length: > 0 })
				target.Performers = Performers;
			if (AlbumArtists is { Length: > 0 })
				target.AlbumArtists = AlbumArtists;
			if (Composers is { Length: > 0 })
				target.Composers = Composers;
			if (Genres is { Length: > 0 })
				target.Genres = Genres;

			// Extended single values
			if (AlbumArtist is not null)
				target.AlbumArtist = AlbumArtist;
			if (Composer is not null)
				target.Composer = Composer;
			if (Conductor is not null)
				target.Conductor = Conductor;
			if (Copyright is not null)
				target.Copyright = Copyright;
			if (DiscNumber.HasValue)
				target.DiscNumber = DiscNumber;
			if (TotalDiscs.HasValue)
				target.TotalDiscs = TotalDiscs;
			if (TotalTracks.HasValue)
				target.TotalTracks = TotalTracks;
			if (BeatsPerMinute.HasValue)
				target.BeatsPerMinute = BeatsPerMinute;
			if (Lyrics is not null)
				target.Lyrics = Lyrics;
			if (Isrc is not null)
				target.Isrc = Isrc;
			if (Publisher is not null)
				target.Publisher = Publisher;
			if (EncodedBy is not null)
				target.EncodedBy = EncodedBy;
			if (EncoderSettings is not null)
				target.EncoderSettings = EncoderSettings;
			if (Grouping is not null)
				target.Grouping = Grouping;
			if (Subtitle is not null)
				target.Subtitle = Subtitle;
			if (Remixer is not null)
				target.Remixer = Remixer;
			if (InitialKey is not null)
				target.InitialKey = InitialKey;
			if (Mood is not null)
				target.Mood = Mood;
			if (MediaType is not null)
				target.MediaType = MediaType;
			if (Language is not null)
				target.Language = Language;
			if (Barcode is not null)
				target.Barcode = Barcode;
			if (CatalogNumber is not null)
				target.CatalogNumber = CatalogNumber;
			if (OriginalReleaseDate is not null)
				target.OriginalReleaseDate = OriginalReleaseDate;
			if (Description is not null)
				target.Description = Description;
			if (DateTagged is not null)
				target.DateTagged = DateTagged;
			if (AmazonId is not null)
				target.AmazonId = AmazonId;
			if (IsCompilation)
				target.IsCompilation = true;

			// Classical music metadata
			if (Work is not null)
				target.Work = Work;
			if (Movement is not null)
				target.Movement = Movement;
			if (MovementNumber.HasValue)
				target.MovementNumber = MovementNumber;
			if (MovementTotal.HasValue)
				target.MovementTotal = MovementTotal;

			// Performers role
			if (PerformersRole is { Length: > 0 })
				target.PerformersRole = PerformersRole;
		}

		// Sort order
		if ((options & TagCopyOptions.SortOrder) != 0) {
			if (AlbumSort is not null)
				target.AlbumSort = AlbumSort;
			if (ArtistSort is not null)
				target.ArtistSort = ArtistSort;
			if (TitleSort is not null)
				target.TitleSort = TitleSort;
			if (AlbumArtistSort is not null)
				target.AlbumArtistSort = AlbumArtistSort;
			if (ComposerSort is not null)
				target.ComposerSort = ComposerSort;
			if (PerformersSort is { Length: > 0 })
				target.PerformersSort = PerformersSort;
			if (AlbumArtistsSort is { Length: > 0 })
				target.AlbumArtistsSort = AlbumArtistsSort;
			if (ComposersSort is { Length: > 0 })
				target.ComposersSort = ComposersSort;
		}

		// ReplayGain and R128
		if ((options & TagCopyOptions.ReplayGain) != 0) {
			if (ReplayGainTrackGain is not null)
				target.ReplayGainTrackGain = ReplayGainTrackGain;
			if (ReplayGainTrackPeak is not null)
				target.ReplayGainTrackPeak = ReplayGainTrackPeak;
			if (ReplayGainAlbumGain is not null)
				target.ReplayGainAlbumGain = ReplayGainAlbumGain;
			if (ReplayGainAlbumPeak is not null)
				target.ReplayGainAlbumPeak = ReplayGainAlbumPeak;
			if (R128TrackGain is not null)
				target.R128TrackGain = R128TrackGain;
			if (R128AlbumGain is not null)
				target.R128AlbumGain = R128AlbumGain;
		}

		// MusicBrainz IDs
		if ((options & TagCopyOptions.MusicBrainz) != 0) {
			if (MusicBrainzTrackId is not null)
				target.MusicBrainzTrackId = MusicBrainzTrackId;
			if (MusicBrainzReleaseId is not null)
				target.MusicBrainzReleaseId = MusicBrainzReleaseId;
			if (MusicBrainzArtistId is not null)
				target.MusicBrainzArtistId = MusicBrainzArtistId;
			if (MusicBrainzReleaseGroupId is not null)
				target.MusicBrainzReleaseGroupId = MusicBrainzReleaseGroupId;
			if (MusicBrainzAlbumArtistId is not null)
				target.MusicBrainzAlbumArtistId = MusicBrainzAlbumArtistId;
			if (MusicBrainzRecordingId is not null)
				target.MusicBrainzRecordingId = MusicBrainzRecordingId;
			if (MusicBrainzWorkId is not null)
				target.MusicBrainzWorkId = MusicBrainzWorkId;
			if (MusicBrainzDiscId is not null)
				target.MusicBrainzDiscId = MusicBrainzDiscId;
			if (MusicBrainzReleaseStatus is not null)
				target.MusicBrainzReleaseStatus = MusicBrainzReleaseStatus;
			if (MusicBrainzReleaseType is not null)
				target.MusicBrainzReleaseType = MusicBrainzReleaseType;
			if (MusicBrainzReleaseCountry is not null)
				target.MusicBrainzReleaseCountry = MusicBrainzReleaseCountry;
			if (AcoustIdId is not null)
				target.AcoustIdId = AcoustIdId;
			if (AcoustIdFingerprint is not null)
				target.AcoustIdFingerprint = AcoustIdFingerprint;
		}

		// Pictures
		if ((options & TagCopyOptions.Pictures) != 0) {
			if (Pictures is { Length: > 0 })
				target.Pictures = Pictures;
		}
	}
}

/// <summary>
/// Options controlling what metadata to copy when using <see cref="Tag.CopyTo"/>.
/// </summary>
[Flags]
public enum TagCopyOptions
{
	/// <summary>
	/// Copy no metadata.
	/// </summary>
	None = 0,

	/// <summary>
	/// Copy basic metadata: Title, Artist, Album, Year, Comment, Genre, Track.
	/// </summary>
	Basic = 1 << 0,

	/// <summary>
	/// Copy extended metadata: Disc info, composers, conductors, publishers, etc.
	/// </summary>
	Extended = 1 << 1,

	/// <summary>
	/// Copy sort order fields.
	/// </summary>
	SortOrder = 1 << 2,

	/// <summary>
	/// Copy ReplayGain values.
	/// </summary>
	ReplayGain = 1 << 3,

	/// <summary>
	/// Copy MusicBrainz identifiers.
	/// </summary>
	MusicBrainz = 1 << 4,

	/// <summary>
	/// Copy embedded pictures/artwork.
	/// </summary>
	Pictures = 1 << 5,

	/// <summary>
	/// Copy all metadata (default).
	/// </summary>
	All = Basic | Extended | SortOrder | ReplayGain | MusicBrainz | Pictures
}
