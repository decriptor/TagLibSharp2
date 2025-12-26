// Copyright (c) 2025 Stephen Shaw and contributors
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

	#region Reading Tests

	[TestMethod]
	public void Read_ValidV24Tag_ParsesHeader ()
	{
		var data = CreateMinimalTag (version: 4, size: 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_ValidV23Tag_ParsesHeader ()
	{
		var data = CreateMinimalTag (version: 3, size: 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_TagWithTitleFrame_ParsesTitle ()
	{
		var data = CreateTagWithTextFrame ("TIT2", "Test Title", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Title", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_TagWithArtistFrame_ParsesArtist ()
	{
		var data = CreateTagWithTextFrame ("TPE1", "Test Artist", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Artist", result.Tag!.Artist);
	}

	[TestMethod]
	public void Read_TagWithAlbumFrame_ParsesAlbum ()
	{
		var data = CreateTagWithTextFrame ("TALB", "Test Album", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Album", result.Tag!.Album);
	}

	[TestMethod]
	public void Read_TagWithMultipleFrames_ParsesAll ()
	{
		var data = CreateTagWithMultipleFrames (version: 4);

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
		var dataV23 = CreateTagWithTextFrame ("TYER", "2024", version: 3);
		var resultV23 = Id3v2Tag.Read (dataV23);

		Assert.IsTrue (resultV23.IsSuccess);
		Assert.AreEqual ("2024", resultV23.Tag!.Year);

		// v2.4 uses TDRC
		var dataV24 = CreateTagWithTextFrame ("TDRC", "2024", version: 4);
		var resultV24 = Id3v2Tag.Read (dataV24);

		Assert.IsTrue (resultV24.IsSuccess);
		Assert.AreEqual ("2024", resultV24.Tag!.Year);
	}

	[TestMethod]
	public void Read_TagWithTrack_ParsesTrack ()
	{
		var data = CreateTagWithTextFrame ("TRCK", "5/12", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5u, result.Tag!.Track);
	}

	[TestMethod]
	public void Read_TagWithGenre_ParsesGenre ()
	{
		var data = CreateTagWithTextFrame ("TCON", "Rock", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Rock", result.Tag!.Genre);
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
		var data = CreateTagWithPadding (version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Tag!.Title);
	}

	#endregion

	#region V2.3 vs V2.4 Size Tests

	[TestMethod]
	public void Read_V23_UsesBigEndianFrameSize ()
	{
		var data = CreateTagWithTextFrame ("TIT2", "Test", version: 3);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V24_UsesSyncsafeFrameSize ()
	{
		var data = CreateTagWithTextFrame ("TIT2", "Test", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.Title);
	}

	#endregion

	#region Frame Collection Tests

	[TestMethod]
	public void Frames_ReturnsAllFrames ()
	{
		var data = CreateTagWithMultipleFrames (version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsGreaterThanOrEqualTo (3, result.Tag!.Frames.Count);
	}

	[TestMethod]
	public void GetTextFrame_ExistingFrame_ReturnsValue ()
	{
		var data = CreateTagWithTextFrame ("TIT2", "Test Title", version: 4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Title", result.Tag!.GetTextFrame ("TIT2"));
	}

	[TestMethod]
	public void GetTextFrame_NonExistingFrame_ReturnsNull ()
	{
		var data = CreateMinimalTag (version: 4, size: 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.GetTextFrame ("TIT2"));
	}

	#endregion

	#region Property Tests

	[TestMethod]
	public void Title_SetValue_CreatesOrUpdatesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Title = "New Title";

		Assert.AreEqual ("New Title", tag.Title);
		Assert.AreEqual ("New Title", tag.GetTextFrame ("TIT2"));
	}

	[TestMethod]
	public void Artist_SetValue_CreatesOrUpdatesFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Artist = "New Artist";

		Assert.AreEqual ("New Artist", tag.Artist);
		Assert.AreEqual ("New Artist", tag.GetTextFrame ("TPE1"));
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

	#endregion

	#region Rendering Tests

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
			Title = "Test Title",
			Artist = "Test Artist",
			Album = "Test Album"
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

	#endregion

	#region Extended Header Tests

	[TestMethod]
	public void Read_V24_WithExtendedHeader_SkipsExtendedHeader ()
	{
		// Create a v2.4 tag with extended header flag set
		// Extended header format in v2.4: syncsafe size (includes size field)
		var header = new byte[10];
		header[0] = (byte)'I';
		header[1] = (byte)'D';
		header[2] = (byte)'3';
		header[3] = 4; // v2.4
		header[4] = 0; // revision
		header[5] = 0x40; // Extended header flag

		// Extended header: 6 bytes total (syncsafe size includes itself)
		// Size = 6 in syncsafe = 0x00 0x00 0x00 0x06
		var extHeader = new byte[6];
		extHeader[0] = 0x00;
		extHeader[1] = 0x00;
		extHeader[2] = 0x00;
		extHeader[3] = 0x06;
		extHeader[4] = 0x01; // number of flag bytes
		extHeader[5] = 0x00; // flags

		// Frame: TIT2 with "Test"
		var frame = CreateFrameBytes ("TIT2", "Test", version: 4);

		// Tag size (syncsafe): extended header + frame = 6 + frame.Length
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
		// Extended header format in v2.3: big-endian size (excludes size field) + 6 bytes min
		var header = new byte[10];
		header[0] = (byte)'I';
		header[1] = (byte)'D';
		header[2] = (byte)'3';
		header[3] = 3; // v2.3
		header[4] = 0; // revision
		header[5] = 0x40; // Extended header flag

		// Extended header: size(4) + flags(2) + padding(4) = 10 bytes
		// Size field = 6 (excludes the 4-byte size field itself)
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
		var frame = CreateFrameBytes ("TIT2", "Test", version: 3);

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

	#endregion

	#region Frame Size Edge Cases

	[TestMethod]
	public void Read_V23_FrameSizeWithHighByte0x80_DoesNotOverflow ()
	{
		// This test specifically verifies the fix for integer overflow bug.
		// When data[0] = 0x80, the expression (data[0] << 24) produces -2147483648
		// (negative) instead of 2147483648 (positive) due to signed integer promotion.
		// The size 0x80000005 = 2147483653 bytes, which exceeds available data.
		// With the bug: Frame size becomes negative → comparison logic breaks
		// With the fix: Frame size is positive but exceeds remaining → parsing stops gracefully

		var tagSize = (uint)20; // Small tag
		var header = CreateMinimalTag (version: 3, size: tagSize);
		var frame = new byte[20];

		// Frame ID "TIT2"
		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size with MSB set: 0x80 0x00 0x00 0x05 = 2,147,483,653 bytes
		// This would be -2,147,483,643 with the bug (signed overflow)
		frame[4] = 0x80;  // High byte >= 0x80 triggers the overflow bug
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

		// The frame size (2GB+) exceeds remaining data, so parsing stops gracefully.
		// Key assertion: Should succeed with no frames parsed, NOT crash or throw.
		Assert.IsTrue (result.IsSuccess);
		Assert.IsEmpty (result.Tag!.Frames);
	}

	[TestMethod]
	public void Read_V23_FrameSizeWithHighByte0xFF_DoesNotOverflow ()
	{
		// Test with highest possible byte value (0xFF) to ensure no overflow
		var tagSize = (uint)20;
		var header = CreateMinimalTag (version: 3, size: tagSize);
		var frame = new byte[20];

		frame[0] = (byte)'T';
		frame[1] = (byte)'I';
		frame[2] = (byte)'T';
		frame[3] = (byte)'2';

		// Frame size: 0xFF 0xFF 0xFF 0x05 = 4,294,967,045 (max with 0xFF high byte)
		// Without fix: This becomes -251 (negative) due to overflow
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
		var header = CreateMinimalTag (version: 3, size: 20);
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

	#endregion

	#region Helper Methods

	static byte[] CreateMinimalTag (byte version, uint size)
	{
		var data = new byte[10];
		data[0] = (byte)'I';
		data[1] = (byte)'D';
		data[2] = (byte)'3';
		data[3] = version;
		data[4] = 0; // revision
		data[5] = 0; // flags

		// Syncsafe size
		data[6] = (byte)((size >> 21) & 0x7F);
		data[7] = (byte)((size >> 14) & 0x7F);
		data[8] = (byte)((size >> 7) & 0x7F);
		data[9] = (byte)(size & 0x7F);

		return data;
	}

	static byte[] CreateTagWithTextFrame (string frameId, string text, byte version)
	{
		// Frame content: encoding (1) + text
		var textBytes = System.Text.Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = 0; // Latin-1 encoding
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Frame header (10 bytes) + content
		var frameSize = frameContent.Length;
		var frame = new byte[10 + frameSize];

		// Frame ID
		var idBytes = System.Text.Encoding.ASCII.GetBytes (frameId);
		Array.Copy (idBytes, 0, frame, 0, 4);

		// Size (big-endian for v2.3, syncsafe for v2.4)
		if (version == 4) {
			// Syncsafe
			frame[4] = (byte)((frameSize >> 21) & 0x7F);
			frame[5] = (byte)((frameSize >> 14) & 0x7F);
			frame[6] = (byte)((frameSize >> 7) & 0x7F);
			frame[7] = (byte)(frameSize & 0x7F);
		} else {
			// Big-endian
			frame[4] = (byte)((frameSize >> 24) & 0xFF);
			frame[5] = (byte)((frameSize >> 16) & 0xFF);
			frame[6] = (byte)((frameSize >> 8) & 0xFF);
			frame[7] = (byte)(frameSize & 0xFF);
		}

		// Flags (2 bytes, zeroes)
		frame[8] = 0;
		frame[9] = 0;

		// Content
		Array.Copy (frameContent, 0, frame, 10, frameSize);

		// Combine header + frame
		var totalSize = (uint)frame.Length;
		var header = CreateMinimalTag (version, totalSize);

		var result = new byte[header.Length + frame.Length];
		Array.Copy (header, result, header.Length);
		Array.Copy (frame, 0, result, header.Length, frame.Length);

		return result;
	}

	static byte[] CreateTagWithMultipleFrames (byte version)
	{
		var frames = new List<byte[]> {
			CreateFrameBytes ("TIT2", "Title", version),
			CreateFrameBytes ("TPE1", "Artist", version),
			CreateFrameBytes ("TALB", "Album", version)
		};

		var framesData = frames.SelectMany (f => f).ToArray ();
		var totalSize = (uint)framesData.Length;
		var header = CreateMinimalTag (version, totalSize);

		var result = new byte[header.Length + framesData.Length];
		Array.Copy (header, result, header.Length);
		Array.Copy (framesData, 0, result, header.Length, framesData.Length);

		return result;
	}

	static byte[] CreateTagWithPadding (byte version)
	{
		var frame = CreateFrameBytes ("TIT2", "Title", version);
		var padding = new byte[50]; // 50 bytes of padding

		var totalSize = (uint)(frame.Length + padding.Length);
		var header = CreateMinimalTag (version, totalSize);

		var result = new byte[header.Length + frame.Length + padding.Length];
		Array.Copy (header, result, header.Length);
		Array.Copy (frame, 0, result, header.Length, frame.Length);
		// padding is already zeros

		return result;
	}

	static byte[] CreateFrameBytes (string frameId, string text, byte version)
	{
		var textBytes = System.Text.Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = 0; // Latin-1
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		var frameSize = frameContent.Length;
		var frame = new byte[10 + frameSize];

		var idBytes = System.Text.Encoding.ASCII.GetBytes (frameId);
		Array.Copy (idBytes, 0, frame, 0, 4);

		if (version == 4) {
			frame[4] = (byte)((frameSize >> 21) & 0x7F);
			frame[5] = (byte)((frameSize >> 14) & 0x7F);
			frame[6] = (byte)((frameSize >> 7) & 0x7F);
			frame[7] = (byte)(frameSize & 0x7F);
		} else {
			frame[4] = (byte)((frameSize >> 24) & 0xFF);
			frame[5] = (byte)((frameSize >> 16) & 0xFF);
			frame[6] = (byte)((frameSize >> 8) & 0xFF);
			frame[7] = (byte)(frameSize & 0xFF);
		}

		frame[8] = 0;
		frame[9] = 0;

		Array.Copy (frameContent, 0, frame, 10, frameSize);
		return frame;
	}

	#endregion
}
