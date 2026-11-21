// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Invariant: widening moved positions costs time linear in the Cues and SeekHead bytes, however the
/// widths cascade, and every value still names its target afterwards.
/// </summary>
public partial class MatroskaFileTests
{
	[TestMethod]
	[Timeout (120_000)]
	public void TryGrow_CascadingCueWidths_SettleInLinearWork ()
	{
		// Cue k holds 0xFFFF - k in two octets. One more octet before them pushes cue 0 over 0xFFFF,
		// its growth pushes cue 1 over, and so on: each widening causes the next.
		var small = CascadeCost (150);
		var large = CascadeCost (300);

		Assert.IsTrue (small.Ok && large.Ok);
		Assert.IsTrue (large.Bytes < small.Bytes * 3, FormattableString.Invariant ($"allocated {small.Bytes} bytes for 150 Cues and {large.Bytes} for 300: not linear"));
		Assert.IsTrue (large.Calls < small.Calls * 3, FormattableString.Invariant ($"placed {small.Calls} values for 150 Cues and {large.Calls} for 300: not linear"));

		// Each value placed is a binary search over at most one growth entry per rendered piece, so
		// log2(301) + 1 < 10 entries. A linear scan examines up to 301 and doubles with N.
		Assert.IsTrue (small.Steps > 0, "the growth search is counted");
		Assert.IsTrue (large.Steps <= large.Calls * 10, FormattableString.Invariant ($"examined {large.Steps} growth entries for {large.Calls} values: not a logarithmic search"));
		Assert.IsTrue (large.Steps < small.Steps * 3, FormattableString.Invariant ($"examined {small.Steps} growth entries for 150 Cues and {large.Steps} for 300: not linear"));
	}

	[TestMethod]
	public void TryGrow_CascadingCueWidths_EveryCueFollowsTheCluster ()
	{
		var original = CascadeLayout (40, out var tracks);
		var clusterBefore = OffsetOfFirst (Body (original), MatroskaIds.Cluster);
		var oldValues = CueValues (original)[0xF1];

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, tracks, out var bytes, out var error), error);

		var shift = (ulong)(OffsetOfFirst (Body (bytes), MatroskaIds.Cluster) - clusterBefore);
		Assert.IsTrue (shift > 40, "every Cues element grew");
		var newValues = CueValues (bytes)[0xF1];
		Assert.AreEqual (oldValues.Count, newValues.Count);
		for (var i = 0; i < oldValues.Count; i++)
			Assert.AreEqual (oldValues[i] + shift, newValues[i], FormattableString.Invariant ($"cue {i}"));
		Assert.IsTrue (EveryCrcMatches (bytes));
	}

	[TestMethod]
	public void TryGrow_ClusterPositionThatCannotWiden_IsVoided ()
	{
		// The optional Position (RFC 9559 §5.1.3.2) holds 0xFFFE in two octets. Three more octets
		// before the Cluster need three; the Cluster cannot grow, so Position becomes a Void.
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var tracks = Master (0x1654AE6B, Master (0xAE, Concat (Element (0xD7, new byte[] { 1 }), Element (0x83, new byte[] { 2 }))));
		var lead = info.Length + tracks.Length;
		var filler = WideVoid (0xFFFE - lead);
		var marker = new byte[] { 0xF1, 0xF2, 0xF3, 0xF4 };
		var cluster = WithCrc (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA7, Two (0xFFFE)), Element (0xA3, marker)));
		var cues = Master (MatroskaIds.Cues, CuePoint (0, new byte[] { 0, 0xFF, 0xFE }));
		var original = WideFile (info, tracks, filler, cluster, cues);
		Assert.IsTrue (IsElementAt (original, 0xFFFE, MatroskaIds.Cluster));
		var grown = Master (0x1654AE6B, Master (0xAE, Concat (Element (0xD7, new byte[] { 1 }), Element (0x83, new byte[] { 2 }), Element (0xB9, new byte[] { 0 }))));

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, grown, out var bytes, out var error), error);

		Assert.IsTrue (IsElementAt (bytes, 0x10001, MatroskaIds.Cluster));
		Assert.AreEqual (ulong.MaxValue, ClusterPositionOf (bytes), "no Position is left holding an old offset");
		var at = IndexOf (bytes, marker) - IndexOf (original, marker);
		Assert.AreEqual (3, at, "the Cluster moved whole and kept its length");
		Assert.AreEqual (0x10001ul, CueValues (bytes)[0xF1][0]);
		Assert.IsTrue (EveryCrcMatches (bytes));
	}

	[TestMethod]
	public void Save_Move_SeekPositionThatWouldWrap_IsNotWritten ()
	{
		// A Chapters Seek holds 2^64 - 1, which no element can be at. Moving it would wrap to a small
		// offset inside the SeekHead; the rendered SeekHead refuses it as its in-place twin does.
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		static byte[] Seek8 (ulong id, ulong position) => Master (0x4DBB, Concat (Element (0x53AB, Id (id)), Element (0x53AC, new[] {
			(byte)(position >> 56), (byte)(position >> 48), (byte)(position >> 40), (byte)(position >> 32),
			(byte)(position >> 24), (byte)(position >> 16), (byte)(position >> 8), (byte)position })));
		var headLength = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, 0), Seek8 (MatroskaIds.Chapters, ulong.MaxValue))).Length;
		var head = Master (MatroskaIds.SeekHead, Concat (Seek2 (MatroskaIds.Info, (ulong)headLength), Seek8 (MatroskaIds.Chapters, ulong.MaxValue)));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x44 })));
		var original = FileWith (head, info, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Wrap";
		var fs = new MockFileSystem ();
		fs.AddFile ("/wrap.mkv", original);

		var saved = parsed.SaveToFile ("/wrap.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/wrap.mkv");
		foreach (var seek in ReadSeeks (bytes))
			Assert.IsTrue (seek.Id != MatroskaIds.Chapters || seek.Position == ulong.MaxValue, FormattableString.Invariant ($"the Chapters Seek wrapped to {seek.Position}"));
		Assert.AreEqual ("Wrap", MatroskaFile.Read (bytes).File!.Tag.Title);
	}

	[TestMethod]
	public void TryGrow_CascadingSeekPositions_GoWideAndEverySeekLands ()
	{
		// Twelve SeekHeads, each with five Seeks for one empty Chapters element. Target k sits at
		// 0xFFFF - 5k with its SeekPositions in two octets. The Tracks octet pushes target 0 past two
		// octets; its five Seeks grow by five, which pushes target 1 past, and so on. After the
		// minimal-width rounds every moved SeekPosition is written at eight octets.
		const int heads = 12;
		const int targetLength = 5;
		var target = new byte[] { 0x10, 0x43, 0xA7, 0x70, 0x80 };
		var tracks = Master (0x1654AE6B, new byte[] { 0xEC, 0x80 });
		var grown = Master (0x1654AE6B, new byte[] { 0xEC, 0x81, 0x00 });
		var children = new List<byte[]> { tracks };
		for (var k = 0; k < heads; k++) {
			var at = (ulong)(0xFFFF - (targetLength * k));
			children.Add (Master (MatroskaIds.SeekHead, Concat (Enumerable.Repeat (Seek2 (MatroskaIds.Chapters, at), 5).ToArray ())));
		}

		var lead = children.Sum (child => child.Length);
		var firstTarget = 0xFFFF - (targetLength * (heads - 1));
		children.Add (WideVoid (firstTarget - lead));
		for (var k = 0; k < heads; k++)
			children.Add (target);
		children.Add (Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x55 }))));
		var original = WideFile (children.ToArray ());
		Assert.IsTrue (ReadSeeks (original).TrueForAll (seek => IsElementAt (original, seek.Position, MatroskaIds.Chapters)));

		Assert.IsTrue (MatroskaRewrite.TryGrow (original, grown, out var bytes, out var error), error);

		var seeks = ReadSeeks (bytes);
		Assert.AreEqual (heads * 5, seeks.Count);
		foreach (var seek in seeks)
			Assert.IsTrue (IsElementAt (bytes, seek.Position, MatroskaIds.Chapters), FormattableString.Invariant ($"a Seek names {seek.Position}, which is not a Chapters element"));
		CollectionAssert.AreEqual (Enumerable.Repeat (8, heads * 5).ToList (), SeekPositionWidths (bytes), "the cascade outlasts the minimal-width rounds, so every SeekPosition is eight octets");
		Assert.IsTrue (EveryCrcMatches (bytes));
	}

	/// <summary>
	/// The bytes allocated, values placed and growth entries searched by one TryGrow over
	/// <paramref name="count"/> cascading Cues.
	/// </summary>
	static (bool Ok, long Bytes, long Calls, long Steps) CascadeCost (int count)
	{
		var original = CascadeLayout (count, out var tracks);
		MatroskaRewrite.TryGrow (original, tracks, out _, out _);
		MatroskaRewrite.SettleCalls = 0;
		MatroskaRewrite.SettleSteps = 0;
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var ok = MatroskaRewrite.TryGrow (original, tracks, out _, out _);
		var bytes = GC.GetAllocatedBytesForCurrentThread () - before;
		return (ok, bytes, MatroskaRewrite.SettleCalls, MatroskaRewrite.SettleSteps);
	}

	/// <summary>
	/// Tracks holding an empty Void, then <paramref name="count"/> one-point Cues elements whose
	/// CueClusterPosition k is <c>0xFFFF - k</c> in two octets, then a Cluster. The returned Tracks has
	/// a one-octet Void, so it is exactly one octet longer.
	/// </summary>
	static byte[] CascadeLayout (int count, out byte[] grownTracks)
	{
		var tracks = Master (0x1654AE6B, new byte[] { 0xEC, 0x80 });
		grownTracks = Master (0x1654AE6B, new byte[] { 0xEC, 0x81, 0x00 });
		var children = new List<byte[]> { tracks };
		for (var k = 0; k < count; k++)
			children.Add (Master (MatroskaIds.Cues, CuePoint ((byte)k, Two (0xFFFF - k))));
		children.Add (Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x55 }))));
		return WideFile (children.ToArray ());
	}

	static int OffsetOfFirst (byte[] body, ulong id)
	{
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == id)
				return offset;
			offset = element.End;
		}

		return -1;
	}

	static byte[] Body (byte[] file)
	{
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		return body;
	}

	/// <summary>A Void of exactly <paramref name="total"/> octets, with a four-octet size.</summary>
	static byte[] WideVoid (int total)
	{
		var bytes = new byte[total];
		var payload = total - 5;
		bytes[0] = 0xEC;
		bytes[1] = 0x10;
		bytes[2] = (byte)(payload >> 16);
		bytes[3] = (byte)(payload >> 8);
		bytes[4] = (byte)payload;
		return bytes;
	}

	/// <summary>The width in octets of every SeekPosition in the Segment's SeekHeads, in file order.</summary>
	static List<int> SeekPositionWidths (byte[] file)
	{
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		var widths = new List<int> ();
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.SeekHead) {
				var seekAt = element.DataOffset;
				while (seekAt < element.End && EbmlReader.TryRead (body, seekAt, element.End, out var seek)) {
					var child = seek.DataOffset;
					while (child < seek.End && EbmlReader.TryRead (body, child, seek.End, out var field)) {
						if (field.Id == MatroskaIds.SeekPosition)
							widths.Add ((int)field.DataLength);
						child = field.End;
					}

					seekAt = seek.End;
				}
			}

			offset = element.End;
		}

		return widths;
	}
}
