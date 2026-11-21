// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	// Every chapter element ID that the WebM container guidelines list as supported.
	static readonly HashSet<ulong> WebMChapterIds = [
		0x1043A770, 0x45B9, 0xB6, 0x73C4, 0x5654, 0x91, 0x92, 0x80, 0x85, 0x437C, 0x437E
	];

	[TestMethod]
	public void ReadChapters_AMillionEmptyAtoms_KeepAtMostTheBudget ()
	{
		// An empty ChapterAtom is two bytes. Past the budget the rest of the parent is one copy,
		// so a crafted file cannot make one object per element.
		var atoms = new byte[2_000_000];
		for (var i = 0; i < atoms.Length; i += 2) {
			atoms[i] = 0xB6;
			atoms[i + 1] = 0x80;
		}

		var data = Matroska (BigElement (0x18538067, BigElement (0x1043A770, BigElement (0x45B9, atoms))));
		var before = GC.GetAllocatedBytesForCurrentThread ();

		var file = MatroskaFile.Read (data).File!;

		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		// About 18 bytes allocated per input byte; one object per element took over 100.
		Assert.IsTrue (allocated < 24L * data.Length, allocated.ToString (System.Globalization.CultureInfo.InvariantCulture));
		Assert.IsFalse (file.Chapters.IsEmpty);
	}

	[TestMethod]
	public void ReadChapters_AMillionEmptyEditions_KeepAtMostTheBudget ()
	{
		// One real edition, then a million empty ones (three bytes each).
		var empty = new byte[3_000_000];
		for (var i = 0; i < empty.Length; i += 3) {
			empty[i] = 0x45;
			empty[i + 1] = 0xB9;
			empty[i + 2] = 0x80;
		}

		var data = Matroska (BigElement (0x18538067, BigElement (0x1043A770, Concat (EditionEntry (Atom (1, 0, null)), empty))));
		var before = GC.GetAllocatedBytesForCurrentThread ();

		var file = MatroskaFile.Read (data).File!;

		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;
		// About 8 bytes allocated per input byte; one object per element took about 30.
		Assert.IsTrue (file.Chapters.Editions.Count <= MatroskaChapters.MaxEntries);
		Assert.IsTrue (allocated < 16L * data.Length, allocated.ToString (System.Globalization.CultureInfo.InvariantCulture));
		Assert.AreEqual (1UL, file.Chapters.Editions[0].Chapters[0].Uid);
	}

	[TestMethod]
	public void ReadChapters_DisplaysPastTheBudget_AreKeptAndWrittenBack ()
	{
		var count = 70_000;
		var displays = new byte[count][];
		for (var i = 0; i < count; i++)
			displays[i] = Master (0x80, Element (0x85, new[] { (byte)('a' + (i % 26)) }));
		var last = Master (0x80, Element (0x85, "LAST"u8));
		var atom = BigElement (0xB6, Concat (UInt (0x73C4, 1), UInt (0x91, 0), Concat (displays), last));
		var data = Matroska (BigElement (0x18538067, Concat (
			BigElement (0x1043A770, BigElement (0x45B9, atom)),
			Master (0x1F43B675, new byte[] { 1 }))));

		var file = MatroskaFile.Read (data).File!;
		var chapter = file.Chapters.Editions[0].Chapters[0];

		Assert.IsTrue (chapter.Displays.Count < count);
		chapter.Title = "First";
		var bytes = Save (file, data);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (last) >= 0);
		Assert.AreEqual ("First", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void ReadChapters_AtomsPastTheBudget_AreKeptAndWrittenBack ()
	{
		var count = 70_000;
		var atoms = new byte[count][];
		for (var i = 0; i < count; i++)
			atoms[i] = Master (0xB6, UInt (0x73C4, (ulong)i + 1));
		var last = Master (0xB6, Concat (UInt (0x73C4, 999_999), Display ("LAST")));
		var data = Matroska (BigElement (0x18538067, Concat (
			BigElement (0x1043A770, BigElement (0x45B9, Concat (Concat (atoms), last))),
			Master (0x1F43B675, new byte[] { 1 }))));

		var file = MatroskaFile.Read (data).File!;
		var edition = file.Chapters.Editions[0];

		Assert.IsTrue (edition.Chapters.Count < count);
		edition.Chapters[0].Title = "First";
		var bytes = Save (file, data);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (last) >= 0);
		Assert.AreEqual ("First", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void ReadChapters_OnlyAKeptEdition_IsNotEmpty ()
	{
		// The first edition spends the budget on unknown children. The second, which has the only
		// chapter, is kept as bytes. The element still has a chapter and still saves.
		var unknown = new byte[3 * (MatroskaChapters.MaxEntries + 10)];
		for (var i = 0; i < unknown.Length; i += 3) {
			unknown[i] = 0x45;
			unknown[i + 1] = 0x01;
			unknown[i + 2] = 0x80;
		}

		var kept = EditionEntry (Atom (7, 0, null, Display ("Kept")));
		var data = Matroska (BigElement (0x18538067, Concat (
			BigElement (0x1043A770, Concat (BigElement (0x45B9, unknown), kept)),
			Master (0x1F43B675, new byte[] { 1 }))));

		var file = MatroskaFile.Read (data).File!;

		Assert.IsFalse (file.Chapters.IsEmpty);
		file.Tag.Artist = "Someone";
		var bytes = Save (file, data);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (kept) >= 0);
	}

	[TestMethod]
	public void SaveChapters_WebM_WritesOnlyWhatWebMSupports ()
	{
		// Unmodelled children WebM marks unsupported are left out too, not only the modelled flags.
		var untouched = Atom (2, Second, null,
			UInt (0x98, 1),
			UInt (0x4598, 0),
			Element (0x6E67, new byte[16]),
			UInt (0x63C3, 60),
			Master (0x8F, UInt (0x89, 1)),
			Master (0x6944, UInt (0x6955, 0)),
			Master (0x80, Concat (Element (0x85, "Two"u8), Element (0x437D, "en-US"u8))));
		var edition = EditionEntry (
			UInt (0x45BC, 5),
			UInt (0x45DB, 1),
			Master (0x4520, Element (0x4521, "Edition name"u8)),
			Atom (1, 0, null, Display ("One")),
			untouched);
		var original = Matroska (Master (0x18538067, Concat (ChaptersElement (edition), Master (0x1F43B675, new byte[] { 1 }))), "webm");
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].Chapters[0].Title = "Edited";

		var bytes = Save (file, original);

		var ids = new List<ulong> ();
		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == 0x1043A770)
				CollectIds (body.AsSpan (element.DataOffset, (int)element.DataLength), ids);
			offset = element.End;
		}

		Assert.IsTrue (ids.Count > 0);
		foreach (var id in ids)
			Assert.IsTrue (WebMChapterIds.Contains (id), id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
		var again = MatroskaFile.Read (bytes).File!.Chapters.Editions[0];
		Assert.AreEqual ("Edited", again.Chapters[0].Title);
		Assert.AreEqual ("Two", again.Chapters[1].Title);
	}

	[TestMethod]
	public void SaveChapters_EndBeforeStart_FailsAndLeavesTheFile ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.AddChapter (TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (5), "Backwards");

		AssertFailure (file, new MockFileSystem (), "/backwards.mkv", original, "ends before it starts");
		Assert.AreEqual (0, file.Render (original).Length);
	}

	[TestMethod]
	public async Task SaveChaptersAsync_EndBeforeStart_Fails ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.AddChapter (TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (5), "Backwards");
		var fs = new MockFileSystem ();
		fs.AddFile ("/async.mkv", original);

		var saved = await file.SaveToFileAsync ("/async.mkv", original, fs);

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "ends before it starts");
		CollectionAssert.AreEqual (original, fs.ReadAllBytes ("/async.mkv"));
	}

	[TestMethod]
	public void SaveChapters_OrderedEditionWithoutAnEnd_Fails ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		var parent = file.Chapters.AddChapter (TimeSpan.Zero, null, "Parent");
		parent.AddChapter (TimeSpan.FromSeconds (1), TimeSpan.FromSeconds (2), "Leaf with an end");
		file.Chapters.AddChapter (TimeSpan.FromSeconds (3), null, "Leaf without an end");
		file.Chapters.Editions[0].IsOrdered = true;

		AssertFailure (file, new MockFileSystem (), "/ordered.mkv", original, "ordered");

		// A parent chapter needs no end; only the lowest level does (RFC 9559 section 20.2.4).
		file.Chapters.Editions[0].Chapters[1].End = TimeSpan.FromSeconds (4);
		Assert.IsTrue (Save (file, original).Length > 0);
	}

	[TestMethod]
	public void SaveChapters_NestedOutsideItsParent_Fails ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		var parent = file.Chapters.AddChapter (TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (20), "Parent");
		var early = parent.AddChapter (TimeSpan.FromSeconds (5), null, "Before the parent");

		AssertFailure (file, new MockFileSystem (), "/early.mkv", original, "outside its parent");
		early.Start = TimeSpan.FromSeconds (25);
		AssertFailure (file, new MockFileSystem (), "/late.mkv", original, "outside its parent");
		early.Start = TimeSpan.FromSeconds (20);
		Assert.IsTrue (Save (file, original).Length > 0);
	}

	[TestMethod]
	public void ChapterTimes_PastTheUnsignedRange_Throw ()
	{
		// 2^64 - 1 ns is about 584.5 years. A larger TimeSpan does not wrap.
		var chapter = new MatroskaChapter ();
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => chapter.Start = TimeSpan.FromDays (600 * 365.25));
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => chapter.End = TimeSpan.MaxValue);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => chapter.End = TimeSpan.FromTicks (-1));

		chapter.Start = TimeSpan.FromTicks ((long)(ulong.MaxValue / 100));
		Assert.AreEqual (ulong.MaxValue / 100 * 100, chapter.StartNanoseconds);
	}

	[TestMethod]
	public void SaveChapters_EditedAtom_DropsItsVoidAndCrc32 ()
	{
		// RFC 8794 section 11.3.1: a CRC-32 is the first child and covers the rest. An edited atom
		// gets new bytes, so the old CRC-32 and the Void go.
		var body = Concat (UInt (0x73C4, 1), UInt (0x91, 0), Display ("Old"), Element (0xEC, new byte[] { 0, 0 }));
		var crc = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (crc, new BinaryData (body).ComputeCrc32 ());
		var atom = Master (0xB6, Concat (Element (0xBF, crc), body));
		var original = FileWith (ChaptersElement (EditionEntry (atom)), Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Chapters.Editions[0].Chapters[0].Title = "New";

		var bytes = Save (file, original);

		var ids = new List<ulong> ();
		Assert.IsTrue (TrySegmentBody (bytes, out var segment, out _));
		var offset = 0;
		while (offset < segment.Length && EbmlReader.TryRead (segment, offset, segment.Length, out var element) && !element.UnknownSize) {
			if (element.Id == 0x1043A770)
				CollectIds (segment.AsSpan (element.DataOffset, (int)element.DataLength), ids);
			offset = element.End;
		}

		CollectionAssert.DoesNotContain (ids, 0xBFUL);
		CollectionAssert.DoesNotContain (ids, 0xECUL);
		Assert.AreEqual ("New", MatroskaFile.Read (bytes).File!.Chapters.Editions[0].Chapters[0].Title);
	}

	[TestMethod]
	public void SaveChapters_NestedDeeperThanTheCapInCode_WritesTheCap ()
	{
		var original = FileWith (Master (0x1F43B675, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		var chapter = file.Chapters.AddChapter (TimeSpan.Zero, null, "Level 1");
		for (var level = 2; level <= MatroskaChapters.MaxDepth + 8; level++)
			chapter = chapter.AddChapter (TimeSpan.Zero, null, "Level " + level.ToString (System.Globalization.CultureInfo.InvariantCulture));

		var bytes = Save (file, original);

		Assert.AreEqual (MatroskaChapters.MaxDepth, MaxAtomDepth (bytes));
	}

	[TestMethod]
	public void SaveChapters_Clear_RemovesChaptersWithoutAtoms ()
	{
		// Editions without a ChapterAtom, or only a Void, render to nothing. Clear still removes the element.
		var atomless = FileWith (ChaptersElement (EditionEntry (UInt (0x45BC, 3))), Master (0x1F43B675, new byte[] { 1 }));
		var voidOnly = FileWith (ChaptersElement (Element (0xEC, new byte[4])), Master (0x1F43B675, new byte[] { 1 }));
		foreach (var original in new[] { atomless, voidOnly }) {
			var file = MatroskaFile.Read (original).File!;
			Assert.IsTrue (file.Chapters.IsEmpty);
			CollectionAssert.AreEqual (original, Save (file, original));

			file.Chapters.Clear ();
			var bytes = Save (file, original);

			Assert.AreEqual (0, TopLevelCount (bytes, MatroskaIds.Chapters));
			Assert.AreEqual (original.Length, bytes.Length);
		}
	}

	static void CollectIds (ReadOnlySpan<byte> body, List<ulong> ids)
	{
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			ids.Add (element.Id);
			if (element.Id is 0x45B9 or 0xB6 or 0x80 or 0x4520 or 0x8F or 0x6944)
				CollectIds (body.Slice (element.DataOffset, (int)element.DataLength), ids);
			offset = element.End;
		}
	}

	/// <summary>How many ChapterAtom elements nest inside one another in the saved Chapters.</summary>
	static int MaxAtomDepth (byte[] file)
	{
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == 0x1043A770)
				return AtomDepth (body.AsSpan (element.DataOffset, (int)element.DataLength));
			offset = element.End;
		}

		return 0;
	}

	static int AtomDepth (ReadOnlySpan<byte> body)
	{
		var deepest = 0;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == 0xB6)
				deepest = Math.Max (deepest, 1 + AtomDepth (payload));
			else if (element.Id == 0x45B9)
				deepest = Math.Max (deepest, AtomDepth (payload));
			offset = element.End;
		}

		return deepest;
	}
}
