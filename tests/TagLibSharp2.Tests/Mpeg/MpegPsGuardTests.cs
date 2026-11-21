// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using static TagLibSharp2.Tests.Mpeg.MpegPsFileTests;
using static TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// One test per input check in the MPEG program stream reader, each with an input the check exists for.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsGuardTests
{
	const long Start = 45_000;

	[TestMethod]
	public void Read_TailStampJustBeforeTheStart_IsNotAWrap ()
	{
		// A last packet stamped 10 ms before the first one (a reordered or spliced frame). Taken
		// modulo 2^33 it would be 26.5 hours after the start. The stream is longer than the head
		// window, so only the tail sees that packet.
		var data = Concat (
			ProgramStream (new Options { EndCode = false, VideoFrameBytes = 12_000 }),
			PackHeader1 (200_000),
			Pes (0xE0, Start - 900, Picture (32), mpeg2: false),
			[0x00, 0x00, 0x01, 0xB9]);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
	}

	[TestMethod]
	public void Read_Mpeg2StampWithAWrongPrefix_IsIgnored ()
	{
		// PTS_DTS_flags say a PTS follows, but its prefix is '1010', not '0010' (13818-1 2.4.3.7).
		var stamp = Timestamp (0x2, Start + (100 * TicksPerSecond));
		stamp[0] |= 0x80;
		var data = Concat (
			ProgramStream (new Options { Mpeg2 = true, EndCode = false }),
			PackHeader2 (200_000),
			[0x00, 0x00, 0x01, 0xE0, 0x00, (byte)(8 + 32), 0x81, 0x80, 0x05],
			stamp,
			Picture (32),
			[0x00, 0x00, 0x01, 0xB9]);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
	}

	[TestMethod]
	public void Read_DropFrameAt5994_SkipsFourPictureNumbersAMinute ()
	{
		// 59.94 Hz drop-frame time code skips ;00 to ;03 at each minute except every tenth, so
		// 00:01:00;04 is picture 60 * 60 + 4 - 4 = 3600. Ten pictures follow that GOP header.
		var o = new Options { Mpeg2 = true, FrameRateCode = 7, Width = 1280, Height = 720 };
		var data = Concat (
			SequenceHeader (o),
			GopHeader (0, 0, 0, 0, dropFrame: true),
			Picture (16),
			GopHeader (0, 1, 0, 4, dropFrame: true),
			Repeat (Picture (16), 10),
			[0x00, 0x00, 0x01, 0xB7]);

		var file = MpegPsFile.Read (data).File!;

		var expected = TimeSpan.FromTicks ((long)Math.Round (3610 * 1001d / 60000 * TimeSpan.TicksPerSecond));
		Assert.AreEqual (expected, file.Duration);
	}

	[TestMethod]
	public void Render_SameLengthOriginalWithAnotherTagSize_ReturnsEmpty ()
	{
		var stream = ProgramStream ();
		var data = Concat (Id3v2 ("Front"), stream);
		var file = MpegPsFile.Read (data).File!;
		// Forty more title bytes push the stream back by forty bytes, all of them tag padding
		// where the stream used to start. The length is kept by cutting the end.
		var other = Concat (Id3v2 ("Front" + new string ('x', 40)), stream).AsSpan (0, data.Length).ToArray ();

		Assert.IsTrue (file.Render (other).IsEmpty);
		Assert.IsFalse (file.SaveToFile ("/x.mpg", other, new Tests.Core.MockFileSystem ()).IsSuccess);
	}

	[TestMethod]
	public void Read_Mpeg2AllOnesBitRateAndExtension_IsNotARate ()
	{
		var data = ProgramStream (new Options { Mpeg2 = true, BitRateValue = 0x3FFFF, BitRateExtension = 0xFFF });

		Assert.AreEqual (0, MpegPsFile.Read (data).File!.VideoProperties!.Value.Bitrate);
	}

	[TestMethod]
	public void Read_Mpeg2BitRateExtension_AddsTheHighBits ()
	{
		// (1 << 18 | 2500) * 400 bit/s.
		var data = ProgramStream (new Options { Mpeg2 = true, BitRateValue = 2500, BitRateExtension = 1 });

		Assert.AreEqual ((int)((((1L << 18) | 2500) * 400) / 1000), MpegPsFile.Read (data).File!.VideoProperties!.Value.Bitrate);
	}

	[TestMethod]
	[DataRow (3)]
	[DataRow (4)]
	public void Read_Id3VersionOrRevisionFF_IsNotAnId3v2Tag (int offset)
	{
		// ID3v2.4 structure 3.1: "$49 44 33 yy yy xx zz zz zz zz", where yy is below $FF.
		byte[] tag = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
		tag[offset] = 0xFF;
		var data = Concat (tag, ProgramStream ());

		Assert.IsFalse (MpegPsFile.IsValidFormat (data));
		Assert.IsFalse (MpegPsFile.Read (data).IsSuccess);
		Assert.AreNotEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (data));
	}

	[TestMethod]
	public void Read_PrependedTagWithAFooter_StartsTheStreamAfterTheFooter ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Footed" }.Render (0, withFooter: true).ToArray ();
		var stream = ProgramStream ();
		var data = Concat (tag, stream);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Footed", file.Tag!.Title);
		Assert.AreEqual (tag.Length, file.Id3v2Size);
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (data));
		file.Tag.Title = "Changed";
		var reread = MpegPsFile.Read (file.Render (data).ToArray ()).File!;
		Assert.AreEqual ("Changed", reread.Tag!.Title);
	}

	[TestMethod]
	public void Read_EnhancedAc3_IsNotDecodedAsAc3 ()
	{
		// bsid 16 is E-AC-3 (A/52 Annex E), whose syncinfo has other fields where AC-3 has fscod.
		var data = ProgramStream (new Options { Mpeg2 = true, AudioKind = Audio.Ac3, Ac3Bsid = 16 });

		var audio = MpegPsFile.Read (data).File!.AudioProperties!.Value;

		Assert.AreEqual ("AC-3", audio.Codec);
		Assert.AreEqual (0, audio.SampleRate);
		Assert.AreEqual (0, audio.Channels);
	}

	[TestMethod]
	public void Read_ManyPrivateSubStreams_StillReadsTheAudioAfterThem ()
	{
		// Every private_stream_1 sub-stream id is a stream: 100 that are not audio (0x00 to 0x63)
		// before the MPEG audio.
		using var output = new MemoryStream ();
		void Write (byte[] bytes) => output.Write (bytes, 0, bytes.Length);
		Write (PackHeader2 (0));
		for (var sub = 0; sub < 100; sub++)
			Write (Pes (0xBD, Start, Concat ([(byte)sub], new byte[16]), mpeg2: true));
		Write (Pes (0xC0, Start, Concat (Mp2Frame (), Mp2Frame ()), mpeg2: true));
		Write ([0x00, 0x00, 0x01, 0xB9]);

		var file = MpegPsFile.Read (output.ToArray ()).File!;

		Assert.AreEqual (48000, file.AudioProperties!.Value.SampleRate);
	}

	static byte[] Timestamp (int prefix, long value)
	{
		var b = new byte[5];
		b[0] = (byte)((prefix << 4) | (int)((value >> 29) & 0x0E) | 0x01);
		b[1] = (byte)(value >> 22);
		b[2] = (byte)((int)((value >> 14) & 0xFE) | 0x01);
		b[3] = (byte)(value >> 7);
		b[4] = (byte)((int)((value << 1) & 0xFE) | 0x01);
		return b;
	}

	static byte[] Repeat (byte[] part, int count)
	{
		var parts = new byte[count][];
		for (var i = 0; i < count; i++)
			parts[i] = part;
		return Concat (parts);
	}
}
