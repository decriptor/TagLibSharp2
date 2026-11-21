// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

/// <summary>
/// An editor that writes back the value a property returned must not change the comment.
/// A field name may repeat (https://xiph.org/vorbis/doc/v-comment.html), and the single-value
/// getter returns the first, so a write-back must not drop the others.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class VorbisCommentWriteBackTests
{
	static readonly string[] ListFields = ["Artist", "AlbumArtist", "Composer", "Genre"];

	static IEnumerable<PropertyInfo> StringProperties () =>
		typeof (VorbisComment).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.PropertyType == typeof (string) && p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0);

	static VorbisComment Reparse (VorbisComment comment)
	{
		var result = VorbisComment.Read (comment.Render ().Span);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}

	static List<string> Snapshot (VorbisComment comment) =>
		comment.Fields.Select (f => f.Name + "=" + f.Value).ToList ();

	[TestMethod]
	[DataRow ("V1", "V2")]
	[DataRow ("", "V2")]
	public void EveryStringProperty_UnchangedWriteBack_KeepsEveryField_AndAnEditReplaces (string first, string second)
	{
		var covered = new List<string> ();
		var failures = new List<string> ();
		foreach (var property in StringProperties ()) {
			var probe = new VorbisComment ("test");
			property.SetValue (probe, "V1");
			if (probe.Fields.Count != 1 || !Equals (property.GetValue (probe), "V1"))
				continue;
			var name = probe.Fields[0].Name;
			covered.Add (property.Name);

			var comment = new VorbisComment ("test");
			comment.AddField (name, first);
			comment.AddField (name, second);
			comment = Reparse (comment);
			var before = Snapshot (comment);
			var read = (string?)property.GetValue (comment);
			property.SetValue (comment, read);
			var after = Snapshot (Reparse (comment));

			if (!before.SequenceEqual (after))
				failures.Add ($"{property.Name}: [{string.Join (" | ", before)}] -> [{string.Join (" | ", after)}]");

			// A case-only edit, or "" over a list whose later values hold text, is not a write-back.
			var current = (string?)property.GetValue (comment);
			var edit = string.IsNullOrEmpty (current) ? "" : current!.ToLowerInvariant ();
			property.SetValue (comment, edit);
			var edited = Snapshot (Reparse (comment));
			if (edited.Any (r => r.Contains ("V2", StringComparison.Ordinal)) || (edit.Length != 0 && !Equals (property.GetValue (comment), edit)))
				failures.Add ($"{property.Name} edit {edit}: [{string.Join (" | ", edited)}]");
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
		foreach (var name in ListFields)
			Assert.Contains (name, covered);
		Assert.IsGreaterThanOrEqualTo (45, covered.Count, string.Join (",", covered));
	}

	[TestMethod]
	public void LeadingEmptyField_ReadsTheNextValue_WriteBackKeepsIt_EmptyStringClears ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ARTIST", "");
		comment.AddField ("ARTIST", "V2");
		comment.AddField ("MOVEMENT", "");
		comment.AddField ("MOVEMENT", "V2");
		comment.AddField ("MOVEMENTNAME", "N");
		Assert.AreEqual ("V2", comment.Artist);
		Assert.AreEqual ("V2", comment.Movement);
		var before = string.Join ("|", comment.Fields.Select (f => f.Name + "=" + f.Value));
		comment.Artist = comment.Artist;
		comment.Movement = comment.Movement;
		Assert.AreEqual (before, string.Join ("|", comment.Fields.Select (f => f.Name + "=" + f.Value)));

		comment.Artist = "";
		comment.Movement = "";

		Assert.AreEqual ("", string.Join ("|", comment.GetValues ("ARTIST")));
		Assert.AreEqual ("", string.Join ("|", comment.GetValues ("MOVEMENT")));
		Assert.HasCount (0, comment.GetValues ("MOVEMENTNAME"));
	}

	[TestMethod]
	public void EmptyString_OverEmptyFieldsOnly_IsAWriteBack ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("ARTIST", "");
		comment.AddField ("ARTIST", "");

		comment.Artist = comment.Artist;

		Assert.HasCount (2, comment.GetValues ("ARTIST"));
	}

	[TestMethod]
	public void Genre_NewValueOrClear_StillReplacesEveryField ()
	{
		var comment = new VorbisComment ("test");
		comment.AddField ("GENRE", "Rock");
		comment.AddField ("GENRE", "Pop");
		comment.AddField ("ARTIST", "P1");
		comment.AddField ("ARTIST", "P2");

		comment.Genre = "Jazz";
		comment.Artist = null;

		Assert.AreEqual ("Jazz", string.Join ("|", comment.GetValues ("GENRE")));
		Assert.HasCount (0, comment.GetValues ("ARTIST"));
	}
}
