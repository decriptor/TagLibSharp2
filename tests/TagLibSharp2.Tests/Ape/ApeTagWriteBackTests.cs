// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text;
using TagLibSharp2.Ape;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// An editor that writes back the value a property returned must not change the tag.
/// An APEv2 text item may hold several values separated by a zero byte, so a write-back
/// must not collapse or comma-join the list.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ApeTagWriteBackTests
{
	static readonly string[] ListFields = ["Artist", "AlbumArtist", "Composer", "Genre"];

	static IEnumerable<PropertyInfo> StringProperties () =>
		typeof (ApeTag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.PropertyType == typeof (string) && p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0);

	static ApeTag Reparse (ApeTag tag)
	{
		var result = ApeTag.Parse (tag.RenderWithOptions (includeHeader: true).Span);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}

	/// <summary>
	/// The items of a rendered tag as key=raw value, in order. Item layout per APEv2: value size (4, LE),
	/// flags (4), key (ASCII, NUL-terminated), value.
	/// </summary>
	static List<string> Snapshot (ApeTag tag)
	{
		var data = tag.Render ().ToArray ();
		var rows = new List<string> ();
		var pos = 0;
		var end = data.Length - 32;
		while (pos < end) {
			var size = BitConverter.ToInt32 (data, pos);
			var flags = BitConverter.ToInt32 (data, pos + 4);
			var keyEnd = Array.IndexOf (data, (byte)0, pos + 8);
			var key = Encoding.ASCII.GetString (data, pos + 8, keyEnd - pos - 8);
			var value = Encoding.UTF8.GetString (data, keyEnd + 1, size);
			rows.Add ($"{key}/{flags}={value}");
			pos = keyEnd + 1 + size;
		}
		rows.Sort (StringComparer.Ordinal);
		return rows;
	}

	static string? SingleKey (ApeTag tag)
	{
		var rows = Snapshot (tag);
		if (rows.Count != 1)
			return null;
		return rows[0].Substring (0, rows[0].IndexOf ('/', StringComparison.Ordinal));
	}

	[TestMethod]
	[DataRow ("V1\0V2")]
	[DataRow ("\0V2")]
	[DataRow ("")]
	public void EveryStringProperty_UnchangedWriteBack_KeepsTheItem (string raw)
	{
		var covered = new List<string> ();
		var failures = new List<string> ();
		foreach (var property in StringProperties ()) {
			var probe = new ApeTag ();
			property.SetValue (probe, "V1");
			var key = SingleKey (probe);
			if (key is null || !Equals (property.GetValue (probe), "V1"))
				continue;
			covered.Add (property.Name);

			var tag = new ApeTag ();
			tag.SetValue (key, raw);
			tag = Reparse (tag);
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
	public void ListFields_UnchangedWriteBack_KeepBothNames ()
	{
		var tag = new ApeTag ();
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
	public void AlbumArtist_WriteBackFromAliasItem_KeepsAliasList ()
	{
		var tag = new ApeTag ();
		tag.SetValue ("ALBUMARTIST", "A1\0A2");
		tag = Reparse (tag);
		Assert.AreEqual ("A1", tag.AlbumArtist);

		tag.AlbumArtist = tag.AlbumArtist;

		var reparsed = Reparse (tag);
		Assert.AreEqual ("A1|A2", string.Join ("|", reparsed.AlbumArtists));
		Assert.AreEqual ("A1, A2", reparsed.GetValue ("ALBUMARTIST"));
	}

	[TestMethod]
	public void ListFields_NewValueOrClear_StillReplacesTheList ()
	{
		var tag = new ApeTag ();
		tag.Performers = ["P1", "P2"];
		tag.Genres = ["Rock", "Pop"];
		tag.Composers = ["C1", "C2"];

		tag.Artist = "P3";
		tag.Genre = null;
		tag.Composer = "";

		Assert.AreEqual ("P3", string.Join ("|", tag.Performers));
		Assert.IsEmpty (tag.Genres);
		Assert.IsEmpty (tag.Composers);
	}

	[TestMethod]
	public void Year_EmptyYearItem_WriteBackKeepsDate ()
	{
		var tag = new ApeTag ();
		tag.SetValue ("Year", "");
		tag.SetValue ("Date", "2001-05-03");
		tag = Reparse (tag);
		Assert.AreEqual ("2001-05-03", tag.Year);

		tag.Year = tag.Year;

		var reparsed = Reparse (tag);
		Assert.AreEqual ("2001-05-03", reparsed.GetValue ("Date"));
		Assert.AreEqual ("", reparsed.GetValue ("Year"));

		reparsed.Year = "";
		Assert.IsNull (reparsed.GetValue ("Year"));
		Assert.IsNull (reparsed.GetValue ("Date"));
	}
}
