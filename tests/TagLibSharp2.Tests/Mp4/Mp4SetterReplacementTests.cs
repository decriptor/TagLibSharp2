// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Setting a property replaces every item its getter reads, so a reader that shows all of
/// them (ffprobe joins them with ';') sees only the new value.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4SetterReplacementTests
{
	const string QuickTime = "com.apple.quicktime.";
	static readonly byte[] Location = TestBuilders.Mp4.CreateMdtaItem (2, "+12.3456-065.4321/");

	// Properties whose getter reads an iTunes atom, a legacy alias, and the QuickTime mdta key.
	[TestMethod]
	[DataRow ("Title", "©nam", "title")]
	[DataRow ("Artist", "©ART", "artist")]
	[DataRow ("Album", "©alb", "album")]
	[DataRow ("Comment", "©cmt", "comment")]
	[DataRow ("Year", "©day", "year")]
	[DataRow ("Description", "desc", "description")]
	[DataRow ("Publisher", "©pub", "publisher")]
	[DataRow ("Genre", "©gen", "genre")]
	[DataRow ("Copyright", "cprt", "copyright")]
	public void Setter_ReplacesEveryItemItsGetterReads (string property, string atom, string mdtaKey)
	{
		var items = new List<byte[]> {
			TestBuilders.Mp4.CreateMdtaItem (1, "Old-mdta"),
			Location,
			Text (atom, "Old-itunes")
		};
		if (property == "Genre")
			items.Add (TestBuilders.Mp4.CreateIlstChild ("gnre", 0, [0x00, 0x4A]));
		if (property == "Copyright")
			items.Add (Text ("©cpy", "Old-alias"));
		var data = TestBuilders.Mp4.CreateWithMdtaIlst ([QuickTime + mdtaKey, QuickTime + "location.ISO6709"], [.. items]);

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Old-itunes", Get (file.Tag!, property));

		Set (file.Tag!, property, "New");
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8), "an old value is still written");
		Assert.AreEqual (0, Count (rendered, "gnre"u8));
		Assert.AreEqual (1, Count (rendered, Location));
		Assert.AreEqual ("New", Get (Mp4File.Read (rendered).File!.Tag!, property));

		Set (file.Tag!, property, null);
		var cleared = file.Render (data).ToArray ();
		Assert.AreEqual (-1, cleared.AsSpan ().IndexOf ("Old-"u8), "an old value is still written");
		Assert.AreEqual (-1, cleared.AsSpan ().IndexOf ("New"u8));
		Assert.AreEqual (1, Count (cleared, Location));
		Assert.IsNull (Get (Mp4File.Read (cleared).File!.Tag!, property));
	}

	// An mdta item is read when no iTunes atom has the value. Apple names the keys
	// com.apple.quicktime.*; ffmpeg's use_metadata_tags writes the bare names.
	[TestMethod]
	[DataRow ("Title", "com.apple.quicktime.title")]
	[DataRow ("Title", "title")]
	[DataRow ("Artist", "artist")]
	[DataRow ("Album", "com.apple.quicktime.album")]
	[DataRow ("Comment", "comment")]
	[DataRow ("Year", "com.apple.quicktime.year")]
	[DataRow ("Description", "description")]
	[DataRow ("Publisher", "com.apple.quicktime.publisher")]
	[DataRow ("Genre", "genre")]
	[DataRow ("Copyright", "com.apple.quicktime.copyright")]
	public void Getter_ReadsQuickTimeKey (string property, string key)
	{
		var data = TestBuilders.Mp4.CreateWithMdtaIlst ([key], TestBuilders.Mp4.CreateMdtaItem (1, "From mdta"));

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("From mdta", Get (file.Tag!, property));

		// An unedited save keeps the item as it was.
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, TestBuilders.Mp4.CreateMdtaItem (1, "From mdta")));
	}

	[TestMethod]
	public void Getter_IgnoresKeyIndexItemsOutsideAnMdtaHandler ()
	{
		// In an iTunes (mdir) meta, a numeric type is just an unknown atom, even beside a keys atom.
		var item = TestBuilders.Mp4.CreateMdtaItem (1, "Not a title");
		var data = TestBuilders.Mp4.CreateWithKeyedIlst ("mdir", ["title"], item);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);

		file.Title = "New";
		Assert.AreEqual (1, Count (file.Render (data).ToArray (), item));
	}

	[TestMethod]
	public void Getter_IgnoresUnmappedAndOutOfRangeKeys ()
	{
		byte[][] items = [
			TestBuilders.Mp4.CreateMdtaItem (1, "A"),
			TestBuilders.Mp4.CreateMdtaItem (2, "B"),
			TestBuilders.Mp4.CreateMdtaItem (3, "C")];
		var data = TestBuilders.Mp4.CreateWithMdtaIlst (["com.apple.quicktime.title.extra", "custom"], items);
		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);

		file.Title = "New";
		var rendered = file.Render (data).ToArray ();
		foreach (var item in items)
			Assert.AreEqual (1, Count (rendered, item));
	}

	[TestMethod]
	public void Clear_RemovesQuickTimeItems ()
	{
		var data = TestBuilders.Mp4.CreateWithMdtaIlst (["title"], TestBuilders.Mp4.CreateMdtaItem (1, "Old-mdta"));
		var file = Mp4File.Read (data).File!;
		file.Tag!.Clear ();
		Assert.IsNull (file.Title);
		file.Title = "New";

		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
		Assert.AreEqual ("New", Mp4File.Read (rendered).File!.Title);
	}

	// Properties whose getter reads one iTunes atom. A malformed atom of the same type is kept on
	// other saves, so the setter must drop it too.
	[TestMethod]
	[DataRow ("AlbumArtist", "aART")]
	[DataRow ("Composer", "©wrt")]
	[DataRow ("Grouping", "©grp")]
	[DataRow ("Lyrics", "©lyr")]
	[DataRow ("EncoderSettings", "©too")]
	[DataRow ("EncodedBy", "©enc")]
	[DataRow ("AlbumSort", "soal")]
	[DataRow ("ArtistSort", "soar")]
	[DataRow ("TitleSort", "sonm")]
	[DataRow ("AlbumArtistSort", "soaa")]
	[DataRow ("ComposerSort", "soco")]
	[DataRow ("Work", "©wrk")]
	[DataRow ("Movement", "©mvn")]
	[DataRow ("PodcastFeedUrl", "purl")]
	[DataRow ("PodcastEpisodeGuid", "egid")]
	[DataRow ("PodcastCategory", "catg")]
	[DataRow ("PodcastKeywords", "keyw")]
	public void TextSetter_ReplacesItsAtomAndMalformedTwin (string property, string atom)
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			Text (atom, "Old-value"),
			TestBuilders.Mp4.CreateBox (atom, [0x01, 0x02, 0x03, 0x04]));

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Old-value", Get (file.Tag!, property));

		Set (file.Tag!, property, "New");
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, Type (atom)));
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
		Assert.AreEqual ("New", Get (Mp4File.Read (rendered).File!.Tag!, property));

		Set (file.Tag!, property, null);
		Assert.AreEqual (0, Count (file.Render (data).ToArray (), Type (atom)));
	}

	[TestMethod]
	[DataRow ("GaplessInfo", "com.apple.iTunes", "iTunSMPB")]
	[DataRow ("MusicBrainzTrackId", "org.musicbrainz", "MusicBrainz Track Id")]
	[DataRow ("MusicBrainzReleaseId", "org.musicbrainz", "MusicBrainz Album Id")]
	[DataRow ("MusicBrainzArtistId", "org.musicbrainz", "MusicBrainz Artist Id")]
	[DataRow ("MusicBrainzAlbumArtistId", "org.musicbrainz", "MusicBrainz Album Artist Id")]
	[DataRow ("MusicBrainzReleaseGroupId", "org.musicbrainz", "MusicBrainz Release Group Id")]
	[DataRow ("MusicBrainzWorkId", "org.musicbrainz", "MusicBrainz Work Id")]
	[DataRow ("MusicBrainzRecordingId", "org.musicbrainz", "MusicBrainz Recording Id")]
	[DataRow ("MusicBrainzDiscId", "org.musicbrainz", "MusicBrainz Disc Id")]
	[DataRow ("MusicBrainzReleaseStatus", "org.musicbrainz", "MusicBrainz Album Status")]
	[DataRow ("MusicBrainzReleaseType", "org.musicbrainz", "MusicBrainz Album Type")]
	[DataRow ("MusicBrainzReleaseCountry", "org.musicbrainz", "MusicBrainz Album Release Country")]
	[DataRow ("Isrc", "com.apple.iTunes", "ISRC")]
	[DataRow ("Conductor", "com.apple.iTunes", "CONDUCTOR")]
	[DataRow ("OriginalReleaseDate", "com.apple.iTunes", "ORIGINAL YEAR")]
	[DataRow ("ReplayGainTrackGain", "com.apple.iTunes", "replaygain_track_gain")]
	[DataRow ("ReplayGainTrackPeak", "com.apple.iTunes", "replaygain_track_peak")]
	[DataRow ("ReplayGainAlbumGain", "com.apple.iTunes", "replaygain_album_gain")]
	[DataRow ("ReplayGainAlbumPeak", "com.apple.iTunes", "replaygain_album_peak")]
	[DataRow ("R128TrackGain", "com.apple.iTunes", "R128_TRACK_GAIN")]
	[DataRow ("R128AlbumGain", "com.apple.iTunes", "R128_ALBUM_GAIN")]
	[DataRow ("AcoustIdId", "com.apple.iTunes", "Acoustid Id")]
	[DataRow ("AcoustIdFingerprint", "com.apple.iTunes", "Acoustid Fingerprint")]
	[DataRow ("InitialKey", "com.apple.iTunes", "initialkey")]
	[DataRow ("Remixer", "com.apple.iTunes", "REMIXER")]
	[DataRow ("Mood", "com.apple.iTunes", "MOOD")]
	[DataRow ("Subtitle", "com.apple.iTunes", "SUBTITLE")]
	[DataRow ("Barcode", "com.apple.iTunes", "BARCODE")]
	[DataRow ("CatalogNumber", "com.apple.iTunes", "CATALOGNUMBER")]
	[DataRow ("AmazonId", "com.apple.iTunes", "ASIN")]
	[DataRow ("DateTagged", "com.apple.iTunes", "date_tagged")]
	[DataRow ("Language", "com.apple.iTunes", "LANGUAGE")]
	[DataRow ("MediaType", "com.apple.iTunes", "MEDIA")]
	public void FreeformSetter_ReplacesItsFieldAndEmptyTwin (string property, string mean, string name)
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateFreeformAtom (mean, name, "Old-value"),
			FreeformWithoutData (mean, name));

		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Old-value", Get (file.Tag!, property));

		Set (file.Tag!, property, "New");
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, "----"u8));
		Assert.AreEqual (-1, rendered.AsSpan ().IndexOf ("Old-"u8));
		Assert.AreEqual ("New", Get (Mp4File.Read (rendered).File!.Tag!, property));

		Set (file.Tag!, property, null);
		Assert.AreEqual (0, Count (file.Render (data).ToArray (), "----"u8));
	}

	[TestMethod]
	[DataRow ("BeatsPerMinute", "tmpo", 120u)]
	[DataRow ("MovementNumber", "©mvi", 2u)]
	[DataRow ("MovementTotal", "©mvc", 4u)]
	[DataRow ("Track", "trkn", 3u)]
	[DataRow ("DiscNumber", "disk", 1u)]
	public void NumberSetter_ReplacesItsAtomAndMalformedTwin (string property, string atom, uint value)
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateBox (atom, [0x01, 0x02, 0x03, 0x04]));

		var file = Mp4File.Read (data).File!;
		Set (file.Tag!, property, value);
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, Type (atom)));
		Assert.AreEqual (value, Get (Mp4File.Read (rendered).File!.Tag!, property));

		Set (file.Tag!, property, null);
		Assert.AreEqual (0, Count (file.Render (data).ToArray (), Type (atom)));
	}

	[TestMethod]
	[DataRow ("IsCompilation", "cpil")]
	[DataRow ("IsGapless", "pgap")]
	[DataRow ("IsPodcast", "pcst")]
	public void FlagSetter_ReplacesItsAtomAndMalformedTwin (string property, string atom)
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (
			TestBuilders.Mp4.CreateBox (atom, [0x01, 0x02, 0x03, 0x04]));

		var file = Mp4File.Read (data).File!;
		Set (file.Tag!, property, true);
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, Type (atom)));
		Assert.AreEqual (true, Get (Mp4File.Read (rendered).File!.Tag!, property));

		Set (file.Tag!, property, false);
		Assert.AreEqual (0, Count (file.Render (data).ToArray (), Type (atom)));
	}

	[TestMethod]
	public void ContentRatingSetter_ReplacesItsAtomAndMalformedTwin ()
	{
		var data = TestBuilders.Mp4.CreateWithIlstAtoms (TestBuilders.Mp4.CreateBox ("rtng", [0x01, 0x02, 0x03, 0x04]));

		var file = Mp4File.Read (data).File!;
		file.Tag!.ContentRating = Mp4ContentRating.Explicit;
		var rendered = file.Render (data).ToArray ();
		Assert.AreEqual (1, Count (rendered, "rtng"u8));
		Assert.AreEqual (Mp4ContentRating.Explicit, Mp4File.Read (rendered).File!.Tag!.ContentRating);

		file.Tag.ContentRating = Mp4ContentRating.None;
		Assert.AreEqual (0, Count (file.Render (data).ToArray (), "rtng"u8));
	}

	static object? Get (Mp4Tag tag, string property) => typeof (Mp4Tag).GetProperty (property)!.GetValue (tag);

	static void Set (Mp4Tag tag, string property, object? value) => typeof (Mp4Tag).GetProperty (property)!.SetValue (tag, value);

	static byte[] Text (string atom, string value) => TestBuilders.Mp4.CreateIlstChild (atom, 1, Encoding.UTF8.GetBytes (value));

	static byte[] Type (string atom) => Encoding.Latin1.GetBytes (atom);

	static byte[] FreeformWithoutData (string mean, string name)
	{
		static byte[] Named (string type, string value) =>
			TestBuilders.Mp4.CreateBox (type, [0, 0, 0, 0, .. Encoding.UTF8.GetBytes (value)]);

		return TestBuilders.Mp4.CreateBox ("----", [.. Named ("mean", mean), .. Named ("name", name)]);
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
