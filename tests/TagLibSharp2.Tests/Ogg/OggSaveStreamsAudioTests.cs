// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

/// <summary>
/// A path save of Vorbis, Opus, and Ogg FLAC must match an in-memory render
/// without reading or writing the audio as one buffer.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ogg")]
public sealed class OggSaveStreamsAudioTests
{
	const int AudioPages = 80;
	const int AudioPacketBytes = 8000;

	[TestMethod]
	[DataRow ("vorbis")]
	[DataRow ("opus")]
	[DataRow ("flac")]
	public void SaveToFile_RewritesAudioOnePageAtATime (string kind)
	{
		var original = Build (kind);
		var fs = new PageSizedFileSystem (original.Length + (64 * 1024));
		fs.Add ("/in.ogg", original);

		var read = MediaFile.Read ("/in.ogg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.Tag!.Title = "Changed";

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = Save (read.File!, "/out.ogg", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (Expected (kind, original), fs.Bytes ("/out.ogg"));
	}

	[TestMethod]
	[DataRow ("vorbis")]
	[DataRow ("opus")]
	[DataRow ("flac")]
	public async Task SaveToFileAsync_RewritesAudioOnePageAtATime (string kind)
	{
		var original = Build (kind);
		var fs = new PageSizedFileSystem (original.Length + (64 * 1024));
		fs.Add ("/in.ogg", original);

		var read = MediaFile.Read ("/in.ogg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.Tag!.Title = "Changed";

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await SaveAsync (read.File!, "/out.ogg", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (Expected (kind, original), fs.Bytes ("/out.ogg"));
	}

	/// <summary>
	/// The page that finishes the last header packet stays in the header prefix.
	/// A later continuation of an audio packet that began on that page is audio,
	/// so the end-of-stream flag belongs only on the real last page.
	/// </summary>
	[TestMethod]
	public void SaveToFile_ContinuedOpusAudio_MatchesFullFileRender ()
	{
		AssertPathMatchesRender ("opus", BuildContinuedOpus ());
	}

	/// <summary>
	/// The page after the last Opus header packet is audio even when its continuation bit is clear.
	/// Stopping only when that bit is set keeps the audio page in the header prefix.
	/// </summary>
	[TestMethod]
	public void SaveToFile_ClearBitOpusAudio_MatchesFullFileRender ()
	{
		AssertPathMatchesRender ("opus", BuildContinuedOpus (OggPageFlags.None));
	}

	/// <summary>
	/// Once the three Vorbis header packets are complete, a fresh page whose body
	/// starts with the identification type is the next chain, not another header.
	/// </summary>
	[TestMethod]
	public void SaveToFile_ChainedVorbisPage_MatchesFullFileRender ()
	{
		AssertPathMatchesRender ("vorbis", BuildChainedVorbis ());
	}

	[TestMethod]
	public void CopyRenumberedPages_DoesNotSeek ()
	{
		var file = Build ("vorbis");
		var counting = new SeekCountingStream (new MemoryStream (file, writable: false));
		using var dest = new MemoryStream ();

		OggPageHelper.CopyRenumberedPages (counting, dest, serialNumber: 0x01020304, startSequence: 7);

		var expected = OggPageHelper.RenumberAudioPages (file, 0x01020304, 7);
		AssertSameBytes (expected, dest.ToArray ());
		Assert.AreEqual (0, counting.PositionSets, "page copy rewound the stream");
	}

	[TestMethod]
	public void CopyRenumberedPages_DoesNotStatEachPage ()
	{
		var file = Build ("vorbis");
		var counting = new LengthCountingStream (new MemoryStream (file, writable: false));
		using var dest = new MemoryStream ();

		OggPageHelper.CopyRenumberedPages (counting, dest, serialNumber: 0x01020304, startSequence: 7);

		var expected = OggPageHelper.RenumberAudioPages (file, 0x01020304, 7);
		AssertSameBytes (expected, dest.ToArray ());
		// FileStream.Length is an fstat on Unix. Once per page dominated the copy.
		Assert.AreEqual (1, counting.LengthGets, "page copy asked for the stream length");
	}

	static FileWriteResult Save (object file, string path, IFileSystem fileSystem) => file switch {
		OggVorbisFile vorbis => vorbis.SaveToFile (path, fileSystem),
		OggOpusFile opus => opus.SaveToFile (path, fileSystem),
		OggFlacFile flac => flac.SaveToFile (path, fileSystem),
		_ => throw new AssertFailedException (file.GetType ().Name)
	};

	static Task<FileWriteResult> SaveAsync (object file, string path, IFileSystem fileSystem) => file switch {
		OggVorbisFile vorbis => vorbis.SaveToFileAsync (path, fileSystem),
		OggOpusFile opus => opus.SaveToFileAsync (path, fileSystem),
		OggFlacFile flac => flac.SaveToFileAsync (path, fileSystem),
		_ => throw new AssertFailedException (file.GetType ().Name)
	};

	static byte[] Expected (string kind, byte[] original)
	{
		switch (kind) {
			case "vorbis":
				var vorbis = OggVorbisFile.Read (original);
				Assert.IsTrue (vorbis.IsSuccess, vorbis.Error);
				vorbis.File!.VorbisComment!.Title = "Changed";
				return vorbis.File.Render (original).ToArray ();
			case "opus":
				var opus = OggOpusFile.Read (original);
				Assert.IsTrue (opus.IsSuccess, opus.Error);
				opus.File!.VorbisComment!.Title = "Changed";
				return opus.File.Render (original).ToArray ();
			case "flac":
				var flac = OggFlacFile.Read (original);
				Assert.IsTrue (flac.IsSuccess, flac.Error);
				flac.File!.VorbisComment!.Title = "Changed";
				return flac.File.Render (original);
			default:
				throw new AssertFailedException (kind);
		}
	}

	static void AssertPathMatchesRender (string kind, byte[] original)
	{
		var fs = new PageSizedFileSystem (original.Length + (64 * 1024));
		fs.Add ("/in.ogg", original);

		var read = MediaFile.Read ("/in.ogg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.Tag!.Title = "Changed";

		var save = Save (read.File!, "/out.ogg", fs);
		Assert.IsTrue (save.IsSuccess, save.Error);
		AssertSameBytes (Expected (kind, original), fs.Bytes ("/out.ogg"));
	}

	/// <summary>
	/// OpusHead, then OpusTags sharing a page with a 255-byte audio segment,
	/// then that packet's 10-byte continuation, then a short final audio page.
	/// </summary>
	static byte[] BuildContinuedOpus (OggPageFlags audioFlags = OggPageFlags.Continuation)
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.Title = "Original";
		var tags = TestBuilders.Opus.CreateOpusTagsPacket (comment);
		Assert.IsTrue (tags.Length < 255, "OpusTags must finish before the continued audio segment");

		var audioStart = new byte[255];
		audioStart[0] = 0xF8;
		var mixed = new byte[tags.Length + audioStart.Length];
		tags.CopyTo (mixed, 0);
		audioStart.CopyTo (mixed, tags.Length);

		var continuation = new byte[10];
		continuation[0] = 0x11;
		var last = new byte[8];
		last[0] = 0xF8;

		using var builder = new BinaryDataBuilder ();
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Opus.CreateOpusHeadPacket (), 0, OggPageFlags.BeginOfStream));
		builder.Add (PageWithLacing (mixed, new byte[] { (byte)tags.Length, 255 }, 1, OggPageFlags.None));
		builder.Add (PageWithLacing (continuation, new byte[] { 10 }, 2, audioFlags));
		builder.Add (TestBuilders.Ogg.CreatePage (last, 3, OggPageFlags.EndOfStream));
		return builder.ToArray ();
	}

	/// <summary>
	/// Three Vorbis header pages, a fresh page whose body starts with 0x01, then one audio page.
	/// </summary>
	static byte[] BuildChainedVorbis ()
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.Title = "Original";
		var chain = new byte[] { 0x01, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73 };
		var audio = new byte[] { 0x00, 1, 2, 3, 4, 5, 6, 7 };

		using var builder = new BinaryDataBuilder ();
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateIdentificationPacket (), 0, OggPageFlags.BeginOfStream));
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateCommentPacket (comment), 1, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateSetupPacket (), 2, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (chain, 3, OggPageFlags.None));
		builder.Add (TestBuilders.Ogg.CreatePage (audio, 4, OggPageFlags.EndOfStream));
		return builder.ToArray ();
	}

	static byte[] PageWithLacing (byte[] body, byte[] lacing, uint sequence, OggPageFlags flags)
	{
		var laced = 0;
		foreach (var size in lacing)
			laced += size;
		Assert.AreEqual (body.Length, laced, "lacing does not cover the page body");

		var page = new byte[27 + lacing.Length + body.Length];
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';
		page[5] = (byte)flags;
		page[14] = 1;
		page[18] = (byte)sequence;
		page[19] = (byte)(sequence >> 8);
		page[20] = (byte)(sequence >> 16);
		page[21] = (byte)(sequence >> 24);
		page[26] = (byte)lacing.Length;
		lacing.CopyTo (page, 27);
		body.CopyTo (page, 27 + lacing.Length);

		var crc = OggCrc.Calculate (page);
		page[22] = (byte)crc;
		page[23] = (byte)(crc >> 8);
		page[24] = (byte)(crc >> 16);
		page[25] = (byte)(crc >> 24);
		return page;
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

	static byte[] Build (string kind)
	{
		var comment = new VorbisComment ("TagLibSharp2");
		comment.Title = "Original";
		using var builder = new BinaryDataBuilder ();

		switch (kind) {
			case "vorbis":
				builder.Add (TestBuilders.Ogg.CreatePage (
					TestBuilders.Ogg.CreateIdentificationPacket (), 0, OggPageFlags.BeginOfStream));
				builder.Add (TestBuilders.Ogg.CreatePage (
					TestBuilders.Ogg.CreateCommentPacket (comment), 1, OggPageFlags.None));
				builder.Add (TestBuilders.Ogg.CreatePage (
					TestBuilders.Ogg.CreateSetupPacket (), 2, OggPageFlags.None));
				AddAudio (builder, 3, 0x00);
				break;
			case "opus":
				builder.Add (TestBuilders.Ogg.CreatePage (
					TestBuilders.Opus.CreateOpusHeadPacket (), 0, OggPageFlags.BeginOfStream));
				builder.Add (TestBuilders.Ogg.CreatePage (
					TestBuilders.Opus.CreateOpusTagsPacket (comment), 1, OggPageFlags.None));
				AddAudio (builder, 2, 0xF8);
				break;
			case "flac":
				builder.Add (TestBuilders.OggFlac.CreateWithMetadata ("Original", "Artist"));
				AddAudio (builder, 2, 0xFF);
				break;
			default:
				throw new AssertFailedException (kind);
		}

		return builder.ToArray ();
	}

	static void AddAudio (BinaryDataBuilder builder, uint sequence, byte packetType)
	{
		var packet = new byte[AudioPacketBytes];
		packet[0] = packetType;
		for (var i = 0; i < AudioPages; i++) {
			packet[1] = (byte)i;
			var flags = i == AudioPages - 1 ? OggPageFlags.EndOfStream : OggPageFlags.None;
			builder.Add (TestBuilders.Ogg.CreatePage (packet, sequence + (uint)i, flags));
		}

		// A truncated OggS tail must be dropped, matching RenumberAudioPages.
		var tail = new byte[28];
		tail[0] = (byte)'O';
		tail[1] = (byte)'g';
		tail[2] = (byte)'g';
		tail[3] = (byte)'S';
		tail[26] = 1;
		tail[27] = 200;
		builder.Add (tail);
	}

	[TestMethod]
	public async Task PageBound_RejectsAReadOrWriteLargerThanOnePage ()
	{
		var limit = OggWindow.MaxPageBytes;
		var fs = new PageSizedFileSystem (limit);
		fs.Add ("/in.ogg", new byte[] { 1, 2, 3, 4 });
		var read = Assert.ThrowsExactly<InvalidOperationException> (() => fs.ReadAllBytes ("/in.ogg"));
		Assert.AreEqual ("ReadAllBytes of 4 bytes", read.Message);
		var readAsync = await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			async () => await fs.ReadAllBytesAsync ("/in.ogg"));
		Assert.AreEqual (read.Message, readAsync.Message);
		Assert.ThrowsExactly<NotSupportedException> (() => fs.OpenReadWrite ("/in.ogg"));

		var exact = new byte[limit];
		fs.WriteAllBytes ("/exact.ogg", exact);
		await fs.WriteAllBytesAsync ("/exact-async.ogg", exact);
		Assert.AreEqual (limit, fs.Bytes ("/exact.ogg").Length);
		var over = new byte[limit + 1];
		var write = Assert.ThrowsExactly<InvalidOperationException> (() => fs.WriteAllBytes ("/over.ogg", over));
		Assert.AreEqual ($"WriteAllBytes of {over.Length} bytes", write.Message);
		Assert.IsFalse (fs.FileExists ("/over.ogg"));
		fs.Move ("/exact.ogg", "/moved.ogg");
		fs.Delete ("/moved.ogg");
		Assert.IsFalse (fs.FileExists ("/moved.ogg"));
		Assert.AreEqual ("a.ogg", fs.GetFileName ("/dir/a.ogg"));

		using (var source = fs.OpenRead ("/in.ogg")) {
			var tooBig = Assert.ThrowsExactly<InvalidOperationException> (() => source.Read (over, 0, over.Length));
			Assert.AreEqual ($"read of {over.Length} bytes", tooBig.Message);
			Assert.ThrowsExactly<InvalidOperationException> (() => source.Read (over.AsSpan ()));
			Assert.ThrowsExactly<InvalidOperationException> (() => source.Write (over, 0, over.Length));
			Assert.ThrowsExactly<InvalidOperationException> (() => source.Write (over.AsSpan ()));
			Assert.AreEqual (4, source.Read (new byte[4], 0, 4));
		}

		using (var created = fs.Create ("/out.ogg")) {
			Assert.IsFalse (created.CanRead);
			Assert.IsTrue (created.CanSeek);
			created.Flush ();
			Assert.ThrowsExactly<NotSupportedException> (() => created.Read (new byte[1], 0, 1));
			Assert.ThrowsExactly<NotSupportedException> (() => created.Read (new byte[1].AsSpan ()));
			Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => created.Seek (0, (SeekOrigin)99));
			Assert.ThrowsExactly<IOException> (() => created.Seek (-1, SeekOrigin.Begin));
			var oversized = Assert.ThrowsExactly<InvalidOperationException> (() => created.Write (over, 0, over.Length));
			Assert.AreEqual ($"write of {over.Length} bytes", oversized.Message);
			Assert.ThrowsExactly<InvalidOperationException> (() => created.Write (over.AsSpan ()));
			created.Write (exact, 0, exact.Length);
			created.Position = exact.Length;
			Assert.ThrowsExactly<IOException> (() => created.Write (new byte[] { 1 }, 0, 1));
			created.Seek (0, SeekOrigin.End);
			created.SetLength (1);
			Assert.AreEqual (1, created.Length);
		}

		await using (var counted = new LengthCountingStream (new MemoryStream (new byte[] { 1, 2, 3, 4 }))) {
			Assert.AreEqual (4, counted.Length);
			Assert.AreEqual (1, counted.LengthGets);
			counted.Position = 1;
			Assert.AreEqual (1, counted.Read (new byte[1].AsSpan ()));
			counted.Write (new byte[] { 9 }, 0, 1);
			counted.Flush ();
		}

		await using (var seeking = new SeekCountingStream (new MemoryStream ())) {
			seeking.SetLength (4);
			seeking.Position = 2;
			Assert.AreEqual (1, seeking.PositionSets);
			Assert.AreEqual (4, seeking.Length);
			var sought = Assert.ThrowsExactly<InvalidOperationException> (() => seeking.Seek (0, SeekOrigin.Begin));
			Assert.AreEqual ("page copy called Seek", sought.Message);
			seeking.Flush ();
			Assert.AreEqual (1, seeking.Read (new byte[1], 0, 1));
			seeking.Write (new byte[] { 1 }, 0, 1);
		}
	}

	/// <summary>
	/// Rejects a read or write larger than one Ogg page. A save that loads the audio fails here.
	/// The write buffer is allocated by the caller so the save's allocation budget does not include it.
	/// </summary>
	sealed class PageSizedFileSystem : IFileSystem
	{
		readonly Dictionary<string, Entry> _files = new (StringComparer.Ordinal);
		readonly byte[] _scratch;

		public PageSizedFileSystem (int writeCapacity) => _scratch = new byte[writeCapacity];

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
			return new BoundedStream (new MemoryStream (entry.Data, 0, entry.Count, writable: false));
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
			if (data.Length > OggWindow.MaxPageBytes)
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
	}

	/// <summary>
	/// Writes into a buffer allocated before the save is measured.
	/// </summary>
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
			RejectOversized (count, "write");
			Copy (buffer.AsSpan (offset, count));
		}

		public override void Write (ReadOnlySpan<byte> buffer)
		{
			RejectOversized (buffer.Length, "write");
			Copy (buffer);
		}

		public override int Read (Span<byte> buffer) => throw new NotSupportedException ();

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
	}

	sealed class BoundedStream : Stream
	{
		readonly MemoryStream _inner;

		public BoundedStream (MemoryStream inner) => _inner = inner;

		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => _inner.CanSeek;
		public override bool CanWrite => _inner.CanWrite;
		public override long Length => _inner.Length;
		public override long Position { get => _inner.Position; set => _inner.Position = value; }

		public override void Flush () => _inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count)
		{
			RejectOversized (count, "read");
			return _inner.Read (buffer, offset, count);
		}

		public override int Read (Span<byte> buffer)
		{
			RejectOversized (buffer.Length, "read");
			return _inner.Read (buffer);
		}

		public override void Write (byte[] buffer, int offset, int count)
		{
			RejectOversized (count, "write");
			_inner.Write (buffer, offset, count);
		}

		public override void Write (ReadOnlySpan<byte> buffer)
		{
			RejectOversized (buffer.Length, "write");
			_inner.Write (buffer);
		}

		public override long Seek (long offset, SeekOrigin origin) => _inner.Seek (offset, origin);

		public override void SetLength (long value) => _inner.SetLength (value);

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_inner.Dispose ();
			base.Dispose (disposing);
		}
	}

	/// <summary>
	/// Counts <see cref="Length"/> reads. <see cref="FileStream.Length"/> is an fstat on Unix.
	/// </summary>
	sealed class LengthCountingStream : Stream
	{
		readonly Stream _inner;

		public LengthCountingStream (Stream inner) => _inner = inner;

		public int LengthGets { get; private set; }

		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => _inner.CanSeek;
		public override bool CanWrite => _inner.CanWrite;

		public override long Length {
			get {
				LengthGets++;
				return _inner.Length;
			}
		}

		public override long Position {
			get => _inner.Position;
			set => _inner.Position = value;
		}

		public override void Flush () => _inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count) =>
			_inner.Read (buffer, offset, count);

		public override int Read (Span<byte> buffer) => _inner.Read (buffer);

		public override long Seek (long offset, SeekOrigin origin) => _inner.Seek (offset, origin);

		public override void SetLength (long value) => _inner.SetLength (value);

		public override void Write (byte[] buffer, int offset, int count) =>
			_inner.Write (buffer, offset, count);

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_inner.Dispose ();
			base.Dispose (disposing);
		}
	}

	/// <summary>
	/// Counts <see cref="Position"/> assignments. Reads still advance the inner stream.
	/// </summary>
	sealed class SeekCountingStream : Stream
	{
		readonly Stream _inner;

		public SeekCountingStream (Stream inner) => _inner = inner;

		public int PositionSets { get; private set; }

		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => _inner.CanSeek;
		public override bool CanWrite => _inner.CanWrite;
		public override long Length => _inner.Length;

		public override long Position {
			get => _inner.Position;
			set {
				PositionSets++;
				_inner.Position = value;
			}
		}

		public override void Flush () => _inner.Flush ();

		public override int Read (byte[] buffer, int offset, int count) =>
			_inner.Read (buffer, offset, count);

		public override long Seek (long offset, SeekOrigin origin) =>
			throw new InvalidOperationException ("page copy called Seek");

		public override void SetLength (long value) => _inner.SetLength (value);

		public override void Write (byte[] buffer, int offset, int count) =>
			_inner.Write (buffer, offset, count);

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_inner.Dispose ();
			base.Dispose (disposing);
		}
	}

	static void RejectOversized (int count, string operation)
	{
		if (count > OggWindow.MaxPageBytes)
			throw new InvalidOperationException ($"{operation} of {count} bytes");
	}
}
