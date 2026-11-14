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

	[TestMethod]
	public void Render_UdtaAfterBadBoxInMoov_KeepsOneNewTitleAndChunkOffset ()
	{
		// ISO 14496-12: moov holds at most one udta. The size-7 box stops parsing, so the old udta is unread tail.
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var mvhd = Box ("mvhd", new byte[4]);
		var bad = BadBox ();
		var udta = Udta ("Old");
		var trak = Wrap (Box ("stco", StcoBody (0)), "stbl", "minf", "mdia", "trak");
		var moov = Box ("moov", Concat (mvhd, bad, udta, trak));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		trak = Wrap (Box ("stco", StcoBody (payloadOffset)), "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", Concat (mvhd, bad, udta, trak));
		var file = Concat (ftyp, moov, mdat);

		var read = Mp4File.Read (file);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNull (read.File!.Title);
		read.File.Title = "New";
		var rendered = read.File.Render (file);
		var again = Mp4File.Read (rendered.Span);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("New", again.File!.Title);
		Assert.AreEqual (1, Count (rendered.Span, "udta"u8), "moov must contain one udta");
		Assert.IsTrue (IndexOf (rendered.Span, "Old"u8) < 0, "old title survived");
		var mvhdAt = IndexOf (rendered.Span, "mvhd"u8);
		var udtaAt = IndexOf (rendered.Span, "udta"u8);
		Assert.IsTrue (mvhdAt >= 0 && udtaAt > mvhdAt, "udta must follow mvhd");
		Assert.AreEqual ((uint)MarkerAt (rendered.Span), ReadStco (rendered.Span));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_BadChildInsideTrak_KeepsFollowingQqqqBox ()
	{
		// trak = [tkhd, size-7 box, QQQQ]. The size-7 box stops child parsing; QQQQ is that trak's unread tail.
		var qqqq = Box ("QQQQ", [0xAB, 0xCD, 0xEF, 0x01]);
		var trak = Box ("trak", Concat (Box ("tkhd", new byte[4]), BadBox (), qqqq));
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var moov = Box ("moov", Concat (Box ("mvhd", new byte[4]), trak));
		var file = Concat (ftyp, moov, Box ("mdat", Marker));

		var rendered = RenderWithTitle (file);
		var at = IndexOf (rendered.Span, "QQQQ"u8);
		Assert.IsTrue (at >= 0, "QQQQ lost");
		Assert.IsTrue (rendered.Span.Slice (at + 4, 4).SequenceEqual (new byte[] { 0xAB, 0xCD, 0xEF, 0x01 }));
	}

	[TestMethod]
	public void Render_MediaBoxNotNamedMdat_MovesChunkOffset ()
	{
		// The bytes after moov are in 'free', not 'mdat'. They still move when moov grows.
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var trak = Wrap (Box ("stco", StcoBody (0)), "stbl", "minf", "mdia", "trak");
		var moov = Box ("moov", trak);
		var media = Box ("free", Marker);
		var payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		trak = Wrap (Box ("stco", StcoBody (payloadOffset)), "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", trak);
		var file = Concat (ftyp, moov, media);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_ExternalDataReference_DoesNotMoveChunkOffsets ()
	{
		// A dref entry with the self-contained flag clear addresses another file. ISO 14496-12 §8.7.2 / §8.7.5.
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var external = TrakWithDref (0, selfContained: false, useCo64: true);
		var contained = TrakWithDref (0, selfContained: true, useCo64: false);
		var absent = Wrap (Box ("stco", StcoBody (0)), "stbl", "minf", "mdia", "trak");
		var moov = Box ("moov", Concat (external, contained, absent));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		Assert.IsTrue (payloadOffset >= ftyp.Length + moov.Length);
		external = TrakWithDref (payloadOffset, selfContained: false, useCo64: true);
		contained = TrakWithDref (payloadOffset, selfContained: true, useCo64: false);
		absent = Wrap (Box ("stco", StcoBody (payloadOffset)), "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", Concat (external, contained, absent));
		var file = Concat (ftyp, moov, mdat);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreNotEqual (payloadOffset, markerAt);
		Assert.AreEqual ((ulong)payloadOffset, ReadCo64 (rendered.Span), "external co64 must stay");
		Assert.AreEqual (markerAt, ReadStcoOccurrence (rendered.Span, 0), "self-contained stco must move");
		Assert.AreEqual (markerAt, ReadStcoOccurrence (rendered.Span, 1), "trak with no dref must move");
	}

	[TestMethod]
	public void Render_ShortDrefEntries_DoNotReadFlagsFromTheNextEntry ()
	{
		// Each entry is the 8-byte header only. A flags read past that header consumes the next entry's size,
		// which is 0x00000008, so the self-contained bit looks clear. ISO 14496-12 §8.7.2: no flags means unknown.
		var entry1 = Box ("url ", []);
		var entry2 = Box ("urn ", []);
		Assert.AreEqual (8, entry1.Length);
		Assert.AreEqual (8, entry2.Length);
		var dref = DrefBox (entry1, entry2);
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, null), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span), "an entry without flags must not mark the trak external");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_LargeSizeDrefEntryWithoutFlags_DoesNotReadTheNextEntry ()
	{
		// largesize 16 is the 16-byte header and does not contain flags. The next entry starts at that boundary.
		var entry1 = new byte[16];
		BinaryPrimitives.WriteUInt32BigEndian (entry1, 1);
		Encoding.ASCII.GetBytes ("url ").CopyTo (entry1, 4);
		BinaryPrimitives.WriteUInt64BigEndian (entry1.AsSpan (8), 16);
		var entry2 = Box ("urn ", []);
		var dref = DrefBox (entry1, entry2);
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, null), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_MixedDataReference_MovesChunkOffset ()
	{
		// One chunk and no stsc. stsd references dref entry 1 (self-contained) and entry 2 (external).
		// Without stsc the trak is still one decision, so this offset moves.
		var dref = DrefBox (DataEntryUrl (true), DataEntryUrl (false));
		var stsd = StsdBox (SampleEntry (1), SampleEntry (2));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, stsd), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span), "self-contained sample offsets must move");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_MixedDataReference_TwoChunks_MovesOnlySelfContainedOffset ()
	{
		// stsc assigns chunk 1 to the self-contained sample entry and chunk 2 to the external one.
		// ISO 14496-12 §8.7.4 / §8.7.5: only the in-file chunk moves by the moov delta.
		var dref = DrefBox (DataEntryUrl (true), DataEntryUrl (false));
		var stsd = StsdBox (SampleEntry (1), SampleEntry (2));
		var file = FileWithChunkTrak (
			offset => TrakWithTwoChunks (offset, offset + 4, dref, stsd, StscBox ()),
			out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span, 0), "self-contained chunk moves");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span, 0));
		Assert.AreEqual (payloadOffset + 4, ReadStco (rendered.Span, 1), "external chunk stays");
	}

	[TestMethod]
	public void Render_MixedDataReference_TwoCo64Chunks_MovesOnlySelfContainedOffset ()
	{
		// Same stsc split as the stco case. co64 entries follow the same per-chunk rule.
		var dref = DrefBox (DataEntryUrl (true), DataEntryUrl (false));
		var stsd = StsdBox (SampleEntry (1), SampleEntry (2));
		var file = FileWithChunkTrak (
			offset => TrakWithTwoChunks (offset, offset + 4, dref, stsd, StscBox (), useCo64: true),
			out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual ((ulong)markerAt, ReadCo64 (rendered.Span, 0), "self-contained co64 chunk moves");
		Assert.AreNotEqual ((ulong)payloadOffset, ReadCo64 (rendered.Span, 0));
		Assert.AreEqual ((ulong)payloadOffset + 4, ReadCo64 (rendered.Span, 1), "external co64 chunk stays");
	}

	[TestMethod]
	public void Render_MixedDataReference_StscRun_UsesGreatestFirstChunk ()
	{
		// Row at chunk 1 is external. Row at chunk 3 is self-contained.
		// Chunk 2 has no row; §8.7.4 uses the greatest first_chunk that is <= 2.
		// Chunk 4 is self-contained but points before moov, so it still does not move.
		var dref = DrefBox (DataEntryUrl (true), DataEntryUrl (false));
		var stsd = StsdBox (SampleEntry (1), SampleEntry (2));
		var stsc = StscRows ((1, 2), (3, 1));
		var file = FileWithChunkTrak (
			offset => TrakWithChunkOffsets (dref, stsd, stsc, false, 8, offset, offset + 4, 8),
			out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (8u, ReadStco (rendered.Span, 0), "external run stays");
		Assert.AreEqual (payloadOffset, ReadStco (rendered.Span, 1), "chunk 2 stays on the external run");
		Assert.AreEqual (markerAt + 4, ReadStco (rendered.Span, 2), "self-contained run moves");
		Assert.AreNotEqual (payloadOffset + 4, ReadStco (rendered.Span, 2));
		Assert.AreEqual (8u, ReadStco (rendered.Span, 3), "offset before moov stays");
	}

	[TestMethod]
	public void Render_ExternalDrefBeforeSelfContained_MovesChunkOffset ()
	{
		// dref order is [external, self-contained]. stsd selects entry 2.
		// The external entry being first must not freeze this trak. ISO 14496-12 §8.5.2.
		var dref = DrefBox (DataEntryUrl (false), DataEntryUrl (true));
		var stsd = StsdBox (SampleEntry (2));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, stsd), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span), "the self-contained index must move");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_OnlyExternalDataReferenceIndex_DoesNotMoveChunkOffset ()
	{
		// The same dref has a self-contained entry, but stsd uses only the external index.
		var dref = DrefBox (DataEntryUrl (true), DataEntryUrl (false));
		var stsd = StsdBox (SampleEntry (2));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, stsd), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		Assert.AreEqual (payloadOffset, ReadStco (rendered.Span), "external sample offsets must stay");
		Assert.AreNotEqual ((uint)MarkerAt (rendered.Span), ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_OutOfRangeDataReferenceIndex_MovesChunkOffset ()
	{
		// Index 2 does not exist. Treat it as self-contained so the offset fix still runs.
		var dref = DrefBox (DataEntryUrl (false));
		var stsd = StsdBox (SampleEntry (2));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, stsd), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_SampleEntryMissingDataReferenceIndex_MovesChunkOffset ()
	{
		// The sample entry is only its 8-byte header. Bytes at entry offset 14 are 0x0001 and must not be read.
		var dref = DrefBox (DataEntryUrl (false));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, StsdWithShortSampleEntry ()), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span), "a missing index is self-contained");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_EmptyStsdWithExternalDref_DoesNotMoveChunkOffset ()
	{
		// stsd is present with entry_count 0, so it yields no data_reference_index.
		// An external dref entry still means this trak is not self-contained. ISO 14496-12 §8.7.2.
		var dref = DrefBox (DataEntryUrl (false));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, StsdBox ()), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		Assert.AreEqual (payloadOffset, ReadStco (rendered.Span), "empty stsd plus an external dref must not move offsets");
		Assert.AreNotEqual ((uint)MarkerAt (rendered.Span), ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_TruncatedStsd_DoesNotReadNextBoxAsDataReferenceIndex ()
	{
		// The sample entry declares 64 bytes, but stsd ends after 16. Those last 8 bytes are the
		// next box header, and the uint16 at the data_reference_index position is 1. dref entry 1
		// is external, so reading the next box would leave the chunk offset unchanged.
		var dref = DrefBox (DataEntryUrl (false));
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, TruncatedStsd ()), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span), "a truncated stsd must not select the external dref");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_TruncatedDrefEntry_DoesNotReadNextBoxAsFlags ()
	{
		// The url entry declares 64 bytes, but dref ends after 16. The bytes at the flags
		// position are the next box (size 0), whose low bit is clear. That is not the self-contained flag.
		var entry = new byte[16];
		BinaryPrimitives.WriteUInt32BigEndian (entry, 64);
		Encoding.ASCII.GetBytes ("url ").CopyTo (entry, 4);
		BinaryPrimitives.WriteUInt32BigEndian (entry.AsSpan (8), 0);
		Encoding.ASCII.GetBytes ("nxtb").CopyTo (entry, 12);
		var dref = DrefBox (entry);
		var file = FileWithChunkTrak (offset => TrakWithDinfAndStbl (offset, dref, null), out var payloadOffset);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span), "a truncated dref entry must not mark the trak external");
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_IllegalLargesize_DoesNotHideLaterStco ()
	{
		// size==1 with largesize 12 is below the 16-byte extended header, but 12 fits in the parent.
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var blob = IllegalLargesizeThenStco (0);
		var moov = Box ("moov", Concat (Box ("mvhd", new byte[4]), blob));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		blob = IllegalLargesizeThenStco (payloadOffset);
		moov = Box ("moov", Concat (Box ("mvhd", new byte[4]), blob));
		var file = Concat (ftyp, moov, mdat);

		var rendered = RenderWithTitle (file);
		var markerAt = (uint)MarkerAt (rendered.Span);
		Assert.AreEqual (markerAt, ReadStco (rendered.Span));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
	}

	[TestMethod]
	public void Render_BadFirstMoovChild_PutsUdtaAfterMvhd ()
	{
		// The first child is not a box, so mvhd and the old udta are both unread. udta must not be emitted first.
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var trak = Wrap (Box ("stco", StcoBody (0)), "stbl", "minf", "mdia", "trak");
		var moov = Box ("moov", Concat (BadBox (), Box ("mvhd", new byte[4]), trak, Udta ("Old")));
		var mdat = Box ("mdat", Marker);
		var payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		trak = Wrap (Box ("stco", StcoBody (payloadOffset)), "stbl", "minf", "mdia", "trak");
		moov = Box ("moov", Concat (BadBox (), Box ("mvhd", new byte[4]), trak, Udta ("Old")));
		var file = Concat (ftyp, moov, mdat);

		var read = Mp4File.Read (file);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.Title = "New";
		var rendered = read.File.Render (file);
		Assert.IsFalse (rendered.IsEmpty);
		Assert.AreEqual (1, Count (rendered.Span, "udta"u8));
		Assert.IsTrue (IndexOf (rendered.Span, "Old"u8) < 0, "old title survived");
		var mvhdAt = IndexOf (rendered.Span, "mvhd"u8);
		var udtaAt = IndexOf (rendered.Span, "udta"u8);
		Assert.IsTrue (mvhdAt >= 0 && udtaAt > mvhdAt, "udta must follow mvhd");
		Assert.AreEqual ((uint)MarkerAt (rendered.Span), ReadStco (rendered.Span));
		Assert.AreNotEqual (payloadOffset, ReadStco (rendered.Span));
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

	static byte[] BadBox ()
	{
		var bad = new byte[7];
		BinaryPrimitives.WriteUInt32BigEndian (bad, 7);
		return bad;
	}

	static byte[] Udta (string title)
	{
		var text = Encoding.UTF8.GetBytes (title);
		var dataBody = new byte[8 + text.Length];
		dataBody[3] = 1;
		text.CopyTo (dataBody.AsSpan (8));
		var nam = BoxBytes ([0xA9, (byte)'n', (byte)'a', (byte)'m'], Box ("data", dataBody));
		var hdlr = new byte[25];
		"mdir"u8.CopyTo (hdlr.AsSpan (8));
		"appl"u8.CopyTo (hdlr.AsSpan (12));
		var meta = Box ("meta", Concat (new byte[4], Box ("hdlr", hdlr), Box ("ilst", nam)));
		// Padding makes this udta a different size from the rebuilt one, so moov (and stco) must move.
		return Box ("udta", Concat (meta, Box ("free", new byte[32])));
	}

	static byte[] BoxBytes (byte[] type, byte[] body)
	{
		var box = new byte[8 + body.Length];
		BinaryPrimitives.WriteUInt32BigEndian (box, (uint)box.Length);
		type.CopyTo (box, 4);
		body.CopyTo (box.AsSpan (8));
		return box;
	}

	static byte[] FullBox (string type, uint flags, byte[] data)
	{
		var body = new byte[4 + data.Length];
		body[1] = (byte)((flags >> 16) & 0xFF);
		body[2] = (byte)((flags >> 8) & 0xFF);
		body[3] = (byte)(flags & 0xFF);
		data.CopyTo (body.AsSpan (4));
		return Box (type, body);
	}

	static byte[] FileWithChunkTrak (Func<uint, byte[]> buildTrak, out uint payloadOffset)
	{
		var ftyp = Box ("ftyp", "M4A "u8.ToArray ());
		var trak = buildTrak (0);
		var moov = Box ("moov", trak);
		var mdat = Box ("mdat", Marker);
		payloadOffset = (uint)(ftyp.Length + moov.Length + 8);
		trak = buildTrak (payloadOffset);
		moov = Box ("moov", trak);
		return Concat (ftyp, moov, mdat);
	}

	static byte[] DrefBox (params byte[][] entries) => FullBox ("dref", 0, CountedEntries (entries));

	static byte[] StsdBox (params byte[][] entries) => FullBox ("stsd", 0, CountedEntries (entries));

	static byte[] DataEntryUrl (bool selfContained) =>
		selfContained
			? FullBox ("url ", 1, [])
			: FullBox ("url ", 0, "ext.bin\0"u8.ToArray ());

	static byte[] SampleEntry (ushort dataReferenceIndex)
	{
		var body = new byte[8];
		BinaryPrimitives.WriteUInt16BigEndian (body.AsSpan (6), dataReferenceIndex);
		return Box ("mp4a", body);
	}

	static byte[] StsdWithShortSampleEntry ()
	{
		var entry = Box ("mp4a", []);
		var bait = new byte[8];
		BinaryPrimitives.WriteUInt16BigEndian (bait.AsSpan (6), 1);
		var data = new byte[4 + entry.Length + bait.Length];
		BinaryPrimitives.WriteUInt32BigEndian (data, 1);
		entry.CopyTo (data.AsSpan (4));
		bait.CopyTo (data.AsSpan (4 + entry.Length));
		return FullBox ("stsd", 0, data);
	}

	static byte[] TruncatedStsd ()
	{
		// Declared sample-entry size is 64. Only 16 bytes are in this stsd.
		// Bytes 8..15 are the next box: size 8, type 'nx' + 0x0001 at the index position.
		var entry = new byte[16];
		BinaryPrimitives.WriteUInt32BigEndian (entry, 64);
		Encoding.ASCII.GetBytes ("mp4a").CopyTo (entry, 4);
		BinaryPrimitives.WriteUInt32BigEndian (entry.AsSpan (8), 8);
		entry[12] = (byte)'n';
		entry[13] = (byte)'x';
		entry[14] = 0x00;
		entry[15] = 0x01;
		var data = new byte[4 + entry.Length];
		BinaryPrimitives.WriteUInt32BigEndian (data, 1);
		entry.CopyTo (data.AsSpan (4));
		return FullBox ("stsd", 0, data);
	}

	static byte[] CountedEntries (byte[][] entries)
	{
		var length = 4;
		foreach (var entry in entries)
			length += entry.Length;
		var data = new byte[length];
		BinaryPrimitives.WriteUInt32BigEndian (data, (uint)entries.Length);
		var pos = 4;
		foreach (var entry in entries) {
			entry.CopyTo (data.AsSpan (pos));
			pos += entry.Length;
		}
		return data;
	}

	static byte[] TrakWithDinfAndStbl (uint chunkOffset, byte[] dref, byte[]? stsd, bool useCo64 = false)
	{
		var chunks = useCo64 ? Box ("co64", Co64Body (chunkOffset)) : Box ("stco", StcoBody (chunkOffset));
		var stbl = Box ("stbl", stsd is null ? chunks : Concat (stsd, chunks));
		var minf = Box ("minf", Concat (Box ("dinf", dref), stbl));
		return Box ("trak", Concat (Box ("tkhd", new byte[4]), Box ("mdia", minf)));
	}

	static byte[] TrakWithTwoChunks (uint firstOffset, uint secondOffset, byte[] dref, byte[] stsd, byte[] stsc, bool useCo64 = false) =>
		TrakWithChunkOffsets (dref, stsd, stsc, useCo64, firstOffset, secondOffset);

	static byte[] TrakWithChunkOffsets (byte[] dref, byte[] stsd, byte[] stsc, bool useCo64, params uint[] offsets)
	{
		byte[] chunks;
		if (useCo64) {
			var wide = new ulong[offsets.Length];
			for (var i = 0; i < offsets.Length; i++)
				wide[i] = offsets[i];
			chunks = Box ("co64", Co64Body (wide));
		} else {
			chunks = Box ("stco", StcoBody (offsets));
		}

		var stbl = Box ("stbl", Concat (stsd, stsc, chunks));
		var minf = Box ("minf", Concat (Box ("dinf", dref), stbl));
		return Box ("trak", Concat (Box ("tkhd", new byte[4]), Box ("mdia", minf)));
	}

	static byte[] StscBox () => StscRows ((1, 1), (2, 2));

	static byte[] StscRows (params (uint FirstChunk, uint SampleDescription)[] rows)
	{
		// ISO 14496-12 §8.7.4: first_chunk, samples_per_chunk, sample_description_index. 1-based.
		var body = new byte[4 + (rows.Length * 12)];
		BinaryPrimitives.WriteUInt32BigEndian (body, (uint)rows.Length);
		for (var i = 0; i < rows.Length; i++) {
			var at = 4 + (i * 12);
			BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (at), rows[i].FirstChunk);
			BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (at + 4), 1);
			BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (at + 8), rows[i].SampleDescription);
		}

		return FullBox ("stsc", 0, body);
	}

	static byte[] TrakWithDref (uint offset, bool selfContained, bool useCo64)
	{
		var url = selfContained
			? FullBox ("url ", 1, [])
			: FullBox ("url ", 0, "ext.bin\0"u8.ToArray ());
		var drefBody = new byte[4 + url.Length];
		BinaryPrimitives.WriteUInt32BigEndian (drefBody, 1);
		url.CopyTo (drefBody.AsSpan (4));
		var dinf = Box ("dinf", FullBox ("dref", 0, drefBody));
		var chunks = useCo64 ? Box ("co64", Co64Body (offset)) : Box ("stco", StcoBody (offset));
		var minf = Box ("minf", Concat (dinf, Box ("stbl", chunks)));
		return Box ("trak", Concat (Box ("tkhd", new byte[4]), Box ("mdia", minf)));
	}

	static byte[] IllegalLargesizeThenStco (uint chunkOffset)
	{
		var stco = Box ("stco", StcoBody (chunkOffset));
		// 16-byte size==1 header whose largesize is 12, overlapping a 12-byte free box, then stco.
		var head = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian (head, 1);
		Encoding.ASCII.GetBytes ("XXXX").CopyTo (head, 4);
		BinaryPrimitives.WriteUInt64BigEndian (head.AsSpan (8), 12);
		Encoding.ASCII.GetBytes ("free").CopyTo (head, 16);
		return Concat (head, stco);
	}

	static uint ReadStcoOccurrence (ReadOnlySpan<byte> data, int occurrence, int entry = 0)
	{
		var start = 0;
		var seen = 0;
		while (start <= data.Length - 4) {
			var rel = IndexOf (data.Slice (start), "stco"u8);
			if (rel < 0)
				break;
			if (seen == occurrence) {
				var at = start + rel + 12 + (entry * 4);
				Assert.IsTrue (at + 4 <= data.Length);
				return BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at, 4));
			}
			seen++;
			start += rel + 4;
		}

		Assert.Fail ($"stco occurrence {occurrence} missing");
		return 0;
	}
}
