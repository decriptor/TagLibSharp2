// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Multi-picture JPEG files: the MPF index is read only inside its segment, and every image it lists stays where it points after a save.</summary>
[TestClass]
[TestCategory ("Unit")]
public class MultiPictureTests
{
	public static IEnumerable<object[]> MpfEdits ()
	{
		yield return ["comment edit after the MPF segment", new Action<Tag> (t => t.Comment = "a much longer comment than before")];
		yield return ["comment removed", new Action<Tag> (t => t.Comment = null)];
		yield return ["clear drops Exif before and APP13 after", new Action<Tag> (t => t.Clear ())];
	}

	static byte[] SecondImage () => Jpeg (
		JpegExif (ExifTiff ([(0x010F, "Canon"), (0x013B, "Jane Private")], [(0x8827, "GPS")])),
		JpegXmp (XmpPacket (Seq ("dc:creator", "Jane Private"))),
		JpegApp (0xE2, "urn:iso:std:iso:ts:21496:-1\0", "\0\0\0\0gain map"),
		JpegApp (0xE2, "ICC_PROFILE\0", "\x01\x01profile"),
		JpegSegment (0xFE, "second image comment"u8.ToArray ()));

	[TestMethod]
	[DynamicData (nameof (MpfEdits))]
	public void Jpeg_MultiPictureIndex_StillPointsAtEachImage (string edit, Action<Tag> apply)
	{
		var input = MpfJpeg (
			JpegExif (ExifTiff ([(0x010F, "Canon")], [])),
			Concat (JpegApp (0xED, "Photoshop 3.0\0", "8BIM"), JpegSegment (0xFE, "c"u8.ToArray ())),
			SecondImage (),
			out var second);
		Assert.IsTrue (input.AsSpan (MpfSecondImage (input)).SequenceEqual (second), "fixture");
		var file = Open (input, "a.jpg");
		apply (file.Tag!);

		var output = file.Render ().ToArray ();

		Assert.AreNotEqual (input.Length, output.Length, edit);
		var entries = MpfEntries (output);
		var at = entries[1].Start;
		Assert.AreEqual (at, entries[0].Size, $"{edit}: image 1's size is its new length");
		Assert.AreEqual (output.Length - at, entries[1].Size, $"{edit}: image 2's size");
		var image2 = output[at..];
		Assert.AreEqual (0xFF, image2[0]);
		Assert.AreEqual (0xD8, image2[1]);
		Assert.IsTrue (Open (image2, "b.jpg").ImageProperties is not null, $"{edit}: image 2 still parses");
		if (edit.StartsWith ("clear", StringComparison.Ordinal)) {
			// Rule 7 reaches every image of the file; image 2 keeps what its decoder needs.
			foreach (var gone in new[] { "Exif\0\0"u8.ToArray (), "http://ns.adobe.com/xap/1.0/"u8.ToArray (), "Jane Private"u8.ToArray (), "second image comment"u8.ToArray () })
				Assert.IsTrue (image2.AsSpan ().IndexOf (gone) < 0, $"image 2 still holds {Encoding.ASCII.GetString (gone)}");
			Assert.IsTrue (image2.AsSpan ().IndexOf ("urn:iso:std:iso:ts:21496:-1"u8) > 0 && image2.AsSpan ().IndexOf ("ICC_PROFILE"u8) > 0);
			Assert.IsTrue (image2.AsSpan ().EndsWith (second.AsSpan (second.Length - 12)), "the scan of image 2 is copied as it is");
		} else {
			CollectionAssert.AreEqual (second, image2, $"{edit}: an edit leaves image 2 as it is");
		}
	}

	[TestMethod]
	public void Jpeg_ClearOfAMultiPictureFile_WhoseIndexCannotBeFollowed_IsRefused ()
	{
		var input = MpfJpeg ([], JpegSegment (0xFE, "c"u8.ToArray ()), SecondImage (), out _);
		var entries = input.AsSpan ().IndexOf ("MPF\0"u8) + 4 + 26;
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (input.AsSpan (entries + 16 + 4), input.Length);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();
		var result = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "other images cannot be cleared");
	}

	[TestMethod]
	public void Jpeg_UnreadableMpfIndex_RefusesAnEditThatMovesTheSecondImage ()
	{
		var input = Concat ([0xFF, 0xD8], JpegApp (0xE2, "MPF\0", "MM\0*\0\0\0\x08"), JpegSegment (0xFE, "c"u8.ToArray ()),
			JpegSegment (0xC0, [0x08, 0x00, 0x03, 0x00, 0x02, 0x01, 0x01, 0x11, 0x00]), JpegSegment (0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]), [0xFF, 0xD9]);
		var file = Open (input, "a.jpg");
		file.Tag!.Comment = "longer comment";
		var result = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "MPF");
	}

	// CIPA DC-007 §5.2: the MP index lies inside its APP2 segment. An index that points out of it is not followed.
	[TestMethod]
	public void MpIndexOutsideItsSegment_IsUnreadable_ForClearAndEdits ()
	{
		var input = MpfJpegMany ([], JpegSegment (0xFE, "comment that the edit makes longer"u8.ToArray ()), Jpeg ());
		var header = input.AsSpan ().IndexOf ("MPF\0"u8) + 4;
		var segmentEnd = header - 8 + 2 + ((input[header - 6] << 8) | input[header - 5]);
		// Point the entry list at the COM segment that follows the APP2 segment.
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (input.AsSpan (header + 18), segmentEnd + 4 - header);
		var original = (byte[])input.Clone ();

		var clear = Open (input, "a.jpg");
		clear.Tag!.Clear ();
		var cleared = clear.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsFalse (cleared.IsSuccess);
		StringAssert.Contains (cleared.Error, "MPF index cannot be read");

		var edit = Open (input, "a.jpg");
		edit.Tag!.Comment = "a longer comment than the old one";
		var edited = edit.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsFalse (edited.IsSuccess);
		StringAssert.Contains (edited.Error, "MPF index cannot be read");
		CollectionAssert.AreEqual (original, edit.Render ().ToArray (), "no byte outside the segment is written");
	}

	public static IEnumerable<object[]> UnfollowableIndexes ()
	{
		var two = Jpeg (JpegSegment (0xFE, "two"u8.ToArray ()));
		var three = Jpeg (JpegSegment (0xFE, "three"u8.ToArray ()));
		var dup = MpfJpegMany ([], [], two, three);
		var (_, offset2) = GetMpEntry (dup, 1);
		SetMpEntry (dup, 2, three.Length, offset2);
		yield return ["two entries at the same image", dup];
		var overlap = MpfJpegMany ([], [], two, three);
		var (size2, offset) = GetMpEntry (overlap, 1);
		SetMpEntry (overlap, 1, size2 + 10, offset);
		yield return ["overlapping images", overlap];
		// An SOI inside a comment before the first image's scan.
		var into = MpfJpegMany ([], JpegSegment (0xFE, [0xFF, 0xD8, 0xFF, 0xFE, 0x00, 0x04, 0x00, 0x00]), two);
		var header = into.AsSpan ().IndexOf ("MPF\0"u8) + 4;
		var soi = into.AsSpan (header).IndexOf (new byte[] { 0xFF, 0xD8, 0xFF, 0xFE }) + header;
		SetMpEntry (into, 1, 8, soi - header);
		yield return ["an image inside the first image's header", into];
	}

	[TestMethod]
	[DynamicData (nameof (UnfollowableIndexes))]
	public void Clear_OfAnIndexThatCannotBeFollowed_IsRefusedWithTheMpfReason (string shape, byte[] input)
	{
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();
		var result = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsFalse (result.IsSuccess, shape);
		StringAssert.Contains (result.Error, "other images cannot be cleared", shape);
	}

	[TestMethod]
	public void SecondMpfSegment_IsNotTheIndex ()
	{
		var second = Jpeg (JpegSegment (0xFE, "second"u8.ToArray ()));
		var input = MpfJpegMany ([], Concat (JpegApp (0xE2, "MPF\0", "II*\0\u0008\0\0\0\0\0"), JpegSegment (0xFE, "c"u8.ToArray ())), second);
		foreach (var apply in new Action<Tag>[] { t => t.Comment = "a longer comment", t => t.Clear () }) {
			var file = Open (input, "a.jpg");
			apply (file.Tag!);
			var output = file.Render ().ToArray ();
			Assert.AreNotEqual (input.Length, output.Length);
			var at = MpfSecondImage (output);
			Assert.IsTrue (at < output.Length && output[at] == 0xFF && output[at + 1] == 0xD8, "the first MPF segment's index is the one rewritten");
			Assert.AreEqual (at, MpfEntries (output)[0].Size);
			Assert.AreEqual (output.Length - at, MpfEntries (output)[1].Size);
		}
	}
}
