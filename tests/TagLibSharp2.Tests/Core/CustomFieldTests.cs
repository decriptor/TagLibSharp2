// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;
using TagLibSharp2.Riff;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// The custom field API on every <see cref="Tag"/>: one name, its values, in the format's own storage.
/// </summary>
/// <remarks>
/// Invariant: after <c>SetCustomField (name, values)</c> returns true, <c>GetCustomField (name)</c> on the tag
/// and on the tag parsed from its rendering returns exactly the non-empty values, in order. After it returns
/// false, the tag renders the same bytes as before.
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Core")]
public class CustomFieldTests
{
	// Each entry: a fresh tag, and a function that renders and reparses it.
	public static IEnumerable<object[]> MultiValueTags => [
		["Vorbis", "MY_FIELD"],
		["Ape", "My Field"],
		["Id3v24", "My Field"],
		["Id3v23", "My Field"],
		["Mp4", "My Field"],
		["Asf", "My/Field"],
		["Matroska", "MY_FIELD"],
	];

	public static IEnumerable<object[]> AllSupportingTags => [
		.. MultiValueTags,
		["RiffInfo", "IXYZ"],
	];

	static Tag Create (string format) => format switch {
		"Vorbis" => new VorbisComment ("test"),
		"Ape" => new ApeTag (),
		"Id3v24" => new Id3v2Tag (Id3v2Version.V24),
		"Id3v23" => new Id3v2Tag (Id3v2Version.V23),
		"Mp4" => new Mp4Tag (),
		"Asf" => new AsfTag (),
		"Matroska" => new MatroskaTag (),
		"RiffInfo" => new RiffInfoTag (),
		_ => throw new ArgumentOutOfRangeException (nameof (format))
	};

	static Tag Reparse (Tag tag) => tag switch {
		VorbisComment v => VorbisComment.Read (v.Render ().Span).Tag!,
		ApeTag a => ApeTag.Parse (a.RenderWithOptions (includeHeader: true).Span).Tag!,
		Id3v2Tag i => Id3v2Tag.Read (i.Render ().Span).Tag!,
		Mp4Tag m => ReparseMp4 (m),
		AsfTag a => ReparseAsf (a),
		MatroskaTag m => ReparseMatroska (m),
		RiffInfoTag r => r.IsEmpty ? new RiffInfoTag () : RiffInfoTag.Parse (r.Render ().Slice (8))!,
		_ => throw new ArgumentOutOfRangeException (nameof (tag))
	};

	static Mp4Tag ReparseMp4 (Mp4Tag tag)
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;
		file.Tag = tag;
		var again = Mp4File.Read (file.Render (original).Span);
		Assert.IsTrue (again.IsSuccess, again.Error);
		// An empty ilst is not written, so the reopened file has no tag.
		return again.File!.Tag ?? new Mp4Tag ();
	}

	static AsfTag ReparseAsf (AsfTag tag)
	{
		var content = AsfContentDescription.Parse (tag.RenderContentDescription ().Span);
		var extended = AsfExtendedContentDescription.Parse (tag.Render ().Span);
		Assert.IsTrue (content.IsSuccess);
		Assert.IsTrue (extended.IsSuccess);
		return new AsfTag (content.Value, extended.Value);
	}

	static MatroskaTag ReparseMatroska (MatroskaTag tag)
	{
		// The Tags element is the whole tag. Wrap it in a segment and read it back.
		var ebml = MatroskaBytes.Master (0x1A45DFA3, MatroskaBytes.Element (0x4282, "matroska"u8));
		var segment = MatroskaBytes.Master (0x18538067, tag.Render ().ToArray ());
		var again = MatroskaFile.Read ([.. ebml, .. segment]);
		Assert.IsTrue (again.IsSuccess, again.Error);
		return again.File!.Tag;
	}

	[TestMethod]
	[DynamicData (nameof (MultiValueTags))]
	public void SetCustomField_SeveralValues_ReadBackInOrderAfterRender (string format, string name)
	{
		var tag = Create (format);

		Assert.IsTrue (tag.SupportsCustomFields);
		Assert.IsTrue (tag.SetCustomField (name, "one", "two", "three"));

		AssertValues (tag.GetCustomField (name), "one", "two", "three");
		AssertValues (Reparse (tag).GetCustomField (name), "one", "two", "three");
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void SetCustomField_OneValue_ReadBackAfterRender (string format, string name)
	{
		var tag = Create (format);

		Assert.IsTrue (tag.SetCustomField (name, "café"));

		AssertValues (Reparse (tag).GetCustomField (name), "café");
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void SetCustomField_ReplacesEarlierValues (string format, string name)
	{
		var tag = Create (format);
		tag.SetCustomField (name, "old");

		Assert.IsTrue (tag.SetCustomField (name, "new"));

		AssertValues (Reparse (tag).GetCustomField (name), "new");
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void SetCustomField_NullAndEmptyValuesSkipped (string format, string name)
	{
		var tag = Create (format);

		Assert.IsTrue (tag.SetCustomField (name, null!, "", "kept", ""));

		AssertValues (tag.GetCustomField (name), "kept");
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void SetCustomField_NoValues_RemovesTheField (string format, string name)
	{
		var tag = Create (format);
		tag.SetCustomField (name, "x");

		Assert.IsTrue (tag.SetCustomField (name));
		Assert.AreEqual (0, tag.GetCustomField (name).Count);
		Assert.IsTrue (tag.SetCustomField (name, (string[])null!));
		Assert.AreEqual (0, Reparse (tag).GetCustomField (name).Count);
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void SetCustomField_ValueWithNul_RefusedAndTagUnchanged (string format, string name)
	{
		var tag = Create (format);
		tag.SetCustomField (name, "before");
		var before = RenderOf (tag);

		Assert.IsFalse (tag.SetCustomField (name, "a\0b"));

		AssertValues (tag.GetCustomField (name), "before");
		CollectionAssert.AreEqual (before, RenderOf (tag));
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void CustomField_EmptyOrNulName_Refused (string format, string name)
	{
		_ = name;
		var tag = Create (format);
		var before = RenderOf (tag);

		Assert.IsFalse (tag.SetCustomField ("", "x"));
		Assert.IsFalse (tag.SetCustomField ("AB\0C", "x"));
		Assert.IsFalse (tag.RemoveCustomField (""));
		Assert.AreEqual (0, tag.GetCustomField ("").Count);
		CollectionAssert.AreEqual (before, RenderOf (tag));
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void RemoveCustomField_ReportsWhetherAValueWasThere (string format, string name)
	{
		var tag = Create (format);
		tag.SetCustomField (name, "x");

		Assert.IsTrue (tag.RemoveCustomField (name));
		Assert.IsFalse (tag.RemoveCustomField (name));
		Assert.AreEqual (0, Reparse (tag).GetCustomField (name).Count);
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void CustomField_NullName_Throws (string format, string name)
	{
		_ = name;
		var tag = Create (format);

		Assert.ThrowsExactly<ArgumentNullException> (() => tag.GetCustomField (null!));
		Assert.ThrowsExactly<ArgumentNullException> (() => tag.SetCustomField (null!, "x"));
		Assert.ThrowsExactly<ArgumentNullException> (() => tag.RemoveCustomField (null!));
	}

	[TestMethod]
	[DynamicData (nameof (AllSupportingTags))]
	public void SetCustomField_LeavesOtherFieldsAlone (string format, string name)
	{
		var tag = Create (format);
		tag.Comment = "comment";

		tag.SetCustomField (name, "x");
		tag.RemoveCustomField (name);

		Assert.AreEqual ("comment", Reparse (tag).Comment);
	}

	static void AssertValues (IReadOnlyList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	static byte[] RenderOf (Tag tag) => tag switch {
		AsfTag a => [.. a.Render ().ToArray (), .. a.RenderContentDescription ().ToArray ()],
		_ => tag.Render ().ToArray ()
	};

	// ───────────────────────── Unsupported ─────────────────────────

	[TestMethod]
	public void Id3v1_DoesNotSupportCustomFields ()
	{
		var tag = new Id3v1Tag { Title = "t" };
		var before = tag.Render ().ToArray ();

		Assert.IsFalse (tag.SupportsCustomFields);
		Assert.IsFalse (tag.SetCustomField ("MY_FIELD", "x"));
		Assert.IsFalse (tag.RemoveCustomField ("MY_FIELD"));
		Assert.AreEqual (0, tag.GetCustomField ("MY_FIELD").Count);
		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());
		Assert.ThrowsExactly<ArgumentNullException> (() => tag.GetCustomField (null!));
		Assert.ThrowsExactly<ArgumentNullException> (() => tag.SetCustomField (null!));
		Assert.ThrowsExactly<ArgumentNullException> (() => tag.RemoveCustomField (null!));
	}

	// ───────────────────────── Vorbis ─────────────────────────

	[TestMethod]
	public void Vorbis_NameIsCaseInsensitiveAndWrittenUpperCase ()
	{
		var tag = new VorbisComment ("test");

		tag.SetCustomField ("Mood", "calm");

		AssertValues (tag.GetCustomField ("MOOD"), "calm");
		Assert.AreEqual ("MOOD", tag.Fields[0].Name);
		Assert.IsTrue (tag.SetCustomField ("mood", "loud"));
		Assert.AreEqual (1, tag.Fields.Count);
	}

	[TestMethod]
	public void Vorbis_EachValueIsItsOwnField ()
	{
		var tag = new VorbisComment ("test");

		tag.SetCustomField ("MY_FIELD", "a", "b");

		var text = Encoding.UTF8.GetString (tag.Render ().ToArray ());
		StringAssert.Contains (text, "MY_FIELD=a");
		StringAssert.Contains (text, "MY_FIELD=b");
	}

	[TestMethod]
	public void Vorbis_ReplacedValuesKeepTheirPosition ()
	{
		var tag = new VorbisComment ("test");
		tag.AddField ("A", "1");
		tag.AddField ("MY_FIELD", "old");
		tag.AddField ("Z", "2");

		tag.SetCustomField ("MY_FIELD", "x", "y");

		AssertValues (tag.Fields.Select (f => f.Name).ToList (), "A", "MY_FIELD", "MY_FIELD", "Z");
	}

	[TestMethod]
	[DataRow ("A=B")]
	[DataRow ("TILDE~")]
	[DataRow ("CAFÉ")]
	[DataRow ("TAB\t")]
	[DataRow ("METADATA_BLOCK_PICTURE")]
	[DataRow ("metadata_block_picture")]
	public void Vorbis_InvalidOrReservedName_Refused (string name)
	{
		var tag = new VorbisComment ("test");

		Assert.IsFalse (tag.SetCustomField (name, "x"));
		Assert.IsFalse (tag.RemoveCustomField (name));
		Assert.AreEqual (0, tag.Fields.Count);
	}

	[TestMethod]
	public void Vorbis_MappedName_IsTheMappedProperty ()
	{
		var tag = new VorbisComment ("test") { Title = "Old" };

		tag.SetCustomField ("title", "New");

		Assert.AreEqual ("New", tag.Title);
		tag.Title = "Again";
		AssertValues (tag.GetCustomField ("TITLE"), "Again");
	}

	[TestMethod]
	public void Vorbis_PicturesUntouchedByCustomFields ()
	{
		var tag = new VorbisComment ("test");
		tag.AddPicture (new FlacPicture ("image/png", PictureType.FrontCover, "", new BinaryData ([1, 2, 3]), 1, 1, 24, 0));

		tag.SetCustomField ("MY_FIELD", "x");

		Assert.AreEqual (1, Reparse (tag).Pictures.Length);
		Assert.AreEqual (0, tag.GetCustomField ("METADATA_BLOCK_PICTURE").Count);
	}

	// ───────────────────────── APE ─────────────────────────

	[TestMethod]
	[DataRow ("A")]
	[DataRow ("ID3")]
	[DataRow ("tag")]
	[DataRow ("OggS")]
	[DataRow ("MP+")]
	[DataRow ("Café")]
	[DataRow ("Tab\tKey")]
	[DataRow ("Cover Art (Front)")]
	[DataRow ("cover art (custom)")]
	public void Ape_InvalidOrReservedKey_Refused (string key)
	{
		var tag = new ApeTag ();

		Assert.IsFalse (tag.SetCustomField (key, "x"));
		Assert.AreEqual (0, tag.ItemCount);
	}

	[TestMethod]
	public void Ape_KeyLengthBounds ()
	{
		var tag = new ApeTag ();

		Assert.IsTrue (tag.SetCustomField ("AB", "x"));
		Assert.IsTrue (tag.SetCustomField (new string ('K', 255), "x"));
		Assert.IsFalse (tag.SetCustomField (new string ('K', 256), "x"));
		Assert.AreEqual (2, tag.ItemCount);
	}

	[TestMethod]
	public void Ape_ValuesAreSeparatedByZeroInOneItem ()
	{
		var tag = new ApeTag ();

		tag.SetCustomField ("My Field", "a", "b");

		var rendered = tag.Render ().ToArray ();
		var needle = "My Field\0a\0b"u8.ToArray ();
		Assert.IsTrue (rendered.AsSpan ().IndexOf (needle) >= 0);
		Assert.AreEqual (1, tag.ItemCount);
	}

	[TestMethod]
	public void Ape_KeyIsCaseInsensitive ()
	{
		var tag = new ApeTag ();
		tag.SetCustomField ("My Field", "old");

		tag.SetCustomField ("MY FIELD", "new");

		Assert.AreEqual (1, tag.ItemCount);
		AssertValues (tag.GetCustomField ("my field"), "new");
	}

	[TestMethod]
	public void Ape_MappedKey_IsTheMappedProperty ()
	{
		var tag = new ApeTag { Title = "Old" };

		tag.SetCustomField ("TITLE", "New");

		Assert.AreEqual ("New", tag.Title);
		Assert.AreEqual (1, tag.ItemCount);
	}

	[TestMethod]
	public void Ape_BinaryItem_HasNoText ()
	{
		var tag = new ApeTag ();
		tag.SetBinaryItem ("Blob", "x.bin", [1, 2, 3]);

		Assert.AreEqual (0, tag.GetCustomField ("Blob").Count);
	}

	// ───────────────────────── ID3v2 ─────────────────────────

	[TestMethod]
	public void Id3v2_DescriptionMatchesExactly ()
	{
		var tag = new Id3v2Tag ();

		tag.SetCustomField ("Mood", "calm");
		tag.SetCustomField ("MOOD", "loud");

		AssertValues (tag.GetCustomField ("Mood"), "calm");
		AssertValues (tag.GetCustomField ("MOOD"), "loud");
		Assert.AreEqual (2, tag.UserTextFrames.Count);
	}

	[TestMethod]
	public void Id3v2_StoresOneTxxxWithNulSeparatedValues ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.SetCustomField ("My Field", "a", "b");

		Assert.AreEqual (1, tag.UserTextFrames.Count);
		Assert.AreEqual ("My Field", tag.UserTextFrames[0].Description);
		Assert.AreEqual ("a\0b", tag.UserTextFrames[0].Value);
		Assert.AreEqual (TextEncodingType.Utf8, tag.UserTextFrames[0].Encoding);
	}

	[TestMethod]
	public void Id3v23_WritesUtf16 ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V23);

		tag.SetCustomField ("My Field", "日本");

		Assert.AreEqual (TextEncodingType.Utf16WithBom, tag.UserTextFrames[0].Encoding);
		var again = Id3v2Tag.Read (tag.Render ().Span).Tag!;
		Assert.AreEqual (3, again.Version);
		AssertValues (again.GetCustomField ("My Field"), "日本");
	}

	[TestMethod]
	public void Id3v2_DuplicateTxxxFrames_ReadTogetherAndReplacedByOne ()
	{
		var tag = new Id3v2Tag ();
		tag.AddUserTextFrame (new UserTextFrame ("Dup", "a"));
		tag.AddUserTextFrame (new UserTextFrame ("Other", "o"));
		tag.AddUserTextFrame (new UserTextFrame ("Dup", "b\0c"));

		AssertValues (tag.GetCustomField ("Dup"), "a", "b", "c");

		tag.SetCustomField ("Dup", "z");
		AssertValues (tag.UserTextFrames.Select (f => f.Description).ToList (), "Dup", "Other");
	}

	[TestMethod]
	public void Id3v2_TxxxThatAMappedPropertyUses_IsThatProperty ()
	{
		var tag = new Id3v2Tag ();

		tag.SetCustomField ("REPLAYGAIN_TRACK_GAIN", "-6.5 dB");

		Assert.AreEqual ("-6.5 dB", tag.ReplayGainTrackGain);
		tag.ReplayGainTrackGain = "-1.0 dB";
		AssertValues (tag.GetCustomField ("REPLAYGAIN_TRACK_GAIN"), "-1.0 dB");
	}

	[TestMethod]
	public void Id3v2_NameNeverAddressesAStandardFrame ()
	{
		var tag = new Id3v2Tag { Title = "Song" };

		tag.SetCustomField ("TITLE", "custom");
		tag.SetCustomField ("TIT2", "custom");

		Assert.AreEqual ("Song", tag.Title);
		Assert.AreEqual (2, tag.UserTextFrames.Count);
	}

	// ───────────────────────── MP4 ─────────────────────────

	[TestMethod]
	public void Mp4_UsesTheITunesMeanAndOneDataAtomPerValue ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;
		file.Tag = new Mp4Tag ();

		file.Tag.SetCustomField ("My Field", "a", "b");

		Assert.AreEqual ("a", file.GetFreeformTag ("com.apple.iTunes", "My Field"));
		var atoms = file.Tag.GetAtoms ()["----:com.apple.iTunes:My Field"];
		Assert.AreEqual (2, atoms.Count);
	}

	[TestMethod]
	public void Mp4_NameWithColon_SurvivesRender ()
	{
		var tag = new Mp4Tag ();

		tag.SetCustomField ("a:b:c", "x");

		AssertValues (Reparse (tag).GetCustomField ("a:b:c"), "x");
	}

	[TestMethod]
	public void Mp4_FreeformSetThroughTheFile_IsTheCustomField ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;

		file.SetFreeformTag ("com.apple.iTunes", "LABEL", "Acme");
		file.SetFreeformTag ("org.example", "LABEL", "Other");

		AssertValues (file.Tag!.GetCustomField ("LABEL"), "Acme");
		Assert.AreEqual (0, file.Tag.GetCustomField ("label").Count);
	}

	// ───────────────────────── ASF ─────────────────────────

	[TestMethod]
	[DataRow ("Title")]
	[DataRow ("AUTHOR")]
	[DataRow ("Copyright")]
	[DataRow ("Description")]
	[DataRow ("Rating")]
	public void Asf_ContentDescriptionName_IsTheFixedField (string name)
	{
		var tag = new AsfTag ();

		Assert.IsTrue (tag.SetCustomField (name, "value"));

		Assert.AreEqual (0, tag.ExtendedContentDescription.Descriptors.Count);
		AssertValues (Reparse (tag).GetCustomField (name), "value");
		Assert.IsFalse (tag.SetCustomField (name, "a", "b"));
		AssertValues (tag.GetCustomField (name), "value");
		Assert.IsTrue (tag.RemoveCustomField (name));
		Assert.AreEqual (0, tag.GetCustomField (name).Count);
	}

	[TestMethod]
	public void Asf_TitleName_IsTheTitleProperty ()
	{
		var tag = new AsfTag ();

		tag.SetCustomField ("title", "Song");

		Assert.AreEqual ("Song", tag.Title);
	}

	[TestMethod]
	public void Asf_MappedAttribute_IsTheMappedProperty ()
	{
		var tag = new AsfTag { Album = "Old", Track = 5 };

		tag.SetCustomField ("WM/AlbumTitle", "New");

		Assert.AreEqual ("New", tag.Album);
		AssertValues (tag.GetCustomField ("WM/TrackNumber"), "5");
	}

	[TestMethod]
	public void Asf_EachValueIsItsOwnAttribute ()
	{
		var tag = new AsfTag ();

		tag.SetCustomField ("My/Field", "a", "b");

		var names = tag.ExtendedContentDescription.Descriptors.Select (d => d.Name).ToList ();
		AssertValues (names, "My/Field", "My/Field");
	}

	[TestMethod]
	public void Asf_PictureAndOverlongValues_Refused ()
	{
		var tag = new AsfTag ();

		Assert.IsFalse (tag.SetCustomField ("WM/Picture", "x"));
		Assert.IsFalse (tag.SetCustomField ("Long", new string ('x', 32767)));
		Assert.IsTrue (tag.SetCustomField ("Long", new string ('x', 32766)));
		Assert.IsFalse (tag.SetCustomField (new string ('N', 32767), "x"));
		Assert.AreEqual (1, tag.ExtendedContentDescription.Descriptors.Count);
	}

	// ───────────────────────── Matroska ─────────────────────────

	[TestMethod]
	public void Matroska_NameMatchesExactly ()
	{
		var tag = new MatroskaTag ();

		tag.SetCustomField ("MY_FIELD", "a");

		Assert.AreEqual (0, tag.GetCustomField ("my_field").Count);
	}

	// ───────────────────────── RIFF INFO ─────────────────────────

	[TestMethod]
	[DataRow ("ABC")]
	[DataRow ("ABCDE")]
	[DataRow ("ABÉD")]
	[DataRow ("AB\tD")]
	public void RiffInfo_NameNotAFourCC_Refused (string name)
	{
		var tag = new RiffInfoTag ();

		Assert.IsFalse (tag.SetCustomField (name, "x"));
		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void RiffInfo_OneValueOnlyAndLatin1Only ()
	{
		var tag = new RiffInfoTag ();

		Assert.IsFalse (tag.SetCustomField ("IXYZ", "a", "b"));
		Assert.IsFalse (tag.SetCustomField ("IXYZ", "日"));
		Assert.IsTrue (tag.IsEmpty);
		Assert.IsTrue (tag.SetCustomField ("IXYZ", "ÿ"));
	}

	[TestMethod]
	public void RiffInfo_MappedCode_IsTheMappedProperty ()
	{
		var tag = new RiffInfoTag { Title = "Old" };

		tag.SetCustomField ("INAM", "New");

		Assert.AreEqual ("New", tag.Title);
		AssertValues (tag.GetCustomField ("inam"), "New");
	}

	// ───────────────────────── Unified views ─────────────────────────

	[TestMethod]
	public void CombinedTag_WritesEverySupportingTagAndSkipsId3v1 ()
	{
		var v2 = new Id3v2Tag ();
		var v1 = new Id3v1Tag { Title = "t" };
		var v1Before = v1.Render ().ToArray ();
		var combined = new CombinedTag (v2, v1);

		Assert.IsTrue (combined.SupportsCustomFields);
		Assert.IsTrue (combined.SetCustomField ("My Field", "a", "b"));

		AssertValues (v2.GetCustomField ("My Field"), "a", "b");
		AssertValues (combined.GetCustomField ("My Field"), "a", "b");
		CollectionAssert.AreEqual (v1Before, v1.Render ().ToArray ());
		Assert.IsTrue (combined.RemoveCustomField ("My Field"));
		Assert.AreEqual (0, combined.GetCustomField ("My Field").Count);
	}

	[TestMethod]
	public void CombinedTag_ReadsTheFirstTagThatHoldsTheName ()
	{
		var first = new ApeTag ();
		var second = new ApeTag ();
		second.SetCustomField ("Key", "from second");
		var combined = new CombinedTag (null, first, second);

		AssertValues (combined.GetCustomField ("Key"), "from second");
		first.SetCustomField ("Key", "from first");
		AssertValues (combined.GetCustomField ("Key"), "from first");
	}

	[TestMethod]
	public void CombinedTag_OnlyId3v1_ReportsUnsupported ()
	{
		var combined = new CombinedTag (null, new Id3v1Tag ());

		Assert.IsFalse (combined.SupportsCustomFields);
		Assert.IsFalse (combined.SetCustomField ("My Field", "x"));
		Assert.IsFalse (combined.RemoveCustomField ("My Field"));
		Assert.ThrowsExactly<ArgumentNullException> (() => combined.SetCustomField (null!, "x"));
		Assert.ThrowsExactly<ArgumentNullException> (() => combined.GetCustomField (null!));
		Assert.ThrowsExactly<ArgumentNullException> (() => combined.RemoveCustomField (null!));
	}
}

/// <summary>
/// Minimal EBML element writer for wrapping a rendered <c>Tags</c> element in a file.
/// </summary>
static class MatroskaBytes
{
	public static byte[] Master (ulong id, byte[] payload) => Element (id, payload);

	public static byte[] Element (ulong id, ReadOnlySpan<byte> payload)
	{
		var idBytes = new List<byte> ();
		for (var shift = 24; shift >= 0; shift -= 8) {
			var b = (byte)(id >> shift);
			if (b != 0 || idBytes.Count > 0)
				idBytes.Add (b);
		}

		// An 8-byte size (0x01 marker) holds any test payload.
		var size = new byte[8];
		size[0] = 0x01;
		var length = (ulong)payload.Length;
		for (var i = 7; i >= 1; i--) {
			size[i] = (byte)length;
			length >>= 8;
		}

		return [.. idBytes, .. size, .. payload];
	}
}
