// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TagLibSharp2.Tests.Riff;

/// <summary>How the builder writes <c>idx1</c> dwChunkOffset.</summary>
public enum AviOldIndex
{
	/// <summary>No idx1 chunk.</summary>
	None,

	/// <summary>Offsets from the <c>movi</c> list type, as the AVI reference specifies.</summary>
	Relative,

	/// <summary>Offsets from the start of the file, as some writers store them.</summary>
	Absolute
}

/// <summary>
/// Builds AVI files from the AVI RIFF reference and the OpenDML 1.02 extensions.
/// Video is 8-bit gray (<c>Y800</c>), audio is 16-bit PCM, and every sample chunk holds
/// bytes unique to its stream and position so a test can tell any two apart.
/// </summary>
public sealed class AviTestBuilder
{
	public int Width { get; set; } = 4;
	public int Height { get; set; } = 2;
	public uint Rate { get; set; } = 25;
	public uint Scale { get; set; } = 1;
	public int Frames { get; set; } = 6;
	public bool Audio { get; set; } = true;
	public bool Video { get; set; } = true;
	public uint AudioSampleRate { get; set; } = 8000;
	public ushort AudioChannels { get; set; } = 1;

	/// <summary>INFO fields written in one LIST INFO after hdrl, or none when empty.</summary>
	public List<(string Id, string Value)> Info { get; } = [];

	/// <summary>The text encoding of INFO values. RIFF INFO has no fixed encoding; ffmpeg writes UTF-8.</summary>
	public Encoding InfoEncoding { get; set; } = Encoding.Latin1;

	/// <summary>When set, a CSET chunk with this wCodePage is written just before the INFO list.</summary>
	public ushort? CharacterSet { get; set; }

	/// <summary>A chunk written right after the RIFF header, before <c>hdrl</c>.</summary>
	public (string FourCC, byte[] Data)? ChunkBeforeHeaders { get; set; }

	/// <summary>A second LIST INFO written after <c>movi</c>, or none when empty.</summary>
	public List<(string Id, string Value)> InfoAfterMovi { get; } = [];

	/// <summary>Data of an <c>ID32</c> chunk written after the INFO list, or none.</summary>
	public byte[]? Id3 { get; set; }

	/// <summary>Data bytes of the JUNK chunk before <c>movi</c>, or no chunk when negative.</summary>
	public int JunkBeforeMovi { get; set; } = 1016;

	/// <summary>The byte the JUNK before <c>movi</c> is filled with. A rewrite of that chunk zeroes it.</summary>
	public byte JunkFill { get; set; }

	public AviOldIndex OldIndex { get; set; } = AviOldIndex.Relative;

	/// <summary>Writes <c>indx</c> super indexes, <c>ix##</c> standard indexes, and <c>odml/dmlh</c>.</summary>
	public bool OpenDml { get; set; }

	/// <summary>Video frames (and audio chunks) written in a <c>RIFF AVIX</c> after the first form.</summary>
	public int AvixFrames { get; set; }

	/// <summary>Makes the video super index name the first standard index twice.</summary>
	public bool DuplicateSuperIndexEntry { get; set; }

	/// <summary>
	/// Writes each standard index with qwBaseOffset 0 and absolute dwOffsets. AVISTDINDEX allows any
	/// base: qwBaseOffset + dwOffset is the file offset of the data.
	/// </summary>
	public bool StandardIndexBaseZero { get; set; }

	/// <summary>bIndexSubType written into each standard index (1 is AVI_INDEX_2FIELD).</summary>
	public byte StandardIndexSubType { get; set; }

	/// <summary>Overrides the video super index's bIndexType.</summary>
	public byte? SuperIndexType { get; set; }

	public uint PaddingGranularity { get; set; }

	public uint? MainTotalFrames { get; set; }
	public uint? VideoLength { get; set; }
	public uint? OdmlTotalFrames { get; set; }
	public uint MicroSecondsPerFrame { get; set; } = 40000;

	/// <summary>biHeight is written negative (a top-down bitmap).</summary>
	public bool TopDown { get; set; }

	/// <summary>An extra chunk written between the INFO list and the JUNK chunk.</summary>
	public (string FourCC, byte[] Data)? ChunkBeforeJunk { get; set; }

	public bool WithMovi { get; set; } = true;

	int AudioBytesPerChunk => (int)(AudioSampleRate / Rate) * 2 * AudioChannels;

	public static byte[] FrameBytes (int stream, int index, int length)
	{
		var bytes = new byte[length];
		for (var i = 0; i < length; i++)
			bytes[i] = (byte)(0x31 + stream * 101 + index * 7 + i * 13);
		if (length >= 2) {
			bytes[0] = (byte)stream;
			bytes[1] = (byte)index;
		}
		return bytes;
	}

	public byte[] Build ()
	{
		using var w = new Writer ();
		w.FourCC ("RIFF");
		var riffSize = w.Placeholder32 ();
		w.FourCC ("AVI ");

		if (ChunkBeforeHeaders is { } first)
			w.Chunk (first.FourCC, first.Data);

		var hdrl = w.BeginList ("hdrl");
		w.FourCC ("avih");
		w.U32 (56);
		w.U32 (MicroSecondsPerFrame);
		w.U32 (0);
		w.U32 (PaddingGranularity);
		w.U32 (0x10);
		w.U32 (MainTotalFrames ?? (uint)Frames);
		w.U32 (0);
		w.U32 ((uint)((Video ? 1 : 0) + (Audio ? 1 : 0)));
		w.U32 (0);
		w.U32 ((uint)Width);
		w.U32 ((uint)Height);
		w.Zero (16);

		var superPositions = new List<long> ();
		if (Video) {
			var strl = w.BeginList ("strl");
			StreamHeader (w, "vids", "Y800", Scale, Rate, VideoLength ?? (uint)(Frames + AvixFrames), 0);
			w.FourCC ("strf");
			w.U32 (40);
			w.U32 (40);
			w.U32 ((uint)Width);
			w.U32 ((uint)(TopDown ? -Height : Height));
			w.U16 (1);
			w.U16 (8);
			w.FourCC ("Y800");
			w.U32 ((uint)(Width * Height));
			w.Zero (16);
			if (OpenDml)
				superPositions.Add (SuperIndex (w, "00dc"));
			w.EndList (strl);
		}

		if (Audio) {
			var strl = w.BeginList ("strl");
			StreamHeader (w, "auds", "\0\0\0\0", 2u * AudioChannels, AudioSampleRate * 2 * AudioChannels,
				(uint)(AudioBytesPerChunk * (Frames + AvixFrames)) / (2u * AudioChannels), 2u * AudioChannels);
			w.FourCC ("strf");
			w.U32 (16);
			w.U16 (1);
			w.U16 (AudioChannels);
			w.U32 (AudioSampleRate);
			w.U32 (AudioSampleRate * 2 * AudioChannels);
			w.U16 ((ushort)(2 * AudioChannels));
			w.U16 (16);
			if (OpenDml)
				superPositions.Add (SuperIndex (w, "01wb"));
			w.EndList (strl);
		}

		if (OpenDml) {
			var odml = w.BeginList ("odml");
			w.FourCC ("dmlh");
			w.U32 (248);
			w.U32 (OdmlTotalFrames ?? (uint)(Frames + AvixFrames));
			w.Zero (244);
			w.EndList (odml);
		}

		w.EndList (hdrl);

		if (CharacterSet is { } codePage) {
			var cset = new byte[8];
			BinaryPrimitives.WriteUInt16LittleEndian (cset, codePage);
			w.Chunk ("CSET", cset);
		}
		if (Info.Count > 0)
			InfoList (w, Info);
		if (Id3 is not null)
			w.Chunk ("ID32", Id3);
		if (ChunkBeforeJunk is { } extra)
			w.Chunk (extra.FourCC, extra.Data);
		if (JunkBeforeMovi >= 0)
			w.Chunk ("JUNK", Enumerable.Repeat (JunkFill, JunkBeforeMovi).ToArray ());

		var index = new List<(string Id, long Position, int Size)> ();
		var standard = new List<(int Stream, long Position)> ();
		if (WithMovi) {
			var movi = w.BeginList ("movi");
			WriteSamples (w, 0, Frames, index);
			if (OpenDml)
				standard.AddRange (StandardIndexes (w, index, movi + 8));
			w.EndList (movi);

			if (OldIndex != AviOldIndex.None) {
				w.FourCC ("idx1");
				w.U32 ((uint)(index.Count * 16));
				foreach (var (id, position, size) in index) {
					w.FourCC (id);
					w.U32 (0x10);
					w.U32 ((uint)(OldIndex == AviOldIndex.Absolute ? position : position - (movi + 8)));
					w.U32 ((uint)size);
				}
			}
		}

		if (InfoAfterMovi.Count > 0)
			InfoList (w, InfoAfterMovi);

		w.Patch32 (riffSize, (uint)(w.Position - 8));

		if (AvixFrames > 0) {
			var avix = w.Position;
			w.FourCC ("RIFF");
			var avixSize = w.Placeholder32 ();
			w.FourCC ("AVIX");
			var movi = w.BeginList ("movi");
			var more = new List<(string Id, long Position, int Size)> ();
			WriteSamples (w, Frames, AvixFrames, more);
			if (OpenDml)
				standard.AddRange (StandardIndexes (w, more, movi + 8));
			w.EndList (movi);
			w.Patch32 (avixSize, (uint)(w.Position - avix - 8));
		}

		var bytes = w.ToArray ();
		for (var s = 0; s < superPositions.Count; s++)
			FillSuperIndex (bytes, superPositions[s], standard.Where (x => x.Stream == s).Select (x => x.Position).ToList (), s == 0);
		return bytes;
	}

	void WriteSamples (Writer w, int first, int count, List<(string Id, long Position, int Size)> index)
	{
		for (var i = first; i < first + count; i++) {
			if (Video) {
				index.Add (("00dc", w.Position, Width * Height));
				w.Chunk ("00dc", FrameBytes (0, i, Width * Height));
			}
			if (Audio) {
				index.Add (("01wb", w.Position, AudioBytesPerChunk));
				w.Chunk ("01wb", FrameBytes (1, i, AudioBytesPerChunk));
			}
		}
	}

	/// <summary>One AVISTDINDEX per stream. qwBaseOffset is the <c>movi</c> list type; dwOffset points at chunk data.</summary>
	List<(int Stream, long Position)> StandardIndexes (Writer w, List<(string Id, long Position, int Size)> index, long baseOffset)
	{
		var written = new List<(int Stream, long Position)> ();
		var ids = new List<string> ();
		if (Video)
			ids.Add ("00dc");
		if (Audio)
			ids.Add ("01wb");
		for (var s = 0; s < ids.Count; s++) {
			var entries = index.Where (x => x.Id == ids[s]).ToList ();
			written.Add ((s, w.Position));
			w.FourCC ("ix0" + s.ToString (System.Globalization.CultureInfo.InvariantCulture));
			w.U32 ((uint)(24 + entries.Count * 8));
			var stored = StandardIndexBaseZero ? 0 : baseOffset;
			w.U16 (2);
			w.U8 (StandardIndexSubType);
			w.U8 (1);
			w.U32 ((uint)entries.Count);
			w.FourCC (ids[s]);
			w.U64 ((ulong)stored);
			w.U32 (0);
			foreach (var entry in entries) {
				w.U32 ((uint)(entry.Position + 8 - stored));
				w.U32 ((uint)entry.Size);
			}
		}

		return written;
	}

	const int SuperEntries = 4;

	static long SuperIndex (Writer w, string id)
	{
		w.FourCC ("indx");
		w.U32 (24 + SuperEntries * 16);
		var position = w.Position;
		w.U16 (4);
		w.U8 (0);
		w.U8 (0);
		w.U32 (0);
		w.FourCC (id);
		w.Zero (12 + SuperEntries * 16);
		return position;
	}

	void FillSuperIndex (byte[] bytes, long data, List<long> standard, bool video)
	{
		var entries = new List<long> (standard);
		if (video && DuplicateSuperIndexEntry && entries.Count > 0)
			entries.Add (entries[0]);
		if (video && SuperIndexType is { } type)
			bytes[data + 3] = type;
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan ((int)data + 4), (uint)entries.Count);
		for (var e = 0; e < entries.Count; e++) {
			var at = (int)data + 24 + e * 16;
			var size = BinaryPrimitives.ReadUInt32LittleEndian (bytes.AsSpan ((int)entries[e] + 4));
			BinaryPrimitives.WriteUInt64LittleEndian (bytes.AsSpan (at), (ulong)entries[e]);
			BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (at + 8), size + 8);
			BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (at + 12), 1);
		}
	}

	static void StreamHeader (Writer w, string type, string handler, uint scale, uint rate, uint length, uint sampleSize)
	{
		w.FourCC ("strh");
		w.U32 (56);
		w.FourCC (type);
		w.FourCC (handler);
		w.U32 (0);
		w.U32 (0);
		w.U32 (0);
		w.U32 (scale);
		w.U32 (rate);
		w.U32 (0);
		w.U32 (length);
		w.U32 (0);
		w.U32 (0xFFFFFFFF);
		w.U32 (sampleSize);
		w.Zero (8);
	}

	void InfoList (Writer w, List<(string Id, string Value)> fields)
	{
		var list = w.BeginList ("INFO");
		foreach (var (id, value) in fields)
			w.Chunk (id, InfoEncoding.GetBytes (value + "\0"));
		w.EndList (list);
	}

	/// <summary>
	/// Resolves every idx1 entry and every entry of every standard index a super index names,
	/// and returns each one's chunk id, size and sample bytes. Each target's header must match.
	/// </summary>
	public static List<string> ResolveIndexes (byte[] file)
	{
		var resolved = new List<string> ();
		var span = file.AsSpan ();
		var riffEnd = Math.Min (file.Length, 8 + (long)U32At (span, 4));
		long movi = -1;
		(long Position, uint Size)? hdrl = null;
		(long Position, uint Size)? idx1 = null;
		for (long p = 12; p + 8 <= riffEnd;) {
			var size = U32At (span, p + 4);
			var id = Id (span, p);
			if (id == "LIST" && Id (span, p + 8) == "movi" && movi < 0)
				movi = p;
			if (id == "LIST" && Id (span, p + 8) == "hdrl")
				hdrl = (p, size);
			if (id == "idx1")
				idx1 = (p, size);
			p += 8 + size + (size & 1);
		}

		if (idx1 is { } old) {
			var entries = old.Size / 16;
			var first = entries > 0 ? (long)U32At (span, old.Position + 16) : 0;
			var absolute = entries > 0 && Id (span, first) == Id (span, old.Position + 8);
			for (var i = 0; i < entries; i++) {
				var at = old.Position + 8 + i * 16;
				var id = Id (span, at);
				var offset = (long)U32At (span, at + 8);
				var size = U32At (span, at + 12);
				resolved.Add ("idx1 " + Resolve (span, absolute ? offset : movi + 8 + offset, id, size));
			}
		}

		if (hdrl is { } list) {
			foreach (var indx in Find (file, list.Position + 12, list.Position + 8 + list.Size, "indx")) {
				var count = U32At (span, indx + 4);
				for (var e = 0; e < count; e++) {
					var ix = (long)BinaryPrimitives.ReadUInt64LittleEndian (span.Slice ((int)(indx + 24 + e * 16)));
					var n = U32At (span, ix + 8 + 4);
					var id = Id (span, ix + 8 + 8);
					var baseOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian (span.Slice ((int)(ix + 8 + 12)));
					for (var k = 0; k < n; k++) {
						var offset = U32At (span, ix + 32 + k * 8);
						var size = U32At (span, ix + 32 + k * 8 + 4) & 0x7FFFFFFF;
						resolved.Add ("ix " + Resolve (span, baseOffset + offset - 8, id, size));
					}
				}
			}
		}

		return resolved;
	}

	static List<long> Find (byte[] file, long start, long end, string fourCC)
	{
		var found = new List<long> ();
		for (var p = start; p + 8 <= end;) {
			var size = U32At (file, p + 4);
			if (Id (file, p) == "LIST")
				found.AddRange (Find (file, p + 12, p + 8 + size, fourCC));
			else if (Id (file, p) == fourCC)
				found.Add (p + 8);
			p += 8 + size + (size & 1);
		}
		return found;
	}

	static string Resolve (ReadOnlySpan<byte> file, long position, string id, uint size)
	{
		if (position < 0 || position + 8 + size > file.Length || Id (file, position) != id || U32At (file, position + 4) != size)
			return $"{id} unresolved";
		var hash = SHA256.HashData (file.Slice ((int)position + 8, (int)size));
		return $"{id} {size} {Convert.ToHexString (hash, 0, 8)}";
	}

	static uint U32At (ReadOnlySpan<byte> data, long offset) => BinaryPrimitives.ReadUInt32LittleEndian (data.Slice ((int)offset));

	static string Id (ReadOnlySpan<byte> data, long offset) =>
		offset < 0 || offset + 4 > data.Length ? "" : Encoding.Latin1.GetString (data.Slice ((int)offset, 4));

	/// <summary>Offsets of every top-level chunk of every RIFF form, as "form/fourcc[listtype]@offset".</summary>
	public static List<(string Name, long Offset, uint Size)> TopLevel (byte[] file)
	{
		var chunks = new List<(string Name, long Offset, uint Size)> ();
		for (long form = 0; form + 12 <= file.Length;) {
			var formSize = U32At (file, form + 4);
			var end = Math.Min (file.Length, form + 8 + formSize);
			for (var p = form + 12; p + 8 <= end;) {
				var size = U32At (file, p + 4);
				var name = Id (file, p) == "LIST" ? "LIST " + Id (file, p + 8) : Id (file, p);
				chunks.Add ((name, p, size));
				p += 8 + size + (size & 1);
			}
			form += 8 + formSize + (formSize & 1);
		}
		return chunks;
	}

	sealed class Writer : IDisposable
	{
		readonly MemoryStream _stream = new ();

		public long Position => _stream.Position;

		public void FourCC (string id) => _stream.Write (Encoding.Latin1.GetBytes (id));

		public void U8 (byte value) => _stream.WriteByte (value);

		public void U16 (ushort value)
		{
			Span<byte> b = stackalloc byte[2];
			BinaryPrimitives.WriteUInt16LittleEndian (b, value);
			_stream.Write (b);
		}

		public void U32 (uint value)
		{
			Span<byte> b = stackalloc byte[4];
			BinaryPrimitives.WriteUInt32LittleEndian (b, value);
			_stream.Write (b);
		}

		public void U64 (ulong value)
		{
			Span<byte> b = stackalloc byte[8];
			BinaryPrimitives.WriteUInt64LittleEndian (b, value);
			_stream.Write (b);
		}

		public void Zero (int count) => _stream.Write (new byte[count]);

		public long Placeholder32 ()
		{
			var at = Position;
			U32 (0);
			return at;
		}

		public void Patch32 (long at, uint value)
		{
			var here = Position;
			_stream.Position = at;
			U32 (value);
			_stream.Position = here;
		}

		public long BeginList (string type)
		{
			var at = Position;
			FourCC ("LIST");
			U32 (0);
			FourCC (type);
			return at;
		}

		public void EndList (long at) => Patch32 (at + 4, (uint)(Position - at - 8));

		public void Chunk (string id, byte[] data)
		{
			FourCC (id);
			U32 ((uint)data.Length);
			_stream.Write (data);
			if ((data.Length & 1) != 0)
				U8 (0);
		}

		public byte[] ToArray () => _stream.ToArray ();

		public void Dispose () => _stream.Dispose ();
	}
}
