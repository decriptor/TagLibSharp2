// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4SaveLayoutTests
{
	[TestMethod]
	public void Render_FastStart_MovesChunkOffsetWithMoov ()
	{
		var file = BuildFastStart (out var payloadOffset);
		var read = Mp4File.Read (file);
		Assert.IsTrue (read.IsSuccess, read.Error);

		read.File!.Title = "Hello";
		var rendered = read.File.Render (file);
		var renderedPayload = IndexOf (rendered.Span, "mdat"u8) + 4;
		var stco = IndexOf (rendered.Span, "stco"u8);
		var offset = BinaryPrimitives.ReadUInt32BigEndian (rendered.Span.Slice (stco + 12));

		Assert.AreEqual ((uint)renderedPayload, offset);
		Assert.AreNotEqual ((uint)payloadOffset, offset);
	}

	[TestMethod]
	public void Render_UnparsedBoxBeforeMdat_KeepsTheMedia ()
	{
		var file = BuildFastStart (out _);
		var withTail = new byte[file.Length + 8 + 8];
		file.CopyTo (withTail);
		BinaryPrimitives.WriteUInt32BigEndian (withTail.AsSpan (file.Length), 7);
		"junk"u8.CopyTo (withTail.AsSpan (file.Length + 4));
		BinaryPrimitives.WriteUInt32BigEndian (withTail.AsSpan (file.Length + 8), 8);
		"mdat"u8.CopyTo (withTail.AsSpan (file.Length + 12));

		var read = Mp4File.Read (withTail);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.Title = "Hello";
		var rendered = read.File.Render (withTail);

		Assert.IsTrue (IndexOf (rendered.Span, "junk"u8) >= 0);
	}

	static byte[] BuildFastStart (out int payloadOffset)
	{
		var stcoBody = new byte[12];
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var stco = Box ("stco", stcoBody);
		var moov = Box ("moov", stco);
		var marker = new byte[] { 0x11, 0x22, 0x33, 0x44 };
		var mdat = Box ("mdat", marker);
		payloadOffset = ftyp.Length + moov.Length + 8;
		BinaryPrimitives.WriteUInt32BigEndian (stcoBody.AsSpan (4), 1);
		BinaryPrimitives.WriteUInt32BigEndian (stcoBody.AsSpan (8), (uint)payloadOffset);
		stco = Box ("stco", stcoBody);
		moov = Box ("moov", stco);

		var file = new byte[ftyp.Length + moov.Length + mdat.Length];
		ftyp.CopyTo (file);
		moov.CopyTo (file.AsSpan (ftyp.Length));
		mdat.CopyTo (file.AsSpan (ftyp.Length + moov.Length));
		return file;
	}

	static byte[] Box (string type, byte[] body)
	{
		var box = new byte[8 + body.Length];
		BinaryPrimitives.WriteUInt32BigEndian (box, (uint)box.Length);
		Encoding.ASCII.GetBytes (type).CopyTo (box, 4);
		body.CopyTo (box.AsSpan (8));
		return box;
	}

	static int IndexOf (ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
	{
		for (var i = 0; i <= data.Length - pattern.Length; i++) {
			if (data.Slice (i, pattern.Length).SequenceEqual (pattern))
				return i;
		}
		return -1;
	}
}
