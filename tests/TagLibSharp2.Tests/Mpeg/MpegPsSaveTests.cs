// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Mpeg.MpegPsFileTests;
using static TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// Saving MPEG program streams: the stream bytes between the tags never change.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsSaveTests
{
	[TestMethod]
	public void Render_UntaggedFile_PrependsId3v2AndKeepsTheStream ()
	{
		var stream = ProgramStream ();
		var file = MpegPsFile.Read (stream).File!;

		file.EnsureId3v2Tag ().Title = "Added";
		var rendered = file.Render (stream).ToArray ();

		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("Added", reread.Tag!.Title);
		CollectionAssert.AreEqual (stream, rendered.AsSpan (reread.Id3v2Size).ToArray ());
		Assert.AreEqual (file.Duration, reread.Duration);
		Assert.IsNull (reread.Id3v1Tag);
	}

	[TestMethod]
	public void Render_Unchanged_IsByteIdentical ()
	{
		var data = Concat (Id3v2 ("Head"), ProgramStream (), Id3v1 ("Foot"));
		var file = MpegPsFile.Read (data).File!;

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_UnchangedForeignTags_KeepTheirBytes ()
	{
		// An ID3v2.3 tag with no padding and an ID3v1 tag padded with spaces: neither is how
		// this library renders them, so only keeping the original bytes is byte-identical.
		byte[] v23 = [
			0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F,
			0x54, 0x49, 0x54, 0x32, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x46, 0x6F, 0x72, 0x65
		];
		var v1 = Id3v1 ("Fore");
		for (var i = 3 + 4; i < 33; i++)
			v1[i] = 0x20;
		var data = Concat (v23, ProgramStream (), v1);
		var file = MpegPsFile.Read (data).File!;
		Assert.AreEqual ("Fore", file.Id3v2Tag!.Title);
		Assert.AreNotEqual (v23.Length, file.Id3v2Tag.Render ().Length);

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_UnparsedId3v2_IsKept ()
	{
		// A valid header around a body that is not frames: the tag does not parse, its bytes stay.
		byte[] odd = [0x49, 0x44, 0x33, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x01, 0x02, 0x03, 0x04];
		var data = Concat (odd, ProgramStream ());
		var file = MpegPsFile.Read (data).File!;

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_TagAddedOverAnUnparsedId3v2_ReplacesIt ()
	{
		byte[] odd = [0x49, 0x44, 0x33, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x01, 0x02, 0x03, 0x04];
		var stream = ProgramStream ();
		var data = Concat (odd, stream);
		var file = MpegPsFile.Read (data).File!;

		file.EnsureId3v2Tag ().Title = "Readable";
		var rendered = file.Render (data).ToArray ();

		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("Readable", reread.Tag!.Title);
		CollectionAssert.AreEqual (stream, rendered.AsSpan (reread.Id3v2Size).ToArray ());
	}

	[TestMethod]
	public void Render_UnchangedUntagged_IsByteIdentical ()
	{
		var data = ProgramStream (new Options { Mpeg2 = true });
		var file = MpegPsFile.Read (data).File!;

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_ChangedTags_KeepTheStreamBetweenThem ()
	{
		var stream = ProgramStream (new Options { Mpeg2 = true, AudioKind = Audio.Ac3 });
		var data = Concat (Id3v2 ("Old"), stream, Id3v1 ("Old"));
		var file = MpegPsFile.Read (data).File!;

		file.Tag!.Title = "A much longer title than before";
		var rendered = file.Render (data).ToArray ();

		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("A much longer title than before", reread.Id3v2Tag!.Title);
		Assert.AreEqual ("A much longer title th", reread.Id3v1Tag!.Title![..22]);
		CollectionAssert.AreEqual (stream, rendered.AsSpan (reread.Id3v2Size, stream.Length).ToArray ());
		Assert.AreEqual (rendered.Length, reread.Id3v2Size + stream.Length + 128);
	}

	[TestMethod]
	public void Render_RemovedTags_DropsTheirBytes ()
	{
		var stream = ProgramStream ();
		var data = Concat (Id3v2 ("Head"), stream, Id3v1 ("Foot"));
		var file = MpegPsFile.Read (data).File!;

		file.Id3v2Tag = null;
		file.Id3v1Tag = null;
		var rendered = file.Render (data).ToArray ();

		CollectionAssert.AreEqual (stream, rendered);
	}

	[TestMethod]
	public void Render_AddedId3v1_AppendsIt ()
	{
		var stream = ProgramStream ();
		var file = MpegPsFile.Read (stream).File!;

		file.Id3v1Tag = new Id3v1Tag { Title = "Tail" };
		var rendered = file.Render (stream).ToArray ();

		Assert.AreEqual (stream.Length + 128, rendered.Length);
		Assert.AreEqual ("Tail", MpegPsFile.Read (rendered).File!.Id3v1Tag!.Title);
	}

	[TestMethod]
	public void Render_ApeBeforeId3v1_StaysInPlace ()
	{
		var stream = ProgramStream ();
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		var data = Concat (stream, ape, Id3v1 ("Foot"));
		var file = MpegPsFile.Read (data).File!;

		file.Tag!.Title = "New";
		var rendered = file.Render (data).ToArray ();

		var reread = MpegPsFile.Read (rendered).File!;
		CollectionAssert.AreEqual (Concat (stream, ape), rendered.AsSpan (reread.Id3v2Size, stream.Length + ape.Length).ToArray ());
		Assert.AreEqual ("New", reread.Id3v1Tag!.Title);
	}

	[TestMethod]
	public void Render_DifferentOriginal_ReturnsEmpty ()
	{
		var data = ProgramStream ();
		var file = MpegPsFile.Read (data).File!;

		Assert.IsTrue (file.Render (data.AsSpan (0, data.Length - 1)).IsEmpty);
		Assert.IsTrue (file.Render (ProgramStream (new Options { Mpeg2 = true })).IsEmpty);
	}

	[TestMethod]
	public void SaveToFile_WritesThroughTheFileSystem ()
	{
		var fs = new MockFileSystem ();
		var stream = ProgramStream ();
		fs.AddFile ("/clip.mpg", stream);
		var file = MpegPsFile.ReadFromFile ("/clip.mpg", fs).File!;
		file.EnsureId3v2Tag ().Title = "Saved";

		var saved = file.SaveToFile ();

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var reread = MpegPsFile.ReadFromFile ("/clip.mpg", fs).File!;
		Assert.AreEqual ("Saved", reread.Tag!.Title);
		CollectionAssert.AreEqual (stream, fs.ReadAllBytes ("/clip.mpg").AsSpan (reread.Id3v2Size).ToArray ());
	}

	[TestMethod]
	public async Task SaveToFileAsync_WritesThroughTheFileSystem ()
	{
		var fs = new MockFileSystem ();
		var stream = ProgramStream ();
		fs.AddFile ("/clip.mpg", stream);
		var file = (await MpegPsFile.ReadFromFileAsync ("/clip.mpg", fs)).File!;
		file.EnsureId3v2Tag ().Title = "Saved";

		var saved = await file.SaveToFileAsync ();

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var reread = (await MpegPsFile.ReadFromFileAsync ("/clip.mpg", fs)).File!;
		Assert.AreEqual ("Saved", reread.Tag!.Title);
		CollectionAssert.AreEqual (stream, fs.ReadAllBytes ("/clip.mpg").AsSpan (reread.Id3v2Size).ToArray ());
	}

	[TestMethod]
	public async Task SaveToFile_ToAnotherPath_LeavesTheSource ()
	{
		var fs = new MockFileSystem ();
		var stream = ProgramStream ();
		fs.AddFile ("/clip.mpg", stream);
		var file = MpegPsFile.ReadFromFile ("/clip.mpg", fs).File!;
		file.EnsureId3v2Tag ().Title = "Copy";

		Assert.IsTrue (file.SaveToFile ("/copy.mpg").IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/copy2.mpg")).IsSuccess);
		Assert.IsTrue (file.SaveToFile ("/copy3.mpg", stream, fs).IsSuccess);
		Assert.IsTrue ((await file.SaveToFileAsync ("/copy4.mpg", stream, fs)).IsSuccess);

		CollectionAssert.AreEqual (stream, fs.ReadAllBytes ("/clip.mpg"));
		foreach (var copy in new[] { "/copy.mpg", "/copy2.mpg", "/copy3.mpg", "/copy4.mpg" })
			Assert.AreEqual ("Copy", MpegPsFile.ReadFromFile (copy, fs).File!.Tag!.Title, copy);
	}

	[TestMethod]
	public async Task SaveToFile_WithoutASource_Fails ()
	{
		var file = MpegPsFile.Read (ProgramStream ()).File!;

		Assert.IsFalse (file.SaveToFile ().IsSuccess);
		Assert.IsFalse (file.SaveToFile ("/x.mpg", new MockFileSystem ()).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ()).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/x.mpg", new MockFileSystem ())).IsSuccess);
	}

	[TestMethod]
	public async Task SaveToFile_WrongOriginal_Fails ()
	{
		var data = ProgramStream ();
		var file = MpegPsFile.Read (data).File!;
		var fs = new MockFileSystem ();

		Assert.IsFalse (file.SaveToFile ("/x.mpg", data.AsSpan (1), fs).IsSuccess);
		Assert.IsFalse ((await file.SaveToFileAsync ("/x.mpg", data.AsMemory (1), fs)).IsSuccess);
		Assert.IsFalse (fs.FileExists ("/x.mpg"));
	}

	[TestMethod]
	public void SaveToFile_SourceChangedSinceRead_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.mpg", ProgramStream ());
		var file = MpegPsFile.ReadFromFile ("/clip.mpg", fs).File!;
		fs.AddFile ("/clip.mpg", ProgramStream (new Options { VideoFrames = 10 }));
		file.EnsureId3v2Tag ().Title = "Stale";

		var saved = file.SaveToFile ();

		Assert.IsFalse (saved.IsSuccess);
	}

	[TestMethod]
	public void Render_ElementaryStream_KeepsTheStream ()
	{
		var stream = ElementaryStream (new Options { Mpeg2 = true });
		var file = MpegPsFile.Read (stream).File!;

		file.EnsureId3v2Tag ().Title = "Elementary";
		var rendered = file.Render (stream).ToArray ();

		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("Elementary", reread.Tag!.Title);
		Assert.AreEqual (MpegPsStreamType.VideoElementaryStream, reread.StreamType);
		CollectionAssert.AreEqual (stream, rendered.AsSpan (reread.Id3v2Size).ToArray ());
		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (rendered));
	}
}
