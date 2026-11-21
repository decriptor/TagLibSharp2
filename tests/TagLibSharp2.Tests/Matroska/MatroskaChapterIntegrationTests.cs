// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Chapter round trips on a real Matroska, WebM, or MKA file.
/// </summary>
/// <remarks>
/// Skipped by default. Set TAGLIBSHARP2_TEST_MATROSKA to a file path, then run
/// <c>dotnet test --filter "TestCategory=Integration&amp;TestCategory=Matroska"</c>.
/// The file is copied, never written.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Matroska")]
public class MatroskaChapterIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MATROSKA";

	[TestMethod]
	public void RealFile_SaveWithoutEdits_IsByteIdentical ()
	{
		var original = File.ReadAllBytes (SkipIfNoTestFile (TestFileEnvVar, "Matroska"));
		var file = MatroskaFile.Read (original).File!;

		CollectionAssert.AreEqual (original, file.Render (original).ToArray ());
	}

	[TestMethod]
	public void RealFile_AddEditAndClearChapters_KeepsClustersAndTags ()
	{
		var original = File.ReadAllBytes (SkipIfNoTestFile (TestFileEnvVar, "Matroska"));
		var file = MatroskaFile.Read (original).File!;
		var title = file.Tag.Title;
		var count = file.Chapters.DefaultEdition?.Chapters.Count ?? 0;

		var added = file.Chapters.AddChapter (TimeSpan.FromSeconds (1), TimeSpan.FromSeconds (2), "Integration chapter");
		added.AddChapter (TimeSpan.FromSeconds (1.5), null, "Nested");
		var saved = file.Render (original).ToArray ();

		Assert.IsTrue (saved.Length > 0);
		CollectionAssert.AreEqual (ClusterPayloads (original), ClusterPayloads (saved));
		var again = MatroskaFile.Read (saved).File!;
		Assert.AreEqual (title, again.Tag.Title);
		var edition = again.Chapters.DefaultEdition!;
		Assert.AreEqual (count + 1, edition.Chapters.Count);
		var read = edition.Chapters[^1];
		Assert.AreEqual (added.Uid, read.Uid);
		Assert.AreEqual ("Integration chapter", read.Title);
		Assert.AreEqual ("Nested", read.Chapters[0].Title);
		// A live file without a SeekHead has nothing to list them in (RFC 9559 section 6.1).
		if (TopLevel (original, MatroskaIds.SeekHead) is not null)
			Assert.IsTrue (ChaptersAreListed (saved), "The SeekHead does not list the Chapters element");

		again.Chapters.Clear ();
		var cleared = again.Render (saved).ToArray ();
		CollectionAssert.AreEqual (ClusterPayloads (original), ClusterPayloads (cleared));
		Assert.IsTrue (MatroskaFile.Read (cleared).File!.Chapters.IsEmpty);
	}

	[TestMethod]
	public void RealFile_TagEdit_KeepsTheChaptersBytes ()
	{
		var original = File.ReadAllBytes (SkipIfNoTestFile (TestFileEnvVar, "Matroska"));
		var file = MatroskaFile.Read (original).File!;
		if (file.Chapters.IsEmpty)
			Assert.Inconclusive ("The file has no chapters.");
		var before = TopLevel (original, MatroskaIds.Chapters);

		file.Tag.Comment = "Integration comment";
		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (before, TopLevel (saved, MatroskaIds.Chapters));
		Assert.AreEqual ("Integration comment", MatroskaFile.Read (saved).File!.Tag.Comment);
	}

	/// <summary>Each cluster's children except <c>Position</c> and CRC-32, which a shift rewrites.</summary>
	static List<byte[]> ClusterPayloads (byte[] file)
	{
		var payloads = new List<byte[]> ();
		foreach (var (start, end) in Children (file, MatroskaIds.Cluster)) {
			if (!EbmlReader.TryRead (file, start, end, out var cluster))
				continue;
			using var kept = new MemoryStream ();
			var offset = cluster.DataOffset;
			while (offset < end && EbmlReader.TryRead (file, offset, end, out var child) && !child.UnknownSize) {
				if (child.Id is not MatroskaIds.ClusterPosition and not MatroskaIds.Crc32)
					kept.Write (file, child.Position, child.End - child.Position);
				offset = child.End;
			}

			payloads.Add (kept.ToArray ());
		}

		return payloads;
	}

	static byte[]? TopLevel (byte[] file, ulong id)
	{
		foreach (var (start, end) in Children (file, id))
			return file.AsSpan (start, end - start).ToArray ();
		return null;
	}

	static bool ChaptersAreListed (byte[] file)
	{
		if (!TrySegment (file, out var dataOffset, out var segmentEnd))
			return false;
		foreach (var (start, end) in Children (file, MatroskaIds.SeekHead)) {
			EbmlReader.TryRead (file, start, end, out var seekHead);
			var offset = seekHead.DataOffset;
			while (offset < end && EbmlReader.TryRead (file, offset, end, out var seek) && !seek.UnknownSize) {
				ulong id = 0;
				ulong position = 0;
				var inner = seek.DataOffset;
				while (inner < seek.End && EbmlReader.TryRead (file, inner, seek.End, out var child) && !child.UnknownSize) {
					var payload = file.AsSpan (child.DataOffset, (int)child.DataLength);
					if (child.Id == MatroskaIds.SeekId)
						id = EbmlReader.ReadUInt (payload);
					else if (child.Id == MatroskaIds.SeekPosition)
						position = EbmlReader.ReadUInt (payload);
					inner = child.End;
				}

				var at = dataOffset + (long)position;
				if (id == MatroskaIds.Chapters && at < segmentEnd
					&& EbmlReader.TryRead (file, (int)at, segmentEnd, out var target) && target.Id == MatroskaIds.Chapters)
					return true;
				offset = seek.End;
			}
		}

		return false;
	}

	static IEnumerable<(int Start, int End)> Children (byte[] file, ulong id)
	{
		if (!TrySegment (file, out var offset, out var end))
			yield break;
		while (offset < end && EbmlReader.TryRead (file, offset, end, out var element)) {
			var elementEnd = element.UnknownSize ? end : element.End;
			if (element.Id == id)
				yield return (element.Position, elementEnd);
			if (element.UnknownSize)
				yield break;
			offset = elementEnd;
		}
	}

	static bool TrySegment (byte[] file, out int dataOffset, out int end)
	{
		dataOffset = 0;
		end = 0;
		var offset = 0;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id == MatroskaIds.Segment) {
				dataOffset = element.DataOffset;
				end = element.UnknownSize ? file.Length : element.End;
				return true;
			}

			if (element.UnknownSize)
				return false;
			offset = element.End;
		}

		return false;
	}
}
