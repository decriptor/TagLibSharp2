// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Png;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A second set of boundaries. Each one is a result a survived mutant changes: a cleared byte, a refusal
/// sentence, or a chunk that must stay.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class MutationPinWave2Tests
{
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
	public void MakerTargetCoveringTheMiddleOfAComment_KeepsOnlyThatMiddle ()
	{
		var bytes = CommentBesideMakerNote (ifdAt: 80, prefix: 0, commentAt: 160, comment: [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88], targetAt: 162, targetCount: 6);
		bytes[168] = 0x5A;

		IfdSteps.Count = 0;
		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[] { 0x00, 0x00, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, bytes[160..168]);
		Assert.AreEqual (0x5A, bytes[168]);
		Assert.AreEqual (131L, IfdSteps.Count);
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
	public void Gif87aClear_KeepsThe87aSignature ()
	{
		var old = new byte[14];
		"GIF87a"u8.CopyTo (old);
		old[6] = 1;
		old[8] = 1;
		old[13] = 0x3B;
		var file = Open (old, "a.gif");
		file.Tag!.Clear ();

		CollectionAssert.AreEqual ("GIF87a"u8.ToArray (), file.Render ().Span.Slice (0, 6).ToArray ());
	}

	[TestMethod]
	public void MakerIfdWhoseNextFieldSticksOutOfTheNote_DoesNotKeepTheComment ()
	{
		// The entry fits in the 14-byte note. The 4-byte next field does not, so the note is not an IFD and its
		// target does not protect the comment.
		var comment = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
		var bytes = new TiffBuilder (200).Header (8)
			.Table (8, [(0x8769, 4, 1, 26)])
			.Table (26, [(0x927C, 7, 14, 80), (0x9286, 7, comment.Length, 160)])
			.Entries (80, (0x004E, 7, comment.Length, 160))
			.Bytes (160, comment)
			.ToArray ();

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[comment.Length], bytes[160..168]);
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
	public void MakerIfdWithARepeatedTag_DoesNotKeepTheComment ()
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
		CollectionAssert.AreEqual (new byte[comment.Length], bytes[160..168]);
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
	public void Gif_NetscapeIdOnADifferentLabelOrBlockLength_IsDroppedOnClear ()
	{
		var wrongLabel = GifWithExtension (ApplicationWithSize (0x99, 11, "NETSCAPE2.0"));
		var wrongSize = GifWithExtension (ApplicationWithSize (0xFF, 12, "NETSCAPE2.0" + "X"));
		var labelFile = Open (wrongLabel, "a.gif");
		var sizeFile = Open (wrongSize, "b.gif");
		labelFile.Tag!.Clear ();
		sizeFile.Tag!.Clear ();

		CollectionAssert.AreEqual (GifTrailerOnly (), labelFile.Render ().Span.ToArray ());
		CollectionAssert.AreEqual (GifTrailerOnly (), sizeFile.Render ().Span.ToArray ());
	}

	[TestMethod]
	public void BinaryByte_StopsTheMakerNoteBeforeAnIfdThatWouldKeepTheComment ()
	{
		var bytes = CommentBesideMakerNote (ifdAt: 81, prefix: 0x1F, commentAt: 160, comment: [0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB], targetAt: 160, targetCount: 6);

		Assert.IsTrue (ExifEditor.TryRemove (bytes, 0, CommentTag (), "eXIf", out var changed, out var error), error);

		Assert.IsTrue (changed);
		CollectionAssert.AreEqual (new byte[] { 0, 0, 0, 0, 0, 0 }, bytes[160..166]);
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

	[TestMethod]
	public void Gif87a_GainsThe89aSignature_AndALocalColorTableStaysWithItsImage ()
	{
		var old = new byte[14];
		"GIF87a"u8.CopyTo (old);
		old[6] = 1;
		old[8] = 1;
		old[13] = 0x3B;
		var upgraded = Open (old, "a.gif");
		upgraded.Tag!.Comment = "Z";
		CollectionAssert.AreEqual ("GIF89a"u8.ToArray (), upgraded.Render ().Span.Slice (0, 6).ToArray ());

		var gif = GifWithLocalTable ();
		var file = Open (gif, "b.gif");
		file.Tag!.Comment = "Z";
		var output = file.Render ().ToArray ();
		var image = output.AsSpan ().IndexOf ((byte)0x2C);
		Assert.IsTrue (image > 0);
		CollectionAssert.AreEqual (new byte[] { 0x21, 0xFE, 1, (byte)'Z', 0 }, output.AsSpan (13, 5).ToArray ());
		CollectionAssert.AreEqual (new byte[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }, output.AsSpan (image + 10, 6).ToArray ());
	}

	[TestMethod]
	public void Gif_UnknownIntroducer_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var data = GifWithExtension ([0x2A, 0x00, 0x00]);

		Assert.IsNull (GifRewriter.Plan (data, tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void GifCommentEdit_KeepsAnApplicationBlockThatMentionsUserComment ()
	{
		var block = new byte[3 + 11 + 1 + 11 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		"XXXXXXXXXXX"u8.CopyTo (block.AsSpan (3));
		block[14] = 11;
		"UserComment"u8.CopyTo (block.AsSpan (15));
		var data = GifWithExtension (block);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		var plan = GifRewriter.Plan (data, tag, out var error);

		Assert.IsNotNull (plan, error);
		var output = plan!.Materialize (data);
		CollectionAssert.AreEqual ("XXXXXXXXXXX"u8.ToArray (), output.AsSpan (13 + 5 + 3, 11).ToArray ());
	}

	[TestMethod]
	public void Gif_ShortImageDescriptor_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;

		Assert.IsNull (GifRewriter.Plan (Concat (header, [0x2C, 0, 0, 0, 0]), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_ImageColorTableCutOffBeforeTheTrailer_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var descriptor = new byte[11];
		descriptor[0] = 0x2C;
		descriptor[9] = 0x80;
		descriptor[10] = 0x3B;

		Assert.IsNull (GifRewriter.Plan (Concat (header, descriptor), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_SubBlockLongerThanTheRestOfTheFile_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		Assert.IsNull (GifRewriter.Plan (GifWithExtension ([0x21, 0xFE, 5, 0x3B]), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_CommentThenACutOffImage_DoesNotYieldTheComment ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var data = Concat (header, [0x21, 0xFE, 2, (byte)'H', (byte)'i', 0, 0x2C, 0, 0]);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read (data, tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_CommentThatEndsTheFileWithoutATrailer_IsNotRead ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var data = Concat (header, [0x21, 0xFE, 2, (byte)'H', (byte)'i', 0]);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read (data, tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_ReadOfANonGif_KeepsACommentAlreadySet ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read ([1, 2, 3], tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_TenByteSignature_NamesTheTruncatedHeader ()
	{
		var data = new byte[10];
		"GIF89a"u8.CopyTo (data);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		Assert.IsNull (GifRewriter.Plan (data, tag, out var error));
		Assert.AreEqual (GifRewriter.HeaderError, error);
	}

	[TestMethod]
	public void Gif_SubBlockThatFillsTheFileWithoutATerminator_IsNotAPlan ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;

		Assert.IsNull (GifRewriter.Plan (Concat (header, [0x21, 0xFE, 1, (byte)'A']), tag, out var error));
		Assert.AreEqual (GifRewriter.SpliceError, error);
	}

	[TestMethod]
	public void Gif_CommentSubBlockCutOff_KeepsACommentAlreadySet ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "keep";

		GifRewriter.Read (Concat (header, [0x21, 0xFE, 5, (byte)'A']), tag);

		Assert.AreEqual ("keep", tag.Comment);
	}

	[TestMethod]
	public void Gif_CommentLongerThanOneSubBlock_SplitsAt255 ()
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		var data = Concat (header, [0x3B]);
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = new string ('A', 300);

		var plan = GifRewriter.Plan (data, tag, out var error);
		Assert.IsNotNull (plan, error);
		var output = plan!.Materialize (data);

		Assert.AreEqual (0x21, output[13]);
		Assert.AreEqual (0xFE, output[14]);
		Assert.AreEqual (255, output[15]);
		Assert.AreEqual ((byte)'A', output[16]);
		Assert.AreEqual (45, output[16 + 255]);
	}

	// A packet whose UTF-8 length is a multiple of 255 fills whole sub-blocks. The extension still ends on one
	// zero; a second zero is the next block and stops the reader before the trailer.
	[TestMethod]
	public void Gif_XmpSubBlockPacketFillingWholeBlocks_EndsTheExtensionOnce ()
	{
		var packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>"
			+ "<rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:UserComment><rdf:Alt>"
			+ "<rdf:li xml:lang='x-default'>Old</rdf:li></rdf:Alt></exif:UserComment></rdf:Description></rdf:RDF></x:xmpmeta>";
		var data = GifWithExtension (GifSubBlockXmp (packet));
		string? comment = null;
		byte[]? output = null;
		for (var n = 1; n <= 255 && output is null; n++) {
			comment = new string ('B', n);
			var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
			tag.Comment = comment;
			var plan = GifRewriter.Plan (data, tag, out var error);
			Assert.IsNotNull (plan, error);
			var rendered = plan!.Materialize (data);
			if (XmpSubBlockPayload (rendered) % 255 == 0)
				output = rendered;
		}

		Assert.IsNotNull (output);
		var read = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		GifRewriter.Read (output, read);
		Assert.AreEqual (comment, read.Comment);
	}

	[TestMethod]
	public void Gif_XmpPacketThatIsNotXml_RefusesTheCommentEdit ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";

		Assert.IsNull (GifRewriter.Plan (GifWithExtension (GifXmp ("<UserComment")), tag, out var error));
		Assert.IsNotNull (error);
		StringAssert.Contains (error, "GIF XMP packet");
		StringAssert.Contains (error, "not well-formed XML");
	}

	[TestMethod]
	public void Gif_TruncatedHeaderAndAnUnlocatableXmpComment_NameThemselves ()
	{
		var tag = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		tag.Comment = "Z";
		Assert.IsNull (GifRewriter.Plan (new byte[10], tag, out var headerError));
		Assert.AreEqual (GifRewriter.HeaderError, headerError);

		var xmp = GifWithExtension (XmpUserCommentBlock ());
		Assert.IsNull (GifRewriter.Plan (xmp, tag, out var xmpError));
		Assert.AreEqual (
			"The GIF XMP extension is neither the XMP raw form with its magic trailer nor data sub-blocks, and holds a UserComment, so that copy of Comment cannot be located and updated.",
			xmpError);
	}

	[TestMethod]
	public void GifClear_KeepsTheLoopExtension_AndDropsAnUnknownApplicationExtension ()
	{
		var gif = GifWithExtension (Application ("XXXXXXXXXXX"), Application ("NETSCAPE2.0"));
		var file = Open (gif, "a.gif");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("NETSCAPE2.0"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("XXXXXXXXXXX"u8) < 0);
	}

	[TestMethod]
	public void PngClear_KeepsGama_AndDropsALookalikeAncillaryChunk ()
	{
		var cleared = Open (Png (PngChunk ("gAMA", [0, 0, 0, 1]), PngChunk ("aaaS", [1]), Text ("Comment", "hi")), "a.png");
		cleared.Tag!.Clear ();
		var afterClear = cleared.Render ().ToArray ();
		Assert.IsTrue (afterClear.AsSpan ().IndexOf ("gAMA"u8) >= 0);
		Assert.IsTrue (afterClear.AsSpan ().IndexOf ("aaaS"u8) < 0);
	}

	[TestMethod]
	public void Png_DropsAnEmptyIdotWhenAChunkBetweenItAndIdatChanges ()
	{
		var file = Open (Png (PngChunk ("iDOT", []), Text ("Comment", "old")), "b.png");
		file.Tag!.Comment = "Z";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("iDOT"u8) < 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("IDAT"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("old"u8) < 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("Comment"u8) >= 0);
	}

	[TestMethod]
	public void PngWithoutIdat_KeepsIdotWhenTextIsInserted ()
	{
		var file = Open (PngEndingAtIend (PngChunk ("iDOT", [0, 0, 0, 1])), "a.png");
		file.Tag!.Title = "t";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("iDOT"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("Title"u8) >= 0);
	}

	[TestMethod]
	public void PngText_IsInsertedBeforeTheFirstIdat ()
	{
		var file = Open (Png (PngChunk ("IDAT", [1]), PngChunk ("iDOT", [0, 0, 0, 1])), "a.png");
		file.Tag!.Title = "t";
		var output = file.Render ().ToArray ();
		var title = output.AsSpan ().IndexOf ("Title"u8);
		var idat = output.AsSpan ().IndexOf ("IDAT"u8);

		Assert.IsTrue (title >= 0);
		Assert.IsTrue (idat > title);
	}

	[TestMethod]
	public void Png_BadCrcChunk_IsKeptWhenAnotherFieldChanges ()
	{
		var gama = PngChunk ("gAMA", [0, 0, 0, 1]);
		gama[^1] ^= 0xFF;
		var file = Open (Png (gama), "a.png");
		file.Tag!.Title = "t";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("Title"u8) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("gAMA"u8) >= 0);
	}

	[TestMethod]
	public void Png_ExifThatIsNotTiff_NamesTheExifChunk ()
	{
		var data = Png (PngChunk ("eXIf", [1, 2, 3, 4, 0x01, 0x3B, 0, 2, 9]));
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Artist);
		tag.Artist = "N";

		Assert.IsNull (PngRewriter.Plan (data, tag, out var error));
		Assert.AreEqual ("The eXIf block is not a TIFF structure, so its copy of Artist cannot be updated.", error);
	}

	[TestMethod]
	public void Png_CompressedXmpPastTheSharedBudget_NamesOneMebibyte ()
	{
		var data = Png (Xmp (new string (' ', PngText.MaxInflatedText + 1), compressed: true));
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Title);
		tag.Title = "t";

		Assert.IsNull (PngRewriter.Plan (data, tag, out var error));
		Assert.AreEqual (
			"The PNG XML:com.adobe.xmp chunk does not fit in the 1 MiB that all compressed text in the file shares, so its copy of a changed field cannot be updated.",
			error);
	}

	[TestMethod]
	public void PngClear_DropsAChunkWhoseThirdByteMatchesAKeptName ()
	{
		// "xxNS" shares tRNS's last two letters. A clear keeps tRNS and drops this ancillary chunk.
		var png = Png (PngChunk ("gAMA", [0, 0, 0, 1]), PngChunk ("xxNS", [1]));
		var file = Open (png, "a.png");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf ("gAMA"u8) > 0);
		Assert.AreEqual (-1, output.AsSpan ().IndexOf ("xxNS"u8));
	}

	[TestMethod]
	public void Png_TextWithoutAKeyword_StaysWhenAnotherFieldChanges ()
	{
		var marker = new byte[] { 0xAB, 0xCD, 0xEF, 0x11 };
		var file = Open (Png (PngChunk ("tEXt", marker), Text ("Comment", "old")), "a.png");
		file.Tag!.Comment = "Z";
		var output = file.Render ().ToArray ();

		Assert.IsTrue (output.AsSpan ().IndexOf (marker) >= 0);
		Assert.IsTrue (output.AsSpan ().IndexOf ("old"u8) < 0);
	}

	[TestMethod]
	public void Svg_UnclosedXmlDeclaration_IsNotSvg ()
	{
		Assert.IsFalse (SvgSize.TryRead ("<?xml version=\"1.0\" <svg width=\"1\" height=\"1\"/>"u8, out var properties));
		Assert.IsNull (properties);
	}

	[TestMethod]
	public void Svg_ProcessingInstructionBeforeTheRoot_StillHasTheSize ()
	{
		Assert.IsTrue (SvgSize.TryRead ("<?pi?> <svg width=\"1\" height=\"1\"/>"u8, out var properties));
		Assert.AreEqual ("1x1", properties is { } size ? $"{size.Width}x{size.Height}" : "none");
	}

	[TestMethod]
	public void Svg_UnclosedProcessingInstruction_IsNotSvg ()
	{
		Assert.IsFalse (SvgSize.TryRead ("<?pi <svg width=\"1\" height=\"1\"/>"u8, out var properties));
		Assert.IsNull (properties);
	}

	[TestMethod]
	[DataRow ("<svg width=\"1\" height=\"1\"", "True|True|1x1")]
	[DataRow ("<svg width=\"1e2\" height=\"1\"/>", "True|True|100x1")]
	[DataRow ("<svg width=\"1.5\" height=\"1\"/>", "True|True|2x1")]
	[DataRow ("<svg width=\"50%\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<svg width=\"1em\" height=\"1\"/>", "True|True|none")]
	public void Svg_MoreBoundaries (string markup, string expected)
	{
		var data = Encoding.UTF8.GetBytes (markup);
		var read = SvgSize.TryRead (data, out var properties);
		Assert.AreEqual (expected, $"{SvgSize.IsSvg (data)}|{read}|{(properties is { } size ? $"{size.Width}x{size.Height}" : "none")}");
	}

	[TestMethod]
	[DataRow ("<svg width=\"10\" height", "True|True|none")]
	[DataRow ("<svg width=50% height=1>", "True|True|none")]
	[DataRow ("<svg width=\"1px", "True|True|none")]
	[DataRow ("<svg width=\"10\"", "True|True|none")]
	[DataRow ("<svg width=\"0.4\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<svg width=1.2.3 height=1>", "True|True|1x1")]
	[DataRow ("<svg width=\"1e+2\" height=\"1\"/>", "True|True|100x1")]
	[DataRow ("<svg width = \"2\" height = \"3\"/>", "True|True|2x3")]
	[DataRow ("<svg width=10 height=20>", "True|True|10x20")]
	[DataRow ("<svg width=\"10\" height=\"x\"/>", "True|True|none")]
	[DataRow ("<svg width=\"10 px\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<svg width=\"1e20\" height=\"1\"/>", "True|True|none")]
	[DataRow ("<!DOCTYPE svg \"x\"> <svg width=\"1\" height=\"2\"/>", "True|True|1x2")]
	[DataRow ("<!DOCTYPE svg \">\"> <svg width=\"1\" height=\"2\"/>", "True|True|1x2")]
	[DataRow ("<svg viewBox=\"0 0 4 5", "True|True|none")]
	[DataRow ("<svg viewBox=0 0 4 5>", "True|True|none")]
	[DataRow ("<svg width=\"10\">", "True|True|none")]
	[DataRow ("<svg w=20>", "True|True|none")]
	[DataRow ("<svg width=2 3 height=1>", "True|True|2x1")]
	[DataRow ("<svg width=10", "True|True|none")]
	[DataRow ("<svg width=\"1e", "True|True|none")]
	[DataRow ("<svg width=\"1e2", "True|True|none")]
	[DataRow ("<svg ab5 width=9 xheight", "True|True|none")]
	public void Svg_EdgesASurvivingComparisonChanges (string markup, string expected)
	{
		var data = Encoding.UTF8.GetBytes (markup);
		var read = SvgSize.TryRead (data, out var properties);
		Assert.AreEqual (expected, $"{SvgSize.IsSvg (data)}|{read}|{(properties is { } size ? $"{size.Width}x{size.Height}" : "none")}");
	}

	[TestMethod]
	public void Png_TextWithABadCrc_IsNotAComment ()
	{
		var chunk = Text ("Comment", "Secret");
		chunk[^1] ^= 0xFF;
		var opened = StillImageFile.Read (Png (chunk));

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsNull (opened.File!.Tag!.Comment);
	}

	[TestMethod]
	public void Png_RawXmpThatNamesTheEditedFieldAndIsNotXml_IsRefused ()
	{
		var packet = XmpPacket (Alt ("dc:title", "Old")).Replace ("</rdf:RDF>", "", StringComparison.Ordinal);
		var file = Open (Png (RawProfile ("xmp", Encoding.UTF8.GetBytes (packet))), "a.png");
		file.Tag!.Title = "New";

		var saved = file.SaveToFile ("/a.png", new MockFileSystem ());

		Assert.IsFalse (saved.IsSuccess, saved.Error);
		StringAssert.Contains (saved.Error, "not well-formed");
	}

	[TestMethod]
	public void RenderTag_UsesTheFormatComment_AndAnUnknownContainerIsRefused ()
	{
		var gif = new ImageTag (MediaFormat.Gif, ImageStores.Comment);
		gif.Comment = "Hi";
		var gifBytes = ImageRewrite.RenderTag (gif);
		Assert.AreEqual (0x21, gifBytes[0]);
		Assert.AreEqual (0xFE, gifBytes[1]);

		var jpeg = new ImageTag (MediaFormat.Jpeg, ImageStores.Comment);
		jpeg.Comment = "Hi";
		var jpegBytes = ImageRewrite.RenderTag (jpeg);
		Assert.AreEqual (0xFF, jpegBytes[0]);
		Assert.AreEqual (0xFE, jpegBytes[1]);

		Assert.IsFalse (ImageRewrite.TryPlan ([1, 2, 3, 4], MediaFormat.Unknown, null, out var plan, out var error));
		Assert.IsNull (plan);
		Assert.AreEqual ("The image container could not be updated.", error);

		var carrier = new byte[14];
		"GIF89a"u8.CopyTo (carrier);
		carrier[6] = 1;
		carrier[8] = 1;
		carrier[13] = 0x3B;
		Assert.IsTrue (ImageRewrite.TryPlan (carrier, MediaFormat.Gif, gif, out var edited, out var editError), editError);
		Assert.IsNotNull (edited);
	}

	[TestMethod]
	public void ReadFromFile_NamesAnIoFailure ()
	{
		var broken = new IOFileSystem (new IOException ("disk"));
		var sync = StillImageFile.ReadFromFile ("/a.jpg", broken);
		var async = StillImageFile.ReadFromFileAsync ("/a.jpg", broken).GetAwaiter ().GetResult ();

		Assert.AreEqual ("Failed to read file: disk", sync.Error);
		Assert.AreEqual ("Failed to read file: disk", async.Error);

		var denied = StillImageFile.ReadFromFile ("/a.jpg", new IOFileSystem (new UnauthorizedAccessException ("no")));
		Assert.AreEqual ("Access denied: no", denied.Error);
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

	static byte[] GifWithLocalTable ()
	{
		var data = new byte[32];
		"GIF89a"u8.CopyTo (data);
		data[6] = 1;
		data[8] = 1;
		data[13] = 0x2C;
		data[18] = 1;
		data[20] = 1;
		data[22] = 0x80;
		for (var i = 0; i < 6; i++)
			data[23 + i] = (byte)(0xA0 + i);
		data[29] = 2;
		data[31] = 0x3B;
		return data;
	}

	static int XmpSubBlockPayload (byte[] data)
	{
		var at = data.AsSpan ().IndexOf ("XMP DataXMP"u8);
		Assert.IsTrue (at >= 0);
		var i = at + 11;
		var payload = 0;
		while (i < data.Length && data[i] != 0) {
			var size = data[i];
			payload += size;
			i += 1 + size;
		}

		return payload;
	}

	static byte[] GifWithExtension (params byte[][] blocks)
	{
		var header = new byte[13];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		return Concat (header, Concat (blocks), [0x3B]);
	}

	static byte[] Application (string id)
	{
		var block = new byte[3 + 11 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		Encoding.ASCII.GetBytes (id).CopyTo (block, 3);
		return block;
	}

	/// <summary>An extension whose first sub-block is <paramref name="payload"/>, then a zero terminator.</summary>
	static byte[] ApplicationWithSize (byte label, int size, string payload)
	{
		var block = new byte[3 + size + 1];
		block[0] = 0x21;
		block[1] = label;
		block[2] = (byte)size;
		Encoding.ASCII.GetBytes (payload).CopyTo (block, 3);
		return block;
	}

	static byte[] GifTrailerOnly ()
	{
		var data = new byte[14];
		"GIF89a"u8.CopyTo (data);
		data[6] = 1;
		data[8] = 1;
		data[13] = 0x3B;
		return data;
	}

	static byte[] XmpUserCommentBlock ()
	{
		// A real application extension: the 11-byte id, then one data sub-block, then the terminator.
		// The packet is not sub-block XML and has no magic trailer, and it names UserComment.
		var block = new byte[3 + 11 + 1 + 11 + 1];
		block[0] = 0x21;
		block[1] = 0xFF;
		block[2] = 11;
		"XMP DataXMP"u8.CopyTo (block.AsSpan (3));
		block[14] = 11;
		"UserComment"u8.CopyTo (block.AsSpan (15));
		return block;
	}

	static byte[] PngEndingAtIend (params byte[][] chunks) =>
		Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0]), Concat (chunks), PngChunk ("IEND", []));

	sealed class IOFileSystem (Exception error) : IFileSystem
	{
		public bool FileExists (string path) => true;

		public Stream OpenRead (string path) => throw error;

		public Stream OpenReadWrite (string path) => throw error;

		public Stream Create (string path) => throw error;

		public byte[] ReadAllBytes (string path) => throw error;

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) => throw error;

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => throw error;

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			throw error;

		public void Delete (string path) => throw error;

		public void Move (string sourcePath, string destinationPath) => throw error;

		public string? GetDirectoryName (string path) => null;

		public string GetFileName (string path) => path;

		public string CombinePath (string path1, string path2) => path1 + path2;

		public IEnumerable<string> EnumerateFiles (string directory, string searchPattern)
		{
			_ = error;
			return [];
		}

		public DateTime? GetLastWriteTimeUtc (string path) => error is null ? null : null;
	}
}
