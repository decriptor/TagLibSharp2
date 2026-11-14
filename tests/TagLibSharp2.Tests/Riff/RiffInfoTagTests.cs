// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class RiffInfoTagTests
{
	static BinaryData CreateInfoChunk (string fieldId, string value)
	{
		var valueBytes = System.Text.Encoding.Latin1.GetBytes (value + "\0");
		using var builder = new BinaryDataBuilder (4 + 8 + valueBytes.Length + (valueBytes.Length & 1));
		builder.AddStringLatin1 ("INFO");
		builder.AddStringLatin1 (fieldId);
		builder.AddUInt32LE ((uint)valueBytes.Length);
		builder.Add (new BinaryData (valueBytes));
		if ((valueBytes.Length & 1) != 0)
			builder.Add (0);
		return builder.ToBinaryData ();
	}

	[TestMethod]
	public void Parse_ValidInfoChunk_ReturnsTag ()
	{
		var data = CreateInfoChunk ("INAM", "Test Title");
		var tag = RiffInfoTag.Parse (data);

		Assert.IsNotNull (tag);
		Assert.AreEqual ("Test Title", tag.Title);
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsNull ()
	{
		var data = new BinaryData ([1, 2, 3]);
		Assert.IsNull (RiffInfoTag.Parse (data));
	}

	[TestMethod]
	public void Parse_WrongListType_ReturnsNull ()
	{
		var data = new BinaryData ([(byte)'a', (byte)'d', (byte)'t', (byte)'l']);
		Assert.IsNull (RiffInfoTag.Parse (data));
	}

	[TestMethod]
	public void TagType_ReturnsRiffInfo ()
	{
		var tag = new RiffInfoTag ();
		Assert.AreEqual (TagTypes.RiffInfo, tag.TagType);
	}

	[TestMethod]
	public void Title_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Title = "My Title" };
		Assert.AreEqual ("My Title", tag.Title);
	}

	[TestMethod]
	public void Title_SetNull_RemovesField ()
	{
		var tag = new RiffInfoTag { Title = "Original" };
		tag.Title = null;
		Assert.IsNull (tag.Title);
	}

	[TestMethod]
	public void Artist_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Artist = "The Artist" };
		Assert.AreEqual ("The Artist", tag.Artist);
	}

	[TestMethod]
	public void Performers_Get_ReturnsArrayWithArtist ()
	{
		var tag = new RiffInfoTag { Artist = "Performer" };
		Assert.AreEqual (1, tag.Performers.Length);
		Assert.AreEqual ("Performer", tag.Performers[0]);
	}

	[TestMethod]
	public void Performers_GetEmpty_ReturnsEmptyArray ()
	{
		var tag = new RiffInfoTag ();
		Assert.AreEqual (0, tag.Performers.Length);
	}

	[TestMethod]
	public void Album_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Album = "Album Name" };
		Assert.AreEqual ("Album Name", tag.Album);
	}

	[TestMethod]
	public void Comment_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Comment = "A comment" };
		Assert.AreEqual ("A comment", tag.Comment);
	}

	[TestMethod]
	public void Genre_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Genre = "Rock" };
		Assert.AreEqual ("Rock", tag.Genre);
	}

	[TestMethod]
	public void Year_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Year = "2024" };
		Assert.AreEqual ("2024", tag.Year);
	}

	[TestMethod]
	public void Track_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Track = 5 };
		Assert.AreEqual (5u, tag.Track);
	}

	[TestMethod]
	public void Track_InvalidNumber_ReturnsNull ()
	{
		var data = CreateInfoChunk ("ITRK", "not-a-number");
		var tag = RiffInfoTag.Parse (data);
		Assert.IsNull (tag?.Track);
	}

	[TestMethod]
	public void Copyright_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Copyright = "(C) 2024" };
		Assert.AreEqual ("(C) 2024", tag.Copyright);
	}

	[TestMethod]
	public void Software_GetSet_Works ()
	{
		var tag = new RiffInfoTag { Software = "TagLibSharp2" };
		Assert.AreEqual ("TagLibSharp2", tag.Software);
	}

	[TestMethod]
	public void GetField_CaseInsensitive ()
	{
		var tag = new RiffInfoTag ();
		tag.SetField ("INAM", "Title");
		Assert.AreEqual ("Title", tag.GetField ("inam"));
	}

	[TestMethod]
	public void IsEmpty_NoFields_ReturnsTrue ()
	{
		var tag = new RiffInfoTag ();
		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_HasFields_ReturnsFalse ()
	{
		var tag = new RiffInfoTag { Title = "T" };
		Assert.IsFalse (tag.IsEmpty);
	}

	[TestMethod]
	public void Clear_RemovesAllFields ()
	{
		var tag = new RiffInfoTag { Title = "T", Artist = "A" };
		tag.Clear ();
		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void Render_EmptyTag_ReturnsEmpty ()
	{
		var tag = new RiffInfoTag ();
		Assert.IsTrue (tag.Render ().IsEmpty);
	}

	[TestMethod]
	public void Render_WithFields_ProducesValidListChunk ()
	{
		var tag = new RiffInfoTag { Title = "Test" };
		var rendered = tag.Render ();

		Assert.AreEqual ((byte)'L', rendered[0]);
		Assert.AreEqual ((byte)'I', rendered[1]);
		Assert.AreEqual ((byte)'S', rendered[2]);
		Assert.AreEqual ((byte)'T', rendered[3]);
		Assert.AreEqual ((byte)'I', rendered[8]);
		Assert.AreEqual ((byte)'N', rendered[9]);
		Assert.AreEqual ((byte)'F', rendered[10]);
		Assert.AreEqual ((byte)'O', rendered[11]);
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var original = new RiffInfoTag {
			Title = "Test Song",
			Artist = "Test Artist",
			Album = "Test Album",
			Year = "2024",
			Track = 7,
			Genre = "Rock"
		};

		var rendered = original.Render ();
		var roundTripped = RiffInfoTag.Parse (rendered.Slice (8));

		Assert.IsNotNull (roundTripped);
		Assert.AreEqual (original.Title, roundTripped.Title);
		Assert.AreEqual (original.Artist, roundTripped.Artist);
		Assert.AreEqual (original.Album, roundTripped.Album);
	}

	[TestMethod]
	public void Parse_FieldSizeAboveIntMax_DoesNotThrow ()
	{
		// INFO plus one field whose size does not fit the buffer and does not fit in an int.
		var data = new byte[16];
		"INFO"u8.CopyTo (data);
		"INAM"u8.CopyTo (data.AsSpan (4));
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (8), uint.MaxValue);

		RiffInfoTag? tag = null;
		try {
			tag = RiffInfoTag.Parse (new BinaryData (data));
		} catch (Exception ex) {
			Assert.Fail ($"Parse threw {ex.GetType ().Name}: {ex.Message}");
		}

		Assert.IsNotNull (tag);
		Assert.IsNull (tag.Title);
	}
}
