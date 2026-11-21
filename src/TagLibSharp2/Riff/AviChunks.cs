// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Riff;

/// <summary>A chunk directly inside the first <c>RIFF 'AVI '</c> form.</summary>
internal readonly struct AviTopChunk
{
	public AviTopChunk (long offset, string fourCC, string? listType, uint size, long end, bool truncated)
	{
		Offset = offset;
		FourCC = fourCC;
		ListType = listType;
		Size = size;
		End = end;
		Truncated = truncated;
	}

	/// <summary>File offset of the chunk header.</summary>
	public long Offset { get; }

	public string FourCC { get; }

	/// <summary>The list type of a <c>LIST</c> chunk, otherwise null.</summary>
	public string? ListType { get; }

	/// <summary>ckSize as stored. It excludes the header and the pad byte.</summary>
	public uint Size { get; }

	/// <summary>Offset after the chunk and its pad byte, clamped to the form.</summary>
	public long End { get; }

	/// <summary>True when ckSize runs past the end of the form or the file.</summary>
	public bool Truncated { get; }

	public long DataOffset => Offset + RiffChunk.HeaderSize;

	public bool IsList (string listType) => FourCC == "LIST" && ListType == listType;

	/// <summary>An ID3v2 tag stored as a RIFF chunk. <c>ID32</c> is the name used in AVI; <c>id3 </c> is the WAV name.</summary>
	public bool IsId3 => FourCC is "ID32" or "id3 ";
}

/// <summary>
/// Walks the chunks of the first RIFF form of an AVI file.
/// </summary>
/// <remarks>
/// RIFF chunks are word aligned: a chunk with an odd ckSize is followed by one pad byte that
/// ckSize does not count (Multimedia Programming Interface and Data Specifications 1.0, "RIFF Chunks").
/// The form ends at 8 + the RIFF ckSize. A ckSize of 0xFFFFFFFF or one past the end of the data
/// is clamped to the data, so a truncated or still-recording file is read as far as it goes.
/// </remarks>
internal static class AviChunks
{
	public const string FormType = "AVI ";

	public static bool IsAvi (ReadOnlySpan<byte> data) =>
		data.Length >= RiffFile.HeaderSize
		&& data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
		&& data[8] == 'A' && data[9] == 'V' && data[10] == 'I' && data[11] == ' ';

	/// <summary>The end of the first form: 8 + ckSize, clamped to <paramref name="length"/>.</summary>
	public static long FormEnd (uint riffSize, long length)
	{
		if (riffSize == 0xFFFFFFFF)
			return length;
		return Math.Min (length, (long)riffSize + 8);
	}

	public static List<AviTopChunk> Walk (ReadOnlySpan<byte> data)
	{
		var chunks = new List<AviTopChunk> ();
		var limit = FormEnd (BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4)), data.Length);
		long position = RiffFile.HeaderSize;
		while (position + RiffChunk.HeaderSize <= limit) {
			var at = (int)position;
			var size = BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (at + 4));
			string? listType = null;
			if (data[at] == 'L' && data[at + 1] == 'I' && data[at + 2] == 'S' && data[at + 3] == 'T'
				&& size >= 4 && position + 12 <= limit)
				listType = AviHeaders.FourCC (data.Slice (at + 8));

			var dataEnd = position + RiffChunk.HeaderSize + size;
			var next = dataEnd + (size & 1);
			var truncated = dataEnd > limit;
			chunks.Add (new AviTopChunk (position, AviHeaders.FourCC (data.Slice (at)), listType, size, Math.Min (next, limit), truncated));
			if (next >= limit)
				break;
			position = next;
		}

		return chunks;
	}

	/// <summary>The data of <paramref name="chunk"/>, clamped to the bytes present.</summary>
	public static ReadOnlySpan<byte> Body (ReadOnlySpan<byte> data, AviTopChunk chunk) =>
		data.Slice ((int)chunk.DataOffset, (int)Math.Min (chunk.Size, data.Length - chunk.DataOffset));

	/// <summary>True for a tag chunk a reader parses. A larger chunk is left alone, read or save.</summary>
	public static bool IsReadable (AviTopChunk chunk) => chunk.Size <= AviFile.MaxTagChunkBytes;

	/// <summary>The first <c>LIST hdrl</c> a reader parses, or -1.</summary>
	public static int HeaderList (List<AviTopChunk> chunks) =>
		chunks.FindIndex (c => c.IsList ("hdrl") && IsReadable (c));

	/// <summary>
	/// Every <c>LIST INFO</c> merged in file order. The first list wins; a later list fills only empty fields.
	/// </summary>
	public static RiffInfoTag? MergeInfo (ReadOnlySpan<byte> data, List<AviTopChunk> chunks)
	{
		RiffInfoTag? info = null;
		foreach (var chunk in chunks) {
			if (!chunk.IsList ("INFO") || !IsReadable (chunk))
				continue;
			var parsed = RiffInfoTag.Parse (new BinaryData (Body (data, chunk)));
			if (parsed is null)
				continue;
			if (info is null)
				info = parsed;
			else
				info.FillMissingFrom (parsed);
		}

		return info;
	}

	/// <summary>
	/// The first ID3v2 chunk, or -1. <paramref name="tag"/> is null when that chunk did not parse.
	/// </summary>
	public static int FirstId3 (ReadOnlySpan<byte> data, List<AviTopChunk> chunks, out Id3v2Tag? tag)
	{
		tag = null;
		for (var i = 0; i < chunks.Count; i++) {
			if (!chunks[i].IsId3 || !IsReadable (chunks[i]))
				continue;
			var result = Id3v2Tag.Read (Body (data, chunks[i]));
			if (result.IsSuccess)
				tag = result.Tag;
			return i;
		}

		return -1;
	}
}
