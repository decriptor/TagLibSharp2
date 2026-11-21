// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// One <c>strl</c> list: the AVISTREAMHEADER fields that time a stream, and its <c>strf</c> bytes.
/// </summary>
/// <remarks>
/// AVISTREAMHEADER: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/avifmt/ns-avifmt-avistreamheader
/// </remarks>
internal readonly struct AviStreamHeader
{
	public AviStreamHeader (string type, uint handler, uint scale, uint rate, uint length, byte[] format)
	{
		Type = type;
		Handler = handler;
		Scale = scale;
		Rate = rate;
		Length = length;
		Format = format;
	}

	/// <summary>fccType: <c>vids</c>, <c>auds</c>, <c>iavs</c>, <c>txts</c> or <c>mids</c>.</summary>
	public string Type { get; }

	/// <summary>fccHandler, as stored.</summary>
	public uint Handler { get; }

	/// <summary>dwScale. dwRate / dwScale is the stream rate in units per second.</summary>
	public uint Scale { get; }

	/// <summary>dwRate.</summary>
	public uint Rate { get; }

	/// <summary>dwLength, in dwRate / dwScale units.</summary>
	public uint Length { get; }

	/// <summary>The <c>strf</c> chunk data: BITMAPINFOHEADER for video, WAVEFORMATEX for audio.</summary>
	public byte[] Format { get; }

	/// <summary>dwLength * dwScale / dwRate, or zero when the rate is zero.</summary>
	public TimeSpan Duration => AviHeaders.Seconds (Rate == 0 ? 0 : (double)Length * Scale / Rate);
}

/// <summary>
/// The <c>hdrl</c> list of an AVI file: the main header, each stream header, and the OpenDML <c>dmlh</c> total.
/// </summary>
/// <remarks>
/// <para>AVI RIFF File Reference: https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference</para>
/// <para>AVIMAINHEADER: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avimainheader</para>
/// <para>
/// OpenDML AVI File Format Extensions 1.02, section "Extended AVI Header (dmlh)":
/// <c>dwTotalFrames</c> counts every frame in the file. AVIMAINHEADER.dwTotalFrames counts
/// only the frames in the first RIFF, so an OpenDML reader takes the <c>dmlh</c> value.
/// </para>
/// </remarks>
internal sealed class AviHeaders
{
	const int MainHeaderBytes = 40;
	const int StreamHeaderBytes = 36;

	bool _sawMainHeader;

	public uint MicroSecondsPerFrame { get; private set; }

	public uint PaddingGranularity { get; private set; }

	public uint TotalFrames { get; private set; }

	public uint Width { get; private set; }

	public uint Height { get; private set; }

	/// <summary>dmlh dwTotalFrames, or zero when there is no <c>odml</c> list.</summary>
	public uint OdmlTotalFrames { get; private set; }

	public List<AviStreamHeader> Streams { get; } = [];

	/// <summary>Reads the body of a <c>LIST hdrl</c>, the bytes after the <c>hdrl</c> list type.</summary>
	public static AviHeaders Parse (ReadOnlySpan<byte> hdrl)
	{
		var headers = new AviHeaders ();
		foreach (var child in Children (hdrl)) {
			var body = hdrl.Slice (child.DataOffset, child.DataLength);
			if (child.Is ("avih")) {
				if (body.Length >= MainHeaderBytes && !headers._sawMainHeader)
					headers.ReadMainHeader (body);
			} else if (child.IsList ("strl")) {
				headers.ReadStreamList (body.Slice (4));
			} else if (child.IsList ("odml")) {
				foreach (var odml in Children (body.Slice (4))) {
					if (odml.Is ("dmlh") && odml.DataLength >= 4) {
						headers.OdmlTotalFrames = BinaryPrimitives.ReadUInt32LittleEndian (body.Slice (4 + odml.DataOffset));
						break;
					}
				}
			}
		}

		return headers;
	}

	/// <summary>
	/// Drops every frame count and stream length, for a file whose writer never filled them in.
	/// </summary>
	public void ForgetLengths ()
	{
		TotalFrames = 0;
		OdmlTotalFrames = 0;
		for (var i = 0; i < Streams.Count; i++) {
			var stream = Streams[i];
			Streams[i] = new AviStreamHeader (stream.Type, stream.Handler, stream.Scale, stream.Rate, 0, stream.Format);
		}
	}

	void ReadMainHeader (ReadOnlySpan<byte> avih)
	{
		_sawMainHeader = true;
		MicroSecondsPerFrame = BinaryPrimitives.ReadUInt32LittleEndian (avih);
		PaddingGranularity = BinaryPrimitives.ReadUInt32LittleEndian (avih.Slice (8));
		TotalFrames = BinaryPrimitives.ReadUInt32LittleEndian (avih.Slice (16));
		Width = BinaryPrimitives.ReadUInt32LittleEndian (avih.Slice (32));
		Height = BinaryPrimitives.ReadUInt32LittleEndian (avih.Slice (36));
	}

	void ReadStreamList (ReadOnlySpan<byte> strl)
	{
		ReadOnlySpan<byte> strh = default;
		ReadOnlySpan<byte> strf = default;
		var sawHeader = false;
		foreach (var child in Children (strl)) {
			if (child.Is ("strh") && !sawHeader) {
				strh = strl.Slice (child.DataOffset, child.DataLength);
				sawHeader = true;
			} else if (child.Is ("strf") && strf.IsEmpty) {
				strf = strl.Slice (child.DataOffset, child.DataLength);
			}
		}

		if (strh.Length < StreamHeaderBytes)
			return;

		Streams.Add (new AviStreamHeader (
			FourCC (strh),
			BinaryPrimitives.ReadUInt32LittleEndian (strh.Slice (4)),
			BinaryPrimitives.ReadUInt32LittleEndian (strh.Slice (20)),
			BinaryPrimitives.ReadUInt32LittleEndian (strh.Slice (24)),
			BinaryPrimitives.ReadUInt32LittleEndian (strh.Slice (32)),
			strf.ToArray ()));
	}

	/// <summary>
	/// Seconds as a <see cref="TimeSpan"/>, or zero when the value is not a positive finite duration.
	/// A 32-bit length times a 32-bit scale can exceed <see cref="TimeSpan.MaxValue"/>.
	/// </summary>
	internal static TimeSpan Seconds (double seconds)
	{
		const double MaxSeconds = long.MaxValue / (double)TimeSpan.TicksPerSecond / 2;
		if (double.IsNaN (seconds) || seconds <= 0 || seconds >= MaxSeconds)
			return TimeSpan.Zero;

		return TimeSpan.FromTicks ((long)Math.Round (seconds * TimeSpan.TicksPerSecond));
	}

	internal static string FourCC (ReadOnlySpan<byte> data) =>
		new ([(char)data[0], (char)data[1], (char)data[2], (char)data[3]]);

	/// <summary>
	/// The sub-chunks of a list body. Each one's data is clamped to the body, and a chunk
	/// that runs past the body ends the walk. The walk is linear in the body length.
	/// </summary>
	internal static List<Child> Children (ReadOnlySpan<byte> body)
	{
		var children = new List<Child> ();
		var offset = 0;
		while (offset + RiffChunk.HeaderSize <= body.Length) {
			var size = BinaryPrimitives.ReadUInt32LittleEndian (body.Slice (offset + 4));
			var dataOffset = offset + RiffChunk.HeaderSize;
			var available = body.Length - dataOffset;
			var length = size > (uint)available ? available : (int)size;
			var isList = length >= 4 && body[offset] == 'L' && body[offset + 1] == 'I' && body[offset + 2] == 'S' && body[offset + 3] == 'T';
			children.Add (new Child (
				FourCC (body.Slice (offset)),
				isList ? FourCC (body.Slice (dataOffset)) : null,
				dataOffset,
				length));

			var next = (long)dataOffset + size + (size & 1);
			if (next > body.Length)
				break;
			offset = (int)next;
		}

		return children;
	}

	internal readonly struct Child
	{
		public Child (string fourCC, string? listType, int dataOffset, int dataLength)
		{
			FourCCValue = fourCC;
			ListType = listType;
			DataOffset = dataOffset;
			DataLength = dataLength;
		}

		public string FourCCValue { get; }

		public string? ListType { get; }

		public int DataOffset { get; }

		public int DataLength { get; }

		public bool Is (string fourCC) => FourCCValue == fourCC;

		public bool IsList (string listType) => ListType == listType;
	}
}

/// <summary>
/// Turns AVI stream headers into <see cref="VideoProperties"/> and <see cref="AudioProperties"/>.
/// </summary>
/// <remarks>
/// <para>BITMAPINFOHEADER: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader</para>
/// <para>WAVEFORMATEX: https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatex</para>
/// </remarks>
internal static class AviProperties
{
	/// <summary>
	/// The file duration. The video stream is timed by its strh dwRate / dwScale. Its frame
	/// count is dmlh dwTotalFrames when present (OpenDML), else strh dwLength, else
	/// avih dwTotalFrames, which covers only the first RIFF. Without video, the first audio stream's length.
	/// </summary>
	public static TimeSpan Duration (AviHeaders headers)
	{
		if (FindVideo (headers) is { } video) {
			var frames = headers.OdmlTotalFrames != 0 ? headers.OdmlTotalFrames
				: video.Length != 0 ? video.Length
				: headers.TotalFrames;
			if (video.Rate != 0 && video.Scale != 0)
				return AviHeaders.Seconds ((double)frames * video.Scale / video.Rate);
			return AviHeaders.Seconds ((double)frames * headers.MicroSecondsPerFrame / 1_000_000d);
		}

		if (FindAudio (headers) is { } audio)
			return audio.Duration;

		return AviHeaders.Seconds ((double)headers.TotalFrames * headers.MicroSecondsPerFrame / 1_000_000d);
	}

	public static AviStreamHeader? FindVideo (AviHeaders headers)
	{
		foreach (var stream in headers.Streams) {
			if (stream.Type is "vids" or "iavs")
				return stream;
		}

		return null;
	}

	public static AviStreamHeader? FindAudio (AviHeaders headers)
	{
		foreach (var stream in headers.Streams) {
			if (stream.Type == "auds")
				return stream;
		}

		return null;
	}

	public static VideoProperties? Video (AviHeaders headers, TimeSpan duration)
	{
		if (FindVideo (headers) is not { } video)
			return null;

		var width = 0;
		var height = 0;
		string? codec = null;
		var format = video.Format;
		if (video.Type == "vids" && format.Length >= 12) {
			var biWidth = BinaryPrimitives.ReadInt32LittleEndian (format.AsSpan (4));
			// A negative biHeight is a top-down bitmap. Its size is the absolute value.
			var biHeight = BinaryPrimitives.ReadInt32LittleEndian (format.AsSpan (8));
			if (biWidth > 0 && biHeight != 0 && biHeight != int.MinValue) {
				width = biWidth;
				height = Math.Abs (biHeight);
			}

			if (format.Length >= 20)
				codec = Compression (BinaryPrimitives.ReadUInt32LittleEndian (format.AsSpan (16)));
		}

		if (width == 0 && headers.Width is > 0 and <= int.MaxValue && headers.Height is > 0 and <= int.MaxValue) {
			width = (int)headers.Width;
			height = (int)headers.Height;
		}

		codec ??= PrintableFourCC (video.Handler);
		var frameRate = video.Rate != 0 && video.Scale != 0
			? (double)video.Rate / video.Scale
			: headers.MicroSecondsPerFrame != 0 ? 1_000_000d / headers.MicroSecondsPerFrame : 0;
		return new VideoProperties (duration, width, height, 0, frameRate, codec);
	}

	public static AudioProperties? Audio (AviHeaders headers, TimeSpan fileDuration)
	{
		if (FindAudio (headers) is not { } audio)
			return null;

		var format = audio.Format;
		var duration = audio.Duration > TimeSpan.Zero ? audio.Duration : fileDuration;
		if (format.Length < 16)
			return new AudioProperties (duration, 0, 0, 0, 0, null);

		var tag = BinaryPrimitives.ReadUInt16LittleEndian (format);
		var channels = BinaryPrimitives.ReadUInt16LittleEndian (format.AsSpan (2));
		var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian (format.AsSpan (4));
		var bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian (format.AsSpan (8));
		var bits = BinaryPrimitives.ReadUInt16LittleEndian (format.AsSpan (14));
		var bitrate = (int)((long)bytesPerSecond * 8 / 1000);
		return new AudioProperties (
			duration,
			bitrate,
			sampleRate > int.MaxValue ? 0 : (int)sampleRate,
			bits,
			channels,
			AudioCodec (tag, format));
	}

	/// <summary>
	/// biCompression. BI_RGB (0) and BI_BITFIELDS (3) are uncompressed RGB. Any other value is a FourCC.
	/// </summary>
	static string? Compression (uint compression) => compression switch {
		0 or 3 => "RGB",
		1 => "RLE8",
		2 => "RLE4",
		_ => PrintableFourCC (compression)
	};

	static string? PrintableFourCC (uint value)
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (bytes, value);
		foreach (var b in bytes) {
			if (b < 0x20 || b > 0x7E)
				return null;
		}

		var text = AviHeaders.FourCC (bytes).TrimEnd ();
		return text.Length == 0 ? null : text;
	}

	/// <summary>
	/// WAVEFORMATEX wFormatTag, as registered in mmreg.h. WAVE_FORMAT_EXTENSIBLE names its SubFormat.
	/// </summary>
	static string AudioCodec (ushort tag, byte[] format)
	{
		if (tag == WavAudioPropertiesParser.FormatExtensible
			&& WavAudioPropertiesParser.ParseExtended (new BinaryData (format)) is { } extended) {
			return extended.SubFormat switch {
				WavSubFormat.Pcm => "PCM",
				WavSubFormat.IeeeFloat => "IEEE Float",
				WavSubFormat.ALaw => "A-Law",
				WavSubFormat.MuLaw => "mu-Law",
				_ => "Extensible"
			};
		}

		return tag switch {
			0x0001 => "PCM",
			0x0002 => "MS ADPCM",
			0x0003 => "IEEE Float",
			0x0006 => "A-Law",
			0x0007 => "mu-Law",
			0x0011 => "IMA ADPCM",
			0x0050 => "MPEG Audio",
			0x0055 => "MP3",
			0x00FF or 0x1600 or 0x1610 => "AAC",
			0x0160 or 0x0161 or 0x0162 or 0x0163 => "WMA",
			0x2000 => "AC-3",
			0x2001 => "DTS",
			0xFFFE => "Extensible",
			_ => "0x" + tag.ToString ("X4", System.Globalization.CultureInfo.InvariantCulture)
		};
	}
}
