// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Ape;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ape")]
public class ApeTagTests
{
	// APE Tag magic bytes: "APETAGEX"
	private static readonly byte[] ApeMagic = "APETAGEX"u8.ToArray ();

	#region Header/Footer Structure Tests

	[TestMethod]
	public void ParseFooter_ValidFooter_ReturnsSuccess ()
	{
		// Arrange - minimal valid APEv2 footer (32 bytes)
		var footer = CreateValidFooter (version: 2000, tagSize: 32, itemCount: 0, flags: 0);

		// Act
		var result = ApeTagFooter.Parse (footer);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2000u, result.Footer!.Version);
		Assert.AreEqual (32u, result.Footer.TagSize);
		Assert.AreEqual (0u, result.Footer.ItemCount);
	}

	[TestMethod]
	public void ParseFooter_InvalidMagic_ReturnsFailure ()
	{
		// Arrange - invalid magic bytes
		var footer = new byte[32];
		"NOTAPETX"u8.CopyTo (footer);

		// Act
		var result = ApeTagFooter.Parse (footer);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("magic"));
	}

	[TestMethod]
	public void ParseFooter_TooShort_ReturnsFailure ()
	{
		// Arrange - data too short for footer
		var footer = new byte[20];

		// Act
		var result = ApeTagFooter.Parse (footer);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ParseFooter_Version1000_ParsesAsV1 ()
	{
		// Arrange - APEv1 footer
		var footer = CreateValidFooter (version: 1000, tagSize: 32, itemCount: 0, flags: 0);

		// Act
		var result = ApeTagFooter.Parse (footer);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1000u, result.Footer!.Version);
	}

	[TestMethod]
	public void ParseHeader_ValidHeader_ReturnsSuccess ()
	{
		// Arrange - valid APEv2 header with header flag set (bit 31)
		var flags = 0x80000000u; // Header present flag
		flags |= 0x20000000u; // This is a header (bit 29)
		var header = CreateValidFooter (version: 2000, tagSize: 64, itemCount: 1, flags: flags);

		// Act
		var result = ApeTagHeader.Parse (header);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header!.IsHeader);
		Assert.IsTrue (result.Header.HasHeader);
	}

	[TestMethod]
	public void Footer_FlagsDecoding_CorrectlyIdentifiesTextType ()
	{
		// Arrange - footer with text type flag (bits 2-1 = 0)
		var footer = CreateValidFooter (version: 2000, tagSize: 32, itemCount: 0, flags: 0);

		// Act
		var result = ApeTagFooter.Parse (footer);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.Footer!.IsReadOnly);
	}

	[TestMethod]
	public void Footer_FlagsDecoding_CorrectlyIdentifiesReadOnly ()
	{
		// Arrange - footer with read-only flag (bit 0 = 1)
		var footer = CreateValidFooter (version: 2000, tagSize: 32, itemCount: 0, flags: 1);

		// Act
		var result = ApeTagFooter.Parse (footer);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Footer!.IsReadOnly);
	}

	#endregion

	#region Item Parsing Tests

	[TestMethod]
	public void ParseItem_ValidTextItem_ReturnsSuccess ()
	{
		// Arrange - text item: "Title" = "Test Song"
		var item = CreateTextItem ("Title", "Test Song");

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Item!.Key);
		Assert.AreEqual ("Test Song", result.Item.ValueAsString);
		Assert.AreEqual (ApeItemType.Text, result.Item.ItemType);
	}

	[TestMethod]
	public void ParseItem_ValidBinaryItem_ReturnsSuccess ()
	{
		// Arrange - binary item with cover art pattern
		var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
		var item = CreateBinaryItem ("Cover Art (Front)", "cover.jpg", imageData);

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Cover Art (Front)", result.Item!.Key);
		Assert.AreEqual (ApeItemType.Binary, result.Item.ItemType);
		Assert.IsNotNull (result.Item.BinaryValue);
	}

	[TestMethod]
	public void ParseItem_EmptyKey_ReturnsFailure ()
	{
		// Arrange - item with empty key (invalid per spec: 2-255 chars)
		var item = CreateTextItem ("", "value");

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("key"));
	}

	[TestMethod]
	public void ParseItem_KeyTooLong_ReturnsFailure ()
	{
		// Arrange - key > 255 characters
		var longKey = new string ('A', 256);
		var item = CreateTextItem (longKey, "value");

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ParseItem_ReservedKey_ReturnsFailure ()
	{
		// Arrange - reserved key "ID3"
		var item = CreateTextItem ("ID3", "value");

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("reserved"));
	}

	[TestMethod]
	public void ParseItem_KeyWithControlCharacter_ReturnsFailure ()
	{
		// Arrange - key with control character (< 0x20)
		var item = CreateTextItem ("Key\x01Name", "value");

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ParseItem_ExternalLocator_ParsesCorrectly ()
	{
		// Arrange - external locator type (flags bits 2-1 = 2)
		var item = CreateExternalLocatorItem ("Lyrics", "http://example.com/lyrics.txt");

		// Act
		var result = ApeTagItem.Parse (item);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (ApeItemType.ExternalLocator, result.Item!.ItemType);
		Assert.AreEqual ("http://example.com/lyrics.txt", result.Item.ValueAsString);
	}

	#endregion

	#region Full Tag Parsing Tests

	[TestMethod]
	public void Parse_EmptyTag_ReturnsEmptyTag ()
	{
		// Arrange - tag with 0 items
		var tag = CreateCompleteTag (new Dictionary<string, string> ());

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.Tag!.ItemCount);
	}

	[TestMethod]
	public void Parse_SingleItem_ReturnsTagWithItem ()
	{
		// Arrange
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			["Title"] = "Test Song"
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Tag!.ItemCount);
		Assert.AreEqual ("Test Song", result.Tag.Title);
	}

	[TestMethod]
	public void Parse_MultipleItems_ReturnsAllItems ()
	{
		// Arrange
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			["Title"] = "Test Song",
			["Artist"] = "Test Artist",
			["Album"] = "Test Album",
			["Year"] = "2025",
			["Track"] = "1/10"
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (5, result.Tag!.ItemCount);
		Assert.AreEqual ("Test Song", result.Tag.Title);
		Assert.AreEqual ("Test Artist", result.Tag.Artist);
		Assert.AreEqual ("Test Album", result.Tag.Album);
		Assert.AreEqual ("2025", result.Tag.Year);
		Assert.AreEqual (1u, result.Tag.Track);
	}

	[TestMethod]
	public void Parse_CaseInsensitiveKeyLookup_FindsItem ()
	{
		// Arrange - keys stored as "Title" but queried as "title"
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			["Title"] = "Test Song"
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Song", result.Tag!.GetValue ("title")); // lowercase query
		Assert.AreEqual ("Test Song", result.Tag!.GetValue ("TITLE")); // uppercase query
	}

	[TestMethod]
	public void Parse_TrackWithTotal_ParsesBothParts ()
	{
		// Arrange - track format "3/12"
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			["Track"] = "3/12"
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3u, result.Tag!.Track);
		Assert.AreEqual (12u, result.Tag.TotalTracks);
	}

	[TestMethod]
	public void Parse_DiscWithTotal_ParsesBothParts ()
	{
		// Arrange - disc format "1/2"
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			["Disc"] = "1/2"
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1u, result.Tag!.Disc);
		Assert.AreEqual (2u, result.Tag.TotalDiscs);
	}

	#endregion

	#region Tag Rendering Tests

	[TestMethod]
	public void Render_EmptyTag_ProducesValidFooter ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act
		var rendered = tag.Render ();

		// Assert
		Assert.IsNotNull (rendered);
		Assert.IsTrue (rendered.Length >= 32); // At least footer size

		// Verify we can parse it back
		var parseResult = ApeTag.Parse (rendered.Span);
		Assert.IsTrue (parseResult.IsSuccess);
	}

	[TestMethod]
	public void Render_WithItems_ProducesValidTag ()
	{
		// Arrange
		var tag = new ApeTag {
			Title = "Test Song",
			Artist = "Test Artist"
		};

		// Act
		var rendered = tag.Render ();

		// Assert
		Assert.IsNotNull (rendered);

		// Round-trip validation
		var parseResult = ApeTag.Parse (rendered.Span);
		Assert.IsTrue (parseResult.IsSuccess);
		Assert.AreEqual ("Test Song", parseResult.Tag!.Title);
		Assert.AreEqual ("Test Artist", parseResult.Tag.Artist);
	}

	[TestMethod]
	public void Render_WithBinaryItem_PreservesBinaryData ()
	{
		// Arrange
		var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG magic
		var tag = new ApeTag ();
		tag.SetBinaryItem ("Cover Art (Front)", "cover.png", imageData);

		// Act
		var rendered = tag.Render ();

		// Assert - round-trip
		var parseResult = ApeTag.Parse (rendered.Span);
		Assert.IsTrue (parseResult.IsSuccess);
		var coverArt = parseResult.Tag!.GetBinaryItem ("Cover Art (Front)");
		Assert.IsNotNull (coverArt);
		CollectionAssert.AreEqual (imageData, coverArt.Data);
	}

	[TestMethod]
	public void Render_IncludesHeader_WhenRequested ()
	{
		// Arrange
		var tag = new ApeTag { Title = "Test" };

		// Act
		var rendered = tag.RenderWithOptions (includeHeader: true);

		// Assert
		Assert.IsTrue (rendered.Length >= 64); // Header (32) + items + Footer (32)

		// Verify header magic at start
		Assert.AreEqual ((byte)'A', rendered[0]);
		Assert.AreEqual ((byte)'P', rendered[1]);
		Assert.AreEqual ((byte)'E', rendered[2]);
		Assert.AreEqual ((byte)'T', rendered[3]);
	}

	#endregion

	#region Round-Trip Tests (Critical for data integrity)

	[TestMethod]
	public void RoundTrip_AllStandardFields_PreservesData ()
	{
		// Arrange - comprehensive tag with all standard fields
		var original = new ApeTag {
			Title = "Symphony No. 5",
			Artist = "Ludwig van Beethoven",
			Album = "Complete Symphonies",
			Year = "1808",
			Genre = "Classical",
			Comment = "First movement",
			Track = 5,
			TotalTracks = 9,
			Disc = 1,
			TotalDiscs = 3
		};

		// Act - render and parse
		var rendered = original.Render ();
		var parseResult = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parseResult.IsSuccess);
		var parsed = parseResult.Tag!;
		Assert.AreEqual (original.Title, parsed.Title);
		Assert.AreEqual (original.Artist, parsed.Artist);
		Assert.AreEqual (original.Album, parsed.Album);
		Assert.AreEqual (original.Year, parsed.Year);
		Assert.AreEqual (original.Genre, parsed.Genre);
		Assert.AreEqual (original.Comment, parsed.Comment);
		Assert.AreEqual (original.Track, parsed.Track);
		Assert.AreEqual (original.TotalTracks, parsed.TotalTracks);
		Assert.AreEqual (original.Disc, parsed.Disc);
		Assert.AreEqual (original.TotalDiscs, parsed.TotalDiscs);
	}

	[TestMethod]
	public void RoundTrip_UnicodeCharacters_PreservesData ()
	{
		// Arrange - Unicode in multiple scripts
		var original = new ApeTag {
			Title = "日本語タイトル",
			Artist = "Артист",
			Album = "مقطوعة موسيقية"
		};

		// Act
		var rendered = original.Render ();
		var parseResult = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parseResult.IsSuccess);
		Assert.AreEqual ("日本語タイトル", parseResult.Tag!.Title);
		Assert.AreEqual ("Артист", parseResult.Tag.Artist);
		Assert.AreEqual ("مقطوعة موسيقية", parseResult.Tag.Album);
	}

	[TestMethod]
	public void RoundTrip_ExtendedMetadata_PreservesData ()
	{
		// Arrange - MusicBrainz and ReplayGain
		var original = new ApeTag ();
		original.SetValue ("MUSICBRAINZ_TRACKID", "12345678-1234-1234-1234-123456789abc");
		original.SetValue ("REPLAYGAIN_TRACK_GAIN", "-6.5 dB");
		original.SetValue ("REPLAYGAIN_TRACK_PEAK", "0.987654");

		// Act
		var rendered = original.Render ();
		var parseResult = ApeTag.Parse (rendered.Span);

		// Assert
		Assert.IsTrue (parseResult.IsSuccess);
		Assert.AreEqual ("12345678-1234-1234-1234-123456789abc",
			parseResult.Tag!.GetValue ("MUSICBRAINZ_TRACKID"));
		Assert.AreEqual ("-6.5 dB", parseResult.Tag.GetValue ("REPLAYGAIN_TRACK_GAIN"));
	}

	#endregion

	#region Edge Cases and Security Tests

	[TestMethod]
	public void Parse_TruncatedData_ReturnsFailure ()
	{
		// Arrange - data claims 100 items but only has footer
		var footer = CreateValidFooter (version: 2000, tagSize: 1000, itemCount: 100, flags: 0);

		// Act
		var result = ApeTag.Parse (footer);

		// Assert
		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Parse_OverflowTagSize_ReturnsFailure ()
	{
		// Arrange - tag size larger than int.MaxValue
		var footer = CreateValidFooter (version: 2000, tagSize: uint.MaxValue, itemCount: 1, flags: 0);

		// Act
		var result = ApeTag.Parse (footer);

		// Assert
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("size") || result.Error.Contains ("overflow"));
	}

	[TestMethod]
	public void Parse_ZeroByteValue_HandlesCorrectly ()
	{
		// Arrange - item with empty value (0 bytes)
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			["EmptyField"] = ""
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Tag!.GetValue ("EmptyField"));
	}

	[TestMethod]
	public void Parse_MaxKeyLength_Succeeds ()
	{
		// Arrange - key at max length (255 chars)
		var maxKey = new string ('K', 255);
		var tag = CreateCompleteTag (new Dictionary<string, string> {
			[maxKey] = "value"
		});

		// Act
		var result = ApeTag.Parse (tag);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("value", result.Tag!.GetValue (maxKey));
	}

	[TestMethod]
	public void Parse_ItemCountMaxValueOnTinyTag_ReturnsFailureWithoutThrowing ()
	{
		// TagSize fits the footer alone; ItemCount cannot fit in those item bytes.
		var footer = CreateValidFooter (version: 2000, tagSize: 32, itemCount: uint.MaxValue, flags: 0);

		var result = ApeTag.Parse (footer);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.Tag);
		Assert.IsNotNull (result.Error);
		Assert.Contains (uint.MaxValue.ToString (CultureInfo.InvariantCulture), result.Error);
	}

	[TestMethod]
	public void Parse_ItemCountFittingInItemBytes_ReturnsFailureWithoutThrowing ()
	{
		// 40_000 items fit an 11-byte minimum (440 KB of 0xFF) but a dictionary of that count is over 1 MB.
		// The 256 capacity cap must keep Parse's allocation well under 1 MB. Do not build a multi-gigabyte buffer.
		const int itemCount = 40_000;
		const int minItemSize = ApeTagItem.MinHeaderSize + ApeTagItem.MinKeyLength + 1;
		const int itemBytes = itemCount * minItemSize;
		var data = new byte[itemBytes + ApeTagFooter.Size];
		Array.Fill (data, (byte)0xFF, 0, itemBytes);
		CreateValidFooter (version: 2000, tagSize: (uint)(itemBytes + ApeTagFooter.Size), itemCount: (uint)itemCount, flags: 0)
			.CopyTo (data, itemBytes);

		var before = GC.GetAllocatedBytesForCurrentThread ();
		var result = ApeTag.Parse (data);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.Tag);
		Assert.IsTrue (allocated < 1_000_000, $"Parse allocated {allocated} bytes");
	}

	[TestMethod]
	public void Parse_ItemCountFitsOneByteBudgetButNotMinimumItem_ReturnsFailure ()
	{
		// Two items fit a 1-byte-per-item budget in 20 bytes, but not an 11-byte minimum item.
		const int itemBytes = 20;
		const uint itemCount = 2;
		var data = new byte[itemBytes + ApeTagFooter.Size];
		Array.Fill (data, (byte)0xFF, 0, itemBytes);
		CreateValidFooter (version: 2000, tagSize: (uint)(itemBytes + ApeTagFooter.Size), itemCount: itemCount, flags: 0)
			.CopyTo (data, itemBytes);

		var result = ApeTag.Parse (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.Tag);
		Assert.IsNotNull (result.Error);
		Assert.Contains ($"APE item count ({itemCount})", result.Error);
	}

	[TestMethod]
	[DataRow (0x80000000u)]
	[DataRow (0xFFFFFFF0u)]
	[DataRow (0x7FFFFFFFu)]
	public void Parse_ValueSizeExceedsRemaining_ReturnsFailureWithoutThrowing (uint valueSize)
	{
		var result = ApeTag.Parse (CreateTagWithValueSize (valueSize));

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNull (result.Tag);
	}

	[TestMethod]
	public void Locate_Id3v1WithoutApeFooter_ReportsAudioEndBeforeTag ()
	{
		var audio = new byte[64];
		audio[0] = 1;
		var id3 = new byte[ApeTagLocation.Id3v1Size];
		"TAG"u8.CopyTo (id3);
		var data = new byte[audio.Length + id3.Length];
		audio.CopyTo (data, 0);
		id3.CopyTo (data, audio.Length);

		var location = ApeTagLocation.Locate (data);

		Assert.IsFalse (location.Found);
		Assert.AreEqual (audio.Length, location.AudioEnd);
		Assert.AreEqual (audio.Length, location.Id3v1Offset);
	}

	[TestMethod]
	public void Rewrite_HeaderOnlyBeforeId3v1_DoesNotDeleteAudio ()
	{
		// A 32-byte header (bit 29) before ID3v1 is not a footer. Honoring it as one deletes 32 audio bytes.
		var audio = new byte[48];
		audio[0] = 0xA1;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x11;

		var header = ApeTagHeader.Create (tagSize: ApeTagFooter.Size, itemCount: 0).Render ();
		var parsedHeader = ApeTagFooter.Parse (header);
		Assert.IsTrue (parsedHeader.IsSuccess, parsedHeader.Error);
		Assert.IsTrue (parsedHeader.Footer!.IsHeader);

		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + header.Length + id3.Length];
		audio.CopyTo (data, 0);
		header.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + header.Length);

		var location = ApeTagLocation.Locate (data);

		Assert.IsFalse (location.Found);
		Assert.AreEqual (data.Length - ApeTagLocation.Id3v1Size, location.AudioEnd);
		Assert.AreEqual (location.AudioEnd, location.Id3v1Offset);

		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (audio.Length, header.Length).SequenceEqual (header));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));
	}

	[TestMethod]
	public void Rewrite_HeaderOnlyAtEof_DoesNotDeleteAudio ()
	{
		// Same header flag at EOF is not a footer either. Locate must not eat the preceding 32 bytes.
		var audio = new byte[48];
		audio[0] = 0xA2;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x12;

		var header = ApeTagHeader.Create (tagSize: ApeTagFooter.Size, itemCount: 0).Render ();
		var data = new byte[audio.Length + header.Length];
		audio.CopyTo (data, 0);
		header.CopyTo (data, audio.Length);

		var location = ApeTagLocation.Locate (data);

		Assert.IsFalse (location.Found);
		Assert.AreEqual (data.Length, location.AudioEnd);

		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (audio.Length, header.Length).SequenceEqual (header));
	}

	[TestMethod]
	public void Rewrite_OverclaimedHeaderPointsAtAudio_DoesNotDeleteAudio ()
	{
		// Footer claims a header, but the 32 bytes at that start are audio, not APETAGEX.
		var audio = new byte[64];
		audio[0] = 0xB1;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x21;

		var footer = ApeTagFooter.Create (tagSize: ApeTagFooter.Size, itemCount: 0, hasHeader: true).Render ();
		var parsed = ApeTagFooter.Parse (footer);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.IsFalse (parsed.Footer!.IsHeader);
		Assert.IsTrue (parsed.Footer.HasHeader);

		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + footer.Length + id3.Length];
		audio.CopyTo (data, 0);
		footer.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + footer.Length);

		var headerStart = audio.Length - ApeTagFooter.Size;
		Assert.IsFalse (data.AsSpan (headerStart, 8).SequenceEqual ("APETAGEX"u8));

		var location = ApeTagLocation.Locate (data);

		Assert.IsTrue (location.Found);
		Assert.AreEqual (audio.Length, location.AudioEnd);
		Assert.AreEqual (audio.Length + footer.Length, location.Id3v1Offset);

		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));
	}

	[TestMethod]
	public void Rewrite_UnboundedFooterBeforeId3v1_WritesTagBeforeId3 ()
	{
		// A footer before ID3v1 whose size does not fit is not a tag. Keep ID3v1 and write in front of it.
		var audio = new byte[64];
		audio[0] = 0xC1;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x31;

		var footer = ApeTagFooter.Create (tagSize: 10_000, itemCount: 0).Render ();
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + footer.Length + id3.Length];
		audio.CopyTo (data, 0);
		footer.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + footer.Length);

		var location = ApeTagLocation.Locate (data);

		Assert.IsFalse (location.Found);
		Assert.AreEqual (data.Length - ApeTagLocation.Id3v1Size, location.AudioEnd);
		Assert.AreEqual (location.AudioEnd, location.Id3v1Offset);

		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (audio.Length, footer.Length).SequenceEqual (footer));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagFooter.Size - ApeTagLocation.Id3v1Size, 8).SequenceEqual ("APETAGEX"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));
		Assert.IsFalse (saved.AsSpan (saved.Length - 8, 8).SequenceEqual ("APETAGEX"u8));
	}

	[TestMethod]
	public void Parse_FollowedById3v1_ParsesApeItem ()
	{
		var tag = new ApeTag {
			Title = "Test Song"
		};
		var rendered = tag.Render ();

		var id3v1 = new byte[128];
		"TAG"u8.CopyTo (id3v1);

		var combined = new byte[rendered.Length + id3v1.Length];
		rendered.Span.CopyTo (combined);
		id3v1.CopyTo (combined, rendered.Length);

		var result = ApeTag.Parse (combined);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Tag!.ItemCount);
		Assert.AreEqual ("Test Song", result.Tag.Title);
	}

	[TestMethod]
	public void Parse_RealFooterAtEof_IgnoresPlantedEmptyFooterBeforeTag ()
	{
		// Real one-item tag at EOF. An empty valid footer sits at EOF-160 and "TAG" at EOF-128.
		// "APETAGEX" contains "TAG" at offset 3, not offset 0, so that marker is ID3v1 only at offset 0.
		var real = new ApeTag { Title = "Real Item" }.Render ().ToArray ();
		Assert.IsTrue (real.Length < 128);

		var emptyFooter = ApeTagFooter.Create (tagSize: 32, itemCount: 0).Render ();
		var beforeReal = new byte[128 - real.Length];
		"TAG"u8.CopyTo (beforeReal);

		var data = new byte[emptyFooter.Length + beforeReal.Length + real.Length];
		emptyFooter.CopyTo (data, 0);
		beforeReal.CopyTo (data, emptyFooter.Length);
		real.CopyTo (data, emptyFooter.Length + beforeReal.Length);

		Assert.IsTrue (data.AsSpan (data.Length - 160, 8).SequenceEqual ("APETAGEX"u8));
		Assert.IsTrue (data.AsSpan (data.Length - 128, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (data.AsSpan (data.Length - ApeTagFooter.Size, 8).SequenceEqual ("APETAGEX"u8));

		var result = ApeTag.Parse (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (1, result.Tag!.ItemCount);
		Assert.AreEqual ("Real Item", result.Tag.Title);
	}

	[TestMethod]
	public void Parse_Lyrics3BetweenApeAndId3v1_ReadsTitleAndSaveKeepsLyrics ()
	{
		// Audio, APE (Title=FromApe), minimal Lyrics3 v2, then ID3v1.
		// LYRICS200 sits immediately before TAG, so the APE footer is before LYRICSBEGIN, not at EOF-160.
		var audio = new byte[80];
		audio[0] = 0xD1;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x41;

		var ape = new ApeTag { Title = "FromApe" }.Render ().ToArray ();
		var lyrics = CreateMinimalLyrics3 ();
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + ape.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		ape.CopyTo (data, audio.Length);
		lyrics.CopyTo (data, audio.Length + ape.Length);
		id3.CopyTo (data, audio.Length + ape.Length + lyrics.Length);

		var parsed = ApeTag.Parse (data);

		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.AreEqual ("FromApe", parsed.Tag!.Title);

		parsed.Tag.Title = "New Title";
		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, parsed.Tag.Render ());

		Assert.AreEqual (1, ApeTagTestData.CountSequence (saved, "LYRICS200"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size - lyrics.Length, lyrics.Length).SequenceEqual (lyrics));
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");

		var reread = ApeTag.Parse (saved);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New Title", reread.Tag!.Title);
	}

	[TestMethod]
	public void Rewrite_Lyrics3AndId3v1WithoutApe_InsertsApeBeforeLyricsBegin ()
	{
		// Lyrics3 and ID3v1 with no APE. A new title is written before LYRICSBEGIN, and TAG stays last.
		var audio = new byte[80];
		audio[0] = 0xD2;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x42;

		var lyrics = CreateMinimalLyrics3 ();
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		lyrics.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + lyrics.Length);

		var parsed = ApeTag.Parse (data);
		Assert.IsFalse (parsed.IsSuccess);
		Assert.IsNull (parsed.Tag);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New Title" }.Render ());

		var lyricsAt = saved.AsSpan ().IndexOf ("LYRICSBEGIN"u8);
		var apeAt = saved.AsSpan ().IndexOf ("APETAGEX"u8);
		Assert.IsTrue (apeAt >= audio.Length, "APE tag replaced audio");
		Assert.IsTrue (lyricsAt > apeAt, "APE tag is not before LYRICSBEGIN");
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size - lyrics.Length, lyrics.Length).SequenceEqual (lyrics));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size).IndexOf ("APETAGEX"u8) < 0, "APE tag was written after TAG");
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.AreEqual (1, ApeTagTestData.CountSequence (saved, "LYRICS200"u8));

		var reread = ApeTag.Parse (saved);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New Title", reread.Tag!.Title);
	}

	[TestMethod]
	public void Rewrite_OverclaimedFooterWithoutHeaderAtEof_KeepsAudioPrefix ()
	{
		// Footer TagSize reaches into the audio and there is no header at that start.
		// The body is not an APE tag, so a save must not delete the over-claimed bytes.
		var audio = MarkerAudio (80, 0xE1);
		var data = BuildOverclaimedFooterFile (audio, lyrics: null, id3: null, claimedItemBytes: 48);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.Length >= audio.Length, "save shrank the file below the audio");
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
	}

	[TestMethod]
	public void Rewrite_OverclaimedFooterWithoutHeaderBeforeId3v1_KeepsAudioPrefix ()
	{
		var audio = MarkerAudio (80, 0xE2);
		var data = BuildOverclaimedFooterFile (audio, lyrics: null, id3: CreateId3v1Suffix (), claimedItemBytes: 48);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));
	}

	[TestMethod]
	public void Rewrite_OverclaimedFooterWithoutHeaderBeforeLyrics3_KeepsAudioPrefix ()
	{
		var audio = MarkerAudio (80, 0xE3);
		var lyrics = CreateMinimalLyrics3 ();
		var data = BuildOverclaimedFooterFile (audio, lyrics, CreateId3v1Suffix (), claimedItemBytes: 48);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size - lyrics.Length, lyrics.Length).SequenceEqual (lyrics));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
	}

	[TestMethod]
	public void Rewrite_Lyrics3v1BeforeId3v1_PreservesSpan ()
	{
		// v1 has no size field. The block is LYRICSBEGIN through LYRICSEND, immediately before TAG.
		var audio = MarkerAudio (80, 0xD4);
		var lyrics = CreateLyrics3V1 ("Line");
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		lyrics.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + lyrics.Length);

		var location = ApeTagLocation.Locate (data);
		Assert.AreEqual (audio.Length, location.AudioEnd);
		Assert.AreEqual (audio.Length, location.LyricsOffset);
		Assert.AreEqual (lyrics.Length, location.LyricsLength);

		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New Title" }.Render ());

		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size - lyrics.Length, lyrics.Length).SequenceEqual (lyrics));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size + 3, 6).SequenceEqual ("KeepMe"u8));

		var reread = ApeTag.Parse (saved);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New Title", reread.Tag!.Title);
	}

	[TestMethod]
	public void Rewrite_LyricsEndFartherThanSearchBound_DoesNotDeleteAudio ()
	{
		// LYRICSBEGIN more than 64 KB before LYRICSEND is not a v1 tag. Do not treat the span as lyrics.
		var audio = MarkerAudio (80, 0xD5);
		var gap = new byte[(64 * 1024) + 32];
		Array.Fill (gap, (byte)0x22);
		"LYRICSBEGIN"u8.CopyTo (gap);
		var end = "LYRICSEND"u8.ToArray ();
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + gap.Length + end.Length + id3.Length];
		audio.CopyTo (data, 0);
		gap.CopyTo (data, audio.Length);
		end.CopyTo (data, audio.Length + gap.Length);
		id3.CopyTo (data, audio.Length + gap.Length + end.Length);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.AreEqual (data.Length - ApeTagLocation.Id3v1Size, location.AudioEnd);
		Assert.AreEqual (-1, location.LyricsOffset);
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
	}

	[TestMethod]
	public void Rewrite_Lyrics3SizeMismatch_DoesNotDeleteAudio ()
	{
		// 000099 points 99 bytes before the size field, into the audio, and those bytes are not LYRICSBEGIN.
		// Trusting the size without that check would end the audio there.
		var audio = MarkerAudio (96, 0xD6);
		var lyrics = CreateMinimalLyrics3 ();
		"000099"u8.CopyTo (lyrics.AsSpan (11));
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		lyrics.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + lyrics.Length);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.AreEqual (data.Length - ApeTagLocation.Id3v1Size, location.AudioEnd);
		Assert.AreEqual (-1, location.LyricsOffset);
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
	}

	[TestMethod]
	public void Rewrite_Lyrics3SizeDoesNotFit_DoesNotDeleteAudio ()
	{
		// Lyrics3 v2: the six digits are the body size, including LYRICSBEGIN and excluding the digits and LYRICS200.
		// 999999 does not fit in front of those digits. Locate must not throw, and a save must keep the audio prefix.
		var audio = MarkerAudio (80, 0xD8);
		var lyrics = new byte[11 + 6 + 9];
		"LYRICSBEGIN"u8.CopyTo (lyrics);
		"999999"u8.CopyTo (lyrics.AsSpan (11));
		"LYRICS200"u8.CopyTo (lyrics.AsSpan (17));
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		lyrics.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + lyrics.Length);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.AreEqual (data.Length - ApeTagLocation.Id3v1Size, location.AudioEnd);
		Assert.AreEqual (-1, location.LyricsOffset);
		Assert.IsTrue (saved.Length >= audio.Length, "save shrank the file below the audio");
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
	}

	[TestMethod]
	public void Rewrite_BadFooterBeforeLyricsBegin_DoesNotDeleteAudio ()
	{
		// Reserved bytes are not zero, so this is not a footer. Its TagSize must not eat the audio in front of LYRICSBEGIN.
		var audio = MarkerAudio (80, 0xD7);
		var badFooter = ApeTagFooter.Create (tagSize: 200, itemCount: 1).Render ();
		badFooter[24] = 1;
		var lyrics = CreateMinimalLyrics3 ();
		var id3 = CreateId3v1Suffix ();
		var data = new byte[audio.Length + badFooter.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		badFooter.CopyTo (data, audio.Length);
		lyrics.CopyTo (data, audio.Length + badFooter.Length);
		id3.CopyTo (data, audio.Length + badFooter.Length + lyrics.Length);

		var location = ApeTagLocation.Locate (data);
		var saved = ApeTagLocation.Rewrite (data, location, new ApeTag { Title = "New" }.Render ());

		Assert.AreEqual (audio.Length + badFooter.Length, location.AudioEnd);
		Assert.IsTrue (saved.AsSpan (0, audio.Length).SequenceEqual (audio), "audio prefix was shortened");
		Assert.IsTrue (saved.AsSpan (audio.Length, badFooter.Length).SequenceEqual (badFooter));
		Assert.IsTrue (saved.AsSpan (saved.Length - ApeTagLocation.Id3v1Size - lyrics.Length, lyrics.Length).SequenceEqual (lyrics));
	}

	#endregion

	#region TagTypes Integration Tests

	[TestMethod]
	public void TagType_ReturnsApe ()
	{
		// Arrange
		var tag = new ApeTag ();

		// Act & Assert
		Assert.AreEqual (TagTypes.Ape, tag.TagType);
	}

	#endregion

	#region Test Helpers

	private static byte[] CreateTagWithValueSize (uint valueSize)
	{
		var key = "Title"u8;
		var item = new byte[8 + key.Length + 1];
		BitConverter.GetBytes (valueSize).CopyTo (item, 0);
		key.CopyTo (item.AsSpan (8));

		var footer = CreateValidFooter (version: 2000, tagSize: (uint)(item.Length + ApeTagFooter.Size), itemCount: 1, flags: 0);
		var data = new byte[item.Length + footer.Length];
		item.CopyTo (data, 0);
		footer.CopyTo (data, item.Length);
		return data;
	}

	private static byte[] MarkerAudio (int length, byte marker)
	{
		var audio = new byte[length];
		audio[0] = marker;
		for (var i = 1; i < audio.Length; i++)
			audio[i] = 0x51;

		return audio;
	}

	/// <summary>
	/// Audio, then a footer with no header flag whose TagSize claims <paramref name="claimedItemBytes"/>
	/// of the audio. Those bytes are not an APE item.
	/// </summary>
	private static byte[] BuildOverclaimedFooterFile (byte[] audio, byte[]? lyrics, byte[]? id3, int claimedItemBytes)
	{
		var footer = ApeTagFooter.Create (
			tagSize: (uint)(claimedItemBytes + ApeTagFooter.Size),
			itemCount: 1).Render ();
		var lyricsLength = lyrics?.Length ?? 0;
		var id3Length = id3?.Length ?? 0;
		var data = new byte[audio.Length + footer.Length + lyricsLength + id3Length];
		audio.CopyTo (data, 0);
		footer.CopyTo (data, audio.Length);
		if (lyrics is not null)
			lyrics.CopyTo (data, audio.Length + footer.Length);
		if (id3 is not null)
			id3.CopyTo (data, audio.Length + footer.Length + lyricsLength);
		return data;
	}

	private static byte[] CreateId3v1Suffix ()
	{
		var id3 = new byte[ApeTagLocation.Id3v1Size];
		"TAG"u8.CopyTo (id3);
		"KeepMe"u8.CopyTo (id3.AsSpan (3));
		return id3;
	}

	/// <summary>
	/// Lyrics3 v1 block: LYRICSBEGIN, the text, and LYRICSEND. There is no size field.
	/// </summary>
	private static byte[] CreateLyrics3V1 (string text)
	{
		var body = System.Text.Encoding.Latin1.GetBytes (text);
		var block = new byte[11 + body.Length + 9];
		"LYRICSBEGIN"u8.CopyTo (block);
		body.CopyTo (block.AsSpan (11));
		"LYRICSEND"u8.CopyTo (block.AsSpan (11 + body.Length));
		return block;
	}

	/// <summary>
	/// Minimal Lyrics3 v2 block: LYRICSBEGIN, a 6-digit size of that body, and LYRICS200.
	/// The size includes LYRICSBEGIN and excludes the size field and the end marker.
	/// </summary>
	private static byte[] CreateMinimalLyrics3 ()
	{
		ReadOnlySpan<byte> begin = "LYRICSBEGIN"u8;
		ReadOnlySpan<byte> end = "LYRICS200"u8;
		var block = new byte[begin.Length + 6 + end.Length];
		begin.CopyTo (block);
		var size = begin.Length;
		for (var i = 5; i >= 0; i--) {
			block[begin.Length + i] = (byte)('0' + (size % 10));
			size /= 10;
		}

		end.CopyTo (block.AsSpan (begin.Length + 6));
		return block;
	}

	private static byte[] CreateValidFooter (uint version, uint tagSize, uint itemCount, uint flags)
	{
		var footer = new byte[32];

		// Preamble: "APETAGEX"
		ApeMagic.CopyTo (footer, 0);

		// Version (little-endian)
		BitConverter.GetBytes (version).CopyTo (footer, 8);

		// Tag size (little-endian)
		BitConverter.GetBytes (tagSize).CopyTo (footer, 12);

		// Item count (little-endian)
		BitConverter.GetBytes (itemCount).CopyTo (footer, 16);

		// Flags (little-endian)
		BitConverter.GetBytes (flags).CopyTo (footer, 20);

		// Reserved (8 bytes of zero) - already zero from new byte[32]

		return footer;
	}

	private static byte[] CreateTextItem (string key, string value)
	{
		var keyBytes = System.Text.Encoding.ASCII.GetBytes (key);
		var valueBytes = System.Text.Encoding.UTF8.GetBytes (value);

		// Item structure: valueSize(4) + flags(4) + key + null + value
		var item = new byte[8 + keyBytes.Length + 1 + valueBytes.Length];

		// Value size (little-endian)
		BitConverter.GetBytes ((uint)valueBytes.Length).CopyTo (item, 0);

		// Flags = 0 (text type, read-write)
		// Already zero from new byte[]

		// Key
		keyBytes.CopyTo (item, 8);

		// Null terminator (already zero)

		// Value
		valueBytes.CopyTo (item, 8 + keyBytes.Length + 1);

		return item;
	}

	private static byte[] CreateBinaryItem (string key, string filename, byte[] data)
	{
		var keyBytes = System.Text.Encoding.ASCII.GetBytes (key);
		var filenameBytes = System.Text.Encoding.ASCII.GetBytes (filename);

		// Binary format: filename + null + binary data
		var valueLength = filenameBytes.Length + 1 + data.Length;

		var item = new byte[8 + keyBytes.Length + 1 + valueLength];

		// Value size
		BitConverter.GetBytes ((uint)valueLength).CopyTo (item, 0);

		// Flags = 2 (binary type, bits 2-1 = 1)
		BitConverter.GetBytes (2u).CopyTo (item, 4);

		// Key
		keyBytes.CopyTo (item, 8);

		// Key null terminator (already zero)

		// Value: filename + null + data
		filenameBytes.CopyTo (item, 8 + keyBytes.Length + 1);
		// Null separator already zero
		data.CopyTo (item, 8 + keyBytes.Length + 1 + filenameBytes.Length + 1);

		return item;
	}

	private static byte[] CreateExternalLocatorItem (string key, string url)
	{
		var keyBytes = System.Text.Encoding.ASCII.GetBytes (key);
		var valueBytes = System.Text.Encoding.UTF8.GetBytes (url);

		var item = new byte[8 + keyBytes.Length + 1 + valueBytes.Length];

		// Value size
		BitConverter.GetBytes ((uint)valueBytes.Length).CopyTo (item, 0);

		// Flags = 4 (external locator, bits 2-1 = 2)
		BitConverter.GetBytes (4u).CopyTo (item, 4);

		// Key
		keyBytes.CopyTo (item, 8);

		// Value
		valueBytes.CopyTo (item, 8 + keyBytes.Length + 1);

		return item;
	}

	private static byte[] CreateCompleteTag (Dictionary<string, string> items)
	{
		using var ms = new MemoryStream ();

		// Write items
		foreach (var kvp in items) {
			var item = CreateTextItem (kvp.Key, kvp.Value);
			ms.Write (item);
		}

		var itemsData = ms.ToArray ();

		// Calculate tag size (items + footer, excludes header for APEv2 compat)
		var tagSize = (uint)(itemsData.Length + 32);

		// Create footer
		var footer = CreateValidFooter (
			version: 2000,
			tagSize: tagSize,
			itemCount: (uint)items.Count,
			flags: 0);

		// Combine items + footer
		var result = new byte[itemsData.Length + 32];
		itemsData.CopyTo (result, 0);
		footer.CopyTo (result, itemsData.Length);

		return result;
	}

	#endregion

	#region ApeTagItem Coverage Tests

	[TestMethod]
	public void ApeTagItem_CreateText_WithReadOnly_SetsFlag ()
	{
		var item = ApeTagItem.CreateText ("Title", "Test", isReadOnly: true);
		Assert.IsTrue (item.IsReadOnly);
		Assert.AreEqual (ApeItemType.Text, item.ItemType);
	}

	[TestMethod]
	public void ApeTagItem_CreateBinary_WithReadOnly_SetsFlag ()
	{
		var item = ApeTagItem.CreateBinary ("Cover", "cover.jpg", [0xFF, 0xD8], isReadOnly: true);
		Assert.IsTrue (item.IsReadOnly);
		Assert.AreEqual (ApeItemType.Binary, item.ItemType);
	}

	[TestMethod]
	public void ApeTagItem_CreateExternalLocator_WithReadOnly_SetsFlag ()
	{
		var item = ApeTagItem.CreateExternalLocator ("Link", "http://example.com", isReadOnly: true);
		Assert.IsTrue (item.IsReadOnly);
		Assert.AreEqual (ApeItemType.ExternalLocator, item.ItemType);
	}

	[TestMethod]
	public void ApeTagItem_Render_RoundTrips ()
	{
		var original = ApeTagItem.CreateText ("Title", "Test Value");
		var rendered = original.Render ();
		var reparsed = ApeTagItem.Parse (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual ("Title", reparsed.Item!.Key);
		Assert.AreEqual ("Test Value", reparsed.Item.ValueAsString);
	}

	[TestMethod]
	public void ApeTagItem_BinaryValue_NoFilename_ReturnsDataOnly ()
	{
		// Create binary item where the value has no null separator (no filename)
		var keyBytes = "Cover"u8.ToArray ();
		var binaryData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var itemData = new byte[8 + keyBytes.Length + 1 + binaryData.Length];

		BitConverter.GetBytes ((uint)binaryData.Length).CopyTo (itemData, 0);
		BitConverter.GetBytes (2u).CopyTo (itemData, 4); // Flags = 2 (binary type)
		keyBytes.CopyTo (itemData, 8);
		binaryData.CopyTo (itemData, 8 + keyBytes.Length + 1);

		var result = ApeTagItem.Parse (itemData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (ApeItemType.Binary, result.Item!.ItemType);
		var bv = result.Item.BinaryValue;
		Assert.IsNotNull (bv);
		Assert.AreEqual ("", bv.Filename);
		CollectionAssert.AreEqual (binaryData, bv.Data);
	}

	[TestMethod]
	public void ValueAsString_TrailingNul_IsNotPartOfTheText ()
	{
		var item = CreateTextItem ("Title", "Tagged Monkey\0");
		var result = ApeTagItem.Parse (item);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Tagged Monkey", result.Item!.ValueAsString);
	}

	[TestMethod]
	public void ValueAsString_EmbeddedNul_JoinsTheValues ()
	{
		var item = CreateTextItem ("Artist", "Elvis\0& Friends");
		var result = ApeTagItem.Parse (item);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Elvis, & Friends", result.Item!.ValueAsString);
	}

	[TestMethod]
	public void ValueAsString_EmptyMiddleValue_IsNotABlankSlot ()
	{
		var item = CreateTextItem ("Artist", "Elvis\0\0Friends");
		var result = ApeTagItem.Parse (item);

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual ("Elvis, Friends", result.Item!.ValueAsString);
	}

	[TestMethod]
	public void ApeTagItem_ValueAsString_ForBinary_ReturnsNull ()
	{
		var item = ApeTagItem.CreateBinary ("Cover", "cover.jpg", [0x89, 0x50]);
		Assert.IsNull (item.ValueAsString);
	}

	[TestMethod]
	public void ApeTagItem_BinaryValue_ForText_ReturnsNull ()
	{
		var item = ApeTagItem.CreateText ("Title", "Test");
		Assert.IsNull (item.BinaryValue);
	}

	[TestMethod]
	public void ApeTagItem_Parse_SingleCharKey_Fails ()
	{
		var data = CreateTextItem ("X", "value");
		var result = ApeTagItem.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("short"));
	}

	[TestMethod]
	public void ApeTagItem_Parse_TwoCharKey_Succeeds ()
	{
		var data = CreateTextItem ("XY", "value");
		var result = ApeTagItem.Parse (data);
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("XY", result.Item!.Key);
	}

	[TestMethod]
	public void ApeTagItem_Parse_InvalidFlagsAreMasked ()
	{
		var keyBytes = "Title"u8.ToArray ();
		var valueBytes = "Test"u8.ToArray ();
		var data = new byte[8 + keyBytes.Length + 1 + valueBytes.Length];

		BitConverter.GetBytes ((uint)valueBytes.Length).CopyTo (data, 0);
		BitConverter.GetBytes (0xF8u).CopyTo (data, 4); // Invalid flag bits
		keyBytes.CopyTo (data, 8);
		valueBytes.CopyTo (data, 8 + keyBytes.Length + 1);

		var result = ApeTagItem.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (ApeItemType.Text, result.Item!.ItemType);
	}

	[TestMethod]
	public void ApeTagItem_Parse_ValueExtendsBeyondData_Fails ()
	{
		var keyBytes = "Title"u8.ToArray ();
		var data = new byte[8 + keyBytes.Length + 1 + 5];

		BitConverter.GetBytes (100u).CopyTo (data, 0); // Claim 100 bytes
		keyBytes.CopyTo (data, 8);

		var result = ApeTagItem.Parse (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("extends") || result.Error.Contains ("beyond"));
	}

	[TestMethod]
	public void ApeTagItem_CreateText_ShortKey_Throws ()
	{
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText ("X", "value"));
	}

	[TestMethod]
	public void ApeTagItem_CreateText_LongKey_Throws ()
	{
		var longKey = new string ('K', 256);
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText (longKey, "value"));
	}

	[TestMethod]
	public void ApeTagItem_CreateText_ReservedKey_Throws ()
	{
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText ("ID3", "value"));
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText ("TAG", "value"));
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText ("OggS", "value"));
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText ("MP+", "value"));
	}

	[TestMethod]
	public void ApeTagItem_CreateText_ControlCharInKey_Throws ()
	{
		Assert.ThrowsExactly<ArgumentException> (() =>
			ApeTagItem.CreateText ("Title\x01", "value"));
	}

	[TestMethod]
	public void ApeTagItemParseResult_OperatorEquals_Works ()
	{
		var failure1 = ApeTagItemParseResult.Failure ("Error A");
		var failure2 = ApeTagItemParseResult.Failure ("Error A");
		var failure3 = ApeTagItemParseResult.Failure ("Error B");

		Assert.IsTrue (failure1 == failure2);
		Assert.IsFalse (failure1 == failure3);
	}

	[TestMethod]
	public void ApeTagItemParseResult_OperatorNotEquals_Works ()
	{
		var failure1 = ApeTagItemParseResult.Failure ("Error A");
		var failure2 = ApeTagItemParseResult.Failure ("Error B");

		Assert.IsTrue (failure1 != failure2);
	}

	[TestMethod]
	public void ApeTagItem_Parse_NoKeyTerminator_Fails ()
	{
		var data = new byte[20];
		BitConverter.GetBytes (4u).CopyTo (data, 0);
		for (int i = 8; i < data.Length; i++)
			data[i] = (byte)'A';

		var result = ApeTagItem.Parse (data);
		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("null"));
	}

	#endregion

	#region ApeTagHeader Coverage Tests

	[TestMethod]
	public void ApeTagHeader_Create_ReadOnly_SetsFlag ()
	{
		var header = ApeTagHeader.Create (100, 5, isReadOnly: true);
		Assert.IsTrue (header.IsReadOnly);
		Assert.IsTrue (header.IsHeader);
		Assert.IsTrue (header.HasHeader);
	}

	[TestMethod]
	public void ApeTagHeader_Render_RoundTrips ()
	{
		var original = ApeTagHeader.Create (200, 10);
		var rendered = original.Render ();
		var reparsed = ApeTagHeader.Parse (rendered);

		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (original.TagSize, reparsed.Header!.TagSize);
		Assert.AreEqual (original.ItemCount, reparsed.Header.ItemCount);
		Assert.IsTrue (reparsed.Header.IsHeader);
	}

	[TestMethod]
	public void ApeTagHeader_Parse_FooterData_Fails ()
	{
		var footer = ApeTagFooter.Create (100, 5, isHeader: false);
		var data = footer.Render ();

		var result = ApeTagHeader.Parse (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsTrue (result.Error!.Contains ("header") || result.Error.Contains ("bit 29"));
	}

	[TestMethod]
	public void ApeTagHeaderParseResult_OperatorEquals_Works ()
	{
		var failure1 = ApeTagHeaderParseResult.Failure ("Error A");
		var failure2 = ApeTagHeaderParseResult.Failure ("Error A");

		Assert.IsTrue (failure1 == failure2);
	}

	[TestMethod]
	public void ApeTagHeaderParseResult_OperatorNotEquals_Works ()
	{
		var failure1 = ApeTagHeaderParseResult.Failure ("Error A");
		var failure2 = ApeTagHeaderParseResult.Failure ("Error B");

		Assert.IsTrue (failure1 != failure2);
	}

	#endregion

	#region Result Type Equality Tests

	[TestMethod]
	public void ApeTagParseResult_Equality_WorksCorrectly ()
	{
		// Arrange
		var tag1 = CreateCompleteTag (new Dictionary<string, string> { { "Title", "Test" } });
		var result1 = ApeTag.Parse (tag1);
		var result2 = ApeTag.Parse (tag1);
		var failure1 = ApeTagParseResult.Failure ("Error 1");
		var failure2 = ApeTagParseResult.Failure ("Error 1");
		var failure3 = ApeTagParseResult.Failure ("Error 2");

		// Act & Assert
		Assert.IsTrue (result1.IsSuccess);
		Assert.IsTrue (result2.IsSuccess);

		// Failures with same error are equal
		Assert.IsTrue (failure1.Equals (failure2));
		Assert.AreEqual (failure1.GetHashCode (), failure2.GetHashCode ());

		// Failures with different errors aren't equal
		Assert.IsFalse (failure1.Equals (failure3));

		// Object equality
		Assert.IsFalse (result1.Equals ((object?)null));
		Assert.IsFalse (result1.Equals ("not a result"));
		Assert.IsTrue (failure1.Equals ((object)failure2));
	}

	[TestMethod]
	public void ApeTagFooterParseResult_Equality_WorksCorrectly ()
	{
		// Arrange
		var footer = CreateValidFooter (2000, 64, 2, 0);
		var result1 = ApeTagFooter.Parse (footer);
		var failure = ApeTagFooterParseResult.Failure ("Error");

		// Act & Assert
		Assert.IsTrue (result1.IsSuccess);
		Assert.IsFalse (failure.IsSuccess);
		_ = result1.GetHashCode ();
		_ = failure.GetHashCode ();
		Assert.IsFalse (result1.Equals ((object?)null));
	}

	[TestMethod]
	public void ApeTagHeaderParseResult_Equality_WorksCorrectly ()
	{
		// Arrange - create header by rendering one
		var header = ApeTagHeader.Create (64, 2);
		var headerData = header.Render ();
		var result = ApeTagHeader.Parse (headerData);
		var failure = ApeTagHeaderParseResult.Failure ("Error");

		// Act & Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (failure.IsSuccess);
		_ = result.GetHashCode ();
		_ = failure.GetHashCode ();
		Assert.IsFalse (result.Equals ((object?)null));
		Assert.IsTrue (failure.Equals ((object)failure));
	}

	[TestMethod]
	public void TextLists_RoundTrip_KeepsEveryNameWithoutAnExtraNul ()
	{
		var tag = new ApeTag {
			Performers = ["Eve", "Fay"],
			AlbumArtists = ["Ada", "Bea"],
			Composers = ["Cara", "Dan"],
			Genres = ["Jazz", "Soul"]
		};

		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("Fay", tag.Performers[1]);
		Assert.HasCount (2, tag.AlbumArtists);
		Assert.AreEqual ("Bea", tag.AlbumArtists[1]);
		Assert.HasCount (2, tag.Composers);
		Assert.AreEqual ("Dan", tag.Composers[1]);
		Assert.HasCount (2, tag.Genres);
		Assert.AreEqual ("Soul", tag.Genres[1]);
		AssertRaw (tag, "Artist", "Eve\0Fay");
		AssertRaw (tag, "Album Artist", "Ada\0Bea");
		AssertRaw (tag, "Composer", "Cara\0Dan");
		AssertRaw (tag, "Genre", "Jazz\0Soul");

		var parsedMany = ApeTag.Parse (tag.Render ().Span);
		Assert.IsTrue (parsedMany.IsSuccess, parsedMany.Error);
		Assert.HasCount (2, parsedMany.Tag!.AlbumArtists);
		Assert.AreEqual ("Ada", parsedMany.Tag.AlbumArtists[0]);
		Assert.AreEqual ("Bea", parsedMany.Tag.AlbumArtists[1]);
		Assert.HasCount (2, parsedMany.Tag.Composers);
		Assert.HasCount (2, parsedMany.Tag.Performers);
		Assert.HasCount (2, parsedMany.Tag.Genres);

		tag.Performers = ["Eve"];
		tag.AlbumArtists = ["Ada"];
		tag.Composers = ["Cara"];
		tag.Genres = ["Jazz"];
		AssertRaw (tag, "Artist", "Eve");
		AssertRaw (tag, "Album Artist", "Ada");
		AssertRaw (tag, "Composer", "Cara");
		AssertRaw (tag, "Genre", "Jazz");

		var parsed = ApeTag.Parse (tag.Render ().Span);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.HasCount (1, parsed.Tag!.Performers);
		Assert.AreEqual ("Eve", parsed.Tag.Performers[0]);
		Assert.AreEqual ("Ada", parsed.Tag.AlbumArtists[0]);
		Assert.AreEqual ("Cara", parsed.Tag.Composers[0]);
		Assert.AreEqual ("Jazz", parsed.Tag.Genres[0]);

		var alias = new ApeTag ();
		alias.SetValue ("ALBUMARTIST", "Ada\0Bea");
		Assert.HasCount (2, alias.AlbumArtists);
		Assert.AreEqual ("Ada", alias.AlbumArtists[0]);
		Assert.AreEqual ("Bea", alias.AlbumArtists[1]);
	}

	[TestMethod]
	public void SingularNames_ReturnTheFirstListValue ()
	{
		var tag = new ApeTag {
			Performers = ["A", "B"],
			AlbumArtists = ["Ada", "Bea"],
			Composers = ["Cara", "Dan"],
			Genres = ["Jazz", "Soul"]
		};

		Assert.AreEqual ("A", tag.Artist);
		Assert.HasCount (2, tag.Performers);
		Assert.AreEqual ("A", tag.Performers[0]);
		Assert.AreEqual ("B", tag.Performers[1]);
		Assert.AreEqual ("Ada", tag.AlbumArtist);
		Assert.HasCount (2, tag.AlbumArtists);
		Assert.AreEqual ("Bea", tag.AlbumArtists[1]);
		Assert.AreEqual ("Cara", tag.Composer);
		Assert.HasCount (2, tag.Composers);
		Assert.AreEqual ("Dan", tag.Composers[1]);
		Assert.AreEqual ("Jazz", tag.Genre);
		Assert.HasCount (2, tag.Genres);
		Assert.AreEqual ("Soul", tag.Genres[1]);

		var parsed = ApeTag.Parse (tag.Render ().Span);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.AreEqual ("A", parsed.Tag!.Artist);
		Assert.HasCount (2, parsed.Tag.Performers);
		Assert.AreEqual ("B", parsed.Tag.Performers[1]);
		Assert.AreEqual ("Ada", parsed.Tag.AlbumArtist);
		Assert.AreEqual ("Cara", parsed.Tag.Composer);
		Assert.AreEqual ("Jazz", parsed.Tag.Genre);
		Assert.HasCount (2, parsed.Tag.Genres);
	}

	[TestMethod]
	public void AlbumArtists_SetAndClear_RemovesBothItemNames ()
	{
		var replaced = new ApeTag ();
		replaced.SetValue ("Album Artist", "Cara\0Dan");
		replaced.SetValue ("ALBUMARTIST", "Ada\0Bea");
		replaced.AlbumArtists = ["A"];

		Assert.AreEqual ("A", replaced.AlbumArtist);
		Assert.HasCount (1, replaced.AlbumArtists);
		Assert.AreEqual ("A", replaced.AlbumArtists[0]);
		Assert.IsNull (replaced.GetValue ("ALBUMARTIST"));
		AssertRaw (replaced, "Album Artist", "A");

		var parsed = ApeTag.Parse (replaced.Render ().Span);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.HasCount (1, parsed.Tag!.AlbumArtists);
		Assert.AreEqual ("A", parsed.Tag.AlbumArtists[0]);
		Assert.AreEqual ("A", parsed.Tag.AlbumArtist);
		Assert.IsNull (parsed.Tag.GetValue ("ALBUMARTIST"));
		Assert.IsNull (parsed.Tag.GetValue ("AlbumArtist"));

		var singular = new ApeTag ();
		singular.SetValue ("Album Artist", "Ada\0Bea");
		singular.SetValue ("ALBUMARTIST", "Cara\0Dan");
		singular.AlbumArtist = "A";
		Assert.AreEqual ("A", singular.AlbumArtist);
		Assert.HasCount (1, singular.AlbumArtists);
		Assert.IsNull (singular.GetValue ("ALBUMARTIST"));
		Assert.AreEqual ("A", singular.GetValue ("Album Artist"));

		singular.AlbumArtist = null;
		Assert.IsNull (singular.AlbumArtist);
		Assert.HasCount (0, singular.AlbumArtists);
		Assert.IsNull (singular.GetValue ("Album Artist"));
		Assert.IsNull (singular.GetValue ("ALBUMARTIST"));
		Assert.AreEqual (0, singular.ItemCount);

		var cleared = new ApeTag ();
		cleared.SetValue ("Album Artist", "Cara\0Dan");
		cleared.SetValue ("ALBUMARTIST", "Ada\0Bea");
		cleared.AlbumArtists = null!;

		var parsedClear = ApeTag.Parse (cleared.Render ().Span);
		Assert.IsTrue (parsedClear.IsSuccess, parsedClear.Error);
		Assert.HasCount (0, parsedClear.Tag!.AlbumArtists);
		Assert.IsNull (parsedClear.Tag.AlbumArtist);
		Assert.IsNull (parsedClear.Tag.GetValue ("ALBUMARTIST"));
		Assert.IsNull (parsedClear.Tag.GetValue ("Album Artist"));
		Assert.AreEqual (0, parsedClear.Tag.ItemCount);

		var empty = new ApeTag ();
		empty.SetValue ("ALBUMARTIST", "Ada\0Bea");
		empty.AlbumArtists = [];
		Assert.AreEqual (0, empty.ItemCount);
		Assert.IsNull (empty.AlbumArtist);
		Assert.HasCount (0, empty.AlbumArtists);

		var blanks = new ApeTag ();
		blanks.SetValue ("Album Artist", "Ada\0Bea");
		blanks.SetValue ("ALBUMARTIST", "Cara");
		blanks.AlbumArtists = [""];
		Assert.AreEqual (0, blanks.ItemCount);
		Assert.IsNull (blanks.GetValue ("ALBUMARTIST"));
		Assert.IsNull (blanks.GetValue ("Album Artist"));
	}

	[TestMethod]
	public void TextList_EmptyPartsAndNonText_AreNotNames ()
	{
		var split = new ApeTag ();
		split.SetValue ("Artist", "A\0\0B");
		Assert.AreEqual ("A", split.Artist);
		Assert.HasCount (2, split.Performers);
		Assert.AreEqual ("B", split.Performers[1]);

		split.Performers = ["A", "", "B"];
		Assert.AreEqual ("A", split.Artist);
		Assert.HasCount (2, split.Performers);
		Assert.AreEqual ("B", split.Performers[1]);
		AssertRaw (split, "Artist", "A\0B");

		var onlyNuls = new ApeTag ();
		onlyNuls.SetValue ("Artist", "\0\0");
		Assert.IsNull (onlyNuls.Artist);
		Assert.HasCount (0, onlyNuls.Performers);

		var emptyText = new ApeTag ();
		emptyText.SetValue ("Genre", "");
		Assert.IsNull (emptyText.Genre);
		Assert.HasCount (0, emptyText.Genres);

		var cleared = new ApeTag { Performers = ["A", "B"], Composers = ["Cara", "Dan"] };
		cleared.Performers = null!;
		cleared.Composers = [];
		Assert.IsNull (cleared.Artist);
		Assert.IsNull (cleared.GetValue ("Artist"));
		Assert.IsNull (cleared.Composer);
		Assert.IsNull (cleared.GetValue ("Composer"));
		Assert.HasCount (0, cleared.Performers);
		Assert.HasCount (0, cleared.Composers);

		var binary = new ApeTag ();
		binary.SetBinaryItem ("Artist", "a.jpg", [0x01]);
		Assert.IsNull (binary.Artist);
		Assert.HasCount (0, binary.Performers);
	}

	static void AssertRaw (ApeTag tag, string key, string expected)
	{
		var raw = RawText (tag, key);
		CollectionAssert.AreEqual (Encoding.UTF8.GetBytes (expected), raw);
	}

	static byte[] RawText (ApeTag tag, string key)
	{
		var bytes = tag.Render ().ToArray ();
		var items = bytes.AsSpan (0, bytes.Length - ApeTagFooter.Size);
		while (!items.IsEmpty) {
			var parsed = ApeTagItem.Parse (items);
			Assert.IsTrue (parsed.IsSuccess, parsed.Error);
			if (string.Equals (parsed.Item!.Key, key, StringComparison.OrdinalIgnoreCase))
				return parsed.Item.RawValue;
			items = items.Slice (parsed.BytesConsumed);
		}

		Assert.Fail ("Missing " + key);
		return [];
	}

	[TestMethod]
	public void ApeTagItemParseResult_Equality_WorksCorrectly ()
	{
		// Arrange
		var item = CreateTextItem ("Title", "Test Song");
		var result = ApeTagItem.Parse (item);
		var failure = ApeTagItemParseResult.Failure ("Error");

		// Act & Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (failure.IsSuccess);
		_ = result.GetHashCode ();
		_ = failure.GetHashCode ();
		Assert.IsFalse (result.Equals ((object?)null));
	}

	#endregion
}
