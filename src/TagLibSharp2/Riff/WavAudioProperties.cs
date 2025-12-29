// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// SubFormat types for WAVEFORMATEXTENSIBLE.
/// </summary>
public enum WavSubFormat
{
	/// <summary>Unknown or unrecognized sub-format.</summary>
	Unknown = 0,
	/// <summary>PCM (uncompressed) audio.</summary>
	Pcm = 1,
	/// <summary>IEEE floating-point audio.</summary>
	IeeeFloat = 3,
	/// <summary>A-law compressed audio.</summary>
	ALaw = 6,
	/// <summary>mu-law compressed audio.</summary>
	MuLaw = 7
}

/// <summary>
/// Speaker position flags for WAVEFORMATEXTENSIBLE dwChannelMask.
/// </summary>
public static class WavChannelMask
{
	/// <summary>Front left speaker.</summary>
	public const uint FrontLeft = 0x1;
	/// <summary>Front right speaker.</summary>
	public const uint FrontRight = 0x2;
	/// <summary>Front center speaker.</summary>
	public const uint FrontCenter = 0x4;
	/// <summary>Low frequency effects (subwoofer).</summary>
	public const uint LowFrequency = 0x8;
	/// <summary>Back left speaker.</summary>
	public const uint BackLeft = 0x10;
	/// <summary>Back right speaker.</summary>
	public const uint BackRight = 0x20;
	/// <summary>Front left of center speaker.</summary>
	public const uint FrontLeftOfCenter = 0x40;
	/// <summary>Front right of center speaker.</summary>
	public const uint FrontRightOfCenter = 0x80;
	/// <summary>Back center speaker.</summary>
	public const uint BackCenter = 0x100;
	/// <summary>Side left speaker.</summary>
	public const uint SideLeft = 0x200;
	/// <summary>Side right speaker.</summary>
	public const uint SideRight = 0x400;
	/// <summary>Top center speaker.</summary>
	public const uint TopCenter = 0x800;
	/// <summary>Top front left speaker.</summary>
	public const uint TopFrontLeft = 0x1000;
	/// <summary>Top front center speaker.</summary>
	public const uint TopFrontCenter = 0x2000;
	/// <summary>Top front right speaker.</summary>
	public const uint TopFrontRight = 0x4000;
	/// <summary>Top back left speaker.</summary>
	public const uint TopBackLeft = 0x8000;
	/// <summary>Top back center speaker.</summary>
	public const uint TopBackCenter = 0x10000;
	/// <summary>Top back right speaker.</summary>
	public const uint TopBackRight = 0x20000;
}

/// <summary>
/// Extended audio properties from WAVEFORMATEXTENSIBLE fmt chunk.
/// </summary>
public readonly struct WavExtendedProperties : IEquatable<WavExtendedProperties>
{
	/// <summary>Number of channels.</summary>
	public int Channels { get; }
	/// <summary>Sample rate in Hz.</summary>
	public int SampleRate { get; }
	/// <summary>Bits per sample (container size).</summary>
	public int BitsPerSample { get; }
	/// <summary>Valid bits of precision in the signal.</summary>
	public int ValidBitsPerSample { get; }
	/// <summary>Speaker position bitmask.</summary>
	public uint ChannelMask { get; }
	/// <summary>The actual audio sub-format.</summary>
	public WavSubFormat SubFormat { get; }

	/// <summary>
	/// Creates a new WavExtendedProperties instance.
	/// </summary>
	public WavExtendedProperties (
		int channels,
		int sampleRate,
		int bitsPerSample,
		int validBitsPerSample,
		uint channelMask,
		WavSubFormat subFormat)
	{
		Channels = channels;
		SampleRate = sampleRate;
		BitsPerSample = bitsPerSample;
		ValidBitsPerSample = validBitsPerSample;
		ChannelMask = channelMask;
		SubFormat = subFormat;
	}

	/// <inheritdoc />
	public bool Equals (WavExtendedProperties other) =>
		Channels == other.Channels &&
		SampleRate == other.SampleRate &&
		BitsPerSample == other.BitsPerSample &&
		ValidBitsPerSample == other.ValidBitsPerSample &&
		ChannelMask == other.ChannelMask &&
		SubFormat == other.SubFormat;

	/// <inheritdoc />
	public override bool Equals (object? obj) =>
		obj is WavExtendedProperties other && Equals (other);

	/// <inheritdoc />
	public override int GetHashCode () =>
		HashCode.Combine (Channels, SampleRate, BitsPerSample, ValidBitsPerSample, ChannelMask, SubFormat);

	/// <summary>Determines whether two instances are equal.</summary>
	public static bool operator == (WavExtendedProperties left, WavExtendedProperties right) =>
		left.Equals (right);

	/// <summary>Determines whether two instances are not equal.</summary>
	public static bool operator != (WavExtendedProperties left, WavExtendedProperties right) =>
		!left.Equals (right);
}

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

	/// <summary>
	/// Parses extended properties from a WAVEFORMATEXTENSIBLE fmt chunk.
	/// </summary>
	/// <param name="fmtData">The fmt chunk data (excluding chunk header).</param>
	/// <returns>The extended properties, or null if not extensible format or invalid.</returns>
	/// <remarks>
	/// WAVEFORMATEXTENSIBLE structure (40 bytes minimum):
	/// - Bytes 0-17:  WAVEFORMATEX base structure
	/// - Bytes 18-19: wValidBitsPerSample
	/// - Bytes 20-23: dwChannelMask
	/// - Bytes 24-39: SubFormat GUID (16 bytes)
	///
	/// Well-known SubFormat GUIDs use format: {formatTag, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71}
	/// </remarks>
	public static WavExtendedProperties? ParseExtended (BinaryData fmtData)
	{
		// Need at least 40 bytes for WAVEFORMATEXTENSIBLE
		if (fmtData.Length < 40)
			return null;

		var formatCode = fmtData.ToUInt16LE (0);

		// Must be extensible format
		if (formatCode != FormatExtensible)
			return null;

		var channels = fmtData.ToUInt16LE (2);
		var sampleRate = (int)fmtData.ToUInt32LE (4);
		var bitsPerSample = fmtData.ToUInt16LE (14);
		var validBitsPerSample = fmtData.ToUInt16LE (18);
		var channelMask = fmtData.ToUInt32LE (20);

		// Parse SubFormat GUID - first two bytes are the format tag
		var subFormatTag = fmtData.ToUInt16LE (24);
		var subFormat = subFormatTag switch {
			1 => WavSubFormat.Pcm,
			3 => WavSubFormat.IeeeFloat,
			6 => WavSubFormat.ALaw,
			7 => WavSubFormat.MuLaw,
			_ => WavSubFormat.Unknown
		};

		return new WavExtendedProperties (
			channels,
			sampleRate,
			bitsPerSample,
			validBitsPerSample,
			channelMask,
			subFormat);
	}
}
