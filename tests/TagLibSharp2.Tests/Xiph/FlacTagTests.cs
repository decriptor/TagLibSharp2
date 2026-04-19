// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

/// <summary>
/// Tests for the FLAC tag abstraction surfaced by <see cref="FlacFile.Tag"/>.
/// </summary>
/// <remarks>
/// FLAC can store pictures in two locations:
/// <list type="bullet">
///   <item>Native PICTURE metadata blocks (block type 6) — RFC 9639 §8.8.</item>
///   <item>METADATA_BLOCK_PICTURE fields inside the VORBIS_COMMENT block (base64-encoded) —
///     https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE.</item>
/// </list>
/// The <see cref="FlacFile.Tag"/> view must surface pictures from both locations (Issue #6).
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class FlacTagTests
{
	/// <summary>
	/// Reproduces GitHub Issue #6: a FLAC file with a native PICTURE block must
	/// surface that picture through the unified <see cref="FlacFile.Tag"/> view.
	/// </summary>
	/// <remarks>
	/// Per RFC 9639 §8.8 and https://xiph.org/flac/format.html#metadata_block_picture,
	/// the PICTURE metadata block (type 6) is the canonical picture storage for FLAC.
	/// Prior to this fix, <c>FlacFile.Tag</c> returned the raw VorbisComment, whose
	/// Pictures property only reflects METADATA_BLOCK_PICTURE entries, missing the
	/// native PICTURE block.
	/// </remarks>
	[TestMethod]
	public void Tag_Pictures_IncludesNativePictureBlocks ()
	{
		var data = TestBuilders.Flac.CreateWithPicture (PictureType.FrontCover);

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Pictures.Count, "precondition: file has one PICTURE block");
		Assert.IsNotNull (result.File.Tag, "Tag view must be available");
		Assert.AreEqual (1, result.File.Tag!.Pictures.Length,
			"Tag.Pictures must surface native PICTURE blocks (Issue #6 / RFC 9639 §8.8)");
	}

	/// <summary>
	/// A FLAC file whose picture is embedded as a METADATA_BLOCK_PICTURE field inside
	/// the VORBIS_COMMENT block (with no separate PICTURE metadata block) must still
	/// surface the picture through <see cref="FlacFile.Tag"/>.
	/// </summary>
	/// <remarks>
	/// Per https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE, pictures may be
	/// embedded directly in the Vorbis comment as base64-encoded METADATA_BLOCK_PICTURE
	/// fields whose decoded payload is identical to a FLAC PICTURE block body. This is
	/// the canonical storage for Ogg Vorbis/Opus and is legal (though discouraged) inside
	/// FLAC files written by older or Ogg-oriented tools.
	/// </remarks>
	[TestMethod]
	public void Tag_Pictures_IncludesMetadataBlockPictureFromVorbisComment ()
	{
		var data = BuildFlacWithEmbeddedPicture ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.File!.Pictures.Count,
			"precondition: file has no native PICTURE blocks");
		Assert.IsNotNull (result.File.VorbisComment);
		Assert.AreEqual (1, result.File.VorbisComment!.Pictures.Length,
			"precondition: picture is in VorbisComment via METADATA_BLOCK_PICTURE");
		Assert.AreEqual (1, result.File.Tag!.Pictures.Length,
			"Tag.Pictures must surface METADATA_BLOCK_PICTURE entries (Vorbis Comment spec)");
	}

	/// <summary>
	/// Setting <see cref="FlacFile.Tag"/>.Pictures must write to the canonical FLAC
	/// PICTURE metadata blocks (per RFC 9639 §8.8), not the legacy METADATA_BLOCK_PICTURE
	/// entries in the VorbisComment block. The FLAC spec defines PICTURE blocks as the
	/// native picture storage for FLAC; METADATA_BLOCK_PICTURE exists primarily for
	/// Ogg Vorbis/Opus containers that lack metadata blocks.
	/// </summary>
	[TestMethod]
	public void Tag_SetPictures_WritesToNativePictureBlocks ()
	{
		var data = TestBuilders.Flac.CreateMinimal ();
		var file = FlacFile.Read (data).File!;

		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (new byte[] { 0xFF, 0xD8, 0xCA, 0xFE }), 10, 10, 24, 0);
		file.Tag!.Pictures = [picture];

		Assert.AreEqual (1, file.Pictures.Count,
			"Tag.Pictures setter must write to native PICTURE blocks (RFC 9639 §8.8)");
		Assert.AreEqual (PictureType.FrontCover, file.Pictures[0].PictureType);
	}

	/// <summary>
	/// After setting <see cref="FlacFile.Tag"/>.Pictures, any pre-existing
	/// METADATA_BLOCK_PICTURE entries in the VorbisComment must be stripped so that
	/// Tag.Pictures has a single source of truth (the PICTURE metadata blocks) and
	/// the file does not carry duplicated/divergent picture data after a round-trip.
	/// </summary>
	[TestMethod]
	public void Tag_SetPictures_StripsLegacyMetadataBlockPictureEntries ()
	{
		var seed = TestBuilders.Flac.CreateWithVorbisComment ("seed", "seed");
		var file = FlacFile.Read (seed).File!;
		file.VorbisComment!.Pictures = [new FlacPicture ("image/jpeg", PictureType.BackCover, "",
			new BinaryData (new byte[] { 0xDE, 0xAD }), 10, 10, 24, 0)];
		Assert.AreEqual (1, file.VorbisComment.Pictures.Length, "precondition: embedded picture present");

		file.Tag!.Pictures = [new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (new byte[] { 0xBE, 0xEF }), 10, 10, 24, 0)];

		Assert.AreEqual (0, file.VorbisComment.Pictures.Length,
			"Legacy METADATA_BLOCK_PICTURE entries must be cleared when Tag.Pictures is set");
		Assert.AreEqual (1, file.Pictures.Count, "New picture lives in native PICTURE block");
	}

	/// <summary>
	/// Text fields read through <see cref="FlacFile.Tag"/> reflect the values stored
	/// in the VorbisComment, since the Vorbis comment block is the only tag-field
	/// storage defined for FLAC (RFC 9639 §8.6). Writes must also round-trip through
	/// the VorbisComment.
	/// </summary>
	[TestMethod]
	public void Tag_TextFields_ReadAndWriteThroughVorbisComment ()
	{
		var data = TestBuilders.Flac.CreateWithVorbisComment ("Original Title", "Original Artist");
		var file = FlacFile.Read (data).File!;

		Assert.AreEqual ("Original Title", file.Tag!.Title);
		Assert.AreEqual ("Original Artist", file.Tag.Artist);

		file.Tag.Title = "New Title";
		file.Tag.Album = "New Album";

		Assert.AreEqual ("New Title", file.VorbisComment!.Title);
		Assert.AreEqual ("New Album", file.VorbisComment.Album);
	}

	/// <summary>
	/// When the same picture appears in both a native PICTURE block and as a
	/// METADATA_BLOCK_PICTURE entry in the VorbisComment, the unified Tag view
	/// must deduplicate — the caller should see one logical picture, not two.
	/// </summary>
	/// <remarks>
	/// This duplicated-storage state is a real-world pattern: some older taggers
	/// write to both locations for compatibility, and modern libraries (TagLib,
	/// Mutagen, Jaudiotagger) all dedupe on read so callers see one entry per
	/// logical picture. The dedup key is (PictureType, MimeType, PictureData).
	/// </remarks>
	[TestMethod]
	public void Tag_Pictures_DedupesWhenBothSourcesHoldSamePicture ()
	{
		var data = BuildFlacWithPictureInBothLocations ();

		var result = FlacFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.File!.Pictures.Count, "precondition: one native PICTURE block");
		Assert.AreEqual (1, result.File.VorbisComment!.Pictures.Length,
			"precondition: one METADATA_BLOCK_PICTURE entry");
		Assert.AreEqual (1, result.File.Tag!.Pictures.Length,
			"identical pictures in both storage locations must dedupe to one");
	}

	/// <summary>
	/// When distinct pictures are present in both storage locations, the unified
	/// Tag view returns all of them. Dedup applies only to identical content.
	/// </summary>
	[TestMethod]
	public void Tag_Pictures_UnionsDistinctPicturesFromBothSources ()
	{
		var seed = TestBuilders.Flac.CreateWithVorbisComment ("seed", "seed");
		var file = FlacFile.Read (seed).File!;

		file.VorbisComment!.Pictures = [new FlacPicture ("image/jpeg", PictureType.BackCover, "",
			new BinaryData (new byte[] { 0xCA, 0xFE }), 10, 10, 24, 0)];
		file.AddPicture (new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (new byte[] { 0xFE, 0xED }), 10, 10, 24, 0));

		var rendered = file.Render (seed).ToArray ();
		var result = FlacFile.Read (rendered);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.File!.Tag!.Pictures.Length,
			"Distinct pictures from both storage locations must union to 2");
	}

	/// <summary>
	/// Integration: direct reproduction of GitHub Issue #6. Reading a FLAC file via
	/// <see cref="MediaFile.ReadFromData"/> must yield a <see cref="MediaFileResult"/>
	/// whose <c>Tag.Pictures</c> matches the underlying file's pictures.
	/// </summary>
	[TestMethod]
	public void MediaFile_ReadFromData_Flac_TagPicturesMatchFilePictures ()
	{
		var data = TestBuilders.Flac.CreateWithPicture (PictureType.FrontCover);

		var result = MediaFile.ReadFromData (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);
		Assert.IsNotNull (result.Tag);

		var flac = (FlacFile)result.File!;
		Assert.AreEqual (flac.Pictures.Count, result.Tag!.Pictures.Length,
			"MediaFileResult.Tag.Pictures must match File.Pictures (Issue #6)");
	}

	static byte[] BuildFlacWithPictureInBothLocations ()
	{
		var seed = TestBuilders.Flac.CreateWithVorbisComment ("seed", "seed");
		var file = FlacFile.Read (seed).File!;

		var pictureBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 };
		file.VorbisComment!.Pictures = [new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (pictureBytes), 10, 10, 24, 0)];
		file.AddPicture (new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (pictureBytes), 10, 10, 24, 0));

		return file.Render (seed).ToArray ();
	}

	static byte[] BuildFlacWithEmbeddedPicture ()
	{
		// Start from a valid FLAC with a VorbisComment, attach an in-comment picture,
		// then round-trip through Render so the result uses METADATA_BLOCK_PICTURE
		// (per VorbisComment spec) rather than a separate PICTURE metadata block.
		var seed = TestBuilders.Flac.CreateWithVorbisComment ("seed", "seed");
		var seedRead = FlacFile.Read (seed);
		Assert.IsTrue (seedRead.IsSuccess);

		var picture = new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), 10, 10, 24, 0);
		seedRead.File!.VorbisComment!.Pictures = [picture];

		return seedRead.File.Render (seed).ToArray ();
	}
}
