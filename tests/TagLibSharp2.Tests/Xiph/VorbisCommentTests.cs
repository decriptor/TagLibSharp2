// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class VorbisCommentTests
{
	[TestMethod]
	public void Constructor_InitializesEmptyTag ()
	{
		var comment = new VorbisComment ();

		Assert.AreEqual ("", comment.VendorString);
		Assert.IsEmpty (comment.Fields);
	}

	[TestMethod]
	public void Constructor_WithVendor_SetsVendorString ()
	{
		var comment = new VorbisComment ("TagLibSharp2 1.0");

		Assert.AreEqual ("TagLibSharp2 1.0", comment.VendorString);
	}

	[TestMethod]
	[DataRow ("Title", "My Song", "TITLE")]
	[DataRow ("Artist", "The Band", "ARTIST")]
	[DataRow ("Album", "Greatest Hits", "ALBUM")]
	[DataRow ("Year", "2024", "DATE")]
	[DataRow ("Genre", "Rock", "GENRE")]
	[DataRow ("Comment", "A great song", "COMMENT")]
	[DataRow ("AlbumArtist", "Various Artists", "ALBUMARTIST")]
	[DataRow ("Composer", "Johann Sebastian Bach", "COMPOSER")]
	public void StringProperty_GetSet_Works (string propertyName, string value, string expectedField)
	{
		var comment = new VorbisComment ();

		// Set property using reflection
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		property.SetValue (comment, value);

		// Verify getter returns same value
		Assert.AreEqual (value, property.GetValue (comment), $"{propertyName} getter mismatch");

		// Verify it maps to correct Vorbis field
		Assert.AreEqual (value, comment.GetValue (expectedField), $"{propertyName} should map to {expectedField}");
	}

	[TestMethod]
	[DataRow ("Track", 5u, "TRACKNUMBER", "5")]
	[DataRow ("DiscNumber", 2u, "DISCNUMBER", "2")]
	[DataRow ("BeatsPerMinute", 140u, "BPM", "140")]
	[DataRow ("TotalTracks", 12u, "TOTALTRACKS", "12")]
	[DataRow ("TotalDiscs", 3u, "TOTALDISCS", "3")]
	public void UIntProperty_GetSet_Works (string propertyName, uint value, string expectedField, string expectedString)
	{
		var comment = new VorbisComment ();

		// Set property using reflection
		var property = typeof (VorbisComment).GetProperty (propertyName)!;
		property.SetValue (comment, value);

		// Verify getter returns same value
		Assert.AreEqual (value, property.GetValue (comment), $"{propertyName} getter mismatch");

		// Verify it maps to correct Vorbis field
		Assert.AreEqual (expectedString, comment.GetValue (expectedField), $"{propertyName} should map to {expectedField}");
	}

	[TestMethod]
	public void AddField_AddsToFieldList ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("CUSTOM", "Value");

		Assert.HasCount (1, comment.Fields);
		Assert.AreEqual ("CUSTOM", comment.Fields[0].Name);
		Assert.AreEqual ("Value", comment.Fields[0].Value);
	}

	[TestMethod]
	public void GetValues_MultipleFields_ReturnsAll ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("ARTIST", "John Lennon");
		comment.AddField ("ARTIST", "Paul McCartney");
		comment.AddField ("ARTIST", "George Harrison");
		comment.AddField ("ARTIST", "Ringo Starr");

		var artists = comment.GetValues ("ARTIST");

		Assert.HasCount (4, artists);
		Assert.IsTrue (artists.Contains ("John Lennon"));
		Assert.IsTrue (artists.Contains ("Paul McCartney"));
		Assert.IsTrue (artists.Contains ("George Harrison"));
		Assert.IsTrue (artists.Contains ("Ringo Starr"));
	}

	[TestMethod]
	public void GetValue_MultipleFields_ReturnsFirst ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("ARTIST", "First Artist");
		comment.AddField ("ARTIST", "Second Artist");

		Assert.AreEqual ("First Artist", comment.GetValue ("ARTIST"));
	}

	[TestMethod]
	public void GetValue_CaseInsensitive ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("TITLE", "Test");

		Assert.AreEqual ("Test", comment.GetValue ("title"));
		Assert.AreEqual ("Test", comment.GetValue ("Title"));
		Assert.AreEqual ("Test", comment.GetValue ("TITLE"));
	}

	[TestMethod]
	public void GetValue_NonExistentField_ReturnsNull ()
	{
		// This test verifies that GetValue returns null for non-existent fields
		// rather than relying on struct default behavior (which can be confusing)
		var comment = new VorbisComment ();
		comment.AddField ("TITLE", "Test");

		var result = comment.GetValue ("NONEXISTENT");

		Assert.IsNull (result);
	}

	[TestMethod]
	public void GetValue_EmptyFields_ReturnsNull ()
	{
		var comment = new VorbisComment ();

		var result = comment.GetValue ("TITLE");

		Assert.IsNull (result);
	}

	[TestMethod]
	public void SetValue_ReplacesExisting ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("TITLE", "Old Title");
		comment.AddField ("TITLE", "Another Title");

		comment.SetValue ("TITLE", "New Title");

		Assert.HasCount (1, comment.GetValues ("TITLE"));
		Assert.AreEqual ("New Title", comment.GetValue ("TITLE"));
	}

	[TestMethod]
	public void SetValue_Null_RemovesField ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("TITLE", "Test");
		comment.SetValue ("TITLE", null);

		Assert.IsEmpty (comment.GetValues ("TITLE"));
		Assert.IsNull (comment.GetValue ("TITLE"));
	}

	[TestMethod]
	public void SetValue_EmptyString_SetsEmptyValue ()
	{
		// Empty strings are valid in Vorbis Comments and should NOT be treated as null.
		// A field with an empty value is semantically different from no field at all.
		var comment = new VorbisComment ();

		comment.AddField ("TITLE", "Test");
		comment.SetValue ("TITLE", "");

		// Empty value should persist, not be removed
		Assert.HasCount (1, comment.GetValues ("TITLE"));
		Assert.AreEqual ("", comment.GetValue ("TITLE"));
	}

	[TestMethod]
	public void RemoveAll_RemovesAllMatchingFields ()
	{
		var comment = new VorbisComment ();

		comment.AddField ("ARTIST", "Artist 1");
		comment.AddField ("ARTIST", "Artist 2");
		comment.AddField ("TITLE", "Title");

		comment.RemoveAll ("ARTIST");

		Assert.IsEmpty (comment.GetValues ("ARTIST"));
		Assert.AreEqual ("Title", comment.GetValue ("TITLE"));
	}

	[TestMethod]
	public void Clear_RemovesAllFieldsButKeepsVendor ()
	{
		var comment = new VorbisComment ("TestVendor");

		comment.Title = "Test";
		comment.Artist = "Artist";

		comment.Clear ();

		Assert.AreEqual ("TestVendor", comment.VendorString);
		Assert.IsEmpty (comment.Fields);
		Assert.IsNull (comment.Title);
	}

	[TestMethod]
	public void Read_ValidData_ParsesCorrectly ()
	{
		// Build a Vorbis Comment block manually
		// Format: [vendor_len:4 LE][vendor:n][field_count:4 LE][fields...]
		using var builder = new BinaryDataBuilder ();

		// Vendor string "Test"
		var vendorBytes = System.Text.Encoding.UTF8.GetBytes ("Test");
		builder.Add (BitConverter.GetBytes ((uint)vendorBytes.Length));
		builder.Add (vendorBytes);

		// 2 fields
		builder.Add (BitConverter.GetBytes ((uint)2));

		// Field 1: TITLE=Song Name
		var field1 = System.Text.Encoding.UTF8.GetBytes ("TITLE=Song Name");
		builder.Add (BitConverter.GetBytes ((uint)field1.Length));
		builder.Add (field1);

		// Field 2: ARTIST=The Artist
		var field2 = System.Text.Encoding.UTF8.GetBytes ("ARTIST=The Artist");
		builder.Add (BitConverter.GetBytes ((uint)field2.Length));
		builder.Add (field2);

		var data = builder.ToBinaryData ();
		var result = VorbisComment.Read (data.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test", result.Tag!.VendorString);
		Assert.AreEqual ("Song Name", result.Tag.Title);
		Assert.AreEqual ("The Artist", result.Tag.Artist);
	}

	[TestMethod]
	public void Read_EmptyFields_ParsesCorrectly ()
	{
		using var builder = new BinaryDataBuilder ();

		// Empty vendor
		builder.Add (BitConverter.GetBytes ((uint)0));

		// 0 fields
		builder.Add (BitConverter.GetBytes ((uint)0));

		var data = builder.ToBinaryData ();
		var result = VorbisComment.Read (data.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Tag!.VendorString);
		Assert.IsEmpty (result.Tag.Fields);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var result = VorbisComment.Read (new byte[] { 0x01, 0x00 });

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Render_ProducesValidData ()
	{
		var comment = new VorbisComment (TestConstants.Vendors.TagLibSharp2);
		comment.Title = TestConstants.Metadata.Title;
		comment.Artist = TestConstants.Metadata.Artist;

		var rendered = comment.Render ();

		// Parse it back
		var result = VorbisComment.Read (rendered.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TestConstants.Vendors.TagLibSharp2, result.Tag!.VendorString);
		Assert.AreEqual (TestConstants.Metadata.Title, result.Tag.Title);
		Assert.AreEqual (TestConstants.Metadata.Artist, result.Tag.Artist);
	}

	[TestMethod]
	public void Render_MultipleArtists_PreservesAll ()
	{
		var comment = new VorbisComment ();
		comment.AddField ("ARTIST", "Artist 1");
		comment.AddField ("ARTIST", "Artist 2");

		var rendered = comment.Render ();
		var result = VorbisComment.Read (rendered.ToArray ());

		Assert.IsTrue (result.IsSuccess);
		var artists = result.Tag!.GetValues ("ARTIST");
		Assert.HasCount (2, artists);
		Assert.IsTrue (artists.Contains ("Artist 1"));
		Assert.IsTrue (artists.Contains ("Artist 2"));
	}

	[TestMethod]
	public void Render_ProducesLittleEndianBytes ()
	{
		var comment = new VorbisComment ("Test");
		var rendered = comment.Render ().ToArray ();

		// Vendor string "Test" is 4 bytes
		// Little-endian format: 0x04, 0x00, 0x00, 0x00
		Assert.AreEqual ((byte)4, rendered[0], "Byte 0 should be 4 (length LSB)");
		Assert.AreEqual ((byte)0, rendered[1], "Byte 1 should be 0");
		Assert.AreEqual ((byte)0, rendered[2], "Byte 2 should be 0");
		Assert.AreEqual ((byte)0, rendered[3], "Byte 3 should be 0 (length MSB)");
	}

	[TestMethod]
	public void Read_VendorLengthOverflow_ReturnsFailure ()
	{
		// Build data with vendor length that would overflow int.MaxValue
		var data = new byte[12];
		// Set vendor length to 0xFFFFFFFF (> int.MaxValue)
		data[0] = 0xFF;
		data[1] = 0xFF;
		data[2] = 0xFF;
		data[3] = 0xFF;

		var result = VorbisComment.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("overflow", result.Error!.ToLowerInvariant ());
	}

	[TestMethod]
	public void Read_FieldLengthOverflow_ReturnsFailure ()
	{
		using var builder = new BinaryDataBuilder ();

		// Empty vendor
		builder.Add (BitConverter.GetBytes (0u));

		// 1 field
		builder.Add (BitConverter.GetBytes (1u));

		// Field with length > int.MaxValue
		builder.Add (new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

		var result = VorbisComment.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("overflow", result.Error!.ToLowerInvariant ());
	}

	[TestMethod]
	public void DiscNumber_WithFormat_ParsesCorrectly ()
	{
		var comment = new VorbisComment ();
		comment.SetValue ("DISCNUMBER", "2/3");

		Assert.AreEqual (2u, comment.DiscNumber);
	}

	[TestMethod]
	public void TotalTracks_FromTrackNumber_ParsesCorrectly ()
	{
		var comment = new VorbisComment ();
		comment.SetValue ("TRACKNUMBER", "5/12");

		Assert.AreEqual (12u, comment.TotalTracks);
	}

	[TestMethod]
	public void Artists_ReturnsAllArtistValues ()
	{
		var comment = new VorbisComment ();
		comment.AddField ("ARTIST", "Artist 1");
		comment.AddField ("ARTIST", "Artist 2");
		comment.AddField ("ARTIST", "Artist 3");

		var artists = comment.Performers;

		Assert.HasCount (3, artists);
		Assert.IsTrue (artists.Contains ("Artist 1"));
		Assert.IsTrue (artists.Contains ("Artist 2"));
		Assert.IsTrue (artists.Contains ("Artist 3"));
	}

	[TestMethod]
	public void Genres_ReturnsAllGenreValues ()
	{
		var comment = new VorbisComment ();
		comment.AddField ("GENRE", "Rock");
		comment.AddField ("GENRE", "Alternative");

		var genres = comment.Genres;

		Assert.HasCount (2, genres);
		Assert.IsTrue (genres.Contains ("Rock"));
		Assert.IsTrue (genres.Contains ("Alternative"));
	}

	[TestMethod]
	public void Composers_ReturnsAllComposerValues ()
	{
		var comment = new VorbisComment ();
		comment.AddField ("COMPOSER", "Lennon");
		comment.AddField ("COMPOSER", "McCartney");

		var composers = comment.Composers;

		Assert.HasCount (2, composers);
		Assert.IsTrue (composers.Contains ("Lennon"));
		Assert.IsTrue (composers.Contains ("McCartney"));
	}

	[TestMethod]
	public void Pictures_InitiallyEmpty ()
	{
		var comment = new VorbisComment ();

		Assert.IsEmpty (comment.Pictures);
	}

	[TestMethod]
	public void AddPicture_AddsToPictureList ()
	{
		var comment = new VorbisComment ();
		var picture = FlacPicture.FromBytes (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

		comment.AddPicture (picture);

		Assert.HasCount (1, comment.Pictures);
		Assert.AreEqual (picture, comment.Pictures[0]);
	}

	[TestMethod]
	public void RemovePictures_RemovesByType ()
	{
		var comment = new VorbisComment ();
		comment.AddPicture (new FlacPicture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (new byte[] { 0xFF, 0xD8 }), 0, 0, 0, 0));
		comment.AddPicture (new FlacPicture ("image/jpeg", PictureType.BackCover, "", new BinaryData (new byte[] { 0xFF, 0xD8 }), 0, 0, 0, 0));

		comment.RemovePictures (PictureType.FrontCover);

		Assert.HasCount (1, comment.Pictures);
		Assert.AreEqual (PictureType.BackCover, comment.Pictures[0].PictureType);
	}

	[TestMethod]
	public void RemoveAllPictures_ClearsAll ()
	{
		var comment = new VorbisComment ();
		comment.AddPicture (FlacPicture.FromBytes (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
		comment.AddPicture (FlacPicture.FromBytes (new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

		comment.RemoveAllPictures ();

		Assert.IsEmpty (comment.Pictures);
	}

	[TestMethod]
	public void Render_WithPictures_RoundTripsCorrectly ()
	{
		var comment = new VorbisComment ("TestVendor");
		comment.Title = "Test";
		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), 100, 100, 24, 0);
		comment.AddPicture (picture);

		var rendered = comment.Render ();

		// Parse it back - should have picture in Pictures collection
		var result = VorbisComment.Read (rendered.ToArray ());
		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.Tag!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, result.Tag.Pictures[0].PictureType);
		Assert.AreEqual ("image/jpeg", result.Tag.Pictures[0].MimeType);
	}

	[TestMethod]
	public void Read_WithMetadataBlockPicture_ParsesPicture ()
	{
		// Create a picture and encode to base64
		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), 100, 100, 24, 0);
		var pictureData = picture.RenderContent ();
		var base64 = Convert.ToBase64String (pictureData.ToArray ());

		// Build Vorbis Comment with METADATA_BLOCK_PICTURE field
		using var builder = new BinaryDataBuilder ();
		var vendorBytes = System.Text.Encoding.UTF8.GetBytes ("Test");
		builder.Add (BitConverter.GetBytes ((uint)vendorBytes.Length));
		builder.Add (vendorBytes);
		builder.Add (BitConverter.GetBytes (1u)); // 1 field
		var fieldBytes = System.Text.Encoding.UTF8.GetBytes ($"METADATA_BLOCK_PICTURE={base64}");
		builder.Add (BitConverter.GetBytes ((uint)fieldBytes.Length));
		builder.Add (fieldBytes);

		var result = VorbisComment.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.Tag!.Pictures);
		Assert.AreEqual (PictureType.FrontCover, result.Tag.Pictures[0].PictureType);
		Assert.AreEqual ("image/jpeg", result.Tag.Pictures[0].MimeType);
	}

	[TestMethod]
	public void Read_WithMetadataBlockPicture_DoesNotDuplicateInFields ()
	{
		// Create a picture and encode to base64
		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "Cover", new BinaryData (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), 100, 100, 24, 0);
		var pictureData = picture.RenderContent ();
		var base64 = Convert.ToBase64String (pictureData.ToArray ());

		// Build Vorbis Comment with METADATA_BLOCK_PICTURE field and a regular field
		using var builder = new BinaryDataBuilder ();
		var vendorBytes = System.Text.Encoding.UTF8.GetBytes ("Test");
		builder.Add (BitConverter.GetBytes ((uint)vendorBytes.Length));
		builder.Add (vendorBytes);
		builder.Add (BitConverter.GetBytes (2u)); // 2 fields

		// Field 1: Regular TITLE field
		var titleBytes = System.Text.Encoding.UTF8.GetBytes ("TITLE=Test Song");
		builder.Add (BitConverter.GetBytes ((uint)titleBytes.Length));
		builder.Add (titleBytes);

		// Field 2: METADATA_BLOCK_PICTURE
		var fieldBytes = System.Text.Encoding.UTF8.GetBytes ($"METADATA_BLOCK_PICTURE={base64}");
		builder.Add (BitConverter.GetBytes ((uint)fieldBytes.Length));
		builder.Add (fieldBytes);

		var result = VorbisComment.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsTrue (result.IsSuccess);
		// Picture should be parsed into Pictures collection
		Assert.HasCount (1, result.Tag!.Pictures);

		// METADATA_BLOCK_PICTURE should NOT be in Fields when successfully parsed as picture
		// Fields should only contain the TITLE field
		Assert.HasCount (1, result.Tag.Fields, "Successfully parsed METADATA_BLOCK_PICTURE should not be duplicated in Fields");
		Assert.AreEqual ("TITLE", result.Tag.Fields[0].Name);
	}

	[TestMethod]
	public void Read_WithCorruptedBase64Picture_PreservesInFields ()
	{
		// Build Vorbis Comment with corrupted METADATA_BLOCK_PICTURE (invalid base64)
		using var builder = new BinaryDataBuilder ();
		var vendorBytes = System.Text.Encoding.UTF8.GetBytes ("Test");
		builder.Add (BitConverter.GetBytes ((uint)vendorBytes.Length));
		builder.Add (vendorBytes);
		builder.Add (BitConverter.GetBytes (1u)); // 1 field

		// Corrupted base64 that won't parse as a picture
		var fieldBytes = System.Text.Encoding.UTF8.GetBytes ("METADATA_BLOCK_PICTURE=!!!not-valid-base64!!!");
		builder.Add (BitConverter.GetBytes ((uint)fieldBytes.Length));
		builder.Add (fieldBytes);

		var result = VorbisComment.Read (builder.ToBinaryData ().ToArray ());

		Assert.IsTrue (result.IsSuccess);
		// No valid pictures parsed
		Assert.IsEmpty (result.Tag!.Pictures);

		// The corrupted field should be preserved in Fields so data isn't lost
		Assert.HasCount (1, result.Tag.Fields);
		Assert.AreEqual ("METADATA_BLOCK_PICTURE", result.Tag.Fields[0].Name);
	}
}
