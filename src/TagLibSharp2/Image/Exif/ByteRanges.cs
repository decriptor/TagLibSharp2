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

	/// <summary>The first range whose end is past <paramref name="start"/>, or <c>sorted.Count</c> when none is.</summary>
	public static int FirstEndingAfter (CountedList<(long Start, long End)> sorted, long start)
	{
		int low = 0, high = sorted.Count;
		while (low < high) {
			IfdSteps.Count++;
			var mid = low + ((high - low) / 2);
			if (sorted[mid].End <= start)
				low = mid + 1;
			else
				high = mid;
		}

		return low;
	}

	// The merged ranges are disjoint and sorted, so their ends are sorted too.
	public static bool Overlaps (CountedList<(long Start, long End)> merged, (long Start, long End) range)
	{
		var low = FirstEndingAfter (merged, range.Start);
		return low < merged.Count && merged[low].Start < range.End;
	}
}
