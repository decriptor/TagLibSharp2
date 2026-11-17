// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class WindowedReadDefectTests
{
	[TestMethod]
	public void Read_TwoDataChunks_DurationUsesTheFirstAndSaveKeepsBoth ()
	{
		var first = new byte[] { 0x01, 0x01, 0x01, 0x01 };
		var second = new byte[] { 0x02, 0x02, 0x02, 0x02, 0x02, 0x02 };
		var wav = TwoDataChunks (first, second, "Window");
		var files = new MockFileSystem ();
		files.AddFile ("/song.wav", wav);

		var streamed = MediaFile.Read ("/song.wav", files);
		var loaded = MediaFile.ReadFromData (wav, "song.wav");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual ("Window", streamed.Tag!.Title);
		Assert.AreEqual (Duration (loaded), Duration (streamed));
		Assert.AreEqual (TimeSpan.FromSeconds (first.Length / 8000.0), Duration (streamed));

		streamed.Tag.Title = "After";
		var saved = streamed.GetFileAs<WavFile> ()!.SaveToFile ("/song.wav", files);
		Assert.IsTrue (saved.IsSuccess, saved.Error);

		var payloads = DataPayloads (files.ReadAllBytes ("/song.wav"));
		Assert.HasCount (2, payloads);
		CollectionAssert.AreEqual (first, payloads[0]);
		CollectionAssert.AreEqual (second, payloads[1]);
		Assert.AreEqual ("After", MediaFile.Read ("/song.wav", files).Tag!.Title);
	}

	[TestMethod]
	public void Read_EmptySecondDataChunk_DoesNotClearTheFirstPayload ()
	{
		var first = new byte[] { 0x01, 0x01, 0x01, 0x01 };
		var wav = TwoDataChunks (first, [], "Window");
		var files = new MockFileSystem ();
		files.AddFile ("/song.wav", wav);

		var streamed = MediaFile.Read ("/song.wav", files);
		var loaded = MediaFile.ReadFromData (wav, "song.wav");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual (Duration (loaded), Duration (streamed));
		Assert.AreEqual (TimeSpan.FromSeconds (0.0005), Duration (streamed));

		var saved = streamed.GetFileAs<WavFile> ()!.SaveToFile ("/song.wav", files);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var payloads = DataPayloads (files.ReadAllBytes ("/song.wav"));
		Assert.HasCount (2, payloads);
		CollectionAssert.AreEqual (first, payloads[0]);
		Assert.IsEmpty (payloads[1]);
	}

	[TestMethod]
	public void Read_DataChunkPastTwoGigabytes_KeepsTheFollowingTitleAndRefusesTheSave ()
	{
		var files = new SparseFileSystem (PastTwoGigabytes ());
		using var opened = files.OpenRead ("/song.wav");
		var streamed = WavFile.Read (opened, "/song.wav", files);

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual ("Window", streamed.File!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (0x80000000 / 8000.0), streamed.File.Properties.Duration);

		var saved = streamed.File.SaveToFile ("/song.wav", files);
		Assert.IsFalse (saved.IsSuccess, saved.Error);
		Assert.AreEqual (0, files.Writes);
	}

	[TestMethod]
	public void Read_XingByteCountPastFourGigabytes_UsesTheFrameCount ()
	{
		var mp3 = new SparseMp3 (XingPastFourGigabytes ());
		var streamed = MediaFile.Read (mp3, "song.mp3");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual (TimeSpan.FromSeconds (100 * 1152 / 44100.0), Duration (streamed));
	}

	[TestMethod]
	public async Task ReadAsync_XingByteCountPastFourGigabytes_UsesTheFrameCount ()
	{
		var mp3 = new SparseMp3 (XingPastFourGigabytes ());
		var streamed = await MediaFile.ReadAsync (mp3, "song.mp3");

		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual (TimeSpan.FromSeconds (100 * 1152 / 44100.0), Duration (streamed));
	}

	static TimeSpan Duration (MediaFileResult result) =>
		((IMediaFile)result.File!).AudioProperties!.Duration;

	static byte[] TwoDataChunks (byte[] first, byte[] second, string title)
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
		AddData (body, first);
		AddData (body, second);
		var info = InfoList (title);
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

	static void AddData (BinaryDataBuilder body, byte[] samples)
	{
		body.AddStringLatin1 ("data");
		body.AddUInt32LE ((uint)samples.Length);
		if (samples.Length > 0)
			body.Add (samples);
		if ((samples.Length & 1) == 1)
			body.Add (0);
	}

	static byte[] InfoList (string title)
	{
		var text = Encoding.ASCII.GetBytes (title + "\0");
		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 ("INFO");
		builder.AddStringLatin1 ("INAM");
		builder.AddUInt32LE ((uint)text.Length);
		builder.Add (text);
		return builder.ToBinaryData ().ToArray ();
	}

	static List<byte[]> DataPayloads (byte[] wav)
	{
		var payloads = new List<byte[]> ();
		var offset = 12;
		while (offset + 8 <= wav.Length) {
			var id = Encoding.ASCII.GetString (wav, offset, 4);
			var size = BitConverter.ToUInt32 (wav, offset + 4);
			var start = offset + 8;
			if (id == "data") {
				var count = (int)Math.Min (size, (uint)Math.Max (0, wav.Length - start));
				var payload = new byte[count];
				Buffer.BlockCopy (wav, start, payload, 0, count);
				payloads.Add (payload);
			}

			var step = 8L + size + (size & 1);
			if (step <= 0 || offset + step > wav.Length)
				break;
			offset += (int)step;
		}

		return payloads;
	}

	static SparseLayout PastTwoGigabytes ()
	{
		using var head = new BinaryDataBuilder ();
		head.AddStringLatin1 ("RIFF");
		head.AddUInt32LE (0);
		head.AddStringLatin1 ("WAVE");
		head.AddStringLatin1 ("fmt ");
		head.AddUInt32LE (16);
		head.AddUInt16LE (1);
		head.AddUInt16LE (1);
		head.AddUInt32LE (8000);
		head.AddUInt32LE (8000);
		head.AddUInt16LE (1);
		head.AddUInt16LE (8);
		head.AddStringLatin1 ("data");
		head.AddUInt32LE (0x80000000);
		var info = InfoList ("Window");
		using var tail = new BinaryDataBuilder ();
		tail.AddStringLatin1 ("LIST");
		tail.AddUInt32LE ((uint)info.Length);
		tail.Add (info);
		var headBytes = head.ToBinaryData ().ToArray ();
		var tailBytes = tail.ToBinaryData ().ToArray ();
		var total = headBytes.Length + 0x80000000L + tailBytes.Length;
		var size = (uint)(total - 8);
		headBytes[4] = (byte)size;
		headBytes[5] = (byte)(size >> 8);
		headBytes[6] = (byte)(size >> 16);
		headBytes[7] = (byte)(size >> 24);
		return new SparseLayout (headBytes, 0x80000000L, tailBytes);
	}

	static SparseLayout XingPastFourGigabytes ()
	{
		var header = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
		Assert.IsTrue (MpegFrame.TryParse (new BinaryData (header), 0, out var frame));
		var audio = new byte[frame!.FrameSize + MpegFrame.HeaderSize];
		header.CopyTo (audio, 0);
		header.CopyTo (audio, frame.FrameSize);
		audio[36] = (byte)'X';
		audio[37] = (byte)'i';
		audio[38] = (byte)'n';
		audio[39] = (byte)'g';
		audio[40] = 0;
		audio[41] = 0;
		audio[42] = 0;
		audio[43] = 0x03;
		audio[44] = 0;
		audio[45] = 0;
		audio[46] = 0;
		audio[47] = 100;
		audio[48] = 0;
		audio[49] = 0;
		audio[50] = 0x17;
		audio[51] = 0x70;
		return new SparseLayout (audio, (long)uint.MaxValue + 5000 - audio.Length, []);
	}

	sealed class SparseFileSystem : IFileSystem
	{
		readonly SparseLayout _layout;

		public SparseFileSystem (SparseLayout layout) => _layout = layout;

		public int Writes { get; private set; }

		public bool FileExists (string path) => path == "/song.wav";

		public Stream OpenRead (string path) => new SparseStream (_layout);

		public Stream OpenReadWrite (string path) => throw new NotSupportedException ();

		public Stream Create (string path) => throw new NotSupportedException ();

		public byte[] ReadAllBytes (string path) => throw new NotSupportedException ();

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException ();

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => Writes++;

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
		{
			Writes++;
			return Task.CompletedTask;
		}

		public void Delete (string path)
		{
		}

		public void Move (string sourcePath, string destinationPath)
		{
		}

		public string? GetDirectoryName (string path) => "/";

		public string GetFileName (string path) => "song.wav";

		public string CombinePath (string path1, string path2) => path1 + path2;
	}

	internal readonly struct SparseLayout
	{
		public SparseLayout (byte[] head, long hole, byte[] tail)
		{
			Head = head;
			Hole = hole;
			Tail = tail;
		}

		public byte[] Head { get; }

		public long Hole { get; }

		public byte[] Tail { get; }

		public long Length => Head.Length + Hole + Tail.Length;
	}

	sealed class SparseMp3 : SparseStream
	{
		public SparseMp3 (SparseLayout layout) : base (layout)
		{
		}
	}

	class SparseStream : Stream
	{
		readonly SparseLayout _layout;
		long _position;

		public SparseStream (SparseLayout layout) => _layout = layout;

		public override bool CanRead => true;

		public override bool CanSeek => true;

		public override bool CanWrite => false;

		public override long Length => _layout.Length;

		public override long Position {
			get => _position;
			set => _position = value;
		}

		public override void Flush ()
		{
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			if (count <= 0 || _position >= Length)
				return 0;

			var written = 0;
			while (written < count && _position < Length) {
				var tailAt = _layout.Head.Length + _layout.Hole;
				if (_position < _layout.Head.Length) {
					var n = (int)Math.Min (count - written, _layout.Head.Length - _position);
					Buffer.BlockCopy (_layout.Head, (int)_position, buffer, offset + written, n);
					_position += n;
					written += n;
				} else if (_position < tailAt) {
					var n = (int)Math.Min (count - written, tailAt - _position);
					Array.Clear (buffer, offset + written, n);
					_position += n;
					written += n;
				} else {
					var tailOffset = (int)(_position - tailAt);
					var n = Math.Min (count - written, _layout.Tail.Length - tailOffset);
					if (n <= 0)
						break;
					Buffer.BlockCopy (_layout.Tail, tailOffset, buffer, offset + written, n);
					_position += n;
					written += n;
				}
			}

			return written;
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			_position = origin switch {
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _position + offset,
				SeekOrigin.End => Length + offset,
				_ => throw new ArgumentOutOfRangeException (nameof (origin))
			};
			return _position;
		}

		public override void SetLength (long value) => throw new NotSupportedException ();

		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}
}
