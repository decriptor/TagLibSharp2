// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Image.Tiff;

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// Whether a field edit may run: the Exif structure is plain, and the edit would remove an entry. One pass over the
/// tables already walked, before a <see cref="RemovalPlan"/> records a range or reads a value array.
/// </summary>
static class PlainExif
{
	/// <summary>A plain block, whether the edit removes an entry, and why it is not plain.</summary>
	public readonly record struct Verdict (bool Plain, bool Removes, string? Why);

	/// <summary>
	/// Strip, tile and thumbnail offset/length tags (TIFF 6.0 §3, tags 0x0111/0x0117, 0x0144/0x0145, and
	/// JPEGInterchangeFormat 0x0201/0x0202). A field edit reads only SHORT (3) and LONG (4) values of these.
	/// </summary>
	public static Verdict Judge (TiffReader tiff, IfdGraph graph, HashSet<int> tags)
	{
		string? why = null;
		var removes = false;
		HashSet<ValueArray>? named = null;
		for (var t = 0; t < graph.Tables.Count; t++) {
			var (at, count, roles) = graph.Tables[t];
			var edited = (roles & IfdGraph.ReadRoles) != 0;
			for (var n = 0; n < count; n++) {
				IfdSteps.Count++;
				var entry = tiff.Entry (at, n);
				var tag = tiff.Tag (entry);
				if (edited && tags.Contains (tag))
					removes = true;
				if (why is not null || !IsPairTag (tag) || tiff.Count (entry) == 0)
					continue;
				if (tiff.Type (entry) is not (3 or 4))
					why = "has a strip, tile or thumbnail offset or length that is not SHORT or LONG";
				else if (TryValueArray (tiff, entry, out var key)) {
					// One pass over entries. A second name of the same array is a shared or cross-paired pair.
					named ??= [];
					if (!named.Add (key))
						why = "names one strip, tile or thumbnail array more than once";
				}
			}

			if (why is not null && removes)
				break;
		}

		return new Verdict (why is null, removes, why);
	}

	static bool IsPairTag (int tag) => tag is 0x0111 or 0x0117 or 0x0144 or 0x0145 or 0x0201 or 0x0202;

	// A SHORT or LONG array that lies wholly in the data. Inline values are the entry's own value field.
	static bool TryValueArray (TiffReader tiff, long entry, out ValueArray key)
	{
		key = default;
		var size = tiff.Type (entry) == 3 ? 2 : 4;
		var items = tiff.Count (entry);
		if (items == 0)
			return false;
		var at = items * size <= 4 ? entry + 8 : tiff.Value (entry);
		if (at < 0 || at > tiff.Length || items > (tiff.Length - at) / size)
			return false;
		key = new ValueArray (at, items, size);
		return true;
	}
}
