// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Image.Tiff;

/// <summary>
/// The width and height of a TIFF or DNG, as exiftool's ImageSize gives them (measured): tags 256 and 257 (SHORT,
/// SSHORT, LONG or SLONG, LONG8 in BigTIFF, one value each, 1 to 2^31 - 1) of the first IFD whose NewSubfileType (254) is 0, the
/// full-resolution image, in the order exiftool reads them: IFD0, its SubIFDs (tag 330) depth first in entry order
/// with each one's next IFDs, then IFD0's next IFD. Without one, IFD0's. DNG is TIFF with tag 50706 in IFD0.
/// Classic TIFF and BigTIFF.
/// </summary>
/// <remarks>
/// Tables, entries, next offsets and SubIFD offsets are read by <see cref="TiffReader"/>, as every IFD walk reads
/// them. The walk is iterative (a file-controlled chain must not recurse), at most 64 SubIFD levels deep and 4,096
/// IFDs in all, each IFD read once, and it stops when the entries it has read pass
/// <see cref="TiffReader.EntryBound"/> (past it the tables share their entries). Detection reads IFD0 only. Unlike
/// exiftool, a width and a height are taken from one IFD, never one from each.
/// </remarks>
static class TiffSize
{
	const int DngVersionTag = 0xC612;
	const int NewSubFileTypeTag = 254;
	const int MaxTiffIfds = 4096;
	const int MaxTiffDepth = 64;
	const int SmallIfdCount = 16;
	const int MaxSubIfds = 32;

	/// <summary>IFD tables read on this thread, for tests that bound the walk by an operation count.</summary>
	[ThreadStatic]
	internal static int IfdReads;

	/// <summary>IFD entries read on this thread, for tests that bound the walk by an operation count.</summary>
	[ThreadStatic]
	internal static long EntryReads;

	public static bool HasDngVersion (ReadOnlySpan<byte> data) =>
		TiffReader.TryOpen (data, out var tiff, out var first) && tiff.TryTable (first, out var count) &&
		ReadIfd (tiff, first, count, [], out _).SawDng;

	/// <param name="data">The TIFF.</param>
	/// <param name="dng">Report the format as DNG even without the DNG version tag (a file named .dng).</param>
	/// <param name="properties">The size, and the format: DNG or TIFF.</param>
	public static bool TryRead (ReadOnlySpan<byte> data, bool dng, out ImageProperties? properties)
	{
		properties = null;
		if (!TiffReader.TryOpen (data, out var tiff, out var first))
			return false;
		var (root, full) = Walk (tiff, first);
		var image = full ?? root;
		if (!image.HasSize)
			return false;
		properties = new ImageProperties (image.Width, image.Height, 0, dng || root.SawDng ? "DNG" : "TIFF");
		return true;
	}

	// IFD0, and the first full-resolution IFD in exiftool's order (IFD0 itself when it is one) or null.
	static (IfdInfo Root, IfdInfo? Full) Walk (TiffReader tiff, long first)
	{
		Span<(long, int)> pending = stackalloc (long, int)[SmallIfdCount];
		Span<long> visitedSmall = stackalloc long[SmallIfdCount];
		Span<long> subs = stackalloc long[MaxSubIfds];
		var stack = new IfdStack (pending);
		Dictionary<long, byte>? visited = null;
		IfdInfo root = default;
		var entries = tiff.EntryBound;
		stack.Push (first, 0);
		var seen = 0;
		while (stack.Count > 0 && seen < MaxTiffIfds) {
			var (offset, depth) = stack.Pop ();
			if (!tiff.TryTable (offset, out var count) || !Visit (visitedSmall, ref visited, seen, offset, tiff.Length))
				continue;
			entries -= count;
			if (entries < 0)
				break;
			var info = ReadIfd (tiff, offset, count, subs, out var subCount);
			if (seen++ == 0)
				root = info;
			if (info.FullResolution)
				return (root, info);
			if (depth >= MaxTiffDepth)
				continue;
			stack.Push (info.Next, depth);
			for (var s = subCount - 1; s >= 0; s--)
				stack.Push (subs[s], depth + 1);
		}

		return (root, null);
	}

	// The work stack of offsets and their SubIFD depths: on the stack for a handful of IFDs, then on the heap,
	// doubling. Every read IFD pushes at most 33 offsets, so it grows with the IFDs read.
	ref struct IfdStack (Span<(long Offset, int Depth)> items)
	{
		Span<(long Offset, int Depth)> _items = items;

		public int Count { get; private set; }

		public void Push (long offset, int depth)
		{
			if (Count == _items.Length) {
				var grown = new (long, int)[2 * Count];
				_items.CopyTo (grown);
				_items = grown;
			}

			_items[Count++] = (offset, depth);
		}

		public (long Offset, int Depth) Pop () => _items[--Count];
	}

	// True the first time a table's offset is seen. The first SmallIfdCount offsets are kept in a stack span and
	// scanned; past that in a set sized by the IFDs the data can hold (the smallest is a 2-byte count and a 4-byte
	// next offset), at most the IFD cap.
	static bool Visit (Span<long> small, ref Dictionary<long, byte>? set, int seen, long offset, int length)
	{
		if (set is null) {
			for (var n = 0; n < seen; n++) {
				if (small[n] == offset)
					return false;
			}

			if (seen < small.Length) {
				small[seen] = offset;
				return true;
			}

			set = new Dictionary<long, byte> (Math.Min (MaxTiffIfds, length / 6));
			foreach (var known in small)
				set[known] = 0;
		}

		if (set.ContainsKey (offset))
			return false;
		set[offset] = 0;
		return true;
	}

	readonly record struct IfdInfo (int Width, int Height, bool FullResolutionType, bool SawDng, long Next)
	{
		public bool HasSize => Width > 0 && Height > 0;
		public bool FullResolution => FullResolutionType && HasSize;
	}

	// What one readable IFD of count entries says. Its SubIFD offsets go to subs, as many as fit.
	static IfdInfo ReadIfd (TiffReader tiff, long offset, int count, Span<long> subs, out int subCount)
	{
		subCount = 0;
		IfdReads++;
		int width = 0, height = 0;
		bool full = false, sawDng = false;
		for (var n = 0; n < count; n++) {
			EntryReads++;
			var entry = tiff.Entry (offset, n);
			switch (tiff.Tag (entry)) {
				case DngVersionTag:
					sawDng = true;
					break;
				case 256 when TryScalar (tiff, entry, out var value) && value <= int.MaxValue:
					width = (int)value;
					break;
				case 257 when TryScalar (tiff, entry, out var value) && value <= int.MaxValue:
					height = (int)value;
					break;
				case NewSubFileTypeTag when TryScalar (tiff, entry, out var value):
					full = value == 0;
					break;
				case TiffReader.SubIfdsPointer when tiff.TryPointers (entry, out var values):
					for (var k = 0L; k < values.Count && subCount < subs.Length; k++)
						subs[subCount++] = tiff.PointerAt (values, k);
					break;
			}
		}

		return new IfdInfo (width, height, full, sawDng, tiff.Next (offset, count));
	}

	// One SHORT, SSHORT, LONG or SLONG value (or LONG8 in BigTIFF) in the entry, as exiftool reads them.
	static bool TryScalar (TiffReader tiff, long entry, out long value)
	{
		var at = (int)tiff.ValueField (entry);
		value = 0;
		if (tiff.Count (entry) != 1)
			return false;
		switch (tiff.Type (entry)) {
			case 3:
				value = tiff.U16 (at);
				return true;
			case 4:
				value = tiff.U32 (at);
				return true;
			case 8:
				value = (short)tiff.U16 (at);
				return true;
			case 9:
				value = (int)tiff.U32 (at);
				return true;
			case 16 when tiff.Big:
				return Endian.TryU64 (tiff.Data, at, tiff.LittleEndian, out value);
			default:
				return false;
		}
	}
}
