// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageAssert;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>TIFF, DNG and BigTIFF width and height: IFD0, the reduced-preview rule, and the bounds of the IFD walk.</summary>
[TestClass]
[TestCategory ("Unit")]
public class TiffSizeTests
{
	// A TIFF whose only IFD has no next-IFD offset, or only 2 of its 4 bytes: exiftool reads ImageWidth 3 and
	// ImageHeight 2 from it, so the file opens with those dimensions.
	[TestMethod]
	[DataRow (4)]
	[DataRow (2)]
	public void Tiff_WhoseIfdHasNoNextOffset_ReportsItsSize (int cut)
	{
		byte[] tiff = [
			(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
			2, 0,
			0x00, 0x01, 3, 0, 1, 0, 0, 0, 3, 0, 0, 0,
			0x01, 0x01, 3, 0, 1, 0, 0, 0, 2, 0, 0, 0,
			0, 0, 0, 0];

		var file = Open (tiff[..^cut], "a.tif");

		Assert.AreEqual (3, file.ImageProperties!.Value.Width);
		Assert.AreEqual (2, file.ImageProperties!.Value.Height);
	}

	[TestMethod]
	public void Tiff_ReducedPreview_YieldsToTheFullResolutionImage ()
	{
		var dng = PreviewIfd (dng: true, fullWidth: 6000, fullHeight: 4000);
		AssertSize (dng, "a.dng", 6000, 4000);
		Assert.AreEqual ("DNG", ((IMediaFile)MediaFile.ReadFromData (dng, "a.dng").File!).ImageProperties!.Value.Format);

		var tiff = PreviewIfd (dng: false, fullWidth: 6000, fullHeight: 4000);
		AssertSize (tiff, "a.tif", 6000, 4000);
		Assert.AreEqual ("TIFF", ((IMediaFile)MediaFile.ReadFromData (tiff, "a.tif").File!).ImageProperties!.Value.Format);

		var firstPage = LinkedTiff ();
		AssertSize (firstPage, "a.tif", 10, 10);

		var cycle = CyclicSubIfd ();
		AssertSize (cycle, "a.tif", 4, 5);
	}

	[TestMethod]
	public void BigTiff_ReportsIfd0Size ()
	{
		AssertSize (BigTiff (70, 46, littleEndian: true), "a.tif", 70, 46);
	}

	// A reduced preview IFD0 (NewSubfileType 1, 256x171) whose SubIFD is the full-resolution image.
	static byte[] PreviewIfd (bool dng, int fullWidth, int fullHeight)
	{
		(int, int, long, long)[] root = [(254, 4, 1, 1), (256, 4, 1, 256), (257, 4, 1, 171), (330, 4, 1, 0)];
		if (dng)
			root = [.. root, (0xC612, 4, 1, 1)];
		var sub = 8 + 2 + (root.Length * 12) + 4;
		root[3] = (330, 4, 1, sub);
		return new TiffBuilder (sub + 2 + (3 * 12) + 4).Header (8).Table (8, root)
			.Table (sub, [(254, 4, 1, 0), (256, 4, 1, fullWidth), (257, 4, 1, fullHeight)]).ToArray ();
	}

	// IFD0 (10x10) and IFD1 (100x100) linked by IFD0's next offset.
	static byte[] LinkedTiff () =>
		new TiffBuilder (8 + 30 + 30).Header (8).Table (8, [(256, 4, 1, 10), (257, 4, 1, 10)], next: 38)
			.Table (38, [(256, 4, 1, 100), (257, 4, 1, 100)]).ToArray ();

	// A reduced IFD0 whose SubIFD is IFD0 itself.
	static byte[] CyclicSubIfd () =>
		new TiffBuilder (8 + 2 + (4 * 12) + 4).Header (8).Table (8, [(254, 4, 1, 1), (256, 4, 1, 4), (257, 4, 1, 5), (330, 4, 1, 8)]).ToArray ();

	[TestMethod]
	public void Tiff_DetectionOfAReducedImageOrDng_AllocatesNoScratch ()
	{
		var reduced = Tiff ((254, 1), (256, 4), (257, 3));
		var dng = TiffChain ([[(254, 1), (256, 4), (257, 3), (330, 8 + 2 + (5 * 12) + 4), (0xC612, 1)], [(254, 0), (256, 400), (257, 300)]]);
		foreach (var data in new[] { reduced, dng }) {
			_ = MediaFile.DetectFormat (data);
			Bounds.AssertAllocatesAtMost (Noise, () => MediaFile.DetectFormat (data));
		}

		Assert.AreEqual (MediaFormat.Dng, MediaFile.DetectFormat (dng));
	}

	static int IfdReads (byte[] tiff)
	{
		TiffSize.IfdReads = 0;
		_ = ImageHeader.TryRead (tiff, MediaFormat.Tiff, out _);
		return TiffSize.IfdReads;
	}

	// A reduced IFD0 followed by N IFDs, the last of them without its next-IFD offset: the size walk reads each IFD
	// once, so the reads grow linearly, as does the allocation.
	[TestMethod]
	public void Tiff_IfdChainEndingWithoutANextOffset_IsReadLinearly ()
	{
		static byte[] Chain (int n)
		{
			var tiff = TiffChain ([[(254, 1), (256, 2), (257, 2)], .. Enumerable.Range (0, n).Select (k => new[] { (254, 1), (256, 3 + k), (257, 3) })]);
			return tiff[..^4];
		}

		var small = IfdReads (Chain (50));
		var large = IfdReads (Chain (200));
		Assert.IsTrue (large <= 201 + 1, $"{large} IFD reads for 201 IFDs");
		Assert.IsTrue (large < 5 * small, $"4x the IFDs took {large / (double)small:F1}x the reads");
		Bounds.AssertLinearAllocation (Chain, data => ImageHeader.TryRead (data, MediaFormat.Tiff, out _), 50, runs: 3);
	}

	[TestMethod]
	public void Tiff_CyclicIfds_AreReadOnce ()
	{
		// IFD0 is a reduced image whose SubIFD and next IFD are itself.
		var cycle = Tiff ((254, 1), (256, 4), (257, 5), (330, 8));
		BitConverter.GetBytes (8).CopyTo (cycle, cycle.Length - 4);
		Assert.IsTrue (IfdReads (cycle) <= 2, $"{IfdReads (cycle)} IFD reads");

		// A SubIFD that names itself as its next IFD comes before the full-resolution SubIFD.
		var loopFirst = SubIfdTree (out var full);
		var opened = MediaFile.ReadFromData (loopFirst, "a.tif");
		Assert.AreEqual (full, ((IMediaFile)opened.File!).ImageProperties!.Value.Width, "the full-resolution SubIFD after the loop is reached");
		Assert.IsTrue (IfdReads (loopFirst) <= 4, $"{IfdReads (loopFirst)} IFD reads");
	}

	// IFD0 (reduced, 2x2) with SubIFDs [A, B]. A has no size and its next IFD is A. B is 900x600, not reduced.
	static byte[] SubIfdTree (out int fullWidth)
	{
		fullWidth = 900;
		const int Ifd0 = 8;
		const int Ifd0Size = 2 + (4 * 12) + 4;
		const int Subs = Ifd0 + Ifd0Size;
		const int A = Subs + 8;
		const int ASize = 2 + 4;
		const int B = A + ASize;
		var data = new byte[B + 2 + (3 * 12) + 4];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		BitConverter.GetBytes (Ifd0).CopyTo (data, 4);
		BitConverter.GetBytes ((ushort)4).CopyTo (data, Ifd0);
		WriteLong (data, Ifd0 + 2, 254, 1);
		WriteLong (data, Ifd0 + 14, 256, 2);
		WriteLong (data, Ifd0 + 26, 257, 2);
		WriteLong (data, Ifd0 + 38, 330, Subs);
		BitConverter.GetBytes (2).CopyTo (data, Ifd0 + 42);
		BitConverter.GetBytes (A).CopyTo (data, Subs);
		BitConverter.GetBytes (B).CopyTo (data, Subs + 4);
		BitConverter.GetBytes (A).CopyTo (data, A + 2);
		BitConverter.GetBytes ((ushort)3).CopyTo (data, B);
		WriteLong (data, B + 2, 254, 0);
		WriteLong (data, B + 14, 256, fullWidth);
		WriteLong (data, B + 26, 257, 600);
		return data;
	}

	[TestMethod]
	public void Tiff_IfdWalk_StopsAtTheIfdCap ()
	{
		// 5000 linked IFDs after a reduced IFD0. The last one is the full-resolution image.
		var ifds = new List<(int, int)[]> { new[] { (254, 1), (256, 2), (257, 2) } };
		for (var n = 0; n < 4998; n++)
			ifds.Add ([]);
		ifds.Add ([(254, 0), (256, 900), (257, 600)]);
		var chain = TiffChain (ifds);

		var reads = IfdReads (chain);

		Assert.IsTrue (reads <= 4097, $"{reads} IFD reads");
		Assert.AreEqual (2, ((IMediaFile)MediaFile.ReadFromData (chain, "a.tif").File!).ImageProperties!.Value.Width, "IFDs past the cap are not read");
	}

	[TestMethod]
	public void Tiff_SubIfdNesting_StopsAtTheDepthCap ()
	{
		// IFD0 (reduced 2x2) -> SubIFD -> SubIFD ... 80 deep; only the deepest is a full-resolution image.
		const int Depth = 80;
		var ifds = new List<(int Tag, int Value)[]> ();
		var offsets = new int[Depth + 1];
		offsets[0] = 8;
		for (var d = 0; d <= Depth; d++) {
			var count = d == 0 ? 4 : d == Depth ? 3 : 2;
			if (d < Depth)
				offsets[d + 1] = offsets[d] + 2 + (12 * count) + 4;
		}

		for (var d = 0; d <= Depth; d++) {
			ifds.Add (d == 0 ? [(254, 1), (256, 2), (257, 2), (330, offsets[1])]
				: d == Depth ? [(254, 0), (256, 900), (257, 600)]
				: [(254, 1), (330, offsets[d + 1])]);
		}

		var data = TiffChain (ifds);
		for (var d = 0; d <= Depth; d++)
			BitConverter.GetBytes (0).CopyTo (data, offsets[d] + 2 + (12 * ifds[d].Length));

		var reads = IfdReads (data);

		Assert.IsTrue (reads <= 67, $"{reads} IFD reads");
		Assert.AreEqual (2, ((IMediaFile)MediaFile.ReadFromData (data, "a.tif").File!).ImageProperties!.Value.Width, "SubIFDs deeper than the cap are not read");
	}

	[TestMethod]
	public void Tiff_MoreSubIfdsThanAreFollowed_StillReadsTheFirstOnes ()
	{
		// IFD0 (reduced) lists 40 SubIFDs; the first is the full-resolution image.
		const int Ifd0Size = 2 + (4 * 12) + 4;
		const int Array = 8 + Ifd0Size;
		const int Full = Array + (40 * 4);
		var data = new byte[Full + 2 + (3 * 12) + 4];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		BitConverter.GetBytes (8).CopyTo (data, 4);
		BitConverter.GetBytes ((ushort)4).CopyTo (data, 8);
		WriteLong (data, 10, 254, 1);
		WriteLong (data, 22, 256, 2);
		WriteLong (data, 34, 257, 2);
		WriteLong (data, 46, 330, Array);
		BitConverter.GetBytes (40).CopyTo (data, 50);
		for (var n = 0; n < 40; n++)
			BitConverter.GetBytes (Full).CopyTo (data, Array + (4 * n));
		BitConverter.GetBytes ((ushort)3).CopyTo (data, Full);
		WriteLong (data, Full + 2, 254, 0);
		WriteLong (data, Full + 14, 256, 900);
		WriteLong (data, Full + 26, 257, 600);

		Assert.AreEqual (900, ((IMediaFile)MediaFile.ReadFromData (data, "a.tif").File!).ImageProperties!.Value.Width);
	}

	[TestMethod]
	public void Tiff_ManyOverlappingSubIfds_AreBoundedByTheIfdCap ()
	{
		// 4500 IFDs at distinct offsets, each listing 32 of the others as SubIFDs.
		const int Count = 4500;
		const int Size = 2 + 12 + 4;
		var arrayAt = 8 + (Count * Size);
		var data = new byte[arrayAt + (Count * 32 * 4)];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		BitConverter.GetBytes (8).CopyTo (data, 4);
		for (var n = 0; n < Count; n++) {
			var at = 8 + (n * Size);
			BitConverter.GetBytes ((ushort)1).CopyTo (data, at);
			var list = arrayAt + (n * 32 * 4);
			WriteLong (data, at + 2, 330, list);
			BitConverter.GetBytes (32).CopyTo (data, at + 6);
			for (var s = 0; s < 32; s++)
				BitConverter.GetBytes (8 + ((((n * 31) + s + 1) % Count) * Size)).CopyTo (data, list + (4 * s));
		}

		// IFD0 must be reduced to start the walk: replace its single SubIFD entry with NewSubFileType and keep the
		// SubIFD list by pointing the next-IFD at IFD 1.
		WriteLong (data, 10, 254, 1);
		BitConverter.GetBytes (8 + Size).CopyTo (data, 8 + 2 + 12);

		var reads = IfdReads (data);
		Assert.IsTrue (reads <= 4097, $"{reads} IFD reads");
		Bounds.AssertAllocatesAtMost (512 * 1024, () => ImageHeader.TryRead (data, MediaFormat.Tiff, out _));
	}

	// K tables at distinct offsets 4 bytes apart, each with 1,000 entries that overlap the next table's, reached
	// through a chain of IFDs whose SubIFD arrays list 32 of them each. Tables that do not overlap hold at most one
	// entry per 12 bytes, so a walk that reads more than that has met tables that overlap: the entries the size walk
	// reads stay within the data's bound, at K and 4K tables alike, instead of K x 1,000.
	static byte[] OverlappingTables (int tables)
	{
		const int Entries = 1000;
		var chain = tables / 32;
		const int ChainAt = 8, ChainSize = 2 + 12 + 4;
		var arraysAt = ChainAt + (chain * ChainSize);
		var regionAt = arraysAt + (chain * 32 * 4);
		var tiff = new TiffBuilder (regionAt + (4 * tables) + 2 + (12 * Entries) + 8).Header (ChainAt);
		for (var j = 0; j < chain; j++) {
			var at = ChainAt + (j * ChainSize);
			tiff.Table (at, [(330, 4, 32, arraysAt + (j * 128))], j == chain - 1 ? 0 : at + ChainSize);
			for (var s = 0; s < 32; s++)
				tiff.U32 (arraysAt + (j * 128) + (4 * s), regionAt + (4 * ((32 * j) + s)));
		}

		for (var i = 0; i < tables; i++)
			tiff.U16 (regionAt + (4 * i), Entries);
		return tiff.ToArray ();
	}

	[TestMethod]
	[DataRow (512)]
	[DataRow (2048)]
	public void Tiff_OverlappingTables_AreReadWithinTheEntryBoundOfTheData (int tables)
	{
		var data = OverlappingTables (tables);
		TiffSize.EntryReads = 0;

		_ = TiffSize.TryRead (data, dng: false, out _);

		Console.WriteLine ($"STEPS overlapping tables {tables}: {TiffSize.EntryReads} entries for {data.Length} bytes");
		Assert.IsTrue (TiffSize.EntryReads <= data.Length / 12, $"{TiffSize.EntryReads} entries read from {data.Length} bytes");
		Assert.IsTrue (TiffSize.EntryReads >= 1000, $"{TiffSize.EntryReads} entries read: the first table, within the bound, is read whole");
	}

	[TestMethod]
	public void Tiff_OverlappingTables_AllocateLinearly () =>
		Bounds.AssertLinearAllocation (OverlappingTables, data => TiffSize.TryRead (data, dng: false, out _), 512, runs: 3);

	[TestMethod]
	public void Read_Tiff_ReportsIfdSize ()
	{
		AssertImage (TiffImage (width: 64, height: 48, littleEndian: true), "photo.tif", MediaFormat.Tiff, 64, 48, "TIFF");
		AssertImage (TiffImage (width: 20, height: 10, littleEndian: false), "photo.tiff", MediaFormat.Tiff, 20, 10, "TIFF");
	}

	[TestMethod]
	public void Read_Dng_IsTiffWithTheDngVersionTag ()
	{
		AssertImage (TiffImage (width: 32, height: 16, littleEndian: true, dng: true), "photo.dng", MediaFormat.Dng, 32, 16, "DNG");
		AssertImage (TiffImage (width: 32, height: 16, littleEndian: true, dng: true), "photo.tif", MediaFormat.Dng, 32, 16, "DNG");
		AssertImage (TiffImage (width: 32, height: 16, littleEndian: true), "photo.dng", MediaFormat.Dng, 32, 16, "DNG");
	}
}
