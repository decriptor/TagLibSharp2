// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A copy makes the target hold the source's value. The setters' write-back no-op must not fire
/// during <see cref="Tag.CopyTo"/>, even when the source value equals the target's first value,
/// or the target keeps values the source does not have.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class TagCopyReplacesListTests
{
	const string Stale = "Zq2";

	static readonly string[] BasicFields = ["Title", "Artist", "Album", "Genre"];

	static IEnumerable<PropertyInfo> StringProperties () =>
		typeof (Tag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.PropertyType == typeof (string) && p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0);

	/// <summary>
	/// A target whose field for <paramref name="property"/> holds "V1" and then <see cref="Stale"/>,
	/// or null when the format does not store "V1" for that property in a text field.
	/// </summary>
	static Tag? BuildTarget (string format, PropertyInfo property)
	{
		switch (format) {
			case "id3v23":
			case "id3v24": {
					var tag = new Id3v2Tag (format == "id3v23" ? Id3v2Version.V23 : Id3v2Version.V24);
					property.SetValue (tag, "V1");
					if (tag.Frames.Count + tag.UserTextFrames.Count == 0 || !Equals (property.GetValue (tag), "V1"))
						return null;
					foreach (var f in tag.Frames)
						f.Text = "V1\0" + Stale;
					foreach (var f in tag.UserTextFrames)
						f.Value = "V1\0" + Stale;
					return Id3v2Tag.Read (tag.Render ().Span).Tag;
				}
			case "ape": {
					var probe = new ApeTag ();
					property.SetValue (probe, "V1");
					if (probe.ItemCount != 1 || !Equals (property.GetValue (probe), "V1"))
						return null;
					var tag = new ApeTag ();
					tag.SetValue (ApeKey (probe), "V1\0" + Stale);
					return ApeTag.Parse (tag.RenderWithOptions (includeHeader: true).Span).Tag;
				}
			case "vorbis": {
					var probe = new VorbisComment ("test");
					property.SetValue (probe, "V1");
					if (probe.Fields.Count != 1 || !Equals (property.GetValue (probe), "V1"))
						return null;
					var tag = new VorbisComment ("test");
					tag.AddField (probe.Fields[0].Name, "V1");
					tag.AddField (probe.Fields[0].Name, Stale);
					return VorbisComment.Read (tag.Render ().Span).Tag;
				}
			default:
				throw new ArgumentOutOfRangeException (nameof (format));
		}
	}

	static string ApeKey (ApeTag tag)
	{
		// One item: value size (4, LE), flags (4), key (ASCII, NUL-terminated), value.
		var data = tag.Render ().ToArray ();
		var keyEnd = Array.IndexOf (data, (byte)0, 8);
		return Encoding.ASCII.GetString (data, 8, keyEnd - 8);
	}

	static Tag NewSource (string format) => format switch {
		"id3v24" => new Id3v2Tag (Id3v2Version.V24),
		"ape" => new ApeTag (),
		_ => new VorbisComment ("source"),
	};

	/// <summary>
	/// Whether CopyTo (All) writes this property at all: a different source value must arrive.
	/// </summary>
	static bool IsCopied (string sourceFormat, string targetFormat, PropertyInfo property)
	{
		var source = NewSource (sourceFormat);
		property.SetValue (source, "V9");
		var target = BuildTarget (targetFormat, property)!;
		source.CopyTo (target, TagCopyOptions.All);
		return Equals (property.GetValue (target), "V9");
	}

	static bool RenderHolds (Tag tag, string text)
	{
		var bytes = tag.Render ().ToArray ();
		var needle = Encoding.UTF8.GetBytes (text);
		for (var i = 0; i + needle.Length <= bytes.Length; i++) {
			if (bytes.AsSpan (i, needle.Length).SequenceEqual (needle))
				return true;
		}
		return false;
	}

	[TestMethod]
	[DataRow ("id3v23")]
	[DataRow ("id3v24")]
	[DataRow ("ape")]
	[DataRow ("vorbis")]
	public void CopyAll_SourceEqualsTargetFirstValue_ReplacesTheList (string targetFormat)
	{
		var covered = new List<string> ();
		var failures = new List<string> ();
		foreach (var sourceFormat in new[] { "id3v24", "ape", "vorbis" }) {
			foreach (var property in StringProperties ()) {
				var source = NewSource (sourceFormat);
				property.SetValue (source, "V1");
				if (!Equals (property.GetValue (source), "V1"))
					continue;
				var target = BuildTarget (targetFormat, property);
				if (target is null || !IsCopied (sourceFormat, targetFormat, property))
					continue;
				covered.Add (property.Name);

				source.CopyTo (target, TagCopyOptions.All);

				if (!Equals (property.GetValue (target), "V1") || RenderHolds (target, Stale))
					failures.Add ($"{sourceFormat}->{targetFormat} {property.Name}: read {property.GetValue (target)}, stale kept={RenderHolds (target, Stale)}");
			}
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
		foreach (var name in new[] { "Artist", "Genre", "Publisher", "Isrc", "CatalogNumber", "MusicBrainzArtistId" })
			Assert.Contains (name, covered);
	}

	[TestMethod]
	[DataRow ("id3v23")]
	[DataRow ("id3v24")]
	[DataRow ("ape")]
	[DataRow ("vorbis")]
	public void CopyBasic_SourceEqualsTargetFirstValue_ReplacesTheList (string targetFormat)
	{
		var failures = new List<string> ();
		foreach (var sourceFormat in new[] { "id3v24", "ape", "vorbis" }) {
			foreach (var name in BasicFields) {
				var property = typeof (Tag).GetProperty (name)!;
				var source = NewSource (sourceFormat);
				property.SetValue (source, "V1");
				var target = BuildTarget (targetFormat, property)!;

				source.CopyTo (target, TagCopyOptions.Basic);

				if (!Equals (property.GetValue (target), "V1") || RenderHolds (target, Stale))
					failures.Add ($"{sourceFormat}->{targetFormat} {name}");
			}
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
	}

	[TestMethod]
	public void CopyBasic_IntoFlacFileTag_ReplacesTheList ()
	{
		var file = FlacFile.Read (TestBuilders.Flac.CreateMinimal ()).File!;
		var comment = new VorbisComment ("test");
		comment.AddField ("ARTIST", "P1");
		comment.AddField ("ARTIST", "P2");
		file.VorbisComment = comment;
		var source = new Id3v2Tag (Id3v2Version.V24) { Artist = "P1" };

		source.CopyTo (file.Tag!, TagCopyOptions.Basic);

		Assert.AreEqual ("P1", string.Join ("|", file.VorbisComment!.GetValues ("ARTIST")));
	}

	[TestMethod]
	public void Copy_ValueEqualToTarget_KeepsTheTargetsFallbackFields ()
	{
		// MOVEMENTNAME, ORIGINALYEAR, and APE Date are fields of their own beside the value the getter
		// reads. A copy of that same value leaves them, and drops only other values of the primary field.
		static VorbisComment Picard ()
		{
			var c = new VorbisComment ("picard");
			c.AddField ("MOVEMENT", "2");
			c.AddField ("MOVEMENTNAME", "Allegro");
			c.AddField ("ORIGINALDATE", "1969-09-26");
			c.AddField ("ORIGINALYEAR", "1969");
			return c;
		}

		var target = Picard ();
		target.AddField ("MOVEMENT", "X");
		Picard ().CopyTo (target, TagCopyOptions.All);
		Assert.AreEqual ("2", string.Join ("|", target.GetValues ("MOVEMENT")));
		Assert.AreEqual ("Allegro", string.Join ("|", target.GetValues ("MOVEMENTNAME")));
		Assert.AreEqual ("1969", string.Join ("|", target.GetValues ("ORIGINALYEAR")));

		var nameOnly = new VorbisComment ("t");
		nameOnly.AddField ("MOVEMENTNAME", "Allegro");
		new VorbisComment ("s") { Movement = "Allegro" }.CopyTo (nameOnly, TagCopyOptions.All);
		Assert.HasCount (0, nameOnly.GetValues ("MOVEMENT"));
		Assert.AreEqual ("Allegro", nameOnly.GetValue ("MOVEMENTNAME"));

		var ape = new ApeTag ();
		ape.SetValue ("Year", "2001");
		ape.SetValue ("Date", "2001-05-03");
		new ApeTag { Year = "2001" }.CopyTo (ape, TagCopyOptions.Basic);
		Assert.AreEqual ("2001-05-03", ape.GetValue ("Date"));
	}

	static Id3v2Tag PicardId3 (Id3v2Version version)
	{
		var tag = new Id3v2Tag (version);
		tag.Performers = ["P1", "P2"];
		tag.Genres = ["Rock", "Pop"];
		tag.SetTextFrameValues ("TPUB", ["Lab1", "Lab2"]);
		tag.SetTextFrameValues ("TSRC", ["ISRC1", "ISRC2"]);
		tag.AddUserTextFrame (new TagLibSharp2.Id3.Id3v2.Frames.UserTextFrame ("CATALOGNUMBER", "CAT1\0CAT2"));
		return Id3v2Tag.Read (tag.Render ().Span).Tag!;
	}

	static List<string> Frames (Id3v2Tag tag) =>
		[.. tag.Frames.Select (f => f.Id + "=" + f.Text), .. tag.UserTextFrames.Select (f => f.Description + "=" + f.Value)];

	[TestMethod]
	[DataRow (Id3v2Version.V23)]
	[DataRow (Id3v2Version.V24)]
	public void Copy_FromAViewOverTheTarget_KeepsEveryList (Id3v2Version version)
	{
		var tag = PicardId3 (version);
		var before = Frames (tag);

		new CombinedTag (tag, null).CopyTo (tag, TagCopyOptions.All);
		CollectionAssert.AreEqual (before, Frames (tag));

		tag.CopyTo (tag, TagCopyOptions.All);
		CollectionAssert.AreEqual (before, Frames (tag));

		// Two different views over the same tag share it, though they are different objects.
		new CombinedTag (tag, null).CopyTo (new CombinedTag (tag, null), TagCopyOptions.All);
		CollectionAssert.AreEqual (before, Frames (tag));

		// Mp3File.Tag is a CombinedTag over the file's ID3v2 and ID3v1 tags.
		byte[] mpeg = [0xFF, 0xFB, 0x90, 0x00, .. new byte[413]];
		var mp3 = Mp3File.Read ([.. tag.Render ().Span, .. mpeg]).File!;
		var fileBefore = Frames (mp3.Id3v2Tag!);
		mp3.Tag!.CopyTo (mp3.Id3v2Tag!, TagCopyOptions.All);
		CollectionAssert.AreEqual (fileBefore, Frames (mp3.Id3v2Tag!));

		// Mp3File.Tag builds a new view on every read.
		var source = mp3.Tag!;
		var target = mp3.Tag!;
		Assert.AreNotSame (source, target);
		source.CopyTo (target, TagCopyOptions.All);
		CollectionAssert.AreEqual (fileBefore, Frames (mp3.Id3v2Tag!));
	}

	[TestMethod]
	public void Copy_FromAViewOverTheTarget_KeepsEveryList_FlacAndApe ()
	{
		var file = FlacFile.Read (TestBuilders.Flac.CreateMinimal ()).File!;
		var comment = new VorbisComment ("test");
		foreach (var (name, value) in new[] { ("ARTIST", "P1"), ("ARTIST", "P2"), ("GENRE", "Rock"), ("GENRE", "Pop"), ("LABEL", "Lab1"), ("LABEL", "Lab2") })
			comment.AddField (name, value);
		file.VorbisComment = comment;
		var before = string.Join ("|", comment.Fields.Select (f => f.Name + "=" + f.Value));

		file.Tag!.CopyTo (comment, TagCopyOptions.All);
		comment.CopyTo (file.Tag!, TagCopyOptions.All);

		Assert.AreEqual (before, string.Join ("|", comment.Fields.Select (f => f.Name + "=" + f.Value)));

		var ape = new ApeTag ();
		ape.SetValue ("Artist", "P1\0P2");
		ape.SetValue ("Genre", "Rock\0Pop");
		ape.SetValue ("Publisher", "Lab1\0Lab2");
		ape.CopyTo (ape, TagCopyOptions.All);
		Assert.AreEqual ("P1|P2", string.Join ("|", ape.Performers));
		Assert.AreEqual ("Rock|Pop", string.Join ("|", ape.Genres));
		Assert.AreEqual ("Lab1, Lab2", ape.GetValue ("Publisher"));
	}

	[TestMethod]
	public void Copy_FromAnUndetectedView_ReadsTheSourceBeforeWriting ()
	{
		// A view this library does not know about. CopyTo reads every source value before it writes,
		// so the Artist write cannot shorten the Performers list the copy reads afterwards.
		var inner = PicardId3 (Id3v2Version.V24);
		var view = new ForwardingTag (inner);

		view.CopyTo (inner, TagCopyOptions.All);

		Assert.AreEqual ("P1|P2", string.Join ("|", inner.Performers));
		Assert.AreEqual ("Rock|Pop", string.Join ("|", inner.Genres));
	}

	[TestMethod]
	public void Copy_OfTheOriginalDateAV23TagReads_KeepsTdor ()
	{
		// In ID3v2.3, TORY holds only the year (ID3v2.3 §4.2.1); a TDOR beside it carries the full date.
		var tdorOnly = new Id3v2Tag (Id3v2Version.V23);
		tdorOnly.SetTextFrameValues ("TDOR", ["1969-09-26"]);
		new VorbisComment ("s") { OriginalReleaseDate = "1969-09-26" }.CopyTo (tdorOnly, TagCopyOptions.All);
		Assert.AreEqual ("1969-09-26", tdorOnly.GetTextFrame ("TDOR"));
		Assert.IsNull (tdorOnly.GetTextFrame ("TORY"));

		var both = new Id3v2Tag (Id3v2Version.V23);
		both.SetTextFrameValues ("TORY", ["1969"]);
		both.SetTextFrameValues ("TDOR", ["1969-09-26"]);
		new VorbisComment ("s") { OriginalReleaseDate = "1969" }.CopyTo (both, TagCopyOptions.All);
		Assert.AreEqual ("1969", both.GetTextFrame ("TORY"));
		Assert.AreEqual ("1969-09-26", both.GetTextFrame ("TDOR"));

		new VorbisComment ("s") { OriginalReleaseDate = "1970-01-02" }.CopyTo (both, TagCopyOptions.All);
		Assert.AreEqual ("1970", both.OriginalReleaseDate);
		Assert.IsNull (both.GetTextFrame ("TDOR"));
	}

	[TestMethod]
	public void Copy_FromAPicardMovementLayout_KeepsTheAppleName ()
	{
		var source = new VorbisComment ("picard");
		source.AddField ("MOVEMENTNAME", "Allegro");
		source.AddField ("MOVEMENT", "2");
		source.AddField ("MOVEMENTTOTAL", "4");
		var target = new Id3v2Tag (Id3v2Version.V24);
		target.SetTextFrameValues ("MVNM", ["Allegro"]);
		target.SetTextFrameValues ("MVIN", ["2/4"]);

		source.CopyTo (target, TagCopyOptions.All);

		Assert.AreEqual ("Allegro", target.GetTextFrame ("MVNM"));
		Assert.AreEqual ("2/4", target.GetTextFrame ("MVIN"));

		var ape = new ApeTag ();
		ape.SetValue ("MOVEMENTNAME", "Allegro");
		ape.SetValue ("MOVEMENT", "2");
		var fromApe = new Id3v2Tag (Id3v2Version.V24);
		ape.CopyTo (fromApe, TagCopyOptions.All);
		Assert.AreEqual ("Allegro", fromApe.GetTextFrame ("MVNM"));
		Assert.AreEqual (2u, fromApe.MovementNumber);
	}

	sealed class ForwardingTag (Id3v2Tag inner) : Tag
	{
		public override TagTypes TagType => inner.TagType;
		public override string? Title { get => inner.Title; set => inner.Title = value; }
		public override string? Artist { get => inner.Artist; set => inner.Artist = value; }
		public override string[] Performers { get => inner.Performers; set => inner.Performers = value; }
		public override string? Album { get => inner.Album; set => inner.Album = value; }
		public override string? Year { get => inner.Year; set => inner.Year = value; }
		public override string? Comment { get => inner.Comment; set => inner.Comment = value; }
		public override string? Genre { get => inner.Genre; set => inner.Genre = value; }
		public override string[] Genres { get => inner.Genres; set => inner.Genres = value; }
		public override uint? Track { get => inner.Track; set => inner.Track = value; }
		public override BinaryData Render () => inner.Render ();
		public override void Clear () => inner.Clear ();
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version3)]
	[DataRow (TestConstants.Id3v2.Version4)]
	public void CopyAll_ListTheTargetHolds_KeepsItsFrames (byte version)
	{
		// UTF-16 frames the setters would not write. With Extended, the names travel in the lists,
		// so a target that already holds them keeps its frames, encoding and order included.
		static byte[] Utf16List (string a, string b) =>
			[0x01, 0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes (a), 0, 0, 0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes (b)];
		byte[] body = [
			.. TestBuilders.Id3v2.CreateFrame ("TPE1", Utf16List ("P1", "P2"), version),
			.. TestBuilders.Id3v2.CreateFrame ("TCON", Utf16List ("Rock", "Pop"), version),
			.. TestBuilders.Id3v2.CreateFrame ("TALB", Utf16List ("A", "B"), version),
		];
		var target = Id3v2Tag.Read ([.. TestBuilders.Id3v2.CreateHeader (version, (uint)body.Length), .. body]).Tag!;
		var before = target.Render ().ToArray ();
		var source = new VorbisComment ("s");
		foreach (var (name, value) in new[] { ("ARTIST", "P1"), ("ARTIST", "P2"), ("GENRE", "Rock"), ("GENRE", "Pop") })
			source.AddField (name, value);

		source.CopyTo (target, TagCopyOptions.All);

		CollectionAssert.AreEqual (before, target.Render ().ToArray ());
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version3)]
	[DataRow (TestConstants.Id3v2.Version4)]
	public void Copy_BetweenViewsOverOneTag_IsByteIdentical (byte version)
	{
		// Numbers and dates in a layout their setters would rewrite ("05/12" becomes "5/12"); a view
		// copy skips every value the target already shows, so none of them is rewritten.
		static byte[] Utf16 (string text) => [0x01, 0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes (text)];
		byte[] body = [
			.. TestBuilders.Id3v2.CreateFrame ("TRCK", Utf16 ("05/12"), version),
			.. TestBuilders.Id3v2.CreateFrame ("TPOS", Utf16 ("1/2"), version),
			.. TestBuilders.Id3v2.CreateFrame (version == 4 ? "TDRC" : "TYER", Utf16 (version == 4 ? "2001-05-03" : "2001"), version),
			.. TestBuilders.Id3v2.CreateFrame ("TBPM", Utf16 ("0120"), version),
			.. TestBuilders.Id3v2.CreateFrame ("TPUB", Utf16 ("Lab1\0Lab2"), version),
		];
		var tag = Id3v2Tag.Read ([.. TestBuilders.Id3v2.CreateHeader (version, (uint)body.Length), .. body]).Tag!;
		var before = tag.Render ().ToArray ();

		new CombinedTag (tag, null).CopyTo (new CombinedTag (tag, null), TagCopyOptions.All);

		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());
	}

	[TestMethod]
	public void Copy_BetweenAnAiffViewAndItsId3Tag_KeepsEveryList ()
	{
		var aiff = TagLibSharp2.Aiff.AiffFile.Read (TestBuilders.Aiff.CreateMinimal ()).File!;
		aiff.Id3v2Tag = PicardId3 (Id3v2Version.V24);
		var before = Frames (aiff.Id3v2Tag!);

		aiff.Tag!.CopyTo (aiff.Id3v2Tag!, TagCopyOptions.All);
		CollectionAssert.AreEqual (before, Frames (aiff.Id3v2Tag!));

		aiff.Id3v2Tag!.CopyTo (aiff.Tag!, TagCopyOptions.All);
		CollectionAssert.AreEqual (before, Frames (aiff.Id3v2Tag!));

		var reread = TagLibSharp2.Aiff.AiffFile.Read (aiff.Render ().Span).File!;
		Assert.AreEqual ("Lab1|Lab2", string.Join ("|", reread.Id3v2Tag!.GetTextFrameValues ("TPUB")));
	}

	/// <summary>
	/// Every Tag type in the library that keeps another tag, or a file that holds tags, is a view.
	/// A view must name the tags it reads (Leaves), or a copy between it and one of them cuts lists.
	/// This catches the next view added without that override.
	/// </summary>
	[TestMethod]
	public void EveryViewType_NamesTheTagsItReads ()
	{
		static bool HoldsTags (Type type) =>
			typeof (Tag).IsAssignableFrom (type)
			|| (type.IsArray && typeof (Tag).IsAssignableFrom (type.GetElementType ()!))
			|| type.GetProperties (BindingFlags.Public | BindingFlags.Instance).Any (p => typeof (Tag).IsAssignableFrom (p.PropertyType));

		var views = new List<string> ();
		var missing = new List<string> ();
		foreach (var type in typeof (Tag).Assembly.GetTypes ().Where (t => t.IsClass && !t.IsAbstract && typeof (Tag).IsAssignableFrom (t))) {
			var wraps = type.GetFields (BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.Any (f => HoldsTags (f.FieldType));
			if (!wraps)
				continue;
			views.Add (type.Name);
			var leaves = type.GetMethod ("Leaves", BindingFlags.Instance | BindingFlags.NonPublic)!;
			if (leaves.DeclaringType != type)
				missing.Add (type.Name);
		}

		Assert.IsEmpty (missing, string.Join (",", missing));
		foreach (var name in new[] { "CombinedTag", "FlacTag", "AiffTag" })
			Assert.Contains (name, views);
	}

	[TestMethod]
	public void WriteBack_AfterACopy_IsANoOpAgain ()
	{
		var target = new Id3v2Tag (Id3v2Version.V24);
		target.SetTextFrameValues ("TPUB", ["Lab1", "Lab2"]);
		new VorbisComment ("source") { Publisher = "Lab1" }.CopyTo (target, TagCopyOptions.All);
		Assert.AreEqual ("Lab1", string.Join ("|", target.GetTextFrameValues ("TPUB")));

		target.SetTextFrameValues ("TPUB", ["Lab1", "Lab2"]);
		target.Publisher = target.Publisher;

		Assert.AreEqual ("Lab1|Lab2", string.Join ("|", target.GetTextFrameValues ("TPUB")));
	}

	[TestMethod]
	public void CopyThatThrows_StillEndsTheCopyScope ()
	{
		var source = new ThrowingTag ();
		var target = new Id3v2Tag (Id3v2Version.V24);

		Assert.ThrowsExactly<InvalidOperationException> (() => source.CopyTo (target, TagCopyOptions.Basic));

		target.SetTextFrameValues ("TPE1", ["P1", "P2"]);
		target.Artist = target.Artist;
		Assert.AreEqual ("P1|P2", string.Join ("|", target.GetTextFrameValues ("TPE1")));
	}

	sealed class ThrowingTag : Tag
	{
		public override TagTypes TagType => TagTypes.None;
		public override string? Title { get => throw new InvalidOperationException (); set { } }
		public override string? Artist { get => null; set { } }
		public override string? Album { get => null; set { } }
		public override string? Year { get => null; set { } }
		public override string? Comment { get => null; set { } }
		public override string? Genre { get => null; set { } }
		public override uint? Track { get => null; set { } }
		public override BinaryData Render () => BinaryData.Empty;
		public override void Clear () { }
	}
}
