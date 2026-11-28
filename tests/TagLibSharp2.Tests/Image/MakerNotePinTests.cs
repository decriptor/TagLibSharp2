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

public partial class MakerNoteByteTests
{
	[TestMethod]
	public void MakerTargetCoveringTheMiddleOfAComment_KeepsOnlyThatMiddle ()
	{
		var bytes = CommentBesideMakerNote (ifdAt: 80, prefix: 0, commentAt: 160, comment: [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88], targetAt: 162, targetCount: 6);
		bytes[168] = 0x5A;

		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[] { 0x00, 0x00, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, bytes[160..168]);
		Assert.AreEqual (0x5A, bytes[168]);
		Assert.AreEqual (136L, IfdSteps.Count);
	}

	[TestMethod]
	public void MakerTargetsBeforeAndAfterAComment_KeepOnlyTheOverlap ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = MakerNoteWithTargets (96, 230, comment, [
			(0x0001, 7, 8, 180),
			(0x0002, 7, 8, 190),
			(0x0003, 7, 8, 200),
			(0x0004, 7, 8, 210),
			(0x004E, 7, 6, 232),
			(0x0100, 7, 5, 250),
		]);
		bytes[238] = 0x5A;
		bytes[249] = 0xA5;

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[] { 0x00, 0x00, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, bytes[230..238]);
		Assert.AreEqual (0x5A, bytes[238]);
		Assert.AreEqual (0xA5, bytes[249]);
	}

	[TestMethod]
	public void MakerNoteThatIsNotUndefined_DoesNotKeepItsTarget ()
	{
		var bytes = CommentBesideMakerNote (ifdAt: 80, prefix: 0, commentAt: 160, comment: [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88], targetAt: 162, targetCount: 6, noteType: 2);
		bytes[168] = 0x5A;

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, bytes[160..168]);
		Assert.AreEqual (0x5A, bytes[168]);
	}

	[TestMethod]
	public void MakerIfdWhoseNextFieldSticksOutOfTheNote_LeavesTheComment ()
	{
		// The entry fits in the 14-byte note. The 4-byte next field does not, so the note is not located and the
		// comment, which sits outside the tables, stays.
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 14, 80), (0x9286, 7, comment.Length, 160)])
			.Entries (80, (0x004E, 7, comment.Length, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void MakerTargetAtOffsetZero_KeepsTheComment ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = CommentBesideMakerNote (ifdAt: 80, prefix: 0, commentAt: 160, comment: comment, targetAt: 0, targetCount: 180);

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void MakerIfdAtTheLastPrefixByte_KeepsTheComment ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var prefix = new byte[32];
		Array.Fill (prefix, (byte)'A');
		var bytes = new TiffBuilder (220).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 50, 80), (0x9286, 7, comment.Length, 160)])
			.Bytes (80, prefix)
			.Entries (112, (0x004E, 7, comment.Length, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void MakerIfdWithARepeatedTag_LeavesTheComment ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 30, 80), (0x9286, 7, comment.Length, 160)])
			.Entries (80, (0x004E, 7, 4, 120), (0x004E, 7, comment.Length, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void MakerIfdOfTypeDouble_KeepsTheComment ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 18, 80), (0x9286, 7, comment.Length, 160)])
			.Entries (80, (0x004E, 12, 1, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void MakerValueThatRunsPastTheFile_DoesNotKeepTheComment ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 30, 80), (0x9286, 7, comment.Length, 180)])
			.Entries (80, (0x0001, 7, 50, 160), (0x004E, 7, comment.Length, 180))
			.Bytes (180, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[comment.Length], bytes[180..188]);
	}

	[TestMethod]
	public void MakerNoteAtTheStartOfTheFile_KeepsTheCommentItsIfdNames ()
	{
		// The note is the first 40 bytes, so it does not touch IFD0. Its pointer bytes are ASCII, and the IFD at
		// offset 8 names the comment, which sits outside the note.
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (40)
			.Entries (8, (0x004E, 7, comment.Length, 160))
			.Table (40, [(0x927C, 7, 40, 0), (0x9286, 7, comment.Length, 160)])
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void MakerTargetThatStartsBeforeTheNote_KeepsTheComment ()
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (220).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 18, 180), (0x9286, 7, comment.Length, 160)])
			.Entries (180, (0x004E, 7, comment.Length, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	[DataRow (0x20)]
	[DataRow (0x7E)]
	public void MakerPrefixOfABoundaryAsciiByte_StillReachesTheIfd (int prefix)
	{
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 19, 80), (0x9286, 7, comment.Length, 160)])
			.Bytes (80, [(byte)prefix])
			.Entries (81, (0x004E, 7, comment.Length, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..168]);
	}

	[TestMethod]
	public void SameArrayNamedByBothSidesOfAPair_KeepsTheCommentThatArrayCovers ()
	{
		var comment = new byte[] { 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x0111, 3, 60, 50), (0x0117, 3, 60, 50), (0x9286, 7, comment.Length, 180)])
			.Bytes (180, comment)
			.ToArray ();
		for (var n = 0; n < 60; n++)
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian (bytes.AsSpan (50 + (n * 2)), 100);

		ExifEditor.SkipCheck = true;
		try {
			Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);
			Assert.IsTrue (changed);
			CollectionAssert.AreEqual (comment, bytes[180..188]);
		} finally {
			ExifEditor.SkipCheck = false;
		}
	}

	[TestMethod]
	public void BinaryByte_StopsTheMakerNote_AndLeavesTheComment ()
	{
		var comment = new byte[] { 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB };
		var bytes = CommentBesideMakerNote (ifdAt: 81, prefix: 0x1F, commentAt: 160, comment: comment, targetAt: 160, targetCount: 6);

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (comment, bytes[160..166]);
	}

	/// <summary>
	/// IFD0 points at an Exif IFD that holds an out-of-line UserComment and a maker note. The note's one entry
	/// names <paramref name="targetCount"/> bytes at <paramref name="targetAt"/>.
	/// </summary>
	static byte[] CommentBesideMakerNote (int ifdAt, byte prefix, int commentAt, byte[] comment, int targetAt, int targetCount, int noteType = 7)
	{
		var blob = new byte[40];
		blob[0] = prefix;
		var built = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, noteType, blob.Length, 80), (0x9286, 7, comment.Length, commentAt)])
			.Bytes (80, blob)
			.Entries (ifdAt, (0x004E, 7, targetCount, targetAt))
			.Bytes (commentAt, comment);
		return built.ToArray ();
	}

	static byte[] MakerNoteWithTargets (int blobLength, int commentAt, byte[] comment, (int Tag, int Type, long Count, long Value)[] entries)
	{
		return new TiffBuilder (320).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, blobLength, 80), (0x9286, 7, comment.Length, commentAt)])
			.Bytes (80, new byte[blobLength])
			.Entries (80, entries)
			.Bytes (commentAt, comment)
			.ToArray ();
	}

	static ImageTag CommentTag ()
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Comment);
		tag.Comment = "New";
		return tag;
	}
}
