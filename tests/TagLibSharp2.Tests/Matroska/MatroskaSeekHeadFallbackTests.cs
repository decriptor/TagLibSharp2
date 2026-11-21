// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	[TestMethod]
	public void Save_GrownTags_WithAFullSeekHead_GoAfterItAndStayListed ()
	{
		// The Tags Seek has a one-byte position and the SeekHead has no Void. Appending the grown
		// Tags past offset 255 would void that Seek, and readers would lose the tags.
		var marker = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5 };
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var seekLength = WithCrc (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), SeekEntry (MatroskaIds.Tags, 1), Seek2 (MatroskaIds.Cluster, 0), Seek2 (MatroskaIds.Cues, 0))).Length;
		var tagsAt = seekLength + info.Length;
		Assert.IsTrue (tagsAt < 0x80);
		var clusterAt = tagsAt + tags.Length;
		var cluster = ClusterWithPosition (clusterAt, Concat (marker, new byte[300]));
		var cuesAt = clusterAt + cluster.Length;
		var seek = WithCrc (MatroskaIds.SeekHead, Concat (
			Seek2 (MatroskaIds.Info, (ulong)seekLength),
			SeekEntry (MatroskaIds.Tags, (ulong)tagsAt),
			Seek2 (MatroskaIds.Cluster, (ulong)clusterAt),
			Seek2 (MatroskaIds.Cues, (ulong)cuesAt)));
		var cues = WithCrc (MatroskaIds.Cues, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new[] { (byte)(clusterAt >> 8), (byte)clusterAt }))))));
		var original = FileWith (seek, info, tags, cluster, cues);
		Assert.AreEqual (seekLength, seek.Length);
		Assert.IsTrue (EveryCrcMatches (original));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('T', 200);
		parsed.Tag.Artist = "Someone";

		var bytes = Save (parsed, original);

		AssertEverySeekAndCue (bytes);
		var listed = ReadSeeks (bytes).Find (entry => entry.Id == MatroskaIds.Tags);
		Assert.AreEqual (MatroskaIds.Tags, listed.Id);
		Assert.IsTrue (listed.Position < (ulong)(IndexOf (bytes, marker) - SegmentBodyAt (bytes)));
		Assert.IsTrue (EveryCrcMatches (bytes));
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.AreEqual (new string ('T', 200), MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.AreEqual ("Someone", MatroskaFile.Read (bytes).File!.Tag.Artist);
	}

	[TestMethod]
	public void Save_MoveThatWouldDropAnotherSeek_IsNotUsed ()
	{
		// Moving the Cues past offset 255 would void its one-byte Seek, so the tags take the old path.
		var info = Master (0x1549A966, Element (0x7BA9, "I"u8));
		var tags = Tags (Tag (30, Simple ("TITLE", "Old")));
		var probe = Master (0x114D9B74, Concat (SeekEntry (MatroskaIds.Info, 1), SeekEntry (MatroskaIds.Tags, 1), SeekEntry (MatroskaIds.Cues, 1)));
		var marker = new byte[] { 0x5A, 0x5B };
		var tagsAt = probe.Length + info.Length;
		var clusterAt = tagsAt + tags.Length;
		// The Cues end past offset 255, so the old path's appended Tags lose their one-byte Seek too.
		var cluster = Master (0x1F43B675, Concat (Element (0xA3, marker), new byte[245 - clusterAt - 10]));
		var cuesAt = clusterAt + cluster.Length;
		Assert.AreEqual (245, cuesAt);
		var seek = Master (0x114D9B74, Concat (
			SeekEntry (MatroskaIds.Info, (ulong)probe.Length),
			SeekEntry (MatroskaIds.Tags, (ulong)tagsAt),
			SeekEntry (MatroskaIds.Cues, (ulong)cuesAt)));
		var cues = Master (0x1C53BB6B, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new[] { (byte)clusterAt }))))));
		var original = FileWith (seek, info, tags, cluster, cues);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('L', 60);

		var bytes = Save (parsed, original);

		Assert.IsTrue (cuesAt + cues.Length > 255);
		var cuesSeek = ReadSeeks (bytes).Find (entry => entry.Id == MatroskaIds.Cues);
		Assert.AreEqual (MatroskaIds.Cues, cuesSeek.Id);
		Assert.IsTrue (IsElementAt (bytes, cuesSeek.Position, MatroskaIds.Cues));
		Assert.AreEqual (IndexOf (original, cluster) - SegmentBodyAt (original), IndexOf (bytes, cluster) - SegmentBodyAt (bytes));
		Assert.AreEqual (new string ('L', 60), MatroskaFile.Read (bytes).File!.Tag.Title);
	}

	[TestMethod]
	public void TryGrow_LongerTracks_MovesEverythingAfterOnce ()
	{
		var marker = new byte[] { 0x71, 0x72, 0x73 };
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tracks = Master (0x1654AE6B, Master (0xAE, Concat (Element (0x83, new byte[] { 2 }), Element (0x86, "A_OPUS"u8))));
		var seekLength = WithCrc (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), Seek2 (MatroskaIds.Tracks, 0), Seek2 (MatroskaIds.Cluster, 0), Seek2 (MatroskaIds.Cues, 0))).Length;
		var tracksAt = seekLength + info.Length;
		var clusterAt = tracksAt + tracks.Length;
		var cluster = ClusterWithPosition (clusterAt, marker);
		var cuesAt = clusterAt + cluster.Length;
		var seek = WithCrc (MatroskaIds.SeekHead, Concat (
			Seek2 (MatroskaIds.Info, (ulong)seekLength),
			Seek2 (MatroskaIds.Tracks, (ulong)tracksAt),
			Seek2 (MatroskaIds.Cluster, (ulong)clusterAt),
			Seek2 (MatroskaIds.Cues, (ulong)cuesAt)));
		var cues = WithCrc (MatroskaIds.Cues, Master (0xBB, Concat (
			Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new[] { (byte)(clusterAt >> 8), (byte)clusterAt }))))));
		var original = FileWith (seek, info, tracks, cluster, cues);
		var entry = Master (0xAE, Concat (
			Element (0x83, new byte[] { 2 }),
			Element (0x86, "A_OPUS"u8),
			Element (0xB9, new byte[] { 0 }),
			Element (0x536E, "A much longer track name"u8)));
		// The caller's CRC-32 is left at zero. The grow recomputes it.
		var grown = Master (0x1654AE6B, Concat (Element (0xBF, new byte[4]), entry));

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, grown, out var bytes, out var error), error);

		AssertEverySeekAndCue (bytes);
		Assert.IsTrue (EveryCrcMatches (bytes));
		Assert.IsTrue (bytes.AsSpan ().IndexOf (entry) >= 0);
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.IsTrue (TryReadCueClusterPosition (bytes, out var cuePosition, out _));
		Assert.AreEqual (cuePosition, ClusterPositionOf (bytes));
		Assert.AreNotEqual ((ulong)clusterAt, cuePosition);
		Assert.AreEqual (grown.Length - tracks.Length, bytes.Length - original.Length);

		// A shorter element stays in place, padded with a Void.
		var shorter = Master (0x1654AE6B, Master (0xAE, Element (0x83, new byte[] { 2 })));
		Assert.IsTrue (MatroskaRewrite.TryGrow (original, shorter, out var padded, out error), error);
		Assert.AreEqual (original.Length, padded.Length);
		Assert.AreEqual (IndexOf (original, cluster), IndexOf (padded, cluster));
		Assert.IsTrue (EveryCrcMatches (padded));
	}

	[TestMethod]
	public void TryGrow_MissingElement_Fails ()
	{
		var original = FileWith (Master (0x1549A966, Element (0x7BA9, "I"u8)), Master (0x1F43B675, new byte[] { 1 }));

		Assert.IsFalse (MatroskaRewrite.TryGrow (original, Master (0x1654AE6B, new byte[] { 0xAE, 0x80 }), out _, out var error));
		StringAssert.Contains (error, "not found");
		Assert.IsFalse (MatroskaRewrite.TryGrow (original, new byte[] { 0x16, 0x54 }, out _, out error));
	}

	/// <summary>Every Seek and every CueClusterPosition points at an element with its ID.</summary>
	static void AssertEverySeekAndCue (byte[] file)
	{
		foreach (var entry in ReadSeeks (file))
			Assert.IsTrue (IsElementAt (file, entry.Position, entry.Id), entry.Id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
		if (TryReadCueClusterPosition (file, out var position, out var crcOk)) {
			Assert.IsTrue (crcOk);
			Assert.IsTrue (IsElementAt (file, position, MatroskaIds.Cluster));
		}
	}

	/// <summary>A Seek with a two-byte position, so its length does not depend on the offset.</summary>
	static byte[] Seek2 (ulong id, ulong position) =>
		Master (0x4DBB, Concat (Element (0x53AB, Id (id)), Element (0x53AC, new[] { (byte)(position >> 8), (byte)position })));

	/// <summary>A cluster with a CRC-32 and a two-byte Position set to <paramref name="at"/>.</summary>
	static byte[] ClusterWithPosition (int at, byte[] marker) =>
		WithCrc (MatroskaIds.Cluster, Concat (
			Element (0xE7, new byte[] { 0 }),
			Element (0xA7, new[] { (byte)(at >> 8), (byte)at }),
			Element (0xA3, marker)));

	static ulong ClusterPositionOf (byte[] file)
	{
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.Cluster) {
				var child = element.DataOffset;
				while (child < element.End && EbmlReader.TryRead (body, child, element.End, out var inner) && !inner.UnknownSize) {
					if (inner.Id == 0xA7)
						return EbmlReader.ReadUInt (body.AsSpan (inner.DataOffset, (int)inner.DataLength));
					child = inner.End;
				}
			}

			offset = element.End;
		}

		return ulong.MaxValue;
	}

	static int SegmentBodyAt (byte[] file)
	{
		Assert.IsTrue (TrySegmentBody (file, out _, out var bodyAt));
		return bodyAt;
	}
}
