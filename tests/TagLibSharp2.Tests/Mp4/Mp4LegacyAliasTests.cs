// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Some fields can be stored under more than one item: the ID3v1-coded genre (gnre) next to ©gen,
/// ©cpy next to cprt, and the key-indexed items of QuickTime metadata (a keys atom names each ilst item
/// by its 1-based index). Setting a property must leave exactly one value that readers show: the setter
/// removes or rewrites every item its getter reads.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4LegacyAliasTests
{
	static readonly byte AcidPunk = (byte)(Id3v1Genre.GetIndex ("Acid Punk") + 1);

	[TestMethod]
	public void Genre_GnreOnly_ReadsTheId3v1Name ()
	{
		var data = WithItems (Item ("gnre", 0, [0, AcidPunk]));

		Assert.AreEqual ("Acid Punk", Mp4File.Read (data).File!.Tag!.Genre);
	}

	[TestMethod]
	public void Genre_TextWinsOverGnre ()
	{
		var data = WithItems (Item ("gnre", 0, [0, AcidPunk]), Text ("©gen", "Rock"));

		Assert.AreEqual ("Rock", Mp4File.Read (data).File!.Tag!.Genre);
	}

	[TestMethod]
	[DataRow (0, DisplayName = "gnre 0 is no genre")]
	[DataRow (200, DisplayName = "gnre past the list")]
	[DataRow (257, DisplayName = "gnre past a byte, which would wrap to Blues")]
	public void Genre_GnreWithoutAName_IsNull (int code)
	{
		var data = WithItems (Item ("gnre", 0, [(byte)(code >> 8), (byte)code]));

		Assert.IsNull (Mp4File.Read (data).File!.Tag!.Genre);
	}

	[TestMethod]
	public void Genre_Set_LeavesOneGenreItem ()
	{
		var data = WithItems (Item ("gnre", 0, [0, AcidPunk]));
		var file = Mp4File.Read (data).File!;

		file.Tag!.Genre = "Jazz";
		var saved = file.Render (data).ToArray ();

		Assert.AreEqual (-1, IndexOf (saved, "gnre"u8.ToArray ()), "the ID3v1 genre would still show");
		Assert.AreEqual ("Jazz", Mp4File.Read (saved).File!.Tag!.Genre);
		Assert.AreEqual ("Jazz", Mp4File.Read (saved).File!.Tag!.Genres.Single ());

		var cleared = Mp4File.Read (saved).File!;
		cleared.Tag!.Genre = null;
		Assert.IsNull (Mp4File.Read (cleared.Render (saved).Span).File!.Tag?.Genre);
	}

	[TestMethod]
	public void Genre_SetNullOnGnreOnly_RemovesIt ()
	{
		var data = WithItems (Item ("gnre", 0, [0, AcidPunk]), Text ("©nam", "T"));
		var file = Mp4File.Read (data).File!;

		file.Tag!.Genre = null;

		Assert.IsNull (file.Tag.Genre);
		Assert.IsNull (Mp4File.Read (file.Render (data).Span).File!.Tag!.Genre);
	}

	[TestMethod]
	public void Copyright_CpyOnly_IsRead ()
	{
		var data = WithItems (Text ("©cpy", "Old (c)"));

		Assert.AreEqual ("Old (c)", Mp4File.Read (data).File!.Tag!.Copyright);
	}

	[TestMethod]
	public void Copyright_Set_LeavesOneCopyrightItem ()
	{
		var data = WithItems (Text ("©cpy", "Old (c)"), Text ("cprt", "Older (c)"));
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("Older (c)", file.Tag!.Copyright);

		file.Tag.Copyright = "New (c)";
		var saved = file.Render (data).ToArray ();

		Assert.AreEqual (-1, IndexOf (saved, Encoding.Latin1.GetBytes ("©cpy")));
		Assert.AreEqual (-1, IndexOf (saved, "Old (c)"u8.ToArray ()));
		Assert.AreEqual ("New (c)", Mp4File.Read (saved).File!.Tag!.Copyright);
	}

	[TestMethod]
	public void QuickTimeKeys_NamedItems_ReadThroughTheirProperties ()
	{
		var data = WithKeyedItems (
			("title", "T"), ("com.apple.quicktime.artist", "A"), ("COM.APPLE.QUICKTIME.GENRE", "G"), ("custom", "xyz"));

		var tag = Mp4File.Read (data).File!.Tag!;

		Assert.AreEqual ("T", tag.Title);
		Assert.AreEqual ("A", tag.Artist);
		Assert.AreEqual ("G", tag.Genre);
		Assert.IsNull (tag.Album);
	}

	[TestMethod]
	public void QuickTimeKeys_Set_RewritesTheKeyedItemAndAddsNoSecondOne ()
	{
		var data = WithKeyedItems (("title", "T"), ("custom", "xyz"));
		var file = Mp4File.Read (data).File!;

		file.Tag!.Title = "rv-title";
		var saved = file.Render (data).ToArray ();

		Assert.AreEqual (-1, IndexOf (saved, Encoding.Latin1.GetBytes ("©nam")), "a second title item would show beside the keyed one");
		var items = ItemCodes (saved);
		CollectionAssert.AreEquivalent (new[] { Code (1), Code (2) }, items);
		var reread = Mp4File.Read (saved).File!.Tag!;
		Assert.AreEqual ("rv-title", reread.Title);
		Assert.IsTrue (IndexOf (saved, "xyz"u8.ToArray ()) >= 0, "the unnamed keyed item survives");

		reread.Title = null;
		var cleared = Mp4File.Read (saved).File!;
		cleared.Tag!.Title = null;
		var clearedBytes = cleared.Render (saved).ToArray ();
		Assert.IsNull (Mp4File.Read (clearedBytes).File!.Tag!.Title);
		CollectionAssert.AreEquivalent (new[] { Code (2) }, ItemCodes (clearedBytes));
	}

	[TestMethod]
	public void QuickTimeKeys_EveryAliasedTextProperty_LeavesOneValue ()
	{
		var properties = new (string Key, Action<Mp4Tag, string?> Set, Func<Mp4Tag, string?> Get)[] {
			("title", (t, v) => t.Title = v, t => t.Title),
			("displayname", (t, v) => t.Title = v, t => t.Title),
			("artist", (t, v) => t.Artist = v, t => t.Artist),
			("album", (t, v) => t.Album = v, t => t.Album),
			("comment", (t, v) => t.Comment = v, t => t.Comment),
			("genre", (t, v) => t.Genre = v, t => t.Genre),
			("copyright", (t, v) => t.Copyright = v, t => t.Copyright),
			("description", (t, v) => t.Description = v, t => t.Description),
			("year", (t, v) => t.Year = v, t => t.Year),
			("date", (t, v) => t.Year = v, t => t.Year),
			("composer", (t, v) => t.Composer = v, t => t.Composer),
			("album_artist", (t, v) => t.AlbumArtist = v, t => t.AlbumArtist),
			("publisher", (t, v) => t.Publisher = v, t => t.Publisher),
			("keywords", (t, v) => t.PodcastKeywords = v, t => t.PodcastKeywords),
			("encoder", (t, v) => t.EncoderSettings = v, t => t.EncoderSettings),
			("software", (t, v) => t.EncoderSettings = v, t => t.EncoderSettings),
			("grouping", (t, v) => t.Grouping = v, t => t.Grouping),
			("lyrics", (t, v) => t.Lyrics = v, t => t.Lyrics),
		};

		foreach (var (key, set, get) in properties) {
			var data = WithKeyedItems (("com.apple.quicktime." + key, "old"));
			var file = Mp4File.Read (data).File!;
			Assert.AreEqual ("old", get (file.Tag!), key);

			set (file.Tag!, "new");
			var saved = file.Render (data).ToArray ();

			Assert.AreEqual ("new", get (Mp4File.Read (saved).File!.Tag!), key);
			Assert.AreEqual (-1, IndexOf (saved, "old"u8.ToArray ()), key);
			CollectionAssert.AreEqual (new[] { Code (1) }, ItemCodes (saved), key);
		}
	}

	[TestMethod]
	public void QuickTimeKeys_SeveralItemsForOneField_LeaveOne ()
	{
		// Two keys name the title, and an iTunes-coded title sits beside them. One value must remain.
		var data = WithKeyedItems (("title", "A"), ("com.apple.quicktime.displayname", "B"));
		var ilst = TestBuilders.Mp4.CreateBox ("\u00A9nam", TestBuilders.Mp4.CreateBox ("data", [0, 0, 0, 1, 0, 0, 0, 0, (byte)'C']));
		data = WithMeta ("mdta", KeysFrom (data), [.. ItemBoxes (data), ilst]);
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual ("C", file.Tag!.Title);

		file.Tag.Title = "One";
		var saved = file.Render (data).ToArray ();

		CollectionAssert.AreEqual (new[] { Code (1) }, ItemCodes (saved));
		Assert.AreEqual ("One", Mp4File.Read (saved).File!.Tag!.Title);
	}

	static byte[] KeysFrom (byte[] file)
	{
		var at = IndexOf (file, "keys"u8.ToArray ()) - 4;
		return file.AsSpan (at, (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at))).ToArray ();
	}

	static byte[][] ItemBoxes (byte[] file)
	{
		var ilst = IndexOf (file, "ilst"u8.ToArray ()) - 4;
		var end = ilst + (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (ilst));
		var boxes = new List<byte[]> ();
		for (var at = ilst + 8; at + 8 <= end;) {
			var size = (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at));
			boxes.Add (file.AsSpan (at, size).ToArray ());
			at += size;
		}
		return [.. boxes];
	}

	[TestMethod]
	public void QuickTimeKeys_MalformedKeysAtom_IsIgnored ()
	{
		// A keys atom whose count or entry size runs past its end names nothing; items stay unnamed.
		foreach (var keys in new[] { KeysAtom (uint.MaxValue, []), KeysAtom (1, [0, 0, 0, 200, .. "mdtatitle"u8]), KeysAtom (1, [0, 0, 0, 4]) }) {
			var data = WithMeta ("mdta", keys, Item (Code (1), 1, "T"u8.ToArray ()));

			var result = Mp4File.Read (data);

			Assert.IsTrue (result.IsSuccess, result.Error);
			Assert.IsNull (result.File!.Tag!.Title);
		}
	}

	[TestMethod]
	public void ItunesFile_WithoutKeys_Unchanged ()
	{
		// An index-like item code in an ordinary ilst is not a key reference without a keys atom.
		var data = WithItems (Item (Code (1), 1, "T"u8.ToArray ()), Text ("©nam", "Title"));
		var file = Mp4File.Read (data).File!;

		file.Tag!.Title = "New";
		var saved = file.Render (data).ToArray ();

		CollectionAssert.AreEquivalent (new[] { Code (1), "©nam" }, ItemCodes (saved));
		Assert.AreEqual ("New", Mp4File.Read (saved).File!.Tag!.Title);
	}

	static string Code (uint index)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian (bytes, index);
		return Encoding.Latin1.GetString (bytes);
	}

	static byte[] Item (string code, int type, byte[] value)
	{
		var data = new byte[8 + value.Length];
		BinaryPrimitives.WriteUInt32BigEndian (data, (uint)type);
		value.CopyTo (data, 8);
		return TestBuilders.Mp4.CreateBox (code, TestBuilders.Mp4.CreateBox ("data", data));
	}

	static byte[] Text (string code, string value) => Item (code, 1, Encoding.UTF8.GetBytes (value));

	static byte[] KeysAtom (uint count, byte[] entries)
	{
		var body = new byte[8 + entries.Length];
		BinaryPrimitives.WriteUInt32BigEndian (body.AsSpan (4), count);
		entries.CopyTo (body, 8);
		return TestBuilders.Mp4.CreateBox ("keys", body);
	}

	static byte[] WithItems (params byte[][] items) => WithMeta ("mdir", null, items);

	static byte[] WithKeyedItems (params (string Key, string Value)[] entries)
	{
		using var keyEntries = new MemoryStream ();
		var items = new List<byte[]> ();
		for (var i = 0; i < entries.Length; i++) {
			var name = Encoding.UTF8.GetBytes (entries[i].Key);
			var size = new byte[4];
			BinaryPrimitives.WriteUInt32BigEndian (size, (uint)(8 + name.Length));
			keyEntries.Write (size);
			keyEntries.Write ("mdta"u8);
			keyEntries.Write (name);
			items.Add (Text (Code ((uint)i + 1), entries[i].Value));
		}
		return WithMeta ("mdta", KeysAtom ((uint)entries.Length, keyEntries.ToArray ()), [.. items]);
	}

	// A minimal M4A whose moov gains udta/meta { hdlr, [keys], ilst { items } }.
	static byte[] WithMeta (string handler, byte[]? keys, params byte[][] items)
	{
		var hdlrBody = new byte[25];
		Encoding.ASCII.GetBytes (handler).CopyTo (hdlrBody, 8);
		var metaBody = Concat ([new byte[4], TestBuilders.Mp4.CreateBox ("hdlr", hdlrBody), keys ?? [], TestBuilders.Mp4.CreateBox ("ilst", Concat (items))]);
		var udta = TestBuilders.Mp4.CreateBox ("udta", TestBuilders.Mp4.CreateBox ("meta", metaBody));

		var file = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var moovAt = IndexOf (file, "moov"u8.ToArray ()) - 4;
		var moovSize = (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (moovAt));
		var moov = TestBuilders.Mp4.CreateBox ("moov", Concat ([file.AsSpan (moovAt + 8, moovSize - 8).ToArray (), udta]));
		return Concat ([file.AsSpan (0, moovAt).ToArray (), moov, file.AsSpan (moovAt + moovSize).ToArray ()]);
	}

	static List<string> ItemCodes (byte[] file)
	{
		var ilst = IndexOf (file, "ilst"u8.ToArray ()) - 4;
		var end = ilst + (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (ilst));
		var codes = new List<string> ();
		for (var at = ilst + 8; at + 8 <= end;) {
			codes.Add (Encoding.Latin1.GetString (file, at + 4, 4));
			at += (int)BinaryPrimitives.ReadUInt32BigEndian (file.AsSpan (at));
		}
		return codes;
	}

	static byte[] Concat (byte[][] parts)
	{
		var result = new byte[parts.Sum (p => p.Length)];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}
		return result;
	}

	static int IndexOf (byte[] haystack, byte[] needle)
	{
		for (var i = 0; i + needle.Length <= haystack.Length; i++) {
			if (haystack.AsSpan (i, needle.Length).SequenceEqual (needle))
				return i;
		}
		return -1;
	}
}
