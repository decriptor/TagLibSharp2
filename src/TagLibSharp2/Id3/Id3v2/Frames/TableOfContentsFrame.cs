// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Id3.Id3v2.Frames;

/// <summary>
/// Represents an ID3v2 CTOC (Table of Contents) frame.
/// </summary>
/// <remarks>
/// <para>
/// CTOC frames define the structure of chapters in audio content, commonly used in podcasts
/// and audiobooks. A tag typically has one top-level CTOC that references CHAP frames.
/// </para>
/// <para>
/// Frame format:
/// </para>
/// <code>
/// Element ID (null-terminated Latin-1 string)
/// Flags (1 byte): bit 1 = top-level, bit 0 = ordered
/// Entry count (1 byte)
/// Child element IDs (null-terminated Latin-1 strings)
/// Optional embedded sub-frames (TIT2 for title, etc.)
/// </code>
/// </remarks>
public sealed class TableOfContentsFrame
{
	readonly List<string> _childElementIds = new (8);

	/// <summary>
	/// Gets the frame ID (always "CTOC").
	/// </summary>
	public static string FrameId => "CTOC";

	/// <summary>
	/// Gets or sets the unique element ID for this table of contents.
	/// </summary>
	/// <remarks>
	/// This ID can be referenced by other CTOC frames for nested tables of contents.
	/// </remarks>
	public string ElementId { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this is the top-level table of contents.
	/// </summary>
	/// <remarks>
	/// Only one CTOC in a tag should have this flag set. This is the entry point
	/// for chapter navigation.
	/// </remarks>
	public bool IsTopLevel { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the child elements are ordered.
	/// </summary>
	/// <remarks>
	/// When true, the child elements should be played/displayed in the order listed.
	/// </remarks>
	public bool IsOrdered { get; set; }

	/// <summary>
	/// Gets or sets the title of this table of contents (from embedded TIT2 frame).
	/// </summary>
	public string? Title { get; set; }

	/// <summary>
	/// Gets the list of child element IDs (references to CHAP or other CTOC frames).
	/// </summary>
	public IReadOnlyList<string> ChildElementIds => _childElementIds;

	/// <summary>
	/// Initializes a new instance of the <see cref="TableOfContentsFrame"/> class.
	/// </summary>
	/// <param name="elementId">The unique element ID.</param>
	public TableOfContentsFrame (string elementId)
	{
		ElementId = elementId;
	}

	/// <summary>
	/// Adds a child element ID to this table of contents.
	/// </summary>
	/// <param name="childId">The element ID of a CHAP or CTOC frame.</param>
	public void AddChildElement (string childId)
	{
		_childElementIds.Add (childId);
	}

	/// <summary>
	/// Removes a child element ID from this table of contents.
	/// </summary>
	/// <param name="childId">The element ID to remove.</param>
	/// <returns>True if the element was found and removed.</returns>
	public bool RemoveChildElement (string childId)
	{
		return _childElementIds.Remove (childId);
	}

	/// <summary>
	/// Clears all child element IDs from this table of contents.
	/// </summary>
	public void ClearChildElements ()
	{
		_childElementIds.Clear ();
	}

	/// <summary>
	/// Attempts to read a CTOC frame from the provided data.
	/// </summary>
	/// <param name="data">The frame content data (excluding frame header).</param>
	/// <param name="version">The ID3v2 version.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static TableOfContentsFrameReadResult Read (ReadOnlySpan<byte> data, Id3v2Version version)
	{
		// Minimum size: element ID (1 + null) + flags (1) + entry count (1) = 4 bytes minimum
		if (data.Length < 4)
			return TableOfContentsFrameReadResult.Failure ("CTOC frame data is too short");

		// Read element ID (null-terminated)
		var nullIndex = data.IndexOf ((byte)0);
		if (nullIndex < 0 || nullIndex == 0)
			return TableOfContentsFrameReadResult.Failure ("CTOC frame missing element ID");

		var elementId = Polyfills.Latin1.GetString (data.Slice (0, nullIndex));
		var offset = nullIndex + 1;

		// Need at least 2 more bytes for flags and entry count
		if (offset + 2 > data.Length)
			return TableOfContentsFrameReadResult.Failure ("CTOC frame missing flags or entry count");

		// Flags: bit 1 = top-level, bit 0 = ordered
		var flags = data[offset];
		var isTopLevel = (flags & 0x02) != 0;
		var isOrdered = (flags & 0x01) != 0;
		offset++;

		// Entry count
		var entryCount = data[offset];
		offset++;

		var frame = new TableOfContentsFrame (elementId) {
			IsTopLevel = isTopLevel,
			IsOrdered = isOrdered
		};

		// Read child element IDs
		for (var i = 0; i < entryCount; i++) {
			if (offset >= data.Length)
				break;

			var childNullIndex = data.Slice (offset).IndexOf ((byte)0);
			if (childNullIndex < 0)
				break;

			var childId = Polyfills.Latin1.GetString (data.Slice (offset, childNullIndex));
			frame.AddChildElement (childId);
			offset += childNullIndex + 1;
		}

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

		return TableOfContentsFrameReadResult.Success (frame, data.Length);
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

		// Calculate child IDs size
		var childIdsSize = 0;
		for (var i = 0; i < _childElementIds.Count; i++)
			childIdsSize += Polyfills.Latin1.GetByteCount (_childElementIds[i]) + 1; // +1 for null

		var baseSize = elementIdBytes.Length + 1 + 1 + 1 + childIdsSize; // +1 null, +1 flags, +1 count
		var totalSize = baseSize;

		if (titleFrameData.HasValue)
			totalSize += 10 + titleFrameData.Value.Length; // 10 for frame header

		using var builder = new BinaryDataBuilder (totalSize);

		// Element ID (null-terminated)
		builder.Add (elementIdBytes);
		builder.Add ((byte)0x00);

		// Flags: bit 1 = top-level, bit 0 = ordered
		byte flags = 0;
		if (IsTopLevel)
			flags |= 0x02;
		if (IsOrdered)
			flags |= 0x01;
		builder.Add (flags);

		// Entry count
		builder.Add ((byte)_childElementIds.Count);

		// Child element IDs (null-terminated)
		for (var i = 0; i < _childElementIds.Count; i++) {
			builder.Add (Polyfills.Latin1.GetBytes (_childElementIds[i]));
			builder.Add ((byte)0x00);
		}

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

	static uint ReadUInt32BE (ReadOnlySpan<byte> data)
	{
		return ((uint)data[0] << 24) |
			   ((uint)data[1] << 16) |
			   ((uint)data[2] << 8) |
			   (uint)data[3];
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
/// Represents the result of reading a CTOC frame.
/// </summary>
public readonly struct TableOfContentsFrameReadResult : IEquatable<TableOfContentsFrameReadResult>
{
	/// <summary>
	/// Gets the parsed frame, or null if parsing failed.
	/// </summary>
	public TableOfContentsFrame? Frame { get; }

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

	TableOfContentsFrameReadResult (TableOfContentsFrame? frame, string? error, int bytesConsumed)
	{
		Frame = frame;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static TableOfContentsFrameReadResult Success (TableOfContentsFrame frame, int bytesConsumed) =>
		new (frame, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static TableOfContentsFrameReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (TableOfContentsFrameReadResult other) =>
		ReferenceEquals (Frame, other.Frame) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is TableOfContentsFrameReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (Frame, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (TableOfContentsFrameReadResult left, TableOfContentsFrameReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (TableOfContentsFrameReadResult left, TableOfContentsFrameReadResult right) =>
		!left.Equals (right);
}
