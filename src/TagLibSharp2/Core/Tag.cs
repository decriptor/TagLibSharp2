// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Abstract base class for all tag types (ID3v1, ID3v2, Vorbis Comments, etc.).
/// Provides common metadata properties with nullable support.
/// </summary>
public abstract class Tag
{
	/// <summary>
	/// Gets or sets the title/song name.
	/// </summary>
	public abstract string? Title { get; set; }

	/// <summary>
	/// Gets or sets the primary artist/performer.
	/// </summary>
	public abstract string? Artist { get; set; }

	/// <summary>
	/// Gets or sets the album/collection name.
	/// </summary>
	public abstract string? Album { get; set; }

	/// <summary>
	/// Gets or sets the year of release.
	/// </summary>
	public abstract string? Year { get; set; }

	/// <summary>
	/// Gets or sets the comment/description.
	/// </summary>
	public abstract string? Comment { get; set; }

	/// <summary>
	/// Gets or sets the genre name.
	/// </summary>
	public abstract string? Genre { get; set; }

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public abstract uint? Track { get; set; }

	/// <summary>
	/// Gets or sets the album artist (for compilations/various artists albums).
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? AlbumArtist { get => null; set { } }

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
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual string? Composer { get => null; set { } }

	/// <summary>
	/// Gets or sets the beats per minute (BPM) of the track.
	/// </summary>
	/// <remarks>
	/// Not all tag formats support this field. Default implementation returns null.
	/// </remarks>
	public virtual uint? BeatsPerMinute { get => null; set { } }

	#region ReplayGain

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

	#endregion

	#region MusicBrainz IDs

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

	#endregion

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
}
