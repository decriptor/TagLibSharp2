// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class Id3v2TagChapterTests
{
	[TestMethod]
	public void ChapterFrames_EmptyByDefault ()
	{
		var tag = new Id3v2Tag ();

		Assert.IsEmpty (tag.ChapterFrames);
		Assert.IsEmpty (tag.TableOfContentsFrames);
	}

	[TestMethod]
	public void AddChapter_AddsToCollection ()
	{
		var tag = new Id3v2Tag ();
		var chapter = new ChapterFrame ("chp1", 0, 60000) {
			Title = "Introduction"
		};

		tag.AddChapter (chapter);

		Assert.HasCount (1, tag.ChapterFrames);
		Assert.AreEqual ("chp1", tag.ChapterFrames[0].ElementId);
		Assert.AreEqual ("Introduction", tag.ChapterFrames[0].Title);
	}

	[TestMethod]
	public void AddTableOfContents_AddsToCollection ()
	{
		var tag = new Id3v2Tag ();
		var toc = new TableOfContentsFrame ("toc") {
			IsTopLevel = true,
			IsOrdered = true
		};
		toc.AddChildElement ("chp1");
		toc.AddChildElement ("chp2");

		tag.AddTableOfContents (toc);

		Assert.HasCount (1, tag.TableOfContentsFrames);
		Assert.AreEqual ("toc", tag.TableOfContentsFrames[0].ElementId);
		Assert.IsTrue (tag.TableOfContentsFrames[0].IsTopLevel);
		Assert.HasCount (2, tag.TableOfContentsFrames[0].ChildElementIds);
	}

	[TestMethod]
	public void GetChapter_ReturnsFirstByDefault ()
	{
		var tag = new Id3v2Tag ();
		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000));
		tag.AddChapter (new ChapterFrame ("chp2", 60000, 120000));

		var chapter = tag.GetChapter ();

		Assert.IsNotNull (chapter);
		Assert.AreEqual ("chp1", chapter.ElementId);
	}

	[TestMethod]
	public void GetChapter_FindsByElementId ()
	{
		var tag = new Id3v2Tag ();
		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000));
		tag.AddChapter (new ChapterFrame ("chp2", 60000, 120000));

		var chapter = tag.GetChapter ("chp2");

		Assert.IsNotNull (chapter);
		Assert.AreEqual ("chp2", chapter.ElementId);
		Assert.AreEqual ((uint)60000, chapter.StartTimeMs);
	}

	[TestMethod]
	public void GetChapter_ReturnsNullWhenNotFound ()
	{
		var tag = new Id3v2Tag ();
		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000));

		var chapter = tag.GetChapter ("nonexistent");

		Assert.IsNull (chapter);
	}

	[TestMethod]
	public void GetTableOfContents_ReturnsTopLevel ()
	{
		var tag = new Id3v2Tag ();
		var nested = new TableOfContentsFrame ("nested") { IsTopLevel = false };
		var topLevel = new TableOfContentsFrame ("toc") { IsTopLevel = true };

		tag.AddTableOfContents (nested);
		tag.AddTableOfContents (topLevel);

		var toc = tag.GetTableOfContents ();

		Assert.IsNotNull (toc);
		Assert.AreEqual ("toc", toc.ElementId);
		Assert.IsTrue (toc.IsTopLevel);
	}

	[TestMethod]
	public void GetTableOfContents_FallsBackToFirst ()
	{
		var tag = new Id3v2Tag ();
		var toc1 = new TableOfContentsFrame ("toc1") { IsTopLevel = false };
		var toc2 = new TableOfContentsFrame ("toc2") { IsTopLevel = false };

		tag.AddTableOfContents (toc1);
		tag.AddTableOfContents (toc2);

		var toc = tag.GetTableOfContents ();

		Assert.IsNotNull (toc);
		Assert.AreEqual ("toc1", toc.ElementId);
	}

	[TestMethod]
	public void GetTableOfContentsById_FindsByElementId ()
	{
		var tag = new Id3v2Tag ();
		tag.AddTableOfContents (new TableOfContentsFrame ("toc1"));
		tag.AddTableOfContents (new TableOfContentsFrame ("toc2"));

		var toc = tag.GetTableOfContentsById ("toc2");

		Assert.IsNotNull (toc);
		Assert.AreEqual ("toc2", toc.ElementId);
	}

	[TestMethod]
	public void RemoveChapters_RemovesByElementId ()
	{
		var tag = new Id3v2Tag ();
		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000));
		tag.AddChapter (new ChapterFrame ("chp2", 60000, 120000));

		tag.RemoveChapters ("chp1");

		Assert.HasCount (1, tag.ChapterFrames);
		Assert.AreEqual ("chp2", tag.ChapterFrames[0].ElementId);
	}

	[TestMethod]
	public void RemoveChapters_RemovesAll ()
	{
		var tag = new Id3v2Tag ();
		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000));
		tag.AddChapter (new ChapterFrame ("chp2", 60000, 120000));

		tag.RemoveChapters ();

		Assert.IsEmpty (tag.ChapterFrames);
	}

	[TestMethod]
	public void RemoveTableOfContents_RemovesByElementId ()
	{
		var tag = new Id3v2Tag ();
		tag.AddTableOfContents (new TableOfContentsFrame ("toc1"));
		tag.AddTableOfContents (new TableOfContentsFrame ("toc2"));

		tag.RemoveTableOfContents ("toc1");

		Assert.HasCount (1, tag.TableOfContentsFrames);
		Assert.AreEqual ("toc2", tag.TableOfContentsFrames[0].ElementId);
	}

	[TestMethod]
	public void RemoveTableOfContents_RemovesAll ()
	{
		var tag = new Id3v2Tag ();
		tag.AddTableOfContents (new TableOfContentsFrame ("toc1"));
		tag.AddTableOfContents (new TableOfContentsFrame ("toc2"));

		tag.RemoveTableOfContents ();

		Assert.IsEmpty (tag.TableOfContentsFrames);
	}

	[TestMethod]
	public void Clear_ClearsChaptersAndTableOfContents ()
	{
		var tag = new Id3v2Tag ();
		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000));
		tag.AddTableOfContents (new TableOfContentsFrame ("toc"));

		tag.Clear ();

		Assert.IsEmpty (tag.ChapterFrames);
		Assert.IsEmpty (tag.TableOfContentsFrames);
	}

	[TestMethod]
	public void RenderAndRead_PreservesChapters ()
	{
		var tag = new Id3v2Tag ();
		var toc = new TableOfContentsFrame ("toc") {
			IsTopLevel = true,
			IsOrdered = true
		};
		toc.AddChildElement ("chp1");
		toc.AddChildElement ("chp2");
		tag.AddTableOfContents (toc);

		tag.AddChapter (new ChapterFrame ("chp1", 0, 60000) { Title = "Intro" });
		tag.AddChapter (new ChapterFrame ("chp2", 60000, 120000) { Title = "Main" });

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		var parsedTag = result.Tag!;

		// Verify table of contents
		Assert.HasCount (1, parsedTag.TableOfContentsFrames);
		var parsedToc = parsedTag.GetTableOfContents ();
		Assert.IsNotNull (parsedToc);
		Assert.AreEqual ("toc", parsedToc.ElementId);
		Assert.IsTrue (parsedToc.IsTopLevel);
		Assert.IsTrue (parsedToc.IsOrdered);
		Assert.HasCount (2, parsedToc.ChildElementIds);

		// Verify chapters
		Assert.HasCount (2, parsedTag.ChapterFrames);
		var chp1 = parsedTag.GetChapter ("chp1");
		Assert.IsNotNull (chp1);
		Assert.AreEqual ((uint)0, chp1.StartTimeMs);
		Assert.AreEqual ((uint)60000, chp1.EndTimeMs);
		Assert.AreEqual ("Intro", chp1.Title);

		var chp2 = parsedTag.GetChapter ("chp2");
		Assert.IsNotNull (chp2);
		Assert.AreEqual ((uint)60000, chp2.StartTimeMs);
		Assert.AreEqual ((uint)120000, chp2.EndTimeMs);
		Assert.AreEqual ("Main", chp2.Title);
	}

	[TestMethod]
	public void RenderAndRead_PreservesByteOffsets ()
	{
		var tag = new Id3v2Tag ();
		var chapter = new ChapterFrame ("chp1", 0, 60000) {
			StartByteOffset = 1000,
			EndByteOffset = 50000
		};
		tag.AddChapter (chapter);

		var rendered = tag.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		var parsedChapter = result.Tag!.GetChapter ("chp1");
		Assert.IsNotNull (parsedChapter);
		Assert.AreEqual ((uint)1000, parsedChapter.StartByteOffset);
		Assert.AreEqual ((uint)50000, parsedChapter.EndByteOffset);
	}
}
