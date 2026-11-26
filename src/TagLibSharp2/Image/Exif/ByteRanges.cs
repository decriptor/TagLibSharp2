// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>Sorted, merged byte ranges and the overlap test the Exif removal uses on them.</summary>
static class ByteRanges
{
	public static CountedList<(long Start, long End)> Merge (CountedList<(long Start, long End)> ranges)
	{
		ranges.Sort ((a, b) => a.Start.CompareTo (b.Start));
		var merged = new CountedList<(long Start, long End)> ();
		for (var n = 0; n < ranges.Count; n++) {
			var range = ranges[n];
			if (range.End <= range.Start)
				continue;
			if (merged.Count > 0 && range.Start <= merged[merged.Count - 1].End)
				merged[merged.Count - 1] = (merged[merged.Count - 1].Start, Math.Max (merged[merged.Count - 1].End, range.End));
			else
				merged.Add (range);
		}

		return merged;
	}

	// The merged ranges are disjoint and sorted, so their ends are sorted too: find the first that ends after
	// the range starts, and check that it starts before the range ends.
	public static bool Overlaps (CountedList<(long Start, long End)> merged, (long Start, long End) range)
	{
		int low = 0, high = merged.Count;
		while (low < high) {
			IfdSteps.Count++;
			var mid = low + ((high - low) / 2);
			if (merged[mid].End <= range.Start)
				low = mid + 1;
			else
				high = mid;
		}

		return low < merged.Count && merged[low].Start < range.End;
	}
}
