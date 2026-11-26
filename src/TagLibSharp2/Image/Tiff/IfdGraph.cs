// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Tiff;

/// <summary>
/// The IFD tables exiftool reads, as measured: IFD0 and the chain after it through next-IFD offsets (IFD1, IFD2,
/// ...); from each of those, and from every Exif, SubIFD and interoperability table they lead to, the Exif IFDs
/// (0x8769), SubIFDs (0x014A) and interoperability IFDs (0xA005) they point at, all read with the Exif::Main tags,
/// so all of them may hold a copy (<see cref="MainRole"/>, <see cref="ExifRole"/>). Pointers are decoded as exiftool
/// decodes them (<see cref="TiffReader.TryPointers"/>), and IFD0's Exif and GPS pointers also as TagLib# reads them
/// (<see cref="TiffReader.RawPointer"/>). GPS tables (0x8825) are read with the GPS tags and follow no pointer
/// (<see cref="GpsRole"/>): kept and protected. The next IFD of an Exif, SubIFD, interoperability or GPS table,
/// which no reader follows, is protected from zeroing only (<see cref="ProtectRole"/>).
/// </summary>
/// <remarks>
/// Each table is walked once by offset; a pointer whose table's entries do not fit in the data is listed as
/// unread. The entries walked are bounded by the data (tables that do not overlap hold at most one per 12 bytes),
/// and so are the pointer offsets read (one per 4 bytes, each array once): past either bound the tables overlap,
/// and the walk stops with <see cref="OverBudget"/> set. Every table, entry and offset visit is counted in
/// <see cref="IfdSteps"/>.
/// </remarks>
sealed class IfdGraph
{
	public const int MainRole = 1;
	public const int ExifRole = 2;
	public const int GpsRole = 4;
	public const int ProtectRole = 8;

	/// <summary>The roles whose tables are read with the Exif tags, and whose pointers are followed.</summary>
	public const int ReadRoles = MainRole | ExifRole;

	/// <summary>The roles of tables some reader reads: with the Exif tags, or GPS tables with the GPS tags.</summary>
	public const int SeenRoles = ReadRoles | GpsRole;

	public CountedList<(long At, int Count, int Roles)> Tables { get; } = new ();

	/// <summary>Offsets a pointer names, inside the data and past the header, where no table can be read.</summary>
	public CountedList<long> Unread { get; } = new ();

	public bool OverBudget { get; set; }

	public static IfdGraph Walk (TiffReader tiff, long ifd0)
	{
		var walk = new WalkState (tiff.EntryBound, ifd0);
		walk.Queue.Enqueue ((ifd0, MainRole));
		while (walk.Queue.Count > 0 && !walk.Graph.OverBudget) {
			IfdSteps.Count++;
			var (at, role) = walk.Queue.Dequeue ();
			Visit (tiff, walk, at, role);
		}

		return walk.Graph;
	}

	sealed class WalkState (long entryBound, long ifd0)
	{
		public IfdGraph Graph { get; } = new ();
		public Queue<(long At, int Role)> Queue { get; } = new ();
		public Dictionary<long, int> Index { get; } = new ();
		public HashSet<long> UnreadSeen { get; } = new ();
		public HashSet<PointerValues> ArraysSeen { get; } = new ();
		public long EntryBound { get; } = entryBound;
		public long Ifd0 { get; } = ifd0;
		public long Offsets { get; set; }
		long _entries;

		// The table's index in the graph, added the first time; past the entry bound the walk is over budget, and
		// its loop stops after this table.
		public int IndexOf (long at, int count)
		{
			if (Index.TryGetValue (at, out var index))
				return index;
			index = Graph.Tables.Count;
			Index[at] = index;
			Graph.Tables.Add ((at, count, 0));
			_entries += count;
			Graph.OverBudget |= _entries > EntryBound;
			return index;
		}
	}

	static void Visit (TiffReader tiff, WalkState walk, long at, int role)
	{
		if (!tiff.TryTable (at, out var count)) {
			if (at >= TiffReader.HeaderSize && at < tiff.Length && walk.UnreadSeen.Add (at))
				walk.Graph.Unread.Add (at);
			return;
		}

		var index = walk.IndexOf (at, count);
		var had = walk.Graph.Tables[index].Roles;
		if ((had & role) != 0)
			return;
		walk.Graph.Tables[index] = (at, count, had | role);
		if ((role & ReadRoles) != 0 && (had & ReadRoles) == 0)
			FollowPointers (tiff, walk, at, count);

		// exiftool follows IFD0's chain of next IFDs. No reader reads the next IFD of an Exif, SubIFD,
		// interoperability or GPS table (measured): it is protected from zeroing only.
		if ((role & MainRole) != 0)
			walk.Queue.Enqueue ((tiff.Next (at, count), MainRole));
		else if ((role & (ExifRole | GpsRole)) != 0 && (had & SeenRoles) == 0)
			walk.Queue.Enqueue ((tiff.Next (at, count), ProtectRole));
	}

	// Past the bound on pointer offsets each further pointer only adds to the count; the walk then stops.
	static void FollowPointers (TiffReader tiff, WalkState walk, long at, int count)
	{
		for (var n = 0; n < count; n++) {
			IfdSteps.Count++;
			var entry = tiff.Entry (at, n);
			var role = tiff.Tag (entry) switch {
				TiffReader.ExifIfdPointer or TiffReader.InteropIfdPointer or TiffReader.SubIfdsPointer => ExifRole,
				TiffReader.GpsIfdPointer => GpsRole,
				_ => 0
			};
			if (role != 0 && tiff.TryPointers (entry, out var values))
				Follow (tiff, walk, values, role);
			// TagLib# reads IFD0's Exif and GPS pointers as the 4-byte field, whatever their type and count (measured).
			if (at == walk.Ifd0 && tiff.Tag (entry) is TiffReader.ExifIfdPointer or TiffReader.GpsIfdPointer)
				Follow (tiff, walk, TiffReader.RawPointer (entry), role);
		}
	}

	// Each pointer value once per array as one type reads it; the values read are bounded by one per 4 bytes of data,
	// and none is read past it.
	static void Follow (TiffReader tiff, WalkState walk, PointerValues values, int role)
	{
		if (!walk.ArraysSeen.Add (values))
			return;
		walk.Offsets += values.Count;
		walk.Graph.OverBudget |= walk.Offsets > tiff.Length / 4;
		for (var k = 0L; k < values.Count && !walk.Graph.OverBudget; k++) {
			IfdSteps.Count++;
			walk.Queue.Enqueue ((tiff.PointerAt (values, k), role));
		}
	}
}
