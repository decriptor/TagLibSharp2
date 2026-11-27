// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// Image path-save cases the shared cancellation rows do not cover: no source path, an unreadable open,
/// a splice that fails before the copy, and a cancel between chunks. Every still-image format is a row.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class ImagePathSaveCancellationTests
{
	const int PastOneChunk = 80 * 1024;

	[TestMethod]
	[DataRow ("jpeg")]
	[DataRow ("png")]
	[DataRow ("gif")]
	[DataRow ("tiff")]
	[DataRow ("dng")]
	[DataRow ("bmp")]
	[DataRow ("pcx")]
	[DataRow ("netpbm")]
	[DataRow ("svg")]
	public async Task PathSave_CancelledTokenWithoutASourcePath_DoesNotWrite (string format)
	{
		var file = ReadHeld (format);
		var fs = new ProbeFileSystem ();
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var save = await file.SaveToFileAsync ("/out." + format, fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Operation was cancelled", save.Error);
		Assert.IsFalse (fs.FileExists ("/out." + format));
	}

	[TestMethod]
	[DynamicData (nameof (UnreadableSources))]
	public async Task PathSave_UnreadableSource_BeatsAnAlreadyCancelledToken (string format, string fault, string expected)
	{
		var (file, fs) = Open (format);
		fs.ArmOpenTracking = true;
		if (fault.StartsWith ("open", StringComparison.Ordinal))
			fs.OpenReadFault = fault == "open-io" ? "io" : "denied";
		else
			fs.LengthFault = fault == "length-io" ? "io" : "denied";

		var save = await file.SaveToFileAsync ("/out." + format, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (expected, save.Error, format);
		Assert.IsFalse (fs.FileExists ("/out." + format));
		Assert.AreEqual (0, fs.CreateCalls);
		if (fault.StartsWith ("length", StringComparison.Ordinal)) {
			Assert.IsNotNull (fs.SaveOpen);
			Assert.IsTrue (fs.SaveOpen.WasDisposed);
		} else {
			Assert.IsNull (fs.SaveOpen);
		}
	}

	[TestMethod]
	[DataRow (
		"jpeg",
		"The JPEG marker list before the scan is truncated or malformed, so the comment cannot be spliced in.")]
	[DataRow (
		"png",
		"The PNG chunk list does not reach IEND, so the text cannot be spliced in.")]
	[DataRow (
		"gif",
		"The GIF block list is truncated or holds an unknown block, so the comment cannot be spliced in.")]
	public async Task PathSave_SpliceFailure_BeatsAnAlreadyCancelledToken (string format, string expected)
	{
		var (file, fs) = OpenTruncated (format);
		fs.ArmOpenTracking = true;
		if (format == "png")
			file.Tag!.Title = "PathSave";
		else
			file.Tag!.Comment = "PathSave";

		var save = await file.SaveToFileAsync ("/out." + format, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (expected, save.Error);
		Assert.IsFalse (fs.FileExists ("/out." + format));
		Assert.AreEqual (0, fs.CreateCalls);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
		CollectionAssert.AreEqual (fs.Original, fs.Store.ReadAllBytes ("/in." + format));
	}

	[TestMethod]
	[DataRow ("jpeg")]
	[DataRow ("png")]
	[DataRow ("gif")]
	[DataRow ("tiff")]
	[DataRow ("dng")]
	[DataRow ("bmp")]
	[DataRow ("pcx")]
	[DataRow ("netpbm")]
	[DataRow ("svg")]
	public async Task PathSave_CancelDuringCopy_DoesNotReplaceTheDestination (string format)
	{
		var data = LargerThanOneChunk (format);
		var (file, fs) = Open (format, data);
		fs.ArmOpenTracking = true;
		using var cts = new CancellationTokenSource ();
		fs.CancelOnWrite = cts;

		var save = await file.SaveToFileAsync ("/out." + format, fs, cts.Token);

		Assert.IsFalse (save.IsSuccess, format);
		Assert.AreEqual ("Operation was cancelled", save.Error, format);
		Assert.IsGreaterThanOrEqualTo (1, fs.CreateCalls);
		Assert.IsLessThanOrEqualTo (ChunkBoundedFileSystem.MaxChunk, fs.MaxWrite);
		Assert.IsFalse (fs.FileExists ("/out." + format));
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
		CollectionAssert.AreEqual (data, fs.Store.ReadAllBytes ("/in." + format));
	}

	[TestMethod]
	[DataRow ("jpeg")]
	[DataRow ("png")]
	[DataRow ("gif")]
	[DataRow ("tiff")]
	[DataRow ("dng")]
	[DataRow ("bmp")]
	[DataRow ("pcx")]
	[DataRow ("netpbm")]
	[DataRow ("svg")]
	public async Task PathSave_SuccessfulCopy_DisposesTheSource (string format)
	{
		var (file, fs) = Open (format);
		fs.ArmOpenTracking = true;

		var save = await file.SaveToFileAsync ("/out." + format, fs);

		Assert.IsTrue (save.IsSuccess, format + ": " + save.Error);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
		Assert.IsTrue (fs.FileExists ("/out." + format));
	}

	[TestMethod]
	[DataRow ("jpeg")]
	[DataRow ("png")]
	[DataRow ("gif")]
	[DataRow ("tiff")]
	[DataRow ("dng")]
	[DataRow ("bmp")]
	[DataRow ("pcx")]
	[DataRow ("netpbm")]
	[DataRow ("svg")]
	public async Task PathSave_ShortSource_DisposesTheSource (string format)
	{
		var (file, fs) = Open (format);
		fs.Add ("/in." + format, [1]);
		fs.ArmOpenTracking = true;

		var save = await file.SaveToFileAsync ("/out." + format, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("The source ended before the requested bytes were copied", save.Error, format);
		Assert.IsFalse (fs.FileExists ("/out." + format));
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
	}

	public static IEnumerable<object[]> UnreadableSources ()
	{
		(string Fault, string Expected)[] faults = [
			("open-io", "Failed to re-read source file: Failed to read file: disk fault"),
			("open-denied", "Failed to re-read source file: Access denied: Access denied"),
			("length-io", "Failed to re-read source file: Failed to read file: disk fault"),
			("length-denied", "Failed to re-read source file: Access denied: Access denied")
		];
		foreach (var format in new[] { "jpeg", "png", "gif", "tiff", "dng", "bmp", "pcx", "netpbm", "svg" }) {
			foreach (var (fault, expected) in faults)
				yield return new object[] { format, fault, expected };
		}
	}

	static (StillImageFile File, ProbeFileSystem Fs) Open (string format, byte[]? data = null)
	{
		data ??= Small (format);
		var fs = new ProbeFileSystem ();
		fs.Add ("/in." + format, data);
		var read = StillImageFile.ReadFromFile ("/in." + format, fs);
		Assert.IsTrue (read.IsSuccess, format + ": " + read.Error);
		return (read.File!, fs);
	}

	static StillImageFile ReadHeld (string format)
	{
		var read = StillImageFile.Read (Small (format));
		Assert.IsTrue (read.IsSuccess, format + ": " + read.Error);
		return read.File!;
	}

	static (StillImageFile File, ProbeFileSystem Fs) OpenTruncated (string format)
	{
		byte[] data = format switch {
			"png" => Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0])),
			"gif" => GifScreen (1, 1, flags: 0),
			_ => Concat (
				[0xFF, 0xD8],
				JpegSegment (0xC0, [0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00]),
				[0xFF, 0xFE, 0x00, 0x10])
		};
		return Open (format, data);
	}

	static byte[] LargerThanOneChunk (string format)
	{
		if (format == "svg")
			return SvgPastOneChunk ();

		var header = Small (format);
		var data = new byte[PastOneChunk];
		header.CopyTo (data, 0);
		return data;
	}

	static byte[] SvgPastOneChunk ()
	{
		var open = "<svg width=\"2\" height=\"3\">"u8;
		var close = "</svg>"u8;
		var data = new byte[PastOneChunk];
		open.CopyTo (data);
		Array.Fill (data, (byte)' ', open.Length, PastOneChunk - open.Length - close.Length);
		close.CopyTo (data.AsSpan (PastOneChunk - close.Length));
		return data;
	}

	static byte[] Small (string format) => format switch {
		"jpeg" => Jpeg (),
		"png" => Png (),
		"gif" => Gif (),
		"tiff" => TiffImage (2, 3),
		"dng" => TiffImage (2, 3, dng: true),
		"bmp" => Bmp (4, 2),
		"pcx" => Pcx (0, 0, 1, 1, planes: 1),
		"netpbm" => "P4\n1 1\n"u8.ToArray (),
		"svg" => "<svg width=\"2\" height=\"3\"></svg>"u8.ToArray (),
		_ => throw new InvalidOperationException (format)
	};

	static CancellationToken Cancelled ()
	{
		var cts = new CancellationTokenSource ();
		cts.Cancel ();
		return cts.Token;
	}
}
