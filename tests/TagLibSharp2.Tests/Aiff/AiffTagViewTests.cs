// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Aiff;

/// <summary>
/// The AIFF combined tag view: every <see cref="Tag"/> member, Clear, IsEmpty, CopyTo, and lifetime.
/// </summary>
public partial class AiffAnnotationTests
{
	static IReadOnlyList<PropertyInfo> SettableTagMembers () => TagMemberSamples.Settable;

	static object MemberSample (PropertyInfo p) => TagMemberSamples.Sample (p);

	static object? MemberCleared (PropertyInfo p) => TagMemberSamples.Cleared (p);

	static string Member (PropertyInfo p, Tag? tag) => TagMemberSamples.Describe (p, tag);

	[TestMethod]
	public void EveryTagMember_ReadsWritesAndClearsThroughTheView_LikeId3 ()
	{
		// The oracle is an Id3v2Tag on its own. The AIFF view over the same ID3 tag must give
		// the same answer for every settable member: read, write, save, and clear.
		var failures = new List<string> ();
		foreach (var p in SettableTagMembers ()) {
			var sample = MemberSample (p);
			var oracle = new Id3v2Tag ();
			p.SetValue (oracle, sample);
			var oracleSaved = Id3v2Tag.Read (oracle.Render ().Span).Tag!;
			var expected = Member (p, oracleSaved);
			p.SetValue (oracleSaved, MemberCleared (p));
			var expectedCleared = Member (p, Id3v2Tag.Read (oracleSaved.Render ().Span).Tag ?? new Id3v2Tag ());

			var read = ReadAiff (Form (Comm (), Id3 (oracle), Ssnd ()));
			var readValue = Member (p, read.Tag);
			if (readValue != expected)
				failures.Add ($"{p.Name} read {readValue}, expected {expected}");

			// A native-only file: the view exists without an ID3 chunk, and the write creates one.
			var written = ReadAiff (Form (Text ("ANNO", "Note"), Comm (), Ssnd ()));
			p.SetValue (written.Tag!, sample);
			var writtenAgain = ReadAiff (written.Render ().ToArray ());
			var writtenValue = Member (p, writtenAgain.Tag);
			var writtenId3 = Member (p, writtenAgain.Id3v2Tag);
			if (writtenValue != expected || writtenId3 != expected)
				failures.Add ($"{p.Name} wrote view {writtenValue} / ID3 {writtenId3}, expected {expected}");

			var cleared = ReadAiff (Form (Comm (), Id3 (oracle), Ssnd ()));
			p.SetValue (cleared.Tag!, MemberCleared (p));
			var clearedAgain = ReadAiff (cleared.Render ().ToArray ());
			var clearedValue = Member (p, clearedAgain.Tag ?? new Id3v2Tag ());
			if (clearedValue != expectedCleared)
				failures.Add ($"{p.Name} cleared {clearedValue}, expected {expectedCleared}");
		}

		Assert.AreEqual (0, failures.Count, string.Join ("\n", failures));
	}

	[TestMethod]
	public void Clear_RemovesNativeTextAndId3_ButKeepsMarkerNotes ()
	{
		var file = ReadAiff (Form (
			Text ("NAME", "Song"),
			Text ("AUTH", "A Person"),
			Text ("(c) ", "C 1999"),
			Text ("ANNO", "Note"),
			Marked ((0, "File"), (5, "Cue")),
			Id3 (new Id3v2Tag { Album = "Album", Lyrics = "words" }),
			Comm (),
			Ssnd ()));
		var tag = file.Tag!;
		Assert.IsFalse (tag.IsEmpty);

		tag.Clear ();

		Assert.IsTrue (tag.IsEmpty);
		Assert.AreSame (tag, file.Tag);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Artist);
		Assert.IsNull (tag.Copyright);
		Assert.IsNull (tag.Comment);
		Assert.IsNull (tag.Album);

		var bytes = file.Render ().ToArray ();
		Assert.IsFalse (HasId3 (bytes));
		var again = ReadAiff (bytes);
		foreach (var id in new[] { "NAME", "AUTH", "(c) ", "ANNO" })
			Assert.IsNull (again.GetChunk (id), id);
		var comt = again.GetChunk ("COMT");
		Assert.IsNotNull (comt, "Marker notes belong to the markers, not to the tag.");
		Assert.IsTrue (comt.Data.ToStringLatin1 ().Contains ("Cue", StringComparison.Ordinal));
		Assert.IsFalse (comt.Data.ToStringLatin1 ().Contains ("File", StringComparison.Ordinal));
		Assert.IsNull (again.Tag?.Comment);
		Assert.IsNull (again.Tag?.Title);
	}

	[TestMethod]
	public void IsEmpty_CountsEachNativeField ()
	{
		foreach (var chunk in new[] { Text ("NAME", "Song"), Text ("AUTH", "Who"), Text ("(c) ", "C"), Text ("ANNO", "Note"), Comt ("File") }) {
			var file = ReadAiff (Form (chunk, Comm (), Ssnd ()));
			Assert.IsFalse (file.Tag!.IsEmpty, System.Text.Encoding.Latin1.GetString (chunk, 0, 4));
		}

		var markerOnly = ReadAiff (Form (Marked ((5, "Cue")), Comm (), Ssnd ()));
		Assert.IsTrue (markerOnly.Tag is null || markerOnly.Tag.IsEmpty, "A marker note is not a tag field.");
	}

	[TestMethod]
	public void CopyTo_FromTheView_MatchesCopyFromTheId3Tag ()
	{
		var id3 = new Id3v2Tag ();
		foreach (var p in SettableTagMembers ())
			p.SetValue (id3, MemberSample (p));
		// A v2.4 recording time keeps its month and day when copied, as Id3v2Tag.CopyTo does.
		id3.Year = "2019-05-06";
		var file = ReadAiff (Form (Comm (), Id3 (id3), Ssnd ()));

		var fromView = new Id3v2Tag ();
		file.Tag!.CopyTo (fromView);
		var fromId3 = new Id3v2Tag ();
		file.Id3v2Tag!.CopyTo (fromId3);

		Assert.AreEqual ("2019-05-06", fromId3.GetTextFrame ("TDRC"));
		Assert.AreEqual (fromId3.GetTextFrame ("TDRC"), fromView.GetTextFrame ("TDRC"));
		foreach (var p in SettableTagMembers ())
			Assert.AreEqual (Member (p, fromId3), Member (p, fromView), p.Name);
	}

	[TestMethod]
	public void Id3AssignedOnUntaggedFile_CreatesOneCachedView ()
	{
		var file = ReadAiff (Form (Comm (), Ssnd ()));
		Assert.IsNull (file.Tag);

		file.Id3v2Tag = new Id3v2Tag { Title = "New" };
		var view = file.Tag;
		Assert.IsNotNull (view);
		Assert.AreSame (view, file.Tag);
		Assert.AreEqual ("New", view.Title);

		Assert.ThrowsExactly<NotSupportedException> (() => view.Render ());

		file.Id3v2Tag = null;
		Assert.AreSame (view, file.Tag, "The view outlives the ID3 tag it was created over.");
		view.Album = "Again";
		Assert.AreEqual ("Again", file.Id3v2Tag?.Album);
	}
}
