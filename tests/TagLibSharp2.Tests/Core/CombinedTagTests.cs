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
	/// Setters write through to every non-null underlying tag so content stays
	/// consistent across formats. For MP3, that means a single
	/// <c>file.Tag.Title = "x"</c> updates both ID3v2 and ID3v1, matching the
	/// round-trip expectation that saving a file emits matching values in both.
	/// Per-format length/encoding limits (e.g. ID3v1's 30-byte cap,
	/// https://id3.org/ID3v1) still apply when each member stores the value.
	/// </summary>
	[TestMethod]
	public void Setters_WriteThroughToEveryMember ()
	{
		var primary = new Id3v2Tag { Title = "Original 2", Track = 1 };
		var secondary = new Id3v1Tag { Title = "Original 1", Track = 2 };
		var combined = new CombinedTag (primary, secondary);

		combined.Title = "Changed";
		combined.Track = 42;

		Assert.AreEqual ("Changed", primary.Title);
		Assert.AreEqual ("Changed", secondary.Title);
		Assert.AreEqual ((uint)42, primary.Track);
		Assert.AreEqual ((uint)42, secondary.Track);
	}

	/// <summary>
	/// A <see cref="CombinedTag"/> is a view — it has no standalone binary
	/// representation. Callers should render the owning file or a specific
	/// underlying tag; calling <see cref="Tag.Render"/> on the facade throws
	/// to surface the misuse instead of silently returning empty bytes.
	/// </summary>
	[TestMethod]
	public void Render_ThrowsNotSupported ()
	{
		var combined = new CombinedTag (new Id3v2Tag { Title = "x" });

		Assert.ThrowsExactly<NotSupportedException> (() => combined.Render ());
	}

	/// <summary>
	/// <see cref="Tag.Clear"/> on the facade clears every non-null underlying
	/// tag. Callers expect "clear the tag" to actually zero out the metadata,
	/// not silently succeed with the old values intact.
	/// </summary>
	[TestMethod]
	public void Clear_ClearsEveryUnderlyingMember ()
	{
		var primary = new Id3v2Tag { Title = "Wipe Me" };
		var secondary = new Id3v1Tag { Title = "Also Wipe Me" };
		var combined = new CombinedTag (primary, secondary);

		combined.Clear ();

		Assert.IsTrue (string.IsNullOrEmpty (primary.Title));
		Assert.IsTrue (string.IsNullOrEmpty (secondary.Title));
	}
}
