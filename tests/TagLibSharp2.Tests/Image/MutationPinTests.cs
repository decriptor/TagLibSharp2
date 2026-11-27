// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// Boundaries the round-18 mutation run left alive: an off-by-one, a flipped operator, or a removed step
/// changes one of these results. Counts are the measured totals of one edit, so dropping or negating any
/// step that edit takes fails the assertion.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class MutationPinTests
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

		Assert.AreEqual ("33,78,93,58", $"{simple},{strips},{note},{foreign}");
	}

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

	[TestMethod]
	public void AsciiPointer_IsNotFollowed_AndAnArrayPastTheData_IsNotAPointer ()
	{
		var ascii = new TiffBuilder (40).Header (8).Table (8, [(0x8769, 2, 1, 1)]).ToArray ();
		Assert.IsTrue (TiffReader.TryOpen (ascii, out var text, out _));
		Assert.IsFalse (text.TryPointers (text.Entry (8, 0), out _));

		var past = new TiffBuilder (40).Header (8).Table (8, [(0x014A, 4, 5, 30)]).ToArray ();
		Assert.IsTrue (TiffReader.TryOpen (past, out var wide, out _));
		Assert.IsFalse (wide.TryPointers (wide.Entry (8, 0), out _));
	}

	[TestMethod]
	public void MotorolaOrder_RequiresBothBytes ()
	{
		Assert.IsFalse (TiffReader.IsTiff ([(byte)'M', (byte)'I', 0, 42, 0, 0, 0, 8]));
		Assert.IsTrue (TiffReader.IsTiff ([(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8]));
	}

	[TestMethod]
	public void Ifd0NextCutShort_IsNullForAJpeg ()
	{
		Assert.IsNull (TiffSize.Ifd0NextCutShort (Jpeg ()));
	}

	[TestMethod]
	public void InvalidProperties_AreNotStored ()
	{
		var file = new StillImageFile ([1, 2, 3], MediaFormat.Svg, new ImageProperties (0, 0, 0, "SVG"), null);

		Assert.IsNull (file.ImageProperties);
	}

	[TestMethod]
	public void ReadFromFile_NullPathThrows_AndAMissingFileNamesItself ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => StillImageFile.ReadFromFile (null!));
		var missing = StillImageFile.ReadFromFile ("/no/such.jpg", new MockFileSystem ());

		Assert.IsFalse (missing.IsSuccess);
		Assert.AreEqual ("File not found: /no/such.jpg", missing.Error);
	}

	[TestMethod]
	public void SaveWithoutASourcePath_NamesTheMissingPath_OnBothOverloads ()
	{
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);

		var sync = opened.File!.SaveToFile ();
		var async = opened.File.SaveToFileAsync ().GetAwaiter ().GetResult ();

		Assert.AreEqual ("No source path available. Use SaveToFile(path) instead.", sync.Error);
		Assert.AreEqual ("No source path available. Use SaveToFile(path) instead.", async.Error);
		Assert.ThrowsExactly<ArgumentNullException> (() => opened.File.SaveToFile ((string)null!));
		Assert.ThrowsExactly<ArgumentNullException> (() => opened.File.SaveToFileAsync ((string)null!).GetAwaiter ().GetResult ());
	}

	[TestMethod]
	public void SaveToFile_UsesTheFileSystemItWasGiven ()
	{
		var bmp = Bmp (1, 1);
		var store = new MockFileSystem ();
		var opened = StillImageFile.Read (bmp);
		Assert.IsTrue (opened.IsSuccess, opened.Error);

		var saved = opened.File!.SaveToFile ("/out.bmp", store);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		CollectionAssert.AreEqual (bmp, store.ReadAllBytes ("/out.bmp"));

		var asyncSaved = opened.File.SaveToFileAsync ("/out-async.bmp", store).GetAwaiter ().GetResult ();

		Assert.IsTrue (asyncSaved.IsSuccess, asyncSaved.Error);
		CollectionAssert.AreEqual (bmp, store.ReadAllBytes ("/out-async.bmp"));
	}

	[TestMethod]
	public void SaveToFile_NullPathThrowsBeforeTheFileSystemIsUsed ()
	{
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var files = new RootDirectoryFileSystem ();

		var sync = Assert.ThrowsExactly<ArgumentNullException> (() => opened.File!.SaveToFile ((string)null!, files));
		var async = Assert.ThrowsExactly<ArgumentNullException> (
			() => opened.File!.SaveToFileAsync ((string)null!, files).GetAwaiter ().GetResult ());

		Assert.AreEqual ("path", sync.ParamName);
		Assert.AreEqual ("path", async.ParamName);
		Assert.AreEqual (0, files.Calls);
	}

	[TestMethod]
	public void Save_SecondWriteReusesTheRentedBuffer ()
	{
		var bmp = Bmp (1, 1);
		var memory = new MockFileSystem ();
		var opened = StillImageFile.Read (bmp);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsTrue (opened.File!.SaveToFile ("/a.bmp", memory).IsSuccess);
		Bounds.AssertAllocatesAtMost (32768, () => {
			var again = opened.File.SaveToFile ("/b.bmp", memory);
			Assert.IsTrue (again.IsSuccess, again.Error);
		}, "held save");

		var stored = new MockFileSystem ();
		stored.AddFile ("/in.bmp", bmp);
		var fromFile = StillImageFile.ReadFromFile ("/in.bmp", stored);
		Assert.IsTrue (fromFile.IsSuccess, fromFile.Error);
		Assert.IsTrue (fromFile.File!.SaveToFile ("/out1.bmp", stored).IsSuccess);
		Bounds.AssertAllocatesAtMost (32768, () => {
			var again = fromFile.File.SaveToFile ("/out2.bmp", stored);
			Assert.IsTrue (again.IsSuccess, again.Error);
		}, "source save");
	}

	[TestMethod]
	public void EmptySourcePath_IsAMissingPath ()
	{
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.SetSource ("", null);

		var saved = opened.File.SaveToFile ();

		Assert.AreEqual ("No source path available. Use SaveToFile(path) instead.", saved.Error);
	}

	[TestMethod]
	public void SpliceFailure_DisposesTheSourceItOpened ()
	{
		var png = Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]));
		var fs = new ProbeFileSystem { ArmOpenTracking = true };
		fs.Add ("/a.png", png);
		var opened = StillImageFile.ReadFromFile ("/a.png", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Tag!.Title = "new";

		var saved = opened.File.SaveToFile ("/b.jpg", fs);

		Assert.IsFalse (saved.IsSuccess, saved.Error);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen!.WasDisposed);
	}

	[TestMethod]
	public void ReadResult_DefaultIsNotSuccess_AndFailuresDifferByError ()
	{
		Assert.IsFalse (default (StillImageFileReadResult).IsSuccess);
		Assert.IsFalse (StillImageFileReadResult.Failure ("a").Equals (StillImageFileReadResult.Failure ("b")));
		var opened = StillImageFile.Read (Bmp (1, 1));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsFalse (opened.Equals (StillImageFile.Read (Bmp (2, 2))));
	}

	[TestMethod]
	public void Gif_GlobalColorTable_StaysAheadOfTheComment_AndACharacterIsNotSplit ()
	{
		var gif = GifWithTable ();
		var file = Open (gif, "a.gif");
		file.Tag!.Comment = new string ('a', 254) + "é";
		var saved = file.SaveToFile ("/a.gif", new MockFileSystem ());
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var output = file.Render ().ToArray ();

		CollectionAssert.AreEqual (gif.AsSpan (13, 6).ToArray (), output.AsSpan (13, 6).ToArray ());
		var comment = GifCommentBytes (output);
		Assert.AreEqual (254, comment[2]);
		Assert.AreEqual (2, comment[2 + 1 + 254]);
		Assert.AreEqual (0xC3, comment[2 + 1 + 254 + 1]);
		Assert.AreEqual (0xA9, comment[2 + 1 + 254 + 2]);
	}

	[TestMethod]
	public void Png_EmptyArtistText_DoesNotEraseTheEarlierArtist ()
	{
		var png = Png (Text ("Artist", "Ada"), Text ("Artist", ""));
		var opened = StillImageFile.Read (png);

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual ("Ada", opened.File!.Tag!.Artist);
	}

	[TestMethod]
	public void Png_SignatureOnly_RefusesTheSpliceWithoutThrowing ()
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Title);
		tag.Title = "t";

		var plan = TagLibSharp2.Image.Png.PngRewriter.Plan (PngSignature, tag, out var error);

		Assert.IsNull (plan);
		Assert.AreEqual (TagLibSharp2.Image.Png.PngRewriter.SpliceError, error);
	}

	[TestMethod]
	[DataRow ("   ")]
	[DataRow ("<!--x--><!--y--><svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<!DOCTYPE svg><!--y--><svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<!DOCTYPE svg]><svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<!DOCTYPE svg <svg width=\"1\" height=\"2\"/>")]
	[DataRow ("<svg")]
	[DataRow ("<sv")]
	[DataRow ("<svg:foo width=\"1\" height=\"2\"/>")]
	[DataRow ("<svg title=\"a > b\" width=\"3\" height=\"4\"/>")]
	[DataRow ("<svg width=\"2147483647\" height=\"1\"/>")]
	[DataRow ("<svg width=\"0\" height=\"1\" viewBox=\"0 0 0 5\"/>")]
	[DataRow ("<svg width=\"1cm\" height=\"10mm\"/>")]
	[DataRow ("<svg width=\"1in\" height=\"1pt\"/>")]
	[DataRow ("<svg width=\"1pc\" height=\"1px\"/>")]
	public void Svg_BoundaryDocuments_KeepTheirSize (string markup)
	{
		var data = Encoding.UTF8.GetBytes (markup);
		var read = SvgSize.TryRead (data, out var properties);
		var detect = SvgSize.IsSvg (data);
		Assert.AreEqual (ExpectedSvg (markup), Describe (detect, read, properties), markup);
	}

	[TestMethod]
	public void Svg_Utf16Spaces_AreNotAnImage ()
	{
		var data = Encoding.Unicode.GetPreamble ().Concat (Encoding.Unicode.GetBytes ("   ")).ToArray ();

		Assert.IsFalse (SvgSize.IsSvg (data));
		Assert.IsFalse (SvgSize.TryRead (data, out var properties));
		Assert.IsNull (properties);
	}

	static string Describe (bool detect, bool read, ImageProperties? properties) =>
		$"{detect}|{read}|{(properties is { } size ? $"{size.Width}x{size.Height}" : "none")}";

	static string ExpectedSvg (string markup) => markup switch {
		"   " => "False|False|none",
		"<!--x--><!--y--><svg width=\"1\" height=\"2\"/>" => "True|True|1x2",
		"<!DOCTYPE svg><!--y--><svg width=\"1\" height=\"2\"/>" => "True|True|1x2",
		"<!DOCTYPE svg]><svg width=\"1\" height=\"2\"/>" => "True|True|1x2",
		"<!DOCTYPE svg <svg width=\"1\" height=\"2\"/>" => "False|False|none",
		"<svg" => "True|True|none",
		"<sv" => "False|False|none",
		"<svg:foo width=\"1\" height=\"2\"/>" => "False|False|none",
		"<svg title=\"a > b\" width=\"3\" height=\"4\"/>" => "True|True|3x4",
		"<svg width=\"2147483647\" height=\"1\"/>" => "True|True|2147483647x1",
		"<svg width=\"0\" height=\"1\" viewBox=\"0 0 0 5\"/>" => "True|True|none",
		"<svg width=\"1cm\" height=\"10mm\"/>" => "True|True|38x38",
		"<svg width=\"1in\" height=\"1pt\"/>" => "True|True|96x1",
		"<svg width=\"1pc\" height=\"1px\"/>" => "True|True|16x1",
		_ => "?",
	};

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

	static PlainExif.Verdict Judge (byte[] bytes, int tag)
	{
		Assert.IsTrue (TiffReader.TryOpen (bytes, out var tiff, out var first));
		return PlainExif.Judge (tiff, IfdGraph.Walk (tiff, first), [tag]);
	}

	static ImageTag CommentTag ()
	{
		var tag = new ImageTag (MediaFormat.Png, ImageStores.Comment);
		tag.Comment = "New";
		return tag;
	}

	static byte[] GifWithTable ()
	{
		var header = new byte[19];
		"GIF89a"u8.CopyTo (header);
		header[6] = 1;
		header[8] = 1;
		header[10] = 0x80;
		for (var i = 0; i < 6; i++)
			header[13 + i] = (byte)(10 + i);
		return Concat (header, [0x3B]);
	}

	static byte[] GifCommentBytes (byte[] gif)
	{
		var at = gif.AsSpan ().IndexOf ((byte)0x21);
		Assert.IsTrue (at > 0);
		return gif[at..];
	}

	/// <summary>A null path is not a directory lookup. Every other call is a failure of the guard.</summary>
	sealed class RootDirectoryFileSystem : IFileSystem
	{
		public int Calls { get; private set; }

		public string? GetDirectoryName (string path)
		{
			Calls++;
			return "/";
		}

		public bool FileExists (string path) => Fail<bool> (path);

		public Stream OpenRead (string path) => Fail<Stream> (path);

		public Stream OpenReadWrite (string path) => Fail<Stream> (path);

		public Stream Create (string path) => Fail<Stream> (path);

		public byte[] ReadAllBytes (string path) => Fail<byte[]> (path);

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
			Fail<Task<byte[]>> (path);

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => Fail<bool> (path);

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			Fail<Task> (path);

		public void Delete (string path) => Fail<bool> (path);

		public void Move (string sourcePath, string destinationPath) => Fail<bool> (sourcePath);

		public string GetFileName (string path) => Fail<string> (path);

		public string CombinePath (string path1, string path2) => Fail<string> (path1);

		T Fail<T> (string path)
		{
			Calls++;
			throw new InvalidOperationException (path);
		}
	}
}
