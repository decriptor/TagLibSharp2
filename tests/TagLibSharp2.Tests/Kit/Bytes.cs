// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests.Kit;

/// <summary>Byte-sequence counts shared by the tests.</summary>
public static class Bytes
{
	/// <summary>How many times <paramref name="needle"/> occurs in <paramref name="data"/>, including overlaps.</summary>
	public static int Count (byte[] data, ReadOnlySpan<byte> needle)
	{
		if (needle.Length == 0 || needle.Length > data.Length)
			return 0;
		var count = 0;
		for (var i = 0; i + needle.Length <= data.Length; i++) {
			if (data.AsSpan (i, needle.Length).SequenceEqual (needle))
				count++;
		}

		return count;
	}
}
