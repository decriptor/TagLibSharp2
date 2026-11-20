// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aiff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aiff")]
public class AiffAnnotationTests
{
	[TestMethod]
	public void Read_Anno_ReturnsCommentAndDuration ()
	{
		var file = ReadAiff (Form (
			Text ("ANNO", "Creator: Logic"),
			Comm (),
			Ssnd ()));

		Assert.AreEqual ("Creator: Logic", file.Tag!.Comment);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
	}

	[TestMethod]
	public void Read_OddAnno_DoesNotConsumeTheFollowingChunk ()
	{
		// Pad byte is outside ckSize and is not text. A non-zero pad makes a swallowed byte visible.
		var file = ReadAiff (Form (
			Text ("ANNO", "Hello", pad: 0x58),
			Text ("NAME", "Song"),
			Comm (),
			Ssnd ()));

		Assert.AreEqual ("Hello", file.Tag!.Comment);
		Assert.AreEqual ("Song", file.Tag.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
		Assert.IsNotNull (file.GetChunk ("COMM"));
	}

	[TestMethod]
	public void Read_Anno_StripsTrailingNulsAndKeepsInternalBytes ()
	{
		var spaced = ReadAiff (Form (Text ("ANNO", "Creator:  Logic ", trailingNul: true), Comm ()));
		Assert.AreEqual ("Creator:  Logic ", spaced.Tag!.Comment);

		var internalNul = ReadAiff (Form (Text ("ANNO", "A\0B"), Comm ()));
		Assert.AreEqual ("A\0B", internalNul.Tag!.Comment);
	}

	[TestMethod]
	public void Read_ComtWithoutAnno_ReturnsCommentText ()
	{
		var file = ReadAiff (Form (Comt ("Take 2"), Comm ()));

		Assert.AreEqual ("Take 2", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Comt_JoinsCommentsInOrder ()
	{
		var file = ReadAiff (Form (Comt ("Take 2", "", "Take 3"), Comm ()));

		Assert.AreEqual ("Take 2\nTake 3", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Comt_OddTextPadIsNotPartOfTheNextComment ()
	{
		var file = ReadAiff (Form (Comt ("A", "B"), Comm ()));

		Assert.AreEqual ("A\nB", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_TruncatedComt_KeepsCompleteComments ()
	{
		var tail = ReadAiff (Form (TruncatedComt (), Comm ()));
		Assert.AreEqual ("Take 2", tail.Tag!.Comment);

		var overrun = ReadAiff (Form (OverrunComt (), Comm ()));
		Assert.AreEqual ("Take 2", overrun.Tag!.Comment);

		var empty = ReadAiff (Form (Chunk ("COMT", [0x00]), Comm ()));
		Assert.IsNull (empty.Tag);
	}

	[TestMethod]
	public void Read_AnnoAndComt_PrefersAnno ()
	{
		var annoFirst = ReadAiff (Form (Text ("ANNO", "Creator: Logic"), Comt ("Take 2"), Comm ()));
		Assert.AreEqual ("Creator: Logic", annoFirst.Tag!.Comment);

		var comtFirst = ReadAiff (Form (Comt ("Take 2"), Text ("ANNO", "Creator: Logic"), Comm ()));
		Assert.AreEqual ("Creator: Logic", comtFirst.Tag!.Comment);
	}

	[TestMethod]
	public void Read_EmptyAnno_UsesComt ()
	{
		var file = ReadAiff (Form (Text ("ANNO", "", trailingNul: true), Comt ("Take 2"), Comm ()));

		Assert.AreEqual ("Take 2", file.Tag!.Comment);
	}

	[TestMethod]
	public void Read_Id3Comment_WinsOverAnno ()
	{
		var id3First = ReadAiff (Form (
			Id3 (new Id3v2Tag { Comment = "from id3" }),
			Text ("ANNO", "Creator: Logic"),
			Comm ()));
		Assert.AreEqual ("from id3", id3First.Tag!.Comment);

		var annoFirst = ReadAiff (Form (
			Text ("ANNO", "Creator: Logic"),
			Id3 (new Id3v2Tag { Comment = "from id3" }),
			Comm ()));
		Assert.AreEqual ("from id3", annoFirst.Tag!.Comment);

		var blank = ReadAiff (Form (
			Id3 (new Id3v2Tag { Comment = " " }),
			Text ("ANNO", "Creator: Logic"),
			Comm ()));
		Assert.AreEqual (" ", blank.Tag!.Comment);
	}

	[TestMethod]
	public void Read_AnnoAroundId3WithoutComment_KeepsAnnotationAndOtherFields ()
	{
		var annoFirst = ReadAiff (Form (
			Text ("ANNO", "Creator: Logic"),
			Id3 (new Id3v2Tag { Title = "Kept Title", Artist = "Kept Artist" }),
			Comm ()));
		Assert.AreEqual ("Creator: Logic", annoFirst.Tag!.Comment);
		Assert.AreEqual ("Kept Title", annoFirst.Tag.Title);
		Assert.AreEqual ("Kept Artist", annoFirst.Tag.Artist);

		var id3First = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "Kept Title", Artist = "Kept Artist" }),
			Text ("ANNO", "Creator: Logic"),
			Comm ()));
		Assert.AreEqual ("Creator: Logic", id3First.Tag!.Comment);
		Assert.AreEqual ("Kept Title", id3First.Tag.Title);
		Assert.AreEqual ("Kept Artist", id3First.Tag.Artist);
	}

	[TestMethod]
	public void Read_NameAndAuth_FillTitleAndArtist ()
	{
		var file = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person", trailingNul: true),
			Comm ()));

		Assert.AreEqual ("Song", file.Tag!.Title);
		Assert.AreEqual ("A Person", file.Tag.Artist);
		Assert.IsNull (file.Tag.Comment);
	}

	[TestMethod]
	public void Read_Id3Title_WinsOverName ()
	{
		var nameFirst = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Id3 (new Id3v2Tag { Title = "From Id3", Artist = "Id3 Artist" }),
			Comm ()));
		Assert.AreEqual ("From Id3", nameFirst.Tag!.Title);
		Assert.AreEqual ("Id3 Artist", nameFirst.Tag.Artist);

		var id3First = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "From Id3" }),
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Comm ()));
		Assert.AreEqual ("From Id3", id3First.Tag!.Title);
		Assert.AreEqual ("A Person", id3First.Tag.Artist);
	}

	[TestMethod]
	public void Read_CopyrightChunk_FillsCopyright ()
	{
		var file = ReadAiff (Form (Text ("(c) ", "Copyright 2020"), Comm ()));

		Assert.AreEqual ("Copyright 2020", file.Tag!.Copyright);
		Assert.IsNotNull (file.GetChunk ("(c) "));
	}

	[TestMethod]
	public void Read_Id3Copyright_WinsOverCopyrightChunk ()
	{
		var chunkFirst = ReadAiff (Form (
			Text ("(c) ", "Copyright 2020"),
			Id3 (new Id3v2Tag { Title = "Kept Title", Copyright = "Owned" }),
			Comm ()));
		Assert.AreEqual ("Owned", chunkFirst.Tag!.Copyright);
		Assert.AreEqual ("Kept Title", chunkFirst.Tag.Title);

		var id3WithoutCopyright = ReadAiff (Form (
			Id3 (new Id3v2Tag { Title = "Kept Title" }),
			Text ("(c) ", "Copyright 2020"),
			Comm ()));
		Assert.AreEqual ("Copyright 2020", id3WithoutCopyright.Tag!.Copyright);
		Assert.AreEqual ("Kept Title", id3WithoutCopyright.Tag.Title);
	}

	[TestMethod]
	public void Read_OnlyComm_HasNullComment ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));

		Assert.IsNull (file.Tag);
		Assert.IsNull (file.Tag?.Comment);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
	}

	[TestMethod]
	public void Read_SeekableStreamAndMediaFile_ReturnAnnotation ()
	{
		// ANNO follows a real SSND payload so a reader that stops at audio, or loads it, fails.
		var bytes = Form (
			Comm (),
			Ssnd (64),
			Text ("ANNO", "Creator: Logic"),
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Text ("(c) ", "Copyright 2020"),
			Comt ("Take 2"));

		var parsed = AiffFile.Read (new MemoryStream (bytes), null, null);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		var file = parsed.File!;
		AssertAnnotation (file);
		var ssnd = file.GetChunk ("SSND");
		Assert.IsNotNull (ssnd);
		Assert.AreEqual (72u, ssnd.Size);
		Assert.AreEqual (0, ssnd.Data.Length);

		var fromStream = MediaFile.Read (new MemoryStream (bytes), "song.aiff");
		Assert.IsTrue (fromStream.IsSuccess, fromStream.Error);
		Assert.AreEqual ("Creator: Logic", fromStream.Tag!.Comment);
		Assert.AreEqual ("Song", fromStream.Tag.Title);
		Assert.AreEqual ("A Person", fromStream.Tag.Artist);
		Assert.AreEqual ("Copyright 2020", fromStream.Tag.Copyright);
		Assert.AreEqual (TimeSpan.FromSeconds (2), ((IMediaFile)fromStream.File!).AudioProperties!.Duration);

		var files = new MockFileSystem ();
		files.AddFile ("/song.aiff", bytes);
		var fromPath = MediaFile.Read ("/song.aiff", files);
		Assert.IsTrue (fromPath.IsSuccess, fromPath.Error);
		Assert.AreEqual ("Creator: Logic", fromPath.Tag!.Comment);
		Assert.AreEqual (0, fromPath.GetFileAs<AiffFile> ()!.GetChunk ("SSND")!.Data.Length);
	}

	[TestMethod]
	public void Render_AnnoComment_SurvivesRoundTrip ()
	{
		var file = ReadAiff (Form (Text ("ANNO", "Creator: Logic"), Comm (), Ssnd ()));

		var again = ReadAiff (file.Render ().ToArray ());

		Assert.AreEqual ("Creator: Logic", again.Tag!.Comment);
		Assert.AreEqual (TimeSpan.FromSeconds (2), again.Properties!.Duration);
	}

	static void AssertAnnotation (AiffFile file)
	{
		Assert.AreEqual ("Creator: Logic", file.Tag!.Comment);
		Assert.AreEqual ("Song", file.Tag.Title);
		Assert.AreEqual ("A Person", file.Tag.Artist);
		Assert.AreEqual ("Copyright 2020", file.Tag.Copyright);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Properties!.Duration);
	}

	static AiffFile ReadAiff (byte[] data)
	{
		var result = AiffFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	static byte[] Form (params byte[][] chunks)
	{
		var contentSize = 4;
		foreach (var chunk in chunks)
			contentSize += chunk.Length;

		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 ("FORM");
		builder.AddUInt32BE ((uint)contentSize);
		builder.AddStringLatin1 ("AIFF");
		foreach (var chunk in chunks)
			builder.Add (chunk);
		return builder.ToArray ();
	}

	static byte[] Comm (int sampleRate = 44100, uint frames = 88200)
	{
		using var data = new BinaryDataBuilder ();
		data.AddUInt16BE (2);
		data.AddUInt32BE (frames);
		data.AddUInt16BE (16);
		data.Add (ExtendedFloat.FromDouble (sampleRate));
		return Chunk ("COMM", data.ToArray ());
	}

	static byte[] Ssnd (int sampleBytes = 0)
	{
		using var data = new BinaryDataBuilder ();
		data.AddUInt32BE (0);
		data.AddUInt32BE (0);
		if (sampleBytes > 0)
			data.Add (new byte[sampleBytes]);
		return Chunk ("SSND", data.ToArray ());
	}

	static byte[] Id3 (Id3v2Tag tag) => Chunk ("ID3 ", tag.Render ().ToArray ());

	static byte[] Text (string fourCc, string text, bool trailingNul = false, byte pad = 0)
	{
		var bytes = System.Text.Encoding.Latin1.GetBytes (text);
		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 (fourCc);
		builder.AddUInt32BE ((uint)(bytes.Length + (trailingNul ? 1 : 0)));
		if (bytes.Length > 0)
			builder.Add (bytes);
		if (trailingNul)
			builder.Add (0);
		if (((bytes.Length + (trailingNul ? 1 : 0)) & 1) == 1)
			builder.Add (pad);
		return builder.ToArray ();
	}

	static byte[] Comt (params string[] texts)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE ((ushort)texts.Length);
		for (var i = 0; i < texts.Length; i++) {
			var text = System.Text.Encoding.Latin1.GetBytes (texts[i]);
			body.AddUInt32BE ((uint)i);
			body.AddUInt16BE (0);
			body.AddUInt16BE ((ushort)text.Length);
			if (text.Length > 0)
				body.Add (text);
			if ((text.Length & 1) == 1)
				body.Add (0);
		}

		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] TruncatedComt ()
	{
		var text = System.Text.Encoding.Latin1.GetBytes ("Take 2");
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE (2);
		body.AddUInt32BE (0);
		body.AddUInt16BE (0);
		body.AddUInt16BE ((ushort)text.Length);
		body.Add (text);
		body.Add (0x00, 0x00, 0x00);
		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] OverrunComt ()
	{
		var text = System.Text.Encoding.Latin1.GetBytes ("Take 2");
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE (2);
		body.AddUInt32BE (0);
		body.AddUInt16BE (0);
		body.AddUInt16BE ((ushort)text.Length);
		body.Add (text);
		body.AddUInt32BE (1);
		body.AddUInt16BE (0);
		body.AddUInt16BE (50);
		body.Add (0x41, 0x42);
		return Chunk ("COMT", body.ToArray ());
	}

	static byte[] Chunk (string fourCc, byte[] data)
	{
		using var builder = new BinaryDataBuilder ();
		builder.AddStringLatin1 (fourCc);
		builder.AddUInt32BE ((uint)data.Length);
		if (data.Length > 0)
			builder.Add (data);
		if ((data.Length & 1) == 1)
			builder.Add (0);
		return builder.ToArray ();
	}
}
