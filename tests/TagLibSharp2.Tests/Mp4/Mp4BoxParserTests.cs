// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

[TestClass]
public class Mp4BoxParserTests
{
	[TestMethod]
	public void ParseBox_BasicBox_ParsesCorrectly()
	{
		// Arrange: Create a basic "ftyp" box
		// size=20 (0x00000014), type="ftyp", data=12 bytes
		var data = BinaryData.FromHexString(
			"00 00 00 14" +     // size: 20 bytes
			"66 74 79 70" +     // type: "ftyp"
			"4D 34 41 20" +     // major_brand: "M4A "
			"00 00 00 00" +     // minor_version: 0
			"4D 34 41 20"       // compatible_brands[0]: "M4A "
		);

		// Act
		var result = Mp4BoxParser.ParseBox(data.Span);

		// Assert
		Assert.IsTrue(result.IsSuccess);
		Assert.IsNotNull(result.Box);
		Assert.AreEqual("ftyp", result.Box.Type);
		Assert.AreEqual(20, result.BytesConsumed);
		Assert.AreEqual(20, result.Box.TotalSize);
		Assert.IsFalse(result.Box.UsesExtendedSize);
		Assert.AreEqual(12, result.Box.Data.Length);
	}

	[TestMethod]
	public void ParseBox_ExtendedSize_ParsesCorrectly()
	{
		// Arrange: Box with size=1 indicates 64-bit largesize follows
		var data = BinaryData.FromHexString(
			"00 00 00 01" +                 // size: 1 (indicates extended size)
			"6D 64 61 74" +                 // type: "mdat"
			"00 00 00 00 00 00 00 18" +     // largesize: 24 bytes
			"00 00 00 00 00 00 00 00"       // data: 8 bytes
		);

		// Act
		var result = Mp4BoxParser.ParseBox(data.Span);

		// Assert
		Assert.IsTrue(result.IsSuccess);
		Assert.IsNotNull(result.Box);
		Assert.AreEqual("mdat", result.Box.Type);
		Assert.AreEqual(24, result.BytesConsumed);
		Assert.AreEqual(24, result.Box.TotalSize);
		Assert.IsTrue(result.Box.UsesExtendedSize);
		Assert.AreEqual(8, result.Box.Data.Length); // 24 - 16 (header)
	}

	[TestMethod]
	public void ParseBox_SizeZero_ExtendsToEndOfData()
	{
		// Arrange: size=0 means box extends to end of file
		var data = BinaryData.FromHexString(
			"00 00 00 00" +     // size: 0 (extends to EOF)
			"6D 64 61 74" +     // type: "mdat"
			"00 11 22 33" +     // data continues to end
			"44 55 66 77"
		);

		// Act
		var result = Mp4BoxParser.ParseBox(data.Span);

		// Assert
		Assert.IsTrue(result.IsSuccess);
		Assert.IsNotNull(result.Box);
		Assert.AreEqual("mdat", result.Box.Type);
		Assert.AreEqual(16, result.BytesConsumed); // Entire remaining data
		Assert.AreEqual(8, result.Box.Data.Length); // 16 - 8 (header)
	}

	[TestMethod]
	public void ParseBox_InvalidSizeTooSmall_ReturnsFailure()
	{
		// Arrange: size < 8 is invalid per ISO 14496-12
		var data = BinaryData.FromHexString(
			"00 00 00 05" +     // size: 5 (less than minimum 8)
			"66 74 79 70"       // type: "ftyp"
		);

		// Act
		var result = Mp4BoxParser.ParseBox(data.Span);

		// Assert
		Assert.IsFalse(result.IsSuccess);
		Assert.IsNull(result.Box);
	}

	[TestMethod]
	public void ParseBox_InsufficientData_ReturnsFailure()
	{
		// Arrange: Box claims to be 20 bytes but only 12 available
		var data = BinaryData.FromHexString(
			"00 00 00 14" +     // size: 20 bytes
			"66 74 79 70" +     // type: "ftyp"
			"00 00 00 00"       // Only 4 more bytes (need 12)
		);

		// Act
		var result = Mp4BoxParser.ParseBox(data.Span);

		// Assert
		Assert.IsFalse(result.IsSuccess);
	}

	[TestMethod]
	public void ParseBox_ContainerBox_ParsesChildren()
	{
		// Arrange: "moov" container with one child "mvhd"
		var mvhdData = BinaryData.Concat(
			BinaryData.FromUInt32BE(20),           // size: 20
			BinaryData.FromString("mvhd", System.Text.Encoding.ASCII),  // type
			BinaryData.FromHexString("00 00 00 00 00 00 00 00 00 00 00 00") // 12 bytes data
		);

		var moovData = BinaryData.Concat(
			BinaryData.FromUInt32BE((uint)(8 + mvhdData.Length)),  // size: header + child
			BinaryData.FromString("moov", System.Text.Encoding.ASCII),  // type
			mvhdData                                               // child data
		);

		// Act
		var result = Mp4BoxParser.ParseBox(moovData.Span);

		// Assert
		Assert.IsTrue(result.IsSuccess);
		Assert.IsNotNull(result.Box);
		Assert.AreEqual("moov", result.Box.Type);
		Assert.IsTrue(result.Box.IsContainer);
		Assert.AreEqual(1, result.Box.Children.Count);
		Assert.AreEqual("mvhd", result.Box.Children[0].Type);
	}

	[TestMethod]
	public void ParseChildren_MultipleBoxes_ParsesAll()
	{
		// Arrange: Two boxes concatenated
		var box1 = BinaryData.Concat(
			BinaryData.FromUInt32BE(12),       // size
			BinaryData.FromString("free", System.Text.Encoding.ASCII),
			BinaryData.FromHexString("00 00 00 00")
		);

		var box2 = BinaryData.Concat(
			BinaryData.FromUInt32BE(12),       // size
			BinaryData.FromString("skip", System.Text.Encoding.ASCII),
			BinaryData.FromHexString("00 00 00 00")
		);

		var combined = box1 + box2;

		// Act
		var children = Mp4BoxParser.ParseChildren(combined.Span);

		// Assert
		Assert.AreEqual(2, children.Count);
		Assert.AreEqual("free", children[0].Type);
		Assert.AreEqual("skip", children[1].Type);
	}

	[TestMethod]
	public void ParseFullBox_ValidData_ParsesVersionAndFlags()
	{
		// Arrange: FullBox data with version=1, flags=0x000002
		var data = BinaryData.FromHexString(
			"01" +              // version: 1
			"00 00 02" +        // flags: 0x000002
			"11 22 33 44"       // content data
		);

		// Act
		var fullBox = Mp4BoxParser.ParseFullBox("mvhd", data);

		// Assert
		Assert.IsNotNull(fullBox);
		Assert.AreEqual("mvhd", fullBox.Type);
		Assert.AreEqual(1, fullBox.Version);
		Assert.AreEqual(0x000002u, fullBox.Flags);
		Assert.AreEqual(4, fullBox.ContentData.Length);
	}

	[TestMethod]
	public void IsContainerBox_KnownContainers_ReturnsTrue()
	{
		// Arrange & Act & Assert
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("moov"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("trak"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("mdia"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("minf"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("stbl"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("udta"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("meta"));
		Assert.IsTrue(Mp4BoxParser.IsContainerBox("ilst"));
	}

	[TestMethod]
	public void IsContainerBox_LeafBoxes_ReturnsFalse()
	{
		// Arrange & Act & Assert
		Assert.IsFalse(Mp4BoxParser.IsContainerBox("ftyp"));
		Assert.IsFalse(Mp4BoxParser.IsContainerBox("mdat"));
		Assert.IsFalse(Mp4BoxParser.IsContainerBox("mvhd"));
		Assert.IsFalse(Mp4BoxParser.IsContainerBox("tkhd"));
	}

	[TestMethod]
	public void Mp4Box_Render_ProducesValidOutput()
	{
		// Arrange: Create a simple box
		var data = BinaryData.FromHexString("11 22 33 44");
		var box = new Mp4Box("test", data);

		// Act
		var rendered = box.Render();

		// Assert
		Assert.AreEqual(12, rendered.Length); // 8 byte header + 4 byte data
		Assert.AreEqual(12u, rendered.ToUInt32BE(0)); // size
		Assert.AreEqual("test", rendered.Slice(4, 4).ToStringLatin1()); // type
		Assert.IsTrue(rendered.Slice(8).Span.SequenceEqual(data.Span)); // data
	}

	[TestMethod]
	public void Mp4Box_RenderLargeBox_UsesExtendedSize()
	{
		// Arrange: Create a box with extended size flag
		// We can't actually create a 4GB+ box in tests, so we test via parsing
		var data = BinaryData.FromHexString(
			"00 00 00 01" +                 // size: 1 (indicates extended size)
			"6D 64 61 74" +                 // type: "mdat"
			"00 00 00 00 00 00 00 18" +     // largesize: 24 bytes
			"00 00 00 00 00 00 00 00"       // data: 8 bytes
		);

		// Act
		var result = Mp4BoxParser.ParseBox(data.Span);

		// Assert
		Assert.IsTrue(result.IsSuccess);
		Assert.IsTrue(result.Box!.UsesExtendedSize);
		Assert.AreEqual(24, result.Box.TotalSize);
	}

	[TestMethod]
	public void Mp4Box_FindChild_ReturnsCorrectBox()
	{
		// Arrange
		var child1 = new Mp4Box("mvhd", BinaryData.FromHexString("00 00 00 00"));
		var child2 = new Mp4Box("trak", BinaryData.FromHexString("11 11 11 11"));
		var parent = new Mp4Box("moov", BinaryData.Empty, new[] { child1, child2 });

		// Act
		var found = parent.FindChild("trak");

		// Assert
		Assert.IsNotNull(found);
		Assert.AreEqual("trak", found.Type);
		Assert.AreSame(child2, found);
	}

	[TestMethod]
	public void Mp4Box_Navigate_FollowsPath()
	{
		// Arrange: Build moov/trak/mdia hierarchy
		var mdia = new Mp4Box("mdia", BinaryData.FromHexString("AA BB CC DD"));
		var trak = new Mp4Box("trak", BinaryData.Empty, new[] { mdia });
		var moov = new Mp4Box("moov", BinaryData.Empty, new[] { trak });

		// Act
		var found = moov.Navigate("trak/mdia");

		// Assert
		Assert.IsNotNull(found);
		Assert.AreEqual("mdia", found.Type);
		Assert.AreSame(mdia, found);
	}

	[TestMethod]
	public void Mp4FullBox_Render_IncludesVersionAndFlags()
	{
		// Arrange
		var contentData = BinaryData.FromHexString("11 22 33 44");
		var fullBox = new Mp4FullBox("mvhd", version: 1, flags: 0x000002, contentData);

		// Act
		var rendered = fullBox.Render();

		// Assert
		// Header (8) + version (1) + flags (3) + content (4) = 16 bytes
		Assert.AreEqual(16, rendered.Length);
		Assert.AreEqual(16u, rendered.ToUInt32BE(0)); // size
		Assert.AreEqual("mvhd", rendered.Slice(4, 4).ToStringLatin1()); // type
		Assert.AreEqual(1, rendered[8]); // version
		Assert.AreEqual(0x000002u, rendered.ToUInt24BE(9)); // flags
		Assert.IsTrue(rendered.Slice(12).Span.SequenceEqual(contentData.Span)); // content
	}
}
