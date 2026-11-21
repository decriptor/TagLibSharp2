// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Riff;

/// <summary>A chunk directly inside the first <c>RIFF 'AVI '</c> form.</summary>
/// <remarks>FourCCs are kept as their little-endian 32-bit value, so a walk allocates nothing per chunk.</remarks>
internal readonly struct AviTopChunk
{
	public AviTopChunk (long offset, uint id, uint listType, uint size, long end, bool truncated)
	{
		Offset = offset;
		Id = id;
		ListType = listType;
		Size = size;
		End = end;
		Truncated = truncated;
	}

	/// <summary>File offset of the chunk header.</summary>
	public long Offset { get; }

	/// <summary>The chunk FourCC.</summary>
	public uint Id { get; }

	/// <summary>The list type of a <c>LIST</c> chunk, otherwise zero.</summary>
	public uint ListType { get; }

	/// <summary>ckSize as stored. It excludes the header and the pad byte.</summary>
	public uint Size { get; }

	/// <summary>Offset after the chunk and its pad byte, clamped to the form.</summary>
	public long End { get; }

	/// <summary>True when ckSize runs past the end of the form or the file.</summary>
	public bool Truncated { get; }

	public long DataOffset => Offset + RiffChunk.HeaderSize;

	public bool IsList (uint listType) => Id == AviChunks.List && ListType == listType;

	public bool IsJunk => Id == AviChunks.Junk;

	/// <summary>An ID3v2 tag stored as a RIFF chunk. <c>ID32</c> is the name used in AVI; <c>id3 </c> is the WAV name.</summary>
	public bool IsId3 => Id == AviChunks.Id32 || Id == AviChunks.Id3Lower;
}

/// <summary>
/// Reads the chunks of the first RIFF form of an AVI file one at a time.
/// </summary>
/// <remarks>
/// RIFF chunks are word aligned: a chunk with an odd ckSize is followed by one pad byte that
/// ckSize does not count (Multimedia Programming Interface and Data Specifications 1.0, "RIFF Chunks").
/// The form ends at 8 + the RIFF ckSize. A ckSize of 0xFFFFFFFF or one past the end of the data
/// is clamped to the data, so a truncated or still-recording file is read as far as it goes.
/// </remarks>
internal ref struct AviChunkReader
{
	readonly ReadOnlySpan<byte> _data;
	readonly long _limit;
	long _position;

	public AviChunkReader (ReadOnlySpan<byte> data)
	{
		_data = data;
		_limit = AviChunks.FormEnd (BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4)), data.Length);
		_position = RiffFile.HeaderSize;
	}

	public bool TryNext (out AviTopChunk chunk)
	{
		chunk = default;
		if (_position + RiffChunk.HeaderSize > _limit)
			return false;

		var at = (int)_position;
		var id = BinaryPrimitives.ReadUInt32LittleEndian (_data.Slice (at));
		var size = BinaryPrimitives.ReadUInt32LittleEndian (_data.Slice (at + 4));
		uint listType = 0;
		if (id == AviChunks.List && size >= 4 && _position + 12 <= _limit)
			listType = BinaryPrimitives.ReadUInt32LittleEndian (_data.Slice (at + 8));

		var dataEnd = _position + RiffChunk.HeaderSize + size;
		var next = dataEnd + (size & 1);
		chunk = new AviTopChunk (_position, id, listType, size, Math.Min (next, _limit), dataEnd > _limit);
		_position = next >= _limit ? long.MaxValue - RiffChunk.HeaderSize : next;
		return true;
	}
}

/// <summary>FourCCs and shared walks over the first RIFF form of an AVI file.</summary>
internal static class AviChunks
{
	public static readonly uint List = Code ("LIST");
	public static readonly uint Junk = Code ("JUNK");
	public static readonly uint Id32 = Code ("ID32");
	public static readonly uint Id3Lower = Code ("id3 ");
	public static readonly uint Idx1 = Code ("idx1");
	public static readonly uint Hdrl = Code ("hdrl");
	public static readonly uint Movi = Code ("movi");
	public static readonly uint Info = Code ("INFO");

	/// <summary>A FourCC as the little-endian value its four bytes read as.</summary>
	public static uint Code (string fourCC) =>
		(uint)fourCC[0] | ((uint)fourCC[1] << 8) | ((uint)fourCC[2] << 16) | ((uint)fourCC[3] << 24);

	public static string Name (uint code) =>
		new ([(char)(code & 0xFF), (char)((code >> 8) & 0xFF), (char)((code >> 16) & 0xFF), (char)(code >> 24)]);

	public static bool IsAvi (ReadOnlySpan<byte> data) =>
		data.Length >= RiffFile.HeaderSize
		&& data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
		&& data[8] == 'A' && data[9] == 'V' && data[10] == 'I' && data[11] == ' ';

	/// <summary>True when the RIFF size is the 0xFFFFFFFF a streaming writer leaves before it finalizes the file.</summary>
	public static bool IsUnfinalized (ReadOnlySpan<byte> data) =>
		BinaryPrimitives.ReadUInt32LittleEndian (data.Slice (4)) == 0xFFFFFFFF;

	/// <summary>The end of the first form: 8 + ckSize, clamped to <paramref name="length"/>.</summary>
	public static long FormEnd (uint riffSize, long length)
	{
		if (riffSize == 0xFFFFFFFF)
			return length;
		return Math.Min (length, (long)riffSize + 8);
	}

	/// <summary>The data of <paramref name="chunk"/>, clamped to the bytes present.</summary>
	public static ReadOnlySpan<byte> Body (ReadOnlySpan<byte> data, AviTopChunk chunk) =>
		data.Slice ((int)chunk.DataOffset, (int)Math.Min (chunk.Size, data.Length - chunk.DataOffset));

	/// <summary>True for a tag chunk a reader parses. A larger chunk is left alone, read or save.</summary>
	public static bool IsReadable (AviTopChunk chunk) => chunk.Size <= AviFile.MaxTagChunkBytes;

	/// <summary>The first <c>LIST hdrl</c> a reader parses.</summary>
	public static bool TryFindHeaderList (ReadOnlySpan<byte> data, out AviTopChunk hdrl)
	{
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out hdrl)) {
			if (hdrl.IsList (Hdrl) && IsReadable (hdrl))
				return true;
		}

		hdrl = default;
		return false;
	}

	/// <summary>The first <c>LIST movi</c>.</summary>
	public static bool TryFindMovi (ReadOnlySpan<byte> data, out AviTopChunk movi)
	{
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out movi)) {
			if (movi.IsList (Movi))
				return true;
		}

		movi = default;
		return false;
	}

	/// <summary>
	/// Every <c>LIST INFO</c> merged in file order. The first list wins; a later list fills only empty fields.
	/// </summary>
	public static RiffInfoTag? MergeInfo (ReadOnlySpan<byte> data)
	{
		RiffInfoTag? info = null;
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var chunk)) {
			if (!chunk.IsList (Info) || !IsReadable (chunk))
				continue;
			var parsed = RiffInfoTag.Parse (new BinaryData (Body (data, chunk)), utf8: true);
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
	/// The first ID3v2 chunk. <paramref name="tag"/> is null when that chunk did not parse.
	/// </summary>
	public static bool TryFindId3 (ReadOnlySpan<byte> data, out AviTopChunk chunk, out Id3v2Tag? tag)
	{
		tag = null;
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out chunk)) {
			if (!chunk.IsId3 || !IsReadable (chunk))
				continue;
			var result = Id3v2Tag.Read (Body (data, chunk));
			if (result.IsSuccess)
				tag = result.Tag;
			return true;
		}

		chunk = default;
		return false;
	}
}
