// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 CHAP (Chapter) frame.
/// </summary>
/// <remarks>
/// <para>
/// CHAP frames define chapters within audio content, commonly used in podcasts and audiobooks.
/// Each chapter has a unique element ID, time range, and optional embedded frames for metadata.
/// </para>
/// <para>
/// Frame format:
/// </para>
/// <code>
/// Element ID (null-terminated Latin-1 string)
/// Start time in milliseconds (4 bytes, big-endian)
/// End time in milliseconds (4 bytes, big-endian)
/// Start byte offset (4 bytes, big-endian, 0xFFFFFFFF if not used)
/// End byte offset (4 bytes, big-endian, 0xFFFFFFFF if not used)
/// Optional embedded sub-frames (TIT2 for title, etc.)
/// </code>
/// </remarks>
public sealed class ChapterFrame
{
	const uint UnusedOffset = 0xFFFFFFFF;

	/// <summary>
	/// Gets the frame ID (always "CHAP").
	/// </summary>
	public static string FrameId => "CHAP";

	/// <summary>
	/// Gets or sets the unique element ID for this chapter.
	/// </summary>
	/// <remarks>
	/// This ID is used by CTOC frames to reference this chapter.
	/// Should be unique within the tag.
	/// </remarks>
	public string ElementId { get; set; }

	/// <summary>
	/// Gets or sets the chapter start time in milliseconds.
	/// </summary>
	public uint StartTimeMs { get; set; }

	/// <summary>
	/// Gets or sets the chapter end time in milliseconds.
	/// </summary>
	public uint EndTimeMs { get; set; }

	/// <summary>
	/// Gets or sets the chapter start byte offset.
	/// </summary>
	/// <remarks>
	/// Set to 0xFFFFFFFF if byte offsets are not used (common).
	/// </remarks>
	public uint StartByteOffset { get; set; }

	/// <summary>
	/// Gets or sets the chapter end byte offset.
	/// </summary>
	/// <remarks>
	/// Set to 0xFFFFFFFF if byte offsets are not used (common).
	/// </remarks>
	public uint EndByteOffset { get; set; }

	/// <summary>
	/// Gets or sets the chapter title (from embedded TIT2 frame).
	/// </summary>
	public string? Title { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="ChapterFrame"/> class.
	/// </summary>
	/// <param name="elementId">The unique element ID.</param>
	/// <param name="startTimeMs">The start time in milliseconds.</param>
	/// <param name="endTimeMs">The end time in milliseconds.</param>
	public ChapterFrame (string elementId, uint startTimeMs, uint endTimeMs)
	{
		ElementId = elementId;
		StartTimeMs = startTimeMs;
		EndTimeMs = endTimeMs;
		StartByteOffset = UnusedOffset;
		EndByteOffset = UnusedOffset;
	}

	/// <summary>
	/// Attempts to read a CHAP frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static ChapterFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		// Minimum size: element ID (1 + null) + times (16) = 18 bytes minimum
		if (data.Length < 18)
			return ChapterFrameReadResult.Failure ("CHAP frame data is too short");

		// Read element ID (null-terminated)
		var nullIndex = data.IndexOf ((byte)0);
		if (nullIndex < 0 || nullIndex == 0)
			return ChapterFrameReadResult.Failure ("CHAP frame missing element ID");

		var elementId = Polyfills.Latin1.GetString (data.Slice (0, nullIndex));
		var offset = nullIndex + 1;

		// Need at least 16 more bytes for time/offset fields
		if (offset + 16 > data.Length)
			return ChapterFrameReadResult.Failure ("CHAP frame missing time data");

		// Start time (4 bytes, big-endian)
		var startTime = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// End time (4 bytes, big-endian)
		var endTime = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// Start byte offset (4 bytes, big-endian)
		var startOffset = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// End byte offset (4 bytes, big-endian)
		var endOffset = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		var frame = new ChapterFrame (elementId, startTime, endTime) {
			StartByteOffset = startOffset,
			EndByteOffset = endOffset
		};

		// Parse embedded sub-frames if present
		while (offset + 10 <= data.Length) // Frame header is 10 bytes
		{
			// Read frame ID
			var frameIdBytes = data.Slice (offset, 4);
			if (frameIdBytes[0] == 0)
				break; // End of frames

			// Check if valid frame ID (A-Z, 0-9)
			var isValidFrame = true;
			for (var i = 0; i < 4; i++) {
				var c = frameIdBytes[i];
				if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) {
					isValidFrame = false;
					break;
				}
			}

			if (!isValidFrame)
				break;

			var subFrameId = System.Text.Encoding.ASCII.GetString (frameIdBytes);
			var frameSize = GetFrameSize (data.Slice (offset + 4, 4), version);

			if (frameSize <= 0 || offset + 10 + frameSize > data.Length)
				break;

			var subFrameData = data.Slice (offset + 10, frameSize);
			offset += 10 + frameSize;

			// Handle known sub-frames
			if (subFrameId == "TIT2") {
				var titleResult = TextFrame.Read (subFrameId, subFrameData, version);
				if (titleResult.IsSuccess)
					frame.Title = titleResult.Frame!.Text;
			}
			// Other sub-frames could be parsed here (TIT3, WXXX, APIC, etc.)
		}

		return ChapterFrameReadResult.Success (frame, data.Length);
	}

	/// <summary>
	/// Renders the frame content to binary data.
	/// </summary>
	/// <returns>The frame content.</returns>
	public BinaryData RenderContent ()
	{
		// Render embedded TIT2 if title is set
		BinaryData? titleFrameData = null;
		if (!string.IsNullOrEmpty (Title)) {
			var titleFrame = new TextFrame ("TIT2", Title!, TextEncodingType.Utf8);
			titleFrameData = titleFrame.RenderContent ();
		}

		var elementIdBytes = Polyfills.Latin1.GetBytes (ElementId);
		var baseSize = elementIdBytes.Length + 1 + 16; // +1 for null, +16 for times/offsets
		var totalSize = baseSize;

		if (titleFrameData.HasValue)
			totalSize += 10 + titleFrameData.Value.Length; // 10 for frame header

		using var builder = new BinaryDataBuilder (totalSize);

		// Element ID (null-terminated)
		builder.Add (elementIdBytes);
		builder.Add ((byte)0x00);

		// Start time (4 bytes, big-endian)
		AddUInt32BE (builder, StartTimeMs);

		// End time (4 bytes, big-endian)
		AddUInt32BE (builder, EndTimeMs);

		// Start byte offset (4 bytes, big-endian)
		AddUInt32BE (builder, StartByteOffset);

		// End byte offset (4 bytes, big-endian)
		AddUInt32BE (builder, EndByteOffset);

		// Embedded TIT2 frame
		if (titleFrameData.HasValue) {
			var tfd = titleFrameData.Value;
			// Frame header: ID (4) + Size (4) + Flags (2)
			builder.Add (System.Text.Encoding.ASCII.GetBytes ("TIT2"));
			AddSyncSafeUInt32 (builder, (uint)tfd.Length);
			builder.Add ((byte)0x00);
			builder.Add ((byte)0x00);
			builder.Add (tfd);
		}

		return builder.ToBinaryData ();
	}

	static uint ReadUInt32BE (ReadOnlySpan<byte> data)
	{
		return ((uint)data[0] << 24) |
			   ((uint)data[1] << 16) |
			   ((uint)data[2] << 8) |
			   (uint)data[3];
	}

	static int GetFrameSize (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		if (version == Id3v2Version.V24) {
			// Syncsafe integer
			return ((data[0] & 0x7F) << 21) |
				   ((data[1] & 0x7F) << 14) |
				   ((data[2] & 0x7F) << 7) |
				   (data[3] & 0x7F);
		}
		// Big-endian for v2.3
		return (int)ReadUInt32BE (data);
	}

	static void AddUInt32BE (BinaryDataBuilder builder, uint value)
	{
		builder.Add ((byte)((value >> 24) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)(value & 0xFF));
	}

	static void AddSyncSafeUInt32 (BinaryDataBuilder builder, uint value)
	{
		builder.Add ((byte)((value >> 21) & 0x7F));
		builder.Add ((byte)((value >> 14) & 0x7F));
		builder.Add ((byte)((value >> 7) & 0x7F));
		builder.Add ((byte)(value & 0x7F));
	}
}

/// <summary>
/// Represents the result of reading a CHAP frame.
/// </summary>
public readonly struct ChapterFrameReadResult : IEquatable<ChapterFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public ChapterFrame? Frame { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => Frame is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed.
	/// </summary>
	public int BytesConsumed { get; }

	ChapterFrameReadResult (ChapterFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static ChapterFrameReadResult Success (ChapterFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static ChapterFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (ChapterFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is ChapterFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (ChapterFrameReadResult left, ChapterFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (ChapterFrameReadResult left, ChapterFrameReadResult right) =>
		!left.Equals (right);
}
