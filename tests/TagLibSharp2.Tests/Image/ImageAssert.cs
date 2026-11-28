// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Assertions on what an opened image reports.</summary>
static class ImageAssert
{
	internal static void AssertSize (byte[] data, string name, int width, int height)
	{
		var opened = MediaFile.ReadFromData (data, name);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var image = (IMediaFile)opened.File!;
		Assert.AreEqual (width, image.ImageProperties!.Value.Width);
		Assert.AreEqual (height, image.ImageProperties.Value.Height);
	}

	internal static void AssertImage (byte[] data, string name, MediaFormat format, int width, int height, string kind)
	{
		AssertOne (MediaFile.ReadFromData (data, name), format, width, height, kind);
		AssertOne (MediaFile.Read (new MemoryStream (data), name), format, width, height, kind);
	}

	internal static void AssertOne (MediaFileResult result, MediaFormat format, int width, int height, string kind)
	{
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (format, result.Format);
		var file = (IMediaFile)result.File!;
		Assert.AreEqual (MediaTypes.Image, file.MediaTypes);
		Assert.IsNull (file.AudioProperties);
		Assert.IsNotNull (file.ImageProperties);
		Assert.AreEqual (width, file.ImageProperties.Value.Width);
		Assert.AreEqual (height, file.ImageProperties.Value.Height);
		Assert.AreEqual (kind, file.ImageProperties.Value.Format);
	}

	internal static IMediaFile ImageOf (byte[] data, string name) =>
		(IMediaFile)MediaFile.ReadFromData (data, name).File!;
}
