// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// Splices a rendered <c>Tags</c> element, and an <c>Attachments</c> element when the pictures
/// changed, into the first Segment.
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

		HashSet<ulong>? removed = null;
		if (tag.PicturesChanged) {
			if (!MatroskaAttachments.TryBuild (data, RangesOf (layout, MatroskaIds.Attachments), tag.Pictures, layout.IsWebM, out var attachments, out var changed, out removed, out error))
				return false;

			// Attachments goes in first, so the tags pass sees the final layout.
			// RFC 9559 section 6.7: an edited Attachments element SHOULD follow the last Cluster.
			if (changed) {
				if (!TryReplace (data, layout, MatroskaIds.Attachments, attachments, out var staged, out error))
					return false;
				if (!TryParse (staged, out layout, out error) || layout is null)
					return false;
				return TryReplace (staged, layout, MatroskaIds.Tags, tag.RenderWithout (removed).ToArray (), out rewritten, out error);
			}
		}

		return TryReplace (data, layout, MatroskaIds.Tags, tag.Render ().ToArray (), out rewritten, out error);
	}

	/// <summary>Returns true when the first SeekHead has a Seek for <paramref name="id"/>.</summary>
	static bool IsReferenced (ReadOnlySpan<byte> data, Layout layout, ulong id)
	{
		foreach (var piece in layout.Pieces) {
			if (piece.Id != MatroskaIds.SeekHead)
				continue;
			var end = piece.Offset + piece.Length;
			if (!EbmlReader.TryRead (data, piece.Offset, end, out var seekHead) || seekHead.UnknownSize)
				return false;
			var offset = seekHead.DataOffset;
			while (offset < seekHead.End) {
				if (!EbmlReader.TryRead (data, offset, seekHead.End, out var seek) || seek.UnknownSize)
					return false;
				if (seek.Id == MatroskaIds.Seek) {
					var inner = seek.DataOffset;
					while (inner < seek.End && EbmlReader.TryRead (data, inner, seek.End, out var child) && !child.UnknownSize) {
						if (child.Id == MatroskaIds.SeekId && ReadId (data.Slice (child.DataOffset, (int)child.DataLength)) == id)
							return true;
						inner = child.End;
					}
				}

				offset = seek.End;
			}

			return false;
		}

		return false;
	}

	static List<(int Offset, int Length)> RangesOf (Layout layout, ulong id)
	{
		var ranges = new List<(int, int)> ();
		foreach (var piece in layout.Pieces) {
			if (piece.Id == id)
				ranges.Add ((piece.Offset, piece.Length));
		}

		return ranges;
	}

	/// <summary>
	/// Writes <paramref name="element"/> as the only <paramref name="id"/> element of the Segment.
	/// An empty element voids every existing one.
	/// </summary>
	static bool TryReplace (ReadOnlySpan<byte> data, Layout layout, ulong id, byte[] element, out byte[] rewritten, out string error)
	{
		rewritten = [];
		var plan = PlanEdit (layout.Pieces, id, element, IsReferenced (data, layout, id));
		if (!TryBuild (data, layout, plan, out var body, out var seekHeads, out var cues, out var elementOffset, out error))
			return false;
		if (element.Length > 0 && elementOffset != plan.ElementOffset) {
			error = plan.PlaceError;
			return false;
		}

		if (!TryPatch (body, seekHeads, cues, plan, out var referenced, out error))
			return false;
		if (!IsReachable (layout.Pieces, plan, seekHeads.Count, referenced, out error))
			return false;

		var originalLength = layout.SegmentEnd - layout.DataOffset;
		if (originalLength != body.Length || !data.Slice (layout.DataOffset, originalLength).SequenceEqual (body))
			UpdateLeadingCrc (body);

		rewritten = Assemble (data, layout, body, out error);
		return rewritten.Length > 0 && error.Length == 0;
	}

	sealed class Layout
	{
		public Layout (int segmentPosition, int idWidth, int sizeWidth, bool unknownSize, int dataOffset, int segmentEnd, List<Piece> pieces, bool isWebM)
		{
			SegmentPosition = segmentPosition;
			IdWidth = idWidth;
			SizeWidth = sizeWidth;
			UnknownSize = unknownSize;
			DataOffset = dataOffset;
			SegmentEnd = segmentEnd;
			Pieces = pieces;
			IsWebM = isWebM;
		}

		public bool IsWebM { get; }

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
		public Plan (ulong id, byte[] element)
		{
			Id = id;
			Element = element;
		}

		/// <summary>Gets the element being written: <c>Tags</c> or <c>Attachments</c>.</summary>
		public ulong Id { get; }

		public byte[] Element { get; }

		public string PlaceError => Id == MatroskaIds.Attachments
			? "Matroska attachments could not be placed"
			: "Matroska tags could not be placed";

		public int ReplaceStart { get; set; } = -1;

		public int ReplaceEnd { get; set; } = -1;

		public byte[]? Replacement { get; set; }

		public bool VoidExisting { get; set; }

		public bool Append { get; set; }

		public int InsertBefore { get; set; } = -1;

		public int ElementOffset { get; set; } = -1;

		public int ShiftFrom { get; set; } = int.MaxValue;

		public int Delta { get; set; }

		public bool MustRetarget { get; set; }

		public bool ShouldAdd { get; set; }
	}

	static Plan PlanEdit (List<Piece> pieces, ulong id, byte[] element, bool referenced)
	{
		var plan = new Plan (id, element);
		var indexes = new List<int> ();
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id == id)
				indexes.Add (i);
		}

		if (element.Length == 0) {
			plan.VoidExisting = indexes.Count > 0;
			plan.MustRetarget = indexes.Count > 0;
			return plan;
		}

		plan.ShouldAdd = indexes.Count == 0;
		if (TryFindFit (pieces, id, element, !referenced, out var start, out var end, out var fitted)) {
			plan.ReplaceStart = start;
			plan.ReplaceEnd = end;
			plan.Replacement = fitted;
			plan.ElementOffset = OffsetOf (pieces, start);
			plan.VoidExisting = true;
			plan.MustRetarget = Moved (pieces, indexes, plan.ElementOffset);
			return plan;
		}

		plan.VoidExisting = true;
		var last = pieces.Count - 1;
		if (last >= 0 && pieces[last].Id == id) {
			plan.ReplaceStart = last;
			plan.ReplaceEnd = last;
			plan.Replacement = element;
			plan.ElementOffset = OffsetOf (pieces, last);
			plan.MustRetarget = Moved (pieces, indexes, plan.ElementOffset);
			return plan;
		}

		if (last >= 0 && pieces[last].UnknownSize) {
			plan.InsertBefore = last;
			plan.ElementOffset = OffsetOf (pieces, last);
			plan.ShiftFrom = plan.ElementOffset;
			plan.Delta = element.Length;
			plan.MustRetarget = true;
			return plan;
		}

		plan.Append = true;
		plan.ElementOffset = OffsetOf (pieces, pieces.Count);
		plan.MustRetarget = indexes.Count > 0;
		return plan;
	}

	static bool Moved (List<Piece> pieces, List<int> indexes, int elementOffset)
	{
		foreach (var index in indexes) {
			if (OffsetOf (pieces, index) != elementOffset)
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
		out int elementOffset,
		out string error)
	{
		body = [];
		seekHeads = new List<(int, int)> ();
		cues = new List<(int, int)> ();
		elementOffset = -1;
		error = "";
		var pieces = layout.Pieces;
		if (plan.Replacement is not null && plan.ReplaceEnd != pieces.Count - 1) {
			var region = 0;
			for (var index = plan.ReplaceStart; index <= plan.ReplaceEnd; index++)
				region += pieces[index].Length;
			if (plan.Replacement.Length != region) {
				error = plan.PlaceError;
				return false;
			}
		}

		using var builder = new BinaryDataBuilder (Math.Max (16, data.Length));
		var i = 0;
		while (i < pieces.Count) {
			if (i == plan.ReplaceStart && plan.Replacement is not null) {
				elementOffset = builder.Length;
				builder.Add (plan.Replacement);
				i = plan.ReplaceEnd + 1;
				continue;
			}

			var piece = pieces[i];
			if (plan.VoidExisting && piece.Id == plan.Id) {
				if (!TryMakeVoid (piece.Length, out var hole)) {
					error = plan.PlaceError;
					return false;
				}

				builder.Add (hole);
				i++;
				continue;
			}

			if (i == plan.InsertBefore) {
				elementOffset = builder.Length;
				builder.Add (plan.Element);
			}

			var at = builder.Length;
			builder.Add (data.Slice (piece.Offset, piece.Length));
			if (piece.Id == MatroskaIds.SeekHead)
				seekHeads.Add ((at, at + piece.Length));
			else if (piece.Id == MatroskaIds.Cues)
				cues.Add ((at, at + piece.Length));
			i++;
		}

		if (plan.Append) {
			elementOffset = builder.Length;
			builder.Add (plan.Element);
		}

		body = builder.ToArray ();
		return true;
	}

	static bool TryPatch (
		byte[] body,
		List<(int Start, int End)> seekHeads,
		List<(int Start, int End)> cues,
		Plan plan,
		out bool referenced,
		out string error)
	{
		error = "";
		referenced = false;
		var strict = plan.MustRetarget || plan.Delta != 0;
		if (strict || plan.ShouldAdd) {
			// A new entry goes in the first SeekHead. A second one holds only Clusters (RFC 9559 section 6.3).
			for (var i = 0; i < seekHeads.Count; i++) {
				if (!TryPatchSeekHead (body, seekHeads[i].Start, seekHeads[i].End, plan, strict, i == 0, ref referenced, out error))
					return false;
			}
		} else
			referenced = true;

		if (plan.Delta == 0)
			return true;

		foreach (var cue in cues) {
			if (!TryPatchCues (body, cue.Start, cue.End, plan, out error))
				return false;
		}

		return true;
	}

	/// <summary>
	/// A new or moved <c>Attachments</c> element must be found by a player that reads the SeekHead
	/// and the elements before the first Cluster. The SeekHead MUST reference every Top-Level Element
	/// (RFC 9559 section 6.3); a live layout puts every other element before the Clusters
	/// (section 25.3.4). A cover that no player would find fails the save instead.
	/// Tags keep the older best-effort placement.
	/// </summary>
	static bool IsReachable (List<Piece> pieces, Plan plan, int seekHeadCount, bool referenced, out string error)
	{
		error = "";
		if (plan.Id != MatroskaIds.Attachments || plan.ElementOffset < 0 || referenced)
			return true;
		if (seekHeadCount > 0) {
			error = "Matroska SeekHead has no room to reference the attachments";
			return false;
		}

		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Cluster)
				continue;
			var clusterAt = OffsetOf (pieces, i);
			if (clusterAt >= plan.ShiftFrom)
				clusterAt += plan.Delta;
			if (plan.ElementOffset < clusterAt)
				return true;
			error = "Matroska file has no SeekHead, so attachments after a Cluster would not be found";
			return false;
		}

		return true;
	}

	static bool TryPatchSeekHead (byte[] body, int start, int end, Plan plan, bool strict, bool mayAdd, ref bool referenced, out string error)
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
		foreach (var child in children) {
			if (child.Id != MatroskaIds.Seek)
				continue;
			if (!TryPatchSeek (body, child, plan, ref changed, ref referenced)) {
				error = "Matroska SeekHead cannot be updated";
				return false;
			}
		}

		if (mayAdd && plan.ElementOffset >= 0 && !referenced && TryAddSeek (body, seekHead, plan.Id, (ulong)plan.ElementOffset)) {
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
			return strict ? TryDropSeek (body, seek, ref changed) : true;

		var isTarget = id == plan.Id;
		ulong desired;
		if (isTarget) {
			if (plan.ElementOffset < 0)
				return TryDropSeek (body, seek, ref changed);
			desired = (ulong)plan.ElementOffset;
		} else if (plan.Delta != 0 && position >= (ulong)plan.ShiftFrom) {
			desired = position + (ulong)plan.Delta;
			if (desired < position)
				return false;
		} else
			return true;

		if (desired == position) {
			if (isTarget)
				referenced = true;
			return true;
		}

		if (positionElement.DataLength is > 0 and <= 8
			&& TryWriteUnsigned (body, positionElement.DataOffset, (int)positionElement.DataLength, desired)) {
			changed = true;
			UpdateElementCrc (body, seek.Position, seek.End);
			if (isTarget)
				referenced = true;
			return true;
		}

		var rebuilt = MakeSeek (id, desired);
		var total = seek.End - seek.Position;
		if (TryFit (rebuilt, total, out var fitted)) {
			fitted.CopyTo (body.AsSpan (seek.Position));
			changed = true;
			if (isTarget)
				referenced = true;
			return true;
		}

		return TryDropSeek (body, seek, ref changed);
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

	static bool TryDropSeek (byte[] body, EbmlElement seek, ref bool changed)
	{
		var total = seek.End - seek.Position;
		if (!TryMakeVoid (total, out var hole))
			return false;
		hole.CopyTo (body.AsSpan (seek.Position));
		changed = true;
		return true;
	}

	static bool TryAddSeek (byte[] body, EbmlElement seekHead, ulong id, ulong elementOffset)
	{
		var seek = MakeSeek (id, elementOffset);
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

	/// <summary>
	/// Grows the SeekHead into the Void right after it (RFC 9559 section 25.2).
	/// A size field that is too narrow is widened by the bytes the Void gives up.
	/// </summary>
	static bool TryAbsorbVoid (byte[] body, EbmlElement seekHead, byte[] seek)
	{
		var at = seekHead.End;
		if ((uint)at >= (uint)body.Length || !EbmlReader.TryRead (body, at, body.Length, out var voidEl))
			return false;
		if (voidEl.Id != MatroskaIds.Void || voidEl.UnknownSize || voidEl.Position != at)
			return false;

		if (!EbmlReader.TryReadVint (body, seekHead.Position, seekHead.DataOffset, true, 4, out _, out var idWidth, out _))
			return false;
		var sizeWidth = seekHead.HeaderLength - idWidth;
		var newDataLength = (ulong)seekHead.DataLength + (ulong)seek.Length;
		if (sizeWidth < 1)
			return false;
		var newWidth = sizeWidth;
		while (newWidth < 8 && newDataLength > MaxSize (newWidth))
			newWidth++;
		if (newDataLength > MaxSize (newWidth))
			return false;

		var extra = newWidth - sizeWidth;
		var voidTotal = voidEl.End - voidEl.Position;
		var remain = voidTotal - seek.Length - extra;
		if (remain < 0 || remain == 1)
			return false;
		if (remain > 0 && !TryMakeVoid (remain, out _))
			return false;

		// Buffer.BlockCopy is safe for an overlapping copy.
		if (extra > 0)
			Buffer.BlockCopy (body, seekHead.DataOffset, body, seekHead.DataOffset + extra, at - seekHead.DataOffset);
		EncodeSize (newDataLength, newWidth).CopyTo (body.AsSpan (seekHead.Position + idWidth));
		seek.CopyTo (body.AsSpan (at + extra));
		if (remain > 0 && TryMakeVoid (remain, out var hole))
			hole.CopyTo (body.AsSpan (at + extra + seek.Length));
		UpdateElementCrc (body, seekHead.Position, at + extra + seek.Length);
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
				if (element.DataLength is < 1 or > 8)
					return false;
				var value = EbmlReader.ReadUInt (body.AsSpan (element.DataOffset, (int)element.DataLength));
				if (value >= (ulong)plan.ShiftFrom) {
					var updated = value + (ulong)plan.Delta;
					if (updated < value || !TryWriteUnsigned (body, element.DataOffset, (int)element.DataLength, updated))
						return false;
					if (updated != value)
						changed = true;
				}
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

		var webm = docType.Equals ("webm", StringComparison.OrdinalIgnoreCase);
		layout = new Layout (segment.Position, idWidth, sizeWidth, segment.UnknownSize, segment.DataOffset, end, pieces, webm);
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

	/// <summary>
	/// The element fits where the old one was, or in a Void.
	/// An element the SeekHead does not reference yet skips the Void right after a SeekHead:
	/// the SeekHead grows into it to add the entry (RFC 9559 sections 6.3 and 25.2).
	/// </summary>
	static bool TryFindFit (List<Piece> pieces, ulong id, ReadOnlySpan<byte> element, bool needsEntry, out int start, out int end, out byte[] fitted)
	{
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != id)
				continue;
			if (TryConsume (pieces, i, element, out end, out fitted)) {
				start = i;
				return true;
			}
		}

		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Void || pieces[i].UnknownSize)
				continue;
			if (needsEntry && i > 0 && pieces[i - 1].Id == MatroskaIds.SeekHead)
				continue;
			if (TryConsume (pieces, i, element, out end, out fitted)) {
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

	internal static uint Crc32 (ReadOnlySpan<byte> data)
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
