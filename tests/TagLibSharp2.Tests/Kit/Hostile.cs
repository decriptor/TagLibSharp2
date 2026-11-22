// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

namespace TagLibSharp2.Tests.Kit;

/// <summary>
/// Truncation and byte-flip matrices over a valid input: nothing a reader does with them may throw.
/// </summary>
/// <remarks>
/// Malformed input returns a failure result; it never throws out of Read, Parse, Detect or Save.
/// Each helper runs every case, then fails once with the count and the first few offsets, so one
/// run shows the whole shape of a bug. An assertion inside <c>read</c> is reported the same way.
/// A stack overflow cannot be caught in process: use <see cref="NestingDepthTests"/> for depth.
/// </remarks>
public static class Hostile
{
	/// <summary>How many failing cases a failure message lists.</summary>
	public const int MaxReported = 8;

	static readonly byte[] DefaultValues = [0x00, 0xFF];
	static readonly byte[] DefaultXors = [0x01, 0x80];

	/// <summary>
	/// Passes every prefix of <paramref name="valid"/>, from empty to the whole input, to <paramref name="read"/>.
	/// </summary>
	/// <param name="valid">A well-formed input.</param>
	/// <param name="read">Reads (and may save) one input; it receives a new array of the exact length.</param>
	/// <param name="step">Cut every <paramref name="step"/> bytes; the full length always runs.</param>
	/// <returns>The number of inputs run.</returns>
	public static int EveryTruncation (byte[] valid, Action<byte[]> read, int step = 1)
	{
		ArgumentNullException.ThrowIfNull (valid);
		ArgumentNullException.ThrowIfNull (read);
		ArgumentOutOfRangeException.ThrowIfLessThan (step, 1);

		var failures = new Failures ("EveryTruncation");
		for (var length = 0; ; length = Math.Min (length + step, valid.Length)) {
			failures.Run ($"length {length}", () => read (valid.AsSpan (0, length).ToArray ()));
			if (length == valid.Length)
				break;
		}

		failures.ThrowIfAny ();
		return failures.Cases;
	}

	/// <summary>
	/// Changes one byte of <paramref name="valid"/> at a time and passes the result to <paramref name="read"/>.
	/// </summary>
	/// <param name="valid">A well-formed input. It is restored after each case.</param>
	/// <param name="read">Reads (and may save) one input. Do not keep the array past the call.</param>
	/// <param name="values">
	/// Values written at each offset. When null, each byte is set to 0x00 and 0xFF and XORed with 0x01 and 0x80:
	/// zero and all-ones sizes, off-by-one lengths and flipped VINT or sign bits. A value equal to the byte is skipped.
	/// </param>
	/// <param name="start">First offset to change.</param>
	/// <param name="length">Number of offsets to change; -1 for the rest of the input.</param>
	/// <returns>The number of inputs run.</returns>
	public static int ByteFlips (byte[] valid, Action<byte[]> read, byte[]? values = null, int start = 0, int length = -1)
	{
		ArgumentNullException.ThrowIfNull (valid);
		ArgumentNullException.ThrowIfNull (read);
		var end = length < 0 ? valid.Length : Math.Min (valid.Length, start + length);

		var failures = new Failures ("ByteFlips");
		var data = (byte[])valid.Clone ();
		for (var offset = start; offset < end; offset++) {
			var original = data[offset];
			foreach (var mutated in Mutations (original, values)) {
				data[offset] = mutated;
				failures.Run ($"offset {offset} = 0x{mutated:X2} (was 0x{original:X2})", () => read (data));
				Array.Copy (valid, data, valid.Length);
			}
		}

		failures.ThrowIfAny ();
		return failures.Cases;
	}

	static IEnumerable<byte> Mutations (byte original, byte[]? values)
	{
		foreach (var value in values ?? DefaultValues) {
			if (value != original)
				yield return value;
		}

		if (values is not null)
			yield break;
		foreach (var xor in DefaultXors)
			yield return (byte)(original ^ xor);
	}

	sealed class Failures (string name)
	{
		readonly StringBuilder _report = new ();
		int _failed;

		public int Cases { get; private set; }

		public void Run (string label, Action action)
		{
			Cases++;
			try {
				action ();
			} catch (Exception ex) when (ex is not OutOfMemoryException) {
				_failed++;
				if (_failed <= MaxReported)
					_report.Append ("\n  ").Append (label).Append (": ").Append (Describe (ex));
			}
		}

		public void ThrowIfAny ()
		{
			if (_failed == 0)
				return;
			var more = _failed > MaxReported ? $"\n  ... and {_failed - MaxReported} more" : "";
			Assert.Fail ($"{name}: {_failed} of {Cases} inputs threw.{_report}{more}");
		}

		static string Describe (Exception ex)
		{
			var frames = (ex.StackTrace ?? "")
				.Split ('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where (line => !line.Contains ("TagLibSharp2.Tests.Kit.Hostile", StringComparison.Ordinal))
				.Take (3);
			return $"{ex.GetType ().Name}: {ex.Message}" + string.Concat (frames.Select (f => "\n      " + f));
		}
	}
}
