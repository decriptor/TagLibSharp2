// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>The image tag on its own: clearing and rendering the stored fields.</summary>
[TestClass]
[TestCategory ("Unit")]
public class ImageTagTests
{
	[TestMethod]
	public void Tag_ClearAndRender_RoundTripTheStoredFields ()
	{
		var file = Open (TitledPng ("Door"), "photo.png");
		var tag = file.Tag!;
		tag.Artist = "Ada";
		tag.Album = "Room";
		tag.Genre = "Rock";
		tag.Track = 2;
		tag.Genre = "";
		tag.Track = null;
		Assert.IsNull (tag.Genre);
		Assert.IsNull (tag.Track);
		Assert.IsNull (tag.Album);
		Assert.IsFalse (tag.IsEmpty);

		var rendered = tag.Render ().ToArray ();
		Assert.IsTrue (Encoding.Latin1.GetString (rendered).Contains ("Door"));

		tag.Clear ();
		Assert.IsTrue (tag.IsEmpty);
		Assert.IsNull (tag.Title);
		Assert.IsNull (tag.Album);
		var cleared = file.Render ().ToArray ();
		Assert.IsNull (Open (cleared, "photo.png").Tag!.Title);
	}
}
