// Copyright (c) 2025 Stephen Shaw and contributors

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
	public void ReadFromData_ValidWav_ReturnsWavFile ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());

		Assert.IsNotNull (wav);
		Assert.IsTrue (wav.IsValid);
	}

	[TestMethod]
	public void ReadFromData_InvalidData_ReturnsNull ()
	{
		var wav = WavFile.ReadFromData (new BinaryData ([1, 2, 3, 4, 5]));
		Assert.IsNull (wav);
	}

	[TestMethod]
	public void ReadFromData_ParsesAudioProperties ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());

		Assert.IsNotNull (wav?.Properties);
		Assert.AreEqual (44100, wav.Properties.SampleRate);
		Assert.AreEqual (2, wav.Properties.Channels);
		Assert.AreEqual (16, wav.Properties.BitsPerSample);
	}

	[TestMethod]
	public void Title_PrefersId3v2OverInfo ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Info Title" };
		wav.Id3v2Tag = new Id3v2Tag { Title = "ID3 Title" };

		Assert.AreEqual ("ID3 Title", wav.Title);
	}

	[TestMethod]
	public void Title_FallsBackToInfo ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Info Title" };

		Assert.AreEqual ("Info Title", wav.Title);
	}

	[TestMethod]
	public void Render_ProducesValidWav ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		var rendered = wav.Render ();

		Assert.AreEqual ((byte)'R', rendered[0]);
		Assert.AreEqual ((byte)'W', rendered[8]);
	}

	[TestMethod]
	public void Render_WithInfoTag_IncludesListChunk ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Test" };

		var rendered = wav.Render ();
		var roundTripped = WavFile.ReadFromData (rendered);

		Assert.IsNotNull (roundTripped?.InfoTag);
		Assert.AreEqual ("Test", roundTripped.InfoTag.Title);
	}

	[TestMethod]
	public void Render_WithId3v2Tag_IncludesId3Chunk ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag { Title = "ID3 Title" };

		var rendered = wav.Render ();
		var roundTripped = WavFile.ReadFromData (rendered);

		Assert.IsNotNull (roundTripped?.Id3v2Tag);
		Assert.AreEqual ("ID3 Title", roundTripped.Id3v2Tag.Title);
	}

	[TestMethod]
	public void ReadFromFile_NonExistentFile_ReturnsNull ()
	{
		var wav = WavFile.ReadFromFile ("/nonexistent/path/file.wav");
		Assert.IsNull (wav);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_NonExistentFile_ReturnsNull ()
	{
		var wav = await WavFile.ReadFromFileAsync ("/nonexistent/path/file.wav");
		Assert.IsNull (wav);
	}

	[TestMethod]
	public void SaveToFile_WithMockFileSystem_WritesData ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		var mockFs = new MockFileSystem ();
		var result = wav.SaveToFile ("/test/output.wav", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/test/output.wav"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_WithMockFileSystem_WritesData ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		var mockFs = new MockFileSystem ();
		var result = await wav.SaveToFileAsync ("/test/output.wav", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (mockFs.FileExists ("/test/output.wav"));
	}

	[TestMethod]
	public void Render_WithFactChunk_PreservesFactChunk ()
	{
		var wav = WavFile.ReadFromData (CreateWavWithFactChunk ());
		Assert.IsNotNull (wav);

		// Modify a tag to force re-render
		wav.InfoTag = new RiffInfoTag { Title = "Modified" };

		var rendered = wav.Render ();
		var roundTripped = WavFile.ReadFromData (rendered);

		Assert.IsNotNull (roundTripped);
		// Verify fact chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("fact"));
	}

	[TestMethod]
	public void Render_WithCueChunk_PreservesCueChunk ()
	{
		var wav = WavFile.ReadFromData (CreateWavWithCueChunk ());
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Title = "Modified" };

		var rendered = wav.Render ();

		// Verify cue chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("cue "));
	}

	[TestMethod]
	public void Render_WithSmplChunk_PreservesSmplChunk ()
	{
		var wav = WavFile.ReadFromData (CreateWavWithSmplChunk ());
		Assert.IsNotNull (wav);

		wav.Id3v2Tag = new Id3v2Tag { Title = "Modified" };

		var rendered = wav.Render ();

		// Verify smpl chunk was preserved
		Assert.IsTrue (rendered.ToStringLatin1 ().Contains ("smpl"));
	}

	[TestMethod]
	public void Render_WithMultipleUnknownChunks_PreservesAll ()
	{
		var wav = WavFile.ReadFromData (CreateWavWithMultipleUnknownChunks ());
		Assert.IsNotNull (wav);

		wav.InfoTag = new RiffInfoTag { Artist = "New Artist" };

		var rendered = wav.Render ();
		var content = rendered.ToStringLatin1 ();

		// Verify all chunks were preserved
		Assert.IsTrue (content.Contains ("fact"));
		Assert.IsTrue (content.Contains ("cue "));
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

	[TestMethod]
	[TestCategory ("BWF")]
	public void ReadFromData_WithBextChunk_ParsesBextTag ()
	{
		var wav = WavFile.ReadFromData (CreateWavWithBextChunk ());

		Assert.IsNotNull (wav);
		Assert.IsNotNull (wav.BextTag);
		Assert.AreEqual ("Test Description", wav.BextTag.Description);
		Assert.AreEqual ("TestOriginator", wav.BextTag.Originator);
	}

	[TestMethod]
	[TestCategory ("BWF")]
	public void Render_WithBextTag_RoundTrips ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());
		Assert.IsNotNull (wav);

		wav.BextTag = new BextTag {
			Description = "Broadcast Description",
			Originator = "ProTools",
			OriginationDate = "2025-12-28",
			OriginationTime = "10:30:00",
			TimeReference = 48000
		};

		var rendered = wav.Render ();
		var roundTripped = WavFile.ReadFromData (rendered);

		Assert.IsNotNull (roundTripped?.BextTag);
		Assert.AreEqual ("Broadcast Description", roundTripped.BextTag.Description);
		Assert.AreEqual ("ProTools", roundTripped.BextTag.Originator);
		Assert.AreEqual ("2025-12-28", roundTripped.BextTag.OriginationDate);
		Assert.AreEqual (48000UL, roundTripped.BextTag.TimeReference);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ReadFromData_WithExtensibleFormat_ParsesExtendedProperties ()
	{
		var wav = WavFile.ReadFromData (CreateWavWithExtensibleFormat ());

		Assert.IsNotNull (wav);
		Assert.IsNotNull (wav.ExtendedProperties);
		Assert.AreEqual (6, wav.ExtendedProperties.Value.Channels);
		Assert.AreEqual (WavSubFormat.Pcm, wav.ExtendedProperties.Value.SubFormat);
		Assert.AreEqual (0x3Fu, wav.ExtendedProperties.Value.ChannelMask);
	}

	[TestMethod]
	[TestCategory ("WAVEFORMATEXTENSIBLE")]
	public void ReadFromData_StandardFormat_NoExtendedProperties ()
	{
		var wav = WavFile.ReadFromData (CreateMinimalWav ());

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
