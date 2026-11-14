// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// Shared APE suffix builders used by Monkey's Audio, WavPack, and Musepack tests.
/// </summary>
internal static class ApeTagTestData
{
	public static byte[] CreateAudioWithApeThenId3v1 (byte[] audio, string title)
	{
		var ape = new ApeTag { Title = title }.RenderWithOptions (includeHeader: true).ToArray ();
		var id3 = new byte[128];
		"TAG"u8.CopyTo (id3);
		"KeepMe"u8.CopyTo (id3.AsSpan (3));

		var data = new byte[audio.Length + ape.Length + id3.Length];
		audio.CopyTo (data, 0);
		ape.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + ape.Length);
		return data;
	}

	public static int CountSequence (ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
	{
		var count = 0;
		for (var i = 0; i <= data.Length - needle.Length; i++) {
			if (data.Slice (i, needle.Length).SequenceEqual (needle))
				count++;
		}

		return count;
	}
}
