// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

/// <summary>
/// AVI INFO text and the CSET chunk (MPIDS 1.0, "CSET (Character Set) Chunk": no CSET, or code page 0,
/// means ISO 8859/1). Invariant: a value the caller did not change keeps its exact bytes, and so its
/// meaning under the file's CSET; a save that cannot keep that refuses.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Avi")]
public class AviInfoTextTests
{
	const string ChangedAscii = "New";
	const string ChangedNonAscii = "Café ☕ 日本";

	static readonly byte[] ShiftJisSong = [0x93, 0xFA, 0x96, 0x7B, 0x82, 0xCC, 0x89, 0xCC, 0x00]; // 日本の歌
	static readonly byte[] Windows1252Halo = [.. "Beyonc"u8, 0xE9, 0x20, 0x96, .. " Halo"u8, 0x00]; // Beyoncé – Halo
	static readonly byte[] Windows1251Alice = [0xC0, 0xEB, 0xE8, 0xF1, 0xE0, 0x00]; // Алиса

	/// <summary>
	/// {CSET none, 0, 28591, 932, 1252, 65001} x {unchanged ASCII, UTF-8, legacy} x {changed ASCII, non-ASCII}.
	/// Each cell either keeps the unchanged field's bytes and text, or refuses the save.
	/// </summary>
	[TestMethod]
	[DynamicData (nameof (Matrix))]
	public void Save_UnchangedField_KeepsItsBytesOrTheSaveRefuses (int codePage, string unchangedKind, string changedKind)
	{
		var unchanged = UnchangedBytes (codePage, unchangedKind);
		var builder = new AviTestBuilder { CharacterSet = codePage < 0 ? null : (ushort?)codePage };
		builder.Info.Add (("INAM", "Old"));
		builder.InfoBytes.Add (("IART", unchanged));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		var artist = file.InfoTag.Artist;
		var changed = changedKind == "ascii" ? ChangedAscii : ChangedNonAscii;
		file.InfoTag.Title = changed;

		var fs = new MockFileSystem ();
		var result = file.SaveToFile ("/out.avi", original, fs);

		var nativeUtf8 = codePage == 65001;
		var becameUtf8 = changedKind == "nonascii" && !nativeUtf8;
		var kind = Kind (codePage, unchanged);
		var expectRefusal = becameUtf8 && kind == "guess";
		var reencoded = becameUtf8 && kind == "exact";
		var cell = $"CSET {codePage}, unchanged {unchangedKind}, changed {changedKind}";
		if (expectRefusal) {
			Assert.IsFalse (result.IsSuccess, cell);
			StringAssert.Contains (result.Error, "IART", cell);
			Assert.IsFalse (fs.FileExists ("/out.avi"), cell);
			return;
		}

		Assert.IsTrue (result.IsSuccess, $"{cell}: {result.Error}");
		var saved = fs.ReadAllBytes ("/out.avi");
		var again = AviFile.Read (saved).File!.InfoTag;
		if (reencoded) {
			// The round trip: the original decoded with its declared code page by .NET's own decoder
			// equals the UTF-8 the save wrote, and the value read back.
			var declared = Oracle (codePage).GetString (unchanged, 0, Array.IndexOf (unchanged, (byte)0));
			CollectionAssert.AreEqual (Encoding.UTF8.GetBytes (declared + "\0"), AviTestBuilder.InfoField (saved, "IART"), cell);
			Assert.AreEqual (declared, again.Artist, cell);
		} else {
			CollectionAssert.AreEqual (unchanged, AviTestBuilder.InfoField (saved, "IART"), cell);
		}

		Assert.AreEqual (artist, again.Artist, cell);
		Assert.AreEqual (changed, again.Title, cell);

		var names = AviSaveTests.Names (saved);
		if (becameUtf8) {
			Assert.AreEqual ((ushort)65001, CodePage (saved), cell);
			Assert.AreEqual (1, names.Count (n => n == "CSET"), cell);
		} else if (codePage < 0) {
			CollectionAssert.DoesNotContain (names, "CSET", cell);
		} else {
			Assert.AreEqual ((ushort)codePage, CodePage (saved), cell);
		}

		if (names.Contains ("CSET"))
			Assert.AreEqual (names.IndexOf ("LIST INFO") - 1, names.IndexOf ("CSET"), $"{cell}: the CSET comes right before the list");
	}

	/// <summary>
	/// The same matrix with the change made through the custom-field API: SetCustomField adds a field the
	/// file did not hold, or RemoveCustomField drops one it did. Each cell keeps the unchanged field's bytes, or
	/// re-encodes it as UTF-8 when the list must become UTF-8, or refuses, exactly as a mapped field change does.
	/// </summary>
	[TestMethod]
	[DynamicData (nameof (Matrix))]
	public void Save_CustomFieldChange_KeepsUnchangedBytesOrTheSaveRefuses (int codePage, string unchangedKind, string changedKind)
	{
		var unchanged = UnchangedBytes (codePage, unchangedKind);
		var builder = new AviTestBuilder { CharacterSet = codePage < 0 ? null : (ushort?)codePage };
		builder.Info.Add (("INAM", "Old"));
		builder.Info.Add (("IOLD", "drop me"));
		builder.InfoBytes.Add (("IART", unchanged));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		var artist = file.InfoTag.Artist;
		var changed = changedKind == "ascii" ? ChangedAscii : ChangedNonAscii;
		Assert.IsTrue (file.InfoTag.SetCustomField ("IXYZ", changed), "an AVI INFO list takes any text");
		Assert.IsTrue (file.InfoTag.RemoveCustomField ("IOLD"));

		var fs = new MockFileSystem ();
		var result = file.SaveToFile ("/out.avi", original, fs);

		var becameUtf8 = changedKind == "nonascii" && codePage != 65001;
		var kind = Kind (codePage, unchanged);
		var cell = $"CSET {codePage}, unchanged {unchangedKind}, custom field {changedKind}";
		if (becameUtf8 && kind == "guess") {
			Assert.IsFalse (result.IsSuccess, cell);
			StringAssert.Contains (result.Error, "IART", cell);
			return;
		}

		Assert.IsTrue (result.IsSuccess, $"{cell}: {result.Error}");
		var saved = fs.ReadAllBytes ("/out.avi");
		var again = AviFile.Read (saved).File!.InfoTag;
		if (becameUtf8 && kind == "exact") {
			var declared = Oracle (codePage).GetString (unchanged, 0, Array.IndexOf (unchanged, (byte)0));
			CollectionAssert.AreEqual (Encoding.UTF8.GetBytes (declared + "\0"), AviTestBuilder.InfoField (saved, "IART"), cell);
		} else {
			CollectionAssert.AreEqual (unchanged, AviTestBuilder.InfoField (saved, "IART"), cell);
		}

		Assert.AreEqual (artist, again.Artist, cell);
		Assert.AreEqual ("Old", again.Title, cell);
		CollectionAssert.AreEqual (new[] { changed }, again.GetCustomField ("IXYZ").ToArray (), cell);
		Assert.AreEqual (0, again.GetCustomField ("IOLD").Count, cell);
		Assert.IsNull (AviTestBuilder.InfoField (saved, "IOLD"), cell);
		if (becameUtf8)
			Assert.AreEqual ((ushort)65001, CodePage (saved), cell);
		else if (codePage >= 0)
			Assert.AreEqual ((ushort)codePage, CodePage (saved), $"{cell}: the declared code page is kept");
	}

	[TestMethod]
	[DataRow ((ushort)1252, "Café", new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9, 0 })]
	[DataRow ((ushort)28591, "Café", new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9, 0 })]
	[DataRow ((ushort)0, "Café", new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9, 0 })]
	public void Save_CustomFieldThatFitsTheDeclaredCodePage_IsWrittenInIt (ushort codePage, string value, byte[] expected)
	{
		var builder = new AviTestBuilder { CharacterSet = codePage };
		builder.Info.Add (("INAM", "Old"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		Assert.IsTrue (file.InfoTag.SetCustomField ("IXYZ", value));

		var fs = new MockFileSystem ();
		Assert.IsTrue (file.SaveToFile ("/out.avi", original, fs).IsSuccess);
		var saved = fs.ReadAllBytes ("/out.avi");

		CollectionAssert.AreEqual (expected, AviTestBuilder.InfoField (saved, "IXYZ"));
		Assert.AreEqual (codePage, CodePage (saved), "the CSET is kept");
		CollectionAssert.AreEqual (new[] { value }, AviFile.Read (saved).File!.InfoTag.GetCustomField ("IXYZ").ToArray ());
	}

	[TestMethod]
	public void WavInfo_CustomFieldOutsideLatin1_IsRefused ()
	{
		// A WAV INFO list is Latin-1 and has no CSET, so text it cannot hold is refused, not mangled.
		var tag = new RiffInfoTag ();
		Assert.IsFalse (tag.SetCustomField ("IXYZ", ChangedNonAscii));
		Assert.IsTrue (tag.SetCustomField ("IXYZ", "Café"));
	}

	public static IEnumerable<object[]> Matrix ()
	{
		foreach (var codePage in new[] { -1, 0, 28591, 932, 1252, 65001 })
			foreach (var unchanged in new[] { "ascii", "utf8", "legacy" })
				foreach (var changed in new[] { "ascii", "nonascii" })
					yield return [codePage, unchanged, changed];
	}

	/// <summary>
	/// How surely the unchanged value is known: ASCII and UTF-8 keep their bytes in a UTF-8 list; text in a
	/// declared code page decoded exactly is rewritten as UTF-8; text whose code page is a guess refuses.
	/// </summary>
	static string Kind (int codePage, byte[] bytes)
	{
		var text = bytes.AsSpan (0, Array.IndexOf (bytes, (byte)0));
		if (!text.ToArray ().Any (b => b > 0x7F))
			return "ascii";
		if (codePage is -1 or 65001 && IsUtf8 (text.ToArray ()))
			return "utf8";
		return codePage switch {
			28591 => "exact",
			0 => text.ToArray ().Any (b => b is >= 0x80 and <= 0x9F) ? "guess" : "exact",
			1252 => text.ToArray ().Any (b => b is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D) ? "guess" : "exact",
			65001 => "native",
			_ => "guess"
		};
	}

	static bool IsUtf8 (byte[] bytes)
	{
		try {
			_ = new UTF8Encoding (false, true).GetString (bytes);
			return true;
		} catch (DecoderFallbackException) {
			return false;
		}
	}

	/// <summary>.NET's decoder for a declared code page, independent of the library's tables.</summary>
	static Encoding Oracle (int codePage) => codePage switch {
		1252 => CodePagesEncodingProvider.Instance.GetEncoding (1252)!,
		_ => Encoding.Latin1
	};

	static byte[] UnchangedBytes (int codePage, string kind) => kind switch {
		"ascii" => [.. "Artist"u8, 0],
		"utf8" => [.. Encoding.UTF8.GetBytes ("Björk ☕"), 0],
		_ => codePage == 932 ? ShiftJisSong : Windows1252Halo
	};

	[TestMethod]
	[DataRow (28591, new byte[] { 0x43, 0x61, 0x66, 0xE9, 0x00 }, "Café")]
	[DataRow (1252, new byte[] { 0x42, 0x65, 0x79, 0x6F, 0x6E, 0x63, 0xE9, 0x20, 0x96, 0x20, 0x48, 0x61, 0x6C, 0x6F, 0x00 }, "Beyoncé – Halo")]
	public void Save_ChangedTextThatFitsTheDeclaredCodePage_StaysInIt (int codePage, byte[] expected, string text)
	{
		var builder = new AviTestBuilder { CharacterSet = (ushort)codePage };
		builder.InfoBytes.Add (("IART", Windows1252Halo));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = text;

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (expected, AviTestBuilder.InfoField (saved, "INAM"));
		CollectionAssert.AreEqual (Windows1252Halo, AviTestBuilder.InfoField (saved, "IART"));
		Assert.AreEqual ((ushort)codePage, CodePage (saved));
		Assert.AreEqual (text, AviFile.Read (saved).File!.InfoTag.Title);
	}

	[TestMethod]
	[DataRow (1252, new byte[] { 0x42, 0x65, 0x79, 0x6F, 0x6E, 0x63, 0xE9, 0x20, 0x96, 0x20, 0x48, 0x61, 0x6C, 0x6F, 0x00 })]
	[DataRow (28591, new byte[] { 0x43, 0x61, 0x66, 0xE9, 0x20, 0xA9, 0x00 })]
	[DataRow (0, new byte[] { 0x43, 0x61, 0x66, 0xE9, 0x20, 0xA9, 0x00 })]
	public void Save_NonAsciiChangeUnderADecodableCset_RewritesUntouchedTextAsTheSameUtf8 (int codePage, byte[] legacy)
	{
		var builder = new AviTestBuilder { CharacterSet = (ushort)codePage };
		builder.InfoBytes.Add (("IART", legacy));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		var artist = file.InfoTag.Artist;
		file.InfoTag.Title = ChangedNonAscii;

		var saved = file.Render (original).ToArray ();

		var declared = Oracle (codePage).GetString (legacy, 0, legacy.Length - 1);
		Assert.AreEqual (declared, artist, "The read already matches .NET's decoder.");
		CollectionAssert.AreEqual (Encoding.UTF8.GetBytes (declared + "\0"), AviTestBuilder.InfoField (saved, "IART"));
		Assert.AreEqual ((ushort)65001, CodePage (saved));
		var again = AviFile.Read (saved).File!.InfoTag;
		Assert.AreEqual (declared, again.Artist);
		Assert.AreEqual (ChangedNonAscii, again.Title);
	}

	[TestMethod]
	[DataRow (1252, new byte[] { 0x41, 0x81, 0x42, 0x00 }, "holds byte 0x81, which code page 1252 leaves undefined")]
	[DataRow (0, new byte[] { 0x41, 0x96, 0x42, 0x00 }, "holds byte 0x96, which the CSET's code page 0")]
	public void Save_UndefinedByteInTheDeclaredCodePage_Refuses (int codePage, byte[] legacy, string reason)
	{
		// 0x81 is unassigned in Windows-1252, and columns 8 and 9 are undefined in CSET 0's ISO 8859/1.
		var builder = new AviTestBuilder { CharacterSet = (ushort)codePage };
		builder.InfoBytes.Add (("IART", legacy));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = ChangedNonAscii;

		var saved = file.SaveToFile ("/x.avi", original, new MockFileSystem ());

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "IART");
		StringAssert.Contains (saved.Error, reason);
		StringAssert.Contains (saved.Error, codePage == 1252 ? "'A\\u0081B'" : "'A\\u0096B'", "Control characters are shown escaped.");
	}

	[TestMethod]
	[DataRow (-1, "is not UTF-8 and the file has no CSET, so its code page is only a guess")]
	[DataRow (932, "is in code page 932, which this library does not decode")]
	[DataRow (1251, "is in code page 1251, which this library does not decode")]
	public void Save_RefusalNamesItsReason (int codePage, string reason)
	{
		var builder = new AviTestBuilder { CharacterSet = codePage < 0 ? null : (ushort?)codePage };
		builder.InfoBytes.Add (("IART", Windows1251Alice));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = ChangedNonAscii;

		var saved = file.SaveToFile ("/x.avi", original, new MockFileSystem ());

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, reason);
	}

	[TestMethod]
	[DataRow ('\u0081')]
	[DataRow ('\u008D')]
	[DataRow ('\u008F')]
	[DataRow ('\u0090')]
	[DataRow ('\u009D')]
	public void Save_UnassignedWindows1252CodePoint_GoesToUtf8AndCanBeEditedAgain (char c)
	{
		// The five code points CP1252.TXT leaves UNDEFINED do not fit 1252, so the list becomes UTF-8,
		// and a later non-ASCII edit of what the library wrote saves too.
		var builder = new AviTestBuilder { CharacterSet = 1252 };
		builder.Info.Add (("INAM", "Old"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "A" + c + "B";

		var first = file.Render (original).ToArray ();

		Assert.AreEqual ((ushort)65001, CodePage (first));
		var again = AviFile.Read (first).File!;
		Assert.AreEqual ("A" + c + "B", again.InfoTag.Title);
		again.InfoTag.Artist = "☕";
		var second = again.SaveToFile ("/x.avi", first, new MockFileSystem ());
		Assert.IsTrue (second.IsSuccess, second.Error);
	}

	/// <summary>
	/// Round trip, both directions, for every declared code page this library encodes: whatever the
	/// encoder writes, the decoder reads back as the same text and as exact (never a guess); and every
	/// byte the decoder reads as exact, the encoder writes back as the same byte.
	/// </summary>
	[TestMethod]
	public void Encoding_RoundTripsInEveryEncodedCodePage ()
	{
		InfoCharacterSet[] sets = [InfoCharacterSet.Latin1, InfoCharacterSet.Latin1Default, InfoCharacterSet.Windows1252, InfoCharacterSet.Utf8];
		var checkedCells = 0;
		foreach (var set in sets) {
			for (var code = 0x80; code <= 0x3000; code++) {
				var value = "A" + (char)code + "B";
				if (!InfoText.CanEncode (value, set))
					continue;
				var bytes = InfoText.Encode (value, set);
				var decoded = InfoText.Decode (bytes, set, out var kind);
				Assert.AreEqual (value, decoded, $"{set} U+{code:X4}");
				Assert.AreNotEqual (InfoTextKind.Guess, kind, $"{set} U+{code:X4}: the encoder wrote bytes its decoder calls a guess");
				checkedCells++;
			}

			for (var b = 0x80; b <= 0xFF; b++) {
				byte[] bytes = [0x41, (byte)b, 0x42, 0];
				var decoded = InfoText.Decode (bytes, set, out var kind);
				if (kind == InfoTextKind.Guess)
					continue;
				Assert.IsTrue (InfoText.CanEncode (decoded, set) || set == InfoCharacterSet.Utf8, $"{set} byte 0x{b:X2}");
				if (set != InfoCharacterSet.Utf8)
					CollectionAssert.AreEqual (bytes, InfoText.Encode (decoded, set), $"{set} byte 0x{b:X2}");
			}
		}

		Assert.IsGreaterThan (1000, checkedCells);
	}

	[TestMethod]
	public void Save_AsciiText_WritesNoCset ()
	{
		var original = new AviTestBuilder ().Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "Plain";

		var saved = file.Render (original).ToArray ();

		CollectionAssert.DoesNotContain (AviSaveTests.Names (saved), "CSET");
		Assert.AreEqual ("Plain", AviFile.Read (saved).File!.InfoTag.Title);
	}

	[TestMethod]
	public void Save_NonAsciiTextWithoutACset_IsUtf8UnderACset ()
	{
		// "é" fits ISO 8859-1, but ffprobe, ffmpeg and TagLib# read INFO as UTF-8, so a file with no
		// CSET gets UTF-8 and a CSET that says so.
		var original = new AviTestBuilder ().Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "Café";

		var saved = file.Render (original).ToArray ();

		Assert.AreEqual ((ushort)65001, CodePage (saved));
		CollectionAssert.AreEqual (Encoding.UTF8.GetBytes ("Café\0"), AviTestBuilder.InfoField (saved, "INAM"));
		Assert.AreEqual ("Café", AviFile.Read (saved).File!.InfoTag.Title);
		Assert.AreEqual ("Café", MediaFile.Read (new MemoryStream (saved)).Tag!.Title, "A stream read keeps the CSET.");
	}

	[TestMethod]
	public void Save_LegacyTextNextToANonAsciiChange_Refuses ()
	{
		// VirtualDub on a Russian system writes cp1251 with no CSET; transcoding it would destroy it.
		var builder = new AviTestBuilder ();
		builder.InfoBytes.Add (("IART", Windows1251Alice));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = ChangedNonAscii;

		var saved = file.SaveToFile ("/x.avi", original, new MockFileSystem ());

		Assert.IsFalse (saved.IsSuccess);
		StringAssert.Contains (saved.Error, "IART");
		Assert.AreEqual (0, file.Render (original).Length, "Render of the file refuses the same way.");

		file.InfoTag.Artist = "Alisa";
		Assert.IsTrue (file.SaveToFile ("/x.avi", original, new MockFileSystem ()).IsSuccess, "Setting the field too lets the save write UTF-8.");
	}

	[TestMethod]
	public void Save_AsciiChangeNextToLegacyText_KeepsItsBytesAndCset ()
	{
		var builder = new AviTestBuilder { CharacterSet = 932 };
		builder.InfoBytes.Add (("INAM", ShiftJisSong));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Artist = "New";

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (ShiftJisSong, AviTestBuilder.InfoField (saved, "INAM"));
		Assert.AreEqual ((ushort)932, CodePage (saved));
		Assert.AreEqual ("New", AviFile.Read (saved).File!.InfoTag.Artist);
	}

	[TestMethod]
	public void Save_AsciiChangeNextToUtf8WithoutACset_AddsNoCset ()
	{
		// An ffmpeg file: UTF-8 with no CSET. An ASCII change leaves the file as the writer left it.
		var builder = new AviTestBuilder { InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("IART", "Björk"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "New";

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (Encoding.UTF8.GetBytes ("Björk\0"), AviTestBuilder.InfoField (saved, "IART"));
		CollectionAssert.DoesNotContain (AviSaveTests.Names (saved), "CSET");
	}

	[TestMethod]
	public void Save_NonAsciiChangeNextToUtf8WithoutACset_DeclaresUtf8 ()
	{
		var builder = new AviTestBuilder { InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("IART", "Björk"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = ChangedNonAscii;

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (Encoding.UTF8.GetBytes ("Björk\0"), AviTestBuilder.InfoField (saved, "IART"));
		Assert.AreEqual ((ushort)65001, CodePage (saved));
		Assert.AreEqual (ChangedNonAscii, AviFile.Read (saved).File!.InfoTag.Title);
	}

	[TestMethod]
	[DataRow (null)]
	[DataRow ((ushort)28591)]
	[DataRow ((ushort)65001)]
	public void Save_UnchangedAsciiField_KeepsItsPadding (ushort? codePage)
	{
		// An all-ASCII list: the writer's extra NULs survive a change to another field.
		var builder = new AviTestBuilder { CharacterSet = codePage };
		builder.Info.Add (("INAM", "Old"));
		builder.InfoBytes.Add (("ICMT", [.. "ascii"u8, 0, 0, 0]));
		builder.InfoBytes.Add (("IART", [.. Encoding.UTF8.GetBytes ("Björk"), 0, 0]));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = "New";
		file.InfoTag.Artist = null;

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual ((byte[])[.. "ascii"u8, 0, 0, 0], AviTestBuilder.InfoField (saved, "ICMT"));
	}

	[TestMethod]
	public void Save_UnchangedUtf8Field_KeepsItsPadding ()
	{
		var builder = new AviTestBuilder { InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("INAM", "Old"));
		builder.InfoBytes.Add (("IART", [.. Encoding.UTF8.GetBytes ("Björk"), 0, 0, 0]));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = ChangedNonAscii;

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual ((byte[])[.. Encoding.UTF8.GetBytes ("Björk"), 0, 0, 0], AviTestBuilder.InfoField (saved, "IART"));
	}

	[TestMethod]
	public void Save_ClearedTag_LeavesTheCset ()
	{
		var builder = new AviTestBuilder { CharacterSet = 65001, InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("INAM", "日本"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		Assert.IsTrue (file.Render (original).Span.SequenceEqual (original), "An unchanged list keeps its CSET.");
		file.InfoTag.Clear ();

		var saved = file.Render (original).ToArray ();

		Assert.HasCount (original.Length, saved);
		CollectionAssert.DoesNotContain (AviSaveTests.Names (saved), "LIST INFO");
		Assert.AreEqual ((ushort)65001, CodePage (saved));
	}

	[TestMethod]
	public void Save_CsetAfterTheList_MovesInFrontOfTheNewList ()
	{
		// exiftool honours a CSET only when it comes before LIST INFO; the rewritten list keeps it in front.
		// The new list is shorter than the old one, so it fits the old list's own slot, ahead of the CSET.
		var builder = new AviTestBuilder { ChunkBeforeJunk = ("CSET", [0xE4, 0x04, 0, 0, 0, 0, 0, 0]) };
		builder.Info.Add (("INAM", "An old title long enough to leave room"));
		builder.InfoBytes.Add (("IART", Windows1252Halo));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		Assert.AreEqual ("Beyoncé – Halo", file.InfoTag.Artist);
		file.InfoTag.Title = "New";

		var saved = file.Render (original).ToArray ();

		var names = AviSaveTests.Names (saved);
		Assert.AreEqual (1, names.Count (n => n == "CSET"));
		Assert.AreEqual (names.IndexOf ("LIST INFO") - 1, names.IndexOf ("CSET"));
		Assert.AreEqual ((ushort)1252, CodePage (saved));
		CollectionAssert.AreEqual (Windows1252Halo, AviTestBuilder.InfoField (saved, "IART"));
	}

	[TestMethod]
	public void Save_ListThatBecomesUtf8_KeepsTheCsetsCountryLanguageAndDialect ()
	{
		// MPIDS 1.0 reads zero country, language and dialect codes as USA and US English; a German
		// file (49, 7, 1) must keep them when only its code page changes.
		var builder = new AviTestBuilder { CharacterSet = 1252, CharacterSetCodes = (49, 7, 1) };
		builder.InfoBytes.Add (("INAM", [.. "Song"u8, 0]));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Artist = "Müller ☕";

		var saved = file.Render (original).ToArray ();

		var cset = (int)AviSaveTests.Offset (saved, "CSET") + 8;
		Assert.AreEqual ((ushort)65001, BinaryPrimitives.ReadUInt16LittleEndian (saved.AsSpan (cset)));
		Assert.AreEqual ((ushort)49, BinaryPrimitives.ReadUInt16LittleEndian (saved.AsSpan (cset + 2)));
		Assert.AreEqual ((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian (saved.AsSpan (cset + 4)));
		Assert.AreEqual ((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian (saved.AsSpan (cset + 6)));
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void Save_ListThatBecomesUtf8_WritesZeroCodesWithoutAFullCset (bool shortCset)
	{
		// No CSET, or one holding only wCodePage: there are no codes to keep.
		var builder = shortCset
			? new AviTestBuilder { ChunkBeforeJunk = ("CSET", [0xE4, 0x04]) }
			: new AviTestBuilder ();
		builder.Info.Add (("INAM", "Song"));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Artist = "Müller ☕";

		var saved = file.Render (original).ToArray ();

		var cset = (int)AviSaveTests.Offset (saved, "CSET") + 8;
		CollectionAssert.AreEqual (new byte[] { 0xE9, 0xFD, 0, 0, 0, 0, 0, 0 }, saved.AsSpan (cset, 8).ToArray ());
		Assert.AreEqual (1, AviSaveTests.Names (saved).Count (n => n == "CSET"));
	}

	[TestMethod]
	[DataRow ("ascii")]
	[DataRow ("nonascii")]
	public void Save_ExistingCset_IsWrittenBackWithItsCodes (string changedKind)
	{
		// A list that stays in the file's code page keeps the file's own CSET, country and language included.
		var builder = new AviTestBuilder { CharacterSet = 65001, CharacterSetCodes = (1, 9, 1), InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("INAM", "Old"));
		var original = builder.Build ();
		var cset = original.AsSpan ((int)AviSaveTests.Offset (original, "CSET"), 16).ToArray ();
		var file = AviFile.Read (original).File!;
		file.InfoTag.Title = changedKind == "ascii" ? ChangedAscii : ChangedNonAscii;

		var saved = file.Render (original).ToArray ();

		CollectionAssert.AreEqual (cset, saved.AsSpan ((int)AviSaveTests.Offset (saved, "CSET"), 16).ToArray ());
	}

	[TestMethod]
	public void Read_Utf8Cset_DecodesUtf8 ()
	{
		var builder = new AviTestBuilder { CharacterSet = 65001, InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("INAM", "Café ☕"));
		var bytes = builder.Build ();

		Assert.AreEqual ("Café ☕", AviFile.Read (bytes).File!.InfoTag.Title);
		Assert.AreEqual ("Café ☕", MediaFile.Read (new MemoryStream (bytes)).Tag!.Title);
	}

	[TestMethod]
	[DataRow ((ushort)0)]
	[DataRow ((ushort)28591)]
	public void Read_Latin1Cset_ReadsEveryByteAsLatin1 (ushort codePage)
	{
		// The CSET says ISO 8859-1, so bytes that happen to be valid UTF-8 are still Latin-1.
		var builder = new AviTestBuilder { CharacterSet = codePage, InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("INAM", "Café"));
		var bytes = builder.Build ();

		Assert.AreEqual ("CafÃ©", AviFile.Read (bytes).File!.InfoTag.Title);
		Assert.AreEqual ("CafÃ©", MediaFile.Read (new MemoryStream (bytes)).Tag!.Title, "A stream read keeps the CSET.");
	}

	[TestMethod]
	[DataRow (null)]
	[DataRow ((ushort)1252)]
	public void Read_Windows1252Text_ReadsItsPunctuation (ushort? codePage)
	{
		var builder = new AviTestBuilder { CharacterSet = codePage };
		builder.InfoBytes.Add (("INAM", Windows1252Halo));

		Assert.AreEqual ("Beyoncé – Halo", AviFile.Read (builder.Build ()).File!.InfoTag.Title);
	}

	[TestMethod]
	public void Read_UnknownCodePage_KeepsBytesItCannotDecode ()
	{
		// CSET 932 is not decoded here; the value is read as best it can be and its bytes stay.
		var builder = new AviTestBuilder { CharacterSet = 932, InfoEncoding = Encoding.UTF8 };
		builder.Info.Add (("IART", "Café"));
		builder.InfoBytes.Add (("INAM", ShiftJisSong));
		var original = builder.Build ();
		var file = AviFile.Read (original).File!;

		Assert.AreEqual ("Café", file.InfoTag.Artist, "Valid UTF-8 is still shown as UTF-8.");
		Assert.IsTrue (file.Render (original).Span.SequenceEqual (original));
	}

	[TestMethod]
	public void Read_ShortCset_IsNoCset ()
	{
		var builder = new AviTestBuilder { InfoEncoding = Encoding.UTF8, ChunkBeforeJunk = ("CSET", [0xE9]) };
		builder.Info.Add (("INAM", "Café"));

		Assert.AreEqual ("Café", AviFile.Read (builder.Build ()).File!.InfoTag.Title);
	}

	[TestMethod]
	public void Render_RefusedList_StillKeepsTheUnchangedBytes ()
	{
		var tag = AviFile.Read (LegacyNextToNothing ()).File!.InfoTag;
		tag.Title = ChangedNonAscii;

		var list = tag.Render ().ToArray ();

		Assert.IsGreaterThanOrEqualTo (0, list.AsSpan ().IndexOf (Windows1251Alice));
		Assert.IsGreaterThanOrEqualTo (0, list.AsSpan ().IndexOf (Encoding.UTF8.GetBytes (ChangedNonAscii)));
	}

	static byte[] LegacyNextToNothing ()
	{
		var builder = new AviTestBuilder ();
		builder.InfoBytes.Add (("IART", Windows1251Alice));
		return builder.Build ();
	}

	static ushort CodePage (byte[] file)
	{
		var cset = (int)AviSaveTests.Offset (file, "CSET");
		return BinaryPrimitives.ReadUInt16LittleEndian (file.AsSpan (cset + 8));
	}
}
