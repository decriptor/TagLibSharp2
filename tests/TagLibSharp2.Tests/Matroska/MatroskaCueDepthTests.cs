// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.ExceptionServices;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Invariant: the Cues walkers follow only the RFC 9559 section 5.1.5 paths (Cues, CuePoint,
/// CueTrackPositions, CueReference), so their depth is fixed whatever the file nests, and any
/// other nesting is copied as it is.
/// </summary>
public partial class MatroskaFileTests
{
	const int NestedDepth = 10_000;

	[TestMethod]
	public void Save_Move_DeeplyNestedCuePoints_SavesOnASmallStack ()
	{
		// SeekHead[Info] with no Void after it, so a title edit moves everything after the SeekHead
		// and walks the Cues in place. The Cues hold 10,000 CuePoints, each inside the last.
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var headLength = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, 0)).Length;
		var head = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, (ulong)headLength));
		var cues = Wide8 (MatroskaIds.Cues, NestedCuePoints (NestedDepth, CuePoint (0, Two (0x40))));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x55 })));
		var original = WideFile (head, info, cues, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = "Deep";
		var fs = new MockFileSystem ();
		fs.AddFile ("/deep.mkv", original);

		var saved = OnSmallStack (() => parsed.SaveToFile ("/deep.mkv", original, fs));

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/deep.mkv");
		Assert.AreEqual ("Deep", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.IsTrue (IndexOf (bytes, cues) > 0, "a CuePoint inside a CuePoint is not on a spec path, so the Cues are copied as they are");
	}

	[TestMethod]
	public void TryGrow_DeeplyNestedCuePoints_RendersOnASmallStack ()
	{
		// One spec CuePoint at 0xFFFF in two octets, which one more octet before it pushes past two
		// octets, so the Cues are written again; then 10,000 nested CuePoints.
		var tracks = Master (0x1654AE6B, new byte[] { 0xEC, 0x80 });
		var grown = Master (0x1654AE6B, new byte[] { 0xEC, 0x81, 0x00 });
		var nested = NestedCuePoints (NestedDepth, CuePoint (1, Two (0x40)));
		var cues = Wide8 (MatroskaIds.Cues, Concat (CuePoint (0, Two (0xFFFF)), nested));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x55 })));
		var original = WideFile (tracks, cues, cluster);

		var (ok, bytes, error) = OnSmallStack (() => (MatroskaRewrite.TryGrow (original, grown, out var output, out var message), output, message));

		Assert.IsTrue (ok, error);
		Assert.AreEqual (0x10000ul, CueValues (bytes)[0xF1][0], "the spec CuePoint is moved and widened");
		Assert.IsTrue (IndexOf (bytes, nested) > 0, "the nested CuePoints are copied as they are");
	}

	[TestMethod]
	public void TryGrow_CuePositionOffTheSpecPaths_IsCopiedAsItIs ()
	{
		// RFC 9559 section 5.1.5: CueClusterPosition lives in CueTrackPositions, CueRefCluster in
		// CueReference. The same IDs anywhere else in the Cues are not positions and are not moved,
		// in place or rendered.
		var info = Master (0x1549A966, Element (0x7BA9, "Info"u8));
		var stray = Concat (Element (0xF1, Two (0x40)), Element (0x97, Two (0x41)));
		var strayInCuePoint = Master (0xBB, Concat (Element (0xB3, new byte[] { 9 }), stray));
		var strayInCues = Element (0xF1, Two (0x42));
		var reference = Master (0xDB, Concat (Element (0x96, new byte[] { 1 }), Element (0x97, Two (0x43))));
		var clusterInReference = Master (0xBB, Concat (Element (0xB3, new byte[] { 8 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, Two (0x44)), reference))));
		foreach (var widen in new[] { false, true }) {
			// With widen, the spec CuePoint holds 0xFFFF and must grow, so the Cues are rendered.
			var spec = CuePoint (0, Two (widen ? 0xFFFF : 0x45));
			var tracks = Master (0x1654AE6B, new byte[] { 0xEC, 0x80 });
			var grown = Master (0x1654AE6B, new byte[] { 0xEC, 0x81, 0x00 });
			var cues = Master (MatroskaIds.Cues, Concat (spec, strayInCuePoint, strayInCues, clusterInReference));
			// The Cues start past every value, so only the Tracks octet moves them.
			var filler = WideVoid (0x10010 - tracks.Length - info.Length);
			var original = WideFile (tracks, info, filler, cues);

			Assert.IsTrue (MatroskaRewrite.TryGrow (original, grown, out var bytes, out var error), error);

			Assert.IsTrue (IndexOf (bytes, strayInCuePoint) > 0, FormattableString.Invariant ($"widen={widen}: positions directly in a CuePoint are not moved"));
			Assert.IsTrue (IndexOf (bytes, strayInCues) > 0, FormattableString.Invariant ($"widen={widen}: a position directly in the Cues is not moved"));
			var values = CueValues (bytes);
			CollectionAssert.AreEqual (new[] { widen ? 0x10000ul : 0x46ul, 0x45ul }, values[0xF1], FormattableString.Invariant ($"widen={widen}: both CueClusterPositions in CueTrackPositions moved"));
			Assert.AreEqual (0x44ul, values[0x97].Single (), FormattableString.Invariant ($"widen={widen}: the CueRefCluster in CueReference moved"));
		}
	}

	/// <summary>Runs <paramref name="body"/> on a thread with a 256 KiB stack and returns its result.</summary>
	static T OnSmallStack<T> (Func<T> body)
	{
		T result = default!;
		ExceptionDispatchInfo? failure = null;
		var thread = new Thread (() => {
			try {
				result = body ();
			} catch (Exception e) {
				failure = ExceptionDispatchInfo.Capture (e);
			}
		}, 256 * 1024);
		thread.Start ();
		thread.Join ();
		failure?.Throw ();
		return result;
	}

	/// <summary>
	/// <paramref name="depth"/> CuePoints, each the only child of the last, around
	/// <paramref name="inner"/>. Each has an eight-octet size, which RFC 8794 section 6.1 allows.
	/// </summary>
	static byte[] NestedCuePoints (int depth, byte[] inner)
	{
		const int header = 9;
		var bytes = new byte[(depth * header) + inner.Length];
		for (var level = 0; level < depth; level++) {
			var at = level * header;
			bytes[at] = 0xBB;
			WriteSize8 (bytes, at + 1, (ulong)(bytes.Length - at - header));
		}

		inner.CopyTo (bytes, depth * header);
		return bytes;
	}

	/// <summary>A four-octet-ID element with an eight-octet size.</summary>
	static byte[] Wide8 (ulong id, byte[] payload)
	{
		var bytes = new byte[12 + payload.Length];
		for (var i = 0; i < 4; i++)
			bytes[i] = (byte)(id >> (8 * (3 - i)));
		WriteSize8 (bytes, 4, (ulong)payload.Length);
		payload.CopyTo (bytes, 12);
		return bytes;
	}

	static void WriteSize8 (byte[] bytes, int at, ulong size)
	{
		bytes[at] = 0x01;
		for (var i = 1; i < 8; i++)
			bytes[at + i] = (byte)(size >> (8 * (7 - i)));
	}
}
