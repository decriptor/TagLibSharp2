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
			("mp3", Mp3Frames (100), false),
			("id3+adts", Concat (tag, Adts (100, length: 400)), true),
			("adts", Adts (100, length: 400), true) }) {
			using var stream = new CountingStream (data);
			Assert.AreEqual (aac, AacFile.IsValidFormat (stream), label);
			Assert.IsTrue (stream.BytesRead <= 10 + 16 + 7, $"{label}: read {stream.BytesRead} bytes");
		}
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
		var budget = 32 + tag.Length + 32 * 1024 + 128 + 64;
		Assert.IsTrue (stream.BytesRead <= budget, $"read {stream.BytesRead} bytes, budget {budget}");
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
