// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Aac;

/// <summary>
/// Builds ADTS and ADIF streams from ISO/IEC 13818-7 §6.1 and §6.2 for tests.
/// </summary>
public static class AacTestBuilder
{
	/// <summary>A payload byte that cannot start a sync word.</summary>
	public const byte Fill = 0x11;

	/// <summary>
	/// One ADTS frame. <paramref name="length"/> is aac_frame_length, header included.
	/// </summary>
	public static byte[] AdtsFrame (
		int length = 64,
		int sampleRateIndex = 4,
		int channelConfiguration = 2,
		int profile = 1,
		bool mpeg2 = false,
		bool crc = false,
		int rawBlocks = 1,
		byte[]? payload = null)
	{
		var frame = new byte[length];
		frame[0] = 0xFF;
		frame[1] = (byte)(0xF0 | (mpeg2 ? 0x08 : 0) | (crc ? 0 : 0x01));
		frame[2] = (byte)((profile << 6) | (sampleRateIndex << 2) | ((channelConfiguration >> 2) & 0x01));
		frame[3] = (byte)(((channelConfiguration & 0x03) << 6) | ((length >> 11) & 0x03));
		frame[4] = (byte)((length >> 3) & 0xFF);
		frame[5] = (byte)(((length & 0x07) << 5) | 0x1F); // buffer fullness 0x7FF (VBR)
		frame[6] = (byte)(0xFC | ((rawBlocks - 1) & 0x03));
		var start = 7 + (crc ? 2 * rawBlocks : 0);
		for (var i = start; i < length; i++)
			frame[i] = Fill;
		payload?.CopyTo (frame, start);
		return frame;
	}

	/// <summary>
	/// <paramref name="frames"/> identical ADTS frames.
	/// </summary>
	public static byte[] Adts (
		int frames,
		int length = 64,
		int sampleRateIndex = 4,
		int channelConfiguration = 2,
		int profile = 1,
		bool mpeg2 = false,
		bool crc = false,
		int rawBlocks = 1,
		byte[]? payload = null)
	{
		var one = AdtsFrame (length, sampleRateIndex, channelConfiguration, profile, mpeg2, crc, rawBlocks, payload);
		var all = new byte[one.Length * frames];
		for (var i = 0; i < frames; i++)
			one.CopyTo (all, i * one.Length);
		return all;
	}

	/// <summary>
	/// A raw data block that opens with a program_config_element (id_syn_ele ID_PCE = 5).
	/// </summary>
	public static byte[] PceBlock (int[] front, int lfe = 0, int sampleRateIndex = 4, int profile = 1, string comment = "")
	{
		var bits = new BitWriter ();
		bits.Write (5, 3);
		WritePce (bits, front, lfe, sampleRateIndex, profile, comment);
		return bits.ToArray ();
	}

	/// <summary>
	/// An ADIF stream: the header and <paramref name="payloadBytes"/> of raw data.
	/// Each entry of <paramref name="programs"/> is the front element list (true = channel pair) of one
	/// program config element.
	/// </summary>
	public static byte[] Adif (
		int bitrate,
		bool variableRate = false,
		int sampleRateIndex = 4,
		int profile = 1,
		bool[][]? programs = null,
		int lfe = 0,
		int payloadBytes = 1000,
		bool copyright = false,
		string comment = "")
	{
		programs ??= [[true]];
		var bits = new BitWriter ();
		foreach (var c in "ADIF")
			bits.Write ((byte)c, 8);
		bits.Write (copyright ? 1 : 0, 1);
		if (copyright) {
			for (var i = 0; i < 9; i++)
				bits.Write (0xAB, 8);
		}
		bits.Write (0, 1); // original_copy
		bits.Write (0, 1); // home
		bits.Write (variableRate ? 1 : 0, 1);
		bits.Write (bitrate, 23);
		bits.Write (programs.Length - 1, 4);
		foreach (var program in programs) {
			if (!variableRate)
				bits.Write (0x12345, 20);
			var front = program.Select (pair => pair ? 2 : 1).ToArray ();
			WritePce (bits, front, lfe, sampleRateIndex, profile, comment);
		}

		var header = bits.ToArray ();
		var file = new byte[header.Length + payloadBytes];
		header.CopyTo (file, 0);
		for (var i = header.Length; i < file.Length; i++)
			file[i] = Fill;
		return file;
	}

	/// <summary>
	/// Writes one program_config_element (ISO/IEC 13818-7 §6.3 Table 25). Each <paramref name="front"/>
	/// entry is 1 (single channel element) or 2 (channel pair element).
	/// </summary>
	static void WritePce (BitWriter bits, int[] front, int lfe, int sampleRateIndex, int profile, string comment)
	{
		bits.Write (0, 4); // element_instance_tag
		bits.Write (profile, 2);
		bits.Write (sampleRateIndex, 4);
		bits.Write (front.Length, 4);
		bits.Write (1, 4); // one side element: a single channel element
		bits.Write (0, 4); // back
		bits.Write (lfe, 2);
		bits.Write (1, 3); // one data element
		bits.Write (1, 4); // one coupling element
		bits.Write (1, 1); // mono_mixdown_present
		bits.Write (3, 4);
		bits.Write (1, 1); // stereo_mixdown_present
		bits.Write (4, 4);
		bits.Write (1, 1); // matrix_mixdown_idx_present
		bits.Write (1, 3);
		foreach (var element in front) {
			bits.Write (element == 2 ? 1 : 0, 1);
			bits.Write (0, 4);
		}
		bits.Write (0, 1); // side: single channel element
		bits.Write (1, 4);
		for (var i = 0; i < lfe; i++)
			bits.Write (0, 4);
		bits.Write (0, 4); // data element tag
		bits.Write (1, 1); // coupling: is_ind_sw
		bits.Write (0, 4);
		bits.AlignToByte ();
		bits.Write (comment.Length, 8);
		foreach (var c in comment)
			bits.Write ((byte)c, 8);
	}

	/// <summary>The channels <see cref="PceBlock"/> and <see cref="Adif"/> describe: front + one side + LFE.</summary>
	public static int PceChannels (int[] front, int lfe = 0) => front.Sum () + 1 + lfe;

	/// <summary>An ID3v2.4 tag with a title.</summary>
	public static byte[] Id3v2Bytes (string title) => new Id3v2Tag { Title = title }.Render ().ToArray ();

	/// <summary>An ID3v1 tag with a title.</summary>
	public static byte[] Id3v1Bytes (string title) => new Id3v1Tag { Title = title }.Render ().ToArray ();

	/// <summary>An APEv2 tag with a title.</summary>
	public static byte[] ApeBytes (string title) => new ApeTag { Title = title }.Render ().ToArray ();

	/// <summary>
	/// MPEG-1 Layer III frames, 128 kbps, 44.1 kHz, 417 bytes each (ISO/IEC 11172-3 §2.4.2.3).
	/// </summary>
	public static byte[] Mp3Frames (int frames)
	{
		var all = new byte[417 * frames];
		for (var f = 0; f < frames; f++) {
			var o = f * 417;
			all[o] = 0xFF;
			all[o + 1] = 0xFB;
			all[o + 2] = 0x90;
			all[o + 3] = 0x00;
		}
		return all;
	}

	/// <summary>Concatenates byte arrays.</summary>
	public static byte[] Concat (params byte[][] parts)
	{
		var all = new byte[parts.Sum (p => p.Length)];
		var at = 0;
		foreach (var part in parts) {
			part.CopyTo (all, at);
			at += part.Length;
		}
		return all;
	}

	/// <summary>Index of <paramref name="needle"/> in <paramref name="haystack"/>, or -1.</summary>
	public static int IndexOf (byte[] haystack, byte[] needle) =>
		haystack.AsSpan ().IndexOf (needle);

	/// <summary>A read-only stream that counts reads and bytes.</summary>
	public sealed class CountingStream (byte[] data) : MemoryStream (data, writable: false)
	{
		public int Reads { get; private set; }

		public long BytesRead { get; private set; }

		public void Reset ()
		{
			Reads = 0;
			BytesRead = 0;
			Position = 0;
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			Reads++;
			var n = base.Read (buffer, offset, count);
			BytesRead += n;
			return n;
		}
	}

	/// <summary>
	/// An ID3v2 tag, then MPEG-1 Layer III frames whose first frame's payload holds a byte run that parses
	/// as an MPEG-2 ADTS header (FF F8 15 07 09 CB: Main, 32 kHz, 4 channels, 6222-byte frame), as a real
	/// encoder's output can.
	/// </summary>
	public static byte[] Mp3WithAdtsLookalike (int frames = 5)
	{
		var mp3 = Mp3Frames (frames);
		byte[] lookalike = [0xFF, 0xF8, 0x15, 0x07, 0x09, 0xCB, 0x00];
		lookalike.CopyTo (mp3, 0x93);
		return Concat (Id3v2Bytes ("Tagged"), mp3);
	}

	sealed class BitWriter
	{
		readonly List<byte> _bytes = [];
		int _bit;

		public void Write (int value, int count)
		{
			for (var i = count - 1; i >= 0; i--) {
				if (_bit == 0)
					_bytes.Add (0);
				if (((value >> i) & 1) == 1)
					_bytes[^1] |= (byte)(0x80 >> _bit);
				_bit = (_bit + 1) & 7;
			}
		}

		public void AlignToByte ()
		{
			while (_bit != 0)
				Write (0, 1);
		}

		public byte[] ToArray () => [.. _bytes];
	}
}
