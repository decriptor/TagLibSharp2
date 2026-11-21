// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

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

	const int SearchChunkBytes = 4096;

	// A program_config_element is at most 2465 bits (see AdifHeader.MaxSize), plus the 3-bit element id.
	const int MaxProgramConfigBytes = 320;

	// ISO/IEC 13818-7 §6.3 raw_data_block: id_syn_ele ID_PCE.
	const int ProgramConfigElementId = 5;

	/// <summary>
	/// True when an AAC stream starts at <paramref name="offset"/>: "ADIF", or an ADTS header whose
	/// successor confirms it. A header whose successor lies past the data is accepted on its own.
	/// </summary>
	public static bool StartsAt (ReadOnlySpan<byte> data, int offset)
	{
		if (offset < 0 || offset >= data.Length)
			return false;

		var rest = data[offset..];
		if (AdifHeader.HasMagic (rest))
			return true;

		if (!AdtsHeader.TryParse (rest, out var header))
			return false;

		return Confirms (header, rest.Length, rest.Length >= header.FrameLength + AdtsHeader.Size
			? rest.Slice (header.FrameLength, AdtsHeader.Size)
			: []);
	}

	/// <summary>
	/// The stream twin of <see cref="StartsAt(ReadOnlySpan{byte}, int)"/>. The stream position moves.
	/// </summary>
	public static bool StartsAt (Stream stream, long offset)
	{
		var buffer = new byte[AdtsHeader.Size];
		if (!ReadAt (stream, offset, buffer, 4))
			return false;
		if (AdifHeader.HasMagic (buffer))
			return true;

		if (!ReadAt (stream, offset, buffer, AdtsHeader.Size) || !AdtsHeader.TryParse (buffer, out var header))
			return false;

		var room = stream.Length - offset;
		var next = new byte[AdtsHeader.Size];
		var hasNext = room >= header.FrameLength + AdtsHeader.Size
			&& ReadAt (stream, offset + header.FrameLength, next, AdtsHeader.Size);
		return Confirms (header, room, hasNext ? next : []);
	}

	// A frame that ends exactly at the end, or whose successor header does not fit, cannot be
	// contradicted. Otherwise the next header must parse and name the same stream.
	static bool Confirms (AdtsHeader header, long room, ReadOnlySpan<byte> next)
	{
		if (room < header.FrameLength + AdtsHeader.Size)
			return true;

		return AdtsHeader.TryParse (next, out var following) && following.SameStream (header);
	}

	/// <summary>
	/// Reads the stream properties from the audio region [<paramref name="start"/>, <paramref name="end"/>).
	/// </summary>
	public static bool TryRead (Stream stream, long start, long end, out AacAudioProperties? properties)
	{
		properties = null;
		if (start < 0 || end > stream.Length || end - start < 4)
			return false;

		var magic = new byte[4];
		if (!ReadAt (stream, start, magic, 4))
			return false;
		if (AdifHeader.HasMagic (magic))
			return TryReadAdif (stream, start, end, out properties);

		var first = FindFrame (stream, start, Math.Min (end, start + FirstFrameSearchBytes), end, null);
		if (first < 0)
			return false;

		return TryWalkAdts (stream, first, end, out properties);
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

	static bool TryWalkAdts (Stream stream, long first, long end, out AacAudioProperties? properties)
	{
		properties = null;
		var buffer = new byte[AdtsHeader.Size];
		if (!ReadAt (stream, first, buffer, AdtsHeader.Size) || !AdtsHeader.TryParse (buffer, out var stream0))
			return false;

		var channels = stream0.Channels;
		if (stream0.ChannelConfiguration == 0)
			channels = ProgramConfigChannels (stream, first, stream0, end);

		long frames = 0;
		long blocks = 0;
		long bytes = 0;
		var pos = first;
		while (pos <= end - AdtsHeader.Size) {
			if (!ReadAt (stream, pos, buffer, AdtsHeader.Size)
				|| !AdtsHeader.TryParse (buffer, out var header)
				|| !header.SameStream (stream0)) {
				var found = FindFrame (stream, pos + 1, end, end, stream0);
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
	/// Finds the first ADTS header at or after <paramref name="from"/> and before <paramref name="limit"/>
	/// that parses, names <paramref name="sameAs"/>'s stream when given, and is confirmed by its successor
	/// inside <paramref name="end"/>. Returns -1 when there is none.
	/// </summary>
	static long FindFrame (Stream stream, long from, long limit, long end, AdtsHeader? sameAs)
	{
		var chunk = new byte[SearchChunkBytes + AdtsHeader.Size - 1];
		var header = new byte[AdtsHeader.Size];
		var at = from;
		while (at < limit && at <= end - AdtsHeader.Size) {
			var length = (int)Math.Min (chunk.Length, end - at);
			if (!ReadAt (stream, at, chunk, length))
				return -1;

			var candidates = (int)Math.Min (SearchChunkBytes, limit - at);
			for (var i = 0; i < candidates && i + AdtsHeader.Size <= length; i++) {
				if (chunk[i] != 0xFF)
					continue;

				var span = chunk.AsSpan (i, AdtsHeader.Size);
				if (!AdtsHeader.TryParse (span, out var candidate))
					continue;
				if (sameAs is { } expected && !candidate.SameStream (expected))
					continue;

				var pos = at + i;
				var room = end - pos;
				var hasNext = room >= candidate.FrameLength + AdtsHeader.Size
					&& ReadAt (stream, pos + candidate.FrameLength, header, AdtsHeader.Size);
				if (Confirms (candidate, room, hasNext ? header : []))
					return pos;
			}

			at += candidates;
		}

		return -1;
	}

	static bool ReadAt (Stream stream, long position, byte[] buffer, int count)
	{
		if (count <= 0 || position < 0 || position > stream.Length - count)
			return false;

		stream.Position = position;
		return StreamBytes.Read (stream, buffer, 0, count) == count;
	}
}
