// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Kit;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Kit")]
public class NestingTests
{
	[TestMethod]
	public void EbmlElement_UsesTheEightOctetSize ()
	{
		var element = Nesting.EbmlElement (0x4487, [0xAA, 0xBB]);

		Assert.AreEqual ("4487" + "0100000000000002" + "AABB", Convert.ToHexString (element));
	}

	[TestMethod]
	public void EbmlUnknownSize_UsesAllOnesInEightOctets ()
	{
		var element = Nesting.EbmlUnknownSize (0x1F43B675, [0xEC, 0x80]);

		Assert.AreEqual ("1F43B675" + "01FFFFFFFFFFFFFF" + "EC80", Convert.ToHexString (element));
	}

	[TestMethod]
	public void EbmlChain_EachLevelHoldsTheRestOfTheChain ()
	{
		var chain = Nesting.EbmlChain (0x67C8, 3, prefix: [0x01], innermost: [0xEE]);

		// Three levels of ID (2) + size (8) + prefix (1), then the innermost byte.
		Assert.AreEqual ((3 * 11) + 1, chain.Length);
		for (var level = 0; level < 3; level++) {
			var at = level * 11;
			Assert.AreEqual (0x67, chain[at]);
			Assert.AreEqual (0xC8, chain[at + 1]);
			Assert.AreEqual (0x01, chain[at + 2], "8-octet size marker");
			var size = BinaryPrimitives.ReadUInt64BigEndian (chain.AsSpan (at + 2)) & 0x00FF_FFFF_FFFF_FFFFUL;
			Assert.AreEqual ((ulong)(chain.Length - at - 10), size, $"level {level}");
			Assert.AreEqual (0x01, chain[at + 10], "prefix");
		}

		Assert.AreEqual (0xEE, chain[^1]);
	}

	[TestMethod]
	public void EbmlChain_ReadsBackThroughTheLibraryReader ()
	{
		var chain = Nesting.EbmlChain (0x67C8, 4, prefix: [], innermost: [0xEC, 0x80]);
		var offset = 0;
		var end = chain.Length;

		for (var level = 0; level < 4; level++) {
			Assert.IsTrue (EbmlReader.TryRead (chain, offset, end, out var element), $"level {level}");
			Assert.AreEqual (0x67C8UL, element.Id);
			Assert.AreEqual (end, element.End);
			offset = element.DataOffset;
		}
	}

	[TestMethod]
	public void IsoBox_UsesTheLargeSize ()
	{
		var box = Nesting.IsoBox ("udta", [0xAA]);

		Assert.AreEqual ("00000001" + "75647461" + "0000000000000011" + "AA", Convert.ToHexString (box));
	}

	[TestMethod]
	public void IsoBoxChain_ReadsBackAsNestedBoxes ()
	{
		var chain = Nesting.IsoBoxChain ("udta", 3, innermost: Nesting.IsoBox ("free", []));

		var box = Mp4Box.Parse (chain).Box!;
		for (var level = 0; level < 3; level++) {
			Assert.AreEqual ("udta", box.Type, $"level {level}");
			Assert.IsTrue (box.UsesExtendedSize);
			Assert.AreEqual (1, box.Children.Count);
			box = box.Children[0];
		}

		Assert.AreEqual ("free", box.Type);
	}

	[TestMethod]
	public void RiffListChain_EachLevelHoldsTheRestOfTheChain ()
	{
		var chain = Nesting.RiffListChain ("INFO", 3, innermost: [0x49, 0x4E, 0x41, 0x4D, 0, 0, 0, 0]);

		Assert.AreEqual ((3 * 12) + 8, chain.Length);
		for (var level = 0; level < 3; level++) {
			var at = level * 12;
			Assert.AreEqual ("LIST", System.Text.Encoding.ASCII.GetString (chain, at, 4));
			Assert.AreEqual ((uint)(chain.Length - at - 8), BinaryPrimitives.ReadUInt32LittleEndian (chain.AsSpan (at + 4)));
			Assert.AreEqual ("INFO", System.Text.Encoding.ASCII.GetString (chain, at + 8, 4));
		}
	}

	[TestMethod]
	public void Id3v2ChapterChain_OneLevel_IsAChapterWithItsTitle ()
	{
		var tag = Id3v2Tag.Read (Nesting.Id3v2Tag (Nesting.Id3v2ChapterChain (1, "Deepest"))).Tag!;

		Assert.HasCount (1, tag.ChapterFrames);
		Assert.AreEqual ("Deepest", tag.ChapterFrames[0].Title);
	}

	[TestMethod]
	public void Id3v2ChapterChain_ManyLevels_IsOneOuterChapter ()
	{
		var tag = Id3v2Tag.Read (Nesting.Id3v2Tag (Nesting.Id3v2ChapterChain (5, "Deepest"))).Tag!;

		Assert.HasCount (1, tag.ChapterFrames);
		Assert.AreEqual ("c", tag.ChapterFrames[0].ElementId);
	}

	[TestMethod]
	public void Id3v2TocChain_OneLevel_IsATableOfContentsWithItsTitle ()
	{
		var tag = Id3v2Tag.Read (Nesting.Id3v2Tag (Nesting.Id3v2TocChain (1, "Contents"))).Tag!;

		Assert.HasCount (1, tag.TableOfContentsFrames);
		Assert.AreEqual ("Contents", tag.TableOfContentsFrames[0].Title);
	}

	[TestMethod]
	public void Id3v2Tag_UsesAVersion4HeaderWithASyncsafeSize ()
	{
		var tag = Nesting.Id3v2Tag (new byte[200]);

		Assert.AreEqual ("494433" + "0400" + "00" + "00000148", Convert.ToHexString (tag, 0, 10));
		Assert.AreEqual (210, tag.Length);
	}

	[TestMethod]
	public void Syncsafe_SplitsSevenBitsPerByte ()
	{
		var bytes = new byte[4];

		Nesting.WriteSyncsafe (bytes, 0x0FFF_FFFF);

		Assert.AreEqual ("7F7F7F7F", Convert.ToHexString (bytes));
	}

	[TestMethod]
	public void Chains_AtTheHostileDepth_StayLinearInSize ()
	{
		// The builders write each level once, so 100k levels cost a few megabytes, not gigabytes.
		var allocated = Bounds.AllocatedBytes (() => _ = Nesting.IsoBoxChain ("udta", Nesting.HostileDepth, []));

		Assert.IsTrue (allocated < 4 * 16 * Nesting.HostileDepth, $"allocated {allocated}");
	}
}
