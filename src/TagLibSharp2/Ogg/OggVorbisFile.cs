// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Ogg;

/// <summary>
/// Represents an Ogg Vorbis audio file with its metadata.
/// </summary>
/// <remarks>
/// <para>
/// Ogg Vorbis files contain Vorbis audio data encapsulated in an Ogg container.
/// The Vorbis stream begins with three required header packets:
/// </para>
/// <list type="number">
/// <item>Identification header (packet type 1) - codec parameters</item>
/// <item>Comment header (packet type 3) - Vorbis Comments metadata</item>
/// <item>Setup header (packet type 5) - codebook data</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/vorbis/doc/Vorbis_I_spec.html
/// </para>
/// </remarks>
public sealed class OggVorbisFile
{
	const int MinVorbisHeaderSize = 7; // 1 byte type + 6 bytes "vorbis"
	static readonly byte[] VorbisMagic = { 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 }; // "vorbis"

	/// <summary>
	/// Gets or sets the Vorbis Comment block containing metadata tags.
	/// </summary>
	public VorbisComment? VorbisComment { get; set; }

	/// <summary>
	/// Gets or sets the title tag.
	/// </summary>
	public string? Title {
		get => VorbisComment?.Title;
		set => EnsureVorbisComment ().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist tag.
	/// </summary>
	public string? Artist {
		get => VorbisComment?.Artist;
		set => EnsureVorbisComment ().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album tag.
	/// </summary>
	public string? Album {
		get => VorbisComment?.Album;
		set => EnsureVorbisComment ().Album = value;
	}

	/// <summary>
	/// Gets or sets the year tag.
	/// </summary>
	public string? Year {
		get => VorbisComment?.Year;
		set => EnsureVorbisComment ().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre tag.
	/// </summary>
	public string? Genre {
		get => VorbisComment?.Genre;
		set => EnsureVorbisComment ().Genre = value;
	}

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public uint? Track {
		get => VorbisComment?.Track;
		set => EnsureVorbisComment ().Track = value;
	}

	/// <summary>
	/// Gets or sets the comment tag.
	/// </summary>
	public string? Comment {
		get => VorbisComment?.Comment;
		set => EnsureVorbisComment ().Comment = value;
	}

	OggVorbisFile ()
	{
	}

	/// <summary>
	/// Attempts to read an Ogg Vorbis file from a file path.
	/// </summary>
	/// <param name="path">The path to the Ogg Vorbis file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static OggVorbisFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return OggVorbisFileReadResult.Failure (readResult.Error!);

		return Read (readResult.Data!);
	}

	/// <summary>
	/// Attempts to read an Ogg Vorbis file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static OggVorbisFileReadResult Read (ReadOnlySpan<byte> data)
	{
		var file = new OggVorbisFile ();
		var offset = 0;
		var pageCount = 0;
		var foundIdentification = false;
		var foundComment = false;

		// For packet reassembly across pages
		var packetBuffer = new List<byte> ();
		var currentPacketIndex = 0; // 0=ident, 1=comment, 2=setup

		// Read Ogg pages until we find the Vorbis comment header
		while (offset < data.Length && pageCount < 50) { // Limit to prevent infinite loop
			var pageResult = ReadOggPageWithSegments (data.Slice (offset));
			if (!pageResult.IsSuccess) {
				if (pageCount == 0)
					return OggVorbisFileReadResult.Failure ($"Invalid Ogg file: {pageResult.Error}");
				break; // No more valid pages
			}

			offset += pageResult.BytesConsumed;
			pageCount++;

			// First page must be BOS with Vorbis identification header
			if (pageCount == 1) {
				if (!pageResult.Page.IsBeginOfStream)
					return OggVorbisFileReadResult.Failure ("First page must have BOS flag");

				// First packet should be identification header
				if (pageResult.Segments.Count == 0)
					return OggVorbisFileReadResult.Failure ("First page has no segments");

				var firstPacket = pageResult.Segments[0];
				if (!IsVorbisIdentificationHeader (firstPacket))
					return OggVorbisFileReadResult.Failure ("Not a Vorbis stream (expected identification header)");

				foundIdentification = true;
				currentPacketIndex = 1; // Next packet will be comment

				// Check if there are more complete packets on this page
				for (var i = 1; i < pageResult.Segments.Count; i++) {
					if (pageResult.IsPacketComplete[i]) {
						// Complete packet on first page - rare but possible
						if (currentPacketIndex == 1) {
							var commentResult = TryParseCommentPacket (pageResult.Segments[i]);
							if (commentResult.IsSuccess) {
								file.VorbisComment = commentResult.Tag;
								foundComment = true;
							} else if (IsVorbisCommentHeader (pageResult.Segments[i])) {
								// It's a comment header but parsing failed (e.g., invalid framing bit)
								return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
							}
							currentPacketIndex = 2;
						}
					} else {
						// Packet continues to next page
						packetBuffer.AddRange (pageResult.Segments[i]);
					}
				}

				continue;
			}

			// Process subsequent pages
			if (foundIdentification && !foundComment) {
				// Handle continuation from previous page
				var segmentIndex = 0;
				if (pageResult.Page.IsContinuation && packetBuffer.Count > 0) {
					// Continuation of previous packet
					if (pageResult.Segments.Count > 0) {
						packetBuffer.AddRange (pageResult.Segments[0]);
						if (pageResult.IsPacketComplete[0]) {
							// Packet is now complete
							if (currentPacketIndex == 1) {
								var packet = packetBuffer.ToArray ();
								var commentResult = TryParseCommentPacket (packet);
								if (commentResult.IsSuccess) {
									file.VorbisComment = commentResult.Tag;
									foundComment = true;
								} else if (IsVorbisCommentHeader (packet)) {
									// It's a comment header but parsing failed
									return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
								}
								currentPacketIndex = 2;
							}
							packetBuffer.Clear ();
						}
						segmentIndex = 1;
					}
				}

				// Process remaining segments on this page
				for (var i = segmentIndex; i < pageResult.Segments.Count && !foundComment; i++) {
					if (pageResult.IsPacketComplete[i]) {
						// Complete packet
						if (currentPacketIndex == 1) {
							var commentResult = TryParseCommentPacket (pageResult.Segments[i]);
							if (commentResult.IsSuccess) {
								file.VorbisComment = commentResult.Tag;
								foundComment = true;
							} else if (IsVorbisCommentHeader (pageResult.Segments[i])) {
								// It's a comment header but parsing failed
								return OggVorbisFileReadResult.Failure (commentResult.Error ?? "Failed to parse comment header");
							}
							currentPacketIndex = 2;
						} else {
							currentPacketIndex++;
						}
					} else {
						// Packet continues to next page
						packetBuffer.Clear ();
						packetBuffer.AddRange (pageResult.Segments[i]);
					}
				}

				if (foundComment)
					break;
			}
		}

		if (!foundIdentification)
			return OggVorbisFileReadResult.Failure ("No Vorbis identification header found");

		return OggVorbisFileReadResult.Success (file, offset);
	}

	static VorbisCommentReadResult TryParseCommentPacket (ReadOnlySpan<byte> packet)
	{
		if (!IsVorbisCommentHeader (packet))
			return VorbisCommentReadResult.Failure ("Not a Vorbis comment header");

		// Validate framing bit (last byte must be 1)
		if (packet.Length < MinVorbisHeaderSize + 1)
			return VorbisCommentReadResult.Failure ("Comment header too short for framing bit");

		var framingBit = packet[packet.Length - 1];
		if (framingBit != 1)
			return VorbisCommentReadResult.Failure ($"Invalid framing bit (expected 1, got {framingBit})");

		// Parse the Vorbis comment
		// Skip packet type (1) + "vorbis" (6) = 7 bytes
		// Also skip framing bit at the end (1 byte)
		var commentSpan = packet.Slice (MinVorbisHeaderSize, packet.Length - MinVorbisHeaderSize - 1);
		return VorbisComment.Read (commentSpan);
	}

	static VorbisCommentReadResult TryParseCommentPacket (byte[] packet) =>
		TryParseCommentPacket (packet.AsSpan ());

	/// <summary>
	/// Result of reading an Ogg page with segment information.
	/// </summary>
	readonly struct OggPageWithSegmentsResult
	{
		public OggPage Page { get; }
		public bool IsSuccess { get; }
		public string? Error { get; }
		public int BytesConsumed { get; }
		public List<byte[]> Segments { get; }
		public List<bool> IsPacketComplete { get; }

		public OggPageWithSegmentsResult (OggPage page, bool isSuccess, string? error, int bytesConsumed,
			List<byte[]> segments, List<bool> isPacketComplete)
		{
			Page = page;
			IsSuccess = isSuccess;
			Error = error;
			BytesConsumed = bytesConsumed;
			Segments = segments;
			IsPacketComplete = isPacketComplete;
		}

		public static OggPageWithSegmentsResult Success (OggPage page, int bytesConsumed,
			List<byte[]> segments, List<bool> isPacketComplete) =>
			new (page, true, null, bytesConsumed, segments, isPacketComplete);

		public static OggPageWithSegmentsResult Failure (string error) =>
			new (default, false, error, 0, new List<byte[]> (), new List<bool> ());
	}

	/// <summary>
	/// Reads an Ogg page and extracts individual packets based on segment table.
	/// </summary>
	static OggPageWithSegmentsResult ReadOggPageWithSegments (ReadOnlySpan<byte> data)
	{
		const int minHeaderSize = 27;

		if (data.Length < minHeaderSize)
			return OggPageWithSegmentsResult.Failure ("Data too short");

		// Read basic page structure
		var pageResult = OggPage.Read (data);
		if (!pageResult.IsSuccess)
			return OggPageWithSegmentsResult.Failure (pageResult.Error ?? "Unknown error");

		// Now re-parse to get segment table info
		if (data[0] != 'O' || data[1] != 'g' || data[2] != 'g' || data[3] != 'S')
			return OggPageWithSegmentsResult.Failure ("Invalid Ogg magic");

		var segmentCount = data[26];
		if (data.Length < 27 + segmentCount)
			return OggPageWithSegmentsResult.Failure ("Data too short for segment table");

		var segmentTable = data.Slice (27, segmentCount);

		// Extract packets from segments
		// A segment of 255 bytes means the packet continues
		// A segment < 255 bytes ends the packet
		var segments = new List<byte[]> ();
		var isPacketComplete = new List<bool> ();
		var currentPacket = new List<byte> ();
		var pageDataOffset = 0;

		for (var i = 0; i < segmentCount; i++) {
			var segmentSize = segmentTable[i];
			if (pageDataOffset + segmentSize > pageResult.Page.Data.Length)
				break;

			// Add segment to current packet using efficient batch copy
			var segmentData = pageResult.Page.Data.Span.Slice (pageDataOffset, segmentSize);
#if NET8_0_OR_GREATER
			// Use CollectionsMarshal for zero-copy append on modern .NET
			var oldCount = currentPacket.Count;
			System.Runtime.InteropServices.CollectionsMarshal.SetCount (currentPacket, oldCount + segmentSize);
			segmentData.CopyTo (System.Runtime.InteropServices.CollectionsMarshal.AsSpan (currentPacket).Slice (oldCount));
#else
			currentPacket.AddRange (segmentData.ToArray ());
#endif

			pageDataOffset += segmentSize;

			// If segment < 255, packet is complete
			if (segmentSize < 255) {
				segments.Add (currentPacket.ToArray ());
				isPacketComplete.Add (true);
				currentPacket.Clear ();
			}
		}

		// If there's remaining data in currentPacket, it continues to next page
		if (currentPacket.Count > 0) {
			segments.Add (currentPacket.ToArray ());
			isPacketComplete.Add (false);
		}

		return OggPageWithSegmentsResult.Success (pageResult.Page, pageResult.BytesConsumed, segments, isPacketComplete);
	}

	static bool IsVorbisIdentificationHeader (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinVorbisHeaderSize)
			return false;

		// Packet type 1 + "vorbis"
		return data[0] == 1 &&
			data[1] == VorbisMagic[0] && data[2] == VorbisMagic[1] &&
			data[3] == VorbisMagic[2] && data[4] == VorbisMagic[3] &&
			data[5] == VorbisMagic[4] && data[6] == VorbisMagic[5];
	}

	static bool IsVorbisCommentHeader (ReadOnlySpan<byte> data)
	{
		if (data.Length < MinVorbisHeaderSize)
			return false;

		// Packet type 3 + "vorbis"
		return data[0] == 3 &&
			data[1] == VorbisMagic[0] && data[2] == VorbisMagic[1] &&
			data[3] == VorbisMagic[2] && data[4] == VorbisMagic[3] &&
			data[5] == VorbisMagic[4] && data[6] == VorbisMagic[5];
	}

	VorbisComment EnsureVorbisComment ()
	{
		VorbisComment ??= new VorbisComment ("TagLibSharp2");
		return VorbisComment;
	}
}

/// <summary>
/// Represents the result of reading an <see cref="OggVorbisFile"/> from binary data.
/// </summary>
public readonly struct OggVorbisFileReadResult : IEquatable<OggVorbisFileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public OggVorbisFile? File { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	OggVorbisFileReadResult (OggVorbisFile? file, string? error, int bytesConsumed)
	{
		File = file;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="file">The parsed file.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static OggVorbisFileReadResult Success (OggVorbisFile file, int bytesConsumed) =>
		new (file, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static OggVorbisFileReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (OggVorbisFileReadResult other) =>
		ReferenceEquals (File, other.File) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is OggVorbisFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (OggVorbisFileReadResult left, OggVorbisFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (OggVorbisFileReadResult left, OggVorbisFileReadResult right) =>
		!left.Equals (right);
}
