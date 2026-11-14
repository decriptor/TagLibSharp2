// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2TagUnsyncAndV22Tests
{
	// ===========================================================================
	// ID3v2.2 Support Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V22Tag_ParsesVersionCorrectly ()
	{
		var data = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version2, 0);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.Tag!.Version);
	}

	[TestMethod]
	public void Read_V22TagWithTitle_ParsesTitleFrame ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrameV22 (
			TestConstants.FrameIdsV22.Title,
			TestConstants.Metadata.Title);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V22TagWithArtist_ParsesArtistFrame ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrameV22 (
			TestConstants.FrameIdsV22.Artist,
			TestConstants.Metadata.Artist);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.Tag!.Artist);
	}

	[TestMethod]
	public void Read_V22TagWithAlbum_ParsesAlbumFrame ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrameV22 (
			TestConstants.FrameIdsV22.Album,
			TestConstants.Metadata.Album);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Album, result.Tag!.Album);
	}

	[TestMethod]
	public void Read_V22TagWithYear_ParsesYearFrame ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrameV22 (
			TestConstants.FrameIdsV22.Year,
			TestConstants.Metadata.Year);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Year, result.Tag!.Year);
	}

	[TestMethod]
	public void Read_V22TagWithTrack_ParsesTrackFrame ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrameV22 (
			TestConstants.FrameIdsV22.Track,
			TestConstants.Metadata.TrackString);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5u, result.Tag!.Track);
	}

	[TestMethod]
	public void Read_V22TagWithMultipleFrames_ParsesAllFrames ()
	{
		// Build multiple v2.2 frames
		var titleFrame = TestBuilders.Id3v2.CreateTextFrameV22 (TestConstants.FrameIdsV22.Title, TestConstants.Metadata.Title);
		var artistFrame = TestBuilders.Id3v2.CreateTextFrameV22 (TestConstants.FrameIdsV22.Artist, TestConstants.Metadata.Artist);
		var albumFrame = TestBuilders.Id3v2.CreateTextFrameV22 (TestConstants.FrameIdsV22.Album, TestConstants.Metadata.Album);

		var frames = new byte[titleFrame.Length + artistFrame.Length + albumFrame.Length];
		titleFrame.CopyTo (frames, 0);
		artistFrame.CopyTo (frames, titleFrame.Length);
		albumFrame.CopyTo (frames, titleFrame.Length + artistFrame.Length);

		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version2, (uint)frames.Length);
		var data = new byte[header.Length + frames.Length];
		header.CopyTo (data, 0);
		frames.CopyTo (data, header.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.Tag.Artist);
		Assert.AreEqual (TestConstants.Metadata.Album, result.Tag.Album);
	}

	// ===========================================================================
	// Unsynchronization Tests
	// ===========================================================================

	[TestMethod]
	public void Read_UnsynchronizedTagWithTitle_ParsesCorrectly ()
	{
		// Create a text frame that contains 0xFF bytes
		var textWithFF = "Test\xFFValue";
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, textWithFF, TestConstants.Id3v2.Version3);

		// Create tag with unsynchronization applied
		var data = TestBuilders.Id3v2.CreateTagWithUnsynchronization (TestConstants.Id3v2.Version3, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (textWithFF, result.Tag!.Title);
	}

	[TestMethod]
	public void Read_UnsynchronizedTagWithMultipleFF_ParsesCorrectly ()
	{
		// Create raw frame content with multiple 0xFF bytes
		// Using raw bytes to avoid string encoding issues
		var frameContent = new byte[] {
			0x00,  // Latin-1 encoding
			0xFF, (byte)'a', 0xFF, (byte)'b', 0xFF, (byte)'c'  // 0xFF a 0xFF b 0xFF c
		};
		var frame = TestBuilders.Id3v2.CreateFrame (TestConstants.FrameIds.Title, frameContent, TestConstants.Id3v2.Version3);

		var data = TestBuilders.Id3v2.CreateTagWithUnsynchronization (TestConstants.Id3v2.Version3, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// Expected: ÿaÿbÿc (0xFF = ÿ in Latin-1)
		Assert.AreEqual ("\u00FFa\u00FFb\u00FFc", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_UnsynchronizedTagWithNoFFBytes_ParsesCorrectly ()
	{
		// Normal text without 0xFF - unsync should not change it
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, TestConstants.Metadata.Title, TestConstants.Id3v2.Version3);

		var data = TestBuilders.Id3v2.CreateTagWithUnsynchronization (TestConstants.Id3v2.Version3, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag!.Title);
	}

	[TestMethod]
	public void Read_UnsyncFlagNotSet_DoesNotRemoveUnsync ()
	{
		// Create a frame that contains 0xFF followed by more data
		// but WITHOUT the unsync flag - should NOT be de-unsynchronized
		// Note: 0x00 terminates Latin-1 strings, so we use 0xFF followed by text
		var frameContent = new byte[] { 0x00, (byte)'H', (byte)'i', 0xFF, (byte)'!' }; // Latin-1 + "Hi" + 0xFF + "!"
		var frame = TestBuilders.Id3v2.CreateFrame (TestConstants.FrameIds.Title, frameContent, TestConstants.Id3v2.Version3);

		var header = TestBuilders.Id3v2.CreateHeader (TestConstants.Id3v2.Version3, (uint)frame.Length);
		var data = new byte[header.Length + frame.Length];
		header.CopyTo (data, 0);
		frame.CopyTo (data, header.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// The title should include the \xFF! as-is since unsync flag is not set
		Assert.AreEqual ("Hi\u00FF!", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_UnsynchronizedV24Tag_ParsesCorrectly ()
	{
		// v2.4 unsynchronises the frame, not the whole tag. The syncsafe size
		// counts the stored bytes, including the 00 inserted after FF.
		var textWithFF = "Value\xFFHere";
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, textWithFF, TestConstants.Id3v2.Version4);
		var unsyncPayload = TestBuilders.Id3v2.ApplyUnsynchronization (frame[10..]);
		var sized = new byte[10 + unsyncPayload.Length];
		frame.AsSpan (0, 10).CopyTo (sized);
		unsyncPayload.CopyTo (sized.AsSpan (10));
		var len = unsyncPayload.Length;
		sized[4] = (byte)((len >> 21) & 0x7F);
		sized[5] = (byte)((len >> 14) & 0x7F);
		sized[6] = (byte)((len >> 7) & 0x7F);
		sized[7] = (byte)(len & 0x7F);
		sized[9] = 0x02;

		var header = TestBuilders.Id3v2.CreateHeaderWithFlags (
			TestConstants.Id3v2.Version4, (uint)sized.Length, TestConstants.Id3v2.FlagUnsynchronization);
		var data = new byte[header.Length + sized.Length];
		header.CopyTo (data, 0);
		sized.CopyTo (data, header.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (textWithFF, result.Tag!.Title);
	}

	[TestMethod]
	public void Read_UnsynchronizedTagWithTrailingFF_ParsesCorrectly ()
	{
		// Trailing 0xFF followed by nothing should still work
		var textEndingFF = "Test\xFF";
		var frame = TestBuilders.Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, textEndingFF, TestConstants.Id3v2.Version3);

		var data = TestBuilders.Id3v2.CreateTagWithUnsynchronization (TestConstants.Id3v2.Version3, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (textEndingFF, result.Tag!.Title);
	}

	// ===========================================================================
	// Combined V2.2 + Unsynchronization Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V22WithUnsynchronization_ParsesCorrectly ()
	{
		// V2.2 tags can also have unsynchronization
		var textWithFF = "Title\xFFTest";
		var textBytes = Encoding.Latin1.GetBytes (textWithFF);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		textBytes.CopyTo (frameContent, 1);

		var frame = TestBuilders.Id3v2.CreateFrameV22 (TestConstants.FrameIdsV22.Title, frameContent);

		// Apply unsynchronization and set flag
		var unsyncFrame = TestBuilders.Id3v2.ApplyUnsynchronization (frame);
		var header = TestBuilders.Id3v2.CreateHeaderWithFlags (
			TestConstants.Id3v2.Version2,
			(uint)unsyncFrame.Length,
			TestConstants.Id3v2.FlagUnsynchronization);

		var data = new byte[header.Length + unsyncFrame.Length];
		header.CopyTo (data, 0);
		unsyncFrame.CopyTo (data, header.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.Tag!.Version);
		Assert.AreEqual (textWithFF, result.Tag.Title);
	}
}
