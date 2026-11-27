// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Image.Xmp;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ExifTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>The Exif edit walks are linear in the entries: step counts at growing sizes for the worst shapes.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifEditScalingTests
{
	// N copies of XPComment in IFD0 and N kept entries in IFD1: one sort and a binary search per removed value.
	static long ExifRemovalSteps (int n)
	{
		const int Header = 8;
		var ifd0 = Header;
		var ifd1 = ifd0 + 2 + (12 * n) + 4;
		var values = ifd1 + 2 + (12 * n) + 4;
		var data = new byte[values + (16 * n)];
		data[0] = (byte)'I';
		data[1] = (byte)'I';
		data[2] = 42;
		BitConverter.GetBytes (ifd0).CopyTo (data, 4);
		BitConverter.GetBytes ((ushort)n).CopyTo (data, ifd0);
		BitConverter.GetBytes ((ushort)n).CopyTo (data, ifd1);
		BitConverter.GetBytes (ifd1).CopyTo (data, ifd0 + 2 + (12 * n));
		for (var k = 0; k < n; k++) {
			foreach (var (table, id, value) in new[] { (ifd0, 0x9C9C, values + (16 * k)), (ifd1, 0x0131, values + (16 * k) + 8) }) {
				var at = table + 2 + (12 * k);
				BitConverter.GetBytes ((ushort)id).CopyTo (data, at);
				BitConverter.GetBytes ((ushort)2).CopyTo (data, at + 2);
				BitConverter.GetBytes (8).CopyTo (data, at + 4);
				BitConverter.GetBytes (value).CopyTo (data, at + 8);
			}
		}

		var tag = new ImageTag (MediaFormat.Png, (ImageStores)0x3F);
		tag.ReadField ((ImageStores)32, "Old");
		tag.Comment = "New";
		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove (data, 0, tag, "eXIf", out var changed, out var error), error);
		Assert.IsTrue (changed);
		return IfdSteps.Count;
	}

	static readonly int[] ExifSizes = [1000, 2000, 4000, 8000];

	[TestMethod]
	public void Edit_ExifRemoval_IsLinearithmicInTheEntries ()
	{
		var steps = ExifSizes.Select (n => (N: n, Steps: ExifRemovalSteps (n))).ToList ();
		// ExifSteps counts every entry visit and every read or write of the removal marks and the protected
		// ranges, so a scan of either per entry (quadratic: about 64x for 8x the entries) shows here.
		// 9 is the measured constant after the removed-versus-protected check (N=1000 took 82,973 steps;
		// 8 * N * ceil(log2 N) is 80,000). A scan per entry is still about 64x at 8x the entries.
		foreach (var (n, count) in steps)
			Assert.IsTrue (count <= 9L * n * (long)Math.Ceiling (Math.Log2 (n)), $"N={n}: {count} steps");
		Assert.IsTrue (steps[^1].Steps < 16 * steps[0].Steps, $"8x the entries took {steps[^1].Steps / (double)steps[0].Steps:F1}x the steps");
	}

	// The scan for the field's tags is one pass: steps grow with the block, not faster (N and 4N bytes, no tag).
	[TestMethod]
	public void Edit_OfABlockThatIsNotClassicTiff_ScansInLinearSteps ()
	{
		long Steps (int size)
		{
			var block = new byte[size];
			"XX\0*"u8.CopyTo (block);
			var file = Open (InCarrier ("png", block), "a.png");
			file.Tag!.Comment = "New";
			IfdSteps.Count = 0;
			Assert.IsTrue (file.SaveToFile ("/a.png", new MockFileSystem ()).IsSuccess);
			return IfdSteps.Count;
		}

		var small = Steps (4096);
		var large = Steps (16384);
		Assert.IsTrue (small <= 2 * 4096, $"{small} steps for 4 KB");
		Assert.IsTrue (large <= 4.5 * small, $"{small} steps at 4 KB, {large} at 16 KB");
	}

	// The worst shapes for every walk over IFD pointers, N entries each, one UserComment to remove; little-endian.
	// "self": N 0x8769 entries at IFD0 itself. "one": N at one Exif IFD of N entries. "mix": half at IFD0, half
	// 0xA005 at one interoperability IFD. "many": N at N distinct one-entry Exif IFDs. "gps-mix": a third each of
	// 0x8769 at IFD0, 0xA005 at one interoperability IFD and 0x8825 at one GPS IFD.
	static byte[] ManyPointers (string shape, int n)
	{
		if (shape is "nested-chain" or "subifds" or "main-chain" or "cycles" or "subifd-arrays" or "subifd-arrays-varied")
			return NestedPointers (shape, n);
		const int Ifd0 = 8;
		var ifd0Entries = shape == "one" ? n : n + 1;
		var exif = Ifd0 + 2 + (12 * ifd0Entries) + 4;
		var exifBytes = shape switch { "one" => 2 + (12 * n) + 4, "many" => 18 * n, _ => 0 };
		var interop = exif + exifBytes;
		var gps = interop + 18;
		var value = gps + 18;
		var tiff = new TiffBuilder (value + OldOne.Length).Header (8);
		var comment = (0x9286, 7, OldOne.Length, value);
		switch (shape) {
			case "self":
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (_ => (0x8769, 4, 1, Ifd0)), comment]);
				break;
			case "one":
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (_ => (0x8769, 4, 1, exif))]);
				tiff.Entries (exif, [.. Enumerable.Range (0, n - 1).Select (k => (0x9003, 7, 4, k)), comment]);
				break;
			case "many":
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (k => (0x8769, 4, 1, exif + (18 * k))), comment]);
				for (var k = 0; k < n; k++)
					tiff.Entries (exif + (18 * k), (0x9003, 7, 4, k));
				break;
			case "gps-mix":
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (k => (k % 3) switch { 0 => (0x8769, 4, 1, Ifd0), 1 => (0xA005, 4, 1, interop), _ => (0x8825, 4, 1, gps) }), comment]);
				break;
			default:
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (k => k % 2 == 0 ? (0x8769, 4, 1, Ifd0) : (0xA005, 4, 1, interop)), comment]);
				break;
		}

		tiff.Entries (interop, (0x0001, 2, 4, 0x00383952));
		tiff.Entries (gps, (0x0001, 2, 2, 0x4E));
		return tiff.Bytes (value, OldOne).ToArray ();
	}

	// The shapes of the nested walk, N tables each, the comment in the last one reached: a chain of N Exif IFDs,
	// each pointing at the next ("nested-chain"); one 0x014A with N SubIFD offsets ("subifds"); IFD0 and N IFDs
	// after it through next-IFD offsets ("main-chain"); N Exif IFDs that each point at the next, at IFD0 and back at
	// IFD0 through their next offset ("cycles"); and N 0x014A entries that all name one array of N offsets to IFD0
	// ("subifd-arrays", N squared offsets to read, refused past the data's bound).
	static byte[] NestedPointers (string shape, int n)
	{
		const int Ifd0 = 8;
		var arrays = shape.StartsWith ("subifd-arrays", StringComparison.Ordinal);
		var first = Ifd0 + (arrays ? 2 + (12 * (n + 1)) + 4 : 2 + (12 * 3) + 4);
		var array = first;
		var tables = array + (shape == "subifds" || arrays ? 4 * n : 0);
		const int Size = 2 + (12 * 2) + 4;
		var value = tables + (arrays ? 0 : Size * n);
		var tiff = new TiffBuilder (value + OldOne.Length).Header (8).Bytes (value, OldOne);
		var comment = (0x9286, 7, OldOne.Length, value);
		var filler = (0x9003, 7, 4, 0x31303032);
		int At (int k) => tables + (Size * k);
		void Next (int at, int entries, int next) => tiff.Bytes (at + 2 + (12 * entries), BitConverter.GetBytes (next));
		switch (shape) {
			case "nested-chain":
				tiff.Entries (Ifd0, (0x8769, 4, 1, At (0)), filler);
				for (var k = 0; k < n; k++)
					tiff.Entries (At (k), k == n - 1 ? comment : (0x8769, 4, 1, At (k + 1)), filler);
				break;
			case "subifds":
				tiff.Entries (Ifd0, (0x014A, 4, n, array), filler);
				for (var k = 0; k < n; k++) {
					tiff.Bytes (array + (4 * k), BitConverter.GetBytes (At (k)));
					tiff.Entries (At (k), k == n - 1 ? comment : filler, filler);
				}

				break;
			case "main-chain":
				tiff.Entries (Ifd0, filler, filler);
				Next (Ifd0, 2, At (0));
				for (var k = 0; k < n; k++) {
					tiff.Entries (At (k), k == n - 1 ? comment : filler, filler);
					if (k < n - 1)
						Next (At (k), 2, At (k + 1));
				}

				break;
			case "cycles":
				tiff.Entries (Ifd0, (0x8769, 4, 1, At (0)), (0x014A, 4, 1, At (0)), comment);
				Next (Ifd0, 3, At (0));
				for (var k = 0; k < n; k++) {
					tiff.Entries (At (k), (0x8769, 4, 1, At ((k + 1) % n)), (0x014A, 4, 1, Ifd0));
					Next (At (k), 2, Ifd0);
				}

				break;
			case "subifd-arrays-varied":
				// Each entry names the same array with another length: about N squared / 2 offsets, past the bound.
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (k => (0x014A, 4, k + 2, array)), comment]);
				for (var k = 0; k < n; k++)
					tiff.Bytes (array + (4 * k), BitConverter.GetBytes (Ifd0));
				break;
			default:
				tiff.Entries (Ifd0, [.. Enumerable.Range (0, n).Select (_ => (0x014A, 4, n, array)), comment]);
				for (var k = 0; k < n; k++)
					tiff.Bytes (array + (4 * k), BitConverter.GetBytes (Ifd0));
				break;
		}

		return tiff.ToArray ();
	}

	static byte[] ManyPointersFile (string carrier, string shape, int n) => carrier == "png"
		? Png (PngChunk ("eXIf", ManyPointers (shape, n)), Text ("Comment", "c"))
		: Jpeg (JpegExif (ManyPointers (shape, n)), JpegSegment (0xFE, "c"u8.ToArray ()));

	static long EditSteps (string carrier, string shape, int n)
	{
		var file = Open (ManyPointersFile (carrier, shape, n), carrier == "png" ? "a.png" : "a.jpg");
		file.Tag!.Comment = "New";
		IfdSteps.Count = 0;
		var output = file.Render ().ToArray ();
		Assert.IsTrue (output.AsSpan ().IndexOf ("old comment one"u8) < 0, $"{carrier} {shape} N={n}: the comment is removed");
		return IfdSteps.Count;
	}

	// However many IFD0 entries point at a table, a Comment edit walks it once: the steps and the allocation
	// grow linearly (with a sort's log factor) in the entries, for PNG eXIf and JPEG APP1 (capped at 64 KB).
	[TestMethod]
	[DataRow ("png", "self", 1000)]
	[DataRow ("png", "one", 1000)]
	[DataRow ("png", "mix", 1000)]
	[DataRow ("jpeg", "self", 1000)]
	[DataRow ("jpeg", "one", 500)]
	[DataRow ("jpeg", "mix", 1000)]
	public void Edit_ManyPointersToOneTable_IsLinearInTheEntries (string carrier, string shape, int n)
	{
		var small = EditSteps (carrier, shape, n);
		var large = EditSteps (carrier, shape, 4 * n);
		Assert.IsTrue (large <= 40L * 4 * n, $"{carrier} {shape}: {large} steps for {4 * n} pointers");
		Assert.IsTrue (large < 6 * small, $"{carrier} {shape}: 4x the pointers took {large / (double)small:F1}x the steps");
		Bounds.AssertLinearAllocation (size => ManyPointersFile (carrier, shape, size), data => {
			var file = Open (data, carrier == "png" ? "a.png" : "a.jpg");
			file.Tag!.Comment = "New";
			_ = file.Render ();
		}, n, runs: 3);
	}

	// A version-65536 Apple gain map behind the primary, so Clear walks the Exif for the maker-note headroom.
	static byte[] AppleGainMapImage () => Jpeg (JpegXmp ("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:HDRGainMap='http://ns.apple.com/HDRGainMap/1.0/' HDRGainMap:HDRGainMapVersion='65536'/></rdf:RDF></x:xmpmeta>"));

	static byte[] WalkInput (string carrier, string shape, int n) => carrier == "png"
		? Png (PngChunk ("eXIf", ManyPointers (shape, n)), Text ("Comment", "c"))
		: MpfJpeg (Concat (JpegExif (ManyPointers (shape, n)), JpegSegment (0xFE, "c"u8.ToArray ())), [], AppleGainMapImage (), out _);

	static void RunWalk (string op, byte[] data, string name)
	{
		var file = Open (data, name);
		if (op == "read") {
			Assert.IsNotNull (file.ImageProperties);
			return;
		}

		if (op == "clear")
			file.Tag!.Clear ();
		else
			file.Tag!.Comment = "New";
		_ = file.Render ();
	}

	// Every walk over IFD pointers and tables (Clear with its maker-note headroom walk, the Comment edit) stays
	// linear in the entries for every worst shape, in PNG eXIf and in a JPEG APP1 behind an Apple gain map: the
	// step count at 4N is under 6x the count at N, and the allocation grows linearly over N, 2N and 4N. The read
	// rows pin that opening such a PNG or JPEG stays linear; it walks no IFD (Exif values are not read). The TIFF
	// and DNG IFD walk is pinned by Tiff_IfdChainEndingWithoutANextOffset_IsReadLinearly and by the "tiff IFD chain"
	// row of ReadAndRewrite_AllocateLinearly.
	public static IEnumerable<object[]> Walks ()
	{
		foreach (var op in new[] { "read", "clear", "edit" }) {
			foreach (var shape in new[] { "self", "one", "mix", "many", "gps-mix", "nested-chain", "subifds", "main-chain", "cycles", "subifd-arrays", "subifd-arrays-varied" }) {
				yield return [op, "png", shape, 600];
				yield return [op, "jpeg", shape, 300];
			}
		}
	}

	[TestMethod]
	[DynamicData (nameof (Walks))]
	public void EveryIfdWalk_IsLinearInTheEntries (string op, string carrier, string shape, int n)
	{
		var name = carrier == "png" ? "a.png" : "a.jpg";
		long Steps (int size)
		{
			var data = WalkInput (carrier, shape, size);
			RunWalk (op, data, name);
			IfdSteps.Count = 0;
			RunWalk (op, data, name);
			return IfdSteps.Count;
		}

		var small = Steps (n);
		var large = Steps (4 * n);
		var row = $"{op} {carrier} {shape}";
		if (op == "edit" || (op == "clear" && carrier == "jpeg"))
			Assert.IsTrue (small > 0, $"{row}: the walk ran");
		// 54 is the measured cost of a successful edit: the plainness pass, marking ranges, and the
		// removed-versus-protected check. jpeg cycles at 1,200 entries took 63,678 steps.
		Assert.IsTrue (large <= 54L * 4 * n, $"{row}: {large} steps for {4 * n} entries");
		Assert.IsTrue (large <= Math.Max (6 * small, 64), $"{row}: 4x the entries took {large / (double)Math.Max (small, 1):F1}x the steps");
		Bounds.AssertLinearAllocation (size => WalkInput (carrier, shape, size), data => RunWalk (op, data, name), n, runs: 3);

		// The edit reaches every copy the reader model sees, however deep the table that holds it.
		if (op == "edit") {
			var input = WalkInput (carrier, shape, n);
			var file = Open (input, name);
			file.Tag!.Comment = "New";
			var output = file.Render ().ToArray ();
			Assert.AreEqual (1, UserCommentsInEveryExifIfd (ExifOf (input)).Count, $"{row}: the input holds one copy");
			if (shape == "subifd-arrays-varied")
				CollectionAssert.AreEqual (input, output, $"{row}: past the bound on SubIFD offsets, refused");
			else
				Assert.AreEqual (0, UserCommentsInEveryExifIfd (ExifOf (output)).Count, $"{row}: no copy is left");
		}
	}

	// N tables share one strip, tile or thumbnail array of N values. A comment edit refuses that structure before
	// the values are read, in linear steps. Title removes nothing and does not read them either.
	[TestMethod]
	[DataRow ("png", "comment", "strips")]
	[DataRow ("png", "title", "strips")]
	[DataRow ("jpeg", "comment", "strips")]
	[DataRow ("jpeg", "title", "strips")]
	[DataRow ("png", "comment", "tiles")]
	[DataRow ("png", "comment", "thumb")]
	[DataRow ("png", "comment", "chain")]
	public void Edit_SharedStripArray_IsLinearInTheTables (string carrier, string field, string shape)
	{
		const int n = 250;
		var small = SharedStripSteps (carrier, field, shape, n);
		var large = SharedStripSteps (carrier, field, shape, 4 * n);
		var row = $"{field} {carrier} {shape}";
		Assert.IsTrue (small > 0, $"{row}: the walk ran");
		// The refusal walks each table and does not read the shared array. 320 steps a table is above that.
		Assert.IsTrue (large <= 320L * 4 * n, $"{row}: {large} steps for {4 * n} tables");
		Assert.IsTrue (large <= Math.Max (6 * small, 64), $"{row}: 4x the tables took {large / (double)small:F1}x the steps ({small} -> {large})");
		Bounds.AssertLinearAllocation (size => SharedStripFile (carrier, shape, size), data => RunSharedStrip (field, data, carrier == "png" ? "a.png" : "a.jpg"), n, runs: 3);
	}

	// The reported sizes: about 85 KB at 2,000 tables and 169 KB at 4,000, each table sharing one strip array.
	// Recording a range per table exhausts a 1 GB heap at the larger one. The edit refuses and leaves the comment.
	[TestMethod]
	[DataRow (2000)]
	[DataRow (4000)]
	public void Edit_SharedStripArray_AtTheReportedSizes_RefusesTheComment (int n)
	{
		var data = SharedStripFile ("png", "strips", n);
		Assert.IsTrue (data.Length > (n == 2000 ? 80_000 : 160_000), $"N={n}: {data.Length} bytes");
		var output = RunSharedStrip ("comment", data, "a.png");
		Assert.AreEqual (1, UserCommentsInEveryExifIfd (ExifOf (output)).Count, $"N={n}: the comment stays");
	}

	static byte[] SharedStripFile (string carrier, string shape, int n) => carrier == "png"
		? Png (PngChunk ("eXIf", SharedStripTiff (shape, n)), Text ("Comment", "c"))
		: Jpeg (JpegExif (SharedStripTiff (shape, n)), JpegSegment (0xFE, "c"u8.ToArray ()));

	static long SharedStripSteps (string carrier, string field, string shape, int n)
	{
		var data = SharedStripFile (carrier, shape, n);
		IfdSteps.Count = 0;
		var output = RunSharedStrip (field, data, carrier == "png" ? "a.png" : "a.jpg");
		var comments = UserCommentsInEveryExifIfd (ExifOf (output)).Count;
		Assert.AreEqual (1, comments, $"{field} {carrier} {shape} N={n}: the shared array is not plain, and a title removes nothing");
		return IfdSteps.Count;
	}

	static byte[] RunSharedStrip (string field, byte[] data, string name)
	{
		// JPEG stores only the comment, so assigning Title never reaches the editor. Measure that walk on the TIFF.
		if (field == "title" && name.EndsWith (".jpg", StringComparison.Ordinal)) {
			var tiff = ExifOf (data);
			var tag = new ImageTag (MediaFormat.Jpeg, (ImageStores)0x3F);
			tag.ReadField (ImageStores.Title, "Old");
			tag.Title = "New";
			Assert.IsTrue (ExifEditor.TryRemove (tiff, 0, tag, "jpeg APP1", out var changed, out var error), error);
			Assert.IsFalse (changed, "jpeg title: the TIFF holds no title");
			return data;
		}

		var file = Open (data, name);
		if (field == "comment")
			file.Tag!.Comment = "New";
		else
			file.Tag!.Title = "New";
		var save = file.SaveToFile ("/out", new MockFileSystem ());
		if (field == "comment") {
			Assert.IsFalse (save.IsSuccess, save.Error);
			StringAssert.Contains (save.Error, "more than once");
			return data;
		}

		Assert.IsTrue (save.IsSuccess, save.Error);
		return file.Render ().ToArray ();
	}

	// IFD0 points at an Exif IFD that holds the UserComment, and at N tables that share one N-value offset array
	// and one N-value length array: a SubIFD array, or a next-IFD chain.
	static byte[] SharedStripTiff (string shape, int n)
	{
		var (offTag, lenTag) = shape switch {
			"tiles" => (0x0144, 0x0145),
			"thumb" => (0x0201, 0x0202),
			_ => (0x0111, 0x0117),
		};
		var chain = shape == "chain";
		const int Ifd0 = 8;
		var ifd0Entries = chain ? 1 : 2;
		var exifAt = Ifd0 + 2 + (12 * ifd0Entries) + 4;
		var arrayAt = exifAt + 18;
		var tablesAt = chain ? arrayAt : arrayAt + (4 * n);
		const int TableSize = 30;
		var offsetsAt = tablesAt + (TableSize * n);
		var lengthsAt = offsetsAt + (4 * n);
		var commentAt = lengthsAt + (4 * n);
		var tiff = new TiffBuilder (commentAt + OldOne.Length).Header (Ifd0);
		if (chain)
			tiff.Table (Ifd0, [(0x8769, 4, 1, exifAt)], next: tablesAt);
		else
			tiff.Table (Ifd0, [(0x8769, 4, 1, exifAt), (0x014A, 4, n, arrayAt)]);
		tiff.Table (exifAt, [(0x9286, 7, OldOne.Length, commentAt)]);
		for (var i = 0; i < n; i++) {
			var at = tablesAt + (TableSize * i);
			var next = chain && i + 1 < n ? at + TableSize : 0L;
			tiff.Table (at, [(offTag, 4, n, offsetsAt), (lenTag, 4, n, lengthsAt)], next);
			if (!chain)
				tiff.U32 (arrayAt + (4 * i), at);
		}

		for (var k = 0; k < n; k++)
			tiff.U32 (offsetsAt + (4 * k), 8).U32 (lengthsAt + (4 * k), 4);
		return tiff.Bytes (commentAt, OldOne).ToArray ();
	}
}
