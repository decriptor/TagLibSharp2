// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aac;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Tests.Core;

using static TagLibSharp2.Tests.Aac.AacTestBuilder;

namespace TagLibSharp2.Tests.Aac;

/// <summary>
/// Saving rewrites the leading ID3v2 tag and the trailing APEv2 and ID3v1 tags. The bytes between
/// the end of the read ID3v2 tag and the start of the trailing tags are copied unchanged.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aac")]
public class AacFileRoundTripTests
{
	static byte[] Frames () =>
		Concat (Adts (30, length: 90), Adts (20, length: 130, rawBlocks: 2));

	static AacFile Open (byte[] data)
	{
		var result = AacFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	static void AssertFramesKept (byte[] saved, byte[] frames)
	{
		var at = IndexOf (saved, frames);
		Assert.IsTrue (at >= 0, "the frame bytes were not copied unchanged");
		var reread = Open (saved);
		Assert.AreEqual (at, reread.Id3v2Size, "the frames must follow the ID3v2 tag directly");
		Assert.AreEqual (50, reread.Properties!.FrameCount);
	}

	[TestMethod]
	public void Save_Id3v2Title_KeepsTheFrames ()
	{
		var frames = Frames ();
		var original = Concat (Id3v2Bytes ("Old"), frames);
		var file = Open (original);

		file.Tag!.Title = "A much longer title than before";
		var saved = file.Render (original).ToArray ();

		AssertFramesKept (saved, frames);
		Assert.AreEqual ("A much longer title than before", Open (saved).Tag!.Title);
	}

	[TestMethod]
	public void Save_UntaggedFile_AddsAnId3v2Tag ()
	{
		var frames = Frames ();
		var file = Open (frames);

		file.EnsureId3v2Tag ().Title = "New";
		file.Tag!.Artist = "Artist";
		var saved = file.Render (frames).ToArray ();

		AssertFramesKept (saved, frames);
		var reread = Open (saved);
		Assert.AreEqual ("New", reread.Tag!.Title);
		Assert.AreEqual ("Artist", reread.Id3v2Tag!.Artist);
		Assert.IsNull (reread.ApeTag);
		Assert.IsNull (reread.Id3v1Tag);
	}

	[TestMethod]
	public void Save_AllTags_WritesEveryTagThroughTheView ()
	{
		var frames = Frames ();
		var original = Concat (Id3v2Bytes ("Front"), frames, ApeBytes ("Ape"), Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Tag!.Title = "Same";
		file.Tag.Album = "Album";
		var saved = file.Render (original).ToArray ();

		AssertFramesKept (saved, frames);
		var reread = Open (saved);
		Assert.AreEqual ("Same", reread.Id3v2Tag!.Title);
		Assert.AreEqual ("Same", reread.ApeTag!.Title);
		Assert.AreEqual ("Same", reread.Id3v1Tag!.Title);
		Assert.AreEqual ("Album", reread.ApeTag.Album);
		Assert.AreEqual (original.Length - Id3v2Bytes ("Front").Length - ApeBytes ("Ape").Length - 128, frames.Length);
	}

	[TestMethod]
	public void Save_RemovedTags_AreGoneAndTheFramesStay ()
	{
		var frames = Frames ();
		var original = Concat (Id3v2Bytes ("Front"), frames, ApeBytes ("Ape"), Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Id3v2Tag = null;
		file.ApeTag = null;
		file.Id3v1Tag = null;
		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (frames, saved);
	}

	[TestMethod]
	public void Save_UnchangedTags_ReproducesTheTrailingTagsAndTheFrames ()
	{
		var frames = Frames ();
		var ape = ApeBytes ("Ape");
		var id3v1 = Id3v1Bytes ("Back");
		var original = Concat (frames, ape, id3v1);

		var saved = Open (original).Render (original).ToArray ();

		CollectionAssert.AreEqual (original, saved);
	}

	[TestMethod]
	public void Save_UnreadableId3v2_KeepsItsBytesAsData ()
	{
		// A header that parses with a body that does not stays in the file.
		var broken = Id3v2Bytes ("Front");
		broken[3] = 9; // unsupported major version
		var frames = Frames ();
		var original = Concat (broken, frames);
		var file = Open (original);

		Assert.IsNull (file.Id3v2Tag);
		Assert.AreEqual (50, file.Properties!.FrameCount);
		var saved = file.Render (original).ToArray ();
		CollectionAssert.AreEqual (original, saved);
	}

	[TestMethod]
	public void Save_Lyrics3InFrontOfId3v1_IsKept ()
	{
		var body = "LYRICSBEGININD00002 10"u8.ToArray ();
		var block = Concat (body, "000022"u8.ToArray (), "LYRICS200"u8.ToArray ());
		var frames = Frames ();
		var original = Concat (frames, ApeBytes ("Ape"), block, Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Tag!.Title = "Edited";
		var saved = file.Render (original).ToArray ();

		Assert.IsTrue (IndexOf (saved, block) >= 0, "the Lyrics3 block was dropped");
		AssertFramesKept (saved, frames);
		Assert.AreEqual ("Edited", Open (saved).Id3v1Tag!.Title);
	}

	[TestMethod]
	public void Save_Lyrics3GoesWithTheId3v1Tag ()
	{
		var body = "LYRICSBEGININD00002 10"u8.ToArray ();
		var block = Concat (body, "000022"u8.ToArray (), "LYRICS200"u8.ToArray ());
		var frames = Frames ();
		var original = Concat (frames, block, Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Id3v1Tag = null;
		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (frames, saved);
	}

	[TestMethod]
	public void Save_Adif_KeepsTheHeaderAndPayload ()
	{
		var adif = Adif (64_000, payloadBytes: 5000);
		var original = Concat (Id3v2Bytes ("Front"), adif, Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Tag!.Title = "Edited";
		var saved = file.Render (original).ToArray ();

		Assert.IsTrue (IndexOf (saved, adif) >= 0);
		var reread = Open (saved);
		Assert.AreEqual ("Edited", reread.Id3v2Tag!.Title);
		Assert.AreEqual ("Edited", reread.Id3v1Tag!.Title);
		Assert.AreEqual (file.Properties!.Duration, reread.Properties!.Duration);
	}

	[TestMethod]
	public void SaveToFile_FromAPath_ReReadsTheSource ()
	{
		var frames = Frames ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Concat (Id3v2Bytes ("Old"), frames));
		var file = AacFile.ReadFromFile ("/a.aac", fs).File!;

		file.Tag!.Title = "New";
		var saved = file.SaveToFile ();

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var written = fs.ReadAllBytes ("/a.aac");
		AssertFramesKept (written, frames);
		Assert.AreEqual ("New", Open (written).Tag!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_FromAPath_ReReadsTheSource ()
	{
		var frames = Frames ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Concat (frames, Id3v1Bytes ("Old")));
		var file = (await AacFile.ReadFromFileAsync ("/a.aac", fs)).File!;

		file.Tag!.Title = "New";
		var saved = await file.SaveToFileAsync ();

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var written = fs.ReadAllBytes ("/a.aac");
		Assert.AreEqual ("New", Open (written).Id3v1Tag!.Title);
		Assert.IsTrue (IndexOf (written, frames) == 0);
	}

	[TestMethod]
	public void SaveToFile_ToAnotherPath_WithOriginalBytes ()
	{
		var frames = Frames ();
		var original = Concat (Id3v2Bytes ("Old"), frames);
		var fs = new MockFileSystem ();
		var file = Open (original);

		file.Tag!.Title = "Copy";
		var saved = file.SaveToFile ("/copy.aac", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Copy", Open (fs.ReadAllBytes ("/copy.aac")).Tag!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_ToAnotherPath_WithOriginalBytes ()
	{
		var frames = Frames ();
		var original = Concat (Id3v2Bytes ("Old"), frames);
		var fs = new MockFileSystem ();
		var file = Open (original);

		file.Tag!.Title = "Copy";
		var saved = await file.SaveToFileAsync ("/copy.aac", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Copy", Open (fs.ReadAllBytes ("/copy.aac")).Tag!.Title);
	}

	[TestMethod]
	public async Task Save_WithoutASourcePath_Fails ()
	{
		var file = Open (Frames ());

		Assert.IsFalse (file.SaveToFile ().IsSuccess);
		Assert.IsFalse (file.SaveToFile ("/x.aac").IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ()).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/x.aac")).IsSuccess);
	}

	[TestMethod]
	public async Task Save_SourceDeleted_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Frames ());
		var file = AacFile.ReadFromFile ("/a.aac", fs).File!;
		fs.Delete ("/a.aac");

		StringAssert.Contains (file.SaveToFile ("/b.aac").Error, "re-read");
		StringAssert.Contains ((await file.SaveToFileAsync ("/b.aac")).Error, "re-read");
	}

	[TestMethod]
	public void Render_EmptyOriginalAndNoTags_FailsTheSave ()
	{
		var file = Open (Frames ());
		var fs = new MockFileSystem ();

		Assert.IsFalse (file.SaveToFile ("/x.aac", [], fs).IsSuccess);
	}

	[TestMethod]
	public async Task RenderAsync_EmptyOriginalAndNoTags_FailsTheSave ()
	{
		var file = Open (Frames ());

		Assert.IsFalse ((await file.SaveToFileAsync ("/x.aac", ReadOnlyMemory<byte>.Empty, new MockFileSystem ())).IsSuccess);
	}

	[TestMethod]
	public void MediaFileRead_Path_SavesThroughTheSourcePath ()
	{
		var frames = Frames ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/a.aac", Concat (Id3v2Bytes ("Old"), frames, ApeBytes ("Ape")));
		var result = MediaFile.Read ("/a.aac", fs);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.GetFileAs<AacFile> ()!;

		result.Tag!.Title = "Streamed";
		var saved = file.SaveToFile (fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var reread = Open (fs.ReadAllBytes ("/a.aac"));
		Assert.AreEqual ("Streamed", reread.Id3v2Tag!.Title);
		Assert.AreEqual ("Streamed", reread.ApeTag!.Title);
		AssertFramesKept (fs.ReadAllBytes ("/a.aac"), frames);
	}

	[TestMethod]
	public void Id3v1Only_TagWritesStayInId3v1 ()
	{
		var frames = Frames ();
		var original = Concat (frames, Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Tag!.Title = "V1";
		var saved = file.Render (original).ToArray ();

		var reread = Open (saved);
		Assert.IsNull (reread.Id3v2Tag);
		Assert.AreEqual ("V1", reread.Id3v1Tag!.Title);
		Assert.AreEqual (frames.Length + 128, saved.Length);
	}

	[TestMethod]
	public void Clear_ThroughTheView_ClearsEveryTag ()
	{
		var original = Concat (Id3v2Bytes ("Front"), Frames (), ApeBytes ("Ape"), Id3v1Bytes ("Back"));
		var file = Open (original);

		file.Tag!.Clear ();

		Assert.IsTrue (string.IsNullOrEmpty (file.Id3v2Tag!.Title));
		Assert.IsTrue (string.IsNullOrEmpty (file.ApeTag!.Title));
		Assert.IsTrue (string.IsNullOrEmpty (file.Id3v1Tag!.Title));
		Assert.IsTrue (file.Tag.IsEmpty);
	}

	[TestMethod]
	public void Id3v1Tag_FromAnotherSource_IsWritten ()
	{
		var frames = Frames ();
		var file = Open (frames);

		file.Id3v1Tag = new Id3v1Tag { Title = "Assigned" };
		var saved = file.Render (frames).ToArray ();

		Assert.AreEqual ("Assigned", Open (saved).Id3v1Tag!.Title);
		Assert.AreEqual (0, IndexOf (saved, frames));
	}
}
