// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using static TagLibSharp2.Tests.Mpeg.MpegPsAppendedTagTests;
using static TagLibSharp2.Tests.Mpeg.MpegPsFileTests;
using static TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder;

namespace TagLibSharp2.Tests.Mpeg;

/// <summary>
/// The tags after an MPEG stream are found from the back (ID3v2.4 section 5), and the stream
/// windows end where those tags start.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsTrailerTests
{
	[TestMethod]
	public void Read_AppendedTagEndingTheFileWithTagAt128FromTheEnd_IsNotId3v1 ()
	{
		// The comment text puts "TAG" 128 bytes before the end of the file, inside the tag.
		var tag = new Id3v2Tag (Id3v2Version.V24) { Title = "Appended Title", Comment = "TAG" + new string ('y', 115) }
			.Render (0, withFooter: true).ToArray ();
		var stream = ProgramStream ();
		var data = Concat (stream, tag);
		Assert.IsTrue (data.AsSpan (data.Length - 128, 3).SequenceEqual ("TAG"u8), "TAG sits at EOF-128");

		AssertAppendedOnly (data, stream, "Appended Title");
	}

	[TestMethod]
	public void Read_ApeTagEndingTheFileWithTagAt128FromTheEnd_IsNotId3v1 ()
	{
		// The APE item value puts "TAG" 128 bytes before the end: 96 value bytes, then the footer.
		var ape = TestBuilders.Ape.CreateTag (title: "TAG" + new string ('z', 93));
		var stream = ProgramStream ();
		var data = Concat (stream, Appended ("App Title"), ape);
		Assert.IsTrue (data.AsSpan (data.Length - 128, 3).SequenceEqual ("TAG"u8), "TAG sits at EOF-128");

		var file = MpegPsFile.Read (data).File!;

		Assert.IsNull (file.Id3v1Tag);
		Assert.AreEqual ("App Title", file.Tag!.Title);
		file.Tag.Title = "New Title";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, "ID3"u8));
		CollectionAssert.AreEqual (ape, rendered.AsSpan (rendered.Length - ape.Length).ToArray (), "the APE tag stays last, untouched");
		Assert.AreEqual ("New Title", MpegPsFile.Read (rendered).File!.Tag!.Title);
	}

	[TestMethod]
	public void Read_ApeTagEndingTheFileWithoutAnAppendedTag_IsNotId3v1 ()
	{
		var ape = TestBuilders.Ape.CreateTag (title: "TAG" + new string ('z', 93));
		var stream = ProgramStream ();
		var data = Concat (stream, ape);

		var file = MpegPsFile.Read (data).File!;

		Assert.IsNull (file.Tag);
		file.EnsureId3v2Tag ().Title = "Front";
		var rendered = file.Render (data).ToArray ();
		CollectionAssert.AreEqual (Concat (stream, ape), rendered.AsSpan (rendered.Length - stream.Length - ape.Length).ToArray ());
	}

	[TestMethod]
	public void Read_TagInsidePictureOfAShortStream_DoesNotChangeTheProperties ()
	{
		// A pack and an MPEG audio PES stamped before the stream's first PTS, inside a PRIV frame
		// of the appended tag. The stream is shorter than the head window.
		var stream = ProgramStream (new Options { AudioKind = Audio.None, VideoFrames = 75 });
		var fake = Concat (PackHeader1 (0), Pes (0xC0, 0, Concat (Mp2Frame (), Mp2Frame ()), mpeg2: false));
		var data = Concat (stream, PrivAppended (fake));
		var plain = MpegPsFile.Read (stream).File!;

		foreach (var file in new[] {
			MpegPsFile.Read (data).File!,
			(MpegPsFile)MediaFile.Read (new MemoryStream (data), "clip.mpg").File!,
			(MpegPsFile)MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg").GetAwaiter ().GetResult ().File!
		}) {
			Assert.AreEqual (plain.Duration, file.Duration);
			Assert.IsNull (file.AudioProperties);
			Assert.AreEqual (MediaTypes.Video, file.MediaTypes);
		}
	}

	[TestMethod]
	public void Read_TagInsideAnApeTagBeforeTheAppendedTag_DoesNotChangeTheProperties ()
	{
		// TagLib#'s order: stream, APEv2, appended ID3v2, ID3v1. The stream ends at the APE tag.
		var stream = ProgramStream (new Options { AudioKind = Audio.None, VideoFrames = 75 });
		var fake = Concat (PackHeader1 (0), Pes (0xC0, 0, Concat (Mp2Frame (), Mp2Frame ()), mpeg2: false));
		var ape = ApeWithBinary (fake);
		var data = Concat (stream, ape, Appended ("After Ape"), Id3v1 ("F"));
		var plain = MpegPsFile.Read (stream).File!;

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("After Ape", file.Tag!.Title);
		Assert.AreEqual (plain.Duration, file.Duration);
		Assert.IsNull (file.AudioProperties);
		file.Tag.Title = "Changed";
		var rendered = file.Render (data).ToArray ();
		CollectionAssert.AreEqual (Concat (stream, ape), rendered.AsSpan (0, stream.Length + ape.Length).ToArray ());
	}

	[TestMethod]
	public void Read_ApeAtTheEndReachingIntoThePrependedTag_IsNotATag ()
	{
		// An APEv2 footer whose size measures back into the prepended ID3v2 tag.
		var front = Id3v2 ("Front");
		var stream = ProgramStream ();
		var footer = Concat ("APETAGEX"u8.ToArray (), BitConverter.GetBytes (2000), BitConverter.GetBytes (stream.Length + 32 + 5),
			BitConverter.GetBytes (0), BitConverter.GetBytes (0), new byte[8]);
		var data = Concat (front, stream, footer);

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Front", result.File!.Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File.Duration);
	}

	[TestMethod]
	public void Read_ApeBeforeId3v1ReachingIntoThePrependedTag_IsNotATag ()
	{
		var front = Id3v2 ("Front");
		var stream = ProgramStream ();
		var footer = Concat ("APETAGEX"u8.ToArray (), BitConverter.GetBytes (2000), BitConverter.GetBytes (stream.Length + 32 + 5),
			BitConverter.GetBytes (0), BitConverter.GetBytes (0), new byte[8]);
		var data = Concat (front, stream, footer, Id3v1 ("Footer"));

		var result = MpegPsFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Front", result.File!.Tag!.Title);
		Assert.AreEqual ("Footer", result.File.Id3v1Tag!.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (2), result.File.Duration);
	}

	[TestMethod]
	public void Read_HeadThatReachesTheTags_IsNotReadAgain ()
	{
		// No sequence header, so the head asks for a larger window; it already holds the whole
		// stream up to ID3v1, so a second pass would read the same bytes again.
		var stream = Concat (PackHeader1 (0), Pes (0xE0, Start, Picture (4000), mpeg2: false), PackHeader1 (3600), Pes (0xE0, Start + 3600, Picture (4000), mpeg2: false));
		var data = Concat (stream, Id3v1 ("Footer"));
		using var counting = new CountingStream (data);

		var opened = MediaFile.Read (counting, "clip.mpg");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.IsTrue (counting.BytesRead < 3 * stream.Length, counting.BytesRead.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	const long Start = 45_000;

	sealed class CountingStream (byte[] data) : MemoryStream (data)
	{
		public long BytesRead { get; private set; }

		public override int Read (byte[] buffer, int offset, int count)
		{
			var read = base.Read (buffer, offset, count);
			BytesRead += read;
			return read;
		}
	}

	[TestMethod]
	public void Read_TagInsideAnApeTagAfterAShortStream_DoesNotChangeTheProperties ()
	{
		var stream = ProgramStream (new Options { AudioKind = Audio.None, VideoFrames = 75 });
		var fake = Concat (PackHeader1 (0), Pes (0xC0, 0, Concat (Mp2Frame (), Mp2Frame ()), mpeg2: false));
		var ape = ApeWithBinary (fake);
		var plain = MpegPsFile.Read (stream).File!;

		var file = MpegPsFile.Read (Concat (stream, ape)).File!;

		Assert.AreEqual (plain.Duration, file.Duration);
		Assert.IsNull (file.AudioProperties);
	}

	static void AssertAppendedOnly (byte[] data, byte[] stream, string title)
	{
		var spanFile = MpegPsFile.Read (data).File!;
		var streamed = (MpegPsFile)MediaFile.Read (new MemoryStream (data), "clip.mpg").File!;
		var asyncFile = (MpegPsFile)MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg").GetAwaiter ().GetResult ().File!;
		foreach (var file in new[] { spanFile, streamed, asyncFile }) {
			Assert.IsNull (file.Id3v1Tag);
			Assert.AreEqual (title, file.Id3v2Tag!.Title);
			Assert.AreEqual (title, file.Tag!.Title);
		}

		CollectionAssert.AreEqual (data, spanFile.Render (data).ToArray (), "a no-op save keeps the file");
		spanFile.Tag!.Title = "New Title";
		var rendered = spanFile.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, "ID3"u8));
		CollectionAssert.AreEqual (stream, rendered.AsSpan (0, stream.Length).ToArray ());
		var reread = MpegPsFile.Read (rendered).File!;
		Assert.AreEqual ("New Title", reread.Tag!.Title);
		Assert.IsNull (reread.Id3v1Tag);
	}

	// A v2.4 tag with a footer holding one PRIV frame (4.27): owner "x", then the bytes.
	static byte[] PrivAppended (byte[] payload)
	{
		var frameSize = 2 + payload.Length;
		var body = Concat ("PRIV"u8.ToArray (), Syncsafe (frameSize), [0x00, 0x00, (byte)'x', 0x00], payload);
		var header = Concat ([0x49, 0x44, 0x33, 0x04, 0x00, 0x10], Syncsafe (body.Length));
		var footer = (byte[])header.Clone ();
		footer[0] = (byte)'3';
		footer[1] = (byte)'D';
		footer[2] = (byte)'I';
		return Concat (header, body, footer);
	}

	// An APEv2 tag (footer only) with one binary item.
	static byte[] ApeWithBinary (byte[] value)
	{
		var item = Concat (BitConverter.GetBytes (value.Length), BitConverter.GetBytes (2), "Cover"u8.ToArray (), [0x00], value);
		var footer = Concat ("APETAGEX"u8.ToArray (), BitConverter.GetBytes (2000), BitConverter.GetBytes (item.Length + 32),
			BitConverter.GetBytes (1), BitConverter.GetBytes (0), new byte[8]);
		return Concat (item, footer);
	}

	static byte[] Syncsafe (int value) =>
		[(byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F), (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)];
}
