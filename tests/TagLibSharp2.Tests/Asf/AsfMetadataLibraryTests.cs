// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// Values 2..n of a multi-valued attribute live in the Metadata Library Object inside the Header Extension
/// Object (ASF spec rev. 01.20.03 §3.4 and §4.8; the layout Windows writes, as recorded at
/// https://mutagen-specs.readthedocs.io/en/latest/asf/index.html "Multiple Values"). Every custom field read
/// sees them, and every write or remove leaves the file holding exactly the values asked for.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfMetadataLibraryTests
{
	// ASF_Reserved_1, the fixed Reserved Field 1 of the Header Extension Object (§3.4).
	static readonly byte[] Reserved1 = Guid.Parse ("ABD3D211-A9BA-11CF-8EE6-00C00C205365").ToByteArray ();

	/// <summary>One Metadata Library or Metadata record: first WORD, stream, name length, type, data length.</summary>
	static byte[] Record (string name, string value, ushort stream = 0, ushort first = 0) =>
		RawRecord (name, 0, Encoding.Unicode.GetBytes (value + "\0"), stream, first);

	static byte[] RawRecord (string name, ushort type, byte[] data, ushort stream = 0, ushort first = 0)
	{
		var nameBytes = Encoding.Unicode.GetBytes (name + "\0");
		var record = new byte[12 + nameBytes.Length + data.Length];
		BinaryPrimitives.WriteUInt16LittleEndian (record, first);
		BinaryPrimitives.WriteUInt16LittleEndian (record.AsSpan (2), stream);
		BinaryPrimitives.WriteUInt16LittleEndian (record.AsSpan (4), (ushort)nameBytes.Length);
		BinaryPrimitives.WriteUInt16LittleEndian (record.AsSpan (6), type);
		BinaryPrimitives.WriteUInt32LittleEndian (record.AsSpan (8), (uint)data.Length);
		nameBytes.CopyTo (record, 12);
		data.CopyTo (record, 12 + nameBytes.Length);
		return record;
	}

	static byte[] Object (AsfGuid guid, byte[] content)
	{
		var result = new byte[24 + content.Length];
		guid.Render ().Span.CopyTo (result);
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (16), (ulong)result.Length);
		content.CopyTo (result, 24);
		return result;
	}

	static byte[] RecordsObject (AsfGuid guid, params byte[][] records)
	{
		var body = new List<byte> ();
		body.AddRange (BitConverter.GetBytes ((ushort)records.Length));
		foreach (var r in records)
			body.AddRange (r);
		return Object (guid, [.. body]);
	}

	static byte[] Library (params byte[][] records) => RecordsObject (AsfGuids.MetadataLibraryObject, records);

	static byte[] Metadata (params byte[][] records) => RecordsObject (AsfGuids.MetadataObject, records);

	static byte[] HeaderExtension (params byte[][] children)
	{
		var data = children.SelectMany (c => c).ToArray ();
		var content = new byte[16 + 2 + 4 + data.Length];
		Reserved1.CopyTo (content, 0);
		BinaryPrimitives.WriteUInt16LittleEndian (content.AsSpan (16), 6);
		BinaryPrimitives.WriteUInt32LittleEndian (content.AsSpan (18), (uint)data.Length);
		data.CopyTo (content, 22);
		return Object (AsfGuids.HeaderExtensionObject, content);
	}

	static byte[] Wma (byte[]? headerExtension, params AsfDescriptor[] descriptors)
	{
		var children = new List<byte[]> {
			AsfTestBuilder.CreateFilePropertiesObject (1000, 128000),
			AsfTestBuilder.CreateAudioStreamPropertiesObject (),
			AsfTestBuilder.CreateContentDescriptionObject ("Song", "First author"),
		};
		if (headerExtension is not null)
			children.Add (headerExtension);
		if (descriptors.Length > 0)
			children.Add (AsfTestBuilder.CreateExtendedContentDescriptionObject (descriptors));
		return [.. AsfTestBuilder.CreateHeaderObject ([.. children]), .. AsfTestBuilder.CreateDataObject (0)];
	}

	static byte[] MultiValued () => Wma (
		HeaderExtension (Metadata (), Library (Record ("MyField", "second"), Record ("MyField", "third"))),
		AsfDescriptor.CreateString ("MyField", "first"));

	static AsfFile Read (byte[] data)
	{
		var result = AsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	static bool Holds (byte[] file, string text) =>
		file.AsSpan ().IndexOf (Encoding.Unicode.GetBytes (text)) >= 0;

	static void AssertValues (IReadOnlyList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	[TestMethod]
	public void GetCustomField_ReadsTheValuesInTheMetadataLibrary ()
	{
		var file = Read (MultiValued ());

		AssertValues (file.Tag.GetCustomField ("MyField"), "first", "second", "third");
		AssertValues (file.Tag.GetCustomField ("myfield"), "first", "second", "third");
	}

	[TestMethod]
	public void SetCustomField_ReplacesTheLibraryValues ()
	{
		var original = MultiValued ();
		var file = Read (original);

		Assert.IsTrue (file.Tag.SetCustomField ("MyField", "x"));
		var saved = file.Render (original);

		AssertValues (Read (saved).Tag.GetCustomField ("MyField"), "x");
		Assert.IsFalse (Holds (saved, "second"));
		Assert.IsFalse (Holds (saved, "third"));
	}

	[TestMethod]
	public void RemoveCustomField_RemovesTheLibraryValues ()
	{
		var original = MultiValued ();
		var file = Read (original);

		Assert.IsTrue (file.Tag.RemoveCustomField ("MyField"));
		Assert.AreEqual (0, file.Tag.GetCustomField ("MyField").Count);
		var saved = file.Render (original);

		Assert.AreEqual (0, Read (saved).Tag.GetCustomField ("MyField").Count);
		Assert.IsFalse (Holds (saved, "MyField"));
	}

	[TestMethod]
	public void RemoveCustomField_ValueOnlyInTheLibrary_IsFoundAndRemoved ()
	{
		var original = Wma (HeaderExtension (Library (Record ("OnlyHere", "v"))));
		var file = Read (original);

		AssertValues (file.Tag.GetCustomField ("OnlyHere"), "v");
		Assert.IsTrue (file.Tag.RemoveCustomField ("OnlyHere"));
		Assert.IsFalse (Holds (file.Render (original), "OnlyHere"));
	}

	[TestMethod]
	public void SetCustomField_SeveralValues_FirstInExtendedContentRestInLibrary ()
	{
		var original = Wma (HeaderExtension (Metadata ()));
		var file = Read (original);

		Assert.IsTrue (file.Tag.SetCustomField ("MyField", "a", "b", "c"));
		var again = Read (file.Render (original));

		AssertValues (again.Tag.GetCustomField ("MyField"), "a", "b", "c");
		Assert.AreEqual (1, again.Tag.ExtendedContentDescription.Descriptors.Count (d => d.Name == "MyField"));
	}

	[TestMethod]
	public void SetCustomField_SeveralValues_FileWithoutHeaderExtension_GetsOne ()
	{
		var original = Wma (null);
		var file = Read (original);

		Assert.IsTrue (file.Tag.SetCustomField ("MyField", "a", "b"));
		var saved = file.Render (original);
		var again = Read (saved);

		AssertValues (again.Tag.GetCustomField ("MyField"), "a", "b");
		Assert.AreEqual (1, again.Tag.ExtendedContentDescription.Descriptors.Count (d => d.Name == "MyField"));
		Assert.IsTrue (saved.AsSpan ().IndexOf (Reserved1) >= 0, "a Header Extension Object was written");
	}

	[TestMethod]
	public void ContentDescriptionName_FirstValueInTheFixedFieldRestInLibrary ()
	{
		var original = Wma (HeaderExtension (Library (Record ("Author", "Second author"))));
		var file = Read (original);
		AssertValues (file.Tag.GetCustomField ("Author"), "First author", "Second author");

		Assert.IsTrue (file.Tag.SetCustomField ("Author", "A", "B"));
		var again = Read (file.Render (original));

		Assert.AreEqual ("A", again.Tag.Artist);
		AssertValues (again.Tag.GetCustomField ("Author"), "A", "B");
		Assert.IsTrue (again.Tag.RemoveCustomField ("Author"));
		Assert.AreEqual (0, again.Tag.GetCustomField ("Author").Count);
	}

	[TestMethod]
	public void MappedSetter_ReplacesTheLibraryValuesOfItsAttribute ()
	{
		var original = Wma (
			HeaderExtension (Library (Record ("WM/AlbumTitle", "Old 2"))),
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Old 1"));
		var file = Read (original);

		file.Tag.Album = "New";
		var saved = file.Render (original);

		AssertValues (Read (saved).Tag.GetCustomField ("WM/AlbumTitle"), "New");
		Assert.IsFalse (Holds (saved, "Old 2"));
	}

	[TestMethod]
	public void OtherRecords_AreKept ()
	{
		byte[] other = Record ("Other", "kept");
		byte[] streamOne = Record ("MyField", "stream one", stream: 1);
		byte[] language = Record ("MyField", "other language", first: 1);
		byte[] metadataRecord = Record ("MyField", "metadata stream", stream: 1);
		var original = Wma (
			HeaderExtension (Metadata (metadataRecord), Library (other, streamOne, Record ("MyField", "second"), language)),
			AsfDescriptor.CreateString ("MyField", "first"));
		var file = Read (original);
		AssertValues (file.Tag.GetCustomField ("MyField"), "first", "second", "other language");

		file.Tag.SetCustomField ("MyField", "x");
		var saved = file.Render (original);

		Assert.IsTrue (saved.AsSpan ().IndexOf (other) >= 0);
		Assert.IsTrue (saved.AsSpan ().IndexOf (streamOne) >= 0, "a stream's record is not the file's");
		Assert.IsTrue (saved.AsSpan ().IndexOf (metadataRecord) >= 0, "a stream's record is not the file's");
		Assert.IsFalse (Holds (saved, "other language"), "every language of the file-level name is replaced");
		AssertValues (Read (saved).Tag.GetCustomField ("MyField"), "x");
	}

	[TestMethod]
	public void NumericLibraryRecords_ReadAsDecimal ()
	{
		var dword = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian (dword, 7);
		var original = Wma (HeaderExtension (Library (
			RawRecord ("N", 3, dword),
			RawRecord ("N", 5, [9, 0]),
			RawRecord ("N", 4, [1, 0, 0, 0, 0, 0, 0, 0]),
			RawRecord ("N", 2, [1, 0]),
			RawRecord ("N", 1, [1, 2, 3]))));

		AssertValues (Read (original).Tag.GetCustomField ("N"), "7", "9", "1");
	}

	[TestMethod]
	public void UnchangedSave_KeepsTheHeaderExtensionBytes ()
	{
		var original = MultiValued ();
		var extension = HeaderExtension (Metadata (), Library (Record ("MyField", "second"), Record ("MyField", "third")));
		var file = Read (original);

		file.Tag.Title = "Changed title";
		var saved = file.Render (original);

		Assert.IsTrue (saved.AsSpan ().IndexOf (extension) >= 0);
	}

	[TestMethod]
	public void MalformedLibrary_IsKeptAndIgnored ()
	{
		// Count says two records, the object holds one and a half.
		var library = Library (Record ("MyField", "second"), Record ("Cut", "off"));
		BinaryPrimitives.WriteUInt64LittleEndian (library.AsSpan (16), (ulong)(library.Length - 6));
		var truncated = library.AsSpan (0, library.Length - 6).ToArray ();
		var original = Wma (HeaderExtension (truncated), AsfDescriptor.CreateString ("MyField", "first"));
		var file = Read (original);

		AssertValues (file.Tag.GetCustomField ("MyField"), "first");
		Assert.IsFalse (file.Tag.SetCustomField ("MyField", "x"), "values the tag cannot see would survive");
		Assert.IsFalse (file.Tag.RemoveCustomField ("MyField"));
		Assert.IsTrue (file.Render (original).AsSpan ().IndexOf (truncated) >= 0);
	}

	[TestMethod]
	public void DuplicateOrShortLibrary_MakesTheExtensionUnreadable ()
	{
		var twice = Wma (HeaderExtension (Library (Record ("A", "1")), Library (Record ("A", "2"))));
		var empty = Wma (HeaderExtension (Object (AsfGuids.MetadataLibraryObject, [])));
		var shortRecord = Wma (HeaderExtension (Object (AsfGuids.MetadataObject, [1, 0, 0, 0, 0])));

		foreach (var original in new[] { twice, empty, shortRecord }) {
			var file = Read (original);
			Assert.IsFalse (file.Tag.SetCustomField ("A", "x"));
			Assert.IsFalse (file.Tag.RemoveCustomField ("A"));
			file.Tag.Title = "Still writable";
			var saved = file.Render (original);
			Assert.AreEqual ("Still writable", Read (saved).Tag.Title);
		}
	}

	[TestMethod]
	public void HeaderExtensionChildThatDoesNotFit_MakesTheExtensionUnreadable ()
	{
		// A Metadata Library Object could hide past a child whose size runs off the end.
		var library = Library (Record ("MyField", "second"));
		var oversized = (byte[])library.Clone ();
		BinaryPrimitives.WriteUInt64LittleEndian (oversized.AsSpan (16), 100_000);
		var original = Wma (HeaderExtension (oversized, library), AsfDescriptor.CreateString ("MyField", "first"));
		var file = Read (original);

		AssertValues (file.Tag.GetCustomField ("MyField"), "first");
		Assert.IsFalse (file.Tag.SetCustomField ("MyField", "x", "y"));
		Assert.IsTrue (file.Render (original).AsSpan ().IndexOf (oversized) >= 0, "the extension is kept as it was");
	}

	[TestMethod]
	public void Clear_RemovesFileLevelRecordsAndKeepsStreamRecords ()
	{
		byte[] streamOne = Record ("IsVBR", "1", stream: 1);
		var original = Wma (HeaderExtension (Library (Record ("MyField", "second"), streamOne)),
			AsfDescriptor.CreateString ("MyField", "first"));
		var file = Read (original);

		file.Tag.Clear ();
		var saved = file.Render (original);

		Assert.AreEqual (0, Read (saved).Tag.GetCustomField ("MyField").Count);
		Assert.IsTrue (saved.AsSpan ().IndexOf (streamOne) >= 0);
	}
}
