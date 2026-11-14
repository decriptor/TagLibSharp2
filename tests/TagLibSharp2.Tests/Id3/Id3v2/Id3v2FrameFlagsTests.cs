// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Text;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2FrameFlagsTests
{
	// ===========================================================================
	// ID3v2.4 Frame Flags - Data Length Indicator Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V24FrameWithDataLengthIndicator_ParsesCorrectly ()
	{
		// ID3v2.4 data length indicator: 4-byte syncsafe integer prepended to frame data
		// Flag is in byte 9, bit 0
		var frameData = CreateFrameWithDataLengthIndicator (
			TestConstants.FrameIds.Title,
			"Test Title",
			TestConstants.Id3v2.Version4);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess, "Should parse successfully");
		Assert.AreEqual ("Test Title", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V24FrameWithDataLengthIndicator_SkipsDataLengthBytes ()
	{
		// Verify the 4-byte data length is properly skipped when reading frame content
		var frameData = CreateFrameWithDataLengthIndicator (
			TestConstants.FrameIds.Artist,
			"Test Artist",
			TestConstants.Id3v2.Version4);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Artist", result.Tag!.Artist);
	}

	// ===========================================================================
	// ID3v2.4 Frame Flags - Per-Frame Unsynchronization Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V24FrameWithUnsyncFlag_DecodesCorrectly ()
	{
		// ID3v2.4 per-frame unsync: flag in byte 9, bit 1
		// 0xFF 0x00 sequences in frame data should be converted to 0xFF
		var frameData = CreateFrameWithPerFrameUnsync (
			TestConstants.FrameIds.Title,
			"Test\xFFValue",
			TestConstants.Id3v2.Version4);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test\xFFValue", result.Tag!.Title);
	}

	// ===========================================================================
	// ID3v2.3/2.4 Frame Flags - Compression Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V24FrameWithCompression_DecompressesCorrectly ()
	{
		// ID3v2.4 compression: flag in byte 9, bit 3
		// Requires data length indicator (bit 0) to also be set
		var frameData = CreateCompressedFrame (
			TestConstants.FrameIds.Title,
			"This is a test title that will be compressed using zlib deflate",
			TestConstants.Id3v2.Version4);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("This is a test title that will be compressed using zlib deflate", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V23FrameWithCompression_DecompressesCorrectly ()
	{
		// ID3v2.3 compression: flag in byte 9, bit 7
		// In v2.3, compressed frames have 4-byte decompressed size before data
		var frameData = CreateCompressedFrame (
			TestConstants.FrameIds.Title,
			"Compressed title text for version 2.3",
			TestConstants.Id3v2.Version3);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version3, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Compressed title text for version 2.3", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_DeclaredSizeWithinCap_KeepsIncompressiblePayload ()
	{
		// Compressed length * 1000 does not fit in an int (about 2.2MB). The declared
		// size stays under 16MB and matches the inflated payload, so the frame is kept.
		const int TextLength = 2_200_000;
		var chars = new char[TextLength];
		Array.Fill (chars, 'B');
		chars[0] = 'K';
		chars[1] = 'E';
		chars[2] = 'E';
		chars[3] = 'P';
		chars[TextLength - 3] = 'E';
		chars[TextLength - 2] = 'N';
		chars[TextLength - 1] = 'D';
		var text = new string (chars);
		var frameContent = new byte[1 + TextLength];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Encoding.Latin1.GetBytes (text).CopyTo (frameContent, 1);

		var compressed = CompressZlib (frameContent, CompressionLevel.NoCompression);
		Assert.IsTrue (compressed.Length > int.MaxValue / 1000, "Fixture must overflow an int multiply by 1000.");
		Assert.IsTrue (frameContent.Length < 16 * 1024 * 1024);

		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			compressed,
			frameContent.Length,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.Title);
		Assert.AreEqual (TextLength, result.Tag.Title.Length);
		Assert.StartsWith ("KEEP", result.Tag.Title);
		Assert.EndsWith ("END", result.Tag.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_ExpansionPastDeclaredSize_DoesNotReturnOversizedText ()
	{
		const int Declared = 64;
		const int Extra = 4 * 1024;
		var frameContent = new byte[Declared + Extra];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		for (var i = 1; i < frameContent.Length; i++)
			frameContent[i] = (byte)'A';

		var compressed = CompressZlib (frameContent, CompressionLevel.Optimal);
		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			compressed,
			Declared,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_ExpansionAboveCap_DoesNotReturnExpandedTitle ()
	{
		const int Declared = 16 * 1024 * 1024 + 1;
		var payload = CompressZlib (new byte[] { TestConstants.Id3v2.EncodingLatin1, (byte)'A' });
		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			payload,
			Declared,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_CapRejectsIncompressibleDeclaredSize ()
	{
		// About 17 KB stored, declared as 16 MiB + 1. The expansion ratio stays
		// under 1100, so only the cap can reject it.
		const int Stored = 17 * 1024;
		const int Declared = 16 * 1024 * 1024 + 1;
		var frameContent = new byte[Stored];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		for (var i = 1; i < frameContent.Length; i++)
			frameContent[i] = (byte)(i * 17);

		var compressed = CompressZlib (frameContent, CompressionLevel.NoCompression);
		Assert.IsTrue (compressed.Length > 16 * 1024, "Payload must stay large enough that the ratio allows it.");
		Assert.IsTrue ((long)Declared <= (long)compressed.Length * 1100L);
		Assert.IsTrue (Declared > 16 * 1024 * 1024);

		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			compressed,
			Declared,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_RatioRejectsWhenCapWouldNot ()
	{
		var frameContent = new byte[] { TestConstants.Id3v2.EncodingLatin1, (byte)'A' };
		var compressed = CompressZlib (frameContent);
		var declared = compressed.Length * 1100 + 1;
		Assert.IsTrue (declared <= 16 * 1024 * 1024);

		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			compressed,
			declared,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_ExpansionBetween1000And1100_KeepsTitle ()
	{
		// Deflate of 800 KB of one byte is just over 1000:1 and under 1100:1, and under 16 MiB.
		const int TextLength = 800_000;
		var frameContent = new byte[1 + TextLength];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Array.Fill (frameContent, (byte)'A', 1, TextLength);

		var compressed = CompressZlib (frameContent, CompressionLevel.Optimal);
		Assert.IsTrue ((long)frameContent.Length > (long)compressed.Length * 1000L);
		Assert.IsTrue ((long)frameContent.Length <= (long)compressed.Length * 1100L);
		Assert.IsTrue (frameContent.Length <= 16 * 1024 * 1024);

		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			compressed,
			frameContent.Length,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Tag!.Title);
		Assert.AreEqual (TextLength, result.Tag.Title.Length);
		Assert.AreEqual ('A', result.Tag.Title[0]);
		Assert.AreEqual ('A', result.Tag.Title[TextLength - 1]);
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version4, 0x09)]
	[DataRow (TestConstants.Id3v2.Version3, 0x80)]
	public void Read_CompressedFrame_ShorterThanSizeField_TitleIsNull (int version, int formatFlags)
	{
		// Three bytes cannot hold the 4-byte decompressed-size field.
		var content = new byte[] { TestConstants.Id3v2.EncodingLatin1, (byte)'H', (byte)'i' };
		var frame = CreateRawFrame (TestConstants.FrameIds.Title, content, (byte)version, (byte)formatFlags);
		var data = CreateTagWithFrame ((byte)version, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version4, 0x04)]
	[DataRow (TestConstants.Id3v2.Version3, 0x40)]
	public void Read_EncryptedFrame_TitleIsNull (int version, int formatFlags)
	{
		var text = Encoding.Latin1.GetBytes ("Secret");
		var content = new byte[1 + text.Length];
		content[0] = TestConstants.Id3v2.EncodingLatin1;
		text.CopyTo (content, 1);
		var frame = CreateRawFrame (TestConstants.FrameIds.Title, content, (byte)version, (byte)formatFlags);
		var data = CreateTagWithFrame ((byte)version, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	[TestMethod]
	public void Read_CompressedFrame_HugeDataLengthIndicator_StaysUnderAMegabyte ()
	{
		// Syncsafe 0x0FFFFFFF over a tiny zlib payload must not allocate hundreds of megabytes.
		const int Declared = 0x0FFFFFFF;
		var payload = CompressZlib (new byte[] { TestConstants.Id3v2.EncodingLatin1, (byte)'A' });
		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			payload,
			Declared,
			TestConstants.Id3v2.Version4);
		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frame);

		Id3v2Tag.Read (data);
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = Id3v2Tag.Read (data);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
		Assert.IsTrue (allocated < 1024 * 1024, $"Read allocated {allocated} bytes");
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version4)]
	[DataRow (TestConstants.Id3v2.Version3)]
	public void Read_CompressedFrame_NotZlib_DoesNotParsePayloadAsText (int version)
	{
		var text = Encoding.Latin1.GetBytes ("Not zlib text");
		var payload = new byte[1 + text.Length];
		payload[0] = TestConstants.Id3v2.EncodingLatin1;
		text.CopyTo (payload, 1);

		var frame = CreateCompressedFrameWithDeclaredSize (
			TestConstants.FrameIds.Title,
			payload,
			payload.Length,
			(byte)version);
		var data = CreateTagWithFrame ((byte)version, frame);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.Tag!.Title);
	}

	// ===========================================================================
	// ID3v2.3/2.4 Frame Flags - Grouping Identity Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V24FrameWithGroupingIdentity_ParsesCorrectly ()
	{
		// ID3v2.4 grouping: flag in byte 9, bit 6
		// 1-byte group identifier prepended to frame data
		var frameData = CreateFrameWithGroupingIdentity (
			TestConstants.FrameIds.Title,
			"Grouped Title",
			groupId: 0x42,
			TestConstants.Id3v2.Version4);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Grouped Title", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_V23FrameWithGroupingIdentity_ParsesCorrectly ()
	{
		// ID3v2.3 grouping: flag in byte 9, bit 5
		var frameData = CreateFrameWithGroupingIdentity (
			TestConstants.FrameIds.Artist,
			"Grouped Artist",
			groupId: 0x01,
			TestConstants.Id3v2.Version3);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version3, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Grouped Artist", result.Tag!.Artist);
	}

	// ===========================================================================
	// Combined Flags Tests
	// ===========================================================================

	[TestMethod]
	public void Read_V24FrameWithDataLengthAndGrouping_ParsesCorrectly ()
	{
		// Both data length indicator and grouping identity set
		var frameData = CreateFrameWithMultipleFlags (
			TestConstants.FrameIds.Album,
			"Test Album",
			TestConstants.Id3v2.Version4,
			hasDataLengthIndicator: true,
			hasGroupingIdentity: true,
			groupId: 0x10);

		var data = CreateTagWithFrame (TestConstants.Id3v2.Version4, frameData);

		var result = Id3v2Tag.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Album", result.Tag!.Album);
	}

	// ===========================================================================
	// Helper Methods
	// ===========================================================================

	static byte[] CreateRawFrame (string frameId, byte[] content, byte version, byte formatFlags)
	{
		var frame = new byte[TestConstants.Id3v2.FrameHeaderSize + content.Length];
		Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);
		var size = content.Length;
		if (version == TestConstants.Id3v2.Version4) {
			frame[4] = (byte)((size >> 21) & 0x7F);
			frame[5] = (byte)((size >> 14) & 0x7F);
			frame[6] = (byte)((size >> 7) & 0x7F);
			frame[7] = (byte)(size & 0x7F);
		} else {
			frame[4] = (byte)((size >> 24) & 0xFF);
			frame[5] = (byte)((size >> 16) & 0xFF);
			frame[6] = (byte)((size >> 8) & 0xFF);
			frame[7] = (byte)(size & 0xFF);
		}

		frame[8] = 0x00;
		frame[9] = formatFlags;
		content.CopyTo (frame, TestConstants.Id3v2.FrameHeaderSize);
		return frame;
	}

	static byte[] CreateTagWithFrame (byte version, byte[] frame)
	{
		var header = TestBuilders.Id3v2.CreateHeader (version, (uint)frame.Length);
		var data = new byte[header.Length + frame.Length];
		header.CopyTo (data, 0);
		frame.CopyTo (data, header.Length);
		return data;
	}

	static byte[] CreateFrameWithDataLengthIndicator (string frameId, string text, byte version)
	{
		var textBytes = Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Data length indicator: 4-byte syncsafe integer representing original content size
		var dataLengthIndicator = new byte[4];
		var contentSize = frameContent.Length;
		dataLengthIndicator[0] = (byte)((contentSize >> 21) & 0x7F);
		dataLengthIndicator[1] = (byte)((contentSize >> 14) & 0x7F);
		dataLengthIndicator[2] = (byte)((contentSize >> 7) & 0x7F);
		dataLengthIndicator[3] = (byte)(contentSize & 0x7F);

		// Frame = header + data length indicator + content
		var totalContentSize = dataLengthIndicator.Length + frameContent.Length;
		var frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];

		// Frame ID
		Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);

		// Size (syncsafe for v2.4)
		frame[4] = (byte)((totalContentSize >> 21) & 0x7F);
		frame[5] = (byte)((totalContentSize >> 14) & 0x7F);
		frame[6] = (byte)((totalContentSize >> 7) & 0x7F);
		frame[7] = (byte)(totalContentSize & 0x7F);

		// Flags: data length indicator flag (bit 0 of byte 9)
		frame[8] = 0x00;
		frame[9] = 0x01; // Data length indicator flag

		// Data length indicator + content
		Array.Copy (dataLengthIndicator, 0, frame, TestConstants.Id3v2.FrameHeaderSize, dataLengthIndicator.Length);
		Array.Copy (frameContent, 0, frame, TestConstants.Id3v2.FrameHeaderSize + dataLengthIndicator.Length, frameContent.Length);

		return frame;
	}

	static byte[] CreateFrameWithPerFrameUnsync (string frameId, string text, byte version)
	{
		var textBytes = Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Apply unsynchronization: insert 0x00 after each 0xFF
		var unsyncContent = ApplyUnsynchronization (frameContent);

		var frame = new byte[TestConstants.Id3v2.FrameHeaderSize + unsyncContent.Length];

		// Frame ID
		Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);

		// Size (syncsafe for v2.4)
		frame[4] = (byte)((unsyncContent.Length >> 21) & 0x7F);
		frame[5] = (byte)((unsyncContent.Length >> 14) & 0x7F);
		frame[6] = (byte)((unsyncContent.Length >> 7) & 0x7F);
		frame[7] = (byte)(unsyncContent.Length & 0x7F);

		// Flags: unsynchronization flag (bit 1 of byte 9)
		frame[8] = 0x00;
		frame[9] = 0x02; // Unsynchronization flag

		Array.Copy (unsyncContent, 0, frame, TestConstants.Id3v2.FrameHeaderSize, unsyncContent.Length);

		return frame;
	}

	static byte[] CreateCompressedFrame (string frameId, string text, byte version)
	{
		var textBytes = Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Compress using zlib deflate
		var compressedData = CompressZlib (frameContent);

		byte[] frame;
		if (version == TestConstants.Id3v2.Version4) {
			// v2.4: compression requires data length indicator
			// Data length indicator (4-byte syncsafe) + compressed data
			var dataLengthIndicator = new byte[4];
			var originalSize = frameContent.Length;
			dataLengthIndicator[0] = (byte)((originalSize >> 21) & 0x7F);
			dataLengthIndicator[1] = (byte)((originalSize >> 14) & 0x7F);
			dataLengthIndicator[2] = (byte)((originalSize >> 7) & 0x7F);
			dataLengthIndicator[3] = (byte)(originalSize & 0x7F);

			var totalContentSize = dataLengthIndicator.Length + compressedData.Length;
			frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];

			Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);
			frame[4] = (byte)((totalContentSize >> 21) & 0x7F);
			frame[5] = (byte)((totalContentSize >> 14) & 0x7F);
			frame[6] = (byte)((totalContentSize >> 7) & 0x7F);
			frame[7] = (byte)(totalContentSize & 0x7F);

			// Flags: compression (bit 3) + data length indicator (bit 0)
			frame[8] = 0x00;
			frame[9] = 0x09; // 0x08 (compression) | 0x01 (data length indicator)

			Array.Copy (dataLengthIndicator, 0, frame, TestConstants.Id3v2.FrameHeaderSize, dataLengthIndicator.Length);
			Array.Copy (compressedData, 0, frame, TestConstants.Id3v2.FrameHeaderSize + dataLengthIndicator.Length, compressedData.Length);
		} else {
			// v2.3: 4-byte big-endian decompressed size + compressed data
			var decompressedSize = new byte[4];
			var originalSize = frameContent.Length;
			decompressedSize[0] = (byte)((originalSize >> 24) & 0xFF);
			decompressedSize[1] = (byte)((originalSize >> 16) & 0xFF);
			decompressedSize[2] = (byte)((originalSize >> 8) & 0xFF);
			decompressedSize[3] = (byte)(originalSize & 0xFF);

			var totalContentSize = decompressedSize.Length + compressedData.Length;
			frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];

			Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);
			frame[4] = (byte)((totalContentSize >> 24) & 0xFF);
			frame[5] = (byte)((totalContentSize >> 16) & 0xFF);
			frame[6] = (byte)((totalContentSize >> 8) & 0xFF);
			frame[7] = (byte)(totalContentSize & 0xFF);

			// Flags: compression (bit 7 of byte 9 for v2.3)
			frame[8] = 0x00;
			frame[9] = 0x80; // Compression flag for v2.3

			Array.Copy (decompressedSize, 0, frame, TestConstants.Id3v2.FrameHeaderSize, decompressedSize.Length);
			Array.Copy (compressedData, 0, frame, TestConstants.Id3v2.FrameHeaderSize + decompressedSize.Length, compressedData.Length);
		}

		return frame;
	}

	static byte[] CreateFrameWithGroupingIdentity (string frameId, string text, byte groupId, byte version)
	{
		var textBytes = Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Group identifier (1 byte) + content
		var totalContentSize = 1 + frameContent.Length;
		var frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];

		Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);

		if (version == TestConstants.Id3v2.Version4) {
			frame[4] = (byte)((totalContentSize >> 21) & 0x7F);
			frame[5] = (byte)((totalContentSize >> 14) & 0x7F);
			frame[6] = (byte)((totalContentSize >> 7) & 0x7F);
			frame[7] = (byte)(totalContentSize & 0x7F);

			// Flags: grouping identity (bit 6 of byte 9 for v2.4)
			frame[8] = 0x00;
			frame[9] = 0x40;
		} else {
			frame[4] = (byte)((totalContentSize >> 24) & 0xFF);
			frame[5] = (byte)((totalContentSize >> 16) & 0xFF);
			frame[6] = (byte)((totalContentSize >> 8) & 0xFF);
			frame[7] = (byte)(totalContentSize & 0xFF);

			// Flags: grouping identity (bit 5 of byte 9 for v2.3)
			frame[8] = 0x00;
			frame[9] = 0x20;
		}

		// Group identifier + content
		frame[TestConstants.Id3v2.FrameHeaderSize] = groupId;
		Array.Copy (frameContent, 0, frame, TestConstants.Id3v2.FrameHeaderSize + 1, frameContent.Length);

		return frame;
	}

	static byte[] CreateFrameWithMultipleFlags (
		string frameId,
		string text,
		byte version,
		bool hasDataLengthIndicator,
		bool hasGroupingIdentity,
		byte groupId = 0)
	{
		var textBytes = Encoding.Latin1.GetBytes (text);
		var frameContent = new byte[1 + textBytes.Length];
		frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
		Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

		// Build extra data before content
		var extraData = new List<byte> ();

		if (hasGroupingIdentity)
			extraData.Add (groupId);

		if (hasDataLengthIndicator) {
			var contentSize = frameContent.Length;
			extraData.Add ((byte)((contentSize >> 21) & 0x7F));
			extraData.Add ((byte)((contentSize >> 14) & 0x7F));
			extraData.Add ((byte)((contentSize >> 7) & 0x7F));
			extraData.Add ((byte)(contentSize & 0x7F));
		}

		var totalContentSize = extraData.Count + frameContent.Length;
		var frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];

		Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);

		// Size (syncsafe for v2.4)
		frame[4] = (byte)((totalContentSize >> 21) & 0x7F);
		frame[5] = (byte)((totalContentSize >> 14) & 0x7F);
		frame[6] = (byte)((totalContentSize >> 7) & 0x7F);
		frame[7] = (byte)(totalContentSize & 0x7F);

		// Build flags byte
		byte flags = 0;
		if (hasDataLengthIndicator)
			flags |= 0x01; // Bit 0
		if (hasGroupingIdentity)
			flags |= 0x40; // Bit 6

		frame[8] = 0x00;
		frame[9] = flags;

		// Copy extra data and content
		var offset = TestConstants.Id3v2.FrameHeaderSize;
		foreach (var b in extraData)
			frame[offset++] = b;
		Array.Copy (frameContent, 0, frame, offset, frameContent.Length);

		return frame;
	}

	static byte[] ApplyUnsynchronization (byte[] data)
	{
		var count = 0;
		foreach (var b in data) {
			if (b == 0xFF)
				count++;
		}

		if (count == 0)
			return data;

		var output = new byte[data.Length + count];
		var outIndex = 0;
		foreach (var b in data) {
			output[outIndex++] = b;
			if (b == 0xFF)
				output[outIndex++] = 0x00;
		}

		return output;
	}

	static byte[] CreateCompressedFrameWithDeclaredSize (string frameId, byte[] compressedData, int declaredSize, byte version)
	{
		byte[] frame;
		if (version == TestConstants.Id3v2.Version4) {
			var dataLengthIndicator = new byte[4];
			dataLengthIndicator[0] = (byte)((declaredSize >> 21) & 0x7F);
			dataLengthIndicator[1] = (byte)((declaredSize >> 14) & 0x7F);
			dataLengthIndicator[2] = (byte)((declaredSize >> 7) & 0x7F);
			dataLengthIndicator[3] = (byte)(declaredSize & 0x7F);

			var totalContentSize = dataLengthIndicator.Length + compressedData.Length;
			frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];
			Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);
			frame[4] = (byte)((totalContentSize >> 21) & 0x7F);
			frame[5] = (byte)((totalContentSize >> 14) & 0x7F);
			frame[6] = (byte)((totalContentSize >> 7) & 0x7F);
			frame[7] = (byte)(totalContentSize & 0x7F);
			frame[8] = 0x00;
			frame[9] = 0x09;
			Array.Copy (dataLengthIndicator, 0, frame, TestConstants.Id3v2.FrameHeaderSize, dataLengthIndicator.Length);
			Array.Copy (compressedData, 0, frame, TestConstants.Id3v2.FrameHeaderSize + dataLengthIndicator.Length, compressedData.Length);
		} else {
			var decompressedSize = new byte[4];
			decompressedSize[0] = (byte)((declaredSize >> 24) & 0xFF);
			decompressedSize[1] = (byte)((declaredSize >> 16) & 0xFF);
			decompressedSize[2] = (byte)((declaredSize >> 8) & 0xFF);
			decompressedSize[3] = (byte)(declaredSize & 0xFF);

			var totalContentSize = decompressedSize.Length + compressedData.Length;
			frame = new byte[TestConstants.Id3v2.FrameHeaderSize + totalContentSize];
			Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);
			frame[4] = (byte)((totalContentSize >> 24) & 0xFF);
			frame[5] = (byte)((totalContentSize >> 16) & 0xFF);
			frame[6] = (byte)((totalContentSize >> 8) & 0xFF);
			frame[7] = (byte)(totalContentSize & 0xFF);
			frame[8] = 0x00;
			frame[9] = 0x80;
			Array.Copy (decompressedSize, 0, frame, TestConstants.Id3v2.FrameHeaderSize, decompressedSize.Length);
			Array.Copy (compressedData, 0, frame, TestConstants.Id3v2.FrameHeaderSize + decompressedSize.Length, compressedData.Length);
		}

		return frame;
	}

	static byte[] CompressZlib (byte[] data) => CompressZlib (data, CompressionLevel.Optimal);

	static byte[] CompressZlib (byte[] data, CompressionLevel level)
	{
		using var output = new MemoryStream ();
		// Write zlib header (CMF=0x78, FLG=0x9C for default compression)
		output.WriteByte (0x78);
		output.WriteByte (0x9C);

		using (var deflate = new DeflateStream (output, level, leaveOpen: true))
			deflate.Write (data, 0, data.Length);

		// Calculate Adler-32 checksum
		var adler = CalculateAdler32 (data);
		output.WriteByte ((byte)((adler >> 24) & 0xFF));
		output.WriteByte ((byte)((adler >> 16) & 0xFF));
		output.WriteByte ((byte)((adler >> 8) & 0xFF));
		output.WriteByte ((byte)(adler & 0xFF));

		return output.ToArray ();
	}

	static uint CalculateAdler32 (byte[] data)
	{
		const uint MOD_ADLER = 65521;
		uint a = 1, b = 0;
		foreach (var byteVal in data) {
			a = (a + byteVal) % MOD_ADLER;
			b = (b + a) % MOD_ADLER;
		}
		return (b << 16) | a;
	}
}
