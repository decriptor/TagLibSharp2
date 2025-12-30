// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ogg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

/// <summary>
/// Tests for Ogg Vorbis write operations.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class OggVorbisFileWriteTests
{
	/// <summary>
	/// Creates a minimal valid Ogg Vorbis file for testing.
	/// </summary>
	static byte[] CreateMinimalOggVorbis (string title = "Test", string artist = "Artist")
	{
		// This creates a minimal Ogg Vorbis stream with:
		// - Page 1: Identification header (required for parsing)
		// - Page 2: Comment header with given metadata
		// - Page 3: Setup header (minimal, just enough to be valid)
		// - No audio pages (not needed for metadata tests)

		var pages = new List<byte[]> ();

		// Page 1: Identification header
		var identPacket = CreateIdentificationPacket ();
		pages.Add (CreateOggPage (identPacket, 0, OggPageFlags.BeginOfStream, 0, 0));

		// Page 2: Comment header + Setup header (minimal)
		var comment = new VorbisComment ("TagLibSharp2");
		comment.Title = title;
		comment.Artist = artist;
		var commentPacket = CreateCommentPacket (comment);

		var setupPacket = CreateMinimalSetupPacket ();

		// Comment and setup on one page with proper segment boundaries
		pages.Add (CreateOggPageWithPackets ([commentPacket, setupPacket], 1, OggPageFlags.None, 0, 1));

		// Combine all pages
		var totalSize = pages.Sum (p => p.Length);
		var result = new byte[totalSize];
		var offset = 0;
		foreach (var page in pages) {
			page.CopyTo (result, offset);
			offset += page.Length;
		}

		return result;
	}

	static byte[] CreateIdentificationPacket ()
	{
		// Vorbis identification header:
		// Type 1 + "vorbis" + version(4) + channels(1) + samplerate(4)
		// + bitrate_max(4) + bitrate_nom(4) + bitrate_min(4) + blocksize(1) + framing(1)
		// Total: 30 bytes
		var packet = new byte[30];
		packet[0] = 1; // Type 1 = identification
		packet[1] = (byte)'v';
		packet[2] = (byte)'o';
		packet[3] = (byte)'r';
		packet[4] = (byte)'b';
		packet[5] = (byte)'i';
		packet[6] = (byte)'s';
		// Version = 0 (bytes 7-10, already 0)
		packet[11] = 2; // Stereo
						// Sample rate = 44100 = 0xAC44
		packet[12] = 0x44;
		packet[13] = 0xAC;
		packet[14] = 0x00;
		packet[15] = 0x00;
		// Bitrates (can be 0)
		// Blocksize: 0x88 = blocksize_0=8, blocksize_1=8 (256 and 256 samples)
		packet[28] = 0x88;
		// Framing bit
		packet[29] = 1;
		return packet;
	}

	static byte[] CreateCommentPacket (VorbisComment comment)
	{
		// Type 3 + "vorbis" + comment data + framing bit
		var commentData = comment.Render ();
		var packet = new byte[7 + commentData.Length + 1];
		packet[0] = 3; // Type 3 = comment
		packet[1] = (byte)'v';
		packet[2] = (byte)'o';
		packet[3] = (byte)'r';
		packet[4] = (byte)'b';
		packet[5] = (byte)'i';
		packet[6] = (byte)'s';
		commentData.Span.CopyTo (packet.AsSpan (7));
		packet[packet.Length - 1] = 1; // Framing bit
		return packet;
	}

	static byte[] CreateMinimalSetupPacket ()
	{
		// Minimal setup header - just type + "vorbis" + minimal codebook data
		// In practice, setup headers are complex, but for testing metadata we just need
		// something that looks like a setup header
		var packet = new byte[8];
		packet[0] = 5; // Type 5 = setup
		packet[1] = (byte)'v';
		packet[2] = (byte)'o';
		packet[3] = (byte)'r';
		packet[4] = (byte)'b';
		packet[5] = (byte)'i';
		packet[6] = (byte)'s';
		packet[7] = 0; // Minimal data
		return packet;
	}

	static byte[] CombinePackets (byte[] packet1, byte[] packet2)
	{
		var result = new byte[packet1.Length + packet2.Length];
		packet1.CopyTo (result, 0);
		packet2.CopyTo (result, packet1.Length);
		return result;
	}

	/// <summary>
	/// Creates an Ogg page with multiple packets, properly building the segment table.
	/// </summary>
	static byte[] CreateOggPageWithPackets (byte[][] packets, uint sequenceNumber, OggPageFlags flags,
		ulong granulePosition, uint serialNumber)
	{
		// Build segment table - each packet needs proper segmentation
		var segments = new List<byte> ();
		var totalDataSize = 0;

		foreach (var packet in packets) {
			var remaining = packet.Length;
			while (remaining >= 255) {
				segments.Add (255);
				remaining -= 255;
			}
			// Final segment < 255 marks end of this packet
			segments.Add ((byte)remaining);
			totalDataSize += packet.Length;
		}

		// Build page header
		var headerSize = 27 + segments.Count;
		var page = new byte[headerSize + totalDataSize];

		// Magic "OggS"
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';

		// Version
		page[4] = 0;

		// Flags
		page[5] = (byte)flags;

		// Granule position (8 bytes LE)
		for (var i = 0; i < 8; i++)
			page[6 + i] = (byte)(granulePosition >> (i * 8));

		// Serial number (4 bytes LE)
		for (var i = 0; i < 4; i++)
			page[14 + i] = (byte)(serialNumber >> (i * 8));

		// Sequence number (4 bytes LE)
		for (var i = 0; i < 4; i++)
			page[18 + i] = (byte)(sequenceNumber >> (i * 8));

		// CRC placeholder (will be calculated)
		page[22] = 0;
		page[23] = 0;
		page[24] = 0;
		page[25] = 0;

		// Segment count
		page[26] = (byte)segments.Count;

		// Segment table
		for (var i = 0; i < segments.Count; i++)
			page[27 + i] = segments[i];

		// Data - concatenate all packets
		var dataOffset = headerSize;
		foreach (var packet in packets) {
			packet.CopyTo (page, dataOffset);
			dataOffset += packet.Length;
		}

		// Calculate and set CRC
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		return page;
	}

	static byte[] CreateOggPage (byte[] data, uint sequenceNumber, OggPageFlags flags,
		ulong granulePosition, uint serialNumber)
	{
		// Build segment table
		var segments = new List<byte> ();
		var remaining = data.Length;
		while (remaining > 0) {
			if (remaining >= 255) {
				segments.Add (255);
				remaining -= 255;
			} else {
				segments.Add ((byte)remaining);
				remaining = 0;
			}
		}

		// Build page header
		var headerSize = 27 + segments.Count;
		var page = new byte[headerSize + data.Length];

		// Magic "OggS"
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';

		// Version
		page[4] = 0;

		// Flags
		page[5] = (byte)flags;

		// Granule position (8 bytes LE)
		for (int i = 0; i < 8; i++)
			page[6 + i] = (byte)(granulePosition >> (i * 8));

		// Serial number (4 bytes LE)
		for (int i = 0; i < 4; i++)
			page[14 + i] = (byte)(serialNumber >> (i * 8));

		// Sequence number (4 bytes LE)
		for (int i = 0; i < 4; i++)
			page[18 + i] = (byte)(sequenceNumber >> (i * 8));

		// CRC placeholder (will be calculated)
		page[22] = 0;
		page[23] = 0;
		page[24] = 0;
		page[25] = 0;

		// Segment count
		page[26] = (byte)segments.Count;

		// Segment table
		for (int i = 0; i < segments.Count; i++)
			page[27 + i] = segments[i];

		// Data
		data.CopyTo (page, headerSize);

		// Calculate and set CRC
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)(crc & 0xFF);
		page[23] = (byte)((crc >> 8) & 0xFF);
		page[24] = (byte)((crc >> 16) & 0xFF);
		page[25] = (byte)((crc >> 24) & 0xFF);

		return page;
	}


	[TestMethod]
	public void CreateMinimalOggVorbis_CanBeRead ()
	{
		var data = CreateMinimalOggVorbis ("Test Song", "Test Artist");

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess, $"Failed to read: {result.Error}");
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Song", result.File.VorbisComment.Title);
		Assert.AreEqual ("Test Artist", result.File.VorbisComment.Artist);
	}



	[TestMethod]
	public void Render_ModifyTitle_PreservesChange ()
	{
		var originalData = CreateMinimalOggVorbis ("Original Title", "Original Artist");
		var result = OggVorbisFile.Read (originalData);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Modified Title";

		var rendered = result.File.Render (originalData);
		Assert.IsFalse (rendered.IsEmpty);

		var reRead = OggVorbisFile.Read (rendered.Span);
		Assert.IsTrue (reRead.IsSuccess, $"Failed to re-read: {reRead.Error}");
		Assert.AreEqual ("Modified Title", reRead.File!.Title);
		Assert.AreEqual ("Original Artist", reRead.File.Artist);
	}

	[TestMethod]
	public void Render_AddReplayGain_PreservesChange ()
	{
		var originalData = CreateMinimalOggVorbis ();
		var result = OggVorbisFile.Read (originalData);
		Assert.IsTrue (result.IsSuccess);

		result.File!.VorbisComment!.ReplayGainTrackGain = "-6.50 dB";
		result.File.VorbisComment.ReplayGainTrackPeak = "0.988547";

		var rendered = result.File.Render (originalData);
		var reRead = OggVorbisFile.Read (rendered.Span);

		Assert.IsTrue (reRead.IsSuccess);
		Assert.AreEqual ("-6.50 dB", reRead.File!.VorbisComment!.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", reRead.File.VorbisComment.ReplayGainTrackPeak);
	}

	[TestMethod]
	public void Render_AddMusicBrainz_PreservesChange ()
	{
		var originalData = CreateMinimalOggVorbis ();
		var result = OggVorbisFile.Read (originalData);
		Assert.IsTrue (result.IsSuccess);

		result.File!.VorbisComment!.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";

		var rendered = result.File.Render (originalData);
		var reRead = OggVorbisFile.Read (rendered.Span);

		Assert.IsTrue (reRead.IsSuccess);
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", reRead.File!.VorbisComment!.MusicBrainzTrackId);
	}

	[TestMethod]
	public void Render_ClearsMetadata_WorksCorrectly ()
	{
		var originalData = CreateMinimalOggVorbis ("Original", "Artist");
		var result = OggVorbisFile.Read (originalData);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = null;
		result.File.Artist = null;

		var rendered = result.File.Render (originalData);
		var reRead = OggVorbisFile.Read (rendered.Span);

		Assert.IsTrue (reRead.IsSuccess);
		Assert.IsNull (reRead.File!.Title);
		Assert.IsNull (reRead.File.Artist);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = CreateMinimalOggVorbis ();
		var result = OggVorbisFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var saveResult = await result.File!.SaveToFileAsync ();

		Assert.IsFalse (saveResult.IsSuccess);
		Assert.IsTrue (saveResult.Error!.Contains ("No source path"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithSourcePath_SavesBack ()
	{
		var originalData = CreateMinimalOggVorbis ("Original", "Artist");
		var tempPath = Path.Combine (Path.GetTempPath (), $"test_{Guid.NewGuid ()}.ogg");

		try {
			File.WriteAllBytes (tempPath, originalData);
			var result = await OggVorbisFile.ReadFromFileAsync (tempPath);
			Assert.IsTrue (result.IsSuccess);

			result.File!.Title = "Modified";
			var saveResult = await result.File.SaveToFileAsync ();

			Assert.IsTrue (saveResult.IsSuccess, $"Save failed: {saveResult.Error}");

			var reRead = await OggVorbisFile.ReadFromFileAsync (tempPath);
			Assert.IsTrue (reRead.IsSuccess);
			Assert.AreEqual ("Modified", reRead.File!.Title);
		} finally {
			if (File.Exists (tempPath))
				File.Delete (tempPath);
		}
	}

	[TestMethod]
	public async Task SaveToFileAsync_ToNewPath_SavesCorrectly ()
	{
		var originalData = CreateMinimalOggVorbis ("Original", "Artist");
		var sourcePath = Path.Combine (Path.GetTempPath (), $"source_{Guid.NewGuid ()}.ogg");
		var newPath = Path.Combine (Path.GetTempPath (), $"dest_{Guid.NewGuid ()}.ogg");

		try {
			File.WriteAllBytes (sourcePath, originalData);
			var result = await OggVorbisFile.ReadFromFileAsync (sourcePath);
			Assert.IsTrue (result.IsSuccess);

			result.File!.Title = "Modified";
			var saveResult = await result.File.SaveToFileAsync (newPath);

			Assert.IsTrue (saveResult.IsSuccess, $"Save failed: {saveResult.Error}");
			Assert.IsTrue (File.Exists (newPath));

			var reRead = await OggVorbisFile.ReadFromFileAsync (newPath);
			Assert.IsTrue (reRead.IsSuccess);
			Assert.AreEqual ("Modified", reRead.File!.Title);
		} finally {
			if (File.Exists (sourcePath))
				File.Delete (sourcePath);
			if (File.Exists (newPath))
				File.Delete (newPath);
		}
	}

}
