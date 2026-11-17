// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Tests;
using TagLibSharp2.Tests.Asf;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A directory scan must read tags and headers, not the audio or video payload.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ScanWithoutAudioTests
{
	const int PayloadBytes = 1024 * 1024;

	[TestMethod]
	public void Read_Mp4_SkipsMdat () => AssertScan (Mp4WithMdat (), "song.m4a");

	[TestMethod]
	public async Task ReadAsync_Mp4_SkipsMdat () => await AssertScanAsync (Mp4WithMdat (), "song.m4a");

	[TestMethod]
	public void Read_Matroska_SkipsClusters () => AssertScan (MatroskaWithCluster (), "song.mkv");

	[TestMethod]
	public async Task ReadAsync_Matroska_SkipsClusters () => await AssertScanAsync (MatroskaWithCluster (), "song.mkv");

	[TestMethod]
	public void Read_OggVorbis_SkipsAudioPages () => AssertScan (VorbisWithAudioPages (), "song.ogg", contiguous: false);

	[TestMethod]
	public async Task ReadAsync_OggVorbis_SkipsAudioPages () => await AssertScanAsync (VorbisWithAudioPages (), "song.ogg", contiguous: false);

	[TestMethod]
	public void Read_Opus_SkipsAudioPages () => AssertScan (OpusWithAudioPages (), "song.opus", contiguous: false);

	[TestMethod]
	public async Task ReadAsync_Opus_SkipsAudioPages () => await AssertScanAsync (OpusWithAudioPages (), "song.opus", contiguous: false);

	[TestMethod]
	public void Read_OggFlac_SkipsAudioPages () => AssertScan (OggFlacWithAudioPages (), "song.oga", contiguous: false);

	[TestMethod]
	public async Task ReadAsync_OggFlac_SkipsAudioPages () => await AssertScanAsync (OggFlacWithAudioPages (), "song.oga", contiguous: false);

	[TestMethod]
	public void Read_Aiff_SkipsSoundData () => AssertScan (AiffWithSound (), "song.aiff");

	[TestMethod]
	public async Task ReadAsync_Aiff_SkipsSoundData () => await AssertScanAsync (AiffWithSound (), "song.aiff");

	[TestMethod]
	public void Read_WavPack_SkipsBlocks () => AssertScan (InsertBeforeApe (TestBuilders.WavPack.CreateWithMetadata ("Window")), "song.wv");

	[TestMethod]
	public async Task ReadAsync_WavPack_SkipsBlocks () =>
		await AssertScanAsync (InsertBeforeApe (TestBuilders.WavPack.CreateWithMetadata ("Window")), "song.wv");

	[TestMethod]
	public void Read_MonkeysAudio_SkipsAudio () =>
		AssertScan (InsertBeforeApe (TestBuilders.MonkeysAudio.CreateWithMetadata ("Window")), "song.ape");

	[TestMethod]
	public async Task ReadAsync_MonkeysAudio_SkipsAudio () =>
		await AssertScanAsync (InsertBeforeApe (TestBuilders.MonkeysAudio.CreateWithMetadata ("Window")), "song.ape");

	[TestMethod]
	public void Read_Musepack_SkipsPackets () =>
		AssertScan (InsertBeforeApe (TestBuilders.Musepack.CreateWithMetadata ("Window")), "song.mpc");

	[TestMethod]
	public async Task ReadAsync_Musepack_SkipsPackets () =>
		await AssertScanAsync (InsertBeforeApe (TestBuilders.Musepack.CreateWithMetadata ("Window")), "song.mpc");

	[TestMethod]
	public void Read_Dsf_SkipsSampleData () => AssertScan (DsfWithSamples (), "song.dsf");

	[TestMethod]
	public async Task ReadAsync_Dsf_SkipsSampleData () => await AssertScanAsync (DsfWithSamples (), "song.dsf");

	[TestMethod]
	public void Read_Dff_SkipsSampleData () => AssertScan (DffWithSamples (), "song.dff");

	[TestMethod]
	public async Task ReadAsync_Dff_SkipsSampleData () => await AssertScanAsync (DffWithSamples (), "song.dff");

	[TestMethod]
	public void Read_Asf_SkipsDataObject () => AssertScan (AsfWithPackets (), "song.wma");

	[TestMethod]
	public async Task ReadAsync_Asf_SkipsDataObject () => await AssertScanAsync (AsfWithPackets (), "song.wma");

	static void AssertScan (byte[] file, string name, bool contiguous = true)
	{
		var loaded = MediaFile.ReadFromData (file, name);
		var files = new CountingFileSystem ();
		files.AddFile ("/" + name, file);

		var streamed = MediaFile.Read ("/" + name, files);

		AssertSame (loaded, streamed, files.BytesRead, file, contiguous);
		AssertSaved (streamed, files, "/" + name, file, contiguous);
		AssertStreamWithoutAPathDoesNotWrite (file, name);
	}

	static async Task AssertScanAsync (byte[] file, string name, bool contiguous = true)
	{
		var loaded = MediaFile.ReadFromData (file, name);
		var files = new CountingFileSystem ();
		files.AddFile ("/" + name, file);

		var streamed = await MediaFile.ReadAsync ("/" + name, files);

		AssertSame (loaded, streamed, files.BytesRead, file, contiguous);
		Assert.IsNotNull (streamed.File);
		streamed.Tag!.Title = "After";
		var saved = await SaveAsync (streamed.File, "/" + name, files);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		AssertSavedBytes (files.ReadAllBytes ("/" + name), file, contiguous);
		Assert.AreEqual ("After", MediaFile.ReadFromData (files.ReadAllBytes ("/" + name), name).Tag!.Title);
	}

	static void AssertSame (MediaFileResult loaded, MediaFileResult streamed, long bytesRead, byte[] file, bool contiguous)
	{
		Assert.IsTrue (loaded.IsSuccess, loaded.Error);
		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual ("Window", streamed.Tag?.Title, $"loaded='{loaded.Tag?.Title}' error='{streamed.Error}'");
		var full = Properties (loaded);
		var window = Properties (streamed);
		Assert.IsNotNull (full);
		Assert.IsNotNull (window);
		Assert.AreEqual (full!.Duration, window!.Duration);
		Assert.AreEqual (full.Bitrate, window.Bitrate);
		Assert.AreEqual (full.SampleRate, window.SampleRate);
		Assert.AreEqual (full.Channels, window.Channels);
		Assert.IsTrue (bytesRead < file.Length - 500_000, $"read {bytesRead} of {file.Length}");
		if (contiguous)
			Assert.IsTrue (IndexOf (file, Payload ()) >= 0);
	}

	static void AssertSaved (MediaFileResult streamed, CountingFileSystem files, string path, byte[] original, bool contiguous)
	{
		streamed.Tag!.Title = "After";
		var saved = Save (streamed.File!, path, files);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var written = files.ReadAllBytes (path);
		AssertSavedBytes (written, original, contiguous);
		Assert.AreEqual ("After", MediaFile.ReadFromData (written, path).Tag!.Title);
	}

	static void AssertSavedBytes (byte[] written, byte[] original, bool contiguous)
	{
		Assert.IsTrue (written.Length > original.Length / 2, $"saved {written.Length} of {original.Length}");
		if (contiguous) {
			Assert.IsTrue (IndexOf (written, Payload ()) >= 0, "payload bytes were not copied");
		} else {
			Assert.IsTrue (IndexOf (written, Marker ()) >= 0, "audio page body was not copied");
		}
	}

	static void AssertStreamWithoutAPathDoesNotWrite (byte[] file, string name)
	{
		var opened = MediaFile.Read (new MemoryStream (file), name);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var files = new MockFileSystem ();
		var saved = Save (opened.File!, "/orphan-" + name, files);
		Assert.IsFalse (saved.IsSuccess, saved.Error);
		Assert.IsFalse (files.FileExists ("/orphan-" + name));
	}

	static IMediaProperties? Properties (MediaFileResult result) =>
		((IMediaFile)result.File!).AudioProperties;

	static FileWriteResult Save (object file, string path, IFileSystem fileSystem) => file switch {
		Mp4File mp4 => mp4.SaveToFile (path, fileSystem),
		MatroskaFile matroska => matroska.SaveToFile (path, fileSystem),
		OggVorbisFile vorbis => vorbis.SaveToFile (path, fileSystem),
		OggOpusFile opus => opus.SaveToFile (path, fileSystem),
		OggFlacFile oggFlac => oggFlac.SaveToFile (path, fileSystem),
		AiffFile aiff => aiff.SaveToFile (path, fileSystem),
		WavPackFile wavPack => wavPack.SaveToFile (path, fileSystem),
		MonkeysAudioFile ape => ape.SaveToFile (path, fileSystem),
		MusepackFile musepack => musepack.SaveToFile (path, fileSystem),
		DsfFile dsf => dsf.SaveToFile (path, fileSystem),
		DffFile dff => dff.SaveToFile (path, fileSystem),
		AsfFile asf => asf.SaveToFile (path, fileSystem),
		_ => FileWriteResult.Failure ("Unsupported file " + file.GetType ().Name)
	};

	static Task<FileWriteResult> SaveAsync (object file, string path, IFileSystem fileSystem) => file switch {
		Mp4File mp4 => mp4.SaveToFileAsync (path, fileSystem),
		MatroskaFile matroska => matroska.SaveToFileAsync (path, fileSystem),
		OggVorbisFile vorbis => vorbis.SaveToFileAsync (path, fileSystem),
		OggOpusFile opus => opus.SaveToFileAsync (path, fileSystem),
		OggFlacFile oggFlac => oggFlac.SaveToFileAsync (path, fileSystem),
		AiffFile aiff => aiff.SaveToFileAsync (path, fileSystem),
		WavPackFile wavPack => wavPack.SaveToFileAsync (path, fileSystem),
		MonkeysAudioFile ape => ape.SaveToFileAsync (path, fileSystem),
		MusepackFile musepack => musepack.SaveToFileAsync (path, fileSystem),
		DsfFile dsf => dsf.SaveToFileAsync (path, fileSystem),
		DffFile dff => dff.SaveToFileAsync (path, fileSystem),
		AsfFile asf => asf.SaveToFileAsync (path, fileSystem),
		_ => Task.FromResult (FileWriteResult.Failure ("Unsupported file " + file.GetType ().Name))
	};

	static byte[] Payload ()
	{
		var payload = new byte[PayloadBytes];
		for (var i = 0; i < payload.Length; i++)
			payload[i] = (byte)(0xA5 ^ (i * 13));
		payload[0] = 0xFE;
		payload[1] = 0xED;
		payload[^2] = 0xBE;
		payload[^1] = 0xEF;
		return payload;
	}

	static byte[] Marker () => [0xFE, 0xED, 0xA5, 0x5A, 0x3C, 0xC3, 0x11, 0x22, 0x33, 0x44, 0xBE, 0xEF];

	static byte[] Mp4WithMdat ()
	{
		var small = TestBuilders.Mp4.CreateWithMetadata ("Window");
		var ftypSize = BinaryPrimitives.ReadInt32BigEndian (small);
		var ftyp = small.AsSpan (0, ftypSize).ToArray ();
		var moovSize = BinaryPrimitives.ReadInt32BigEndian (small.AsSpan (ftypSize));
		var moov = small.AsSpan (ftypSize, moovSize).ToArray ();
		return Concat (ftyp, Box ("mdat", Payload ()), moov);
	}

	static byte[] Box (string type, byte[] data)
	{
		var box = new byte[8 + data.Length];
		BinaryPrimitives.WriteUInt32BigEndian (box, (uint)box.Length);
		Encoding.ASCII.GetBytes (type).CopyTo (box, 4);
		data.CopyTo (box, 8);
		return box;
	}

	static byte[] MatroskaWithCluster ()
	{
		var duration = new byte[8];
		BinaryPrimitives.WriteDoubleBigEndian (duration, 1_000_000_000d);
		var rate = new byte[4];
		BinaryPrimitives.WriteSingleBigEndian (rate, 44100f);
		var info = Master (0x1549A966, Element (0x4489, duration));
		var track = Master (0x1654AE6B, Master (0xAE, Concat (
			Element (0x83, [2]),
			Element (0x86, "A_VORBIS"u8),
			Master (0xE1, Concat (Element (0xB5, rate), Element (0x9F, [2]))))));
		var tags = Master (0x1254C367, Master (0x7373, Concat (
			Master (0x63C0, Element (0x68CA, [30])),
			Master (0x67C8, Concat (
				Element (0x45A3, "TITLE"u8),
				Element (0x4487, "Window"u8))))));
		var cluster = Element (0x1F43B675, Payload ());
		var ebml = Master (0x1A45DFA3, Element (0x4282, "matroska"u8));
		return Concat (ebml, Element (0x18538067, Concat (info, track, tags, cluster)));
	}

	static byte[] VorbisWithAudioPages ()
	{
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2) { Title = "Window" };
		var head = TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateIdentificationPacket (), 0, OggPageFlags.BeginOfStream);
		var tags = TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateCommentPacket (comment), 1, OggPageFlags.None);
		var setup = TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateSetupPacket (), 2, OggPageFlags.None);
		return Concat (head, tags, setup, AudioPages (3, 44100));
	}

	static byte[] OpusWithAudioPages ()
	{
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2) { Title = "Window" };
		var head = TestBuilders.Ogg.CreatePage (
			TestBuilders.Opus.CreateOpusHeadPacket (), 0, OggPageFlags.BeginOfStream);
		var tags = TestBuilders.Ogg.CreatePage (
			TestBuilders.Opus.CreateOpusTagsPacket (comment), 1, OggPageFlags.None);
		return Concat (head, tags, AudioPages (2, 48000 + 312));
	}

	static byte[] OggFlacWithAudioPages ()
	{
		var comment = TestBuilders.OggFlac.CreateWithMetadata ("Window");
		// The builder's last page is EOS. Audio pages follow it; duration comes from STREAMINFO.
		return Concat (comment, AudioPages (2, 0, firstByte: 0xFF));
	}

	static byte[] AudioPages (uint sequence, ulong granule, byte firstByte = 0x11)
	{
		// 254 * 255 fits in one page (255 lacing values). 255 * 255 needs a 256th segment.
		var body = new byte[254 * 255];
		Array.Fill (body, (byte)0x11);
		body[0] = firstByte;
		Marker ().CopyTo (body, 10);
		using var pages = new MemoryStream ();
		for (var i = 0; i < 16; i++) {
			var flags = i == 15 ? OggPageFlags.EndOfStream : OggPageFlags.None;
			var page = TestBuilders.Ogg.CreatePage (body, sequence + (uint)i, flags);
			if (i == 15)
				BinaryPrimitives.WriteUInt64LittleEndian (page.AsSpan (6), granule);
			pages.Write (page);
		}

		return pages.ToArray ();
	}

	static byte[] AiffWithSound ()
	{
		var id3 = new Id3v2Tag { Title = "Window" }.Render ().ToArray ();
		using var body = new BinaryDataBuilder ();
		body.AddStringLatin1 ("COMM");
		body.AddUInt32BE (18);
		body.AddUInt16BE (1);
		body.AddUInt32BE (44100);
		body.AddUInt16BE (16);
		body.Add (new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0, 0, 0, 0, 0, 0 });
		body.AddStringLatin1 ("SSND");
		body.AddUInt32BE ((uint)(8 + PayloadBytes));
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		body.Add (Payload ());
		body.AddStringLatin1 ("ID3 ");
		body.AddUInt32BE ((uint)id3.Length);
		body.Add (id3);
		if ((id3.Length & 1) == 1)
			body.Add (0);
		var chunks = body.ToBinaryData ();
		using var file = new BinaryDataBuilder ();
		file.AddStringLatin1 ("FORM");
		file.AddUInt32BE ((uint)(4 + chunks.Length));
		file.AddStringLatin1 ("AIFF");
		file.Add (chunks);
		return file.ToBinaryData ().ToArray ();
	}

	static byte[] InsertBeforeApe (byte[] file)
	{
		var at = IndexOf (file, "APETAGEX"u8);
		Assert.IsTrue (at > 0);
		var payload = Payload ();
		var result = new byte[file.Length + payload.Length];
		Buffer.BlockCopy (file, 0, result, 0, at);
		Buffer.BlockCopy (payload, 0, result, at, payload.Length);
		Buffer.BlockCopy (file, at, result, at + payload.Length, file.Length - at);
		return result;
	}

	static byte[] DsfWithSamples ()
	{
		var file = TestBuilders.Dsf.CreateWithMetadata ("Window");
		var meta = (int)BinaryPrimitives.ReadUInt64LittleEndian (file.AsSpan (20));
		var payload = Payload ();
		var result = new byte[file.Length + payload.Length];
		Buffer.BlockCopy (file, 0, result, 0, meta);
		Buffer.BlockCopy (payload, 0, result, meta, payload.Length);
		Buffer.BlockCopy (file, meta, result, meta + payload.Length, file.Length - meta);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (12), (ulong)result.Length);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (20), (ulong)(meta + payload.Length));
		var dataSize = BinaryPrimitives.ReadUInt64LittleEndian (file.AsSpan (84));
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (84), dataSize + (ulong)payload.Length);
		return result;
	}

	static byte[] DffWithSamples ()
	{
		var file = TestBuilders.Dff.CreateWithId3v2 ("Window");
		// Form type and CMPR also contain "DSD ". The audio chunk is the last one.
		var at = LastIndexOf (file, "DSD "u8);
		Assert.IsTrue (at > 16);
		var size = BinaryPrimitives.ReadUInt64BigEndian (file.AsSpan (at + 4));
		var dataStart = at + 12;
		var payload = Payload ();
		var result = new byte[file.Length + payload.Length];
		Buffer.BlockCopy (file, 0, result, 0, dataStart);
		Buffer.BlockCopy (payload, 0, result, dataStart, payload.Length);
		Buffer.BlockCopy (file, dataStart, result, dataStart + payload.Length, file.Length - dataStart);
		BinaryPrimitives.WriteUInt64BigEndian (result.AsSpan (at + 4), size + (ulong)payload.Length);
		var form = BinaryPrimitives.ReadUInt64BigEndian (result.AsSpan (4));
		BinaryPrimitives.WriteUInt64BigEndian (result.AsSpan (4), form + (ulong)payload.Length);
		return result;
	}

	static byte[] AsfWithPackets ()
	{
		var file = AsfTestBuilder.CreateMinimalWma ("Window");
		var headerSize = (int)BinaryPrimitives.ReadUInt64LittleEndian (file.AsSpan (16));
		var payload = Payload ();
		var result = new byte[file.Length + payload.Length];
		Buffer.BlockCopy (file, 0, result, 0, file.Length);
		Buffer.BlockCopy (payload, 0, result, file.Length, payload.Length);
		var dataSize = BinaryPrimitives.ReadUInt64LittleEndian (result.AsSpan (headerSize + 16));
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (headerSize + 16), dataSize + (ulong)payload.Length);
		return result;
	}

	static byte[] Element (ulong id, ReadOnlySpan<byte> payload)
	{
		var idBytes = Id (id);
		var size = EbmlSize (payload.Length);
		var result = new byte[idBytes.Length + size.Length + payload.Length];
		idBytes.CopyTo (result, 0);
		size.CopyTo (result, idBytes.Length);
		payload.CopyTo (result.AsSpan (idBytes.Length + size.Length));
		return result;
	}

	static byte[] Master (ulong id, params byte[][] children) => Element (id, Concat (children));

	static byte[] EbmlSize (int length)
	{
		if (length < 0x7F)
			return [(byte)(0x80 | length)];
		if (length < 0x3FFF)
			return [(byte)(0x40 | (length >> 8)), (byte)length];
		if (length < 0x1F_FFFF)
			return [(byte)(0x20 | (length >> 16)), (byte)(length >> 8), (byte)length];
		return [(byte)(0x10 | (length >> 24)), (byte)(length >> 16), (byte)(length >> 8), (byte)length];
	}

	static byte[] Id (ulong id)
	{
		var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		var bytes = new byte[width];
		for (var i = width - 1; i >= 0; i--) {
			bytes[i] = (byte)id;
			id >>= 8;
		}

		return bytes;
	}

	static byte[] Concat (params byte[][] parts)
	{
		var length = 0;
		foreach (var part in parts)
			length += part.Length;
		var result = new byte[length];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}

		return result;
	}

	static int LastIndexOf (byte[] haystack, ReadOnlySpan<byte> needle)
	{
		if (needle.Length == 0 || needle.Length > haystack.Length)
			return -1;
		for (var i = haystack.Length - needle.Length; i >= 0; i--) {
			if (haystack[i] == needle[0] && haystack.AsSpan (i, needle.Length).SequenceEqual (needle))
				return i;
		}

		return -1;
	}

	static int IndexOf (byte[] haystack, ReadOnlySpan<byte> needle)
	{
		if (needle.Length == 0 || needle.Length > haystack.Length)
			return -1;
		for (var i = 0; i <= haystack.Length - needle.Length; i++) {
			if (haystack[i] == needle[0] && haystack.AsSpan (i, needle.Length).SequenceEqual (needle))
				return i;
		}

		return -1;
	}

	sealed class CountingFileSystem : IFileSystem
	{
		readonly MockFileSystem _inner = new ();
		long _bytesRead;

		public long BytesRead => _bytesRead;

		public void AddFile (string path, byte[] data) => _inner.AddFile (path, data);

		public bool FileExists (string path) => _inner.FileExists (path);

		public Stream OpenRead (string path) => new CountingStream (_inner.OpenRead (path), read => _bytesRead += read);

		public Stream OpenReadWrite (string path) => _inner.OpenReadWrite (path);

		public Stream Create (string path) => _inner.Create (path);

		public byte[] ReadAllBytes (string path) => _inner.ReadAllBytes (path);

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
			_inner.ReadAllBytesAsync (path, cancellationToken);

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => _inner.WriteAllBytes (path, data);

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			_inner.WriteAllBytesAsync (path, data, cancellationToken);

		public void Delete (string path) => _inner.Delete (path);

		public void Move (string sourcePath, string destinationPath) => _inner.Move (sourcePath, destinationPath);

		public string? GetDirectoryName (string path) => _inner.GetDirectoryName (path);

		public string GetFileName (string path) => _inner.GetFileName (path);

		public string CombinePath (string path1, string path2) => _inner.CombinePath (path1, path2);
	}

	sealed class CountingStream : Stream
	{
		readonly Stream _inner;
		readonly Action<int> _count;

		public CountingStream (Stream inner, Action<int> count)
		{
			_inner = inner;
			_count = count;
		}

		public override bool CanRead => _inner.CanRead;

		public override bool CanSeek => _inner.CanSeek;

		public override bool CanWrite => false;

		public override long Length => _inner.Length;

		public override long Position {
			get => _inner.Position;
			set => _inner.Position = value;
		}

		public override void Flush () => _inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count)
		{
			var read = _inner.Read (buffer, offset, count);
			_count (read);
			return read;
		}

		public override int Read (Span<byte> buffer)
		{
			var read = _inner.Read (buffer);
			_count (read);
			return read;
		}

		public override ValueTask<int> ReadAsync (Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			var read = _inner.Read (buffer.Span);
			_count (read);
			return new ValueTask<int> (read);
		}

		public override long Seek (long offset, SeekOrigin origin) => _inner.Seek (offset, origin);

		public override void SetLength (long value) => throw new NotSupportedException ();

		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}
}
