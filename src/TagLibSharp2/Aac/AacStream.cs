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
	/// Decides one candidate. <paramref name="room"/> is the bytes from the candidate to the end of the data;
	/// <paramref name="successor"/> holds the header the candidate points at when that header lies inside the data.
	/// A confirmed header decides for its format. An MPEG header that cannot be checked (its frame or its
	/// successor runs past the data, or it is free format) also decides MP3, so truncated MP3 data stays MP3.
	/// An ADTS header that cannot be checked decides AAC only as the first byte of the search.
	/// A contradicted header is skipped.
	/// </summary>
	static Verdict Judge (Candidate candidate, long room, bool first, ReadOnlySpan<byte> successor)
	{
		var checkable = candidate.Distance >= candidate.Need
			&& room >= (long)candidate.Distance + candidate.Need
			&& successor.Length >= candidate.Need;

		if (candidate.Kind == SyncKind.Mpeg) {
			if (!checkable)
				return Verdict.Mp3;
			return MpegFrame.TryParse (successor, out _) || IsTrailingTag (successor) ? Verdict.Mp3 : Verdict.Skip;
		}

		if (!checkable)
			return first ? Verdict.Aac : Verdict.Skip;

		var confirmed = (AdtsHeader.TryParse (successor, out var next) && next.SameStream (candidate.Adts))
			|| IsTrailingTag (successor);
		return confirmed ? Verdict.Aac : Verdict.Skip;
	}

	// The frame ends where an ID3v1, APEv2 (with a header), or Lyrics3 tag begins.
	static bool IsTrailingTag (ReadOnlySpan<byte> next) =>
		next.StartsWith ("TAG"u8) || next.StartsWith ("APETAGE"u8) || next.StartsWith ("LYRICSB"u8);

	/// <summary>
	/// The one rule, for detection and for reading, that says where an AAC stream starts in
	/// <paramref name="data"/>. "ADIF" at offset 0 starts an ADIF stream. Otherwise the first decisive sync in
	/// the first <paramref name="searchBytes"/> bytes decides (see <see cref="Judge"/>): an ADTS header starts
	/// an ADTS stream, an MPEG audio frame means the data is not AAC.
	/// </summary>
	/// <param name="data">The bytes from the earliest possible start.</param>
	/// <param name="searchBytes">How many start offsets to try.</param>
	/// <param name="headerLength">The length of the start header: 7 for ADTS, the ADIF header's size.
	/// A caller that knows where the audio ends rejects a start whose header runs past it.</param>
	/// <returns>The offset of the stream in <paramref name="data"/>, or -1.</returns>
	public static int FindStart (ReadOnlySpan<byte> data, int searchBytes, out int headerLength)
	{
		headerLength = AdtsHeader.Size;
		if (AdifHeader.HasMagic (data)) {
			if (!AdifHeader.TryParse (data[..Math.Min (AdifHeader.MaxSize, data.Length)], out var adif))
				return -1;
			headerLength = adif.Size;
			return 0;
		}

		var limit = Math.Min (searchBytes, data.Length);
		for (var i = 0; i < limit; i++) {
			if (data[i] != 0xFF || !TryCandidate (data[i..], out var candidate))
				continue;

			var at = (long)i + candidate.Distance;
			var successor = at + candidate.Need <= data.Length ? data.Slice ((int)at, candidate.Need) : [];
			switch (Judge (candidate, data.Length - i, i == 0, successor)) {
				case Verdict.Aac:
					return i;
				case Verdict.Mp3:
					return -1;
			}
		}

		return -1;
	}

	/// <summary>
	/// The stream twin of <see cref="FindStart(ReadOnlySpan{byte}, int, out int)"/>. It reads 16 bytes, then 4 KB chunks
	/// only while no sync has decided, and each successor header on its own, so a file decided at its first
	/// byte costs about 23 bytes. The stream position moves.
	/// </summary>
	public static long FindStart (Stream stream, long offset, long end, int searchBytes, out int headerLength)
	{
		headerLength = AdtsHeader.Size;
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
				if (!TryReadAdifHeader (stream, offset, end, out var adif))
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

				var at = here + candidate.Distance;
				ReadOnlySpan<byte> successor = [];
				if (at + candidate.Need <= pos + length)
					successor = chunk.AsSpan ((int)(at - pos), candidate.Need);
				else if (at + candidate.Need <= end && ReadAt (stream, at, small, candidate.Need))
					successor = small.AsSpan (0, candidate.Need);

				switch (Judge (candidate, end - here, here == offset, successor)) {
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
		if (AdtsHeader.TryParse (next, out var following))
			return following.SameStream (header);

		// The frame ends where an ID3v1, APEv2, or Lyrics3 tag begins.
		return IsTrailingTag (next);
	}

	/// <summary>
	/// Reads the stream properties from the audio region [<paramref name="start"/>, <paramref name="end"/>).
	/// The stream starts where <see cref="FindStart(ReadOnlySpan{byte}, int, out int)"/> says, within <see cref="FirstFrameSearchBytes"/>.
	/// </summary>
	/// <remarks>
	/// The start is judged against the whole stream, as detection judges it, so a frame that ends where a
	/// trailing tag begins is confirmed by that tag on both paths. The start header itself must lie inside the
	/// audio region, on both paths too: a header cut by a trailing tag is not AAC. Frames are counted only inside
	/// the audio region.
	/// </remarks>
	public static bool TryRead (Stream stream, long start, long end, out AacAudioProperties? properties)
	{
		properties = null;
		if (start < 0 || end > stream.Length || start > end)
			return false;

		var first = FindStart (stream, start, stream.Length, FirstFrameSearchBytes, out var headerLength);
		if (first < 0 || first + headerLength > end)
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
		// The header is bounded by the stream, as detection bounds it. The payload ends at the trailing tags.
		if (!TryReadAdifHeader (stream, start, stream.Length, out var header))
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
