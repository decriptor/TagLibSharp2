// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// QuickTime metadata whose meta handler is "mdta": ilst items are typed by a 1-based index into
/// the keys atom. Fields are read from their keys and written back to them, never as iTunes atoms.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4QuickTimeKeysTests
{
	const string Mean = "com.apple.iTunes";
	const string MusicBrainz = "org.musicbrainz";
	static readonly byte[] Other = TestBuilders.Mp4.CreateMdtaItem (2, "+12.3456-065.4321/");
	static readonly string[] ArtistOnly = ["artist"];
	static readonly string[] DisplayNameOnly = ["com.apple.quicktime.displayname"];
	static readonly string[] ArtistThenTitle = ["com.apple.quicktime.artist", "com.apple.quicktime.title"];

	// Every key name this library maps: ffmpeg's -movflags use_metadata_tags names, Apple's
	// com.apple.quicktime.* keys, and the freeform names (which ffmpeg writes as keys).
	[TestMethod]
	[DataRow ("title", "Title")]
	[DataRow ("artist", "Artist")]
	[DataRow ("album", "Album")]
	[DataRow ("album_artist", "AlbumArtist")]
	[DataRow ("composer", "Composer")]
	[DataRow ("date", "Year")]
	[DataRow ("comment", "Comment")]
	[DataRow ("genre", "Genre")]
	[DataRow ("copyright", "Copyright")]
	[DataRow ("description", "Description")]
	[DataRow ("grouping", "Grouping")]
	[DataRow ("lyrics", "Lyrics")]
	[DataRow ("encoder", "EncoderSettings")]
	[DataRow ("encoded_by", "EncodedBy")]
	[DataRow ("publisher", "Publisher")]
	[DataRow ("sort_name", "TitleSort")]
	[DataRow ("sort_artist", "ArtistSort")]
	[DataRow ("sort_album", "AlbumSort")]
	[DataRow ("sort_album_artist", "AlbumArtistSort")]
	[DataRow ("sort_composer", "ComposerSort")]
	[DataRow ("category", "PodcastCategory")]
	[DataRow ("keywords", "PodcastKeywords")]
	[DataRow ("com.apple.quicktime.title", "Title")]
	[DataRow ("com.apple.quicktime.displayname", "Title")]
	[DataRow ("com.apple.quicktime.artist", "Artist")]
	[DataRow ("com.apple.quicktime.album", "Album")]
	[DataRow ("com.apple.quicktime.comment", "Comment")]
	[DataRow ("com.apple.quicktime.copyright", "Copyright")]
	[DataRow ("com.apple.quicktime.description", "Description")]
	[DataRow ("com.apple.quicktime.genre", "Genre")]
	[DataRow ("com.apple.quicktime.publisher", "Publisher")]
	[DataRow ("com.apple.quicktime.composer", "Composer")]
	[DataRow ("com.apple.quicktime.encodedby", "EncodedBy")]
	[DataRow ("com.apple.quicktime.year", "Year")]
	[DataRow ("com.apple.quicktime.software", "EncoderSettings")]
	[DataRow ("com.apple.quicktime.keywords", "PodcastKeywords")]
	[DataRow ("iTunSMPB", "GaplessInfo")]
	[DataRow ("MusicBrainz Track Id", "MusicBrainzTrackId")]
	[DataRow ("MusicBrainz Album Id", "MusicBrainzReleaseId")]
	[DataRow ("MusicBrainz Artist Id", "MusicBrainzArtistId")]
	[DataRow ("MusicBrainz Album Artist Id", "MusicBrainzAlbumArtistId")]
	[DataRow ("MusicBrainz Release Group Id", "MusicBrainzReleaseGroupId")]
	[DataRow ("MusicBrainz Work Id", "MusicBrainzWorkId")]
	[DataRow ("MusicBrainz Recording Id", "MusicBrainzRecordingId")]
	[DataRow ("MusicBrainz Disc Id", "MusicBrainzDiscId")]
	[DataRow ("MusicBrainz Album Status", "MusicBrainzReleaseStatus")]
	[DataRow ("MusicBrainz Album Type", "MusicBrainzReleaseType")]
	[DataRow ("MusicBrainz Album Release Country", "MusicBrainzReleaseCountry")]
	[DataRow ("Acoustid Id", "AcoustIdId")]
	[DataRow ("Acoustid Fingerprint", "AcoustIdFingerprint")]
	[DataRow ("ISRC", "Isrc")]
	[DataRow ("CONDUCTOR", "Conductor")]
	[DataRow ("ORIGINAL YEAR", "OriginalReleaseDate")]
	[DataRow ("initialkey", "InitialKey")]
	[DataRow ("REMIXER", "Remixer")]
	[DataRow ("MOOD", "Mood")]
	[DataRow ("SUBTITLE", "Subtitle")]
	[DataRow ("BARCODE", "Barcode")]
	[DataRow ("CATALOGNUMBER", "CatalogNumber")]
	[DataRow ("ASIN", "AmazonId")]
	[DataRow ("date_tagged", "DateTagged")]
	[DataRow ("LANGUAGE", "Language")]
	[DataRow ("MEDIA", "MediaType")]
	[DataRow ("replaygain_track_gain", "ReplayGainTrackGain")]
	[DataRow ("replaygain_track_peak", "ReplayGainTrackPeak")]
	[DataRow ("replaygain_album_gain", "ReplayGainAlbumGain")]
	[DataRow ("replaygain_album_peak", "ReplayGainAlbumPeak")]
	[DataRow ("R128_TRACK_GAIN", "R128TrackGain")]
	[DataRow ("R128_ALBUM_GAIN", "R128AlbumGain")]
	public void TextKey_IsReadAndReplacedInPlace (string key, string property)
	{
		var data = Keyed ([key, "com.apple.quicktime.location.ISO6709"],
			TestBuilders.Mp4.CreateMdtaItem (1, "Old-value"), Other);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Old-value", Get (file.Tag!, property));

		Set (file.Tag!, property, "New");
		var rendered = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (rendered, keyCount: 2);
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")));
		Assert.AreEqual (1, Count (rendered, Other));
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
		Assert.AreEqual ("New", Get (Mp4File.Read (rendered).File!.Tag!, property));

		Set (file.Tag!, property, null);
		var cleared = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (cleared, keyCount: 2);
		Assert.AreEqual (-1, cleared.AsSpan ().IndexOf ("New"u8));
		Assert.AreEqual (1, Count (cleared, Other));
		Assert.IsNull (Get (Mp4File.Read (cleared).File!.Tag!, property));
	}

	[TestMethod]
	[DataRow ("track", "3/10", "5/10")]
	[DataRow ("disc", "1/2", "2/2")]
	public void NumberPairKey_IsReadAsNumbersAndWrittenAsText (string key, string old, string expected)
	{
		var data = Keyed ([key], TestBuilders.Mp4.CreateMdtaItem (1, old));
		var file = Mp4File.Read (data).File!;
		var tag = file.Tag!;
		if (key == "track") {
			Assert.AreEqual (3u, tag.Track);
			Assert.AreEqual (10u, tag.TotalTracks);
			tag.Track = 5;
		} else {
			Assert.AreEqual (1u, tag.DiscNumber);
			Assert.AreEqual (2u, tag.TotalDiscs);
			tag.DiscNumber = 2;
		}

		var rendered = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (rendered, keyCount: 1);
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, expected)));
	}

	[TestMethod]
	[DataRow ("compilation", "IsCompilation")]
	[DataRow ("gapless_playback", "IsGapless")]
	[DataRow ("podcast", "IsPodcast")]
	public void FlagKey_IsReadAsFlagAndWrittenAsText (string key, string property)
	{
		var data = Keyed ([key], TestBuilders.Mp4.CreateMdtaItem (1, "1"));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual (true, Get (file.Tag!, property));

		Set (file.Tag!, property, false);
		var cleared = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (cleared, TestBuilders.Mp4.CreateMdtaItem (1, "1")));
		Assert.IsNull (Mp4File.Read (cleared).File!.Tag, "the only item is gone");

		Set (file.Tag!, property, true);
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), TestBuilders.Mp4.CreateMdtaItem (1, "1")));
	}

	[TestMethod]
	public void RatingKey_IsReadAsContentRating ()
	{
		var data = Keyed (["rating"], TestBuilders.Mp4.CreateMdtaItem (1, "2"));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual (Mp4ContentRating.Clean, file.Tag!.ContentRating);

		file.Tag.ContentRating = Mp4ContentRating.Explicit;
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), TestBuilders.Mp4.CreateMdtaItem (1, "1")));
	}

	// Index 0 is reserved and names no key.
	[TestMethod]
	public void ItemTypeZero_NamesNoKey ()
	{
		var zero = TestBuilders.Mp4.CreateMdtaItem (0, "Zero");
		var data = Keyed (["title"], zero, TestBuilders.Mp4.CreateMdtaItem (1, "T"));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("T", file.Title);
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), zero));
	}

	// A text key's value is UTF-8 (value type 1); other data is not that field's value.
	[TestMethod]
	[DataRow (0)]
	[DataRow (2)]
	[DataRow (Mp4AtomMapping.TypeInteger)]
	public void TextKey_IgnoresNonUtf8Data (int typeIndicator)
	{
		var item = TestBuilders.Mp4.CreateMdtaItem (1, typeIndicator, Encoding.BigEndianUnicode.GetBytes ("X"));
		var data = Keyed (["title"], item);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), item));
	}

	[TestMethod]
	public void TextKey_FirstUtf8ValueWins ()
	{
		var payload = new byte[][] {
			TestBuilders.Mp4.CreateBox ("data", [0, 0, 0, 0, 0, 0, 0, 0, 0x01]),
			TestBuilders.Mp4.CreateBox ("data", [0, 0, 0, 1, 0, 0, 0, 0, (byte)'U'])
		};
		var item = TestBuilders.Mp4.CreateBox ("\0\0\0\u0001", [.. payload[0], .. payload[1]]);
		var data = Keyed (["title"], item);
		Assert.AreEqual ("U", Mp4File.Read (data).File!.Title);
	}

	[TestMethod]
	public void UnparsableNumberText_IsKeptAndIgnored ()
	{
		var item = TestBuilders.Mp4.CreateMdtaItem (1, "side A");
		var data = Keyed (["track"], item);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Tag!.Track);
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), item));
	}

	[TestMethod]
	public void UdtaNamespaceKey_NamesAnItunesAtom ()
	{
		// QTFF: a "udta" namespace key's value is a user data atom type.
		var keys = TestBuilders.Mp4.KeysPayload (("udta", Encoding.Latin1.GetBytes ("©nam")), ("udta", Encoding.Latin1.GetBytes ("tmpo")));
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", keys, [
			TestBuilders.Mp4.CreateMdtaItem (1, "Old"),
			TestBuilders.Mp4.CreateMdtaItem (2, Mp4AtomMapping.TypeInteger, [0, 120])], inMovie: false, udtaExtra: null);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Old", file.Title);
		Assert.AreEqual (120u, file.Tag!.BeatsPerMinute);

		file.Title = "New";
		file.Tag.BeatsPerMinute = 90;
		var rendered = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (rendered, keyCount: 2);
		var reread = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("New", reread.Title);
		Assert.AreEqual (90u, reread.Tag!.BeatsPerMinute);
	}

	[TestMethod]
	public void OtherNamespace_IsNotMapped ()
	{
		var item = TestBuilders.Mp4.CreateMdtaItem (1, "Not a title");
		var keys = TestBuilders.Mp4.KeysPayload (("xyzw", Encoding.UTF8.GetBytes ("title")));
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", keys, [item], inMovie: false, udtaExtra: null);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), item));
	}

	// A field the file has no key for gets one, named the way the file names its keys.
	[TestMethod]
	[DataRow ("title", "Artist", "artist")]
	[DataRow ("com.apple.quicktime.title", "Artist", "com.apple.quicktime.artist")]
	[DataRow ("title", "Publisher", "publisher")]
	[DataRow ("com.apple.quicktime.title", "Publisher", "com.apple.quicktime.publisher")]
	[DataRow ("title", "Work", "udta:©wrk")]
	[DataRow ("com.apple.quicktime.title", "AlbumArtist", "album_artist")]
	[DataRow ("title", "Mood", "MOOD")]
	public void NewField_GetsAKey (string existingKey, string property, string expectedKey)
	{
		var data = Keyed ([existingKey], TestBuilders.Mp4.CreateMdtaItem (1, "T"));
		var file = Mp4File.Read (data).File!;
		Set (file.Tag!, property, "Added");

		var rendered = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (rendered, keyCount: 2);
		var keys = Keys (rendered);
		Assert.HasCount (2, keys);
		Assert.AreEqual (existingKey, keys[0]);
		Assert.AreEqual (expectedKey, keys[1]);
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (2, "Added")));
		Assert.AreEqual ("Added", Get (Mp4File.Read (rendered).File!.Tag!, property));
	}

	[TestMethod]
	public void NewPicture_GetsTheArtworkKey ()
	{
		var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "T"));
		var file = Mp4File.Read (data).File!;
		file.AddPicture (new Mp4Picture ("image/jpeg", PictureType.FrontCover, "", new BinaryData (jpeg)));

		var rendered = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (rendered, keyCount: 2);
		Assert.AreEqual ("com.apple.quicktime.artwork", Keys (rendered)[1]);
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (2, Mp4AtomMapping.TypeJpeg, jpeg)));

		var reread = Mp4File.Read (rendered).File!;
		Assert.HasCount (1, reread.Pictures);
		CollectionAssert.AreEqual (jpeg, reread.Pictures[0].PictureData.ToArray ());
	}

	// Setting every property of an mdta tag writes no iTunes atom into it.
	[TestMethod]
	public void EveryProperty_IsWrittenAsAKeyedItem ()
	{
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "T"));
		var file = Mp4File.Read (data).File!;
		var tag = file.Tag!;
		var stored = new List<System.Reflection.PropertyInfo> ();
		foreach (var property in typeof (Mp4Tag).GetProperties ()) {
			if (property.PropertyType == typeof (string) && property.GetSetMethod () is not null)
				property.SetValue (tag, "v-" + property.Name);
		}

		// Some properties share a field and some are not stored in MP4; compare what the tag kept.
		foreach (var property in typeof (Mp4Tag).GetProperties ()) {
			if (property.PropertyType == typeof (string) && property.GetSetMethod () is not null &&
				(string?)property.GetValue (tag) == "v-" + property.Name)
				stored.Add (property);
		}

		tag.Track = 3;
		tag.TotalTracks = 9;
		tag.DiscNumber = 1;
		tag.BeatsPerMinute = 100;
		tag.MovementNumber = 2;
		tag.IsCompilation = true;
		tag.ContentRating = Mp4ContentRating.Explicit;
		file.AddPicture (new Mp4Picture ([0x89, 0x50, 0x4E, 0x47], isJpeg: false));

		var rendered = file.Render (data).ToArray ();
		var keys = Keys (rendered);
		AssertOnlyKeyedItems (rendered, keys.Count);

		var reread = Mp4File.Read (rendered).File!.Tag!;
		foreach (var property in stored)
			Assert.AreEqual ("v-" + property.Name, property.GetValue (reread), property.Name);

		Assert.IsGreaterThan (50, stored.Count);
		Assert.AreEqual (3u, reread.Track);
		Assert.AreEqual (9u, reread.TotalTracks);
		Assert.AreEqual (1u, reread.DiscNumber);
		Assert.AreEqual (100u, reread.BeatsPerMinute);
		Assert.AreEqual (2u, reread.MovementNumber);
		Assert.IsTrue (reread.IsCompilation);
		Assert.AreEqual (Mp4ContentRating.Explicit, reread.ContentRating);
		Assert.HasCount (1, reread.Pictures);
	}

	[TestMethod]
	public void AssignedTag_TakesTheKeysOfAnMdtaMeta ()
	{
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "Old-T"));
		var file = Mp4File.Read (data).File!;
		file.Tag = new Mp4Tag { Title = "New" };

		var rendered = file.Render (data).ToArray ();
		AssertOnlyKeyedItems (rendered, keyCount: 1);
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")));
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
	}

	[TestMethod]
	public void Clear_DropsKeysAndItems ()
	{
		var data = Keyed (["title", "custom"], TestBuilders.Mp4.CreateMdtaItem (1, "Old-T"), TestBuilders.Mp4.CreateMdtaItem (2, "Old-C"));
		var file = Mp4File.Read (data).File!;
		file.Tag!.Clear ();
		file.Tag.Artist = "New";

		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
		CollectionAssert.AreEqual (ArtistOnly, Keys (rendered).ToArray ());
		AssertOnlyKeyedItems (rendered, keyCount: 1);
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Tag!.Artist);
	}

	// Two keys for one field: the getter reads the first, an unedited save keeps both, and a set
	// leaves one.
	[TestMethod]
	[DataRow ("title", "com.apple.quicktime.title", "Title")]
	[DataRow ("MOOD", "MOOD", "Mood")]
	[DataRow ("track", "track", "Track")]
	public void TwoKeysForOneField_FirstIsReadAndASetLeavesOne (string first, string second, string property)
	{
		var isTrack = property == "Track";
		var firstItem = TestBuilders.Mp4.CreateMdtaItem (1, isTrack ? "1/9" : "A");
		var secondItem = TestBuilders.Mp4.CreateMdtaItem (2, isTrack ? "2/9" : "B");
		var data = Keyed ([first, second], firstItem, secondItem);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual (isTrack ? 1u : "A", Get (file.Tag!, property));
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		Set (file.Tag!, property, isTrack ? 5u : "New");
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, firstItem));
		Assert.AreEqual (0, Count (rendered, secondItem));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, isTrack ? "5/9" : "New")));
		Assert.AreEqual (isTrack ? 5u : "New", Get (Mp4File.Read (rendered).File!.Tag!, property));
	}

	[TestMethod]
	public void TwoArtworkKeys_FirstIsReadAndASetLeavesOne ()
	{
		var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
		var data = Keyed (["com.apple.quicktime.artwork", "com.apple.quicktime.artwork"],
			TestBuilders.Mp4.CreateMdtaItem (1, Mp4AtomMapping.TypeJpeg, jpeg),
			TestBuilders.Mp4.CreateMdtaItem (2, Mp4AtomMapping.TypePng, png));

		var file = Mp4File.Read (data).File!;
		Assert.HasCount (1, file.Pictures);
		CollectionAssert.AreEqual (jpeg, file.Pictures[0].PictureData.ToArray ());
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Tag!.Pictures = [new Mp4Picture (png, isJpeg: false)];
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (2, Mp4AtomMapping.TypePng, png)));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, Mp4AtomMapping.TypePng, png)));
	}

	[TestMethod]
	public void UneditedSave_IsByteIdentical ()
	{
		var data = Keyed (["major_brand", "title", "track", "compilation", "MusicBrainz Track Id", "custom", "encoder"],
			TestBuilders.Mp4.CreateMdtaItem (1, "M4A "),
			TestBuilders.Mp4.CreateMdtaItem (2, "T"),
			TestBuilders.Mp4.CreateMdtaItem (3, "3/10"),
			TestBuilders.Mp4.CreateMdtaItem (4, "1"),
			TestBuilders.Mp4.CreateMdtaItem (5, "mbid"),
			TestBuilders.Mp4.CreateMdtaItem (6, "xyz"),
			TestBuilders.Mp4.CreateMdtaItem (7, "Lavf"));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("mbid", file.Tag!.MusicBrainzTrackId);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	// QuickTime and Apple devices put the meta in moov, as a plain atom without version and flags.
	[TestMethod]
	public void MovieMeta_IsReadAndWrittenInPlace ()
	{
		var udtaChild = TestBuilders.Mp4.CreateBox ("©xyz", [0, 5, 0x15, 0xC7, (byte)'+', (byte)'1', (byte)'/', 0, 0]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title", "custom"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "Old-T"), TestBuilders.Mp4.CreateMdtaItem (2, "xyz")], inMovie: true, udtaExtra: udtaChild);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Old-T", file.Title);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
		Assert.AreEqual (1, Count (rendered, "meta"u8), "no second meta was added");
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateBox ("udta", udtaChild)), "udta is unchanged");
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")));
		AssertOnlyKeyedItems (rendered, keyCount: 2);

		var reread = Mp4File.Read (rendered);
		Assert.IsTrue (reread.IsSuccess, reread.Error);
		Assert.AreEqual ("New", reread.File!.Title);
	}

	[TestMethod]
	public void MovieMeta_ReadsWhenUdtaHasNoList ()
	{
		var udtaMeta = TestBuilders.Mp4.CreateBox ("meta", [0, 0, 0, 0]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("title"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "T")], inMovie: true, udtaExtra: udtaMeta);
		Assert.AreEqual ("T", Mp4File.Read (data).File!.Title);
	}

	// A damaged keys atom names no item. The items and the keys atom are kept as they are.
	[TestMethod]
	[DataRow (new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 4, (byte)'m', (byte)'d', (byte)'t', (byte)'a' }, DisplayName = "entry size 4")]
	[DataRow (new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, (byte)'m', (byte)'d', (byte)'t', (byte)'a' }, DisplayName = "entry size 0")]
	[DataRow (new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 99, (byte)'m', (byte)'d', (byte)'t', (byte)'a', (byte)'t' }, DisplayName = "entry past the end")]
	[DataRow (new byte[] { 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 13, (byte)'m', (byte)'d', (byte)'t', (byte)'a', (byte)'t', (byte)'i', (byte)'t', (byte)'l', (byte)'e' }, DisplayName = "fewer entries than the count")]
	[DataRow (new byte[] { 0, 0, 0, 0, 0, 0 }, DisplayName = "no entry count")]
	public void DamagedKeys_DoNotThrowAndAreKept (byte[] keysPayload)
	{
		var item = TestBuilders.Mp4.CreateMdtaItem (1, "T");
		var keysBox = TestBuilders.Mp4.CreateBox ("keys", keysPayload);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", keysPayload, [item], inMovie: false, udtaExtra: null);

		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		var file = result.File!;
		Assert.IsNull (file.Title);

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, item));
		Assert.AreEqual (1, Count (rendered, keysBox));
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	[TestMethod]
	public void MissingKeysAtom_NamesNoItem ()
	{
		var item = TestBuilders.Mp4.CreateMdtaItem (1, "T");
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", null, [item], inMovie: false, udtaExtra: null);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, item));
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	[TestMethod]
	public void DamagedKeys_FromAFile_DoNotThrow ()
	{
		var path = Path.GetTempFileName ();
		try {
			var keys = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 4, (byte)'m', (byte)'d', (byte)'t', (byte)'a' };
			File.WriteAllBytes (path, TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", keys, [TestBuilders.Mp4.CreateMdtaItem (1, "T")], inMovie: false, udtaExtra: null));
			var result = Mp4File.ReadFromFile (path);
			Assert.IsTrue (result.IsSuccess, result.Error);
		} finally {
			File.Delete (path);
		}
	}

	// An item whose type is no key's index has no meaning; a key added at that index must not give it one.
	[TestMethod]
	public void StrayItem_IsKeptUntilANewKeyTakesItsIndex ()
	{
		var stray = TestBuilders.Mp4.CreateMdtaItem (2, "Stray");
		var data = Keyed (["custom"], TestBuilders.Mp4.CreateMdtaItem (1, "xyz"), stray);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), stray));

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, stray));
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	// An item the reader cannot take as its field's value (vinyl "A1", UTF-16 text, no data, an
	// empty artwork item) is kept, and a set of that field replaces it at the same key index.
	[TestMethod]
	[DataRow ("track", "Track", "A1", 5u, "5")]
	[DataRow ("disc", "DiscNumber", "Side B", 2u, "2")]
	[DataRow ("title", "Title", "utf16", "New", "New")]
	[DataRow ("title", "Title", "nodata", "New", "New")]
	public void RejectedValue_IsKeptAndASetReplacesIt (string key, string property, string kind, object value, string written)
	{
		var rejected = kind switch {
			"utf16" => TestBuilders.Mp4.CreateMdtaItem (1, 2, Encoding.BigEndianUnicode.GetBytes ("Old")),
			"nodata" => TestBuilders.Mp4.CreateBox ("\0\0\0\u0001", []),
			_ => TestBuilders.Mp4.CreateMdtaItem (1, kind)
		};
		var data = Keyed ([key, "custom"], rejected, TestBuilders.Mp4.CreateMdtaItem (2, "xyz"));
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (Get (file.Tag!, property));
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		Set (file.Tag!, property, value);
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, rejected));
		CollectionAssert.AreEqual (new uint[] { 1, 2 }, ItemTypes (rendered));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, written)));
		Assert.AreEqual (value, Get (Mp4File.Read (rendered).File!.Tag!, property));
	}

	[TestMethod]
	public void EmptyArtworkItem_IsKeptAndASetReplacesIt ()
	{
		var empty = TestBuilders.Mp4.CreateBox ("\0\0\0\u0001", []);
		var data = Keyed (["com.apple.quicktime.artwork"], empty);
		var file = Mp4File.Read (data).File!;
		Assert.IsEmpty (file.Pictures);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
		file.Tag!.Pictures = [new Mp4Picture (jpeg, isJpeg: true)];
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, empty));
		CollectionAssert.AreEqual (new uint[] { 1 }, ItemTypes (rendered));
	}

	// mutagen edits an ffmpeg file by adding ©nam beside the keyed title. Readers that read
	// iTunes atoms (TagLib#, mutagen, AVFoundation's common title) show the ©nam, which is the
	// newer edit, so it wins; an unedited save keeps both; a set replaces both with one keyed item.
	[TestMethod]
	[DataRow (true)]
	[DataRow (false)]
	public void ItunesAtomBesideKeyedItem_WinsIsKeptAndASetReplacesBoth (bool itunesFirst)
	{
		var keyed = TestBuilders.Mp4.CreateMdtaItem (1, "Keyed");
		var itunes = TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Edited"));
		var data = Keyed (["title", "encoder"], itunesFirst ? [itunes, keyed, Other] : [keyed, itunes, Other]);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Edited", file.Title);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, keyed));
		Assert.AreEqual (0, Count (rendered, itunes));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")));
		AssertOnlyKeyedItems (rendered, keyCount: 2);
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	// A file saved by an older version has iTunes atoms in its mdta meta. An unedited save keeps them.
	[TestMethod]
	public void ItunesAtomsInAnMdtaMeta_AreKeptOnAnUneditedSave ()
	{
		var data = Keyed (["custom"], TestBuilders.Mp4.CreateMdtaItem (1, "xyz"),
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("T")),
			TestBuilders.Mp4.CreateFreeformAtom ("com.apple.iTunes", "MOOD", "Calm"),
			TestBuilders.Mp4.CreateCovrAtom ([0xFF, 0xD8, 0xFF], Mp4AtomMapping.TypeJpeg));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("T", file.Title);
		Assert.AreEqual ("Calm", file.Tag!.Mood);
		Assert.HasCount (1, file.Pictures);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());
	}

	// Freeform fields round-trip by mean and name. A mean other than the name's own is written as "mean:name".
	[TestMethod]
	[DataRow ("com.apple.iTunes", "MYFIELD", "MYFIELD")]
	[DataRow ("org.example", "MusicBrainz Track Id", "org.example:MusicBrainz Track Id")]
	[DataRow ("org.example", "CUSTOM", "org.example:CUSTOM")]
	[DataRow ("com.apple.iTunes", "title", "com.apple.iTunes:title")]
	[DataRow ("com.apple.iTunes", "a.b:c", "com.apple.iTunes:a.b:c")]
	[DataRow ("com.apple.iTunes", "UFIDhttp://www.cddb.com/id3/taginfo1.html", "UFIDhttp://www.cddb.com/id3/taginfo1.html")]
	public void FreeformField_RoundTripsByMeanAndName (string mean, string name, string expectedKey)
	{
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "T"));
		var file = Mp4File.Read (data).File!;
		file.SetFreeformTag (mean, name, "hello");

		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (expectedKey, Keys (rendered)[1]);
		var reread = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("hello", reread.GetFreeformTag (mean, name));
		Assert.AreEqual ("T", reread.Title);
		if (mean != Mp4AtomMapping.MusicBrainzNamespace)
			Assert.IsNull (reread.Tag!.MusicBrainzTrackId);
	}

	// The documented limit: a mean without a dot (or with a space) cannot be told apart from the
	// name, so it comes back as part of the name under com.apple.iTunes.
	[TestMethod]
	[DataRow ("MyApp", "X")]
	[DataRow ("org.example app", "X")]
	public void FreeformField_MeanThatIsNotReverseDns_ComesBackInTheName (string mean, string name)
	{
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "T"));
		var file = Mp4File.Read (data).File!;
		file.SetFreeformTag (mean, name, "hello");

		var reread = Mp4File.Read (file.Render (data).ToArray ()).File!;
		Assert.IsNull (reread.GetFreeformTag (mean, name));
		Assert.AreEqual ("hello", reread.GetFreeformTag (Mp4AtomMapping.AppleNamespace, mean + ":" + name));
	}

	// An unmapped Apple key is not a freeform field; it is kept as it is.
	[TestMethod]
	public void UnmappedAppleKey_IsKeptAndNotFreeform ()
	{
		var make = TestBuilders.Mp4.CreateMdtaItem (1, "Apple");
		var data = Keyed (["com.apple.quicktime.make"], make);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.GetFreeformTag (Mp4AtomMapping.AppleNamespace, "com.apple.quicktime.make"));
		file.Title = "New";
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), make));
	}

	[TestMethod]
	public void UnmappedFfmpegKey_IsAnItunesFreeformField ()
	{
		var data = Keyed (["custom"], TestBuilders.Mp4.CreateMdtaItem (1, "xyz"));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("xyz", file.GetFreeformTag (Mp4AtomMapping.AppleNamespace, "custom"));

		file.SetFreeformTag (Mp4AtomMapping.AppleNamespace, "custom", "new");
		var rendered = file.Render (data).ToArray ();
		CollectionAssert.AreEqual (new uint[] { 1 }, ItemTypes (rendered));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "new")));
	}

	// ffmpeg writes udta/meta; Apple writes moov/meta. QTFF ("Metadata atoms and types"): when user
	// data and metadata declare the same information, the metadata takes precedence. So the
	// fields moov/meta declares are read from it, an unedited save keeps both metas, and a set
	// writes the new value to moov/meta and to udta/meta, so every reader shows it.
	[TestMethod]
	public void BothMetas_MovieMetaWinsForItsFieldsAndASetWritesThere ()
	{
		var encoder = TestBuilders.Mp4.CreateIlstChild ("©too", 1, Encoding.UTF8.GetBytes ("Lavf"));
		var userTitle = TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("UserT"));
		var userAlbum = TestBuilders.Mp4.CreateIlstChild ("©alb", 1, Encoding.UTF8.GetBytes ("UserAl"));
		var appleTitle = TestBuilders.Mp4.CreateMdtaItem (1, "AppleT");
		var appleArtist = TestBuilders.Mp4.CreateMdtaItem (2, "AppleA");
		var udta = TestBuilders.Mp4.CreateBox ("meta", [
			0, 0, 0, 0,
			.. TestBuilders.Mp4.CreateBox ("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. "mdirappl"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
			.. TestBuilders.Mp4.CreateBox ("ilst", [.. encoder, .. userTitle, .. userAlbum])]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title", "com.apple.quicktime.artist"),
			[appleTitle, appleArtist], inMovie: true, udtaExtra: udta);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("AppleT", file.Title);
		Assert.AreEqual ("AppleA", file.Tag!.Artist);
		Assert.AreEqual ("UserAl", file.Tag.Album);
		Assert.AreEqual ("Lavf", file.Tag.EncoderSettings);

		// An unedited save keeps every item; an iTunes ilst may be written in another order.
		var unedited = file.Render (data).ToArray ();
		Assert.HasCount (data.Length, unedited);
		foreach (var item in new[] { encoder, userTitle, userAlbum, appleTitle, appleArtist })
			Assert.AreEqual (1, Count (unedited, item));

		file.Title = "New";
		file.Tag.Album = "NewAl";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, appleTitle));
		Assert.AreEqual (0, Count (rendered, userTitle));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")), "the title is written to moov/meta");
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("New"))), "and to udta/meta");
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateIlstChild ("©alb", 1, Encoding.UTF8.GetBytes ("NewAl"))), "the album stays in udta/meta");
		Assert.AreEqual (1, Count (rendered, appleArtist));
		Assert.AreEqual (1, Count (rendered, encoder));
		var reread = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("New", reread.Title);
		Assert.AreEqual ("AppleA", reread.Tag!.Artist);
		Assert.AreEqual ("NewAl", reread.Tag.Album);

		file.Tag.Artist = null;
		var cleared = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (cleared, appleArtist));
		Assert.AreEqual (0, Count (cleared, "©ART"u8));
		Assert.IsNull (Mp4File.Read (cleared).File!.Tag!.Artist);
	}

	// A field that lives in moov/meta stays there when it is cleared and set again.
	[TestMethod]
	public void BothMetas_ClearedThenSetFieldStaysInMovieMeta ()
	{
		var udta = TestBuilders.Mp4.CreateBox ("meta", [
			0, 0, 0, 0,
			.. TestBuilders.Mp4.CreateBox ("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. "mdirappl"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
			.. TestBuilders.Mp4.CreateBox ("ilst", TestBuilders.Mp4.CreateIlstChild ("©too", 1, Encoding.UTF8.GetBytes ("Lavf")))]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "AppleT")], inMovie: true, udtaExtra: udta);

		var file = Mp4File.Read (data).File!;
		file.Title = null;
		file.Title = "Again";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "Again")));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Again"))));
	}

	// An iTunes atom in moov/meta's keyed ilst has no key. A set gives it one, named the way moov/meta names its keys.
	[TestMethod]
	public void BothMetas_FieldWithoutKeyInMovieMetaGetsOne ()
	{
		var udta = TestBuilders.Mp4.CreateBox ("meta", [
			0, 0, 0, 0,
			.. TestBuilders.Mp4.CreateBox ("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. "mdirappl"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
			.. TestBuilders.Mp4.CreateBox ("ilst", TestBuilders.Mp4.CreateIlstChild ("©too", 1, Encoding.UTF8.GetBytes ("Lavf")))]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.artist"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "AppleA"), TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("MovieT"))],
			inMovie: true, udtaExtra: udta);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("MovieT", file.Title);
		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("MovieT"))));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("New"))), "udta/meta gets it too");
		CollectionAssert.AreEqual (ArtistThenTitle, Keys (rendered));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (2, "New")));
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	// The set rewrites the item at moov/meta's own key, even when that key is not the name a new key would get.
	[TestMethod]
	public void BothMetas_SetRewritesMovieMetaAtItsOwnKey ()
	{
		var udta = TestBuilders.Mp4.CreateBox ("meta", [
			0, 0, 0, 0,
			.. TestBuilders.Mp4.CreateBox ("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. "mdirappl"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
			.. TestBuilders.Mp4.CreateBox ("ilst", TestBuilders.Mp4.CreateIlstChild ("©too", 1, Encoding.UTF8.GetBytes ("Lavf")))]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.displayname"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "Shown")], inMovie: true, udtaExtra: udta);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Shown", file.Title);
		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		CollectionAssert.AreEqual (DisplayNameOnly, Keys (rendered));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")));
	}

	// In moov/meta a keyed item beats an iTunes atom beside it: AVFoundation reads only the keyed
	// item there. In udta/meta the iTunes atom (the newer, lenient edit) wins, as before.
	[TestMethod]
	[DataRow (true, "Keyed")]
	[DataRow (false, "Itunes")]
	public void KeyedItemBesideItunesAtom_WinnerDependsOnTheMeta (bool inMovie, string expected)
	{
		var keyed = TestBuilders.Mp4.CreateMdtaItem (1, "Keyed");
		var itunes = TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Itunes"));
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title"),
			[itunes, keyed], inMovie, udtaExtra: null);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual (expected, file.Title);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, keyed));
		Assert.AreEqual (0, Count (rendered, itunes));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "New")));
	}

	// With both metas, moov/meta's keyed value beats an iTunes atom in moov/meta and the udta/meta value.
	[TestMethod]
	public void BothMetas_KeyedMovieItemBeatsMovieItunesAtomAndUdta ()
	{
		var udta = UdtaMeta (TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("UdtaT")));
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title"),
			[TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("InjT")), TestBuilders.Mp4.CreateMdtaItem (1, "AppleT")],
			inMovie: true, udtaExtra: udta);
		Assert.AreEqual ("AppleT", Mp4File.Read (data).File!.Title);
	}

	// Items of moov/meta that name no field (Apple's make, model, location) are kept beside
	// udta/meta on every save, and Clear removes them with everything else.
	[TestMethod]
	public void BothMetas_UnmappedMovieItemsAreKeptAndClearRemovesThem ()
	{
		var make = TestBuilders.Mp4.CreateMdtaItem (2, "Apple");
		var location = TestBuilders.Mp4.CreateMdtaItem (3, "+37.3349-122.0090/");
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta",
			TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title", "com.apple.quicktime.make", "com.apple.quicktime.location.ISO6709"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "AppleT"), make, location], inMovie: true,
			udtaExtra: UdtaMeta (TestBuilders.Mp4.CreateIlstChild ("©too", 1, Encoding.UTF8.GetBytes ("Lavf"))));

		var file = Mp4File.Read (data).File!;
		file.Title = "New";
		file.Tag!.Album = "NewAl";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, make));
		Assert.AreEqual (1, Count (rendered, location));

		file.Tag.Clear ();
		file.Title = "Fresh";
		var cleared = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (cleared, make));
		Assert.AreEqual (0, Count (cleared, location));
		Assert.AreEqual (-1, cleared.AsSpan ().IndexOf ("AppleT"u8));
		var reread = Mp4File.Read (cleared).File!;
		Assert.AreEqual ("Fresh", reread.Title);
		Assert.IsNull (reread.Tag!.Album);
	}

	// A malformed ---- in an mdta meta keeps its mean and name, so setting that field drops it.
	[TestMethod]
	public void MalformedFreeformInAnMdtaMeta_IsDroppedWhenItsFieldIsSet ()
	{
		var malformed = TestBuilders.Mp4.CreateBox ("----", [
			.. TestBuilders.Mp4.CreateBox ("mean", [0, 0, 0, 0, .. "com.apple.iTunes"u8]),
			.. TestBuilders.Mp4.CreateBox ("name", [0, 0, 0, 0, .. "MOOD"u8])]);
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "T"), malformed);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Tag!.Mood);
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Tag.Mood = "Calm";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (0, Count (rendered, malformed));
		Assert.AreEqual ("Calm", Mp4File.Read (rendered).File!.Tag!.Mood);
	}

	// Two iTunes atoms for one field in an mdta meta: the last one is read, as in an iTunes ilst.
	[TestMethod]
	public void TwoItunesAtomsInAnMdtaMeta_LastOneIsRead ()
	{
		var data = Keyed (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "Keyed"),
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("First")),
			TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Last")));
		Assert.AreEqual ("Last", Mp4File.Read (data).File!.Title);
	}

	static byte[] UdtaMeta (params byte[][] items) => TestBuilders.Mp4.CreateBox ("meta", [
		0, 0, 0, 0,
		.. TestBuilders.Mp4.CreateBox ("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. "mdirappl"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
		.. TestBuilders.Mp4.CreateBox ("ilst", [.. items.SelectMany (item => item)])]);

	// A moov/meta whose handler is not "mdta" is not QuickTime metadata: AVFoundation, ExifTool,
	// TagLib# and mutagen read the udta/meta value. Its items are kept as they are and name no field.
	[TestMethod]
	public void BothMetas_ItunesMovieMetaIsKeptAndNotRead ()
	{
		var movieTitle = TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("MovieMdir"));
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdir", null, [movieTitle], inMovie: true,
			udtaExtra: UdtaMeta (TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("UdtaT"))));

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("UdtaT", file.Title);

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, movieTitle));
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("New"))));
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	// A tag assigned in place of the one read replaces every value readers see: moov/meta's fields
	// go (its other items stay), and the assigned values are written.
	[TestMethod]
	public void BothMetas_AssignedTagReplacesMovieMetaFields ()
	{
		var make = TestBuilders.Mp4.CreateMdtaItem (2, "Apple");
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title", "com.apple.quicktime.make"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "AppleT"), make], inMovie: true,
			udtaExtra: UdtaMeta (TestBuilders.Mp4.CreateIlstChild ("©too", 1, Encoding.UTF8.GetBytes ("Lavf"))));

		var file = Mp4File.Read (data).File!;
		file.Tag = new Mp4Tag { Title = "X" };
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("AppleT"u8));
		Assert.AreEqual (1, Count (rendered, make));
		var reread = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("X", reread.Title);
		Assert.IsNull (reread.Tag!.EncoderSettings);
	}

	// Copying a tag read from both metas to another file writes every value it reports.
	[TestMethod]
	public void BothMetas_CopiedTagCarriesEveryValue ()
	{
		var source = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("com.apple.quicktime.title"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "AppleT")], inMovie: true,
			udtaExtra: UdtaMeta (
				TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Shadowed")),
				TestBuilders.Mp4.CreateIlstChild ("©alb", 1, Encoding.UTF8.GetBytes ("UdtaAl"))));
		var target = TestBuilders.Mp4.CreateWithIlstAtoms (TestBuilders.Mp4.CreateIlstChild ("©nam", 1, Encoding.UTF8.GetBytes ("Old")));

		var from = Mp4File.Read (source).File!;
		var to = Mp4File.Read (target).File!;
		Assert.AreEqual ("AppleT", from.Title);
		to.Tag = from.Tag;
		var rendered = to.Render (target).ToArray ();
		var reread = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("AppleT", reread.Title);
		Assert.AreEqual ("UdtaAl", reread.Tag!.Album);
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Shadowed"u8));
	}

	static byte[] NameType () => [0xA9, (byte)'n', (byte)'a', (byte)'m'];

	// mutagen pads the meta with a free box of zeros. It goes back as it was, not rebuilt from what
	// its zeros parse as.
	[TestMethod]
	[DataRow ("mdta")]
	[DataRow ("mdir")]
	public void FreePaddingInMeta_IsKeptByteForByte (string handler)
	{
		var padding = TestBuilders.Mp4.CreateBox ("free", new byte[64]);
		var data = TestBuilders.Mp4.CreateWithKeyedMeta (handler, TestBuilders.Mp4.KeysPayload ("title"),
			[TestBuilders.Mp4.CreateMdtaItem (1, "T")], inMovie: false, udtaExtra: null, metaTail: padding);
		var file = Mp4File.Read (data).File!;
		CollectionAssert.AreEqual (data, file.Render (data).ToArray ());

		file.Tag!.Artist = "A";
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), padding));
	}

	// The types of the first ilst's items.
	static uint[] ItemTypes (byte[] file)
	{
		var ilst = Find (file, "ilst"u8);
		var types = new List<uint> ();
		var offset = 0;
		while (offset + 8 <= ilst.Length) {
			types.Add (BinaryPrimitives.ReadUInt32BigEndian (ilst.Slice (offset + 4, 4)));
			offset += (int)BinaryPrimitives.ReadUInt32BigEndian (ilst.Slice (offset, 4));
		}

		return [.. types];
	}

	static byte[] Keyed (string[] keys, params byte[][] items) =>
		TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload (keys), items, inMovie: false, udtaExtra: null);

	static object? Get (Mp4Tag tag, string property) => typeof (Mp4Tag).GetProperty (property)!.GetValue (tag);

	static void Set (Mp4Tag tag, string property, object? value) => typeof (Mp4Tag).GetProperty (property)!.SetValue (tag, value);

	// Every ilst item type must be a key index (1..keyCount).
	internal static void AssertOnlyKeyedItems (byte[] file, int keyCount)
	{
		var ilst = Find (file, "ilst"u8);
		var offset = 0;
		while (offset + 8 <= ilst.Length) {
			var size = (int)BinaryPrimitives.ReadUInt32BigEndian (ilst.Slice (offset, 4));
			var type = BinaryPrimitives.ReadUInt32BigEndian (ilst.Slice (offset + 4, 4));
			Assert.IsTrue (type >= 1 && type <= keyCount, $"ilst item type 0x{type:X8} is not a key index (1..{keyCount})");
			offset += size;
		}
	}

	static List<string> Keys (byte[] file)
	{
		var keys = Find (file, "keys"u8);
		var count = BinaryPrimitives.ReadUInt32BigEndian (keys.Slice (4, 4));
		var names = new List<string> ();
		var offset = 8;
		for (var i = 0; i < count; i++) {
			var size = (int)BinaryPrimitives.ReadUInt32BigEndian (keys.Slice (offset, 4));
			var ns = Encoding.Latin1.GetString (keys.Slice (offset + 4, 4));
			var value = ns == "mdta" ? Encoding.UTF8.GetString (keys.Slice (offset + 8, size - 8)) : Encoding.Latin1.GetString (keys.Slice (offset + 8, size - 8));
			names.Add (ns == "mdta" ? value : ns + ":" + value);
			offset += size;
		}

		return names;
	}

	// The payload of the first box of this type.
	static ReadOnlySpan<byte> Find (byte[] file, ReadOnlySpan<byte> type)
	{
		var at = file.AsSpan ().IndexOf (type);
		Assert.IsGreaterThanOrEqualTo (4, at, "box missing");
		var size = (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at - 4, 4));
		return file.AsSpan (at + 4, size - 8);
	}

	static int Count (ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
	{
		var count = 0;
		var start = 0;
		while (start < data.Length) {
			var at = data.Slice (start).IndexOf (pattern);
			if (at < 0)
				break;
			count++;
			start += at + pattern.Length;
		}

		return count;
	}
}
