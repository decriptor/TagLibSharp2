// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Reads and writes MP4 chapter lists: the Nero <c>chpl</c> box and the QuickTime chapter track.
/// </summary>
/// <remarks>
/// <para>
/// Chapter track: a track's <c>tref</c> box holds a <c>chap</c> reference naming the chapter track's
/// track_ID (ISO 14496-12 §8.3.3; QuickTime File Format, "Track Reference Atoms", type <c>chap</c>).
/// That track has a <c>text</c> handler. Each sample is one title: a 16-bit big-endian byte length, then the
/// text, UTF-8 or UTF-16 with a byte order mark, then optional modifier boxes (3GPP TS 26.245 §5.17;
/// QuickTime File Format, "Text Media"). Sample times come from <c>stts</c> in the <c>mdhd</c> timescale
/// (§8.4.2, §8.6.1.2); sample positions from <c>stsc</c>, <c>stsz</c>, and <c>stco</c>/<c>co64</c>
/// (§8.7.3 to §8.7.5). An edit list (§8.6.6) shifts the track onto the movie timeline.
/// </para>
/// <para>
/// Nero <c>chpl</c> in <c>moov/udta</c> has no published specification. The layout here is the one in files
/// written by ffmpeg and read back by ffprobe and MediaInfo: FullBox version 1 and flags 0, 4 reserved bytes,
/// an 8-bit count, then per entry a 64-bit start in 100 ns units, an 8-bit byte length, and UTF-8 text.
/// A file with 300 chapters gets 255 entries, so the count is one byte.
/// </para>
/// </remarks>
internal static class Mp4ChapterCodec
{
	/// <summary>The most entries an 8-bit chpl count can hold.</summary>
	internal const int MaxChplEntries = 255;

	/// <summary>The longest chpl title, in UTF-8 bytes.</summary>
	internal const int MaxChplTitleBytes = 255;

	/// <summary>The longest text a chapter sample can hold behind its 16-bit length.</summary>
	internal const int MaxSampleTextBytes = ushort.MaxValue;

	/// <summary>Chapter samples read from one track. Bounds a constant-size stsz that declares no table.</summary>
	internal const int MaxTrackChapters = 65_535;

	const int SampleReadLimit = 2 + MaxSampleTextBytes;

	/// <summary>The save failure for a chapter change on a fragmented movie that has a chapter track.</summary>
	internal const string FragmentedChapterTrackError =
		"Cannot write MP4 chapters: the file is a fragmented movie whose chapter track has, or may have, samples "
		+ "in movie fragments (moof/traf). Rewriting that track in moov would leave the fragment samples as a "
		+ "second chapter list. Write the file unfragmented first.";
	const long TicksPerSecond = TimeSpan.TicksPerSecond;
	const uint DefaultTimescale = 1000;

	// ---- chpl

	/// <summary>
	/// Reads moov/udta/chpl, or null when there is none.
	/// </summary>
	public static List<Mp4Chapter>? ReadChpl (Mp4Box moov)
	{
		var chpl = moov.FindChild ("udta")?.FindChild ("chpl");
		return chpl is null ? null : ParseChpl (chpl.Data.Span);
	}

	/// <summary>
	/// Parses a chpl payload (version and flags first). Version 1 has 4 reserved bytes before the count.
	/// Another version uses the layout, with or without those bytes, that ends exactly at the box end.
	/// An entry that runs past the box ends the list.
	/// </summary>
	public static List<Mp4Chapter> ParseChpl (ReadOnlySpan<byte> payload)
	{
		var reserved = ParseChplEntries (payload, 8, out var exact);
		if (payload.Length > 0 && payload[0] == 1)
			return reserved;
		if (exact)
			return reserved;

		var compact = ParseChplEntries (payload, 4, out var compactExact);
		return compactExact || compact.Count > reserved.Count ? compact : reserved;
	}

	static List<Mp4Chapter> ParseChplEntries (ReadOnlySpan<byte> payload, int countPosition, out bool exact)
	{
		var chapters = new List<Mp4Chapter> ();
		exact = false;
		if (payload.Length <= countPosition)
			return chapters;

		var count = payload[countPosition];
		var pos = countPosition + 1;
		for (var i = 0; i < count; i++) {
			if (payload.Length - pos < 9)
				return chapters;

			var start = BinaryPrimitives.ReadUInt64BigEndian (payload.Slice (pos));
			var length = payload[pos + 8];
			pos += 9;
			if (payload.Length - pos < length)
				return chapters;

			var title = DecodeUtf8 (payload.Slice (pos, length));
			pos += length;
			var ticks = start > (ulong)TimeSpan.MaxValue.Ticks ? TimeSpan.MaxValue.Ticks : (long)start;
			chapters.Add (new Mp4Chapter (TimeSpan.FromTicks (ticks), title));
		}

		exact = pos == payload.Length;
		return chapters;
	}

	/// <summary>
	/// Renders a complete chpl box: version 1, the first 255 chapters, titles cut to 255 UTF-8 bytes.
	/// </summary>
	public static BinaryData RenderChpl (IReadOnlyList<Mp4Chapter> chapters)
	{
		var count = Math.Min (chapters.Count, MaxChplEntries);
		using var body = new BinaryDataBuilder ();
		body.Add ((byte)1);
		body.AddUInt24BE (0);
		body.AddUInt32BE (0);
		body.Add ((byte)count);
		for (var i = 0; i < count; i++) {
			var title = TruncateUtf8 (chapters[i].Title, MaxChplTitleBytes);
			body.AddUInt64BE ((ulong)chapters[i].StartTime.Ticks);
			body.Add ((byte)title.Length);
			body.Add (title);
		}

		return WrapBox ("chpl", body.Span);
	}

	// ---- chapter track: read

	/// <summary>
	/// The chapter track named by the first <c>tref</c>/<c>chap</c> reference that resolves to a text track.
	/// </summary>
	public static ChapterTrack? FindChapterTrack (Mp4Box moov)
	{
		var tracks = FindChapterTracks (moov);
		return tracks.Count > 0 ? tracks[0] : null;
	}

	/// <summary>
	/// Every text track a <c>tref</c>/<c>chap</c> reference names, in reference order. A track_ID names the first
	/// trak that has it. Apple files can name a second text track (chapter URLs) next to the titles.
	/// </summary>
	/// <remarks>
	/// track_IDs are unique (§8.3.2.3), but files that reuse one exist. A chap entry is a track_ID because the
	/// spec says so (§8.3.3), and it resolves here, once, to a trak object. Every other decision in this class
	/// (chapter media, presentation track, replaced or removed trak) is made on trak objects, never on IDs. The
	/// exceptions are structures the spec ties to an ID: tref targets, tfhd (§8.8.7) and trex (§8.8.3).
	/// </remarks>
	/// <param name="moov">The movie box.</param>
	/// <param name="textOnly">False to also return non-text chapter media (Apple chapter image tracks). A track
	/// that holds a chap reference of its own, or a sound track, is a presentation track and is never returned
	/// in that case.</param>
	public static List<ChapterTrack> FindChapterTracks (Mp4Box moov, bool textOnly = true) =>
		FindChapterTracks (moov.Children, textOnly);

	static List<ChapterTrack> FindChapterTracks (IReadOnlyList<Mp4Box> children, bool textOnly)
	{
		var traks = new List<(Mp4Box Trak, uint Id)> ();
		var byId = new Dictionary<uint, Mp4Box> ();
		foreach (var child in children) {
			if (child.Type != "trak" || !TryReadTrackId (child, out var id))
				continue;

			traks.Add ((child, id));
			if (!byId.ContainsKey (id))
				byId[id] = child;
		}

		// Holders are trak objects: a target is a presentation track when that trak holds a chap reference,
		// not when another trak with the same (invalidly shared, §8.3.2.3) track_ID does.
		var holders = new HashSet<Mp4Box> ();
		foreach (var (trak, _) in traks) {
			var tref = trak.FindChild ("tref");
			if (tref is not null && ReadChapterReferences (tref.Data.Span).Count > 0)
				holders.Add (trak);
		}

		var found = new List<ChapterTrack> ();
		var seen = new HashSet<Mp4Box> ();
		foreach (var (trak, _) in traks) {
			var tref = trak.FindChild ("tref");
			if (tref is null)
				continue;

			foreach (var reference in ReadChapterReferences (tref.Data.Span)) {
				// A chap reference names a track_ID, which resolves to the first trak that has it.
				if (!byId.TryGetValue (reference, out var target) || ReferenceEquals (target, trak))
					continue;

				var media = textOnly
					? IsTextHandler (target)
					: !holders.Contains (target) && HandlerType (target) != "soun";
				if (media && seen.Add (target))
					found.Add (new ChapterTrack (target, reference));
			}
		}

		return found;
	}

	/// <summary>
	/// Locates the chapter track's samples: those in its moov sample table, then those in movie fragments
	/// (ISO 14496-12 §8.8.1: fragments add samples to a track that moov declares). Null when there are none.
	/// Each sample's read length stops after the 16-bit length and 65,535 text bytes.
	/// Total read length stops at <paramref name="readBudget"/>, so overlapping samples cannot read more than the file.
	/// </summary>
	/// <param name="track">The chapter track.</param>
	/// <param name="movieTimescale">The mvhd timescale, for the edit list.</param>
	/// <param name="readBudget">The most sample bytes to read in total.</param>
	/// <param name="fragments">Top-level moof boxes in file order, with their file offsets.</param>
	/// <param name="moov">The moov that holds mvex/trex defaults for fragments.</param>
	public static TrackLayout? ReadLayout (
		ChapterTrack track,
		uint movieTimescale,
		long readBudget,
		IReadOnlyList<(Mp4Box Moof, long Start)>? fragments = null,
		Mp4Box? moov = null)
	{
		var mdia = track.Trak.FindChild ("mdia");
		var timescale = ReadMediaTimescale (mdia);
		if (timescale == 0)
			return null;

		var samples = new List<TextSample> ();
		ulong time = 0;
		var budget = readBudget;
		var stbl = mdia?.FindChild ("minf")?.FindChild ("stbl");
		var complete = stbl is not null && AppendTableSamples (stbl, samples, ref time, ref budget);
		if ((complete || stbl is null) && fragments is not null && fragments.Count > 0 && moov is not null)
			AppendFragmentSamples (track.Id, fragments, moov, samples, ref time, ref budget, null);

		return samples.Count == 0 ? null : new TrackLayout (timescale, samples, ReadEdit (track.Trak, movieTimescale));
	}

	/// <summary>
	/// Adds the samples of a moov sample table (§8.7.3 to §8.7.5, §8.6.1.2). False when the read budget or the
	/// sample cap stopped it.
	/// </summary>
	static bool AppendTableSamples (Mp4Box stbl, List<TextSample> samples, ref ulong time, ref long budget)
	{
		var stsz = stbl.FindChild ("stsz");
		var stsc = stbl.FindChild ("stsc");
		var stts = stbl.FindChild ("stts");
		var stco = stbl.FindChild ("stco");
		var co64 = stbl.FindChild ("co64");
		if (stsz is null || stsc is null || (stco is null && co64 is null))
			return true;

		var sizes = ReadSampleSizes (stsz.Data.Span);
		var chunks = co64 is not null ? ReadChunkOffsets (co64.Data.Span, 8) : ReadChunkOffsets (stco!.Data.Span, 4);
		var rows = ReadStscRows (stsc.Data.Span);
		var deltas = stts is null ? new List<SttsEntry> () : ReadTimeToSample (stts.Data.Span);

		var row = 0;
		var sampleIndex = 0;
		var deltaIndex = 0;
		var deltaLeft = deltas.Count > 0 ? deltas[0].Count : 0u;
		for (var chunk = 0; chunk < chunks.Count && sampleIndex < sizes.Count; chunk++) {
			var chunkNumber = (uint)chunk + 1;
			while (row + 1 < rows.Count && rows[row + 1].FirstChunk <= chunkNumber)
				row++;
			var perChunk = rows.Count > 0 && rows[row].FirstChunk <= chunkNumber ? rows[row].SamplesPerChunk : 0u;

			var offset = chunks[chunk];
			for (var k = 0u; k < perChunk && sampleIndex < sizes.Count; k++) {
				while (deltaLeft == 0 && deltaIndex + 1 < deltas.Count) {
					deltaIndex++;
					deltaLeft = deltas[deltaIndex].Count;
				}

				var delta = deltaLeft > 0 ? deltas[deltaIndex].Delta : 0u;
				if (deltaLeft > 0)
					deltaLeft--;

				var size = sizes[sampleIndex];
				if (!TryAdd (samples, offset, size, time, delta, ref budget))
					return false;
				time += delta;
				offset += size;
				sampleIndex++;
			}
		}

		return true;
	}

	static bool TryAdd (List<TextSample> samples, long offset, uint size, ulong start, ulong duration, ref long budget)
	{
		var readLength = (int)Math.Min (size, (uint)SampleReadLimit);
		if (readLength > budget || samples.Count >= MaxTrackChapters)
			return false;

		budget -= readLength;
		samples.Add (new TextSample (offset, size, readLength, start, duration));
		return true;
	}

	/// <summary>
	/// trex defaults for <paramref name="trackId"/> (§8.8.3): sample duration, size, in that order after
	/// track_ID and sample_description_index.
	/// </summary>
	static (uint Duration, uint Size) ReadTrex (Mp4Box moov, uint trackId)
	{
		var mvex = moov.FindChild ("mvex");
		if (mvex is null)
			return (0, 0);

		foreach (var child in mvex.Children) {
			var body = child.Data.Span;
			if (child.Type != "trex" || body.Length < 20 || BinaryPrimitives.ReadUInt32BigEndian (body.Slice (4)) != trackId)
				continue;

			return (BinaryPrimitives.ReadUInt32BigEndian (body.Slice (12)), BinaryPrimitives.ReadUInt32BigEndian (body.Slice (16)));
		}

		return (0, 0);
	}

	/// <summary>
	/// Adds the track's samples from every traf in <paramref name="fragments"/>.
	/// </summary>
	/// <remarks>
	/// tfhd (§8.8.7): flag 0x1 gives base_data_offset, 0x8 and 0x10 default duration and size, 0x020000 makes the
	/// moof the base. Without either base flag, the first traf of a moof starts at the moof and each later traf at
	/// the end of the previous traf's data, so every traf's data length is measured. tfdt (§8.8.12) sets the
	/// decode time. trun (§8.8.8): flag 0x1 data_offset from the base, 0x4 first_sample_flags, then per sample
	/// 0x100 duration, 0x200 size, 0x400 flags, 0x800 composition offset. A run without data_offset continues
	/// where the previous run of the traf ended. A run's sample count is bounded by its bytes when it has
	/// per-sample fields; otherwise its length is count times the default size, with no loop.
	/// Returns false when anything was left unread: a traf or trun it cannot read, an unread box tail, or the
	/// read budget or sample cap. A null <paramref name="trackId"/> only measures the runs into
	/// <paramref name="runs"/> and stops at the first such point; reading skips it and goes on.
	/// </remarks>
	static bool AppendFragmentSamples (
		uint? trackId,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		Mp4Box moov,
		List<TextSample> samples,
		ref ulong time,
		ref long budget,
		Exclusion? runs)
	{
		// Measuring (no track) serves a guard that must fail closed: it stops at the first thing it cannot read.
		// Reading goes on past it, as other readers do, and keeps every sample it can locate.
		var measuring = trackId is null;
		var complete = true;
		var defaults = new Dictionary<uint, (uint Duration, uint Size)> ();
		foreach (var (moof, moofStart) in fragments) {
			// Children past a box that did not parse are unread; a traf could be among them.
			if (moof.UnparsedTail.Length > 0) {
				if (measuring)
					return false;
				complete = false;
			}

			var previousEnd = moofStart;
			var firstTraf = true;
			// After a traf whose data length is unknown, the implicit base of the next one (§8.8.7: the end of the
			// previous traf's data) is unknown too.
			var previousEndKnown = true;
			foreach (var traf in moof.Children) {
				if (traf.Type != "traf")
					continue;

				var trafComplete = traf.UnparsedTail.Length == 0;
				if (!trafComplete) {
					if (measuring)
						return false;
					complete = false;
				}

				if (!TryReadTfhd (traf, firstTraf, moofStart, previousEnd, moov, defaults, out var id, out var tfhdFlags, out var baseOffset, out var defaultDuration, out var defaultSize)) {
					if (measuring)
						return false;
					complete = false;
					firstTraf = false;
					previousEndKnown = false;
					continue;
				}

				if (!firstTraf && !previousEndKnown && (tfhdFlags & 0x020001) == 0) {
					complete = false;
					continue;
				}

				var mine = id == trackId;
				if (mine) {
					var tfdt = Payload (traf.FindChild ("tfdt"));
					if (tfdt.Length >= 8)
						time = tfdt[0] == 1 && tfdt.Length >= 12 ? BinaryPrimitives.ReadUInt64BigEndian (tfdt.Slice (4)) : BinaryPrimitives.ReadUInt32BigEndian (tfdt.Slice (4));
				}

				var runEnd = baseOffset;
				foreach (var trun in traf.Children) {
					if (trun.Type != "trun")
						continue;
					var runStart = runEnd;
					var ok = ReadRun (trun.Data.Span, baseOffset, ref runStart, ref runEnd, defaultDuration, defaultSize, mine, samples, ref time, ref budget);
					if (runs is not null && !runs.Avoids (runStart, runEnd))
						return false;
					if (!ok) {
						if (measuring)
							return false;
						complete = false;
						trafComplete = false;
						break;
					}
				}

				// Auxiliary information (saio, §8.7.9) in a traf is located from the traf's base data offset.
				if (runs is not null && !TryAuxRanges (traf, baseOffset, runs))
					return false;

				previousEnd = runEnd;
				previousEndKnown = trafComplete;
				firstTraf = false;
			}
		}

		return complete;
	}

	/// <summary>
	/// tfhd (§8.8.7): track_ID, the base data offset (explicit, the moof, or the end of the previous traf's
	/// data), and the default sample duration and size (tfhd, else trex). False when a field it needs is cut off.
	/// </summary>
	static bool TryReadTfhd (
		Mp4Box traf,
		bool firstTraf,
		long moofStart,
		long previousEnd,
		Mp4Box moov,
		Dictionary<uint, (uint Duration, uint Size)> defaults,
		out uint id,
		out uint flags,
		out long baseOffset,
		out uint defaultDuration,
		out uint defaultSize)
	{
		id = 0;
		flags = 0;
		baseOffset = 0;
		defaultDuration = 0;
		defaultSize = 0;
		var tfhd = Payload (traf.FindChild ("tfhd"));
		if (tfhd.Length < 8)
			return false;

		flags = BinaryPrimitives.ReadUInt32BigEndian (tfhd) & 0xFFFFFF;
		id = BinaryPrimitives.ReadUInt32BigEndian (tfhd.Slice (4));
		var pos = 8;
		baseOffset = firstTraf || (flags & 0x020000) != 0 ? moofStart : previousEnd;
		if ((flags & 0x1) != 0) {
			if (!TryRead64 (tfhd, ref pos, out var explicitBase))
				return false;
			baseOffset = explicitBase > long.MaxValue / 2 ? long.MaxValue / 2 : (long)explicitBase;
		}

		if ((flags & 0x2) != 0)
			pos += 4;
		if (!defaults.TryGetValue (id, out var trex)) {
			trex = ReadTrex (moov, id);
			defaults[id] = trex;
		}

		defaultDuration = trex.Duration;
		defaultSize = trex.Size;
		if ((flags & 0x8) != 0 && !TryRead32 (tfhd, ref pos, out defaultDuration))
			return false;
		return (flags & 0x10) == 0 || TryRead32 (tfhd, ref pos, out defaultSize);
	}

	static bool ReadRun (
		ReadOnlySpan<byte> trun,
		long baseOffset,
		ref long start,
		ref long position,
		uint defaultDuration,
		uint defaultSize,
		bool mine,
		List<TextSample> samples,
		ref ulong time,
		ref long budget)
	{
		if (trun.Length < 8)
			return false;

		var flags = BinaryPrimitives.ReadUInt32BigEndian (trun) & 0xFFFFFF;
		var count = BinaryPrimitives.ReadUInt32BigEndian (trun.Slice (4));
		var pos = 8;
		if ((flags & 0x1) != 0) {
			if (trun.Length - pos < 4)
				return false;
			position = baseOffset + BinaryPrimitives.ReadInt32BigEndian (trun.Slice (pos));
			pos += 4;
		}

		start = position;

		if ((flags & 0x4) != 0)
			pos += 4;

		var hasDuration = (flags & 0x100) != 0;
		var hasSize = (flags & 0x200) != 0;
		var entry = (hasDuration ? 4 : 0) + (hasSize ? 4 : 0) + ((flags & 0x400) != 0 ? 4 : 0) + ((flags & 0x800) != 0 ? 4 : 0);
		if (entry == 0) {
			// Every sample takes the defaults: the run's length needs no loop, and only this track's samples
			// are listed (capped).
			if (!mine) {
				var length = (ulong)count * defaultSize;
				position = length > (ulong)(long.MaxValue / 4) || position > long.MaxValue / 4
					? long.MaxValue / 2
					: position + (long)length;
				return true;
			}
		} else if (pos > trun.Length || count > (uint)((trun.Length - pos) / entry)) {
			return false;
		}

		for (var i = 0u; i < count; i++) {
			var duration = defaultDuration;
			var size = defaultSize;
			if (entry > 0) {
				if (hasDuration)
					duration = BinaryPrimitives.ReadUInt32BigEndian (trun.Slice (pos));
				if (hasSize)
					size = BinaryPrimitives.ReadUInt32BigEndian (trun.Slice (pos + (hasDuration ? 4 : 0)));
				pos += entry;
			}

			if (mine && !TryAdd (samples, position, size, time, duration, ref budget))
				return false;
			if (mine)
				time += duration;
			position += size;
		}

		return true;
	}

	/// <summary>
	/// True when every traf run (§8.8.7, §8.8.8) and traf aux-info range (§8.7.9) of every track can be read
	/// and avoids <paramref name="exclusion"/>.
	/// </summary>
	static bool FragmentsAvoid (IReadOnlyList<(Mp4Box Moof, long Start)> fragments, Mp4Box moov, Exclusion exclusion)
	{
		ulong time = 0;
		var budget = 0L;
		return AppendFragmentSamples (null, fragments, moov, [], ref time, ref budget, exclusion);
	}

	/// <summary>
	/// Trak objects, compared by reference (<see cref="Mp4Box"/> keeps reference equality). A pass over every trak
	/// that asks whether each one is in the set makes one lookup per trak, so it stays linear however many traks
	/// the file declares. <see cref="Steps"/> counts the lookups.
	/// </summary>
	internal sealed class TrakSet (IEnumerable<ChapterTrack> tracks)
	{
		readonly HashSet<Mp4Box> _traks = new (tracks.Select (t => t.Trak));

		public long Steps { get; private set; }

		public bool Contains (Mp4Box trak)
		{
			Steps++;
			return _traks.Contains (trak);
		}
	}

	/// <summary>
	/// A cap on steps of work, shared by every pass that the file can make repeat (one per track, one per
	/// sample). <see cref="Steps"/> counts what was taken, for the work-bound tests.
	/// </summary>
	internal sealed class StepBudget (long limit)
	{
		public long Steps { get; private set; }

		/// <summary>Takes one step; false once the limit is spent, and the caller stops.</summary>
		public bool Take ()
		{
			if (Steps >= limit)
				return false;
			Steps++;
			return true;
		}
	}

	/// <summary>
	/// A file range that references must avoid. Each range is tested as it is found and none is stored, so the
	/// work is bounded by the bytes that describe the ranges, not by how many ranges they declare.
	/// </summary>
	internal sealed class Exclusion (long start, long end)
	{
		/// <summary>
		/// How many extents, runs, chunks, and aux offsets were walked. Tests use it to bound the work.
		/// </summary>
		public long Steps { get; private set; }

		/// <summary>Counts one step of a walk that may not call <see cref="Avoids"/>.</summary>
		public void Step () => Steps++;

		/// <summary>True when [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>) avoids the range.</summary>
		public bool Avoids (long rangeStart, long rangeEnd)
		{
			Steps++;
			return rangeEnd <= start || rangeStart >= end;
		}
	}

	/// <summary>
	/// Adds the ranges of the auxiliary information in <paramref name="container"/> (an stbl, or a traf with its
	/// base data offset). saio (§8.7.9) gives one offset per chunk or one for the whole track or traf; saiz
	/// gives the sizes. Each offset is counted with the total aux size after it, which covers every layout.
	/// False when a saio has no saiz, or either does not parse.
	/// </summary>
	static bool TryAuxRanges (Mp4Box container, long baseOffset, Exclusion exclusion)
	{
		foreach (var saio in container.Children) {
			if (saio.Type != "saio")
				continue;

			if (!TryAuxSize (container, out var total))
				return false;

			var body = saio.Data.Span;
			if (body.Length < 8)
				return false;

			var version = body[0];
			var pos = (BinaryPrimitives.ReadUInt32BigEndian (body) & 1) != 0 ? 12 : 4;
			if (!TryReadWidth (body, ref pos, 4, out var count))
				return false;

			var width = version == 0 ? 4 : 8;
			if (count > (ulong)(body.Length - pos) / (ulong)width)
				return false;

			for (var i = 0UL; i < count; i++) {
				TryReadWidth (body, ref pos, width, out var offset);
				if (offset > (ulong)(long.MaxValue / 2) || baseOffset > long.MaxValue / 2)
					return false;

				var start = baseOffset + (long)offset;
				if (total > long.MaxValue - start || !exclusion.Avoids (start, start + Math.Max (total, 1)))
					return false;
			}
		}

		return true;
	}

	/// <summary>
	/// The total size of the auxiliary information saiz (§8.7.8) describes: the default size times the count,
	/// or the sum of the per-sample sizes. False without a saiz, or with one that does not parse.
	/// </summary>
	static bool TryAuxSize (Mp4Box container, out long total)
	{
		total = 0;
		var saiz = container.FindChild ("saiz");
		var body = saiz is null ? ReadOnlySpan<byte>.Empty : saiz.Data.Span;
		var pos = body.Length >= 4 && (BinaryPrimitives.ReadUInt32BigEndian (body) & 1) != 0 ? 12 : 4;
		if (body.Length < pos + 5)
			return false;

		var defaultSize = body[pos];
		var count = BinaryPrimitives.ReadUInt32BigEndian (body.Slice (pos + 1));
		pos += 5;
		if (defaultSize != 0) {
			total = (long)defaultSize * count;
			return true;
		}

		if (count > (ulong)(body.Length - pos))
			return false;
		for (var i = 0; i < count; i++)
			total += body[pos + i];
		return true;
	}

	/// <summary>
	/// True when a traf in <paramref name="fragments"/> belongs, or may belong, to one of <paramref name="trackIds"/>.
	/// </summary>
	static bool HasFragments (IReadOnlyList<(Mp4Box Moof, long Start)> fragments, HashSet<uint> trackIds)
	{
		if (trackIds.Count == 0)
			return false;

		foreach (var (moof, _) in fragments) {
			// An unread moof tail, or a traf whose track it cannot tell, may hold one of these tracks: fail closed.
			if (moof.UnparsedTail.Length > 0)
				return true;

			foreach (var traf in moof.Children) {
				if (traf.Type != "traf")
					continue;

				var tfhd = Payload (traf.FindChild ("tfhd"));
				if (tfhd.Length < 8 || trackIds.Contains (BinaryPrimitives.ReadUInt32BigEndian (tfhd.Slice (4))))
					return true;
			}
		}

		return false;
	}

	static bool TryRead32 (ReadOnlySpan<byte> data, ref int pos, out uint value)
	{
		value = 0;
		if (data.Length - pos < 4)
			return false;
		value = BinaryPrimitives.ReadUInt32BigEndian (data.Slice (pos));
		pos += 4;
		return true;
	}

	static bool TryRead64 (ReadOnlySpan<byte> data, ref int pos, out ulong value)
	{
		value = 0;
		if (data.Length - pos < 8)
			return false;
		value = BinaryPrimitives.ReadUInt64BigEndian (data.Slice (pos));
		pos += 8;
		return true;
	}

	/// <summary>
	/// Decodes one chapter sample: a 16-bit length, then UTF-16 when a byte order mark leads, otherwise UTF-8.
	/// A length past the sample stops at the sample end. Modifier boxes after the text are ignored.
	/// </summary>
	public static string DecodeSample (ReadOnlySpan<byte> sample) => Decode (sample).Text;

	/// <summary>
	/// <see cref="DecodeSample"/>, and whether the text was valid UTF-8, or valid UTF-16 after its byte order
	/// mark (TN2174: "a series of Unicode characters, either in UTF-16 or UTF-8"). Bytes that are not valid
	/// text are not a title: a stale fragment offset can land on media data (ffmpeg +global_sidx output).
	/// The text is then the lossy decoding. Validity is a scan, not a caught exception, so a file of invalid
	/// samples costs one decode per sample.
	/// </summary>
	public static SampleText Decode (ReadOnlySpan<byte> sample)
	{
		if (sample.Length < 2)
			return new SampleText (string.Empty, true);

		var length = Math.Min (BinaryPrimitives.ReadUInt16BigEndian (sample), sample.Length - 2);
		var text = sample.Slice (2, length);
		if (text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF) {
			text = text.Slice (2);
			return new SampleText (Encoding.BigEndianUnicode.GetString (text).TrimEnd ('\0'), IsValidUtf16 (text, bigEndian: true));
		}

		if (text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE) {
			text = text.Slice (2);
			return new SampleText (Encoding.Unicode.GetString (text).TrimEnd ('\0'), IsValidUtf16 (text, bigEndian: false));
		}

		return new SampleText (DecodeUtf8 (text), IsValidUtf8 (text));
	}

	/// <summary>
	/// True when <paramref name="bytes"/> is well-formed UTF-8 (RFC 3629 §4; Unicode §3.9 Table 3-7).
	/// </summary>
	internal static bool IsValidUtf8 (ReadOnlySpan<byte> bytes)
	{
#if NET8_0_OR_GREATER
		return System.Text.Unicode.Utf8.IsValid (bytes);
#else
		return ScanUtf8 (bytes);
#endif
	}

	/// <summary>
	/// A UTF-8 well-formedness scan per Unicode §3.9 Table 3-7: no overlong forms, no surrogates
	/// (ED A0..BF), nothing above U+10FFFF (F4 90..), no stray continuation bytes, no cut-off sequence.
	/// </summary>
	internal static bool ScanUtf8 (ReadOnlySpan<byte> bytes)
	{
		var i = 0;
		while (i < bytes.Length) {
			var b = bytes[i];
			if (b < 0x80) {
				i++;
				continue;
			}

			int count;
			byte low = 0x80, high = 0xBF;
			if (b >= 0xC2 && b <= 0xDF) {
				count = 1;
			} else if (b >= 0xE0 && b <= 0xEF) {
				count = 2;
				if (b == 0xE0)
					low = 0xA0;
				else if (b == 0xED)
					high = 0x9F;
			} else if (b >= 0xF0 && b <= 0xF4) {
				count = 3;
				if (b == 0xF0)
					low = 0x90;
				else if (b == 0xF4)
					high = 0x8F;
			} else {
				return false;
			}

			if (bytes.Length - i <= count)
				return false;
			if (bytes[i + 1] < low || bytes[i + 1] > high)
				return false;
			for (var k = 2; k <= count; k++) {
				if ((bytes[i + k] & 0xC0) != 0x80)
					return false;
			}

			i += count + 1;
		}

		return true;
	}

	/// <summary>
	/// True when <paramref name="bytes"/> is whole UTF-16 code units with every surrogate paired
	/// (a high D800..DBFF followed by a low DC00..DFFF; Unicode §3.9 D91).
	/// </summary>
	internal static bool IsValidUtf16 (ReadOnlySpan<byte> bytes, bool bigEndian)
	{
		if (bytes.Length % 2 != 0)
			return false;

		for (var i = 0; i < bytes.Length; i += 2) {
			var unit = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian (bytes.Slice (i)) : BinaryPrimitives.ReadUInt16LittleEndian (bytes.Slice (i));
			if (unit < 0xD800 || unit > 0xDFFF)
				continue;
			if (unit > 0xDBFF || i + 2 >= bytes.Length)
				return false;

			var next = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian (bytes.Slice (i + 2)) : BinaryPrimitives.ReadUInt16LittleEndian (bytes.Slice (i + 2));
			if (next < 0xDC00 || next > 0xDFFF)
				return false;
			i += 2;
		}

		return true;
	}

	/// <summary>
	/// A decoded chapter sample. <see cref="Valid"/> is false when the bytes were not valid text.
	/// </summary>
	internal readonly record struct SampleText (string Text, bool Valid);

	/// <summary>
	/// Builds the chapter list. The chapter track wins over chpl, as ffprobe reads it. A null title is a
	/// sample outside the file, and an invalid one is not text: chpl wins then. Without chpl, an outside
	/// sample's title is empty and an invalid one keeps its lossy decoding.
	/// When chpl has the same titles in the same order, its 100 ns start times are used: the track starts its
	/// first sample at zero and rounds to its timescale. End times follow the next start, then the track end
	/// (also when chpl supplied the titles), then <paramref name="duration"/>.
	/// </summary>
	public static List<Mp4Chapter> Merge (
		TrackLayout? layout,
		IReadOnlyList<SampleText?>? titles,
		List<Mp4Chapter>? chpl,
		TimeSpan duration)
	{
		var chapters = new List<Mp4Chapter> ();
		TimeSpan? trackEnd = null;
		var hasChpl = chpl is not null && chpl.Count > 0;
		if (layout is not null && titles is not null && titles.Count == layout.Samples.Count && titles.Count > 0
			&& !(hasChpl && titles.Any (t => t is null || !t.Value.Valid))) {
			var useChpl = chpl is not null && chpl.Count == titles.Count;
			for (var i = 0; useChpl && i < titles.Count; i++) {
				if (!string.Equals (chpl![i].Title, ChplTitle (titles[i]!.Value.Text), StringComparison.Ordinal))
					useChpl = false;
			}

			for (var i = 0; i < titles.Count; i++) {
				var start = useChpl ? chpl![i].StartTime : layout.StartOf (i);
				chapters.Add (new Mp4Chapter (start, titles[i]?.Text ?? string.Empty));
			}

		} else if (chpl is not null) {
			chapters.AddRange (chpl);
		}

		// The track's timing holds even when its text does not, so its end still bounds the last chapter.
		if (layout is not null && chapters.Count > 0) {
			var last = layout.Samples[layout.Samples.Count - 1];
			if (last.Duration > 0)
				trackEnd = layout.TimeOf (last.Start + last.Duration);
		}

		return WithEndTimes (SortByStart (chapters), trackEnd ?? duration);
	}

	/// <summary>
	/// A stable sort by start time: chapters that start together keep their order.
	/// </summary>
	public static List<Mp4Chapter> SortByStart (IEnumerable<Mp4Chapter> chapters) =>
		chapters
			.Select ((chapter, index) => (chapter, index))
			.OrderBy (item => item.chapter.StartTime)
			.ThenBy (item => item.index)
			.Select (item => item.chapter)
			.ToList ();

	/// <summary>
	/// Sets each end time to the next start, and the last to <paramref name="end"/> (not before its start).
	/// </summary>
	public static List<Mp4Chapter> WithEndTimes (List<Mp4Chapter> chapters, TimeSpan end)
	{
		var result = new List<Mp4Chapter> (chapters.Count);
		for (var i = 0; i < chapters.Count; i++) {
			var next = i + 1 < chapters.Count ? chapters[i + 1].StartTime : end;
			result.Add (chapters[i].WithEndTime (next));
		}

		return result;
	}

	/// <summary>
	/// Reads the chapter list from <paramref name="moov"/>, its movie fragments, and the whole file that contains them.
	/// </summary>
	public static List<Mp4Chapter> Read (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		ReadOnlySpan<byte> file,
		TimeSpan duration) =>
		Read (moov, fragments, new FileBytes (file), duration);

	internal static List<Mp4Chapter> Read (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		FileBytes file,
		TimeSpan duration)
	{
		var chpl = ReadChpl (moov);
		var track = FindChapterTrack (moov);
		var layout = track is null ? null : ReadLayout (track, ReadMovieTimescale (moov), file.Length, fragments, moov);
		List<SampleText?>? titles = null;
		if (layout is not null) {
			titles = new List<SampleText?> (layout.Samples.Count);
			foreach (var sample in layout.Samples) {
				var inFile = sample.Offset >= 0 && sample.ReadLength <= file.Length - sample.Offset;
				titles.Add (inFile ? Decode (file.Slice (sample.Offset, sample.ReadLength)) : null);
			}
		}

		return Merge (layout, titles, chpl, duration);
	}

	/// <summary>
	/// Reads the chapter list from <paramref name="moov"/>, fetching chapter samples from <paramref name="stream"/>.
	/// </summary>
	public static List<Mp4Chapter> Read (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		Stream stream,
		TimeSpan duration)
	{
		var chpl = ReadChpl (moov);
		var track = FindChapterTrack (moov);
		var length = stream.Length;
		var layout = track is null ? null : ReadLayout (track, ReadMovieTimescale (moov), length, fragments, moov);
		List<SampleText?>? titles = null;
		if (layout is not null) {
			titles = new List<SampleText?> (layout.Samples.Count);
			foreach (var sample in layout.Samples) {
				SampleText? title = null;
				if (sample.Offset >= 0 && sample.Offset <= length - sample.ReadLength) {
					stream.Position = sample.Offset;
					var bytes = StreamBytes.ReadAtMost (stream, sample.ReadLength);
					if (bytes.Length == sample.ReadLength)
						title = Decode (bytes);
				}

				titles.Add (title);
			}
		}

		return Merge (layout, titles, chpl, duration);
	}

	// ---- chapter track: write

	/// <summary>
	/// Plans a chapter save: the new chpl, the moov children with the chapter track and the
	/// <c>tref</c>/<c>chap</c> references updated, and the sample bytes the chapter track points at.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An existing chapter track is replaced in place with the same track_ID. Without one, a new text track is
	/// added after the last trak, and every sound and video track gets a <c>chap</c> reference to it.
	/// An empty list removes chpl, every track a <c>chap</c> reference names (titles, and Apple chapter URL and
	/// image tracks), and the references to them.
	/// </para>
	/// <para>
	/// A set replaces every chapter title track with the one list. The first text track a <c>chap</c> names is
	/// rebuilt, and every other one goes the way a clear removes it, so no reader shows old titles: ffprobe can
	/// take a later one, and AVFoundation picks one per locale. Two kinds of text track stay. A chapter URL
	/// track (Apple enhanced podcasts) has samples that carry a hypertext modifier box (<c>href</c>, 3GPP
	/// TS 26.245 §5.17.1.5). A text track whose samples cannot all be read cannot be told apart, so it stays too.
	/// Chapter image tracks are video tracks and are not touched.
	/// </para>
	/// <para>
	/// No track is added to a fragmented movie (<c>mvex</c>, §8.8.1, needs a trex per track) or to a moov with
	/// no trak; chpl alone carries the list there. A chapter track with samples in movie fragments is refused
	/// (<see cref="ChapterPlan.Error"/>): fragments add samples to tracks that moov declares (§8.8.1), so a
	/// track rebuilt in moov would leave the fragment samples as a second, stale list. Clearing drops the
	/// removed tracks' trex boxes too (§8.8.3).
	/// </para>
	/// <para>
	/// The new track's single chunk offset is written as 0. <see cref="WriteChunkOffset"/> sets it once the
	/// position of the sample data is known.
	/// </para>
	/// </remarks>
	/// <param name="moov">The movie box.</param>
	/// <param name="fragments">Top-level moof boxes in file order, with their file offsets.</param>
	/// <param name="chapters">The list to write; empty clears.</param>
	/// <param name="duration">The movie duration, for the last chapter.</param>
	/// <param name="quickTime">Whether a new track uses the QuickTime layout.</param>
	/// <param name="file">The whole file, to read the samples of extra chapter text tracks.</param>
	public static ChapterPlan Plan (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		IReadOnlyList<Mp4Chapter> chapters,
		TimeSpan duration,
		bool quickTime,
		ReadOnlySpan<byte> file = default) =>
		Plan (moov, fragments, chapters, duration, quickTime, new FileBytes (file));

	internal static ChapterPlan Plan (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		IReadOnlyList<Mp4Chapter> chapters,
		TimeSpan duration,
		bool quickTime,
		FileBytes file)
	{
		var chapterTracks = FindChapterTracks (moov);
		var existing = chapterTracks.Count > 0 ? chapterTracks[0] : null;
		var children = new List<Mp4Box> (moov.Children);
		if (chapters.Count == 0) {
			// Every track a chap reference names is chapter media: titles, Apple chapter URLs (text), and Apple
			// chapter images (video). Apple players show chapters from any of them, so all of them go.
			// The trak objects go, not every trak with their IDs: a trak that shares an ID is not chapter media.
			var media = FindChapterTracks (moov, textOnly: false);
			if (HasFragments (fragments, new HashSet<uint> (media.Select (t => t.Id))))
				return ChapterPlan.Refused (FragmentedChapterTrackError);

			var lookups = RemoveTraks (children, media);
			return new ChapterPlan (BinaryData.Empty, children, -1, [], null, media) { TrakLookups = lookups };
		}

		// The other title tracks go (see remarks). Their fragment samples, if any, would stay as a stale list.
		// One budget for all of them: a file declares how many tracks and samples there are, not how many bytes.
		// Only a proven URL track may keep fragment samples: a track that cannot be told apart could hold titles.
		var extras = new List<ChapterTrack> ();
		var notUrl = new HashSet<uint> ();
		var budget = new StepBudget (file.Length);
		for (var i = 1; i < chapterTracks.Count; i++) {
			var kind = ClassifyTextTrack (chapterTracks[i].Trak, file, budget);
			if (kind == TextTrackKind.Title)
				extras.Add (chapterTracks[i]);
			if (kind != TextTrackKind.Url)
				notUrl.Add (chapterTracks[i].Id);
		}

		if (existing is not null && HasFragments (fragments, [existing.Id, .. notUrl]))
			return ChapterPlan.Refused (FragmentedChapterTrackError);

		var chpl = RenderChpl (chapters);
		var lastTrak = children.FindLastIndex (b => b.Type == "trak");
		if (existing is null && (lastTrak < 0 || moov.FindChild ("mvex") is not null))
			return new ChapterPlan (chpl, children, -1, []);

		var movieTimescale = ReadMovieTimescale (moov);
		uint trackId;
		uint timescale;
		if (existing is not null) {
			trackId = existing.Id;
			timescale = ReadMediaTimescale (existing.Trak.FindChild ("mdia"));
		} else {
			if (!TryNewTrackId (moov, out trackId))
				return new ChapterPlan (chpl, children, -1, []);
			timescale = movieTimescale;
		}

		if (timescale == 0)
			timescale = DefaultTimescale;

		var deltas = SampleDeltas (chapters, duration, ref timescale);
		var samples = RenderSamples (chapters, out var sizes);
		var style = existing is null ? TrackStyle.Default (quickTime) : TrackStyle.From (existing.Trak, quickTime);
		var trak = BuildTrak (trackId, timescale, movieTimescale, deltas, sizes, quickTime, style);

		if (existing is not null) {
			children[children.IndexOf (existing.Trak)] = trak;
		} else {
			children.Insert (lastTrak + 1, trak);
			var mvhdIndex = children.FindIndex (b => b.Type == "mvhd");
			if (mvhdIndex >= 0)
				children[mvhdIndex] = WithNextTrackId (children[mvhdIndex], trackId);
		}

		// A replaced track keeps its id, so the references that found it still do, and its trex stays valid.
		// The extra title tracks go after the rebuilt trak has replaced the old one (which may hold a chap of its
		// own); each chap entry for them then names the rebuilt track.
		if (existing is null)
			ReplaceReferences (children, new HashSet<uint> { trackId }, add: true);
		else if (extras.Count > 0)
			RemoveTraks (children, extras, trackId);
		var ordinal = children.Where (b => b.Type == "trak").ToList ().IndexOf (trak);
		// Only the rebuilt track's old samples count as replaced: a removed extra title track's samples stay as
		// dead bytes, so an mdat that holds them is kept, as when they share an mdat with other media.
		return new ChapterPlan (chpl, children, ordinal, samples, null, existing is null ? [] : [existing]) {
			SampleSteps = budget.Steps,
		};
	}

	/// <summary>
	/// Removes <paramref name="tracks"/> from <paramref name="children"/> by trak object, and the trex (§8.8.3) of
	/// each ID no remaining trak has. Every <c>chap</c> entry for their IDs (the entry meant that trak, even when
	/// another trak shares its ID) is dropped by a clear. A set passes <paramref name="retarget"/>, the rebuilt
	/// title track's ID, and each such entry then names it instead, so a track whose chapter list was a removed
	/// title track still has one. The set calls this after the rebuilt trak (which has no tref) is in place. Returns the set lookups made, a step count for the work-bound tests.
	/// </summary>
	static long RemoveTraks (List<Mp4Box> children, List<ChapterTrack> tracks, uint? retarget = null)
	{
		var removedTraks = new TrakSet (tracks);
		children.RemoveAll (removedTraks.Contains);

		var removed = new HashSet<uint> (tracks.Select (t => t.Id));
		if (removed.Count > 0)
			ReplaceReferences (children, removed, add: false, retarget);
		var unused = new HashSet<uint> (removed);
		foreach (var child in children) {
			if (child.Type == "trak" && TryReadTrackId (child, out var id))
				unused.Remove (id);
		}

		if (unused.Count > 0)
			WithoutTrex (children, unused);
		return removedTraks.Steps;
	}

	/// <summary>
	/// True when a chap reference names a title track after the first one (see <see cref="ClassifyTextTrack"/>):
	/// a set would remove it, so setting the list the file already reads is not a no-op there.
	/// </summary>
	public static bool HasExtraTitleTracks (Mp4Box moov, ReadOnlySpan<byte> file) =>
		HasExtraTitleTracks (moov, new FileBytes (file));

	internal static bool HasExtraTitleTracks (Mp4Box moov, FileBytes file)
	{
		var tracks = FindChapterTracks (moov);
		var budget = new StepBudget (file.Length);
		for (var i = 1; i < tracks.Count; i++) {
			if (ClassifyTextTrack (tracks[i].Trak, file, budget) == TextTrackKind.Title)
				return true;
		}

		return false;
	}

	internal enum TextTrackKind
	{
		/// <summary>Every sample read, none with an href: chapter titles.</summary>
		Title,

		/// <summary>A sample carries an href modifier: chapter URLs (Apple enhanced podcasts).</summary>
		Url,

		/// <summary>Cannot be told: see <see cref="ClassifyTextTrack"/>.</summary>
		Unknown,
	}

	/// <summary>
	/// Classifies the chapter text trak <paramref name="trak"/> by the samples in its moov sample table. Any
	/// sample with a hypertext modifier box (<c>href</c>, 3GPP TS 26.245 §5.17.1.5) makes it a URL track. When
	/// every sample lies in <paramref name="file"/> and none has one, it is a title track. Otherwise it is
	/// unknown: a sample outside the file, tables that do not parse, no samples in moov (they may be in
	/// fragments), or <paramref name="budget"/> spent. Each sample and each modifier box costs one step from
	/// <paramref name="budget"/>, and no sample table is copied, so the work across every extra track is
	/// bounded by the file's length.
	/// </summary>
	static TextTrackKind ClassifyTextTrack (Mp4Box trak, FileBytes file, StepBudget budget)
	{
		var stbl = trak.FindChild ("mdia")?.FindChild ("minf")?.FindChild ("stbl");
		var co64 = stbl?.FindChild ("co64");
		var stco = stbl?.FindChild ("stco");
		var stsc = stbl?.FindChild ("stsc");
		if (stbl is null || (stco is null && co64 is null) || stsc is null)
			return TextTrackKind.Unknown;

		var width = co64 is not null ? 8 : 4;
		var chunkTable = (co64 ?? stco)!.Data.Span;
		if (!TableComplete (chunkTable, 8, width) || !TableComplete (stsc.Data.Span, 8, 12) || !SampleSizes.TryRead (stbl, out var sizes))
			return TextTrackKind.Unknown;

		var chunks = ReadChunkOffsets (chunkTable, width);
		var rows = ReadStscRows (stsc.Data.Span);
		var row = 0;
		long sample = 0;
		for (var chunk = 0; chunk < chunks.Count && sample < sizes.Count; chunk++) {
			var chunkNumber = (uint)chunk + 1;
			while (row + 1 < rows.Count && rows[row + 1].FirstChunk <= chunkNumber)
				row++;
			var perChunk = rows.Count > 0 && rows[row].FirstChunk <= chunkNumber ? rows[row].SamplesPerChunk : 0u;
			var offset = chunks[chunk];
			for (var k = 0u; k < perChunk && sample < sizes.Count; k++, sample++) {
				if (!budget.Take () || !sizes.TrySum (sample, 1, out var size))
					return TextTrackKind.Unknown;
				var readLength = (int)Math.Min (size, SampleReadLimit);
				if (offset < 0 || readLength > file.Length - offset)
					return TextTrackKind.Unknown;
				if (HasHyperText (file.Slice (offset, readLength), budget))
					return TextTrackKind.Url;
				offset += size;
			}
		}

		return sample > 0 ? TextTrackKind.Title : TextTrackKind.Unknown;
	}

	/// <summary>
	/// True when a text sample (16-bit length, text, then sample modifier boxes; 3GPP TS 26.245 §5.17)
	/// carries an <c>href</c> modifier box (§5.17.1.5). The walk stops at the first box that does not fit, and each box
	/// costs one step from <paramref name="budget"/>; a spent budget also stops it.
	/// </summary>
	internal static bool HasHyperText (ReadOnlySpan<byte> sample, StepBudget budget)
	{
		if (sample.Length < 2)
			return false;

		var pos = 2 + Math.Min (BinaryPrimitives.ReadUInt16BigEndian (sample), sample.Length - 2);
		while (sample.Length - pos >= 8 && budget.Take ()) {
			if (sample.Slice (pos + 4, 4).SequenceEqual ("href"u8))
				return true;
			var size = BinaryPrimitives.ReadUInt32BigEndian (sample.Slice (pos));
			if (size < 8 || size > (uint)(sample.Length - pos))
				return false;
			pos += (int)size;
		}

		return false;
	}

	/// <summary>
	/// Drops the trex (§8.8.3) of each removed track from mvex: a trex names a track that moov declares.
	/// </summary>
	static void WithoutTrex (List<Mp4Box> children, HashSet<uint> removed)
	{
		var index = children.FindIndex (b => b.Type == "mvex");
		if (index < 0)
			return;

		var mvex = children[index];
		var kept = mvex.Children
			.Where (c => c.Type != "trex" || c.Data.Length < 8 || !removed.Contains (BinaryPrimitives.ReadUInt32BigEndian (c.Data.Span.Slice (4))))
			.ToList ();
		if (kept.Count != mvex.Children.Count)
			children[index] = new Mp4Box ("mvex", mvex.Data, kept, mvex.UsesExtendedSize, mvex.UnparsedTail, mvex.HeaderPrefix);
	}

	/// <summary>
	/// True when the box at [<paramref name="boxStart"/>, <paramref name="boxEnd"/>) can be dropped: the old
	/// samples of the tracks <paramref name="plan"/> replaces tile its payload exactly, with no gap and no overlap,
	/// and every byte range anything else in the file references can be computed and none overlaps it. Such an
	/// mdat is the one a previous chapter save wrote. Anything that cannot be computed keeps it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The references are: every other trak's chunks and stbl aux info (<see cref="TryTrackRanges"/>, saio
	/// §8.7.9), every item extent of every iloc in a meta at file, movie or track level
	/// (<see cref="TryItemRanges"/>, §8.11.3), and every traf run and traf aux info (§8.8.7, §8.8.8, §8.7.9). Traks are compared by object, not track_ID, so a trak that shares the replaced track's
	/// ID is still checked.
	/// </para>
	/// </remarks>
	public static bool HoldsOnlyReplacedSamples (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		ChapterPlan plan,
		long boxStart,
		long payloadStart,
		long boxEnd) =>
		HoldsOnlyReplacedSamples (moov, fragments, plan, boxStart, payloadStart, boxEnd, [moov]);

	/// <summary>
	/// <see cref="HoldsOnlyReplacedSamples(Mp4Box, IReadOnlyList{ValueTuple{Mp4Box, long}}, ChapterPlan, long, long, long)"/>
	/// with the file's top-level boxes, so a file-level meta's iloc is counted too.
	/// </summary>
	/// <param name="moov">The movie box.</param>
	/// <param name="fragments">Top-level moof boxes with their file offsets.</param>
	/// <param name="plan">The chapter save.</param>
	/// <param name="boxStart">The candidate mdat's file offset.</param>
	/// <param name="payloadStart">Where its payload starts.</param>
	/// <param name="boxEnd">Where it ends.</param>
	/// <param name="topLevel">The file's top-level boxes, moov among them.</param>
	public static bool HoldsOnlyReplacedSamples (
		Mp4Box moov,
		IReadOnlyList<(Mp4Box Moof, long Start)> fragments,
		ChapterPlan plan,
		long boxStart,
		long payloadStart,
		long boxEnd,
		IReadOnlyList<Mp4Box> topLevel)
	{
		var replaced = plan.ReplacedTraks;
		if (replaced.Count == 0)
			return false;

		if (!TilesExactly (moov, replaced, payloadStart, boxEnd))
			return false;

		// Fail closed: a fragment that cannot be read could point into the box. Every reference is tested as it
		// is found; the first that overlaps, or that cannot be computed, keeps the box.
		var exclusion = new Exclusion (boxStart, boxEnd);
		return FragmentsAvoid (fragments, moov, exclusion) && TryCollectReferences (moov, topLevel, new TrakSet (replaced), exclusion);
	}

	/// <summary>
	/// True when the samples of the replaced tracks cover [<paramref name="payloadStart"/>, <paramref name="boxEnd"/>)
	/// end to end.
	/// </summary>
	static bool TilesExactly (Mp4Box moov, List<ChapterTrack> replaced, long payloadStart, long boxEnd)
	{
		var ranges = new List<(long Start, long End)> ();
		var movieTimescale = ReadMovieTimescale (moov);
		foreach (var track in replaced) {
			var layout = ReadLayout (track, movieTimescale, long.MaxValue);
			if (layout is null)
				continue;
			foreach (var sample in layout.Samples)
				ranges.Add ((sample.Offset, sample.Offset + sample.Size));
		}

		if (ranges.Count == 0)
			return false;

		ranges.Sort ((a, b) => a.Start.CompareTo (b.Start));
		var covered = payloadStart;
		foreach (var (start, end) in ranges) {
			if (start != covered || end > boxEnd)
				return false;
			covered = end;
		}

		return covered == boxEnd;
	}

	/// <summary>
	/// Every file range the movie references outside the replaced traks: the other traks' chunks and saio aux
	/// info, and every iloc item extent under <paramref name="topLevel"/>. False when any of them cannot be computed: an unread moov
	/// tail (it can hold a trak), a trak whose tables cannot be read, an iloc that does not parse, or an unread
	/// box tail that holds an iloc.
	/// </summary>
	internal static bool TryCollectReferences (Mp4Box moov, IReadOnlyList<Mp4Box> topLevel, TrakSet replaced, Exclusion ranges)
	{
		if (moov.UnparsedTail.Length > 0)
			return false;

		foreach (var child in moov.Children) {
			if (child.Type != "trak" || replaced.Contains (child))
				continue;
			if (!TryTrackRanges (child, ranges))
				return false;
		}

		foreach (var box in topLevel) {
			if (!TryItemRanges (box, ranges, 0))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Adds the chunk extents of <paramref name="trak"/>: each chunk's offset (stco/co64) plus the sizes of its
	/// samples (stsc with stsz or stz2; §8.7.3 to §8.7.5), and its stbl aux info (saio with saiz, §8.7.8-9). A
	/// trak with no chunk offset table has no chunk data. False when an extent cannot be computed: an unread
	/// tail on the trak, mdia, minf, or stbl, a missing table, a count past its box, an stz2 field size other
	/// than 4, 8, or 16, a saio without a saiz or either cut short, or arithmetic that does not fit 63 bits.
	/// </summary>
	static bool TryTrackRanges (Mp4Box trak, Exclusion ranges)
	{
		var mdia = trak.FindChild ("mdia");
		var minf = mdia?.FindChild ("minf");
		var stbl = minf?.FindChild ("stbl");
		if (stbl is null || trak.UnparsedTail.Length > 0 || mdia!.UnparsedTail.Length > 0
			|| minf!.UnparsedTail.Length > 0 || stbl.UnparsedTail.Length > 0)
			return false;

		// Auxiliary information (saio, §8.7.9) in an stbl is at absolute file offsets.
		if (!TryAuxRanges (stbl, 0, ranges))
			return false;

		var co64 = stbl.FindChild ("co64");
		var stco = stbl.FindChild ("stco");
		if (stco is null && co64 is null)
			return true;

		var width = co64 is not null ? 8 : 4;
		var chunkTable = (co64 ?? stco)!.Data.Span;
		if (!TableComplete (chunkTable, 8, width))
			return false;

		var chunks = ReadChunkOffsets (chunkTable, width);
		if (chunks.Count == 0)
			return true;

		var stsc = stbl.FindChild ("stsc");
		if (stsc is null || !TableComplete (stsc.Data.Span, 8, 12) || !SampleSizes.TryRead (stbl, out var sizes))
			return false;

		var rows = ReadStscRows (stsc.Data.Span);
		var row = 0;
		long sample = 0;
		for (var chunk = 0; chunk < chunks.Count; chunk++) {
			var chunkNumber = (uint)chunk + 1;
			while (row + 1 < rows.Count && rows[row + 1].FirstChunk <= chunkNumber)
				row++;
			var perChunk = rows.Count > 0 && rows[row].FirstChunk <= chunkNumber ? rows[row].SamplesPerChunk : 0u;
			var inChunk = Math.Min (perChunk, Math.Max (0, sizes.Count - sample));
			var offset = chunks[chunk];
			if (offset < 0 || offset >= long.MaxValue / 2 || !sizes.TrySum (sample, inChunk, out var length)
				|| length > long.MaxValue - offset)
				return false;

			if (!ranges.Avoids (offset, offset + Math.Max (length, 1)))
				return false;
			sample += inChunk;
		}

		return true;
	}

	/// <summary>
	/// The sample sizes of an stbl: stsz (§8.7.3.2), a constant or one 32-bit entry per sample, or stz2
	/// (§8.7.3.3), entries of 4, 8, or 16 bits (4-bit entries two to a byte, high nibble first).
	/// </summary>
	readonly ref struct SampleSizes
	{
		readonly ReadOnlySpan<byte> _entries;
		readonly uint _constant;
		readonly int _bits;

		SampleSizes (ReadOnlySpan<byte> entries, uint constant, int bits, long count)
		{
			_entries = entries;
			_constant = constant;
			_bits = bits;
			Count = count;
		}

		public long Count { get; }

		public static bool TryRead (Mp4Box stbl, out SampleSizes sizes)
		{
			sizes = default;
			var stsz = stbl.FindChild ("stsz");
			if (stsz is not null) {
				var body = stsz.Data.Span;
				if (body.Length < 12)
					return false;
				var constant = BinaryPrimitives.ReadUInt32BigEndian (body.Slice (4));
				var count = BinaryPrimitives.ReadUInt32BigEndian (body.Slice (8));
				if (constant == 0 && count > (ulong)(body.Length - 12) / 4)
					return false;
				sizes = new SampleSizes (body.Slice (12), constant, 32, count);
				return true;
			}

			var stz2 = stbl.FindChild ("stz2");
			if (stz2 is null)
				return false;

			var compact = stz2.Data.Span;
			if (compact.Length < 12)
				return false;
			var bits = compact[7];
			var entries = BinaryPrimitives.ReadUInt32BigEndian (compact.Slice (8));
			if (bits is not (4 or 8 or 16) || ((ulong)entries * bits + 7) / 8 > (ulong)(compact.Length - 12))
				return false;
			sizes = new SampleSizes (compact.Slice (12), 0, bits, entries);
			return true;
		}

		/// <summary>
		/// The total size of <paramref name="count"/> samples from <paramref name="first"/>; false on overflow.
		/// </summary>
		public bool TrySum (long first, long count, out long total)
		{
			total = 0;
			if (count <= 0)
				return true;

			if (_constant != 0) {
				if (count > long.MaxValue / _constant)
					return false;
				total = count * _constant;
				return true;
			}

			for (var i = first; i < first + count; i++) {
				long size = _bits switch {
					32 => BinaryPrimitives.ReadUInt32BigEndian (_entries.Slice ((int)(i * 4))),
					16 => BinaryPrimitives.ReadUInt16BigEndian (_entries.Slice ((int)(i * 2))),
					8 => _entries[(int)i],
					_ => (i & 1) == 0 ? _entries[(int)(i / 2)] >> 4 : _entries[(int)(i / 2)] & 0x0F,
				};
				if (total > long.MaxValue - size)
					return false;
				total += size;
			}

			return true;
		}
	}

	/// <summary>
	/// Adds the file extents of every iloc under <paramref name="box"/>. A meta without an iloc references
	/// nothing. False when an iloc does not parse, or when the unread tail of a box that stopped parsing holds
	/// the bytes "iloc": an iloc there cannot be read. Unread bytes without them cannot hold an iloc, since
	/// every box carries its type code; that keeps a meta with stray trailing bytes (seen in iTunes files)
	/// from blocking.
	/// </summary>
	static bool TryItemRanges (Mp4Box box, Exclusion ranges, int depth)
	{
		if (depth > 32)
			return false;

		var tail = box.UnparsedTail.Span;
		if (tail.IndexOf ("iloc"u8) >= 0)
			return false;

		foreach (var child in box.Children) {
			if (child.Type == "iloc") {
				if (!TryIlocRanges (child.Data.Span, ranges))
					return false;
			} else if (!TryItemRanges (child, ranges, depth + 1)) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// iloc (§8.11.3): version 0 to 2; offset, length, and base offset sizes of 0, 4, or 8 bytes; an index size
	/// in versions 1 and 2. Construction method 0 places each extent at base_offset + extent_offset in a file
	/// (counted here for any data reference, as it may be this file); an extent_length of 0 runs to the end.
	/// Methods 1 (idat) and 2 (item) are not file offsets. False when the box is cut short, a field size is not
	/// 0, 4, or 8, or a sum does not fit 63 bits.
	/// </summary>
	internal static bool TryIlocRanges (ReadOnlySpan<byte> iloc, Exclusion ranges)
	{
		if (iloc.Length < 8)
			return false;

		var version = iloc[0];
		if (version > 2)
			return false;

		var offsetSize = iloc[4] >> 4;
		var lengthSize = iloc[4] & 0x0F;
		var baseSize = iloc[5] >> 4;
		var indexSize = version is 1 or 2 ? iloc[5] & 0x0F : 0;
		if (!IsWidth (offsetSize) || !IsWidth (lengthSize) || !IsWidth (baseSize) || !IsWidth (indexSize))
			return false;

		var pos = 6;
		ulong count;
		if (version < 2) {
			if (!TryReadWidth (iloc, ref pos, 2, out count))
				return false;
		} else if (!TryReadWidth (iloc, ref pos, 4, out count)) {
			return false;
		}

		for (var item = 0UL; item < count; item++) {
			if (!TryReadWidth (iloc, ref pos, version < 2 ? 2 : 4, out _))
				return false;

			ulong method = 0;
			if (version is 1 or 2) {
				if (!TryReadWidth (iloc, ref pos, 2, out method))
					return false;
				method &= 0x0F;
			}

			if (!TryReadWidth (iloc, ref pos, 2, out _) || !TryReadWidth (iloc, ref pos, baseSize, out var baseOffset)
				|| !TryReadWidth (iloc, ref pos, 2, out var extents))
				return false;

			// Extents with no index, offset, or length fields take no bytes and are all the same extent, so one
			// stands for them all. Any other extent count must fit the bytes left in the box.
			var extentBytes = indexSize + offsetSize + lengthSize;
			if (extentBytes == 0)
				extents = Math.Min (extents, 1UL);
			else if (extents > (ulong)(iloc.Length - pos) / (ulong)extentBytes)
				return false;

			for (var extent = 0UL; extent < extents; extent++) {
				if (!TryReadWidth (iloc, ref pos, indexSize, out _) || !TryReadWidth (iloc, ref pos, offsetSize, out var extentOffset)
					|| !TryReadWidth (iloc, ref pos, lengthSize, out var extentLength))
					return false;
				ranges.Step ();
				if (method != 0)
					continue;
				if (baseOffset > long.MaxValue || extentOffset > (ulong)long.MaxValue - baseOffset)
					return false;

				// An extent_length of 0 is the whole referenced data from the offset (§8.11.3).
				var start = (long)(baseOffset + extentOffset);
				if (extentLength > (ulong)(long.MaxValue - start))
					return false;
				var end = extentLength == 0 ? long.MaxValue : start + (long)extentLength;
				if (!ranges.Avoids (start, end))
					return false;
			}
		}

		return true;
	}

	static bool IsWidth (int size) => size is 0 or 4 or 8;

	static bool TryReadWidth (ReadOnlySpan<byte> data, ref int pos, int width, out ulong value)
	{
		value = 0;
		if (width == 0)
			return true;
		if (data.Length - pos < width)
			return false;

		value = width switch {
			2 => BinaryPrimitives.ReadUInt16BigEndian (data.Slice (pos)),
			4 => BinaryPrimitives.ReadUInt32BigEndian (data.Slice (pos)),
			_ => BinaryPrimitives.ReadUInt64BigEndian (data.Slice (pos)),
		};
		pos += width;
		return true;
	}

	/// <summary>
	/// True when the entry count at <paramref name="countAt"/> - 4 of a FullBox payload fits its entries in the box.
	/// </summary>
	static bool TableComplete (ReadOnlySpan<byte> payload, int countAt, int entrySize)
	{
		if (payload.Length < countAt)
			return false;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (countAt - 4));
		return count <= (ulong)(payload.Length - countAt) / (ulong)entrySize;
	}

	/// <summary>
	/// Writes the chapter track's single chunk offset into a rendered moov. Finds the trak at
	/// <paramref name="trakOrdinal"/> among moov's traks (by position, not track_ID, which a file can reuse),
	/// then its stco.
	/// </summary>
	public static bool WriteChunkOffset (Span<byte> moov, int trakOrdinal, uint offset)
	{
		var boxes = Children (moov);
		if (boxes.Count == 0 || boxes[0].Type != "moov" || trakOrdinal < 0)
			return false;

		var body = moov.Slice (boxes[0].Header, boxes[0].Length - boxes[0].Header);
		var ordinal = -1;
		foreach (var (start, length, header, type) in Children (body)) {
			if (type != "trak" || ++ordinal != trakOrdinal)
				continue;

			var trak = body.Slice (start + header, length - header);

			var stco = FindPath (trak, "mdia", "minf", "stbl", "stco");
			if (stco.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian (stco.Slice (4)) != 1)
				return false;

			BinaryPrimitives.WriteUInt32BigEndian (stco.Slice (8), offset);
			return true;
		}

		return false;
	}

	/// <summary>
	/// One delta per chapter in the media timescale. The first sample starts at zero, so the chapter track
	/// covers the timeline from the start, as a QuickTime chapter list does. The last chapter runs to
	/// <paramref name="duration"/>, or one unit when it starts at or after it. A delta that does not fit 32 bits
	/// moves the track to a 1000 Hz timescale.
	/// </summary>
	static ulong[] SampleDeltas (IReadOnlyList<Mp4Chapter> chapters, TimeSpan duration, ref uint timescale)
	{
		while (true) {
			var deltas = new ulong[chapters.Count];
			var fits = true;
			for (var i = 0; i < chapters.Count; i++) {
				var start = i == 0 ? 0UL : ToMedia (chapters[i].StartTime, timescale);
				var end = i + 1 < chapters.Count ? ToMedia (chapters[i + 1].StartTime, timescale) : ToMedia (duration, timescale);
				deltas[i] = end > start ? end - start : (i + 1 < chapters.Count ? 0UL : 1UL);
				if (deltas[i] > uint.MaxValue)
					fits = false;
			}

			if (fits || timescale == DefaultTimescale) {
				for (var i = 0; i < deltas.Length; i++)
					deltas[i] = Math.Min (deltas[i], uint.MaxValue);
				return deltas;
			}

			timescale = DefaultTimescale;
		}
	}

	/// <summary>
	/// Each sample: 16-bit length, UTF-8 text cut to 65,535 bytes, then an <c>encd</c> box with value 0x100,
	/// the UTF-8 marker that ffmpeg-written and Apple-written chapter samples carry. Readers skip modifier
	/// boxes they do not know (3GPP TS 26.245 §5.17).
	/// </summary>
	static byte[] RenderSamples (IReadOnlyList<Mp4Chapter> chapters, out uint[] sizes)
	{
		sizes = new uint[chapters.Count];
		using var data = new BinaryDataBuilder ();
		for (var i = 0; i < chapters.Count; i++) {
			var start = data.Length;
			var text = TruncateUtf8 (chapters[i].Title, MaxSampleTextBytes);
			data.AddUInt16BE ((ushort)text.Length);
			data.Add (text);
			data.AddUInt32BE (12);
			data.AddStringLatin1 ("encd");
			data.AddUInt32BE (0x100);
			sizes[i] = (uint)(data.Length - start);
		}

		return data.ToArray ();
	}

	static Mp4Box BuildTrak (
		uint trackId,
		uint timescale,
		uint movieTimescale,
		ulong[] deltas,
		uint[] sizes,
		bool quickTime,
		TrackStyle style)
	{
		ulong mediaDuration = 0;
		foreach (var delta in deltas)
			mediaDuration += delta;
		var movieDuration = movieTimescale == 0 ? 0 : ToMedia (ToTime (mediaDuration, timescale), movieTimescale);

		using var trak = new BinaryDataBuilder ();
		trak.Add (Tkhd (trackId, movieDuration, style.TkhdFlags));
		using (var mdia = new BinaryDataBuilder ()) {
			mdia.Add (Mdhd (timescale, mediaDuration, style.Language));
			mdia.Add (style.Hdlr);
			mdia.Add (Minf (deltas, sizes, quickTime));
			trak.Add (WrapBox ("mdia", mdia.Span));
		}

		var bytes = WrapBox ("trak", trak.Span);
		return Mp4Box.Parse (bytes.Span).Box!;
	}

	/// <summary>
	/// What a rebuilt chapter track keeps from the track it replaces: the tkhd flags (§8.3.2), the mdhd
	/// language (§8.4.2; Apple players look chapters up by language), and the hdlr box with its name (§8.4.3).
	/// A new track is disabled (flags 2: in the movie, not enabled; TN2174 "Chapter Marker Track", QTFF
	/// "Chapter lists"), has language <c>und</c>, and a <c>text</c> handler.
	/// </summary>
	internal readonly record struct TrackStyle (uint TkhdFlags, ushort Language, BinaryData Hdlr)
	{
		const ushort Undetermined = 0x55C4;

		public static TrackStyle Default (bool quickTime) => new (2, Undetermined, Mp4ChapterCodec.Hdlr (quickTime));

		public static TrackStyle From (Mp4Box trak, bool quickTime)
		{
			var fallback = Default (quickTime);
			var tkhd = Payload (trak.FindChild ("tkhd"));
			var flags = tkhd.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian (tkhd) & 0xFFFFFF : fallback.TkhdFlags;

			var mdia = trak.FindChild ("mdia");
			var mdhd = Payload (mdia?.FindChild ("mdhd"));
			var at = mdhd.Length > 0 && mdhd[0] == 1 ? 32 : 20;
			var language = mdhd.Length >= at + 2 ? BinaryPrimitives.ReadUInt16BigEndian (mdhd.Slice (at)) : fallback.Language;

			var hdlr = mdia?.FindChild ("hdlr");
			return new TrackStyle (flags, language, hdlr is null ? fallback.Hdlr : hdlr.Render ());
		}
	}

	/// <summary>
	/// tkhd (§8.3.2) with <paramref name="flags"/>. No volume, identity matrix, no size.
	/// </summary>
	static BinaryData Tkhd (uint trackId, ulong duration, uint flags)
	{
		var wide = duration > uint.MaxValue;
		using var body = new BinaryDataBuilder ();
		body.Add ((byte)(wide ? 1 : 0));
		body.AddUInt24BE (flags);
		if (wide) {
			body.AddUInt64BE (0);
			body.AddUInt64BE (0);
			body.AddUInt32BE (trackId);
			body.AddUInt32BE (0);
			body.AddUInt64BE (duration);
		} else {
			body.AddUInt32BE (0);
			body.AddUInt32BE (0);
			body.AddUInt32BE (trackId);
			body.AddUInt32BE (0);
			body.AddUInt32BE ((uint)duration);
		}

		body.AddZeros (8);
		body.AddUInt16BE (0);
		body.AddUInt16BE (0);
		body.AddUInt16BE (0);
		body.AddUInt16BE (0);
		AddIdentityMatrix (body);
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		return WrapBox ("tkhd", body.Span);
	}

	/// <summary>
	/// mdhd (§8.4.2) with the packed ISO 639-2/T <paramref name="language"/>.
	/// </summary>
	static BinaryData Mdhd (uint timescale, ulong duration, ushort language)
	{
		var wide = duration > uint.MaxValue;
		using var body = new BinaryDataBuilder ();
		body.Add ((byte)(wide ? 1 : 0));
		body.AddUInt24BE (0);
		if (wide) {
			body.AddUInt64BE (0);
			body.AddUInt64BE (0);
			body.AddUInt32BE (timescale);
			body.AddUInt64BE (duration);
		} else {
			body.AddUInt32BE (0);
			body.AddUInt32BE (0);
			body.AddUInt32BE (timescale);
			body.AddUInt32BE ((uint)duration);
		}

		body.AddUInt16BE (language);
		body.AddUInt16BE (0);
		return WrapBox ("mdhd", body.Span);
	}

	/// <summary>
	/// hdlr (§8.4.3) with handler type <c>text</c>. QuickTime files set the component type to <c>mhlr</c>
	/// and store the name as a counted string (QuickTime File Format, "Handler Reference Atoms").
	/// </summary>
	static BinaryData Hdlr (bool quickTime)
	{
		const string name = "Chapters";
		using var body = new BinaryDataBuilder ();
		body.AddUInt32BE (0);
		if (quickTime)
			body.AddStringLatin1 ("mhlr");
		else
			body.AddUInt32BE (0);
		body.AddStringLatin1 ("text");
		body.AddZeros (12);
		if (quickTime) {
			body.Add ((byte)name.Length);
			body.AddStringLatin1 (name);
		} else {
			body.AddStringLatin1NullTerminated (name);
		}

		return WrapBox ("hdlr", body.Span);
	}

	/// <summary>
	/// minf: the media header, a self-contained data reference (§8.7.2), and the sample table, all samples in
	/// one chunk. An MP4 chapter track uses the null media header <c>nmhd</c> (§8.4.5.2; Apple TN2174, "Chapter
	/// Marker Track": "the Null Media Header box (nmhd) is used as the media information header. This is in
	/// contrast to the Generic Media Header box (gmhd) box used in the QuickTime Text Track"). A QuickTime file
	/// uses the base media information header <c>gmhd</c> with <c>gmin</c> (version, flags, graphics mode,
	/// opcolor, balance, reserved) and the text media information atom <c>text</c>, a matrix (QTFF, "Base media
	/// information header atom", "Base media info atom", "Text media information atom").
	/// </summary>
	static BinaryData Minf (ulong[] deltas, uint[] sizes, bool quickTime)
	{
		using var minf = new BinaryDataBuilder ();

		if (quickTime) {
			using var gmhd = new BinaryDataBuilder ();
			using var gmin = new BinaryDataBuilder ();
			gmin.AddUInt32BE (0);
			gmin.AddUInt16BE (0x40);
			gmin.AddUInt16BE (0x8000);
			gmin.AddUInt16BE (0x8000);
			gmin.AddUInt16BE (0x8000);
			gmin.AddUInt16BE (0);
			gmin.AddUInt16BE (0);
			gmhd.Add (WrapBox ("gmin", gmin.Span));

			using var text = new BinaryDataBuilder ();
			AddIdentityMatrix (text);
			gmhd.Add (WrapBox ("text", text.Span));
			minf.Add (WrapBox ("gmhd", gmhd.Span));
		} else {
			minf.Add (WrapBox ("nmhd", [0, 0, 0, 0]));
		}

		using (var dref = new BinaryDataBuilder ()) {
			dref.AddUInt32BE (0);
			dref.AddUInt32BE (1);
			dref.AddUInt32BE (12);
			dref.AddStringLatin1 ("url ");
			dref.AddUInt32BE (1);
			minf.Add (WrapBox ("dinf", WrapBox ("dref", dref.Span).Span));
		}

		using (var stbl = new BinaryDataBuilder ()) {
			stbl.Add (quickTime ? QuickTimeStsd () : Mp4Stsd ());
			stbl.Add (Stts (deltas));

			using (var stsc = new BinaryDataBuilder ()) {
				stsc.AddUInt32BE (0);
				stsc.AddUInt32BE (1);
				stsc.AddUInt32BE (1);
				stsc.AddUInt32BE ((uint)sizes.Length);
				stsc.AddUInt32BE (1);
				stbl.Add (WrapBox ("stsc", stsc.Span));
			}

			using (var stsz = new BinaryDataBuilder ()) {
				stsz.AddUInt32BE (0);
				stsz.AddUInt32BE (0);
				stsz.AddUInt32BE ((uint)sizes.Length);
				foreach (var size in sizes)
					stsz.AddUInt32BE (size);
				stbl.Add (WrapBox ("stsz", stsz.Span));
			}

			using (var stco = new BinaryDataBuilder ()) {
				stco.AddUInt32BE (0);
				stco.AddUInt32BE (1);
				stco.AddUInt32BE (0);
				stbl.Add (WrapBox ("stco", stco.Span));
			}

			minf.Add (WrapBox ("stbl", stbl.Span));
		}

		return WrapBox ("minf", minf.Span);
	}

	/// <summary>
	/// One <c>tx3g</c> sample entry with data_reference_index 1 (§8.5.2), the 3GPP TextSampleEntry
	/// (TS 26.245 §5.16) that TN2174 names for an MP4 chapter track: display flags, horizontal and vertical
	/// justification, background colour (RGBA), default text box, default style record (start and end char, font
	/// ID 1, face, size 18, white), and a font table with font 1. TN2174: chapter readers ignore the formatting.
	/// </summary>
	static BinaryData Mp4Stsd ()
	{
		using var entry = new BinaryDataBuilder ();
		entry.AddZeros (6);
		entry.AddUInt16BE (1);
		entry.AddUInt32BE (0);
		entry.Add ((byte)0);
		entry.Add ((byte)0);
		entry.AddUInt32BE (0);
		entry.AddZeros (8);
		entry.AddUInt16BE (0);
		entry.AddUInt16BE (0);
		entry.AddUInt16BE (1);
		entry.Add ((byte)0);
		entry.Add ((byte)18);
		entry.AddUInt32BE (0xFFFFFFFF);

		using var ftab = new BinaryDataBuilder ();
		const string font = "Sans-Serif";
		ftab.AddUInt16BE (1);
		ftab.AddUInt16BE (1);
		ftab.Add ((byte)font.Length);
		ftab.AddStringLatin1 (font);
		entry.Add (WrapBox ("ftab", ftab.Span));

		return SampleDescription ("tx3g", entry.Span);
	}

	/// <summary>
	/// One <c>text</c> sample entry with data_reference_index 1, laid out as the QTFF "Text sample description":
	/// display flags (32), text justification (32, 0 = left), background color (48), default text box (64),
	/// reserved (64), font number (16), font face (16), reserved (8), reserved (16), foreground color (48), and
	/// the text name as a Pascal string. Every field is zero and the name is empty: chapter text is not drawn.
	/// AVFoundation and MediaInfo stop listing the chapters when the foreground colour is white
	/// (FFFF FFFF FFFF); red and near-black still list them. Black is used.
	/// </summary>
	static BinaryData QuickTimeStsd ()
	{
		using var entry = new BinaryDataBuilder ();
		entry.AddZeros (6);
		entry.AddUInt16BE (1);
		entry.AddUInt32BE (0);
		entry.AddUInt32BE (0);
		entry.AddZeros (6);
		entry.AddZeros (8);
		entry.AddZeros (8);
		entry.AddUInt16BE (0);
		entry.AddUInt16BE (0);
		entry.Add ((byte)0);
		entry.AddUInt16BE (0);
		entry.AddZeros (6);
		entry.Add ((byte)0);
		return SampleDescription ("text", entry.Span);
	}

	static BinaryData SampleDescription (string format, ReadOnlySpan<byte> entry)
	{
		using var stsd = new BinaryDataBuilder ();
		stsd.AddUInt32BE (0);
		stsd.AddUInt32BE (1);
		stsd.Add (WrapBox (format, entry));
		return WrapBox ("stsd", stsd.Span);
	}

	/// <summary>
	/// stts (§8.6.1.2): runs of equal deltas share an entry.
	/// </summary>
	static BinaryData Stts (ulong[] deltas)
	{
		var runs = new List<(uint Count, uint Delta)> ();
		foreach (var delta in deltas) {
			var last = runs.Count - 1;
			if (last >= 0 && runs[last].Delta == (uint)delta)
				runs[last] = (runs[last].Count + 1, runs[last].Delta);
			else
				runs.Add ((1, (uint)delta));
		}

		using var body = new BinaryDataBuilder ();
		body.AddUInt32BE (0);
		body.AddUInt32BE ((uint)runs.Count);
		foreach (var (count, delta) in runs) {
			body.AddUInt32BE (count);
			body.AddUInt32BE (delta);
		}

		return WrapBox ("stts", body.Span);
	}

	static void AddIdentityMatrix (BinaryDataBuilder builder)
	{
		builder.AddUInt32BE (0x00010000);
		builder.AddUInt32BE (0);
		builder.AddUInt32BE (0);
		builder.AddUInt32BE (0);
		builder.AddUInt32BE (0x00010000);
		builder.AddUInt32BE (0);
		builder.AddUInt32BE (0);
		builder.AddUInt32BE (0);
		builder.AddUInt32BE (0x40000000);
	}

	/// <summary>
	/// Adds the id in <paramref name="trackIds"/> to the <c>chap</c> reference of every sound and video trak
	/// that is not itself chapter media, or removes every id in <paramref name="trackIds"/> from every
	/// <c>chap</c> reference. An emptied
	/// <c>chap</c> and an emptied <c>tref</c> are dropped. Other reference types are kept byte for byte.
	/// </summary>
	/// <remarks>
	/// Chapter media is the set of trak objects <see cref="FindChapterTracks(Mp4Box, bool)"/> returns for any
	/// chapter media: what a chap reference resolves to, unless that trak holds a chap of its own or is a sound
	/// trak. Apple files keep chapter images in such a video trak, and it gets no reference. A trak that only
	/// shares an ID with chapter media is not chapter media and gets one. The new chapter trak is a text trak,
	/// so it never references itself. Removing: the removed traks are
	/// already gone from <paramref name="children"/>, and every remaining trak drops its chap entries for
	/// those IDs, which named them, even a trak that shares one.
	/// </remarks>
	static void ReplaceReferences (List<Mp4Box> children, HashSet<uint> trackIds, bool add, uint? retarget = null)
	{
		var chapterMedia = add ? new TrakSet (FindChapterTracks (children, textOnly: false)) : null;
		for (var i = 0; i < children.Count; i++) {
			var trak = children[i];
			if (trak.Type != "trak")
				continue;

			var tref = trak.FindChild ("tref");
			if (add && (!IsAudioOrVideo (trak) || chapterMedia!.Contains (trak)))
				continue;
			if (!add && tref is null)
				continue;

			var newTref = RebuildTref (Payload (tref), trackIds, add, retarget);
			if (tref is not null && newTref.Span.SequenceEqual (tref.Data.Span))
				continue;

			var trakChildren = new List<Mp4Box> (trak.Children);
			var index = tref is null ? -1 : trakChildren.IndexOf (tref);
			if (newTref.IsEmpty) {
				if (index >= 0)
					trakChildren.RemoveAt (index);
			} else if (index >= 0) {
				trakChildren[index] = new Mp4Box ("tref", newTref);
			} else {
				var mdia = trakChildren.FindIndex (b => b.Type == "mdia");
				trakChildren.Insert (mdia >= 0 ? mdia : trakChildren.Count, new Mp4Box ("tref", newTref));
			}

			children[i] = new Mp4Box ("trak", trak.Data, trakChildren, trak.UsesExtendedSize, trak.UnparsedTail, trak.HeaderPrefix);
		}
	}

	/// <summary>
	/// The tref payload with the single id in <paramref name="trackIds"/> added to its <c>chap</c> box, or with
	/// every id in <paramref name="trackIds"/> removed from it. Entries already present keep their order; an id
	/// already present is not added twice.
	/// </summary>
	internal static BinaryData RebuildTref (ReadOnlySpan<byte> payload, HashSet<uint> trackIds, bool add, uint? retarget = null)
	{
		var trackId = add ? trackIds.First () : 0;
		using var builder = new BinaryDataBuilder ();
		var placed = false;
		var end = 0;
		foreach (var (start, length, header, type) in Children (payload)) {
			end = start + length;
			if (type != "chap") {
				builder.Add (payload.Slice (start, length));
				continue;
			}

			// Other chap entries stay: Apple files also reference chapter image and URL tracks here.
			// Removing with a retarget: an entry for a removed id names the retarget instead, once, in the
			// position of its first entry.
			var ids = new List<uint> ();
			var placedRetarget = false;
			var body = payload.Slice (start + header, length - header);
			for (var pos = 0; pos + 4 <= body.Length; pos += 4) {
				var id = BinaryPrimitives.ReadUInt32BigEndian (body.Slice (pos));
				var named = !add && trackIds.Contains (id) && retarget is not null ? retarget.Value : id;
				if (!add && trackIds.Contains (id) && retarget is null)
					continue;
				if (retarget is not null && named == retarget.Value) {
					if (placedRetarget)
						continue;
					placedRetarget = true;
				}

				ids.Add (named);
			}

			if (add && !placed && !ids.Contains (trackId))
				ids.Add (trackId);
			placed = true;
			if (ids.Count > 0)
				builder.Add (ChapBox (ids));
		}

		if (end < payload.Length)
			builder.Add (payload.Slice (end));
		if (add && !placed)
			builder.Add (ChapBox ([trackId]));
		return builder.ToBinaryData ();
	}

	static BinaryData ChapBox (IReadOnlyList<uint> ids)
	{
		using var body = new BinaryDataBuilder ();
		foreach (var id in ids)
			body.AddUInt32BE (id);
		return WrapBox ("chap", body.Span);
	}

	/// <summary>
	/// One past the largest track_ID, and not below mvhd next_track_ID (§8.2.2). False when no id is left.
	/// </summary>
	static bool TryNewTrackId (Mp4Box moov, out uint trackId)
	{
		uint max = 0;
		foreach (var child in moov.Children) {
			if (child.Type == "trak" && TryReadTrackId (child, out var id) && id > max)
				max = id;
		}

		trackId = 0;
		if (max == uint.MaxValue)
			return false;

		trackId = max + 1;
		var next = ReadNextTrackId (moov.FindChild ("mvhd"));
		if (next > trackId && next != uint.MaxValue)
			trackId = next;
		return true;
	}

	static uint ReadNextTrackId (Mp4Box? mvhd)
	{
		if (mvhd is null)
			return 0;

		var data = mvhd.Data.Span;
		var at = NextTrackIdPosition (data);
		return at < 0 ? 0 : BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at));
	}

	static Mp4Box WithNextTrackId (Mp4Box mvhd, uint trackId)
	{
		var data = mvhd.Data.ToArray ();
		var at = NextTrackIdPosition (data);
		if (at < 0 || BinaryPrimitives.ReadUInt32BigEndian (data.AsSpan (at)) > trackId)
			return mvhd;

		BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (at), trackId == uint.MaxValue ? trackId : trackId + 1);
		return new Mp4Box ("mvhd", new BinaryData (data), usesExtendedSize: mvhd.UsesExtendedSize);
	}

	/// <summary>
	/// next_track_ID is the last field of mvhd: offset 96 in version 0, 108 in version 1 (§8.2.2).
	/// </summary>
	static int NextTrackIdPosition (ReadOnlySpan<byte> mvhd)
	{
		if (mvhd.Length < 4)
			return -1;

		var at = mvhd[0] == 1 ? 108 : 96;
		return mvhd.Length >= at + 4 ? at : -1;
	}

	// ---- box fields

	/// <summary>
	/// tkhd track_ID: offset 12 in version 0, 20 in version 1 (§8.3.2).
	/// </summary>
	static bool TryReadTrackId (Mp4Box trak, out uint id)
	{
		id = 0;
		var tkhd = trak.FindChild ("tkhd");
		return tkhd is not null && TryTrackIdFromTkhd (tkhd.Data.Span, out id);
	}

	static bool TryTrackIdFromTkhd (ReadOnlySpan<byte> tkhd, out uint id)
	{
		id = 0;
		if (tkhd.Length < 4)
			return false;

		var at = tkhd[0] == 1 ? 20 : 12;
		if (tkhd.Length < at + 4)
			return false;

		id = BinaryPrimitives.ReadUInt32BigEndian (tkhd.Slice (at));
		return true;
	}

	/// <summary>
	/// mvhd duration over its timescale (§8.2.2): 32-bit in version 0 at offset 16, 64-bit in version 1 at 24.
	/// </summary>
	public static TimeSpan MovieDuration (Mp4Box moov)
	{
		var mvhd = Payload (moov.FindChild ("mvhd"));
		var timescale = ReadTimescale (mvhd);
		if (timescale == 0)
			return TimeSpan.Zero;

		if (mvhd[0] == 1)
			return mvhd.Length >= 32 ? ToTime (BinaryPrimitives.ReadUInt64BigEndian (mvhd.Slice (24)), timescale) : TimeSpan.Zero;

		return mvhd.Length >= 20 ? ToTime (BinaryPrimitives.ReadUInt32BigEndian (mvhd.Slice (16)), timescale) : TimeSpan.Zero;
	}

	static uint ReadMovieTimescale (Mp4Box moov)
	{
		var mvhd = Payload (moov.FindChild ("mvhd"));
		return ReadTimescale (mvhd);
	}

	static uint ReadMediaTimescale (Mp4Box? mdia)
	{
		var mdhd = Payload (mdia?.FindChild ("mdhd"));
		return ReadTimescale (mdhd);
	}

	/// <summary>
	/// mvhd and mdhd share the timescale position: offset 12 in version 0, 20 in version 1 (§8.2.2, §8.4.2).
	/// </summary>
	static uint ReadTimescale (ReadOnlySpan<byte> header)
	{
		if (header.Length < 4)
			return 0;

		var at = header[0] == 1 ? 20 : 12;
		return header.Length >= at + 4 ? BinaryPrimitives.ReadUInt32BigEndian (header.Slice (at)) : 0;
	}

	/// <summary>
	/// hdlr handler_type at payload offset 8 (§8.4.3). <c>text</c> is QuickTime and 3GPP text; <c>sbtl</c>
	/// is the Apple subtitle handler some writers use for chapter text.
	/// </summary>
	static bool IsTextHandler (Mp4Box trak) => HandlerType (trak) is "text" or "sbtl";

	static bool IsAudioOrVideo (Mp4Box trak) => HandlerType (trak) is "soun" or "vide";

	static string HandlerType (Mp4Box trak)
	{
		var hdlr = Payload (trak.FindChild ("mdia")?.FindChild ("hdlr"));
		if (hdlr.Length < 12)
			return string.Empty;

		return Polyfills.Latin1.GetString (hdlr.Slice (8, 4));
	}

	static List<uint> ReadChapterReferences (ReadOnlySpan<byte> tref)
	{
		var ids = new List<uint> ();
		foreach (var (start, length, header, type) in Children (tref)) {
			if (type != "chap")
				continue;

			var body = tref.Slice (start + header, length - header);
			for (var pos = 0; pos + 4 <= body.Length; pos += 4)
				ids.Add (BinaryPrimitives.ReadUInt32BigEndian (body.Slice (pos)));
		}

		return ids;
	}

	/// <summary>
	/// The first edit list entries (§8.6.6): an empty edit (media_time -1) delays the track by its segment
	/// duration in the movie timescale; the next entry's media_time is the first media time shown.
	/// </summary>
	static (TimeSpan Delay, ulong MediaStart) ReadEdit (Mp4Box trak, uint movieTimescale)
	{
		var elst = Payload (trak.FindChild ("edts")?.FindChild ("elst"));
		if (elst.Length < 8)
			return (TimeSpan.Zero, 0);

		var wide = elst[0] == 1;
		var entrySize = wide ? 20 : 12;
		var count = BinaryPrimitives.ReadUInt32BigEndian (elst.Slice (4));
		var delay = TimeSpan.Zero;
		var pos = 8;
		for (var i = 0u; i < count && i < 2 && elst.Length - pos >= entrySize; i++, pos += entrySize) {
			ulong segment;
			long mediaTime;
			if (wide) {
				segment = BinaryPrimitives.ReadUInt64BigEndian (elst.Slice (pos));
				mediaTime = BinaryPrimitives.ReadInt64BigEndian (elst.Slice (pos + 8));
			} else {
				segment = BinaryPrimitives.ReadUInt32BigEndian (elst.Slice (pos));
				mediaTime = BinaryPrimitives.ReadInt32BigEndian (elst.Slice (pos + 4));
			}

			if (mediaTime == -1) {
				if (i == 0 && movieTimescale > 0)
					delay = ToTime (segment, movieTimescale);
				continue;
			}

			return (delay, mediaTime < 0 ? 0 : (ulong)mediaTime);
		}

		return (delay, 0);
	}

	// ---- sample tables

	/// <summary>
	/// stsz (§8.7.3.2): a constant size repeats for sample_count samples, capped at
	/// <see cref="MaxTrackChapters"/>; otherwise one entry per sample, as many as the box holds.
	/// </summary>
	static List<uint> ReadSampleSizes (ReadOnlySpan<byte> stsz)
	{
		var sizes = new List<uint> ();
		if (stsz.Length < 12)
			return sizes;

		var constant = BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (4));
		var count = BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (8));
		var limit = (int)Math.Min (count, (uint)MaxTrackChapters);
		if (constant != 0) {
			for (var i = 0; i < limit; i++)
				sizes.Add (constant);
			return sizes;
		}

		for (var i = 0; i < limit && 12 + ((i + 1) * 4) <= stsz.Length; i++)
			sizes.Add (BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (12 + (i * 4))));
		return sizes;
	}

	static List<long> ReadChunkOffsets (ReadOnlySpan<byte> payload, int width)
	{
		var offsets = new List<long> ();
		if (payload.Length < 8)
			return offsets;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0u; i < count && payload.Length - pos >= width; i++, pos += width) {
			if (width == 4) {
				offsets.Add (BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos)));
			} else {
				var value = BinaryPrimitives.ReadUInt64BigEndian (payload.Slice (pos));
				offsets.Add (value > long.MaxValue ? long.MaxValue / 2 : (long)value);
			}
		}

		return offsets;
	}

	static List<StscRow> ReadStscRows (ReadOnlySpan<byte> payload)
	{
		var rows = new List<StscRow> ();
		if (payload.Length < 8)
			return rows;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0u; i < count && payload.Length - pos >= 12; i++, pos += 12) {
			rows.Add (new StscRow (
				BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos)),
				BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos + 4))));
		}

		return rows;
	}

	static List<SttsEntry> ReadTimeToSample (ReadOnlySpan<byte> payload)
	{
		var entries = new List<SttsEntry> ();
		if (payload.Length < 8)
			return entries;

		var count = BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (4));
		var pos = 8;
		for (var i = 0u; i < count && payload.Length - pos >= 8; i++, pos += 8) {
			entries.Add (new SttsEntry (
				BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos)),
				BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos + 4))));
		}

		return entries;
	}

	// ---- helpers

	/// <summary>
	/// Media units to time, without overflow: whole seconds and the remainder are scaled apart.
	/// </summary>
	internal static TimeSpan ToTime (ulong value, uint timescale)
	{
		if (timescale == 0)
			return TimeSpan.Zero;

		var seconds = value / timescale;
		var remainder = value % timescale;
		if (seconds > (ulong)(TimeSpan.MaxValue.Ticks / TicksPerSecond) - 1)
			return TimeSpan.MaxValue;

		return TimeSpan.FromTicks ((long)(seconds * TicksPerSecond) + (long)(remainder * TicksPerSecond / timescale));
	}

	/// <summary>
	/// Time to media units, rounded to the nearest unit, saturating at <see cref="ulong.MaxValue"/>.
	/// </summary>
	internal static ulong ToMedia (TimeSpan time, uint timescale)
	{
		if (time <= TimeSpan.Zero)
			return 0;

		var ticks = (ulong)time.Ticks;
		var seconds = ticks / TicksPerSecond;
		var remainder = ticks % TicksPerSecond;
		if (seconds > ulong.MaxValue / timescale - 1)
			return ulong.MaxValue;

		return (seconds * timescale) + ((remainder * timescale) + (TicksPerSecond / 2)) / TicksPerSecond;
	}

	static ReadOnlySpan<byte> Payload (Mp4Box? box) => box is null ? ReadOnlySpan<byte>.Empty : box.Data.Span;

	static string ChplTitle (string title) => DecodeUtf8 (TruncateUtf8 (title, MaxChplTitleBytes));

	/// <summary>
	/// UTF-8 bytes of <paramref name="value"/>, cut on a character boundary to at most <paramref name="maxBytes"/>.
	/// </summary>
	internal static byte[] TruncateUtf8 (string value, int maxBytes)
	{
		var bytes = Encoding.UTF8.GetBytes (value);
		if (bytes.Length <= maxBytes)
			return bytes;

		var cut = maxBytes;
		while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
			cut--;
		return bytes.AsSpan (0, cut).ToArray ();
	}

	static string DecodeUtf8 (ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString (bytes).TrimEnd ('\0');

	static BinaryData WrapBox (string type, ReadOnlySpan<byte> body)
	{
		using var builder = new BinaryDataBuilder (Mp4Box.HeaderSize + body.Length);
		builder.AddUInt32BE ((uint)(Mp4Box.HeaderSize + body.Length));
		builder.AddStringLatin1 (type);
		builder.Add (body);
		return builder.ToBinaryData ();
	}

	/// <summary>
	/// Child boxes of a payload. Stops at a size that does not fit; size 0 runs to the end; size 1 is largesize.
	/// </summary>
	static List<(int Start, int Length, int Header, string Type)> Children (ReadOnlySpan<byte> payload)
	{
		var children = new List<(int, int, int, string)> ();
		var pos = 0;
		while (payload.Length - pos >= 8) {
			var size = (long)BinaryPrimitives.ReadUInt32BigEndian (payload.Slice (pos));
			var header = 8;
			if (size == 1) {
				if (payload.Length - pos < 16)
					break;
				var large = BinaryPrimitives.ReadUInt64BigEndian (payload.Slice (pos + 8));
				size = large > long.MaxValue ? -1 : (long)large;
				header = 16;
			} else if (size == 0) {
				size = payload.Length - pos;
			}

			if (size < header || size > payload.Length - pos)
				break;

			children.Add ((pos, (int)size, header, Polyfills.Latin1.GetString (payload.Slice (pos + 4, 4))));
			pos += (int)size;
		}

		return children;
	}

	/// <summary>
	/// The payload at the end of <paramref name="path"/>, or empty.
	/// </summary>
	static Span<byte> FindPath (Span<byte> payload, params string[] path)
	{
		var current = payload;
		foreach (var name in path) {
			var found = false;
			foreach (var (start, length, header, type) in Children (current)) {
				if (type != name)
					continue;

				current = current.Slice (start + header, length - header);
				found = true;
				break;
			}

			if (!found)
				return Span<byte>.Empty;
		}

		return current;
	}

	readonly record struct StscRow (uint FirstChunk, uint SamplesPerChunk);

	readonly record struct SttsEntry (uint Count, uint Delta);

	/// <summary>
	/// The chapter trak and its track_ID.
	/// </summary>
	internal sealed class ChapterTrack (Mp4Box trak, uint id)
	{
		public Mp4Box Trak { get; } = trak;

		public uint Id { get; } = id;
	}

	/// <summary>
	/// One chapter sample: file offset, stsz size, bytes to read, start and duration in the media timescale.
	/// </summary>
	internal readonly record struct TextSample (long Offset, uint Size, int ReadLength, ulong Start, ulong Duration);

	/// <summary>
	/// The chapter track's samples and how its media time maps onto the movie timeline.
	/// </summary>
	internal sealed class TrackLayout (uint timescale, List<TextSample> samples, (TimeSpan Delay, ulong MediaStart) edit)
	{
		public List<TextSample> Samples { get; } = samples;

		public TimeSpan StartOf (int index) => TimeOf (Samples[index].Start);

		public TimeSpan TimeOf (ulong mediaTime)
		{
			var shown = mediaTime > edit.MediaStart ? mediaTime - edit.MediaStart : 0;
			var time = ToTime (shown, timescale);
			return time > TimeSpan.MaxValue - edit.Delay ? TimeSpan.MaxValue : time + edit.Delay;
		}
	}

	/// <summary>
	/// The result of planning a chapter save. <see cref="TrakOrdinal"/> is -1 when no chapter track is written.
	/// <see cref="Chpl"/> is empty when chpl is removed.
	/// </summary>
	internal sealed class ChapterPlan (
		BinaryData chpl,
		List<Mp4Box> children,
		int trakOrdinal,
		byte[] sampleData,
		string? error = null,
		List<ChapterTrack>? replacedTraks = null)
	{
		/// <summary>
		/// The trak objects this plan rebuilds or removes. Their old samples are no longer referenced.
		/// </summary>
		public List<ChapterTrack> ReplacedTraks { get; } = replacedTraks ?? [];

		/// <summary>Why the chapters cannot be written, or null.</summary>
		public string? Error { get; } = error;

		/// <summary>Lookups a clear made in the set of removed traks: a step count for the work-bound tests.</summary>
		public long TrakLookups { get; init; }

		/// <summary>
		/// Samples and modifier boxes a set examined to tell title tracks from URL tracks: a step count for the
		/// work-bound tests. It never exceeds the file's length.
		/// </summary>
		public long SampleSteps { get; init; }

		public static ChapterPlan Refused (string error) => new (BinaryData.Empty, [], -1, [], error);

		public BinaryData Chpl { get; } = chpl;

		public List<Mp4Box> Children { get; } = children;

		/// <summary>
		/// The position of the new or rebuilt chapter trak among the traks in <see cref="Children"/>, or -1 when
		/// there are no chapter samples to write. The rendered moov keeps that order.
		/// </summary>
		public int TrakOrdinal { get; } = trakOrdinal;

		public byte[] SampleData { get; } = sampleData;
	}

	/// <summary>
	/// A file as a span, or as a stream plus a length. <see cref="Slice"/> of a stream reads that window
	/// and no more; each read is at most <see cref="StreamCopy.ChunkSize"/>. The returned span is valid
	/// until the next <see cref="Slice"/>.
	/// </summary>
	internal readonly ref struct FileBytes
	{
		readonly ReadOnlySpan<byte> _span;
		readonly Stream? _stream;
		readonly byte[]? _chunk;

		public FileBytes (ReadOnlySpan<byte> span)
		{
			_span = span;
			Length = span.Length;
		}

		// chunk is a StreamCopy.Rent buffer. Only its first ChunkSize bytes are used.
		public FileBytes (Stream stream, long length, byte[] chunk)
		{
			_stream = stream;
			_chunk = chunk;
			Length = length;
		}

		public long Length { get; }

		public ReadOnlySpan<byte> Slice (long offset, int count)
		{
			if (_stream is null)
				return _span.Slice ((int)offset, count);

			var dest = count <= StreamCopy.ChunkSize ? _chunk! : new byte[count];
			if (_stream.Position != offset)
				_stream.Position = offset;
			var read = 0;
			while (read < count) {
				var n = _stream.Read (dest, read, Math.Min (StreamCopy.ChunkSize, count - read));
				if (n <= 0)
					throw new EndOfStreamException ("The source ended before the requested bytes were copied");
				read += n;
			}

			return dest.AsSpan (0, count);
		}
	}
}
