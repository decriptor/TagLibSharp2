// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Integration tests that require a real Matroska or WebM file.
/// </summary>
/// <remarks>
/// These tests are skipped by default. To run them:
/// TAGLIBSHARP2_TEST_MATROSKA=/path/to/file.mkv dotnet test --filter "TestCategory=Integration"
/// The file is copied to a temporary path; the original is never written.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Matroska")]
public class MatroskaIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MATROSKA";

	[TestMethod]
	public void SortNames_RoundTripThroughSave_AndClear ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var copy = Path.Combine (Path.GetTempPath (), $"taglibsharp2-sort-{Guid.NewGuid ():N}{Path.GetExtension (path)}");
		File.Copy (path, copy);
		try {
			var original = MatroskaFile.ReadFromFile (copy);
			Assert.IsTrue (original.IsSuccess, original.Error);
			var before = original.File!.Tag;
			var title = before.Title;
			var artist = before.Artist;
			var albumArtists = before.AlbumArtists;
			var composers = before.Composers;
			var artistSort = before.ArtistSort;

			before.AlbumArtistSort = "Band, The";
			before.ComposerSort = "Bach, Johann Sebastian";
			Assert.IsTrue (original.File.SaveToFile ().IsSuccess);

			var saved = MatroskaFile.ReadFromFile (copy);
			Assert.IsTrue (saved.IsSuccess, saved.Error);
			var tag = saved.File!.Tag;
			Assert.AreEqual ("Band, The", tag.AlbumArtistSort);
			Assert.AreEqual ("Bach, Johann Sebastian", tag.ComposerSort);
			Assert.AreEqual (title, tag.Title);
			Assert.AreEqual (artist, tag.Artist);
			Assert.AreEqual (artistSort, tag.ArtistSort);
			CollectionAssert.AreEqual (albumArtists, tag.AlbumArtists);
			CollectionAssert.AreEqual (composers, tag.Composers);

			tag.AlbumArtistSort = null;
			tag.ComposerSort = null;
			Assert.IsTrue (saved.File.SaveToFile ().IsSuccess);

			var cleared = MatroskaFile.ReadFromFile (copy);
			Assert.IsTrue (cleared.IsSuccess, cleared.Error);
			Assert.IsNull (cleared.File!.Tag.AlbumArtistSort);
			Assert.IsNull (cleared.File.Tag.ComposerSort);
			Assert.AreEqual (title, cleared.File.Tag.Title);
			CollectionAssert.AreEqual (composers, cleared.File.Tag.Composers);
		} finally {
			File.Delete (copy);
		}
	}

	[TestMethod]
	public async Task SortNames_RoundTripThroughSaveAsync ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var copy = Path.Combine (Path.GetTempPath (), $"taglibsharp2-sort-{Guid.NewGuid ():N}{Path.GetExtension (path)}");
		File.Copy (path, copy);
		try {
			var original = await MatroskaFile.ReadFromFileAsync (copy);
			Assert.IsTrue (original.IsSuccess, original.Error);
			var composers = original.File!.Tag.Composers;
			original.File.Tag.AlbumArtistSort = "Band, The";
			original.File.Tag.ComposerSort = "Bach, Johann Sebastian";
			Assert.IsTrue ((await original.File.SaveToFileAsync ()).IsSuccess);

			var saved = await MatroskaFile.ReadFromFileAsync (copy);
			Assert.IsTrue (saved.IsSuccess, saved.Error);
			Assert.AreEqual ("Band, The", saved.File!.Tag.AlbumArtistSort);
			Assert.AreEqual ("Bach, Johann Sebastian", saved.File.Tag.ComposerSort);
			CollectionAssert.AreEqual (composers, saved.File.Tag.Composers);
		} finally {
			File.Delete (copy);
		}
	}
}
