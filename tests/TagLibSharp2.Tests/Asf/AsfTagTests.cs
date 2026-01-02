// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfTagTests
{
	// ═══════════════════════════════════════════════════════════════
	// Content Description Mappings
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Title_Get_ReturnsFromContentDescription ()
	{
		var content = new AsfContentDescription ("Test Title", "", "", "", "");
		var tag = new AsfTag (content, null);

		Assert.AreEqual ("Test Title", tag.Title);
	}

	[TestMethod]
	public void Title_Set_UpdatesContentDescription ()
	{
		var tag = new AsfTag ();
		tag.Title = "New Title";

		Assert.AreEqual ("New Title", tag.Title);
	}

	[TestMethod]
	public void Artist_Get_ReturnsFromAuthor ()
	{
		var content = new AsfContentDescription ("", "Test Artist", "", "", "");
		var tag = new AsfTag (content, null);

		Assert.AreEqual ("Test Artist", tag.Artist);
	}

	[TestMethod]
	public void Artist_Set_UpdatesAuthor ()
	{
		var tag = new AsfTag ();
		tag.Artist = "New Artist";

		Assert.AreEqual ("New Artist", tag.Artist);
	}

	[TestMethod]
	public void Copyright_Get_ReturnsFromContentDescription ()
	{
		var content = new AsfContentDescription ("", "", "2025 Test", "", "");
		var tag = new AsfTag (content, null);

		Assert.AreEqual ("2025 Test", tag.Copyright);
	}

	[TestMethod]
	public void Comment_Get_ReturnsFromContentDescription ()
	{
		var content = new AsfContentDescription ("", "", "", "A comment", "");
		var tag = new AsfTag (content, null);

		Assert.AreEqual ("A comment", tag.Comment);
	}

	// ═══════════════════════════════════════════════════════════════
	// Extended Attribute Mappings
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Album_Get_ReturnsFromWmAlbumTitle ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual ("Test Album", tag.Album);
	}

	[TestMethod]
	public void Album_Set_CreatesWmAlbumTitle ()
	{
		var tag = new AsfTag ();
		tag.Album = "New Album";

		Assert.AreEqual ("New Album", tag.Album);
	}

	[TestMethod]
	public void Year_Get_ReturnsFromWmYear ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/Year", "2025")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual ("2025", tag.Year);
	}

	[TestMethod]
	public void Year_Set_CreatesWmYear ()
	{
		var tag = new AsfTag ();
		tag.Year = "2025";

		Assert.AreEqual ("2025", tag.Year);
	}

	[TestMethod]
	public void Track_Get_FromDword_ReturnsValue ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateDword ("WM/TrackNumber", 5)
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual (5u, tag.Track);
	}

	[TestMethod]
	public void Track_Get_FromString_ParsesValue ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/TrackNumber", "7")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual (7u, tag.Track);
	}

	[TestMethod]
	public void Track_Set_CreatesDword ()
	{
		var tag = new AsfTag ();
		tag.Track = 10;

		Assert.AreEqual (10u, tag.Track);
	}

	[TestMethod]
	public void Genre_Get_ReturnsFromWmGenre ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/Genre", "Rock")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual ("Rock", tag.Genre);
	}

	[TestMethod]
	public void Genre_Set_CreatesWmGenre ()
	{
		var tag = new AsfTag ();
		tag.Genre = "Jazz";

		Assert.AreEqual ("Jazz", tag.Genre);
	}

	[TestMethod]
	public void AlbumArtist_Get_ReturnsFromWmAlbumArtist ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/AlbumArtist", "Various Artists")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual ("Various Artists", tag.AlbumArtist);
	}

	[TestMethod]
	public void Composer_Get_ReturnsFromWmComposer ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/Composer", "Bach")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual ("Bach", tag.Composer);
	}

	[TestMethod]
	public void Conductor_Get_ReturnsFromWmConductor ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/Conductor", "Karajan")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual ("Karajan", tag.Conductor);
	}

	[TestMethod]
	public void DiscNumber_Get_FromPartOfSet_ReturnsFirst ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/PartOfSet", "2/3")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual (2u, tag.DiscNumber);
	}

	[TestMethod]
	public void DiscCount_Get_FromPartOfSet_ReturnsSecond ()
	{
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/PartOfSet", "2/3")
		]);
		var tag = new AsfTag (null, extended);

		Assert.AreEqual (3u, tag.DiscCount);
	}

	// ═══════════════════════════════════════════════════════════════
	// Clear Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Clear_RemovesAllMetadata ()
	{
		var content = new AsfContentDescription ("Title", "Artist", "Copyright", "Comment", "Rating");
		var extended = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Album"),
			AsfDescriptor.CreateString ("WM/Genre", "Rock")
		]);
		var tag = new AsfTag (content, extended);

		tag.Clear ();

		Assert.AreEqual ("", tag.Title);
		Assert.AreEqual ("", tag.Artist);
		Assert.AreEqual ("", tag.Album);
		Assert.AreEqual ("", tag.Genre);
	}

	// ═══════════════════════════════════════════════════════════════
	// Empty Tag Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void NewTag_AllPropertiesEmpty ()
	{
		var tag = new AsfTag ();

		Assert.AreEqual ("", tag.Title);
		Assert.AreEqual ("", tag.Artist);
		Assert.AreEqual ("", tag.Album);
		Assert.IsTrue (string.IsNullOrEmpty (tag.Year));
		Assert.IsNull (tag.Track);
		Assert.AreEqual ("", tag.Genre);
	}

	[TestMethod]
	public void IsEmpty_NewTag_ReturnsTrue ()
	{
		var tag = new AsfTag ();

		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_WithTitle_ReturnsFalse ()
	{
		var tag = new AsfTag ();
		tag.Title = "Test";

		Assert.IsFalse (tag.IsEmpty);
	}

	// ═══════════════════════════════════════════════════════════════
	// Unicode Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Unicode_Title_Preserved ()
	{
		var tag = new AsfTag ();
		tag.Title = "日本語タイトル";

		Assert.AreEqual ("日本語タイトル", tag.Title);
	}

	[TestMethod]
	public void Unicode_Album_Preserved ()
	{
		var tag = new AsfTag ();
		tag.Album = "中文专辑";

		Assert.AreEqual ("中文专辑", tag.Album);
	}
}
