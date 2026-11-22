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
		var allocated = Bounds.AssertAllocatesAtMost (10_000, () => _ = new byte[1_000]);

		Assert.IsTrue (allocated >= 1_000);
	}

	[TestMethod]
	public void AssertAllocatesAtMost_OverTheLimit_FailsWithTheCount ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertAllocatesAtMost (1_000, () => _ = new byte[50_000], "parse"));

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
			_ = new byte[64 * 1024];
			for (var i = 0; i < data.Length; i += 32)
				_ = data.AsSpan (i).ToArray ();
		}

		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Bounds.AssertLinearAllocation (n => new byte[n], FixedThenQuadratic, 1_000));

		Assert.Contains ("grew", error.Message);
	}

	[TestMethod]
	public void AssertLinearAllocation_LinearWorkBehindAFixedCost_Passes ()
	{
		Bounds.AssertLinearAllocation (n => new byte[n], data => {
			_ = new byte[256 * 1024];
			_ = (byte[])data.Clone ();
		}, 10_000);
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
