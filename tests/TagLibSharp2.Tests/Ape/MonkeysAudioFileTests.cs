// TDD Tests for Monkey's Audio (.ape) file support
// Written first to define expected behavior

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Ape;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MonkeysAudio")]
public class MonkeysAudioFileTests
{
	#region Magic and File Recognition

	[TestMethod]
	public void Parse_ValidMagic_ReturnsSuccess ()
	{
		var data = CreateMinimalApeFile (version: 3990);
		var result = MonkeysAudioFile.Parse (data);
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

		var result = MonkeysAudioFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("magic") || result.Error.Contains ("MAC"));
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsError ()
	{
		var data = new byte[3]; // Too short for magic
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
	}

	#endregion

	#region Version Handling

	[TestMethod]
	public void Parse_Version3990_ParsesCorrectly ()
	{
		// Version 3.99 = 3990 (new format with descriptor)
		var data = CreateMinimalApeFile (version: 3990);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (3990, result.File!.Version);
	}

	[TestMethod]
	public void Parse_Version3970_ParsesCorrectly ()
	{
		// Version 3.97 = 3970 (old format, last version without descriptor)
		var data = CreateMinimalApeFileOldFormat (version: 3970);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (3970, result.File!.Version);
	}

	[TestMethod]
	public void Parse_Version3980_UsesDescriptorFormat ()
	{
		// Version 3.98+ uses APE_DESCRIPTOR
		var data = CreateMinimalApeFile (version: 3980);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (3980, result.File!.Version);
	}

	#endregion

	#region Audio Properties

	[TestMethod]
	public void Parse_ExtractsSampleRate ()
	{
		var data = CreateMinimalApeFile (version: 3990, sampleRate: 44100);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsChannels ()
	{
		var data = CreateMinimalApeFile (version: 3990, channels: 2);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_ExtractsBitsPerSample ()
	{
		var data = CreateMinimalApeFile (version: 3990, bitsPerSample: 16);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (16, result.File!.BitsPerSample);
	}

	[TestMethod]
	public void Parse_ExtractsTotalFrames ()
	{
		var data = CreateMinimalApeFile (version: 3990, totalFrames: 1000);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1000u, result.File!.TotalFrames);
	}

	[TestMethod]
	public void Parse_CalculatesDuration ()
	{
		// With sample rate 44100 and enough samples for ~10 seconds
		var data = CreateMinimalApeFile (version: 3990, sampleRate: 44100, totalFrames: 100, blocksPerFrame: 4410);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Properties);
		// Duration should be approximately 10 seconds (100 frames * 4410 blocks / 44100 sample rate)
		Assert.IsTrue (result.File.Properties!.Duration.TotalSeconds > 9 && result.File.Properties.Duration.TotalSeconds < 11);
	}

	[TestMethod]
	public void Parse_ExtractsCompressionLevel ()
	{
		var data = CreateMinimalApeFile (version: 3990, compressionLevel: 2000); // Normal compression
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2000, result.File!.CompressionLevel);
	}

	#endregion

	#region AudioProperties Consistency

	[TestMethod]
	public void Properties_MatchesFileProperties ()
	{
		var data = CreateMinimalApeFile (version: 3990, sampleRate: 48000, channels: 2, bitsPerSample: 24);
		var result = MonkeysAudioFile.Parse (data);

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
		var data = CreateMinimalApeFile (version: 3990);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.ApeTag);
	}

	[TestMethod]
	public void Parse_WithApeTag_ReadsTitle ()
	{
		var data = CreateMinimalApeFileWithTag (version: 3990, title: "Test Song");
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);
		Assert.AreEqual ("Test Song", result.File.ApeTag!.Title);
	}

	[TestMethod]
	public void Parse_WithApeTag_ReadsArtist ()
	{
		var data = CreateMinimalApeFileWithTag (version: 3990, artist: "Test Artist");
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);
		Assert.AreEqual ("Test Artist", result.File.ApeTag!.Artist);
	}

	[TestMethod]
	public void EnsureApeTag_CreatesTag ()
	{
		var data = CreateMinimalApeFile (version: 3990);
		var result = MonkeysAudioFile.Parse (data);
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
		var data = CreateMinimalApeFileWithTag (version: 3990, title: "Test");
		var result = MonkeysAudioFile.Parse (data);
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
		var data = CreateMinimalApeFile (version: 3990);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ape", data);

		var result = MonkeysAudioFile.ReadFromFile ("/test.ape", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalApeFile (version: 3990);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ape", data);

		var result = await MonkeysAudioFile.ReadFromFileAsync ("/test.ape", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void SaveToFile_PreservesAudioData ()
	{
		var data = CreateMinimalApeFile (version: 3990);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ape", data);

		var readResult = MonkeysAudioFile.ReadFromFile ("/test.ape", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "New Title";

		var saveResult = file.SaveToFile ("/output.ape", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Re-read and verify
		var verifyResult = MonkeysAudioFile.ReadFromFile ("/output.ape", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("New Title", verifyResult.File!.ApeTag!.Title);
	}

	[TestMethod]
	public void SaveToFile_WithoutPath_UsesSourcePath ()
	{
		var data = CreateMinimalApeFile (version: 3990);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.ape", data);

		var readResult = MonkeysAudioFile.ReadFromFile ("/music/song.ape", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "Updated";

		var saveResult = file.SaveToFile (mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Verify saved to original path
		var verifyResult = MonkeysAudioFile.ReadFromFile ("/music/song.ape", mockFs);
		Assert.AreEqual ("Updated", verifyResult.File!.ApeTag!.Title);
	}

	#endregion

	#region Render

	[TestMethod]
	public void Render_WithNewTag_AppendsTagToAudioData ()
	{
		var data = CreateMinimalApeFile (version: 3990);
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.EnsureApeTag ().Title = "Test";

		var rendered = file.Render (data);

		// Rendered data should be larger than original (tag added)
		Assert.IsTrue (rendered.Length > data.Length);

		// Should still be valid APE file
		var reparsed = MonkeysAudioFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Test", reparsed.File!.ApeTag!.Title);
	}

	[TestMethod]
	public void Render_RemoveTag_StripsTagFromFile ()
	{
		var data = CreateMinimalApeFileWithTag (version: 3990, title: "Original");
		var result = MonkeysAudioFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.RemoveApeTag ();

		var rendered = file.Render (data);

		// Re-parse should have no tag
		var reparsed = MonkeysAudioFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.IsNull (reparsed.File!.ApeTag);
	}

	#endregion

	#region Old Format Tests (≤3.97)

	[TestMethod]
	public void Parse_OldFormat_ExtractsSampleRate ()
	{
		var data = CreateMinimalApeFileOldFormat (version: 3970, sampleRate: 44100);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_OldFormat_ExtractsChannels ()
	{
		var data = CreateMinimalApeFileOldFormat (version: 3970, channels: 2);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_OldFormat_ExtractsBitsPerSample ()
	{
		var data = CreateMinimalApeFileOldFormat (version: 3970, bitsPerSample: 16);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (16, result.File!.BitsPerSample);
	}

	#endregion

	#region Edge Cases

	[TestMethod]
	public void Parse_HighResAudio_96kHz24bit ()
	{
		var data = CreateMinimalApeFile (version: 3990, sampleRate: 96000, bitsPerSample: 24);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (96000, result.File!.SampleRate);
		Assert.AreEqual (24, result.File.BitsPerSample);
	}

	[TestMethod]
	public void Parse_MonoFile ()
	{
		var data = CreateMinimalApeFile (version: 3990, channels: 1);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_MultiChannel_5Point1 ()
	{
		var data = CreateMinimalApeFile (version: 3990, channels: 6);
		var result = MonkeysAudioFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (6, result.File!.Channels);
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Creates a minimal APE file with new format (≥3.98) descriptor
	/// </summary>
	private static byte[] CreateMinimalApeFile (
		int version = 3990,
		int sampleRate = 44100,
		int channels = 2,
		int bitsPerSample = 16,
		uint totalFrames = 100,
		int compressionLevel = 2000,
		uint blocksPerFrame = 73728)
	{
		using var ms = new MemoryStream ();

		// Magic "MAC "
		ms.Write ("MAC "u8);

		// Version (uint16 LE)
		WriteUInt16LE (ms, (ushort)version);

		// APE_DESCRIPTOR (52 bytes total including version)
		// Padding length (2 bytes) - already at offset 6
		WriteUInt16LE (ms, 0); // padding

		// Descriptor bytes (uint32 LE) - offset to APE_HEADER
		WriteUInt32LE (ms, 52); // descriptor size

		// Header bytes (uint32 LE)
		WriteUInt32LE (ms, 24); // header size

		// Seek table bytes (uint32 LE)
		WriteUInt32LE (ms, 0);

		// Wave header bytes (uint32 LE)
		WriteUInt32LE (ms, 0);

		// Audio data bytes (uint32 LE)
		WriteUInt32LE (ms, 1000);

		// Audio data bytes high (uint32 LE)
		WriteUInt32LE (ms, 0);

		// Terminating data bytes (uint32 LE)
		WriteUInt32LE (ms, 0);

		// MD5 hash (16 bytes)
		ms.Write (new byte[16]);

		// APE_HEADER (24 bytes)
		// Compression type (uint16 LE)
		WriteUInt16LE (ms, (ushort)compressionLevel);

		// Format flags (uint16 LE)
		WriteUInt16LE (ms, 0);

		// Blocks per frame (uint32 LE)
		WriteUInt32LE (ms, blocksPerFrame);

		// Final frame blocks (uint32 LE)
		WriteUInt32LE (ms, 4410);

		// Total frames (uint32 LE)
		WriteUInt32LE (ms, totalFrames);

		// Bits per sample (uint16 LE)
		WriteUInt16LE (ms, (ushort)bitsPerSample);

		// Channels (uint16 LE)
		WriteUInt16LE (ms, (ushort)channels);

		// Sample rate (uint32 LE)
		WriteUInt32LE (ms, (uint)sampleRate);

		// Minimal audio data placeholder
		ms.Write (new byte[100]);

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a minimal APE file with old format (≤3.97)
	/// </summary>
	private static byte[] CreateMinimalApeFileOldFormat (
		int version = 3970,
		int sampleRate = 44100,
		int channels = 2,
		int bitsPerSample = 16)
	{
		using var ms = new MemoryStream ();

		// Magic "MAC "
		ms.Write ("MAC "u8);

		// Version (uint16 LE)
		WriteUInt16LE (ms, (ushort)version);

		// APE_HEADER_OLD format
		// Compression type (uint16 LE)
		WriteUInt16LE (ms, 2000);

		// Format flags (uint16 LE)
		WriteUInt16LE (ms, 0);

		// Channels (uint16 LE)
		WriteUInt16LE (ms, (ushort)channels);

		// Sample rate (uint32 LE)
		WriteUInt32LE (ms, (uint)sampleRate);

		// Header bytes (uint32 LE)
		WriteUInt32LE (ms, 0);

		// Terminating bytes (uint32 LE)
		WriteUInt32LE (ms, 0);

		// Total frames (uint32 LE)
		WriteUInt32LE (ms, 100);

		// Final frame blocks (uint32 LE)
		WriteUInt32LE (ms, 4410);

		// Bits per sample (uint16 LE) - in old format, at specific offset
		WriteUInt16LE (ms, (ushort)bitsPerSample);

		// Padding to align
		WriteUInt16LE (ms, 0);

		// Minimal audio data placeholder
		ms.Write (new byte[100]);

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates APE file with APEv2 tag
	/// </summary>
	private static byte[] CreateMinimalApeFileWithTag (
		int version = 3990,
		string? title = null,
		string? artist = null)
	{
		var audioData = CreateMinimalApeFile (version);

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
