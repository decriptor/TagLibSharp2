// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>A path save that was cancelled does not replace the destination.</summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class ImagePathSaveCancellationTests
{
	[TestMethod]
	public async Task JpegPathSave_CancelledToken_DoesNotReplaceTheDestination ()
	{
		var data = Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()));
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.jpg", data);
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var save = await read.File!.SaveToFileAsync ("/out.jpg", fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Failed to re-read source file: Operation was cancelled", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
	}
}
