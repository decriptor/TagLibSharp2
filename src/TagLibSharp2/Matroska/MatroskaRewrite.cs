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
static partial class MatroskaRewrite
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
		if (!TryApply (data, layout, plan, out var body, out var seekHeadCount, out var referenced, out error))
			return false;

		// RFC 9559 section 6.1: the SeekHead MUST list an added Top-Level Element. When the plan above
		// cannot, the element goes right after the first SeekHead, which is written again with one more
		// Seek. Without a SeekHead it goes before the first Cluster (section 25.3.4). Everything after
		// moves once. Only one body is kept: the first is dropped before the move is built, and built
		// again only when the move cannot be planned.
		var moveError = "";
		if (NeedsMove (layout, plan, seekHeadCount, referenced)) {
			body = [];
			if (TryPlanMove (data, layout, id, element, out var moved)
				&& TryApply (data, layout, moved, out var movedBody, out var movedHeads, out var movedReferenced, out moveError)) {
				plan = moved;
				body = movedBody;
				seekHeadCount = movedHeads;
				referenced = movedReferenced;
			} else if (!TryApply (data, layout, plan, out body, out seekHeadCount, out referenced, out error))
				return false;
		}

		if (!IsReachable (layout, plan, seekHeadCount, referenced, out error)) {
			// The first plan left the element unlisted because the move failed; say why it did.
			if (moveError.Length > 0)
				error = moveError;
			return false;
		}

		return TryFinish (data, layout, body, out rewritten, out error);
	}

	/// <summary>
	/// Replaces the first Top-Level Element with the ID of <paramref name="replacement"/>. A shorter
	/// replacement is padded with a Void. A longer one grows in place, and everything after it moves
	/// once: SeekHead and Cues positions, Cluster <c>Position</c>, and their CRC-32 values follow.
	/// A position that no longer fits its field is written again wider, so no Seek is lost
	/// (RFC 9559 section 6.3).
	/// </summary>
	internal static bool TryGrow (ReadOnlySpan<byte> data, byte[] replacement, out byte[] rewritten, out string error)
	{
		rewritten = [];
		if (!TryParse (data, out var layout, out error) || layout is null)
			return false;
		if (!EbmlReader.TryRead (replacement, 0, replacement.Length, out var parsed) || parsed.UnknownSize || parsed.End != replacement.Length) {
			error = "Matroska replacement is not one element";
			return false;
		}

		// A CRC-32 the caller left stale is recomputed, so a shorter replacement is valid too.
		replacement = replacement.ToArray ();
		UpdateElementCrc (replacement, 0, replacement.Length);
		var pieces = layout.Pieces;
		var index = pieces.FindIndex (piece => piece.Id == parsed.Id && !piece.UnknownSize);
		if (index < 0) {
			error = "Matroska element to replace was not found";
			return false;
		}

		// The plan writes no element of its own (ID 0), so every Seek, the grown element's included,
		// follows Map like any other position.
		var plan = new Plan (0, []);
		if (replacement.Length <= pieces[index].Length) {
			if (!TryFit (replacement, pieces[index].Length, out var fitted)) {
				error = "Matroska element could not be placed";
				return false;
			}

			plan.ReplaceStart = index;
			plan.ReplaceEnd = index;
			plan.Replacement = fitted;
		} else {
			plan.GrowIndex = index;
			plan.Grown = replacement;
			plan.ShiftFrom = layout.Starts[index + 1];
			plan.Delta = replacement.Length - pieces[index].Length;
			plan.MustRetarget = true;
		}

		if (!TryApply (data, layout, plan, out var body, out _, out _, out error))
			return false;
		return TryFinish (data, layout, body, out rewritten, out error);
	}

	/// <summary>
	/// Builds the Segment body for <paramref name="plan"/> and patches every position in it. A SeekHead
	/// or Cues whose moved values do not fit in place is added to <see cref="Plan.RenderIndexes"/> and
	/// the body is built again; each round only adds pieces, so it ends.
	/// </summary>
	static bool TryApply (
		ReadOnlySpan<byte> data,
		Layout layout,
		Plan plan,
		out byte[] body,
		out int seekHeadCount,
		out bool referenced,
		out string error)
	{
		body = [];
		seekHeadCount = 0;
		referenced = false;
		// The first attempt patches in place. When any value does not fit, every known-size SeekHead and
		// Cues is rendered, so a widening that pushes another value over a width boundary is settled
		// in TrySettle and the body is built at most twice (once more if a piece cannot be rendered).
		for (var attempt = 0; attempt < 3; attempt++) {
			if (!TrySettle (data, layout, plan, out error))
				return false;
			if (!TryBuild (data, layout, plan, out body, out var seekHeads, out var cues, out var elementOffset, out error))
				return false;
			if (plan.Element.Length > 0 && elementOffset != plan.NewElementOffset) {
				error = plan.PlaceError;
				return false;
			}

			var widen = new List<int> ();
			if (!TryPatch (body, seekHeads, cues, plan, widen, out referenced, out error))
				return false;
			if (widen.Count == 0) {
				seekHeadCount = seekHeads.Count;
				// The grown piece's own CRC-32 covers bytes the patches above may have changed.
				if (plan.Grown is not null && plan.GrownAt >= 0)
					UpdateElementCrc (body, plan.GrownAt, plan.GrownAt + plan.Grown.Length);
				return TryPatchClusterPositions (body, layout, plan, out error);
			}

			body = [];
			foreach (var index in widen)
				plan.RenderIndexes.Add (index);
			for (var index = 0; index < layout.Pieces.Count; index++) {
				var piece = layout.Pieces[index];
				if ((piece.Id == MatroskaIds.SeekHead || piece.Id == MatroskaIds.Cues) && !piece.UnknownSize)
					plan.RenderIndexes.Add (index);
			}
		}

		error = "Matroska positions could not be updated";
		return false;
	}

	/// <summary>
	/// Writes every piece in <see cref="Plan.RenderIndexes"/> until no length changes. A rendered piece
	/// never shrinks below its last length (a Void takes the slack), and positions only grow, so the
	/// lengths only grow. A file can make each widening cause the next; after
	/// <see cref="WideAfterRounds"/> rounds every moved value is written at eight octets, which no shift
	/// can outgrow. A value that first moves in a later round still grows once, so a crafted file can
	/// keep the lengths growing: the rounds are capped at <see cref="WideAfterRounds"/> + 3, and a file
	/// that reaches the cap fails here (a move then falls back to its first placement). Each round
	/// costs one pass over the rendered pieces, so the work is linear in their size.
	/// </summary>
	static bool TrySettle (ReadOnlySpan<byte> data, Layout layout, Plan plan, out string error)
	{
		error = "";
		if (plan.RenderIndexes.Count == 0)
			return true;

		var pieces = layout.Pieces;
		var firstHead = pieces.FindIndex (piece => piece.Id == MatroskaIds.SeekHead);
		var lengths = new Dictionary<int, int> ();
		foreach (var index in plan.RenderIndexes)
			lengths[index] = plan.Rendered.TryGetValue (index, out var last) ? last.Length : pieces[index].Length;

		for (var round = 0; round < WideAfterRounds + 3; round++) {
			if (round == WideAfterRounds)
				plan.WideValues = true;
			var growth = new List<(int, int)> (plan.RenderIndexes.Count);
			foreach (var index in plan.RenderIndexes)
				growth.Add ((layout.Starts[index + 1], lengths[index] - pieces[index].Length));
			plan.SetGrowth (growth);

			var grew = false;
			plan.RenderedTarget = false;
			foreach (var index in plan.RenderIndexes) {
				var piece = pieces[index];
				var source = data.Slice (piece.Offset, piece.Length);
				byte[]? rendered;
				if (piece.Id == MatroskaIds.Cues)
					rendered = RenderCues (source, plan);
				else {
					var isHead = index == plan.HeadIndex;
					if (isHead) {
						// The element goes right after this SeekHead, at its length so far.
						plan.ElementOffset = (int)plan.Map ((ulong)layout.Starts[index]) + lengths[index];
					}

					rendered = RenderSeekHead (source, plan, isHead || (index == firstHead && plan.Element.Length > 0));
					if (rendered is not null && index == firstHead && plan.Id != 0 && plan.NewElementOffset >= 0)
						plan.RenderedTarget = true;
				}

				if (rendered is null) {
					error = piece.Id == MatroskaIds.Cues ? "Matroska Cues cannot be updated" : "Matroska SeekHead cannot be updated";
					return false;
				}

				if (rendered.Length < lengths[index] && !TryFit (rendered, lengths[index], out rendered)) {
					error = "Matroska positions could not be updated";
					return false;
				}

				if (rendered.Length > lengths[index]) {
					lengths[index] = rendered.Length;
					grew = true;
				}

				plan.Rendered[index] = rendered;
			}

			if (!grew)
				return true;
		}

		error = "Matroska positions could not be updated";
		return false;
	}

	/// <summary>Rounds of minimal widths before every moved value is written at eight octets.</summary>
	const int WideAfterRounds = 4;

	static bool TryFinish (ReadOnlySpan<byte> data, Layout layout, byte[] body, out byte[] rewritten, out string error)
	{
		rewritten = [];
		error = "";
		var originalLength = layout.SegmentEnd - layout.DataOffset;
		if (originalLength != body.Length || !data.Slice (layout.DataOffset, originalLength).SequenceEqual (body))
			UpdateLeadingCrc (body);

		rewritten = Assemble (data, layout, body, out error);
		return rewritten.Length > 0 && error.Length == 0;
	}

	/// <summary>
	/// True when the element was placed but a reader could not find it: no Seek lists it, or there is
	/// no SeekHead and it sits after the first Cluster.
	/// </summary>
	static bool NeedsMove (Layout layout, Plan plan, int seekHeadCount, bool referenced)
	{
		if (plan.Element.Length == 0 || plan.ElementOffset < 0 || referenced)
			return false;
		if (seekHeadCount > 0)
			return true;
		var first = layout.Pieces.FindIndex (piece => piece.Id == MatroskaIds.Cluster);
		return first >= 0 && plan.NewElementOffset > (int)plan.Map ((ulong)layout.Starts[first]);
	}

	/// <summary>
	/// Plans <paramref name="element"/> right after the first SeekHead, or before the first Cluster
	/// when there is no SeekHead. The first SeekHead is written again with every position already
	/// moved, each at the width it needs, plus a Seek for <paramref name="id"/>. Every existing
	/// <paramref name="id"/> element is voided in place.
	/// </summary>
	static bool TryPlanMove (ReadOnlySpan<byte> data, Layout layout, ulong id, byte[] element, out Plan plan)
	{
		var pieces = layout.Pieces;
		plan = new Plan (id, element) { VoidExisting = true, MustRetarget = true };
		var index = pieces.FindIndex (piece => piece.Id == MatroskaIds.SeekHead);
		if (index < 0) {
			var cluster = pieces.FindIndex (piece => piece.Id == MatroskaIds.Cluster);
			if (cluster < 0)
				return false;
			plan.InsertBefore = cluster;
			plan.ElementOffset = layout.Starts[cluster];
			plan.ShiftFrom = plan.ElementOffset;
			plan.Delta = element.Length;
			return true;
		}

		var piece = pieces[index];
		if (piece.UnknownSize || !TryReadSeekHead (data.Slice (piece.Offset, piece.Length), out _, out _, out _, out _))
			return false;

		// The SeekHead is rendered like any other piece; its growth and the element both start at its old end.
		plan.HeadIndex = index;
		plan.RenderIndexes.Add (index);
		plan.ShiftFrom = layout.Starts[index + 1];
		plan.Delta = element.Length;
		plan.ElementOffset = layout.Starts[index + 1];
		if (index + 1 < pieces.Count)
			plan.InsertBefore = index + 1;
		else
			plan.Append = true;
		return true;
	}

	/// <summary>Splits a SeekHead into its ID bytes, whether it has a CRC-32, other children, and its Seeks.</summary>
	static bool TryReadSeekHead (ReadOnlySpan<byte> head, out byte[] idBytes, out bool crc, out List<byte[]> kept, out List<(ulong Id, ulong Position)> seeks)
	{
		idBytes = [];
		crc = false;
		kept = new List<byte[]> ();
		seeks = new List<(ulong, ulong)> ();
		var copy = head.ToArray ();
		if (!EbmlReader.TryRead (copy, 0, copy.Length, out var seekHead) || seekHead.UnknownSize || seekHead.End != copy.Length)
			return false;
		if (!EbmlReader.TryReadVint (copy, 0, copy.Length, true, 4, out _, out var idWidth, out _))
			return false;
		idBytes = copy.AsSpan (0, idWidth).ToArray ();
		var offset = seekHead.DataOffset;
		while (offset < seekHead.End) {
			if (!EbmlReader.TryRead (copy, offset, seekHead.End, out var child) || child.UnknownSize)
				return false;
			if (child.Id == MatroskaIds.Seek) {
				if (TryReadSeek (copy, child, out var seekId, out var position, out _))
					seeks.Add ((seekId, position));
			} else if (child.Id == MatroskaIds.Crc32)
				crc = true;
			else if (child.Id != MatroskaIds.Void)
				kept.Add (copy.AsSpan (child.Position, child.End - child.Position).ToArray ());
			offset = child.End;
		}

		return true;
	}

	/// <summary>
	/// Writes a SeekHead whose Seeks hold their moved positions. A Seek for <see cref="Plan.Id"/> names
	/// <see cref="Plan.NewElementOffset"/>, or is left out when the element was removed. With
	/// <paramref name="addTarget"/>, a missing Seek for the element is added.
	/// </summary>
	static byte[]? RenderSeekHead (ReadOnlySpan<byte> source, Plan plan, bool addTarget)
	{
		if (!TryReadSeekHead (source, out var idBytes, out var crc, out var kept, out var seeks))
			return null;

		using var body = new BinaryDataBuilder ();
		// RFC 8794 section 11.3.1: a CRC-32 is the first child. It is filled in last.
		if (crc)
			body.Add (EbmlWriter.Master (MatroskaIds.Crc32, new byte[4]));
		var target = plan.NewElementOffset;
		var listed = false;
		foreach (var (seekId, position) in seeks) {
			if (plan.Id == 0 || seekId != plan.Id) {
				// A position that would wrap names no element; refuse it as TryPatchSeek does.
				var moved = plan.Map (position);
				if (moved < position)
					return null;
				body.Add (MakeSeek (seekId, moved, plan.WideValues));
				continue;
			}

			if (!listed && target >= 0)
				body.Add (MakeSeek (seekId, (ulong)target, plan.WideValues));
			listed = true;
		}

		if (!listed && addTarget && target >= 0 && plan.Id != 0)
			body.Add (MakeSeek (plan.Id, (ulong)target, plan.WideValues));
		foreach (var child in kept)
			body.Add (child);

		var payload = body.ToArray ();
		if (crc)
			WriteCrc (payload, 2, 6, payload.Length);
		using var head = new BinaryDataBuilder (payload.Length + 12);
		head.Add (idBytes);
		head.Add (EncodeFittedSize ((ulong)payload.Length, 1));
		head.Add (payload);
		return head.ToArray ();
	}

	/// <summary>
	/// Writes a Cues element with its Segment Positions moved: CueClusterPosition, CueCodecState,
	/// and the historic CueRefCluster and CueRefCodecState (RFC 9559 sections 5.1.5.1.2.2,
	/// 5.1.5.1.2.6, A.37, A.39). A value keeps its width when it fits and widens when it does not.
	/// Every master's CRC-32 is recomputed. Other children are copied as they are.
	/// </summary>
	static byte[]? RenderCues (ReadOnlySpan<byte> source, Plan plan)
	{
		// Every render starts from the source piece, which is exactly one Cues element.
		if (!EbmlReader.TryRead (source, 0, source.Length, out var cues) || cues.UnknownSize || cues.Id != MatroskaIds.Cues || cues.End != source.Length)
			return null;
		return RenderCueMaster (source, cues, plan);
	}

	/// <summary>
	/// Writes one Cues-path master. Only the child that <see cref="CueChildMaster"/> names is walked,
	/// so the recursion is at most four levels deep whatever the file nests.
	/// </summary>
	static byte[]? RenderCueMaster (ReadOnlySpan<byte> source, EbmlElement master, Plan plan)
	{
		var childMaster = CueChildMaster (master.Id);
		using var payload = new BinaryDataBuilder ((int)master.DataLength + 16);
		var crcHeader = -1;
		var crcEnd = 0;
		var offset = master.DataOffset;
		while (offset < master.End) {
			if (!EbmlReader.TryRead (source, offset, master.End, out var child) || child.UnknownSize)
				return null;
			var bytes = source.Slice (child.Position, child.End - child.Position);
			if (child.Id == MatroskaIds.Crc32 && offset == master.DataOffset && child.DataLength == 4) {
				crcHeader = child.HeaderLength;
				crcEnd = child.End - child.Position;
				payload.Add (bytes);
			} else if (IsCuePosition (master.Id, child.Id)) {
				if (!TryMoveCueValue (source, child, plan, out var value, out var keep))
					return null;
				if (keep)
					payload.Add (bytes);
				else {
					var width = plan.WideValues ? 8 : Math.Max ((int)child.DataLength, UnsignedBytes (value).Length);
					var field = new byte[width];
					TryWriteUnsigned (field, 0, width, value);
					payload.Add (source.Slice (child.Position, child.HeaderLength - SizeWidth (source, child)));
					payload.Add (EncodeFittedSize ((ulong)width, SizeWidth (source, child)));
					payload.Add (field);
				}
			} else if (child.Id == childMaster) {
				var inner = RenderCueMaster (source, child, plan);
				if (inner is null)
					return null;
				payload.Add (inner);
			} else
				payload.Add (bytes);
			offset = child.End;
		}

		var data = payload.ToArray ();
		if (crcHeader >= 0)
			WriteCrc (data, crcHeader, crcEnd, data.Length);
		var sizeWidth = SizeWidth (source, master);
		var size = EncodeFittedSize ((ulong)data.Length, sizeWidth);
		if (size.Length == 0)
			return null;
		var idLength = master.HeaderLength - sizeWidth;
		var element = new byte[idLength + size.Length + data.Length];
		source.Slice (master.Position, idLength).CopyTo (element);
		size.CopyTo (element.AsSpan (idLength));
		data.CopyTo (element.AsSpan (idLength + size.Length));
		return element;
	}

	static int SizeWidth (ReadOnlySpan<byte> source, EbmlElement element) =>
		EbmlReader.TryReadVint (source, element.Position, element.DataOffset, true, 4, out _, out var idWidth, out _)
			? element.HeaderLength - idWidth
			: 1;

	/// <summary>
	/// Gets the one master a Cues-path master holds, or 0. RFC 9559 section 5.1.5 fixes the paths:
	/// <c>\Segment\Cues\CuePoint</c>, <c>...\CuePoint\CueTrackPositions</c> and
	/// <c>...\CueTrackPositions\CueReference</c>. Any other nesting is not on a path, so it is copied as it is.
	/// </summary>
	static ulong CueChildMaster (ulong parent) => parent switch {
		MatroskaIds.Cues => MatroskaIds.CuePoint,
		MatroskaIds.CuePoint => MatroskaIds.CueTrackPositions,
		MatroskaIds.CueTrackPositions => MatroskaIds.CueReference,
		_ => 0
	};

	/// <summary>
	/// Gets a value indicating whether <paramref name="id"/> is a Segment Position in
	/// <paramref name="parent"/>: CueClusterPosition and CueCodecState in CueTrackPositions
	/// (RFC 9559 sections 5.1.5.1.2.2, 5.1.5.1.2.6), CueRefCluster and CueRefCodecState in
	/// CueReference (Appendix A.37, A.39).
	/// </summary>
	static bool IsCuePosition (ulong parent, ulong id) => parent switch {
		MatroskaIds.CueTrackPositions => id is MatroskaIds.CueClusterPosition or MatroskaIds.CueCodecState,
		MatroskaIds.CueReference => id is MatroskaIds.CueRefCluster or MatroskaIds.CueRefCodecState,
		_ => false
	};

	/// <summary>
	/// Reads a Cues position and maps it. An empty or zero CueCodecState or CueRefCodecState names the
	/// TrackEntry, not a position (RFC 9559 section 5.1.5.1.2.6), so <paramref name="keep"/> is set.
	/// </summary>
	static bool TryMoveCueValue (ReadOnlySpan<byte> source, EbmlElement element, Plan plan, out ulong value, out bool keep)
	{
		value = 0;
		keep = true;
		var codecState = element.Id is MatroskaIds.CueCodecState or MatroskaIds.CueRefCodecState;
		if (element.DataLength > 8)
			return false;
		// CueClusterPosition and the historic CueRefCluster and CueRefCodecState have no default, so an
		// empty one is 0 (RFC 8794 section 7.2); CueCodecState's default is 0 (RFC 9559 section
		// 5.1.5.1.2.6), so an empty one reads the same.
		var old = element.DataLength == 0 ? 0 : EbmlReader.ReadUInt (source.Slice (element.DataOffset, (int)element.DataLength));
		if (codecState && old == 0)
			return true;
		value = plan.Map (old);
		keep = value == old;
		return value >= old;
	}

	/// <summary>
	/// A Cluster's optional <c>Position</c> (0xA7) is its own Segment offset (RFC 9559 section 5.1.3.2).
	/// A value that matched the old offset gets <see cref="Plan.Map"/> of it. One that cannot be
	/// widened in place is voided, since the element is optional.
	/// </summary>
	static bool TryPatchClusterPositions (byte[] body, Layout layout, Plan plan, out string error)
	{
		error = "";
		if (!plan.Shifts)
			return true;

		var pieces = layout.Pieces;
		for (var index = 0; index < pieces.Count; index++) {
			var piece = pieces[index];
			var old = layout.Starts[index];
			if (piece.Id != MatroskaIds.Cluster || plan.Map ((ulong)old) == (ulong)old)
				continue;

			var at = (int)plan.Map ((ulong)old);
			var end = at + piece.Length;
			if (end > body.Length || !EbmlReader.TryRead (body, at, end, out var cluster) || cluster.Id != MatroskaIds.Cluster) {
				error = "Matroska Cluster cannot be bounded";
				return false;
			}

			var changed = false;
			var child = cluster.DataOffset;
			while (child < end && EbmlReader.TryRead (body, child, end, out var element) && !element.UnknownSize) {
				// An empty Position is 0 (RFC 8794 section 7.2), which names a Cluster at offset 0.
				if (element.Id == MatroskaIds.Position
					&& element.DataLength <= 8
					&& (element.DataLength == 0 ? 0 : EbmlReader.ReadUInt (body.AsSpan (element.DataOffset, (int)element.DataLength))) == (ulong)old) {
					if (!TryWriteUnsigned (body, element.DataOffset, (int)element.DataLength, (ulong)at)) {
						if (!TryMakeVoid (element.End - element.Position, out var hole)) {
							error = "Matroska Cluster Position cannot be updated";
							return false;
						}

						hole.CopyTo (body.AsSpan (element.Position));
					}

					changed = true;
				}

				child = element.End;
			}

			if (changed)
				UpdateChildCrc (body, cluster.DataOffset, end);
		}

		return true;
	}

	/// <summary>Updates a CRC-32 that is the first child in <paramref name="dataOffset"/>..<paramref name="end"/>.</summary>
	static void UpdateChildCrc (byte[] body, int dataOffset, int end)
	{
		if (!EbmlReader.TryRead (body, dataOffset, end, out var first))
			return;
		if (first.Id != MatroskaIds.Crc32 || first.UnknownSize || first.DataLength != 4)
			return;
		WriteCrc (body, first.DataOffset, first.End, end);
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
			Starts = new int[pieces.Count + 1];
			for (var i = 0; i < pieces.Count; i++)
				Starts[i + 1] = Starts[i] + pieces[i].Length;
		}

		/// <summary>Gets each piece's Segment offset, plus the Segment length at the end.</summary>
		public int[] Starts { get; }

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

	[ThreadStatic]
	static long t_settleCalls;

	[ThreadStatic]
	static long t_settleSteps;

	/// <summary>Gets or sets how many values the growth search placed on this thread.</summary>
	internal static long SettleCalls {
		get => t_settleCalls;
		set => t_settleCalls = value;
	}

	/// <summary>
	/// Gets or sets how many growth entries the search examined on this thread. A save stays linear
	/// while this is a logarithmic multiple of <see cref="SettleCalls"/>, so tests bound it instead of a
	/// wall-clock time.
	/// </summary>
	internal static long SettleSteps {
		get => t_settleSteps;
		set => t_settleSteps = value;
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

		/// <summary>Gets or sets the first old Segment offset that moves by <see cref="Delta"/>.</summary>
		public int ShiftFrom { get; set; } = int.MaxValue;

		public int Delta { get; set; }

		public bool MustRetarget { get; set; }

		public bool ShouldAdd { get; set; }

		/// <summary>Gets or sets the index of the piece that <see cref="Grown"/> replaces, or -1.</summary>
		public int GrowIndex { get; set; } = -1;

		/// <summary>Gets or sets the bytes that replace piece <see cref="GrowIndex"/>. They can be longer.</summary>
		public byte[]? Grown { get; set; }

		/// <summary>Gets or sets where the build put <see cref="Grown"/>.</summary>
		public int GrownAt { get; set; } = -1;

		/// <summary>
		/// Gets the SeekHead and Cues pieces that are written again, each value at the width it needs,
		/// because a moved position no longer fits where it was (RFC 8794 section 6.1 lets a value use
		/// any width). Each one's growth moves what follows it.
		/// </summary>
		public SortedSet<int> RenderIndexes { get; } = new ();

		/// <summary>Gets the bytes each piece in <see cref="RenderIndexes"/> is written as.</summary>
		public Dictionary<int, byte[]> Rendered { get; } = new ();

		/// <summary>
		/// Gets or sets the first SeekHead the move writes again with a Seek for <see cref="Id"/>, or -1.
		/// When set, <see cref="ElementOffset"/> is already a new offset: right after that SeekHead.
		/// </summary>
		public int HeadIndex { get; set; } = -1;

		/// <summary>Gets or sets a value indicating whether a rendered first SeekHead lists <see cref="Id"/>.</summary>
		public bool RenderedTarget { get; set; }

		int[] _growthFrom = [];
		long[] _growthSum = [0];

		/// <summary>Gets each rendered piece's old end offset and how many octets it grew, in offset order.</summary>
		public List<(int From, int Delta)> Growth { get; } = new ();

		/// <summary>
		/// Gets or sets a value indicating whether every moved value in a rendered piece is written at
		/// eight octets, so its length no longer depends on the shift.
		/// </summary>
		public bool WideValues { get; set; }

		/// <summary>Gets a value indicating whether any old offset has a new value.</summary>
		public bool Shifts => Delta != 0 || Growth.Exists (step => step.Delta != 0);

		/// <summary>Replaces <see cref="Growth"/> (sorted by offset) and its prefix sums.</summary>
		public void SetGrowth (List<(int From, int Delta)> growth)
		{
			Growth.Clear ();
			Growth.AddRange (growth);
			_growthFrom = new int[growth.Count];
			_growthSum = new long[growth.Count + 1];
			for (var i = 0; i < growth.Count; i++) {
				_growthFrom[i] = growth[i].From;
				_growthSum[i + 1] = _growthSum[i] + growth[i].Delta;
			}
		}

		/// <summary>
		/// Maps an old Segment offset to its new one. This is the only place a shift is applied:
		/// SeekPosition, the Cues positions, and Cluster <c>Position</c> all go through it.
		/// </summary>
		public ulong Map (ulong old) => Settle (Delta != 0 && old >= (ulong)ShiftFrom ? old + (ulong)Delta : old, old);

		/// <summary>
		/// Adds the growth of every rendered piece that ends at or before <paramref name="old"/>: a binary
		/// search over the prefix sums.
		/// </summary>
		ulong Settle (ulong value, ulong old)
		{
			t_settleCalls++;
			var low = 0;
			var high = _growthFrom.Length;
			while (low < high) {
				t_settleSteps++;
				var mid = (low + high) >>> 1;
				if ((ulong)_growthFrom[mid] <= old)
					low = mid + 1;
				else
					high = mid;
			}

			return value + (ulong)_growthSum[low];
		}

		/// <summary>
		/// Gets where the element lands in the new Segment, or -1. The rendered pieces before it
		/// move it; its own insert does not.
		/// </summary>
		public int NewElementOffset => ElementOffset < 0 || HeadIndex >= 0
			? ElementOffset
			: (int)Settle ((ulong)ElementOffset, (ulong)ElementOffset);
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
		out List<(int Start, int End, int Index, bool Final)> seekHeads,
		out List<(int Start, int End, int Index, bool Final)> cues,
		out int elementOffset,
		out string error)
	{
		body = [];
		seekHeads = new List<(int, int, int, bool)> ();
		cues = new List<(int, int, int, bool)> ();
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

		// The body is sized first and written once, so a save holds one copy of it.
		var total = 0L;
		if (!TryWalk (data, layout, plan, null, ref total, seekHeads, cues, ref elementOffset, out error))
			return false;
		if (total > int.MaxValue) {
			error = "Matroska segment is too large to rewrite";
			return false;
		}

		body = new byte[total];
		var written = 0L;
		seekHeads.Clear ();
		cues.Clear ();
		elementOffset = -1;
		return TryWalk (data, layout, plan, body, ref written, seekHeads, cues, ref elementOffset, out error);
	}

	/// <summary>
	/// Walks the pieces in output order. With no <paramref name="body"/> it only counts the length.
	/// </summary>
	static bool TryWalk (
		ReadOnlySpan<byte> data,
		Layout layout,
		Plan plan,
		byte[]? body,
		ref long at,
		List<(int Start, int End, int Index, bool Final)> seekHeads,
		List<(int Start, int End, int Index, bool Final)> cues,
		ref int elementOffset,
		out string error)
	{
		error = "";
		var pieces = layout.Pieces;
		var i = 0;
		while (i < pieces.Count) {
			if (i == plan.ReplaceStart && plan.Replacement is not null) {
				elementOffset = (int)at;
				Put (body, ref at, plan.Replacement);
				i = plan.ReplaceEnd + 1;
				continue;
			}

			var piece = pieces[i];
			if (i == plan.GrowIndex && plan.Grown is not null) {
				plan.GrownAt = (int)at;
				Put (body, ref at, plan.Grown);
				Note (piece.Id, plan.GrownAt, (int)at, i, false, seekHeads, cues);
				i++;
				continue;
			}

			// The insert comes before this piece even when the piece is an old copy that is voided.
			if (i == plan.InsertBefore) {
				elementOffset = (int)at;
				Put (body, ref at, plan.Element);
			}

			if (plan.Rendered.TryGetValue (i, out var rendered)) {
				var start = (int)at;
				Put (body, ref at, rendered);
				Note (piece.Id, start, (int)at, i, true, seekHeads, cues);
				i++;
				continue;
			}

			if (plan.VoidExisting && piece.Id == plan.Id) {
				if (!TryMakeVoid (piece.Length, out var hole)) {
					error = plan.PlaceError;
					return false;
				}

				Put (body, ref at, hole);
				i++;
				continue;
			}

			var from = (int)at;
			Put (body, ref at, data.Slice (piece.Offset, piece.Length));
			Note (piece.Id, from, (int)at, i, false, seekHeads, cues);
			i++;
		}

		if (plan.Append) {
			elementOffset = (int)at;
			Put (body, ref at, plan.Element);
		}

		return true;
	}

	static void Put (byte[]? body, ref long at, ReadOnlySpan<byte> bytes)
	{
		if (body is not null)
			bytes.CopyTo (body.AsSpan ((int)at));
		at += bytes.Length;
	}

	static void Note (
		ulong id,
		int start,
		int end,
		int index,
		bool final,
		List<(int Start, int End, int Index, bool Final)> seekHeads,
		List<(int Start, int End, int Index, bool Final)> cues)
	{
		if (id == MatroskaIds.SeekHead)
			seekHeads.Add ((start, end, index, final));
		else if (id == MatroskaIds.Cues)
			cues.Add ((start, end, index, final));
	}

	/// <summary>
	/// Patches every SeekHead and Cues in place. A piece whose moved values do not fit their fields is
	/// added to <paramref name="widen"/> instead, so it can be written again wider; a Seek is never
	/// voided for that (RFC 9559 section 6.3).
	/// </summary>
	static bool TryPatch (
		byte[] body,
		List<(int Start, int End, int Index, bool Final)> seekHeads,
		List<(int Start, int End, int Index, bool Final)> cues,
		Plan plan,
		List<int> widen,
		out bool referenced,
		out string error)
	{
		error = "";
		referenced = false;
		var strict = plan.MustRetarget || plan.Shifts;
		if (strict || plan.ShouldAdd) {
			// A new entry goes in the first SeekHead. A second one holds only Clusters (RFC 9559 section 6.3).
			for (var i = 0; i < seekHeads.Count; i++) {
				var (start, end, index, final) = seekHeads[i];
				if (final) {
					if (i == 0 && plan.RenderedTarget)
						referenced = true;
					continue;
				}

				var tooNarrow = false;
				if (!TryPatchSeekHead (body, start, end, plan, strict, i == 0, ref referenced, ref tooNarrow, out error))
					return false;
				if (tooNarrow)
					widen.Add (index);
			}
		} else
			referenced = true;

		if (!plan.Shifts)
			return true;

		foreach (var (start, end, index, final) in cues) {
			if (final)
				continue;
			var tooNarrow = false;
			if (!TryPatchCues (body, start, end, plan, ref tooNarrow, out error))
				return false;
			if (tooNarrow)
				widen.Add (index);
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
	static bool IsReachable (Layout layout, Plan plan, int seekHeadCount, bool referenced, out string error)
	{
		error = "";
		if (plan.Id != MatroskaIds.Attachments || plan.ElementOffset < 0 || referenced)
			return true;
		if (seekHeadCount > 0) {
			error = "Matroska SeekHead cannot be written again to reference the attachments";
			return false;
		}

		var pieces = layout.Pieces;
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Cluster)
				continue;
			var clusterAt = (int)plan.Map ((ulong)layout.Starts[i]);
			if (plan.NewElementOffset < clusterAt)
				return true;
			error = "Matroska file has no SeekHead, so attachments after a Cluster would not be found";
			return false;
		}

		return true;
	}

	static bool TryPatchSeekHead (byte[] body, int start, int end, Plan plan, bool strict, bool mayAdd, ref bool referenced, ref bool tooNarrow, out string error)
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
			if (!TryPatchSeek (body, child, plan, ref changed, ref referenced, ref tooNarrow)) {
				error = "Matroska SeekHead cannot be updated";
				return false;
			}
		}

		if (tooNarrow)
			return true;

		var target = plan.NewElementOffset;
		if (mayAdd && target >= 0 && !referenced && TryAddSeek (body, seekHead, plan.Id, (ulong)target)) {
			changed = true;
			referenced = true;
		}

		if (changed)
			UpdateElementCrc (body, seekHead.Position, seekHead.End);
		return true;
	}

	/// <summary>
	/// Patches one Seek. A moved position for another element that no longer fits sets
	/// <paramref name="tooNarrow"/>, so the SeekHead is written again wider.
	/// </summary>
	static bool TryPatchSeek (byte[] body, EbmlElement seek, Plan plan, ref bool changed, ref bool referenced, ref bool tooNarrow)
	{
		var strict = plan.MustRetarget || plan.Shifts;
		if (!TryReadSeek (body, seek, out var id, out var position, out var positionElement))
			return strict ? TryVoidElement (body, seek, ref changed) : true;

		var isTarget = plan.Id != 0 && id == plan.Id;
		ulong desired;
		if (isTarget) {
			if (plan.ElementOffset < 0)
				return TryVoidElement (body, seek, ref changed);
			desired = (ulong)plan.NewElementOffset;
		} else if (plan.Map (position) != position) {
			desired = plan.Map (position);
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

		// The element's own Seek may go: the move lists it again. Any other Seek must stay.
		if (isTarget)
			return TryVoidElement (body, seek, ref changed);
		tooNarrow = true;
		return true;
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

	static bool TryPatchCues (byte[] body, int start, int end, Plan plan, ref bool tooNarrow, out string error)
	{
		error = "";
		if (!EbmlReader.TryRead (body, start, Math.Min (end, body.Length), out var cues) || cues.UnknownSize || cues.Position != start) {
			error = "Matroska Cues cannot be bounded";
			return false;
		}

		var changed = false;
		if (!TryPatchCueValues (body, MatroskaIds.Cues, cues.DataOffset, cues.End, plan, ref changed, ref tooNarrow)) {
			error = "Matroska Cues cannot be updated";
			return false;
		}

		if (changed && !tooNarrow)
			UpdateElementCrc (body, cues.Position, cues.End);
		return true;
	}

	/// <summary>
	/// Moves every Cues position in place in the children of a <paramref name="parent"/> master (see
	/// <see cref="RenderCues"/> for the list). Only the child that <see cref="CueChildMaster"/> names is
	/// walked, so the recursion is at most four levels deep. A value that no longer fits its field sets
	/// <paramref name="tooNarrow"/>. A malformed value fails.
	/// </summary>
	static bool TryPatchCueValues (byte[] body, ulong parent, int offset, int end, Plan plan, ref bool changed, ref bool tooNarrow)
	{
		var childMaster = CueChildMaster (parent);
		while (offset < end) {
			if (!EbmlReader.TryRead (body, offset, end, out var element) || element.UnknownSize)
				return false;
			if (IsCuePosition (parent, element.Id)) {
				if (!TryMoveCueValue (body, element, plan, out var updated, out var keep))
					return false;
				if (!keep) {
					if (TryWriteUnsigned (body, element.DataOffset, (int)element.DataLength, updated))
						changed = true;
					else
						tooNarrow = true;
				}
			} else if (element.Id == childMaster) {
				var nested = false;
				if (!TryPatchCueValues (body, element.Id, element.DataOffset, element.End, plan, ref nested, ref tooNarrow))
					return false;
				if (nested) {
					changed = true;
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

		// Written once into an array of the final length.
		var tail = data.Length - layout.SegmentEnd;
		var output = new byte[(long)layout.SegmentPosition + layout.IdWidth + size.Length + body.Length + tail];
		var at = 0L;
		Put (output, ref at, data.Slice (0, layout.SegmentPosition + layout.IdWidth));
		Put (output, ref at, size);
		Put (output, ref at, body);
		if (tail > 0)
			Put (output, ref at, data.Slice (layout.SegmentEnd));
		return output;
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

	/// <summary>A Seek; with <paramref name="wide"/> its SeekPosition always has eight octets.</summary>
	static byte[] MakeSeek (ulong elementId, ulong position, bool wide = false)
	{
		var seekId = EbmlWriter.Master (MatroskaIds.SeekId, IdBytes (elementId));
		var octets = UnsignedBytes (position);
		if (wide && octets.Length < 8) {
			var padded = new byte[8];
			octets.CopyTo (padded, 8 - octets.Length);
			octets = padded;
		}

		var seekPosition = EbmlWriter.Master (MatroskaIds.SeekPosition, octets);
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

	[ThreadStatic]
	static long t_offsetSteps;

	/// <summary>
	/// Pieces summed by <see cref="OffsetOf"/> on this thread. A save stays linear while this is a small
	/// multiple of the piece count, so tests bound it instead of a wall-clock time.
	/// </summary>
	internal static long OffsetSteps {
		get => t_offsetSteps;
		set => t_offsetSteps = value;
	}

	static int OffsetOf (List<Piece> pieces, int index)
	{
		t_offsetSteps += index;
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
