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
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Tests;
using TagLibSharp2.Tests.Asf;
using TagLibSharp2.WavPack;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class WindowedEdgeTests
{
	[TestMethod]
	[DataRow ("song.m4a")]
	[DataRow ("song.mkv")]
	[DataRow ("song.ogg")]
	[DataRow ("song.opus")]
	[DataRow ("song.oga")]
	[DataRow ("song.aiff")]
	[DataRow ("song.wv")]
	[DataRow ("song.ape")]
	[DataRow ("song.mpc")]
	[DataRow ("song.dsf")]
	[DataRow ("song.dff")]
	[DataRow ("song.wma")]
	public void Read_ExtensionWithNoHeader_Fails (string name)
	{
		var opened = MediaFile.Read (new MemoryStream (new byte[] { 1, 2 }), name);

		Assert.IsFalse (opened.IsSuccess, name);
		Assert.IsFalse (string.IsNullOrEmpty (opened.Error), name);
	}

	[TestMethod]
	public void Read_MissingDirectory_ReportsTheFileAsMissing ()
	{
		var files = new MissingDirectory ();
		var opened = MediaFile.Read ("/gone/song.mp3", files);

		Assert.IsFalse (opened.IsSuccess);
		Assert.AreEqual ("File not found: /gone/song.mp3", opened.Error);
	}

	[TestMethod]
	public async Task ReadAsync_MissingDirectory_ReportsTheFileAsMissing ()
	{
		var files = new MissingDirectory ();
		var opened = await MediaFile.ReadAsync ("/gone/song.mp3", files);

		Assert.IsFalse (opened.IsSuccess);
		Assert.AreEqual ("File not found: /gone/song.mp3", opened.Error);
	}

	[TestMethod]
	public void Skip_NonPositiveNonSeekableAndPastTheEnd_DoesNotReadPastTheBuffer ()
	{
		var stream = new MemoryStream (new byte[] { 1, 2, 3, 4 }) { Position = 1 };
		StreamBytes.Skip (stream, 0);
		StreamBytes.Skip (stream, -4);
		Assert.AreEqual (1, stream.Position);
		StreamBytes.Skip (stream, 100);
		Assert.AreEqual (4, stream.Position);

		var copied = new NonSeekableStream (new MemoryStream (new byte[] { 9, 8 }));
		StreamBytes.Skip (copied, 5);
		var buffer = new byte[2];
		Assert.AreEqual (2, copied.Read (buffer, 0, 2));
		CollectionAssert.AreEqual (new byte[] { 9, 8 }, buffer);
	}

	[TestMethod]
	public void Read_NonSeekableCopy_MatchesTheSeekableRead ()
	{
		SameWhenCopied (TestBuilders.Aiff.CreateMinimal (), "song.aiff");
		SameWhenCopied (TestBuilders.WavPack.CreateMinimal (), "song.wv");
		SameWhenCopied (TestBuilders.MonkeysAudio.CreateMinimal (), "song.ape");
		SameWhenCopied (TestBuilders.Musepack.CreateMinimal (), "song.mpc");
		SameWhenCopied (TestBuilders.Dsf.CreateMinimal (), "song.dsf");
		SameWhenCopied (TestBuilders.Dff.CreateMinimal (), "song.dff");
		SameWhenCopied (TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac), "song.m4a");
		SameWhenCopied (TestBuilders.Ogg.CreateMinimalFile (), "song.ogg");
		SameWhenCopied (TestBuilders.Opus.CreateMinimalFile (), "song.opus");
		SameWhenCopied (TestBuilders.OggFlac.CreateMinimal (), "song.oga");
		SameWhenCopied (AsfTestBuilder.CreateMinimalWma ("Window"), "song.wma");
		SameWhenCopied (MatroskaAlbum (), "song.mkv");
	}

	[TestMethod]
	public void Read_WithASourcePath_StillParsesANonSeekableCopy ()
	{
		var files = new MockFileSystem ();
		AssertCopied (AiffFile.Read (Copy (TestBuilders.Aiff.CreateMinimal ()), "/song.aiff", files));
		AssertCopied (WavPackFile.Read (Copy (TestBuilders.WavPack.CreateMinimal ()), "/song.wv", files));
		AssertCopied (MonkeysAudioFile.Read (Copy (TestBuilders.MonkeysAudio.CreateMinimal ()), "/song.ape", files));
		AssertCopied (MusepackFile.Read (Copy (TestBuilders.Musepack.CreateMinimal ()), "/song.mpc", files));
		AssertCopied (DsfFile.Read (Copy (TestBuilders.Dsf.CreateMinimal ()), "/song.dsf", files));
		AssertCopied (DffFile.Read (Copy (TestBuilders.Dff.CreateMinimal ()), "/song.dff", files));
		AssertCopied (Mp4File.Read (Copy (TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac)), "/song.m4a", files));
		AssertCopied (OggVorbisFile.Read (Copy (TestBuilders.Ogg.CreateMinimalFile ()), "/song.ogg", files));
		AssertCopied (OggOpusFile.Read (Copy (TestBuilders.Opus.CreateMinimalFile ()), "/song.opus", files));
		AssertCopied (OggFlacFile.Read (Copy (TestBuilders.OggFlac.CreateMinimal ()), "/song.oga", files));
		AssertCopied (AsfFile.Read (Copy (AsfTestBuilder.CreateMinimalWma ("Window")), "/song.wma", files));
		AssertCopied (MatroskaFile.Read (Copy (MatroskaAlbum ()), "/song.mkv", files));
	}

	[TestMethod]
	public async Task Save_WithoutBytesOrASourcePath_FailsClosed ()
	{
		await ExpectNoSource (WavPackFile.Read (Copy (TestBuilders.WavPack.CreateMinimal ()), "", null).File!, static file => file.SaveToFileAsync ("/copy.wv"));
		await ExpectNoSource (MonkeysAudioFile.Read (Copy (TestBuilders.MonkeysAudio.CreateMinimal ()), "", null).File!, static file => file.SaveToFileAsync ("/copy.ape"));
		await ExpectNoSource (MusepackFile.Read (Copy (TestBuilders.Musepack.CreateMinimal ()), "", null).File!, static file => file.SaveToFileAsync ("/copy.mpc"));
		await ExpectNoSource (AsfFile.Read (Copy (AsfTestBuilder.CreateMinimalWma ("Window")), "", null).File!, static file => file.SaveToFileAsync ("/copy.wma"));
		await ExpectNoSource (DffFile.Read (Copy (TestBuilders.Dff.CreateMinimal ()), "", null).File!, static file => file.SaveToFileAsync ("/copy.dff"));
	}

	[TestMethod]
	public async Task Save_WhenTheSourceDisappears_ReportsTheRereadFailure ()
	{
		var files = new MockFileSystem ();
		await ExpectReread (WavPackFile.Read (AtStart (TestBuilders.WavPack.CreateMinimal ()), "/song.wv", files).File!, static file => file.SaveToFile ("/copy.wv"), static file => file.SaveToFileAsync ("/copy.wv"));
		await ExpectReread (MonkeysAudioFile.Read (AtStart (TestBuilders.MonkeysAudio.CreateMinimal ()), "/song.ape", files).File!, static file => file.SaveToFile ("/copy.ape"), static file => file.SaveToFileAsync ("/copy.ape"));
		await ExpectReread (MusepackFile.Read (AtStart (TestBuilders.Musepack.CreateMinimal ()), "/song.mpc", files).File!, static file => file.SaveToFile ("/copy.mpc"), static file => file.SaveToFileAsync ("/copy.mpc"));
		await ExpectReread (AsfFile.Read (AtStart (AsfTestBuilder.CreateMinimalWma ("Window")), "/song.wma", files).File!, static file => file.SaveToFile ("/copy.wma"), static file => file.SaveToFileAsync ("/copy.wma"));
		await ExpectReread (OggFlacFile.Read (AtStart (TestBuilders.OggFlac.CreateMinimal ()), "/song.oga", files).File!, static file => file.SaveToFile ("/copy.oga"), static file => file.SaveToFileAsync ("/copy.oga"));
		await ExpectReread (DffFile.Read (AtStart (TestBuilders.Dff.CreateMinimal ()), "/song.dff", files).File!, static file => file.SaveToFile ("/copy.dff"), static file => file.SaveToFileAsync ("/copy.dff"));
		await ExpectReread (Mp4File.Read (AtStart (TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac)), "/song.m4a", files).File!, static file => file.SaveToFile ("/copy.m4a"), static file => file.SaveToFileAsync ("/copy.m4a"));
		await ExpectReread (DsfFile.Read (AtStart (TestBuilders.Dsf.CreateMinimal ()), "/song.dsf", files).File!, static file => file.SaveToFile ("/copy.dsf"), static file => file.SaveToFileAsync ("/copy.dsf"));
		await ExpectReread (MatroskaFile.Read (AtStart (MatroskaAlbum ()), "/song.mkv", files).File!, static file => file.SaveToFile ("/copy.mkv"), static file => file.SaveToFileAsync ("/copy.mkv"));
	}

	[TestMethod]
	public void Read_WavPackBlockLargerThanTheMetadataWindow_KeepsSmallSubBlocks ()
	{
		var body = new byte[32 + 4 + 42 + 1];
		"wvpk"u8.CopyTo (body);
		BinaryPrimitives.WriteUInt32LittleEndian (body.AsSpan (4), 70_000);
		BinaryPrimitives.WriteUInt16LittleEndian (body.AsSpan (8), 0x0410);
		body[32] = 0x01;
		body[33] = 1;
		body[34] = 0x11;
		body[35] = 0x22;
		body[36] = 0x0A;
		body[37] = 20;
		body[78] = 0x02;
		using var stream = new ReportedStream (body, 8 + 70_000);

		var opened = WavPackFile.Read (stream, "/song.wv", new MockFileSystem ());

		Assert.IsTrue (opened.IsSuccess, opened.Error);
	}

	[TestMethod]
	public void Read_OldAndOversizedApeHeaders_FollowTheVersion ()
	{
		var old = MonkeysAudioFile.Read (AtStart (TestBuilders.MonkeysAudio.CreateMinimal (version: 3800)), "/old.ape", null);
		Assert.IsTrue (old.IsSuccess, old.Error);

		var truncated = new byte[10];
		"MAC "u8.CopyTo (truncated);
		BinaryPrimitives.WriteUInt16LittleEndian (truncated.AsSpan (4), 3990);
		var shortDescriptor = MonkeysAudioFile.Read (AtStart (truncated), null, null);
		Assert.IsFalse (shortDescriptor.IsSuccess);
		StringAssert.Contains (shortDescriptor.Error, "truncated descriptor");

		var huge = new byte[16];
		"MAC "u8.CopyTo (huge);
		BinaryPrimitives.WriteUInt16LittleEndian (huge.AsSpan (4), 3990);
		BinaryPrimitives.WriteUInt32LittleEndian (huge.AsSpan (8), 2_000_000);
		var oversized = MonkeysAudioFile.Read (AtStart (huge), null, null);
		Assert.IsFalse (oversized.IsSuccess);
		StringAssert.Contains (oversized.Error, "too large");
	}

	[TestMethod]
	public void ApeTail_RejectsAShortGarbageOrOversizedFooter ()
	{
		Assert.IsFalse (ApeTagTail.TryRead (new MemoryStream (new byte[8]), out _, out _));
		Assert.IsFalse (ApeTagTail.TryRead (new MemoryStream (new byte[40]), out _, out _));

		var footer = new byte[32];
		"APETAGEX"u8.CopyTo (footer);
		BinaryPrimitives.WriteUInt32LittleEndian (footer.AsSpan (8), 2000);
		BinaryPrimitives.WriteUInt32LittleEndian (footer.AsSpan (12), uint.MaxValue);
		Assert.IsFalse (ApeTagTail.TryRead (new MemoryStream (footer), out _, out _));

		BinaryPrimitives.WriteUInt32LittleEndian (footer.AsSpan (12), 32);
		BinaryPrimitives.WriteUInt32LittleEndian (footer.AsSpan (20), 0x20000000);
		Assert.IsFalse (ApeTagTail.TryRead (new MemoryStream (footer), out _, out _));
	}

	[TestMethod]
	public void Read_AiffFormThatIsNotAudio_Fails ()
	{
		var bytes = new byte[12];
		"FORM"u8.CopyTo (bytes);
		"WAVE"u8.CopyTo (bytes.AsSpan (8));

		var opened = AiffFile.Read (AtStart (bytes), null, null);

		Assert.IsFalse (opened.IsSuccess);
		StringAssert.Contains (opened.Error, "form type");
	}

	[TestMethod]
	public void Read_AsfHeaderSizeOutOfRange_Fails ()
	{
		var head = new byte[30];
		new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C }.CopyTo (head, 0);
		BinaryPrimitives.WriteUInt64LittleEndian (head.AsSpan (16), 10);

		var opened = AsfFile.Read (AtStart (head), null, null);

		Assert.IsFalse (opened.IsSuccess);
		StringAssert.Contains (opened.Error, "out of range");
	}

	[TestMethod]
	public void Read_Mp4SizeForms_StopOrMeasureTheBox ()
	{
		Assert.IsNotNull (Mp4File.Read (AtStart (Box (4, "ftyp")), null, null).Error);
		Assert.IsNotNull (Mp4File.Read (AtStart (Box (1, "ftyp")), null, null).Error);

		var shortExtended = new byte[16];
		BinaryPrimitives.WriteUInt32BigEndian (shortExtended, 1);
		"ftyp"u8.CopyTo (shortExtended.AsSpan (4));
		BinaryPrimitives.WriteUInt64BigEndian (shortExtended.AsSpan (8), 8);
		Assert.IsNotNull (Mp4File.Read (AtStart (shortExtended), null, null).Error);

		var toEnd = new byte[16];
		"free"u8.CopyTo (toEnd.AsSpan (4));
		Assert.IsNotNull (Mp4File.Read (AtStart (toEnd), null, null).Error);

		var extended = new byte[32];
		BinaryPrimitives.WriteUInt32BigEndian (extended, 1);
		"free"u8.CopyTo (extended.AsSpan (4));
		BinaryPrimitives.WriteUInt64BigEndian (extended.AsSpan (8), 32);
		Assert.IsNotNull (Mp4File.Read (AtStart (extended), null, null).Error);
	}

	[TestMethod]
	public void Read_MatroskaWithoutASegment_Fails ()
	{
		var unknown = MatroskaFile.Read (AtStart (new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0xFF }), null, null);
		Assert.IsFalse (unknown.IsSuccess);
		StringAssert.Contains (unknown.Error, "unknown");

		var none = MatroskaFile.Read (AtStart (new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x81, 0x00 }), null, null);
		Assert.IsFalse (none.IsSuccess);
		StringAssert.Contains (none.Error, "no Segment");

		var oversized = new byte[] { 0x18, 0x53, 0x80, 0x67, 0x43, 0xE8 };
		var clamped = MatroskaFile.Read (AtStart (oversized), null, null);
		Assert.IsFalse (clamped.IsSuccess);
		StringAssert.Contains (clamped.Error, "EBML");
	}

	[TestMethod]
	public void Ebml_HeaderShorterThanAnIdOrASize_IsNotAnElement ()
	{
		Assert.IsFalse (EbmlReader.TryPeekHeader (ReadOnlySpan<byte>.Empty, out _, out _, out _));
		Assert.IsFalse (EbmlReader.TryPeekHeader (new byte[] { 0x80 }, out _, out _, out _));
	}

	[TestMethod]
	public void Read_DsfMetadataAndDataThatDisagree_Fail ()
	{
		var pastEnd = TestBuilders.Dsf.CreateMinimal ();
		BinaryPrimitives.WriteUInt64LittleEndian (pastEnd.AsSpan (20), (ulong)pastEnd.Length + 50);
		var metadata = DsfFile.Read (AtStart (pastEnd), null, null);
		Assert.IsFalse (metadata.IsSuccess);
		StringAssert.Contains (metadata.Error, "Metadata offset");

		var overlap = TestBuilders.Dsf.CreateMinimal ();
		BinaryPrimitives.WriteUInt64LittleEndian (overlap.AsSpan (20), 90);
		var crossed = DsfFile.Read (AtStart (overlap), null, null);
		Assert.IsFalse (crossed.IsSuccess);
		StringAssert.Contains (crossed.Error, "metadata");

		var inflated = TestBuilders.Dsf.CreateMinimal ();
		BinaryPrimitives.WriteUInt64LittleEndian (inflated.AsSpan (84), 50_000);
		var data = DsfFile.Read (AtStart (inflated), null, null);
		Assert.IsFalse (data.IsSuccess);
		Assert.IsFalse (string.IsNullOrEmpty (data.Error));
	}

	[TestMethod]
	public void Read_DffChunkPastTheEnd_Stops ()
	{
		var bytes = new byte[28];
		"FRM8"u8.CopyTo (bytes);
		"DSD "u8.CopyTo (bytes.AsSpan (12));
		"PROP"u8.CopyTo (bytes.AsSpan (16));
		BinaryPrimitives.WriteUInt64BigEndian (bytes.AsSpan (20), 1000);

		var opened = DffFile.Read (AtStart (bytes), "/song.dff", new MockFileSystem ());

		Assert.IsFalse (opened.IsSuccess);
		StringAssert.Contains (opened.Error, "FVER");
	}

	[TestMethod]
	public void Read_MusepackSv7_ReadsTheFixedHeader ()
	{
		var bytes = new byte[32];
		"MP+"u8.CopyTo (bytes);

		var opened = MusepackFile.Read (AtStart (bytes), null, null);

		Assert.IsFalse (opened.IsSuccess);
		StringAssert.Contains (opened.Error, "unsupported SV7 version");
	}

	[TestMethod]
	public void OggWindow_ContinuedAndTruncatedPages_Stop ()
	{
		var continued = Concat (
			OggPage (0x00, new byte[] { 255 }, new byte[255]),
			OggPage (0x01, new byte[] { 10 }, new byte[10]),
			OggPage (0x00, new byte[] { 4 }, new byte[4]));
		using var stream = new MemoryStream (continued) { Position = 2 };
		var headers = OggWindow.ReadHeaders (stream, OggHeaderKind.Vorbis);
		Assert.IsTrue (headers.Length > 0);

		Assert.IsEmpty (OggWindow.ReadLastPage (new MemoryStream (new byte[8])));
		Assert.IsEmpty (OggWindow.ReadLastPage (new MemoryStream (new byte[40])));

		var cut = new byte[27];
		"OggS"u8.CopyTo (cut);
		cut[26] = 5;
		Assert.IsEmpty (OggWindow.ReadHeaders (AtStart (cut), OggHeaderKind.Opus));

		var tail = new byte[30];
		"OggS"u8.CopyTo (tail.AsSpan (20));
		Assert.IsEmpty (OggWindow.ReadLastPage (AtStart (tail)));
	}

	static void SameWhenCopied (byte[] file, string name)
	{
		var direct = MediaFile.Read (new MemoryStream (file) { Position = 3 }, name);
		var copied = MediaFile.Read (new NonSeekableStream (new MemoryStream (file)), name);
		Assert.IsTrue (direct.IsSuccess, name + " " + direct.Error);
		Assert.IsTrue (copied.IsSuccess, name + " " + copied.Error);
		Assert.AreEqual (direct.Tag?.Title, copied.Tag?.Title, name);
		var left = ((IMediaFile)direct.File!).AudioProperties?.Duration;
		var right = ((IMediaFile)copied.File!).AudioProperties?.Duration;
		Assert.AreEqual (left, right, name);
	}

	static void AssertCopied (AiffFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (WavPackFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (MonkeysAudioFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (MusepackFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (DsfFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (DffFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (Mp4FileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (OggVorbisFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (OggOpusFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (OggFlacFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (AsfFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static void AssertCopied (MatroskaFileReadResult result) => Assert.IsTrue (result.IsSuccess, result.Error);

	static async Task ExpectNoSource<T> (T? file, Func<T, Task<FileWriteResult>> save)
		where T : class
	{
		Assert.IsNotNull (file);
		var saved = await save (file);
		Assert.IsFalse (saved.IsSuccess, saved.Error);
		StringAssert.Contains (saved.Error, "No source path");
	}

	static async Task ExpectReread<T> (T? file, Func<T, FileWriteResult> save, Func<T, Task<FileWriteResult>> saveAsync)
		where T : class
	{
		Assert.IsNotNull (file);
		var sync = save (file);
		Assert.IsFalse (sync.IsSuccess, sync.Error);
		StringAssert.Contains (sync.Error, "re-read");
		var async = await saveAsync (file);
		Assert.IsFalse (async.IsSuccess, async.Error);
		StringAssert.Contains (async.Error, "re-read");
	}

	static MemoryStream AtStart (byte[] data) => new (data);

	static NonSeekableStream Copy (byte[] data) => new (new MemoryStream (data));

	static byte[] Box (uint size, string type)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian (bytes, size);
		Encoding.ASCII.GetBytes (type).CopyTo (bytes, 4);
		return bytes;
	}

	static byte[] MatroskaAlbum ()
	{
		var title = Element (0x67C8, Concat (Element (0x45A3, "TITLE"u8), Element (0x4487, "Album"u8)));
		var targets = Element (0x63C0, Element (0x68CA, new byte[] { 50 }));
		var tag = Element (0x7373, Concat (targets, title));
		var tags = Element (0x1254C367, tag);
		var segment = Element (0x18538067, tags);
		var ebml = Element (0x1A45DFA3, Element (0x4282, "matroska"u8));
		return Concat (ebml, segment);
	}

	static byte[] Element (ulong id, ReadOnlySpan<byte> payload)
	{
		var size = payload.Length < 0x7F
			? new[] { (byte)(0x80 | payload.Length) }
			: new[] { (byte)(0x40 | (payload.Length >> 8)), (byte)payload.Length };
		var idBytes = new byte[4];
		for (var i = 3; i >= 0; i--) {
			idBytes[i] = (byte)id;
			id >>= 8;
		}

		var width = idBytes[0] == 0 ? idBytes[1] == 0 ? idBytes[2] == 0 ? 1 : 2 : 3 : 4;
		var result = new byte[width + size.Length + payload.Length];
		idBytes.AsSpan (4 - width).CopyTo (result);
		size.CopyTo (result, width);
		payload.CopyTo (result.AsSpan (width + size.Length));
		return result;
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

	static byte[] OggPage (byte flags, byte[] segments, byte[] body)
	{
		var page = new byte[27 + segments.Length + body.Length];
		"OggS"u8.CopyTo (page);
		page[5] = flags;
		page[26] = (byte)segments.Length;
		segments.CopyTo (page, 27);
		body.CopyTo (page, 27 + segments.Length);
		return page;
	}

	sealed class ReportedStream : MemoryStream
	{
		readonly long _length;

		public ReportedStream (byte[] content, long length) : base (content) => _length = length;

		public override long Length => _length;
	}

	sealed class NonSeekableStream : Stream
	{
		readonly Stream _inner;

		public NonSeekableStream (Stream inner) => _inner = inner;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException ();

		public override long Position {
			get => throw new NotSupportedException ();
			set => throw new NotSupportedException ();
		}

		public override void Flush () { }

		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);

		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();

		public override void SetLength (long value) => throw new NotSupportedException ();

		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}

	sealed class MissingDirectory : IFileSystem
	{
		public bool FileExists (string path) => true;

		public Stream OpenRead (string path) => throw new DirectoryNotFoundException (path);

		public Stream OpenReadWrite (string path) => throw new DirectoryNotFoundException (path);

		public Stream Create (string path) => throw new DirectoryNotFoundException (path);

		public byte[] ReadAllBytes (string path) => throw new DirectoryNotFoundException (path);

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
			throw new DirectoryNotFoundException (path);

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => throw new DirectoryNotFoundException (path);

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			throw new DirectoryNotFoundException (path);

		public void Delete (string path) => throw new DirectoryNotFoundException (path);

		public void Move (string sourcePath, string destinationPath) => throw new DirectoryNotFoundException (sourcePath);

		public string? GetDirectoryName (string path) => "/gone";

		public string GetFileName (string path) => "song.mp3";

		public string CombinePath (string path1, string path2) => path1 + path2;
	}
}
