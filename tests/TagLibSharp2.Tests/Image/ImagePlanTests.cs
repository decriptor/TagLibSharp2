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

/// <summary>Image plan bounds: empty pieces, ranges past <see cref="int.MaxValue"/>, and the bytes beside a cut.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ImagePlanTests
{
	[TestMethod]
	public void ImagePlan_EmptyAppendsAndAZeroCopy_AddNothing ()
	{
		var plan = new ImagePlan ();
		plan.Add (ReadOnlySpan<byte>.Empty);
		plan.Add ([]);
		plan.Copy (5, 0);

		Assert.AreEqual (0, plan.Length);
		Assert.AreEqual (0, plan.SourceEnd);
	}

	[TestMethod]
	public void ImagePlan_NegativeCopy_Throws ()
	{
		var plan = new ImagePlan ();

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => plan.Copy (0, -1));
	}

	[TestMethod]
	public void ImagePlan_LengthPastInt32_ThrowsBeforeAllocatingANegativeArray ()
	{
		var plan = new ImagePlan ();
		plan.Copy (0, (long)int.MaxValue + 1);

		var error = Assert.ThrowsExactly<OverflowException> (() => plan.Materialize ([]));

		Assert.AreEqual ("Arithmetic operation resulted in an overflow.", error.Message);
	}

	[TestMethod]
	public void ImagePlan_HeldOffsetPastInt32_ThrowsBeforeSlicing ()
	{
		var plan = new ImagePlan ();
		plan.Copy ((long)int.MaxValue + 1, 1);
		using var destination = new MemoryStream ();
		var chunk = StreamCopy.Rent ();
		try {
			var error = Assert.ThrowsExactly<OverflowException> (() => plan.Write (null, new byte[1], destination, chunk));
			Assert.AreEqual ("Arithmetic operation resulted in an overflow.", error.Message);
		} finally {
			StreamCopy.Return (chunk);
		}
	}

	[TestMethod]
	public void ImagePlan_LengthAtTwoToTheThirtyTwo_ThrowsBeforeAllocating ()
	{
		var plan = new ImagePlan ();
		plan.Copy (0, int.MaxValue);
		plan.Copy (0, int.MaxValue);
		plan.Copy (0, 2);

		Assert.AreEqual (1L << 32, plan.Length);
		var error = Assert.ThrowsExactly<OverflowException> (() => plan.Materialize ([]));
		Assert.AreEqual ("Arithmetic operation resulted in an overflow.", error.Message);
	}

	[TestMethod]
	public void ImagePlan_RangeOfTwoToTheThirtyTwo_ThrowsBeforeAllocating ()
	{
		var plan = new ImagePlan ();
		plan.Copy (0, int.MaxValue);
		plan.Copy (0, int.MaxValue);
		plan.Copy (0, 2);

		var error = Assert.ThrowsExactly<OverflowException> (() => plan.MaterializeRange (0, 1L << 32, []));
		Assert.AreEqual ("Arithmetic operation resulted in an overflow.", error.Message);
	}

	[TestMethod]
	public void ImagePlan_HeldLengthPastInt32_ThrowsBeforeSlicing ()
	{
		var plan = new ImagePlan ();
		plan.Copy (0, (long)int.MaxValue + 1);
		using var destination = new MemoryStream ();
		var chunk = StreamCopy.Rent ();
		try {
			var error = Assert.ThrowsExactly<OverflowException> (() => plan.Write (null, new byte[1], destination, chunk));
			Assert.AreEqual ("Arithmetic operation resulted in an overflow.", error.Message);
		} finally {
			StreamCopy.Return (chunk);
		}
	}

	[TestMethod]
	public void ImagePlan_WindowWhosePiecePassesInt32_ThrowsBeforeSlicing ()
	{
		var plan = new ImagePlan ();
		plan.Copy (0, (long)int.MaxValue - 5);
		plan.Copy ((long)int.MaxValue - 5, 20);

		var error = Assert.ThrowsExactly<OverflowException> (() => plan.MaterializeRange ((long)int.MaxValue + 1, 5, []));
		Assert.AreEqual ("Arithmetic operation resulted in an overflow.", error.Message);
	}

	[TestMethod]
	public void ImagePlan_MaterializeShared_ReturnsTheSourceOnlyForTheWholeFile ()
	{
		var source = new byte[] { 1, 2, 3, 4 };
		var whole = new ImagePlan ();
		whole.Copy (0, 2);
		whole.Copy (2, 2);
		Assert.IsTrue (MemoryMarshal.TryGetArray (whole.MaterializeShared (source), out ArraySegment<byte> shared));
		Assert.AreSame (source, shared.Array);

		var half = new ImagePlan ();
		half.Copy (0, 2);
		CollectionAssert.AreEqual (new byte[] { 1, 2 }, half.MaterializeShared (source).ToArray ());

		var literal = new ImagePlan ();
		literal.Add (new byte[] { 9, 9, 9, 9 });
		CollectionAssert.AreEqual (new byte[] { 9, 9, 9, 9 }, literal.MaterializeShared (source).ToArray ());

		var shifted = new ImagePlan ();
		shifted.Copy (1, source.Length);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => shifted.MaterializeShared (source));
	}

	[TestMethod]
	public void ImagePlan_MaterializeRange_HonoursEmptyExactAndPieceBoundaries ()
	{
		var source = new byte[] { 1, 2, 3, 4 };
		var plan = new ImagePlan ();
		plan.Copy (0, 4);
		plan.Add ([5, 6]);

		Assert.AreEqual (0, plan.MaterializeRange (0, 0, source).Length);
		Assert.AreEqual (0, plan.MaterializeRange (plan.Length, 0, source).Length);
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3, 4, 5, 6 }, plan.MaterializeRange (0, 6, source));
		CollectionAssert.AreEqual (new byte[] { 1 }, plan.MaterializeRange (0, 1, source));
		CollectionAssert.AreEqual (new byte[] { 2, 3 }, plan.MaterializeRange (1, 2, source));
		CollectionAssert.AreEqual (new byte[] { 5, 6 }, plan.MaterializeRange (4, 2, source));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => plan.MaterializeRange (-1, 1, source));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => plan.MaterializeRange (0, -1, source));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => plan.MaterializeRange (7, 0, source));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => plan.MaterializeRange (0, 7, source));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => plan.MaterializeRange (4, 3, source));
	}

	[TestMethod]
	public void OutputAt_NamesTheFirstByteOfACopiedRangeAndNotTheBytePastIt ()
	{
		var plan = new ImagePlan ();
		plan.Add (new byte[] { 7, 7 });
		plan.Copy (10, 4);

		Assert.AreEqual (-1L, plan.OutputAt (0));
		Assert.AreEqual (2L, plan.OutputAt (10));
		Assert.AreEqual (5L, plan.OutputAt (13));
		Assert.AreEqual (-1L, plan.OutputAt (14));
	}

	[TestMethod]
	public void ImagePlan_CutAndSplice_KeepBytesOutsideTheSpan ()
	{
		var source = new byte[] { 1, 2, 3, 4, 5, 6 };
		var plan = new ImagePlan ();
		plan.Copy (0, 3);
		plan.Copy (3, 3);
		plan.Cut (0, 0);
		plan.Cut (3, 2);
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3, 6 }, plan.Materialize (source));

		var spliced = new ImagePlan ();
		spliced.Copy (0, 4);
		spliced.Splice (0, 0, []);
		spliced.Splice (1, 2, [9, 8]);
		CollectionAssert.AreEqual (new byte[] { 1, 9, 8, 4 }, spliced.Materialize (source));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => spliced.Cut (-1, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => spliced.Cut (0, -1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => spliced.Splice (0, 1, [1, 2]));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => spliced.Splice (4, 1, [1]));
	}

	[TestMethod]
	public void ImagePlan_LiteralSpliceAndExactEndCut_KeepTheBytesBesideTheEdit ()
	{
		var source = new byte[] { 1, 2, 3, 4 };
		var literal = new ImagePlan ();
		literal.Add ([1, 2, 3, 4, 5]);
		literal.Splice (1, 2, [9, 8]);
		CollectionAssert.AreEqual (new byte[] { 1, 9, 8, 4, 5 }, literal.Materialize (source));

		var across = new ImagePlan ();
		across.Copy (0, 4);
		across.Add ([5, 6]);
		CollectionAssert.AreEqual (new byte[] { 2, 3, 4, 5, 6 }, across.MaterializeRange (1, 5, source));

		var second = new ImagePlan ();
		second.Add ([1, 2]);
		second.Add ([3, 4, 5, 6, 7]);
		CollectionAssert.AreEqual (new byte[] { 2, 3, 4 }, second.MaterializeRange (1, 3, []));

		var cut = new ImagePlan ();
		cut.Copy (0, 4);
		cut.Cut (8, 0);
		cut.Cut (0, 1);
		cut.Cut (0, cut.Length);
		Assert.AreEqual (0, cut.Length);

		var end = new ImagePlan ();
		end.Copy (0, 4);
		end.Splice (0, 2, [7, 7]);
		end.Splice (2, 2, [6, 6]);
		CollectionAssert.AreEqual (new byte[] { 7, 7, 6, 6 }, end.Materialize (source));

		var over = new ImagePlan ();
		over.Copy (0, 4);
		var error = Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => over.MaterializeRange (1, 4, source));
		Assert.AreEqual ("start", error.ParamName);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => over.Cut (1, 4));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => over.Splice (1, 4, [9, 9, 9, 9]));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => over.Splice (-1, 1, [9]));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => over.Splice (-5, 6, new byte[6]));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => over.Cut (-1, 1));
	}

	[TestMethod]
	public void ImagePlan_WriteFromHeld_WritesTheRangeAndTheLiteral_AndACancelledTokenWritesNothing ()
	{
		var source = new byte[] { 1, 2, 3, 4 };
		var plan = new ImagePlan ();
		plan.Copy (1, 2);
		plan.Add ([9]);
		using var destination = new MemoryStream ();
		var chunk = StreamCopy.Rent ();
		try {
			plan.Write (null, source, destination, chunk);
			CollectionAssert.AreEqual (new byte[] { 2, 3, 9 }, destination.ToArray ());

			var blocked = new ImagePlan ();
			blocked.Add ([1, 2, 3]);
			using var second = new MemoryStream ();
			Assert.ThrowsExactly<OperationCanceledException> (() =>
				blocked.Write (null, [], second, chunk, new CancellationToken (true)));
			Assert.AreEqual (0, second.Length);
		} finally {
			StreamCopy.Return (chunk);
		}
	}
}
