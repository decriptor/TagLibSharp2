// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Matroska;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Matroska;

// The sort ownership model, as an oracle that reads rendered bytes (the PR body states it in prose):
// - A nested SORT_WITH sorts the SimpleTag it is in (tagging.html, "Nested Information").
// - A flat X_SORT sorts the X in its own Tag (the ffmpeg convention). A Tag with no X passes its X_SORT down.
// - Climbing from the item, levels form runs of one value. A broader Tag that repeats the same X is the
//   same value (tagging.html, "Target types": "you can also repeat the value for each track"); a Tag with a
//   different X starts the next run. The item's sort is the nested sort on the X it reads, else the first
//   flat X_SORT in its run.
// - The album artist of a file with a track level is the ARTIST at 50, so its run starts at 50 (with no
//   ARTIST there, the album has none) and climbs the same way.
// - Only file-level Tags count: a Tag that targets a chapter, attachment, track or edition is another item's.
// Every operation must keep: the library reads what the oracle reads; a write-back changes no byte; a copy
// of the values read changes no value; a sort set leaves exactly one copy in the item's run, in the value's
// Tag (at 50 or below), and a clear leaves none; a Tag of another item is untouched; and no other value's
// sort changes. A value write that makes two levels equal joins their runs and one that makes them differ
// splits them; only then may the item's sort change with its value unchanged.
public partial class MatroskaFileTests
{
	sealed record ModelTag (string Name, string Text, List<string> Sorts);

	sealed record ModelRun (string Key, ulong Head, string? Sort, List<ulong> Levels);

	[TestMethod]
	public void SortModel_HoldsForEveryShapeAndOperation ()
	{
		var failures = new List<string> ();
		var cases = 0;
		foreach (var pair in SortPairs) {
			foreach (var (shape, data) in ModelShapes (pair)) {
				foreach (var op in ModelOps (pair)) {
					cases++;
					var label = $"{pair.Name} {shape}: {op.Name}";
					try {
						CheckModel (pair, data, op, failures, label);
					} catch (AssertFailedException e) {
						failures.Add ($"{label}: {e.Message}");
					}
				}
			}
		}

		Assert.IsTrue (cases > 10000, $"only {cases} cases");
		if (Environment.GetEnvironmentVariable ("SORT_MODEL_DUMP") is string dump)
			File.WriteAllLines (dump, failures);
		Assert.IsEmpty (failures, $"{failures.Count} of {cases} cases broke the model, e.g.\n{string.Join ("\n", failures.Take (8))}");
	}

	sealed record ModelOp (string Name, Action<MatroskaTag> Apply, string Kind, string? Expected = null, Action<MatroskaTag>? Then = null);

	static ModelOp[] ModelOps (SortPair pair)
	{
		Action<MatroskaTag, string?> setSort = pair.Getter switch {
			Getter.Artist => (t, v) => t.ArtistSort = v,
			Getter.Composer => (t, v) => t.ComposerSort = v,
			_ => (t, v) => t.AlbumArtistSort = v,
		};
		var ops = new List<ModelOp> {
			new ("sort = NEW", t => setSort (t, "NEW"), "set", "NEW"),
			new ("sort = null", t => setSort (t, null), "clear"),
			new ("sort = empty", t => setSort (t, ""), "clear"),
			new ("sort written back", t => setSort (t, ReadSort (t, pair)), "writeback"),
			new ("every value and sort written back", WriteEverythingBack, "writebackall"),
			new ("CopyTo of the values read", t => { var copy = new VorbisComment (); t.CopyTo (copy); copy.CopyTo (t); }, "copyread"),
			new ("sort = NEW, then the value", t => setSort (t, "NEW"), "set", "NEW", t => SetValueOf (t, pair, "NEWV")),
			new ("Title = NEWV", t => t.Title = "NEWV", "unrelated"),
			new ("Genre = NEWV", t => t.Genre = "NEWV", "unrelated"),
		};
		// Writes of each text a shape holds, so a write can make two levels equal or different.
		foreach (var text in new[] { $"{pair.Value}30", $"{pair.Value}50", $"{pair.Value}70", $"{pair.Value}A", $"{pair.Value}B", pair.Value })
			ops.Add (new ($"value = {text}", t => SetValueOf (t, pair, text), "value"));
		if (pair.Getter is Getter.Artist) {
			foreach (var text in new[] { "ARTIST30", "ARTIST50", "ARTIST70", "ARTISTA", "ARTISTB", "ARTIST" })
				ops.Add (new ($"AlbumArtists = {text}", t => t.AlbumArtists = [text], "value"));
		}
		switch (pair.Getter) {
			case Getter.Artist:
				ops.Add (new ("Artist = NEWV", t => t.Artist = "NEWV", "value"));
				ops.Add (new ("Artist = the artist read", t => t.Artist = t.Artist, "writeback"));
				ops.Add (new ("CopyTo Artist only, as read", t => new VorbisComment { Artist = t.Artist }.CopyTo (t), "copyone"));
				ops.Add (new ("CopyTo Artist and sort", t => new VorbisComment { Artist = "NEWV", ArtistSort = "NEWS" }.CopyTo (t), "copy", "NEWS"));
				ops.Add (new ("AlbumArtists = NEWV", t => t.AlbumArtists = ["NEWV"], "value"));
				ops.Add (new ("AlbumArtists = []", t => t.AlbumArtists = [], "valueclear"));
				ops.Add (new ("Artist = null", t => t.Artist = null, "valueclear"));
				ops.Add (new ("Performers = []", t => t.Performers = [], "valueclear"));
				break;
			case Getter.Composer:
				ops.Add (new ("Composers = NEWV", t => t.Composers = ["NEWV"], "value"));
				ops.Add (new ("CopyTo Composers and sort", t => new VorbisComment { Composers = ["NEWV"], ComposerSort = "NEWS" }.CopyTo (t), "copy", "NEWS"));
				ops.Add (new ("Composers = []", t => t.Composers = [], "valueclear"));
				break;
			default:
				ops.Add (new ("AlbumArtists = NEWV", t => t.AlbumArtists = ["NEWV"], "value"));
				ops.Add (new ("AlbumArtists = []", t => t.AlbumArtists = [], "valueclear"));
				break;
		}

		return [.. ops];
	}

	static void SetValueOf (MatroskaTag t, SortPair pair, string text)
	{
		switch (pair.Getter) {
			case Getter.Artist:
				t.Artist = text;
				break;
			case Getter.Composer:
				t.Composers = [text];
				break;
			default:
				t.AlbumArtists = [text];
				break;
		}
	}

	static string ModelState (MatroskaTag t, bool values = true) =>
		string.Join (" | ", t.Artist, values ? string.Join (",", t.Performers) : "", string.Join (",", t.AlbumArtists), string.Join (",", t.Composers),
			values ? t.ArtistSort : "", t.AlbumArtistSort, t.ComposerSort, t.Title, t.Album, t.Genre);

	static void WriteEverythingBack (MatroskaTag t)
	{
		t.Artist = t.Artist;
		t.Performers = t.Performers;
		t.AlbumArtists = t.AlbumArtists;
		t.Composers = t.Composers;
		t.ArtistSort = t.ArtistSort;
		t.AlbumArtistSort = t.AlbumArtistSort;
		t.ComposerSort = t.ComposerSort;
		t.Title = t.Title;
		t.Genre = t.Genre;
	}

	static void CheckModel (SortPair pair, byte[] data, ModelOp op, List<string> failures, string label)
	{
		var tag = MatroskaFile.Read (data).File!.Tag;
		var beforeBytes = tag.Render ().ToArray ();
		var before = ModelLayout (beforeBytes);
		var (beforeKey, beforeSort, beforeRuns) = Oracle (before, pair);
		Assert.AreEqual (beforeSort, ReadSort (tag, pair), "the library reads what the model reads");

		op.Apply (tag);

		var afterBytes = tag.Render ().ToArray ();
		var after = ModelLayout (afterBytes);
		CollectionAssert.AreEqual (OtherItemTags (beforeBytes), OtherItemTags (afterBytes), "a Tag of a chapter, attachment, track or edition changed");
		var (afterKey, afterSort, afterRuns) = Oracle (after, pair);
		Assert.AreEqual (afterSort, ReadSort (tag, pair), "after: the library reads what the model reads");
		Assert.AreEqual (afterSort, ReadSort (MatroskaFile.Read (FileWith (afterBytes)).File!.Tag, pair), "after a render");

		if (op.Then is not null) {
			// The sort sits in its value's Tag, so a later write of the item's value leaves it there.
			var held = SortLevels (after, op.Expected!);
			op.Then (tag);
			var final = ModelLayout (tag.Render ().ToArray ());
			CollectionAssert.IsSubsetOf (held, SortLevels (final, op.Expected!), $"the sort left {string.Join (",", held)}");
		}

		// Writing back every value is a write-back, except where rule 8 makes it a clear: an empty list read from a
		// whole-file field (Composers, a flat ALBUM_ARTIST) while a broader level still holds text (#136).
		var kind = op.Kind == "writebackall" ? (EmptyReadOverText (before, pair) ? "valueclear" : "writeback") : op.Kind;
		switch (kind) {
			case "writeback":
				CollectionAssert.AreEqual (beforeBytes, afterBytes, "a write-back changed bytes");
				return;
			case "copyread":
				// A copy replaces (Tag.IsWriteBack): the bytes may change, but every value and sort reads the same,
				// and a sort the copy wrote leaves one copy.
				Assert.AreEqual (ModelState (MatroskaFile.Read (FileWith (beforeBytes)).File!.Tag), ModelState (tag), "a copy of the values read changed what is read");
				if (afterSort is not null && beforeBytes.AsSpan ().SequenceEqual (afterBytes) is false)
					AssertOneCopy (after, pair, afterSort);
				break;
			case "copyone":
				// One artist copied over a list replaces the list (a value write); nothing else changes.
				Assert.AreEqual (ModelState (MatroskaFile.Read (FileWith (beforeBytes)).File!.Tag, values: false), ModelState (tag, values: false), "a copy of the artist read changed another property");
				break;
			case "unrelated":
				// A write to another property, even one that adds a track level, leaves every value and sort of the
				// pair where it was; what each getter reads is then the model's read of that layout (checked above).
				CollectionAssert.AreEqual (PairLayout (before, pair), PairLayout (after, pair), "another property's write moved a value or sort of the pair");
				return;
			case "set":
				Assert.AreEqual (op.Expected, afterSort, "the set reads back");
				AssertOneCopy (after, pair, op.Expected!);
				AssertInTheValuesTag (after, pair, op.Expected!);
				break;

			case "clear":
				Assert.IsNull (afterSort, "the clear reads back");
				AssertNoCopy (after, pair, beforeSort);
				break;
			case "copy":
				Assert.AreEqual ("NEWV", afterKey, "the copied value is read");
				Assert.AreEqual (op.Expected, afterSort, "the copied sort is read");
				AssertOneCopy (after, pair, op.Expected!);
				break;
		}

		// A value write moves sorts only as rule 7 says, so the whole layout of the pair's sorts is predicted from the
		// layout before. A sort write changes no other value's own sort.
		if (kind is "value" or "valueclear" or "copy" or "copyone")
			AssertValueWriteFollowsTheModel (before, after, pair, skipItemRun: op.Kind == "copy");
		else
			AssertOtherSortsKept (before, after, pair, beforeRuns);
	}

	static bool EmptyReadOverText (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair)
	{
		var name = ValueName (layout, pair);
		if (name is not ("COMPOSER" or "ALBUM_ARTIST"))
			return false;
		var read = layout.Values.FirstOrDefault (tags => tags.Exists (t => t.Name == name));
		return read is not null && read.TrueForAll (t => t.Name != name || t.Text.Length == 0)
			&& layout.Values.Any (tags => tags.Exists (t => t.Name == name && t.Text.Length > 0));
	}

	static string? ValuesAt (SortedDictionary<ulong, List<ModelTag>> layout, ulong level, string name) =>
		layout.TryGetValue (level, out var tags) && tags.Exists (t => t.Name == name)
			? string.Join ("|", tags.Where (t => t.Name == name).Select (t => t.Text)) : null;

	// Every level's values, flat sorts and nested sorts of the pair, as text.
	static List<string> PairLayout (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair)
	{
		var name = ValueName (layout, pair);
		var flat = FlatName (layout, pair);
		return [.. layout.Select (l => $"{l.Key}: {string.Join (",", l.Value.Where (t => t.Name == name).Select (t => $"{t.Text}({string.Join ("/", t.Sorts)})"))} | {string.Join (",", l.Value.Where (t => t.Name == flat).Select (t => t.Text))}")
			.Where (line => !line.EndsWith (":  | ", StringComparison.Ordinal))];
	}

	/// <summary>
	/// The sort a value's head level owns: the nested sort on its SimpleTag, else the first flat sort from it up
	/// through value-less levels and levels that repeat it (rules 2 and 4).
	/// </summary>
	static string? OwnSort (SortedDictionary<ulong, List<ModelTag>> layout, ulong head, string name, string flat)
	{
		if (!layout.TryGetValue (head, out var headTags))
			return null;
		var nested = headTags.Where (t => t.Name == name && t.Text.Length > 0).SelectMany (t => t.Sorts).FirstOrDefault (s => s.Length > 0);
		if (nested is not null)
			return nested;
		var value = ValuesAt (layout, head, name);
		foreach (var level in layout.Keys.Where (l => l >= head)) {
			var at = ValuesAt (layout, level, name);
			if (level > head && at is not null && at != value)
				break;
			var flats = layout[level].Where (t => t.Name == flat).ToList ();
			if (flats.Count > 0)
				return flats.Select (t => t.Text).FirstOrDefault (t => t.Length > 0);
		}

		return null;
	}

	// A sort write sets or clears the item's sort; every other value keeps its own.
	static void AssertOtherSortsKept (SortedDictionary<ulong, List<ModelTag>> before, SortedDictionary<ulong, List<ModelTag>> after, SortPair pair, List<ModelRun> beforeRuns)
	{
		var name = ValueName (before, pair);
		var flat = FlatName (before, pair);
		// The item's own run is the one written. A split (rule 10) gives the item a copy of its value, and the
		// broader value it copied keeps its own sort.
		var item = ItemRun (before, pair).Head;
		var split = item is not null && ItemRun (after, pair).Head is ulong now && now != item;
		foreach (var run in beforeRuns) {
			if (run == beforeRuns[0] && !split)
				continue;
			Assert.AreEqual (OwnSort (before, run.Head, name, flat), OwnSort (after, run.Head, name, flat), $"the {run.Key} at {run.Head} lost or changed its sort");
		}
	}

	/// <summary>
	/// Rule 7 applied to the layout before, compared with the layout after. A rename keeps its sorts. A cleared
	/// value takes its own Tag's flat sorts and those of the value-less broader Tags up to the next value, except
	/// the one an equal narrower value reads. A sort the narrower value read from at or above the written level,
	/// which is no longer in its run, moves down to it. A whole-file field (Composers, a flat ALBUM_ARTIST) is
	/// written where it is read, and its clear removes every value and every sort with them. A value written
	/// equal to the broader value it shadows repeats that value's nested sorts. Nested sorts elsewhere stay.
	/// </summary>
	static void AssertValueWriteFollowsTheModel (SortedDictionary<ulong, List<ModelTag>> before, SortedDictionary<ulong, List<ModelTag>> after, SortPair pair, bool skipItemRun)
	{
		var name = ValueName (before, pair);
		var flat = FlatName (before, pair);
		var wholeFile = name is "COMPOSER" or "ALBUM_ARTIST";
		var fixedAlbum = pair.Getter is Getter.AlbumArtistByArtist && !ByAlbumArtistName (before, pair);
		var levels = before.Keys.Union (after.Keys).OrderBy (l => l).ToList ();
		var predicted = levels.ToDictionary (l => l, l => before.TryGetValue (l, out var tags) ? tags.Where (t => t.Name == flat).Select (t => t.Text).ToList () : []);
		var written = levels.Where (l => ValuesAt (before, l, name) != ValuesAt (after, l, name)).ToList ();

		if (wholeFile) {
			if (written.Count > 0 && levels.TrueForAll (l => ValuesAt (after, l, name) is null))
				foreach (var level in levels)
					predicted[level].Clear ();
		} else {
			foreach (var at in written) {
				// The narrower value is the lowest one, below the written level; the sort it reads is the first flat in its run.
				var narrower = fixedAlbum ? null : levels.Where (l => l < at && ValuesAt (before, l, name) is not null).Cast<ulong?> ().FirstOrDefault ();
				ulong? read = narrower is null ? null : ItemRun (before, pair).Levels.Where (l => predicted[l].Count > 0).Cast<ulong?> ().FirstOrDefault ();
				var kept = read is ulong r && r >= at ? read : null;
				if (ValuesAt (before, at, name) is not null && ValuesAt (after, at, name) is null) {
					foreach (var level in levels.Where (l => l >= at)) {
						if (level > at && ValuesAt (before, level, name) is not null)
							break;
						if (level != kept)
							predicted[level].Clear ();
					}
				}

				if (kept is ulong from && narrower is ulong to) {
					var stillRead = ItemRun (after, pair).Levels.Where (l => predicted[l].Count > 0).Cast<ulong?> ().FirstOrDefault ();
					if (stillRead != from && predicted[to].Count == 0) {
						predicted[to] = predicted[from];
						predicted[from] = [];
					}
				}
			}
		}

		// A copy then sets the item's sort, which rewrites the flats of the item's run: the levels from the item up,
		// through every level without a value (a Tag the set emptied is gone) or with the item's value.
		var skip = new HashSet<ulong> ();
		if (skipItemRun) {
			var itemValue = levels.Select (l => ValuesAt (after, l, name)).FirstOrDefault (v => v is not null);
			foreach (var level in levels) {
				var at = ValuesAt (after, level, name);
				if (at is not null && at != itemValue)
					break;
				skip.Add (level);
			}
		}
		foreach (var level in levels.Where (l => !skip.Contains (l))) {
			var actual = after.TryGetValue (level, out var tags) ? tags.Where (t => t.Name == flat).Select (t => t.Text).Order ().ToList () : [];
			CollectionAssert.AreEqual (predicted[level].Order ().ToList (), actual, $"level {level}: flat sorts [{string.Join (",", actual)}] where the model puts [{string.Join (",", predicted[level])}]");

			string Nested (SortedDictionary<ulong, List<ModelTag>> layout, ulong at) =>
				layout.TryGetValue (at, out var found) ? string.Join (",", found.Where (t => t.Name == name).Select (t => string.Join ("/", t.Sorts))) : "";
			if (!written.Contains (level)) {
				Assert.AreEqual (Nested (before, level), Nested (after, level), $"level {level}: a nested sort changed");
			} else if (!wholeFile && ValuesAt (before, level, name) is null && ValuesAt (after, level, name) is string now) {
				var broader = levels.Where (l => l > level && ValuesAt (after, l, name) is not null).Cast<ulong?> ().FirstOrDefault ();
				if (broader is ulong b && ValuesAt (after, b, name) == now)
					Assert.AreEqual (Nested (before, b), Nested (after, level), $"level {level}: a repeat of the {b} value lacks its nested sorts");
			}
		}
	}

	// The pair the library uses for the album artist depends on the file: ALBUM_ARTIST when it has one.
	static bool ByAlbumArtistName (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair) =>
		pair.Getter is Getter.AlbumArtistByName or Getter.AlbumArtistByArtist && layout.Values.Any (tags => tags.Exists (t => t.Name == "ALBUM_ARTIST"));

	static string ValueName (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair) =>
		pair.Getter is Getter.AlbumArtistByName or Getter.AlbumArtistByArtist ? (ByAlbumArtistName (layout, pair) ? "ALBUM_ARTIST" : "ARTIST") : pair.Value;

	static string FlatName (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair) => pair.Flat;

	/// <summary>The model's read: the item's value key, its sort, and every run from the item up.</summary>
	static (string? Key, string? Sort, List<ModelRun> Runs) Oracle (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair)
	{
		var name = ValueName (layout, pair);
		var flat = FlatName (layout, pair);
		var levels = layout.Keys.ToList ();
		var item = levels.Count == 0 ? 50UL : levels[0];
		var fixedAlbum = pair.Getter is Getter.AlbumArtistByArtist or Getter.AlbumArtistByName && !ByAlbumArtistName (layout, pair) && item < 50;

		string Values (ulong level) => string.Join ("|", layout[level].Where (t => t.Name == name).Select (t => t.Text));
		bool HasValue (ulong level) => layout[level].Exists (t => t.Name == name);
		string? FirstFlat (ulong level)
		{
			var flats = layout[level].Where (t => t.Name == flat).ToList ();
			return flats.Count == 0 ? null : flats.Select (t => t.Text).FirstOrDefault (t => t.Length > 0);
		}

		(bool Found, string? Sort) FirstFlatOrBlock (ulong level) => layout[level].Exists (t => t.Name == flat) ? (true, FirstFlat (level)) : (false, null);

		string? NestedOf (ulong level)
		{
			var parent = layout[level].FirstOrDefault (t => t.Name == name && t.Text.Length > 0);
			return parent?.Sorts.FirstOrDefault (s => s.Length > 0);
		}

		if (fixedAlbum) {
			var album = AlbumRun (layout, name);
			var key = layout.ContainsKey (50) && HasValue (50) ? Values (50) : null;
			var sort = (key is null ? null : NestedOf (50)) ?? album.Select (FirstFlatOrBlock).FirstOrDefault (s => s.Found).Sort;
			return (key, sort, [new ModelRun (key ?? "", 50, sort, album)]);
		}

		// Split the levels into runs: levels below the first value join its run; a level with a different value starts the next.
		var runs = new List<(string? Key, ulong Head, List<ulong> Levels)> ();
		var pending = new List<ulong> ();
		foreach (var level in levels) {
			if (!HasValue (level)) {
				if (runs.Count == 0)
					pending.Add (level);
				else
					runs[^1].Levels.Add (level);
				continue;
			}

			var key = Values (level);
			if (runs.Count > 0 && runs[^1].Key == key) {
				runs[^1].Levels.Add (level);
				continue;
			}

			var members = runs.Count == 0 ? new List<ulong> (pending) { level } : [level];
			runs.Add ((key, level, members));
		}

		if (runs.Count == 0) {
			// No value at all: every flat sort is the item's.
			var orphan = pending.Select (FirstFlatOrBlock).FirstOrDefault (s => s.Found);
			return (null, orphan.Sort, []);
		}


		// The item reads its run from the item up. A value's own sort, compared across an edit, is its head
		// Tag's and above: a sort in a narrower Tag that only the item inherited is the item's.
		string? SortOf (string? key, ulong head, IEnumerable<ulong> reads) =>
			NestedOf (head) ?? reads.Select (FirstFlatOrBlock).FirstOrDefault (s => s.Found).Sort;
		var model = new List<ModelRun> ();
		foreach (var (key, head, members) in runs)
			model.Add (new ModelRun (key!, head, SortOf (key, head, members.Where (l => l >= head)), members));
		return (runs[0].Key, SortOf (runs[0].Key, runs[0].Head, runs[0].Levels), model);
	}

	// One sort for the item's value: the nested one on the SimpleTag read with no flat left in its run, or one flat.
	static void AssertOneCopy (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair, string expected)
	{
		var (flats, nested) = ItemRunSorts (layout, pair);
		if (nested == expected)
			Assert.IsEmpty (flats, $"a nested sort left flat copies: {string.Join (",", flats)}");
		else
			CollectionAssert.AreEqual (new[] { expected }, flats, "the run holds one flat copy of the new sort");
	}

	static void AssertNoCopy (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair, string? old)
	{
		var (flats, nested) = ItemRunSorts (layout, pair);
		Assert.IsEmpty (flats.Where (f => f.Length > 0), $"the clear left {string.Join (",", flats)} for another reader");
		Assert.IsNull (nested, "the clear left a nested sort");
		if (old is not null)
			Assert.IsFalse (flats.Contains (old));
	}

	static HashSet<ulong> ItemRunLevels (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair) => [.. ItemRun (layout, pair).Levels];

	static (List<string> Flats, string? Nested) ItemRunSorts (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair)
	{
		var (run, head) = ItemRun (layout, pair);
		var name = ValueName (layout, pair);
		var flat = FlatName (layout, pair);
		var flats = run.SelectMany (l => layout[l].Where (t => t.Name == flat).Select (t => t.Text)).ToList ();
		var nested = head is ulong h ? layout[h].FirstOrDefault (t => t.Name == name && t.Text.Length > 0)?.Sorts.FirstOrDefault (s => s.Length > 0) : null;
		return (flats, nested);
	}

	static (List<ulong> Levels, ulong? Head) ItemRun (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair)
	{
		var name = ValueName (layout, pair);
		var levels = layout.Keys.ToList ();
		if (levels.Count == 0)
			return ([], null);
		var item = levels[0];
		var fixedAlbum = pair.Getter is Getter.AlbumArtistByArtist or Getter.AlbumArtistByName && !ByAlbumArtistName (layout, pair) && item < 50;
		var run = new List<ulong> ();
		string? value = null;
		ulong? head = null;
		if (fixedAlbum)
			return (AlbumRun (layout, name), layout.TryGetValue (50, out var album) && album.Exists (t => t.Name == name) ? 50UL : null);
		foreach (var level in levels) {
			var tags = layout[level];
			if (tags.Exists (t => t.Name == name)) {
				var key = string.Join ("|", tags.Where (t => t.Name == name).Select (t => t.Text));
				if (value is null) {
					value = key;
					head = level;
				} else if (value != key) {
					break;
				}
			}

			run.Add (level);
		}

		return (run, head);
	}

	static List<ulong> SortLevels (SortedDictionary<ulong, List<ModelTag>> layout, string sort) =>
		[.. layout.Where (l => l.Value.Exists (t => t.Text == sort || t.Sorts.Contains (sort))).Select (l => l.Key)];

	// The album's run: level 50 and each broader level up to the first whose value is not 50's.
	static List<ulong> AlbumRun (SortedDictionary<ulong, List<ModelTag>> layout, string name)
	{
		// With no ARTIST at 50 the album has none, and any broader value, an empty one too, is another value's.
		var album = ValuesAt (layout, 50, name);
		var run = new List<ulong> ();
		foreach (var level in layout.Keys.Where (l => l >= 50)) {
			if (level > 50 && ValuesAt (layout, level, name) is string value && value != album)
				break;
			run.Add (level);
		}

		return run;
	}

	// The sort set sits in the Tag of the value it sorts when that is at 50 or below, else at 50 or below.
	static void AssertInTheValuesTag (SortedDictionary<ulong, List<ModelTag>> layout, SortPair pair, string expected)
	{
		var (run, head) = ItemRun (layout, pair);
		var flat = FlatName (layout, pair);
		var at = run.Where (l => layout[l].Exists (t => t.Name == flat && t.Text == expected)).ToList ();
		if (at.Count == 0)
			return;
		if (head is ulong h && h <= 50)
			CollectionAssert.AreEqual (new[] { h }, at, "the flat sort is not in its value's Tag");
		else
			Assert.IsTrue (at.TrueForAll (l => l <= 50), $"the flat sort is at {string.Join (",", at)}, above the album");
	}

	static bool IsOtherItems (ReadOnlySpan<byte> targets)
	{
		for (var i = 0; i < targets.Length && EbmlReader.TryRead (targets, i, targets.Length, out var child); i = child.End) {
			if (child.Id is 0x63C5 or 0x63C9 or 0x63C4 or 0x63C6 && targets.Slice (child.DataOffset, (int)child.DataLength).IndexOfAnyExcept ((byte)0) >= 0)
				return true;
		}

		return false;
	}

	// The Tags that target a chapter, attachment, track or edition, as bytes.
	static List<string> OtherItemTags (byte[] tagsElement)
	{
		var found = new List<string> ();
		if (tagsElement.Length == 0 || !EbmlReader.TryRead (tagsElement, 0, tagsElement.Length, out var tags))
			return found;
		var body = tagsElement.AsSpan (tags.DataOffset, (int)tags.DataLength);
		for (var at = 0; at < body.Length && EbmlReader.TryRead (body, at, body.Length, out var tag); at = tag.End) {
			var inner = body.Slice (tag.DataOffset, (int)tag.DataLength);
			for (var i = 0; i < inner.Length && EbmlReader.TryRead (inner, i, inner.Length, out var child); i = child.End) {
				if (child.Id == MatroskaIds.Targets && IsOtherItems (inner.Slice (child.DataOffset, (int)child.DataLength)))
					found.Add (Convert.ToHexString (body[at..tag.End]));
			}
		}

		return found;
	}

	static SortedDictionary<ulong, List<ModelTag>> ModelLayout (byte[] tagsElement)
	{
		var layout = new SortedDictionary<ulong, List<ModelTag>> ();
		if (tagsElement.Length == 0 || !EbmlReader.TryRead (tagsElement, 0, tagsElement.Length, out var tags))
			return layout;
		var body = tagsElement.AsSpan (tags.DataOffset, (int)tags.DataLength);
		for (var at = 0; at < body.Length && EbmlReader.TryRead (body, at, body.Length, out var tag); at = tag.End) {
			if (tag.Id != MatroskaIds.Tag)
				continue;
			var inner = body.Slice (tag.DataOffset, (int)tag.DataLength);
			ulong level = 50;
			var other = false;
			var entries = new List<ModelTag> ();
			for (var i = 0; i < inner.Length && EbmlReader.TryRead (inner, i, inner.Length, out var child); i = child.End) {
				var payload = inner.Slice (child.DataOffset, (int)child.DataLength);
				if (child.Id == MatroskaIds.Targets) {
					level = TargetLevel (payload) ?? 50;
					other = IsOtherItems (payload);
				} else if (child.Id == MatroskaIds.SimpleTag) {
					entries.Add (ModelTagOf (payload));
				}
			}

			if (other)
				continue;

			if (!layout.TryGetValue (level, out var list))
				layout[level] = list = [];
			list.AddRange (entries);
		}

		return layout;
	}

	static ModelTag ModelTagOf (ReadOnlySpan<byte> simple)
	{
		var name = "";
		var text = "";
		var sorts = new List<string> ();
		for (var i = 0; i < simple.Length && EbmlReader.TryRead (simple, i, simple.Length, out var child); i = child.End) {
			var payload = simple.Slice (child.DataOffset, (int)child.DataLength);
			if (child.Id == MatroskaIds.TagName)
				name = Encoding.UTF8.GetString (payload);
			else if (child.Id == MatroskaIds.TagString)
				text = Encoding.UTF8.GetString (payload);
			else if (child.Id == MatroskaIds.SimpleTag) {
				var inner = ModelTagOf (payload);
				if (inner.Name == "SORT_WITH")
					sorts.Add (inner.Text);
			}
		}

		return new ModelTag (name, text, sorts);
	}

	// The 432 ownership shapes with distinct texts per level, the same 432 with one text repeated at every
	// level, and the shapes the reviews built (ffmpeg's untargeted Tag, repeats, value-less sorts at 60/70).
	static IEnumerable<(string Name, byte[] Data)> ModelShapes (SortPair pair)
	{
		foreach (var shape in OwnershipShapes (pair))
			yield return shape;
		foreach (var (name, data) in OwnershipShapes (pair, sameText: true))
			yield return ($"same {name}", data);
		// Empty texts (E): an empty value is a value, and "" is the value read only when no value holds text.
		foreach (var pattern in new[] { "EBC", "AEC", "ABE", "EEC", "AEE", "EEE", "EAA", "AEA" }) {
			foreach (var (name, data) in OwnershipShapes (pair, pattern: pattern, fillerOnly: true))
				yield return ($"empty {name}", data);
		}

		foreach (var pattern in new[] { "AAB", "ABA", "ABB" }) {
			foreach (var (name, data) in OwnershipShapes (pair, pattern: pattern))
				yield return ($"mixed {name}", data);
		}

		// Tags of other items, holding the pair's names, beside every distinct shape with a track title.
		byte[][] others = [
			ChapterTag (30, Chapter (1), Simple (pair.Value, "Ch"), Simple (pair.Flat, "ch")),
			ChapterTag (50, Element (0x63C6, new byte[] { 1 }), Simple (pair.Value, "At"), Simple (pair.Flat, "at")),
			ChapterTag (30, Track (1), Simple (pair.Value, "Tr"), Simple (pair.Flat, "tr")),
			ChapterTag (50, Edition (1), Simple (pair.Value, "Ed"), SimpleSorted (pair.Value, "Ed2", "ed2"), Simple (pair.Flat, "ed")),
		];
		foreach (var (name, data) in OwnershipShapes (pair, others: others))
			yield return ($"with other items {name}", data);

		// Two values at a level: a run needs the whole list to repeat.
		yield return ("two values at 50", FileWith (Tags (Tag (50, Simple (pair.Value, "X"), Simple (pair.Value, "Y"), Simple (pair.Flat, "xy")), Tag (30, Simple ("TITLE", "Song"), Simple (pair.Value, "X")))));
		yield return ("two values repeated", FileWith (Tags (Tag (50, Simple (pair.Value, "X"), Simple (pair.Value, "Y"), Simple (pair.Flat, "xy")), Tag (30, Simple ("TITLE", "Song"), Simple (pair.Value, "X"), Simple (pair.Value, "Y")))));
		yield return ("two values, the first repeated at 70", FileWith (Tags (Tag (70, Simple (pair.Value, "X"), Simple (pair.Flat, "x70")), Tag (50, Simple (pair.Value, "X"), Simple (pair.Value, "Y")), Tag (30, Simple ("TITLE", "Song")))));
		yield return ("value-less 70 album artist sort", FileWith (Tags (Tag (70, Simple (pair.Flat, "x")), Tag (50, Simple (pair.Value, "Band")), Tag (30, Simple ("ARTIST", "Solo"), Simple ("TITLE", "Song")))));
		yield return ("value-less 60 sort over an album", FileWith (Tags (Tag (60, Simple (pair.Flat, "x")), Tag (50, Simple (pair.Value, "Band"), Simple ("TITLE", "Alb")))));
		static byte[] Untargeted (params byte[][] simple) => Master (0x7373, Concat (Master (0x63C0, Array.Empty<byte> ()), Concat (simple)));
		yield return ("ffmpeg untargeted", FileWith (Tags (Untargeted (Simple ("TITLE", "Song"), Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort"),
			Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "Bach, J"), Simple ("ALBUM_ARTIST", "AA"), Simple ("ALBUM_ARTIST_SORT", "aa sort")))));
		yield return ("50 + 30 title", FileWith (Tags (Tag (50, Simple ("TITLE", "Alb"), Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort")), Tag (30, Simple ("TITLE", "Song")))));
		yield return ("nested 50 + 30 title", FileWith (Tags (Tag (50, Simple ("TITLE", "Alb"), SimpleSorted ("ARTIST", "Ada", "ada nested")), Tag (30, Simple ("TITLE", "Song")))));
		yield return ("R1 different composers", FileWith (Tags (Tag (50, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "Bach, J")), Tag (30, Simple ("TITLE", "Song"), Simple ("COMPOSER", "Mozart")))));
		yield return ("R2 different artists", FileWith (Tags (Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort")), Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "Solo")))));
		yield return ("R3 different album artists", FileWith (Tags (Tag (50, Simple ("ALBUM_ARTIST", "AA50"), Simple ("ALBUM_ARTIST_SORT", "aa50 sort")), Tag (30, Simple ("TITLE", "Song"), Simple ("ALBUM_ARTIST", "AA30")))));
		yield return ("R4 value-less 50 sort", FileWith (Tags (Tag (50, Simple ("TITLE", "Alb"), Simple ("COMPOSER_SORT", "Bach, J")), Tag (30, Simple ("TITLE", "Song"), Simple ("COMPOSER", "Bach")))));
		yield return ("R5 repeated artist", FileWith (Tags (Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort")), Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "Ada")))));
		yield return ("R5 repeated composer", FileWith (Tags (Tag (50, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "Bach, J")), Tag (30, Simple ("TITLE", "Song"), Simple ("COMPOSER", "Bach")))));
		yield return ("R5 repeated album artist", FileWith (Tags (Tag (50, Simple ("ALBUM_ARTIST", "X"), Simple ("ALBUM_ARTIST_SORT", "x sort")), Tag (30, Simple ("TITLE", "Song"), Simple ("ALBUM_ARTIST", "X")))));
		yield return ("value-less 70 sort above the track", FileWith (Tags (Tag (70, Simple ("ARTIST_SORT", "x")), Tag (50, Simple ("TITLE", "Alb")), Tag (30, Simple ("ARTIST", "Solo"), Simple ("TITLE", "Song")))));
		yield return ("value-less 60 sort above the track", FileWith (Tags (Tag (60, Simple ("ARTIST_SORT", "x")), Tag (50, Simple ("TITLE", "Alb")), Tag (30, Simple ("ARTIST", "Solo"), Simple ("TITLE", "Song")))));
		yield return ("performers with sorts", FileWith (Tags (Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "ada")), Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "Solo"), Simple ("ARTIST_SORT", "solo")))));
		yield return ("70 collection artist", FileWith (Tags (Tag (70, Simple ("ARTIST", "Coll"), Simple ("ARTIST_SORT", "coll")), Tag (30, Simple ("TITLE", "Song")))));
	}

	[TestMethod]
	public void SortModel_ValuesInAnotherOrder_AreAnotherValue ()
	{
		// The same artists in another order are another list: the album's ARTIST_SORT is not the track's.
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "B"), Simple ("ARTIST", "A"), Simple ("ARTIST_SORT", "b, a")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "A"), Simple ("ARTIST", "B"))))).File!.Tag;
		Assert.IsNull (tag.ArtistSort);

		var same = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "A"), Simple ("ARTIST", "B"), Simple ("ARTIST_SORT", "a, b")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "A"), Simple ("ARTIST", "B"))))).File!.Tag;
		Assert.AreEqual ("a, b", same.ArtistSort);
	}

	[TestMethod]
	public void SortModel_RunsCompareTheDefaultLanguageTexts_EmptyOnesIncluded ()
	{
		// A run compares the values the getter reads: the default-language ones, in order, an empty one included.
		var languages = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST", "Ada-fr", isDefault: false), Simple ("ARTIST_SORT", "ada")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "Ada"))))).File!.Tag;
		Assert.AreEqual ("ada", languages.ArtistSort);

		var empty = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ARTIST", ""), Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "ada")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "Ada"))))).File!.Tag;
		Assert.IsNull (empty.ArtistSort);
	}

	[TestMethod]
	public void SortModel_FlatSet_GoesInTheValuesTag ()
	{
		// One copy, beside the value it sorts: a value-less broader Tag's copy is replaced by one in the value's Tag.
		var original = FileWith (Tags (Tag (50, Simple ("TITLE", "Alb"), Simple ("ARTIST_SORT", "old")), Tag (30, Simple ("TITLE", "Song"), Simple ("ARTIST", "Solo"))));
		var tag = MatroskaFile.Read (original).File!.Tag;
		Assert.AreEqual ("old", tag.ArtistSort);

		tag.ArtistSort = "new";

		var bytes = FileWith (tag.Render ().ToArray ());
		Assert.IsTrue (FileTargetHas (bytes, 30, "ARTIST_SORT"));
		Assert.IsFalse (FileTargetHas (bytes, 50, "ARTIST_SORT"));
		Assert.AreEqual ("new", MatroskaFile.Read (bytes).File!.Tag.ArtistSort);
	}

	[TestMethod]
	public void SortSet_DoesNotMoveWhereLaterFieldsLand ()
	{
		// The track's ARTIST_SORT goes beside its ARTIST at 30; a new field still goes to 50, as it did before.
		foreach (var setFirst in new[] { false, true }) {
			var original = FileWith (
				Tags (Tag (50, Simple ("GENRE", "Rock")), Tag (30, Simple ("ARTIST", "Solo"))),
				Master (0x1F43B675, SortClusterMarker));
			var parsed = MatroskaFile.Read (original).File!;

			if (setFirst)
				parsed.Tag.ArtistSort = "solo";
			parsed.Tag.Conductor = "Cond";

			SaveAndReread (parsed, original, out var bytes);
			Assert.IsTrue (FileTargetHas (bytes, 50, "CONDUCTOR"), $"sort set first: {setFirst}");
		}
	}

	[TestMethod]
	public void SortSet_ReplacesTheFlatCopyInPlace ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "bach"), Simple ("GENRE", "Rock"))))).File!.Tag;
		var before = tag.Render ().ToArray ();

		tag.ComposerSort = "BACH";

		var after = tag.Render ().ToArray ();
		Assert.AreEqual (before.Length, after.Length);
		Assert.IsTrue (after.AsSpan ().IndexOf ("BACH"u8) < after.AsSpan ().IndexOf ("GENRE"u8), "the sort keeps its place");
	}
}
