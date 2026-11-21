// Copyright (c) 2025-2026 Stephen Shaw and contributors
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
	[ThreadStatic]
	static int t_copyDepth;

	/// <summary>
	/// True when a setter is handed exactly the value its getter returned, compared ordinally, as when an
	/// editor writes back what it read. The setter then leaves the tag alone, so the other values of a
	/// multi-value field and any fallback field stay. Never true for null, which always clears, or while
	/// <see cref="CopyTo"/> runs on this thread: a copy replaces the target's value even when it equals the
	/// target's first value.
	/// </summary>
	internal static bool IsWriteBack (string? value, string? current) =>
		!IsCopying && IsValueRead (value, current);

	/// <summary>
	/// True when <paramref name="value"/> is not null and equals <paramref name="current"/> ordinally.
	/// </summary>
	internal static bool IsValueRead (string? value, string? current) =>
		value is not null && string.Equals (value, current, StringComparison.Ordinal);

	/// <summary>
	/// True while <see cref="CopyTo"/> runs on this thread.
	/// </summary>
	internal static bool IsCopying => t_copyDepth > 0;

	/// <inheritdoc cref="IsWriteBack(string?, string?)"/>
	internal static bool IsWriteBack (uint? value, uint? current) =>
		!IsCopying && value is not null && value == current;

	/// <summary>
	/// Gets the type of tag represented by this instance.
	/// </summary>
	public abstract TagTypes TagType { get; }

	/// <summary>
	/// Gets or sets the title/song name.
	/// </summary>
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Truncated to 30 bytes. Latin-1 encoding only (Unicode characters are lost).</description></item>
	/// <item><description>ID3v2, Vorbis, MP4, ASF: Full Unicode support, no practical length limit.</description></item>
	/// </list>
	/// </remarks>
	public abstract string? Title { get; set; }

	/// <summary>
	/// Gets or sets the primary artist/performer.
	/// </summary>
	/// <remarks>
	/// <para>This returns the first performer. For multiple performers, use <see cref="Performers"/>.
	/// In ID3v2, APE, and Vorbis Comments, writing back the value read keeps the other names; any other value, and any value <see cref="CopyTo"/> writes, replaces the list.</para>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Truncated to 30 bytes. Latin-1 encoding only (Unicode characters are lost).</description></item>
	/// <item><description>ID3v2, Vorbis, MP4, ASF: Full Unicode support, no practical length limit.</description></item>
	/// </list>
	/// </remarks>
	public abstract string? Artist { get; set; }

	/// <summary>
	/// Gets or sets the performers/artists for this track.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the primary array of artists. The <see cref="Artist"/> property returns
	/// the first value from this array.
	/// </para>
	/// <para>
	/// <b>Design Note:</b> This property uses an array type for compatibility with
	/// the original TagLib# API and to support multiple artists naturally. While
	/// <c>IReadOnlyList&lt;string&gt;</c> would be more idiomatic in modern C#,
	/// the array type ensures drop-in compatibility for existing TagLib# users
	/// and matches common tagging conventions (separator-split artist strings).
	/// </para>
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
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Truncated to 30 bytes. Latin-1 encoding only (Unicode characters are lost).</description></item>
	/// <item><description>ID3v2, Vorbis, MP4, ASF: Full Unicode support, no practical length limit.</description></item>
	/// </list>
	/// </remarks>
	public abstract string? Album { get; set; }

	/// <summary>
	/// Gets or sets the year of release.
	/// </summary>
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Limited to 4 characters (e.g., "2024"). Longer values are truncated.</description></item>
	/// <item><description>ID3v2, Vorbis, MP4, ASF: Supports full date strings (e.g., "2024-12-25").</description></item>
	/// </list>
	/// </remarks>
	public abstract string? Year { get; set; }

	/// <summary>
	/// Gets the release date <see cref="CopyTo"/> writes into <see cref="Year"/>.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="Year"/>. A format whose public year is only four digits
	/// overrides this to carry a full timestamp. Same-assembly subclasses only.
	/// </remarks>
	private protected virtual string? CopyableDate => Year;

	/// <summary>
	/// Reads <see cref="CopyableDate"/> from another tag in this assembly.
	/// </summary>
	internal string? ReadCopyableDate () => CopyableDate;

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
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Truncated to 28 bytes (or 30 if no track number). Latin-1 encoding only.</description></item>
	/// <item><description>ID3v2, Vorbis, MP4, ASF: Full Unicode support, no practical length limit.</description></item>
	/// </list>
	/// </remarks>
	public abstract string? Comment { get; set; }

	/// <summary>
	/// Gets or sets the genre name.
	/// </summary>
	/// <remarks>
	/// <para>This returns the first genre. For multiple genres, use <see cref="Genres"/>.
	/// In ID3v2, APE, and Vorbis Comments, writing back the value read keeps the other names; any other value, and any value <see cref="CopyTo"/> writes, replaces the list.</para>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Single genre only, must match one of 192 predefined genres. Custom genres not supported.</description></item>
	/// <item><description>ID3v2, Vorbis, MP4, ASF: Full Unicode support, multiple genres, custom genres allowed.</description></item>
	/// </list>
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
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Limited to 0-255. Values above 255 are silently clamped.</description></item>
	/// <item><description>MP4: Limited to 0-65535 (16-bit). Values above are silently clamped.</description></item>
	/// <item><description>ID3v2, Vorbis, ASF: Full 32-bit range supported.</description></item>
	/// </list>
	/// <para>
	/// <b>Design Note:</b> This property uses <c>uint?</c> (unsigned 32-bit integer)
	/// rather than <c>int?</c> to enforce non-negative semantics at the type level.
	/// Track numbers are inherently non-negative values, and using unsigned types
	/// prevents accidental negative values. The nullable wrapper indicates when no
	/// track number has been set.
	/// </para>
	/// </remarks>
	public abstract uint? Track { get; set; }

	/// <summary>
	/// Gets or sets the album artist (for compilations/various artists albums).
	/// </summary>
	/// <remarks>
	/// This returns the first album artist. For multiple album artists, use <see cref="AlbumArtists"/>.
	/// In ID3v2, APE, and Vorbis Comments, writing back the value read keeps the other names; any other value, and any value <see cref="CopyTo"/> writes, replaces the list.
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
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Not supported (returns null).</description></item>
	/// <item><description>MP4: Limited to 0-65535 (16-bit). Values above are silently clamped.</description></item>
	/// <item><description>ID3v2, Vorbis, ASF: Full 32-bit range supported.</description></item>
	/// </list>
	/// <para>Not all tag formats support this field. Default implementation returns null.</para>
	/// </remarks>
	public virtual uint? DiscNumber { get => null; set { } }

	/// <summary>
	/// Gets or sets the composer.
	/// </summary>
	/// <remarks>
	/// This returns the first composer. For multiple composers, use <see cref="Composers"/>.
	/// In ID3v2, APE, and Vorbis Comments, writing back the value read keeps the other names; any other value, and any value <see cref="CopyTo"/> writes, replaces the list.
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
	/// In ID3v2, this is read from Apple's MVNM frame, then a TXXX frame with description "MOVEMENT".
	/// A write stores the name in both, and a clear removes both.
	/// In Vorbis Comments, this is stored as MOVEMENT or MOVEMENTNAME; a new value also replaces an
	/// existing MOVEMENTNAME, and writing back the value already read changes nothing.
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
	/// In ID3v2, this is read from the index half of Apple's MVIN frame ("2/4"), then a TXXX frame
	/// with description "MOVEMENTNUMBER". A write updates both and keeps the total half of MVIN.
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
	/// In ID3v2, this is read from the total half of Apple's MVIN frame ("2/4"), then a TXXX frame
	/// with description "MOVEMENTTOTAL". A write updates both and keeps the index half of MVIN.
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
	/// Gets or sets the podcast feed URL.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For podcasts, this is the RSS/Atom feed URL where the podcast can be subscribed.
	/// </para>
	/// <para>
	/// In ID3v2, this is stored in the Apple proprietary WFED frame.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// </remarks>
#pragma warning disable CA1056 // URI properties should not be strings - ID3v2 URLs may be malformed
	public virtual string? PodcastFeedUrl { get => null; set { } }
#pragma warning restore CA1056

	/// <summary>
	/// Gets or sets the total number of tracks on the album/disc.
	/// </summary>
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Not supported (returns null).</description></item>
	/// <item><description>MP4: Limited to 0-65535 (16-bit). Values above are silently clamped.</description></item>
	/// <item><description>ID3v2, Vorbis, ASF: Full 32-bit range supported.</description></item>
	/// </list>
	/// <para>Not all tag formats support this field. Default implementation returns null.</para>
	/// </remarks>
	public virtual uint? TotalTracks { get => null; set { } }

	/// <summary>
	/// Gets or sets the total number of discs in the set.
	/// </summary>
	/// <remarks>
	/// <para>Format-specific limitations:</para>
	/// <list type="bullet">
	/// <item><description>ID3v1: Not supported (returns null).</description></item>
	/// <item><description>MP4: Limited to 0-65535 (16-bit). Values above are silently clamped.</description></item>
	/// <item><description>ID3v2, Vorbis, ASF: Full 32-bit range supported.</description></item>
	/// </list>
	/// <para>Not all tag formats support this field. Default implementation returns null.</para>
	/// </remarks>
	public virtual uint? TotalDiscs { get => null; set { } }

	/// <summary>
	/// Gets or sets the disc subtitle.
	/// </summary>
	/// <remarks>
	/// Used for multi-disc albums where each disc has a different title
	/// (e.g., "Disc 1: The Early Years", "Disc 2: The Later Years").
	/// ID3v2 uses TSST frame. Vorbis uses DISCSUBTITLE field.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? DiscSubtitle { get => null; set { } }

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
				? ClampToQ78 (value.Value).ToString (CultureInfo.InvariantCulture)
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
				? ClampToQ78 (value.Value).ToString (CultureInfo.InvariantCulture)
				: null;
		}
	}

	/// <summary>
	/// Clamps a dB value to the valid Q7.8 fixed-point range for R128 gain.
	/// </summary>
	/// <param name="dB">The gain value in decibels.</param>
	/// <returns>A Q7.8 value clamped to short range (-32768 to 32767).</returns>
	static short ClampToQ78 (double dB)
	{
		var q78 = Math.Round (dB * 256.0);
		if (q78 > short.MaxValue)
			return short.MaxValue;
		if (q78 < short.MinValue)
			return short.MinValue;
		return (short)q78;
	}

	// MusicBrainz IDs

	/// <summary>
	/// Gets or sets the MusicBrainz Track ID (Recording MBID).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A UUID identifying this specific recording in the MusicBrainz database.
	/// Not all tag formats support this field. Default implementation returns null.
	/// </para>
	/// <para>
	/// <b>Design Note:</b> This property is named "TrackId" for historical compatibility
	/// with the original TagLib# and MusicBrainz naming conventions. In MusicBrainz terminology,
	/// a "track" is a recording as it appears on a specific release, while "recording"
	/// refers to the unique audio content. This property stores the Recording MBID,
	/// which is the standard identifier used by MusicBrainz Picard and other taggers.
	/// See also <see cref="MusicBrainzRecordingId"/> which stores the same identifier in
	/// formats that support it separately.
	/// </para>
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
	/// Only non-null values are copied. Every source value is read before any is written. The target's
	/// existing values are overwritten for any property that has a non-null value in the source, and a
	/// source value that equals the target's first value still replaces the target's list; a list the
	/// target already holds is left as it is.
	/// </para>
	/// <para>
	/// When the source is the target or reads from it (a file's combined <c>Tag</c> copied into one of its
	/// own tags), the copy only adds values the target does not show. A fallback field of its own beside a
	/// value equal to the copied one stays: Vorbis MOVEMENTNAME or ORIGINALYEAR, APE Date, and an
	/// ID3v2.3 TDOR beside TORY.
	/// </para>
	/// <para>
	/// This is useful for converting between tag formats or synchronizing metadata
	/// between multiple tag types in the same file.
	/// </para>
	/// </remarks>
	/// <example>
	/// <para><b>Copy all metadata between tags:</b></para>
	/// <code>
	/// // Copy all metadata from FLAC to MP3
	/// flacFile.Tag.CopyTo(mp3File.Tag);
	/// </code>
	///
	/// <para><b>Copy only basic metadata (title, artist, album):</b></para>
	/// <code>
	/// flacFile.Tag.CopyTo(mp3File.Tag, TagCopyOptions.Basic);
	/// </code>
	///
	/// <para><b>Copy everything except pictures (for space savings):</b></para>
	/// <code>
	/// flacFile.Tag.CopyTo(mp3File.Tag, TagCopyOptions.All &amp; ~TagCopyOptions.Pictures);
	/// </code>
	///
	/// <para><b>Copy only MusicBrainz identifiers:</b></para>
	/// <code>
	/// flacFile.Tag.CopyTo(mp3File.Tag, TagCopyOptions.Identifiers);
	/// </code>
	/// </example>
	public void CopyTo (Tag target, TagCopyOptions options = TagCopyOptions.All)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (target is null)
			throw new ArgumentNullException (nameof (target));
#else
		ArgumentNullException.ThrowIfNull (target);
#endif

		if (ReferenceEquals (this, target))
			return;

		var writes = SnapshotCopyWrites (options);

		// A source that reads from the target, or a target that forwards to the source, already shows
		// the target's values. Each is then a write-back and changes nothing (IsWriteBack), so only
		// values the target does not hold arrive and its lists stay whole.
		if (ReadsFrom (target) || target.ReadsFrom (this)) {
			foreach (var write in writes)
				write (target);
			return;
		}

		// Otherwise a copy makes the target hold the source's value, so the write-back no-op in the
		// setters is off for the duration, also for tags a wrapper target forwards to.
		t_copyDepth++;
		try {
			foreach (var write in writes)
				write (target);
		} finally {
			t_copyDepth--;
		}
	}

	/// <summary>
	/// True when this tag is <paramref name="tag"/> or reads its values from it, as a file's combined
	/// view reads from the tags it combines.
	/// </summary>
	internal virtual bool ReadsFrom (Tag tag) => ReferenceEquals (this, tag);

	/// <summary>
	/// Reads every value to copy before writing any, so a source that reads from the target (a file's
	/// combined view copied into one of its tags) sees the target as it was, not half-written.
	/// </summary>
	List<Action<Tag>> SnapshotCopyWrites (TagCopyOptions options)
	{
		var writes = new List<Action<Tag>> ();

		// Basic metadata (always copied unless explicitly excluded)
		if ((options & TagCopyOptions.Basic) != 0) {
			if (Title is not null)
				Queue (writes, Title, static (t, v) => t.Title = v);
			if (Artist is not null)
				Queue (writes, Artist, static (t, v) => t.Artist = v);
			if (Album is not null)
				Queue (writes, Album, static (t, v) => t.Album = v);
			var copyableDate = CopyableDate;
			if (copyableDate is not null)
				Queue (writes, copyableDate, static (t, v) => t.Year = v);
			if (Comment is not null)
				Queue (writes, Comment, static (t, v) => t.Comment = v);
			if (Genre is not null)
				Queue (writes, Genre, static (t, v) => t.Genre = v);
			if (Track.HasValue)
				Queue (writes, Track, static (t, v) => t.Track = v);
		}

		// Extended metadata
		if ((options & TagCopyOptions.Extended) != 0) {
			// Arrays
			if (Performers is { Length: > 0 })
				QueueList (writes, Performers, static t => t.Performers, static (t, v) => t.Performers = v);
			if (AlbumArtists is { Length: > 0 })
				QueueList (writes, AlbumArtists, static t => t.AlbumArtists, static (t, v) => t.AlbumArtists = v);
			if (Composers is { Length: > 0 })
				QueueList (writes, Composers, static t => t.Composers, static (t, v) => t.Composers = v);
			if (Genres is { Length: > 0 })
				QueueList (writes, Genres, static t => t.Genres, static (t, v) => t.Genres = v);

			// Extended single values
			// The singular value is the first entry. Writing it after the array
			// replaces the whole list with that one name.
			if (AlbumArtist is not null && AlbumArtists is not { Length: > 0 })
				Queue (writes, AlbumArtist, static (t, v) => t.AlbumArtist = v);
			if (Composer is not null && Composers is not { Length: > 0 })
				Queue (writes, Composer, static (t, v) => t.Composer = v);
			if (Conductor is not null)
				Queue (writes, Conductor, static (t, v) => t.Conductor = v);
			if (Copyright is not null)
				Queue (writes, Copyright, static (t, v) => t.Copyright = v);
			if (DiscNumber.HasValue)
				Queue (writes, DiscNumber, static (t, v) => t.DiscNumber = v);
			if (TotalDiscs.HasValue)
				Queue (writes, TotalDiscs, static (t, v) => t.TotalDiscs = v);
			if (DiscSubtitle is not null)
				Queue (writes, DiscSubtitle, static (t, v) => t.DiscSubtitle = v);
			if (TotalTracks.HasValue)
				Queue (writes, TotalTracks, static (t, v) => t.TotalTracks = v);
			if (BeatsPerMinute.HasValue)
				Queue (writes, BeatsPerMinute, static (t, v) => t.BeatsPerMinute = v);
			if (Lyrics is not null)
				Queue (writes, Lyrics, static (t, v) => t.Lyrics = v);
			if (Isrc is not null)
				Queue (writes, Isrc, static (t, v) => t.Isrc = v);
			if (Publisher is not null)
				Queue (writes, Publisher, static (t, v) => t.Publisher = v);
			if (EncodedBy is not null)
				Queue (writes, EncodedBy, static (t, v) => t.EncodedBy = v);
			if (EncoderSettings is not null)
				Queue (writes, EncoderSettings, static (t, v) => t.EncoderSettings = v);
			if (Grouping is not null)
				Queue (writes, Grouping, static (t, v) => t.Grouping = v);
			if (Subtitle is not null)
				Queue (writes, Subtitle, static (t, v) => t.Subtitle = v);
			if (Remixer is not null)
				Queue (writes, Remixer, static (t, v) => t.Remixer = v);
			if (InitialKey is not null)
				Queue (writes, InitialKey, static (t, v) => t.InitialKey = v);
			if (Mood is not null)
				Queue (writes, Mood, static (t, v) => t.Mood = v);
			if (MediaType is not null)
				Queue (writes, MediaType, static (t, v) => t.MediaType = v);
			if (Language is not null)
				Queue (writes, Language, static (t, v) => t.Language = v);
			if (Barcode is not null)
				Queue (writes, Barcode, static (t, v) => t.Barcode = v);
			if (CatalogNumber is not null)
				Queue (writes, CatalogNumber, static (t, v) => t.CatalogNumber = v);
			if (OriginalReleaseDate is not null)
				Queue (writes, OriginalReleaseDate, static (t, v) => t.OriginalReleaseDate = v);
			if (Description is not null)
				Queue (writes, Description, static (t, v) => t.Description = v);
			if (DateTagged is not null)
				Queue (writes, DateTagged, static (t, v) => t.DateTagged = v);
			if (AmazonId is not null)
				Queue (writes, AmazonId, static (t, v) => t.AmazonId = v);
			if (IsCompilation)
				Queue (writes, true, static (t, v) => t.IsCompilation = v);

			// Classical music metadata
			if (Work is not null)
				Queue (writes, Work, static (t, v) => t.Work = v);
			if (Movement is not null)
				Queue (writes, Movement, static (t, v) => t.Movement = v);
			if (MovementNumber.HasValue)
				Queue (writes, MovementNumber, static (t, v) => t.MovementNumber = v);
			if (MovementTotal.HasValue)
				Queue (writes, MovementTotal, static (t, v) => t.MovementTotal = v);

			// Performers role
			if (PerformersRole is { Length: > 0 })
				QueueList (writes, PerformersRole, static t => t.PerformersRole, static (t, v) => t.PerformersRole = v);
		}

		// Sort order
		if ((options & TagCopyOptions.SortOrder) != 0) {
			if (AlbumSort is not null)
				Queue (writes, AlbumSort, static (t, v) => t.AlbumSort = v);
			if (ArtistSort is not null)
				Queue (writes, ArtistSort, static (t, v) => t.ArtistSort = v);
			if (TitleSort is not null)
				Queue (writes, TitleSort, static (t, v) => t.TitleSort = v);
			if (AlbumArtistSort is not null)
				Queue (writes, AlbumArtistSort, static (t, v) => t.AlbumArtistSort = v);
			if (ComposerSort is not null)
				Queue (writes, ComposerSort, static (t, v) => t.ComposerSort = v);
			if (PerformersSort is { Length: > 0 })
				QueueList (writes, PerformersSort, static t => t.PerformersSort, static (t, v) => t.PerformersSort = v);
			if (AlbumArtistsSort is { Length: > 0 })
				QueueList (writes, AlbumArtistsSort, static t => t.AlbumArtistsSort, static (t, v) => t.AlbumArtistsSort = v);
			if (ComposersSort is { Length: > 0 })
				QueueList (writes, ComposersSort, static t => t.ComposersSort, static (t, v) => t.ComposersSort = v);
		}

		// ReplayGain and R128
		if ((options & TagCopyOptions.ReplayGain) != 0) {
			if (ReplayGainTrackGain is not null)
				Queue (writes, ReplayGainTrackGain, static (t, v) => t.ReplayGainTrackGain = v);
			if (ReplayGainTrackPeak is not null)
				Queue (writes, ReplayGainTrackPeak, static (t, v) => t.ReplayGainTrackPeak = v);
			if (ReplayGainAlbumGain is not null)
				Queue (writes, ReplayGainAlbumGain, static (t, v) => t.ReplayGainAlbumGain = v);
			if (ReplayGainAlbumPeak is not null)
				Queue (writes, ReplayGainAlbumPeak, static (t, v) => t.ReplayGainAlbumPeak = v);
			if (R128TrackGain is not null)
				Queue (writes, R128TrackGain, static (t, v) => t.R128TrackGain = v);
			if (R128AlbumGain is not null)
				Queue (writes, R128AlbumGain, static (t, v) => t.R128AlbumGain = v);
		}

		// MusicBrainz IDs
		if ((options & TagCopyOptions.MusicBrainz) != 0) {
			if (MusicBrainzTrackId is not null)
				Queue (writes, MusicBrainzTrackId, static (t, v) => t.MusicBrainzTrackId = v);
			if (MusicBrainzReleaseId is not null)
				Queue (writes, MusicBrainzReleaseId, static (t, v) => t.MusicBrainzReleaseId = v);
			if (MusicBrainzArtistId is not null)
				Queue (writes, MusicBrainzArtistId, static (t, v) => t.MusicBrainzArtistId = v);
			if (MusicBrainzReleaseGroupId is not null)
				Queue (writes, MusicBrainzReleaseGroupId, static (t, v) => t.MusicBrainzReleaseGroupId = v);
			if (MusicBrainzAlbumArtistId is not null)
				Queue (writes, MusicBrainzAlbumArtistId, static (t, v) => t.MusicBrainzAlbumArtistId = v);
			if (MusicBrainzRecordingId is not null)
				Queue (writes, MusicBrainzRecordingId, static (t, v) => t.MusicBrainzRecordingId = v);
			if (MusicBrainzWorkId is not null)
				Queue (writes, MusicBrainzWorkId, static (t, v) => t.MusicBrainzWorkId = v);
			if (MusicBrainzDiscId is not null)
				Queue (writes, MusicBrainzDiscId, static (t, v) => t.MusicBrainzDiscId = v);
			if (MusicBrainzReleaseStatus is not null)
				Queue (writes, MusicBrainzReleaseStatus, static (t, v) => t.MusicBrainzReleaseStatus = v);
			if (MusicBrainzReleaseType is not null)
				Queue (writes, MusicBrainzReleaseType, static (t, v) => t.MusicBrainzReleaseType = v);
			if (MusicBrainzReleaseCountry is not null)
				Queue (writes, MusicBrainzReleaseCountry, static (t, v) => t.MusicBrainzReleaseCountry = v);
			if (AcoustIdId is not null)
				Queue (writes, AcoustIdId, static (t, v) => t.AcoustIdId = v);
			if (AcoustIdFingerprint is not null)
				Queue (writes, AcoustIdFingerprint, static (t, v) => t.AcoustIdFingerprint = v);
		}

		// Pictures
		if ((options & TagCopyOptions.Pictures) != 0) {
			if (Pictures is { Length: > 0 })
				Queue (writes, Pictures, static (t, v) => t.Pictures = v);
		}
		return writes;
	}

	static void Queue<T> (List<Action<Tag>> writes, T value, Action<Tag, T> set) =>
		writes.Add (t => set (t, value));

	/// <summary>
	/// Queues a list write that is skipped when the target already holds exactly that list, so a copy
	/// of a list the target shows leaves its frames, items, or fields as they are.
	/// </summary>
	static void QueueList (List<Action<Tag>> writes, string[] value, Func<Tag, string[]?> get, Action<Tag, string[]> set) =>
		writes.Add (t => {
			if (get (t) is not { } current || !current.AsSpan ().SequenceEqual (value))
				set (t, value);
		});
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
