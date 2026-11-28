// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Png;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

public partial class ExifPlainTests
{
	[TestMethod]
	public void OneStripOffset_IsNotARepeatedPairTag ()
	{
		var bytes = new TiffBuilder (40).Header (8).Table (8, [(0x9286, 7, 4, 0x41424344), (0x0111, 3, 1, 1)]).ToArray ();
		var verdict = Judge (bytes, 0x9286);

		Assert.IsTrue (verdict.Plain, verdict.Why);
		Assert.IsTrue (verdict.Removes);
	}

	[TestMethod]
	public void PairCounts_ThatExceedHalfTheBlock_AreNotPlain ()
	{
		var over = new TiffBuilder (100).Header (8).Table (8, [(0x9286, 7, 4, 0x41424344), (0x0111, 3, 30, 40), (0x0117, 3, 26, 40)]).ToArray ();
		var verdict = Judge (over, 0x9286);
		Assert.IsFalse (verdict.Plain, verdict.Why);
		Assert.AreEqual (PlainExif.OverBudget, verdict.Why);
	}

	static PlainExif.Verdict Judge (byte[] bytes, int tag)
	{
		Assert.IsTrue (TiffReader.TryOpen (bytes, out var tiff, out var first));
		return PlainExif.Judge (tiff, IfdGraph.Walk (tiff, first), [tag]);
	}

	[TestMethod]
	public void ZeroCountStripOffset_IsPlain ()
	{
		var bytes = new TiffBuilder (40).Header (8).Table (8, [(0x9286, 7, 4, 0x41424344), (0x0111, 3, 0, 0)]).ToArray ();
		var verdict = Judge (bytes);

		Assert.IsTrue (verdict.Plain, verdict.Why);
		Assert.IsNull (verdict.Why);
		Assert.IsTrue (verdict.Removes);
	}

	[TestMethod]
	public void SkipCheck_StillRefusesWhenThePlanReadsPastHalfTheBlock ()
	{
		var bytes = new TiffBuilder (100).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x41424344), (0x0111, 3, 30, 40), (0x0117, 3, 26, 40)])
			.ToArray ();
		ExifEditor.SkipCheck = true;
		try {
			Assert.IsFalse (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error));
			Assert.IsFalse (changed);
			Assert.AreEqual (
				"The eXIf block names more strip, tile or thumbnail values than its data holds, so its copy of Comment cannot be updated.",
				error);
		} finally {
			ExifEditor.SkipCheck = false;
		}
	}

	[TestMethod]
	public void OverBudgetPlan_IsNotClean ()
	{
		var bytes = new TiffBuilder (100).Header (8)
			.Table (8, [(0x9286, 7, 4, 0x41424344), (0x0111, 3, 30, 40), (0x0117, 3, 26, 40)])
			.ToArray ();
		Assert.IsTrue (TiffReader.TryOpen (bytes, out var tiff, out var first));

		var plan = RemovalPlan.Mark (tiff, IfdGraph.Walk (tiff, first), [0x9286]);

		Assert.IsTrue (plan.ValueBudgetExceeded);
		Assert.IsFalse (plan.IsClean ());
	}

	static PlainExif.Verdict Judge (byte[] bytes)
	{
		Assert.IsTrue (TiffReader.TryOpen (bytes, out var tiff, out var first));
		return PlainExif.Judge (tiff, IfdGraph.Walk (tiff, first), [0x9286]);
	}

	static ImageTag CommentTag ()
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Comment);
		tag.Comment = "New";
		return tag;
	}
}
