// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// Builds MPEG program streams (ISO/IEC 11172-1, ISO/IEC 13818-1) and MPEG video
/// elementary streams (ISO/IEC 11172-2, ISO/IEC 13818-2) for tests.
/// </summary>
internal static class MpegPsTestBuilder
{
	/// <summary>One 90 kHz tick per 1/90000 s. A 25 fps frame is 3600 ticks.</summary>
	public const long TicksPerSecond = 90_000;

	/// <summary>An MPEG-1 Layer II frame at 48 kHz holds 1152 samples: 2160 ticks.</summary>
	public const long Mp2FrameTicks = 1152 * TicksPerSecond / 48_000;

	/// <summary>An AC-3 frame holds 1536 samples: 2880 ticks at 48 kHz.</summary>
	public const long Ac3FrameTicks = 1536 * TicksPerSecond / 48_000;

	/// <summary>The audio a program stream carries.</summary>
	public enum Audio
	{
		None,
		Mp2,
		Ac3,
		Lpcm
	}

	/// <summary>Options for <see cref="ProgramStream"/>.</summary>
	public sealed class Options
	{
		public bool Mpeg2 { get; init; }
		public int Width { get; init; } = 352;
		public int Height { get; init; } = 288;
		public int FrameRateCode { get; init; } = 3;
		public int FrameRateExtensionN { get; init; }
		public int FrameRateExtensionD { get; init; }
		public int VideoFrames { get; init; } = 50;
		public bool Video { get; init; } = true;
		public Audio AudioKind { get; init; } = Audio.Mp2;
		public int AudioFrames { get; init; } = -1;
		public long StartPts { get; init; } = 45_000;
		public long StartScr { get; init; }
		public bool WritePts { get; init; } = true;
		public int PackStuffing { get; init; }
		public int VideoFrameBytes { get; init; } = 64;
		public int Ac3Acmod { get; init; } = 2;
		public bool Ac3Lfe { get; init; }

		/// <summary>The AC-3 bsid. 8 is AC-3; 16 is E-AC-3, whose syncinfo differs.</summary>
		public int Ac3Bsid { get; init; } = 8;

		/// <summary>The 12 high bits of an MPEG-2 bit rate (sequence extension bit_rate_extension).</summary>
		public int BitRateExtension { get; init; }
		public bool EndCode { get; init; } = true;
		public int BitRateValue { get; init; } = 2500;

		/// <summary>Audio time stamps start this many ticks before the video's.</summary>
		public long AudioLeadTicks { get; init; }

		/// <summary>Bytes of a padding stream (0xBE) added in a pack after the first frames.</summary>
		public int PaddingBytes { get; init; }

		/// <summary>Bytes inside the padding, written at its start, so a test can find the payload.</summary>
		public byte[]? PaddingMarker { get; init; }

		/// <summary>The pack after which <see cref="PaddingBytes"/> is written; negative counts from the end.</summary>
		public int PaddingPack { get; init; } = 1;

		/// <summary>Bytes of padding stream in the first pack, before any PES packet.</summary>
		public int LeadingPaddingBytes { get; init; }
	}

	/// <summary>
	/// The frame period in 90 kHz ticks for a frame_rate_code (ISO/IEC 13818-2 Table 6-4).
	/// </summary>
	public static double FrameTicks (int frameRateCode) => frameRateCode switch {
		1 => TicksPerSecond * 1001d / 24000,
		2 => TicksPerSecond / 24d,
		3 => TicksPerSecond / 25d,
		4 => TicksPerSecond * 1001d / 30000,
		5 => TicksPerSecond / 30d,
		6 => TicksPerSecond / 50d,
		7 => TicksPerSecond * 1001d / 60000,
		8 => TicksPerSecond / 60d,
		_ => 0
	};

	/// <summary>
	/// A program stream with one pack per video frame. Each pack carries the frame's
	/// video PES and the audio frames that start before the next video frame.
	/// </summary>
	public static byte[] ProgramStream (Options? options = null)
	{
		var o = options ?? new Options ();
		using var output = new MemoryStream ();
		var frameTicks = FrameTicks (o.FrameRateCode);
		var audioTicks = o.AudioKind == Audio.Ac3 ? Ac3FrameTicks : Mp2FrameTicks;
		var videoFrames = o.Video ? o.VideoFrames : 0;
		var audioFrames = o.AudioFrames >= 0
			? o.AudioFrames
			: o.AudioKind == Audio.None ? 0 : (int)Math.Round (Math.Max (o.VideoFrames, 1) * frameTicks / audioTicks);
		var packs = Math.Max (videoFrames, (int)Math.Ceiling (audioFrames * audioTicks / frameTicks));
		var nextAudio = 0;
		for (var i = 0; i < packs; i++) {
			var scr = o.StartScr + (long)(i * frameTicks);
			Write (output, o.Mpeg2 ? PackHeader2 (scr, o.PackStuffing) : PackHeader1 (scr));
			if (i == 0)
				Write (output, SystemHeader (o.Video, o.AudioKind));
			if (i == 0 && o.LeadingPaddingBytes > 0)
				Write (output, Padding (o.LeadingPaddingBytes));

			if (i < videoFrames) {
				var payload = i == 0
					? Concat (SequenceHeader (o, o.BitRateValue), GopHeader (0, 0, 0, 0, false), Picture (o.VideoFrameBytes))
					: Picture (o.VideoFrameBytes);
				long? pts = o.WritePts ? o.StartPts + (long)Math.Round (i * frameTicks) : null;
				Write (output, Pes (0xE0, pts, payload, o.Mpeg2));
			}

			var packEnd = (i + 1) * frameTicks;
			using var audio = new MemoryStream ();
			long? audioPts = null;
			while (nextAudio < audioFrames && nextAudio * audioTicks < packEnd) {
				audioPts ??= o.StartPts - o.AudioLeadTicks + (nextAudio * audioTicks);
				Write (audio, AudioFrame (o));
				nextAudio++;
			}

			if (audio.Length > 0) {
				var pts = o.WritePts ? audioPts : null;
				Write (output, AudioPes (o, pts, audio.ToArray ()));
			}

			var paddingPack = o.PaddingPack < 0 ? packs + o.PaddingPack : o.PaddingPack;
			if (i == paddingPack && o.PaddingBytes > 0)
				Write (output, Padding (o.PaddingBytes, o.PaddingMarker));
		}

		if (o.EndCode)
			Write (output, [0x00, 0x00, 0x01, 0xB9]);
		return output.ToArray ();
	}

	/// <summary>
	/// A video elementary stream: sequence header, then GOPs of <paramref name="gopLength"/> pictures.
	/// Each GOP header carries the time code of its first picture.
	/// </summary>
	public static byte[] ElementaryStream (
		Options? options = null,
		int gopLength = 12,
		bool dropFrame = false,
		int startHours = 0,
		bool zeroTimeCodes = false,
		int bitRateValue = 2500)
	{
		var o = options ?? new Options ();
		using var output = new MemoryStream ();
		var nominal = (int)Math.Round (TicksPerSecond / FrameTicks (o.FrameRateCode));
		Write (output, SequenceHeader (o, bitRateValue));
		for (var i = 0; i < o.VideoFrames; i++) {
			if (i % gopLength == 0) {
				var (h, m, s, p) = zeroTimeCodes ? (0, 0, 0, 0) : TimeCode (i, nominal, dropFrame, startHours);
				Write (output, GopHeader (h, m, s, p, dropFrame));
			}

			Write (output, Picture (o.VideoFrameBytes));
		}

		Write (output, [0x00, 0x00, 0x01, 0xB7]);
		return output.ToArray ();
	}

	static (int, int, int, int) TimeCode (int frame, int nominal, bool dropFrame, int startHours)
	{
		var number = frame;
		if (dropFrame) {
			// SMPTE 12M drop-frame: two picture numbers (four at 60) are skipped each minute
			// except every tenth minute.
			var drop = nominal == 60 ? 4 : 2;
			var perTenMinutes = (nominal * 600) - (drop * 9);
			var perMinute = (nominal * 60) - drop;
			var tens = number / perTenMinutes;
			var rest = number % perTenMinutes;
			number += drop * 9 * tens;
			if (rest > drop)
				number += drop * ((rest - drop) / perMinute);
		}

		var pictures = number % nominal;
		var seconds = number / nominal % 60;
		var minutes = number / nominal / 60 % 60;
		var hours = startHours + (number / nominal / 3600);
		return (hours, minutes, seconds, pictures);
	}

	/// <summary>ISO/IEC 11172-1 2.4.3.2 pack header: '0010' and a 33-bit SCR.</summary>
	public static byte[] PackHeader1 (long scr, int muxRate = 2500)
	{
		var header = new byte[12];
		header[2] = 0x01;
		header[3] = 0xBA;
		WriteTimestamp (header, 4, 0x2, scr);
		header[9] = (byte)(0x80 | (muxRate >> 15));
		header[10] = (byte)(muxRate >> 7);
		header[11] = (byte)((muxRate << 1) | 0x01);
		return header;
	}

	/// <summary>
	/// ISO/IEC 13818-1 2.5.3.3 pack header: '01', a 33-bit SCR base, a 9-bit extension,
	/// and up to seven 0xFF stuffing bytes.
	/// </summary>
	public static byte[] PackHeader2 (long scrBase, int stuffing = 0, int scrExtension = 0, int muxRate = 25000)
	{
		var header = new byte[14 + stuffing];
		header[2] = 0x01;
		header[3] = 0xBA;
		header[4] = (byte)(0x40 | (int)((scrBase >> 27) & 0x38) | 0x04 | (int)((scrBase >> 28) & 0x03));
		header[5] = (byte)(scrBase >> 20);
		header[6] = (byte)((int)((scrBase >> 12) & 0xF8) | 0x04 | (int)((scrBase >> 13) & 0x03));
		header[7] = (byte)(scrBase >> 5);
		header[8] = (byte)((int)((scrBase << 3) & 0xF8) | 0x04 | ((scrExtension >> 7) & 0x03));
		header[9] = (byte)((scrExtension << 1) | 0x01);
		header[10] = (byte)(muxRate >> 14);
		header[11] = (byte)(muxRate >> 6);
		header[12] = (byte)((muxRate << 2) | 0x03);
		header[13] = (byte)(0xF8 | stuffing);
		for (var i = 0; i < stuffing; i++)
			header[14 + i] = 0xFF;
		return header;
	}

	/// <summary>ISO/IEC 11172-1 2.4.3.2 / 13818-1 2.5.3.5 system header with one entry per stream.</summary>
	public static byte[] SystemHeader (bool video, Audio audio)
	{
		using var body = new MemoryStream ();
		Write (body, [0x80, 0x01, 0x01, (byte)((audio == Audio.None ? 0 : 4) | 0x00), 0xE1, 0xFF]);
		if (video)
			Write (body, [0xE0, 0xE0, 0xE8]);
		if (audio == Audio.Mp2)
			Write (body, [0xC0, 0xC0, 0x20]);
		else if (audio != Audio.None)
			Write (body, [0xBD, 0xE0, 0x3A]);
		var bytes = body.ToArray ();
		return Concat ([0x00, 0x00, 0x01, 0xBB, (byte)(bytes.Length >> 8), (byte)bytes.Length], bytes);
	}

	/// <summary>
	/// A PES packet. MPEG-2 uses the 13818-1 2.4.3.7 header ('10', flags, header length).
	/// MPEG-1 uses the 11172-1 2.4.3.3 packet header (stuffing, STD buffer, PTS).
	/// </summary>
	public static byte[] Pes (byte streamId, long? pts, byte[] payload, bool mpeg2)
	{
		byte[] header;
		if (mpeg2) {
			header = pts is long p2 ? new byte[8] : new byte[3];
			header[0] = 0x81;
			header[1] = (byte)(pts is null ? 0x00 : 0x80);
			header[2] = (byte)(header.Length - 3);
			if (pts is long value)
				WriteTimestamp (header, 3, 0x2, value);
		} else if (pts is long p1) {
			header = new byte[7];
			header[0] = 0x40 | 0x20;
			header[1] = 0x00;
			WriteTimestamp (header, 2, 0x2, p1);
		} else {
			header = [0x0F];
		}

		var length = header.Length + payload.Length;
		return Concat ([0x00, 0x00, 0x01, streamId, (byte)(length >> 8), (byte)length], header, payload);
	}

	/// <summary>A padding stream packet (stream_id 0xBE). Its payload has no PES header.</summary>
	public static byte[] Padding (int bytes, byte[]? marker = null)
	{
		using var output = new MemoryStream ();
		var left = bytes;
		var first = true;
		while (left > 0) {
			var size = Math.Min (left, 65000);
			var body = new byte[size];
			for (var i = 0; i < body.Length; i++)
				body[i] = 0xFF;
			if (first && marker is not null)
				Array.Copy (marker, body, Math.Min (marker.Length, body.Length));
			first = false;
			Write (output, Concat ([0x00, 0x00, 0x01, 0xBE, (byte)(size >> 8), (byte)size], body));
			left -= size;
		}

		return output.ToArray ();
	}

	/// <summary>
	/// ISO/IEC 11172-2 2.4.2.3 sequence header. MPEG-2 adds the 13818-2 6.2.2.3 sequence extension.
	/// </summary>
	public static byte[] SequenceHeader (Options o, int bitRateValue = 2500)
	{
		var width = o.Width & 0xFFF;
		var height = o.Height & 0xFFF;
		var header = new byte[12];
		header[2] = 0x01;
		header[3] = 0xB3;
		header[4] = (byte)(width >> 4);
		header[5] = (byte)((width << 4) | (height >> 8));
		header[6] = (byte)height;
		header[7] = (byte)((o.Mpeg2 ? 0x20 : 0x10) | o.FrameRateCode);
		header[8] = (byte)(bitRateValue >> 10);
		header[9] = (byte)(bitRateValue >> 2);
		header[10] = (byte)(((bitRateValue & 0x03) << 6) | 0x20 | 0x00);
		header[11] = 0x00;
		if (!o.Mpeg2)
			return header;

		var ext = new byte[10];
		ext[2] = 0x01;
		ext[3] = 0xB5;
		var hExt = (o.Width >> 12) & 0x03;
		var vExt = (o.Height >> 12) & 0x03;
		// Identifier 1, profile_and_level 0x48, progressive, 4:2:0, then the size extensions.
		ext[4] = 0x10 | 0x04;
		ext[5] = (byte)(0x80 | 0x08 | 0x02 | (hExt >> 1));
		ext[6] = (byte)(((hExt & 0x01) << 7) | (vExt << 5) | ((o.BitRateExtension >> 7) & 0x1F));
		ext[7] = (byte)(((o.BitRateExtension & 0x7F) << 1) | 0x01);
		ext[8] = 0x00;
		ext[9] = (byte)((o.FrameRateExtensionN << 5) | o.FrameRateExtensionD);
		return Concat (header, ext);
	}

	/// <summary>ISO/IEC 13818-2 6.2.2.6 group_of_pictures_header with a 25-bit time_code.</summary>
	public static byte[] GopHeader (int hours, int minutes, int seconds, int pictures, bool dropFrame)
	{
		var code = ((dropFrame ? 1u : 0u) << 24)
			| ((uint)hours << 19)
			| ((uint)minutes << 13)
			| (1u << 12)
			| ((uint)seconds << 6)
			| (uint)pictures;
		var value = (code << 7) | (1u << 6);
		return [0x00, 0x00, 0x01, 0xB8, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
	}

	/// <summary>A picture start code (0x00000100) and one slice. The body has no start code prefix.</summary>
	public static byte[] Picture (int bytes)
	{
		var picture = new byte[Math.Max (bytes, 12)];
		picture[2] = 0x01;
		picture[3] = 0x00;
		picture[4] = 0x00;
		picture[5] = 0x0F;
		picture[6] = 0xFF;
		picture[7] = 0xF8;
		picture[8] = 0x00;
		picture[9] = 0x00;
		picture[10] = 0x01;
		picture[11] = 0x01;
		for (var i = 12; i < picture.Length; i++)
			picture[i] = 0x55;
		return picture;
	}

	/// <summary>An MPEG-1 Layer II frame: 48 kHz, 192 kbps, stereo (ISO/IEC 11172-3 2.4.1.3).</summary>
	public static byte[] Mp2Frame ()
	{
		var frame = new byte[144 * 192_000 / 48_000];
		frame[0] = 0xFF;
		frame[1] = 0xFD;
		frame[2] = 0xA4;
		frame[3] = 0x00;
		return frame;
	}

	/// <summary>
	/// An AC-3 sync frame (ATSC A/52 5.3): 48 kHz, 192 kbps, the given acmod and LFE flag.
	/// </summary>
	public static byte[] Ac3Frame (int acmod, bool lfe, int bsid = 8)
	{
		// frmsizecod 20 is 192 kbps; at 48 kHz the frame is 384 words.
		var frame = new byte[768];
		frame[0] = 0x0B;
		frame[1] = 0x77;
		frame[4] = 0x00 | 20;
		var bits = new BitWriter ();
		bits.Write (bsid, 5);
		bits.Write (0, 3);
		bits.Write (acmod, 3);
		if ((acmod & 0x1) != 0 && acmod != 0x1)
			bits.Write (0, 2);
		if ((acmod & 0x4) != 0)
			bits.Write (0, 2);
		if (acmod == 0x2)
			bits.Write (0, 2);
		bits.Write (lfe ? 1 : 0, 1);
		bits.CopyTo (frame, 5);
		return frame;
	}

	static byte[] AudioFrame (Options o) => o.AudioKind switch {
		Audio.Ac3 => Ac3Frame (o.Ac3Acmod, o.Ac3Lfe, o.Ac3Bsid),
		Audio.Lpcm => new byte[1920],
		_ => Mp2Frame ()
	};

	static byte[] AudioPes (Options o, long? pts, byte[] frames) => o.AudioKind switch {
		Audio.Ac3 => Pes (0xBD, pts, Concat ([0x80, 0x01, 0x00, 0x01], frames), o.Mpeg2),
		Audio.Lpcm => Pes (0xBD, pts, Concat ([0xA0, 0x07, 0x00, 0x04, 0x0C, 0x01, 0x80], frames), o.Mpeg2),
		_ => Pes (0xC0, pts, frames, o.Mpeg2)
	};

	/// <summary>ISO/IEC 13818-1 2.4.3.7 / 11172-1 2.4.3.3: a 4-bit prefix and a 33-bit value with marker bits.</summary>
	static void WriteTimestamp (byte[] buffer, int offset, int prefix, long value)
	{
		buffer[offset] = (byte)((prefix << 4) | (int)((value >> 29) & 0x0E) | 0x01);
		buffer[offset + 1] = (byte)(value >> 22);
		buffer[offset + 2] = (byte)((int)((value >> 14) & 0xFE) | 0x01);
		buffer[offset + 3] = (byte)(value >> 7);
		buffer[offset + 4] = (byte)((int)((value << 1) & 0xFE) | 0x01);
	}

	public static byte[] Concat (params byte[][] parts)
	{
		var total = 0;
		foreach (var part in parts)
			total += part.Length;
		var result = new byte[total];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}

		return result;
	}

	static void Write (MemoryStream stream, byte[] bytes) => stream.Write (bytes, 0, bytes.Length);

	sealed class BitWriter
	{
		readonly List<bool> _bits = [];

		public void Write (int value, int count)
		{
			for (var i = count - 1; i >= 0; i--)
				_bits.Add (((value >> i) & 1) != 0);
		}

		public void CopyTo (byte[] buffer, int offset)
		{
			for (var i = 0; i < _bits.Count; i++) {
				if (_bits[i])
					buffer[offset + (i / 8)] |= (byte)(0x80 >> (i % 8));
			}
		}
	}
}
