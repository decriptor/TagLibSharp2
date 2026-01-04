// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// Tests that verify ASF/WMA data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class AsfFileRoundTripTests
{
	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = AsfTestBuilder.CreateMinimalWma (title: "Test Title", artist: "Test Artist");

		// Act
		var result1 = AsfFile.Read (original);
		Assert.IsTrue (result1.IsSuccess, $"First parse failed: {result1.Error}");
		var file1 = result1.File!;

		var written = file1.Render (original);
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var result2 = AsfFile.Read (written);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Test Title", file2.Tag.Title);
		Assert.AreEqual ("Test Artist", file2.Tag.Artist);
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = AsfTestBuilder.CreateMinimalWma (title: "Original");

		var result = AsfFile.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;
		var tag = file.Tag;

		// Set all standard metadata fields
		tag.Title = "Round Trip Title";
		tag.Artist = "Round Trip Artist";
		tag.Album = "Round Trip Album";
		tag.Year = "2025";
		tag.Genre = "Rock";
		tag.Track = 5;
		tag.TotalTracks = 12;
		tag.DiscNumber = 1;
		tag.Composer = "Composer Name";
		tag.Comment = "This is a comment";

		// Act
		var written = file.Render (original);
		var result2 = AsfFile.Read (written);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var reloaded = result2.File!.Tag;

		// Assert
		Assert.AreEqual ("Round Trip Title", reloaded.Title);
		Assert.AreEqual ("Round Trip Artist", reloaded.Artist);
		Assert.AreEqual ("Round Trip Album", reloaded.Album);
		Assert.AreEqual ("2025", reloaded.Year);
		Assert.AreEqual ("Rock", reloaded.Genre);
		Assert.AreEqual (5u, reloaded.Track);
		Assert.AreEqual (12u, reloaded.TotalTracks);
		Assert.AreEqual (1u, reloaded.DiscNumber);
		Assert.AreEqual ("Composer Name", reloaded.Composer);
		Assert.AreEqual ("This is a comment", reloaded.Comment);
	}

	[TestMethod]
	public void MusicBrainzIds_SurviveRoundTrip ()
	{
		// Arrange
		var original = AsfTestBuilder.CreateMinimalWma (title: "With MB IDs");
		var result = AsfFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.Tag;

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		tag.MusicBrainzTrackId = mbTrackId;
		tag.MusicBrainzReleaseId = mbReleaseId;

		// Act
		var written = file.Render (original);
		var result2 = AsfFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.Tag;

		// Assert
		Assert.AreEqual (mbTrackId, reloaded.MusicBrainzTrackId);
		Assert.AreEqual (mbReleaseId, reloaded.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void ReplayGain_SurvivesRoundTrip ()
	{
		// Arrange
		var original = AsfTestBuilder.CreateMinimalWma (title: "With ReplayGain");
		var result = AsfFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.Tag;

		tag.ReplayGainTrackGain = "-6.50 dB";
		tag.ReplayGainTrackPeak = "0.988547";
		tag.ReplayGainAlbumGain = "-7.25 dB";
		tag.ReplayGainAlbumPeak = "1.0";

		// Act
		var written = file.Render (original);
		var result2 = AsfFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.Tag;

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
		var original = AsfTestBuilder.CreateMinimalWma (title: "Original", sampleRate: 44100);
		var result = AsfFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Store original audio properties
		var originalSampleRate = file.Properties.SampleRate;
		var originalChannels = file.Properties.Channels;

		// Modify metadata
		file.Tag.Title = "Modified Title";

		// Act
		var written = file.Render (original);
		var result2 = AsfFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert: Audio properties unchanged
		Assert.AreEqual (originalSampleRate, file2.Properties.SampleRate);
		Assert.AreEqual (originalChannels, file2.Properties.Channels);
		Assert.AreEqual ("Modified Title", file2.Tag.Title);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = AsfTestBuilder.CreateMinimalWma (title: "Title", artist: "Artist");

		// Act: Write multiple times
		var result1 = AsfFile.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		var written1 = file1.Render (original);
		var size1 = written1.Length;

		var result2 = AsfFile.Read (written1);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		var written2 = file2.Render (written1);
		var size2 = written2.Length;

		var result3 = AsfFile.Read (written2);
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
	public void Unicode_AllScripts ()
	{
		// Arrange
		var original = AsfTestBuilder.CreateMinimalWma (title: "Original");
		var result = AsfFile.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;
		var tag = file.Tag;

		// Set metadata with various Unicode characters
		tag.Title = "日本語タイトル";
		tag.Artist = "Café Français";
		tag.Album = "Album Name";
		tag.Comment = "Unicode test";

		// Act
		var written = file.Render (original);
		var result2 = AsfFile.Read (written);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!.Tag;

		// Assert
		Assert.AreEqual ("日本語タイトル", reloaded.Title);
		Assert.AreEqual ("Café Français", reloaded.Artist);
		Assert.AreEqual ("Album Name", reloaded.Album);
		Assert.AreEqual ("Unicode test", reloaded.Comment);
	}
}
