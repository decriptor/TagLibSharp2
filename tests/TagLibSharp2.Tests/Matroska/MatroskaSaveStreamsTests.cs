// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// A path save copies Matroska cluster bytes. It does not read or write the file as one buffer,
/// and the bytes match <see cref="MatroskaFile.Render"/>.
/// </summary>
public partial class MatroskaFileTests
{
	const int ClusterBytes = 256 * 1024;

	[TestMethod]
	public void SaveToFile_LargeCluster_CopiesItInChunks ()
	{
		var (file, original, expected, fs) = OpenLarge (withPosition: false, withSeek: false);
		Check (original, expected, fs, () => file.SaveToFile ("/out.mkv", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_LargeCluster_CopiesItInChunks ()
	{
		var (file, original, expected, fs) = OpenLarge (withPosition: false, withSeek: false);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.mkv", fs));
	}

	[TestMethod]
	public void SaveToFile_LargeClusterWithPosition_UpdatesThePosition ()
	{
		var (file, original, expected, fs) = OpenLarge (withPosition: true, withSeek: false);
		Check (original, expected, fs, () => file.SaveToFile ("/out.mkv", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_LargeClusterWithPosition_UpdatesThePosition ()
	{
		var (file, original, expected, fs) = OpenLarge (withPosition: true, withSeek: false);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.mkv", fs));
	}

	[TestMethod]
	public void SaveToFile_LargeClusterWithSeek_UpdatesTheSeek ()
	{
		var (file, original, expected, fs) = OpenLarge (withPosition: false, withSeek: true);
		Check (original, expected, fs, () => file.SaveToFile ("/out.mkv", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_LargeClusterWithSeek_UpdatesTheSeek ()
	{
		var (file, original, expected, fs) = OpenLarge (withPosition: false, withSeek: true);
		await CheckAsync (original, expected, fs, () => file.SaveToFileAsync ("/out.mkv", fs));
	}

	static (MatroskaFile File, byte[] Original, byte[] Expected, ChunkBoundedFileSystem Fs) OpenLarge (bool withPosition, bool withSeek)
	{
		var original = LargeFile (withPosition, withSeek);
		Assert.IsTrue (original.Length > ChunkBoundedFileSystem.MaxChunk, $"file is {original.Length} bytes");
		var expected = Expected (original);
		Assert.IsTrue (expected.Length > original.Length, "the new artist must grow the file");
		var fs = new ChunkBoundedFileSystem (Math.Max (original.Length, expected.Length) + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.mkv", original);
		var opened = MediaFile.Read ("/in.mkv", fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = (MatroskaFile)opened.File!;
		file.Tag.Artist = "Added";
		fs.ResetSourceLengthGets ();
		return (file, original, expected, fs);
	}

	static byte[] Expected (byte[] original)
	{
		var oracle = MatroskaFile.Read (original).File!;
		oracle.Tag.Artist = "Added";
		var rendered = oracle.Render (original);
		Assert.IsFalse (rendered.IsEmpty, "the oracle render failed");
		return rendered.ToArray ();
	}

	static void Check (byte[] original, byte[] expected, ChunkBoundedFileSystem fs, Func<FileWriteResult> save)
	{
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
		AssertSameBytes (expected, fs.Bytes ("/out.mkv"));
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

	static byte[] LargeFile (bool withPosition, bool withSeek)
	{
		var tags = Tags (Tag (50, Simple ("TITLE", "Movie")));
		var clusterAt = tags.Length;
		byte[]? seek = null;
		if (withSeek) {
			// Two-octet positions, so the head's length does not depend on the value.
			var probe = Master (MatroskaIds.SeekHead, SeekWide (MatroskaIds.Cluster, 0));
			clusterAt = probe.Length + tags.Length;
			seek = Master (MatroskaIds.SeekHead, SeekWide (MatroskaIds.Cluster, (ulong)clusterAt));
			Assert.AreEqual (probe.Length, seek.Length, "the seek head length changed with its position");
		}

		var cluster = LargeCluster (withPosition ? (ulong)clusterAt : null);
		var body = seek is null ? Concat (tags, cluster) : Concat (seek, tags, cluster);
		return Matroska (Unknown (0x18538067, body));
	}

	static byte[] SeekWide (ulong id, ulong position) =>
		Master (0x4DBB, Concat (Element (0x53AB, Id (id)), Element (0x53AC, new byte[] { (byte)(position >> 8), (byte)position })));

	static byte[] LargeCluster (ulong? position)
	{
		var samples = new byte[ClusterBytes];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = (byte)(i * 31);
		var covered = new List<byte[]> { Element (0xE7, new byte[] { 0 }) };
		if (position is ulong at)
			covered.Add (Element (0xA7, UIntBytes (at)));
		covered.Add (samples);
		var afterCrc = Concat (covered.ToArray ());
		var crc = new BinaryData (afterCrc).ComputeCrc32 ();
		var crcBytes = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (crcBytes, crc);
		return WideElement (MatroskaIds.Cluster, Concat (Element (0xBF, crcBytes), afterCrc));
	}
}
