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
/// The same checks run in CI on built files from <see cref="MatroskaFileTests"/>.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Matroska")]
public class MatroskaIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MATROSKA";

	[TestMethod]
	public void SortNames_RoundTripThroughSave_AndClear () =>
		MatroskaSortRoundTrip.VerifyOnCopy (SkipIfNoTestFile (TestFileEnvVar, "Matroska"));

	[TestMethod]
	public Task SortNames_RoundTripThroughSaveAsync_AndClear () =>
		MatroskaSortRoundTrip.VerifyOnCopyAsync (SkipIfNoTestFile (TestFileEnvVar, "Matroska"));
}

/// <summary>
/// Sets, saves, re-reads, clears, and re-reads <c>AlbumArtistSort</c> and <c>ComposerSort</c>
/// on a temporary copy of a Matroska file, checking the parent values never change.
/// </summary>
static class MatroskaSortRoundTrip
{
	public const string AlbumArtistSort = "Band, The";
	public const string ComposerSort = "Bach, Johann Sebastian";

	/// <param name="source">The file to copy. It is never written.</param>
	/// <param name="afterSet">Inspects the saved bytes after the sort names are set.</param>
	public static void VerifyOnCopy (string source, Action<byte[]>? afterSet = null)
	{
		var copy = CopyToTemp (source);
		try {
			var original = MatroskaFile.ReadFromFile (copy);
			Assert.IsTrue (original.IsSuccess, original.Error);
			var before = Snapshot.Of (original.File!.Tag);

			original.File.Tag.AlbumArtistSort = AlbumArtistSort;
			original.File.Tag.ComposerSort = ComposerSort;
			var saved = original.File.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var bytes = File.ReadAllBytes (copy);
			afterSet?.Invoke (bytes);
			var set = MatroskaFile.Read (bytes);
			Assert.IsTrue (set.IsSuccess, set.Error);
			AssertSet (set.File!.Tag, before);

			var reopened = MatroskaFile.ReadFromFile (copy);
			Assert.IsTrue (reopened.IsSuccess, reopened.Error);
			reopened.File!.Tag.AlbumArtistSort = null;
			reopened.File.Tag.ComposerSort = null;
			var clearedSave = reopened.File.SaveToFile ();
			Assert.IsTrue (clearedSave.IsSuccess, clearedSave.Error);

			var cleared = MatroskaFile.ReadFromFile (copy);
			Assert.IsTrue (cleared.IsSuccess, cleared.Error);
			AssertCleared (cleared.File!.Tag, before);
		} finally {
			File.Delete (copy);
		}
	}

	/// <param name="source">The file to copy. It is never written.</param>
	/// <param name="afterSet">Inspects the saved bytes after the sort names are set.</param>
	public static async Task VerifyOnCopyAsync (string source, Action<byte[]>? afterSet = null)
	{
		var copy = CopyToTemp (source);
		try {
			var original = await MatroskaFile.ReadFromFileAsync (copy);
			Assert.IsTrue (original.IsSuccess, original.Error);
			var before = Snapshot.Of (original.File!.Tag);

			original.File.Tag.AlbumArtistSort = AlbumArtistSort;
			original.File.Tag.ComposerSort = ComposerSort;
			var saved = await original.File.SaveToFileAsync ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var set = await MatroskaFile.ReadFromFileAsync (copy);
			Assert.IsTrue (set.IsSuccess, set.Error);
			afterSet?.Invoke (File.ReadAllBytes (copy));
			AssertSet (set.File!.Tag, before);

			set.File.Tag.AlbumArtistSort = null;
			set.File.Tag.ComposerSort = null;
			var clearedSave = await set.File.SaveToFileAsync ();
			Assert.IsTrue (clearedSave.IsSuccess, clearedSave.Error);

			var cleared = await MatroskaFile.ReadFromFileAsync (copy);
			Assert.IsTrue (cleared.IsSuccess, cleared.Error);
			AssertCleared (cleared.File!.Tag, before);
		} finally {
			File.Delete (copy);
		}
	}

	static string CopyToTemp (string source)
	{
		var copy = Path.Combine (Path.GetTempPath (), $"taglibsharp2-sort-{Guid.NewGuid ():N}{Path.GetExtension (source)}");
		File.Copy (source, copy);
		return copy;
	}

	static void AssertSet (MatroskaTag tag, Snapshot before)
	{
		Assert.AreEqual (AlbumArtistSort, tag.AlbumArtistSort);
		Assert.AreEqual (ComposerSort, tag.ComposerSort);
		before.AssertParentsUnchanged (tag);
	}

	static void AssertCleared (MatroskaTag tag, Snapshot before)
	{
		Assert.IsNull (tag.AlbumArtistSort);
		Assert.IsNull (tag.ComposerSort);
		before.AssertParentsUnchanged (tag);
	}

	sealed record Snapshot (string? Title, string? Artist, string? ArtistSort, string[] AlbumArtists, string[] Composers)
	{
		public static Snapshot Of (MatroskaTag tag) =>
			new (tag.Title, tag.Artist, tag.ArtistSort, tag.AlbumArtists, tag.Composers);

		public void AssertParentsUnchanged (MatroskaTag tag)
		{
			Assert.AreEqual (Title, tag.Title);
			Assert.AreEqual (Artist, tag.Artist);
			Assert.AreEqual (ArtistSort, tag.ArtistSort);
			CollectionAssert.AreEqual (AlbumArtists, tag.AlbumArtists);
			CollectionAssert.AreEqual (Composers, tag.Composers);
		}
	}
}
