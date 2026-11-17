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
	public static bool TryParse (BinaryData data, int audioOffset, out MpegAudioProperties? properties) =>
		TryParse (data, audioOffset, data.Length, out properties, out _);

	/// <summary>
	/// Parses audio properties when <paramref name="fileLength"/> is the real file
	/// and <paramref name="data"/> is only the tag plus the first audio frames.
	/// <paramref name="needsEntireFile"/> is set when a Xing or VBRI byte count runs
	/// past the end of the file, so the frame count has to be walked from the audio
	/// that is actually present. The Xing byte count is the stream size. LAME writes
	/// the file size, which includes bytes before the first audio frame.
	/// </summary>
	internal static bool TryParse (
		BinaryData data,
		int audioOffset,
		long fileLength,
		out MpegAudioProperties? properties,
		out bool needsEntireFile)
	{
		properties = null;
		needsEntireFile = false;
		if (fileLength < data.Length)
			fileLength = data.Length;

		// Find the first valid MPEG frame
		var frameOffset = FindFirstFrame (data, audioOffset);
		if (frameOffset < 0)
			return false;

		if (!MpegFrame.TryParse (data, frameOffset, out var frame) || frame is null)
			return false;

		// Determine channel count
		var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;

		// Xing sits at header + side info. A set CRC bit adds two bytes to
		// XingHeaderOffset, but encoders still store the tag at the side-info offset.
		XingHeader? xingHeader = null;
		var sideInfoOffset = frame.XingHeaderOffset - (frame.HasCrc ? 2 : 0);
		if (!TryReadXing (data, frameOffset + sideInfoOffset, out xingHeader) && frame.HasCrc)
			TryReadXing (data, frameOffset + frame.XingHeaderOffset, out xingHeader);

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

		var audioDataSize = fileLength - audioOffset;
		var trustVbri = xingHeader is null && vbriHeader is not null
			&& DeclaredSizeFits (vbriHeader.ByteCount > 0 ? vbriHeader.ByteCount : null, fileLength);

		if (xingHeader is { FrameCount: uint xingFrames } && DeclaredSizeFits (xingHeader.ByteCount, fileLength)) {
			// VBR with Xing header - accurate duration
			frameCount = xingFrames;
			var totalSamples = (double)xingFrames * frame.SamplesPerFrame;
			duration = TimeSpan.FromSeconds (totalSamples / frame.SampleRate);
			isVbr = xingHeader.IsVbr;

			// Calculate average bitrate
			if (xingHeader.ByteCount != null && duration.TotalSeconds > 0)
				bitrate = (int)(xingHeader.ByteCount.Value * 8 / duration.TotalSeconds / 1000);
			else if (duration.TotalSeconds > 0)
				bitrate = (int)(audioDataSize * 8 / duration.TotalSeconds / 1000);
			else
				bitrate = frame.Bitrate;
		} else if (trustVbri && vbriHeader is not null) {
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
		} else if (xingHeader?.FrameCount != null || vbriHeader is not null) {
			// The bytes we hold are not the whole file. Counting frames here would
			// stop at the end of the probe instead of the end of the audio.
			if (fileLength > data.Length) {
				needsEntireFile = true;
				return false;
			}

			// The header names a byte count past the end of this buffer, so its frame
			// count describes a longer file. Count the frames that are actually here.
			var counted = CountFrames (data, frameOffset);
			isVbr = xingHeader?.IsVbr ?? true;
			bitrate = frame.Bitrate;
			if (counted > 0 && frame.SampleRate > 0) {
				frameCount = (uint)counted;
				duration = TimeSpan.FromSeconds (counted * (double)frame.SamplesPerFrame / frame.SampleRate);
			} else {
				duration = TimeSpan.Zero;
			}
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
			if (span[i] != 0xFF || (span[i + 1] & 0xE0) != 0xE0)
				continue;

			if (!MpegFrame.TryParse (data, i, out var frame) || frame is null)
				continue;

			// Free bitrate has no frame size, so the next header cannot be checked.
			if (frame.FrameSize <= 0)
				continue;

			var next = i + frame.FrameSize;
			// A header alone, or a frame that ends too close to the buffer to hold
			// another header, cannot be confirmed. Accept it.
			if (next > data.Length || next + MpegFrame.HeaderSize > data.Length)
				return i;

			if (MpegFrame.TryParse (data, next, out var nextFrame) && nextFrame is not null)
				return i;
		}

		return -1;
	}

	static bool TryReadXing (BinaryData data, int offset, out XingHeader? header)
	{
		header = null;
		if (offset < 0 || offset + 8 > data.Length)
			return false;
		return XingHeader.TryParse (data, offset, out header);
	}

	/// <summary>
	/// A missing byte count makes no size claim. A count past the bytes after the
	/// first frame belongs to a longer file than the one in hand.
	/// </summary>
	static bool DeclaredSizeFits (uint? byteCount, long fileLength)
	{
		if (byteCount is null)
			return true;
		return fileLength >= 0 && byteCount.Value <= fileLength;
	}

	static int CountFrames (BinaryData data, int firstOffset)
	{
		// A header-sized step can land on a false sync inside the first frame and stop.
		// The run that actually continues is the audio. The opening frame still counts
		// when that run starts later. A chain already walked is skipped: a suffix of it
		// is shorter, so each frame is parsed once.
		var fromFirst = WalkFrames (data, firstOffset, out var fromFirstEnd);
		var best = fromFirst;
		var span = data.Span;
		var last = data.Length - MpegFrame.HeaderSize;
		var i = firstOffset + 1;
		while (i <= last) {
			if (fromFirst > 0 && i < fromFirstEnd) {
				i = fromFirstEnd;
				continue;
			}

			if (span[i] != 0xFF || (span[i + 1] & 0xE0) != 0xE0) {
				i++;
				continue;
			}

			var count = WalkFrames (data, i, out var end);
			if (count > best)
				best = count;
			if (count > 0 && end > i)
				i = end;
			else
				i++;
		}
		if (best > fromFirst)
			best++;
		return best;
	}

	static int WalkFrames (BinaryData data, int offset, out int end)
	{
		end = offset;
		var count = 0;
		while (offset >= 0 && offset + MpegFrame.HeaderSize <= data.Length) {
			if (!MpegFrame.TryParse (data, offset, out var frame) || frame is null || frame.FrameSize <= 0)
				break;
			if (frame.FrameSize > data.Length - offset)
				break;

			var next = offset + frame.FrameSize;
			var nextFits = next + MpegFrame.HeaderSize <= data.Length;
			var nextParses = nextFits
				&& MpegFrame.TryParse (data, next, out var nextFrame)
				&& nextFrame is not null;
			if (!nextParses) {
				// The last frame of a run has nothing after it. A lone unconfirmed header does not.
				if (count > 0 || !nextFits) {
					count++;
					end = next;
				}
				break;
			}

			count++;
			offset = next;
			end = offset;
		}
		return count;
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
