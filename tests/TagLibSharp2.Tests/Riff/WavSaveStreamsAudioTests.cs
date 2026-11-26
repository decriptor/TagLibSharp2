// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

/// <summary>
/// A path save copies WAV sample bytes. It does not read or write the file as one buffer,
/// and the bytes match <see cref="WavFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public sealed class WavSaveStreamsAudioTests
{
	const int AudioBytes = (256 * 1024) + 1;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.InfoTag = new RiffInfoTag { Title = "Changed" };
		var expected = Expected (original);
		Check (original, expected, fs, () => file.SaveToFile ("/out.wav", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.InfoTag = new RiffInfoTag { Title = "Changed" };
		var expected = Expected (original);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.wav", fs));
	}

	/// <summary>
	/// A windowed save opens the source. The source is closed when the destination directory is missing.
	/// </summary>
	[TestMethod]
	public void SaveToFile_DestinationNeverCreated_DisposesTheSource ()
	{
		var original = WavWithAudio (new byte[] { 1, 2, 3, 4 });
		var fs = new SaveProbeFileSystem (original, throwOnCreate: true);
		using var readStream = new MemoryStream (original, writable: false);
		var read = WavFile.Read (readStream, SaveProbeFileSystem.SourcePath, fs);
		Assert.IsTrue (read.IsSuccess, read.Error);

		var save = read.File!.SaveToFile ("/out.wav", fs);

		Assert.IsFalse (save.IsSuccess, save.Error);
		Assert.AreEqual ("I/O error writing file: missing parent", save.Error);
		Assert.IsFalse (fs.FileExists ("/out.wav"));
		Assert.IsNotNull (fs.Opened);
		Assert.IsTrue (fs.Opened.WasDisposed, "source stream still open after Create threw");
	}

	static byte[] Expected (byte[] original)
	{
		var oracle = WavFile.Read (original).File!;
		oracle.InfoTag = new RiffInfoTag { Title = "Changed" };
		return oracle.Render ().ToArray ();
	}

	static (WavFile File, byte[] Original, ChunkBoundedFileSystem Fs) Open ()
	{
		var audio = new byte[AudioBytes];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)((i * 17) % 200 + 1);

		var original = WavWithAudio (audio);
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.wav", original);
		var opened = MediaFile.Read ("/in.wav", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		return ((WavFile)opened.File!, original, fs);
	}

	static byte[] WavWithAudio (byte[] audio)
	{
		var pad = audio.Length & 1;
		var riffSize = 4 + (8 + 16) + (8 + audio.Length + pad);
		using var data = new MemoryStream ();
		WriteAscii (data, "RIFF");
		WriteU32 (data, (uint)riffSize);
		WriteAscii (data, "WAVE");
		WriteAscii (data, "fmt ");
		WriteU32 (data, 16);
		WriteU16 (data, 1);
		WriteU16 (data, 1);
		WriteU32 (data, 44100);
		WriteU32 (data, 88200);
		WriteU16 (data, 2);
		WriteU16 (data, 16);
		WriteAscii (data, "data");
		WriteU32 (data, (uint)audio.Length);
		data.Write (audio, 0, audio.Length);
		if (pad == 1)
			data.WriteByte (0);
		return data.ToArray ();
	}

	static void WriteAscii (Stream stream, string text)
	{
		var bytes = Encoding.ASCII.GetBytes (text);
		stream.Write (bytes, 0, bytes.Length);
	}

	static void WriteU16 (Stream stream, ushort value)
	{
		Span<byte> bytes = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian (bytes, value);
		stream.Write (bytes.ToArray (), 0, 2);
	}

	static void WriteU32 (Stream stream, uint value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (bytes, value);
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
		AssertSameBytes (expected, fs.Bytes ("/out.wav"));
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
