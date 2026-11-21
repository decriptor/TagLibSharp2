// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// gnre is a 1-based ID3v1 code. ©gen wins when both atoms are present, and writes stay text.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4GnreTests
{
	[TestMethod]
	public void Gnre_OneBasedCode_ReturnsId3v1Name ()
	{
		// Id3v1Genre: 24 Soundtrack, 98 Easy Listening, 73 Acid Punk, 23 Pranks, 0 Blues.
		Assert.AreEqual ("Soundtrack", ReadGenre (25));
		Assert.AreEqual ("Easy Listening", ReadGenre (99));
		Assert.AreEqual ("Acid Punk", ReadGenre (74));
		Assert.AreEqual ("Pranks", ReadGenre (24));
		Assert.AreEqual ("Blues", ReadGenre (1));
	}

	[TestMethod]
	public void Gnre_UnmappedCode_ReturnsNull ()
	{
		Assert.IsNull (ReadGenre (0));
		Assert.IsNull (ReadGenre (193));
		Assert.IsNull (ReadGenre (257));
	}

	[TestMethod]
	public void Gnre_LastId3v1Code_ReturnsName_AndNextIsNull ()
	{
		var last = Id3v1Genre.Count - 1;
		Assert.AreEqual (Id3v1Genre.GetName ((byte)last), ReadGenre (last + 1));
		Assert.IsNull (ReadGenre (last + 2));
	}

	[TestMethod]
	public void Gnre_Type18AndType21_TwoBytePayload_Decodes ()
	{
		// Rock is ID3v1 index 17, stored as the 1-based code 18.
		var rock = Id3v1Genre.GetIndex ("Rock") + 1;
		Assert.AreEqual (18, rock);
		Assert.AreEqual ("Rock", ReadGenre (rock, typeIndicator: 18));
		Assert.AreEqual ("Rock", ReadGenre (rock, typeIndicator: 21));
	}

	[TestMethod]
	public void Genre_TextAtomWinsOverGnre ()
	{
		var data = TestBuilders.Mp4.CreateWithGenres (textGenre: "Jazz", gnre: 25);
		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Jazz", result.File!.Genre);
	}

	[TestMethod]
	public void Genre_Set_WritesTextAndDropsGnre ()
	{
		var data = TestBuilders.Mp4.CreateWithGenres (gnre: 25);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual ("Soundtrack", file.Genre);

		file.Genre = "Rock";

		Assert.AreEqual ("Rock", file.Genre);
		var rendered = file.Render (data);
		AssertAtoms (rendered, textGenre: true, gnre: false);
		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("Rock", reread.File!.Genre);
	}

	[TestMethod]
	public void Genre_Clear_RemovesTextAndGnre ()
	{
		var data = TestBuilders.Mp4.CreateWithGenres (textGenre: "Jazz", gnre: 25);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;

		file.Genre = null;

		Assert.IsNull (file.Genre);
		var rendered = file.Render (data);
		AssertAtoms (rendered, textGenre: false, gnre: false);
		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.IsNull (reread.File!.Genre);
	}

	[TestMethod]
	public void TitleOnlySave_KeepsUnknownIlstChild_AndGnreUntilGenreIsSet ()
	{
		var orchPayload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };
		var orch = TestBuilders.Mp4.CreateIlstChild ("orch", typeIndicator: 7, orchPayload, locale: 0x01020304);
		var legacyName = TestBuilders.Mp4.CreateIlstChild ("©ope", typeIndicator: 1, Encoding.UTF8.GetBytes ("Opus One"), locale: 0x63);
		var gnre = TestBuilders.Mp4.CreateIlstChild ("gnre", typeIndicator: 0, new byte[] { 0x00, 25 });
		var trkn = TestBuilders.Mp4.CreateIlstChild ("trkn", typeIndicator: 0, new byte[] { 0x00, 0x00, 0x00, 0x03, 0x00, 0x0A, 0x00, 0x00 });
		var disk = TestBuilders.Mp4.CreateIlstChild ("disk", typeIndicator: 0, new byte[] { 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00 });
		var freeform = TestBuilders.Mp4.CreateFreeformAtom ("com.apple.iTunes", "ORCHID", "kept");
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateIlstChild ("©nam", typeIndicator: 1, Encoding.UTF8.GetBytes ("OldTitleXYZ")),
			orch,
			legacyName,
			gnre,
			trkn,
			disk,
			freeform);

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.AreEqual ("Soundtrack", file.Genre);

		file.Title = "NewTitleXYZ";
		file.Track = 7;
		file.DiscNumber = 2;

		var rendered = file.Render (data);
		Assert.IsTrue (rendered.Span.IndexOf (orch) >= 0, "orch was dropped");
		Assert.IsTrue (rendered.Span.IndexOf (legacyName) >= 0, "©ope was dropped");
		Assert.IsTrue (rendered.Span.IndexOf (gnre) >= 0, "gnre was dropped");
		Assert.AreEqual (1, Count (rendered.Span, "----"u8));
		Assert.IsTrue (rendered.Span.IndexOf ("OldTitleXYZ"u8) < 0, "old title survived");

		var reread = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		var saved = reread.File!;
		Assert.AreEqual ("NewTitleXYZ", saved.Title);
		Assert.AreEqual (7u, saved.Track);
		Assert.AreEqual (10u, saved.TotalTracks);
		Assert.AreEqual (2u, saved.DiscNumber);
		Assert.AreEqual (2u, saved.TotalDiscs);
		Assert.AreEqual ("Soundtrack", saved.Genre);
		Assert.AreEqual ("kept", saved.GetFreeformTag ("com.apple.iTunes", "ORCHID"));

		saved.Genre = "Rock";
		var cleared = saved.Render (rendered.Span);
		Assert.IsTrue (cleared.Span.IndexOf (orch) >= 0, "orch was dropped when genre changed");
		Assert.IsTrue (cleared.Span.IndexOf (legacyName) >= 0, "©ope was dropped when genre changed");
		Assert.IsTrue (cleared.Span.IndexOf (gnre) < 0, "gnre survived a genre write");
		Assert.AreEqual (1, Count (cleared.Span, "----"u8));

		var afterGenre = Mp4File.Read (cleared.Span);
		Assert.IsTrue (afterGenre.IsSuccess, afterGenre.Error);
		Assert.AreEqual ("Rock", afterGenre.File!.Genre);
		Assert.AreEqual ("NewTitleXYZ", afterGenre.File.Title);
		Assert.AreEqual (7u, afterGenre.File.Track);
		Assert.AreEqual ("kept", afterGenre.File.GetFreeformTag ("com.apple.iTunes", "ORCHID"));
	}

	static string? ReadGenre (int gnre, int typeIndicator = 0)
	{
		var result = Mp4File.Read (TestBuilders.Mp4.CreateWithGenres (gnre: gnre, gnreType: typeIndicator));
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!.Genre;
	}

	static int Count (ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
	{
		var count = 0;
		var start = 0;
		while (start < data.Length) {
			var at = data.Slice (start).IndexOf (pattern);
			if (at < 0)
				break;
			count++;
			start += at + pattern.Length;
		}

		return count;
	}

	static void AssertAtoms (BinaryData rendered, bool textGenre, bool gnre)
	{
		ReadOnlySpan<byte> textAtom = [0xA9, 0x67, 0x65, 0x6E];
		Assert.AreEqual (textGenre, rendered.Span.IndexOf (textAtom) >= 0);
		Assert.AreEqual (gnre, rendered.Span.IndexOf ("gnre"u8) >= 0);
	}
}
