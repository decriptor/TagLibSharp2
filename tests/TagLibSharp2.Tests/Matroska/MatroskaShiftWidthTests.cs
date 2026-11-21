// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Invariant: when a save moves elements, every Segment Position (RFC 9559 §16) still names its
/// element, at whatever width it now needs. No Seek is dropped and no value is truncated.
/// Positions live in SeekPosition, CueClusterPosition, CueCodecState, CueRefCluster,
/// CueRefCodecState, and Cluster <c>Position</c>.
/// </summary>
public partial class MatroskaFileTests
{
	static readonly byte[] KeptSeekHeadChild = { 0x55, 0x55, 0x82, 0x0A, 0x0B };

	[TestMethod]
	public void Save_Move_WidensACueClusterPositionBeforeTheClusters ()
	{
		// A 70 KB cover goes right after the full SeekHead. The Cues sit before the Cluster with a
		// two-byte CueClusterPosition, which must widen; its growth moves the Cluster again.
		var original = MoveLayout (cuesFirst: true, out var marker);
		var parsed = MatroskaFile.Read (original).File!;
		var cover = new byte[70000];
		cover[0] = 0xFF;
		cover[1] = 0xD8;
		parsed.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", cover)];

		var bytes = Save (parsed, original);

		AssertSeeksPreserved (original, bytes);
		AssertEverySeekAndCue (bytes);
		Assert.IsTrue (ReadSeeks (bytes).Exists (entry => entry.Id == MatroskaIds.Attachments && IsElementAt (bytes, entry.Position, MatroskaIds.Attachments)));
		Assert.IsTrue (TryReadCueClusterPosition (bytes, out var cue, out _));
		Assert.IsTrue (cue > 0xFFFF);
		Assert.AreEqual (cue, ClusterPositionOf (bytes));
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.IsTrue (bytes.AsSpan ().IndexOf (KeptSeekHeadChild) >= 0);
		Assert.AreEqual (70000, MatroskaFile.Read (bytes).File!.Tag.Pictures[0].PictureData.Length);
	}

	[TestMethod]
	public void Save_Move_LargeTitleKeepsItsSeek ()
	{
		// The Tags grow past 64 KB. They move after the SeekHead, and the Cues after the Cluster widen.
		var original = MoveLayout (cuesFirst: false, out var marker);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('T', 70000);

		var bytes = Save (parsed, original);

		AssertSeeksPreserved (original, bytes);
		AssertEverySeekAndCue (bytes);
		var tags = ReadSeeks (bytes).Find (entry => entry.Id == MatroskaIds.Tags);
		Assert.AreEqual (MatroskaIds.Tags, tags.Id);
		Assert.IsTrue (tags.Position < (ulong)(IndexOf (bytes, marker) - SegmentBodyAt (bytes)), "the Tags come before the Cluster");
		Assert.IsTrue (TryReadCueClusterPosition (bytes, out var cue, out _));
		Assert.AreEqual (cue, ClusterPositionOf (bytes));
		Assert.AreEqual (new string ('T', 70000), MatroskaFile.Read (bytes).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_Move_RemapsCueCodecStateAndCueReference ()
	{
		// CueCodecState is a Segment Position (RFC 9559 §5.1.5.1.2.6); 0 names the TrackEntry and stays 0.
		// CueRefCluster and CueRefCodecState (Appendix A.37, A.39) are Segment Positions too.
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var seekLength = WithCrc (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), SeekEntry (MatroskaIds.Tags, 1), Seek2 (MatroskaIds.Cluster, 0), Seek2 (MatroskaIds.Cues, 0))).Length;
		var tagsAt = seekLength + info.Length;
		var clusterAt = tagsAt + tags.Length;
		var cluster = ClusterWithPosition (clusterAt, new byte[300]);
		var cuesAt = clusterAt + cluster.Length;
		var seek = WithCrc (MatroskaIds.SeekHead, Concat (
			Seek2 (MatroskaIds.Info, (ulong)seekLength),
			SeekEntry (MatroskaIds.Tags, (ulong)tagsAt),
			Seek2 (MatroskaIds.Cluster, (ulong)clusterAt),
			Seek2 (MatroskaIds.Cues, (ulong)cuesAt)));
		var reference = Master (0xDB, Concat (
			Element (0x96, new byte[] { 1 }),
			Element (0x97, Two (clusterAt)),
			Element (0xEB, Two (clusterAt + 12))));
		var cues = WithCrc (MatroskaIds.Cues, Concat (
			WithCrc (0xBB, Concat (
				Element (0xB3, new byte[] { 0 }),
				Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, Two (clusterAt)), Element (0xEA, Two (clusterAt + 10)), reference)))),
			Master (0xBB, Concat (
				Element (0xB3, new byte[] { 5 }),
				Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, Two (clusterAt)), Element (0xEA, new byte[] { 0 })))))));
		var original = FileWith (seek, info, tags, cluster, cues);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('T', 200);

		var bytes = Save (parsed, original);

		AssertSeeksPreserved (original, bytes);
		AssertEverySeekAndCue (bytes);
		var values = CueValues (bytes);
		var moved = values[0xF1][0];
		Assert.AreNotEqual ((ulong)clusterAt, moved);
		CollectionAssert.AreEqual (new[] { moved, moved }, values[0xF1]);
		CollectionAssert.AreEqual (new[] { moved + 10, 0ul }, values[0xEA]);
		CollectionAssert.AreEqual (new[] { moved }, values[0x97]);
		CollectionAssert.AreEqual (new[] { moved + 12 }, values[0xEB]);
	}

	[TestMethod]
	public void Save_InsertBeforeAClusterAtZero_KeepsAZeroCodecState ()
	{
		// Without a SeekHead the Tags go before the first Cluster, which is at offset 0, so offset 0
		// moves. CueClusterPosition 0 names that Cluster and follows it; CueCodecState 0 names the
		// TrackEntry (RFC 9559 section 5.1.5.1.2.6) and stays 0.
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x44 })));
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new byte[] { 0 }), Element (0xEA, new byte[] { 0 }))))));
		var original = FileWith (cluster, cues);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Zero";

		var bytes = Save (parsed, original);

		var values = CueValues (bytes);
		Assert.IsTrue (values[0xF1][0] > 0);
		Assert.IsTrue (IsElementAt (bytes, values[0xF1][0], MatroskaIds.Cluster));
		Assert.AreEqual (1, values[0xEA].Count);
		Assert.AreEqual (0ul, values[0xEA][0]);
		Assert.AreEqual ("Zero", MatroskaFile.Read (bytes).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_Move_WithAWideSeekHead_PadsTheShorterRewrite ()
	{
		// Four-byte SeekPositions and an eight-byte size, with no Void after the SeekHead. The moved
		// SeekHead is written shorter, and a Void keeps it from shrinking below its old length.
		var info = Master (0x1549A966, Element (0x7BA9, "Wide"u8));
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		static byte[] Seek4 (ulong id, int at) => Master (0x4DBB, Concat (Element (0x53AB, Id (id)), Element (0x53AC, new[] { (byte)(at >> 24), (byte)(at >> 16), (byte)(at >> 8), (byte)at })));
		static byte[] Head (int infoAt, int tagsAt, int clusterAt)
		{
			var payload = Concat (Seek4 (MatroskaIds.Info, infoAt), Seek4 (MatroskaIds.Tags, tagsAt), Seek4 (MatroskaIds.Cluster, clusterAt));
			return Concat (new byte[] { 0x11, 0x4D, 0x9B, 0x74, 0x01, 0, 0, 0, 0, 0, 0, (byte)payload.Length }, payload);
		}

		var headLength = Head (0, 0, 0).Length;
		var infoAt = headLength;
		var tagsAt = infoAt + info.Length;
		var clusterAt = tagsAt + tags.Length;
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x55 })));
		var original = FileWith (Head (infoAt, tagsAt, clusterAt), info, tags, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Pictures = [new TestPicture ("image/png", PictureType.FrontCover, "", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 })];

		var bytes = Save (parsed, original);

		AssertSeeksPreserved (original, bytes);
		AssertEverySeekAndCue (bytes);
		Assert.IsTrue (ReadSeeks (bytes).Exists (entry => entry.Id == MatroskaIds.Attachments && IsElementAt (bytes, entry.Position, MatroskaIds.Attachments)));
		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		Assert.IsTrue (EbmlReader.TryRead (body, 0, body.Length, out var head));
		Assert.IsTrue (EbmlReader.TryRead (body, head.End, body.Length, out var after));
		Assert.AreEqual (MatroskaIds.Void, after.Id, "a Void takes the octets the shorter SeekHead gave up");
		Assert.AreEqual (headLength, after.End);
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_MoveThatCannotReadTheSeekHead_KeepsTheEditWhereItFits ()
	{
		// The SeekHead holds a child that does not parse, so it cannot be written again. The move is
		// refused and the first plan, dropped before the move was built, is built once more.
		var info = Master (0x1549A966, Element (0x7BA9, "Bad"u8));
		var probe = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), new byte[] { 0x00, 0x00 }));
		var seek = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, (ulong)probe.Length), new byte[] { 0x00, 0x00 }));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x66 })));
		var original = FileWith (seek, info, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Kept";

		var bytes = Save (parsed, original);

		Assert.AreEqual ("Kept", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.AreEqual (IndexOf (original, seek) - SegmentBodyAt (original), IndexOf (bytes, seek) - SegmentBodyAt (bytes), "the SeekHead is left as it was");
		Assert.AreEqual (IndexOf (original, cluster) - SegmentBodyAt (original), IndexOf (bytes, cluster) - SegmentBodyAt (bytes), "nothing moved");
	}

	[TestMethod]
	public void TryGrow_SeekThatCrossesAWidthBoundary_Widens ()
	{
		// The Cues Seek holds 65534 in two bytes. Three more octets need a third byte.
		var original = GrowLayout (cuesAt: 0xFFFE, secondClusterAt: -1, out _);
		Assert.IsTrue (ReadSeeks (original).Exists (entry => entry.Id == MatroskaIds.Cues && entry.Position == 0xFFFE));

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, GrownTracks (), out var bytes, out var error), error);

		AssertSeeksPreserved (original, bytes);
		AssertEverySeekAndCue (bytes);
		Assert.IsTrue (ReadSeeks (bytes).Exists (entry => entry.Id == MatroskaIds.Cues && entry.Position == 0x10001));
	}

	[TestMethod]
	public void TryGrow_CueClusterPositionThatCrossesAWidthBoundary_Widens ()
	{
		// The second Cluster is at 65533 and its CueClusterPosition has two bytes.
		var original = GrowLayout (cuesAt: -1, secondClusterAt: 0xFFFD, out var markers);

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, GrownTracks (), out var bytes, out var error), error);

		AssertSeeksPreserved (original, bytes);
		AssertEverySeekAndCue (bytes);
		var cues = CueValues (bytes)[0xF1];
		Assert.AreEqual (2, cues.Count);
		Assert.AreEqual (0x10000ul, cues[1]);
		Assert.IsTrue (IsElementAt (bytes, cues[1], MatroskaIds.Cluster));
		foreach (var marker in markers)
			Assert.AreEqual (1, CountOf (bytes, marker));
	}

	[TestMethod]
	public void Save_InsertBeforeALiveCluster_WidensTheCue ()
	{
		// Tags too big for any room go before the last, unknown-size Cluster. Its two-byte
		// CueClusterPosition passes 0xFFFF and widens.
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		var probe = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), Seek2 (MatroskaIds.Cues, 0), Element (0xEC, new byte[40])));
		var infoAt = probe.Length;
		var cuesAt = infoAt + info.Length;
		var cuesProbe = Master (MatroskaIds.Cues, Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }), Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, Two (0)))))));
		var clusterAt = cuesAt + cuesProbe.Length;
		var seek = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, (ulong)infoAt), Seek2 (MatroskaIds.Cues, (ulong)cuesAt), Element (0xEC, new byte[40])));
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }), Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, Two (clusterAt)))))));
		var block = Element (0xA3, new byte[] { 0x81, 0x00, 0x00, 0x80, 0x3C, 0x3D });
		var original = Matroska (Concat (Unknown (0x18538067, Concat (seek, info, cues, Unknown (0x1F43B675, Concat (Element (0xE7, new byte[] { 0 }), block))))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('L', 70000);

		var bytes = Save (parsed, original);

		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual (new string ('L', 70000), again.Tag.Title);
		Assert.AreEqual ("Live", MatroskaFile.Read (original).File!.Tag.Title);
		var cueValues = CueValuesUnknown (bytes)[0xF1];
		Assert.IsTrue (cueValues[0] > 0xFFFF);
		Assert.IsTrue (IsElementAtUnknown (bytes, cueValues[0], MatroskaIds.Cluster));
		Assert.IsTrue (bytes.AsSpan ().EndsWith (block));
	}

	/// <summary>
	/// SeekHead (full, with a kept non-Seek child), Info, Tags, a 256-byte Void, then Cues and a
	/// Cluster in either order. The CueClusterPosition has two bytes and the Cluster a three-byte Position.
	/// </summary>
	static byte[] MoveLayout (bool cuesFirst, out byte[] marker)
	{
		marker = new byte[] { 0xC6, 0xC7, 0xC8, 0xC9, 0xCA };
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var pad = Element (0xEC, new byte[0x100 - 3]);
		// The Tags Seek has one byte, as muxers write it, so the Tags cannot stay listed at the end.
		static byte[] Head (int infoAt, int tagsAt, int cuesAt, int clusterAt) => WithCrc (MatroskaIds.SeekHead, Concat (
			Seek2 (MatroskaIds.Info, (ulong)infoAt),
			SeekEntry (MatroskaIds.Tags, (ulong)tagsAt),
			Seek2 (MatroskaIds.Cues, (ulong)cuesAt),
			Seek2 (MatroskaIds.Cluster, (ulong)clusterAt),
			KeptSeekHeadChild));
		static byte[] CuesFor (int clusterAt) => WithCrc (MatroskaIds.Cues, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, Two (clusterAt)))))));
		var headLength = Head (0, 0, 0, 0).Length;
		var infoAt = headLength;
		var tagsAt = infoAt + info.Length;
		var afterPad = tagsAt + tags.Length + pad.Length;
		var cuesLength = CuesFor (0).Length;
		var clusterAt = cuesFirst ? afterPad + cuesLength : afterPad;
		var cluster = WithCrc (MatroskaIds.Cluster, Concat (
			Element (0xE7, new byte[] { 0 }),
			Element (0xA7, new byte[] { 0, (byte)(clusterAt >> 8), (byte)clusterAt }),
			Element (0xA3, marker)));
		var cuesAt = cuesFirst ? afterPad : afterPad + cluster.Length;
		Assert.IsTrue (clusterAt is > 0xFF and < 0xFFFF);
		Assert.IsTrue (tagsAt < 0x80);
		var head = Head (infoAt, tagsAt, cuesAt, clusterAt);
		var cues = CuesFor (clusterAt);
		var original = cuesFirst
			? FileWith (head, info, tags, pad, cues, cluster)
			: FileWith (head, info, tags, pad, cluster, cues);
		Assert.IsTrue (IsElementAt (original, (ulong)clusterAt, MatroskaIds.Cluster));
		Assert.IsTrue (IsElementAt (original, (ulong)cuesAt, MatroskaIds.Cues));
		Assert.IsTrue (EveryCrcMatches (original));
		return original;
	}

	/// <summary>
	/// SeekHead, Info, Tracks, Cluster(s), Cues. With <paramref name="cuesAt"/> set, one Cluster is
	/// sized so the Cues start there. With <paramref name="secondClusterAt"/> set, a second Cluster
	/// starts there and the Cues list both.
	/// </summary>
	static byte[] GrowLayout (int cuesAt, int secondClusterAt, out List<byte[]> markers)
	{
		markers = new List<byte[]> { new byte[] { 0xD1, 0xD2, 0xD3, 0xD4 }, new byte[] { 0xE1, 0xE2, 0xE3, 0xE4 } };
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tracks = Master (0x1654AE6B, Master (0xAE, Concat (Element (0xD7, new byte[] { 1 }), Element (0x83, new byte[] { 2 }), Element (0x86, "A_OPUS"u8))));
		var wideCues = cuesAt < 0;
		byte[] CuesSeek (int at) => wideCues ? Seek3 (MatroskaIds.Cues, (ulong)at) : Seek2 (MatroskaIds.Cues, (ulong)at);
		var headLength = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), Seek2 (MatroskaIds.Tracks, 0), CuesSeek (0))).Length;
		var clusterAt = headLength + info.Length + tracks.Length;
		var clusters = new List<byte[]> ();
		if (cuesAt >= 0) {
			clusters.Add (SizedCluster (cuesAt - clusterAt, markers[0]));
		} else {
			clusters.Add (SizedCluster (secondClusterAt - clusterAt, markers[0]));
			clusters.Add (SizedCluster (64, markers[1]));
			cuesAt = secondClusterAt + 64;
		}

		var cuePoints = new List<byte[]> { CuePoint (0, Two (clusterAt)) };
		if (clusters.Count > 1)
			cuePoints.Add (CuePoint (1, Two (secondClusterAt)));
		var cues = Master (MatroskaIds.Cues, Concat (cuePoints.ToArray ()));
		var head = Master (MatroskaIds.SeekHead, Concat (
			Seek2 (MatroskaIds.Info, (ulong)headLength),
			Seek2 (MatroskaIds.Tracks, (ulong)(headLength + info.Length)),
			CuesSeek (cuesAt)));
		Assert.AreEqual (headLength, head.Length);
		var original = WideFile (Concat (head, info, tracks), Concat (clusters.ToArray ()), cues);
		Assert.IsTrue (IsElementAt (original, (ulong)cuesAt, MatroskaIds.Cues));
		return original;
	}

	static byte[] GrownTracks () => Master (0x1654AE6B, Master (0xAE, Concat (
		Element (0xD7, new byte[] { 1 }),
		Element (0x83, new byte[] { 2 }),
		Element (0x86, "A_OPUS"u8),
		Element (0xB9, new byte[] { 0 }))));

	static byte[] CuePoint (byte time, byte[] clusterPosition) => Master (0xBB, Concat (
		Element (0xB3, new[] { time }),
		Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, clusterPosition)))));

	/// <summary>A Cluster of exactly <paramref name="total"/> bytes, with a four-byte size.</summary>
	static byte[] SizedCluster (int total, byte[] marker)
	{
		var payload = new byte[total - 8];
		payload[0] = 0xE7;
		payload[1] = 0x81;
		payload[2] = 0x00;
		var blockData = payload.Length - 3 - 5;
		payload[3] = 0xA3;
		payload[4] = 0x10;
		payload[5] = (byte)(blockData >> 16);
		payload[6] = (byte)(blockData >> 8);
		payload[7] = (byte)blockData;
		marker.CopyTo (payload, 8);
		var cluster = new byte[total];
		new byte[] { 0x1F, 0x43, 0xB6, 0x75, 0x10, (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length }.CopyTo (cluster, 0);
		payload.CopyTo (cluster, 8);
		return cluster;
	}

	static byte[] Two (int value) => new[] { (byte)(value >> 8), (byte)value };

	static byte[] Seek3 (ulong id, ulong position) =>
		Master (0x4DBB, Concat (Element (0x53AB, Id (id)), Element (0x53AC, new[] { (byte)(position >> 16), (byte)(position >> 8), (byte)position })));

	/// <summary>An EBML header and a Segment with a four-byte size, for bodies past 16 KB.</summary>
	static byte[] WideFile (params byte[][] children)
	{
		var body = Concat (children);
		var header = new byte[] { 0x18, 0x53, 0x80, 0x67, 0x10, (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length };
		return Concat (Master (0x1A45DFA3, Element (0x4282, "matroska"u8)), header, body);
	}

	/// <summary>
	/// Every Seek that named an element in <paramref name="original"/> is still listed in
	/// <paramref name="saved"/> and names an element with its ID.
	/// </summary>
	static void AssertSeeksPreserved (byte[] original, byte[] saved)
	{
		var before = ReadSeeks (original).Where (entry => IsElementAt (original, entry.Position, entry.Id)).Select (entry => entry.Id).OrderBy (id => id).ToList ();
		var after = ReadSeeks (saved).Where (entry => IsElementAt (saved, entry.Position, entry.Id)).Select (entry => entry.Id).ToList ();
		foreach (var id in before) {
			Assert.IsTrue (after.Remove (id), $"the Seek for 0x{id:X} was dropped or no longer lands");
		}
	}

	/// <summary>Every CueClusterPosition, CueCodecState, CueRefCluster, and CueRefCodecState value, in file order.</summary>
	static Dictionary<ulong, List<ulong>> CueValues (byte[] file)
	{
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		return CueValuesIn (body);
	}

	static Dictionary<ulong, List<ulong>> CueValuesUnknown (byte[] file)
	{
		var offset = 0;
		while (EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id == MatroskaIds.Segment)
				return CueValuesIn (file.AsSpan (element.DataOffset));
			offset = element.End;
		}

		Assert.Fail ("no Segment");
		return [];
	}

	static Dictionary<ulong, List<ulong>> CueValuesIn (ReadOnlySpan<byte> body)
	{
		var values = new Dictionary<ulong, List<ulong>> { [0xF1] = [], [0xEA] = [], [0x97] = [], [0xEB] = [] };
		Collect (body, values, inCues: false);
		return values;
	}

	static void Collect (ReadOnlySpan<byte> body, Dictionary<ulong, List<ulong>> values, bool inCues)
	{
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element)) {
			if (element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.Cues || (inCues && element.Id is 0xBB or 0xB7 or 0xDB))
				Collect (payload, values, inCues: true);
			else if (inCues && values.TryGetValue (element.Id, out var list))
				list.Add (EbmlReader.ReadUInt (payload));
			offset = element.End;
		}
	}
}
