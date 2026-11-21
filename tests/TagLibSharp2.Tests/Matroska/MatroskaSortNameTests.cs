// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Matroska;

// AlbumArtistSort is SORT_WITH on the ARTIST that AlbumArtists reads; ComposerSort is SORT_WITH on the
// COMPOSER that Composers reads. Each falls back to a flat ALBUM_ARTIST_SORT / COMPOSER_SORT tag.
public partial class MatroskaFileTests
{
	static readonly string[] SortTheBand = ["The Band"];
	static readonly string[] SortBach = ["Bach"];

	[TestMethod]
	public void Read_NestedSortWith_FillsAlbumArtistAndComposerSort ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "Band, The")),
			Tag (30,
				SimpleSorted ("ARTIST", "Ada", "ada"),
				SimpleSorted ("COMPOSER", "J. S. Bach", "Bach, Johann Sebastian"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
		Assert.AreEqual ("Bach, Johann Sebastian", tag.ComposerSort);
	}

	[TestMethod]
	public void Read_FlatSortNames_FillAlbumArtistAndComposerSort ()
	{
		var file = FileWith (Tags (
			Tag (50, Simple ("ARTIST", "The Band"), Simple ("ALBUM_ARTIST_SORT", "flat band")),
			Tag (30, Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "flat bach"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("flat band", tag.AlbumArtistSort);
		Assert.AreEqual ("flat bach", tag.ComposerSort);
	}

	[TestMethod]
	public void Read_NestedSort_WinsOverTheFlatName ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "nested band"), Simple ("ALBUM_ARTIST_SORT", "flat band")),
			Tag (30, SimpleSorted ("COMPOSER", "Bach", "nested bach"), Simple ("COMPOSER_SORT", "flat bach"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("nested band", tag.AlbumArtistSort);
		Assert.AreEqual ("nested bach", tag.ComposerSort);
	}

	[TestMethod]
	public void Read_FlatComposerSort_InheritsFromTheAlbum ()
	{
		var file = FileWith (Tags (
			Tag (50, Simple ("COMPOSER_SORT", "album bach")),
			Tag (30, Simple ("COMPOSER", "Bach"))));

		Assert.AreEqual ("album bach", MatroskaFile.Read (file).File!.Tag.ComposerSort);
	}

	[TestMethod]
	public void Read_TrackArtistSort_IsNotTheAlbumArtistSort ()
	{
		var file = FileWith (Tags (
			Tag (50, Simple ("ARTIST", "The Band")),
			Tag (30, SimpleSorted ("ARTIST", "Ada", "ada"), Simple ("ALBUM_ARTIST_SORT", "track level"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		Assert.AreEqual ("ada", tag.ArtistSort);
		Assert.IsNull (tag.AlbumArtistSort);
	}

	[TestMethod]
	public void Read_AlbumLevelOnly_AlbumArtistSortIsTheArtistSort ()
	{
		// With no track-level tags the album ARTIST is both the artist and the album artist.
		var file = FileWith (Tags (Tag (50, SimpleSorted ("ARTIST", "The Band", "Band, The"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
		Assert.AreEqual ("Band, The", tag.ArtistSort);
	}

	[TestMethod]
	public void Read_CollectionArtist_SuppliesAlbumArtistSortWhenTheAlbumIsTheItem ()
	{
		var file = FileWith (Tags (
			Tag (70, SimpleSorted ("ARTIST", "The Band", "Band, The")),
			Tag (50, Simple ("TITLE", "The Album"))));

		var tag = MatroskaFile.Read (file).File!.Tag;

		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
		Assert.AreEqual ("Band, The", tag.AlbumArtistSort);

		tag.AlbumArtistSort = "other";

		Assert.AreEqual ("other", tag.AlbumArtistSort);
		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);

		tag.AlbumArtistSort = null;

		Assert.IsNull (tag.AlbumArtistSort);
		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
	}

	[TestMethod]
	public void SortSet_ReplacesTheNestedAlbumArtistAndComposerSort ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "band")),
			Tag (30,
				SimpleSorted ("ARTIST", "Ada", "ada"),
				SimpleSorted ("COMPOSER", "Bach", "bach"))));
		var tag = MatroskaFile.Read (file).File!.Tag;

		tag.AlbumArtistSort = "other band";
		tag.ComposerSort = "other bach";

		Assert.AreEqual ("other band", tag.AlbumArtistSort);
		Assert.AreEqual ("other bach", tag.ComposerSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
		CollectionAssert.AreEqual (SortBach, tag.Composers);
		Assert.AreEqual ("Ada", tag.Artist);

		tag.AlbumArtistSort = null;
		tag.ComposerSort = null;

		Assert.IsNull (tag.AlbumArtistSort);
		Assert.IsNull (tag.ComposerSort);
		Assert.AreEqual ("ada", tag.ArtistSort);
		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
		CollectionAssert.AreEqual (SortBach, tag.Composers);
	}

	[TestMethod]
	public void SortSet_WithoutTheParent_WritesTheFlatName ()
	{
		var original = FileWith (Tags (Tag (30, Simple ("TITLE", "The Song"))));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.AlbumArtistSort = "band";
		parsed.Tag.ComposerSort = "bach";

		Assert.AreEqual ("band", parsed.Tag.AlbumArtistSort);
		Assert.AreEqual ("bach", parsed.Tag.ComposerSort);
		Assert.IsEmpty (parsed.Tag.AlbumArtists);
		Assert.IsEmpty (parsed.Tag.Composers);
		Assert.IsNull (parsed.Tag.ArtistSort);

		var rendered = parsed.Tag.Render ().ToArray ();
		Assert.AreEqual (1, CountOf (rendered, "ALBUM_ARTIST_SORT"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (rendered, "COMPOSER_SORT"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (rendered, "SORT_WITH"u8.ToArray ()));

		var again = SaveAndRead (parsed, original);
		Assert.AreEqual ("band", again.Tag.AlbumArtistSort);
		Assert.AreEqual ("bach", again.Tag.ComposerSort);
		Assert.AreEqual ("The Song", again.Tag.Title);
		Assert.IsEmpty (again.Tag.AlbumArtists);
		Assert.IsEmpty (again.Tag.Composers);
	}

	[TestMethod]
	public void SortSet_WithoutTheParent_PutsTheAlbumArtistSortAtTheAlbum ()
	{
		// A flat album-artist sort written at the track would be invisible to the getter.
		var parsed = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("TITLE", "The Song"))))).File!;

		parsed.Tag.AlbumArtistSort = "band";

		var rendered = MatroskaFile.Read (FileWith (parsed.Tag.Render ().ToArray ())).File!.Tag;
		Assert.AreEqual ("band", rendered.AlbumArtistSort);
		var trackOnly = MatroskaFile.Read (FileWith (Tags (
			Tag (30, Simple ("TITLE", "The Song"), Simple ("ALBUM_ARTIST_SORT", "band"))))).File!.Tag;
		Assert.IsNull (trackOnly.AlbumArtistSort);
	}

	[TestMethod]
	public void SortSet_ParentWithoutNestedSort_KeepsTheParent ()
	{
		var original = FileWith (Tags (
			Tag (50, Simple ("ARTIST", "The Band")),
			Tag (30, Simple ("ARTIST", "Ada"), Simple ("COMPOSER", "Bach"))));
		var parsed = MatroskaFile.Read (original).File!;

		parsed.Tag.AlbumArtistSort = "band";
		parsed.Tag.ComposerSort = "bach";

		var again = SaveAndRead (parsed, original);
		Assert.AreEqual ("band", again.Tag.AlbumArtistSort);
		Assert.AreEqual ("bach", again.Tag.ComposerSort);
		Assert.IsNull (again.Tag.ArtistSort);
		Assert.AreEqual ("Ada", again.Tag.Artist);
		CollectionAssert.AreEqual (SortTheBand, again.Tag.AlbumArtists);
		CollectionAssert.AreEqual (SortBach, again.Tag.Composers);
	}

	[TestMethod]
	public void SortSet_Null_ClearsEveryVisibleAlbumArtistSort ()
	{
		var original = FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "nested"), Simple ("ALBUM_ARTIST_SORT", "flat")),
			Tag (30, Simple ("ARTIST", "Ada"))));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual ("nested", parsed.Tag.AlbumArtistSort);

		parsed.Tag.AlbumArtistSort = null;

		Assert.IsNull (parsed.Tag.AlbumArtistSort);
		var again = SaveAndRead (parsed, original);
		Assert.IsNull (again.Tag.AlbumArtistSort);
		CollectionAssert.AreEqual (SortTheBand, again.Tag.AlbumArtists);
		Assert.AreEqual ("Ada", again.Tag.Artist);
	}

	[TestMethod]
	public void SortSet_Null_ClearsAFlatAlbumArtistSortAboveTheAlbumItem ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (70, Simple ("ALBUM_ARTIST_SORT", "collection")),
			Tag (50, Simple ("ARTIST", "The Band"), Simple ("ALBUM_ARTIST_SORT", "album"))))).File!.Tag;
		Assert.AreEqual ("album", tag.AlbumArtistSort);

		tag.AlbumArtistSort = null;

		Assert.IsNull (tag.AlbumArtistSort);
		CollectionAssert.AreEqual (SortTheBand, tag.AlbumArtists);
	}

	[TestMethod]
	public void SortSet_Null_ClearsEveryVisibleComposerSort ()
	{
		var original = FileWith (Tags (
			Tag (50, Simple ("COMPOSER_SORT", "album flat")),
			Tag (30, SimpleSorted ("COMPOSER", "Bach", "nested"), Simple ("COMPOSER_SORT", "track flat"))));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual ("nested", parsed.Tag.ComposerSort);

		parsed.Tag.ComposerSort = null;

		Assert.IsNull (parsed.Tag.ComposerSort);
		var again = SaveAndRead (parsed, original);
		Assert.IsNull (again.Tag.ComposerSort);
		CollectionAssert.AreEqual (SortBach, again.Tag.Composers);
	}

	[TestMethod]
	public void SortSet_AlbumArtistSort_LeavesTheTrackArtistSort ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "band")),
			Tag (30, SimpleSorted ("ARTIST", "Ada", "ada"))))).File!.Tag;

		tag.ArtistSort = "artist";
		tag.AlbumArtistSort = "album artist";

		Assert.AreEqual ("artist", tag.ArtistSort);
		Assert.AreEqual ("album artist", tag.AlbumArtistSort);

		tag.ArtistSort = null;

		Assert.IsNull (tag.ArtistSort);
		Assert.AreEqual ("album artist", tag.AlbumArtistSort);
	}

	[TestMethod]
	public void Clear_RemovesAlbumArtistAndComposerSort ()
	{
		var tag = MatroskaFile.Read (FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "band")),
			Tag (30, SimpleSorted ("COMPOSER", "Bach", "bach"))))).File!.Tag;

		tag.Clear ();

		Assert.IsNull (tag.AlbumArtistSort);
		Assert.IsNull (tag.ComposerSort);
	}

	[TestMethod]
	public void MediaFileView_ForwardsAlbumArtistAndComposerSort ()
	{
		var file = FileWith (Tags (
			Tag (50, SimpleSorted ("ARTIST", "The Band", "band")),
			Tag (30, SimpleSorted ("COMPOSER", "Bach", "bach"))));
		var fs = new MockFileSystem ();
		fs.AddFile ("/sort.mkv", file);

		var read = MediaFile.Read ("/sort.mkv", fs);

		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.AreEqual (MediaFormat.Matroska, read.Format);
		Assert.AreEqual ("band", read.Tag!.AlbumArtistSort);
		Assert.AreEqual ("bach", read.Tag.ComposerSort);
	}

	[TestMethod]
	public void CopyTo_CarriesAlbumArtistAndComposerSortIntoMatroska ()
	{
		var source = new VorbisComment { AlbumArtistSort = "band", ComposerSort = "bach" };
		var target = MatroskaFile.Read (FileWith (Tags (Tag (30, Simple ("TITLE", "The Song"))))).File!.Tag;

		source.CopyTo (target);

		Assert.AreEqual ("band", target.AlbumArtistSort);
		Assert.AreEqual ("bach", target.ComposerSort);
	}

	// The real-file round trip in MatroskaIntegrationTests, run on built files so CI covers it.
	static readonly byte[] SortClusterMarker = [0x5A, 0xA5, 0x5A, 0xA5, 0x5A];

	static byte[] NestedSortFile () => FileWith (
		Tags (
			Tag (50, Simple ("TITLE", "The Album"), SimpleSorted ("ARTIST", "The Band", "band")),
			Tag (30,
				Simple ("TITLE", "The Song"),
				SimpleSorted ("ARTIST", "Ada", "ada"),
				SimpleSorted ("COMPOSER", "Bach", "bach"))),
		Master (0x1F43B675, SortClusterMarker));

	static byte[] FlatSortFile () => FileWith (
		Tags (
			Tag (50, Simple ("TITLE", "The Album"), Simple ("ARTIST", "The Band"), Simple ("ALBUM_ARTIST_SORT", "band")),
			Tag (30, Simple ("TITLE", "The Song"), Simple ("COMPOSER", "Bach"), Simple ("COMPOSER_SORT", "bach"))),
		Master (0x1F43B675, SortClusterMarker));

	static void AssertNestedSortSaved (byte[] saved)
	{
		Assert.AreEqual (1, CountOf (saved, SortClusterMarker));
		Assert.AreEqual (0, CountOf (saved, "ALBUM_ARTIST_SORT"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (saved, "COMPOSER_SORT"u8.ToArray ()));
		Assert.AreEqual (3, CountOf (saved, "SORT_WITH"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (saved, Encoding.UTF8.GetBytes (MatroskaSortRoundTrip.AlbumArtistSort)));
		Assert.AreEqual (1, CountOf (saved, Encoding.UTF8.GetBytes (MatroskaSortRoundTrip.ComposerSort)));
		Assert.AreEqual (1, CountOf (saved, "ada"u8.ToArray ()));
	}

	static void AssertFlatSortSaved (byte[] saved)
	{
		Assert.AreEqual (1, CountOf (saved, SortClusterMarker));
		Assert.AreEqual (1, CountOf (saved, "ALBUM_ARTIST_SORT"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (saved, "COMPOSER_SORT"u8.ToArray ()));
		Assert.AreEqual (0, CountOf (saved, "SORT_WITH"u8.ToArray ()));
		Assert.AreEqual (1, CountOf (saved, Encoding.UTF8.GetBytes (MatroskaSortRoundTrip.AlbumArtistSort)));
		Assert.AreEqual (1, CountOf (saved, Encoding.UTF8.GetBytes (MatroskaSortRoundTrip.ComposerSort)));
	}

	static string WriteTempMatroska (byte[] data)
	{
		var path = Path.Combine (Path.GetTempPath (), $"taglibsharp2-built-{Guid.NewGuid ():N}.mkv");
		File.WriteAllBytes (path, data);
		return path;
	}

	[TestMethod]
	public void RoundTrip_NestedSortWith_OnDisk ()
	{
		var path = WriteTempMatroska (NestedSortFile ());
		try {
			MatroskaSortRoundTrip.VerifyOnCopy (path, AssertNestedSortSaved);
		} finally {
			File.Delete (path);
		}
	}

	[TestMethod]
	public async Task RoundTrip_NestedSortWith_OnDiskAsync ()
	{
		var path = WriteTempMatroska (NestedSortFile ());
		try {
			await MatroskaSortRoundTrip.VerifyOnCopyAsync (path, AssertNestedSortSaved);
		} finally {
			File.Delete (path);
		}
	}

	[TestMethod]
	public void RoundTrip_FlatSortNames_OnDisk ()
	{
		var path = WriteTempMatroska (FlatSortFile ());
		try {
			MatroskaSortRoundTrip.VerifyOnCopy (path, AssertFlatSortSaved);
		} finally {
			File.Delete (path);
		}
	}

	[TestMethod]
	public async Task RoundTrip_FlatSortNames_OnDiskAsync ()
	{
		var path = WriteTempMatroska (FlatSortFile ());
		try {
			await MatroskaSortRoundTrip.VerifyOnCopyAsync (path, AssertFlatSortSaved);
		} finally {
			File.Delete (path);
		}
	}

	[TestMethod]
	public void RoundTrip_WithoutParents_OnDisk ()
	{
		var path = WriteTempMatroska (FileWith (
			Tags (Tag (30, Simple ("TITLE", "The Song"))),
			Master (0x1F43B675, SortClusterMarker)));
		try {
			MatroskaSortRoundTrip.VerifyOnCopy (path, AssertFlatSortSaved);
		} finally {
			File.Delete (path);
		}
	}

	static MatroskaFile SaveAndRead (MatroskaFile parsed, byte[] original)
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/sort.mkv", original);
		var saved = parsed.SaveToFile ("/sort.mkv", original, fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var again = MatroskaFile.Read (fs.ReadAllBytes ("/sort.mkv"));
		Assert.IsTrue (again.IsSuccess, again.Error);
		return again.File!;
	}
}
