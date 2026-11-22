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
/// <item>standard index entries (AVISTDINDEX, and the field and timed variants): "qwBaseOffset +
/// AVISTDINDEX_ENTRY.dwOffset gives the offset from the start of the file to the data", for any base:
/// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avistdindex</item>
/// </list>
/// <para>
/// The new tag chunks are first placed in the room the old tag chunks and the <c>JUNK</c> chunks
/// between <c>hdrl</c> and <c>movi</c> used; the AVI RIFF reference requires <c>hdrl</c> to be the
/// first list. A run of such chunks is a slot. When one slot holds the tags exactly, or with room for
/// a <c>JUNK</c> header, nothing moves and no offset changes. Otherwise the tags go in the first slot,
/// followed by <see cref="ReservedJunkBytes"/> of <c>JUNK</c>, and every byte after that slot moves by
/// the same delta. Every offset above that points at or past the slot is shifted: an absolute value
/// itself, and for a standard index either its qwBaseOffset (when the base moves) or each dwOffset
/// whose target moves (when the base stays). The move is refused, and nothing is written, when an
/// index cannot be verified first.
/// </para>
/// <para>
/// A rewritten tag chunk that is cut off by the end of the file becomes <c>JUNK</c> of the length
/// present, so a removed or cleared value does not come back from it.
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
	const byte IndexSubTwoField = 0x01;
	const long MaxOutputBytes = 0x7FFFFFC7;

	public static bool TryRewrite (ReadOnlySpan<byte> original, RiffInfoTag info, Id3v2Tag? id3, out byte[] output, out string error) =>
		TryRewrite (original, null, info, id3, out output, out error);

	/// <summary>
	/// Rewrites the tags. <paramref name="owned"/>, when set, holds the same bytes as
	/// <paramref name="original"/> and belongs to the caller, who gives it up: a save that moves
	/// nothing writes into it instead of copying the file.
	/// </summary>
	public static bool TryRewrite (
		ReadOnlySpan<byte> original,
		byte[]? owned,
		RiffInfoTag info,
		Id3v2Tag? id3,
		out byte[] output,
		out string error)
	{
		output = [];
		error = "";
		if (!AviChunks.IsAvi (original)) {
			error = "Cannot save: the data is not an AVI file.";
			return false;
		}

		if (!AviChunks.TryFindMovi (original, out var movi)) {
			error = "Cannot save an AVI file that has no movi list.";
			return false;
		}

		var hasHeaders = AviChunks.TryFindHeaderList (original, out var hdrl);
		var infoChanged = !info.SameFieldsAs (AviChunks.MergeInfo (original));
		var id3Present = AviChunks.TryFindId3 (original, out var id3Chunk, out var originalId3);
		var id3Changed = Id3Changed (id3Present, originalId3, id3);
		if (!infoChanged && !id3Changed) {
			output = owned ?? original.ToArray ();
			return true;
		}

		var list = BinaryData.Empty;
		byte[]? characterSet = null;
		if (infoChanged && !info.IsEmpty) {
			if (!info.TryRenderAvi (out list, out var declareUtf8, out var refused)) {
				error = refused!;
				return false;
			}

			// The CSET goes right before the list; exiftool honours only a CSET it meets first.
			characterSet = declareUtf8 ? AviChunks.Utf8CharacterSetChunk () : AviChunks.FirstCharacterSetChunk (original);
		}

		var blob = Blob (list, characterSet, id3, id3Changed, id3Present ? AviChunks.Name (id3Chunk.Id) : "ID32");
		var plan = Plan (original, movi, hasHeaders ? hdrl.Offset : -1, new Changes (infoChanged, characterSet is not null, id3Changed), blob.Length);
		if (blob.Length == 0 || plan.Target is not null) {
			output = owned ?? original.ToArray ();
			if (plan.Target is { } target) {
				blob.CopyTo (output, (int)target.Start);
				if (target.Length > blob.Length)
					WriteJunk (output, target.Start + blob.Length, target.Length - blob.Length);
			}

			foreach (var range in plan.Rewritten)
				WriteJunk (output, range.Start, range.Length);
			return true;
		}

		return TryMove (original, movi, hasHeaders ? hdrl : null, plan, blob, out output, out error);
	}

	/// <summary>
	/// One walk over the first form. Free room is a JUNK chunk between <c>hdrl</c> and <c>movi</c>, or a
	/// tag chunk whose tag changed; adjacent free chunks form a slot. The target is the first slot that
	/// holds <paramref name="blobLength"/>. Every other changed tag chunk, wherever it is, is rewritten as JUNK.
	/// Only slots that hold a tag chunk are kept, so the plan's size does not grow with the JUNK count.
	/// </summary>
	static SavePlan Plan (ReadOnlySpan<byte> original, AviTopChunk movi, long hdrlOffset, Changes changes, int blobLength)
	{
		var plan = new SavePlan ();
		var afterHeaders = hdrlOffset < 0;
		var beforeMovi = true;
		Range? run = null;
		var runHasTag = false;
		var reader = new AviChunkReader (original);
		while (reader.TryNext (out var chunk)) {
			var isTag = IsChangedTag (chunk, changes);
			var isFree = beforeMovi && afterHeaders && !chunk.Truncated && (isTag || chunk.IsJunk);
			if (isFree) {
				run = run is { } open ? new Range (open.Start, chunk.End) : new Range (chunk.Offset, chunk.End);
				runHasTag |= isTag;
			} else {
				Close (plan, ref run, ref runHasTag, blobLength);
				if (isTag)
					plan.Rewritten.Add (new Range (chunk.Offset, chunk.End));
			}

			if (chunk.Offset == movi.Offset)
				beforeMovi = false;
			if (chunk.Offset == hdrlOffset)
				afterHeaders = true;
		}

		Close (plan, ref run, ref runHasTag, blobLength);
		return plan;
	}

	static void Close (SavePlan plan, ref Range? run, ref bool runHasTag, int blobLength)
	{
		if (run is not { } slot)
			return;

		var hadTag = runHasTag;
		run = null;
		runHasTag = false;
		plan.First ??= slot;
		if (plan.Target is null && Holds (slot.Length, blobLength))
			plan.Target = slot;
		else if (hadTag)
			plan.Rewritten.Add (slot);
	}

	/// <summary>A slot holds the tags exactly or with room for a JUNK header after them.</summary>
	static bool Holds (long room, int length) => room == length || room >= length + RiffChunk.HeaderSize;

	/// <summary>
	/// A tag chunk is rewritten only when its tag changed, so an unchanged ID3v2 or INFO chunk keeps
	/// its bytes and its place. A CSET chunk belongs to the INFO text and is rewritten with it. A cut-off chunk counts: its value was read, so it must not outlive a change.
	/// </summary>
	static bool IsChangedTag (AviTopChunk chunk, Changes changes) =>
		AviChunks.IsReadable (chunk)
		&& ((changes.Info && chunk.IsList (AviChunks.Info))
			|| (changes.CharacterSet && chunk.IsCharacterSet)
			|| (changes.Id3 && chunk.IsId3));

	/// <summary>
	/// What the save rewrites. A CSET is rewritten only when a new list is written with one in front:
	/// the file's own CSET again, or a 65001 CSET for a list that became UTF-8. A cleared list leaves it.
	/// </summary>
	readonly struct Changes (bool info, bool characterSet, bool id3)
	{
		public bool Info { get; } = info;

		public bool CharacterSet { get; } = characterSet;

		public bool Id3 { get; } = id3;
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

	static byte[] Blob (BinaryData list, byte[]? characterSet, Id3v2Tag? id3, bool id3Changed, string id3FourCC)
	{
		using var builder = new BinaryDataBuilder ();
		if (list.Length > 0) {
			if (characterSet is not null)
				builder.Add (characterSet);
			builder.Add (list);
		}

		if (id3Changed && id3 is { IsEmpty: false })
			builder.Add (new RiffChunk (id3FourCC, RenderId3 (id3)).Render ());
		return builder.ToBinaryData ().ToArray ();
	}

	static bool TryMove (
		ReadOnlySpan<byte> original,
		AviTopChunk movi,
		AviTopChunk? hdrl,
		SavePlan plan,
		byte[] blob,
		out byte[] output,
		out string error)
	{
		output = [];
		var slot = plan.First ?? new Range (movi.Offset, movi.Offset);
		var granularity = Granularity (original, hdrl);
		var delta = (long)blob.Length + RiffChunk.HeaderSize + ReservedJunkBytes - slot.Length;
		delta = (delta + granularity - 1) / granularity * granularity;

		var riffSize = BinaryPrimitives.ReadUInt32LittleEndian (original.Slice (4));
		// A streaming writer's 0xFFFFFFFF placeholder is past the end of any file this can save.
		if ((long)riffSize + 8 > original.Length) {
			error = "Cannot save: the tags need more room than the AVI file reserves, and its RIFF size does not match the file.";
			return false;
		}

		if ((long)riffSize + delta > uint.MaxValue || original.Length + delta > MaxOutputBytes) {
			error = "Cannot save: the tags need more room than the AVI file reserves, and the file would grow past its size limit.";
			return false;
		}

		var reclaimed = new List<Range> (plan.Rewritten) { slot };
		var patches = new Patches (slot.End, delta);
		if (!CollectOldIndex (original, movi, patches, out error)
			|| (hdrl is { } headers && !CollectOpenDmlIndexes (original, headers, reclaimed, patches, out error))
			|| !patches.Fits (out error))
			return false;

		output = new byte[original.Length + delta];
		original.Slice (0, (int)slot.Start).CopyTo (output);
		blob.CopyTo (output, (int)slot.Start);
		WriteJunk (output, slot.Start + blob.Length, slot.Length + delta - blob.Length);
		original.Slice ((int)slot.End).CopyTo (output.AsSpan ((int)(slot.End + delta)));
		BinaryPrimitives.WriteUInt32LittleEndian (output.AsSpan (4), (uint)(riffSize + delta));

		foreach (var range in plan.Rewritten) {
			if (range.Start != slot.Start)
				WriteJunk (output, patches.Map (range.Start), range.Length);
		}

		patches.Apply (output);
		return true;
	}

	/// <summary>
	/// avih dwPaddingGranularity: data is padded to multiples of this value. A move by a multiple keeps that alignment.
	/// </summary>
	static long Granularity (ReadOnlySpan<byte> original, AviTopChunk? hdrl)
	{
		if (hdrl is not { } headers)
			return 2;

		var body = AviChunks.Body (original, headers).Slice (4);
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
	static bool CollectOldIndex (ReadOnlySpan<byte> original, AviTopChunk movi, Patches patches, out string error)
	{
		error = "";
		var relativeBase = movi.DataOffset;
		var reader = new AviChunkReader (original);
		while (reader.TryNext (out var chunk)) {
			if (chunk.Id != AviChunks.Idx1)
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
					patches.AddAbsolute32 (original, chunk.DataOffset + j * OldIndexEntryBytes + 8);
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
	/// index chunks. A standard index can also sit in the <c>strl</c> itself.
	/// </summary>
	static bool CollectOpenDmlIndexes (
		ReadOnlySpan<byte> original,
		AviTopChunk hdrl,
		List<Range> reclaimed,
		Patches patches,
		out string error)
	{
		error = "";
		var hdrlBody = hdrl.DataOffset + 4;
		var body = AviChunks.Body (original, hdrl).Slice (4);
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
			if (!CollectStandardIndex (original, data - RiffChunk.HeaderSize, reclaimed, patches))
				return false;
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
			if (offset > (ulong)original.Length || !CollectStandardIndex (original, (long)offset, reclaimed, patches))
				return false;
			patches.AddAbsolute64 (original, entry);
		}

		error = "";
		return true;
	}

	/// <summary>
	/// A standard, timed or field index chunk at <paramref name="chunk"/>, outside any rewritten
	/// chunk, with its whole entry table inside the chunk. When its qwBaseOffset moves, shifting the
	/// base keeps every entry. When the base stays, each dwOffset whose target moves is shifted instead.
	/// A field index's entries carry a second-field offset as well, so it is not patched entry by entry.
	/// Each index is collected once, even when two super index entries name it.
	/// </summary>
	static bool CollectStandardIndex (ReadOnlySpan<byte> original, long chunk, List<Range> reclaimed, Patches patches)
	{
		if (!patches.FirstVisit (chunk))
			return true;
		if (chunk < 0 || chunk + RiffChunk.HeaderSize + IndexHeaderBytes > original.Length)
			return false;

		var size = BinaryPrimitives.ReadUInt32LittleEndian (original.Slice ((int)chunk + 4));
		var data = chunk + RiffChunk.HeaderSize;
		var header = original.Slice ((int)data, IndexHeaderBytes);
		var longsPerEntry = BinaryPrimitives.ReadUInt16LittleEndian (header);
		var subType = header[2];
		var type = header[3];
		var count = BinaryPrimitives.ReadUInt32LittleEndian (header.Slice (4));
		if (type is not (IndexOfChunks or IndexOfTimedChunks or IndexOfSubTwoField) || longsPerEntry < 2)
			return false;

		var stride = longsPerEntry * 4L;
		var end = data + IndexHeaderBytes + count * stride;
		if (end > data + size || end > original.Length)
			return false;
		foreach (var range in reclaimed) {
			if (end > range.Start && chunk < range.End)
				return false;
		}

		var baseOffset = BinaryPrimitives.ReadUInt64LittleEndian (header.Slice (12));
		if (baseOffset >= (ulong)patches.Threshold) {
			patches.AddAbsolute64 (original, data + 12);
			return true;
		}

		var twoField = subType == IndexSubTwoField || type == IndexOfSubTwoField;
		for (var k = 0; k < count; k++) {
			var entry = data + IndexHeaderBytes + k * stride;
			if (!patches.AddRelative32 (original, entry, baseOffset))
				continue;
			if (twoField)
				return false;
		}

		return true;
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

	sealed class SavePlan
	{
		/// <summary>The first slot after <c>hdrl</c>; a move grows it.</summary>
		public Range? First { get; set; }

		/// <summary>The first slot that holds the tags, when one does.</summary>
		public Range? Target { get; set; }

		/// <summary>Changed tag chunks and slots holding them, other than the target, to rewrite as JUNK.</summary>
		public List<Range> Rewritten { get; } = [];
	}

	/// <summary>
	/// Offsets to shift, as positions in the original file. Whether a value moves is decided from the
	/// original bytes when it is added: an absolute value moves when it points at or past
	/// <see cref="Threshold"/>; a dwOffset moves when its base plus itself does. Each added value
	/// gains the delta when applied.
	/// </summary>
	sealed class Patches (long threshold, long delta)
	{
		readonly HashSet<long> _visited = [];
		readonly List<(long Position, bool Wide)> _positions = [];
		bool _overflows;

		/// <summary>The first original offset that moves.</summary>
		public long Threshold => threshold;

		public long Map (long position) => position >= threshold ? position + delta : position;

		/// <summary>True the first time a standard index chunk is seen.</summary>
		public bool FirstVisit (long chunk) => _visited.Add (chunk);

		public void AddAbsolute32 (ReadOnlySpan<byte> original, long position)
		{
			var value = BinaryPrimitives.ReadUInt32LittleEndian (original.Slice ((int)position));
			if (value < threshold)
				return;
			_overflows |= value + delta > uint.MaxValue;
			_positions.Add ((position, false));
		}

		public void AddAbsolute64 (ReadOnlySpan<byte> original, long position)
		{
			var value = BinaryPrimitives.ReadUInt64LittleEndian (original.Slice ((int)position));
			if (value < (ulong)threshold)
				return;
			_overflows |= value > (ulong)(long.MaxValue - delta);
			_positions.Add ((position, true));
		}

		/// <summary>Adds a dwOffset relative to <paramref name="baseOffset"/> when its target moves, and says whether it did.</summary>
		public bool AddRelative32 (ReadOnlySpan<byte> original, long position, ulong baseOffset)
		{
			var value = BinaryPrimitives.ReadUInt32LittleEndian (original.Slice ((int)position));
			if (baseOffset + value < (ulong)threshold)
				return false;
			_overflows |= value + delta > uint.MaxValue;
			_positions.Add ((position, false));
			return true;
		}

		public bool Fits (out string error)
		{
			error = _overflows ? "Cannot save: moving the AVI data would overflow an index offset." : "";
			return !_overflows;
		}

		public void Apply (byte[] output)
		{
			foreach (var (position, wide) in _positions) {
				var at = output.AsSpan ((int)Map (position));
				if (wide)
					BinaryPrimitives.WriteUInt64LittleEndian (at, BinaryPrimitives.ReadUInt64LittleEndian (at) + (ulong)delta);
				else
					BinaryPrimitives.WriteUInt32LittleEndian (at, (uint)(BinaryPrimitives.ReadUInt32LittleEndian (at) + delta));
			}
		}
	}
}
