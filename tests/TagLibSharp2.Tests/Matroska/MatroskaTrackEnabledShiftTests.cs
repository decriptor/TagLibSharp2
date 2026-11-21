// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// A track-flag save that moves later elements. Invariant: every Seek that named an element before
/// still names it, every Cues position follows its element at the width it needs, and a FlagEnabled
/// that is added leaves a DocTypeVersion of at least 2 (RFC 9559 sections 5.1.4.1.4, 6.3, 7, 16).
/// </summary>
public partial class MatroskaFileTests
{
	[TestMethod]
	public void Save_InsertWithoutAVoid_SeekThatCrossesAWidthBoundary_KeepsIt ()
	{
		// The Cues Seek holds 65534 in two bytes; three more octets need a third.
		var original = GrowLayout (cuesAt: 0xFFFE, secondClusterAt: -1, out _);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		AssertMovedCorrectly (original, saved);
		Assert.IsTrue (ReadSeeks (saved).Exists (entry => entry.Id == MatroskaIds.Cues && entry.Position == 0x10001));
	}

	[TestMethod]
	public void Save_InsertWithoutAVoid_CueThatCrossesAWidthBoundary_Widens ()
	{
		// The second Cluster is at 65533 with a two-byte CueClusterPosition.
		var original = GrowLayout (cuesAt: -1, secondClusterAt: 0xFFFD, out var markers);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		AssertSeeksLand (original, saved);
		var cues = CueValues (saved)[0xF1];
		Assert.AreEqual (0x10000ul, cues[1]);
		Assert.IsTrue (IsElementAt (saved, cues[1], MatroskaIds.Cluster));
		foreach (var marker in markers)
			Assert.AreEqual (1, CountOf (saved, marker));
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_InsertWithoutAVoid_MovesCueCodecStateAndCueRefCluster ()
	{
		// CueCodecState (RFC 9559 §5.1.5.1.2.6) and CueRefCluster (§A.37) are Segment Positions.
		var info = Master (0x1549A966, Element (0x7BA9, "Codec"u8));
		var tracks = Master (0x1654AE6B, AudioEntry (1, 22050));
		var headLength = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), Seek2 (MatroskaIds.Tracks, 0), Seek2 (MatroskaIds.Cues, 0))).Length;
		var first = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x01 })));
		var second = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 1 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x02 })));
		var firstAt = headLength + info.Length + tracks.Length;
		var secondAt = firstAt + first.Length;
		var cuesAt = secondAt + second.Length;
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 1 }),
			Master (0xB7, Concat (
				Element (0xF7, new byte[] { 1 }),
				Element (0xF1, Two (secondAt)),
				Element (0xEA, Two (secondAt)),
				Master (0xDB, Concat (Element (0x96, new byte[] { 0 }), Element (0x97, Two (firstAt)))))))));
		var head = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, (ulong)headLength), Seek2 (MatroskaIds.Tracks, (ulong)(headLength + info.Length)), Seek2 (MatroskaIds.Cues, (ulong)cuesAt)));
		var original = TrackFile (head, info, tracks, first, second, cues);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		AssertMovedCorrectly (original, saved);
		var values = CueValues (saved);
		Assert.AreEqual ((ulong)secondAt + 3, values[0xF1][0]);
		Assert.AreEqual ((ulong)secondAt + 3, values[0xEA][0]);
		Assert.AreEqual ((ulong)firstAt + 3, values[0x97][0]);
		Assert.IsTrue (IsElementAt (saved, values[0x97][0], MatroskaIds.Cluster));
	}

	[TestMethod]
	public void Save_UnknownSizeTracksWithoutRoom_SaysWhy ()
	{
		// The only Tracks has an unknown size, so it cannot be grown.
		var info = Master (0x1549A966, Element (0x7BA9, "U"u8));
		var original = TrackFile (info, Unknown (0x1654AE6B, AudioEntry (1, 22050)), Master (0x1F43B675, ClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual (1, parsed.Tracks.Count);
		parsed.Tracks[0].IsEnabled = false;

		AssertFailure (parsed, new MockFileSystem (), "/unknown.mkv", original, "unknown size");
	}

	[TestMethod]
	public void Save_InsertedFlag_AddsAMissingDocTypeVersion ()
	{
		// An omitted DocTypeVersion is 1 (RFC 8794 §11.2.7). FlagEnabled has minver 2, and RFC 9559
		// section 7 requires DocTypeVersion to be at least the highest version of any element present.
		// The EBML header is outside the Segment, so growing it moves no Segment Position.
		var header = WithCrc (0x1A45DFA3, Concat (Element (0x4282, "matroska"u8), Element (0x4285, new byte[] { 1 })));
		var original = Concat (header, Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050)), MakeVoid (3), Master (0x1F43B675, ClusterMarker)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 4, saved.Length);
		AssertDocTypeVersion (saved, 2);
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
		Assert.AreEqual (IndexOf (original, ClusterMarker) - SegmentBodyAt (original), IndexOf (saved, ClusterMarker) - SegmentBodyAt (saved), "no Segment Position moved");
	}

	[TestMethod]
	public void Save_InsertedFlag_ReplacesAnEmptyDocTypeVersion ()
	{
		// An empty DocTypeVersion takes its default of 1 (RFC 8794 §6.1).
		var header = Master (0x1A45DFA3, Concat (Element (0x4282, "matroska"u8), Element (0x4287, Array.Empty<byte> ())));
		var original = Concat (header, Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050)), MakeVoid (3), Master (0x1F43B675, ClusterMarker)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 1, saved.Length);
		AssertDocTypeVersion (saved, 2);
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_InsertedFlag_WidensTheEbmlHeaderSize ()
	{
		// 123 header octets fit a one-octet size; the 4-octet DocTypeVersion makes 127, which needs two
		// (RFC 8794 section 6.1: all ones is reserved). The file grows by 5 and the size reads 40 7F.
		var docType = Element (0x4282, "matroska"u8);
		var filler = Element (0xEC, new byte[123 - docType.Length - 2]);
		var header = Master (0x1A45DFA3, Concat (docType, filler));
		Assert.AreEqual (4 + 1 + 123, header.Length);
		var original = Concat (header, Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050)), MakeVoid (3), Master (0x1F43B675, ClusterMarker)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 5, saved.Length);
		CollectionAssert.AreEqual (new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x40, 0x7F }, saved.AsSpan (0, 6).ToArray ());
		AssertDocTypeVersion (saved, 2);
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_InsertedFlag_ReplacesAWideEmptyDocTypeVersion ()
	{
		// An empty DocTypeVersion with an eight-octet size (10 octets) becomes 42 87 81 02, so the
		// header shrinks by 6.
		var wideEmpty = new byte[] { 0x42, 0x87, 0x01, 0, 0, 0, 0, 0, 0, 0 };
		var header = Master (0x1A45DFA3, Concat (Element (0x4282, "matroska"u8), wideEmpty));
		var original = Concat (header, Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050)), MakeVoid (3), Master (0x1F43B675, ClusterMarker)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length - 6, saved.Length);
		AssertDocTypeVersion (saved, 2);
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_InPlaceFlag_LeavesAMissingDocTypeVersion ()
	{
		// The flag was already stored, so the file already declared what it needed.
		var header = Master (0x1A45DFA3, Element (0x4282, "matroska"u8));
		var original = Concat (header, Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050, Enabled (1)))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (1, DifferingBytes (original, saved));
	}

	[TestMethod]
	public void Save_InsertedFlag_RaisesEveryDocTypeVersionCopy ()
	{
		// DocTypeVersion has maxOccurs 1 (RFC 8794 §11.2.7), so two copies are invalid. Every copy below 2
		// is raised, as every FlagEnabled copy is written, so no reader is left with an old value.
		foreach (var second in new[] { new byte[] { 0x42, 0x87, 0x81, 0x04 }, new byte[] { 0x42, 0x87, 0x80 } }) {
			var header = Master (0x1A45DFA3, Concat (Element (0x4282, "matroska"u8), Element (0x4287, new byte[] { 1 }), second));
			var original = Concat (header, Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050)), MakeVoid (3), Master (0x1F43B675, ClusterMarker)));
			var parsed = MatroskaFile.Read (original).File!;
			parsed.Tracks[0].IsEnabled = false;

			var saved = Save (parsed, original);

			var copies = DocTypeVersions (saved);
			Assert.AreEqual (2, copies.Count);
			Assert.AreEqual (2ul, copies[0]);
			Assert.AreEqual (second.Length == 3 ? 2ul : 4ul, copies[1]);
			Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
		}
	}

	static List<ulong> DocTypeVersions (byte[] file)
	{
		Assert.IsTrue (EbmlReader.TryRead (file, 0, file.Length, out var header));
		var found = new List<ulong> ();
		var offset = header.DataOffset;
		while (offset < header.End && EbmlReader.TryRead (file, offset, header.End, out var child)) {
			if (child.Id == MatroskaIds.DocTypeVersion)
				found.Add (child.DataLength == 0 ? 1 : EbmlReader.ReadUInt (file.AsSpan (child.DataOffset, (int)child.DataLength)));
			offset = child.End;
		}

		return found;
	}

	static void AssertDocTypeVersion (byte[] file, ulong expected)
	{
		Assert.IsTrue (EbmlReader.TryRead (file, 0, file.Length, out var header));
		ulong? found = null;
		var offset = header.DataOffset;
		while (offset < header.End && EbmlReader.TryRead (file, offset, header.End, out var child)) {
			if (child.Id == MatroskaIds.DocTypeVersion)
				found = EbmlReader.ReadUInt (file.AsSpan (child.DataOffset, (int)child.DataLength));
			offset = child.End;
		}

		Assert.AreEqual (expected, found);
		if (EbmlReader.TryRead (file, header.DataOffset, header.End, out var first) && first.Id == MatroskaIds.Crc32)
			Assert.IsTrue (CrcMatches (file.AsSpan (header.DataOffset, (int)header.DataLength)), "EBML header CRC-32");
	}
}
