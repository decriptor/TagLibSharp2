// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Matroska;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Matroska;

// Invariant: a sort sits with the value it sorts. A flat *_SORT belongs to the value in its own Tag
// (the ffmpeg convention) and is inherited by narrower levels (tagging.html, "Target types") up to the
// next broader Tag that holds a value of its own. Writing a sort or a value never changes a value or a
// sort that belongs to another level.
public partial class MatroskaFileTests
{
	enum Getter { Artist, AlbumArtistByArtist, Composer, AlbumArtistByName }

	sealed record SortPair (string Name, string Value, string Flat, Getter Getter, bool WithAda);

	static readonly SortPair[] SortPairs = [
		new ("ARTIST", "ARTIST", "ARTIST_SORT", Getter.Artist, false),
		new ("ARTIST for the album", "ARTIST", "ALBUM_ARTIST_SORT", Getter.AlbumArtistByArtist, false),
		new ("COMPOSER", "COMPOSER", "COMPOSER_SORT", Getter.Composer, false),
		new ("ALBUM_ARTIST", "ALBUM_ARTIST", "ALBUM_ARTIST_SORT", Getter.AlbumArtistByName, true),
	];

	// Each level 30, 50, 70: absent, value, value + flat sort, value with SORT_WITH, both, flat sort only.
	static IEnumerable<(string Name, byte[] Data)> OwnershipShapes (SortPair pair, bool sameText = false, string? pattern = null, byte[][]? others = null, bool fillerOnly = false)
	{
		string[] kinds = ["-", "v", "vf", "n", "nf", "f"];
		ulong[] levels = [30, 50, 70];
		for (var i = 0; i < 6 * 6 * 6 * 2; i++) {
			var picks = new[] { i % 6, i / 6 % 6, i / 36 % 6 };
			var filler = i / 216 == 1;
			if ((others is not null || fillerOnly) && !filler)
				continue;
			var blocks = new List<byte[]> ();
			for (var at = 2; at >= 0; at--) {
				var level = levels[at];
				var simple = new List<byte[]> ();
				var kind = kinds[picks[at]];
				var text = pattern is not null ? (pattern[at] == 'E' ? "" : $"{pair.Value}{pattern[at]}") : sameText ? pair.Value : $"{pair.Value}{level}";
				if (kind is "v" or "vf")
					simple.Add (Simple (pair.Value, text));
				if (kind is "n" or "nf")
					simple.Add (SimpleSorted (pair.Value, text, $"n{level}"));
				if (kind is "vf" or "nf" or "f")
					simple.Add (Simple (pair.Flat, $"f{level}"));
				if (level == 50 && pair.WithAda)
					simple.Add (Simple ("ARTIST", "Ada"));
				if (level == 30 && filler)
					simple.Add (Simple ("TITLE", "Song"));
				if (simple.Count > 0)
					blocks.Add (Tag (level, [.. simple]));
			}

			if (blocks.Count == 0)
				continue;
			if (others is not null)
				blocks.AddRange (others);
			yield return ($"{pattern}30={kinds[picks[0]]} 50={kinds[picks[1]]} 70={kinds[picks[2]]} filler={filler}", FileWith (Tags ([.. blocks])));
		}
	}

	static string? ReadSort (MatroskaTag tag, SortPair pair) => pair.Getter switch {
		Getter.Artist => tag.ArtistSort,
		Getter.Composer => tag.ComposerSort,
		_ => tag.AlbumArtistSort,
	};

	static ulong? TargetLevel (ReadOnlySpan<byte> targets)
	{
		for (var i = 0; i < targets.Length && EbmlReader.TryRead (targets, i, targets.Length, out var child); i = child.End) {
			if (child.Id == MatroskaIds.TargetTypeValue)
				return EbmlReader.ReadUInt (targets.Slice (child.DataOffset, (int)child.DataLength));
		}

		return null;
	}

	[TestMethod]
	public void ArtistSort_OnAnFfmpegFile_AfterANewTrackArtist_StaysWithTheTrackArtist ()
	{
		// ffmpeg's untargeted Tag holds ARTIST and its ARTIST_SORT. A track ARTIST at 30 is a different
		// value, so its sort goes beside it and the album's pair is kept.
		var original = FileWith (
			Tags (Master (0x7373, Concat (Master (0x63C0, Array.Empty<byte> ()),
				Simple ("TITLE", "Song"), Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort")))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.Artist = "Solo";
		Assert.IsNull (parsed.Tag.ArtistSort, "Ada's sort is not Solo's");
		parsed.Tag.ArtistSort = "Solo, X";

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("Solo", again.Artist);
		Assert.AreEqual ("Solo, X", again.ArtistSort);
		Assert.IsTrue (FileTargetHas (bytes, 30, "ARTIST_SORT"));
		Assert.IsTrue (HasTagString (bytes, "Ada sort"), "the album's sort is kept");

		again.ArtistSort = null;
		Assert.IsNull (again.ArtistSort);
		Assert.IsTrue (HasTagString (again.Render ().ToArray (), "Ada sort"), "a clear keeps the album's sort");
	}

	[TestMethod]
	public void ItemField_InheritedFromACollection_IsWrittenAtTheAlbum ()
	{
		// A target broader than the album is not where ordinary fields are written: the set goes to 50
		// and shadows the collection value, which stays as it was.
		foreach (var (name, set, get) in new (string, Action<MatroskaTag>, Func<MatroskaTag, string?>)[] {
			("GENRE", t => t.Genre = "Jazz", t => t.Genre),
			("COMPOSER", t => t.Composers = ["Jazz"], t => t.Composers.FirstOrDefault ()),
		}) {
			var original = FileWith (Tags (Tag (70, Simple (name, "Rock")), Tag (30, Simple ("TITLE", "Song"))), Master (0x1F43B675, SortClusterMarker));
			var parsed = MatroskaFile.Read (original).File!;

			set (parsed.Tag);

			var again = SaveAndReread (parsed, original, out var bytes);
			Assert.AreEqual ("Jazz", get (again), name);
			Assert.IsTrue (FileTargetHas (bytes, 70, name), name);
			Assert.IsTrue (HasTagString (bytes, "Rock"), $"{name}: the collection value is kept");
			Assert.AreEqual (2, CountOf (bytes, TagNameElement (name)), name);
			Assert.IsTrue (FileTargetHas (bytes, 30, name) || FileTargetHas (bytes, 50, name), name);
		}
	}

	[TestMethod]
	public void NewField_BesideAReadEmptyTrackTag_GoesToTheTrack ()
	{
		// A Tag that was empty when read is kept (#132) and is the item, so it is not skipped like an
		// ARTIST-only copy.
		var original = FileWith (
			Tags (Tag (50, Simple ("GENRE", "Rock")), Master (0x7373, Master (0x63C0, Element (0x68CA, new byte[] { 30 })))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.Conductor = "Cond";

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("Cond", again.Conductor);
		Assert.IsTrue (FileTargetHas (bytes, 30, "CONDUCTOR"));
	}

	[TestMethod]
	public void NewField_AfterAClearEmptiesTheTrackTag_GoesToTheAlbum ()
	{
		var original = FileWith (
			Tags (Tag (50, Simple ("TITLE", "The Album")), Tag (30, Simple ("TITLE", "The Song"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.Title = null;
		parsed.Tag.Conductor = "Cond";

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("Cond", again.Conductor);
		CollectionAssert.AreEqual (new ulong[] { 50 }, TargetLevelsInOrder (bytes));
	}

	[TestMethod]
	public void FlatSort_ForACollectionValue_IsWrittenBelowIt ()
	{
		// A target broader than the album is not where ordinary fields go; the sort is written below its
		// value, where it still pairs with it, and the collection Tag is left as it was.
		foreach (var (name, flat, set, get) in new (string, string, Action<MatroskaTag>, Func<MatroskaTag, string?>)[] {
			("ARTIST", "ARTIST_SORT", t => t.ArtistSort = "NEW", t => t.ArtistSort),
			("COMPOSER", "COMPOSER_SORT", t => t.ComposerSort = "NEW", t => t.ComposerSort),
		}) {
			var original = FileWith (Tags (Tag (70, Simple (name, "Collection")), Tag (30, Simple ("TITLE", "Song"))), Master (0x1F43B675, SortClusterMarker));
			var parsed = MatroskaFile.Read (original).File!;

			set (parsed.Tag);

			var again = SaveAndReread (parsed, original, out var bytes);
			Assert.AreEqual ("NEW", get (again), name);
			Assert.IsFalse (FileTargetHas (bytes, 70, flat), name);
		}
	}

	[TestMethod]
	public void SortSplit_CarriesTheFlatSortsWithEachCopy ()
	{
		// An empty SORT_WITH leaves the flat sorts in charge. The track copy repeats the value, so ARTIST_SORT
		// still reads; a copy from above the album takes ALBUM_ARTIST_SORT; new fields go beside the others.
		var single = FileWith (
			Tags (Tag (50, SimpleSorted ("ARTIST", "The Band", ""), Simple ("ARTIST_SORT", "flat artist"), Simple ("GENRE", "Rock"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (single).File!;
		Assert.AreEqual ("flat artist", parsed.Tag.ArtistSort);

		parsed.Tag.AlbumArtistSort = "album";
		parsed.Tag.Conductor = "Cond";

		var again = SaveAndReread (parsed, single, out var bytes);
		Assert.AreEqual ("flat artist", again.ArtistSort);
		Assert.AreEqual ("album", again.AlbumArtistSort);
		Assert.IsTrue (FileTargetHas (bytes, 50, "CONDUCTOR"));
		Assert.IsFalse (FileTargetHas (bytes, 30, "CONDUCTOR"));

		var collection = FileWith (
			Tags (Tag (70, SimpleSorted ("ARTIST", "The Band", ""), Simple ("ALBUM_ARTIST_SORT", "flat album")), Tag (50, Simple ("TITLE", "The Album"))),
			Master (0x1F43B675, SortClusterMarker));
		var fromCollection = MatroskaFile.Read (collection).File!;
		Assert.AreEqual ("flat album", fromCollection.Tag.AlbumArtistSort);

		fromCollection.Tag.ArtistSort = "track";

		var reread = SaveAndReread (fromCollection, collection, out _);
		Assert.AreEqual ("track", reread.ArtistSort);
		Assert.AreEqual ("flat album", reread.AlbumArtistSort);
	}

	[TestMethod]
	public void ValueClear_TakesTheFlatSortsOfItsTagWithIt ()
	{
		var album = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "A50"), Simple ("ARTIST_SORT", "a50 sort"), Simple ("ALBUM_ARTIST_SORT", "aa50 sort")),
			Tag (30, Simple ("TITLE", "Song"))))).File!.Tag;
		album.AlbumArtists = [];
		Assert.IsNull (album.ArtistSort);
		Assert.IsNull (album.AlbumArtistSort);
		Assert.IsFalse (HasTagName (FileWith (album.Render ().ToArray ()), "ARTIST_SORT"));
		Assert.IsFalse (HasTagName (FileWith (album.Render ().ToArray ()), "ALBUM_ARTIST_SORT"));

		var track = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "ada")),
			Tag (30, Simple ("ARTIST", "Solo"), Simple ("ARTIST_SORT", "solo"))))).File!.Tag;
		track.Artist = null;
		Assert.AreEqual ("Ada", track.Artist);
		Assert.AreEqual ("ada", track.ArtistSort, "the track now inherits the album's pair");
		Assert.IsFalse (HasTagString (FileWith (track.Render ().ToArray ()), "solo"));

		var titles = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("TITLE", "Album"), Simple ("ALBUM_SORT", "album sort")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("TITLE_SORT", "song sort"), Simple ("GENRE", "Rock"))))).File!.Tag;
		titles.Title = null;
		titles.Album = null;
		Assert.IsNull (titles.TitleSort);
		Assert.IsNull (titles.AlbumSort);
		var rendered = FileWith (titles.Render ().ToArray ());
		Assert.IsFalse (HasTagName (rendered, "TITLE_SORT"));
		Assert.IsFalse (HasTagName (rendered, "ALBUM_SORT"));

		var composers = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "bach")),
			Tag (30, Simple ("TITLE", "Song"))))).File!.Tag;
		composers.Composers = [];
		Assert.IsNull (composers.ComposerSort);
		Assert.IsFalse (HasTagName (FileWith (composers.Render ().ToArray ()), "COMPOSER_SORT"));

		// A sort with no value is not removed by clearing values that do not exist.
		var orphan = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("TITLE", "Album"), Simple ("COMPOSER_SORT", "bach"))))).File!.Tag;
		orphan.Composers = [];
		Assert.AreEqual ("bach", orphan.ComposerSort);
	}

	[TestMethod]
	public void ValueSet_InATagWhoseSortTheTrackInherits_LeavesTheSortWithTheTrack ()
	{
		// The 50 ARTIST_SORT has no ARTIST beside it, so the track's ARTIST inherits it. A new album ARTIST
		// in that Tag would take it over; it moves to the track instead.
		var original = FileWith (
			Tags (Tag (50, Simple ("TITLE", "Album"), Simple ("ARTIST_SORT", "track sort")), Tag (30, Simple ("ARTIST", "Solo"))),
			Master (0x1F43B675, SortClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual ("track sort", parsed.Tag.ArtistSort);

		parsed.Tag.AlbumArtists = ["Band"];

		var again = SaveAndReread (parsed, original, out var bytes);
		Assert.AreEqual ("track sort", again.ArtistSort);
		Assert.AreEqual ("Band", string.Join ("|", again.AlbumArtists));
		Assert.IsTrue (FileTargetHas (bytes, 30, "ARTIST_SORT"));
		Assert.IsFalse (FileTargetHas (bytes, 50, "ARTIST_SORT"));
	}

	[TestMethod]
	public void SortPairing_IsLinearInTheTags ()
	{
		// Thousands of Tags at distinct TargetTypeValues, each with a flat sort the file controls. Pairing a
		// sort visits each Tag a bounded number of times; a per-level rescan would visit them squared.
		const int count = 3000;
		var blocks = new List<byte[]> {
			Tag (30, Simple ("ARTIST", "Solo"), Simple ("COMPOSER", "Bach"), Simple ("TITLE", "Song")),
		};
		for (var i = 0; i < count; i++) {
			var level = (ushort)(1000 + i);
			var target = Master (0x63C0, Element (0x68CA, [(byte)(level >> 8), (byte)level]));
			blocks.Add (Master (0x7373, Concat (target,
				Simple ("ARTIST_SORT", $"a{i}"), Simple ("COMPOSER_SORT", $"c{i}"), Simple ("ALBUM_ARTIST_SORT", $"aa{i}"))));
		}

		var data = Matroska (WideElement (0x18538067, WideElement (0x1254C367, Concat ([.. blocks]))));
		Assert.IsTrue (MatroskaFile.Read (data).File!.Tag.Render ().Length > count * 20, "every Tag is read");
		foreach (var (name, op) in new (string, Action<MatroskaTag>)[] {
			("ArtistSort get", t => _ = t.ArtistSort),
			("ArtistSort set", t => t.ArtistSort = "x"),
			("ArtistSort clear", t => t.ArtistSort = null),
			("ComposerSort set", t => t.ComposerSort = "x"),
			("ComposerSort clear", t => t.ComposerSort = null),
			("AlbumArtistSort set", t => t.AlbumArtistSort = "x"),
			("AlbumArtistSort clear", t => t.AlbumArtistSort = null),
			("Artist clear", t => t.Artist = null),
			("AlbumArtists set", t => t.AlbumArtists = ["Band"]),
			("Composers clear", t => t.Composers = []),
		}) {
			var tag = MatroskaFile.Read (data).File!.Tag;
			MatroskaTag.LevelSteps = 0;

			op (tag);

			var steps = MatroskaTag.LevelSteps;
			Assert.IsTrue (steps >= count, $"{name}: visited only {steps} Tags of {count}; the counter is not counting");
			Assert.IsTrue (steps <= 64L * count, $"{name}: visited {steps} Tags for {count}; pairing is not linear");
		}
	}

	[TestMethod]
	public void SortAndValueWrites_OnHostileAndEmptyFields_StayConsistent ()
	{
		static byte[] Bare (string name) => Master (0x67C8, Element (0x45A3, Encoding.UTF8.GetBytes (name)));
		static byte[] Nested (string name, string text, int depth)
		{
			var inner = Simple ("SORT_WITH", "deep");
			for (var i = 0; i < depth; i++)
				inner = Master (0x67C8, Concat (Element (0x45A3, "SORT_WITH"u8), Element (0x4487, "d"u8), inner));
			return Master (0x67C8, Concat (Element (0x45A3, Encoding.UTF8.GetBytes (name)), Element (0x4487, Encoding.UTF8.GetBytes (text)), inner));
		}

		var emptyTarget = Master (0x63C0, Element (0x68CA, Array.Empty<byte> ()));
		var shapes = new (string, byte[])[] {
			("empty ARTIST blocks the album's", FileWith (Tags (Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "ada")), Tag (30, Simple ("ARTIST", ""))))),
			("ARTIST without a TagString", FileWith (Tags (Tag (50, Bare ("ARTIST"), Simple ("ARTIST_SORT", "flat")), Tag (30, Simple ("TITLE", "Song"))))),
			("empty TagName", FileWith (Tags (Tag (50, Bare (""), Simple ("ARTIST", "Ada"))))),
			("empty TargetTypeValue is 50", FileWith (Tags (Master (0x7373, Concat (emptyTarget, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "ada"))), Tag (30, Simple ("ARTIST", "Solo"))))),
			("empty SORT_WITH", FileWith (Tags (Tag (50, SimpleSorted ("ARTIST", "Ada", ""), SimpleSorted ("COMPOSER", "Bach", ""))))),
			("SORT_WITH without a TagString", FileWith (Tags (Tag (50, Master (0x67C8, Concat (Element (0x45A3, "ARTIST"u8), Element (0x4487, "Ada"u8), Bare ("SORT_WITH"))))))),
			("SORT_WITH nested 3 deep", FileWith (Tags (Tag (50, Nested ("ARTIST", "Ada", 3)), Tag (30, Simple ("TITLE", "Song"))))),
			("SORT_WITH nested 200 deep", FileWith (Tags (Tag (50, Nested ("COMPOSER", "Bach", 200), Nested ("ARTIST", "Ada", 200))))),
			("read-empty Tags only", FileWith (Tags (Master (0x7373, Master (0x63C0, Element (0x68CA, new byte[] { 30 }))), Master (0x7373, emptyTarget)))),
			("empty flat sorts", FileWith (Tags (Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", ""), Simple ("ALBUM_ARTIST_SORT", ""), Simple ("COMPOSER_SORT", ""))))),
		};
		var ops = new (string, Action<MatroskaTag>)[] {
			("sorts set", t => { t.ArtistSort = "x"; t.AlbumArtistSort = "y"; t.ComposerSort = "z"; }),
			("sorts cleared", t => { t.ArtistSort = null; t.AlbumArtistSort = ""; t.ComposerSort = null; }),
			("values set", t => { t.Artist = "A"; t.AlbumArtists = ["B"]; t.Composers = ["C"]; }),
			("values cleared", t => { t.Artist = null; t.AlbumArtists = []; t.Composers = []; t.Title = null; }),
			("write back", t => { t.ArtistSort = t.ArtistSort; t.AlbumArtistSort = t.AlbumArtistSort; t.Artist = t.Artist; t.AlbumArtists = t.AlbumArtists; }),
		};
		foreach (var (shape, data) in shapes) {
			foreach (var (name, op) in ops) {
				var label = $"{shape}: {name}";
				var read = MatroskaFile.Read (data);
				Assert.IsTrue (read.IsSuccess, $"{label}: {read.Error}");
				var tag = read.File!.Tag;

				op (tag);

				var again = MatroskaFile.Read (FileWith (tag.Render ().ToArray ()));
				Assert.IsTrue (again.IsSuccess, $"{label}: {again.Error}");
				var reread = again.File!.Tag;
				Assert.AreEqual (tag.ArtistSort, reread.ArtistSort, label);
				Assert.AreEqual (tag.AlbumArtistSort, reread.AlbumArtistSort, label);
				Assert.AreEqual (tag.ComposerSort, reread.ComposerSort, label);
				Assert.AreEqual (string.Join ("|", tag.AlbumArtists), string.Join ("|", reread.AlbumArtists), label);
				if (name == "sorts set") {
					Assert.AreEqual ("x", tag.ArtistSort, label);
					Assert.AreEqual ("y", tag.AlbumArtistSort, label);
					Assert.AreEqual ("z", tag.ComposerSort, label);
				}

				if (name == "sorts cleared") {
					Assert.IsNull (tag.ArtistSort, label);
					Assert.IsNull (tag.AlbumArtistSort, label);
					Assert.IsNull (tag.ComposerSort, label);
				}
			}
		}
	}

	[TestMethod]
	public void ValueClear_OfARepeat_LeavesTheBroaderValuesSort ()
	{
		// The 70 ARTIST still applies to the track once the album's repeat goes (tagging.html, "Target types"), so
		// its own ARTIST_SORT stays with it: a clear takes its own Tag's sorts and the value-less ones above it.
		foreach (var albumArtistSort in new[] { false, true }) {
			var flat = albumArtistSort ? "ALBUM_ARTIST_SORT" : "ARTIST_SORT";
			var tag = MatroskaFile.Read (FileWith (Tags (
				Tag (70, Simple ("ARTIST", "Ada"), Simple (flat, "x")),
				Tag (50, Simple ("ARTIST", "Ada"), Simple ("TITLE", "Al")),
				Tag (30, Simple ("TITLE", "S"))))).File!.Tag;
			var before = albumArtistSort ? tag.AlbumArtistSort : tag.ArtistSort;

			tag.AlbumArtists = [];

			var again = MatroskaFile.Read (FileWith (tag.Render ().ToArray ())).File!.Tag;
			Assert.AreEqual ("Ada", again.Artist);
			Assert.AreEqual ("x", before);
			Assert.AreEqual ("x", albumArtistSort ? FirstTagString (tag, 70, flat) : again.ArtistSort, flat);
		}
	}

	[TestMethod]
	public void ValueSet_EqualToTheBroaderValue_RepeatsItsSortWith ()
	{
		// The repeat compares the texts the getter reads, the default-language ones (rule 2): a French name beside
		// the collection artist does not make AlbumArtists = ["Coll"] another value.
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (70, SimpleSorted ("ARTIST", "Coll", "coll"), Simple ("ARTIST", "Coll-fr", isDefault: false)),
			Tag (30, Simple ("TITLE", "S"))))).File!.Tag;

		tag.AlbumArtists = ["Coll"];

		Assert.AreEqual ("coll", tag.ArtistSort);
		Assert.AreEqual ("coll", tag.AlbumArtistSort);

		// The track ARTIST shadows the collection's too when the item is below the track.
		var part = MatroskaFile.Read (FileWith (Tags (
			Tag (70, SimpleSorted ("ARTIST", "Coll", "coll")),
			Tag (20, Simple ("ARTIST", "Part"))))).File!.Tag;

		part.Artist = "Coll";

		var bytes = FileWith (part.Render ().ToArray ());
		Assert.IsTrue (FileTargetHas (bytes, 30, "ARTIST"));
		Assert.AreEqual (2, CountOf (bytes, "coll"u8.ToArray ()), "the track's repeat carries the SORT_WITH");
	}

	static string? FirstTagString (MatroskaTag tag, ulong level, string name)
	{
		var layout = ModelLayout (tag.Render ().ToArray ());
		return layout.TryGetValue (level, out var tags) ? tags.FirstOrDefault (t => t.Name == name)?.Text : null;
	}

	[TestMethod]
	public void ValueSet_ForAnAlbumTitle_DoesNotRepeatACollectionSort ()
	{
		// Album reads level 50 alone, so an album title equal to the collection's does not shadow it: the
		// collection title's SORT_WITH stays its own (rule 1).
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (70, SimpleSorted ("TITLE", "Coll", "coll sort")), Tag (30, Simple ("TITLE", "S"))))).File!.Tag;

		tag.Album = "Coll";

		Assert.IsNull (tag.AlbumSort);
		Assert.AreEqual (1, CountOf (tag.Render ().ToArray (), "coll sort"u8.ToArray ()));
	}

	[TestMethod]
	public void Runs_CountAValueWithoutText_AsEmpty ()
	{
		// A SimpleTag with no TagString holds an empty value, the same as TagString "" (RFC 9559 section 5.1.8.1.2).
		var noText = Master (0x67C8, Element (0x45A3, "ARTIST"u8));
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, noText, Simple ("ARTIST_SORT", "s")),
			Tag (30, Simple ("TITLE", "S"), Simple ("ARTIST", ""))))).File!.Tag;

		Assert.AreEqual ("s", tag.ArtistSort);

		// The same when no value is in the default language, so the run compares every language's values.
		var other = Master (0x67C8, Element (0x45A3, "ARTIST"u8), Element (0x4484, new byte[] { 0 }));
		var languages = MatroskaFile.Read (FileWith (Tags (
			Tag (50, other, Simple ("ARTIST_SORT", "s")),
			Tag (30, Simple ("TITLE", "S"), Simple ("ARTIST", "", isDefault: false))))).File!.Tag;
		Assert.AreEqual ("s", languages.ArtistSort);
	}

	[TestMethod]
	public void CustomField_ForASortName_BehavesAsTheSortProperty ()
	{
		// A custom field named ARTIST_SORT (or any flat sort name) reads, sets and removes what the sort property
		// does, on every ownership shape: the same value read and the same bytes after.
		var failures = new List<string> ();
		foreach (var pair in SortPairs) {
			foreach (var (shape, data) in OwnershipShapes (pair)) {
				var byProperty = MatroskaFile.Read (data).File!.Tag;
				var byName = MatroskaFile.Read (data).File!.Tag;
				var read = ReadSort (byProperty, pair);
				if (!byName.GetCustomField (pair.Flat).SequenceEqual (read is null ? [] : [read]))
					failures.Add ($"{pair.Name} {shape}: get");

				foreach (var value in new[] { "NEW", null }) {
					var p = MatroskaFile.Read (data).File!.Tag;
					var n = MatroskaFile.Read (data).File!.Tag;
					SetSortOf (p, pair, value);
					if (value is null)
						n.RemoveCustomField (pair.Flat);
					else
						n.SetCustomField (pair.Flat, value);
					if (ReadSort (n, pair) != ReadSort (p, pair) || !n.Render ().ToArray ().AsSpan ().SequenceEqual (p.Render ().ToArray ()))
						failures.Add ($"{pair.Name} {shape}: {(value is null ? "remove" : "set")}");
				}
			}
		}

		var titles = MatroskaFile.Read (FileWith (Tags (Tag (50, SimpleSorted ("TITLE", "Al", "al")), Tag (30, Simple ("TITLE", "S"), Simple ("TITLE_SORT", "s"))))).File!.Tag;
		Assert.AreEqual ("s", string.Join ("|", titles.GetCustomField ("TITLE_SORT")));
		Assert.AreEqual ("al", string.Join ("|", titles.GetCustomField ("ALBUM_SORT")));
		Assert.IsFalse (titles.SetCustomField ("ARTIST_SORT", "a", "b"), "a sort is one name");
		Assert.IsTrue (titles.SetCustomField ("TITLE_SORT", "t"));
		Assert.AreEqual ("t", titles.TitleSort);
		Assert.IsTrue (titles.RemoveCustomField ("ALBUM_SORT"));
		Assert.IsNull (titles.AlbumSort);
		Assert.IsEmpty (failures, $"{failures.Count} differ, e.g. {string.Join ("; ", failures.Take (5))}");
	}

	static void SetSortOf (MatroskaTag tag, SortPair pair, string? value)
	{
		switch (pair.Getter) {
			case Getter.Artist:
				tag.ArtistSort = value;
				break;
			case Getter.Composer:
				tag.ComposerSort = value;
				break;
			default:
				tag.AlbumArtistSort = value;
				break;
		}
	}
}
