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
	public static List<ChapterTrack> FindChapterTracks (Mp4Box moov)
	{
		var traks = new List<(Mp4Box Trak, uint Id)> ();
		var byId = new Dictionary<uint, Mp4Box> ();
		foreach (var child in moov.Children) {
			if (child.Type != "trak" || !TryReadTrackId (child, out var id))
				continue;

			traks.Add ((child, id));
			if (!byId.ContainsKey (id))
				byId[id] = child;
		}

		var found = new List<ChapterTrack> ();
		var seen = new HashSet<uint> ();
		foreach (var (trak, id) in traks) {
			var tref = trak.FindChild ("tref");
			if (tref is null)
				continue;

			foreach (var reference in ReadChapterReferences (tref.Data.Span)) {
				if (reference != id && byId.TryGetValue (reference, out var target) && IsTextHandler (target) && seen.Add (reference))
					found.Add (new ChapterTrack (target, reference));
			}
		}

		return found;
	}

	/// <summary>
	/// Locates the chapter track's samples. Null when its sample table cannot be read.
	/// Each sample's read length stops after the 16-bit length and 65,535 text bytes.
	/// Total read length stops at <paramref name="readBudget"/>, so overlapping samples cannot read more than the file.
	/// </summary>
	public static TrackLayout? ReadLayout (ChapterTrack track, uint movieTimescale, long readBudget)
	{
		var mdia = track.Trak.FindChild ("mdia");
		var timescale = ReadMediaTimescale (mdia);
		var stbl = mdia?.FindChild ("minf")?.FindChild ("stbl");
		if (timescale == 0 || stbl is null)
			return null;

		var stsz = stbl.FindChild ("stsz");
		var stsc = stbl.FindChild ("stsc");
		var stts = stbl.FindChild ("stts");
		var stco = stbl.FindChild ("stco");
		var co64 = stbl.FindChild ("co64");
		if (stsz is null || stsc is null || (stco is null && co64 is null))
			return null;

		var sizes = ReadSampleSizes (stsz.Data.Span);
		var chunks = co64 is not null ? ReadChunkOffsets (co64.Data.Span, 8) : ReadChunkOffsets (stco!.Data.Span, 4);
		var rows = ReadStscRows (stsc.Data.Span);
		var deltas = stts is null ? new List<SttsEntry> () : ReadTimeToSample (stts.Data.Span);

		var samples = new List<TextSample> (Math.Min (sizes.Count, 64));
		var row = 0;
		var sampleIndex = 0;
		var deltaIndex = 0;
		var deltaLeft = deltas.Count > 0 ? deltas[0].Count : 0u;
		ulong time = 0;
		long budget = readBudget;
		for (var chunk = 0; chunk < chunks.Count && sampleIndex < sizes.Count; chunk++) {
			var chunkNumber = (uint)chunk + 1;
			while (row + 1 < rows.Count && rows[row + 1].FirstChunk <= chunkNumber)
				row++;
			var perChunk = rows.Count > 0 && rows[row].FirstChunk <= chunkNumber ? rows[row].SamplesPerChunk : 0u;

			var offset = chunks[chunk];
			for (var k = 0u; k < perChunk && sampleIndex < sizes.Count; k++) {
				var size = sizes[sampleIndex];
				var readLength = (int)Math.Min (size, (uint)SampleReadLimit);
				if (readLength > budget)
					return samples.Count == 0 ? null : new TrackLayout (timescale, samples, ReadEdit (track.Trak, movieTimescale));
				budget -= readLength;

				while (deltaLeft == 0 && deltaIndex + 1 < deltas.Count) {
					deltaIndex++;
					deltaLeft = deltas[deltaIndex].Count;
				}

				var delta = deltaLeft > 0 ? deltas[deltaIndex].Delta : 0u;
				if (deltaLeft > 0)
					deltaLeft--;

				samples.Add (new TextSample (offset, size, readLength, time, delta));
				time += delta;
				offset += size;
				sampleIndex++;
			}
		}

		if (samples.Count == 0)
			return null;

		return new TrackLayout (timescale, samples, ReadEdit (track.Trak, movieTimescale));
	}

	/// <summary>
	/// Decodes one chapter sample: a 16-bit length, then UTF-16 when a byte order mark leads, otherwise UTF-8.
	/// A length past the sample stops at the sample end. Modifier boxes after the text are ignored.
	/// </summary>
	public static string DecodeSample (ReadOnlySpan<byte> sample)
	{
		if (sample.Length < 2)
			return string.Empty;

		var length = Math.Min (BinaryPrimitives.ReadUInt16BigEndian (sample), sample.Length - 2);
		var text = sample.Slice (2, length);
		string value;
		if (text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF)
			value = Encoding.BigEndianUnicode.GetString (text.Slice (2));
		else if (text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE)
			value = Encoding.Unicode.GetString (text.Slice (2));
		else
			value = DecodeUtf8 (text);

		return value.TrimEnd ('\0');
	}

	/// <summary>
	/// Builds the chapter list. The chapter track wins over chpl, as ffprobe reads it. A null title is a
	/// sample outside the file: chpl wins then, and without chpl that chapter's title is empty.
	/// When chpl has the same titles in the same order, its 100 ns start times are used: the track starts its
	/// first sample at zero and rounds to its timescale. End times follow the next start, the track end, or
	/// <paramref name="duration"/>.
	/// </summary>
	public static List<Mp4Chapter> Merge (
		TrackLayout? layout,
		IReadOnlyList<string?>? titles,
		List<Mp4Chapter>? chpl,
		TimeSpan duration)
	{
		var chapters = new List<Mp4Chapter> ();
		TimeSpan? trackEnd = null;
		var hasChpl = chpl is not null && chpl.Count > 0;
		if (layout is not null && titles is not null && titles.Count == layout.Samples.Count && titles.Count > 0
			&& !(hasChpl && titles.Contains (null))) {
			var useChpl = chpl is not null && chpl.Count == titles.Count;
			for (var i = 0; useChpl && i < titles.Count; i++) {
				if (!string.Equals (chpl![i].Title, ChplTitle (titles[i]!), StringComparison.Ordinal))
					useChpl = false;
			}

			for (var i = 0; i < titles.Count; i++) {
				var start = useChpl ? chpl![i].StartTime : layout.StartOf (i);
				chapters.Add (new Mp4Chapter (start, titles[i] ?? string.Empty));
			}

			var last = layout.Samples[layout.Samples.Count - 1];
			if (last.Duration > 0)
				trackEnd = layout.TimeOf (last.Start + last.Duration);
		} else if (chpl is not null) {
			chapters.AddRange (chpl);
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
	/// Reads the chapter list from <paramref name="moov"/> and the whole file that contains it.
	/// </summary>
	public static List<Mp4Chapter> Read (Mp4Box moov, ReadOnlySpan<byte> file, TimeSpan duration)
	{
		var chpl = ReadChpl (moov);
		var track = FindChapterTrack (moov);
		var layout = track is null ? null : ReadLayout (track, ReadMovieTimescale (moov), file.Length);
		List<string?>? titles = null;
		if (layout is not null) {
			titles = new List<string?> (layout.Samples.Count);
			foreach (var sample in layout.Samples) {
				var inFile = sample.Offset >= 0 && sample.Offset <= file.Length - sample.ReadLength;
				titles.Add (inFile ? DecodeSample (file.Slice ((int)sample.Offset, sample.ReadLength)) : null);
			}
		}

		return Merge (layout, titles, chpl, duration);
	}

	/// <summary>
	/// Reads the chapter list from <paramref name="moov"/>, fetching chapter samples from <paramref name="stream"/>.
	/// </summary>
	public static List<Mp4Chapter> Read (Mp4Box moov, Stream stream, TimeSpan duration)
	{
		var chpl = ReadChpl (moov);
		var track = FindChapterTrack (moov);
		var length = stream.Length;
		var layout = track is null ? null : ReadLayout (track, ReadMovieTimescale (moov), length);
		List<string?>? titles = null;
		if (layout is not null) {
			titles = new List<string?> (layout.Samples.Count);
			foreach (var sample in layout.Samples) {
				string? title = null;
				if (sample.Offset >= 0 && sample.Offset <= length - sample.ReadLength) {
					stream.Position = sample.Offset;
					var bytes = StreamBytes.ReadAtMost (stream, sample.ReadLength);
					if (bytes.Length == sample.ReadLength)
						title = DecodeSample (bytes);
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
	/// An empty list removes chpl, every text track a <c>chap</c> reference names (Apple files can have a
	/// second one for chapter URLs), and the references to them. Chapter image tracks stay.
	/// </para>
	/// <para>
	/// No track is added to a fragmented movie (<c>mvex</c>, §8.8.1, needs a trex per track) or to a moov with
	/// no trak; chpl alone carries the list there.
	/// </para>
	/// <para>
	/// The new track's single chunk offset is written as 0. <see cref="WriteChunkOffset"/> sets it once the
	/// position of the sample data is known.
	/// </para>
	/// </remarks>
	public static ChapterPlan Plan (Mp4Box moov, IReadOnlyList<Mp4Chapter> chapters, TimeSpan duration, bool quickTime)
	{
		var chapterTracks = FindChapterTracks (moov);
		var existing = chapterTracks.Count > 0 ? chapterTracks[0] : null;
		var children = new List<Mp4Box> (moov.Children);
		if (chapters.Count == 0) {
			// Every chap text track reads as a chapter list, so all of them go. Chapter image tracks stay.
			var removed = new HashSet<uint> ();
			foreach (var track in chapterTracks) {
				children.Remove (track.Trak);
				removed.Add (track.Id);
			}

			if (removed.Count > 0)
				ReplaceReferences (children, removed, add: false);
			return new ChapterPlan (BinaryData.Empty, children, 0, [], removed);
		}

		var chpl = RenderChpl (chapters);
		var lastTrak = children.FindLastIndex (b => b.Type == "trak");
		if (existing is null && (lastTrak < 0 || moov.FindChild ("mvex") is not null))
			return new ChapterPlan (chpl, children, 0, []);

		var movieTimescale = ReadMovieTimescale (moov);
		uint trackId;
		uint timescale;
		if (existing is not null) {
			trackId = existing.Id;
			timescale = ReadMediaTimescale (existing.Trak.FindChild ("mdia"));
		} else {
			if (!TryNewTrackId (moov, out trackId))
				return new ChapterPlan (chpl, children, 0, []);
			timescale = movieTimescale;
		}

		if (timescale == 0)
			timescale = DefaultTimescale;

		var deltas = SampleDeltas (chapters, duration, ref timescale);
		var samples = RenderSamples (chapters, out var sizes);
		var trak = BuildTrak (trackId, timescale, movieTimescale, deltas, sizes, quickTime);

		if (existing is not null) {
			children[children.IndexOf (existing.Trak)] = trak;
		} else {
			children.Insert (lastTrak + 1, trak);
			var mvhdIndex = children.FindIndex (b => b.Type == "mvhd");
			if (mvhdIndex >= 0)
				children[mvhdIndex] = WithNextTrackId (children[mvhdIndex], trackId);
		}

		// A replaced track keeps its id, so the references that found it still do.
		if (existing is null)
			ReplaceReferences (children, new HashSet<uint> { trackId }, add: true);
		return new ChapterPlan (chpl, children, trackId, samples, existing is null ? null : [existing.Id]);
	}

	/// <summary>
	/// True when the box at [<paramref name="boxStart"/>, <paramref name="boxEnd"/>) holds nothing but the old
	/// samples of the tracks <paramref name="plan"/> replaces: those samples tile its payload exactly, with no gap
	/// and no overlap, and no other track has a chunk offset inside the box. Such an mdat is the one a previous
	/// chapter save wrote; once its tracks are rebuilt nothing references it, so it can be dropped.
	/// A fragmented movie never qualifies: movie fragments locate their data in other ways (§8.8).
	/// </summary>
	public static bool HoldsOnlyReplacedSamples (Mp4Box moov, ChapterPlan plan, long boxStart, long payloadStart, long boxEnd)
	{
		if (plan.ReplacedTrackIds.Count == 0 || moov.FindChild ("mvex") is not null)
			return false;

		var ranges = new List<(long Start, long End)> ();
		var movieTimescale = ReadMovieTimescale (moov);
		foreach (var track in FindChapterTracks (moov)) {
			if (!plan.ReplacedTrackIds.Contains (track.Id))
				continue;

			var layout = ReadLayout (track, movieTimescale, long.MaxValue);
			if (layout is null)
				return false;
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

		if (covered != boxEnd)
			return false;

		foreach (var child in moov.Children) {
			if (child.Type != "trak")
				continue;
			if (TryReadTrackId (child, out var id) && plan.ReplacedTrackIds.Contains (id))
				continue;

			var stbl = child.FindChild ("mdia")?.FindChild ("minf")?.FindChild ("stbl");
			var co64 = stbl?.FindChild ("co64");
			var stco = stbl?.FindChild ("stco");
			var offsets = co64 is not null ? ReadChunkOffsets (co64.Data.Span, 8)
				: stco is not null ? ReadChunkOffsets (stco.Data.Span, 4)
				: [];
			foreach (var offset in offsets) {
				if (offset >= boxStart && offset < boxEnd)
					return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Writes the chapter track's single chunk offset into a rendered moov.
	/// Finds the trak whose tkhd has <paramref name="trackId"/>, then its stco.
	/// </summary>
	public static bool WriteChunkOffset (Span<byte> moov, uint trackId, uint offset)
	{
		var boxes = Children (moov);
		if (boxes.Count == 0 || boxes[0].Type != "moov")
			return false;

		var body = moov.Slice (boxes[0].Header, boxes[0].Length - boxes[0].Header);
		foreach (var (start, length, header, type) in Children (body)) {
			if (type != "trak")
				continue;

			var trak = body.Slice (start + header, length - header);
			if (!TryTrackIdFromBytes (trak, out var id) || id != trackId)
				continue;

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

	static Mp4Box BuildTrak (uint trackId, uint timescale, uint movieTimescale, ulong[] deltas, uint[] sizes, bool quickTime)
	{
		ulong mediaDuration = 0;
		foreach (var delta in deltas)
			mediaDuration += delta;
		var movieDuration = movieTimescale == 0 ? 0 : ToMedia (ToTime (mediaDuration, timescale), movieTimescale);

		using var trak = new BinaryDataBuilder ();
		trak.Add (Tkhd (trackId, movieDuration));
		using (var mdia = new BinaryDataBuilder ()) {
			mdia.Add (Mdhd (timescale, mediaDuration));
			mdia.Add (Hdlr (quickTime));
			mdia.Add (Minf (deltas, sizes));
			trak.Add (WrapBox ("mdia", mdia.Span));
		}

		var bytes = WrapBox ("trak", trak.Span);
		return Mp4Box.Parse (bytes.Span).Box!;
	}

	/// <summary>
	/// tkhd (§8.3.2). Flags 2: in the movie, not enabled, so players do not show the titles as subtitles.
	/// No volume, identity matrix, no size.
	/// </summary>
	static BinaryData Tkhd (uint trackId, ulong duration)
	{
		var wide = duration > uint.MaxValue;
		using var body = new BinaryDataBuilder ();
		body.Add ((byte)(wide ? 1 : 0));
		body.AddUInt24BE (2);
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
	/// mdhd (§8.4.2), language "und" packed as three 5-bit letters.
	/// </summary>
	static BinaryData Mdhd (uint timescale, ulong duration)
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

		body.AddUInt16BE (0x55C4);
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
	/// minf with a QuickTime base media header (<c>gmhd</c> holding <c>gmin</c> and the text media
	/// <c>text</c> matrix; QuickTime File Format, "Base Media Information Header Atoms"), a self-contained
	/// data reference (§8.7.2), and the sample table. All samples sit in one chunk.
	/// </summary>
	static BinaryData Minf (ulong[] deltas, uint[] sizes)
	{
		using var minf = new BinaryDataBuilder ();

		using (var gmhd = new BinaryDataBuilder ()) {
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
			stbl.Add (Stsd ());
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
	/// One <c>text</c> sample entry with data_reference_index 1 (§8.5.2). The body is the 3GPP
	/// TextSampleEntry (TS 26.245 §5.16): display flags, justification, background colour, default text box,
	/// default style record, and a font table with one font. Files written by ffmpeg use the same body.
	/// </summary>
	static BinaryData Stsd ()
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

		using var stsd = new BinaryDataBuilder ();
		stsd.AddUInt32BE (0);
		stsd.AddUInt32BE (1);
		stsd.Add (WrapBox ("text", entry.Span));
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
	/// Adds the id in <paramref name="trackIds"/> to the <c>chap</c> reference of every sound and video track
	/// that is not itself chapter media, or removes every id in <paramref name="trackIds"/> from every
	/// <c>chap</c> reference. An emptied <c>chap</c> and an emptied <c>tref</c> are dropped. Other reference
	/// types are kept byte for byte.
	/// </summary>
	static void ReplaceReferences (List<Mp4Box> children, HashSet<uint> trackIds, bool add)
	{
		// A track that a chap reference names is chapter media (Apple files hold chapter images in a video
		// track). It is not a presentation track and does not get a chap reference of its own.
		var chapterMedia = new HashSet<uint> ();
		foreach (var child in children) {
			var tref = child.Type == "trak" ? child.FindChild ("tref") : null;
			if (tref is not null)
				chapterMedia.UnionWith (ReadChapterReferences (tref.Data.Span));
		}

		for (var i = 0; i < children.Count; i++) {
			var trak = children[i];
			if (trak.Type != "trak")
				continue;
			if (TryReadTrackId (trak, out var id) && trackIds.Contains (id))
				continue;

			var tref = trak.FindChild ("tref");
			if (add && (!IsAudioOrVideo (trak) || chapterMedia.Contains (id)))
				continue;
			if (!add && tref is null)
				continue;

			var newTref = RebuildTref (Payload (tref), trackIds, add);
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
	static BinaryData RebuildTref (ReadOnlySpan<byte> payload, HashSet<uint> trackIds, bool add)
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
			var ids = new List<uint> ();
			var body = payload.Slice (start + header, length - header);
			for (var pos = 0; pos + 4 <= body.Length; pos += 4) {
				var id = BinaryPrimitives.ReadUInt32BigEndian (body.Slice (pos));
				if (add || !trackIds.Contains (id))
					ids.Add (id);
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

	static bool TryTrackIdFromBytes (ReadOnlySpan<byte> trakBody, out uint id)
	{
		id = 0;
		foreach (var (start, length, header, type) in Children (trakBody)) {
			if (type == "tkhd")
				return TryTrackIdFromTkhd (trakBody.Slice (start + header, length - header), out id);
		}

		return false;
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
	/// The result of <see cref="Plan"/>. <see cref="TrackId"/> is 0 when no chapter track is written.
	/// <see cref="Chpl"/> is empty when chpl is removed.
	/// </summary>
	internal sealed class ChapterPlan (
		BinaryData chpl,
		List<Mp4Box> children,
		uint trackId,
		byte[] sampleData,
		HashSet<uint>? replaced = null)
	{
		/// <summary>The chapter tracks this plan rebuilds or removes. Their old samples are no longer referenced.</summary>
		public HashSet<uint> ReplacedTrackIds { get; } = replaced ?? [];

		public BinaryData Chpl { get; } = chpl;

		public List<Mp4Box> Children { get; } = children;

		public uint TrackId { get; } = trackId;

		public byte[] SampleData { get; } = sampleData;
	}
}
