// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4SaveLayoutTests
{
	static readonly byte[] Marker = [0x11, 0x22, 0x33, 0x44];

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
		// The only mdat follows the size-7 box. A copy of the 8-byte "junk" header is not enough.
		var stcoBody = StcoBody (0);
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var moov = Box ("moov", Box ("stco", stcoBody));
		var bad = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian (bad, 7);
		"junk"u8.CopyTo (bad.AsSpan (4));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = ftyp.Length + moov.Length + bad.Length + 8;
		moov = Box ("moov", Box ("stco", StcoBody ((uint)payloadOffset)));
		var file = Concat (ftyp, moov, bad, mdat);

		var rendered = RenderWithTitle (file);
		var mdatType = IndexOf (rendered.Span, "mdat"u8);
		var junk = IndexOf (rendered.Span, "junk"u8);
		Assert.IsTrue (junk >= 0);
		Assert.IsTrue (mdatType > junk, "mdat must follow the size-7 box");
		Assert.IsTrue (mdatType + 4 + Marker.Length <= rendered.Length);
		Assert.IsTrue (rendered.Span.Slice (mdatType + 4, Marker.Length).SequenceEqual (Marker));
		Assert.AreEqual ((uint)(mdatType + 4), ReadStco (rendered.Span));
		Assert.AreNotEqual ((uint)payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_LargeSizeMvhd_MovesNestedStcoByMoovDelta ()
	{
		var stco = Box ("stco", StcoBody (0));
		var trak = Wrap (stco, "stbl", "minf", "mdia", "trak");
		var mvhd = LargeBox ("mvhd", []);
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var moov = Box ("moov", Concat (mvhd, trak));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = ftyp.Length + moov.Length + 8;
		stco = Box ("stco", StcoBody ((uint)payloadOffset));
		trak = Wrap (stco, "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", Concat (mvhd, trak));
		var file = Concat (ftyp, moov, mdat);

		var rendered = RenderWithTitle (file);
		var oldMoov = BoxLength (file, "moov"u8);
		var newMoov = BoxLength (rendered.Span, "moov"u8);
		var delta = newMoov - oldMoov;
		var markerAt = MarkerAt (rendered.Span);
		var entry = ReadStco (rendered.Span);

		Assert.IsTrue (delta > 0);
		Assert.AreEqual ((uint)payloadOffset + (uint)delta, entry);
		Assert.AreEqual ((uint)markerAt, entry);
	}

	[TestMethod]
	public void Render_UnparsedChildInMoov_KeepsTrakAndChunkOffset ()
	{
		// Size 7 is shorter than a header, so parsing stops. trak starts on the next byte.
		var trak = Wrap (Box ("stco", StcoBody (0)), "stbl", "minf", "mdia", "trak");
		var mvhd = Box ("mvhd", new byte[4]);
		var bad = new byte[7];
		BinaryPrimitives.WriteUInt32BigEndian (bad, 7);
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var moov = Box ("moov", Concat (mvhd, bad, trak));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = ftyp.Length + moov.Length + 8;
		trak = Wrap (Box ("stco", StcoBody ((uint)payloadOffset)), "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", Concat (mvhd, bad, trak));
		var file = Concat (ftyp, moov, mdat);

		var rendered = RenderWithTitle (file);
		Assert.IsTrue (Count (rendered.Span, "trak"u8) > 0);
		Assert.IsTrue (Count (rendered.Span, "stco"u8) > 0);
		var markerAt = MarkerAt (rendered.Span);
		Assert.AreEqual ((uint)markerAt, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_NestedStcoAndCo64_MovesOnlyOffsetsAtOrAfterMoovEnd ()
	{
		const uint early = 8;
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var trak1 = Wrap (Box ("stco", StcoBody (early, 0)), "stbl", "minf", "mdia", "trak");
		var trak2 = Wrap (Box ("co64", Co64Body (0)), "stbl", "minf", "mdia", "trak");
		var moov = Box ("moov", Concat (trak1, trak2));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		var oldMoovEnd = ftyp.Length + moov.Length;
		Assert.IsTrue (early < oldMoovEnd);
		Assert.IsTrue (payloadOffset >= oldMoovEnd);

		trak1 = Wrap (Box ("stco", StcoBody (early, payloadOffset)), "stbl", "minf", "mdia", "trak");
		trak2 = Wrap (Box ("co64", Co64Body (payloadOffset)), "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", Concat (trak1, trak2));
		var file = Concat (ftyp, moov, mdat);

		var rendered = RenderWithTitle (file);
		var markerAt = MarkerAt (rendered.Span);
		Assert.AreEqual (early, ReadStco (rendered.Span));
		Assert.AreEqual ((uint)markerAt, ReadStco (rendered.Span, 1));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span, 1));
		Assert.AreEqual ((ulong)markerAt, ReadCo64 (rendered.Span));
		Assert.AreNotEqual ((ulong)payloadOffset, ReadCo64 (rendered.Span));
	}

	[TestMethod]
	public void Render_MoovAfterMdat_DoesNotMoveChunkOffsets ()
	{
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var mdat = Box ("mdat", Marker);
		var payloadOffset = (uint)(ftyp.Length + 8);
		var trak1 = Wrap (Box ("stco", StcoBody (payloadOffset)), "stbl", "minf", "mdia", "trak");
		var trak2 = Wrap (Box ("co64", Co64Body (payloadOffset)), "stbl", "minf", "mdia", "trak");
		var moov = Box ("moov", Concat (trak1, trak2));
		var file = Concat (ftyp, mdat, moov);
		var oldMoovEnd = ftyp.Length + mdat.Length + moov.Length;
		Assert.IsTrue (payloadOffset < oldMoovEnd);

		var rendered = RenderWithTitle (file);
		Assert.IsTrue (rendered.Length > file.Length);
		var markerAt = MarkerAt (rendered.Span);
		Assert.AreEqual ((int)payloadOffset, markerAt);
		Assert.AreEqual (payloadOffset, ReadStco (rendered.Span));
		Assert.AreEqual ((ulong)payloadOffset, ReadCo64 (rendered.Span));
	}

	static BinaryData RenderWithTitle (byte[] file)
	{
		var read = Mp4File.Read (file);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.Title = "Hello";
		var rendered = read.File.Render (file);
		Assert.IsFalse (rendered.IsEmpty);
		var again = Mp4File.Read (rendered.Span);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Hello", again.File!.Title);
		return rendered;
	}

	static byte[] BuildFastStart (out int payloadOffset)
	{
		var stcoBody = new byte[12];
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var stco = Box ("stco", stcoBody);
		var moov = Box ("moov", stco);
		var mdat = Box ("mdat", Marker);
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

	static byte[] StcoBody (params uint[] entries)
	{
		var body = new byte[8 + (entries.Length * 4)];
		BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (4), (uint)entries.Length);
		for (var i = 0; i < entries.Length; i++)
			BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (8 + (i * 4)), entries[i]);
		return body;
	}

	static byte[] Co64Body (params ulong[] entries)
	{
		var body = new byte[8 + (entries.Length * 8)];
		BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (4), (uint)entries.Length);
		for (var i = 0; i < entries.Length; i++)
			BinaryPrimitives.WriteUInt64BigEndian (body.AsSpan (8 + (i * 8)), entries[i]);
		return body;
	}

	static byte[] LargeBox (string type, byte[] body)
	{
		var box = new byte[16 + body.Length];
		BinaryPrimitives.WriteUInt32BigEndian (box, 1);
		Encoding.ASCII.GetBytes (type).CopyTo (box.AsSpan (4));
		BinaryPrimitives.WriteUInt64BigEndian (box.AsSpan (8), (ulong)box.Length);
		body.CopyTo (box.AsSpan (16));
		return box;
	}

	static byte[] Wrap (byte[] inner, params string[] parentsInnerFirst)
	{
		var current = inner;
		foreach (var type in parentsInnerFirst)
			current = Box (type, current);
		return current;
	}

	static byte[] Concat (params byte[][] parts)
	{
		var length = 0;
		foreach (var part in parts)
			length += part.Length;
		var result = new byte[length];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result.AsSpan (offset));
			offset += part.Length;
		}
		return result;
	}

	static byte[] Box (string type, byte[] body)
	{
		var box = new byte[8 + body.Length];
		BinaryPrimitives.WriteUInt32BigEndian (box, (uint)box.Length);
		Encoding.ASCII.GetBytes (type).CopyTo (box, 4);
		body.CopyTo (box.AsSpan (8));
		return box;
	}

	static int MarkerAt (ReadOnlySpan<byte> data)
	{
		var at = IndexOf (data, Marker);
		Assert.IsTrue (at >= 0, "media marker missing");
		return at;
	}

	static int BoxLength (ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
	{
		var typeAt = IndexOf (data, type);
		Assert.IsTrue (typeAt >= 4);
		return (int)BinaryPrimitives.ReadUInt32BigEndian (data.Slice (typeAt - 4, 4));
	}

	static uint ReadStco (ReadOnlySpan<byte> data, int entry = 0)
	{
		var stco = IndexOf (data, "stco"u8);
		Assert.IsTrue (stco >= 0, "stco missing");
		var at = stco + 12 + (entry * 4);
		Assert.IsTrue (at + 4 <= data.Length);
		return BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at, 4));
	}

	static ulong ReadCo64 (ReadOnlySpan<byte> data, int entry = 0)
	{
		var co64 = IndexOf (data, "co64"u8);
		Assert.IsTrue (co64 >= 0, "co64 missing");
		var at = co64 + 12 + (entry * 8);
		Assert.IsTrue (at + 8 <= data.Length);
		return BinaryPrimitives.ReadUInt64BigEndian (data.Slice (at, 8));
	}

	static int Count (ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
	{
		var count = 0;
		var start = 0;
		while (start <= data.Length - pattern.Length) {
			var rel = IndexOf (data.Slice (start), pattern);
			if (rel < 0)
				break;
			count++;
			start += rel + pattern.Length;
		}
		return count;
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
