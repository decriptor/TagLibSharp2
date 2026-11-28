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

public partial class ExifEditTests
{
	[TestMethod]
	public void UnchangedExifRequest_TakesNoSteps ()
	{
		var tiff = new TiffBuilder (30).Header (8).Table (8, [(0x010E, 2, 4, 0x41424344)]).ToArray ();
		var tag = new ImageTag (MediaFormat.Jpeg, ImageStores.Comment);
		IfdSteps.Count = 0;

		Assert.IsTrue (ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error), error);

		Assert.IsFalse (changed);
		Assert.AreEqual (0, IfdSteps.Count);
	}

	[TestMethod]
	public void CommentEdit_TakesTheMeasuredSteps ()
	{
		var simple = Steps (new TiffBuilder (30).Header (8).Table (8, [(0x9286, 7, 4, 0x41424344)]).ToArray ());
		var strips = Steps (new TiffBuilder (80).Header (8).Table (8, [(0x9286, 7, 4, 0x41424344), (0x0111, 3, 2, 50), (0x0117, 3, 2, 54)])
			.U16 (50, 1).U16 (52, 1).U16 (54, 4).U16 (56, 4).ToArray ());
		var note = Steps (new TiffBuilder (160).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x9286, 7, 4, 0x41424344), (0x927C, 7, 30, 80)])
			.Table (80, [(0x0001, 7, 4, 100)])
			.Bytes (100, "KEEP"u8.ToArray ()).ToArray ());
		var foreign = ForeignSteps ();

		Assert.IsTrue (simple <= 60 && strips <= 160 && note <= 320 && foreign <= 64, $"{simple},{strips},{note},{foreign}");
	}

	[TestMethod]
	public void KeptTableThatCoversACompactedOne_Refuses ()
	{
		// IFD0 is not edited and its bytes run through offset 62. The Exif IFD at 40 is inside those bytes and
		// holds the comment, so compacting it would rewrite IFD0. The furthest-end of the kept table is what
		// sees that.
		var bytes = new TiffBuilder (80).Header (8)
			.Table (8, [(0x8769, 4, 1, 40), (0x010F, 2, 4, 0x41424344), (0x0110, 2, 4, 0x41424344), (0x0112, 3, 1, 1)])
			.Table (40, [(0x9286, 7, 4, 0x41424344)])
			.ToArray ();

		Assert.IsFalse (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error));

		Assert.IsFalse (changed);
		StringAssert.Contains (error, ExifEditor.TablesOverlap);
	}

	static long Steps (byte[] tiff)
	{
		var tag = CommentTag ();
		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove (tiff, 0, tag, "eXIf", out var changed, out var error), error);
		Assert.IsTrue (changed);
		return IfdSteps.Count;
	}

	static long ForeignSteps ()
	{
		var block = new byte[32];
		"XX\0*"u8.CopyTo (block);
		var tag = CommentTag ();
		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove (block, 0, tag, "eXIf", out var changed, out var error), error);
		Assert.IsFalse (changed);
		return IfdSteps.Count;
	}

	[TestMethod]
	public void UnreadTable_ProtectsTheTailAShortCountWouldLeave ()
	{
		// (length - 80 - 2) % 12 == 11, so a count of 1 does not fit and the table is unread.
		// The comment's bytes are the last of the file, inside that protected tail.
		var bytes = new TiffBuilder (93).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x9286, 7, 5, 91)], next: 80)
			.U16 (80, 1)
			.ToArray ();

		Assert.IsFalse (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error));
		Assert.IsFalse (changed);
		Assert.IsNotNull (error);
	}

	[TestMethod]
	public void ProtectOnlyValueInsideTheEditedTable_RefusesBecauseTheStructureWouldChange ()
	{
		var bytes = new TiffBuilder (120).Header (8)
			.Table (8, [(0x8769, 4, 1, 40), (0x9286, 7, 4, 0x41424344)])
			.Table (40, [(0x010F, 2, 4, 0x41424344)], next: 70)
			.Table (70, [(0x004E, 7, 6, 10)])
			.ToArray ();

		Assert.IsFalse (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error));

		Assert.IsFalse (changed);
		StringAssert.Contains (error, ExifEditor.StructureWouldChange);
	}

	static ImageTag CommentTag ()
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Comment);
		tag.Comment = "New";
		return tag;
	}
}
