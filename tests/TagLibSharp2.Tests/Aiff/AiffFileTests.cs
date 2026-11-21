// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public class AiffFileTests
{
	// AIFF format uses big-endian byte order and FORM container
	// Reference: http://paulbourke.net/dataformats/audio/

	[TestMethod]
	public void TryParse_ValidAiffFile_ReturnsTrue ()
	{
		// Minimal valid AIFF: FORM header + COMM chunk + SSND chunk
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		var result = AiffFile.TryRead (data, out var file);

		Assert.IsTrue (result);
		Assert.IsNotNull (file);
		Assert.IsTrue (file.IsValid);
		Assert.AreEqual ("AIFF", file.FormType);
	}

	[TestMethod]
	public void TryParse_InvalidMagic_ReturnsFalse ()
	{
		byte[] data = [
			0x52, 0x49, 0x46, 0x46, // "RIFF" instead of "FORM"
			0x00, 0x00, 0x00, 0x10, // Size
			0x41, 0x49, 0x46, 0x46, // "AIFF"
		];

		var result = AiffFile.TryRead (new BinaryData (data), out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_TooShort_ReturnsFalse ()
	{
		byte[] data = [0x46, 0x4F, 0x52, 0x4D]; // Just "FORM"

		var result = AiffFile.TryRead (new BinaryData (data), out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_AifcFormType_Succeeds ()
	{
		// AIFC is compressed AIFF
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000, isAifc: true);

		var result = AiffFile.TryRead (data, out var file);

		Assert.IsTrue (result);
		Assert.AreEqual ("AIFC", file!.FormType);
	}

	[TestMethod]
	public void AudioProperties_AifcWithCompression_ParsesCompressionType ()
	{
		// AIFC with NONE (uncompressed) compression type
		var data = CreateAifcFileWithCompression (44100, 16, 2, 1000, "NONE", "not compressed");

		AiffFile.TryRead (data, out var file);

		Assert.IsNotNull (file?.Properties);
		Assert.AreEqual ("NONE", file.Properties.CompressionType);
		Assert.AreEqual ("not compressed", file.Properties.CompressionName);
	}

	[TestMethod]
	public void AudioProperties_AifcWithSowtCompression_ParsesCorrectly ()
	{
		// AIFC with sowt (little-endian PCM) compression
		var data = CreateAifcFileWithCompression (44100, 16, 2, 1000, "sowt", "");

		AiffFile.TryRead (data, out var file);

		Assert.IsNotNull (file?.Properties);
		Assert.AreEqual ("sowt", file.Properties.CompressionType);
	}

	[TestMethod]
	public void AudioProperties_StandardAiff_HasNoCompression ()
	{
		// Standard AIFF has no compression fields
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000, isAifc: false);

		AiffFile.TryRead (data, out var file);

		Assert.IsNotNull (file?.Properties);
		Assert.IsNull (file.Properties.CompressionType);
		Assert.IsNull (file.Properties.CompressionName);
	}

	[TestMethod]
	public void AudioProperties_CommonChunk_ParsedCorrectly ()
	{
		var data = CreateMinimalAiffFile (
			sampleRate: 48000,
			bitsPerSample: 24,
			channels: 2,
			sampleFrames: 96000);

		AiffFile.TryRead (data, out var file);

		Assert.IsNotNull (file?.Properties);
		Assert.AreEqual (48000, file.Properties.SampleRate);
		Assert.AreEqual (24, file.Properties.BitsPerSample);
		Assert.AreEqual (2, file.Properties.Channels);
		Assert.AreEqual (96000u, file.Properties.SampleFrames);
	}

	[TestMethod]
	public void AudioProperties_Duration_CalculatedCorrectly ()
	{
		// 2 seconds of audio at 44100 Hz
		var data = CreateMinimalAiffFile (
			sampleRate: 44100,
			bitsPerSample: 16,
			channels: 2,
			sampleFrames: 88200);

		AiffFile.TryRead (data, out var file);

		Assert.AreEqual (TimeSpan.FromSeconds (2), file!.Properties!.Duration);
	}

	[TestMethod]
	public void AudioProperties_Bitrate_CalculatedCorrectly ()
	{
		// 44100 Hz, 16-bit, stereo = 1411.2 kbps
		var data = CreateMinimalAiffFile (44100, 16, 2, 44100);

		AiffFile.TryRead (data, out var file);

		Assert.AreEqual (1411, file!.Properties!.Bitrate);
	}

	[TestMethod]
	public void GetChunk_ExistingChunk_ReturnsChunk ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		AiffFile.TryRead (data, out var file);

		var commChunk = file!.GetChunk ("COMM");
		Assert.IsNotNull (commChunk);
		Assert.AreEqual ("COMM", commChunk.FourCC);
	}

	[TestMethod]
	public void GetChunk_NonExistingChunk_ReturnsNull ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		AiffFile.TryRead (data, out var file);

		var chunk = file!.GetChunk ("NONX");
		Assert.IsNull (chunk);
	}

	[TestMethod]
	public void TryParse_WithId3Chunk_ParsesTag ()
	{
		var data = CreateAiffWithId3Tag ("Test Title", "Test Artist");

		AiffFile.TryRead (data, out var file);

		Assert.IsNotNull (file?.Tag);
		Assert.AreEqual ("Test Title", file.Tag.Title);
		Assert.AreEqual ("Test Artist", file.Tag.Artist);
	}

	[TestMethod]
	public void AllChunks_ReturnsAllParsedChunks ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		AiffFile.TryRead (data, out var file);

		Assert.IsTrue (file!.AllChunks.Count >= 2);  // At least COMM and SSND
	}

	[TestMethod]
	public void TryParse_ChunkPaddedToEvenBoundary_HandlesCorrectly ()
	{
		// Create AIFF with odd-sized chunk to test padding
		var data = CreateAiffWithOddChunk ();

		var result = AiffFile.TryRead (data, out var file);

		Assert.IsTrue (result);
		Assert.IsTrue (file!.AllChunks.Count >= 2);
	}

	// Helper methods to create test AIFF data

	static BinaryData CreateMinimalAiffFile (
		int sampleRate,
		int bitsPerSample,
		int channels,
		uint sampleFrames,
		bool isAifc = false)
	{
		var builder = new BinaryDataBuilder ();

		// Create COMM chunk first to calculate sizes
		var commChunk = CreateCommChunk (channels, sampleFrames, bitsPerSample, sampleRate);

		// Create minimal SSND chunk (just header, no actual samples)
		var ssndChunk = CreateSsndChunk (8); // Just offset + blockSize

		// Calculate total size (everything after the size field)
		var contentSize = 4 + commChunk.Length + ssndChunk.Length; // formType + chunks

		// FORM header
		builder.Add (0x46, 0x4F, 0x52, 0x4D); // "FORM"
		builder.AddUInt32BE ((uint)contentSize);
		builder.Add (isAifc
			? [0x41, 0x49, 0x46, 0x43]  // "AIFC"
			: [0x41, 0x49, 0x46, 0x46]); // "AIFF"

		// Add chunks
		builder.Add (commChunk);
		builder.Add (ssndChunk);

		return builder.ToBinaryData ();
	}

	static byte[] CreateCommChunk (int channels, uint sampleFrames, int bitsPerSample, int sampleRate)
	{
		var builder = new BinaryDataBuilder ();

		// COMM chunk header
		builder.Add (0x43, 0x4F, 0x4D, 0x4D); // "COMM"
		builder.AddUInt32BE (18); // Size: 2 + 4 + 2 + 10 = 18 bytes

		// COMM data
		builder.AddUInt16BE ((ushort)channels);
		builder.AddUInt32BE (sampleFrames);
		builder.AddUInt16BE ((ushort)bitsPerSample);
		builder.Add (ExtendedFloat.FromDouble (sampleRate)); // 10-byte extended float

		return builder.ToArray ();
	}

	static byte[] CreateAifcCommChunk (int channels, uint sampleFrames, int bitsPerSample, int sampleRate,
		string compressionType, string compressionName)
	{
		var builder = new BinaryDataBuilder ();

		// AIFC COMM data: standard 18 bytes + 4-byte type + Pascal string
		var nameBytes = System.Text.Encoding.ASCII.GetBytes (compressionName);
		var pascalStringSize = 1 + nameBytes.Length; // Length byte + string
		if (pascalStringSize % 2 == 1) pascalStringSize++; // Pad to even

		var dataSize = 18 + 4 + pascalStringSize;

		// COMM chunk header
		builder.Add (0x43, 0x4F, 0x4D, 0x4D); // "COMM"
		builder.AddUInt32BE ((uint)dataSize);

		// COMM data - standard AIFF fields
		builder.AddUInt16BE ((ushort)channels);
		builder.AddUInt32BE (sampleFrames);
		builder.AddUInt16BE ((ushort)bitsPerSample);
		builder.Add (ExtendedFloat.FromDouble (sampleRate)); // 10-byte extended float

		// AIFC additions: compression type (4 bytes)
		var typeBytes = System.Text.Encoding.ASCII.GetBytes (compressionType.PadRight (4).Substring (0, 4));
		builder.Add (typeBytes);

		// Pascal string: length byte + string + optional pad
		builder.Add ((byte)nameBytes.Length);
		if (nameBytes.Length > 0)
			builder.Add (nameBytes);
		if ((1 + nameBytes.Length) % 2 == 1)
			builder.Add (0x00); // Pad to even

		return builder.ToArray ();
	}

	static BinaryData CreateAifcFileWithCompression (int sampleRate, int bitsPerSample, int channels, uint sampleFrames,
		string compressionType, string compressionName)
	{
		var builder = new BinaryDataBuilder ();

		// Create AIFC COMM chunk with compression
		var commChunk = CreateAifcCommChunk (channels, sampleFrames, bitsPerSample, sampleRate, compressionType, compressionName);
		var ssndChunk = CreateSsndChunk (8);

		// Calculate total size
		var contentSize = 4 + commChunk.Length + ssndChunk.Length;

		// FORM header
		builder.Add (0x46, 0x4F, 0x52, 0x4D); // "FORM"
		builder.AddUInt32BE ((uint)contentSize);
		builder.Add (0x41, 0x49, 0x46, 0x43); // "AIFC"

		// Add chunks
		builder.Add (commChunk);
		builder.Add (ssndChunk);

		return builder.ToBinaryData ();
	}

	static byte[] CreateSsndChunk (int sampleDataSize)
	{
		var builder = new BinaryDataBuilder ();

		// SSND chunk header
		builder.Add (0x53, 0x53, 0x4E, 0x44); // "SSND"
		builder.AddUInt32BE ((uint)(8 + sampleDataSize)); // Size: offset(4) + blockSize(4) + sampleData

		// SSND data
		builder.AddUInt32BE (0); // offset
		builder.AddUInt32BE (0); // blockSize
		if (sampleDataSize > 0)
			builder.Add (new byte[sampleDataSize]); // Sample data

		return builder.ToArray ();
	}

	static BinaryData CreateAiffWithId3Tag (string title, string artist)
	{
		var builder = new BinaryDataBuilder ();

		// Create ID3v2 tag
		var id3Data = CreateId3v2Tag (title, artist);

		// Create COMM and SSND chunks
		var commChunk = CreateCommChunk (2, 1000, 16, 44100);
		var ssndChunk = CreateSsndChunk (8);

		// ID3 chunk
		var id3ChunkBuilder = new BinaryDataBuilder ();
		id3ChunkBuilder.Add (0x49, 0x44, 0x33, 0x20); // "ID3 " (note space padding)
		id3ChunkBuilder.AddUInt32BE ((uint)id3Data.Length);
		id3ChunkBuilder.Add (id3Data);
		if (id3Data.Length % 2 == 1)
			id3ChunkBuilder.Add (0); // Pad to even
		var id3Chunk = id3ChunkBuilder.ToArray ();

		// Calculate total size
		var contentSize = 4 + commChunk.Length + ssndChunk.Length + id3Chunk.Length;

		// Build file
		builder.Add (0x46, 0x4F, 0x52, 0x4D); // "FORM"
		builder.AddUInt32BE ((uint)contentSize);
		builder.Add (0x41, 0x49, 0x46, 0x46); // "AIFF"
		builder.Add (commChunk);
		builder.Add (id3Chunk);
		builder.Add (ssndChunk);

		return builder.ToBinaryData ();
	}

	static byte[] CreateId3v2Tag (string title, string artist)
	{
		var builder = new BinaryDataBuilder ();

		// ID3v2 header
		builder.Add (0x49, 0x44, 0x33); // "ID3"
		builder.Add (0x04, 0x00); // Version 2.4.0
		builder.Add (0x00); // Flags

		// Build frames first to get size
		var frames = new BinaryDataBuilder ();

		// TIT2 frame (title)
		AddTextFrame (frames, "TIT2", title);

		// TPE1 frame (artist)
		AddTextFrame (frames, "TPE1", artist);

		var framesData = frames.ToArray ();

		// Syncsafe size
		builder.AddSyncSafeUInt32 ((uint)framesData.Length);
		builder.Add (framesData);

		return builder.ToArray ();
	}

	static void AddTextFrame (BinaryDataBuilder builder, string frameId, string text)
	{
		var textBytes = System.Text.Encoding.UTF8.GetBytes (text);
		var frameSize = 1 + textBytes.Length; // encoding byte + text

		builder.Add (System.Text.Encoding.ASCII.GetBytes (frameId));
		builder.AddSyncSafeUInt32 ((uint)frameSize);
		builder.Add (0x00, 0x00); // Flags
		builder.Add (0x03); // UTF-8 encoding
		builder.Add (textBytes);
	}

	static BinaryData CreateAiffWithOddChunk ()
	{
		var builder = new BinaryDataBuilder ();

		// Create chunks
		var commChunk = CreateCommChunk (2, 1000, 16, 44100);
		var ssndChunk = CreateSsndChunk (8);

		// Create odd-sized annotation chunk
		var annoChunkBuilder = new BinaryDataBuilder ();
		annoChunkBuilder.Add (0x41, 0x4E, 0x4E, 0x4F); // "ANNO"
		annoChunkBuilder.AddUInt32BE (5); // Odd size
		annoChunkBuilder.Add (0x48, 0x65, 0x6C, 0x6C, 0x6F); // "Hello" (5 bytes)
		annoChunkBuilder.Add (0x00); // Padding byte
		var annoChunk = annoChunkBuilder.ToArray ();

		var contentSize = 4 + commChunk.Length + annoChunk.Length + ssndChunk.Length;

		builder.Add (0x46, 0x4F, 0x52, 0x4D); // "FORM"
		builder.AddUInt32BE ((uint)contentSize);
		builder.Add (0x41, 0x49, 0x46, 0x46); // "AIFF"
		builder.Add (commChunk);
		builder.Add (annoChunk);
		builder.Add (ssndChunk);

		return builder.ToBinaryData ();
	}

	// Write support tests

	[TestMethod]
	public void Render_ProducesValidAiff ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		AiffFile.TryRead (data, out var file);

		var rendered = file!.Render ();

		Assert.AreEqual ((byte)'F', rendered[0]);
		Assert.AreEqual ((byte)'O', rendered[1]);
		Assert.AreEqual ((byte)'R', rendered[2]);
		Assert.AreEqual ((byte)'M', rendered[3]);
		Assert.AreEqual ((byte)'A', rendered[8]);
		Assert.AreEqual ((byte)'I', rendered[9]);
		Assert.AreEqual ((byte)'F', rendered[10]);
		Assert.AreEqual ((byte)'F', rendered[11]);
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesAudioProperties ()
	{
		var data = CreateMinimalAiffFile (48000, 24, 2, 96000);
		AiffFile.TryRead (data, out var file);

		var rendered = file!.Render ();
		AiffFile.TryRead (rendered, out var roundTripped);

		Assert.IsNotNull (roundTripped?.Properties);
		Assert.AreEqual (48000, roundTripped.Properties.SampleRate);
		Assert.AreEqual (24, roundTripped.Properties.BitsPerSample);
		Assert.AreEqual (2, roundTripped.Properties.Channels);
	}

	[TestMethod]
	public void Render_WithId3Tag_IncludesId3Chunk ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		AiffFile.TryRead (data, out var file);

		file!.Id3v2Tag = new TagLibSharp2.Id3.Id3v2.Id3v2Tag { Title = "Test Title" };

		var rendered = file.Render ();
		AiffFile.TryRead (rendered, out var roundTripped);

		Assert.IsNotNull (roundTripped?.Tag);
		Assert.AreEqual ("Test Title", roundTripped.Tag.Title);
	}

	[TestMethod]
	public void Render_WithAnnoChunk_PreservesAnnoChunk ()
	{
		var data = CreateAiffWithOddChunk ();
		AiffFile.TryRead (data, out var file);

		var rendered = file!.Render ();

		// Verify ANNO chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("ANNO"));
	}

	[TestMethod]
	public void Render_AifcFile_PreservesFormType ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000, isAifc: true);
		AiffFile.TryRead (data, out var file);

		var rendered = file!.Render ();
		AiffFile.TryRead (rendered, out var roundTripped);

		Assert.AreEqual ("AIFC", roundTripped!.FormType);
	}

	[TestMethod]
	public void SaveToFile_WithMockFileSystem_WritesData ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		AiffFile.TryRead (data, out var file);

		var mockFs = new MockFileSystem ();
		var result = file!.SaveToFile ("/test/output.aiff", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/test/output.aiff"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithMockFileSystem_WritesData ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		AiffFile.TryRead (data, out var file);

		var mockFs = new MockFileSystem ();
		var result = await file!.SaveToFileAsync ("/test/output.aiff", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/test/output.aiff"));
	}

	// ReadFromFile tests

	[TestMethod]
	public void ReadFromFile_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.aiff", data.ToArray ());

		var result = AiffFile.ReadFromFile ("/test.aiff", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsTrue (result.File.IsValid);
		Assert.AreEqual ("AIFF", result.File.FormType);
	}

	[TestMethod]
	public void ReadFromFile_FileNotFound_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = AiffFile.ReadFromFile ("/nonexistent.aiff", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "not found");
	}

	[TestMethod]
	public void ReadFromFile_AccessDenied_ReturnsFailure ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.aiff", data.ToArray ());
		mockFs.MarkInaccessible ("/test.aiff");

		var result = AiffFile.ReadFromFile ("/test.aiff", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "Access denied");
	}

	[TestMethod]
	public void ReadFromFile_InvalidData_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/invalid.aiff", [0x00, 0x01, 0x02, 0x03]); // Invalid AIFF data

		var result = AiffFile.ReadFromFile ("/invalid.aiff", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.aiff", data.ToArray ());

		var result = await AiffFile.ReadFromFileAsync ("/test.aiff", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsTrue (result.File.IsValid);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_FileNotFound_ReturnsFailure ()
	{
		var mockFs = new MockFileSystem ();

		var result = await AiffFile.ReadFromFileAsync ("/nonexistent.aiff", mockFs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Cancellation_ReturnsFailure ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.aiff", data.ToArray ());
		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await AiffFile.ReadFromFileAsync ("/test.aiff", mockFs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public void ReadFromFile_WithId3Tag_ParsesTag ()
	{
		var data = CreateAiffWithId3Tag ("Test Title", "Test Artist");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/tagged.aiff", data.ToArray ());

		var result = AiffFile.ReadFromFile ("/tagged.aiff", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File?.Tag);
		Assert.AreEqual ("Test Title", result.File.Tag.Title);
		Assert.AreEqual ("Test Artist", result.File.Tag.Artist);
	}
}
