// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>The JPEG COM segment: reading and writing the comment before the scan.</summary>
[TestClass]
[TestCategory ("Unit")]
public class JpegCommentTests
{
	[TestMethod]
	public void Jpeg_RoundTripsComment_AndLeavesTheScan ()
	{
		var scan = new byte[] { 0xFF, 0xFE, 0x00, 0x03, 0x99, 0x88, 0xFF, 0xD9, 0xAA, 0xBB };
		var data = Jpeg ("old", scan);
		var file = Open (data, "photo.jpg");
		Assert.AreEqual ("old", file.Tag!.Comment);
		Assert.AreEqual (8, file.ImageProperties!.Value.Width);
		file.Tag.Comment = "old";
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());

		file.Tag.Comment = "門";
		var rendered = file.Render ().ToArray ();
		CollectionAssert.AreEqual (scan, rendered.Skip (rendered.Length - scan.Length).ToArray ());
		Assert.AreEqual ("門", Open (rendered, "photo.jpg").Tag!.Comment);
		Assert.AreEqual (8, Open (rendered, "photo.jpg").ImageProperties!.Value.Width);

		var longComment = new string ('B', 65534);
		file.Tag.Comment = longComment;
		var split = file.Render ().ToArray ();
		CollectionAssert.AreEqual (scan, split.Skip (split.Length - scan.Length).ToArray ());
		Assert.AreEqual (longComment, Open (split, "photo.jpg").Tag!.Comment);
	}

	static byte[] Jpeg (string comment, byte[] scan)
	{
		var text = Encoding.UTF8.GetBytes (comment);
		var data = new byte[2 + 4 + text.Length + 2 + 11 + 4 + scan.Length];
		data[0] = 0xFF;
		data[1] = 0xD8;
		data[2] = 0xFF;
		data[3] = 0xFE;
		data[4] = (byte)((text.Length + 2) >> 8);
		data[5] = (byte)(text.Length + 2);
		text.CopyTo (data, 6);
		var i = 6 + text.Length;
		data[i++] = 0xFF;
		data[i++] = 0xC0;
		data[i++] = 0x00;
		data[i++] = 0x0B;
		data[i++] = 0x08;
		data[i++] = 0x00;
		data[i++] = 0x01;
		data[i++] = 0x00;
		data[i++] = 0x08;
		data[i++] = 0x01;
		data[i++] = 0x01;
		data[i++] = 0x11;
		data[i++] = 0x00;
		data[i++] = 0xFF;
		data[i++] = 0xDA;
		data[i++] = 0x00;
		data[i++] = 0x02;
		scan.CopyTo (data, i);
		return data;
	}
}
