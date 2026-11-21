// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	[TestMethod]
	public void SaveChapters_UntouchedInvalidChapter_DoesNotBlockAnEdit ()
	{
		// Another tool wrote chapter 2 with its end before its start. Editing chapter 1, or adding one,
		// still saves, and chapter 2 goes back byte for byte.
		var bad = Atom (2, 5 * Second, 2 * Second, Display ("Backwards"));
		var original = FileWith (
			ChaptersElement (EditionEntry (Atom (1, 0, Second, Display ("One")), bad)),
			Master (0x1F43B675, new byte[] { 1 }));

		var renamed = MatroskaFile.Read (original).File!;
		renamed.Chapters.Editions[0].Chapters[0].Title = "Renamed";
		var bytes = Save (renamed, original);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (bad) >= 0);
		var again = MatroskaFile.Read (bytes).File!.Chapters.Editions[0];
		Assert.AreEqual ("Renamed", again.Chapters[0].Title);
		Assert.AreEqual (2 * Second, again.Chapters[1].EndNanoseconds);

		var added = MatroskaFile.Read (original).File!;
		added.Chapters.AddChapter (TimeSpan.FromSeconds (9), TimeSpan.FromSeconds (10), "Added");
		Assert.IsTrue (Save (added, original).AsSpan ().IndexOf (bad) >= 0);
	}

	[TestMethod]
	public void SaveChapters_EditedInvalidChapter_FailsAndNamesIt ()
	{
		var original = FileWith (
			ChaptersElement (EditionEntry (Atom (1, 0, Second, Display ("One")), Atom (77, 5 * Second, 2 * Second, Display ("Backwards")))),
			Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].Chapters[1].Title = "Still backwards";

		AssertFailure (file, new MockFileSystem (), "/named.mkv", original, "edition 1, chapter 2, UID 77");
	}

	[TestMethod]
	public void SaveChapters_EditionMadeOrdered_ChecksItsUntouchedChapters ()
	{
		// Making an edition ordered is an edit to it, so every lowest-level chapter in it needs an end.
		var original = FileWith (
			ChaptersElement (EditionEntry (Atom (1, 0, Second), Atom (2, Second, null, Display ("No end")))),
			Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].IsOrdered = true;

		AssertFailure (file, new MockFileSystem (), "/ordered.mkv", original, "edition 1, chapter 2, UID 2");
	}

	[TestMethod]
	public void SaveChapters_UntouchedChapters_KeepTheirBytes ()
	{
		// A CRC-32, a Void and padded integers in an untouched chapter would not survive a rewrite.
		var body = Concat (Element (0x73C4, new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }), Element (0x91, new byte[] { 0, 0 }), Display ("Keep"), Element (0xEC, new byte[] { 0 }));
		var crc = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (crc, new BinaryData (body).ComputeCrc32 ());
		var untouched = Master (0xB6, Concat (Element (0xBF, crc), body));
		var original = FileWith (
			ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Edit me")), untouched)),
			Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].Chapters[0].Title = "Edited";

		var bytes = Save (file, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (untouched) >= 0);
		Assert.AreEqual ("Edited", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_OrderedEditionWithAKeptLeaf_SavesAfterAnEdit ()
	{
		// Only the leaf, 33 levels down, has an end. It is past the depth limit, so the chapter at
		// level 32 holds it as bytes and is still a parent (RFC 9559 section 20.2.4).
		var atom = Atom (33, 0, 10 * Second);
		for (var level = 32; level >= 1; level--)
			atom = Atom ((ulong)level, 0, null, atom);
		var original = FileWith (ChaptersElement (EditionEntry (UInt (0x45DD, 1), atom)), Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		var deepest = file.Chapters.Editions[0].Chapters[0];
		while (deepest.Chapters.Count > 0)
			deepest = deepest.Chapters[0];
		Assert.AreEqual ((ulong)MatroskaChapters.MaxDepth, deepest.Uid);
		deepest.Title = "Level 32";

		var bytes = Save (file, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (Atom (33, 0, 10 * Second)) >= 0);
		Assert.AreEqual (MatroskaChapters.MaxDepth + 1, MaxAtomDepth (bytes));
	}

	[TestMethod]
	public void SaveChapters_EditionsPastTheBudget_StayBeforeAnAddedEdition ()
	{
		var count = 30_000;
		var editions = new byte[count][];
		for (var i = 0; i < count; i++)
			editions[i] = EditionEntry (Master (0xB6, UInt (0x73C4, (ulong)i + 1)));
		var last = editions[count - 1];
		var data = Matroska (BigElement (0x18538067, Concat (
			BigElement (0x1043A770, Concat (editions)),
			Master (0x1F43B675, new byte[] { 1 }))));
		var file = MatroskaFile.Read (data).File!;
		Assert.IsTrue (file.Chapters.Editions.Count < count);
		file.Chapters.AddEdition ().AddChapter (TimeSpan.Zero, null, "ADDED EDITION");

		var bytes = Save (file, data);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (last) >= 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (last) < bytes.AsSpan ().IndexOf ("ADDED EDITION"u8));
	}

	[TestMethod]
	public void SaveChapters_RemoveEditionWithoutAtoms_RemovesTheElement ()
	{
		// RemoveEdition and a removal through the list behave like Clear for an edition without chapters.
		var original = FileWith (ChaptersElement (EditionEntry (UInt (0x45BC, 3))), Master (0x1F43B675, new byte[] { 1 }));

		var removed = MatroskaFile.Read (original).File!;
		Assert.IsTrue (removed.Chapters.RemoveEdition (removed.Chapters.Editions[0]));
		Assert.AreEqual (0, TopLevelCount (Save (removed, original), MatroskaIds.Chapters));

		var listed = MatroskaFile.Read (original).File!;
		listed.Chapters.Editions.RemoveAt (0);
		Assert.AreEqual (0, TopLevelCount (Save (listed, original), MatroskaIds.Chapters));

		var untouched = MatroskaFile.Read (original).File!;
		CollectionAssert.AreEqual (original, Save (untouched, original));
	}

	[TestMethod]
	public void SaveChapters_WebM_ValuesWebMDoesNotStore_AreNotAnEdit ()
	{
		// WebM has no chapter flags, edition flags, EditionUID or ChapLanguageBCP47, so setting them
		// changes nothing in the file, and an ordered flag does not bring in the ordered-edition rule.
		var original = Matroska (Master (0x18538067, Concat (
			ChaptersElement (EditionEntry (Atom (1, 0, null, Display ("Web")))),
			Master (0x1F43B675, new byte[] { 1 }))), "webm");
		var file = MatroskaFile.Read (original).File!;
		var chapter = file.Chapters.Editions[0].Chapters[0];
		chapter.IsHidden = true;
		chapter.IsEnabled = false;
		chapter.Displays[0].LanguagesBcp47.Add ("en-US");
		file.Chapters.Editions[0].IsOrdered = true;
		file.Chapters.Editions[0].Uid = 42;

		CollectionAssert.AreEqual (original, Save (file, original));
	}
}
