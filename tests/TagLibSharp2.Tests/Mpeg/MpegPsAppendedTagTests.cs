// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Mpeg.MpegPsFileTests;
using static TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// An ID3v2.4 tag appended after the stream, found by its "3DI" footer (ID3v2.4 structure 3.4, 5).
/// TagLib# writes .mpg tags this way: the stream, an appended ID3v2.4 tag, then ID3v1.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsAppendedTagTests
{
	[TestMethod]
	public void Read_AppendedTagBeforeId3v1_IsTheId3v2Tag ()
	{
		var stream = ProgramStream ();
		var data = Concat (stream, Appended ("Appended"), Id3v1 ("Footer"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Appended", file.Id3v2Tag!.Title);
		Assert.AreEqual ("Appended", file.Tag!.Title);
		Assert.AreEqual ("Footer", file.Id3v1Tag!.Title);
		Assert.AreEqual (0, file.Id3v2Size);
		Assert.AreEqual (TimeSpan.FromSeconds (2), file.Duration);
		Assert.AreSame (file.Id3v2Tag, file.EnsureId3v2Tag ());
	}

	[TestMethod]
	public void Read_AppendedTagAtTheEnd_IsTheId3v2Tag ()
	{
		var data = Concat (ProgramStream (), Appended ("Last"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Last", file.Tag!.Title);
		Assert.IsNull (file.Id3v1Tag);
	}

	[TestMethod]
	public void Read_AppendedTagBeforeApeAndId3v1_IsTheId3v2Tag ()
	{
		// Section 5: "Add a tag at the end of the file, before tags from other tagging systems."
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		var data = Concat (ProgramStream (), Appended ("Before Ape"), ape, Id3v1 ("Footer"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Before Ape", file.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void Read_AppendedTagBeforeApeAtTheEnd_IsTheId3v2Tag ()
	{
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		var data = Concat (ProgramStream (), Appended ("Before Ape"), ape);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Before Ape", file.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void Read_ElementaryStreamWithAppendedTag_EndsTheStreamAtTheTag ()
	{
		var stream = ElementaryStream (new Options { Mpeg2 = true, VideoFrames = 90 });
		var data = Concat (stream, Appended ("Video"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Video", file.Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (90 / 25d), file.Duration);
	}

	[TestMethod]
	[DataRow (0, (byte)'X')]
	[DataRow (3, (byte)3)]
	[DataRow (9, (byte)0x7F)]
	public void Read_FooterWithoutAMatchingHeader_IsStreamData (int offset, byte value)
	{
		// The footer is "a copy of the header" (3.4): no "ID3" there, or a header that says v2.3
		// or another size, means the bytes are not that tag.
		var tag = Appended ("Broken");
		tag[offset] = value;
		var data = Concat (ProgramStream (), tag);

		var file = MpegPsFile.Read (data).File!;

		Assert.IsNull (file.Tag);
		var rendered = file.Render (data).ToArray ();
		CollectionAssert.AreEqual (data, rendered);
	}

	[TestMethod]
	public void Read_V23TagWithAFooter_IsStreamData ()
	{
		// Only v2.4 has a footer (3.4); a v2.3 header has no footer flag to set.
		var tag = Appended ("Three");
		tag[3] = 3;
		tag[^7] = 3;
		var data = Concat (ProgramStream (), tag);

		Assert.IsNull (MpegPsFile.Read (data).File!.Tag);
	}

	[TestMethod]
	public void Read_ApeSizeLongerThanTheFile_FindsNoAppendedTag ()
	{
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		// TagSize in the APE footer (bytes 12 to 15 of the last 32) claims 2 GiB.
		ape[^20] = 0x00;
		ape[^19] = 0x00;
		ape[^18] = 0x00;
		ape[^17] = 0x80;
		var data = Concat (ProgramStream (), Appended ("Lost"), ape);

		var result = MpegPsFile.Read (data);
		var streamed = MediaFile.Read (new MemoryStream (data), "clip.mpg");

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.Tag);
		Assert.IsNull (streamed.Tag);
	}

	[TestMethod]
	public void Read_ApeHeaderAtTheEnd_IsNotAnApeFooter ()
	{
		// 32 bytes with the APE header bit (29) set, sized as if they were a whole tag. A header
		// has a footer's layout but is not one, so nothing before it is an appended tag.
		var header = new byte[32];
		"APETAGEX"u8.CopyTo (header);
		header[8] = 0xD0;
		header[9] = 0x07;
		header[12] = 32;
		header[23] = 0x20;
		var data = Concat (ProgramStream (), Appended ("Hidden"), header);

		Assert.IsNull (MpegPsFile.Read (data).File!.Tag);
	}

	[TestMethod]
	public void Read_FooterWithoutTheFooterFlag_IsStreamData ()
	{
		// Header flag d (3.1) says a footer is present; a "3DI" copy of a header without it is not a footer.
		var tag = Appended ("Flagless");
		tag[5] = 0x00;
		tag[^5] = 0x00;
		var data = Concat (ProgramStream (), tag);

		Assert.IsNull (MpegPsFile.Read (data).File!.Tag);
	}

	[TestMethod]
	public void Read_FooterReachingIntoThePrependedTag_IsIgnored ()
	{
		// A prepended tag whose PRIV frame holds bytes that look like an appended tag's header,
		// and a footer at the end of the file that measures back to them. The appended tag may
		// not start before the stream, so the footer is stream data.
		var stream = ProgramStream ();
		byte[] owner = [(byte)'x', 0x00];
		byte[] fake = [0x49, 0x44, 0x33, 0x04, 0x00, 0x10, 0, 0, 0, 0];
		var frameSize = owner.Length + fake.Length;
		byte[] frameHeader = [(byte)'P', (byte)'R', (byte)'I', (byte)'V', 0, 0, 0, (byte)frameSize, 0, 0];
		var bodySize = frameHeader.Length + frameSize;
		byte[] tagHeader = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0, 0, 0, (byte)bodySize];
		var fakeAt = tagHeader.Length + frameHeader.Length + owner.Length;
		var length = tagHeader.Length + bodySize + stream.Length + 10;
		var size = length - fakeAt - 20;
		byte[] syncsafe = [(byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)];
		syncsafe.CopyTo (fake, 6);
		var footer = (byte[])fake.Clone ();
		footer[0] = (byte)'3';
		footer[1] = (byte)'D';
		footer[2] = (byte)'I';
		var data = Concat (tagHeader, frameHeader, owner, fake, stream, footer);
		Assert.AreEqual (length, data.Length);

		var result = MpegPsFile.Read (data);
		var streamed = MediaFile.Read (new MemoryStream (data), "clip.mpg");

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsTrue (streamed.IsSuccess, streamed.Error);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File!.Duration);
		CollectionAssert.AreEqual (data, result.File.Render (data).ToArray ());
	}

	[TestMethod]
	public void Read_FooterClaimingMoreThanTheFile_IsStreamData ()
	{
		var tag = Appended ("Huge");
		// A syncsafe size of 256 MiB in the footer only.
		for (var i = tag.Length - 4; i < tag.Length; i++)
			tag[i] = 0x7F;
		var data = Concat (ProgramStream (), tag);

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsNull (result.File!.Tag);
	}

	[TestMethod]
	public void Read_FooterOverlappingThePrependedTag_IsStreamData ()
	{
		// A 3DI footer whose size reaches back into the prepended tag.
		var front = Id3v2 ("Front");
		var footer = Appended ("x").AsSpan (^10).ToArray ();
		var size = front.Length + 4;
		footer[6] = (byte)((size >> 21) & 0x7F);
		footer[7] = (byte)((size >> 14) & 0x7F);
		footer[8] = (byte)((size >> 7) & 0x7F);
		footer[9] = (byte)(size & 0x7F);
		var data = Concat (front, ProgramStream (), footer);

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Front", file.Tag!.Title);
	}

	[TestMethod]
	public void Read_StreamPathsAgreeWithTheSpan ()
	{
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		foreach (var data in new[] {
			Concat (ProgramStream (), Appended ("A"), Id3v1 ("F")),
			Concat (ProgramStream (), Appended ("B"), ape, Id3v1 ("F")),
			Concat (Id3v2 ("Front"), ProgramStream (), Appended ("C"))
		}) {
			var span = MpegPsFile.Read (data).File!;
			var streamed = (MpegPsFile)MediaFile.Read (new MemoryStream (data), "clip.mpg").File!;
			var asyncRead = (MpegPsFile)MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg").GetAwaiter ().GetResult ().File!;

			Assert.AreEqual (span.Tag!.Title, streamed.Tag!.Title);
			Assert.AreEqual (span.Tag.Title, asyncRead.Tag!.Title);
			Assert.AreEqual (span.Duration, streamed.Duration);
			Assert.AreEqual (span.Duration, asyncRead.Duration);
			CollectionAssert.AreEqual (span.Render (data).ToArray (), streamed.Render (data).ToArray ());
			CollectionAssert.AreEqual (span.Render (data).ToArray (), asyncRead.Render (data).ToArray ());
		}
	}

	[TestMethod]
	public async Task ReadFromFile_AppendedTag_IsTheId3v2Tag ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.mpg", Concat (ProgramStream (), Appended ("Disk"), Id3v1 ("F")));

		Assert.AreEqual ("Disk", MpegPsFile.ReadFromFile ("/clip.mpg", fs).File!.Tag!.Title);
		Assert.AreEqual ("Disk", (await MpegPsFile.ReadFromFileAsync ("/clip.mpg", fs)).File!.Tag!.Title);
	}

	[TestMethod]
	public void Render_ChangedAppendedTag_IsRewrittenInPlaceAsTheOnlyTag ()
	{
		var stream = ProgramStream ();
		var data = Concat (stream, Appended ("Old"), Id3v1 ("Old"));
		var file = MpegPsFile.Read (data).File!;

		file.Tag!.Title = "New";
		var rendered = file.Render (data).ToArray ();

		Assert.AreEqual (1, Count (rendered, "ID3"u8), "one ID3v2 tag");
		Assert.AreEqual (1, Count (rendered, "3DI"u8), "with its footer");
		CollectionAssert.AreEqual (stream, rendered.AsSpan (0, stream.Length).ToArray ());
		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("New", reread.Id3v2Tag!.Title);
		Assert.AreEqual ("New", reread.Id3v1Tag!.Title);
		Assert.AreEqual (0, reread.Id3v2Size);
	}

	[TestMethod]
	public void Render_ChangedAppendedTagBeforeApe_KeepsTheApeTagAfterIt ()
	{
		var stream = ProgramStream ();
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		var data = Concat (stream, Appended ("Old"), ape, Id3v1 ("Old"));
		var file = MpegPsFile.Read (data).File!;

		file.Id3v2Tag!.Title = "New";
		var rendered = file.Render (data).ToArray ();

		var expectedTag = file.Id3v2Tag.Render (0, withFooter: true).ToArray ();
		CollectionAssert.AreEqual (Concat (stream, expectedTag, ape, Id3v1 ("Old")), rendered);
	}

	[TestMethod]
	public void Render_UnchangedForeignAppendedTag_KeepsItsBytes ()
	{
		var data = Concat (ProgramStream (), ForeignAppended, Id3v1 ("Footer"));
		var file = MpegPsFile.Read (data).File!;
		Assert.AreEqual ("Foot", file.Id3v2Tag!.Title);
		Assert.IsFalse (file.Id3v2Tag.Render (0, withFooter: true).Span.SequenceEqual (ForeignAppended), "renders differently");

		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	[TestMethod]
	public void Render_PrependedAndAppended_AppendedSupersedesAndOneTagIsWritten ()
	{
		// Section 5: for every new tag found the old one is discarded unless the update flag is set.
		var stream = ProgramStream ();
		var data = Concat (Id3v2 ("Front"), stream, Appended ("Back"));
		var file = MpegPsFile.Read (data).File!;
		Assert.AreEqual ("Back", file.Tag!.Title);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray (), "an unchanged save keeps both");

		file.Tag.Title = "Changed";
		var rendered = file.Render (data).ToArray ();

		Assert.AreEqual (1, Count (rendered, "ID3"u8));
		CollectionAssert.AreEqual (stream, rendered.AsSpan (0, stream.Length).ToArray ());
		Assert.AreEqual ("Changed", MpegPsFile.Read (rendered).File!.Tag!.Title);
	}

	[TestMethod]
	public void Read_AppendedUpdateTag_FallsBackToThePrependedTag ()
	{
		var front = new Id3v2Tag (Id3v2Version.V24) { Title = "Front", Album = "Front Album" }.Render ().ToArray ();
		var update = UpdateTag ("Back");
		var data = Concat (front, ProgramStream (), update);
		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Back", file.Tag!.Title);
		Assert.AreEqual ("Front Album", file.Tag.Album);

		file.Tag.Artist = "Both";
		var reread = MpegPsFile.Read (file.Render (data).ToArray ()).File!;

		Assert.AreEqual ("Back", reread.Tag!.Title);
		Assert.AreEqual ("Front Album", reread.Tag.Album);
		Assert.AreEqual ("Both", reread.Tag.Artist);
	}

	[TestMethod]
	public void Render_AppendedTagReplacedByV23_MovesToTheFront ()
	{
		var stream = ProgramStream ();
		var data = Concat (stream, Appended ("Old"), Id3v1 ("F"));
		var file = MpegPsFile.Read (data).File!;

		// A v2.3 tag has no footer, so it cannot be found at the end (section 3.4).
		file.Id3v2Tag = new Id3v2Tag (Id3v2Version.V23) { Title = "Three" };
		var rendered = file.Render (data).ToArray ();

		Assert.AreEqual (1, Count (rendered, "ID3"u8));
		Assert.AreEqual (0, Count (rendered, "3DI"u8));
		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("Three", reread.Id3v2Tag!.Title);
		Assert.IsTrue (reread.Id3v2Size > 0);
		CollectionAssert.AreEqual (stream, rendered.AsSpan (reread.Id3v2Size, stream.Length).ToArray ());
	}

	[TestMethod]
	public void Render_AppendedTagRemoved_LeavesNoId3v2 ()
	{
		var stream = ProgramStream ();
		var data = Concat (Id3v2 ("Front"), stream, Appended ("Back"), Id3v1 ("F"));
		var file = MpegPsFile.Read (data).File!;

		file.Id3v2Tag = null;
		var rendered = file.Render (data).ToArray ();

		CollectionAssert.AreEqual (Concat (stream, Id3v1 ("F")), rendered);
	}

	[TestMethod]
	public void SaveToFile_AppendedTag_WritesOneTag ()
	{
		var fs = new MockFileSystem ();
		var stream = ProgramStream ();
		fs.AddFile ("/clip.mpg", Concat (stream, Appended ("Old"), Id3v1 ("Old")));
		var file = MpegPsFile.ReadFromFile ("/clip.mpg", fs).File!;
		file.EnsureId3v2Tag ().Title = "Saved";

		Assert.IsTrue (file.SaveToFile ().IsSuccess);

		var written = fs.ReadAllBytes ("/clip.mpg");
		Assert.AreEqual (1, Count (written, "ID3"u8));
		Assert.AreEqual ("Saved", MpegPsFile.Read (written).File!.Tag!.Title);
	}

	internal static byte[] Appended (string title) =>
		new Id3v2Tag (Id3v2Version.V24) { Title = title }.Render (0, withFooter: true).ToArray ();

	// A v2.4 tag with a footer and a TIT2 "Foot" whose read-only status flag is set (4.1.1 flag c),
	// which this library renders with the flags cleared.
	static readonly byte[] ForeignAppended = [
		0x49, 0x44, 0x33, 0x04, 0x00, 0x10, 0x00, 0x00, 0x00, 0x0F,
		0x54, 0x49, 0x54, 0x32, 0x00, 0x00, 0x00, 0x05, 0x20, 0x00, 0x00, 0x46, 0x6F, 0x6F, 0x74,
		0x33, 0x44, 0x49, 0x04, 0x00, 0x10, 0x00, 0x00, 0x00, 0x0F
	];

	// A v2.4 tag with a footer and an extended header whose "tag is an update" flag is set
	// (ID3v2.4 structure 3.2: size 7 including itself, one flag byte %0b000000, flag data length $00).
	internal static byte[] UpdateTag (string title, int flagBytes = 1, byte flags = 0x40)
	{
		var plain = Appended (title);
		var body = plain.AsSpan (10, plain.Length - 20).ToArray ();
		// 3.2: each set flag carries a length byte and its data, in flag order: b (update) none,
		// c (CRC) 5 bytes, d (restrictions) 1 byte. The size counts the whole extended header.
		var flagData = new List<byte> ();
		if ((flags & 0x40) != 0)
			flagData.Add (0x00);
		if ((flags & 0x20) != 0)
			flagData.AddRange (new byte[] { 0x05, 0x00, 0x00, 0x00, 0x00, 0x00 });
		if ((flags & 0x10) != 0)
			flagData.AddRange (new byte[] { 0x01, 0x00 });
		var extendedSize = 6 + flagData.Count;
		var extended = Concat ([0x00, 0x00, 0x00, (byte)extendedSize, (byte)flagBytes, flags], flagData.ToArray ());
		var size = extended.Length + body.Length;
		byte[] header = [0x49, 0x44, 0x33, 0x04, 0x00, 0x50, (byte)(size >> 21), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)];
		var footer = (byte[])header.Clone ();
		footer[0] = (byte)'3';
		footer[1] = (byte)'D';
		footer[2] = (byte)'I';
		return Concat (header, extended, body, footer);
	}

	internal static int Count (byte[] data, ReadOnlySpan<byte> needle) => TagLibSharp2.Tests.Kit.Bytes.Count (data, needle);
}
