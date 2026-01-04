// TDD Tests for DSF/DFF API Consistency
// These tests define the expected consistent API surface for both DSD formats

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Tests that verify DSF and DFF have consistent API surfaces.
/// Written first (TDD) to define expected behavior.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Consistency")]
public class DsdApiConsistencyTests
{
	// ===== Property Type Consistency Tests =====

	[TestMethod]
	public void DsfFile_SampleRate_IsInt ()
	{
		// DSF should use int (not uint) to match AudioProperties and DFF
		var data = CreateMinimalDsfFile ();
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		// SampleRate should be int type
		int sampleRate = result.File!.SampleRate;
		Assert.AreEqual (2822400, sampleRate);
	}

	[TestMethod]
	public void DffFile_SampleRate_IsInt ()
	{
		var data = CreateMinimalDffFile ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		int sampleRate = result.File!.SampleRate;
		Assert.AreEqual (2822400, sampleRate);
	}

	[TestMethod]
	public void DsfFile_Channels_PropertyExists ()
	{
		// DSF should have Channels property (not just ChannelCount)
		var data = CreateMinimalDsfFile ();
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		// Channels should exist and be int type
		int channels = result.File!.Channels;
		Assert.AreEqual (2, channels);
	}

	[TestMethod]
	public void DffFile_Channels_PropertyExists ()
	{
		var data = CreateMinimalDffFile ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		int channels = result.File!.Channels;
		Assert.AreEqual (2, channels);
	}

	[TestMethod]
	public void DsfFile_BitsPerSample_IsInt ()
	{
		var data = CreateMinimalDsfFile ();
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		int bitsPerSample = result.File!.BitsPerSample;
		Assert.AreEqual (1, bitsPerSample);
	}

	[TestMethod]
	public void DffFile_BitsPerSample_PropertyExists ()
	{
		// DFF should have BitsPerSample on main class (not just Properties)
		var data = CreateMinimalDffFile ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		int bitsPerSample = result.File!.BitsPerSample;
		Assert.AreEqual (1, bitsPerSample);
	}

	// ===== SampleCount Consistency Tests =====

	[TestMethod]
	public void DsfFile_SampleCount_IsPublic ()
	{
		var data = CreateMinimalDsfFile ();
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		ulong sampleCount = result.File!.SampleCount;
		Assert.IsTrue (sampleCount > 0);
	}

	[TestMethod]
	public void DffFile_SampleCount_IsPublic ()
	{
		// DFF should expose SampleCount publicly (like DSF)
		var data = CreateMinimalDffFile ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		ulong sampleCount = result.File!.SampleCount;
		Assert.IsTrue (sampleCount > 0);
	}

	// ===== SaveToFile API Consistency Tests =====

	[TestMethod]
	public void DffFile_SaveToFile_WithPathAndFileSystem_Exists ()
	{
		// DFF should have SaveToFile(string path, IFileSystem?) like DSF
		var data = CreateMinimalDffFile ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/source.dff", data);

		// This overload should exist and work
		var file = result.File!;
		var saveResult = file.SaveToFile ("/output.dff", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);
	}

	[TestMethod]
	public void DffFile_SaveToFile_NoArgs_UsesSourcePath ()
	{
		// DFF should have SaveToFile(IFileSystem?) like DSF
		var data = CreateMinimalDffFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.dff", data);

		var readResult = DffFile.ReadFromFile ("/music/song.dff", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Test";

		// Should save back to source path
		var saveResult = file.SaveToFile (mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Verify it was saved
		var savedData = mockFs.ReadAllBytes ("/music/song.dff");
		var reparsed = DffFile.Read (savedData);
		Assert.AreEqual ("Test", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void DffFile_SaveToFile_RespectsSourceFileSystem ()
	{
		// When reading with a custom IFileSystem, saving should use it too
		var data = CreateMinimalDffFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.dff", data);

		var readResult = DffFile.ReadFromFile ("/music/song.dff", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Updated";

		// Save without specifying fileSystem - should use the one from ReadFromFile
		var saveResult = file.SaveToFile ();
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Verify it was saved to the mock filesystem
		Assert.IsTrue (mockFs.FileExists ("/music/song.dff"));
		var savedData = mockFs.ReadAllBytes ("/music/song.dff");
		var reparsed = DffFile.Read (savedData);
		Assert.AreEqual ("Updated", reparsed.File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task DffFile_SaveToFileAsync_WithPath_Exists ()
	{
		// DFF should have SaveToFileAsync(string path, IFileSystem?, CancellationToken) like DSF
		var data = CreateMinimalDffFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/source.dff", data);

		var readResult = await DffFile.ReadFromFileAsync ("/source.dff", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureId3v2Tag ().Title = "Async Test";

		// This overload should exist
		var saveResult = await file.SaveToFileAsync ("/output.dff", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		var savedData = mockFs.ReadAllBytes ("/output.dff");
		var reparsed = DffFile.Read (savedData);
		Assert.AreEqual ("Async Test", reparsed.File!.Id3v2Tag!.Title);
	}

	// ===== AudioProperties Consistency Tests =====

	[TestMethod]
	public void DsfAudioProperties_Channels_MatchesFileChannels ()
	{
		var data = CreateMinimalDsfFile ();
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		// Properties.Channels should match File.Channels
		Assert.AreEqual (result.File!.Channels, result.File.Properties!.Channels);
	}

	[TestMethod]
	public void DffAudioProperties_Channels_MatchesFileChannels ()
	{
		var data = CreateMinimalDffFile ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		// Properties.Channels should match File.Channels
		Assert.AreEqual (result.File!.Channels, result.File.Properties!.Channels);
	}

	// ===== Helper Methods =====

	private static byte[] CreateMinimalDsfFile ()
	{
		// Create minimal valid DSF file
		var dsd = CreateDsdChunk (28, 0, 0);
		var fmt = CreateFmtChunk (1, 0, 2, 2, 2822400, 1, 2822400, 4096);
		var dataHeader = CreateDataChunkHeader (100);
		var audioData = new byte[88];

		var totalSize = dsd.Length + fmt.Length + dataHeader.Length + audioData.Length;

		// Update file size in DSD chunk
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (dsd.AsSpan (12), (ulong)totalSize);

		var result = new byte[totalSize];
		var offset = 0;
		dsd.CopyTo (result, offset); offset += dsd.Length;
		fmt.CopyTo (result, offset); offset += fmt.Length;
		dataHeader.CopyTo (result, offset); offset += dataHeader.Length;
		audioData.CopyTo (result, offset);

		return result;
	}

	private static byte[] CreateDsdChunk (ulong chunkSize, ulong fileSize, ulong metadataOffset)
	{
		var data = new byte[28];
		data[0] = (byte)'D'; data[1] = (byte)'S'; data[2] = (byte)'D'; data[3] = (byte)' ';
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), chunkSize);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (12), fileSize);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (20), metadataOffset);
		return data;
	}

	private static byte[] CreateFmtChunk (
		uint formatVersion, uint formatId, uint channelType, uint channelCount,
		uint sampleRate, uint bitsPerSample, ulong sampleCount, uint blockSizePerChannel)
	{
		var data = new byte[52];
		data[0] = (byte)'f'; data[1] = (byte)'m'; data[2] = (byte)'t'; data[3] = (byte)' ';
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), 52UL);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (12), formatVersion);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (16), formatId);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (20), channelType);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (24), channelCount);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (28), sampleRate);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (32), bitsPerSample);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (36), sampleCount);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (44), blockSizePerChannel);
		return data;
	}

	private static byte[] CreateDataChunkHeader (ulong chunkSize)
	{
		var data = new byte[12];
		data[0] = (byte)'d'; data[1] = (byte)'a'; data[2] = (byte)'t'; data[3] = (byte)'a';
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (4), chunkSize);
		return data;
	}

	private static byte[] CreateMinimalDffFile ()
	{
		using var ms = new MemoryStream ();

		// FRM8 header
		ms.Write ("FRM8"u8);
		var sizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("DSD "u8);

		// FVER chunk
		ms.Write ("FVER"u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 0x01050000);

		// PROP chunk
		var propStart = ms.Position;
		ms.Write ("PROP"u8);
		var propSizePosition = ms.Position;
		WriteUInt64BE (ms, 0);
		ms.Write ("SND "u8);

		// FS sub-chunk
		ms.Write ("FS  "u8);
		WriteUInt64BE (ms, 4);
		WriteUInt32BE (ms, 2822400);

		// CHNL sub-chunk
		ms.Write ("CHNL"u8);
		WriteUInt64BE (ms, 10);
		WriteUInt16BE (ms, 2);
		ms.Write ("SLFT"u8);
		ms.Write ("SRGT"u8);

		// CMPR sub-chunk
		ms.Write ("CMPR"u8);
		WriteUInt64BE (ms, 4 + 1 + 14);
		ms.Write ("DSD "u8);
		ms.WriteByte (14);
		ms.Write ("not compressed"u8);

		var propEnd = ms.Position;
		var propSize = propEnd - propStart - 12;
		ms.Position = propSizePosition;
		WriteUInt64BE (ms, (ulong)propSize);
		ms.Position = propEnd;

		if (propSize % 2 != 0) ms.WriteByte (0);

		// DSD chunk with sample data
		ms.Write ("DSD "u8);
		var audioDataSize = 16384UL * 2 / 8; // sampleCount * channels / 8
		WriteUInt64BE (ms, audioDataSize);
		ms.Write (new byte[audioDataSize]);

		// Update FRM8 size
		var totalSize = ms.Position;
		ms.Position = sizePosition;
		WriteUInt64BE (ms, (ulong)(totalSize - 12));

		return ms.ToArray ();
	}

	private static void WriteUInt64BE (Stream s, ulong v)
	{
		for (int i = 7; i >= 0; i--) s.WriteByte ((byte)(v >> (i * 8)));
	}

	private static void WriteUInt32BE (Stream s, uint v)
	{
		s.WriteByte ((byte)(v >> 24));
		s.WriteByte ((byte)(v >> 16));
		s.WriteByte ((byte)(v >> 8));
		s.WriteByte ((byte)v);
	}

	private static void WriteUInt16BE (Stream s, ushort v)
	{
		s.WriteByte ((byte)(v >> 8));
		s.WriteByte ((byte)v);
	}
}
