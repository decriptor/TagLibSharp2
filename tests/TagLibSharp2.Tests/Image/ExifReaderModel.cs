// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// The tests' own Exif reader, written from measured reader behaviour and sharing nothing with the code under test.
/// The rules are the ones recorded from exiftool 13.55 in <c>Golden/exif-reader-probes.txt</c> (each row a probe
/// and what exiftool showed), and <see cref="ExifReaderGoldenTests"/> checks this reader against exiftool's own output
/// for every probe and every matrix input, so a rule that drifts from exiftool turns the tests red.
/// </summary>
/// <remarks>
/// <para>Measured rules: a classic TIFF header; a table is read when it starts at byte 8 or later and its count and
/// entries lie in the data (the next offset may be missing). IFD0 and the chain of next IFDs, Exif IFDs (0x8769),
/// SubIFDs (0x014A) and interoperability IFDs (0xA005) are read with the Exif tags and their pointers are followed;
/// GPS IFDs (0x8825) are read with the GPS tags and nothing is followed from them; the next IFD of anything but the
/// main chain is not read. Pointer values: BYTE, SHORT, LONG, SBYTE, SSHORT, SLONG, IFD, and UNDEFINED with count 1;
/// 0x8769 only with count 1; 0x8825 and 0xA005 by their first value; 0x014A by every value; out-of-line values must
/// all lie in the data; a negative value names nothing.</para>
/// <para>Two views. <see cref="Exiftool"/> is exactly what exiftool shows: depth-first in entry order, the main
/// chain's next IFD after a table's subdirectories (not after a table it dropped), each offset read once in the kind
/// it is first reached as (but an interoperability IFD is read as GPS too), a pointer array it hides as a value not
/// followed, a table
/// read without its entries of an unknown format (0, or 14 and above) and not at all when its first entry has one,
/// read only up to its 11th warning (an unknown format other than 0, or a value past the data or suspicious) when
/// entries follow it, an aborted main table ending the chain, and a value hidden when it starts in the
/// 8-byte header, runs past the data, or overlaps its own table's count and entries. <see cref="AnyReader"/> is what
/// some reader may read: every table reached in any kind, every entry, every value in the data, and the tables
/// TagLib# reads through IFD0's Exif and GPS pointers, whose 4-byte field it reads as a LONG whatever their type and
/// count (measured: a SHORT, ASCII, RATIONAL, count-0 or count-2 0x8769 or 0x8825 in IFD0). It is the view a save
/// must keep.</para>
/// </remarks>
sealed class ExifReaderModel
{
	public enum Kind { Main, Exif, SubIfd, Interop, Gps }

	/// <summary>The field tags the tests count: UserComment, the XP tags, ImageDescription, Artist, Copyright.</summary>
	public static readonly int[] FieldTags = [0x9286, 0x9C9B, 0x9C9C, 0x9C9D, 0x010E, 0x013B, 0x8298];

	public sealed class Table (long at, int count)
	{
		public long At { get; } = at;
		public int Count { get; } = count;
		public HashSet<Kind> Kinds { get; } = [];

		/// <summary>
		/// The entries a reader reads: all of them, or for exiftool none when the first has an unknown format, and those
		/// up to its 11th warning.
		/// </summary>
		public int Read { get; set; } = count;

		/// <summary>exiftool stopped reading the table at too many warnings; for IFD0's chain, the chain ends there.</summary>
		public bool Aborted { get; set; }

		public bool ReadsExifTags => Kinds.Any (k => k != Kind.Gps);
	}

	readonly byte[] _t;
	readonly bool _le;
	readonly bool _exiftool;

	ExifReaderModel (byte[] tiff, bool exiftool)
	{
		_t = tiff;
		_le = tiff.Length >= 2 && tiff[0] == (byte)'I';
		_exiftool = exiftool;
	}

	public List<Table> Tables { get; } = [];

	/// <summary>Pointer targets inside the data where no table can be read.</summary>
	public List<long> Unread { get; } = [];

	/// <summary>Every pointer value read (for the bound of one per 4 bytes).</summary>
	public long PointerValues { get; private set; }

	public long Entries => Tables.Sum (t => (long)t.Count);

	/// <summary>The view a save must keep: everything some reader may read.</summary>
	public static ExifReaderModel Of (byte[] tiff) => AnyReader (tiff);

	public static ExifReaderModel AnyReader (byte[] tiff) => new ExifReaderModel (tiff, exiftool: false).WalkAll ();

	public static ExifReaderModel Exiftool (byte[] tiff) => new ExifReaderModel (tiff, exiftool: true).WalkDepthFirst ();

	public int U16 (long at) => _le ? _t[at] | (_t[at + 1] << 8) : (_t[at] << 8) | _t[at + 1];

	public long U32 (long at) => _le
		? _t[at] | ((long)_t[at + 1] << 8) | ((long)_t[at + 2] << 16) | ((long)_t[at + 3] << 24)
		: ((long)_t[at] << 24) | ((long)_t[at + 1] << 16) | ((long)_t[at + 2] << 8) | _t[at + 3];

	bool Classic => _t.Length >= 8 && (_le ? _t[0] == 'I' && _t[1] == 'I' && _t[2] == 42 && _t[3] == 0 : _t[0] == 'M' && _t[1] == 'M' && _t[2] == 0 && _t[3] == 42);

	bool Readable (long at) => at >= 8 && at + 2 <= _t.Length && at + 2 + (12L * U16 (at)) <= _t.Length;

	static Kind? KindOf (int tag) => tag switch {
		0x8769 => Kind.Exif,
		0x014A => Kind.SubIfd,
		0xA005 => Kind.Interop,
		0x8825 => Kind.Gps,
		_ => null
	};

	// Bytes per value of a type, for pointers: the integer and IFD types (UNDEFINED is checked by the caller).
	static int PointerSize (int type) => type switch { 1 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 13 => 4, _ => 0 };

	// The tables a pointer entry names, as exiftool follows them. exiftool also ignores an out-of-line array that it
	// hides as a value (in the header, or overlapping its own table).
	List<long> Targets (Table table, long entry)
	{
		var targets = new List<long> ();
		var tag = U16 (entry);
		var type = U16 (entry + 2);
		var count = U32 (entry + 4);
		var size = PointerSize (type);
		if (size == 0 || count == 0 || (type == 7 && count != 1) || (tag == 0x8769 && count != 1))
			return targets;
		var at = count * size <= 4 ? entry + 8 : U32 (entry + 8);
		if (at + (count * size) > _t.Length || (count * size > 4 && _exiftool && Hidden (table, at, count * size)))
			return targets;
		var read = tag is 0x8825 or 0xA005 ? 1 : count;
		for (var k = 0L; k < read; k++) {
			var p = at + (k * size);
			long value = size == 1 ? _t[p] : size == 2 ? U16 (p) : U32 (p);
			var signed = type is 6 or 8 or 9;
			if (!signed || value < (1L << ((8 * size) - 1)))
				targets.Add (value);
		}

		PointerValues += read;
		return targets;
	}

	// Every table any reader reaches, breadth-first, with every kind it is reached as.
	ExifReaderModel WalkAll ()
	{
		if (!Classic)
			return this;
		var index = new Dictionary<long, Table> ();
		var queue = new Queue<(long At, Kind Kind)> ();
		queue.Enqueue ((U32 (4), Kind.Main));
		while (queue.Count > 0) {
			var (at, kind) = queue.Dequeue ();
			if (!Readable (at)) {
				if (at >= 8 && at < _t.Length && !Unread.Contains (at))
					Unread.Add (at);
				continue;
			}

			if (!index.TryGetValue (at, out var table)) {
				table = new Table (at, U16 (at));
				index[at] = table;
				Tables.Add (table);
			}

			var readBefore = table.ReadsExifTags;
			if (!table.Kinds.Add (kind) || kind == Kind.Gps || readBefore)
				continue;
			for (var n = 0; n < table.Count; n++) {
				var e = at + 2 + (12 * n);
				if (KindOf (U16 (e)) is Kind child)
					foreach (var target in Targets (table, e))
						queue.Enqueue ((target, child));
				if (at == U32 (4) && U16 (e) is 0x8769 or 0x8825)
					queue.Enqueue ((U32 (e + 8), U16 (e) == 0x8769 ? Kind.Exif : Kind.Gps));
			}

			if (kind == Kind.Main && at + 2 + (12 * table.Count) + 4 <= _t.Length)
				queue.Enqueue ((U32 (at + 2 + (12 * table.Count)), Kind.Main));
		}

		return this;
	}

	// exiftool's walk: depth-first in entry order, each offset once, a table stopped at an unknown format.
	ExifReaderModel WalkDepthFirst ()
	{
		if (!Classic)
			return this;
		var seen = new HashSet<long> ();
		var frames = new Stack<Frame> ();
		Enter (U32 (4), Kind.Main, seen, frames);
		while (frames.Count > 0) {
			var frame = frames.Peek ();
			if (frame.Pending.Count > 0) {
				Enter (frame.Pending.Dequeue (), frame.PendingKind, seen, frames);
				continue;
			}

			if (frame.Index < frame.Table.Read) {
				Step (frame);
				continue;
			}

			frames.Pop ();
			var t = frame.Table;
			if (frame.Kind == Kind.Main && !t.Aborted && (t.Read > 0 || t.Count == 0) && t.At + 2 + (12 * t.Count) + 4 <= _t.Length)
				Enter (U32 (t.At + 2 + (12 * t.Count)), Kind.Main, seen, frames);
		}

		return this;
	}

	sealed class Frame (Table table, Kind kind)
	{
		public Table Table { get; } = table;
		public Kind Kind { get; } = kind;
		public int Index { get; set; }
		public int Warnings { get; set; }
		public Queue<long> Pending { get; } = new ();
		public Kind PendingKind { get; set; }
	}

	void Enter (long at, Kind kind, HashSet<long> seen, Stack<Frame> frames)
	{
		if (!seen.Add (at)) {
			// The one exception to reading an offset once: a table read as an interoperability IFD is read as GPS too.
			if (kind == Kind.Gps && Tables.Find (t => t.At == at) is Table read && read.Kinds.SetEquals ([Kind.Interop]))
				read.Kinds.Add (Kind.Gps);
			return;
		}

		if (!Readable (at)) {
			if (at >= 8 && at < _t.Length)
				Unread.Add (at);
			return;
		}

		var table = new Table (at, U16 (at));
		table.Kinds.Add (kind);
		Tables.Add (table);
		frames.Push (new Frame (table, kind));
	}

	void Step (Frame frame)
	{
		var t = frame.Table;
		var e = t.At + 2 + (12 * frame.Index);
		var type = U16 (e + 2);
		if (type is 0 or > 13 && frame.Index == 0) {
			t.Read = 0;
			return;
		}

		// exiftool stops reading a table at the entry after its 11th warning ("Too many warnings -- parsing aborted"),
		// counted per table: an 11th warning on the last entry stops nothing.
		if (frame.Warnings > 10) {
			t.Read = frame.Index;
			t.Aborted = true;
			return;
		}

		if (Warns (t, e, type))
			frame.Warnings++;
		frame.Index++;
		if (type is not (0 or > 13) && frame.Kind != Kind.Gps && KindOf (U16 (e)) is Kind child) {
			foreach (var target in Targets (t, e))
				frame.Pending.Enqueue (target);
			frame.PendingKind = child;
		}
	}

	// The entries exiftool counts toward that limit: an unknown format other than 0, and an out-of-line value that runs
	// past the data or that it calls suspicious. Pointer warnings and tags out of sequence do not count (measured).
	bool Warns (Table table, long e, int type)
	{
		if (type is 0 or > 13)
			return type != 0;
		var size = TypeSize (type) * U32 (e + 4);
		var at = U32 (e + 8);
		return size > 4 && (at + size > _t.Length || Hidden (table, at, size));
	}

	static long TypeSize (int type) => type switch {
		1 or 2 or 6 or 7 => 1,
		3 or 8 => 2,
		4 or 9 or 11 or 13 => 4,
		5 or 10 or 12 => 8,
		_ => 0
	};

	// An entry's value bytes as the view reads them: inline, out of line, or null when exiftool hides it.
	byte[]? ValueOf (Table table, long e)
	{
		var size = TypeSize (U16 (e + 2)) * U32 (e + 4);
		if (size <= 4)
			return _t[(int)(e + 8)..(int)(e + 12)];
		var at = U32 (e + 8);
		if (at + size > _t.Length || (_exiftool && Hidden (table, at, size)))
			return null;
		return _t[(int)at..(int)(at + size)];
	}

	// exiftool's "suspicious offset": a value that starts in the header or overlaps its own table's count and entries.
	static bool Hidden (Table table, long at, long size) => at < 8 || (at < table.At + 2 + (12L * table.Count) && table.At < at + size);

	// The entries a reader shows: exiftool skips an entry of an unknown format, and the whole table when its first
	// entry has one.
	IEnumerable<(Table Table, long Entry)> Shown ()
	{
		foreach (var t in Tables) {
			for (var n = 0; n < t.Read; n++) {
				var e = t.At + 2 + (12 * n);
				if (!_exiftool || U16 (e + 2) is not (0 or > 13))
					yield return (t, e);
			}
		}
	}

	/// <summary>One table offset per copy of one of the tags a reader shows: in a table read with the Exif tags.</summary>
	public List<long> CopiesOf (params int[] tags) =>
		[.. Shown ().Where (s => s.Table.ReadsExifTags && Array.IndexOf (tags, U16 (s.Entry)) >= 0 && ValueOf (s.Table, s.Entry) is not null).Select (s => s.Table.At)];

	/// <summary>The ASCII values shown from the tables read with the Exif tags, by tag (a later one replaces an earlier one).</summary>
	public Dictionary<int, string> Strings ()
	{
		var strings = new Dictionary<int, string> ();
		foreach (var (table, e) in Shown ()) {
			if (table.ReadsExifTags && U16 (e + 2) == 2 && ValueOf (table, e) is byte[] value)
				strings[U16 (e)] = Encoding.ASCII.GetString (value).TrimEnd ('\0');
		}

		return strings;
	}

	/// <summary>
	/// What a reader shows, as text: each table in offset order with its kinds, every entry read with its value bytes,
	/// its next offset, and the bytes its offset/length pairs name (thumbnail, strips, tiles). Entries with a tag in <paramref name="removed"/> are left out of tables read with the Exif
	/// tags. Nothing is masked: a value that lies on a table is compared too.
	/// </summary>
	public string Visible (HashSet<int> removed)
	{
		var text = new StringBuilder ();
		foreach (var t in Tables.OrderBy (t => t.At)) {
			text.Append ('[').Append (t.At).Append (' ').Append (string.Join ("+", t.Kinds.OrderBy (k => k))).Append (']');
			foreach (var (_, e) in Shown ().Where (s => s.Table == t)) {
				if (t.ReadsExifTags && removed.Contains (U16 (e)))
					continue;
				// An out-of-line value in the data is shown with its offset: where it lies decides whether exiftool shows
				// it (the suspicious-offset rule). One past the data is shown by no reader, wherever it points, and nor is
				// the out-of-line value of an Exif, GPS or interoperability IFD pointer (exiftool: "Bad value"; what it
				// leads to is in the tables).
				var size = TypeSize (U16 (e + 2)) * U32 (e + 4);
				var value = size > 4 && U16 (e) is 0x8769 or 0x8825 or 0xA005 ? null : ValueOf (t, e);
				text.Append (U16 (e).ToString ("X4", CultureInfo.InvariantCulture)).Append ('/').Append (U16 (e + 2)).Append ('/').Append (U32 (e + 4))
					.Append ('=').Append (value is null ? "OOR" : Convert.ToHexString (value));
				if (value is not null && size > 4)
					text.Append ('@').Append (U32 (e + 8));
				text.Append (';');
			}

			var nextAt = t.At + 2 + (12L * t.Count);
			text.Append (" next=").Append (nextAt + 4 <= _t.Length ? U32 (nextAt) : 0).Append (' ');
			if (t.ReadsExifTags)
				AppendData (text, t);
		}

		return text.ToString ();
	}

	// TIFF 6.0 offset/length pairs a reader extracts: the IFD1 thumbnail (JPEGInterchangeFormat and its length),
	// strips and tiles. Each pair by its first entries; the bytes they name inside the data.
	static readonly (int Offsets, int Lengths)[] DataPairs = [(0x0201, 0x0202), (0x0111, 0x0117), (0x0144, 0x0145)];

	void AppendData (StringBuilder text, Table t)
	{
		foreach (var (offsetsTag, lengthsTag) in DataPairs) {
			var offsets = Integers (t, offsetsTag);
			var lengths = Integers (t, lengthsTag);
			for (var k = 0; k < offsets.Count && k < lengths.Count; k++) {
				var (from, to) = (offsets[k], offsets[k] + lengths[k]);
				text.Append (CultureInfo.InvariantCulture, $" data {offsetsTag:X4}=").Append (to <= _t.Length && from < to ? Convert.ToHexString (_t, (int)from, (int)(to - from)) : "OOR");
			}
		}
	}

	// The SHORT or LONG values of the first entry with the tag.
	List<long> Integers (Table t, int tag)
	{
		for (var n = 0; n < t.Count; n++) {
			var e = t.At + 2 + (12 * n);
			if (U16 (e) != tag)
				continue;
			var size = U16 (e + 2) switch { 3 => 2, 4 => 4, _ => 0 };
			var count = U32 (e + 4);
			var at = count * size <= 4 ? e + 8 : U32 (e + 8);
			if (size == 0 || at + (count * size) > _t.Length)
				return [];
			return [.. Enumerable.Range (0, (int)count).Select (k => size == 2 ? U16 (at + (2 * k)) : U32 (at + (4 * k)))];
		}

		return [];
	}

	/// <summary>
	/// The field tags shown, counted, as one line of the golden files: "E" for a table read with the Exif tags, "G"
	/// for a GPS table (its 0x0001 GPSLatitudeRef), then the tag in hex and the count.
	/// </summary>
	public string Summary ()
	{
		var counts = new SortedDictionary<string, int> (StringComparer.Ordinal);
		foreach (var (table, e) in Shown ()) {
			var tag = U16 (e);
			var key = table.ReadsExifTags && Array.IndexOf (FieldTags, tag) >= 0 ? "E" + tag.ToString ("X4", CultureInfo.InvariantCulture)
				: table.Kinds.Contains (Kind.Gps) && tag == 0x0001 ? "G0001" : null;
			if (key is not null && ValueOf (table, e) is not null)
				counts[key] = counts.GetValueOrDefault (key) + 1;
		}

		return counts.Count == 0 ? "-" : string.Join (" ", counts.Select (c => $"{c.Key}={c.Value}"));
	}
}
