// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Opening and saving a still image through every entry point: data, path, stream, sync and async, cancellation, and results.</summary>
[TestClass]
[TestCategory ("Unit")]
public class StillImageFileEntryTests
{
	[TestMethod]
	public void Save_WithoutASourcePath_Fails ()
	{
		var file = Open (TitledPng ("Door"), "photo.png");
		Assert.IsNull (file.VideoProperties);
		var sync = file.SaveToFile ();
		var async = file.SaveToFileAsync ().GetAwaiter ().GetResult ();
		Assert.IsFalse (sync.IsSuccess, sync.Error);
		Assert.IsFalse (async.IsSuccess, async.Error);
		file.Dispose ();
	}

	[TestMethod]
	public void SaveToFileAsync_WritesTheRenderedBytes ()
	{
		var data = TitledPng ("Door");
		var fs = new MockFileSystem ();
		fs.AddFile ("/photo.png", data);
		var opened = MediaFile.Read ("/photo.png", fs);
		var file = (StillImageFile)opened.File!;
		file.Tag!.Comment = "async";
		var wrote = file.SaveToFileAsync ().GetAwaiter ().GetResult ();
		Assert.IsTrue (wrote.IsSuccess, wrote.Error);
		Assert.AreEqual ("async", MediaFile.Read ("/photo.png", fs).Tag!.Comment);
		Assert.AreEqual ("Door", MediaFile.Read ("/photo.png", fs).Tag!.Title);
	}

	[TestMethod]
	public void ReadAsync_OpensAnImageAndHonoursCancellationDuringTheBodyRead ()
	{
		var png = MinimalPng (PngChunk ("IDAT", [0x01]));
		using (var stream = new MemoryStream (png, writable: false)) {
			var opened = MediaFile.ReadAsync (stream, "a.png").GetAwaiter ().GetResult ();
			Assert.IsTrue (opened.IsSuccess, opened.Error);
			var image = (IMediaFile)opened.File!;
			Assert.AreEqual (1, image.ImageProperties!.Value.Width);
			Assert.AreEqual (1, image.ImageProperties.Value.Height);
		}

		using var cancel = new CancelAfterPeek (png);
		Assert.ThrowsExactly<OperationCanceledException> (() =>
			MediaFile.ReadAsync (cancel, "a.png").GetAwaiter ().GetResult ());
	}

	sealed class CancelAfterPeek : MemoryStream
	{
		int _asyncReads;

		public CancelAfterPeek (byte[] data)
			: base (data, writable: false)
		{
		}

		public override ValueTask<int> ReadAsync (Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (++_asyncReads > 1)
				throw new OperationCanceledException (cancellationToken);
			return base.ReadAsync (buffer, cancellationToken);
		}
	}

	static byte[] LargePng () => Png (PngChunk ("IDAT", new byte[8 * Mib]));

	[TestMethod]
	public void ReadFromData_KeepsTheCallersArray ()
	{
		var png = LargePng ();
		_ = MediaFile.ReadFromData (png, "a.png");
		Bounds.AssertAllocatesAtMost (64 * 1024, () => MediaFile.ReadFromData (png, "a.png"));
	}

	[TestMethod]
	public void ReadFromAPath_CopiesTheFileOnce ()
	{
		var png = LargePng ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.png", png);
		_ = MediaFile.Read ("/a.png", fs);
		_ = StillImageFile.ReadFromFile ("/a.png", fs);
		var limit = png.Length + (256 * 1024);

		Bounds.AssertAllocatesAtMost (limit, () => Assert.IsTrue (MediaFile.Read ("/a.png", fs).IsSuccess), "MediaFile.Read");
		Bounds.AssertAllocatesAtMost (limit, () => Assert.IsTrue (StillImageFile.ReadFromFile ("/a.png", fs).IsSuccess), "StillImageFile.ReadFromFile");
		using var stream = new MemoryStream (png, writable: false);
		Bounds.AssertAllocatesAtMost (limit, () => Assert.IsTrue (MediaFile.Read (stream, "a.png").IsSuccess), "MediaFile.Read (stream)");
	}

	[TestMethod]
	public void Render_ReturnsTheOriginalBytes ()
	{
		var data = PngIhdrWithoutCrc (width: 2, height: 2, depth: 8, color: 0);
		var opened = MediaFile.ReadFromData (data, "a.png");
		var file = (StillImageFile)opened.File!;
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());
	}

	[TestMethod]
	public void SaveToFile_WritesTheRenderedBytes ()
	{
		var data = MinimalPng (PngChunk ("IDAT", [0x07]));
		var fs = new MockFileSystem ();
		fs.AddFile ("/photo.png", data);
		var opened = MediaFile.Read ("/photo.png", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = (StillImageFile)opened.File!;
		file.Tag!.Title = "Saved";
		var wrote = file.SaveToFile ();
		Assert.IsTrue (wrote.IsSuccess, wrote.Error);
		Assert.AreEqual ("Saved", MediaFile.Read ("/photo.png", fs).Tag!.Title);
		CollectionAssert.AreEqual (new byte[] { 0x07 }, PngPayload (fs.ReadAllBytes ("/photo.png"), "IDAT"));
	}

	public static IEnumerable<object[]> Images ()
	{
		yield return ["/a.png", Png (Text ("Title", "T")), MediaFormat.Png, 2, 3];
		yield return ["/a.gif", Gif (GifComment ("C")), MediaFormat.Gif, 1, 1];
		yield return ["/a.jpg", Jpeg (JpegSegment (0xFE, "C"u8.ToArray ())), MediaFormat.Jpeg, 2, 3];
		yield return ["/a.tif", Tiff ((256, 4), (257, 5)), MediaFormat.Tiff, 4, 5];
		yield return ["/a.dng", Tiff ((256, 4), (257, 5)), MediaFormat.Dng, 4, 5];
		yield return ["/a.svg", Encoding.UTF8.GetBytes ("<svg width='6' height='7'/>"), MediaFormat.Svg, 6, 7];
		yield return ["/a.pgm", Encoding.ASCII.GetBytes ("P5 8 9 255\n"), MediaFormat.Netpbm, 8, 9];
	}

	[TestMethod]
	[DynamicData (nameof (Images))]
	public void EveryEntryPoint_OpensTheSameImage (string path, byte[] data, MediaFormat format, int width, int height)
	{
		var fs = new MockFileSystem ();
		fs.AddFile (path, data);
		var opened = new[] {
			StillImageFile.ReadFromFile (path, fs).File,
			StillImageFile.ReadFromFileAsync (path, fs).GetAwaiter ().GetResult ().File,
			(StillImageFile?)MediaFile.Read (path, fs).File,
		};
		foreach (var file in opened) {
			Assert.IsNotNull (file, path);
			Assert.AreEqual (format, file.Format, path);
			Assert.AreEqual (width, file.ImageProperties!.Value.Width, path);
			Assert.AreEqual (height, file.ImageProperties.Value.Height, path);
			Assert.AreEqual (path, file.SourcePath);
		}

		var fromBytes = StillImageFile.Read (data);
		Assert.IsTrue (fromBytes.IsSuccess, fromBytes.Error);
		Assert.AreEqual (format == MediaFormat.Dng ? MediaFormat.Tiff : format, fromBytes.File!.Format, "bytes alone cannot name a DNG without its version tag");
		Assert.IsNull (fromBytes.File.SourcePath);
		Assert.IsTrue (StillImageFile.TryRead (data, out var tried));
		Assert.AreEqual (width, tried!.ImageProperties!.Value.Width);
		Assert.IsTrue (StillImageFile.IsValidFormat (data));
	}

	[TestMethod]
	public void ReadFromFile_ThenSave_WritesBackToTheSource ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.png", Png (Text ("Title", "Old")));
		var file = StillImageFile.ReadFromFile ("/a.png", fs).File!;
		file.Tag!.Title = "New";
		Assert.IsTrue (file.SaveToFile ().IsSuccess);
		Assert.AreEqual ("New", StillImageFile.ReadFromFile ("/a.png", fs).File!.Tag!.Title);

		var again = StillImageFile.ReadFromFileAsync ("/a.png", fs).GetAwaiter ().GetResult ().File!;
		again.Tag!.Title = "Async";
		Assert.IsTrue (again.SaveToFileAsync ().GetAwaiter ().GetResult ().IsSuccess);
		Assert.AreEqual ("Async", StillImageFile.ReadFromFile ("/a.png", fs).File!.Tag!.Title);
	}

	[TestMethod]
	public void Failures_ReturnAResultWithTheirOwnReason ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.mp3", [0xFF, 0xFB, 0x90, 0x00]);
		fs.AddFile ("/locked.png", Png ());
		fs.MarkInaccessible ("/locked.png");
		fs.AddFile ("/bad.png", Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0])));

		StringAssert.Contains (StillImageFile.ReadFromFile ("/missing.png", fs).Error, "not found");
		StringAssert.Contains (StillImageFile.ReadFromFileAsync ("/missing.png", fs).GetAwaiter ().GetResult ().Error, "not found");
		StringAssert.Contains (StillImageFile.ReadFromFile ("/a.mp3", fs).Error, "Not a still image");
		StringAssert.Contains (StillImageFile.ReadFromFile ("/locked.png", fs).Error, "Access denied");
		StringAssert.Contains (StillImageFile.ReadFromFileAsync ("/locked.png", fs).GetAwaiter ().GetResult ().Error, "Access denied");
		StringAssert.Contains (StillImageFile.ReadFromFile ("/bad.png", fs).Error, "Not a Png image");
		Assert.IsFalse (StillImageFile.TryRead ([], out var none));
		Assert.IsNull (none);
		Assert.IsFalse (StillImageFile.IsValidFormat ("ID3"u8));
		Assert.ThrowsExactly<ArgumentNullException> (() => StillImageFile.ReadFromFile (null!, fs));
	}

	[TestMethod]
	public void ReadFromFileAsync_IsCancelled ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.png", Png ());
		using var cancel = new CancellationTokenSource ();
		cancel.Cancel ();
		var result = StillImageFile.ReadFromFileAsync ("/a.png", fs, cancel.Token).GetAwaiter ().GetResult ();
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public void ReadResult_HasValueEquality ()
	{
		var file = StillImageFile.Read (Png ()).File!;
		var a = StillImageFileReadResult.Success (file);
		var b = StillImageFileReadResult.Success (file);
		var failed = StillImageFileReadResult.Failure ("x");
		Assert.IsTrue (a == b);
		Assert.IsFalse (a != b);
		Assert.IsTrue (a.Equals ((object)b));
		Assert.AreEqual (a.GetHashCode (), b.GetHashCode ());
		Assert.IsTrue (a != failed);
		Assert.IsFalse (failed.IsSuccess);
		Assert.IsFalse (a.Equals ("x"));
	}
}
