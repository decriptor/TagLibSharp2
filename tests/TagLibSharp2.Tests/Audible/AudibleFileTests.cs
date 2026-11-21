// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Audible;
using TagLibSharp2.Core;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Audible;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Audible")]
public class AudibleFileTests
{
	[TestMethod]
	public void Read_ValidFile_ReadsTheDictionary ()
	{
		var result = AudibleFile.Read (AudibleTestBuilder.Create ());

		Assert.IsTrue (result.IsSuccess, result.Error);
		var tag = result.File!.Tag;
		Assert.AreEqual ("A Test Book (Unabridged)", tag.Title);
		Assert.AreEqual ("Ann Author", tag.Artist);
		Assert.AreEqual ("Ann Author", tag.Author);
		Assert.AreEqual ("Ned Narrator", tag.Narrator);
		Assert.AreEqual ("Test Provider", tag.Provider);
		Assert.AreEqual ("20-JAN-2009", tag.PublishDate);
		Assert.AreEqual ("2009", tag.Year);
		Assert.AreEqual ("Short description.", tag.Description);
		Assert.AreEqual ("A longer description of the book.", tag.LongDescription);
		Assert.AreEqual ("one, two", tag.Keywords);
		Assert.AreEqual ("&#169;2009 Ann Author; (P)2009 Test", tag.Copyright);
		Assert.AreEqual ("A Test Book", tag.ShortTitle);
		Assert.AreEqual ("A Test Book (Unabridged)", tag.ParentTitle);
		Assert.AreEqual ("A Test Book", tag.ParentShortTitle);
		Assert.AreEqual ("PF_TEST_000001_acelp85", tag.ProductId);
		Assert.AreEqual ("PF_TEST_000001", tag.TitleId);
		Assert.AreEqual ("acelp85", tag.Codec);
		Assert.AreEqual ("1.98", tag.GetValue ("price"));
		Assert.AreEqual ("6BBD842E462FE82ED754F4EBC32C096EF3120A54", tag.GetValue ("68ec733412b9"));
		CollectionAssert.AreEqual (AudibleTestBuilder.DefaultEntries.Select (e => e.Key).ToList (), tag.Keys.ToList ());
		Assert.AreEqual (TagTypes.Audible, tag.TagType);
		Assert.AreEqual (MediaFormat.Audible, result.File.Format);
	}

	[TestMethod]
	public void Read_KeysWithoutATagField_ReturnNull ()
	{
		var tag = AudibleFile.Read (AudibleTestBuilder.Create ()).File!.Tag;

		Assert.IsNull (tag.Album);
		Assert.IsNull (tag.Comment);
		Assert.IsNull (tag.Genre);
		Assert.IsNull (tag.Track);
		Assert.IsNull (tag.GetValue ("missing"));
	}

	[TestMethod]
	[DataRow ("acelp85", 8500, 8, 8000, 1, "ACELP.net")]
	[DataRow ("acelp16", 16000, 16, 16000, 1, "ACELP.net")]
	[DataRow ("mp332", 32000, 32, 22050, 0, "MP3")]
	[DataRow ("ACELP16", 16000, 16, 16000, 1, "ACELP.net")]
	public void Read_Codec_DurationIsAudioBitsOverBitRate (string codec, int bitsPerSecond, int kbps, int sampleRate, int channels, string name)
	{
		// Eight seconds of audio is exactly bitsPerSecond bytes.
		var audio = AudibleTestBuilder.Audio (bitsPerSecond);

		var file = AudibleFile.Read (AudibleTestBuilder.Create (audio: audio, codec: codec)).File!;

		Assert.AreEqual (TimeSpan.FromSeconds (8), file.Properties.Duration);
		Assert.AreEqual (kbps, file.Properties.Bitrate);
		Assert.AreEqual (sampleRate, file.Properties.SampleRate);
		Assert.AreEqual (channels, file.Properties.Channels);
		Assert.AreEqual (name, file.Properties.Codec);
		Assert.AreEqual (MediaTypes.Audio, ((IMediaFile)file).MediaTypes);
	}

	[TestMethod]
	public void Read_DurationMatchesFfprobeOnTheRealAcelp85Sample ()
	{
		// first.aa: chunk 10 is 3032215 bytes, codec acelp85; ffprobe reports duration=2853.849412.
		var file = AudibleFile.Read (AudibleTestBuilder.Create (audio: new byte[3032215])).File!;

		Assert.AreEqual (2853.849412, file.Properties.Duration.TotalSeconds, 0.000001);
	}

	[TestMethod]
	public void Read_UnknownCodec_HasNoDuration ()
	{
		var file = AudibleFile.Read (AudibleTestBuilder.Create (codec: "sony5")).File!;

		Assert.AreEqual (TimeSpan.Zero, file.Properties.Duration);
		Assert.AreEqual (0, file.Properties.Bitrate);
		Assert.AreEqual ("sony5", file.Properties.Codec);
		Assert.AreEqual (MediaTypes.None, ((IMediaFile)file).MediaTypes);
	}

	[TestMethod]
	public void Read_Cover_IsTheFrontCoverPicture ()
	{
		var jpeg = AudibleTestBuilder.Jpeg ();

		var tag = AudibleFile.Read (AudibleTestBuilder.Create (cover: jpeg)).File!.Tag;

		Assert.HasCount (1, tag.Pictures);
		Assert.AreEqual ("image/jpeg", tag.Pictures[0].MimeType);
		Assert.AreEqual (PictureType.FrontCover, tag.Pictures[0].PictureType);
		Assert.AreEqual (string.Empty, tag.Pictures[0].Description);
		CollectionAssert.AreEqual (jpeg, tag.Pictures[0].PictureData.ToArray ());
	}

	[TestMethod]
	public void Read_NoCover_HasNoPicture ()
	{
		var tag = AudibleFile.Read (AudibleTestBuilder.Create ()).File!.Tag;

		Assert.IsEmpty (tag.Pictures);
	}

	[TestMethod]
	[DataRow (0u)]
	[DataRow (100_000u)]
	public void Read_CoverPositionOutsideItsChunk_HasNoPicture (uint position)
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var (_, offset, _) = AudibleTestBuilder.Toc (data, 11);
		AudibleTestBuilder.SetU32 (data, (int)offset + 4, position);

		var result = AudibleFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.IsEmpty (result.File!.Tag.Pictures);
	}

	[TestMethod]
	public void Read_CoverLengthPastItsChunk_HasNoPicture ()
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var (_, offset, _) = AudibleTestBuilder.Toc (data, 11);
		AudibleTestBuilder.SetU32 (data, (int)offset, 65);

		Assert.IsEmpty (AudibleFile.Read (data).File!.Tag.Pictures);
	}

	[TestMethod]
	public void Read_FileTruncatedAfterTheDictionary_StillReadsTagsAndDuration ()
	{
		// The real samples are cut at 10 KiB, well inside the audio, while the TOC describes the whole file.
		var full = AudibleTestBuilder.Create (audio: new byte[85_000], cover: AudibleTestBuilder.Jpeg ());
		var (_, dictionaryOffset, dictionarySize) = AudibleTestBuilder.Toc (full, 2);
		var cut = full.AsSpan (0, (int)(dictionaryOffset + dictionarySize + 100)).ToArray ();

		var result = AudibleFile.Read (cut);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("A Test Book (Unabridged)", result.File!.Tag.Title);
		Assert.AreEqual (TimeSpan.FromSeconds (80), result.File.Properties.Duration);
		Assert.AreEqual ((uint)full.Length, result.File.DeclaredFileSize);
		Assert.IsEmpty (result.File.Tag.Pictures);
	}

	[TestMethod]
	public void Read_EveryPrefixShorterThanTheDictionaryEnd_FailsWithoutThrowing ()
	{
		var full = AudibleTestBuilder.Create ();
		var (_, offset, size) = AudibleTestBuilder.Toc (full, 2);
		var dictionaryEnd = (int)(offset + size);

		for (var length = 0; length < dictionaryEnd; length++) {
			var result = AudibleFile.Read (full.AsSpan (0, length));
			Assert.IsFalse (result.IsSuccess, $"prefix of {length} bytes");
			Assert.IsNotNull (result.Error);
		}
		Assert.IsTrue (AudibleFile.Read (full.AsSpan (0, dictionaryEnd)).IsSuccess);
	}

	[TestMethod]
	public void Read_WrongMagic_Fails ()
	{
		var data = AudibleTestBuilder.Create ();
		data[4] ^= 0xFF;

		var result = AudibleFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "magic");
	}

	[TestMethod]
	[DataRow (0u)]
	[DataRow (1025u)]
	[DataRow (uint.MaxValue)]
	public void Read_TocCountOutOfRange_Fails (uint count)
	{
		var data = AudibleTestBuilder.Create ();
		AudibleTestBuilder.SetU32 (data, 8, count);

		Assert.IsFalse (AudibleFile.Read (data).IsSuccess);
	}

	[TestMethod]
	public void Read_NoDictionaryChunk_Fails ()
	{
		var data = AudibleTestBuilder.Create ();
		AudibleTestBuilder.SetU32 (data, 16 + (2 * 12), 99);

		var result = AudibleFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "dictionary");
	}

	[TestMethod]
	public void Read_DictionaryPastTheEnd_Fails ()
	{
		var data = AudibleTestBuilder.Create ();
		AudibleTestBuilder.SetU32 (data, 16 + (2 * 12) + 8, uint.MaxValue);

		Assert.IsFalse (AudibleFile.Read (data).IsSuccess);
	}

	[TestMethod]
	public void Read_DictionaryInsideTheToc_Fails ()
	{
		var data = AudibleTestBuilder.Create ();
		AudibleTestBuilder.SetU32 (data, 16 + (2 * 12) + 4, 20);

		Assert.IsFalse (AudibleFile.Read (data).IsSuccess);
	}

	[TestMethod]
	[DataRow (uint.MaxValue)]
	[DataRow (1000u)]
	public void Read_DictionaryCountTheChunkCannotHold_Fails (uint count)
	{
		var data = AudibleTestBuilder.Create ();
		var (_, offset, _) = AudibleTestBuilder.Toc (data, 2);
		AudibleTestBuilder.SetU32 (data, (int)offset, count);

		Assert.IsFalse (AudibleFile.Read (data).IsSuccess);
	}

	[TestMethod]
	[DataRow (1, uint.MaxValue)]
	[DataRow (5, uint.MaxValue)]
	[DataRow (5, 100_000u)]
	public void Read_DictionaryEntryLengthPastTheChunk_Fails (int field, uint value)
	{
		var data = AudibleTestBuilder.Create ();
		var (_, offset, _) = AudibleTestBuilder.Toc (data, 2);
		AudibleTestBuilder.SetU32 (data, (int)offset + 4 + field, value);

		Assert.IsFalse (AudibleFile.Read (data).IsSuccess);
	}

	[TestMethod]
	public void Read_RandomBytesAfterTheMagic_NeverThrow ()
	{
		var random = new Random (110);
		for (var i = 0; i < 2000; i++) {
			var data = AudibleTestBuilder.Create ();
			for (var flips = 0; flips < 8; flips++)
				data[8 + random.Next (data.Length - 8)] = (byte)random.Next (256);

			var result = AudibleFile.Read (data);
			if (!result.IsSuccess)
				Assert.IsNotNull (result.Error);
		}
	}

	[TestMethod]
	public void IsValidFormat_ChecksTheMagicAtOffset4 ()
	{
		Assert.IsTrue (AudibleFile.IsValidFormat (AudibleTestBuilder.Create ()));
		Assert.IsFalse (AudibleFile.IsValidFormat (new byte[] { 0, 0, 0, 0, 0x57, 0x90, 0x75 }));
		Assert.IsFalse (AudibleFile.IsValidFormat (new byte[8]));
	}

	[TestMethod]
	public void TryRead_ReportsSuccessAndFailure ()
	{
		Assert.IsTrue (AudibleFile.TryRead (AudibleTestBuilder.Create (), out var file));
		Assert.IsNotNull (file);
		Assert.IsFalse (AudibleFile.TryRead (new byte[3], out var none));
		Assert.IsNull (none);
	}

	[TestMethod]
	public void ReadResult_Equality ()
	{
		var failure = AudibleFileReadResult.Failure ("x");
		Assert.AreEqual (failure, AudibleFileReadResult.Failure ("x"));
		Assert.IsTrue (failure == AudibleFileReadResult.Failure ("x"));
		Assert.IsTrue (failure != AudibleFileReadResult.Failure ("y"));
		Assert.IsFalse (failure.Equals ((object)"x"));
		Assert.AreEqual (failure.GetHashCode (), AudibleFileReadResult.Failure ("x").GetHashCode ());
	}

	[TestMethod]
	public void ReadFromFile_ReadsAndRemembersThePath ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create ());

		var result = AudibleFile.ReadFromFile ("/book.aa", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("/book.aa", result.File!.SourcePath);
		Assert.AreEqual ("Ned Narrator", result.File.Tag.Narrator);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ReadsAndRemembersThePath ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ()));

		var result = await AudibleFile.ReadFromFileAsync ("/book.aa", fs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("/book.aa", result.File!.SourcePath);
		Assert.HasCount (1, result.File.Tag.Pictures);
	}

	[TestMethod]
	public async Task ReadFromFile_MissingOrDenied_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/denied.aa", AudibleTestBuilder.Create ());
		fs.MarkInaccessible ("/denied.aa");

		Assert.IsFalse (AudibleFile.ReadFromFile ("/missing.aa", fs).IsSuccess);
		Assert.IsFalse ((await AudibleFile.ReadFromFileAsync ("/missing.aa", fs)).IsSuccess);
		StringAssert.Contains (AudibleFile.ReadFromFile ("/denied.aa", fs).Error, "Access denied");
		StringAssert.Contains ((await AudibleFile.ReadFromFileAsync ("/denied.aa", fs)).Error, "Access denied");
	}

	[TestMethod]
	public async Task ReadFromFileAsync_Cancelled_Fails ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", AudibleTestBuilder.Create ());
		using var cts = new CancellationTokenSource ();
		await cts.CancelAsync ();

		var result = await AudibleFile.ReadFromFileAsync ("/book.aa", fs, cts.Token);

		Assert.IsFalse (result.IsSuccess);
		StringAssert.Contains (result.Error, "cancelled");
	}

	[TestMethod]
	public void MediaFile_DetectsByMagic_WithAnyName ()
	{
		var data = AudibleTestBuilder.Create ();

		Assert.AreEqual (MediaFormat.Audible, MediaFile.DetectFormat (data));
		var result = MediaFile.ReadFromData (data, "book.bin");
		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Audible, result.Format);
		Assert.IsInstanceOfType<AudibleFile> (result.File);
		Assert.AreEqual ("Ned Narrator", ((AudibleTag)result.Tag!).Narrator);
	}

	[TestMethod]
	public void MediaFile_ExtensionFallback_AaIsAudibleAndAaxIsMp4 ()
	{
		Assert.AreEqual (MediaFormat.Audible, MediaFile.DetectFormat (new byte[] { 1, 2, 3 }, "book.aa"));
		Assert.AreEqual (MediaFormat.Audible, MediaFile.DetectFormat (new byte[] { 1, 2, 3 }, "BOOK.AA"));
		Assert.AreEqual (MediaFormat.Mp4, MediaFile.DetectFormat (new byte[] { 1, 2, 3 }, "book.aax"));
		Assert.IsFalse (MediaFile.ReadFromData (new byte[] { 1, 2, 3 }, "book.aa").IsSuccess);
	}

	[TestMethod]
	public void MediaFile_Mp4WithAaxName_StaysMp4 ()
	{
		var mp4 = TestBuilders.Mp4.CreateWithMetadata ("Book");

		Assert.AreEqual (MediaFormat.Mp4, MediaFile.DetectFormat (mp4, "book.aa"));
		Assert.AreEqual (MediaFormat.Mp4, MediaFile.ReadFromData (mp4, "book.aax").Format);
	}

	[TestMethod]
	public async Task MediaFile_PathAndStreamReads_MatchTheByteRead ()
	{
		var data = AudibleTestBuilder.Create (cover: AudibleTestBuilder.Jpeg ());
		var fs = new MockFileSystem ();
		fs.AddFile ("/book.aa", data);

		var path = MediaFile.Read ("/book.aa", fs);
		var pathAsync = await MediaFile.ReadAsync ("/book.aa", fs);
		var stream = MediaFile.Read (new MemoryStream (data));
		var streamAsync = await MediaFile.ReadAsync (new MemoryStream (data));
		using var unseekable = new NonSeekableStream (data);
		var piped = MediaFile.Read (unseekable);

		foreach (var result in new[] { path, pathAsync, stream, streamAsync, piped }) {
			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.AreEqual (MediaFormat.Audible, result.Format);
			var file = (AudibleFile)result.File!;
			Assert.AreEqual ("A Test Book (Unabridged)", file.Tag.Title);
			Assert.HasCount (1, file.Tag.Pictures);
			Assert.AreEqual (TimeSpan.FromSeconds (8), file.Properties.Duration);
		}
		Assert.AreEqual ("/book.aa", ((AudibleFile)path.File!).SourcePath);
		Assert.AreEqual ("/book.aa", ((AudibleFile)pathAsync.File!).SourcePath);
	}

	[TestMethod]
	public void Dispose_ThenRender_Throws ()
	{
		var data = AudibleTestBuilder.Create ();
		var file = AudibleFile.Read (data).File!;
		file.Dispose ();
		file.Dispose ();

		Assert.ThrowsExactly<ObjectDisposedException> (() => file.Render (data));
		Assert.ThrowsExactly<ObjectDisposedException> (() => file.SaveToFile ("/x.aa", new MockFileSystem ()));
		Assert.ThrowsExactly<ObjectDisposedException> (() => file.SaveToFile (new MockFileSystem ()));
		Assert.ThrowsExactly<ObjectDisposedException> (() => file.SaveToFile ("/x.aa", data, new MockFileSystem ()));
	}

	[TestMethod]
	public async Task Dispose_ThenSaveAsync_Throws ()
	{
		var data = AudibleTestBuilder.Create ();
		var file = AudibleFile.Read (data).File!;
		file.Dispose ();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException> (() => file.SaveToFileAsync ("/x.aa", new MockFileSystem ()));
		await Assert.ThrowsExactlyAsync<ObjectDisposedException> (() => file.SaveToFileAsync (new MockFileSystem ()));
		await Assert.ThrowsExactlyAsync<ObjectDisposedException> (() => file.SaveToFileAsync ("/x.aa", data, new MockFileSystem ()));
	}

	sealed class NonSeekableStream (byte[] data) : Stream
	{
		readonly MemoryStream _inner = new (data);
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException ();
		public override long Position { get => throw new NotSupportedException (); set => throw new NotSupportedException (); }
		public override void Flush () { }
		public override int Read (byte[] buffer, int offset, int count) => _inner.Read (buffer, offset, count);
		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}
}
