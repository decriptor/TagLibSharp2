// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

/// <summary>
/// Invariant: after any save, every idx1 and OpenDML index entry resolves to the same sample bytes
/// it resolved to before, and the movi sample bytes are unchanged.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Avi")]
public class AviSaveTests
{
	static readonly string LongText = new ('x', 3000);
	static readonly string[] TaggedLayout = ["LIST hdrl", "LIST INFO", "JUNK", "LIST movi", "idx1"];
	static readonly string[] SplitSlotLayout = ["LIST hdrl", "JUNK", "IDVX", "LIST INFO", "JUNK", "LIST movi", "idx1"];

	[TestMethod]
	public void Save_UnchangedTags_WritesTheOriginalBytes ()
	{
		var builder = new AviTestBuilder { Id3 = AviFileTests.Id3 ("Title", "Album"), JunkFill = 0xAB };
		builder.Info.AddRange ([("INAM", "Movie"), ("ISFT", "Writer")]);
		builder.InfoAfterMovi.Add (("IART", "Later"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;

		Assert.IsTrue (file.Render (original).Span.SequenceEqual (original));
		file.InfoTag.Title = "Changed";
		file.InfoTag.Title = "Movie";
		Assert.IsTrue (file.Render (original).Span.SequenceEqual (original), "A tag set back to its value is unchanged.");
	}

	[TestMethod]
	public void Save_TagFitsTheJunk_KeepsMoviInPlace ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Old"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "A longer title than before";
		file.InfoTag.Artist = "Someone";

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		Assert.AreEqual (Offset (original, "LIST movi"), Offset (saved, "LIST movi"));
		AssertSamplesKept (original, saved);
		var again = AviFile.Read (saved).File!;
		Assert.AreEqual ("A longer title than before", again.InfoTag.Title);
		Assert.AreEqual ("Someone", again.InfoTag.Artist);
		CollectionAssert.AreEqual (
			TaggedLayout,
			Names (saved));
	}

	[TestMethod]
	public void Save_NewTagOnAnUntaggedFile_UsesTheJunk ()
	{
		var original = new AviTestBuilder ().Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "Fresh";

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		Assert.AreEqual ("Fresh", AviFile.Read (saved).File!.InfoTag.Title);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_ClearedTag_LeavesJunkOfTheSameSize ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Gone"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Clear ();

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		CollectionAssert.DoesNotContain (Names (saved), "LIST INFO");
		Assert.IsTrue (AviFile.Read (saved).File!.InfoTag.IsEmpty);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_InfoAfterMovi_BecomesJunkAndTheTagMovesBeforeMovi ()
	{
		var builder = new AviTestBuilder ();
		builder.InfoAfterMovi.Add (("INAM", "Trailing"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Artist = "Added";

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		var names = Names (saved);
		Assert.AreEqual (1, names.Count (n => n == "LIST INFO"));
		Assert.IsLessThan (names.IndexOf ("LIST movi"), names.IndexOf ("LIST INFO"));
		Assert.AreEqual ("JUNK", names[^1]);
		var again = AviFile.Read (saved).File!.InfoTag;
		Assert.AreEqual ("Trailing", again.Title);
		Assert.AreEqual ("Added", again.Artist);
	}

	[TestMethod]
	public void Save_SlotSplitByAnotherChunk_PicksTheSlotThatFits ()
	{
		var builder = new AviTestBuilder { ChunkBeforeJunk = ("IDVX", new byte[128]), JunkBeforeMovi = 200 };
		builder.Info.Add (("INAM", "A"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = new string ('t', 150);

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		CollectionAssert.AreEqual (
			SplitSlotLayout,
			Names (saved));
		Assert.AreEqual (new string ('t', 150), AviFile.Read (saved).File!.InfoTag.Title);
	}

	[TestMethod]
	[DataRow (16)]
	[DataRow (24)]
	[DataRow (100)]
	public void Save_SlotThatHoldsTheTag_StaysInPlace (int junk)
	{
		// INAM "abc" renders as LIST (8) + INFO (4) + INAM (8) + "abc\0" (4) = 24 bytes.
		// A 24-byte slot holds it exactly; a 32-byte slot holds it and an empty JUNK.
		var original = Untagged (junk);
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "abc";

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		Assert.AreEqual ("abc", AviFile.Read (saved).File!.InfoTag.Title);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	[DataRow (18)]
	[DataRow (20)]
	[DataRow (22)]
	public void Save_SlotTooSmallForAJunkHeader_MovesTheData (int junk)
	{
		// 2 to 6 bytes left over cannot hold a JUNK header, so the tag does not fit in place.
		var original = Untagged (junk);
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "abc";

		var saved = file.Render (original).ToArray ();

		Assert.IsGreaterThan (original.Length, saved.Length);
		Assert.AreEqual ("abc", AviFile.Read (saved).File!.InfoTag.Title);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_TagGrowsPastTheJunk_MovesMoviAndKeepsARelativeIndex ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Short"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var saved = file.Render (original).ToArray ();

		var delta = saved.Length - original.Length;
		Assert.IsGreaterThan (0, delta);
		Assert.AreEqual (Offset (original, "LIST movi") + delta, Offset (saved, "LIST movi"));
		Assert.AreEqual ((uint)(saved.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian (saved.AsSpan (4)));
		AssertSamplesKept (original, saved);
		CollectionAssert.AreEqual (OldIndexOffsets (original), OldIndexOffsets (saved), "Relative offsets do not change.");
		Assert.AreEqual (LongText, AviFile.Read (saved).File!.InfoTag.Comment);
		var junk = TopLevel (saved).First (c => c.Name == "JUNK");
		Assert.IsGreaterThanOrEqualTo ((uint)AviRewrite.ReservedJunkBytes, junk.Size);
	}

	[TestMethod]
	public void Save_AbsoluteIdx1_ShiftsEveryEntry ()
	{
		var builder = new AviTestBuilder { OldIndex = AviOldIndex.Absolute };
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var saved = file.Render (original).ToArray ();

		var delta = (uint)(saved.Length - original.Length);
		CollectionAssert.AreEqual (OldIndexOffsets (original).Select (o => o + delta).ToList (), OldIndexOffsets (saved));
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_OpenDml_ShiftsSuperIndexAndBaseOffsets ()
	{
		var builder = new AviTestBuilder { OpenDml = true, Frames = 4, AvixFrames = 5 };
		builder.Info.Add (("INAM", "OpenDML"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var saved = file.Render (original).ToArray ();

		var delta = saved.Length - original.Length;
		Assert.IsGreaterThan (0, delta);
		AssertSamplesKept (original, saved);
		var avix = TopLevelForms (saved);
		Assert.AreEqual (TopLevelForms (original)[1] + delta, avix[1], "The AVIX form follows the grown first form.");
		Assert.AreEqual ("AVIX", System.Text.Encoding.Latin1.GetString (saved, (int)avix[1] + 8, 4));
		Assert.AreEqual (TimeSpan.FromSeconds (9d / 25), AviFile.Read (saved).File!.Duration);
	}

	[TestMethod]
	public void Save_TwoSuperIndexEntriesForOneIndex_ShiftsItsBaseOnce ()
	{
		var builder = new AviTestBuilder { OpenDml = true, DuplicateSuperIndexEntry = true };
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var saved = file.Render (original).ToArray ();

		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_UnknownIndexType_RefusesToMove ()
	{
		var builder = new AviTestBuilder { OpenDml = true, SuperIndexType = 0x80 };
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;
		var fs = new MockFileSystem ();

		var saved = file.SaveToFile ("/out.avi", original, fs);

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "OpenDML index");
		Assert.IsFalse (fs.FileExists ("/out.avi"));
		Assert.AreEqual (0, file.Render (original).Length);

		file.InfoTag.Comment = "short";
		Assert.IsTrue (file.SaveToFile ("/out.avi", original, fs).IsSuccess, "A tag that fits never needs the index.");
	}

	[TestMethod]
	public void Save_StandardIndexInTheStreamList_ShiftsItsBase ()
	{
		var original = new AviTestBuilder { OpenDml = true, SuperIndexType = 0x01 }.Build ();
		// The video indx is now read as a standard index whose qwBaseOffset (data + 12) is zero.
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var saved = file.Render (original).ToArray ();

		Assert.IsGreaterThan (original.Length, saved.Length);
	}

	[TestMethod]
	public void Save_SuperIndexPointingIntoJunk_RefusesToMove ()
	{
		var original = new AviTestBuilder { OpenDml = true }.Build ();
		var indx = AviFileTests.IndexOf (original, "indx"u8) + 8;
		var junk = Offset (original, "JUNK");
		BinaryPrimitives.WriteUInt64LittleEndian (original.AsSpan (indx + 24), (ulong)junk);
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		Assert.AreEqual (0, file.Render (original).Length);
	}

	[TestMethod]
	public void Save_SuperIndexCountPastTheChunk_RefusesToMove ()
	{
		var original = new AviTestBuilder { OpenDml = true }.Build ();
		var indx = AviFileTests.IndexOf (original, "indx"u8) + 8;
		BinaryPrimitives.WriteUInt32LittleEndian (original.AsSpan (indx + 4), 1000);
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		Assert.AreEqual (0, file.Render (original).Length);
	}

	[TestMethod]
	public void Save_Idx1MatchingNeitherBase_RefusesToMoveButSavesInPlace ()
	{
		var original = new AviTestBuilder ().Build ();
		var idx1 = (int)Offset (original, "idx1") + 8;
		for (var at = idx1; at + 16 <= original.Length; at += 16)
			BinaryPrimitives.WriteUInt32LittleEndian (original.AsSpan (at + 8), 3);
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var fs = new MockFileSystem ();
		var moved = file.SaveToFile ("/x.avi", original, fs);
		Assert.IsFalse (moved.IsSuccess);
		StringAssert.Contains (moved.Error, "idx1");

		file.InfoTag.Comment = "fits";
		Assert.IsTrue (file.SaveToFile ("/x.avi", original, fs).IsSuccess);
	}

	[TestMethod]
	public void Save_PaddingGranularity_MovesByAMultiple ()
	{
		var original = new AviTestBuilder { PaddingGranularity = 2048 }.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Comment = LongText;

		var saved = file.Render (original).ToArray ();

		Assert.AreEqual (0, (saved.Length - original.Length) % 2048);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_NoJunkAndNoTag_InsertsTheTagBeforeMovi ()
	{
		var original = new AviTestBuilder { JunkBeforeMovi = -1 }.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "Inserted";

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (
			TaggedLayout,
			Names (saved));
		Assert.AreEqual ("Inserted", AviFile.Read (saved).File!.InfoTag.Title);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void Save_NoMovi_Fails ()
	{
		var original = new AviTestBuilder { WithMovi = false }.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "x";

		var saved = file.SaveToFile ("/x.avi", original, new MockFileSystem ());

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "movi");
	}

	[TestMethod]
	public void Save_NotAnAviFile_Fails ()
	{
		var file = AviFile.Read (new AviTestBuilder ().Build ()).File!;

		Assert.IsFalse (file.SaveToFile ("/x.avi", TestBuilders.Wav.CreateMinimal (), new MockFileSystem ()).IsSuccess);
	}

	[TestMethod]
	public void Save_RiffSizePlaceholder_SavesInPlaceButRefusesToMove ()
	{
		var original = new AviTestBuilder ().Build ();
		BinaryPrimitives.WriteUInt32LittleEndian (original.AsSpan (4), 0xFFFFFFFF);
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "fits";
		Assert.HasCount (original.Length, file.Render (original).ToArray ());

		file.InfoTag.Comment = LongText;
		Assert.AreEqual (0, file.Render (original).Length);
	}

	[TestMethod]
	public void Save_ChangedId3_RewritesOnlyTheId3Chunk ()
	{
		var builder = new AviTestBuilder { Id3 = AviFileTests.Id3 ("Old", "Album") };
		builder.Info.Add (("INAM", "Info"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.Id3v2Tag!.Title = "New";

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		Assert.AreEqual (Offset (original, "LIST INFO"), Offset (saved, "LIST INFO"), "The unchanged INFO list keeps its place.");
		var again = AviFile.Read (saved).File!;
		Assert.AreEqual ("New", again.Id3v2Tag!.Title);
		Assert.AreEqual ("Info", again.InfoTag.Title);
	}

	[TestMethod]
	public void Save_RemovedId3_DropsTheChunk ()
	{
		var original = new AviTestBuilder { Id3 = AviFileTests.Id3 ("Old", "Album") }.Build ();
		var file = AviFile.Read (original).File!;
		file.Id3v2Tag = null;

		var saved = file.Render (original).ToArray ();

		CollectionAssert.DoesNotContain (Names (saved), "ID32");
		Assert.IsNull (AviFile.Read (saved).File!.Id3v2Tag);
	}

	[TestMethod]
	public void Save_NewId3_AddsAnId32Chunk ()
	{
		var original = new AviTestBuilder ().Build ();
		var file = AviFile.Read (original).File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "Added" };

		var saved = file.Render (original).ToArray ();

		CollectionAssert.Contains (Names (saved), "ID32");
		Assert.AreEqual ("Added", AviFile.Read (saved).File!.Id3v2Tag!.Title);
		file.Id3v2Tag = new Id3v2Tag ();
		Assert.IsTrue (file.Render (original).Span.SequenceEqual (original), "An empty new ID3v2 tag adds nothing.");
	}

	[TestMethod]
	public void Save_UnparsableId3_IsKeptWhileTheTagStaysNull ()
	{
		var builder = new AviTestBuilder { Id3 = [1, 2, 3, 4] };
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "x";

		var saved = file.Render (original).ToArray ();
		Assert.AreEqual (Offset (original, "ID32"), Offset (saved, "ID32"));

		file.Id3v2Tag = new Id3v2Tag { Title = "Replaced" };
		var replaced = file.Render (original).ToArray ();
		Assert.AreEqual ("Replaced", AviFile.Read (replaced).File!.Id3v2Tag!.Title);
		Assert.AreEqual (1, Names (replaced).Count (n => n == "ID32"));
	}

	[TestMethod]
	public void Save_UnifiedTagWritesBothTags ()
	{
		var builder = new AviTestBuilder { Id3 = AviFileTests.Id3 ("Old", "Album") };
		builder.Info.Add (("INAM", "Old"));
		var original = builder.Build ();
		var result = MediaFile.ReadFromData (original);
		result.Tag!.Title = "Both";

		var saved = result.GetFileAs<AviFile> ()!.Render (original).ToArray ();

		var again = AviFile.Read (saved).File!;
		Assert.AreEqual ("Both", again.InfoTag.Title);
		Assert.AreEqual ("Both", again.Id3v2Tag!.Title);
		AssertSamplesKept (original, saved);
	}

	[TestMethod]
	public void SaveToFile_ReReadsTheSourceAndWritesAtomically ()
	{
		var builder = new AviTestBuilder ();
		builder.Info.Add (("INAM", "Disk"));
		var original = builder.Build ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.avi", original);
		var file = (AviFile)MediaFile.Read ("/clip.avi", fs).File!;
		file.InfoTag.Title = "Saved";

		Assert.IsTrue (file.SaveToFile ().IsSuccess);
		Assert.AreEqual ("Saved", AviFile.Read (fs.ReadAllBytes ("/clip.avi")).File!.InfoTag.Title);
		Assert.IsTrue (file.SaveToFile ("/copy.avi").IsSuccess);
		AssertSamplesKept (original, fs.ReadAllBytes ("/copy.avi"));
	}

	[TestMethod]
	public async Task SaveToFileAsync_ReReadsTheSource ()
	{
		var original = new AviTestBuilder { OpenDml = true, AvixFrames = 2 }.Build ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.avi", original);
		var file = (AviFile)(await MediaFile.ReadAsync ("/clip.avi", fs)).File!;
		file.InfoTag.Comment = LongText;

		var saved = await file.SaveToFileAsync ();

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		AssertSamplesKept (original, fs.ReadAllBytes ("/clip.avi"));
		Assert.IsTrue ((await file.SaveToFileAsync ("/copy.avi", fs)).IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/mem.avi", original, fs)).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/bad.avi", new byte[4], fs)).IsSuccess);
	}

	[TestMethod]
	public async Task Save_WithoutASourcePath_Fails ()
	{
		var file = AviFile.Read (new AviTestBuilder ().Build ()).File!;

		Assert.IsFalse (file.SaveToFile ().IsSuccess);
		Assert.IsFalse (file.SaveToFile ("/x.avi").IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ()).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/x.avi")).IsSuccess);
	}

	[TestMethod]
	public async Task Save_SourceGone_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.avi", new AviTestBuilder ().Build ());
		var file = AviFile.ReadFromFile ("/clip.avi", fs).File!;
		fs.Delete ("/clip.avi");

		Assert.IsFalse (file.SaveToFile ().IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ()).IsSuccess);
	}

	[TestMethod]
	public void Save_EveryTruncation_DoesNotThrowOrBreakTheSamples ()
	{
		var builder = new AviTestBuilder { OpenDml = true, AvixFrames = 2, Id3 = AviFileTests.Id3 ("T", "A") };
		builder.Info.Add (("INAM", "Cut"));
		var full = builder.Build ();
		var file = AviFile.Read (full).File!;
		file.InfoTag.Comment = LongText;

		for (var length = 0; length <= full.Length; length++) {
			var prefix = full.AsSpan (0, length).ToArray ();
			var saved = file.Render (prefix);
			if (saved.Length > 0)
				Assert.IsTrue (AviFile.Read (saved.Span).IsSuccess, $"length {length}");
		}
	}

	static byte[] Untagged (int junk) => new AviTestBuilder { JunkBeforeMovi = junk, OldIndex = AviOldIndex.Relative }.Build ();

	static void AssertSamplesKept (byte[] original, byte[] saved)
	{
		var before = AviTestBuilder.ResolveIndexes (original);
		var after = AviTestBuilder.ResolveIndexes (saved);
		Assert.IsNotEmpty (before);
		Assert.IsFalse (before.Any (e => e.EndsWith ("unresolved", StringComparison.Ordinal)), "The original's indexes resolve.");
		CollectionAssert.AreEqual (before, after);
	}

	static List<(string Name, long Offset, uint Size)> TopLevel (byte[] file) => AviTestBuilder.TopLevel (file);

	static List<string> Names (byte[] file)
	{
		var end = 8 + BinaryPrimitives.ReadUInt32LittleEndian (file.AsSpan (4));
		return TopLevel (file).Where (c => c.Offset < end).Select (c => c.Name).ToList ();
	}

	static long Offset (byte[] file, string name) => TopLevel (file).First (c => c.Name == name).Offset;

	static List<long> TopLevelForms (byte[] file)
	{
		var forms = new List<long> ();
		for (long p = 0; p + 8 <= file.Length;) {
			forms.Add (p);
			var size = BinaryPrimitives.ReadUInt32LittleEndian (file.AsSpan ((int)p + 4));
			p += 8 + size + (size & 1);
		}
		return forms;
	}

	static List<uint> OldIndexOffsets (byte[] file)
	{
		var idx1 = Offset (file, "idx1");
		var size = BinaryPrimitives.ReadUInt32LittleEndian (file.AsSpan ((int)idx1 + 4));
		var offsets = new List<uint> ();
		for (var at = idx1 + 8; at < idx1 + 8 + size; at += 16)
			offsets.Add (BinaryPrimitives.ReadUInt32LittleEndian (file.AsSpan ((int)at + 8)));
		return offsets;
	}
}
