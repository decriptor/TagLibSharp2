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

/// <summary>The GIF comment extension: reading, writing, and where a new one goes.</summary>
[TestClass]
[TestCategory ("Unit")]
public partial class GifCommentTests
{
	[TestMethod]
	public void Gif_RoundTripsComment_AndLeavesImageBytesThatLookLikeAComment ()
	{
		var trap = new byte[] { 0x21, 0xFE, 0x00, 0x99 };
		var data = GifWithComment ("hello", trap);
		var file = Open (data, "anim.gif");
		Assert.AreEqual ("hello", file.Tag!.Comment);
		file.Tag.Comment = "hello";
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());

		file.Tag.Title = "Nope";
		Assert.IsNull (file.Tag.Title);
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());

		var comment = new string ('A', 300);
		file.Tag.Comment = comment;
		var rendered = file.Render ().ToArray ();
		CollectionAssert.AreEqual (trap, GifImageSubBlock (rendered));
		Assert.AreEqual (comment, Open (rendered, "anim.gif").Tag!.Comment);
		Assert.IsNull (Open (rendered, "anim.gif").Tag!.Title);

		file.Tag.Comment = null;
		var cleared = file.Render ().ToArray ();
		Assert.IsNull (Open (cleared, "anim.gif").Tag!.Comment);
		CollectionAssert.AreEqual (trap, GifImageSubBlock (cleared));
	}

	[TestMethod]
	public void Gif87a_Comment_IsSavedAsGif89a ()
	{
		var data = new byte[14];
		System.Text.Encoding.ASCII.GetBytes ("GIF87a").CopyTo (data, 0);
		data[6] = 4;
		data[8] = 2;
		data[13] = 0x3B;
		var file = Open (data, "old.gif");
		CollectionAssert.AreEqual (data, file.Render ().ToArray ());

		file.Tag!.Comment = "note";
		var rendered = file.Render ().ToArray ();
		CollectionAssert.AreEqual (System.Text.Encoding.ASCII.GetBytes ("GIF89a"), rendered.Take (6).ToArray ());
		Assert.AreEqual (4, Open (rendered, "old.gif").ImageProperties!.Value.Width);
		Assert.AreEqual (2, Open (rendered, "old.gif").ImageProperties!.Value.Height);
		Assert.AreEqual ("note", Open (rendered, "old.gif").Tag!.Comment);
	}

	[TestMethod]
	public void Gif_NewComment_IsWrittenBeforeTheImage ()
	{
		var trap = new byte[] { 0x21, 0xFE, 0x00, 0x99 };
		var data = GifWithCommentAfterImage ("old", trap);
		var file = Open (data, "anim.gif");
		file.Tag!.Comment = "placed";
		var rendered = file.Render ().ToArray ();

		Assert.IsTrue (IndexOf (rendered, 0x21, 0xFE) < IndexOf (rendered, 0x2C, -1), "comment follows the image");
		Assert.AreEqual ("placed", Open (rendered, "anim.gif").Tag!.Comment);
		CollectionAssert.AreEqual (trap, GifImageSubBlock (rendered));
	}

	static byte[] GifWithCommentAfterImage (string comment, byte[] trap)
	{
		var imageFirst = GifWithComment (comment, trap);
		var commentAt = IndexOf (imageFirst, 0x21, 0xFE);
		var imageAt = IndexOf (imageFirst, 0x2C, -1);
		var header = imageFirst.AsSpan (0, commentAt).ToArray ();
		var commentBlock = imageFirst.AsSpan (commentAt, imageAt - commentAt).ToArray ();
		var rest = imageFirst.AsSpan (imageAt).ToArray ();
		return Concat (header, rest[..^1], commentBlock, [0x3B]);
	}
}
