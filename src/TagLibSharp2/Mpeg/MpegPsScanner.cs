// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mpeg;

/// <summary>
/// Reads stream properties and duration from the head and the tail of an MPEG program
/// stream or MPEG video elementary stream. The bytes between the two windows are not needed.
/// </summary>
/// <remarks>
/// <para>
/// Program stream layout (ISO/IEC 11172-1 2.4.3.1, ISO/IEC 13818-1 2.5.3.1): a sequence of
/// packs, each a pack header (0x000001BA) followed by an optional system header (0x000001BB)
/// and packets. A packet is a start code prefix, a stream_id (0xBC to 0xFF) and a 16-bit
/// length. The stream ends with 0x000001B9.
/// </para>
/// <para>
/// Duration is the span of presentation time stamps: the earliest PTS in the head to the
/// latest PTS in the tail plus the length of the access unit it stamps (one video frame,
/// one MPEG audio frame, one AC-3 frame). Time stamps count a 90 kHz clock modulo 2^33
/// (13818-1 2.4.3.7), so the span is taken modulo 2^33 and a single wrap is measured
/// correctly. A stream without PTS falls back to the SCR of the first and last packs.
/// </para>
/// <para>
/// An elementary video stream has no time stamps. Its duration counts pictures: the
/// time_code of the first GOP header (13818-2 6.3.8) to the time_code of the last GOP
/// header in the tail, plus the pictures after it, divided by the frame rate. When the time
/// codes do not advance, the duration is the stream size over the sequence header's bit rate,
/// which for a variable-rate stream is the maximum rate and so gives a lower bound.
/// An MPEG-1 stream that marks its rate variable has no estimate.
/// </para>
/// </remarks>
internal static class MpegPsScanner
{
	/// <summary>First and last window size. Each grows once to <see cref="LargeWindow"/> when it lacks what it needs.</summary>
	public const int SmallWindow = 256 * 1024;

	/// <summary>
	/// 13818-1 2.7.4 requires a PTS at least every 0.7 s, which is under 4 MiB at the
	/// highest MPEG-2 rates (80 Mbit/s for high level).
	/// </summary>
	public const int LargeWindow = 4 * 1024 * 1024;

	/// <summary>Leading zero bytes allowed before the first start code (13818-2 5.3 zero_byte stuffing).</summary>
	const int MaxLeadingZeros = 16;

	/// <summary>Bytes of each stream's first payload kept to find its codec header.</summary>
	const int ProbeBytes = 16 * 1024;

	const long Wrap = 1L << 33;

	/// <summary>A tail time stamp this close before the start is a reordered frame, not a 26-hour stream.</summary>
	const long BeforeStart = 60 * 90_000;

	const int PrivateStream1 = 0xBD;

	/// <summary>
	/// Returns the offset of the first pack header or sequence header in <paramref name="data"/>,
	/// allowing a few leading zero bytes, or -1 when the data does not start an MPEG stream.
	/// </summary>
	public static int StreamStart (ReadOnlySpan<byte> data)
	{
		var zeros = 0;
		while (zeros < data.Length && zeros < MaxLeadingZeros + 2 && data[zeros] == 0)
			zeros++;
		if (zeros < 2 || zeros + 1 >= data.Length || data[zeros] != 0x01)
			return -1;

		var at = zeros - 2;
		var rest = data.Slice (at);
		if (rest[3] == 0xB3)
			return MpegVideoSequenceHeader.LooksLikeStart (rest) ? at : -1;
		if (rest[3] != 0xBA)
			return -1;

		var length = PackHeaderLength (rest, out _, out _);
		if (length <= 0)
			return -1;

		// What follows a pack header is another start code (a system header, a packet, or the end
		// code). This keeps a box whose size is 0x000001BA from passing for a pack. It is required,
		// not checked only when the bytes are there: the reader sees the stream cut at the first
		// tag after it while detection sees the file, and both must give the same answer.
		if (rest.Length < length + 3 || rest[length] != 0 || rest[length + 1] != 0 || rest[length + 2] != 1)
			return -1;
		return at;
	}

	/// <summary>
	/// The length of the pack header at the start of <paramref name="data"/>, or 0 when it is
	/// not a complete, well-formed pack header.
	/// </summary>
	/// <remarks>
	/// ISO/IEC 11172-1 2.4.3.2: '0010', SCR[32..30], marker, SCR[29..15], marker, SCR[14..0],
	/// marker, marker, mux_rate (22), marker: 12 bytes.
	/// ISO/IEC 13818-1 2.5.3.3 Table 2-33: '01', SCR_base[32..30], marker, SCR_base[29..15],
	/// marker, SCR_base[14..0], marker, SCR_extension (9), marker, program_mux_rate (22),
	/// marker, marker, reserved (5), pack_stuffing_length (3), then that many stuffing bytes.
	/// </remarks>
	public static int PackHeaderLength (ReadOnlySpan<byte> data, out int version, out long scr)
	{
		version = 0;
		scr = 0;
		if (data.Length < 12 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0xBA)
			return 0;

		if ((data[4] & 0xF1) == 0x21) {
			if ((data[6] & 0x01) == 0 || (data[8] & 0x01) == 0 || (data[9] & 0x80) == 0 || (data[11] & 0x01) == 0)
				return 0;
			version = 1;
			scr = ReadTimestamp (data.Slice (4));
			return 12;
		}

		if ((data[4] & 0xC4) != 0x44 || data.Length < 14)
			return 0;
		if ((data[6] & 0x04) == 0 || (data[8] & 0x04) == 0 || (data[9] & 0x01) == 0 || (data[12] & 0x03) != 0x03)
			return 0;

		var length = 14 + (data[13] & 0x07);
		if (data.Length < length)
			return 0;

		version = 2;
		scr = ((long)(data[4] & 0x38) << 27)
			| ((long)(data[4] & 0x03) << 28)
			| ((long)data[5] << 20)
			| ((long)(data[6] & 0xF8) << 12)
			| ((long)(data[6] & 0x03) << 13)
			| ((long)data[7] << 5)
			| ((long)data[8] >> 3);
		return length;
	}

	/// <summary>
	/// A 33-bit time stamp in the 5-byte form shared by the MPEG-1 SCR, PTS and DTS and the
	/// MPEG-2 PTS and DTS: 4 prefix bits, bits 32..30, marker, 29..15, marker, 14..0, marker.
	/// </summary>
	static long ReadTimestamp (ReadOnlySpan<byte> b) =>
		((long)(b[0] & 0x0E) << 29)
		| ((long)b[1] << 22)
		| ((long)(b[2] & 0xFE) << 14)
		| ((long)b[3] << 7)
		| ((long)b[4] >> 1);

	// The prefix is '0010' (PTS alone) or '0011' (PTS then DTS); each marker bit is 1.
	static bool IsTimestamp (ReadOnlySpan<byte> b) =>
		(b[0] & 0xE0) == 0x20 && (b[0] & 0x01) == 1 && (b[2] & 0x01) == 1 && (b[4] & 0x01) == 1;

	/// <summary>
	/// Analyses the windows. <paramref name="head"/> starts at the first start code.
	/// <paramref name="tail"/> ends at the end of the stream, before any trailing tag.
	/// </summary>
	/// <param name="head">The first bytes of the stream.</param>
	/// <param name="tail">The last bytes of the stream. May overlap the head.</param>
	/// <param name="streamLength">Bytes from the first start code to the end of the stream.</param>
	/// <param name="tailOffset">Offset of <paramref name="tail"/> from the first start code.</param>
	public static MpegPsAnalysis Analyze (ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long streamLength, long tailOffset)
	{
		var analysis = new MpegPsAnalysis ();
		if (head.Length >= 4 && head[3] == 0xB3) {
			AnalyzeElementary (head, tail, streamLength, tailOffset, analysis);
			return analysis;
		}

		analysis.StreamType = MpegPsStreamType.ProgramStream;
		var state = new ScanState ();
		WalkPacks (head, 0, state, isHead: true);
		WalkPacks (tail, SyncToPack (tail), state, isHead: false);
		Finish (state, analysis);
		return analysis;
	}

	static void AnalyzeElementary (ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long streamLength, long tailOffset, MpegPsAnalysis analysis)
	{
		analysis.StreamType = MpegPsStreamType.VideoElementaryStream;
		analysis.HasVideo = true;
		if (!MpegVideoSequenceHeader.TryFind (head, out var sequence)) {
			analysis.NeedsLargerHead = true;
			return;
		}

		analysis.Video = sequence;
		var firstGop = MpegStartCode.Find (head, 0, 0xB8);
		var lastGop = LastStartCode (tail, 0xB8);
		if (firstGop >= 0 && lastGop >= 0 && firstGop + 8 <= head.Length && lastGop + 8 <= tail.Length) {
			var nominal = (int)Math.Round (sequence.FrameRate);
			var first = GopFrameNumber (head.Slice (firstGop + 4), nominal);
			var last = GopFrameNumber (tail.Slice (lastGop + 4), nominal);
			var lastAt = tailOffset + lastGop;
			var advances = last > first || (last == first && lastAt == firstGop);
			if (advances) {
				var after = CountStartCodes (tail, lastGop + 8, 0x00);
				var frames = last - first + after;
				if (frames > 0 && sequence.FrameRate > 0) {
					analysis.Duration = Seconds (frames / sequence.FrameRate);
					return;
				}
			}
		} else if (firstGop >= 0 && lastGop < 0 && tail.Length < streamLength) {
			analysis.NeedsLargerTail = true;
		}

		if (sequence.BitRate > 0 && streamLength > 0)
			analysis.Duration = Seconds (streamLength * 8d / sequence.BitRate);
	}

	/// <summary>
	/// The picture number of a GOP time_code: drop_frame_flag (1), hours (5), minutes (6),
	/// marker (1), seconds (6), pictures (6) (13818-2 Table 6-11). Drop-frame time code
	/// skips picture numbers 0 and 1 (0 to 3 at 60 Hz) of every minute except each tenth.
	/// </summary>
	static long GopFrameNumber (ReadOnlySpan<byte> b, int nominal)
	{
		var drop = (b[0] & 0x80) != 0;
		var hours = (b[0] >> 2) & 0x1F;
		var minutes = ((b[0] & 0x03) << 4) | (b[1] >> 4);
		var seconds = ((b[1] & 0x07) << 3) | (b[2] >> 5);
		var pictures = ((b[2] & 0x1F) << 1) | (b[3] >> 7);
		var totalMinutes = (hours * 60L) + minutes;
		var number = (((totalMinutes * 60) + seconds) * nominal) + pictures;
		if (drop && (nominal == 30 || nominal == 60)) {
			var skipped = nominal == 60 ? 4 : 2;
			number -= skipped * (totalMinutes - (totalMinutes / 10));
		}

		return number;
	}

	static int LastStartCode (ReadOnlySpan<byte> data, byte code)
	{
		var last = -1;
		var at = 0;
		while (true) {
			var found = MpegStartCode.Find (data, at, code);
			if (found < 0)
				return last;
			last = found;
			at = found + 4;
		}
	}

	static int CountStartCodes (ReadOnlySpan<byte> data, int from, byte code)
	{
		var count = 0;
		var at = from;
		while (true) {
			var found = MpegStartCode.Find (data, at, code);
			if (found < 0)
				return count;
			count++;
			at = found + 4;
		}
	}

	/// <summary>
	/// The first pack header in <paramref name="tail"/> that is followed by another start code.
	/// Payload bytes may imitate 0x000001BA, so a candidate must also parse.
	/// </summary>
	static int SyncToPack (ReadOnlySpan<byte> tail)
	{
		var at = 0;
		while (true) {
			var found = MpegStartCode.Find (tail, at, 0xBA);
			if (found < 0)
				return tail.Length;
			var length = PackHeaderLength (tail.Slice (found), out _, out _);
			var next = found + length;
			if (length > 0 && next + 3 <= tail.Length && tail[next] == 0 && tail[next + 1] == 0 && tail[next + 2] == 1)
				return found;
			at = found + 4;
		}
	}

	/// <summary>
	/// Walks packs and packets from <paramref name="start"/>. A byte run that is not a start
	/// code is skipped to the next valid pack header.
	/// </summary>
	static void WalkPacks (ReadOnlySpan<byte> data, int start, ScanState state, bool isHead)
	{
		var pos = start;
		while (pos + 4 <= data.Length) {
			if (data[pos] != 0 || data[pos + 1] != 0 || data[pos + 2] != 1) {
				pos = SyncToPack (data.Slice (pos + 1)) + pos + 1;
				continue;
			}

			var code = data[pos + 3];
			if (code == 0xBA) {
				var length = PackHeaderLength (data.Slice (pos), out var version, out var scr);
				if (length == 0) {
					pos = SyncToPack (data.Slice (pos + 1)) + pos + 1;
					continue;
				}

				state.NoteScr (scr, version, isHead);
				pos += length;
				continue;
			}

			if (code == 0xB9) {
				pos += 4;
				continue;
			}

			if (code < 0xBB) {
				pos = SyncToPack (data.Slice (pos + 1)) + pos + 1;
				continue;
			}

			if (pos + 6 > data.Length)
				return;
			var packetLength = (data[pos + 4] << 8) | data[pos + 5];
			var bodyStart = pos + 6;
			var bodyEnd = Math.Min (data.Length, bodyStart + packetLength);
			if (code != 0xBB)
				ReadPacket (code, data.Slice (bodyStart, bodyEnd - bodyStart), state, isHead);
			if (packetLength == 0) {
				// Only a transport stream may leave a video packet unbounded (13818-1 2.4.3.7).
				pos = SyncToPack (data.Slice (bodyStart)) + bodyStart;
				continue;
			}

			pos = bodyStart + packetLength;
		}
	}

	/// <summary>
	/// Reads the PES header of one packet and records its time stamp and, in the head, its payload.
	/// </summary>
	static void ReadPacket (int streamId, ReadOnlySpan<byte> body, ScanState state, bool isHead)
	{
		// 13818-1 Table 2-22: these streams carry no PES header extension.
		if (streamId is 0xBC or 0xBE or 0xBF or 0xF0 or 0xF1 or 0xF2 or 0xF8 or 0xFF)
			return;
		if (body.IsEmpty)
			return;

		long? pts = null;
		int payloadStart;
		if ((body[0] & 0xC0) == 0x80) {
			// 13818-1 2.4.3.7: '10' flags, PTS_DTS_flags, PES_header_data_length.
			if (body.Length < 3)
				return;
			var headerLength = body[2];
			payloadStart = 3 + headerLength;
			if ((body[1] & 0x80) != 0 && headerLength >= 5 && body.Length >= 8 && IsTimestamp (body.Slice (3)))
				pts = ReadTimestamp (body.Slice (3));
		} else {
			// 11172-1 2.4.3.3: up to 16 stuffing bytes, an optional STD buffer field, then
			// '0010' PTS, '0011' PTS and DTS, or '0000 1111'.
			var p = 0;
			while (p < body.Length && p < 16 && body[p] == 0xFF)
				p++;
			if (p < body.Length && (body[p] & 0xC0) == 0x40)
				p += 2;
			if (p >= body.Length)
				return;
			var flags = body[p] >> 4;
			if (flags is 0x2 or 0x3) {
				var size = flags == 0x2 ? 5 : 10;
				if (p + size > body.Length)
					return;
				if (IsTimestamp (body.Slice (p)))
					pts = ReadTimestamp (body.Slice (p));
				payloadStart = p + size;
			} else if (body[p] == 0x0F) {
				payloadStart = p + 1;
			} else {
				return;
			}
		}

		if (payloadStart > body.Length)
			payloadStart = body.Length;
		var payload = body.Slice (payloadStart);

		var key = streamId;
		var kind = StreamKind.Other;
		if (streamId >= 0xE0 && streamId <= 0xEF) {
			kind = StreamKind.Video;
		} else if (streamId >= 0xC0 && streamId <= 0xDF) {
			kind = StreamKind.MpegAudio;
		} else if (streamId == PrivateStream1 && !payload.IsEmpty) {
			// DVD-Video sub-streams of private_stream_1, as ffmpeg and mediainfo read them:
			// 0x80-0x87 AC-3 and 0x88-0x8F DTS with a 4-byte header, 0xA0-0xA7 LPCM with a 7-byte header.
			var sub = payload[0];
			key = (PrivateStream1 << 8) | sub;
			if (sub >= 0x80 && sub <= 0x87) {
				kind = StreamKind.Ac3;
				payload = payload.Length > 4 ? payload.Slice (4) : [];
			} else if (sub >= 0x88 && sub <= 0x8F) {
				kind = StreamKind.Dts;
				payload = payload.Length > 4 ? payload.Slice (4) : [];
			} else if (sub >= 0xA0 && sub <= 0xA7) {
				kind = StreamKind.Lpcm;
			}
		}

		var stream = state.Stream (key, kind, isHead);
		if (stream is null)
			return;
		if (pts is long value)
			state.NotePts (stream, value, isHead);
		if (isHead)
			stream.AddProbe (payload, slide: kind == StreamKind.Video);
	}

	static void Finish (ScanState state, MpegPsAnalysis analysis)
	{
		analysis.SystemsVersion = state.SystemsVersion;
		StreamState? video = null;
		StreamState? audio = null;
		foreach (var stream in state.Order) {
			if (stream.Kind == StreamKind.Video)
				video ??= stream;
			else if (stream.Kind is StreamKind.MpegAudio or StreamKind.Ac3 or StreamKind.Dts or StreamKind.Lpcm)
				audio ??= stream;
		}

		analysis.HasVideo = video is not null;
		analysis.HasAudio = audio is not null;
		if (video is not null) {
			if (MpegVideoSequenceHeader.TryFind (video.Probe, out var sequence)) {
				analysis.Video = sequence;
				video.FrameTicks = sequence.FrameRate > 0 ? 90_000 / sequence.FrameRate : 0;
			} else {
				analysis.NeedsLargerHead = true;
			}
		}

		foreach (var stream in state.Order)
			ReadAudioHeader (stream, stream == audio ? analysis : null);

		if (state.Order.Count == 0 || (!state.AnyHeadPts && !state.HeadScr.HasValue))
			analysis.NeedsLargerHead = true;
		if ((state.AnyHeadPts && !state.AnyTailPts) || (!state.AnyHeadPts && !state.TailScr.HasValue))
			analysis.NeedsLargerTail = true;

		if (state.AnyHeadPts && state.AnyTailPts) {
			// Tail stamps are already ticks after the earliest head stamp.
			double end = 0;
			foreach (var stream in state.Order) {
				if (stream.TailMax is not long max)
					continue;
				end = Math.Max (end, max + stream.FrameTicks);
			}

			if (end > 0)
				analysis.Duration = Seconds (end / 90_000d);
		}

		if (analysis.Duration is null && state.HeadScr is long first && state.TailScr is long last) {
			var span = Modulo (last - first);
			if (span > 0)
				analysis.Duration = Seconds (span / 90_000d);
		}
	}

	static void ReadAudioHeader (StreamState stream, MpegPsAnalysis? analysis)
	{
		var probe = stream.Probe;
		switch (stream.Kind) {
			case StreamKind.MpegAudio:
				if (TryFindMpegAudio (probe, out var frame) && frame is not null) {
					stream.FrameTicks = frame.SamplesPerFrame * 90_000d / frame.SampleRate;
					if (analysis is not null)
						analysis.Audio = new MpegPsAudio (MpegAudioProperties.CodecName (frame.Version, frame.Layer), frame.SampleRate, ChannelCount (frame.ChannelMode), frame.Bitrate, 0);
				} else if (analysis is not null) {
					analysis.Audio = new MpegPsAudio ("MPEG Audio", 0, 0, 0, 0);
				}

				break;
			case StreamKind.Ac3:
				if (Ac3SyncFrame.TryFind (probe, out var ac3)) {
					stream.FrameTicks = Ac3SyncFrame.SamplesPerFrame * 90_000d / ac3.SampleRate;
					if (analysis is not null)
						analysis.Audio = new MpegPsAudio ("AC-3", ac3.SampleRate, ac3.Channels, ac3.Bitrate, 0);
				} else if (analysis is not null) {
					analysis.Audio = new MpegPsAudio ("AC-3", 0, 0, 0, 0);
				}

				break;
			case StreamKind.Dts:
				if (analysis is not null)
					analysis.Audio = new MpegPsAudio ("DTS", 0, 0, 0, 0);
				break;
			case StreamKind.Lpcm:
				if (analysis is not null)
					analysis.Audio = ReadLpcm (probe);
				break;
		}
	}

	/// <summary>
	/// The DVD LPCM sub-stream header: sub-stream id, frame count, first access unit pointer (2),
	/// emphasis and frame number, then quantization (2 bits: 16, 20, 24 bit), sampling
	/// frequency (2 bits: 48, 96 kHz), a reserved bit, and channels minus one (3 bits).
	/// Checked against ffmpeg's pcm_dvd output at 48 and 96 kHz, 16 and 24 bit, 1 and 2 channels.
	/// </summary>
	static MpegPsAudio ReadLpcm (ReadOnlySpan<byte> probe)
	{
		if (probe.Length < 7)
			return new MpegPsAudio ("LPCM", 0, 0, 0, 0);
		var info = probe[5];
		var quantization = info >> 6;
		var bits = quantization == 3 ? 0 : 16 + (quantization * 4);
		var sampleRate = ((info >> 4) & 0x03) switch {
			0 => 48000,
			1 => 96000,
			_ => 0
		};
		var channels = (info & 0x07) + 1;
		var bitrate = (int)((long)sampleRate * bits * channels / 1000);
		return new MpegPsAudio ("LPCM", sampleRate, channels, bitrate, bits);
	}

	static int ChannelCount (ChannelMode mode) => mode == ChannelMode.Mono ? 1 : 2;

	/// <summary>
	/// The first MPEG audio frame header in <paramref name="probe"/> whose successor, when it
	/// fits, is also a frame header. A PES payload need not start on a frame.
	/// </summary>
	static bool TryFindMpegAudio (ReadOnlySpan<byte> probe, out MpegFrame? frame)
	{
		frame = null;
		for (var i = 0; i + MpegFrame.HeaderSize <= probe.Length; i++) {
			if (probe[i] != 0xFF)
				continue;
			if (!MpegFrame.TryParse (probe.Slice (i), out var candidate) || candidate is null || candidate.FrameSize <= MpegFrame.HeaderSize)
				continue;
			var next = i + candidate.FrameSize;
			if (next + MpegFrame.HeaderSize > probe.Length || MpegFrame.TryParse (probe.Slice (next), out _)) {
				frame = candidate;
				return true;
			}
		}

		return false;
	}

	// TimeSpan.FromSeconds rounds to whole milliseconds before .NET Core 3.0.
	static TimeSpan Seconds (double seconds) =>
		TimeSpan.FromTicks ((long)Math.Round (seconds * TimeSpan.TicksPerSecond));

	static long Modulo (long value)
	{
		var m = value % Wrap;
		return m < 0 ? m + Wrap : m;
	}

	enum StreamKind
	{
		Other,
		Video,
		MpegAudio,
		Ac3,
		Dts,
		Lpcm
	}

	sealed class StreamState (int key, StreamKind kind)
	{
		// A sequence header with both quantiser matrices, its extension, and the next start code fit.
		const int SlideKeep = 256;

		byte[] _probe = [];
		int _probeLength;
		bool _found;

		public int Key { get; } = key;

		public StreamKind Kind { get; } = kind;

		/// <summary>The latest PTS in the tail, as ticks after the start.</summary>
		public long? TailMax { get; set; }

		/// <summary>The length of one access unit in 90 kHz ticks, or 0 when unknown.</summary>
		public double FrameTicks { get; set; }

		public ReadOnlySpan<byte> Probe => _probe.AsSpan (0, _probeLength);

		/// <summary>
		/// Keeps the first <see cref="ProbeBytes"/> of payload. A video stream instead keeps
		/// looking, packet by packet, until a sequence header turns up, so a stream cut in the
		/// middle of a GOP still finds the next one within the head window.
		/// </summary>
		public void AddProbe (ReadOnlySpan<byte> payload, bool slide)
		{
			if (payload.IsEmpty || _found)
				return;
			if (slide) {
				// The last bytes of the previous packets, then this one: a header split between
				// packets is whole here.
				var joined = new byte[_probeLength + payload.Length];
				_probe.AsSpan (0, _probeLength).CopyTo (joined);
				payload.CopyTo (joined.AsSpan (_probeLength));
				if (MpegVideoSequenceHeader.TryFind (joined, out _)) {
					_probe = joined;
					_probeLength = joined.Length;
					_found = true;
					return;
				}

				var keep = Math.Min (SlideKeep, joined.Length);
				_probe = joined.AsSpan (joined.Length - keep).ToArray ();
				_probeLength = keep;
				return;
			}

			var room = ProbeBytes - _probeLength;
			if (room <= 0)
				return;
			var take = Math.Min (room, payload.Length);
			if (_probe.Length < _probeLength + take) {
				var grown = new byte[Math.Min (ProbeBytes, Math.Max (_probeLength + take, _probe.Length * 2))];
				_probe.AsSpan (0, _probeLength).CopyTo (grown);
				_probe = grown;
			}

			payload.Slice (0, take).CopyTo (_probe.AsSpan (_probeLength));
			_probeLength += take;
		}
	}

	sealed class ScanState
	{
		readonly Dictionary<int, StreamState> _streams = [];
		long _reference;
		long _minDelta;

		public List<StreamState> Order { get; } = [];

		public int SystemsVersion { get; private set; }

		public long? HeadScr { get; private set; }

		public long? TailScr { get; private set; }

		public bool AnyHeadPts { get; private set; }

		public bool AnyTailPts { get; private set; }

		/// <summary>The earliest head PTS, modulo 2^33.</summary>
		public long Start => Modulo (_reference + _minDelta);

		public void NoteScr (long scr, int version, bool isHead)
		{
			if (isHead) {
				HeadScr ??= scr;
				if (SystemsVersion == 0)
					SystemsVersion = version;
			} else {
				TailScr = scr;
			}
		}

		public StreamState? Stream (int key, StreamKind kind, bool isHead)
		{
			if (_streams.TryGetValue (key, out var stream))
				return stream;
			// No cap is needed: a key is a stream_id (0xBD to 0xFF) or a private_stream_1
			// sub-stream id, so there are at most 323, and probes hold only head payload.
			if (!isHead)
				return null;
			stream = new StreamState (key, kind);
			_streams[key] = stream;
			Order.Add (stream);
			return stream;
		}

		public void NotePts (StreamState stream, long pts, bool isHead)
		{
			if (isHead) {
				if (!AnyHeadPts) {
					_reference = pts;
					_minDelta = 0;
					AnyHeadPts = true;
					return;
				}

				// A head stamp sits within 2^32 ticks of the first one, before or after it.
				var delta = Modulo (pts - _reference);
				if (delta >= Wrap / 2)
					delta -= Wrap;
				if (delta < _minDelta)
					_minDelta = delta;
				return;
			}

			if (!AnyHeadPts)
				return;
			var after = Modulo (pts - Start);
			if (after > Wrap - BeforeStart)
				return;
			AnyTailPts = true;
			if (stream.TailMax is not long max || after > max)
				stream.TailMax = after;
		}
	}
}

/// <summary>The first audio stream of a program stream.</summary>
internal readonly record struct MpegPsAudio (string Codec, int SampleRate, int Channels, int Bitrate, int BitsPerSample);

/// <summary>What <see cref="MpegPsScanner.Analyze"/> found.</summary>
internal sealed class MpegPsAnalysis
{
	public MpegPsStreamType StreamType { get; set; }

	public int SystemsVersion { get; set; }

	public bool HasVideo { get; set; }

	public bool HasAudio { get; set; }

	public MpegVideoSequenceHeader? Video { get; set; }

	public MpegPsAudio? Audio { get; set; }

	public TimeSpan? Duration { get; set; }

	/// <summary>The head window ended before a codec header or time stamp it needs.</summary>
	public bool NeedsLargerHead { get; set; }

	/// <summary>The tail window held no time stamp.</summary>
	public bool NeedsLargerTail { get; set; }
}
