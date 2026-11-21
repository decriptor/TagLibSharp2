// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Integration tests that require a real Matroska or WebM file.
/// </summary>
/// <remarks>
/// These tests are skipped by default. To run them:
/// 1. Set the TAGLIBSHARP2_TEST_MATROSKA environment variable to a path to a .mkv, .mka, or .webm file
/// 2. Run with: dotnet test --filter "TestCategory=Integration"
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Matroska")]
public class MatroskaIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MATROSKA";

	[TestMethod]
	public void Read_RealMatroska_ListsTracks ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var file = Open (File.ReadAllBytes (path));

		foreach (var track in file.Tracks)
			Console.WriteLine ($"  #{track.TrackNumber} {track.TrackType} {track.CodecId} default={track.IsDefault} enabled={track.IsEnabled}");

		Assert.IsTrue (file.Tracks.Count > 0);
		Assert.AreEqual ((file.MediaTypes & MediaTypes.Audio) != 0, file.Tracks.Any (t => t.TrackType == MatroskaTrackType.Audio));
		Assert.AreEqual ((file.MediaTypes & MediaTypes.Video) != 0, file.Tracks.Any (t => t.TrackType == MatroskaTrackType.Video));
	}

	[TestMethod]
	public void Save_RealMatroska_UntouchedTracks_KeepTheirBytes ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var file = Open (original);
		foreach (var track in file.Tracks)
			track.IsEnabled = track.IsEnabled;

		var saved = file.Render (original).ToArray ();

		Assert.IsTrue (saved.Length > 0);
		CollectionAssert.AreEqual (Children (original, MatroskaIds.Tracks), Children (saved, MatroskaIds.Tracks));
		CollectionAssert.AreEqual (Children (original, MatroskaIds.Cluster), Children (saved, MatroskaIds.Cluster));
	}

	[TestMethod]
	public void Save_RealMatroska_ToggledTrack_RoundTripsAndKeepsClusters ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var file = Open (original);
		var index = IndexOfFirst (file, MatroskaTrackType.Audio);
		var before = file.Tracks[index].IsEnabled;
		file.Tracks[index].IsEnabled = !before;

		var fs = new MockFileSystem ();
		var saved = file.SaveToFile ("/toggled.mkv", original, fs);
		if (!saved.IsSuccess && saved.Error!.Contains ("no Void right after Tracks", StringComparison.Ordinal))
			Assert.Inconclusive ("Adding FlagEnabled to this file would move later elements: " + saved.Error);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var toggled = fs.ReadAllBytes ("/toggled.mkv");
		var again = Open (toggled);
		Assert.AreEqual (!before, again.Tracks[index].IsEnabled);
		for (var i = 0; i < file.Tracks.Count; i++) {
			Assert.AreEqual (file.Tracks[i].TrackNumber, again.Tracks[i].TrackNumber);
			if (i != index)
				Assert.AreEqual (file.Tracks[i].IsEnabled, again.Tracks[i].IsEnabled);
		}

		CollectionAssert.AreEqual (Children (original, MatroskaIds.Cluster), Children (toggled, MatroskaIds.Cluster));

		again.Tracks[index].IsEnabled = before;
		var restored = again.Render (toggled).ToArray ();
		Assert.AreEqual (before, Open (restored).Tracks[index].IsEnabled);
		Assert.AreEqual (toggled.Length, restored.Length, "a stored flag is rewritten in place");
	}

	static MatroskaFile Open (byte[] data)
	{
		var result = MatroskaFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	static int IndexOfFirst (MatroskaFile file, MatroskaTrackType type)
	{
		for (var i = 0; i < file.Tracks.Count; i++) {
			if (file.Tracks[i].TrackType == type)
				return i;
		}

		return 0;
	}

	/// <summary>A SHA-256 of the payload of every first-Segment child with <paramref name="id"/>, in file order.</summary>
	static List<string> Children (byte[] file, ulong id)
	{
		var found = new List<string> ();
		var offset = 0;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id == MatroskaIds.Segment) {
				var end = MatroskaFile.PayloadEnd (file, element);
				var at = element.DataOffset;
				while (at < end) {
					if (!EbmlReader.TryRead (file, at, end, out var child)) {
						at++;
						continue;
					}

					var childEnd = child.UnknownSize ? end : child.End;
					if (child.Id == id)
						found.Add (Convert.ToHexString (System.Security.Cryptography.SHA256.HashData (file.AsSpan (child.DataOffset, childEnd - child.DataOffset))));
					if (child.UnknownSize)
						break;
					at = child.End;
				}

				break;
			}

			if (element.UnknownSize)
				break;
			offset = element.End;
		}

		return found;
	}
}
