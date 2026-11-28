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

public partial class ImageSaveContractTests
{
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
}
