// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2TagTests
{
	// ID3v2 Tag Structure:
	// - Header (10 bytes)
	// - Extended header (optional, variable size)
	// - Frames (variable, until padding or end)
	// - Padding (optional, zeros until tag size)
	// - Footer (optional, 10 bytes, v2.4 only)

	// Frame Header (v2.3/v2.4):
	// Offset  Size  Field
	// 0       4     Frame ID (ASCII)
	// 4       4     Size (big-endian v2.3, syncsafe v2.4)
	// 8       2     Flags
	// 10+     n     Frame data


	[TestMethod]
	public void Read_ValidV24Tag_ParsesHeader ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_ValidV23Tag_ParsesHeader ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_TagWithTitleFrame_ParsesTitle ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, TestConstants.Metadata.Title, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.Title);
	}

	[TestMethod]
	public void Read_TagWithArtistFrame_ParsesArtist ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Artist, TestConstants.Metadata.Artist, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.Tag!.Artist);
	}

	[TestMethod]
	public void Read_TagWithAlbumFrame_ParsesAlbum ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Album, TestConstants.Metadata.Album, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Album, result.Tag!.Album);
	}

	[TestMethod]
	public void Read_TagWithMultipleFrames_ParsesAll ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithFrames (TestConstants.Id3v2.Version4,
			(TestConstants.FrameIds.Title, "Title"),
			(TestConstants.FrameIds.Artist, "Artist"),
			(TestConstants.FrameIds.Album, "Album"));

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Tag!.Title);
		Assert.AreEqual ("Artist", result.Tag.Artist);
		Assert.AreEqual ("Album", result.Tag.Album);
	}

	[TestMethod]
	public void Read_TagWithYear_ParsesYear ()
	{
		// v2.3 uses TYER
		var dataV23 = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Year, "2024", TestConstants.Id3v2.Version3);
		var resultV23 = Id3v2Tag.Read (dataV23);

		Assert.IsTrue (resultV23.IsSuccess);
		Assert.AreEqual ("2024", resultV23.Tag!.Year);

		// v2.4 uses TDRC
		var dataV24 = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.RecordingTime, "2024", TestConstants.Id3v2.Version4);
		var resultV24 = Id3v2Tag.Read (dataV24);

		Assert.IsTrue (resultV24.IsSuccess);
		Assert.AreEqual ("2024", resultV24.Tag!.Year);
	}

	[TestMethod]
	public void Read_TagWithTrack_ParsesTrack ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Track, "5/12", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5u, result.Tag!.Track);
	}

	[TestMethod]
	public void Read_TagWithGenre_ParsesGenre ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "Rock", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Rock", result.Tag!.Genre);
	}

	[TestMethod]
	public void Read_TagWithNumericGenreReference_ResolvesId3v1Name ()
	{
		// ID3v2.3 TCON "(13)" is genre 13 in the ID3v1 list, which is Pop.
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Genre, "(13)", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Pop", result.Tag!.Genre);
	}

	[TestMethod]
	public void Read_NoMagic_ReturnsNotFound ()
	{
		var data = new byte[] { (byte)'X', (byte)'Y', (byte)'Z', 4, 0, 0, 0, 0, 0, 0 };

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsNotFound);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { (byte)'I', (byte)'D', (byte)'3' };

		var result = Id3v2Tag.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_TagWithPadding_StopsAtPadding ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithPadding (TestConstants.Id3v2.Version4, 50);
		// Add a frame before the padding
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Title", TestConstants.Id3v2.Version4);
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, (uint)(frame.Length + 50));
		var fullData = new byte[header.Length + frame.Length + 50];
		Array.Copy (header, fullData, header.Length);
		Array.Copy (frame, 0, fullData, header.Length, frame.Length);

		var result = Id3v2Tag.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Tag!.Title);
	}



	[TestMethod]
	public void Read_V23_UsesBigEndianFrameSize ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V24_UsesSyncsafeFrameSize ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}



	[TestMethod]
	public void Frames_ReturnsAllFrames ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithFrames (TestConstants.Id3v2.Version4,
			(TestConstants.FrameIds.Title, "Title"),
			(TestConstants.FrameIds.Artist, "Artist"),
			(TestConstants.FrameIds.Album, "Album"));

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsGreaterThanOrEqualTo (3, result.Tag!.Frames.Count);
	}

	[TestMethod]
	public void GetTextFrame_ExistingFrame_ReturnsValue ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.Title, TestConstants.Metadata.Title, TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.GetTextFrame (TestConstants.FrameIds.Title));
	}

	[TestMethod]
	public void GetTextFrame_NonExistingFrame_ReturnsNull ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.GetTextFrame (TestConstants.FrameIds.Title));
	}



	[TestMethod]
	public void Title_SetValue_CreatesOrUpdatesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Title = "New Title";

		Assert.AreEqual ("New Title", tag.Title);
		Assert.AreEqual ("New Title", tag.GetTextFrame (TestConstants.FrameIds.Title));
	}

	[TestMethod]
	public void Artist_SetValue_CreatesOrUpdatesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Artist = "New Artist";

		Assert.AreEqual ("New Artist", tag.Artist);
		Assert.AreEqual ("New Artist", tag.GetTextFrame (TestConstants.FrameIds.Artist));
	}

	[TestMethod]
	public void Clear_RemovesAllFrames ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) {
			Title = "Title",
			Artist = "Artist"
		};

		tag.Clear ();

		Assert.IsTrue (tag.IsEmpty);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Artist);
	}

	[TestMethod]
	public void IsEmpty_NoFrames_ReturnsTrue ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_WithFrames_ReturnsFalse ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "X" };

		Assert.IsFalse (tag.IsEmpty);
	}



	[TestMethod]
	public void Render_EmptyTag_CreatesValidHeader ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		var data = tag.Render ();

		// Should have at least header (10 bytes)
		Assert.IsGreaterThanOrEqualTo (10, data.Length);
		Assert.IsTrue (data.StartsWith ("ID3"u8));
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist,
			Album = TestConstants.Metadata.Album
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Title, result.Tag!.Title);
		Assert.AreEqual (original.Artist, result.Tag.Artist);
		Assert.AreEqual (original.Album, result.Tag.Album);
	}

	[TestMethod]
	public void Render_WithPadding_IncludesPadding ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "X" };

		var data = tag.Render (paddingSize: 100);

		// Should include padding in total size
		Assert.IsGreaterThan (100, data.Length);
	}

	// ═══════════════════════════════════════════════════════════════
	// Footer Tests (TDD - ID3v2.4 footer support)
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_WithFooter_Includes10ByteFooter ()
	{
		// ID3v2.4 only - footer is 10 bytes with "3DI" magic
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Test" };

		var dataWithoutFooter = tag.Render (paddingSize: 0, withFooter: false);
		var dataWithFooter = tag.Render (paddingSize: 0, withFooter: true);

		// Footer adds exactly 10 bytes
		Assert.AreEqual (dataWithoutFooter.Length + 10, dataWithFooter.Length);
	}

	[TestMethod]
	public void Render_WithFooter_EndsWithMagic3DI ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Test" };

		var data = tag.Render (paddingSize: 0, withFooter: true);

		// Footer magic "3DI" at end
		Assert.AreEqual ((byte)'3', data[data.Length - 10]);
		Assert.AreEqual ((byte)'D', data[data.Length - 9]);
		Assert.AreEqual ((byte)'I', data[data.Length - 8]);
	}

	[TestMethod]
	public void Render_WithFooter_SetsFooterFlagInHeader ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Test" };

		var data = tag.Render (paddingSize: 0, withFooter: true);

		// Header flags at offset 5, footer flag is 0x10 (bit 4)
		Assert.IsTrue ((data[5] & 0x10) != 0, "Footer flag should be set in header");
	}

	[TestMethod]
	public void Render_WithFooter_RoundTripPreservesData ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24) {
			Title = "Test Title",
			Artist = "Test Artist"
		};

		var rendered = original.Render (paddingSize: 0, withFooter: true);
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Title", result.Tag!.Title);
		Assert.AreEqual ("Test Artist", result.Tag.Artist);
	}

	[TestMethod]
	public void Render_V23WithFooter_DoesNotIncludeFooter ()
	{
		// Footer is only valid for v2.4, v2.3 should ignore
		var tag = new Id3v2Tag (Id3v2Version.V23) { Title = "Test" };

		var dataWithoutFooter = tag.Render (paddingSize: 0, withFooter: false);
		var dataWithFooter = tag.Render (paddingSize: 0, withFooter: true);

		// V2.3 should NOT add footer even when requested
		Assert.AreEqual (dataWithoutFooter.Length, dataWithFooter.Length);
	}

	[TestMethod]
	public void AlbumArtist_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AlbumArtist = "Various Artists";

		Assert.AreEqual ("Various Artists", tag.AlbumArtist);
		Assert.AreEqual ("Various Artists", tag.GetTextFrame (TestConstants.FrameIds.AlbumArtist));
	}

	[TestMethod]
	public void AlbumArtist_FromFile_ParsesCorrectly ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.AlbumArtist, "Compilation Artist", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Compilation Artist", result.Tag!.AlbumArtist);
	}

	[TestMethod]
	public void DiscNumber_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.DiscNumber = 2;

		Assert.AreEqual (2u, tag.DiscNumber);
		Assert.AreEqual ("2", tag.GetTextFrame (TestConstants.FrameIds.DiscNumber));
	}

	[TestMethod]
	public void DiscNumber_WithSlashFormat_ParsesCorrectly ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (TestConstants.FrameIds.DiscNumber, "2/3", TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2u, result.Tag!.DiscNumber);
	}

	[TestMethod]
	public void DiscNumber_SetNull_ClearsField ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.DiscNumber = 2;

		tag.DiscNumber = null;

		Assert.IsNull (tag.DiscNumber);
		Assert.IsNull (tag.GetTextFrame (TestConstants.FrameIds.DiscNumber));
	}

	[TestMethod]
	public void Composer_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.Composer = "Johann Sebastian Bach";

		Assert.AreEqual ("Johann Sebastian Bach", tag.Composer);
		Assert.AreEqual ("Johann Sebastian Bach", tag.GetTextFrame (TestConstants.FrameIds.Composer));
	}

	[TestMethod]
	public void BeatsPerMinute_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.BeatsPerMinute = 120;

		Assert.AreEqual (120u, tag.BeatsPerMinute);
		Assert.AreEqual ("120", tag.GetTextFrame (TestConstants.FrameIds.Bpm));
	}



	[TestMethod]
	public void Read_V24_WithExtendedHeader_SkipsExtendedHeader ()
	{
		// Create a v2.4 tag with extended header flag set
		var header = new byte[10];
		header[0] = (byte)'I';
		header[1] = (byte)'D';
		header[2] = (byte)'3';
		header[3] = 4; // v2.4
		header[4] = 0; // revision
		header[5] = 0x40; // Extended header flag

		// Extended header: 6 bytes total (syncsafe size includes itself)
		var extHeader = new byte[6];
		extHeader[0] = 0x00;
		extHeader[1] = 0x00;
		extHeader[2] = 0x00;
		extHeader[3] = 0x06;
		extHeader[4] = 0x01; // number of flag bytes
		extHeader[5] = 0x00; // flags

		// Frame: TIT2 with "Test"
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version4);

		// Tag size (syncsafe): extended header + frame
		var tagSize = (uint)(extHeader.Length + frame.Length);
		header[6] = (byte)((tagSize >> 21) & 0x7F);
		header[7] = (byte)((tagSize >> 14) & 0x7F);
		header[8] = (byte)((tagSize >> 7) & 0x7F);
		header[9] = (byte)(tagSize & 0x7F);

		var data = new byte[header.Length + extHeader.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (extHeader, 0, data, header.Length, extHeader.Length);
		Array.Copy (frame, 0, data, header.Length + extHeader.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V23_WithExtendedHeader_SkipsExtendedHeader ()
	{
		// Create a v2.3 tag with extended header flag set
		var header = new byte[10];
		header[0] = (byte)'I';
		header[1] = (byte)'D';
		header[2] = (byte)'3';
		header[3] = 3; // v2.3
		header[4] = 0; // revision
		header[5] = 0x40; // Extended header flag

		// Extended header: size(4) + flags(2) + padding(4) = 10 bytes
		var extHeader = new byte[10];
		extHeader[0] = 0x00;
		extHeader[1] = 0x00;
		extHeader[2] = 0x00;
		extHeader[3] = 0x06; // 6 bytes after size field
		extHeader[4] = 0x00; // flags byte 1
		extHeader[5] = 0x00; // flags byte 2
		extHeader[6] = 0x00; // padding size
		extHeader[7] = 0x00;
		extHeader[8] = 0x00;
		extHeader[9] = 0x00;

		// Frame: TIT2 with "Test"
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version3);

		// Tag size (syncsafe): extended header + frame
		var tagSize = (uint)(extHeader.Length + frame.Length);
		header[6] = (byte)((tagSize >> 21) & 0x7F);
		header[7] = (byte)((tagSize >> 14) & 0x7F);
		header[8] = (byte)((tagSize >> 7) & 0x7F);
		header[9] = (byte)(tagSize & 0x7F);

		var data = new byte[header.Length + extHeader.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (extHeader, 0, data, header.Length, extHeader.Length);
		Array.Copy (frame, 0, data, header.Length + extHeader.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}



	[TestMethod]
	public void Read_V23_FrameSizeWithHighByte0x80_DoesNotOverflow ()
	{
		// This test verifies the fix for integer overflow bug.
		// When data[0] = 0x80, the expression (data[0] << 24) produces -2147483648
		// (negative) instead of 2147483648 (positive) due to signed integer promotion.
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 20);
		var frame = new byte[20];

		// Frame ID "TIT2"
		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size with MSB set: 0x80 0x00 0x00 0x05 = 2,147,483,653 bytes
		frame[4] = 0x80;
		frame[5] = 0x00;
		frame[6] = 0x00;
		frame[7] = 0x05;

		// Flags
		frame[8] = 0x00;
		frame[9] = 0x00;

		// Frame content (won't be reached due to size overflow)
		frame[10] = 0x00;
		frame[11] = (byte)'X';

		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		// Should succeed with no frames parsed, NOT crash or throw.
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}

	[TestMethod]
	public void Read_V23_FrameSizeWithHighByte0xFF_DoesNotOverflow ()
	{
		// Test with highest possible byte value (0xFF)
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 20);
		var frame = new byte[20];

		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size: 0xFF 0xFF 0xFF 0x05 = 4,294,967,045
		frame[4] = 0xFF;
		frame[5] = 0xFF;
		frame[6] = 0xFF;
		frame[7] = 0x05;

		frame[8] = 0x00;
		frame[9] = 0x00;
		frame[10] = 0x00;
		frame[11] = (byte)'X';

		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		// Should not crash; frame size exceeds data, so no frames parsed
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}

	[TestMethod]
	public void Read_V23_NormalFrameSize_ParsesCorrectly ()
	{
		// Baseline test: Normal frame size should still work
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, 20);
		var frame = new byte[20];

		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size: 0x00 0x00 0x00 0x05 = 5 bytes (normal)
		frame[4] = 0x00;
		frame[5] = 0x00;
		frame[6] = 0x00;
		frame[7] = 0x05;

		frame[8] = 0x00;
		frame[9] = 0x00;

		// Frame content: encoding (Latin-1) + "Test"
		frame[10] = 0x00;
		frame[11] = (byte)'T';
		frame[12] = (byte)'e';
		frame[13] = (byte)'s';
		frame[14] = (byte)'t';

		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_TruncatedFile_HandlesGracefully ()
	{
		// Create a tag that claims to be 1000 bytes but only has 50 bytes of data.
		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 1000);

		// Add a valid frame that fits in the truncated data
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, "Test", TestConstants.Id3v2.Version4);

		// Total data is header (10) + frame (~16), but header claims 1000 bytes
		var data = new byte[header.Length + frame.Length];
		Array.Copy (header, data, header.Length);
		Array.Copy (frame, 0, data, header.Length, frame.Length);

		var result = Id3v2Tag.Read (data);

		// Should succeed and parse the frame that's actually present
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_HeaderClaimsLargeSize_NoFramesAvailable ()
	{
		// Header claims 10000 bytes but there's no frame data at all
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version4, 10000);

		var result = Id3v2Tag.Read (data);

		// Should succeed with no frames (header-only is valid, just empty)
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}
}
