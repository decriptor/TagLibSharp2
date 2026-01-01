// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Tests for MP4 box parsing.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4BoxTests
{
	[TestMethod]
	public void ParseBasicBox_ValidBoxHeader_ParsesSizeAndType ()
	{
		var data = TestBuilders.Mp4.CreateBox ("ftyp", new byte[] { 0x6D, 0x34, 0x61, 0x20 }); // "m4a "

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("ftyp", result.Box!.Type);
		Assert.AreEqual (12, result.Box.TotalSize); // 8 (header) + 4 (data)
	}

	[TestMethod]
	public void ParseBasicBox_DataTooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00 }; // Less than 8 bytes

		var result = Mp4Box.Parse (data);

		Assert.IsFalse (result.IsSuccess);
		// Error details are optional at this stub stage
	}

	[TestMethod]
	public void ParseExtendedSizeBox_64BitSize_ParsesCorrectly ()
	{
		// Extended size: size=1 in header, actual size in next 8 bytes
		var data = TestBuilders.Mp4.CreateExtendedSizeBox ("mdat", new byte[100]);

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("mdat", result.Box!.Type);
		Assert.IsTrue (result.Box.TotalSize > uint.MaxValue || result.Box.UsesExtendedSize);
	}

	[TestMethod]
	public void ParseFullBox_VersionAndFlags_ParsesCorrectly ()
	{
		// FullBox has 4 extra bytes: version (1 byte) + flags (3 bytes)
		// This test documents expected behavior - Mp4FullBox would expose Version/Flags
		var boxData = new byte[] { 0x00, 0x00, 0x00, 0x00 }; // version=0, flags=0
		var data = TestBuilders.Mp4.CreateBox ("hdlr", boxData);

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("hdlr", result.Box!.Type);
		// Version and Flags are parsed by Mp4BoxParser.ParseFullBox, not Mp4Box.Parse
		// The data is available in Box.Data for further parsing
		Assert.AreEqual (4, result.Box.Data.Length);
	}

	[TestMethod]
	public void ParseBox_SizeZero_ExtendsToEndOfFile ()
	{
		// size=0 means "extends to EOF"
		var data = TestBuilders.Mp4.CreateBoxWithSizeZero ("mdat", new byte[1000]);

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("mdat", result.Box!.Type);
		// When size=0, TotalSize reflects actual consumed bytes
	}

	[TestMethod]
	public void ParseBox_InvalidSize_ReturnsFailure ()
	{
		// Size less than 8 (header size) is invalid
		var data = new byte[16];
		data[0] = 0x00;
		data[1] = 0x00;
		data[2] = 0x00;
		data[3] = 0x04; // Size = 4, but header is 8 bytes
		data[4] = (byte)'f';
		data[5] = (byte)'t';
		data[6] = (byte)'y';
		data[7] = (byte)'p';

		var result = Mp4Box.Parse (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ParseContainerBox_WithChildren_ParsesHierarchy ()
	{
		// Create moov container with child boxes
		var udtaData = TestBuilders.Mp4.CreateBox ("udta", new byte[4]);
		var moovData = TestBuilders.Mp4.CreateBox ("moov", udtaData);

		var result = Mp4Box.Parse (moovData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("moov", result.Box!.Type);
		Assert.IsTrue (result.Box.IsContainer);
		Assert.HasCount (1, result.Box.Children);
		Assert.AreEqual ("udta", result.Box.Children[0].Type);
	}

	[TestMethod]
	public void NavigateBoxHierarchy_NestedBoxes_FindsCorrectPath ()
	{
		// moov -> udta -> meta -> ilst
		// meta is a FullBox with version(1) + flags(3) prefix
		var ilstData = TestBuilders.Mp4.CreateBox ("ilst", new byte[4]);
		var metaContent = new byte[4 + ilstData.Length]; // version+flags + ilst
		Array.Copy (ilstData, 0, metaContent, 4, ilstData.Length);
		var metaData = TestBuilders.Mp4.CreateBox ("meta", metaContent);
		var udtaData = TestBuilders.Mp4.CreateBox ("udta", metaData);
		var moovData = TestBuilders.Mp4.CreateBox ("moov", udtaData);

		var result = Mp4Box.Parse (moovData);

		Assert.IsTrue (result.IsSuccess);
		var moov = result.Box!;
		var udta = moov.FindChild ("udta");
		Assert.IsNotNull (udta);
		var meta = udta!.FindChild ("meta");
		Assert.IsNotNull (meta);
		var ilst = meta!.FindChild ("ilst");
		Assert.IsNotNull (ilst);
	}

	[TestMethod]
	public void ParseBox_UnknownType_ParsesAnyway ()
	{
		// Should parse boxes with unknown types
		var data = TestBuilders.Mp4.CreateBox ("UNKN", new byte[] { 0x01, 0x02, 0x03 });

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("UNKN", result.Box!.Type);
	}

	[TestMethod]
	public void ParseBox_EmptyBox_ParsesCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateBox ("free", Array.Empty<byte> ());

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("free", result.Box!.Type);
		Assert.AreEqual (8, result.Box.TotalSize); // Just header
	}

	[TestMethod]
	public void ParseMultipleBoxes_InSequence_ParsesAllBoxes ()
	{
		var box1 = TestBuilders.Mp4.CreateBox ("ftyp", new byte[4]);
		var box2 = TestBuilders.Mp4.CreateBox ("free", new byte[8]);
		var combined = new byte[box1.Length + box2.Length];
		Array.Copy (box1, 0, combined, 0, box1.Length);
		Array.Copy (box2, 0, combined, box1.Length, box2.Length);

		var result1 = Mp4Box.Parse (combined);
		Assert.IsTrue (result1.IsSuccess);
		Assert.AreEqual ("ftyp", result1.Box!.Type);

		var offset = (int)result1.Box.TotalSize;
		var result2 = Mp4Box.Parse (combined.AsSpan (offset));
		Assert.IsTrue (result2.IsSuccess);
		Assert.AreEqual ("free", result2.Box!.Type);
	}

	[TestMethod]
	public void BoxType_FourCharacterCode_IsReadable ()
	{
		var data = TestBuilders.Mp4.CreateBox ("moov", new byte[4]);

		var result = Mp4Box.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Box!.Type.Length);
		Assert.AreEqual ("moov", result.Box.Type);
	}

	[TestMethod]
	public void ParseBox_WithPadding_IgnoresPadding ()
	{
		var boxData = TestBuilders.Mp4.CreateBox ("skip", new byte[16]);
		var withPadding = new byte[boxData.Length + 100];
		Array.Copy (boxData, withPadding, boxData.Length);

		var result = Mp4Box.Parse (withPadding);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("skip", result.Box!.Type);
		Assert.AreEqual (24, result.Box.TotalSize); // 8 + 16
	}
}
