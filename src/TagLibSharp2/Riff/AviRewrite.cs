// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Riff;

/// <summary>
/// Writes AVI tag chunks without changing the <c>movi</c> sample data or breaking an index.
/// </summary>
/// <remarks>
/// <para>
/// Invariant: every index entry in the saved file resolves to the same sample bytes it resolved
/// to in the original. Offsets that can point into the file are:
/// </para>
/// <list type="bullet">
/// <item><c>idx1</c> dwChunkOffset (AVIOLDINDEX), relative to the <c>movi</c> list type or, in some files,
/// absolute: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avioldindex</item>
/// <item><c>indx</c> super index qwOffset (AVISUPERINDEX), absolute:
/// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avisuperindex</item>
/// <item>standard index qwBaseOffset (AVISTDINDEX, also the field and timed variants), absolute:
/// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avistdindex</item>
/// </list>
/// <para>
/// The new tag chunks are first placed in the room the old tag chunks and the <c>JUNK</c> chunks
/// before <c>movi</c> used. A run of such chunks is a slot. When one slot holds the tags exactly, or
/// with room for a <c>JUNK</c> header, nothing after the slot moves and no offset changes. Otherwise
/// the tags go in the first slot, followed by <see cref="ReservedJunkBytes"/> of <c>JUNK</c>, and
/// every byte after that slot moves by the same delta. Each offset listed above that points at or past
/// the slot is shifted by that delta. The move is refused, and nothing is written, when an index
/// cannot be verified first: an <c>idx1</c> whose entries match neither offset base, or an
/// <c>indx</c> of an index type this writer does not know.
/// </para>
/// </remarks>
internal static class AviRewrite
{
	/// <summary>JUNK left after the tags when the data has to move, so the next edit fits in place.</summary>
	internal const int ReservedJunkBytes = 1024;

	const int IndexHeaderBytes = 24;
	const int SuperIndexEntryBytes = 16;
	const int OldIndexEntryBytes = 16;
	const byte IndexOfIndexes = 0x00;
	const byte IndexOfChunks = 0x01;
	const byte IndexOfTimedChunks = 0x02;
	const byte IndexOfSubTwoField = 0x03;
	const long MaxOutputBytes = 0x7FFFFFC7;

	public static bool TryRewrite (ReadOnlySpan<byte> original, RiffInfoTag info, Id3v2Tag? id3, out byte[] output, out string error)
	{
		output = [];
		error = "";
		if (!AviChunks.IsAvi (original)) {
			error = "Cannot save: the data is not an AVI file.";
			return false;
		}

		var chunks = AviChunks.Walk (original);
		var movi = chunks.FindIndex (c => c.IsList ("movi"));
		if (movi < 0) {
			error = "Cannot save an AVI file that has no movi list.";
			return false;
		}

		var originalInfo = AviChunks.MergeInfo (original, chunks)?.Render () ?? BinaryData.Empty;
		var infoChanged = !originalInfo.Equals (info.Render ());
		var id3Index = AviChunks.FirstId3 (original, chunks, out var originalId3);
		var id3Changed = Id3Changed (id3Index >= 0, originalId3, id3);
		if (!infoChanged && !id3Changed) {
			output = original.ToArray ();
			return true;
		}

		var blob = Blob (info, infoChanged, id3, id3Changed, id3Index >= 0 ? chunks[id3Index].FourCC : "ID32");
		var slots = new List<Range> ();
		var after = new List<Range> ();
		for (var i = 0; i < chunks.Count; i++) {
			if (!IsReclaimed (chunks[i], i < movi, infoChanged, id3Changed))
				continue;
			if (i > movi)
				after.Add (new Range (chunks[i].Offset, chunks[i].End));
			else if (slots.Count > 0 && slots[slots.Count - 1].End == chunks[i].Offset)
				slots[slots.Count - 1] = new Range (slots[slots.Count - 1].Start, chunks[i].End);
			else
				slots.Add (new Range (chunks[i].Offset, chunks[i].End));
		}

		var target = PickSlot (slots, blob.Length);
		if (blob.Length == 0 || target >= 0) {
			output = original.ToArray ();
			for (var k = 0; k < slots.Count; k++) {
				var slot = slots[k];
				if (k == target) {
					blob.CopyTo (output, (int)slot.Start);
					if (slot.Length > blob.Length)
						WriteJunk (output, slot.Start + blob.Length, slot.Length - blob.Length);
				} else {
					WriteJunk (output, slot.Start, slot.Length);
				}
			}

			foreach (var range in after)
				WriteJunk (output, range.Start, range.Length);
			return true;
		}

		return TryMove (original, chunks, movi, slots, after, blob, out output, out error);
	}

	/// <summary>
	/// A JUNK chunk before <c>movi</c> is free room. A tag chunk is rewritten only when its tag changed,
	/// so an unchanged ID3v2 or INFO chunk keeps its bytes and its place.
	/// </summary>
	static bool IsReclaimed (AviTopChunk chunk, bool beforeMovi, bool infoChanged, bool id3Changed)
	{
		if (chunk.Truncated)
			return false;
		if (chunk.FourCC == "JUNK")
			return beforeMovi;
		if (!AviChunks.IsReadable (chunk))
			return false;
		return (infoChanged && chunk.IsList ("INFO")) || (id3Changed && chunk.IsId3);
	}

	static bool Id3Changed (bool present, Id3v2Tag? original, Id3v2Tag? current)
	{
		if (!present)
			return current is { IsEmpty: false };
		if (original is null)
			return current is not null;
		return current is null || !RenderId3 (current).Equals (RenderId3 (original));
	}

	/// <summary>An ID3v2 tag in a RIFF chunk needs no padding of its own; JUNK holds the spare room.</summary>
	static BinaryData RenderId3 (Id3v2Tag tag) => tag.Render (0);

	static byte[] Blob (RiffInfoTag info, bool infoChanged, Id3v2Tag? id3, bool id3Changed, string id3FourCC)
	{
		using var builder = new BinaryDataBuilder ();
		if (infoChanged && !info.IsEmpty)
			builder.Add (info.Render ());
		if (id3Changed && id3 is { IsEmpty: false })
			builder.Add (new RiffChunk (id3FourCC, RenderId3 (id3)).Render ());
		return builder.ToBinaryData ().ToArray ();
	}

	/// <summary>
	/// The first slot that holds <paramref name="length"/> bytes exactly or with room for a JUNK header, or -1.
	/// </summary>
	static int PickSlot (List<Range> slots, int length)
	{
		for (var k = 0; k < slots.Count; k++) {
			var room = slots[k].Length;
			if (room == length || room >= length + RiffChunk.HeaderSize)
				return k;
		}

		return -1;
	}

	static bool TryMove (
		ReadOnlySpan<byte> original,
		List<AviTopChunk> chunks,
		int movi,
		List<Range> slots,
		List<Range> after,
		byte[] blob,
		out byte[] output,
		out string error)
	{
		output = [];
		var slot = slots.Count > 0 ? slots[0] : new Range (chunks[movi].Offset, chunks[movi].Offset);
		var hdrl = AviChunks.HeaderList (chunks);
		var granularity = Granularity (original, chunks, hdrl);
		var delta = (long)blob.Length + RiffChunk.HeaderSize + ReservedJunkBytes - slot.Length;
		delta = (delta + granularity - 1) / granularity * granularity;

		var riffSize = BinaryPrimitives.ReadUInt32LittleEndian (original.Slice (4));
		if (riffSize == 0xFFFFFFFF || (long)riffSize + 8 > original.Length) {
			error = "Cannot save: the tags need more room than the AVI file reserves, and its RIFF size does not match the file.";
			return false;
		}

		if ((long)riffSize + delta > uint.MaxValue || original.Length + delta > MaxOutputBytes) {
			error = "Cannot save: the tags need more room than the AVI file reserves, and the file would grow past its size limit.";
			return false;
		}

		var reclaimed = new List<Range> (slots);
		reclaimed.AddRange (after);
		var patches = new Patches (slot.End, delta);
		if (!CollectOldIndex (original, chunks, movi, patches, out error)
			|| !CollectOpenDmlIndexes (original, chunks, hdrl, reclaimed, patches, out error)
			|| !patches.Fits (original, out error))
			return false;

		output = new byte[original.Length + delta];
		original.Slice (0, (int)slot.Start).CopyTo (output);
		blob.CopyTo (output, (int)slot.Start);
		WriteJunk (output, slot.Start + blob.Length, slot.Length + delta - blob.Length);
		original.Slice ((int)slot.End).CopyTo (output.AsSpan ((int)(slot.End + delta)));
		BinaryPrimitives.WriteUInt32LittleEndian (output.AsSpan (4), (uint)(riffSize + delta));

		for (var k = 1; k < slots.Count; k++)
			WriteJunk (output, patches.Map (slots[k].Start), slots[k].Length);
		foreach (var range in after)
			WriteJunk (output, patches.Map (range.Start), range.Length);

		patches.Apply (output);
		return true;
	}

	/// <summary>
	/// avih dwPaddingGranularity: data is padded to multiples of this value. A move by a multiple keeps that alignment.
	/// </summary>
	static long Granularity (ReadOnlySpan<byte> original, List<AviTopChunk> chunks, int hdrl)
	{
		if (hdrl < 0)
			return 2;

		var body = AviChunks.Body (original, chunks[hdrl]).Slice (4);
		foreach (var child in AviHeaders.Children (body)) {
			if (!child.Is ("avih") || child.DataLength < 12)
				continue;
			var value = BinaryPrimitives.ReadUInt32LittleEndian (body.Slice (child.DataOffset + 8));
			return value >= 2 && value <= 1024 * 1024 && (value & 1) == 0 ? value : 2;
		}

		return 2;
	}

	/// <summary>
	/// Absolute <c>idx1</c> offsets move with the data. Relative offsets count from the <c>movi</c>
	/// list type, which moves too, so they stay. The first entry that names a chunk decides which.
	/// </summary>
	static bool CollectOldIndex (ReadOnlySpan<byte> original, List<AviTopChunk> chunks, int movi, Patches patches, out string error)
	{
		error = "";
		var relativeBase = chunks[movi].DataOffset;
		foreach (var chunk in chunks) {
			if (chunk.FourCC != "idx1")
				continue;

			var entries = AviChunks.Body (original, chunk).Length / OldIndexEntryBytes;
			bool? absolute = null;
			for (var j = 0; j < entries && absolute is null; j++) {
				var entry = original.Slice ((int)chunk.DataOffset + j * OldIndexEntryBytes);
				var id = entry.Slice (0, 4);
				var offset = BinaryPrimitives.ReadUInt32LittleEndian (entry.Slice (8));
				if (NamesChunk (original, relativeBase + offset, id))
					absolute = false;
				else if (NamesChunk (original, offset, id))
					absolute = true;
			}

			if (entries > 0 && absolute is null) {
				error = "Cannot save: the tags need more room than the AVI file reserves, and its idx1 offsets do not point at its chunks.";
				return false;
			}

			if (absolute == true) {
				for (var j = 0; j < entries; j++)
					patches.Add32 (chunk.DataOffset + j * OldIndexEntryBytes + 8);
			}
		}

		return true;
	}

	/// <summary>True when a chunk, or a LIST of that type (a <c>rec </c> list), starts at <paramref name="position"/>.</summary>
	static bool NamesChunk (ReadOnlySpan<byte> data, long position, ReadOnlySpan<byte> id)
	{
		if (position < 0 || position + RiffChunk.HeaderSize > data.Length)
			return false;

		var at = (int)position;
		if (data.Slice (at, 4).SequenceEqual (id))
			return true;
		return data.Slice (at, 4).SequenceEqual ("LIST"u8)
			&& position + 12 <= data.Length
			&& data.Slice (at + 8, 4).SequenceEqual (id);
	}

	/// <summary>
	/// Every <c>indx</c> in a <c>strl</c>. A super index's entries are absolute offsets of standard
	/// index chunks, and each of those holds an absolute qwBaseOffset. A standard index can also sit
	/// in the <c>strl</c> itself.
	/// </summary>
	static bool CollectOpenDmlIndexes (
		ReadOnlySpan<byte> original,
		List<AviTopChunk> chunks,
		int hdrl,
		List<Range> reclaimed,
		Patches patches,
		out string error)
	{
		error = "";
		if (hdrl < 0)
			return true;

		var hdrlBody = chunks[hdrl].DataOffset + 4;
		var body = AviChunks.Body (original, chunks[hdrl]).Slice (4);
		foreach (var strl in AviHeaders.Children (body)) {
			if (!strl.IsList ("strl"))
				continue;

			var strlBody = hdrlBody + strl.DataOffset + 4;
			foreach (var child in AviHeaders.Children (body.Slice (strl.DataOffset + 4, strl.DataLength - 4))) {
				if (child.Is ("indx")
					&& !CollectIndex (original, strlBody + child.DataOffset, child.DataLength, reclaimed, patches, out error))
					return false;
			}
		}

		return true;
	}

	static bool CollectIndex (ReadOnlySpan<byte> original, long data, int length, List<Range> reclaimed, Patches patches, out string error)
	{
		error = "Cannot save: the tags need more room than the AVI file reserves, and an OpenDML index could not be verified.";
		if (length < IndexHeaderBytes)
			return false;

		var header = original.Slice ((int)data, IndexHeaderBytes);
		var type = header[3];
		if (type is IndexOfChunks or IndexOfTimedChunks or IndexOfSubTwoField) {
			patches.Add64 (data + 12);
			error = "";
			return true;
		}

		if (type != IndexOfIndexes || BinaryPrimitives.ReadUInt16LittleEndian (header) != 4)
			return false;

		var count = BinaryPrimitives.ReadUInt32LittleEndian (header.Slice (4));
		if (IndexHeaderBytes + (long)count * SuperIndexEntryBytes > length)
			return false;

		for (var e = 0; e < count; e++) {
			var entry = data + IndexHeaderBytes + (long)e * SuperIndexEntryBytes;
			var offset = BinaryPrimitives.ReadUInt64LittleEndian (original.Slice ((int)entry));
			if (offset == 0)
				continue;
			if (!IsStandardIndex (original, offset, reclaimed))
				return false;
			patches.Add64 (entry);
			patches.Add64 ((long)offset + RiffChunk.HeaderSize + 12);
		}

		error = "";
		return true;
	}

	/// <summary>A standard, timed or field index chunk header at <paramref name="offset"/>, outside any rewritten chunk.</summary>
	static bool IsStandardIndex (ReadOnlySpan<byte> original, ulong offset, List<Range> reclaimed)
	{
		if (original.Length < RiffChunk.HeaderSize + IndexHeaderBytes
			|| offset > (ulong)(original.Length - RiffChunk.HeaderSize - IndexHeaderBytes))
			return false;

		var at = (long)offset;
		foreach (var range in reclaimed) {
			if (at + RiffChunk.HeaderSize + IndexHeaderBytes > range.Start && at < range.End)
				return false;
		}

		var size = BinaryPrimitives.ReadUInt32LittleEndian (original.Slice ((int)at + 4));
		var type = original[(int)at + RiffChunk.HeaderSize + 3];
		return size >= IndexHeaderBytes && type is IndexOfChunks or IndexOfTimedChunks or IndexOfSubTwoField;
	}

	/// <summary>Writes a JUNK chunk of <paramref name="total"/> bytes, header included, with zero data.</summary>
	static void WriteJunk (byte[] buffer, long offset, long total)
	{
		var at = (int)offset;
		"JUNK"u8.CopyTo (buffer.AsSpan (at));
		BinaryPrimitives.WriteUInt32LittleEndian (buffer.AsSpan (at + 4), (uint)(total - RiffChunk.HeaderSize));
		Array.Clear (buffer, at + RiffChunk.HeaderSize, (int)(total - RiffChunk.HeaderSize));
	}

	readonly struct Range
	{
		public Range (long start, long end)
		{
			Start = start;
			End = end;
		}

		public long Start { get; }

		public long End { get; }

		public long Length => End - Start;
	}

	/// <summary>
	/// Absolute offsets to shift. Positions are in the original file. A stored value at or past
	/// <see cref="_threshold"/> points at data that moved, and gains <see cref="_delta"/>.
	/// Each position is patched once, even when two super index entries name the same standard index.
	/// </summary>
	sealed class Patches
	{
		readonly long _threshold;
		readonly long _delta;
		readonly HashSet<long> _seen = [];
		readonly List<(long Position, bool Wide)> _positions = [];

		public Patches (long threshold, long delta)
		{
			_threshold = threshold;
			_delta = delta;
		}

		public long Map (long position) => position >= _threshold ? position + _delta : position;

		public void Add32 (long position)
		{
			if (_seen.Add (position))
				_positions.Add ((position, false));
		}

		public void Add64 (long position)
		{
			if (_seen.Add (position))
				_positions.Add ((position, true));
		}

		public bool Fits (ReadOnlySpan<byte> original, out string error)
		{
			error = "";
			foreach (var (position, wide) in _positions) {
				var value = wide
					? BinaryPrimitives.ReadUInt64LittleEndian (original.Slice ((int)position))
					: BinaryPrimitives.ReadUInt32LittleEndian (original.Slice ((int)position));
				if (value >= (ulong)_threshold && value + (ulong)_delta > (wide ? (ulong)long.MaxValue : uint.MaxValue)) {
					error = "Cannot save: moving the AVI data would overflow an index offset.";
					return false;
				}
			}

			return true;
		}

		public void Apply (byte[] output)
		{
			foreach (var (position, wide) in _positions) {
				var at = output.AsSpan ((int)Map (position));
				if (wide) {
					var value = BinaryPrimitives.ReadUInt64LittleEndian (at);
					if (value >= (ulong)_threshold)
						BinaryPrimitives.WriteUInt64LittleEndian (at, value + (ulong)_delta);
				} else {
					var value = BinaryPrimitives.ReadUInt32LittleEndian (at);
					if (value >= (ulong)_threshold)
						BinaryPrimitives.WriteUInt32LittleEndian (at, (uint)(value + _delta));
				}
			}
		}
	}
}
