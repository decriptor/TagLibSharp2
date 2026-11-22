// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// Writing a mapped property's own value back changes nothing, on a file whose attributes also hold values
/// 2..n in the Metadata Library Object (the layout Windows writes; ASF spec rev. 01.20.03 §4.8 and
/// https://mutagen-specs.readthedocs.io/en/latest/asf/index.html "Multiple Values"). The array properties read
/// every value, so a read-modify-write of them keeps the file's values too.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfMappedWriteBackTests
{
	static readonly string[] ContentDescriptionNames = ["Title", "Author", "Copyright", "Description", "Rating"];
	static readonly string[] Xyz = ["x", "y", "z"];
	static readonly string[] Only = ["only"];
	static readonly string[] NewArtist = ["New"];
	static readonly string[] NewGenre = ["New genre"];
	static readonly string[] Nine = ["9"];

	/// <summary>Every public mapped property of <see cref="AsfTag"/> that can be read and written.</summary>
	static IEnumerable<PropertyInfo> MappedProperties () =>
		typeof (AsfTag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0)
			.OrderBy (p => p.Name, StringComparer.Ordinal);

	public static IEnumerable<object[]> PropertyNames => MappedProperties ().Select (p => new object[] { p.Name });

	static object? Sample (PropertyInfo p) => p.Name is "R128TrackGain" or "R128AlbumGain"
		// A Q7.8 integer, so the dB views read it and their write-back is a write-back.
		? "-512"
		: p.PropertyType switch {
			var t when t == typeof (string) => "v " + p.Name,
			var t when t == typeof (uint?) => (uint?)7,
			var t when t == typeof (bool) => true,
			var t when t == typeof (string[]) => new[] { "first " + p.Name },
			var t when t == typeof (IPicture[]) => Array.Empty<IPicture> (),
			// R128 gain in dB is a view over the R128 string properties, which get their own sample.
			var t when t == typeof (double?) => null,
			_ => throw new InvalidOperationException ($"no sample for {p.Name} ({p.PropertyType})")
		};

	/// <summary>
	/// A WMA with every mapped property set, and a second value for every attribute in the Metadata Library.
	/// </summary>
	static (byte[] File, string[] Names) MultiValuedFile ()
	{
		var tag = new AsfTag ();
		foreach (var p in MappedProperties ()) {
			if (Sample (p) is { } sample)
				p.SetValue (tag, sample);
		}

		var names = ContentDescriptionNames
			.Where (n => tag.GetCustomField (n).Count > 0)
			.Concat (tag.ExtendedContentDescription.Descriptors.Select (d => d.Name))
			.Distinct (StringComparer.OrdinalIgnoreCase)
			.ToArray ();
		var library = AsfMetadataLibraryTests.Library ([.. names.Select (n => AsfMetadataLibraryTests.Record (n, "second " + n))]);
		var file = AsfTestBuilder.CreateHeaderObject (
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfMetadataLibraryTests.Object (AsfGuids.ContentDescriptionObject, tag.RenderContentDescription ().ToArray ()),
			AsfMetadataLibraryTests.HeaderExtension (library),
			AsfMetadataLibraryTests.Object (AsfGuids.ExtendedContentDescriptionObject, tag.Render ().ToArray ()));
		return ([.. file, .. AsfTestBuilder.CreateDataObject (0)], names);
	}

	static AsfFile Read (byte[] data)
	{
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	[TestMethod]
	[DynamicData (nameof (PropertyNames))]
	public void UnchangedWriteBack_ChangesNothing (string property)
	{
		var (original, names) = MultiValuedFile ();
		var untouched = Read (original).Render (original);
		var p = typeof (AsfTag).GetProperty (property)!;

		var file = Read (original);
		p.SetValue (file.Tag, p.GetValue (file.Tag));
		var saved = file.Render (original);

		var again = Read (saved).Tag;
		foreach (var name in names) {
			CollectionAssert.AreEqual (
				Read (untouched).Tag.GetCustomField (name).ToArray (), again.GetCustomField (name).ToArray (),
				$"{property} = {property} changed {name}");
		}
		CollectionAssert.AreEqual (untouched, saved, $"{property} = {property} changed the file");
	}

	[TestMethod]
	public void Fixture_HoldsTwoValuesForEveryAttribute ()
	{
		var (original, names) = MultiValuedFile ();
		var tag = Read (original).Tag;

		Assert.IsTrue (names.Length > 40, $"only {names.Length} attributes");
		foreach (var name in names) {
			// A BOOL attribute has no text, so only its library value reads back.
			var values = tag.GetCustomField (name);
			CollectionAssert.Contains (values.ToArray (), "second " + name);
			Assert.AreEqual (name == "WM/IsCompilation" ? 1 : 2, values.Count, name);
		}
	}

	[TestMethod]
	[DataRow ("Performers", "Author")]
	[DataRow ("AlbumArtists", "WM/AlbumArtist")]
	[DataRow ("Composers", "WM/Composer")]
	[DataRow ("Genres", "WM/Genre")]
	public void ArrayProperty_ReadsAndWritesEveryValue (string property, string attribute)
	{
		var (original, _) = MultiValuedFile ();
		var p = typeof (AsfTag).GetProperty (property)!;
		var file = Read (original);

		var values = (string[])p.GetValue (file.Tag)!;
		CollectionAssert.AreEqual (file.Tag.GetCustomField (attribute).ToArray (), values);
		Assert.AreEqual (2, values.Length);

		p.SetValue (file.Tag, Xyz);
		var again = Read (file.Render (original)).Tag;
		CollectionAssert.AreEqual (Xyz, (string[])p.GetValue (again)!);
		CollectionAssert.AreEqual (Xyz, again.GetCustomField (attribute).ToArray ());

		p.SetValue (again, Only);
		CollectionAssert.AreEqual (Only, again.GetCustomField (attribute).ToArray ());
		p.SetValue (again, Array.Empty<string> ());
		Assert.AreEqual (0, again.GetCustomField (attribute).Count);
	}

	[TestMethod]
	public void ChangedScalar_ReplacesEveryValue ()
	{
		var (original, _) = MultiValuedFile ();
		var file = Read (original);

		file.Tag.Artist = "New";
		file.Tag.Genre = "New genre";
		file.Tag.Track = 9;
		var again = Read (file.Render (original)).Tag;

		CollectionAssert.AreEqual (NewArtist, again.Performers);
		CollectionAssert.AreEqual (NewGenre, again.Genres);
		CollectionAssert.AreEqual (Nine, again.GetCustomField ("WM/TrackNumber").ToArray ());
	}
}
