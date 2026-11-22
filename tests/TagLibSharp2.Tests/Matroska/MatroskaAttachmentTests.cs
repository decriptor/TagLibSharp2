// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// Writing pictures as attachments. RFC 9559 sections 5.1.6, 6.7 and 21.1.
/// Invariant: every attachment the caller did not change keeps its bytes, and the pictures read
/// back from a saved file are the pictures that were set.
/// </summary>
public partial class MatroskaFileTests
{
	static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0x33 };
	static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x44, 0x55 };
	static readonly byte[] FontBytes = { 0x00, 0x01, 0x00, 0x00, 0x7F, 0x7E };

	[TestMethod]
	public void Save_FrontCover_IsWrittenAsCoverJpg_AfterTheLastCluster ()
	{
		var cluster = Master (MatroskaIds.Cluster, new byte[] { 0xAA, 0xBB, 0xCC });
		var original = WithSeekHeadRoom (Info (), cluster);
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual (1, again.Tag.Pictures.Length);
		Assert.AreEqual (PictureType.FrontCover, again.Tag.Pictures[0].PictureType);
		Assert.AreEqual ("image/jpeg", again.Tag.Pictures[0].MimeType);
		CollectionAssert.AreEqual (Jpeg, again.Tag.Pictures[0].PictureData.ToArray ());

		var files = AttachedFiles (bytes);
		Assert.AreEqual (1, files.Count);
		Assert.AreEqual ("cover.jpg", files[0].Name);
		Assert.IsNull (files[0].Description);
		Assert.IsNotNull (files[0].Uid);
		Assert.AreNotEqual (0UL, files[0].Uid);

		// RFC 9559 section 6.7: an edited Attachments element SHOULD follow the last Cluster.
		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		var clusterAt = body.AsSpan ().IndexOf (cluster);
		Assert.IsTrue (clusterAt > 0);
		Assert.AreEqual (clusterAt, TrySegmentBody (original, out var before, out _) ? before.AsSpan ().IndexOf (cluster) : -1);
		Assert.IsTrue (OffsetOfChild (bytes, MatroskaIds.Attachments) > clusterAt);

		// RFC 9559 section 6.3: the SeekHead references every Top-Level Element.
		var seek = ReadSeeks (bytes).Single (s => s.Id == MatroskaIds.Attachments);
		Assert.IsTrue (IsElementAt (bytes, seek.Position, MatroskaIds.Attachments));
	}

	[TestMethod]
	public void Save_PngFrontCover_IsCoverPng ()
	{
		var original = WithSeekHeadRoom (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/png", PictureType.FrontCover, "Front", Png)];

		var files = AttachedFiles (Save (file, original));

		Assert.AreEqual ("cover.png", files[0].Name);
		Assert.AreEqual ("Front", files[0].Description);
		Assert.AreEqual ("image/png", files[0].MediaType);
	}

	[TestMethod]
	public void Save_WithoutTouchingPictures_KeepsTheAttachmentsBytes ()
	{
		var attachments = Attachments (CoverPng (5), Font (6));
		var original = Movie (Tags (Tag (50, Simple ("TITLE", "Film"))), attachments, Master (MatroskaIds.Cluster, new byte[] { 4 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Artist = "Someone";

		var bytes = Save (file, original);

		Assert.AreEqual (1, CountOf (bytes, attachments));
		Assert.AreEqual ("Someone", MatroskaFile.Read (bytes).File!.Tag.Artist);
	}

	[TestMethod]
	public void Save_SameOrCopiedOrReorderedPictures_KeepTheAttachmentsBytes ()
	{
		var attachments = Attachments (CoverPng (5), Font (6), Picture ("back.jpg", "image/jpeg", null, Jpeg, 7));
		var original = Movie (attachments, Master (MatroskaIds.Cluster, new byte[] { 4 }));

		var same = MatroskaFile.Read (original).File!;
		same.Tag.Pictures = same.Tag.Pictures;
		CollectionAssert.AreEqual (original, Save (same, original));

		var copied = MatroskaFile.Read (original).File!;
		copied.Tag.Pictures = copied.Tag.Pictures.Select (p => (IPicture)new TestPicture (p.MimeType, p.PictureType, p.Description, p.PictureData.ToArray ())).ToArray ();
		CollectionAssert.AreEqual (original, Save (copied, original));

		// Storage order is kept: a reorder alone is not an edit.
		var reordered = MatroskaFile.Read (original).File!;
		reordered.Tag.Pictures = Enumerable.Reverse (reordered.Tag.Pictures).ToArray ();
		CollectionAssert.AreEqual (original, Save (reordered, original));
	}

	[TestMethod]
	public void Save_AddingABackCover_KeepsTheCoverAndTheFont ()
	{
		var cover = CoverPng (5);
		var font = Font (6);
		var original = Movie (Attachments (cover, font), Master (MatroskaIds.Cluster, new byte[] { 4 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [.. file.Tag.Pictures, new TestPicture ("image/jpeg", PictureType.BackCover, "Back side", Jpeg)];

		var bytes = Save (file, original);

		var files = AttachedFiles (bytes);
		Assert.AreEqual (3, files.Count);
		CollectionAssert.AreEqual (cover, files[0].Raw);
		CollectionAssert.AreEqual (font, files[1].Raw);
		Assert.AreEqual ("back.jpg", files[2].Name);
		Assert.AreEqual ("Back side", files[2].Description);
		Assert.IsTrue (files[2].Uid is not (null or 0 or 5 or 6));

		var pictures = MatroskaFile.Read (bytes).File!.Tag.Pictures;
		Assert.AreEqual (2, pictures.Length);
		Assert.AreEqual (PictureType.FrontCover, pictures[0].PictureType);
		Assert.AreEqual (PictureType.BackCover, pictures[1].PictureType);
		Assert.AreEqual ("Back side", pictures[1].Description);
	}

	[TestMethod]
	public void Save_ReplacingTheFrontCover_RemovesOnlyThatImage_AndPutsTheNewCoverFirst ()
	{
		var font = Font (6);
		var original = Movie (Attachments (font, CoverPng (5)), Master (MatroskaIds.Cluster, new byte[] { 4 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var files = AttachedFiles (bytes);
		Assert.AreEqual (2, files.Count);
		Assert.AreEqual ("cover.jpg", files[0].Name);
		CollectionAssert.AreEqual (font, files[1].Raw);
		Assert.IsTrue (bytes.AsSpan ().IndexOf ("cover.png"u8) < 0);
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_RemovingEveryPicture_KeepsTheFont ()
	{
		var font = Font (6);
		var original = Movie (Attachments (CoverPng (5), font), Master (MatroskaIds.Cluster, new byte[] { 4 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [];

		var bytes = Save (file, original);

		var files = AttachedFiles (bytes);
		Assert.AreEqual (1, files.Count);
		CollectionAssert.AreEqual (font, files[0].Raw);
		Assert.AreEqual (0, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_Clear_RemovesPicturesButNotFonts ()
	{
		var font = Font (6);
		var original = Movie (Tags (Tag (50, Simple ("TITLE", "Film"))), Attachments (CoverPng (5), font), Master (MatroskaIds.Cluster, new byte[] { 4 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Clear ();

		var bytes = Save (file, original);

		var files = AttachedFiles (bytes);
		Assert.AreEqual (1, files.Count);
		CollectionAssert.AreEqual (font, files[0].Raw);
		var again = MatroskaFile.Read (bytes).File!;
		Assert.AreEqual (0, again.Tag.Pictures.Length);
		Assert.IsNull (again.Tag.Title);
	}

	[TestMethod]
	public void Save_RemovingTheOnlyAttachment_VoidsTheElement_AndItsSeek ()
	{
		var attachments = Attachments (CoverPng (5));
		var info = Info ();
		var cluster = Master (MatroskaIds.Cluster, new byte[] { 4 });
		var seekHead = Master (MatroskaIds.SeekHead, Concat (
			SeekEntry (MatroskaIds.Info, 0),
			SeekEntry (MatroskaIds.Attachments, 0)));
		var infoAt = (ulong)seekHead.Length;
		var attachmentsAt = infoAt + (ulong)info.Length;
		seekHead = Master (MatroskaIds.SeekHead, Concat (
			SeekEntry (MatroskaIds.Info, infoAt),
			SeekEntry (MatroskaIds.Attachments, attachmentsAt)));
		var original = FileWith (seekHead, info, attachments, cluster);
		Assert.IsTrue (IsElementAt (original, attachmentsAt, MatroskaIds.Attachments));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [];

		var bytes = Save (file, original);

		Assert.AreEqual (-1, OffsetOfChild (bytes, MatroskaIds.Attachments));
		Assert.IsFalse (ReadSeeks (bytes).Any (s => s.Id == MatroskaIds.Attachments));
		Assert.AreEqual (original.Length, bytes.Length);
		Assert.AreEqual (1, CountOf (bytes, cluster));
	}

	[TestMethod]
	public void Save_EveryPictureType_RoundTrips ()
	{
		var original = Movie (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		var pictures = new List<IPicture> ();
		for (var value = 0; value <= (int)PictureType.PublisherLogo; value++)
			pictures.Add (new TestPicture (value % 2 == 0 ? "image/jpeg" : "image/png", (PictureType)value, "Picture " + value, value % 2 == 0 ? Jpeg : Png));
		file.Tag.Pictures = [.. pictures];

		var bytes = Save (file, original);

		var read = MatroskaFile.Read (bytes).File!.Tag.Pictures;
		Assert.AreEqual (pictures.Count, read.Length);
		foreach (var expected in pictures) {
			var match = read.Single (p => p.Description == expected.Description);
			Assert.AreEqual (expected.PictureType, match.PictureType, expected.Description);
			Assert.AreEqual (expected.MimeType, match.MimeType);
			CollectionAssert.AreEqual (expected.PictureData.ToArray (), match.PictureData.ToArray ());
		}

		var names = AttachedFiles (bytes).Select (f => f.Name).ToList ();
		Assert.AreEqual ("cover.png", names[0]);
		CollectionAssert.Contains (names, "back.jpg");
		CollectionAssert.Contains (names, "artist.jpg");
		CollectionAssert.Contains (names, "other.jpg");
		CollectionAssert.Contains (names, "publisher_logo.jpg");
		Assert.AreEqual (1, names.Count (n => n!.Contains ("cover", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Save_RepeatedTypes_GetNumberedNames_AndKeepTheirType ()
	{
		var original = Movie (Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [
			new TestPicture ("image/jpeg", PictureType.Other, "a", Jpeg),
			new TestPicture ("image/jpeg", PictureType.Other, "b", Jpeg),
			new TestPicture ("image/jpeg", PictureType.FrontCover, "c", Jpeg),
			new TestPicture ("image/jpeg", PictureType.FrontCover, "d", Jpeg),
		];

		var bytes = Save (file, original);

		var names = AttachedFiles (bytes).Select (f => f.Name).ToArray ();
		Assert.AreEqual ("cover.jpg|cover_2.jpg|other.jpg|other_2.jpg", string.Join ("|", names));
		var read = MatroskaFile.Read (bytes).File!.Tag.Pictures;
		CollectionAssert.AreEqual (
			new[] { PictureType.FrontCover, PictureType.FrontCover, PictureType.Other, PictureType.Other },
			read.Select (p => p.PictureType).ToArray ());
		Assert.AreEqual ("c|d|a|b", string.Join ("|", read.Select (p => p.Description)));
	}

	[TestMethod]
	public void Save_NewName_DoesNotCollideWithAKeptAttachment ()
	{
		var keptCover = CoverPng (5);
		var stray = Picture ("cover.jpg", "application/octet-stream", null, Jpeg, 9);
		var original = Movie (Attachments (keptCover, stray), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [.. file.Tag.Pictures, new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var names = AttachedFiles (Save (file, original)).Select (f => f.Name).ToArray ();

		// The kept cover.png stays first, so the new front cover goes after the kept attachments.
		Assert.AreEqual ("cover.png|cover.jpg|cover_2.jpg", string.Join ("|", names));
	}

	[TestMethod]
	public void Save_NewPictures_AreIdempotent ()
	{
		var original = WithSeekHeadRoom (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [
			new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg),
			new TestPicture ("image/png", PictureType.Artist, "The band", Png),
		];

		var first = Save (file, original);
		var second = Save (file, first);

		CollectionAssert.AreEqual (first, second);
	}

	[TestMethod]
	public void Save_NonImageMediaType_FailsAndLeavesTheFile ()
	{
		var original = Movie (Master (MatroskaIds.Cluster, new byte[] { 1 }));
		foreach (var mime in new[] { "application/pdf", "image/", "image/jpeég", "text/plain" }) {
			var file = MatroskaFile.Read (original).File!;
			file.Tag.Pictures = [new TestPicture (mime, PictureType.FrontCover, "", Jpeg)];
			AssertFailure (file, new MockFileSystem (), "/bad.mkv", original, "image/");
		}
	}

	[TestMethod]
	public void Save_EmptyMediaType_IsDetected_OrFails ()
	{
		var original = Movie (Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("", PictureType.FrontCover, "", Png)];
		var files = AttachedFiles (Save (file, original));
		Assert.AreEqual ("image/png", files[0].MediaType);
		Assert.AreEqual ("cover.png", files[0].Name);

		var unknown = MatroskaFile.Read (original).File!;
		unknown.Tag.Pictures = [new TestPicture ("", PictureType.FrontCover, "", new byte[] { 1, 2, 3 })];
		AssertFailure (unknown, new MockFileSystem (), "/unknown.mkv", original, "image/");
	}

	[TestMethod]
	public void Save_EmptyPicture_Fails ()
	{
		var original = Movie (Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", [])];

		AssertFailure (file, new MockFileSystem (), "/empty.mkv", original, "no data");
	}

	[TestMethod]
	public void Save_OtherImageTypes_AreWrittenWithTheirMediaType ()
	{
		var gif = "GIF89a\u0001\u0002"u8.ToArray ();
		var original = Movie (Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [
			new TestPicture ("image/gif", PictureType.FrontCover, "", gif),
			new TestPicture ("image/svg+xml", PictureType.Illustration, "", "<svg/>"u8.ToArray ()),
			new TestPicture ("IMAGE/WEBP", PictureType.Media, "", "RIFF0000WEBP"u8.ToArray ()),
		];

		var bytes = Save (file, original);

		var files = AttachedFiles (bytes);
		Assert.AreEqual ("cover.gif|illustration.svg|media.webp", string.Join ("|", files.Select (f => f.Name)));
		Assert.AreEqual ("image/gif|image/svg+xml|IMAGE/WEBP", string.Join ("|", files.Select (f => f.MediaType)));
		Assert.AreEqual (3, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_PicturesOnWebM_FailsAndLeavesTheFile ()
	{
		var original = Matroska (Master (MatroskaIds.Segment, Concat (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }))), "webm");
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		AssertFailure (file, new MockFileSystem (), "/movie.webm", original, "WebM");
	}

	[TestMethod]
	public void Save_NoPicturesOnWebM_StillSavesTheTags ()
	{
		var original = Matroska (Master (MatroskaIds.Segment, Concat (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }))), "webm");
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [];
		file.Tag.Title = "Clip";

		var bytes = Save (file, original);

		Assert.AreEqual ("Clip", MatroskaFile.Read (bytes).File!.Tag.Title);
		Assert.AreEqual (-1, OffsetOfChild (bytes, MatroskaIds.Attachments));
	}

	[TestMethod]
	public void Save_RemovedAttachment_DropsTheTagsThatTargetedIt ()
	{
		var onlyAttachment = Master (0x7373, Concat (
			Master (0x63C0, Concat (Element (0x68CA, new byte[] { 30 }), Element (0x63C6, new byte[] { 5 }))),
			Simple ("TITLE", "Cover title")));
		var attachmentAndTrack = Master (0x7373, Concat (
			Master (0x63C0, Concat (Element (0x68CA, new byte[] { 30 }), Element (0x63C6, new byte[] { 5 }), Element (0x63C5, new byte[] { 1 }))),
			Simple ("TITLE", "Shared title")));
		var fontTag = Master (0x7373, Concat (
			Master (0x63C0, Concat (Element (0x68CA, new byte[] { 30 }), Element (0x63C6, new byte[] { 6 }))),
			Simple ("TITLE", "Font title")));
		var original = Movie (
			Tags (Tag (50, Simple ("TITLE", "Film")), onlyAttachment, attachmentAndTrack, fontTag),
			Attachments (CoverPng (5), Font (6)),
			Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [];

		var bytes = Save (file, original);

		Assert.IsTrue (bytes.AsSpan ().IndexOf ("Cover title"u8) < 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x63C6, new byte[] { 5 })) < 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf ("Shared title"u8) >= 0);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Element (0x63C5, new byte[] { 1 })) >= 0);
		Assert.AreEqual (1, CountOf (bytes, fontTag));
		Assert.AreEqual ("Film", MatroskaFile.Read (bytes).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_NewAttachments_FitInAVoidBeforeTheClusters ()
	{
		var hole = new byte[200];
		hole[0] = 0xEC;
		hole[1] = 0x40;
		hole[2] = 200 - 3;
		var cluster = Master (MatroskaIds.Cluster, new byte[] { 0x5A, 0x5B });
		var original = FileWith (Info (), hole, cluster);
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		Assert.AreEqual (original.Length, bytes.Length);
		Assert.AreEqual (IndexOf (original, cluster), IndexOf (bytes, cluster));
		Assert.IsTrue (OffsetOfChild (bytes, MatroskaIds.Attachments) < OffsetOfChild (bytes, MatroskaIds.Cluster));
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_NewAttachments_LeaveTheSeekHeadVoidForTheSeekEntry ()
	{
		var cluster = Master (MatroskaIds.Cluster, new byte[] { 0x5A });
		var original = WithSeekHeadRoom (Info (), cluster);
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var seek = ReadSeeks (bytes).Single (s => s.Id == MatroskaIds.Attachments);
		Assert.IsTrue (IsElementAt (bytes, seek.Position, MatroskaIds.Attachments));
		Assert.IsTrue (seek.Position > (ulong)OffsetOfChild (bytes, MatroskaIds.Cluster));
	}

	[TestMethod]
	public void Save_NewAttachments_BeforeAnUnknownSizeCluster_ShiftTheCuesAndSeeks ()
	{
		var info = Info ();
		var cluster = Unknown (MatroskaIds.Cluster, Element (0xE7, new byte[] { 0 }));
		var cuesPlaceholder = CueElement (0);
		var room = Element (MatroskaIds.Void, new byte[20]);
		var seekHead = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, 0x10), SeekEntry (MatroskaIds.Cluster, 0x10), room));
		var clusterAt = (ulong)(seekHead.Length + info.Length + cuesPlaceholder.Length);
		seekHead = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, (ulong)seekHead.Length), SeekEntry (MatroskaIds.Cluster, clusterAt), room));
		var original = Matroska (Unknown (MatroskaIds.Segment, Concat (seekHead, info, CueElement (clusterAt), cluster)));
		Assert.IsTrue (IsElementAtUnknown (original, clusterAt, MatroskaIds.Cluster));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var again = MatroskaFile.Read (bytes);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual (1, again.File!.Tag.Pictures.Length);
		var moved = ReadSeeksUnknown (bytes).Single (s => s.Id == MatroskaIds.Cluster).Position;
		Assert.IsTrue (moved > clusterAt);
		Assert.IsTrue (IsElementAtUnknown (bytes, moved, MatroskaIds.Cluster));
		Assert.AreEqual (moved, CuePositionUnknown (bytes));
		var attachmentsAt = ReadSeeksUnknown (bytes).Single (s => s.Id == MatroskaIds.Attachments).Position;
		Assert.IsTrue (IsElementAtUnknown (bytes, attachmentsAt, MatroskaIds.Attachments));
		Assert.AreEqual (clusterAt, attachmentsAt);
	}

	[TestMethod]
	public void Save_NewAttachments_ShiftClusterPositionAndCues_LikeATagsInsert ()
	{
		// Attachments go in before the last, unknown-size Cluster, so that Cluster moves. Its Position
		// (RFC 9559 section 5.1.3.2) and its CueClusterPosition move with it; the earlier Cluster stays.
		static byte[] Two (ulong value) => [(byte)(value >> 8), (byte)value];
		static byte[] Cue (ulong time, ulong at) => Master (0xBB, Concat (
			Element (0xB3, new[] { (byte)time }),
			Master (0xB7, Concat (Element (0xF7, new byte[] { 1 }), Element (MatroskaIds.CueClusterPosition, Two (at))))));
		var info = Info ();
		var room = Element (MatroskaIds.Void, new byte[24]);
		byte[] Build (ulong first, ulong second)
		{
			var seekHead = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, 0x10), SeekEntry (MatroskaIds.Cues, 0x10), room));
			var cues = Master (MatroskaIds.Cues, Concat (Cue (0, first), Cue (1, second)));
			var known = Master (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 0 }), Element (MatroskaIds.Position, Two (first))));
			var unknown = Unknown (MatroskaIds.Cluster, Concat (Element (0xE7, new byte[] { 1 }), Element (MatroskaIds.Position, Two (second))));
			return Concat (seekHead, info, cues, known, unknown);
		}

		var probe = Build (0x100, 0x100);
		var firstAt = (ulong)IndexOf (probe, Id (MatroskaIds.Cluster));
		var secondAt = (ulong)probe.AsSpan ((int)firstAt + 1).IndexOf (Id (MatroskaIds.Cluster)) + firstAt + 1;
		var original = Matroska (Unknown (MatroskaIds.Segment, Build (firstAt, secondAt)));
		Assert.IsTrue (IsElementAtUnknown (original, firstAt, MatroskaIds.Cluster));
		Assert.IsTrue (IsElementAtUnknown (original, secondAt, MatroskaIds.Cluster));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var bodyAt = UnknownSegmentBodyAt (bytes);
		var attachmentsAt = ReadSeeksUnknown (bytes).Single (s => s.Id == MatroskaIds.Attachments).Position;
		Assert.AreEqual (secondAt, attachmentsAt);
		var positions = ClusterPositions (bytes, bodyAt);
		Assert.AreEqual (2, positions.Count);
		Assert.AreEqual ((firstAt, firstAt), positions[0]);
		Assert.IsTrue (positions[1].At > secondAt);
		Assert.AreEqual (positions[1].At, positions[1].Stored);
		CollectionAssert.AreEqual (new[] { firstAt, positions[1].At }, CueClusterPositions (bytes, bodyAt));
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	/// <summary>Each Cluster's segment offset and the Position it stores.</summary>
	static List<(ulong At, ulong Stored)> ClusterPositions (byte[] file, int bodyAt)
	{
		// Known-size children are stepped over; an unknown-size Cluster runs to the end of the file.
		var found = new List<(ulong, ulong)> ();
		var at = bodyAt;
		while (at < file.Length && EbmlReader.TryRead (file, at, file.Length, out var element)) {
			var end = element.UnknownSize ? file.Length : element.End;
			if (element.Id == MatroskaIds.Cluster) {
				var offset = element.DataOffset;
				while (offset < end && EbmlReader.TryRead (file, offset, end, out var child) && !child.UnknownSize) {
					if (child.Id == MatroskaIds.Position)
						found.Add (((ulong)(at - bodyAt), EbmlReader.ReadUInt (file.AsSpan (child.DataOffset, (int)child.DataLength))));
					offset = child.End;
				}
			}

			at = end;
		}

		return found;
	}

	static List<EbmlElement> ChildrenOf (byte[] file, int offset, int end, ulong id)
	{
		var found = new List<EbmlElement> ();
		while (offset < end && EbmlReader.TryRead (file, offset, end, out var element) && !element.UnknownSize) {
			if (element.Id == id)
				found.Add (element);
			offset = element.End;
		}

		return found;
	}

	static List<ulong> CueClusterPositions (byte[] file, int bodyAt)
	{
		var found = new List<ulong> ();
		var at = bodyAt;
		EbmlElement cues;
		while (true) {
			Assert.IsTrue (EbmlReader.TryRead (file, at, file.Length, out cues) && !cues.UnknownSize);
			if (cues.Id == MatroskaIds.Cues)
				break;
			at = cues.End;
		}

		// Only the spec path: Cues, CuePoint, CueTrackPositions, CueClusterPosition (RFC 9559 section 5.1.5).
		foreach (var point in ChildrenOf (file, cues.DataOffset, cues.End, 0xBB)) {
			foreach (var positions in ChildrenOf (file, point.DataOffset, point.End, 0xB7)) {
				foreach (var position in ChildrenOf (file, positions.DataOffset, positions.End, MatroskaIds.CueClusterPosition))
					found.Add (EbmlReader.ReadUInt (file.AsSpan (position.DataOffset, (int)position.DataLength)));
			}
		}

		return found;
	}

	[TestMethod]
	public void Save_NewAttachments_WithoutSeekHeadRoom_GoAfterTheSeekHeadAndAreListed ()
	{
		// RFC 9559 section 6.3: the SeekHead MUST reference every Top-Level Element. With no room
		// for a Seek, the SeekHead grows and the attachments go right after it.
		var info = Info ();
		var cluster = Master (MatroskaIds.Cluster, new byte[] { 0xE7, 0x81, 0x00, 0xA3, 0x82, 0x51, 0x52 });
		var probe = Master (MatroskaIds.SeekHead, SeekEntry (MatroskaIds.Info, 0));
		var seekHead = Master (MatroskaIds.SeekHead, SeekEntry (MatroskaIds.Info, (ulong)probe.Length));
		var original = FileWith (seekHead, info, cluster);
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var seeks = ReadSeeks (bytes);
		var attachments = seeks.Find (entry => entry.Id == MatroskaIds.Attachments);
		Assert.AreEqual (MatroskaIds.Attachments, attachments.Id);
		Assert.IsTrue (IsElementAt (bytes, attachments.Position, MatroskaIds.Attachments));
		Assert.IsTrue (IsElementAt (bytes, seeks.Find (entry => entry.Id == MatroskaIds.Info).Position, MatroskaIds.Info));
		Assert.IsTrue (OffsetOfChild (bytes, MatroskaIds.Attachments) < OffsetOfChild (bytes, MatroskaIds.Cluster));
		Assert.AreEqual (1, CountOf (bytes, cluster));
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_NewSeekEntry_WidensAFullSeekHeadSize_IntoTheVoidAfterIt ()
	{
		// The payload is 120 of the 126 bytes a one-byte size can hold, with a CRC-32 first.
		// The filler is not a Void, so the entry cannot go inside the SeekHead.
		var info = Info ();
		var filler = Element (0x5555, new byte[97]);
		var hole = new byte[64];
		hole[0] = 0xEC;
		hole[1] = 0x80 | 62;
		var probe = WithCrc (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, 0), filler));
		Assert.AreEqual (4 + 1 + 120, probe.Length);
		var seekHead = WithCrc (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, (ulong)(probe.Length + hole.Length)), filler));
		var original = FileWith (seekHead, hole, info, Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var seek = ReadSeeks (bytes).Single (s => s.Id == MatroskaIds.Attachments);
		Assert.IsTrue (IsElementAt (bytes, seek.Position, MatroskaIds.Attachments));
		var infoSeek = ReadSeeks (bytes).Single (s => s.Id == MatroskaIds.Info);
		Assert.IsTrue (IsElementAt (bytes, infoSeek.Position, MatroskaIds.Info));
		Assert.IsTrue (SeekHeadCrcOk (bytes));
		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		Assert.IsTrue (EbmlReader.TryRead (body, 0, body.Length, out var head));
		Assert.AreEqual (6, head.HeaderLength);
		Assert.AreEqual (IndexOf (original, info), IndexOf (bytes, info));
	}

	[TestMethod]
	public void Save_NewAttachments_WithoutASeekHead_GoBeforeTheClusters ()
	{
		var hole = Element (MatroskaIds.Void, new byte[100]);
		var cluster = Master (MatroskaIds.Cluster, new byte[] { 1 });
		var roomy = FileWith (Info (), hole, cluster);
		var fits = MatroskaFile.Read (roomy).File!;
		fits.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];
		var bytes = Save (fits, roomy);
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
		Assert.IsTrue (OffsetOfChild (bytes, MatroskaIds.Attachments) < OffsetOfChild (bytes, MatroskaIds.Cluster));

		// Without a SeekHead a player reads only up to the first Cluster (RFC 9559 section 25.3.4).
		// With no Void there, the attachments are inserted before the Cluster, which moves once.
		var full = FileWith (Info (), cluster);
		var after = MatroskaFile.Read (full).File!;
		after.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];
		var moved = Save (after, full);
		Assert.AreEqual (1, MatroskaFile.Read (moved).File!.Tag.Pictures.Length);
		Assert.IsTrue (OffsetOfChild (moved, MatroskaIds.Attachments) < OffsetOfChild (moved, MatroskaIds.Cluster));
		Assert.AreEqual (1, CountOf (moved, cluster));
	}

	[TestMethod]
	public void Save_NewAttachments_BeforeTheOnlyUnknownSizeCluster_WithoutASeekHead_AreWritten ()
	{
		var cluster = Unknown (MatroskaIds.Cluster, Element (0xE7, new byte[] { 0 }));
		var original = Matroska (Unknown (MatroskaIds.Segment, Concat (Info (), cluster)));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
		Assert.IsTrue (bytes.AsSpan ().IndexOf (Id (MatroskaIds.Attachments)) < bytes.AsSpan ().IndexOf (Id (MatroskaIds.Cluster)));
	}

	[TestMethod]
	public void Save_ExistingAttachmentsAfterTheClusters_WithoutASeekHead_CanGrowInPlace ()
	{
		var original = FileWith (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }), Attachments (CoverPng (5)));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [.. file.Tag.Pictures, new TestPicture ("image/jpeg", PictureType.BackCover, "", Jpeg)];

		var bytes = Save (file, original);

		Assert.AreEqual (2, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_NewSeekEntry_NeverGoesInTheSecondSeekHead ()
	{
		// A second SeekHead MUST only reference Clusters (RFC 9559 section 6.3).
		var room = Element (MatroskaIds.Void, new byte[24]);
		var info = Info ();
		var second = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Cluster, 0), room));
		var firstProbe = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, 0), SeekEntry (MatroskaIds.SeekHead, 0)));
		var infoAt = (ulong)firstProbe.Length;
		var secondAt = infoAt + (ulong)info.Length;
		var clusterAt = secondAt + (ulong)second.Length;
		second = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Cluster, clusterAt), room));
		var first = Master (MatroskaIds.SeekHead, Concat (SeekEntry (MatroskaIds.Info, infoAt), SeekEntry (MatroskaIds.SeekHead, secondAt)));
		var original = FileWith (first, info, second, Master (MatroskaIds.Cluster, new byte[] { 1 }));
		Assert.IsTrue (IsElementAt (original, clusterAt, MatroskaIds.Cluster));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		// The first SeekHead grows to list the attachments. The second still lists only the Cluster.
		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		var heads = new List<List<SeekRef>> ();
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.SeekHead) {
				var found = new List<SeekRef> ();
				ReadSeekHead (body.AsSpan (element.DataOffset, (int)element.DataLength), found);
				heads.Add (found);
			}

			offset = element.End;
		}

		Assert.AreEqual (2, heads.Count);
		var listed = heads[0].Find (entry => entry.Id == MatroskaIds.Attachments);
		Assert.AreEqual (MatroskaIds.Attachments, listed.Id);
		Assert.IsTrue (IsElementAt (bytes, listed.Position, MatroskaIds.Attachments));
		Assert.IsTrue (heads[1].TrueForAll (entry => entry.Id == MatroskaIds.Cluster));
		Assert.IsTrue (IsElementAt (bytes, heads[1][0].Position, MatroskaIds.Cluster));
		foreach (var entry in heads[0])
			Assert.IsTrue (IsElementAt (bytes, entry.Position, entry.Id));
		Assert.AreEqual (1, MatroskaFile.Read (bytes).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	public void Save_AttachmentsWithCrc_GetANewCrc ()
	{
		var attachments = WithCrc (MatroskaIds.Attachments, Concat (CoverPng (5), Font (6)));
		var original = Movie (attachments, Master (MatroskaIds.Cluster, new byte[] { 1 }));
		Assert.IsTrue (EveryCrcMatches (original));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [.. file.Tag.Pictures, new TestPicture ("image/jpeg", PictureType.BackCover, "", Jpeg)];

		var bytes = Save (file, original);

		Assert.AreEqual (3, AttachedFiles (bytes).Count);
		Assert.IsTrue (EveryCrcMatches (bytes));
		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		var at = OffsetOfChild (bytes, MatroskaIds.Attachments);
		Assert.IsTrue (EbmlReader.TryRead (body, at, body.Length, out var element));
		Assert.IsTrue (EbmlReader.TryRead (body, element.DataOffset, element.End, out var first));
		Assert.AreEqual (MatroskaIds.Crc32, first.Id);
	}

	[TestMethod]
	public void Save_TwoAttachmentsElements_BecomeOne ()
	{
		var font = Font (6);
		var original = Movie (Attachments (CoverPng (5)), Attachments (font), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		Assert.AreEqual (1, file.Tag.Pictures.Length);
		file.Tag.Pictures = [];

		var bytes = Save (file, original);

		Assert.IsTrue (TrySegmentBody (bytes, out var body, out _));
		Assert.AreEqual (1, CountChildren (body, MatroskaIds.Attachments));
		var files = AttachedFiles (bytes);
		Assert.AreEqual (1, files.Count);
		CollectionAssert.AreEqual (font, files[0].Raw);
	}

	[TestMethod]
	public void Save_MalformedAttachments_FailsOnlyWhenPicturesChange ()
	{
		var broken = Master (MatroskaIds.Attachments, Concat (CoverPng (5), new byte[] { 0x61, 0xA7, 0x88, 0x01 }));
		var original = Movie (broken, Master (MatroskaIds.Cluster, new byte[] { 1 }));

		var untouched = MatroskaFile.Read (original).File!;
		untouched.Tag.Title = "Kept";
		var bytes = Save (untouched, original);
		Assert.AreEqual (1, CountOf (bytes, broken));

		var changed = MatroskaFile.Read (original).File!;
		changed.Tag.Pictures = [];
		AssertFailure (changed, new MockFileSystem (), "/broken.mkv", original, "Attachments cannot be bounded");
	}

	[TestMethod]
	public void ReadAndSave_AttachmentsCutAtEveryByte_NeverThrow ()
	{
		var payload = Concat (CoverPng (5), Font (6), Picture ("back.jpg", "image/jpeg", "Back", Jpeg, 7));
		for (var cut = 0; cut <= payload.Length; cut++) {
			var original = Movie (Master (MatroskaIds.Attachments, payload.AsSpan (0, cut).ToArray ()), Master (MatroskaIds.Cluster, new byte[] { 1 }));
			var read = MatroskaFile.Read (original);
			Assert.IsTrue (read.IsSuccess, read.Error);
			var file = read.File!;
			file.Tag.Pictures = [.. file.Tag.Pictures.Skip (1), new TestPicture ("image/jpeg", PictureType.Media, "", Jpeg)];

			var fs = new MockFileSystem ();
			fs.AddFile ("/cut.mkv", original);
			var saved = file.SaveToFile ("/cut.mkv", original, fs);
			if (!saved.IsSuccess) {
				StringAssert.Contains (saved.Error, "Attachments cannot be bounded");
				CollectionAssert.AreEqual (original, fs.ReadAllBytes ("/cut.mkv"));
				continue;
			}

			var again = MatroskaFile.Read (fs.ReadAllBytes ("/cut.mkv"));
			Assert.IsTrue (again.IsSuccess, again.Error);
			Assert.IsTrue (again.File!.Tag.Pictures.Any (p => p.PictureType == PictureType.Media), "cut " + cut);
		}
	}

	[TestMethod]
	[DataRow ("image/")]
	[DataRow ("image/jpeg ")]
	[DataRow ("IMAGE/JPEG")]
	[DataRow ("image/jp\u00e9g")]
	[DataRow ("image/\u0001png")]
	public void Save_ReadPictureWithAnyStoredMediaType_MatchesItsOwnAttachment (string mediaType)
	{
		// The reader surfaces every image/ attachment, so the writer must match it as stored.
		// RFC 9559 section 5.1.6.1.3 asks for an RFC 6838 type, but muxers write what they are given.
		var loose = Picture ("cover.jpg", mediaType, null, Jpeg, 5);
		var targeted = Master (0x7373, Concat (
			Master (0x63C0, Concat (Element (0x68CA, new byte[] { 30 }), Element (0x63C6, new byte[] { 5 }))),
			Simple ("COMMENT", "hello")));
		var font = Font (6);
		var original = Movie (Tags (targeted), Attachments (loose, font), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		Assert.AreEqual (1, MatroskaFile.Read (original).File!.Tag.Pictures.Length);

		var same = MatroskaFile.Read (original).File!;
		same.Tag.Pictures = same.Tag.Pictures;
		CollectionAssert.AreEqual (original, Save (same, original));

		var copied = MatroskaFile.Read (original).File!;
		copied.Tag.Pictures = [.. copied.Tag.Pictures.Select (p => (IPicture)new TestPicture (p.MimeType, p.PictureType, p.Description, p.PictureData.ToArray ()))];
		CollectionAssert.AreEqual (original, Save (copied, original));

		var added = MatroskaFile.Read (original).File!;
		added.Tag.Pictures = [.. added.Tag.Pictures, new TestPicture ("image/png", PictureType.BackCover, "", Png)];
		var bytes = Save (added, original);
		var files = AttachedFiles (bytes);
		Assert.AreEqual (3, files.Count);
		CollectionAssert.AreEqual (loose, files[0].Raw);
		CollectionAssert.AreEqual (font, files[1].Raw);
		Assert.AreEqual (1, CountOf (bytes, targeted));
	}

	[TestMethod]
	[DataRow ("cover.jpg", PictureType.FrontCover, "cover.jpg|font.ttf|cover_2.jpg")]
	[DataRow ("cover.png", PictureType.FrontCover, "cover.png|font.ttf|cover.jpg")]
	[DataRow ("small_cover.jpg", PictureType.FrontCover, "cover.jpg|small_cover.jpg|font.ttf")]
	[DataRow ("cover_land.jpg", PictureType.FrontCover, "cover.jpg|cover_land.jpg|font.ttf")]
	[DataRow ("small_cover_land.jpg", PictureType.FrontCover, "cover.jpg|small_cover_land.jpg|font.ttf")]
	[DataRow ("back_cover.jpg", PictureType.FrontCover, "cover.jpg|back_cover.jpg|font.ttf")]
	[DataRow ("Cover.jpg", PictureType.FrontCover, "cover.jpg|Cover.jpg|font.ttf")]
	[DataRow ("back.jpg", PictureType.BackCover, "cover.jpg|back.jpg|font.ttf")]
	public void Save_NewFrontCover_GoesFirst_UnlessTheNormalCoverIsKept (string keptName, PictureType keptType, string expected)
	{
		// RFC 9559 section 21.1: the normal cover is cover.(jpg|png) and SHOULD be the first
		// attachment. small_ and _land variants are not the normal cover, and names are case-sensitive.
		var kept = Picture (keptName, "image/jpeg", null, Jpeg, 5);
		var original = Movie (Attachments (kept, Font (6)), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		Assert.AreEqual (keptType, file.Tag.Pictures.Single ().PictureType);
		file.Tag.Pictures = [.. file.Tag.Pictures, new TestPicture ("image/jpeg", PictureType.FrontCover, "", Png)];

		var files = AttachedFiles (Save (file, original));

		Assert.AreEqual (expected, string.Join ("|", files.Select (f => f.Name)));
		CollectionAssert.AreEqual (kept, files.Single (f => f.Name == keptName).Raw);
	}

	[TestMethod]
	[DataRow ("cover.png", "image/png", true, "cover.jpg|cover.png|font.ttf")]
	[DataRow ("cover.jpg", "application/octet-stream", false, "cover_2.jpg|cover.jpg|font.ttf")]
	public void Save_KeptCoverThatIsNotAPicture_IsNotTheNormalCover (string keptName, string keptType, bool emptyData, string expected)
	{
		// A kept cover.* that the reader does not surface (empty FileData or a non-image type) cannot
		// be shown, so it is not the normal cover of RFC 9559 section 21.1: the new cover goes first.
		var kept = Picture (keptName, keptType, null, emptyData ? [] : Jpeg, 5);
		var original = Movie (Attachments (kept, Font (6)), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		Assert.AreEqual (0, file.Tag.Pictures.Length);
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Png)];

		var files = AttachedFiles (Save (file, original));

		Assert.AreEqual (expected, string.Join ("|", files.Select (f => f.Name)));
		CollectionAssert.AreEqual (kept, files[1].Raw);
	}

	[TestMethod]
	[DataRow (true, 0)]
	[DataRow (false, 120)]
	public void Save_CoverAndTrackFlag_AreBothWritten (bool flagStored, int voidAfterTracks)
	{
		// The track pass writes FlagEnabled first, then the attachments and tags passes run on its output.
		var tracks = Master (MatroskaIds.Tracks, Concat (
			AudioEntry (1, 44100, flagStored ? [Enabled (1)] : []),
			VideoEntry (2, 320)));
		var original = CoverAndTracksFile (tracks, voidAfterTracks);
		var file = MatroskaFile.Read (original).File!;
		Assert.IsTrue (file.Tracks[0].IsEnabled);
		file.Tracks[0].IsEnabled = false;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var bytes = Save (file, original);

		var again = MatroskaFile.Read (bytes).File!;
		Assert.IsFalse (again.Tracks[0].IsEnabled);
		Assert.IsTrue (again.Tracks[1].IsEnabled);
		Assert.AreEqual (1, again.Tag.Pictures.Length);
		Assert.AreEqual ("cover.jpg", AttachedFiles (bytes)[0].Name);
		Assert.AreEqual ("Movie", again.Tag.Title);
		Assert.AreEqual (1, CountOf (bytes, Master (MatroskaIds.Cluster, ClusterMarker)));
		AssertSeeksLand (original, bytes);
		Assert.IsTrue (ReadSeeks (bytes).Any (s => s.Id == MatroskaIds.Attachments));
	}

	[TestMethod]
	public void Save_UntouchedCoverAndTracks_IsByteIdentical ()
	{
		var tracks = Master (MatroskaIds.Tracks, Concat (AudioEntry (1, 44100, Enabled (1)), VideoEntry (2, 320)));
		var withCover = CoverAndTracksFile (tracks, 0);
		var first = MatroskaFile.Read (withCover).File!;
		first.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];
		var original = Save (first, withCover);

		var file = MatroskaFile.Read (original).File!;
		foreach (var track in file.Tracks)
			track.IsEnabled = track.IsEnabled;
		file.Tag.Pictures = file.Tag.Pictures;

		CollectionAssert.AreEqual (original, Save (file, original));
	}

	/// <summary>
	/// SeekHead, a Void it can grow into, Info, Tracks, an optional Void, Tags, Cues, Cluster.
	/// </summary>
	static byte[] CoverAndTracksFile (byte[] tracks, int voidAfterTracks)
	{
		var info = Info ();
		var hole = voidAfterTracks == 0 ? Array.Empty<byte> () : MakeVoid (voidAfterTracks);
		var tags = Tags (Tag (50, Simple ("TITLE", "Movie")));
		var cluster = Master (MatroskaIds.Cluster, ClusterMarker);
		return Layout (new[] { MatroskaIds.Info, MatroskaIds.Tracks, MatroskaIds.Tags, MatroskaIds.Cues, MatroskaIds.Cluster },
			(seekHead, positions) => Concat (seekHead, MakeVoid (64), info, tracks, hole, tags, CueElement (positions[4]), cluster));
	}

	[TestMethod]
	public void Save_CopyWithAnEmptyMediaType_MatchesByTheDetectedType ()
	{
		// Picture.DetectMimeType reads image/jpeg from the bytes, which is the stored type.
		var cover = Picture ("cover.jpg", "image/jpeg", null, Jpeg, 5);
		var targeted = Master (0x7373, Concat (
			Master (0x63C0, Concat (Element (0x68CA, new byte[] { 30 }), Element (0x63C6, new byte[] { 5 }))),
			Simple ("COMMENT", "hello")));
		var original = Movie (Tags (targeted), Attachments (cover), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [.. file.Tag.Pictures.Select (p => (IPicture)new TestPicture ("", p.PictureType, p.Description, p.PictureData.ToArray ()))];

		var bytes = Save (file, original);

		CollectionAssert.AreEqual (original, bytes);
		Assert.AreEqual (1, CountOf (bytes, targeted));
	}

	[TestMethod]
	public void Save_CopyWithAnotherCase_MatchesTheStoredMediaType ()
	{
		// Media types are case-insensitive (RFC 6838 section 4.2), so IMAGE/JPEG and image/jpeg match.
		var cover = Picture ("cover.jpg", "IMAGE/JPEG", null, Jpeg, 5);
		var original = Movie (Attachments (cover, Font (6)), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [.. file.Tag.Pictures.Select (p => (IPicture)new TestPicture ("image/jpeg", p.PictureType, p.Description, p.PictureData.ToArray ()))];

		CollectionAssert.AreEqual (original, Save (file, original));
	}

	[TestMethod]
	public void ReadAndSave_ImageWithEmptyFileData_IsNotAPicture_AndIsKept ()
	{
		var empty = Picture ("cover.png", "image/png", null, [], 5);
		var original = Movie (Attachments (empty), Master (MatroskaIds.Cluster, new byte[] { 1 }));

		var file = MatroskaFile.Read (original).File!;
		Assert.AreEqual (0, file.Tag.Pictures.Length);
		file.Tag.Pictures = file.Tag.Pictures;
		CollectionAssert.AreEqual (original, Save (file, original));

		var added = MatroskaFile.Read (original).File!;
		added.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.BackCover, "", Jpeg)];
		var files = AttachedFiles (Save (added, original));
		Assert.AreEqual (2, files.Count);
		CollectionAssert.AreEqual (empty, files[0].Raw);
		Assert.AreEqual ("back.jpg", files[1].Name);
	}

	[TestMethod]
	public void Build_AddingAPicture_AllocatesForTheAttachments_NotForTheFile ()
	{
		// 64 MB of cluster payload. The Attachments body is a few hundred bytes.
		var cluster = EbmlWriter.Master (MatroskaIds.Cluster, new byte[64 << 20]).ToArray ();
		var original = Movie (cluster);
		var pictures = new IPicture[] { new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg) };

		var before = GC.GetAllocatedBytesForCurrentThread ();
		Assert.IsTrue (MatroskaAttachments.TryBuild (original, [], pictures, false, out var element, out var changed, out _, out var error), error);
		var allocated = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsTrue (changed);
		Assert.IsTrue (element.Length < 512);
		Assert.IsTrue (allocated < 1 << 20, "allocated " + allocated);
	}

	[TestMethod]
	public void Save_LargePicture_IsWrittenWithAWideSize ()
	{
		var big = new byte[3_000_000];
		big[0] = 0xFF;
		big[1] = 0xD8;
		big[2] = 0xFF;
		big[^1] = 0x42;
		var original = Movie (Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var file = MatroskaFile.Read (original).File!;
		file.Tag.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", big)];

		var bytes = Save (file, original);

		var pictures = MatroskaFile.Read (bytes).File!.Tag.Pictures;
		Assert.AreEqual (1, pictures.Length);
		Assert.IsTrue (pictures[0].PictureData.Span.SequenceEqual (big));
	}

	[TestMethod]
	public async Task SaveAsync_WritesPicturesLikeTheSyncSave ()
	{
		var original = WithSeekHeadRoom (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var fs = new MockFileSystem ();
		fs.AddFile ("/async.mkv", original);
		var read = await MatroskaFile.ReadFromFileAsync ("/async.mkv", fs);
		var file = read.File!;
		file.Tag.Pictures = [new TestPicture ("image/png", PictureType.FrontCover, "", Png)];

		var saved = await file.SaveToFileAsync (fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var again = MatroskaFile.Read (fs.ReadAllBytes ("/async.mkv")).File!;
		Assert.AreEqual (1, again.Tag.Pictures.Length);
		Assert.AreEqual ("cover.png", AttachedFiles (fs.ReadAllBytes ("/async.mkv"))[0].Name);
	}

	[TestMethod]
	public void Save_ThroughMediaFile_WritesPictures ()
	{
		var original = WithSeekHeadRoom (Info (), Master (MatroskaIds.Cluster, new byte[] { 1 }));
		var fs = new MockFileSystem ();
		fs.AddFile ("/media.mkv", original);
		var result = MediaFile.Read ("/media.mkv", fs);
		Assert.IsTrue (result.IsSuccess, result.Error);
		result.Tag!.Pictures = [new TestPicture ("image/jpeg", PictureType.FrontCover, "", Jpeg)];

		var saved = ((MatroskaFile)result.File!).SaveToFile (fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual (1, MatroskaFile.Read (fs.ReadAllBytes ("/media.mkv")).File!.Tag.Pictures.Length);
	}

	[TestMethod]
	[DataRow ("back.jpg", null, PictureType.BackCover)]
	[DataRow ("artist_2.png", null, PictureType.Artist)]
	[DataRow ("small_cover.png", null, PictureType.FrontCover)]
	[DataRow ("cover_land.jpg", null, PictureType.FrontCover)]
	[DataRow ("small_cover_land.jpg", null, PictureType.FrontCover)]
	[DataRow ("Cover.JPG", null, PictureType.FrontCover)]
	[DataRow ("poster.jpg", "Front", PictureType.FrontCover)]
	[DataRow ("other.png", "my cover", PictureType.Other)]
	[DataRow ("Back.jpg", null, PictureType.Other)]
	[DataRow ("artist_x.png", null, PictureType.Other)]
	[DataRow ("_2.png", null, PictureType.Other)]
	[DataRow ("poster.jpg", null, PictureType.Other)]
	public void Read_AttachmentName_MapsToThePictureType (string name, string? description, PictureType expected)
	{
		var file = Movie (Attachments (Picture (name, "image/jpeg", description, Jpeg, 1)));

		var pictures = MatroskaFile.Read (file).File!.Tag.Pictures;

		Assert.AreEqual (1, pictures.Length);
		Assert.AreEqual (expected, pictures[0].PictureType);
		Assert.AreEqual (description ?? name, pictures[0].Description);
	}

	[TestMethod]
	[DataRow ("image/jpeg", "jpg")]
	[DataRow ("image/JPG", "jpg")]
	[DataRow ("image/pjpeg", "jpg")]
	[DataRow ("image/png; charset=x", "png")]
	[DataRow ("image/svg+xml", "svg")]
	[DataRow ("image/vnd.microsoft.icon", "vndmicrosofticon")]
	[DataRow ("image/***", "img")]
	[DataRow ("image", "img")]
	public void ExtensionOf_UsesTheSubtype (string mediaType, string expected) =>
		Assert.AreEqual (expected, MatroskaAttachments.ExtensionOf (mediaType));

	[TestMethod]
	[DataRow (0)]
	[DataRow (126)]
	[DataRow (127)]
	[DataRow (16382)]
	[DataRow (16383)]
	[DataRow (2097150)]
	[DataRow (2097151)]
	[DataRow (268435454)]
	[DataRow (268435455)]
	[DataRow (int.MaxValue)]
	public void EbmlWriter_Size_IsNeverTheUnknownSize (int length)
	{
		var size = EbmlWriter.Size (length);

		Assert.IsTrue (EbmlReader.TryReadVint (size, 0, size.Length, false, 8, out var value, out var width, out var unknown));
		Assert.IsFalse (unknown);
		Assert.AreEqual ((ulong)length, value);
		Assert.AreEqual (size.Length, width);
	}

	static byte[] Info () => Master (MatroskaIds.Info, Element (0x2AD7B1, new byte[] { 0x0F, 0x42, 0x40 }));

	/// <summary>A SeekHead with room, Info, then <paramref name="children"/>.</summary>
	static byte[] Movie (params byte[][] children) => WithSeekHeadRoom (Info (), children);

	/// <summary>A SeekHead that references Info, followed by a Void it can grow into (RFC 9559 section 25.2).</summary>
	static byte[] WithSeekHeadRoom (byte[] info, params byte[][] rest)
	{
		var hole = new byte[64];
		hole[0] = 0xEC;
		hole[1] = 0x80 | 62;
		var probe = Master (MatroskaIds.SeekHead, SeekEntry (MatroskaIds.Info, 0));
		var seekHead = Master (MatroskaIds.SeekHead, SeekEntry (MatroskaIds.Info, (ulong)(probe.Length + hole.Length)));
		return FileWith (Concat (seekHead, hole, info), Concat (rest));
	}

	static byte[] Attachments (params byte[][] files) => Master (MatroskaIds.Attachments, Concat (files));

	static byte[] CoverPng (byte uid) => Picture ("cover.png", "image/png", null, Png, uid);

	static byte[] Font (byte uid) => Master (MatroskaIds.AttachedFile, Concat (
		Element (MatroskaIds.FileName, "font.ttf"u8),
		Element (MatroskaIds.FileMimeType, "application/x-truetype-font"u8),
		Element (MatroskaIds.FileData, FontBytes),
		Element (MatroskaIds.FileUid, new[] { uid })));

	static byte[] Picture (string name, string mediaType, string? description, byte[] data, byte uid)
	{
		var parts = new List<byte[]> ();
		if (description is not null)
			parts.Add (Element (MatroskaIds.FileDescription, Encoding.UTF8.GetBytes (description)));
		parts.Add (Element (MatroskaIds.FileName, Encoding.UTF8.GetBytes (name)));
		parts.Add (Element (MatroskaIds.FileMimeType, Encoding.UTF8.GetBytes (mediaType)));
		parts.Add (Element (MatroskaIds.FileData, data));
		parts.Add (Element (MatroskaIds.FileUid, new[] { uid }));
		return Master (MatroskaIds.AttachedFile, Concat (parts.ToArray ()));
	}

	/// <summary>The segment offset of the first segment child with <paramref name="id"/>, or -1.</summary>
	static int OffsetOfChild (byte[] file, ulong id)
	{
		if (!TrySegmentBody (file, out var body, out _))
			return -1;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == id)
				return offset;
			offset = element.End;
		}

		return -1;
	}

	static int CountChildren (byte[] body, ulong id)
	{
		var count = 0;
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == id)
				count++;
			offset = element.End;
		}

		return count;
	}

	static List<AttachedFileView> AttachedFiles (byte[] file)
	{
		var found = new List<AttachedFileView> ();
		Assert.IsTrue (TrySegmentBody (file, out var body, out _));
		var at = OffsetOfChild (file, MatroskaIds.Attachments);
		if (at < 0)
			return found;
		Assert.IsTrue (EbmlReader.TryRead (body, at, body.Length, out var attachments));
		var offset = attachments.DataOffset;
		while (offset < attachments.End) {
			Assert.IsTrue (EbmlReader.TryRead (body, offset, attachments.End, out var child));
			if (child.Id == MatroskaIds.AttachedFile) {
				var info = MatroskaAttachments.Read (body.AsSpan (child.DataOffset, (int)child.DataLength));
				found.Add (new AttachedFileView (body.AsSpan (child.Position, child.End - child.Position).ToArray (), info.Name, info.Description, info.MediaType, info.Uid));
			}

			offset = child.End;
		}

		return found;
	}

	sealed record AttachedFileView (byte[] Raw, string? Name, string? Description, string? MediaType, ulong? Uid);

	static bool IsElementAtUnknown (byte[] file, ulong segmentPosition, ulong id)
	{
		var bodyAt = UnknownSegmentBodyAt (file);
		var idBytes = Id (id);
		var at = bodyAt + (int)segmentPosition;
		return at + idBytes.Length <= file.Length && file.AsSpan (at, idBytes.Length).SequenceEqual (idBytes);
	}

	static int UnknownSegmentBodyAt (byte[] file)
	{
		Assert.IsTrue (EbmlReader.TryRead (file, 0, file.Length, out var header));
		Assert.IsTrue (EbmlReader.TryRead (file, header.End, file.Length, out var segment));
		Assert.IsTrue (segment.UnknownSize);
		return segment.DataOffset;
	}

	static List<SeekRef> ReadSeeksUnknown (byte[] file)
	{
		var bodyAt = UnknownSegmentBodyAt (file);
		var found = new List<SeekRef> ();
		Assert.IsTrue (EbmlReader.TryRead (file, bodyAt, file.Length, out var seekHead));
		Assert.AreEqual (MatroskaIds.SeekHead, seekHead.Id);
		ReadSeekHead (file.AsSpan (seekHead.DataOffset, (int)seekHead.DataLength), found);
		return found;
	}

	static ulong CuePositionUnknown (byte[] file)
	{
		var bodyAt = UnknownSegmentBodyAt (file);
		var offset = bodyAt;
		while (EbmlReader.TryRead (file, offset, file.Length, out var element) && !element.UnknownSize) {
			if (element.Id == MatroskaIds.Cues) {
				var position = 0UL;
				Assert.IsTrue (TryFindCuePosition (file.AsSpan (element.DataOffset, (int)element.DataLength), ref position));
				return position;
			}

			offset = element.End;
		}

		Assert.Fail ("No Cues");
		return 0;
	}

	sealed class TestPicture (string mimeType, PictureType pictureType, string description, byte[] data) : IPicture
	{
		public string MimeType { get; } = mimeType;

		public PictureType PictureType { get; } = pictureType;

		public string Description { get; } = description;

		public BinaryData PictureData { get; } = new (data);
	}
}
