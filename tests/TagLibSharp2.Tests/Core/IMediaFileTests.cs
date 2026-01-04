using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Asf;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;

// Suppress CA1859 warnings - the whole point of these tests is to verify that
// file classes implement IMediaFile, so we intentionally use the interface type.
#pragma warning disable CA1859

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Tests that all file classes implement the IMediaFile interface.
/// </summary>
[TestClass]
public class IMediaFileTests
{
	[TestMethod]
	public void FlacFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (title: "Test");
		var result = FlacFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
		Assert.IsNotNull (mediaFile.AudioProperties);
	}

	[TestMethod]
	public void Mp3File_ImplementsIMediaFile ()
	{
		// Create minimal ID3v2 tag with audio data
		var tag = TestBuilders.Id3v2.CreateTagWithFrames (4, ("TIT2", "Test"), ("TPE1", "Artist"));
		var audioFrame = new byte[417]; // Minimal MP3 audio frame
		audioFrame[0] = 0xFF;
		audioFrame[1] = 0xFB;
		audioFrame[2] = 0x90;
		audioFrame[3] = 0x00;
		var data = new byte[tag.Length + audioFrame.Length];
		tag.CopyTo (data, 0);
		audioFrame.CopyTo (data, tag.Length);

		var result = Mp3File.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void Mp4File_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Mp4.CreateWithMetadata (title: "Test");
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void OggVorbisFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ();
		var result = OggVorbisFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void OggOpusFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Opus.CreateMinimalFile ();
		var result = OggOpusFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void OggFlacFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.OggFlac.CreateMinimal ();
		var result = OggFlacFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void WavFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Wav.CreateMinimal ();
		var result = WavFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// WAV may or may not have a tag depending on creation method
	}

	[TestMethod]
	public void AiffFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Aiff.CreateMinimal ();
		var result = AiffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// AIFF may or may not have a tag depending on creation method
	}

	[TestMethod]
	public void AsfFile_ImplementsIMediaFile ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void DsfFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Dsf.CreateMinimal ();
		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// DSF may or may not have a tag
	}

	[TestMethod]
	public void DffFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Dff.CreateMinimal ();
		var result = DffFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// DFF may or may not have a tag
	}

	[TestMethod]
	public void WavPackFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.WavPack.CreateMinimal ();
		var result = WavPackFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// WavPack may or may not have a tag
	}

	[TestMethod]
	public void MonkeysAudioFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.MonkeysAudio.CreateMinimal ();
		var result = MonkeysAudioFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// MonkeysAudio may or may not have a tag
	}

	[TestMethod]
	public void MusepackFile_ImplementsIMediaFile ()
	{
		var data = TestBuilders.Musepack.CreateMinimal ();
		var result = MusepackFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.IsNotNull (mediaFile);
		// Musepack may or may not have a tag
	}

	[TestMethod]
	public void MediaFileResult_File_ReturnsIMediaFile ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (title: "Test");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.flac", data);

		var result = MediaFile.Read ("/test.flac", mockFs);
		Assert.IsTrue (result.IsSuccess);

		// File property should be usable as IMediaFile
		var file = result.File;
		Assert.IsNotNull (file);

		// The returned object should implement IMediaFile
		IMediaFile mediaFile = (IMediaFile)file!;
		Assert.IsNotNull (mediaFile.Tag);
	}

	[TestMethod]
	public void IMediaFile_SourcePath_ReturnsPath ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (title: "Test");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.flac", data);

		var result = FlacFile.ReadFromFile ("/music/song.flac", mockFs);
		Assert.IsTrue (result.IsSuccess);

		IMediaFile mediaFile = result.File!;
		Assert.AreEqual ("/music/song.flac", mediaFile.SourcePath);
	}

	[TestMethod]
	public void IMediaFile_Format_ReturnsCorrectFormat ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment (title: "Test");
		var result = FlacFile.Read (data);

		IMediaFile mediaFile = result.File!;
		Assert.AreEqual (MediaFormat.Flac, mediaFile.Format);
	}
}
