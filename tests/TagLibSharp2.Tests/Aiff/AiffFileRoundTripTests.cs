// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Aiff;

/// <summary>
/// Tests that verify AIFF data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class AiffFileRoundTripTests
{
	static BinaryData CreateMinimalAiff ()
	{
		var builder = new BinaryDataBuilder ();

		// Create COMM chunk
		var commChunk = CreateCommChunk (2, 1000, 16, 44100);
		var ssndChunk = CreateSsndChunk ();

		// Calculate total size
		var contentSize = 4 + commChunk.Length + ssndChunk.Length;

		// FORM header
		builder.Add (0x46, 0x4F, 0x52, 0x4D); // "FORM"
		builder.AddUInt32BE ((uint)contentSize);
		builder.Add (0x41, 0x49, 0x46, 0x46); // "AIFF"

		builder.Add (commChunk);
		builder.Add (ssndChunk);

		return builder.ToBinaryData ();
	}

	static byte[] CreateCommChunk (int channels, uint sampleFrames, int bitsPerSample, int sampleRate)
	{
		var builder = new BinaryDataBuilder ();

		builder.Add (0x43, 0x4F, 0x4D, 0x4D); // "COMM"
		builder.AddUInt32BE (18);

		builder.AddUInt16BE ((ushort)channels);
		builder.AddUInt32BE (sampleFrames);
		builder.AddUInt16BE ((ushort)bitsPerSample);
		builder.Add (ExtendedFloat.FromDouble (sampleRate));

		return builder.ToArray ();
	}

	static byte[] CreateSsndChunk ()
	{
		var builder = new BinaryDataBuilder ();

		builder.Add (0x53, 0x53, 0x4E, 0x44); // "SSND"
		builder.AddUInt32BE (8);
		builder.AddUInt32BE (0); // offset
		builder.AddUInt32BE (0); // blockSize

		return builder.ToArray ();
	}

	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = CreateMinimalAiff ();

		// Act
		Assert.IsTrue (AiffFile.TryParse (original, out var file1), "First parse failed");

		file1!.Tag = new Id3v2Tag { Title = "Test Title", Artist = "Test Artist" };

		var written = file1.Render ();
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		Assert.IsTrue (AiffFile.TryParse (written, out var file2), "Second parse failed");

		// Assert
		Assert.AreEqual ("Test Title", file2!.Tag?.Title);
		Assert.AreEqual ("Test Artist", file2.Tag?.Artist);
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = CreateMinimalAiff ();
		Assert.IsTrue (AiffFile.TryParse (original, out var file));

		// Set all standard metadata fields via Id3v2Tag
		file!.Tag = new Id3v2Tag {
			Title = "Round Trip Title",
			Artist = "Round Trip Artist",
			Album = "Round Trip Album",
			Year = "2025",
			Genre = "Rock",
			Track = 5,
			TotalTracks = 12,
			DiscNumber = 1,
			TotalDiscs = 2,
			Composer = "Composer Name",
			Comment = "This is a comment"
		};

		// Act
		var written = file.Render ();
		Assert.IsTrue (AiffFile.TryParse (written, out var reloaded));

		// Assert
		Assert.IsNotNull (reloaded?.Tag);
		Assert.AreEqual ("Round Trip Title", reloaded.Tag.Title);
		Assert.AreEqual ("Round Trip Artist", reloaded.Tag.Artist);
		Assert.AreEqual ("Round Trip Album", reloaded.Tag.Album);
		Assert.AreEqual ("2025", reloaded.Tag.Year);
		Assert.AreEqual ("Rock", reloaded.Tag.Genre);
		Assert.AreEqual (5u, reloaded.Tag.Track);
		Assert.AreEqual (12u, reloaded.Tag.TotalTracks);
		Assert.AreEqual (1u, reloaded.Tag.DiscNumber);
		Assert.AreEqual (2u, reloaded.Tag.TotalDiscs);
		Assert.AreEqual ("Composer Name", reloaded.Tag.Composer);
		Assert.AreEqual ("This is a comment", reloaded.Tag.Comment);
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

		var original = CreateMinimalAiff ();
		Assert.IsTrue (AiffFile.TryParse (original, out var file));

		// Add picture via Id3v2Tag
		var tag = new Id3v2Tag { Title = "With Art" };
		var picture = new PictureFrame ("image/jpeg", PictureType.FrontCover, "Cover", new BinaryData (jpegData));
		tag.AddPicture (picture);
		file!.Tag = tag;

		// Act
		var written = file.Render ();
		Assert.IsTrue (AiffFile.TryParse (written, out var reloaded));

		// Assert
		var pictures = reloaded!.Tag!.PictureFrames;
		Assert.AreEqual (1, pictures.Count);
		CollectionAssert.AreEqual (jpegData, pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void AudioProperties_PreservedAfterMetadataChange ()
	{
		// Arrange
		var original = CreateMinimalAiff ();
		Assert.IsTrue (AiffFile.TryParse (original, out var file));
		Assert.IsNotNull (file?.AudioProperties);

		// Store original audio properties
		var originalSampleRate = file.AudioProperties.SampleRate;
		var originalChannels = file.AudioProperties.Channels;
		var originalBitDepth = file.AudioProperties.BitsPerSample;

		// Modify metadata
		file.Tag = new Id3v2Tag { Title = "Modified Title" };

		// Act
		var written = file.Render ();
		Assert.IsTrue (AiffFile.TryParse (written, out var reloaded));
		Assert.IsNotNull (reloaded?.AudioProperties);

		// Assert: Audio properties unchanged
		Assert.AreEqual (originalSampleRate, reloaded.AudioProperties.SampleRate);
		Assert.AreEqual (originalChannels, reloaded.AudioProperties.Channels);
		Assert.AreEqual (originalBitDepth, reloaded.AudioProperties.BitsPerSample);
		Assert.AreEqual ("Modified Title", reloaded.Tag?.Title);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = CreateMinimalAiff ();

		// Act: Write multiple times
		Assert.IsTrue (AiffFile.TryParse (original, out var file1));
		file1!.Tag = new Id3v2Tag { Title = "Title", Artist = "Artist" };
		var written1 = file1.Render ();
		var size1 = written1.Length;

		Assert.IsTrue (AiffFile.TryParse (written1, out var file2));
		var written2 = file2!.Render ();
		var size2 = written2.Length;

		Assert.IsTrue (AiffFile.TryParse (written2, out var file3));
		var written3 = file3!.Render ();
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
	public void MusicBrainzIds_SurviveRoundTrip ()
	{
		// Arrange
		var original = CreateMinimalAiff ();
		Assert.IsTrue (AiffFile.TryParse (original, out var file));

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		file!.Tag = new Id3v2Tag {
			Title = "With MB IDs",
			MusicBrainzTrackId = mbTrackId,
			MusicBrainzReleaseId = mbReleaseId
		};

		// Act
		var written = file.Render ();
		Assert.IsTrue (AiffFile.TryParse (written, out var reloaded));

		// Assert
		Assert.AreEqual (mbTrackId, reloaded!.Tag!.MusicBrainzTrackId);
		Assert.AreEqual (mbReleaseId, reloaded.Tag.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void ReplayGain_SurvivesRoundTrip ()
	{
		// Arrange
		var original = CreateMinimalAiff ();
		Assert.IsTrue (AiffFile.TryParse (original, out var file));

		file!.Tag = new Id3v2Tag {
			Title = "With ReplayGain",
			ReplayGainTrackGain = "-6.50 dB",
			ReplayGainTrackPeak = "0.988547",
			ReplayGainAlbumGain = "-7.25 dB",
			ReplayGainAlbumPeak = "1.0"
		};

		// Act
		var written = file.Render ();
		Assert.IsTrue (AiffFile.TryParse (written, out var reloaded));

		// Assert
		Assert.AreEqual ("-6.50 dB", reloaded!.Tag!.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", reloaded.Tag.ReplayGainTrackPeak);
		Assert.AreEqual ("-7.25 dB", reloaded.Tag.ReplayGainAlbumGain);
		Assert.AreEqual ("1.0", reloaded.Tag.ReplayGainAlbumPeak);
	}
}
