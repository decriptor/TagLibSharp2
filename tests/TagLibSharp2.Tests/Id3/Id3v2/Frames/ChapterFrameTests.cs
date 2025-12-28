// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class ChapterFrameTests
{
	[TestMethod]
	public void Constructor_SetsProperties ()
	{
		var chapter = new ChapterFrame ("chp1", 0, 60000) {
			Title = "Introduction"
		};

		Assert.AreEqual ("chp1", chapter.ElementId);
		Assert.AreEqual ((uint)0, chapter.StartTimeMs);
		Assert.AreEqual ((uint)60000, chapter.EndTimeMs);
		Assert.AreEqual ("Introduction", chapter.Title);
	}

	[TestMethod]
	public void FrameId_ReturnsCHAP ()
	{
		Assert.AreEqual ("CHAP", ChapterFrame.FrameId);
	}

	[TestMethod]
	public void Read_SimpleChapter_ParsesCorrectly ()
	{
		var data = BuildChapterFrame ("chp1", 0, 60000, 0xFFFFFFFF, 0xFFFFFFFF);

		var result = ChapterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("chp1", result.Frame!.ElementId);
		Assert.AreEqual ((uint)0, result.Frame.StartTimeMs);
		Assert.AreEqual ((uint)60000, result.Frame.EndTimeMs);
	}

	[TestMethod]
	public void Read_ChapterWithByteOffsets_ParsesCorrectly ()
	{
		var data = BuildChapterFrame ("chp2", 60000, 120000, 1000, 2000);

		var result = ChapterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((uint)60000, result.Frame!.StartTimeMs);
		Assert.AreEqual ((uint)1000, result.Frame.StartByteOffset);
		Assert.AreEqual ((uint)2000, result.Frame.EndByteOffset);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[10]; // Way too short

		var result = ChapterFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_NoNullTerminator_ReturnsFailure ()
	{
		// Just some bytes without null terminator
		var data = System.Text.Encoding.ASCII.GetBytes ("chp1chp1chp1");

		var result = ChapterFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var original = new ChapterFrame ("chapter1", 30000, 90000) {
			Title = "Chapter One",
			StartByteOffset = 5000,
			EndByteOffset = 15000
		};

		var rendered = original.RenderContent ();
		var result = ChapterFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("chapter1", result.Frame!.ElementId);
		Assert.AreEqual ((uint)30000, result.Frame.StartTimeMs);
		Assert.AreEqual ((uint)90000, result.Frame.EndTimeMs);
		Assert.AreEqual ((uint)5000, result.Frame.StartByteOffset);
		Assert.AreEqual ((uint)15000, result.Frame.EndByteOffset);
	}

	[TestMethod]
	public void RenderContent_WithTitle_IncludesEmbeddedTIT2 ()
	{
		var original = new ChapterFrame ("chp1", 0, 60000) {
			Title = "The Beginning"
		};

		var rendered = original.RenderContent ();
		var result = ChapterFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("The Beginning", result.Frame!.Title);
	}

	static byte[] BuildChapterFrame (string elementId, uint startMs, uint endMs, uint startOffset, uint endOffset)
	{
		using var builder = new BinaryDataBuilder ();

		// Element ID (null-terminated)
		builder.Add (System.Text.Encoding.Latin1.GetBytes (elementId));
		builder.Add ((byte)0x00);

		// Start time (4 bytes, big-endian)
		AddUInt32BE (builder, startMs);

		// End time (4 bytes, big-endian)
		AddUInt32BE (builder, endMs);

		// Start byte offset (4 bytes, big-endian)
		AddUInt32BE (builder, startOffset);

		// End byte offset (4 bytes, big-endian)
		AddUInt32BE (builder, endOffset);

		// No embedded frames for basic test

		return builder.ToBinaryData ().ToArray ();
	}

	static void AddUInt32BE (BinaryDataBuilder builder, uint value)
	{
		builder.Add ((byte)((value >> 24) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)(value & 0xFF));
	}
}

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class TableOfContentsFrameTests
{
	[TestMethod]
	public void Constructor_SetsProperties ()
	{
		var toc = new TableOfContentsFrame ("toc");

		Assert.AreEqual ("toc", toc.ElementId);
		Assert.IsFalse (toc.IsTopLevel);
		Assert.IsFalse (toc.IsOrdered);
		Assert.IsEmpty (toc.ChildElementIds);
	}

	[TestMethod]
	public void FrameId_ReturnsCTOC ()
	{
		Assert.AreEqual ("CTOC", TableOfContentsFrame.FrameId);
	}

	[TestMethod]
	public void AddChildElement_AddsId ()
	{
		var toc = new TableOfContentsFrame ("toc");

		toc.AddChildElement ("chp1");
		toc.AddChildElement ("chp2");

		Assert.HasCount (2, toc.ChildElementIds);
		Assert.AreEqual ("chp1", toc.ChildElementIds[0]);
		Assert.AreEqual ("chp2", toc.ChildElementIds[1]);
	}

	[TestMethod]
	public void Read_SimpleTableOfContents_ParsesCorrectly ()
	{
		var data = BuildTableOfContentsFrame ("toc", isTopLevel: true, isOrdered: true, "chp1", "chp2", "chp3");

		var result = TableOfContentsFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("toc", result.Frame!.ElementId);
		Assert.IsTrue (result.Frame.IsTopLevel);
		Assert.IsTrue (result.Frame.IsOrdered);
		Assert.HasCount (3, result.Frame.ChildElementIds);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[2]; // Way too short

		var result = TableOfContentsFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var original = new TableOfContentsFrame ("toc") {
			IsTopLevel = true,
			IsOrdered = true,
			Title = "Table of Contents"
		};
		original.AddChildElement ("chapter1");
		original.AddChildElement ("chapter2");

		var rendered = original.RenderContent ();
		var result = TableOfContentsFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("toc", result.Frame!.ElementId);
		Assert.IsTrue (result.Frame.IsTopLevel);
		Assert.IsTrue (result.Frame.IsOrdered);
		Assert.HasCount (2, result.Frame.ChildElementIds);
		Assert.AreEqual ("chapter1", result.Frame.ChildElementIds[0]);
		Assert.AreEqual ("chapter2", result.Frame.ChildElementIds[1]);
	}

	static byte[] BuildTableOfContentsFrame (string elementId, bool isTopLevel, bool isOrdered, params string[] childIds)
	{
		using var builder = new BinaryDataBuilder ();

		// Element ID (null-terminated)
		builder.Add (System.Text.Encoding.Latin1.GetBytes (elementId));
		builder.Add ((byte)0x00);

		// Flags: bit 1 = top level, bit 0 = ordered
		byte flags = 0;
		if (isTopLevel)
			flags |= 0x02;
		if (isOrdered)
			flags |= 0x01;
		builder.Add (flags);

		// Entry count (1 byte)
		builder.Add ((byte)childIds.Length);

		// Child element IDs (null-terminated strings)
		foreach (var childId in childIds) {
			builder.Add (System.Text.Encoding.Latin1.GetBytes (childId));
			builder.Add ((byte)0x00);
		}

		// No embedded frames for basic test

		return builder.ToBinaryData ().ToArray ();
	}
}
