// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;

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
		var result = Mp3File.Read (Array.Empty<byte> ());

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
		var tag = new Id3v2Tag ();
		tag.Title = "Test Song";
		tag.Artist = "Test Artist";

		var tagData = tag.Render ();

		// Append some fake audio data
		var audioData = new byte[256];
		var fullData = new byte[tagData.Length + audioData.Length];
		tagData.Span.CopyTo (fullData);

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.Id3v2Tag);
		Assert.AreEqual ("Test Song", result.File.Id3v2Tag.Title);
		Assert.AreEqual ("Test Artist", result.File.Id3v2Tag.Artist);
		Assert.IsNull (result.File.Id3v1Tag);
	}

	[TestMethod]
	public void Read_Id3v1Only_ParsesTag ()
	{
		// Create ID3v1 tag at end of file
		var tag = new Id3v1Tag ();
		tag.Title = "Test Song";
		tag.Artist = "Test Artist";

		var tagData = tag.Render ();

		// Create fake audio data + ID3v1 at end
		var audioData = new byte[256];
		var fullData = new byte[audioData.Length + tagData.Length];
		tagData.Span.CopyTo (fullData.AsSpan (audioData.Length));

		var result = Mp3File.Read (fullData);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.File!.Id3v2Tag);
		Assert.IsNotNull (result.File.Id3v1Tag);
		Assert.AreEqual ("Test Song", result.File.Id3v1Tag.Title);
		Assert.AreEqual ("Test Artist", result.File.Id3v1Tag.Artist);
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

		Assert.IsNotNull (result.File?.AudioProperties);
		Assert.AreEqual (44100, result.File.AudioProperties.SampleRate);
		Assert.AreEqual (2, result.File.AudioProperties.Channels);
		Assert.IsTrue (result.File.AudioProperties.IsVbr);
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

		Assert.IsNotNull (result.File?.AudioProperties);
		Assert.AreEqual (44100, result.File.AudioProperties.SampleRate);
		Assert.AreEqual (128, result.File.AudioProperties.Bitrate);
		Assert.IsFalse (result.File.AudioProperties.IsVbr);
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
		Assert.AreEqual (result.File.AudioProperties!.Duration, result.File.Duration);
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

}
