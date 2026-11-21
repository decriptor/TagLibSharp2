// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

public partial class MatroskaFileTests
{
	static readonly byte[] Cluster = Master (0x1F43B675, new byte[] { 0x55, 0x66 });

	static MatroskaFile Reopen (MatroskaFile file, byte[] original)
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/f.mkv", original);
		var saved = file.SaveToFile ("/f.mkv", original, fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/f.mkv");
		Assert.AreEqual (1, CountOf (bytes, Cluster), "the cluster is copied once");
		var again = MatroskaFile.Read (bytes);
		Assert.IsTrue (again.IsSuccess, again.Error);
		return again.File!;
	}

	static void AssertCustom (IReadOnlyList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	[TestMethod]
	public void CustomField_WritesAtTheItemLevel ()
	{
		var original = FileWith (
			Tags (Tag (50, Simple ("TITLE", "Album")), Tag (30, Simple ("TITLE", "Track"))),
			Cluster);
		var file = MatroskaFile.Read (original).File!;

		Assert.IsTrue (file.Tag.SetCustomField ("MY_FIELD", "a", "b"));
		var again = Reopen (file, original);

		AssertCustom (again.Tag.GetCustomField ("MY_FIELD"), "a", "b");
		Assert.AreEqual ("Album", again.Tag.Album);
		Assert.AreEqual ("Track", again.Tag.Title);
		Assert.AreEqual (0, again.Tag.GetCustomField ("my_field").Count);
	}

	[TestMethod]
	public void CustomField_BroaderLevelShowsThroughUntilSetAndRemoveClearsBoth ()
	{
		var original = FileWith (
			Tags (Tag (50, Simple ("LABEL", "Album label")), Tag (30, Simple ("TITLE", "Track"))),
			Cluster);
		var file = MatroskaFile.Read (original).File!;

		AssertCustom (file.Tag.GetCustomField ("LABEL"), "Album label");

		file.Tag.SetCustomField ("LABEL", "Track label");
		var again = Reopen (file, original);
		AssertCustom (again.Tag.GetCustomField ("LABEL"), "Track label");

		Assert.IsTrue (again.Tag.RemoveCustomField ("LABEL"));
		Assert.AreEqual (0, again.Tag.GetCustomField ("LABEL").Count);
		var removed = Reopen (again, original);
		Assert.AreEqual (0, removed.Tag.GetCustomField ("LABEL").Count);
	}

	[TestMethod]
	public void CustomField_ChapterTag_IsNotTheFile ()
	{
		var original = FileWith (
			Tags (
				Tag (50, Simple ("TITLE", "Movie")),
				ChapterTag (30, Chapter (7), Simple ("LABEL", "Chapter label"))),
			Cluster);
		var file = MatroskaFile.Read (original).File!;

		Assert.AreEqual (0, file.Tag.GetCustomField ("LABEL").Count);
		Assert.IsFalse (file.Tag.RemoveCustomField ("LABEL"));

		file.Tag.SetCustomField ("LABEL", "File label");
		var again = Reopen (file, original);
		AssertCustom (again.Tag.GetCustomField ("LABEL"), "File label");
		var bytes = again.Tag.Render ().ToArray ();
		Assert.IsTrue (bytes.AsSpan ().IndexOf ("Chapter label"u8) >= 0);
	}

	[TestMethod]
	public void CustomField_FileWithoutTags_GetsATagsElement ()
	{
		var original = FileWith (Cluster);
		var file = MatroskaFile.Read (original).File!;

		Assert.IsTrue (file.Tag.SetCustomField ("MY_FIELD", "x"));
		var again = Reopen (file, original);

		AssertCustom (again.Tag.GetCustomField ("MY_FIELD"), "x");
	}
}
