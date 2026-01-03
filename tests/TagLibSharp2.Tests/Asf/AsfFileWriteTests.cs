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

		var rendered = result.Value.Render (data);

		Assert.IsNotNull (rendered);
		Assert.IsTrue (rendered.Length > 0);
	}

	[TestMethod]
	public void Render_OutputCanBeReparsed ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test Song");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess, reparsed.Error);
	}

	[TestMethod]
	public void Render_PreservesTitle ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Original Title");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Original Title", reparsed.Value.Title);
	}

	[TestMethod]
	public void Render_PreservesArtist ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Original Artist");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Original Artist", reparsed.Value.Artist);
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

		result.Value.Title = "Modified Title";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Modified Title", reparsed.Value.Title);
	}

	[TestMethod]
	public void Render_ModifiedArtist_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Original");
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Artist = "Modified Artist";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Modified Artist", reparsed.Value.Artist);
	}

	[TestMethod]
	public void Render_ModifiedAlbum_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Album = "New Album";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("New Album", reparsed.Value.Album);
	}

	[TestMethod]
	public void Render_AddingMetadataToEmptyFile_Works ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (); // No metadata
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Title = "Added Title";
		result.Value.Artist = "Added Artist";
		result.Value.Album = "Added Album";

		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Added Title", reparsed.Value.Title);
		Assert.AreEqual ("Added Artist", reparsed.Value.Artist);
		Assert.AreEqual ("Added Album", reparsed.Value.Album);
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
		var originalDuration = result.Value.AudioProperties.Duration;

		result.Value.Title = "Changed";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (originalDuration, reparsed.Value.AudioProperties.Duration);
	}

	[TestMethod]
	public void Render_PreservesSampleRate ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (sampleRate: 48000);
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Title = "Changed";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (48000, reparsed.Value.AudioProperties.SampleRate);
	}

	[TestMethod]
	public void Render_PreservesChannels ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (channels: 2);
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Title = "Changed";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (2, reparsed.Value.AudioProperties.Channels);
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

		result.Value.Title = "日本語タイトル";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("日本語タイトル", reparsed.Value.Title);
	}

	[TestMethod]
	public void Render_UnicodeArtist_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Artist = "Café Français";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Café Français", reparsed.Value.Artist);
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

		result.Value.Tag.Year = "2024";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("2024", reparsed.Value.Tag.Year);
	}

	[TestMethod]
	public void Render_Genre_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Tag.Genre = "Rock";
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Rock", reparsed.Value.Tag.Genre);
	}

	[TestMethod]
	public void Render_Track_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Tag.Track = 5;
		var rendered = result.Value.Render (data);
		var reparsed = AsfFile.Read (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (5u, reparsed.Value.Tag.Track);
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

		result.Value.Title = "Modified";
		var writeResult = result.Value.SaveToFile ("/test.wma", mockFs);

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

		result.Value.Title = "Modified Title";
		result.Value.SaveToFile ("/test.wma", mockFs);

		var reread = AsfFile.ReadFromFile ("/test.wma", mockFs);
		Assert.IsTrue (reread.IsSuccess);
		Assert.AreEqual ("Modified Title", reread.Value.Title);
	}

	[TestMethod]
	public void SaveToFile_ToSourcePath_Works ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/source.wma", data);

		var result = AsfFile.ReadFromFile ("/source.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Title = "Updated";
		var writeResult = result.Value.SaveToFile (mockFs);

		Assert.IsTrue (writeResult.IsSuccess, writeResult.Error);

		var reread = AsfFile.ReadFromFile ("/source.wma", mockFs);
		Assert.AreEqual ("Updated", reread.Value.Title);
	}

	[TestMethod]
	public void SaveToFile_NoSourcePath_ReturnsFailure ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data); // No source path set
		Assert.IsTrue (result.IsSuccess);

		var writeResult = result.Value.SaveToFile ();

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

		result.Value.Title = "Async Modified";
		var writeResult = await result.Value.SaveToFileAsync ("/async.wma", mockFs);

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

		result.Value.Title = "Async Title";
		result.Value.Artist = "Async Artist";
		await result.Value.SaveToFileAsync ("/async.wma", mockFs);

		var reread = await AsfFile.ReadFromFileAsync ("/async.wma", mockFs);
		Assert.IsTrue (reread.IsSuccess);
		Assert.AreEqual ("Async Title", reread.Value.Title);
		Assert.AreEqual ("Async Artist", reread.Value.Artist);
	}

	[TestMethod]
	public async Task SaveToFileAsync_ToSourcePath_Works ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/source.wma", data);

		var result = await AsfFile.ReadFromFileAsync ("/source.wma", mockFs);
		Assert.IsTrue (result.IsSuccess);

		result.Value.Title = "Async Updated";
		var writeResult = await result.Value.SaveToFileAsync (mockFs);

		Assert.IsTrue (writeResult.IsSuccess, writeResult.Error);

		var reread = await AsfFile.ReadFromFileAsync ("/source.wma", mockFs);
		Assert.AreEqual ("Async Updated", reread.Value.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_NoSourcePath_ReturnsFailure ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();
		var result = AsfFile.Read (data); // No source path set
		Assert.IsTrue (result.IsSuccess);

		var writeResult = await result.Value.SaveToFileAsync ();

		Assert.IsFalse (writeResult.IsSuccess);
		Assert.IsNotNull (writeResult.Error);
	}
}
