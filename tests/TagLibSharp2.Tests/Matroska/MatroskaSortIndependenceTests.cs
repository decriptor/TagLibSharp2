// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Matroska;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Matroska;

// Invariant: setting or clearing AlbumArtistSort never changes ArtistSort, and the reverse, on every
// file shape, and neither changes a parent value. The album artist is the flat ALBUM_ARTIST when the
// file has one (the ffmpeg convention), else the album-level ARTIST, and its sort pairs with it.
public partial class MatroskaFileTests
{
	static readonly (string Name, Func<byte[]> Build)[] SortShapes = [
		("single-level nested", () => FileWith (Tags (
			Tag (50, Simple ("TITLE", "The Album"), SimpleSorted ("ARTIST", "The Band", "shared"))))),
		("single-level nested, two artists", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "A", "shared"), Simple ("ARTIST", "B"))))),
		("collection-level nested", () => FileWith (Tags (
			Tag (70, SimpleSorted ("ARTIST", "The Band", "shared")),
			Tag (50, Simple ("TITLE", "The Album"))))),
		("single-level nested and flat", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "shared"), Simple ("ARTIST_SORT", "flat artist"),
				Simple ("ALBUM_ARTIST_SORT", "flat album artist"))))),
		("single-level empty nested", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", ""), Simple ("ARTIST_SORT", "flat artist"))))),
		("single-level non-default nested", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "shared"), Simple ("ARTIST", "Die Band", isDefault: false))))),
		("album nested, track without artist", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "shared")),
			Tag (30, Simple ("TITLE", "The Song"))))),
		("album nested and flat, subtrack without artist", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "shared"), Simple ("ALBUM_ARTIST_SORT", "flat")),
			Tag (20, Simple ("TITLE", "The Part"))))),
		("collection nested, track without artist", () => FileWith (Tags (
			Tag (70, SimpleSorted ("ARTIST", "The Band", "collection")),
			Tag (30, Simple ("TITLE", "The Song"))))),
		("album and track nested", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "album")),
			Tag (30, SimpleSorted ("ARTIST", "Ada", "track"))))),
		("ffmpeg flat album artist", () => FileWith (Tags (
			Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort"),
				Simple ("ALBUM_ARTIST", "The Band"), Simple ("ALBUM_ARTIST_SORT", "Band, The"))))),
		("nested album artist name", () => FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "Ada", "ada"), SimpleSorted ("ALBUM_ARTIST", "The Band", "band"))))),
		("no artist", () => FileWith (Tags (Tag (50, Simple ("TITLE", "The Album"))))),
	];

	static byte[] SortShape (string name) => Array.Find (SortShapes, shape => shape.Name == name).Build ();

	[TestMethod]
	public void SortSet_AlbumArtistAndArtistSort_StayIndependentOnEveryShape ()
	{
		var operations = new (string Name, Action<MatroskaTag> Apply, Func<MatroskaTag, string?> Read, string? Expected)[] {
			("AlbumArtistSort = NEW", t => t.AlbumArtistSort = "NEW", t => t.AlbumArtistSort, "NEW"),
			("AlbumArtistSort = null", t => t.AlbumArtistSort = null, t => t.AlbumArtistSort, null),
			("ArtistSort = NEW", t => t.ArtistSort = "NEW", t => t.ArtistSort, "NEW"),
			("ArtistSort = null", t => t.ArtistSort = null, t => t.ArtistSort, null),
			("AlbumArtistSort = empty", t => t.AlbumArtistSort = "", t => t.AlbumArtistSort, null),
			("ArtistSort = empty", t => t.ArtistSort = "", t => t.ArtistSort, null),
		};

		foreach (var (shape, build) in SortShapes) {
			foreach (var (name, apply, read, expected) in operations) {
				var label = $"{shape}: {name}";
				var tag = MatroskaFile.Read (build ()).File!.Tag;
				var before = SortState.Of (tag);

				apply (tag);

				Assert.AreEqual (expected, read (tag), label);
				var after = SortState.Of (tag);
				var reread = SortState.Of (MatroskaFile.Read (FileWith (tag.Render ().ToArray ())).File!.Tag);
				Assert.AreEqual (after, reread, $"{label}: render");
				var expectedState = name.StartsWith ("AlbumArtistSort", StringComparison.Ordinal)
					? before with { AlbumArtistSort = expected }
					: before with { ArtistSort = expected };
				Assert.AreEqual (expectedState, after, label);
			}
		}
	}

	[TestMethod]
	public void SortSet_Empty_ClearsLikeNull_OnEverySortName ()
	{
		var setters = new (string Name, Action<MatroskaTag, string?> Set, Func<MatroskaTag, string?> Get)[] {
			("TitleSort", (t, v) => t.TitleSort = v, t => t.TitleSort),
			("ArtistSort", (t, v) => t.ArtistSort = v, t => t.ArtistSort),
			("AlbumSort", (t, v) => t.AlbumSort = v, t => t.AlbumSort),
			("AlbumArtistSort", (t, v) => t.AlbumArtistSort = v, t => t.AlbumArtistSort),
			("ComposerSort", (t, v) => t.ComposerSort = v, t => t.ComposerSort),
		};
		static byte[] Build () => FileWith (Tags (
			Tag (70, Simple ("ARTIST_SORT", "f70"), Simple ("ALBUM_ARTIST_SORT", "f70"), Simple ("COMPOSER_SORT", "f70")),
			Tag (50,
				SimpleSorted ("TITLE", "The Album", "album"), SimpleSorted ("ARTIST", "The Band", "band"),
				Simple ("ARTIST_SORT", "f50"), Simple ("ALBUM_ARTIST_SORT", "f50"), Simple ("ALBUM_SORT", "f50"),
				SimpleSorted ("COMPOSER", "Bach", "bach"), Simple ("COMPOSER_SORT", "f50")),
			Tag (30, SimpleSorted ("TITLE", "The Song", "song"), Simple ("TITLE_SORT", "f30"))));

		foreach (var (name, set, get) in setters) {
			var tag = MatroskaFile.Read (Build ()).File!.Tag;
			Assert.IsNotNull (get (tag), name);

			set (tag, "");

			Assert.IsNull (get (tag), name);
			Assert.IsNull (get (MatroskaFile.Read (FileWith (tag.Render ().ToArray ())).File!.Tag), $"{name}: render");
		}
	}

	[TestMethod]
	public void SortSet_SingleLevelSharedArtist_ReviewerSequence_KeepsBoth ()
	{
		var tag = MatroskaFile.Read (SortShape ("single-level nested")).File!.Tag;

		tag.ArtistSort = "Artist-only sort";
		tag.AlbumArtistSort = "Album-artist sort";

		Assert.AreEqual ("Artist-only sort", tag.ArtistSort);
		Assert.AreEqual ("Album-artist sort", tag.AlbumArtistSort);

		tag.AlbumArtistSort = null;

		Assert.AreEqual ("Artist-only sort", tag.ArtistSort);
		Assert.IsNull (tag.AlbumArtistSort);
		Assert.AreEqual ("The Band", tag.Artist);
		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
	}

	[TestMethod]
	public void SortSet_SameValueOnSharedArtist_LeavesTheFileAlone ()
	{
		var original = SortShape ("single-level nested");
		var tag = MatroskaFile.Read (original).File!.Tag;
		var rendered = tag.Render ().ToArray ();

		tag.AlbumArtistSort = "shared";
		tag.ArtistSort = "shared";

		CollectionAssert.AreEqual (rendered, tag.Render ().ToArray ());
	}

	[TestMethod]
	public void CopyTo_SingleLevelNestedTarget_KeepsArtistAndAlbumArtistSortApart ()
	{
		var source = new VorbisComment { ArtistSort = "Beatles, The", AlbumArtistSort = "Various" };
		var target = MatroskaFile.Read (SortShape ("single-level nested")).File!.Tag;

		source.CopyTo (target);

		Assert.AreEqual ("Beatles, The", target.ArtistSort);
		Assert.AreEqual ("Various", target.AlbumArtistSort);
		Assert.AreEqual ("The Band", target.Artist);
		CollectionAssert.AreEqual (SortTheBand, target.AlbumArtists);
	}

	[TestMethod]
	public void Read_SingleLevel_NestedSortWinsOverTheFlatName ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "nested"), Simple ("ALBUM_ARTIST_SORT", "flat"))))).File!.Tag;
		var collection = MatroskaFile.Read (FileWith (Tags (
			Tag (70, SimpleSorted ("ARTIST", "The Band", "nested"), Simple ("ALBUM_ARTIST_SORT", "flat")),
			Tag (50, Simple ("TITLE", "The Album"))))).File!.Tag;

		Assert.AreEqual ("nested", tag.AlbumArtistSort);
		Assert.AreEqual ("nested", collection.AlbumArtistSort);
	}

	[TestMethod]
	public void Read_FlatAlbumArtist_NestedSortWinsOverTheFlatName ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "Ada"), SimpleSorted ("ALBUM_ARTIST", "The Band", "nested"),
				Simple ("ALBUM_ARTIST_SORT", "flat"))))).File!.Tag;

		Assert.AreEqual ("nested", tag.AlbumArtistSort);
	}

	[TestMethod]
	public void Read_FlatAlbumArtist_PairsWithItsSort ()
	{
		var tag = MatroskaFile.Read (SortShape ("ffmpeg flat album artist")).File!.Tag;

		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
		Assert.AreEqual ("Ada", tag.Artist);
		Assert.AreEqual ("Ada sort", tag.ArtistSort);
	}

	[TestMethod]
	public void Read_FlatAlbumArtistAboveTheTrack_IsTheAlbumArtist ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "Album Artist By Spec"), Simple ("ALBUM_ARTIST", "The Band"),
				Simple ("ALBUM_ARTIST_SORT", "Band, The")),
			Tag (30, SimpleSorted ("ARTIST", "Ada", "ada"))))).File!.Tag;

		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
	}

	[TestMethod]
	public void AlbumArtistsSet_FlatAlbumArtist_WritesItAndKeepsTheArtist ()
	{
		var tag = MatroskaFile.Read (SortShape ("ffmpeg flat album artist")).File!.Tag;

		tag.AlbumArtists = ["New Band"];

		Assert.AreEqual ("New Band", string.Join ("|", tag.AlbumArtists));
		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
		Assert.AreEqual ("Ada", tag.Artist);
		Assert.AreEqual ("Ada sort", tag.ArtistSort);

		var again = MatroskaFile.Read (FileWith (tag.Render ().ToArray ())).File!.Tag;
		Assert.AreEqual ("New Band", string.Join ("|", again.AlbumArtists));
		Assert.AreEqual ("Ada", again.Artist);
	}

	[TestMethod]
	public void AlbumArtistsClear_FlatAlbumArtist_RemovesItAndItsSort ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (70, Simple ("ALBUM_ARTIST", "Collection Band")),
			Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort"),
				Simple ("ALBUM_ARTIST", "The Band"), Simple ("ALBUM_ARTIST_SORT", "Band, The"))))).File!.Tag;

		tag.AlbumArtists = [];

		// The album-level ARTIST is the album artist by the specification once ALBUM_ARTIST is gone.
		Assert.AreEqual ("Ada", string.Join ("|", tag.AlbumArtists));
		Assert.IsNull (tag.AlbumArtistSort);
		Assert.AreEqual ("Ada", tag.Artist);
		Assert.AreEqual ("Ada sort", tag.ArtistSort);
	}

	[TestMethod]
	public void SortSet_FlatAlbumArtist_WritesAlbumArtistSortNotTheArtistSort ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "Ada", "ada"), Simple ("ALBUM_ARTIST", "The Band"))))).File!.Tag;
		Assert.IsNull (tag.AlbumArtistSort);

		tag.AlbumArtistSort = "Band, The";

		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
		var rendered = tag.Render ().ToArray ();
		Assert.AreEqual (1, CountOf (rendered, "ALBUM_ARTIST_SORT"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (rendered, "SORT_WITH"u8.ToArray ()));

		tag.AlbumArtistSort = null;

		Assert.IsNull (tag.AlbumArtistSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
	}

	[TestMethod]
	public void RoundTrip_SingleLevelSharedArtist_OnDisk ()
	{
		var path = WriteTempMatroska (FileWith (
			Tags (Tag (50, Simple ("TITLE", "The Album"), SimpleSorted ("ARTIST", "The Band", "shared"),
				SimpleSorted ("COMPOSER", "Bach", "bach"), Simple ("GENRE", "Rock"))),
			Master (0x1F43B675, SortClusterMarker)));
		try {
			MatroskaSortRoundTrip.VerifyOnCopy (path, saved => {
				Assert.AreEqual (1, CountOf (saved, SortClusterMarker));
				Assert.AreEqual (1, CountOf (saved, TagNameElement ("GENRE")), "the genre is replaced, not shadowed");
			});
		} finally {
			File.Delete (path);
		}
	}

	[TestMethod]
	public async Task RoundTrip_FlatAlbumArtist_OnDiskAsync ()
	{
		var path = WriteTempMatroska (SortShape ("ffmpeg flat album artist"));
		try {
			await MatroskaSortRoundTrip.VerifyOnCopyAsync (path, saved => Assert.AreEqual (1, CountOf (saved, "ALBUM_ARTIST_SORT"u8.ToArray ())));
		} finally {
			File.Delete (path);
		}
	}

	// Invariant: a sort write never changes where later item-field writes land. After any sort operation on
	// any shape, clearing Genre, Comment or Year reads null, and setting one reads the new value from a single
	// SimpleTag, in memory and after a render. The fields start at 50, where TagLib# and ffmpeg write them.
	[TestMethod]
	public void SortSet_ThenItemFields_ClearAndSetOnEveryShape ()
	{
		var sortOps = new (string Name, Action<MatroskaTag> Apply, Func<MatroskaTag, string?> Read, string? Expected)[] {
			("AlbumArtistSort = NEW", t => t.AlbumArtistSort = "NEW", t => t.AlbumArtistSort, "NEW"),
			("AlbumArtistSort = null", t => t.AlbumArtistSort = null, t => t.AlbumArtistSort, null),
			("AlbumArtistSort = empty", t => t.AlbumArtistSort = "", t => t.AlbumArtistSort, null),
			("ArtistSort = NEW", t => t.ArtistSort = "NEW", t => t.ArtistSort, "NEW"),
			("ArtistSort = null", t => t.ArtistSort = null, t => t.ArtistSort, null),
			("ArtistSort = empty", t => t.ArtistSort = "", t => t.ArtistSort, null),
		};
		var fields = new (string Name, string TagName, Action<MatroskaTag, string?> Set, Func<MatroskaTag, string?> Get)[] {
			("Genre", "GENRE", (t, v) => t.Genre = v, t => t.Genre),
			("Comment", "COMMENT", (t, v) => t.Comment = v, t => t.Comment),
			("Year", "DATE_RELEASED", (t, v) => t.Year = v, t => t.Year),
			("Genres", "GENRE", (t, v) => t.Genres = v is null ? [] : [v], t => t.Genres.FirstOrDefault ()),
		};

		foreach (var (shape, data) in ItemFieldShapes ()) {
			foreach (var (sortName, apply, read, expected) in sortOps) {
				foreach (var (field, tagName, set, get) in fields) {
					foreach (var value in new[] { null, "1999" }) {
						var label = $"{shape}: {sortName}, then {field} = {value ?? "null"}";
						var tag = MatroskaFile.Read (data).File!.Tag;

						apply (tag);
						set (tag, value);

						Assert.AreEqual (value, get (tag), label);
						Assert.AreEqual (expected, read (tag), $"{label}: sort");
						var rendered = tag.Render ().ToArray ();
						var again = MatroskaFile.Read (FileWith (rendered)).File!.Tag;
						Assert.AreEqual (value, get (again), $"{label}: render");
						Assert.AreEqual (expected, read (again), $"{label}: render sort");
						Assert.AreEqual (value is null ? 0 : 1, CountOf (rendered, TagNameElement (tagName)), $"{label}: {tagName} count");
					}
				}
			}
		}
	}

	static IEnumerable<(string Name, byte[] Data)> ItemFieldShapes ()
	{
		string[] kinds = ["absent", "plain", "nested"];
		// ARTIST at 30 and 50 {absent, plain, nested} x nested ARTIST at 70 x ALBUM_ARTIST {none, 50, nested 70} x track filler.
		for (var i = 0; i < 3 * 3 * 2 * 3 * 2; i++) {
			var a30 = i % 3;
			var a50 = i / 3 % 3;
			var a70 = i / 9 % 2;
			var albumArtist = i / 18 % 3;
			var filler = i / 54;
			var at70 = new List<byte[]> ();
			var at50 = new List<byte[]> { Simple ("GENRE", "Rock"), Simple ("COMMENT", "c50"), Simple ("DATE_RELEASED", "2001") };
			var at30 = new List<byte[]> ();
			if (ArtistAt (a30, 30) is { } x30)
				at30.Add (x30);
			if (ArtistAt (a50, 50) is { } x50)
				at50.Add (x50);
			if (a70 == 1)
				at70.Add (SimpleSorted ("ARTIST", "A70", "n70"));
			if (albumArtist == 1)
				at50.Add (Simple ("ALBUM_ARTIST", "AA50"));
			if (albumArtist == 2)
				at70.Add (SimpleSorted ("ALBUM_ARTIST", "AA70", "aa70"));
			if (filler == 1)
				at30.Add (Simple ("TITLE", "Song"));
			var blocks = new List<byte[]> ();
			if (at70.Count > 0)
				blocks.Add (Tag (70, [.. at70]));
			blocks.Add (Tag (50, [.. at50]));
			if (at30.Count > 0)
				blocks.Add (Tag (30, [.. at30]));
			yield return ($"a30={kinds[a30]} a50={kinds[a50]} a70={a70} albumArtist={albumArtist} filler={filler}",
				FileWith (Tags ([.. blocks])));
		}
	}

	static byte[]? ArtistAt (int kind, ulong level) => kind switch {
		1 => Simple ("ARTIST", $"A{level}"),
		2 => SimpleSorted ("ARTIST", $"A{level}", $"n{level}"),
		_ => null,
	};

	static byte[] TagNameElement (string name) => Element (0x45A3, Encoding.UTF8.GetBytes (name));

	[TestMethod]
	public void NewField_AfterASortSplit_StaysWithTheFileFields ()
	{
		// TagLib# writes every tag at 50 and reads new fields only there. The ARTIST copy at 30 that keeps
		// ArtistSort apart must not pull later new fields down to 30.
		var original = FileWith (
			Tags (Tag (50, Simple ("TITLE", "The Album"), SimpleSorted ("ARTIST", "The Band", "shared"), Simple ("GENRE", "Rock"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.ArtistSort = "track";
		parsed.Tag.Conductor = "Cond";
		parsed.Tag.Genre = "Jazz";

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("track", again.ArtistSort);
		Assert.AreEqual ("shared", again.AlbumArtistSort);
		Assert.AreEqual ("Cond", again.Conductor);
		Assert.AreEqual ("Jazz", again.Genre);
		Assert.IsTrue (FileTargetHas (bytes, 30, "ARTIST"), "the track keeps its own ARTIST");
		Assert.IsTrue (FileTargetHas (bytes, 50, "CONDUCTOR"));
		Assert.IsFalse (FileTargetHas (bytes, 30, "CONDUCTOR"));
		Assert.IsTrue (FileTargetHas (bytes, 50, "GENRE"));
		Assert.AreEqual (1, CountOf (bytes, TagNameElement ("GENRE")));
		CollectionAssert.AreEqual (new ulong[] { 50, 30 }, TargetLevelsInOrder (bytes), "the new track Tag goes after the album's");
	}

	[TestMethod]
	public void SortSplit_FromACollectionArtist_OrdersTheNewTagsBroadestFirst ()
	{
		var original = FileWith (
			Tags (Tag (70, SimpleSorted ("ARTIST", "The Band", "shared")), Tag (60, Simple ("TOTAL_PARTS", "2"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.AlbumArtistSort = "album";

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("album", again.AlbumArtistSort);
		Assert.AreEqual ("shared", again.ArtistSort);
		CollectionAssert.AreEqual (new ulong[] { 70, 60, 50, 30 }, TargetLevelsInOrder (bytes));
	}

	[TestMethod]
	public void SortClear_ThatEmptiesATag_DropsIt ()
	{
		// RFC 9559 section 5.1.8.1.2: a Tag holds at least one SimpleTag. Clearing the sorts empties the 30 Tag;
		// the file is then album-level, so the 60 ALBUM_ARTIST_SORT is visible and goes too.
		var original = FileWith (
			Tags (
				Tag (60, Simple ("ALBUM_ARTIST_SORT", "aa")),
				Tag (50, Simple ("TITLE", "The Album"), Simple ("ARTIST", "The Band"), Simple ("COMPOSER", "Bach")),
				Tag (30, Simple ("ARTIST_SORT", "a"), Simple ("COMPOSER_SORT", "c"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.ArtistSort = null;
		parsed.Tag.ComposerSort = null;
		parsed.Tag.AlbumArtistSort = null;

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.IsNull (again.ArtistSort);
		Assert.IsNull (again.ComposerSort);
		Assert.IsNull (again.AlbumArtistSort);
		CollectionAssert.AreEqual (new ulong[] { 50 }, TargetLevelsInOrder (bytes));
		Assert.AreEqual (1, CountOf (bytes, Id (0x7373)));
	}

	[TestMethod]
	public void SortSet_WithoutASharedNestedSort_AddsNoTrackArtist ()
	{
		// A split is only for a shared SORT_WITH: not for a bare ARTIST, and not when ALBUM_ARTIST names the album artist.
		var shapes = new[] {
			("bare single-level artist", FileWith (Tags (Tag (50, Simple ("ARTIST", "The Band"), Simple ("GENRE", "Rock"))))),
			("nested artist beside ALBUM_ARTIST", SortShape ("nested album artist name")),
			("ffmpeg flat album artist", SortShape ("ffmpeg flat album artist")),
		};
		foreach (var (name, data) in shapes) {
			foreach (var write in new Action<MatroskaTag>[] { t => t.ArtistSort = "NEW", t => t.AlbumArtistSort = "NEW", t => t.ArtistSort = null }) {
				var tag = MatroskaFile.Read (data).File!.Tag;
				var artistsBefore = CountOf (tag.Render ().ToArray (), TagNameElement ("ARTIST"));

				write (tag);

				var rendered = tag.Render ().ToArray ();
				Assert.AreEqual (artistsBefore, CountOf (rendered, TagNameElement ("ARTIST")), name);
				Assert.IsFalse (FileTargetHas (FileWith (rendered), 30, "ARTIST"), name);
			}
		}
	}

	[TestMethod]
	public void Clear_ThatEmptiesTheTrackTag_ReadsLikeTheSavedFile ()
	{
		// An emptied Tag is not rendered (RFC 9559 section 5.1.8.1.2), so the file is then album-level and the
		// collection ARTIST reaches the album. Memory must say what a re-read of the saved file says.
		var original = FileWith (
			Tags (
				Tag (70, SimpleSorted ("ARTIST", "Collection", "collection")),
				Tag (50, Simple ("TITLE", "The Album")),
				Tag (30, Simple ("GENRE", "Jazz"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.IsEmpty (parsed.Tag.AlbumArtists);

		parsed.Tag.Genre = null;

		var inMemory = SortState.Of (parsed.Tag);
		var again = SaveAndReread (parsed, original, out _);
		Assert.AreEqual (SortState.Of (again), inMemory);
		Assert.AreEqual ("Collection", string.Join ("|", parsed.Tag.AlbumArtists));
	}

	[TestMethod]
	public void AlbumArtistsSet_CollectionAlbumArtist_StaysAtItsLevel ()
	{
		var original = FileWith (
			Tags (Tag (70, SimpleSorted ("ALBUM_ARTIST", "Old Band", "Band, Old")), Tag (30, Simple ("TITLE", "The Song"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.AlbumArtists = ["New Band"];

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("New Band", string.Join ("|", again.AlbumArtists));
		Assert.AreEqual (1, CountOf (bytes, TagNameElement ("ALBUM_ARTIST")));
		Assert.IsTrue (FileTargetHas (bytes, 70, "ALBUM_ARTIST"));
		Assert.IsFalse (FileTargetHas (bytes, 30, "ALBUM_ARTIST"));
	}

	[TestMethod]
	public void AlbumArtistsSet_EmptyStrings_ClearTheFlatAlbumArtistAndItsSort ()
	{
		foreach (var empty in new[] { new[] { "" }, new[] { "", "" } }) {
			var tag = MatroskaFile.Read (SortShape ("ffmpeg flat album artist")).File!.Tag;

			tag.AlbumArtists = empty;

			Assert.AreEqual ("Ada", string.Join ("|", tag.AlbumArtists));
			Assert.IsNull (tag.AlbumArtistSort);
			var rendered = tag.Render ().ToArray ();
			Assert.AreEqual (0, CountOf (rendered, TagNameElement ("ALBUM_ARTIST")));
			Assert.AreEqual (0, CountOf (rendered, TagNameElement ("ALBUM_ARTIST_SORT")));
		}
	}

	sealed record SortState (
		string? Title, string? Album, string? Artist, string Performers, string AlbumArtists, string Composers,
		string? ArtistSort, string? AlbumArtistSort, string? ComposerSort, string? TitleSort, string? AlbumSort)
	{
		public static SortState Of (MatroskaTag tag) => new (
			tag.Title, tag.Album, tag.Artist, string.Join ("|", tag.Performers), string.Join ("|", tag.AlbumArtists),
			string.Join ("|", tag.Composers), tag.ArtistSort, tag.AlbumArtistSort, tag.ComposerSort, tag.TitleSort,
			tag.AlbumSort);
	}
}
