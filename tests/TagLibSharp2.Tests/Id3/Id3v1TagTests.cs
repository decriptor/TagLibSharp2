// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;

namespace TagLibSharp2.Tests.Id3;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class Id3v1TagTests
{
	// ID3v1 is a fixed 128-byte structure at the end of a file
	// Offset  Size  Field
	// 0       3     "TAG" magic
	// 3       30    Title (Latin-1, null-padded)
	// 33      30    Artist
	// 63      30    Album
	// 93      4     Year
	// 97      30    Comment (or 28 + null + track in v1.1)
	// 127     1     Genre ID


	[TestMethod]
	public void Read_ValidTag_ParsesAllFields ()
	{
		var data = CreateId3v1Tag (
			title: "Test Title",
			artist: "Test Artist",
			album: "Test Album",
			year: "2024",
			comment: "Test Comment",
			genre: 17); // Rock

		var result = Id3v1Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Title", result.Tag!.Title);
		Assert.AreEqual ("Test Artist", result.Tag.Artist);
		Assert.AreEqual ("Test Album", result.Tag.Album);
		Assert.AreEqual ("2024", result.Tag.Year);
		Assert.AreEqual ("Test Comment", result.Tag.Comment);
		Assert.AreEqual ("Rock", result.Tag.Genre);
		Assert.AreEqual (17, result.Tag.GenreIndex);
		Assert.IsNull (result.Tag.Track); // v1.0 has no track
		Assert.IsFalse (result.Tag.IsVersion11);
	}

	[TestMethod]
	public void Read_Version11WithTrack_ParsesTrackNumber ()
	{
		var data = CreateId3v11Tag (
			title: "Track Test",
			artist: "Artist",
			album: "Album",
			year: "2024",
			comment: "Comment",
			track: 5,
			genre: 0); // Blues

		var result = Id3v1Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Tag!.IsVersion11);
		Assert.AreEqual (5u, result.Tag.Track);
		Assert.AreEqual ("Comment", result.Tag.Comment);
		Assert.AreEqual ("Blues", result.Tag.Genre);
	}

	[TestMethod]
	public void Read_NoMagic_ReturnsNotFound ()
	{
		var data = new byte[128];
		data[0] = (byte)'X';
		data[1] = (byte)'Y';
		data[2] = (byte)'Z';

		var result = Id3v1Tag.Read (data);

		Assert.IsTrue (result.IsNotFound);
		Assert.IsNull (result.Tag);
		Assert.IsNull (result.Error);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[100]; // Less than 128 bytes

		var result = Id3v1Tag.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsFalse (result.IsNotFound);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_NullPaddedFields_TrimsCorrectly ()
	{
		var data = CreateId3v1Tag (
			title: "Short",
			artist: "A",
			album: "",
			year: "99",
			comment: "C",
			genre: 255);

		var result = Id3v1Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Short", result.Tag!.Title);
		Assert.AreEqual ("A", result.Tag.Artist);
		Assert.AreEqual ("", result.Tag.Album);
		Assert.AreEqual ("99", result.Tag.Year);
		Assert.AreEqual ("C", result.Tag.Comment);
		Assert.IsNull (result.Tag.Genre); // 255 = undefined
	}

	[TestMethod]
	public void Read_MaxLengthFields_ParsesFullLength ()
	{
		var title = new string ('T', 30);
		var artist = new string ('A', 30);
		var album = new string ('L', 30);
		var comment = new string ('C', 30);

		var data = CreateId3v1Tag (title, artist, album, "2024", comment, 1);

		var result = Id3v1Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (30, result.Tag!.Title!.Length);
		Assert.AreEqual (30, result.Tag.Artist!.Length);
		Assert.AreEqual (30, result.Tag.Album!.Length);
		Assert.AreEqual (30, result.Tag.Comment!.Length);
	}

	[TestMethod]
	public void Read_TrackZero_TreatedAsNoTrack ()
	{
		// If track byte is 0, it's not a valid track number
		var data = CreateId3v11Tag ("Title", "Artist", "Album", "2024", "Comment", track: 0, genre: 0);

		var result = Id3v1Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// Track 0 should be treated as "no track" not "track 0"
		Assert.IsNull (result.Tag!.Track);
	}



	[TestMethod]
	public void Render_BasicTag_Creates128Bytes ()
	{
		var tag = new Id3v1Tag {
			Title = "Test",
			Artist = "Artist",
			Album = "Album"
		};

		var data = tag.Render ();

		Assert.AreEqual (128, data.Length);
		Assert.IsTrue (data.StartsWith ("TAG"u8));
	}

	[TestMethod]
	public void Render_Version11_IncludesTrackNumber ()
	{
		var tag = new Id3v1Tag {
			Title = "Test",
			Track = 7
		};

		var data = tag.Render ();

		Assert.AreEqual (128, data.Length);
		Assert.AreEqual (0, data[125]); // Null separator
		Assert.AreEqual (7, data[126]); // Track number
	}

	[TestMethod]
	public void Render_LongField_Truncates ()
	{
		var tag = new Id3v1Tag {
			Title = new string ('X', 50) // Longer than 30
		};

		var data = tag.Render ();

		// Should still be 128 bytes, title truncated to 30
		Assert.AreEqual (128, data.Length);
	}

	[TestMethod]
	public void Render_GenreByName_SetsCorrectIndex ()
	{
		var tag = new Id3v1Tag {
			Genre = "Jazz"
		};

		var data = tag.Render ();

		Assert.AreEqual (8, data[127]); // Jazz = genre index 8
	}

	[TestMethod]
	public void Render_UnknownGenre_SetsUndefined ()
	{
		var tag = new Id3v1Tag {
			Genre = "Unknown Genre Name"
		};

		var data = tag.Render ();

		Assert.AreEqual (255, data[127]); // Undefined
	}



	[TestMethod]
	public void RoundTrip_AllFields_PreservesData ()
	{
		var original = new Id3v1Tag {
			Title = "Round Trip",
			Artist = "Test Artist",
			Album = "Test Album",
			Year = "2024",
			Comment = "A comment",
			Genre = "Rock"
		};

		var rendered = original.Render ();
		var result = Id3v1Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Title, result.Tag!.Title);
		Assert.AreEqual (original.Artist, result.Tag.Artist);
		Assert.AreEqual (original.Album, result.Tag.Album);
		Assert.AreEqual (original.Year, result.Tag.Year);
		Assert.AreEqual (original.Comment, result.Tag.Comment);
		Assert.AreEqual (original.Genre, result.Tag.Genre);
	}

	[TestMethod]
	public void RoundTrip_Version11_PreservesTrack ()
	{
		var original = new Id3v1Tag {
			Title = "With Track",
			Comment = "Short comment",
			Track = 12
		};

		var rendered = original.Render ();
		var result = Id3v1Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (12u, result.Tag!.Track);
		Assert.AreEqual ("Short comment", result.Tag.Comment);
	}

	[TestMethod]
	public void RoundTrip_EmptyTag_PreservesEmpty ()
	{
		var original = new Id3v1Tag ();

		var rendered = original.Render ();
		var result = Id3v1Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Tag!.IsEmpty);
	}



	[TestMethod]
	public void GenreIndex_StandardGenres_MapCorrectly ()
	{
		Assert.AreEqual ("Blues", Id3v1Genre.GetName (0));
		Assert.AreEqual ("Classic Rock", Id3v1Genre.GetName (1));
		Assert.AreEqual ("Country", Id3v1Genre.GetName (2));
		Assert.AreEqual ("Dance", Id3v1Genre.GetName (3));
		Assert.AreEqual ("Disco", Id3v1Genre.GetName (4));
		Assert.AreEqual ("Rock", Id3v1Genre.GetName (17));
		Assert.AreEqual ("Pop", Id3v1Genre.GetName (13));
	}

	[TestMethod]
	public void GenreIndex_OutOfRange_ReturnsNull ()
	{
		Assert.IsNull (Id3v1Genre.GetName (255));
		Assert.IsNull (Id3v1Genre.GetName (200));
	}

	[TestMethod]
	public void GenreName_ValidName_ReturnsIndex ()
	{
		Assert.AreEqual (0, Id3v1Genre.GetIndex ("Blues"));
		Assert.AreEqual (17, Id3v1Genre.GetIndex ("Rock"));
		Assert.AreEqual (13, Id3v1Genre.GetIndex ("Pop"));
	}

	[TestMethod]
	public void GenreName_CaseInsensitive_ReturnsIndex ()
	{
		Assert.AreEqual (0, Id3v1Genre.GetIndex ("blues"));
		Assert.AreEqual (0, Id3v1Genre.GetIndex ("BLUES"));
		Assert.AreEqual (17, Id3v1Genre.GetIndex ("ROCK"));
	}

	[TestMethod]
	public void GenreName_Unknown_ReturnsUndefined ()
	{
		Assert.AreEqual (255, Id3v1Genre.GetIndex ("Not A Genre"));
		Assert.AreEqual (255, Id3v1Genre.GetIndex (null));
		Assert.AreEqual (255, Id3v1Genre.GetIndex (""));
	}



	[TestMethod]
	public void Clear_ResetsAllFields ()
	{
		var tag = new Id3v1Tag {
			Title = "Test",
			Artist = "Artist",
			Track = 5
		};

		tag.Clear ();

		Assert.IsTrue (tag.IsEmpty);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Artist);
		Assert.IsNull (tag.Track);
	}

	[TestMethod]
	public void IsEmpty_WithAnyField_ReturnsFalse ()
	{
		var tag = new Id3v1Tag { Title = "X" };
		Assert.IsFalse (tag.IsEmpty);

		tag = new Id3v1Tag { Track = 1 };
		Assert.IsFalse (tag.IsEmpty);
	}



	static byte[] CreateId3v1Tag (string title, string artist, string album,
		string year, string comment, byte genre)
	{
		var data = new byte[128];
		data[0] = (byte)'T';
		data[1] = (byte)'A';
		data[2] = (byte)'G';

		WriteField (data, 3, 30, title);
		WriteField (data, 33, 30, artist);
		WriteField (data, 63, 30, album);
		WriteField (data, 93, 4, year);
		WriteField (data, 97, 30, comment);
		data[127] = genre;

		return data;
	}

	static byte[] CreateId3v11Tag (string title, string artist, string album,
		string year, string comment, byte track, byte genre)
	{
		var data = CreateId3v1Tag (title, artist, album, year, comment, genre);

		// ID3v1.1: comment is 28 bytes, then null, then track
		// Rewrite comment field for v1.1
		for (var i = 97; i < 127; i++)
			data[i] = 0;

		WriteField (data, 97, 28, comment);
		data[125] = 0; // Null separator (indicates v1.1)
		data[126] = track;

		return data;
	}

	static void WriteField (byte[] data, int offset, int size, string value)
	{
		var bytes = System.Text.Encoding.Latin1.GetBytes (value);
		var length = Math.Min (bytes.Length, size);
		Array.Copy (bytes, 0, data, offset, length);
	}

}
