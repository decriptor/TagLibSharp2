// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Custom fields on real files. Each test is skipped unless its environment variable names a file.
/// </summary>
/// <remarks>
/// Example: <c>TAGLIBSHARP2_TEST_FLAC=/path/song.flac dotnet test --filter "FullyQualifiedName~CustomFieldIntegrationTests"</c>.
/// The file is copied to a temporary directory; the original is never written.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
public class CustomFieldIntegrationTests : FileFormatTestBase
{
	const string Name = "TAGLIBSHARP2_CUSTOM";

	[TestMethod]
	[DataRow ("TAGLIBSHARP2_TEST_FLAC", "FLAC")]
	[DataRow ("TAGLIBSHARP2_TEST_MP3", "MP3")]
	[DataRow ("TAGLIBSHARP2_TEST_M4A", "M4A")]
	[DataRow ("TAGLIBSHARP2_TEST_OGG", "Ogg Vorbis or Opus")]
	[DataRow ("TAGLIBSHARP2_TEST_APE", "Monkey's Audio")]
	[DataRow ("TAGLIBSHARP2_TEST_WV", "WavPack")]
	[DataRow ("TAGLIBSHARP2_TEST_WMA", "WMA")]
	[DataRow ("TAGLIBSHARP2_TEST_MKV", "Matroska")]
	[DataRow ("TAGLIBSHARP2_TEST_WAV", "WAV")]
	[DataRow ("TAGLIBSHARP2_TEST_AIFF", "AIFF")]
	public void RealFile_CustomField_SurvivesSaveAndReopen (string envVar, string fileType)
	{
		var source = SkipIfNoTestFile (envVar, fileType);
		var dir = Path.Combine (Path.GetTempPath (), "tls2-custom-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (dir);
		var path = Path.Combine (dir, Path.GetFileName (source));
		File.Copy (source, path);
		try {
			var opened = MediaFile.Read (path);
			Assert.IsTrue (opened.IsSuccess, opened.Error);
			var tag = opened.Tag;
			Assert.IsNotNull (tag, "the sample needs a tag to write into");
			Assert.IsTrue (tag.SupportsCustomFields);

			// RIFF INFO holds one value under a four-character code.
			var name = tag is RiffInfoTag ? "IXYZ" : Name;
			var values = tag is RiffInfoTag ? new[] { "only" } : ["first", "second"];
			var title = tag.Title;
			Assert.IsTrue (tag.SetCustomField (name, values));
			Save (opened.File!, path);

			var reopened = MediaFile.Read (path);
			Assert.IsTrue (reopened.IsSuccess, reopened.Error);
			CollectionAssert.AreEqual (values, reopened.Tag!.GetCustomField (name).ToArray ());
			Assert.AreEqual (title, reopened.Tag.Title);

			Assert.IsTrue (reopened.Tag.RemoveCustomField (name));
			Save (reopened.File!, path);
			Assert.AreEqual (0, MediaFile.Read (path).Tag!.GetCustomField (name).Count);
		} finally {
			Directory.Delete (dir, recursive: true);
		}
	}

	static void Save (object file, string path)
	{
		var original = File.ReadAllBytes (path);
		var result = file switch {
			FlacFile f => f.SaveToFile (path, original),
			Mp3File f => f.SaveToFile (path, original),
			Mp4File f => f.SaveToFile (path, original),
			OggVorbisFile f => f.SaveToFile (path, original),
			OggOpusFile f => f.SaveToFile (path, original),
			MonkeysAudioFile f => f.SaveToFile (path, original),
			WavPackFile f => f.SaveToFile (path, original),
			AsfFile f => f.SaveToFile (path, original),
			MatroskaFile f => f.SaveToFile (path, original),
			WavFile f => f.SaveToFile (path),
			AiffFile f => f.SaveToFile (path),
			_ => throw new AssertFailedException ($"No save path for {file.GetType ().Name}")
		};
		Assert.IsTrue (result.IsSuccess, result.Error);
	}
}
