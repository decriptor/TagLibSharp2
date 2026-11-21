// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Aiff;

/// <summary>
/// Read view over an AIFF file's ID3 tag and native text chunks.
/// Comment, Title, Artist, and Copyright prefer ID3. Other fields are ID3 only.
/// </summary>
sealed class AiffTag : Tag
{
	readonly AiffFile _file;

	internal AiffTag (AiffFile file) => _file = file;

	/// <inheritdoc/>
	public override TagTypes TagType => _file.Id3v2Tag?.TagType ?? TagTypes.None;

	/// <inheritdoc/>
	public override string? Title {
		get => _file.Title;
		set => _file.Title = value;
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => _file.Artist;
		set => _file.Artist = value;
	}

	/// <inheritdoc/>
	public override string? Album {
		get => Id3?.Album;
		set => Ensure ().Album = value;
	}

	/// <inheritdoc/>
	public override string? Year {
		get => Id3?.Year;
		set => Ensure ().Year = value;
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => _file.Comment;
		set => _file.Comment = value;
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => Id3?.Genre;
		set => Ensure ().Genre = value;
	}

	/// <inheritdoc/>
	public override uint? Track {
		get => Id3?.Track;
		set => Ensure ().Track = value;
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => _file.Copyright;
		set => _file.Copyright = value;
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => Id3?.AlbumArtist;
		set => Ensure ().AlbumArtist = value;
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => Id3?.Composer;
		set => Ensure ().Composer = value;
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => Id3?.Conductor;
		set => Ensure ().Conductor = value;
	}

	/// <inheritdoc/>
	public override string? Work {
		get => Id3?.Work;
		set => Ensure ().Work = value;
	}

	/// <inheritdoc/>
	public override string? Movement {
		get => Id3?.Movement;
		set => Ensure ().Movement = value;
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => Id3?.Lyrics;
		set => Ensure ().Lyrics = value;
	}

	/// <inheritdoc/>
	public override string? Grouping {
		get => Id3?.Grouping;
		set => Ensure ().Grouping = value;
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get => Id3?.DiscNumber;
		set => Ensure ().DiscNumber = value;
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get => Id3?.TotalTracks;
		set => Ensure ().TotalTracks = value;
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get => Id3?.TotalDiscs;
		set => Ensure ().TotalDiscs = value;
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get => Id3?.BeatsPerMinute;
		set => Ensure ().BeatsPerMinute = value;
	}

	/// <inheritdoc/>
	public override uint? MovementNumber {
		get => Id3?.MovementNumber;
		set => Ensure ().MovementNumber = value;
	}

	/// <inheritdoc/>
	public override uint? MovementTotal {
		get => Id3?.MovementTotal;
		set => Ensure ().MovementTotal = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => Id3?.ReplayGainTrackGain;
		set => Ensure ().ReplayGainTrackGain = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => Id3?.ReplayGainTrackPeak;
		set => Ensure ().ReplayGainTrackPeak = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => Id3?.ReplayGainAlbumGain;
		set => Ensure ().ReplayGainAlbumGain = value;
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => Id3?.ReplayGainAlbumPeak;
		set => Ensure ().ReplayGainAlbumPeak = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => Id3?.MusicBrainzTrackId;
		set => Ensure ().MusicBrainzTrackId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => Id3?.MusicBrainzReleaseId;
		set => Ensure ().MusicBrainzReleaseId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => Id3?.MusicBrainzArtistId;
		set => Ensure ().MusicBrainzArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => Id3?.MusicBrainzReleaseGroupId;
		set => Ensure ().MusicBrainzReleaseGroupId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => Id3?.MusicBrainzAlbumArtistId;
		set => Ensure ().MusicBrainzAlbumArtistId = value;
	}

	/// <inheritdoc/>
	public override string? MusicBrainzRecordingId {
		get => Id3?.MusicBrainzRecordingId;
		set => Ensure ().MusicBrainzRecordingId = value;
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => Id3?.IsCompilation ?? false;
		set => Ensure ().IsCompilation = value;
	}

	/// <inheritdoc/>
#pragma warning disable CA1819
	public override string[] Performers {
		get {
			if (Id3?.Performers is { Length: > 0 } people)
				return people;
			var artist = _file.Artist;
			return string.IsNullOrEmpty (artist) ? [] : [artist!];
		}
		set => Ensure ().Performers = value;
	}

	/// <inheritdoc/>
	public override string[] Genres {
		get => Id3?.Genres ?? [];
		set => Ensure ().Genres = value;
	}

	/// <inheritdoc/>
	public override IPicture[] Pictures {
		get => Id3?.Pictures ?? [];
		set => Ensure ().Pictures = value;
	}
#pragma warning restore CA1819

	/// <inheritdoc/>
	public override bool IsEmpty =>
		base.IsEmpty && string.IsNullOrEmpty (Copyright);

	/// <inheritdoc/>
	public override BinaryData Render () =>
		throw new NotSupportedException (
			"AiffTag is a view over an AIFF file. Render the owning AiffFile instead.");

	/// <inheritdoc/>
	public override void Clear ()
	{
		_file.Comment = null;
		_file.Title = null;
		_file.Artist = null;
		_file.Copyright = null;
		_file.Id3v2Tag?.Clear ();
	}

	Id3v2Tag? Id3 => _file.Id3v2Tag;

	Id3v2Tag Ensure () => _file.EnsureId3 ();
}
