// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
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

	static string? ReadGenre (int gnre)
	{
		var result = Mp4File.Read (TestBuilders.Mp4.CreateWithGenres (gnre: gnre));
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!.Genre;
	}

	static void AssertAtoms (BinaryData rendered, bool textGenre, bool gnre)
	{
		ReadOnlySpan<byte> textAtom = [0xA9, 0x67, 0x65, 0x6E];
		Assert.AreEqual (textGenre, rendered.Span.IndexOf (textAtom) >= 0);
		Assert.AreEqual (gnre, rendered.Span.IndexOf ("gnre"u8) >= 0);
	}
}
