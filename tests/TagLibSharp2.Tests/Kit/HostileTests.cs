// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests.Kit;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Kit")]
public class HostileTests
{
	static readonly byte[] Sample = [0x10, 0x20, 0x30, 0x40, 0x50];
	static readonly int[] AllLengths = [0, 1, 2, 3, 4, 5];
	static readonly int[] SteppedLengths = [0, 2, 4, 5];
	static readonly string[] SkippedEqual = ["0101", "0000"];
	static readonly int[] MiddleOffsets = [1, 2];

	[TestMethod]
	public void EveryTruncation_PassesEveryLengthFromZeroToFull ()
	{
		var lengths = new List<int> ();

		var cases = Hostile.EveryTruncation (Sample, data => lengths.Add (data.Length));

		CollectionAssert.AreEqual (AllLengths, lengths);
		Assert.AreEqual (6, cases);
	}

	[TestMethod]
	public void EveryTruncation_PassesThePrefixBytes ()
	{
		var seen = new List<string> ();

		Hostile.EveryTruncation (Sample, data => seen.Add (Convert.ToHexString (data)));

		Assert.AreEqual ("102030", seen[3]);
		Assert.AreEqual ("1020304050", seen[5]);
	}

	[TestMethod]
	public void EveryTruncation_WithAStep_StillEndsAtTheFullLength ()
	{
		var lengths = new List<int> ();

		Hostile.EveryTruncation (Sample, data => lengths.Add (data.Length), step: 2);

		CollectionAssert.AreEqual (SteppedLengths, lengths);
	}

	[TestMethod]
	public void EveryTruncation_ReaderThrows_FailsWithTheLengthAndException ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (() => Hostile.EveryTruncation (Sample, data => {
			if (data.Length == 3)
				throw new InvalidDataException ("past the end");
		}));

		Assert.Contains ("1 of 6", error.Message);
		Assert.Contains ("length 3", error.Message);
		Assert.Contains ("InvalidDataException", error.Message);
		Assert.Contains ("past the end", error.Message);
	}

	[TestMethod]
	public void EveryTruncation_AnAssertInTheReader_IsReportedWithTheLength ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Hostile.EveryTruncation (Sample, data => Assert.IsTrue (data.Length < 4, "too long")));

		Assert.Contains ("2 of 6", error.Message);
		Assert.Contains ("length 4", error.Message);
	}

	[TestMethod]
	public void EveryTruncation_ManyFailures_ReportsTheFirstFew ()
	{
		var big = new byte[100];

		var error = Assert.ThrowsExactly<AssertFailedException> (
			() => Hostile.EveryTruncation (big, _ => throw new InvalidOperationException ("always")));

		Assert.Contains ("101 of 101", error.Message);
		Assert.Contains ("length 0", error.Message);
		Assert.DoesNotContain ("length 99", error.Message);
	}

	[TestMethod]
	public void ByteFlips_DefaultMutations_ChangeOneByteAtATimeAndRestoreIt ()
	{
		var seen = new List<string> ();

		var cases = Hostile.ByteFlips (Sample, data => seen.Add (Convert.ToHexString (data)));

		// 0x00, 0xFF, XOR 0x01 and XOR 0x80 at each of the five offsets.
		Assert.AreEqual (20, cases);
		Assert.AreEqual ("0020304050", seen[0]);
		Assert.AreEqual ("FF20304050", seen[1]);
		Assert.AreEqual ("1120304050", seen[2]);
		Assert.AreEqual ("9020304050", seen[3]);
		Assert.AreEqual ("10203040D0", seen[19]);
		Assert.AreEqual ("1020304050", Convert.ToHexString (Sample));
	}

	[TestMethod]
	public void ByteFlips_AValueEqualToTheByte_IsSkipped ()
	{
		var seen = new List<string> ();

		Hostile.ByteFlips ([0x00, 0x01], data => seen.Add (Convert.ToHexString (data)), values: [0x00, 0x01]);

		CollectionAssert.AreEqual (SkippedEqual, seen);
	}

	[TestMethod]
	public void ByteFlips_ARange_TouchesOnlyThoseOffsets ()
	{
		var offsets = new HashSet<int> ();

		Hostile.ByteFlips (Sample, data => {
			for (var i = 0; i < data.Length; i++) {
				if (data[i] != Sample[i])
					offsets.Add (i);
			}
		}, start: 1, length: 2);

		CollectionAssert.AreEquivalent (MiddleOffsets, offsets.ToArray ());
	}

	[TestMethod]
	public void ByteFlips_ReaderThrows_FailsWithTheOffsetAndValue ()
	{
		var error = Assert.ThrowsExactly<AssertFailedException> (() => Hostile.ByteFlips (Sample, data => {
			if (data[2] == 0xFF)
				throw new ArgumentOutOfRangeException (nameof (data), "bad size");
		}));

		Assert.Contains ("offset 2", error.Message);
		Assert.Contains ("0xFF", error.Message);
		Assert.Contains ("ArgumentOutOfRangeException", error.Message);
	}

	[TestMethod]
	public void ByteFlips_EmptyInput_RunsNothing ()
	{
		var calls = 0;

		var cases = Hostile.ByteFlips ([], _ => calls++);

		Assert.AreEqual (0, cases);
		Assert.AreEqual (0, calls);
	}
}
