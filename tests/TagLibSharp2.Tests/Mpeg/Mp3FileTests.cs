// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// Tests for <see cref="Mp3File"/> class.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class Mp3FileTests
{

	[TestMethod]
	public void Read_EmptyData_ReturnsFailure ()
	{
		var result = Mp3File.Read ([]);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_DataTooShort_ReturnsFailure ()
	{
		// Less than minimum size for any valid structure
		var result = Mp3File.Read (new byte[] { 0x00, 0x00, 0x00 });

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_Id3v2Only_ParsesTag ()
	{
		// Create minimal ID3v2.4 tag
		var tag = new Id3v2Tag {
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist
		};

		var tagData = tag.Render ();

		// Append some fake audio data
		var audioData = new byte[256];
		var fullData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (fullData);

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Id3v2Tag);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File.Id3v2Tag.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.File.Id3v2Tag.Artist);
		Assert.IsNull (result.File.Id3v1Tag);
	}

	[TestMethod]
	public void Read_Id3v1Only_ParsesTag ()
	{
		// Create ID3v1 tag at end of file
		var tag = new Id3v1Tag {
			Title = TestConstants.Metadata.Title,
			Artist = TestConstants.Metadata.Artist
		};

		var tagData = tag.Render ();

		// Create fake audio data + ID3v1 at end
		var audioData = new byte[256];
		var fullData = new byte[audioData.Length + tagData.Length];
		tagData.Span.CopyTo (fullData.AsSpan (audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.File!.Id3v2Tag);
		Assert.IsNotNull (result.File.Id3v1Tag);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File.Id3v1Tag.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.File.Id3v1Tag.Artist);
	}

	/// <summary>
	/// When an MP3 file carries both an ID3v2 and an ID3v1 tag, <see cref="Mp3File.Tag"/>
	/// must return a unified view that prefers ID3v2 for fields v2 has and falls back
	/// to ID3v1 for fields only present in the legacy tag.
	/// </summary>
	/// <remarks>
	/// ID3v1 (https://id3.org/ID3v1) has a fixed 128-byte footer with short length
	/// limits. ID3v2 (https://id3.org/id3v2.4.0-structure) is prepended to the file
	/// and has no such limits. Per widespread convention (followed by iTunes, foobar2000,
	/// MusicBrainz Picard, and TagLib itself), ID3v2 is authoritative when both are
	/// present. A file might still carry ID3v1-only data (older files re-tagged by tools
	/// that only update v2), so we must not silently lose those values.
	/// </remarks>
	[TestMethod]
	public void Tag_BothIdTags_PrefersId3v2ButFallsBackToId3v1 ()
	{
		// ID3v2: has Title but no Genre
		var id3v2 = new Id3v2Tag { Title = "V2 Title" };
		var v2Data = id3v2.Render ();

		// ID3v1: has both Title and Genre
		var id3v1 = new Id3v1Tag { Title = "V1 Title", Genre = "Rock" };
		var v1Data = id3v1.Render ();

		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length + v1Data.Length];
		v2Data.Span.CopyTo (fullData);
		v1Data.Span.CopyTo (fullData.AsSpan (v2Data.Length + audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Id3v2Tag);
		Assert.IsNotNull (result.File.Id3v1Tag);
		Assert.AreEqual ("V2 Title", result.File.Tag!.Title,
			"Title should come from ID3v2 (authoritative when both present)");
		Assert.AreEqual ("Rock", result.File.Tag.Genre,
			"Genre should fall back to ID3v1 when ID3v2 has no Genre");
	}

	/// <summary>
	/// An MP3 file with no ID3v2 and no ID3v1 must expose <c>Tag == null</c>,
	/// matching the "tag is absent" convention used by other
	/// <see cref="IMediaFile"/> implementations in this repo. A Mp3File that
	/// always materialized a facade would lie about the absence of metadata.
	/// </summary>
	[TestMethod]
	public void Tag_NoTagsPresent_ReturnsNull ()
	{
		var audioOnly = new byte[256];

		var result = Mp3File.Read (audioOnly);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.File!.Id3v2Tag);
		Assert.IsNull (result.File.Id3v1Tag);
		Assert.IsNull (result.File.Tag);
	}

	/// <summary>
	/// Writing through the combined Tag view updates both ID3v2 and ID3v1 so the
	/// file saves with consistent metadata in both tags. ID3v1's format-specific
	/// limits still apply (30-byte cap per https://id3.org/ID3v1), but the
	/// values round-trip as far as each format allows.
	/// </summary>
	[TestMethod]
	public void Tag_Setter_WritesThroughToBothIdTags ()
	{
		var id3v2 = new Id3v2Tag { Title = "Before" };
		var id3v1 = new Id3v1Tag { Title = "Before" };
		var v2Data = id3v2.Render ();
		var v1Data = id3v1.Render ();

		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length + v1Data.Length];
		v2Data.Span.CopyTo (fullData);
		v1Data.Span.CopyTo (fullData.AsSpan (v2Data.Length + audioData.Length));

		var file = Mp3File.Read (fullData).File!;
		file.Tag!.Title = "After";

		Assert.AreEqual ("After", file.Id3v2Tag!.Title, "ID3v2 updated");
		Assert.AreEqual ("After", file.Id3v1Tag!.Title, "ID3v1 updated");
	}

	[TestMethod]
	public void Read_BothTags_ParsesBoth ()
	{
		// Create ID3v2 tag
		var id3v2 = new Id3v2Tag ();
		id3v2.Title = "V2 Title";
		id3v2.Artist = "V2 Artist";
		var v2Data = id3v2.Render ();

		// Create ID3v1 tag
		var id3v1 = new Id3v1Tag ();
		id3v1.Title = "V1 Title";
		id3v1.Artist = "V1 Artist";
		var v1Data = id3v1.Render ();

		// Create full file: ID3v2 + audio + ID3v1
		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length + v1Data.Length];
		v2Data.Span.CopyTo (fullData);
		v1Data.Span.CopyTo (fullData.AsSpan (v2Data.Length + audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Id3v2Tag);
		Assert.IsNotNull (result.File.Id3v1Tag);
		Assert.AreEqual ("V2 Title", result.File.Id3v2Tag.Title);
		Assert.AreEqual ("V1 Title", result.File.Id3v1Tag.Title);
	}



	[TestMethod]
	public void Title_WithId3v2_ReturnsId3v2Value ()
	{
		var id3v2 = new Id3v2Tag ();
		id3v2.Title = "V2 Title";
		var v2Data = id3v2.Render ();

		var id3v1 = new Id3v1Tag ();
		id3v1.Title = "V1 Title";
		var v1Data = id3v1.Render ();

		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length + v1Data.Length];
		v2Data.Span.CopyTo (fullData);
		v1Data.Span.CopyTo (fullData.AsSpan (v2Data.Length + audioData.Length));

		var result = Mp3File.Read (fullData);

		// ID3v2 should take precedence
		Assert.AreEqual ("V2 Title", result.File!.Title);
	}

	[TestMethod]
	public void Title_OnlyId3v1_ReturnsId3v1Value ()
	{
		var id3v1 = new Id3v1Tag ();
		id3v1.Title = "V1 Title";
		var v1Data = id3v1.Render ();

		var audioData = new byte[256];
		var fullData = new byte[audioData.Length + v1Data.Length];
		v1Data.Span.CopyTo (fullData.AsSpan (audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.AreEqual ("V1 Title", result.File!.Title);
	}

	[TestMethod]
	public void SetTitle_NoTags_CreatesId3v2 ()
	{
		// Create file with no tags (just audio data)
		var audioData = new byte[256];

		var result = Mp3File.Read (audioData);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "New Title";

		Assert.IsNotNull (result.File.Id3v2Tag);
		Assert.AreEqual ("New Title", result.File.Id3v2Tag.Title);
		Assert.AreEqual ("New Title", result.File.Title);
	}

	[TestMethod]
	public void ReplayGain_GetSet_WorksViaId3v2 ()
	{
		var id3v2 = new Id3v2Tag ();
		var v2Data = id3v2.Render ();

		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length];
		v2Data.Span.CopyTo (fullData);

		var result = Mp3File.Read (fullData);

		result.File!.ReplayGainTrackGain = "-6.50 dB";
		result.File.ReplayGainTrackPeak = "0.988547";

		Assert.AreEqual ("-6.50 dB", result.File.ReplayGainTrackGain);
		Assert.AreEqual ("0.988547", result.File.ReplayGainTrackPeak);
		Assert.AreEqual ("-6.50 dB", result.File.Id3v2Tag!.ReplayGainTrackGain);
	}

	[TestMethod]
	public void MusicBrainz_GetSet_WorksViaId3v2 ()
	{
		var id3v2 = new Id3v2Tag ();
		var v2Data = id3v2.Render ();

		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length];
		v2Data.Span.CopyTo (fullData);

		var result = Mp3File.Read (fullData);

		result.File!.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";

		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", result.File.MusicBrainzTrackId);
		Assert.AreEqual ("f4e7c9d8-1234-5678-9abc-def012345678", result.File.Id3v2Tag!.MusicBrainzTrackId);
	}



	[TestMethod]
	public void Read_WithXingHeader_ParsesAudioProperties ()
	{
		// Create ID3v2 + MP3 frame with Xing header
		var id3v2 = new Id3v2Tag ();
		var v2Data = id3v2.Render ();

		// Create MP3 frame header (MPEG1 Layer3, 128kbps, 44100Hz, stereo)
		var frameData = new TagLibSharp2.Core.BinaryDataBuilder ();
		frameData.Add (0xFF, 0xFB, 0x90, 0x00); // MPEG1 L3 128kbps 44100Hz stereo

		// Side info (32 bytes for stereo)
		frameData.Add (new byte[32]);

		// Xing header with frame count
		frameData.Add (0x58, 0x69, 0x6E, 0x67); // "Xing"
		frameData.AddUInt32BE (0x03);            // Flags: frames + bytes
		frameData.AddUInt32BE (4717);            // Frame count for ~107 seconds
		frameData.AddUInt32BE (1710336);         // Byte count

		// Pad to full frame size
		while (frameData.Length < 417)
			frameData.Add (0);

		var fullData = new byte[v2Data.Length + frameData.Length];
		v2Data.Span.CopyTo (fullData);
		frameData.ToBinaryData ().Span.CopyTo (fullData.AsSpan (v2Data.Length));

		var result = Mp3File.Read (fullData);

		Assert.IsNotNull (result.File?.Properties);
		Assert.AreEqual (44100, result.File.Properties.SampleRate);
		Assert.AreEqual (2, result.File.Properties.Channels);
		Assert.IsTrue (result.File.Properties.IsVbr);
		Assert.IsTrue (result.File.Duration?.TotalSeconds > 100);
	}

	[TestMethod]
	public void Read_CbrMp3_ParsesAudioProperties ()
	{
		// Create CBR MP3 (no Xing header)
		var frameData = new TagLibSharp2.Core.BinaryDataBuilder ();
		frameData.Add (0xFF, 0xFB, 0x90, 0x00); // MPEG1 L3 128kbps 44100Hz stereo

		// Side info
		frameData.Add (new byte[32]);

		// No Xing header, just padding
		while (frameData.Length < 417)
			frameData.Add (0);

		// Add more frames to simulate file size
		while (frameData.Length < 50000)
			frameData.Add (0);

		var result = Mp3File.Read (frameData.ToArray ());

		Assert.IsNotNull (result.File?.Properties);
		Assert.AreEqual (44100, result.File.Properties.SampleRate);
		Assert.AreEqual (128, result.File.Properties.Bitrate);
		Assert.IsFalse (result.File.Properties.IsVbr);
	}

	[TestMethod]
	public void Duration_DelegatesTo_AudioProperties ()
	{
		// Create minimal MP3 with frame
		var frameData = new TagLibSharp2.Core.BinaryDataBuilder ();
		frameData.Add (0xFF, 0xFB, 0x90, 0x00);
		frameData.Add (new byte[32]);
		while (frameData.Length < 50000)
			frameData.Add (0);

		var result = Mp3File.Read (frameData.ToArray ());

		Assert.IsNotNull (result.File?.Duration);
		Assert.AreEqual (result.File.Properties!.Duration, result.File.Duration);
	}



	[TestMethod]
	public void Render_WithId3v2_PreservesTags ()
	{
		var id3v2 = new Id3v2Tag ();
		id3v2.Title = "Test Song";
		var v2Data = id3v2.Render ();

		var audioData = new byte[256];
		for (int i = 0; i < audioData.Length; i++)
			audioData[i] = (byte)(i & 0xFF);

		var fullData = new byte[v2Data.Length + audioData.Length];
		v2Data.Span.CopyTo (fullData);
		audioData.CopyTo (fullData, v2Data.Length);

		var result = Mp3File.Read (fullData);
		result.File!.Title = "Modified Title";

		var rendered = result.File.Render (fullData);

		// Parse the rendered data
		var reRead = Mp3File.Read (rendered.Span);
		Assert.IsTrue (reRead.IsSuccess);
		Assert.AreEqual ("Modified Title", reRead.File!.Title);
	}



	[TestMethod]
	public async Task ReadFromFileAsync_WithMockFileSystem_ParsesTag ()
	{
		var fs = new MockFileSystem ();

		// Create minimal MP3 with ID3v2 tag
		var tag = new Id3v2Tag { Title = TestConstants.Metadata.Title };
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var fullData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (fullData);

		fs.AddFile ("/test.mp3", fullData);

		var result = await Mp3File.ReadFromFileAsync ("/test.mp3", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File!.Title);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_FileNotFound_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		var result = await Mp3File.ReadFromFileAsync ("/nonexistent.mp3", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Cancellation_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		var tag = new Id3v2Tag { Title = "Test" };
		var tagData = tag.Render ();
		fs.AddFile ("/test.mp3", tagData.ToArray ());

		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await Mp3File.ReadFromFileAsync ("/test.mp3", fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithMockFileSystem_WritesData ()
	{
		var fs = new MockFileSystem ();

		// Create and modify MP3
		var tag = new Id3v2Tag { Title = "Original" };
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var originalData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (originalData);

		var result = Mp3File.Read (originalData);
		result.File!.Title = "Modified";

		var writeResult = await result.File.SaveToFileAsync ("/output.mp3", originalData, fs);

		Assert.IsTrue (writeResult.IsSuccess);
		Assert.IsTrue (fs.FileExists ("/output.mp3"));

		// Verify the saved file
		var savedData = fs.ReadAllBytes ("/output.mp3");
		var reRead = Mp3File.Read (savedData);
		Assert.AreEqual ("Modified", reRead.File!.Title);
	}

	[TestMethod]
	public void ReadFromFile_WithMockFileSystem_ParsesTag ()
	{
		var fs = new MockFileSystem ();

		var tag = new Id3v2Tag { Title = TestConstants.Metadata.Title };
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var fullData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (fullData);

		fs.AddFile ("/test.mp3", fullData);

		var result = Mp3File.ReadFromFile ("/test.mp3", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File!.Title);
		Assert.AreEqual ("/test.mp3", result.File.SourcePath);
	}

	[TestMethod]
	public void ReadFromFile_FileNotFound_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		var result = Mp3File.ReadFromFile ("/nonexistent.mp3", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void SaveToFile_WithPathAndOriginalData_WritesCorrectly ()
	{
		var fs = new MockFileSystem ();

		var tag = new Id3v2Tag { Title = "Original" };
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var originalData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (originalData);

		var result = Mp3File.Read (originalData);
		result.File!.Title = "Modified";

		var writeResult = result.File.SaveToFile ("/output.mp3", originalData, fs);

		Assert.IsTrue (writeResult.IsSuccess);
		var savedData = fs.ReadAllBytes ("/output.mp3");
		var reRead = Mp3File.Read (savedData);
		Assert.AreEqual ("Modified", reRead.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_ConvenienceWithPath_ReReadsSourceFile ()
	{
		var fs = new MockFileSystem ();

		var tag = new Id3v2Tag { Title = "Original" };
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var fullData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (fullData);

		fs.AddFile ("/source.mp3", fullData);

		var result = Mp3File.ReadFromFile ("/source.mp3", fs);
		result.File!.Title = "Modified";

		var writeResult = result.File.SaveToFile ("/output.mp3", fs);

		Assert.IsTrue (writeResult.IsSuccess);
		var savedData = fs.ReadAllBytes ("/output.mp3");
		var reRead = Mp3File.Read (savedData);
		Assert.AreEqual ("Modified", reRead.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_ConvenienceNoPath_SavesBackToSource ()
	{
		var fs = new MockFileSystem ();

		var tag = new Id3v2Tag { Title = "Original" };
		var tagData = tag.Render ();
		var audioData = new byte[256];
		var fullData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (fullData);

		fs.AddFile ("/source.mp3", fullData);

		var result = Mp3File.ReadFromFile ("/source.mp3", fs);
		result.File!.Title = "Modified";

		var writeResult = result.File.SaveToFile (fs);

		Assert.IsTrue (writeResult.IsSuccess);
		var savedData = fs.ReadAllBytes ("/source.mp3");
		var reRead = Mp3File.Read (savedData);
		Assert.AreEqual ("Modified", reRead.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_NoSourcePath_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		// Read from binary data (no source path)
		var tag = new Id3v2Tag { Title = "Test" };
		var tagData = tag.Render ();
		var result = Mp3File.Read (tagData.Span);

		var writeResult = result.File!.SaveToFile (fs);

		Assert.IsFalse (writeResult.IsSuccess);
		Assert.IsTrue (writeResult.Error!.Contains ("No source path"));
	}

	[TestMethod]
	public void SaveToFile_ConvenienceWithPath_NoSourcePath_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		// Read from binary data (no source path)
		var tag = new Id3v2Tag { Title = "Test" };
		var tagData = tag.Render ();
		var result = Mp3File.Read (tagData.Span);

		var writeResult = result.File!.SaveToFile ("/output.mp3", fs);

		Assert.IsFalse (writeResult.IsSuccess);
	}

	[TestMethod]
	public void HasId3v1Tag_WithTag_ReturnsTrue ()
	{
		var id3v1 = new Id3v1Tag { Title = "Test" };
		var v1Data = id3v1.Render ();
		var audioData = new byte[256];
		var fullData = new byte[audioData.Length + v1Data.Length];
		v1Data.Span.CopyTo (fullData.AsSpan (audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.File!.HasId3v1Tag);
		Assert.IsFalse (result.File.HasId3v2Tag);
	}

	[TestMethod]
	public void HasId3v2Tag_WithTag_ReturnsTrue ()
	{
		var id3v2 = new Id3v2Tag { Title = "Test" };
		var v2Data = id3v2.Render ();
		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length];
		v2Data.Span.CopyTo (fullData);

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.File!.HasId3v2Tag);
		Assert.IsFalse (result.File.HasId3v1Tag);
	}

	[TestMethod]
	public void Id3v2Size_ReturnsCorrectValue ()
	{
		var id3v2 = new Id3v2Tag { Title = "Test Title" };
		var v2Data = id3v2.Render ();
		var audioData = new byte[256];
		var fullData = new byte[v2Data.Length + audioData.Length];
		v2Data.Span.CopyTo (fullData);

		var result = Mp3File.Read (fullData);

		Assert.AreEqual (v2Data.Length, result.File!.Id3v2Size);
	}

	[TestMethod]
	public void Album_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.Album = "Test Album";

		Assert.AreEqual ("Test Album", result.File.Album);
		Assert.AreEqual ("Test Album", result.File.Id3v2Tag!.Album);
	}

	[TestMethod]
	public void Year_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.Year = "2025";

		Assert.AreEqual ("2025", result.File.Year);
	}

	[TestMethod]
	public void Genre_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.Genre = "Rock";

		Assert.AreEqual ("Rock", result.File.Genre);
	}

	[TestMethod]
	public void Track_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.Track = 5;

		Assert.AreEqual (5u, result.File.Track);
	}

	[TestMethod]
	public void Comment_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.Comment = "Great song!";

		Assert.AreEqual ("Great song!", result.File.Comment);
	}

	[TestMethod]
	public void AlbumArtist_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.AlbumArtist = "Various Artists";

		Assert.AreEqual ("Various Artists", result.File.AlbumArtist);
	}

	[TestMethod]
	public void DiscNumber_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.DiscNumber = 2;

		Assert.AreEqual (2u, result.File.DiscNumber);
	}

	[TestMethod]
	public void Composer_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.Composer = "Mozart";

		Assert.AreEqual ("Mozart", result.File.Composer);
	}

	[TestMethod]
	public void BeatsPerMinute_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.BeatsPerMinute = 120;

		Assert.AreEqual (120u, result.File.BeatsPerMinute);
	}

	[TestMethod]
	public void ReplayGainAlbumGain_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.ReplayGainAlbumGain = "-8.50 dB";

		Assert.AreEqual ("-8.50 dB", result.File.ReplayGainAlbumGain);
	}

	[TestMethod]
	public void ReplayGainAlbumPeak_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.ReplayGainAlbumPeak = "0.999";

		Assert.AreEqual ("0.999", result.File.ReplayGainAlbumPeak);
	}

	[TestMethod]
	public void MusicBrainzReleaseId_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

		Assert.AreEqual ("a1b2c3d4-5678-90ab-cdef-1234567890ab", result.File.MusicBrainzReleaseId);
	}

	[TestMethod]
	public void MusicBrainzArtistId_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.MusicBrainzArtistId = "artist-uuid-1234";

		Assert.AreEqual ("artist-uuid-1234", result.File.MusicBrainzArtistId);
	}

	[TestMethod]
	public void MusicBrainzReleaseGroupId_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.MusicBrainzReleaseGroupId = "release-group-uuid";

		Assert.AreEqual ("release-group-uuid", result.File.MusicBrainzReleaseGroupId);
	}

	[TestMethod]
	public void MusicBrainzAlbumArtistId_GetSet_WorksCorrectly ()
	{
		var result = Mp3File.Read (new byte[256]);
		result.File!.MusicBrainzAlbumArtistId = "album-artist-uuid";

		Assert.AreEqual ("album-artist-uuid", result.File.MusicBrainzAlbumArtistId);
	}

	[TestMethod]
	public void Properties_WithId3v1Fallback_ReturnsId3v1Values ()
	{
		var id3v1 = new Id3v1Tag {
			Title = "V1 Title",
			Artist = "V1 Artist",
			Album = "V1 Album",
			Year = "1999",
			Genre = "Rock",
			Track = 3,
			Comment = "V1 Comment"
		};
		var v1Data = id3v1.Render ();
		var audioData = new byte[256];
		var fullData = new byte[audioData.Length + v1Data.Length];
		v1Data.Span.CopyTo (fullData.AsSpan (audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.AreEqual ("V1 Title", result.File!.Title);
		Assert.AreEqual ("V1 Artist", result.File.Artist);
		Assert.AreEqual ("V1 Album", result.File.Album);
		Assert.AreEqual ("1999", result.File.Year);
		Assert.AreEqual ("Rock", result.File.Genre);
		Assert.AreEqual (3u, result.File.Track);
		Assert.AreEqual ("V1 Comment", result.File.Comment);
	}

	[TestMethod]
	public void Render_WithBothTags_PreservesBoth ()
	{
		var id3v2 = new Id3v2Tag { Title = "V2 Title" };
		var v2Data = id3v2.Render ();

		var id3v1 = new Id3v1Tag { Title = "V1 Title" };
		var v1Data = id3v1.Render ();

		var audioData = new byte[256];
		for (int i = 0; i < audioData.Length; i++)
			audioData[i] = (byte)(i & 0xFF);

		var fullData = new byte[v2Data.Length + audioData.Length + v1Data.Length];
		v2Data.Span.CopyTo (fullData);
		audioData.CopyTo (fullData, v2Data.Length);
		v1Data.Span.CopyTo (fullData.AsSpan (v2Data.Length + audioData.Length));

		var result = Mp3File.Read (fullData);
		var rendered = result.File!.Render (fullData);

		var reRead = Mp3File.Read (rendered.Span);
		Assert.IsTrue (reRead.File!.HasId3v2Tag);
		Assert.IsTrue (reRead.File.HasId3v1Tag);
		Assert.AreEqual ("V2 Title", reRead.File.Id3v2Tag!.Title);
		Assert.AreEqual ("V1 Title", reRead.File.Id3v1Tag!.Title);
	}

	[TestMethod]
	public void Mp3FileReadResult_Equality_WorksCorrectly ()
	{
		var result1 = Mp3FileReadResult.Failure ("Error");
		var result2 = Mp3FileReadResult.Failure ("Error");
		var result3 = Mp3FileReadResult.Failure ("Different");

		Assert.AreEqual (result1, result2);
		Assert.AreNotEqual (result1, result3);
		Assert.IsTrue (result1 == result2);
		Assert.IsTrue (result1 != result3);
	}

	[TestMethod]
	public void Mp3FileReadResult_GetHashCode_ConsistentWithEquals ()
	{
		var result1 = Mp3FileReadResult.Failure ("Error");
		var result2 = Mp3FileReadResult.Failure ("Error");

		Assert.AreEqual (result1.GetHashCode (), result2.GetHashCode ());
	}

	[TestMethod]
	public void Mp3FileReadResult_EqualsObject_WorksCorrectly ()
	{
		var result = Mp3FileReadResult.Failure ("Error");

		Assert.IsFalse (result.Equals (null));
		Assert.IsFalse (result.Equals ("not a result"));
		Assert.IsTrue (result.Equals ((object)Mp3FileReadResult.Failure ("Error")));
	}
}
