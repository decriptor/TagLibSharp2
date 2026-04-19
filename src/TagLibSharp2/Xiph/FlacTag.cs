// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Unified tag view over a <see cref="FlacFile"/>'s metadata.
/// </summary>
/// <remarks>
/// <para>
/// FLAC can store pictures in two spec-defined locations:
/// </para>
/// <list type="bullet">
///   <item>Native PICTURE metadata blocks (block type 6), per RFC 9639 §8.8.</item>
///   <item>METADATA_BLOCK_PICTURE fields inside the VORBIS_COMMENT block (base64-encoded),
///     per https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE.</item>
/// </list>
/// <para>
/// This class provides a single Tag view that surfaces both so callers do not need
/// to know FLAC's internal storage layout.
/// </para>
/// </remarks>
public sealed class FlacTag : Tag
{
	readonly FlacFile _file;

	internal FlacTag (FlacFile file)
	{
		_file = file;
	}

	/// <inheritdoc/>
	public override TagTypes TagType => TagTypes.Xiph | TagTypes.FlacMetadata;

	/// <inheritdoc/>
	public override string? Title {
		get => _file.VorbisComment?.Title;
		set => EnsureVorbisComment ().Title = value;
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _file.VorbisComment?.Artist;
		set => EnsureVorbisComment ().Artist = value;
	}

	/// <inheritdoc/>
	public override string? Album {
		get => _file.VorbisComment?.Album;
		set => EnsureVorbisComment ().Album = value;
	}

	/// <inheritdoc/>
	public override string? Year {
		get => _file.VorbisComment?.Year;
		set => EnsureVorbisComment ().Year = value;
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => _file.VorbisComment?.Comment;
		set => EnsureVorbisComment ().Comment = value;
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => _file.VorbisComment?.Genre;
		set => EnsureVorbisComment ().Genre = value;
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => _file.VorbisComment?.Track;
		set => EnsureVorbisComment ().Track = value;
	}

	VorbisComment EnsureVorbisComment () =>
		_file.VorbisComment ??= new VorbisComment ("TagLibSharp2");

	/// <inheritdoc/>
#pragma warning disable CA1819 // Properties should not return arrays - Tag API contract
	public override IPicture[] Pictures {
		get {
			var blockPictures = _file.Pictures;
			var embedded = _file.VorbisComment?.Pictures ?? [];
			var merged = new List<IPicture> (blockPictures.Count + embedded.Length);
			var seen = new HashSet<(PictureType, string, BinaryData)> ();
			foreach (var p in blockPictures) {
				if (seen.Add ((p.PictureType, p.MimeType, p.PictureData)))
					merged.Add (p);
			}
			foreach (var p in embedded) {
				if (seen.Add ((p.PictureType, p.MimeType, p.PictureData)))
					merged.Add (p);
			}
			return [.. merged];
		}
		set {
			_file.RemoveAllPictures ();
			_file.VorbisComment?.RemoveAllPictures ();
			if (value is null)
				return;
			foreach (var p in value) {
				var flacPic = p as FlacPicture ?? new FlacPicture (
					p.MimeType, p.PictureType, p.Description, p.PictureData, 0, 0, 0, 0);
				_file.AddPicture (flacPic);
			}
		}
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override BinaryData Render () => BinaryData.Empty;

	/// <inheritdoc/>
	public override void Clear () { }
}
