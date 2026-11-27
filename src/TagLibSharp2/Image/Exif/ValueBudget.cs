// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Exif;

/// <summary>
/// Whether offset and length arrays name more values than half the bytes. Equality still fits: two arrays that
/// both lie in the data can meet it. The plainness check counts from the entry headers and the removal plan counts
/// the values it is about to read. Both ask here. The plan keeps its own stop, because a test may skip the
/// plainness check: that stop is what the edit matrix counts, and it is what keeps the plan from allocating past it.
/// </summary>
static class ValueBudget
{
	public static bool PastHalf (long already, long more, long length) => more > (length / 2) - already;
}
