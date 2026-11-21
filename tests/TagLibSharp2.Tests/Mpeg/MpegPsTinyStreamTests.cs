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
/// Streams shorter than the tags around them: every trailing tag must sit after the prepended
/// tag, and no input makes a reader throw.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("MpegPs")]
public sealed class MpegPsTinyStreamTests
{
	// A pack header and the end code: the shortest program stream there is.
	static byte[] TinyStream () => Concat (PackHeader1 (0), [0x00, 0x00, 0x01, 0xB9]);

	/// <summary>
	/// An ID3v2.3 tag whose TXXX text puts "TAG" 128 bytes before the end of the file, then a
	/// stream shorter than 128 bytes. Those 128 bytes start inside the prepended tag, so they are
	/// not an ID3v1 tag after the stream.
	/// </summary>
	static byte[] OverlappingId3v1 ()
	{
		var stream = TinyStream ();
		for (var pad = 0; pad < 200; pad++) {
			var tag = new Id3v2Tag (Id3v2Version.V23) { Title = "Front" };
			tag.SetUserText ("PAD", "TAG" + new string ('q', pad));
			var bytes = tag.Render (0).ToArray ();
			var data = Concat (bytes, stream);
			if (data.Length >= 128 && data.AsSpan (data.Length - 128, 3).SequenceEqual ("TAG"u8))
				return data;
		}

		throw new InvalidOperationException ("no padding puts TAG at EOF-128");
	}

	[TestMethod]
	public async Task Read_Id3v1RegionInsideThePrependedTag_IsNotId3v1 ()
	{
		var data = OverlappingId3v1 ();
		Assert.IsTrue (TinyStream ().Length < 128);

		var span = MpegPsFile.Read (data);
		var streamed = MediaFile.Read (new MemoryStream (data), "clip.mpg");
		var asyncRead = await MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg");
		var fromData = MediaFile.ReadFromData (data, "clip.mpg");

		Assert.IsTrue (span.IsSuccess, span.Error);
		foreach (var opened in new[] { streamed, asyncRead, fromData }) {
			Assert.IsTrue (opened.IsSuccess, opened.Error);
			Assert.AreEqual (MediaFormat.MpegPs, opened.Format);
		}

		foreach (var file in new[] { span.File!, (MpegPsFile)streamed.File!, (MpegPsFile)asyncRead.File!, (MpegPsFile)fromData.File! }) {
			Assert.AreEqual ("Front", file.Tag!.Title);
			Assert.IsNull (file.Id3v1Tag);
			CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
		}
	}

	[TestMethod]
	public void Read_TinyStreamWithARealId3v1_ReadsIt ()
	{
		var data = Concat (Id3v2 ("Front"), TinyStream (), Id3v1 ("Footer"));

		var file = MpegPsFile.Read (data).File!;

		Assert.AreEqual ("Footer", file.Id3v1Tag!.Title);
		Assert.AreEqual ("Front", file.Tag!.Title);
	}

	[TestMethod]
	public void Read_TagOnlyCutShort_FailsWithoutThrowing ()
	{
		// A tag that declares more bytes than the file has, with "TAG" 128 bytes from the end.
		var tag = new Id3v2Tag (Id3v2Version.V23) { Title = "Cut" };
		tag.SetUserText ("PAD", "TAG" + new string ('q', 400));
		var bytes = tag.Render (4096).ToArray ();
		var cut = bytes.AsSpan (0, bytes.Length - 2048).ToArray ();

		foreach (var length in new[] { cut.Length, cut.Length - 1, 300, 140, 128, 20, 10 }) {
			var data = cut.AsSpan (0, length).ToArray ();
			var result = MpegPsFile.Read (data);
			Assert.IsFalse (result.IsSuccess, length.ToString (System.Globalization.CultureInfo.InvariantCulture));
			_ = MediaFile.ReadFromData (data, "clip.mpg");
			_ = MediaFile.Read (new MemoryStream (data), "clip.mpg");
		}
	}

	/// <summary>
	/// Property: for small files with tags on both sides, cutting the end at every length and
	/// replacing each of the last bytes never makes a reader throw, the three readers agree, and
	/// a file that reads renders back to its own bytes.
	/// </summary>
	[TestMethod]
	public async Task TailMutations_NeverThrowAndTheReadersAgree ()
	{
		byte[] values = [0x00, 0x7F, 0x80, 0xFF, 0x10, 0x04, (byte)'I', (byte)'3', (byte)'T', (byte)'A'];
		var ape = TestBuilders.Ape.CreateTag (title: "Ape");
		var fixtures = new[] {
			OverlappingId3v1 (),
			Concat (Id3v2 ("Front"), TinyStream (), Id3v1 ("Footer")),
			Concat (Id3v2 ("Front"), TinyStream (), Appended ("Back"), Id3v1 ("Footer")),
			Concat (TinyStream (), Appended ("Back"), ape),
			Concat (Id3v2 ("Front"), TinyStream (), ape, Appended ("Back"), Id3v1 ("Footer")),
			Concat (ProgramStream (new Options { VideoFrames = 3, VideoFrameBytes = 16 }), UpdateTag ("Upd"))
		};

		var inputs = 0;
		foreach (var fixture in fixtures) {
			var reach = Math.Min (fixture.Length, 300);
			for (var cut = 0; cut <= reach; cut++)
				await Check (fixture.AsSpan (0, fixture.Length - cut).ToArray ());
			for (var back = 1; back <= reach; back++) {
				foreach (var value in values) {
					var data = (byte[])fixture.Clone ();
					data[^back] = value;
					await Check (data);
				}
			}
		}

		Assert.IsTrue (inputs > 10_000, inputs.ToString (System.Globalization.CultureInfo.InvariantCulture));

		async Task Check (byte[] data)
		{
			inputs++;
			MpegPsFileReadResult span;
			MediaFileResult streamed, asyncRead;
			try {
				span = MpegPsFile.Read (data);
				streamed = MediaFile.Read (new MemoryStream (data), "clip.mpg");
				asyncRead = await MediaFile.ReadAsync (new MemoryStream (data), "clip.mpg");
				_ = MediaFile.ReadFromData (data, "clip.mpg");
			} catch (Exception ex) {
				Assert.Fail ($"{ex.GetType ().Name} on {data.Length} bytes: {ex.Message}");
				return;
			}

			if (!span.IsSuccess)
				return;
			CollectionAssert.AreEqual (data, span.File!.Render (data).ToArray (), "a no-op render keeps the bytes");
			foreach (var other in new[] { streamed, asyncRead }) {
				if (other.File is not MpegPsFile file)
					continue;
				Assert.AreEqual (span.File.Tag?.Title, file.Tag?.Title);
				Assert.AreEqual (span.File.Duration, file.Duration);
				Assert.AreEqual (span.File.Id3v1Tag is null, file.Id3v1Tag is null);
			}
		}
	}
}
