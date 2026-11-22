// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Integration tests that require a real Matroska or WebM file: tracks and picture saves.
/// Picture saves work on a temporary copy; the original is never written.
/// </summary>
/// <remarks>
/// These tests are skipped by default. To run them:
/// 1. Set the TAGLIBSHARP2_TEST_MATROSKA environment variable to a path to a .mkv, .mka, or .webm file
/// 2. Run with: dotnet test --filter "TestCategory=Integration"
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Matroska")]
public class MatroskaIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MATROSKA";

	[TestMethod]
	public void Read_RealMatroska_ListsTracks ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var file = Open (File.ReadAllBytes (path));

		foreach (var track in file.Tracks)
			Console.WriteLine ($"  #{track.TrackNumber} {track.TrackType} {track.CodecId} default={track.IsDefault} enabled={track.IsEnabled}");

		Assert.IsTrue (file.Tracks.Count > 0);
		Assert.AreEqual ((file.MediaTypes & MediaTypes.Audio) != 0, file.Tracks.Any (t => t.TrackType == MatroskaTrackType.Audio));
		Assert.AreEqual ((file.MediaTypes & MediaTypes.Video) != 0, file.Tracks.Any (t => t.TrackType == MatroskaTrackType.Video));
	}

	[TestMethod]
	public void Save_RealMatroska_UntouchedTracks_KeepTheirBytes ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var file = Open (original);
		foreach (var track in file.Tracks)
			track.IsEnabled = track.IsEnabled;

		var saved = file.Render (original).ToArray ();

		Assert.IsTrue (saved.Length > 0);
		CollectionAssert.AreEqual (Children (original, MatroskaIds.Tracks), Children (saved, MatroskaIds.Tracks));
		CollectionAssert.AreEqual (Children (original, MatroskaIds.Cluster), Children (saved, MatroskaIds.Cluster));
	}

	[TestMethod]
	public void Save_RealMatroska_ToggledTrack_RoundTripsAndKeepsClusters ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var file = Open (original);
		var index = IndexOfFirst (file, MatroskaTrackType.Audio);
		var before = file.Tracks[index].IsEnabled;
		file.Tracks[index].IsEnabled = !before;

		var fs = new MockFileSystem ();
		var saved = file.SaveToFile ("/toggled.mkv", original, fs);
		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var toggled = fs.ReadAllBytes ("/toggled.mkv");
		var again = Open (toggled);
		Assert.AreEqual (!before, again.Tracks[index].IsEnabled);
		for (var i = 0; i < file.Tracks.Count; i++) {
			Assert.AreEqual (file.Tracks[i].TrackNumber, again.Tracks[i].TrackNumber);
			if (i != index)
				Assert.AreEqual (file.Tracks[i].IsEnabled, again.Tracks[i].IsEnabled);
		}

		// Blocks are byte-identical; only a moved cluster's Position value differs.
		CollectionAssert.AreEqual (Children (original, MatroskaIds.Cluster), Children (toggled, MatroskaIds.Cluster));
		// Every Seek that named an element still names one with its ID (RFC 9559 section 6.3).
		CollectionAssert.AreEquivalent (LandingSeeks (original), LandingSeeks (toggled));

		again.Tracks[index].IsEnabled = before;
		var restored = again.Render (toggled).ToArray ();
		Assert.AreEqual (before, Open (restored).Tracks[index].IsEnabled);
		Assert.AreEqual (toggled.Length, restored.Length, "a stored flag is rewritten in place");
	}

	/// <summary>The ID of every Seek, in every SeekHead, whose position names an element with that ID.</summary>
	static List<ulong> LandingSeeks (byte[] file)
	{
		var found = new List<ulong> ();
		var offset = 0;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id != MatroskaIds.Segment) {
				if (element.UnknownSize)
					break;
				offset = element.End;
				continue;
			}

			var end = MatroskaFile.PayloadEnd (file, element);
			var bodyAt = element.DataOffset;
			var at = bodyAt;
			while (at < end) {
				if (!EbmlReader.TryRead (file, at, end, out var child)) {
					at++;
					continue;
				}

				if (child.UnknownSize)
					break;
				if (child.Id == MatroskaIds.SeekHead) {
					var seek = child.DataOffset;
					while (seek < child.End && EbmlReader.TryRead (file, seek, child.End, out var entry) && !entry.UnknownSize) {
						if (entry.Id == 0x4DBB && TrySeek (file, entry, out var id, out var position)) {
							var target = bodyAt + (long)position;
							if (target < end && EbmlReader.TryRead (file, (int)target, end, out var named) && named.Id == id)
								found.Add (id);
						}

						seek = entry.End;
					}
				}

				at = child.End;
			}

			break;
		}

		return found;
	}

	static bool TrySeek (byte[] file, EbmlElement seek, out ulong id, out ulong position)
	{
		id = 0;
		position = 0;
		var sawId = false;
		var sawPosition = false;
		var at = seek.DataOffset;
		while (at < seek.End && EbmlReader.TryRead (file, at, seek.End, out var child) && !child.UnknownSize) {
			var payload = file.AsSpan (child.DataOffset, (int)child.DataLength);
			if (child.Id == 0x53AB) {
				foreach (var b in payload)
					id = (id << 8) | b;
				sawId = true;
			} else if (child.Id == 0x53AC) {
				position = EbmlReader.ReadUInt (payload);
				sawPosition = true;
			}

			at = child.End;
		}

		return sawId && sawPosition;
	}

	static MatroskaFile Open (byte[] data)
	{
		var result = MatroskaFile.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}

	static int IndexOfFirst (MatroskaFile file, MatroskaTrackType type)
	{
		for (var i = 0; i < file.Tracks.Count; i++) {
			if (file.Tracks[i].TrackType == type)
				return i;
		}

		return 0;
	}

	/// <summary>
	/// A cluster payload with its <c>Position</c> value zeroed. Position is the cluster's own segment
	/// position (RFC 9559 §5.1.3.2), so it changes whenever the cluster moves; the blocks do not.
	/// </summary>
	static byte[] WithoutPosition (ReadOnlySpan<byte> payload, ulong id)
	{
		var copy = payload.ToArray ();
		if (id != MatroskaIds.Cluster)
			return copy;
		var offset = 0;
		while (offset < copy.Length && EbmlReader.TryRead (copy, offset, copy.Length, out var child) && !child.UnknownSize) {
			if (child.Id == 0xA7)
				copy.AsSpan (child.DataOffset, (int)child.DataLength).Clear ();
			offset = child.End;
		}

		return copy;
	}

	/// <summary>A SHA-256 of the payload of every first-Segment child with <paramref name="id"/>, in file order.</summary>
	static List<string> Children (byte[] file, ulong id)
	{
		var found = new List<string> ();
		var offset = 0;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id == MatroskaIds.Segment) {
				var end = MatroskaFile.PayloadEnd (file, element);
				var at = element.DataOffset;
				while (at < end) {
					if (!EbmlReader.TryRead (file, at, end, out var child)) {
						at++;
						continue;
					}

					var childEnd = child.UnknownSize ? end : child.End;
					if (child.Id == id)
						found.Add (Convert.ToHexString (System.Security.Cryptography.SHA256.HashData (WithoutPosition (file.AsSpan (child.DataOffset, childEnd - child.DataOffset), id))));
					if (child.UnknownSize)
						break;
					at = child.End;
				}

				break;
			}

			if (element.UnknownSize)
				break;
			offset = element.End;
		}

		return found;
	}

	static readonly byte[] BackCover = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

	[TestMethod]
	public void Save_RealMatroska_UntouchedPictures_KeepTheAttachmentsBytes ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var before = TopLevel (original, MatroskaIds.Attachments);

		var copy = Copy (path);
		try {
			var file = MatroskaFile.ReadFromFile (copy).File!;
			file.Tag.Title = "Integration title";
			file.Tag.Pictures = file.Tag.Pictures;
			var saved = file.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var after = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (before, TopLevel (after, MatroskaIds.Attachments));
			CollectionAssert.AreEqual (ClusterPayloads (original), ClusterPayloads (after));
			Assert.AreEqual ("Integration title", MatroskaFile.ReadFromFile (copy).File!.Tag.Title);
		} finally {
			File.Delete (copy);
		}
	}

	[TestMethod]
	public void Save_RealMatroska_AddingThenRemovingABackCover_KeepsTheClustersAndOtherAttachments ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var clusters = ClusterPayloads (original);
		var originalFiles = AttachedFiles (original);

		var copy = Copy (path);
		try {
			var file = MatroskaFile.ReadFromFile (copy).File!;
			var pictures = file.Tag.Pictures;
			file.Tag.Pictures = [.. pictures, new BackPicture ()];
			var saved = file.SaveToFile ();
			// Documented refusal: WebM has no attachments. A full SeekHead no longer refuses: the
			// attachments move after it, and the clusters move whole.
			if (!saved.IsSuccess && saved.Error!.Contains ("WebM", StringComparison.Ordinal)) {
				CollectionAssert.AreEqual (original, File.ReadAllBytes (copy));
				Assert.Inconclusive ("This file refuses a new attachment: " + saved.Error);
			}
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var added = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (clusters, ClusterPayloads (added));
			foreach (var kept in originalFiles)
				Assert.IsTrue (added.AsSpan ().IndexOf (kept) >= 0, "An original attachment changed");
			var withBack = MatroskaFile.Read (added).File!.Tag.Pictures;
			Assert.AreEqual (pictures.Length + 1, withBack.Length);
			Assert.AreEqual (PictureType.BackCover, withBack[^1].PictureType);
			CollectionAssert.AreEqual (BackCover, withBack[^1].PictureData.ToArray ());

			var reread = MatroskaFile.ReadFromFile (copy).File!;
			reread.Tag.Pictures = [.. reread.Tag.Pictures.Where (p => p.PictureType != PictureType.BackCover || !p.PictureData.Span.SequenceEqual (BackCover))];
			saved = reread.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var removed = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (clusters, ClusterPayloads (removed));
			var final = AttachedFiles (removed);
			Assert.AreEqual (originalFiles.Count, final.Count);
			for (var i = 0; i < final.Count; i++)
				CollectionAssert.AreEqual (originalFiles[i], final[i]);
		} finally {
			File.Delete (copy);
		}
	}

	[TestMethod]
	public void Save_RealMatroska_CoverAndTrackFlag_AreBothWritten ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var file = Open (original);
		Assert.IsTrue (file.Tracks.Count > 0);
		var number = file.Tracks[^1].TrackNumber;
		file.Tracks[^1].IsEnabled = !file.Tracks[^1].IsEnabled;
		file.Tag.Pictures = [.. file.Tag.Pictures, new BackPicture ()];

		var copy = Copy (path);
		try {
			var saved = file.SaveToFile (copy, original);
			// Documented refusals: WebM, a SeekHead without room, a FlagEnabled that an unknown-size or second Tracks has no room for.
			if (!saved.IsSuccess && (saved.Error!.Contains ("SeekHead", StringComparison.Ordinal)
				|| saved.Error.Contains ("WebM", StringComparison.Ordinal)
				|| saved.Error.Contains ("FlagEnabled", StringComparison.Ordinal))) {
				CollectionAssert.AreEqual (original, File.ReadAllBytes (copy));
				Assert.Inconclusive ("This file refuses the combined save: " + saved.Error);
			}

			Assert.IsTrue (saved.IsSuccess, saved.Error);
			var bytes = File.ReadAllBytes (copy);
			var again = Open (bytes);
			Assert.AreEqual (!Open (original).Tracks[^1].IsEnabled, again.Tracks.Single (t => t.TrackNumber == number).IsEnabled);
			Assert.IsTrue (again.Tag.Pictures.Any (p => p.PictureType == PictureType.BackCover && p.PictureData.Span.SequenceEqual (BackCover)));
			// A missing flag can move the Clusters: their blocks stay byte-identical, while Position and
			// CRC-32 follow them. Every Seek and CueClusterPosition that named an element still does.
			CollectionAssert.AreEqual (ClusterPayloads (original), ClusterPayloads (bytes));
			CollectionAssert.IsSubsetOf (LandingSeeks (original), LandingSeeks (bytes));
			Assert.AreEqual (LandingCues (original), LandingCues (bytes));
		} finally {
			File.Delete (copy);
		}
	}

	[TestMethod]
	public void Save_RealMatroska_ArtistEdit_KeepsTheTagsListed ()
	{
		// RFC 9559 section 6.1: after an edit the SeekHead still lists every Top-Level Element.
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var copy = Copy (path);
		try {
			var file = MatroskaFile.ReadFromFile (copy).File!;
			var title = file.Tag.Title;
			file.Tag.Artist = "Integration artist " + new string ('x', 40);
			var saved = file.SaveToFile ();
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var after = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (ClusterPayloads (original), ClusterPayloads (after));
			var seeks = FirstSeekHead (after);
			if (FirstSeekHead (original).Count > 0)
				Assert.IsTrue (seeks.Exists (entry => entry.Id == MatroskaIds.Tags), "The SeekHead does not list the Tags");
			var children = SegmentChildren (after);
			var bodyAt = SegmentBodyAt (after);
			foreach (var (id, position) in seeks)
				Assert.IsTrue (children.Exists (child => child.Id == id && child.Start - bodyAt == (int)position), id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
			var again = MatroskaFile.ReadFromFile (copy).File!;
			Assert.AreEqual (title, again.Tag.Title);
			Assert.AreEqual ("Integration artist " + new string ('x', 40), again.Tag.Artist);
		} finally {
			File.Delete (copy);
		}
	}

	[TestMethod]
	public void Save_RealMatroska_LargeCover_IsListedAndEveryOffsetLands ()
	{
		// A 70 KB cover is common album art. On a file whose CueClusterPosition has two bytes the move
		// pushes the Clusters past 0xFFFF, so the Cues must widen (RFC 9559 sections 6.1 and 16).
		var path = SkipIfNoTestFile (TestFileEnvVar, "Matroska");
		var original = File.ReadAllBytes (path);
		var copy = Copy (path);
		try {
			var file = MatroskaFile.ReadFromFile (copy).File!;
			var cover = new byte[70000];
			cover[0] = 0xFF;
			cover[1] = 0xD8;
			file.Tag.Pictures = [.. file.Tag.Pictures, new LargePicture (cover)];
			var saved = file.SaveToFile ();
			if (!saved.IsSuccess && saved.Error!.Contains ("WebM", StringComparison.Ordinal))
				Assert.Inconclusive ("This file refuses a new attachment: " + saved.Error);
			Assert.IsTrue (saved.IsSuccess, saved.Error);

			var after = File.ReadAllBytes (copy);
			CollectionAssert.AreEqual (ClusterPayloads (original), ClusterPayloads (after));
			var seeks = FirstSeekHead (after);
			if (FirstSeekHead (original).Count > 0)
				Assert.IsTrue (seeks.Exists (entry => entry.Id == MatroskaIds.Attachments), "The SeekHead does not list the Attachments");
			var children = SegmentChildren (after);
			var bodyAt = SegmentBodyAt (after);
			foreach (var (id, position) in seeks)
				Assert.IsTrue (children.Exists (child => child.Id == id && child.Start - bodyAt == (int)position), id.ToString ("X", System.Globalization.CultureInfo.InvariantCulture));
			// Every CueClusterPosition that named a Cluster still does. A broken one in the source stays as it was.
			Assert.AreEqual (LandingCues (original), LandingCues (after));
			// A Position left in place names its own Cluster; one that could not widen was voided.
			foreach (var (id, start, end) in children) {
				if (id != MatroskaIds.Cluster || !EbmlReader.TryRead (after, start, end, out var cluster))
					continue;
				var at = cluster.DataOffset;
				while (at < end && EbmlReader.TryRead (after, at, end, out var child) && !child.UnknownSize) {
					if (child.Id == MatroskaIds.Position)
						Assert.AreEqual ((ulong)(start - bodyAt), EbmlReader.ReadUInt (after.AsSpan (child.DataOffset, (int)child.DataLength)), "Cluster Position");
					at = child.End;
				}
			}

			Assert.IsTrue (MatroskaFile.Read (after).File!.Tag.Pictures.Any (p => p.PictureData.Length == cover.Length));
		} finally {
			File.Delete (copy);
		}
	}

	static int LandingCues (byte[] file)
	{
		var bodyAt = SegmentBodyAt (file);
		var children = SegmentChildren (file);
		return CueClusterPositions (file).Count (position => children.Exists (child => child.Id == MatroskaIds.Cluster && child.Start - bodyAt == (int)position));
	}

	/// <summary>Every CueClusterPosition in the first Segment's Cues.</summary>
	static List<ulong> CueClusterPositions (byte[] file)
	{
		var found = new List<ulong> ();
		foreach (var (id, start, end) in SegmentChildren (file)) {
			if (id == MatroskaIds.Cues && EbmlReader.TryRead (file, start, end, out var cues) && !cues.UnknownSize)
				CollectCues (file, MatroskaIds.Cues, cues.DataOffset, cues.End, found);
		}

		return found;
	}

	/// <summary>
	/// Reads CueClusterPosition on its RFC 9559 section 5.1.5 path only: Cues, CuePoint,
	/// CueTrackPositions. The same ID anywhere else in the Cues is not a position.
	/// </summary>
	static void CollectCues (byte[] file, ulong parent, int offset, int end, List<ulong> found)
	{
		var child = parent switch {
			MatroskaIds.Cues => MatroskaIds.CuePoint,
			MatroskaIds.CuePoint => MatroskaIds.CueTrackPositions,
			_ => ulong.MaxValue
		};
		while (offset < end && EbmlReader.TryRead (file, offset, end, out var element) && !element.UnknownSize) {
			if (parent == MatroskaIds.CueTrackPositions && element.Id == MatroskaIds.CueClusterPosition)
				found.Add (EbmlReader.ReadUInt (file.AsSpan (element.DataOffset, (int)element.DataLength)));
			else if (element.Id == child)
				CollectCues (file, element.Id, element.DataOffset, element.End, found);
			offset = element.End;
		}
	}

	sealed class LargePicture (byte[] data) : IPicture
	{
		public string MimeType => "image/jpeg";

		public PictureType PictureType => PictureType.FrontCover;

		public string Description => "";

		public BinaryData PictureData => new (data);
	}

	/// <summary>The Seek entries of the first SeekHead, positions relative to the Segment payload.</summary>
	static List<(ulong Id, ulong Position)> FirstSeekHead (byte[] file)
	{
		var seeks = new List<(ulong, ulong)> ();
		foreach (var (id, start, end) in SegmentChildren (file)) {
			if (id != MatroskaIds.SeekHead)
				continue;
			EbmlReader.TryRead (file, start, end, out var head);
			var at = head.DataOffset;
			while (at < end && EbmlReader.TryRead (file, at, end, out var seek) && !seek.UnknownSize) {
				ulong seekId = 0;
				ulong position = 0;
				var inner = seek.DataOffset;
				while (seek.Id == MatroskaIds.Seek && inner < seek.End && EbmlReader.TryRead (file, inner, seek.End, out var child) && !child.UnknownSize) {
					if (child.Id == MatroskaIds.SeekId)
						seekId = EbmlReader.ReadUInt (file.AsSpan (child.DataOffset, (int)child.DataLength));
					else if (child.Id == MatroskaIds.SeekPosition)
						position = EbmlReader.ReadUInt (file.AsSpan (child.DataOffset, (int)child.DataLength));
					inner = child.End;
				}

				if (seekId != 0)
					seeks.Add ((seekId, position));
				at = seek.End;
			}

			break;
		}

		return seeks;
	}

	static int SegmentBodyAt (byte[] file)
	{
		Assert.IsTrue (EbmlReader.TryRead (file, 0, file.Length, out var header));
		var offset = header.End;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id == MatroskaIds.Segment)
				return element.DataOffset;
			offset = element.End;
		}

		return -1;
	}

	static string Copy (string path)
	{
		var copy = Path.Combine (Path.GetTempPath (), "taglibsharp2-" + Guid.NewGuid ().ToString ("N") + Path.GetExtension (path));
		File.Copy (path, copy);
		return copy;
	}

	/// <summary>
	/// Every Cluster's children except <c>Position</c> and CRC-32, concatenated. A move rewrites those
	/// two (RFC 9559 section 5.1.3.2); every block stays byte for byte.
	/// </summary>
	static byte[] ClusterPayloads (byte[] file)
	{
		using var kept = new MemoryStream ();
		foreach (var (childId, start, end) in SegmentChildren (file)) {
			if (childId != MatroskaIds.Cluster || !EbmlReader.TryRead (file, start, end, out var cluster))
				continue;
			var offset = cluster.DataOffset;
			while (offset < end && EbmlReader.TryRead (file, offset, end, out var child) && !child.UnknownSize) {
				// Position moves with the Cluster and is voided when it cannot widen in place; the blocks do not change.
				if (child.Id is not MatroskaIds.Position and not MatroskaIds.Crc32 and not MatroskaIds.Void)
					kept.Write (file, child.Position, child.End - child.Position);
				offset = child.End;
			}
		}

		return kept.ToArray ();
	}

	/// <summary>Every segment child with <paramref name="id"/>, concatenated.</summary>
	static byte[] TopLevel (byte[] file, ulong id)
	{
		using var kept = new MemoryStream ();
		foreach (var (childId, start, end) in SegmentChildren (file)) {
			if (childId == id)
				kept.Write (file, start, end - start);
		}

		return kept.ToArray ();
	}

	static List<byte[]> AttachedFiles (byte[] file)
	{
		var found = new List<byte[]> ();
		foreach (var (id, start, end) in SegmentChildren (file)) {
			if (id != MatroskaIds.Attachments)
				continue;
			Assert.IsTrue (EbmlReader.TryRead (file, start, end, out var attachments));
			var offset = attachments.DataOffset;
			while (offset < end && EbmlReader.TryRead (file, offset, end, out var child) && !child.UnknownSize) {
				if (child.Id == MatroskaIds.AttachedFile)
					found.Add (file.AsSpan (child.Position, child.End - child.Position).ToArray ());
				offset = child.End;
			}
		}

		return found;
	}

	static List<(ulong Id, int Start, int End)> SegmentChildren (byte[] file)
	{
		var children = new List<(ulong, int, int)> ();
		Assert.IsTrue (EbmlReader.TryRead (file, 0, file.Length, out var header));
		var offset = header.End;
		while (offset < file.Length && EbmlReader.TryRead (file, offset, file.Length, out var element)) {
			if (element.Id != MatroskaIds.Segment) {
				offset = element.End;
				continue;
			}

			var end = element.UnknownSize ? file.Length : element.End;
			var at = element.DataOffset;
			while (at < end && EbmlReader.TryRead (file, at, end, out var child) && !child.UnknownSize) {
				children.Add ((child.Id, child.Position, child.End));
				at = child.End;
			}

			break;
		}

		return children;
	}

	sealed class BackPicture : IPicture
	{
		public string MimeType => "image/png";

		public PictureType PictureType => PictureType.BackCover;

		public string Description => "Integration back cover";

		public BinaryData PictureData { get; } = new (BackCover);
	}
}
