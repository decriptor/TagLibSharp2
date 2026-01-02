// TDD Tests for Musepack (.mpc) file support
// Written first to define expected behavior

using TagLibSharp2.Core;
using TagLibSharp2.Musepack;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Musepack;

[TestClass]
[TestCategory ("Unit")]
public class MusepackFileTests
{
	// Musepack sample rate table (SV7)
	private static readonly int[] SampleRateTable = [44100, 48000, 37800, 32000];

	#region Magic and File Recognition

	[TestMethod]
	public void Parse_SV7Magic_ReturnsSuccess ()
	{
		var data = CreateMinimalMusepackSV7File ();
		var result = MusepackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void Parse_SV8Magic_ReturnsSuccess ()
	{
		var data = CreateMinimalMusepackSV8File ();
		var result = MusepackFile.Parse (data);
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

		var result = MusepackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("magic") || result.Error.Contains ("MP+") || result.Error.Contains ("MPCK"));
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsError ()
	{
		var data = new byte[3]; // Too short for magic
		var result = MusepackFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
	}

	#endregion

	#region SV7 Header Parsing

	[TestMethod]
	public void Parse_SV7_ExtractsVersion ()
	{
		var data = CreateMinimalMusepackSV7File (streamVersion: 7);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (7, result.File!.StreamVersion);
	}

	[TestMethod]
	public void Parse_SV7_ExtractsSampleRate_44100 ()
	{
		// 44100 = index 0 in sample rate table
		var data = CreateMinimalMusepackSV7File (sampleRateIndex: 0);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_SV7_ExtractsSampleRate_48000 ()
	{
		// 48000 = index 1 in sample rate table
		var data = CreateMinimalMusepackSV7File (sampleRateIndex: 1);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (48000, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_SV7_ExtractsChannels ()
	{
		var data = CreateMinimalMusepackSV7File (channels: 2);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_SV7_ExtractsFrameCount ()
	{
		var data = CreateMinimalMusepackSV7File (frameCount: 1000);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1000u, result.File!.FrameCount);
	}

	#endregion

	#region SV8 Header Parsing

	[TestMethod]
	public void Parse_SV8_ExtractsVersion ()
	{
		var data = CreateMinimalMusepackSV8File ();
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (8, result.File!.StreamVersion);
	}

	[TestMethod]
	public void Parse_SV8_ExtractsSampleRate ()
	{
		var data = CreateMinimalMusepackSV8File (sampleRate: 44100);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_SV8_ExtractsChannels ()
	{
		var data = CreateMinimalMusepackSV8File (channels: 2);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_SV8_ExtractsTotalSamples ()
	{
		var data = CreateMinimalMusepackSV8File (totalSamples: 441000);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (441000UL, result.File!.TotalSamples);
	}

	#endregion

	#region AudioProperties

	[TestMethod]
	public void Parse_SV7_CalculatesDuration ()
	{
		// 1152 samples per frame * 100 frames at 44100 Hz
		var data = CreateMinimalMusepackSV7File (frameCount: 100, sampleRateIndex: 0);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Properties);
		// Duration should be approximately (100 * 1152) / 44100 ≈ 2.6 seconds
		var expectedDuration = 100.0 * 1152 / 44100;
		Assert.IsTrue (Math.Abs (result.File.Properties!.Duration.TotalSeconds - expectedDuration) < 0.1);
	}

	[TestMethod]
	public void Parse_SV8_CalculatesDuration ()
	{
		// 441000 samples at 44100 Hz = 10 seconds
		var data = CreateMinimalMusepackSV8File (totalSamples: 441000, sampleRate: 44100);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Properties);
		Assert.IsTrue (result.File.Properties!.Duration.TotalSeconds > 9.9 && result.File.Properties.Duration.TotalSeconds < 10.1);
	}

	[TestMethod]
	public void Properties_MatchesFileProperties ()
	{
		var data = CreateMinimalMusepackSV7File (sampleRateIndex: 1, channels: 2);
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		var props = file.Properties!;

		Assert.AreEqual (file.SampleRate, props.SampleRate);
		Assert.AreEqual (file.Channels, props.Channels);
	}

	#endregion

	#region APE Tag Support

	[TestMethod]
	public void Parse_NoTag_ApeTagIsNull ()
	{
		var data = CreateMinimalMusepackSV7File ();
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.ApeTag);
	}

	[TestMethod]
	public void Parse_WithApeTag_ReadsTitle ()
	{
		var data = CreateMusepackFileWithTag (title: "Test Song");
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);
		Assert.AreEqual ("Test Song", result.File.ApeTag!.Title);
	}

	[TestMethod]
	public void Parse_WithApeTag_ReadsArtist ()
	{
		var data = CreateMusepackFileWithTag (artist: "Test Artist");
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.ApeTag);
		Assert.AreEqual ("Test Artist", result.File.ApeTag!.Artist);
	}

	[TestMethod]
	public void EnsureApeTag_CreatesTag ()
	{
		var data = CreateMinimalMusepackSV7File ();
		var result = MusepackFile.Parse (data);
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
		var data = CreateMusepackFileWithTag (title: "Test");
		var result = MusepackFile.Parse (data);
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
		var data = CreateMinimalMusepackSV7File ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.mpc", data);

		var result = MusepackFile.ReadFromFile ("/test.mpc", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalMusepackSV7File ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.mpc", data);

		var result = await MusepackFile.ReadFromFileAsync ("/test.mpc", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void SaveToFile_PreservesAudioData ()
	{
		var data = CreateMinimalMusepackSV7File ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.mpc", data);

		var readResult = MusepackFile.ReadFromFile ("/test.mpc", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureApeTag ().Title = "New Title";

		var saveResult = file.SaveToFile ("/output.mpc", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Re-read and verify
		var verifyResult = MusepackFile.ReadFromFile ("/output.mpc", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("New Title", verifyResult.File!.ApeTag!.Title);
	}

	#endregion

	#region Render

	[TestMethod]
	public void Render_WithNewTag_AppendsTagToAudioData ()
	{
		var data = CreateMinimalMusepackSV7File ();
		var result = MusepackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.EnsureApeTag ().Title = "Test";

		var rendered = file.Render (data);

		// Rendered data should be larger than original (tag added)
		Assert.IsTrue (rendered.Length > data.Length);

		// Should still be valid Musepack file
		var reparsed = MusepackFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Test", reparsed.File!.ApeTag!.Title);
	}

	[TestMethod]
	public void Render_RemoveTag_StripsTagFromFile ()
	{
		var data = CreateMusepackFileWithTag (title: "Original");
		var result = MusepackFile.Parse (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.RemoveApeTag ();

		var rendered = file.Render (data);

		// Re-parse should have no tag
		var reparsed = MusepackFile.Parse (rendered);
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.IsNull (reparsed.File!.ApeTag);
	}

	#endregion

	#region Edge Cases

	[TestMethod]
	public void Parse_AllSV7SampleRates ()
	{
		// Test all 4 SV7 sample rates
		for (int i = 0; i < SampleRateTable.Length; i++) {
			var data = CreateMinimalMusepackSV7File (sampleRateIndex: i);
			var result = MusepackFile.Parse (data);

			Assert.IsTrue (result.IsSuccess, $"Failed for sample rate index {i}");
			Assert.AreEqual (SampleRateTable[i], result.File!.SampleRate, $"Wrong sample rate for index {i}");
		}
	}

	[TestMethod]
	public void Parse_SV7_DefaultsStereo ()
	{
		// SV7 format doesn't explicitly store channel count in header
		// It's always assumed stereo for most implementations
		var data = CreateMinimalMusepackSV7File ();
		var result = MusepackFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Creates a minimal Musepack SV7 file (public for use in MediaFileTests).
	/// </summary>
	public static byte[] CreateMinimalMusepackSV7FilePublic () => CreateMinimalMusepackSV7File ();

	/// <summary>
	/// Creates a minimal Musepack SV7 file.
	/// SV7 header structure (per Musepack spec):
	/// [0-2] Magic "MP+"
	/// [3] Stream version (upper 4 bits) | intensity stereo (lower 4 bits)
	/// [4-7] Frame count (32-bit LE)
	/// ... other header fields
	/// </summary>
	private static byte[] CreateMinimalMusepackSV7File (
		int streamVersion = 7,
		int sampleRateIndex = 0, // 44100
		int channels = 2,
		uint frameCount = 100)
	{
		using var ms = new MemoryStream ();

		// [0-2] Magic "MP+"
		ms.Write ("MP+"u8);

		// [3] Version (upper 4 bits) | flags (lower 4 bits)
		// Bits 7-4: stream version
		// Bits 3-0: intensity stereo flags
		ms.WriteByte ((byte)((streamVersion << 4) | 0));

		// [4-7] Frame count (32-bit LE)
		WriteUInt32LE (ms, frameCount);

		// [8-9] Max level (unused for parsing)
		WriteUInt16LE (ms, 0);

		// [10-13] Flags and profile info
		// Bits 31-30: sample rate index (2 bits) - stored at bit position in DWORD
		// We need to encode sample rate in the flags field
		uint flags = 0;
		flags |= (uint)(sampleRateIndex & 0x3) << 30; // Sample rate at bits 30-31
													  // Channels are derived: stereo by default unless mid-side only
													  // For simplicity, we'll treat channels as 2 for standard stereo
		WriteUInt32LE (ms, flags);

		// [14-15] Encoder info (unused for parsing)
		WriteUInt16LE (ms, 0);

		// Minimal audio data (enough to be valid)
		var audioData = new byte[64];
		ms.Write (audioData);

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates a minimal Musepack SV8 file.
	/// SV8 uses packet-based format with "MPCK" magic.
	/// </summary>
	private static byte[] CreateMinimalMusepackSV8File (
		int sampleRate = 44100,
		int channels = 2,
		ulong totalSamples = 441000)
	{
		using var ms = new MemoryStream ();

		// [0-3] Magic "MPCK"
		ms.Write ("MPCK"u8);

		// SH packet (Stream Header)
		var shPacket = CreateSV8StreamHeaderPacket (sampleRate, channels, totalSamples);
		ms.Write (shPacket);

		// Minimal audio data placeholder
		var audioData = new byte[64];
		ms.Write (audioData);

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates an SV8 Stream Header (SH) packet.
	/// </summary>
	private static byte[] CreateSV8StreamHeaderPacket (int sampleRate, int channels, ulong totalSamples)
	{
		using var ms = new MemoryStream ();

		// Packet key: "SH" (2 bytes)
		ms.Write ("SH"u8);

		// Packet size as variable-length integer
		// For simplicity, we'll calculate the size and use a single-byte size if possible
		using var payloadMs = new MemoryStream ();

		// SH payload:
		// [0] CRC (skip for test - 1 byte for version indicator)
		payloadMs.WriteByte (8); // Version 8

		// Sample count as variable-length integer
		WriteVarInt (payloadMs, totalSamples);

		// Beginning silence as variable-length integer
		WriteVarInt (payloadMs, 0);

		// Sample rate index (3 bits) | channels - 1 (lower 5 bits) combined
		// Sample rate: 0=44100, 1=48000, 2=37800, 3=32000
		var sampleRateIdx = sampleRate switch {
			44100 => 0,
			48000 => 1,
			37800 => 2,
			32000 => 3,
			_ => 0
		};
		// Encode as: sample_rate_index (3 bits) + padding, then channels
		payloadMs.WriteByte ((byte)sampleRateIdx);
		payloadMs.WriteByte ((byte)(channels - 1));

		var payload = payloadMs.ToArray ();

		// Size includes the key (2) + size field (1+) + payload
		var totalSize = 2 + 1 + payload.Length; // Simplified: assuming size fits in 1 byte
		WriteVarInt (ms, (ulong)totalSize);

		ms.Write (payload);

		return ms.ToArray ();
	}

	/// <summary>
	/// Creates Musepack file with APEv2 tag.
	/// </summary>
	private static byte[] CreateMusepackFileWithTag (
		string? title = null,
		string? artist = null)
	{
		var audioData = CreateMinimalMusepackSV7File ();

		// Build APEv2 tag items
		var items = new List<byte[]> ();
		if (title is not null)
			items.Add (CreateApeTagItem ("Title", title));
		if (artist is not null)
			items.Add (CreateApeTagItem ("Artist", artist));

		var itemsData = items.SelectMany (x => x).ToArray ();

		using var ms = new MemoryStream ();
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

	private static void WriteVarInt (Stream s, ulong value)
	{
		// Write variable-length integer (7 bits per byte, MSB indicates continuation)
		// For simplicity, handle values up to 127 in one byte
		if (value <= 0x7F) {
			s.WriteByte ((byte)value);
		} else {
			// Multi-byte encoding
			var bytes = new List<byte> ();
			while (value > 0) {
				bytes.Add ((byte)(value & 0x7F));
				value >>= 7;
			}
			// Write in reverse with continuation bits
			for (int i = bytes.Count - 1; i >= 0; i--) {
				var b = bytes[i];
				if (i > 0) b |= 0x80; // Set continuation bit
				s.WriteByte (b);
			}
		}
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
