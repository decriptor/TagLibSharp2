// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Image.Jpeg;

/// <summary>
/// The width, height and bit depth of a JPEG: the first start-of-frame segment (ITU-T T.81 §B.2.2), or with a zero
/// height, the DNL segment after the first scan (§B.2.5).
/// </summary>
static class JpegSize
{
	public static bool IsJpeg (ReadOnlySpan<byte> data) =>
		data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

	public static bool TryRead (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		if (!IsJpeg (data))
			return false;
		var i = 2;
		Frame? pending = null;
		while (TryNextSegment (data, ref i, out var marker, out var length)) {
			if (IsStartOfFrame (marker)) {
				if (!TryFrame (data.Slice (i, length), out var frame))
					return false;
				if (frame.Height > 0) {
					properties = frame.Properties (frame.Height);
					return true;
				}

				// ITU-T T.81 §B.2.2: a zero height is defined by the DNL segment at the end of the first scan.
				pending = frame;
			} else if (marker == 0xDA) {
				if (pending is not Frame waiting || !TryDnlHeight (data, i + length, out var lines))
					return false;
				properties = waiting.Properties (lines);
				return true;
			}

			i += length;
		}

		return false;
	}

	// The next marker segment at i, past fill bytes and RSTn: its marker, and i at its length field. False at a
	// byte that is not a marker, at SOI or EOI, or when the segment runs past the data.
	static bool TryNextSegment (ReadOnlySpan<byte> data, ref int i, out byte marker, out int length)
	{
		marker = 0;
		length = 0;
		while (i + 1 < data.Length) {
			if (data[i] != 0xFF)
				return false;
			while (i < data.Length && data[i] == 0xFF)
				i++;
			if (i >= data.Length)
				return false;
			marker = data[i++];
			if (marker is 0xD8 or 0xD9)
				return false;
			if (marker is >= 0xD0 and <= 0xD7)
				continue;
			if (i + 1 >= data.Length)
				return false;
			length = (data[i] << 8) | data[i + 1];
			return length >= 2 && i + length <= data.Length;
		}

		return false;
	}

	readonly record struct Frame (int Width, int Height, int Precision, int Components)
	{
		public ImageProperties Properties (int height)
		{
			var depth = Precision;
			if (Components is >= 1 and <= 4 && Precision <= int.MaxValue / Components)
				depth = Precision * Components;
			return new ImageProperties (Width, height, depth, "JPEG");
		}
	}

	// A start-of-frame segment from its length field: precision, height, width, then the component count.
	static bool TryFrame (ReadOnlySpan<byte> segment, out Frame frame)
	{
		frame = default;
		if (segment.Length < 7)
			return false;
		var height = (segment[3] << 8) | segment[4];
		var width = (segment[5] << 8) | segment[6];
		if (width <= 0 || height < 0)
			return false;
		var components = segment.Length >= 8 && segment[7] is >= 1 and <= 4 ? segment[7] : 1;
		frame = new Frame (width, height, segment[2], components);
		return true;
	}

	// ITU-T T.81 §B.2.5: a DNL segment "shall immediately follow the first scan". The scan's entropy-coded data
	// (§B.1.1.5) holds no marker: a 0xFF data byte is followed by a stuffed 0x00, and RSTn (0xD0-0xD7) may sit
	// between intervals. Fill bytes 0xFF may precede a marker (§B.1.1.2). Any other marker before DNL means
	// there is none. One pass over the bytes present.
	static bool TryDnlHeight (ReadOnlySpan<byte> data, int scan, out int lines)
	{
		lines = 0;
		var i = scan;
		while (i < data.Length) {
			if (data[i] != 0xFF) {
				i++;
				continue;
			}

			while (i < data.Length && data[i] == 0xFF)
				i++;
			if (i >= data.Length)
				return false;
			var marker = data[i++];
			if (marker == 0x00 || marker is >= 0xD0 and <= 0xD7)
				continue;
			if (marker != 0xDC || i + 3 >= data.Length)
				return false;
			var length = (data[i] << 8) | data[i + 1];
			if (length != 4)
				return false;
			lines = (data[i + 2] << 8) | data[i + 3];
			return lines > 0;
		}

		return false;
	}

	static bool IsStartOfFrame (byte marker) =>
		marker is 0xC0 or 0xC1 or 0xC2 or 0xC3
			or 0xC5 or 0xC6 or 0xC7
			or 0xC9 or 0xCA or 0xCB
			or 0xCD or 0xCE or 0xCF;
}
