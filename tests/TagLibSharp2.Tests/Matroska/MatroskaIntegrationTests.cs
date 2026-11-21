// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Integration tests that require a real Matroska or WebM file: tracks and picture saves.
/// Picture saves work on a temporary copy; the original is never written.
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

	static readonly byte[] BackCover = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

	[TestMethod]
	public void Save_RealMatroska_UntouchedPictures_KeepTheAttachmentsBytes ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var before = TopLevel (original, MatroskaIds.Attachments);

		var copy = Copy (path);
		try {
			var file = MatroskaFile.ReadFromFile (copy).File!;
			file.Tag.Title = "Integration title";
			file.Tag.Pictures = file.Tag.Pictures;
			var saved = file.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var after = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (before, TopLevel (after, MatroskaIds.Attachments));
			CollectionAssert.AreEqual (TopLevel (original, MatroskaIds.Cluster), TopLevel (after, MatroskaIds.Cluster));
			Assert.AreEqual ("Integration title", MatroskaFile.ReadFromFile (copy).File!.Tag.Title);
		} finally {
			File.Delete (copy);
		}
	}

	[TestMethod]
	public void Save_RealMatroska_AddingThenRemovingABackCover_KeepsTheClustersAndOtherAttachments ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var clusters = TopLevel (original, MatroskaIds.Cluster);
		var originalFiles = AttachedFiles (original);

		var copy = Copy (path);
		try {
			var file = MatroskaFile.ReadFromFile (copy).File!;
			var pictures = file.Tag.Pictures;
			file.Tag.Pictures = [.. pictures, new BackPicture ()];
			var saved = file.SaveToFile ();
			// Documented refusals: WebM has no attachments, and a cover the SeekHead cannot reference is not written.
			if (!saved.IsSuccess && (saved.Error!.Contains ("SeekHead", StringComparison.Ordinal) || saved.Error.Contains ("WebM", StringComparison.Ordinal))) {
				CollectionAssert.AreEqual (original, File.ReadAllBytes (copy));
				Assert.Inconclusive ("This file refuses a new attachment: " + saved.Error);
			}
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var added = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (clusters, TopLevel (added, MatroskaIds.Cluster));
			foreach (var kept in originalFiles)
				Assert.IsTrue (added.AsSpan ().IndexOf (kept) >= 0, "An original attachment changed");
			var withBack = MatroskaFile.Read (added).File!.Tag.Pictures;
			Assert.AreEqual (pictures.Length + 1, withBack.Length);
			Assert.AreEqual (PictureType.BackCover, withBack[^1].PictureType);
			CollectionAssert.AreEqual (BackCover, withBack[^1].PictureData.ToArray ());

			var reread = MatroskaFile.ReadFromFile (copy).File!;
			reread.Tag.Pictures = [.. reread.Tag.Pictures.Where (p => p.PictureType != PictureType.BackCover || !p.PictureData.Span.SequenceEqual (BackCover))];
			saved = reread.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var removed = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (clusters, TopLevel (removed, MatroskaIds.Cluster));
			var final = AttachedFiles (removed);
			Assert.AreEqual (originalFiles.Count, final.Count);
			for (var i = 0; i < final.Count; i++)
				CollectionAssert.AreEqual (originalFiles[i], final[i]);
		} finally {
			File.Delete (copy);
		}
	}

	static string Copy (string path)
	{
		var copy = Path.Combine (Path.GetTempPath (), "taglibsharp2-" + Guid.NewGuid ().ToString ("N") + Path.GetExtension (path));
		File.Copy (path, copy);
		return copy;
	}

	/// <summary>Every segment child with <paramref name="id"/>, concatenated.</summary>
	static byte[] TopLevel (byte[] file, ulong id)
	{
		using var kept = new MemoryStream ();
		foreach (var (childId, start, end) in SegmentChildren (file)) {
			if (childId == id)
				kept.Write (file, start, end - start);
		}

		return kept.ToArray ();
	}

	static List<byte[]> AttachedFiles (byte[] file)
	{
		var found = new List<byte[]> ();
		foreach (var (id, start, end) in SegmentChildren (file)) {
			if (id != MatroskaIds.Attachments)
				continue;
			Assert.IsTrue (EbmlReader.TryRead (file, start, end, out var attachments));
			var offset = attachments.DataOffset;
			while (offset < end && EbmlReader.TryRead (file, offset, end, out var child) && !child.UnknownSize) {
				if (child.Id == MatroskaIds.AttachedFile)
					found.Add (file.AsSpan (child.Position, child.End - child.Position).ToArray ());
				offset = child.End;
			}
		}

		return found;
	}

	static List<(ulong Id, int Start, int End)> SegmentChildren (byte[] file)
	{
		var children = new List<(ulong, int, int)> ();
		Assert.IsTrue (EbmlReader.TryRead (file, 0, file.Length, out var header));
		var offset = header.End;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id != MatroskaIds.Segment) {
				offset = element.End;
				continue;
			}

			var end = element.UnknownSize ? file.Length : element.End;
			var at = element.DataOffset;
			while (at < end && EbmlReader.TryRead (file, at, end, out var child) && !child.UnknownSize) {
				children.Add ((child.Id, child.Position, child.End));
				at = child.End;
			}

			break;
		}

		return children;
	}

	sealed class BackPicture : IPicture
	{
		public string MimeType => "image/png";

		public PictureType PictureType => PictureType.BackCover;

		public string Description => "Integration back cover";

		public BinaryData PictureData { get; } = new (BackCover);
	}
}
