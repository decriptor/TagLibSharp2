// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// Parses audio properties from a WAV file's fmt chunk.
/// </summary>
/// <remarks>
/// WAV fmt chunk structure (PCM format, 16 bytes minimum):
/// - Bytes 0-1:   Audio format (1=PCM, 3=IEEE float, etc.)
/// - Bytes 2-3:   Number of channels
/// - Bytes 4-7:   Sample rate (Hz)
/// - Bytes 8-11:  Byte rate (sample rate * channels * bits/8)
/// - Bytes 12-13: Block align (channels * bits/8)
/// - Bytes 14-15: Bits per sample
///
/// Extended format (18+ bytes):
/// - Bytes 16-17: Extension size
/// - Bytes 18+:   Format-specific extension data
/// </remarks>
public static class WavAudioPropertiesParser
{
	/// <summary>
	/// Audio format code for PCM (uncompressed).
	/// </summary>
	public const ushort FormatPcm = 1;

	/// <summary>
	/// Audio format code for IEEE float.
	/// </summary>
	public const ushort FormatIeeeFloat = 3;

	/// <summary>
	/// Audio format code for extensible format.
	/// </summary>
	public const ushort FormatExtensible = 0xFFFE;

	/// <summary>
	/// Gets a human-readable description of the audio format code.
	/// </summary>
	public static string GetFormatDescription (ushort formatCode) => formatCode switch {
		FormatPcm => "PCM",
		FormatIeeeFloat => "IEEE Float",
		FormatExtensible => "Extensible",
		6 => "A-Law",
		7 => "mu-Law",
		_ => $"WAV ({formatCode})"
	};

	/// <summary>
	/// Parses audio properties from a fmt chunk and optional data chunk size.
	/// </summary>
	/// <param name="fmtData">The fmt chunk data (excluding chunk header).</param>
	/// <param name="dataChunkSize">The size of the data chunk, or -1 if unknown.</param>
	/// <returns>The parsed audio properties, or null if invalid.</returns>
	public static AudioProperties? Parse (BinaryData fmtData, long dataChunkSize = -1)
	{
		if (fmtData.Length < 16)
			return null;

		var formatCode = fmtData.ToUInt16LE (0);
		var channels = fmtData.ToUInt16LE (2);
		var sampleRate = (int)fmtData.ToUInt32LE (4);
		var byteRate = (int)fmtData.ToUInt32LE (8);
		// Block align at offset 12, not needed for basic properties
		var bitsPerSample = fmtData.ToUInt16LE (14);

		// Validate basic sanity
		if (channels == 0 || sampleRate == 0)
			return null;

		// Calculate duration if we know the data size
		var duration = TimeSpan.Zero;
		if (dataChunkSize > 0 && byteRate > 0)
			duration = TimeSpan.FromSeconds ((double)dataChunkSize / byteRate);

		// Calculate bitrate in kbps
		var bitrate = byteRate * 8 / 1000;

		var codec = GetFormatDescription (formatCode);

		return new AudioProperties (
			duration,
			bitrate,
			sampleRate,
			bitsPerSample,
			channels,
			codec);
	}
}
