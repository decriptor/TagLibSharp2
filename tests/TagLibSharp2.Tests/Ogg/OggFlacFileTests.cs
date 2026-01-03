// TDD Tests for Ogg FLAC (.oga/.ogg) file support
// Written first to define expected behavior

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("OggFlac")]
public class OggFlacFileTests
{
	#region Magic and File Recognition

	[TestMethod]
	public void Parse_ValidOggFlac_ReturnsSuccess ()
	{
		var data = CreateMinimalOggFlacFile ();
		var result = OggFlacFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void Parse_InvalidMagic_ReturnsError ()
	{
		var data = new byte[100];
		var result = OggFlacFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsError ()
	{
		var data = new byte[3];
		var result = OggFlacFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Parse_RegularOggVorbis_ReturnsError ()
	{
		// An Ogg Vorbis file should not parse as Ogg FLAC
		var data = CreateMinimalOggVorbisFile ();
		var result = OggFlacFile.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("FLAC") || result.Error.Contains ("stream"));
	}

	#endregion

	#region Stream Info Parsing

	[TestMethod]
	public void Parse_ExtractsSampleRate ()
	{
		var data = CreateMinimalOggFlacFile (sampleRate: 44100);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (44100, result.File!.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsChannels ()
	{
		var data = CreateMinimalOggFlacFile (channels: 2);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (2, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_ExtractsBitsPerSample ()
	{
		var data = CreateMinimalOggFlacFile (bitsPerSample: 16);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (16, result.File!.BitsPerSample);
	}

	[TestMethod]
	public void Parse_ExtractsTotalSamples ()
	{
		var data = CreateMinimalOggFlacFile (totalSamples: 441000);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (441000UL, result.File!.TotalSamples);
	}

	#endregion

	#region Audio Properties

	[TestMethod]
	public void Parse_CalculatesDuration ()
	{
		// 441000 samples at 44100 Hz = 10 seconds
		var data = CreateMinimalOggFlacFile (sampleRate: 44100, totalSamples: 441000);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.Properties);
		var duration = result.File.Properties!.Duration;
		Assert.IsTrue (duration.TotalSeconds > 9.9 && duration.TotalSeconds < 10.1);
	}

	[TestMethod]
	public void Properties_MatchesFileProperties ()
	{
		var data = CreateMinimalOggFlacFile (sampleRate: 48000, channels: 2, bitsPerSample: 24);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		var props = file.Properties!;

		Assert.AreEqual (file.SampleRate, props.SampleRate);
		Assert.AreEqual (file.Channels, props.Channels);
		Assert.AreEqual (file.BitsPerSample, props.BitsPerSample);
	}

	#endregion

	#region Vorbis Comment Support

	[TestMethod]
	public void Parse_NoComment_VorbisCommentIsNull ()
	{
		var data = CreateMinimalOggFlacFile (includeVorbisComment: false);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.VorbisComment);
	}

	[TestMethod]
	public void Parse_WithVorbisComment_ReadsTitle ()
	{
		var data = CreateMinimalOggFlacFileWithComment (title: "Test Song");
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Song", result.File.VorbisComment!.Title);
	}

	[TestMethod]
	public void Parse_WithVorbisComment_ReadsArtist ()
	{
		var data = CreateMinimalOggFlacFileWithComment (artist: "Test Artist");
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Artist", result.File.VorbisComment!.Artist);
	}

	[TestMethod]
	public void EnsureVorbisComment_CreatesComment ()
	{
		var data = CreateMinimalOggFlacFile (includeVorbisComment: false);
		var result = OggFlacFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);

		var file = result.File!;
		Assert.IsNull (file.VorbisComment);

		var comment = file.EnsureVorbisComment ();
		Assert.IsNotNull (comment);
		Assert.AreSame (comment, file.VorbisComment);
	}

	[TestMethod]
	public void RemoveVorbisComment_RemovesExistingComment ()
	{
		var data = CreateMinimalOggFlacFileWithComment (title: "Test");
		var result = OggFlacFile.Parse (data);
		Assert.IsTrue (result.IsSuccess, result.Error);

		var file = result.File!;
		Assert.IsNotNull (file.VorbisComment);

		file.RemoveVorbisComment ();
		Assert.IsNull (file.VorbisComment);
	}

	#endregion

	#region File I/O

	[TestMethod]
	public void ReadFromFile_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalOggFlacFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.oga", data);

		var result = OggFlacFile.ReadFromFile ("/test.oga", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = CreateMinimalOggFlacFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.oga", data);

		var result = await OggFlacFile.ReadFromFileAsync ("/test.oga", mockFs);
		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void SaveToFile_PreservesAudioData ()
	{
		var data = CreateMinimalOggFlacFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.oga", data);

		var readResult = OggFlacFile.ReadFromFile ("/test.oga", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureVorbisComment ().Title = "New Title";

		var saveResult = file.SaveToFile ("/output.oga", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Re-read and verify
		var verifyResult = OggFlacFile.ReadFromFile ("/output.oga", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("New Title", verifyResult.File!.VorbisComment!.Title);
	}

	[TestMethod]
	public void SaveToFile_WithoutPath_UsesSourcePath ()
	{
		var data = CreateMinimalOggFlacFile ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.oga", data);

		var readResult = OggFlacFile.ReadFromFile ("/music/song.oga", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.EnsureVorbisComment ().Title = "Updated";

		var saveResult = file.SaveToFile (mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Verify saved to original path
		var verifyResult = OggFlacFile.ReadFromFile ("/music/song.oga", mockFs);
		Assert.AreEqual ("Updated", verifyResult.File!.VorbisComment!.Title);
	}

	#endregion

	#region Edge Cases

	[TestMethod]
	public void Parse_HighRes_96kHz24bit ()
	{
		var data = CreateMinimalOggFlacFile (sampleRate: 96000, bitsPerSample: 24);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (96000, result.File!.SampleRate);
		Assert.AreEqual (24, result.File.BitsPerSample);
	}

	[TestMethod]
	public void Parse_Mono ()
	{
		var data = CreateMinimalOggFlacFile (channels: 1);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1, result.File!.Channels);
	}

	[TestMethod]
	public void Parse_5Point1Surround ()
	{
		var data = CreateMinimalOggFlacFile (channels: 6);
		var result = OggFlacFile.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (6, result.File!.Channels);
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Creates a minimal Ogg FLAC file
	/// </summary>
	private static byte[] CreateMinimalOggFlacFile (
		int sampleRate = 44100,
		int channels = 2,
		int bitsPerSample = 16,
		ulong totalSamples = 44100,
		bool includeVorbisComment = true)
	{
		using var ms = new MemoryStream ();

		// Ogg page 1: FLAC header packet
		var headerPacket = CreateFlacHeaderPacket (sampleRate, channels, bitsPerSample, totalSamples);
		WriteOggPage (ms, headerPacket, 0, true, false, 0, 0);

		// Ogg page 2: Vorbis comment (if included)
		if (includeVorbisComment) {
			var commentPacket = CreateVorbisCommentPacket ();
			WriteOggPage (ms, commentPacket, 1, false, false, headerPacket.Length, 1);
		}

		// Ogg page 3: End of stream (minimal)
		WriteOggPage (ms, Array.Empty<byte> (), 2, false, true, headerPacket.Length + (includeVorbisComment ? 20 : 0), 2);

		return ms.ToArray ();
	}

	private static byte[] CreateMinimalOggFlacFileWithComment (
		string? title = null,
		string? artist = null)
	{
		using var ms = new MemoryStream ();

		// Ogg page 1: FLAC header packet
		var headerPacket = CreateFlacHeaderPacket (44100, 2, 16, 44100);
		WriteOggPage (ms, headerPacket, 0, true, false, 0, 0);

		// Ogg page 2: Vorbis comment with title/artist
		var commentPacket = CreateVorbisCommentPacket (title, artist);
		WriteOggPage (ms, commentPacket, 1, false, false, headerPacket.Length, 1);

		// Ogg page 3: End of stream
		WriteOggPage (ms, Array.Empty<byte> (), 2, false, true, headerPacket.Length + commentPacket.Length, 2);

		return ms.ToArray ();
	}

	private static byte[] CreateMinimalOggVorbisFile ()
	{
		using var ms = new MemoryStream ();

		// Vorbis identification header (minimal)
		var identPacket = new byte[30];
		identPacket[0] = 1; // Packet type
		System.Text.Encoding.ASCII.GetBytes ("vorbis").CopyTo (identPacket, 1);

		WriteOggPage (ms, identPacket, 0, true, false, 0, 0);
		WriteOggPage (ms, Array.Empty<byte> (), 1, false, true, 30, 1);

		return ms.ToArray ();
	}

	private static byte[] CreateFlacHeaderPacket (int sampleRate, int channels, int bitsPerSample, ulong totalSamples)
	{
		using var ms = new MemoryStream ();

		// Ogg FLAC header: 0x7F "FLAC" + version + header count + "fLaC" + STREAMINFO
		ms.WriteByte (0x7F); // Packet type
		ms.Write ("FLAC"u8);

		// Ogg FLAC mapping version
		ms.WriteByte (1); // Major version
		ms.WriteByte (0); // Minor version

		// Number of header packets (big-endian 16-bit)
		WriteUInt16BE (ms, 1); // One header packet (STREAMINFO)

		// Native FLAC header: "fLaC"
		ms.Write ("fLaC"u8);

		// STREAMINFO metadata block
		// Block header: last-block flag (1) + type (0=STREAMINFO) + size (34)
		ms.WriteByte (0x80); // Last block, type 0
		WriteUInt24BE (ms, 34); // STREAMINFO is 34 bytes

		// STREAMINFO data (34 bytes)
		// Min block size (16 bits) + Max block size (16 bits)
		WriteUInt16BE (ms, 4096);
		WriteUInt16BE (ms, 4096);

		// Min frame size (24 bits) + Max frame size (24 bits)
		WriteUInt24BE (ms, 0);
		WriteUInt24BE (ms, 0);

		// Sample rate (20 bits) + channels-1 (3 bits) + bits-1 (5 bits) + total samples high (4 bits)
		// This is a packed 64-bit field
		ulong packed = 0;
		packed |= (ulong)(sampleRate & 0xFFFFF) << 44; // 20 bits for sample rate
		packed |= (ulong)((channels - 1) & 0x7) << 41; // 3 bits for channels
		packed |= (ulong)((bitsPerSample - 1) & 0x1F) << 36; // 5 bits for bits per sample
		packed |= totalSamples & 0xFFFFFFFFF; // 36 bits for total samples

		WriteUInt64BE (ms, packed);

		// MD5 signature (16 bytes)
		ms.Write (new byte[16]);

		return ms.ToArray ();
	}

	private static byte[] CreateVorbisCommentPacket (string? title = null, string? artist = null)
	{
		using var ms = new MemoryStream ();

		// Vorbis comment block header (for FLAC in Ogg)
		// Type 4 = VORBIS_COMMENT
		ms.WriteByte (0x84); // Last = false, type = 4

		// We'll fill in the size after
		var commentData = CreateVorbisCommentData (title, artist);
		WriteUInt24BE (ms, (uint)commentData.Length);
		ms.Write (commentData);

		return ms.ToArray ();
	}

	private static byte[] CreateVorbisCommentData (string? title = null, string? artist = null)
	{
		using var ms = new MemoryStream ();

		// Vendor string
		var vendor = System.Text.Encoding.UTF8.GetBytes ("TagLibSharp2");
		WriteUInt32LE (ms, (uint)vendor.Length);
		ms.Write (vendor);

		// Count fields
		var comments = new List<string> ();
		if (title is not null) comments.Add ($"TITLE={title}");
		if (artist is not null) comments.Add ($"ARTIST={artist}");

		WriteUInt32LE (ms, (uint)comments.Count);

		foreach (var comment in comments) {
			var bytes = System.Text.Encoding.UTF8.GetBytes (comment);
			WriteUInt32LE (ms, (uint)bytes.Length);
			ms.Write (bytes);
		}

		return ms.ToArray ();
	}

	private static void WriteOggPage (Stream ms, byte[] data, int pageSequence, bool bos, bool eos, long granulePosition, int segmentCount)
	{
		// Ogg page header
		ms.Write ("OggS"u8); // Magic
		ms.WriteByte (0); // Version

		// Flags
		byte flags = 0;
		if (bos) flags |= 0x02; // Beginning of stream
		if (eos) flags |= 0x04; // End of stream
		ms.WriteByte (flags);

		// Granule position (8 bytes LE)
		WriteUInt64LE (ms, (ulong)granulePosition);

		// Serial number (4 bytes)
		WriteUInt32LE (ms, 1);

		// Page sequence (4 bytes)
		WriteUInt32LE (ms, (uint)pageSequence);

		// CRC (4 bytes) - would need to calculate, using 0 for simplicity
		WriteUInt32LE (ms, 0);

		// Number of segments
		var numSegments = data.Length == 0 ? 0 : (data.Length + 254) / 255;
		ms.WriteByte ((byte)numSegments);

		// Segment table
		var remaining = data.Length;
		for (int i = 0; i < numSegments; i++) {
			var segSize = Math.Min (255, remaining);
			ms.WriteByte ((byte)segSize);
			remaining -= segSize;
		}

		// Data
		ms.Write (data);
	}

	private static void WriteUInt64BE (Stream s, ulong v)
	{
		for (int i = 7; i >= 0; i--)
			s.WriteByte ((byte)(v >> (i * 8)));
	}

	private static void WriteUInt64LE (Stream s, ulong v)
	{
		for (int i = 0; i < 8; i++)
			s.WriteByte ((byte)(v >> (i * 8)));
	}

	private static void WriteUInt32LE (Stream s, uint v)
	{
		s.WriteByte ((byte)v);
		s.WriteByte ((byte)(v >> 8));
		s.WriteByte ((byte)(v >> 16));
		s.WriteByte ((byte)(v >> 24));
	}

	private static void WriteUInt24BE (Stream s, uint v)
	{
		s.WriteByte ((byte)(v >> 16));
		s.WriteByte ((byte)(v >> 8));
		s.WriteByte ((byte)v);
	}

	private static void WriteUInt16BE (Stream s, ushort v)
	{
		s.WriteByte ((byte)(v >> 8));
		s.WriteByte ((byte)v);
	}

	#endregion
}
