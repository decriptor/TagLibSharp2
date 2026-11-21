// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using TagLibSharp2.Audible;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Audible;

/// <summary>
/// A save rewrites the dictionary only. Every other chunk keeps its bytes, every TOC offset past the
/// dictionary moves by the size change, and the header, whole-file entry, and cover position follow.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Audible")]
public class AudibleRoundTripTests
{
	static readonly string[] KeptByClear = [
		"product_id", "title_id", "codec", "HeaderSeed", "EncryptedBlocks", "HeaderKey",
		"license_list", "CPUType", "license_count", "68ec733412b9", "user_alias"
	];

	[TestMethod]
	public void Render_Unchanged_IsByteIdentical ()
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var file = AudibleFile.Read (data).File!;

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_SameValueSetAgain_IsByteIdentical ()
	{
		var data = AudibleTestBuilder.Create ();
		var file = AudibleFile.Read (data).File!;
		file.Tag.Title = file.Tag.Title;
		file.Tag.Narrator = file.Tag.Narrator;

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_NonAsciiKeyAndInvalidUtf8Value_AreWrittenBackUnchanged ()
	{
		var dictionary = AudibleTestBuilder.Dictionary ([("title", "T"), ("codec", "acelp85")]);
		// Append an entry with a 0xE9 key byte and a lone 0xFF value byte, then fix the count.
		var odd = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 1, (byte)'k', 0xE9, 0xFF };
		dictionary = AudibleTestBuilder.Concat (dictionary, odd);
		BinaryPrimitives.WriteUInt32BigEndian (dictionary, 3);
		var data = AudibleTestBuilder.CreateRaw (dictionary, AudibleTestBuilder.Audio (100), null);
		var file = AudibleFile.Read (data).File!;

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_BytesAfterTheLastEntry_AreKept ()
	{
		var dictionary = AudibleTestBuilder.Concat (
			AudibleTestBuilder.Dictionary ([("title", "T"), ("codec", "acelp85")]), [0xAB, 0xCD, 0xEF]);
		var data = AudibleTestBuilder.CreateRaw (dictionary, AudibleTestBuilder.Audio (100), null);
		var file = AudibleFile.Read (data).File!;
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Tag.Title = "Longer title";
		var chunk = AudibleTestBuilder.Chunk (file.Render (data).ToArray (), 2);

		CollectionAssert.AreEqual (new byte[] { 0xAB, 0xCD, 0xEF }, chunk[^3..]);
	}

	[TestMethod]
	[DataRow ("x")]
	[DataRow ("A much, much longer replacement title for this audiobook – with UTF-8 ✓")]
	public void Render_TitleChange_MovesEveryLaterChunkAndKeepsTheirBytes (string title)
	{
		var jpeg = AudibleTestBuilder.Jpeg ();
		var data = AudibleTestBuilder.Create (cover: jpeg);
		var file = AudibleFile.Read (data).File!;
		file.Tag.Title = title;

		var output = file.Render (data).ToArray ();

		var oldDictionary = AudibleTestBuilder.Toc (data, 2);
		var newDictionary = AudibleTestBuilder.Toc (output, 2);
		var delta = (long)newDictionary.Size - oldDictionary.Size;
		Assert.AreNotEqual (0L, delta);
		Assert.AreEqual (data.Length + delta, output.Length);
		Assert.AreEqual ((uint)output.Length, BinaryPrimitives.ReadUInt32BigEndian (output));
		CollectionAssert.AreEqual (data.AsSpan (4, 12).ToArray (), output.AsSpan (4, 12).ToArray (), "magic, count, and unknown word");

		var whole = AudibleTestBuilder.Toc (output, 0);
		Assert.AreEqual ((0u, 0u, (uint)output.Length), whole);

		for (var i = 1; i < 12; i++) {
			var before = AudibleTestBuilder.Toc (data, i);
			var after = AudibleTestBuilder.Toc (output, i);
			Assert.AreEqual (before.Type, after.Type);
			if (i < 2) {
				Assert.AreEqual (before, after, $"chunk {i} is before the dictionary");
			} else if (i > 2) {
				Assert.AreEqual (before.Offset + delta, (long)after.Offset, $"chunk {i} offset");
				Assert.AreEqual (before.Size, after.Size, $"chunk {i} size");
			}
			if (i != 2 && i != 11)
				CollectionAssert.AreEqual (AudibleTestBuilder.Chunk (data, i), AudibleTestBuilder.Chunk (output, i), $"chunk {i} bytes");
		}

		var cover = AudibleTestBuilder.Toc (output, 11);
		Assert.AreEqual (cover.Offset + 8, BinaryPrimitives.ReadUInt32BigEndian (output.AsSpan ((int)cover.Offset + 4)), "cover position");
		Assert.AreEqual ((uint)jpeg.Length, BinaryPrimitives.ReadUInt32BigEndian (output.AsSpan ((int)cover.Offset)));

		var reread = AudibleFile.Read (output).File!;
		Assert.AreEqual (title, reread.Tag.Title);
		CollectionAssert.AreEqual (jpeg, reread.Tag.Pictures[0].PictureData.ToArray ());
		Assert.AreEqual (file.Properties, reread.Properties);
		CollectionAssert.AreEqual (file.Tag.Keys.ToList (), reread.Tag.Keys.ToList ());
		foreach (var key in reread.Tag.Keys.Where (k => k != "title"))
			Assert.AreEqual (AudibleFile.Read (data).File!.Tag.GetValue (key), reread.Tag.GetValue (key), key);
	}

	[TestMethod]
	public void Render_TruncatedFile_ShiftsTheDeclaredSizeAndOffsetsByTheSameAmount ()
	{
		var full = AudibleTestBuilder.Create (audio: new byte[85_000], cover: AudibleTestBuilder.Jpeg ());
		var (_, dictionaryOffset, dictionarySize) = AudibleTestBuilder.Toc (full, 2);
		var cut = full.AsSpan (0, (int)(dictionaryOffset + dictionarySize + 300)).ToArray ();
		var file = AudibleFile.Read (cut).File!;
		file.Tag.Narrator = "Someone With A Much Longer Name Than Before";

		var output = file.Render (cut).ToArray ();

		var delta = (long)AudibleTestBuilder.Toc (output, 2).Size - dictionarySize;
		Assert.AreEqual (cut.Length + delta, output.Length);
		Assert.AreEqual (full.Length + delta, BinaryPrimitives.ReadUInt32BigEndian (output));
		Assert.AreEqual (full.Length + delta, AudibleTestBuilder.Toc (output, 0).Size);
		for (var i = 3; i < 12; i++)
			Assert.AreEqual (AudibleTestBuilder.Toc (full, i).Offset + delta, (long)AudibleTestBuilder.Toc (output, i).Offset);
		CollectionAssert.AreEqual (
			cut.AsSpan ((int)(dictionaryOffset + dictionarySize)).ToArray (),
			output.AsSpan ((int)(dictionaryOffset + dictionarySize + delta)).ToArray ());

		var reread = AudibleFile.Read (output).File!;
		Assert.AreEqual ("Someone With A Much Longer Name Than Before", reread.Tag.Narrator);
		Assert.AreEqual (file.Properties.Duration, reread.Properties.Duration);
	}

	[TestMethod]
	public void Render_DataFromAnotherFile_ReturnsEmpty ()
	{
		var file = AudibleFile.Read (AudibleTestBuilder.Create ()).File!;
		var other = AudibleTestBuilder.Create (audio: AudibleTestBuilder.Audio (999));

		Assert.IsTrue (file.Render (other).IsEmpty);
		Assert.IsTrue (file.Render (new byte[10]).IsEmpty);
	}

	[TestMethod]
	public void Render_ChunkThatEndsInsideTheDictionary_ReturnsEmpty ()
	{
		var data = AudibleTestBuilder.Create ();
		var (_, dictionaryOffset, _) = AudibleTestBuilder.Toc (data, 2);
		// Chunk 1 now runs 10 bytes into the dictionary; moving the dictionary would split it.
		var (_, oneOffset, _) = AudibleTestBuilder.Toc (data, 1);
		AudibleTestBuilder.SetU32 (data, 16 + 12 + 8, dictionaryOffset - oneOffset + 10);
		var file = AudibleFile.Read (data).File!;
		file.Tag.Title = "Changed";

		Assert.IsTrue (file.Render (data).IsEmpty);
		Assert.IsFalse (file.SaveToFile ("/out.aa", data, new MockFileSystem ()).IsSuccess);
	}

	[TestMethod]
	public void Render_CoverWithAForeignPosition_LeavesThePositionAlone ()
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var (_, coverOffset, _) = AudibleTestBuilder.Toc (data, 11);
		AudibleTestBuilder.SetU32 (data, (int)coverOffset + 4, 7);
		var file = AudibleFile.Read (data).File!;
		file.Tag.Title = "Changed title";

		var output = file.Render (data).ToArray ();

		var (_, newOffset, _) = AudibleTestBuilder.Toc (output, 11);
		Assert.AreEqual (7u, BinaryPrimitives.ReadUInt32BigEndian (output.AsSpan ((int)newOffset + 4)));
	}

	[TestMethod]
	public void Render_ClearedTag_KeepsDrmAndIdentifierKeys ()
	{
		var data = AudibleTestBuilder.Create ();
		var file = AudibleFile.Read (data).File!;
		file.Tag.Clear ();

		var reread = AudibleFile.Read (file.Render (data).Span).File!;

		Assert.IsTrue (reread.Tag.IsEmpty);
		CollectionAssert.AreEqual (KeptByClear, reread.Tag.Keys.ToList ());
		Assert.AreEqual (file.Properties.Duration, reread.Properties.Duration);
	}

	[TestMethod]
	public void SaveToFile_FromPath_RewritesAndCanSaveAgain ()
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", data);
		var file = AudibleFile.ReadFromFile ("/book.aa", fs).File!;

		file.Tag.Title = "First save with a longer title";
		Assert.IsTrue (file.SaveToFile (fs).IsSuccess);
		file.Tag.Title = "Second";
		var second = file.SaveToFile (fs);

		Assert.IsTrue (second.IsSuccess, second.Error);
		var reread = AudibleFile.ReadFromFile ("/book.aa", fs).File!;
		Assert.AreEqual ("Second", reread.Tag.Title);
		Assert.HasCount (1, reread.Tag.Pictures);
		Assert.IsTrue (AudibleTestBuilder.IndexOf (fs.ReadAllBytes ("/book.aa"), AudibleTestBuilder.Audio (8500)) >= 0);
	}

	[TestMethod]
	public async Task SaveToFileAsync_FromPath_RewritesAndCanSaveAgain ()
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", data);
		var file = (await AudibleFile.ReadFromFileAsync ("/book.aa", fs)).File!;

		file.Tag.Narrator = "First async save";
		Assert.IsTrue ((await file.SaveToFileAsync (fs)).IsSuccess);
		file.Tag.Narrator = "Second";
		var second = await file.SaveToFileAsync (fs);

		Assert.IsTrue (second.IsSuccess, second.Error);
		Assert.AreEqual ("Second", AudibleFile.Read (fs.ReadAllBytes ("/book.aa")).File!.Tag.Narrator);
	}

	[TestMethod]
	public async Task SaveToFile_ToAnotherPath_LeavesTheSourceAndCanSaveAgain ()
	{
		var data = AudibleTestBuilder.Create ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", data);
		var file = AudibleFile.ReadFromFile ("/book.aa", fs).File!;
		file.Tag.Title = "Copy";

		Assert.IsTrue (file.SaveToFile ("/copy.aa", fs).IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/copy2.aa", fs)).IsSuccess);

		CollectionAssert.AreEqual (data, fs.ReadAllBytes ("/book.aa"));
		Assert.AreEqual ("Copy", AudibleFile.Read (fs.ReadAllBytes ("/copy.aa")).File!.Tag.Title);
		CollectionAssert.AreEqual (fs.ReadAllBytes ("/copy.aa"), fs.ReadAllBytes ("/copy2.aa"));
	}

	[TestMethod]
	public async Task SaveToFile_ReadFromBytes_UsesThoseBytes ()
	{
		var data = AudibleTestBuilder.Create ();
		var file = AudibleFile.Read (data).File!;
		file.Tag.Title = "From bytes";
		var fs = new MockFileSystem ();

		Assert.IsTrue (file.SaveToFile ("/a.aa", fs).IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/b.aa", fs)).IsSuccess);
		Assert.IsTrue (file.SaveToFile ("/c.aa", data, fs).IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/d.aa", data, fs)).IsSuccess);

		foreach (var path in new[] { "/a.aa", "/b.aa", "/c.aa", "/d.aa" })
			Assert.AreEqual ("From bytes", AudibleFile.Read (fs.ReadAllBytes (path)).File!.Tag.Title, path);
	}

	[TestMethod]
	public async Task SaveToFile_StreamWithoutAPath_Fails ()
	{
		var opened = MediaFile.Read (new MemoryStream (AudibleTestBuilder.Create ()));
		var file = (AudibleFile)opened.File!;
		var fs = new MockFileSystem ();

		Assert.IsFalse (file.SaveToFile ("/x.aa", fs).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/x.aa", fs)).IsSuccess);
		Assert.IsFalse (file.SaveToFile (fs).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync (fs)).IsSuccess);
		Assert.IsFalse (fs.FileExists ("/x.aa"));
	}

	[TestMethod]
	public async Task SaveToFile_WrongOriginalData_Fails ()
	{
		var file = AudibleFile.Read (AudibleTestBuilder.Create ()).File!;
		var fs = new MockFileSystem ();

		Assert.IsFalse (file.SaveToFile ("/x.aa", new byte[20], fs).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/x.aa", new byte[20], fs)).IsSuccess);
		Assert.IsFalse (fs.FileExists ("/x.aa"));
	}

	[TestMethod]
	public async Task SaveToFile_SourceDeleted_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create ());
		var file = AudibleFile.ReadFromFile ("/book.aa", fs).File!;
		fs.Delete ("/book.aa");

		StringAssert.Contains (file.SaveToFile ("/out.aa", fs).Error, "re-read");
		StringAssert.Contains ((await file.SaveToFileAsync ("/out.aa", fs)).Error, "re-read");
	}
}
