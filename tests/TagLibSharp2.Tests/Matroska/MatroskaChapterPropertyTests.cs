// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	/// <summary>One edit: <c>Edit</c> changes the model; <c>Same</c> touches the same value but leaves it as read.</summary>
	sealed record ChapterEdit (string Name, Action<MatroskaChapters> Edit, Action<MatroskaChapters> Same, bool StoredInWebM, bool IsEdit = true);

	[TestMethod]
	public void SaveChapters_EveryModelledValue_IsDetectedAndKeptWhenUntouched ()
	{
		// For every settable value and list operation, at the top level and nested, in Matroska and WebM:
		// an edit is saved and reads back, setting a value to what it already holds is byte-identical,
		// and a value WebM does not store is byte-identical in WebM.
		var failures = new List<string> ();
		foreach (var docType in new[] { "matroska", "webm" }) {
			var webm = docType == "webm";
			var original = ChapterFixture (docType);
			foreach (var edit in ChapterEdits ()) {
				var edited = MatroskaFile.Read (original).File!;
				edit.Edit (edited.Chapters);
				var bytes = Save (edited, original);
				var expectChange = edit.IsEdit && (!webm || edit.StoredInWebM);
				if (expectChange) {
					if (bytes.AsSpan ().SequenceEqual (original))
						failures.Add (docType + " " + edit.Name + ": the edit was not saved");
					else if (DumpChapters (MatroskaFile.Read (bytes).File!.Chapters, webm) != DumpChapters (edited.Chapters, webm))
						failures.Add (docType + " " + edit.Name + ": the reread differs from the model");
				} else if (!bytes.AsSpan ().SequenceEqual (original))
					failures.Add (docType + " " + edit.Name + ": a value that is not written changed the file");

				var same = MatroskaFile.Read (original).File!;
				edit.Same (same.Chapters);
				if (!Save (same, original).AsSpan ().SequenceEqual (original))
					failures.Add (docType + " " + edit.Name + ": setting the value back changed the file");
			}
		}

		Assert.AreEqual (0, failures.Count, string.Join ("\n", failures));
	}

	[TestMethod]
	public void SaveChapters_ParentMovedPastAnUntouchedChild_Fails ()
	{
		// RFC 9559 section 20.2.3: changing the parent puts its untouched children's starts back in question.
		var original = ChapterFixture ("matroska");
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].Chapters[0].Start = TimeSpan.FromSeconds (1.5);

		AssertFailure (file, new MockFileSystem (), "/parent.mkv", original, "edition 1, chapter 1.1, UID 2");
	}

	[TestMethod]
	public void SaveChapters_UntouchedLeafMovedIntoAnOrderedEdition_Fails ()
	{
		// RFC 9559 section 20.2.4: a leaf in an ordered edition needs an end, wherever it came from.
		var original = FileWith (
			ChaptersElement (
				EditionEntry (UInt (0x45BC, 1), Atom (10, 0, Second), Atom (11, 2 * Second, null, Display ("No end"))),
				EditionEntry (UInt (0x45BC, 2), UInt (0x45DD, 1), Atom (20, 0, Second, Display ("Ordered leaf")))),
			Master (0x1F43B675, new byte[] { 1 }));

		var moved = MatroskaFile.Read (original).File!;
		var leaf = moved.Chapters.Editions[0].Chapters[1];
		moved.Chapters.Editions[0].RemoveChapter (leaf);
		moved.Chapters.Editions[1].Chapters.Add (leaf);
		AssertFailure (moved, new MockFileSystem (), "/moved.mkv", original, "edition 2, chapter 2, UID 11");

		// A nested leaf without an end, moved to the top of the ordered edition, is checked the same way.
		var nestedFile = FileWith (
			ChaptersElement (
				EditionEntry (Atom (30, 0, 10 * Second, Atom (31, Second, null, Display ("Nested, no end")))),
				EditionEntry (UInt (0x45DD, 1), Atom (40, 0, Second))),
			Master (0x1F43B675, new byte[] { 1 }));
		var lifted = MatroskaFile.Read (nestedFile).File!;
		var parent = lifted.Chapters.Editions[0].Chapters[0];
		var child = parent.Chapters[0];
		parent.RemoveChapter (child);
		lifted.Chapters.Editions[1].Chapters.Add (child);
		AssertFailure (lifted, new MockFileSystem (), "/lifted.mkv", nestedFile, "UID 31");
	}

	[TestMethod]
	public void SaveChapters_WebM_OrderedFlagDoesNotApplyTheOrderedRule ()
	{
		// WebM stores no EditionFlagOrdered, so a real edit next to an end-less chapter still saves.
		var original = ChapterFixture ("webm");
		var file = MatroskaFile.Read (original).File!;
		var edition = file.Chapters.Editions[0];
		edition.IsOrdered = true;
		edition.Chapters[1].End = null;
		edition.Chapters[0].Title = "Edited";

		var bytes = Save (file, original);

		Assert.AreEqual ("Edited", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_ChapterNestedInItself_DoesNotThrow ()
	{
		// The depth limit stops Prepare, validation and writing on a cycle built in code.
		var original = ChapterFixture ("matroska");
		var file = MatroskaFile.Read (original).File!;
		var chapter = file.Chapters.Editions[0].Chapters[0];
		chapter.Chapters.Add (chapter);

		var bytes = Save (file, original);

		Assert.AreEqual (MatroskaChapters.MaxDepth, MaxAtomDepth (bytes));
	}

	[TestMethod]
	public void SaveChapters_UntouchedEdition_KeepsItsBytes ()
	{
		// The second edition carries a CRC-32 that a rewrite would drop. Editing the first leaves it as it was.
		var body = Concat (UInt (0x45BC, 2), Atom (5, 0, Second, Display ("Second edition")));
		var crc = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (crc, new BinaryData (body).ComputeCrc32 ());
		var second = Master (0x45B9, Concat (Element (0xBF, crc), body));
		var original = FileWith (
			ChaptersElement (EditionEntry (UInt (0x45BC, 1), Atom (4, 0, Second, Display ("First"))), second),
			Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].Chapters[0].Title = "Edited";

		var bytes = Save (file, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf (second) >= 0);
		Assert.AreEqual ("Edited", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_AddedEmptyEdition_IsNotAnEdit ()
	{
		// An edition without chapters is never written, so adding one leaves the file byte-identical,
		// CRC-32 and unmodelled WebM children included.
		foreach (var docType in new[] { "matroska", "webm" }) {
			var body = Concat (EditionEntry (UInt (0x45BC, 1), Atom (1, 0, Second, Display ("Kept"), Master (0x6944, UInt (0x6955, 0)))));
			var crc = new byte[4];
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (crc, new BinaryData (body).ComputeCrc32 ());
			var original = Matroska (Master (0x18538067, Concat (
				Master (0x1043A770, Concat (Element (0xBF, crc), body)),
				Master (0x1F43B675, new byte[] { 1 }))), docType);

			var added = MatroskaFile.Read (original).File!;
			added.Chapters.AddEdition ();
			CollectionAssert.AreEqual (original, Save (added, original), docType);

			var inserted = MatroskaFile.Read (original).File!;
			inserted.Chapters.Editions.Insert (0, new MatroskaEdition ());
			CollectionAssert.AreEqual (original, Save (inserted, original), docType);
		}
	}

	/// <summary>
	/// Edition 5 holds chapter 1 (0–10 s, display "A" eng/us) with chapter 2 nested in it (1–2 s, "B"),
	/// then chapter 3 (20–30 s, "C").
	/// </summary>
	static byte[] ChapterFixture (string docType) =>
		Matroska (Master (0x18538067, Concat (
			ChaptersElement (EditionEntry (
				UInt (0x45BC, 5),
				Atom (1, 0, 10 * Second, Display ("A", "eng", "us"), Atom (2, Second, 2 * Second, Display ("B", "eng"))),
				Atom (3, 20 * Second, 30 * Second, Display ("C", "eng")))),
			Master (0x1F43B675, new byte[] { 1 }))), docType);

	static IEnumerable<ChapterEdit> ChapterEdits ()
	{
		static MatroskaChapter Top (MatroskaChapters c) => c.Editions[0].Chapters[0];
		static MatroskaChapter Nested (MatroskaChapters c) => c.Editions[0].Chapters[0].Chapters[0];

		foreach (var (where, pick, uid) in new (string, Func<MatroskaChapters, MatroskaChapter>, ulong)[] { ("top", Top, 99), ("nested", Nested, 98) }) {
			yield return new ($"{where} Uid", c => pick (c).Uid = uid, c => pick (c).Uid = pick (c).Uid, true);
			yield return new ($"{where} StringUid", c => pick (c).StringUid = "sid", c => pick (c).StringUid = pick (c).StringUid, true);
			yield return new ($"{where} StartNanoseconds", c => pick (c).StartNanoseconds += 100, c => pick (c).StartNanoseconds = pick (c).StartNanoseconds, true);
			yield return new ($"{where} Start", c => pick (c).Start += TimeSpan.FromMilliseconds (1), c => pick (c).Start = pick (c).Start, true);
			yield return new ($"{where} EndNanoseconds", c => pick (c).EndNanoseconds += 1000, c => pick (c).EndNanoseconds = pick (c).EndNanoseconds, true);
			yield return new ($"{where} End cleared", c => pick (c).End = null, c => pick (c).End = pick (c).End, true);
			yield return new ($"{where} IsHidden", c => pick (c).IsHidden = true, c => pick (c).IsHidden = pick (c).IsHidden, false);
			yield return new ($"{where} IsEnabled", c => pick (c).IsEnabled = false, c => pick (c).IsEnabled = pick (c).IsEnabled, false);
			yield return new ($"{where} Title", c => pick (c).Title = "New title", c => pick (c).Title = pick (c).Title, true);
			yield return new ($"{where} display Text", c => pick (c).Displays[0].Text = "Text", c => pick (c).Displays[0].Text = pick (c).Displays[0].Text, true);
			yield return new ($"{where} display language", c => pick (c).Displays[0].Languages[0] = "fre", c => pick (c).Displays[0].Languages[0] = pick (c).Displays[0].Languages[0], true);
			yield return new ($"{where} display language added", c => pick (c).Displays[0].Languages.Add ("ger"), c => { pick (c).Displays[0].Languages.Add ("ger"); pick (c).Displays[0].Languages.Remove ("ger"); }, true);
			yield return new ($"{where} display country", c => pick (c).Displays[0].Countries.Add ("de"), c => { pick (c).Displays[0].Countries.Add ("de"); pick (c).Displays[0].Countries.Remove ("de"); }, true);
			yield return new ($"{where} display BCP 47", c => pick (c).Displays[0].LanguagesBcp47.Add ("en-GB"), c => { pick (c).Displays[0].LanguagesBcp47.Add ("en-GB"); pick (c).Displays[0].LanguagesBcp47.Remove ("en-GB"); }, false);
			yield return new ($"{where} display added", c => pick (c).Displays.Add (new MatroskaChapterDisplay ("Second", "eng")), c => { var d = new MatroskaChapterDisplay ("Second", "eng"); pick (c).Displays.Add (d); pick (c).Displays.Remove (d); }, true);
			yield return new ($"{where} display removed", c => pick (c).Displays.RemoveAt (0), c => { var d = pick (c).Displays[0]; pick (c).Displays.RemoveAt (0); pick (c).Displays.Insert (0, d); }, true);
			yield return new ($"{where} nested chapter added", c => pick (c).AddChapter (pick (c).Start, pick (c).End, "Inner"), c => pick (c).RemoveChapter (pick (c).AddChapter (pick (c).Start, pick (c).End, "Inner")), true);
		}

		yield return new ("top nested chapter removed", c => Top (c).Chapters.RemoveAt (0), c => { var n = Top (c).Chapters[0]; Top (c).Chapters.RemoveAt (0); Top (c).Chapters.Insert (0, n); }, true);
		yield return new ("edition Uid", c => c.Editions[0].Uid = 77, c => c.Editions[0].Uid = c.Editions[0].Uid, false);
		yield return new ("edition IsDefault", c => c.Editions[0].IsDefault = true, c => c.Editions[0].IsDefault = c.Editions[0].IsDefault, false);
		yield return new ("edition IsOrdered", c => c.Editions[0].IsOrdered = true, c => c.Editions[0].IsOrdered = c.Editions[0].IsOrdered, false);
		yield return new ("edition IsHidden", c => c.Editions[0].IsHidden = true, c => c.Editions[0].IsHidden = c.Editions[0].IsHidden, false);
		yield return new ("edition chapter removed", c => c.Editions[0].Chapters.RemoveAt (1), c => { var x = c.Editions[0].Chapters[1]; c.Editions[0].Chapters.RemoveAt (1); c.Editions[0].Chapters.Insert (1, x); }, true);
		yield return new ("edition chapter added", c => c.Editions[0].AddChapter (TimeSpan.FromSeconds (40), TimeSpan.FromSeconds (41), "D"), c => c.Editions[0].RemoveChapter (c.Editions[0].AddChapter (TimeSpan.FromSeconds (40), null, "D")), true);
		yield return new ("edition chapters reordered", c => { var x = c.Editions[0].Chapters[0]; c.Editions[0].Chapters.RemoveAt (0); c.Editions[0].Chapters.Add (x); }, c => { var x = c.Editions[0].Chapters[1]; c.Editions[0].Chapters.RemoveAt (1); c.Editions[0].Chapters.Insert (1, x); }, true);
		yield return new ("edition added with a chapter", c => c.AddEdition ().AddChapter (TimeSpan.Zero, TimeSpan.FromSeconds (1), "New edition"), c => c.RemoveEdition (c.AddEdition ()), true);
		yield return new ("edition added empty", c => c.AddEdition (), c => c.Editions.Insert (0, new MatroskaEdition ()), true, IsEdit: false);
		yield return new ("edition removed", c => c.RemoveEdition (c.Editions[0]), c => { var e = c.Editions[0]; c.RemoveEdition (e); c.Editions.Add (e); }, true);
		yield return new ("cleared", c => c.Clear (), c => c.Editions[0].Chapters[0].Title = c.Editions[0].Chapters[0].Title, true);
	}

	/// <summary>Every value a save writes, recursively. WebM leaves out what it does not store.</summary>
	static string DumpChapters (MatroskaChapters chapters, bool webm)
	{
		var dump = new StringBuilder ();
		foreach (var edition in chapters.Editions) {
			if (edition.Chapters.Count == 0)
				continue;
			dump.Append ("E[");
			if (!webm)
				dump.Append (CultureInfo.InvariantCulture, $"{edition.Uid},{edition.IsDefault},{edition.IsOrdered},{edition.IsHidden}");
			foreach (var chapter in edition.Chapters)
				DumpChapter (chapter, webm, dump);
			dump.Append (']');
		}

		return dump.ToString ();
	}

	static void DumpChapter (MatroskaChapter chapter, bool webm, StringBuilder dump)
	{
		dump.Append (CultureInfo.InvariantCulture, $"C[{chapter.Uid},{chapter.StringUid},{chapter.StartNanoseconds},{chapter.EndNanoseconds}");
		if (!webm)
			dump.Append (CultureInfo.InvariantCulture, $",{chapter.IsHidden},{chapter.IsEnabled}");
		foreach (var display in chapter.Displays) {
			dump.Append ("D[").Append (display.Text).Append (';').Append (string.Join ("+", display.Languages)).Append (';');
			if (!webm)
				dump.Append (string.Join ("+", display.LanguagesBcp47));
			dump.Append (';').Append (string.Join ("+", display.Countries)).Append (']');
		}

		foreach (var nested in chapter.Chapters)
			DumpChapter (nested, webm, dump);
		dump.Append (']');
	}
}
