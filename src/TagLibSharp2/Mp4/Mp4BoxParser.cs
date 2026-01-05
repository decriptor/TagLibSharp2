// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Internal helper for parsing ISO 14496-12 boxes from binary data.
/// Implements the exact box header parsing logic per the specification.
/// </summary>
internal static class Mp4BoxParser
{
	// Container boxes that contain child boxes (ISO 14496-12 + common extensions)
	static readonly HashSet<string> ContainerBoxes = new ()
	{
		"moov", "trak", "mdia", "minf", "stbl", "udta", "meta",
		"ilst", "edts", "dinf", "mvex", "moof", "traf", "mfra",
		"skip", "free", "wide", // Can contain padding
		"----", // iTunes freeform
		"covr", // Can have multiple data atoms
		// iTunes metadata atoms (ilst children that contain data sub-boxes)
		"©nam", "©ART", "©alb", "©day", "©cmt", "©gen", "©wrt", "©grp",
		"©lyr", "©too", "©pub", "©enc", "cprt", "aART", "desc", "ldes",
		"trkn", "disk", "tmpo", "cpil", "pgap", "rtng", // track, disc, tempo, rating, etc.
		"soal", "soaa", "soar", "soco", "sonm", // sort fields
		"©mvn", "©mvi", "©mvc", "©wrk", "shwm", // classical music
		"purl", "egid", "catg", "keyw", "pcst", // podcast
	};

	/// <summary>
	/// Parses a single box from the data at the specified offset.
	/// Follows ISO 14496-12 box header parsing exactly.
	/// </summary>
	/// <param name="data">The data to parse from.</param>
	/// <param name="offset">The byte offset to start parsing at.</param>
	/// <param name="maxSize">Maximum size constraint (for bounds checking), or 0 for end of data.</param>
	/// <returns>Parse result containing the box and bytes consumed, or failure.</returns>
	public static Mp4BoxReadResult ParseBox (ReadOnlySpan<byte> data, int offset = 0, long maxSize = 0)
	{
		if (offset >= data.Length || offset < 0)
			return Mp4BoxReadResult.Failure ();

		var remaining = data.Slice (offset);

		// Minimum box size is 8 bytes (size + type)
		if (remaining.Length < 8)
			return Mp4BoxReadResult.Failure ();

		// Read basic header: size (4 bytes) + type (4 bytes)
		var size32 = BinaryData.FromByteArray (remaining.Slice (0, 4).ToArray ()).ToUInt32BE ();
		var typeBytes = remaining.Slice (4, 4);
		var type = Polyfills.Latin1.GetString (typeBytes);

		long boxSize;
		int headerSize;
		bool usesExtendedSize;

		// Handle special size values per ISO 14496-12 §4.2
		if (size32 == 1) {
			// Extended 64-bit size
			if (remaining.Length < 16)
				return Mp4BoxReadResult.Failure ();

			var largeSize = BinaryData.FromByteArray (remaining.Slice (8, 8).ToArray ()).ToUInt64BE ();
			boxSize = (long)largeSize;
			headerSize = 16;
			usesExtendedSize = true;
		} else if (size32 == 0) {
			// Box extends to end of file (only valid for last box)
			boxSize = remaining.Length;
			headerSize = 8;
			usesExtendedSize = false;
		} else {
			// Standard 32-bit size
			boxSize = size32;
			headerSize = 8;
			usesExtendedSize = false;
		}

		// Validate size per ISO 14496-12 (must be at least header size)
		if (boxSize < headerSize)
			return Mp4BoxReadResult.Failure ();

		// Check bounds
		if (maxSize > 0 && boxSize > maxSize)
			return Mp4BoxReadResult.Failure ();

		if (boxSize > remaining.Length)
			return Mp4BoxReadResult.Failure ();

		// Extract box data (everything after header)
		var dataSize = (int)(boxSize - headerSize);
		var boxData = dataSize > 0
			? new BinaryData (remaining.Slice (headerSize, dataSize))
			: BinaryData.Empty;

		// Parse children if this is a container box
		IReadOnlyList<Mp4Box>? children = null;
		if (IsContainerBox (type)) {
			// meta is a FullBox: skip version(1) + flags(3) before children
			if (type == "meta" && boxData.Length >= 4)
				children = ParseChildren (boxData.Slice (4).Span);
			else
				children = ParseChildren (boxData.Span);
		}

		var box = new Mp4Box (type, boxData, children, usesExtendedSize);
		return new Mp4BoxReadResult (box, (int)boxSize);
	}

	/// <summary>
	/// Determines if a box type is a container that holds child boxes.
	/// </summary>
	/// <param name="type">The 4-character box type.</param>
	/// <returns>True if the box is a container.</returns>
	public static bool IsContainerBox (string type)
	{
		return ContainerBoxes.Contains (type);
	}

	/// <summary>
	/// Parses all child boxes from container data.
	/// </summary>
	/// <param name="data">The container's data (excluding container header).</param>
	/// <returns>List of parsed child boxes.</returns>
	public static IReadOnlyList<Mp4Box> ParseChildren (ReadOnlySpan<byte> data)
	{
		var children = new List<Mp4Box> ();
		var offset = 0;

		while (offset < data.Length) {
			var result = ParseBox (data, offset, data.Length - offset);
			if (!result.IsSuccess)
				break;

			children.Add (result.Box!);
			offset += result.BytesConsumed;
		}

		return children;
	}

	/// <summary>
	/// Parses a FullBox from the data (version + flags + content).
	/// Many MP4 boxes extend FullBox.
	/// </summary>
	/// <param name="type">The box type.</param>
	/// <param name="data">The box data.</param>
	/// <param name="children">Optional child boxes if this is a container.</param>
	/// <param name="usesExtendedSize">Whether the box uses extended size.</param>
	/// <returns>Parsed FullBox or null if data is too short.</returns>
	public static Mp4FullBox? ParseFullBox (string type, BinaryData data, IReadOnlyList<Mp4Box>? children = null, bool usesExtendedSize = false)
	{
		if (data.Length < 4)
			return null;

		return Mp4FullBox.Parse (type, data, children, usesExtendedSize);
	}
}
