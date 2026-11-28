// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests.Kit;

/// <summary>
/// Memory and work bounds for parse and save paths, measured without a clock.
/// </summary>
/// <remarks>
/// Allocation counts are deterministic on a loaded machine where wall-clock time is not.
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> sees only the calling thread, so measure
/// synchronous code; an async path that hops threads under-reports.
/// </remarks>
public static class Bounds
{
	/// <summary>
	/// Returns the bytes allocated on the calling thread while <paramref name="action"/> runs.
	/// </summary>
	public static long AllocatedBytes (Action action)
	{
		ArgumentNullException.ThrowIfNull (action);
		var before = GC.GetAllocatedBytesForCurrentThread ();
		action ();
		return GC.GetAllocatedBytesForCurrentThread () - before;
	}

	/// <summary>
	/// Fails unless <paramref name="action"/> allocates at most <paramref name="limit"/> bytes.
	/// </summary>
	/// <returns>The bytes allocated.</returns>
	public static long AssertAllocatesAtMost (long limit, Action action, string? what = null)
	{
		var allocated = AllocatedBytes (action);
		if (allocated > limit)
			Assert.Fail ($"{what ?? "action"} allocated {allocated} bytes; the bound is {limit}");
		return allocated;
	}

	/// <summary>
	/// Fails when the allocation of <paramref name="run"/> grows faster than its input.
	/// </summary>
	/// <remarks>
	/// Measures the inputs built for <c>n</c>, <c>2n</c> and <c>4n</c> and compares the increments, so a
	/// fixed cost cannot hide a quadratic one: linear work adds twice as much from 2n to 4n as from n to 2n,
	/// quadratic work adds four times as much. The bound is <c>2 × slack</c>. An increment under the noise
	/// floor (1 KB, or 1% of the n allocation) counts as no growth, so a flat series passes and a flat series
	/// that then jumps still fails (<see cref="AssertGrowsLinearly"/>). A parser that copies each nested
	/// payload, or rescans a list per item, fails here. Building the input is not measured. Each size is warmed
	/// once, then the action alone is measured, and the least of the runs is kept
	/// (<see cref="LeastAllocated"/>).
	/// </remarks>
	/// <param name="runs">Runs measured per size; the least is kept. Above 1 it drops a one-off extra allocation.</param>
	public static void AssertLinearAllocation (Func<int, byte[]> build, Action<byte[]> run, int n, double slack = 1.5, int runs = 1)
	{
		ArgumentNullException.ThrowIfNull (build);
		ArgumentNullException.ThrowIfNull (run);
		var builtOne = build (n);
		var builtTwo = build (n * 2);
		var builtFour = build (n * 4);
		var one = LeastAllocated (() => run (builtOne), runs);
		var two = LeastAllocated (() => run (builtTwo), runs);
		var four = LeastAllocated (() => run (builtFour), runs);
		AssertGrowsLinearly (one, two, four, slack);
	}

	/// <summary>
	/// Fails when the allocations measured for inputs of n, 2n and 4n grow faster than the input.
	/// </summary>
	/// <remarks>
	/// An increment under the noise floor — 1 KB, or 1% of the n allocation, whichever is larger — counts as no
	/// growth, so a flat or noisy-flat series passes. A series that is flat and then jumps still fails. When the
	/// first increment is below the floor, the baseline is the floor, so a linear step just under it passes. A step
	/// above the absolute 1 KB floor whose successor reaches <c>2 × slack</c> still fails when both steps sit under
	/// 1% of a large n: zeroing both would hide that superlinear series. Linear work adds twice as much from 2n to
	/// 4n as from n to 2n; the bound is <c>2 × slack</c>.
	/// </remarks>
	public static void AssertGrowsLinearly (long one, long two, long four, double slack = 1.5)
	{
		var noise = Math.Max (1024L, one / 100);
		var first = two - one;
		var second = four - two;
		var bound = 2 * slack;
		// Both steps are inside the floor. Compare them to each other when the first step is above the absolute
		// 1 KB floor: a 3x jump under 1% of a large baseline is still superlinear, and the strict bound would miss
		// a ratio that lands on 2 × slack exactly.
		if (second <= noise && Math.Abs (first) <= noise) {
			if (first > 1024 && second >= first * bound)
				Assert.Fail ($"allocation grew {second / (double)first:F1}x as fast from 2n to 4n as from n to 2n ({one} -> {two} -> {four} bytes); linear is 2x, the bound {bound:F1}x");
			return;
		}

		var baseline = Math.Max (first, noise);
		if (second > baseline * bound)
			Assert.Fail ($"allocation grew {second / (double)baseline:F1}x as fast from 2n to 4n as from n to 2n ({one} -> {two} -> {four} bytes); linear is 2x, the bound {bound:F1}x");
	}

	/// <summary>
	/// Bytes allocated by <paramref name="action"/> alone: one warm-up, then the least of <paramref name="runs"/>
	/// measured calls.
	/// </summary>
	/// <remarks>
	/// The warm-up pays for JIT and static caches. Before each measured call, one array is returned to every
	/// shared <see cref="System.Buffers.ArrayPool{T}"/> bucket. A rewriter rents a buffer that grows with the
	/// input and returns it; the next call's smaller rent takes that larger buffer when its own bucket is empty,
	/// and the following rent allocates a new one. Measured on the compacted IFD chain, that miss is one bucket:
	/// 64 KB, 128 KB and 256 KB at 2,000, 4,000 and 8,000 tables. Counted only at 8,000, the second increment is
	/// 3.0x the first, on the slack bound. With every rent a hit the same edit is 2.6x. A test that compares
	/// sizes is marked not to run beside other tests, because another test's rent during the sample can still
	/// take the buffer just returned.
	/// </remarks>
	public static long LeastAllocated (Action action, int runs = 3)
	{
		ArgumentNullException.ThrowIfNull (action);
		action ();
		var least = long.MaxValue;
		for (var r = 0; r < Math.Max (runs, 1); r++) {
			ReturnOneBufferToEachPoolBucket ();
			least = Math.Min (least, AllocatedBytes (action));
		}

		return least;
	}

	// Rent from the largest bucket down while holding the arrays, so a small rent cannot take a larger bucket's
	// only buffer, then return them smallest first so each bucket has one.
	static void ReturnOneBufferToEachPoolBucket ()
	{
		var held = new byte[17][];
		var count = 0;
		for (var size = 1024 * 1024; size >= 16; size >>= 1)
			held[count++] = System.Buffers.ArrayPool<byte>.Shared.Rent (size);
		for (var i = count - 1; i >= 0; i--)
			System.Buffers.ArrayPool<byte>.Shared.Return (held[i]);
	}
}

/// <summary>
/// A read-only stream that counts what a reader asks of it.
/// </summary>
/// <remarks>
/// The operation counter for stream paths: pin that a seekable read touches only the ranges a format
/// needs (<see cref="BytesRead"/> well under the file length), or that detection reads a bounded prefix.
/// </remarks>
public sealed class CountingStream (Stream inner) : Stream
{
	/// <summary>Total bytes returned by every read.</summary>
	public long BytesRead { get; private set; }

	/// <summary>Number of read calls, sync and async.</summary>
	public int ReadCalls { get; private set; }

	/// <summary>Number of <see cref="Seek"/> calls and <see cref="Position"/> sets.</summary>
	public int Seeks { get; private set; }

	/// <inheritdoc/>
	public override bool CanRead => inner.CanRead;

	/// <inheritdoc/>
	public override bool CanSeek => inner.CanSeek;

	/// <inheritdoc/>
	public override bool CanWrite => false;

	/// <inheritdoc/>
	public override long Length => inner.Length;

	/// <inheritdoc/>
	public override long Position {
		get => inner.Position;
		set {
			Seeks++;
			inner.Position = value;
		}
	}

	/// <inheritdoc/>
	public override int Read (byte[] buffer, int offset, int count) => Count (inner.Read (buffer, offset, count));

	/// <inheritdoc/>
	public override int Read (Span<byte> buffer) => Count (inner.Read (buffer));

	/// <inheritdoc/>
	public override async Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		Count (await inner.ReadAsync (buffer.AsMemory (offset, count), cancellationToken).ConfigureAwait (false));

	/// <inheritdoc/>
	public override async ValueTask<int> ReadAsync (Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		Count (await inner.ReadAsync (buffer, cancellationToken).ConfigureAwait (false));

	/// <inheritdoc/>
	public override long Seek (long offset, SeekOrigin origin)
	{
		Seeks++;
		return inner.Seek (offset, origin);
	}

	/// <inheritdoc/>
	public override void Flush () { }

	/// <inheritdoc/>
	public override void SetLength (long value) => throw new NotSupportedException ();

	/// <inheritdoc/>
	public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

	/// <inheritdoc/>
	protected override void Dispose (bool disposing)
	{
		if (disposing)
			inner.Dispose ();
		base.Dispose (disposing);
	}

	int Count (int read)
	{
		ReadCalls++;
		BytesRead += read;
		return read;
	}
}
