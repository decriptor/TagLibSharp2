// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Riff;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class WindowedReadEdgeTests
{
	[TestMethod]
	public void Read_OpenThrows_ReturnsTheIoFailure ()
	{
		var files = new ThrowingFileSystem (new IOException ("disk"));
		var result = MediaFile.Read ("/song.mp3", files);
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Failed to read file: disk", result.Error);
	}

	[TestMethod]
	public void Read_OpenDenied_ReturnsAccessDenied ()
	{
		var files = new ThrowingFileSystem (new UnauthorizedAccessException ("no"));
		var result = MediaFile.Read ("/song.mp3", files);
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Access denied: no", result.Error);
	}

	[TestMethod]
	public async Task ReadAsync_OpenThrows_ReturnsTheIoFailure ()
	{
		var files = new ThrowingFileSystem (new IOException ("disk"));
		var result = await MediaFile.ReadAsync ("/song.mp3", files);
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Failed to read file: disk", result.Error);
	}

	[TestMethod]
	public async Task ReadAsync_OpenDenied_ReturnsAccessDenied ()
	{
		var files = new ThrowingFileSystem (new UnauthorizedAccessException ("no"));
		var result = await MediaFile.ReadAsync ("/song.mp3", files);
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Access denied: no", result.Error);
	}

	[TestMethod]
	public void Read_StreamNotAtTheStart_RewindsBeforeDetecting ()
	{
		var bytes = TinyMp3 (200);
		var stream = new MemoryStream (bytes) { Position = 8 };
		var result = MediaFile.Read (stream, "song.mp3");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (Duration (result) > TimeSpan.Zero);
	}

	[TestMethod]
	public async Task ReadAsync_UnrecognizedBytes_CopiesTheStream ()
	{
		var stream = new MemoryStream (new byte[16]) { Position = 4 };
		var result = await MediaFile.ReadAsync (stream, "notes.bin");
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_UnrecognizedBytes_CopiesTheStream ()
	{
		var result = MediaFile.Read (new MemoryStream (new byte[16]), "notes.bin");
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_TruncatedFlac_ReturnsTheParseFailure ()
	{
		var result = MediaFile.Read (new MemoryStream ("fLaC"u8.ToArray ()), "song.flac");
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("FLAC", StringComparison.Ordinal));
	}

	[TestMethod]
	public void Read_WavExtensionThatIsNotRiff_ReturnsTheParseFailure ()
	{
		var result = MediaFile.Read (new MemoryStream (new byte[] { 1, 2, 3, 4 }), "song.wav");
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("WAV", StringComparison.Ordinal));
	}

	[TestMethod]
	public void Read_Mp3ExtensionThatIsTooShort_ReturnsTheParseFailure ()
	{
		var result = MediaFile.Read (new MemoryStream (new byte[] { 1, 2 }), "song.mp3");
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("too short", StringComparison.Ordinal) || result.Error.Contains ("MP3", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ReadAtMost_NonPositiveCount_ReturnsEmpty ()
	{
		var stream = new MemoryStream (new byte[] { 1, 2, 3 });
		Assert.IsEmpty (StreamBytes.ReadAtMost (stream, 0));
		Assert.IsEmpty (StreamBytes.ReadAtMost (stream, -1));
	}

	[TestMethod]
	public async Task ReadAtMostAsync_NonPositiveCount_ReturnsEmpty ()
	{
		var stream = new MemoryStream (new byte[] { 1, 2, 3 });
		Assert.IsEmpty (await StreamBytes.ReadAtMostAsync (stream, 0, CancellationToken.None));
	}

	[TestMethod]
	public void ReadToEnd_SeekableEmptyOrAtTheEnd_ReturnsEmpty ()
	{
		var stream = new MemoryStream ();
		Assert.IsEmpty (StreamBytes.ReadToEnd (stream));
		stream.Write (new byte[] { 1 }, 0, 1);
		stream.Position = stream.Length;
		Assert.IsEmpty (StreamBytes.ReadToEnd (stream));
	}

	[TestMethod]
	public void ReadToEnd_SeekableBytes_ReturnsThem ()
	{
		var stream = new MemoryStream (new byte[] { 9, 8, 7 });
		CollectionAssert.AreEqual (new byte[] { 9, 8, 7 }, StreamBytes.ReadToEnd (stream));
	}

	[TestMethod]
	public void ReadToEnd_PastIntMax_Throws ()
	{
		var error = Assert.ThrowsExactly<IOException> (() => StreamBytes.ReadToEnd (new ReportedLengthStream ((long)int.MaxValue + 8)));
		Assert.AreEqual ("File is too large to read into memory", error.Message);
	}

	[TestMethod]
	public async Task ReadToEndAsync_CoversTheSeekableAndCopyPaths ()
	{
		var empty = new MemoryStream ();
		Assert.IsEmpty (await StreamBytes.ReadToEndAsync (empty, CancellationToken.None));

		var huge = new ReportedLengthStream ((long)int.MaxValue + 8);
		var error = await Assert.ThrowsExactlyAsync<IOException> (
			() => StreamBytes.ReadToEndAsync (huge, CancellationToken.None));
		Assert.AreEqual ("File is too large to read into memory", error.Message);

		var copy = new NonSeekableStream (new MemoryStream (new byte[] { 4, 5 }));
		CollectionAssert.AreEqual (new byte[] { 4, 5 }, await StreamBytes.ReadToEndAsync (copy, CancellationToken.None));
	}

	[TestMethod]
	public void ReadAtMost_EmptyStream_ReturnsEmpty ()
	{
		Assert.IsEmpty (StreamBytes.ReadAtMost (new MemoryStream (), 8));
	}

	[TestMethod]
	public void Read_Mp3ShorterThanFourBytes_Fails ()
	{
		var result = Mp3File.Read (new MemoryStream (new byte[] { 0xFF, 0xFB }));
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Invalid MP3 file: data too short", result.Error);
	}

	[TestMethod]
	public async Task ReadAsync_Mp3ShorterThanFourBytes_Fails ()
	{
		var result = await Mp3File.ReadAsync (new MemoryStream (new byte[] { 0xFF, 0xFB }), CancellationToken.None);
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Invalid MP3 file: data too short", result.Error);
	}

	[TestMethod]
	public void Read_NonSeekableMp3_StillReturnsTheDuration ()
	{
		var bytes = TinyMp3 (200);
		var result = MediaFile.Read (new NonSeekableStream (new MemoryStream (bytes)), "song.mp3");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (Duration (MediaFile.ReadFromData (bytes, "song.mp3")), Duration (result));
	}

	[TestMethod]
	public async Task ReadAsync_Mp3NotAtTheStart_Rewinds ()
	{
		var bytes = TinyMp3 (200);
		var stream = new MemoryStream (bytes) { Position = 3 };
		var result = await MediaFile.ReadAsync (stream, "song.mp3");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (Duration (result) > TimeSpan.Zero);
	}

	[TestMethod]
	public void Read_Mp3WhoseTailFitsInTheProbe_DoesNotSeekBackward ()
	{
		var bytes = TinyMp3 ((32 * 1024) + 50);
		var result = MediaFile.Read (new MemoryStream (bytes), "song.mp3");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (Duration (result) > TimeSpan.Zero);
	}

	[TestMethod]
	public void Read_XingCountPastTheFile_ReadsTheAudioThatIsPresent ()
	{
		var frame = Frame ();
		var audio = new byte[frame.FrameSize + MpegFrame.HeaderSize];
		new byte[] { 0xFF, 0xFB, 0x90, 0x00 }.CopyTo (audio, 0);
		new byte[] { 0xFF, 0xFB, 0x90, 0x00 }.CopyTo (audio, frame.FrameSize);
		audio[36] = (byte)'X';
		audio[37] = (byte)'i';
		audio[38] = (byte)'n';
		audio[39] = (byte)'g';
		audio[43] = 0x03;
		audio[47] = 10;
		audio[48] = 0xFF;
		audio[49] = 0xFF;
		audio[50] = 0xFF;
		audio[51] = 0xFF;
		var file = new byte[48 * 1024];
		audio.CopyTo (file, 0);

		var result = MediaFile.Read (new MemoryStream (file), "song.mp3");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (Duration (result) > TimeSpan.Zero);
		Assert.IsTrue (Duration (result) < TimeSpan.FromSeconds (10));
	}

	[TestMethod]
	public void Read_NonSeekableWav_ReturnsTheTitle ()
	{
		var wav = WavWithTitle ("Window", new byte[] { 1, 2, 3, 4 });
		var result = MediaFile.Read (new NonSeekableStream (new MemoryStream (wav)), "song.wav");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Window", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_WavNotAtTheStart_Rewinds ()
	{
		var wav = WavWithTitle ("Window", new byte[] { 1, 2, 3, 4 });
		var stream = new MemoryStream (wav) { Position = 4 };
		var result = WavFile.Read (stream, "/song.wav", new MockFileSystem ());
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Window", result.File!.Title);
	}

	[TestMethod]
	public void Read_WavThatIsNotRiff_Fails ()
	{
		var result = WavFile.Read (new MemoryStream (new byte[16]), null, null);
		Assert.IsFalse (result.IsSuccess);
		Assert.AreEqual ("Invalid WAV file: invalid RIFF structure", result.Error);
	}

	[TestMethod]
	public void Read_OddSizedDataChunk_SkipsThePadAndKeepsTheTitle ()
	{
		var wav = WavWithTitle ("Odd", new byte[] { 0x11 });
		var result = MediaFile.Read (new MemoryStream (wav), "song.wav");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Odd", result.Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (1 / 8000.0), Duration (result));
	}

	[TestMethod]
	public void Read_DataChunkCutOffByTheFile_UsesTheBytesThatExist ()
	{
		var wav = WavWithTitle ("Short", new byte[] { 1, 2, 3, 4 });
		// Claim 40 sample bytes. The title sits inside that claim, so the walk stops on the data chunk.
		var claimed = (byte[])wav.Clone ();
		var dataSizeAt = IndexOf (claimed, "data"u8.ToArray ()) + 4;
		claimed[dataSizeAt] = 40;
		var result = MediaFile.Read (new MemoryStream (claimed), "song.wav");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (Duration (result) > TimeSpan.Zero);
		Assert.IsNull (result.Tag?.Title);
	}

	[TestMethod]
	public void Read_WavWithNoDataChunk_HasNoDuration ()
	{
		var result = MediaFile.Read (new MemoryStream (WavWithTitle ("Fmt", null)), "song.wav");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (TimeSpan.Zero, Duration (result));
	}

	[TestMethod]
	public void Read_EmptyDataChunk_ReportsZeroDuration ()
	{
		var result = MediaFile.Read (new MemoryStream (WavWithTitle ("Empty", [])), "song.wav");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (TimeSpan.Zero, Duration (result));
		Assert.AreEqual ("Empty", result.Tag!.Title);
	}

	[TestMethod]
	public void Render_SkippedSamplesWithoutAPath_Throws ()
	{
		var wav = WavWithTitle ("Window", new byte[] { 1, 2, 3, 4 });
		var parsed = WavFile.Read (new MemoryStream (wav), null, null);
		var error = Assert.ThrowsExactly<InvalidOperationException> (() => parsed.File!.Render ());
		Assert.AreEqual ("Cannot save a WAV file whose sample data was not loaded.", error.Message);
	}

	[TestMethod]
	public async Task SaveToFileAsync_SkippedSamplesWithoutAPath_Fails ()
	{
		var wav = WavWithTitle ("Window", new byte[] { 1, 2, 3, 4 });
		var parsed = WavFile.Read (new MemoryStream (wav), null, null);
		var saved = await parsed.File!.SaveToFileAsync ("/song.wav");
		Assert.IsFalse (saved.IsSuccess);
		Assert.AreEqual ("Cannot save a WAV file whose sample data was not loaded.", saved.Error);
	}

	[TestMethod]
	public void Save_SourceShorterThanTheSkippedSamples_DoesNotWrite ()
	{
		var wav = WavWithTitle ("Window", new byte[32]);
		var files = new MockFileSystem ();
		files.AddFile ("/song.wav", wav);
		var opened = MediaFile.Read ("/song.wav", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		files.AddFile ("/song.wav", wav.AsSpan (0, 16).ToArray ());

		var saved = opened.GetFileAs<WavFile> ()!.SaveToFile ("/song.wav", files);
		Assert.IsFalse (saved.IsSuccess);
		Assert.AreEqual ("Cannot save a WAV file whose sample data is no longer in the source file.", saved.Error);
		Assert.AreEqual (16, files.ReadAllBytes ("/song.wav").Length);
	}

	[TestMethod]
	public void Read_NonSeekableFlac_ReturnsTheSampleRate ()
	{
		var flac = TestBuilders.Flac.CreateMinimal ();
		var result = MediaFile.Read (new NonSeekableStream (new MemoryStream (flac)), "song.flac");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, ((IMediaFile)result.File!).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public async Task ReadAsync_FlacNotAtTheStart_Rewinds ()
	{
		var flac = TestBuilders.Flac.CreateMinimal ();
		var stream = new MemoryStream (flac) { Position = 2 };
		var result = await MediaFile.ReadAsync (stream, "song.flac");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, ((IMediaFile)result.File!).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Read_FlacBlockCutOff_Stops ()
	{
		var flac = TestBuilders.Flac.CreateMinimal ();
		var cut = flac.AsSpan (0, 8).ToArray ();
		var result = FlacFile.Read (new MemoryStream (cut));
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public async Task ReadAsync_FlacHeaderTooShort_Stops ()
	{
		var bytes = "fLaC"u8.ToArray ().Concat (new byte[] { 0x00 }).ToArray ();
		var result = await FlacFile.ReadAsync (new MemoryStream (bytes), CancellationToken.None);
		Assert.IsFalse (result.IsSuccess);
	}

	static TimeSpan Duration (MediaFileResult result) =>
		((IMediaFile)result.File!).AudioProperties!.Duration;

	static MpegFrame Frame ()
	{
		Assert.IsTrue (MpegFrame.TryParse (new BinaryData (new byte[] { 0xFF, 0xFB, 0x90, 0x00 }), 0, out var frame));
		return frame!;
	}

	static byte[] TinyMp3 (int length)
	{
		var frame = Frame ();
		var audio = new byte[Math.Max (length, frame.FrameSize + MpegFrame.HeaderSize)];
		new byte[] { 0xFF, 0xFB, 0x90, 0x00 }.CopyTo (audio, 0);
		new byte[] { 0xFF, 0xFB, 0x90, 0x00 }.CopyTo (audio, frame.FrameSize);
		if (audio.Length != length && length > audio.Length)
			return audio;
		if (length < audio.Length)
			return audio.AsSpan (0, Math.Max (length, frame.FrameSize + 4)).ToArray ();
		return audio;
	}

	static byte[] WavWithTitle (string title, byte[]? samples)
	{
		using var body = new BinaryDataBuilder ();
		body.AddStringLatin1 ("WAVE");
		body.AddStringLatin1 ("fmt ");
		body.AddUInt32LE (16);
		body.AddUInt16LE (1);
		body.AddUInt16LE (1);
		body.AddUInt32LE (8000);
		body.AddUInt32LE (8000);
		body.AddUInt16LE (1);
		body.AddUInt16LE (8);
		if (samples is not null) {
			body.AddStringLatin1 ("data");
			body.AddUInt32LE ((uint)samples.Length);
			if (samples.Length > 0)
				body.Add (samples);
			if ((samples.Length & 1) == 1)
				body.Add (0);
		}

		var text = System.Text.Encoding.ASCII.GetBytes (title + "\0");
		using var info = new BinaryDataBuilder ();
		info.AddStringLatin1 ("INFO");
		info.AddStringLatin1 ("INAM");
		info.AddUInt32LE ((uint)text.Length);
		info.Add (text);
		var list = info.ToBinaryData ();
		body.AddStringLatin1 ("LIST");
		body.AddUInt32LE ((uint)list.Length);
		body.Add (list);
		return Riff (body);
	}

	static byte[] Riff (string form) => Riff (System.Text.Encoding.ASCII.GetBytes (form));

	static byte[] Riff (BinaryDataBuilder body) => Riff (body.ToBinaryData ().Span);

	static byte[] Riff (ReadOnlySpan<byte> body)
	{
		var file = new byte[8 + body.Length];
		"RIFF"u8.CopyTo (file);
		BitConverter.GetBytes ((uint)body.Length).CopyTo (file, 4);
		body.CopyTo (file.AsSpan (8));
		return file;
	}

	static int IndexOf (byte[] data, byte[] pattern)
	{
		for (var i = 0; i <= data.Length - pattern.Length; i++) {
			if (data.AsSpan (i, pattern.Length).SequenceEqual (pattern))
				return i;
		}

		return -1;
	}

	sealed class ThrowingFileSystem : IFileSystem
	{
		readonly Exception _error;
		public ThrowingFileSystem (Exception error) => _error = error;
		public bool FileExists (string path) => true;
		public Stream OpenRead (string path) => throw _error;
		public Stream OpenReadWrite (string path) => throw _error;
		public Stream Create (string path) => throw _error;
		public byte[] ReadAllBytes (string path) => throw _error;
		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) => throw _error;
		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => throw _error;
		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => throw _error;
		public void Delete (string path) => throw _error;
		public void Move (string sourcePath, string destinationPath) => throw _error;
		public string? GetDirectoryName (string path) => "/";
		public string GetFileName (string path) => path;
		public string CombinePath (string path1, string path2) => path1 + path2;
	}

	sealed class ReportedLengthStream : Stream
	{
		public ReportedLengthStream (long length) => Length = length;
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length { get; }
		public override long Position { get; set; }
		public override void Flush () { }
		public override int Read (byte[] buffer, int offset, int count) => 0;
		public override long Seek (long offset, SeekOrigin origin) => Position;
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}

	sealed class NonSeekableStream : Stream
	{
		readonly Stream _inner;
		public NonSeekableStream (Stream inner) => _inner = inner;
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException ();
		public override long Position { get => throw new NotSupportedException (); set => throw new NotSupportedException (); }
		public override void Flush () { }
		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);
		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}
}
