// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class FlacMetadataBlockHeaderTests
{
	[TestMethod]
	public void Read_StreamInfo_NotLast_ParsesCorrectly ()
	{
		// STREAMINFO block (type 0), not last, size = 34 (0x000022)
		var data = new byte[] { 0x00, 0x00, 0x00, 0x22 };

		var result = FlacMetadataBlockHeader.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.Header.IsLast);
		Assert.AreEqual (FlacBlockType.StreamInfo, result.Header.BlockType);
		Assert.AreEqual (34, result.Header.DataLength);
	}

	[TestMethod]
	public void Read_VorbisComment_Last_ParsesCorrectly ()
	{
		// VORBIS_COMMENT block (type 4), last block, size = 256 (0x000100)
		// IsLast flag is bit 7 of first byte, so 0x84 = 0x80 | 0x04
		var data = new byte[] { 0x84, 0x00, 0x01, 0x00 };

		var result = FlacMetadataBlockHeader.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.IsLast);
		Assert.AreEqual (FlacBlockType.VorbisComment, result.Header.BlockType);
		Assert.AreEqual (256, result.Header.DataLength);
	}

	[TestMethod]
	public void Read_Picture_ParsesCorrectly ()
	{
		// PICTURE block (type 6), not last, size = 50000 (0x00C350)
		var data = new byte[] { 0x06, 0x00, 0xC3, 0x50 };

		var result = FlacMetadataBlockHeader.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.Header.IsLast);
		Assert.AreEqual (FlacBlockType.Picture, result.Header.BlockType);
		Assert.AreEqual (50000, result.Header.DataLength);
	}

	[TestMethod]
	public void Read_Padding_ParsesCorrectly ()
	{
		// PADDING block (type 1), last block, size = 1024 (0x000400)
		var data = new byte[] { 0x81, 0x00, 0x04, 0x00 };

		var result = FlacMetadataBlockHeader.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.IsLast);
		Assert.AreEqual (FlacBlockType.Padding, result.Header.BlockType);
		Assert.AreEqual (1024, result.Header.DataLength);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00 }; // Only 3 bytes

		var result = FlacMetadataBlockHeader.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Render_StreamInfo_ProducesCorrectBytes ()
	{
		var header = new FlacMetadataBlockHeader (false, FlacBlockType.StreamInfo, 34);

		var rendered = header.Render ();

		Assert.AreEqual (4, rendered.Length);
		var bytes = rendered.ToArray ();
		Assert.AreEqual ((byte)0x00, bytes[0]); // Not last, type 0
		Assert.AreEqual ((byte)0x00, bytes[1]);
		Assert.AreEqual ((byte)0x00, bytes[2]);
		Assert.AreEqual ((byte)0x22, bytes[3]); // Size 34
	}

	[TestMethod]
	public void Render_VorbisComment_Last_ProducesCorrectBytes ()
	{
		var header = new FlacMetadataBlockHeader (true, FlacBlockType.VorbisComment, 256);

		var rendered = header.Render ();

		var bytes = rendered.ToArray ();
		Assert.AreEqual ((byte)0x84, bytes[0]); // Last + type 4
		Assert.AreEqual ((byte)0x00, bytes[1]);
		Assert.AreEqual ((byte)0x01, bytes[2]);
		Assert.AreEqual ((byte)0x00, bytes[3]); // Size 256
	}

	[TestMethod]
	public void Roundtrip_PreservesValues ()
	{
		var original = new FlacMetadataBlockHeader (true, FlacBlockType.Picture, 12345);

		var rendered = original.Render ();
		var result = FlacMetadataBlockHeader.Read (rendered.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.IsLast, result.Header.IsLast);
		Assert.AreEqual (original.BlockType, result.Header.BlockType);
		Assert.AreEqual (original.DataLength, result.Header.DataLength);
	}

	[TestMethod]
	public void Constructor_MaxDataLength_Works ()
	{
		// Maximum data length is 24 bits = 16777215 (0xFFFFFF)
		var header = new FlacMetadataBlockHeader (false, FlacBlockType.Padding, 16777215);

		Assert.AreEqual (16777215, header.DataLength);
	}
}
