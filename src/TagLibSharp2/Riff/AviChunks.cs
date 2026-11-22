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

	/// <summary>A <c>CSET</c> chunk, which names the code page of the file's text.</summary>
	public bool IsCharacterSet => Id == AviChunks.Cset;

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
	public static readonly uint Cset = Code ("CSET");

	/// <summary>CSET wCodePage for UTF-8.</summary>
	public const ushort Utf8CodePage = 65001;

	/// <summary>CSET wCodePage for ISO 8859-1.</summary>
	const ushort Latin1CodePage = 28591;

	/// <summary>CSET wCodePage for Windows-1252.</summary>
	const ushort Windows1252CodePage = 1252;

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
	/// The character set the first <c>CSET</c> chunk declares (Multimedia Programming Interface and
	/// Data Specifications 1.0, "CSET (Character Set) Chunk": wCodePage, wCountryCode, wLanguageCode,
	/// wDialect). Code page 0 is the spec's ISO 8859-1 default. Code pages other than 0, 28591, 1252
	/// and 65001 are <see cref="InfoCharacterSet.Other"/>; no CSET, or one without a wCodePage, is
	/// <see cref="InfoCharacterSet.None"/>.
	/// </summary>
	public static InfoCharacterSet CharacterSet (ReadOnlySpan<byte> data)
	{
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var chunk)) {
			if (!chunk.IsCharacterSet)
				continue;
			var body = Body (data, chunk);
			if (body.Length < 2)
				return InfoCharacterSet.None;
			return BinaryPrimitives.ReadUInt16LittleEndian (body) switch {
				Utf8CodePage => InfoCharacterSet.Utf8,
				0 => InfoCharacterSet.Latin1Default,
				Latin1CodePage => InfoCharacterSet.Latin1,
				Windows1252CodePage => InfoCharacterSet.Windows1252,
				_ => InfoCharacterSet.Other
			};
		}

		return InfoCharacterSet.None;
	}

	/// <summary>The first whole CSET chunk, header and pad included, to write again in front of a rewritten INFO list.</summary>
	public static byte[]? FirstCharacterSetChunk (ReadOnlySpan<byte> data)
	{
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var chunk)) {
			if (!chunk.IsCharacterSet)
				continue;
			if (chunk.Truncated)
				return null;
			return data.Slice ((int)chunk.Offset, (int)(chunk.End - chunk.Offset)).ToArray ();
		}

		return null;
	}

	/// <summary>
	/// A CSET chunk declaring UTF-8 (wCodePage 65001). The file's first CSET keeps its wCountryCode,
	/// wLanguage and wDialect: MPIDS 1.0 reads a zero there as USA and US English, so zeroing them would
	/// change what the file declares. With no CSET, or one too short to hold them, they are zero.
	/// </summary>
	public static byte[] Utf8CharacterSetChunk (ReadOnlySpan<byte> data)
	{
		var chunk = new byte[RiffChunk.HeaderSize + 8];
		BinaryPrimitives.WriteUInt32LittleEndian (chunk, Cset);
		BinaryPrimitives.WriteUInt32LittleEndian (chunk.AsSpan (4), 8);
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var existing)) {
			if (!existing.IsCharacterSet)
				continue;
			var body = Body (data, existing);
			if (body.Length >= 8)
				body.Slice (2, 6).CopyTo (chunk.AsSpan (RiffChunk.HeaderSize + 2));
			break;
		}

		BinaryPrimitives.WriteUInt16LittleEndian (chunk.AsSpan (8), Utf8CodePage);
		return chunk;
	}

	/// <summary>The first CSET's wCodePage, or -1 when there is no CSET or it is too short to hold one.</summary>
	public static int CodePage (ReadOnlySpan<byte> data)
	{
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var chunk)) {
			if (!chunk.IsCharacterSet)
				continue;
			var body = Body (data, chunk);
			return body.Length < 2 ? -1 : BinaryPrimitives.ReadUInt16LittleEndian (body);
		}

		return -1;
	}

	/// <summary>
	/// Every <c>LIST INFO</c> merged in file order, decoded in the file's character set.
	/// The first list wins; a later list fills only empty fields.
	/// </summary>
	public static RiffInfoTag? MergeInfo (ReadOnlySpan<byte> data)
	{
		var characterSet = CharacterSet (data);
		var codePage = CodePage (data);
		RiffInfoTag? info = null;
		var reader = new AviChunkReader (data);
		while (reader.TryNext (out var chunk)) {
			if (!chunk.IsList (Info) || !IsReadable (chunk))
				continue;
			var parsed = RiffInfoTag.Parse (new BinaryData (Body (data, chunk)), characterSet, codePage);
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
