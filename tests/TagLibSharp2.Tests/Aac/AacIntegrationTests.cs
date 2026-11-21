// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aac;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aac;

/// <summary>
/// Integration tests that require a real raw AAC file.
/// </summary>
/// <remarks>
/// Skipped by default. Set TAGLIBSHARP2_TEST_AAC to the path of an ADTS or ADIF file, then run
/// <c>dotnet test --filter "TestCategory=Integration&amp;TestCategory=Aac"</c>.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Aac")]
public class AacIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_AAC";

	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void Read_RealAac_IsAacOnEveryPath ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "AAC");
		var bytes = File.ReadAllBytes (path);

		var direct = AacFile.ReadFromFile (path);
		var streamed = MediaFile.Read (path);
		var loaded = MediaFile.ReadFromData (bytes, path);

		Assert.IsTrue (direct.IsSuccess, direct.Error);
		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.IsTrue (loaded.IsSuccess, loaded.Error);
		Assert.AreEqual (MediaFormat.Aac, streamed.Format);
		Assert.AreEqual (MediaFormat.Aac, loaded.Format);

		var props = direct.File!.Properties!;
		TestContext.WriteLine ($"{props.StreamFormat} MPEG-{props.MpegVersion} {props.Codec} {props.SampleRate} Hz " +
			$"{props.Channels} ch {props.Bitrate} kbps {props.Duration.TotalSeconds:F6} s {props.FrameCount} frames");
		TestContext.WriteLine ($"ID3v2={direct.File.HasId3v2Tag} APE={direct.File.HasApeTag} ID3v1={direct.File.HasId3v1Tag} " +
			$"title='{direct.File.Tag?.Title}' artist='{direct.File.Tag?.Artist}' album='{direct.File.Tag?.Album}'");
		Assert.IsTrue (props.SampleRate > 0);
		Assert.IsTrue (props.Duration > TimeSpan.Zero);

		var window = streamed.GetFileAs<AacFile> ()!.Properties!;
		Assert.AreEqual (props.Duration, window.Duration);
		Assert.AreEqual (props.Channels, window.Channels);
		Assert.AreEqual (props.FrameCount, window.FrameCount);
	}

	[TestMethod]
	public void Save_RealAac_KeepsTheFrameBytes ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "AAC");
		var original = File.ReadAllBytes (path);
		var file = AacFile.ReadFromFile (path).File!;
		var start = file.Id3v2Size;
		var end = TagLibSharp2.Ape.ApeTagLocation.Locate (original).AudioEnd;
		var frames = original.AsSpan (start, end - start).ToArray ();

		file.EnsureId3v2Tag ();
		file.Tag!.Title = "Integration";
		var saved = file.Render (original).ToArray ();

		var reread = AacFile.Read (saved);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("Integration", reread.File!.Tag!.Title);
		Assert.AreEqual (file.Properties!.Duration, reread.File.Properties!.Duration);
		CollectionAssert.AreEqual (frames, saved.AsSpan (reread.File.Id3v2Size, frames.Length).ToArray ());
	}
}
