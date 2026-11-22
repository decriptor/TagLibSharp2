// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Reading a UTF-16 text frame allocates the frame and its strings, and nothing per string on the way
/// (CLAUDE.md, "Span&lt;T&gt; first: ... avoid allocations").
/// </summary>
/// <remarks>
/// The bound is measured on this thread with <see cref="GC.GetAllocatedBytesForCurrentThread"/>, against building
/// the same objects directly, so it does not depend on machine load or on other threads.
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3v2")]
public class Utf16AllocationTests
{
	const int Reads = 2000;

	// What a read may spend beyond the frame and its strings: none of the frame's text, per string, is copied.
	const long SlackPerRead = 8;

	static byte[] Utf16 (string text) => [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes (text)];

	public static IEnumerable<object[]> Values => [
		["a"],
		["eighteen characters"],
		["a\0b"],
		["eighteen characters\0another eighteen c"],
		["x\0\0y\0z"],
		["long"],
	];

	// A value too long for the stack buffer, so the pooled path is measured too.
	static string Expand (string value) =>
		value == "long" ? new string ('L', 600) + "\0" + new string ('M', 600) : value;

	/// <summary>One BOM per string, the strings separated by 00 00.</summary>
	static byte[] Encode (string value)
	{
		var bytes = new List<byte> ();
		var parts = value.Split ('\0');
		for (var i = 0; i < parts.Length; i++) {
			if (i > 0)
				bytes.AddRange (new byte[] { 0, 0 });
			bytes.AddRange (Utf16 (parts[i]));
		}
		return [.. bytes];
	}

	static long PerCall (Action action)
	{
		for (var i = 0; i < 50; i++)
			action ();
		var before = GC.GetAllocatedBytesForCurrentThread ();
		for (var i = 0; i < Reads; i++)
			action ();
		return (GC.GetAllocatedBytesForCurrentThread () - before) / Reads;
	}

	[TestMethod]
	[DynamicData (nameof (Values))]
	public void TextFrameRead_AllocatesOnlyTheFrameAndItsText (string name)
	{
		var value = Expand (name);
		byte[] content = [0x01, .. Encode (value)];
		Assert.AreEqual (value, TextFrame.Read ("TPE1", content, Id3v2Version.V24).Frame!.Text);

		var read = PerCall (() => TextFrame.Read ("TPE1", content, Id3v2Version.V24));
		var direct = PerCall (() => _ = new TextFrame ("TPE1", value.AsSpan ().ToString (), TextEncodingType.Utf16WithBom));

		Assert.IsTrue (read <= direct + SlackPerRead, $"read allocates {read} B, the frame and its text are {direct} B");
	}

	[TestMethod]
	[DynamicData (nameof (Values))]
	public void UserTextFrameRead_AllocatesOnlyTheFrameAndItsStrings (string name)
	{
		var value = Expand (name);
		byte[] content = [0x01, .. Utf16 ("MyField"), 0, 0, .. Encode (value)];
		Assert.AreEqual (value, UserTextFrame.Read (content, Id3v2Version.V24).Frame!.Value);

		var read = PerCall (() => UserTextFrame.Read (content, Id3v2Version.V24));
		var direct = PerCall (() => _ = new UserTextFrame ("MyField".AsSpan ().ToString (), value.AsSpan ().ToString (),
			TextEncodingType.Utf16WithBom));

		Assert.IsTrue (read <= direct + SlackPerRead, $"read allocates {read} B, the frame and its strings are {direct} B");
	}
}
