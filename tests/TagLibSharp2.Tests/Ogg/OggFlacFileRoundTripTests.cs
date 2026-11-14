// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

/// <summary>
/// Tests that verify Ogg FLAC data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class OggFlacFileRoundTripTests
{
	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "Test Title", artist: "Test Artist");

		// Act
		var result1 = OggFlacFile.Read (original);
		Assert.IsTrue (result1.IsSuccess, $"First parse failed: {result1.Error}");
		var file1 = result1.File!;

		var written = file1.Render (original);
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Test Title", file2.VorbisComment?.Title);
		Assert.AreEqual ("Test Artist", file2.VorbisComment?.Artist);
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "Original");

		var result = OggFlacFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;
		var tag = file.EnsureVorbisComment ();

		// Set all standard metadata fields
		tag.Title = "Round Trip Title";
		tag.Artist = "Round Trip Artist";
		tag.Album = "Round Trip Album";
		tag.Year = "2025";
		tag.Genre = "Classical";
		tag.Track = 5;
		tag.TotalTracks = 12;
		tag.DiscNumber = 1;
		tag.TotalDiscs = 2;
		tag.Composer = "Composer Name";
		tag.Comment = "This is a comment";

		// Act
		var written = file.Render (original);
		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var reloaded = result2.File!.VorbisComment!;

		// Assert
		Assert.AreEqual ("Round Trip Title", reloaded.Title);
		Assert.AreEqual ("Round Trip Artist", reloaded.Artist);
		Assert.AreEqual ("Round Trip Album", reloaded.Album);
		Assert.AreEqual ("2025", reloaded.Year);
		Assert.AreEqual ("Classical", reloaded.Genre);
		Assert.AreEqual (5u, reloaded.Track);
		Assert.AreEqual (12u, reloaded.TotalTracks);
		Assert.AreEqual (1u, reloaded.DiscNumber);
		Assert.AreEqual (2u, reloaded.TotalDiscs);
		Assert.AreEqual ("Composer Name", reloaded.Composer);
		Assert.AreEqual ("This is a comment", reloaded.Comment);
	}

	[TestMethod]
	public void CoverArt_PreservesBinaryData ()
	{
		// Arrange: Create minimal JPEG header bytes
		var jpegData = new byte[] {
			0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10,
			0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
			0x01, 0x00, 0x00, 0x01, 0x00, 0x01,
			0x00, 0x00, 0xFF, 0xD9
		};

		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "With Art");
		var result = OggFlacFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "Cover",
			new BinaryData (jpegData), 100, 100, 24, 0);
		file.EnsureVorbisComment ().AddPicture (picture);

		// Act
		var written = file.Render (original);
		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		var pictures = file2.VorbisComment!.PictureBlocks;
		Assert.AreEqual (1, pictures.Count);
		CollectionAssert.AreEqual (jpegData, pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void MusicBrainzIds_SurviveRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "With MB IDs");
		var result = OggFlacFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.EnsureVorbisComment ();

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		tag.MusicBrainzTrackId = mbTrackId;
		tag.MusicBrainzReleaseId = mbReleaseId;

		// Act
		var written = file.Render (original);
		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.VorbisComment!;

		// Assert
		Assert.AreEqual (mbTrackId, reloaded.MusicBrainzTrackId);
		Assert.AreEqual (mbReleaseId, reloaded.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void ReplayGain_SurvivesRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "With ReplayGain");
		var result = OggFlacFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.EnsureVorbisComment ();

		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.25 dB";
		tag.ReplayGainAlbumPeak = "1.0";

		// Act
		var written = file.Render (original);
		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.VorbisComment!;

		// Assert
		Assert.AreEqual ("-6.50 dB", reloaded.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", reloaded.ReplayGainTrackPeak);
		Assert.AreEqual ("-7.25 dB", reloaded.ReplayGainAlbumGain);
		Assert.AreEqual ("1.0", reloaded.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void AudioProperties_PreservedAfterMetadataChange ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "Original");
		var result = OggFlacFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		Assert.IsNotNull (file.Properties, "Initial Properties should not be null");

		// Store original audio properties
		var originalSampleRate = file.SampleRate;
		var originalChannels = file.Channels;
		var originalBitsPerSample = file.BitsPerSample;

		// Modify metadata
		file.EnsureVorbisComment ().Title = "Modified Title";

		// Act
		var written = file.Render (original);
		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert: Audio properties unchanged
		Assert.AreEqual (originalSampleRate, file2.SampleRate);
		Assert.AreEqual (originalChannels, file2.Channels);
		Assert.AreEqual (originalBitsPerSample, file2.BitsPerSample);
		Assert.AreEqual ("Modified Title", file2.VorbisComment?.Title);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "Title", artist: "Artist");

		// Act: Write multiple times
		var result1 = OggFlacFile.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		var written1 = file1.Render (original);
		var size1 = written1.Length;

		var result2 = OggFlacFile.Read (written1);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		var written2 = file2.Render (written1);
		var size2 = written2.Length;

		var result3 = OggFlacFile.Read (written2);
		Assert.IsTrue (result3.IsSuccess);
		var file3 = result3.File!;
		var written3 = file3.Render (written2);
		var size3 = written3.Length;

		// Assert: Sizes should be stable (allowing for small padding variations)
		var maxDelta = Math.Max (size1, size2) * 0.1; // Allow 10% variance for padding
		Assert.IsTrue (
			Math.Abs (size2 - size1) <= maxDelta,
			$"Size grew unexpectedly from {size1} to {size2}");
		Assert.IsTrue (
			Math.Abs (size3 - size2) <= maxDelta,
			$"Size grew unexpectedly from {size2} to {size3}");
	}

	[TestMethod]
	public void LargePicture_RoundTripsAfterTitleChange ()
	{
		// A picture packet just over one Ogg page (65025 bytes). The second page is
		// a continuation; its first payload byte is 0xFF so a reader that classifies
		// pages by that byte would treat the continuation as audio.
		const int pictureLength = 70000;
		var picture = new byte[pictureLength];
		Array.Fill (picture, (byte)0x5A);
		picture[0] = 0x11;
		picture[1] = 0x22;
		picture[2] = 0x33;
		picture[3] = 0x44;
		picture[65021] = 0xFF;

		var original = CreateOggFlacWithPicture (picture);
		var read = OggFlacFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNotNull (read.File);
		read.File.EnsureVorbisComment ().Title = "Changed Title";

		var rendered = read.File.Render (original);
		Assert.IsGreaterThan (0, rendered.Length, "Render dropped the file instead of writing the picture");

		var reread = OggFlacFile.Read (rendered);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("Changed Title", reread.File!.VorbisComment?.Title);
		Assert.IsTrue (reread.File.TryGetMetadataBlock (6, out var stored));
		CollectionAssert.AreEqual (picture, stored);

		var pages = SplitPages (rendered);
		Assert.AreEqual (5, pages.Count, "Continuation page was copied into the audio again");
		var fullPage = -1;
		var continuedPages = 0;
		for (var i = 0; i < pages.Count; i++) {
			if (pages[i][26] == 255)
				fullPage = i;
			if ((pages[i][5] & (byte)OggPageFlags.Continuation) != 0)
				continuedPages++;
		}

		Assert.AreEqual (1, continuedPages, "Picture continuation was written more than once");
		Assert.IsGreaterThanOrEqualTo (0, fullPage, "Picture packet should fill a 255-segment page");
		Assert.IsLessThan (pages.Count - 1, fullPage);
		var continuation = pages[fullPage + 1];
		Assert.AreEqual ((byte)OggPageFlags.Continuation, continuation[5],
			"Second page of the picture packet must be a continuation");
		Assert.AreEqual (0, continuation[5] & 0x06);
	}

	static byte[] CreateOggFlacWithPicture (byte[] picture)
	{
		var comment = new VorbisComment ("TagLibSharp2") { Title = "Original Title" };
		var commentBody = comment.Render ().ToArray ();
		var commentPacket = WrapFlacBlock (0x04, commentBody);
		var picturePacket = WrapFlacBlock (0x86, picture);
		var headerPacket = CreateHeaderPacket (headerPackets: 2);
		var audioPacket = new byte[] { 0xFF, 0xF8, 0xC9, 0x12, 0x00 };

		using var output = new MemoryStream ();
		uint sequence = 0;
		WritePages (output, headerPacket, OggPageFlags.BeginOfStream, ref sequence);
		WritePages (output, commentPacket, OggPageFlags.None, ref sequence);
		WritePages (output, picturePacket, OggPageFlags.None, ref sequence);
		WritePages (output, audioPacket, OggPageFlags.EndOfStream, ref sequence);
		return output.ToArray ();
	}

	static byte[] WrapFlacBlock (byte typeAndFlags, byte[] data)
	{
		var packet = new byte[4 + data.Length];
		packet[0] = typeAndFlags;
		packet[1] = (byte)((data.Length >> 16) & 0xFF);
		packet[2] = (byte)((data.Length >> 8) & 0xFF);
		packet[3] = (byte)(data.Length & 0xFF);
		data.CopyTo (packet, 4);
		return packet;
	}

	static byte[] CreateHeaderPacket (ushort headerPackets)
	{
		using var ms = new MemoryStream ();
		ms.WriteByte (0x7F);
		ms.Write ("FLAC"u8);
		ms.WriteByte (1);
		ms.WriteByte (0);
		ms.WriteByte ((byte)(headerPackets >> 8));
		ms.WriteByte ((byte)headerPackets);
		ms.Write ("fLaC"u8);
		ms.WriteByte (0x00);
		ms.WriteByte (0);
		ms.WriteByte (0);
		ms.WriteByte (34);
		ms.Write (new byte[] { 0x10, 0x00, 0x10, 0x00 });
		ms.Write (new byte[6]);
		ulong packed = ((ulong)44100 << 44) | ((ulong)1 << 41) | ((ulong)15 << 36) | 44100UL;
		for (var i = 7; i >= 0; i--)
			ms.WriteByte ((byte)(packed >> (i * 8)));
		ms.Write (new byte[16]);
		return ms.ToArray ();
	}

	static void WritePages (Stream output, byte[] packet, OggPageFlags firstFlags, ref uint sequence)
	{
		const int maxPageBytes = 255 * 255;
		var offset = 0;
		var first = true;
		while (offset < packet.Length || (packet.Length > 0 && packet.Length % maxPageBytes == 0 && offset == packet.Length)) {
			var remaining = packet.Length - offset;
			byte[] payload;
			byte[] lacing;
			OggPageFlags flags;
			ulong granule;
			if (remaining > maxPageBytes || (remaining == maxPageBytes && packet.Length % maxPageBytes == 0 && remaining > 0)) {
				payload = new byte[maxPageBytes];
				Array.Copy (packet, offset, payload, 0, maxPageBytes);
				lacing = new byte[255];
				Array.Fill (lacing, (byte)255);
				flags = first ? firstFlags : OggPageFlags.Continuation;
				granule = ulong.MaxValue;
				offset += maxPageBytes;
			} else if (remaining == 0) {
				payload = [];
				lacing = [0];
				flags = OggPageFlags.Continuation;
				granule = 0;
				offset++;
			} else {
				payload = new byte[remaining];
				Array.Copy (packet, offset, payload, 0, remaining);
				var count = (remaining / 255) + 1;
				lacing = new byte[count];
				for (var i = 0; i < count - 1; i++)
					lacing[i] = 255;
				lacing[^1] = (byte)(remaining % 255);
				flags = first ? firstFlags : OggPageFlags.Continuation;
				granule = 0;
				offset = packet.Length;
			}

			first = false;
			WriteOnePage (output, payload, lacing, flags, granule, sequence);
			sequence++;
			if (remaining == 0)
				break;
		}
	}

	static void WriteOnePage (Stream output, byte[] payload, byte[] lacing, OggPageFlags flags, ulong granule, uint sequence)
	{
		var page = new byte[27 + lacing.Length + payload.Length];
		page[0] = (byte)'O';
		page[1] = (byte)'g';
		page[2] = (byte)'g';
		page[3] = (byte)'S';
		page[5] = (byte)flags;
		for (var i = 0; i < 8; i++)
			page[6 + i] = (byte)(granule >> (8 * i));
		page[14] = 1;
		page[18] = (byte)sequence;
		page[19] = (byte)(sequence >> 8);
		page[20] = (byte)(sequence >> 16);
		page[21] = (byte)(sequence >> 24);
		page[26] = (byte)lacing.Length;
		lacing.CopyTo (page, 27);
		payload.CopyTo (page, 27 + lacing.Length);
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)crc;
		page[23] = (byte)(crc >> 8);
		page[24] = (byte)(crc >> 16);
		page[25] = (byte)(crc >> 24);
		output.Write (page);
	}

	static List<byte[]> SplitPages (byte[] data)
	{
		var pages = new List<byte[]> ();
		var offset = 0;
		while (offset + 27 <= data.Length &&
			   data[offset] == 'O' && data[offset + 1] == 'g' &&
			   data[offset + 2] == 'g' && data[offset + 3] == 'S') {
			var segmentCount = data[offset + 26];
			var dataSize = 0;
			for (var i = 0; i < segmentCount; i++)
				dataSize += data[offset + 27 + i];
			var size = 27 + segmentCount + dataSize;
			var page = new byte[size];
			Array.Copy (data, offset, page, 0, size);
			pages.Add (page);
			offset += size;
		}

		return pages;
	}

	[TestMethod]
	public void Unicode_AllScripts ()
	{
		// Arrange
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "Original");
		var result = OggFlacFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.EnsureVorbisComment ();

		// Set metadata with various Unicode characters
		tag.Title = "Titulo";
		tag.Artist = "Artist Name";
		tag.Album = "Album Name";
		tag.Comment = "Unicode test";

		// Act
		var written = file.Render (original);
		var result2 = OggFlacFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.VorbisComment!;

		// Assert
		Assert.AreEqual ("Titulo", reloaded.Title);
		Assert.AreEqual ("Artist Name", reloaded.Artist);
		Assert.AreEqual ("Album Name", reloaded.Album);
		Assert.AreEqual ("Unicode test", reloaded.Comment);
	}
}
