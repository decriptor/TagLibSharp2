// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

/// <summary>
/// Tests that verify Ogg Vorbis data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class OggVorbisFileRoundTripTests
{
	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "Test Title", artist: "Test Artist");

		// Act
		var result1 = OggVorbisFile.Read (original);
		Assert.IsTrue (result1.IsSuccess, $"First parse failed: {result1.Error}");
		var file1 = result1.File!;

		var written = file1.Render (original);
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var result2 = OggVorbisFile.Read (written.Span);
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
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "Original");

		var result = OggVorbisFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;
		var tag = file.VorbisComment!;

		// Set all standard metadata fields
		tag.Title = "Round Trip Title";
		tag.Artist = "Round Trip Artist";
		tag.Album = "Round Trip Album";
		tag.Year = "2025";
		tag.Genre = "Rock";
		tag.Track = 5;
		tag.TotalTracks = 12;
		tag.DiscNumber = 1;
		tag.TotalDiscs = 2;
		tag.Composer = "Composer Name";
		tag.Comment = "This is a comment";

		// Act
		var written = file.Render (original);
		var result2 = OggVorbisFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var reloaded = result2.File!.VorbisComment!;

		// Assert
		Assert.AreEqual ("Round Trip Title", reloaded.Title);
		Assert.AreEqual ("Round Trip Artist", reloaded.Artist);
		Assert.AreEqual ("Round Trip Album", reloaded.Album);
		Assert.AreEqual ("2025", reloaded.Year);
		Assert.AreEqual ("Rock", reloaded.Genre);
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

		var original = TestBuilders.Ogg.CreateMinimalFile (title: "With Art");
		var result = OggVorbisFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "Cover",
			new BinaryData (jpegData), 100, 100, 24, 0);
		file.VorbisComment!.AddPicture (picture);

		// Act
		var written = file.Render (original);
		var result2 = OggVorbisFile.Read (written.Span);
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
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "With MB IDs");
		var result = OggVorbisFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.VorbisComment!;

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		tag.MusicBrainzTrackId = mbTrackId;
		tag.MusicBrainzReleaseId = mbReleaseId;

		// Act
		var written = file.Render (original);
		var result2 = OggVorbisFile.Read (written.Span);
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
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "With ReplayGain");
		var result = OggVorbisFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.VorbisComment!;

		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.25 dB";
		tag.ReplayGainAlbumPeak = "1.0";

		// Act
		var written = file.Render (original);
		var result2 = OggVorbisFile.Read (written.Span);
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
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "Original");
		var result = OggVorbisFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Store original audio properties
		var originalSampleRate = file.Properties.SampleRate;
		var originalChannels = file.Properties.Channels;

		// Modify metadata
		file.VorbisComment!.Title = "Modified Title";

		// Act
		var written = file.Render (original);
		var result2 = OggVorbisFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert: Audio properties unchanged
		Assert.AreEqual (originalSampleRate, file2.Properties.SampleRate);
		Assert.AreEqual (originalChannels, file2.Properties.Channels);
		Assert.AreEqual ("Modified Title", file2.VorbisComment?.Title);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "Title", artist: "Artist");

		// Act: Write multiple times
		var result1 = OggVorbisFile.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		var written1 = file1.Render (original);
		var size1 = written1.Length;

		var result2 = OggVorbisFile.Read (written1.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		var written2 = file2.Render (written1.Span);
		var size2 = written2.Length;

		var result3 = OggVorbisFile.Read (written2.Span);
		Assert.IsTrue (result3.IsSuccess);
		var file3 = result3.File!;
		var written3 = file3.Render (written2.Span);
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
	public void Unicode_AllScripts ()
	{
		// Arrange
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "Original");
		var result = OggVorbisFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.VorbisComment!;

		// Set metadata with various Unicode characters
		tag.Title = "Titulo";
		tag.Artist = "Artist Name";
		tag.Album = "Album Name";
		tag.Comment = "Unicode test";

		// Act
		var written = file.Render (original);
		var result2 = OggVorbisFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.VorbisComment!;

		// Assert
		Assert.AreEqual ("Titulo", reloaded.Title);
		Assert.AreEqual ("Artist Name", reloaded.Artist);
		Assert.AreEqual ("Album Name", reloaded.Album);
		Assert.AreEqual ("Unicode test", reloaded.Comment);
	}

	[TestMethod]
	public void CommentPacket_Exact65025Bytes_RoundTripsWithZeroLacing ()
	{
		// A comment packet of exactly 255 * 255 bytes ends with a lacing value of 0 on the next page.
		// Dropping that terminator concatenates the setup header or leaves the comment short.
		const int targetPacket = 255 * 255;
		var original = TestBuilders.Ogg.CreateMinimalFile (title: "Boundary", artist: "Artist");
		var read = OggVorbisFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var tag = read.File!.VorbisComment!;

		var packetBefore = 8 + tag.Render ().Length;
		const string fieldName = "COMMENT";
		var valueLength = targetPacket - packetBefore - (4 + fieldName.Length + 1);
		Assert.IsGreaterThan (0, valueLength);
		var value = new string ('Q', valueLength);
		tag.Comment = value;
		Assert.AreEqual (targetPacket, 8 + tag.Render ().Length, "Failed to size the comment packet to 65025 bytes");

		var written = read.File.Render (original);
		Assert.IsGreaterThan (0, written.Length);
		var commentPacket = OggPacketAssembler.Read (written.Span)
			.Single (packet => packet.Data.Length > 7 && packet.Data[0] == 3 && packet.Data[1] == (byte)'v');
		Assert.AreEqual (targetPacket, commentPacket.Data.Length);
		Assert.AreEqual (2, commentPacket.Lacings.Count, "65025-byte packet is one full page plus a terminator");
		Assert.AreEqual (255, commentPacket.Lacings[0].Length);
		for (var segment = 0; segment < commentPacket.Lacings[0].Length; segment++)
			Assert.AreEqual (255, commentPacket.Lacings[0][segment]);
		Assert.AreEqual (ulong.MaxValue, commentPacket.Granules[0]);
		Assert.AreEqual (0, commentPacket.Flags[0] & (byte)OggPageFlags.Continuation);
		Assert.AreEqual (1, commentPacket.Lacings[1].Length);
		Assert.AreEqual (0, commentPacket.Lacings[1][0], "Terminating lacing value is 0");
		Assert.AreEqual ((byte)OggPageFlags.Continuation, commentPacket.Flags[1]);
		Assert.AreEqual (0, commentPacket.Flags[1] & 0x06);

		var again = OggVorbisFile.Read (written.Span);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual (value, again.File!.VorbisComment!.Comment);
	}
}
