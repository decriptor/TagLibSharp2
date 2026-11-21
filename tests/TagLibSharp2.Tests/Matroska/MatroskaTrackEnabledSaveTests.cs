// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Saving <c>TrackEntry\FlagEnabled</c>. RFC 9559 §5.1.4.1.4.
/// Invariant: a save writes the flag only for a track the caller set, and every segment position in the
/// SeekHead, Cues, and Cluster <c>Position</c> still names its element afterwards. Room comes from the
/// Void right after Tracks when it has enough; otherwise everything after Tracks moves once.
/// </summary>
public partial class MatroskaFileTests
{
	[TestMethod]
	public void Save_UntouchedTracks_StaysByteIdentical ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050), VideoEntry (2, 640, Enabled (1))), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;

		Assert.IsTrue (parsed.Render (original).Span.SequenceEqual (original));
		parsed.Tracks[0].IsEnabled = true;
		parsed.Tracks[1].IsEnabled = true;
		Assert.IsTrue (parsed.Render (original).Span.SequenceEqual (original));
	}

	[TestMethod]
	public void Save_DisableWithAStoredFlag_WritesTheByteInPlace ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050, Enabled (1)), AudioEntry (2, 44100, Default (0))), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual (22050, ((IMediaFile)parsed).AudioProperties!.SampleRate);
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.AreEqual (1, DifferingBytes (original, saved));
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.IsTrue (again.Tracks[1].IsEnabled);
		Assert.AreEqual (44100, ((IMediaFile)again).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Save_InPlaceFlag_LeavesAnUnreadableSeekHeadAlone ()
	{
		// Nothing moves, so a SeekHead the writer cannot walk is not a reason to fail.
		var seekHead = Master (MatroskaIds.SeekHead, new byte[] { 0x00, 0x00, 0x00 });
		var tracks = Master (0x1654AE6B, AudioEntry (1, 22050, Enabled (1)));
		var original = TrackFile (seekHead, tracks, Master (0x1F43B675, ClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (1, DifferingBytes (original, saved));
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_InsertedFlag_LeavesAnUnreadableSeekHeadAlone ()
	{
		// The Void after Tracks absorbs the element, so the SeekHead is never read.
		var seekHead = Master (MatroskaIds.SeekHead, new byte[] { 0x00, 0x00, 0x00 });
		var tracks = Master (0x1654AE6B, AudioEntry (1, 22050));
		var original = TrackFile (seekHead, tracks, MakeVoid (8), Master (0x1F43B675, ClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.IsTrue (saved.AsSpan ().StartsWith (original.AsSpan (0, IndexOf (original, Master (0x1654AE6B, AudioEntry (1, 22050))))));
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_EnableWithAStoredFlag_WritesTheByteInPlace ()
	{
		var original = TracksFile (AudioEntry (1, 22050, Element (0xB9, new byte[] { 0, 0 })), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		Assert.IsFalse (parsed.Tracks[0].IsEnabled);
		parsed.Tracks[0].IsEnabled = true;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.AreEqual (1, DifferingBytes (original, saved));
		Assert.IsTrue (saved.AsSpan ().IndexOf (Element (0xB9, new byte[] { 0, 1 })) >= 0);
		Assert.IsTrue (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_DisableWithoutAFlag_ShrinksTheVoidAfterTracks ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050), AudioEntry (2, 44100)), withVoid: 20);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.AreEqual (IndexOf (original, ClusterMarker), IndexOf (saved, ClusterMarker));
		Assert.IsTrue (saved.AsSpan ().IndexOf (Element (0xEC, new byte[15])) >= 0);
		AssertSeeksLand (original, saved);
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.IsTrue (again.Tracks[1].IsEnabled);
		Assert.AreEqual (44100, ((IMediaFile)again).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Save_DisableWithoutAFlag_TakesAVoidOfExactlyTheFlagSize ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 3);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.AreEqual (IndexOf (original, ClusterMarker), IndexOf (saved, ClusterMarker));
		Assert.AreEqual (-1, saved.AsSpan ().IndexOf (new byte[] { 0xEC, 0x81, 0x00 }));
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_VoidBeforeTracks_StaysAndLaterElementsMove ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Before"u8));
		var tracks = Master (0x1654AE6B, Concat (AudioEntry (1, 22050), AudioEntry (2, 44100)));
		var hole = Element (0xEC, new byte[30]);
		var original = Layout (new[] { MatroskaIds.Info, MatroskaIds.Tracks, MatroskaIds.Cues, MatroskaIds.Cluster },
			(seekHead, positions) => Concat (seekHead, hole, info, tracks, CueElement (positions[3]), Master (0x1F43B675, ClusterMarker)));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 3, saved.Length);
		Assert.AreEqual (IndexOf (original, hole), IndexOf (saved, hole));
		AssertMovedCorrectly (original, saved);
		Assert.AreEqual ("Before", MatroskaFile.Read (saved).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_InsertWithoutAVoid_MovesTheLaterElements ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050), AudioEntry (2, 44100)), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 3, saved.Length);
		Assert.AreEqual (1, CountOf (saved, ClusterMarker));
		AssertMovedCorrectly (original, saved);
		Assert.IsTrue (EveryCrcMatches (saved));
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.IsTrue (again.Tracks[1].IsEnabled);
		Assert.AreEqual ("Movie", again.Tag.Title);
		Assert.AreEqual (44100, ((IMediaFile)again).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public async Task SaveAsync_InsertWithoutAVoid_MovesTheLaterElements ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;
		var fs = new MockFileSystem ();

		var saved = await parsed.SaveToFileAsync ("/shift-async.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/shift-async.mkv");
		Assert.AreEqual (original.Length + 3, bytes.Length);
		AssertMovedCorrectly (original, bytes);
		Assert.IsTrue (parsed.Render (original).Span.SequenceEqual (bytes));
	}

	[TestMethod]
	public void Save_InsertWithoutAVoid_MovesClusterPosition ()
	{
		// Cluster\Position is the cluster's own segment position (RFC 9559 §5.1.3.2).
		var original = Layout (new[] { MatroskaIds.Info, MatroskaIds.Tracks, MatroskaIds.Cues, MatroskaIds.Cluster },
			(seekHead, positions) => Concat (
				seekHead,
				Master (0x1549A966, Element (0x7BA9, "Pos"u8)),
				Master (0x1654AE6B, AudioEntry (1, 22050)),
				CueElement (positions[3]),
				Master (0x1F43B675, Concat (Element (0xE7, new byte[] { 0 }), Element (0xA7, new byte[] { 0, (byte)positions[3] }), Element (0xA3, ClusterMarker)))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		AssertMovedCorrectly (original, saved);
		Assert.IsTrue (TryReadCueClusterPosition (saved, out var cluster, out _));
		Assert.IsTrue (saved.AsSpan ().IndexOf (Element (0xA7, new byte[] { (byte)(cluster >> 8), (byte)cluster })) >= 0,
			$"Cluster Position should be {cluster}");
	}

	[TestMethod]
	public void Save_InsertIntoASecondTracksWithoutAVoid_Fails ()
	{
		// Only the first Tracks can move later elements; a second one is out of spec (maxOccurs 1).
		var first = Master (0x1654AE6B, AudioEntry (1, 22050));
		var second = Master (0x1654AE6B, AudioEntry (2, 44100));
		var original = TrackFile (first, second, Master (0x1F43B675, ClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[1].IsEnabled = false;

		AssertFailure (parsed, new MockFileSystem (), "/second.mkv", original, "no Void right after Tracks");
	}

	[TestMethod]
	public void Save_VoidThatWouldLeaveOneByte_IsNotUsed ()
	{
		// No Void is one octet long (RFC 8794 §11.3.2 needs an ID and a size), so the later elements move.
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 4);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 3, saved.Length);
		Assert.IsTrue (saved.AsSpan ().IndexOf (Element (0xEC, new byte[2])) >= 0);
		AssertMovedCorrectly (original, saved);
	}

	[TestMethod]
	public void Save_VoidOfFiveOctets_LeavesTwo ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 5);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.IsTrue (saved.AsSpan ().IndexOf (new byte[] { 0xB9, 0x81, 0x00, 0xEC, 0x80 }) >= 0);
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_VoidPastACluster_IsNotUsed ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Far"u8));
		var tracks = Master (0x1654AE6B, AudioEntry (1, 22050));
		var hole = Element (0xEC, new byte[18]);
		var original = Layout (new[] { MatroskaIds.Info, MatroskaIds.Tracks, MatroskaIds.Cues, MatroskaIds.Cluster },
			(seekHead, positions) => Concat (seekHead, info, tracks, Master (0x1F43B675, ClusterMarker), CueElement (positions[3]), hole));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 3, saved.Length);
		Assert.IsTrue (saved.AsSpan ().EndsWith (hole));
		AssertMovedCorrectly (original, saved);
	}

	[TestMethod]
	public void Save_UnknownSizeSegment_MovesTheLiveCluster ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		var tracks = Master (0x1654AE6B, AudioEntry (1, 22050));
		var block = Element (0xA3, ClusterMarker);
		var original = TrackMatroska (Unknown (0x18538067, Concat (info, tracks, Unknown (0x1F43B675, Concat (Element (0xE7, new byte[] { 0 }), block)))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length + 3, saved.Length);
		Assert.IsTrue (saved.AsSpan ().EndsWith (block));
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.AreEqual ("Live", again.Tag.Title);
	}

	[TestMethod]
	public void Save_ManyTracksElements_EachFindsItsOwnRoom ()
	{
		// Each Tracks takes the Void after it; the walk is one pass over the pieces.
		var children = new List<byte[]> ();
		for (var i = 1; i <= 200; i++) {
			children.Add (Master (0x1654AE6B, AudioEntry ((ulong)i, 22050)));
			children.Add (Element (0xEC, new byte[3]));
		}

		children.Add (Master (0x1F43B675, ClusterMarker));
		var original = TrackFile (children.ToArray ());
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual (200, parsed.Tracks.Count);
		foreach (var track in parsed.Tracks)
			track.IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.IsTrue (MatroskaFile.Read (saved).File!.Tracks.All (t => !t.IsEnabled));
	}

	[TestMethod]
	public void Save_EmptyFlag_GrowsByOneByte ()
	{
		var original = TracksFile (AudioEntry (1, 22050, Element (0xB9, Array.Empty<byte> ())), withVoid: 3);
		var parsed = MatroskaFile.Read (original).File!;
		Assert.IsTrue (parsed.Tracks[0].IsEnabled);
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.IsTrue (saved.AsSpan ().IndexOf (new byte[] { 0xB9, 0x81, 0x00 }) >= 0);
		Assert.IsTrue (saved.AsSpan ().IndexOf (new byte[] { 0xEC, 0x80, 0x12, 0x54, 0xC3, 0x67 }) >= 0, "a 2-octet Void is left before Tags");
		AssertSeeksLand (original, saved);
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_SizesThatOutgrowOneByte_AreWidened ()
	{
		// 124 payload bytes fit a one-byte size. Three more do not (RFC 8794 §4.4: 0x7F is reserved).
		var filler = 124 - AudioBody (1, 22050).Length - 3;
		var entry = AudioEntry (1, 22050, Element (0x63A2, new byte[filler]));
		Assert.AreEqual (2 + 124, entry.Length);
		var original = TracksFile (entry, withVoid: 7);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.IsTrue (saved.AsSpan ().IndexOf (new byte[] { 0xB9, 0x81, 0x00, 0xEC, 0x80 }) >= 0);
		AssertSeeksLand (original, saved);
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.AreEqual (22050, ((IMediaFile)again).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Save_TrackEntryAndTracksCrc_AreRecomputed ()
	{
		var entry = WithCrc (0xAE, AudioBody (1, 22050));
		var other = WithCrc (0xAE, AudioBody (2, 44100, Enabled (1)));
		var tracks = WithCrc (MatroskaIds.Tracks, Concat (entry, other));
		var original = TracksFileWith (tracks, withVoid: 10);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;
		parsed.Tracks[1].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.IsTrue (EveryCrcMatches (saved));
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.IsFalse (again.Tracks[1].IsEnabled);
	}

	[TestMethod]
	public void Save_TrackFlagAndTags_AreBothWritten ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050), VideoEntry (2, 640)), withVoid: 12);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[1].IsEnabled = false;
		parsed.Tag.Title = "Renamed";

		var saved = Save (parsed, original);

		AssertSeeksLand (original, saved);
		Assert.IsTrue (EveryCrcMatches (saved));
		var again = MatroskaFile.Read (saved).File!;
		Assert.AreEqual ("Renamed", again.Tag.Title);
		Assert.IsTrue (again.Tracks[0].IsEnabled);
		Assert.IsFalse (again.Tracks[1].IsEnabled);
		Assert.AreEqual (1, CountOf (saved, ClusterMarker));
	}

	[TestMethod]
	public void Save_InsertedFlag_RaisesDocTypeVersionOne ()
	{
		// FlagEnabled has minver 2. RFC 9559 §5.1.4.1.4, RFC 8794 §11.2.7.
		var header = WithCrc (0x1A45DFA3, Concat (
			Element (0x4282, "matroska"u8),
			Element (0x4287, new byte[] { 1 }),
			Element (0x4285, new byte[] { 1 })));
		var segment = Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050)), MakeVoid (3), Master (0x1F43B675, ClusterMarker));
		var original = Concat (header, segment);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.IsTrue (saved.AsSpan ().IndexOf (Element (0x4287, new byte[] { 2 })) >= 0);
		Assert.IsTrue (saved.AsSpan ().IndexOf (Element (0x4285, new byte[] { 1 })) >= 0);
		Assert.IsTrue (CrcMatches (saved.AsSpan (5, header.Length - 5)));
		Assert.IsFalse (MatroskaFile.Read (saved).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_InPlaceFlag_LeavesDocTypeVersion ()
	{
		var header = Master (0x1A45DFA3, Concat (Element (0x4282, "matroska"u8), Element (0x4287, new byte[] { 1 })));
		var segment = Master (0x18538067, Master (0x1654AE6B, AudioEntry (1, 22050, Enabled (1))));
		var original = Concat (header, segment);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (1, DifferingBytes (original, saved));
	}

	[TestMethod]
	public void Save_UnknownSizeSegment_KeepsTheLiveClusterInPlace ()
	{
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		var tracks = Master (0x1654AE6B, AudioEntry (1, 22050));
		// An unknown-size cluster ends where its children stop parsing, so the marker sits in a SimpleBlock.
		var block = Element (0xA3, ClusterMarker);
		var original = TrackMatroska (Unknown (0x18538067, Concat (info, tracks, MakeVoid (6), Unknown (0x1F43B675, Concat (Element (0xE7, new byte[] { 0 }), block)))));
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		var saved = Save (parsed, original);

		Assert.AreEqual (original.Length, saved.Length);
		Assert.AreEqual (IndexOf (original, block), IndexOf (saved, block));
		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.AreEqual ("Live", again.Tag.Title);
	}

	[TestMethod]
	public void Save_SecondTracksElement_KeepsTheOrdinals ()
	{
		var first = Master (0x1654AE6B, AudioEntry (1, 22050));
		var second = Master (0x1654AE6B, Concat (AudioEntry (2, 44100), VideoEntry (3, 640)));
		var original = TrackFile (first, MakeVoid (8), second, MakeVoid (3), Master (0x1F43B675, ClusterMarker));
		var parsed = MatroskaFile.Read (original).File!;
		Assert.AreEqual (3, parsed.Tracks.Count);
		parsed.Tracks[0].IsEnabled = false;
		parsed.Tracks[2].IsEnabled = false;

		var saved = Save (parsed, original);

		var again = MatroskaFile.Read (saved).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.IsTrue (again.Tracks[1].IsEnabled);
		Assert.IsFalse (again.Tracks[2].IsEnabled);
		Assert.AreEqual (1, CountOf (saved, ClusterMarker));
	}

	[TestMethod]
	public void Save_ChangedBackAfterASave_IsWrittenAgain ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 10);
		var fs = new MockFileSystem ();
		fs.AddFile ("/flag.mkv", original);
		var parsed = MatroskaFile.ReadFromFile ("/flag.mkv", fs).File!;

		parsed.Tracks[0].IsEnabled = false;
		Assert.IsTrue (parsed.SaveToFile ().IsSuccess);
		var disabled = fs.ReadAllBytes ("/flag.mkv");
		Assert.IsTrue (parsed.SaveToFile ().IsSuccess);
		Assert.IsTrue (fs.ReadAllBytes ("/flag.mkv").AsSpan ().SequenceEqual (disabled));
		Assert.IsFalse (MatroskaFile.Read (disabled).File!.Tracks[0].IsEnabled);

		parsed.Tracks[0].IsEnabled = true;
		Assert.IsTrue (parsed.SaveToFile ().IsSuccess);
		Assert.IsTrue (MatroskaFile.Read (fs.ReadAllBytes ("/flag.mkv")).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public async Task SaveAsync_WritesTheFlag ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 10);
		var fs = new MockFileSystem ();
		fs.AddFile ("/async-flag.mkv", original);
		var opened = await MatroskaFile.ReadFromFileAsync ("/async-flag.mkv", fs);
		opened.File!.Tracks[0].IsEnabled = false;

		var saved = await opened.File.SaveToFileAsync ();

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.IsFalse (MatroskaFile.Read (fs.ReadAllBytes ("/async-flag.mkv")).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public async Task SaveAsync_WithData_WritesTheFlag ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 3);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;
		var fs = new MockFileSystem ();

		var saved = await parsed.SaveToFileAsync ("/async-data.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.IsFalse (MatroskaFile.Read (fs.ReadAllBytes ("/async-data.mkv")).File!.Tracks[0].IsEnabled);
	}

	[TestMethod]
	public void Save_DifferentTrackInTheData_Fails ()
	{
		var original = TracksFile (AudioEntry (1, 22050), withVoid: 0);
		var other = TracksFile (AudioEntry (5, 22050), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		AssertFailure (parsed, new MockFileSystem (), "/other.mkv", other, "Matroska track");
		Assert.IsTrue (parsed.Render (other).IsEmpty);
	}

	[TestMethod]
	public void Save_MissingTrackInTheData_Fails ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050), AudioEntry (2, 44100)), withVoid: 0);
		var shorter = TracksFile (AudioEntry (1, 22050), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[1].IsEnabled = false;

		AssertFailure (parsed, new MockFileSystem (), "/shorter.mkv", shorter, "Matroska track");
	}

	[TestMethod]
	public void Save_OversizedFlag_Fails ()
	{
		var original = TracksFile (AudioEntry (1, 22050, Element (0xB9, new byte[9])), withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = !parsed.Tracks[0].IsEnabled;

		AssertFailure (parsed, new MockFileSystem (), "/wide.mkv", original, "FlagEnabled");
	}

	[TestMethod]
	public void Save_TrackEntryThatDoesNotParse_Fails ()
	{
		var broken = Master (0xAE, Concat (AudioBody (1, 22050), new byte[] { 0x00, 0x00 }));
		var original = TracksFile (broken, withVoid: 0);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tracks[0].IsEnabled = false;

		AssertFailure (parsed, new MockFileSystem (), "/broken.mkv", original, "TrackEntry");
	}

	static int DifferingBytes (byte[] a, byte[] b)
	{
		Assert.AreEqual (a.Length, b.Length);
		var count = 0;
		for (var i = 0; i < a.Length; i++) {
			if (a[i] != b[i])
				count++;
		}

		return count;
	}

	static void AssertMovedCorrectly (byte[] original, byte[] file)
	{
		AssertSeeksLand (original, file);
		// A Cues CRC-32, when present, is checked by EveryCrcMatches in AssertSeeksLand.
		if (TryReadCueClusterPosition (file, out var cue, out _))
			Assert.IsTrue (IsElementAt (file, cue, MatroskaIds.Cluster), $"CueClusterPosition {cue}");

		Assert.IsFalse (MatroskaFile.Read (file).File!.Tracks[0].IsEnabled);
	}

	/// <summary>
	/// Every Seek in <paramref name="file"/> lands, and every Seek that landed in
	/// <paramref name="original"/> is still listed (RFC 9559 section 6.3).
	/// </summary>
	static void AssertSeeksLand (byte[] original, byte[] file)
	{
		var seeks = ReadSeeks (file);
		Assert.IsTrue (seeks.Count > 0);
		foreach (var seek in seeks)
			Assert.IsTrue (IsElementAt (file, seek.Position, seek.Id), $"Seek for 0x{seek.Id:X} at {seek.Position}");
		AssertSeeksPreserved (original, file);
		Assert.IsTrue (EveryCrcMatches (file), "every CRC-32 that is present matches");
	}
}
