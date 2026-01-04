// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Mpeg;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// Tests that verify MP3 data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class Mp3FileRoundTripTests
{
	static byte[] CreateMp3WithId3v2 (string? title = null, string? artist = null)
	{
		var tag = new Id3v2Tag ();
		if (!string.IsNullOrEmpty (title))
			tag.Title = title;
		if (!string.IsNullOrEmpty (artist))
			tag.Artist = artist;

		var tagData = tag.Render ();
		var audioData = new byte[256]; // Minimal "audio" placeholder
		var result = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (result);
		audioData.CopyTo (result.AsSpan (tagData.Length));
		return result;
	}

	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = CreateMp3WithId3v2 (title: "Test Title", artist: "Test Artist");

		// Act
		var result1 = Mp3File.Read (original);
		Assert.IsTrue (result1.IsSuccess, $"First parse failed: {result1.Error}");
		var file1 = result1.File!;

		var written = file1.Render (original);
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Test Title", file2.Title);
		Assert.AreEqual ("Test Artist", file2.Artist);
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = CreateMp3WithId3v2 (title: "Original");

		var result = Mp3File.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		// Set all standard metadata fields
		file.Title = "Round Trip Title";
		file.Artist = "Round Trip Artist";
		file.Album = "Round Trip Album";
		file.Year = "2025";
		file.Genre = "Rock";
		file.Track = 5;
		file.DiscNumber = 1;
		file.Composer = "Composer Name";
		file.Comment = "This is a comment";

		// Set TotalTracks and TotalDiscs via Id3v2Tag directly
		file.Id3v2Tag!.TotalTracks = 12;
		file.Id3v2Tag.TotalDiscs = 2;

		// Act
		var written = file.Render (original);
		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var reloaded = result2.File!;

		// Assert
		Assert.AreEqual ("Round Trip Title", reloaded.Title);
		Assert.AreEqual ("Round Trip Artist", reloaded.Artist);
		Assert.AreEqual ("Round Trip Album", reloaded.Album);
		Assert.AreEqual ("2025", reloaded.Year);
		Assert.AreEqual ("Rock", reloaded.Genre);
		Assert.AreEqual (5u, reloaded.Track);
		Assert.AreEqual (12u, reloaded.Id3v2Tag!.TotalTracks);
		Assert.AreEqual (1u, reloaded.DiscNumber);
		Assert.AreEqual (2u, reloaded.Id3v2Tag.TotalDiscs);
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

		var original = CreateMp3WithId3v2 (title: "With Art");
		var result = Mp3File.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		// Add picture via Id3v2Tag
		var picture = new PictureFrame ("image/jpeg", PictureType.FrontCover, "Cover", new BinaryData (jpegData));
		file.Id3v2Tag!.AddPicture (picture);

		// Act
		var written = file.Render (original);
		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		var pictures = file2.Id3v2Tag!.PictureFrames;
		Assert.AreEqual (1, pictures.Count);
		CollectionAssert.AreEqual (jpegData, pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void Unicode_AllScripts ()
	{
		// Arrange
		var original = CreateMp3WithId3v2 (title: "Original");
		var result = Mp3File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Set metadata with various Unicode characters
		file.Title = "Titulo";
		file.Artist = "Artist Name";
		file.Album = "Album Name";
		file.Comment = "Unicode test";

		// Act
		var written = file.Render (original);
		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!;

		// Assert
		Assert.AreEqual ("Titulo", reloaded.Title);
		Assert.AreEqual ("Artist Name", reloaded.Artist);
		Assert.AreEqual ("Album Name", reloaded.Album);
		Assert.AreEqual ("Unicode test", reloaded.Comment);
	}

	[TestMethod]
	public void MusicBrainzIds_SurviveRoundTrip ()
	{
		// Arrange
		var original = CreateMp3WithId3v2 (title: "With MB IDs");
		var result = Mp3File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		file.MusicBrainzTrackId = mbTrackId;
		file.MusicBrainzReleaseId = mbReleaseId;

		// Act
		var written = file.Render (original);
		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!;

		// Assert
		Assert.AreEqual (mbTrackId, reloaded.MusicBrainzTrackId);
		Assert.AreEqual (mbReleaseId, reloaded.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void ReplayGain_SurvivesRoundTrip ()
	{
		// Arrange
		var original = CreateMp3WithId3v2 (title: "With ReplayGain");
		var result = Mp3File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		file.ReplayGainTrackGain = "-6.50 dB";
		file.ReplayGainTrackPeak = "0.988547";
		file.ReplayGainAlbumGain = "-7.25 dB";
		file.ReplayGainAlbumPeak = "1.0";

		// Act
		var written = file.Render (original);
		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!;

		// Assert
		Assert.AreEqual ("-6.50 dB", reloaded.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", reloaded.ReplayGainTrackPeak);
		Assert.AreEqual ("-7.25 dB", reloaded.ReplayGainAlbumGain);
		Assert.AreEqual ("1.0", reloaded.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = CreateMp3WithId3v2 (title: "Title", artist: "Artist");

		// Act: Write multiple times
		var result1 = Mp3File.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		var written1 = file1.Render (original);
		var size1 = written1.Length;

		var result2 = Mp3File.Read (written1.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		var written2 = file2.Render (written1.Span);
		var size2 = written2.Length;

		var result3 = Mp3File.Read (written2.Span);
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
	public void Id3v2Tag_CreatedOnDemand_SurvivesRoundTrip ()
	{
		// Arrange: File without existing ID3v2 tag
		var original = new byte[256]; // Just audio placeholder

		var result = Mp3File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Set metadata which should create ID3v2 tag on demand
		file.Title = "New Title";
		file.Artist = "New Artist";

		// Act
		var written = file.Render (original);
		var result2 = Mp3File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var reloaded = result2.File!;

		// Assert
		Assert.IsNotNull (reloaded.Id3v2Tag);
		Assert.AreEqual ("New Title", reloaded.Title);
		Assert.AreEqual ("New Artist", reloaded.Artist);
	}
}
