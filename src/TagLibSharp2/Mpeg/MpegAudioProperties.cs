// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mpeg;

/// <summary>
/// Represents audio properties of an MPEG audio file (MP3).
/// </summary>
/// <remarks>
/// <para>
/// For VBR files with Xing or VBRI headers, duration is calculated accurately
/// from the frame count. For CBR files or VBR files without headers, duration
/// is estimated from file size and bitrate.
/// </para>
/// </remarks>
public sealed class MpegAudioProperties : IMediaProperties
{
	/// <summary>
	/// Gets the duration of the audio.
	/// </summary>
	public TimeSpan Duration { get; }

	/// <summary>
	/// Gets the bitrate in kbps.
	/// </summary>
	/// <remarks>
	/// For VBR files, this is the average bitrate.
	/// </remarks>
	public int Bitrate { get; }

	/// <summary>
	/// Gets the sample rate in Hz.
	/// </summary>
	public int SampleRate { get; }

	/// <summary>
	/// Gets the number of channels.
	/// </summary>
	public int Channels { get; }

	/// <summary>
	/// Gets the bits per sample.
	/// </summary>
	/// <remarks>
	/// Returns 0 for MP3 as it's a lossy format where bit depth doesn't apply.
	/// </remarks>
	public int BitsPerSample => 0;

	/// <summary>
	/// Gets the codec name. Layer III is "MP3" for every MPEG version.
	/// Layer I and Layer II include the version (for example, "MPEG-2 Layer II").
	/// </summary>
	public string Codec => (Version, Layer) switch {
		(MpegVersion.Version1, MpegLayer.Layer1) => "MPEG-1 Layer I",
		(MpegVersion.Version1, MpegLayer.Layer2) => "MPEG-1 Layer II",
		(MpegVersion.Version2, MpegLayer.Layer1) => "MPEG-2 Layer I",
		(MpegVersion.Version2, MpegLayer.Layer2) => "MPEG-2 Layer II",
		(MpegVersion.Version25, MpegLayer.Layer1) => "MPEG-2.5 Layer I",
		(MpegVersion.Version25, MpegLayer.Layer2) => "MPEG-2.5 Layer II",
		(_, MpegLayer.Layer3) => "MP3",
		_ => $"{FormatVersion (Version)} {FormatLayer (Layer)}"
	};

	/// <summary>
	/// Gets a value indicating whether this is a variable bit rate file.
	/// </summary>
	public bool IsVbr { get; }

	/// <summary>
	/// Gets the MPEG version.
	/// </summary>
	public MpegVersion Version { get; }

	/// <summary>
	/// Gets the MPEG layer.
	/// </summary>
	public MpegLayer Layer { get; }

	/// <summary>
	/// Gets the total number of frames (if known from VBR header).
	/// </summary>
	public uint? FrameCount { get; }

	MpegAudioProperties (
		TimeSpan duration,
		int bitrate,
		int sampleRate,
		int channels,
		bool isVbr,
		MpegVersion version,
		MpegLayer layer,
		uint? frameCount)
	{
		Duration = duration;
		Bitrate = bitrate;
		SampleRate = sampleRate;
		Channels = channels;
		IsVbr = isVbr;
		Version = version;
		Layer = layer;
		FrameCount = frameCount;
	}

	/// <summary>
	/// Attempts to parse audio properties from MPEG data.
	/// </summary>
	/// <param name="data">The binary data to parse.</param>
	/// <param name="audioOffset">The offset where audio data starts (after ID3v2).</param>
	/// <param name="properties">The parsed properties, or null if parsing failed.</param>
	/// <returns>True if parsing succeeded, false otherwise.</returns>
	public static bool TryParse (BinaryData data, int audioOffset, out MpegAudioProperties? properties)
	{
		properties = null;

		// Find the first valid MPEG frame
		var frameOffset = FindFirstFrame (data, audioOffset);
		if (frameOffset < 0)
			return false;

		if (!MpegFrame.TryParse (data, frameOffset, out var frame) || frame is null)
			return false;

		// Determine channel count
		var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;

		// Try to find Xing header
		var xingOffset = frameOffset + frame.XingHeaderOffset;
		XingHeader? xingHeader = null;
		if (xingOffset + 8 <= data.Length)
			XingHeader.TryParse (data, xingOffset, out xingHeader);

		// Try to find VBRI header (if no Xing)
		VbriHeader? vbriHeader = null;
		if (xingHeader is null) {
			var vbriOffset = frameOffset + MpegFrame.VbriHeaderOffset;
			if (vbriOffset + VbriHeader.MinHeaderSize <= data.Length)
				VbriHeader.TryParse (data, vbriOffset, out vbriHeader);
		}

		// Calculate duration and bitrate
		TimeSpan duration;
		int bitrate;
		bool isVbr;
		uint? frameCount = null;

		var audioDataSize = data.Length - audioOffset;

		if (xingHeader?.FrameCount != null) {
			// VBR with Xing header - accurate duration
			frameCount = xingHeader.FrameCount;
			var totalSamples = (double)frameCount.Value * frame.SamplesPerFrame;
			duration = TimeSpan.FromSeconds (totalSamples / frame.SampleRate);
			isVbr = xingHeader.IsVbr;

			// Calculate average bitrate
			if (xingHeader.ByteCount != null && duration.TotalSeconds > 0)
				bitrate = (int)(xingHeader.ByteCount.Value * 8 / duration.TotalSeconds / 1000);
			else if (duration.TotalSeconds > 0)
				bitrate = (int)(audioDataSize * 8 / duration.TotalSeconds / 1000);
			else
				bitrate = frame.Bitrate;
		} else if (vbriHeader != null) {
			// VBR with VBRI header - accurate duration
			frameCount = vbriHeader.FrameCount;
			var totalSamples = (double)frameCount.Value * frame.SamplesPerFrame;
			duration = TimeSpan.FromSeconds (totalSamples / frame.SampleRate);
			isVbr = true;

			// Calculate average bitrate
			if (vbriHeader.ByteCount > 0 && duration.TotalSeconds > 0)
				bitrate = (int)(vbriHeader.ByteCount * 8 / duration.TotalSeconds / 1000);
			else
				bitrate = frame.Bitrate;
		} else {
			// CBR or VBR without header - estimate from file size
			isVbr = false;
			bitrate = frame.Bitrate;

			if (bitrate > 0)
				duration = TimeSpan.FromSeconds ((double)audioDataSize * 8 / (bitrate * 1000));
			else
				duration = TimeSpan.Zero;
		}

		properties = new MpegAudioProperties (
			duration,
			bitrate,
			frame.SampleRate,
			channels,
			isVbr,
			frame.Version,
			frame.Layer,
			frameCount);

		return true;
	}

	/// <summary>
	/// Scans for the first valid MPEG frame sync.
	/// </summary>
	static int FindFirstFrame (BinaryData data, int startOffset)
	{
		var span = data.Span;
		var searchLimit = Math.Min (startOffset + 8192, data.Length - 4);

		for (int i = startOffset; i < searchLimit; i++) {
			// Check for frame sync (0xFF + upper 3 bits of next byte)
			if (span[i] == 0xFF && (span[i + 1] & 0xE0) == 0xE0) {
				// Verify it's a valid frame
				if (MpegFrame.TryParse (data, i, out var frame) && frame is not null) {
					// Extra validation: check if next frame sync exists at expected position
					if (frame.FrameSize > 0 && i + frame.FrameSize < data.Length - 2) {
						if (span[i + frame.FrameSize] == 0xFF &&
							(span[i + frame.FrameSize + 1] & 0xE0) == 0xE0)
							return i;
					}
					// If we can't verify next frame, accept this one anyway
					return i;
				}
			}
		}

		return -1;
	}

	static string FormatVersion (MpegVersion version) => version switch {
		MpegVersion.Version1 => "MPEG-1",
		MpegVersion.Version2 => "MPEG-2",
		MpegVersion.Version25 => "MPEG-2.5",
		_ => "MPEG"
	};

	// Codec returns "MP3" for every Layer III file before this is called.
	static string FormatLayer (MpegLayer layer) => layer switch {
		MpegLayer.Layer1 => "Layer I",
		MpegLayer.Layer2 => "Layer II",
		_ => "Audio"
	};
}
