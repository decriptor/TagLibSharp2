// TDD Tests for WavPack (.wv) file support
// Written first to define expected behavior

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.WavPack;

namespace TagLibSharp2.Tests.WavPack;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("WavPack")]
public class WavPackFileTests
{
	// WavPack sample rate table
	private static readonly int[] SampleRateTable = [
		6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000,
		32000, 44100, 48000, 64000, 88200, 96000, 192000
	];

	#region Magic and File Recognition

	[TestMethod]
	public void Parse_ValidMagic_ReturnsSuccess ()
	{
		var data = CreateMinimalWavPackFile ();
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void Parse_InvalidMagic_ReturnsError ()
	{
		var data = new byte[100];
		data[0] = (byte)'X';
		data[1] = (byte)'X';
		data[2] = (byte)'X';
		data[3] = (byte)'X';

		var result = WavPackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("magic") || result.Error.Contains ("wvpk"));
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsError ()
	{
		var data = new byte[3]; // Too short for magic
		var result = WavPackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
	}

	#endregion

	#region Header Parsing

	[TestMethod]
	public void Parse_ExtractsVersion ()
	{
		var data = CreateMinimalWavPackFile (version: 0x410);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (0x410, result.File!.Version);
	}

	[TestMethod]
	public void Parse_ExtractsBlockSize ()
	{
		var data = CreateMinimalWavPackFile (blockSize: 1000);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1000u, result.File!.BlockSize);
	}

	#endregion

	#region Audio Properties from Flags

	[TestMethod]
	public void Parse_ExtractsSampleRate_44100 ()
	{
		// 44100 = index 9 in sample rate table
		var data = CreateMinimalWavPackFile (sampleRateIndex: 9);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsSampleRate_48000 ()
	{
		// 48000 = index 10 in sample rate table
		var data = CreateMinimalWavPackFile (sampleRateIndex: 10);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (48000, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsSampleRate_96000 ()
	{
		// 96000 = index 13 in sample rate table
		var data = CreateMinimalWavPackFile (sampleRateIndex: 13);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (96000, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsBitsPerSample_16bit ()
	{
		// bytesPerSample - 1 in flags bits 0-1
		// 16-bit = 2 bytes = flags bits 0-1 = 1
		var data = CreateMinimalWavPackFile (bytesPerSample: 2);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (16, result.File!.BitsPerSample);
	}

	[TestMethod]
	public void Parse_ExtractsBitsPerSample_24bit ()
	{
		// 24-bit = 3 bytes = flags bits 0-1 = 2
		var data = CreateMinimalWavPackFile (bytesPerSample: 3);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (24, result.File!.BitsPerSample);
	}

	[TestMethod]
	public void Parse_ExtractsChannels_Stereo ()
	{
		// Mono flag = bit 2. If not set, stereo
		var data = CreateMinimalWavPackFile (isMono: false);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_ExtractsChannels_Mono ()
	{
		// Mono flag = bit 2
		var data = CreateMinimalWavPackFile (isMono: true);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_ExtractsTotalSamples ()
	{
		var data = CreateMinimalWavPackFile (totalSamples: 441000);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (441000u, result.File!.TotalSamples);
	}

	#endregion

	#region AudioProperties

	[TestMethod]
	public void Parse_CalculatesDuration ()
	{
		// 441000 samples at 44100 Hz = 10 seconds
		var data = CreateMinimalWavPackFile (sampleRateIndex: 9, totalSamples: 441000);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Properties);
		Assert.IsTrue (result.File.Properties!.Duration.TotalSeconds > 9.9 && result.File.Properties.Duration.TotalSeconds < 10.1);
	}

	[TestMethod]
	public void Properties_MatchesFileProperties ()
	{
		var data = CreateMinimalWavPackFile (sampleRateIndex: 10, bytesPerSample: 3, isMono: false);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		var props = file.Properties!;

		Assert.AreEqual (file.SampleRate, props.SampleRate);
		Assert.AreEqual (file.Channels, props.Channels);
		Assert.AreEqual (file.BitsPerSample, props.BitsPerSample);
	}

	#endregion

	#region APE Tag Support

	[TestMethod]
	public void Parse_NoTag_ApeTagIsNull ()
	{
		var data = CreateMinimalWavPackFile ();
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.ApeTag);
	}

	[TestMethod]
	public void Parse_WithApeTag_ReadsTitle ()
	{
		var data = CreateMinimalWavPackFileWithTag (title: "Test Song");
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);
		Assert.AreEqual ("Test Song", result.File.ApeTag!.Title);
	}

	[TestMethod]
	public void Parse_WithApeTag_ReadsArtist ()
	{
		var data = CreateMinimalWavPackFileWithTag (artist: "Test Artist");
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);
		Assert.AreEqual ("Test Artist", result.File.ApeTag!.Artist);
	}

	[TestMethod]
	public void EnsureApeTag_CreatesTag ()
	{
		var data = CreateMinimalWavPackFile ();
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);

		var file = result.File!;
		Assert.IsNull (file.ApeTag);

		var tag = file.EnsureApeTag ();
		Assert.IsNotNull (tag);
		Assert.AreSame (tag, file.ApeTag);
	}

	[TestMethod]
	public void RemoveApeTag_RemovesExistingTag ()
	{
		var data = CreateMinimalWavPackFileWithTag (title: "Test");
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);

		var file = result.File!;
		Assert.IsNotNull (file.ApeTag);

		file.RemoveApeTag ();
		Assert.IsNull (file.ApeTag);
	}

	#endregion

	#region File I/O

	[TestMethod]
	public void ReadFromFile_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalWavPackFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wv", data);

		var result = WavPackFile.ReadFromFile ("/test.wv", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalWavPackFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wv", data);

		var result = await WavPackFile.ReadFromFileAsync ("/test.wv", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void SaveToFile_PreservesAudioData ()
	{
		var data = CreateMinimalWavPackFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wv", data);

		var readResult = WavPackFile.ReadFromFile ("/test.wv", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "New Title";

		var saveResult = file.SaveToFile ("/output.wv", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Re-read and verify
		var verifyResult = WavPackFile.ReadFromFile ("/output.wv", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("New Title", verifyResult.File!.ApeTag!.Title);
	}

	[TestMethod]
	public void SaveToFile_WithoutPath_UsesSourcePath ()
	{
		var data = CreateMinimalWavPackFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.wv", data);

		var readResult = WavPackFile.ReadFromFile ("/music/song.wv", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Updated";

		var saveResult = file.SaveToFile (mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Verify saved to original path
		var verifyResult = WavPackFile.ReadFromFile ("/music/song.wv", mockFs);
		Assert.AreEqual ("Updated", verifyResult.File!.ApeTag!.Title);
	}

	#endregion

	#region Render

	[TestMethod]
	public void Render_WithNewTag_AppendsTagToAudioData ()
	{
		var data = CreateMinimalWavPackFile ();
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.EnsureApeTag ().Title = "Test";

		var rendered = file.Render (data);

		// Rendered data should be larger than original (tag added)
		Assert.IsTrue (rendered.Length > data.Length);

		// Should still be valid WavPack file
		var reparsed = WavPackFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Test", reparsed.File!.ApeTag!.Title);
	}

	[TestMethod]
	public void Render_RemoveTag_StripsTagFromFile ()
	{
		var data = CreateMinimalWavPackFileWithTag (title: "Original");
		var result = WavPackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.RemoveApeTag ();

		var rendered = file.Render (data);

		// Re-parse should have no tag
		var reparsed = WavPackFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.IsNull (reparsed.File!.ApeTag);
	}

	#endregion

	#region Edge Cases

	[TestMethod]
	public void Parse_AllSampleRates ()
	{
		// Test all 15 standard sample rates
		for (int i = 0; i < SampleRateTable.Length; i++) {
			var data = CreateMinimalWavPackFile (sampleRateIndex: i);
			var result = WavPackFile.Parse (data);

			Assert.IsTrue (result.IsSuccess, $"Failed for sample rate index {i}");
			Assert.AreEqual (SampleRateTable[i], result.File!.SampleRate, $"Wrong sample rate for index {i}");
		}
	}

	[TestMethod]
	public void Parse_8bit ()
	{
		// 8-bit = 1 byte = flags bits 0-1 = 0
		var data = CreateMinimalWavPackFile (bytesPerSample: 1);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (8, result.File!.BitsPerSample);
	}

	[TestMethod]
	public void Parse_32bit ()
	{
		// 32-bit = 4 bytes = flags bits 0-1 = 3
		var data = CreateMinimalWavPackFile (bytesPerSample: 4);
		var result = WavPackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (32, result.File!.BitsPerSample);
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Creates a minimal WavPack file with block header
	/// </summary>
	private static byte[] CreateMinimalWavPackFile (
		int version = 0x410,
		uint blockSize = 100,
		int sampleRateIndex = 9, // 44100
		int bytesPerSample = 2, // 16-bit
		bool isMono = false,
		uint totalSamples = 44100)
	{
		using var ms = new MemoryStream ();

		// WavPack block header (32 bytes)
		// [0-3] Magic "wvpk"
		ms.Write ("wvpk"u8);

		// [4-7] Block size (excluding first 8 bytes)
		WriteUInt32LE (ms, blockSize);

		// [8-9] Version
		WriteUInt16LE (ms, (ushort)version);

		// [10] Track number
		ms.WriteByte (0);

		// [11] Index number
		ms.WriteByte (0);

		// [12-15] Total samples (for first block in file, -1 for unknown)
		WriteUInt32LE (ms, totalSamples);

		// [16-19] Block index
		WriteUInt32LE (ms, 0);

		// [20-23] Block samples
		WriteUInt32LE (ms, totalSamples);

		// [24-27] Flags
		// Bits 0-1: bytes per sample - 1
		// Bit 2: mono
		// Bits 23-26: sample rate index (0-14), 15 = custom
		uint flags = 0;
		flags |= (uint)(bytesPerSample - 1) & 0x3; // bits 0-1
		if (isMono) flags |= 0x4; // bit 2
		flags |= (uint)(sampleRateIndex & 0xF) << 23; // bits 23-26
		WriteUInt32LE (ms, flags);

		// [28-31] CRC
		WriteUInt32LE (ms, 0);

		// Minimal audio data placeholder (block size - 24 = compressed data)
		var audioDataSize = Math.Max (0, (int)blockSize - 24);
		ms.Write (new byte[audioDataSize]);

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates WavPack file with APEv2 tag
	/// </summary>
	private static byte[] CreateMinimalWavPackFileWithTag (
		string? title = null,
		string? artist = null)
	{
		var audioData = CreateMinimalWavPackFile ();

		// Build APEv2 tag items
		var items = new List<byte[]> ();
		if (title is not null)
			items.Add (CreateApeTagItem ("Title", title));
		if (artist is not null)
			items.Add (CreateApeTagItem ("Artist", artist));

		var itemsData = items.SelectMany (x => x).ToArray ();

		var ms = new MemoryStream ();
		ms.Write (audioData);

		// APEv2 header (32 bytes)
		ms.Write ("APETAGEX"u8);
		WriteUInt32LE (ms, 2000); // Version
		WriteUInt32LE (ms, (uint)(itemsData.Length + 32)); // Size (items + footer)
		WriteUInt32LE (ms, (uint)items.Count);
		WriteUInt32LE (ms, 0x80000000); // Flags: header present
		ms.Write (new byte[8]); // Reserved

		// Items
		ms.Write (itemsData);

		// APEv2 footer (32 bytes)
		ms.Write ("APETAGEX"u8);
		WriteUInt32LE (ms, 2000);
		WriteUInt32LE (ms, (uint)(itemsData.Length + 32));
		WriteUInt32LE (ms, (uint)items.Count);
		WriteUInt32LE (ms, 0); // Flags: footer, no header flag
		ms.Write (new byte[8]);

		return ms.ToArray ();
	}

	private static byte[] CreateApeTagItem (string key, string value)
	{
		using var ms = new MemoryStream ();
		var valueBytes = System.Text.Encoding.UTF8.GetBytes (value);
		var keyBytes = System.Text.Encoding.UTF8.GetBytes (key);

		WriteUInt32LE (ms, (uint)valueBytes.Length);
		WriteUInt32LE (ms, 0); // Flags (UTF-8 text)
		ms.Write (keyBytes);
		ms.WriteByte (0); // Null terminator for key
		ms.Write (valueBytes);

		return ms.ToArray ();
	}

	private static void WriteUInt32LE (Stream s, uint v)
	{
		s.WriteByte ((byte)v);
		s.WriteByte ((byte)(v >> 8));
		s.WriteByte ((byte)(v >> 16));
		s.WriteByte ((byte)(v >> 24));
	}

	private static void WriteUInt16LE (Stream s, ushort v)
	{
		s.WriteByte ((byte)v);
		s.WriteByte ((byte)(v >> 8));
	}

	#endregion
}
