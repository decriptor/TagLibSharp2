// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aac;
using TagLibSharp2.Core;

using static TagLibSharp2.Tests.Aac.AacTestBuilder;

namespace TagLibSharp2.Tests.Aac;

/// <summary>
/// A path save copies AAC audio. It does not read or write the file as one buffer,
/// and the bytes match <see cref="AacFile.Render"/>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aac")]
public sealed class AacSaveStreamsAudioTests
{
	const int FrameLength = 64;
	const int FrameCount = (256 * 1024) / FrameLength;

	[TestMethod]
	public void SaveToFile_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Tag!.Title = "Changed";
		Check (original, file.Render (original).ToArray (), fs, () => file.SaveToFile ("/out.aac", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CopiesAudioWithoutLoadingTheFile ()
	{
		var (file, original, fs) = Open ();
		file.Tag!.Title = "Changed";
		var expected = file.Render (original).ToArray ();
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.aac", fs));
	}

	static (AacFile File, byte[] Original, ChunkBoundedFileSystem Fs) Open ()
	{
		var original = Build ();
		var fs = new ChunkBoundedFileSystem (original.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.aac", original);
		var opened = MediaFile.Read ("/in.aac", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		return ((AacFile)opened.File!, original, fs);
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
		AssertSameBytes (expected, fs.Bytes ("/out.aac"));
	}

	static byte[] Build ()
	{
		var frames = Adts (FrameCount, length: FrameLength);
		return Concat (Id3v2Bytes ("Front"), frames, ApeBytes ("Ape"), Lyrics3V2 (), Id3v1Bytes ("Back"));
	}

	// Lyrics3 v2 (https://id3.org/Lyrics3v2): body, a 6-digit size of that body, then LYRICS200.
	static byte[] Lyrics3V2 ()
	{
		var body = Concat ("LYRICSBEGIN"u8.ToArray (), "IND00003110"u8.ToArray (), "LYR00008la la la"u8.ToArray ());
		var size = System.Text.Encoding.ASCII.GetBytes (body.Length.ToString ("D6", System.Globalization.CultureInfo.InvariantCulture));
		return Concat (body, size, "LYRICS200"u8.ToArray ());
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
