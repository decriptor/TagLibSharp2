// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Encoding $01 text with several strings. ID3v2.4 §4 says the encoding is "UTF-16 encoded Unicode with BOM"
/// and that all strings in a frame share one byte order; ID3v2.3 §3.3 says Unicode strings begin with a BOM.
/// So each string may carry its own BOM, and none of them is part of the text.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3v2")]
public class Utf16MultiStringTests
{
	static readonly byte[] Le = [0xFF, 0xFE];
	static readonly byte[] Be = [0xFE, 0xFF];
	static readonly string[] FirstSecond = ["first", "second"];

	static byte[] Utf16 (string text, bool bigEndian) =>
		bigEndian ? System.Text.Encoding.BigEndianUnicode.GetBytes (text) : System.Text.Encoding.Unicode.GetBytes (text);

	/// <summary>Two strings, each optionally led by a BOM, separated by 00 00.</summary>
	static byte[] TwoStrings (bool bigEndian, bool bomOnSecond, bool trailingNul)
	{
		var bom = bigEndian ? Be : Le;
		byte[] second = bomOnSecond ? [.. bom, .. Utf16 ("second", bigEndian)] : Utf16 ("second", bigEndian);
		byte[] text = [.. bom, .. Utf16 ("first", bigEndian), 0, 0, .. second];
		return trailingNul ? [.. text, 0, 0] : text;
	}

	public static IEnumerable<object[]> Layouts => [
		[false, true, false],
		[true, true, false],
		[false, false, false],
		[true, false, false],
		[false, true, true],
		[true, true, true],
	];

	[TestMethod]
	[DynamicData (nameof (Layouts))]
	public void Txxx_EachStringsBom_IsNotPartOfTheValue (bool bigEndian, bool bomOnSecond, bool trailingNul)
	{
		byte[] content = [0x01, .. Le, .. Utf16 ("MyField", false), 0, 0, .. TwoStrings (bigEndian, bomOnSecond, trailingNul)];

		foreach (var version in new byte[] { 3, 4 }) {
			var tag = ReadTag (version, TestBuilders.Id3v2.CreateFrame ("TXXX", content, version));

			CollectionAssert.AreEqual (FirstSecond, tag.GetCustomField ("MyField").ToArray ());
		}
	}

	[TestMethod]
	[DynamicData (nameof (Layouts))]
	public void TextFrame_EachStringsBom_IsNotPartOfTheValue (bool bigEndian, bool bomOnSecond, bool trailingNul)
	{
		byte[] content = [0x01, .. TwoStrings (bigEndian, bomOnSecond, trailingNul)];

		var tag = ReadTag (4, TestBuilders.Id3v2.CreateFrame ("TPE1", content, 4));

		CollectionAssert.AreEqual (FirstSecond, tag.Performers);
	}

	[TestMethod]
	public void UserTextFrame_Read_StripsEveryBom ()
	{
		byte[] content = [0x01, .. Le, .. Utf16 ("D", false), 0, 0, .. TwoStrings (false, true, false)];

		var frame = UserTextFrame.Read (content, Id3v2Version.V24).Frame!;

		Assert.AreEqual ("first\0second", frame.Value);
	}

	[TestMethod]
	public void TextFrame_Read_StripsEveryBom ()
	{
		byte[] content = [0x01, .. TwoStrings (true, true, false)];

		var frame = TextFrame.Read ("TPE1", content, Id3v2Version.V24).Frame!;

		Assert.AreEqual ("first\0second", frame.Text);
	}

	[TestMethod]
	public void Id3v23_SetCustomField_WritesABomBeforeEachValue ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V23);

		tag.SetCustomField ("F", "first", "second");

		var content = tag.UserTextFrames[0].RenderContent ().ToArray ();
		byte[] expected = [0x01, .. Le, .. Utf16 ("F", false), 0, 0, .. Le, .. Utf16 ("first", false), 0, 0, .. Le, .. Utf16 ("second", false)];
		CollectionAssert.AreEqual (expected, content);
		var again = Id3v2Tag.Read (tag.Render ().Span).Tag!;
		CollectionAssert.AreEqual (FirstSecond, again.GetCustomField ("F").ToArray ());
	}

	[TestMethod]
	public void Utf16Frame_WithABomPerString_RendersTheSameBytes ()
	{
		byte[] txxx = [0x01, .. Le, .. Utf16 ("MyField", false), 0, 0, .. TwoStrings (false, true, false)];
		byte[] tpe1 = [0x01, .. TwoStrings (false, true, false)];

		Assert.IsTrue (UserTextFrame.Read (txxx, Id3v2Version.V24).Frame!.RenderContent ().Span.SequenceEqual (txxx));
		Assert.IsTrue (TextFrame.Read ("TPE1", tpe1, Id3v2Version.V24).Frame!.RenderContent ().Span.SequenceEqual (tpe1));
	}

	public static IEnumerable<object[]> ReadLayouts => [
		["one BOM per string", (byte[])[.. Le, .. Utf16 ("first", false), 0, 0, .. Le, .. Utf16 ("second", false)]],
		["one BOM per string, terminated", (byte[])[.. Le, .. Utf16 ("first", false), 0, 0, .. Le, .. Utf16 ("second", false), 0, 0]],
		["one BOM for the frame", (byte[])[.. Le, .. Utf16 ("first", false), 0, 0, .. Utf16 ("second", false)]],
		["one string, terminated", (byte[])[.. Le, .. Utf16 ("first/second", false), 0, 0]],
		["inner NUL, one BOM", (byte[])[.. Le, .. Utf16 ("T", false), 0, 0, .. Utf16 ("T", false)]],
		// mutagen's text=['a','','b']: every string, the empty one too, has a BOM.
		["empty middle string with a BOM", (byte[])[.. Le, .. Utf16 ("a", false), 0, 0, .. Le, 0, 0, .. Le, .. Utf16 ("b", false), 0, 0]],
		["empty middle string without a BOM", (byte[])[.. Le, .. Utf16 ("a", false), 0, 0, 0, 0, .. Le, .. Utf16 ("b", false)]],
		["two empty middle strings with BOMs", (byte[])[.. Le, .. Utf16 ("a", false), 0, 0, .. Le, 0, 0, .. Le, 0, 0, .. Le, .. Utf16 ("b", false)]],
		["empty first string", (byte[])[.. Le, 0, 0, .. Le, .. Utf16 ("b", false)]],
		// mutagen's text=['x','']: the empty last string has a BOM.
		["empty last string with a BOM", (byte[])[.. Le, .. Utf16 ("x", false), 0, 0, .. Le]],
		["empty last string with a BOM, terminated", (byte[])[.. Le, .. Utf16 ("x", false), 0, 0, .. Le, 0, 0]],
		["two empty strings with BOMs", (byte[])[.. Le, 0, 0, .. Le]],
		["one string, two terminators", (byte[])[.. Le, .. Utf16 ("x", false), 0, 0, 0, 0]],
	];

	[TestMethod]
	[DynamicData (nameof (ReadLayouts))]
	public void Utf16Frame_UnchangedRender_KeepsTheLayoutItWasReadWith (string layout, byte[] text)
	{
		_ = layout;
		byte[] txxx = [0x01, .. Le, .. Utf16 ("MyField", false), 0, 0, .. text];
		byte[] tpe1 = [0x01, .. text];

		CollectionAssert.AreEqual (txxx, UserTextFrame.Read (txxx, Id3v2Version.V24).Frame!.RenderContent ().ToArray ());
		// The text holds no terminator; the layout keeps it, so both frames render the bytes they were read from.
		var textFrame = TextFrame.Read ("TPE1", tpe1, Id3v2Version.V24).Frame!;
		Assert.IsFalse (textFrame.Text.Contains ('\uFEFF'), "a BOM is not text");
		CollectionAssert.AreEqual (tpe1, textFrame.RenderContent ().ToArray ());
	}

	[TestMethod]
	[DataRow ("a\0\uFEFFb")]
	[DataRow ("a\0\uFFFEb")]
	[DataRow ("\uFEFFa\0b")]
	[DataRow ("a\0\uFEFF")]
	public void Utf16Text_StringStartingWithAByteOrderMarkChar_KeepsIt (string text)
	{
		// U+FEFF or U+FFFE at the start of a string's text encodes as the bytes of a BOM; the string then gets a
		// real BOM in front, so a reader does not take that char for one.
		foreach (var frame in new[] { new TextFrame ("TPE1", text, TextEncodingType.Utf16WithBom) }) {
			var again = TextFrame.Read ("TPE1", frame.RenderContent ().Span, Id3v2Version.V24).Frame!;
			Assert.AreEqual (text, again.Text);
			CollectionAssert.AreEqual (again.RenderContent ().ToArray (), TextFrame.Read ("TPE1", again.RenderContent ().Span, Id3v2Version.V24).Frame!.RenderContent ().ToArray ());
		}
	}

	[TestMethod]
	public void Utf16SingleString_RendersAsBefore ()
	{
		var frame = new TextFrame ("TIT2", "Song", TextEncodingType.Utf16WithBom);

		byte[] expected = [0x01, .. Le, .. Utf16 ("Song", false)];
		CollectionAssert.AreEqual (expected, frame.RenderContent ().ToArray ());
	}

	static Id3v2Tag ReadTag (byte version, byte[] frame)
	{
		byte[] header = TestBuilders.Id3v2.CreateHeader (version, (uint)frame.Length);
		var result = Id3v2Tag.Read ([.. header, .. frame]);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}
}
