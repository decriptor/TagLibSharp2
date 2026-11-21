// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Every kind of frame the tag holds counts as content: a WAV or AIFF save writes an ID3 chunk that holds only
/// that frame, and <see cref="Tag.Clear"/> removes it.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3v2")]
public class Id3v2FrameKindsTests
{
	static readonly Dictionary<string, Action<Id3v2Tag>> Kinds = new () {
		["TIT2"] = t => t.Title = "t",
		["APIC"] = t => t.AddPicture (new PictureFrame ("image/png", PictureType.FrontCover, "", [1, 2, 3])),
		["COMM"] = t => t.AddComment (new CommentFrame ("c")),
		["TXXX"] = t => t.AddUserTextFrame (new UserTextFrame ("d", "v")),
		["USLT"] = t => t.AddLyrics (new LyricsFrame ("l")),
		["UFID"] = t => t.AddUniqueFileId (new UniqueFileIdFrame ("http://example.org", "id")),
		["TIPL"] = t => {
			var people = new InvolvedPeopleFrame (InvolvedPeopleFrameType.InvolvedPeople);
			people.Add ("producer", "p");
			t.AddInvolvedPeopleFrame (people);
		},
		["POPM"] = t => t.SetPopularimeter ("a@example.org", 128, 3),
		["WOAR"] = t => t.SetUrl ("WOAR", "http://example.org/artist"),
		["WXXX"] = t => t.SetUserUrl ("d", "http://example.org"),
		["SYLT"] = t => {
			var sync = new SyncLyricsFrame ();
			sync.AddSyncItem ("line", 1000);
			t.AddSyncLyrics (sync);
		},
		["GEOB"] = t => t.AddGeneralObject (new GeneralObjectFrame ("application/octet-stream", "f.bin", "d", new BinaryData ([1, 2]))),
		["PRIV"] = t => t.AddPrivateFrame (new PrivateFrame ("owner", new BinaryData ([1, 2]))),
		["CHAP"] = t => t.AddChapter (new ChapterFrame ("ch1", 0, 1000)),
		["CTOC"] = t => {
			var toc = new TableOfContentsFrame ("toc");
			toc.AddChildElement ("ch1");
			t.AddTableOfContents (toc);
		},
	};

	public static IEnumerable<object[]> KindNames => Kinds.Keys.Select (k => new object[] { k });

	static Id3v2Tag TagWithOnly (string kind)
	{
		var tag = new Id3v2Tag ();
		Kinds[kind] (tag);
		Assert.IsTrue (tag.Render ().Span.IndexOf (System.Text.Encoding.ASCII.GetBytes (kind)) >= 0, "the frame renders");
		return tag;
	}

	[TestMethod]
	[DynamicData (nameof (KindNames))]
	public void Wav_TagWithOnlyThisFrame_IsWritten (string kind)
	{
		var tag = TagWithOnly (kind);
		var wav = WavFile.Read (TestBuilders.Wav.CreateMinimal ()).File!;
		wav.Id3v2Tag = tag;

		var reloaded = WavFile.Read (wav.Render ().Span).File!;

		Assert.IsNotNull (reloaded.Id3v2Tag, $"{kind} alone must keep the ID3 chunk");
		CollectionAssert.AreEqual (tag.Render ().ToArray (), reloaded.Id3v2Tag.Render ().ToArray ());
	}

	[TestMethod]
	[DynamicData (nameof (KindNames))]
	public void Aiff_TagWithOnlyThisFrame_IsWritten (string kind)
	{
		var tag = TagWithOnly (kind);
		var aiff = AiffFile.Read (TestBuilders.Aiff.CreateMinimal ()).File!;
		aiff.Tag = tag;

		var reloaded = AiffFile.Read (aiff.Render ().Span).File!;

		Assert.IsNotNull (reloaded.Tag, $"{kind} alone must keep the ID3 chunk");
		CollectionAssert.AreEqual (tag.Render ().ToArray (), reloaded.Tag.Render ().ToArray ());
	}

	[TestMethod]
	[DynamicData (nameof (KindNames))]
	public void Clear_RemovesThisFrame (string kind)
	{
		var tag = TagWithOnly (kind);

		tag.Clear ();

		CollectionAssert.AreEqual (new Id3v2Tag ().Render ().ToArray (), tag.Render ().ToArray ());
		var wav = WavFile.Read (TestBuilders.Wav.CreateMinimal ()).File!;
		wav.Id3v2Tag = tag;
		Assert.IsNull (WavFile.Read (wav.Render ().Span).File!.Id3v2Tag, "an empty tag writes no chunk");
	}
}
