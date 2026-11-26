// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Dff;

/// <summary>
/// A path save copies the DSD payload. It does not read or write the file as one buffer,
/// and the bytes match <see cref="DffFile.Render"/>. Render and the span save stay one buffer.
/// Held sample bytes win when the file on disk differs, and that save does not open the source.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Dff")]
public sealed class DffSaveStreamsAudioTests
{
	const int AudioBytes = 256 * 1024;

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void SaveToFile_CopiesSoundWithoutLoadingTheFile (bool heldBytes)
	{
		var built = Build ();
		var (file, fs, expected) = Open (built, heldBytes);

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = file.SaveToFile ("/out.dff", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		AssertSaved (save, fs, expected, SourceLength (built, heldBytes), allocated, heldBytes);
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task SaveToFileAsync_CopiesSoundWithoutLoadingTheFile (bool heldBytes)
	{
		var built = Build ();
		var (file, fs, expected) = Open (built, heldBytes);

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await file.SaveToFileAsync ("/out.dff", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		AssertSaved (save, fs, expected, SourceLength (built, heldBytes), allocated, heldBytes);
	}

	/// <summary>
	/// Saving with no path writes the source. A windowed file is copied, not read as one buffer.
	/// </summary>
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task SaveToFileAsync_NoPath_CopiesSoundWithoutLoadingTheFile (bool heldBytes)
	{
		var built = Build ();
		var (file, fs, expected) = Open (built, heldBytes);

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = await file.SaveToFileAsync (fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (heldBytes ? 0 : 1, fs.SourceLengthGets, "source length reads");
		var originalLength = SourceLength (built, heldBytes);
		Assert.IsTrue (
			allocated < originalLength / 2,
			$"save allocated {allocated} bytes for a {originalLength} byte file");
		AssertSameBytes (expected, fs.Bytes ("/in.dff"));
	}

	/// <summary>
	/// The buffer argument is the file when nothing is held. Held bytes win, and the disk file is not read.
	/// </summary>
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task SaveToFileAsync_WithOriginalData_MatchesTheSpanOverload (bool heldBytes)
	{
		var built = Build ();
		var (file, _, expectedHeld) = Open (built, heldBytes);
		var supplied = (byte[])built.File.Clone ();
		supplied[built.AudioByte + 100] ^= 0xFF;

		var oracle = DffFile.Read (supplied);
		Assert.IsTrue (oracle.IsSuccess, oracle.Error);
		oracle.File!.EnsureId3v2Tag ().Title = "Changed";
		var expectedSupplied = oracle.File.Render ().ToArray ();

		var disk = new MockFileSystem ();
		disk.AddFile ("/in.dff", built.File);
		var save = await file.SaveToFileAsync ("/out.dff", supplied, disk);

		Assert.IsTrue (save.IsSuccess, save.Error);
		AssertSameBytes (heldBytes ? expectedHeld : expectedSupplied, disk.ReadAllBytes ("/out.dff"));
	}

	static int SourceLength (Built built, bool heldBytes) =>
		(heldBytes ? built.WithTrailer : built.File).Length;

	// No ID3 chunk and no tag edit. Render returns the original bytes, including a short tail
	// that a chunk walk would drop. The path save has to copy those bytes.
	[TestMethod]
	public void SaveToFile_UnchangedFile_CopiesWithoutLoadingTheFile ()
	{
		var original = BuildPlain ();
		var expected = DffFile.Read (original);
		Assert.IsTrue (expected.IsSuccess, expected.Error);
		Assert.IsTrue (expected.File!.Render ().Span.SequenceEqual (original));

		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.dff", original);
		var read = MediaFile.Read ("/in.dff", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);

		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var save = ((DffFile)read.File!).SaveToFile ("/out.dff", fs);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (allocated < original.Length / 2, $"save allocated {allocated} bytes");
		AssertSameBytes (original, fs.Bytes ("/out.dff"));
	}

	/// <summary>
	/// A windowed save opens the source. The source is closed when the destination directory is missing.
	/// </summary>
	[TestMethod]
	public void SaveToFile_DestinationNeverCreated_DisposesTheSource ()
	{
		var original = BuildPlain ();
		var fs = new SaveProbeFileSystem (original, throwOnCreate: true);
		using var readStream = new MemoryStream (original, writable: false);
		var read = DffFile.Read (readStream, "/in.dff", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);

		var save = read.File!.SaveToFile ("/out.dff", fs);

		Assert.IsFalse (save.IsSuccess, save.Error);
		Assert.AreEqual ("I/O error writing file: missing parent", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.dff"));
		Assert.IsNotNull (fs.Opened);
		Assert.IsTrue (fs.Opened.WasDisposed, "source stream still open after Create threw");
	}

	static void AssertSaved (
		FileWriteResult save,
		ChunkBoundedFileSystem fs,
		byte[] expected,
		int originalLength,
		long allocated,
		bool heldBytes)
	{
		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (heldBytes ? 0 : 1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < originalLength / 2,
			$"save allocated {allocated} bytes for a {originalLength} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.dff"));
	}

	static (DffFile File, ChunkBoundedFileSystem Fs, byte[] Expected) Open (Built built, bool heldBytes)
	{
		// A trailer after FRM8 is not a chunk. The windowed read appends the sound header after
		// that tail and then cannot see it, so the disk case leaves the trailer off.
		var source = heldBytes ? built.WithTrailer : built.File;
		var expected = heldBytes ? built.TrailerExpected : built.Expected;
		if (!heldBytes) {
			var fs = new ChunkBoundedFileSystem (expected.Length + ChunkBoundedFileSystem.MaxChunk);
			fs.Add ("/in.dff", source);
			var read = MediaFile.Read ("/in.dff", fs);
			Assert.IsTrue (read.IsSuccess, read.Error);
			var file = (DffFile)read.File!;
			file.EnsureId3v2Tag ().Title = "Changed";
			return (file, fs, expected);
		}

		var mock = new MockFileSystem ();
		mock.AddFile ("/in.dff", source);
		var stored = DffFile.ReadFromFile ("/in.dff", mock);
		Assert.IsTrue (stored.IsSuccess, stored.Error);
		var held = stored.File!;
		held.EnsureId3v2Tag ().Title = "Changed";

		var onDisk = (byte[])source.Clone ();
		onDisk[built.AudioByte] ^= 0xFF;
		var chunkFs = new ChunkBoundedFileSystem (expected.Length + ChunkBoundedFileSystem.MaxChunk);
		chunkFs.Add ("/in.dff", onDisk);
		return (held, chunkFs, expected);
	}

	readonly record struct Built (byte[] File, byte[] WithTrailer, int AudioByte, byte[] Expected, byte[] TrailerExpected);

	// A leading odd ID3 chunk sits in front of the sound data, so removing it moves the DSD
	// payload and the DSTI offset that points into it. An APE footer and ID3v1 tag sit after FRM8.
	static Built Build ()
	{
		using var ms = new MemoryStream ();
		ms.Write ("FRM8"u8);
		var sizeAt = ms.Position;
		WriteUInt64 (ms, 0);
		ms.Write ("DSD "u8);

		ms.Write ("FVER"u8);
		WriteUInt64 (ms, 4);
		WriteUInt32 (ms, 0x01050000);

		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizeAt = ms.Position;
		WriteUInt64 (ms, 0);
		ms.Write ("SND "u8);
		ms.Write ("FS  "u8);
		WriteUInt64 (ms, 4);
		WriteUInt32 (ms, 2822400);
		ms.Write ("CHNL"u8);
		WriteUInt64 (ms, 10);
		WriteUInt16 (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);
		ms.Write ("CMPR"u8);
		WriteUInt64 (ms, 19);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);
		var propSize = ms.Position - propStart - 12;
		var propEnd = ms.Position;
		ms.Position = propSizeAt;
		WriteUInt64 (ms, (ulong)propSize);
		ms.Position = propEnd;
		if ((propSize & 1) != 0)
			ms.WriteByte (0);

		var id3 = new Id3v2Tag { Title = "Old" }.Render ().ToArray ();
		if ((id3.Length & 1) == 0) {
			var odd = new byte[id3.Length + 1];
			id3.CopyTo (odd, 0);
			id3 = odd;
		}

		ms.Write ("ID3 "u8);
		WriteUInt64 (ms, (ulong)id3.Length);
		ms.Write (id3, 0, id3.Length);
		ms.WriteByte (0);

		var audioByte = (int)ms.Position + 12;
		ms.Write ("DSD "u8);
		WriteUInt64 (ms, (ulong)AudioBytes);
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);
		ms.Write (audio, 0, audio.Length);

		var marker = (ulong)(audioByte + 17);
		ms.Write ("DSTI"u8);
		WriteUInt64 (ms, 24);
		WriteUInt64 (ms, 16);
		WriteUInt32 (ms, 4);
		WriteUInt64 (ms, marker);
		WriteUInt32 (ms, 4);

		var formEnd = ms.Position;
		ms.Position = sizeAt;
		WriteUInt64 (ms, (ulong)(formEnd - 12));
		ms.Position = formEnd;

		var file = ms.ToArray ();
		var withTrailer = new byte[file.Length + 32 + Id3v1Tag.TagSize];
		file.CopyTo (withTrailer, 0);
		"APETAGEX"u8.CopyTo (withTrailer.AsSpan (file.Length));
		BinaryPrimitives.WriteUInt32LittleEndian (withTrailer.AsSpan (file.Length + 8), 2000);
		BinaryPrimitives.WriteUInt32LittleEndian (withTrailer.AsSpan (file.Length + 12), 32);
		withTrailer[file.Length + 32] = (byte)'T';
		withTrailer[file.Length + 33] = (byte)'A';
		withTrailer[file.Length + 34] = (byte)'G';

		return new Built (file, withTrailer, audioByte, ExpectedOf (file), ExpectedOf (withTrailer));
	}

	static byte[] BuildPlain ()
	{
		using var ms = new MemoryStream ();
		ms.Write ("FRM8"u8);
		var sizeAt = ms.Position;
		WriteUInt64 (ms, 0);
		ms.Write ("DSD "u8);
		ms.Write ("FVER"u8);
		WriteUInt64 (ms, 4);
		WriteUInt32 (ms, 0x01050000);
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizeAt = ms.Position;
		WriteUInt64 (ms, 0);
		ms.Write ("SND "u8);
		ms.Write ("FS  "u8);
		WriteUInt64 (ms, 4);
		WriteUInt32 (ms, 2822400);
		ms.Write ("CHNL"u8);
		WriteUInt64 (ms, 10);
		WriteUInt16 (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);
		ms.Write ("CMPR"u8);
		WriteUInt64 (ms, 19);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);
		var propSize = ms.Position - propStart - 12;
		var propEnd = ms.Position;
		ms.Position = propSizeAt;
		WriteUInt64 (ms, (ulong)propSize);
		ms.Position = propEnd;
		if ((propSize & 1) != 0)
			ms.WriteByte (0);
		ms.Write ("DSD "u8);
		WriteUInt64 (ms, (ulong)AudioBytes);
		ms.Write (new byte[AudioBytes], 0, AudioBytes);
		var formEnd = ms.Position;
		ms.Position = sizeAt;
		WriteUInt64 (ms, (ulong)(formEnd - 12));
		ms.Position = formEnd;
		ms.Write (new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
		return ms.ToArray ();
	}

	static byte[] ExpectedOf (byte[] file)
	{
		var parsed = DffFile.Read (file);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		parsed.File!.EnsureId3v2Tag ().Title = "Changed";
		return parsed.File.Render ().ToArray ();
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

	static void WriteUInt64 (Stream stream, ulong value)
	{
		Span<byte> bytes = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64BigEndian (bytes, value);
		stream.Write (bytes);
	}

	static void WriteUInt32 (Stream stream, uint value)
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian (bytes, value);
		stream.Write (bytes);
	}

	static void WriteUInt16 (Stream stream, ushort value)
	{
		Span<byte> bytes = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16BigEndian (bytes, value);
		stream.Write (bytes);
	}
}
