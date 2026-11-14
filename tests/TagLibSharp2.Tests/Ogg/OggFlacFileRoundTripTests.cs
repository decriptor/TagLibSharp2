// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Tests.Core;
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
	public void TitleSave_RoundTripsCommentAndPictureWithoutSecondWalk ()
	{
		var picture = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
		var original = CreateOggFlacWithPicture (picture);
		var read = OggFlacFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNotNull (read.File);
		Assert.AreEqual ("Original Title", read.File.VorbisComment?.Title);
		Assert.AreEqual (1, read.File.PacketWalkCount, "Read should walk packets once");

		read.File.EnsureVorbisComment ().Title = "Saved Title";
		var rendered = read.File.Render (original);
		Assert.IsGreaterThan (0, rendered.Length);
		Assert.AreEqual (1, read.File.PacketWalkCount, "Render walked an unchanged file again");

		var reread = OggFlacFile.Read (rendered);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("Saved Title", reread.File!.VorbisComment?.Title);
		Assert.IsTrue (reread.File.TryGetMetadataBlock (6, out var stored));
		CollectionAssert.AreEqual (picture, stored);
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

	[TestMethod]
	[DataRow (65025)]
	[DataRow (130050)]
	public void PicturePacket_MultipleOf65025_RoundTripsWithZeroLacing (int packetLength)
	{
		// 255 * 255 bytes fills a page. The packet still needs a lacing value of 0 on the next
		// page. The payload guess is packetLength - 4; the assertion is the measured packet.
		var picture = new byte[packetLength - 4];
		picture[0] = 0x21;
		picture[^1] = 0x43;
		if (picture.Length > 65021)
			picture[65021] = 0xFF;

		var original = CreateOggFlacWithPicture (picture);
		var read = OggFlacFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.EnsureVorbisComment ().Title = "Boundary";

		var rendered = read.File.Render (original);
		Assert.IsGreaterThan (0, rendered.Length);
		var picturePacket = OggPacketAssembler.Read (rendered)
			.Single (packet => packet.Data.Length > 0 && (packet.Data[0] & 0x7F) == 6);
		AssertEndsOnZeroLacing (picturePacket, packetLength);

		var reread = OggFlacFile.Read (rendered);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("Boundary", reread.File!.VorbisComment?.Title);
		Assert.IsTrue (reread.File.TryGetMetadataBlock (6, out var stored));
		CollectionAssert.AreEqual (picture, stored);
	}

	[TestMethod]
	public void SaveToFile_CommentBlockPast24Bits_LeavesFileUnchanged ()
	{
		// A picture is stored as base64 inside the Vorbis comment. Past 0xFFFFFF bytes the
		// 24-bit block length cannot be written. About 12.6 MB of image bytes is enough.
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "T");
		var mock = new MockFileSystem ();
		const string path = "/album.oga";
		mock.AddFile (path, original);

		var read = OggFlacFile.ReadFromFile (path, mock);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var tag = read.File!.VorbisComment!;
		var commentWithoutPicture = tag.Render ().Length;
		var blockOverhead = MeasuredPictureBlockOverhead (tag);
		tag.RemoveAllPictures ();

		var pictureBytes = PictureBytesPast24BitLimit (commentWithoutPicture, blockOverhead);
		var contentLength = blockOverhead + pictureBytes;
		var base64Length = 4 * ((contentLength + 2) / 3);
		var commentLength = commentWithoutPicture + 4 + "METADATA_BLOCK_PICTURE=".Length + base64Length;
		Assert.IsGreaterThan (0xFFFFFF, commentLength, "Picture did not expand the comment past 24 bits");
		Assert.IsLessThanOrEqualTo (0xFFFFFF + 4, commentLength, "Picture is larger than the one overflow case");

		tag.AddPicture (new FlacPicture (
			"image/jpeg", PictureType.FrontCover, "", BinaryData.WrapUnsafe (new byte[pictureBytes]), 0, 0, 0, 0));

		var save = read.File.SaveToFile (path, mock);
		if (save.IsSuccess) {
			Assert.Fail (
				$"SaveToFile returned success for a Vorbis comment of {commentLength} bytes. " +
				$"A 24-bit length would wrap to {commentLength & 0xFFFFFF}.");
		}

		Assert.IsTrue (save.Error!.Contains ("render", StringComparison.OrdinalIgnoreCase), save.Error);
		CollectionAssert.AreEqual (original, mock.ReadAllBytes (path));
	}

	[TestMethod]
	public async Task SaveToFileAsync_CommentBlockPast24Bits_LeavesFileUnchanged ()
	{
		// Same overflow as the sync save: a base64 picture pushes the Vorbis comment past 0xFFFFFF bytes.
		var original = TestBuilders.OggFlac.CreateWithMetadata (title: "T");
		var mock = new MockFileSystem ();
		const string path = "/album.oga";
		mock.AddFile (path, original);

		var read = OggFlacFile.ReadFromFile (path, mock);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var tag = read.File!.VorbisComment!;
		var commentWithoutPicture = tag.Render ().Length;
		var blockOverhead = MeasuredPictureBlockOverhead (tag);
		tag.RemoveAllPictures ();

		var pictureBytes = PictureBytesPast24BitLimit (commentWithoutPicture, blockOverhead);
		var contentLength = blockOverhead + pictureBytes;
		var base64Length = 4 * ((contentLength + 2) / 3);
		var commentLength = commentWithoutPicture + 4 + "METADATA_BLOCK_PICTURE=".Length + base64Length;
		Assert.IsGreaterThan (0xFFFFFF, commentLength, "Picture did not expand the comment past 24 bits");
		Assert.IsLessThanOrEqualTo (0xFFFFFF + 4, commentLength, "Picture is larger than the one overflow case");

		tag.AddPicture (new FlacPicture (
			"image/jpeg", PictureType.FrontCover, "", BinaryData.WrapUnsafe (new byte[pictureBytes]), 0, 0, 0, 0));

		var save = await read.File.SaveToFileAsync (path, mock);
		if (save.IsSuccess) {
			Assert.Fail (
				$"SaveToFileAsync returned success for a Vorbis comment of {commentLength} bytes. " +
				$"A 24-bit length would wrap to {commentLength & 0xFFFFFF}.");
		}

		Assert.IsFalse (save.IsSuccess, save.Error);
		Assert.IsTrue (save.Error!.Contains ("render", StringComparison.OrdinalIgnoreCase), save.Error);
		CollectionAssert.AreEqual (original, mock.ReadAllBytes (path));
	}

	[TestMethod]
	public void LegalMaximumMetadataBlock_IsAccepted ()
	{
		const int dataLength = 0xFFFFFF;
		var packet = new byte[4 + dataLength];
		packet[0] = 0x81;
		packet[1] = 0xFF;
		packet[2] = 0xFF;
		packet[3] = 0xFF;
		packet[4] = 0xA5;
		packet[^1] = 0x5A;

		var read = OggFlacFile.Read (BuildPackets (CreateHeaderPacket (1), packet));
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsTrue (read.File!.TryGetMetadataBlock (1, out var stored), read.Error);
		Assert.AreEqual (dataLength, stored.Length);
		Assert.AreEqual (0xA5, stored[0]);
		Assert.AreEqual (0x5A, stored[^1]);
	}

	[TestMethod]
	public void MetadataPacketLargerThanMaximumBlock_IsRejected ()
	{
		// One byte past 4 + 0xFFFFFF. The declared length still says 0xFFFFFF; the packet does not fit.
		const int dataLength = 0xFFFFFF;
		var packet = new byte[4 + dataLength + 1];
		packet[0] = 0x81;
		packet[1] = 0xFF;
		packet[2] = 0xFF;
		packet[3] = 0xFF;
		packet[4] = 0xA5;
		packet[^1] = 0x5A;

		var read = OggFlacFile.Read (BuildPackets (CreateHeaderPacket (1), packet));
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsFalse (
			read.File!.TryGetMetadataBlock (1, out _),
			"A packet larger than 4 + 0xFFFFFF is not a legal metadata block");
	}

	[TestMethod]
	public void WalkFailureAfterCompleteComment_KeepsCommentAndBlocks ()
	{
		var comment = new VorbisComment ("TagLibSharp2") { Title = "Survives" };
		var commentPacket = WrapFlacBlock (0x04, comment.Render ().ToArray ());
		var picture = new byte[] { 1, 2, 3, 4 };
		var picturePacket = WrapFlacBlock (0x06, picture);
		var header = OggPageHelper.BuildOggPage (
			[CreateHeaderPacket (2)], OggPageFlags.BeginOfStream, 0, serialNumber: 1, sequenceNumber: 0);
		var metadata = OggPageHelper.BuildOggPage (
			[commentPacket, picturePacket], OggPageFlags.None, 0, serialNumber: 1, sequenceNumber: 1);
		var broken = OggPageHelper.BuildOggPage (
			[new byte[] { 0x01 }], OggPageFlags.Continuation, 0, serialNumber: 1, sequenceNumber: 2);

		var fileBytes = Concat (header, metadata, broken);
		var read = OggFlacFile.Read (fileBytes);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.AreEqual ("Survives", read.File!.VorbisComment?.Title);
		Assert.IsTrue (read.File.TryGetMetadataBlock (6, out var stored));
		CollectionAssert.AreEqual (picture, stored);
		Assert.AreEqual (0, read.File.Render (fileBytes).Length, "A failed walk is not a file that can be rewritten");
	}

	[TestMethod]
	public void SharedMetadataAndAudioPage_KeepsCommentButDoesNotParseAudio ()
	{
		// RFC 9639 section 10.1: the first audio packet must start on a new page.
		var comment = new VorbisComment ("TagLibSharp2") { Title = "Kept" };
		var commentPacket = WrapFlacBlock (0x04, comment.Render ().ToArray ());
		var audio = new byte[] { 0xFF, 0xF8, 0xC9, 0x12, 0x00 };
		var header = OggPageHelper.BuildOggPage (
			[CreateHeaderPacket (1)], OggPageFlags.BeginOfStream, 0, serialNumber: 1, sequenceNumber: 0);
		var shared = OggPageHelper.BuildOggPage (
			[commentPacket, audio], OggPageFlags.None, 0, serialNumber: 1, sequenceNumber: 1);
		var fileBytes = Concat (header, shared);

		var mock = new MockFileSystem ();
		const string path = "/shared.oga";
		mock.AddFile (path, fileBytes);
		var read = OggFlacFile.ReadFromFile (path, mock);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.AreEqual ("Kept", read.File!.VorbisComment?.Title);

		read.File.VorbisComment!.Title = "Changed";
		var save = read.File.SaveToFile (path, mock);
		Assert.IsFalse (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (fileBytes, mock.ReadAllBytes (path));
	}

	[TestMethod]
	public void WalkFailureBeforeAnyComment_StillSucceedsWithNoComment ()
	{
		// The comment packet never reaches its terminating lacing value, so there is no comment
		// to keep. The identification header is intact and the read may still succeed.
		var header = OggPageHelper.BuildOggPage (
			[CreateHeaderPacket (1)], OggPageFlags.BeginOfStream, 0, serialNumber: 1, sequenceNumber: 0);
		var unfinished = new byte[70000];
		unfinished[0] = 0x04;
		var (pages, _) = OggPageHelper.BuildMultiPagePacket (unfinished, OggPageFlags.None, 0, serialNumber: 1, startSequence: 1);
		Assert.AreEqual (255, pages[0][26], "Comment page must be a full page, not a finished comment");

		var read = OggFlacFile.Read (Concat (header, pages[0]));
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNull (
			read.File!.VorbisComment,
			"A walk that fails before any comment packet is complete may still succeed, with no comment");
	}

	[TestMethod]
	public void PageBudgetBeforeAudio_SaveLeavesFileUnchanged ()
	{
		// RFC 9639 section 10.1: header packets finish before the first audio page.
		// Stopping at the page budget with a finished packet is not that audio page.
		const int metadataPages = 10_000;
		var original = CreateMetadataPagesThenAudio (metadataPages, lastMarker: 0x5A);
		var mock = new MockFileSystem ();
		const string path = "/budget.oga";
		mock.AddFile (path, original);

		var read = OggFlacFile.ReadFromFile (path, mock);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.EnsureVorbisComment ().Title = "Changed";

		var save = read.File.SaveToFile (path, mock);
		Assert.IsFalse (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (original, mock.ReadAllBytes (path));
	}

	[TestMethod]
	public void CommentAndMaxPicture_UnderPageBudget_RoundTrips ()
	{
		// 0xFFFFFF is the largest FLAC metadata block (RFC 9639 section 8.1). With a comment
		// page it is more than 260 Ogg pages and still under the FLAC page budget.
		var picture = new byte[0xFFFFFF];
		picture[0] = 0x11;
		picture[65021] = 0xFF;
		picture[^1] = 0x22;
		AssertPictureRoundTrip (CreateOggFlacWithPicture (picture), picture, second: null);
	}

	[TestMethod]
	public void TwoLargePictures_UnderPageBudget_RoundTrip ()
	{
		// Short pages keep the file small while the page count stays over the Vorbis cap
		// and under the FLAC budget. Each picture is its own continued packet.
		var first = RepeatedPicture (pages: 200, marker: 0x31);
		var second = RepeatedPicture (pages: 200, marker: 0x32);
		var original = CreateOggFlacWithShortPictures (first, second);
		AssertPictureRoundTrip (original, first, second);
	}

	[TestMethod]
	public void ManyMetadataBlocks_UnderPageBudget_RoundTrip ()
	{
		// One block per page, one short of the budget, then the audio page. The last block
		// is still metadata, so a walk that stops early must not report success.
		const int metadataPages = 9_999;
		var original = CreateMetadataPagesThenAudio (metadataPages, lastMarker: 0x77);
		var read = OggFlacFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsTrue (read.File!.TryGetMetadataBlock (6, out var stored), read.Error);
		Assert.AreEqual (0x77, stored[^1]);

		read.File.EnsureVorbisComment ().Title = "Kept";
		var rendered = read.File.Render (original);
		Assert.IsGreaterThan (0, rendered.Length, "A metadata stream under the page budget was not written");

		var again = OggFlacFile.Read (rendered);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Kept", again.File!.VorbisComment?.Title);
		Assert.IsTrue (again.File.TryGetMetadataBlock (6, out var reread), again.Error);
		Assert.AreEqual (0x77, reread[^1]);
	}

	[TestMethod]
	public void AudioPacket_DoesNotBeginAFreshPage_SaveLeavesFileUnchanged ()
	{
		// RFC 9639 section 10.1: the first audio packet begins a fresh page. A continued
		// packet is not that page, and neither is a later packet on a metadata page.
		AssertSaveLeavesBytesUnchanged (ContinuedMetadataThenAudioOnSamePage (), "/continued-audio.oga");
		AssertSaveLeavesBytesUnchanged (MetadataAndAudioOnSamePage (), "/shared-audio.oga");
	}

	static void AssertPictureRoundTrip (byte[] original, byte[] picture, byte[]? second)
	{
		var read = OggFlacFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.EnsureVorbisComment ().Title = "Changed Title";

		var rendered = read.File.Render (original);
		Assert.IsGreaterThan (0, rendered.Length, "Render dropped a picture that fits in the FLAC page budget");

		var again = OggFlacFile.Read (rendered);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Changed Title", again.File!.VorbisComment?.Title);
		Assert.IsTrue (again.File.TryGetMetadataBlock (6, out var stored));
		CollectionAssert.AreEqual (picture, stored);
		if (second is not null) {
			var pictures = again.File.MetadataBlockPayloads (6);
			Assert.AreEqual (2, pictures.Count);
			CollectionAssert.AreEqual (second, pictures[1]);
		}
	}

	static void AssertSaveLeavesBytesUnchanged (byte[] original, string path)
	{
		var mock = new MockFileSystem ();
		mock.AddFile (path, original);
		var read = OggFlacFile.ReadFromFile (path, mock);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.EnsureVorbisComment ().Title = "Changed";

		var save = read.File.SaveToFile (path, mock);
		Assert.IsFalse (save.IsSuccess, save.Error);
		CollectionAssert.AreEqual (original, mock.ReadAllBytes (path));
	}

	static byte[] ContinuedMetadataThenAudioOnSamePage ()
	{
		var comment = new byte[256];
		comment[0] = 0x04;
		comment[3] = 252;
		// Four bytes so the 24-bit length is 0. A longer frame would be rejected as a
		// metadata block even after the fresh-page check is removed.
		var audio = new byte[] { 0xFF, 0x00, 0x00, 0x00 };

		using var output = new MemoryStream ();
		uint sequence = 0;
		WritePages (output, CreateHeaderPacket (1), OggPageFlags.BeginOfStream, ref sequence);

		var first = new byte[255];
		Array.Copy (comment, first, 255);
		WriteOnePage (output, first, [255], OggPageFlags.None, ulong.MaxValue, sequence++);

		var rest = new byte[1 + audio.Length];
		rest[0] = comment[255];
		audio.CopyTo (rest, 1);
		WriteOnePage (output, rest, [1, (byte)audio.Length], OggPageFlags.Continuation, 0, sequence++);
		return output.ToArray ();
	}

	static byte[] MetadataAndAudioOnSamePage ()
	{
		var comment = new VorbisComment ("TagLibSharp2") { Title = "Kept" };
		var commentPacket = WrapFlacBlock (0x04, comment.Render ().ToArray ());
		var audio = new byte[] { 0xFF, 0x00, 0x00, 0x00 };
		var header = OggPageHelper.BuildOggPage (
			[CreateHeaderPacket (1)], OggPageFlags.BeginOfStream, 0, serialNumber: 1, sequenceNumber: 0);
		var shared = OggPageHelper.BuildOggPage (
			[commentPacket, audio], OggPageFlags.None, 0, serialNumber: 1, sequenceNumber: 1);
		return Concat (header, shared);
	}

	static byte[] CreateMetadataPagesThenAudio (int metadataPages, byte lastMarker)
	{
		using var output = new MemoryStream ();
		uint sequence = 0;
		WritePages (output, CreateHeaderPacket (1), OggPageFlags.BeginOfStream, ref sequence);
		for (var i = 1; i < metadataPages - 1; i++)
			WritePages (output, WrapFlacBlock (0x01, [(byte)(i & 0xFF)]), OggPageFlags.None, ref sequence);

		var picture = new byte[] { 0x10, lastMarker };
		WritePages (output, WrapFlacBlock (0x06, picture), OggPageFlags.None, ref sequence);
		WritePages (output, [0xFF, 0xF8, 0xC9, 0x12, 0x00], OggPageFlags.EndOfStream, ref sequence);
		return output.ToArray ();
	}

	static byte[] RepeatedPicture (int pages, byte marker)
	{
		var picture = new byte[((pages - 1) * 255) + 10];
		picture[0] = marker;
		picture[^1] = marker;
		return picture;
	}

	static byte[] CreateOggFlacWithShortPictures (byte[] first, byte[] second)
	{
		var comment = new VorbisComment ("TagLibSharp2") { Title = "Original Title" };
		var commentPacket = WrapFlacBlock (0x04, comment.Render ().ToArray ());
		using var output = new MemoryStream ();
		uint sequence = 0;
		WritePages (output, CreateHeaderPacket (2), OggPageFlags.BeginOfStream, ref sequence);
		WritePages (output, commentPacket, OggPageFlags.None, ref sequence);
		WriteContinuedSegmentPages (output, WrapFlacBlock (0x06, first), OggPageFlags.None, ref sequence);
		WriteContinuedSegmentPages (output, WrapFlacBlock (0x86, second), OggPageFlags.None, ref sequence);
		WritePages (output, [0xFF, 0xF8, 0xC9, 0x12, 0x00], OggPageFlags.EndOfStream, ref sequence);
		return output.ToArray ();
	}

	static void WriteContinuedSegmentPages (Stream output, byte[] packet, OggPageFlags firstFlags, ref uint sequence)
	{
		var offset = 0;
		var first = true;
		while (offset < packet.Length) {
			var remaining = packet.Length - offset;
			var take = Math.Min (255, remaining);
			var finishes = take < 255;
			var payload = new byte[take];
			Array.Copy (packet, offset, payload, 0, take);
			var flags = first ? firstFlags : OggPageFlags.Continuation;
			WriteOnePage (output, payload, [(byte)take], flags, finishes ? 0 : ulong.MaxValue, sequence++);
			offset += take;
			first = false;
			if (finishes)
				break;
		}
	}

	static void AssertEndsOnZeroLacing (OggPacketAssembler.Packet packet, int packetLength)
	{
		Assert.AreEqual (packetLength, packet.Data.Length, "Measured packet size does not match the lacing boundary");
		const int pageBytes = 255 * 255;
		Assert.AreEqual (0, packetLength % pageBytes);
		var fullPages = packetLength / pageBytes;
		Assert.AreEqual (fullPages + 1, packet.Lacings.Count, "Missing the 0-lacing terminator page");
		Assert.AreEqual (0, packet.Flags[0] & (byte)OggPageFlags.Continuation);

		for (var i = 0; i < fullPages; i++) {
			Assert.AreEqual (255, packet.Lacings[i].Length);
			for (var segment = 0; segment < packet.Lacings[i].Length; segment++)
				Assert.AreEqual (255, packet.Lacings[i][segment]);
			Assert.AreEqual (ulong.MaxValue, packet.Granules[i], "Unfinished page must use granule -1");
			if (i > 0)
				Assert.AreEqual ((byte)OggPageFlags.Continuation, packet.Flags[i]);
		}

		Assert.AreEqual (1, packet.Lacings[^1].Length, "Terminator page has a single lacing value");
		Assert.AreEqual (0, packet.Lacings[^1][0], "Terminating lacing value is 0");
		Assert.AreEqual ((byte)OggPageFlags.Continuation, packet.Flags[^1]);
		Assert.AreEqual (0, packet.Flags[^1] & 0x06);
	}

	static int MeasuredPictureBlockOverhead (VorbisComment tag)
	{
		var before = tag.Render ().Length;
		var probe = new FlacPicture (
			"image/jpeg", PictureType.FrontCover, "", new BinaryData (new byte[] { 0x01 }), 0, 0, 0, 0);
		var blockLength = probe.RenderContent ().Length;
		tag.AddPicture (probe);
		var growth = tag.Render ().Length - before;
		var base64Length = 4 * ((blockLength + 2) / 3);
		Assert.AreEqual (4 + "METADATA_BLOCK_PICTURE=".Length + base64Length, growth,
			"Picture field overhead changed; measure it instead of guessing");
		return blockLength - 1;
	}

	static int PictureBytesPast24BitLimit (int commentWithoutPicture, int blockOverhead)
	{
		var fieldName = "METADATA_BLOCK_PICTURE=".Length;
		var minimumBase64 = 0xFFFFFF - commentWithoutPicture - 4 - fieldName + 1;
		var base64Length = (minimumBase64 + 3) & ~3;
		var groups = base64Length / 4;
		var content = (groups * 3) - 2;
		return content - blockOverhead;
	}

	static byte[] BuildPackets (params byte[][] packets)
	{
		using var output = new MemoryStream ();
		uint sequence = 0;
		for (var i = 0; i < packets.Length; i++) {
			var flags = i == 0 ? OggPageFlags.BeginOfStream : OggPageFlags.None;
			WritePages (output, packets[i], flags, ref sequence);
		}

		return output.ToArray ();
	}

	static byte[] Concat (params byte[][] parts)
	{
		var length = 0;
		for (var i = 0; i < parts.Length; i++)
			length += parts[i].Length;

		var result = new byte[length];
		var offset = 0;
		for (var i = 0; i < parts.Length; i++) {
			parts[i].CopyTo (result, offset);
			offset += parts[i].Length;
		}

		return result;
	}
}

static class OggPacketAssembler
{
	internal sealed class Packet
	{
		public byte[] Data { get; private set; } = [];
		public List<byte[]> Lacings { get; } = new ();
		public List<byte> Flags { get; } = new ();
		public List<ulong> Granules { get; } = new ();

		public void Finish (List<byte> data)
		{
			Data = data.ToArray ();
		}
	}

	public static List<Packet> Read (ReadOnlySpan<byte> file)
	{
		var packets = new List<Packet> ();
		var open = new List<byte> ();
		var lacings = new List<byte[]> ();
		var flags = new List<byte> ();
		var granules = new List<ulong> ();
		var offset = 0;

		while (offset + 27 <= file.Length &&
			   file[offset] == (byte)'O' && file[offset + 1] == (byte)'g' &&
			   file[offset + 2] == (byte)'g' && file[offset + 3] == (byte)'S') {
			var segmentCount = file[offset + 26];
			var tableEnd = offset + 27 + segmentCount;
			if (tableEnd > file.Length)
				break;

			var dataSize = 0;
			for (var i = 0; i < segmentCount; i++)
				dataSize += file[offset + 27 + i];
			if (tableEnd + dataSize > file.Length)
				break;

			var pageFlags = file[offset + 5];
			ulong granule = 0;
			for (var i = 0; i < 8; i++)
				granule |= (ulong)file[offset + 6 + i] << (8 * i);

			var continued = (pageFlags & (byte)OggPageFlags.Continuation) != 0;
			if (!continued && (open.Count > 0 || lacings.Count > 0))
				packets.Add (Finish (open, lacings, flags, granules));

			var lace = new byte[segmentCount];
			file.Slice (offset + 27, segmentCount).CopyTo (lace);
			lacings.Add (lace);
			flags.Add (pageFlags);
			granules.Add (granule);

			var dataOffset = tableEnd;
			for (var i = 0; i < segmentCount; i++) {
				var size = lace[i];
				if (size > 0) {
					open.AddRange (file.Slice (dataOffset, size).ToArray ());
					dataOffset += size;
				}

				if (size < 255)
					packets.Add (Finish (open, lacings, flags, granules));
			}

			offset = tableEnd + dataSize;
		}

		if (open.Count > 0 || lacings.Count > 0)
			packets.Add (Finish (open, lacings, flags, granules));

		return packets;
	}

	static Packet Finish (List<byte> data, List<byte[]> lacings, List<byte> flags, List<ulong> granules)
	{
		var packet = new Packet ();
		packet.Finish (data);
		packet.Lacings.AddRange (lacings);
		packet.Flags.AddRange (flags);
		packet.Granules.AddRange (granules);
		data.Clear ();
		lacings.Clear ();
		flags.Clear ();
		granules.Clear ();
		return packet;
	}
}
