// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Invariant: a save that has to move a new element after a full SeekHead either succeeds with the
/// element listed, or fails with an error that names its own cause and leaves the file as it was.
/// </summary>
public partial class MatroskaFileTests
{
	[TestMethod]
	[DataRow ("listed", "")]
	[DataRow ("malformed-cue", "Cues")]
	[DataRow ("unreadable-seekhead", "SeekHead")]
	[DataRow ("webm", "WebM")]
	public void Save_NewAttachments_ThatMustMove_SucceedOrNameTheirCause (string variant, string cause)
	{
		// A SeekHead with no room for an Attachments Seek, so the Attachments must go right after it and
		// everything after it must move. Each variant breaks one thing that move needs.
		var info = Master (0x1549A966, Element (0x7BA9, "Refusal"u8));
		var cuePosition = variant == "malformed-cue" ? new byte[9] : Two (0x40);
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, cuePosition))))));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x99 })));
		var headLength = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, 0)).Length;
		var seek = Seek2 (MatroskaIds.Info, (ulong)headLength);
		// A last child that claims more bytes than the SeekHead holds makes the SeekHead unreadable.
		var head = variant == "unreadable-seekhead"
			? Master (MatroskaIds.SeekHead, Concat (seek, new byte[] { 0x4D, 0xBB, 0x85 }))
			: Master (MatroskaIds.SeekHead, seek);
		var segment = Master (0x18538067, Concat (head, info, cues, cluster));
		var original = Matroska (segment, variant == "webm" ? "webm" : "matroska");
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];
		var fs = new MockFileSystem ();
		fs.AddFile ("/refusal.mkv", original);

		var saved = parsed.SaveToFile ("/refusal.mkv", original, fs);

		var bytes = fs.ReadAllBytes ("/refusal.mkv");
		if (cause.Length == 0) {
			Assert.IsTrue (saved.IsSuccess, saved.Error);
			Assert.IsTrue (ReadSeeks (bytes).Exists (s => s.Id == MatroskaIds.Attachments), "the moved Attachments are listed");
			return;
		}

		Assert.IsFalse (saved.IsSuccess, variant);
		StringAssert.Contains (saved.Error, cause, variant);
		CollectionAssert.AreEqual (original, bytes, variant + ": the file is left as it was");
	}

	[TestMethod]
	public async Task SaveToFileAsync_NewAttachments_MoveThatCannotUpdateTheCues_NamesTheCues ()
	{
		// The async twin of the malformed-cue row: the same rewrite, the same error.
		var info = Master (0x1549A966, Element (0x7BA9, "Async"u8));
		var headLength = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, 0)).Length;
		var head = Master (MatroskaIds.SeekHead, Seek2 (MatroskaIds.Info, (ulong)headLength));
		var cues = Master (MatroskaIds.Cues, Master (0xBB, Concat (Element (0xB3, new byte[] { 0 }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (0xF1, new byte[9]))))));
		var cluster = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA3, new byte[] { 0x81, 0, 0, 0x80, 0x98 })));
		var original = FileWith (head, info, cues, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];
		var fs = new MockFileSystem ();
		fs.AddFile ("/async.mkv", original);

		var saved = await parsed.SaveToFileAsync ("/async.mkv", original, fs);

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "Cues");
		CollectionAssert.AreEqual (original, fs.ReadAllBytes ("/async.mkv"));
	}
}
