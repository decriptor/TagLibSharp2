// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class WavFileTests
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
	public void Read_ValidWav_ReturnsWavFile ()
	{
		var result = WavFile.Read (CreateMinimalWav ());

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsTrue (result.File.IsValid);
	}

	[TestMethod]
	public void Read_InvalidData_ReturnsFailure ()
	{
		var result = WavFile.Read (new BinaryData ([1, 2, 3, 4, 5]));
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
	}

	[TestMethod]
	public void TryRead_ValidWav_ReturnsTrueWithFile ()
	{
		Assert.IsTrue (WavFile.TryRead (CreateMinimalWav (), out var wav));
		Assert.IsNotNull (wav);
		Assert.IsTrue (wav.IsValid);
	}

	[TestMethod]
	public void TryRead_InvalidData_ReturnsFalse ()
	{
		Assert.IsFalse (WavFile.TryRead (new BinaryData ([1, 2, 3, 4, 5]), out var wav));
		Assert.IsNull (wav);
	}

	[TestMethod]
	public void Read_ParsesAudioProperties ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;

		Assert.IsNotNull (wav?.Properties);
		Assert.AreEqual (44100, wav.Properties.SampleRate);
		Assert.AreEqual (2, wav.Properties.Channels);
		Assert.AreEqual (16, wav.Properties.BitsPerSample);
	}

	[TestMethod]
	public void Title_PrefersId3v2OverInfo ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Info Title" };
		wav.Id3v2Tag = new Id3v2Tag { Title = "ID3 Title" };

		Assert.AreEqual ("ID3 Title", wav.Title);
	}

	[TestMethod]
	public void Title_FallsBackToInfo ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Info Title" };

		Assert.AreEqual ("Info Title", wav.Title);
	}

	[TestMethod]
	public void Render_ProducesValidWav ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		var rendered = wav.Render ();

		Assert.AreEqual ((byte)'R', rendered[0]);
		Assert.AreEqual ((byte)'W', rendered[8]);
	}

	[TestMethod]
	public void Render_WithInfoTag_IncludesListChunk ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Test" };

		var rendered = wav.Render ();
		var roundTripped = WavFile.Read (rendered).File;

		Assert.IsNotNull (roundTripped?.InfoTag);
		Assert.AreEqual ("Test", roundTripped.InfoTag.Title);
	}

	[TestMethod]
	public void Render_WithId3v2Tag_IncludesId3Chunk ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag { Title = "ID3 Title" };

		var rendered = wav.Render ();
		var roundTripped = WavFile.Read (rendered).File;

		Assert.IsNotNull (roundTripped?.Id3v2Tag);
		Assert.AreEqual ("ID3 Title", roundTripped.Id3v2Tag.Title);
	}

	[TestMethod]
	public void ReadFromFile_NonExistentFile_ReturnsFailure ()
	{
		var result = WavFile.ReadFromFile ("/nonexistent/path/file.wav");
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsFailure ()
	{
		var result = await WavFile.ReadFromFileAsync ("/nonexistent/path/file.wav");
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.File);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void SaveToFile_WithMockFileSystem_WritesData ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		var mockFs = new MockFileSystem ();
		var result = wav.SaveToFile ("/test/output.wav", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/test/output.wav"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithMockFileSystem_WritesData ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		var mockFs = new MockFileSystem ();
		var result = await wav.SaveToFileAsync ("/test/output.wav", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/test/output.wav"));
	}

	[TestMethod]
	public void Render_WithFactChunk_PreservesFactChunk ()
	{
		var wav = WavFile.Read (CreateWavWithFactChunk ()).File;
		Assert.IsNotNull (wav);

		// Modify a tag to force re-render
		wav.InfoTag = new RiffInfoTag { Title = "Modified" };

		var rendered = wav.Render ();
		var roundTripped = WavFile.Read (rendered).File;

		Assert.IsNotNull (roundTripped);
		// Verify fact chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("fact"));
	}

	[TestMethod]
	public void Render_WithCueChunk_PreservesCueChunk ()
	{
		var wav = WavFile.Read (CreateWavWithCueChunk ()).File;
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Modified" };

		var rendered = wav.Render ();

		// Verify cue chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("cue "));
	}

	[TestMethod]
	public void Render_WithSmplChunk_PreservesSmplChunk ()
	{
		var wav = WavFile.Read (CreateWavWithSmplChunk ()).File;
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag { Title = "Modified" };

		var rendered = wav.Render ();

		// Verify smpl chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("smpl"));
	}

	[TestMethod]
	public void Render_WithMultipleUnknownChunks_PreservesAll ()
	{
		var wav = WavFile.Read (CreateWavWithMultipleUnknownChunks ()).File;
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Artist = "New Artist" };

		var rendered = wav.Render ();
		var content = rendered.ToStringLatin1 ();

		// Verify all chunks were preserved
		Assert.IsTrue (content.Contains ("fact"));
		Assert.IsTrue (content.Contains ("cue "));
	}

	[TestMethod]
	public void Render_AdtlList_PreservesAdtlList ()
	{
		var wav = WavFile.Read (CreateWavWithAdtlList ()).File;
		Assert.IsNotNull (wav);

		var rendered = wav.Render ();

		AssertAdtlListPreserved (rendered);
	}

	[TestMethod]
	public void Render_AdtlList_PreservedWhenInfoTagRewritten ()
	{
		var wav = WavFile.Read (CreateWavWithAdtlList ()).File;
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Rewritten" };

		var rendered = wav.Render ();
		AssertAdtlListPreserved (rendered);

		var roundTripped = WavFile.Read (rendered).File;
		Assert.AreEqual ("Rewritten", roundTripped?.InfoTag?.Title);
	}

	static void AssertAdtlListPreserved (BinaryData rendered)
	{
		Assert.IsTrue (RiffFile.TryParse (rendered, out var riff));

		var found = false;
		foreach (var chunk in riff.GetChunks ("LIST")) {
			if (chunk.Data.Length < 4 || chunk.Data.Slice (0, 4).ToStringLatin1 () != "adtl")
				continue;

			Assert.IsTrue (chunk.Data.ToStringLatin1 ().Contains ("cue-labels"));
			found = true;
		}

		Assert.IsTrue (found, "LIST adtl was dropped");
	}

	static BinaryData CreateWavWithFactChunk ()
	{
		using var builder = new BinaryDataBuilder (512);

		// RIFF header - size will be calculated
		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (52); // 4 (WAVE) + 24 (fmt) + 12 (fact) + 8 (data) + 4 (data size placeholder)
		builder.AddStringLatin1 ("WAVE");

		// fmt chunk (16 bytes data)
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);    // PCM
		builder.AddUInt16LE (2);    // stereo
		builder.AddUInt32LE (44100); // sample rate
		builder.AddUInt32LE (176400); // byte rate
		builder.AddUInt16LE (4);    // block align
		builder.AddUInt16LE (16);   // bits per sample

		// fact chunk (4 bytes data - sample count)
		builder.AddStringLatin1 ("fact");
		builder.AddUInt32LE (4);
		builder.AddUInt32LE (88200); // 2 seconds at 44100 Hz

		// data chunk (empty)
		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateWavWithCueChunk ()
	{
		using var builder = new BinaryDataBuilder (512);

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (60); // size will need adjustment
		builder.AddStringLatin1 ("WAVE");

		// fmt chunk
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);
		builder.AddUInt16LE (2);
		builder.AddUInt32LE (44100);
		builder.AddUInt32LE (176400);
		builder.AddUInt16LE (4);
		builder.AddUInt16LE (16);

		// cue chunk (minimal - 4 byte count + 24 byte cue point)
		builder.AddStringLatin1 ("cue ");
		builder.AddUInt32LE (28);  // size: 4 (count) + 24 (one cue point)
		builder.AddUInt32LE (1);   // 1 cue point
								   // Cue point: ID(4) + Position(4) + ChunkID(4) + ChunkStart(4) + BlockStart(4) + SampleOffset(4)
		builder.AddUInt32LE (1);   // ID
		builder.AddUInt32LE (0);   // Position
		builder.AddStringLatin1 ("data"); // Chunk ID
		builder.AddUInt32LE (0);   // Chunk start
		builder.AddUInt32LE (0);   // Block start
		builder.AddUInt32LE (44100); // Sample offset (1 second mark)

		// data chunk
		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateWavWithSmplChunk ()
	{
		using var builder = new BinaryDataBuilder (512);

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (80);
		builder.AddStringLatin1 ("WAVE");

		// fmt chunk
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);
		builder.AddUInt16LE (2);
		builder.AddUInt32LE (44100);
		builder.AddUInt32LE (176400);
		builder.AddUInt16LE (4);
		builder.AddUInt16LE (16);

		// smpl chunk (36 bytes minimum)
		builder.AddStringLatin1 ("smpl");
		builder.AddUInt32LE (36);
		builder.AddUInt32LE (0);  // Manufacturer
		builder.AddUInt32LE (0);  // Product
		builder.AddUInt32LE (22675); // Sample period (1/44100 in nanoseconds)
		builder.AddUInt32LE (60); // MIDI unity note (middle C)
		builder.AddUInt32LE (0);  // MIDI pitch fraction
		builder.AddUInt32LE (0);  // SMPTE format
		builder.AddUInt32LE (0);  // SMPTE offset
		builder.AddUInt32LE (0);  // Num sample loops
		builder.AddUInt32LE (0);  // Sampler data

		// data chunk
		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateWavWithMultipleUnknownChunks ()
	{
		using var builder = new BinaryDataBuilder (512);

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (68);
		builder.AddStringLatin1 ("WAVE");

		// fmt chunk
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);
		builder.AddUInt16LE (2);
		builder.AddUInt32LE (44100);
		builder.AddUInt32LE (176400);
		builder.AddUInt16LE (4);
		builder.AddUInt16LE (16);

		// fact chunk
		builder.AddStringLatin1 ("fact");
		builder.AddUInt32LE (4);
		builder.AddUInt32LE (88200);

		// cue chunk (minimal)
		builder.AddStringLatin1 ("cue ");
		builder.AddUInt32LE (4);
		builder.AddUInt32LE (0); // 0 cue points

		// data chunk
		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateWavWithAdtlList ()
	{
		using var body = new BinaryDataBuilder (128);
		body.AddStringLatin1 ("WAVE");

		body.AddStringLatin1 ("fmt ");
		body.AddUInt32LE (16);
		body.AddUInt16LE (1);
		body.AddUInt16LE (1);
		body.AddUInt32LE (44100);
		body.AddUInt32LE (44100);
		body.AddUInt16LE (1);
		body.AddUInt16LE (8);

		body.AddStringLatin1 ("data");
		body.AddUInt32LE (4);
		body.Add (0x01, 0x02, 0x03, 0x04);

		// LIST whose data starts with "adtl", plus a cue-label payload.
		body.AddStringLatin1 ("LIST");
		body.AddUInt32LE (14);
		body.AddStringLatin1 ("adtl");
		body.AddStringLatin1 ("cue-labels");

		var bodyData = body.ToBinaryData ();
		using var file = new BinaryDataBuilder (8 + bodyData.Length);
		file.AddStringLatin1 ("RIFF");
		file.AddUInt32LE ((uint)bodyData.Length);
		file.Add (bodyData);
		return file.ToBinaryData ();
	}

	[TestMethod]
	[TestCategory ("BWF")]
	public void Read_WithBextChunk_ParsesBextTag ()
	{
		var wav = WavFile.Read (CreateWavWithBextChunk ()).File;

		Assert.IsNotNull (wav);
		Assert.IsNotNull (wav.BextTag);
		Assert.AreEqual ("Test Description", wav.BextTag.Description);
		Assert.AreEqual ("TestOriginator", wav.BextTag.Originator);
	}

	[TestMethod]
	[TestCategory ("BWF")]
	public void Render_WithBextTag_RoundTrips ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;
		Assert.IsNotNull (wav);

		wav.BextTag = new BextTag {
			Description = "Broadcast Description",
			Originator = "ProTools",
			OriginationDate = "2025-12-28",
			OriginationTime = "10:30:00",
			TimeReference = 48000
		};

		var rendered = wav.Render ();
		var roundTripped = WavFile.Read (rendered).File;

		Assert.IsNotNull (roundTripped?.BextTag);
		Assert.AreEqual ("Broadcast Description", roundTripped.BextTag.Description);
		Assert.AreEqual ("ProTools", roundTripped.BextTag.Originator);
		Assert.AreEqual ("2025-12-28", roundTripped.BextTag.OriginationDate);
		Assert.AreEqual (48000UL, roundTripped.BextTag.TimeReference);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void Read_WithExtensibleFormat_ParsesExtendedProperties ()
	{
		var wav = WavFile.Read (CreateWavWithExtensibleFormat ()).File;

		Assert.IsNotNull (wav);
		Assert.IsNotNull (wav.ExtendedProperties);
		Assert.AreEqual (6, wav.ExtendedProperties.Value.Channels);
		Assert.AreEqual (WavSubFormat.Pcm, wav.ExtendedProperties.Value.SubFormat);
		Assert.AreEqual (0x3Fu, wav.ExtendedProperties.Value.ChannelMask);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void Read_StandardFormat_NoExtendedProperties ()
	{
		var wav = WavFile.Read (CreateMinimalWav ()).File;

		Assert.IsNotNull (wav);
		Assert.IsNull (wav.ExtendedProperties);
	}

	static BinaryData CreateWavWithBextChunk ()
	{
		using var builder = new BinaryDataBuilder (1024);

		var bextData = new byte[BextTag.MinimumSize];
		// Write description at offset 0
		var desc = System.Text.Encoding.ASCII.GetBytes ("Test Description");
		Array.Copy (desc, bextData, desc.Length);
		// Write originator at offset 256
		var orig = System.Text.Encoding.ASCII.GetBytes ("TestOriginator");
		Array.Copy (orig, 0, bextData, 256, orig.Length);

		var bextSize = BextTag.MinimumSize;
		var riffSize = 4 + 24 + 8 + bextSize + 8;

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE ((uint)riffSize);
		builder.AddStringLatin1 ("WAVE");

		// fmt chunk
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (16);
		builder.AddUInt16LE (1);
		builder.AddUInt16LE (2);
		builder.AddUInt32LE (44100);
		builder.AddUInt32LE (176400);
		builder.AddUInt16LE (4);
		builder.AddUInt16LE (16);

		// bext chunk
		builder.AddStringLatin1 ("bext");
		builder.AddUInt32LE ((uint)bextSize);
		builder.Add (bextData);

		// data chunk
		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}

	static BinaryData CreateWavWithExtensibleFormat ()
	{
		using var builder = new BinaryDataBuilder (512);

		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (60);
		builder.AddStringLatin1 ("WAVE");

		// fmt chunk (40 bytes for WAVEFORMATEXTENSIBLE)
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (40);
		builder.AddUInt16LE (0xFFFE); // Extensible
		builder.AddUInt16LE (6);      // 6 channels (5.1)
		builder.AddUInt32LE (48000);  // Sample rate
		builder.AddUInt32LE (576000); // Byte rate (48000 * 6 * 2)
		builder.AddUInt16LE (12);     // Block align (6 * 2)
		builder.AddUInt16LE (16);     // Bits per sample
		builder.AddUInt16LE (22);     // cbSize
		builder.AddUInt16LE (16);     // Valid bits per sample
		builder.AddUInt32LE (0x3F);   // Channel mask (5.1: FL|FR|FC|LFE|BL|BR)
									  // SubFormat GUID for PCM
		byte[] subFormat = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
			0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];
		builder.Add (subFormat);

		// data chunk
		builder.AddStringLatin1 ("data");
		builder.AddUInt32LE (0);

		return builder.ToBinaryData ();
	}
}
