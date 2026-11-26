// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Image.Jpeg;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// The rules of the MP index (CIPA DC-007 §5.2): where it is found, when it can be followed, and how its entries
/// are rewritten. Image starts are the MPImageStart values exiftool shows for the same files (measured).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class MpfIndexTests
{
	static readonly byte[] Two = Jpeg (JpegSegment (0xFE, "two"u8.ToArray ()));
	static readonly byte[] Three = Jpeg (JpegSegment (0xFE, "three"u8.ToArray ()));

	/// <summary>How a test MP header is laid out.</summary>
	internal sealed record Layout
	{
		public string Mark { get; init; } = "MM\0*";
		public bool VersionFirst { get; init; }
		public bool EntryTag { get; init; } = true;
		public int ListShift { get; init; }
		public int Tail { get; init; }
		public bool Reverse { get; init; }
		public byte[] Before { get; init; } = [];
		public byte[] Between { get; init; } = [];
		public int Next { get; init; }
		public int ExtraEntries { get; init; }
		public bool? LittleEndianValues { get; init; }
		public byte[] Scan { get; init; } = [0x12, 0x34];
	}

	// A multi-picture file: the first image, whose APP2 "MPF" segment holds the MP header laid out as asked, then the
	// later images; the MP entries hold each image's size and offset from the MP header (0 for the first).
	internal static byte[] File (Layout layout, params byte[][] later)
	{
		var le = layout.LittleEndianValues ?? layout.Mark.StartsWith ("II", StringComparison.Ordinal);
		var count = later.Length + 1;
		var tags = (layout.VersionFirst ? 1 : 0) + (layout.EntryTag ? 1 : 0);
		var ifdEnd = 8 + 2 + (12 * tags) + 4;
		var list = ifdEnd + layout.ListShift;
		var mp = new byte[Math.Max (ifdEnd, list + (16 * count)) + layout.Tail];
		System.Text.Encoding.Latin1.GetBytes (layout.Mark).CopyTo (mp, 0);
		void U16 (int at, int value) { if (le) BinaryPrimitives.WriteUInt16LittleEndian (mp.AsSpan (at), (ushort)value); else BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (at), (ushort)value); }
		void U32 (int at, int value) { if (le) BinaryPrimitives.WriteInt32LittleEndian (mp.AsSpan (at), value); else BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (at), value); }
		U32 (4, 8);
		U16 (8, tags);
		var entry = 10;
		if (layout.VersionFirst) {
			U16 (entry, 0xB000);
			U16 (entry + 2, 7);
			U32 (entry + 4, 4);
			"0100"u8.CopyTo (mp.AsSpan (entry + 8));
			entry += 12;
		}

		if (layout.EntryTag) {
			U16 (entry, 0xB002);
			U16 (entry + 2, 7);
			U32 (entry + 4, 16 * (count + layout.ExtraEntries));
			U32 (entry + 8, list);
			entry += 12;
		}

		U32 (entry, layout.Next);

		var first = Concat ([0xFF, 0xD8], layout.Before, JpegSegment (0xE2, Concat ("MPF\0"u8.ToArray (), mp)), layout.Between,
			JpegSegment (0xC0, [0x08, 0x00, 0x03, 0x00, 0x02, 0x01, 0x01, 0x11, 0x00]),
			JpegSegment (0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]), layout.Scan, [0xFF, 0xD9]);
		var header = 2 + layout.Before.Length + 4 + 4;
		void Entry (int n, int size, int offset)
		{
			var at = header + list + (16 * n);
			if (le) {
				BinaryPrimitives.WriteInt32LittleEndian (first.AsSpan (at + 4), size);
				BinaryPrimitives.WriteInt32LittleEndian (first.AsSpan (at + 8), offset);
			} else {
				BinaryPrimitives.WriteInt32BigEndian (first.AsSpan (at + 4), size);
				BinaryPrimitives.WriteInt32BigEndian (first.AsSpan (at + 8), offset);
			}
		}

		if (layout.EntryTag && list + (16 * count) <= mp.Length) {
			Entry (0, first.Length, 0);
			var start = first.Length;
			for (var n = 0; n < later.Length; n++) {
				Entry (layout.Reverse ? later.Length - n : n + 1, later[n].Length, start - header);
				start += later[n].Length;
			}
		}

		return Concat ([first, .. later]);
	}

	static int Header (byte[] file) => file.AsSpan ().IndexOf ("MPF\0"u8) + 4;

	[TestMethod]
	[DataRow ("big-endian, MPFVersion first", "MM\0*", true, false, new[] { 1, 123, 39, 2, 162, 41 })]
	[DataRow ("little-endian", "II*\0", false, false, new[] { 1, 111, 39, 2, 150, 41 })]
	[DataRow ("entries in reverse file order", "MM\0*", false, true, new[] { 2, 111, 39, 1, 150, 41 })]
	public void Read_ListsTheLaterImages_WhereExiftoolShowsThem_InFileOrder (string shape, string mark, bool versionFirst, bool reverse, int[] expected)
	{
		var file = File (new Layout { Mark = mark, VersionFirst = versionFirst, Reverse = reverse }, Two, Three);

		var index = MpfIndex.Read (file);

		Assert.IsFalse (index.Unreadable, shape);
		CollectionAssert.AreEqual (expected, index.Images.SelectMany (i => new[] { i.Entry, i.Start, i.Size }).ToArray (), shape);
	}

	// An MP header that cannot be followed makes the index unreadable: a byte order other than "II" or "MM", too
	// few bytes for the header and the IFD offset, or an entry list that starts in the header or ends past the
	// segment.
	[TestMethod]
	[DataRow ("XX\0*", 0, 0)]
	[DataRow ("IM*\0", 0, 0)]
	[DataRow ("MI\0*", 0, 0)]
	[DataRow ("MM\0*", -16, 0)]
	[DataRow ("MM\0*", 0, 1)]
	public void Read_OfAnIndexThatCannotBeFollowed_IsUnreadable (string mark, int listShift, int extraEntries)
	{
		var file = File (new Layout { Mark = mark, ListShift = listShift, ExtraEntries = extraEntries }, Two);

		Assert.IsTrue (MpfIndex.Read (file).Unreadable);
	}

	[TestMethod]
	public void Read_OfAnMpHeaderTooShortForItsIfdOffset_IsUnreadable ()
	{
		var file = Concat ([0xFF, 0xD8], JpegSegment (0xE2, Concat ("MPF\0"u8.ToArray (), "MM\0*\0\0\0"u8.ToArray ())), Jpeg ()[2..]);

		Assert.IsTrue (MpfIndex.Read (file).Unreadable);
	}

	// An index without an MP Entry tag lists no image; one whose IFD ends where the next bytes read B0 02 (an MP
	// Entry tag) still has none, since an IFD holds only its counted entries.
	[TestMethod]
	[DataRow (0)]
	[DataRow (unchecked((int)0xB0020007))]
	public void Read_OfAnIndexWithoutAnMpEntryTag_ListsNoImage (int next)
	{
		var file = File (new Layout { VersionFirst = true, EntryTag = false, Next = next }, Two);

		var index = MpfIndex.Read (file);

		Assert.IsFalse (index.Unreadable);
		Assert.AreEqual (0, index.Images.Count);
	}

	// Fill bytes (FF FF) and a standalone marker (TEM, FF 01) before the APP2 segment are stepped over.
	[TestMethod]
	public void Read_FindsTheIndexAfterFillBytesAndAStandaloneMarker ()
	{
		var file = File (new Layout { Before = [0xFF, 0xFF, 0x01] }, Two);

		var index = MpfIndex.Read (file);

		Assert.AreEqual (1, index.Images.Count);
		Assert.AreEqual (Header (file) + (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (Header (file) + 26 + 16 + 8)), index.Images[0].Start);
	}

	[TestMethod]
	public void Read_OfAFileThatEndsWithTheMpfSegment_IsUnreadable ()
	{
		var file = File (new Layout (), Two);
		var end = Header (file) - 8 + 2 + ((file[Header (file) - 6] << 8) | file[Header (file) - 5]);

		Assert.IsTrue (MpfIndex.Read (file.AsSpan (0, end)).Unreadable);
	}

	// The smallest JPEG, SOI then EOI, is an image of 4 bytes.
	[TestMethod]
	public void Read_ALaterImageOfFourBytes_IsAnImage ()
	{
		var index = MpfIndex.Read (File (new Layout (), [0xFF, 0xD8, 0xFF, 0xD9]));

		Assert.IsFalse (index.Unreadable);
		Assert.AreEqual (4, index.Images.Single ().Size);
	}

	// Each entry's new offset and size must fit the 4-byte fields: a negative or larger value is refused.
	[TestMethod]
	[DataRow (-1L, 0L)]
	[DataRow (1L + uint.MaxValue, 0L)]
	[DataRow (0L, -1L)]
	[DataRow (0L, 1L + uint.MaxValue)]
	[DataRow (0L, (long)uint.MaxValue)]
	public void RewriteEntries_RefusesAValueTheFieldsCannotHold (long offset, long size)
	{
		var file = File (new Layout (), Two);
		var header = Header (file);
		var limit = header - 8 + 2 + ((file[header - 6] << 8) | file[header - 5]);

		var rewritten = MpfIndex.TryRewriteEntries (file, header, limit, (n, _, _) => n == 1 ? (offset, size) : (0, 1));

		Assert.AreEqual (size == uint.MaxValue, rewritten);
	}

	// Only the listed entries are rewritten: bytes after the list, inside the segment, are kept.
	[TestMethod]
	public void RewriteEntries_WritesOnlyTheListedEntries ()
	{
		var file = File (new Layout { Tail = 16 }, Two);
		var header = Header (file);
		var limit = header - 8 + 2 + ((file[header - 6] << 8) | file[header - 5]);
		var before = file[(limit - 16)..limit];

		Assert.IsTrue (MpfIndex.TryRewriteEntries (file, header, limit, (_, _, _) => (7, 9)));

		CollectionAssert.AreEqual (before, file[(limit - 16)..limit]);
		Assert.AreEqual (9u, BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (limit - 16 - 16 + 4)));
	}
	// "IM" is no byte order, even with little-endian values after it.
	[TestMethod]
	public void Read_OfAnIndexMarkedIm_IsUnreadable_ThoughItsValuesReadAsLittleEndian () =>
		Assert.IsTrue (MpfIndex.Read (File (new Layout { Mark = "IM*\0", LittleEndianValues = true }, Two)).Unreadable);

	// The first image ends at its EOI: a "JPEG" inside its entropy-coded data, after the SOS, overlaps it and is not
	// an image the index can list (Clear would cut the first image there).
	[TestMethod]
	public void Read_OfAnImageInsideTheFirstImagesScanData_IsUnreadable ()
	{
		var file = File (new Layout { Scan = [0x12, 0xFF, 0xD8, 0xFF, 0xD9, 0x34] }, Two);
		var header = Header (file);
		var inner = file.AsSpan ().IndexOf (new byte[] { 0x12, 0xFF, 0xD8 }) + 1;
		BinaryPrimitives.WriteInt32BigEndian (file.AsSpan (header + 26 + 16 + 4), 4);
		BinaryPrimitives.WriteInt32BigEndian (file.AsSpan (header + 26 + 16 + 8), inner - header);

		Assert.IsTrue (MpfIndex.Read (file).Unreadable);
	}

	// Before the APP2 segment: standalone markers RST0 and SOI are stepped over, an empty segment (length 2) is
	// passed, and an APP1 whose payload starts "MPF" is not the index.
	[TestMethod]
	[DataRow (new byte[] { 0xFF, 0xD0 })]
	[DataRow (new byte[] { 0xFF, 0xD8 })]
	[DataRow (new byte[] { 0xFF, 0xE1, 0x00, 0x02 })]
	[DataRow (new byte[] { 0xFF, 0xE1, 0x00, 0x0E, 0x4D, 0x50, 0x46, 0x00, 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08 })]
	public void Read_FindsTheIndexAfter (byte[] before)
	{
		var file = File (new Layout { Before = before }, Two);

		var index = MpfIndex.Read (file);

		Assert.IsFalse (index.Unreadable);
		Assert.AreEqual (file.Length - Two.Length, index.Images.Single ().Start);
	}

	// A segment length below 2 ends the marker walk (no index, nothing thrown), and so does an APP2 that runs past
	// the data.
	[TestMethod]
	public void Read_StopsAtASegmentLengthItCannotFollow ()
	{
		var file = File (new Layout { Before = [0xFF, 0xE1, 0x00, 0x00] }, Two);
		var index = MpfIndex.Read (file);
		Assert.IsFalse (index.Unreadable);
		Assert.AreEqual (0, index.Images.Count);

		byte[] cut = [0xFF, 0xD8, 0xFF, 0xE2, 0x7F, 0xFF, 0x4D, 0x50, 0x46, 0x00];
		Assert.AreEqual (0, MpfIndex.Read (cut).Images.Count);
	}

	// An APP2 "MPF" segment whose length runs 1 to 4 bytes past the data is cut short: it is not an index, and reading
	// it reads nothing past the data.
	[TestMethod]
	[DataRow (1)]
	[DataRow (2)]
	[DataRow (4)]
	public void Read_OfAnMpfSegmentThatRunsPastTheData_FindsNoIndex (int past)
	{
		byte[] data = [0xFF, 0xD8, 0xFF, 0xE2, 0x00, 0x00, 0x4D, 0x50, 0x46, 0x00, 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08];
		var length = data.Length - 4 + past;
		data[4] = (byte)(length >> 8);
		data[5] = (byte)length;

		var index = MpfIndex.Read (data);

		Assert.IsFalse (index.Unreadable);
		Assert.AreEqual (0, index.Images.Count);
	}

	// The 4-byte fields take 0 to 2^32 - 1.
	[TestMethod]
	[DataRow ((long)uint.MaxValue, 0L)]
	[DataRow (0L, 0L)]
	public void RewriteEntries_WritesTheFieldsLimits (long offset, long size)
	{
		var file = File (new Layout (), Two);
		var header = Header (file);
		var limit = header - 8 + 2 + ((file[header - 6] << 8) | file[header - 5]);

		Assert.IsTrue (MpfIndex.TryRewriteEntries (file, header, limit, (n, _, _) => n == 1 ? (offset, size) : (0, 1)));
		Assert.AreEqual ((uint)offset, BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (header + 26 + 16 + 8)));
	}

	// An entry list that starts in the 8-byte MP header is not rewritten; one that starts right after it is.
	[TestMethod]
	[DataRow (4, false)]
	[DataRow (8, true)]
	public void RewriteEntries_OfAListStartingAt (int listAt, bool rewritten)
	{
		var file = File (new Layout (), Two);
		var header = Header (file);
		var limit = header - 8 + 2 + ((file[header - 6] << 8) | file[header - 5]);
		BinaryPrimitives.WriteInt32BigEndian (file.AsSpan (header + 18), listAt);
		var before = file.ToArray ();

		Assert.AreEqual (rewritten, MpfIndex.TryRewriteEntries (file, header, limit, (_, _, _) => (7, 9)));
		if (!rewritten)
			CollectionAssert.AreEqual (before, file);
	}
	// The index is the first APP2 "MPF" segment: a second one, whose own index lists only the first image, is not it.
	[TestMethod]
	public void Read_UsesTheFirstMpfSegment ()
	{
		var mp = new byte[8 + 2 + 12 + 4 + 16];
		"MM\0*"u8.CopyTo (mp);
		BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (4), 8);
		BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (8), 1);
		BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (10), 0xB002);
		BinaryPrimitives.WriteUInt16BigEndian (mp.AsSpan (12), 7);
		BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (14), 16);
		BinaryPrimitives.WriteInt32BigEndian (mp.AsSpan (18), 26);
		var file = File (new Layout { Between = JpegSegment (0xE2, Concat ("MPF\0"u8.ToArray (), mp)) }, Two);

		Assert.AreEqual (1, MpfIndex.Read (file).Images.Count);
	}
}
