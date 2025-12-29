// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Riff;

/// <summary>
/// Represents BWF (Broadcast Wave Format) bext chunk metadata.
/// </summary>
/// <remarks>
/// The bext chunk is defined by EBU Tech 3285 and contains professional
/// broadcast metadata including description, originator, timestamps, and
/// time reference for synchronization.
///
/// Structure (fixed 602 bytes + variable CodingHistory):
/// - Offset 0:   Description (256 bytes, ASCII null-padded)
/// - Offset 256: Originator (32 bytes, ASCII null-padded)
/// - Offset 288: OriginatorReference (32 bytes, ASCII null-padded)
/// - Offset 320: OriginationDate (10 bytes, YYYY-MM-DD)
/// - Offset 330: OriginationTime (8 bytes, HH:MM:SS)
/// - Offset 338: TimeReference (8 bytes, 64-bit LE sample count)
/// - Offset 346: Version (2 bytes, LE)
/// - Offset 348: UMID (64 bytes, Version 1+)
/// - Offset 412: Loudness fields (10 bytes, Version 2+)
/// - Offset 422: Reserved (180 bytes)
/// - Offset 602: CodingHistory (variable length, CR/LF lines)
/// </remarks>
public class BextTag
{
	/// <summary>
	/// Minimum size of a bext chunk (fixed portion).
	/// </summary>
	public const int MinimumSize = 602;

	/// <summary>
	/// FourCC identifier for the bext chunk.
	/// </summary>
	public const string ChunkId = "bext";

	// Field offsets
	const int DescriptionOffset = 0;
	const int DescriptionSize = 256;
	const int OriginatorOffset = 256;
	const int OriginatorSize = 32;
	const int OriginatorReferenceOffset = 288;
	const int OriginatorReferenceSize = 32;
	const int OriginationDateOffset = 320;
	const int OriginationDateSize = 10;
	const int OriginationTimeOffset = 330;
	const int OriginationTimeSize = 8;
	const int TimeReferenceOffset = 338;
	const int VersionOffset = 346;
	const int UmidOffset = 348;
	const int UmidSize = 64;
	const int CodingHistoryOffset = 602;

	/// <summary>
	/// Gets or sets the free-form description of the audio content (max 256 characters).
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Gets or sets the name of the originator/creator (max 32 characters).
	/// </summary>
	public string? Originator { get; set; }

	/// <summary>
	/// Gets or sets a unique reference for the file (max 32 characters).
	/// </summary>
	public string? OriginatorReference { get; set; }

	/// <summary>
	/// Gets or sets the origination date in YYYY-MM-DD format.
	/// </summary>
	public string? OriginationDate { get; set; }

	/// <summary>
	/// Gets or sets the origination time in HH:MM:SS format.
	/// </summary>
	public string? OriginationTime { get; set; }

	/// <summary>
	/// Gets or sets the time reference as sample count from midnight.
	/// </summary>
	/// <remarks>
	/// This is a 64-bit sample count that can be used for synchronization.
	/// At 48kHz, the maximum value allows for over 12 million years.
	/// </remarks>
	public ulong TimeReference { get; set; }

	/// <summary>
	/// Gets or sets the BWF version (0, 1, or 2).
	/// </summary>
	/// <remarks>
	/// Version 0: Basic fields only
	/// Version 1: Adds UMID
	/// Version 2: Adds loudness metadata
	/// </remarks>
	public ushort Version { get; set; }

	/// <summary>
	/// Gets or sets the SMPTE UMID (Unique Material Identifier).
	/// </summary>
	/// <remarks>
	/// Only present in Version 1 and later. 64 bytes, with the last 32
	/// set to zero if using basic (32-byte) UMID.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - UMID is a fixed-size identifier
	public byte[]? Umid { get; set; }
#pragma warning restore CA1819

	/// <summary>
	/// Gets or sets the coding history as a series of lines.
	/// </summary>
	/// <remarks>
	/// Each line describes a step in the audio's history, typically in the format:
	/// A=PCM,F=48000,W=24,M=stereo,T=original
	/// Lines are separated by CR/LF in the file.
	/// </remarks>
	public string? CodingHistory { get; set; }

	/// <summary>
	/// Gets whether this tag has no data.
	/// </summary>
	public bool IsEmpty =>
		string.IsNullOrEmpty (Description) &&
		string.IsNullOrEmpty (Originator) &&
		string.IsNullOrEmpty (OriginatorReference) &&
		string.IsNullOrEmpty (OriginationDate) &&
		string.IsNullOrEmpty (OriginationTime) &&
		TimeReference == 0 &&
		string.IsNullOrEmpty (CodingHistory);

	/// <summary>
	/// Parses a bext chunk from binary data.
	/// </summary>
	/// <param name="data">The bext chunk data (excluding chunk header).</param>
	/// <returns>The parsed tag, or null if invalid.</returns>
	public static BextTag? Parse (BinaryData data)
	{
		if (data.Length < MinimumSize)
			return null;

		var tag = new BextTag ();

		// Parse fixed-length ASCII fields
		tag.Description = ReadAsciiField (data, DescriptionOffset, DescriptionSize);
		tag.Originator = ReadAsciiField (data, OriginatorOffset, OriginatorSize);
		tag.OriginatorReference = ReadAsciiField (data, OriginatorReferenceOffset, OriginatorReferenceSize);
		tag.OriginationDate = ReadAsciiField (data, OriginationDateOffset, OriginationDateSize);
		tag.OriginationTime = ReadAsciiField (data, OriginationTimeOffset, OriginationTimeSize);

		// TimeReference (64-bit LE as low + high DWORDs)
		var timeLow = data.ToUInt32LE (TimeReferenceOffset);
		var timeHigh = data.ToUInt32LE (TimeReferenceOffset + 4);
		tag.TimeReference = timeLow | ((ulong)timeHigh << 32);

		// Version
		tag.Version = data.ToUInt16LE (VersionOffset);

		// UMID (Version 1+)
		if (tag.Version >= 1) {
			var umid = new byte[UmidSize];
			data.Slice (UmidOffset, UmidSize).Span.CopyTo (umid);
			// Only set if non-zero
			bool hasUmid = false;
			for (int i = 0; i < UmidSize; i++) {
				if (umid[i] != 0) {
					hasUmid = true;
					break;
				}
			}
			if (hasUmid)
				tag.Umid = umid;
		}

		// CodingHistory (variable length after fixed portion)
		if (data.Length > CodingHistoryOffset) {
			var historyData = data.Slice (CodingHistoryOffset);
			var history = historyData.ToStringLatin1 ().TrimEnd ('\r', '\n', '\0');
			if (!string.IsNullOrEmpty (history))
				tag.CodingHistory = history;
		}

		return tag;
	}

	/// <summary>
	/// Renders this tag to binary data.
	/// </summary>
	/// <returns>The complete bext chunk data.</returns>
	public BinaryData Render ()
	{
		var codingHistoryBytes = string.IsNullOrEmpty (CodingHistory)
			? Array.Empty<byte> ()
			: System.Text.Encoding.ASCII.GetBytes (CodingHistory + "\r\n");

		var totalSize = MinimumSize + codingHistoryBytes.Length;
		using var builder = new BinaryDataBuilder (totalSize);

		// Write fixed-length fields
		WriteAsciiField (builder, Description, DescriptionSize);
		WriteAsciiField (builder, Originator, OriginatorSize);
		WriteAsciiField (builder, OriginatorReference, OriginatorReferenceSize);
		WriteAsciiField (builder, OriginationDate, OriginationDateSize);
		WriteAsciiField (builder, OriginationTime, OriginationTimeSize);

		// TimeReference (64-bit LE as low + high DWORDs)
		builder.AddUInt32LE ((uint)(TimeReference & 0xFFFFFFFF));
		builder.AddUInt32LE ((uint)(TimeReference >> 32));

		// Version
		builder.AddUInt16LE (Version);

		// UMID (64 bytes)
		if (Umid is not null && Umid.Length == UmidSize) {
			builder.Add (Umid);
		} else {
			builder.Add (new byte[UmidSize]);
		}

		// Loudness values (10 bytes for Version 2) + Reserved (180 bytes) = 190 bytes
		// We don't support loudness values yet, so just write zeros
		builder.Add (new byte[190]);

		// CodingHistory
		if (codingHistoryBytes.Length > 0)
			builder.Add (codingHistoryBytes);

		return builder.ToBinaryData ();
	}

	static string? ReadAsciiField (BinaryData data, int offset, int size)
	{
		var fieldData = data.Slice (offset, size);
		var str = fieldData.ToStringLatin1 ().TrimEnd ('\0');
		return string.IsNullOrEmpty (str) ? null : str;
	}

	static void WriteAsciiField (BinaryDataBuilder builder, string? value, int size)
	{
		var bytes = new byte[size];
		if (!string.IsNullOrEmpty (value)) {
			var encoded = System.Text.Encoding.ASCII.GetBytes (value);
			var copyLen = Math.Min (encoded.Length, size);
			Array.Copy (encoded, bytes, copyLen);
		}
		builder.Add (bytes);
	}
}
