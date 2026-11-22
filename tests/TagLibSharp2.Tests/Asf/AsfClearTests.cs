// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Reflection;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mp4;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// Null always clears (the rule of <c>Tag.IsWriteBack</c>): only a write-back of the exact value read is a no-op.
/// A stored value the getter cannot read, such as the DWORD year mutagen writes for <c>f['WM/Year'] = [2020]</c>, is
/// removed by <c>= null</c>, together with the attribute's Metadata Library values, as before the write-back rule.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfClearTests
{
	static readonly string[] EmptyFirstSecond = ["", "First", "Second"];
	static readonly string[] ContentDescriptionProperties = ["Title", "Artist", "Copyright", "Comment", "Rating"];

	static IEnumerable<PropertyInfo> ScalarProperties () =>
		typeof (AsfTag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0)
			.Where (p => p.PropertyType == typeof (string) || p.PropertyType == typeof (uint?))
			.Where (p => !ContentDescriptionProperties.Contains (p.Name))
			.OrderBy (p => p.Name, StringComparer.Ordinal);

	public static IEnumerable<object[]> ScalarNames => ScalarProperties ().Select (p => new object[] { p.Name });

	/// <summary>The one Extended Content Description attribute a property writes, or null when it maps none.</summary>
	static string? AttributeOf (PropertyInfo property)
	{
		var probe = new AsfTag ();
		property.SetValue (probe, property.PropertyType == typeof (string) ? "-512" : (object)(uint?)3);
		return probe.ExtendedContentDescription.Descriptors.Count == 1
			? probe.ExtendedContentDescription.Descriptors[0].Name
			: null;
	}

	/// <summary>
	/// A stored type the property's getter does not read: a DWORD for a string attribute and for the disc
	/// "n/m" string, a WORD for a number attribute.
	/// </summary>
	static AsfDescriptor Unreadable (PropertyInfo property, string attribute) =>
		property.PropertyType == typeof (string) || attribute == "WM/PartOfSet"
			? AsfDescriptor.CreateDword (attribute, 2020)
			: AsfDescriptor.CreateWord (attribute, 5);

	static byte[] File (AsfDescriptor? stored, string attribute, string? contentTitle = null) =>
		[.. AsfTestBuilder.CreateHeaderObject ([
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfTestBuilder.CreateContentDescriptionObject (contentTitle, null),
			AsfMetadataLibraryTests.HeaderExtension (AsfMetadataLibraryTests.Library (AsfMetadataLibraryTests.Record (attribute, "second"))),
			.. stored is null ? Array.Empty<byte[]> () : [AsfTestBuilder.CreateExtendedContentDescriptionObject (stored)],
		]), .. AsfTestBuilder.CreateDataObject (0)];

	static AsfFile Read (byte[] data)
	{
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	[TestMethod]
	[DynamicData (nameof (ScalarNames))]
	public void NullOverAnUnreadableValue_ClearsItAndTheLibraryValues (string property)
	{
		var p = typeof (AsfTag).GetProperty (property)!;
		var attribute = AttributeOf (p);
		if (attribute is null)
			return; // ASF maps no attribute for this property.
		var original = File (Unreadable (p, attribute), attribute);
		var file = Read (original);
		// The getter cannot read the stored DWORD or WORD; it reads the library's string value, or nothing.
		Assert.IsTrue (p.GetValue (file.Tag) is null or "" or "second", $"{property} should not read the {attribute} it cannot parse");
		Assert.AreEqual (2, file.Tag.GetCustomField (attribute).Count, "the fixture holds two values");

		p.SetValue (file.Tag, null);
		var again = Read (file.Render (original)).Tag;

		Assert.IsFalse (again.ExtendedContentDescription.Descriptors.Any (d => d.Name == attribute), $"{property} = null kept {attribute}");
		Assert.AreEqual (0, again.GetCustomField (attribute).Count, $"{property} = null kept a value of {attribute}");
	}

	[TestMethod]
	public void NullOverAnUnreadableValue_CoversTheMappedAttributes ()
	{
		var covered = ScalarProperties ().Where (p => AttributeOf (p) is not null).Select (p => p.Name).ToList ();

		foreach (var name in new[] { "Year", "Album", "Genre", "Track", "DiscNumber", "TotalDiscs", "BeatsPerMinute", "TotalTracks", "R128TrackGain" })
			CollectionAssert.Contains (covered, name);
	}

	[TestMethod]
	public void IsCompilationFalse_OverAnUnreadableValue_WritesFalse ()
	{
		var original = File (AsfDescriptor.CreateDword ("WM/IsCompilation", 1), "WM/IsCompilation");
		var file = Read (original);
		Assert.IsFalse (file.Tag.IsCompilation);

		file.Tag.IsCompilation = false;
		var again = Read (file.Render (original)).Tag;

		var stored = again.ExtendedContentDescription.Descriptors.Single (d => d.Name == "WM/IsCompilation");
		Assert.AreEqual (AsfAttributeType.Bool, stored.Type);
		Assert.IsFalse (stored.BoolValue);
		Assert.AreEqual (0, again.GetCustomField ("WM/IsCompilation").Count);
	}

	[TestMethod]
	public void IsCompilationFalse_WhenNothingIsStored_WritesNothing ()
	{
		var original = File (null, "Other");
		var untouched = Read (original).Render (original);
		var file = Read (original);

		file.Tag.IsCompilation = false;

		CollectionAssert.AreEqual (untouched, file.Render (original));
	}

	[TestMethod]
	[DataRow ("Title", "Title")]
	[DataRow ("Artist", "Author")]
	[DataRow ("Copyright", "Copyright")]
	[DataRow ("Comment", "Description")]
	[DataRow ("Rating", "Rating")]
	public void NullOnAContentDescriptionField_ClearsItsLibraryValues (string property, string attribute)
	{
		var original = File (null, attribute);
		var file = Read (original);
		Assert.AreEqual (1, file.Tag.GetCustomField (attribute).Count);

		typeof (AsfTag).GetProperty (property)!.SetValue (file.Tag, null);
		var again = Read (file.Render (original)).Tag;

		Assert.AreEqual (0, again.GetCustomField (attribute).Count);
	}

	[TestMethod]
	public void LibraryOnlyValue_IsTheFirstNonEmptyValue_AndEmptyIsAClear ()
	{
		// A single-value getter reads the first non-empty value (the rule the ID3v2, APE, and Vorbis Comment
		// getters follow), here the Metadata Library's, when the Extended Content Description holds none.
		var original = File (null, "WM/AlbumTitle");
		var untouched = Read (original).Render (original);
		var file = Read (original);
		Assert.AreEqual ("second", file.Tag.Album);

		file.Tag.Album = file.Tag.Album;
		CollectionAssert.AreEqual (untouched, file.Render (original), "a write-back changes nothing");

		file.Tag.Album = "";
		Assert.AreEqual (0, Read (file.Render (original)).Tag.GetCustomField ("WM/AlbumTitle").Count, "\"\" over text is a clear");
	}

	[TestMethod]
	public void EmptyFirstValue_ReadsTheFirstNonEmptyValue ()
	{
		var original = File (AsfDescriptor.CreateString ("WM/AlbumTitle", ""), "WM/AlbumTitle", contentTitle: null);
		var file = Read (original);

		Assert.AreEqual ("second", file.Tag.Album);
		var withTitle = Read (File (null, "Title"));
		Assert.AreEqual ("second", withTitle.Tag.Title, "an empty Content Description field falls through to the library");
	}

	[TestMethod]
	[DataRow ("Performers", "Artist")]
	[DataRow ("AlbumArtists", "AlbumArtist")]
	[DataRow ("Composers", "Composer")]
	[DataRow ("Genres", "Genre")]
	public void ArraySetter_UnreadableLibrary_WritesTheFirstValueAndKeepsTheLibraryBytes (string arrayProperty, string scalarProperty)
	{
		// Count says two records, the object holds one and a half, so the tag cannot see every value there.
		var library = AsfMetadataLibraryTests.Library (
			AsfMetadataLibraryTests.Record ("Other", "x"), AsfMetadataLibraryTests.Record ("Cut", "off"));
		BinaryPrimitives.WriteUInt64LittleEndian (library.AsSpan (16), (ulong)(library.Length - 6));
		var truncated = library.AsSpan (0, library.Length - 6).ToArray ();
		byte[] original = [.. AsfTestBuilder.CreateHeaderObject (
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfMetadataLibraryTests.HeaderExtension (truncated)), .. AsfTestBuilder.CreateDataObject (0)];
		var file = Read (original);

		// The first non-empty entry (Tag.FirstNonEmpty), as every list-to-single setter stores.
		typeof (AsfTag).GetProperty (arrayProperty)!.SetValue (file.Tag, EmptyFirstSecond);
		var saved = file.Render (original);

		Assert.AreEqual ("First", typeof (AsfTag).GetProperty (scalarProperty)!.GetValue (Read (saved).Tag));
		Assert.IsTrue (saved.AsSpan ().IndexOf (truncated) >= 0, "the library the tag cannot parse is kept as it was");
	}

	public static IEnumerable<object[]> R128Formats => [["vorbis"], ["ape"], ["id3v24"], ["id3v23"], ["mp4"], ["asf"]];

	static Tag NewTag (string format) => format switch {
		"vorbis" => new VorbisComment ("test"),
		"ape" => new ApeTag (),
		"id3v24" => new Id3v2Tag (Id3v2Version.V24),
		"id3v23" => new Id3v2Tag (Id3v2Version.V23),
		"mp4" => new Mp4Tag (),
		_ => new AsfTag (),
	};

	[TestMethod]
	[DynamicData (nameof (R128Formats))]
	public void R128GainDbNull_ClearsTextItCannotParse (string format)
	{
		foreach (var text in new[] { "abc", "99999" }) {
			var tag = NewTag (format);
			tag.R128TrackGain = text;
			tag.R128AlbumGain = text;
			Assert.IsNull (tag.R128TrackGainDb);

			tag.R128TrackGainDb = null;
			tag.R128AlbumGainDb = null;

			Assert.IsNull (tag.R128TrackGain, $"{format} {text}");
			Assert.IsNull (tag.R128AlbumGain, $"{format} {text}");
		}
	}

	[TestMethod]
	[DynamicData (nameof (R128Formats))]
	public void R128GainDbWriteBack_KeepsTheStoredText (string format)
	{
		var tag = NewTag (format);
		tag.R128TrackGain = "-0512";

		tag.R128TrackGainDb = tag.R128TrackGainDb;

		Assert.AreEqual ("-0512", tag.R128TrackGain);
	}
}
