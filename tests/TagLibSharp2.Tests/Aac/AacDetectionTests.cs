// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aac;
using TagLibSharp2.Core;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Tests.Core;

using static TagLibSharp2.Tests.Aac.AacTestBuilder;

namespace TagLibSharp2.Tests.Aac;

/// <summary>
/// A file is AAC when "ADIF" or a confirmed ADTS header (layer '00') sits at offset 0 or right after a
/// leading ID3v2 tag. An MPEG audio frame there stays MP3. Every entry point agrees.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Aac")]
[TestCategory ("FormatDetection")]
public class AacDetectionTests
{
	/// <summary>
	/// Detects through every surface: the span, the bytes, a seekable stream, a non-seekable stream,
	/// a path, and the async twins. All must agree.
	/// </summary>
	static async Task<MediaFormat> DetectEverywhere (byte[] data, string name = "song.bin")
	{
		var span = MediaFile.DetectFormat (data, name);
		var bytes = MediaFile.ReadFromData (data, name);
		var stream = MediaFile.Read (new MemoryStream (data), name);
		var forwardOnly = MediaFile.Read (new ForwardOnlyStream (data), name);
		var asyncStream = await MediaFile.ReadAsync (new MemoryStream (data), name);
		var fs = new MockFileSystem ();
		fs.AddFile ("/" + name, data);
		var path = MediaFile.Read ("/" + name, fs);
		var asyncPath = await MediaFile.ReadAsync ("/" + name, fs);

		foreach (var (label, result) in new[] {
			("bytes", bytes), ("stream", stream), ("forward-only", forwardOnly),
			("async stream", asyncStream), ("path", path), ("async path", asyncPath) }) {
			var format = result.IsSuccess ? result.Format : MediaFormat.Unknown;
			Assert.AreEqual (span, format, $"{label}: {result.Error}");
		}

		return span;
	}

	[TestMethod]
	public async Task BareAdts_IsAac () =>
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Adts (20)));

	[TestMethod]
	public async Task BareAdif_IsAac () =>
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Adif (64_000)));

	[TestMethod]
	public async Task Id3v2ThenAdts_IsAac () =>
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Concat (Id3v2Bytes ("T"), Adts (20))));

	[TestMethod]
	public async Task Id3v2ThenAdif_IsAac () =>
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Concat (Id3v2Bytes ("T"), Adif (64_000))));

	[TestMethod]
	public async Task Id3v2ThenProgramStream_IsMpegPs () =>
		Assert.AreEqual (MediaFormat.MpegPs, await DetectEverywhere (Concat (Id3v2Bytes ("T"), TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder.ProgramStream ())));

	[TestMethod]
	public void Id3v2ThenProgramStreamCarryingAdts_IsMpegPsNotAac ()
	{
		// A program stream is checked first: ADTS audio in its PES packets is not a raw AAC file.
		// ADTS frames right after the first pack and system headers, so the AAC rule alone would claim the file.
		var ps = TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder.ProgramStream ();
		var pack = TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder.PackHeader1 (0).Length
			+ TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder.SystemHeader (true, TagLibSharp2.Tests.Mpeg.MpegPsTestBuilder.Audio.Mp2).Length;
		Adts (4, length: 64).CopyTo (ps, pack);
		var data = Concat (Id3v2Bytes ("T"), ps);
		Assert.IsTrue (AacFile.IsValidFormat (data), "the fixture must look like AAC to the AAC rule");
		Assert.IsTrue (TagLibSharp2.Mpeg.MpegPsFile.IsValidFormat (data));

		Assert.AreEqual (MediaFormat.MpegPs, MediaFile.DetectFormat (data));
		Assert.AreNotEqual (MediaFormat.Aac, MediaFile.Read (new MemoryStream (data)).Format);
		Assert.AreNotEqual (MediaFormat.Aac, MediaFile.ReadFromData (data).Format);
	}

	[TestMethod]
	public async Task Id3v2ThenAdts_IsNotMpegPs ()
	{
		var data = Concat (Id3v2Bytes ("T"), Adts (20));

		Assert.IsFalse (TagLibSharp2.Mpeg.MpegPsFile.IsValidFormat (data));
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (data, "song.mpg"));
	}

	[TestMethod]
	public async Task Id3v2ThenMpegAudio_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Concat (Id3v2Bytes ("T"), Mp3Frames (5))));

	[TestMethod]
	public async Task Id3v2ThenMpegAudio_NamedAac_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Concat (Id3v2Bytes ("T"), Mp3Frames (5)), "song.aac"));

	[TestMethod]
	public async Task BareMpegAudio_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Mp3Frames (5)));

	[TestMethod]
	public async Task Id3v2ThenNothing_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Concat (Id3v2Bytes ("T"), new byte[100])));

	[TestMethod]
	[DataRow ("song.aac")]
	[DataRow ("song.mp3")]
	[DataRow ("song.bin")]
	public async Task Id3v2ThenPaddingThenAdts_IsAac (string name)
	{
		// Bytes between the tag and the first frame: the reader's first-frame window decides detection too.
		foreach (var gap in new[] { 1, 1024, AacStream.FirstFrameSearchBytes - 1 }) {
			var data = Concat (Id3v2Bytes ("T"), new byte[gap], Adts (20));

			Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (data, name), $"gap {gap}");
			Assert.AreEqual (20, MediaFile.ReadFromData (data, name).GetFileAs<AacFile> ()!.Properties!.FrameCount);
		}
	}

	[TestMethod]
	public async Task Id3v2ThenPaddingThenAdif_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Concat (Id3v2Bytes ("T"), new byte[10], Adif (64_000))));

	[TestMethod]
	public async Task Id3v2ThenPaddingPastTheWindowThenAdts_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (
			Concat (Id3v2Bytes ("T"), new byte[AacStream.FirstFrameSearchBytes], Adts (20)), "song.aac"));

	[TestMethod]
	public async Task Id3v2ThenPaddingThenMpegAudio_StaysMp3 () =>
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Concat (Id3v2Bytes ("T"), new byte[1024], Mp3Frames (5)), "song.aac"));

	[TestMethod]
	public async Task Id3v2ThenMpegAudioThenAdts_StaysMp3 ()
	{
		// The first confirmed sync after the tag decides. MPEG audio comes first here.
		var data = Concat (Id3v2Bytes ("T"), new byte[10], Mp3Frames (3), Adts (20));

		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (data, "song.aac"));
		Assert.IsFalse (AacFile.Read (data).IsSuccess);
	}

	[TestMethod]
	public async Task OneFrameThenATrailingTag_IsAac ()
	{
		// A trailing tag after the frame confirms where the frame ends, like the end of the file does.
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Concat (AdtsFrame (100), Id3v1Bytes ("Back"))));
		// An APEv2 tag with a header (as ffmpeg writes it) starts with "APETAGEX".
		var ape = new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray ();
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Concat (Id3v2Bytes ("T"), AdtsFrame (100), ape)));
	}

	[TestMethod]
	public void TruncatedId3TaggedMp3_IsNeverAac ()
	{
		// An MPEG header that cannot be checked (its frame runs past the data) decides MP3, as an ADTS
		// header that cannot be checked decides AAC. Every prefix past the tag stays MP3.
		var data = Mp3WithAdtsLookalike ();
		var tagEnd = Id3v2Bytes ("Tagged").Length;
		for (var length = tagEnd + 4; length <= data.Length; length++) {
			var prefix = data.AsSpan (0, length);
			Assert.AreEqual (MediaFormat.Mp3, MediaFile.DetectFormat (prefix), $"prefix {length}");
			Assert.IsTrue (Mp3File.IsValidFormat (prefix), $"prefix {length}");
			Assert.IsFalse (AacFile.IsValidFormat (prefix), $"prefix {length}");
		}
	}

	[TestMethod]
	[DataRow (200)]
	[DataRow (274)]
	[DataRow (600)]
	public async Task TruncatedId3TaggedMp3File_OpensAsMp3 (int afterTag)
	{
		// Cut inside the first or second MPEG frame, past the ADTS look-alike at 0x93.
		var data = Mp3WithAdtsLookalike ().AsSpan (0, Id3v2Bytes ("Tagged").Length + afterTag).ToArray ();

		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (data, "cut.mp3"));
	}

	[TestMethod]
	public void Detection_ReadsOnlyTheFirstHeaders ()
	{
		// The first bytes after the tag decide an MP3 or a clean AAC file: the header and its successor.
		var tag = Id3v2Bytes ("T");
		foreach (var (label, data, aac) in new[] {
			("id3+mp3", Concat (tag, Mp3Frames (100)), false),
			("id3+mp3+id3v1", Concat (tag, Mp3Frames (100), Id3v1Bytes ("Back")), false),
			("mp3", Mp3Frames (100), false),
			("id3+adts", Concat (tag, Adts (100, length: 400)), true),
			("adts", Adts (100, length: 400), true) }) {
			using var stream = new CountingStream (data);
			Assert.AreEqual (aac, AacFile.IsValidFormat (stream), label);
			// An AAC start also checks the trailing-tag markers: 3 bytes at EOF-128 and 8 at EOF-32. An MP3, even
			// with an ID3v1 tag, is decided at its first frame and never reads the tail.
			var budget = 10 + 16 + 7 + (aac ? 3 + 8 : 0);
			Assert.IsTrue (stream.BytesRead <= budget, $"{label}: read {stream.BytesRead} bytes, budget {budget}");
		}
	}

	// The fewest bytes one call allocates over a few runs, so a one-off runtime allocation (a JIT or a
	// lazily built cache landing on this thread) does not count against the code under test.
	static long Allocated (Action action)
	{
		action ();
		var least = long.MaxValue;
		for (var run = 0; run < 5; run++) {
			var before = GC.GetAllocatedBytesForCurrentThread ();
			action ();
			least = Math.Min (least, GC.GetAllocatedBytesForCurrentThread () - before);
		}
		return least;
	}

	[TestMethod]
	public void SpanDetectionAndReadFromData_OnALargeTaggedFile_DoNotCopyIt ()
	{
		// 4 MB of ADTS frames with trailing tags: every span entry point finds the audio end on the span itself.
		var frames = Adts (10_000, length: 400);
		var ape = new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray ();
		foreach (var (label, data) in new[] {
			("ADTS + ID3v1", Concat (frames, Id3v1Bytes ("Back"))),
			("ID3v2 + ADTS + ID3v1", Concat (Id3v2Bytes ("Front"), frames, Id3v1Bytes ("Back"))),
			("ID3v2 + ADTS + APEv2 + Lyrics3 + ID3v1", Concat (Id3v2Bytes ("Front"), frames, ape, Lyrics3V2 (), Id3v1Bytes ("Back"))) }) {
			Assert.IsTrue (data.Length > 4_000_000);
			Assert.IsTrue (Allocated (() => MediaFile.DetectFormat (data)) < 64 * 1024, $"{label}: DetectFormat");
			Assert.IsTrue (Allocated (() => AacFile.IsValidFormat (data)) < 64 * 1024, $"{label}: AacFile.IsValidFormat");
			Assert.IsTrue (Allocated (() => Mp3File.IsValidFormat (data)) < 64 * 1024, $"{label}: Mp3File.IsValidFormat");
			var read = Allocated (() => MediaFile.ReadFromData (data));
			Assert.IsTrue (read < 512 * 1024, $"{label}: ReadFromData allocated {read} bytes");
			Assert.AreEqual (MediaFormat.Aac, MediaFile.DetectFormat (data), label);
		}
	}

	[TestMethod]
	public void SpanDetection_OfATaggedMp3_DoesNotParseItsTrailingTags ()
	{
		// The first MPEG frame decides MP3 before the audio end is needed, so the tail tags are never parsed.
		var ape = new TagLibSharp2.Ape.ApeTag { Title = "Ape", Album = "Album", Comment = new string ('c', 2000) }.RenderWithOptions (includeHeader: true).ToArray ();
		var data = Concat (Id3v2Bytes ("T"), Mp3Frames (50), ape, Id3v1Bytes ("Back"));

		Assert.IsTrue (Mp3File.IsValidFormat (data));
		var allocated = Allocated (() => Mp3File.IsValidFormat (data));
		Assert.IsTrue (allocated < 1024, $"allocated {allocated} bytes");
	}

	[TestMethod]
	public void MediaFileRead_Id3TaggedMp3_ReadsNoMoreThanTheMp3ReaderNeeds ()
	{
		// MP3's own windowed read: the tag, a 32 KB audio probe, and the 128-byte footer.
		var tag = Id3v2Bytes ("T");
		var data = Concat (tag, Mp3Frames (1000));
		using var stream = new CountingStream (data);

		var result = MediaFile.Read (stream, "song.mp3");

		Assert.AreEqual (MediaFormat.Mp3, result.Format);
		// The MPEG-PS probe after the tag (MpegPsFile.StartsAfterId3v2) adds its 10-byte header and 64-byte reads.
		var budget = 32 + tag.Length + 32 * 1024 + 128 + 64 + 10 + 64;
		Assert.IsTrue (stream.BytesRead <= budget, $"read {stream.BytesRead} bytes, budget {budget}");
	}

	[TestMethod]
	public async Task Id3v2ThenPaddingThenOneFrame_ATrailingTagDoesNotChangeTheAnswer ()
	{
		// The frame after the gap cannot be checked: it ends where the audio ends, at the end of the file or at
		// a trailing tag alike. It is not the first byte, so it is not AAC either way, and the file stays MP3.
		var audio = Concat (Id3v2Bytes ("T"), new byte[100], AdtsFrame (300));
		foreach (var (name, tail) in TrailingTags ()) {
			var data = Concat (audio, tail);
			Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (data, "song.aac"), name);
		}
	}

	[TestMethod]
	public async Task FalseMpegHeaderInTheGap_ATrailingTagDoesNotChangeTheAnswer ()
	{
		// MPEG-2 Layer II, 160 kbps, 16 kHz: a 1440-byte frame that runs past the audio, with or without a tag
		// after it. An MPEG header that cannot be checked decides MP3.
		byte[] falseMpeg = [0xFF, 0xF5, 0xE8, 0x00];
		var gap = Concat (new byte[10], falseMpeg, new byte[10]);
		var audio = Concat (Id3v2Bytes ("T"), gap, Adts (3, length: 450));
		foreach (var (name, tail) in TrailingTags ())
			Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (Concat (audio, tail)), name);
	}

	/// <summary>Audio shapes whose answer and frame count must not depend on a trailing tag.</summary>
	static IEnumerable<(string Name, byte[] Bytes)> AudioShapes () => [
		("one frame", AdtsFrame (300)),
		("one frame cut", AdtsFrame (300)[..200]),
		("three frames", Adts (3, length: 300)),
		("three frames, last cut", Adts (3, length: 300)[..750]),
		("header only", AdtsFrame (300)[..7]),
		("padding then frames", Concat (new byte[100], Adts (3, length: 300))),
		("padding then one frame", Concat (new byte[100], AdtsFrame (300))),
		("ADIF", Adif (64_000, payloadBytes: 500))
	];

	[TestMethod]
	public async Task ATrailingTag_NeverChangesTheAnswerOrTheFrameCount ()
	{
		// The audio ends at the end of the file or at a trailing tag. Either one ends a frame the same way, so
		// detection and the frame count are those of the same audio with no tag.
		foreach (var withId3v2 in new[] { false, true }) {
			foreach (var (shape, audio) in AudioShapes ()) {
				var bare = withId3v2 ? Concat (Id3v2Bytes ("T"), audio) : audio;
				// By content (no extension to fall back on), and the reader on its own.
				var expected = await DetectEverywhere (bare);
				var expectedRead = AacFile.Read (bare);
				foreach (var (tagName, tail) in TrailingTags ()) {
					var data = Concat (bare, tail);
					var label = $"{shape} + {tagName}, ID3v2 {withId3v2}";
					Assert.AreEqual (expected, await DetectEverywhere (data), label);
					var read = AacFile.Read (data);
					Assert.AreEqual (expectedRead.IsSuccess, read.IsSuccess, label);
					Assert.AreEqual (expectedRead.File?.Properties?.FrameCount, read.File?.Properties?.FrameCount, label);
					Assert.AreEqual (expectedRead.File?.Properties?.Duration, read.File?.Properties?.Duration, label);
				}
			}
		}
	}

	[TestMethod]
	public async Task TruncatedAdifHeader_IsNotDetectedEither ()
	{
		var adif = Adif (64_000, comment: "a comment", payloadBytes: 0);
		var data = Concat (Id3v2Bytes ("T"), adif.AsSpan (0, adif.Length - 3).ToArray ());

		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (data));
		Assert.IsFalse (AacFile.IsValidFormat (adif.AsSpan (0, adif.Length - 3)));
	}

	[TestMethod]
	public void Id3TaggedNonMpegData_WithALateAdtsLookalike_IsNeverAac ()
	{
		// An ADTS header that cannot be checked counts only at the first byte after the tag. A FLAC frame
		// sync (FF F8) is ADTS-shaped, so FLAC, WavPack, MP4 or Matroska data behind an ID3v2 tag would
		// otherwise detect as AAC on some prefix.
		byte[] lookalike = [0xFF, 0xF8, 0x15, 0x07, 0x09, 0xCB, 0x00];
		var data = Concat (Id3v2Bytes ("T"), "fLaC"u8.ToArray (), new byte[40], lookalike, new byte[300]);
		var tagEnd = Id3v2Bytes ("T").Length;
		for (var length = tagEnd + 1; length <= data.Length; length++) {
			Assert.AreNotEqual (MediaFormat.Aac, MediaFile.DetectFormat (data.AsSpan (0, length)), $"prefix {length}");
			Assert.IsFalse (AacFile.IsValidFormat (data.AsSpan (0, length)), $"prefix {length}");

			// The stream twin, which MediaFile.Read (path or stream) uses.
			var prefix = data.AsSpan (0, length).ToArray ();
			using (var stream = new MemoryStream (prefix))
				Assert.IsFalse (AacFile.IsValidFormat (stream), $"stream prefix {length}");
			using (var stream = new MemoryStream (prefix))
				Assert.AreNotEqual (MediaFormat.Aac, MediaFile.Read (stream).Format, $"MediaFile.Read prefix {length}");
		}
	}

	/// <summary>The trailing tags a cut start header can run into.</summary>
	static IEnumerable<(string Name, byte[] Bytes)> TrailingTags () => [
		("none", []),
		("ID3v1", Id3v1Bytes ("Back")),
		("APEv2", new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray ()),
		("APEv2+ID3v1", Concat (new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray (), Id3v1Bytes ("Back"))),
		("footer-only APEv2", ApeBytes ("Ape")),
		("Lyrics3v1+ID3v1", Concat (Lyrics3V1 (), Id3v1Bytes ("Back"))),
		("Lyrics3v2+ID3v1", Concat (Lyrics3V2 (), Id3v1Bytes ("Back"))),
		("APEv2+Lyrics3v2+ID3v1", Concat (new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray (), Lyrics3V2 (), Id3v1Bytes ("Back"))),
		("APEv2 claiming a missing header", ApeClaimingAMissingHeader ()),
		("APEv2 claiming a missing header+ID3v1", Concat (ApeClaimingAMissingHeader (), Id3v1Bytes ("Back")))
	];

	// Every tail above, plus one whose 32 "header" bytes are not a header. Those bytes are audio by the tail rule,
	// so they can change the answer; the entry points must still agree on it.
	static IEnumerable<(string Name, byte[] Bytes)> AllTails () => TrailingTags ().Concat ([
		("APEv2 with a damaged header", ApeWithADamagedHeader ()),
		("APEv2 with a damaged header+ID3v1", Concat (ApeWithADamagedHeader (), Id3v1Bytes ("Back")))
	]);

	// A footer-only tag whose footer sets bit 31, "contains a header", with no header in front of the items.
	static byte[] ApeClaimingAMissingHeader ()
	{
		var tag = ApeBytes ("Ape");
		tag[^9] |= 0x80; // footer flags, little-endian, bytes 20-23 of the 32-byte footer
		return tag;
	}

	// A tag with a header whose "APETAGEX" is damaged, so the footer's header flag is not backed by a header.
	static byte[] ApeWithADamagedHeader ()
	{
		var tag = new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray ();
		tag[2] ^= 0x20;
		return tag;
	}

	// Lyrics3 v1 (https://id3.org/Lyrics3): LYRICSBEGIN, text, LYRICSEND, directly before ID3v1.
	static byte[] Lyrics3V1 () => Concat ("LYRICSBEGIN"u8.ToArray (), "la la la"u8.ToArray (), "LYRICSEND"u8.ToArray ());

	// Lyrics3 v2 (https://id3.org/Lyrics3v2): LYRICSBEGIN and fields, a 6-digit size of that body, LYRICS200.
	static byte[] Lyrics3V2 ()
	{
		var body = Concat ("LYRICSBEGIN"u8.ToArray (), "IND00003110"u8.ToArray (), "LYR00008la la la"u8.ToArray ());
		var size = System.Text.Encoding.ASCII.GetBytes (body.Length.ToString ("D6", System.Globalization.CultureInfo.InvariantCulture));
		return Concat (body, size, "LYRICS200"u8.ToArray ());
	}

	[TestMethod]
	public void SpanDetectionSpanReadAndStreamRead_AlwaysAgree ()
	{
		// One tail rule for every entry point: detection on a span and on a stream, and the read on each, judge
		// the same bytes. Cuts through the first frames, every trailing tag (malformed footers too), with and
		// without ID3v2.
		var audio = Adts (3, length: 300);
		var cuts = Enumerable.Range (1, 40).Concat (Enumerable.Range (293, 18)).Concat (Enumerable.Range (593, 18)).Append (audio.Length);
		var cases = 0;
		foreach (var withId3v2 in new[] { false, true }) {
			foreach (var cut in cuts) {
				foreach (var (tagName, tail) in AllTails ()) {
					var body = Concat (audio[..cut], tail);
					var data = withId3v2 ? Concat (Id3v2Bytes ("T"), body) : body;
					AssertEntryPointsAgree (data, $"cut {cut} + {tagName}, ID3v2 {withId3v2}");
					cases++;
				}
			}
		}
		Assert.IsTrue (cases > 1000);
	}

	[TestMethod]
	public void SpanAndStream_AgreeOnMutatedTrailingTags ()
	{
		// Random cuts and random byte changes in an APEv2 tag (header, items, footer), alone, before ID3v1, and
		// before Lyrics3 v2 + ID3v1.
		var random = new Random (144);
		var audio = Adts (3, length: 300);
		var apeTag = new TagLibSharp2.Ape.ApeTag { Title = "Ape" }.RenderWithOptions (includeHeader: true).ToArray ();
		for (var round = 0; round < 3000; round++) {
			var ape = (byte[])apeTag.Clone ();
			for (var flips = random.Next (1, 3); flips > 0; flips--)
				ape[random.Next (ape.Length)] = (byte)random.Next (256);
			if (random.Next (4) == 0)
				ape = ape[32..]; // drop the header, keep the footer's flags
			var tail = (round % 3) switch {
				0 => ape,
				1 => Concat (ape, Id3v1Bytes ("Back")),
				_ => Concat (ape, Lyrics3V2 (), Id3v1Bytes ("Back"))
			};
			var body = Concat (audio[..random.Next (1, audio.Length + 1)], tail);
			var data = random.Next (2) == 0 ? body : Concat (Id3v2Bytes ("T"), body);
			AssertEntryPointsAgree (data, $"round {round}");
		}
	}

	static void AssertEntryPointsAgree (byte[] data, string label)
	{
		var spanValid = AacFile.IsValidFormat (data);
		using (var stream = new MemoryStream (data))
			Assert.AreEqual (spanValid, AacFile.IsValidFormat (stream), $"{label}: span vs stream detection");

		var spanRead = AacFile.Read (data);
		AacFileReadResult streamRead;
		using (var stream = new MemoryStream (data))
			streamRead = AacFile.Read (stream, null, null);
		Assert.AreEqual (spanRead.IsSuccess, streamRead.IsSuccess, $"{label}: span vs stream read");
		Assert.AreEqual (spanRead.File?.Properties?.FrameCount, streamRead.File?.Properties?.FrameCount, $"{label}: frames");
		if (spanValid)
			Assert.IsTrue (spanRead.IsSuccess, $"{label}: detected as AAC but did not open: {spanRead.Error}");

		var detected = MediaFile.DetectFormat (data);
		var fromData = MediaFile.ReadFromData (data);
		MediaFileResult fromStream;
		using (var stream = new MemoryStream (data))
			fromStream = MediaFile.Read (stream);
		Assert.AreEqual (detected == MediaFormat.Aac, fromData.IsSuccess && fromData.Format == MediaFormat.Aac, $"{label}: DetectFormat vs ReadFromData");
		Assert.AreEqual (detected == MediaFormat.Aac, fromStream.IsSuccess && fromStream.Format == MediaFormat.Aac, $"{label}: DetectFormat vs Read (stream)");
	}

	[TestMethod]
	public async Task StartHeaderCutByATrailingTag_IsNeverAac ()
	{
		// A start header must lie inside the audio region. With a trailing tag after a cut header, the tag's
		// bytes would otherwise complete it ("TAG..." parsed as ADIF or ADTS fields).
		var adif = Adif (64_000, comment: "x", payloadBytes: 0);
		var adts = AdtsFrame (64)[..7];
		foreach (var (kind, header) in new[] { ("ADIF", adif), ("ADTS", adts) }) {
			foreach (var (tagName, tail) in TrailingTags ()) {
				for (var cut = 3; cut < header.Length; cut++) {
					var data = Concat (Id3v2Bytes ("Front"), header[..cut], tail);
					var label = $"{kind} cut {cut} + {tagName}";

					Assert.AreNotEqual (MediaFormat.Aac, await DetectEverywhere (data), label);
					Assert.IsFalse (AacFile.Read (data).IsSuccess, label);
					Assert.IsFalse (AacFile.IsValidFormat (data), label);
				}

				// The whole header (for ADTS, the whole frame, which the tag then confirms) opens as AAC.
				var whole = Concat (Id3v2Bytes ("Front"), kind == "ADTS" ? AdtsFrame (64) : header, tail);
				Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (whole), $"{kind} whole + {tagName}");
			}
		}
	}

	[TestMethod]
	public async Task UncheckableMpegHeaderBeforeConfirmedAdts_IsMp3 ()
	{
		// The MPEG frame (417 bytes) runs past the data, so it cannot be checked, and it decides MP3 even though
		// confirmed ADTS frames follow inside it.
		var data = Concat (Id3v2Bytes ("T"), new byte[1], Mp3Frames (1).AsSpan (0, 4).ToArray (), new byte[4], Adts (3), new byte[20]);

		Assert.IsFalse (AacFile.IsValidFormat (data));
		Assert.AreEqual (MediaFormat.Mp3, await DetectEverywhere (data));
	}

	[TestMethod]
	public async Task AdtsHeaderCutByTheStreamChunkEdge_IsFoundOnEveryPath ()
	{
		// The stream search reads 16 bytes, then 4 KB: a header at 4106-4111 bytes past the tag straddles
		// that chunk's end and must be read again whole.
		var tag = Id3v2Bytes ("T");
		for (var gap = 4100; gap <= 4113; gap++) {
			var data = Concat (tag, new byte[gap], Adts (20));
			Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (data), $"gap {gap}");

			// Both twins name the same first frame, so no frame is lost.
			using var stream = new MemoryStream (data);
			Assert.AreEqual (gap, AacStream.FindStart (data.AsSpan (tag.Length), AacStream.FirstFrameSearchBytes, -1, out _), $"span, gap {gap}");
			Assert.AreEqual (tag.Length + gap, AacStream.FindStart (stream, tag.Length, AacStream.FirstFrameSearchBytes, -1, out _), $"stream, gap {gap}");
			Assert.AreEqual (20, MediaFile.ReadFromData (data).GetFileAs<AacFile> ()!.Properties!.FrameCount, $"gap {gap}");
		}
	}

	[TestMethod]
	public async Task LargeId3v2ThenAdts_IsAacThroughTheShortPeek ()
	{
		// The stream peek is 32 bytes and the tag is far longer.
		var tag = new TagLibSharp2.Id3.Id3v2.Id3v2Tag { Title = "T", Comment = new string ('c', 20_000) };
		var data = Concat (tag.Render ().ToArray (), Adts (20));

		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (data));
	}

	[TestMethod]
	public async Task AdtsFrameLongerThanTheLongPeek_IsAac () =>
		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (Adts (4, length: 8000)));

	[TestMethod]
	public async Task AdtsHeaderTheNextHeaderContradicts_IsNotAac ()
	{
		var data = Concat (AdtsFrame (64), new byte[200]);

		Assert.AreEqual (MediaFormat.Unknown, await DetectEverywhere (data));
	}

	[TestMethod]
	public async Task AacExtension_WithoutAStream_FailsOnTheAacReader ()
	{
		var data = new byte[200];
		var result = MediaFile.ReadFromData (data, "song.aac");

		Assert.AreEqual (MediaFormat.Aac, MediaFile.DetectFormat (data, "song.aac"));
		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "AAC");
		var streamed = MediaFile.Read (new MemoryStream (data), "song.aac");
		Assert.IsFalse (streamed.IsSuccess);
		Assert.AreEqual (result.Error, streamed.Error);
		var asyncStreamed = await MediaFile.ReadAsync (new MemoryStream (data), "song.aac");
		Assert.AreEqual (result.Error, asyncStreamed.Error);
	}

	[TestMethod]
	public async Task AacExtension_WithAdtsAfterJunk_UsesTheFallback ()
	{
		// No magic at 0, so only the extension names the format. The reader then finds the first frame.
		var data = Concat (new byte[100], Adts (10));

		Assert.AreEqual (MediaFormat.Aac, await DetectEverywhere (data, "song.aac"));
		Assert.AreEqual (MediaFormat.Unknown, MediaFile.DetectFormat (data, "song.bin"));
	}

	[TestMethod]
	public void AacExtension_OnMp4_StaysMp4 ()
	{
		var data = TestBuilders.Mp4.CreateWithMetadata ("Mp4");

		Assert.AreEqual (MediaFormat.Mp4, MediaFile.DetectFormat (data, "song.aac"));
	}

	[TestMethod]
	public void Utf16Bom_IsNotAac () =>
		Assert.AreEqual (MediaFormat.Unknown, MediaFile.DetectFormat ([0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00]));

	[TestMethod]
	public void Mp3IsValidFormat_IsFalseForId3PrefixedAac ()
	{
		Assert.IsFalse (Mp3File.IsValidFormat (Concat (Id3v2Bytes ("T"), Adts (5))));
		Assert.IsTrue (Mp3File.IsValidFormat (Concat (Id3v2Bytes ("T"), Mp3Frames (5))));
		Assert.IsTrue (Mp3File.IsValidFormat (Id3v2Bytes ("T").AsSpan (0, 10)));
		Assert.IsFalse (Mp3File.IsValidFormat (Adts (5)));
	}

	[TestMethod]
	public void ReadFromData_Id3PrefixedAac_ReadsTheTagAndTheStream ()
	{
		var result = MediaFile.ReadFromData (Concat (Id3v2Bytes ("Front"), Adts (20)), "x.mp3");

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Front", result.Tag!.Title);
		var file = result.GetFileAs<AacFile> ()!;
		Assert.AreEqual (20, file.Properties!.FrameCount);
	}

	/// <summary>A stream that cannot seek.</summary>
	sealed class ForwardOnlyStream (byte[] data) : Stream
	{
		readonly MemoryStream _inner = new (data);

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException ();
		public override long Position {
			get => throw new NotSupportedException ();
			set => throw new NotSupportedException ();
		}

		public override void Flush () { }
		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);
		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_inner.Dispose ();
			base.Dispose (disposing);
		}
	}
}
