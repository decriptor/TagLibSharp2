// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Tests for <see cref="CombinedTag"/> — a Tag facade that exposes a unified view
/// over multiple underlying Tag instances in a well-defined priority order.
/// </summary>
/// <remarks>
/// The motivating case is an MP3 file that contains both an ID3v2 tag and an
/// ID3v1 tag. Per the ID3v1 specification (https://id3.org/ID3v1), ID3v1 has
/// strict field length limits (title/artist/album capped at 30 bytes, year at
/// 4 bytes, etc.). ID3v2 has no such limits. When both are present, taggers
/// generally treat ID3v2 as authoritative and ID3v1 as a legacy-compat copy,
/// so reads must prefer ID3v2 and fall back to ID3v1 for fields missing in v2.
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Core")]
public class CombinedTagTests
{
	/// <summary>
	/// When the primary tag has a field value, that value wins regardless of what
	/// later tags hold. This implements the "ID3v2 is authoritative" convention.
	/// </summary>
	[TestMethod]
	public void Title_PrimaryTagValueWinsOverSecondary ()
	{
		var primary = new Id3v2Tag { Title = "From Primary" };
		var secondary = new Id3v1Tag { Title = "From Secondary" };

		var combined = new CombinedTag (primary, secondary);

		Assert.AreEqual ("From Primary", combined.Title);
	}

	/// <summary>
	/// When the primary tag does not have a value for a field, the combined tag
	/// falls back to later tags. This prevents ID3v1-only information (typical
	/// for files tagged prior to widespread ID3v2 adoption) from being lost.
	/// </summary>
	[TestMethod]
	public void Title_FallsBackToSecondaryWhenPrimaryIsEmpty ()
	{
		var primary = new Id3v2Tag ();
		var secondary = new Id3v1Tag { Title = "Only In Secondary" };

		var combined = new CombinedTag (primary, secondary);

		Assert.AreEqual ("Only In Secondary", combined.Title);
	}

	/// <summary>
	/// A null tag in the priority list is skipped. This supports the common
	/// case where a file is being read and one of the tag blocks is absent.
	/// </summary>
	[TestMethod]
	public void Title_SkipsNullTagsInPriorityList ()
	{
		var secondary = new Id3v1Tag { Title = "From Secondary" };

		var combined = new CombinedTag (null, secondary);

		Assert.AreEqual ("From Secondary", combined.Title);
	}

	/// <summary>
	/// All string fields follow the same first-non-empty selection across the
	/// priority list. This test locks in the uniform behavior rather than
	/// relying on only Title/Genre from earlier tests.
	/// </summary>
	[TestMethod]
	public void AllStringFields_ReturnFirstNonEmptyValueAcrossTags ()
	{
		var primary = new Id3v2Tag { Artist = "Primary Artist" };
		var secondary = new Id3v1Tag {
			Album = "Secondary Album",
			Year = "2001",
			Comment = "Secondary Comment",
			Genre = "Rock", // must be a valid ID3v1 genre (https://id3.org/ID3v1)
		};

		var combined = new CombinedTag (primary, secondary);

		Assert.AreEqual ("Primary Artist", combined.Artist, "Artist from primary");
		Assert.AreEqual ("Secondary Album", combined.Album, "Album falls back to secondary");
		Assert.AreEqual ("2001", combined.Year, "Year falls back to secondary");
		Assert.AreEqual ("Secondary Comment", combined.Comment, "Comment falls back to secondary");
		Assert.AreEqual ("Rock", combined.Genre, "Genre falls back to secondary");
	}

	/// <summary>
	/// Track (uint?) follows the same first-non-null pattern as string fields.
	/// </summary>
	[TestMethod]
	public void Track_ReturnsFirstNonNullValueAcrossTags ()
	{
		var primary = new Id3v2Tag ();
		var secondary = new Id3v1Tag { Track = 7 };

		var combined = new CombinedTag (primary, secondary);

		Assert.AreEqual ((uint)7, combined.Track);
	}

	/// <summary>
	/// <see cref="CombinedTag.TagType"/> aggregates the tag type flags of every
	/// non-null member. This lets callers check "does this file have Xiph data?"
	/// via a single bitwise query.
	/// </summary>
	[TestMethod]
	public void TagType_AggregatesFlagsAcrossMembers ()
	{
		var combined = new CombinedTag (new Id3v2Tag (), new Id3v1Tag ());

		Assert.AreEqual (TagTypes.Id3v2 | TagTypes.Id3v1, combined.TagType);
	}

	/// <summary>
	/// <see cref="CombinedTag.Pictures"/> unions pictures across member tags and
	/// deduplicates on (PictureType, MimeType, PictureData). The dedup behavior
	/// mirrors FlacTag's and is the library convention (see also TagLib C++,
	/// Jaudiotagger).
	/// </summary>
	[TestMethod]
	public void Pictures_UnionsAcrossTagsWithDedup ()
	{
		var sharedBytes = new byte[] { 0x01, 0x02 };
		var primary = new Id3v2Tag ();
		primary.Pictures = [new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
			new BinaryData (sharedBytes), 0, 0, 0, 0)];
		var secondary = new Id3v2Tag ();
		secondary.Pictures = [
			new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
				new BinaryData (sharedBytes), 0, 0, 0, 0),
			new FlacPicture ("image/jpeg", PictureType.BackCover, "",
				new BinaryData (new byte[] { 0x03 }), 0, 0, 0, 0),
		];

		var combined = new CombinedTag (primary, secondary);

		Assert.AreEqual (2, combined.Pictures.Length,
			"duplicate front cover across tags dedupes; distinct back cover remains");
	}

	/// <summary>
	/// The base <see cref="CombinedTag"/> exposes writes as no-ops so that it
	/// cannot accidentally mutate underlying tags without a subclass making an
	/// explicit decision. Format-specific subclasses override individual
	/// setters when write-through is the correct behavior.
	/// </summary>
	[TestMethod]
	public void Setters_AreNoOpsOnBaseFacade ()
	{
		var primary = new Id3v2Tag { Title = "Original" };
		var combined = new CombinedTag (primary);

		combined.Title = "Changed";
		combined.Track = 42;
		combined.Pictures = [];

		Assert.AreEqual ("Original", primary.Title, "Base CombinedTag.Title setter does not mutate members");
		Assert.IsNull (primary.Track, "Base CombinedTag.Track setter does not mutate members");
	}

	[TestMethod]
	public void Render_ReturnsEmptyBinaryData ()
	{
		var combined = new CombinedTag (new Id3v2Tag { Title = "x" });

		var rendered = combined.Render ();

		Assert.IsTrue (rendered.IsEmpty,
			"CombinedTag is a view; it does not own bytes and Render returns empty");
	}

	[TestMethod]
	public void Clear_IsNoOp ()
	{
		var primary = new Id3v2Tag { Title = "Keep Me" };
		var combined = new CombinedTag (primary);

		combined.Clear ();

		Assert.AreEqual ("Keep Me", primary.Title,
			"Base CombinedTag.Clear does not mutate members");
	}
}
