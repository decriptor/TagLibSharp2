// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests.Kit;

// The child-process runner is what turns a stack overflow into one failed test.
// These cases prove it on purpose-built children before any format relies on it.
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Kit")]
[DoNotParallelize]
public class KitChildTests
{
	static readonly TimeSpan Timeout = TimeSpan.FromMinutes (2);

	[TestMethod]
	public void Run_AChildThatFinishes_ReportsItsOutput ()
	{
		var result = KitChild.Run (Timeout, "kit-selftest", "ok");

		Assert.AreEqual (0, result.ExitCode, result.Describe ());
		Assert.IsFalse (result.TimedOut);
		Assert.Contains ("selftest ok", result.Output);
	}

	[TestMethod]
	public void Run_AChildThatOverflowsTheStack_IsAFailureAndTheHostSurvives ()
	{
		var result = KitChild.Run (Timeout, "kit-selftest", "overflow");

		Assert.AreNotEqual (0, result.ExitCode, result.Describe ());
		Assert.IsFalse (result.TimedOut);
		Assert.IsTrue (result.Error.Contains ("stack overflow", StringComparison.OrdinalIgnoreCase), result.Describe ());
	}

	[TestMethod]
	public void Run_ACaseRunsOnTheSmallStack ()
	{
		// 100,000 frames fit an 8 MiB main thread; on the 1 MiB stack the cases run on, they overflow.
		var result = KitChild.Run (Timeout, "kit-selftest", "stack");

		Assert.AreNotEqual (0, result.ExitCode, result.Describe ());
		Assert.IsTrue (result.Error.Contains ("stack overflow", StringComparison.OrdinalIgnoreCase), result.Describe ());
	}

	[TestMethod]
	public void Run_AChildThatThrows_IsAFailure ()
	{
		var result = KitChild.Run (Timeout, "kit-selftest", "throw");

		Assert.AreNotEqual (0, result.ExitCode, result.Describe ());
		Assert.Contains ("InvalidOperationException", result.Error);
	}

	[TestMethod]
	public void Run_AChildPastTheHeapLimit_IsAFailure ()
	{
		var result = KitChild.Run (Timeout, "kit-selftest", "allocate");

		Assert.AreNotEqual (0, result.ExitCode, result.Describe ());
		Assert.IsTrue (result.Error.Contains ("out of memory", StringComparison.OrdinalIgnoreCase), result.Describe ());
	}

	[TestMethod]
	public void Run_AChildThatDoesNotFinish_IsKilledAtTheTimeout ()
	{
		var result = KitChild.Run (TimeSpan.FromSeconds (3), "kit-selftest", "hang");

		Assert.IsTrue (result.TimedOut, result.Describe ());
		Assert.Contains ("timed out", result.Describe ());
	}

	[TestMethod]
	public void Run_AnUnknownCommand_ExitsWithUsage ()
	{
		var result = KitChild.Run (Timeout, "no-such-command");

		Assert.AreEqual (KitProgram.UsageExitCode, result.ExitCode, result.Describe ());
		Assert.Contains ("kit-depth", result.Error);
	}
}
