// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text;
using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A copy makes the target hold the source's value. The setters' write-back no-op must not fire
/// during <see cref="Tag.CopyTo"/>, even when the source value equals the target's first value,
/// or the target keeps values the source does not have.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class TagCopyReplacesListTests
{
	const string Stale = "Zq2";

	static readonly string[] BasicFields = ["Title", "Artist", "Album", "Genre"];

	static IEnumerable<PropertyInfo> StringProperties () =>
		typeof (Tag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.PropertyType == typeof (string) && p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0);

	/// <summary>
	/// A target whose field for <paramref name="property"/> holds "V1" and then <see cref="Stale"/>,
	/// or null when the format does not store "V1" for that property in a text field.
	/// </summary>
	static Tag? BuildTarget (string format, PropertyInfo property)
	{
		switch (format) {
			case "id3v23":
			case "id3v24": {
					var tag = new Id3v2Tag (format == "id3v23" ? Id3v2Version.V23 : Id3v2Version.V24);
					property.SetValue (tag, "V1");
					if (tag.Frames.Count + tag.UserTextFrames.Count == 0 || !Equals (property.GetValue (tag), "V1"))
						return null;
					foreach (var f in tag.Frames)
						f.Text = "V1\0" + Stale;
					foreach (var f in tag.UserTextFrames)
						f.Value = "V1\0" + Stale;
					return Id3v2Tag.Read (tag.Render ().Span).Tag;
				}
			case "ape": {
					var probe = new ApeTag ();
					property.SetValue (probe, "V1");
					if (probe.ItemCount != 1 || !Equals (property.GetValue (probe), "V1"))
						return null;
					var tag = new ApeTag ();
					tag.SetValue (ApeKey (probe), "V1\0" + Stale);
					return ApeTag.Parse (tag.RenderWithOptions (includeHeader: true).Span).Tag;
				}
			case "vorbis": {
					var probe = new VorbisComment ("test");
					property.SetValue (probe, "V1");
					if (probe.Fields.Count != 1 || !Equals (property.GetValue (probe), "V1"))
						return null;
					var tag = new VorbisComment ("test");
					tag.AddField (probe.Fields[0].Name, "V1");
					tag.AddField (probe.Fields[0].Name, Stale);
					return VorbisComment.Read (tag.Render ().Span).Tag;
				}
			default:
				throw new ArgumentOutOfRangeException (nameof (format));
		}
	}

	static string ApeKey (ApeTag tag)
	{
		// One item: value size (4, LE), flags (4), key (ASCII, NUL-terminated), value.
		var data = tag.Render ().ToArray ();
		var keyEnd = Array.IndexOf (data, (byte)0, 8);
		return Encoding.ASCII.GetString (data, 8, keyEnd - 8);
	}

	static Tag NewSource (string format) => format switch {
		"id3v24" => new Id3v2Tag (Id3v2Version.V24),
		"ape" => new ApeTag (),
		_ => new VorbisComment ("source"),
	};

	/// <summary>
	/// Whether CopyTo (All) writes this property at all: a different source value must arrive.
	/// </summary>
	static bool IsCopied (string sourceFormat, string targetFormat, PropertyInfo property)
	{
		var source = NewSource (sourceFormat);
		property.SetValue (source, "V9");
		var target = BuildTarget (targetFormat, property)!;
		source.CopyTo (target, TagCopyOptions.All);
		return Equals (property.GetValue (target), "V9");
	}

	static bool RenderHolds (Tag tag, string text)
	{
		var bytes = tag.Render ().ToArray ();
		var needle = Encoding.UTF8.GetBytes (text);
		for (var i = 0; i + needle.Length <= bytes.Length; i++) {
			if (bytes.AsSpan (i, needle.Length).SequenceEqual (needle))
				return true;
		}
		return false;
	}

	[TestMethod]
	[DataRow ("id3v23")]
	[DataRow ("id3v24")]
	[DataRow ("ape")]
	[DataRow ("vorbis")]
	public void CopyAll_SourceEqualsTargetFirstValue_ReplacesTheList (string targetFormat)
	{
		var covered = new List<string> ();
		var failures = new List<string> ();
		foreach (var sourceFormat in new[] { "id3v24", "ape", "vorbis" }) {
			foreach (var property in StringProperties ()) {
				var source = NewSource (sourceFormat);
				property.SetValue (source, "V1");
				if (!Equals (property.GetValue (source), "V1"))
					continue;
				var target = BuildTarget (targetFormat, property);
				if (target is null || !IsCopied (sourceFormat, targetFormat, property))
					continue;
				covered.Add (property.Name);

				source.CopyTo (target, TagCopyOptions.All);

				if (!Equals (property.GetValue (target), "V1") || RenderHolds (target, Stale))
					failures.Add ($"{sourceFormat}->{targetFormat} {property.Name}: read {property.GetValue (target)}, stale kept={RenderHolds (target, Stale)}");
			}
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
		foreach (var name in new[] { "Artist", "Genre", "Publisher", "Isrc", "CatalogNumber", "MusicBrainzArtistId" })
			Assert.Contains (name, covered);
	}

	[TestMethod]
	[DataRow ("id3v23")]
	[DataRow ("id3v24")]
	[DataRow ("ape")]
	[DataRow ("vorbis")]
	public void CopyBasic_SourceEqualsTargetFirstValue_ReplacesTheList (string targetFormat)
	{
		var failures = new List<string> ();
		foreach (var sourceFormat in new[] { "id3v24", "ape", "vorbis" }) {
			foreach (var name in BasicFields) {
				var property = typeof (Tag).GetProperty (name)!;
				var source = NewSource (sourceFormat);
				property.SetValue (source, "V1");
				var target = BuildTarget (targetFormat, property)!;

				source.CopyTo (target, TagCopyOptions.Basic);

				if (!Equals (property.GetValue (target), "V1") || RenderHolds (target, Stale))
					failures.Add ($"{sourceFormat}->{targetFormat} {name}");
			}
		}

		Assert.IsEmpty (failures, string.Join ("\n", failures));
	}

	[TestMethod]
	public void CopyBasic_IntoFlacFileTag_ReplacesTheList ()
	{
		var file = FlacFile.Read (TestBuilders.Flac.CreateMinimal ()).File!;
		var comment = new VorbisComment ("test");
		comment.AddField ("ARTIST", "P1");
		comment.AddField ("ARTIST", "P2");
		file.VorbisComment = comment;
		var source = new Id3v2Tag (Id3v2Version.V24) { Artist = "P1" };

		source.CopyTo (file.Tag!, TagCopyOptions.Basic);

		Assert.AreEqual ("P1", string.Join ("|", file.VorbisComment!.GetValues ("ARTIST")));
	}

	[TestMethod]
	public void Copy_ValueEqualToTarget_KeepsTheTargetsFallbackFields ()
	{
		// MOVEMENTNAME, ORIGINALYEAR, and APE Date are fields of their own beside the value the getter
		// reads. A copy of that same value leaves them, and drops only other values of the primary field.
		static VorbisComment Picard ()
		{
			var c = new VorbisComment ("picard");
			c.AddField ("MOVEMENT", "2");
			c.AddField ("MOVEMENTNAME", "Allegro");
			c.AddField ("ORIGINALDATE", "1969-09-26");
			c.AddField ("ORIGINALYEAR", "1969");
			return c;
		}

		var target = Picard ();
		target.AddField ("MOVEMENT", "X");
		Picard ().CopyTo (target, TagCopyOptions.All);
		Assert.AreEqual ("2", string.Join ("|", target.GetValues ("MOVEMENT")));
		Assert.AreEqual ("Allegro", string.Join ("|", target.GetValues ("MOVEMENTNAME")));
		Assert.AreEqual ("1969", string.Join ("|", target.GetValues ("ORIGINALYEAR")));

		var nameOnly = new VorbisComment ("t");
		nameOnly.AddField ("MOVEMENTNAME", "Allegro");
		new VorbisComment ("s") { Movement = "Allegro" }.CopyTo (nameOnly, TagCopyOptions.All);
		Assert.HasCount (0, nameOnly.GetValues ("MOVEMENT"));
		Assert.AreEqual ("Allegro", nameOnly.GetValue ("MOVEMENTNAME"));

		var ape = new ApeTag ();
		ape.SetValue ("Year", "2001");
		ape.SetValue ("Date", "2001-05-03");
		new ApeTag { Year = "2001" }.CopyTo (ape, TagCopyOptions.Basic);
		Assert.AreEqual ("2001-05-03", ape.GetValue ("Date"));
	}

	[TestMethod]
	public void WriteBack_AfterACopy_IsANoOpAgain ()
	{
		var target = new Id3v2Tag (Id3v2Version.V24);
		target.SetTextFrameValues ("TPUB", ["Lab1", "Lab2"]);
		new VorbisComment ("source") { Publisher = "Lab1" }.CopyTo (target, TagCopyOptions.All);
		Assert.AreEqual ("Lab1", string.Join ("|", target.GetTextFrameValues ("TPUB")));

		target.SetTextFrameValues ("TPUB", ["Lab1", "Lab2"]);
		target.Publisher = target.Publisher;

		Assert.AreEqual ("Lab1|Lab2", string.Join ("|", target.GetTextFrameValues ("TPUB")));
	}

	[TestMethod]
	public void CopyThatThrows_StillEndsTheCopyScope ()
	{
		var source = new ThrowingTag ();
		var target = new Id3v2Tag (Id3v2Version.V24);

		Assert.ThrowsExactly<InvalidOperationException> (() => source.CopyTo (target, TagCopyOptions.Basic));

		target.SetTextFrameValues ("TPE1", ["P1", "P2"]);
		target.Artist = target.Artist;
		Assert.AreEqual ("P1|P2", string.Join ("|", target.GetTextFrameValues ("TPE1")));
	}

	sealed class ThrowingTag : Tag
	{
		public override TagTypes TagType => TagTypes.None;
		public override string? Title { get => throw new InvalidOperationException (); set { } }
		public override string? Artist { get => null; set { } }
		public override string? Album { get => null; set { } }
		public override string? Year { get => null; set { } }
		public override string? Comment { get => null; set { } }
		public override string? Genre { get => null; set { } }
		public override uint? Track { get => null; set { } }
		public override BinaryData Render () => BinaryData.Empty;
		public override void Clear () { }
	}
}
