// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ExifTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// The Exif edit over the structure space: every ordered pair of IFD structures, the second placed against the first
/// in every relation (same start, inside, covers, touching or sharing a byte on either side, starting or ending in the
/// first's count, entries, next offset or value), in both byte orders and three carriers, through every field edit,
/// a no-op save and Clear.
/// </summary>
/// <remarks>
/// The oracle is the tests' own reader (<see cref="ExifReaderModel"/>, checked against exiftool for every input
/// here by <see cref="ExifReaderGoldenTests"/>). Two-sided and judged by consequence: a save must leave exactly what
/// any reader sees, less the removed copies (no value masked), and exiftool must still read the same tables with
/// the same other entries; a refusal is right only when the same edit made without its check
/// (<see cref="ExifEditor.SkipCheck"/>) would have changed something either view sees, or when the unchecked edit
/// is refused too (the linear bound); a field with no copy always saves with the Exif bytes unchanged.
/// The test also asserts its own coverage: inputs, saves and refusals per relation, both byte orders, and every
/// structure where the canonical input puts it.
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
public class ExifEditMatrixTests
{
	// Tables: IFD0 and the chain after it (IFD1, IFD2); Exif IFDs at depth 1 and 2, one IFD1 points at, one an
	// IFD0 pointer typed SHORT names, and one only TagLib# reads (IFD0's 0x8769 typed ASCII, its field a LONG); a SubIFD given inline and one given by an offset array; an interoperability
	// IFD and an Exif IFD under it; a GPS table holding an Exif IFD pointer and a SubIFD pointer to a table no reader
	// reads (GpsChild); a pointer target whose entries do not fit. Values: the removed UserComment of every table that
	// holds one, the removed XPComment, a kept Artist value, and IFD1's thumbnail data.
	internal enum S
	{
		Ifd0, Exif, Nested, Ifd1, Ifd2, Ifd1Exif, ShortExif, RawExif, SubIfd, SubArray, SubIfd2, Interop, InteropExif, Gps, GpsChild, Unread,
		UcExif, UcNested, UcIfd1Exif, UcIfd2, UcShort, UcRaw, UcSub, UcSub2, UcInterop, UcInteropExif, UcGpsChild, Xp, Kept, Thumb,
	}

	static readonly S[] All = Enum.GetValues<S> ();

	static byte[] Uc (string text) => Concat ("ASCII\0\0\0"u8.ToArray (), Encoding.ASCII.GetBytes (text));

	static readonly Dictionary<S, byte[]> Values = new () {
		[S.UcExif] = Uc ("uc-exif!"),
		[S.UcNested] = Uc ("uc-nest!"),
		[S.UcIfd1Exif] = Uc ("uc-ifd1!"),
		[S.UcIfd2] = Uc ("uc-ifd2!"),
		[S.UcShort] = Uc ("uc-shrt!"),
		[S.UcRaw] = Uc ("uc-raw!!"),
		[S.UcSub] = Uc ("uc-subi!"),
		[S.UcSub2] = Uc ("uc-sub2!"),
		[S.UcInterop] = Uc ("uc-iop!!"),
		[S.UcInteropExif] = Uc ("uc-iopx!"),
		[S.UcGpsChild] = Uc ("uc-gpsc!"),
		[S.SubArray] = new byte[8],
		[S.Xp] = Encoding.Unicode.GetBytes ("xp-old\0"),
		[S.Kept] = "kept-value\0\0"u8.ToArray (),
		[S.Thumb] = "thumbnail-bytes!"u8.ToArray (),
	};

	// Each table's entries: (tag, type, count, the structure its value names, or a literal value).
	static (int Tag, int Type, int Count, S? Names, long Literal)[] EntriesOf (S s) => s switch {
		S.Ifd0 => [(0x8769, 4, 1, S.Exif, 0), (0x8769, 4, 1, S.Unread, 0), (0x8825, 4, 1, S.Gps, 0), (0x014A, 4, 1, S.SubIfd, 0),
			(0x014A, 4, 2, S.SubArray, 0), (0x9C9C, 1, Values[S.Xp].Length, S.Xp, 0), (0x013B, 2, Values[S.Kept].Length, S.Kept, 0), (0x8769, 3, 1, S.ShortExif, 0),
			(0x8769, 2, 4, S.RawExif, 0)],
		S.Exif => [(0x9286, 7, Values[S.UcExif].Length, S.UcExif, 0), (0x8769, 4, 1, S.Nested, 0), (0xA005, 4, 1, S.Interop, 0)],
		S.Ifd1 => [(0x8769, 4, 1, S.Ifd1Exif, 0), (0x9003, 7, 4, null, 0x31303032), (0x0201, 4, 1, S.Thumb, 0), (0x0202, 4, 1, null, Values[S.Thumb].Length)],
		S.Interop => [(0x0001, 2, 4, null, 0x00383952), (0x9286, 7, Values[S.UcInterop].Length, S.UcInterop, 0), (0x8769, 4, 1, S.InteropExif, 0)],
		S.Gps => [(0x0001, 2, 2, null, 0x4E), (0x8769, 4, 1, S.GpsChild, 0), (0x014A, 4, 1, S.GpsChild, 0)],
		_ => [(0x9286, 7, Values[ValueOf (s)].Length, ValueOf (s), 0)],
	};

	static S ValueOf (S table) => table switch {
		S.Nested => S.UcNested,
		S.Ifd1Exif => S.UcIfd1Exif,
		S.Ifd2 => S.UcIfd2,
		S.ShortExif => S.UcShort,
		S.RawExif => S.UcRaw,
		S.SubIfd => S.UcSub,
		S.SubIfd2 => S.UcSub2,
		S.InteropExif => S.UcInteropExif,
		S.GpsChild => S.UcGpsChild,
		_ => S.UcExif
	};

	static bool IsTable (S s) => s is not S.Unread && !Values.ContainsKey (s);

	static int LengthOf (S s) => s switch {
		S.Unread => 2,
		_ when Values.TryGetValue (s, out var value) => value.Length,
		_ => 2 + (12 * EntriesOf (s).Length) + 4,
	};

	// The canonical layout: every structure on its own, 4 bytes apart.
	internal static Dictionary<S, int> Canonical ()
	{
		var at = new Dictionary<S, int> ();
		var next = 8;
		foreach (var s in All) {
			at[s] = next;
			next += LengthOf (s) + 4;
		}

		return at;
	}

	// Where X goes against Y. Disjoint is the canonical layout itself (every pair 4 or more bytes apart).
	internal static readonly string[] Relations = [
		"same start", "inside", "covers", "touches before", "touches after", "shares first byte", "shares last byte",
		"starts in count", "starts in entries", "starts in next", "starts in value",
		"ends in count", "ends in entries", "ends in next", "ends in value",
	];

	static int? Place (string relation, S x, int yAt, S y)
	{
		int lx = LengthOf (x), ly = LengthOf (y), yEnd = yAt + ly;
		var table = IsTable (y);
		int? anchor = relation[(relation.LastIndexOf (' ') + 1)..] switch {
			"count" when table || y == S.Unread => yAt + 1,
			"entries" when table => yAt + 2 + 6,
			"next" when table => yEnd - 2,
			"value" when !table && y != S.Unread => yAt + 1,
			_ => null,
		};
		int? at = relation switch {
			"same start" => yAt,
			"inside" => lx <= ly - 2 ? yAt + ((ly - lx) / 2) : null,
			"covers" => lx >= ly + 2 ? yAt - ((lx - ly) / 2) : null,
			"touches before" => yAt - lx,
			"touches after" => yEnd,
			"shares first byte" => yAt - lx + 1,
			"shares last byte" => yEnd - 1,
			_ when anchor is null => null,
			_ when relation.StartsWith ("starts", StringComparison.Ordinal) => anchor,
			_ => anchor + 1 - lx,
		};
		return at >= 8 ? at : null;
	}

	// Writes every structure at its place, X last so that X is whole and what it lands on is overwritten.
	internal static byte[] Build (Dictionary<S, int> at, S? last, bool bigEndian)
	{
		var tiff = new TiffBuilder (All.Max (s => at[s] + LengthOf (s)) + 16, bigEndian).Header (at[S.Ifd0]);
		foreach (var s in All) {
			if (s != last)
				Write (tiff, at, s);
		}

		if (last is S x)
			Write (tiff, at, x);
		return tiff.ToArray ();
	}

	static void Write (TiffBuilder tiff, Dictionary<S, int> at, S s)
	{
		if (s == S.Unread) {
			// 257 entries: they do not fit, so no reader reads this table.
			tiff.U16 (at[s], 0x0101);
			return;
		}

		if (s == S.SubArray) {
			// Two offsets: the second SubIFD, and 0 (no table).
			tiff.U32 (at[s], at[S.SubIfd2]).U32 (at[s] + 4, 0);
			return;
		}

		if (!IsTable (s)) {
			tiff.Bytes (at[s], Values[s]);
			return;
		}

		var entries = EntriesOf (s);
		long? next = s == S.Ifd0 ? at[S.Ifd1] : s == S.Ifd1 ? at[S.Ifd2] : 0;
		tiff.Table (at[s], [.. entries.Select (e => (e.Tag, e.Type, (long)e.Count, e.Names is S named ? (long)at[named] : e.Literal))], next);
		// A SHORT pointer holds its offset in the first two bytes of the field, in the file's byte order.
		for (var n = 0; n < entries.Length; n++) {
			if (entries[n].Type == 3 && entries[n].Names is S named)
				tiff.U32 (at[s] + 2 + (12 * n) + 8, 0).U16 (at[s] + 2 + (12 * n) + 8, at[named]);
		}
	}

	/// <summary>Every input: the canonical layout, then each relation of each ordered pair, in each byte order.</summary>
	internal static IEnumerable<(string Name, byte[] Tiff)> Inputs ()
	{
		var canonical = Canonical ();
		foreach (var bigEndian in new[] { false, true }) {
			yield return ($"{(bigEndian ? "BE" : "LE")} [disjoint] every pair", Build (canonical, null, bigEndian));
			foreach (var x in All) {
				foreach (var y in All) {
					if (x == y)
						continue;
					// Two relations that put X at the same offset are one input, named by the first.
					var seen = new HashSet<int> ();
					foreach (var relation in Relations) {
						if (Place (relation, x, canonical[y], y) is not int at || !seen.Add (at))
							continue;
						var placed = new Dictionary<S, int> (canonical) { [x] = at };
						yield return ($"{(bigEndian ? "BE" : "LE")} {x} [{relation}] {y}", Build (placed, x, bigEndian));
					}
				}
			}
		}
	}

	static readonly string[] Carriers = ["jpeg APP1", "png eXIf", "png raw EXIF profile"];

	sealed class Tally
	{
		public int Inputs;
		public int Saved;
		public int Bound;
		public readonly Dictionary<string, int> InputsBy = [];
		public readonly Dictionary<string, int> SavedBy = [];
		public readonly Dictionary<string, int> RefusedBy = [];
		public readonly Dictionary<string, int> FailedBy = [];
		public readonly List<string> Failures = [];

		public static void Add (Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault (key) + 1;

		public void Fail (string kind, string message)
		{
			Add (FailedBy, kind);
			if (Failures.Count < 40)
				Failures.Add (message);
		}
	}

	// What each operation asks to remove. A JPEG stores only the comment, so its other setters change nothing.
	static HashSet<int> RemovedBy (string operation, string carrier) => operation switch {
		"Comment" or "Comment null" => [0x9286, 0x9C9C],
		"Artist" when carrier != "jpeg APP1" => [0x013B, 0x9C9D],
		"Title" when carrier != "jpeg APP1" => [0x9C9B],
		"Description" when carrier != "jpeg APP1" => [0x010E],
		"Copyright" when carrier != "jpeg APP1" => [0x8298],
		_ => [],
	};

	static void Apply (string operation, ImageTag tag)
	{
		switch (operation) {
			case "Comment":
				tag.Comment = "new";
				break;
			case "Comment null":
				tag.Comment = null;
				break;
			case "Artist":
				tag.Artist = "new";
				break;
			case "Title":
				tag.Title = "new";
				break;
			case "Description":
				tag.Description = "new";
				break;
			case "Copyright":
				tag.Copyright = "new";
				break;
			case "Clear":
				tag.Clear ();
				break;
		}
	}

	// The save, and its output (the input when refused).
	static (bool Saved, string? Error, byte[] Output) Save (byte[] input, string carrier, string operation, bool unchecked_)
	{
		var file = (StillImageFile)MediaFile.ReadFromData (input, carrier.StartsWith ("jpeg", StringComparison.Ordinal) ? "a.jpg" : "a.png").File!;
		Apply (operation, (ImageTag)file.Tag!);
		ExifEditor.SkipCheck = unchecked_;
		try {
			var save = file.SaveToFile ("/out", new MockFileSystem ());
			return (save.IsSuccess, save.Error, file.Render ().ToArray ());
		} finally {
			ExifEditor.SkipCheck = false;
		}
	}

	static Tally RunMatrix (string operation)
	{
		var tally = new Tally ();
		foreach (var (name, tiff) in Inputs ()) {
			var before = ExifReaderModel.AnyReader (tiff);
			foreach (var carrier in Carriers) {
				tally.Inputs++;
				Tally.Add (tally.InputsBy, RelationOf (name));
				Check (tally, operation, carrier, name, tiff, before);
			}
		}

		return tally;
	}

	static void Check (Tally tally, string operation, string carrier, string name, byte[] tiff, ExifReaderModel before)
	{
		var cell = $"{operation} | {carrier} | {name}";
		var input = InCarrier (carrier, tiff);
		var (saved, error, output) = Save (input, carrier, operation, unchecked_: false);
		if (operation == "Clear") {
			if (!saved || ExifIn (carrier, output) is not null)
				tally.Fail ("Clear left Exif or was refused", $"{cell}: {error}");
			else
				tally.Saved++;
			return;
		}

		var removed = RemovedBy (operation, carrier);
		var expected = before.Visible (removed);
		if (!before.Tables.Any (t => t.ReadsExifTags && Enumerable.Range (0, t.Count).Any (n => removed.Contains (before.U16 (t.At + 2 + (12 * n)))))) {
			// Nothing to remove: never refused, and the Exif block keeps every byte. The one exception is the linear
			// bound: past it the walk stops, and the edit is refused when the block's bytes hold one of the field's
			// tags anywhere, as some table may (TagLib# reads the tag at any type).
			if (!saved && error!.Contains ("beyond what its data holds", StringComparison.Ordinal) && HoldsTag (tiff, removed))
				tally.Bound++;
			else if (!saved)
				tally.Fail ("refused with nothing to remove", $"{cell}: {error}");
			else if (!ExifIn (carrier, output)!.AsSpan ().SequenceEqual (tiff))
				tally.Fail ("changed with nothing to remove", $"{cell}: the Exif block changed");
			else
				tally.Saved++;
			return;
		}

		if (saved) {
			tally.Saved++;
			Tally.Add (tally.SavedBy, RelationOf (name));
			var got = ExifReaderModel.AnyReader (ExifIn (carrier, output)!).Visible ([]);
			if (got != expected)
				tally.Fail ("saved, and readers see more than the copies change", $"{cell}: readers would show\n    {got}\n  instead of\n    {expected}");
			var exifAfter = ExiftoolView (ExifIn (carrier, output)!, removed);
			if (exifAfter != ExiftoolView (tiff, removed))
				tally.Fail ("saved, and exiftool reads a different table", $"{cell}: exiftool would show\n    {exifAfter}\n  instead of\n    {ExiftoolView (tiff, removed)}");
			return;
		}

		// Refused: right when the unchecked edit would change what a reader sees, including which tables exiftool
		// reads, or is refused too (the bound).
		var (rawSaved, _, rawOutput) = Save (input, carrier, operation, unchecked_: true);
		Tally.Add (tally.RefusedBy, RelationOf (name));
		if (!rawSaved)
			tally.Bound++;
		else if (ExifReaderModel.AnyReader (ExifIn (carrier, rawOutput)!).Visible ([]) == expected
			&& ExiftoolView (ExifIn (carrier, rawOutput)!, removed) == ExiftoolView (tiff, removed))
			tally.Fail ("refused when safe", $"{cell}: refused, but the edit changes nothing else readers see: {error}");
	}

	// What exiftool shows once the removed tags are gone, in every table. A save or a refusal is wrong when the
	// edit changes which tables it reads or which other entries those tables hold: compaction can move a format
	// it does not read into the first entry, or out of it.
	static string ExiftoolView (byte[] tiff, HashSet<int> removed) => ExifReaderModel.Exiftool (tiff).Visible (removed, anyTable: true);

	// Whether any two bytes of the block, read in its byte order, are one of the tags.
	static bool HoldsTag (byte[] tiff, HashSet<int> tags)
	{
		var le = tiff[0] == (byte)'I';
		for (var at = 0; at + 2 <= tiff.Length; at++) {
			if (tags.Contains (le ? tiff[at] | (tiff[at + 1] << 8) : (tiff[at] << 8) | tiff[at + 1]))
				return true;
		}

		return false;
	}

	static string RelationOf (string name) => name[(name.IndexOf ('[') + 1)..name.IndexOf (']')];

	static string Format (Dictionary<string, int> counts) =>
		string.Join ("; ", counts.OrderBy (p => p.Key, StringComparer.Ordinal).Select (p => $"{p.Key}: {p.Value}"));

	[TestMethod]
	[DataRow ("Comment")]
	[DataRow ("Comment null")]
	[DataRow ("Artist")]
	[DataRow ("Title")]
	[DataRow ("Description")]
	[DataRow ("Copyright")]
	[DataRow ("no-op save")]
	[DataRow ("Clear")]
	public void EveryStructurePairAndRelation_SavesExactlyWhatReadersShouldSee_OrRefusesOnlyWhenUnsafe (string operation)
	{
		var tally = RunMatrix (operation);

		Console.WriteLine ($"{operation}: {tally.Inputs} inputs ({Format (tally.InputsBy)}); {tally.Saved} saved ({Format (tally.SavedBy)}); refused by relation: {Format (tally.RefusedBy)}; at the bound: {tally.Bound}");
		Assert.AreEqual (0, tally.FailedBy.Values.Sum (), $"{operation}: failing cells of {tally.Inputs} by kind: {Format (tally.FailedBy)}\n" + string.Join ("\n", tally.Failures));
		Assert.AreEqual (16, tally.InputsBy.Count, $"{operation}: every relation has inputs");
		Assert.IsTrue (tally.InputsBy.Values.All (n => n >= 3 * 2), $"{operation}: every relation in both byte orders and every carrier");
		if (operation is "Comment" or "Comment null") {
			// The edit both saves and refuses in every relation but the base layout (which only saves).
			Assert.AreEqual (16, tally.SavedBy.Count, $"{operation}: saves per relation: {Format (tally.SavedBy)}");
			Assert.AreEqual (15, tally.RefusedBy.Count, $"{operation}: refusals per relation: {Format (tally.RefusedBy)}");
			Assert.IsFalse (tally.RefusedBy.ContainsKey ("disjoint"), "the base layout saves");
		}
	}

	// The coverage the matrix rests on: in the base layout every table is where readers find it, in its kinds, every
	// copy is shown, the table under the GPS pointers and the unread target are read by no one, and both byte orders
	// are generated alike.
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void TheBaseLayout_PutsEveryStructureWhereReadersFindIt (bool bigEndian)
	{
		var at = Canonical ();

		var model = ExifReaderModel.AnyReader (Build (at, null, bigEndian));

		var tables = model.Tables.ToDictionary (t => t.At, t => string.Join ("+", t.Kinds.OrderBy (k => k)));
		var expected = new Dictionary<S, string> {
			[S.Ifd0] = "Main",
			[S.Ifd1] = "Main",
			[S.Ifd2] = "Main",
			[S.Exif] = "Exif",
			[S.Nested] = "Exif",
			[S.Ifd1Exif] = "Exif",
			[S.ShortExif] = "Exif",
			[S.RawExif] = "Exif",
			[S.InteropExif] = "Exif",
			[S.SubIfd] = "SubIfd",
			[S.SubIfd2] = "SubIfd",
			[S.Interop] = "Interop",
			[S.Gps] = "Gps",
		};
		foreach (var (s, kinds) in expected)
			Assert.AreEqual (kinds, tables.GetValueOrDefault (at[s]), $"{s} at {at[s]}");
		Assert.AreEqual (expected.Count, tables.Count, "no other table is read: not GpsChild, not the unread target");
		CollectionAssert.AreEqual (new long[] { at[S.Unread] }, model.Unread);
		var copies = model.CopiesOf (0x9286);
		foreach (var table in new[] { S.Exif, S.Nested, S.Ifd1Exif, S.Ifd2, S.ShortExif, S.RawExif, S.SubIfd, S.SubIfd2, S.Interop, S.InteropExif })
			Assert.AreEqual (1, copies.Count (c => c == at[table]), $"{table} shows its UserComment");
		Assert.AreEqual (10, copies.Count);
		Assert.AreEqual (Inputs ().Count (i => i.Name.StartsWith ("LE", StringComparison.Ordinal)), Inputs ().Count (i => i.Name.StartsWith ("BE", StringComparison.Ordinal)), "both byte orders alike");
	}
}
