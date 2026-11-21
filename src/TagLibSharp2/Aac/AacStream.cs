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

	/// <summary>
	/// The bytes <see cref="FindStart(ReadOnlySpan{byte}, long, int)"/> needs to decide a search of <paramref name="searchBytes"/>:
	/// every candidate plus the farthest successor header.
	/// </summary>
	public static int WindowBytes (int searchBytes) => searchBytes - 1 + SuccessorReach;

	/// <summary>
	/// The one rule, for detection and for reading, that says where an AAC stream starts in
	/// <paramref name="window"/>. "ADIF" at offset 0 starts an ADIF stream. Otherwise the first ADTS header
	/// in the first <paramref name="searchBytes"/> bytes whose successor confirms it starts an ADTS stream,
	/// unless an MPEG audio frame (layer other than '00', ISO/IEC 11172-3 §2.4.2.3) comes first.
	/// </summary>
	/// <param name="window">The bytes from the earliest possible start.</param>
	/// <param name="room">The bytes from the earliest possible start to the end of the data.</param>
	/// <param name="searchBytes">How many start offsets to try.</param>
	/// <returns>The offset of the stream in <paramref name="window"/>, or -1.</returns>
	public static int FindStart (ReadOnlySpan<byte> window, long room, int searchBytes)
	{
		if (AdifHeader.HasMagic (window))
			return 0;

		var limit = (int)Math.Min (searchBytes, Math.Min (window.Length, room));
		for (var i = 0; i < limit; i++) {
			if (window[i] != 0xFF)
				continue;

			var rest = window[i..];
			if (AdtsHeader.TryParse (rest, out var header)) {
				if (IsConfirmed (header, rest, room - i))
					return i;
			} else if (MpegFrame.IsStartOfAudio (rest)) {
				return -1;
			}
		}

		return -1;
	}

	/// <summary>
	/// The stream twin of <see cref="FindStart(ReadOnlySpan{byte}, long, int)"/>: reads the window at <paramref name="offset"/>. The stream position moves.
	/// </summary>
	public static long FindStart (Stream stream, long offset, long end, int searchBytes)
	{
		if (offset < 0 || offset >= end)
			return -1;

		var length = (int)Math.Min (WindowBytes (searchBytes), end - offset);
		var window = new byte[length];
		if (!ReadAt (stream, offset, window, length))
			return -1;

		var found = FindStart (window, end - offset, searchBytes);
		return found < 0 ? -1 : offset + found;
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
		return next.StartsWith ("TAG"u8) || next.StartsWith ("APETAGE"u8) || next.StartsWith ("LYRICSB"u8);
	}

	/// <summary>
	/// Reads the stream properties from the audio region [<paramref name="start"/>, <paramref name="end"/>).
	/// The stream starts where <see cref="FindStart(ReadOnlySpan{byte}, long, int)"/> says, within <see cref="FirstFrameSearchBytes"/>.
	/// </summary>
	public static bool TryRead (Stream stream, long start, long end, out AacAudioProperties? properties)
	{
		properties = null;
		if (start < 0 || end > stream.Length || end - start < 4)
			return false;

		var first = FindStart (stream, start, end, FirstFrameSearchBytes);
		if (first < 0)
			return false;

		var magic = new byte[4];
		if (first == start && ReadAt (stream, start, magic, 4) && AdifHeader.HasMagic (magic))
			return TryReadAdif (stream, start, end, out properties);

		return TryWalkAdts (new ScanReader (stream, end), first, end, out properties);
	}

	static bool TryReadAdif (Stream stream, long start, long end, out AacAudioProperties? properties)
	{
		properties = null;
		var length = (int)Math.Min (AdifHeader.MaxSize, end - start);
		var bytes = new byte[length];
		if (!ReadAt (stream, start, bytes, length) || !AdifHeader.TryParse (bytes, out var header))
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
		readonly byte[] _buffer = new byte[SearchChunkBytes + SuccessorReach];
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
				var length = (int)Math.Min (_buffer.Length, end - at);
				if (!ReadAt (stream, at, _buffer, length))
					return [];
				_start = at;
				_length = length;
			}

			return _buffer.AsSpan ((int)(at - _start), (int)(_start + _length - at));
		}

		public bool TryHeader (long pos, out AdtsHeader header)
		{
			if (_start >= 0 && pos >= _start && pos + AdtsHeader.Size <= _start + _length)
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
