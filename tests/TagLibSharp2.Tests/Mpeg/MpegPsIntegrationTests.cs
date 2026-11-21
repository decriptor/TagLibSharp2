// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// Integration tests that require a real MPEG program stream or video elementary stream.
/// </summary>
/// <remarks>
/// These tests are skipped by default. To run them, set
/// TAGLIBSHARP2_TEST_MPEGPS to a program stream (.mpg, .vob) and/or
/// TAGLIBSHARP2_TEST_MPEGVIDEO to a video elementary stream (.m2v, .mpv), then run
/// <c>dotnet test --filter "TestCategory=Integration&amp;TestCategory=MpegPs"</c>.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("MpegPs")]
public class MpegPsIntegrationTests : FileFormatTestBase
{
	const string ProgramStreamEnvVar = "TAGLIBSHARP2_TEST_MPEGPS";
	const string ElementaryEnvVar = "TAGLIBSHARP2_TEST_MPEGVIDEO";

	[TestMethod]
	public void Read_RealProgramStream_HasPropertiesAndDuration ()
	{
		var path = SkipIfNoTestFile (ProgramStreamEnvVar, "MPEG program stream");

		var file = Open (path);

		Assert.AreEqual (MpegPsStreamType.ProgramStream, file.StreamType);
		Assert.IsTrue (file.SystemsVersion is 1 or 2);
		Assert.IsTrue (file.Duration > TimeSpan.Zero, file.Duration?.ToString ());
		Assert.IsTrue (file.VideoProperties is { IsValid: true } || file.AudioProperties is not null);
		Print (path, file);
	}

	[TestMethod]
	public void Read_RealElementaryStream_HasPropertiesAndDuration ()
	{
		var path = SkipIfNoTestFile (ElementaryEnvVar, "MPEG video elementary stream");

		var file = Open (path);

		Assert.AreEqual (MpegPsStreamType.VideoElementaryStream, file.StreamType);
		Assert.IsTrue (file.VideoProperties is { IsValid: true });
		Print (path, file);
	}

	[TestMethod]
	public void Save_RealProgramStream_KeepsTheStreamBytes () =>
		AssertSaveKeepsStream (SkipIfNoTestFile (ProgramStreamEnvVar, "MPEG program stream"));

	[TestMethod]
	public void Save_RealElementaryStream_KeepsTheStreamBytes () =>
		AssertSaveKeepsStream (SkipIfNoTestFile (ElementaryEnvVar, "MPEG video elementary stream"));

	[TestMethod]
	public async Task SaveAsync_RealProgramStream_UnchangedIsByteIdentical ()
	{
		var path = SkipIfNoTestFile (ProgramStreamEnvVar, "MPEG program stream");
		var copy = CopyToTemp (path);
		try {
			var file = (await MpegPsFile.ReadFromFileAsync (copy)).File!;
			var saved = await file.SaveToFileAsync ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);
			CollectionAssert.AreEqual (File.ReadAllBytes (path), File.ReadAllBytes (copy));
		} finally {
			File.Delete (copy);
		}
	}

	static MpegPsFile Open (string path)
	{
		var opened = MediaFile.Read (path);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		return (MpegPsFile)opened.File!;
	}

	static void AssertSaveKeepsStream (string path)
	{
		var original = File.ReadAllBytes (path);
		var before = MpegPsFile.Read (original).File!;
		var copy = CopyToTemp (path);
		try {
			var file = MpegPsFile.ReadFromFile (copy).File!;
			file.EnsureId3v2Tag ().Title = "TagLibSharp2 integration";
			var saved = file.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var written = File.ReadAllBytes (copy);
			var after = MpegPsFile.Read (written).File!;
			Assert.AreEqual ("TagLibSharp2 integration", after.Tag!.Title);
			Assert.AreEqual (before.Duration, after.Duration);
			Assert.IsTrue (Stream (original, before).SequenceEqual (Stream (written, after)), "stream bytes changed");
		} finally {
			File.Delete (copy);
		}
	}

	// The bytes between the prepended tag and the appended tag or ID3v1. An appended tag ends
	// with a "3DI" footer whose syncsafe size excludes its header and footer.
	static ReadOnlySpan<byte> Stream (byte[] data, MpegPsFile file)
	{
		var end = data.Length - (file.Id3v1Tag is null ? 0 : 128);
		if (end - file.Id3v2Size >= 10 && data[end - 10] == '3' && data[end - 9] == 'D' && data[end - 8] == 'I') {
			var size = (data[end - 4] << 21) | (data[end - 3] << 14) | (data[end - 2] << 7) | data[end - 1];
			end -= size + 20;
		}

		return data.AsSpan (file.Id3v2Size, end - file.Id3v2Size);
	}

	static string CopyToTemp (string path)
	{
		var copy = Path.Combine (Path.GetTempPath (), "tls2-" + Guid.NewGuid ().ToString ("N") + Path.GetExtension (path));
		File.Copy (path, copy);
		return copy;
	}

	static void Print (string path, MpegPsFile file)
	{
		Console.WriteLine ($"{Path.GetFileName (path)}: {file.StreamType}, systems {file.SystemsVersion}, video {file.VideoVersion}, duration {file.Duration}");
		if (file.VideoProperties is { } video)
			Console.WriteLine ($"  video: {video}");
		if (file.AudioProperties is { } audio)
			Console.WriteLine ($"  audio: {audio}");
	}
}
