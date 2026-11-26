// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aiff;

/// <summary>
/// A path save copies AIFF sound bytes. It does not read or write the file as one buffer,
/// and the bytes match <see cref="AiffFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public sealed class AiffSaveStreamsAudioTests
{
	const int AudioBytes = (256 * 1024) + 1;

	[TestMethod]
	public void SaveToFile_CopiesSoundWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Id3v2Tag = new Id3v2Tag { Title = "Changed" };
		var expected = Expected (original);
		Check (original, expected, fs, () => file.SaveToFile ("/out.aiff", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesSoundWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Id3v2Tag = new Id3v2Tag { Title = "Changed" };
		var expected = Expected (original);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.aiff", fs));
	}

	/// <summary>
	/// A windowed save opens the source. The source is closed when the destination directory is missing.
	/// </summary>
	[TestMethod]
	public void SaveToFile_DestinationNeverCreated_DisposesTheSource ()
	{
		var original = AiffWithSound (new byte[] { 1, 2, 3, 4 });
		var fs = new SaveProbeFileSystem (original, throwOnCreate: true);
		using var readStream = new MemoryStream (original, writable: false);
		var read = AiffFile.Read (readStream, SaveProbeFileSystem.SourcePath, fs);
		Assert.IsTrue (read.IsSuccess, read.Error);

		var save = read.File!.SaveToFile ("/out.aiff", fs);

		Assert.IsFalse (save.IsSuccess, save.Error);
		Assert.AreEqual ("I/O error writing file: missing parent", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.aiff"));
		Assert.IsNotNull (fs.Opened);
		Assert.IsTrue (fs.Opened.WasDisposed, "source stream still open after Create threw");
	}

	static byte[] Expected (byte[] original)
	{
		var oracle = AiffFile.Read (original).File!;
		oracle.Id3v2Tag = new Id3v2Tag { Title = "Changed" };
		return oracle.Render ().ToArray ();
	}

	static (AiffFile File, byte[] Original, ChunkBoundedFileSystem Fs) Open ()
	{
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);

		var original = AiffWithSound (audio);
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.aiff", original);
		var opened = MediaFile.Read ("/in.aiff", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		return ((AiffFile)opened.File!, original, fs);
	}

	static byte[] AiffWithSound (byte[] audio)
	{
		var ssnd = 8 + audio.Length;
		var pad = ssnd & 1;
		using var data = new MemoryStream ();
		WriteAscii (data, "FORM");
		WriteU32 (data, 0);
		WriteAscii (data, "AIFF");
		WriteAscii (data, "COMM");
		WriteU32 (data, 18);
		WriteU16 (data, 1);
		WriteU32 (data, (uint)audio.Length);
		WriteU16 (data, 8);
		data.Write (new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0, 0, 0, 0, 0, 0 }, 0, 10);
		WriteAscii (data, "SSND");
		WriteU32 (data, (uint)ssnd);
		WriteU32 (data, 0);
		WriteU32 (data, 0);
		data.Write (audio, 0, audio.Length);
		if (pad == 1)
			data.WriteByte (0);

		var bytes = data.ToArray ();
		BinaryPrimitives.WriteUInt32BigEndian (bytes.AsSpan (4), (uint)(bytes.Length - 8));
		return bytes;
	}

	static void WriteAscii (Stream stream, string text)
	{
		var bytes = System.Text.Encoding.ASCII.GetBytes (text);
		stream.Write (bytes, 0, bytes.Length);
	}

	static void WriteU16 (Stream stream, ushort value)
	{
		Span<byte> bytes = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16BigEndian (bytes, value);
		stream.Write (bytes.ToArray (), 0, 2);
	}

	static void WriteU32 (Stream stream, uint value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian (bytes, value);
		stream.Write (bytes, 0, 4);
	}

	static void Check (byte[] original, byte[] expected, ChunkBoundedFileSystem fs, Func<FileWriteResult> save)
	{
		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = save ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		AssertSaved (original, expected, fs, result, allocated);
	}

	static async Task CheckAsync (
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		Func<Task<FileWriteResult>> save)
	{
		fs.ResetSourceLengthGets ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = await save ();
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		AssertSaved (original, expected, fs, result, allocated);
	}

	static void AssertSaved (
		byte[] original,
		byte[] expected,
		ChunkBoundedFileSystem fs,
		FileWriteResult save,
		long allocated)
	{
		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets, "source length reads");
		Assert.IsTrue (
			allocated < original.Length / 2,
			$"save allocated {allocated} bytes for a {original.Length} byte file");
		AssertSameBytes (expected, fs.Bytes ("/out.aiff"));
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
}
