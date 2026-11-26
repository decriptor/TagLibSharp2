// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

static partial class MatroskaRewrite
{
	/// <summary>
	/// Plans a path save. Cluster payloads stay on <paramref name="source"/>; the bytes written later match
	/// <see cref="TryRewrite"/>.
	/// </summary>
	internal static bool TryPlanStream (
		Stream source,
		long length,
		byte[] chunk,
		MatroskaTag tag,
		MatroskaChapters chapters,
		IReadOnlyList<TrackFlagEdit>? edits,
		out StreamPlan? plan,
		out string error)
	{
		plan = null;
		var reader = new Reader (source, length, chunk);
		if (!TryParseStream (reader, out var parsed, out error) || parsed is null)
			return false;

		var doc = parsed;
		if (edits is not null) {
			if (!TryApplyFlags (doc, edits, out var flagged, out var inserted, out error))
				return false;
			if (flagged is not null) {
				doc = flagged;
				if (inserted)
					RaisePrefix (doc, 2);
			}
		}

		var editChapters = chapters.IsModified;
		if (editChapters && !chapters.TryValidate (out error))
			return false;

		HashSet<ulong>? removed = null;
		var attachmentsChanged = false;
		if (tag.PicturesChanged) {
			if (!MatroskaAttachments.TryBuild (doc.Data, RangesOf (doc.Layout, MatroskaIds.Attachments), tag.Pictures, doc.Layout.IsWebM, out var attachments, out attachmentsChanged, out removed, out error))
				return false;
			if (attachmentsChanged && !TryStageStream (ref doc, MatroskaIds.Attachments, attachments, false, out error))
				return false;
		}

		if (editChapters && !TryStageStream (ref doc, MatroskaIds.Chapters, chapters.Render ().ToArray (), chapters.HasOrderedEdition, out error))
			return false;

		var tags = (attachmentsChanged ? tag.RenderWithout (removed) : tag.Render ()).ToArray ();
		if (!tag.IsAsRead (tags) && !TryStageStream (ref doc, MatroskaIds.Tags, tags, false, out error))
			return false;

		byte[]? size = null;
		if (doc.Changed) {
			var body = doc.Layout.Starts[doc.Layout.Pieces.Count];
			size = doc.UnknownSize ? doc.SizeField : EncodeFittedSize ((ulong)body, doc.SizeWidth);
			if (size.Length == 0) {
				error = "Matroska segment size cannot be written";
				return false;
			}
		}

		plan = new StreamPlan (doc, size);
		return true;
	}

	/// <summary>Writes <paramref name="plan"/>. Cluster bytes are copied from <paramref name="source"/> in chunks.</summary>
	internal static void WritePlan (Stream source, Stream destination, StreamPlan plan, byte[] chunk)
	{
		var doc = plan.Document;
		if (!doc.Changed) {
			source.Position = 0;
			StreamCopy.Copy (source, destination, doc.FileLength, chunk);
			return;
		}

		StreamCopy.Write (destination, doc.Head, chunk);
		StreamCopy.Write (destination, plan.Size!, chunk);
		var bodyStart = destination.Position;
		var hasCrc = TryFindLeadingCrc (doc, out var payloadAt, out var coveredAt);
		var crc = 0xFFFFFFFFu;
		var bodyPos = 0;
		var pieces = doc.Layout.Pieces;
		for (var i = 0; i < pieces.Count; i++) {
			var piece = pieces[i];
			if (piece.Id == MatroskaIds.Cluster) {
				var state = doc.Clusters[i] ?? throw new InvalidOperationException ("Matroska Cluster cannot be bounded");
				WriteCluster (source, destination, state, chunk, ref crc, coveredAt, ref bodyPos, hasCrc);
				continue;
			}

			var bytes = doc.Data.AsSpan (piece.Offset, piece.Length).ToArray ();
			StreamCopy.Write (destination, bytes, chunk);
			if (hasCrc)
				Feed (bytes, bodyPos, coveredAt, ref crc);
			bodyPos += piece.Length;
		}

		if (hasCrc) {
			var end = destination.Position;
			var four = new byte[4];
			var value = ~crc;
			four[0] = (byte)value;
			four[1] = (byte)(value >> 8);
			four[2] = (byte)(value >> 16);
			four[3] = (byte)(value >> 24);
			destination.Position = bodyStart + payloadAt;
			destination.Write (four, 0, 4);
			destination.Position = end;
		}

		if (doc.TailLength > 0) {
			source.Position = doc.TailOrigin;
			StreamCopy.Copy (source, destination, doc.TailLength, chunk);
		}
	}

	static bool TryStageStream (ref Document doc, ulong id, byte[] element, bool afterInfo, out string error)
	{
		if (!TryReplaceStream (doc, id, element, afterInfo, out var next, out error))
			return false;
		doc = next;
		return true;
	}

	static bool TryReplaceStream (Document doc, ulong id, byte[] element, bool afterInfo, out Document built, out string error)
	{
		built = doc;
		var plan = PlanEdit (doc.Layout.Pieces, id, element, IsReferenced (doc.Data, doc.Layout, id));
		if (!TryApplyStream (doc, plan, out built, out var seekHeadCount, out var referenced, out error))
			return false;

		var moveError = "";
		if (NeedsMove (doc.Layout, plan, seekHeadCount, referenced)) {
			if (TryPlanMove (doc.Data, doc.Layout, id, element, afterInfo, out var moved)
				&& TryApplyStream (doc, moved, out var movedDoc, out var movedHeads, out var movedReferenced, out moveError)) {
				plan = moved;
				built = movedDoc;
				seekHeadCount = movedHeads;
				referenced = movedReferenced;
			} else if (!TryApplyStream (doc, plan, out built, out seekHeadCount, out referenced, out error))
				return false;
		}

		if (!IsReachable (doc.Layout, plan, seekHeadCount, referenced, out error)) {
			if (moveError.Length > 0)
				error = moveError;
			return false;
		}

		return true;
	}

	static bool TryGrowStream (Document doc, byte[] replacement, out Document built, out string error)
	{
		built = doc;
		replacement = replacement.ToArray ();
		UpdateElementCrc (replacement, 0, replacement.Length);
		if (!EbmlReader.TryRead (replacement, 0, replacement.Length, out var parsed) || parsed.UnknownSize || parsed.End != replacement.Length) {
			error = "Matroska replacement is not one element";
			return false;
		}

		var pieces = doc.Layout.Pieces;
		var index = pieces.FindIndex (piece => piece.Id == parsed.Id && !piece.UnknownSize);
		if (index < 0) {
			error = "Matroska element to replace was not found";
			return false;
		}

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
			plan.ShiftFrom = doc.Layout.Starts[index + 1];
			plan.Delta = replacement.Length - pieces[index].Length;
			plan.MustRetarget = true;
		}

		return TryApplyStream (doc, plan, out built, out _, out _, out error);
	}

	static bool TryApplyStream (
		Document doc,
		Plan plan,
		out Document built,
		out int seekHeadCount,
		out bool referenced,
		out string error)
	{
		built = doc;
		seekHeadCount = 0;
		referenced = false;
		error = "";
		var pieces = doc.Layout.Pieces;
		if (plan.Replacement is not null && plan.ReplaceEnd != pieces.Count - 1) {
			var region = 0;
			for (var index = plan.ReplaceStart; index <= plan.ReplaceEnd; index++)
				region += pieces[index].Length;
			if (plan.Replacement.Length != region) {
				error = plan.PlaceError;
				return false;
			}
		}

		for (var attempt = 0; attempt < 3; attempt++) {
			if (!TrySettle (doc.Data, doc.Layout, plan, out error))
				return false;

			using var draft = new Draft ();
			if (!TryWalkStream (doc, plan, draft, out var elementOffset, out error))
				return false;
			if (plan.Element.Length > 0 && elementOffset != plan.NewElementOffset) {
				error = plan.PlaceError;
				return false;
			}

			var widen = new List<int> ();
			if (!TryPatchDraft (draft, plan, widen, out referenced, out error))
				return false;
			if (widen.Count > 0) {
				foreach (var index in widen)
					plan.RenderIndexes.Add (index);
				for (var index = 0; index < pieces.Count; index++) {
					var piece = pieces[index];
					if ((piece.Id == MatroskaIds.SeekHead || piece.Id == MatroskaIds.Cues) && !piece.UnknownSize)
						plan.RenderIndexes.Add (index);
				}

				continue;
			}

			if (plan.Grown is not null && plan.GrownAt >= 0 && !TryUpdateGrownCrc (draft, plan, out error))
				return false;
			if (!TryBakeClusters (doc, plan, draft, out error))
				return false;
			if (!TryBuildDocument (doc, draft, out built, out error))
				return false;

			seekHeadCount = draft.SeekHeadCount;
			return true;
		}

		error = "Matroska positions could not be updated";
		return false;
	}

	static bool TryWalkStream (Document doc, Plan plan, Draft draft, out int elementOffset, out string error)
	{
		elementOffset = -1;
		error = "";
		var pieces = doc.Layout.Pieces;
		var i = 0;
		while (i < pieces.Count) {
			if (i == plan.ReplaceStart && plan.Replacement is not null) {
				elementOffset = draft.At;
				if (!draft.TryAdd (plan.Replacement, false, 0, -1, false, out error))
					return false;
				i = plan.ReplaceEnd + 1;
				continue;
			}

			var piece = pieces[i];
			if (i == plan.GrowIndex && plan.Grown is not null) {
				plan.GrownAt = draft.At;
				if (!draft.TryAdd (plan.Grown, true, piece.Id, i, false, out error))
					return false;
				i++;
				continue;
			}

			if (i == plan.InsertBefore) {
				elementOffset = draft.At;
				if (!draft.TryAdd (plan.Element, false, 0, -1, false, out error))
					return false;
			}

			if (plan.Rendered.TryGetValue (i, out var rendered)) {
				if (!draft.TryAdd (rendered, true, piece.Id, i, true, out error))
					return false;
				i++;
				continue;
			}

			if (plan.VoidExisting && piece.Id == plan.Id) {
				if (!TryMakeVoid (piece.Length, out var hole)) {
					error = plan.PlaceError;
					return false;
				}

				if (!draft.TryAdd (hole, false, 0, -1, false, out error))
					return false;
				i++;
				continue;
			}

			if (piece.Id == MatroskaIds.Cluster) {
				var state = doc.Clusters[i];
				if (state is null) {
					error = "Matroska Cluster cannot be bounded";
					return false;
				}

				if (!draft.TryAddCluster (state, i, out error))
					return false;
				i++;
				continue;
			}

			if (!TrySlice (doc, piece, out var bytes, out error) || !draft.TryAdd (bytes, true, piece.Id, i, false, out error))
				return false;
			i++;
		}

		if (plan.Append) {
			elementOffset = draft.At;
			if (!draft.TryAdd (plan.Element, false, 0, -1, false, out error))
				return false;
		}

		return draft.Finish (out error);
	}

	static bool TryPatchDraft (Draft draft, Plan plan, List<int> widen, out bool referenced, out string error)
	{
		error = "";
		referenced = false;
		var strict = plan.MustRetarget || plan.Shifts;
		var heads = draft.Heads;
		if (strict || plan.ShouldAdd) {
			for (var i = 0; i < heads.Count; i++) {
				var mark = heads[i];
				if (mark.Final) {
					if (i == 0 && plan.RenderedTarget)
						referenced = true;
					continue;
				}

				var bytes = draft.BytesOf (mark);
				var tooNarrow = false;
				if (!TryPatchSeekHead (bytes, mark.LocalStart, mark.LocalEnd, plan, strict, i == 0, ref referenced, ref tooNarrow, out error))
					return false;
				if (tooNarrow)
					widen.Add (mark.PieceIndex);
			}
		} else
			referenced = true;

		if (!plan.Shifts)
			return true;

		foreach (var mark in draft.Cues) {
			if (mark.Final)
				continue;
			var bytes = draft.BytesOf (mark);
			var tooNarrow = false;
			if (!TryPatchCues (bytes, mark.LocalStart, mark.LocalEnd, plan, ref tooNarrow, out error))
				return false;
			if (tooNarrow)
				widen.Add (mark.PieceIndex);
		}

		return true;
	}

	static bool TryUpdateGrownCrc (Draft draft, Plan plan, out string error)
	{
		error = "";
		var grown = plan.Grown!;
		foreach (var seg in draft.Segments) {
			if (seg.Bytes is null)
				continue;
			var local = plan.GrownAt - seg.BodyStart;
			if (local < 0 || grown.Length > seg.Bytes.Length - local)
				continue;
			UpdateElementCrc (seg.Bytes, local, local + grown.Length);
			return true;
		}

		error = "Matroska segment cannot be bounded";
		return false;
	}

	static bool TryBakeClusters (Document doc, Plan plan, Draft draft, out string error)
	{
		error = "";
		foreach (var seg in draft.Segments) {
			if (seg.Cluster is null)
				continue;
			if (!TryBake (seg.Cluster, doc.Layout.Starts[seg.SourceIndex], plan, out var baked, out error))
				return false;
			seg.Cluster = baked;
		}

		return true;
	}

	static bool TryBake (ClusterState source, int oldOffset, Plan plan, out ClusterState baked, out string error)
	{
		error = "";
		baked = source.Clone ();
		if (!plan.Shifts || plan.Map ((ulong)oldOffset) == (ulong)oldOffset)
			return true;

		var at = (int)plan.Map ((ulong)oldOffset);
		var spots = new List<Spot> (baked.Spots.Count);
		var changed = false;
		foreach (var spot in baked.Spots) {
			if (spot.Value != (ulong)oldOffset || spot.DataLength > 8) {
				spots.Add (spot);
				continue;
			}

			changed = true;
			if (spot.DataLength is > 0 and <= 8) {
				var bytes = new byte[spot.DataLength];
				if (TryWriteUnsigned (bytes, 0, spot.DataLength, (ulong)at)) {
					baked.Overlays.Add (new Overlay (spot.DataAt, bytes));
					spots.Add (new Spot (spot.ElementAt, spot.ElementLength, spot.DataAt, spot.DataLength, (ulong)at));
					continue;
				}
			}

			if (!TryMakeVoid (spot.ElementLength, out var hole)) {
				error = "Matroska Cluster Position cannot be updated";
				return false;
			}

			baked.Overlays.Add (new Overlay (spot.ElementAt, hole));
		}

		baked.Spots = spots;
		if (changed)
			baked.CrcDirty = true;
		return true;
	}

	static bool TryApplyFlags (Document doc, IReadOnlyList<TrackFlagEdit> edits, out Document? updated, out bool inserted, out string error)
	{
		updated = null;
		inserted = false;
		error = "";
		var byOrdinal = new Dictionary<int, int> (edits.Count);
		for (var i = 0; i < edits.Count; i++)
			byOrdinal[edits[i].Ordinal] = i;

		var pieces = doc.Layout.Pieces;
		var found = new bool[edits.Count];
		var replacements = new byte[]?[pieces.Count];
		var ordinal = 0;
		var any = false;
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Tracks)
				continue;
			if (!TryEditTracks (doc.Data, pieces[i], edits, byOrdinal, found, ref ordinal, ref inserted, out replacements[i], out error))
				return false;
			any |= replacements[i] is not null;
		}

		for (var i = 0; i < edits.Count; i++) {
			if (!found[i]) {
				error = "Matroska track " + edits[i].TrackNumber.ToString (System.Globalization.CultureInfo.InvariantCulture)
					+ " is not in the data being saved";
				return false;
			}
		}

		if (!any)
			return true;

		var growable = pieces.FindIndex (piece => piece.Id == MatroskaIds.Tracks && !piece.UnknownSize);
		using var draft = new Draft ();
		if (!TryLayFlags (doc, replacements, growable, draft, out var grow, out var inPlace, out error) || !draft.Finish (out error))
			return false;
		if (inPlace && !TryBuildDocument (doc, draft, out updated, out error))
			return false;
		if (grow is not null && !TryGrowStream (updated ?? doc, grow, out updated, out error))
			return false;
		return true;
	}

	static bool TryLayFlags (Document doc, byte[]?[] replacements, int growable, Draft draft, out byte[]? grow, out bool inPlace, out string error)
	{
		grow = null;
		inPlace = false;
		error = "";
		var pieces = doc.Layout.Pieces;
		var index = 0;
		while (index < pieces.Count) {
			var piece = pieces[index];
			if (replacements[index] is not { } tracks) {
				if (!EmitPiece (doc, draft, index, out error))
					return false;
				index++;
				continue;
			}

			var extra = tracks.Length - piece.Length;
			var room = index + 1 < pieces.Count && pieces[index + 1].Id == MatroskaIds.Void && !pieces[index + 1].UnknownSize
				? pieces[index + 1].Length
				: 0;
			if (extra > 0 && room != extra && room - extra < 2) {
				if (index != growable) {
					var octets = extra.ToString (System.Globalization.CultureInfo.InvariantCulture);
					error = piece.UnknownSize
						? "Matroska FlagEnabled needs " + octets + " more octets, but Tracks has an unknown size and no Void right after it"
						: "Matroska FlagEnabled needs " + octets + " more octets in a second Tracks and no Void right after Tracks can give them";
					return false;
				}

				if (!EmitPiece (doc, draft, index, out error))
					return false;
				grow = tracks;
				index++;
				continue;
			}

			if (!draft.TryAdd (tracks, false, 0, -1, false, out error))
				return false;
			inPlace = true;
			if (extra == 0) {
				index++;
				continue;
			}

			if (room > extra && TryMakeVoid (room - extra, out var shrunk) && !draft.TryAdd (shrunk, false, 0, -1, false, out error))
				return false;
			index += 2;
		}

		return true;
	}

	static bool EmitPiece (Document doc, Draft draft, int index, out string error)
	{
		var piece = doc.Layout.Pieces[index];
		if (piece.Id == MatroskaIds.Cluster) {
			var state = doc.Clusters[index];
			if (state is null) {
				error = "Matroska Cluster cannot be bounded";
				return false;
			}

			return draft.TryAddCluster (state, index, out error);
		}

		if (!TrySlice (doc, piece, out var bytes, out error))
			return false;
		return draft.TryAdd (bytes, false, 0, -1, false, out error);
	}

	static bool TrySlice (Document doc, Piece piece, out byte[] bytes, out string error)
	{
		bytes = [];
		error = "";
		if (piece.Offset < 0 || piece.Length < 0 || piece.Offset > doc.Data.Length - piece.Length) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		bytes = new byte[piece.Length];
		if (piece.Length > 0)
			doc.Data.AsSpan (piece.Offset, piece.Length).CopyTo (bytes);
		return true;
	}

	static void RaisePrefix (Document doc, ulong version)
	{
		var prefix = new byte[doc.Head.Length + doc.SizeField.Length];
		doc.Head.CopyTo (prefix, 0);
		doc.SizeField.CopyTo (prefix, doc.Head.Length);
		var raised = RaiseDocTypeVersion (prefix, version);
		if (raised.Length < doc.SizeWidth)
			return;
		var headLength = raised.Length - doc.SizeWidth;
		var head = new byte[headLength];
		var size = new byte[doc.SizeWidth];
		raised.AsSpan (0, headLength).CopyTo (head);
		raised.AsSpan (headLength, doc.SizeWidth).CopyTo (size);
		doc.Head = head;
		doc.SizeField = size;
		doc.SegmentPosition = headLength - doc.IdWidth;
		doc.Changed = true;
	}

	static bool TryBuildDocument (Document source, Draft draft, out Document built, out string error)
	{
		built = source;
		error = "";
		using var packed = new MemoryStream ();
		var pieces = new List<Piece> ();
		var states = new List<ClusterState?> ();
		var saw = false;
		foreach (var seg in draft.Segments) {
			if (seg.Cluster is { } cluster) {
				pieces.Add (new Piece (MatroskaIds.Cluster, -1, cluster.Length, cluster.UnknownSize));
				states.Add (cluster);
				saw = true;
				continue;
			}

			var bytes = seg.Bytes ?? [];
			var baseOffset = packed.Length > int.MaxValue ? -1 : (int)packed.Length;
			if (baseOffset < 0) {
				error = "Matroska segment is too large to rewrite";
				return false;
			}

			var before = pieces.Count;
			if (!TryParseRun (bytes, baseOffset, ref saw, pieces, out error))
				return false;
			for (var n = before; n < pieces.Count; n++)
				states.Add (null);
			if (bytes.Length > 0)
				packed.Write (bytes, 0, bytes.Length);
		}

		long body = 0;
		long meta = 0;
		foreach (var piece in pieces) {
			body += piece.Length;
			if (piece.Id != MatroskaIds.Cluster)
				meta += piece.Length;
		}

		if (body != draft.At || meta != packed.Length) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		if (body > int.MaxValue) {
			error = "Matroska segment is too large to rewrite";
			return false;
		}

		var data = packed.ToArray ();
		var layout = MakeLayout (source, pieces, (int)body);
		built = source.With (layout, data, states.ToArray ());
		return true;
	}

	static bool TryParseRun (byte[] run, int packedBase, ref bool sawElement, List<Piece> pieces, out string error)
	{
		error = "";
		var offset = 0;
		var end = run.Length;
		while (offset < end) {
			if (!EbmlReader.TryRead (run, offset, end, out var element)) {
				if (sawElement) {
					error = "Matroska segment cannot be bounded";
					return false;
				}

				var gap = offset;
				while (offset < end && !EbmlReader.TryRead (run, offset, end, out _))
					offset++;
				if (offset > gap)
					pieces.Add (new Piece (0, packedBase + gap, offset - gap, false));
				continue;
			}

			int pieceEnd;
			if (element.UnknownSize) {
				if (!TryBound (run, element.DataOffset, end, out pieceEnd)) {
					error = "Matroska element size is unknown and cannot be bounded";
					return false;
				}
			} else
				pieceEnd = element.End;

			if (pieceEnd <= offset || pieceEnd > end) {
				error = "Matroska segment cannot be bounded";
				return false;
			}

			pieces.Add (new Piece (element.Id, packedBase + element.Position, pieceEnd - element.Position, element.UnknownSize));
			sawElement = true;
			offset = pieceEnd;
		}

		return true;
	}

	static Layout MakeLayout (Document source, List<Piece> pieces, int bodyLength) =>
		new (source.SegmentPosition, source.IdWidth, source.SizeWidth, source.UnknownSize, 0, bodyLength, pieces, source.Layout.IsWebM);

	static bool TryParseStream (Reader reader, out Document? doc, out string error)
	{
		doc = null;
		error = "";
		if (reader.Length < 4) {
			error = "Not a Matroska file: missing EBML header";
			return false;
		}

		var magic = reader.ReadExact (0, 4);
		if (!MatroskaFile.IsValidFormat (magic)) {
			error = "Not a Matroska file: missing EBML header";
			return false;
		}

		if (!reader.TryElement (0, reader.Length, out var headerId, out _, out var headerData, out var headerEnd)
			|| headerId != MatroskaIds.Ebml
			|| headerData < 0
			|| headerEnd > int.MaxValue) {
			error = "Not a Matroska file: EBML header is truncated";
			return false;
		}

		var headerBytes = reader.ReadExact (0, (int)headerEnd);
		if (!EbmlReader.TryRead (headerBytes, 0, headerBytes.Length, out var header)) {
			error = "Not a Matroska file: EBML header is truncated";
			return false;
		}

		var docType = ReadDocType (headerBytes, header);
		if (docType is null) {
			error = "Not a Matroska file: DocType is missing";
			return false;
		}

		if (!docType.Equals ("matroska", StringComparison.OrdinalIgnoreCase)
			&& !docType.Equals ("webm", StringComparison.OrdinalIgnoreCase)) {
			error = "Not a Matroska file: DocType is " + docType;
			return false;
		}

		var scan = (long)header.End;
		var found = false;
		long segmentPos = 0;
		var segmentHeader = 0;
		long segmentData = -1;
		long segmentDataEnd = 0;
		while (scan < reader.Length) {
			if (!reader.TryElement (scan, reader.Length, out var id, out var headerLength, out var dataLength, out var elementEnd))
				break;
			if (id == MatroskaIds.Segment) {
				segmentPos = scan;
				segmentHeader = headerLength;
				segmentData = dataLength;
				segmentDataEnd = elementEnd;
				found = true;
				break;
			}

			if (dataLength < 0) {
				error = "Matroska element size is unknown before Segment";
				return false;
			}

			scan = elementEnd;
		}

		if (!found) {
			error = "Matroska file has no Segment";
			return false;
		}

		var dataOffset = segmentPos + segmentHeader;
		long segmentEnd = segmentData < 0 ? reader.EndAtNextTopLevel (dataOffset) : segmentDataEnd;
		if (segmentEnd < dataOffset || segmentEnd > reader.Length) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		var segmentHeaderBytes = reader.ReadExact (segmentPos, segmentHeader);
		if (!EbmlReader.TryReadVint (segmentHeaderBytes, 0, segmentHeaderBytes.Length, true, 4, out _, out var idWidth, out _)) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		var sizeWidth = segmentHeader - idWidth;
		if (sizeWidth < 1 || idWidth < 1) {
			error = "Matroska segment cannot be bounded";
			return false;
		}

		var headLength = segmentPos + idWidth;
		if (headLength > int.MaxValue || sizeWidth > int.MaxValue) {
			error = "Matroska segment is too large to rewrite";
			return false;
		}

		var head = reader.ReadExact (0, (int)headLength);
		var sizeField = reader.ReadExact (segmentPos + idWidth, sizeWidth);
		using var packed = new MemoryStream ();
		var pieces = new List<Piece> ();
		var states = new List<ClusterState?> ();
		var saw = false;
		var cursor = dataOffset;
		while (cursor < segmentEnd) {
			if (!reader.TryElement (cursor, segmentEnd, out var id, out var headerLength, out var dataLength, out var elementEnd)) {
				if (saw) {
					error = "Matroska segment cannot be bounded";
					return false;
				}

				var gap = cursor;
				while (cursor < segmentEnd && !reader.TryElement (cursor, segmentEnd, out _, out _, out _, out _))
					cursor++;
				if (cursor > gap && !TryAddStreamPiece (reader, packed, pieces, states, 0, gap, cursor - gap, false, out error))
					return false;
				continue;
			}

			long pieceEnd;
			var unknown = dataLength < 0;
			if (unknown) {
				if (!reader.TryBound (cursor + headerLength, segmentEnd, out pieceEnd)) {
					error = "Matroska element size is unknown and cannot be bounded";
					return false;
				}
			} else
				pieceEnd = elementEnd;

			if (pieceEnd <= cursor || pieceEnd > segmentEnd) {
				error = "Matroska segment cannot be bounded";
				return false;
			}

			var pieceLength = pieceEnd - cursor;
			if (id == MatroskaIds.Cluster) {
				if (pieceLength > int.MaxValue) {
					error = "Matroska segment is too large to rewrite";
					return false;
				}

				var state = new ClusterState { Origin = cursor, Length = (int)pieceLength, UnknownSize = unknown };
				reader.ScanCluster (state, cursor, headerLength, pieceEnd);
				pieces.Add (new Piece (MatroskaIds.Cluster, -1, state.Length, unknown));
				states.Add (state);
			} else if (!TryAddStreamPiece (reader, packed, pieces, states, id, cursor, pieceLength, unknown, out error))
				return false;

			saw = true;
			cursor = pieceEnd;
		}

		long body = 0;
		long meta = 0;
		foreach (var piece in pieces) {
			body += piece.Length;
			if (piece.Id != MatroskaIds.Cluster)
				meta += piece.Length;
		}

		if (body != segmentEnd - dataOffset || meta != packed.Length || body > int.MaxValue) {
			error = body > int.MaxValue ? "Matroska segment is too large to rewrite" : "Matroska segment cannot be bounded";
			return false;
		}

		var webm = docType.Equals ("webm", StringComparison.OrdinalIgnoreCase);
		var layout = new Layout ((int)segmentPos, idWidth, sizeWidth, segmentData < 0, 0, (int)body, pieces, webm);
		doc = new Document (layout, packed.ToArray (), states.ToArray (), head, sizeField, segmentEnd, reader.Length - segmentEnd, reader.Length);
		return true;
	}

	static bool TryAddStreamPiece (
		Reader reader,
		MemoryStream packed,
		List<Piece> pieces,
		List<ClusterState?> states,
		ulong id,
		long filePos,
		long length,
		bool unknown,
		out string error)
	{
		error = "";
		if (length > int.MaxValue || packed.Length > int.MaxValue - length) {
			error = "Matroska segment is too large to rewrite";
			return false;
		}

		var offset = (int)packed.Length;
		var bytes = reader.ReadExact (filePos, (int)length);
		if (bytes.Length > 0)
			packed.Write (bytes, 0, bytes.Length);
		pieces.Add (new Piece (id, offset, (int)length, unknown));
		states.Add (null);
		return true;
	}

	static bool TryFindLeadingCrc (Document doc, out int payload, out int covered)
	{
		payload = -1;
		covered = 0;
		var prefix = 0;
		var pieces = doc.Layout.Pieces;
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id == MatroskaIds.Cluster) {
				prefix = doc.Layout.Starts[i];
				break;
			}

			prefix = doc.Layout.Starts[i + 1];
		}

		if (prefix <= 0 || prefix > doc.Data.Length)
			return false;

		var offset = 0;
		while (offset < prefix && !EbmlReader.TryRead (doc.Data, offset, prefix, out _))
			offset++;
		if (!EbmlReader.TryRead (doc.Data, offset, prefix, out var element))
			return false;
		if (element.Id != MatroskaIds.Crc32 || element.UnknownSize || element.DataLength != 4)
			return false;
		payload = element.DataOffset;
		covered = element.End;
		return true;
	}

	static void WriteCluster (
		Stream source,
		Stream destination,
		ClusterState state,
		byte[] chunk,
		ref uint leading,
		int coveredAt,
		ref int bodyPos,
		bool feed)
	{
		if (state.Length == 0)
			return;

		byte[]? crcBytes = null;
		if (state.CrcDirty && state.CrcPayload >= 0) {
			var clusterCrc = HashCluster (source, state, chunk);
			crcBytes = new byte[4];
			crcBytes[0] = (byte)clusterCrc;
			crcBytes[1] = (byte)(clusterCrc >> 8);
			crcBytes[2] = (byte)(clusterCrc >> 16);
			crcBytes[3] = (byte)(clusterCrc >> 24);
		}

		if (crcBytes is null && state.Overlays.Count == 0 && !feed) {
			source.Position = state.Origin;
			StreamCopy.Copy (source, destination, state.Length, chunk);
			bodyPos += state.Length;
			return;
		}

		source.Position = state.Origin;
		var index = 0;
		while (index < state.Length) {
			var want = Math.Min (StreamCopy.ChunkSize, state.Length - index);
			ReadWindow (source, chunk, want);
			ApplyOverlays (chunk, index, want, state.Overlays);
			if (crcBytes is not null)
				ApplyOne (chunk, index, want, state.CrcPayload, crcBytes);
			if (feed)
				Feed (chunk, want, bodyPos + index, coveredAt, ref leading);
			destination.Write (chunk, 0, want);
			index += want;
		}

		bodyPos += state.Length;
	}

	static uint HashCluster (Stream source, ClusterState state, byte[] chunk)
	{
		var crc = 0xFFFFFFFFu;
		source.Position = state.Origin;
		var index = 0;
		while (index < state.Length) {
			var want = Math.Min (StreamCopy.ChunkSize, state.Length - index);
			ReadWindow (source, chunk, want);
			ApplyOverlays (chunk, index, want, state.Overlays);
			var start = index > state.CrcCovered ? index : state.CrcCovered;
			var end = index + want;
			if (start < end)
				crc = UpdateCrc (crc, chunk, start - index, end - start);
			index += want;
		}

		return ~crc;
	}

	static void ReadWindow (Stream source, byte[] chunk, int want)
	{
		var got = 0;
		while (got < want) {
			var n = source.Read (chunk, got, want - got);
			if (n <= 0)
				throw new EndOfStreamException ("The source ended before the requested bytes were copied");
			got += n;
		}
	}

	static void ApplyOverlays (byte[] window, int windowStart, int read, List<Overlay> overlays)
	{
		foreach (var overlay in overlays)
			ApplyOne (window, windowStart, read, overlay.At, overlay.Bytes);
	}

	static void ApplyOne (byte[] window, int windowStart, int read, int at, byte[] bytes)
	{
		var start = windowStart > at ? windowStart : at;
		var end = windowStart + read;
		var overlayEnd = at + bytes.Length;
		if (overlayEnd < end)
			end = overlayEnd;
		if (start >= end)
			return;
		Buffer.BlockCopy (bytes, start - at, window, start - windowStart, end - start);
	}

	static void Feed (byte[] data, int bodyAt, int covered, ref uint crc) =>
		Feed (data, data.Length, bodyAt, covered, ref crc);

	static void Feed (byte[] data, int length, int bodyAt, int covered, ref uint crc)
	{
		var start = covered > bodyAt ? covered - bodyAt : 0;
		if (start >= length)
			return;
		crc = UpdateCrc (crc, data, start, length - start);
	}

	static uint UpdateCrc (uint crc, byte[] data, int offset, int count)
	{
		var end = offset + count;
		for (var i = offset; i < end; i++)
			crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
		return crc;
	}

	internal sealed class StreamPlan
	{
		internal StreamPlan (Document document, byte[]? size)
		{
			Document = document;
			Size = size;
		}

		internal Document Document { get; }

		internal byte[]? Size { get; }
	}

	internal sealed class Document
	{
		public Document (
			Layout layout,
			byte[] data,
			ClusterState?[] clusters,
			byte[] head,
			byte[] sizeField,
			long tailOrigin,
			long tailLength,
			long fileLength)
		{
			Layout = layout;
			Data = data;
			Clusters = clusters;
			Head = head;
			SizeField = sizeField;
			TailOrigin = tailOrigin;
			TailLength = tailLength;
			FileLength = fileLength;
			SegmentPosition = layout.SegmentPosition;
			IdWidth = layout.IdWidth;
			SizeWidth = layout.SizeWidth;
			UnknownSize = layout.UnknownSize;
		}

		public Layout Layout { get; }

		public byte[] Data { get; }

		public ClusterState?[] Clusters { get; }

		public byte[] Head { get; set; }

		public byte[] SizeField { get; set; }

		public long TailOrigin { get; }

		public long TailLength { get; }

		public long FileLength { get; }

		public int SegmentPosition { get; set; }

		public int IdWidth { get; }

		public int SizeWidth { get; }

		public bool UnknownSize { get; }

		public bool Changed { get; set; }

		public Document With (Layout layout, byte[] data, ClusterState?[] clusters)
		{
			var next = new Document (layout, data, clusters, Head, SizeField, TailOrigin, TailLength, FileLength) {
				SegmentPosition = SegmentPosition,
				Changed = true
			};
			return next;
		}
	}

	internal sealed class ClusterState
	{
		public long Origin { get; set; }

		public int Length { get; set; }

		public bool UnknownSize { get; set; }

		public int CrcPayload { get; set; } = -1;

		public int CrcCovered { get; set; } = -1;

		public bool CrcDirty { get; set; }

		public List<Spot> Spots { get; set; } = new ();

		public List<Overlay> Overlays { get; set; } = new ();

		public ClusterState Clone () => new () {
			Origin = Origin,
			Length = Length,
			UnknownSize = UnknownSize,
			CrcPayload = CrcPayload,
			CrcCovered = CrcCovered,
			CrcDirty = CrcDirty,
			Spots = new List<Spot> (Spots),
			Overlays = new List<Overlay> (Overlays)
		};
	}

	internal readonly struct Spot
	{
		public Spot (int elementAt, int elementLength, int dataAt, int dataLength, ulong value)
		{
			ElementAt = elementAt;
			ElementLength = elementLength;
			DataAt = dataAt;
			DataLength = dataLength;
			Value = value;
		}

		public int ElementAt { get; }

		public int ElementLength { get; }

		public int DataAt { get; }

		public int DataLength { get; }

		public ulong Value { get; }
	}

	internal readonly struct Overlay
	{
		public Overlay (int at, byte[] bytes)
		{
			At = at;
			Bytes = bytes;
		}

		public int At { get; }

		public byte[] Bytes { get; }
	}

	sealed class Draft : IDisposable
	{
		readonly MemoryStream _meta = new ();
		readonly List<Segment> _segments = new ();
		readonly List<Mark> _marks = new ();
		int _at;

		public int At => _at;

		public List<Segment> Segments => _segments;

		public int SeekHeadCount {
			get {
				var count = 0;
				foreach (var mark in _marks) {
					if (mark.Id == MatroskaIds.SeekHead)
						count++;
				}

				return count;
			}
		}

		public List<Mark> Heads {
			get {
				var heads = new List<Mark> ();
				foreach (var mark in _marks) {
					if (mark.Id == MatroskaIds.SeekHead)
						heads.Add (mark);
				}

				return heads;
			}
		}

		public List<Mark> Cues {
			get {
				var cues = new List<Mark> ();
				foreach (var mark in _marks) {
					if (mark.Id == MatroskaIds.Cues)
						cues.Add (mark);
				}

				return cues;
			}
		}

		public byte[] BytesOf (Mark mark) => _segments[mark.SegmentIndex].Bytes!;

		public bool TryAdd (byte[] bytes, bool note, ulong id, int index, bool final, out string error)
		{
			error = "";
			if (bytes.Length > int.MaxValue - _at) {
				error = "Matroska segment is too large to rewrite";
				return false;
			}

			var start = _at;
			if (bytes.Length > 0)
				_meta.Write (bytes, 0, bytes.Length);
			_at += bytes.Length;
			if (note && (id == MatroskaIds.SeekHead || id == MatroskaIds.Cues))
				_marks.Add (new Mark (id, start, _at, index, final));
			return true;
		}

		public bool TryAddCluster (ClusterState state, int sourceIndex, out string error)
		{
			error = "";
			Flush ();
			if (state.Length > int.MaxValue - _at) {
				error = "Matroska segment is too large to rewrite";
				return false;
			}

			_segments.Add (new Segment { Cluster = state, SourceIndex = sourceIndex, BodyStart = _at });
			_at += state.Length;
			return true;
		}

		public bool Finish (out string error)
		{
			error = "";
			Flush ();
			foreach (var mark in _marks) {
				if (mark.SegmentIndex < 0) {
					error = "Matroska segment cannot be bounded";
					return false;
				}
			}

			return true;
		}

		public void Dispose () => _meta.Dispose ();

		void Flush ()
		{
			if (_meta.Length == 0)
				return;
			var bytes = _meta.ToArray ();
			var bodyStart = _at - bytes.Length;
			var segment = new Segment { Bytes = bytes, BodyStart = bodyStart, SourceIndex = -1 };
			_segments.Add (segment);
			var segmentIndex = _segments.Count - 1;
			foreach (var mark in _marks) {
				if (mark.SegmentIndex >= 0 || mark.AbsStart < bodyStart || mark.AbsStart >= _at)
					continue;
				mark.SegmentIndex = segmentIndex;
				mark.LocalStart = mark.AbsStart - bodyStart;
				mark.LocalEnd = mark.AbsEnd - bodyStart;
			}

			_meta.SetLength (0);
		}
	}

	sealed class Segment
	{
		public byte[]? Bytes { get; set; }

		public ClusterState? Cluster { get; set; }

		public int SourceIndex { get; set; }

		public int BodyStart { get; set; }
	}

	sealed class Mark
	{
		public Mark (ulong id, int absStart, int absEnd, int pieceIndex, bool final)
		{
			Id = id;
			AbsStart = absStart;
			AbsEnd = absEnd;
			PieceIndex = pieceIndex;
			Final = final;
			SegmentIndex = -1;
		}

		public ulong Id { get; }

		public int AbsStart { get; }

		public int AbsEnd { get; }

		public int PieceIndex { get; }

		public bool Final { get; }

		public int SegmentIndex { get; set; }

		public int LocalStart { get; set; }

		public int LocalEnd { get; set; }
	}

	sealed class Reader
	{
		readonly Stream _stream;
		readonly long _length;
		readonly byte[] _chunk;
		readonly byte[] _probe = new byte[12];

		public Reader (Stream stream, long length, byte[] chunk)
		{
			_stream = stream;
			_length = length;
			_chunk = chunk;
		}

		public long Length => _length;

		public bool TryElement (long pos, long limit, out ulong id, out int headerLength, out long dataLength, out long elementEnd)
		{
			id = 0;
			headerLength = 0;
			dataLength = -1;
			elementEnd = -1;
			if (pos < 0 || pos >= limit || pos >= _length)
				return false;
			if (!TryPeek (pos, out id, out headerLength, out dataLength))
				return false;
			if (dataLength < 0)
				return true;

			var end = pos + headerLength + dataLength;
			if (end < pos || end > limit)
				return false;
			elementEnd = end;
			return true;
		}

		public bool TryBound (long offset, long parentEnd, out long end)
		{
			end = offset;
			while (offset < parentEnd) {
				if (!TryElement (offset, parentEnd, out var id, out _, out var dataLength, out var elementEnd))
					return false;
				if (MatroskaIds.IsSegmentChild (id)) {
					end = offset;
					return true;
				}

				if (dataLength < 0)
					return false;
				offset = elementEnd;
			}

			end = parentEnd;
			return true;
		}

		public long EndAtNextTopLevel (long offset)
		{
			while (offset < _length) {
				if (!TryElement (offset, _length, out var id, out var headerLength, out var dataLength, out var elementEnd))
					return _length;
				if (id == MatroskaIds.Ebml || id == MatroskaIds.Segment)
					return offset;
				if (dataLength < 0) {
					if (!TrySkipUnknown (offset + headerLength, out var next))
						return _length;
					offset = next;
					continue;
				}

				offset = elementEnd;
			}

			return _length;
		}

		public byte[] ReadExact (long pos, int count)
		{
			if (count == 0)
				return [];
			var dest = new byte[count];
			_stream.Position = pos;
			StreamCopy.Read (_stream, dest, 0, count, _chunk);
			return dest;
		}

		public void ScanCluster (ClusterState state, long origin, int headerLength, long clusterEnd)
		{
			var child = origin + headerLength;
			var first = true;
			while (child < clusterEnd) {
				if (!TryElement (child, clusterEnd, out var id, out var childHeader, out var dataLength, out var childEnd))
					break;
				if (dataLength < 0)
					break;

				var relative = child - origin;
				var childLength = childEnd - child;
				if (relative < 0 || childLength < 0 || relative > int.MaxValue || childLength > int.MaxValue)
					break;
				if (first && id == MatroskaIds.Crc32 && dataLength == 4) {
					var payload = relative + childHeader;
					if (payload <= int.MaxValue && childEnd - origin <= int.MaxValue) {
						state.CrcPayload = (int)payload;
						state.CrcCovered = (int)(childEnd - origin);
					}
				}

				if (id == MatroskaIds.Position && dataLength <= 8) {
					ulong value = 0;
					if (dataLength > 0) {
						var raw = ReadExact (child + childHeader, (int)dataLength);
						value = EbmlReader.ReadUInt (raw);
					}

					state.Spots.Add (new Spot ((int)relative, (int)childLength, (int)(relative + childHeader), (int)dataLength, value));
				}

				first = false;
				child = childEnd;
			}
		}

		bool TrySkipUnknown (long offset, out long next)
		{
			next = offset;
			while (offset < _length) {
				if (!TryElement (offset, _length, out var id, out _, out var dataLength, out var elementEnd))
					return false;
				if (MatroskaIds.IsSegmentChild (id) || id == MatroskaIds.Ebml || id == MatroskaIds.Segment) {
					next = offset;
					return true;
				}

				if (dataLength < 0)
					return false;
				offset = elementEnd;
			}

			next = _length;
			return false;
		}

		bool TryPeek (long pos, out ulong id, out int headerLength, out long dataLength)
		{
			id = 0;
			headerLength = 0;
			dataLength = -1;
			var available = _length - pos;
			if (available <= 0)
				return false;
			var want = available > 12 ? 12 : (int)available;
			_stream.Position = pos;
			var got = 0;
			while (got < want) {
				var n = _stream.Read (_probe, got, want - got);
				if (n <= 0)
					break;
				got += n;
			}

			return got > 0 && EbmlReader.TryPeekHeader (_probe.AsSpan (0, got), out id, out headerLength, out dataLength);
		}
	}
}
