// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Invariant: an empty unsigned Segment Position is 0 (RFC 8794 section 7.2), so a save that moves
/// later elements keeps it and still succeeds, in place and when the Cues are written again.
/// A move that cannot update the Cues names them.
/// </summary>
public partial class MatroskaFileTests
{
	static readonly byte[] EmptyCueRefCluster = [0x97, 0x80];
	static readonly byte[] EmptyCueClusterPosition = [0xF1, 0x80];

	[TestMethod]
	public void Save_InsertBeforeLiveCluster_EmptyCueValues_AreKept ()
	{
		// A live stream: the Segment and its last Cluster have unknown sizes, so new Tags go right
		// before that Cluster and the Cues are walked for positions to move. One CueReference holds an
		// empty CueRefCluster (RFC 9559 Appendix A.37, no default) and one CuePoint an empty
		// CueClusterPosition. Both are 0, below anything that moves, so both stay as they are.
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		byte[] Cues (int clusterAt) => Master (MatroskaIds.Cues, Concat (
			Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }), Master (0xB7, Concat (
				Element (0xF7, new byte[] { 1 }),
				Element (0xF1, Two (clusterAt)),
				Master (0xDB, Concat (Element (0x96, new byte[] { 0 }), EmptyCueRefCluster)))))),
			Master (0xBB, Concat (Element (0xB3, new byte[] { 1 }), Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), EmptyCueClusterPosition))))));
		var clusterAt = info.Length + Cues (0).Length;
		var cluster = Unknown (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x66 })));
		var original = Matroska (Unknown (MatroskaIds.Segment, Concat (info, Cues (clusterAt), cluster)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Artist = "Empty values";
		var fs = new MockFileSystem ();
		fs.AddFile ("/empty.mkv", original);

		var saved = parsed.SaveToFile ("/empty.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/empty.mkv");
		Assert.AreEqual ("Empty values", MatroskaFile.Read (bytes).File!.Tag.Artist);
		Assert.AreEqual (1, CountOf (bytes, EmptyCueRefCluster), "the empty CueRefCluster is kept");
		Assert.AreEqual (1, CountOf (bytes, EmptyCueClusterPosition), "the empty CueClusterPosition is kept");
		var movedTo = IndexOf (bytes, Id (MatroskaIds.Cluster)) - UnknownSegmentBodyAt (bytes);
		Assert.IsTrue (movedTo > clusterAt, "the live Cluster moved");
		Assert.IsTrue (IndexOf (bytes, Element (0xF1, Two (movedTo))) > 0, "the other CueClusterPosition follows the Cluster");
	}

	[TestMethod]
	public void TryGrow_EmptyCueValues_AreKeptWhenTheCuesAreWrittenAgain ()
	{
		// The first CuePoint holds 0xFFFF in two octets, which one more octet before it pushes past two
		// octets, so the Cues are written again. The empty values stay empty in that rendering.
		var tracks = Master (0x1654AE6B, new byte[] { 0xEC, 0x80 });
		var grown = Master (0x1654AE6B, new byte[] { 0xEC, 0x81, 0x00 });
		var cues = Master (MatroskaIds.Cues, Concat (
			CuePoint (0, Two (0xFFFF)),
			Master (0xBB, Concat (Element (0xB3, new byte[] { 1 }), Master (0xB7, Concat (
				Element (0xF7, new byte[] { 1 }),
				EmptyCueClusterPosition,
				Master (0xDB, Concat (Element (0x96, new byte[] { 0 }), EmptyCueRefCluster))))))));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x55 })));
		var original = WideFile (tracks, cues, cluster);

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, grown, out var bytes, out var error), error);

		var values = CueValues (bytes)[0xF1];
		Assert.AreEqual (2, values.Count);
		// Everything before 0xFFFF grew: the Tracks octet and the rendered Cues.
		Assert.AreEqual (0xFFFFul + (ulong)(bytes.Length - original.Length), values[0], "the full CueClusterPosition moved and widened");
		Assert.AreEqual (0ul, values[1], "the empty one still reads as 0");
		Assert.AreEqual (1, CountOf (bytes, EmptyCueClusterPosition), "the empty CueClusterPosition is kept");
		Assert.AreEqual (1, CountOf (bytes, EmptyCueRefCluster), "the empty CueRefCluster is kept");
		Assert.IsTrue (EveryCrcMatches (bytes));
	}

	[TestMethod]
	public void Save_InsertAtOffsetZero_EmptyValuesThatNameIt_Move ()
	{
		// No SeekHead and the Cluster first, at Segment offset 0. Its empty Position and the empty
		// CueClusterPosition are both 0, so both name that Cluster. New Tags go before the first
		// Cluster (RFC 9559 section 25.3.4), so offset 0 moves: the CueClusterPosition is written
		// wider, and the Position, which cannot widen in place, is voided as an optional element.
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), new byte[] { 0xA7, 0x80 }, Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0xAA })));
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), EmptyCueClusterPosition)))));
		var info = Master (0x1549A966, Element (0x7BA9, "Zero"u8));
		var original = FileWith (cluster, cues, info);
		Assert.IsTrue (IsElementAt (original, 0, MatroskaIds.Cluster));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Moved from zero";
		var fs = new MockFileSystem ();
		fs.AddFile ("/zero.mkv", original);

		var saved = parsed.SaveToFile ("/zero.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/zero.mkv");
		Assert.AreEqual ("Moved from zero", MatroskaFile.Read (bytes).File!.Tag.Title);
		var cue = CueValues (bytes)[0xF1].Single ();
		Assert.IsTrue (cue > 0, "the CueClusterPosition followed the Cluster");
		Assert.IsTrue (IsElementAt (bytes, cue, MatroskaIds.Cluster), FormattableString.Invariant ($"CueClusterPosition {cue} names the Cluster"));
		Assert.AreEqual (0, CountOf (bytes, new byte[] { 0xA7, 0x80 }), "the empty Position no longer claims offset 0");
		Assert.AreEqual (1, CountOf (bytes, Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0xAA })), "the block is kept");
		Assert.IsTrue (EveryCrcMatches (bytes));
	}

	[TestMethod]
	public void Save_NewAttachments_MoveThatCannotUpdateTheCues_NamesTheCues ()
	{
		// The SeekHead has no room, so the Attachments must move after it, and the move must update
		// the Cues. A nine-octet CueClusterPosition is malformed (RFC 8794 section 7.2 allows 0 to 8),
		// so the move fails. Placing the Attachments without the move fails too; the error says why
		// the move did, because that is what the file needs fixed.
		var info = Master (0x1549A966, Element (0x7BA9, "Bad cue"u8));
		var headLength = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, 0)).Length;
		var head = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, (ulong)headLength));
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new byte[9]))))));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x77 })));
		var original = FileWith (head, info, cues, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];
		var fs = new MockFileSystem ();
		fs.AddFile ("/bad-cue.mkv", original);

		var saved = parsed.SaveToFile ("/bad-cue.mkv", original, fs);

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "Cues");
		CollectionAssert.AreEqual (original, fs.ReadAllBytes ("/bad-cue.mkv"));
	}
}
