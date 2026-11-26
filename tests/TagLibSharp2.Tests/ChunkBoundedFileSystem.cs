// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests;

/// <summary>
/// Rejects a read or write larger than 64 KB. A save that loads the audio fails here.
/// The write buffer is allocated by the caller so the save's allocation budget does not include it.
/// </summary>
public sealed class ChunkBoundedFileSystem : IFileSystem
{
	public const int MaxChunk = 64 * 1024;

	readonly Dictionary<string, Entry> _files = new (StringComparer.Ordinal);
	readonly byte[] _scratch;

	/// <summary>
	/// Largest accepted read. The default is <see cref="MaxChunk"/>. A reader that takes one
	/// larger window may raise this for the read, then put it back before the save.
	/// Writes stay at <see cref="MaxChunk"/>.
	/// </summary>
	public int MaxRead { get; set; } = MaxChunk;

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

		void Reject (int count)
		{
			if (count > _owner.MaxRead)
				throw new InvalidOperationException ($"read of {count} bytes");
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
