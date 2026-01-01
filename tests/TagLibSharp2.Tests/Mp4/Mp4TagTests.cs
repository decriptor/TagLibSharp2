// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Tests for MP4 metadata (iTunes-style tags).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4TagTests
{
	[TestMethod]
	public void ReadTitle_Nam_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAtom ("©nam", "My Song Title");

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("My Song Title", result.File!.Title);
	}

	[TestMethod]
	public void WriteTitle_Nam_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Title = "New Song";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("New Song", reResult.File!.Title);
	}

	[TestMethod]
	public void ReadArtist_Art_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAtom ("©ART", TestConstants.Metadata.Artist);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.File!.Artist);
	}

	[TestMethod]
	public void WriteArtist_Art_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Artist = "New Artist";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("New Artist", reResult.File!.Artist);
	}

	[TestMethod]
	public void ReadAlbum_Alb_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithAtom ("©alb", TestConstants.Metadata.Album);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Album, result.File!.Album);
	}

	[TestMethod]
	public void WriteAlbum_Alb_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Album = "New Album";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("New Album", reResult.File!.Album);
	}

	[TestMethod]
	public void ReadTrackNumber_Trkn_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithTrackNumber (5, 12);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5u, result.File!.Track);
		Assert.AreEqual (12u, result.File.TrackCount);
	}

	[TestMethod]
	public void WriteTrackNumber_Trkn_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Track = 7;
		file.TrackCount = 15;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual (7u, reResult.File!.Track);
		Assert.AreEqual (15u, reResult.File.TrackCount);
	}

	[TestMethod]
	public void ReadDiscNumber_Disk_ReturnsCorrectValue ()
	{
		var data = TestBuilders.Mp4.CreateWithDiscNumber (2, 3);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2u, result.File!.DiscNumber);
		Assert.AreEqual (3u, result.File.DiscCount);
	}

	[TestMethod]
	public void WriteDiscNumber_Disk_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.DiscNumber = 1;
		file.DiscCount = 2;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual (1u, reResult.File!.DiscNumber);
		Assert.AreEqual (2u, reResult.File.DiscCount);
	}

	[TestMethod]
	public void ReadCoverArt_CovrJpeg_ReturnsImage ()
	{
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
		var data = TestBuilders.Mp4.CreateWithCoverArt (jpegData, Mp4PictureType.Jpeg);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.File!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, result.File.Pictures[0].PictureType);
		CollectionAssert.AreEqual (jpegData, result.File.Pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void ReadCoverArt_CovrPng_ReturnsImage ()
	{
		var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
		var data = TestBuilders.Mp4.CreateWithCoverArt (pngData, Mp4PictureType.Png);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.File!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, result.File.Pictures[0].PictureType);
	}

	[TestMethod]
	public void WriteCoverArt_Jpeg_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var picture = new Mp4Picture (jpegData, isJpeg: true);
		file.AddPicture (picture);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.HasCount (1, reResult.File!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, reResult.File.Pictures[0].PictureType);
	}

	[TestMethod]
	public void WriteCoverArt_Png_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
		var picture = new Mp4Picture (pngData, isJpeg: false);
		file.AddPicture (picture);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.HasCount (1, reResult.File!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, reResult.File.Pictures[0].PictureType);
	}

	[TestMethod]
	public void ReadFreeformTag_DashDashDash_ReturnsValue ()
	{
		var data = TestBuilders.Mp4.CreateWithFreeformTag ("com.apple.iTunes", "CUSTOM", "Custom Value");

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		var value = result.File!.GetFreeformTag ("com.apple.iTunes", "CUSTOM");
		Assert.AreEqual ("Custom Value", value);
	}

	[TestMethod]
	public void WriteFreeformTag_DashDashDash_WritesCorrectAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.SetFreeformTag ("com.apple.iTunes", "TEST", "Test Value");

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Test Value", reResult.File!.GetFreeformTag ("com.apple.iTunes", "TEST"));
	}

	[TestMethod]
	public void ClearAllMetadata_RemovesAllTags ()
	{
		var data = TestBuilders.Mp4.CreateWithMetadata (
			title: "Title",
			artist: "Artist",
			album: "Album");

		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ClearAllMetadata ();

		Assert.IsNull (file.Title);
		Assert.IsNull (file.Artist);
		Assert.IsNull (file.Album);
	}

	[TestMethod]
	public void Year_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Year = "2025";

		Assert.AreEqual ("2025", file.Year);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("2025", reResult.File!.Year);
	}

	[TestMethod]
	public void Genre_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Genre = "Rock";

		Assert.AreEqual ("Rock", file.Genre);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Rock", reResult.File!.Genre);
	}

	[TestMethod]
	public void Comment_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Comment = "Great song!";

		Assert.AreEqual ("Great song!", file.Comment);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Great song!", reResult.File!.Comment);
	}

	[TestMethod]
	public void AlbumArtist_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.AlbumArtist = "Various Artists";

		Assert.AreEqual ("Various Artists", file.AlbumArtist);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Various Artists", reResult.File!.AlbumArtist);
	}

	[TestMethod]
	public void Composer_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Composer = "Mozart";

		Assert.AreEqual ("Mozart", file.Composer);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Mozart", reResult.File!.Composer);
	}

	[TestMethod]
	public void RemovePictures_RemovesAllCoverArt ()
	{
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var data = TestBuilders.Mp4.CreateWithCoverArt (jpegData, Mp4PictureType.Jpeg);

		var result = Mp4File.Read (data);
		var file = result.File!;

		file.RemovePictures ();

		Assert.IsEmpty (file.Pictures);
	}

	// Sort order atom tests

	[TestMethod]
	public void AlbumSort_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.AlbumSort = "White Album, The";

		Assert.AreEqual ("White Album, The", file.AlbumSort);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("White Album, The", reResult.File!.AlbumSort);
	}

	[TestMethod]
	public void ArtistSort_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ArtistSort = "Beatles, The";

		Assert.AreEqual ("Beatles, The", file.ArtistSort);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Beatles, The", reResult.File!.ArtistSort);
	}

	[TestMethod]
	public void TitleSort_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.TitleSort = "Day in the Life, A";

		Assert.AreEqual ("Day in the Life, A", file.TitleSort);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Day in the Life, A", reResult.File!.TitleSort);
	}

	[TestMethod]
	public void AlbumArtistSort_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.AlbumArtistSort = "Various";

		Assert.AreEqual ("Various", file.AlbumArtistSort);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Various", reResult.File!.AlbumArtistSort);
	}

	[TestMethod]
	public void ComposerSort_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ComposerSort = "Mozart, Wolfgang Amadeus";

		Assert.AreEqual ("Mozart, Wolfgang Amadeus", file.ComposerSort);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Mozart, Wolfgang Amadeus", reResult.File!.ComposerSort);
	}

	// Classical music metadata tests

	[TestMethod]
	public void Work_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Work = "Symphony No. 9 in D minor, Op. 125";

		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", file.Work);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", reResult.File!.Work);
	}

	[TestMethod]
	public void Movement_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Movement = "Allegro con brio";

		Assert.AreEqual ("Allegro con brio", file.Movement);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Allegro con brio", reResult.File!.Movement);
	}

	[TestMethod]
	public void MovementNumber_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MovementNumber = 2;

		Assert.AreEqual (2u, file.MovementNumber);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual (2u, reResult.File!.MovementNumber);
	}

	[TestMethod]
	public void MovementTotal_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MovementTotal = 4;

		Assert.AreEqual (4u, file.MovementTotal);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual (4u, reResult.File!.MovementTotal);
	}

	// Additional metadata property tests

	[TestMethod]
	public void Isrc_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Isrc = "USRC17607839";

		Assert.AreEqual ("USRC17607839", file.Isrc);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("USRC17607839", reResult.File!.Isrc);
	}

	[TestMethod]
	public void Conductor_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Conductor = "Herbert von Karajan";

		Assert.AreEqual ("Herbert von Karajan", file.Conductor);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Herbert von Karajan", reResult.File!.Conductor);
	}

	[TestMethod]
	public void OriginalReleaseDate_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.OriginalReleaseDate = "1985";

		Assert.AreEqual ("1985", file.OriginalReleaseDate);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("1985", reResult.File!.OriginalReleaseDate);
	}

	// ReplayGain property tests

	[TestMethod]
	public void ReplayGainTrackGain_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ReplayGainTrackGain = "-6.50 dB";

		Assert.AreEqual ("-6.50 dB", file.ReplayGainTrackGain);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("-6.50 dB", reResult.File!.ReplayGainTrackGain);
	}

	[TestMethod]
	public void ReplayGainTrackPeak_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ReplayGainTrackPeak = "0.988547";

		Assert.AreEqual ("0.988547", file.ReplayGainTrackPeak);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("0.988547", reResult.File!.ReplayGainTrackPeak);
	}

	[TestMethod]
	public void ReplayGainAlbumGain_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ReplayGainAlbumGain = "-5.20 dB";

		Assert.AreEqual ("-5.20 dB", file.ReplayGainAlbumGain);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("-5.20 dB", reResult.File!.ReplayGainAlbumGain);
	}

	[TestMethod]
	public void ReplayGainAlbumPeak_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ReplayGainAlbumPeak = "0.995123";

		Assert.AreEqual ("0.995123", file.ReplayGainAlbumPeak);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("0.995123", reResult.File!.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void ReplayGain_AllProperties_RoundTrip ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set all ReplayGain properties
		file.ReplayGainTrackGain = "-7.25 dB";
		file.ReplayGainTrackPeak = "0.912345";
		file.ReplayGainAlbumGain = "-6.10 dB";
		file.ReplayGainAlbumPeak = "0.998765";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual ("-7.25 dB", reFile.ReplayGainTrackGain);
		Assert.AreEqual ("0.912345", reFile.ReplayGainTrackPeak);
		Assert.AreEqual ("-6.10 dB", reFile.ReplayGainAlbumGain);
		Assert.AreEqual ("0.998765", reFile.ReplayGainAlbumPeak);
	}

	// R128 loudness property tests

	[TestMethod]
	public void R128TrackGain_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// R128 uses Q7.8 fixed-point, so 256 = 1 dB
		file.R128TrackGain = "-512";

		Assert.AreEqual ("-512", file.R128TrackGain);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("-512", reResult.File!.R128TrackGain);
	}

	[TestMethod]
	public void R128AlbumGain_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// R128 uses Q7.8 fixed-point, so -256 = -1 dB
		file.R128AlbumGain = "-256";

		Assert.AreEqual ("-256", file.R128AlbumGain);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("-256", reResult.File!.R128AlbumGain);
	}

	[TestMethod]
	public void R128TrackGainDb_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set via dB property, which converts to Q7.8
		file.R128TrackGainDb = -2.0;

		// -2 dB * 256 = -512
		Assert.AreEqual ("-512", file.R128TrackGain);
		Assert.AreEqual (-2.0, file.R128TrackGainDb);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual (-2.0, reResult.File!.R128TrackGainDb);
	}

	[TestMethod]
	public void R128AlbumGainDb_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set via dB property, which converts to Q7.8
		file.R128AlbumGainDb = 1.5;

		// 1.5 dB * 256 = 384
		Assert.AreEqual ("384", file.R128AlbumGain);
		Assert.AreEqual (1.5, file.R128AlbumGainDb);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual (1.5, reResult.File!.R128AlbumGainDb);
	}

	[TestMethod]
	public void ReplayGain_ClearValue_RemovesAtom ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.ReplayGainTrackGain = "-6.50 dB";
		Assert.IsNotNull (file.ReplayGainTrackGain);

		file.ReplayGainTrackGain = null;
		Assert.IsNull (file.ReplayGainTrackGain);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.IsNull (reResult.File!.ReplayGainTrackGain);
	}

	// AcoustID tests

	[TestMethod]
	public void AcoustIdId_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.AcoustIdId = "f1b5a8c7-3e4d-4a2b-9c6e-1d2f3a4b5c6d";

		Assert.AreEqual ("f1b5a8c7-3e4d-4a2b-9c6e-1d2f3a4b5c6d", file.AcoustIdId);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("f1b5a8c7-3e4d-4a2b-9c6e-1d2f3a4b5c6d", reResult.File!.AcoustIdId);
	}

	[TestMethod]
	public void AcoustIdFingerprint_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Fingerprints are long base64-like strings
		file.AcoustIdFingerprint = "AQADtJKSJJKSJJKS...truncated...";

		Assert.AreEqual ("AQADtJKSJJKSJJKS...truncated...", file.AcoustIdFingerprint);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("AQADtJKSJJKSJJKS...truncated...", reResult.File!.AcoustIdFingerprint);
	}

	// Extended MusicBrainz tests

	[TestMethod]
	public void MusicBrainzRecordingId_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MusicBrainzRecordingId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", file.MusicBrainzRecordingId);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", reResult.File!.MusicBrainzRecordingId);
	}

	[TestMethod]
	public void MusicBrainzDiscId_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MusicBrainzDiscId = "XzPq9cQtRjON_bCVwK12345678-";

		Assert.AreEqual ("XzPq9cQtRjON_bCVwK12345678-", file.MusicBrainzDiscId);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("XzPq9cQtRjON_bCVwK12345678-", reResult.File!.MusicBrainzDiscId);
	}

	[TestMethod]
	public void MusicBrainzReleaseStatus_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MusicBrainzReleaseStatus = "official";

		Assert.AreEqual ("official", file.MusicBrainzReleaseStatus);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("official", reResult.File!.MusicBrainzReleaseStatus);
	}

	[TestMethod]
	public void MusicBrainzReleaseType_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MusicBrainzReleaseType = "album";

		Assert.AreEqual ("album", file.MusicBrainzReleaseType);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("album", reResult.File!.MusicBrainzReleaseType);
	}

	[TestMethod]
	public void MusicBrainzReleaseCountry_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MusicBrainzReleaseCountry = "US";

		Assert.AreEqual ("US", file.MusicBrainzReleaseCountry);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("US", reResult.File!.MusicBrainzReleaseCountry);
	}

	// DJ and remix metadata tests

	[TestMethod]
	public void InitialKey_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.InitialKey = "Am";

		Assert.AreEqual ("Am", file.InitialKey);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Am", reResult.File!.InitialKey);
	}

	[TestMethod]
	public void Remixer_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Remixer = "Tiësto";

		Assert.AreEqual ("Tiësto", file.Remixer);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Tiësto", reResult.File!.Remixer);
	}

	[TestMethod]
	public void Mood_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Mood = "Energetic";

		Assert.AreEqual ("Energetic", file.Mood);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Energetic", reResult.File!.Mood);
	}

	[TestMethod]
	public void Subtitle_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Subtitle = "Radio Edit";

		Assert.AreEqual ("Radio Edit", file.Subtitle);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("Radio Edit", reResult.File!.Subtitle);
	}

	// Collector metadata tests

	[TestMethod]
	public void Barcode_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Barcode = "0886443927087";

		Assert.AreEqual ("0886443927087", file.Barcode);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("0886443927087", reResult.File!.Barcode);
	}

	[TestMethod]
	public void CatalogNumber_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.CatalogNumber = "WPCR-80001";

		Assert.AreEqual ("WPCR-80001", file.CatalogNumber);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("WPCR-80001", reResult.File!.CatalogNumber);
	}

	[TestMethod]
	public void AmazonId_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.AmazonId = "B000002UAL";

		Assert.AreEqual ("B000002UAL", file.AmazonId);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("B000002UAL", reResult.File!.AmazonId);
	}

	// Library management tests

	[TestMethod]
	public void DateTagged_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.DateTagged = "2025-12-31T10:30:00";

		Assert.AreEqual ("2025-12-31T10:30:00", file.DateTagged);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("2025-12-31T10:30:00", reResult.File!.DateTagged);
	}

	[TestMethod]
	public void Language_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Language = "eng";

		Assert.AreEqual ("eng", file.Language);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("eng", reResult.File!.Language);
	}

	[TestMethod]
	public void MediaType_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.MediaType = "CD";

		Assert.AreEqual ("CD", file.MediaType);

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		Assert.AreEqual ("CD", reResult.File!.MediaType);
	}

	// All extended metadata round-trip test

	[TestMethod]
	public void ExtendedMetadata_AllProperties_RoundTrip ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set all extended properties
		file.AcoustIdId = "acoust-id-123";
		file.MusicBrainzRecordingId = "mb-recording-456";
		file.MusicBrainzReleaseStatus = "official";
		file.MusicBrainzReleaseType = "album";
		file.MusicBrainzReleaseCountry = "GB";
		file.InitialKey = "Dm";
		file.Remixer = "DJ Shadow";
		file.Mood = "Chill";
		file.Barcode = "1234567890123";
		file.CatalogNumber = "CAT-001";
		file.DateTagged = "2025-01-15";
		file.Language = "jpn";
		file.MediaType = "Digital";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual ("acoust-id-123", reFile.AcoustIdId);
		Assert.AreEqual ("mb-recording-456", reFile.MusicBrainzRecordingId);
		Assert.AreEqual ("official", reFile.MusicBrainzReleaseStatus);
		Assert.AreEqual ("album", reFile.MusicBrainzReleaseType);
		Assert.AreEqual ("GB", reFile.MusicBrainzReleaseCountry);
		Assert.AreEqual ("Dm", reFile.InitialKey);
		Assert.AreEqual ("DJ Shadow", reFile.Remixer);
		Assert.AreEqual ("Chill", reFile.Mood);
		Assert.AreEqual ("1234567890123", reFile.Barcode);
		Assert.AreEqual ("CAT-001", reFile.CatalogNumber);
		Assert.AreEqual ("2025-01-15", reFile.DateTagged);
		Assert.AreEqual ("jpn", reFile.Language);
		Assert.AreEqual ("Digital", reFile.MediaType);
	}
}
