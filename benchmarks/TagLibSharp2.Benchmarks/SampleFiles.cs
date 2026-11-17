using System.Buffers.Binary;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Benchmarks;

static class SampleFiles
{
	public static readonly string[] Formats = ["mp3", "wav", "flac"];

	public static byte[] Create (string format, int payloadBytes) => format switch {
		"mp3" => Mp3 (payloadBytes),
		"wav" => Wav (payloadBytes),
		"flac" => Flac (payloadBytes),
		_ => throw new ArgumentOutOfRangeException (nameof (format), format, "Unknown sample format")
	};

	static byte[] Mp3 (int payloadBytes)
	{
		var id3v2 = new Id3v2Tag { Title = "Window" }.Render ().ToArray ();
		var header = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
		const int FrameSize = 417;
		var audio = new byte[FrameSize + 4];
		header.CopyTo (audio, 0);
		header.CopyTo (audio, FrameSize);
		var id3v1 = new Id3v1Tag { Title = "Tail" }.Render ().ToArray ();
		var file = new byte[id3v2.Length + audio.Length + payloadBytes + id3v1.Length];
		var offset = 0;
		id3v2.CopyTo (file, offset);
		offset += id3v2.Length;
		audio.CopyTo (file, offset);
		offset += audio.Length + payloadBytes;
		id3v1.CopyTo (file, offset);
		return file;
	}

	static byte[] Wav (int payloadBytes)
	{
		var title = "Window\0"u8;
		var listBody = 4 + 4 + 4 + title.Length;
		var form = 4 + (8 + 16) + (8 + payloadBytes) + (8 + listBody);
		var file = new byte[8 + form];
		var span = file.AsSpan ();
		"RIFF"u8.CopyTo (span);
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (4), (uint)form);
		var o = 8;
		"WAVE"u8.CopyTo (span.Slice (o));
		o += 4;
		"fmt "u8.CopyTo (span.Slice (o));
		o += 4;
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (o), 16);
		o += 4;
		BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (o), 1);
		o += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (o), 1);
		o += 2;
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (o), 44100);
		o += 4;
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (o), 44100);
		o += 4;
		BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (o), 1);
		o += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (span.Slice (o), 8);
		o += 2;
		"data"u8.CopyTo (span.Slice (o));
		o += 4;
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (o), (uint)payloadBytes);
		o += 4 + payloadBytes;
		"LIST"u8.CopyTo (span.Slice (o));
		o += 4;
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (o), (uint)listBody);
		o += 4;
		"INFO"u8.CopyTo (span.Slice (o));
		o += 4;
		"INAM"u8.CopyTo (span.Slice (o));
		o += 4;
		BinaryPrimitives.WriteUInt32LittleEndian (span.Slice (o), (uint)title.Length);
		o += 4;
		title.CopyTo (span.Slice (o));
		return file;
	}

	static byte[] Flac (int payloadBytes)
	{
		var file = new byte[4 + 4 + 34 + payloadBytes];
		"fLaC"u8.CopyTo (file);
		file[4] = 0x80;
		file[7] = 34;
		var info = file.AsSpan (8, 34);
		const int SampleRate = 44100;
		info[10] = (byte)((SampleRate >> 12) & 0xFF);
		info[11] = (byte)((SampleRate >> 4) & 0xFF);
		info[12] = (byte)((SampleRate & 0x0F) << 4);
		info[12] |= 0x02;
		info[13] = 0xF0;
		BinaryPrimitives.WriteUInt32BigEndian (info.Slice (14), 44100);
		return file;
	}
}
