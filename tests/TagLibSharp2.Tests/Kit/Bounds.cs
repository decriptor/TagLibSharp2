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
	/// payload, or rescans a list per item, fails here. Building the input is not measured, and each size runs
	/// once before it is measured so one-time JIT and static allocations do not count.
	/// </remarks>
	/// <param name="runs">Runs measured per size; the least is kept. Above 1 it filters allocations another
	/// thread causes, such as a shared <see cref="System.Buffers.ArrayPool{T}"/> that another test emptied.</param>
	public static void AssertLinearAllocation (Func<int, byte[]> build, Action<byte[]> run, int n, double slack = 1.5, int runs = 1)
	{
		ArgumentNullException.ThrowIfNull (build);
		ArgumentNullException.ThrowIfNull (run);
		var one = Measure (build (n), run, runs);
		var two = Measure (build (n * 2), run, runs);
		var four = Measure (build (n * 4), run, runs);
		AssertGrowsLinearly (one, two, four, slack);
	}

	/// <summary>
	/// Fails when the allocations measured for inputs of n, 2n and 4n grow faster than the input.
	/// </summary>
	/// <remarks>
	/// An increment under the noise floor — 1 KB, or 1% of the n allocation, whichever is larger — counts as no
	/// growth, so a flat or noisy-flat series passes. A series that is flat and then jumps still fails: the baseline
	/// of a zero first increment is 1. Linear work adds twice as much from 2n to 4n as from n to 2n; the bound is
	/// <c>2 × slack</c>.
	/// </remarks>
	public static void AssertGrowsLinearly (long one, long two, long four, double slack = 1.5)
	{
		var noise = Math.Max (1024L, one / 100);
		var first = two - one;
		var second = four - two;
		if (Math.Abs (first) <= noise)
			first = 0;
		if (second <= noise)
			second = 0;
		if (first == 0 && second == 0)
			return;
		var baseline = Math.Max (first, 1);
		if (second > baseline * 2 * slack)
			Assert.Fail ($"allocation grew {second / (double)baseline:F1}x as fast from 2n to 4n as from n to 2n ({one} -> {two} -> {four} bytes); linear is 2x, the bound {2 * slack:F1}x");
	}

	static long Measure (byte[] input, Action<byte[]> run, int runs)
	{
		run (input);
		var least = long.MaxValue;
		for (var r = 0; r < Math.Max (runs, 1); r++)
			least = Math.Min (least, AllocatedBytes (() => run (input)));
		return least;
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
