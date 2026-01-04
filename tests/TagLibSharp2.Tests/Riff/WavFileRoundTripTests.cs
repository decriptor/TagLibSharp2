// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

/// <summary>
/// Tests that verify WAV data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class WavFileRoundTripTests
{
	static BinaryData CreateMinimalWav ()
	{
		using var builder = new BinaryDataBuilder (512);

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (36);
		builder.AddStringLatin1 ("WAVE");

		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);
		builder.AddUInt16LE (2);
		builder.AddUInt32LE (44100);
		builder.AddUInt32LE (176400);
		builder.AddUInt16LE (4);
		builder.AddUInt16LE (16);

		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = CreateMinimalWav ();

		// Act
		var file1 = WavFile.ReadFromData (original);
		Assert.IsNotNull (file1, "First parse failed");
		file1.Id3v2Tag = new Id3v2Tag { Title = "Test Title", Artist = "Test Artist" };

		var written = file1.Render ();
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var file2 = WavFile.ReadFromData (written);
		Assert.IsNotNull (file2, "Second parse failed");

		// Assert
		Assert.AreEqual ("Test Title", file2.Id3v2Tag?.Title);
		Assert.AreEqual ("Test Artist", file2.Id3v2Tag?.Artist);
	}

	[TestMethod]
	public void Id3v2Tag_AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = CreateMinimalWav ();
		var file = WavFile.ReadFromData (original);
		Assert.IsNotNull (file);

		// Set all standard metadata fields via Id3v2Tag
		file.Id3v2Tag = new Id3v2Tag {
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
		var reloaded = WavFile.ReadFromData (written);
		Assert.IsNotNull (reloaded?.Id3v2Tag);

		// Assert
		Assert.AreEqual ("Round Trip Title", reloaded.Id3v2Tag.Title);
		Assert.AreEqual ("Round Trip Artist", reloaded.Id3v2Tag.Artist);
		Assert.AreEqual ("Round Trip Album", reloaded.Id3v2Tag.Album);
		Assert.AreEqual ("2025", reloaded.Id3v2Tag.Year);
		Assert.AreEqual ("Rock", reloaded.Id3v2Tag.Genre);
		Assert.AreEqual (5u, reloaded.Id3v2Tag.Track);
		Assert.AreEqual (12u, reloaded.Id3v2Tag.TotalTracks);
		Assert.AreEqual (1u, reloaded.Id3v2Tag.DiscNumber);
		Assert.AreEqual (2u, reloaded.Id3v2Tag.TotalDiscs);
		Assert.AreEqual ("Composer Name", reloaded.Id3v2Tag.Composer);
		Assert.AreEqual ("This is a comment", reloaded.Id3v2Tag.Comment);
	}

	[TestMethod]
	public void InfoTag_SurvivesRoundTrip ()
	{
		// Arrange
		var original = CreateMinimalWav ();
		var file = WavFile.ReadFromData (original);
		Assert.IsNotNull (file);

		file.InfoTag = new RiffInfoTag {
			Title = "Info Title",
			Artist = "Info Artist",
			Album = "Info Album",
			Year = "2025",
			Genre = "Classical",
			Comment = "Info Comment"
		};

		// Act
		var written = file.Render ();
		var reloaded = WavFile.ReadFromData (written);
		Assert.IsNotNull (reloaded?.InfoTag);

		// Assert
		Assert.AreEqual ("Info Title", reloaded.InfoTag.Title);
		Assert.AreEqual ("Info Artist", reloaded.InfoTag.Artist);
		Assert.AreEqual ("Info Album", reloaded.InfoTag.Album);
		Assert.AreEqual ("2025", reloaded.InfoTag.Year);
		Assert.AreEqual ("Classical", reloaded.InfoTag.Genre);
		Assert.AreEqual ("Info Comment", reloaded.InfoTag.Comment);
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

		var original = CreateMinimalWav ();
		var file = WavFile.ReadFromData (original);
		Assert.IsNotNull (file);

		// Add picture via Id3v2Tag
		file.Id3v2Tag = new Id3v2Tag { Title = "With Art" };
		var picture = new PictureFrame ("image/jpeg", PictureType.FrontCover, "Cover", new BinaryData (jpegData));
		file.Id3v2Tag.AddPicture (picture);

		// Act
		var written = file.Render ();
		var reloaded = WavFile.ReadFromData (written);
		Assert.IsNotNull (reloaded?.Id3v2Tag);

		// Assert
		var pictures = reloaded.Id3v2Tag.PictureFrames;
		Assert.AreEqual (1, pictures.Count);
		CollectionAssert.AreEqual (jpegData, pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void AudioProperties_PreservedAfterMetadataChange ()
	{
		// Arrange
		var original = CreateMinimalWav ();
		var file = WavFile.ReadFromData (original);
		Assert.IsNotNull (file?.Properties);

		// Store original audio properties
		var originalSampleRate = file.Properties.SampleRate;
		var originalChannels = file.Properties.Channels;
		var originalBitDepth = file.Properties.BitsPerSample;

		// Modify metadata
		file.Id3v2Tag = new Id3v2Tag { Title = "Modified Title" };

		// Act
		var written = file.Render ();
		var reloaded = WavFile.ReadFromData (written);
		Assert.IsNotNull (reloaded?.Properties);

		// Assert: Audio properties unchanged
		Assert.AreEqual (originalSampleRate, reloaded.Properties.SampleRate);
		Assert.AreEqual (originalChannels, reloaded.Properties.Channels);
		Assert.AreEqual (originalBitDepth, reloaded.Properties.BitsPerSample);
		Assert.AreEqual ("Modified Title", reloaded.Id3v2Tag?.Title);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = CreateMinimalWav ();

		// Act: Write multiple times
		var file1 = WavFile.ReadFromData (original);
		Assert.IsNotNull (file1);
		file1.Id3v2Tag = new Id3v2Tag { Title = "Title", Artist = "Artist" };
		var written1 = file1.Render ();
		var size1 = written1.Length;

		var file2 = WavFile.ReadFromData (written1);
		Assert.IsNotNull (file2);
		var written2 = file2.Render ();
		var size2 = written2.Length;

		var file3 = WavFile.ReadFromData (written2);
		Assert.IsNotNull (file3);
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
	public void BothTags_PreferenceMaintained ()
	{
		// Arrange: Set both InfoTag and Id3v2Tag
		var original = CreateMinimalWav ();
		var file = WavFile.ReadFromData (original);
		Assert.IsNotNull (file);

		file.InfoTag = new RiffInfoTag { Title = "Info Title" };
		file.Id3v2Tag = new Id3v2Tag { Title = "ID3 Title" };

		// Act
		var written = file.Render ();
		var reloaded = WavFile.ReadFromData (written);
		Assert.IsNotNull (reloaded);

		// Assert: Id3v2 takes precedence, but both preserved
		Assert.AreEqual ("ID3 Title", reloaded.Title);
		Assert.AreEqual ("Info Title", reloaded.InfoTag?.Title);
		Assert.AreEqual ("ID3 Title", reloaded.Id3v2Tag?.Title);
	}
}
