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
	public void EveryStringProperty_UnchangedWriteBack_KeepsEveryValue_AndAnEditReplaces (Id3v2Version version, string raw)
	{
		var covered = new List<string> ();
		var failures = new List<string> ();
		foreach (var property in StringProperties ()) {
			var tag = BuildMultiValue (property, version, raw);
			if (tag is null)
				continue;
			covered.Add (property.Name);

			var before = Snapshot (tag);
			var read = (string?)property.GetValue (tag);
			property.SetValue (tag, read);
			var after = Snapshot (Reparse (tag));

			if (!before.SequenceEqual (after))
				failures.Add ($"{property.Name}: [{string.Join (" | ", before)}] -> [{string.Join (" | ", after)}]");

			// A case-only edit, or "" over a list whose later values hold text, is not a write-back.
			var current = (string?)property.GetValue (tag);
			var edit = string.IsNullOrEmpty (current) ? "" : current!.ToLowerInvariant ();
			property.SetValue (tag, edit);
			var edited = Snapshot (Reparse (tag));
			if (edited.Any (r => r.Contains ("V2", StringComparison.Ordinal)) || (edit.Length != 0 && !Equals (property.GetValue (tag), edit)))
				failures.Add ($"{property.Name} edit {edit}: [{string.Join (" | ", edited)}]");
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures).Replace ("\0", "\\0", StringComparison.Ordinal));
		foreach (var name in ListFields)
			Assert.Contains (name, covered);
		Assert.IsGreaterThanOrEqualTo (45, covered.Count, string.Join (",", covered));
	}

	/// <summary>
	/// Values a property may need before its getter reads them back: a name, a year, a date, a URL, an identifier.
	/// </summary>
	static readonly string[] Candidates = ["V1", "2001", "2001-05-03", "http://example.com/feed", "b10b5bd8-0000-4000-8000-000000000001"];

	/// <summary>
	/// Content in a layout this library's setters never write: UTF-16 with a BOM on every value,
	/// a second value, a non-default language, a second COMM frame.
	/// </summary>
	static byte[] Utf16 (string text) => [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes (text)];

	static byte[] Join (params byte[][] parts) => parts.SelectMany (p => p).ToArray ();

	static readonly byte[] Nul16 = [0, 0];

	/// <summary>
	/// Rebuilds every frame the setter wrote for <paramref name="probe"/> in a foreign layout, holding
	/// <paramref name="value"/>, and parses the result.
	/// </summary>
	static Id3v2Tag BuildForeign (Id3v2Tag probe, byte version, string value)
	{
		var frames = new List<byte[]> ();
		foreach (var f in probe.Frames)
			frames.Add (TestBuilders.Id3v2.CreateFrame (f.Id, Join ([0x01], Utf16 (value), Nul16, Utf16 ("V2")), version));
		foreach (var f in probe.UserTextFrames)
			frames.Add (TestBuilders.Id3v2.CreateFrame ("TXXX", Join ([0x01], Utf16 (f.Description), Nul16, Utf16 (value), Nul16, Utf16 ("V2")), version));
		if (probe.Comments.Count > 0) {
			// An empty comment first: the getter shows the first non-empty one, as ffprobe and TagLib# do.
			frames.Add (TestBuilders.Id3v2.CreateFrame ("COMM", Join ([0x00], "eng"u8.ToArray (), [0x00]), version));
			frames.Add (TestBuilders.Id3v2.CreateFrame ("COMM", Join ([0x00], "deu"u8.ToArray (), [0x00], System.Text.Encoding.Latin1.GetBytes (value)), version));
			frames.Add (TestBuilders.Id3v2.CreateFrame ("COMM", Join ([0x00], "eng"u8.ToArray (), "note"u8.ToArray (), [0x00], "other"u8.ToArray ()), version));
		}
		foreach (var _ in probe.LyricsFrames) {
			frames.Add (TestBuilders.Id3v2.CreateFrame ("USLT", Join ([0x00], "eng"u8.ToArray (), [0x00]), version));
			frames.Add (TestBuilders.Id3v2.CreateFrame ("USLT", Join ([0x01], "deu"u8.ToArray (), Utf16 (""), Nul16, Utf16 (value)), version));
		}
		foreach (var f in probe.UrlFrames)
			frames.Add (TestBuilders.Id3v2.CreateFrame (f.Id, System.Text.Encoding.Latin1.GetBytes (value), version));
		foreach (var f in probe.UniqueFileIdFrames)
			frames.Add (TestBuilders.Id3v2.CreateFrame ("UFID", Join (System.Text.Encoding.Latin1.GetBytes (f.Owner), [0x00], System.Text.Encoding.ASCII.GetBytes (value)), version));
		// A frame after the property's frames, so a setter that rewrites them moves them.
		frames.Add (TestBuilders.Id3v2.CreateFrame (probe.Frames.Any (f => f.Id == "TALB") ? "TIT2" : "TALB", Join ([0x01], Utf16 ("after")), version));

		var body = Join ([.. frames]);
		var result = Id3v2Tag.Read (Join (TestBuilders.Id3v2.CreateHeader (version, (uint)body.Length), body));
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version3)]
	[DataRow (TestConstants.Id3v2.Version4)]
	public void EveryStringProperty_WriteBackOverForeignFrames_IsByteIdentical (byte version)
	{
		var failures = new List<string> ();
		var uncovered = new List<string> ();
		foreach (var property in StringProperties ()) {
			Id3v2Tag? tag = null;
			foreach (var candidate in Candidates) {
				var probe = new Id3v2Tag ((Id3v2Version)version);
				property.SetValue (probe, candidate);
				if (!Equals (property.GetValue (probe), candidate))
					continue;
				var foreign = BuildForeign (probe, version, candidate);
				if (Equals (property.GetValue (foreign), candidate)) {
					tag = foreign;
					break;
				}
			}
			if (tag is null) {
				uncovered.Add (property.Name);
				continue;
			}

			var before = tag.Render ().ToArray ();
			property.SetValue (tag, property.GetValue (tag));
			if (!before.AsSpan ().SequenceEqual (tag.Render ().Span))
				failures.Add (property.Name);
		}

		Assert.IsEmpty (failures, string.Join (",", failures));
		Assert.IsEmpty (uncovered, string.Join (",", uncovered));
	}

	[TestMethod]
	[DataRow (TestConstants.Id3v2.Version3)]
	[DataRow (TestConstants.Id3v2.Version4)]
	public void CommentAndLyrics_EmptyFirstFrame_ReadTheNextAndEmptyStringClears (byte version)
	{
		var body = Join (
			TestBuilders.Id3v2.CreateFrame ("COMM", Join ([0x00], "eng"u8.ToArray (), [0x00]), version),
			TestBuilders.Id3v2.CreateFrame ("COMM", Join ([0x00], "deu"u8.ToArray (), [0x00], "Hallo"u8.ToArray ()), version),
			TestBuilders.Id3v2.CreateFrame ("USLT", Join ([0x00], "eng"u8.ToArray (), [0x00]), version),
			TestBuilders.Id3v2.CreateFrame ("USLT", Join ([0x00], "deu"u8.ToArray (), [0x00], "Lied"u8.ToArray ()), version));
		var tag = Id3v2Tag.Read (Join (TestBuilders.Id3v2.CreateHeader (version, (uint)body.Length), body)).Tag!;
		Assert.AreEqual ("Hallo", tag.Comment);
		Assert.AreEqual ("Lied", tag.Lyrics);

		var before = tag.Render ().ToArray ();
		tag.Comment = tag.Comment;
		tag.Lyrics = tag.Lyrics;
		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());

		tag.Comment = "";
		tag.Lyrics = "";
		Assert.IsEmpty (tag.Comments);
		Assert.IsEmpty (tag.LyricsFrames);
		Assert.IsNull (tag.Comment);
		Assert.IsNull (tag.Lyrics);
	}

	[TestMethod]
	public void CommentAndLyrics_EveryFrameEmpty_ReadEmpty ()
	{
		var body = Join (
			TestBuilders.Id3v2.CreateFrame ("COMM", Join ([0x00], "eng"u8.ToArray (), [0x00]), 4),
			TestBuilders.Id3v2.CreateFrame ("USLT", Join ([0x00], "eng"u8.ToArray (), [0x00]), 4));
		var tag = Id3v2Tag.Read (Join (TestBuilders.Id3v2.CreateHeader (4, (uint)body.Length), body)).Tag!;

		Assert.AreEqual ("", tag.Comment);
		Assert.AreEqual ("", tag.Lyrics);
	}

	[TestMethod]
	public void Year_V23WriteBack_KeepsTheOriginalEncoding ()
	{
		var result = Id3v2Tag.Read (TestBuilders.Id3v2.CreateTagWithTextFrame ("TYER", "2001", TestConstants.Id3v2.Version3));
		Assert.IsTrue (result.IsSuccess, result.Error);
		var tag = result.Tag!;
		var before = tag.Render ().ToArray ();

		tag.Year = tag.Year;

		CollectionAssert.AreEqual (before, tag.Render ().ToArray ());
	}

	[TestMethod]
	[DataRow (Id3v2Version.V23)]
	[DataRow (Id3v2Version.V24)]
	public void LeadingEmptyValue_ReadsTheNextValue_WriteBackKeepsIt_EmptyStringClears (Id3v2Version version)
	{
		var tag = new Id3v2Tag (version);
		tag.Artist = "x";
		tag.Genre = "x";
		tag.Movement = "x";
		foreach (var f in tag.Frames)
			f.Text = "\0V2";
		foreach (var f in tag.UserTextFrames)
			f.Value = "\0V2";
		tag = Reparse (tag);
		Assert.AreEqual ("V2", tag.Artist);
		Assert.AreEqual ("V2", tag.Genre);
		Assert.AreEqual ("V2", tag.Movement);
		var before = Snapshot (tag);

		tag.Artist = tag.Artist;
		tag.Genre = tag.Genre;
		tag.Movement = tag.Movement;
		CollectionAssert.AreEqual (before, Snapshot (Reparse (tag)));

		tag.Artist = "";
		tag.Genre = "";
		tag.Movement = "";

		Assert.IsNull (tag.GetTextFrame ("TPE1"));
		Assert.IsNull (tag.GetTextFrame ("TCON"));
		Assert.IsNull (tag.GetTextFrame ("MVNM"));
		Assert.IsNull (tag.GetUserText ("MOVEMENT"));
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
	public void Genre_EmptyValues_ReadTheFirstNonEmptyValue ()
	{
		// Like every other text frame, the genre is the first non-empty value, and "" only when
		// every value is empty. Writing back what was read keeps the frame either way.
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.Genre = "Rock";
		tag.Frames.First (f => f.Id == "TCON").Text = "";
		Assert.AreEqual ("", tag.Genre);
		tag.Genre = tag.Genre;
		Assert.AreEqual ("", tag.Frames.First (f => f.Id == "TCON").Text);

		tag.Frames.First (f => f.Id == "TCON").Text = "\0Pop";
		Assert.AreEqual ("Pop", tag.Genre);
		tag.Genre = tag.Genre;
		Assert.AreEqual ("\0Pop", tag.Frames.First (f => f.Id == "TCON").Text);
		tag.Genre = "";
		Assert.IsNull (tag.GetTextFrame ("TCON"));

		tag.Genre = "Rock";
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
