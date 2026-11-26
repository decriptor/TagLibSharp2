// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// A path save copies the audio. It does not read or write the file as one buffer,
/// and the bytes match <see cref="Mp3File.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mpeg")]
public sealed class Mp3SaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;
	const int MaxChunk = 64 * 1024;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var original = Build ();
		var fs = new ChunkBoundedFileSystem (original.Length + MaxChunk);
		fs.Add ("/in.mp3", original);

		var read = MediaFile.Read ("/in.mp3", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = (Mp3File)read.File!;
		file.Title = "Changed";
		var expected = file.Render (original).ToArray ();

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = file.SaveToFile ("/out.mp3", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.mp3"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var original = Build ();
		var fs = new ChunkBoundedFileSystem (original.Length + MaxChunk);
		fs.Add ("/in.mp3", original);

		var read = MediaFile.Read ("/in.mp3", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var file = (Mp3File)read.File!;
		file.Title = "Changed";
		var expected = file.Render (original).ToArray ();

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await file.SaveToFileAsync ("/out.mp3", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.mp3"));
	}

	static byte[] Build ()
	{
		var id3v2 = new Id3v2Tag { Title = "Original" }.Render ().ToArray ();
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);

		var id3v1 = TestBuilders.Id3v1.CreateTag (title: "Original", artist: "Artist");
		var file = new byte[id3v2.Length + audio.Length + id3v1.Length];
		id3v2.CopyTo (file, 0);
		audio.CopyTo (file, id3v2.Length);
		id3v1.CopyTo (file, id3v2.Length + audio.Length);
		return file;
	}

	static void AssertSameBytes (byte[] expected, byte[] actual)
	{
		Assert.AreEqual (expected.Length, actual.Length, "saved length");
		var mismatch = -1;
		var length = Math.Min (expected.Length, actual.Length);
		for (var i = 0; i < length; i++) {
			if (expected[i] == actual[i])
				continue;
			mismatch = i;
			break;
		}

		Assert.AreEqual (-1, mismatch, $"first mismatched byte at {mismatch}");
	}

	[TestMethod]
	public async Task ChunkBound_RejectsAFullFileReadAndAnOversizedWrite ()
	{
		var fs = new ChunkBoundedFileSystem (MaxChunk);
		fs.Add ("/in.mp3", new byte[] { 1, 2, 3, 4 });
		var read = Assert.ThrowsExactly<InvalidOperationException> (() => fs.ReadAllBytes ("/in.mp3"));
		Assert.AreEqual ("ReadAllBytes of 4 bytes", read.Message);
		var readAsync = await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await fs.ReadAllBytesAsync ("/in.mp3"));
		Assert.AreEqual (read.Message, readAsync.Message);
		Assert.ThrowsExactly<NotSupportedException> (() => fs.OpenReadWrite ("/in.mp3"));

		var exact = new byte[MaxChunk];
		fs.WriteAllBytes ("/exact.mp3", exact);
		await fs.WriteAllBytesAsync ("/exact-async.mp3", exact);
		Assert.AreEqual (MaxChunk, fs.Bytes ("/exact.mp3").Length);
		var over = new byte[MaxChunk + 1];
		var write = Assert.ThrowsExactly<InvalidOperationException> (() => fs.WriteAllBytes ("/over.mp3", over));
		Assert.AreEqual ($"WriteAllBytes of {over.Length} bytes", write.Message);
		Assert.IsFalse (fs.FileExists ("/over.mp3"));
		fs.Move ("/exact.mp3", "/moved.mp3");
		fs.Delete ("/moved.mp3");
		Assert.AreEqual ("a.mp3", fs.GetFileName ("/dir/a.mp3"));
		Assert.AreEqual (Path.GetDirectoryName ("/dir/a.mp3"), fs.GetDirectoryName ("/dir/a.mp3"));

		using (var source = fs.OpenRead ("/in.mp3")) {
			Assert.IsFalse (source.CanWrite);
			Assert.ThrowsExactly<NotSupportedException> (() => source.SetLength (1));
			Assert.ThrowsExactly<NotSupportedException> (() => source.Write (new byte[1], 0, 1));
			var tooBig = Assert.ThrowsExactly<InvalidOperationException> (() => source.Read (over, 0, over.Length));
			Assert.AreEqual ($"read of {over.Length} bytes", tooBig.Message);
			Assert.ThrowsExactly<InvalidOperationException> (() => source.Read (over.AsSpan ()));
			Assert.AreEqual (4, source.Read (new byte[4], 0, 4));
		}

		using var created = fs.Create ("/out.mp3");
		Assert.IsFalse (created.CanRead);
		Assert.IsTrue (created.CanSeek);
		created.Flush ();
		Assert.ThrowsExactly<NotSupportedException> (() => created.Read (new byte[1], 0, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => created.Seek (0, (SeekOrigin)99));
		Assert.ThrowsExactly<IOException> (() => created.Seek (-1, SeekOrigin.Begin));
		var oversized = Assert.ThrowsExactly<InvalidOperationException> (() => created.Write (over, 0, over.Length));
		Assert.AreEqual ($"write of {over.Length} bytes", oversized.Message);
		Assert.ThrowsExactly<InvalidOperationException> (() => created.Write (over.AsSpan ()));
		created.Write (exact, 0, exact.Length);
		created.Position = exact.Length;
		Assert.ThrowsExactly<IOException> (() => created.Write (new byte[] { 1 }, 0, 1));
		created.Seek (0, SeekOrigin.End);
		created.Seek (0, SeekOrigin.Current);
		created.SetLength (2);
		Assert.AreEqual (2, created.Length);
	}

	/// <summary>
	/// Rejects a read or write larger than 64 KB. A save that loads the audio fails here.
	/// The write buffer is allocated by the caller so the save's allocation budget does not include it.
	/// </summary>
	sealed class ChunkBoundedFileSystem : IFileSystem
	{
		readonly Dictionary<string, Entry> _files = new (StringComparer.Ordinal);
		readonly byte[] _scratch;

		public int SourceLengthGets { get; private set; }

		public void ResetSourceLengthGets () => SourceLengthGets = 0;

		public ChunkBoundedFileSystem (int writeCapacity) => _scratch = new byte[writeCapacity];

		public void Add (string path, byte[] data) => _files[path] = new Entry (data, data.Length);

		public byte[] Bytes (string path)
		{
			var entry = Get (path);
			var exact = new byte[entry.Count];
			Buffer.BlockCopy (entry.Data, 0, exact, 0, entry.Count);
			return exact;
		}

		public bool FileExists (string path) => _files.ContainsKey (path);

		public Stream OpenRead (string path)
		{
			var entry = Get (path);
			return new BoundedStream (this, new MemoryStream (entry.Data, 0, entry.Count, writable: false));
		}

		public Stream OpenReadWrite (string path) => throw new NotSupportedException ();

		public Stream Create (string path)
		{
			var stream = new ScratchStream (_scratch, count => _files[path] = new Entry (_scratch, count));
			_files[path] = new Entry (_scratch, 0);
			return stream;
		}

		public byte[] ReadAllBytes (string path) =>
			throw new InvalidOperationException ($"ReadAllBytes of {Get (path).Count} bytes");

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException ($"ReadAllBytes of {Get (path).Count} bytes");

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data)
		{
			if (data.Length > MaxChunk)
				throw new InvalidOperationException ($"WriteAllBytes of {data.Length} bytes");
			_files[path] = new Entry (data.ToArray (), data.Length);
		}

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
		{
			WriteAllBytes (path, data.Span);
			return Task.CompletedTask;
		}

		public void Delete (string path) => _files.Remove (path);

		public void Move (string sourcePath, string destinationPath)
		{
			_files[destinationPath] = Get (sourcePath);
			_files.Remove (sourcePath);
		}

		public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

		public string GetFileName (string path) => Path.GetFileName (path);

		public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);

		Entry Get (string path) =>
			_files.TryGetValue (path, out var entry) ? entry : throw new FileNotFoundException (path);

		readonly record struct Entry (byte[] Data, int Count);

		sealed class BoundedStream : Stream
		{
			readonly ChunkBoundedFileSystem _owner;
			readonly MemoryStream _inner;

			public BoundedStream (ChunkBoundedFileSystem owner, MemoryStream inner)
			{
				_owner = owner;
				_inner = inner;
			}

			public override bool CanRead => _inner.CanRead;
			public override bool CanSeek => _inner.CanSeek;
			public override bool CanWrite => false;
			public override long Length {
				get {
					_owner.SourceLengthGets++;
					return _inner.Length;
				}
			}
			public override long Position { get => _inner.Position; set => _inner.Position = value; }

			public override void Flush () => _inner.Flush ();

			public override int Read (byte[] buffer, int offset, int count)
			{
				Reject (count);
				return _inner.Read (buffer, offset, count);
			}

			public override int Read (Span<byte> buffer)
			{
				Reject (buffer.Length);
				return _inner.Read (buffer);
			}

			public override long Seek (long offset, SeekOrigin origin) => _inner.Seek (offset, origin);

			public override void SetLength (long value) => throw new NotSupportedException ();

			public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

			protected override void Dispose (bool disposing)
			{
				if (disposing)
					_inner.Dispose ();
				base.Dispose (disposing);
			}

			static void Reject (int count)
			{
				if (count > MaxChunk)
					throw new InvalidOperationException ($"read of {count} bytes");
			}
		}

		sealed class ScratchStream : Stream
		{
			readonly byte[] _buffer;
			readonly Action<int> _commit;
			int _length;
			int _position;
			bool _committed;

			public ScratchStream (byte[] buffer, Action<int> commit)
			{
				_buffer = buffer;
				_commit = commit;
			}

			public override bool CanRead => false;
			public override bool CanSeek => true;
			public override bool CanWrite => true;
			public override long Length => _length;
			public override long Position {
				get => _position;
				set => _position = checked((int)value);
			}

			public override void Flush ()
			{
			}

			public override int Read (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

			public override long Seek (long offset, SeekOrigin origin)
			{
				var next = origin switch {
					SeekOrigin.Begin => offset,
					SeekOrigin.Current => _position + offset,
					SeekOrigin.End => _length + offset,
					_ => throw new ArgumentOutOfRangeException (nameof (origin))
				};
				if (next < 0 || next > _buffer.Length)
					throw new IOException ("Seek is outside the scratch buffer");
				_position = (int)next;
				return _position;
			}

			public override void SetLength (long value) => _length = checked((int)value);

			public override void Write (byte[] buffer, int offset, int count)
			{
				Reject (count);
				Copy (buffer.AsSpan (offset, count));
			}

			public override void Write (ReadOnlySpan<byte> buffer)
			{
				Reject (buffer.Length);
				Copy (buffer);
			}

			protected override void Dispose (bool disposing)
			{
				if (disposing && !_committed) {
					_committed = true;
					_commit (_length);
				}

				base.Dispose (disposing);
			}

			void Copy (ReadOnlySpan<byte> data)
			{
				if ((uint)_position > (uint)_buffer.Length || data.Length > _buffer.Length - _position)
					throw new IOException ("Scratch buffer is full");
				data.CopyTo (_buffer.AsSpan (_position));
				_position += data.Length;
				if (_position > _length)
					_length = _position;
			}

			static void Reject (int count)
			{
				if (count > MaxChunk)
					throw new InvalidOperationException ($"write of {count} bytes");
			}
		}
	}
}
