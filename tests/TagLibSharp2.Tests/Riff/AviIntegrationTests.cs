// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

/// <summary>
/// Integration tests that need a real AVI file.
/// </summary>
/// <remarks>
/// Skipped by default. To run them, point TAGLIBSHARP2_TEST_AVI at an AVI file:
/// <c>TAGLIBSHARP2_TEST_AVI=/path/to/clip.avi dotnet test --filter "TestCategory=Integration&amp;TestCategory=Avi"</c>.
/// The file is only read; saves are rendered in memory.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Avi")]
public class AviIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_AVI";

	[TestMethod]
	public void Read_RealAvi_ReadsStreamsAndDuration ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "AVI");

		var result = MediaFile.Read (path);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Avi, result.Format);
		var file = result.GetFileAs<AviFile> ()!;
		Console.WriteLine ($"Duration {file.Duration}, video {file.VideoProperties}, audio {file.AudioProperties}");
		foreach (var id in file.InfoTag.FieldIds)
			Console.WriteLine ($"  {id} = {file.InfoTag.GetField (id)}");
		Assert.IsGreaterThan (TimeSpan.Zero, file.Duration);
		Assert.IsTrue (file.VideoProperties is { IsValid: true } || file.AudioProperties is not null);
		var loaded = AviFile.Read (File.ReadAllBytes (path)).File!;
		Assert.AreEqual (loaded.Duration, file.Duration, "A stream read matches a byte read.");
		Assert.AreEqual (loaded.VideoProperties, file.VideoProperties);
	}

	[TestMethod]
	public void Save_RealAvi_UnchangedIsByteIdentical ()
	{
		var original = File.ReadAllBytes (SkipIfNoTestFile (TestFileEnvVar, "AVI"));
		var file = AviFile.Read (original).File!;

		Assert.IsTrue (file.Render (original).Span.SequenceEqual (original));
	}

	[TestMethod]
	public void Save_RealAvi_ShortTitleKeepsMoviInPlace ()
	{
		var original = File.ReadAllBytes (SkipIfNoTestFile (TestFileEnvVar, "AVI"));
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "TagLibSharp2";

		var saved = file.Render (original).ToArray ();

		Assert.IsNotEmpty (saved);
		Assert.AreEqual ("TagLibSharp2", AviFile.Read (saved).File!.InfoTag.Title);
		CollectionAssert.AreEqual (AviTestBuilder.ResolveIndexes (original), AviTestBuilder.ResolveIndexes (saved));
		Console.WriteLine (saved.Length == original.Length ? "Saved in place." : $"Moved by {saved.Length - original.Length} bytes.");
	}

	[TestMethod]
	public void Save_RealAvi_TagPastTheJunkShiftsEveryIndex ()
	{
		var original = File.ReadAllBytes (SkipIfNoTestFile (TestFileEnvVar, "AVI"));
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = new string ('c', 8000);

		var saved = file.Render (original).ToArray ();

		Assert.IsNotEmpty (saved, "The save was refused.");
		Assert.IsGreaterThan (original.Length, saved.Length);
		Assert.AreEqual (file.InfoTag.Comment, AviFile.Read (saved).File!.InfoTag.Comment);
		var before = AviTestBuilder.ResolveIndexes (original);
		Console.WriteLine ($"{before.Count} index entries, moved by {saved.Length - original.Length} bytes.");
		CollectionAssert.AreEqual (before, AviTestBuilder.ResolveIndexes (saved));
	}
}
