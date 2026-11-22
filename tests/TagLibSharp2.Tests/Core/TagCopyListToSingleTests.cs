// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mp4;
using TagLibSharp2.Riff;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A tag that stores one name gets the name the source shows. An empty first entry (repeated Vorbis
/// fields, https://xiph.org/vorbis/doc/v-comment.html) is not that name, so a list copied into a
/// single-value tag stores its first non-empty entry.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class TagCopyListToSingleTests
{
	static readonly (string Property, string Field)[] Fields = [
		("Artist", "ARTIST"), ("Genre", "GENRE"), ("AlbumArtist", "ALBUMARTIST"), ("Composer", "COMPOSER"),
	];

	static readonly TagCopyOptions[] Options = [
		TagCopyOptions.All, TagCopyOptions.Basic | TagCopyOptions.Extended, TagCopyOptions.Extended, TagCopyOptions.Basic,
	];

	static Tag NewTarget (string name) => name switch {
		"mp4" => new Mp4Tag (),
		"asf" => new AsfTag (),
		"riff" => new RiffInfoTag (),
		"id3v1" => new Id3v1Tag (),
		_ => new CombinedTag (new Id3v2Tag (Id3v2Version.V24), new Id3v1Tag ()),
	};

	[TestMethod]
	[DataRow ("mp4")]
	[DataRow ("asf")]
	[DataRow ("riff")]
	[DataRow ("id3v1")]
	[DataRow ("id3v2+id3v1")]
	public void CopyFromALeadingEmptyList_StoresTheNameTheSourceShows (string targetName)
	{
		var failures = new List<string> ();
		var covered = 0;
		foreach (var (propertyName, field) in Fields) {
			var property = typeof (Tag).GetProperty (propertyName)!;
			var probe = NewTarget (targetName);
			property.SetValue (probe, "Probe");
			if (!Equals (property.GetValue (probe), "Probe"))
				continue;

			foreach (var options in Options) {
				if (propertyName is "AlbumArtist" or "Composer" && (options & TagCopyOptions.Extended) == 0)
					continue;
				var source = new VorbisComment ("source");
				source.AddField (field, "");
				source.AddField (field, "Rock");
				var target = NewTarget (targetName);
				property.SetValue (target, "Old");

				source.CopyTo (target, options);
				covered++;

				if (!Equals (property.GetValue (target), "Rock"))
					failures.Add ($"{propertyName} {options}: {property.GetValue (target) ?? "<null>"}");
				if (target is CombinedTag combined && !Equals (property.GetValue (((IReadOnlyList<Tag?>)typeof (CombinedTag).GetProperty ("Tags", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue (combined)!)[1]), property.Name is "Artist" or "Genre" ? "Rock" : null))
					failures.Add ($"{propertyName} {options}: ID3v1 half");
			}
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
		Assert.IsGreaterThanOrEqualTo (4, covered);
	}

	[TestMethod]
	public void ListSetter_OnASingleValueTag_StoresTheFirstNonEmptyEntry ()
	{
		foreach (var tag in new Tag[] { new Mp4Tag (), new AsfTag (), new RiffInfoTag (), new Id3v1Tag () }) {
			tag.Performers = ["", "P2", "P3"];
			tag.Genres = ["", "Pop"];
			Assert.AreEqual ("P2", tag.Artist, tag.GetType ().Name);
			Assert.AreEqual ("Pop", tag.Genre, tag.GetType ().Name);

			tag.Performers = ["", ""];
			Assert.IsTrue (string.IsNullOrEmpty (tag.Artist), tag.GetType ().Name);
		}
	}
}
