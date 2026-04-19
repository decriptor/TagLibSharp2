// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;

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
}
