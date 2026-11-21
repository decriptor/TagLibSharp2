// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Audible;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Audible;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Audible")]
public class AudibleTagTests
{
	static readonly string[] ExpectedKeys = ["a", "title", "b"];

	static AudibleTag Parsed () => AudibleFile.Read (AudibleTestBuilder.Create ()).File!.Tag;

	static AudibleTag RoundTrip (AudibleTag tag)
	{
		var parsed = AudibleTag.Parse (tag.Render ().Span);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		return parsed.Tag!;
	}

	[TestMethod]
	public void EveryNamedProperty_RoundTripsUnderItsKey ()
	{
		var setters = new (string Key, Action<AudibleTag, string?> Set, Func<AudibleTag, string?> Get)[] {
			(AudibleTag.TitleKey, (t, v) => t.Title = v, t => t.Title),
			(AudibleTag.AuthorKey, (t, v) => t.Artist = v, t => t.Artist),
			(AudibleTag.AuthorKey, (t, v) => t.Author = v, t => t.Author),
			(AudibleTag.NarratorKey, (t, v) => t.Narrator = v, t => t.Narrator),
			(AudibleTag.ProviderKey, (t, v) => t.Provider = v, t => t.Provider),
			(AudibleTag.PublishDateKey, (t, v) => t.PublishDate = v, t => t.PublishDate),
			(AudibleTag.DescriptionKey, (t, v) => t.Description = v, t => t.Description),
			(AudibleTag.LongDescriptionKey, (t, v) => t.LongDescription = v, t => t.LongDescription),
			(AudibleTag.KeywordsKey, (t, v) => t.Keywords = v, t => t.Keywords),
			(AudibleTag.CopyrightKey, (t, v) => t.Copyright = v, t => t.Copyright),
			(AudibleTag.ShortTitleKey, (t, v) => t.ShortTitle = v, t => t.ShortTitle),
			(AudibleTag.ParentTitleKey, (t, v) => t.ParentTitle = v, t => t.ParentTitle),
			(AudibleTag.ParentShortTitleKey, (t, v) => t.ParentShortTitle = v, t => t.ParentShortTitle),
			(AudibleTag.ProductIdKey, (t, v) => t.ProductId = v, t => t.ProductId),
			(AudibleTag.TitleIdKey, (t, v) => t.TitleId = v, t => t.TitleId),
		};

		foreach (var (key, set, get) in setters) {
			var tag = new AudibleTag ();
			set (tag, "Ünïcode ✓ " + key);

			var reread = RoundTrip (tag);

			Assert.AreEqual ("Ünïcode ✓ " + key, get (reread), key);
			Assert.AreEqual ("Ünïcode ✓ " + key, reread.GetValue (key), key);
			set (reread, null);
			Assert.IsNull (get (reread), key);
			Assert.IsNull (reread.GetValue (key), key);
		}
	}

	[TestMethod]
	public void UnmappedFields_IgnoreAValueAndWriteNothing ()
	{
		var tag = new AudibleTag {
			Album = "Album",
			Comment = "Comment",
			Genre = "Genre",
			Track = 3,
			Pictures = [new AudibleTestPicture ()]
		};

		Assert.IsNull (tag.Album);
		Assert.IsNull (tag.Comment);
		Assert.IsNull (tag.Genre);
		Assert.IsNull (tag.Track);
		Assert.IsEmpty (tag.Pictures);
		Assert.IsEmpty (tag.Keys);
		Assert.AreEqual (4, tag.Render ().Length);
	}

	[TestMethod]
	[DataRow ("20-JAN-2009", "2009")]
	[DataRow ("2011", "2011")]
	[DataRow ("JAN-20", null)]
	[DataRow ("abc", null)]
	public void Year_IsTheLastFourDigitsOfPubdate (string pubdate, string? year)
	{
		var tag = new AudibleTag { PublishDate = pubdate };

		Assert.AreEqual (year, tag.Year);
	}

	[TestMethod]
	public void Year_Set_KeepsTheDayAndMonth ()
	{
		var tag = new AudibleTag { PublishDate = "20-JAN-2009" };

		tag.Year = "2012";

		Assert.AreEqual ("20-JAN-2012", tag.PublishDate);
	}

	[TestMethod]
	[DataRow ("2012-03-04")]
	[DataRow ("2012")]
	public void Year_SetWithoutADayMonthDate_StoresTheValue (string value)
	{
		var tag = new AudibleTag { PublishDate = "sometime" };

		tag.Year = value;

		Assert.AreEqual (value, tag.PublishDate);
		tag.Year = null;
		Assert.IsNull (tag.PublishDate);
	}

	[TestMethod]
	public void SetValue_ExistingKey_KeepsItsPositionAndDropsLaterDuplicates ()
	{
		var dictionary = AudibleTestBuilder.Dictionary ([("a", "1"), ("title", "first"), ("b", "2"), ("title", "second")]);
		var tag = AudibleTag.Parse (dictionary).Tag!;
		Assert.AreEqual ("first", tag.Title);

		tag.Title = "only";

		CollectionAssert.AreEqual (ExpectedKeys, tag.Keys.ToList ());
		Assert.AreEqual ("only", RoundTrip (tag).Title);
	}

	[TestMethod]
	public void SetValue_NewKey_IsAppended ()
	{
		var tag = Parsed ();

		tag.SetValue ("custom_key", "custom value");

		Assert.AreEqual ("custom_key", tag.Keys[^1]);
		Assert.AreEqual ("custom value", RoundTrip (tag).GetValue ("custom_key"));
	}

	[TestMethod]
	public void SetValue_EmptyRemovesEveryCopy ()
	{
		var tag = AudibleTag.Parse (AudibleTestBuilder.Dictionary ([("k", "1"), ("k", "2")])).Tag!;

		tag.SetValue ("k", "");

		Assert.IsEmpty (tag.Keys);
	}

	[TestMethod]
	[DataRow ("codec")]
	[DataRow ("HeaderSeed")]
	[DataRow ("HeaderKey")]
	[DataRow ("EncryptedBlocks")]
	public void SetValue_DrmKey_Throws (string key)
	{
		var tag = Parsed ();

		Assert.ThrowsExactly<ArgumentException> (() => tag.SetValue (key, "x"));
		Assert.ThrowsExactly<ArgumentException> (() => tag.SetValue (key, null));
		Assert.IsNotNull (tag.GetValue (key));
	}

	[TestMethod]
	[DataRow ("")]
	[DataRow ("clé")]
	public void SetValue_BadKey_Throws (string key)
	{
		Assert.ThrowsExactly<ArgumentException> (() => new AudibleTag ().SetValue (key, "x"));
	}

	[TestMethod]
	public void Clear_RemovesDescriptiveKeysOnly ()
	{
		var tag = Parsed ();
		Assert.IsFalse (tag.IsEmpty);

		tag.Clear ();

		Assert.IsTrue (tag.IsEmpty);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Narrator);
		Assert.AreEqual ("acelp85", tag.Codec);
		Assert.AreEqual ("2348022159 2271241904 4180629618 1869089845", tag.GetValue ("HeaderKey"));
		Assert.AreEqual ("PF_TEST_000001", tag.TitleId);
	}

	[TestMethod]
	public void IsEmpty_TrueForANewTag_FalseWithAnyDescriptiveKey ()
	{
		var tag = new AudibleTag ();
		Assert.IsTrue (tag.IsEmpty);

		tag.SetValue ("unknown", "x");
		Assert.IsTrue (tag.IsEmpty);

		tag.Keywords = "k";
		Assert.IsFalse (tag.IsEmpty);
	}

	[TestMethod]
	public void Parse_ShorterThanTheCount_Fails ()
	{
		Assert.IsFalse (AudibleTag.Parse ([0, 0, 0]).IsSuccess);
		Assert.IsTrue (AudibleTag.Parse ([0, 0, 0, 0]).IsSuccess);
	}

	[TestMethod]
	public void Parse_EveryTruncation_FailsWithoutThrowing ()
	{
		var dictionary = AudibleTestBuilder.Dictionary (AudibleTestBuilder.DefaultEntries);

		for (var length = 0; length < dictionary.Length; length++)
			Assert.IsFalse (AudibleTag.Parse (dictionary.AsSpan (0, length)).IsSuccess, $"length {length}");
		Assert.AreEqual (dictionary.Length, AudibleTag.Parse (dictionary).BytesConsumed);
	}

	[TestMethod]
	public void Render_IsThePublishedLayout ()
	{
		var tag = new AudibleTag { Title = "T" };

		CollectionAssert.AreEqual (
			new byte[] { 0, 0, 0, 1, 0, 0, 0, 0, 5, 0, 0, 0, 1, (byte)'t', (byte)'i', (byte)'t', (byte)'l', (byte)'e', (byte)'T' },
			tag.Render ().ToArray ());
	}

	sealed class AudibleTestPicture : IPicture
	{
		public string MimeType => "image/png";
		public PictureType PictureType => PictureType.FrontCover;
		public string Description => "";
		public BinaryData PictureData => new ([1, 2, 3]);
	}
}
