// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// A <c>FlagEnabled</c> value the caller set on one track, with the identity that track had when it was read.
/// </summary>
internal readonly struct TrackFlagEdit
{
	public TrackFlagEdit (int ordinal, ulong trackNumber, ulong trackUid, bool enabled)
	{
		Ordinal = ordinal;
		TrackNumber = trackNumber;
		TrackUid = trackUid;
		Enabled = enabled;
	}

	public int Ordinal { get; }

	public ulong TrackNumber { get; }

	public ulong TrackUid { get; }

	public bool Enabled { get; }
}

static partial class MatroskaRewrite
{
	/// <summary>A <c>FlagEnabled</c> element with a 1-octet size and a 1-octet value.</summary>
	const int FlagElementLength = 3;

	/// <summary>
	/// Writes <c>TrackEntry\FlagEnabled</c> (RFC 9559 §5.1.4.1.4) for each edit whose stored value differs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A stored element of 1 to 8 octets is rewritten in place. An empty or missing element becomes a
	/// 3-octet element. The <c>TrackEntry</c> and <c>Tracks</c> sizes grow to hold it and keep their
	/// octet width when the new size fits (RFC 8794 §6.1).
	/// </para>
	/// <para>
	/// The growth comes out of the <c>Void</c> (RFC 8794 §11.3.2) right after <c>Tracks</c> when it has room,
	/// so no element moves and no segment position changes (RFC 9559 §6.1, §16). Otherwise the first
	/// known-size Tracks grows through <see cref="TryGrow"/>: everything after it moves once, and the
	/// SeekHead, Cues, Cluster <c>Position</c>, and CRC-32 values follow. Another Tracks without room fails
	/// the save, since only one is allowed (RFC 9559 §5.1.4, maxOccurs 1).
	/// </para>
	/// <para>
	/// FlagEnabled has minver 2, and DocTypeVersion must be at least the highest version of any element
	/// present (RFC 9559 section 7). When an element is added, a stored DocTypeVersion below 2 is raised,
	/// and an empty or missing one (which means 1, RFC 8794 sections 6.1 and 11.2.7) is written as 2.
	/// The EBML header is outside the Segment, so this moves no Segment Position (RFC 9559 section 16).
	/// </para>
	/// </remarks>
	/// <param name="data">The whole file.</param>
	/// <param name="edits">The tracks the caller set, by ordinal.</param>
	/// <param name="updated">The rewritten file, or null when every stored flag already matches.</param>
	/// <param name="error">Why the flags could not be written.</param>
	public static bool TryWriteTrackFlags (ReadOnlySpan<byte> data, IReadOnlyList<TrackFlagEdit> edits, out byte[]? updated, out string error)
	{
		updated = null;
		if (!TryParse (data, out var layout, out error) || layout is null)
			return false;

		var byOrdinal = new Dictionary<int, int> (edits.Count);
		for (var i = 0; i < edits.Count; i++)
			byOrdinal[edits[i].Ordinal] = i;

		var pieces = layout.Pieces;
		var found = new bool[edits.Count];
		var replacements = new byte[]?[pieces.Count];
		var ordinal = 0;
		var inserted = false;
		var any = false;
		for (var i = 0; i < pieces.Count; i++) {
			if (pieces[i].Id != MatroskaIds.Tracks)
				continue;
			if (!TryEditTracks (data, pieces[i], edits, byOrdinal, found, ref ordinal, ref inserted, out replacements[i], out error))
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
		if (!TryBuildInPlace (data, layout, replacements, growable, out var body, out var grow, out var inPlace, out error))
			return false;

		if (inPlace) {
			UpdateLeadingCrc (body);
			updated = Assemble (data, layout, body, out error);
			if (updated.Length == 0 || error.Length > 0) {
				updated = null;
				return false;
			}
		}

		if (grow is not null) {
			if (!TryGrow (updated ?? data, grow, out var grown, out error)) {
				updated = null;
				return false;
			}

			updated = grown;
		}

		if (inserted && updated is not null)
			updated = RaiseDocTypeVersion (updated, 2);
		return true;
	}

	/// <summary>
	/// Walks the <c>TrackEntry</c> children the way <see cref="MatroskaFile"/> reads them, so ordinals match.
	/// </summary>
	static bool TryEditTracks (
		ReadOnlySpan<byte> data,
		Piece piece,
		IReadOnlyList<TrackFlagEdit> edits,
		Dictionary<int, int> byOrdinal,
		bool[] found,
		ref int ordinal,
		ref bool inserted,
		out byte[]? replaced,
		out string error)
	{
		replaced = null;
		error = "";
		var end = piece.Offset + piece.Length;
		if (!EbmlReader.TryRead (data, piece.Offset, end, out var tracks)) {
			error = "Matroska Tracks cannot be bounded";
			return false;
		}

		var childrenEnd = tracks.UnknownSize ? end : tracks.End;
		List<(int Start, int End, byte[] Bytes)>? changes = null;
		var offset = tracks.DataOffset;
		while (offset < childrenEnd) {
			if (!EbmlReader.TryRead (data, offset, childrenEnd, out var child))
				break;
			if (child.Id == MatroskaIds.TrackEntry && !child.UnknownSize) {
				var here = ordinal++;
				if (byOrdinal.TryGetValue (here, out var index)) {
					if (!TryEditEntry (data, child, edits[index], ref inserted, out var entry, out error))
						return false;
					found[index] = true;
					if (entry is not null)
						(changes ??= new List<(int, int, byte[])> ()).Add ((child.Position, child.End, entry));
				}
			}

			if (child.UnknownSize)
				break;
			offset = child.End;
		}

		if (changes is null)
			return true;

		using var payload = new BinaryDataBuilder (childrenEnd - tracks.DataOffset + FlagElementLength * changes.Count);
		var at = tracks.DataOffset;
		foreach (var change in changes) {
			payload.Add (data.Slice (at, change.Start - at));
			payload.Add (change.Bytes);
			at = change.End;
		}

		payload.Add (data.Slice (at, childrenEnd - at));
		if (!TryWrap (data, tracks, payload.Span, out replaced)) {
			error = "Matroska Tracks size cannot be written";
			return false;
		}

		UpdateCrcAt (replaced);
		return true;
	}

	/// <summary>Returns the rewritten <c>TrackEntry</c>, or null when the stored flag already matches.</summary>
	static bool TryEditEntry (ReadOnlySpan<byte> data, EbmlElement entry, TrackFlagEdit edit, ref bool inserted, out byte[]? replaced, out string error)
	{
		replaced = null;
		error = "";
		ulong number = 0;
		ulong uid = 0;
		var stored = true;
		var flags = new List<EbmlElement> (1);
		var offset = entry.DataOffset;
		while (offset < entry.End) {
			if (!EbmlReader.TryRead (data, offset, entry.End, out var child) || child.UnknownSize) {
				error = "Matroska TrackEntry cannot be bounded";
				return false;
			}

			var payload = data.Slice (child.DataOffset, (int)child.DataLength);
			if (child.Id == MatroskaIds.TrackNumber)
				number = EbmlReader.ReadUInt (payload);
			else if (child.Id == MatroskaIds.TrackUid)
				uid = EbmlReader.ReadUInt (payload);
			else if (child.Id == MatroskaIds.FlagEnabled) {
				stored = MatroskaFile.IsFlagSet (payload);
				flags.Add (child);
			}

			offset = child.End;
		}

		if (number != edit.TrackNumber || uid != edit.TrackUid) {
			error = "Matroska track " + edit.TrackNumber.ToString (System.Globalization.CultureInfo.InvariantCulture)
				+ " is not in the data being saved";
			return false;
		}

		if (stored == edit.Enabled)
			return true;

		// An unsigned integer is 0 to 8 octets (RFC 8794 §7.2). Every copy gets the value, so no reader sees the old one.
		var value = edit.Enabled ? (byte)1 : (byte)0;
		using var body = new BinaryDataBuilder ((int)entry.DataLength + FlagElementLength);
		var at = entry.DataOffset;
		foreach (var flag in flags) {
			if (flag.DataLength > 8) {
				error = "Matroska FlagEnabled is longer than 8 octets";
				return false;
			}

			body.Add (data.Slice (at, flag.Position - at));
			if (flag.DataLength == 0)
				body.Add (Flag (value));
			else {
				body.Add (data.Slice (flag.Position, flag.HeaderLength));
				var bytes = new byte[flag.DataLength];
				bytes[bytes.Length - 1] = value;
				body.Add (bytes);
			}

			at = flag.End;
		}

		body.Add (data.Slice (at, entry.End - at));
		if (flags.Count == 0) {
			body.Add (Flag (value));
			inserted = true;
		}

		if (!TryWrap (data, entry, body.Span, out replaced)) {
			error = "Matroska TrackEntry size cannot be written";
			return false;
		}

		UpdateCrcAt (replaced);
		return true;
	}

	static byte[] Flag (byte value) => new byte[] { (byte)MatroskaIds.FlagEnabled, 0x81, value };

	/// <summary>
	/// Puts the original ID in front of <paramref name="payload"/>. A known size keeps its width when it fits.
	/// An unknown size stays unknown, since its end is the next sibling.
	/// </summary>
	static bool TryWrap (ReadOnlySpan<byte> data, EbmlElement element, ReadOnlySpan<byte> payload, out byte[] wrapped)
	{
		wrapped = [];
		if (!EbmlReader.TryReadVint (data, element.Position, element.DataOffset, true, 4, out _, out var idWidth, out _))
			return false;

		byte[] size;
		if (element.UnknownSize)
			size = data.Slice (element.Position + idWidth, element.HeaderLength - idWidth).ToArray ();
		else {
			size = EncodeFittedSize ((ulong)payload.Length, element.HeaderLength - idWidth);
			if (size.Length == 0)
				return false;
		}

		wrapped = new byte[idWidth + size.Length + payload.Length];
		data.Slice (element.Position, idWidth).CopyTo (wrapped);
		size.CopyTo (wrapped.AsSpan (idWidth));
		payload.CopyTo (wrapped.AsSpan (idWidth + size.Length));
		return true;
	}

	/// <summary>
	/// Recomputes the CRC-32 of the master element at the start of <paramref name="element"/>.
	/// An unknown-size master's data runs to the end of the array.
	/// </summary>
	static void UpdateCrcAt (byte[] element)
	{
		if (!EbmlReader.TryRead (element, 0, element.Length, out var master))
			return;
		var dataEnd = master.UnknownSize ? element.Length : master.End;
		if (!EbmlReader.TryRead (element, master.DataOffset, dataEnd, out var first))
			return;
		if (first.Id != MatroskaIds.Crc32 || first.UnknownSize || first.DataLength != 4)
			return;
		WriteCrc (element, first.DataOffset, first.End, dataEnd);
	}

	/// <summary>
	/// Lays out the segment with the edited <c>Tracks</c> where they were. A Tracks that grew takes the room
	/// from the Void right after it, so no element changes its segment position (RFC 9559 §16) and the
	/// SeekHead, Cues, and clusters stay as they are. A Void can shrink to nothing or to at least 2 octets.
	/// The Tracks at <paramref name="growable"/> that has no such room keeps its old bytes here and comes back
	/// as <paramref name="grow"/>, for <see cref="TryGrow"/> to move the later elements.
	/// <paramref name="inPlace"/> is false when that Tracks is the only change.
	/// </summary>
	static bool TryBuildInPlace (
		ReadOnlySpan<byte> data,
		Layout layout,
		byte[]?[] replacements,
		int growable,
		out byte[] body,
		out byte[]? grow,
		out bool inPlace,
		out string error)
	{
		body = [];
		grow = null;
		inPlace = false;
		error = "";
		var pieces = layout.Pieces;
		using var builder = new BinaryDataBuilder (layout.SegmentEnd - layout.DataOffset);
		for (var i = 0; i < pieces.Count; i++) {
			var piece = pieces[i];
			if (replacements[i] is not { } tracks) {
				builder.Add (data.Slice (piece.Offset, piece.Length));
				continue;
			}

			var extra = tracks.Length - piece.Length;
			var room = i + 1 < pieces.Count && pieces[i + 1].Id == MatroskaIds.Void && !pieces[i + 1].UnknownSize
				? pieces[i + 1].Length
				: 0;
			if (extra > 0 && room != extra && room - extra < 2) {
				if (i != growable) {
					var octets = extra.ToString (System.Globalization.CultureInfo.InvariantCulture);
					error = piece.UnknownSize
						? "Matroska FlagEnabled needs " + octets + " more octets, but Tracks has an unknown size and no Void right after it"
						: "Matroska FlagEnabled needs " + octets + " more octets in a second Tracks and no Void right after Tracks can give them";
					return false;
				}

				builder.Add (data.Slice (piece.Offset, piece.Length));
				grow = tracks;
				continue;
			}

			builder.Add (tracks);
			inPlace = true;
			if (extra == 0)
				continue;

			if (room > extra && TryMakeVoid (room - extra, out var shrunk))
				builder.Add (shrunk);
			i++;
		}

		body = builder.ToArray ();
		return true;
	}

	/// <summary>
	/// Makes the first EBML header's <c>DocTypeVersion</c> at least <paramref name="version"/>. A stored value
	/// is rewritten in place; an empty or missing one is written as a new 1-octet element and the header
	/// is rebuilt, its size widening if it must, and its CRC-32 recomputed. DocTypeVersion has maxOccurs 1
	/// (RFC 8794 section 11.2.7), but every copy below the target is raised, as every FlagEnabled copy is
	/// written, so no reader keeps an old value. A header that cannot be read is left as it is.
	/// </summary>
	static byte[] RaiseDocTypeVersion (byte[] file, ulong version)
	{
		if (!EbmlReader.TryRead (file, 0, file.Length, out var header) || header.Id != MatroskaIds.Ebml || header.UnknownSize)
			return file;
		var element = EbmlWriter.Unsigned (MatroskaIds.DocTypeVersion, version).ToArray ();
		var children = new List<EbmlElement> ();
		var insertAfter = -1;
		var seen = false;
		var offset = header.DataOffset;
		while (offset < header.End) {
			if (!EbmlReader.TryRead (file, offset, header.End, out var child) || child.UnknownSize)
				return file;
			if (child.Id == MatroskaIds.DocTypeVersion)
				seen = true;
			else if (child.Id == MatroskaIds.DocType)
				insertAfter = children.Count;
			children.Add (child);
			offset = child.End;
		}

		// Stored copies that are too low are raised in place. Empty copies, or none at all, need a rebuild.
		var rebuild = !seen;
		var raised = false;
		foreach (var child in children) {
			if (child.Id != MatroskaIds.DocTypeVersion || child.DataLength > 8)
				continue;
			if (child.DataLength == 0) {
				rebuild = true;
				continue;
			}

			var value = EbmlReader.ReadUInt (file.AsSpan (child.DataOffset, (int)child.DataLength));
			if (value < version && TryWriteUnsigned (file, child.DataOffset, (int)child.DataLength, version))
				raised = true;
		}

		if (!rebuild) {
			if (raised)
				UpdateElementCrc (file, header.Position, header.End);
			return file;
		}

		using var payload = new BinaryDataBuilder ((int)header.DataLength + element.Length);
		for (var i = 0; i < children.Count; i++) {
			var child = children[i];
			if (child.Id == MatroskaIds.DocTypeVersion && child.DataLength == 0)
				payload.Add (element);
			else
				payload.Add (file.AsSpan (child.Position, child.End - child.Position));
			if (!seen && i == insertAfter)
				payload.Add (element);
		}

		if (!seen && insertAfter < 0)
			payload.Add (element);
		if (!EbmlReader.TryReadVint (file, 0, header.DataOffset, true, 4, out _, out var idWidth, out _))
			return file;
		var size = EncodeFittedSize ((ulong)payload.Length, header.HeaderLength - idWidth);
		if (size.Length == 0)
			return file;

		var grown = new byte[(long)file.Length - (header.End - header.Position) + idWidth + size.Length + payload.Length];
		file.AsSpan (0, idWidth).CopyTo (grown);
		size.CopyTo (grown.AsSpan (idWidth));
		payload.Span.CopyTo (grown.AsSpan (idWidth + size.Length));
		file.AsSpan (header.End).CopyTo (grown.AsSpan (idWidth + size.Length + payload.Length));
		UpdateElementCrc (grown, 0, idWidth + size.Length + payload.Length);
		return grown;
	}
}
