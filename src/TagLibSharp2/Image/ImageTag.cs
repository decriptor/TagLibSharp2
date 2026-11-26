// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Image;

[Flags]
enum ImageStores
{
	None = 0,
	Title = 1 << 0,
	Artist = 1 << 1,
	Description = 1 << 2,
	Copyright = 1 << 3,
	Year = 1 << 4,
	Comment = 1 << 5,
	Png = Title | Artist | Description | Copyright | Year | Comment,
	CommentOnly = Comment
}

/// <summary>
/// Text stored in an image file. PNG keeps the standard text keywords.
/// GIF and JPEG keep the comment. A field the format does not store reads
/// back as null, and assigning it changes nothing.
/// </summary>
sealed class ImageTag : Tag
{
	readonly ImageStores _stores;
	ImageStores _dirty;
	string? _title;
	string? _artist;
	string? _description;
	string? _copyright;
	string? _year;
	string? _comment;

	internal ImageTag (MediaFormat format, ImageStores stores)
	{
		Format = format;
		_stores = stores;
	}

	internal MediaFormat Format { get; }

	/// <summary>Clear asked the rewriter to drop text it does not map onto a field.</summary>
	internal bool DropUnmapped { get; private set; }

	internal bool AffectsFile => _dirty != 0 || DropUnmapped;

	internal bool Changed (ImageStores field) => (_dirty & field) != 0;

	internal string? Field (ImageStores field) => field switch {
		ImageStores.Title => _title,
		ImageStores.Artist => _artist,
		ImageStores.Description => _description,
		ImageStores.Copyright => _copyright,
		ImageStores.Year => _year,
		ImageStores.Comment => _comment,
		_ => null
	};

	internal void ReadField (ImageStores field, string? value)
	{
		if (field == ImageStores.None || (_stores & field) == 0)
			return;
		Assign (field, string.IsNullOrEmpty (value) ? null : value, markDirty: false);
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Image;

	/// <inheritdoc/>
	public override string? Title {
		get => _title;
		set => Assign (ImageStores.Title, value, markDirty: true);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _artist;
		set => Assign (ImageStores.Artist, value, markDirty: true);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => null;
		set { }
	}

	/// <inheritdoc/>
	public override string? Year {
		get => _year;
		set => Assign (ImageStores.Year, value, markDirty: true);
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => _comment;
		set => Assign (ImageStores.Comment, value, markDirty: true);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => null;
		set { }
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => null;
		set { }
	}

	/// <inheritdoc/>
	public override string? Description {
		get => _description;
		set => Assign (ImageStores.Description, value, markDirty: true);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => _copyright;
		set => Assign (ImageStores.Copyright, value, markDirty: true);
	}

	/// <inheritdoc/>
	public override bool IsEmpty =>
		base.IsEmpty
		&& string.IsNullOrEmpty (_description)
		&& string.IsNullOrEmpty (_copyright);

	/// <inheritdoc/>
	public override BinaryData Render () => new (ImageRewrite.RenderTag (this));

	/// <inheritdoc/>
	public override void Clear ()
	{
		DropUnmapped = true;
		ClearStored (ImageStores.Title);
		ClearStored (ImageStores.Artist);
		ClearStored (ImageStores.Description);
		ClearStored (ImageStores.Copyright);
		ClearStored (ImageStores.Year);
		ClearStored (ImageStores.Comment);
	}

	void ClearStored (ImageStores field)
	{
		if (Field (field) is null)
			return;
		Assign (field, null, markDirty: true);
	}

	void Assign (ImageStores field, string? value, bool markDirty)
	{
		if ((_stores & field) == 0)
			return;
		value = EmptyAsNull (value);
		if (string.Equals (Field (field), value, StringComparison.Ordinal))
			return;

		switch (field) {
			case ImageStores.Title:
				_title = value;
				break;
			case ImageStores.Artist:
				_artist = value;
				break;
			case ImageStores.Description:
				_description = value;
				break;
			case ImageStores.Copyright:
				_copyright = value;
				break;
			case ImageStores.Year:
				_year = value;
				break;
			case ImageStores.Comment:
				_comment = value;
				break;
			default:
				return;
		}

		if (markDirty && (_stores & field) != 0)
			_dirty |= field;
	}

	static string? EmptyAsNull (string? value) => string.IsNullOrEmpty (value) ? null : value;
}
