// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class MediaFileStreamTests
{
	[TestMethod]
	public void Read_NullStream_Throws ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => MediaFile.Read ((Stream)null!));
	}

	[TestMethod]
	public void Read_Mp3Stream_SkipsTheAudioAndKeepsTheTags ()
	{
		var file = Mp3WithALargeGap ();
		var counted = new CountingStream (new MemoryStream (file));

		var streamed = MediaFile.Read (counted, "song.mp3");
		var loaded = MediaFile.ReadFromData (file, "song.mp3");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual ("Window", streamed.Tag!.Title);
		Assert.AreEqual ("Tail", streamed.GetFileAs<Mp3File> ()!.Id3v1Tag!.Title);
		Assert.AreEqual (Duration (loaded), Duration (streamed));
		Assert.IsTrue (counted.BytesRead < file.Length - 500_000, $"read {counted.BytesRead} of {file.Length}");
	}

	[TestMethod]
	public async Task ReadAsync_Mp3Stream_SkipsTheAudioAndKeepsTheTags ()
	{
		var file = Mp3WithALargeGap ();
		var counted = new CountingStream (new MemoryStream (file));

		var streamed = await MediaFile.ReadAsync (counted, "song.mp3");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual ("Window", streamed.Tag!.Title);
		Assert.IsTrue (counted.BytesRead < file.Length - 500_000, $"read {counted.BytesRead} of {file.Length}");
	}

	[TestMethod]
	public void Read_FlacStream_StopsBeforeTheAudioFrames ()
	{
		var metadata = TestBuilders.Flac.CreateMinimal ();
		var file = new byte[metadata.Length + (1024 * 1024)];
		metadata.CopyTo (file, 0);
		var counted = new CountingStream (new MemoryStream (file));

		var streamed = MediaFile.Read (counted, "song.flac");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual (44100, ((IMediaFile)streamed.File!).AudioProperties!.SampleRate);
		Assert.IsTrue (counted.BytesRead < metadata.Length + 8192, $"read {counted.BytesRead} of {file.Length}");
	}

	[TestMethod]
	public void Read_WavStream_SkipsTheSampleData ()
	{
		var file = WavWithALargeDataChunk ();
		var counted = new CountingStream (new MemoryStream (file));

		var streamed = MediaFile.Read (counted, "song.wav");
		var loaded = MediaFile.ReadFromData (file, "song.wav");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual ("Window", streamed.Tag!.Title);
		Assert.AreEqual (Duration (loaded), Duration (streamed));
		Assert.IsTrue (counted.BytesRead < file.Length - 500_000, $"read {counted.BytesRead} of {file.Length}");
	}

	[TestMethod]
	public void Read_NonSeekableStream_StillReadsTheTitle ()
	{
		var file = Mp3WithALargeGap ();
		var stream = new NonSeekableStream (new MemoryStream (file));

		var result = MediaFile.Read (stream, "song.mp3");

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Window", result.Tag!.Title);
	}

	[TestMethod]
	public void Read_Path_DoesNotLoadTheWholeMp3 ()
	{
		var file = Mp3WithALargeGap ();
		var files = new CountingFileSystem ();
		files.AddFile ("/song.mp3", file);

		var result = MediaFile.Read ("/song.mp3", files);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Window", result.Tag!.Title);
		Assert.AreEqual (0, files.ReadAllBytesCalls);
		Assert.IsTrue (files.BytesRead < file.Length - 500_000, $"read {files.BytesRead} of {file.Length}");
	}

	static TimeSpan Duration (MediaFileResult result) =>
		((IMediaFile)result.File!).AudioProperties!.Duration;

	static byte[] Mp3WithALargeGap ()
	{
		var id3v2 = new Id3v2Tag { Title = "Window" }.Render ().ToArray ();
		var header = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
		Assert.IsTrue (MpegFrame.TryParse (new BinaryData (header), 0, out var frame));
		var audio = new byte[frame!.FrameSize + MpegFrame.HeaderSize];
		header.CopyTo (audio, 0);
		header.CopyTo (audio, frame.FrameSize);
		var gap = new byte[1024 * 1024];
		var id3v1 = new Id3v1Tag { Title = "Tail" }.Render ().ToArray ();

		var file = new byte[id3v2.Length + audio.Length + gap.Length + id3v1.Length];
		var offset = 0;
		id3v2.CopyTo (file, offset);
		offset += id3v2.Length;
		audio.CopyTo (file, offset);
		offset += audio.Length;
		gap.CopyTo (file, offset);
		offset += gap.Length;
		id3v1.CopyTo (file, offset);
		return file;
	}

	static byte[] WavWithALargeDataChunk ()
	{
		using var body = new BinaryDataBuilder ();
		body.AddStringLatin1 ("WAVE");
		body.AddStringLatin1 ("fmt ");
		body.AddUInt32LE (16);
		body.AddUInt16LE (1);
		body.AddUInt16LE (1);
		body.AddUInt32LE (44100);
		body.AddUInt32LE (88200);
		body.AddUInt16LE (2);
		body.AddUInt16LE (16);
		body.AddStringLatin1 ("data");
		body.AddUInt32LE (1024 * 1024);
		body.Add (new byte[1024 * 1024]);
		var info = InfoList ("Window");
		body.AddStringLatin1 ("LIST");
		body.AddUInt32LE ((uint)info.Length);
		body.Add (info);

		var payload = body.ToBinaryData ();
		using var file = new BinaryDataBuilder ();
		file.AddStringLatin1 ("RIFF");
		file.AddUInt32LE ((uint)payload.Length);
		file.Add (payload);
		return file.ToBinaryData ().ToArray ();
	}

	static byte[] InfoList (string title)
	{
		var text = System.Text.Encoding.ASCII.GetBytes (title + "\0");
		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 ("INFO");
		builder.AddStringLatin1 ("INAM");
		builder.AddUInt32LE ((uint)text.Length);
		builder.Add (text);
		return builder.ToBinaryData ().ToArray ();
	}

	sealed class CountingStream : Stream
	{
		readonly Stream _inner;

		public CountingStream (Stream inner) => _inner = inner;

		public long BytesRead { get; private set; }

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
			BytesRead += read;
			return read;
		}

		public override int Read (Span<byte> buffer)
		{
			var read = _inner.Read (buffer);
			BytesRead += read;
			return read;
		}

		public override ValueTask<int> ReadAsync (Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			var read = _inner.Read (buffer.Span);
			BytesRead += read;
			return new ValueTask<int> (read);
		}

		public override long Seek (long offset, SeekOrigin origin) => _inner.Seek (offset, origin);

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

		public override long Position {
			get => throw new NotSupportedException ();
			set => throw new NotSupportedException ();
		}

		public override void Flush ()
		{
		}

		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);

		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();

		public override void SetLength (long value) => throw new NotSupportedException ();

		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}

	sealed class CountingFileSystem : IFileSystem
	{
		readonly MockFileSystem _inner = new ();

		public CountingStream? Opened { get; private set; }

		public int ReadAllBytesCalls { get; private set; }

		public long BytesRead => Opened?.BytesRead ?? 0;

		public void AddFile (string path, byte[] data) => _inner.AddFile (path, data);

		public bool FileExists (string path) => _inner.FileExists (path);

		public Stream OpenRead (string path)
		{
			Opened = new CountingStream (_inner.OpenRead (path));
			return Opened;
		}

		public Stream OpenReadWrite (string path) => _inner.OpenReadWrite (path);

		public Stream Create (string path) => _inner.Create (path);

		public byte[] ReadAllBytes (string path)
		{
			ReadAllBytesCalls++;
			return _inner.ReadAllBytes (path);
		}

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default)
		{
			ReadAllBytesCalls++;
			return _inner.ReadAllBytesAsync (path, cancellationToken);
		}

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => _inner.WriteAllBytes (path, data);

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			_inner.WriteAllBytesAsync (path, data, cancellationToken);

		public void Delete (string path) => _inner.Delete (path);

		public void Move (string sourcePath, string destinationPath) => _inner.Move (sourcePath, destinationPath);

		public string? GetDirectoryName (string path) => _inner.GetDirectoryName (path);

		public string GetFileName (string path) => _inner.GetFileName (path);

		public string CombinePath (string path1, string path2) => _inner.CombinePath (path1, path2);
	}
}
