// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;

namespace TagLibSharp2.Aac;

/// <summary>
/// Finds and measures the ADTS or ADIF stream between a file's leading and trailing tags.
/// </summary>
/// <remarks>
/// <para>
/// Every read is bounded by the audio region. ADTS frames are walked header by header:
/// 7 bytes per frame are read and the payload is skipped, so the work is linear in the
/// frame count and the memory is constant.
/// </para>
/// <para>
/// A frame counts when its whole aac_frame_length fits before the trailing tags. A header
/// that does not parse, or that names a different stream than the first frame, starts a
/// forward search for the next frame whose successor confirms it. Each byte is searched
/// once, so a damaged file stays linear.
/// </para>
/// </remarks>
internal static class AacStream
{
	/// <summary>
	/// How far past the leading tag the first frame may start. Encoders and taggers can leave
	/// padding between them.
	/// </summary>
	public const int FirstFrameSearchBytes = 8192;

	// aac_frame_length is 13 bits, so a frame's successor header starts at most this far past it.
	const int MaxFrameLength = 8191;
	const int SuccessorReach = MaxFrameLength + AdtsHeader.Size;

	const int SearchChunkBytes = 16384;

	// A program_config_element is at most 2465 bits (see AdifHeader.MaxSize), plus the 3-bit element id.
	const int MaxProgramConfigBytes = 320;

	// ISO/IEC 13818-7 §6.3 raw_data_block: id_syn_ele ID_PCE.
	const int ProgramConfigElementId = 5;

	// The first stream read decides most files: an MPEG or ADTS header at the start.
	const int FirstProbeBytes = 16;
	const int ProbeChunkBytes = 4096;

	enum SyncKind { Adts, Mpeg }

	enum Verdict { Skip, Aac, Mp3 }

	readonly struct Candidate (SyncKind kind, AdtsHeader adts, int distance, int need)
	{
		public SyncKind Kind => kind;
		public AdtsHeader Adts => adts;

		/// <summary>Offset of the successor header from this one: the frame length.</summary>
		public int Distance => distance;

		/// <summary>Bytes of the successor header needed to check it.</summary>
		public int Need => need;
	}

	// A position whose bytes parse as an ADTS header (layer '00') or an MPEG audio frame header
	// (layer other than '00', ISO/IEC 11172-3 §2.4.2.3). The layer bits keep the two apart.
	static bool TryCandidate (ReadOnlySpan<byte> head, out Candidate candidate)
	{
		if (AdtsHeader.TryParse (head, out var adts)) {
			candidate = new Candidate (SyncKind.Adts, adts, adts.FrameLength, AdtsHeader.Size);
			return true;
		}

		if (MpegFrame.TryParse (head, out var mpeg) && mpeg is not null) {
			candidate = new Candidate (SyncKind.Mpeg, default, mpeg.FrameSize, MpegFrame.HeaderSize);
			return true;
		}

		candidate = default;
		return false;
	}

	/// <summary>
	/// Decides one candidate. <paramref name="room"/> is the bytes from the candidate to the end of the audio;
	/// <paramref name="successor"/> holds the header the candidate points at when that header lies inside the audio.
	/// A confirmed header decides for its format. An MPEG header that cannot be checked (its frame or its
	/// successor runs past the audio, or it is free format) also decides MP3, so truncated MP3 data stays MP3.
	/// An ADTS header that cannot be checked decides AAC only as the first byte of the search.
	/// A contradicted header is skipped. The end of the audio is the end of the data or the start of a trailing
	/// tag, and either one ends a frame the same way, so a trailing tag never changes the answer.
	/// </summary>
	static Verdict Judge (Candidate candidate, long room, bool first, ReadOnlySpan<byte> successor)
	{
		var checkable = candidate.Distance >= candidate.Need
			&& room >= (long)candidate.Distance + candidate.Need
			&& successor.Length >= candidate.Need;

		if (candidate.Kind == SyncKind.Mpeg) {
			if (!checkable)
				return Verdict.Mp3;
			return MpegFrame.TryParse (successor, out _) ? Verdict.Mp3 : Verdict.Skip;
		}

		if (!checkable)
			return first ? Verdict.Aac : Verdict.Skip;

		return AdtsHeader.TryParse (successor, out var next) && next.SameStream (candidate.Adts) ? Verdict.Aac : Verdict.Skip;
	}

	// A candidate judged against the audio end. Its own header must lie inside the audio: a header cut by a
	// trailing tag is not a header.
	static Verdict JudgeAt (ReadOnlySpan<byte> data, int i, Candidate candidate, long audioEnd)
	{
		if (i + (long)candidate.Need > audioEnd)
			return Verdict.Skip;

		var at = (long)i + candidate.Distance;
		var successor = at + candidate.Need <= audioEnd ? data.Slice ((int)at, candidate.Need) : [];
		return Judge (candidate, audioEnd - i, i == 0, successor);
	}

	/// <summary>
	/// The one rule, for detection and for reading, that says where an AAC stream starts in
	/// <paramref name="data"/>, which runs from the earliest possible start to the end of the file.
	/// "ADIF" at offset 0 starts an ADIF stream when its header parses inside the audio. Otherwise the first decisive
	/// sync in the first <paramref name="searchBytes"/> bytes decides (see <see cref="Judge"/>): an ADTS header
	/// starts an ADTS stream, an MPEG audio frame means the data is not AAC.
	/// </summary>
	/// <param name="data">The bytes from the earliest possible start to the end of the file.</param>
	/// <param name="searchBytes">How many start offsets to try.</param>
	/// <param name="audioEnd">Where the audio ends in <paramref name="data"/> (before any trailing tags), or -1 to
	/// find it with <see cref="AacFile.AudioEnd(ReadOnlySpan{byte})"/> when a candidate first needs it.
	/// An MPEG frame decided at the first byte, the usual MP3, never needs it.</param>
	/// <param name="headerLength">The length of the start header: 7 for ADTS, the ADIF header's size.</param>
	/// <returns>The offset of the stream in <paramref name="data"/>, or -1.</returns>
	public static int FindStart (ReadOnlySpan<byte> data, int searchBytes, long audioEnd, out int headerLength)
	{
		headerLength = AdtsHeader.Size;
		if (AdifHeader.HasMagic (data)) {
			if (audioEnd < 0)
				audioEnd = AacFile.AudioEnd (data);
			var bound = (int)Math.Min (AdifHeader.MaxSize, audioEnd);
			if (!AdifHeader.TryParse (data[..bound], out var adif))
				return -1;
			headerLength = adif.Size;
			return 0;
		}

		var limit = Math.Min (searchBytes, data.Length);
		for (var i = 0; i < limit; i++) {
			if (data[i] != 0xFF || !TryCandidate (data[i..], out var candidate))
				continue;

			// An ADTS candidate always needs the audio end. An MPEG candidate is judged against the end of the
			// data first: MP3 decided there is MP3 against any earlier audio end too (a successor past the audio
			// is uncheckable, which also decides MP3), so a plain MP3 never reads its tail here.
			if (audioEnd < 0 && candidate.Kind == SyncKind.Adts)
				audioEnd = AacFile.AudioEnd (data);
			var verdict = JudgeAt (data, i, candidate, audioEnd < 0 ? data.Length : audioEnd);
			if (audioEnd < 0 && verdict != Verdict.Mp3) {
				audioEnd = AacFile.AudioEnd (data);
				verdict = JudgeAt (data, i, candidate, audioEnd);
			}

			switch (verdict) {
				case Verdict.Aac:
					return i;
				case Verdict.Mp3:
					return -1;
			}
		}

		return -1;
	}

	/// <summary>
	/// The stream twin of <see cref="FindStart(ReadOnlySpan{byte}, int, long, out int)"/>, for the data from
	/// <paramref name="offset"/> to the end of the stream. It reads 16 bytes, then 4 KB chunks only while no sync has
	/// decided, and each successor header on its own, so a file decided at its first byte costs about 23 bytes.
	/// The tail is read (<see cref="AacFile.AudioEnd(Stream, long)"/>) only when <paramref name="audioEnd"/> is -1 and
	/// a candidate needs it. The stream position moves.
	/// </summary>
	public static long FindStart (Stream stream, long offset, int searchBytes, long audioEnd, out int headerLength)
	{
		headerLength = AdtsHeader.Size;
		var end = stream.Length;
		if (offset < 0 || offset >= end)
			return -1;

		var limit = Math.Min (offset + searchBytes, end);
		var chunk = new byte[FirstProbeBytes];
		var small = new byte[AdtsHeader.Size];
		var pos = offset;
		while (pos < limit) {
			var length = (int)Math.Min (chunk.Length, end - pos);
			if (!ReadAt (stream, pos, chunk, length))
				return -1;
			if (pos == offset && AdifHeader.HasMagic (chunk.AsSpan (0, length))) {
				if (audioEnd < 0)
					audioEnd = AacFile.AudioEnd (stream, offset);
				if (!TryReadAdifHeader (stream, offset, audioEnd, out var adif))
					return -1;
				headerLength = adif.Size;
				return offset;
			}

			var scan = (int)Math.Min (length, limit - pos);
			for (var i = 0; i < scan; i++) {
				if (chunk[i] != 0xFF)
					continue;

				var here = pos + i;
				ReadOnlySpan<byte> head = chunk.AsSpan (i, length - i);
				if (head.Length < AdtsHeader.Size && end - here > head.Length) {
					var headLength = (int)Math.Min (AdtsHeader.Size, end - here);
					if (!ReadAt (stream, here, small, headLength))
						return -1;
					head = small.AsSpan (0, headLength);
				}

				if (!TryCandidate (head, out var candidate))
					continue;

				var context = new ChunkView (chunk, pos, length);
				if (audioEnd < 0 && candidate.Kind == SyncKind.Adts)
					audioEnd = AacFile.AudioEnd (stream, offset);
				var verdict = JudgeAt (stream, context, small, here, offset, candidate, audioEnd < 0 ? end : audioEnd);
				if (audioEnd < 0 && verdict != Verdict.Mp3) {
					audioEnd = AacFile.AudioEnd (stream, offset);
					verdict = JudgeAt (stream, context, small, here, offset, candidate, audioEnd);
				}

				switch (verdict) {
					case Verdict.Aac:
						return here;
					case Verdict.Mp3:
						return -1;
				}
			}

			pos += scan;
			if (chunk.Length == FirstProbeBytes)
				chunk = new byte[ProbeChunkBytes];
		}

		return -1;
	}

	readonly struct ChunkView (byte[] bytes, long start, int length)
	{
		public byte[] Bytes => bytes;
		public long Start => start;
		public int Length => length;
	}

	// The stream twin of the span JudgeAt: the successor comes from the chunk when it is there, else one read.
	static Verdict JudgeAt (Stream stream, ChunkView chunk, byte[] small, long here, long offset, Candidate candidate, long audioEnd)
	{
		if (here + candidate.Need > audioEnd)
			return Verdict.Skip;

		var at = here + candidate.Distance;
		ReadOnlySpan<byte> successor = [];
		if (at + candidate.Need <= audioEnd) {
			if (at + candidate.Need <= chunk.Start + chunk.Length)
				successor = chunk.Bytes.AsSpan ((int)(at - chunk.Start), candidate.Need);
			else if (ReadAt (stream, at, small, candidate.Need))
				successor = small.AsSpan (0, candidate.Need);
		}

		return Judge (candidate, audioEnd - here, here == offset, successor);
	}

	/// <summary>
	/// True when the header at the start of <paramref name="rest"/> is confirmed. Its successor names the same
	/// stream, or is a trailing tag, or cannot be checked: the frame reaches the end of the data
	/// (<paramref name="room"/>) or of the bytes in hand.
	/// </summary>
	static bool IsConfirmed (AdtsHeader header, ReadOnlySpan<byte> rest, long room)
	{
		var successor = header.FrameLength + AdtsHeader.Size;
		if (room < successor || rest.Length < successor)
			return true;

		var next = rest.Slice (header.FrameLength, AdtsHeader.Size);
		return AdtsHeader.TryParse (next, out var following) && following.SameStream (header);
	}

	/// <summary>
	/// Reads the stream properties from the audio region [<paramref name="start"/>, <paramref name="end"/>).
	/// The stream starts where <see cref="FindStart(ReadOnlySpan{byte}, int, long, out int)"/> says, within <see cref="FirstFrameSearchBytes"/>.
	/// </summary>
	/// <remarks>
	/// The start is judged against the audio region, as detection judges it: the start header and any successor
	/// that checks it must lie before the trailing tags, so a header cut by a trailing tag is not AAC and a tag
	/// never confirms or contradicts a frame. Frames are counted only inside the audio region.
	/// </remarks>
	public static bool TryRead (Stream stream, long start, long end, out AacAudioProperties? properties)
	{
		properties = null;
		if (start < 0 || end > stream.Length || start > end)
			return false;

		var first = FindStart (stream, start, FirstFrameSearchBytes, end, out _);
		if (first < 0)
			return false;

		var magic = new byte[4];
		if (first == start && ReadAt (stream, start, magic, 4) && AdifHeader.HasMagic (magic))
			return TryReadAdif (stream, start, end, out properties);

		return TryWalkAdts (new ScanReader (stream, end), first, end, out properties);
	}

	// Most ADIF headers are a few dozen bytes. Read a short prefix first and the bounded maximum only when
	// the header does not fit in it.
	const int AdifProbeBytes = 256;

	static bool TryReadAdifHeader (Stream stream, long start, long end, out AdifHeader header)
	{
		header = default;
		var room = end - start;
		var length = (int)Math.Min (AdifProbeBytes, room);
		var bytes = new byte[length];
		if (!ReadAt (stream, start, bytes, length))
			return false;
		if (AdifHeader.TryParse (bytes, out header))
			return true;
		if (room <= AdifProbeBytes)
			return false;

		bytes = new byte[(int)Math.Min (AdifHeader.MaxSize, room)];
		return ReadAt (stream, start, bytes, bytes.Length) && AdifHeader.TryParse (bytes, out header);
	}

	static bool TryReadAdif (Stream stream, long start, long end, out AacAudioProperties? properties)
	{
		properties = null;
		// The header and the payload both end at the trailing tags, as detection bounds them.
		if (!TryReadAdifHeader (stream, start, end, out var header))
			return false;

		var payload = end - start - header.Size;
		var duration = header.Bitrate > 0 && payload > 0
			? TimeSpan.FromTicks ((long)Math.Round ((double)payload * 8 * TimeSpan.TicksPerSecond / header.Bitrate))
			: TimeSpan.Zero;
		var program = header.Program;
		properties = new AacAudioProperties (
			duration,
			header.Bitrate / 1000,
			AdtsHeader.SampleRateFor (program.SampleRateIndex),
			program.Channels,
			program.ProfileBits == 3 ? AacProfile.Unknown : (AacProfile)(program.ProfileBits + 1),
			AacStreamFormat.Adif,
			0,
			0);
		return true;
	}

	static bool TryWalkAdts (ScanReader reader, long first, long end, out AacAudioProperties? properties)
	{
		properties = null;
		if (!reader.TryHeader (first, out var stream0))
			return false;

		var channels = stream0.Channels;
		if (stream0.ChannelConfiguration == 0)
			channels = ProgramConfigChannels (reader.Stream, first, stream0, end);

		long frames = 0;
		long blocks = 0;
		long bytes = 0;
		var pos = first;
		while (pos <= end - AdtsHeader.Size) {
			if (!reader.TryHeader (pos, out var header) || !header.SameStream (stream0)) {
				var found = FindFrame (reader, pos + 1, stream0);
				if (found < 0)
					break;
				pos = found;
				continue;
			}

			if (header.FrameLength > end - pos)
				break;

			frames++;
			blocks += header.RawDataBlocks;
			bytes += header.FrameLength;
			pos += header.FrameLength;
		}

		var sampleRate = stream0.SampleRate;
		var samples = (double)blocks * AdtsHeader.SamplesPerBlock;
		var duration = TimeSpan.FromTicks ((long)Math.Round (samples * TimeSpan.TicksPerSecond / sampleRate));
		var bitrate = duration > TimeSpan.Zero
			? (int)(bytes * 8 / duration.TotalSeconds / 1000)
			: 0;
		properties = new AacAudioProperties (
			duration,
			bitrate,
			sampleRate,
			channels,
			stream0.Profile,
			AacStreamFormat.Adts,
			stream0.IsMpeg2 ? 2 : 4,
			frames);
		return true;
	}

	// channel_configuration 0: the first raw data block opens with the program config element
	// (ISO/IEC 14496-3 §1.A.2.2.1). Anything else leaves the count unknown.
	static int ProgramConfigChannels (Stream stream, long frame, AdtsHeader header, long end)
	{
		var at = frame + header.PayloadOffset;
		var room = Math.Min (frame + header.FrameLength, end) - at;
		var length = (int)Math.Min (MaxProgramConfigBytes, room);
		if (length <= 0)
			return 0;

		var bytes = new byte[length];
		if (!ReadAt (stream, at, bytes, length))
			return 0;

		var reader = new AacBitReader (bytes);
		if (reader.Read (3) != ProgramConfigElementId)
			return 0;

		return AacProgramConfig.TryRead (ref reader, out var config) ? config.Channels : 0;
	}

	/// <summary>
	/// Finds the first ADTS header at or after <paramref name="from"/> that names <paramref name="sameAs"/>'s
	/// stream and is confirmed (see <see cref="IsConfirmed"/>). Returns -1 when there is none.
	/// Candidates and their successors are read from the scan buffer, so the search reads each byte
	/// about once however many false syncs it rejects.
	/// </summary>
	static long FindFrame (ScanReader reader, long from, AdtsHeader sameAs)
	{
		var end = reader.End;
		var at = from;
		while (at <= end - AdtsHeader.Size) {
			var chunk = reader.Search (at);
			if (chunk.Length < AdtsHeader.Size)
				return -1;

			// Every candidate below this offset has its farthest successor in the chunk, or the chunk reaches the end.
			var candidates = at + chunk.Length >= end
				? chunk.Length - AdtsHeader.Size + 1
				: chunk.Length - SuccessorReach + 1;
			for (var i = 0; i < candidates; i++) {
				if (chunk[i] != 0xFF)
					continue;

				var rest = chunk[i..];
				if (!AdtsHeader.TryParse (rest, out var candidate) || !candidate.SameStream (sameAs))
					continue;

				if (IsConfirmed (candidate, rest, end - (at + i)))
					return at + i;
			}

			at += candidates;
		}

		return -1;
	}

	/// <summary>
	/// Reads frame headers for one scan. A header inside the search buffer comes from it. Any other
	/// header is a 7-byte read, so walking well-formed frames never reads their payload.
	/// </summary>
	sealed class ScanReader (Stream stream, long end)
	{
		byte[]? _buffer;
		readonly byte[] _header = new byte[AdtsHeader.Size];
		long _start = -1;
		int _length;

		public Stream Stream => stream;

		public long End => end;

		/// <summary>
		/// The bytes from <paramref name="at"/> for a search. The buffer is kept while it still holds more
		/// than one successor reach past <paramref name="at"/> (or holds the end); otherwise it refills at
		/// <paramref name="at"/>. Each refill therefore moves at least one chunk forward.
		/// </summary>
		public ReadOnlySpan<byte> Search (long at)
		{
			var bufferEnd = _start + _length;
			var inside = _start >= 0 && at >= _start && at < bufferEnd;
			if (!inside || (bufferEnd < end && bufferEnd - at < SuccessorReach)) {
				// Allocated on the first resync. A clean file never needs it.
				_buffer ??= new byte[SearchChunkBytes + SuccessorReach];
				var length = (int)Math.Min (_buffer.Length, end - at);
				if (!ReadAt (stream, at, _buffer, length))
					return [];
				_start = at;
				_length = length;
			}

			return _buffer!.AsSpan ((int)(at - _start), (int)(_start + _length - at));
		}

		public bool TryHeader (long pos, out AdtsHeader header)
		{
			if (_buffer is not null && _start >= 0 && pos >= _start && pos + AdtsHeader.Size <= _start + _length)
				return AdtsHeader.TryParse (_buffer.AsSpan ((int)(pos - _start), AdtsHeader.Size), out header);

			header = default;
			return ReadAt (stream, pos, _header, AdtsHeader.Size) && AdtsHeader.TryParse (_header, out header);
		}
	}

	static bool ReadAt (Stream stream, long position, byte[] buffer, int count)
	{
		if (count <= 0 || position < 0 || position > stream.Length - count)
			return false;

		stream.Position = position;
		return StreamBytes.Read (stream, buffer, 0, count) == count;
	}
}
