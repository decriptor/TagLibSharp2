// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// In-memory file system for a path save. An unseekable open, a length fault, or open tracking wraps the
/// source. A create counts the call and records the largest write.
/// </summary>
sealed class ProbeFileSystem : IFileSystem
{
	public MockFileSystem Store { get; } = new ();

	public bool Seekable { get; set; } = true;

	public string? OpenReadFault { get; set; }

	public string? LengthFault { get; set; }

	public bool ArmOpenTracking { get; set; }

	public TrackedStream? SaveOpen { get; private set; }

	public CancellationTokenSource? CancelOnWrite { get; set; }

	public int CreateCalls { get; private set; }

	public int MaxWrite { get; private set; }

	public byte[] Original { get; private set; } = [];

	public void Add (string path, byte[] data)
	{
		Original = data.ToArray ();
		Store.AddFile (path, data);
	}

	public bool FileExists (string path) => Store.FileExists (path);

	public Stream OpenRead (string path)
	{
		if (OpenReadFault == "denied")
			throw new UnauthorizedAccessException ("Access denied");
		if (OpenReadFault == "io")
			throw new IOException ("disk fault");

		var stream = Store.OpenRead (path);
		if (Seekable && LengthFault is null && !ArmOpenTracking)
			return stream;

		var tracked = new TrackedStream (stream, Seekable, LengthFault);
		if (ArmOpenTracking)
			SaveOpen = tracked;
		return tracked;
	}

	public Stream OpenReadWrite (string path) => Store.OpenReadWrite (path);

	public Stream Create (string path)
	{
		CreateCalls++;
		return new RecordingStream (Store.Create (path), this);
	}

	public byte[] ReadAllBytes (string path) => Store.ReadAllBytes (path);

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
		Store.ReadAllBytesAsync (path, cancellationToken);

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => Store.WriteAllBytes (path, data);

	public Task WriteAllBytesAsync (
		string path,
		ReadOnlyMemory<byte> data,
		CancellationToken cancellationToken = default) =>
		Store.WriteAllBytesAsync (path, data, cancellationToken);

	public void Delete (string path) => Store.Delete (path);

	public void Move (string sourcePath, string destinationPath) => Store.Move (sourcePath, destinationPath);

	public string? GetDirectoryName (string path) => Store.GetDirectoryName (path);

	public string GetFileName (string path) => Store.GetFileName (path);

	public string CombinePath (string path1, string path2) => Store.CombinePath (path1, path2);

	internal void NoteWrite (int count)
	{
		if (count > MaxWrite)
			MaxWrite = count;
		CancelOnWrite?.Cancel ();
	}

	public sealed class TrackedStream : Stream
	{
		readonly Stream _inner;
		readonly bool _seekable;
		readonly string? _lengthFault;

		public TrackedStream (Stream inner, bool seekable, string? lengthFault)
		{
			_inner = inner;
			_seekable = seekable;
			_lengthFault = lengthFault;
		}

		public bool WasDisposed { get; private set; }

		public override bool CanRead => _inner.CanRead;

		public override bool CanSeek => _seekable;

		public override bool CanWrite => false;

		public override long Length {
			get {
				if (_lengthFault == "io")
					throw new IOException ("disk fault");
				if (_lengthFault == "denied")
					throw new UnauthorizedAccessException ("Access denied");
				if (!_seekable)
					throw new NotSupportedException ();
				return _inner.Length;
			}
		}

		public override long Position {
			get => _seekable ? _inner.Position : throw new NotSupportedException ();
			set {
				if (!_seekable)
					throw new NotSupportedException ();
				_inner.Position = value;
			}
		}

		public override void Flush () => _inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);

		public override long Seek (long offset, SeekOrigin origin) =>
			_seekable ? _inner.Seek (offset, origin) : throw new NotSupportedException ();

		public override void SetLength (long value) => throw new NotSupportedException ();

		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

		protected override void Dispose (bool disposing)
		{
			if (disposing) {
				WasDisposed = true;
				_inner.Dispose ();
			}

			base.Dispose (disposing);
		}
	}

	sealed class RecordingStream : Stream
	{
		readonly Stream _inner;
		readonly ProbeFileSystem _owner;

		public RecordingStream (Stream inner, ProbeFileSystem owner)
		{
			_inner = inner;
			_owner = owner;
		}

		public override bool CanRead => _inner.CanRead;

		public override bool CanSeek => _inner.CanSeek;

		public override bool CanWrite => _inner.CanWrite;

		public override long Length => _inner.Length;

		public override long Position {
			get => _inner.Position;
			set => _inner.Position = value;
		}

		public override void Flush () => _inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);

		public override long Seek (long offset, SeekOrigin origin) => _inner.Seek (offset, origin);

		public override void SetLength (long value) => _inner.SetLength (value);

		public override void Write (byte[] buffer, int offset, int count)
		{
			_owner.NoteWrite (count);
			_inner.Write (buffer, offset, count);
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_inner.Dispose ();
			base.Dispose (disposing);
		}
	}
}
