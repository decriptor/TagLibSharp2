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
