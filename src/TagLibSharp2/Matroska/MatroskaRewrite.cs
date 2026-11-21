// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// Splices a rendered <c>Tags</c> element into the first Segment.
/// An unknown-size element is copied only when its end is a parsed sibling or the parent end.
/// Bytes that do not parse are not scanned once a real element has been seen.
/// </summary>
static class MatroskaRewrite
{
	public static bool TryRewrite (ReadOnlySpan<byte> data, MatroskaTag tag, out byte[] rewritten, out string error)
	{
		rewritten = [];
		if (!TryParse (data, out var layout, out error) || layout is null)
			return false;

		var tags = tag.Render ().ToArray ();
		var plan = PlanEdit (layout.Pieces, tags);
		if (!TryBuild (data, layout, plan, out var body, out var seekHeads, out var cues, out var clusters, out var tagsOffset, out error))
			return false;
		if (tags.Length > 0 && tagsOffset != plan.TagsOffset) {
			error = "Matroska tags could not be placed";
			return false;
		}

		if (!TryPatch (body, seekHeads, cues, clusters, plan, out error))
			return false;

		var originalLength = layout.SegmentEnd - layout.DataOffset;
		if (originalLength != body.Length || !data.Slice (layout.DataOffset, originalLength).SequenceEqual (body))
			UpdateLeadingCrc (body);

		rewritten = Assemble (data, layout, body, out error);
		return rewritten.Length > 0 && error.Length == 0;
	}

	sealed class Layout
	{
		public Layout (int segmentPosition, int idWidth, int sizeWidth, bool unknownSize, int dataOffset, int segmentEnd, List<Piece> pieces)
		{
			SegmentPosition = segmentPosition;
			IdWidth = idWidth;
			SizeWidth = sizeWidth;
			UnknownSize = unknownSize;
			DataOffset = dataOffset;
			SegmentEnd = segmentEnd;
			Pieces = pieces;
		}

		public int SegmentPosition { get; }

		public int IdWidth { get; }

		public int SizeWidth { get; }

		public bool UnknownSize { get; }

		public int DataOffset { get; }

		public int SegmentEnd { get; }

		public List<Piece> Pieces { get; }
	}

	readonly struct Piece
	{
		public Piece (ulong id, int offset, int length, bool unknownSize)
		{
			Id = id;
			Offset = offset;
			Length = length;
			UnknownSize = unknownSize;
		}

		public ulong Id { get; }

		public int Offset { get; }

		public int Length { get; }

		public bool UnknownSize { get; }
	}

	sealed class Plan
	{
		public byte[] Tags { get; set; } = [];

		public int ReplaceStart { get; set; } = -1;

		public int ReplaceEnd { get; set; } = -1;

		public byte[]? Replacement { get; set; }

		public bool VoidTags { get; set; }

		public bool Append { get; set; }

		public int InsertBefore { get; set; } = -1;

		public int TagsOffset { get; set; } = -1;

		public int ShiftFrom { get; set; } = int.MaxValue;

		public int Delta { get; set; }

		public bool MustRetarget { get; set; }

		public bool ShouldAdd { get; set; }
	}

	static Plan PlanEdit (List<Piece> pieces, byte[] tags)
	{
		var plan = new Plan { Tags = tags };
		var tagIndexes = new List<int> ();
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id == MatroskaIds.Tags)
				tagIndexes.Add (i);
		}

		if (tags.Length == 0) {
			plan.VoidTags = tagIndexes.Count > 0;
			plan.MustRetarget = tagIndexes.Count > 0;
			return plan;
		}

		plan.ShouldAdd = tagIndexes.Count == 0;
		if (TryFindFit (pieces, tags, out var start, out var end, out var fitted)) {
			plan.ReplaceStart = start;
			plan.ReplaceEnd = end;
			plan.Replacement = fitted;
			plan.TagsOffset = OffsetOf (pieces, start);
			plan.VoidTags = true;
			plan.MustRetarget = TagMoved (pieces, tagIndexes, plan.TagsOffset);
			return plan;
		}

		plan.VoidTags = true;
		var last = pieces.Count - 1;
		if (last >= 0 && pieces[last].Id == MatroskaIds.Tags) {
			plan.ReplaceStart = last;
			plan.ReplaceEnd = last;
			plan.Replacement = tags;
			plan.TagsOffset = OffsetOf (pieces, last);
			plan.MustRetarget = TagMoved (pieces, tagIndexes, plan.TagsOffset);
			return plan;
		}

		if (last >= 0 && pieces[last].UnknownSize) {
			plan.InsertBefore = last;
			plan.TagsOffset = OffsetOf (pieces, last);
			plan.ShiftFrom = plan.TagsOffset;
			plan.Delta = tags.Length;
			plan.MustRetarget = true;
			return plan;
		}

		plan.Append = true;
		plan.TagsOffset = OffsetOf (pieces, pieces.Count);
		plan.MustRetarget = tagIndexes.Count > 0;
		return plan;
	}

	static bool TagMoved (List<Piece> pieces, List<int> tagIndexes, int tagsOffset)
	{
		foreach (var index in tagIndexes) {
			if (OffsetOf (pieces, index) != tagsOffset)
				return true;
		}

		return false;
	}

	static bool TryBuild (
		ReadOnlySpan<byte> data,
		Layout layout,
		Plan plan,
		out byte[] body,
		out List<(int Start, int End)> seekHeads,
		out List<(int Start, int End)> cues,
		out List<(int Start, int End, int SegmentOffset)> clusters,
		out int tagsOffset,
		out string error)
	{
		body = [];
		seekHeads = new List<(int, int)> ();
		cues = new List<(int, int)> ();
		clusters = new List<(int, int, int)> ();
		tagsOffset = -1;
		error = "";
		var pieces = layout.Pieces;
		if (plan.Replacement is not null && plan.ReplaceEnd != pieces.Count - 1) {
			var region = 0;
			for (var index = plan.ReplaceStart; index <= plan.ReplaceEnd; index++)
				region += pieces[index].Length;
			if (plan.Replacement.Length != region) {
				error = "Matroska tags could not be placed";
				return false;
			}
		}

		using var builder = new BinaryDataBuilder (Math.Max (16, data.Length));
		var i = 0;
		while (i < pieces.Count) {
			if (i == plan.ReplaceStart && plan.Replacement is not null) {
				tagsOffset = builder.Length;
				builder.Add (plan.Replacement);
				i = plan.ReplaceEnd + 1;
				continue;
			}

			var piece = pieces[i];
			if (plan.VoidTags && piece.Id == MatroskaIds.Tags) {
				if (!TryMakeVoid (piece.Length, out var hole)) {
					error = "Matroska tags could not be placed";
					return false;
				}

				builder.Add (hole);
				i++;
				continue;
			}

			if (i == plan.InsertBefore) {
				tagsOffset = builder.Length;
				builder.Add (plan.Tags);
			}

			var at = builder.Length;
			builder.Add (data.Slice (piece.Offset, piece.Length));
			if (piece.Id == MatroskaIds.SeekHead)
				seekHeads.Add ((at, at + piece.Length));
			else if (piece.Id == MatroskaIds.Cues)
				cues.Add ((at, at + piece.Length));
			else if (piece.Id == MatroskaIds.Cluster) {
				// Piece.Offset is the file offset. Position is relative to the segment payload.
				clusters.Add ((at, at + piece.Length, piece.Offset - layout.DataOffset));
			}
			i++;
		}

		if (plan.Append) {
			tagsOffset = builder.Length;
			builder.Add (plan.Tags);
		}

		body = builder.ToArray ();
		return true;
	}

	static bool TryPatch (
		byte[] body,
		List<(int Start, int End)> seekHeads,
		List<(int Start, int End)> cues,
		List<(int Start, int End, int SegmentOffset)> clusters,
		Plan plan,
		out string error)
	{
		error = "";
		var strict = plan.MustRetarget || plan.Delta != 0;
		if (strict || plan.ShouldAdd) {
			foreach (var seekHead in seekHeads) {
				if (!TryPatchSeekHead (body, seekHead.Start, seekHead.End, plan, strict, out error))
					return false;
			}
		}

		if (plan.Delta == 0)
			return true;

		foreach (var cue in cues) {
			if (!TryPatchCues (body, cue.Start, cue.End, plan, out error))
				return false;
		}

		foreach (var cluster in clusters) {
			if (!TryPatchClusterPosition (body, cluster.Start, cluster.End, cluster.SegmentOffset, plan, out error))
				return false;
		}

		return true;
	}

	static bool TryPatchSeekHead (byte[] body, int start, int end, Plan plan, bool strict, out string error)
	{
		error = "";
		if (!EbmlReader.TryRead (body, start, Math.Min (end, body.Length), out var seekHead)
			|| seekHead.UnknownSize
			|| seekHead.Position != start) {
			error = "Matroska SeekHead cannot be bounded";
			return !strict;
		}

		var children = new List<EbmlElement> ();
		var offset = seekHead.DataOffset;
		while (offset < seekHead.End) {
			if (!EbmlReader.TryRead (body, offset, seekHead.End, out var child) || child.UnknownSize) {
				error = "Matroska SeekHead cannot be bounded";
				return !strict;
			}

			children.Add (child);
			offset = child.End;
		}

		if (offset != seekHead.End) {
			error = "Matroska SeekHead cannot be bounded";
			return !strict;
		}

		var changed = false;
		var referenced = false;
		foreach (var child in children) {
			if (child.Id != MatroskaIds.Seek)
				continue;
			if (!TryPatchSeek (body, child, plan, ref changed, ref referenced)) {
				error = "Matroska SeekHead cannot be updated";
				return false;
			}
		}

		if (plan.TagsOffset >= 0 && !referenced && TryAddSeek (body, seekHead, (ulong)plan.TagsOffset)) {
			changed = true;
			referenced = true;
		}

		if (changed)
			UpdateElementCrc (body, seekHead.Position, seekHead.End);
		return true;
	}

	static bool TryPatchSeek (byte[] body, EbmlElement seek, Plan plan, ref bool changed, ref bool referenced)
	{
		var strict = plan.MustRetarget || plan.Delta != 0;
		if (!TryReadSeek (body, seek, out var id, out var position, out var positionElement))
			return strict ? TryVoidElement (body, seek, ref changed) : true;

		var isTags = id == MatroskaIds.Tags;
		ulong desired;
		if (isTags) {
			if (plan.TagsOffset < 0)
				return TryVoidElement (body, seek, ref changed);
			desired = (ulong)plan.TagsOffset;
		} else if (plan.Delta != 0 && position >= (ulong)plan.ShiftFrom) {
			desired = position + (ulong)plan.Delta;
			if (desired < position)
				return false;
		} else
			return true;

		if (desired == position) {
			if (isTags)
				referenced = true;
			return true;
		}

		if (positionElement.DataLength is > 0 and <= 8
			&& TryWriteUnsigned (body, positionElement.DataOffset, (int)positionElement.DataLength, desired)) {
			changed = true;
			UpdateElementCrc (body, seek.Position, seek.End);
			if (isTags)
				referenced = true;
			return true;
		}

		var rebuilt = MakeSeek (isTags ? MatroskaIds.Tags : id, desired);
		var total = seek.End - seek.Position;
		if (TryFit (rebuilt, total, out var fitted)) {
			fitted.CopyTo (body.AsSpan (seek.Position));
			changed = true;
			if (isTags)
				referenced = true;
			return true;
		}

		return TryVoidElement (body, seek, ref changed);
	}

	static bool TryReadSeek (byte[] body, EbmlElement seek, out ulong id, out ulong position, out EbmlElement positionElement)
	{
		id = 0;
		position = 0;
		positionElement = default;
		var sawId = false;
		var sawPosition = false;
		var offset = seek.DataOffset;
		while (offset < seek.End) {
			if (!EbmlReader.TryRead (body, offset, seek.End, out var child) || child.UnknownSize)
				return false;
			var payload = body.AsSpan (child.DataOffset, child.UnknownSize ? 0 : (int)child.DataLength);
			if (child.Id == MatroskaIds.SeekId && payload.Length > 0) {
				id = ReadId (payload);
				sawId = true;
			} else if (child.Id == MatroskaIds.SeekPosition) {
				position = child.DataLength <= 0 ? 0 : EbmlReader.ReadUInt (payload);
				positionElement = child;
				sawPosition = true;
			}

			offset = child.End;
		}

		return sawId && sawPosition && offset == seek.End;
	}

	static bool TryVoidElement (byte[] body, EbmlElement element, ref bool changed)
	{
		var total = element.End - element.Position;
		if (!TryMakeVoid (total, out var hole))
			return false;
		hole.CopyTo (body.AsSpan (element.Position));
		changed = true;
		return true;
	}

	static bool TryAddSeek (byte[] body, EbmlElement seekHead, ulong tagsOffset)
	{
		var seek = MakeSeek (MatroskaIds.Tags, tagsOffset);
		var offset = seekHead.DataOffset;
		while (offset < seekHead.End) {
			if (!EbmlReader.TryRead (body, offset, seekHead.End, out var child) || child.UnknownSize)
				break;
			if (child.Id == MatroskaIds.Void) {
				var total = child.End - child.Position;
				if (TryFit (seek, total, out var fitted)) {
					fitted.CopyTo (body.AsSpan (child.Position));
					return true;
				}
			}

			offset = child.End;
		}

		return TryAbsorbVoid (body, seekHead, seek);
	}

	static bool TryAbsorbVoid (byte[] body, EbmlElement seekHead, byte[] seek)
	{
		var at = seekHead.End;
		if ((uint)at >= (uint)body.Length || !EbmlReader.TryRead (body, at, body.Length, out var voidEl))
			return false;
		if (voidEl.Id != MatroskaIds.Void || voidEl.UnknownSize || voidEl.Position != at)
			return false;

		var voidTotal = voidEl.End - voidEl.Position;
		var remain = voidTotal - seek.Length;
		if (remain < 0 || remain == 1)
			return false;
		if (remain > 0 && !TryMakeVoid (remain, out _))
			return false;

		if (!EbmlReader.TryReadVint (body, seekHead.Position, seekHead.DataOffset, true, 4, out _, out var idWidth, out _))
			return false;
		var sizeWidth = seekHead.HeaderLength - idWidth;
		var newDataLength = (ulong)seekHead.DataLength + (ulong)seek.Length;
		if (sizeWidth < 1 || newDataLength > MaxSize (sizeWidth))
			return false;

		EncodeSize (newDataLength, sizeWidth).CopyTo (body.AsSpan (seekHead.Position + idWidth));
		seek.CopyTo (body.AsSpan (at));
		if (remain > 0 && TryMakeVoid (remain, out var hole))
			hole.CopyTo (body.AsSpan (at + seek.Length));
		UpdateElementCrc (body, seekHead.Position, seekHead.End + seek.Length);
		return true;
	}

	static bool TryPatchCues (byte[] body, int start, int end, Plan plan, out string error)
	{
		error = "";
		if (!EbmlReader.TryRead (body, start, Math.Min (end, body.Length), out var cues) || cues.UnknownSize || cues.Position != start) {
			error = "Matroska Cues cannot be bounded";
			return false;
		}

		var changed = false;
		if (!TryPatchCueValues (body, cues.DataOffset, cues.End, plan, ref changed)) {
			error = "Matroska Cues cannot be updated";
			return false;
		}

		if (changed)
			UpdateElementCrc (body, cues.Position, cues.End);
		return true;
	}

	static bool TryPatchCueValues (byte[] body, int offset, int end, Plan plan, ref bool changed)
	{
		while (offset < end) {
			if (!EbmlReader.TryRead (body, offset, end, out var element) || element.UnknownSize)
				return false;
			if (element.Id == MatroskaIds.CueClusterPosition) {
				if (!TryAddDelta (body, element, plan, ref changed, emptyIsZero: false))
					return false;
			} else if (element.Id == MatroskaIds.CueCodecState) {
				if (!TryAddDeltaOrVoid (body, element, plan, ref changed))
					return false;
			} else if (element.Id is MatroskaIds.CuePoint or MatroskaIds.CueTrackPositions or MatroskaIds.Cues) {
				var nested = false;
				if (!TryPatchCueValues (body, element.DataOffset, element.End, plan, ref nested))
					return false;
				if (nested) {
					changed = true;
					if (element.Id != MatroskaIds.Cues)
						UpdateElementCrc (body, element.Position, element.End);
				}
			}

			offset = element.End;
		}

		return true;
	}

	/// <summary>
	/// A cluster at or after the insertion point moved by <see cref="Plan.Delta"/>.
	/// <see cref="MatroskaIds.Position"/> is that cluster's segment offset.
	/// A stored 0 is a live stream and stays 0 when the shift starts later.
	/// </summary>
	static bool TryPatchClusterPosition (byte[] body, int start, int end, int segmentOffset, Plan plan, out string error)
	{
		error = "";
		if (segmentOffset < plan.ShiftFrom)
			return true;

		if (!EbmlReader.TryRead (body, start, end, out var cluster) || cluster.Position != start || cluster.Id != MatroskaIds.Cluster) {
			error = "Matroska Cluster cannot be bounded";
			return false;
		}

		var changed = false;
		var offset = cluster.DataOffset;
		while (offset < end) {
			if (!EbmlReader.TryRead (body, offset, end, out var child) || child.UnknownSize) {
				error = "Matroska Cluster cannot be bounded";
				return false;
			}

			// PrevSize is the previous cluster's length, not a segment offset.
			// Position is optional, so a value that does not fit is removed instead of failing the save.
			if (child.Id == MatroskaIds.Position && !TryAddDeltaOrVoid (body, child, plan, ref changed)) {
				error = "Matroska Cluster Position cannot be updated";
				return false;
			}

			offset = child.End;
		}

		if (offset != end) {
			error = "Matroska Cluster cannot be bounded";
			return false;
		}

		if (changed)
			UpdatePieceCrc (body, cluster.DataOffset, end);
		return true;
	}

	/// <summary>
	/// Adds <see cref="Plan.Delta"/> when the stored segment offset is at or past the insertion point.
	/// An empty element is 0 (RFC 8794) and is left unchanged when <paramref name="emptyIsZero"/> is set.
	/// </summary>
	static bool TryAddDelta (byte[] body, EbmlElement element, Plan plan, ref bool changed, bool emptyIsZero)
	{
		if (element.DataLength == 0)
			return emptyIsZero;
		if (element.DataLength is < 1 or > 8)
			return false;
		var value = EbmlReader.ReadUInt (body.AsSpan (element.DataOffset, (int)element.DataLength));
		if (value < (ulong)plan.ShiftFrom)
			return true;

		var updated = value + (ulong)plan.Delta;
		if (updated < value || !TryWriteUnsigned (body, element.DataOffset, (int)element.DataLength, updated))
			return false;
		if (updated != value)
			changed = true;
		return true;
	}

	static bool TryAddDeltaOrVoid (byte[] body, EbmlElement element, Plan plan, ref bool changed)
	{
		if (TryAddDelta (body, element, plan, ref changed, emptyIsZero: true))
			return true;
		return TryVoidElement (body, element, ref changed);
	}

	/// <summary>
	/// Unknown-size parents have no element end, so <see cref="UpdateElementCrc"/> cannot see them.
	/// The CRC-32 covers the bytes after itself through the piece end.
	/// </summary>
	static void UpdatePieceCrc (byte[] body, int dataOffset, int pieceEnd)
	{
		if (!EbmlReader.TryRead (body, dataOffset, pieceEnd, out var first))
			return;
		if (first.Id != MatroskaIds.Crc32 || first.UnknownSize || first.DataLength != 4)
			return;
		WriteCrc (body, first.DataOffset, first.End, pieceEnd);
	}

	static bool TryParse (ReadOnlySpan<byte> data, out Layout? layout, out string error)
	{
		layout = null;
		error = "";
		if (!MatroskaFile.IsValidFormat (data)) {
			error = "Not a Matroska file: missing EBML header";
			return false;
		}

		if (!EbmlReader.TryRead (data, 0, data.Length, out var header) || header.Id != MatroskaIds.Ebml || header.UnknownSize) {
			error = "Not a Matroska file: EBML header is truncated";
			return false;
		}

		var docType = ReadDocType (data, header);
		if (docType is null) {
			error = "Not a Matroska file: DocType is missing";
			return false;
		}

		if (!docType.Equals ("matroska", StringComparison.OrdinalIgnoreCase)
			&& !docType.Equals ("webm", StringComparison.OrdinalIgnoreCase)) {
			error = "Not a Matroska file: DocType is " + docType;
			return false;
		}

		var offset = header.End;
		var found = false;
		EbmlElement segment = default;
		while (offset < data.Length) {
			if (!EbmlReader.TryRead (data, offset, data.Length, out var element))
				break;
			if (element.Id == MatroskaIds.Segment) {
				segment = element;
				found = true;
				break;
			}

			if (element.UnknownSize) {
				error = "Matroska element size is unknown before Segment";
				return false;
			}

			offset = element.End;
		}

		if (!found) {
			error = "Matroska file has no Segment";
			return false;
		}

		var end = MatroskaFile.PayloadEnd (data, segment);
		if (!TryPieces (data, segment.DataOffset, end, out var pieces, out error))
			return false;
		if (!EbmlReader.TryReadVint (data, segment.Position, segment.DataOffset, true, 4, out _, out var idWidth, out _)) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		var sizeWidth = segment.HeaderLength - idWidth;
		if (sizeWidth < 1 || idWidth < 1) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		layout = new Layout (segment.Position, idWidth, sizeWidth, segment.UnknownSize, segment.DataOffset, end, pieces);
		return true;
	}

	static bool TryPieces (ReadOnlySpan<byte> data, int start, int end, out List<Piece> pieces, out string error)
	{
		pieces = new List<Piece> ();
		error = "";
		var offset = start;
		var sawElement = false;
		while (offset < end) {
			if (!EbmlReader.TryRead (data, offset, end, out var element)) {
				if (sawElement) {
					error = "Matroska segment cannot be bounded";
					return false;
				}

				var gap = offset;
				while (offset < end && !EbmlReader.TryRead (data, offset, end, out _))
					offset++;
				if (offset > gap)
					pieces.Add (new Piece (0, gap, offset - gap, false));
				continue;
			}

			int pieceEnd;
			if (element.UnknownSize) {
				if (!TryBound (data, element.DataOffset, end, out pieceEnd)) {
					error = "Matroska element size is unknown and cannot be bounded";
					return false;
				}
			} else
				pieceEnd = element.End;

			if (pieceEnd < element.Position || pieceEnd > end) {
				error = "Matroska segment cannot be bounded";
				return false;
			}

			pieces.Add (new Piece (element.Id, element.Position, pieceEnd - element.Position, element.UnknownSize));
			sawElement = true;
			offset = pieceEnd;
		}

		if (OffsetOf (pieces, pieces.Count) != end - start) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		return true;
	}

	/// <summary>
	/// The unknown element ends at the next top-level sibling, or at <paramref name="parentEnd"/>
	/// when every byte up to that point parses. A failed read is not skipped.
	/// </summary>
	static bool TryBound (ReadOnlySpan<byte> data, int offset, int parentEnd, out int end)
	{
		end = offset;
		while (offset < parentEnd) {
			if (!EbmlReader.TryRead (data, offset, parentEnd, out var element))
				return false;
			if (MatroskaIds.IsSegmentChild (element.Id)) {
				end = element.Position;
				return true;
			}

			if (element.UnknownSize)
				return false;
			offset = element.End;
		}

		end = parentEnd;
		return true;
	}

	static string? ReadDocType (ReadOnlySpan<byte> data, EbmlElement header)
	{
		var offset = header.DataOffset;
		var end = header.End;
		while (offset < end) {
			if (!EbmlReader.TryRead (data, offset, end, out var element) || element.UnknownSize)
				break;
			if (element.Id == MatroskaIds.DocType)
				return EbmlReader.ReadString (data.Slice (element.DataOffset, (int)element.DataLength));
			offset = element.End;
		}

		return null;
	}

	static byte[] Assemble (ReadOnlySpan<byte> data, Layout layout, byte[] body, out string error)
	{
		error = "";
		var size = layout.UnknownSize
			? data.Slice (layout.SegmentPosition + layout.IdWidth, layout.SizeWidth).ToArray ()
			: EncodeFittedSize ((ulong)body.Length, layout.SizeWidth);
		if (size.Length == 0) {
			error = "Matroska segment size cannot be written";
			return [];
		}

		using var output = new BinaryDataBuilder (data.Length + body.Length + 8);
		output.Add (data.Slice (0, layout.SegmentPosition));
		output.Add (data.Slice (layout.SegmentPosition, layout.IdWidth));
		output.Add (size);
		output.Add (body);
		if (layout.SegmentEnd < data.Length)
			output.Add (data.Slice (layout.SegmentEnd));
		return output.ToArray ();
	}

	static bool TryFindFit (List<Piece> pieces, ReadOnlySpan<byte> tags, out int start, out int end, out byte[] fitted)
	{
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Tags)
				continue;
			if (TryConsume (pieces, i, tags, out end, out fitted)) {
				start = i;
				return true;
			}
		}

		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Void || pieces[i].UnknownSize)
				continue;
			if (TryConsume (pieces, i, tags, out end, out fitted)) {
				start = i;
				return true;
			}
		}

		start = -1;
		end = -1;
		fitted = [];
		return false;
	}

	static bool TryConsume (List<Piece> pieces, int start, ReadOnlySpan<byte> tags, out int end, out byte[] fitted)
	{
		var available = 0;
		end = start;
		fitted = [];
		for (var i = start; i < pieces.Count; i++) {
			if (i != start && (pieces[i].Id != MatroskaIds.Void || pieces[i].UnknownSize || pieces[start].UnknownSize))
				break;
			available += pieces[i].Length;
			end = i;
			if (available >= tags.Length && TryFit (tags, available, out fitted))
				return true;
		}

		return false;
	}

	static bool TryFit (ReadOnlySpan<byte> element, int region, out byte[] fitted)
	{
		fitted = [];
		if (element.Length > region || region < 0)
			return false;
		var slack = region - element.Length;
		if (slack == 0) {
			fitted = element.ToArray ();
			return true;
		}

		if (slack == 1)
			return TryWiden (element, out fitted) && fitted.Length == region;
		if (!TryMakeVoid (slack, out var hole))
			return false;
		fitted = new byte[region];
		element.CopyTo (fitted);
		hole.CopyTo (fitted.AsSpan (element.Length));
		return true;
	}

	static bool TryWiden (ReadOnlySpan<byte> element, out byte[] widened)
	{
		widened = [];
		if (!EbmlReader.TryRead (element, 0, element.Length, out var parsed) || parsed.UnknownSize)
			return false;
		if (!EbmlReader.TryReadVint (element, 0, element.Length, true, 4, out _, out var idWidth, out _))
			return false;
		var sizeWidth = parsed.HeaderLength - idWidth;
		if (sizeWidth < 1 || sizeWidth >= 8)
			return false;
		var payloadLength = (int)parsed.DataLength;
		var size = EncodeSize ((ulong)payloadLength, sizeWidth + 1);
		widened = new byte[idWidth + size.Length + payloadLength];
		element.Slice (0, idWidth).CopyTo (widened);
		size.CopyTo (widened.AsSpan (idWidth));
		element.Slice (parsed.DataOffset, payloadLength).CopyTo (widened.AsSpan (idWidth + size.Length));
		return widened.Length == element.Length + 1;
	}

	static bool TryMakeVoid (int total, out byte[] bytes)
	{
		bytes = [];
		if (total < 2)
			return false;
		for (var width = 1; width <= 8; width++) {
			var payload = total - 1 - width;
			if (payload < 0 || (ulong)payload > MaxSize (width))
				continue;
			var size = EncodeSize ((ulong)payload, width);
			bytes = new byte[total];
			bytes[0] = 0xEC;
			size.CopyTo (bytes.AsSpan (1));
			return true;
		}

		return false;
	}

	static byte[] MakeSeek (ulong elementId, ulong position)
	{
		var seekId = EbmlWriter.Master (MatroskaIds.SeekId, IdBytes (elementId));
		var seekPosition = EbmlWriter.Master (MatroskaIds.SeekPosition, UnsignedBytes (position));
		using var body = new BinaryDataBuilder (seekId.Length + seekPosition.Length);
		body.Add (seekId);
		body.Add (seekPosition);
		return EbmlWriter.Master (MatroskaIds.Seek, body.Span).ToArray ();
	}

	static byte[] UnsignedBytes (ulong value)
	{
		var width = 1;
		var probe = value;
		while (probe > 0xFF && width < 8) {
			width++;
			probe >>= 8;
		}

		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)value;
			value >>= 8;
		}

		return bytes;
	}

	static byte[] IdBytes (ulong id)
	{
		var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)id;
			id >>= 8;
		}

		return bytes;
	}

	static ulong ReadId (ReadOnlySpan<byte> payload)
	{
		ulong id = 0;
		for (var i = 0; i < payload.Length; i++)
			id = (id << 8) | payload[i];
		return id;
	}

	static bool TryWriteUnsigned (byte[] buffer, int offset, int width, ulong value)
	{
		if (width is < 1 or > 8)
			return false;
		if (width < 8) {
			var max = (1UL << (8 * width)) - 1;
			if (value > max)
				return false;
		}

		for (var i = width - 1; i >= 0; i--) {
			buffer[offset + i] = (byte)value;
			value >>= 8;
		}

		return true;
	}

	static byte[] EncodeFittedSize (ulong value, int width)
	{
		if (value <= MaxSize (width))
			return EncodeSize (value, width);
		var needed = 0;
		for (var candidate = width + 1; candidate <= 8; candidate++) {
			if (value <= MaxSize (candidate)) {
				needed = candidate;
				break;
			}
		}

		return needed == 0 ? [] : EncodeSize (value, needed);
	}

	static byte[] EncodeSize (ulong value, int width)
	{
		var raw = (1UL << (7 * width)) | value;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)raw;
			raw >>= 8;
		}

		return bytes;
	}

	static ulong MaxSize (int width) => (1UL << (7 * width)) - 2;

	static int OffsetOf (List<Piece> pieces, int index)
	{
		var offset = 0;
		for (var i = 0; i < index; i++)
			offset += pieces[i].Length;
		return offset;
	}

	static void UpdateElementCrc (byte[] body, int elementStart, int elementEnd)
	{
		if (!EbmlReader.TryRead (body, elementStart, elementEnd, out var parent) || parent.UnknownSize)
			return;
		if (!EbmlReader.TryRead (body, parent.DataOffset, parent.End, out var first))
			return;
		if (first.Id != MatroskaIds.Crc32 || first.UnknownSize || first.DataLength != 4)
			return;
		WriteCrc (body, first.DataOffset, first.End, parent.End);
	}

	static void UpdateLeadingCrc (byte[] body)
	{
		var offset = 0;
		while (offset < body.Length && !EbmlReader.TryRead (body, offset, body.Length, out _))
			offset++;
		if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
			return;
		if (element.Id != MatroskaIds.Crc32 || element.UnknownSize || element.DataLength != 4)
			return;
		WriteCrc (body, element.DataOffset, element.End, body.Length);
	}

	static void WriteCrc (byte[] body, int payloadOffset, int coveredStart, int coveredEnd)
	{
		var crc = Crc32 (body.AsSpan (coveredStart, coveredEnd - coveredStart));
		body[payloadOffset] = (byte)crc;
		body[payloadOffset + 1] = (byte)(crc >> 8);
		body[payloadOffset + 2] = (byte)(crc >> 16);
		body[payloadOffset + 3] = (byte)(crc >> 24);
	}

	static uint Crc32 (ReadOnlySpan<byte> data)
	{
		var crc = 0xFFFFFFFFu;
		for (var i = 0; i < data.Length; i++)
			crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
		return ~crc;
	}

	static readonly uint[] CrcTable = BuildCrcTable ();

	static uint[] BuildCrcTable ()
	{
		var table = new uint[256];
		for (uint i = 0; i < table.Length; i++) {
			var crc = i;
			for (var bit = 0; bit < 8; bit++)
				crc = (crc & 1) == 1 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
			table[i] = crc;
		}

		return table;
	}
}
