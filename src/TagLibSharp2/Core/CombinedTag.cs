// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// A tag facade that exposes a unified view over multiple underlying <see cref="Tag"/>
/// instances in priority order.
/// </summary>
/// <remarks>
/// <para>
/// Getters return the first non-null/non-empty value from the ordered list of tags.
/// Pictures are unioned across members and deduplicated on (PictureType, MimeType,
/// PictureData).
/// </para>
/// <para>
/// Writes are no-ops on this base facade; callers wanting mutation should operate
/// on the underlying tags directly. Format-specific subclasses (e.g. the FLAC tag
/// view) override individual members to forward writes to the appropriate storage.
/// </para>
/// <para>
/// The motivating case is an MP3 file that carries both an ID3v2 tag
/// (https://id3.org/id3v2.4.0-structure) and an ID3v1 tag (https://id3.org/ID3v1).
/// ID3v2 is authoritative when both are present, but ID3v1-only fields must still
/// surface through the unified view so legacy data is not silently lost.
/// </para>
/// </remarks>
public class CombinedTag : Tag
{
	readonly Tag?[] _tags;

	/// <summary>
	/// Initializes a new <see cref="CombinedTag"/> with the supplied tags in
	/// priority order. The first non-null tag's value wins for each field.
	/// </summary>
	/// <param name="tags">Tags in priority order. Null entries are allowed and ignored.</param>
	public CombinedTag (params Tag?[] tags)
	{
		_tags = tags ?? [];
	}

	/// <summary>
	/// Gets the underlying tags in priority order. Null entries are preserved.
	/// </summary>
	protected IReadOnlyList<Tag?> Tags => _tags;

	T? FirstNonDefault<T> (Func<Tag, T?> selector, Func<T?, bool> isDefault)
	{
		foreach (var tag in _tags) {
			if (tag is null)
				continue;
			var value = selector (tag);
			if (!isDefault (value))
				return value;
		}
		return default;
	}

	string? FirstNonEmptyString (Func<Tag, string?> selector) =>
		FirstNonDefault (selector, string.IsNullOrEmpty);

	uint? FirstNonNullUInt (Func<Tag, uint?> selector) =>
		FirstNonDefault<uint?> (selector, v => !v.HasValue);

	/// <inheritdoc/>
	public override TagTypes TagType {
		get {
			var types = TagTypes.None;
			foreach (var tag in _tags) {
				if (tag is not null)
					types |= tag.TagType;
			}
			return types;
		}
	}

	/// <inheritdoc/>
	public override string? Title {
		get => FirstNonEmptyString (t => t.Title);
		set { }
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => FirstNonEmptyString (t => t.Artist);
		set { }
	}

	/// <inheritdoc/>
	public override string? Album {
		get => FirstNonEmptyString (t => t.Album);
		set { }
	}

	/// <inheritdoc/>
	public override string? Year {
		get => FirstNonEmptyString (t => t.Year);
		set { }
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => FirstNonEmptyString (t => t.Comment);
		set { }
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => FirstNonEmptyString (t => t.Genre);
		set { }
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => FirstNonNullUInt (t => t.Track);
		set { }
	}

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override IPicture[] Pictures {
		get {
			var merged = new List<IPicture> ();
			var seen = new HashSet<(PictureType, string, BinaryData)> ();
			foreach (var tag in _tags) {
				if (tag is null)
					continue;
				foreach (var p in tag.Pictures) {
					if (seen.Add ((p.PictureType, p.MimeType, p.PictureData)))
						merged.Add (p);
				}
			}
			return [.. merged];
		}
		set { }
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override BinaryData Render () => BinaryData.Empty;

	/// <inheritdoc/>
	public override void Clear () { }
}
