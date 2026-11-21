// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Every child of ilst is a metadata item, whatever its code. An item the reader has no property
/// for (Audible writes CDEK, CDET, AACR, GUID, and others) must survive a save.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4UnknownItemTests
{
	[TestMethod]
	[DataRow ("CDEK")]
	[DataRow ("@xyz")]
	[DataRow ("©zzz")]
	public void UnnamedItem_SurvivesASave (string code)
	{
		var data = WithItem (code, "opaque value");

		var file = Mp4File.Read (data).File!;
		Assert.IsNull (file.Title);
		file.Artist = "Changed";
		var saved = file.Render (data).ToArray ();

		Assert.IsTrue (IndexOf (saved, Encoding.Latin1.GetBytes (code)) >= 0, $"{code} dropped on save");
		Assert.IsTrue (IndexOf (saved, "opaque value"u8.ToArray ()) >= 0);
		Assert.AreEqual ("Changed", Mp4File.Read (saved).File!.Artist);
	}

	[TestMethod]
	public void UnnamedItem_SurvivesConsecutiveSaves ()
	{
		var data = WithItem ("GUID", "kept");

		for (var i = 0; i < 3; i++) {
			var file = Mp4File.Read (data).File!;
			file.Artist = "Pass " + i;
			data = file.Render (data).ToArray ();
		}

		Assert.IsTrue (IndexOf (data, "GUID"u8.ToArray ()) >= 0);
		Assert.IsTrue (IndexOf (data, "kept"u8.ToArray ()) >= 0);
	}

	// A file whose only ilst item is a title, renamed to code.
	static byte[] WithItem (string code, string value)
	{
		var data = TestBuilders.Mp4.CreateWithMetadata (title: value);
		var at = IndexOf (data, Encoding.Latin1.GetBytes ("©nam"));
		Encoding.Latin1.GetBytes (code).CopyTo (data, at);
		return data;
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
