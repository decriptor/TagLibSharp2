// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents an index point within a FLAC cue sheet track.
/// </summary>
/// <remarks>
/// Index points mark specific positions within a track. Index 0 is the pre-gap,
/// index 1 is the track start, and higher indices mark sub-divisions.
/// </remarks>
public sealed class FlacCueSheetIndex
{
	/// <summary>
	/// Gets or sets the index point number (0-99).
	/// </summary>
	/// <remarks>
	/// Index 0 is the pre-gap, index 1 is where the track actually starts.
	/// </remarks>
	public byte IndexNumber { get; set; }

	/// <summary>
	/// Gets or sets the offset of this index point relative to the track start, in samples.
	/// </summary>
	public ulong Offset { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="FlacCueSheetIndex"/> class.
	/// </summary>
	/// <param name="indexNumber">The index point number.</param>
	/// <param name="offset">The offset in samples relative to track start.</param>
	public FlacCueSheetIndex (byte indexNumber, ulong offset)
	{
		IndexNumber = indexNumber;
		Offset = offset;
	}
}

/// <summary>
/// Represents a track within a FLAC cue sheet.
/// </summary>
/// <remarks>
/// <para>
/// FLAC cue sheet tracks correspond to CD audio tracks. Each track has:
/// </para>
/// <list type="bullet">
/// <item>Track number (1-99 for regular tracks, 170 for lead-out)</item>
/// <item>Offset in samples from the start of the file</item>
/// <item>ISRC (International Standard Recording Code)</item>
/// <item>Index points for seeking within the track</item>
/// </list>
/// </remarks>
public sealed class FlacCueSheetTrack
{
	readonly List<FlacCueSheetIndex> _indices = new (4);

	/// <summary>
	/// Gets or sets the track number (1-99, or 170 for lead-out track).
	/// </summary>
	public byte TrackNumber { get; set; }

	/// <summary>
	/// Gets or sets the track offset from the start of the file, in samples.
	/// </summary>
	public ulong Offset { get; set; }

	/// <summary>
	/// Gets or sets the ISRC (International Standard Recording Code).
	/// </summary>
	/// <remarks>
	/// 12 ASCII characters, or empty string if not set.
	/// </remarks>
	public string Isrc { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this is an audio track.
	/// </summary>
	/// <remarks>
	/// True for audio tracks, false for data tracks. CD audio typically has this set to true.
	/// </remarks>
	public bool IsAudio { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether pre-emphasis is applied.
	/// </summary>
	public bool HasPreEmphasis { get; set; }

	/// <summary>
	/// Gets the list of index points for this track.
	/// </summary>
	public IReadOnlyList<FlacCueSheetIndex> Indices => _indices;

	/// <summary>
	/// Initializes a new instance of the <see cref="FlacCueSheetTrack"/> class.
	/// </summary>
	/// <param name="trackNumber">The track number.</param>
	/// <param name="offset">The track offset in samples.</param>
	public FlacCueSheetTrack (byte trackNumber, ulong offset)
	{
		TrackNumber = trackNumber;
		Offset = offset;
		Isrc = "";
		IsAudio = true;
	}

	/// <summary>
	/// Adds an index point to this track.
	/// </summary>
	/// <param name="index">The index to add.</param>
	public void AddIndex (FlacCueSheetIndex index)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (index is null)
			throw new ArgumentNullException (nameof (index));
#else
		ArgumentNullException.ThrowIfNull (index);
#endif
		_indices.Add (index);
	}

	/// <summary>
	/// Clears all index points from this track.
	/// </summary>
	public void ClearIndices ()
	{
		_indices.Clear ();
	}
}

/// <summary>
/// Represents a FLAC CUESHEET metadata block.
/// </summary>
/// <remarks>
/// <para>
/// The CUESHEET block stores CD table-of-contents information, useful for:
/// </para>
/// <list type="bullet">
/// <item>Preserving track boundaries from ripped CDs</item>
/// <item>Enabling gapless playback</item>
/// <item>Storing disc and track ISRCs</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/flac/format.html#metadata_block_cuesheet
/// </para>
/// </remarks>
public sealed class FlacCueSheet
{
	const int MediaCatalogNumberSize = 128;
	const int MinimumBlockSize = 396; // Header without any tracks
	const int TrackSize = 36; // Base track size without indices
	const int IndexSize = 12; // Size of each index point

	readonly List<FlacCueSheetTrack> _tracks = new (16);

	/// <summary>
	/// Gets or sets the media catalog number (MCN/UPC/EAN).
	/// </summary>
	/// <remarks>
	/// For CDs, this is the 13-digit UPC/EAN barcode. Empty string if not set.
	/// </remarks>
	public string MediaCatalogNumber { get; set; }

	/// <summary>
	/// Gets or sets the number of lead-in samples.
	/// </summary>
	/// <remarks>
	/// For CD-DA, this is usually 88200 samples (2 seconds at 44.1kHz).
	/// </remarks>
	public ulong LeadInSamples { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this represents a Compact Disc.
	/// </summary>
	/// <remarks>
	/// When true, the CUESHEET data must conform to Red Book CD-DA constraints.
	/// </remarks>
	public bool IsCompactDisc { get; set; }

	/// <summary>
	/// Gets the list of tracks in this cue sheet.
	/// </summary>
	/// <remarks>
	/// For CD audio, the last track should be the lead-out track (track number 170).
	/// </remarks>
	public IReadOnlyList<FlacCueSheetTrack> Tracks => _tracks;

	/// <summary>
	/// Initializes a new instance of the <see cref="FlacCueSheet"/> class.
	/// </summary>
	public FlacCueSheet ()
	{
		MediaCatalogNumber = "";
	}

	/// <summary>
	/// Adds a track to this cue sheet.
	/// </summary>
	/// <param name="track">The track to add.</param>
	public void AddTrack (FlacCueSheetTrack track)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (track is null)
			throw new ArgumentNullException (nameof (track));
#else
		ArgumentNullException.ThrowIfNull (track);
#endif
		_tracks.Add (track);
	}

	/// <summary>
	/// Clears all tracks from this cue sheet.
	/// </summary>
	public void ClearTracks ()
	{
		_tracks.Clear ();
	}

	/// <summary>
	/// Attempts to read a CUESHEET block from the provided data.
	/// </summary>
	/// <param name="data">The block data (excluding block header).</param>
	/// <returns>A result indicating success or failure.</returns>
	public static FlacCueSheetReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinimumBlockSize)
			return FlacCueSheetReadResult.Failure ("CUESHEET data is too short");

		var cueSheet = new FlacCueSheet ();
		var offset = 0;

		// Media catalog number (128 bytes, null-padded ASCII)
		var catalogBytes = data.Slice (offset, MediaCatalogNumberSize);
		cueSheet.MediaCatalogNumber = ReadNullTerminatedAscii (catalogBytes);
		offset += MediaCatalogNumberSize;

		// Lead-in samples (8 bytes, big-endian)
		cueSheet.LeadInSamples = ReadUInt64BE (data.Slice (offset, 8));
		offset += 8;

		// Flags: bit 7 = is compact disc
		cueSheet.IsCompactDisc = (data[offset] & 0x80) != 0;
		offset += 1;

		// Reserved (258 bytes)
		offset += 258;

		// Number of tracks (1 byte)
		var numTracks = data[offset];
		offset += 1;

		// Parse tracks
		for (var i = 0; i < numTracks; i++) {
			if (offset + TrackSize > data.Length)
				return FlacCueSheetReadResult.Failure ("CUESHEET track data extends beyond block");

			// Track offset (8 bytes, big-endian)
			var trackOffset = ReadUInt64BE (data.Slice (offset, 8));
			offset += 8;

			// Track number (1 byte)
			var trackNumber = data[offset];
			offset += 1;

			// ISRC (12 bytes, ASCII)
			var isrcBytes = data.Slice (offset, 12);
			var isrc = ReadNullTerminatedAscii (isrcBytes);
			offset += 12;

			// Flags: bit 7 = non-audio (inverted from our IsAudio), bit 6 = pre-emphasis
			var trackFlags = data[offset];
			var isAudio = (trackFlags & 0x80) == 0;
			var hasPreEmphasis = (trackFlags & 0x40) != 0;
			offset += 1;

			// Reserved (13 bytes)
			offset += 13;

			// Number of track indices (1 byte)
			var numIndices = data[offset];
			offset += 1;

			var track = new FlacCueSheetTrack (trackNumber, trackOffset) {
				Isrc = isrc,
				IsAudio = isAudio,
				HasPreEmphasis = hasPreEmphasis
			};

			// Parse indices
			for (var j = 0; j < numIndices; j++) {
				if (offset + IndexSize > data.Length)
					return FlacCueSheetReadResult.Failure ("CUESHEET index data extends beyond block");

				// Index offset (8 bytes, big-endian)
				var indexOffset = ReadUInt64BE (data.Slice (offset, 8));
				offset += 8;

				// Index number (1 byte)
				var indexNumber = data[offset];
				offset += 1;

				// Reserved (3 bytes)
				offset += 3;

				track.AddIndex (new FlacCueSheetIndex (indexNumber, indexOffset));
			}

			cueSheet.AddTrack (track);
		}

		return FlacCueSheetReadResult.Success (cueSheet, offset);
	}

	/// <summary>
	/// Renders this cue sheet to binary data.
	/// </summary>
	/// <returns>The rendered block data.</returns>
	public BinaryData Render ()
	{
		// Calculate total size
		var totalSize = MinimumBlockSize;
		for (var i = 0; i < _tracks.Count; i++)
			totalSize += TrackSize + (_tracks[i].Indices.Count * IndexSize);

		using var builder = new BinaryDataBuilder (totalSize);

		// Media catalog number (128 bytes, null-padded ASCII)
		var catalogBytes = System.Text.Encoding.ASCII.GetBytes (MediaCatalogNumber);
		if (catalogBytes.Length < MediaCatalogNumberSize) {
			builder.Add (catalogBytes);
			builder.AddZeros (MediaCatalogNumberSize - catalogBytes.Length);
		} else {
			builder.Add (catalogBytes.AsSpan ().Slice (0, MediaCatalogNumberSize));
		}

		// Lead-in samples (8 bytes, big-endian)
		AddUInt64BE (builder, LeadInSamples);

		// Flags + reserved: bit 7 = is compact disc, rest is reserved
		builder.Add ((byte)(IsCompactDisc ? 0x80 : 0x00));
		builder.AddZeros (258);

		// Number of tracks (1 byte)
		builder.Add ((byte)_tracks.Count);

		// Render tracks
		for (var i = 0; i < _tracks.Count; i++) {
			var track = _tracks[i];

			// Track offset (8 bytes, big-endian)
			AddUInt64BE (builder, track.Offset);

			// Track number (1 byte)
			builder.Add (track.TrackNumber);

			// ISRC (12 bytes, null-padded ASCII)
			var isrcBytes = System.Text.Encoding.ASCII.GetBytes (track.Isrc);
			if (isrcBytes.Length < 12) {
				builder.Add (isrcBytes);
				builder.AddZeros (12 - isrcBytes.Length);
			} else {
				builder.Add (isrcBytes.AsSpan ().Slice (0, 12));
			}

			// Flags: bit 7 = non-audio (inverted), bit 6 = pre-emphasis
			byte trackFlags = 0;
			if (!track.IsAudio)
				trackFlags |= 0x80;
			if (track.HasPreEmphasis)
				trackFlags |= 0x40;
			builder.Add (trackFlags);

			// Reserved (13 bytes)
			builder.AddZeros (13);

			// Number of track indices (1 byte)
			builder.Add ((byte)track.Indices.Count);

			// Render indices
			for (var j = 0; j < track.Indices.Count; j++) {
				var index = track.Indices[j];

				// Index offset (8 bytes, big-endian)
				AddUInt64BE (builder, index.Offset);

				// Index number (1 byte)
				builder.Add (index.IndexNumber);

				// Reserved (3 bytes)
				builder.AddZeros (3);
			}
		}

		return builder.ToBinaryData ();
	}

	static string ReadNullTerminatedAscii (ReadOnlySpan<byte> data)
	{
		var nullIndex = data.IndexOf ((byte)0);
		if (nullIndex == 0)
			return "";
		if (nullIndex < 0)
			nullIndex = data.Length;

		return System.Text.Encoding.ASCII.GetString (data.Slice (0, nullIndex));
	}

	static ulong ReadUInt64BE (ReadOnlySpan<byte> data)
	{
		return ((ulong)data[0] << 56) |
			   ((ulong)data[1] << 48) |
			   ((ulong)data[2] << 40) |
			   ((ulong)data[3] << 32) |
			   ((ulong)data[4] << 24) |
			   ((ulong)data[5] << 16) |
			   ((ulong)data[6] << 8) |
			   (ulong)data[7];
	}

	static void AddUInt64BE (BinaryDataBuilder builder, ulong value)
	{
		builder.Add ((byte)((value >> 56) & 0xFF));
		builder.Add ((byte)((value >> 48) & 0xFF));
		builder.Add ((byte)((value >> 40) & 0xFF));
		builder.Add ((byte)((value >> 32) & 0xFF));
		builder.Add ((byte)((value >> 24) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)(value & 0xFF));
	}
}

/// <summary>
/// Represents the result of reading a FLAC CUESHEET block.
/// </summary>
public readonly struct FlacCueSheetReadResult : IEquatable<FlacCueSheetReadResult>
{
	/// <summary>
	/// Gets the parsed cue sheet, or null if parsing failed.
	/// </summary>
	public FlacCueSheet? CueSheet { get; }

	/// <summary>
	/// Gets a value indicating whether parsing succeeded.
	/// </summary>
	public bool IsSuccess => CueSheet is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed.
	/// </summary>
	public int BytesConsumed { get; }

	FlacCueSheetReadResult (FlacCueSheet? cueSheet, string? error, int bytesConsumed)
	{
		CueSheet = cueSheet;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static FlacCueSheetReadResult Success (FlacCueSheet cueSheet, int bytesConsumed) =>
		new (cueSheet, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static FlacCueSheetReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (FlacCueSheetReadResult other) =>
		ReferenceEquals (CueSheet, other.CueSheet) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FlacCueSheetReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (CueSheet, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (FlacCueSheetReadResult left, FlacCueSheetReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (FlacCueSheetReadResult left, FlacCueSheetReadResult right) =>
		!left.Equals (right);
}
