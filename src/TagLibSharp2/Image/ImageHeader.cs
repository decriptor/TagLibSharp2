// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image.Jpeg;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image;

/// <summary>
/// Detects the still-image formats and reads their dimensions from the container, not from Exif. PNG IHDR is the
/// PNG specification, GIF the logical screen of GIF89a, BMP the DIB header, PCX the ZSoft window and Netpbm the
/// P1-P6 raster header; JPEG, TIFF and DNG, and SVG have their own readers (<see cref="JpegSize"/>,
/// <see cref="TiffSize"/>, <see cref="SvgSize"/>).
/// </summary>
internal static class ImageHeader
{
	public static bool TryDetect (ReadOnlySpan<byte> data, out MediaFormat format)
	{
		format = JpegSize.IsJpeg (data) ? MediaFormat.Jpeg
			: IsPng (data) ? MediaFormat.Png
			: IsGif (data) ? MediaFormat.Gif
			: IsBmp (data) ? MediaFormat.Bmp
			: IsPcx (data) ? MediaFormat.Pcx
			: IsNetpbm (data) ? MediaFormat.Netpbm
			: TiffReader.IsTiff (data) ? TiffSize.HasDngVersion (data) ? MediaFormat.Dng : MediaFormat.Tiff
			: SvgSize.IsSvg (data) ? MediaFormat.Svg
			: MediaFormat.Unknown;
		return format != MediaFormat.Unknown;
	}

	public static bool TryRead (ReadOnlySpan<byte> data, MediaFormat format, out ImageProperties? properties)
	{
		properties = null;
		return format switch {
			MediaFormat.Jpeg => JpegSize.TryRead (data, out properties),
			MediaFormat.Png => TryPng (data, out properties),
			MediaFormat.Gif => TryGif (data, out properties),
			MediaFormat.Bmp => TryBmp (data, out properties),
			MediaFormat.Pcx => TryPcx (data, out properties),
			MediaFormat.Netpbm => TryNetpbm (data, out properties),
			MediaFormat.Tiff => TiffSize.TryRead (data, dng: false, out properties),
			MediaFormat.Dng => TiffSize.TryRead (data, dng: true, out properties),
			MediaFormat.Svg => SvgSize.TryRead (data, out properties),
			_ => false
		};
	}

	// 0x89 must be written as a byte. In a UTF-8 literal, \x89 is U+0089 and encodes as C2 89.
	static bool IsPng (ReadOnlySpan<byte> data) =>
		data.Length >= 8
		&& data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G'
		&& data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

	static bool TryPng (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		if (!IsPng (data) || data.Length < 29 || !data.Slice (12, 4).SequenceEqual ("IHDR"u8))
			return false;

		var width = Endian.I32 (data, 16, le: false);
		var height = Endian.I32 (data, 20, le: false);
		if (width <= 0 || height <= 0)
			return false;

		var bitDepth = data[24];
		var samples = data[25] switch {
			0 => 1,
			2 => 3,
			3 => 1,
			4 => 2,
			6 => 4,
			_ => 0
		};
		if (samples == 0)
			return false;

		properties = new ImageProperties (width, height, bitDepth * samples, "PNG");
		return true;
	}

	static bool IsGif (ReadOnlySpan<byte> data) =>
		data.Length >= 6 && (data.StartsWith ("GIF87a"u8) || data.StartsWith ("GIF89a"u8));

	static bool TryGif (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		if (!IsGif (data) || data.Length < 13)
			return false;

		var width = data[6] | (data[7] << 8);
		var height = data[8] | (data[9] << 8);
		if (width <= 0 || height <= 0)
			return false;

		var depth = (data[10] & 0x07) + 1;
		properties = new ImageProperties (width, height, depth, "GIF");
		return true;
	}

	static bool IsBmp (ReadOnlySpan<byte> data) =>
		data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M';

	static bool TryBmp (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		if (!IsBmp (data))
			return false;

		var header = Endian.I32 (data, 14, le: true);
		int width;
		int height;
		var depth = 0;
		if (header == 12) {
			if (data.Length < 26)
				return false;
			width = data[18] | (data[19] << 8);
			height = data[20] | (data[21] << 8);
			depth = data[24] | (data[25] << 8);
		} else if (header >= 16 && data.Length >= 26) {
			width = Endian.I32 (data, 18, le: true);
			var rawHeight = Endian.I32 (data, 22, le: true);
			if (rawHeight == int.MinValue)
				return false;
			height = rawHeight < 0 ? -rawHeight : rawHeight;
			if (data.Length >= 30)
				depth = data[28] | (data[29] << 8);
		} else {
			return false;
		}

		if (width <= 0 || height <= 0)
			return false;

		properties = new ImageProperties (width, height, depth, "BMP");
		return true;
	}

	static bool IsPcx (ReadOnlySpan<byte> data) =>
		data.Length >= 12 && data[0] == 0x0A && data[1] <= 5 && data[2] <= 1;

	static bool TryPcx (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		if (!IsPcx (data))
			return false;

		var xmin = data[4] | (data[5] << 8);
		var ymin = data[6] | (data[7] << 8);
		var xmax = data[8] | (data[9] << 8);
		var ymax = data[10] | (data[11] << 8);
		if (xmax < xmin || ymax < ymin)
			return false;

		var depth = (int)data[3];
		if (data.Length >= 66 && data[65] > 0)
			depth *= data[65];

		properties = new ImageProperties (xmax - xmin + 1, ymax - ymin + 1, depth, "PCX");
		return true;
	}

	static bool IsNetpbm (ReadOnlySpan<byte> data) =>
		data.Length >= 3 && data[0] == (byte)'P' && data[1] is >= (byte)'1' and <= (byte)'6' && IsSpace (data[2]);

	static bool TryNetpbm (ReadOnlySpan<byte> data, out ImageProperties? properties)
	{
		properties = null;
		if (!IsNetpbm (data))
			return false;

		var i = 2;
		if (!TryNetpbmInt (data, ref i, out var width) || !TryNetpbmInt (data, ref i, out var height))
			return false;
		if (width <= 0 || height <= 0)
			return false;

		var kind = data[1] switch {
			(byte)'1' or (byte)'4' => "PBM",
			(byte)'2' or (byte)'5' => "PGM",
			_ => "PPM"
		};
		properties = new ImageProperties (width, height, 0, kind);
		return true;
	}

	static bool TryNetpbmInt (ReadOnlySpan<byte> data, ref int i, out int value)
	{
		value = 0;
		while (i < data.Length) {
			if (data[i] == (byte)'#') {
				while (i < data.Length && data[i] != (byte)'\n')
					i++;
			} else if (IsSpace (data[i])) {
				i++;
			} else {
				break;
			}
		}

		if (i >= data.Length || data[i] is < (byte)'0' or > (byte)'9')
			return false;

		var acc = 0;
		while (i < data.Length && data[i] is >= (byte)'0' and <= (byte)'9') {
			var digit = data[i] - (byte)'0';
			if (acc > (1_000_000_000 - digit) / 10)
				return false;
			acc = (acc * 10) + digit;
			i++;
		}

		value = acc;
		return true;
	}

	static bool IsSpace (byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
