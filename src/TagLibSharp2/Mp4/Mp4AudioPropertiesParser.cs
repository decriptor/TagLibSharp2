// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Parses audio properties from MP4/M4A files.
/// </summary>
/// <remarks>
/// <para>
/// Extracts duration, sample rate, channel count, bit depth, bitrate,
/// and codec information from the MP4 container structure.
/// </para>
/// <para>
/// Navigation paths:
/// - Duration: moov → mvhd (movie header) or moov → trak → mdia → mdhd (media header)
/// - Sample rate: moov → trak → mdia → minf → stbl → stsd → mp4a/alac → codec-specific box
/// - Channels: AudioSampleEntry or codec-specific configuration
/// - Bitrate: esds (AAC), alac magic cookie, or calculated from sample table
/// </para>
/// <para>
/// Reference: ISO/IEC 14496-12 (ISO Base Media File Format), ISO/IEC 14496-14 (MP4 File Format)
/// </para>
/// </remarks>
public static class Mp4AudioPropertiesParser
{
	/// <summary>
	/// Parses audio properties from MP4 file data.
	/// </summary>
	/// <param name="data">The complete MP4 file data.</param>
	/// <returns>Audio properties, or AudioProperties.Empty if parsing failed.</returns>
	public static AudioProperties Parse(ReadOnlySpan<byte> data)
	{
		// Find moov box
		var moovBox = FindBox(data, "moov");
		if (moovBox.IsEmpty)
			return AudioProperties.Empty;

		// Extract duration from mvhd
		var mvhdBox = FindBox(moovBox, "mvhd");
		var duration = TimeSpan.Zero;
		if (!mvhdBox.IsEmpty)
		{
			duration = ParseMvhdDuration(mvhdBox);
		}

		// Find first audio track
		var audioTrack = FindFirstAudioTrack(moovBox);
		if (audioTrack.IsEmpty)
			return AudioProperties.Empty;

		// Parse media header for more accurate duration and timescale
		var mdiaBox = FindBox(audioTrack, "mdia");
		if (!mdiaBox.IsEmpty)
		{
			var mdhdBox = FindBox(mdiaBox, "mdhd");
			if (!mdhdBox.IsEmpty)
			{
				var trackDuration = ParseMdhdDuration(mdhdBox);
				if (trackDuration > TimeSpan.Zero)
					duration = trackDuration;
			}
		}

		// Navigate to sample description
		var minfBox = FindBox(mdiaBox, "minf");
		var stblBox = FindBox(minfBox, "stbl");
		var stsdBox = FindBox(stblBox, "stsd");

		if (stsdBox.IsEmpty)
			return new AudioProperties(duration, 0, 0, 0, 0, null);

		// Parse sample description to get codec and properties
		return ParseSampleDescription(stsdBox, duration);
	}

	/// <summary>
	/// Finds a box with the specified 4-character code within parent data.
	/// </summary>
	/// <param name="data">The parent box data to search within.</param>
	/// <param name="fourCC">The 4-character box code (e.g., "moov", "trak").</param>
	/// <returns>The box data (excluding header), or empty if not found.</returns>
	private static ReadOnlySpan<byte> FindBox(ReadOnlySpan<byte> data, string fourCC)
	{
		if (data.Length < 8 || fourCC.Length != 4)
			return ReadOnlySpan<byte>.Empty;

		var targetBytes = System.Text.Encoding.ASCII.GetBytes(fourCC);
		var offset = 0;

		while (offset + 8 <= data.Length)
		{
			// Read box size (4 bytes, big-endian)
			var boxSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

			// Read box type (4 bytes)
			var boxType = data.Slice(offset + 4, 4);

			// Check if this is the box we're looking for
			if (boxType.SequenceEqual(targetBytes))
			{
				// Handle extended size
				var headerSize = 8;
				if (boxSize == 1)
				{
					if (offset + 16 > data.Length)
						return ReadOnlySpan<byte>.Empty;
					boxSize = (int)BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset + 8, 8));
					headerSize = 16;
				}

				// Return box content (after header)
				var contentStart = offset + headerSize;
				var contentSize = boxSize - headerSize;
				if (contentStart + contentSize > data.Length)
					return ReadOnlySpan<byte>.Empty;

				return data.Slice(contentStart, contentSize);
			}

			// Move to next box
			if (boxSize <= 0 || boxSize < 8)
				break;

			offset += boxSize;
		}

		return ReadOnlySpan<byte>.Empty;
	}

	/// <summary>
	/// Finds the first audio track in the moov box.
	/// </summary>
	/// <param name="moovData">The moov box data.</param>
	/// <returns>The first audio trak box data, or empty if not found.</returns>
	private static ReadOnlySpan<byte> FindFirstAudioTrack(ReadOnlySpan<byte> moovData)
	{
		var offset = 0;

		// Iterate through all trak boxes
		while (offset + 8 <= moovData.Length)
		{
			var boxSize = (int)BinaryPrimitives.ReadUInt32BigEndian(moovData.Slice(offset, 4));
			var boxType = moovData.Slice(offset + 4, 4);

			if (boxSize <= 0 || boxSize < 8)
				break;

			// Check if this is a trak box
			if (boxType.SequenceEqual(System.Text.Encoding.ASCII.GetBytes("trak")))
			{
				var headerSize = 8;
				if (boxSize == 1)
				{
					if (offset + 16 > moovData.Length)
						break;
					boxSize = (int)BinaryPrimitives.ReadUInt64BigEndian(moovData.Slice(offset + 8, 8));
					headerSize = 16;
				}

				var trakData = moovData.Slice(offset + headerSize, boxSize - headerSize);

				// Check if this is an audio track by looking for hdlr with 'soun'
				var mdiaBox = FindBox(trakData, "mdia");
				if (!mdiaBox.IsEmpty)
				{
					var hdlrBox = FindBox(mdiaBox, "hdlr");
					if (!hdlrBox.IsEmpty && hdlrBox.Length >= 12)
					{
						// hdlr format: version(1) + flags(3) + pre_defined(4) + handler_type(4)
						var handlerType = hdlrBox.Slice(8, 4);
						if (handlerType.SequenceEqual(System.Text.Encoding.ASCII.GetBytes("soun")))
						{
							return trakData;
						}
					}
				}
			}

			offset += boxSize;
		}

		return ReadOnlySpan<byte>.Empty;
	}

	/// <summary>
	/// Parses duration from mvhd (movie header) box.
	/// </summary>
	/// <param name="mvhdData">The mvhd box data (after FullBox header).</param>
	/// <returns>The duration, or TimeSpan.Zero if parsing failed.</returns>
	private static TimeSpan ParseMvhdDuration(ReadOnlySpan<byte> mvhdData)
	{
		if (mvhdData.Length < 4)
			return TimeSpan.Zero;

		// FullBox: version(1) + flags(3)
		var version = mvhdData[0];

		if (version == 0)
		{
			// Version 0: creation_time(4) + modification_time(4) + timescale(4) + duration(4)
			if (mvhdData.Length < 20)
				return TimeSpan.Zero;

			var timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhdData.Slice(12, 4));
			var duration = BinaryPrimitives.ReadUInt32BigEndian(mvhdData.Slice(16, 4));

			if (timescale == 0)
				return TimeSpan.Zero;

			return TimeSpan.FromSeconds((double)duration / timescale);
		}
		else if (version == 1)
		{
			// Version 1: creation_time(8) + modification_time(8) + timescale(4) + duration(8)
			if (mvhdData.Length < 32)
				return TimeSpan.Zero;

			var timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhdData.Slice(20, 4));
			var duration = BinaryPrimitives.ReadUInt64BigEndian(mvhdData.Slice(24, 8));

			if (timescale == 0)
				return TimeSpan.Zero;

			return TimeSpan.FromSeconds((double)duration / timescale);
		}

		return TimeSpan.Zero;
	}

	/// <summary>
	/// Parses duration from mdhd (media header) box.
	/// </summary>
	/// <param name="mdhdData">The mdhd box data (after FullBox header).</param>
	/// <returns>The duration, or TimeSpan.Zero if parsing failed.</returns>
	private static TimeSpan ParseMdhdDuration(ReadOnlySpan<byte> mdhdData)
	{
		if (mdhdData.Length < 4)
			return TimeSpan.Zero;

		var version = mdhdData[0];

		if (version == 0)
		{
			if (mdhdData.Length < 24)
				return TimeSpan.Zero;

			var timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhdData.Slice(12, 4));
			var duration = BinaryPrimitives.ReadUInt32BigEndian(mdhdData.Slice(16, 4));

			if (timescale == 0)
				return TimeSpan.Zero;

			return TimeSpan.FromSeconds((double)duration / timescale);
		}
		else if (version == 1)
		{
			if (mdhdData.Length < 36)
				return TimeSpan.Zero;

			var timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhdData.Slice(20, 4));
			var duration = BinaryPrimitives.ReadUInt64BigEndian(mdhdData.Slice(24, 8));

			if (timescale == 0)
				return TimeSpan.Zero;

			return TimeSpan.FromSeconds((double)duration / timescale);
		}

		return TimeSpan.Zero;
	}

	/// <summary>
	/// Parses the sample description box to extract codec and audio properties.
	/// </summary>
	/// <param name="stsdData">The stsd box data.</param>
	/// <param name="duration">The duration from mvhd/mdhd.</param>
	/// <returns>The parsed audio properties.</returns>
	private static AudioProperties ParseSampleDescription(ReadOnlySpan<byte> stsdData, TimeSpan duration)
	{
		if (stsdData.Length < 16)
			return new AudioProperties(duration, 0, 0, 0, 0, null);

		// stsd: version(1) + flags(3) + entry_count(4)
		var entryCount = BinaryPrimitives.ReadUInt32BigEndian(stsdData.Slice(4, 4));
		if (entryCount == 0)
			return new AudioProperties(duration, 0, 0, 0, 0, null);

		var offset = 8;

		// Read first sample entry
		if (offset + 8 > stsdData.Length)
			return new AudioProperties(duration, 0, 0, 0, 0, null);

		var entrySize = (int)BinaryPrimitives.ReadUInt32BigEndian(stsdData.Slice(offset, 4));
		var entryType = stsdData.Slice(offset + 4, 4);

		if (offset + entrySize > stsdData.Length)
			return new AudioProperties(duration, 0, 0, 0, 0, null);

		var entryData = stsdData.Slice(offset + 8, entrySize - 8);

		// Parse AudioSampleEntry
		// Format: reserved(6) + data_reference_index(2) + reserved(8) + channelcount(2) + samplesize(2) + reserved(4) + samplerate(4)
		if (entryData.Length < 28)
			return new AudioProperties(duration, 0, 0, 0, 0, null);

		var channels = BinaryPrimitives.ReadUInt16BigEndian(entryData.Slice(16, 2));
		var sampleSize = BinaryPrimitives.ReadUInt16BigEndian(entryData.Slice(18, 2));
		var sampleRateFixed = BinaryPrimitives.ReadUInt32BigEndian(entryData.Slice(24, 4));
		var sampleRateFromEntry = (int)(sampleRateFixed >> 16); // 16.16 fixed-point

		// Determine codec type and parse codec-specific configuration
		var codec = System.Text.Encoding.ASCII.GetString(entryType);
		var codecSpecificData = entryData.Slice(28);

		if (codec == "mp4a")
		{
			// AAC - look for esds box
			return ParseMp4aEntry(codecSpecificData, duration, channels, sampleSize, sampleRateFromEntry);
		}
		else if (codec == "alac")
		{
			// ALAC - look for alac magic cookie
			return ParseAlacEntry(codecSpecificData, duration, channels, sampleSize, sampleRateFromEntry);
		}
		else
		{
			// Unknown codec, use basic info
			return new AudioProperties(
				duration,
				0, // No bitrate available
				sampleRateFromEntry,
				sampleSize,
				channels,
				codec);
		}
	}

	/// <summary>
	/// Parses mp4a (AAC) sample entry with esds box.
	/// </summary>
	private static AudioProperties ParseMp4aEntry(
		ReadOnlySpan<byte> data,
		TimeSpan duration,
		int channelsFromEntry,
		int sampleSizeFromEntry,
		int sampleRateFromEntry)
	{
		// Find esds box within mp4a entry
		var esdsBox = FindBox(data, "esds");
		if (esdsBox.IsEmpty)
		{
			return new AudioProperties(duration, 0, sampleRateFromEntry, sampleSizeFromEntry, channelsFromEntry, "AAC");
		}

		// Parse esds (skip FullBox version/flags - 4 bytes)
		if (esdsBox.Length < 4)
			return new AudioProperties(duration, 0, sampleRateFromEntry, sampleSizeFromEntry, channelsFromEntry, "AAC");

		var esdsConfig = EsdsParser.Parse(esdsBox.Slice(4));
		if (esdsConfig is null)
			return new AudioProperties(duration, 0, sampleRateFromEntry, sampleSizeFromEntry, channelsFromEntry, "AAC");

		var config = esdsConfig.Value;

		// Prefer codec-specific values over AudioSampleEntry values
		var sampleRate = config.SampleRate > 0 ? config.SampleRate : sampleRateFromEntry;
		var channels = config.Channels > 0 ? config.Channels : channelsFromEntry;
		var bitrate = config.AvgBitrate > 0 ? (int)(config.AvgBitrate / 1000) : 0;

		// Determine AAC sub-type from objectTypeIndication
		var codecName = config.ObjectTypeIndication switch
		{
			0x40 => "AAC-LC",
			0x66 => "AAC Main",
			0x67 => "AAC-LC",
			0x68 => "AAC-SSR",
			0x69 => "AAC-LTP",
			0x6B => "MP3",
			_ => "AAC"
		};

		return new AudioProperties(duration, bitrate, sampleRate, 0, channels, codecName);
	}

	/// <summary>
	/// Parses alac sample entry with magic cookie.
	/// </summary>
	private static AudioProperties ParseAlacEntry(
		ReadOnlySpan<byte> data,
		TimeSpan duration,
		int channelsFromEntry,
		int sampleSizeFromEntry,
		int sampleRateFromEntry)
	{
		// Find alac box within alac entry (yes, nested 'alac' boxes)
		var alacBox = FindBox(data, "alac");
		if (alacBox.IsEmpty)
			return new AudioProperties(duration, 0, sampleRateFromEntry, sampleSizeFromEntry, channelsFromEntry, "ALAC");

		var alacConfig = AlacMagicCookie.Parse(alacBox);
		if (alacConfig is null)
			return new AudioProperties(duration, 0, sampleRateFromEntry, sampleSizeFromEntry, channelsFromEntry, "ALAC");

		var config = alacConfig.Value;
		var bitrate = config.AvgBitrate > 0 ? (int)(config.AvgBitrate / 1000) : 0;

		// Prefer magic cookie values, fall back to AudioSampleEntry values
		var sampleRate = config.SampleRate > 0 ? config.SampleRate : sampleRateFromEntry;
		var channels = config.Channels > 0 ? config.Channels : channelsFromEntry;
		var sampleSize = config.SampleSize > 0 ? config.SampleSize : sampleSizeFromEntry;

		return new AudioProperties(
			duration,
			bitrate,
			sampleRate,
			sampleSize,
			channels,
			"ALAC");
	}
}
