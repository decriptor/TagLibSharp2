// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;

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

		var result = AiffFile.TryParse (data, out var file);

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

		var result = AiffFile.TryParse (new BinaryData (data), out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_TooShort_ReturnsFalse ()
	{
		byte[] data = [0x46, 0x4F, 0x52, 0x4D]; // Just "FORM"

		var result = AiffFile.TryParse (new BinaryData (data), out _);

		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_AifcFormType_Succeeds ()
	{
		// AIFC is compressed AIFF
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000, isAifc: true);

		var result = AiffFile.TryParse (data, out var file);

		Assert.IsTrue (result);
		Assert.AreEqual ("AIFC", file!.FormType);
	}

	[TestMethod]
	public void AudioProperties_CommonChunk_ParsedCorrectly ()
	{
		var data = CreateMinimalAiffFile (
			sampleRate: 48000,
			bitsPerSample: 24,
			channels: 2,
			sampleFrames: 96000);

		AiffFile.TryParse (data, out var file);

		Assert.IsNotNull (file?.AudioProperties);
		Assert.AreEqual (48000, file.AudioProperties.SampleRate);
		Assert.AreEqual (24, file.AudioProperties.BitsPerSample);
		Assert.AreEqual (2, file.AudioProperties.Channels);
		Assert.AreEqual (96000u, file.AudioProperties.SampleFrames);
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

		AiffFile.TryParse (data, out var file);

		Assert.AreEqual (TimeSpan.FromSeconds (2), file!.AudioProperties!.Duration);
	}

	[TestMethod]
	public void AudioProperties_Bitrate_CalculatedCorrectly ()
	{
		// 44100 Hz, 16-bit, stereo = 1411.2 kbps
		var data = CreateMinimalAiffFile (44100, 16, 2, 44100);

		AiffFile.TryParse (data, out var file);

		Assert.AreEqual (1411, file!.AudioProperties!.Bitrate);
	}

	[TestMethod]
	public void GetChunk_ExistingChunk_ReturnsChunk ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		AiffFile.TryParse (data, out var file);

		var commChunk = file!.GetChunk ("COMM");
		Assert.IsNotNull (commChunk);
		Assert.AreEqual ("COMM", commChunk.FourCC);
	}

	[TestMethod]
	public void GetChunk_NonExistingChunk_ReturnsNull ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		AiffFile.TryParse (data, out var file);

		var chunk = file!.GetChunk ("NONX");
		Assert.IsNull (chunk);
	}

	[TestMethod]
	public void TryParse_WithId3Chunk_ParsesTag ()
	{
		var data = CreateAiffWithId3Tag ("Test Title", "Test Artist");

		AiffFile.TryParse (data, out var file);

		Assert.IsNotNull (file?.Tag);
		Assert.AreEqual ("Test Title", file.Tag.Title);
		Assert.AreEqual ("Test Artist", file.Tag.Artist);
	}

	[TestMethod]
	public void AllChunks_ReturnsAllParsedChunks ()
	{
		var data = CreateMinimalAiffFile (44100, 16, 2, 1000);

		AiffFile.TryParse (data, out var file);

		Assert.IsTrue (file!.AllChunks.Count >= 2);  // At least COMM and SSND
	}

	[TestMethod]
	public void TryParse_ChunkPaddedToEvenBoundary_HandlesCorrectly ()
	{
		// Create AIFF with odd-sized chunk to test padding
		var data = CreateAiffWithOddChunk ();

		var result = AiffFile.TryParse (data, out var file);

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
}
