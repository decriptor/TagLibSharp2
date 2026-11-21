// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Audible;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Audible;

/// <summary>
/// Integration tests against real Audible files.
/// </summary>
/// <remarks>
/// Skipped unless the environment names a file:
/// <list type="bullet">
/// <item>TAGLIBSHARP2_TEST_AA: an Audible .aa file (for example taglib-sharp's samples/audible/first.aa)</item>
/// <item>TAGLIBSHARP2_TEST_AAX: an Audible .aax file</item>
/// </list>
/// Example: TAGLIBSHARP2_TEST_AA=/path/first.aa dotnet test --filter "TestCategory=Integration&amp;TestCategory=Audible"
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Audible")]
public class AudibleIntegrationTests : FileFormatTestBase
{
	const string AaEnvVar = "TAGLIBSHARP2_TEST_AA";
	const string AaxEnvVar = "TAGLIBSHARP2_TEST_AAX";

	[TestMethod]
	public void RealAa_ReadsTagsAndDuration ()
	{
		var path = SkipIfNoTestFile (AaEnvVar, "Audible .aa");

		var result = MediaFile.Read (path);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Audible, result.Format);
		var file = (AudibleFile)result.File!;
		Assert.IsFalse (string.IsNullOrEmpty (file.Tag.Title));
		Assert.IsFalse (string.IsNullOrEmpty (file.Tag.Author));
		Assert.IsFalse (string.IsNullOrEmpty (file.Tag.Codec));
		Assert.IsNotNull (file.Tag.GetValue ("HeaderKey"));
		Assert.IsTrue (file.Properties.Duration > TimeSpan.Zero, $"codec {file.Tag.Codec}");
		Console.WriteLine ($"{file.Tag.Title} / {file.Tag.Author} / {file.Tag.Narrator} / {file.Tag.Codec} / {file.Properties.Duration.TotalSeconds:F6}s");
	}

	[TestMethod]
	public void RealAa_StreamAndByteReadsAgree ()
	{
		var path = SkipIfNoTestFile (AaEnvVar, "Audible .aa");
		var bytes = File.ReadAllBytes (path);

		var fromBytes = AudibleFile.Read (bytes).File!;
		var fromPath = AudibleFile.ReadFromFile (path).File!;

		CollectionAssert.AreEqual (fromBytes.Tag.Keys.ToList (), fromPath.Tag.Keys.ToList ());
		foreach (var key in fromBytes.Tag.Keys)
			Assert.AreEqual (fromBytes.Tag.GetValue (key), fromPath.Tag.GetValue (key), key);
		Assert.AreEqual (fromBytes.Properties, fromPath.Properties);
		Assert.AreEqual (fromBytes.Tag.Pictures.Length, fromPath.Tag.Pictures.Length);
	}

	[TestMethod]
	public void RealAa_UnchangedRender_IsByteIdentical ()
	{
		var path = SkipIfNoTestFile (AaEnvVar, "Audible .aa");
		var bytes = File.ReadAllBytes (path);

		var file = AudibleFile.Read (bytes).File!;

		CollectionAssert.AreEqual (bytes, file.Render (bytes).ToArray ());
	}

	[TestMethod]
	public void RealAa_EditedSave_KeepsEveryOtherByte ()
	{
		var path = SkipIfNoTestFile (AaEnvVar, "Audible .aa");
		var bytes = File.ReadAllBytes (path);
		var file = AudibleFile.Read (bytes).File!;
		var before = file.Tag.Keys.ToDictionary (k => k, k => file.Tag.GetValue (k));
		var dictionary = AudibleTestBuilder.Toc (bytes, 2);

		file.Tag.Title = "Edited title – ✓ with a different length";
		file.Tag.Narrator = "Edited narrator";
		var output = file.Render (bytes).ToArray ();

		var delta = output.Length - bytes.Length;
		var reread = AudibleFile.Read (output).File!;
		Assert.AreEqual ("Edited title – ✓ with a different length", reread.Tag.Title);
		Assert.AreEqual ("Edited narrator", reread.Tag.Narrator);
		foreach (var (key, value) in before.Where (kv => kv.Key is not "title" and not "narrator"))
			Assert.AreEqual (value, reread.Tag.GetValue (key), key);
		Assert.AreEqual (file.Properties, reread.Properties);
		Assert.AreEqual ((long)file.DeclaredFileSize + delta, reread.DeclaredFileSize);
		var end = (int)(dictionary.Offset + dictionary.Size);
		CollectionAssert.AreEqual (bytes.AsSpan (end).ToArray (), output.AsSpan (end + delta).ToArray (), "bytes after the dictionary");
	}

	[TestMethod]
	public void RealAax_OpensAsMp4WhenItsMovieBoxIsComplete ()
	{
		var path = SkipIfNoTestFile (AaxEnvVar, "Audible .aax");

		var result = MediaFile.Read (path);

		// taglib-sharp's second.aax is cut at 10 KiB inside moov/trak/mdia/minf/stbl/stsz, before udta.
		// ffprobe also rejects it ("error reading header"), so a failure is reported, not a crash.
		if (!result.IsSuccess) {
			Assert.IsNotNull (result.Error);
			Assert.Inconclusive ($"{path} could not be read as MPEG-4: {result.Error}");
		}
		Assert.AreEqual (MediaFormat.Mp4, result.Format);
		var file = (Mp4File)result.File!;
		Assert.AreEqual ("aax", file.FileType);
		Console.WriteLine ($"{file.Title} / {file.Narrator} / {file.ProductId} / {file.Properties.Duration}");
	}
}
