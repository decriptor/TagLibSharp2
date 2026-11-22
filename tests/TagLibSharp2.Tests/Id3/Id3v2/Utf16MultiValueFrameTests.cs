// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

/// <summary>
/// Every multi-value reader over encoding $01 text gets the same strings: text frames (ID3v2.4 §4.2, through the
/// mapped multi-value properties), TXXX (§4.2.6), the involved people lists TIPL, TMCL (§4.2.2) and IPLS
/// (ID3v2.3 §4.4), in ID3v2.2, 2.3 and 2.4. Each string may begin with its own BOM (ID3v2.3 §3.3, and the layout
/// mutagen writes by default); a string without one keeps the byte order of the string before it
/// (ID3v2.4 §4: "All Unicode strings ... SHALL have the same byteorder").
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3v2")]
public class Utf16MultiValueFrameTests
{
	static readonly byte[] Le = [0xFF, 0xFE];
	static readonly byte[] Be = [0xFE, 0xFF];
	static readonly string[] Ab = ["Alpha", "Beta"];
	static readonly string[] MultiValueFrameIds = ["TPE1", "TPE2", "TCOM", "TCON"];
	static readonly string[] AEmptyB = ["a", "", "b"];
	static readonly string[] GuitarEmpty = ["g", ""];
	static readonly string[] GuitarOnly = ["g"];

	/// <summary>The byte layouts a writer produces for the strings, each followed by 00 00 except the last.</summary>
	public enum Layout
	{
		/// <summary>A little-endian BOM on every string (mutagen's default).</summary>
		LittleEndianBomEach,
		/// <summary>A big-endian BOM on every string.</summary>
		BigEndianBomEach,
		/// <summary>One little-endian BOM for the frame.</summary>
		LittleEndianBomOnce,
		/// <summary>One big-endian BOM for the frame; later strings keep that order.</summary>
		BigEndianBomOnce,
		/// <summary>A BOM on every string and a terminator after the last.</summary>
		LittleEndianBomEachTerminated,
	}

	public static IEnumerable<object[]> Layouts => Enum.GetValues<Layout> ().Select (l => new object[] { l });

	static byte[] Encode (Layout layout, params string[] strings)
	{
		var bigEndian = layout is Layout.BigEndianBomEach or Layout.BigEndianBomOnce;
		var bomEach = layout is Layout.LittleEndianBomEach or Layout.BigEndianBomEach or Layout.LittleEndianBomEachTerminated;
		var bytes = new List<byte> ();
		for (var i = 0; i < strings.Length; i++) {
			if (i > 0)
				bytes.AddRange (new byte[] { 0, 0 });
			if (i == 0 || bomEach)
				bytes.AddRange (bigEndian ? Be : Le);
			bytes.AddRange (bigEndian
				? System.Text.Encoding.BigEndianUnicode.GetBytes (strings[i])
				: System.Text.Encoding.Unicode.GetBytes (strings[i]));
		}
		if (layout == Layout.LittleEndianBomEachTerminated)
			bytes.AddRange (new byte[] { 0, 0 });
		return [.. bytes];
	}

	static Id3v2Tag ReadTag (byte version, string frameId, byte[] content)
	{
		var frame = version == 2
			? TestBuilders.Id3v2.CreateFrameV22 (frameId, content)
			: TestBuilders.Id3v2.CreateFrame (frameId, content, version);
		var result = Id3v2Tag.Read ([.. TestBuilders.Id3v2.CreateHeader (version, (uint)frame.Length), .. frame]);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.Tag!;
	}

	public static IEnumerable<object[]> TextFrames =>
		from layout in Enum.GetValues<Layout> ()
		from version in new byte[] { 3, 4 }
		from id in MultiValueFrameIds
		select new object[] { layout, version, id };

	static string[] PropertyFor (Id3v2Tag tag, string frameId) => frameId switch {
		"TPE1" or "TP1" => tag.Performers,
		"TPE2" => tag.AlbumArtists,
		"TCOM" => tag.Composers,
		"TCON" => tag.Genres,
		_ => throw new ArgumentOutOfRangeException (nameof (frameId))
	};

	[TestMethod]
	[DynamicData (nameof (TextFrames))]
	public void MultiValueTextFrame_ReadsEachStringWithoutItsBom (Layout layout, byte version, string frameId)
	{
		var tag = ReadTag (version, frameId, [0x01, .. Encode (layout, Ab)]);

		CollectionAssert.AreEqual (Ab, PropertyFor (tag, frameId));
		CollectionAssert.AreEqual (Ab, tag.GetTextFrameValues (frameId).ToArray ());
	}

	[TestMethod]
	[DynamicData (nameof (Layouts))]
	public void Id3v22TextFrame_ReadsEachStringWithoutItsBom (Layout layout)
	{
		var tag = ReadTag (2, "TP1", [0x01, .. Encode (layout, Ab)]);

		CollectionAssert.AreEqual (Ab, tag.Performers);
	}

	[TestMethod]
	[DynamicData (nameof (Layouts))]
	public void Txxx_ReadsEachStringWithoutItsBom (Layout layout)
	{
		foreach (var version in new byte[] { 3, 4 }) {
			byte[] content = [0x01, .. Encode (Layout.LittleEndianBomEach, "Field"), 0, 0, .. Encode (layout, Ab)];
			var tag = ReadTag (version, "TXXX", content);

			CollectionAssert.AreEqual (Ab, tag.GetCustomField ("Field").ToArray ());
		}
	}

	[TestMethod]
	[DynamicData (nameof (Layouts))]
	public void InvolvedPeople_ReadsEachStringWithoutItsBom (Layout layout)
	{
		string[] strings = ["producer", "Alpha", "engineer", "Beta"];
		foreach (var (version, id) in new (byte, string)[] { (4, "TIPL"), (4, "TMCL"), (3, "IPLS") }) {
			var frame = InvolvedPeopleFrame.Read (id, [0x01, .. Encode (layout, strings)],
				version == 4 ? Id3v2Version.V24 : Id3v2Version.V23).Frame!;

			CollectionAssert.AreEqual (
				new[] { ("producer", "Alpha"), ("engineer", "Beta") },
				frame.Pairs.ToArray (), $"{id} {layout}");
		}
	}

	[TestMethod]
	public void InvolvedPeople_InTheTag_ReadsEachStringWithoutItsBom ()
	{
		var tag = ReadTag (4, "TIPL", [0x01, .. Encode (Layout.BigEndianBomOnce, "producer", "Alpha")]);

		Assert.AreEqual ("Alpha", tag.InvolvedPeopleFrames[0].GetPerson ("producer"));
	}

	[TestMethod]
	public void Utf16Text_DecodeStrings_IsTheSplitterEveryReaderUses ()
	{
		var strings = Utf16Text.DecodeStrings (Encode (Layout.BigEndianBomOnce, "a", "", "b"));

		CollectionAssert.AreEqual (AEmptyB, strings.ToArray ());
		Assert.AreEqual ("a\0\0b", Utf16Text.Decode (Encode (Layout.LittleEndianBomEach, "a", "", "b")));
	}

	[TestMethod]
	public void InvolvedPeople_LoneByteAfterATerminator_IsAnEmptyPerson ()
	{
		// As #136 reads it: a lone last byte after 00 00 starts a string with no code unit, so the role it follows
		// still pairs, with an empty person. The byte itself is never text.
		byte[] data = [0x01, 0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes ("a\0Alpha\0guitar"), 0x00, 0x00, 0x41];
		foreach (var (version, id) in new (byte, string)[] { (4, "TIPL"), (4, "TMCL"), (3, "IPLS") }) {
			var frame = InvolvedPeopleFrame.Read (id, data, version == 4 ? Id3v2Version.V24 : Id3v2Version.V23).Frame!;

			CollectionAssert.AreEqual (new[] { ("a", "Alpha"), ("guitar", "") }, frame.Pairs.ToArray (), id);
		}
		CollectionAssert.AreEqual (GuitarEmpty, Utf16Text.DecodeStrings ([0xFF, 0xFE, 0x67, 0x00, 0x00, 0x00, 0x41]).ToArray ());
		CollectionAssert.AreEqual (GuitarOnly, Utf16Text.DecodeStrings ([0xFF, 0xFE, 0x67, 0x00, 0x00, 0x00]).ToArray (),
			"an even frame's trailing terminator is not a value");
		CollectionAssert.AreEqual (GuitarOnly, Utf16Text.DecodeStrings ([0xFF, 0xFE, 0x67, 0x00, 0x41]).ToArray (),
			"a lone byte after text is dropped");
	}
}
