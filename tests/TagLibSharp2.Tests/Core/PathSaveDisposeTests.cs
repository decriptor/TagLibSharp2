// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// <see cref="PathSave.Replace"/> closes the source before the destination is replaced,
/// including when the destination is never created.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class PathSaveDisposeTests
{
	[TestMethod]
	public async Task Probe_RoundTripsAFileWhenCreateSucceeds ()
	{
		var fs = new SaveProbeFileSystem (new byte[] { 1, 2, 3, 4 }, throwOnCreate: false);
		using (var readWrite = fs.OpenReadWrite (SaveProbeFileSystem.SourcePath))
			Assert.AreEqual (4, readWrite.Length);
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3, 4 }, fs.ReadAllBytes (SaveProbeFileSystem.SourcePath));
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3, 4 }, await fs.ReadAllBytesAsync (SaveProbeFileSystem.SourcePath));
		fs.WriteAllBytes ("/copy.bin", new byte[] { 9 });
		await fs.WriteAllBytesAsync ("/copy-async.bin", new byte[] { 8 });
		fs.Move ("/copy.bin", "/moved.bin");
		Assert.IsTrue (fs.FileExists ("/moved.bin"));
		fs.Delete ("/moved.bin");
		Assert.IsFalse (fs.FileExists ("/moved.bin"));
		Assert.AreEqual ("a.bin", fs.GetFileName ("/dir/a.bin"));
		Assert.AreEqual (Path.GetDirectoryName ("/dir/a.bin"), fs.GetDirectoryName ("/dir/a.bin"));
		Assert.AreEqual (Path.Combine ("a", "b"), fs.CombinePath ("a", "b"));
		using (var created = fs.Create ("/out.bin"))
			created.Write (new byte[] { 7, 7 }, 0, 2);
		CollectionAssert.AreEqual (new byte[] { 7, 7 }, fs.ReadAllBytes ("/out.bin"));
		var missing = Assert.ThrowsExactly<FileNotFoundException> (() => fs.ReadAllBytes ("/missing.bin"));
		Assert.AreEqual ("/missing.bin", missing.FileName);
		Assert.ThrowsExactly<FileNotFoundException> (() => fs.OpenRead ("/missing.bin"));
		Assert.ThrowsExactly<FileNotFoundException> (() => fs.Move ("/missing.bin", "/elsewhere.bin"));
	}

	[TestMethod]
	public void Replace_DestinationNeverCreated_DisposesTheSource ()
	{
		var stream = new RecordingStream ([1, 2, 3, 4]);
		var fs = new SaveProbeFileSystem (source: null, throwOnCreate: true);
		var opened = new OpenedFile (stream, stream.Length, fs);
		try {
			var save = PathSave.Replace ("/out.bin", opened, _ => { });
			Assert.IsFalse (save.IsSuccess, save.Error);
			Assert.AreEqual ("I/O error writing file: missing parent", save.Error);
			Assert.IsFalse (fs.FileExists ("/out.bin"));
			Assert.IsTrue (stream.WasDisposed, "source stream still open after Create threw");
		} finally {
			opened.Dispose ();
		}
	}

	[TestMethod]
	public void Replace_SuccessfulWrite_DisposesTheSource ()
	{
		var payload = new byte[] { 9, 8, 7, 6 };
		var stream = new RecordingStream (payload);
		var fs = new SaveProbeFileSystem (source: null, throwOnCreate: false);
		var opened = new OpenedFile (stream, stream.Length, fs);
		try {
			var save = PathSave.Replace ("/out.bin", opened, output => output.Write (payload, 0, payload.Length));
			Assert.IsTrue (save.IsSuccess, save.Error);
			CollectionAssert.AreEqual (payload, fs.ReadAllBytes ("/out.bin"));
			Assert.IsTrue (stream.WasDisposed, "source stream still open after a successful replace");
		} finally {
			opened.Dispose ();
		}
	}
}

/// <summary>
/// A file system whose <see cref="IFileSystem.Create"/> can fail before any bytes are written.
/// <see cref="Opened"/> is the stream returned for the source file.
/// </summary>
internal sealed class SaveProbeFileSystem : IFileSystem
{
	public const string SourcePath = "/in.dff";

	readonly Dictionary<string, byte[]> _files = new (StringComparer.Ordinal);
	readonly bool _throwOnCreate;

	public SaveProbeFileSystem (byte[]? source, bool throwOnCreate)
	{
		_throwOnCreate = throwOnCreate;
		if (source is not null)
			_files[SourcePath] = source;
	}

	public RecordingStream? Opened { get; private set; }

	public bool FileExists (string path) => _files.ContainsKey (path);

	public Stream OpenRead (string path)
	{
		if (!_files.TryGetValue (path, out var data))
			throw new FileNotFoundException ("File not found", path);

		var stream = new RecordingStream (data);
		Opened = stream;
		return stream;
	}

	public Stream OpenReadWrite (string path) => OpenRead (path);

	public Stream Create (string path)
	{
		if (_throwOnCreate)
			throw new DirectoryNotFoundException ("missing parent");

		var stream = new Stored (this, path);
		_files[path] = [];
		return stream;
	}

	public byte[] ReadAllBytes (string path)
	{
		if (!_files.TryGetValue (path, out var data))
			throw new FileNotFoundException ("File not found", path);
		return data;
	}

	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		return Task.FromResult (ReadAllBytes (path));
	}

	public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => _files[path] = data.ToArray ();

	public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		WriteAllBytes (path, data.Span);
		return Task.CompletedTask;
	}

	public void Delete (string path) => _files.Remove (path);

	public void Move (string sourcePath, string destinationPath)
	{
		if (!_files.TryGetValue (sourcePath, out var data))
			throw new FileNotFoundException ("Source file not found", sourcePath);
		_files[destinationPath] = data;
		_files.Remove (sourcePath);
	}

	public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

	public string GetFileName (string path) => Path.GetFileName (path);

	public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);

	sealed class Stored : MemoryStream
	{
		readonly SaveProbeFileSystem _owner;
		readonly string _path;
		bool _committed;

		public Stored (SaveProbeFileSystem owner, string path)
		{
			_owner = owner;
			_path = path;
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing && !_committed) {
				_committed = true;
				_owner._files[_path] = ToArray ();
			}

			base.Dispose (disposing);
		}
	}
}

/// <summary>
/// A read-only stream that records <see cref="Dispose"/>.
/// </summary>
internal sealed class RecordingStream : MemoryStream
{
	public RecordingStream (byte[] buffer)
		: base (buffer, writable: false)
	{
	}

	public bool WasDisposed { get; private set; }

	protected override void Dispose (bool disposing)
	{
		if (disposing)
			WasDisposed = true;
		base.Dispose (disposing);
	}
}
