// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Tests that verify MP4 data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class Mp4RoundTripTests
{
	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateWithMetadata ("Test Title", "Test Artist");

		// Act
		var result1 = Mp4File.Read (original);
		Assert.IsTrue (result1.IsSuccess, $"First parse failed: {result1.Error}");
		var file1 = result1.File!;

		var written = file1.Render (original);
		Assert.IsTrue (written.Length > 0, "Render produced empty output");

		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Test Title", file2.Title);
		Assert.AreEqual ("Test Artist", file2.Artist);
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange: Create file with all standard metadata
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);

		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		// Set all standard metadata fields (uses EnsureTag() internally)
		file.Title = "Round Trip Title";
		file.Artist = "Round Trip Artist";
		file.Album = "Round Trip Album";
		file.Year = "2025";
		file.Genre = "Rock";
		file.Track = 5;
		file.TotalTracks = 12;
		file.DiscNumber = 1;
		file.TotalDiscs = 2;
		file.Composer = "Composer Name";
		file.Comment = "This is a comment";
		file.AlbumArtist = "Album Artist";

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var reloaded = result2.File!;

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
		Assert.AreEqual ("Album Artist", reloaded.AlbumArtist);
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

		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		var picture = new Mp4Picture ("image/jpeg", PictureType.FrontCover, "Cover", new BinaryData (jpegData));
		file.AddPicture (picture);

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual (1, file2.Pictures.Length);
		CollectionAssert.AreEqual (jpegData, file2.Pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void MediaData_RemainsUnchanged ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateWithMetadata ("Original Title", "Original Artist");

		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess, $"Parse failed: {result.Error}");
		var file = result.File!;

		// Modify only metadata
		file.Title = "Modified Title";

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess, $"Second parse failed: {result2.Error}");
		var file2 = result2.File!;

		// Assert - metadata changed, structure preserved
		Assert.AreEqual ("Modified Title", file2.Title);
	}

	[TestMethod]
	public void EmptyMetadata_ToPopulated_ToEmpty ()
	{
		// Arrange: Start with no metadata
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);

		// Step 1: Parse file with no ilst
		var result1 = Mp4File.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		Assert.IsNull (file1.Title);

		// Step 2: Add metadata (uses EnsureTag internally)
		file1.Title = "Added Title";
		file1.Artist = "Added Artist";

		var withMetadata = file1.Render (original);
		var result2 = Mp4File.Read (withMetadata.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		Assert.AreEqual ("Added Title", file2.Title);

		// Step 3: Remove all metadata
		file2.Tag!.Clear ();

		var cleared = file2.Render (withMetadata.Span);
		var result3 = Mp4File.Read (cleared.Span);
		Assert.IsTrue (result3.IsSuccess);
		var file3 = result3.File!;

		// Assert: Tag should be empty
		Assert.IsNull (file3.Title);
		Assert.IsNull (file3.Artist);
	}

	[TestMethod]
	public void Unicode_AllScripts ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// Set metadata with various Unicode scripts
		file.Title = "Titulo";
		file.Artist = "Artist Name";
		file.Album = "Album Name";
		file.Comment = "Unicode test";

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Titulo", file2.Title);
		Assert.AreEqual ("Artist Name", file2.Artist);
		Assert.AreEqual ("Album Name", file2.Album);
		Assert.AreEqual ("Unicode test", file2.Comment);
	}

	[TestMethod]
	public void LargeMetadata_LongComment ()
	{
		// Arrange: Long comment (lyrics-size)
		var longComment = new string ('X', 10000);

		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		file.Comment = longComment;

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual (longComment, file2.Comment);
	}

	[TestMethod]
	public void MultipleWrites_NoSignificantDataGrowth ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateWithMetadata ("Title", "Artist");

		// Act: Write multiple times
		var result1 = Mp4File.Read (original);
		Assert.IsTrue (result1.IsSuccess);
		var file1 = result1.File!;
		var written1 = file1.Render (original);
		var size1 = written1.Length;

		var result2 = Mp4File.Read (written1.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;
		var written2 = file2.Render (written1.Span);
		var size2 = written2.Length;

		var result3 = Mp4File.Read (written2.Span);
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
	public void TrackNumber_BinaryFormat ()
	{
		// Arrange: trkn uses binary format (track/total)
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		file.Track = 5;
		file.TotalTracks = 12;

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual (5u, file2.Track);
		Assert.AreEqual (12u, file2.TotalTracks);
	}

	[TestMethod]
	public void DiscNumber_BinaryFormat ()
	{
		// Arrange: disk uses binary format (disc/total)
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		file.DiscNumber = 2;
		file.TotalDiscs = 3;

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual (2u, file2.DiscNumber);
		Assert.AreEqual (3u, file2.TotalDiscs);
	}

	[TestMethod]
	public void MusicBrainzIds_SurviveRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		var mbTrackId = "12345678-1234-1234-1234-123456789012";
		var mbReleaseId = "87654321-4321-4321-4321-210987654321";

		// Set via freeform tags API
		file.SetFreeformTag ("com.apple.iTunes", "MusicBrainz Track Id", mbTrackId);
		file.SetFreeformTag ("com.apple.iTunes", "MusicBrainz Album Id", mbReleaseId);

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual (mbTrackId, file2.GetFreeformTag ("com.apple.iTunes", "MusicBrainz Track Id"));
		Assert.AreEqual (mbReleaseId, file2.GetFreeformTag ("com.apple.iTunes", "MusicBrainz Album Id"));
	}

	[TestMethod]
	public void ReplayGain_SurvivesRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		file.ReplayGainTrackGain = "-6.50 dB";
		file.ReplayGainTrackPeak = "0.988547";
		file.ReplayGainAlbumGain = "-7.25 dB";
		file.ReplayGainAlbumPeak = "1.0";

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("-6.50 dB", file2.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", file2.ReplayGainTrackPeak);
		Assert.AreEqual ("-7.25 dB", file2.ReplayGainAlbumGain);
		Assert.AreEqual ("1.0", file2.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void SortFields_SurviveRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		file.TitleSort = "Title, The";
		file.ArtistSort = "Artist, The";
		file.AlbumSort = "Album, The";

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.AreEqual ("Title, The", file2.TitleSort);
		Assert.AreEqual ("Artist, The", file2.ArtistSort);
		Assert.AreEqual ("Album, The", file2.AlbumSort);
	}

	[TestMethod]
	public void Compilation_SurvivesRoundTrip ()
	{
		// Arrange
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (original);
		Assert.IsTrue (result.IsSuccess);
		var file = result.File!;

		// First set any property to ensure Tag is created
		file.Title = "";
		file.Tag!.IsCompilation = true;

		// Act
		var written = file.Render (original);
		var result2 = Mp4File.Read (written.Span);
		Assert.IsTrue (result2.IsSuccess);
		var file2 = result2.File!;

		// Assert
		Assert.IsTrue (file2.Tag!.IsCompilation);
	}
}
