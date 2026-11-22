// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Matroska;

namespace TagLibSharp2.Tests.Matroska;

// Library rule: writing back the value read changes nothing. Every setter this PR reaches, on every
// ownership shape plus item fields at 30, 50, 70 and untargeted, renders byte for byte as before.
public partial class MatroskaFileTests
{
	// Each property's value as the getter reads it, joined for arrays; writing it back is the operation.
	static readonly (string Name, Func<MatroskaTag, string?> Read, Action<MatroskaTag> WriteBack)[] WriteBacks = [
		("ArtistSort", t => t.ArtistSort, t => t.ArtistSort = t.ArtistSort),
		("AlbumArtistSort", t => t.AlbumArtistSort, t => t.AlbumArtistSort = t.AlbumArtistSort),
		("ComposerSort", t => t.ComposerSort, t => t.ComposerSort = t.ComposerSort),
		("TitleSort", t => t.TitleSort, t => t.TitleSort = t.TitleSort),
		("AlbumSort", t => t.AlbumSort, t => t.AlbumSort = t.AlbumSort),
		("Artist", t => t.Artist, t => t.Artist = t.Artist),
		("Performers", t => Joined (t.Performers), t => t.Performers = t.Performers),
		("AlbumArtists", t => Joined (t.AlbumArtists), t => t.AlbumArtists = t.AlbumArtists),
		("Composers", t => Joined (t.Composers), t => t.Composers = t.Composers),
		("Title", t => t.Title, t => t.Title = t.Title),
		("Album", t => t.Album, t => t.Album = t.Album),
		("Genre", t => t.Genre, t => t.Genre = t.Genre),
		("Genres", t => Joined (t.Genres), t => t.Genres = t.Genres),
		("Year", t => t.Year, t => t.Year = t.Year),
		("Comment", t => t.Comment, t => t.Comment = t.Comment),
		("BeatsPerMinute", t => t.BeatsPerMinute?.ToString (System.Globalization.CultureInfo.InvariantCulture), t => t.BeatsPerMinute = t.BeatsPerMinute),
		("Publisher", t => t.Publisher, t => t.Publisher = t.Publisher),
		("ReplayGainTrackGain", t => t.ReplayGainTrackGain, t => t.ReplayGainTrackGain = t.ReplayGainTrackGain),
		("ReplayGainTrackPeak", t => t.ReplayGainTrackPeak, t => t.ReplayGainTrackPeak = t.ReplayGainTrackPeak),
	];

	static string? Joined (string[] values) => values.Length == 0 ? null : string.Join ("|", values);

	// A value read back unchanged is a no-op: the bytes stay. Null is a clear: it still reads null afterwards,
	// and may remove a value a broader level held behind it.
	[TestMethod]
	public void WritingBackTheValueRead_ChangesNoBytes ()
	{
		var failures = new List<string> ();
		foreach (var (shape, data) in WriteBackShapes ()) {
			foreach (var (name, read, writeBack) in WriteBacks) {
				var tag = MatroskaFile.Read (data).File!.Tag;
				var before = tag.Render ().ToArray ();
				var value = read (tag);

				writeBack (tag);

				if (value is not null && !before.AsSpan ().SequenceEqual (tag.Render ().ToArray ()))
					failures.Add ($"{shape}: {name}");
				if (value is null && read (tag) is not null)
					failures.Add ($"{shape}: {name} null");
			}
		}

		var byName = failures.GroupBy (f => f[(f.LastIndexOf (": ", StringComparison.Ordinal) + 2)..]).Select (g => $"{g.Key}={g.Count ()}");
		Assert.IsEmpty (failures, $"{failures.Count} write-backs changed bytes ({string.Join (", ", byName)}), e.g. {string.Join ("; ", failures.Take (4))}");
	}

	static IEnumerable<(string Name, byte[] Data)> WriteBackShapes ()
	{
		foreach (var pair in SortPairs) {
			foreach (var (shape, data) in OwnershipShapes (pair))
				yield return ($"{pair.Name} {shape}", data);
		}

		foreach (var (shape, data) in ItemFieldShapes ())
			yield return ($"item {shape}", data);
		foreach (var (name, build) in SortShapes)
			yield return ($"sort {name}", build ());
		yield return ("genre at 70", FileWith (Tags (Tag (70, Simple ("GENRE", "Rock")), Tag (30, Simple ("TITLE", "Song")))));
		yield return ("untargeted ffmpeg", FileWith (Tags (Master (0x7373, Concat (Master (0x63C0, Array.Empty<byte> ()),
			Simple ("TITLE", "Song"), Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "Ada sort"), Simple ("GENRE", "Rock"))))));
		yield return ("fallback names", FileWith (Tags (Tag (50, Simple ("BPM", "120.0"), Simple ("LABEL", "Lbl"),
			Simple ("REPLAYGAIN_GAIN", "-3 dB"), Simple ("REPLAYGAIN_PEAK", "0.9")), Tag (30, Simple ("TITLE", "Song")))));
		yield return ("blockers at 30", FileWith (Tags (
			Tag (50, Simple ("GENRE", "Rock"), Simple ("COMPOSER", "Bach"), Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "ada")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("GENRE", ""), Simple ("COMPOSER", ""), Simple ("ARTIST", ""), Simple ("ARTIST_SORT", "")))));
		yield return ("titles with sorts", FileWith (Tags (
			Tag (50, SimpleSorted ("TITLE", "Album", "album"), Simple ("ALBUM_SORT", "flat album")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("TITLE_SORT", "song")))));
		// A nested sort beside a flat one, and a value-less title sort above its title: the write-back reads the
		// nested sort first and the run above, so neither moves.
		yield return ("title sorts nested and flat", FileWith (Tags (
			Tag (50, SimpleSorted ("TITLE", "Album", "album"), Simple ("ALBUM_SORT", "flat album")),
			Tag (30, SimpleSorted ("TITLE", "Song", "song"), Simple ("TITLE_SORT", "flat song")))));
		yield return ("title sorts above their titles", FileWith (Tags (
			Tag (60, Simple ("ALBUM_SORT", "x")), Tag (50, Simple ("TITLE", "Al")),
			Tag (40, Simple ("TITLE_SORT", "y")), Tag (30, Simple ("TITLE", "S")))));
		// Two values of a name a fallback getter reads, and a value at the collection level: writing back the
		// first keeps the list and its level.
		yield return ("lists of fallback names", FileWith (Tags (Tag (50, Simple ("PUBLISHER", "P"), Simple ("PUBLISHER", "Q"),
			Simple ("REPLAYGAIN_TRACK_GAIN", "-6 dB"), Simple ("REPLAYGAIN_TRACK_GAIN", "-7 dB"), Simple ("BPM", "120"), Simple ("BPM", "130")),
			Tag (30, Simple ("TITLE", "Song")))));
		yield return ("fields at 70", FileWith (Tags (Tag (70, Simple ("PUBLISHER", "P"), Simple ("REPLAYGAIN_TRACK_GAIN", "-6 dB"), Simple ("BPM", "120")),
			Tag (30, Simple ("TITLE", "Song")))));
	}

	[TestMethod]
	public void NullList_Clears_WithItsSorts ()
	{
		// Null is a clear (Tag.IsWriteBack never matches null), for a list too.
		var performers = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("ARTIST", "A"), Simple ("ARTIST_SORT", "x"))))).File!.Tag;
		performers.Performers = null!;
		Assert.IsFalse (HasTagName (FileWith (performers.Render ().ToArray ()), "ARTIST_SORT"));
		Assert.IsEmpty (performers.Performers);

		var albumArtists = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("ARTIST", "Ada"), Simple ("ARTIST_SORT", "x")), Tag (30, Simple ("TITLE", "S"))))).File!.Tag;
		albumArtists.AlbumArtists = null!;
		Assert.IsFalse (HasTagName (FileWith (albumArtists.Render ().ToArray ()), "ARTIST"));
		Assert.IsFalse (HasTagName (FileWith (albumArtists.Render ().ToArray ()), "ARTIST_SORT"));

		var composers = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "x"))))).File!.Tag;
		composers.Composers = null!;
		Assert.IsFalse (HasTagName (FileWith (composers.Render ().ToArray ()), "COMPOSER_SORT"));
		Assert.IsEmpty (composers.Composers);

		var genres = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("GENRE", "Rock")), Tag (30, Simple ("GENRE", ""), Simple ("TITLE", "S"))))).File!.Tag;
		Assert.IsEmpty (genres.Genres);
		genres.Genres = null!;
		Assert.IsFalse (HasTagName (FileWith (genres.Render ().ToArray ()), "GENRE"), "the clear removes what the empty one hides");
	}

	[TestMethod]
	public void ListWrite_WithEmptyEntries_IsTheListRead ()
	{
		// Empty entries are not values: the rest, in order, is what the getter reads, here from the collection level,
		// where a write would otherwise add a copy at the album level.
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (70, Simple ("GENRE", "Rock"), Simple ("GENRE", "Pop")), Tag (30, Simple ("TITLE", "S"))))).File!.Tag;
		var before = tag.Render ().ToArray ();

		tag.Genres = ["", "Rock", "", "Pop"];

		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());
	}

	[TestMethod]
	public void WritingBackOtherValues_StillWrites ()
	{
		// The no-op is only for the values read, in order: a reorder, a subset or a new value is written.
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("GENRE", "A"), Simple ("GENRE", "B"))))).File!.Tag;

		tag.Genres = ["B", "A"];
		Assert.AreEqual ("B|A", string.Join ("|", tag.Genres));
		tag.Genres = ["B"];
		Assert.AreEqual ("B", string.Join ("|", tag.Genres));
		tag.Genres = ["B", "C"];
		Assert.AreEqual ("B|C", string.Join ("|", tag.Genres));
	}

	[TestMethod]
	public void Clear_OfANullField_RemovesWhatABroaderLevelHides ()
	{
		// A "" at 30 hides the album's GENRE from us (RFC 9559 section 24.2) but not from readers that ignore
		// levels. Writing null back is a clear, so the hidden value goes too, and so is "" or an empty list, as a value
		// still holds text (#136). A cleared track ARTIST keeps its
		// blocker, or the album's ARTIST would show through as the track's.
		var data = FileWith (Tags (
			Tag (50, Simple ("GENRE", "Rock"), Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "bach"), Simple ("ARTIST", "Ada")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("GENRE", ""), Simple ("COMPOSER", ""), Simple ("ARTIST", ""))));
		var tag = MatroskaFile.Read (data).File!.Tag;
		Assert.IsNull (tag.Genre);
		Assert.IsEmpty (tag.Composers);
		Assert.IsNull (tag.Artist);

		tag.Genre = null;
		tag.Composers = [];
		tag.Artist = null;
		tag.ComposerSort = null;

		var rendered = FileWith (tag.Render ().ToArray ());
		Assert.IsFalse (HasTagName (rendered, "GENRE"));
		Assert.IsFalse (HasTagName (rendered, "COMPOSER"));
		Assert.IsFalse (HasTagName (rendered, "COMPOSER_SORT"));
		Assert.IsNull (tag.Artist);
		Assert.IsTrue (HasTagString (rendered, "Ada"), "the album artist is kept");
	}

	[TestMethod]
	public void Clear_OfANullSortOrAlbumArtist_RemovesWhatABroaderLevelHides ()
	{
		var sorts = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "bach")),
			Tag (30, Simple ("TITLE", "Song"), Simple ("COMPOSER_SORT", ""))))).File!.Tag;
		Assert.IsNull (sorts.ComposerSort);
		sorts.ComposerSort = null;
		Assert.IsFalse (HasTagName (FileWith (sorts.Render ().ToArray ()), "COMPOSER_SORT"));

		var albumArtist = MatroskaFile.Read (FileWith (Tags (
			Tag (70, Simple ("ALBUM_ARTIST", "AA70")),
			Tag (50, Simple ("TITLE", "Album"), Simple ("ALBUM_ARTIST", ""))))).File!.Tag;
		Assert.IsEmpty (albumArtist.AlbumArtists);
		albumArtist.AlbumArtists = [];
		Assert.IsFalse (HasTagName (FileWith (albumArtist.Render ().ToArray ()), "ALBUM_ARTIST"));
	}

	[TestMethod]
	public void FallbackNames_ClearAndReplaceWithTheirField ()
	{
		static MatroskaTag Read () => MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("LABEL", "Lbl"),
			Simple ("REPLAYGAIN_GAIN", "-3 dB"), Simple ("REPLAYGAIN_PEAK", "0.9"))))).File!.Tag;

		var cleared = Read ();
		cleared.Publisher = null;
		cleared.ReplayGainTrackGain = null;
		cleared.ReplayGainTrackPeak = null;
		Assert.IsNull (cleared.Publisher);
		Assert.IsNull (cleared.ReplayGainTrackGain);
		Assert.IsNull (cleared.ReplayGainTrackPeak);

		var set = Read ();
		set.Publisher = "Pub";
		set.ReplayGainTrackGain = "-6 dB";
		set.ReplayGainTrackPeak = "0.5";
		var rendered = FileWith (set.Render ().ToArray ());
		Assert.AreEqual ("Pub", set.Publisher);
		Assert.IsTrue (HasTagName (rendered, "LABEL"), "LABEL is its own tag");
		Assert.IsFalse (HasTagName (rendered, "REPLAYGAIN_GAIN"), "the older copy goes");
		Assert.IsFalse (HasTagName (rendered, "REPLAYGAIN_PEAK"), "the older copy goes");
		Assert.AreEqual ("-6 dB", set.ReplayGainTrackGain);
	}
}
