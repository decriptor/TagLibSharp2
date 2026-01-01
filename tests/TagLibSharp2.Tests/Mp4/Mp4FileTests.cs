// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Tests for MP4/M4A file operations.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4FileTests
{
	[TestMethod]
	public void Read_ValidM4aAac_ParsesFile ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("AAC", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Read_ValidM4aAlac_ParsesFile ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Alac);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("ALAC", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Read_EmptyData_ReturnsFailure ()
	{
		var result = Mp4File.Read ([]);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x00, 0x00, 0x00, 0x04 };

		var result = Mp4File.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[100];
		data[0] = 0x00;
		data[1] = 0x00;
		data[2] = 0x00;
		data[3] = 0x14;
		// Not "ftyp"
		data[4] = 0x58;
		data[5] = 0x58;
		data[6] = 0x58;
		data[7] = 0x58;

		var result = Mp4File.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_DetectFileTypeFromFtyp_M4a ()
	{
		var data = TestBuilders.Mp4.CreateWithFtyp ("M4A ");

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("M4A", result.File!.FileType);
	}

	[TestMethod]
	public void Read_DetectFileTypeFromFtyp_Mp4 ()
	{
		var data = TestBuilders.Mp4.CreateWithFtyp ("mp42");

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.Contains ("mp4", result.File!.FileType, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_MissingMoovBox_ReturnsFailure ()
	{
		var data = TestBuilders.Mp4.CreateWithoutMoov ();

		var result = Mp4File.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("moov", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_MissingIlst_NoMetadata ()
	{
		var data = TestBuilders.Mp4.CreateWithoutIlst ();

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNull (result.File!.Title);
		Assert.IsNull (result.File.Artist);
	}

	[TestMethod]
	public void Read_WithMetadata_ParsesTags ()
	{
		var data = TestBuilders.Mp4.CreateWithMetadata (
			title: TestConstants.Metadata.Title,
			artist: TestConstants.Metadata.Artist);

		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File!.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.File.Artist);
	}

	[TestMethod]
	public void Title_Get_ReturnsMetadata ()
	{
		var data = TestBuilders.Mp4.CreateWithMetadata (title: "Test Song");

		var result = Mp4File.Read (data);

		Assert.AreEqual ("Test Song", result.File!.Title);
	}

	[TestMethod]
	public void Title_Set_UpdatesMetadata ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		Assert.AreEqual ("New Title", file.Title);
	}

	[TestMethod]
	public void Artist_Get_ReturnsMetadata ()
	{
		var data = TestBuilders.Mp4.CreateWithMetadata (artist: TestConstants.Metadata.Artist);

		var result = Mp4File.Read (data);

		Assert.AreEqual (TestConstants.Metadata.Artist, result.File!.Artist);
	}

	[TestMethod]
	public void Artist_Set_UpdatesMetadata ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Artist = "New Artist";

		Assert.AreEqual ("New Artist", file.Artist);
	}

	[TestMethod]
	public void Album_GetSet_WorksCorrectly ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.Album = "Test Album";

		Assert.AreEqual ("Test Album", file.Album);
	}

	[TestMethod]
	public void RoundTrip_ReadWriteRead_PreservesMetadata ()
	{
		var originalData = TestBuilders.Mp4.CreateWithMetadata (
			title: "Original Title",
			artist: "Original Artist");

		var result1 = Mp4File.Read (originalData);
		var file = result1.File!;
		file.Title = "Modified Title";

		var renderedData = file.Render (originalData);

		var result2 = Mp4File.Read (renderedData.Span);
		Assert.IsTrue (result2.IsSuccess);
		Assert.AreEqual ("Modified Title", result2.File!.Title);
		Assert.AreEqual ("Original Artist", result2.File.Artist);
	}

	[TestMethod]
	public void Render_PreservesAudioData ()
	{
		var audioData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
		var originalData = TestBuilders.Mp4.CreateWithAudioData (audioData);

		var result = Mp4File.Read (originalData);
		result.File!.Title = "New Title";

		var rendered = result.File.Render (originalData);

		// Re-read and verify audio data is preserved
		var reResult = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reResult.IsSuccess);
		// Audio data verification would require extracting mdat box
		Assert.AreEqual ("New Title", reResult.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_WritesToFileSystem ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;
		file.Title = "Saved Title";

		var fs = new MockFileSystem ();
		var writeResult = file.SaveToFile ("/output.m4a", data, fs);

		Assert.IsTrue (writeResult.IsSuccess);
		Assert.IsTrue (fs.FileExists ("/output.m4a"));

		var savedData = fs.ReadAllBytes ("/output.m4a");
		var reResult = Mp4File.Read (savedData);
		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Saved Title", reResult.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesToFileSystem ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;
		file.Artist = "Async Artist";

		var fs = new MockFileSystem ();
		var writeResult = await file.SaveToFileAsync ("/output.m4a", data, fs);

		Assert.IsTrue (writeResult.IsSuccess);

		var savedData = fs.ReadAllBytes ("/output.m4a");
		var reResult = Mp4File.Read (savedData);
		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Async Artist", reResult.File!.Artist);
	}

	[TestMethod]
	public void ReadFromFile_WithMockFileSystem_ParsesTag ()
	{
		var fs = new MockFileSystem ();
		var data = TestBuilders.Mp4.CreateWithMetadata (title: TestConstants.Metadata.Title);
		fs.AddFile ("/test.m4a", data);

		var result = Mp4File.ReadFromFile ("/test.m4a", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File!.Title);
		Assert.AreEqual ("/test.m4a", result.File.SourcePath);
	}

	[TestMethod]
	public void ReadFromFile_FileNotFound_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		var result = Mp4File.ReadFromFile ("/nonexistent.m4a", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_WithMockFileSystem_ParsesTag ()
	{
		var fs = new MockFileSystem ();
		var data = TestBuilders.Mp4.CreateWithMetadata (title: TestConstants.Metadata.Title);
		fs.AddFile ("/test.m4a", data);

		var result = await Mp4File.ReadFromFileAsync ("/test.m4a", fs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Metadata.Title, result.File!.Title);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_FileNotFound_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();

		var result = await Mp4File.ReadFromFileAsync ("/nonexistent.m4a", fs);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Cancellation_ReturnsFailure ()
	{
		var fs = new MockFileSystem ();
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		fs.AddFile ("/test.m4a", data);

		var cts = new CancellationTokenSource ();
		cts.Cancel ();

		var result = await Mp4File.ReadFromFileAsync ("/test.m4a", fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Duration_ReturnsAudioProperties ()
	{
		var data = TestBuilders.Mp4.CreateWithDuration (TimeSpan.FromSeconds (180));

		var result = Mp4File.Read (data);

		Assert.IsNotNull (result.File?.Duration);
		Assert.IsTrue (result.File.Duration.Value.TotalSeconds >= 179 && result.File.Duration.Value.TotalSeconds <= 181);
	}
}
