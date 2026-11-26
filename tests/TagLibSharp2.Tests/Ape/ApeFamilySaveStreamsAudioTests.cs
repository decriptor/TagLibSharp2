// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Musepack;
using TagLibSharp2.WavPack;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// A path save copies WavPack, Monkey's Audio, and Musepack audio.
/// It does not read or write the file as one buffer, and the bytes match each format's Render.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ape")]
public sealed class ApeFamilySaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;

	[TestMethod]
	public void WavPack_SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open<WavPackFile> (".wv", TestBuilders.WavPack.CreateMinimal ());
		file.ApeTag!.Title = "Changed";
		Check (".wv", original, file.Render (original), fs, () => file.SaveToFile ("/out.wv", fs));
	}

	[TestMethod]
	public async Task WavPack_SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open<WavPackFile> (".wv", TestBuilders.WavPack.CreateMinimal ());
		file.ApeTag!.Title = "Changed";
		var expected = file.Render (original);
		await CheckAsync (".wv", original, expected, fs, () => file.SaveToFileAsync ("/out.wv", fs));
	}

	[TestMethod]
	public void MonkeysAudio_SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open<MonkeysAudioFile> (".ape", TestBuilders.MonkeysAudio.CreateMinimal ());
		file.ApeTag!.Title = "Changed";
		Check (".ape", original, file.Render (original), fs, () => file.SaveToFile ("/out.ape", fs));
	}

	[TestMethod]
	public async Task MonkeysAudio_SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open<MonkeysAudioFile> (".ape", TestBuilders.MonkeysAudio.CreateMinimal ());
		file.ApeTag!.Title = "Changed";
		var expected = file.Render (original);
		await CheckAsync (".ape", original, expected, fs, () => file.SaveToFileAsync ("/out.ape", fs));
	}

	[TestMethod]
	public void Musepack_SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open<MusepackFile> (".mpc", TestBuilders.Musepack.CreateMinimal ());
		file.ApeTag!.Title = "Changed";
		Check (".mpc", original, file.Render (original), fs, () => file.SaveToFile ("/out.mpc", fs));
	}

	[TestMethod]
	public async Task Musepack_SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open<MusepackFile> (".mpc", TestBuilders.Musepack.CreateMinimal ());
		file.ApeTag!.Title = "Changed";
		var expected = file.Render (original);
		await CheckAsync (".mpc", original, expected, fs, () => file.SaveToFileAsync ("/out.mpc", fs));
	}

	/// <summary>
	/// Monkey's Audio, WavPack, and Musepack share this write. A suffix that does not fit in one
	/// buffer is reported before an already-cancelled token, and the destination is not replaced.
	/// </summary>
	[TestMethod]
	public void WriteFromSource_TagPastOneBuffer_BeatsAnAlreadyCancelledToken ()
	{
		const string source = "/in.ape";
		const string destination = "/out.ape";
		var fs = HugeApeTail ();
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();

		Exception? thrown = null;
		FileWriteResult save = default;
		try {
			save = ApeTagLocation.WriteFromSource (source, destination, fs, [], cancelled.Token);
		} catch (Exception ex) {
			thrown = ex;
		}

		Assert.IsInstanceOfType<InvalidOperationException> (thrown, save.Error);
		Assert.AreEqual ("The APE tag does not fit in one buffer", thrown!.Message);
		Assert.IsFalse (fs.FileExists (destination), "destination was replaced");
		Assert.AreEqual (0, fs.Creates, "the size failure replaced the destination");
	}

	/// <summary>
	/// The same suffix throws when the token is not cancelled. This pins the fixture the cancelled test uses.
	/// </summary>
	[TestMethod]
	public void WriteFromSource_TagPastOneBuffer_Throws ()
	{
		var fs = HugeApeTail ();
		var thrown = Assert.ThrowsExactly<InvalidOperationException> (
			() => ApeTagLocation.WriteFromSource ("/in.ape", "/out.ape", fs, []));

		Assert.AreEqual ("The APE tag does not fit in one buffer", thrown.Message);
		Assert.IsFalse (fs.FileExists ("/out.ape"));
	}

	// ID3v1, the Lyrics3 end and size fields, and two APE footers. That is the tail the save probes.
	static HugeTailFileSystem HugeApeTail ()
	{
		var tail = new byte[128 + 9 + 6 + (ApeTagFooter.Size * 2)];
		var footer = ApeTagFooter.Create ((uint)((long)int.MaxValue + 1), 0).Render ();
		footer.CopyTo (tail.AsSpan (tail.Length - footer.Length));
		return new HugeTailFileSystem ("/in.ape", (long)int.MaxValue + 1, tail);
	}

	static (T File, byte[] Original, ChunkBoundedFileSystem Fs) Open<T> (string extension, byte[] header)
		where T : class
	{
		var original = Build (header);
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in" + extension, original);
		var opened = MediaFile.Read ("/in" + extension, fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		return ((T)opened.File!, original, fs);
	}

	static void Check (
		string extension,
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		Func<FileWriteResult> save)
	{
		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = save ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		AssertSaved (original, expected, fs, extension, result, allocated);
	}

	static async Task CheckAsync (
		string extension,
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		Func<Task<FileWriteResult>> save)
	{
		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = await save ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		AssertSaved (original, expected, fs, extension, result, allocated);
	}

	static void AssertSaved (
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		string extension,
		FileWriteResult save,
		long allocated)
	{
		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out" + extension));
	}

	static byte[] Build (byte[] header)
	{
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);

		var tag = TestBuilders.Ape.CreateTag ("Original");
		var file = new byte[header.Length + audio.Length + tag.Length];
		header.CopyTo (file, 0);
		audio.CopyTo (file, header.Length);
		tag.CopyTo (file, header.Length + audio.Length);
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
	public async Task HugeTail_DoesNotReplaceTheDestination_AndRejectsAFullFileRead ()
	{
		var tail = new byte[] { 9, 8, 7 };
		var fs = new HugeTailFileSystem ("/in.ape", length: 1_000_000, tail);
		Assert.ThrowsExactly<NotSupportedException> (() => fs.OpenReadWrite ("/in.ape"));
		Assert.ThrowsExactly<NotSupportedException> (() => fs.ReadAllBytes ("/in.ape"));
		await Assert.ThrowsExactlyAsync<NotSupportedException> (async () => await fs.ReadAllBytesAsync ("/in.ape"));
		Assert.ThrowsExactly<NotSupportedException> (() => fs.WriteAllBytes ("/out.ape", tail));
		await Assert.ThrowsExactlyAsync<NotSupportedException> (async () => await fs.WriteAllBytesAsync ("/out.ape", tail));
		fs.Delete ("/in.ape");
		var replaced = Assert.ThrowsExactly<InvalidOperationException> (() => fs.Move ("/in.ape", "/out.ape"));
		Assert.AreEqual ("destination was replaced", replaced.Message);
		Assert.AreEqual ("a.ape", fs.GetFileName ("/dir/a.ape"));
		Assert.AreEqual (Path.GetDirectoryName ("/dir/a.ape"), fs.GetDirectoryName ("/dir/a.ape"));

		using var source = fs.OpenRead ("/in.ape");
		Assert.IsTrue (source.CanRead);
		Assert.IsTrue (source.CanSeek);
		Assert.IsFalse (source.CanWrite);
		Assert.AreEqual (1_000_000, source.Length);
		source.Flush ();
		Assert.ThrowsExactly<NotSupportedException> (() => source.SetLength (1));
		Assert.ThrowsExactly<NotSupportedException> (() => source.Write (tail, 0, tail.Length));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => source.Seek (0, (SeekOrigin)99));
		source.Seek (1_000_000 - tail.Length, SeekOrigin.Begin);
		var got = new byte[tail.Length];
		Assert.AreEqual (tail.Length, source.Read (got, 0, got.Length));
		CollectionAssert.AreEqual (tail, got);
		source.Position = 0;
		Assert.AreEqual (0, source.Read (got, 0, got.Length));
		using var created = fs.Create ("/out.ape");
		Assert.AreEqual (1, fs.Creates);
		Assert.ThrowsExactly<FileNotFoundException> (() => fs.OpenRead ("/missing.ape"));
	}

	sealed class HugeTailFileSystem : IFileSystem
	{
		readonly string _source;
		readonly long _length;
		readonly byte[] _tail;

		public HugeTailFileSystem (string source, long length, byte[] tail)
		{
			_source = source;
			_length = length;
			_tail = tail;
		}

		public int Creates { get; private set; }

		public bool FileExists (string path) => path == _source;

		public Stream OpenRead (string path)
		{
			if (path != _source)
				throw new FileNotFoundException (path);
			return new TailStream (_length, _tail);
		}

		public Stream OpenReadWrite (string path) => throw new NotSupportedException ();

		public Stream Create (string path)
		{
			Creates++;
			return new MemoryStream ();
		}

		public byte[] ReadAllBytes (string path) => throw new NotSupportedException ();

		public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException ();

		public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => throw new NotSupportedException ();

		public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException ();

		public void Delete (string path)
		{
		}

		public void Move (string sourcePath, string destinationPath) =>
			throw new InvalidOperationException ("destination was replaced");

		public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);

		public string GetFileName (string path) => Path.GetFileName (path);

		public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);

		sealed class TailStream : Stream
		{
			readonly long _length;
			readonly byte[] _tail;
			long _position;

			public TailStream (long length, byte[] tail)
			{
				_length = length;
				_tail = tail;
			}

			public override bool CanRead => true;

			public override bool CanSeek => true;

			public override bool CanWrite => false;

			public override long Length => _length;

			public override long Position {
				get => _position;
				set {
					ArgumentOutOfRangeException.ThrowIfNegative (value);
					_position = value;
				}
			}

			public override void Flush ()
			{
			}

			public override int Read (byte[] buffer, int offset, int count) =>
				Read (buffer.AsSpan (offset, count));

			public override int Read (Span<byte> buffer)
			{
				var tailStart = _length - _tail.Length;
				if (_position < tailStart || _position >= _length)
					return 0;

				var index = (int)(_position - tailStart);
				var n = Math.Min (buffer.Length, _tail.Length - index);
				_tail.AsSpan (index, n).CopyTo (buffer);
				_position += n;
				return n;
			}

			public override long Seek (long offset, SeekOrigin origin)
			{
				var next = origin switch {
					SeekOrigin.Begin => offset,
					SeekOrigin.Current => _position + offset,
					SeekOrigin.End => _length + offset,
					_ => throw new ArgumentOutOfRangeException (nameof (origin))
				};
				Position = next;
				return _position;
			}

			public override void SetLength (long value) => throw new NotSupportedException ();

			public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
		}
	}
}
