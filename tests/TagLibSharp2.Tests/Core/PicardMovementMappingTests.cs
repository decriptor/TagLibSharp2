// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Vorbis and APE movements follow MusicBrainz Picard's tag mapping
/// (https://picard-docs.musicbrainz.org/en/v3.0/appendices/tag_mapping.html): the name is
/// MOVEMENTNAME and the number is MOVEMENT. Setting or clearing one half never changes how the
/// other half reads, in any layout.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class PicardMovementMappingTests
{
	static readonly (string Name, (string Key, string Value)[] Fields)[] Layouts = [
		("picard", [("MOVEMENT", "2"), ("MOVEMENTNAME", "Allegro")]),
		("picard number only", [("MOVEMENT", "2")]),
		("picard name only", [("MOVEMENTNAME", "Allegro")]),
		("picard + MOVEMENTNUMBER", [("MOVEMENT", "2"), ("MOVEMENTNAME", "Allegro"), ("MOVEMENTNUMBER", "3")]),
		("legacy name", [("MOVEMENT", "Allegro")]),
		("legacy name + number", [("MOVEMENT", "Allegro"), ("MOVEMENTNUMBER", "2")]),
		("numeric name", [("MOVEMENTNAME", "1812"), ("MOVEMENT", "4")]),
		("empty", []),
	];

	static Tag Build (string format, (string Key, string Value)[] fields)
	{
		if (format == "vorbis") {
			var comment = new VorbisComment ("test");
			foreach (var (key, value) in fields)
				comment.AddField (key, value);
			return VorbisComment.Read (comment.Render ().Span).Tag!;
		}

		var ape = new ApeTag ();
		foreach (var (key, value) in fields)
			ape.SetValue (key, value);
		return ApeTag.Parse (ape.RenderWithOptions (includeHeader: true).Span).Tag!;
	}

	static Tag Reparse (Tag tag) => tag is VorbisComment
		? VorbisComment.Read (tag.Render ().Span).Tag!
		: ApeTag.Parse (((ApeTag)tag).RenderWithOptions (includeHeader: true).Span).Tag!;

	[TestMethod]
	[DataRow ("vorbis")]
	[DataRow ("ape")]
	public void SetOrClearOneHalf_LeavesTheOtherHalfAsItRead (string format)
	{
		var failures = new List<string> ();
		foreach (var (layout, fields) in Layouts) {
			var ops = new (string Name, Action<Tag> Apply, bool Name_, object? Expected)[] {
				("Movement = null", t => t.Movement = null, true, null),
				("Movement = X", t => t.Movement = "X", true, "X"),
				("Movement = 99", t => t.Movement = "99", true, "99"),
				("MovementNumber = null", t => t.MovementNumber = null, false, null),
				("MovementNumber = 7", t => t.MovementNumber = 7, false, 7u),
			};
			foreach (var (op, apply, nameHalf, expected) in ops) {
				var tag = Build (format, fields);
				var name = tag.Movement;
				var number = tag.MovementNumber;

				apply (tag);
				foreach (var (side, t) in new[] { ("live", tag), ("reparsed", Reparse (tag)) }) {
					var gotName = t.Movement;
					var gotNumber = t.MovementNumber;
					var ok = nameHalf
						? Equals (gotName, expected) && gotNumber == number
						: gotNumber == (uint?)expected && gotName == name;
					if (!ok)
						failures.Add ($"{layout} {op} {side}: name {name} -> {gotName}, number {number} -> {gotNumber}");
				}
			}
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
	}

	[TestMethod]
	[DataRow ("vorbis")]
	[DataRow ("ape")]
	public void PicardLayout_ReadsNameAndNumber_AndWritesThePicardKeys (string format)
	{
		var tag = Build (format, [("MOVEMENT", "2"), ("MOVEMENTNAME", "Allegro"), ("MOVEMENTTOTAL", "4")]);
		Assert.AreEqual ("Allegro", tag.Movement);
		Assert.AreEqual (2u, tag.MovementNumber);
		Assert.AreEqual (4u, tag.MovementTotal);

		tag.Movement = tag.Movement;
		tag.MovementNumber = tag.MovementNumber;
		Assert.AreEqual ("MOVEMENT=2|MOVEMENTNAME=Allegro|MOVEMENTNUMBER=", Raw (tag));

		tag.MovementNumber = 5;
		tag.Movement = "Adagio";
		Assert.AreEqual ("MOVEMENT=5|MOVEMENTNAME=Adagio|MOVEMENTNUMBER=", Raw (tag));

		tag.MovementNumber = null;
		Assert.AreEqual ("MOVEMENT=|MOVEMENTNAME=Adagio|MOVEMENTNUMBER=", Raw (tag));
		Assert.AreEqual ("Adagio", tag.Movement);
	}

	[TestMethod]
	[DataRow ("vorbis")]
	[DataRow ("ape")]
	public void MovementNumber_WriteBack_KeepsTheStoredText (string format)
	{
		var tag = Build (format, [("MOVEMENTNUMBER", "03"), ("MOVEMENT", "Allegro")]);
		Assert.AreEqual (3u, tag.MovementNumber);

		tag.MovementNumber = tag.MovementNumber;

		Assert.AreEqual ("MOVEMENT=Allegro|MOVEMENTNAME=|MOVEMENTNUMBER=03", Raw (tag));
	}

	static string Raw (Tag tag)
	{
		string Get (string key) => tag is VorbisComment c ? string.Join (",", c.GetValues (key)) : ((ApeTag)tag).GetValue (key) ?? "";
		return $"MOVEMENT={Get ("MOVEMENT")}|MOVEMENTNAME={Get ("MOVEMENTNAME")}|MOVEMENTNUMBER={Get ("MOVEMENTNUMBER")}";
	}
}
