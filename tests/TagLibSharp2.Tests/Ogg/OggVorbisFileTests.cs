// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Ogg;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ogg")]
public class OggVorbisFileTests
{
	[TestMethod]
	public void Read_ValidOggVorbis_ParsesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test Title", "Test Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Test Title", result.File.VorbisComment.Title);
		Assert.AreEqual ("Test Artist", result.File.VorbisComment.Artist);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[100]; // All zeros, no valid Ogg pages

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("Ogg", result.Error!);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x4F, 0x67, 0x67, 0x53 }; // Just "OggS"

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_NotVorbisCodec_ReturnsFailure ()
	{
		var data = TestBuilders.Ogg.CreatePageWithNonVorbisData ();

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("Vorbis", result.Error!);
	}

	[TestMethod]
	public void Title_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("My Song", "", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.AreEqual ("My Song", result.File!.Title);
	}

	[TestMethod]
	public void Title_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Title = "New Title";

		Assert.AreEqual ("New Title", file.Title);
		Assert.IsNotNull (file.VorbisComment);
	}

	[TestMethod]
	public void Artist_DelegatesToVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "Test Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.AreEqual ("Test Artist", result.File!.Artist);
	}

	[TestMethod]
	public void Read_MultiSegmentCommentWithUnsetFramingBit_KeepsTitle ()
	{
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2);
		comment.Title = "Loom";
		comment.SetValue ("PADDING", new string ('x', 300));
		var packet = TestBuilders.Ogg.CreateCommentPacket (comment, validFramingBit: false);
		Assert.IsGreaterThan (255, packet.Length);
		Assert.AreEqual ((byte)0, packet[^1]);

		using var builder = new BinaryDataBuilder ();
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateIdentificationPacket (),
			0,
			OggPageFlags.BeginOfStream,
			calculateCrc: false));
		builder.Add (TestBuilders.Ogg.CreatePage (packet, 1, OggPageFlags.None, calculateCrc: false));
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateSetupPacket (),
			2,
			OggPageFlags.EndOfStream,
			calculateCrc: false));

		var result = OggVorbisFile.Read (builder.ToArray ());

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Loom", result.File!.Title);
	}

	[TestMethod]
	public void Read_CommentAcrossPagesWithoutContinuation_KeepsTitle ()
	{
		// One page holds 255 segments of 255 bytes. Later pages leave the continuation flag unset.
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2);
		comment.Title = "Loom";
		// Two full pages of 255-byte segments, then a tail. The middle page must not drop the title.
		comment.SetValue ("PADDING", new string ('x', 140_000));
		var packet = TestBuilders.Ogg.CreateCommentPacket (comment, validFramingBit: false);
		Assert.IsGreaterThan (255 * 255 * 2, packet.Length);
		Assert.AreEqual ((byte)0, packet[^1]);

		var (commentPages, nextSequence) = OggPageHelper.BuildMultiPagePacket (
			packet,
			OggPageFlags.None,
			0,
			serialNumber: 1,
			startSequence: 1);
		Assert.IsGreaterThan (2, commentPages.Length);
		for (var i = 1; i < commentPages.Length; i++) {
			ClearContinuationFlag (commentPages[i]);
			Assert.AreEqual ((byte)0, commentPages[i][5]);
		}

		var setup = OggPageHelper.BuildOggPage (
			new[] { TestBuilders.Ogg.CreateSetupPacket () },
			OggPageFlags.None,
			0,
			1,
			nextSequence);
		var audio = OggPageHelper.BuildOggPage (
			new[] { new byte[64] },
			OggPageFlags.EndOfStream,
			44100,
			1,
			nextSequence + 1);

		using var builder = new BinaryDataBuilder ();
		builder.Add (OggPageHelper.BuildOggPage (
			new[] { TestBuilders.Ogg.CreateIdentificationPacket () },
			OggPageFlags.BeginOfStream,
			0,
			1,
			0));
		foreach (var page in commentPages)
			builder.Add (page);
		builder.Add (setup);
		var headersLength = builder.Length;
		builder.Add (audio);
		var file = builder.ToArray ();

		var headers = OggWindow.ReadHeaders (new MemoryStream (file), OggHeaderKind.Vorbis);
		Assert.IsTrue (headers.Length <= headersLength, $"header window copied audio ({headers.Length} > {headersLength})");
		Assert.IsTrue (headers.Length < file.Length);

		var fromBytes = OggVorbisFile.Read (file);
		Assert.IsTrue (fromBytes.IsSuccess, fromBytes.Error);
		Assert.AreEqual ("Loom", fromBytes.File!.Title);

		var fromStream = OggVorbisFile.Read (new MemoryStream (file), null, null);
		Assert.IsTrue (fromStream.IsSuccess, fromStream.Error);
		Assert.AreEqual ("Loom", fromStream.File!.Title);

		var fromMedia = MediaFile.Read (new MemoryStream (file), "song.ogg");
		Assert.IsTrue (fromMedia.IsSuccess, fromMedia.Error);
		Assert.AreEqual ("Loom", fromMedia.Tag!.Title);
	}

	[TestMethod]
	public void Read_TruncatedLastPage_UsesItsGranule ()
	{
		const ulong previousGranule = 44100;
		const ulong truncatedGranule = 441000;
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2);
		comment.Title = "Loom";

		var previous = OggPageHelper.BuildOggPage (
			new[] { new byte[] { 0x20, 0x00, 0x00, 0x00 } },
			OggPageFlags.None,
			previousGranule,
			1,
			3);
		var last = OggPageHelper.BuildOggPage (
			new[] { new byte[40] },
			OggPageFlags.EndOfStream,
			truncatedGranule,
			1,
			4);
		// Segment table stays so the granule is readable. The body does not.
		var truncated = last.AsSpan (0, 32).ToArray ();
		Assert.IsTrue (truncated.Length < last.Length);

		using var builder = new BinaryDataBuilder ();
		builder.Add (OggPageHelper.BuildOggPage (
			new[] { TestBuilders.Ogg.CreateIdentificationPacket () },
			OggPageFlags.BeginOfStream,
			0,
			1,
			0));
		builder.Add (OggPageHelper.BuildOggPage (
			new[] { TestBuilders.Ogg.CreateCommentPacket (comment) },
			OggPageFlags.None,
			0,
			1,
			1));
		builder.Add (OggPageHelper.BuildOggPage (
			new[] { TestBuilders.Ogg.CreateSetupPacket () },
			OggPageFlags.None,
			0,
			1,
			2));
		builder.Add (previous);
		builder.Add (truncated);
		builder.Add (0x11, 0x22, 0x33);
		var file = builder.ToArray ();

		var fromStream = OggVorbisFile.Read (new MemoryStream (file), null, null);
		Assert.IsTrue (fromStream.IsSuccess, fromStream.Error);
		Assert.AreEqual (TimeSpan.FromSeconds (10), fromStream.File!.Properties.Duration);

		var fromMedia = MediaFile.Read (new MemoryStream (file), "song.ogg");
		Assert.IsTrue (fromMedia.IsSuccess, fromMedia.Error);
		var duration = ((IMediaFile)fromMedia.File!).AudioProperties!.Duration;
		Assert.AreEqual (TimeSpan.FromSeconds (10), duration);
		Assert.AreNotEqual (TimeSpan.FromSeconds (1), duration);
		Assert.AreNotEqual (TimeSpan.Zero, duration);
	}

	[TestMethod]
	public void Read_FramingByteThenZeroPadding_KeepsTitleWithoutPadding ()
	{
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2);
		comment.Title = "Loom";
		comment.Artist = "Yellow";
		var packet = TestBuilders.Ogg.CreateCommentPacket (comment, validFramingBit: true);
		var padded = new byte[packet.Length + 16];
		packet.CopyTo (padded, 0);
		Assert.AreEqual ((byte)1, padded[packet.Length - 1]);
		Assert.AreEqual ((byte)0, padded[^1]);

		var result = OggVorbisFile.Read (CreateFileWithCommentPacket (padded));

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Loom", result.File!.Title);
		Assert.AreEqual (4, result.File.Title!.Length);
		Assert.AreEqual ("Yellow", result.File.Artist);
		Assert.AreEqual (6, result.File.Artist!.Length);
	}

	[TestMethod]
	public void Read_FieldLengthClaimsFramingByteBeforePadding_ReturnsFailure ()
	{
		// The length covers the 0x01 framing byte. Zero padding follows, so dropping
		// the final byte still leaves that 0x01 inside the artist.
		var artist = "ARTIST=Yellow"u8;
		using var comment = new BinaryDataBuilder ();
		comment.Add (TestConstants.Ogg.PacketTypeComment);
		comment.Add (TestConstants.Magic.Vorbis);
		comment.AddUInt32LE (1);
		comment.Add ((byte)'X');
		comment.AddUInt32LE (1);
		comment.AddUInt32LE ((uint)(artist.Length + 1));
		comment.Add (artist);
		comment.Add ((byte)1);
		comment.Add (0, 0, 0);

		var result = OggVorbisFile.Read (CreateFileWithCommentPacket (comment.ToArray ()));

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("framing", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_UnsetFramingBit_KeepsCommentsAndAudioProperties ()
	{
		var data = TestBuilders.Ogg.CreateFileWithInvalidFramingBit ();
		var packets = OggPageHelper.ExtractHeaderPackets (data, maxPackets: 2);
		Assert.IsTrue (packets.IsSuccess, packets.Error);
		Assert.AreEqual ((byte)0, packets.Packets[1][^1]);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Loom", result.File!.Title);
		Assert.AreEqual (44100, result.File.Properties.SampleRate);
		Assert.AreEqual (2, result.File.Properties.Channels);
		Assert.AreEqual (128, result.File.Properties.Bitrate);
		Assert.AreEqual ("Vorbis", result.File.Properties.Codec);
		Assert.AreEqual (TimeSpan.Zero, result.File.Properties.Duration);
	}

	[TestMethod]
	public void Save_UnsetFramingBit_WritesFramingBitOneAndKeepsTitle ()
	{
		var data = TestBuilders.Ogg.CreateFileWithInvalidFramingBit ();
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ogg", data);

		var readResult = OggVorbisFile.ReadFromFile ("/test.ogg", mockFs);
		Assert.IsTrue (readResult.IsSuccess, readResult.Error);
		Assert.AreEqual ("Loom", readResult.File!.Title);

		var saveResult = readResult.File.SaveToFile ("/output.ogg", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		var written = mockFs.ReadAllBytes ("/output.ogg");
		var packets = OggPageHelper.ExtractHeaderPackets (written, maxPackets: 2);
		Assert.IsTrue (packets.IsSuccess, packets.Error);
		Assert.AreEqual (TestConstants.Ogg.PacketTypeComment, packets.Packets[1][0]);
		Assert.AreEqual ((byte)1, packets.Packets[1][^1]);

		var verify = OggVorbisFile.Read (written);
		Assert.IsTrue (verify.IsSuccess, verify.Error);
		Assert.AreEqual ("Loom", verify.File!.Title);
	}

	[TestMethod]
	public void Read_CommentLengthPastFramingByte_ReturnsFailure ()
	{
		// Field length claims the framing byte. That byte is not comment data.
		using var comment = new BinaryDataBuilder ();
		comment.Add (TestConstants.Ogg.PacketTypeComment);
		comment.Add (TestConstants.Magic.Vorbis);
		comment.AddUInt32LE (1);
		comment.Add ((byte)'X');
		comment.AddUInt32LE (1);
		comment.AddUInt32LE (8);
		comment.Add ("TITLE=Z"u8);
		comment.Add ((byte)0);

		var result = OggVorbisFile.Read (CreateFileWithCommentPacket (comment.ToArray ()));

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("length", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_CommentPacketTooShortForFramingByte_ReturnsFailure ()
	{
		var packet = new byte[TestConstants.Magic.Vorbis.Length + 1];
		packet[0] = TestConstants.Ogg.PacketTypeComment;
		TestConstants.Magic.Vorbis.CopyTo (packet, 1);

		var result = OggVorbisFile.Read (CreateFileWithCommentPacket (packet));

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("framing", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	[DataRow (1u)]
	[DataRow (2u)]
	[DataRow (0xFFFFFFFFu)]
	public void Read_InvalidVorbisVersion_ReturnsFailure (uint version)
	{
		// Per Vorbis I spec, vorbis_version must be 0
		var data = TestBuilders.Ogg.CreateFileWithInvalidVorbisVersion (version);

		var result = OggVorbisFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_ValidFramingBit_Succeeds ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Title", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
	}

	[TestMethod]
	public void Read_LargeCommentSpanningMultiplePages_ParsesCorrectly ()
	{
		var data = TestBuilders.Ogg.CreateFileWithMultiPageComment ();

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.VorbisComment);
		Assert.AreEqual ("Multi-Page Title", result.File.VorbisComment.Title);
		var longValue = result.File.VorbisComment.GetValue ("LONGFIELD");
		Assert.IsNotNull (longValue);
		Assert.IsGreaterThan (60000, longValue!.Length);
	}

	[TestMethod]
	[DataRow ("日本語タイトル 🎵", "Художник")]
	[DataRow ("Café", "Naïve")]
	[DataRow ("", "   ")]
	public void Read_Utf8EdgeCases_ParsesCorrectly (string title, string artist)
	{
		var data = TestBuilders.Ogg.CreateMinimalFile (title, artist, calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		if (string.IsNullOrEmpty (title))
			Assert.IsNull (result.File!.Title);
		else
			Assert.AreEqual (title, result.File!.Title);
		Assert.AreEqual (artist, result.File!.Artist);
	}

	[TestMethod]
	public void Properties_ParsesSampleRateFromIdentificationHeader ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (44100, result.File!.Properties.SampleRate);
	}

	[TestMethod]
	public void Properties_ParsesChannelsFromIdentificationHeader ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Properties.Channels);
	}

	[TestMethod]
	public void Properties_ParsesBitrateFromIdentificationHeader ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (128, result.File!.Properties.Bitrate);
	}

	[TestMethod]
	public void Properties_CodecIsVorbis ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Vorbis", result.File!.Properties.Codec);
	}

	[TestMethod]
	public void Properties_BitsPerSampleIsZero ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.Properties.BitsPerSample);
	}

	[TestMethod]
	[DataRow (48000, 1, 192000, 192)]
	[DataRow (96000, 2, 320000, 320)]
	[DataRow (44100, 6, 128000, 128)]
	public void Properties_CustomValues_ParsesCorrectly (int sampleRate, int channels, int bitrateNominal, int expectedBitrateKbps)
	{
		var data = TestBuilders.Ogg.CreateFileWithProperties (sampleRate, channels, bitrateNominal, calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (sampleRate, result.File!.Properties.SampleRate);
		Assert.AreEqual (channels, result.File.Properties.Channels);
		Assert.AreEqual (expectedBitrateKbps, result.File.Properties.Bitrate);
	}

	[TestMethod]
	public void Read_WithValidateCrcFalse_AcceptsBadCrc ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
	}

	[TestMethod]
	public void Read_WithValidateCrcTrue_RejectsBadCrc ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);

		var result = OggVorbisFile.Read (data, validateCrc: true);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
		Assert.Contains ("CRC", result.Error!, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void Read_WithValidateCrcTrue_AcceptsValidCrc ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: true);

		var result = OggVorbisFile.Read (data, validateCrc: true);

		Assert.IsTrue (result.IsSuccess, $"Expected success but got: {result.Error}");
		Assert.IsNotNull (result.File);
		Assert.AreEqual ("Test", result.File!.Title);
	}

	// ═══════════════════════════════════════════════════════════════
	// Tag Setter Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Album_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Album = "New Album";

		Assert.AreEqual ("New Album", file.Album);
		Assert.AreEqual ("New Album", file.VorbisComment?.Album);
	}

	[TestMethod]
	public void Year_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Year = "2025";

		Assert.AreEqual ("2025", file.Year);
	}

	[TestMethod]
	public void Track_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Track = 5;

		Assert.AreEqual (5u, file.Track);
	}

	[TestMethod]
	public void Genre_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Genre = "Rock";

		Assert.AreEqual ("Rock", file.Genre);
	}

	[TestMethod]
	public void Comment_Set_UpdatesVorbisComment ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		var file = result.File!;

		file.Comment = "Test comment";

		Assert.AreEqual ("Test comment", file.Comment);
	}

	// ═══════════════════════════════════════════════════════════════
	// File I/O Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void ReadFromFile_ValidFile_ReturnsSuccess ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Test", "Artist", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ogg", data);

		var result = OggVorbisFile.ReadFromFile ("/test.ogg", mockFs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Test", result.File!.Title);
	}

	[TestMethod]
	public void ReadFromFile_SetsSourcePath ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.ogg", data);

		var result = OggVorbisFile.ReadFromFile ("/music/song.ogg", mockFs);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("/music/song.ogg", result.File!.SourcePath);
	}

	[TestMethod]
	public async Task ReadFromFileAsync_ValidFile_ReturnsSuccess ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Async Test", "Artist", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ogg", data);

		var result = await OggVorbisFile.ReadFromFileAsync ("/test.ogg", mockFs);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Async Test", result.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_ModifyAndSave_PreservesChanges ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Original", "Artist", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ogg", data);

		var readResult = OggVorbisFile.ReadFromFile ("/test.ogg", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Title = "Modified Title";

		var saveResult = file.SaveToFile ("/output.ogg", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Re-read and verify
		var verifyResult = OggVorbisFile.ReadFromFile ("/output.ogg", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("Modified Title", verifyResult.File!.Title);
	}

	[TestMethod]
	public void SaveToFile_WithoutPath_UsesSourcePath ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Original", "Artist", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/music/song.ogg", data);

		var readResult = OggVorbisFile.ReadFromFile ("/music/song.ogg", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Title = "Updated";

		var saveResult = file.SaveToFile (mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Verify saved to original path
		var verifyResult = OggVorbisFile.ReadFromFile ("/music/song.ogg", mockFs);
		Assert.AreEqual ("Updated", verifyResult.File!.Title);
	}

	[TestMethod]
	public async Task SaveToFileAsync_PreservesChanges ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Original", "Artist", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ogg", data);

		var readResult = await OggVorbisFile.ReadFromFileAsync ("/test.ogg", mockFs);
		Assert.IsTrue (readResult.IsSuccess);

		var file = readResult.File!;
		file.Title = "Async Modified";

		var saveResult = await file.SaveToFileAsync ("/output.ogg", mockFs);
		Assert.IsTrue (saveResult.IsSuccess, saveResult.Error);

		// Re-read and verify
		var verifyResult = await OggVorbisFile.ReadFromFileAsync ("/output.ogg", mockFs);
		Assert.IsTrue (verifyResult.IsSuccess);
		Assert.AreEqual ("Async Modified", verifyResult.File!.Title);
	}

	// ═══════════════════════════════════════════════════════════════
	// Render Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_ModifiedMetadata_ProducesValidFile ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("Original", "Artist", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.Title = "Modified";
		file.Album = "Test Album";

		var rendered = file.Render (data);

		Assert.IsNotNull (rendered);
		Assert.IsTrue (rendered.Length > 0);

		// Re-parse and verify
		var reparsed = OggVorbisFile.Read (rendered.ToArray ());
		Assert.IsTrue (reparsed.IsSuccess, reparsed.Error);
		Assert.AreEqual ("Modified", reparsed.File!.Title);
		Assert.AreEqual ("Test Album", reparsed.File.Album);
	}

	[TestMethod]
	public void Render_MultipleFields_PreservesAll ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.Title = "Test Title";
		file.Artist = "Test Artist";
		file.Album = "Test Album";
		file.Year = "2025";
		file.Genre = "Rock";
		file.Track = 7;

		var rendered = file.Render (data);
		var reparsed = OggVorbisFile.Read (rendered.ToArray ());

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Test Title", reparsed.File!.Title);
		Assert.AreEqual ("Test Artist", reparsed.File.Artist);
		Assert.AreEqual ("Test Album", reparsed.File.Album);
		Assert.AreEqual ("2025", reparsed.File.Year);
		Assert.AreEqual ("Rock", reparsed.File.Genre);
		Assert.AreEqual (7u, reparsed.File.Track);
	}

	// ═══════════════════════════════════════════════════════════════
	// Dispose Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Dispose_ClearsSourcePath ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var mockFs = new MockFileSystem ();
		mockFs.AddFile ("/test.ogg", data);

		var result = OggVorbisFile.ReadFromFile ("/test.ogg", mockFs);
		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File!.SourcePath);

		result.File.Dispose ();

		Assert.IsNull (result.File.SourcePath);
	}

	[TestMethod]
	public void Dispose_CalledTwice_DoesNotThrow ()
	{
		var data = TestBuilders.Ogg.CreateMinimalFile ("", "", calculateCrc: false);
		var result = OggVorbisFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		result.File!.Dispose ();
		result.File.Dispose (); // Should not throw
	}

	static void ClearContinuationFlag (byte[] page)
	{
		page[5] = 0;
		page[22] = 0;
		page[23] = 0;
		page[24] = 0;
		page[25] = 0;
		var crc = OggCrc.Calculate (page);
		page[22] = (byte)crc;
		page[23] = (byte)(crc >> 8);
		page[24] = (byte)(crc >> 16);
		page[25] = (byte)(crc >> 24);
	}

	static byte[] CreateFileWithCommentPacket (byte[] commentPacket)
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateIdentificationPacket (),
			0,
			OggPageFlags.BeginOfStream,
			calculateCrc: false));
		builder.Add (TestBuilders.Ogg.CreatePage (commentPacket, 1, OggPageFlags.None, calculateCrc: false));
		builder.Add (TestBuilders.Ogg.CreatePage (
			TestBuilders.Ogg.CreateSetupPacket (),
			2,
			OggPageFlags.EndOfStream,
			calculateCrc: false));
		return builder.ToArray ();
	}
}
