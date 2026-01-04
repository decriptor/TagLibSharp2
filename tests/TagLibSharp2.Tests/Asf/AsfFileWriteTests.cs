// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfFileWriteTests
{
	// ═══════════════════════════════════════════════════════════════
	// Render Basic Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_ReturnsValidAsfData ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.File!.Render (data);

		Assert.IsNotNull (rendered);
		Assert.IsTrue (rendered.Length > 0);
	}

	[TestMethod]
	public void Render_OutputCanBeReparsed ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test Song");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess, reparsed.Error);
	}

	[TestMethod]
	public void Render_PreservesTitle ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Original Title");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Original Title", reparsed.File!.Title);
	}

	[TestMethod]
	public void Render_PreservesArtist ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Original Artist");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Original Artist", reparsed.File!.Artist);
	}

	// ═══════════════════════════════════════════════════════════════
	// Roundtrip Modification Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_ModifiedTitle_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Original");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Modified Title";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Modified Title", reparsed.File!.Title);
	}

	[TestMethod]
	public void Render_ModifiedArtist_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Original");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Artist = "Modified Artist";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Modified Artist", reparsed.File!.Artist);
	}

	[TestMethod]
	public void Render_ModifiedAlbum_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Album = "New Album";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("New Album", reparsed.File!.Album);
	}

	[TestMethod]
	public void Render_AddingMetadataToEmptyFile_Works ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (); // No metadata
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Added Title";
		result.File!.Artist = "Added Artist";
		result.File!.Album = "Added Album";

		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Added Title", reparsed.File!.Title);
		Assert.AreEqual ("Added Artist", reparsed.File!.Artist);
		Assert.AreEqual ("Added Album", reparsed.File!.Album);
	}

	// ═══════════════════════════════════════════════════════════════
	// Audio Properties Preservation Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_PreservesDuration ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (durationMs: 180000);
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);
		var originalDuration = result.File!.AudioProperties.Duration;

		result.File!.Title = "Changed";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (originalDuration, reparsed.File!.AudioProperties.Duration);
	}

	[TestMethod]
	public void Render_PreservesSampleRate ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (sampleRate: 48000);
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Changed";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (48000, reparsed.File!.AudioProperties.SampleRate);
	}

	[TestMethod]
	public void Render_PreservesChannels ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (channels: 2);
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Changed";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (2, reparsed.File!.AudioProperties.Channels);
	}

	// ═══════════════════════════════════════════════════════════════
	// Unicode Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_UnicodeTitle_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "日本語タイトル";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("日本語タイトル", reparsed.File!.Title);
	}

	[TestMethod]
	public void Render_UnicodeArtist_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Artist = "Café Français";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Café Français", reparsed.File!.Artist);
	}

	// ═══════════════════════════════════════════════════════════════
	// Extended Metadata Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_Year_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.Year = "2024";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("2024", reparsed.File!.Tag.Year);
	}

	[TestMethod]
	public void Render_Genre_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.Genre = "Rock";
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Rock", reparsed.File!.Tag.Genre);
	}

	[TestMethod]
	public void Render_Track_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Tag.Track = 5;
		var rendered = result.File!.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (5u, reparsed.File!.Tag.Track);
	}

	// ═══════════════════════════════════════════════════════════════
	// SaveToFile Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void SaveToFile_WritesFile ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Original");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wma", data);

		var result = AsfFile.ReadFromFile ("/test.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Modified";
		var writeResult = result.File!.SaveToFile ("/test.wma", mockFs);

		Assert.IsTrue (writeResult.IsSuccess, writeResult.Error);
	}

	[TestMethod]
	public void SaveToFile_ModificationsPreserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Original");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.wma", data);

		var result = AsfFile.ReadFromFile ("/test.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Modified Title";
		result.File!.SaveToFile ("/test.wma", mockFs);

		var reread = AsfFile.ReadFromFile ("/test.wma", mockFs);
		Assert.IsTrue (reread.IsSuccess);
		Assert.AreEqual ("Modified Title", reread.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_ToSourcePath_Works ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/source.wma", data);

		var result = AsfFile.ReadFromFile ("/source.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Updated";
		var writeResult = result.File!.SaveToFile (mockFs);

		Assert.IsTrue (writeResult.IsSuccess, writeResult.Error);

		var reread = AsfFile.ReadFromFile ("/source.wma", mockFs);
		Assert.AreEqual ("Updated", reread.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_NoSourcePath_ReturnsFailure ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data); // No source path set
		Assert.IsTrue (result.IsSuccess);

		var writeResult = result.File!.SaveToFile ();

		Assert.IsFalse (writeResult.IsSuccess);
		Assert.IsNotNull (writeResult.Error);
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesFile ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Original");
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/async.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/async.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Async Modified";
		var writeResult = await result.File!.SaveToFileAsync ("/async.wma", mockFs);

		Assert.IsTrue (writeResult.IsSuccess, writeResult.Error);
	}

	[TestMethod]
	public async Task SaveToFileAsync_ModificationsPreserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/async.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/async.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Async Title";
		result.File!.Artist = "Async Artist";
		await result.File!.SaveToFileAsync ("/async.wma", mockFs);

		var reread = await AsfFile.ReadFromFileAsync ("/async.wma", mockFs);
		Assert.IsTrue (reread.IsSuccess);
		Assert.AreEqual ("Async Title", reread.File!.Title);
		Assert.AreEqual ("Async Artist", reread.File!.Artist);
	}

	[TestMethod]
	public async Task SaveToFileAsync_ToSourcePath_Works ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/source.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/source.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Title = "Async Updated";
		var writeResult = await result.File!.SaveToFileAsync (mockFs);

		Assert.IsTrue (writeResult.IsSuccess, writeResult.Error);

		var reread = await AsfFile.ReadFromFileAsync ("/source.wma", mockFs);
		Assert.AreEqual ("Async Updated", reread.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data); // No source path set
		Assert.IsTrue (result.IsSuccess);

		var writeResult = await result.File!.SaveToFileAsync ();

		Assert.IsFalse (writeResult.IsSuccess);
		Assert.IsNotNull (writeResult.Error);
	}
}
