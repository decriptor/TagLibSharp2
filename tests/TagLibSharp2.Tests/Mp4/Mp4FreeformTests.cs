// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Freeform <c>----</c> atoms: which <c>data</c> types are text, and keys whose mean or name holds a colon.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4FreeformTests
{
	static void AssertValues (IReadOnlyList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	public void Mp4_ImplicitAndUtf8Data_AreText (int typeIndicator)
	{
		// Apple's well-known types: 0 is implicit (no type indicated), 1 is UTF-8. GetFreeformTag reads both.
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;
		file.Tag = new Mp4Tag ();
		file.Tag.SetFreeformAtoms ("com.apple.iTunes", "Implicit",
			[new Mp4DataAtom (typeIndicator, BinaryData.FromStringUtf8 ("implicit"))]);
		var again = Mp4File.Read (file.Render (original).Span).File!;

		Assert.AreEqual ("implicit", again.GetFreeformTag ("com.apple.iTunes", "Implicit"));
		AssertValues (again.Tag!.GetCustomField ("Implicit"), "implicit");
	}

	[TestMethod]
	public void Mp4_NonTextData_IsNotAValue ()
	{
		var tag = new Mp4Tag ();
		tag.SetFreeformAtoms ("com.apple.iTunes", "Blob", [
			new Mp4DataAtom (13, new BinaryData ([0xFF, 0xD8])),
			new Mp4DataAtom (1, BinaryData.FromStringUtf8 ("text"))]);

		AssertValues (tag.GetCustomField ("Blob"), "text");
	}

	[TestMethod]
	[DataRow ("a:b", "c")]
	[DataRow ("a", "b:c")]
	[DataRow ("x\\y", "z")]
	[DataRow ("a\\:b", "c\\d")]
	[DataRow (":lead", "trail:")]
	public void Mp4_FreeformMeanAndName_RoundTripWhateverTheyHold (string mean, string name)
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;

		file.SetFreeformTag (mean, name, "val");
		var rendered = file.Render (original).ToArray ();

		// mean and name are full boxes: size, type, version and flags, then the UTF-8 text.
		byte[] meanBox = [.. "mean"u8, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes (mean)];
		byte[] nameBox = [.. "name"u8, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes (name)];
		Assert.IsTrue (rendered.AsSpan ().IndexOf (meanBox) >= 0, "the mean is written as given");
		Assert.IsTrue (rendered.AsSpan ().IndexOf (nameBox) >= 0, "the name is written as given");
		var again = Mp4File.Read (rendered).File!;
		Assert.AreEqual ("val", again.GetFreeformTag (mean, name));
		CollectionAssert.AreEqual (rendered, again.Render (rendered).ToArray (), "an unchanged save keeps the atom");
	}

	[TestMethod]
	public void Mp4_FreeformMeanWithColon_IsNotAnotherMeanAndName ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;

		file.SetFreeformTag ("a:b", "c", "one");
		file.SetFreeformTag ("a", "b:c", "two");
		var again = Mp4File.Read (file.Render (original).Span).File!;

		Assert.AreEqual ("one", again.GetFreeformTag ("a:b", "c"));
		Assert.AreEqual ("two", again.GetFreeformTag ("a", "b:c"));
	}

	// ── #133's two metas: custom fields follow the freeform fields ──

	static byte[] KeyedFile (string[] keys, params byte[][] items) =>
		TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload (keys), items, inMovie: false, udtaExtra: null);

	static int Count (byte[] data, byte[] needle)
	{
		var count = 0;
		for (var at = data.AsSpan ().IndexOf (needle); at >= 0;) {
			count++;
			var next = data.AsSpan (at + 1).IndexOf (needle);
			at = next < 0 ? -1 : at + 1 + next;
		}
		return count;
	}

	static readonly string[] AB = ["a", "b"];
	static readonly string[] Kept = ["kept"];

	[TestMethod]
	public void Mdta_SetCustomField_WritesAKeyedItemNotAnITunesAtom ()
	{
		var data = KeyedFile (["com.apple.quicktime.artist"], TestBuilders.Mp4.CreateMdtaItem (1, "Artist"));
		var file = Mp4File.Read (data).File!;

		Assert.IsTrue (file.Tag!.SetCustomField ("MYFIELD", "a", "b"));
		var rendered = file.Render (data).ToArray ();

		Mp4QuickTimeKeysTests.AssertOnlyKeyedItems (rendered, 2);
		Assert.AreEqual (0, Count (rendered, "----"u8.ToArray ()), "an mdta meta holds no iTunes freeform atom");
		Assert.AreEqual (1, Count (rendered, "MYFIELD"u8.ToArray ()), "the name is one key");
		CollectionAssert.AreEqual (AB, Mp4File.Read (rendered).File!.Tag!.GetCustomField ("MYFIELD").ToArray ());
	}

	[TestMethod]
	public void Mdta_ReadField_IsACustomFieldAndRemoveLeavesNothing ()
	{
		// ffmpeg writes an unmapped name as a key; it reads as a com.apple.iTunes freeform field.
		var data = KeyedFile (["com.apple.quicktime.artist", "MYFIELD"],
			TestBuilders.Mp4.CreateMdtaItem (1, "Artist"), TestBuilders.Mp4.CreateMdtaItem (2, "kept"));
		var file = Mp4File.Read (data).File!;
		CollectionAssert.AreEqual (Kept, file.Tag!.GetCustomField ("MYFIELD").ToArray ());

		Assert.IsTrue (file.Tag.RemoveCustomField ("MYFIELD"));
		var rendered = file.Render (data).ToArray ();

		// The item goes; its keys entry stays, as for every field a set removes in an mdta meta (only Clear drops
		// keys), so the other items keep their indexes. A key no item uses shows nothing.
		Assert.AreEqual (0, Count (rendered, "kept"u8.ToArray ()), "the item is gone");
		Mp4QuickTimeKeysTests.AssertOnlyKeyedItems (rendered, 2);
		var again = Mp4File.Read (rendered).File!.Tag!;
		Assert.AreEqual (0, again.GetCustomField ("MYFIELD").Count);
		Assert.AreEqual ("Artist", again.Artist, "the other key stays");
		Assert.IsFalse (again.RemoveCustomField ("MYFIELD"), "nothing is left to remove");
	}

	[TestMethod]
	public void ITunes_RemoveCustomField_DropsTheAtomReadFromTheFile ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (original).File!;
		file.Tag = new Mp4Tag ();
		file.Tag.SetCustomField ("MYFIELD", "kept");
		var saved = file.Render (original).ToArray ();

		var again = Mp4File.Read (saved).File!;
		Assert.IsTrue (again.Tag!.RemoveCustomField ("MYFIELD"));
		var rendered = again.Render (saved).ToArray ();

		Assert.AreEqual (0, Count (rendered, "MYFIELD"u8.ToArray ()));
		Assert.AreEqual (0, Mp4File.Read (rendered).File!.Tag?.GetCustomField ("MYFIELD").Count ?? 0);
	}

	[TestMethod]
	[DataRow ("mdta")]
	[DataRow ("itunes")]
	public void Clear_LeavesNoCustomField (string layout)
	{
		byte[] data;
		if (layout == "mdta") {
			data = KeyedFile (["MYFIELD"], TestBuilders.Mp4.CreateMdtaItem (1, "kept"));
		} else {
			var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
			var built = Mp4File.Read (original).File!;
			built.Tag = new Mp4Tag ();
			built.Tag.SetCustomField ("MYFIELD", "kept");
			data = built.Render (original).ToArray ();
		}
		var file = Mp4File.Read (data).File!;
		Assert.AreEqual (1, file.Tag!.GetCustomField ("MYFIELD").Count);

		file.Tag.Clear ();
		var rendered = file.Render (data).ToArray ();

		Assert.AreEqual (0, Count (rendered, "MYFIELD"u8.ToArray ()));
		Assert.AreEqual (0, Count (rendered, "kept"u8.ToArray ()));
	}

	[TestMethod]
	public void CopyTo_KeepsTheTargetsCustomField ()
	{
		// A copy writes mapped fields only; a custom field, like any item the copy does not map, stays.
		var data = KeyedFile (["com.apple.quicktime.artist", "MYFIELD"],
			TestBuilders.Mp4.CreateMdtaItem (1, "Old"), TestBuilders.Mp4.CreateMdtaItem (2, "kept"));
		var file = Mp4File.Read (data).File!;

		new TagLibSharp2.Xiph.VorbisComment ("v") { Artist = "New", Title = "T" }.CopyTo (file.Tag!, TagCopyOptions.All);
		var again = Mp4File.Read (file.Render (data).ToArray ()).File!.Tag!;

		Assert.AreEqual ("New", again.Artist);
		CollectionAssert.AreEqual (Kept, again.GetCustomField ("MYFIELD").ToArray ());
	}
}
