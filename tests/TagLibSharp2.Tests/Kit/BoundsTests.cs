// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests.Kit;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Kit")]
public class BoundsTests
{
	[TestMethod]
	public void AllocatedBytes_CountsWhatTheActionAllocates ()
	{
		byte[]? kept = null;

		var allocated = Bounds.AllocatedBytes (() => kept = new byte[100_000]);

		Assert.IsNotNull (kept);
		Assert.IsTrue (allocated >= 100_000, $"allocated {allocated}");
		Assert.IsTrue (allocated < 200_000, $"allocated {allocated}");
	}

	[TestMethod]
	public void AllocatedBytes_ActionThatAllocatesNothing_IsNearZero ()
	{
		var total = 0;

		var allocated = Bounds.AllocatedBytes (() => total += 1);

		Assert.AreEqual (1, total);
		Assert.IsTrue (allocated < 1_000, $"allocated {allocated}");
	}

	[TestMethod]
	public void AssertAllocatesAtMost_UnderTheLimit_Passes ()
	{
		var allocated = Bounds.AssertAllocatesAtMost (10_000, () => GC.KeepAlive (new byte[1_000]));

		Assert.IsTrue (allocated >= 1_000);
	}

	[TestMethod]
	public void AssertAllocatesAtMost_OverTheLimit_FailsWithTheCount ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertAllocatesAtMost (1_000, () => GC.KeepAlive (new byte[50_000]), "parse"));

		Assert.Contains ("parse", error.Message);
		Assert.Contains ("1000", error.Message);
	}

	[TestMethod]
	public void AssertLinearAllocation_LinearWork_Passes ()
	{
		Bounds.AssertLinearAllocation (n => new byte[n], data => _ = (byte[])data.Clone (), 10_000);
	}

	[TestMethod]
	public void AssertLinearAllocation_QuadraticWork_Fails ()
	{
		// One copy per remaining suffix: the shape of a parser that copies each nested payload.
		static void Quadratic (byte[] data)
		{
			for (var i = 0; i < data.Length; i += 16)
				_ = data.AsSpan (i).ToArray ();
		}

		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertLinearAllocation (n => new byte[n], Quadratic, 4_096));

		Assert.Contains ("grew", error.Message);
	}

	[TestMethod]
	public void AssertLinearAllocation_QuadraticWorkBehindAFixedCost_Fails ()
	{
		// A 64 KB fixed cost plus about n squared / 32: a single n-to-4n ratio stays under 6x and passes.
		static void FixedThenQuadratic (byte[] data)
		{
			GC.KeepAlive (new byte[64 * 1024]);
			for (var i = 0; i < data.Length; i += 32)
				_ = data.AsSpan (i).ToArray ();
		}

		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertLinearAllocation (n => new byte[n], FixedThenQuadratic, 1_000));

		Assert.Contains ("grew", error.Message);
	}

	[TestMethod]
	public void AssertLinearAllocation_OneTimeSetup_IsNotCounted ()
	{
		// The first call allocates a 1 MB cache, as JIT or static initialisation would. Without the
		// warm-up run the n measurement holds it, the n-to-2n increment goes negative, and this fails.
		byte[]? cache = null;
		Bounds.AssertLinearAllocation (n => new byte[n], data => {
			cache ??= new byte[1024 * 1024];
			GC.KeepAlive ((byte[])data.Clone ());
		}, 10_000);

		Assert.IsNotNull (cache);
	}

	[TestMethod]
	public void AssertLinearAllocation_QuadraticWork_FailsWithSeveralRuns ()
	{
		// Keeping the least of three runs does not hide work that is quadratic on every run.
		static void Quadratic (byte[] data)
		{
			for (var i = 0; i < data.Length; i += 16)
				_ = data.AsSpan (i).ToArray ();
		}

		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertLinearAllocation (n => new byte[n], Quadratic, 4_096, runs: 3));

		Assert.Contains ("grew", error.Message);
	}

	// Linear work whose first measured run at 4n also allocates 4 MB, as when another thread emptied a shared
	// pool: one run sees the noise and fails, the least of three does not.
	static Action<byte[]> LinearWithOneNoisyRun (int n)
	{
		var calls = new Dictionary<int, int> ();
		return data => {
			calls[data.Length] = calls.GetValueOrDefault (data.Length) + 1;
			if (data.Length == 4 * n && calls[data.Length] == 2)
				GC.KeepAlive (new byte[4 * 1024 * 1024]);
			GC.KeepAlive ((byte[])data.Clone ());
		};
	}

	[TestMethod]
	public void AssertLinearAllocation_OneNoisyRun_FailsWithOneRun_AndPassesWithSeveral ()
	{
		Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertLinearAllocation (n => new byte[n], LinearWithOneNoisyRun (10_000), 10_000));

		Bounds.AssertLinearAllocation (n => new byte[n], LinearWithOneNoisyRun (10_000), 10_000, runs: 3);
	}

	[TestMethod]
	public void AssertLinearAllocation_LinearWorkBehindAFixedCost_Passes ()
	{
		Bounds.AssertLinearAllocation (n => new byte[n], data => {
			GC.KeepAlive (new byte[256 * 1024]);
			_ = (byte[])data.Clone ();
		}, 10_000);
	}

	// Ubuntu CI measured these on EveryIfdWalk_IsLinearInTheEntries: the n-to-2n step is zero or negative, and
	// treating that step as 1 made an 80-byte bump at 4n look like 80x growth.
	[TestMethod]
	public void AssertGrowsLinearly_FlatSeries_Passes ()
	{
		Bounds.AssertGrowsLinearly (24936, 24856, 24936);
		Bounds.AssertGrowsLinearly (24968, 24968, 25048);
		Bounds.AssertGrowsLinearly (1584, 1584, 1584);
	}

	[TestMethod]
	public void AssertGrowsLinearly_SuperlinearSeries_Fails ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertGrowsLinearly (1_000_000, 2_000_000, 8_000_000));

		Assert.Contains ("grew", error.Message);
	}

	// A first increment inside the noise floor must not excuse a jump at 4n.
	[TestMethod]
	public void AssertGrowsLinearly_FlatThenASpike_Fails ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertGrowsLinearly (24936, 24856, 1_000_000));

		Assert.Contains ("grew", error.Message);
	}

	// The 1% floor must not hide a doubling that is still above the absolute 1 KB floor, and it must not
	// turn a genuinely linear step that sits under that floor into a failure against a baseline of 1.
	[TestMethod]
	[DataRow (10_000L, 11_000L, 13_000L, false)]
	[DataRow (1_000_000L, 1_008_000L, 1_024_000L, false)]
	[DataRow (10_000_000L, 10_030_000L, 10_090_000L, false)]
	[DataRow (10_000_000L, 10_030_000L, 10_120_000L, true)]
	public void AssertGrowsLinearly_NoiseFloorEdges_MatchTheSeries (long one, long two, long four, bool superlinear)
	{
		if (!superlinear) {
			Bounds.AssertGrowsLinearly (one, two, four);
			return;
		}

		var error = Assert.ThrowsExactly<AssertFailedException> (() => Bounds.AssertGrowsLinearly (one, two, four));
		Assert.Contains ("grew", error.Message);
	}

	[TestMethod]
	public void CountingStream_CountsReadsSeeksAndBytes ()
	{
		using var stream = new CountingStream (new MemoryStream (new byte[1_000]));
		var buffer = new byte[100];

		_ = stream.Read (buffer, 0, buffer.Length);
		stream.Seek (500, SeekOrigin.Begin);
		_ = stream.Read (buffer.AsSpan (0, 50));
		stream.Position = 990;
		_ = stream.Read (buffer, 0, buffer.Length);

		Assert.AreEqual (160, stream.BytesRead);
		Assert.AreEqual (3, stream.ReadCalls);
		Assert.AreEqual (2, stream.Seeks);
		Assert.AreEqual (1_000, stream.Length);
	}

	[TestMethod]
	public async Task CountingStream_CountsAsyncReads ()
	{
		using var stream = new CountingStream (new MemoryStream (new byte[64]));
		var buffer = new byte[64];

		_ = await stream.ReadAsync (buffer.AsMemory (0, 10));
#pragma warning disable CA1835 // The array overload is the one under test.
		_ = await stream.ReadAsync (buffer, 0, 20);
#pragma warning restore CA1835

		Assert.AreEqual (30, stream.BytesRead);
		Assert.AreEqual (2, stream.ReadCalls);
	}

	[TestMethod]
	public void CountingStream_DoesNotWrite ()
	{
		using var stream = new CountingStream (new MemoryStream (new byte[8]));

		Assert.IsFalse (stream.CanWrite);
		Assert.ThrowsExactly<NotSupportedException> (() => stream.Write ([1], 0, 1));
	}
}
