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
	public void EveryStringProperty_UnchangedWriteBack_KeepsEveryField (string first, string second)
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

			property.SetValue (comment, property.GetValue (comment));
			var after = Snapshot (Reparse (comment));

			if (!before.SequenceEqual (after))
				failures.Add ($"{property.Name}: [{string.Join (" | ", before)}] -> [{string.Join (" | ", after)}]");
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
		foreach (var name in ListFields)
			Assert.Contains (name, covered);
		Assert.IsGreaterThanOrEqualTo (45, covered.Count, string.Join (",", covered));
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
