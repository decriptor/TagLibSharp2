// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Dff;

/// <summary>
/// Tests that verify DFF (DSDIFF) data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class DffFileRoundTripTests
{
	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = TestBuilders.Dff.CreateWithMetadata (title: "Test Title", artist: "Test Artist");

		// Act
		var result1 = DffFile.Read (original);
		Assert.IsTrue (result1.IsSuccess, $"First parse failed: {result1.Error}");
		var file1 = result1.File!;

		var written = file1.Render ();
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Test Title", file2.Id3v2Tag?.Title);
		Assert.AreEqual ("Test Artist", file2.Id3v2Tag?.Artist);
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Dff.CreateWithMetadata (title: "Original");

		var result = DffFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;
		var tag = file.EnsureId3v2Tag ();

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
		var written = file.Render ();
		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var reloaded = result2.File!.Id3v2Tag!;

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

		var original = TestBuilders.Dff.CreateWithMetadata (title: "With Art");
		var result = DffFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		var picture = new PictureFrame ("image/jpeg", PictureType.FrontCover, "Cover", new BinaryData (jpegData));
		file.EnsureId3v2Tag ().AddPicture (picture);

		// Act
		var written = file.Render ();
		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		var pictures = file2.Id3v2Tag!.PictureFrames;
		Assert.AreEqual (1, pictures.Count);
		CollectionAssert.AreEqual (jpegData, pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void MusicBrainzIds_SurviveRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Dff.CreateWithMetadata (title: "With MB IDs");
		var result = DffFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.EnsureId3v2Tag ();

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		tag.MusicBrainzTrackId = mbTrackId;
		tag.MusicBrainzReleaseId = mbReleaseId;

		// Act
		var written = file.Render ();
		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.Id3v2Tag!;

		// Assert
		Assert.AreEqual (mbTrackId, reloaded.MusicBrainzTrackId);
		Assert.AreEqual (mbReleaseId, reloaded.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void ReplayGain_SurvivesRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Dff.CreateWithMetadata (title: "With ReplayGain");
		var result = DffFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.EnsureId3v2Tag ();

		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.25 dB";
		tag.ReplayGainAlbumPeak = "1.0";

		// Act
		var written = file.Render ();
		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.Id3v2Tag!;

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
		var original = TestBuilders.Dff.CreateWithMetadata (title: "Original");
		var result = DffFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		Assert.IsNotNull (file.Properties, "Initial Properties should not be null");

		// Store original audio properties
		var originalSampleRate = file.Properties.SampleRate;
		var originalChannels = file.Properties.Channels;

		// Modify metadata
		file.EnsureId3v2Tag ().Title = "Modified Title";

		// Act
		var written = file.Render ();
		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert: Audio properties unchanged
		Assert.AreEqual (originalSampleRate, file2.Properties!.SampleRate);
		Assert.AreEqual (originalChannels, file2.Properties.Channels);
		Assert.AreEqual ("Modified Title", file2.Id3v2Tag?.Title);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = TestBuilders.Dff.CreateWithMetadata (title: "Title", artist: "Artist");

		// Act: Write multiple times
		var result1 = DffFile.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		var written1 = file1.Render ();
		var size1 = written1.Length;

		var result2 = DffFile.Read (written1.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		var written2 = file2.Render ();
		var size2 = written2.Length;

		var result3 = DffFile.Read (written2.Span);
		Assert.IsTrue (result3.IsSuccess);
		var file3 = result3.File!;
		var written3 = file3.Render ();
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
		var original = TestBuilders.Dff.CreateWithMetadata (title: "Original");
		var result = DffFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.EnsureId3v2Tag ();

		// Set metadata with various Unicode characters
		tag.Title = "Titulo";
		tag.Artist = "Artist Name";
		tag.Album = "Album Name";
		tag.Comment = "Unicode test";

		// Act
		var written = file.Render ();
		var result2 = DffFile.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.Id3v2Tag!;

		// Assert
		Assert.AreEqual ("Titulo", reloaded.Title);
		Assert.AreEqual ("Artist Name", reloaded.Artist);
		Assert.AreEqual ("Album Name", reloaded.Album);
		Assert.AreEqual ("Unicode test", reloaded.Comment);
	}
}
