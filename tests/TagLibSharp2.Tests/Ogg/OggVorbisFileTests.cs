// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ogg")]
public class OggVorbisFileTests
{
	[TestMethod]
	public void Read_ValidOggVorbis_ParsesVorbisComment ()
	{
		var data = BuildMinimalOggVorbisFile ("Test Title", "Test Artist");

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Title", result.File.VorbisComment.Title);
		Assert.AreEqual ("Test Artist", result.File.VorbisComment.Artist);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[100]; // All zeros, no valid Ogg pages

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("Ogg", result.Error!);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x4F, 0x67, 0x67, 0x53 }; // Just "OggS"

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_NotVorbisCodec_ReturnsFailure ()
	{
		// Valid Ogg page but not a Vorbis identification header
		var data = BuildOggPageWithNonVorbisData ();

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("Vorbis", result.Error!);
	}

	[TestMethod]
	public void Title_DelegatesToVorbisComment ()
	{
		var data = BuildMinimalOggVorbisFile ("My Song", "");

		var result = OggVorbisFile.Read (data);

		Assert.AreEqual ("My Song", result.File!.Title);
	}

	[TestMethod]
	public void Title_Set_UpdatesVorbisComment ()
	{
		var data = BuildMinimalOggVorbisFile ("", "");
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		Assert.AreEqual ("New Title", file.Title);
		Assert.IsNotNull (file.VorbisComment);
	}

	[TestMethod]
	public void Artist_DelegatesToVorbisComment ()
	{
		var data = BuildMinimalOggVorbisFile ("", "Test Artist");

		var result = OggVorbisFile.Read (data);

		Assert.AreEqual ("Test Artist", result.File!.Artist);
	}

	[TestMethod]
	public void Read_InvalidFramingBit_ReturnsFailure ()
	{
		// Build a file where the comment header has invalid framing bit
		var data = BuildOggVorbisFileWithInvalidFramingBit ();

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("framing", result.Error!.ToLowerInvariant ());
	}

	[TestMethod]
	public void Read_ValidFramingBit_Succeeds ()
	{
		var data = BuildMinimalOggVorbisFile ("Title", "Artist");

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
	}

	[TestMethod]
	public void Read_LargeCommentSpanningMultiplePages_ParsesCorrectly ()
	{
		// Build an Ogg Vorbis file where the comment header spans 3+ pages
		// Ogg pages max out at ~64KB (255 segments * 255 bytes)
		// We'll create a comment with a very long field that spans multiple pages
		var data = BuildOggVorbisFileWithMultiPageComment ();

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Multi-Page Title", result.File.VorbisComment.Title);
		// Verify the long field was properly reassembled
		var longValue = result.File.VorbisComment.GetValue ("LONGFIELD");
		Assert.IsNotNull (longValue);
		Assert.IsGreaterThan (60000, longValue!.Length);
	}

	[TestMethod]
	public void Read_Utf8EdgeCases_ParsesCorrectly ()
	{
		// Test various UTF-8 edge cases:
		// - ASCII
		// - Multi-byte characters (Chinese, Japanese, Korean)
		// - Emojis (4-byte UTF-8)
		// - Right-to-left (Arabic)
		// - Combining characters
		var data = BuildMinimalOggVorbisFile ("日本語タイトル 🎵", "Художник");

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("日本語タイトル 🎵", result.File!.Title);
		Assert.AreEqual ("Художник", result.File.Artist);
	}

	[TestMethod]
	public void Read_Utf8EmptyAndWhitespace_ParsesCorrectly ()
	{
		var data = BuildMinimalOggVorbisFile ("", "   ");

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.File!.Title); // Empty string becomes null
		Assert.AreEqual ("   ", result.File.Artist); // Whitespace preserved
	}

	[TestMethod]
	public void Read_Utf8CombiningCharacters_ParsesCorrectly ()
	{
		// Test combining characters (e and combining acute accent vs precomposed é)
		var data = BuildMinimalOggVorbisFile ("Café", "Naïve");

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Café", result.File!.Title);
		Assert.AreEqual ("Naïve", result.File.Artist);
	}

	#region Helper Methods

	/// <summary>
	/// Builds a minimal Ogg Vorbis file with the three required Vorbis header packets:
	/// 1. Identification header (packet type 1)
	/// 2. Comment header (packet type 3) - contains Vorbis Comment metadata
	/// 3. Setup header (packet type 5)
	/// </summary>
	static byte[] BuildMinimalOggVorbisFile (string title, string artist)
	{
		using var builder = new BinaryDataBuilder ();

		// Build Vorbis identification header (30 bytes minimum)
		var identPacket = BuildVorbisIdentificationPacket ();

		// Build Vorbis comment header
		var comment = new VorbisComment ("TagLibSharp2");
		if (!string.IsNullOrEmpty (title))
			comment.Title = title;
		if (!string.IsNullOrEmpty (artist))
			comment.Artist = artist;
		var commentData = comment.Render ().ToArray ();

		// Wrap comment in Vorbis header format (packet type 3 + "vorbis")
		var commentPacket = BuildVorbisCommentPacket (commentData);

		// Build minimal setup header (just the marker)
		var setupPacket = BuildVorbisSetupPacket ();

		// Page 1: BOS page with identification header
		var page1 = BuildOggPage (
			flags: OggPageFlags.BeginOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 0,
			data: identPacket);
		builder.Add (page1);

		// Page 2: Comment header page
		var page2 = BuildOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 1,
			data: commentPacket);
		builder.Add (page2);

		// Page 3: Setup header and EOS
		var page3 = BuildOggPage (
			flags: OggPageFlags.EndOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 2,
			data: setupPacket);
		builder.Add (page3);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildVorbisIdentificationPacket ()
	{
		using var builder = new BinaryDataBuilder ();

		// Packet type 1 (identification)
		builder.Add ((byte)1);

		// "vorbis" magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("vorbis"));

		// Vorbis version (0)
		builder.Add (BitConverter.GetBytes (0U));

		// Audio channels (2)
		builder.Add ((byte)2);

		// Sample rate (44100 Hz)
		builder.Add (BitConverter.GetBytes (44100U));

		// Bitrate maximum (0 = unset)
		builder.Add (BitConverter.GetBytes (0));

		// Bitrate nominal (128000)
		builder.Add (BitConverter.GetBytes (128000));

		// Bitrate minimum (0 = unset)
		builder.Add (BitConverter.GetBytes (0));

		// Block sizes (encoded in 1 byte: log2 values)
		// blocksize_0 = 256 (log2 = 8), blocksize_1 = 2048 (log2 = 11)
		builder.Add ((byte)0xB8); // (11 << 4) | 8

		// Framing bit
		builder.Add ((byte)1);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildVorbisCommentPacket (byte[] commentData)
	{
		using var builder = new BinaryDataBuilder ();

		// Packet type 3 (comment)
		builder.Add ((byte)3);

		// "vorbis" magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("vorbis"));

		// Comment data
		builder.Add (commentData);

		// Framing bit
		builder.Add ((byte)1);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildVorbisSetupPacket ()
	{
		using var builder = new BinaryDataBuilder ();

		// Packet type 5 (setup)
		builder.Add ((byte)5);

		// "vorbis" magic
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("vorbis"));

		// Minimal codebook data (just enough to be recognized)
		// In real files this would be much larger
		builder.AddZeros (10);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildOggPageWithNonVorbisData ()
	{
		// Build a valid Ogg page but with non-Vorbis data
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		return BuildOggPage (
			flags: OggPageFlags.BeginOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 0,
			data: data);
	}

	static byte[] BuildOggVorbisFileWithInvalidFramingBit ()
	{
		using var builder = new BinaryDataBuilder ();

		// Build Vorbis identification header
		var identPacket = BuildVorbisIdentificationPacket ();

		// Build comment packet with INVALID framing bit (0 instead of 1)
		var comment = new VorbisComment ("TagLibSharp2");
		var commentData = comment.Render ().ToArray ();

		// Wrap in Vorbis header format but with wrong framing bit
		using var commentBuilder = new BinaryDataBuilder ();
		commentBuilder.Add ((byte)3); // Packet type 3 (comment)
		commentBuilder.Add (System.Text.Encoding.ASCII.GetBytes ("vorbis"));
		commentBuilder.Add (commentData);
		commentBuilder.Add ((byte)0); // INVALID framing bit (should be 1)
		var commentPacket = commentBuilder.ToBinaryData ().ToArray ();

		// Minimal setup header
		var setupPacket = BuildVorbisSetupPacket ();

		// Page 1: BOS with identification
		var page1 = BuildOggPage (
			flags: OggPageFlags.BeginOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 0,
			data: identPacket);
		builder.Add (page1);

		// Page 2: Comment with invalid framing
		var page2 = BuildOggPage (
			flags: OggPageFlags.None,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 1,
			data: commentPacket);
		builder.Add (page2);

		// Page 3: Setup + EOS
		var page3 = BuildOggPage (
			flags: OggPageFlags.EndOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 2,
			data: setupPacket);
		builder.Add (page3);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildOggVorbisFileWithMultiPageComment ()
	{
		using var builder = new BinaryDataBuilder ();

		// Build Vorbis identification header
		var identPacket = BuildVorbisIdentificationPacket ();

		// Build a very large comment that will span multiple pages
		// One Ogg page can hold max 255 segments * 255 bytes = ~65KB
		// We'll create a field with > 130KB of data to span 3+ pages
		var comment = new VorbisComment ("TagLibSharp2");
		comment.Title = "Multi-Page Title";
		// Create a 70KB+ field value
		var longValue = new string ('X', 70000);
		comment.SetValue ("LONGFIELD", longValue);
		var commentData = comment.Render ().ToArray ();

		// Wrap comment in Vorbis header format
		var commentPacket = BuildVorbisCommentPacket (commentData);

		// Build minimal setup header
		var setupPacket = BuildVorbisSetupPacket ();

		// Page 1: BOS with identification header
		var page1 = BuildOggPage (
			flags: OggPageFlags.BeginOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: 0,
			data: identPacket);
		builder.Add (page1);

		// Split comment packet across multiple pages
		// Each page can hold max 255 * 255 = 65025 bytes of data
		const int maxPageData = 255 * 255;
		var commentOffset = 0;
		var sequenceNum = 1u;

		while (commentOffset < commentPacket.Length) {
			var remaining = commentPacket.Length - commentOffset;
			var pageDataLen = Math.Min (remaining, maxPageData);
			var pageData = new byte[pageDataLen];
			Array.Copy (commentPacket, commentOffset, pageData, 0, pageDataLen);

			var flags = OggPageFlags.None;
			if (commentOffset > 0)
				flags |= OggPageFlags.Continuation;

			var page = BuildOggPage (
				flags: flags,
				granulePosition: 0,
				serialNumber: 1,
				sequenceNumber: sequenceNum,
				data: pageData);
			builder.Add (page);

			commentOffset += pageDataLen;
			sequenceNum++;
		}

		// Final page: Setup header and EOS
		var finalPage = BuildOggPage (
			flags: OggPageFlags.EndOfStream,
			granulePosition: 0,
			serialNumber: 1,
			sequenceNumber: sequenceNum,
			data: setupPacket);
		builder.Add (finalPage);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildOggPage (OggPageFlags flags, ulong granulePosition, uint serialNumber,
		uint sequenceNumber, byte[] data)
	{
		using var builder = new BinaryDataBuilder ();

		// Magic: "OggS"
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("OggS"));

		// Version: 0
		builder.Add ((byte)0);

		// Flags
		builder.Add ((byte)flags);

		// Granule position (8 bytes LE)
		builder.Add (BitConverter.GetBytes (granulePosition));

		// Serial number (4 bytes LE)
		builder.Add (BitConverter.GetBytes (serialNumber));

		// Sequence number (4 bytes LE)
		builder.Add (BitConverter.GetBytes (sequenceNumber));

		// CRC (4 bytes) - set to 0 for testing
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 });

		// Build segment table
		var segments = new List<byte> ();
		var remaining = data.Length;
		while (remaining > 0) {
			var segSize = Math.Min (remaining, 255);
			segments.Add ((byte)segSize);
			remaining -= segSize;
		}

		// Segment count
		builder.Add ((byte)segments.Count);

		// Segment table
		foreach (var seg in segments)
			builder.Add (seg);

		// Data
		builder.Add (data);

		return builder.ToBinaryData ().ToArray ();
	}

	#endregion
}
