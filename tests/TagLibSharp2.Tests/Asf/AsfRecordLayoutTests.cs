// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using TagLibSharp2.Asf;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// The number and BOOL properties read an attribute the way the string properties do: the first non-empty value,
/// from the Extended Content Description and then the file-level Metadata and Metadata Library records (ASF §4.7,
/// §4.8). Over every layout a write-back of the value read changes nothing, null (or false) clears every record of
/// the attribute, and a new value replaces them all.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfRecordLayoutTests
{
	static readonly string[] Nine = ["9"];
	static readonly string[] Five = ["5"];

	public static IEnumerable<object[]> NumberProperties => [
		["Track", "WM/TrackNumber"],
		["TotalTracks", "WM/TrackTotal"],
		["BeatsPerMinute", "WM/BeatsPerMinute"],
		["MovementNumber", "WM/MovementNumber"],
		["MovementTotal", "WM/MovementTotal"],
	];

	/// <summary>Layouts that hold the number 7 where the getter can find it, as mutagen writes them.</summary>
	public static IEnumerable<object[]> NumberLayouts => [
		["empty ECD string, Metadata string"],
		["empty ECD string, Library DWORD"],
		["Metadata DWORD only"],
		["Library string only"],
		["ECD DWORD, Library DWORD 8"],
		["empty Metadata string, Library DWORD"],
	];

	public static IEnumerable<object[]> NumberCells =>
		NumberProperties.SelectMany (p => NumberLayouts.Select (l => new object[] { p[0], p[1], l[0] }));

	static byte[] Dword (uint value)
	{
		var data = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (data, value);
		return data;
	}

	/// <summary>A BOOL in the Metadata and Metadata Library Objects is a WORD (ASF §4.7, §4.8).</summary>
	static byte[] RecordBool (bool value) => [value ? (byte)1 : (byte)0, 0];

	static byte[] NumberFile (string attribute, string layout) => layout switch {
		"empty ECD string, Metadata string" => File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.Record (attribute, "7")),
			AsfDescriptor.CreateString (attribute, "")),
		"empty ECD string, Library DWORD" => File (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.RawRecord (attribute, 3, Dword (7))),
			AsfDescriptor.CreateString (attribute, "")),
		"Metadata DWORD only" => File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord (attribute, 3, Dword (7)))),
		"Library string only" => File (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.Record (attribute, "7"))),
		"empty Metadata string, Library DWORD" => File ([
			.. AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.Record (attribute, "")),
			.. AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.RawRecord (attribute, 3, Dword (7)))]),
		"ECD DWORD, Library DWORD 8" => File (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.RawRecord (attribute, 3, Dword (8))),
			AsfDescriptor.CreateDword (attribute, 7)),
		_ => throw new ArgumentOutOfRangeException (nameof (layout))
	};

	static byte[] File (byte[] records, params AsfDescriptor[] descriptors) =>
		[.. AsfTestBuilder.CreateHeaderObject ([
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfMetadataLibraryTests.HeaderExtension (records),
			.. descriptors.Length == 0 ? Array.Empty<byte[]> () : [AsfTestBuilder.CreateExtendedContentDescriptionObject (descriptors)],
		]), .. AsfTestBuilder.CreateDataObject (0)];

	static AsfFile Read (byte[] data)
	{
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	/// <summary>How many times the attribute's name appears in the file: once per stored value.</summary>
	static int Occurrences (byte[] file, string attribute)
	{
		var name = Encoding.Unicode.GetBytes (attribute + "\0");
		var count = 0;
		for (var i = 0; i + name.Length <= file.Length; i++) {
			if (file.AsSpan (i, name.Length).SequenceEqual (name))
				count++;
		}
		return count;
	}

	[TestMethod]
	[DynamicData (nameof (NumberCells))]
	public void Number_ReadsTheFirstNonEmptyValue_AndAWriteBackChangesNothing (string property, string attribute, string layout)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var original = NumberFile (attribute, layout);
		var untouched = Read (original).Render (original);
		var file = Read (original);

		Assert.AreEqual ((uint?)7, p.GetValue (file.Tag), $"{property} over {layout}");
		p.SetValue (file.Tag, p.GetValue (file.Tag));

		CollectionAssert.AreEqual (untouched, file.Render (original), $"{property} = {property} over {layout} changed the file");
	}

	[TestMethod]
	[DynamicData (nameof (NumberCells))]
	public void Number_NullClearsEveryRecord (string property, string attribute, string layout)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var original = NumberFile (attribute, layout);
		var file = Read (original);

		p.SetValue (file.Tag, null);
		var saved = file.Render (original);
		var again = Read (saved).Tag;

		Assert.IsNull (p.GetValue (again), $"{property} = null over {layout}");
		Assert.AreEqual (0, Occurrences (saved, attribute), $"{property} = null over {layout} kept a value");
	}

	[TestMethod]
	[DynamicData (nameof (NumberCells))]
	public void Number_NewValueReplacesEveryRecord (string property, string attribute, string layout)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var original = NumberFile (attribute, layout);
		var file = Read (original);

		p.SetValue (file.Tag, (uint?)9);
		var again = Read (file.Render (original)).Tag;

		Assert.AreEqual ((uint?)9, p.GetValue (again));
		CollectionAssert.AreEqual (Nine, again.GetCustomField (attribute).ToArray (), $"{property} = 9 over {layout}");
	}

	[TestMethod]
	public void NumberProperties_CoverEveryNumberAttribute ()
	{
		var listed = NumberProperties.Select (p => (string)p[0]).ToList ();
		foreach (var p in typeof (AsfTag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.CanRead && p.CanWrite && p.PropertyType == typeof (uint?))) {
			var probe = new AsfTag ();
			p.SetValue (probe, (uint?)3);
			var written = probe.ExtendedContentDescription.Descriptors;
			if (written.Count == 1 && written[0].Type == AsfAttributeType.Dword)
				CollectionAssert.Contains (listed, p.Name, $"{p.Name} writes a DWORD; add it to the layout matrix");
		}
	}

	/// <summary>Layouts of WM/IsCompilation and what <c>IsCompilation</c> reads from each.</summary>
	public static IEnumerable<object[]> BoolLayouts => [
		["Metadata true only", true],
		["Library true only", true],
		["empty ECD string, Metadata true", true],
		["ECD true, Library false", true],
		["Metadata false only", false],
		["ECD false, Library true", false],
		["Metadata DWORD 1 only", false],
	];

	/// <summary>Layouts where the getter reads false but something else is stored, so false is a clear.</summary>
	static readonly string[] FalseClears = ["ECD false, Library true", "Metadata DWORD 1 only"];

	static byte[] BoolFile (string layout)
	{
		const string name = "WM/IsCompilation";
		return layout switch {
			"Metadata true only" => File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord (name, 2, RecordBool (true)))),
			"Library true only" => File (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.RawRecord (name, 2, RecordBool (true)))),
			"empty ECD string, Metadata true" => File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord (name, 2, RecordBool (true))),
				AsfDescriptor.CreateString (name, "")),
			"ECD true, Library false" => File (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.RawRecord (name, 2, RecordBool (false))),
				AsfDescriptor.CreateBool (name, true)),
			"Metadata false only" => File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord (name, 2, RecordBool (false)))),
			"Metadata DWORD 1 only" => File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord (name, 3, Dword (1)))),
			"ECD false, Library true" => File (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.RawRecord (name, 2, RecordBool (true))),
				AsfDescriptor.CreateBool (name, false)),
			_ => throw new ArgumentOutOfRangeException (nameof (layout))
		};
	}

	[TestMethod]
	[DynamicData (nameof (BoolLayouts))]
	public void IsCompilation_ReadsTheFirstValue_AndAWriteBackChangesNothing (string layout, bool expected)
	{
		var original = BoolFile (layout);
		var untouched = Read (original).Render (original);
		var file = Read (original);

		Assert.AreEqual (expected, file.Tag.IsCompilation, layout);
		if (FalseClears.Contains (layout))
			return; // False over a stored true or a DWORD is a clear (IsCompilation_FalseClearsEveryTrue).
		file.Tag.IsCompilation = expected;

		CollectionAssert.AreEqual (untouched, file.Render (original), $"IsCompilation = {expected} over {layout} changed the file");
	}

	[TestMethod]
	[DynamicData (nameof (BoolLayouts))]
	public void IsCompilation_FalseClearsEveryTrue (string layout, bool expected)
	{
		_ = expected;
		var original = BoolFile (layout);
		var file = Read (original);

		file.Tag.IsCompilation = false;
		var saved = file.Render (original);
		var again = Read (saved).Tag;

		Assert.IsFalse (again.IsCompilation, layout);
		if (layout == "Metadata false only") {
			CollectionAssert.AreEqual (Read (original).Render (original), saved, "false over a stored false changes nothing");
			return;
		}
		// The one value left is the Extended Content Description BOOL false the setter writes.
		Assert.AreEqual (1, Occurrences (saved, "WM/IsCompilation"), $"IsCompilation = false over {layout} kept a record");
		var stored = again.ExtendedContentDescription.Descriptors.Single (d => d.Name == "WM/IsCompilation");
		Assert.AreEqual (AsfAttributeType.Bool, stored.Type);
		Assert.IsFalse (stored.BoolValue);
	}

	[TestMethod]
	[DynamicData (nameof (BoolLayouts))]
	public void IsCompilation_TrueOverAFalseReplacesEveryRecord (string layout, bool expected)
	{
		var original = BoolFile (layout);
		var file = Read (original);
		if (expected)
			return; // A write-back (IsCompilation_ReadsTheFirstValue_AndAWriteBackChangesNothing).

		file.Tag.IsCompilation = true;
		var saved = file.Render (original);
		var again = Read (saved).Tag;

		Assert.IsTrue (again.IsCompilation, layout);
		Assert.AreEqual (1, Occurrences (saved, "WM/IsCompilation"), $"IsCompilation = true over {layout} kept a record");
	}

	[TestMethod]
	public void IsCompilation_NullBoolRecord_IsNotTrue ()
	{
		// A BOOL record too short to hold its WORD reads as no value.
		var original = File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord ("WM/IsCompilation", 2, [])));
		Assert.IsFalse (Read (original).Tag.IsCompilation);
	}

	[TestMethod]
	public void EmptyString_OverAnEmptyStringAndANumber_ClearsBoth ()
	{
		// mutagen's WM/AlbumTitle = [Unicode '', DWORD 5]: Album reads "", yet "" is not the value read, because
		// another value of the attribute is not empty. So "" is a clear.
		var original = File (AsfMetadataLibraryTests.Metadata (AsfMetadataLibraryTests.RawRecord ("WM/AlbumTitle", 3, Dword (5))),
			AsfDescriptor.CreateString ("WM/AlbumTitle", ""));
		var file = Read (original);
		Assert.AreEqual ("", file.Tag.Album);
		CollectionAssert.AreEqual (Five, file.Tag.GetCustomField ("WM/AlbumTitle").ToArray (), "the DWORD is the one value with text");

		file.Tag.Album = "";
		var saved = file.Render (original);

		Assert.AreEqual (0, Occurrences (saved, "WM/AlbumTitle"), "\"\" over [\"\", 5] kept a value");
	}

	public static IEnumerable<object[]> ContentDescriptionFields => [
		["Title", "Title"],
		["Artist", "Author"],
		["Copyright", "Copyright"],
		["Comment", "Description"],
		["Rating", "Rating"],
	];

	/// <summary>
	/// ffmpeg writes each Content Description field a second time as an Extended Content Description descriptor of the
	/// same name, so mutagen reads <c>Author</c> as <c>['old', 'old']</c> and ffprobe shows the descriptor.
	/// </summary>
	static byte[] DuplicatedFile (string attribute, string? fieldValue, string descriptorValue) =>
		[.. AsfTestBuilder.CreateHeaderObject ([
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfTestBuilder.CreateContentDescriptionObject (
				attribute == "Title" ? fieldValue : null,
				attribute == "Author" ? fieldValue : null,
				attribute == "Copyright" ? fieldValue : null,
				attribute == "Description" ? fieldValue : null,
				attribute == "Rating" ? fieldValue : null),
			AsfTestBuilder.CreateExtendedContentDescriptionObject (AsfDescriptor.CreateString (FfmpegSpelling (attribute), descriptorValue)),
		]), .. AsfTestBuilder.CreateDataObject (0)];

	/// <summary>
	/// The name ffmpeg gives the descriptor copy: <c>title</c>, <c>copyright</c>, and <c>rating</c> in lower case,
	/// <c>Author</c> and <c>Description</c> as the Content Description names them. Every call below uses the
	/// documented name, so a match that minds case leaves three of the five copies behind.
	/// </summary>
	static string FfmpegSpelling (string attribute) => attribute switch {
		"Title" => "title",
		"Copyright" => "copyright",
		"Rating" => "rating",
		_ => attribute,
	};

	static readonly string[] Old = ["old"];
	static readonly string[] Only = ["only"];

	[TestMethod]
	[DynamicData (nameof (ContentDescriptionFields))]
	public void ContentDescriptionField_WithItsDescriptorTwin_ReadsOnceAndAWriteBackChangesNothing (string property, string attribute)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var original = DuplicatedFile (attribute, "old", "old");
		var untouched = Read (original).Render (original);
		var file = Read (original);

		Assert.AreEqual ("old", p.GetValue (file.Tag));
		CollectionAssert.AreEqual (Old, file.Tag.GetCustomField (attribute).ToArray ());
		p.SetValue (file.Tag, p.GetValue (file.Tag));

		CollectionAssert.AreEqual (untouched, file.Render (original));
	}

	[TestMethod]
	[DynamicData (nameof (ContentDescriptionFields))]
	public void ContentDescriptionField_NewValue_ReplacesItsDescriptorTwin (string property, string attribute)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var original = DuplicatedFile (attribute, "old", "old");
		var file = Read (original);

		p.SetValue (file.Tag, "V2");
		var again = Read (file.Render (original)).Tag;

		Assert.AreEqual ("V2", p.GetValue (again));
		Assert.IsFalse (again.ExtendedContentDescription.Descriptors.Any (d => string.Equals (d.Name, attribute, StringComparison.OrdinalIgnoreCase)), $"{property} = V2 left the old {attribute} descriptor");
	}

	[TestMethod]
	[DynamicData (nameof (ContentDescriptionFields))]
	public void ContentDescriptionField_CustomFieldWriteAndRemove_ReplaceItsDescriptorTwin (string property, string attribute)
	{
		_ = property;
		var original = DuplicatedFile (attribute, "old", "old");
		var file = Read (original);
		Assert.IsTrue (file.Tag.SetCustomField (attribute, "a", "b"));
		var set = Read (file.Render (original)).Tag;
		Assert.IsFalse (set.ExtendedContentDescription.Descriptors.Any (d => string.Equals (d.Name, attribute, StringComparison.OrdinalIgnoreCase)), "SetCustomField left the descriptor");

		file = Read (original);
		Assert.IsTrue (file.Tag.RemoveCustomField (attribute));
		var removed = Read (file.Render (original)).Tag;
		Assert.IsFalse (removed.ExtendedContentDescription.Descriptors.Any (d => string.Equals (d.Name, attribute, StringComparison.OrdinalIgnoreCase)), "RemoveCustomField left the descriptor");
		Assert.AreEqual (0, removed.GetCustomField (attribute).Count);
	}

	[TestMethod]
	[DynamicData (nameof (ContentDescriptionFields))]
	public void ContentDescriptionField_EmptyField_ReadsItsDescriptorTwin (string property, string attribute)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var file = Read (DuplicatedFile (attribute, null, "only"));

		Assert.AreEqual ("only", p.GetValue (file.Tag));
		CollectionAssert.AreEqual (Only, file.Tag.GetCustomField (attribute).ToArray ());
		Assert.IsTrue (file.Tag.RemoveCustomField (attribute), "a descriptor-only value is removed");
	}

	/// <summary>
	/// The file ffmpeg writes for <c>-metadata artist=old -metadata title=T</c>: the Content Description holds Title and
	/// Author, and the Extended Content Description holds them again as <c>Author</c> and a lowercase <c>title</c>.
	/// </summary>
	static byte[] FfmpegFile () =>
		[.. AsfTestBuilder.CreateHeaderObject ([
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfTestBuilder.CreateContentDescriptionObject ("T", "old"),
			AsfTestBuilder.CreateExtendedContentDescriptionObject (
				AsfDescriptor.CreateString ("Author", "old"), AsfDescriptor.CreateString ("title", "T")),
		]), .. AsfTestBuilder.CreateDataObject (0)];

	static readonly string[] NewN2 = ["new", "n2"];
	static readonly string[] New = ["new"];

	static bool HasDescriptor (AsfTag tag, string name) =>
		tag.ExtendedContentDescription.Descriptors.Any (d => string.Equals (d.Name, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>Every write of Author or Title leaves no stale Extended Content Description copy behind.</summary>
	[TestMethod]
	[DataRow ("RemoveCustomField Author")]
	[DataRow ("RemoveCustomField title")]
	[DataRow ("SetCustomField Author")]
	[DataRow ("Performers")]
	[DataRow ("Artist")]
	[DataRow ("Title")]
	[DataRow ("CopyTo")]
	public void FfmpegFile_EveryWrite_LeavesNoStaleDescriptor (string write)
	{
		var original = FfmpegFile ();
		var file = Read (original);
		var tag = file.Tag;
		string attribute;
		string[] expected;
		switch (write) {
			case "RemoveCustomField Author":
				Assert.IsTrue (tag.RemoveCustomField ("Author"));
				(attribute, expected) = ("Author", []);
				break;
			case "RemoveCustomField title":
				Assert.IsTrue (tag.RemoveCustomField ("title"), "the Content Description name matches in any case");
				(attribute, expected) = ("Title", []);
				break;
			case "SetCustomField Author":
				Assert.IsTrue (tag.SetCustomField ("Author", "new"));
				(attribute, expected) = ("Author", New);
				break;
			case "Performers":
				tag.Performers = NewN2;
				(attribute, expected) = ("Author", NewN2);
				break;
			case "Artist":
				tag.Artist = "new";
				(attribute, expected) = ("Author", New);
				break;
			case "Title":
				tag.Title = "new";
				(attribute, expected) = ("Title", New);
				break;
			default:
				var source = new VorbisComment ("source") { Artist = "new", Title = "new" };
				source.CopyTo (tag);
				(attribute, expected) = ("Author", New);
				break;
		}

		var again = Read (file.Render (original)).Tag;

		Assert.IsFalse (HasDescriptor (again, attribute), $"{write} left the old {attribute} descriptor");
		CollectionAssert.AreEqual (expected, again.GetCustomField (attribute).ToArray (), write);
	}

	[TestMethod]
	public void FfmpegFile_WriteBack_ChangesNothing ()
	{
		var original = FfmpegFile ();
		var untouched = Read (original).Render (original);
		var file = Read (original);

		Assert.AreEqual ("old", file.Tag.Artist);
		CollectionAssert.AreEqual (Old, file.Tag.Performers);
		file.Tag.Artist = file.Tag.Artist;
		file.Tag.Title = file.Tag.Title;
		file.Tag.Performers = file.Tag.Performers;
		Assert.IsTrue (file.Tag.SetCustomField ("Author", [.. file.Tag.GetCustomField ("Author")]));

		// SetCustomField always rewrites, so compare what it leaves: one value, and the Title copy untouched.
		var again = Read (file.Render (original)).Tag;
		CollectionAssert.AreEqual (Old, again.GetCustomField ("Author").ToArray ());
		Assert.IsFalse (HasDescriptor (again, "Author"));
		Assert.IsTrue (HasDescriptor (again, "title"), "a write of Author leaves the title copy alone");

		var mapped = Read (original);
		mapped.Tag.Artist = mapped.Tag.Artist;
		mapped.Tag.Title = mapped.Tag.Title;
		mapped.Tag.Performers = mapped.Tag.Performers;
		CollectionAssert.AreEqual (untouched, mapped.Render (original), "a mapped write-back changes nothing");
	}

	/// <summary>
	/// The twin of the Content Description case rule for every other name: ASF attribute names match in any case
	/// (the library's rule since the Extended Content Description reader), so a set or remove by one spelling
	/// replaces a descriptor and the file-level records stored under another.
	/// </summary>
	[TestMethod]
	public void OtherName_SetAndRemove_MatchAnySpelling ()
	{
		static byte[] Stored () => File (
			AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.Record ("wm/albumtitle", "lib")),
			AsfDescriptor.CreateString ("WM/AlbumTitle", "old"));

		var file = Read (Stored ());
		Assert.IsTrue (file.Tag.SetCustomField ("wm/ALBUMTITLE", "new"));
		var set = Read (file.Render (Stored ())).Tag;
		CollectionAssert.AreEqual (New, set.GetCustomField ("WM/AlbumTitle").ToArray ());
		Assert.AreEqual (1, set.ExtendedContentDescription.Descriptors.Count (d => string.Equals (d.Name, "WM/AlbumTitle", StringComparison.OrdinalIgnoreCase)));

		file = Read (Stored ());
		Assert.IsTrue (file.Tag.RemoveCustomField ("WM/ALBUMTITLE"));
		var removed = Read (file.Render (Stored ())).Tag;
		Assert.AreEqual (0, removed.GetCustomField ("wm/albumtitle").Count);
		Assert.IsFalse (HasDescriptor (removed, "WM/AlbumTitle"));
	}
}
