// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
