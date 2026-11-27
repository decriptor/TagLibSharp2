// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>A path save that was cancelled does not replace the destination.</summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class ImagePathSaveCancellationTests
{
	[TestMethod]
	public async Task JpegPathSave_CancelledToken_DoesNotReplaceTheDestination ()
	{
		var data = Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()));
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.jpg", data);
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var save = await read.File!.SaveToFileAsync ("/out.jpg", fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Failed to re-read source file: Operation was cancelled", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
	}

	[TestMethod]
	public async Task JpegPathSave_MissingSource_BeatsAnAlreadyCancelledToken ()
	{
		var data = Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()));
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.jpg", data);
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.Delete ("/in.jpg");
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var save = await read.File!.SaveToFileAsync ("/out.jpg", fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Failed to re-read source file: File not found: /in.jpg", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
	}

	[TestMethod]
	public async Task JpegPathSave_ShortSource_BeatsAnAlreadyCancelledToken ()
	{
		var data = Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()));
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.jpg", data);
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.AddFile ("/in.jpg", [1]);
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var save = await read.File!.SaveToFileAsync ("/out.jpg", fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("The source ended before the requested bytes were copied", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
	}

	[TestMethod]
	public async Task JpegPathSave_CancelledTokenWithoutASourcePath_DoesNotWrite ()
	{
		var read = StillImageFile.Read (Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ())));
		Assert.IsTrue (read.IsSuccess, read.Error);
		var fs = new MockFileSystem ();
		using var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var save = await read.File!.SaveToFileAsync ("/out.jpg", fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Operation was cancelled", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
	}

	[TestMethod]
	public async Task JpegPathSave_UnseekableSource_BeatsAnAlreadyCancelledToken ()
	{
		var (file, fs) = OpenJpeg ();
		fs.Seekable = false;
		fs.ArmOpenTracking = true;

		var save = await file.SaveToFileAsync ("/out.jpg", fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Failed to re-read source file: source is not seekable", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
		Assert.AreEqual (0, fs.CreateCalls);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
	}

	[TestMethod]
	[DataRow ("open-io", "Failed to re-read source file: Failed to read file: disk fault")]
	[DataRow ("open-denied", "Failed to re-read source file: Access denied: Access denied")]
	[DataRow ("length-io", "Failed to re-read source file: Failed to read file: disk fault")]
	[DataRow ("length-denied", "Failed to re-read source file: Access denied: Access denied")]
	public async Task JpegPathSave_UnreadableSource_BeatsAnAlreadyCancelledToken (string fault, string expected)
	{
		var (file, fs) = OpenJpeg ();
		fs.ArmOpenTracking = true;
		if (fault.StartsWith ("open", StringComparison.Ordinal))
			fs.OpenReadFault = fault == "open-io" ? "io" : "denied";
		else
			fs.LengthFault = fault == "length-io" ? "io" : "denied";

		var save = await file.SaveToFileAsync ("/out.jpg", fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (expected, save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
		Assert.AreEqual (0, fs.CreateCalls);
		if (fault.StartsWith ("length", StringComparison.Ordinal)) {
			Assert.IsNotNull (fs.SaveOpen);
			Assert.IsTrue (fs.SaveOpen.WasDisposed);
		} else {
			Assert.IsNull (fs.SaveOpen);
		}
	}

	[TestMethod]
	[DataRow (
		"jpeg",
		"The JPEG marker list before the scan is truncated or malformed, so the comment cannot be spliced in.")]
	[DataRow (
		"png",
		"The PNG chunk list does not reach IEND, so the text cannot be spliced in.")]
	[DataRow (
		"gif",
		"The GIF block list is truncated or holds an unknown block, so the comment cannot be spliced in.")]
	public async Task PathSave_SpliceFailure_BeatsAnAlreadyCancelledToken (string format, string expected)
	{
		var (file, fs) = OpenTruncated (format);
		fs.ArmOpenTracking = true;
		if (format == "png")
			file.Tag!.Title = "PathSave";
		else
			file.Tag!.Comment = "PathSave";

		var save = await file.SaveToFileAsync ("/out." + format, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (expected, save.Error);
		Assert.IsFalse (fs.FileExists ("/out." + format));
		Assert.AreEqual (0, fs.CreateCalls);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
		CollectionAssert.AreEqual (fs.Original, fs.Store.ReadAllBytes ("/in." + format));
	}

	[TestMethod]
	public async Task JpegPathSave_CancelDuringCopy_DoesNotReplaceTheDestination ()
	{
		var header = TiffImage (2, 3);
		var data = new byte[200 * 1024];
		header.CopyTo (data, 0);
		var fs = new FaultFileSystem ();
		fs.Add ("/in.tiff", data);
		var read = StillImageFile.ReadFromFile ("/in.tiff", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.ArmOpenTracking = true;
		using var cts = new CancellationTokenSource ();
		fs.CancelOnWrite = cts;

		var save = await read.File!.SaveToFileAsync ("/out.tiff", fs, cts.Token);

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("Operation was cancelled", save.Error);
		Assert.IsGreaterThanOrEqualTo (1, fs.CreateCalls);
		Assert.IsLessThanOrEqualTo (ChunkBoundedFileSystem.MaxChunk, fs.MaxWrite);
		Assert.IsFalse (fs.FileExists ("/out.tiff"));
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
		CollectionAssert.AreEqual (data, fs.Store.ReadAllBytes ("/in.tiff"));
	}

	[TestMethod]
	public async Task JpegPathSave_SuccessfulCopy_DisposesTheSource ()
	{
		var (file, fs) = OpenJpeg ();
		fs.ArmOpenTracking = true;

		var save = await file.SaveToFileAsync ("/out.jpg", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
		Assert.IsTrue (fs.FileExists ("/out.jpg"));
	}

	[TestMethod]
	public async Task JpegPathSave_ShortSource_DisposesTheSource ()
	{
		var (file, fs) = OpenJpeg ();
		fs.Add ("/in.jpg", [1]);
		fs.ArmOpenTracking = true;

		var save = await file.SaveToFileAsync ("/out.jpg", fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("The source ended before the requested bytes were copied", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.jpg"));
		Assert.IsNotNull (fs.SaveOpen);
		Assert.IsTrue (fs.SaveOpen.WasDisposed);
	}

	static (StillImageFile File, FaultFileSystem Fs) OpenJpeg ()
	{
		var data = Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()));
		var fs = new FaultFileSystem ();
		fs.Add ("/in.jpg", data);
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		return (read.File!, fs);
	}

	static (StillImageFile File, FaultFileSystem Fs) OpenTruncated (string format)
	{
		byte[] data = format switch {
			"png" => Concat (PngSignature, PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0])),
			"gif" => GifScreen (1, 1, flags: 0),
			_ => Concat (
				[0xFF, 0xD8],
				JpegSegment (0xC0, [0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00]),
				[0xFF, 0xFE, 0x00, 0x10])
		};
		var fs = new FaultFileSystem ();
		fs.Add ("/in." + format, data);
		var read = StillImageFile.ReadFromFile ("/in." + format, fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		return (read.File!, fs);
	}

	static CancellationToken Cancelled ()
	{
		var cts = new CancellationTokenSource ();
		cts.Cancel ();
		return cts.Token;
	}

	sealed class FaultFileSystem : IFileSystem
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

			var tracked = new TrackedStream (Store.OpenRead (path), Seekable, LengthFault);
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
	}

	sealed class TrackedStream : Stream
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
		readonly FaultFileSystem _owner;

		public RecordingStream (Stream inner, FaultFileSystem owner)
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
