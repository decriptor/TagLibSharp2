// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Picture saves against a real Matroska file.
/// </summary>
/// <remarks>
/// Skipped by default. Set TAGLIBSHARP2_TEST_MATROSKA to an .mkv or .mka file, then run
/// <c>dotnet test --filter "TestCategory=Integration&amp;TestCategory=Matroska"</c>.
/// The file is copied to a temporary path; the original is never written.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Matroska")]
public class MatroskaIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MATROSKA";

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
