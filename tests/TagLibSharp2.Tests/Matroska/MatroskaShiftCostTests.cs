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
		var small = CascadeAllocation (150, out var smallOk);
		var large = CascadeAllocation (300, out var largeOk);

		Assert.IsTrue (smallOk && largeOk);
		Assert.IsTrue (large < small * 3, FormattableString.Invariant ($"allocated {small} bytes for 150 Cues and {large} for 300: not linear"));
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

	/// <summary>Bytes allocated by one TryGrow over <paramref name="count"/> cascading Cues.</summary>
	static long CascadeAllocation (int count, out bool ok)
	{
		var original = CascadeLayout (count, out var tracks);
		MatroskaRewrite.TryGrow (original, tracks, out _, out _);
		var before = GC.GetAllocatedBytesForCurrentThread ();
		ok = MatroskaRewrite.TryGrow (original, tracks, out _, out _);
		return GC.GetAllocatedBytesForCurrentThread () - before;
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
}
