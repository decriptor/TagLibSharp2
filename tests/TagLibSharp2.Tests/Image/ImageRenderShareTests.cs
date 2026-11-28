// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// An unedited render is the buffer the file already holds. Two renders share that one array. An edit allocates
/// its own.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ImageRenderShareTests
{
	public static IEnumerable<object[]> Formats { get; } = [
		["jpeg"], ["png"], ["gif"], ["tiff"], ["dng"], ["bmp"], ["pcx"], ["netpbm"], ["svg"],
	];

	[TestMethod]
	[DynamicData (nameof (Formats))]
	public void UneditedRender_SharesOneBuffer (string format)
	{
		var opened = StillImageFile.Read (Small (format));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = opened.File!;

		Assert.IsTrue (MemoryMarshal.TryGetArray (file.Render (), out ArraySegment<byte> first), format);
		Assert.IsTrue (MemoryMarshal.TryGetArray (file.Render (), out ArraySegment<byte> second), format);

		Assert.AreSame (first.Array, second.Array, format);
		Assert.AreEqual (0, first.Offset, format);
		Assert.AreEqual (Small (format).Length, first.Count, format);
	}

	[TestMethod]
	[DataRow ("jpeg")]
	[DataRow ("png")]
	[DataRow ("gif")]
	public void EditedRender_AllocatesItsOwnBuffer (string format)
	{
		var opened = StillImageFile.Read (Small (format));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = opened.File!;
		Assert.IsTrue (MemoryMarshal.TryGetArray (file.Render (), out ArraySegment<byte> original), format);

		file.Tag!.Comment = "edited";
		var edited = file.Render ();

		Assert.IsTrue (MemoryMarshal.TryGetArray (edited, out ArraySegment<byte> copy), format);
		Assert.AreNotSame (original.Array, copy.Array, format);
		Assert.IsFalse (edited.Span.SequenceEqual (original.AsSpan ()), format);
	}

	// A 256 KiB BMP. The second render of an unedited file must not allocate another copy of it.
	[TestMethod]
	public void UneditedRender_OfALargeFile_StaysUnderThePlan ()
	{
		var opened = StillImageFile.Read (Bmp (2, 3, length: 256 * 1024));
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = opened.File!;
		_ = file.Render ();

		// Measured: the plan for the whole file, and no copy of the 262,144 bytes.
		var allocated = Bounds.AssertAllocatesAtMost (216, () => _ = file.Render (), "second render of a 256 KiB BMP");
		Assert.AreEqual (216, allocated);
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
}
