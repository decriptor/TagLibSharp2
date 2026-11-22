// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Matroska;

// Tag.IsWriteBack (#136): writing back the value read changes nothing, but a CopyTo makes the target hold exactly
// the source's value, so a copy equal to the target's first value still replaces the target's other values.
public partial class MatroskaFileTests
{
	[TestMethod]
	public void CopyTo_ValueEqualToTheFirst_ReplacesTheList ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (50,
			Simple ("ISRC", "A"), Simple ("ISRC", "B"),
			Simple ("CATALOG_NUMBER", "C1"), Simple ("CATALOG_NUMBER", "C2"),
			Simple ("BPM", "120"), Simple ("BPM", "130"),
			Simple ("PUBLISHER", "P1"), Simple ("PUBLISHER", "P2"), Simple ("LABEL", "L"))))).File!.Tag;

		new VorbisComment { Isrc = "A", CatalogNumber = "C1", BeatsPerMinute = 120, Publisher = "P1" }.CopyTo (tag);

		var again = MatroskaFile.Read (FileWith (tag.Render ().ToArray ())).File!.Tag;
		var bytes = tag.Render ().ToArray ();
		Assert.AreEqual ("A", again.Isrc);
		Assert.AreEqual (1, CountOf (bytes, "ISRC"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (bytes, "CATALOG_NUMBER"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (bytes, "130"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (bytes, "P2"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (bytes, "LABEL"u8.ToArray ()), "the fallback name is its own field");
	}

	[TestMethod]
	public void CopyBasic_ArtistEqualToTheFirst_ReplacesThePerformers ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("ARTIST", "Ada"), Simple ("ARTIST", "Bob"))))).File!.Tag;

		new VorbisComment { Artist = "Ada" }.CopyTo (tag, TagCopyOptions.Basic);

		Assert.AreEqual ("Ada", string.Join ("|", tag.Performers));
	}

	[TestMethod]
	public void CopyTo_ValuesTheTargetHoldsAsTheOnlyOnes_ChangesNoBytes ()
	{
		// The title read from the album, a nested sort read, and a list held as read: nothing to replace.
		var original = FileWith (Tags (
			Tag (50, Simple ("TITLE", "Alb"), SimpleSorted ("ARTIST", "Band", "band"), Simple ("ARTIST_SORT", "stray")),
			Tag (30, Simple ("GENRE", "Rock"))));
		var tag = MatroskaFile.Read (original).File!.Tag;
		var before = tag.Render ().ToArray ();
		Assert.AreEqual ("Alb", tag.Title);

		new VorbisComment { Title = "Alb", ArtistSort = "band", Genres = ["Rock"] }.CopyTo (tag);

		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());

		// One value held at the collection level, and a BPM stored as "120.0": a copy finds each held as it is.
		var collection = MatroskaFile.Read (FileWith (Tags (Tag (70, Simple ("PUBLISHER", "P"), Simple ("BPM", "120.0")), Tag (30, Simple ("TITLE", "S"))))).File!.Tag;
		var held = collection.Render ().ToArray ();
		new VorbisComment { Publisher = "P", BeatsPerMinute = 120 }.CopyTo (collection);
		CollectionAssert.AreEqual (held, collection.Render ().ToArray ());
	}

	[TestMethod]
	public void EmptyWrite_OverAFieldThatHoldsNoText_ChangesNoBytes ()
	{
		// "" is the value read only when no value of the name holds text (#136).
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("ALBUM_ARTIST", ""), Simple ("ALBUM_ARTIST_SORT", "x"), Simple ("ARTIST", "Band")),
			Tag (30, Simple ("TITLE", "Song"))))).File!.Tag;
		var before = tag.Render ().ToArray ();

		tag.AlbumArtists = tag.AlbumArtists;

		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());
		Assert.AreEqual ("x", tag.AlbumArtistSort);
	}

	[TestMethod]
	public void CopyTo_TitleEqualToTheSegmentTitle_ReplacesTheTrackTitles ()
	{
		// Title reads the track's TITLE before the segment title (RFC 9559 section 5.1.2.12 names the segment).
		var tag = MatroskaFile.Read (FileWith (
			Master (0x1549A966, Element (0x7BA9, "A"u8)),
			Tags (Tag (30, Simple ("TITLE", "A"), Simple ("TITLE", "Zq2"))))).File!.Tag;
		Assert.AreEqual ("A", tag.Title);

		new VorbisComment { Title = "A" }.CopyTo (tag, TagCopyOptions.Basic);

		Assert.AreEqual (0, CountOf (tag.Render ().ToArray (), "Zq2"u8.ToArray ()));
	}

	[TestMethod]
	public void EmptyWrite_OfTheComposersRead_KeepsTheirSort ()
	{
		// An empty COMPOSER reads as no composers and holds no text; writing that back is the value read, so its sort
		// stays (#136). The same holds for "" over a field that holds no text. A chapter's composer is not the file's.
		foreach (var data in new[] {
			FileWith (Tags (Tag (50, Simple ("COMPOSER", ""), Simple ("COMPOSER_SORT", "x"), Simple ("GENRE", "")), Tag (30, Simple ("TITLE", ""), Simple ("GENRE", "")),
				ChapterTag (30, Chapter (2), Simple ("TITLE", "Chapter")))),
			FileWith (Tags (Tag (50, Simple ("COMPOSER", ""), Simple ("COMPOSER_SORT", "x")))),
			FileWith (Tags (Tag (50, Simple ("COMPOSER", ""), Simple ("COMPOSER_SORT", "x")), ChapterTag (50, Chapter (1), Simple ("COMPOSER", "Ch")))) }) {
			var tag = MatroskaFile.Read (data).File!.Tag;
			var before = tag.Render ().ToArray ();

			tag.Composers = tag.Composers;
			tag.Genres = tag.Genres;
			if (tag.Genre is null)
				tag.Genre = "";
			if (tag.Title is null)
				tag.Title = "";

			CollectionAssert.AreEqual (before, tag.Render ().ToArray ());
			Assert.AreEqual ("x", tag.ComposerSort);
		}
	}

	[TestMethod]
	public void EmptyWrite_OverAnEmptyValueWithTextBehindIt_Clears ()
	{
		// An empty PUBLISHER at 30 reads as none, but the album's still holds text, so "" is a clear (#136).
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, Simple ("PUBLISHER", "P"), Simple ("GENRE", "Rock"), Simple ("REPLAYGAIN_GAIN", "-3 dB")),
			Tag (30, Simple ("TITLE", "S"), Simple ("PUBLISHER", ""), Simple ("GENRE", ""), Simple ("REPLAYGAIN_GAIN", ""))))).File!.Tag;
		Assert.IsNull (tag.Publisher);

		tag.Publisher = "";
		tag.Genre = "";
		tag.ReplayGainTrackGain = "";

		var bytes = tag.Render ().ToArray ();
		Assert.AreEqual (0, CountOf (bytes, "PUBLISHER"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (bytes, "Rock"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (bytes, "REPLAYGAIN_GAIN"u8.ToArray ()), "the older name holds text behind its empty one");

		// The fallback name alone holds the hidden text.
		var label = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("LABEL", "L")), Tag (30, Simple ("TITLE", "S"), Simple ("LABEL", ""))))).File!.Tag;
		Assert.IsNull (label.Publisher);
		label.Publisher = "";
		Assert.AreEqual (0, CountOf (label.Render ().ToArray (), "LABEL"u8.ToArray ()));

		// A one-level setter clears its level when that level holds an empty value, even while a narrower one reads text.
		var artist = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("ARTIST", ""), Simple ("ARTIST_SORT", "s"), Simple ("TITLE", "S")), Tag (20, Simple ("ARTIST", "X"))))).File!.Tag;
		Assert.AreEqual ("X", artist.Artist);
		artist.Artist = "";
		Assert.AreEqual (0, CountOf (artist.Render ().ToArray (), "ARTIST_SORT"u8.ToArray ()));
	}

	[TestMethod]
	public void EmptyWrite_OverAFieldWithAFallback_ClearsBothNames ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (Tag (50,
			Simple ("PUBLISHER", "P"), Simple ("LABEL", "L"),
			Simple ("REPLAYGAIN_TRACK_GAIN", "-1 dB"), Simple ("REPLAYGAIN_GAIN", "-2 dB"),
			Simple ("GENRE", "Rock"))))).File!.Tag;

		tag.Publisher = "";
		tag.ReplayGainTrackGain = "";
		tag.Genre = "";

		var again = MatroskaFile.Read (FileWith (tag.Render ().ToArray ())).File!.Tag;
		Assert.IsNull (again.Publisher);
		Assert.IsNull (again.ReplayGainTrackGain);
		Assert.IsNull (again.Genre);
	}
}
