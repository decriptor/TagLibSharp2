// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// Which IFD's width and height a TIFF reports. Every expected size is exiftool 13.55's ImageSize for the same bytes
/// (measured), except where a case's name gives exiftool's answer and says why it differs.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class TiffSizeRuleTests
{
	static (int, int, long, long) L (int tag, long value) => (tag, 4, 1, value);

	static (int, int, long, long)[] Image (long subfileType, long width, long height) =>
		[L (254, subfileType), L (256, width), L (257, height)];

	static (int, int, long, long)[] Size (long width, long height) => [L (256, width), L (257, height)];

	// Little-endian classic TIFF, IFD0 first; a table's next offset, then raw bytes.
	static byte[] Classic ((int At, (int, int, long, long)[] Entries, long Next)[] ifds, params (int At, byte[] Bytes)[] raw)
	{
		var length = Math.Max (ifds.Max (i => i.At + 2 + (12 * i.Entries.Length) + 4), raw.Length == 0 ? 0 : raw.Max (r => r.At + r.Bytes.Length)) + 8;
		var tiff = new TiffBuilder (length).Header (ifds[0].At);
		foreach (var (at, entries, next) in ifds)
			tiff.Table (at, entries, next);
		foreach (var (at, bytes) in raw)
			tiff.Bytes (at, bytes);
		return tiff.ToArray ();
	}

	static byte[] Longs (params long[] values) => [.. values.SelectMany (v => BitConverter.GetBytes ((uint)v))];

	/// <summary>Every case: its name, its bytes, and the size it reports ("none" for no size).</summary>
	internal static IEnumerable<(string Name, byte[] Tiff, string Size)> Cases ()
	{
		yield return ("IFD0, SHORT", Classic ([(8, [(256, 3, 1, 30), (257, 3, 1, 31)], 0)]), "30x31");
		yield return ("IFD0, LONG 2^31 - 1", Classic ([(8, Size (int.MaxValue, 31), 0)]), "2147483647x31");
		yield return ("IFD0, LONG 2^31 (exiftool: 2147483648x31, past an int)", Classic ([(8, Size (1L << 31, 31), 0)]), "none");
		yield return ("IFD0, width 0 (exiftool: 0x5, no image)", Classic ([(8, Size (0, 5), 0)]), "none");
		yield return ("IFD0, width only", Classic ([(8, [L (256, 30)], 0)]), "none");
		yield return ("IFD0, width with 2 values", Classic ([(8, [(256, 4, 2, 100), L (257, 31)], 0)]), "none");
		yield return ("IFD0, SSHORT and SLONG", Classic ([(8, [(256, 8, 1, 30), (257, 9, 1, 31)], 0)]), "30x31");
		yield return ("IFD0, SSHORT width -1 (exiftool: -1x31, no image)", Classic ([(8, [(256, 8, 1, 0xFFFF), L (257, 31)], 0)]), "none");
		yield return ("IFD0, SLONG width -1 (exiftool: -1x31, no image)", Classic ([(8, [(256, 9, 1, 0xFFFFFFFF), L (257, 31)], 0)]), "none");
		yield return ("IFD0, width RATIONAL", Classic ([(8, [(256, 5, 1, 100), L (257, 31)], 0)]), "none");
		yield return ("an entry with 2^31 values after the size", Classic ([(8, [(256, 3, 1, 3), (257, 3, 1, 2), (0x010E, 2, 1L << 31, 100)], 0)]), "3x2");
		yield return ("IFD0 inside the header", new TiffBuilder (200).Header (2).Table (16, Size (30, 31)).ToArray (), "none");
		yield return ("IFD0's entries past the data", Classic ([(8, Size (30, 31), 0)])[..30], "none");

		yield return ("full IFD0", Classic ([(8, [.. Image (0, 5, 5), L (330, 100)], 0), (100, Image (0, 6, 6), 0)]), "5x5");
		yield return ("reduced IFD0, smaller full SubIFD", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, Image (0, 5, 5), 0)]), "5x5");
		yield return ("reduced IFD0, larger reduced SubIFD", Classic ([(8, [.. Image (1, 5, 5), L (330, 100)], 0), (100, Image (1, 6, 6), 0)]), "5x5");
		yield return ("IFD0 without a subfile type, full SubIFD", Classic ([(8, [.. Size (10, 10), L (330, 100)], 0), (100, Image (0, 30, 30), 0)]), "30x30");
		yield return ("reduced IFD0, SubIFD without a subfile type", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, Size (30, 30), 0)]), "10x10");
		yield return ("IFD0 without a size, full SubIFD", Classic ([(8, [L (254, 1), L (330, 100)], 0), (100, Image (0, 30, 30), 0)]), "30x30");
		yield return ("two full SubIFDs: the first", Classic ([(8, [.. Image (1, 10, 10), (330, 4, 2, 300)], 0), (100, Image (0, 20, 20), 0), (200, Image (0, 30, 30), 0)], (300, Longs (100, 200))), "20x20");
		yield return ("a full SubIFD of a reduced SubIFD", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [.. Image (1, 20, 20), L (330, 200)], 0), (200, Image (0, 30, 30), 0)]), "30x30");
		yield return ("a SubIFD's full next IFD", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, Image (1, 20, 20), 200), (200, Image (0, 40, 40), 0)]), "40x40");
		yield return ("a full SubIFD before a full IFD1", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 200), (100, Image (0, 30, 30), 0), (200, Image (0, 40, 40), 0)]), "30x30");
		yield return ("reduced IFD0, full IFD1", Classic ([(8, Image (1, 10, 10), 100), (100, Image (0, 50, 40), 0)]), "50x40");
		yield return ("reduced IFD0, IFD1 without a subfile type", Classic ([(8, Image (1, 10, 10), 100), (100, Size (50, 40), 0)]), "10x10");
		yield return ("a page IFD0 (subfile type 2), full IFD1", Classic ([(8, Image (2, 10, 10), 100), (100, Image (0, 100, 100), 0)]), "100x100");
		yield return ("subfile type SHORT 0", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [(254, 3, 1, 0), L (256, 30), L (257, 30)], 0)]), "30x30");
		yield return ("subfile type with 2 values", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [(254, 4, 2, 0), L (256, 30), L (257, 30)], 0)]), "10x10");
		yield return ("SubIFD offset typed SHORT", Classic ([(8, [.. Image (1, 256, 171), (330, 3, 1, 100)], 0), (100, Image (0, 6000, 4000), 0)]), "6000x4000");
		yield return ("SubIFD offset SHORT 40,000, a full image there", Classic ([(8, [.. Image (1, 10, 10), (330, 3, 1, 40000)], 0), (40000, Image (0, 30, 31), 0)]), "30x31");
		yield return ("SubIFD offset typed ASCII", Classic ([(8, [.. Image (1, 256, 171), (330, 2, 1, 100)], 0), (100, Image (0, 6000, 4000), 0)]), "256x171");
		yield return ("full SubIFD without a width (exiftool: 10x30, a width and a height from two IFDs)", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [L (254, 0), L (257, 30)], 0)]), "10x10");
		yield return ("full SubIFD whose last width is 0 (exiftool: 0x31; as in exiftool the last width counts, and 0 is no size)", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [.. Image (0, 30, 31), L (256, 0)], 0)]), "10x10");
		yield return ("IFD0, height 2^31 - 1", Classic ([(8, Size (31, int.MaxValue), 0)]), "31x2147483647");
		yield return ("IFD0, a second width typed ASCII", Classic ([(8, [L (256, 30), L (257, 31), (256, 2, 4, 0x00303034)], 0)]), "30x31");
		yield return ("IFD0, a second height typed ASCII", Classic ([(8, [L (256, 30), L (257, 31), (257, 2, 4, 0x00303034)], 0)]), "30x31");
		yield return ("subfile type typed ASCII (exiftool: 30x30, it reads the text as a number)", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [(254, 2, 1, 0), L (256, 30), L (257, 30)], 0)]), "10x10");

		yield return ("IFD0 of 4,097 entries", Classic ([(8, [.. Size (77, 55), .. Filler (4095)], 0)]), "77x55");
		yield return ("IFD0 of 65,535 entries, the size last", Classic ([(8, [.. Filler (65533), .. Size (77, 55)], 0)]), "77x55");
		yield return ("a full SubIFD of 5,000 entries", Classic ([(8, [.. Image (1, 10, 10), L (330, 100)], 0), (100, [.. Image (0, 300, 200), .. Filler (4997)], 0)]), "300x200");
		foreach (var (type, name) in new[] { (16, "LONG8"), (17, "SLONG8"), (18, "IFD8") })
			yield return ($"SubIFD offset typed {name} (BigTIFF only)", Classic ([(8, [.. Image (1, 10, 10), (330, type, 1, 100)], 0), (100, Image (0, 30, 31), 0)]), "10x10");
		yield return ("SubIFD offset SBYTE -16, a full image at 240", Classic ([(8, [.. Image (1, 10, 10), (330, 6, 1, 0xF0)], 0), (240, Image (0, 30, 31), 0)]), "10x10");
		yield return ("SubIFD offset SSHORT -16, a full image at 65,520", Classic ([(8, [.. Image (1, 10, 10), (330, 8, 1, 0xFFF0)], 0), (65520, Image (0, 30, 31), 0)]), "10x10");
		yield return ("three LONG SubIFD offsets, the first a full image, the third past the data", Classic ([(8, [.. Image (1, 10, 10), (330, 4, 3, 400)], 0), (200, Image (0, 30, 31), 0), (300, [L (254, 1)], 0)], (400, Longs (200, 300)))[..408], "10x10");

		foreach (var bigEndian in new[] { false, true }) {
			var order = bigEndian ? "BE" : "LE";
			yield return ($"BigTIFF {order}, LONG8", Big (bigEndian, [(16, [(256, 16, 1, 70), (257, 16, 1, 46)], 0)]), "70x46");
			yield return ($"BigTIFF {order}, reduced IFD0, full SubIFD as one LONG8", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 16, 1, 200)], 0), (200, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)]), "30x31");
			yield return ($"BigTIFF {order}, full SubIFD as one IFD8", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 18, 1, 200)], 0), (200, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)]), "30x31");
			yield return ($"BigTIFF {order}, SubIFDs as two LONG8 at an offset", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 16, 2, 400)], 0), (200, [(254, 16, 1, 1)], 0), (300, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)], (400, 200, 8), (408, 300, 8)), "30x31");
			yield return ($"BigTIFF {order}, SubIFDs as two LONG8 ending at the data's end", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 16, 2, 400)], 0), (200, [(254, 16, 1, 1)], 0), (300, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)], (400, 200, 8), (408, 300, 8))[..416], "30x31");
			yield return ($"BigTIFF {order}, SubIFDs at an offset above 2^63 - 1", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 16, 2, -1)], 0)]), "10x10");
			yield return ($"BigTIFF {order}, a SubIFD array past the data", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 16, 2, 4000)], 0)]), "10x10");
			yield return ($"BigTIFF {order}, LONG8 width 2^31 (exiftool: 2147483648x46, past an int)", Big (bigEndian, [(16, [(256, 16, 1, 1L << 31), (257, 16, 1, 46)], 0)]), "none");
			foreach (var (type, name, size) in new[] { (1, "BYTE", 1), (6, "SBYTE", 1), (3, "SHORT", 2), (8, "SSHORT", 2), (4, "LONG", 4), (9, "SLONG", 4), (13, "IFD", 4), (16, "LONG8", 8), (17, "SLONG8", 8), (18, "IFD8", 8) })
				yield return ($"BigTIFF {order}, SubIFD typed {name}", Big (bigEndian, [(16, [.. BigReduced, (330, type, 1, InEntry (120, size, bigEndian))], 0), (120, BigFull, 0)]), "30x31");
			foreach (var (type, name) in new[] { (7, "UNDEFINED"), (2, "ASCII"), (5, "RATIONAL"), (10, "SRATIONAL"), (11, "FLOAT"), (12, "DOUBLE") })
				yield return ($"BigTIFF {order}, SubIFD typed {name} (exiftool reads none)", Big (bigEndian, [(16, [.. BigReduced, (330, type, 1, InEntry (120, 1, bigEndian))], 0), (120, BigFull, 0)]), "10x10");
			yield return ($"BigTIFF {order}, SubIFD SBYTE -16, a full image at 240", Big (bigEndian, [(16, [.. BigReduced, (330, 6, 1, InEntry (0xF0, 1, bigEndian))], 0), (240, BigFull, 0)]), "10x10");
			yield return ($"BigTIFF {order}, SubIFD BYTE 240, a full image there", Big (bigEndian, [(16, [.. BigReduced, (330, 1, 1, InEntry (0xF0, 1, bigEndian))], 0), (240, BigFull, 0)]), "30x31");
			yield return ($"BigTIFF {order}, SubIFD SSHORT -16, a full image at 65,520", Big (bigEndian, [(16, [.. BigReduced, (330, 8, 1, InEntry (0xFFF0, 2, bigEndian))], 0), (65520, BigFull, 0)]), "10x10");
			yield return ($"BigTIFF {order}, SubIFD BYTE count 0", Big (bigEndian, [(16, [.. BigReduced, (330, 1, 0, InEntry (120, 1, bigEndian))], 0), (120, BigFull, 0)]), "10x10");
			yield return ($"BigTIFF {order}, two BYTE SubIFD offsets in the entry", Big (bigEndian, [(16, [.. BigReduced, (330, 1, 2, InEntry (120, 1, bigEndian))], 0), (120, BigFull, 0)]), "30x31");
			yield return ($"BigTIFF {order}, three LONG8 SubIFD offsets, the first a full image, the third past the data", Big (bigEndian, [(16, [.. BigReduced, (330, 16, 3, 400)], 0), (200, BigFull, 0), (300, [(254, 16, 1, 1)], 0)], (400, 200, 8), (408, 300, 8))[..416], "10x10");
			yield return ($"BigTIFF {order}, three LONG SubIFD offsets, the first a full image, the third past the data", Big (bigEndian, [(16, [.. BigReduced, (330, 4, 3, 400)], 0), (200, BigFull, 0), (300, [(254, 16, 1, 1)], 0)], (400, 200, 4), (404, 300, 4))[..408], "10x10");
			yield return ($"BigTIFF {order}, IFD0 of 4,097 entries", Big (bigEndian, [(16, [(256, 16, 1, 77), (257, 16, 1, 55), .. Filler (4095)], 0)]), "77x55");
			yield return ($"BigTIFF {order}, reduced IFD0, a next IFD above 2^63 - 1", Big (bigEndian, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10)], -1)]), "10x10");
		}

		yield return ("BigTIFF LE, SHORT", Big (false, [(16, [(256, 3, 1, 70), (257, 3, 1, 46)], 0)]), "70x46");
		yield return ("BigTIFF LE, LONG with two SubIFDs in the entry", Big (false, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 4, 2, 200L | (300L << 32))], 0), (200, [(254, 16, 1, 1)], 0), (300, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)]), "30x31");
		yield return ("BigTIFF LE, SubIFD typed SHORT", Big (false, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 3, 1, 200)], 0), (200, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)]), "30x31");
		yield return ("BigTIFF LE, SubIFD typed ASCII", Big (false, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 2, 1, 200)], 0), (200, [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)], 0)]), "10x10");
		yield return ("BigTIFF LE, a width whose value count is above 2^63 - 1", Big (false, [(16, [(256, 16, -1, 70), (257, 16, 1, 46)], 0)]), "none");
		yield return ("BigTIFF LE, LONG8 width 2^32 + 5 (exiftool: 4294967301x46, past an int)", Big (false, [(16, [(256, 16, 1, (1L << 32) + 5), (257, 16, 1, 46)], 0)]), "none");
		yield return ("BigTIFF LE, three LONG SubIFD offsets, the third past the data", new TiffBuilder (168, big: true).Header (16)
			.Table (16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 4, 3, 160)]).Table (120, [(254, 16, 1, 1)])
			.U32 (160, 120).U32 (164, 120).ToArray (), "10x10");
		yield return ("BigTIFF LE, a SubIFD count of 1 then bytes that read as a full IFD's offset", Big (false, [(16, [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10), (330, 16, 1, 200), (500, 0, 0, 0)], 0), (200, [(254, 16, 1, 1)], 0), (500, Image (0, 30, 31), 0)]), "10x10");
		yield return ("BigTIFF LE, LONG8 width above 2^63 - 1 (exiftool: 18446744073709551615x46, past an int)", Big (false, [(16, [(256, 16, 1, -1), (257, 16, 1, 46)], 0)]), "none");
		yield return ("BigTIFF, offset size 4", Big (false, [(16, Size (70, 46), 0)]).Patch (4, 4), "none");
		yield return ("BigTIFF, reserved 1", Big (false, [(16, Size (70, 46), 0)]).Patch (6, 1), "none");
		yield return ("BigTIFF header only", Big (false, [(16, [], 0)])[..16], "none");
		yield return ("BigTIFF, an IFD0 count above 2^63 - 1 before a full IFD's offset", Big (false, [(16, [], 0), (100, Image (0, 70, 46), 0)], (16, -1, 8), (24, 100, 8)), "none");
	}

	// A chain of nested SubIFDs below a reduced IFD0, each the only SubIFD of the one above; only the deepest is full.
	static byte[] Nested (int depth)
	{
		var ifds = new List<(int At, (int, int, long, long)[] Entries, long Next)> ();
		for (var d = 0; d <= depth; d++) {
			var at = 8 + (d * 64);
			ifds.Add ((at, d == depth ? Image (0, 900, 600) : [.. Image (1, 2, 2), L (330, at + 64)], 0));
		}

		return Classic ([.. ifds]);
	}

	// The walk reads SubIFDs 64 levels below IFD0 and no deeper (exiftool reads deeper; this is the walk's bound).
	[TestMethod]
	[DataRow (64, 900)]
	[DataRow (65, 2)]
	public void Tiff_FullImage_IsFoundDownToTheDepthBound (int depth, int width)
	{
		Assert.IsTrue (TiffSize.TryRead (Nested (depth), dng: false, out var properties));
		Assert.AreEqual (width, properties!.Value.Width);
	}

	// 20 SubIFDs of a reduced IFD0, all reduced but the last: more offsets wait than the stack span holds, and the one
	// pushed first, at the bottom, is the full image.
	[TestMethod]
	public void Tiff_FullImage_AfterMoreSubIfdsThanTheStackSpanHolds_IsFound ()
	{
		var subs = Enumerable.Range (0, 20).Select (n => (long)(400 + (n * 64))).ToArray ();
		var ifds = new List<(int At, (int, int, long, long)[] Entries, long Next)> { (8, [.. Image (1, 2, 2), (330, 4, 20, 200)], 0) };
		for (var n = 0; n < 20; n++)
			ifds.Add (((int)subs[n], n == 19 ? Image (0, 900, 600) : Image (1, 3, 3), 0));
		var tiff = Classic ([.. ifds], (200, Longs (subs)));

		Assert.IsTrue (TiffSize.TryRead (tiff, dng: false, out var properties));
		Assert.AreEqual (900, properties!.Value.Width);
	}

	// A chain of 20 reduced IFDs whose last names IFD0 again: past the 16 offsets kept on the stack the walk still
	// knows IFD0, so it reads each IFD once and stops.
	[TestMethod]
	public void Tiff_ChainBackToIfd0AfterSixteenIfds_IsReadOnce ()
	{
		var ifds = Enumerable.Range (0, 20).Select (n => (8 + (n * 64), Image (1, 2 + n, 2), n == 19 ? 8L : 8 + ((n + 1) * 64))).ToArray ();
		var tiff = Classic (ifds);
		TiffSize.IfdReads = 0;

		Assert.IsTrue (TiffSize.TryRead (tiff, dng: false, out var properties));

		Assert.AreEqual (2, properties!.Value.Width, "no full image: IFD0's size");
		Assert.AreEqual (20, TiffSize.IfdReads, "each of the 20 once, IFD0 included");
	}

	// The walk reads at most 4,096 IFDs: a full image that is the 4,096th IFD read is found, the 4,097th is not.
	// IFD0 (reduced) has a reduced SubIFD whose next offset is 0, the end of its chain, which takes no part in the
	// bound; then IFD0's chain of empty IFDs ends in the full image.
	[TestMethod]
	[DataRow (4096, 900)]
	[DataRow (4097, 2)]
	public void Tiff_FullImage_IsFoundUpToTheIfdBound (int position, int width)
	{
		const int Ifd0 = 8, Sub = Ifd0 + 2 + (4 * 12) + 4, Chain = Sub + 2 + (3 * 12) + 4;
		var ifds = new List<(int At, (int, int, long, long)[] Entries, long Next)> {
			(Ifd0, [.. Image (1, 2, 2), L (330, Sub)], Chain),
			(Sub, Image (1, 3, 3), 0),
		};
		var at = Chain;
		for (var n = 3; n < position; n++, at += 6)
			ifds.Add ((at, [], at + 6));
		ifds.Add ((at, Image (0, 900, 600), 0));

		Assert.IsTrue (TiffSize.TryRead (Classic ([.. ifds]), dng: false, out var properties));
		Assert.AreEqual (width, properties!.Value.Width);
	}

	// A reduced TIFF of up to 16 IFDs is walked on the stack: reading its size allocates nothing. Past 16 the visited
	// set is sized by the IFDs the data can hold (6 bytes each), not by the bound of 4,096.
	[TestMethod]
	public void Tiff_SizeWalk_AllocatesNothingUpToSixteenIfds_AndByTheDataPast ()
	{
		static byte[] Chain (int ifds) => Classic ([.. Enumerable.Range (0, ifds).Select (n => (8 + (n * 64), Image (1, 2 + n, 2), n == ifds - 1 ? 0L : 8 + ((n + 1) * 64)))]);
		var small = Chain (16);
		var large = Chain (20);
		_ = TiffSize.TryRead (small, dng: false, out _);
		_ = TiffSize.TryRead (large, dng: false, out _);

		TagLibSharp2.Tests.Kit.Bounds.AssertAllocatesAtMost (0, () => TiffSize.TryRead (small, dng: false, out _));
		TagLibSharp2.Tests.Kit.Bounds.AssertAllocatesAtMost (large.Length * 8, () => TiffSize.TryRead (large, dng: false, out _));
	}

	// DNG is TIFF whose IFD0 holds the DNG version tag, or a TIFF named .dng; the tag in a SubIFD alone is not DNG.
	[TestMethod]
	[DataRow ("IFD0", false, "DNG")]
	[DataRow ("SubIFD", false, "TIFF")]
	[DataRow ("none", true, "DNG")]
	public void Tiff_IsDng_ByIfd0sVersionTagOrTheName (string where, bool named, string format)
	{
		(int, int, long, long)[] dng = [(0xC612, 1, 4, 0x00000401)];
		var tiff = Classic ([(8, [.. Image (1, 2, 2), L (330, 100), .. where == "IFD0" ? dng : []], 0), (100, [.. Image (0, 30, 31), .. where == "SubIFD" ? dng : []], 0)]);

		Assert.IsTrue (TiffSize.TryRead (tiff, named, out var properties));
		Assert.AreEqual (format, properties!.Value.Format);
		Assert.AreEqual (where == "IFD0", TiffSize.HasDngVersion (tiff));
	}

	// Entries of a tag no reader knows, to make a table large.
	static (int, int, long, long)[] Filler (int count) => [.. Enumerable.Repeat ((0xEEEE, 3, 1L, 0L), count)];

	static readonly (int, int, long, long)[] BigReduced = [(254, 16, 1, 1), (256, 16, 1, 10), (257, 16, 1, 10)];
	static readonly (int, int, long, long)[] BigFull = [(254, 16, 1, 0), (256, 16, 1, 30), (257, 16, 1, 31)];

	// A value of the given size at the start of a BigTIFF entry's 8-byte field, as the builder writes the field as
	// one 8-byte word in the file's byte order.
	static long InEntry (long value, int size, bool bigEndian) => bigEndian ? value << (8 * (8 - size)) : value;

	// BigTIFF, IFD0 first; raw 8-byte words (at, value, size) after the tables.
	static byte[] Big (bool bigEndian, (int At, (int, int, long, long)[] Entries, long Next)[] ifds, params (int At, long Value, int Size)[] words)
	{
		var length = Math.Max (ifds.Max (i => i.At + 8 + (20 * i.Entries.Length) + 8), words.Length == 0 ? 0 : words.Max (w => w.At + w.Size)) + 8;
		var tiff = new TiffBuilder (length, bigEndian, big: true).Header (ifds[0].At);
		foreach (var (at, entries, next) in ifds)
			tiff.Table (at, entries, next);
		foreach (var (at, value, size) in words) {
			if (size == 4)
				tiff.U32 (at, value);
			else
				tiff.U64 (at, value);
		}
		return tiff.ToArray ();
	}

	[TestMethod]
	public void Tiff_ReportsTheSizeExiftoolReports ()
	{
		var wrong = new List<string> ();
		foreach (var (name, tiff, size) in Cases ()) {
			var got = TiffSize.TryRead (tiff, dng: false, out var properties) ? $"{properties!.Value.Width}x{properties.Value.Height}" : "none";
			if (got != size)
				wrong.Add ($"{name}: {got}, expected {size}");
		}

		Assert.AreEqual (0, wrong.Count, string.Join ("\n", wrong));
	}
}

static class TiffBytes
{
	/// <summary>The bytes with one byte changed.</summary>
	public static byte[] Patch (this byte[] data, int at, byte value)
	{
		var copy = (byte[])data.Clone ();
		copy[at] = value;
		return copy;
	}
}
