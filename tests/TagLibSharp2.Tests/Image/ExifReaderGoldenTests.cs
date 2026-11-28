// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// The tests' Exif reader against exiftool's own output. <c>Golden/exif-reader-probes.txt</c> records what exiftool
/// shows for a probe of each reading rule (pointer tags × location × type × count, values of several sizes, the
/// next IFD of every kind of table, unknown formats, value offsets in the header, the data's end and the value's own
/// table, the order tables are read in); <c>Golden/exif-edit-matrix.txt</c> records it for every input of the Exif
/// edit matrix. <see cref="ExifReaderModel.Exiftool"/> must give the same field-tag counts for every input, so a rule
/// of the model that is not exiftool's turns these red. Regenerate both with <c>tools/exif/golden.sh</c>.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public class ExifReaderGoldenTests
{
	const string DumpVariable = "TAGLIBSHARP2_EXIF_GOLDEN_DUMP";

	[TestMethod]
	public void ReaderModel_ShowsWhatExiftoolShows_ForEveryProbe () =>
		AssertMatchesGolden ("probes", "exif-reader-probes.txt", Probes ());

	[TestMethod]
	public void ReaderModel_ShowsWhatExiftoolShows_ForEveryMatrixInput () =>
		AssertMatchesGolden ("matrix", "exif-edit-matrix.txt", ExifEditMatrixTests.Inputs ());

	static void AssertMatchesGolden (string set, string golden, IEnumerable<(string Name, byte[] Tiff)> inputs)
	{
		var list = inputs.ToList ();
		if (Environment.GetEnvironmentVariable (DumpVariable) is { Length: > 0 } dump) {
			Dump (Path.Combine (dump, set), list);
			return;
		}

		var (count, sha, summaries, sequence) = Read (golden);
		Assert.AreEqual (count, list.Count, $"{golden}: the inputs changed; regenerate with tools/exif/golden.sh");
		Assert.AreEqual (sha, Sha256 (list), $"{golden}: the inputs changed; regenerate with tools/exif/golden.sh");
		var mismatches = new List<string> ();
		for (var n = 0; n < list.Count; n++) {
			var model = ExifReaderModel.Exiftool (list[n].Tiff).Summary ();
			var exiftool = summaries[sequence[n]];
			if (model != exiftool)
				mismatches.Add ($"#{n} {list[n].Name}: exiftool {exiftool}, model {model}");
		}

		Assert.AreEqual (0, mismatches.Count, $"{mismatches.Count} of {list.Count} inputs:\n" + string.Join ("\n", mismatches.Take (25)));
	}

	static void Dump (string directory, List<(string Name, byte[] Tiff)> inputs)
	{
		Directory.CreateDirectory (directory);
		for (var n = 0; n < inputs.Count; n++)
			File.WriteAllBytes (Path.Combine (directory, $"{n:D5}.tif"), inputs[n].Tiff);
	}

	static string Sha256 (List<(string Name, byte[] Tiff)> inputs)
	{
		using var sha = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
		foreach (var (_, tiff) in inputs)
			sha.AppendData (tiff);
		return Convert.ToHexString (sha.GetHashAndReset ()).ToLowerInvariant ();
	}

	static (int Count, string Sha, List<string> Summaries, List<int> Sequence) Read (string golden)
	{
		using var stream = typeof (ExifReaderGoldenTests).Assembly.GetManifestResourceStream ("Golden." + golden)!;
		using var reader = new StreamReader (stream);
		var lines = reader.ReadToEnd ().Split ('\n', StringSplitOptions.RemoveEmptyEntries);
		var header = lines[1].Split (' ');
		var summaries = new List<string> ();
		var sequence = new List<int> ();
		var section = "";
		foreach (var line in lines.Skip (2)) {
			if (line.StartsWith ('@')) {
				section = line;
				continue;
			}

			if (section == "@summaries")
				summaries.Add (line[(line.IndexOf (' ') + 1)..]);
			else
				sequence.AddRange (line.Split (' ').Select (n => int.Parse (n, CultureInfo.InvariantCulture)));
		}

		return (int.Parse (header[2], CultureInfo.InvariantCulture), header[4], summaries, sequence);
	}

	static readonly byte[] Uc = "ASCII\0\0\0probe-uc!"u8.ToArray ();

	/// <summary>A probe of each reading rule, as the golden file lists them.</summary>
	internal static IEnumerable<(string Name, byte[] Tiff)> Probes () =>
		PointerTypes ().Concat (PointerValues ()).Concat (NextIfds ()).Concat (ValuePlaces ()).Concat (UnknownFormats ())
			.Concat (FirstEntryByKind ()).Concat (ReadOrder ()).Concat (TooManyWarnings ());

	// exiftool stops reading a table at its 11th warning: 10 or 11 entries of each kind before an Exif IFD's
	// UserComment (the counted kinds and three that are not), the count kept per table, IFD0 stopped before its Exif
	// IFD pointer and before its next IFD, and an 11th warning on a table's last entry, which stops nothing.
	static IEnumerable<(string, byte[])> TooManyWarnings ()
	{
		var kinds = new (string Name, Func<int, (int, int, long, long)> Entry)[] {
			("unknown formats", j => (0xC000 + j, 16, 1, 0)),
			("values past the data", j => (0xC000 + j, 2, 20, 60000)),
			("values in the header", j => (0xC000 + j, 2, 20, 4)),
			("values inside their own table", j => (0xC000 + j, 2, 20, 44)),
			("entries of type 0", j => (0xC000 + j, 0, 1, 0)),
			("tags out of sequence", j => (0x0100 - j, 3, 1, 1)),
		};
		(int, int, long, long) good = (0x9000, 7, 4, 0x30333230), uc = (0x9286, 7, Uc.Length, 300);
		foreach (var (name, entry) in kinds) {
			foreach (var k in new[] { 10, 11 }) {
				(int, int, long, long)[] exif = [good, .. Enumerable.Range (0, k).Select (entry), uc];
				yield return ($"{k} {name} before the UserComment", new TiffBuilder (400).Header (8).Table (8, [(0x8769, 4, 1, 40)]).Table (40, exif).Bytes (300, Uc).ToArray ());
			}
		}

		var bad = Enumerable.Range (0, 6).Select (kinds[0].Entry).ToArray ();
		yield return ("6 warnings in IFD0 and 6 in the Exif IFD", new TiffBuilder (400).Header (8).Table (8, [(0x0100, 4, 1, 1), .. bad, (0x8769, 4, 1, 120)])
			.Table (120, [good, .. bad, uc]).Bytes (300, Uc).ToArray ());
		var eleven = Enumerable.Range (0, 11).Select (kinds[0].Entry).ToArray ();
		yield return ("IFD0 stopped before its Exif IFD pointer", new TiffBuilder (400).Header (8).Table (8, [(0x0100, 4, 1, 1), .. eleven, (0x8769, 4, 1, 200)])
			.Table (200, [good, uc]).Bytes (300, Uc).ToArray ());
		yield return ("IFD0's 11th warning on its last entry, then its next IFD", new TiffBuilder (400).Header (8).Table (8, [(0x0100, 4, 1, 1), .. eleven], next: 200)
			.Table (200, [uc]).Bytes (300, Uc).ToArray ());
		yield return ("IFD0 stopped, then its next IFD", new TiffBuilder (400).Header (8).Table (8, [(0x0100, 4, 1, 1), .. eleven, (0x0101, 4, 1, 1)], next: 200)
			.Table (200, [uc]).Bytes (300, Uc).ToArray ());
	}

	// Each pointer tag in each kind of table, with each type and a count of 0, 1 and 2; the target holds a UserComment
	// and an ImageDescription (a GPS target: GPSLatitudeRef). Little-endian, and big-endian for the integer types.
	static IEnumerable<(string, byte[])> PointerTypes ()
	{
		int[] types = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 16];
		foreach (var bigEndian in new[] { false, true }) {
			foreach (var pointer in new[] { 0x8769, 0xA005, 0x8825, 0x014A }) {
				foreach (var where in new[] { "IFD0", "ExifIFD", "InteropIFD", "SubIFD", "GPS", "IFD1" }) {
					foreach (var type in bigEndian ? [1, 3, 4, 6, 8, 13] : types) {
						foreach (var count in new[] { 0, 1, 2 })
							yield return ($"pointer {pointer:X4} in {where} type {type} count {count} {(bigEndian ? "BE" : "LE")}", PointerProbe (pointer, where, type, count, bigEndian, 200));
					}
				}

				yield return ($"pointer {pointer:X4} SBYTE 100 {(bigEndian ? "BE" : "LE")}", PointerProbe (pointer, "IFD0", 6, 1, bigEndian, 100));
			}
		}
	}

	static byte[] PointerProbe (int pointer, string where, int type, int count, bool bigEndian, int target)
	{
		var t = new TiffBuilder (400, bigEndian).Header (8);
		var carrier = where switch { "IFD0" => 8, "InteropIFD" => 80, _ => 40 };
		switch (where) {
			case "ExifIFD":
				t.Table (8, [(0x8769, 4, 1, 40)]);
				break;
			case "InteropIFD":
				t.Table (8, [(0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 80)]);
				break;
			case "SubIFD":
				t.Table (8, [(0x014A, 4, 1, 40)]);
				break;
			case "GPS":
				t.Table (8, [(0x8825, 4, 1, 40)]);
				break;
			case "IFD1":
				t.Table (8, [(0x0100, 4, 1, 1)], next: 40);
				break;
		}

		t.Table (carrier, [(pointer, type, count, 0)]).Bytes (carrier + 2 + 8, Field (type, count, target, bigEndian));
		if (pointer == 0x8825)
			t.Table (target, [(0x0001, 2, 2, 0)]).Bytes (target + 2 + 8, "N\0"u8);
		else
			t.Table (target, [(0x9286, 7, Uc.Length, 300), (0x010E, 2, 8, 330)]).Bytes (300, Uc).Bytes (330, "desc-ok\0"u8);
		return t.ToArray ();
	}

	// The value as the type stores it, left-justified in the 4-byte field (a count of 2 repeats a 1- or 2-byte value).
	static byte[] Field (int type, int count, int value, bool bigEndian)
	{
		var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, _ => 4 };
		var field = new byte[4];
		if (size == 1) {
			field[0] = (byte)value;
		} else if (size == 2) {
			(field[0], field[1]) = bigEndian ? ((byte)(value >> 8), (byte)value) : ((byte)value, (byte)(value >> 8));
		} else {
			for (var k = 0; k < 4; k++)
				field[bigEndian ? k : 3 - k] = (byte)(value >> (8 * (3 - k)));
		}

		if (count == 2 && size <= 2)
			field.AsSpan (0, size).CopyTo (field.AsSpan (size));
		return field;
	}

	// Pointers with 1-3 distinct values (BYTE, SHORT, SSHORT, SBYTE inline and out of line) and a LONG array of two,
	// each value a table with its own UserComment: which of them a reader follows. Then offsets 0, in the header, at
	// the data's end, and negative.
	static IEnumerable<(string, byte[])> PointerValues ()
	{
		foreach (var bigEndian in new[] { false, true }) {
			foreach (var pointer in new[] { 0x8769, 0xA005, 0x8825, 0x014A }) {
				foreach (var (type, size) in new[] { (1, 1), (3, 2), (8, 2), (6, 1) }) {
					foreach (var count in new[] { 1, 2, 3 })
						yield return ($"pointer {pointer:X4} type {type} values {count} {(bigEndian ? "BE" : "LE")}", ValuesProbe (pointer, type, size, count, bigEndian));
				}

				yield return ($"pointer {pointer:X4} LONG array of 2 {(bigEndian ? "BE" : "LE")}", ValuesProbe (pointer, 4, 4, 2, bigEndian));
			}
		}

		foreach (var (type, value, name) in new[] { (6, 0xC8L, "SBYTE negative"), (8, 0xFF38L, "SSHORT negative"), (9, 0xFFFFFFC8L, "SLONG negative"), (4, 0L, "LONG 0"), (3, 0L, "SHORT 0"), (4, 3L, "LONG into the header"), (4, 399L, "LONG at the data's end") }) {
			var t = new TiffBuilder (400).Header (8).Table (8, [(0x8769, type, 1, 0)]);
			var field = new byte[4];
			for (var k = 0; k < 4; k++)
				field[k] = (byte)(value >> (8 * k));
			yield return ($"pointer value {name}", t.Bytes (8 + 2 + 8, field).ToArray ());
		}
	}

	static byte[] ValuesProbe (int pointer, int type, int size, int count, bool bigEndian)
	{
		var t = new TiffBuilder (600, bigEndian).Header (8);
		long[] targets = size == 1 ? [100, 140, 180] : [100, 200, 300];
		var bytes = new byte[count * size];
		for (var k = 0; k < count; k++) {
			for (var b = 0; b < size; b++)
				bytes[(k * size) + b] = (byte)(targets[k] >> (8 * (bigEndian ? size - 1 - b : b)));
		}

		t.Table (8, [(pointer, type, count, 0)]);
		if (bytes.Length <= 4)
			t.Bytes (8 + 2 + 8, bytes);
		else
			t.U32 (8 + 2 + 8, 400).Bytes (400, bytes);
		string[] names = ["aaaaa", "bbbbb", "ccccc"];
		for (var k = 0; k < count; k++) {
			var at = (int)targets[k];
			if (pointer == 0x8825)
				t.Table (at, [(0x0001, 2, 2, 0)]).Bytes (at + 2 + 8, [(byte)"NSE"[k], 0]);
			else
				t.Table (at, [(0x9286, 7, Uc.Length, 450 + (40 * k))]).Bytes (450 + (40 * k), Encoding.ASCII.GetBytes ("ASCII\0\0\0" + names[k] + "-uc!"));
		}

		return t.ToArray ();
	}

	// The next IFD of each kind of table: only the main chain's is read.
	static IEnumerable<(string, byte[])> NextIfds ()
	{
		foreach (var kind in new[] { "ExifIFD", "InteropIFD", "SubIFD", "GPS", "IFD1" }) {
			var t = new TiffBuilder (600).Header (8);
			_ = kind switch {
				"ExifIFD" => t.Table (8, [(0x8769, 4, 1, 40)]).Table (40, [(0x9003, 2, 4, 0x30303032)], next: 200),
				"InteropIFD" => t.Table (8, [(0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 80)]).Table (80, [(0x0001, 2, 4, 0x00383952)], next: 200),
				"SubIFD" => t.Table (8, [(0x014A, 4, 1, 40)]).Table (40, [(0x00FE, 4, 1, 0)], next: 200),
				"GPS" => t.Table (8, [(0x8825, 4, 1, 40)]).Table (40, [(0x0001, 2, 2, 0x4E)], next: 200),
				_ => t.Table (8, [(0x0100, 4, 1, 1)], next: 40).Table (40, [(0x0100, 4, 1, 1)], next: 200),
			};
			yield return ($"next IFD of {kind}", t.Table (200, [(0x9286, 7, Uc.Length, 300)]).Bytes (300, Uc).ToArray ());
		}
	}

	// A UserComment value placed in the header, in its own table's count, entries and next offset, before and after
	// the table, inside a parent or child table, and past the data's end.
	static IEnumerable<(string, byte[])> ValuePlaces ()
	{
		foreach (var at in new[] { 26, 27, 30, 39, 41, 54, 65, 66, 70, 300, 395 }) {
			var t = new TiffBuilder (400).Header (8).Table (8, [(0x8769, 4, 1, 40)])
				.Table (40, [(0x9003, 2, 20, 350), (0x9286, 7, Uc.Length, at)]).Bytes (350, "2009:09:09 09:09:09\0"u8);
			if (at is <= 26 or >= 70 and < 386)
				t.Bytes (at, Uc);
			yield return ($"value at {at} by an Exif IFD at 40 to 70", t.ToArray ());
		}

		foreach (var (at, size) in new[] { (0, 8), (2, 8), (4, 8), (7, 8), (8, 8), (2, 17), (80, 17), (83, 17), (84, 17) }) {
			var t = new TiffBuilder (400).Header (100).Table (100, [(0x9286, 7, size, at)]);
			yield return ($"value of {size} at {at} by IFD0 at 100", t.ToArray ());
		}

		yield return ("value inside the parent table", new TiffBuilder (400).Header (100).Table (100, [(0x8769, 4, 1, 200), (0x9003, 2, 20, 300)])
			.Table (200, [(0x9286, 7, 14, 104)]).ToArray ());
		yield return ("value inside a child table", new TiffBuilder (400).Header (100).Table (100, [(0x8769, 4, 1, 200), (0x9286, 7, 14, 204)])
			.Table (200, [(0x9003, 2, 20, 300)]).ToArray ());
	}

	// A format outside 1-13 drops the whole table when it is the first entry, in every kind of table, and is skipped
	// when it is later. An empty IFD0 is read and its next IFD is followed; one dropped for a type-0 first entry is
	// not. Little-endian; the editor tests pin both byte orders. ImageDescription and GPSLatitudeRef are what the
	// golden summary counts.
	static IEnumerable<(string, byte[])> FirstEntryByKind ()
	{
		(int, int, long, long) desc = (0x010E, 2, 4, 0x6C6C6948);
		(int, int, long, long) gps = (0x0001, 2, 2, 0x4E);
		foreach (var kind in new[] { "IFD0", "IFD1", "Exif", "SubIFD", "Interop" }) {
			yield return ($"{kind} type 0 first", KindTable (kind, [(0x0010, 0, 1, 0), desc]));
			yield return ($"{kind} type 14 first", KindTable (kind, [(0x0010, 14, 1, 0), desc]));
			yield return ($"{kind} type 14 later", KindTable (kind, [desc, (0x0010, 14, 1, 0)]));
		}

		yield return ("GPS type 0 first", GpsTable ([(0x0010, 0, 1, 0), gps]));
		yield return ("GPS type 14 first", GpsTable ([(0x0010, 14, 1, 0), gps]));
		yield return ("GPS type 14 later", GpsTable ([gps, (0x0010, 14, 1, 0)]));
		yield return ("the chain after an empty IFD0", new TiffBuilder (80).Header (8).Table (8, [], next: 40).Table (40, [desc]).ToArray ());
		yield return ("the chain after an IFD0 whose first entry is type 0", new TiffBuilder (80).Header (8)
			.Table (8, [(0x0010, 0, 1, 0)], next: 40).Table (40, [desc]).ToArray ());

		static byte[] KindTable (string kind, (int, int, long, long)[] entries)
		{
			var t = new TiffBuilder (120).Header (8);
			switch (kind) {
				case "IFD0": t.Table (8, entries); break;
				case "IFD1": t.Table (8, [(0x0100, 4, 1, 1)], next: 40).Table (40, entries); break;
				case "SubIFD": t.Table (8, [(0x014A, 4, 1, 40)]).Table (40, entries); break;
				case "Exif": t.Table (8, [(0x8769, 4, 1, 40)]).Table (40, entries); break;
				default: t.Table (8, [(0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 80)]).Table (80, entries); break;
			}

			return t.ToArray ();
		}

		static byte[] GpsTable ((int, int, long, long)[] entries) =>
			new TiffBuilder (120).Header (8).Table (8, [(0x8825, 4, 1, 40)]).Table (40, entries).ToArray ();
	}

	// An entry of each type first, in the middle and last of a table with a UserComment, and a UserComment of each
	// type: an unknown format in the first entry drops the table, anywhere else only that entry.
	static IEnumerable<(string, byte[])> UnknownFormats ()
	{
		foreach (var type in new[] { 0, 1, 7, 12, 13, 14, 15, 16, 17, 18, 19, 255 }) {
			(int, int, long, long) odd = (0x9004, type, 1, 0x31323334), date = (0x9003, 2, 4, 0x30303032), uc = (0x9286, 7, Uc.Length, 300);
			foreach (var (where, entries) in new (string, (int, int, long, long)[])[] { ("first", [odd, date, uc]), ("in the middle", [date, odd, uc]), ("last", [date, uc, odd]) }) {
				var t = new TiffBuilder (400).Header (8).Table (8, [(0x8769, 4, 1, 40)]).Table (40, entries).Bytes (300, Uc);
				yield return ($"type {type} {where}", t.ToArray ());
			}

			var typed = new TiffBuilder (400).Header (8).Table (8, [(0x8769, 4, 1, 40)]).Table (40, [date, (0x9286, type, Uc.Length, 300)]).Bytes (300, Uc);
			yield return ($"a UserComment of type {type}", typed.ToArray ());
		}
	}

	// One table reached twice, as two kinds, in several orders: the first reach decides, depth-first, the main
	// chain's next IFD after the subdirectories.
	static IEnumerable<(string, byte[])> ReadOrder ()
	{
		static TiffBuilder Shared (TiffBuilder t) => t.Table (200, [(0x9286, 7, Uc.Length, 300), (0x0001, 2, 2, 0x4E)]).Bytes (300, Uc);
		yield return ("GPS first, then Exif", Shared (new TiffBuilder (500).Header (8).Table (8, [(0x8825, 4, 1, 200), (0x8769, 4, 1, 200)])).ToArray ());
		yield return ("Exif first, then GPS", Shared (new TiffBuilder (500).Header (8).Table (8, [(0x8769, 4, 1, 200), (0x8825, 4, 1, 200)])).ToArray ());
		yield return ("Exif in depth before IFD0's GPS", Shared (new TiffBuilder (500).Header (8).Table (8, [(0x8769, 4, 1, 60), (0x8825, 4, 1, 200)]).Table (60, [(0x8769, 4, 1, 200)])).ToArray ());
		yield return ("IFD0's GPS before IFD1's Exif", Shared (new TiffBuilder (500).Header (8).Table (8, [(0x8825, 4, 1, 200)], next: 100).Table (100, [(0x8769, 4, 1, 200)])).ToArray ());
		yield return ("IFD1 is the Exif IFD", new TiffBuilder (500).Header (8).Table (8, [(0x8769, 4, 1, 100)], next: 100).Table (100, [(0x9286, 7, Uc.Length, 300)]).Bytes (300, Uc).ToArray ());
		yield return ("the Exif IFD is IFD0", new TiffBuilder (500).Header (8).Table (8, [(0x8769, 4, 1, 8), (0x9286, 7, Uc.Length, 300)]).Bytes (300, Uc).ToArray ());
		var kinds = new (string Name, int Tag)[] { ("Exif", 0x8769), ("Interop", 0xA005), ("GPS", 0x8825), ("SubIFD", 0x014A) };
		foreach (var first in kinds) {
			foreach (var then in kinds.Where (k => k != first))
				yield return ($"{first.Name}, then {then.Name}", Shared (new TiffBuilder (500).Header (8).Table (8, [(first.Tag, 4, 1, 200), (then.Tag, 4, 1, 200)])).ToArray ());
		}

		yield return ("the chain after a table dropped for its first entry", new TiffBuilder (500).Header (8).Table (8, [(0x0100, 4, 1, 1)], next: 40)
			.Table (40, [(0x0100, 0x7777, 1, 1)], next: 100).Table (100, [(0x9286, 7, Uc.Length, 300)]).Bytes (300, Uc).ToArray ());
		yield return ("a SubIFD array inside its own table", new TiffBuilder (500).Header (8).Table (8, [(0x014A, 4, 2, 10), (0x9003, 2, 4, 0x30303032)])
			.U32 (22, 200).Table (200, [(0x9286, 7, Uc.Length, 300)]).Bytes (300, Uc).ToArray ());
		yield return ("a SubIFD array after its own table", new TiffBuilder (500).Header (8).Table (8, [(0x014A, 4, 2, 150)])
			.U32 (150, 200).Table (200, [(0x9286, 7, Uc.Length, 300)]).Bytes (300, Uc).ToArray ());
		yield return ("an Interop IFD's Interop and Exif IFDs", new TiffBuilder (500).Header (8).Table (8, [(0x8769, 4, 1, 40)]).Table (40, [(0xA005, 4, 1, 80)])
			.Table (80, [(0xA005, 4, 1, 120), (0x9286, 7, Uc.Length, 300)]).Table (120, [(0x8769, 4, 1, 160)]).Table (160, [(0x9286, 7, Uc.Length, 330)])
			.Bytes (300, Uc).Bytes (330, Uc).ToArray ());
	}
}
