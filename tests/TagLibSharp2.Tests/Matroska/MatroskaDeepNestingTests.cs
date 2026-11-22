// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

// A file controls how deep its elements nest. Reading or saving must not recurse once per level,
// or a small crafted file ends the process with a stack overflow that no caller can catch.
public partial class MatroskaFileTests
{
	const int HostileDepth = 100_000;

	[TestMethod]
	public void Save_CuePointsNestedDeep_DoesNotOverflowTheStack ()
	{
		var marker = new byte[] { 0xC1, 0xC2, 0xC3, 0xC4 };
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		var inner = Master (MatroskaIds.CueTrackPositions,
			Element (0xF7, new byte[] { 1 }),
			Element (MatroskaIds.CueClusterPosition, new byte[] { 0x10 }));
		var cues = Wide (MatroskaIds.Cues, Nested (MatroskaIds.CuePoint, HostileDepth, [], inner));
		var cluster = Unknown (0x1F43B675, Element (0xEC, marker));
		var original = DeepWideFile (info, cues, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('N', 400);

		var fs = new MockFileSystem ();
		fs.AddFile ("/deep-cues.mkv", original);
		var saved = parsed.SaveToFile ("/deep-cues.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var bytes = fs.ReadAllBytes ("/deep-cues.mkv");
		Assert.AreEqual (1, CountOf (bytes, marker));
		Assert.AreEqual (new string ('N', 400), MatroskaFile.Read (bytes).File!.Tag.Title);
	}

	[TestMethod]
	public void Save_CueClusterPositionOffTheSpecPath_IsLeftAsStored ()
	{
		// RFC 9559 §5.1.5 places CueClusterPosition only in Cues\CuePoint\CueTrackPositions.
		var marker = new byte[] { 0xC1, 0xC2, 0xC3, 0xC4 };
		var info = Master (0x1549A966, Element (0x7BA9, "Live"u8));
		var stray = Element (MatroskaIds.CueClusterPosition, new byte[] { 0x00, 0x7E });
		var cues = Master (MatroskaIds.Cues, Master (MatroskaIds.CuePoint, Element (0xB3, new byte[] { 0 }), stray));
		var cluster = Unknown (0x1F43B675, Element (0xEC, marker));
		var original = FileWith (info, cues, cluster);
		var parsed = MatroskaFile.Read (original).File!;
		parsed.Tag.Title = new string ('N', 400);

		var fs = new MockFileSystem ();
		fs.AddFile ("/stray.mkv", original);
		var saved = parsed.SaveToFile ("/stray.mkv", original, fs);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual (1, CountOf (fs.ReadAllBytes ("/stray.mkv"), stray));
	}

	[TestMethod]
	public void Read_SimpleTagsNestedDeep_DoesNotOverflowAndSaveKeepsTheNesting ()
	{
		var leaf = Master (0x67C8, Element (0x45A3, "LEAF"u8), Element (0x4487, "deepest"u8));
		var chain = Nested (0x67C8, HostileDepth, Element (0x45A3, "N"u8), leaf);
		var title = Wide (0x67C8, Concat (Element (0x45A3, "TITLE"u8), Element (0x4487, "Outer"u8), chain));
		var tags = Wide (0x1254C367, Wide (0x7373, Concat (Master (0x63C0, Element (0x68CA, new byte[] { 30 })), title)));
		var original = DeepWideFile (Master (0x1549A966, Element (0x7BA9, "Deep"u8)), tags);

		var read = MatroskaFile.Read (original);

		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.AreEqual ("Outer", read.File!.Tag.Title);

		read.File.Tag.Artist = "Someone";
		var rendered = read.File.Render (original).ToArray ();
		Assert.AreNotEqual (0, rendered.Length);
		Assert.AreEqual (1, Occurrences (rendered, KeptSlice (chain)), "the SimpleTags below the parse depth are written back as stored, once");
		Assert.AreEqual (HostileDepth, NestingBelowTitle (rendered));

		var reread = MatroskaFile.Read (rendered).File!;
		Assert.AreEqual ("Outer", reread.Tag.Title);
		Assert.AreEqual ("Someone", reread.Tag.Artist);
	}

	// TITLE is level 1 and each chain link is one level deeper, so the leaf sits at level depth + 2.
	// Levels up to MaxSimpleTagDepth are parsed and re-encoded with minimal sizes; anything deeper is
	// kept as stored. Every element here is written with an 8-octet size, so a parsed one changes bytes
	// and a kept one does not.
	[TestMethod]
	[DataRow (MatroskaFile.MaxSimpleTagDepth - 2, false)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth - 1, true)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth, true)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth + 1, true)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth + 5, true)]
	public void Save_SimpleTagsAtAndPastTheParseDepth_KeepOnlyTheDeeperOnesAsStored (int depth, bool kept)
	{
		var leaf = Wide (0x67C8, Concat (Element (0x45A3, "LEAF"u8), Element (0x4487, "v"u8)));
		var chain = Nested (0x67C8, depth, Element (0x45A3, "N"u8), leaf);
		var title = Wide (0x67C8, Concat (Element (0x45A3, "TITLE"u8), Element (0x4487, "Outer"u8), chain));
		var original = DeepWideFile (
			Master (0x1549A966, Element (0x7BA9, "Deep"u8)),
			Wide (0x1254C367, Wide (0x7373, Concat (Master (0x63C0, Element (0x68CA, new byte[] { 30 })), title))));
		var file = MatroskaFile.Read (original).File!;

		file.Tag.Artist = "Someone";
		var rendered = file.Render (original).ToArray ();

		Assert.AreEqual (depth, NestingBelowTitle (rendered));
		Assert.AreEqual (kept ? 1 : 0, Occurrences (rendered, leaf), "the leaf's stored bytes");
		if (kept)
			Assert.AreEqual (1, Occurrences (rendered, KeptSlice (chain)));
		Assert.IsTrue (MatroskaFile.Read (rendered).IsSuccess);
		Assert.AreEqual (1, Occurrences (rendered, Element (0x45A3, "LEAF"u8)), "the leaf is written once");
		Assert.AreEqual (1, Occurrences (rendered, Element (0x4487, "v"u8)), "with its value");
	}

	[TestMethod]
	[DataRow (MatroskaFile.MaxSimpleTagDepth - 1)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth + 1)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth + 5)]
	public void Save_SimpleTagsNestedPastTheParseDepth_AreByteIdenticalAfterAnEdit (int depth)
	{
		var leaf = Master (0x67C8, Element (0x45A3, "LEAF"u8), Element (0x4487, "v"u8));
		var chain = Nested (0x67C8, depth, Element (0x45A3, "N"u8), leaf);
		var title = Wide (0x67C8, Concat (Element (0x45A3, "TITLE"u8), Element (0x4487, "Outer"u8), chain));
		var original = DeepWideFile (
			Master (0x1549A966, Element (0x7BA9, "Deep"u8)),
			Wide (0x1254C367, Wide (0x7373, Concat (Master (0x63C0, Element (0x68CA, new byte[] { 30 })), title))));
		var file = MatroskaFile.Read (original).File!;

		file.Tag.Artist = "Someone";
		var rendered = MatroskaFile.Read (file.Render (original).ToArray ()).File!;
		rendered.Tag.Artist = null;
		var restored = rendered.Render (file.Render (original).ToArray ()).ToArray ();

		Assert.AreEqual (depth, NestingBelowTitle (restored));
		Assert.AreEqual (1, Occurrences (restored, KeptSlice (chain)), "the kept SimpleTags survive two saves byte for byte");
	}

	[TestMethod]
	[DataRow (MatroskaFile.MaxSimpleTagDepth + 1)]
	[DataRow (MatroskaFile.MaxSimpleTagDepth + 5)]
	public void Save_NewValueOnATagNestedPastTheParseDepth_KeepsItsNesting (int depth)
	{
		var leaf = Master (0x67C8, Element (0x45A3, "LEAF"u8), Element (0x4487, "v"u8));
		var chain = Nested (0x67C8, depth, Element (0x45A3, "N"u8), leaf);
		var title = Wide (0x67C8, Concat (Element (0x45A3, "TITLE"u8), Element (0x4487, "Outer"u8), chain));
		var original = DeepWideFile (
			Master (0x1549A966, Element (0x7BA9, "Deep"u8)),
			Wide (0x1254C367, Wide (0x7373, Concat (Master (0x63C0, Element (0x68CA, new byte[] { 30 })), title))));
		var file = MatroskaFile.Read (original).File!;

		file.Tag.Title = "Renamed";
		var rendered = file.Render (original).ToArray ();

		Assert.AreEqual ("Renamed", MatroskaFile.Read (rendered).File!.Tag.Title);
		Assert.AreEqual (depth, NestingBelowTitle (rendered));
		Assert.AreEqual (1, Occurrences (rendered, KeptSlice (chain)));
	}

	// The stored bytes of the first SimpleTag below the parse depth: chain link MaxSimpleTagDepth - 1
	// (link i is level i + 2), through the end of the chain, which is that link's whole subtree.
	static byte[] KeptSlice (byte[] chain)
	{
		var link = Id (0x67C8).Length + 8 + Element (0x45A3, "N"u8).Length;
		return chain.AsSpan ((MatroskaFile.MaxSimpleTagDepth - 1) * link).ToArray ();
	}

	static int Occurrences (byte[] data, byte[] pattern)
	{
		var count = 0;
		var span = data.AsSpan ();
		var at = span.IndexOf (pattern);
		while (at >= 0) {
			count++;
			span = span.Slice (at + 1);
			at = span.IndexOf (pattern);
		}

		return count;
	}

	// Counts the SimpleTags nested under the first TITLE SimpleTag by walking the bytes.
	static int NestingBelowTitle (byte[] file)
	{
		var title = file.AsSpan ().IndexOf ("TITLE"u8);
		Assert.IsTrue (title >= 0);
		var count = 0;
		var span = file.AsSpan (title);
		var name = new byte[] { 0x45, 0xA3, 0x81, (byte)'N' };
		var at = span.IndexOf (name);
		while (at >= 0) {
			count++;
			span = span.Slice (at + name.Length);
			at = span.IndexOf (name);
		}

		return count;
	}

	// Each level is the element ID, an 8-octet size (RFC 8794 §4.4), the prefix, then the next level.
	static byte[] Nested (ulong id, int depth, byte[] prefix, byte[] innermost)
	{
		var idBytes = Id (id);
		var level = idBytes.Length + 8 + prefix.Length;
		var result = new byte[(level * depth) + innermost.Length];
		for (var i = 0; i < depth; i++) {
			var at = i * level;
			idBytes.CopyTo (result, at);
			WriteWideSize (result, at + idBytes.Length, (ulong)(result.Length - at - level + prefix.Length));
			prefix.CopyTo (result, at + idBytes.Length + 8);
		}

		innermost.CopyTo (result, level * depth);
		return result;
	}

	static byte[] Wide (ulong id, byte[] payload)
	{
		var idBytes = Id (id);
		var result = new byte[idBytes.Length + 8 + payload.Length];
		idBytes.CopyTo (result, 0);
		WriteWideSize (result, idBytes.Length, (ulong)payload.Length);
		payload.CopyTo (result, idBytes.Length + 8);
		return result;
	}

	static byte[] DeepWideFile (params byte[][] segmentChildren) => Matroska (Wide (0x18538067, Concat (segmentChildren)));

	static void WriteWideSize (byte[] buffer, int at, ulong size)
	{
		buffer[at] = 0x01;
		for (var i = 7; i >= 1; i--) {
			buffer[at + i] = (byte)size;
			size >>= 8;
		}
	}
}
