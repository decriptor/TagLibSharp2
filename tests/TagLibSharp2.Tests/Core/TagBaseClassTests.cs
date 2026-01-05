// Licensed under the MIT License.

namespace TagLibSharp2.Tests.Core;

using TagLibSharp2.Core;

/// <summary>
/// Tests for the Tag abstract base class default implementations.
/// Uses a minimal concrete implementation to test virtual property defaults.
/// </summary>
[TestClass]
public class TagBaseClassTests
{
	/// <summary>
	/// Minimal Tag implementation that only implements abstract members.
	/// All virtual members use the base class defaults.
	/// </summary>
	sealed class MinimalTag : Tag
	{
		public override TagTypes TagType => TagTypes.None;
		public override string? Title { get; set; }
		public override string? Artist { get; set; }
		public override string? Album { get; set; }
		public override string? Year { get; set; }
		public override string? Comment { get; set; }
		public override string? Genre { get; set; }
		public override uint? Track { get; set; }
		public override IPicture[] Pictures { get; set; } = [];
		public override BinaryData Render () => BinaryData.Empty;

		public override void Clear ()
		{
			Title = null;
			Artist = null;
			Album = null;
			Year = null;
			Comment = null;
			Genre = null;
			Track = null;
			Pictures = [];
		}
	}

	[TestMethod]
	public void Performers_DefaultImplementation_ReturnsArtistAsArray ()
	{
		var tag = new MinimalTag { Artist = "Test Artist" };

		var performers = tag.Performers;

		Assert.AreEqual (1, performers.Length);
		Assert.AreEqual ("Test Artist", performers[0]);
	}

	[TestMethod]
	public void Performers_DefaultImplementation_ReturnsEmptyWhenNoArtist ()
	{
		var tag = new MinimalTag { Artist = null };

		var performers = tag.Performers;

		Assert.AreEqual (0, performers.Length);
	}

	[TestMethod]
	public void Performers_DefaultImplementation_ReturnsEmptyWhenEmptyArtist ()
	{
		var tag = new MinimalTag { Artist = "" };

		var performers = tag.Performers;

		Assert.AreEqual (0, performers.Length);
	}

	[TestMethod]
	public void Performers_Set_DefaultImplementation_SetsArtist ()
	{
		var tag = new MinimalTag ();

		tag.Performers = ["Artist 1", "Artist 2"];

		Assert.AreEqual ("Artist 1", tag.Artist);
	}

	[TestMethod]
	public void Performers_SetEmpty_DefaultImplementation_ClearsArtist ()
	{
		var tag = new MinimalTag { Artist = "Original" };

		tag.Performers = [];

		Assert.IsNull (tag.Artist);
	}

	[TestMethod]
	public void Genres_DefaultImplementation_ReturnsGenreAsArray ()
	{
		var tag = new MinimalTag { Genre = "Rock" };

		var genres = tag.Genres;

		Assert.AreEqual (1, genres.Length);
		Assert.AreEqual ("Rock", genres[0]);
	}

	[TestMethod]
	public void Genres_DefaultImplementation_ReturnsEmptyWhenNoGenre ()
	{
		var tag = new MinimalTag { Genre = null };

		var genres = tag.Genres;

		Assert.AreEqual (0, genres.Length);
	}

	[TestMethod]
	public void Genres_Set_DefaultImplementation_SetsGenre ()
	{
		var tag = new MinimalTag ();

		tag.Genres = ["Rock", "Pop"];

		Assert.AreEqual ("Rock", tag.Genre);
	}

	[TestMethod]
	public void OriginalReleaseDate_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void OriginalReleaseDate_DefaultImplementation_SetIsNoOp ()
	{
		var tag = new MinimalTag ();

		tag.OriginalReleaseDate = "2020";

		// Default implementation ignores sets
		Assert.IsNull (tag.OriginalReleaseDate);
	}

	[TestMethod]
	public void AlbumArtist_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.AlbumArtist);
	}

	[TestMethod]
	public void AlbumArtists_DefaultImplementation_ReturnsEmptyArray ()
	{
		var tag = new MinimalTag ();

		var albumArtists = tag.AlbumArtists;

		Assert.AreEqual (0, albumArtists.Length);
	}

	[TestMethod]
	public void AlbumArtists_Set_DefaultImplementation_SetsAlbumArtist ()
	{
		var tag = new MinimalTag ();

		tag.AlbumArtists = ["Various Artists"];

		// Default implementation forwards to AlbumArtist which is also default (no-op)
		Assert.IsNull (tag.AlbumArtist);
	}

	[TestMethod]
	public void DiscNumber_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.DiscNumber);
	}

	[TestMethod]
	public void Composer_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Composer);
	}

	[TestMethod]
	public void Composers_DefaultImplementation_ReturnsEmptyArray ()
	{
		var tag = new MinimalTag ();

		var composers = tag.Composers;

		Assert.AreEqual (0, composers.Length);
	}

	[TestMethod]
	public void Composers_Set_DefaultImplementation_SetsComposer ()
	{
		var tag = new MinimalTag ();

		tag.Composers = ["Bach", "Mozart"];

		// Default implementation forwards to Composer which is also default (no-op)
		Assert.IsNull (tag.Composer);
	}

	[TestMethod]
	public void BeatsPerMinute_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.BeatsPerMinute);
	}

	[TestMethod]
	public void Conductor_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Conductor);
	}

	[TestMethod]
	public void Work_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Work);
	}

	[TestMethod]
	public void Movement_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Movement);
	}

	[TestMethod]
	public void MovementNumber_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MovementNumber);
	}

	[TestMethod]
	public void MovementTotal_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MovementTotal);
	}

	[TestMethod]
	public void Copyright_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Copyright);
	}

	[TestMethod]
	public void Publisher_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Publisher);
	}

	[TestMethod]
	public void Lyrics_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.Lyrics);
	}

	[TestMethod]
	public void IsCompilation_DefaultImplementation_ReturnsFalse ()
	{
		var tag = new MinimalTag ();

		Assert.IsFalse (tag.IsCompilation);
	}

	[TestMethod]
	public void MusicBrainzReleaseId_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void MusicBrainzArtistId_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MusicBrainzArtistId);
	}

	[TestMethod]
	public void MusicBrainzTrackId_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MusicBrainzTrackId);
	}

	[TestMethod]
	public void MusicBrainzReleaseGroupId_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void MusicBrainzDiscId_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.MusicBrainzDiscId);
	}

	[TestMethod]
	public void ReplayGainTrackGain_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.ReplayGainTrackGain);
	}

	[TestMethod]
	public void ReplayGainTrackPeak_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.ReplayGainTrackPeak);
	}

	[TestMethod]
	public void ReplayGainAlbumGain_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.ReplayGainAlbumGain);
	}

	[TestMethod]
	public void ReplayGainAlbumPeak_DefaultImplementation_ReturnsNull ()
	{
		var tag = new MinimalTag ();

		Assert.IsNull (tag.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void Clear_ResetsAllFields ()
	{
		var tag = new MinimalTag {
			Title = "Test",
			Artist = "Artist",
			Album = "Album",
			Year = "2024",
			Comment = "Comment",
			Genre = "Rock",
			Track = 5
		};

		tag.Clear ();

		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Artist);
		Assert.IsNull (tag.Album);
		Assert.IsNull (tag.Year);
		Assert.IsNull (tag.Comment);
		Assert.IsNull (tag.Genre);
		Assert.IsNull (tag.Track);
	}

	[TestMethod]
	public void IsEmpty_ReturnsTrueWhenAllFieldsAreEmpty ()
	{
		var tag = new MinimalTag ();

		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_ReturnsFalseWhenTitleIsSet ()
	{
		var tag = new MinimalTag { Title = "Test" };

		Assert.IsFalse (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_ReturnsFalseWhenArtistIsSet ()
	{
		var tag = new MinimalTag { Artist = "Test" };

		Assert.IsFalse (tag.IsEmpty);
	}
}
