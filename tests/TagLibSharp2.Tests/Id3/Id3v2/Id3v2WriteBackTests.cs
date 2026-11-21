// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// An editor that writes back the value a property returned must not change the tag.
/// A single-value getter returns the first value of an ID3v2.4 multi-value text frame
/// (NUL-separated, ID3v2.4 §4.2), so a write-back must not collapse the list.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class Id3v2WriteBackTests
{
	static readonly string[] ListFields = ["Artist", "AlbumArtist", "Composer", "Genre"];

	/// <summary>
	/// Every settable string property on <see cref="Id3v2Tag"/>.
	/// </summary>
	static IEnumerable<PropertyInfo> StringProperties () =>
		typeof (Id3v2Tag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.PropertyType == typeof (string) && p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0);

	static Id3v2Tag Reparse (Id3v2Tag tag)
	{
		var result = Id3v2Tag.Read (tag.Render ().Span);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}

	static List<string> Snapshot (Id3v2Tag tag)
	{
		var rows = new List<string> ();
		foreach (var f in tag.Frames)
			rows.Add (f.Id + "=" + f.Text);
		foreach (var f in tag.UserTextFrames)
			rows.Add ("TXXX:" + f.Description + "=" + f.Value);
		return rows;
	}

	/// <summary>
	/// Builds a tag in which every text and TXXX frame the property writes holds <paramref name="raw"/>.
	/// Returns null when the property does not store "V1" in a text or TXXX frame.
	/// </summary>
	static Id3v2Tag? BuildMultiValue (PropertyInfo property, Id3v2Version version, string raw)
	{
		var tag = new Id3v2Tag (version);
		property.SetValue (tag, "V1");
		var touched = tag.Frames.Count + tag.UserTextFrames.Count;
		if (touched == 0 || !Equals (property.GetValue (tag), "V1"))
			return null;

		foreach (var f in tag.Frames)
			f.Text = raw;
		foreach (var f in tag.UserTextFrames)
			f.Value = raw;
		return Reparse (tag);
	}

	[TestMethod]
	[DataRow (Id3v2Version.V23, "V1\0V2")]
	[DataRow (Id3v2Version.V24, "V1\0V2")]
	[DataRow (Id3v2Version.V23, "\0V2")]
	[DataRow (Id3v2Version.V24, "\0V2")]
	public void EveryStringProperty_UnchangedWriteBack_KeepsEveryValue (Id3v2Version version, string raw)
	{
		var covered = new List<string> ();
		var failures = new List<string> ();
		foreach (var property in StringProperties ()) {
			var tag = BuildMultiValue (property, version, raw);
			if (tag is null)
				continue;
			covered.Add (property.Name);

			var before = Snapshot (tag);
			property.SetValue (tag, property.GetValue (tag));
			var after = Snapshot (Reparse (tag));

			if (!before.SequenceEqual (after))
				failures.Add ($"{property.Name}: [{string.Join (" | ", before)}] -> [{string.Join (" | ", after)}]");
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures).Replace ("\0", "\\0", StringComparison.Ordinal));
		foreach (var name in ListFields)
			Assert.Contains (name, covered);
		Assert.IsGreaterThanOrEqualTo (45, covered.Count, string.Join (",", covered));
	}

	[TestMethod]
	[DataRow (Id3v2Version.V23)]
	[DataRow (Id3v2Version.V24)]
	public void ListFields_UnchangedWriteBack_KeepBothNames (Id3v2Version version)
	{
		var tag = new Id3v2Tag (version);
		tag.Performers = ["P1", "P2"];
		tag.AlbumArtists = ["A1", "A2"];
		tag.Composers = ["C1", "C2"];
		tag.Genres = ["Rock", "Pop"];
		tag = Reparse (tag);

		tag.Artist = tag.Artist;
		tag.AlbumArtist = tag.AlbumArtist;
		tag.Composer = tag.Composer;
		tag.Genre = tag.Genre;
		var reparsed = Reparse (tag);

		Assert.AreEqual ("P1|P2", string.Join ("|", reparsed.Performers));
		Assert.AreEqual ("A1|A2", string.Join ("|", reparsed.AlbumArtists));
		Assert.AreEqual ("C1|C2", string.Join ("|", reparsed.Composers));
		Assert.AreEqual ("Rock|Pop", string.Join ("|", reparsed.Genres));
	}

	[TestMethod]
	public void Genre_WriteBackOfDisplayName_KeepsNumericGenreList ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.SetTextFrameValues ("TCON", ["17", "Pop"]);
		tag = Reparse (tag);
		Assert.AreEqual ("Rock", tag.Genre);

		tag.Genre = tag.Genre;

		var reparsed = Reparse (tag);
		Assert.AreEqual ("17|Pop", string.Join ("|", reparsed.GetTextFrameValues ("TCON").ToArray ()));
	}

	[TestMethod]
	[DataRow (Id3v2Version.V23)]
	[DataRow (Id3v2Version.V24)]
	public void ListFields_NewValue_StillReplacesTheList (Id3v2Version version)
	{
		var tag = new Id3v2Tag (version);
		tag.Performers = ["P1", "P2"];
		tag.Genres = ["Rock", "Pop"];

		tag.Artist = "P3";
		tag.Genre = "Jazz";

		var reparsed = Reparse (tag);
		Assert.AreEqual ("P3", string.Join ("|", reparsed.Performers));
		Assert.AreEqual ("Jazz", string.Join ("|", reparsed.Genres));
	}

	[TestMethod]
	public void ListFields_Clear_StillRemovesTheFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Performers = ["P1", "P2"];
		tag.Composers = ["C1", "C2"];

		tag.Artist = null;
		tag.Composer = "";

		Assert.IsNull (tag.GetTextFrame ("TPE1"));
		Assert.IsNull (tag.GetTextFrame ("TCOM"));
	}

	[TestMethod]
	[DataRow (Id3v2Version.V23)]
	[DataRow (Id3v2Version.V24)]
	public void MovementIndex_UnchangedWriteBack_DoesNotAddTxxxFrames (Id3v2Version version)
	{
		var result = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame ("MVIN", "2/4", (byte)version));
		Assert.IsTrue (result.IsSuccess, result.Error);
		var tag = result.Tag!;
		var before = Snapshot (tag);

		tag.MovementNumber = tag.MovementNumber;
		tag.MovementTotal = tag.MovementTotal;

		CollectionAssert.AreEqual (before, Snapshot (tag));
	}

	[TestMethod]
	public void Movement_WriteBackOfTxxxOnlyName_DoesNotAddAppleFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.SetUserText ("MOVEMENT", "Allegro");

		tag.Movement = tag.Movement;

		Assert.IsNull (tag.GetTextFrame ("MVNM"));
		Assert.AreEqual ("Allegro", tag.GetUserText ("MOVEMENT"));
	}

	[TestMethod]
	[DataRow ("\0Pop")]
	[DataRow ("")]
	public void Genre_EmptyFirstValue_ReadsEmptyAndWriteBackKeepsFrame (string raw)
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Genre = "Rock";
		tag.Frames.First (f => f.Id == "TCON").Text = raw;

		Assert.AreEqual ("", tag.Genre);
		tag.Genre = tag.Genre;
		Assert.AreEqual (raw, tag.Frames.First (f => f.Id == "TCON").Text);

		tag.Genre = null;
		Assert.IsNull (tag.GetTextFrame ("TCON"));
	}

	[TestMethod]
	public void Movement_EmptyAppleFrame_WriteBackKeepsTxxxName ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Movement = "Allegro";
		tag.Frames.First (f => f.Id == "MVNM").Text = "";
		tag = Reparse (tag);
		Assert.AreEqual ("Allegro", tag.Movement);

		tag.Movement = tag.Movement;

		var reparsed = Reparse (tag);
		Assert.AreEqual ("Allegro", reparsed.GetUserText ("MOVEMENT"));
		Assert.AreEqual ("Allegro", reparsed.Movement);
	}

	[TestMethod]
	[DataRow (Id3v2Version.V24, "TDOR", "", "TORY", "1999", "1999")]
	[DataRow (Id3v2Version.V24, "TDOR", "abc", "TORY", "1999", "1999")]
	[DataRow (Id3v2Version.V23, "TORY", "", "TDOR", "1999-05-01", "1999-05-01")]
	[DataRow (Id3v2Version.V24, "TDOR", "1999-05-01", "TORY", "1999", "1999-05-01")]
	public void OriginalReleaseDate_UnchangedWriteBack_KeepsBothFrames (
		Id3v2Version version, string primary, string primaryText, string other, string otherText, string expected)
	{
		var tag = new Id3v2Tag (version);
		tag.SetTextFrameValues (primary, ["placeholder"]);
		tag.SetTextFrameValues (other, [otherText]);
		tag.Frames.First (f => f.Id == primary).Text = primaryText;
		tag = Reparse (tag);
		Assert.AreEqual (expected, tag.OriginalReleaseDate);
		var before = Snapshot (tag);

		tag.OriginalReleaseDate = tag.OriginalReleaseDate;

		CollectionAssert.AreEqual (before, Snapshot (Reparse (tag)));
	}
}
