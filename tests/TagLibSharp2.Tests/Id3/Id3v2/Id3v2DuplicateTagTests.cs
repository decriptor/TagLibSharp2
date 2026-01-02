// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Tests for handling duplicate ID3v2 tags in a single file.
/// Some files may contain both ID3v2.3 and ID3v2.4 tags due to different taggers.
/// </summary>
[TestClass]
public class Id3v2DuplicateTagTests
{
	[TestMethod]
	public void Read_SingleTag_NoDuplicateDetected ()
	{
		var data = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Title,
			"Single Tag",
			TestConstants.Id3v2.Version4);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Single Tag", result.Tag!.Title);
		Assert.IsFalse (result.HasDuplicateTag, "Should not detect duplicate when only one tag exists");
	}

	[TestMethod]
	public void Read_DuplicateTags_DetectsDuplicate ()
	{
		// Create two ID3v2 tags back-to-back
		var tag1 = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Title,
			"First Tag",
			TestConstants.Id3v2.Version3);
		var tag2 = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Title,
			"Second Tag",
			TestConstants.Id3v2.Version4);

		// Concatenate both tags
		var data = new byte[tag1.Length + tag2.Length];
		Array.Copy (tag1, data, tag1.Length);
		Array.Copy (tag2, 0, data, tag1.Length, tag2.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("First Tag", result.Tag!.Title, "Should use first tag's data");
		Assert.IsTrue (result.HasDuplicateTag, "Should detect duplicate tag");
	}

	[TestMethod]
	public void Read_DuplicateTags_V23ThenV24_DetectsDuplicate ()
	{
		// Common scenario: file tagged by WMP (v2.3) then iTunes (v2.4)
		var v23Tag = CreateTagWithVersion (3, "WMP Title", "WMP Artist");
		var v24Tag = CreateTagWithVersion (4, "iTunes Title", "iTunes Artist");

		var data = new byte[v23Tag.Length + v24Tag.Length];
		Array.Copy (v23Tag, data, v23Tag.Length);
		Array.Copy (v24Tag, 0, data, v23Tag.Length, v24Tag.Length);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("WMP Title", result.Tag!.Title, "Should use first (v2.3) tag");
		Assert.IsTrue (result.HasDuplicateTag);
	}

	[TestMethod]
	public void Read_TagFollowedByPadding_NoDuplicateDetected ()
	{
		// Create tag with extra padding at end (not a second tag)
		var tag = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Title,
			"Title",
			TestConstants.Id3v2.Version4);

		// Add padding (zeros, not a valid ID3 header)
		var data = new byte[tag.Length + 100];
		Array.Copy (tag, data, tag.Length);
		// Remaining bytes are zeros (padding)

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.HasDuplicateTag, "Padding should not be detected as duplicate");
	}

	[TestMethod]
	public void Read_TagFollowedByAudioData_NoDuplicateDetected ()
	{
		// Create tag followed by MP3 sync bytes (not a second tag)
		var tag = TestBuilders.Id3v2.CreateTagWithTextFrame (
			TestConstants.FrameIds.Title,
			"Title",
			TestConstants.Id3v2.Version4);

		// Add fake audio data (MP3 sync bytes)
		var data = new byte[tag.Length + 10];
		Array.Copy (tag, data, tag.Length);
		data[tag.Length] = 0xFF;     // MP3 sync
		data[tag.Length + 1] = 0xFB; // MP3 frame header

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.HasDuplicateTag, "Audio data should not be detected as duplicate");
	}

	static byte[] CreateTagWithVersion (byte version, string title, string artist)
	{
		using var builder = new BinaryDataBuilder ();

		// Build frames first to calculate size
		var titleFrame = CreateTextFrame ("TIT2", title);
		var artistFrame = CreateTextFrame ("TPE1", artist);
		var framesSize = titleFrame.Length + artistFrame.Length;

		// ID3v2 header
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("ID3"));
		builder.Add (version);
		builder.Add ((byte)0); // Revision
		builder.Add ((byte)0); // Flags

		// Tag size (syncsafe)
		builder.Add ((byte)((framesSize >> 21) & 0x7F));
		builder.Add ((byte)((framesSize >> 14) & 0x7F));
		builder.Add ((byte)((framesSize >> 7) & 0x7F));
		builder.Add ((byte)(framesSize & 0x7F));

		// Frames
		builder.Add (titleFrame);
		builder.Add (artistFrame);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] CreateTextFrame (string frameId, string text)
	{
		var textBytes = System.Text.Encoding.UTF8.GetBytes (text);
		var contentSize = 1 + textBytes.Length; // encoding byte + text

		using var builder = new BinaryDataBuilder ();

		// Frame ID
		builder.Add (System.Text.Encoding.ASCII.GetBytes (frameId));

		// Frame size (syncsafe for simplicity, works for both v2.3 and v2.4 small frames)
		builder.Add ((byte)0);
		builder.Add ((byte)0);
		builder.Add ((byte)0);
		builder.Add ((byte)contentSize);

		// Flags
		builder.Add ((byte)0);
		builder.Add ((byte)0);

		// Content: encoding byte + text
		builder.Add ((byte)3); // UTF-8
		builder.Add (textBytes);

		return builder.ToBinaryData ().ToArray ();
	}
}
